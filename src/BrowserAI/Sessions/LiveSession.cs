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
                CloseForIdleAsync,
                logging.Factory.CreateLogger<BrowserIdleTimer>(),
                environment.Clock);
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

    /// <summary>
    /// How long a shutdown waits for each session's own <c>browser_close</c>
    /// before it ends the child anyway.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>e2 of P7, decided by the root session 2026-10-03 for the maintainer's
    /// review.</b> A client gives BrowserAI almost no time at its end: Claude
    /// Code 2.1.288 starts <c>taskkill /T /F</c> on the server 0.53 to 1.15 s after
    /// it closes the server's input, and Codex gives none. A browser that is
    /// closed by its own tool flushes what it holds; one that is killed keeps a
    /// cookie only if it had been on disk for about 30 s. Measured 2026-10-03:
    /// a <c>browser_close</c> took 151 to 915 ms on Chromium and 444 to 1,163 ms
    /// on Firefox (<see href="../../../kb/playwright/provisioning-and-timings.md#what-a-session-keeps-across-a-browser-close-and-what-brings-the-rest-back----measured-2026-10-03">kb</see>),
    /// and sessions are closed all at once, so this is about the window there is.
    /// </para>
    /// <para>
    /// <b>The bound is the other half, and it is not optional.</b> A
    /// <c>browser_close</c> that meets an armed debugger pause never answers --
    /// 12 of 12 on 0.0.82 and 6 of 6 on 0.0.83 -- so an unbounded one would hang a
    /// shutdown. Past the bound the child is ended through its stdin and its job
    /// exactly as before.
    /// </para>
    /// </remarks>
    public static TimeSpan ShutdownCloseBudget { get; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long the idle close waits for the browser to answer its own
    /// <c>browser_close</c> before it ends the child anyway.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q367 a, decided 2026-10-03 by the maintainer, in his words verbatim:
    /// <i>"Q367 a - but why just 1 sec.? Why not be very gracefull here?"</i></b>
    /// The idle close asks the browser to close itself before it ends the child,
    /// because a browser closed by its own tool flushes what it holds: in the
    /// research of 2026-10-03 a child ended through its stdin with no close first
    /// lost a store in 1 of 16 Chromium and 1 of 19 Firefox runs, where a
    /// <c>browser_close</c> first kept everything, 6 of 6
    /// (<see href="../../../kb/playwright/provisioning-and-timings.md#how-old-a-write-must-be-before-a-hard-kill-keeps-it----measured-2026-10-03">kb</see>).
    /// </para>
    /// <para>
    /// <b>Why thirty seconds, and not the second <see cref="ShutdownCloseBudget"/>
    /// gives.</b> That second is what a client leaves a server before it kills the
    /// tree, 0.53 to 1.15 s for Claude Code; at idle nobody is waiting on the
    /// answer. A call that arrives meanwhile is refused at once with the sentence
    /// naming <c>browserai_resume</c>, because <see cref="Closed"/> is set before
    /// the close is sent, and the teardown a resume, a destroy or a shutdown starts
    /// ends the wait the moment it begins. So the cap costs nothing while a close is
    /// answered, and the slowest close the state-across-close batch of 2026-10-03
    /// timed answered in 1,163 ms (Firefox at 0.0.83; Chromium's slowest there was
    /// 915 ms): the cap is about 26 times that. It bounds one case alone: a
    /// debugger pause armed in the browser parks the close
    /// and it never answers, 12 of 12 on <c>@playwright/mcp</c> 0.0.82 and 6 of 6
    /// on 0.0.83. There the cap is how long a wedged browser and its child are held
    /// past the idle period before the child is ended through its stdin, which a
    /// paused child obeys, and thirty seconds is a twentieth of the ten idle
    /// minutes that came before it.
    /// </para>
    /// </remarks>
    public static TimeSpan IdleCloseBudget { get; } = TimeSpan.FromSeconds(30);

    private readonly ServerRegistryReap _reap;
    private readonly Func<ChildConnection, bool> _browserIsOpen;
    private readonly TimeProvider _clock;
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
    /// </remarks>
    public SessionClosure? Closed => Volatile.Read(ref _closed);

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
    /// Records that the caller's own <c>browser_close</c> is about to close this
    /// session's browser, before it is sent.
    /// </summary>
    /// <remarks>
    /// <b>P3 b, the maintainer's words of 2026-10-03, verbatim: "p3 b".</b> The
    /// caller's close is treated like the idle close: every later call is refused
    /// until <c>browserai_resume</c>, which starts the browser again and lets its
    /// own session restore reopen the tabs. Marked before the close is forwarded,
    /// so a close that never answers still leaves a session the resume can
    /// recover; see <see cref="Closed"/>.
    /// </remarks>
    public void ClosedByTheCaller() =>
        _ = Interlocked.CompareExchange(ref _closed, new SessionClosure(SessionCloseCause.Caller, _clock.GetUtcNow(), Idle?.Period), null);

    /// <summary>
    /// Ends this session's child once the caller's own <c>browser_close</c> has
    /// been answered, so a closed session holds no node process either.
    /// </summary>
    /// <remarks>
    /// <b>The same teardown the idle close makes</b>, and cheap here: the browser
    /// has already gone, so the node child exits in about 20 ms once its stdin
    /// closes (measured 2026-10-03, 14 to 36 ms over twelve runs). A session that
    /// is closed holds nothing until the resume that opens it again.
    /// </remarks>
    /// <returns>A task that completes once the child is gone.</returns>
    public async Task EndTheChildAfterTheCallersCloseAsync()
    {
        if (Closed is { Cause: SessionCloseCause.Caller })
        {
            await Child.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sends this session's browser its own <c>browser_close</c>, bounded, so a
    /// shutdown lets the browser flush before the child is ended.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>e2 of P7. Only where there is a browser to close and nothing has closed
    /// it already</b>: a closed session's browser is gone or wedged, and a
    /// <c>browser_close</c> with no browser up starts one in order to close it.
    /// </para>
    /// <para>
    /// <b>It writes no row.</b> A teardown has never written one, and the time
    /// this runs in is the second a client gives before it kills the tree, which
    /// is not the moment to add a database write to every session.
    /// </para>
    /// </remarks>
    /// <returns>A task that completes once the close has answered or the bound has passed.</returns>
    public async Task CloseTheBrowserForShutdownAsync()
    {
        if (Closed is not null || !BrowserIsOpen)
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

        using var bound = new CancellationTokenSource(ShutdownCloseBudget);

        try
        {
            _ = await Child.AskAsync(
                RequestMethods.ToolsCall,
                new JsonObject
                {
                    ["name"] = BrowserCloseTool,
                    ["arguments"] = new JsonObject(),
                },
                bound.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            IdleLog.ShutdownCloseUnanswered(Logger, Location.FullPath, ShutdownCloseBudget);
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
    /// <returns>The teardown.</returns>
    public async ValueTask TearDownAsync(string reapCause)
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        // The timer first, so it cannot start a close of a child that is being
        // torn down -- and so a close already in flight is waited for here
        // instead of failing noisily against a closed transport.
        if (Idle is not null)
        {
            await Idle.DisposeAsync().ConfigureAwait(false);
        }

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
    /// <see cref="Closed"/> already set, waited on for at most
    /// <see cref="IdleCloseBudget"/>, and cut short by a teardown that starts
    /// meanwhile, and past either the child is ended exactly as before. A close
    /// that meets an armed pause therefore costs thirty seconds and no session.
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
    /// <param name="cancellationToken">Cancelled by a teardown that starts while this runs.</param>
    /// <returns>What went, or <see langword="null"/> when there was nothing to close.</returns>
    private async Task<BrowserCloseResult?> CloseForIdleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Closed is not null || !BrowserIsOpen)
        {
            return null;
        }

        var before = ProcessesInTheJob();
        var browserWasUp = TheJobHoldsABrowser();

        _ = Interlocked.CompareExchange(
            ref _closed,
            new SessionClosure(SessionCloseCause.Idle, _clock.GetUtcNow(), Idle?.Period),
            null);

        long? row = null;

        try
        {
            row = Lock.Append(BrowserCloseTool, IdleCloseWhy);
        }
        catch (Exception failure) when (failure is SqliteException or ObjectDisposedException)
        {
            SessionLog.IdleCloseNotRecorded(Lock.Logger, Lock.Location.FullPath, failure);
        }

        await AskTheBrowserToCloseAsync(cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// The idle close's own <c>browser_close</c>: sent, and waited on until the
    /// browser answers, <see cref="IdleCloseBudget"/> passes or a teardown starts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The cap reads the session's own clock</b>, the one the idle timer reads,
    /// so the suite drives it the way it drives the period and never waits it out.
    /// </para>
    /// <para>
    /// <b>A teardown ends the wait at once, and it is the only thing besides the
    /// cap that does.</b> The token is the idle timer's, cancelled when the session
    /// is torn down, so a resume, a destroy or a shutdown meeting a close that is
    /// still waiting -- the armed-pause case -- does not sit out the cap or the
    /// timer's own teardown bound behind it. The child is then told the close was
    /// cancelled, by the id BrowserAI put on it, as any cancelled call is.
    /// </para>
    /// <para>
    /// ⚠️ <b>What a shutdown does not get here, named and not handled.</b> A
    /// shutdown asks each open browser to close within
    /// <see cref="ShutdownCloseBudget"/>, and skips a session already closed, so a
    /// shutdown that lands while this close is being answered ends the child in the
    /// middle of the answer. The window is the close's own duration, measured at up
    /// to 1,163 ms, and it opens once per idle close.
    /// </para>
    /// <para>
    /// <b>Whatever the answer says, the child is ended next</b>, so an error
    /// answer is not reported: it would describe a browser the very next line
    /// takes down anyway.
    /// </para>
    /// </remarks>
    /// <param name="teardown">Cancelled by a teardown that starts while this waits.</param>
    /// <returns>A task that completes once the close has answered, the cap has passed or a teardown has begun.</returns>
    private async Task AskTheBrowserToCloseAsync(CancellationToken teardown)
    {
        using var cap = new CancellationTokenSource(IdleCloseBudget, _clock);
        using var either = CancellationTokenSource.CreateLinkedTokenSource(cap.Token, teardown);

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
        catch (OperationCanceledException) when (!teardown.IsCancellationRequested)
        {
            IdleLog.IdleCloseUnanswered(Logger, Location.FullPath, IdleCloseBudget);
        }
        catch (OperationCanceledException)
        {
            // A teardown began. The idle close ends the child next all the same,
            // and the teardown, which waits for the idle close, finds it ended.
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
/// <param name="Tracing">Whether upstream records the run, as <c>saveSession</c>.</param>
/// <param name="Debug">Whether this session's own log is at debug level.</param>
/// <param name="Run">Everything else a caller can set per run.</param>
internal sealed record SessionRunSettings(bool Headed, bool Tracing, bool Debug, RunOptions Run);

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
