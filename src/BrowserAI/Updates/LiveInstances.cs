// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Hosting;
using BrowserAI.Sessions;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Updates;

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
/// ([§D](../../../plan/D-locking.md)). Each run creates one file and holds it
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
/// <b>Deliberately not <see cref="IAppPaths.InstanceRoot"/>.</b> That
/// directory's liveness signal is the child holding it as a working directory,
/// so a run has no signal until its child has started — and the update check
/// runs on a background thread from the moment the process starts, which is
/// inside exactly that window.
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
            var acquired = gate.Acquire(LockScopes.PerDirectoryGate);

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
    /// root.
    /// </summary>
    /// <remarks>
    /// <b>Every uncertainty answers <see langword="false"/>.</b> A marker that
    /// cannot be opened for a reason other than sharing, a directory that cannot
    /// be enumerated, a mutex that cannot be taken — all of them mean <i>do not
    /// apply</i>, because the cost of being wrong in that direction is a delayed
    /// update and the cost of being wrong in the other is every other agent's
    /// session.
    /// </remarks>
    /// <returns><see langword="true"/> only when nothing else is alive.</returns>
    public bool AmIAlone()
    {
        if (_held is null)
        {
            return false;
        }

        try
        {
            using var gate = MachineMutex.Create(_mutexName);

            if (gate.Acquire(LockScopes.PerDirectoryGate) is MutexAcquisition.NotAcquired)
            {
                return false;
            }

            try
            {
                var others = 0;

                foreach (var candidate in Directory.EnumerateFiles(_directory, "*.live"))
                {
                    if (string.Equals(candidate, OwnFile, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (IsHeld(candidate))
                    {
                        others++;
                        continue;
                    }

                    // Not held: whoever wrote it is gone. Reclaim it here rather
                    // than leaving a growing pile that makes every later census
                    // slower, and delete it under the same mutex that guards a
                    // join so a starting process cannot lose its own file.
                    TryDelete(candidate);
                }

                if (others is not 0)
                {
                    UpdateLog.NotAlone(_logger, others);
                }

                return others is 0;
            }
            finally
            {
                gate.Release();
            }
        }
#pragma warning disable CA1031 // Any failure to establish solitude is answered "not alone", which is the only safe direction.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            UpdateLog.CouldNotCensusLiveSet(_logger, _directory, failure);
            return false;
        }
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
        TryDelete(OwnFile);
    }

    /// <summary>
    /// The per-root mutex name, from the same canonicalisation every other
    /// directory-keyed name in this product uses.
    /// </summary>
    /// <remarks>
    /// One canonicalisation function, four consumers — the per-directory gate,
    /// the lock file, the session index key and now this. A second spelling is
    /// how two names come to mean different things while both report success.
    /// </remarks>
    /// <param name="rootAppDir">The install root.</param>
    /// <returns>A <c>Global\</c> name.</returns>
    public static string MutexNameFor(string rootAppDir) => SessionPath.Resolve(rootAppDir).MutexName;

    private static bool IsHeld(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1);
            return false;
        }
        catch (IOException failure) when ((failure.HResult & 0xFFFF) is ErrorSharingViolation or ErrorLockViolation)
        {
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
#pragma warning disable CA1031 // Anything else -- a permission problem, a locked volume -- is treated as a live instance, which is the safe answer.
        catch (Exception)
#pragma warning restore CA1031
        {
            return true;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
#pragma warning disable CA1031 // A marker that will not delete is litter; the next census retries it.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
