// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Logging;
using BrowserAI.Protocol;
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
    /// <param name="launch">
    /// Exactly what that child was launched with, kept so a child that has died can
    /// be replaced by one started the same way and not one assembled again from
    /// arguments that may since have moved.
    /// </param>
    /// <param name="logging">This session's own logging stack. This object owns it.</param>
    /// <param name="config">The config the child was started with.</param>
    /// <param name="configFile">Where that config was written.</param>
    /// <param name="createdHere">Whether this connection is the one that created the session.</param>
    /// <param name="idlePeriod">How long this session's browser may sit unused before it is closed.</param>
    /// <param name="clock">The clock the idle timer reads. <see cref="TimeProvider.System"/> in the product.</param>
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
        ChildProcessOptions launch,
        SessionLogging logging,
        GeneratedConfig config,
        string configFile,
        bool createdHere,
        TimeSpan idlePeriod,
        TimeProvider clock,
        ServerRegistryReap reap)
    {
        ArgumentNullException.ThrowIfNull(logging);
        ArgumentNullException.ThrowIfNull(reap);

        Location = location;
        Lock = sessionLock;
        BrowsersClaim = browsersClaim;
        _child = child;
        Launch = launch;
        Logging = logging;
        Config = config;
        ConfigFile = configFile;
        CreatedHere = createdHere;
        _reap = reap;
        Logger = logging.Factory.CreateLogger<LiveSession>();

        // ⚠️ LAST, AND IT READS `Child` , NOT THE ARGUMENT. The timer
        // outlives any one child: a resume that meets a dead child swaps a new
        // one in, and a callback that had captured the original would keep
        // sending browser_close into a transport whose peer is gone.
        Idle = new BrowserIdleTimer(
            location.FullPath,
            idlePeriod,
            async token =>
            {
                var closed = await CloseBrowserAsync(Child, sessionLock, token).ConfigureAwait(false);

                // ⚠️ AFTER the close and never before it, and only when there was
                // something in the job besides the node child -- which is what
                // says a browser tree was up and is now dead, and therefore that
                // upstream's registry holds a descriptor nothing else will ever
                // unlink. A close that found no browser wrote no descriptor.
                if (closed.ProcessesBefore > 1)
                {
                    _reap.Start(ServerRegistryReap.AfterIdleClose);
                }

                return closed;
            },
            logging.Factory.CreateLogger<BrowserIdleTimer>(),
            clock);
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
    /// </remarks>
    public const string IdleCloseWhy =
        "BrowserAI closed this session's browser itself: nothing had been forwarded through the session for the idle period, "
        + "so the browser tree was released and the node child kept. Nothing was lost -- the next call relaunches the browser and answers normally.";

    private readonly ServerRegistryReap _reap;
    private ChildConnection _child;
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

    /// <summary>The <c>@playwright/mcp</c> child driving it, which is not the same one for the session's whole life.</summary>
    /// <remarks>
    /// ⚠️ <b>Replaceable since 2026-09-17, and everything that reads it has to
    /// read it through this property and not capture it.</b>
    /// <see cref="ReplaceChildAsync"/> swaps in a child started by
    /// <c>browserai_resume</c> after the original died; a caller holding the old
    /// reference would go on talking to a transport whose peer is gone, which is
    /// the wedge the replacement exists to end.
    /// </remarks>
    public ChildConnection Child => Volatile.Read(ref _child);

    /// <summary>What this session's child was launched with.</summary>
    /// <remarks>
    /// <b>Kept, not recomputed</b>, so a relaunch is the same launch: the
    /// same payload, the same browsers root, the same generated config file and
    /// the same working directory, down to the bytes on the command line.
    /// </remarks>
    public ChildProcessOptions Launch { get; }

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
    /// The one timer: this session's browser is closed once nothing has driven it
    /// for <see cref="BrowserIdleTimer.Period"/>, and the node child is kept.
    /// </summary>
    /// <remarks>
    /// It belongs to this lifetime and not to the manager because everything
    /// it acts on does: one session is one child, one job and one log, and a
    /// timer owned anywhere else would need a way to name a session that has
    /// already gone.
    /// </remarks>
    public BrowserIdleTimer Idle { get; }

    /// <summary>
    /// Swaps in a child started to replace one that died, and tears the dead one
    /// down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The old connection is disposed and not dropped, and that is the
    /// containment half, not tidiness.</b> Disposing it closes the job
    /// handle, and closing the job handle is what ends anything still alive
    /// inside it -- a browser tree whose <c>node</c> parent died but which the
    /// kernel has not been told about is exactly the state this method is
    /// reached in.
    /// </para>
    /// <para>
    /// <b>The swap happens first.</b> A call arriving mid-replacement reaches
    /// the new child and not the one being torn down, which is the ordering
    /// a caller can actually be answered under.
    /// </para>
    /// </remarks>
    /// <param name="replacement">The child to drive this session from now on. This object owns it.</param>
    /// <returns>A task that completes once the dead child has been torn down.</returns>
    public async ValueTask ReplaceChildAsync(ChildConnection replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        var previous = Interlocked.Exchange(ref _child, replacement);

        if (ReferenceEquals(previous, replacement))
        {
            return;
        }

        await previous.DisposeAsync().ConfigureAwait(false);
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

        // The timer first, so it cannot send a close into a child that is being
        // torn down -- and so a close already in flight is waited for here
        // instead of failing noisily against a closed transport.
        await Idle.DisposeAsync().ConfigureAwait(false);

        // ⚠️ READ BEFORE THE CHILD GOES, because afterwards there is no job to
        // ask. More than the node child in it means a browser tree is about to
        // die, and therefore that a descriptor in Playwright's registry is about
        // to become one nothing else will ever unlink -- see the reap below.
        var browserWasUp = Child.JobProcessIds().Count > 1;

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
    /// Closes this session's browser and keeps its node child, by calling
    /// upstream's own tool.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The tool is the mechanism, and nothing else would be.</b> Killing the
    /// browser out of the job would take the node child with it -- the job is per
    /// child, which is the containment contract -- and there is no other lever:
    /// Playwright owns the browser process, so asking Playwright is the only way
    /// to put it down without putting the session down too.
    /// </para>
    /// <para>
    /// <b>Measured 2026-08-16 against <c>@playwright/mcp</c> 0.0.79, twice.</b>
    /// The tool's own result text reads <c>await page.close()</c> and <i>"No open
    /// tabs"</i>, which reads as closing a tab and is not: closing the last page
    /// tears the persistent context down, and every process under the browsers
    /// root goes with it while the node child stays. Called again with no browser
    /// open it answers the same text and is not an error, so a close that races
    /// anything costs a round trip and not a failure.
    /// </para>
    /// <para>
    /// ⚠️ <b>IT WRITES A ROW, and it wrote nothing at all until 2026-08-26.</b>
    /// The close talks to the child directly and never touched
    /// <see cref="Lock"/>; while <c>browserai.log</c> existed the event survived
    /// there, and P2 deleted that file -- so an autonomous browser close became
    /// invisible in the only record there is, and a reader met an unexplained gap
    /// in wall-clock time followed by a silent relaunch.
    /// </para>
    /// <para>
    /// <b>Written the way every forwarded call's row is written</b>:
    /// <c>in-flight</c> before the call reaches the child, settled from the
    /// child's own answer. The ordering is not decoration here either -- a close
    /// that hangs, or one whose child has died, leaves the row unsettled, which
    /// is exactly the state <c>browserai_catch_up</c> renders as <i>"no answer
    /// was recorded"</i>. Writing it afterwards would lose precisely the closes
    /// anybody investigates.
    /// </para>
    /// <para>
    /// ⚠️ <b>A record that cannot be written does NOT stop the close, and that is
    /// the opposite of the rule at the caller's door.</b> A forwarded call is
    /// refused when its row will not write, because the caller can retry and a
    /// gap nobody is told about is worse. There is no caller here and nothing to
    /// refuse to: declining to close would leave a browser tree up for the life
    /// of the session to protect a log line. The failure is logged and the close
    /// proceeds.
    /// </para>
    /// </remarks>
    private static async Task<BrowserCloseResult> CloseBrowserAsync(
        ChildConnection child,
        SessionLock sessionLock,
        CancellationToken cancellationToken)
    {
        var before = child.JobProcessIds().Count;
        long? row = null;

        try
        {
            row = sessionLock.Append(BrowserCloseTool, IdleCloseWhy);
        }
        catch (Exception failure) when (failure is SqliteException or ObjectDisposedException)
        {
            SessionLog.IdleCloseNotRecorded(sessionLock.Logger, sessionLock.Location.FullPath, failure);
        }

        var answer = await child.AskAsync(
            RequestMethods.ToolsCall,
            new JsonObject
            {
                ["name"] = BrowserCloseTool,
                ["arguments"] = new JsonObject(),
            },
            cancellationToken).ConfigureAwait(false);

        if (answer.Response is null)
        {
            var why = answer.ProtocolFailure?.Message
                ?? answer.TransportFailure?.Message
                ?? "the child answered with neither a result nor an error";

            Settle(sessionLock, row, SessionStore.Failed, Encoding.UTF8.GetBytes(why));

            return new BrowserCloseResult(before, child.JobProcessIds().Count, why);
        }

        Settle(sessionLock, row, SessionStore.Successful, failure: null);

        return new BrowserCloseResult(before, child.JobProcessIds().Count, Failure: null);
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
