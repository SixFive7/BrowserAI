// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Logging;
using BrowserAI.Proxy;
using BrowserAI.Runtime;
using BrowserAI.Storage;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Sessions;

/// <summary>
/// One session this process is driving: the directory it owns, the lock that
/// proves it, its own child, and its own log.
/// </summary>
/// <remarks>
/// <para>
/// <b>One job object per child, never one shared job.</b> A shared job fuses
/// every session's process tree together, so tearing down one session would kill
/// them all -- and assigning BrowserAI itself would make it a casualty of its own
/// cleanup. The job lives inside <see cref="ChildConnection"/>'s transport, which
/// is per session by construction.
/// </para>
/// <para>
/// <b>Disposal releases the directory and leaves the record.</b> The holder
/// record outliving the holder is what makes a stale lock a sentence -- <i>"held
/// by PID 1234 since 14:02, no longer running -- reclaiming"</i> -- and not a
/// refusal, and reclaim is forever, so a torn-down session stays resumable
/// against its directory indefinitely.
/// </para>
/// </remarks>
internal sealed class LiveSession : IAsyncDisposable, IVisibleWindowOwner
{
    /// <param name="location">The canonicalised session directory.</param>
    /// <param name="sessionLock">The held lock. This object owns it.</param>
    /// <param name="browsersClaim">
    /// The <b>shared</b> claim on the machine's browsers root, taken by <c>init</c>
    /// or <c>resume</c> before anything else and held for this session's whole life.
    /// This object owns it. See <see cref="Runtime.MaintenanceLock"/>: it is what
    /// makes <c>browserai_reinstall_browser</c>'s exclusive open fail while this
    /// session exists, whatever browser family it uses.
    /// </param>
    /// <param name="child">The child driving this session. This object owns it.</param>
    /// <param name="settings">The per-run arguments that child was launched with.</param>
    /// <param name="logging">This session's own logging stack. This object owns it.</param>
    /// <param name="config">The config the child was started with.</param>
    /// <param name="configFile">Where that config was written.</param>
    /// <param name="createdHere">Whether this connection is the one that created the session.</param>
    /// <param name="environment">
    /// Where the idle period, the clock and the question <i>is a browser up</i>
    /// come from. The product's own values in the product; the suite's in a rig.
    /// </param>
    /// <param name="reap">
    /// Playwright's own registry reaper, started detached whenever a close here
    /// really put a browser tree down. This object does not own it: one reap
    /// serves every session in the process, because the registry it prunes is
    /// machine-wide and so is its log.
    /// </param>
    /// <param name="browserExecutable">
    /// The browser's absolute executable path, which <see cref="WatchTheBrowser"/>
    /// finds the browser's main process by, or <see langword="null"/> when this build
    /// does not provision the family.
    /// </param>
    public LiveSession(
        SessionPath location,
        SessionLock sessionLock,
        MaintenanceLock browsersClaim,
        ChildConnection child,
        SessionRunSettings settings,
        SessionLogging logging,
        GeneratedConfig config,
        string configFile,
        bool createdHere,
        SessionEnvironment environment,
        ServerRegistryReap reap,
        string? browserExecutable = null)
    {
        ArgumentNullException.ThrowIfNull(logging);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(reap);

        Location = location;
        Lock = sessionLock;
        BrowsersClaim = browsersClaim;
        Child = child;
        Settings = settings;
        Logging = logging;
        Config = config;
        ConfigFile = configFile;
        CreatedHere = createdHere;
        _reap = reap;
        _browserIsOpen = environment.BrowserIsOpen;
        _watchTheBrowser = environment.WatchTheBrowser;
        BrowserExecutable = browserExecutable;
        _clock = environment.Clock;
        _cutShortToken = _cutShort.Token;
        Logger = logging.Factory.CreateLogger<LiveSession>();

        // ⚠️ A TIMER FOR EVERY SESSION BUT ONE SET TO NEVER, since 2026-10-08, E2.
        // Until that day there was no timer at all for a headed session, Q326 a, the
        // maintainer's words of 2026-10-03, verbatim: "Q326 a - the timer is there
        // to conserve system resources the user cannot see. Also, interactive
        // windows mostly hold user state so they are super valuable." His E2 of
        // 2026-10-07 reverses it: "What if we change the never to 1 hour and then
        // allow the calling agent to change this default behaviour with a
        // parameter? This would allow critical interactive user processes to remain
        // indefinite but have a sensical timeout default for interactive sessions
        // that can be restarted." So a visible window gets an hour unless the agent
        // says otherwise, and "never" is the agent's to say, for either mode. A
        // session set to never still has no timer at all, for the reason Q326 a's
        // headed session had none: an object that does not exist cannot be armed by
        // a call nobody thought about.
        _inputWatch = settings.Headed ? environment.InputWatch : null;

        Idle = environment.IdlePeriodFor(settings.Idle) is { } period
            ? new BrowserIdleTimer(
                location.FullPath,
                period,
                FireIdleAsync,
                logging.Factory.CreateLogger<BrowserIdleTimer>(),
                environment.Clock,

                // F4: a visible window's input is read once more before its countdown
                // decides it has run out.
                _inputWatch is { } inputs ? inputs.Tick : null)
            : null;

        _lastActivity = _clock.GetUtcNow().UtcTicks;
    }

    /// <summary>
    /// How often a headed session whose client went is asked whether its browser
    /// is still up: <b>15 s</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A look and not a close.</b> Q326 a keeps a headed session out of the idle
    /// timer, because its window holds what a person was doing. Once its client has
    /// gone nothing drives it, so the session host keeps it for as long as the
    /// window is open and lets it go once the person has closed it, which this
    /// notices. Asking is one membership read of the child's job.
    /// </para>
    /// <para>
    /// <b>The price of the period</b> is a closed window's node child, about 50 MB, held
    /// for at most one look. It is a seam for the suite through the session
    /// environment's clock, as the idle period is. ⚠️ <i>Corrected 2026-10-08
    /// (previously "One cadence with the host's ... the session host's own look,
    /// SessionHostServer.LingerLook", a quarter of the host's minute-long linger)</i>:
    /// the session host went with the coordinator when the one resident background
    /// took both their places, and the background never ends on its own (S a), so
    /// there is no linger to keep in step with. The period stays the 15 s it was.
    /// </para>
    /// <para>
    /// ⚠️ <i>Corrected 2026-10-10 by addition: since 2026-10-08, E2, a headed session is
    /// not out of the idle timer. It has a countdown of an hour unless the agent set
    /// another time, and only a session set to never has none. This look still lets a
    /// session go once the person has closed its window, whatever its countdown.</i>
    /// </para>
    /// </remarks>
    public static TimeSpan DetachedWindowLook { get; } = SessionTimes.DetachedWindowLook;

    /// <summary>
    /// Which connection drives this session now, and whether the host is letting it go.
    /// </summary>
    /// <remarks>
    /// <b>Its own lock and not <see cref="SessionLock"/>'s</b>: a claim is asked on
    /// every forwarded call, and it decides nothing the record holds.
    /// </remarks>
    private readonly Lock _attachment = new();

    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private CallerConnection? _attachedTo;
    private bool _releasing;
    private ITimer? _windowWatch;
    private Func<LiveSession, Task>? _afterIdle;

    /// <summary>
    /// Completes once the session host has let this session go, after
    /// <see cref="TryBeginRelease"/> answered <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <b>What a resume waits for when it meets <see cref="SessionClaim.Releasing"/></b>:
    /// the lock is this process's own until the release has finished, so the resume
    /// opens the directory again only after it.
    /// </remarks>
    public Task Released => _released.Task;

    /// <summary>Records that the release <see cref="TryBeginRelease"/> began has finished.</summary>
    public void ReleaseFinished() => _ = _released.TrySetResult();

    /// <summary>
    /// The connection this session is attached to, or <see langword="null"/> once its
    /// client has gone.
    /// </summary>
    public CallerConnection? AttachedTo
    {
        get
        {
            lock (_attachment)
            {
                return _attachedTo;
            }
        }
    }

    /// <summary>Whether this session's client has gone and nothing has taken it over yet.</summary>
    public bool IsDetached
    {
        get
        {
            lock (_attachment)
            {
                return _attachedTo is null && !_releasing;
            }
        }
    }

    /// <summary>
    /// Attaches this session to the connection that opened it.
    /// </summary>
    /// <param name="connection">The connection.</param>
    public void AttachTo(CallerConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        lock (_attachment)
        {
            _attachedTo = connection;
        }
    }

    /// <summary>
    /// Asks whether a connection may drive this session, and takes it over for that
    /// connection when its own client has gone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q366 b: a relaunched client reconnects to the session the host kept</b>
    /// <i>"through browserai_resume or its next call"</i>, which is this. A session
    /// whose connection has ended is taken over by the next connection that names it,
    /// and its browser never closed, so there is nothing to restore.
    /// </para>
    /// <para>
    /// <b>A session another open connection drives is refused</b>, as a directory
    /// another process holds is refused by its lock. A session the host is already
    /// letting go answers <see cref="SessionClaim.Releasing"/>, and the caller is told
    /// what a session nobody holds is told.
    /// </para>
    /// </remarks>
    /// <param name="connection">The connection naming it.</param>
    /// <param name="holder">The connection driving it, when the answer is <see cref="SessionClaim.HeldElsewhere"/>.</param>
    /// <returns>Whether the connection may go ahead, and how.</returns>
    public SessionClaim Claim(CallerConnection connection, out CallerConnection? holder)
    {
        ArgumentNullException.ThrowIfNull(connection);

        ITimer? watch;

        lock (_attachment)
        {
            holder = null;

            if (_releasing)
            {
                return SessionClaim.Releasing;
            }

            if (ReferenceEquals(_attachedTo, connection))
            {
                return SessionClaim.Attached;
            }

            if (_attachedTo is { IsOpen: true } other)
            {
                holder = other;
                return SessionClaim.HeldElsewhere;
            }

            _attachedTo = connection;
            watch = _windowWatch;
            _windowWatch = null;
        }

        watch?.Dispose();

        return SessionClaim.TakenOver;
    }

    /// <summary>
    /// Detaches this session from a connection that has ended, when it is the one
    /// attached.
    /// </summary>
    /// <param name="connection">The connection that ended.</param>
    /// <returns>Whether this session was attached to it and is detached now.</returns>
    public bool DetachFrom(CallerConnection connection)
    {
        lock (_attachment)
        {
            if (!ReferenceEquals(_attachedTo, connection))
            {
                return false;
            }

            _attachedTo = null;
            return true;
        }
    }

    /// <summary>
    /// Marks a detached session as being let go, so no connection can take it over
    /// from here on.
    /// </summary>
    /// <returns>Whether it was detached and is now this caller's to release.</returns>
    public bool TryBeginRelease()
    {
        ITimer? watch;

        lock (_attachment)
        {
            if (_attachedTo is not null || _releasing)
            {
                return false;
            }

            _releasing = true;
            watch = _windowWatch;
            _windowWatch = null;
        }

        watch?.Dispose();

        return true;
    }

    /// <summary>
    /// What runs once the idle timer has fired, whatever it found: the session host's
    /// release of a session whose client went. Set once, by the manager that holds
    /// this session.
    /// </summary>
    /// <param name="afterIdle">The callback.</param>
    public void WhenIdleFires(Func<LiveSession, Task> afterIdle) => Volatile.Write(ref _afterIdle, afterIdle);

    /// <summary>
    /// Looks every <see cref="DetachedWindowLook"/> whether a detached headed session's
    /// browser is still up, and once it is not, hands the session to
    /// <paramref name="whenGone"/>.
    /// </summary>
    /// <remarks>
    /// <b>Stopped by a claim and by a release</b>, so a session a client takes over is
    /// never let go behind it.
    /// </remarks>
    /// <param name="whenGone">The release.</param>
    public void WatchTheWindowWhileDetached(Func<LiveSession, Task> whenGone)
    {
        ArgumentNullException.ThrowIfNull(whenGone);

        var timer = _clock.CreateTimer(
            _ =>
            {
                if (!IsDetached || BrowserIsOpen)
                {
                    return;
                }

                StopWatching();
                _ = Task.Run(() => whenGone(this));
            },
            null,
            DetachedWindowLook,
            DetachedWindowLook);

        ITimer? previous;

        lock (_attachment)
        {
            if (!(_attachedTo is null && !_releasing))
            {
                previous = timer;
            }
            else
            {
                previous = _windowWatch;
                _windowWatch = timer;
            }
        }

        previous?.Dispose();
    }

    private void StopWatching()
    {
        ITimer? watch;

        lock (_attachment)
        {
            watch = _windowWatch;
            _windowWatch = null;
        }

        watch?.Dispose();
    }

    /// <summary>The idle timer's close, and then whatever the host does about a session whose client went.</summary>
    /// <remarks>
    /// <b>The callback is started and not awaited</b>: a release tears this session
    /// down, and the teardown waits for the very close this runs inside.
    /// </remarks>
    /// <returns>What the close did.</returns>
    private async Task<BrowserCloseResult?> FireIdleAsync()
    {
        try
        {
            return await CloseForIdleAsync().ConfigureAwait(false);
        }
        finally
        {
            if (Volatile.Read(ref _afterIdle) is { } after)
            {
                _ = Task.Run(() => after(this), CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Upstream's own tool, spelled as upstream spells it.
    /// </summary>
    /// <remarks>
    /// <b>It is not a schema and it is not a rename.</b> The scope boundary
    /// forbids authoring a tool definition in C#; this is a <i>call</i> to a tool
    /// the child already advertises, whose name passes through byte for byte
    /// everywhere else. It is checked against the committed
    /// <c>upstream-snapshots/tools-list.json</c> by the suite, so an upstream
    /// rename turns the build red instead of turning the timer into a no-op.
    /// </remarks>
    public const string BrowserCloseTool = "browser_close";

    /// <summary>
    /// The <c>why</c> the idle close records, which is BrowserAI's own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>It has to be self-attributing, because every other row in the log
    /// was written by a caller.</b> <c>browserai_catch_up</c> presents the log as
    /// <i>"WHAT WAS DONE HERE -- the session's own log ... This is what BrowserAI
    /// did"</i>, and a reader meeting a <c>browser_close</c> with a caller-shaped
    /// sentence beside it would reasonably conclude an agent had closed the
    /// browser. This one names the timer, so the row reads as the only thing in
    /// the log nobody asked for.
    /// </para>
    /// <para>
    /// <b>The period is deliberately not interpolated.</b> It is a seam the suite
    /// drives in milliseconds, so quoting it would put a test-shaped number into
    /// a model-facing record on every close -- and the fact a reader needs is
    /// <i>why there is a gap here</i>, which the sentence carries without it.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-10-03 (previously "...so the browser tree was released
    /// and the node child kept. Nothing was lost -- the next call relaunches the
    /// browser and answers normally.").</b> Both halves stopped being true. The
    /// field report of 2026-10-01 showed the next call running on
    /// <c>about:blank</c>, and the 2026-10-03 measurement showed pages, tabs,
    /// <c>sessionStorage</c>, typed text and session cookies gone after that
    /// close. And under P4 b the close ends the whole child and the next call is
    /// refused until <c>browserai_resume</c>, whose restore is what brings the tabs
    /// back. So the row says what happened and what the reader of the log does
    /// next, and nothing about what was kept that a measurement would have to
    /// back.
    /// </para>
    /// </remarks>
    public const string IdleCloseWhy =
        // The texts polish, 2026-10-10, page #224 (previously ", and in a visible window
        // nobody had used it,"), which read as if every session had a window.
        "BrowserAI closed this session's browser itself: no call had named the session for its idle period and, in a visible window, nobody had typed or clicked in it, "
        + "so it ended the browser server, node child included. Every browser call is refused until browserai_resume starts a new one, "
        + "and the browser's own session restore then reopens the tabs that were open.";

    // ⚠️ Corrected 2026-10-08 (previously "nothing had been forwarded through the
    // session for the idle period"): since E2 and F2 every call that names the
    // session restarts the countdown, and in a visible window the person's input does
    // too (F4).

    // ⚠️ DELETED 2026-10-08: `NothingWasOpenToClose`, what a caller's own
    // `browser_close` was answered with when no browser was up -- "No browser was
    // open in this session, so there was nothing to close and none was started in
    // order to close it. The session is unchanged: the next browser call starts a
    // browser as usual." F1 a denies `browser_close` at the door, and
    // `browserai_close` with no browser up closes the session all the same
    // (`SessionManager.ClosedWithNoBrowserUp`), still without sending a close: one
    // with no browser up starts a browser in order to close it, measured 2026-10-03
    // at 8 to 9 browser processes, a capture rewritten empty and a registry
    // descriptor nothing reaps.

    // ⚠️ RETIRED 2026-10-04: `ShutdownCloseBudget`, one second, and
    // `IdleCloseBudget`, thirty seconds, stood here. D4.1 and D4.2, the
    // maintainer's words verbatim: "Make it a roomy 1 min. We want everything
    // nicely saved to disk even on a slow system." and "Same 1 min. under option
    // d (lane c)". Every clean close takes `SessionTimes.BrowserCloseCap` since,
    // one cap with its derivation beside it, and neither name exists any more.

    private readonly ServerRegistryReap _reap;
    private readonly Func<ChildConnection, bool> _browserIsOpen;
    private readonly Func<ChildConnection, string?, Action<int?>, IDisposable?> _watchTheBrowser;
    private readonly TimeProvider _clock;

    /// <summary>The wait on the browser's main process, once one is up and found.</summary>
    private IDisposable? _browserWatch;

    /// <summary>1 once a forwarded call has left a browser up in this session.</summary>
    private int _browserSeenUp;

    /// <summary>1 while the last forwarded call was a <c>browser_tabs</c> close.</summary>
    private int _lastCallClosedATab;

    /// <summary>1 once a browser server that ended on its own has been recorded.</summary>
    private int _serverEndRecorded;

    /// <summary>What runs once the browser ended without BrowserAI closing it: the session host's release of a session nobody drives.</summary>
    private Func<LiveSession, Task>? _afterTheBrowserEnded;

    /// <summary>Guards <see cref="_closesInFlight"/>.</summary>
    private readonly Lock _closing = new();

    /// <summary>
    /// Every clean close in flight on this session: the idle close's, the caller's
    /// own and the shutdown's, each until the browser has answered or the cap has run
    /// out. Guarded by <see cref="_closing"/>.
    /// </summary>
    private readonly List<Task> _closesInFlight = [];

    /// <summary>Cancelled by a destroy, the one thing that may cut a close short.</summary>
    private readonly CancellationTokenSource _cutShort = new();

    /// <summary>
    /// <see cref="_cutShort"/>'s token, read once while the source is alive, so a
    /// waiter that asks after a teardown has disposed it registers nothing and throws
    /// nothing.
    /// </summary>
    private readonly CancellationToken _cutShortToken;

    private SessionClosure? _closed;
    private long _lastActivity;

    /// <summary>The process's check of the person's input, for a visible session; <see langword="null"/> otherwise.</summary>
    private readonly VisibleInputWatch? _inputWatch;

    /// <summary>This session's registration with <see cref="_inputWatch"/>, once its browser is watched.</summary>
    private IDisposable? _inputRegistration;
    private int _reapOwed;
    private int _disposed;

    /// <summary>The canonicalised session directory. It is the identity.</summary>
    public SessionPath Location { get; }

    /// <summary>The held lock, and the record inside it.</summary>
    public SessionLock Lock { get; }

    /// <summary>
    /// The shared claim on the machine's browsers root, held for this session's
    /// whole life.
    /// </summary>
    /// <remarks>
    /// <b>It is not read for anything and that is the point.</b> Its existence
    /// as an open handle is the whole mechanism: a reinstall's exclusive open is
    /// refused by the kernel while it lives, and the kernel releases it however
    /// this process dies.
    /// </remarks>
    public MaintenanceLock BrowsersClaim { get; }

    /// <summary>The <c>@playwright/mcp</c> child driving it, for this object's whole life.</summary>
    /// <remarks>
    /// ⚠️ <b>One child per <see cref="LiveSession"/> again since 2026-10-03
    /// (previously replaceable, through a <c>ReplaceChildAsync</c> that a resume
    /// meeting a dead child used from 2026-09-17).</b> A resume that needs a new
    /// child now tears this whole object down and opens the session again, which
    /// is also what applies a new run's settings, so there is no swap left to get
    /// wrong.
    /// </remarks>
    public ChildConnection Child { get; }

    /// <summary>The browser's absolute executable path, or <see langword="null"/>.</summary>
    public string? BrowserExecutable { get; }

    /// <summary>The per-run arguments this session's child was launched with.</summary>
    /// <remarks>
    /// <b>Kept so a resume can compare and not guess.</b> Q324 a: while a browser
    /// is up a resume applies nothing, and refuses a setting it was asked for that
    /// differs from the one in use, naming both.
    /// </remarks>
    public SessionRunSettings Settings { get; }

    /// <summary>
    /// How this session's browser was closed, or <see langword="null"/> while it
    /// has not been.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>P2 a and P3 b, the maintainer's words of 2026-10-03, verbatim: "p2 a /
    /// p3 b".</b> After the idle close or the caller's own <c>browser_close</c>,
    /// every forwarded call is refused until <c>browserai_resume</c>, and the
    /// refusal says why and what to do. It is never cleared on this object:
    /// the resume that ends it replaces the object.
    /// </para>
    /// <para>
    /// ⚠️ <b>Set before anything is closed, and that ordering is what makes the
    /// armed-close wedge recoverable.</b> A <c>browser_close</c> that meets an
    /// armed debugger pause never answers and leaves every page tool failing; the
    /// session is already closed by then, so the next call is refused with a
    /// sentence naming <c>browserai_resume</c>, and the resume ends that child
    /// through its stdin, which a paused child still obeys.
    /// </para>
    /// <para>
    /// ⚠️ <b>The resume waits for the close first since 2026-10-04</b> (previously it
    /// ended the child the moment it arrived). The maintainer's warning of that day,
    /// verbatim: <i>"Just thinking about it, if we were to resume within that close
    /// window we will need to handle atomicity and orderign correctly. Beware when
    /// building lane c."</i> A child ended through its stdin while its browser is
    /// closing is one <c>@playwright/mcp</c> force-kills about 1 ms into its own
    /// graceful close, so the resume, a release and a shutdown all wait for
    /// <see cref="WaitForTheCloseInFlightAsync"/>, which the cap bounds; a call that
    /// arrives meanwhile is refused at once, as it always was.
    /// </para>
    /// </remarks>
    public SessionClosure? Closed => Volatile.Read(ref _closed);

    /// <summary>
    /// Whether a clean close is still in flight on this session: sent, and neither
    /// answered nor out of its cap.
    /// </summary>
    public bool CloseIsInFlight
    {
        get
        {
            lock (_closing)
            {
                return _closesInFlight.Exists(close => !close.IsCompleted);
            }
        }
    }

    /// <summary>
    /// Waits until every clean close in flight on this session has finished: the
    /// browser has answered, or the cap has run out and the close has gone on to end
    /// the child. Returns at once when none is in flight, or once a destroy has cut
    /// them short.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The ordering rule of 2026-10-04: nothing may cut a clean close short.</b> A
    /// resume, a takeover of a kept session that goes on to reopen it, the session
    /// host letting a session go and a shutdown all call this before they end the
    /// child. A <c>browserai_destroy</c> does not wait: it deletes the profile the
    /// close would save (<see cref="CutTheCloseShort"/>).
    /// </para>
    /// <para>
    /// <b>Bounded by construction, and by the one cap.</b> The idle close and the
    /// shutdown's close each wait for their answer for at most
    /// <see cref="SessionTimes.BrowserCloseCap"/> and then end the child, and the
    /// caller's own close is let go of at the same cap from when it was sent. So this
    /// never waits longer than the cap and a child's end through its stdin.
    /// </para>
    /// </remarks>
    /// <returns>A task that completes once nothing is closing.</returns>
    public async Task WaitForTheCloseInFlightAsync()
    {
        Task[] closes;

        lock (_closing)
        {
            closes = [.. _closesInFlight];
        }

        if (closes.Length is 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(closes).WaitAsync(_cutShortToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cutShortToken.IsCancellationRequested)
        {
            // A destroy cut the close short; it ends the child itself.
        }
    }

    /// <summary>
    /// Lets a destroy end whatever clean close is in flight without waiting for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Decided 2026-10-04 for the maintainer's review</b>: the ordering rule holds
    /// for every way a session is reopened, let go or shut down, and a destroy is the
    /// one exception. It deletes the profile the close would flush, so waiting up to
    /// the cap would keep its caller waiting for nothing that survives.
    /// </para>
    /// <para>
    /// <b>The idle close and the shutdown's close end their wait at once</b>, the child
    /// is told the close was cancelled, and it is ended through its stdin. The
    /// caller's own close is not waited for, and the child ending under it answers the
    /// caller with the child's failure to answer, as before.
    /// </para>
    /// </remarks>
    public void CutTheCloseShort()
    {
        if (CloseIsInFlight && !_cutShortToken.IsCancellationRequested)
        {
            IdleLog.CloseCutShortByDestroy(Logger, Location.FullPath);
        }

        try
        {
            _cutShort.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The teardown has finished, and with it every close.
        }
    }

    /// <summary>Records one clean close as in flight, before its request is sent.</summary>
    /// <returns>What the close completes once it has finished.</returns>
    private TaskCompletionSource BeginTheCloseInFlight()
    {
        var close = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_closing)
        {
            _ = _closesInFlight.RemoveAll(finished => finished.IsCompleted);
            _closesInFlight.Add(close.Task);
        }

        return close;
    }

    /// <summary>
    /// A logger writing into this session's own log, built once.
    /// </summary>
    /// <remarks>
    /// <b>Cached, not created per call.</b> Every session-scoped call
    /// writes its <c>why</c> here, so this is on the hot path of the whole
    /// proxy; <c>CreateLogger</c> allocates and takes the factory's lock.
    /// </remarks>
    public ILogger Logger { get; }

    /// <summary>This session's own log file and level.</summary>
    public SessionLogging Logging { get; }

    /// <summary>The config the child was started with, and every opinion in it.</summary>
    public GeneratedConfig Config { get; }

    /// <summary>Where that config was written.</summary>
    public string ConfigFile { get; }

    /// <summary>
    /// Whether <b>this</b> connection created the session, as opposed to
    /// resuming one somebody else made.
    /// </summary>
    /// <remarks>
    /// There is no bearer token, so this is what recovers the guarantee a minted
    /// handle was going to provide: a caller driving a session it did not create
    /// is told so, at first use, and not at reclaim time.
    /// </remarks>
    public bool CreatedHere { get; }

    /// <summary>Whether the notice about driving somebody else's session has been given.</summary>
    public bool NoticeGiven { get; set; }

    /// <summary>
    /// The one timer: this session's browser server is ended once nothing has
    /// driven it for <see cref="BrowserIdleTimer.Period"/>. <see langword="null"/>
    /// when the session's idle setting is never.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It belongs to this lifetime and not to the manager because everything
    /// it acts on does: one session is one child, one job and one log, and a
    /// timer owned anywhere else would need a way to name a session that has
    /// already gone.
    /// </para>
    /// <para>
    /// ⚠️ <i>Corrected 2026-10-08 (previously "<see langword="null"/> for a headed
    /// session, which is never idle-closed"), E2</i>: a visible window has one too.
    /// </para>
    /// </remarks>
    public BrowserIdleTimer? Idle { get; }

    /// <summary>
    /// When a call last named this session, or the person last used its window, as a
    /// moment on the session's clock.
    /// </summary>
    public DateTimeOffset LastActivity => new(Volatile.Read(ref _lastActivity), TimeSpan.Zero);

    /// <summary>
    /// A call named this session, or the person used its window: the moment is
    /// noted and the countdown starts again, whatever the call is answered with.
    /// </summary>
    /// <remarks>
    /// <b>F2, the maintainer's words of 2026-10-08 verbatim:</b> <i>"Also, any type of
    /// call, even if refused once because the settings are different should reset the
    /// countdown timer on live sessions."</i> A closed session has no browser to keep,
    /// and its countdown is over; noting the call costs it nothing.
    /// </remarks>
    public void NoteActivity()
    {
        Volatile.Write(ref _lastActivity, _clock.GetUtcNow().UtcTicks);
        Idle?.Touch();
    }

    /// <summary>
    /// Marks one forwarded call as driving this session, so its countdown cannot run
    /// out under the call, and notes the activity at both ends of it.
    /// </summary>
    /// <returns>A scope to dispose when the call is answered.</returns>
    public IDisposable Driving()
    {
        Volatile.Write(ref _lastActivity, _clock.GetUtcNow().UtcTicks);

        return new DrivingScope(this);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>The browser's main process, while the session is open</b>: the pid
    /// <see cref="BrowserExitWatch"/> found inside the session's own job and holds open,
    /// and <see langword="null"/> once the session is closed or before a browser is
    /// watched, which no window is ever matched against.
    /// </remarks>
    int? IVisibleWindowOwner.WindowProcessId =>
        Closed is null && Volatile.Read(ref _browserWatch) is IWatchedBrowser watched ? watched.ProcessId : null;

    /// <inheritdoc />
    /// <remarks>
    /// <b>F4, decided 2026-10-08 by the maintainer, in his words verbatim:</b> <i>"f4 a -
    /// but only if this is easy."</i>, then <i>"Make sure the keyboard and mouse input
    /// check does not lag the system."</i> and <i>"Make sure these reads happen at the
    /// same pace regardless of how many visible windows there are."</i> The person's
    /// keyboard or mouse input in this session's window, while it is in front, is
    /// activity, as a call that names the session is.
    /// </remarks>
    void IVisibleWindowOwner.PersonWasActive() => NoteActivity();

    /// <summary>Leaves the visible-input check, when this session had joined it.</summary>
    private void StopWatchingInput() => Interlocked.Exchange(ref _inputRegistration, null)?.Dispose();

    /// <summary>
    /// This session's idle countdown for the update and the dashboard, or
    /// <see langword="null"/> once it is closed and holds nothing.
    /// </summary>
    /// <remarks>
    /// <b>The seam ARCH and UI read, added 2026-10-08</b>: per open session, its
    /// directory, its purpose, whether it has a window, and the deadline of its
    /// countdown, <see langword="null"/> exactly when the agent set it to never. Read
    /// from memory and never from the store, so a reader once a second waits on
    /// nothing.
    /// </remarks>
    /// <returns>The countdown, or <see langword="null"/>.</returns>
    public SessionCountdown? Countdown()
    {
        if (Closed is not null)
        {
            return null;
        }

        return new SessionCountdown(
            Location.FullPath,
            Lock.Record.Purpose is { Length: > 0 } purpose ? purpose : null,
            Settings.Headed,
            Idle?.CountdownEndsAt,
            LastActivity,
            BrowserIsOpen);
    }

    /// <summary>The forwarded call's scope: the countdown held, and the activity noted when it ends.</summary>
    /// <param name="session">The session.</param>
    private sealed class DrivingScope(LiveSession session) : IDisposable
    {
        /// <summary>The countdown's own scope, or <see langword="null"/> for a session set to never.</summary>
        private readonly IDisposable? _call = session.Idle?.Call();

        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) is not 0)
            {
                return;
            }

            Volatile.Write(ref session._lastActivity, session._clock.GetUtcNow().UtcTicks);
            _call?.Dispose();
        }
    }

    /// <summary>
    /// Whether this session's browser server has a browser up: anything in the
    /// child's job beyond the child's own processes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The predicate the idle close, the teardown, a resume and the server's
    /// pipe all read</b>, so they agree on what <i>a browser is open</i> means. It
    /// is <see cref="SessionEnvironment.BrowserIsOpen"/>, which asks the kernel for
    /// the job's members in the product and asks a double in the in-process rig.
    /// </para>
    /// <para>
    /// <b>A job that cannot be read answers <see langword="false"/></b>, because
    /// the one reader that asks from outside the session's own lifetime -- a
    /// description on the server's pipe -- must never fail over it: a child torn
    /// down between the question and the answer has no browser, which is the
    /// truth by the time anybody reads it.
    /// </para>
    /// </remarks>
    public bool BrowserIsOpen
    {
        get
        {
            try
            {
                return _browserIsOpen(Child);
            }
            catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or ObjectDisposedException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// <c>browserai_close</c>: marks this session closed by the agent, records why,
    /// asks the browser to close itself, and ends the child once the browser has
    /// answered or the one close cap has run out, in that order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>F1 a, decided 2026-10-08 by the maintainer</b>: one tool of BrowserAI's own
    /// ends a session's browser and keeps the session, and Playwright's
    /// <c>browser_close</c> is denied. <i>Previously <c>SendTheCallersCloseAsync</c> and
    /// <c>EndTheChildAfterTheCallersCloseAsync</c>, which sent the caller's own
    /// <c>browser_close</c> on to the child (P3 b, 2026-10-03) and ended the child once
    /// the proxy had the answer.</i> What carried over unchanged is every rule those
    /// two kept: the session is closed and the close recorded before anything is sent,
    /// so a close that never answers still leaves a session the resume recovers
    /// (<see cref="Closed"/>); the close is in flight from the moment it is sent, so a
    /// resume, a release and a shutdown wait for it; and the close is sent with no
    /// token of the caller's, so a caller that stops waiting leaves it to finish and
    /// the child is ended once it is over.
    /// </para>
    /// <para>
    /// <b>What changed is the cap's reach.</b> The caller's own close was let go of at
    /// the cap for whoever waited behind it, and the caller itself went on waiting for
    /// an answer a wedged browser never gave. This answers its caller at the cap too:
    /// the close is BrowserAI's, so BrowserAI says how it ended.
    /// </para>
    /// <para>
    /// <b>With no browser up nothing is sent</b>, because a <c>browser_close</c> with no
    /// browser up starts one in order to close it (measured 2026-10-03), and the child
    /// is ended all the same: after <c>browserai_close</c> a session is closed whatever
    /// it held. A decision taken 2026-10-08 for the maintainer's review.
    /// </para>
    /// </remarks>
    /// <param name="by">The connection the close arrived on, which the reason names.</param>
    /// <param name="why">What the call gave as its reason.</param>
    /// <param name="cancellationToken">The caller's token: it ends the caller's wait and never the close.</param>
    /// <param name="cause">
    /// Why it is closed: <see cref="SessionCloseCause.Caller"/> for <c>browserai_close</c>,
    /// and <see cref="SessionCloseCause.SettingsChanged"/> for a resume that closes the
    /// browser to open it with other settings, F1 a and F2 d of 2026-10-08. The close is
    /// the same either way; only the reason recorded differs.
    /// </param>
    /// <returns>How the close ended.</returns>
    public async Task<AgentClose> CloseForTheAgentAsync(CallerConnection by, string why, CancellationToken cancellationToken, SessionCloseCause cause = SessionCloseCause.Caller)
    {
        ArgumentNullException.ThrowIfNull(by);

        var closure = new SessionClosure(cause, _clock.GetUtcNow(), Idle?.Period)
        {
            ClosedBy = by,

            // The texts polish, 2026-10-10: what a model reads back, so the conversation's
            // client and folder and never a pid.
            By = by.Conversation(),
            Why = why,
        };

        // 8 b: the reason is recorded, and its row is the call's own.
        if (Interlocked.CompareExchange(ref _closed, closure, null) is not null)
        {
            return AgentClose.AlreadyClosed;
        }

        Record(closure, writeRow: false);

        if (!BrowserIsOpen)
        {
            await Child.DisposeAsync().ConfigureAwait(false);
            return AgentClose.NothingWasOpen;
        }

        // The reap is owed now, for the reason the shutdown's close gives: the child
        // is ended below, and a teardown that finds no job starts no reap.
        if (TheJobHoldsABrowser())
        {
            Volatile.Write(ref _reapOwed, 1);
        }

        var finished = BeginTheCloseInFlight();

        var asked = Child.AskAsync(
            RequestMethods.ToolsCall,
            new JsonObject
            {
                ["name"] = BrowserCloseTool,
                ["arguments"] = new JsonObject(),
            },
            CancellationToken.None);

        // Observed, so a close the child never answers, which faults once the child is
        // ended, is not left as an exception nobody read.
        _ = asked.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        // What decides the answer is how the wait ended and not whether the close has
        // ended by the time this reads it: a resume waiting behind the same close can
        // end the child the moment the cap runs out, and the close then faults, which
        // would read as answered.
        var answeredInTime = LetTheAgentsCloseGoAtTheCapAsync(asked, finished);

        bool answered;

        try
        {
            answered = await answeredInTime.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller stopped waiting; the close goes on, and the child is ended
            // once it is over, off the caller's own request.
            IdleLog.CallerLeftItsClose(Logger, Location.FullPath, SessionTimes.BrowserCloseCap);
            _ = EndTheChildOnceTheCloseIsOverAsync();
            throw;
        }

        await Child.DisposeAsync().ConfigureAwait(false);

        return answered ? AgentClose.Closed : AgentClose.CapRanOut;
    }

    /// <summary>Ends the child once every close in flight is over, for a caller that did not wait.</summary>
    /// <returns>The end.</returns>
    private async Task EndTheChildOnceTheCloseIsOverAsync()
    {
        try
        {
            await WaitForTheCloseInFlightAsync().ConfigureAwait(false);
            await Child.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is ObjectDisposedException or InvalidOperationException or IOException)
        {
            // A teardown got there first; it waits for the same close and ends the same child.
        }
    }

    /// <summary>
    /// Sends this session's browser its own <c>browser_close</c>, capped, so a
    /// shutdown lets the browser flush before the child is ended.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>e2 of P7. Only where there is a browser to close and nothing has closed
    /// it already</b>: a closed session's browser is gone or wedged, and a
    /// <c>browser_close</c> with no browser up starts one in order to close it. Nor
    /// where a close is already in flight, which the teardown waits for instead.
    /// </para>
    /// <para>
    /// <b>It writes no row.</b> A teardown has never written one, and a shutdown
    /// closes every session at once, which is not the moment to add a database
    /// write to each.
    /// </para>
    /// <para>
    /// ⚠️ <b>The cap is <see cref="SessionTimes.BrowserCloseCap"/> since 2026-10-04,
    /// D4.2</b>, the maintainer's words verbatim: <i>"Same 1 min. under option d (lane
    /// c)"</i> (previously one second in a server a client started, the window Claude
    /// Code leaves before its kill, and thirty seconds in the session host). Measured on
    /// the session's own clock, the one the idle timer reads. Where a client kills the
    /// server it started, its kill still lands first; see the cap's own remarks.
    /// </para>
    /// </remarks>
    /// <param name="cause">
    /// Why this process is shutting down, as <see cref="SessionManager.ShuttingDownBecause"/>
    /// was told: <see cref="SessionCloseCause.Updating"/>,
    /// <see cref="SessionCloseCause.Stopped"/> or <see cref="SessionCloseCause.Failed"/>
    /// for the background, and <see cref="SessionCloseCause.ServerShutDown"/> for a host a
    /// client's own server owns.
    /// </param>
    /// <returns>A task that completes once the close has answered or the cap has run out.</returns>
    public async Task CloseTheBrowserForShutdownAsync(SessionCloseCause cause = SessionCloseCause.ServerShutDown)
    {
        // ⚠️ 8 b, 2026-10-04: a session that was not already closed is marked closed
        // and its reason recorded FIRST, whether or not a browser is up, so the next
        // BrowserAI to open it can say why it was closed. Before, a shutdown wrote
        // nothing, and a session it ended read afterwards as one nobody had closed.
        if (Closed is null && !CloseIsInFlight)
        {
            var closure = new SessionClosure(cause, _clock.GetUtcNow(), Idle?.Period)
            {
                By = AttachedTo?.Conversation(),
            };

            if (Interlocked.CompareExchange(ref _closed, closure, null) is null)
            {
                Record(closure, writeRow: true);
            }
            else
            {
                return;
            }
        }
        else
        {
            return;
        }

        if (!BrowserIsOpen)
        {
            return;
        }

        // ⚠️ THE REAP IS OWED NOW, because the close below takes the browser
        // down before the teardown looks. The teardown starts a reap only for a
        // browser it still finds up, and a clean close leaves its descriptor in
        // Playwright's registry all the same (a persistent profile's is never
        // deleted on close), so without this a shutdown that closed its browsers
        // first would stop reaping them. The kernel's count and not
        // `BrowserIsOpen`, for the reason the teardown gives.
        if (TheJobHoldsABrowser())
        {
            Volatile.Write(ref _reapOwed, 1);
        }

        var finished = BeginTheCloseInFlight();

        try
        {
            using var cap = new CancellationTokenSource(SessionTimes.BrowserCloseCap, _clock);
            using var either = CancellationTokenSource.CreateLinkedTokenSource(cap.Token, _cutShortToken);

            _ = await Child.AskAsync(
                RequestMethods.ToolsCall,
                new JsonObject
                {
                    ["name"] = BrowserCloseTool,
                    ["arguments"] = new JsonObject(),
                },
                either.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cutShortToken.IsCancellationRequested)
        {
            // A destroy cut it short, and said so.
        }
        catch (OperationCanceledException)
        {
            IdleLog.ShutdownCloseUnanswered(Logger, Location.FullPath, SessionTimes.BrowserCloseCap);
        }
        finally
        {
            _ = finished.TrySetResult();
        }
    }

    /// <summary>
    /// Lets go of the agent's own close at the cap, measured from when it was sent,
    /// so whatever waits for it waits no longer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It ends nothing itself.</b> What the cap bounds is the agent's own call, and
    /// the resume, the release or the shutdown waiting behind it; whichever goes on
    /// first ends the child through its stdin.
    /// </para>
    /// <para>
    /// <i>Renamed 2026-10-08 (previously <c>LetTheCallersCloseGoAtTheCapAsync</c>,
    /// for the caller's own <c>browser_close</c>, "A close that never answers, an armed
    /// debugger pause, keeps the caller waiting as it always did").</i> The agent's own
    /// call is answered at the cap now.
    /// </para>
    /// </remarks>
    /// <param name="close">The agent's close, as sent.</param>
    /// <param name="finished">Completed once the close is answered or the cap has run out.</param>
    /// <returns>Whether the browser answered within the cap; a close that failed did not.</returns>
    private async Task<bool> LetTheAgentsCloseGoAtTheCapAsync(Task close, TaskCompletionSource finished)
    {
        try
        {
            await close.WaitAsync(SessionTimes.BrowserCloseCap, _clock).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            IdleLog.CallersCloseUnanswered(Logger, Location.FullPath, SessionTimes.BrowserCloseCap);
            return false;
        }
#pragma warning disable CA1031 // A close that failed did not finish cleanly, which is what the answer says; this only marks it over.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
        finally
        {
            _ = finished.TrySetResult();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The teardown, under the cause a client going away or a process shutting
    /// down has. A <c>destroy</c> calls <see cref="TearDownAsync"/> itself, so
    /// that the reap it starts says which of the two it was.
    /// </remarks>
    public ValueTask DisposeAsync() => TearDownAsync(ServerRegistryReap.AfterTeardown);

    /// <summary>
    /// Tears this session down, saying what for.
    /// </summary>
    /// <remarks>
    /// <b>The cause is not decoration: it is the only thing that tells the two
    /// paths apart in the log.</b> One method serves a destroy, a client that went
    /// away and a process shutting down -- the work is identical -- and the reap
    /// below is machine-wide housekeeping somebody may one day have to account
    /// for.
    /// </remarks>
    /// <param name="reapCause">Which close this is, for the reap's record.</param>
    /// <param name="cutTheCloseShort">
    /// Whether a clean close in flight may be ended without waiting for it, which only
    /// a destroy asks for (<see cref="CutTheCloseShort"/>). Every other teardown waits
    /// for it, bounded by <see cref="SessionTimes.BrowserCloseCap"/>.
    /// </param>
    /// <returns>The teardown.</returns>
    public async ValueTask TearDownAsync(string reapCause, bool cutTheCloseShort = false)
    {
        // Before the one-shot guard, so a destroy that meets a teardown already
        // waiting on a close still lets that teardown go on.
        if (cutTheCloseShort)
        {
            CutTheCloseShort();
        }

        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        // ⚠️ NOTHING CUTS A CLEAN CLOSE SHORT, 2026-10-04. A release, a shutdown
        // and the resume that reopens a session all come through here, and a close
        // still in flight is waited for before the child is ended: ending it now
        // would close the child's stdin, on which @playwright/mcp force-kills a
        // browser that is in the middle of closing. Said in the session's log,
        // because the wait can be as long as the cap.
        if (CloseIsInFlight && !_cutShortToken.IsCancellationRequested)
        {
            IdleLog.TeardownWaitsForTheClose(Logger, Location.FullPath, SessionTimes.BrowserCloseCap);
        }

        // The timer first, so it cannot start a close of a child that is being
        // torn down -- and so an idle close already in flight is waited for here
        // instead of failing noisily against a closed transport.
        if (Idle is not null)
        {
            await Idle.DisposeAsync().ConfigureAwait(false);
        }

        // And a detached headed session's look at its window, for the same reason.
        StopWatching();

        // And the input check, before the watch whose pid it matches against goes.
        StopWatchingInput();

        // And the wait on the browser, so a browser this teardown ends is not read
        // as one that ended on its own.
        Interlocked.Exchange(ref _browserWatch, null)?.Dispose();

        // And the caller's own close or the shutdown's, which the timer does not
        // know about.
        await WaitForTheCloseInFlightAsync().ConfigureAwait(false);

        // ⚠️ READ BEFORE THE CHILD GOES, because afterwards there is no job to
        // ask. More than the child's own processes in it means a browser tree is
        // about to die, and therefore that a descriptor in Playwright's registry
        // is about to become one nothing else will ever unlink -- see the reap
        // below. Corrected 2026-10-03 (previously "More than the node child in
        // it"): a console host shares the job with node, so the count against
        // one started a reap for every session, browser or not.
        //
        // ⚠️ THE KERNEL'S COUNT AND NOT `BrowserIsOpen`, because the reap is
        // a real process run against a real registry: a double that says a
        // browser is up has written no descriptor, and the in-process rig must
        // never start a reaper on the strength of one.
        var browserWasUp = TheJobHoldsABrowser() || Volatile.Read(ref _reapOwed) is 1;

        // The child next. Disposing it closes the child's stdin, which is
        // upstream's own graceful teardown path, and then closes the job handle,
        // which is what guarantees no browser is left behind.
        await Child.DisposeAsync().ConfigureAwait(false);

        // ⚠️ STARTED, NOT AWAITED, and this is the one line of T7 in a teardown.
        // A destroy awaits this method before it answers its caller, so awaiting
        // a reap here would put upstream's quadratic `list()` -- 2.4 minutes on
        // this machine's backlog the first time -- in front of a caller's answer.
        // It is started after the child so that the descriptor it is meant to
        // collect is already dead, and it can neither throw nor block.
        if (browserWasUp)
        {
            _reap.Start(reapCause);
        }

        Lock.Dispose();

        // ⚠️ AFTER THE CHILD, and the order is the guarantee, not tidiness.
        // Releasing the claim tells the machine that nothing of this session is
        // running out of the browsers root, so it may not be released while the
        // child -- and therefore the browser -- is still up: a reinstall would
        // take the root exclusively and delete a tree with a live browser in it.
        BrowsersClaim.Dispose();

        // Last, so anything logged on the way down still lands in the session's
        // own file -- and so the file handle is closed before a destroy tries to
        // delete the directory holding it.
        Logging.Dispose();

        TryDeleteConfig();

        // Every close has finished by here. A waiter that asks later holds the
        // token read at construction, which registers nothing once this is gone.
        _cutShort.Dispose();
    }

    /// <summary>
    /// What runs once the browser ended without BrowserAI closing it, whatever the
    /// cause: the session host's release of a session whose client went. Set once, by
    /// the manager that holds this session.
    /// </summary>
    /// <param name="afterTheBrowserEnded">The callback.</param>
    public void WhenTheBrowserEndsOnItsOwn(Func<LiveSession, Task> afterTheBrowserEnded) =>
        Volatile.Write(ref _afterTheBrowserEnded, afterTheBrowserEnded);

    /// <summary>Whether a forwarded call has left a browser up in this session.</summary>
    public bool BrowserWasSeenUp => Volatile.Read(ref _browserSeenUp) is 1;

    /// <summary>
    /// Notes what a forwarded call is about to do, for the one thing the browser ending
    /// afterwards depends on: whether it closed a tab.
    /// </summary>
    /// <remarks>
    /// <b>Firefox ends when its last tab is closed</b> (measured 2026-10-04 at
    /// <c>firefox-1553</c>, exit code 0, 2 of 2), which is the agent's own doing and
    /// not a person's, so the reason says so.
    /// </remarks>
    /// <param name="tool">The tool, as the caller named it.</param>
    /// <param name="arguments">The caller's arguments.</param>
    public void NoteTheCall(string tool, JsonObject? arguments)
    {
        var closesATab = string.Equals(tool, TabsTool, StringComparison.Ordinal)
            && arguments?["action"] is JsonValue action
            && action.GetValueKind() is System.Text.Json.JsonValueKind.String
            && string.Equals(action.GetValue<string>(), "close", StringComparison.Ordinal);

        Volatile.Write(ref _lastCallClosedATab, closesATab ? 1 : 0);
    }

    /// <summary>
    /// Starts watching the browser's main process once a call has left a browser up,
    /// so the moment it ends without BrowserAI closing it is the moment the session
    /// knows. Asked after every forwarded call, and cheap once a watch is armed.
    /// </summary>
    /// <remarks>
    /// <b>8 b, decided 2026-10-04 by the maintainer</b>: a person closing a headed
    /// window closes the session as the agent's own <c>browser_close</c> does. Until
    /// then the next call met <c>@playwright/mcp</c> starting a new browser on its own
    /// (measured 2026-10-04: the call answered normally, Chromium's tabs came back and
    /// Firefox's did not), and nothing recorded that the window had been closed.
    /// </remarks>
    public void WatchTheBrowser()
    {
        if (Closed is not null || Volatile.Read(ref _disposed) is not 0 || Volatile.Read(ref _browserWatch) is not null || !BrowserIsOpen)
        {
            return;
        }

        Volatile.Write(ref _browserSeenUp, 1);

        var watch = _watchTheBrowser(Child, BrowserExecutable, TheBrowserEnded);

        if (watch is not null && Interlocked.CompareExchange(ref _browserWatch, watch, null) is not null)
        {
            watch.Dispose();
            return;
        }

        // F4, 2026-10-08: a visible session joins the process's check of the person's
        // input once its browser is watched, because the watched pid is what a window
        // in front is matched against.
        if (watch is not null && _inputWatch is { } inputs)
        {
            var registration = inputs.Watch(this);

            if (Interlocked.CompareExchange(ref _inputRegistration, registration, null) is not null)
            {
                registration.Dispose();
            }
        }
    }

    /// <summary>
    /// The browser ended and BrowserAI had not asked it to: the session is closed, its
    /// reason recorded, its browser server ended, and a session nobody drives is let go.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The reason comes from the exit code and the session.</b> Measured 2026-10-04
    /// at <c>chromium-1247</c> and <c>firefox-1553</c>: 0 is a clean exit, which is
    /// what a person closing a headed window leaves, and Firefox closing its last tab;
    /// any other code is what a crash or a kill leaves. A headless browser has no
    /// window, so a clean exit there is said as the browser's own.
    /// </para>
    /// <para>
    /// <b>Nothing here races BrowserAI's own closes</b>: each of them marks the session
    /// closed, or a close in flight, before it touches the browser, and a teardown sets
    /// its own mark first, so an exit any of them causes is passed over.
    /// </para>
    /// </remarks>
    /// <param name="exitCode">The browser's exit code, or <see langword="null"/> when it is not known.</param>
    public void TheBrowserEnded(int? exitCode)
    {
        if (Volatile.Read(ref _disposed) is not 0 || Closed is not null || CloseIsInFlight)
        {
            return;
        }

        if (Child.ChildHasGone)
        {
            RecordTheServerEnded();
            return;
        }

        var cause = exitCode is { } code && code is not 0 ? SessionCloseCause.BrowserCrashed
            : Volatile.Read(ref _lastCallClosedATab) is 1 ? SessionCloseCause.LastTabClosed
            : Settings.Headed ? SessionCloseCause.WindowClosed
            : SessionCloseCause.BrowserEnded;

        var closure = new SessionClosure(cause, _clock.GetUtcNow(), Idle?.Period) { ExitCode = exitCode };

        if (Interlocked.CompareExchange(ref _closed, closure, null) is not null)
        {
            return;
        }

        Record(closure, writeRow: true);

        // A closed session holds nothing until the resume that opens it again, as
        // after the idle close and the caller's own (P4 b, P3 b).
        _ = EndTheChildOnceTheCloseIsOverAsync();

        if (Volatile.Read(ref _afterTheBrowserEnded) is { } after)
        {
            _ = Task.Run(() => after(this), CancellationToken.None);
        }
    }

    /// <summary>
    /// Records, once, that this session's browser server ended without BrowserAI ending
    /// it, so the next BrowserAI to open the session can say why it was closed.
    /// </summary>
    /// <remarks>
    /// It does not mark the session closed: a call meeting it is answered with
    /// <see cref="SessionErrors.BrowserServerHasGone"/>, which says the same thing in
    /// its own words.
    /// </remarks>
    public void RecordTheServerEnded()
    {
        if (Closed is not null || Interlocked.Exchange(ref _serverEndRecorded, 1) is not 0)
        {
            return;
        }

        Record(new SessionClosure(SessionCloseCause.ServerEnded, _clock.GetUtcNow(), Idle?.Period), writeRow: true);
    }

    /// <summary>
    /// Records that the background's own end let this session go with nothing to keep,
    /// and why it ended, unless a close was already recorded for it.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-10 for the texts review's #24</b>: the reason is the one the
    /// shutdown records for every session it closes, so a session with no browser up
    /// says the same as one with a browser.
    /// </remarks>
    /// <param name="cause">Why the background ended: an update, a stop or a failure.</param>
    /// <param name="by">The client whose connection the end closed.</param>
    public void RecordTheBackgroundsEnd(SessionCloseCause cause, string? by)
    {
        if (Closed is not null || Volatile.Read(ref _serverEndRecorded) is not 0)
        {
            return;
        }

        var closure = new SessionClosure(cause, _clock.GetUtcNow(), Idle?.Period) { By = by };

        if (Interlocked.CompareExchange(ref _closed, closure, null) is null)
        {
            Record(closure, writeRow: true);
        }
    }

    /// <summary>
    /// Records that the session host let this session go with nothing to keep, unless
    /// a close was already recorded for it.
    /// </summary>
    /// <param name="by">The client that drove it and went away.</param>
    /// <param name="detail">What there was to keep: nothing, in one clause.</param>
    public void RecordTheRelease(string? by, string detail)
    {
        if (Closed is not null || Volatile.Read(ref _serverEndRecorded) is not 0)
        {
            return;
        }

        var closure = new SessionClosure(SessionCloseCause.Released, _clock.GetUtcNow(), Idle?.Period) { By = by, Detail = detail };

        if (Interlocked.CompareExchange(ref _closed, closure, null) is null)
        {
            Record(closure, writeRow: true);
        }
    }

    /// <summary>
    /// Writes a close's reason into the session's record: always a <c>closed</c>
    /// statement, and a log row for a close no call of its own already stands for.
    /// </summary>
    /// <remarks>
    /// <b>A record that cannot be written stops nothing</b>, for the reason the idle
    /// close gives: there is no caller to refuse, and the close has happened.
    /// </remarks>
    /// <param name="closure">The close.</param>
    /// <param name="writeRow">Whether to write the log row.</param>
    private void Record(SessionClosure closure, bool writeRow)
    {
        // A closed session's window is no one's to use: it leaves the input check, and
        // the check stops once no visible session is left in it (F4).
        StopWatchingInput();

        try
        {
            Lock.AppendLifecycle(RecordFields.Closed, closure.Recorded.Write());

            if (writeRow)
            {
                var row = Lock.Append(CloseReasons.LogRowTool, CloseReasons.Of(closure, asking: null));

                Lock.Settle(row, SessionStore.Successful, failure: null);
            }
        }
        catch (Exception failure) when (failure is SqliteException or ObjectDisposedException)
        {
            SessionLog.CloseNotRecorded(Lock.Logger, Lock.Location.FullPath, closure.Cause.ToString(), failure);
        }
    }

    /// <summary>Upstream's tab tool, spelled as upstream spells it.</summary>
    private const string TabsTool = "browser_tabs";

    /// <summary>
    /// The idle close: ends this session's whole browser server, node child
    /// included, when a browser is up -- and does nothing at all when one is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>P4 b, the maintainer's words of 2026-10-03, verbatim: "p4 b - unless
    /// this causes state loss. If that is the case research the differences
    /// between a and b. Go for b but report back in the morning for me to
    /// reconsider."</b> <i>Corrected 2026-10-03 (previously this sent upstream's
    /// own <c>browser_close</c> and kept the node child, "the tool is the
    /// mechanism, and nothing else would be").</i> Measured the same day: ending
    /// the whole child keeps exactly what that close kept, 12 of 12 runs, frees
    /// about 124 MB of working set (176 MB private) per idle session that the
    /// node child held, and costs 0.3 to 0.5 s at the resume that starts a new
    /// one. And it never sends a <c>browser_close</c>, which is the half that
    /// matters most: a <c>browser_close</c> that meets an armed debugger pause
    /// never answers and wedges the session (12 of 12 on 0.0.82, 6 of 6 on
    /// 0.0.83), while a child whose stdin closes exits in 0.75 to 1.5 s even
    /// while paused (measured on 0.0.82 and not re-taken on 0.0.83).
    /// </para>
    /// <para>
    /// ⚠️ <b>It asks the browser to close itself first again since 2026-10-03,
    /// Q367 a</b> (previously "And it never sends a <c>browser_close</c>, which is
    /// the half that matters most", the sentence above). The whole child still
    /// goes, and the wedge stays bounded: the close is sent with
    /// <see cref="Closed"/> already set, waited on for at most the cap, and past it
    /// the child is ended exactly as before. A close that meets an armed pause
    /// therefore costs the cap and no session.
    /// </para>
    /// <para>
    /// ⚠️ <b>The cap is <see cref="SessionTimes.BrowserCloseCap"/>, one minute, since
    /// 2026-10-04, D4.1</b>, the maintainer's words verbatim: <i>"Make it a roomy 1
    /// min. We want everything nicely saved to disk even on a slow system."</i>
    /// (previously thirty seconds, <c>IdleCloseBudget</c>). And nothing but a destroy
    /// ends the wait early any more (previously a resume, a destroy or a shutdown
    /// that started meanwhile ended it at once): each of the others waits for it.
    /// </para>
    /// <para>
    /// <b>The teardown is the one every other end of a session uses</b>: stdin
    /// closed, which is upstream's own graceful path, then up to the child's
    /// shutdown bound for it to exit, then the job. See
    /// <c>ChildProcessSession.ShutdownPeerAsync</c>.
    /// </para>
    /// <para>
    /// <b>Q327 a: with only the node child left there is nothing to close</b>, so
    /// it writes no row, closes nothing and leaves the session open. A session in
    /// that state answers its next call as it always did.
    /// </para>
    /// <para>
    /// ⚠️ <b>IT WRITES A ROW, and it wrote nothing at all until 2026-08-26.</b>
    /// While <c>browserai.log</c> existed the event survived there, and P2 deleted
    /// that file -- so an autonomous close became invisible in the only record
    /// there is. Written <c>in-flight</c> before the teardown and settled after it,
    /// so a teardown that hangs leaves the state <c>browserai_catch_up</c> renders
    /// as <i>"no answer was recorded"</i>.
    /// </para>
    /// <para>
    /// ⚠️ <b>A record that cannot be written does NOT stop the close, and that is
    /// the opposite of the rule at the caller's door.</b> There is no caller here
    /// and nothing to refuse to: declining to close would leave a browser tree up
    /// for the life of the session to protect a log line. The failure is logged
    /// and the close proceeds.
    /// </para>
    /// <para>
    /// ⚠️ <b>The residual race is narrowed and not removed, as it was before.</b>
    /// A call that passes the door in the microseconds between the timer's second
    /// look and <see cref="Closed"/> being set is forwarded into a child that is
    /// being ended, and is answered with the transport's failure; the call after
    /// it is refused with the sentence that names <c>browserai_resume</c>.
    /// </para>
    /// </remarks>
    /// <returns>What went, or <see langword="null"/> when there was nothing to close.</returns>
    private async Task<BrowserCloseResult?> CloseForIdleAsync()
    {
        // A teardown that has begun ends the child itself, and a close already in
        // flight is the one this would send.
        if (Volatile.Read(ref _disposed) is not 0 || Closed is not null || CloseIsInFlight || !BrowserIsOpen)
        {
            return null;
        }

        var before = ProcessesInTheJob();
        var browserWasUp = TheJobHoldsABrowser();

        // A visible window's close says nobody had used the window either (E2, F4).
        var idle = new SessionClosure(SessionCloseCause.Idle, _clock.GetUtcNow(), Idle?.Period)
        {
            Detail = Settings.Headed ? CloseReasons.NobodyUsedTheWindow : null,
        };

        if (Interlocked.CompareExchange(ref _closed, idle, null) is not null)
        {
            // The caller's own close got there first, and it is the close in flight.
            return null;
        }

        // 8 b: the reason is recorded; the row is the idle close's own, below.
        Record(idle, writeRow: false);

        var finished = BeginTheCloseInFlight();

        try
        {
            long? row = null;

            try
            {
                // ⚠️ Under CloseReasons.LogRowTool since 2026-10-10, the texts review's
                // #180 (previously browser_close, which no model can call since F1 a):
                // the row is the one close nobody asked for, and its name says so.
                row = Lock.Append(CloseReasons.LogRowTool, IdleCloseWhy);
            }
            catch (Exception failure) when (failure is SqliteException or ObjectDisposedException)
            {
                SessionLog.IdleCloseNotRecorded(Lock.Logger, Lock.Location.FullPath, failure);
            }

            await AskTheBrowserToCloseAsync().ConfigureAwait(false);

            await Child.DisposeAsync().ConfigureAwait(false);

            Settle(Lock, row, SessionStore.Successful, failure: null);

            // ⚠️ AFTER the teardown and never before it: a browser tree was up and is
            // now dead, so upstream's registry holds a descriptor nothing else will
            // ever unlink. Asked of the kernel's count for the reason the teardown
            // gives: a double's browser wrote no descriptor.
            if (browserWasUp)
            {
                _reap.Start(ServerRegistryReap.AfterIdleClose);
            }

            return new BrowserCloseResult(before, ProcessesInTheJob());
        }
        finally
        {
            // Over, the child included, so a resume waiting on it opens a directory
            // nothing of this session is still writing into.
            _ = finished.TrySetResult();
        }
    }

    /// <summary>
    /// The idle close's own <c>browser_close</c>: sent, and waited on until the
    /// browser answers or <see cref="SessionTimes.BrowserCloseCap"/> passes, or until a
    /// destroy cuts it short.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The cap reads the session's own clock</b>, the one the idle timer reads,
    /// so the suite drives it the way it drives the period and never waits it out.
    /// </para>
    /// <para>
    /// ⚠️ <b>A destroy is the only thing besides the cap that ends the wait, since
    /// 2026-10-04</b> (previously the token was the idle timer's, cancelled by any
    /// teardown, so a resume, a destroy or a shutdown that met the close ended it at
    /// once). The maintainer's warning of that day, verbatim: <i>"if we were to resume
    /// within that close window we will need to handle atomicity and orderign
    /// correctly."</i> The resume, a release and a shutdown now wait for the answer;
    /// a destroy deletes what the close would save, so it ends the wait, and the child
    /// is told the close was cancelled, by the id BrowserAI put on it.
    /// </para>
    /// <para>
    /// <b>Whatever the answer says, the child is ended next</b>, so an error
    /// answer is not reported: it would describe a browser the very next line
    /// takes down anyway.
    /// </para>
    /// </remarks>
    /// <returns>A task that completes once the close has answered, the cap has passed or a destroy has cut it short.</returns>
    private async Task AskTheBrowserToCloseAsync()
    {
        using var cap = new CancellationTokenSource(SessionTimes.BrowserCloseCap, _clock);
        using var either = CancellationTokenSource.CreateLinkedTokenSource(cap.Token, _cutShortToken);

        try
        {
            _ = await Child.AskAsync(
                RequestMethods.ToolsCall,
                new JsonObject
                {
                    ["name"] = BrowserCloseTool,
                    ["arguments"] = new JsonObject(),
                },
                either.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cutShortToken.IsCancellationRequested)
        {
            // A destroy cut it short, and said so. The child is ended next.
        }
        catch (OperationCanceledException)
        {
            IdleLog.IdleCloseUnanswered(Logger, Location.FullPath, SessionTimes.BrowserCloseCap);
        }
    }

    /// <summary>
    /// Whether the kernel counts a browser in the child's job, for the reap: a
    /// double's browser wrote no descriptor, so this never asks the seam.
    /// </summary>
    /// <returns>Whether processes beyond the child's own are in the job, or false once it cannot be read.</returns>
    private bool TheJobHoldsABrowser()
    {
        try
        {
            return Child.HoldsMoreThanItsOwnProcesses();
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>How many processes the child's job holds, for the idle close's log line.</summary>
    /// <returns>The count, or zero once the job cannot be read.</returns>
    private int ProcessesInTheJob()
    {
        try
        {
            return Child.JobProcessIds().Count;
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or ObjectDisposedException)
        {
            return 0;
        }
    }

    /// <summary>Settles the idle close's row, when one was written.</summary>
    /// <param name="sessionLock">The session's lock.</param>
    /// <param name="row">The row's id, or <see langword="null"/> when none was written.</param>
    /// <param name="outcome">One of <see cref="SessionStore"/>'s three spells.</param>
    /// <param name="failure">What failed, or <see langword="null"/>.</param>
    private static void Settle(SessionLock sessionLock, long? row, string outcome, byte[]? failure)
    {
        if (row is { } id)
        {
            // `SessionLock.Settle` already swallows a SQLite refusal and returns
            // early on a disposed lock, which is the whole set of ways this can
            // fail after the browser has been asked to close.
            sessionLock.Settle(id, outcome, failure);
        }
    }

    private void TryDeleteConfig()
    {
        try
        {
            File.Delete(ConfigFile);
        }
#pragma warning disable CA1031 // A generated config that will not delete is litter in a per-run directory the next run sweeps.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}

/// <summary>
/// The per-run arguments one child of a session was launched with.
/// </summary>
/// <remarks>
/// <b>A record, so two of them compare by value</b> -- which is the whole of how a
/// resume decides whether it has anything to apply. ⚠️ <i>Corrected 2026-10-08
/// (previously "None of it is written to the session's record: every run says what it
/// wants, and this is what the current run said.")</i>: F2 d records what each run used,
/// as <see cref="RecordFields.Settings"/>, so a resume can say what it would change;
/// every run still says what it wants, and the record is what a call is compared with,
/// never what it is filled in from.
/// </remarks>
/// <param name="Headed">Whether the browser has a window.</param>
/// <param name="Transcript">
/// Whether upstream writes <c>session.md</c>, its <c>saveSession</c>. ⚠️
/// <i>Renamed 2026-10-04 (previously <c>Tracing</c>, "Whether upstream records
/// the run")</i>, with the argument it carries, Q371 c.
/// </param>
/// <param name="Debug">Whether this session's own log is at debug level.</param>
/// <param name="Run">Everything else a caller can set per run.</param>
/// <param name="Idle">
/// How long the browser may sit unused before BrowserAI closes it, minutes or never.
/// ⚠️ <i>Added 2026-10-08, E2</i>: the countdown is per session since that day, and a
/// visible window has one too.
/// </param>
internal sealed record SessionRunSettings(bool Headed, bool Transcript, bool Debug, RunOptions Run, IdleSetting Idle)
{
    /// <summary>The JSON a <c>settings</c> statement carries: every setting, named as a call names it.</summary>
    /// <returns>The value.</returns>
    public string Write() =>
        new JsonObject
        {
            [RunSettingNames.Headed] = Headed,
            [RunSettingNames.Transcript] = Transcript,
            [RunSettingNames.CaptureNetwork] = Run.CaptureNetwork,
            [IdleSetting.ParameterName] = Idle.Minutes is { } minutes ? JsonValue.Create(minutes) : JsonValue.Create(IdleSetting.NeverWord),
            [RunSettingNames.Viewport] = Run.Viewport.ToString(),
            [RunSettingNames.Locale] = Run.Locale,
            [RunSettingNames.TimeZone] = Run.TimeZone,
            [RunSettingNames.IgnoreHttpsErrors] = Run.IgnoreHttpsErrors,
            [RunSettingNames.Debug] = Debug,
        }.ToJsonString();

    /// <summary>A <c>settings</c> statement back out of its value.</summary>
    /// <remarks>
    /// <b>All of it or nothing</b>: a value missing a setting, or carrying one in a
    /// shape this build does not read, is <see langword="null"/>, which a resume reads
    /// as nothing to compare with. A comparison against half a run would hold a call
    /// back for a difference nobody asked for.
    /// </remarks>
    /// <param name="value">The stored text.</param>
    /// <returns>The settings, or <see langword="null"/>.</returns>
    public static SessionRunSettings? Read(string value)
    {
        try
        {
            if (JsonNode.Parse(value) is not JsonObject read
                || flag(read, RunSettingNames.Headed) is not { } headed
                || flag(read, RunSettingNames.Transcript) is not { } transcript
                || flag(read, RunSettingNames.CaptureNetwork) is not { } capture
                || flag(read, RunSettingNames.IgnoreHttpsErrors) is not { } ignore
                || flag(read, RunSettingNames.Debug) is not { } debug
                || text(read, RunSettingNames.Locale) is not { } locale
                || text(read, RunSettingNames.Viewport) is not { } viewport
                || !ViewportSize.TryParse(viewport, out var size)
                || idle(read) is not { } setting
                || read[RunSettingNames.TimeZone] is not (null or JsonValue))
            {
                return null;
            }

            var zone = read[RunSettingNames.TimeZone] is JsonValue named && named.TryGetValue<string>(out var spelled) ? spelled : null;

            if (read[RunSettingNames.TimeZone] is not null && zone is null)
            {
                return null;
            }

            return new SessionRunSettings(
                headed,
                transcript,
                debug,
                new RunOptions
                {
                    Viewport = size,
                    Locale = locale,
                    TimeZone = zone,
                    IgnoreHttpsErrors = ignore,
                    CaptureNetwork = capture,
                },
                setting);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        static bool? flag(JsonObject node, string name) =>
            node[name] is JsonValue value && value.TryGetValue<bool>(out var read) ? read : null;

        static string? text(JsonObject node, string name) =>
            node[name] is JsonValue value && value.TryGetValue<string>(out var read) ? read : null;

        static IdleSetting? idle(JsonObject node) => node[IdleSetting.ParameterName] switch
        {
            JsonValue value when value.TryGetValue<int>(out var minutes) && minutes >= 1 => IdleSetting.Of(minutes),
            JsonValue value when value.TryGetValue<string>(out var word) && string.Equals(word, IdleSetting.NeverWord, StringComparison.Ordinal) => IdleSetting.Never,
            _ => null,
        };
    }
}

/// <summary>How <c>browserai_close</c> ended.</summary>
internal enum AgentClose
{
    /// <summary>The browser answered its close within the cap, and the child was ended.</summary>
    Closed,

    /// <summary>No browser was up, so no close was sent, and the child was ended.</summary>
    NothingWasOpen,

    /// <summary>The browser did not answer within the cap, and the child was ended through its stdin.</summary>
    CapRanOut,

    /// <summary>Another close got there first, and this one did nothing.</summary>
    AlreadyClosed,
}

/// <summary>Whether a connection may drive a session the host holds.</summary>
internal enum SessionClaim
{
    /// <summary>The session is already attached to this connection.</summary>
    Attached,

    /// <summary>Its client had gone, and this connection has taken it over.</summary>
    TakenOver,

    /// <summary>Another connection that is still open drives it.</summary>
    HeldElsewhere,

    /// <summary>The host is letting it go; to a caller it is a session nobody holds.</summary>
    Releasing,
}

/// <summary>Who or what closed a session's browser.</summary>
/// <remarks>
/// ⚠️ <b>The member names are a stored spelling since 2026-10-04</b>: a
/// <c>closed</c> statement in <c>browserai.data</c> carries the name, and a name this
/// build does not know reads back as <see cref="Unknown"/>. Renaming one is a change
/// to every record already written.
/// </remarks>
internal enum SessionCloseCause
{
    /// <summary>BrowserAI's own idle timer, after a headless session went unused.</summary>
    Idle,

    /// <summary>
    /// A close call, from this client or another: <c>browserai_close</c> since
    /// 2026-10-08, and <c>browser_close</c> in a record written before that day.
    /// </summary>
    Caller,

    /// <summary>A person closed a headed session's window: the browser exited cleanly with nobody asking.</summary>
    WindowClosed,

    /// <summary>The browser exited after <c>browser_tabs</c> closed its last tab, which Firefox does.</summary>
    LastTabClosed,

    /// <summary>A headless browser exited cleanly with nobody asking.</summary>
    BrowserEnded,

    /// <summary>The browser ended with a non-zero exit code: a crash or a kill.</summary>
    BrowserCrashed,

    /// <summary>The browser server, the node child, ended with nobody ending it.</summary>
    ServerEnded,

    /// <summary>
    /// BrowserAI was asked to stop through its pipe, which the uninstall hook does since
    /// 2026-10-08. <i>Corrected 2026-10-10 (previously "for an update, or from its own
    /// page"): an update records <see cref="Updating"/> since that day, and the page asks
    /// no stop.</i> A record written before it holds this for those two as well.
    /// </summary>
    Stopped,

    /// <summary>
    /// The BrowserAI a client started shut down because that client went away. Since
    /// 2026-10-08 the product starts no such server, so only the suite's in-process rig,
    /// whose one proxy owns its host, records it.
    /// </summary>
    ServerShutDown,

    /// <summary>The session host let a session go whose client went away, with nothing to keep.</summary>
    Released,

    /// <summary>
    /// A resume asked for other settings, and its same call sent again closed the browser
    /// to open it with them: F1 a and F2 d, 2026-10-08.
    /// </summary>
    SettingsChanged,

    /// <summary>
    /// The background closed every session to install an update: the person's
    /// <i>Install now</i>, or the update it installs once nothing holds it. Added
    /// 2026-10-10 for the texts review's #24.
    /// </summary>
    Updating,

    /// <summary>
    /// The background ended on a failure and closed its sessions on the way out. Added
    /// 2026-10-10 with <see cref="Updating"/>, so that no end of the background is
    /// recorded as a client that went away.
    /// </summary>
    Failed,

    /// <summary>Read back, never recorded: an opening no close followed.</summary>
    Unrecorded,

    /// <summary>Read back: a cause this build does not know.</summary>
    Unknown,
}

/// <summary>How and when a session's browser was closed.</summary>
/// <param name="Cause">Who or what closed it.</param>
/// <param name="At">When.</param>
/// <param name="IdlePeriod">The session's idle period, which the refusal names after an idle close.</param>
internal sealed record SessionClosure(SessionCloseCause Cause, DateTimeOffset At, TimeSpan? IdlePeriod)
{
    /// <summary>The connection whose call closed it, in this process only, so a refusal can say whose it was.</summary>
    public Proxy.CallerConnection? ClosedBy { get; init; }

    /// <summary>That client, as BrowserAI describes one, which the record keeps.</summary>
    public string? By { get; init; }

    /// <summary>What the closing call gave as its reason.</summary>
    public string? Why { get; init; }

    /// <summary>The browser's exit code, when it ended with nobody asking.</summary>
    public int? ExitCode { get; init; }

    /// <summary>A clause of BrowserAI's own, for a cause that has one.</summary>
    public string? Detail { get; init; }

    /// <summary>The close as the record keeps it.</summary>
    public RecordedClose Recorded => new(Cause, By, Why, ExitCode, OpenedAt: null, Detail);
}
