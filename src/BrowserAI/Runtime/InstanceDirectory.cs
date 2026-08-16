// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;

namespace BrowserAI.Runtime;

/// <summary>
/// The directory one run of BrowserAI gives its child: the generated config, and
/// the child's working directory.
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
/// <b>The liveness check is the working-directory lock, not a pid.</b> Windows
/// refuses to delete a directory that is some process's current directory, so
/// <see cref="Delete"/> simply fails for a run that is still going and succeeds
/// for one that is not. A pid recorded in the name would need a creation-time
/// pair to be safe against reuse, and would still be wrong the moment a pid was
/// recycled; the lock cannot be wrong. The age guard covers the one gap in it:
/// the instants between creating a directory and the child adopting it as its
/// cwd.
/// </para>
/// <para>
/// This is not the stray sweep. That one is about browsers and windows, it is
/// specified in [§C](../../plan/C-sessions.md) and it arrives at build-order
/// step 16; this is four lines that stop BrowserAI's own bookkeeping growing
/// without limit in the meantime.
/// </para>
/// </remarks>
internal static class InstanceDirectory
{
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
    /// <returns>The new directory's absolute path.</returns>
    public static string CreateFresh(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        Sweep(paths.InstanceRoot);

        var directory = Path.Combine(
            paths.InstanceRoot,
            $"{Environment.ProcessId}-{Guid.NewGuid():N}");

        _ = Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// Deletes a directory if nothing holds it, which is the normal exit path.
    /// </summary>
    /// <param name="directory">The directory to remove.</param>
    public static void Delete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
#pragma warning disable CA1031 // A directory something still holds open is the next run's sweep, never this run's exit code.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private static void Sweep(string root)
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

            Delete(directory);
        }
    }
}
