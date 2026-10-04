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
internal sealed class LiveSession : IAsyncDisposable
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
        ServerRegistryReap reap)
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
        _clock = environment.Clock;
        _cutShortToken = _cutShort.Token;
        Logger = logging.Factory.CreateLogger<LiveSession>();

        // ⚠️ NO TIMER AT ALL FOR A HEADED SESSION, Q326 a, the maintainer's
        // words of 2026-10-03, verbatim: "Q326 a - the timer is there to
        // conserve system resources the user cannot see. Also, interactive
        // windows mostly hold user state so they are super valuable." Not a
        // timer that is never armed: an object that does not exist cannot be
        // armed by a call nobody thought about. Its config writes upstream's own
        // idle timeout as zero for the same reason (BrowserConfiguration).
        Idle = settings.Headed
            ? null
            : new BrowserIdleTimer(
                location.FullPath,
                environment.BrowserIdlePeriod,
                FireIdleAsync,
                logging.Factory.CreateLogger<BrowserIdleTimer>(),
                environment.Clock);
    }

    /// <summary>
    /// How often a headed session whose client went is asked whether its browser
    /// is still up: the session host's own look,
    /// <see cref="Proxy.SessionHostServer.LingerLook"/>, <b>15 s</b>.
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
    /// <b>One cadence with the host's</b>: when the last window closes, this look lets
    /// the session go and the host's next look at its own emptiness sees it, so the
    /// host's linger starts no later than two looks after the window closed. The price
    /// of the period is a closed window's node child, about 50 MB, held for at most one
    /// look. It is a seam for the suite through the session environment's clock, as the
    /// idle period is.
    /// </para>
    /// </remarks>
    public static TimeSpan DetachedWindowLook { get; } = Proxy.SessionHostServer.LingerLook;

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
        "BrowserAI closed this session's browser itself: nothing had been forwarded through the session for the idle period, "
        + "so it ended the browser server, node child included. Every browser call is refused until browserai_resume starts a new one, "
        + "and the browser's own session restore then reopens the tabs that were open.";

    /// <summary>
    /// What a caller's own <c>browser_close</c> is answered with when no browser
    /// was open to close.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not forwarded, because forwarding it would start a browser.</b> Measured
    /// 2026-10-03 at <c>@playwright/mcp</c> 0.0.82 and 0.0.83: a
    /// <c>browser_close</c> with no browser up launched 8 to 9 browser processes in
    /// order to close them, rewrote a network capture empty and left a registry
    /// descriptor nothing reaps. In a headed session that is a window that appears
    /// and goes again.
    /// </para>
    /// <para>
    /// <b>An answer and not a refusal</b>, so it lives here and not in
    /// <c>SessionErrors</c>: closing what is not open has done what it was asked.
    /// And it does NOT close the session, which is the idle close's rule applied
    /// to the caller's (P3 b): a timer that finds only the node child does nothing,
    /// and so does this.
    /// </para>
    /// </remarks>
    public const string NothingWasOpenToClose =
        "No browser was open in this session, so there was nothing to close and none was started in order to close it. "
        + "The session is unchanged: the next browser call starts a browser as usual.";

    // ⚠️ RETIRED 2026-10-04: `ShutdownCloseBudget`, one second, and
    // `IdleCloseBudget`, thirty seconds, stood here. D4.1 and D4.2, the
    // maintainer's words verbatim: "Make it a roomy 1 min. We want everything
    // nicely saved to disk even on a slow system." and "Same 1 min. under option
    // d (lane c)". Every clean close takes `SessionTimes.BrowserCloseCap` since,
    // one cap with its derivation beside it, and neither name exists any more.

    private readonly ServerRegistryReap _reap;
    private readonly Func<ChildConnection, bool> _browserIsOpen;
    private readonly TimeProvider _clock;

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
    /// for a headed session, which is never idle-closed.
    /// </summary>
    /// <remarks>
    /// It belongs to this lifetime and not to the manager because everything
    /// it acts on does: one session is one child, one job and one log, and a
    /// timer owned anywhere else would need a way to name a session that has
    /// already gone.
    /// </remarks>
    public BrowserIdleTimer? Idle { get; }

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
    /// Marks this session closed by the caller's own <c>browser_close</c>, records the
    /// close as in flight, and sends it, in that order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>P3 b, the maintainer's words of 2026-10-03, verbatim: "p3 b".</b> The
    /// caller's close is treated like the idle close: every later call is refused
    /// until <c>browserai_resume</c>, which starts the browser again and lets its
    /// own session restore reopen the tabs. Marked before the close is forwarded,
    /// so a close that never answers still leaves a session the resume can
    /// recover; see <see cref="Closed"/>.
    /// </para>
    /// <para>
    /// ⚠️ <b>One step since 2026-10-04</b> (previously the proxy marked the session
    /// closed and forwarded the close a few lines later with the caller's own token).
    /// A resume that landed between the two met a closed session with nothing in
    /// flight and ended the child under the close it was about to receive. And the
    /// close is sent with no token of the caller's: a caller that cancels it, or a
    /// connection that ends under it, stops waiting for the answer and leaves the close
    /// to finish in the child, which is ended only once the close is over
    /// (<see cref="EndTheChildAfterTheCallersCloseAsync"/>). Whoever waits for it waits
    /// at most <see cref="SessionTimes.BrowserCloseCap"/> from now.
    /// </para>
    /// </remarks>
    /// <param name="method">The JSON-RPC method, as the caller sent it.</param>
    /// <param name="parameters">The caller's parameters, with BrowserAI's own taken out.</param>
    /// <returns>The child's answer, for the caller to wait on under its own token.</returns>
    public Task<ChildAnswer> SendTheCallersCloseAsync(string method, JsonNode? parameters)
    {
        _ = Interlocked.CompareExchange(ref _closed, new SessionClosure(SessionCloseCause.Caller, _clock.GetUtcNow(), Idle?.Period), null);

        var finished = BeginTheCloseInFlight();
        var asked = Child.AskAsync(method, parameters, CancellationToken.None);

        _ = LetTheCallersCloseGoAtTheCapAsync(asked, finished);

        return asked;
    }

    /// <summary>
    /// Ends this session's child once the caller's own <c>browser_close</c> is over,
    /// so a closed session holds no node process either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The same teardown the idle close makes</b>, and cheap here: the browser
    /// has already gone, so the node child exits in about 20 ms once its stdin
    /// closes (measured 2026-10-03, 14 to 36 ms over twelve runs). A session that
    /// is closed holds nothing until the resume that opens it again.
    /// </para>
    /// <para>
    /// ⚠️ <b>Over means answered or out of its cap, and not that the caller stopped
    /// waiting</b>, since 2026-10-04: a caller that cancelled its close, or whose
    /// connection ended, leaves the close running in the child, and ending the child
    /// then would force-kill a browser that is in the middle of closing.
    /// </para>
    /// <para>
    /// <b>A caller that left is not waited for here</b>: the child is ended once the
    /// close is over, off the caller's own request, so a connection that ended under
    /// its close is let go at once and a client that comes back meets the session
    /// closing and not driven by the connection it left behind.
    /// </para>
    /// </remarks>
    /// <param name="answered">Whether the caller had the answer, or stopped waiting before it.</param>
    /// <returns>A task that completes once the child is gone, or once its end is under way for a caller that left.</returns>
    public async Task EndTheChildAfterTheCallersCloseAsync(bool answered)
    {
        if (Closed is not { Cause: SessionCloseCause.Caller })
        {
            return;
        }

        if (answered)
        {
            await WaitForTheCloseInFlightAsync().ConfigureAwait(false);
            await Child.DisposeAsync().ConfigureAwait(false);
            return;
        }

        if (CloseIsInFlight)
        {
            IdleLog.CallerLeftItsClose(Logger, Location.FullPath, SessionTimes.BrowserCloseCap);
        }

        _ = EndTheChildOnceTheCloseIsOverAsync();
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
    /// <returns>A task that completes once the close has answered or the cap has run out.</returns>
    public async Task CloseTheBrowserForShutdownAsync()
    {
        if (Closed is not null || CloseIsInFlight || !BrowserIsOpen)
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
    /// Lets go of the caller's own close at the cap, measured from when it was sent,
    /// so whatever waits for it waits no longer.
    /// </summary>
    /// <remarks>
    /// <b>It ends nothing itself.</b> A close that never answers, an armed debugger
    /// pause, keeps the caller waiting as it always did; what the cap bounds is the
    /// resume, the release or the shutdown that is waiting behind it, which then ends
    /// the child through its stdin, and the child ending answers the caller.
    /// </remarks>
    /// <param name="close">The caller's close, as sent.</param>
    /// <param name="finished">Completed once the close is answered or the cap has run out.</param>
    /// <returns>The wait.</returns>
    private async Task LetTheCallersCloseGoAtTheCapAsync(Task close, TaskCompletionSource finished)
    {
        try
        {
            await close.WaitAsync(SessionTimes.BrowserCloseCap, _clock).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            IdleLog.CallersCloseUnanswered(Logger, Location.FullPath, SessionTimes.BrowserCloseCap);
        }
#pragma warning disable CA1031 // The close's own failure is the caller's to read in the answer; this only marks it over.
        catch (Exception)
#pragma warning restore CA1031
        {
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

        if (Interlocked.CompareExchange(
            ref _closed,
            new SessionClosure(SessionCloseCause.Idle, _clock.GetUtcNow(), Idle?.Period),
            null) is not null)
        {
            // The caller's own close got there first, and it is the close in flight.
            return null;
        }

        var finished = BeginTheCloseInFlight();

        try
        {
            long? row = null;

            try
            {
                row = Lock.Append(BrowserCloseTool, IdleCloseWhy);
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
/// resume decides whether it has anything to apply. None of it is written to the
/// session's record: every run says what it wants, and this is what the current
/// run said.
/// </remarks>
/// <param name="Headed">Whether the browser has a window.</param>
/// <param name="Transcript">
/// Whether upstream writes <c>session.md</c>, its <c>saveSession</c>. ⚠️
/// <i>Renamed 2026-10-04 (previously <c>Tracing</c>, "Whether upstream records
/// the run")</i>, with the argument it carries, Q371 c.
/// </param>
/// <param name="Debug">Whether this session's own log is at debug level.</param>
/// <param name="Run">Everything else a caller can set per run.</param>
internal sealed record SessionRunSettings(bool Headed, bool Transcript, bool Debug, RunOptions Run);

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

/// <summary>Who closed a session's browser.</summary>
internal enum SessionCloseCause
{
    /// <summary>BrowserAI's own idle timer, after a headless session went unused.</summary>
    Idle,

    /// <summary>The caller's own <c>browser_close</c>.</summary>
    Caller,
}

/// <summary>How and when a session's browser was closed.</summary>
/// <param name="Cause">Who closed it.</param>
/// <param name="At">When.</param>
/// <param name="IdlePeriod">The session's idle period, which the refusal names after an idle close.</param>
internal sealed record SessionClosure(SessionCloseCause Cause, DateTimeOffset At, TimeSpan? IdlePeriod);
