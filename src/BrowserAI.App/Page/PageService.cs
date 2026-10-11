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

    /// <summary>
    /// The same, with no conversation named, so that no client's records are read: what
    /// the once-a-second watch compares to tell whether to send the tabs a new state.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-11, the maintainer's 46 a</b>: the conversations and sessions views
    /// are live. A name is read when a state is drawn, the way the update page's names are
    /// (1.3 c), and never once a second.
    /// </remarks>
    /// <returns>The snapshot, every relay's conversation left unread.</returns>
    SessionsSnapshot Peek();
}

/// <summary>What the page reads of the session folders BrowserAI knows of.</summary>
/// <remarks>
/// <b>Added 2026-10-11, the maintainer's 43 b</b>: each session folder's size, and the
/// folders not open now, which the background passes to the page. A measurement walks
/// every folder, so the page asks for one on the thread pool and never under its lock.
/// </remarks>
internal interface IPageFolders
{
    /// <summary>Finds every session folder and weighs each one.</summary>
    /// <returns>The measurement.</returns>
    FolderMeasure Measure();
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
    private readonly Dictionary<PageKind, PageNote?> _notes = NoNotes();

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

    /// <summary>What the watch last saw of the sessions, every name left out (46 a).</summary>
    private string? _sessionsSignature;

    /// <summary>The folders of the sessions held when the watch last looked, which a measurement follows (43 b).</summary>
    private string? _heldFolders;

    /// <summary>The last measurement of the session folders, or <see langword="null"/> before the first.</summary>
    private FolderMeasure? _folders;

    /// <summary>Whether a measurement is running, and whether another was asked for while it ran.</summary>
    private bool _measureRunning;

    private bool _measureAgain;

    /// <summary>Whether the person asked for the running measurement, which the storage page says while it runs.</summary>
    private bool _measuring;

    /// <summary>What holds a downloaded update, and the person's install-now: the background's update core.</summary>
    /// <remarks>
    /// <i>Required since 2026-10-10</i> (#73 of the texts review): the one background
    /// always has its update core, so a page with nothing reporting what holds an update
    /// does not exist, and a snapshot the page does not have is a read that failed.
    /// </remarks>
    internal required IUpdateHolds Holds { get; init; }

    /// <summary>The session folders BrowserAI knows of, and what each weighs (43 b, 2026-10-11).</summary>
    internal required IPageFolders Folders { get; init; }

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
                    // While a listener is up, what holds the update and what the
                    // sessions are are read once a second, and every tab is sent a new
                    // state when either has changed (46 a).
                    HoldsWatch = _clock.CreateTimer(_ => Watch(), null, HoldsWatchPeriod, HoldsWatchPeriod),
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
    /// <param name="kind">Which page.</param>
    /// <param name="subject">The session a session's page is about, by its opaque name, or <see langword="null"/>.</param>
    /// <returns>The view.</returns>
    public PageView View(PageKind kind, string? subject = null)
    {
        // Read outside the lock: the background answers both from its own memory, and a
        // page that waited on the background under its own lock would wait twice. The
        // home, the conversations page's BrowserAI and the install page say them too.
        var holds = kind is PageKind.Update or PageKind.Status or PageKind.Sessions ? ReadHolds() : null;
        var updates = kind is PageKind.Update or PageKind.Status or PageKind.Sessions or PageKind.Install ? ReadWatch() : UpdateWatch.Unreported;

        lock (_gate)
        {
            return new PageView(_facts, _snapshot, _notes[kind], _registration, _registering, holds, Changelog, Install?.Difference)
            {
                Subject = subject,
                Updates = updates,
                Folders = _folders,
                Measuring = _measuring,
                Now = _clock.GetUtcNow(),
            };
        }
    }

    /// <inheritdoc />
    public async Task<bool> ServeAsync(HttpContext context, string route, string query)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(query);

        var get = string.Equals(context.Request.Method, "GET", StringComparison.Ordinal);

        // 40 a, 2026-10-11: every page has a route of its own, and the four that were
        // handed out before keep theirs: the home is the status page's, the conversations
        // page the sessions page's.
        if (get && PageNames.OfRoute(route) is { } kind)
        {
            await BeforeDrawingAsync(kind, context.RequestAborted).ConfigureAwait(false);
            await WriteAsync(context, "text/html; charset=utf-8", Page(kind, query)).ConfigureAwait(false);
            return true;
        }

        switch (route)
        {
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
    /// How often a listener reads what holds the update and what the sessions are, to
    /// send the tabs a new state when either has changed: <b>once a second</b>, the unit
    /// the countdowns count in. The countdowns themselves run in the page's script.
    /// </summary>
    /// <remarks>
    /// <i>Corrected 2026-10-11 (previously "reads what holds the update" alone)</i>: the
    /// maintainer's 46 a made the conversations and sessions views live on the same watch.
    /// </remarks>
    internal static TimeSpan HoldsWatchPeriod { get; } = ProcessBounds.PageHoldsWatchPeriod;

    /// <summary>A table with every page and no note on any of them.</summary>
    private static Dictionary<PageKind, PageNote?> NoNotes()
    {
        var notes = new Dictionary<PageKind, PageNote?>();

        foreach (var kind in Enum.GetValues<PageKind>())
        {
            notes[kind] = null;
        }

        return notes;
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

        return PageContent.Document(View(kind, SubjectOf(kind, query)), kind, tab, OccasionFor(tab), _clock.GetUtcNow());
    }

    /// <summary>The session a session's page is about, as its address names it.</summary>
    private static string? SubjectOf(PageKind kind, string query) => kind is PageKind.Session ? PageGate.QueryValue(query, "id") : null;

    /// <summary>
    /// What a page is drawn from, read before it is drawn: the sessions with their
    /// conversations named, the setup of Claude Code and Codex, and the session folders'
    /// sizes, each for the pages that show it.
    /// </summary>
    /// <remarks>
    /// <b>The registration and the measurement arrive through the stream</b>: both are read
    /// on the thread pool and the page is drawn without waiting for them, the way the status
    /// page read the registration for every load. The sessions are read before the draw,
    /// because a page without them would say nothing runs.
    /// </remarks>
    /// <param name="kind">The page about to be drawn.</param>
    /// <param name="cancellationToken">Ends the read.</param>
    /// <returns>The read.</returns>
    private async Task BeforeDrawingAsync(PageKind kind, CancellationToken cancellationToken)
    {
        if (kind is PageKind.Status or PageKind.Setup)
        {
            _ = StartRegistrationRead();
        }

        if (kind is PageKind.Status or PageKind.Sessions or PageKind.Session or PageKind.Storage)
        {
            StartMeasure(asked: false);
        }

        if (kind is not (PageKind.Changelog or PageKind.Setup or PageKind.Update))
        {
            await RefreshSessionsAsync(cancellationToken).ConfigureAwait(false);
        }
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
            PageContent.Fragment(View(stream.Page, stream.Subject), stream.Page, stream.Tab, OccasionFor(stream.Tab), _clock.GetUtcNow()))));

    /// <summary>Sends every tab what it shows now.</summary>
    private void Push() => Tabs?.Broadcast(StateFor);

    private async Task StreamAsync(HttpContext context, string query)
    {
        var tabs = Tabs;
        var page = PageNames.Parse(PageGate.QueryValue(query, "page"));

        if (tabs?.Connect(TabOf(query), page, SubjectOf(page, query)) is not { } stream)
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
            "measure-sessions" => MeasureAsked(),
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

    /// <summary>Where the updates come from and when BrowserAI last checked, or nothing reported where the read failed.</summary>
    /// <returns>The watch.</returns>
    private UpdateWatch ReadWatch()
    {
        try
        {
            return Holds.Watch();
        }
#pragma warning disable CA1031 // A read that failed is a page that says nothing of it, never a background that stops.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            // An event of its own since fact 7 of the reader pass of 2026-10-11 (previously
            // HoldsUnread's 7014, which says what holds the update could not be read).
            PageServiceLog.WatchUnread(_logger, failure);
            return UpdateWatch.Unreported;
        }
    }

    /// <summary>The sessions with no conversation named, or <see langword="null"/> where the read failed.</summary>
    /// <returns>The snapshot.</returns>
    private SessionsSnapshot? PeekSessions()
    {
        try
        {
            return _sessions.Peek();
        }
#pragma warning disable CA1031 // A read that failed changes nothing the watch sends; the next second reads again.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            PageServiceLog.SessionsUnpeeked(_logger, failure);
            return null;
        }
    }

    /// <summary>
    /// One second of the watch: a new state for every tab when what holds the update or
    /// what the sessions are has changed, and a measurement when the sessions held have.
    /// </summary>
    /// <remarks>
    /// <b>46 a, the maintainer's choice of 2026-10-11</b>: the conversations and sessions
    /// views are live, pushed on every change, as the update page already was. The watch
    /// compares the sessions with every conversation's name left out, so a second reads no
    /// client's records; a change is read again with the names, and that read is what the
    /// tabs are sent. A session that opens or goes is a folder to weigh (43 b).
    /// </remarks>
    private void Watch()
    {
        var holds = UpdatePageContent.Signature(ReadHolds(), _clock.GetUtcNow());
        var peeked = PeekSessions();
        var sessions = peeked is null ? null : ConversationsContent.Signature(peeked);
        var held = peeked is null ? null : ConversationsContent.HeldFolders(peeked);
        bool holdsChanged;
        bool sessionsChanged;
        bool heldChanged;

        lock (_gate)
        {
            holdsChanged = !string.Equals(holds, _holdsSignature, StringComparison.Ordinal);
            _holdsSignature = holds;
            sessionsChanged = sessions is not null && !string.Equals(sessions, _sessionsSignature, StringComparison.Ordinal);
            _sessionsSignature = sessions ?? _sessionsSignature;
            heldChanged = held is not null && !string.Equals(held, _heldFolders, StringComparison.Ordinal);
            _heldFolders = held ?? _heldFolders;
        }

        if (heldChanged)
        {
            StartMeasure(asked: false);
        }

        if (sessionsChanged)
        {
            // The read pushes once it is in, and with it whatever else changed.
            _ = RefreshSessionsAsync(CancellationToken.None);
        }
        else if (holdsChanged)
        {
            Push();
        }
    }

    /// <summary>
    /// Weighs every session folder on the thread pool, unless a measurement is running,
    /// in which case one more runs after it.
    /// </summary>
    /// <param name="asked">
    /// Whether the person asked, which the storage page says while it runs; a measurement
    /// nobody asked for sends the tabs a new state only when it found something new.
    /// </param>
    private void StartMeasure(bool asked)
    {
        lock (_gate)
        {
            _measuring |= asked;

            if (_measureRunning)
            {
                _measureAgain = true;
                return;
            }

            _measureRunning = true;
        }

        if (asked)
        {
            Push();
        }

        _ = Task.Run(RunMeasurements, CancellationToken.None);
    }

    /// <summary>The storage page's <i>Measure again</i>: a measurement the person asked for.</summary>
    /// <returns>Always <see langword="true"/>: the request was taken.</returns>
    private bool MeasureAsked()
    {
        StartMeasure(asked: true);
        return true;
    }

    /// <summary>Measures, and once more for every measurement asked for while one ran.</summary>
    private void RunMeasurements()
    {
        while (true)
        {
            FolderMeasure? measured;

            try
            {
                measured = Folders.Measure();
            }
#pragma warning disable CA1031 // A measurement that failed leaves the last one standing and is logged; the background keeps running.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                PageServiceLog.FoldersUnmeasured(_logger, failure);
                measured = null;
            }

            bool changed;
            bool again;

            lock (_gate)
            {
                changed = _measuring || (measured is not null && !SameFolders(_folders, measured));
                _folders = measured ?? _folders;
                again = _measureAgain;
                _measureAgain = false;
                _measureRunning = again;
                _measuring &= again;
            }

            if (changed)
            {
                Push();
            }

            if (!again)
            {
                return;
            }
        }
    }

    /// <summary>Whether two measurements found the same folders with the same sizes.</summary>
    private static bool SameFolders(FolderMeasure? before, FolderMeasure after) =>
        before is not null
        && before.Folders.Count == after.Folders.Count
        && before.Folders.Zip(after.Folders).All(static pair => pair.First == pair.Second);

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

            // Corrected 2026-10-11 (previously "Closing every session and connection, then
            // installing ..."), 42 a: a browser closes, and a session is a folder that stays.
            // And the reader pass of that day (previously "... and ending every connection, ..."):
            // the page says conversation, never connection.
            _notes[PageKind.Update] = new PageNote($"Closing every browser and disconnecting every conversation, then installing BrowserAI {version}.");
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

    /// <summary>Opens a session's folder in Explorer, named by the opaque name the page was given.</summary>
    /// <remarks>
    /// <b>A folder not open now is found by the measurement</b>, the storage page's rows
    /// since 2026-10-11 (43 b); a name neither the sessions nor the measurement holds opens
    /// nothing, the way a path sent in its place never has. Opening is all it does (41 a).
    /// </remarks>
    /// <param name="id">The session's opaque name.</param>
    /// <returns>Whether the request was taken.</returns>
    private bool OpenSession(string? id)
    {
        string? directory;

        lock (_gate)
        {
            directory = id is null ? null : _snapshot.SessionById(id)?.Directory ?? _folders?.ById(id)?.Directory;
        }

        if (directory is null)
        {
            return false;
        }

        _host.OpenFolder(directory);
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

        // A trace is opened from its session's page since 2026-10-11 (40 a), which says so.
        if (_host.OpenTrace(trace.Path) is { } refused)
        {
            Note(PageKind.Session, new PageNote("The trace viewer did not open.", refused));
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

    /// <summary>What the setup page says while one action runs.</summary>
    /// <remarks>
    /// <i>Corrected 2026-10-11 (previously "Registering BrowserAI with {name}.")</i>, the
    /// maintainer's 42 a: an app is set up.
    /// </remarks>
    private static string Working(ClientState client, string action)
    {
        var name = client.Client.DisplayName;

        return action switch
        {
            "register" => $"Setting up BrowserAI in {name}.",
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

            // A picker closed without a choice changes nothing and says nothing. The note
            // is the setup page's, where the buttons are since 2026-10-11 (40 a).
            if (note is not null)
            {
                _notes[PageKind.Setup] = note;
            }
        }

        Push();
        _ = StartRegistrationRead();
    }

    private async Task<PageNote?> InProjectAsync(AppState state, RegistrationClient who, bool register)
    {
        // Corrected 2026-10-11 (previously "Choose the folder to register BrowserAI in for
        // ..."), 42 a: the picker says what the button said.
        var picked = await _host.PickFolderAsync(register
            ? $"Choose the folder to set up BrowserAI in for {who.DisplayName}. A {who.ProjectFileName} is written under it, to be committed with the project."
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
            // Corrected 2026-10-11 (previously "BrowserAI could not read its running
            // servers.", on the sessions page alone), 42 a and 40 a: every page that shows
            // them says it.
            var note = new PageNote("BrowserAI could not read its conversations and sessions.", failure.Message);

            lock (_gate)
            {
                _notes[PageKind.Sessions] = note;
                _notes[PageKind.Status] = note;
                _notes[PageKind.Session] = note;
            }

            Push();
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

        /// <summary>The once-a-second read of what holds the update and what the sessions are, or <see langword="null"/> before it is set.</summary>
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

        [LoggerMessage(EventId = 7015, Level = LogLevel.Warning, Message = "The page's watch could not read the sessions, so it reads them again a second later.")]
        public static partial void SessionsUnpeeked(ILogger logger, Exception failure);

        [LoggerMessage(EventId = 7016, Level = LogLevel.Warning, Message = "The session folders could not be measured for the page, so it shows the last measurement.")]
        public static partial void FoldersUnmeasured(ILogger logger, Exception failure);

        [LoggerMessage(EventId = 7017, Level = LogLevel.Warning, Message = "Where the updates come from could not be read for the page, so it says nothing about them.")]
        public static partial void WatchUnread(ILogger logger, Exception failure);
    }
}
