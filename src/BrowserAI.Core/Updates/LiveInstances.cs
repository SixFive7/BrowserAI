// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Hosting;
using BrowserAI.Sessions;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Updates;

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
    /// Markers this pass could not settle -- unopenable for a reason other than
    /// sharing, or free and undeletable. <b>None of them were touched.</b>
    /// </summary>
    public int Undetermined { get; init; }

    /// <summary>Whether the gate was found abandoned by a dead holder (race R3).</summary>
    public bool GateWasAbandoned { get; init; }

    /// <summary>
    /// The wait this pass asked the gate for, read back off the gate and not
    /// restated here. <see langword="null"/> when no acquire happened at all --
    /// the directory did not exist, or the gate could not be created.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It is how a caller can tell an instant skip from a skip that waited
    /// first</b>, which <see cref="Outcome"/> cannot: both are
    /// <see cref="LiveMarkerReclaimOutcome.Skipped"/>. This pass takes the gate
    /// at <see cref="Sessions.LockScopes.NeverWaits"/> on purpose -- it runs while
    /// a process is starting, and a reclaim is never worth a millisecond of
    /// startup -- so a value other than zero here is the defect, arriving as a
    /// fact and not as an inference from a stopwatch on a loaded machine.
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
/// The live-marker directory under one install root, and the one pass that removes
/// the markers whose holders have gone.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Corrected 2026-10-10 (previously "Every BrowserAI running out of one
/// install root, counted by the only signal that cannot lie: an open file handle the
/// OS releases on death.")</b>. The census is deleted. Since the one-binary build of
/// 2026-10-08 no process joins the live set: the resident background's update core
/// decides when an update installs, by what holds it. <c>Join</c>, <c>Census</c> and
/// <c>AmIAlone</c>, which nothing called after that day, were deleted on 2026-10-10 by
/// the maintainer's decision <i>"9 a"</i>, with <c>Liveness</c>,
/// <c>LivenessAnswer</c>, the instance this class was and its <c>OwnFile</c>,
/// <c>StartReclaimInBackground</c>, <c>IsMarkerHeld</c>, <c>RootKeyFor</c>, and
/// <c>LockScopes.LiveInstanceGate</c>, the wait the join and the census took. What is
/// left is <see cref="ReclaimStaleMarkers"/>, which the stray sweep runs: a build
/// from before 2026-10-08 that an update's kill pass, a crash or a kill ended left its
/// marker behind, and the pass removes it once nothing holds it. The paragraphs below
/// are the record of the census.
/// </para>
/// <para>
/// <b>This exists to gate the update apply, and the thing it prevents is
/// measured.</b> Velopack's <c>force_stop_package</c> kills every process whose
/// image path is under the install root -- on <c>apply</c>, <c>install</c>,
/// <c>start</c>, <c>uninstall</c> <b>and after every hook returns</b>, matching
/// by path, without asking
/// ([kb](../../../kb/packaging/velopack.md#4-force_stop_package-kills-everything-under-the-root)).
/// At the concurrency BrowserAI is designed for -- eight editors with a dozen
/// agent sessions each -- one process deciding to update destroys every other
/// live session mid-task, and it is precisely the landmine the only prior art
/// available cannot have hit, that product being single-instance.
/// <i>Corrected 2026-09-24 @ Velopack 1.2.158, by addition (previously, and still
/// above, a list that named <c>start</c> without a qualifier)</i>: <c>start</c>
/// calls it only on its legacy <c>app-</c> migration branch, and <c>apply</c>
/// calls it once more immediately before the swap. Measured that day, an apply
/// ended every server still running under the root before the swap, and the ones
/// started after it ran the new version
/// ([kb](../../../kb/packaging/velopack.md#what-an-apply-does-to-every-process-under-the-root----read-and-measured-at-12158-2026-09-24)).
/// </para>
/// <para>
/// <b>The handle is the mechanism, exactly as it is for a session directory</b>
/// (<c>Sessions.SessionLock</c>). Each run creates one file and holds it
/// <c>FileAccess.ReadWrite, FileShare.Read</c>: another process asking for write
/// access is refused by the kernel, and a process that was killed, crashed or
/// was terminated by a job object releases it anyway. A pid file would need a
/// creation-time pair to survive recycling and would still be a claim and
/// not a fact.
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
/// child has started").</b> It has one now --
/// <c>Runtime.InstanceDirectory.MarkerFileName</c>, this same mechanism applied
/// to the same problem -- and the separation stands on what was always the load
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
/// was <c>Census</c>, which <c>UpdateService</c> reaches
/// <i>after</i> an update has been found <b>and</b> downloaded. That had never
/// once happened on the machine this product is developed on, and
/// <b>755 unheld markers</b> had accumulated in two days. Reclaim is now a
/// routine of its own -- <see cref="ReclaimStaleMarkers"/> -- run from the stray
/// sweep and from startup, and <c>Census</c> keeps doing it as well
/// because a census that walked past a dead marker would count it.
/// </para>
/// </remarks>
internal static class LiveInstances
{
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

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

    /// <summary>
    /// Removes every marker under an install root whose holder is gone, and
    /// touches nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both call sites take the same gate as a join and a census, and both
    /// skip instantly when it is held.</b> <i>Corrected 2026-10-10 by addition: one
    /// call site is left, the stray sweep, since the startup run
    /// (<c>StartReclaimInBackground</c>), the join and the census were deleted; the
    /// gate is the one a build from before 2026-10-08 joins under.</i> One process
    /// reclaims and the rest
    /// move on -- the same discipline <c>Sessions.StraySweep</c> already
    /// applies machine-wide, reused and not reinvented. The timeout is
    /// <see cref="LockScopes.NeverWaits"/> and not
    /// <c>LockScopes.LiveInstanceGate</c>, deleted 2026-10-10, precisely because this may run
    /// while a process is starting: a reclaim is never worth a millisecond of
    /// startup, and a skipped reclaim is not a missed one, because whoever holds
    /// the gate is walking the same directory.
    /// </para>
    /// <para>
    /// <b>A marker is stale only when it is NOT HELD. Existence is not
    /// held-ness</b> -- the same rule <c>Runtime.MaintenanceLock</c> and
    /// <c>Sessions.SessionLock</c> state about their own files, and for
    /// the same reason: a crashed holder leaves the file behind, so existence
    /// means <i>somebody died here once</i> and never <i>somebody is working
    /// now</i>. Held-ness is a sharing violation on an open this file's own
    /// <c>Join</c>, deleted 2026-10-10, would be refused by, and nothing else in the
    /// answer is acted on.
    /// </para>
    /// <para>
    /// <b>Reclaiming another process's live marker would be a serious bug</b> --
    /// it would make a running instance invisible to every later census and
    /// therefore killable by an apply. The negative is proved with a positive
    /// control and not argued:
    /// <c>UpdateTests.AHeldMarkerSurvivesTheReclaimAndTheSameMarkerGoesOnceItIsReleased</c>
    /// holds one marker open, runs this, requires it to survive, releases it,
    /// runs this again and requires it to go -- so a pass that removed nothing at
    /// all could not pass either half.
    /// </para>
    /// </remarks>
    /// <param name="installRoot">The install root, for the directory and the gate's name.</param>
    /// <param name="logger">Where the pass is recorded. Never <c>stdout</c>.</param>
    /// <returns>What the pass found and what it removed.</returns>
    public static LiveMarkerReclaim ReclaimStaleMarkers(string installRoot, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentNullException.ThrowIfNull(logger);

        var directory = DirectoryUnder(installRoot);

        if (!Directory.Exists(directory))
        {
            // Nothing has ever joined here. Not a failure and not worth a mutex.
            return new LiveMarkerReclaim { Outcome = LiveMarkerReclaimOutcome.Ran };
        }

        var mutexName = MutexNameFor(installRoot);

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
                    // No marker is skipped by name. A marker a live process
                    // holds needs no exemption because it is HELD -- which is
                    // the property the pass reads and the only one it is
                    // allowed to act on.
                    var pass = Walk(directory);

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
    /// The live set's own machine-wide gate: the same canonicalisation every
    /// other directory-keyed name in this product uses, in a namespace of its
    /// own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One canonicalisation function, four consumers -- the per-directory gate,
    /// the lock file, the session index key and this. A second spelling is how
    /// two names come to mean different things while both report success.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-23 (previously
    /// <c>SessionPath.Resolve(rootAppDir).MutexName</c>, with no prefix of its
    /// own).</b> That shared the <i>per-directory gate's</i> namespace as well
    /// as its canonicalisation, which made this a fourth scope wearing the
    /// first's names -- and <see cref="Sessions.LockScopes"/> documents three.
    /// A session opened on the install root itself collided <b>exactly</b>, and
    /// nothing refuses that path: <c>CanonicalPath</c> refuses network
    /// paths and aliased spellings, and <c>%LOCALAPPDATA%\BrowserAI</c> is
    /// neither.
    /// </para>
    /// <para>
    /// <b>What the collision cost was silent and lasted the process's life.</b>
    /// <c>Join</c> waits <c>LockScopes.LiveInstanceGate</c>, five
    /// seconds. Queued behind a hold of the 120-second
    /// <see cref="LockScopes.PerDirectoryGate"/> it expires, and a failed join
    /// costs this process its ability to update <b>for good</b> -- one log line,
    /// no refusal, nothing a caller could see. <c>AmIAlone</c>'s census
    /// held the same object from the other side, blocking that directory's
    /// <c>TryAcquire</c>. Found by
    /// [the adversarial review](../../../docs/reviews/2026-08-18-adversarial-locking.md),
    /// B3.
    /// </para>
    /// <para>
    /// <b>The prefix is the remedy the review named and the one this product
    /// had already used once</b> -- <c>BrowserProvisioner.MutexPrefix</c> is
    /// <c>Global\BrowserAI-Provision-</c> for exactly this reason. Adding a
    /// second one is cheaper than making the session guard understand a
    /// directory it otherwise has no opinion about.
    /// </para>
    /// <para>
    /// ⚠️ <b>The name changed, so a BrowserAI from before this and one from
    /// after do not serialise against each other on the live set.</b> That
    /// window is one upgrade wide and what is inside it is safe by
    /// construction: <c>Join</c> creates a file whose name carries a
    /// GUID, and <see cref="Walk"/> only removes a marker it has itself proven
    /// unheld, so two passes running together reach the same answer more slowly
    /// and not a different one.
    /// </para>
    /// </remarks>
    /// <param name="installRoot">The install root, never the data root.</param>
    /// <returns>A <c>Global\</c> name.</returns>
    /// <remarks>
    /// ⚠️ <b>It takes the identity chain and not the canonicaliser in front of
    /// it, and that is a decision -- 2026-08-26.</b> The app root is not a
    /// caller's string: it is this process's own, already judged against the
    /// user's profile through the filesystem by
    /// <see cref="Hosting.InstallRootScope"/>, which resolves both sides of that
    /// comparison the same way <c>Sessions.CanonicalPath</c> would.
    /// Asking again would be a second object-manager call and a directory open
    /// per census for an answer already established, and it would make the live
    /// set's gate refusable -- which is a startup failure wearing an update
    /// check's name.
    /// <para>
    /// ⚠️ <b>Corrected 2026-10-10 by addition: the key is
    /// <see cref="RootKey.For"/>'s</b> (previously <c>RootKeyFor</c>, a copy of the
    /// same derivation kept here, deleted with the census). Neither the derivation
    /// nor the name changed, so a build from before 2026-10-08 and this one still meet
    /// at one gate. <c>Join</c>, <c>AmIAlone</c> and <c>LockScopes.LiveInstanceGate</c>
    /// in the remarks above were deleted that day.
    /// </para>
    /// </remarks>
    public static string MutexNameFor(string installRoot) =>
        MutexPrefix + RootKey.For(installRoot);

    /// <summary>The folder the markers live in, directly under an install root.</summary>
    public const string DirectoryName = "live";

    /// <summary>
    /// Where the markers for one install root are, and the one place that folder
    /// name is spelled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The INSTALL root, and this is the one thing in the product that is
    /// still keyed to it -- 2026-09-15.</b> Everything else moved to the data
    /// root at <c>%LocalAppData%\BrowserAI</c> that day
    /// (<see cref="Hosting.IAppPaths"/>); this did not, and the reason is that
    /// the census asks a question <i>about</i> the install root.
    /// <c>force_stop_package</c> terminates every process whose image path is
    /// under that root, so <i>am I the last one?</i> means <i>is any other
    /// process running out of this install?</i> -- and a set keyed to the data
    /// root would answer about processes a different install root's apply would
    /// not touch, and would miss the ones it would.
    /// </para>
    /// <para>
    /// <b>An uninstalled process passes its data root</b>, because there is no
    /// install root to ask about and the two were the same thing until this date.
    /// That keeps the suite's scratch roots working exactly as they did: what
    /// <c>BROWSERAI_ROOT</c> moves is still one self-consistent set of markers.
    /// </para>
    /// </remarks>
    /// <param name="installRoot">The install root.</param>
    /// <returns>The marker directory.</returns>
    public static string DirectoryUnder(string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        return Path.Combine(installRoot, DirectoryName);
    }

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
    /// cannot come to different conclusions about the same file -- which is what
    /// a second copy of the sharing-violation rule would eventually produce.
    /// <i>Corrected 2026-10-10 by addition: one caller is left,
    /// <see cref="ReclaimStaleMarkers"/>, since the census was deleted, and the
    /// census's parameter <c>own</c>, the marker it skipped by name so that it did
    /// not count itself, went with it.</i>
    /// </remarks>
    /// <param name="directory">The marker directory, which must exist.</param>
    /// <returns>The tallies.</returns>
    private static MarkerWalk Walk(string directory)
    {
        var held = 0;
        var reclaimed = 0;
        var unreclaimed = 0;
        var undetermined = 0;
        string? why = null;

        foreach (var candidate in Directory.EnumerateFiles(directory, "*.live"))
        {
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
                // Not held: whoever wrote it is gone. Removed and not left
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
    /// path</i> and not <i>somebody else is running</i>. The safe side is now
    /// preserved by <c>Census</c>, which lets an uncertainty decide the
    /// verdict when nothing definite did, and by <c>AmIAlone</c>, which
    /// collapses both to <see langword="false"/>. <i>Corrected 2026-10-10 by
    /// addition: both were deleted that day. The reclaim leaves an unknown marker
    /// where it is, which is the safe side for a pass that deletes.</i>
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
