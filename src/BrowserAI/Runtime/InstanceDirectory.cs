// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Runtime;

/// <summary>
/// The directory one run of BrowserAI gives its child: the generated config, the
/// surface child's profile, and the child's working directory.
/// </summary>
/// <remarks>
/// <para>
/// <b>It cannot be cleaned up only on the way out, and that is measured rather
/// than anticipated.</b> The containment contract says BrowserAI may be
/// terminated from outside and run no code afterwards — that is the whole point
/// of the job object — so a <c>finally</c> is by construction the path that does
/// not run in the case that matters. A run that is killed leaves its directory
/// behind, and after nineteen such runs the suite had left nineteen of them.
/// </para>
/// <para>
/// <b>The liveness check is the working-directory lock, not a pid — and the
/// operation that tests it is a rename, not a delete.</b> A pid recorded in the
/// name would need a creation-time pair to be safe against reuse, and would
/// still be wrong the moment a pid was recycled; the lock cannot be wrong. The
/// age guard covers the one gap in it: the instants between creating a directory
/// and the child adopting it as its cwd.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-16 (previously "Windows refuses to delete a directory
/// that is some process's current directory, so <c>Delete</c> simply fails for a
/// run that is still going and succeeds for one that is not").</b> Measured
/// twice on .NET 10.0.11, against a process started with the directory as its
/// cwd: <c>Directory.Delete(path, recursive: true)</c> <b>empties the directory
/// completely</b> and only then fails on the directory node itself. The live
/// run's generated config, its surface child's profile, its output folder and
/// its downloads folder were all gone; what survived was an empty directory. So
/// the sweep did not skip a live run at all — it gutted one and reported
/// nothing, on every startup, against any instance older than
/// <see cref="YoungEnoughToStillBeStarting"/>. <c>Directory.Move</c> refuses the
/// same directory with its contents untouched, and succeeds the moment the
/// holder exits, so the rename is the liveness test the delete was mistaken for.
/// It is also an <b>atomic claim</b>: two BrowserAIs sweeping the same root at
/// the same instant cannot both win it.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-24 (previously "The liveness check is the
/// working-directory lock … the lock cannot be wrong").</b> The lock cannot be
/// wrong about what it measures, and what it measured was the wrong process.
/// <b>Exactly one process ever held this directory as its current directory —
/// the surface child</b> — while the directory holds the generated config of
/// <i>every</i> session in the run, and session children are given the session's
/// own output root instead. So a surface child that died while the run kept
/// serving left nothing holding the directory at all; a directory's
/// <c>GetLastWriteTimeUtc</c> does not move when files inside it are written, so
/// five minutes later another BrowserAI's startup sweep renamed it aside and
/// deleted it, taking every live session's config with it. Found independently
/// by both 2026-08-18 adversarial reviews —
/// [locking](../../../docs/reviews/2026-08-18-adversarial-locking.md) B5 and
/// [processes](../../../docs/reviews/2026-08-18-adversarial-processes.md)
/// finding 11 — and carried as one hazard because it is one.
/// </para>
/// <para>
/// <b>What removes it is a held marker, and the reason is that a sharing
/// violation is a fact the kernel enforces rather than an inference.</b>
/// <see cref="CreateFresh"/> opens <see cref="MarkerFileName"/> inside the
/// directory it just created and holds it for the whole life of the process —
/// the same mechanism <c>Updates.LiveInstances</c>, <c>Sessions.SessionLock</c>
/// and <see cref="MaintenanceLock"/> all already use, and the kernel releases it
/// however the process dies. It is taken by <b>BrowserAI itself</b> rather than
/// by any child, so the signal no longer depends on one child staying alive; and
/// because Windows refuses to rename a directory while any handle is open below
/// it, the marker also makes <see cref="Claim"/>'s rename refuse, which is the
/// gate that was already there.
/// </para>
/// <para>
/// <b>The age guard survives and is no longer load-bearing.</b> It used to cover
/// the whole interval between creating the directory and a child adopting it as
/// a cwd, which is as long as a child takes to start; it now covers the
/// microseconds between <c>CreateDirectory</c> and the marker open two statements
/// later. It is kept because that window is real and costs nothing to hold, not
/// because anything rests on it.
/// </para>
/// <para>
/// This is not the stray sweep. That one is about browsers and windows and lives
/// in <see cref="Sessions.StraySweep"/>; this stops BrowserAI's own bookkeeping
/// growing without limit.
/// </para>
/// </remarks>
internal static class InstanceDirectory
{
    /// <summary>
    /// The name a directory takes while it is being removed, so that a partly
    /// removed one is never confused with a live run's.
    /// </summary>
    /// <remarks>
    /// A fixed shape rather than a suffix on the original name: a suffix would
    /// grow the path by one segment on every sweep that could not finish, and a
    /// tree that survives because its path is too long is the failure this whole
    /// class is about.
    /// </remarks>
    private const string ClaimedPrefix = "sweeping-";

    // The two Win32 codes that mean "somebody else has this open on terms that
    // exclude you", exactly as Updates.LiveInstances reads them off its own
    // markers. Everything else means the question was not answered.
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    /// <summary>
    /// How recently a directory may have been touched and still be swept. It
    /// covers the instants between <see cref="CreateFresh"/> creating a directory
    /// and the same method opening its marker two statements later.
    /// </summary>
    private static readonly TimeSpan YoungEnoughToStillBeStarting = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The file one run holds open inside its own instance directory for the
    /// whole of its life, and the only positive liveness signal here.
    /// </summary>
    /// <remarks>
    /// <b>Beside the generated config rather than inside a subfolder</b>, because
    /// the thing being proved alive is the directory itself. Its name ends
    /// <c>.live</c> to read the same way <c>Updates.LiveInstances</c>' markers
    /// do; nothing parses it and nothing is ever written into it, since
    /// held-ness is a sharing violation and never a file's contents.
    /// </remarks>
    public const string MarkerFileName = "instance.live";

    /// <summary>
    /// Reclaims what earlier runs left behind, then creates this run's own
    /// directory and takes the marker that proves it is live.
    /// </summary>
    /// <remarks>
    /// <b>The caller must dispose the answer, and must dispose it before
    /// <see cref="Delete"/>.</b> The marker is a file inside the tree being
    /// walked, so a caller that deleted first would have its own handle named
    /// back to it as the one node that would not go.
    /// </remarks>
    /// <param name="paths">Where instance directories live.</param>
    /// <param name="logger">Where a directory that would not go is reported.</param>
    /// <returns>The new directory and the marker held inside it.</returns>
    public static InstanceDirectoryHold CreateFresh(IAppPaths paths, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        Sweep(paths.InstanceRoot, logger);

        var directory = Path.Combine(
            paths.InstanceRoot,
            $"{Environment.ProcessId}-{Guid.NewGuid():N}");

        _ = Directory.CreateDirectory(directory);

        var marker = Path.Combine(directory, MarkerFileName);

        try
        {
            // The same open LiveInstances and SessionLock use: deny write to
            // everybody else, allow read, and let the kernel release it however
            // this process dies. Opened inline into the hold's constructor for
            // the reason LiveInstances.Join does the same -- ownership of a
            // FileStream passing through a helper's return value is exactly what
            // CA2000 cannot follow.
            return new InstanceDirectoryHold(
                directory,
                new FileStream(marker, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // ⚠️ A failure to mark is not a failure to start, which is the same
            // posture Updates.LiveInstances.Join takes for the same kind of
            // claim. What is lost is the proof that this directory is live,
            // which puts the run back on the working-directory lock and the age
            // guard it had before -- degraded, and said out loud, rather than
            // refusing to serve over a bookkeeping file.
            InstanceDirectoryLog.NotMarked(logger, marker, failure);
            return new InstanceDirectoryHold(directory, marker: null);
        }
    }

    /// <summary>
    /// Deletes a directory item by item, reporting whatever would not go.
    /// </summary>
    /// <remarks>
    /// <b><see cref="TreeDelete"/>, never <c>Directory.Delete(recursive: true)</c>.</b>
    /// This
    /// runs on the clean exit path, on a directory that has just held a running
    /// browser, and Chromium leaves mapped files behind for a moment after exit —
    /// the race is the normal case rather than the unlucky one. The framework
    /// primitive answers a locked file with one exception naming one node; this
    /// answers with every node that survived, which is what makes a leftover
    /// attributable instead of merely present.
    /// </remarks>
    /// <param name="directory">The directory to remove.</param>
    /// <param name="logger">Where what survived is reported.</param>
    public static void Delete(string directory, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(logger);

        var failures = new List<string>();
        TreeDelete.Remove(directory, failures);

        if (failures.Count is not 0)
        {
            // Not an exit code and not a throw: a directory something still
            // holds open is the next run's sweep. But it is never silent, which
            // is the half that was missing.
            InstanceDirectoryLog.NotFullyRemoved(
                logger,
                directory,
                failures.Count,
                string.Join(Environment.NewLine, failures));
        }
    }

    private static void Sweep(string root, ILogger logger)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        var cutoff = DateTime.UtcNow - YoungEnoughToStillBeStarting;

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            // ⚠️ FIRST, because it is the only question in this loop the kernel
            // answers rather than one this code infers. A held marker is a live
            // BrowserAI and nothing else can be, so the pass can say WHY it left
            // a directory alone -- which the rename below cannot, since it is
            // refused just as readily by a scanner's handle or a denied ACL.
            if (IsMarkerHeld(directory))
            {
                InstanceDirectoryLog.HeldByALiveInstance(logger, directory, MarkerFileName);
                continue;
            }

            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) > cutoff)
                {
                    continue;
                }
            }
#pragma warning disable CA1031 // A directory that cannot be stat'd is one to leave alone.
            catch (Exception)
#pragma warning restore CA1031
            {
                continue;
            }

            if (Claim(directory) is not { } claimed)
            {
                InstanceDirectoryLog.StillHeld(logger, directory);
                continue;
            }

            Delete(claimed, logger);
        }
    }

    /// <summary>
    /// Whether a candidate directory's marker is open in some live process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Held-ness is a sharing violation and never the file's existence</b>,
    /// which is the rule <c>Sessions.SessionLock</c>, <see cref="MaintenanceLock"/>
    /// and <c>Updates.LiveInstances</c> all follow: a crashed run leaves the
    /// marker behind, so existence would mean <i>somebody died here once</i>.
    /// </para>
    /// <para>
    /// <b>Everything that is not a sharing violation answers <see langword="false"/>,
    /// and that is deliberate.</b> Absent, denied, on a directory that vanished
    /// mid-enumeration — none of those is <i>held</i>, and none of them is acted
    /// on here either: <see cref="Claim"/>'s rename is still the claim and still
    /// the last word, and it refuses for any of those reasons just as surely.
    /// Answering <see langword="true"/> on an unreadable marker would make every
    /// directory this token cannot open permanent.
    /// </para>
    /// <para>
    /// <b>It is a boolean rather than the three-valued census
    /// <c>Updates.LiveInstances.Probe</c> answers</b>, because there is nothing
    /// here for a third value to decide: the updater has to weigh <i>could not
    /// tell</i> against <i>alone</i> before an apply that kills processes, and
    /// this only chooses whether to attempt a rename that is itself the guard.
    /// </para>
    /// </remarks>
    /// <param name="directory">The candidate.</param>
    /// <returns>Whether a live process holds its marker.</returns>
    private static bool IsMarkerHeld(string directory)
    {
        try
        {
            using var probe = new FileStream(
                Path.Combine(directory, MarkerFileName),
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 1);

            return false;
        }
        catch (IOException failure) when ((failure.HResult & 0xFFFF) is ErrorSharingViolation or ErrorLockViolation)
        {
            return true;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Takes a directory out of the live set, or answers that it could not.
    /// </summary>
    /// <remarks>
    /// The rename is both halves of the check at once. It fails while any
    /// process holds the directory as its current directory — which is the
    /// liveness signal, and it fails <i>before</i> a single file has been
    /// touched — and it succeeds atomically, so a second BrowserAI sweeping the
    /// same root at the same instant meets a path that is no longer there rather
    /// than a tree it is also deleting.
    /// </remarks>
    /// <param name="directory">The candidate.</param>
    /// <returns>The name it now has, or <see langword="null"/> when something still holds it.</returns>
    private static string? Claim(string directory)
    {
        var claimed = Path.Combine(
            Path.GetDirectoryName(directory) ?? directory,
            $"{ClaimedPrefix}{Guid.NewGuid():N}");

        try
        {
            Directory.Move(directory, claimed);
            return claimed;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// One run's instance directory, and the marker that proves this process is
/// still alive in it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Disposing it releases the marker and nothing else.</b> The directory
/// itself is removed by <see cref="InstanceDirectory.Delete"/> on the clean exit
/// path, or by the next run's sweep on the path where this process was
/// terminated from outside and ran no code at all — which is the case the whole
/// sweep exists for.
/// </para>
/// <para>
/// ⚠️ <b>The order matters and it is the caller's to keep:</b> release this
/// <i>before</i> deleting the tree, or the one handle this process holds is the
/// one node <c>TreeDelete</c> reports as a survivor.
/// </para>
/// </remarks>
internal sealed class InstanceDirectoryHold : IDisposable
{
    private readonly FileStream? _marker;

    /// <summary>Takes ownership of one run's directory and its marker.</summary>
    /// <param name="directory">The directory this run was given.</param>
    /// <param name="marker">The held marker, or <see langword="null"/> when it could not be taken.</param>
    internal InstanceDirectoryHold(string directory, FileStream? marker)
    {
        Directory = directory;
        _marker = marker;
    }

    /// <summary>This run's instance directory, absolute.</summary>
    public string Directory { get; }

    /// <summary>
    /// Whether the marker was taken. <see langword="false"/> is a run whose
    /// liveness is back to the working-directory lock and the age guard, and it
    /// has already been reported.
    /// </summary>
    public bool IsMarked => _marker is not null;

    /// <inheritdoc />
    public void Dispose() => _marker?.Dispose();
}

/// <summary>What the instance sweep could not do, and why.</summary>
internal static partial class InstanceDirectoryLog
{
    /// <summary>
    /// A tree was walked and something in it survived.
    /// </summary>
    /// <remarks>
    /// Warning rather than Information: an instance directory that will not go
    /// is disk this product is responsible for and is not reclaiming, and the
    /// per-node list is the only thing that says which file held it.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="directory">The tree that was walked.</param>
    /// <param name="count">How many nodes survived.</param>
    /// <param name="failures">Each of them, one per line.</param>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "The instance directory {Directory} was not fully removed: {Count} node(s) would not go. The next run's sweep tries again.\n{Failures}")]
    public static partial void NotFullyRemoved(ILogger logger, string directory, int count, string failures);

    /// <summary>
    /// A candidate could not be claimed, so nothing in it was touched.
    /// </summary>
    /// <remarks>
    /// Debug, because it is the ordinary state whenever a second BrowserAI is
    /// running: the rename is refused while any process holds the directory as
    /// its current directory, which is exactly what a live run does.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="directory">The candidate that was left alone.</param>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "The instance directory {Directory} could not be renamed aside, so something still holds it and it was left untouched.")]
    public static partial void StillHeld(ILogger logger, string directory);

    /// <summary>
    /// A candidate belongs to a BrowserAI that is still running, proven by the
    /// marker it holds.
    /// </summary>
    /// <remarks>
    /// Debug, because it is the ordinary state whenever a second BrowserAI is
    /// running — and it is a <i>different</i> line from
    /// <see cref="StillHeld"/> on purpose. That one says a rename was refused
    /// and cannot say by what; this one names a live instance.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="directory">The candidate that was left alone.</param>
    /// <param name="marker">The marker file whose holder proved it.</param>
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Debug,
        Message = "The instance directory {Directory} belongs to a BrowserAI that is still running — its marker '{Marker}' is open — so nothing in it was touched.")]
    public static partial void HeldByALiveInstance(ILogger logger, string directory, string marker);

    /// <summary>
    /// This run could not take the marker inside its own instance directory.
    /// </summary>
    /// <remarks>
    /// Warning: the run serves normally, and what it has lost is the proof that
    /// its directory is live. Another BrowserAI's startup sweep is then back to
    /// judging it on the working-directory lock and a timestamp, which is the
    /// state this marker exists to replace.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="marker">The marker that could not be taken.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "'{Marker}' could not be created and held, so this run cannot prove its instance directory is live. It is serving anyway; another BrowserAI's sweep now judges that directory on a rename and a timestamp, which is what the marker replaced.")]
    public static partial void NotMarked(ILogger logger, string marker, Exception failure);
}
