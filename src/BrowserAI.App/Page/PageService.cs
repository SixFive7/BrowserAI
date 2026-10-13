// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using BrowserAI.App.Interop;
using BrowserAI.Registration;
using BrowserAI.Runtime;
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

    /// <summary>Asks the person for a folder through Windows' own picker, Q311.</summary>
    /// <param name="prompt">What the picker says the folder is for.</param>
    /// <returns>What the person chose, or that they chose nothing.</returns>
    Task<FolderPick> PickFolderAsync(string prompt);

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
    Task<SessionsSnapshot> PeekAsync();
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
    Task<FolderMeasure> MeasureAsync();
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
/// starts one, on a fresh port with a fresh token, and <see cref="TryStopAsync"/> ends it
/// once no tab is connected and a minute has passed (Q336 a). A later hand-out
/// starts a new one, so an address outlives its listener only in a tab nobody is
/// using.
/// </para>
/// <para>
/// ⚠️ <b>The listener stays up while the background runs since 2026-10-12, 69 d with fallback
/// b, the maintainer's words of 2026-10-11 verbatim: <i>"69 d with fallback to b"</i>.</b>
/// <i>Added by addition: the paragraph above is what the page did until that day.</i> The
/// first hand-out still starts the listener, and nothing but the background's end stops it,
/// so one token stays valid for the background's whole run; a background that starts again
/// takes the port the last one held, when it is free, and goes on numbering tabs where it
/// stopped (<see cref="IPageListenerMemory"/>). A browser remembers <i>Always allow</i> for
/// opening BrowserAI per address, so the address a folder link is clicked from has to last.
/// </para>
/// <para>
/// <b>Two threads call in and one lock guards what they share.</b> The pipe's
/// thread hands out addresses; Kestrel's threads serve requests and run actions;
/// the coordinator's thread decides whether to stop. Rendering reads a snapshot
/// taken under the lock and never the fields themselves.
/// </para>
/// <para>
/// <b>No lock is held across an await</b>, since 2026-10-11, the maintainer's decision
/// 54: the lock guards fields and nothing else, a listener's start and stop are awaited
/// outside it, and the hand-outs take their turn at starting one on a semaphore awaited
/// with <see cref="SemaphoreSlim.WaitAsync()"/>. <i>Previously the first hand-out started
/// the listener under the lock and waited for it there, and a stop waited for the
/// listener on the caller's thread.</i> The "threads" above are pool threads since that
/// day, and the coordinator's is the background's loop.
/// </para>
/// </remarks>
internal sealed partial class PageService : IPageRoutes, IAsyncDisposable
{
    private readonly Lock _gate = new();

    /// <summary>
    /// Taken by a hand-out for as long as it may start a listener, so a second hand-out
    /// finds the first one's: the lock above is never held across the start, which is
    /// awaited (decision 54, 2026-10-11).
    /// </summary>
    private readonly SemaphoreSlim _starting = new(1, 1);

    /// <summary>Taken by a push for as long as it draws and sends, so the tabs receive their states in the order they were drawn.</summary>
    private readonly SemaphoreSlim _pushing = new(1, 1);
    private int _disposed;
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

    /// <summary>
    /// What holds the update, every conversation named, as it was last read: before a page
    /// that shows it is drawn, and by the watch once a second; <see langword="null"/> where
    /// the read failed or before the first.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-11</b>, the maintainer's decision 63: the names are read from the
    /// clients' files with asynchronous reads, and a page is drawn from what was read,
    /// as its sessions are (<see cref="_snapshot"/>). <i>Previously every draw read the holds
    /// itself, on the drawing thread.</i>
    /// </remarks>
    private UpdateHoldSnapshot? _holds;

    /// <summary>Whether a second of the watch is running, so a slow one is not overtaken by the next.</summary>
    private int _watching;

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

    /// <summary>The newest reading of what BrowserAI's processes use, or <see langword="null"/> before the first (59 b).</summary>
    private PageUsage? _usage;

    /// <summary>How many connected tabs show the figures: the readings run while one does, and never otherwise (59 a and b).</summary>
    private int _usageTabs;

    /// <summary>The readings' timer, while a tab shows the figures.</summary>
    private ITimer? _usageWatch;

    /// <summary>Whether a reading is running, so a slow one is not overtaken by the next.</summary>
    private int _reading;

    /// <summary>What the after-update notice's record says, once it has been read (57 a).</summary>
    private UpdateNotice? _notice;

    /// <summary>The one read of the notice's record, and the record written after an update.</summary>
    private TaskCompletionSource? _noticeLoaded;

    /// <summary>What holds a downloaded update, and the person's install-now: the background's update core.</summary>
    /// <remarks>
    /// <i>Required since 2026-10-10</i> (#73 of the texts review): the one background
    /// always has its update core, so a page with nothing reporting what holds an update
    /// does not exist, and a snapshot the page does not have is a read that failed.
    /// </remarks>
    internal required IUpdateHolds Holds { get; init; }

    /// <summary>The session folders BrowserAI knows of, and what each weighs (43 b, 2026-10-11).</summary>
    internal required IPageFolders Folders { get; init; }

    /// <summary>
    /// Where the listener's port and last tab number are kept across the background's
    /// restarts (69 b, 2026-10-11): a file under the data root in the product, and what the
    /// process remembers where nothing sets one.
    /// </summary>
    internal IPageListenerMemory Memory { get; init; } = new PageListenerMemoryInProcess();

    /// <summary>What BrowserAI's own processes use, read by the background (59 b, 62 a and 65 a, 2026-10-11).</summary>
    internal required IPageUsage Usage { get; init; }

    /// <summary>The installed version's section of the changelog shipped in the build, or <see langword="null"/>.</summary>
    internal ChangelogSection? Changelog { get; init; }

    /// <summary>Every release the changelog shipped in the build holds, in its order (56 a, 2026-10-11).</summary>
    internal IReadOnlyList<ChangelogSection> Releases { get; init; } = [];

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
    /// <remarks>
    /// <b>The listener is started outside the lock and awaited</b>, since 2026-10-11, the
    /// maintainer's decision 54. <i>Corrected that day (previously "Started here, under the
    /// lock and synchronously: the pipe's thread is the caller, it has a bound of its own,
    /// and a second hand-out must find this listener and not start another")</i>: a second
    /// hand-out still finds it, because hand-outs take <see cref="_starting"/> in turn, and
    /// the tab is still taken under the lock, from the listener that is up at that moment.
    /// </remarks>
    public async Task<string?> HandOutAsync(PageKind kind)
    {
        if (Volatile.Read(ref _disposed) is not 0)
        {
            return null;
        }

        await _starting.WaitAsync().ConfigureAwait(false);

        try
        {
            while (true)
            {
                (int Port, int Tab, Served Served)? handed = null;

                lock (_gate)
                {
                    if (_stopping)
                    {
                        return null;
                    }

                    if (_current is { Tabs.Closing: false } current)
                    {
                        handed = (current.Listener.Gate.Port, current.Tabs.HandOut()!.Value, current);
                        _occasionTab ??= (current, handed.Value.Tab);
                    }
                }

                if (handed is { } given)
                {
                    // 69 b: the next background goes on numbering from here.
                    await RememberAsync(given.Port, given.Tab).ConfigureAwait(false);

                    var route = PageNames.RouteOf(kind);

                    return string.Create(CultureInfo.InvariantCulture, $"{given.Served.Listener.Gate.Root}{route}?tab={given.Tab}");
                }

                // 69 b: the port the last listener of this data root held, and its last tab.
                var recall = await RecallAsync().ConfigureAwait(false);

#pragma warning disable CA2000 // The service keeps the listener it takes and stops it; the finally stops one it does not take.
                var started = await Served.StartAsync(this, _clock, _linger, _wake, _logger, recall).ConfigureAwait(false);
#pragma warning restore CA2000
                var taken = false;

                try
                {
                    // While a listener is up, what holds the update and what the sessions
                    // are are read once a second, and every tab is sent a new state when
                    // either has changed (46 a).
                    started.HoldsWatch = _clock.CreateTimer(_ => _ = WatchAsync(), null, HoldsWatchPeriod, HoldsWatchPeriod);

                    lock (_gate)
                    {
                        if (!_stopping && _current is null or { Tabs.Closing: true })
                        {
                            _current = started;
                            taken = true;
                        }
                    }
                }
                finally
                {
                    if (!taken)
                    {
                        await started.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            _ = _starting.Release();
        }
    }

    /// <summary>
    /// Ends the listener when the background ends with it, and never while it runs.
    /// </summary>
    /// <param name="final">
    /// Whether the background stops with it, after which nothing more is handed out;
    /// <see langword="false"/> for the background's loop, which asks on every wake.
    /// </param>
    /// <returns>Whether no listener is up now.</returns>
    /// <remarks>
    /// ⚠️ <b>69 d, the maintainer's words of 2026-10-11 verbatim: <i>"69 d with fallback to
    /// b"</i>.</b> <i>Corrected 2026-10-12 (previously "Ends the listener once no tab is
    /// connected and the linger has run out", Q336 a's minute after the last tab)</i>: the
    /// listener stays up while the background runs, so its address and its token last as
    /// long as the background, and a browser's <i>Always allow</i> for that address does too.
    /// The background's loop still asks on every wake the tabs make, and is told no.
    /// </remarks>
    public async Task<bool> TryStopAsync(bool final)
    {
        Served? ending;

        lock (_gate)
        {
            if (_current is not null && !final)
            {
                return false;
            }

            ending = _current;
            _current = null;
            _stopping |= final;
        }

        if (ending is not null)
        {
            await ending.DisposeAsync().ConfigureAwait(false);
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

    /// <summary>
    /// The install's state changed: a session was refused as a broken install, or one
    /// matched again, so every open tab is sent its new state (10 b, 2026-10-10).
    /// </summary>
    public void InstallHealthChanged() => Push();

    /// <summary>Everything the page would show now.</summary>
    /// <param name="kind">Which page.</param>
    /// <param name="subject">What the page is about, as its address names it: the session the conversations page has chosen, by its opaque name, or the release the changelog page shows; or <see langword="null"/>.</param>
    /// <returns>The view.</returns>
    public PageView View(PageKind kind, string? subject = null)
    {
        // Read outside the lock: the background answers both from its own memory, and a
        // page that waited on the background under its own lock would wait twice. The
        // home, the conversations page's BrowserAI and the install page say them too.
        var holds = kind is PageKind.Update or PageKind.Status or PageKind.Sessions ? LastHolds() : null;
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
                Usage = _usage,
                UpdatedTo = _notice is { Seen: false } waiting && string.Equals(waiting.Version, _facts.Version, StringComparison.Ordinal) ? waiting.Version : null,
                Releases = Releases,

                // 55 a and 70 a, 2026-10-11: the folder links carry the token of the listener up now.
                Links = _facts.LinkScheme is { } scheme && _current is { } served ? new PageLinkContext(scheme, served.Listener.Gate.Token) : null,
            };
        }
    }

    /// <inheritdoc />
    public bool IsPage(string route) => PageNames.OfRoute(route) is not null;

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
    /// <remarks>
    /// <b>It waits for a hand-out that is starting a listener</b>, so the listener it
    /// starts is the one stopped here, or is never kept.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        Served? ending;

        await _starting.WaitAsync().ConfigureAwait(false);

        try
        {
            lock (_gate)
            {
                ending = _current;
                _current = null;
                _stopping = true;
            }
        }
        finally
        {
            _ = _starting.Release();
        }

        if (ending is not null)
        {
            await ending.DisposeAsync().ConfigureAwait(false);
        }

        StopReadingUsage();
        _starting.Dispose();
        _pushing.Dispose();
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

    /// <summary>What a page is about, as its address names it.</summary>
    /// <remarks>
    /// <b>The session the conversations page has chosen</b>, in <c>id</c>, since 2026-10-12, when
    /// the page took over from one session's page and kept its parameter; <b>and the release
    /// the changelog page shows</b>, in <c>version</c> (56 a). A tab's stream names either in
    /// <c>id</c>, which is what the page's script sends.
    /// </remarks>
    private static string? SubjectOf(PageKind kind, string query) => kind switch
    {
        PageKind.Sessions => PageGate.QueryValue(query, "id"),
        PageKind.Changelog => PageGate.QueryValue(query, "version") ?? PageGate.QueryValue(query, "id"),
        _ => null,
    };

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

        // 57 a: every What is new link opens this page, and opening it is seeing the notice.
        if (kind is PageKind.Changelog)
        {
            await MarkNoticeSeenAsync().ConfigureAwait(false);
        }
        else
        {
            await EnsureNoticeAsync().ConfigureAwait(false);
        }

        // 59 b: a page that shows the figures is drawn with a first reading, and the
        // readings that follow come with its tab's stream.
        if (ShowsUsage(kind) && LastUsage() is null)
        {
            await ReadUsageAsync().ConfigureAwait(false);
        }

        if (kind is PageKind.Status or PageKind.Sessions or PageKind.Storage)
        {
            StartMeasure(asked: false);
        }

        if (kind is PageKind.Update or PageKind.Status or PageKind.Sessions)
        {
            var holds = await ReadHoldsAsync().ConfigureAwait(false);

            lock (_gate)
            {
                _holds = holds;
            }
        }

        if (kind is not (PageKind.Changelog or PageKind.Setup or PageKind.Update))
        {
            await RefreshSessionsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>What holds the update as it was last read, or <see langword="null"/>.</summary>
    /// <returns>The snapshot.</returns>
    private UpdateHoldSnapshot? LastHolds()
    {
        lock (_gate)
        {
            return _holds;
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
    /// <summary>Sends every tab a state drawn now.</summary>
    /// <remarks>
    /// ⚠️ <b>One broadcast at a time since 2026-10-13</b> (previously each push drew and sent
    /// on its caller's thread, and nothing put two of them in order): two pushes on two
    /// threads could draw an older view and a newer one and send them the other way round,
    /// so a tab showed the older view until the next change, which the gate over
    /// <c>e092de31</c> found. A push now waits its turn on <see cref="_pushing"/>, awaited
    /// and never blocking a worker (54), and draws when its turn comes, so the last state a
    /// tab receives is drawn from the newest model. The lock is the broadcast's, one for all
    /// the tabs, because a broadcast already draws its tabs one after another.
    /// </remarks>
    private void Push() => _ = DrawAndSendInTurnAsync();

    private async Task DrawAndSendInTurnAsync()
    {
        try
        {
            await _pushing.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The page is gone, and its tabs with it.
            return;
        }

        try
        {
            Tabs?.Broadcast(StateFor);
        }
        finally
        {
            ReleasePushing();
        }
    }

    private void ReleasePushing()
    {
        try
        {
            _ = _pushing.Release();
        }
        catch (ObjectDisposedException)
        {
            // Disposed with the page while this push drew, and nothing waits for it now.
        }
    }

    private async Task StreamAsync(HttpContext context, string query)
    {
        var tabs = Tabs;
        var page = PageNames.Parse(PageGate.QueryValue(query, "page"));

        if (tabs?.Connect(TabOf(query), page, SubjectOf(page, query)) is not { } stream)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (ShowsUsage(page))
        {
            UsageTabArrived();
        }

        try
        {
            await EnsureNoticeAsync().ConfigureAwait(false);

            context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream";

            await context.Response.StartAsync(context.RequestAborted).ConfigureAwait(false);

            // A stream's first state reads what holds the update for itself, as a draw does:
            // a tab whose stream opens with no draw before it, the reconnect after the
            // background restarted, would otherwise be told what holds the update cannot
            // be read until the watch's next second. Added 2026-10-11, when the holds came
            // to be read asynchronously and cached; until then every state read them.
            if (page is PageKind.Update or PageKind.Status or PageKind.Sessions)
            {
                var holds = await ReadHoldsAsync().ConfigureAwait(false);

                lock (_gate)
                {
                    _holds = holds;
                }
            }

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
            // Before the tab is let go, so that a tab counted gone no longer has a reading coming.
            if (ShowsUsage(page))
            {
                UsageTabLeft();
            }

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
            "open-folder" => await OpenFolderAsync(String(request, "folder")).ConfigureAwait(false),
            "open-session" => OpenSession(String(request, "session")),
            "refresh-sessions" => Run(RefreshSessionsAsync(CancellationToken.None)),
            "measure-sessions" => MeasureAsked(),
            "dismiss-notice" => Run(MarkNoticeSeenAsync()),
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
    private async Task<UpdateHoldSnapshot?> ReadHoldsAsync()
    {
        try
        {
            return await Holds.ReadAsync().ConfigureAwait(false);
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
    private async Task<SessionsSnapshot?> PeekSessionsAsync()
    {
        try
        {
            return await _sessions.PeekAsync().ConfigureAwait(false);
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
    /// <i>Asynchronous since 2026-10-11, with the holds' read; a second that is still
    /// running when the next is due is not overtaken by it.</i>
    /// </remarks>
    /// <returns>The second, which never faults.</returns>
    private async Task WatchAsync()
    {
        if (Interlocked.Exchange(ref _watching, 1) is not 0)
        {
            return;
        }

        try
        {
            await WatchOnceAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A timer's second: a failure is a line in the log, and the next second looks again.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            PageServiceLog.HoldsUnread(_logger, failure);
        }
        finally
        {
            Volatile.Write(ref _watching, 0);
        }
    }

    private async Task WatchOnceAsync()
    {
        var read = await ReadHoldsAsync().ConfigureAwait(false);
        var holds = UpdatePageContent.Signature(read, _clock.GetUtcNow());
        var peeked = await PeekSessionsAsync().ConfigureAwait(false);
        var sessions = peeked is null ? null : ConversationsContent.Signature(peeked);
        var held = peeked is null ? null : ConversationsContent.HeldFolders(peeked);
        bool holdsChanged;
        bool sessionsChanged;
        bool heldChanged;

        lock (_gate)
        {
            _holds = read;
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

        _ = Task.Run(RunMeasurementsAsync, CancellationToken.None);
    }

    /// <summary>The storage page's <i>Measure again</i>: a measurement the person asked for.</summary>
    /// <returns>Always <see langword="true"/>: the request was taken.</returns>
    private bool MeasureAsked()
    {
        StartMeasure(asked: true);
        return true;
    }

    /// <summary>Measures, and once more for every measurement asked for while one ran.</summary>
    /// <returns>The measurements, which never fault.</returns>
    private async Task RunMeasurementsAsync()
    {
        while (true)
        {
            FolderMeasure? measured;

            try
            {
                measured = await Folders.MeasureAsync().ConfigureAwait(false);
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

    /// <summary>
    /// How often the figures are read while a tab shows them: every 2 s, 59 b. Their charts
    /// reach back <see cref="UsageWindow"/>.
    /// </summary>
    internal static TimeSpan UsageInterval { get; } = ProcessBounds.PageUsageInterval;

    /// <summary>How far back the CPU and memory charts reach: 2 minutes.</summary>
    internal static TimeSpan UsageWindow { get; } = ProcessBounds.PageUsageWindow;

    /// <summary>
    /// Reads the after-update notice's record once, and after an update records the new
    /// version as one whose notice waits to be seen (57 a).
    /// </summary>
    /// <remarks>
    /// <b>The background calls it as it starts</b>, so the record of an update is written
    /// whether or not a page is opened before the next start; each page and stream awaits it
    /// before it is drawn.
    /// </remarks>
    /// <returns>The read, which never faults.</returns>
    public Task PrepareAsync() => EnsureNoticeAsync();

    /// <summary>Whether a page shows the CPU and memory figures: the overview and the conversations page, whose inspector says each session's.</summary>
    private static bool ShowsUsage(PageKind kind) => kind is PageKind.Status or PageKind.Sessions;

    /// <summary>The newest reading, or <see langword="null"/>.</summary>
    private PageUsage? LastUsage()
    {
        lock (_gate)
        {
            return _usage;
        }
    }

    /// <summary>A tab that shows the figures connected: the first starts the readings.</summary>
    private void UsageTabArrived()
    {
        lock (_gate)
        {
            if (_usageTabs++ is 0 && !_stopping)
            {
                _usageWatch = _clock.CreateTimer(static page => _ = ((PageService)page!).ReadUsageAsync(), this, UsageInterval, UsageInterval);
            }
        }
    }

    /// <summary>A tab that shows the figures left: the last stops the readings.</summary>
    private void UsageTabLeft()
    {
        lock (_gate)
        {
            if (--_usageTabs is not 0)
            {
                return;
            }
        }

        StopReadingUsage();
    }

    private void StopReadingUsage()
    {
        ITimer? ending;

        lock (_gate)
        {
            ending = _usageWatch;
            _usageWatch = null;
        }

        ending?.Dispose();
    }

    /// <summary>
    /// One reading: what BrowserAI's processes use now, the figures counted against the
    /// reading before, and the figures sent to every tab that shows them (59 b).
    /// </summary>
    /// <remarks>
    /// <b>The reading runs on the reader's own thread and is awaited</b>, so no pool worker
    /// waits in it (54). A reading still running when the next is due is not overtaken; one
    /// that fails is a line in the log, and the figures stay at the last reading.
    /// </remarks>
    /// <returns>The reading, which never faults.</returns>
    private async Task ReadUsageAsync()
    {
        if (Interlocked.Exchange(ref _reading, 1) is not 0)
        {
            return;
        }

        try
        {
            UsageReading reading;

            try
            {
                reading = await Usage.ReadAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // The background is going, and its reader with it.
                return;
            }
#pragma warning disable CA1031 // A reading that failed is a line in the log, and the figures stay at the last one.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                PageServiceLog.UsageUnread(_logger, failure);
                return;
            }

            PageUsage usage;

            lock (_gate)
            {
                usage = _usage = PageUsage.Next(_usage, reading, _clock.GetUtcNow(), UsageWindow);
            }

            var figures = new PageEvent(PageEvents.Usage, UsageContent.EventData(usage, UsageWindow));

            Tabs?.Broadcast(stream => ShowsUsage(stream.Page) ? figures : null);
        }
        finally
        {
            Volatile.Write(ref _reading, 0);
        }
    }

    /// <summary>Reads the notice's record once; every caller awaits the same read.</summary>
    private Task EnsureNoticeAsync()
    {
        var mine = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        if (Interlocked.CompareExchange(ref _noticeLoaded, mine, null) is { } loading)
        {
            return loading.Task;
        }

        _ = LoadNoticeAsync(mine);
        return mine.Task;
    }

    private async Task LoadNoticeAsync(TaskCompletionSource loaded)
    {
        try
        {
            var record = await UpdateNoticeFile.ReadAsync(_facts.DataRoot).ConfigureAwait(false);

            // The start after an update: its version's notice waits to be seen, unless the
            // record already names that version.
            if (_firstOccasion is Occasion.AfterUpdate && !string.Equals(record?.Version, _facts.Version, StringComparison.Ordinal))
            {
                record = new UpdateNotice(_facts.Version, Seen: false);

                lock (_gate)
                {
                    _notice = record;
                }

                await WriteNoticeAsync(record).ConfigureAwait(false);
            }

            lock (_gate)
            {
                _notice = record;
            }
        }
        finally
        {
            _ = loaded.TrySetResult();
        }
    }

    /// <summary>The person has seen what is new: the notice goes from every tab, and the record says so (57 a).</summary>
    /// <returns>The change, which never faults.</returns>
    private async Task MarkNoticeSeenAsync()
    {
        await EnsureNoticeAsync().ConfigureAwait(false);

        UpdateNotice seen;

        lock (_gate)
        {
            if (_notice is not { Seen: false } waiting)
            {
                return;
            }

            _notice = seen = waiting with { Seen = true };
        }

        Push();
        await WriteNoticeAsync(seen).ConfigureAwait(false);
    }

    private async Task WriteNoticeAsync(UpdateNotice notice)
    {
        try
        {
            await UpdateNoticeFile.WriteAsync(_facts.DataRoot, notice).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            PageServiceLog.NoticeUnwritten(_logger, failure);
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

    private async Task<bool> OpenFolderAsync(string? folder)
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

        // Made on the file-system thread, the maintainer's 81 c, and then opened.
        _ = await FileSystemThread.CreateDirectoryAsync(directory).ConfigureAwait(false);
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

    /// <summary>
    /// A folder link a link start asks about: whether it carries the token of the listener
    /// up now, and the folder it opens, or the picker's sentence and what the choice does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>55 a, 70 a and 75 a, the maintainer's words of 2026-10-11 verbatim: <i>"55 a"</i>,
    /// <i>"70 a"</i> and <i>"75 a"</i>.</b> The link carries the page's own token, which is
    /// compared in constant time with the listener's, and names what it opens the way the
    /// buttons did: <c>install</c>, <c>data</c> or <c>logs</c>, a session's opaque name, or an
    /// app's key and an action the page offered it. A path is never taken from a link, so a
    /// link another page drew opens nothing without the token, and nothing but BrowserAI's
    /// own folders with it.
    /// </para>
    /// <para>
    /// <b>A picker holds the setup the way the button's did</b>: the setup page says it is
    /// open, no other setup action starts, and the choice runs the registration it was asked
    /// for. The link start shows the picker, so it comes to the front, and the setup page
    /// says so with no warning that it may be behind.
    /// </para>
    /// </remarks>
    /// <param name="ask">What the link asks for.</param>
    /// <returns>The answer.</returns>
    public async Task<LinkAnswer> LinkAsync(LinkAsk ask)
    {
        ArgumentNullException.ThrowIfNull(ask);

        PageGate? gate;

        lock (_gate)
        {
            gate = _current?.Listener.Gate;
        }

        if (gate is null || !gate.IsToken(ask.Token))
        {
            return new LinkAnswer.Refused(
                "This link does not carry the key of the page BrowserAI serves now, so it opens nothing: it comes from a tab of a BrowserAI that has stopped since, or from another page.");
        }

        switch (ask.Verb)
        {
            case LinkVerb.OpenFolder:
                if (FolderOf(ask.Folder) is not { Length: > 0 } directory)
                {
                    return new LinkAnswer.Refused("This link names no folder of BrowserAI's, so it opens nothing.");
                }

                // Made on the file-system thread, the maintainer's 81 c, as the button's was.
                _ = await FileSystemThread.CreateDirectoryAsync(directory).ConfigureAwait(false);
                return new LinkAnswer.Open(directory);

            case LinkVerb.OpenSession:
                string? folder;

                lock (_gate)
                {
                    folder = ask.Session is null ? null : _snapshot.SessionById(ask.Session)?.Directory ?? _folders?.ById(ask.Session)?.Directory;
                }

                return folder is null
                    ? new LinkAnswer.Refused("This link names no session BrowserAI knows of now, so it opens nothing.")
                    : new LinkAnswer.Open(folder);

            default:
                return PickFor(ask);
        }
    }

    /// <summary>The folder one of the page's folder names stands for, or <see langword="null"/>.</summary>
    /// <param name="folder">The name: <c>install</c>, <c>data</c> or <c>logs</c>.</param>
    /// <returns>The folder.</returns>
    private string? FolderOf(string? folder) => folder switch
    {
        "install" => _facts.InstallRoot,
        "data" => _facts.DataRoot,
        "logs" => _facts.LogDirectory,
        _ => null,
    };

    /// <summary>
    /// A link to Windows' folder picker: the setup action is held as the button's was, and
    /// the link start is handed the picker's sentence and what follows the choice (75 a).
    /// </summary>
    /// <param name="ask">The link.</param>
    /// <returns>The picker, or why there is none.</returns>
    private LinkAnswer PickFor(LinkAsk ask)
    {
        AppState state;
        ClientState client;

        lock (_gate)
        {
            if (_registration?.State is not { } read
                || read.Clients.FirstOrDefault(each => string.Equals(each.Client.Key, ask.Client, StringComparison.Ordinal)) is not { } found
                || ask.Action is not { } action
                || PageLinks.VerbOf(action) is not LinkVerb.Pick
                || !Offered(found, action))
            {
                return new LinkAnswer.Refused("The setup page offers no such setup now, so no folder picker opens. Reload the page to see what it offers.");
            }

            if (_registering is not null)
            {
                return new LinkAnswer.Refused("Another setup is running, so no folder picker opens.");
            }

            state = read;
            client = found;
            _registering = $"Windows' folder picker is open for {found.Client.DisplayName}.";
        }

        Push();

        var register = string.Equals(ask.Action, "register-in-project", StringComparison.Ordinal);

        return new LinkAnswer.Pick(PromptFor(client.Client, register), async picked =>
        {
            PageNote? note;

            try
            {
                note = await AfterThePickAsync(state, client.Client, register, picked).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // A registration that threw is a sentence on the page; the background keeps running.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                note = new PageNote($"BrowserAI could not finish that request for {client.Client.DisplayName}.", failure.Message);
            }

            lock (_gate)
            {
                _registering = null;

                if (note is not null)
                {
                    _notes[PageKind.Setup] = note;
                }
            }

            Push();
            _ = StartRegistrationRead();

            return note?.Sentence ?? "No folder was chosen, so nothing changed.";
        });
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

    private async Task<PageNote?> InProjectAsync(AppState state, RegistrationClient who, bool register) =>
        await AfterThePickAsync(state, who, register, await _host.PickFolderAsync(PromptFor(who, register)).ConfigureAwait(false)).ConfigureAwait(false);

    /// <summary>What Windows' folder picker says the folder is for, for the button and the link alike.</summary>
    /// <param name="who">The app.</param>
    /// <param name="register">Whether the folder is set up, or BrowserAI is removed from it.</param>
    /// <returns>The sentence.</returns>
    private static string PromptFor(RegistrationClient who, bool register) =>
        // Corrected 2026-10-11 (previously "Choose the folder to register BrowserAI in for
        // ..."), 42 a: the picker says what the button said.
        register
            ? $"Choose the folder to set up BrowserAI in for {who.DisplayName}. A {who.ProjectFileName} is written under it, to be committed with the project."
            : $"Choose the project folder to remove BrowserAI from for {who.DisplayName}. Only an entry this install wrote in its {who.ProjectFileName} is removed.";

    /// <summary>What the folder chosen in the picker does: the project setup, or its removal (75 a shares it with the button).</summary>
    /// <param name="state">The registration the page last read.</param>
    /// <param name="who">The app.</param>
    /// <param name="register">Whether the folder is set up, or BrowserAI is removed from it.</param>
    /// <param name="picked">What the picker came back with.</param>
    /// <returns>The setup page's note, or <see langword="null"/> when nothing was chosen.</returns>
    private async Task<PageNote?> AfterThePickAsync(AppState state, RegistrationClient who, bool register, FolderPick picked)
    {
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

    /// <summary>What the listener of this data root last remembered, or nothing where it cannot be read.</summary>
    /// <returns>The port and the last tab number.</returns>
    private async Task<PageListenerRecall> RecallAsync()
    {
        try
        {
            return await Memory.ReadAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A memory that cannot be read is a port Windows picks, never a page that does not open.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            PageServiceLog.MemoryUnread(_logger, failure);
            return new PageListenerRecall(null, 0);
        }
    }

    /// <summary>Remembers the listener's port and the last tab it handed out, for the next background (69 b).</summary>
    /// <param name="port">The port.</param>
    /// <param name="tab">The tab number.</param>
    /// <returns>The write, which never faults.</returns>
    private async Task RememberAsync(int port, int tab)
    {
        try
        {
            await Memory.WriteAsync(port, tab).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A memory that cannot be written costs the next background its port, never this hand-out.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            PageServiceLog.MemoryUnwritten(_logger, failure);
        }
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
    private sealed class Served : IAsyncDisposable
    {
        private Served(PageTabs tabs, PageListener listener)
        {
            Tabs = tabs;
            Listener = listener;
        }

        public PageListener Listener { get; }

        public PageTabs Tabs { get; }

        /// <summary>The once-a-second read of what holds the update and what the sessions are, or <see langword="null"/> before it is set.</summary>
        public ITimer? HoldsWatch { get; set; }

        /// <summary>Starts a listener and its tabs.</summary>
        /// <param name="routes">What answers an admitted request.</param>
        /// <param name="clock">The clock the linger runs on.</param>
        /// <param name="linger">How long to wait after the last tab leaves.</param>
        /// <param name="wake">Wakes the coordinator.</param>
        /// <param name="logger">Where the listener reports.</param>
        /// <param name="recall">The port the last listener held and its last tab number (69 b).</param>
        /// <returns>The listener and its tabs.</returns>
        public static async Task<Served> StartAsync(IPageRoutes routes, TimeProvider clock, TimeSpan linger, Action wake, ILogger logger, PageListenerRecall recall)
        {
            var tabs = new PageTabs(clock, linger, wake, recall.Tab);

            try
            {
                return new Served(tabs, await PageListener.StartAsync(routes, logger, recall.Port ?? 0).ConfigureAwait(false));
            }
            catch
            {
                tabs.Dispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            HoldsWatch?.Dispose();
            await Listener.DisposeAsync().ConfigureAwait(false);
            Tabs.Dispose();
        }
    }

    /// <summary>The page's own records.</summary>
    private static partial class PageServiceLog
    {
        // Corrected 2026-10-12 by the reader's pass (previously "The page listener on port
        // {Port} stopped: no tab was left and the linger ran out."): since 69 d the listener
        // stops only when the background ends (TryStopAsync), so no linger ends it.
        [LoggerMessage(EventId = 7011, Level = LogLevel.Information, Message = "The page listener on port {Port} stopped with the background.")]
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

        // 69 b, 2026-10-11.
        [LoggerMessage(EventId = 7018, Level = LogLevel.Warning, Message = "The page's last port could not be read, so its listener takes a port Windows picks.")]
        public static partial void MemoryUnread(ILogger logger, Exception failure);

        [LoggerMessage(EventId = 7019, Level = LogLevel.Warning, Message = "The page's port could not be written down, so the next background may listen on another port, where a browser asks again before opening BrowserAI.")]
        public static partial void MemoryUnwritten(ILogger logger, Exception failure);

        [LoggerMessage(EventId = 7020, Level = LogLevel.Warning, Message = "What BrowserAI's processes use could not be read for the page, so it shows the last reading.")]
        public static partial void UsageUnread(ILogger logger, Exception failure);

        // 7022 since 2026-10-12 (previously 7021, which DesktopPageHost's DesktopLog.Failed already
        // holds in the same category, BrowserAI.Background; ProcessLogTests'
        // EveryLineOfTheBackgroundsAndTheRelaysCategoriesHasAnEventIdOfItsOwn named the two).
        [LoggerMessage(EventId = 7022, Level = LogLevel.Warning, Message = "The after-update notice's record could not be written, so the next start may not know whether the notice was seen.")]
        public static partial void NoticeUnwritten(ILogger logger, Exception failure);
    }
}
