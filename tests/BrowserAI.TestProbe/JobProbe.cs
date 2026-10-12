// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Interop;
using BrowserAI.Protocol;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.TestProbe;

/// <summary>
/// The three process roles the job-containment acceptance test needs, ported
/// from the <c>docs/evidence/2026-08-15-jobtest/</c> prototype that measured 16 runs, 106
/// processes, 0 escapees and 0 survivors.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the launcher is a separate process at all.</b> The property under
/// test is what happens when the process holding the job handle is killed from
/// outside, and a test host cannot be that process. So the host starts this
/// launcher, the launcher starts the real child inside a job created by
/// <b>product code</b>, and the host then terminates the launcher and asks
/// whether anything survived.
/// </para>
/// <para>
/// <b>Why the launcher does the enumeration.</b> <c>IsProcessInJob</c> needs a
/// handle to the job, and the job is unnamed and never duplicated, so the only
/// process that can answer the question is the one that created it. The report
/// it writes is the evidence; the test host reads that file and acts only on
/// the pids and creation times in it.
/// </para>
/// <para>
/// <b>No step of this file matches a process by image name.</b> The toolhelp
/// walk deliberately declares <c>szExeFile</c> as an opaque buffer and never
/// reads it -- the structure has to be the size Windows expects, but nothing
/// here can compare it to <c>chrome.exe</c> even by accident. The image path a
/// report row carries since 2026-10-12 is read off that process's own handle and
/// only printed, beside the pid and creation time that identify it.
/// </para>
/// <para>
/// ⚠️ <b>A process is who it is, never a pid alone -- 95 a and c, the maintainer's
/// words of 2026-10-12 verbatim: <i>"95 your recommendation"</i>.</b> Every member
/// the job's port reports is opened at its join message and held until the report
/// is written, the walk is seeded with those processes, and each walked process is
/// asked whether it is still there before it is asked whether it is the job's. The
/// two planted launcher modes, <c>job-launcher-told-of</c> and
/// <c>job-launcher-ending-one</c>, are how <c>JobContainmentTests</c> holds both.
/// </para>
/// </remarks>
internal static partial class JobProbe
{
    private const uint JobObjectMessageNewProcess = 6;
    private const int JobObjectAssociateCompletionPortClass = 7;
    private const nuint CompletionKey = 0x4242;
    private const uint SnapshotProcesses = 0x00000002;
    private const uint ProcessQueryLimitedInformation = 0x00001000;
    private const uint ProcessTerminate = 0x00000001;

    /// <summary>
    /// Required to ask a process handle whether it is signalled, and not implied by
    /// <see cref="ProcessQueryLimitedInformation"/>.
    /// </summary>
    private const uint Synchronize = 0x00100000;
    private const uint CreateBreakawayFromJob = 0x01000000;
    private const uint CreateNoWindow = 0x08000000;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const uint JobObjectLimitBreakawayOk = 0x00000800;
    private const uint JobObjectLimitSilentBreakawayOk = 0x00001000;
    private const int JobObjectExtendedLimitInformationClass = 9;

    // The three evidence files the launcher writes into its output directory.
    // Spelled here and nowhere else: the host does not name them, it inlines
    // every small file it finds in the tree when it reports a failure, so
    // renaming one here cannot silently drop it from the failure message.
    private const string ChildStandardOutputFile = "child-stdout.log";
    private const string ChildStandardErrorFile = "child-stderr.log";
    private const string LauncherErrorFile = "launcher-error.txt";

    private const uint WaitObject0 = 0x00000000;
    private const uint WaitTimeout = 0x00000102;

    /// <summary>
    /// Creates a job with the product's own code, starts a child in it, proves
    /// what is inside it, and then waits to be killed.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b><paramref name="readyPatience"/> comes from the host and is not a
    /// constant here, because two nested patiences is how the tighter one
    /// silently becomes the real bound.</b> This used to wait 60 s regardless,
    /// under a host that advertised 180 s for exactly the case the comment on it
    /// names -- "a cold browser on a loaded machine is the normal reason this is
    /// slow". A launcher that gives up at a third of that makes the host's number
    /// a decoration, and the failure it produces is reported by the host two
    /// minutes later, wearing the host's number. Measured 2026-08-17: a Firefox
    /// launch that failed at t=60 s was reported as a 3-minute timeout, which
    /// reads as a budget being too tight instead of a launch that never
    /// happened. One budget, named by the caller.
    /// </remarks>
    /// <param name="outputDirectory">Where the report and the done marker are written.</param>
    /// <param name="readyFile">The file the child writes once its own tree is up.</param>
    /// <param name="readyPatience">How long the child gets to report, which is the host's whole budget.</param>
    /// <param name="command">The executable to start inside the job.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <param name="plant">What a planted run adds, or <see cref="LauncherPlant.None"/>.</param>
    /// <returns>Never, on the happy path: the process parks until it is terminated.</returns>
    public static async Task<int> LauncherAsync(
        string outputDirectory,
        string readyFile,
        TimeSpan readyPatience,
        string command,
        IReadOnlyList<string> arguments,
        LauncherPlant plant)
    {
        _ = Directory.CreateDirectory(outputDirectory);

        // Product code, not a copy of it. If this line stops producing a
        // correctly configured job, this test fails instead of a comment
        // becoming untrue.
        using var job = JobObject.CreateKillOnClose();

        // Associated BEFORE the child exists, so the very first membership
        // message is captured. This is what makes a process whose parent has
        // already exited reachable: the walk below is seeded with every member the
        // job ever reported, not only with the ones still linked to the root.
        var port = AssociateCompletionPort(job);

        // ⚠️ EVERY MEMBER IS HELD FROM ITS JOIN MESSAGE, AND THE WALK IS SEEDED BY WHO IT IS
        // -- 95 a, the maintainer's words of 2026-10-12 verbatim: "95 your recommendation".
        // Until that day the drain kept bare pids and the walk seeded every one still in
        // the snapshot, so a helper Chromium had retired, whose pid Windows had given to a
        // process outside the job, could be walked, opened and counted, which fits the
        // Chromium arm's "escapees 2" of that day. The drain now opens each member as its
        // message arrives and keeps the handle while IsProcessInJob says the process is
        // this job's, until the report is written, and Windows does not reuse a pid while
        // a handle to it is open (kb, measured 2026-08-18). The drain stays on a thread of
        // its own, as before: its dequeue is a kernel wait no worker of the pool is parked in.
        using var members = new HeldMembers(job);
        var drain = new Thread(() => DrainCompletionPort(port, members)) { IsBackground = true };
        drain.Start();

        if (plant.ReportedStranger is not 0)
        {
            // The plant of 95 a: one join message more, in the shape the job's own messages
            // have, naming a live process outside the job. It is what the port says of a
            // member that exited before the walk once Windows has given its pid away.
            PostJoin(port, plant.ReportedStranger);
        }

        using var process = JobLauncher.Start(job, command, arguments, outputDirectory, ChildEnvironment.Build());

        // ⚠️ Recorded, not discarded, and that is the whole difference
        // between a diagnosable failure and a three-minute mystery. This used to
        // read the two pipes and throw the bytes away -- which kept a chatty
        // child off a full buffer and left the host, when the child never came
        // up, with nothing to read but "the launcher never wrote 'done'" naming
        // a scratch tree the test then deletes. Measured 2026-08-17: that is
        // exactly how a Firefox launch failure presented.
        Drain(process.StandardOutput, Path.Combine(outputDirectory, ChildStandardOutputFile));
        Drain(process.StandardError, Path.Combine(outputDirectory, ChildStandardErrorFile));

        // Measured on every run, not only on the failing ones: the distribution
        // is what says whether a bound is too tight or whether something is
        // broken, and it cannot be reconstructed from the runs that failed.
        var readyClock = Stopwatch.StartNew();

        try
        {
            await WaitForFileAsync(readyFile, readyPatience);
        }
        catch (TimeoutException failure)
        {
            // Written before the throw, because the throw kills this process and
            // KILL_ON_JOB_CLOSE then takes the whole tree with it -- so this is
            // the last moment at which anything can say what happened.
            await File.WriteAllTextAsync(
                Path.Combine(outputDirectory, LauncherErrorFile),
                $"{failure.Message}{Environment.NewLine}"
                + $"The child had{(process.HasExited ? string.Empty : " not")} exited"
                + (process.HasExited ? $" with code {process.TryReadExitCode()?.ToString(CultureInfo.InvariantCulture) ?? "<unreadable>"}" : string.Empty)
                + $". Job members at the timeout: {string.Join(", ", job.ProcessIds())}",
                new UTF8Encoding(false));

            throw;
        }
        finally
        {
            readyClock.Stop();
        }

        // Descendants a browser or a runtime starts asynchronously -- renderers,
        // GPU, crashpad -- appear after the child reports ready.
        await WaitUntilTheTreeStopsGrowingAsync(job, readyPatience);

        // ⚠️ Two snapshots of the job's own membership, taken either side of the
        // walk, because a live browser is not a static tree: Chromium starts and
        // retires helpers continuously, so a single list taken AFTER the walk
        // reports every process born during it as one the walk missed. Measured
        // 2026-08-16 against a real Chromium -- one phantom "missed" member,
        // reproducibly, against a node tree that had never produced any.
        //
        // The per-row check uses the union, so a process born during the walk is
        // still recognised as a member. The "did the walk miss anything"
        // direction uses the INTERSECTION -- members present both before and
        // after -- which is the only set whose absence from the walk means the
        // seeding actually failed.
        var jobBefore = job.ProcessIds();
        var held = members.Held();
        var walk = Walk(process.Id, held);
        var jobAfter = job.ProcessIds();

        var jobProcessIds = jobAfter;
        var jobProcessIdSet = jobBefore.Concat(jobAfter).ToHashSet();
        var stableJobMembers = jobBefore.Where(jobAfter.Contains).ToList();

        // The plant of 95 c: one member of the walk ended between the second membership list
        // and its row, and held open past its exit, which is what a Chromium helper that
        // retires during the walk is while its parent still holds it.
        using var ended = plant.EndsOneMember ? await EndOneMemberAsync(job, walk, readyFile, readyPatience) : null;

        var rows = new JsonArray();
        var escapees = 0;

        foreach (var entry in walk)
        {
            var reading = Query(entry, held, job);

            if (reading is { Verdict: ProcessQueryVerdict.Verdict.Queried, InOurJob: false })
            {
                escapees++;
            }

            rows.Add(new JsonObject
            {
                ["pid"] = entry.ProcessId,
                ["parentPid"] = entry.ParentProcessId,
                ["createdFileTime"] = entry.CreatedFileTime,
                ["depth"] = entry.Depth,
                ["held"] = entry.Held,
                ["imagePath"] = reading.ImagePath,
                ["inOurJob"] = reading.InOurJob,
                ["inAnyJob"] = reading.InAnyJob,
                ["inJobProcessIdList"] = jobProcessIdSet.Contains(entry.ProcessId),
                ["verdict"] = reading.Verdict.ToString(),
                ["note"] = reading.Note,
            });
        }

        var walked = walk.Select(entry => entry.ProcessId).ToHashSet();

        var report = new JsonObject
        {
            ["launcherPid"] = Environment.ProcessId,
            ["rootChildPid"] = process.Id,
            ["readyMilliseconds"] = readyClock.Elapsed.TotalMilliseconds,
            ["readyPatienceMilliseconds"] = readyPatience.TotalMilliseconds,
            ["limitFlags"] = job.LimitFlags,
            ["uiRestrictions"] = job.UiRestrictions,
            ["handleIsInheritable"] = job.HandleIsInheritable,
            ["jobProcessIds"] = new JsonArray([.. jobProcessIds.Select(id => JsonValue.Create(id))]),
            ["jobMembersTheWalkMissed"] = new JsonArray(
                [.. stableJobMembers.Where(id => !walked.Contains(id)).Select(id => JsonValue.Create(id))]),
            ["escapees"] = escapees,
            ["membersHeld"] = held.Count,
            ["joinsNotHeld"] = members.NotHeld(),
            ["walk"] = rows,
            ["endedMember"] = ended is null
                ? null
                : new JsonObject
                {
                    ["pid"] = ended.ProcessId,
                    ["createdFileTime"] = ended.CreatedFileTime,
                },
            ["childReport"] = await ReadChildReportAsync(readyFile),
        };

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "report.json"), report.ToJsonString(), new UTF8Encoding(false));

        // Held until the report is written, as 95 a has it, and no longer: the drain ends
        // with its next message or its next half second, and holds nothing more.
        members.Release();

        // The done marker is written last and is what the test waits on, so a
        // partially written report can never be read as a complete one.
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "done"), "ok", new UTF8Encoding(false));

        // Parks until the test terminates it from outside, which is the event
        // the whole acceptance test exists to observe.
        await Task.Delay(Timeout.Infinite);
        return 0;
    }

    /// <summary>
    /// Stands in for <c>node</c>: reproduces libuv's permissive job around
    /// itself, spawns grandchildren into it, and proves that a breakaway is
    /// refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The nested permissive job is the point, not decoration.</b> libuv
    /// creates a global job carrying <c>BREAKAWAY_OK</c> and
    /// <c>SILENT_BREAKAWAY_OK</c> and assigns every non-detached child to it,
    /// and Playwright spawns the browser with <c>detached: false</c> on Windows
    /// -- so this is the exact configuration that sits between BrowserAI's job
    /// and a real browser, and the exact one that would leak if the outer job
    /// were misconfigured. Reproducing it here makes the acceptance test prove
    /// that on a machine with no payload built.
    /// </para>
    /// <para>
    /// The grandchildren are started the ordinary way, with no job call of any
    /// kind, because that is the case the guarantee rests on: membership is
    /// inherited automatically, and the component that "just spawns normally"
    /// is precisely the one that cannot escape.
    /// </para>
    /// </remarks>
    /// <param name="readyFile">Where to report this process's own tree.</param>
    /// <param name="grandchildCount">How many grandchildren to start.</param>
    /// <returns>Never, on the happy path: the process parks until it is terminated.</returns>
    public static async Task<int> ChildAsync(string readyFile, int grandchildCount)
    {
        // FIRST, while the only jobs above this process are BrowserAI's and the
        // suite's -- neither of which permits breakaway. This is the plain
        // configuration the guarantee is stated for.
        var (aloneError, alonePid) = AttemptBreakaway();

#pragma warning disable CA2000 // Held for the life of the process on purpose: closing this handle would terminate the grandchildren it exists to contain.
        var libuvShaped = CreateJobObjectW(nint.Zero, null);
#pragma warning restore CA2000

        if (libuvShaped.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The probe could not create its libuv-shaped job.");
        }

        SetLimitFlags(libuvShaped, JobObjectLimitKillOnJobClose | JobObjectLimitBreakawayOk | JobObjectLimitSilentBreakawayOk);

        if (!AssignProcessToJobObject(libuvShaped, GetCurrentProcess()))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The probe could not join its own libuv-shaped job.");
        }

        var grandchildren = new JsonArray();

        for (var i = 0; i < grandchildCount; i++)
        {
            var grandchild = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(readyFile)!,
                ArgumentList = { "job-grandchild" },
            })!;

            grandchildren.Add(grandchild.Id);
        }

        // SECOND, now that a permissive job sits between this process and
        // BrowserAI's. This is the production shape: libuv's job is always in
        // the chain, because Playwright spawns the browser with
        // `detached: false` on Windows.
        var (nestedError, nestedPid) = AttemptBreakaway();

        var report = new JsonObject
        {
            ["childPid"] = Environment.ProcessId,
            ["grandchildPids"] = grandchildren,
            ["breakawayFromOurJobAlone"] = new JsonObject
            {
                ["error"] = aloneError,
                ["pid"] = alonePid,
            },
            ["breakawayInsideNestedJob"] = new JsonObject
            {
                ["error"] = nestedError,
                ["pid"] = nestedPid,
            },
        };

        await File.WriteAllTextAsync(readyFile, report.ToJsonString(), new UTF8Encoding(false));

        await Task.Delay(Timeout.Infinite);
        return 0;
    }

    /// <summary>A leaf process that does nothing but exist until it is killed.</summary>
    /// <returns>Never: the process parks until it is terminated.</returns>
    public static async Task<int> GrandchildAsync()
    {
        await Task.Delay(Timeout.Infinite);
        return 0;
    }

    /// <summary>
    /// Tries to start a process that asks to leave the job, and reports the
    /// Win32 error and the pid, not a verdict.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both outcomes are reported because both are correct, in different
    /// configurations, and the test asserts which one belongs where.</b>
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// With no permissive job in the chain, the launch <b>fails</b> with
    /// <c>ERROR_ACCESS_DENIED</c> (5). That is the fact the guarantee rests on:
    /// a job granting no breakaway converts an escape attempt into a launch
    /// failure.
    /// </item>
    /// <item>
    /// With a permissive job nested inside ours -- libuv's, which is always
    /// there in production -- the launch <b>succeeds</b> and the new process
    /// stops at the first job that does not permit breakaway, which is ours. It
    /// is contained, not escaped. A verdict of "0 means we failed" would be
    /// wrong here, which is why this returns the pid too: what matters is where
    /// the process ended up, not whether the call returned.
    /// </item>
    /// </list>
    /// </remarks>
    private static (int Error, int ProcessId) AttemptBreakaway()
    {
        var commandLine = new char[Environment.ProcessPath!.Length + " job-grandchild".Length + 3];
        var text = $"\"{Environment.ProcessPath}\" job-grandchild";
        text.CopyTo(0, commandLine, 0, text.Length);

        var startupInfo = default(StartupInfo);
        startupInfo.Cb = Unsafe.SizeOf<StartupInfo>();

        if (!CreateProcessW(
                Environment.ProcessPath,
                ref commandLine[0],
                nint.Zero,
                nint.Zero,
                bInheritHandles: false,
                CreateBreakawayFromJob | CreateNoWindow,
                nint.Zero,
                null,
                ref startupInfo,
                out var information))
        {
            return (Marshal.GetLastPInvokeError(), 0);
        }

        // Left running on purpose: the walk has to reach it, so that where it
        // ended up is measured, not assumed.
        _ = CloseHandle(information.Thread);
        _ = CloseHandle(information.Process);
        return (0, (int)information.ProcessId);
    }

    private static async Task<JsonNode?> ReadChildReportAsync(string readyFile)
    {
        try
        {
            return JsonNode.Parse(await File.ReadAllTextAsync(readyFile));
        }
#pragma warning disable CA1031 // A missing or malformed child report is reported as absent; the walk is the real evidence.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>
    /// Drains one of the child's pipes into a file beside the report.
    /// </summary>
    /// <remarks>
    /// The draining is what keeps a chatty child off a full pipe buffer; the
    /// file is what makes a launch that never came up readable afterwards. Both
    /// matter, and the version that only drained cost a three-minute failure
    /// with no evidence in it.
    /// </remarks>
    /// <param name="stream">The pipe.</param>
    /// <param name="path">Where to record what came out of it.</param>
    private static void Drain(Stream stream, string path) =>
        _ = Task.Run(async () =>
        {
            var buffer = new byte[4096];

            await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, bufferSize: 0, FileOptions.Asynchronous);

            int read;

            while ((read = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);

                // Flushed per chunk on purpose: this process is killed from
                // outside at the end of every successful run, and buffered
                // evidence is evidence that is never written.
                await file.FlushAsync().ConfigureAwait(false);
            }
        });

    /// <summary>
    /// Waits until the job's membership has stopped changing, not for a fixed
    /// number of milliseconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-18 (previously <c>Thread.Sleep(1500)</c>, with the
    /// note "descendants a browser or a runtime starts asynchronously appear
    /// after the child reports ready").</b> That is true and 1,500 ms was a guess
    /// at how long they take. A guess in this position does not fail loudly -- it
    /// fails <i>quietly</i>, by walking a tree that is still coming up, which
    /// makes "every process is in our job" a claim about fewer processes than
    /// existed. On a machine running the whole suite at once, the guess is wrong
    /// in exactly that direction.
    /// </para>
    /// <para>
    /// <b>The event is quiescence.</b> The kernel's own membership list is read
    /// until it has been identical ten times running, which is the observable
    /// form of "the tree has finished coming up" and costs whatever it needs to
    /// on a slow machine instead of a fixed 1.5 s. Anything still appearing
    /// resets the count, so a tree that takes ten seconds to finish is waited out
    /// and one that is already complete is not.
    /// </para>
    /// <para>
    /// <b>It never fails, and it asserts no floor.</b> The patience is the host's,
    /// and running it out is not an error here -- the host's own assertions
    /// (<c>walk.Count &gt;= 4</c> among them) are what decide, and they have the
    /// evidence. A wait that threw, or that insisted on a member count before
    /// returning, would replace those assertions with this one and could hang a
    /// suite for the whole of a browser-shaped patience.
    /// </para>
    /// </remarks>
    /// <param name="job">The job whose membership is being watched.</param>
    /// <param name="patience">The host's budget, shared and not re-invented.</param>
    private static async Task WaitUntilTheTreeStopsGrowingAsync(JobObject job, TimeSpan patience)
    {
        // Ten readings 100 ms apart: one second of stillness, which is the
        // criterion, not a duration to spend.
        const int StillReadings = 10;

        var deadline = Stopwatch.StartNew();
        var previous = new HashSet<int>();
        var still = 0;

        while (deadline.Elapsed < patience)
        {
            var members = job.ProcessIds().ToHashSet();

            still = members.SetEquals(previous) ? still + 1 : 0;
            previous = members;

            if (still >= StillReadings)
            {
                return;
            }

            await Task.Delay(100);
        }
    }

    /// <summary>
    /// Ends the child's first grandchild and waits for its exit, and keeps it open: the plant
    /// of 95 c, a walked member that has gone by the time its row is read while a handle still
    /// holds it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It acts on one process, a member of this launcher's own job whose identity it reads
    /// twice.</b> The pid comes from the child's report and the creation time from the walk,
    /// and the handle it ends the process through is refused unless it names that same process
    /// and <c>IsProcessInJob</c> says the process is in the job this launcher created.
    /// </para>
    /// <para>
    /// <b>The handle stays open after the exit</b>, as a Chromium helper's does while the browser
    /// holds it, so the member is still an object Windows answers for at its query:
    /// <c>OpenProcess</c> on its pid succeeds, <c>IsProcessInJob</c> says it is in the job, and
    /// neither the job's own list nor a toolhelp snapshot names it (20 of 20, measured
    /// 2026-10-12, kb).
    /// </para>
    /// </remarks>
    /// <param name="job">The launcher's job.</param>
    /// <param name="walk">The walk the member must be part of.</param>
    /// <param name="readyFile">The child's report, which names its grandchildren.</param>
    /// <param name="patience">How long the exit is waited for: the host's budget.</param>
    /// <returns>The ended member, still open.</returns>
    private static async Task<EndedMember> EndOneMemberAsync(JobObject job, List<ProcessEntry> walk, string readyFile, TimeSpan patience)
    {
        var childReport = await ReadChildReportAsync(readyFile);
        var named = childReport?["grandchildPids"]?.AsArray().FirstOrDefault()
            ?? throw new InvalidOperationException($"The child's report at '{readyFile}' names no grandchild, so the plant has nothing to end.");
        var processId = (int)named;
        var victim = walk.Find(entry => entry.ProcessId == processId);

        if (victim.ProcessId != processId)
        {
            throw new InvalidOperationException($"The walk did not reach the child's first grandchild, pid {processId.ToString(CultureInfo.InvariantCulture)}, so the plant has nothing to end.");
        }

        var handle = OpenProcessToEnd(ProcessTerminate | Synchronize | ProcessQueryLimitedInformation, bInheritHandle: false, (uint)processId);

        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error, $"Could not open the child's first grandchild, pid {processId.ToString(CultureInfo.InvariantCulture)}, to end it.");
        }

        var ended = new EndedMember(processId, victim.CreatedFileTime, handle);

        try
        {
            if (!GetProcessTimes(handle, out var created, out _, out _, out _) || created != victim.CreatedFileTime)
            {
                throw new InvalidOperationException($"Pid {processId.ToString(CultureInfo.InvariantCulture)} no longer names the grandchild the walk listed, so the plant ends nothing.");
            }

            if (!IsProcessInJob(handle, job.Handle, out var member) || !member)
            {
                throw new InvalidOperationException($"Pid {processId.ToString(CultureInfo.InvariantCulture)} is not in this launcher's job, so the plant ends nothing.");
            }

            if (!TerminateProcess(handle, 1))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not end the child's first grandchild, pid {processId.ToString(CultureInfo.InvariantCulture)}.");
            }

            return await HandleWaits.WhenSignaledAsync(ended, patience)
                ? ended
                : throw new TimeoutException($"The child's first grandchild, pid {processId.ToString(CultureInfo.InvariantCulture)}, was still running {patience.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)} s after it was ended.");
        }
        catch
        {
            ended.Dispose();
            throw;
        }
    }

    private static async Task WaitForFileAsync(string path, TimeSpan patience)
    {
        var deadline = Stopwatch.StartNew();

        while (deadline.Elapsed < patience)
        {
            if (File.Exists(path))
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"The child never wrote '{path}'.");
    }

    private static nint AssociateCompletionPort(JobObject job)
    {
        var port = CreateIoCompletionPort(-1, nint.Zero, nuint.Zero, 1);

        if (port == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not create the completion port for the job.");
        }

        var association = new JobObjectAssociateCompletionPort
        {
            CompletionKey = CompletionKey,
            CompletionPort = port,
        };

        return SetInformationJobObject(
            job.Handle,
            JobObjectAssociateCompletionPortClass,
            ref association,
            (uint)Unsafe.SizeOf<JobObjectAssociateCompletionPort>())
            ? port
            : throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not associate a completion port with the job.");
    }

    /// <summary>Posts one join message to the job's port, in the shape the job's own have.</summary>
    /// <remarks>
    /// A job's message arrives as its number where a transfer's byte count would be, the key
    /// the port was associated with, and the pid where an <c>OVERLAPPED</c> pointer would be,
    /// and one posted with <c>PostQueuedCompletionStatus</c> is dequeued with the same three
    /// values: 20 of 20, measured 2026-10-12 (kb).
    /// </remarks>
    /// <param name="port">The job's completion port.</param>
    /// <param name="processId">The pid the message names.</param>
    private static void PostJoin(nint port, int processId)
    {
        if (!PostQueuedCompletionStatus(port, JobObjectMessageNewProcess, CompletionKey, processId))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not post the planted join message to the job's port.");
        }
    }

    private static void DrainCompletionPort(nint port, HeldMembers members)
    {
        while (!members.Released)
        {
            if (GetQueuedCompletionStatus(port, out var message, out _, out var overlapped, 500) && message == JobObjectMessageNewProcess)
            {
                // The "overlapped" pointer is the pid for job messages.
                members.Joined((int)overlapped);
            }
        }
    }

    private static void SetLimitFlags(SafeJobHandle job, uint limitFlags)
    {
        var information = default(JobObjectExtendedLimitInformation);
        information.BasicLimitInformation.LimitFlags = limitFlags;

        if (!SetInformationJobObject(
                job,
                JobObjectExtendedLimitInformationClass,
                ref information,
                (uint)Unsafe.SizeOf<JobObjectExtendedLimitInformation>()))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The probe could not configure its libuv-shaped job.");
        }
    }

    /// <summary>
    /// A toolhelp descendant walk, seeded with the root child <b>and</b> every
    /// member the job reported that the launcher holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The seeding is what makes this a cross-check instead of a second
    /// opinion from the same source: a process whose parent has already exited
    /// is re-parented and would be invisible to a pure parent-child walk, so it
    /// is reached from the completion port instead. A child that predates its
    /// parent is skipped, because a pid the kernel has reused is not a
    /// descendant.
    /// </para>
    /// <para>
    /// ⚠️ <b>A seed is a process, never a number -- 95 a, 2026-10-12.</b> Each held member's
    /// pid is pinned by the launcher's handle to it, and so is the root child's, by the
    /// handle <c>JobLauncher</c> answered, so the snapshot's entry under that pid is that
    /// process and no other. A held member that has exited is in no snapshot (20 of 20,
    /// measured 2026-10-12, kb), and seeds nothing.
    /// </para>
    /// </remarks>
    /// <param name="rootProcessId">The child the launcher started, whose handle it holds.</param>
    /// <param name="held">The members held when the walk began.</param>
    /// <returns>The walk, shallowest first.</returns>
    private static List<ProcessEntry> Walk(int rootProcessId, Dictionary<int, HeldMember> held)
    {
        var all = Snapshot(held);
        var byProcessId = all.ToDictionary(entry => entry.ProcessId);
        var children = all.GroupBy(entry => entry.ParentProcessId).ToDictionary(group => group.Key, group => group.ToList());

        var seeds = new List<int> { rootProcessId };
        seeds.AddRange(held.Keys.Where(id => id != rootProcessId));

        var result = new List<ProcessEntry>();
        var seen = new HashSet<int>();
        var pending = new Stack<ProcessEntry>();

        foreach (var seed in seeds.Where(byProcessId.ContainsKey))
        {
            pending.Push(byProcessId[seed] with { Depth = 0 });
        }

        while (pending.Count > 0)
        {
            var entry = pending.Pop();

            if (!seen.Add(entry.ProcessId))
            {
                continue;
            }

            result.Add(entry);

            if (!children.TryGetValue(entry.ProcessId, out var descendants))
            {
                continue;
            }

            foreach (var descendant in descendants.Where(candidate => candidate.ProcessId != entry.ProcessId))
            {
                if (descendant.CreatedFileTime is not 0 && entry.CreatedFileTime is not 0 && descendant.CreatedFileTime < entry.CreatedFileTime)
                {
                    // A pid the kernel reused: it cannot be a child of a
                    // process that started after it.
                    continue;
                }

                pending.Push(descendant with { Depth = entry.Depth + 1 });
            }
        }

        return [.. result.OrderBy(entry => entry.Depth).ThenBy(entry => entry.ProcessId)];
    }

    /// <summary>
    /// Every process on the machine, each with its parent and its creation time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A held member's creation time is the one read off the launcher's own handle</b>, so
    /// its entry costs no second open. Any other entry's is read by opening its pid after the
    /// snapshot, and that read can land on another process if the listed one has exited and
    /// Windows has given its pid away in between.
    /// </para>
    /// <para>
    /// ⚠️ <b>So a creation time later than the snapshot is no creation time -- 95 a,
    /// 2026-10-12.</b> Every process the snapshot lists was created before the moment read
    /// once it exists, so a later one names a process the snapshot never saw. The entry keeps
    /// 0, the value that already meant unreadable, and its query reads the process as gone.
    /// </para>
    /// </remarks>
    /// <param name="held">The members the launcher holds.</param>
    /// <returns>The entries, in the snapshot's order.</returns>
    private static List<ProcessEntry> Snapshot(Dictionary<int, HeldMember> held)
    {
        var entries = new List<ProcessEntry>();

        using var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);

        if (snapshot.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not take a process snapshot.");
        }

        // Read once the snapshot exists, and compared with every creation time read after it.
        var taken = DateTime.UtcNow.ToFileTimeUtc();

        var entry = default(ProcessEntry32);
        entry.Size = (uint)Unsafe.SizeOf<ProcessEntry32>();

        if (!Process32FirstW(snapshot, ref entry))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not read the first process in the snapshot.");
        }

        do
        {
            var processId = (int)entry.ProcessId;
            var created = held.TryGetValue(processId, out var member) ? member.CreatedFileTime : CreationTimeOf(processId);

            entries.Add(new ProcessEntry(processId, (int)entry.ParentProcessId, created > taken ? 0 : created, 0, member is not null));
        }
        while (Process32NextW(snapshot, ref entry));

        return entries;
    }

    private static long CreationTimeOf(int processId)
    {
        using var handle = OpenProcess(ProcessQueryLimitedInformation, bInheritHandle: false, (uint)processId);

        return !handle.IsInvalid && GetProcessTimes(handle, out var creation, out _, out _, out _) ? creation : 0;
    }

    /// <summary>
    /// Asks Windows about one walked process, by who it is: whether it is still there first,
    /// and only then whether it is the job's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>WHETHER IT IS STILL THERE IS ASKED FIRST -- 95 c, the maintainer's words of
    /// 2026-10-12 verbatim: "95 your recommendation".</b> A member that has exited is still an
    /// object Windows answers for while anything holds a handle to it: <c>OpenProcess</c> on
    /// its pid succeeds and <c>IsProcessInJob</c> says it is in the job, while the job's own
    /// list leaves it out (20 of 20, measured 2026-10-12, kb). Asked the other way round, a
    /// Chromium helper that retired during the walk while the browser held it read as a live
    /// member the job's list did not have, which is the red of 2026-10-11. A row found exited
    /// is <see cref="ProcessQueryVerdict.Verdict.Exited"/>, counted neither in the job nor
    /// out of it.
    /// </para>
    /// <para>
    /// <b>A process the launcher does not hold is asked about only once its creation time
    /// matches the walk's</b>, so a pid Windows has given to another process since reads as the
    /// walked process having exited, and never as a process outside the job.
    /// </para>
    /// </remarks>
    /// <param name="entry">The walked process.</param>
    /// <param name="held">The members held when the walk began.</param>
    /// <param name="job">The launcher's job.</param>
    /// <returns>What was read.</returns>
    private static Reading Query(ProcessEntry entry, Dictionary<int, HeldMember> held, JobObject job)
    {
        if (held.TryGetValue(entry.ProcessId, out var member))
        {
            return Read(member.Handle, job);
        }

        using var handle = OpenProcess(Synchronize | ProcessQueryLimitedInformation, bInheritHandle: false, (uint)entry.ProcessId);

        if (handle.IsInvalid)
        {
            // ⚠️ A PID THE WALK SAW AND THE QUERY CANNOT OPEN IS NOT ONE
            // OUTCOME, and spelling it as one read a probe race as a
            // containment failure on 2026-09-16. The tree here is live:
            // helpers retire while the walk is still running, so by the time
            // the row is built a descendant may simply be gone -- which is
            // containment holding and not failing, since an exited
            // process is neither a survivor nor an escapee.
            //
            // ProcessQueryVerdict decides which it was, from what Windows
            // said, once. It is NOT a retry and NOT a suppression: any error
            // that is not one of the two vanished-pid shapes stays
            // Unreadable, keeps its note and still reddens the run. See
            // JobContainmentTests.AWalkedPidThatVanishedBeforeTheQueryIsExitedAndAnyOtherFailureIsStillAFailure,
            // which plants all three directions with live controls on the
            // error numbers.
            var lastError = Marshal.GetLastPInvokeError();
            var note = $"OpenProcess failed with {lastError.ToString(CultureInfo.InvariantCulture)}";

            return ProcessQueryVerdict.ForFailedOpen(lastError) is ProcessQueryVerdict.Verdict.Exited
                ? Reading.Exited(note + ", so this process had exited between the walk and the query")
                : Reading.Unreadable(note);
        }

        if (!GetProcessTimes(handle, out var created, out _, out _, out _))
        {
            return Reading.Unreadable($"GetProcessTimes failed with {Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture)}");
        }

        return created == entry.CreatedFileTime
            ? Read(handle, job)
            : Reading.Exited(
                $"its pid now names a process created at {created.ToString(CultureInfo.InvariantCulture)}, and not the one the walk listed, so that one had exited between the walk and the query");
    }

    /// <summary>
    /// Reads one process through a handle known to name it: gone, or the job's or not.
    /// </summary>
    /// <remarks>
    /// The zero timeout is the whole wait: <c>WaitForSingleObject</c> answers whether the
    /// handle is signalled and returns, and parks nothing, as <c>LaunchedProcess.HasExited</c>
    /// does in the product.
    /// </remarks>
    /// <param name="handle">An open handle to the process, with <c>SYNCHRONIZE</c>.</param>
    /// <param name="job">The launcher's job.</param>
    /// <returns>What was read.</returns>
    private static Reading Read(SafeProcessHandle handle, JobObject job)
    {
        var answer = WaitForSingleObject(handle, 0);

        return answer switch
        {
            WaitObject0 => Reading.Exited("it had exited between the walk and the query"),
            WaitTimeout => new Reading(ProcessQueryVerdict.Verdict.Queried, job.Contains(handle), JobObject.IsInAnyJob(handle), ImagePathOf(handle), string.Empty),
            _ => Reading.Unreadable(
                $"WaitForSingleObject answered 0x{answer.ToString("X8", CultureInfo.InvariantCulture)} with error {Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture)}"),
        };
    }

    /// <summary>What the query of one walked process established.</summary>
    /// <param name="Verdict">Whether the process was read, had gone, or could not be read.</param>
    /// <param name="InOurJob">What <c>IsProcessInJob</c> said about the launcher's job, when it was asked.</param>
    /// <param name="InAnyJob">What it said about any job, when it was asked.</param>
    /// <param name="ImagePath">The image path read off the handle, printed and never compared.</param>
    /// <param name="Note">Why a row is not a plain read, or empty.</param>
    private readonly record struct Reading(ProcessQueryVerdict.Verdict Verdict, bool? InOurJob, bool? InAnyJob, string? ImagePath, string Note)
    {
        /// <summary>A process that had gone by its query: counted neither in the job nor out of it.</summary>
        /// <param name="note">What showed it had gone.</param>
        /// <returns>The reading.</returns>
        public static Reading Exited(string note) => new(ProcessQueryVerdict.Verdict.Exited, null, null, null, note);

        /// <summary>A process nothing could be read about, which reddens the run.</summary>
        /// <param name="note">What failed.</param>
        /// <returns>The reading.</returns>
        public static Reading Unreadable(string note) => new(ProcessQueryVerdict.Verdict.Unreadable, null, null, null, note);
    }

    /// <summary>
    /// Every member the job's port reported that is the job's own, each held open from its
    /// join message until the report is written, and every join message that held nothing,
    /// with the reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Opened on the drain's thread the moment its message is dequeued</b>, with
    /// <c>SYNCHRONIZE</c> and <c>PROCESS_QUERY_LIMITED_INFORMATION</c>. A member that has
    /// already exited still opens while anything holds it, and <c>IsProcessInJob</c> still says
    /// it is the job's (20 of 20, measured 2026-10-12, kb), so it is held for what it is.
    /// </para>
    /// <para>
    /// <b>One window stays open, and the membership check is what makes it harmless.</b> Between
    /// a member's creation and this open the member can exit, nothing may hold it, and Windows
    /// can give its pid to another process. That process is outside the job,
    /// <c>IsProcessInJob</c> says so, and the message holds nothing. A member that exits with
    /// nobody holding it fails the open, and is no seed, which costs the walk nothing: it is
    /// gone.
    /// </para>
    /// </remarks>
    /// <param name="job">The job whose members these are.</param>
    private sealed class HeldMembers(JobObject job) : IDisposable
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<int, HeldMember> _held = [];
        private readonly List<(int ProcessId, string Reason)> _notHeld = [];
        private bool _released;

        /// <summary>Whether the report is written and every handle let go.</summary>
        public bool Released
        {
            get
            {
                lock (_gate)
                {
                    return _released;
                }
            }
        }

        /// <summary>Holds the process a join message names, while it is this job's.</summary>
        /// <param name="processId">The pid the message names.</param>
        public void Joined(int processId)
        {
            var handle = OpenProcess(Synchronize | ProcessQueryLimitedInformation, bInheritHandle: false, (uint)processId);
            string? reason;

            try
            {
                if (handle.IsInvalid)
                {
                    reason = $"OpenProcess failed with {Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture)}, so the member had exited and nothing held it";
                }
                else
                {
                    reason = ReasonNotToHold(handle, out var created);

                    if (reason is null)
                    {
                        lock (_gate)
                        {
                            // A message that arrives after the report holds nothing, and a second
                            // message for a member already held needs no second handle. Another
                            // process under a pid still held is what the held handle prevents, and
                            // the report says so if it ever happens.
                            if (!_released)
                            {
                                if (!_held.TryGetValue(processId, out var already))
                                {
                                    _held[processId] = new HeldMember(processId, created, handle);
                                    handle = null;
                                }
                                else if (already.CreatedFileTime != created)
                                {
                                    reason = $"a second process under a pid already held, created at {created.ToString(CultureInfo.InvariantCulture)}, which a held handle is measured to prevent";
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                handle?.Dispose();
            }

            if (reason is not null)
            {
                Refuse(processId, reason);
            }
        }

        /// <summary>The members held now, for one walk.</summary>
        /// <returns>A copy keyed by pid; the handles stay this object's.</returns>
        public Dictionary<int, HeldMember> Held()
        {
            lock (_gate)
            {
                return new Dictionary<int, HeldMember>(_held);
            }
        }

        /// <summary>Every join message that held nothing, for the report.</summary>
        /// <returns>One object per message: the pid and the reason.</returns>
        public JsonArray NotHeld()
        {
            lock (_gate)
            {
                return [.. _notHeld.Select(entry => new JsonObject { ["pid"] = entry.ProcessId, ["reason"] = entry.Reason })];
            }
        }

        /// <summary>Lets every handle go: the report is written.</summary>
        public void Release()
        {
            lock (_gate)
            {
                _released = true;

                foreach (var member in _held.Values)
                {
                    member.Dispose();
                }

                _held.Clear();
            }
        }

        /// <inheritdoc />
        public void Dispose() => Release();

        private string? ReasonNotToHold(SafeProcessHandle handle, out long created)
        {
            created = 0;

            try
            {
                if (!job.Contains(handle))
                {
                    return "its pid names a process outside the job, so the member had exited and Windows had given the pid away before it was opened";
                }
            }
            catch (Win32Exception failure)
            {
                return $"IsProcessInJob failed with {failure.NativeErrorCode.ToString(CultureInfo.InvariantCulture)}";
            }

            return GetProcessTimes(handle, out created, out _, out _, out _)
                ? null
                : $"GetProcessTimes failed with {Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture)}";
        }

        private void Refuse(int processId, string reason)
        {
            lock (_gate)
            {
                _notHeld.Add((processId, reason));
            }
        }
    }

    /// <summary>One member held open from its join message: who it is, and the handle.</summary>
    /// <param name="processId">Its pid, which the handle pins.</param>
    /// <param name="createdFileTime">Its creation time, read off the handle.</param>
    /// <param name="handle">The handle, with <c>SYNCHRONIZE</c> and <c>PROCESS_QUERY_LIMITED_INFORMATION</c>.</param>
    private sealed class HeldMember(int processId, long createdFileTime, SafeProcessHandle handle) : IDisposable
    {
        /// <summary>Its pid.</summary>
        public int ProcessId { get; } = processId;

        /// <summary>Its creation time.</summary>
        public long CreatedFileTime { get; } = createdFileTime;

        /// <summary>The handle that pins its pid.</summary>
        public SafeProcessHandle Handle { get; } = handle;

        /// <inheritdoc />
        public void Dispose() => Handle.Dispose();
    }

    /// <summary>
    /// A process's full image path, read off a handle to it, for a report a person reads.
    /// </summary>
    /// <remarks>
    /// Never compared with anything: the rule is never to select a process by its image, and
    /// this is printed beside the pid and creation time that identify the process. Windows
    /// refuses the read for a process that has exited, with error 31 (measured 2026-10-12, kb),
    /// so an exited row has none.
    /// </remarks>
    /// <param name="handle">An open handle to the process.</param>
    /// <returns>The path, or <see langword="null"/> when Windows would not give it.</returns>
    private static string? ImagePathOf(SafeProcessHandle handle)
    {
        // Sized for the extended limit, as the product's own read is.
        var buffer = new char[32768];
        var length = (uint)buffer.Length;

        return QueryFullProcessImageNameW(handle, 0, ref buffer[0], ref length)
            ? new string(buffer, 0, (int)length)
            : null;
    }

    /// <summary>One process in the snapshot: identity only, never a name.</summary>
    /// <param name="ProcessId">Its pid.</param>
    /// <param name="ParentProcessId">The pid of the process that created it.</param>
    /// <param name="CreatedFileTime">Its creation time, or 0 when it could not be read as the snapshot's.</param>
    /// <param name="Depth">How far below a seed the walk found it.</param>
    /// <param name="Held">Whether the launcher has held it since its join message.</param>
    private readonly record struct ProcessEntry(int ProcessId, int ParentProcessId, long CreatedFileTime, int Depth, bool Held);

    /// <summary>
    /// The member the plant of 95 c ended, held open past its exit until the launcher ends.
    /// </summary>
    private sealed class EndedMember : WaitHandle
    {
        /// <summary>Wraps the open handle the member was ended through.</summary>
        /// <param name="processId">The member's pid.</param>
        /// <param name="createdFileTime">Its creation time, read off the walk.</param>
        /// <param name="handle">The open handle, which this owns from here on.</param>
        public EndedMember(int processId, long createdFileTime, SafeWaitHandle handle)
        {
            ProcessId = processId;
            CreatedFileTime = createdFileTime;
            SafeWaitHandle = handle;
        }

        /// <summary>The member's pid.</summary>
        public int ProcessId { get; }

        /// <summary>The member's creation time.</summary>
        public long CreatedFileTime { get; }
    }

    /// <summary>
    /// The 260-character image-name field, declared so the structure is the
    /// size Windows expects and never read. <b>That is deliberate:</b> the rule
    /// is never to match a process by image name, and a field that is never
    /// projected into a string cannot be compared to one.
    /// </summary>
    /// <remarks>
    /// The element type is <see cref="ushort"/> and not <see cref="char"/>
    /// because <c>char</c> is not blittable under runtime marshalling -- the
    /// generator refuses it with SYSLIB1051 -- and because UTF-16 code units
    /// nobody decodes is exactly what this field is.
    /// </remarks>
    [InlineArray(260)]
    private struct ImageNameWeDoNotRead
    {
        private ushort _element0;
    }

#pragma warning disable CS0649 // Filled in by the kernel; several fields exist only to make the struct the size Windows expects.
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nuint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        public ImageNameWeDoNotRead ImageName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectAssociateCompletionPort
    {
        public nuint CompletionKey;
        public nint CompletionPort;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Cb;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2Length;
        public nint Reserved2;
        public nint StdInput;
        public nint StdOutput;
        public nint StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public uint ProcessId;
        public uint ThreadId;
    }
#pragma warning restore CS0649

    // System32 only, on every P/Invoke in this repository (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateIoCompletionPort(nint fileHandle, nint existingCompletionPort, nuint completionKey, uint numberOfConcurrentThreads);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetQueuedCompletionStatus(
        nint completionPort,
        out uint lpNumberOfBytesTransferred,
        out nuint lpCompletionKey,
        out nint lpOverlapped,
        uint dwMilliseconds);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostQueuedCompletionStatus(
        nint completionPort,
        uint dwNumberOfBytesTransferred,
        nuint dwCompletionKey,
        nint lpOverlapped);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(
        SafeJobHandle hJob,
        int jobObjectInformationClass,
        ref JobObjectAssociateCompletionPort lpJobObjectInformation,
        uint cbJobObjectInformationLength);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, EntryPoint = "SetInformationJobObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(
        SafeJobHandle hJob,
        int jobObjectInformationClass,
        ref JobObjectExtendedLimitInformation lpJobObjectInformation,
        uint cbJobObjectInformationLength);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeJobHandle CreateJobObjectW(nint lpJobAttributes, string? lpName);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(SafeJobHandle hJob, nint hProcess);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeSnapshotHandle CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32FirstW(SafeSnapshotHandle hSnapshot, ref ProcessEntry32 lppe);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32NextW(SafeSnapshotHandle hSnapshot, ref ProcessEntry32 lppe);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(
        SafeProcessHandle hProcess,
        out long lpCreationTime,
        out long lpExitTime,
        out long lpKernelTime,
        out long lpUserTime);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(SafeProcessHandle hHandle, uint dwMilliseconds);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageNameW(
        SafeProcessHandle hProcess,
        uint dwFlags,
        ref char lpExeName,
        ref uint lpdwSize);

    /// <summary>
    /// <c>OpenProcess</c>, answering a handle a <see cref="WaitHandle"/> can own: the one process
    /// the plant of 95 c ends, waited for through the product's <c>HandleWaits</c>.
    /// </summary>
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
    private static partial SafeWaitHandle OpenProcessToEnd(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(
        SafeWaitHandle hProcess,
        out long lpCreationTime,
        out long lpExitTime,
        out long lpKernelTime,
        out long lpUserTime);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsProcessInJob(
        SafeWaitHandle processHandle,
        SafeJobHandle jobHandle,
        [MarshalAs(UnmanagedType.Bool)] out bool result);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(SafeWaitHandle hProcess, uint uExitCode);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessW(
        string? lpApplicationName,
        ref char lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        nint lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfo lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);
}

/// <summary>What a planted run of the launcher adds to its ordinary run.</summary>
/// <param name="ReportedStranger">
/// A pid posted to the job's port as one join message more before the child starts, or 0 for
/// none: the plant of 95 a.
/// </param>
/// <param name="EndsOneMember">
/// Whether the launcher ends the child's first grandchild between the walk and its rows and
/// holds it open past its exit: the plant of 95 c.
/// </param>
internal readonly record struct LauncherPlant(int ReportedStranger, bool EndsOneMember)
{
    /// <summary>The ordinary run, with nothing planted.</summary>
    public static LauncherPlant None => default;
}

/// <summary>A toolhelp snapshot handle that closes itself.</summary>
internal sealed partial class SafeSnapshotHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>Creates an invalid handle for the marshaller to fill in.</summary>
    public SafeSnapshotHandle()
        : base(ownsHandle: true)
    {
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle() => CloseHandle(handle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);
}
