// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.TestProbe;

/// <summary>
/// The cross-process half of the session lock's evidence.
/// </summary>
/// <remarks>
/// <para>
/// <b>A single-threaded stub proves nothing here.</b> Every property the lock
/// claims is a property of two or more <i>processes</i>: a named mutex is a
/// kernel object shared between them, a file-sharing violation is what one
/// process's open does to another's, an abandoned mutex requires a holder that
/// really died, and "exactly one acquires" is a statement about a race that has
/// to be run. So the assertions live in the test host and the contenders live
/// here, driving the real product types.
/// </para>
/// <para>
/// <b>Nothing here can outlive its test.</b> Every wait is bounded by
/// <see cref="Patience"/> as well as by whatever the host does, so a probe whose
/// host died still exits on its own — belt as well as the test's job object.
/// </para>
/// </remarks>
internal static class SessionProbe
{
    /// <summary>
    /// The longest any probe waits for anything before giving up and exiting.
    /// A leaked process is a defect in the test; this is the backstop for the
    /// case where the test itself is what failed.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Reports every name the canonicaliser derives from one spelling of a
    /// directory, resolved against <b>this</b> process's working directory.
    /// </summary>
    /// <remarks>
    /// A relative spelling can only be tested from a process whose current
    /// directory is the one it is relative to, and the test host's is fixed.
    /// Setting <c>Environment.CurrentDirectory</c> inside a parallel test host
    /// would be a global mutation aimed at a local question.
    /// </remarks>
    /// <param name="spelling">Any spelling of a directory.</param>
    /// <param name="reportPath">Where to write the derived names.</param>
    /// <returns>Zero.</returns>
    public static int Identity(string spelling, string reportPath)
    {
        var path = SessionPath.Resolve(spelling);

        Write(reportPath, new JsonObject
        {
            ["spelling"] = spelling,
            ["workingDirectory"] = Environment.CurrentDirectory,
            ["fullPath"] = path.FullPath,
            ["key"] = path.Key,
            ["hash"] = path.Hash,
            ["mutexName"] = path.MutexName,
            ["indexKey"] = path.IndexKey,
            ["lockFile"] = path.LockFile,
        });

        return 0;
    }

    /// <summary>
    /// One contender in the race: waits on a start gate shared by all of them,
    /// attempts the directory once, and reports what it got.
    /// </summary>
    /// <param name="directory">The session directory every contender is racing for.</param>
    /// <param name="startEventName">A named event the host sets to release them all at once.</param>
    /// <param name="reportPath">Where to write this contender's outcome.</param>
    /// <param name="releasePath">A file the host creates when the winner may let go.</param>
    /// <returns>Zero.</returns>
    public static int Race(string directory, string startEventName, string reportPath, string releasePath)
    {
        var path = SessionPath.Resolve(directory);

        using (var start = EventWaitHandle.OpenExisting(startEventName))
        {
            // The host waits for every contender's ready file before setting the
            // event, so that the attempt below really is simultaneous. Without
            // it the event is already set by the time a late starter opens it,
            // and the "race" is a queue.
            File.WriteAllText($"{reportPath}.ready", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            _ = start.WaitOne(Patience);
        }

        var request = new SessionLockRequest
        {
            Mode = "headless",
            Browser = "chromium",
            Purpose = $"race contender {Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}",
        };

        var clock = Stopwatch.StartNew();
        var result = SessionLock.TryAcquire(path, request, NullLogger.Instance);
        var elapsed = clock.Elapsed;

        try
        {
            Write(reportPath, new JsonObject
            {
                ["pid"] = Environment.ProcessId,
                ["outcome"] = result.Outcome.ToString(),
                ["taken"] = result.Taken,
                ["message"] = result.Message,
                ["holderPid"] = result.Holder?.Holder.ProcessId,
                ["gateWasAbandoned"] = result.Acquired?.GateWasAbandoned ?? false,
                ["elapsedMilliseconds"] = elapsed.TotalMilliseconds,
            });

            // The winner must still be holding when the losers make their
            // attempt, or a second acquire would be correct behaviour and the
            // race would prove nothing. The host decides when that is over.
            if (result.Taken)
            {
                WaitForFile(releasePath);
            }
        }
        finally
        {
            result.Acquired?.Dispose();
        }

        return 0;
    }

    /// <summary>
    /// Takes the directory and stays alive holding it, so the host can kill it
    /// from outside and prove the reclaim path.
    /// </summary>
    /// <param name="directory">The session directory.</param>
    /// <param name="readyPath">Written once the lock is held.</param>
    /// <param name="purpose">What to record as the purpose.</param>
    /// <returns>Zero if it never acquired, otherwise it is killed before returning.</returns>
    public static int Hold(string directory, string readyPath, string purpose)
    {
        var path = SessionPath.Resolve(directory);

        var result = SessionLock.TryAcquire(
            path,
            new SessionLockRequest { Mode = "headless", Browser = "chromium", Purpose = purpose },
            NullLogger.Instance);

        try
        {
            Write(readyPath, new JsonObject
            {
                ["pid"] = Environment.ProcessId,
                ["outcome"] = result.Outcome.ToString(),
                ["taken"] = result.Taken,
                ["message"] = result.Message,
            });

            if (!result.Taken)
            {
                return 1;
            }

            // Killed from outside. Reaching the end of this wait means the host
            // failed to do so, and the exit code says which.
            Thread.Sleep(Patience);
            return 2;
        }
        finally
        {
            result.Acquired?.Dispose();
        }
    }

    /// <summary>
    /// Takes the <b>per-directory mutex only</b> and stays alive holding it, so
    /// that killing this process abandons it.
    /// </summary>
    /// <remarks>
    /// A mutex is abandoned when the thread that owns it dies without releasing.
    /// Nothing short of a real process death produces that, which is why this is
    /// a probe and not a stub.
    /// </remarks>
    /// <param name="directory">The session directory whose gate to take.</param>
    /// <param name="readyPath">Written once the mutex is held.</param>
    /// <returns>Two if the host failed to kill it.</returns>
    public static int HoldGate(string directory, string readyPath)
    {
        var path = SessionPath.Resolve(directory);

        using var gate = MachineMutex.Create(path.MutexName);
        var acquisition = gate.Acquire(TimeSpan.FromSeconds(30));

        Write(readyPath, new JsonObject
        {
            ["pid"] = Environment.ProcessId,
            ["mutexName"] = gate.Name,
            ["acquisition"] = acquisition.ToString(),
        });

        if (acquisition is MutexAcquisition.NotAcquired)
        {
            return 1;
        }

        Thread.Sleep(Patience);
        return 2;
    }

    /// <summary>
    /// The sweep scope: try-acquire-and-skip at zero timeout, from N processes
    /// at once.
    /// </summary>
    /// <param name="mutexName">The machine-wide name to contend for.</param>
    /// <param name="startEventName">A named event the host sets to release them all at once.</param>
    /// <param name="reportPath">Where to write this process's outcome.</param>
    /// <param name="releasePath">A file the host creates when the winner may let go.</param>
    /// <returns>Zero.</returns>
    public static int Sweep(string mutexName, string startEventName, string reportPath, string releasePath)
    {
        using var mutex = MachineMutex.Create(mutexName);

        using (var start = EventWaitHandle.OpenExisting(startEventName))
        {
            File.WriteAllText($"{reportPath}.ready", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            _ = start.WaitOne(Patience);
        }

        var clock = Stopwatch.StartNew();
        var acquisition = mutex.Acquire(LockScopes.NeverWaits);
        var elapsed = clock.Elapsed;

        try
        {
            Write(reportPath, new JsonObject
            {
                ["pid"] = Environment.ProcessId,
                ["acquisition"] = acquisition.ToString(),
                ["elapsedMilliseconds"] = elapsed.TotalMilliseconds,
            });

            if (acquisition is not MutexAcquisition.NotAcquired)
            {
                WaitForFile(releasePath);
            }
        }
        finally
        {
            if (acquisition is not MutexAcquisition.NotAcquired)
            {
                mutex.Release();
            }
        }

        return 0;
    }

    /// <summary>
    /// Holds the directory and rewrites its record over and over, so a reader in
    /// another process can be shown never to see a torn file.
    /// </summary>
    /// <param name="directory">The session directory.</param>
    /// <param name="readyPath">Written once the lock is held.</param>
    /// <param name="rewrites">How many times to replace the record.</param>
    /// <param name="donePath">Written when every rewrite is finished.</param>
    /// <returns>Zero.</returns>
    public static int Rewrite(string directory, string readyPath, int rewrites, string donePath)
    {
        var path = SessionPath.Resolve(directory);

        var result = SessionLock.TryAcquire(
            path,
            new SessionLockRequest { Mode = "headless", Browser = "chromium", Purpose = "rewrite probe" },
            NullLogger.Instance);

        try
        {
            Write(readyPath, new JsonObject
            {
                ["pid"] = Environment.ProcessId,
                ["outcome"] = result.Outcome.ToString(),
                ["taken"] = result.Taken,
            });

            if (result.Acquired is not { } held)
            {
                return 1;
            }

            // The host starts its reader loop and then says go. Without the
            // handshake the rewrites can be finished before the reader is
            // scheduled at all, and the test passes having observed nothing.
            WaitForFile($"{readyPath}.go");

            var completed = 0;
            string? failure = null;

            try
            {
                for (var i = 0; i < rewrites; i++)
                {
                    var round = i;
                    held.Rewrite(current => current with
                    {
                        LastUsed = DateTimeOffset.Now,
                        Purpose = $"rewrite {round.ToString(CultureInfo.InvariantCulture)}",
                    });

                    completed++;
                }
            }
#pragma warning disable CA1031 // A crash here must reach the host as a named failure. Left to escape it is drained into the void and presents as a 90-second timeout.
            catch (Exception thrown)
#pragma warning restore CA1031
            {
                failure = $"{thrown.GetType().Name}: {thrown.Message}";
            }

            Write(donePath, new JsonObject
            {
                ["rewrites"] = completed,
                ["requested"] = rewrites,
                ["failure"] = failure,
            });

            return failure is null ? 0 : 3;
        }
        finally
        {
            result.Acquired?.Dispose();
        }
    }

    private static void WaitForFile(string path)
    {
        var clock = Stopwatch.StartNew();

        while (!File.Exists(path) && clock.Elapsed < Patience)
        {
            Thread.Sleep(10);
        }
    }

    /// <summary>
    /// Writes a report the host polls for. The temp-and-rename is not decoration:
    /// a host that reads the file the instant it appears would otherwise read a
    /// partly-written one, and that failure looks exactly like the product being
    /// wrong.
    /// </summary>
    private static void Write(string path, JsonObject report)
    {
        var temp = $"{path}.writing";
        File.WriteAllText(temp, report.ToJsonString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }
}
