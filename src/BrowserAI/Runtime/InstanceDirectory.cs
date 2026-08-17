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

    /// <summary>
    /// How recently a directory may have been touched and still be swept. A run
    /// that has just started has created its directory but may not yet have a
    /// child holding it open.
    /// </summary>
    private static readonly TimeSpan YoungEnoughToStillBeStarting = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Reclaims what earlier runs left behind, then creates this run's own
    /// directory.
    /// </summary>
    /// <param name="paths">Where instance directories live.</param>
    /// <param name="logger">Where a directory that would not go is reported.</param>
    /// <returns>The new directory's absolute path.</returns>
    public static string CreateFresh(IAppPaths paths, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        Sweep(paths.InstanceRoot, logger);

        var directory = Path.Combine(
            paths.InstanceRoot,
            $"{Environment.ProcessId}-{Guid.NewGuid():N}");

        _ = Directory.CreateDirectory(directory);
        return directory;
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
}
