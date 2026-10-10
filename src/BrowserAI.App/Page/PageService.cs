// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using BrowserAI.App.Interop;
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

/// <summary>What the page reads of the background's sessions and the clients connected to it.</summary>
/// <remarks>
/// <i>Corrected 2026-10-10 (previously "What the page asks of the running servers", with
/// a <c>CloseAsync</c> that asked one server to stop)</i>: the maintainer's 17 a took
/// the page's close away, because a relay ends with its client.
/// </remarks>
internal interface IPageSessions
{
    /// <summary>Reads the background and every client connected to it, and the sessions it holds.</summary>
    /// <param name="cancellationToken">Ends the read.</param>
    /// <returns>The snapshot.</returns>
    Task<SessionsSnapshot> ReadAsync(CancellationToken cancellationToken);
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
internal sealed partial class PageService : IPageRoutes, IAsyncDisposable, IDisposable
{
    private readonly Lock _gate = new();
    private readonly PageFacts _facts;
    private readonly IPageSessions _sessions;
    private readonly IPageRegistration _registrar;
    private readonly IPageHost _host;
    private readonly Action _wake;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _linger;
    private readonly ILogger _logger;
    private readonly Dictionary<PageKind, PageNote?> _notes = new()
    {
        [PageKind.Status] = null,
        [PageKind.Sessions] = null,
        [PageKind.Update] = null,
        [PageKind.Changelog] = null,
    };

    private Served? _current;
    private readonly Occasion _firstOccasion;
    private (Served Listener, int Tab)? _occasionTab;
    private bool _stopping;
    private SessionsSnapshot _snapshot = SessionsSnapshot.Empty;
    private RegistrationSnapshot? _registration;
    private string? _registering;
    private bool _readingRegistration;
    private bool _installingNow;
    private string? _holdsSignature;

    /// <summary>What holds a downloaded update, and the person's install-now: the background's update core.</summary>
    /// <remarks>
    /// <i>Required since 2026-10-10</i> (#73 of the texts review): the one background
    /// always has its update core, so a page with nothing reporting what holds an update
    /// does not exist, and a snapshot the page does not have is a read that failed.
    /// </remarks>
    internal required IUpdateHolds Holds { get; init; }

    /// <summary>The installed version's section of the changelog shipped in the build, or <see langword="null"/>.</summary>
    internal ChangelogSection? Changelog { get; init; }

    /// <summary>
    /// Whether the install is broken, which every page says first while it stands, or
    /// <see langword="null"/> where nothing reports it (10 b, 2026-10-10).
    /// </summary>
    internal BrokenInstallNotice? Install { get; init; }

    /// <summary>A page for one coordinator.</summary>
    /// <param name="facts">What does not change.</param>
    /// <param name="firstOccasion">Why the coordinator was started, which the first tab says.</param>
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
        IPageSessions sessions,
        IPageRegistration registrar,
        IPageHost host,
        Action wake,
        TimeProvider clock,
        TimeSpan linger,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(registrar);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(wake);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _facts = facts;
        _firstOccasion = firstOccasion;
        _sessions = sessions;
        _registrar = registrar;
        _host = host;
        _wake = wake;
        _clock = clock;
        _linger = linger;
        _logger = logger;
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
                _current = new Served(this, _clock, _linger, _wake, _logger)
                {
                    // While a listener is up, what holds the update is read once a
                    // second, and every tab is sent a new state when it has changed.
                    HoldsWatch = _clock.CreateTimer(_ => WatchHolds(), null, HoldsWatchPeriod, HoldsWatchPeriod),
                };
            }

            var tab = _current.Tabs.HandOut()!.Value;

            _occasionTab ??= (_current, tab);

            var route = PageNames.RouteOf(kind);

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

    /// <summary>
    /// The install's state changed: a session was refused as a broken install, or one
    /// matched again, so every open tab is sent its new state (10 b, 2026-10-10).
    /// </summary>
    public void InstallHealthChanged() => Push();

    /// <summary>Everything the page would show now.</summary>
    /// <returns>The view.</returns>
    public PageView View(PageKind kind)
    {
        // Read outside the lock: the background answers it from its own memory, and
        // a page that waited on the background under its own lock would wait twice.
        // The status page reads it too, for its update section.
        var holds = kind is PageKind.Update or PageKind.Status ? ReadHolds() : null;

        lock (_gate)
        {
            return new PageView(_facts, _snapshot, _notes[kind], _registration, _registering, holds, Changelog, Install?.Difference);
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
                // The registration is read again for every page load, the way the window
                // read it each time it opened, and arrives through the stream when it is in.
                StartRegistrationRead();
                await WriteAsync(context, "text/html; charset=utf-8", Page(PageKind.Status, query)).ConfigureAwait(false);
                return true;

            case "sessions" when get:
                await RefreshSessionsAsync(context.RequestAborted).ConfigureAwait(false);
                await WriteAsync(context, "text/html; charset=utf-8", Page(PageKind.Sessions, query)).ConfigureAwait(false);
                return true;

            case "update" when get:
                await WriteAsync(context, "text/html; charset=utf-8", Page(PageKind.Update, query)).ConfigureAwait(false);
                return true;

            case "changelog" when get:
                await WriteAsync(context, "text/html; charset=utf-8", Page(PageKind.Changelog, query)).ConfigureAwait(false);
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
    public ValueTask DisposeAsync()
    {
        Served? ending;

        lock (_gate)
        {
            ending = _current;
            _current = null;
            _stopping = true;
        }

        ending?.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// How often a listener reads what holds the update, to send the tabs a new
    /// state when it has changed: <b>once a second</b>, the unit the countdowns count
    /// in. The countdowns themselves run in the page's script.
    /// </summary>
    internal static TimeSpan HoldsWatchPeriod { get; } = ProcessBounds.PageHoldsWatchPeriod;

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
        var page = PageNames.Parse(PageGate.QueryValue(query, "page"));

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
            "install-now" => StartInstallNow(String(request, "version")),
            "open-folder" => OpenFolder(String(request, "folder")),
            "open-session" => OpenSession(String(request, "session")),
            "open-trace" => OpenTrace(String(request, "trace")),
            "refresh-sessions" => Run(RefreshSessionsAsync(CancellationToken.None)),
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

    /// <summary>Reads what holds the update, or <see langword="null"/> where the read failed.</summary>
    /// <returns>The snapshot.</returns>
    private UpdateHoldSnapshot? ReadHolds()
    {
        try
        {
            return Holds.Read();
        }
#pragma warning disable CA1031 // A read that failed is a page that says so, never a background that stops.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            PageServiceLog.HoldsUnread(_logger, failure);
            return null;
        }
    }

    /// <summary>One second of the watch: a new state for every tab when what holds the update has changed.</summary>
    private void WatchHolds()
    {
        var signature = UpdatePageContent.Signature(ReadHolds(), _clock.GetUtcNow());
        bool changed;

        lock (_gate)
        {
            changed = !string.Equals(signature, _holdsSignature, StringComparison.Ordinal);
            _holdsSignature = signature;
        }

        if (changed)
        {
            Push();
        }
    }

    /// <summary>The update page's install button: the person's install-now, through the background.</summary>
    /// <param name="version">The version the page showed.</param>
    /// <returns>Whether the request was taken.</returns>
    private bool StartInstallNow(string? version)
    {
        if (version is not { Length: > 0 })
        {
            return false;
        }

        lock (_gate)
        {
            if (_installingNow)
            {
                return true;
            }

            _installingNow = true;
            _notes[PageKind.Update] = new PageNote($"Closing every session and connection, then installing BrowserAI {version}.");
        }

        Push();
        _ = Task.Run(() => InstallNowAsync(Holds, version), CancellationToken.None);
        return true;
    }

    private async Task InstallNowAsync(IUpdateHolds holds, string version)
    {
        PageNote? refused;

        try
        {
            // Bounded by the update's own tripwire: closing every session is each one's
            // minute at most, and then the hand-over.
            using var bounded = new CancellationTokenSource(UpdateBudgets.CrashTripwire, _clock);

            refused = await holds.InstallNowAsync(version, bounded.Token).ConfigureAwait(false) is { } why
                ? new PageNote(why)
                : null;
        }
#pragma warning disable CA1031 // An install that threw is a sentence on the page; the background keeps running.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            refused = new PageNote($"BrowserAI {version} was not installed.", failure.Message);
        }

        // #105, 2026-10-10: an install that started is said to every tab once, by the
        // background's own stop, which also says how to get the page back. The page
        // said it as well, released by the same stop, and a tab showed whichever came
        // first; now the page shows what the background reports until that stop.
        lock (_gate)
        {
            _installingNow = false;
            _notes[PageKind.Update] = refused;
        }

        Push();
    }

    private static string? String(JsonElement request, string name) =>
        request.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

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

        /// <summary>The once-a-second read of what holds the update, or <see langword="null"/> where nothing reports it.</summary>
        public ITimer? HoldsWatch { get; init; }

        public void Dispose()
        {
            HoldsWatch?.Dispose();
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

        [LoggerMessage(EventId = 7014, Level = LogLevel.Warning, Message = "What holds the update could not be read for the page.")]
        public static partial void HoldsUnread(ILogger logger, Exception failure);
    }
}
