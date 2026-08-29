// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using BrowserAI.Hosting;
using BrowserAI.Storage;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Sessions;

/// <summary>
/// Ownership of one session directory: the open handle on
/// <c>browserai.lock</c> that proves it, and the <c>browserai.data</c> store
/// beside it that says what the session has done.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two files, two questions, and neither answers the other's.</b>
/// <c>browserai.lock</c> says <i>who owns this directory</i>: it is held
/// <c>FileAccess.ReadWrite, FileShare.Read</c>, so a second BrowserAI asking
/// for write access is refused by the kernel while every reader is admitted,
/// and the OS releases it when the holder dies however it dies.
/// <c>browserai.data</c> says <i>what happened here</i>, and readers go to it
/// directly — which is why the guard never has to admit anybody to itself.
/// </para>
/// <para>
/// ⚠️ <b>THE LOCK IS WRITTEN ONCE, AT ACQUISITION, AND NEVER AGAIN — and that
/// single change dissolves two hazards at once.</b> The record this replaces
/// was durably rewritten and renamed on <i>every forwarded call</i>, so its
/// name was unbound for milliseconds at a time: a prober landing there had to
/// answer <i>undetermined</i> rather than <i>free</i>, and a peer's transient
/// handle could refuse the writer's own re-open. Nothing rewrites this file, so
/// an absence is an absence, the per-call unheld window is gone, and the only
/// rename left is the one inside the per-directory gate at acquisition.
/// </para>
/// <para>
/// <b>Acquisition never waits.</b> On contention this returns immediately with
/// the holder's pid, its start time, when the lock was taken and the recorded
/// purpose. Whether to retry, and for how long, is the calling model's
/// decision: BrowserAI cannot know what a wait costs its caller, and a timer
/// inside the server converts a fact the agent could act on into an unexplained
/// delay. The one wait in this file is the per-directory gate, which is held
/// for milliseconds around create-or-take.
/// </para>
/// <para>
/// ⚠️ <b>The pre-gate probe is a sound ownership test and an unsound freedom
/// test, and the whole design is that asymmetry.</b> A sharing violation is the
/// kernel's answer and no mutex ever made it more true, so a probe that can say
/// <i>held, by X</i> may refuse immediately. Anything else — a lock file that
/// opened, no lock file at all, a denied open — <b>must</b> fall through to the
/// unchanged <see cref="MachineMutex.Create"/> →
/// <c>Acquire(PerDirectoryGate)</c> → <c>TakeOrReport</c> path.
/// [The adversarial review](../../../docs/reviews/2026-08-18-adversarial-locking.md),
/// D, is why the constraint is absolute: with the gate skipped on the free path
/// the rename retry loop becomes the serialiser, and a retry loop is not a lock
/// — it hands the name to whoever happens to be retrying when the incumbent
/// lets go.
/// </para>
/// <para>
/// <b>An append is an <c>INSERT</c>, not a rewrite.</b> A forwarded call adds
/// one row to <c>browserai.data</c> in autocommit; a purpose change adds one
/// row to <c>statements</c>. Neither takes the per-directory gate, because
/// neither replaces a file — the gate exists to make create-or-take atomic and
/// nothing else in this class needs it any more.
/// </para>
/// <para>
/// <b>Two callers on one session are the design, so this object is thread-safe
/// about its own lifetime.</b> Nothing above <c>SessionManager</c> serialises
/// tool calls, so a <c>browserai_set_purpose</c> and a
/// <c>browserai_destroy</c> naming the same directory arrive at one instance
/// concurrently. Every writing path and <b>both</b> disposal paths hold
/// <see cref="_inProcess"/> for their whole body — which is
/// [adversarial review B4](../../../docs/reviews/2026-08-18-adversarial-locking.md)
/// rather than defensive programming, and which is also what serialises the
/// <c>INSERT</c>-then-<c>last_insert_rowid</c> pair on one SQLite connection.
/// </para>
/// </remarks>
internal sealed class SessionLock : IDisposable
{
    /// <summary>
    /// The in-process half of the same exclusion: every writing path and
    /// <b>both</b> disposal paths hold this for their whole body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-08-24 for
    /// [adversarial review B4](../../../docs/reviews/2026-08-18-adversarial-locking.md),
    /// which is the one finding in that review whose failure does not heal.</b>
    /// <c>SessionManager</c> serialises nothing — its <c>_live</c> is a
    /// <c>ConcurrentDictionary</c> and is the only synchronisation there is —
    /// so two tool calls naming one session run concurrently by design. A
    /// <see cref="_disposed"/> check <i>outside</i> the exclusion is a decision
    /// taken by reading a field another thread is writing, and narrowing that
    /// window does not close it: the disposal disposes the very object a
    /// blocked writer would wake holding.
    /// </para>
    /// <para>
    /// ⚠️ <b>It carries a second job since the store arrived.</b> A
    /// <c>SQLite</c> connection is one connection: <see cref="Append"/> is an
    /// <c>INSERT</c> followed by <c>last_insert_rowid</c>, and two threads
    /// interleaving there would hand one call the other's row id — after which
    /// a settle lands on somebody else's entry. The pair is atomic because it
    /// runs in here.
    /// </para>
    /// <para>
    /// <b>It is not a fourth lock scope.</b> <see cref="LockScopes"/> names the
    /// machine-wide scopes — three named kernel objects, in one place — and
    /// this is a private field with no name, no kernel object and no reach
    /// outside this instance.
    /// </para>
    /// </remarks>
    private readonly Lock _inProcess = new();

    private readonly MachineMutex _gate;
    private readonly LockFileHold _hold;
    private readonly SessionStore _store;
    private readonly IDisposable? _logScope;
    private long _opening;
    private int _disposed;

    private SessionLock(
        SessionPath location,
        MachineMutex gate,
        LockFileHold hold,
        SessionStore store,
        SessionRecord record,
        long opening,
        bool gateWasAbandoned,
        ILogger logger)
    {
        _opening = opening;
        Location = location;
        _gate = gate;
        _hold = hold;
        _store = store;
        Record = record;
        GateWasAbandoned = gateWasAbandoned;
        Logger = logger;

        // The session on every record written while this lock is held. The
        // provider was given scope support at build-order step 2 for this.
        _logScope = logger.BeginScope($"session={location.FullPath}");
    }

    /// <summary>The canonicalised directory this lock owns.</summary>
    public SessionPath Location { get; }

    /// <summary>
    /// The logger this lock's own records go to, scoped to this session.
    /// </summary>
    /// <remarks>
    /// <b>Exposed 2026-08-26 for the one writer that is not a caller.</b> The
    /// idle browser close writes a row of its own and has no caller's logger to
    /// borrow — it runs from a timer, off any request — so a failure to record it
    /// belongs in the same session-scoped sink as every other failure about this
    /// store. It is deliberately not a general seam: nothing here may be used to
    /// give the lock a second logger.
    /// </remarks>
    public ILogger Logger { get; }

    /// <summary>The record as it stood when this lock last wrote a statement.</summary>
    /// <remarks>
    /// <b>Refreshed when a statement is appended and not when a log row is.</b>
    /// A statement changes what the session <i>is</i> — its purpose, its
    /// directory — and every answer this product composes reads those; a log
    /// row changes only how much of a log there is, which is counted at the
    /// moment somebody asks.
    /// </remarks>
    public SessionRecord Record { get; private set; }

    /// <summary>
    /// Whether the per-directory mutex was found abandoned when this lock was
    /// taken — a previous holder died inside create-or-take.
    /// </summary>
    /// <remarks>
    /// Surfaced rather than swallowed. The acquisition itself was never in
    /// doubt; what an abandoned mutex reports is that the protected state may
    /// be torn, and that is the only warning the OS gives.
    /// </remarks>
    public bool GateWasAbandoned { get; }

    /// <summary>
    /// Takes the directory, or says who has it — immediately, either way.
    /// </summary>
    /// <param name="location">The canonicalised session directory.</param>
    /// <param name="request">Browser and purpose for the new record.</param>
    /// <param name="logger">Where the acquisition is recorded.</param>
    /// <returns>The outcome, the lock when there is one, and a sentence for the caller.</returns>
    /// <remarks>
    /// Synchronous, and that is not an oversight: a named mutex is owned by the
    /// thread that waited on it, and a continuation resuming elsewhere makes
    /// the release throw about "an unsynchronized block of code".
    /// </remarks>
    public static SessionLockResult TryAcquire(SessionPath location, SessionLockRequest request, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(logger);

        if (!Directory.Exists(location.FullPath))
        {
            return new SessionLockResult(
                SessionLockOutcome.DirectoryMissing,
                $"'{location.FullPath}' does not exist. A session directory is created by BrowserAI, never assumed: create the directory, or name one that is already a session.");
        }

        // ⚠️ BEFORE THE PROBE, BEFORE THE GATE, AND BEFORE ANYTHING IS WRITTEN.
        // A directory holding the old record is not a session this build can
        // take, and taking it would put a `browserai.lock` beside a
        // `browserai.json` that still claims to be the guard.
        if (SessionLayout.OldFormatRefusal(location) is { } notThisFormat)
        {
            return new SessionLockResult(SessionLockOutcome.NotThisFormat, notThisFormat);
        }

        // ⚠️ IN FRONT OF THE GATE, NEVER INSTEAD OF IT. Everything below this
        // line is unchanged, and that is the design rather than caution -- see
        // this type's own remarks.
        if (ProbeForHolder(location, logger) is { } refusal)
        {
            return refusal;
        }

        // Declared before the try and disposed unconditionally in the finally,
        // set to null the moment ownership moves into the returned lock.
        MachineMutex? gate = null;

        try
        {
            try
            {
                gate = MachineMutex.Create(location.MutexName);
            }
            catch (Exception failure) when (failure
                is UnauthorizedAccessException
                or WaitHandleCannotBeOpenedException
                or IOException
                or NotSupportedException)
            {
                // No machine-wide lock means no lock, and therefore no session.
                // There is deliberately no Local\ retry: a Local\ mutex reports
                // success while serialising nothing across logon sessions, which
                // is the one arrangement where two BrowserAIs open one browser
                // profile without either being able to detect it.
                SessionLog.NoMachineWideLock(logger, location.MutexName, failure);

                return new SessionLockResult(
                    SessionLockOutcome.Refused,
                    SessionErrors.NoMachineWideLock(location.FullPath, location.MutexName, failure.Message));
            }

            // A non-null alias, because `gate` itself is set to null on transfer
            // and the release below must still find the object it waited on.
            var owned = gate;
            var acquisition = owned.Acquire(LockScopes.PerDirectoryGate);

            if (acquisition is MutexAcquisition.NotAcquired)
            {
                // ⚠️ The old sentence here asserted "that section takes
                // milliseconds, so something is wrong that waiting longer will
                // not fix", and it was measured false on 2026-08-18: at 200
                // processes released together against one directory, 73 refusals
                // of 796 arrived here purely because each peer enters this gate
                // in turn to discover the file is held. Waiting longer was the
                // whole remedy.
                return new SessionLockResult(
                    SessionLockOutcome.Busy,
                    $"'{location.FullPath}' is being opened or closed by another BrowserAI, and the queue for it did not clear within {LockScopes.PerDirectoryGate.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds. " +
                    "Only processes trying to TAKE this directory queue here — one that merely wanted to know who holds it would already have been told — so either a process is wedged inside create-or-take, or more BrowserAI processes are opening this one directory at once than that will serve. Nothing was changed. " +
                    "Wait and call again, or give this session a directory of its own, which is the arrangement this design expects and which never queues at all.");
            }

            if (acquisition is MutexAcquisition.AcquiredAbandoned)
            {
                // R3. The wait SUCCEEDED and this thread owns the mutex; what
                // the exception reported is that a previous holder died inside
                // create-or-take. Proceeding is mandatory -- letting it escape
                // disables locking permanently after the first crash, and
                // nothing reports it.
                SessionLog.GateWasAbandoned(logger, location.MutexName, location.FullPath);
            }

            try
            {
                var result = TakeOrReport(location, request, logger, owned, acquisition is MutexAcquisition.AcquiredAbandoned);

                if (result.Taken)
                {
                    gate = null;
                }

                return result;
            }
            finally
            {
                // Held for the create-or-take section only -- single
                // milliseconds -- whether or not it was taken.
                owned.Release();
            }
        }
        finally
        {
            gate?.Dispose();
        }
    }

    /// <summary>
    /// Appends one <c>in-flight</c> row and answers with its id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>It is written BEFORE the call it describes is forwarded, and that
    /// ordering is the property rather than an implementation detail.</b> A
    /// navigation that hangs, a child that dies, a process that is killed — the
    /// calls anybody investigates — still left a row saying what they were for.
    /// A row written on the way back would be missing from exactly those.
    /// </para>
    /// <para>
    /// <b>It throws rather than swallowing.</b> A call BrowserAI could not
    /// record is not forwarded: the whole point of one time-ordered log is that
    /// reading it back tells you what the session did, and a gap nobody is told
    /// about is worse than a refusal somebody can act on.
    /// </para>
    /// <para>
    /// <b>An <c>INSERT</c>, in autocommit, with no gate and no rename.</b> The
    /// record this replaces paid a whole-file <c>WriteThrough</c> plus a rename
    /// per call — 3.94 ms at 1 KB rising to 13.62 ms at 400 KB, with the record
    /// absent-and-unheld for the whole window
    /// ([HAZARDS](../../../HAZARDS.md#hazard-index)).
    /// </para>
    /// </remarks>
    /// <param name="tool">The tool name, verbatim, whatever the caller said.</param>
    /// <param name="why">What the caller said the call was for.</param>
    /// <returns>The row's id, for the settle that follows.</returns>
    /// <exception cref="SqliteException">The row could not be written.</exception>
    /// <exception cref="ObjectDisposedException">This lock has been released.</exception>
    public long Append(string tool, string why)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(why);

        lock (_inProcess)
        {
            ObjectDisposedException.ThrowIf(_disposed is not 0, this);

            return _store.AppendLog(
                SessionRecordReader.Stamp(DateTimeOffset.Now),
                RecordText.Sanitise(tool),
                RecordText.Sanitise(why),
                SessionStore.InFlight);
        }
    }

    /// <summary>
    /// Settles a row that was written before its call was forwarded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A failure payload only on failure.</b> A call that succeeded stores
    /// nothing beyond the fact and the instant: the child's own answer is the
    /// caller's, has already been returned byte-identical, and putting a copy
    /// of it in the record would make the record the traffic rather than the
    /// reasons.
    /// </para>
    /// <para>
    /// <b>A settle after the session is gone is dropped, not thrown.</b>
    /// <c>browserai_destroy</c> tears a session down while calls may still be
    /// in flight — that is the tool working — and an exception on the way back
    /// from the child would replace the child's own answer with a complaint
    /// about bookkeeping.
    /// </para>
    /// </remarks>
    /// <param name="id">The row from <see cref="Append"/>.</param>
    /// <param name="outcome">
    /// <see cref="SessionStore.Successful"/> or <see cref="SessionStore.Failed"/>.
    /// </param>
    /// <param name="failure">Why it failed, or <see langword="null"/> when it did not.</param>
    public void Settle(long id, string outcome, byte[]? failure)
    {
        lock (_inProcess)
        {
            if (_disposed is not 0)
            {
                return;
            }

            try
            {
                if (!_store.Settle(id, outcome, SessionRecordReader.Stamp(DateTimeOffset.Now), failure))
                {
                    SessionLog.OutcomeLanded(Logger, id, Location.FullPath);
                }
            }
            catch (SqliteException refused)
            {
                // The answer is already on its way back to the caller. What is
                // lost is the outcome of one row, which `browserai_catch_up`
                // renders as "no answer was recorded" -- a true statement about
                // the record rather than a failed call.
                SessionLog.OutcomeNotRecorded(Logger, id, Location.FullPath, refused);
            }
        }
    }

    /// <summary>
    /// Settles the row the acquisition itself wrote, once, whichever way the
    /// call that took the directory ended.
    /// </summary>
    /// <remarks>
    /// <b>An acquisition is a call like any other and it can fail after its row
    /// exists.</b> <c>browserai_init</c> writes its purpose before the browser
    /// is launched — deliberately, so a launch that hangs still left a record of
    /// what the directory was for — and the launch is exactly the part that
    /// hangs. Settling here is what stops a session whose child never started
    /// reading back as a call still in flight for the rest of the directory's
    /// life.
    /// </remarks>
    /// <param name="outcome">How the opening call ended.</param>
    /// <param name="failure">Why it failed, or <see langword="null"/> when it did not.</param>
    public void SettleOpening(string outcome, byte[]? failure)
    {
        var id = Interlocked.Exchange(ref _opening, 0);

        if (id is not 0)
        {
            Settle(id, outcome, failure);
        }
    }

    /// <summary>
    /// Says what this session is now for, by adding a statement rather than by
    /// replacing one.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>This is what killed the concatenation.</b> A resume with a purpose
    /// used to build the next value out of the whole of the previous one —
    /// <c>$"{record.Purpose} | {appended}"</c> — which grew quadratically and,
    /// at the 2,000-character cap, silently dropped the tail of the sentence a
    /// caller had just written. A row is a row; "current" means the newest.
    /// </remarks>
    /// <param name="purpose">What the session is for, sanitised by the caller.</param>
    /// <exception cref="SqliteException">The statement could not be written.</exception>
    /// <exception cref="ObjectDisposedException">This lock has been released.</exception>
    public void AppendPurpose(string purpose)
    {
        ArgumentNullException.ThrowIfNull(purpose);

        lock (_inProcess)
        {
            ObjectDisposedException.ThrowIf(_disposed is not 0, this);

            _store.Append(new StoredStatement(
                RecordFields.Purpose,
                SessionRecordReader.Stamp(DateTimeOffset.Now),
                purpose));

            Record = SessionRecordReader.Read(_store);
        }
    }

    /// <summary>
    /// Releases the directory and deletes what is left of it, with the release
    /// and the delete inside <b>one hold</b> of the per-directory gate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This exists because <c>browserai_destroy</c>'s destructive act has to
    /// happen while the directory is still ours, and its last nodes cannot be
    /// removed while it is.</b> Windows will not unlink a file this process is
    /// holding open, so <c>browserai.lock</c> — and therefore the directory
    /// above it — can only go after the handle does. The instant between is
    /// exactly the instant a peer's <see cref="TryAcquire"/> reclaims the
    /// directory and launches a browser into a tree that is about to be
    /// deleted. Every BrowserAI takes the per-directory gate before
    /// create-or-take, so holding it across the release makes that instant
    /// unobservable.
    /// </para>
    /// <para>
    /// ⚠️ <b>Added 2026-08-18 (previously the caller released the lock and then
    /// walked the tree for a size before deleting it).</b> The comment
    /// defending that release said <i>"the lock has done its job the moment
    /// ownership is proven"</i>, which is the defect stated as a
    /// justification. Found by
    /// [the adversarial review](../../../docs/reviews/2026-08-18-adversarial-locking.md),
    /// A1.
    /// </para>
    /// <para>
    /// <b>A gate that could not be taken narrows the window rather than
    /// abandoning the delete.</b> Refusing here would leave a directory whose
    /// guard names a holder that has finished with it, and a session nobody
    /// destroys is worse than the race this closes.
    /// </para>
    /// </remarks>
    /// <param name="delete">
    /// Removes what is left. It runs <b>after</b> the handles are closed and
    /// <b>before</b> the gate is released, so it may unlink
    /// <c>browserai.lock</c> and <c>browserai.data</c> and no peer can be
    /// inside create-or-take while it does.
    /// </param>
    /// <exception cref="ObjectDisposedException">This lock has already been released.</exception>
    public void ReleaseAndDelete(Action delete)
    {
        ArgumentNullException.ThrowIfNull(delete);

        // Both disposal paths take this, not just Dispose: a per-session lock
        // that covered only one of them would close half of B4 and leave the
        // half that unlinks the directory. See _inProcess.
        lock (_inProcess)
        {
            ObjectDisposedException.ThrowIf(Interlocked.Exchange(ref _disposed, 1) is not 0, this);

            var acquisition = _gate.Acquire(LockScopes.PerDirectoryGate);

            try
            {
                SessionLog.Released(Logger, Location.FullPath);

                // The store first: closing the last connection checkpoints and
                // removes the write-ahead log, so what the delete below meets is
                // one file rather than three.
                _store.Dispose();
                _hold.Dispose();
                delete();
            }
            finally
            {
                if (acquisition is not MutexAcquisition.NotAcquired)
                {
                    _gate.Release();
                }

                _logScope?.Dispose();
                _gate.Dispose();
            }
        }
    }

    /// <summary>
    /// Releases the directory. Both files stay: a guard that outlives its
    /// holder is what makes a stale lock a sentence rather than a refusal, and
    /// the record is the whole of what the session did.
    /// </summary>
    public void Dispose()
    {
        // ⚠️ IT WAITS FOR AN IN-FLIGHT WRITE RATHER THAN RACING ONE. Disposing
        // _gate underneath a writer that is holding it is B4. See _inProcess.
        lock (_inProcess)
        {
            if (Interlocked.Exchange(ref _disposed, 1) is not 0)
            {
                return;
            }

            SessionLog.Released(Logger, Location.FullPath);

            _store.Dispose();
            _hold.Dispose();
            _logScope?.Dispose();
            _gate.Dispose();
        }
    }

    /// <summary>
    /// Takes a session directory <b>without writing anything into it</b>, or
    /// says why it could not be taken.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the sweep's ownership test, and it is deliberately not
    /// <see cref="TryAcquire"/>.</b> The rule it implements is race R1 —
    /// <i>the sweep may only kill a browser whose directory lock it can itself
    /// acquire, and it holds that lock for the whole kill</i> — which is what
    /// stops one process sweeping away a browser that a second process,
    /// mid-<c>init</c> on the same directory, has just launched.
    /// </para>
    /// <para>
    /// <b>But "whose lock we can acquire" must not mean
    /// <see cref="TryAcquire"/>, and that distinction is the whole reason this
    /// method exists.</b> A sweep is not opening a session, and
    /// <see cref="TryAcquire"/> would write a new <c>browserai.lock</c> naming
    /// the sweeper and add a holder row to the crashed session's own history. A
    /// janitor is the last party that should be editing the evidence.
    /// </para>
    /// <para>
    /// <b>THIS METHOD opens the guard and never the store.</b> The question is
    /// *is this directory ours to act on*, which the kernel answers on one
    /// <c>CreateFile</c>; opening the record would tell it nothing it acts on and
    /// would create a <c>-shm</c> in a directory it is only visiting.
    /// </para>
    /// <para>
    /// ⚠️ ***Corrected 2026-08-26 (previously "It opens the guard and never the
    /// store", with no subject, which read as a property of the sweep).*** <b>It
    /// was true of this method and false of the pass that calls it.</b>
    /// <c>StraySweep.Pass</c> calls <c>SessionIndex.Sweep</c>, which followed
    /// every entry on the machine through <see cref="ReadRecord"/> — one
    /// <c>SessionStore.OpenForReading</c> per registered session, and the index
    /// is machine-wide. <b>So one process start was one store open per session on
    /// the host</b>, each leaving a <c>-shm</c> and a <c>-wal</c> in a directory
    /// nobody named. Measured 2026-08-26 through the published binary: a second
    /// BrowserAI that had done nothing but <c>initialize</c> — no
    /// <c>tools/call</c> at all — put both files back beside a cleanly-closed
    /// session's store.
    /// </para>
    /// <para>
    /// ⚠️ <b>Closed the same day, at the maintainer's decision, and the sentence
    /// above is once again true of the whole path rather than only of this
    /// method.</b> <c>Sweep</c> walks at <c>SessionIndexDepth.Guard</c>: the
    /// record was never part of its decision — every removable state is settled
    /// by the directory and the guard, and the record only ever filled an
    /// inventory a sweep does not print. <b>A server start opens no store at
    /// all</b>, and the one entry whose record is read in full is the one the
    /// pass is about to act on, where there is nothing to open, because a
    /// removable entry is one whose <c>browserai.data</c> is absent.
    /// <c>SessionIndexTests.ASweepOpensNoSessionsStoreAndLeavesACleanlyClosedOneAtTwoFiles</c>.
    /// </para>
    /// <para>
    /// <b>The per-directory gate is taken and released around the open, and the
    /// file handle outlives it.</b> The gate exists to make create-or-take
    /// atomic, and it must not be held across a process kill; the handle is
    /// what keeps the directory ours meanwhile.
    /// </para>
    /// <para>
    /// <b>A directory with no <c>browserai.lock</c>, one holding the old format,
    /// or one held by somebody else all answer the same way: not ours to act
    /// on.</b> Every one of those is a refusal to kill, which is the only
    /// direction this method is allowed to be wrong in.
    /// </para>
    /// </remarks>
    /// <param name="location">The canonicalised session directory.</param>
    /// <param name="hold">The hold, when one was taken.</param>
    /// <returns>Why it could not be held, or <see langword="null"/> when it was.</returns>
    public static string? TryHoldUnowned(SessionPath location, out SessionDirectoryHold? hold)
    {
        ArgumentNullException.ThrowIfNull(location);

        hold = null;

        // Declared before the try and disposed unconditionally in the finally,
        // the same shape TryAcquire uses for the same reason.
        MachineMutex? gate = null;

        try
        {
            try
            {
                gate = MachineMutex.Create(location.MutexName);
            }
            catch (Exception failure) when (failure
                is UnauthorizedAccessException
                or WaitHandleCannotBeOpenedException
                or IOException
                or NotSupportedException)
            {
                return $"the machine-wide lock '{location.MutexName}' could not be created ({failure.Message})";
            }

            // R3 again, on the per-directory gate: an abandoned mutex WAS
            // acquired. Proceeding is mandatory -- and here the abandonment
            // carries no extra warning worth acting on, because nothing is
            // written.
            if (gate.Acquire(LockScopes.PerDirectoryGate) is MutexAcquisition.NotAcquired)
            {
                return $"another BrowserAI is inside create-or-take on '{location.FullPath}'";
            }

            LockFileHold? held = null;

            try
            {
                try
                {
                    held = LockFile.Hold(location.LockFile);
                }
                catch (Exception failure) when (failure is FileNotFoundException or DirectoryNotFoundException)
                {
                    return $"'{location.FullPath}' holds no '{SessionLayout.LockFileName}', so it is not a BrowserAI session";
                }
                catch (IOException failure) when (RenameWindow.IsSharingViolation(failure))
                {
                    return $"'{location.FullPath}' is held by a live session";
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
                {
                    return $"'{location.LockFile}' could not be opened ({failure.Message})";
                }

                // CA2000: ownership of the handle moves into the returned hold,
                // which the caller disposes, and the rule's dataflow cannot see
                // a transfer into an out parameter.
#pragma warning disable CA2000
                hold = new SessionDirectoryHold(location, held);
#pragma warning restore CA2000
                held = null;
                return null;
            }
            finally
            {
                held?.Dispose();
                gate.Release();
            }
        }
        finally
        {
            gate?.Dispose();
        }
    }

    /// <summary>Reads a session's record without taking it.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>READING A SESSION DIRECTORY IS NOT SIDE-EFFECT-FREE, measured
    /// 2026-08-26 rather than reasoned about.</b> Against a crashed holder's
    /// uncheckpointed write-ahead log this read-only open <i>recovers</i> the
    /// log and answers with the newest rows —
    /// <c>SQLITE_OPEN_READONLY</c> constrains the database file and not the
    /// directory — and building the wal-index leaves a <c>-shm</c> beside the
    /// store in a directory the caller only asked to look at. Where the caller
    /// may not create that file the open is refused instead, and the session
    /// stays unreadable until somebody opens it for writing, which the next
    /// acquisition does. That refusal is the right way round: a reader that
    /// silently ignored the <c>-wal</c> would answer confidently with a
    /// session's history as of its last checkpoint.
    /// </para>
    /// <para>
    /// <b>It says nothing about whether anybody holds the directory</b>, and no
    /// caller may infer that from the newest holder statement.
    /// <see cref="ProbeLiveness"/> is that question.
    /// </para>
    /// </remarks>
    /// <param name="location">The canonicalised session directory.</param>
    /// <param name="patience">
    /// How long a transient refusal is waited out. <see cref="TimeSpan.Zero"/>
    /// for a caller that would rather answer without the record than wait for
    /// it.
    /// </param>
    /// <returns>The record, or <see langword="null"/> if there is no store.</returns>
    /// <exception cref="SessionRecordException">There is a record and it cannot be acted on.</exception>
    public static SessionRecord? ReadRecord(SessionPath location, TimeSpan? patience = null)
    {
        ArgumentNullException.ThrowIfNull(location);

        if (SessionLayout.OldFormatRefusal(location) is { } notThisFormat)
        {
            throw new SessionRecordException(notThisFormat);
        }

        if (!File.Exists(location.DataFile))
        {
            return null;
        }

        var budget = patience ?? RenameWindow.Budget;
        var clock = Stopwatch.StartNew();
        var delay = 5;

        while (true)
        {
            try
            {
                using var store = SessionStore.OpenForReading(location.DataFile);

                return SessionRecordReader.Read(store);
            }
            catch (SqliteException refused)
            {
                // ⚠️ TWO CODES ARE WAITED OUT AND NOTHING ELSE IS, AND THE LINE
                // BETWEEN THEM IS WHAT MAKES THIS A WAIT RATHER THAN A RETRY
                // LOOP. `SQLITE_BUSY` and `SQLITE_IOERR`'s shared-memory arms are
                // what a reader meets while a holder is DYING: its handles on the
                // `-wal` and the `-shm` are closing while this open is mapping
                // them, and the condition ends by itself in milliseconds.
                // Everything else is an answer: `SQLITE_NOTADB` is a file that is
                // not ours, and `SQLITE_CANTOPEN` is a directory this process may
                // not create the wal-index in -- which P1 pinned as a refusal on
                // purpose, because answering from a stale checkpoint instead
                // would be the confident-wrong-answer class this repository keeps
                // closing. Waiting either of those out would spend the budget and
                // then say the same thing.
                var transient = refused.Result is Sqlite.Busy or Sqlite.IoError;

                if (!transient || clock.Elapsed >= budget)
                {
                    throw new SessionRecordException(
                        $"'{location.DataFile}' is there and BrowserAI could not read it: {refused.Message}",
                        refused);
                }

                Thread.Sleep(delay);
                delay = Math.Min(delay * 2, 100);
            }
        }
    }

    /// <summary>
    /// Whether anything holds a session directory at the instant of the look —
    /// <b>held, not held, or neither</b> — without taking anything, without
    /// opening a process handle and <b>without opening the record</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One <c>CreateFile</c> on <c>browserai.lock</c> and nothing else.</b>
    /// Measured 2026-08-20 against the file this replaces, over 2,000
    /// iterations, 3 runs: <b>0.035 ms</b> free and <b>0.049 ms</b> held — the
    /// held arm costs more because it is a managed exception rather than a
    /// return
    /// ([kb](../../../kb/windows/detection.md#the-pre-gate-probe-as-a-liveness-report--measured-2026-08-20)).
    /// <b>It must never consult the store.</b> A database open is orders of
    /// magnitude dearer, it can create files in a directory nobody asked it to
    /// touch, and the newest holder row cannot answer this question anyway.
    /// </para>
    /// <para>
    /// ⚠️ <b>The gate is gone from this path, and the reason is that the file it
    /// asks about is now written once.</b> Between 2026-08-20 and 2026-08-24
    /// <c>browserai_list</c> read a bare probe's *not held* as free and printed
    /// <i>in use: no</i> about a session another agent was driving — because
    /// every forwarded call rewrote the record, dropping and retaking the
    /// ownership handle. Nothing rewrites <c>browserai.lock</c>. The only
    /// window left is between the rename and the first hold at acquisition,
    /// inside the per-directory gate, and what a reporting caller sees there is
    /// *free* about a directory somebody is in the middle of taking — a
    /// momentary truth that corrects itself rather than a stale one that does
    /// not.
    /// </para>
    /// <para>
    /// <b>The three answers are not symmetrical.</b> A sharing violation may be
    /// read as <see cref="SessionLiveness.Held"/>; a denied open or a device
    /// error is <see cref="SessionLiveness.Undetermined"/> and carries a
    /// reason. <see cref="SessionLiveness.NotHeld"/> is a snapshot rather than
    /// a claim on the directory, which is why <see cref="ProbeForHolder"/>
    /// still falls through to the gate on it.
    /// </para>
    /// <para>
    /// ⚠️ <b>THIS OPEN CANNOT BE MADE HARMLESS.</b> To be refused by a holder's
    /// <c>FileShare.Read</c> the probe must ask for access outside
    /// <c>Read</c>; a handle whose granted access is outside <c>Read</c> is
    /// exactly what an open sharing only <c>Read</c> is refused by. Detecting
    /// an owner and blocking one are the same capability, so no share mode
    /// dissolves it — it is absorbed on the other side, at
    /// <see cref="LockFile.TakeAndWrite"/>'s hold, which runs under the gate
    /// and therefore cannot meet an owner.
    /// </para>
    /// </remarks>
    /// <param name="location">The canonicalised session directory.</param>
    /// <returns>The state, and a reason whenever it is not settled.</returns>
    public static SessionLivenessAnswer ProbeLiveness(SessionPath location)
    {
        ArgumentNullException.ThrowIfNull(location);

        var answer = LockFile.Probe(location.LockFile);

        return answer.State switch
        {
            LockFileState.Held => new SessionLivenessAnswer(SessionLiveness.Held, null),
            LockFileState.Released => new SessionLivenessAnswer(SessionLiveness.NotHeld, null),
            LockFileState.Free => new SessionLivenessAnswer(SessionLiveness.NotHeld, null),
            _ => new SessionLivenessAnswer(SessionLiveness.Undetermined, answer.Why),
        };
    }

    /// <summary>
    /// Answers <i>who holds this directory</i> without taking the
    /// per-directory gate — or answers nothing at all, and lets the gate
    /// decide.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The problem it removes.</b> Every process that wanted to know who
    /// held a session took <see cref="LockScopes.PerDirectoryGate"/>, losers
    /// included, and a loser only wants to read a name so it can report it. The
    /// cost is super-linear in the number of contenders: measured on an idle
    /// machine, 16 contenders produced a slowest refusal of 367 ms, 100 — the
    /// charter's design point — produced 3,349 ms, and 200 reached the
    /// then-five-second gate and came back <c>Busy</c> by queueing alone
    /// ([kb](../../../kb/windows/detection.md#named-mutexes-and-lock-files)).
    /// <b>The gate was being taken to answer a question the kernel had already
    /// answered.</b>
    /// </para>
    /// <para>
    /// <b>It short-circuits only when it can NAME the holder.</b> A lock file
    /// that is held but unreadable falls through to the gate, which reports it
    /// better because it holds the gate while it looks.
    /// </para>
    /// </remarks>
    /// <param name="location">The canonicalised session directory.</param>
    /// <param name="logger">Where the refusal is recorded.</param>
    /// <returns>
    /// A refusal naming the holder, or <see langword="null"/> when the question
    /// must be settled under the gate.
    /// </returns>
    private static SessionLockResult? ProbeForHolder(SessionPath location, ILogger logger)
    {
        if (ProbeLiveness(location).State is not SessionLiveness.Held)
        {
            // Opened, absent or denied outright -- and this caller cannot act on
            // any of them. The gate can.
            return null;
        }

        LockFileHolder? holder;

        try
        {
            holder = LockFile.Read(location.LockFile);
        }
        catch (Exception failure) when (failure is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            // Damage is a real answer, but it is not "held by X", and
            // TakeOrReport reports it better because it holds the gate while it
            // looks.
            return null;
        }

        if (holder is null)
        {
            return null;
        }

        SessionLog.Contended(logger, location.FullPath, holder.ProcessId);

        return HeldBy(location, holder, PurposeOf(location));
    }

    /// <summary>
    /// What a held session says it is for, or <see langword="null"/> when that
    /// cannot be read.
    /// </summary>
    /// <remarks>
    /// <b>Only ever for a refusal, and never for a decision.</b> The purpose is
    /// what makes <i>this directory is busy</i> actionable — a model reading
    /// <i>"held by PID 8124, which is checking out the staging cart"</i> knows
    /// whether to wait or to pick another directory. A record that cannot be
    /// opened costs that clause and nothing else, so every failure here is
    /// swallowed rather than propagated into an ownership answer the kernel
    /// already settled.
    /// </remarks>
    /// <param name="location">The session directory.</param>
    /// <returns>The purpose, or <see langword="null"/>.</returns>
    private static string? PurposeOf(SessionPath location)
    {
        try
        {
            // ⚠️ NO PATIENCE, AND THAT IS THE POINT OF THE PARAMETER. This runs
            // on the contended path, in front of the gate, for a caller whose
            // whole business is one sentence -- so a reader that waited a dying
            // holder out here would put that wait in front of every refusal, on
            // exactly the path the pre-gate probe exists to keep fast.
            return ReadRecord(location, TimeSpan.Zero)?.Purpose;
        }
        catch (SessionRecordException)
        {
            return null;
        }
    }

    private static SessionLockResult TakeOrReport(
        SessionPath location,
        SessionLockRequest request,
        ILogger logger,
        MachineMutex gate,
        bool gateWasAbandoned)
    {
        // ⚠️ THE OWNERSHIP TEST, UNDER THE GATE. `LockFile.Read` shares write
        // and Delete, so it CANNOT be the test: a live holder does not refuse
        // it. The probe asks for ReadWrite, which a holder's FileShare.Read
        // does refuse, and that sharing violation is the kernel's own answer.
        var probe = LockFile.Probe(location.LockFile);

        if (probe.State is LockFileState.Held)
        {
            return Contended(location, logger);
        }

        if (probe.State is LockFileState.Undetermined)
        {
            SessionLog.UnreadableLockFile(logger, location.LockFile, new IOException(probe.Why));

            return new SessionLockResult(
                SessionLockOutcome.Unreadable,
                SessionErrors.LockFileCannotBeOpened(location.FullPath, location.LockFile, probe.Why ?? "the open failed", RenameWindow.Budget));
        }

        LockFileHolder? previousHolder;

        try
        {
            previousHolder = LockFile.Read(location.LockFile);
        }
        catch (InvalidDataException failure)
        {
            // We can have the name but cannot understand the guard. Refusing is
            // the whole point of strict parsing, so nothing is overwritten.
            SessionLog.UnreadableLockFile(logger, location.LockFile, failure);
            return new SessionLockResult(SessionLockOutcome.Unreadable, failure.Message);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            SessionLog.UnreadableLockFile(logger, location.LockFile, failure);

            return new SessionLockResult(
                SessionLockOutcome.Unreadable,
                SessionErrors.LockFileCannotBeOpened(location.FullPath, location.LockFile, failure.Message, RenameWindow.Budget));
        }

        SessionRecord? previous;

        try
        {
            previous = ReadRecord(location);
        }
        catch (SessionRecordException failure)
        {
            SessionLog.UnreadableLockFile(logger, location.DataFile, failure);
            return new SessionLockResult(SessionLockOutcome.Unreadable, failure.Message);
        }

        // ⚠️ ASKED HERE BECAUSE THE RECORD IS ALREADY READ AND THE GATE IS
        // ALREADY HELD. `init`'s pre-gate look is ungated by construction; the
        // same question here costs one comparison and cannot be asked at the
        // wrong instant, because a peer taking a directory holds this gate to do
        // it. Nothing is written and nothing is taken -- a refusal that changed
        // the record would destroy the evidence the caller is being sent to look
        // at.
        if (previous is not null && request.RefuseAnExistingRecord)
        {
            return new SessionLockResult(
                SessionLockOutcome.AlreadyASession,
                SessionErrors.SessionAlreadyExists(
                    location.FullPath,
                    previous.Browser,
                    previous.Created,
                    previous.LastUsed,
                    previous.Purpose),
                holder: previous);
        }

        var previousRunning = previousHolder is not null && previousHolder.IsAlive();

        var now = DateTimeOffset.Now;
        var holder = LockFileHolder.ForThisProcess();

        LockFileHold? hold = null;
        SessionStore? store = null;
        SessionLock? taken = null;

        try
        {
            try
            {
                // Temp file, durable write, rename -- and this is the only rename
                // `browserai.lock` will ever see, because everything the session
                // goes on to say goes into the store beside it.
                LockFile.Write(location.LockFile, holder);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                SessionLog.CouldNotWriteLockFile(logger, location.LockFile, failure);

                return new SessionLockResult(
                    SessionLockOutcome.Unreadable,
                    $"BrowserAI could not write '{location.LockFile}' ({failure.Message}), so the directory was not taken and nothing was changed. Check that the volume has space and that the directory is writable.");
            }

            // ⚠️ A SECOND CATCH RATHER THAN A SECOND STATEMENT IN THE FIRST ONE,
            // AND THE REASON IS THE SENTENCE ABOVE. The write is a rename of a
            // fully-formed guard over the name: once it returns,
            // `browserai.lock` HAS been replaced and it names this process. A
            // failure here is therefore the one case where "nothing was changed"
            // is false at the moment it is said -- and a caller acting on it
            // reads the reclaim it meets on the next call as somebody else's
            // crashed session rather than as its own last attempt.
            //
            // It does not try to undo the write, deliberately. Restoring the
            // previous guard means a second durable write along the path that
            // just refused us, and deleting it means a delete on the same path;
            // either can fail in turn, and the answer would then have to describe
            // a rollback that half happened. Naming the state exactly is cheaper
            // and cannot make it worse.
            try
            {
                hold = RenameWindow.WaitOutWhereNoOwnerIsPossible(() => LockFile.Hold(location.LockFile));
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                SessionLog.CouldNotWriteLockFile(logger, location.LockFile, failure);

                return new SessionLockResult(
                    SessionLockOutcome.Unreadable,
                    $"BrowserAI replaced '{location.LockFile}' and could not then hold it ({failure.Message}), so the directory was NOT taken -- but the record WAS written, and it now names this process as the holder. "
                    + $"Nothing holds '{location.FullPath}': call again, and the acquisition will report reclaiming it from a process that is still running, which is this one. "
                    + $"A peer that merely looked at the file is already waited out for {RenameWindow.Budget.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s before this is said, so if the call fails the same way, something on this machine is denying that file or holding it open for longer than that.");
            }

            try
            {
                store = SessionStore.OpenForWriting(location.DataFile);

                // ⚠️ ONE TRANSACTION, and this is the only explicit one in the
                // store. The holder row and the statements saying what this
                // session now is are one fact: a reader that saw half of it would
                // see a session held by somebody for no reason, or a reason with
                // no holder.
                store.RecordAcquisition(Compose(location, request, previous, holder, now));
            }
            catch (SqliteException refused)
            {
                // ⚠️ THE GUARD IS ALREADY WRITTEN AND THIS PROCESS IS NAMED IN
                // IT. Saying "nothing was changed" here would be false at the
                // moment it is said. The hold is dropped, so nothing holds the
                // directory; the next call reclaims it from a process that is
                // still running, which is this one.
                SessionLog.CouldNotWriteLockFile(logger, location.DataFile, refused);

                return new SessionLockResult(
                    SessionLockOutcome.Unreadable,
                    $"BrowserAI took '{location.LockFile}' and could not then write '{location.DataFile}' ({refused.Message}), so the session was NOT opened. "
                    + $"Nothing holds '{location.FullPath}': call again, and the acquisition will report reclaiming it from a process that is still running, which is this one. "
                    + "Check that the volume has space, that the directory is writable, and that it is not on a filesystem without shared memory — a network share is the usual cause.");
            }

            // ⚠️ `in-flight`, AND SETTLED BY THE CALLER RATHER THAN HERE. The
            // acquisition succeeded; what has not happened yet is the browser
            // launch, and a row written `successful` at this line would say a
            // session opened when what it means is that a directory was taken.
            var opening = request.Entry is { } entry
                ? store.AppendLog(
                    SessionRecordReader.Stamp(now),
                    RecordText.Sanitise(entry.Tool),
                    RecordText.Sanitise(entry.Why),
                    SessionStore.InFlight)
                : 0;

            var record = SessionRecordReader.Read(store);

            // CA2000 is disabled for this one statement and nothing else. The
            // pattern the rule asks for is already here -- locals declared
            // before the try, unconditional disposal in the finally and null
            // assignments the instant ownership moves -- but the transfer is
            // into the RETURNED SessionLockResult, and the rule's dataflow
            // cannot see an ownership move into an object that is not itself
            // disposable.
#pragma warning disable CA2000
            taken = new SessionLock(location, gate, hold, store, record, opening, gateWasAbandoned, logger);
#pragma warning restore CA2000

            hold = null;
            store = null;

            var result = previous is null && previousHolder is null
                ? Fresh(location, logger, taken)
                : Reclaimed(location, logger, taken, previousHolder, previous, previousRunning, holder);

            taken = null;
            return result;
        }
        finally
        {
            store?.Dispose();
            hold?.Dispose();
            taken?.Dispose();
        }
    }

    /// <summary>
    /// The statements one acquisition makes: a row for every field whose value
    /// has moved, plus a holder row every time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing is overwritten and nothing is invented.</b> A field whose
    /// value has not changed gains no row, so a session opened a hundred times
    /// still reports one <c>browser</c> statement — while <c>directory</c>
    /// gains one the moment the tree is moved or copied, which is what lets
    /// <c>resume</c> hand a model the provenance instead of demanding an
    /// acknowledgement for it.
    /// </para>
    /// <para>
    /// <b>The holder row is the one dedup does not bound</b>, because
    /// <c>(pid, creationFileTime)</c> is never the same twice. That is the
    /// point: it is a history of acquisitions, and two acquisitions by the same
    /// process would still be two.
    /// </para>
    /// </remarks>
    /// <param name="location">Where the record is being written.</param>
    /// <param name="request">Browser and purpose as the caller asked for them.</param>
    /// <param name="previous">The record found on disk, or <see langword="null"/>.</param>
    /// <param name="holder">Who is taking the directory.</param>
    /// <param name="now">The instant every statement written by this call carries.</param>
    /// <returns>The statements, in the order they should be stored.</returns>
    private static List<StoredStatement> Compose(
        SessionPath location,
        SessionLockRequest request,
        SessionRecord? previous,
        LockFileHolder holder,
        DateTimeOffset now)
    {
        var at = SessionRecordReader.Stamp(now);
        var statements = new List<StoredStatement>();

        add(RecordFields.Directory, location.FullPath, previous?.Directory);
        add(RecordFields.Browser, request.Browser, previous?.Browser);
        add(RecordFields.Purpose, RecordText.Sanitise(request.Purpose), previous?.Purpose);
        add(RecordFields.BrowserAiVersion, BuildVersion.Current, previous?.BrowserAiVersion);

        statements.Add(new StoredStatement(RecordFields.Holder, at, SessionRecordReader.WriteHolder(holder)));

        return statements;

        void add(string field, string value, string? current)
        {
            if (!string.Equals(value, current, StringComparison.Ordinal))
            {
                statements.Add(new StoredStatement(field, at, value));
            }
        }
    }

    private static SessionLockResult Fresh(SessionPath location, ILogger logger, SessionLock taken)
    {
        SessionLog.Acquired(logger, location.FullPath, location.MutexName);
        return new SessionLockResult(SessionLockOutcome.Acquired, $"'{location.FullPath}' is now held by this session.", taken);
    }

    /// <summary>
    /// A directory that already had a guard, a record, or both, is now this
    /// session's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Two records, one outcome, and the split arrived 2026-08-30.</b>
    /// A process re-taking a directory <i>it itself</i> last held is by far the
    /// commonest arrival here — every <c>destroy</c> and every
    /// <c>set_purpose</c> disposes the live session and re-acquires, so the
    /// guard on disk names this very process — and it was logged with the same
    /// sentence as a genuine takeover: <i>previous holder was PID n, still
    /// running: True</i>. That is true word by word and false as a whole. It
    /// reads as <i>a live stranger's directory was taken</i>, which is the one
    /// event on this path that would be worth waking up for, and it was the
    /// only thing 2,081 of 2,081 machine-log acquisitions ever said.
    /// </para>
    /// <para>
    /// <b>The identity is <c>(pid, creationFileTime)</c> and the pid alone is
    /// not enough</b> — Windows reuses pids within seconds, and a stranger
    /// wearing this process's number must read as the takeover it is. The
    /// comparison is against the holder this acquisition just wrote rather than
    /// against a freshly-read identity, so the two cannot disagree.
    /// <c>ClientProcessName</c> is deliberately outside it: it is display-only,
    /// it can be null, and a record's equality including it would make a
    /// self-retake read as a takeover whenever the client name failed to
    /// resolve once.
    /// </para>
    /// <para>
    /// <b>Only the record splits.</b> The outcome stays
    /// <see cref="SessionLockOutcome.Reclaimed"/> and
    /// <c>HolderRunning</c> stays true, because both are answers about the
    /// directory rather than about who is asking, and a caller that branches on
    /// them is right to see no difference — the directory <i>was</i> held and
    /// <i>is</i> now ours. The caller-facing sentence is also unchanged and
    /// still says <i>still running but has let the directory go</i>, which for
    /// this shape is accurate and slightly odd; it is left alone rather than
    /// tuned, because the false story was in the log and moving the refusal text
    /// would be a second change wearing the first one's justification.
    /// </para>
    /// </remarks>
    /// <param name="location">The session directory.</param>
    /// <param name="logger">Where the record goes.</param>
    /// <param name="taken">The acquisition, whose ownership this transfers.</param>
    /// <param name="previousHolder">What the guard on disk named, if it could be read.</param>
    /// <param name="previous">The record on disk, if there was one.</param>
    /// <param name="previousRunning">Whether the previous holder's process is alive.</param>
    /// <param name="holder">This acquisition's own identity.</param>
    /// <returns>The result, carrying the acquisition.</returns>
    private static SessionLockResult Reclaimed(
        SessionPath location,
        ILogger logger,
        SessionLock taken,
        LockFileHolder? previousHolder,
        SessionRecord? previous,
        bool previousRunning,
        LockFileHolder holder)
    {
        var pid = previousHolder?.ProcessId ?? previous?.Holder?.ProcessId ?? 0;
        var created = previousHolder?.ProcessCreatedFileTime ?? previous?.Holder?.ProcessCreatedFileTime ?? 0;
        var since = created is 0 ? previous?.TakenAt ?? DateTimeOffset.Now : DateTimeOffset.FromFileTime(created);

        var ourOwn = previousHolder is not null
            && previousHolder.ProcessId == holder.ProcessId
            && previousHolder.ProcessCreatedFileTime == holder.ProcessCreatedFileTime;

        if (ourOwn)
        {
            SessionLog.ReacquiredOurOwnGuard(logger, location.FullPath, pid);
        }
        else
        {
            SessionLog.Reclaimed(logger, location.FullPath, pid, previousRunning);
        }

        return new SessionLockResult(
            SessionLockOutcome.Reclaimed,
            SessionErrors.LockReclaimed(location.FullPath, pid, since, previousRunning, previous?.Purpose ?? string.Empty),
            taken,
            previous,
            previousRunning);
    }

    private static SessionLockResult Contended(SessionPath location, ILogger logger)
    {
        LockFileHolder? holder;

        try
        {
            holder = LockFile.Read(location.LockFile);
        }
        catch (Exception failure) when (failure is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            SessionLog.UnreadableLockFile(logger, location.LockFile, failure);

            return new SessionLockResult(
                SessionLockOutcome.Held,
                $"'{location.FullPath}' is held by another process, and its '{SessionLayout.LockFileName}' cannot be read to say which: {failure.Message}");
        }

        SessionLog.Contended(logger, location.FullPath, holder?.ProcessId ?? 0);

        return holder is null
            ? new SessionLockResult(
                SessionLockOutcome.Held,
                $"'{location.FullPath}' is held by another process, which removed its '{SessionLayout.LockFileName}' between the refusal and the read. Nothing was changed. Try again, or choose another directory.")
            : HeldBy(location, holder, PurposeOf(location));
    }

    /// <summary>
    /// The one refusal that names a live holder, written once so that the
    /// pre-gate probe and the gated open cannot answer the same fact
    /// differently.
    /// </summary>
    /// <remarks>
    /// <b><c>holderRunning: true</c> is a statement about the handle, not about
    /// the pid.</b> Both call sites arrive here having just been refused by the
    /// kernel on an open of <c>browserai.lock</c>, and Windows releases a
    /// handle when its process dies however it dies — so something is alive
    /// holding it, and that is a stronger fact than any liveness check on the
    /// recorded <c>(pid, creationFileTime)</c> could produce.
    /// </remarks>
    /// <param name="location">The session directory.</param>
    /// <param name="holder">What the lock file says.</param>
    /// <param name="purpose">What the record says the session is for, if it could be read.</param>
    /// <returns>The refusal.</returns>
    private static SessionLockResult HeldBy(SessionPath location, LockFileHolder holder, string? purpose) =>
        new(
            SessionLockOutcome.Held,
            SessionErrors.LockHeld(
                location.FullPath,
                holder.ProcessId,
                holder.ClientProcessName,
                DateTimeOffset.FromFileTime(holder.ProcessCreatedFileTime),

                // ⚠️ The lock file is written once, at acquisition, so its own
                // last-write time IS when the lock was taken -- which is the
                // instant this sentence claims. Nothing rewrites it, so it
                // cannot drift the way `LastUsed` does.
                TakenAt(location),
                purpose ?? string.Empty),
            holderRunning: true,
            guard: holder);

    /// <summary>When the guard was written, which is when the directory was taken.</summary>
    /// <param name="location">The session directory.</param>
    /// <returns>The instant, or now when it cannot be read.</returns>
    private static DateTimeOffset TakenAt(SessionPath location)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTime(location.LockFile));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return DateTimeOffset.Now;
        }
    }
}

/// <summary>
/// A session directory held open for the length of one operation, with nothing
/// written into it.
/// </summary>
/// <remarks>
/// The kernel is the whole mechanism: the handle is <c>FileAccess.ReadWrite,
/// FileShare.Read</c>, so any other BrowserAI opening <c>browserai.lock</c> to
/// take the directory is refused while this lives. Disposing releases it and
/// leaves the directory exactly as it was found.
/// </remarks>
internal sealed class SessionDirectoryHold(SessionPath location, LockFileHold held) : IDisposable
{
    /// <summary>The directory this hold owns.</summary>
    public SessionPath Location { get; } = location;

    /// <inheritdoc />
    public void Dispose() => held.Dispose();
}

/// <summary>What a caller asked for when taking a directory.</summary>
internal sealed record SessionLockRequest
{
    /// <summary>The browser family.</summary>
    public required string Browser { get; init; }

    /// <summary>Free text from the calling model, de-controlled on the way in.</summary>
    public required string Purpose { get; init; }

    /// <summary>
    /// What to record about the call that took the directory, or
    /// <see langword="null"/> to record nothing.
    /// </summary>
    /// <remarks>
    /// <b>Null means "take the directory and say nothing", and exactly one
    /// caller wants that:</b> <c>browserai_destroy</c>, which takes the record
    /// in order to delete it. Every other path has something to record —
    /// <c>init</c> its purpose, <c>resume</c> and <c>set_purpose</c> their
    /// <c>why</c> — and a path that took the directory without saying why it
    /// did would be a gap in the one stream this record exists to keep whole.
    /// </remarks>
    public SessionCall? Entry { get; init; }

    /// <summary>
    /// Whether finding a record already on disk is a <b>refusal</b> rather than
    /// something to reclaim. <see langword="false"/> unless the caller says
    /// otherwise, because reclaiming is what every other path wants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Set by <c>browserai_init</c> and nothing else.</b> `init` means
    /// <i>make a session here</i>, and a directory that already carries a
    /// record has to be `resume`d instead — otherwise the second call silently
    /// rebinds the session's browser family, adding a `chromium` statement to a
    /// Firefox profile's history or the reverse.
    /// </para>
    /// <para>
    /// ⚠️ <b>It is asked UNDER THE GATE because the ungated ask can miss.</b>
    /// `init`'s own pre-gate look — `SessionManager.Existing` — reads the store
    /// with no lock held. Under the gate the record has already been read for
    /// the reclaim path, so the same question costs nothing.
    /// </para>
    /// </remarks>
    public bool RefuseAnExistingRecord { get; init; }
}

/// <summary>
/// One call, as it is recorded before anything is done about it.
/// </summary>
/// <param name="Tool">The tool name, verbatim, whatever the caller said.</param>
/// <param name="Why">
/// What the caller said it was for. For <c>browserai_init</c> this is the
/// purpose, because that call has no separate <c>why</c> and the purpose IS the
/// reason the session exists.
/// </param>
internal sealed record SessionCall(string Tool, string Why);

/// <summary>
/// Whether a session directory is being driven right now, as
/// <see cref="SessionLock.ProbeLiveness"/> answers it.
/// </summary>
/// <remarks>
/// <b>Three values because a report and a decision need different things.</b>
/// The pre-gate probe produced all three internally and threw two of them away,
/// because its only caller could act on one. A reader — <c>browserai_list</c> —
/// has the opposite problem: the value it must never print is a confident
/// <i>free</i> that was really <i>could not tell</i>.
/// </remarks>
internal enum SessionLiveness
{
    /// <summary>
    /// Nothing held it at the instant of the look.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>A snapshot and never a claim.</b> It says an open succeeded once,
    /// or that there is no guard at all; it does not say the directory is free,
    /// and nothing may take a directory on the strength of it —
    /// <see cref="SessionLock.TryAcquire"/> still settles that under the
    /// per-directory gate.
    /// </remarks>
    NotHeld,

    /// <summary>
    /// A live process holds it: the kernel refused an open a holder's
    /// <c>FileShare.Read</c> denies.
    /// </summary>
    /// <remarks>
    /// <b>It says <i>something</i> holds the file, never <i>who</i>.</b> The
    /// guard on disk can name a previous holder, so a caller that turns this
    /// into "held by PID n" is publishing a sentence the probe cannot support.
    /// </remarks>
    Held,

    /// <summary>
    /// Neither could be established, and
    /// <see cref="SessionLivenessAnswer.Why"/> says what stopped it.
    /// </summary>
    Undetermined,
}

/// <summary>
/// What <see cref="SessionLock.ProbeLiveness"/> found, and why when it found
/// nothing.
/// </summary>
/// <param name="State">Which of the three.</param>
/// <param name="Why">
/// The path and the failure a diagnosis starts from. Never
/// <see langword="null"/> for <see cref="SessionLiveness.Undetermined"/>, and
/// always <see langword="null"/> for the other two.
/// </param>
internal readonly record struct SessionLivenessAnswer(SessionLiveness State, string? Why);

/// <summary>How an attempt on a session directory ended.</summary>
internal enum SessionLockOutcome
{
    /// <summary>Taken, and nothing was there before.</summary>
    Acquired,

    /// <summary>Taken, and a previous holder's record was found and kept.</summary>
    Reclaimed,

    /// <summary>Somebody else holds it right now. The message names who.</summary>
    Held,

    /// <summary>
    /// A record was already on disk and the request said that is a refusal.
    /// Nothing was taken and nothing was written.
    /// </summary>
    AlreadyASession,

    /// <summary>Somebody else is inside create-or-take and did not come out.</summary>
    Busy,

    /// <summary>The directory does not exist.</summary>
    DirectoryMissing,

    /// <summary>There is a guard or a record and it cannot be acted on, or cannot be written.</summary>
    Unreadable,

    /// <summary>
    /// The directory holds a session in a format this build does not read. It
    /// is not damage and it is not free, and saying so with the format as the
    /// reason is what stops a caller trying to repair a file that is intact.
    /// </summary>
    NotThisFormat,

    /// <summary>
    /// The machine-wide lock could not be created, so there is no lock and
    /// therefore no session. A hard blocker, never a quiet downgrade.
    /// </summary>
    Refused,
}

/// <summary>
/// The outcome of one attempt, with a sentence written for the model that
/// asked.
/// </summary>
/// <param name="outcome">What happened.</param>
/// <param name="message">What to tell the caller.</param>
/// <param name="acquired">The lock, when there is one.</param>
/// <param name="holder">The previous holder's record, when one was read.</param>
/// <param name="holderRunning">Whether that holder's process is still alive.</param>
/// <param name="guard">Who the guard names, when it could be read.</param>
internal sealed class SessionLockResult(
    SessionLockOutcome outcome,
    string message,
    SessionLock? acquired = null,
    SessionRecord? holder = null,
    bool holderRunning = false,
    LockFileHolder? guard = null)
{
    /// <summary>What happened.</summary>
    public SessionLockOutcome Outcome { get; } = outcome;

    /// <summary>What to tell the caller. Every failure names a recovery.</summary>
    public string Message { get; } = message;

    /// <summary>The lock, when one was taken.</summary>
    public SessionLock? Acquired { get; } = acquired;

    /// <summary>The record that was found, when one was.</summary>
    public SessionRecord? Holder { get; } = holder;

    /// <summary>Whether <see cref="Holder"/>'s process is still running.</summary>
    public bool HolderRunning { get; } = holderRunning;

    /// <summary>
    /// Who <c>browserai.lock</c> names, when it could be read.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Separate from <see cref="Holder"/> because they answer different
    /// questions and are read from different files.</b> This is the guard: the
    /// identity a refusal names, read from the file whose sharing violation
    /// produced the refusal in the first place. <see cref="Holder"/> is the
    /// <i>record</i> — what the session is for, when it was created — which a
    /// contended acquisition may not be able to read at all.
    /// </remarks>
    public LockFileHolder? Guard { get; } = guard;

    /// <summary>Whether the directory was taken.</summary>
    public bool Taken => Acquired is not null;
}

/// <summary>Source-generated log messages for the locking path.</summary>
internal static partial class SessionLog
{
    /// <summary>A directory was taken for the first time.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="directory">The session directory.</param>
    /// <param name="mutex">The per-directory mutex that guarded the take.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Session lock acquired for {Directory} under {Mutex}.")]
    public static partial void Acquired(ILogger logger, string directory, string mutex);

    /// <summary>A directory was taken over from a previous holder.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="directory">The session directory.</param>
    /// <param name="previousProcessId">The previous holder's pid.</param>
    /// <param name="previousStillRunning">Whether that process is still alive.</param>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Session lock reclaimed for {Directory}; previous holder was PID {PreviousProcessId}, still running: {PreviousStillRunning}.")]
    public static partial void Reclaimed(ILogger logger, string directory, int previousProcessId, bool previousStillRunning);

    /// <summary>
    /// A directory was taken by the process the guard on it already named.
    /// </summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="directory">The session directory.</param>
    /// <param name="processId">This process's pid, which is also the guard's.</param>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Its own event rather than a parameter on
    /// <see cref="Reclaimed"/>, because the two are different events wearing
    /// one outcome.</b> A reclaim is <i>somebody else's directory is now
    /// ours</i>; this is <i>we are back in a directory we never left the
    /// machine holding</i>. Every <c>destroy</c> and every <c>set_purpose</c>
    /// produces one, so on the machine-wide log this is not the rare case —
    /// it is the only case, 2,081 of 2,081, and until 2026-08-30 all of them
    /// were logged as reclaims from a live process.
    /// </para>
    /// <para>
    /// <b>It does not say <c>still running</c>, and the omission is the
    /// point.</b> The process is running: it is this one, and it is the one
    /// writing the line. Repeating that here is what made the reclaim sentence
    /// read as an alarm.
    /// </para>
    /// </remarks>
    [LoggerMessage(
        EventId = 12,
        Level = LogLevel.Information,
        Message = "Session lock re-acquired for {Directory} by the process that already held its guard, PID {ProcessId} -- this process. Nothing was taken from anybody.")]
    public static partial void ReacquiredOurOwnGuard(ILogger logger, string directory, int processId);

    /// <summary>Somebody else holds the directory.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="directory">The session directory.</param>
    /// <param name="holderProcessId">The holder's pid, or zero if it could not be read.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Session lock for {Directory} is held by PID {HolderProcessId}; returning immediately.")]
    public static partial void Contended(ILogger logger, string directory, int holderProcessId);

    /// <summary>The per-directory mutex was abandoned by a holder that died.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="mutex">The mutex.</param>
    /// <param name="directory">The session directory it guards.</param>
    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "{Mutex} was abandoned by a process that died holding it; it is now acquired and {Directory} is being taken. Whatever that process was writing may be incomplete.")]
    public static partial void GateWasAbandoned(ILogger logger, string mutex, string directory);

    /// <summary>The machine-wide object could not be created at all.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="mutex">The name that could not be created.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Error,
        Message = "Could not create the machine-wide lock {Mutex}. No session was created; BrowserAI does not fall back to a logon-session-scoped lock.")]
    public static partial void NoMachineWideLock(ILogger logger, string mutex, Exception? failure);

    /// <summary>A guard or a record exists and cannot be acted on.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="path">The file.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "{Path} could not be read and the session was not taken.")]
    public static partial void UnreadableLockFile(ILogger logger, string path, Exception failure);

    /// <summary>The guard or the record could not be written.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="path">The file.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(EventId = 7, Level = LogLevel.Error, Message = "{Path} could not be written and the session was not taken.")]
    public static partial void CouldNotWriteLockFile(ILogger logger, string path, Exception failure);

    /// <summary>The lock was released.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="directory">The session directory.</param>
    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Session lock released for {Directory}; the guard stays so a later reader can say who had it.")]
    public static partial void Released(ILogger logger, string directory);

    /// <summary>A settle matched no row.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="id">The row that was not there.</param>
    /// <param name="directory">The session directory.</param>
    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Warning,
        Message = "The outcome of log row {Id} in {Directory} matched no row, so that call will read as never answered.")]
    public static partial void OutcomeLanded(ILogger logger, long id, string directory);

    /// <summary>A settle was refused by the store.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="id">The row.</param>
    /// <param name="directory">The session directory.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Warning,
        Message = "The outcome of log row {Id} in {Directory} could not be written; the call itself was answered.")]
    public static partial void OutcomeNotRecorded(ILogger logger, long id, string directory, Exception failure);

    /// <summary>
    /// The idle close's own row could not be written, and the close went ahead
    /// anyway.
    /// </summary>
    /// <remarks>
    /// <b>The opposite decision from the one at the caller's door</b>, and it is
    /// recorded rather than implied: a forwarded call whose row will not write is
    /// refused, because a caller can retry. Nobody asked for this close, so
    /// declining it would leave a browser tree up for the life of the session to
    /// protect a log line.
    /// </remarks>
    /// <param name="logger">Where it goes.</param>
    /// <param name="directory">The session directory.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Warning,
        Message = "The idle browser close for {Directory} could not be recorded; the browser was closed anyway, so the log will not show it.")]
    public static partial void IdleCloseNotRecorded(ILogger logger, string directory, Exception failure);
}
