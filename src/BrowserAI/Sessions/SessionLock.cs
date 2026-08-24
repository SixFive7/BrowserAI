// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using BrowserAI.Hosting;
using BrowserAI.Interop;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Sessions;

/// <summary>
/// Ownership of one session directory: the open handle on <c>browserai.json</c> that
/// proves it, and the record inside that says who and why.
/// </summary>
/// <remarks>
/// <para>
/// <b>The handle is the lock.</b> <c>browserai.json</c> is held
/// <c>FileAccess.ReadWrite, FileShare.Read</c> — a second BrowserAI asking for
/// write access is refused by the kernel, while any reader can still say who
/// holds it and why. The OS releases the handle when the holder dies, however it
/// dies, so <i>stale</i> and <i>alive</i> are distinguishable without guessing.
/// </para>
/// <para>
/// <b>Acquisition never waits.</b> On contention this returns immediately with
/// the holder's pid, its start time, when the lock was taken and the recorded
/// purpose. Whether to retry, and for how long, is the calling model's decision:
/// BrowserAI cannot know what a wait costs its caller, and a timer inside the
/// server converts a fact the agent could act on into an unexplained delay. The
/// one wait in this file is the per-directory gate, which is held for
/// milliseconds around create-or-take.
/// </para>
/// <para>
/// ⚠️ <b>"Immediately" was not true of a contender until 2026-08-18, and the gap
/// was the gate.</b> A loser took the per-directory mutex too — behind every peer
/// naming the same directory — purely to discover the file was held and read a
/// name out of it. <see cref="ProbeForHolder"/> now answers that question in
/// front of the gate, because the sharing violation is the kernel's answer and
/// the mutex never made it more true. <b>It refuses; it never acquires.</b>
/// Anything the probe cannot settle falls through to the gate unchanged, and the
/// reason that constraint is absolute is written out at
/// <see cref="ProbeForHolder"/>.
/// </para>
/// <para>
/// <b>Two requirements collide here, and the collision is real.</b> The
/// paragraph above makes the <i>open handle</i> the lock. Durability makes the
/// <i>atomic rename</i> the way the record is put in place: a plain write returns
/// once the bytes are in the file-system cache, so a power loss between the write
/// and the flush leaves a file the writer believes it wrote — and <c>browserai.json</c>
/// is the one file whose loss cannot be reconstructed, because it is the entire
/// ownership guard. So it is written <c>WriteThrough</c>, flushed with
/// <c>Flush(true)</c>, and moved over the previous copy. <b>Measured 2026-08-16: a
/// rename cannot replace a file whose
/// handle is open, under any share mode.</b> Not <c>FileShare.Read</c>, not
/// <c>Read | Delete</c>, not <c>ReadWrite | Delete</c> — all three fail
/// identically, with <c>ERROR_ACCESS_DENIED</c> rather than the sharing
/// violation one would expect, because <c>MoveFileEx</c> asks for DELETE on the
/// destination. So there is no share mode that dissolves the collision, and the
/// resolution has to be close → rename → re-open, performed <b>entirely inside
/// the per-directory mutex</b>. That is exactly what the mutex is for: every
/// BrowserAI takes it before create-or-take, so the instants in which nobody
/// holds the name are instants in which nobody else can <i>take</i> it. See
/// <c>SessionLockTests.ARenameCannotReplaceALockFileWhoseOwnHandleIsStillOpen</c>,
/// which walks all three share modes on every run.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-19 (previously "instants in which nobody else can
/// look").</b> A peer can look, and does: <see cref="ProbeForHolder"/> runs in
/// front of the gate by design, so it opens <c>browserai.json</c> inside that gap and
/// its handle — held for one <c>FileStream</c> construction — refuses the gate
/// holder's own re-open. <b>Nobody else can take it, which is the property the
/// gate was for; nobody else can look was never true and the design never needed
/// it.</b> The re-open therefore goes through <see cref="ReopenHeld"/>, which
/// waits a transient handle out because under the gate no owner can be behind
/// it.
/// </para>
/// <para>
/// <b>Two callers on one session are the design, so this object is
/// thread-safe about its own lifetime.</b> Nothing above
/// <c>SessionManager</c> serialises tool calls, so a
/// <c>browserai_set_purpose</c> and a <c>browserai_destroy</c> naming the same
/// directory arrive at one instance concurrently. Every mutating path and
/// <b>both</b> disposal paths therefore hold <see cref="_inProcess"/> for their
/// whole body — which is where the reasoning lives, and which is
/// [adversarial review B4](../../../docs/reviews/2026-08-18-adversarial-locking.md)
/// rather than defensive programming.
/// </para>
/// <para>
/// <b>The error code is load-bearing, not trivia.</b> <c>ERROR_ACCESS_DENIED</c>
/// surfaces as <c>UnauthorizedAccessException</c>, which is <i>not</i> an
/// <c>IOException</c> — so the rename retry below catches both, and a version
/// that caught only <c>IOException</c> would not retry the case that actually
/// happens.
/// </para>
/// </remarks>
internal sealed class SessionLock : IDisposable
{
    /// <summary>
    /// How long the atomic rename keeps retrying before giving up.
    /// </summary>
    /// <remarks>
    /// <b>Measured 2026-08-16, and it replaced a budget that was too small.</b>
    /// Five attempts over 150 ms — the shape the C# prior art on this machine
    /// uses — was observed exhausting under full-suite load, with
    /// <c>ERROR_ACCESS_DENIED</c> on the destination while another process held
    /// <c>browserai.json</c> open for reading in a tight loop. Something briefly
    /// holding the destination is a live condition rather than a bug (a
    /// concurrent reader, a virus scanner opening a file that was just created),
    /// so the answer is a budget generous enough to outlast it — and a bound, so
    /// that a permanent condition is still reported rather than waited on
    /// forever.
    /// <para>
    /// ⚠️ <b>Read from <see cref="RenameWindow.Budget"/> since 2026-08-18, not
    /// re-declared.</b> The number has not changed; what changed is that the
    /// reader side of this same rename now waits the same amount, and two
    /// literals here could drift apart while one cannot.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan MoveBudget = RenameWindow.Budget;

    /// <summary>
    /// The in-process half of the same exclusion: every mutating path and
    /// <b>both</b> disposal paths hold this for their whole body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-08-24 for
    /// [adversarial review B4](../../../docs/reviews/2026-08-18-adversarial-locking.md),
    /// which is the one finding in that review whose failure does not heal.</b>
    /// <c>SessionManager</c> serialises nothing — its <c>_live</c> is a
    /// <c>ConcurrentDictionary</c> and is the only synchronisation there is — so
    /// two tool calls naming one session run concurrently by design. Before
    /// this, <see cref="Rewrite"/> tested <see cref="_disposed"/> and *then* took
    /// <see cref="_gate"/>, while <see cref="Dispose"/> disposed
    /// <see cref="_gate"/> underneath it. A <c>browserai_destroy</c> landing in
    /// that window left the rewrite re-opening <c>browserai.json</c> into a
    /// disposed lock and <c>_gate.Release()</c> throwing — after which
    /// <b>every</b> <see cref="TryAcquire"/> on that directory answered
    /// <c>Held</c>, naming a pid with no session, for the life of the process,
    /// while the destroy reported a partial failure blaming <i>"something still
    /// has them open"</i>.
    /// </para>
    /// <para>
    /// <b>A tighter <see cref="_disposed"/> check would not have done, and the
    /// review says so in as many words.</b> Taking <see cref="_gate"/> before the
    /// check narrows the window and does not close it: the disposal disposes
    /// <see cref="_gate"/> itself, so a rewrite blocked on it wakes holding a
    /// disposed object. A check that races is still a race; what closes it is
    /// something the disposal has to take and cannot destroy.
    /// </para>
    /// <para>
    /// <b>It is not a fourth lock scope.</b> <see cref="LockScopes"/> names the
    /// machine-wide scopes — three named kernel objects, in one place — and this
    /// is a private field with no name, no kernel object and no reach outside
    /// this instance, so <c>SessionLockTests.TheThreeScopesAreNamedInOnePlaceAndAllOfThemAreGlobal</c>
    /// and <c>.TheProductCreatesNamedKernelObjectsInExactlyOnePlace</c> are both
    /// untouched by it. What it serialises is <b>one object's</b> callers; what
    /// <see cref="_gate"/> serialises is the machine's.
    /// </para>
    /// <para>
    /// <b>The order is always this one, then <see cref="_gate"/>.</b>
    /// <see cref="Rewrite"/> and <see cref="ReleaseAndDelete"/> both take them
    /// that way round and nothing takes them the other way, so there is no
    /// lock-ordering cycle to construct. Every delegate this class invokes —
    /// <c>update</c> and <c>delete</c> — runs inside both, which is what makes a
    /// caller's callback part of the critical section rather than a hole in it.
    /// </para>
    /// <para>
    /// <b>What it costs, stated rather than discovered.</b> A disposal now waits
    /// for an in-flight rewrite instead of racing it, and a rewrite runs
    /// <c>WriteThrough</c> plus a rename under <see cref="_gate"/> — so
    /// <c>LiveSession.DisposeAsync</c> can block for as long as one of those
    /// takes. That is the whole point: the alternative is the two overlapping,
    /// which is the defect.
    /// </para>
    /// </remarks>
    private readonly Lock _inProcess = new();

    private readonly MachineMutex _gate;
    private readonly IDisposable? _logScope;
    private readonly ILogger _logger;
    private FileStream _held;
    private int _disposed;

    private SessionLock(
        SessionPath location,
        MachineMutex gate,
        FileStream held,
        LockRecord record,
        bool gateWasAbandoned,
        ILogger logger)
    {
        Location = location;
        _gate = gate;
        _held = held;
        Record = record;
        GateWasAbandoned = gateWasAbandoned;
        _logger = logger;

        // The session on every record written while this lock is held. The
        // provider was given scope support at build-order step 2 for this.
        _logScope = logger.BeginScope($"session={location.FullPath}");
    }

    /// <summary>The canonicalised directory this lock owns.</summary>
    public SessionPath Location { get; }

    /// <summary>The record currently on disk.</summary>
    public LockRecord Record { get; private set; }

    /// <summary>
    /// Whether the per-directory mutex was found abandoned when this lock was
    /// taken — a previous holder died inside create-or-take.
    /// </summary>
    /// <remarks>
    /// Surfaced rather than swallowed. The acquisition itself was never in
    /// doubt; what an abandoned mutex reports is that the protected state may be
    /// torn, and that is the only warning the OS gives.
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
    /// thread that waited on it, and a continuation resuming elsewhere makes the
    /// release throw about "an unsynchronized block of code".
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

        // ⚠️ IN FRONT OF THE GATE, NEVER INSTEAD OF IT. Everything below this
        // line is unchanged, and that is the design rather than caution -- see
        // ProbeForHolder.
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
                // whole remedy. A refusal that misdiagnoses the machine is worse
                // than a bare one, because the model reading it acts on the
                // diagnosis -- so this now names both possibilities and neither
                // is presented as established.
                //
                // ⚠️ And "every process naming it queues here" stopped being true
                // when ProbeForHolder was added: a peer that can name the holder
                // is refused in front of this gate and never creates the mutex,
                // so what queues here is only processes that intend to TAKE the
                // directory and found it unheld a moment ago. That makes reaching
                // this line a much stronger signal than it used to be, and the
                // sentence says so rather than keeping a diagnosis the code can
                // no longer support.
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
    /// Appends one entry to this session's log, under the per-directory gate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It is a whole-record durable write, on every forwarded browser
    /// call.</b> <see cref="Rewrite"/> closes the handle, writes a temp file
    /// <c>WriteThrough</c>, flushes it to disk, renames it over
    /// <c>browserai.json</c> and re-opens — so a busy session pays that per call.
    /// That is the cost of the maintainer's decision to keep the log inside the
    /// record rather than in a sibling append-only file, and it is stated here
    /// rather than discovered: an append-only sibling would have been an
    /// <c>O(entry)</c> write with no rename at all.
    /// </para>
    /// <para>
    /// <b>It throws rather than swallowing.</b> A log entry that could not be
    /// written is exactly the kind of loss this record exists to prevent, so the
    /// caller decides — and the caller is <c>BrowserProxy</c>, which refuses the
    /// call rather than forwarding one it could not record.
    /// </para>
    /// </remarks>
    /// <param name="entry">What the session just did.</param>
    /// <exception cref="IOException">The gate could not be taken, or the record could not be written.</exception>
    public void Append(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Rewrite(record => record with { Log = LockRecord.AppendLog(record.Log, entry) });
    }

    /// <summary>
    /// Rewrites the record durably, keeping ownership across the replacement.
    /// </summary>
    /// <param name="update">Produces the next record from the current one.</param>
    /// <exception cref="IOException">The gate could not be taken, or the record could not be replaced.</exception>
    public void Rewrite(Func<LockRecord, LockRecord> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        // ⚠️ THE DISPOSAL CHECK IS INSIDE THIS LOCK AND MUST STAY THERE. Outside
        // it, the check and the acquisition below are two steps with a window
        // between them, and the object guarding the critical section is disposed
        // by the other thread inside that window -- which is B4, and which is why
        // "test _disposed more carefully" is not a fix. See _inProcess.
        lock (_inProcess)
        {
            ObjectDisposedException.ThrowIf(_disposed is not 0, this);

            if (_gate.Acquire(LockScopes.PerDirectoryGate) is MutexAcquisition.NotAcquired)
            {
                throw new IOException($"Could not take '{_gate.Name}' to rewrite '{Location.LockFile}'.");
            }

            try
            {
                var next = update(Record);

                // The same close/rename/re-open as acquisition, and for the same
                // reason: an atomic rename cannot replace a file we are holding, and
                // the gate is what makes the gap unobservable.
                _held.Dispose();

                try
                {
                    WriteDurably(Location.LockFile, next);
                    _held = ReopenHeld(Location.LockFile);
                    Record = next;
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
                {
                    // A FAILED REWRITE MUST NOT ALSO RELEASE THE LOCK, and without
                    // this it did: the handle is dropped before the replacement, so
                    // an exception on the way through left the session silently
                    // unowned while the caller was told only that a write failed.
                    // Take the name back before letting the failure out.
                    SessionLog.CouldNotWriteLockFile(_logger, Location.LockFile, failure);
                    _held = Reclaim(Location.LockFile, failure);
                    throw;
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private static FileStream Reclaim(string lockFile, Exception cause)
    {
        try
        {
            return ReopenHeld(lockFile);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Both the replacement and the recovery failed, which is the one
            // state this object cannot represent. Say so rather than hand back
            // something that reports ownership it does not have.
            throw new IOException(
                $"'{lockFile}' could not be rewritten ({cause.Message}) and could not be re-opened afterwards ({failure.Message}), so this session no longer holds its directory. Destroy the session and create it again.",
                failure);
        }
    }

    /// <summary>
    /// Releases the directory and deletes what is left of it, with the release
    /// and the delete inside <b>one hold</b> of the per-directory gate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This exists because <c>browserai_destroy</c>'s destructive act has to
    /// happen while the directory is still ours, and its last two nodes cannot
    /// be removed while it is.</b> Windows will not unlink a file this process
    /// is holding open, so <c>browserai.json</c> — and therefore the directory above
    /// it — can only go after the handle does. The instant between is exactly
    /// the instant a peer's <see cref="TryAcquire"/> reclaims the directory and
    /// launches a browser into a tree that is about to be deleted. Every
    /// BrowserAI takes the per-directory gate before create-or-take, so holding
    /// it across the release makes that instant unobservable — the same
    /// mechanism, for the same reason, that makes <see cref="Rewrite"/>'s
    /// close/rename/re-open gap unobservable.
    /// </para>
    /// <para>
    /// ⚠️ <b>Added 2026-08-18 (previously the caller released the lock and then
    /// walked the tree for a size before deleting it).</b> The comment defending
    /// that release said <i>"the lock has done its job the moment ownership is
    /// proven"</i>, which is the defect stated as a justification: ownership was
    /// proven for an instant and the destructive act happened outside it, with a
    /// full recursive walk of a Chromium profile in between. Found by
    /// [the adversarial review](../../../docs/reviews/2026-08-18-adversarial-locking.md),
    /// A1.
    /// </para>
    /// <para>
    /// <b>A gate that could not be taken narrows the window rather than
    /// abandoning the delete.</b> Refusing here would leave a directory whose
    /// record claims a holder that has finished with it, and a session nobody
    /// destroys is worse than the race this closes.
    /// </para>
    /// </remarks>
    /// <param name="delete">
    /// Removes what is left. It runs <b>after</b> the handle is closed and
    /// <b>before</b> the gate is released, so it may unlink <c>browserai.json</c>
    /// and no peer can be inside create-or-take while it does.
    /// </param>
    /// <exception cref="ObjectDisposedException">This lock has already been released.</exception>
    public void ReleaseAndDelete(Action delete)
    {
        ArgumentNullException.ThrowIfNull(delete);

        // Both disposal paths take this, not just Dispose: a per-session lock
        // that covered only one of them would close half of B4 and leave the half
        // that unlinks the directory. See _inProcess.
        lock (_inProcess)
        {
            ObjectDisposedException.ThrowIf(Interlocked.Exchange(ref _disposed, 1) is not 0, this);

            var acquisition = _gate.Acquire(LockScopes.PerDirectoryGate);

            try
            {
                SessionLog.Released(_logger, Location.FullPath);

                _held.Dispose();
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
    /// Releases the directory. <c>browserai.json</c> stays: the holder record
    /// outliving the holder is what makes a stale lock a sentence rather than a
    /// refusal.
    /// </summary>
    public void Dispose()
    {
        // ⚠️ IT WAITS FOR AN IN-FLIGHT MUTATION RATHER THAN RACING ONE. Disposing
        // _gate underneath a rewrite that is holding it is B4: the release then
        // throws, the re-opened handle on browserai.json is never closed, and every
        // later TryAcquire on this directory answers Held naming a pid with no
        // session. See _inProcess.
        lock (_inProcess)
        {
            if (Interlocked.Exchange(ref _disposed, 1) is not 0)
            {
                return;
            }

            SessionLog.Released(_logger, Location.FullPath);

            _held.Dispose();
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
    /// stops one process sweeping away a browser that a second process, mid-
    /// <c>init</c> on the same directory, has just launched.
    /// If <c>browserai.json</c> cannot be opened for write, someone owns the
    /// directory, and the sweep skips it unconditionally.
    /// <c>StraySweepTests</c> carries one test per race, this one included.
    /// </para>
    /// <para>
    /// <b>But "whose lock we can acquire" must not mean <see cref="TryAcquire"/>,
    /// and that distinction is the whole reason this method exists.</b> A sweep is
    /// not opening a session, and <see cref="TryAcquire"/> would rewrite
    /// <c>browserai.json</c> with the sweeper as holder — overwriting a crashed
    /// session's own record, its purpose and its history with a janitor's. That
    /// record is the one piece of evidence about what the stray was, and a
    /// janitor is the last party that should be destroying it.
    /// </para>
    /// <para>
    /// <b>The per-directory gate is taken and released around the open, and the
    /// file handle outlives it.</b> The gate exists to make create-or-take
    /// atomic, and it must not be held across a process kill; the handle is what
    /// keeps the directory ours meanwhile, because a concurrent
    /// <see cref="TryAcquire"/> opens the same file <c>FileAccess.ReadWrite</c>
    /// and is refused by the kernel while we hold it.
    /// </para>
    /// <para>
    /// <b>A directory with no <c>browserai.json</c>, an unparseable one, or one held
    /// by somebody else all answer the same way: not ours to act on.</b> Every
    /// one of those is a refusal to kill, which is the only direction this
    /// method is allowed to be wrong in.
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
            // written and a torn record would be caught by the parse below.
            if (gate.Acquire(LockScopes.PerDirectoryGate) is MutexAcquisition.NotAcquired)
            {
                return $"another BrowserAI is inside create-or-take on '{location.FullPath}'";
            }

            FileStream? held = null;

            try
            {
                try
                {
                    held = OpenHeld(location.LockFile);
                }
                catch (Exception failure) when (failure is FileNotFoundException or DirectoryNotFoundException)
                {
                    return $"'{location.FullPath}' holds no '{SessionLayout.LockFileName}', so it is not a BrowserAI session";
                }
                catch (IOException failure) when (RenameWindow.IsSharingViolation(failure))
                {
                    return $"'{location.FullPath}' is held by a live session";
                }
                catch (UnauthorizedAccessException failure)
                {
                    return $"'{location.LockFile}' could not be opened ({failure.Message})";
                }

                LockRecord? record;

                try
                {
                    record = Parse(held, location.LockFile);
                }
                catch (LockFileException failure)
                {
                    return $"'{location.LockFile}' cannot be read ({failure.Message})";
                }

                if (record is null)
                {
                    return $"'{location.LockFile}' is empty, so nothing proves this directory is a BrowserAI session";
                }

                // CA2000 for the same reason TakeOrReport carries it: ownership
                // of the handle moves into the returned hold, which the caller
                // disposes, and the rule's dataflow cannot see a transfer into
                // an out parameter.
#pragma warning disable CA2000
                hold = new SessionDirectoryHold(location, held, record);
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
    /// <param name="location">The canonicalised session directory.</param>
    /// <returns>The record, or <see langword="null"/> if there is no lock file.</returns>
    /// <exception cref="LockFileException">There is a lock file and it cannot be acted on.</exception>
    public static LockRecord? ReadRecord(SessionPath location)
    {
        ArgumentNullException.ThrowIfNull(location);

        try
        {
            // ⚠️ Through RenameWindow, which is the READ side of the rename this
            // class's own `Replace` performs. A reader that arrives while another
            // process is replacing `browserai.json` is refused ACCESS_DENIED -- not a
            // sharing violation -- and nothing on this path caught it. See that
            // type's remarks for the measurement and for which callers may use it.
            using var stream = RenameWindow.WaitOut(() =>
                // FileShare.ReadWrite because the holder has it open for WRITE, and
                // a reader that does not share write is refused outright -- which
                // would turn "somebody owns this" into "this file is unreadable".
                // FileShare.Delete because the atomic rename needs DELETE on the
                // destination, and one reader without it blocks every rewrite.
                new FileStream(
                    location.LockFile,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 1));

            return Parse(stream, location.LockFile);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Answers <i>who holds this directory</i> without taking the per-directory
    /// gate — or answers nothing at all, and lets the gate decide.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The problem it removes.</b> Every process that wanted to know who held
    /// a session took <see cref="LockScopes.PerDirectoryGate"/>, losers included,
    /// and a loser only wants to read a name so it can report it. So a refusal
    /// waited behind the whole queue rather than behind one critical section, and
    /// the cost is super-linear in the number of contenders: measured on an idle
    /// machine, 16 contenders produced a slowest refusal of 367 ms, 100 — the
    /// charter's design point — produced 3,349 ms, and 200 reached the
    /// then-five-second gate and came back <c>Busy</c> by queueing alone, with
    /// nothing wrong ([kb](../../../kb/windows/detection.md#named-mutexes-and-lock-files)).
    /// <b>The gate was being taken to answer a question the kernel had already
    /// answered.</b>
    /// </para>
    /// <para>
    /// ⚠️ <b>THE PROBE IS A SOUND OWNERSHIP TEST AND AN UNSOUND FREEDOM TEST, and
    /// the whole design is that asymmetry.</b> A sharing violation is the
    /// kernel's answer and no mutex ever made it more true, so a probe that can
    /// say <i>held, by X</i> may refuse immediately. A probe that says
    /// <i>looks free</i> proves nothing and <b>must</b> fall through to the
    /// unchanged <see cref="MachineMutex.Create"/> →
    /// <c>Acquire(PerDirectoryGate)</c> → <see cref="TakeOrReport"/> below.
    /// </para>
    /// <para>
    /// <b>What happens if the free path skips the gate</b>, from
    /// [the adversarial review](../../../docs/reviews/2026-08-18-adversarial-locking.md),
    /// D — this was attacked before it was built, and it is why the constraint is
    /// absolute. Two contenders both probe and both see "free". A writes its
    /// record, renames it into place and holds it. B's rename is refused —
    /// <c>ERROR_ACCESS_DENIED</c>, because A's handle is open — and B
    /// <i>retries</i>, to <see cref="MoveBudget"/>. The instant anything closes
    /// A's handle for a moment — a <see cref="Rewrite"/>, a teardown, a
    /// <c>destroy</c> — B's next retry lands, B renames over A's record and
    /// re-opens it. <b>B now holds <c>browserai.json</c> and A holds a valid handle to
    /// a now-nameless file</b>, which is what a Windows rename over an open file
    /// does to the loser; A finds out only on its next rewrite. Both report
    /// ownership and two processes drive one profile. The mechanism underneath:
    /// with the gate gone, <b>the rename retry loop becomes the serialiser</b>,
    /// and a retry loop is not a lock — it hands the name to whoever happens to
    /// be retrying when the incumbent lets go.
    /// </para>
    /// <para>
    /// <b>Deliberately not through <see cref="RenameWindow"/>.</b> By that type's
    /// own table this is the archetype of a <i>not entitled</i> caller: it exists
    /// to find out whether something else holds the file, so waiting a refusal
    /// out would invert the mechanism. It also cannot tell a delete-pending
    /// destination from a permanent ACL denial — both arrive as
    /// <see cref="UnauthorizedAccessException"/> — so it does not try: anything
    /// that is not a sharing violation falls through and is decided under the
    /// gate, where the window is unobservable.
    /// </para>
    /// <para>
    /// <b>It short-circuits only when it can NAME the holder.</b> A read taken
    /// outside the gate can land in the transient absence between the unlink and
    /// the rename of a record being replaced, and come back <see langword="null"/>
    /// about a holder that removed nothing. Rather than print that hedge, an
    /// unreadable or absent record falls through too — under the gate that window
    /// does not exist, and the answer costs exactly what it cost before this
    /// method was written.
    /// </para>
    /// <para>
    /// <b>The share mode is wider than a holder's, on purpose.</b>
    /// <see cref="OpenHeld"/> asks for <c>FileShare.Read</c> because it intends to
    /// keep the file; this asks for <c>ReadWrite | Delete</c> because it intends
    /// to let go immediately, and a transient handle without <c>Delete</c> would
    /// refuse a concurrent holder's rename for as long as it lived. The
    /// <i>access</i> is what makes the test work and it is unchanged:
    /// <c>ReadWrite</c> is refused by a holder's <c>FileShare.Read</c>, which is
    /// the sharing violation being read as ownership.
    /// </para>
    /// <para>
    /// ⚠️ <b>AND THE HANDLE IT HOLDS FOR THAT INSTANT REFUSES A GATE HOLDER'S
    /// OWN RE-OPEN. Observed 2026-08-19, CI run 32203064556 attempt 1.</b> Widen
    /// the share mode all you like: the <i>granted access</i> is what a
    /// <c>FileShare.Read</c> open is refused by, and the granted access has to be
    /// <c>ReadWrite</c> or this method cannot detect an owner at all.
    /// <b>Detecting an owner and blocking one are the same capability</b>, so
    /// this cannot be fixed here and is not attempted here. It is absorbed on the
    /// other side, by <see cref="ReopenHeld"/>, at the three opens that hold the
    /// gate and therefore cannot meet an owner. What that leaves standing is
    /// narrower and is written down rather than fixed: an ownership test that
    /// meets this handle — <see cref="OpenHeld"/> at <see cref="TakeOrReport"/>'s
    /// first open — still reads it as <c>Contended</c>, which names a holder out
    /// of a record that may be stale. It is in the
    /// [hazard index](../../../HAZARDS.md#hazard-index), and it cannot be
    /// discriminated from a real owner by anything cheaper than waiting, because
    /// <see cref="TryHoldUnowned"/> holds a directory whose record names somebody
    /// else entirely.
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
        // ⚠️ ONE OPENER, TWO CALLERS. `ProbeLiveness` is this method's own open,
        // extracted 2026-08-20 when `browserai_list` needed the same question
        // answered for a session it has no intention of taking. Extracted rather
        // than copied, because a second spelling of "held is a sharing violation
        // and nothing else is free" is exactly how two components come to
        // disagree about one file while both report success.
        if (ProbeLiveness(location).State is not SessionLiveness.Held)
        {
            // Opened, absent, mid-rename or denied outright -- and this caller
            // cannot tell those apart, nor act on any of them. The gate can.
            return null;
        }

        LockRecord? holder;

        try
        {
            holder = ReadRecord(location);
        }
        catch (LockFileException)
        {
            // Damage is a real answer, but it is not "held by X", and TakeOrReport
            // reports it better because it holds the gate while it looks.
            return null;
        }

        if (holder is null)
        {
            return null;
        }

        SessionLog.Contended(logger, location.FullPath, holder.Holder.ProcessId);
        return HeldBy(location, holder);
    }

    /// <summary>
    /// Whether anything holds a session's <c>browserai.json</c> at the instant of the
    /// look — <b>held, not held, or neither</b> — without taking anything and
    /// without opening a process handle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-08-20 for <c>browserai_list</c>, which reported mode,
    /// browser, purpose, dates and size and said nothing at all about whether a
    /// session was being driven.</b> A caller could not tell an abandoned
    /// session from one another agent is inside — which matters most in the one
    /// moment before <c>browserai_destroy</c>. The alternative was the
    /// process-liveness check, and this beats it on both counts: it needs no
    /// process handle, so a token that may not open the peer cannot defeat it,
    /// and it asks about the resource rather than about a pid that may have been
    /// recycled.
    /// </para>
    /// <para>
    /// <b>The three answers are not symmetrical, and the asymmetry is this
    /// namespace's standing rule.</b> A sharing violation may be read as
    /// <see cref="SessionLiveness.Held"/>; <b>nothing else may be read as
    /// free</b>. An absent record, a mid-rename window, a denied open and a
    /// device error are all <see cref="SessionLiveness.Undetermined"/> and carry
    /// a reason — collapsing any of them into <i>free</i> would hand a caller a
    /// confident wrong answer in the direction that costs a session.
    /// <see cref="SessionLiveness.NotHeld"/> is the weakest of the three and
    /// says so in its own summary: it is a snapshot, not a claim on the
    /// directory, and it is why <see cref="ProbeForHolder"/> still falls through
    /// to the gate on it.
    /// </para>
    /// <para>
    /// ⚠️ <b>THIS OPEN CANNOT BE MADE HARMLESS, AND A CALLER THAT RUNS IT OVER A
    /// WHOLE TREE IS WIDENING SOMETHING THAT WAS ALREADY WRITTEN DOWN.</b> To be
    /// refused by a holder's <c>FileShare.Read</c> the probe must ask for access
    /// outside <c>Read</c>; a handle whose granted access is outside <c>Read</c>
    /// is exactly what an open sharing only <c>Read</c> is refused by. Detecting
    /// an owner and blocking one are the same capability, so no share mode
    /// dissolves it — see this method's own remarks on <see cref="ProbeForHolder"/>
    /// and CI run 32203064556 attempt 1. What that leaves is a peer's
    /// <see cref="OpenHeld"/> meeting this handle for the instant it lives and
    /// reading it as <c>Contended</c>: a wrong <i>sentence</i>, never a wrong
    /// owner, and it is in the [hazard index](../../../HAZARDS.md#hazard-index).
    /// <b>What a caller must not do is report that sentence onward.</b>
    /// <c>SessionManager.List</c> names no holder from this answer for exactly
    /// that reason — the record on disk may name a previous one — and it does not
    /// probe a session this process is already driving, because it knows that
    /// one without asking the kernel.
    /// </para>
    /// <para>
    /// <b>Cost: one <c>CreateFile</c> and one <c>CloseHandle</c> per call</b>,
    /// with no directory walk, no process open and no mutex. Measured
    /// 2026-08-20 over 2,000 iterations, 3 runs: <b>0.035 ms</b> when the file
    /// is free and <b>0.049 ms</b> when it is held — the held arm costs more
    /// because it is a managed exception rather than a return
    /// ([kb](../../../kb/windows/detection.md#the-pre-gate-probe-as-a-liveness-report--measured-2026-08-20)).
    /// </para>
    /// </remarks>
    /// <param name="location">The canonicalised session directory.</param>
    /// <returns>The state, and a reason whenever it is not settled.</returns>
    public static SessionLivenessAnswer ProbeLiveness(SessionPath location)
    {
        ArgumentNullException.ThrowIfNull(location);

        try
        {
            using var probe = new FileStream(
                location.LockFile,
                FileMode.Open,
                FileAccess.ReadWrite,

                // Wider than a holder's on purpose: this handle lets go
                // immediately, and one without Delete would refuse a concurrent
                // holder's rename for as long as it lived. The ACCESS is what
                // makes the test work and it is ReadWrite, which is what a
                // holder's FileShare.Read refuses.
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1);

            return new SessionLivenessAnswer(SessionLiveness.NotHeld, null);
        }
        catch (IOException failure) when (RenameWindow.IsSharingViolation(failure))
        {
            // The kernel's own answer to "does somebody own this", and the one
            // answer this method is allowed to give positively.
            return new SessionLivenessAnswer(SessionLiveness.Held, null);
        }
        catch (Exception failure) when (failure is FileNotFoundException or DirectoryNotFoundException)
        {
            // ⚠️ NOT read as free. `browserai.json`'s NAME IS UNBOUND for an instant
            // while a peer replaces the record, and a directory whose record is
            // being replaced is owned by whoever is replacing it. An absence is
            // therefore a window this caller cannot see the inside of.
            return new SessionLivenessAnswer(
                SessionLiveness.Undetermined,
                $"'{location.LockFile}' was not there when this looked, which is a record being replaced as often as it is a session that has gone.");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return new SessionLivenessAnswer(
                SessionLiveness.Undetermined,
                $"'{location.LockFile}' could not be opened, and the failure was not a sharing violation ({failure.Message}).");
        }
    }

    private static SessionLockResult TakeOrReport(
        SessionPath location,
        SessionLockRequest request,
        ILogger logger,
        MachineMutex gate,
        bool gateWasAbandoned)
    {
        LockRecord? previous;
        FileStream? held = null;
        SessionLock? taken = null;

        try
        {
            try
            {
                held = OpenHeld(location.LockFile);
                previous = Parse(held, location.LockFile);
            }
            catch (FileNotFoundException)
            {
                // Free, and never locked. Nothing to reclaim, nothing to report.
                previous = null;
            }
            catch (IOException failure) when (RenameWindow.IsSharingViolation(failure))
            {
                return Contended(location, logger);
            }
            catch (LockFileException failure)
            {
                // We hold the name but cannot understand the record. Refusing is
                // the whole point of strict parsing, so the handle is dropped
                // rather than the file overwritten.
                SessionLog.UnreadableLockFile(logger, location.LockFile, failure);
                return new SessionLockResult(SessionLockOutcome.Unreadable, failure.Message);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // ⚠️ THE ARM THAT USED TO BE MISSING, added 2026-08-19. The three
                // catches above are a missing file, a sharing violation and an
                // unparseable record; an `UnauthorizedAccessException` is not an
                // `IOException` and is none of them, so a permanently denied
                // `browserai.json` PROPAGATED OUT OF `TryAcquire` -- the product's
                // primary session-opening entry point -- after `RenameWindow` had
                // spent its whole budget waiting out a rename that was never in
                // flight. `OpenHeld`'s remarks recorded that this had already
                // happened once and that the wait was the answer; the wait
                // narrows the transient window and does nothing about a
                // permanent denial, which is the case that reaches here.
                //
                // The catch is deliberately wider than the exception that
                // motivated it. Every other `IOException` shape -- a
                // `DirectoryNotFoundException` from a directory removed between
                // the gate and this line, a device error, a name that stopped
                // resolving -- is the same kind of event to the caller: the
                // record could not be read and the directory was not taken. An
                // arm keyed only on the one exception somebody hit is how this
                // gap was made in the first place.
                SessionLog.UnreadableLockFile(logger, location.LockFile, failure);

                return new SessionLockResult(
                    SessionLockOutcome.Unreadable,
                    SessionErrors.LockFileCannotBeOpened(location.FullPath, location.LockFile, failure.Message, MoveBudget));
            }

            // ⚠️ ASKED HERE BECAUSE THE RECORD IS ALREADY READ AND THE GATE IS
            // ALREADY HELD, added 2026-08-19. `init`'s pre-gate look is ungated
            // by construction and can land in the instant in which browserai.json's
            // NAME IS UNBOUND while a peer replaces the record -- it then reads
            // null as "free, proceed" and reaches the reclaim below, which
            // appends a browser statement and rebinds the session's family. The same question here costs one comparison and cannot be
            // asked at the wrong instant, because a peer replacing a record
            // holds this gate to do it.
            //
            // Nothing is written and nothing is taken: the handle opened above
            // is dropped by the finally, which is the same shape the unreadable
            // arm uses and for the same reason -- a refusal that changed the
            // record would destroy the evidence the caller is being sent to look
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

            var previousRunning = previous is not null
                && ProcessLiveness.IsAlive(previous.Holder.ProcessId, previous.Holder.ProcessCreatedFileTime);

            var record = Compose(location, request, previous, DateTimeOffset.Now);

            // Close before renaming: Windows will not replace a file whose
            // handle we are holding, and the gate is why the gap is safe.
            held?.Dispose();
            held = null;

            try
            {
                WriteDurably(location.LockFile, record);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                SessionLog.CouldNotWriteLockFile(logger, location.LockFile, failure);

                return new SessionLockResult(
                    SessionLockOutcome.Unreadable,
                    $"BrowserAI could not write '{location.LockFile}' ({failure.Message}), so the directory was not taken and nothing was changed. Check that the volume has space and that the directory is writable.");
            }

            // ⚠️ A SECOND CATCH RATHER THAN A SECOND STATEMENT IN THE FIRST ONE,
            // AND THE REASON IS THE SENTENCE ABOVE. Corrected 2026-08-19
            // (previously `WriteDurably` and this open shared one catch, which
            // answered "nothing was changed" to both). The write is a rename of
            // a fully-formed record over the name: once it returns, browserai.json
            // HAS been replaced and it names this process as the holder. A
            // failure here is therefore the one case where "nothing was changed"
            // is false at the moment it is said -- and a caller acting on it
            // reads the reclaim it meets on the next call as somebody else's
            // crashed session rather than as its own last attempt.
            //
            // It does not try to undo the write, deliberately. Restoring
            // `previous` means a second WriteDurably along the path that just
            // refused us, and deleting the record when there was no previous one
            // means a delete on the same path; either can fail in turn, and the
            // answer would then have to describe a rollback that half happened.
            // Naming the state exactly is cheaper and cannot make it worse: the
            // record is stale by construction, nothing holds the directory, and
            // the reclaim path already handles a record whose holder is alive.
            //
            // ⚠️ ReopenHeld AND NOT OpenHeld, ADDED 2026-08-19, AND THIS LINE IS
            // THE ONE THE CI FAILURE WAS ABOUT. The record is on disk naming this
            // process and the gate is held, so nothing can be the owner here --
            // becoming one means passing through this method, which needs the
            // gate. A refusal is therefore an ungated transient handle, and the
            // product has exactly one ungated opener that asks for write access:
            // ProbeForHolder, whose handle lives for the length of one
            // FileStream construction. Believing it cost run 32203064556 a lock.
            try
            {
                held = ReopenHeld(location.LockFile);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                SessionLog.CouldNotWriteLockFile(logger, location.LockFile, failure);

                return new SessionLockResult(
                    SessionLockOutcome.Unreadable,
                    $"BrowserAI replaced '{location.LockFile}' and could not then re-open it ({failure.Message}), so the directory was NOT taken -- but the record WAS written, and it now names this process as the holder. "
                    + $"Nothing holds '{location.FullPath}': call again, and the acquisition will report reclaiming it from a process that is still running, which is this one. "
                    + $"A peer that merely looked at the file is already waited out for {MoveBudget.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s before this is said, so if the call fails the same way, something on this machine is denying that file or holding it open for longer than that.");
            }

            // CA2000 is disabled for this one statement and nothing else. The
            // pattern the rule asks for is already here -- a local declared
            // before the try, an unconditional `taken?.Dispose()` in the finally
            // and a null assignment the instant ownership moves -- but the
            // transfer is into the RETURNED SessionLockResult, which is a
            // handle the caller keeps, and the rule's dataflow cannot see an
            // ownership move into an object that is not itself disposable.
            // Making SessionLockResult disposable to satisfy it would be worse:
            // every caller would then hold a `using` over the result and destroy
            // the lock it just acquired.
#pragma warning disable CA2000
            taken = new SessionLock(location, gate, held, record, gateWasAbandoned, logger);
#pragma warning restore CA2000

            // Ownership of the handle has moved into the lock object.
            held = null;

            var result = previous is null
                ? Fresh(location, logger, taken)
                : Reclaimed(location, logger, taken, previous, previousRunning);

            taken = null;
            return result;
        }
        finally
        {
            held?.Dispose();
            taken?.Dispose();
        }
    }

    private static SessionLockResult Fresh(SessionPath location, ILogger logger, SessionLock taken)
    {
        SessionLog.Acquired(logger, location.FullPath, location.MutexName);
        return new SessionLockResult(SessionLockOutcome.Acquired, $"'{location.FullPath}' is now held by this session.", taken);
    }

    private static SessionLockResult Reclaimed(
        SessionPath location,
        ILogger logger,
        SessionLock taken,
        LockRecord previous,
        bool previousRunning)
    {
        var since = DateTimeOffset.FromFileTime(previous.Holder.ProcessCreatedFileTime);
        SessionLog.Reclaimed(logger, location.FullPath, previous.Holder.ProcessId, previousRunning);

        return new SessionLockResult(
            SessionLockOutcome.Reclaimed,
            SessionErrors.LockReclaimed(
                location.FullPath,
                previous.Holder.ProcessId,
                since,
                previousRunning,
                previous.Purpose),
            taken,
            previous,
            previousRunning);
    }

    private static SessionLockResult Contended(SessionPath location, ILogger logger)
    {
        LockRecord? holder;

        try
        {
            holder = ReadRecord(location);
        }
        catch (LockFileException failure)
        {
            SessionLog.UnreadableLockFile(logger, location.LockFile, failure);

            return new SessionLockResult(
                SessionLockOutcome.Held,
                $"'{location.FullPath}' is held by another process, and its '{SessionLayout.LockFileName}' cannot be read to say which: {failure.Message}");
        }

        SessionLog.Contended(logger, location.FullPath, holder?.Holder.ProcessId ?? 0);

        if (holder is null)
        {
            return new SessionLockResult(
                SessionLockOutcome.Held,
                $"'{location.FullPath}' is held by another process, which removed its '{SessionLayout.LockFileName}' between the refusal and the read. Nothing was changed. Try again, or choose another directory.");
        }

        return HeldBy(location, holder);
    }

    /// <summary>
    /// The one refusal that names a live holder, written once so that the
    /// pre-gate probe and the gated open cannot answer the same fact differently.
    /// </summary>
    /// <remarks>
    /// <b><c>holderRunning: true</c> is a statement about the handle, not about
    /// the pid.</b> Both call sites arrive here having just been refused by the
    /// kernel on an open of <c>browserai.json</c>, and Windows releases a handle when
    /// its process dies however it dies — so something is alive holding it, and
    /// that is a stronger fact than any liveness check on the recorded
    /// <c>(pid, creationFileTime)</c> could produce.
    /// </remarks>
    /// <param name="location">The session directory.</param>
    /// <param name="holder">The record read from it.</param>
    /// <returns>The refusal.</returns>
    private static SessionLockResult HeldBy(SessionPath location, LockRecord holder) =>
        new(
            SessionLockOutcome.Held,
            SessionErrors.LockHeld(
                location.FullPath,
                holder.Holder.ProcessId,
                holder.Holder.ClientProcessName,
                DateTimeOffset.FromFileTime(holder.Holder.ProcessCreatedFileTime),

                // ⚠️ TakenAt and not LastUsed. Under schema 2 they are different
                // instants: a `set_purpose` moves LastUsed past the moment the
                // current holder took the directory, and this sentence says
                // "took the lock at".
                holder.TakenAt,
                holder.Purpose),
            holder: holder,
            holderRunning: true);

    /// <summary>
    /// The next record: the previous one with a statement appended to every
    /// field whose value has moved.
    /// </summary>
    /// <remarks>
    /// <b>Nothing is overwritten and nothing is invented.</b> A field whose value
    /// has not changed keeps the list it had, so a session that is opened a
    /// hundred times still reports one <c>browser</c> statement — while
    /// <c>directory</c> gains one the moment the
    /// tree is moved or copied, which is what lets <c>resume</c> hand a model the
    /// provenance instead of demanding an acknowledgement for it.
    /// </remarks>
    /// <param name="location">Where the record is being written.</param>
    /// <param name="request">Browser and purpose as the caller asked for them.</param>
    /// <param name="previous">The record found on disk, or <see langword="null"/> for a directory that has never been locked.</param>
    /// <param name="now">The instant every statement written by this call carries.</param>
    /// <returns>The record to write.</returns>
    private static LockRecord Compose(SessionPath location, SessionLockRequest request, LockRecord? previous, DateTimeOffset now)
    {
        var holder = new LockHolder
        {
            ProcessId = Environment.ProcessId,
            ProcessCreatedFileTime = ProcessLiveness.CreationTimeOfThisProcess(),
            ClientProcessName = ProcessLiveness.ClientProcessName(),
        };

        return new LockRecord
        {
            SchemaVersion = LockRecord.CurrentSchemaVersion,
            DirectoryHistory = LockRecord.Append(previous?.DirectoryHistory, location.FullPath, now),
            BrowserHistory = LockRecord.Append(previous?.BrowserHistory, request.Browser, now),
            PurposeHistory = LockRecord.Append(previous?.PurposeHistory, LockRecord.SanitisePurpose(request.Purpose), now),
            BrowserAiVersionHistory = LockRecord.Append(previous?.BrowserAiVersionHistory, BuildVersion.Current, now),
            HolderHistory = LockRecord.Append(previous?.HolderHistory, holder, now),

            // ⚠️ AN ENTRY OR NOTHING, decided by the caller rather than here.
            // `init` and `resume` both have something to say about why they
            // opened the directory; `browserai_destroy` takes it in order to
            // delete it, and an entry appended to a record that is about to be
            // unlinked is a write nobody reads. `browserai_set_purpose` against
            // a CLOSED session takes it too, and that one does append -- which
            // is the whole reason this is a request field rather than a flag on
            // the method.
            Log = request.Entry is { } entry
                ? LockRecord.AppendLog(previous?.Log, entry)
                : previous?.Log ?? [],
        };
    }

    private static string Stamp(DateTimeOffset moment) => moment.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>
    /// Opens <c>browserai.json</c> the way a holder holds it, <b>as an ownership
    /// test</b>: read-write, sharing only reads, so any other BrowserAI trying to
    /// take the same directory meets a sharing violation — and so does this,
    /// which is the answer rather than a condition to wait out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-19 (previously "every one of the four call sites
    /// needs it … two of them open the file straight after this class has
    /// renamed a new record over it").</b> There are two call sites now, not
    /// four, and both are ownership tests: <see cref="TakeOrReport"/>'s open
    /// before the write and <see cref="TryHoldUnowned"/>'s. The three that open
    /// straight after this class's own rename moved to
    /// <see cref="ReopenHeld"/>, which waits out a sharing violation as well —
    /// they cannot meet an owner, and what they were meeting instead was a
    /// peer's pre-gate probe.
    /// </para>
    /// <para>
    /// ⚠️ <b>Through <see cref="RenameWindow"/>, added 2026-08-18.</b> These open
    /// a file another process may be renaming right now — and a delete-pending
    /// destination refuses an open with <c>ACCESS_DENIED</c>, which
    /// <see cref="TakeOrReport"/> did not catch: it handled the sharing
    /// violation (which means <i>owned</i>, and must not be waited out) and a
    /// <c>LockFileException</c>, so an <see cref="UnauthorizedAccessException"/>
    /// propagated out of <c>TryAcquire</c> — the product's primary entry point
    /// for opening a session.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-19 (previously "which <c>TakeOrReport</c> does not
    /// catch", in the present tense, a year after the wait was added).</b> It now
    /// does: there is a fourth arm on that first open, over
    /// <c>IOException or UnauthorizedAccessException</c>, answering
    /// <see cref="SessionErrors.LockFileCannotBeOpened"/>. <b>The wait and the
    /// catch are answers to two different faults and the repository had been
    /// treating them as one.</b> This wait exists for a rename that IS in flight
    /// and will end; a denial that is permanent — an ACL, an antivirus hold —
    /// consumes the whole budget and then still has to be reported, and until
    /// 2026-08-19 it was reported by throwing. The sentence above described the
    /// gap correctly and read as though the wait had closed it.
    /// </para>
    /// </remarks>
    /// <param name="lockFile">The lock file.</param>
    /// <returns>The held handle.</returns>
    private static FileStream OpenHeld(string lockFile) =>
        RenameWindow.WaitOut(() => Hold(lockFile));

    /// <summary>
    /// The same open, taken by the one process that can own the file, so a
    /// sharing violation is waited out rather than believed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>THE DIFFERENCE FROM <see cref="OpenHeld"/> IS THE PRECONDITION, NOT
    /// THE OPEN.</b> Both produce a holder's handle and the two
    /// <c>FileStream</c> arguments are one method so they cannot drift. What
    /// separates them is who may call: this needs
    /// <c>LockScopes.PerDirectoryGate</c> held <b>and</b> a record on disk this
    /// process has just written or is taking back after its own write failed.
    /// Under that precondition no second owner can exist, so a refusal is an
    /// ungated transient handle — see <see cref="RenameWindow"/>'s table, third
    /// row, for the sharing arithmetic that says the probe cannot be made not to
    /// produce one.
    /// </para>
    /// <para>
    /// <b>Added 2026-08-19, after the interleaving stopped being a prediction.</b>
    /// CI run 32203064556 attempt 1: contender 2652 wrote its record, was
    /// refused on this open by a peer's pre-gate probe, and answered that it had
    /// not taken the directory; contender 696 reclaimed the same directory 61 ms
    /// later, and <c>browserai.json</c> carried two processes' holder statements. The
    /// three call sites are the two post-write re-opens and
    /// <see cref="Reclaim"/>; <c>SessionLockTests.TheOpensThatFollowThisClassesOwnWriteAreTheOnlyOnesThatWaitOutAHandle</c>
    /// fails the build if a fourth appears or one of the three moves back.
    /// </para>
    /// </remarks>
    /// <param name="lockFile">The lock file.</param>
    /// <returns>The held handle.</returns>
    internal static FileStream ReopenHeld(string lockFile) =>
        RenameWindow.WaitOutWhereNoOwnerIsPossible(() => Hold(lockFile));

    /// <summary>
    /// The holder's open itself, written once so the two callers cannot come to
    /// hold the file in different modes.
    /// </summary>
    /// <param name="lockFile">The lock file.</param>
    /// <returns>The handle.</returns>
    private static FileStream Hold(string lockFile) =>
        new(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1);

    private static LockRecord? Parse(FileStream stream, string path)
    {
        stream.Position = 0;

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        // A zero-length browserai.json is treated as "no record" rather than as
        // corruption. Nothing this product writes can produce one -- the record
        // arrives by rename, fully formed -- so it means somebody else made the
        // file, and refusing to act on an empty file would strand a directory
        // that is in fact free.
        return buffer.Length is 0 ? null : LockRecord.Read(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), path);
    }

    private static void WriteDurably(string lockFile, LockRecord record)
    {
        var directory = Path.GetDirectoryName(lockFile)
            ?? throw new IOException($"'{lockFile}' has no directory to write into.");

        // The temp file must be in the target's own directory: a rename is only
        // atomic within one volume, and only cheap within one directory.
        var temp = Path.Combine(directory, SessionLayout.NewLockFileName());

        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.WriteThrough))
            {
                // WriteThrough at open AND Flush(flushToDisk: true) on the way
                // out. A plain Write returns once the bytes are in the
                // filesystem cache, so a power loss between the write and the
                // flush leaves a file the writer believes it wrote -- and
                // browserai.json is the entire ownership guard, the one file whose
                // loss cannot be reconstructed.
                stream.Write(record.ToUtf8());
                stream.Flush(flushToDisk: true);
            }

            Replace(temp, lockFile);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void Replace(string temp, string lockFile)
    {
        var clock = Stopwatch.StartNew();
        var delay = 5;
        var attempts = 0;

        while (true)
        {
            attempts++;

            try
            {
                // File.Move(overwrite: true), never File.Replace: Replace
                // REQUIRES the destination to exist, and the first write of a
                // lock file is exactly the case where it does not. Move maps to
                // MoveFileEx with MOVEFILE_REPLACE_EXISTING, which covers both,
                // and a reader therefore sees either the old file or the new one
                // and never a torn one.
                File.Move(temp, lockFile, overwrite: true);
                return;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // Something is holding the destination. That is a live condition
                // rather than a bug, so it is retried -- the retry differs from
                // the call that failed in the only way that can help here, which
                // is that the world has had time to move on. Both types are
                // caught deliberately: the measured failure is ACCESS_DENIED,
                // which is NOT an IOException, so a handler for that alone would
                // never retry the case that actually happens.
                if (clock.Elapsed >= MoveBudget)
                {
                    // ⚠️ The attempt count is in the message because it is what
                    // distinguishes the two reasons this can expire, and the
                    // message used to assert one of them. MANY attempts means
                    // something really is holding the file. FEW attempts over the
                    // whole budget means this process was not being scheduled --
                    // measured 2026-08-18 as "3 attempts over 2.3 s" against a
                    // loop that asks for 15 ms of sleep in those three, on a
                    // machine running the whole test suite at once. Saying
                    // "something else is holding it open" was wrong in that case,
                    // and the reader could not tell.
                    throw new IOException(
                        $"'{lockFile}' could not be replaced after {attempts.ToString(CultureInfo.InvariantCulture)} attempts over {clock.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}s: {failure.Message} "
                        + $"With {attempts.ToString(CultureInfo.InvariantCulture)} attempts in {MoveBudget.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s, either something else is holding it open or this process was starved of a thread; a low attempt count means the latter.",
                        failure);
                }

                Thread.Sleep(delay);
                delay = Math.Min(delay * 2, 100);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
#pragma warning disable CA1031 // A temp file that will not delete is litter, never a reason to fail an acquisition that succeeded.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}

/// <summary>
/// A session directory held open for the length of one operation, with nothing
/// written into it.
/// </summary>
/// <remarks>
/// The kernel is the whole mechanism: the handle is <c>FileAccess.ReadWrite,
/// FileShare.Read</c>, so any other BrowserAI opening <c>browserai.json</c> to take
/// the directory is refused while this lives. Disposing releases it and leaves
/// the record exactly as it was found.
/// </remarks>
internal sealed class SessionDirectoryHold(SessionPath location, FileStream held, LockRecord record) : IDisposable
{
    /// <summary>The directory this hold owns.</summary>
    public SessionPath Location { get; } = location;

    /// <summary>The record found there, unmodified.</summary>
    public LockRecord Record { get; } = record;

    /// <inheritdoc />
    public void Dispose() => held.Dispose();
}

/// <summary>What a caller asked for when taking a directory.</summary>
internal sealed record SessionLockRequest
{
    /// <summary>The browser family.</summary>
    public required string Browser { get; init; }

    /// <summary>Free text from the calling model, capped and de-controlled on the way in.</summary>
    public required string Purpose { get; init; }

    /// <summary>
    /// What to append to the session's log, or <see langword="null"/> to append
    /// nothing.
    /// </summary>
    /// <remarks>
    /// <b>Null means "take the directory and say nothing", and exactly one
    /// caller wants that:</b> <c>browserai_destroy</c>, which takes the record in
    /// order to delete it. Every other path has something to record — <c>init</c>
    /// its purpose, <c>resume</c> and <c>set_purpose</c> their <c>why</c> — and a
    /// path that took the directory without saying why it did would be a gap in
    /// the one stream this record exists to keep whole.
    /// </remarks>
    public LogEntry? Entry { get; init; }

    /// <summary>
    /// Whether finding a record already on disk is a <b>refusal</b> rather than
    /// something to reclaim. <see langword="false"/> unless the caller says
    /// otherwise, because reclaiming is what every other path wants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Set by <c>browserai_init</c> and nothing else.</b> `init` means
    /// <i>make a session here</i>, and a directory that already carries a record
    /// has to be `resume`d instead — otherwise the second call silently rebinds
    /// the session's browser family, appending a `chromium` statement to a
    /// Firefox profile's history or the reverse.
    /// </para>
    /// <para>
    /// ⚠️ <b>It is asked UNDER THE GATE because the ungated ask can miss.</b>
    /// `init`'s own pre-gate look — `SessionManager.Existing` — reads
    /// `browserai.json` with no lock held, so it can land in the instant in which the
    /// name is unbound while a peer replaces the record, read <see langword="null"/>
    /// as <i>free, proceed</i>, and reach a reclaim. Under the gate the record
    /// has already been read for the reclaim path, so the same question costs
    /// nothing and cannot be asked at the wrong instant. Added 2026-08-19, from
    /// the adversarial review's enumeration of ungated readers that ACT on an
    /// absence.
    /// </para>
    /// <para>
    /// <b>The pre-gate look deliberately stays.</b> It is what gives `init` one
    /// answer for a lost session, a neatly closed one and one this very process
    /// has open — <c>SessionErrors.SessionAlreadyExists</c>, naming the purpose,
    /// the mode and the date. Deleting it would let <c>SessionLock.ProbeForHolder</c>
    /// answer first for a live session with a shorter sentence about who holds
    /// the file, which is the regression <c>SessionManager.InitAsync</c>'s own
    /// comment records having already been made once.
    /// </para>
    /// </remarks>
    public bool RefuseAnExistingRecord { get; init; }
}

/// <summary>
/// Whether a session directory is being driven right now, as
/// <see cref="SessionLock.ProbeLiveness"/> answers it.
/// </summary>
/// <remarks>
/// <b>Three values because a report and a decision need different things.</b>
/// The pre-gate probe already produced all three internally and threw two of
/// them away, because its only caller could act on one. A reader —
/// <c>browserai_list</c> — has the opposite problem: the value it must never
/// print is a confident <i>free</i> that was really <i>could not tell</i>.
/// </remarks>
internal enum SessionLiveness
{
    /// <summary>
    /// Nothing held it at the instant of the look.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>A snapshot and never a claim.</b> It says an open succeeded once;
    /// it does not say the directory is free, and nothing may take a directory
    /// on the strength of it — <see cref="SessionLock.TryAcquire"/> still settles
    /// that under the per-directory gate.
    /// </remarks>
    NotHeld,

    /// <summary>
    /// A live process holds it: the kernel refused an open a holder's
    /// <c>FileShare.Read</c> denies.
    /// </summary>
    /// <remarks>
    /// <b>It says <i>something</i> holds the file, never <i>who</i>.</b> The
    /// record on disk can name a previous holder, so a caller that turns this
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
/// The path and the failure a diagnosis starts from. Never <see langword="null"/>
/// for <see cref="SessionLiveness.Undetermined"/>, and always
/// <see langword="null"/> for the other two.
/// </param>
internal readonly record struct SessionLivenessAnswer(SessionLiveness State, string? Why);

/// <summary>How an attempt on a session directory ended.</summary>
internal enum SessionLockOutcome
{
    /// <summary>Taken, and nothing was there before.</summary>
    Acquired,

    /// <summary>Taken, and a previous holder's record was found and replaced.</summary>
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

    /// <summary>There is a lock file and it cannot be acted on, or cannot be written.</summary>
    Unreadable,

    /// <summary>
    /// The machine-wide lock could not be created, so there is no lock and
    /// therefore no session. A hard blocker, never a quiet downgrade.
    /// </summary>
    Refused,
}

/// <summary>
/// The outcome of one attempt, with a sentence written for the model that asked.
/// </summary>
/// <param name="outcome">What happened.</param>
/// <param name="message">What to tell the caller.</param>
/// <param name="acquired">The lock, when there is one.</param>
/// <param name="holder">The previous or current holder's record, when one was read.</param>
/// <param name="holderRunning">Whether that holder's process is still alive.</param>
internal sealed class SessionLockResult(
    SessionLockOutcome outcome,
    string message,
    SessionLock? acquired = null,
    LockRecord? holder = null,
    bool holderRunning = false)
{
    /// <summary>What happened.</summary>
    public SessionLockOutcome Outcome { get; } = outcome;

    /// <summary>What to tell the caller. Every failure names a recovery.</summary>
    public string Message { get; } = message;

    /// <summary>The lock, when one was taken.</summary>
    public SessionLock? Acquired { get; } = acquired;

    /// <summary>The record that was found, when one was.</summary>
    public LockRecord? Holder { get; } = holder;

    /// <summary>Whether <see cref="Holder"/>'s process is still running.</summary>
    public bool HolderRunning { get; } = holderRunning;

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

    /// <summary>A lock file exists and cannot be acted on.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="path">The lock file.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "{Path} could not be read and the session was not taken.")]
    public static partial void UnreadableLockFile(ILogger logger, string path, Exception failure);

    /// <summary>The record could not be written.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="path">The lock file.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(EventId = 7, Level = LogLevel.Error, Message = "{Path} could not be written and the session was not taken.")]
    public static partial void CouldNotWriteLockFile(ILogger logger, string path, Exception failure);

    /// <summary>The lock was released.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="directory">The session directory.</param>
    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Session lock released for {Directory}; the record stays so a later reader can say who had it.")]
    public static partial void Released(ILogger logger, string directory);
}
