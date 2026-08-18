// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using BrowserAI.Hosting;
using BrowserAI.Interop;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Sessions;

/// <summary>
/// Ownership of one session directory: the open handle on <c>lock.json</c> that
/// proves it, and the record inside that says who and why.
/// </summary>
/// <remarks>
/// <para>
/// <b>The handle is the lock.</b> <c>lock.json</c> is held
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
/// <b>Two requirements collide here, and the collision is real.</b> The
/// paragraph above makes the <i>open handle</i> the lock. Durability makes the
/// <i>atomic rename</i> the way the record is put in place: a plain write returns
/// once the bytes are in the file-system cache, so a power loss between the write
/// and the flush leaves a file the writer believes it wrote — and <c>lock.json</c>
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
/// holds the name are instants in which nobody else can look. See
/// <c>SessionLockTests.ARenameCannotReplaceALockFileWhoseOwnHandleIsStillOpen</c>,
/// which walks all three share modes on every run.
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
    /// <c>lock.json</c> open for reading in a tight loop. Something briefly
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

    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

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
    /// <param name="request">Mode, browser and purpose for the new record.</param>
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
                return new SessionLockResult(
                    SessionLockOutcome.Busy,
                    $"'{location.FullPath}' is being opened or closed by another BrowserAI right now, and it did not finish within {LockScopes.PerDirectoryGate.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds. " +
                    "That section takes milliseconds, so something is wrong that waiting longer will not fix. Nothing was changed.");
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
    /// Rewrites the record durably, keeping ownership across the replacement.
    /// </summary>
    /// <param name="update">Produces the next record from the current one.</param>
    /// <exception cref="IOException">The gate could not be taken, or the record could not be replaced.</exception>
    public void Rewrite(Func<LockRecord, LockRecord> update)
    {
        ArgumentNullException.ThrowIfNull(update);
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
                _held = OpenHeld(Location.LockFile);
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

    private static FileStream Reclaim(string lockFile, Exception cause)
    {
        try
        {
            return OpenHeld(lockFile);
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
    /// Releases the directory. <c>lock.json</c> stays: the holder record
    /// outliving the holder is what makes a stale lock a sentence rather than a
    /// refusal.
    /// </summary>
    public void Dispose()
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
    /// If <c>lock.json</c> cannot be opened for write, someone owns the
    /// directory, and the sweep skips it unconditionally.
    /// <c>StraySweepTests</c> carries one test per race, this one included.
    /// </para>
    /// <para>
    /// <b>But "whose lock we can acquire" must not mean <see cref="TryAcquire"/>,
    /// and that distinction is the whole reason this method exists.</b> A sweep is
    /// not opening a session, and <see cref="TryAcquire"/> would rewrite
    /// <c>lock.json</c> with the sweeper as holder — overwriting a crashed
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
    /// <b>A directory with no <c>lock.json</c>, an unparseable one, or one held
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
                catch (IOException failure) when (IsSharingViolation(failure))
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
            // process is replacing `lock.json` is refused ACCESS_DENIED -- not a
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
            catch (IOException failure) when (IsSharingViolation(failure))
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
                held = OpenHeld(location.LockFile);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                SessionLog.CouldNotWriteLockFile(logger, location.LockFile, failure);

                return new SessionLockResult(
                    SessionLockOutcome.Unreadable,
                    $"BrowserAI could not write '{location.LockFile}' ({failure.Message}), so the directory was not taken and nothing was changed. Check that the volume has space and that the directory is writable.");
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

        var started = DateTimeOffset.FromFileTime(holder.Holder.ProcessCreatedFileTime);

        return new SessionLockResult(
            SessionLockOutcome.Held,
            SessionErrors.LockHeld(
                location.FullPath,
                holder.Holder.ProcessId,
                holder.Holder.ClientProcessName,
                started,
                holder.LastUsed,
                holder.Purpose),
            holder: holder,
            holderRunning: true);
    }

    private static LockRecord Compose(SessionPath location, SessionLockRequest request, LockRecord? previous, DateTimeOffset now)
    {
        var purpose = LockRecord.SanitisePurpose(request.Purpose);
        var history = new List<string>(previous?.PurposeHistory ?? []);

        if (history.Count is 0 || !string.Equals(history[^1], purpose, StringComparison.Ordinal))
        {
            history.Add(purpose);
        }

        return new LockRecord
        {
            SchemaVersion = LockRecord.CurrentSchemaVersion,
            Directory = location.FullPath,
            Mode = request.Mode,
            Browser = request.Browser,
            Purpose = purpose,
            PurposeHistory = history,
            Created = previous?.Created ?? now,
            LastUsed = now,
            BrowserAiVersion = BuildVersion.Current,
            Holder = new LockHolder
            {
                ProcessId = Environment.ProcessId,
                ProcessCreatedFileTime = ProcessLiveness.CreationTimeOfThisProcess(),
                ClientProcessName = ProcessLiveness.ClientProcessName(),
            },
        };
    }

    private static string Stamp(DateTimeOffset moment) => moment.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>
    /// Opens <c>lock.json</c> the way a holder holds it: read-write, sharing
    /// only reads, so any other BrowserAI trying to take the same directory
    /// meets a sharing violation.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Through <see cref="RenameWindow"/>, added 2026-08-18, and every one
    /// of the four call sites needs it.</b> Two of them open the file straight
    /// after this class has renamed a new record over it, and the other two open
    /// a file another process may be renaming right now — and a delete-pending
    /// destination refuses an open with <c>ACCESS_DENIED</c>, which
    /// <see cref="TakeOrReport"/> does not catch: it handles the sharing
    /// violation (which means <i>owned</i>, and must not be waited out) and a
    /// <c>LockFileException</c>, so an <see cref="UnauthorizedAccessException"/>
    /// propagated out of <c>TryAcquire</c> — the product's primary entry point
    /// for opening a session.
    /// </remarks>
    /// <param name="lockFile">The lock file.</param>
    /// <returns>The held handle.</returns>
    private static FileStream OpenHeld(string lockFile) =>
        RenameWindow.WaitOut(() =>
            new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1));

    private static bool IsSharingViolation(IOException failure) =>
        (failure.HResult & 0xFFFF) is ErrorSharingViolation or ErrorLockViolation;

    private static LockRecord? Parse(FileStream stream, string path)
    {
        stream.Position = 0;

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        // A zero-length lock.json is treated as "no record" rather than as
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
        var temp = Path.Combine(directory, $"{SessionLayout.LockFileName}.new-{Guid.NewGuid():N}");

        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.WriteThrough))
            {
                // WriteThrough at open AND Flush(flushToDisk: true) on the way
                // out. A plain Write returns once the bytes are in the
                // filesystem cache, so a power loss between the write and the
                // flush leaves a file the writer believes it wrote -- and
                // lock.json is the entire ownership guard, the one file whose
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
                    throw new IOException(
                        $"'{lockFile}' could not be replaced after {attempts.ToString(CultureInfo.InvariantCulture)} attempts over {clock.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}s: {failure.Message} Something else is holding it open.",
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
/// FileShare.Read</c>, so any other BrowserAI opening <c>lock.json</c> to take
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
    /// <summary>The session's mode.</summary>
    public required string Mode { get; init; }

    /// <summary>The browser family.</summary>
    public required string Browser { get; init; }

    /// <summary>Free text from the calling model, capped and de-controlled on the way in.</summary>
    public required string Purpose { get; init; }
}

/// <summary>How an attempt on a session directory ended.</summary>
internal enum SessionLockOutcome
{
    /// <summary>Taken, and nothing was there before.</summary>
    Acquired,

    /// <summary>Taken, and a previous holder's record was found and replaced.</summary>
    Reclaimed,

    /// <summary>Somebody else holds it right now. The message names who.</summary>
    Held,

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
