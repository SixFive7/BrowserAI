// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Hosting;
using BrowserAI.Sessions;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Updates;

/// <summary>
/// What a census of the live set established. <b>Three-valued, because the
/// third value is a different fact from the second.</b>
/// </summary>
/// <remarks>
/// ⚠️ <b>Widened 2026-08-20 (previously a <see langword="bool"/>, whose
/// <see langword="false"/> meant <i>not alone</i> and <i>could not tell</i> at
/// once).</b> That conflation was written for the updater, where both answers
/// mean <i>do not apply</i> and the safe direction is the same one — see
/// <see cref="LiveInstances.AmIAlone"/>, which still collapses them and is the
/// guarantee that the updater did not move. For anything that <i>repairs</i>
/// rather than refrains, the two are opposites: a refusal built on
/// <see cref="Undetermined"/> is permanent and has nothing to act on, and it
/// reads on a log line exactly like a refusal built on a peer that is genuinely
/// there.
/// </remarks>
internal enum Liveness
{
    /// <summary>Nothing else is running out of this install root.</summary>
    Alone,

    /// <summary>
    /// At least <see cref="LivenessAnswer.Others"/> other processes are, each
    /// proven by a marker file the kernel refused to hand over.
    /// </summary>
    /// <remarks>
    /// <b>At least, never exactly.</b> A marker whose held-ness could not be
    /// established does not reduce a count that is already positive — it is
    /// reported in <see cref="LivenessAnswer.Why"/> instead — because a definite
    /// <i>somebody is there</i> is more use to every caller than an uncertainty
    /// that would erase it.
    /// </remarks>
    NotAlone,

    /// <summary>
    /// The question was not settled. <see cref="LivenessAnswer.Why"/> says what
    /// stopped it, and saying so is the whole point of this value existing.
    /// </summary>
    Undetermined,
}

/// <summary>The census answer, and the reason when there is not one.</summary>
internal sealed record LivenessAnswer
{
    /// <summary>Nothing else is alive, and that was established rather than assumed.</summary>
    public static readonly LivenessAnswer IsAlone = new() { State = Liveness.Alone };

    /// <summary>Which of the three.</summary>
    public required Liveness State { get; init; }

    /// <summary>
    /// How many other live instances were counted. Meaningful only for
    /// <see cref="Liveness.NotAlone"/>, and a lower bound even then.
    /// </summary>
    public int Others { get; init; }

    /// <summary>
    /// Why the answer could not be settled — a path, a mutex name, an
    /// exception's own message. Never <see langword="null"/> for
    /// <see cref="Liveness.Undetermined"/>.
    /// </summary>
    /// <remarks>
    /// <b>This is the load-bearing half of the widening.</b> A tool that refuses
    /// on <see cref="Liveness.Undetermined"/> can only be diagnosed if the
    /// refusal names the thing that could not be read; without it the caller is
    /// left with a permanent no and nowhere to look.
    /// </remarks>
    public string? Why { get; init; }

    /// <summary>Somebody else is there, and this many were counted.</summary>
    /// <param name="others">How many markers were proven held by another process.</param>
    /// <returns>The answer.</returns>
    public static LivenessAnswer NotAlone(int others) =>
        new() { State = Liveness.NotAlone, Others = others };

    /// <summary>The question could not be settled, and this is what stopped it.</summary>
    /// <param name="why">The path, name or message a diagnosis starts from.</param>
    /// <returns>The answer.</returns>
    public static LivenessAnswer Undetermined(string why) =>
        new() { State = Liveness.Undetermined, Why = why };
}

/// <summary>How a reclaim pass over the live-marker directory ended.</summary>
internal enum LiveMarkerReclaimOutcome
{
    /// <summary>It ran, and the counts say what it found.</summary>
    Ran,

    /// <summary>
    /// Another process holds the gate and is doing the same work. Not a missed
    /// reclaim.
    /// </summary>
    Skipped,

    /// <summary>The machine-wide gate could not be created, so nothing was touched.</summary>
    NoLock,

    /// <summary>The directory could not be read. Nothing was removed.</summary>
    Failed,
}

/// <summary>What one reclaim pass found and what it removed.</summary>
internal sealed record LiveMarkerReclaim
{
    /// <summary>How the pass ended.</summary>
    public required LiveMarkerReclaimOutcome Outcome { get; init; }

    /// <summary>Markers proven <b>not held</b> and removed.</summary>
    public int Reclaimed { get; init; }

    /// <summary>Markers proven held, and therefore left exactly where they were.</summary>
    public int Held { get; init; }

    /// <summary>
    /// Markers this pass could not settle — unopenable for a reason other than
    /// sharing, or free and undeletable. <b>None of them were touched.</b>
    /// </summary>
    public int Undetermined { get; init; }

    /// <summary>Whether the gate was found abandoned by a dead holder (race R3).</summary>
    public bool GateWasAbandoned { get; init; }

    /// <summary>
    /// The wait this pass asked the gate for, read back off the gate rather than
    /// restated here. <see langword="null"/> when no acquire happened at all —
    /// the directory did not exist, or the gate could not be created.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It is how a caller can tell an instant skip from a skip that waited
    /// first</b>, which <see cref="Outcome"/> cannot: both are
    /// <see cref="LiveMarkerReclaimOutcome.Skipped"/>. This pass takes the gate
    /// at <see cref="Sessions.LockScopes.NeverWaits"/> on purpose — it runs while
    /// a process is starting, and a reclaim is never worth a millisecond of
    /// startup — so a value other than zero here is the defect, arriving as a
    /// fact rather than as an inference from a stopwatch on a loaded machine.
    /// </para>
    /// <para>
    /// See <see cref="Sessions.MachineMutex.LastAcquireTimeout"/> for what this
    /// proves and what it does not.
    /// </para>
    /// </remarks>
    public TimeSpan? GateWait { get; init; }

    /// <summary>The first reason anything was left alone, for the log line.</summary>
    public string? Why { get; init; }

    /// <summary>One line for the log.</summary>
    public string Summary =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"outcome={Outcome} reclaimed={Reclaimed} held={Held} undetermined={Undetermined} abandonedGate={GateWasAbandoned} why={Why ?? "-"}");
}

/// <summary>
/// Every BrowserAI running out of one install root, counted by the only signal
/// that cannot lie: an open file handle the OS releases on death.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists to gate the update apply, and the thing it prevents is
/// measured.</b> Velopack's <c>force_stop_package</c> kills every process whose
/// image path is under the install root — on <c>apply</c>, <c>install</c>,
/// <c>start</c>, <c>uninstall</c> <b>and after every hook returns</b>, matching
/// by path, without asking
/// ([kb](../../../kb/packaging/velopack.md#4-force_stop_package-kills-everything-under-the-root)).
/// At the concurrency BrowserAI is designed for — eight editors with a dozen
/// agent sessions each — one process deciding to update destroys every other
/// live session mid-task, and it is precisely the landmine the only prior art
/// available cannot have hit, that product being single-instance.
/// </para>
/// <para>
/// <b>The handle is the mechanism, exactly as it is for a session directory</b>
/// (<see cref="Sessions.SessionLock"/>). Each run creates one file and holds it
/// <c>FileAccess.ReadWrite, FileShare.Read</c>: another process asking for write
/// access is refused by the kernel, and a process that was killed, crashed or
/// was terminated by a job object releases it anyway. A pid file would need a
/// creation-time pair to survive recycling and would still be a claim rather
/// than a fact.
/// </para>
/// <para>
/// <b>The census runs inside the per-root mutex, and the join does too.</b>
/// Without that, two BrowserAIs starting together could each census before the
/// other joined and both conclude they were alone. With it, the orderings that
/// remain are the safe ones: either a process is visible to the census, or it
/// has not joined yet and will find the applier's own file when it does.
/// </para>
/// <para>
/// <b>Deliberately not <see cref="IAppPaths.InstanceRoot"/>.</b> ⚠️
/// <b>Corrected 2026-08-24 (previously "That directory's liveness signal is the
/// child holding it as a working directory, so a run has no signal until its
/// child has started").</b> It has one now —
/// <c>Runtime.InstanceDirectory.MarkerFileName</c>, this same mechanism applied
/// to the same problem — and the separation stands on what was always the load
/// bearing half: <b>this marker is joined before the instance directory
/// exists at all</b>, and the update check runs on a background thread from the
/// moment the process starts, which is inside exactly that window. The two also
/// answer different questions and are reclaimed by different passes: that one
/// asks <i>may this directory be deleted</i>, and this one asks <i>am I the last
/// instance</i>.
/// </para>
/// <para>
/// ⚠️ <b>Reclaim used to happen only here, and that was measured to be nowhere.</b>
/// Until 2026-08-20 the only code that removed a marker whose holder had died
/// was <see cref="Census"/>, which <see cref="UpdateService"/> reaches
/// <i>after</i> an update has been found <b>and</b> downloaded. That had never
/// once happened on the machine this product is developed on, and
/// <b>755 unheld markers</b> had accumulated in two days. Reclaim is now a
/// routine of its own — <see cref="ReclaimStaleMarkers"/> — run from the stray
/// sweep and from startup, and <see cref="Census"/> keeps doing it as well
/// because a census that walked past a dead marker would count it.
/// </para>
/// </remarks>
internal sealed class LiveInstances : IDisposable
{
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    private readonly string _directory;
    private readonly string _mutexName;
    private readonly ILogger _logger;
    private FileStream? _held;

    private LiveInstances(string directory, string ownFile, string mutexName, FileStream held, ILogger logger)
    {
        _directory = directory;
        OwnFile = ownFile;
        _mutexName = mutexName;
        _held = held;
        _logger = logger;
    }

    /// <summary>Whether a marker file is held, free, or neither answer.</summary>
    private enum MarkerState
    {
        /// <summary>A live process holds it. It is another instance and it is never touched.</summary>
        Held,

        /// <summary>Nothing holds it. Whoever wrote it is gone.</summary>
        Free,

        /// <summary>Neither could be established. Left alone, and reported.</summary>
        Unknown,
    }

    /// <summary>This process's own marker file.</summary>
    public string OwnFile { get; }

    /// <summary>
    /// Announces this process, and keeps announcing it until disposal or death.
    /// </summary>
    /// <param name="paths">The app-paths seam.</param>
    /// <param name="logger">Where a failure is reported.</param>
    /// <returns>The registration, or <see langword="null"/> if one could not be made.</returns>
    /// <remarks>
    /// <b>A failure to join is not a failure to start.</b> BrowserAI's job is to
    /// serve stdio; the only thing lost is the ability to update, and an update
    /// that cannot prove it is alone must not happen anyway. So this returns
    /// null and logs rather than throwing.
    /// </remarks>
    public static LiveInstances? Join(IAppPaths paths, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        var directory = paths.LiveInstanceDirectory;
        var mutexName = MutexNameFor(paths.RootAppDir);

        try
        {
            _ = Directory.CreateDirectory(directory);

            using var gate = MachineMutex.Create(mutexName);
            var acquired = gate.Acquire(LockScopes.LiveInstanceGate);

            if (acquired is MutexAcquisition.NotAcquired)
            {
                UpdateLog.CouldNotJoinLiveSet(logger, directory, null);
                return null;
            }

            try
            {
                var file = Path.Combine(
                    directory,
                    string.Create(CultureInfo.InvariantCulture, $"{Environment.ProcessId}-{Guid.NewGuid():N}.live"));

                // Same open as SessionLock's: deny write to everyone else, allow
                // read, so a census can see the name and cannot take it.
                //
                // ⚠️ NOTHING IS RECLAIMED HERE, AND THAT IS A DECISION. This
                // hold is on the startup path and every process on the machine
                // queues behind it; walking 755 markers inside it would put the
                // enumeration into a five-second-gated critical section that a
                // hundred starting processes contend for, and a join that times
                // out makes this process INVISIBLE to a peer's census -- which
                // is the one failure this whole file exists to prevent. The
                // startup reclaim is a separate, zero-timeout, background pass:
                // see ReclaimStaleMarkers and StartReclaimInBackground.
                var held = new FileStream(file, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1);

                return new LiveInstances(directory, file, mutexName, held, logger);
            }
            finally
            {
                gate.Release();
            }
        }
#pragma warning disable CA1031 // Joining is best-effort by design: the consequence of failing is that this process never updates, which is the safe direction.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            UpdateLog.CouldNotJoinLiveSet(logger, directory, failure);
            return null;
        }
    }

    /// <summary>
    /// Whether this process is the only BrowserAI running out of this install
    /// root — <b>or that the question could not be settled, and why</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three answers, and the third one carries a sentence.</b> A marker that
    /// cannot be opened for a reason other than sharing, a directory that cannot
    /// be enumerated, a gate that expired, a process that has already left the
    /// live set — none of those is <i>somebody else is running</i>, and none of
    /// them is <i>nobody is</i>. They are <see cref="Liveness.Undetermined"/>,
    /// and <see cref="LivenessAnswer.Why"/> names the path or the failure so that
    /// a refusal built on one can be diagnosed instead of merely repeated.
    /// </para>
    /// <para>
    /// <b>A positive count wins over an uncertainty.</b> Two markers proven held
    /// and one unreadable is <see cref="Liveness.NotAlone"/> with
    /// <c>Others = 2</c> — <i>at least two</i> — rather than
    /// <see cref="Liveness.Undetermined"/>. Erasing a fact that was established
    /// because a different one was not is a strictly worse answer for every
    /// caller.
    /// </para>
    /// <para>
    /// <b>It reclaims as it counts, under the gate it is already holding.</b> A
    /// marker proven free is removed here as well as by
    /// <see cref="ReclaimStaleMarkers"/>, because a census that walked past one
    /// would have to count it as something, and there is no honest value for it.
    /// A free marker that will not delete is <b>not</b> counted as another
    /// instance and does not make the answer undetermined — it never was one —
    /// which is what keeps this reclaim from changing the verdict.
    /// </para>
    /// </remarks>
    /// <returns>Alone, not alone with a lower bound, or undetermined with a reason.</returns>
    public LivenessAnswer Census()
    {
        if (_held is null)
        {
            // Not "alone" and not "not alone": this process is no longer a
            // member of the set it is asking about, so it cannot speak for it.
            return LivenessAnswer.Undetermined(
                $"this process has left the live set under '{_directory}' — its own marker '{OwnFile}' was released — so a census taken now would not include it.");
        }

        try
        {
            using var gate = MachineMutex.Create(_mutexName);

            if (gate.Acquire(LockScopes.LiveInstanceGate) is MutexAcquisition.NotAcquired)
            {
                return LivenessAnswer.Undetermined(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"the live-instance gate '{_mutexName}' was still held after {LockScopes.LiveInstanceGate.TotalSeconds:F0}s, so the set under '{_directory}' was never read."));
            }

            try
            {
                var pass = Walk(_directory, OwnFile);

                if (pass.Held is not 0)
                {
                    UpdateLog.NotAlone(_logger, pass.Held);
                    return LivenessAnswer.NotAlone(pass.Held);
                }

                return pass.Undetermined is 0
                    ? LivenessAnswer.IsAlone
                    : LivenessAnswer.Undetermined(
                        pass.Why ?? $"a marker under '{_directory}' could not be read, and no reason was recorded.");
            }
            finally
            {
                gate.Release();
            }
        }
#pragma warning disable CA1031 // Any failure to establish solitude is answered "undetermined", which AmIAlone collapses to the same safe direction it always had.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            UpdateLog.CouldNotCensusLiveSet(_logger, _directory, failure);

            return LivenessAnswer.Undetermined(
                $"the live set under '{_directory}' could not be read ({failure.Message}).");
        }
    }

    /// <summary>
    /// Whether this process is the only BrowserAI running out of this install
    /// root, with every uncertainty answered <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This method is the guarantee that the updater did not move on
    /// 2026-08-20.</b> It is one expression over <see cref="Census"/> and it has
    /// exactly one <see langword="true"/> arm, so
    /// <see cref="Liveness.Undetermined"/> is treated precisely as
    /// <see cref="Liveness.NotAlone"/> was and is: <i>do not apply</i>. The cost
    /// of being wrong in that direction is a delayed update; the cost of being
    /// wrong in the other is every other agent's session.
    /// <c>UpdateTests.EveryCensusAnswerOtherThanAloneStillReadsAsNotAloneToTheUpdater</c>
    /// asserts the mapping over all three values, and
    /// <c>UpdateTests.AnUndeterminedCensusStagesTheUpdateExactlyAsANotAloneOneDoes</c>
    /// asserts it through <see cref="UpdateService"/> itself rather than through
    /// this signature.
    /// </para>
    /// <para>
    /// <b>Widening a return type is where a consumer silently changes</b>, so the
    /// widening deliberately did not touch this one. <see cref="UpdateService"/>
    /// still calls this and nothing else.
    /// </para>
    /// </remarks>
    /// <returns><see langword="true"/> only when nothing else is alive.</returns>
    public bool AmIAlone() => Census().State is Liveness.Alone;

    /// <summary>
    /// Removes every marker under an install root whose holder is gone, and
    /// touches nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both call sites take the same gate as a join and a census, and both
    /// skip instantly when it is held.</b> One process reclaims and the rest
    /// move on — the same discipline <see cref="Sessions.StraySweep"/> already
    /// applies machine-wide, reused rather than reinvented. The timeout is
    /// <see cref="LockScopes.NeverWaits"/> and not
    /// <see cref="LockScopes.LiveInstanceGate"/> precisely because this may run
    /// while a process is starting: a reclaim is never worth a millisecond of
    /// startup, and a skipped reclaim is not a missed one, because whoever holds
    /// the gate is walking the same directory.
    /// </para>
    /// <para>
    /// <b>A marker is stale only when it is NOT HELD. Existence is not
    /// held-ness</b> — the same rule <see cref="Runtime.MaintenanceLock"/> and
    /// <see cref="Sessions.SessionLock"/> state about their own files, and for
    /// the same reason: a crashed holder leaves the file behind, so existence
    /// means <i>somebody died here once</i> and never <i>somebody is working
    /// now</i>. Held-ness is a sharing violation on an open this file's own
    /// <see cref="Join"/> would be refused by, and nothing else in the answer is
    /// acted on.
    /// </para>
    /// <para>
    /// <b>Reclaiming another process's live marker would be a serious bug</b> —
    /// it would make a running instance invisible to every later census and
    /// therefore killable by an apply. The negative is proved with a positive
    /// control rather than argued:
    /// <c>UpdateTests.AHeldMarkerSurvivesTheReclaimAndTheSameMarkerGoesOnceItIsReleased</c>
    /// holds one marker open, runs this, requires it to survive, releases it,
    /// runs this again and requires it to go — so a pass that removed nothing at
    /// all could not pass either half.
    /// </para>
    /// </remarks>
    /// <param name="paths">The app-paths seam, for the directory and the gate's name.</param>
    /// <param name="logger">Where the pass is recorded. Never <c>stdout</c>.</param>
    /// <returns>What the pass found and what it removed.</returns>
    public static LiveMarkerReclaim ReclaimStaleMarkers(IAppPaths paths, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        var directory = paths.LiveInstanceDirectory;

        if (!Directory.Exists(directory))
        {
            // Nothing has ever joined here. Not a failure and not worth a mutex.
            return new LiveMarkerReclaim { Outcome = LiveMarkerReclaimOutcome.Ran };
        }

        var mutexName = MutexNameFor(paths.RootAppDir);

        // Declared before the try and disposed unconditionally in the finally:
        // the pattern the rest of this product uses around a named object
        // created inside a guarded region.
        MachineMutex? gate = null;

        try
        {
            try
            {
                gate = MachineMutex.Create(mutexName);
            }
            catch (Exception failure) when (failure
                is UnauthorizedAccessException
                or WaitHandleCannotBeOpenedException
                or IOException
                or NotSupportedException)
            {
                // Degraded, never fatal, and never a refusal to start. A session
                // refuses outright when it cannot have its own lock because
                // there the alternative is two browsers in one profile; here the
                // alternative is a marker file nobody swept.
                UpdateLog.CouldNotReclaimLiveMarkers(logger, directory, failure);

                return new LiveMarkerReclaim
                {
                    Outcome = LiveMarkerReclaimOutcome.NoLock,
                    Why = $"the gate '{mutexName}' could not be created ({failure.Message}).",
                };
            }

            var acquisition = gate.Acquire(LockScopes.NeverWaits);

            if (acquisition is MutexAcquisition.NotAcquired)
            {
                UpdateLog.LiveMarkerReclaimSkipped(logger, mutexName);

                return new LiveMarkerReclaim
                {
                    Outcome = LiveMarkerReclaimOutcome.Skipped,
                    GateWait = gate.LastAcquireTimeout,
                    Why = $"another process holds '{mutexName}' and is walking the same directory.",
                };
            }

            try
            {
                LiveMarkerReclaim result;

                try
                {
                    // own: null. This is a static pass with no marker of its
                    // own, and the caller's marker needs no name-based exemption
                    // because it is HELD -- which is the property the pass reads
                    // and the only one it is allowed to act on.
                    var pass = Walk(directory, own: null);

                    result = new LiveMarkerReclaim
                    {
                        Outcome = LiveMarkerReclaimOutcome.Ran,
                        Reclaimed = pass.Reclaimed,
                        Held = pass.Held,
                        Undetermined = pass.Undetermined + pass.Unreclaimed,
                        GateWasAbandoned = acquisition is MutexAcquisition.AcquiredAbandoned,
                        GateWait = gate.LastAcquireTimeout,
                        Why = pass.Why,
                    };
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
                {
                    UpdateLog.CouldNotReclaimLiveMarkers(logger, directory, failure);

                    result = new LiveMarkerReclaim
                    {
                        Outcome = LiveMarkerReclaimOutcome.Failed,
                        GateWasAbandoned = acquisition is MutexAcquisition.AcquiredAbandoned,
                        GateWait = gate.LastAcquireTimeout,
                        Why = $"'{directory}' could not be enumerated ({failure.Message}).",
                    };
                }

                UpdateLog.ReclaimedLiveMarkers(logger, result.Summary);
                return result;
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            gate?.Dispose();
        }
    }

    /// <summary>
    /// Runs one reclaim pass on a background thread and returns immediately.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Startup must never wait for this.</b> The pass takes a machine-wide
    /// mutex at zero timeout and walks one directory; both are fast and neither
    /// is on the request path, so it runs on its own background thread with a
    /// catch-all at the boundary. An exception escaping a background thread
    /// tears the process down, and this process is an MCP server whose caller
    /// would see a transport that simply stopped.
    /// </para>
    /// <para>
    /// <b>Why this exists beside the copy inside
    /// <see cref="Sessions.StraySweep"/>, which also runs at startup.</b> The
    /// sweep can decline to run for reasons that have nothing to do with
    /// markers: another process holds <see cref="LockScopes.Sweep"/>, or the
    /// payload manifest its factory reads is broken. Neither of those should
    /// cost a machine its marker reclaim, and this path shares none of it.
    /// </para>
    /// </remarks>
    /// <param name="paths">The app-paths seam.</param>
    /// <param name="logger">Where a failure is reported.</param>
    public static void StartReclaimInBackground(IAppPaths paths, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        var thread = new Thread(() =>
        {
            try
            {
                // ReclaimStaleMarkers logs its own census, so nothing is logged
                // here: a second line would be the same fact twice.
                _ = ReclaimStaleMarkers(paths, logger);
            }
#pragma warning disable CA1031 // The whole purpose of this method: nothing a reclaim can do may end the process.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                try
                {
                    UpdateLog.CouldNotReclaimLiveMarkers(logger, paths.LiveInstanceDirectory, failure);
                }
#pragma warning disable CA1031 // A logger that throws must not defeat the catch-all that was reporting through it.
                catch (Exception)
#pragma warning restore CA1031
                {
                }
            }
        })
        {
            IsBackground = true,
            Name = "BrowserAI live-marker reclaim",
        };

        thread.Start();
    }

    /// <summary>Leaves the live set.</summary>
    public void Dispose()
    {
        var held = Interlocked.Exchange(ref _held, null);

        if (held is null)
        {
            return;
        }

        held.Dispose();
        _ = TryDelete(OwnFile, out _);
    }

    /// <summary>
    /// The live set's own machine-wide gate: the same canonicalisation every
    /// other directory-keyed name in this product uses, in a namespace of its
    /// own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One canonicalisation function, four consumers — the per-directory gate,
    /// the lock file, the session index key and this. A second spelling is how
    /// two names come to mean different things while both report success.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-23 (previously
    /// <c>SessionPath.Resolve(rootAppDir).MutexName</c>, with no prefix of its
    /// own).</b> That shared the <i>per-directory gate's</i> namespace as well
    /// as its canonicalisation, which made this a fourth scope wearing the
    /// first's names — and <see cref="Sessions.LockScopes"/> documents three.
    /// A session opened on the install root itself collided <b>exactly</b>, and
    /// nothing refuses that path: <c>CanonicalPath</c> refuses network
    /// paths and aliased spellings, and <c>%LOCALAPPDATA%\BrowserAI</c> is
    /// neither.
    /// </para>
    /// <para>
    /// <b>What the collision cost was silent and lasted the process's life.</b>
    /// <see cref="Join"/> waits <see cref="LockScopes.LiveInstanceGate"/>, five
    /// seconds. Queued behind a hold of the 120-second
    /// <see cref="LockScopes.PerDirectoryGate"/> it expires, and a failed join
    /// costs this process its ability to update <b>for good</b> — one log line,
    /// no refusal, nothing a caller could see. <see cref="AmIAlone"/>'s census
    /// held the same object from the other side, blocking that directory's
    /// <c>TryAcquire</c>. Found by
    /// [the adversarial review](../../../docs/reviews/2026-08-18-adversarial-locking.md),
    /// B3.
    /// </para>
    /// <para>
    /// <b>The prefix is the remedy the review named and the one this product
    /// had already used once</b> — <c>BrowserProvisioner.MutexPrefix</c> is
    /// <c>Global\BrowserAI-Provision-</c> for exactly this reason. Adding a
    /// second one is cheaper than making the session guard understand a
    /// directory it otherwise has no opinion about.
    /// </para>
    /// <para>
    /// ⚠️ <b>The name changed, so a BrowserAI from before this and one from
    /// after do not serialise against each other on the live set.</b> That
    /// window is one upgrade wide and what is inside it is safe by
    /// construction: <see cref="Join"/> creates a file whose name carries a
    /// GUID, and <see cref="Walk"/> only removes a marker it has itself proven
    /// unheld, so two passes running together reach the same answer more slowly
    /// rather than a different one.
    /// </para>
    /// </remarks>
    /// <param name="rootAppDir">The install root.</param>
    /// <returns>A <c>Global\</c> name.</returns>
    /// <remarks>
    /// ⚠️ <b>It takes the identity chain and not the canonicaliser in front of
    /// it, and that is a decision — 2026-08-26.</b> The app root is not a
    /// caller's string: it is this process's own, already judged against the
    /// user's profile through the filesystem by
    /// <see cref="Hosting.InstallRootScope"/>, which resolves both sides of that
    /// comparison the same way <see cref="Sessions.CanonicalPath"/> would.
    /// Asking again would be a second object-manager call and a directory open
    /// per census for an answer already established, and it would make the live
    /// set's gate refusable — which is a startup failure wearing an update
    /// check's name.
    /// </remarks>
    public static string MutexNameFor(string rootAppDir) =>
        MutexPrefix + SessionPath.For(rootAppDir).MutexName[LockScopes.PerDirectoryPrefix.Length..];

    /// <summary>
    /// The live set's own prefix, so that this scope and a session's
    /// per-directory gate cannot name one kernel object.
    /// </summary>
    public const string MutexPrefix = $@"{LockScopes.GlobalPrefix}BrowserAI-Live-";

    /// <summary>
    /// One walk of the marker directory: count what is held, remove what is not,
    /// and touch nothing it could not settle.
    /// </summary>
    /// <remarks>
    /// <b>The gate is the caller's to hold, and both callers do.</b> This is the
    /// one routine that decides a marker's fate, so a census and a reclaim
    /// cannot come to different conclusions about the same file — which is what
    /// a second copy of the sharing-violation rule would eventually produce.
    /// </remarks>
    /// <param name="directory">The marker directory, which must exist.</param>
    /// <param name="own">
    /// A marker to skip by name, or <see langword="null"/>. Belt to the braces:
    /// a caller's own marker is held and would be counted rather than removed
    /// anyway, but a census must not count itself.
    /// </param>
    /// <returns>The tallies.</returns>
    private static MarkerWalk Walk(string directory, string? own)
    {
        var held = 0;
        var reclaimed = 0;
        var unreclaimed = 0;
        var undetermined = 0;
        string? why = null;

        foreach (var candidate in Directory.EnumerateFiles(directory, "*.live"))
        {
            if (own is not null && string.Equals(candidate, own, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var (state, reason) = Probe(candidate);

            if (state is MarkerState.Held)
            {
                held++;
                continue;
            }

            if (state is MarkerState.Unknown)
            {
                undetermined++;
                why ??= reason;
                continue;
            }

            if (TryDelete(candidate, out var refusal))
            {
                // Not held: whoever wrote it is gone. Removed rather than left
                // as a growing pile that makes every later walk slower.
                reclaimed++;
                continue;
            }

            // ⚠️ NOT counted as another instance and NOT counted as an
            // uncertainty. It was proven free; only the removal failed, and a
            // removal failure must never move a census verdict.
            unreclaimed++;
            why ??= refusal;
        }

        return new MarkerWalk
        {
            Held = held,
            Reclaimed = reclaimed,
            Unreclaimed = unreclaimed,
            Undetermined = undetermined,
            Why = why,
        };
    }

    /// <summary>
    /// Whether one marker is held, by asking the kernel for the access a holder
    /// denies.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The <see cref="MarkerState.Unknown"/> arm used to answer
    /// <i>held</i>, and that was right for its one caller and wrong as a
    /// general answer.</b> Counting an unreadable marker as a live instance kept
    /// the updater on the safe side, which is why it was written that way; what
    /// it cost was the ability to say <i>this is a permissions problem on this
    /// path</i> rather than <i>somebody else is running</i>. The safe side is now
    /// preserved by <see cref="Census"/>, which lets an uncertainty decide the
    /// verdict when nothing definite did, and by <see cref="AmIAlone"/>, which
    /// collapses both to <see langword="false"/>.
    /// </remarks>
    /// <param name="path">The marker file.</param>
    /// <returns>Its state, and a reason when there is not one.</returns>
    private static (MarkerState State, string? Why) Probe(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1);
            return (MarkerState.Free, null);
        }
        catch (IOException failure) when ((failure.HResult & 0xFFFF) is ErrorSharingViolation or ErrorLockViolation)
        {
            return (MarkerState.Held, null);
        }
        catch (Exception failure) when (failure is FileNotFoundException or DirectoryNotFoundException)
        {
            // It went away between the enumeration and the look. There is
            // nothing to count and nothing left to remove.
            return (MarkerState.Free, null);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return (
                MarkerState.Unknown,
                $"'{path}' could not be opened, and the failure was not a sharing violation ({failure.Message}).");
        }
    }

    private static bool TryDelete(string path, out string? refusal)
    {
        try
        {
            File.Delete(path);
            refusal = null;
            return true;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // A marker that will not delete is litter; the next pass retries it.
            refusal = $"'{path}' is not held and could not be removed ({failure.Message}).";
            return false;
        }
    }

    /// <summary>What one walk of the marker directory found.</summary>
    private readonly record struct MarkerWalk
    {
        /// <summary>Markers another process holds.</summary>
        public required int Held { get; init; }

        /// <summary>Markers proven free and removed.</summary>
        public required int Reclaimed { get; init; }

        /// <summary>Markers proven free that would not delete. Never a verdict.</summary>
        public required int Unreclaimed { get; init; }

        /// <summary>Markers whose held-ness could not be established.</summary>
        public required int Undetermined { get; init; }

        /// <summary>The first reason anything was left alone.</summary>
        public required string? Why { get; init; }
    }
}
