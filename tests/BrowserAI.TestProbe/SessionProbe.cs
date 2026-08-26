// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Hosting;
using BrowserAI.Interop;
using BrowserAI.Logging;
using BrowserAI.Sessions;
using Microsoft.Extensions.Logging;
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
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-18 (previously two minutes).</b> This is a
    /// SELF-DESTRUCT BACKSTOP, and a backstop that fires while its test is still
    /// running is not a backstop — it is a promptness assertion on the test host,
    /// enforced from another process, that fails as <i>"the process I planted has
    /// gone"</i>. Two minutes is reachable: at
    /// <c>SuiteParallelism.Unbounded</c> the suite puts 419 tests on one machine
    /// at once and <c>SaturationTests</c> alone runs a hundred processes, and a
    /// test that holds one of these probes across that can legitimately take
    /// longer.
    /// </para>
    /// <para>
    /// <b>Nothing depends on this being tight.</b> Every probe is launched into a
    /// <c>JobObjectScope</c> carrying <c>KILL_ON_JOB_CLOSE</c>, so the host
    /// finishing — or dying — closes the last handle and the kernel takes the
    /// probe with it. This only has to be shorter than "forever", so that a probe
    /// started outside a job by a hand-run command cannot outlive the day. Half
    /// an hour is that, and it is the same shape as the suite's own
    /// <c>TestDefaults.ProcessHang</c>, which cannot be referenced from here
    /// because this is a separate executable.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Reports what the one path function makes of a spelling, from a process
    /// whose working directory the caller chose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A relative spelling can only be judged from a process whose current
    /// directory is the one it is relative to, and the test host's is fixed.
    /// Setting <c>Environment.CurrentDirectory</c> inside a parallel test host
    /// would be a global mutation aimed at a local question.
    /// </para>
    /// <para>
    /// ⚠️ <b>It reports the refusal as well as the names — 2026-08-26, previously
    /// only the derived names.</b> A relative spelling used to canonicalise
    /// against this process's own working directory and produce an identity; it
    /// is refused now, and a probe that could only report an identity had no way
    /// to say so except by crashing.
    /// </para>
    /// </remarks>
    /// <param name="spelling">Any spelling of a directory.</param>
    /// <param name="reportPath">Where to write the verdict.</param>
    /// <returns>Zero.</returns>
    public static int Identity(string spelling, string reportPath)
    {
        var verdict = CanonicalPath.Of(spelling, PathOrigin.Named, "directory");
        var path = verdict.Canonical is null ? null : SessionPath.For(verdict.Canonical);

        Write(reportPath, new JsonObject
        {
            ["spelling"] = spelling,
            ["workingDirectory"] = Environment.CurrentDirectory,
            ["canonical"] = verdict.Canonical,
            ["refusal"] = verdict.Refusal,
            ["unestablished"] = verdict.Unestablished,
            ["fullPath"] = path?.FullPath,
            ["key"] = path?.Key,
            ["hash"] = path?.Hash,
            ["mutexName"] = path?.MutexName,
            ["indexKey"] = path?.IndexKey,
            ["lockFile"] = path?.LockFile,
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
        var path = SessionPath.For(directory);

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
            Browser = "chromium",
            Purpose = $"race contender {Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}",
        };

        // ⚠️ EVERY FIELD BELOW IS THERE BECAUSE ONE OCCURRENCE WENT UNDIAGNOSED.
        // Measured 2026-08-18: a loser reported an outcome other than `Held` once
        // in eighteen full-suite runs, and the report it wrote carried the
        // outcome, the message and nothing else — so the run that caught it could
        // not say which outcome, what the machine looked like, or whether the
        // holder it failed to name was even alive. A failure that names its own
        // state costs one run to diagnose; one that says "not Held" costs twenty.
        //
        // The three non-`Held` refusals `TryAcquire` can return are told apart by
        // different fields, which is why they are all here:
        //
        //   Busy       -- the per-directory gate expired. `elapsedMilliseconds`
        //                 sits at the gate's own timeout and the lock file is
        //                 perfectly readable, naming a live holder.
        //   Unreadable -- the record parsed as damage, or could not be written.
        //                 `lockFile` carries the bytes that did it.
        //   Refused    -- the machine-wide mutex could not be created at all.
        //                 Nothing is on disk and the message names the Win32 cause.
        var clock = Stopwatch.StartNew();
        SessionLockResult? result = null;
        string? failure = null;

        try
        {
            result = SessionLock.TryAcquire(path, request, NullLogger.Instance);
        }
#pragma warning disable CA1031 // A throw out of TryAcquire is the most interesting outcome of all, and unwritten it presents to the host as a report that never appeared.
        catch (Exception thrown)
#pragma warning restore CA1031
        {
            failure = $"{thrown.GetType().Name}: {thrown.Message}";
        }

        var elapsed = clock.Elapsed;

        // Taken AFTER the attempt and before anything else runs, so it is as
        // close to the instant of the outcome as this process can get.
        var onDisk = StateOfTheLockFile(path.LockFile);

        try
        {
            Write(reportPath, new JsonObject
            {
                ["pid"] = Environment.ProcessId,
                ["createdFileTime"] = ProcessLiveness.CreationTimeOfThisProcess(),
                ["outcome"] = result?.Outcome.ToString(),
                ["taken"] = result?.Taken ?? false,
                ["message"] = result?.Message,
                ["failure"] = failure,
                ["holderPid"] = result?.Guard?.ProcessId,
                ["holderCreatedFileTime"] = result?.Guard?.ProcessCreatedFileTime,
                ["holderRunning"] = result?.HolderRunning ?? false,
                ["gateWasAbandoned"] = result?.Acquired?.GateWasAbandoned ?? false,
                ["elapsedMilliseconds"] = elapsed.TotalMilliseconds,
                ["gateTimeoutMilliseconds"] = LockScopes.PerDirectoryGate.TotalMilliseconds,
                ["lockFile"] = onDisk,
            });

            // The winner must still be holding when the losers make their
            // attempt, or a second acquire would be correct behaviour and the
            // race would prove nothing. The host decides when that is over.
            if (result?.Taken is true)
            {
                WaitForFile(releasePath);
            }
        }
        finally
        {
            result?.Acquired?.Dispose();
        }

        return 0;
    }

    /// <summary>
    /// What <c>browserai.json</c> looked like at the instant an attempt on it ended.
    /// </summary>
    /// <remarks>
    /// <b>Read with every share flag set, so that this diagnostic can never be
    /// the thing that fails.</b> It is looking at a file another process holds
    /// <c>FileAccess.ReadWrite, FileShare.Read</c> and may be renaming over, and
    /// a probe that threw while describing the state would destroy the only
    /// evidence there was. The temp count is the decisive column when the record
    /// is absent: the writer's <c>browserai.json.new-&lt;guid&gt;</c> exists for
    /// exactly the length of one rewrite.
    /// </remarks>
    /// <param name="lockFile">The lock file to describe.</param>
    /// <returns>What the machine said, for the host's failure message.</returns>
    private static JsonObject StateOfTheLockFile(string lockFile)
    {
        var state = new JsonObject { ["exists"] = File.Exists(lockFile) };
        var directory = Path.GetDirectoryName(lockFile);

#pragma warning disable CA1031 // Every catch in this method is deliberately unnarrowed: this is the only evidence a failed run will have, and a diagnostic that throws while describing a failure destroys it.
        try
        {
            var file = new FileInfo(lockFile);
            state["length"] = file.Exists ? file.Length : -1;
        }
        catch (Exception thrown)
        {
            state["length"] = -2;
            state["lengthFailure"] = thrown.Message;
        }

        try
        {
            using var stream = new FileStream(
                lockFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1);

            using var reader = new StreamReader(stream);
            state["text"] = reader.ReadToEnd();
        }
        catch (Exception thrown)
        {
            state["text"] = null;
            state["textFailure"] = $"{thrown.GetType().Name}: {thrown.Message}";
        }

        try
        {
            state["rewriteTempsPresent"] = directory is null
                ? -1
                : Directory.EnumerateFiles(directory, $"{SessionLayout.LockFileName}.new-*").Count();

            state["entries"] = directory is null
                ? null
                : string.Join(", ", Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        }
        catch (Exception thrown)
        {
            state["rewriteTempsPresent"] = -2;
            state["entriesFailure"] = $"{thrown.GetType().Name}: {thrown.Message}";
        }
#pragma warning restore CA1031

        return state;
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
        var path = SessionPath.For(directory);

        var result = SessionLock.TryAcquire(
            path,
            new SessionLockRequest { Browser = "chromium", Purpose = purpose },
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
        var path = SessionPath.For(directory);

        using var gate = MachineMutex.Create(path.MutexName);
        // Patience, not thirty seconds: this probe exists to BE the holder, and an
        // acquire that gave up early would make the test flaky in the one
        // direction that reads as a product defect. Corrected 2026-08-18.
        var acquisition = gate.Acquire(Patience);

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
    /// Takes a named machine-wide mutex the caller names and holds it until
    /// killed.
    /// </summary>
    /// <remarks>
    /// <b>The wait is bounded rather than zero, unlike <see cref="Sweep"/>'s.</b>
    /// This probe exists to <i>be</i> the holder — for the skip path and for the
    /// abandoned-mutex path — so it must end up holding the object even if
    /// another process on the machine is momentarily using the same name. A
    /// zero-timeout acquire here would make the test flaky in the one direction
    /// that reads as a product defect.
    /// </remarks>
    /// <param name="mutexName">The <c>Global\</c> name to take.</param>
    /// <param name="readyPath">Written once it is held.</param>
    /// <returns>One if it never acquired, two if the host failed to kill it.</returns>
    public static int HoldNamed(string mutexName, string readyPath)
    {
        using var mutex = MachineMutex.Create(mutexName);
        var acquisition = mutex.Acquire(Patience);

        Write(readyPath, new JsonObject
        {
            ["pid"] = Environment.ProcessId,
            ["mutexName"] = mutex.Name,
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
    /// <remarks>
    /// <b>The report carries the timeout the acquire was handed, not how long it
    /// took.</b> A stopwatch here would be measuring the scheduler on a loaded
    /// box, across a process boundary, in each of eight processes started
    /// together — and process creation is the most contended operation there is.
    /// <see cref="MachineMutex.LastAcquireTimeout"/> is set by the acquire from
    /// its own argument, so nothing a starved machine does can move it.
    /// </remarks>
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

        var acquisition = mutex.Acquire(LockScopes.NeverWaits);

        try
        {
            Write(reportPath, new JsonObject
            {
                ["pid"] = Environment.ProcessId,
                ["acquisition"] = acquisition.ToString(),

                // The wait this acquire was HANDED, read back off the gate --
                // never the wall-clock time the call took. See
                // MachineMutex.LastAcquireTimeout for what it proves and what it
                // deliberately does not, and
                // SessionLockTests.AGateRecordsTheWaitItWasHandedRatherThanAConstant
                // for the control that the record follows the argument.
                ["acquireTimeoutTicks"] = mutex.LastAcquireTimeout?.Ticks,
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
    /// Runs one real stray sweep in a process of its own, so what it writes to
    /// <c>stdout</c> can be counted rather than assumed.
    /// </summary>
    /// <remarks>
    /// <b>Out of process because that is the only place the question is
    /// answerable.</b> <c>stdout</c> is a process-wide handle, and a test host
    /// that redirects <see cref="Console.Out"/> is measuring its own redirection
    /// as much as the product. Here the host reads the pipe: whatever the sweep
    /// wrote is what arrives, and the expected count is zero bytes. This probe
    /// writes nothing to <c>stdout</c> itself, which is why the count means what
    /// it says.
    /// </remarks>
    /// <param name="reportPath">Where to write the pass's own census.</param>
    /// <param name="images">The image paths that count as ours, separated by <c>;</c>.</param>
    /// <returns>Zero.</returns>
    public static int StraySweepPass(string reportPath, string images)
    {
        var sweep = new StraySweep(
            [.. images.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            index: null,
            NullLogger.Instance);

        var result = sweep.Run();

        Write(reportPath, new JsonObject
        {
            ["pid"] = Environment.ProcessId,
            ["outcome"] = result.Outcome.ToString(),
            ["summary"] = result.Summary,
            ["candidates"] = result.Candidates,
            ["terminated"] = result.Terminated.Count,
            ["windows"] = result.WindowsWalked,

            // The strings, not just the count: a title this machine really
            // publishes and the guard really refuses is the only evidence that
            // the guard is doing anything at all.
            ["rejectedTitles"] = new JsonArray([.. result.RejectedTitles.Select(title => (JsonNode)title)]),
        });

        return 0;
    }

    /// <summary>
    /// Holds the directory and appends to its record over and over, so a reader
    /// in another process can be shown never to see it half-written or absent.
    /// </summary>
    /// <param name="directory">The session directory.</param>
    /// <param name="readyPath">Written once the lock is held.</param>
    /// <param name="rewrites">How many times to replace the record.</param>
    /// <param name="donePath">Written when every rewrite is finished.</param>
    /// <returns>Zero.</returns>
    public static int Rewrite(string directory, string readyPath, int rewrites, string donePath)
    {
        var path = SessionPath.For(directory);

        var result = SessionLock.TryAcquire(
            path,
            new SessionLockRequest { Browser = "chromium", Purpose = "rewrite probe" },
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

                    // The purpose is a list of timestamped statements and
                    // `lastUsed` is derived from them, so a change appends a row
                    // rather than assigning a field. The value differs on every
                    // round on purpose: a reader has to be able to tell that
                    // what it saw changed under it, and a hundred identical
                    // purposes would prove nothing.
                    held.AppendPurpose($"rewrite {round.ToString(CultureInfo.InvariantCulture)}");

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

    /// <summary>
    /// One writer in the index race: waits on a shared start gate, then
    /// re-asserts the same index entry as fast as it can.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The index takes no lock, so "one valid file" is a claim about what two
    /// processes renaming over one name do to each other</b> — and that is not
    /// something a single-threaded stub can be asked. Every writer targets the
    /// same key, so every write after the first is a rename over a file another
    /// process may be renaming over at the same instant.
    /// </para>
    /// <para>
    /// The failure to record is deliberately routed through the real
    /// <see cref="ProcessLog"/> rather than counted here: recording never
    /// throws, so a counter in this file would have to duplicate the product's
    /// own judgement of what failed. The host reads the log instead, which also
    /// proves the warning is written where somebody would find it.
    /// </para>
    /// </remarks>
    /// <param name="directory">The session directory every writer records.</param>
    /// <param name="root">The app-data root whose <c>index\</c> and <c>logs\</c> are used.</param>
    /// <param name="startEventName">A named event the host sets to release them all at once.</param>
    /// <param name="reportPath">Where to write this writer's outcome.</param>
    /// <param name="writes">How many times to re-assert the entry.</param>
    /// <returns>Zero.</returns>
    public static int Index(string directory, string root, string startEventName, string reportPath, int writes)
    {
        var path = SessionPath.For(directory);
        var paths = new LocalAppDataPaths(root);

        using var log = ProcessLog.Create(paths, LogLevel.Trace);
        var index = new SessionIndex(paths, log.Factory.CreateLogger("BrowserAI.TestProbe"));

        using (var start = EventWaitHandle.OpenExisting(startEventName))
        {
            File.WriteAllText($"{reportPath}.ready", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            _ = start.WaitOne(Patience);
        }

        var clock = Stopwatch.StartNew();

        for (var i = 0; i < writes; i++)
        {
            index.Record(path);
        }

        var elapsed = clock.Elapsed;

        Write(reportPath, new JsonObject
        {
            ["pid"] = Environment.ProcessId,
            ["writes"] = writes,
            ["indexRoot"] = index.Root,
            ["elapsedMilliseconds"] = elapsed.TotalMilliseconds,
        });

        return 0;
    }

    /// <summary>
    /// Holds a file open exactly the way Firefox holds <c>parent.lock</c>, and
    /// stays alive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Read <i>and</i> write, and no sharing at all</b> — which is what
    /// Firefox's profile lock asks for, and the reason a preflight's open is
    /// refused whatever share mode the preflight itself permits. A stub that
    /// merely opened for write with <c>FileShare.None</c> would produce the same
    /// sharing violation by a different route and would stop being evidence the
    /// moment Firefox changed.
    /// </para>
    /// <para>
    /// <b>A separate process, because that is the whole point.</b> A lock held on
    /// another thread of the test host would not be refused to the host at all,
    /// and the Restart Manager would name the host — so the attribution assertion
    /// would be about the wrong process.
    /// </para>
    /// </remarks>
    /// <param name="path">The file to hold. Created if it does not exist.</param>
    /// <param name="readyPath">Written once the handle is open.</param>
    /// <returns>Two if the host failed to kill it.</returns>
    public static int HoldFile(string path, string readyPath)
    {
        using var held = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        Write(readyPath, new JsonObject
        {
            ["pid"] = Environment.ProcessId,
            ["path"] = path,
            ["startedFileTime"] = Process.GetCurrentProcess().StartTime.ToFileTime(),
        });

        // Killed from outside. Reaching the end of this wait means the host
        // failed to do so, and the exit code says which.
        Thread.Sleep(Patience);
        return 2;
    }

    /// <summary>
    /// Takes the <b>shared</b> claim on a browsers root through the product's
    /// own code and holds it until this process is killed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It exists to prove the one property no in-process test can:</b> a
    /// reader that dies releases the claim, with nothing running to clean up
    /// after it. Windows closes the handle when the process object goes, whether
    /// the process exited, crashed or was terminated — which is exactly why this
    /// claim is a file and not a named semaphore, whose count is not restored on
    /// a holder's death.
    /// </para>
    /// <para>
    /// <b>It calls <c>MaintenanceLock.TakeShared</c> rather than opening the file
    /// itself</b>, so what a host asserts against is the product's own open with
    /// the product's own share mode. A hand-written <c>FileStream</c> here would
    /// be a test of this file.
    /// </para>
    /// </remarks>
    /// <param name="browsersRoot">The browsers root to claim.</param>
    /// <param name="readyPath">Written once the claim is held.</param>
    /// <returns>2 if this process was never killed, which is the host's failure.</returns>
    public static int HoldBrowsersClaim(string browsersRoot, string readyPath)
    {
        using var claim = BrowserAI.Runtime.MaintenanceLock.TakeShared(browsersRoot, out _, out _);

        Write(readyPath, new JsonObject
        {
            ["pid"] = Environment.ProcessId,
            ["path"] = browsersRoot,
            ["held"] = claim is not null,
            ["startedFileTime"] = Process.GetCurrentProcess().StartTime.ToFileTime(),
        });

        // Killed from outside. Reaching the end of this wait means the host
        // failed to do so, and the exit code says which.
        Thread.Sleep(Patience);
        return 2;
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
        Publish(temp, path);
    }

    /// <summary>
    /// Renames a report into place, waiting out a scanner's handle on the
    /// destination.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Added 2026-08-18.</b> A file this process has just closed is briefly
    /// held by something outside this repository, and
    /// <c>MOVEFILE_REPLACE_EXISTING</c> wants DELETE on the destination — so it
    /// is refused <c>ACCESS_DENIED</c> rather than as a sharing violation, and an
    /// unretried rename kills the probe. The host then reports <i>"the probe
    /// never wrote its report"</i>, which is true and names the wrong cause.
    /// Measured elsewhere in this suite at one occurrence in twenty full runs.
    /// The bound is <see cref="Patience"/>, the probe's own hang detector; every
    /// observed occurrence cleared on the first retry.
    /// </remarks>
    /// <param name="temp">The fully written temporary file.</param>
    /// <param name="path">Where the host is looking.</param>
    private static void Publish(string temp, string path)
    {
        var waited = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception failure) when (failure is UnauthorizedAccessException or IOException)
            {
                if (waited.Elapsed > Patience)
                {
                    throw new InvalidOperationException(
                        $"'{path}' could not be replaced by a rename within {Patience}. Something outside this repository is holding it.",
                        failure);
                }

                Thread.Sleep(10);
            }
        }
    }
}
