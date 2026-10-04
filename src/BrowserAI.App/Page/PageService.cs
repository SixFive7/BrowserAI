// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using BrowserAI.App.Interop;
using BrowserAI.Coordination;
using BrowserAI.Registration;
using BrowserAI.Updates;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace BrowserAI.App.Page;

/// <summary>What the page asks of the process that hosts it.</summary>
/// <remarks>
/// <b>The one seam between the page and the desktop.</b> Every call here either
/// opens something on the person's screen or runs on the coordinator's own
/// thread, so the suite replaces all of it and nothing a test does reaches the
/// screen.
/// </remarks>
internal interface IPageHost
{
    /// <summary>Opens a folder in Explorer.</summary>
    /// <param name="directory">The folder.</param>
    void OpenFolder(string directory);

    /// <summary>Opens Playwright's trace viewer on one trace.</summary>
    /// <param name="trace">The trace file.</param>
    /// <returns>A sentence when it could not be opened, or <see langword="null"/>.</returns>
    string? OpenTrace(string trace);

    /// <summary>Asks the person for a folder through Windows' own picker, Q311.</summary>
    /// <param name="prompt">What the picker says the folder is for.</param>
    /// <returns>What the person chose, or that they chose nothing.</returns>
    Task<FolderPick> PickFolderAsync(string prompt);

    /// <summary>Runs what was queued for the coordinator's own thread. Called by the coordinator's loop.</summary>
    void RunQueuedWork();
}

/// <summary>What the page asks of the update machinery.</summary>
internal interface IPageUpdates
{
    /// <summary>Why no check can run, or <see langword="null"/> when one can.</summary>
    UpdateStage? Unavailable { get; }

    /// <summary>
    /// The release list a local feed is missing, or <see langword="null"/> when the
    /// feed is not a folder or has one.
    /// </summary>
    /// <returns>The path that is not there.</returns>
    string? MissingReleaseList();

    /// <summary>Asks the feed.</summary>
    /// <param name="cancellationToken">Ends the wait.</param>
    /// <returns>What it offers, or <see langword="null"/> for nothing.</returns>
    Task<UpdateCandidate?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>The package a server has already downloaded, or <see langword="null"/>.</summary>
    /// <returns>The candidate.</returns>
    UpdateCandidate? Staged();

    /// <summary>Downloads the candidate when it is not on disk, and hands it to the updater with a restart.</summary>
    /// <param name="candidate">What to install.</param>
    /// <param name="cancellationToken">Ends the download.</param>
    /// <returns>The work. Once it returns, this process has to exit for the updater to go on.</returns>
    Task InstallAsync(UpdateCandidate candidate, CancellationToken cancellationToken);
}

/// <summary>What the page asks of the running servers.</summary>
internal interface IPageSessions
{
    /// <summary>Reads every server running from this install, and what each holds.</summary>
    /// <param name="cancellationToken">Ends the read.</param>
    /// <returns>The snapshot.</returns>
    Task<SessionsSnapshot> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Asks one server to stop, through its pipe.</summary>
    /// <param name="server">The server.</param>
    /// <param name="cancellationToken">Ends the wait.</param>
    /// <returns><see langword="null"/> when it acknowledged, and otherwise why not, as one sentence.</returns>
    Task<string?> CloseAsync(ServerEntry server, CancellationToken cancellationToken);
}

/// <summary>
/// The page the coordinator serves: its listener, its tabs, what it shows and
/// what its buttons do.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q315 a, the maintainer's words verbatim: <i>"Q315 a"</i></b>: a tab in
/// whatever browser the person uses replaces the configuration window. This type
/// is the whole of the page's half; the coordinator holds one, hands out its
/// address through the pipe, and asks it whether it may stop.
/// </para>
/// <para>
/// <b>A listener exists only while it is wanted.</b> The first address handed out
/// starts one, on a fresh port with a fresh token, and <see cref="TryStop"/> ends it
/// once no tab is connected and a minute has passed (Q336 a). A later hand-out
/// starts a new one, so an address outlives its listener only in a tab nobody is
/// using.
/// </para>
/// <para>
/// <b>Two threads call in and one lock guards what they share.</b> The pipe's
/// thread hands out addresses; Kestrel's threads serve requests and run actions;
/// the coordinator's thread decides whether to stop. Rendering reads a snapshot
/// taken under the lock and never the fields themselves.
/// </para>
/// </remarks>
internal sealed partial class PageService : IPageRoutes, ICoordinatorPage, IAsyncDisposable, IDisposable
{
    private readonly Lock _gate = new();
    private readonly PageFacts _facts;
    private readonly IPageUpdates _updates;
    private readonly IPageSessions _sessions;
    private readonly IPageRegistration _registrar;
    private readonly IPageHost _host;
    private readonly Action _wake;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _linger;
    private readonly ILogger _logger;
    private readonly Dictionary<PageKind, PageNote?> _notes = new() { [PageKind.Status] = null, [PageKind.Sessions] = null };

    private Served? _current;
    private readonly Occasion _firstOccasion;
    private (Served Listener, int Tab)? _occasionTab;
    private bool _stopping;
    private bool _exitRequested;
    private UpdateView _update;
    private UpdateCandidate? _offered;
    private UpdateCandidate? _staged;
    private SessionsSnapshot _snapshot = SessionsSnapshot.Empty;
    private CancellationTokenSource? _check;
    private RegistrationSnapshot? _registration;
    private string? _registering;
    private bool _readingRegistration;

    /// <summary>
    /// The coordinator's hold on the session host, or <see langword="null"/> where
    /// there is none: an install stops the host through it (Q366 b).
    /// </summary>
    internal ISessionHostHold? SessionHost { get; init; }

    /// <summary>A page for one coordinator.</summary>
    /// <param name="facts">What does not change.</param>
    /// <param name="firstOccasion">Why the coordinator was started, which the first tab says.</param>
    /// <param name="updates">The update machinery.</param>
    /// <param name="sessions">The running servers.</param>
    /// <param name="registrar">Every client's registration, read and changed.</param>
    /// <param name="host">The desktop.</param>
    /// <param name="wake">Wakes the coordinator's thread; called whenever something it decides on changed.</param>
    /// <param name="clock">The clock the linger runs on.</param>
    /// <param name="linger">How long to wait after the last tab leaves: <see cref="PageTabs.ProductLinger"/> in the product.</param>
    /// <param name="logger">Where the page reports.</param>
    public PageService(
        PageFacts facts,
        Occasion firstOccasion,
        IPageUpdates updates,
        IPageSessions sessions,
        IPageRegistration registrar,
        IPageHost host,
        Action wake,
        TimeProvider clock,
        TimeSpan linger,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(registrar);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(wake);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _facts = facts;
        _firstOccasion = firstOccasion;
        _updates = updates;
        _sessions = sessions;
        _registrar = registrar;
        _host = host;
        _wake = wake;
        _clock = clock;
        _linger = linger;
        _logger = logger;
        _update = new UpdateView(updates.Unavailable ?? UpdateStage.NotChecked);
        _staged = updates.Staged();
    }

    /// <summary>Whether a listener is up.</summary>
    public bool IsServing
    {
        get
        {
            lock (_gate)
            {
                return _current is not null;
            }
        }
    }

    /// <summary>Whether an action asked the coordinator to exit: an install has handed over to the updater.</summary>
    /// <returns>Whether it did.</returns>
    public bool IsExitRequested()
    {
        lock (_gate)
        {
            return _exitRequested;
        }
    }

    /// <summary>The tabs of the listener that is up, or <see langword="null"/>.</summary>
    internal PageTabs? Tabs
    {
        get
        {
            lock (_gate)
            {
                return _current?.Tabs;
            }
        }
    }

    /// <summary>The gate of the listener that is up, or <see langword="null"/>.</summary>
    internal PageGate? Gate
    {
        get
        {
            lock (_gate)
            {
                return _current?.Listener.Gate;
            }
        }
    }

    /// <summary>
    /// Hands out the address of a new tab: starts a listener when none is up, takes
    /// the next tab number, and composes the address.
    /// </summary>
    /// <param name="kind">Which page the tab opens on.</param>
    /// <returns>The address, or <see langword="null"/> once the coordinator is stopping.</returns>
    public string? HandOut(PageKind kind)
    {
        lock (_gate)
        {
            if (_stopping)
            {
                return null;
            }

            if (_current is null || _current.Tabs.Closing)
            {
                // Started here, under the lock and synchronously: the pipe's thread
                // is the caller, it has a bound of its own, and a second hand-out
                // must find this listener and not start another.
                _current = new Served(this, _clock, _linger, _wake, _logger);
            }

            var tab = _current.Tabs.HandOut()!.Value;

            _occasionTab ??= (_current, tab);

            var route = kind is PageKind.Sessions ? "sessions" : string.Empty;

            return string.Create(CultureInfo.InvariantCulture, $"{_current.Listener.Gate.Root}{route}?tab={tab}");
        }
    }

    /// <summary>
    /// Ends the listener once no tab is connected and the linger has run out.
    /// </summary>
    /// <param name="final">
    /// Whether the coordinator stops with it, after which nothing more is handed
    /// out; <see langword="false"/> while an update it waits to apply keeps it
    /// running.
    /// </param>
    /// <returns>Whether no listener is up now.</returns>
    public bool TryStop(bool final)
    {
        Served? ending;

        lock (_gate)
        {
            if (_current is { } current && !current.Tabs.TryClose())
            {
                return false;
            }

            ending = _current;
            _current = null;
            _stopping |= final;
        }

        if (ending is not null)
        {
            ending.Dispose();
            PageServiceLog.Stopped(_logger, ending.Listener.Gate.Port);
        }

        return true;
    }

    /// <summary>
    /// Says one last thing to every tab and ends their streams, and from then on
    /// hands out nothing: the coordinator is going.
    /// </summary>
    /// <param name="sentence">What to say.</param>
    public void Tell(string sentence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sentence);

        lock (_gate)
        {
            _stopping = true;
        }

        Tabs?.CloseAll(Json(writer => writer.WriteString("sentence", sentence)));
    }

    /// <inheritdoc />
    public void RunQueuedWork() => _host.RunQueuedWork();

    /// <inheritdoc />
    public void Staged(UpdateCandidate? pending)
    {
        var staged = pending;
        bool changed;

        lock (_gate)
        {
            changed = !string.Equals(staged?.Version, _staged?.Version, StringComparison.Ordinal);
            _staged = staged;
        }

        if (changed)
        {
            Push();
        }
    }

    /// <summary>Everything the page would show now.</summary>
    /// <returns>The view.</returns>
    public PageView View(PageKind kind)
    {
        lock (_gate)
        {
            return new PageView(_facts, _update, _staged?.Version, _snapshot, _notes[kind], _registration, _registering);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ServeAsync(HttpContext context, string route, string query)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(query);

        var get = string.Equals(context.Request.Method, "GET", StringComparison.Ordinal);

        switch (route)
        {
            case "" when get:
                // What a server has staged is read again for every page load, so a
                // tab opened between two of the coordinator's passes is not behind.
                // The registration is read again too, the way the window read it each
                // time it opened, and arrives through the stream when it is in.
                Staged(_updates.Staged());
                StartRegistrationRead();
                await WriteAsync(context, "text/html; charset=utf-8", Page(PageKind.Status, query)).ConfigureAwait(false);
                return true;

            case "sessions" when get:
                await RefreshSessionsAsync(context.RequestAborted).ConfigureAwait(false);
                await WriteAsync(context, "text/html; charset=utf-8", Page(PageKind.Sessions, query)).ConfigureAwait(false);
                return true;

            case "page.css" when get:
                await WriteAsync(context, "text/css; charset=utf-8", PageAssets.Css).ConfigureAwait(false);
                return true;

            case "page.js" when get:
                await WriteAsync(context, "text/javascript; charset=utf-8", PageAssets.Script).ConfigureAwait(false);
                return true;

            case "events" when get:
                await StreamAsync(context, query).ConfigureAwait(false);
                return true;

            case "action" when !get:
                return await ActAsync(context).ConfigureAwait(false);

            default:
                return false;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Served? ending;
        CancellationTokenSource? check;

        lock (_gate)
        {
            ending = _current;
            check = _check;
            _current = null;
            _check = null;
            _stopping = true;
        }

        if (check is not null)
        {
            await check.CancelAsync().ConfigureAwait(false);
            check.Dispose();
        }

        ending?.Dispose();
    }

    /// <summary>Stops the listener, waiting on the calling thread.</summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>The tab number a query carries, or zero.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The number.</returns>
    internal static int TabOf(string query) =>
        PageGate.QueryValue(query, "tab") is { } text
        && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var tab)
            ? tab
            : 0;

    private static async Task WriteAsync(HttpContext context, string contentType, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = contentType;
        context.Response.ContentLength = bytes.Length;

        await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }

    private static string Json(Action<Utf8JsonWriter> members)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            members(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private string Page(PageKind kind, string query)
    {
        var tab = TabOf(query);

        return PageContent.Document(View(kind), kind, tab, OccasionFor(tab), _clock.GetUtcNow());
    }

    private Occasion OccasionFor(int tab)
    {
        lock (_gate)
        {
            return _occasionTab is { } first && ReferenceEquals(first.Listener, _current) && first.Tab == tab
                ? _firstOccasion
                : Occasion.Ordinary;
        }
    }

    private PageEvent StateFor(TabStream stream) =>
        new(PageEvents.State, Json(writer => writer.WriteString(
            "html",
            PageContent.Fragment(View(stream.Page), stream.Page, stream.Tab, OccasionFor(stream.Tab), _clock.GetUtcNow()))));

    /// <summary>Sends every tab what it shows now.</summary>
    private void Push() => Tabs?.Broadcast(StateFor);

    private async Task StreamAsync(HttpContext context, string query)
    {
        var tabs = Tabs;
        var page = string.Equals(PageGate.QueryValue(query, "page"), "sessions", StringComparison.Ordinal) ? PageKind.Sessions : PageKind.Status;

        if (tabs?.Connect(TabOf(query), page) is not { } stream)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        try
        {
            context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream";

            await context.Response.StartAsync(context.RequestAborted).ConfigureAwait(false);

            if (!stream.Superseded)
            {
                stream.Send(StateFor(stream));
            }

            await foreach (var @event in stream.Events.ReadAllAsync(context.RequestAborted).ConfigureAwait(false))
            {
                var frame = Encoding.UTF8.GetBytes($"event: {@event.Name}\ndata: {@event.Data}\n\n");

                await context.Response.Body.WriteAsync(frame, context.RequestAborted).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
            }
        }
        finally
        {
            tabs.Disconnect(stream);
        }
    }

    private async Task<bool> ActAsync(HttpContext context)
    {
        string? action;
        JsonElement request;

        try
        {
            var body = new byte[(int)(context.Request.ContentLength ?? 0)];

            await context.Request.Body.ReadExactlyAsync(body, context.RequestAborted).ConfigureAwait(false);

            using var document = JsonDocument.Parse(body);

            request = document.RootElement.Clone();
            action = request.ValueKind is JsonValueKind.Object && request.TryGetProperty("action", out var named) && named.ValueKind is JsonValueKind.String
                ? named.GetString()
                : null;
        }
        catch (JsonException)
        {
            return false;
        }

        PageServiceLog.Action(_logger, action ?? "(none)");

        var taken = action switch
        {
            "check-updates" => StartCheck(),
            "stop-check" => StopCheck(),
            "install-update" => StartInstall(String(request, "version")),
            "open-folder" => OpenFolder(String(request, "folder")),
            "open-session" => OpenSession(String(request, "session")),
            "open-trace" => OpenTrace(String(request, "trace")),
            "refresh-sessions" => Run(RefreshSessionsAsync(CancellationToken.None)),
            "close-servers" => Run(CloseServersAsync(Strings(request, "servers"))),
            "read-registration" => StartRegistrationRead(),
            "register" or "unregister" or "register-in-project" or "unregister-from-project" or "unregister-from-a-project" =>
                StartRegistration(String(request, "client"), action),
            _ => false,
        };

        if (!taken)
        {
            return false;
        }

        context.Response.StatusCode = StatusCodes.Status204NoContent;
        return true;
    }

    private static string? String(JsonElement request, string name) =>
        request.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    private static List<string> Strings(JsonElement request, string name) =>
        request.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.Array
            ? [.. value.EnumerateArray().Where(item => item.ValueKind is JsonValueKind.String).Select(item => item.GetString()!)]
            : [];

    private static bool Run(Task work)
    {
        _ = work;
        return true;
    }

    private void Note(PageKind kind, PageNote? note)
    {
        lock (_gate)
        {
            _notes[kind] = note;
        }

        Push();
    }

    private bool StartCheck()
    {
        CancellationTokenSource cancel;

        lock (_gate)
        {
            if (_update.Stage is UpdateStage.Checking or UpdateStage.Installing || _updates.Unavailable is not null)
            {
                return true;
            }

            _check?.Cancel();
            cancel = _check = new CancellationTokenSource();
            _update = new UpdateView(UpdateStage.Checking);
            _offered = null;
        }

        Push();
        _ = Task.Run(() => AskTheFeedAsync(cancel), CancellationToken.None);
        return true;
    }

    private async Task AskTheFeedAsync(CancellationTokenSource cancel)
    {
        UpdateView result;
        UpdateCandidate? offered = null;

        try
        {
            if (_updates.MissingReleaseList() is { } missing)
            {
                result = new UpdateView(UpdateStage.NoReleaseList, Details: $"No file at {missing}");
            }
            else
            {
                // The wait is bounded by the server's own tripwire for the same call and
                // can be given up from the page; the request itself takes no token.
                var check = _updates.CheckAsync(cancel.Token);
                var gaveUp = Task.Delay(Timeout.InfiniteTimeSpan, cancel.Token);
                var outOfTime = Task.Delay(UpdateService.CrashTripwire, _clock, cancel.Token);
                var first = await Task.WhenAny(check, gaveUp, outOfTime).ConfigureAwait(false);

                if (first != check)
                {
                    result = cancel.IsCancellationRequested
                        ? new UpdateView(UpdateStage.NotChecked)
                        : new UpdateView(UpdateStage.Failed, Details: $"The release feed did not answer within {UpdateService.CrashTripwire.TotalMinutes:F0} minutes.");
                }
                else
                {
                    offered = await check.ConfigureAwait(false);
                    result = offered is null
                        ? new UpdateView(UpdateStage.UpToDate)
                        : new UpdateView(UpdateStage.Available, offered.Version, offered.IsDowngrade);
                }
            }
        }
#pragma warning disable CA1031 // A failed check is a sentence on the page, never a coordinator that stops.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            result = new UpdateView(UpdateStage.Failed, Details: failure.Message);
        }

        lock (_gate)
        {
            if (!ReferenceEquals(_check, cancel))
            {
                // Given up, and perhaps a newer check started: this answer is nobody's.
                PageServiceLog.CheckDropped(_logger, result.Stage);
                return;
            }

            _check = null;
            _update = result;
            _offered = offered;
        }

        cancel.Dispose();
        Push();
    }

    private bool StopCheck()
    {
        lock (_gate)
        {
            if (_update.Stage is not UpdateStage.Checking || _check is null)
            {
                return true;
            }

            _check.Cancel();
            _check = null;
            _update = new UpdateView(UpdateStage.NotChecked);
        }

        Push();
        return true;
    }

    private bool StartInstall(string? version)
    {
        UpdateCandidate? candidate;

        lock (_gate)
        {
            if (_update.Stage is UpdateStage.Installing)
            {
                return true;
            }

            candidate = OnOffer(version);

            if (candidate is null)
            {
                _notes[PageKind.Status] = new PageNote("That version is no longer on offer, so nothing was installed. Check for updates again.");
            }
            else
            {
                _update = new UpdateView(UpdateStage.Installing, candidate.Version);
                _notes[PageKind.Status] = null;
            }
        }

        Push();

        if (candidate is not null)
        {
            _ = Task.Run(() => InstallAsync(candidate), CancellationToken.None);
        }

        return true;
    }

    /// <summary>The staged or checked candidate of one version, or <see langword="null"/>. Called under the lock.</summary>
    /// <param name="version">The version the button named.</param>
    /// <returns>The candidate.</returns>
    private UpdateCandidate? OnOffer(string? version)
    {
        if (_staged is { } staged && string.Equals(staged.Version, version, StringComparison.Ordinal))
        {
            return staged;
        }

        return _offered is { } offered && string.Equals(offered.Version, version, StringComparison.Ordinal) ? offered : null;
    }

    /// <summary>Installs one candidate: every server asked to stop first, then the hand-over.</summary>
    /// <remarks>
    /// <para>
    /// <b>Each running server is asked to stop through its pipe before the
    /// updater is started</b>, so a call it is answering gets Q286 b's sentence and
    /// its browsers close themselves. The updater's own kill pass ends whatever is
    /// left once this process has gone, which is what Velopack does on every apply.
    /// </para>
    /// <para>
    /// <b>The session host is this process's own (Q366 b)</b>, and it is stopped the
    /// way the coordinator's own apply stops it: after the servers, through
    /// <see cref="ISessionHostHold.StopForUpdate"/>, which has it close every
    /// browser, waits for it to end and starts no other. Without that, the hand-over
    /// ends this process and its job takes the host and every browser down
    /// mid-close. An install that does not happen lets a host start again.
    /// <i>Added 2026-10-04, the day after the session host arrived.</i>
    /// </para>
    /// </remarks>
    /// <param name="candidate">What to install.</param>
    private async Task InstallAsync(UpdateCandidate candidate)
    {
        var hold = SessionHost;
        var holding = false;

        try
        {
            var running = await _sessions.ReadAsync(CancellationToken.None).ConfigureAwait(false);

            foreach (var server in running.Servers.Where(server => !server.IsHost))
            {
                _ = await _sessions.CloseAsync(server, CancellationToken.None).ConfigureAwait(false);
            }

            if (hold is not null)
            {
                holding = true;
                _ = hold.StopForUpdate();
            }

            // Bounded by the server's own tripwire for the same download, the bound the
            // window's install ran under.
            using var bounded = new CancellationTokenSource(UpdateService.CrashTripwire, _clock);

            await _updates.InstallAsync(candidate, bounded.Token).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // An install that threw is a sentence on the page; the coordinator keeps running.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            if (holding)
            {
                hold!.Reopen();
            }

            lock (_gate)
            {
                _update = new UpdateView(UpdateStage.InstallFailed, candidate.Version, Details: failure.Message);
            }

            Push();
            return;
        }

        Tell($"BrowserAI is installing {candidate.Version}. This tab has stopped, and a new one opens when the install is done.");

        lock (_gate)
        {
            _exitRequested = true;
            _stopping = true;
        }

        _wake();
    }

    private bool OpenFolder(string? folder)
    {
        var directory = folder switch
        {
            "install" => _facts.InstallRoot,
            "data" => _facts.DataRoot,
            "logs" => _facts.LogDirectory,
            _ => null,
        };

        if (directory is not { Length: > 0 })
        {
            return false;
        }

        _ = Directory.CreateDirectory(directory);
        _host.OpenFolder(directory);
        return true;
    }

    private bool OpenSession(string? id)
    {
        SessionEntry? session;

        lock (_gate)
        {
            session = id is null ? null : _snapshot.SessionById(id);
        }

        if (session is null)
        {
            return false;
        }

        _host.OpenFolder(session.Directory);
        return true;
    }

    private bool OpenTrace(string? id)
    {
        TraceEntry? trace;

        lock (_gate)
        {
            trace = id is null ? null : _snapshot.TraceById(id);
        }

        if (trace is null)
        {
            return false;
        }

        if (_host.OpenTrace(trace.Path) is { } refused)
        {
            Note(PageKind.Sessions, new PageNote("The trace viewer did not open.", refused));
        }

        return true;
    }

    /// <summary>Reads every client's registration again, unless a read is already running.</summary>
    /// <returns>Always <see langword="true"/>: the request was taken.</returns>
    private bool StartRegistrationRead()
    {
        lock (_gate)
        {
            if (_readingRegistration)
            {
                return true;
            }

            _readingRegistration = true;
        }

        _ = Task.Run(ReadRegistrationAsync, CancellationToken.None);
        return true;
    }

    private async Task ReadRegistrationAsync()
    {
        RegistrationSnapshot read;

        try
        {
            var state = await _registrar.ReadAsync(CancellationToken.None).ConfigureAwait(false);

            read = new RegistrationSnapshot(_clock.GetUtcNow(), state);
        }
#pragma warning disable CA1031 // A read that failed is a sentence on the page, never a coordinator that stops.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            read = new RegistrationSnapshot(_clock.GetUtcNow(), null, failure.Message);
        }

        lock (_gate)
        {
            _registration = read;
            _readingRegistration = false;
        }

        Push();
    }

    /// <summary>
    /// Starts one registration action for one client, when the page offered it for
    /// that client in the state it last read.
    /// </summary>
    /// <remarks>
    /// <b>The client is named by its key and nothing else, and the folder never comes
    /// from the page</b>: a project folder is the one the client's own read found, or
    /// one a person picks in Windows' picker, which the coordinator opens (Q311). An
    /// action the page did not offer for that client is refused like an unknown one,
    /// and one action runs at a time; the page offers no button while it does.
    /// </remarks>
    /// <param name="key">The client's key.</param>
    /// <param name="action">The action.</param>
    /// <returns>Whether the request was taken.</returns>
    private bool StartRegistration(string? key, string action)
    {
        AppState state;
        ClientState client;

        lock (_gate)
        {
            if (_registration?.State is not { } read
                || read.Clients.FirstOrDefault(each => string.Equals(each.Client.Key, key, StringComparison.Ordinal)) is not { } found
                || !Offered(found, action))
            {
                return false;
            }

            if (_registering is not null)
            {
                return true;
            }

            state = read;
            client = found;
            _registering = Working(found, action);
        }

        Push();
        _ = Task.Run(() => RegisterAsync(state, client, action), CancellationToken.None);
        return true;
    }

    private static bool Offered(ClientState client, string action) => action switch
    {
        "register" => client.MayRegister,
        "unregister" => client.MayUnregister,
        "register-in-project" => client.MayRegisterInProject,
        "unregister-from-project" => client.MayUnregisterFromProject,
        "unregister-from-a-project" => client.MayUnregisterFromAProject,
        _ => false,
    };

    private static string Working(ClientState client, string action)
    {
        var name = client.Client.DisplayName;

        return action switch
        {
            "register" => $"Registering BrowserAI with {name}.",
            "unregister" => $"Removing BrowserAI from {name}.",
            "unregister-from-project" => $"Removing BrowserAI from the project at {client.ProjectDirectory} for {name}.",
            _ => $"Windows' folder picker is open for {name}. If it is not in front, it is behind this browser window.",
        };
    }

    private async Task RegisterAsync(AppState state, ClientState client, string action)
    {
        var who = client.Client;
        PageNote? note;

        try
        {
            note = action switch
            {
                "register" => RegistrationNotes.For(await _registrar.RegisterAsync(who, CancellationToken.None).ConfigureAwait(false), who, registering: true),
                "unregister" => RegistrationNotes.For(await _registrar.UnregisterAsync(who, CancellationToken.None).ConfigureAwait(false), who, registering: false),
                "unregister-from-project" => RegistrationNotes.ForProject(
                    await _registrar.UnregisterFromProjectAsync(who, client.ProjectDirectory!, CancellationToken.None).ConfigureAwait(false), who, registering: false, null),
                "register-in-project" => await InProjectAsync(state, who, register: true).ConfigureAwait(false),
                _ => await InProjectAsync(state, who, register: false).ConfigureAwait(false),
            };
        }
#pragma warning disable CA1031 // A registration that threw is a sentence on the page; the coordinator keeps running.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            note = new PageNote($"BrowserAI could not finish that request for {who.DisplayName}.", failure.Message);
        }

        lock (_gate)
        {
            _registering = null;

            // A picker closed without a choice changes nothing and says nothing.
            if (note is not null)
            {
                _notes[PageKind.Status] = note;
            }
        }

        Push();
        _ = StartRegistrationRead();
    }

    private async Task<PageNote?> InProjectAsync(AppState state, RegistrationClient who, bool register)
    {
        var picked = await _host.PickFolderAsync(register
            ? $"Choose the folder to register BrowserAI in for {who.DisplayName}. A {who.ProjectFileName} is written under it, to be committed with the project."
            : $"Choose the project folder to remove BrowserAI from for {who.DisplayName}. Only an entry this install wrote in its {who.ProjectFileName} is removed.").ConfigureAwait(false);

        if (picked.Outcome is FolderPickOutcome.Failed)
        {
            return new PageNote("The folder that was chosen could not be used, so nothing was changed.", picked.Reason);
        }

        if (picked.Outcome is not FolderPickOutcome.Picked || picked.Path is not { Length: > 0 } folder)
        {
            return null;
        }

        if (!register)
        {
            return RegistrationNotes.ForProject(
                await _registrar.UnregisterFromProjectAsync(who, folder, CancellationToken.None).ConfigureAwait(false), who, registering: false, null);
        }

        // The client decides what a project file says (Q294 b): Claude Code's entry is
        // the portable spelling, Codex's the bare server name found on the PATH. Codex's
        // sentence names what that name finds, which is RegisterAI's answer about the
        // entry it just wrote, and it is where Q314 b's restart sentence is.
        var server = state.ServerCommand ?? string.Empty;
        var project = who.ProjectCommandFor(server, state.InstallRoot);
        var report = await _registrar.RegisterInProjectAsync(who, folder, project.Command, CancellationToken.None).ConfigureAwait(false);
        var note = project.Note ?? (server.Length > 0 ? who.ProjectNoteAfter(server, report.ResolvesTo) : null);

        return RegistrationNotes.ForProject(report, who, registering: true, note);
    }

    private async Task RefreshSessionsAsync(CancellationToken cancellationToken)
    {
        SessionsSnapshot read;

        try
        {
            read = await _sessions.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A read that failed is a sentence on the page.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            Note(PageKind.Sessions, new PageNote("BrowserAI could not read its running servers.", failure.Message));
            return;
        }

        lock (_gate)
        {
            _snapshot = read;
        }

        Push();
    }

    private async Task CloseServersAsync(IReadOnlyList<string> ids)
    {
        List<ServerEntry> chosen;

        lock (_gate)
        {
            // The session host is not a server a person closes from here (Q366 b):
            // closing it ends every client's sessions, and the page offers no box for it.
            chosen = [.. ids.Select(_snapshot.ServerById).OfType<ServerEntry>().Where(server => !server.IsHost)];
        }

        if (chosen.Count is 0)
        {
            Note(PageKind.Sessions, new PageNote("No server was selected, so nothing was closed."));
            return;
        }

        var refused = new List<string>();

        foreach (var server in chosen)
        {
            string? why;

            try
            {
                why = await _sessions.CloseAsync(server, CancellationToken.None).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // One server that could not be asked is a line in the note, and the others are still asked.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                why = failure.Message;
            }

            if (why is not null)
            {
                refused.Add(string.Create(CultureInfo.InvariantCulture, $"pid {server.Description.ProcessId}: {why}"));
            }
        }

        var closed = chosen.Count - refused.Count;

        Note(
            PageKind.Sessions,
            refused.Count is 0
                ? new PageNote(closed is 1 ? "The server was asked to close." : $"{closed} servers were asked to close.")
                : new PageNote(
                    refused.Count == chosen.Count
                        ? "No server could be asked to close."
                        : $"{closed} of {chosen.Count} servers were asked to close, and the others could not be.",
                    string.Join("\n", refused)));

        await RefreshSessionsAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>One listener and its tabs, started together and stopped together.</summary>
    private sealed class Served : IDisposable
    {
        /// <summary>Starts a listener and its tabs.</summary>
        /// <param name="routes">What answers an admitted request.</param>
        /// <param name="clock">The clock the linger runs on.</param>
        /// <param name="linger">How long to wait after the last tab leaves.</param>
        /// <param name="wake">Wakes the coordinator.</param>
        /// <param name="logger">Where the listener reports.</param>
        public Served(IPageRoutes routes, TimeProvider clock, TimeSpan linger, Action wake, ILogger logger)
        {
            Tabs = new PageTabs(clock, linger, wake);

            try
            {
                Listener = PageListener.Start(routes, logger);
            }
            catch
            {
                Tabs.Dispose();
                throw;
            }
        }

        public PageListener Listener { get; }

        public PageTabs Tabs { get; }

        public void Dispose()
        {
            Listener.Dispose();
            Tabs.Dispose();
        }
    }

    /// <summary>The page's own records.</summary>
    private static partial class PageServiceLog
    {
        [LoggerMessage(EventId = 7011, Level = LogLevel.Information, Message = "The page listener on port {Port} stopped: no tab was left and the linger ran out.")]
        public static partial void Stopped(ILogger logger, int port);

        [LoggerMessage(EventId = 7012, Level = LogLevel.Information, Message = "The page asked for '{Action}'.")]
        public static partial void Action(ILogger logger, string action);

        [LoggerMessage(EventId = 7013, Level = LogLevel.Information, Message = "An update check that was given up came back ({Stage}), and nobody was waiting for it.")]
        public static partial void CheckDropped(ILogger logger, UpdateStage stage);
    }
}
