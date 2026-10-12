// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text.Json.Nodes;
using BrowserAI.TestProbe;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The acceptance test for zero process leakage: a real descendant tree, every
/// member proven to be in the job, the launcher hard-killed from outside, and
/// nothing left alive.
/// </summary>
/// <remarks>
/// <para>
/// <b>The kill has to come from outside, and the launcher has to be a separate
/// process.</b> What is being proven is that containment survives BrowserAI
/// dying without running any code -- no <c>finally</c>, no shutdown hook, no
/// handler. That is why the test host starts a launcher process, and why it
/// terminates that launcher instead of asking it to exit.
/// </para>
/// <para>
/// <b>What answers "is this pid in our job?"</b> is the launcher, because the
/// job is unnamed and never duplicated, so no other process can hold a handle
/// to it. The launcher writes what it found and the host asserts on the file.
/// </para>
/// <para>
/// <b>Two independent enumerations, deliberately.</b> The kernel's own member
/// list from <c>QueryInformationJobObject</c>, and a toolhelp descendant walk
/// seeded from an I/O completion port on the job. The seeding is what closes
/// the gap between them: a process whose parent has already exited is
/// re-parented and invisible to a pure parent-child walk, and would be missed
/// by a check that only walked. The two lists must agree in both directions.
/// ⚠️ <i>Added 2026-10-12 by addition, 95 a:</i> the launcher holds each member
/// from its join message and seeds the walk with those processes, never with a
/// bare pid, which
/// <see cref="APidThePortReportedThatNamesAProcessOutsideTheJobIsNeitherWalkedNorCounted"/>
/// holds.
/// </para>
/// <para>
/// <b>Nothing here matches a process by image name at any step</b>, including
/// the survivor check. Every pid was recorded when it was spawned and is
/// re-validated against its recorded creation time before anything acts on it.
/// </para>
/// </remarks>
internal sealed class JobContainmentTests
{
    private static readonly string ProbePath = Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe");

    /// <summary>
    /// How long the launcher gets to bring a tree up and report on it. Generous:
    /// a slow machine starting a runtime is the normal reason this is slow, and
    /// a tight deadline here reports as a containment failure.
    /// </summary>
    private static readonly TimeSpan ReportPatience = TestDefaults.ProcessHang;

    /// <summary>
    /// How long every member of the tree gets to be gone after the launcher is
    /// terminated. <c>KILL_ON_JOB_CLOSE</c> is a kernel operation, so this is
    /// scheduling latency, not a shutdown sequence.
    /// </summary>
    private static readonly TimeSpan TeardownPatience = TestDefaults.ProcessHang;

    /// <summary>
    /// A node child that spawns two of its own, then reports the tree. The
    /// spawn is deliberately <c>detached: false</c>-shaped -- an ordinary
    /// <c>child_process.spawn</c> -- because that is what puts the grandchildren
    /// in libuv's permissive job, which is the configuration that would leak if
    /// ours were misconfigured.
    /// </summary>
    private const string NodeTreeScript =
        "const cp=require('child_process');const fs=require('fs');" +
        "const kids=[];" +
        "for(let i=0;i<2;i++){kids.push(cp.spawn(process.execPath,['-e','setInterval(()=>{},1e9)'],{stdio:'ignore'}).pid);}" +
        "fs.writeFileSync(process.argv[1],JSON.stringify({childPid:process.pid,grandchildPids:kids}));" +
        "setInterval(()=>{},1e9);";

    [Test]
    public async Task ADescendantTreeIsContainedAndNothingSurvivesTheLauncher()
    {
        // The probe child reproduces libuv's job shape around itself before it
        // spawns anything, so this arm exercises the nested-permissive-job case
        // on a machine with no payload built.
        var report = await RunAsync("job-probe-tree", ProbePath, ["job-child", "{ready}", "3"], expectedMinimumProcesses: 4);
        var child = report["childReport"]!;

        // 5 is ERROR_ACCESS_DENIED, and this is the fact the whole guarantee
        // rests on: with no permissive job in the chain, a job that grants no
        // breakaway converts an escape attempt into a launch failure. Nothing
        // was created, so there is no pid.
        await Assert.That((int)child["breakawayFromOurJobAlone"]!["error"]!).IsEqualTo(5);
        await Assert.That((int)child["breakawayFromOurJobAlone"]!["pid"]!).IsEqualTo(0);

        // With libuv's permissive job nested inside ours the answer is
        // different, and it took a failing assertion to find out. The launch
        // SUCCEEDS -- error 0 -- because the request is granted by the inner
        // job. What it cannot do is leave ours: a breakaway walks up the
        // hierarchy and stops at the first job that does not permit it.
        //
        // So the assertion is not on the return value at all. It is that the
        // process which did break away is in the walk above, where every row
        // has already been asserted to be in our job.
        var escaper = (int)child["breakawayInsideNestedJob"]!["pid"]!;

        await Assert.That((int)child["breakawayInsideNestedJob"]!["error"]!).IsEqualTo(0);
        await Assert.That(escaper).IsNotEqualTo(0);
        await Assert.That(report["walk"]!.AsArray().Select(row => (int)row!["pid"]!)).Contains(escaper);
    }

    /// <summary>
    /// <b>A pid that vanishes between the walk and the query is EXITED, and a
    /// query that fails for any other reason is still a failure.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The race this closes, and what it cost.</b> On 2026-09-16, on a
    /// docs-only commit,
    /// <see cref="ADescendantTreeIsContainedAndNothingSurvivesTheLauncher"/> went
    /// red <i>after</i> <c>escapees == 0</c> had already passed: a row came back
    /// with a null <c>inOurJob</c> because <c>OpenProcess</c> failed for a
    /// descendant that had exited between the toolhelp walk and the per-row
    /// query. Nothing about containment was in question -- an exited process is
    /// not a survivor and cannot be an escapee -- but the rig had no way to
    /// express <i>gone</i>, so it expressed <i>unknown</i>, and the host reads
    /// unknown as failure. A re-run was green, which is the signature of a rig
    /// race and not a product defect.
    /// </para>
    /// <para>
    /// ⚠️ <b>This is a classification, not a retry and not a suppression, and
    /// the third arm is what says so.</b> Nothing re-queries and nothing waits;
    /// the verdict is taken from what Windows said the first time. An error that
    /// is not one of the two vanished-pid shapes is still
    /// <see cref="ProcessQueryVerdict.Verdict.Unreadable"/>, which is still a red
    /// row carrying its note -- so the change cannot absorb a genuine failure to
    /// read a process.
    /// </para>
    /// <para>
    /// <b>The error numbers are MEASURED here, not asserted from
    /// memory.</b> Two live controls run beside the synthetic ones: a process
    /// this arm starts and waits for is opened after it is gone, and the error
    /// that comes back is compared against the constant the classification keys
    /// on; and the same open against a pid that <i>is</i> alive succeeds, so the
    /// failing read is a real reading and not a call that can only ever
    /// fail. A table of constants with no live control would be a test of what
    /// somebody typed.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AWalkedPidThatVanishedBeforeTheQueryIsExitedAndAnyOtherFailureIsStillAFailure()
    {
        // ---- The live control: what Windows ACTUALLY says about a gone pid ---
        // cmd /c exit runs and returns; nothing holds a handle to it afterwards
        // except this Process object, which is disposed before the open.
        int gonePid;

        using (var transient = Process.Start(new ProcessStartInfo(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            "/c exit")
        {
            UseShellExecute = false,

            // Redirecting the streams does not suppress the console; this would
            // otherwise flash a window on the maintainer's screen once a run.
            CreateNoWindow = true,
        })!)
        {
            gonePid = transient.Id;
            await transient.WaitForExitAsync();
        }

        // The .NET Process object held the last handle; with it disposed the pid
        // names nothing. A hang detector, not a promptness claim: the pid is
        // released when the last handle closes, and nothing here is timed.
        var deadline = Stopwatch.StartNew();
        int goneError;

        while ((goneError = ProcessIdentity.OpenProcessErrorFor(gonePid)) is 0
            && deadline.Elapsed < TestDefaults.ProcessHang)
        {
            await Task.Delay(25);
        }

        await Assert.That(goneError)
            .IsEqualTo(ProcessQueryVerdict.InvalidParameter)
            .Because("the classification below keys on this number and it must be the one Windows actually returns");

        // And the other half of the live control: a pid that IS alive opens, so
        // the reading above is a reading and not a call that always fails.
        await Assert.That(ProcessIdentity.OpenProcessErrorFor(Environment.ProcessId))
            .IsEqualTo(0)
            .Because("a probe that can only ever fail proves nothing about the failing case");

        // ---- The classification, over that measured number and its sibling ---
        await Assert.That(ProcessQueryVerdict.ForFailedOpen(goneError))
            .IsEqualTo(ProcessQueryVerdict.Verdict.Exited);

        await Assert.That(ProcessQueryVerdict.ForFailedOpen(ProcessQueryVerdict.AccessDenied))
            .IsEqualTo(ProcessQueryVerdict.Verdict.Exited)
            .Because("PROCESS_QUERY_LIMITED_INFORMATION is granted over every same-user unprotected process, so a denial means the pid was recycled and the process we walked has gone");

        // ---- And the direction that must NOT move -----------------------------
        // Each of these is a real Win32 error that is not a vanished pid, and
        // each must stay a failure. If this arm ever goes green on all of them
        // the classification has become a suppression.
        foreach (var unrelated in new[]
        {
            6,    // ERROR_INVALID_HANDLE
            8,    // ERROR_NOT_ENOUGH_MEMORY
            50,   // ERROR_NOT_SUPPORTED
            1450, // ERROR_NO_SYSTEM_RESOURCES
        })
        {
            await Assert.That(ProcessQueryVerdict.ForFailedOpen(unrelated))
                .IsEqualTo(ProcessQueryVerdict.Verdict.Unreadable)
                .Because($"{unrelated} is not a pid that vanished and nothing here may absorb it");
        }
    }

    /// <summary>
    /// <b>A join message whose pid names a process outside the job seeds nothing</b>, and the
    /// walk neither reaches that process nor counts it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>95 a, decided 2026-10-12 by the maintainer, in his words verbatim: <i>"95 your
    /// recommendation"</i>.</b> The Chromium arm of <see cref="BrowserContainmentTests"/> went
    /// red in 4 of 77 full runs, the last time with <c>escapees</c> 2. The launcher seeded its
    /// walk with every pid the job's port had reported, as a bare number, so a helper Chromium
    /// had retired before the walk, whose pid Windows had given to a process outside the job,
    /// was seeded, walked, opened and counted, and a console process brings a <c>conhost</c>
    /// child the walk then reaches too.
    /// </para>
    /// <para>
    /// <b>The plant is that join message, posted to the port by hand</b>: the launcher's
    /// <c>job-launcher-told-of</c> mode posts one more, in the shape the job's own have, naming
    /// this test host, which is alive and outside the launcher's job. A walk seeded by bare pid
    /// counts the test host and everything it started; a launcher that opens each member at
    /// its join message and keeps it only when <c>IsProcessInJob</c> says it is the job's
    /// drops it there, and says so in the report's <c>joinsNotHeld</c>.
    /// </para>
    /// <para>
    /// <b>Watched red 2026-10-12 at <c>1c4ea1a3</c></b>, the walk as it stood, under the suite
    /// lock: <i>"Expected to be 0, because the launcher counted 43 walked process(es) outside
    /// its job"</i>, the first row the test host itself and the rest what it had started, its
    /// probes and their <c>node</c>, <c>conhost</c>, Chromium and Firefox processes, nine of
    /// them already gone and read without an image path.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APidThePortReportedThatNamesAProcessOutsideTheJobIsNeitherWalkedNorCounted()
    {
        var stranger = Environment.ProcessId;

        var report = await RunAsync(
            "job-probe-told-of",
            ProbePath,
            ["job-child", "{ready}", "3"],
            expectedMinimumProcesses: 4,
            mode: ["job-launcher-told-of", stranger.ToString(CultureInfo.InvariantCulture)]);

        var said = report.ToJsonString();

        await Assert.That(report["walk"]!.AsArray().Select(row => (int)row!["pid"]!)).DoesNotContain(stranger).Because(said);
        await Assert.That(report["joinsNotHeld"]?.AsArray().Select(entry => (int)entry!["pid"]!) ?? []).Contains(stranger).Because(said);
    }

    /// <summary>
    /// <b>A walked member that has exited by the time its row is read is <c>Exited</c></b>,
    /// counted neither in the job nor out of it, while a handle still holds it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>95 c, decided 2026-10-12 by the maintainer, in his words verbatim: <i>"95 your
    /// recommendation"</i>.</b> A Chromium helper that retires during the walk is still an
    /// object Windows answers for while the browser holds a handle to it: <c>OpenProcess</c> on
    /// its pid succeeds and <c>IsProcessInJob</c> says it is in the job, while neither the
    /// job's own list nor a toolhelp snapshot names it any more (20 of 20, measured
    /// 2026-10-12, [kb](../../kb/windows/job-objects.md)). A query that asks the job first
    /// reads that corpse as a live member, which is the red of 2026-10-11: a row whose
    /// <c>inOurJob</c> was true and whose <c>inJobProcessIdList</c> was false.
    /// </para>
    /// <para>
    /// <b>The plant ends one member between the walk and its row and holds it open</b>: the
    /// launcher's <c>job-launcher-ending-one</c> mode ends the child's first grandchild, a
    /// member of the launcher's own job whose pid and creation time it checks first, waits for
    /// its exit and keeps the handle. Its row has to read <c>Exited</c>, with no membership
    /// read off it at all.
    /// </para>
    /// <para>
    /// <b>Watched red 2026-10-12 at <c>1c4ea1a3</c></b>, under the suite lock: <i>"Expected to
    /// be equal to "Exited" ... but received "Queried""</i>. The walk as it stood opened the
    /// ended member again by its pid, the open succeeded, and <c>IsProcessInJob</c> read it as
    /// a live member of the job.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AWalkedMemberThatHadExitedByItsQueryIsExitedAndCountedNeitherWay()
    {
        var report = await RunAsync(
            "job-probe-ending-one",
            ProbePath,
            ["job-child", "{ready}", "3"],
            expectedMinimumProcesses: 4,
            mode: ["job-launcher-ending-one"]);

        var ended = report["endedMember"]!;
        var said = report.ToJsonString();

        var row = report["walk"]!.AsArray().Single(candidate =>
            (int)candidate!["pid"]! == (int)ended["pid"]! && (long)candidate["createdFileTime"]! == (long)ended["createdFileTime"]!)!;

        await Assert.That((string)row["verdict"]!).IsEqualTo(nameof(ProcessQueryVerdict.Verdict.Exited)).Because(said);
        await Assert.That((bool?)row["inOurJob"]).IsNull().Because(said);
        await Assert.That((bool?)row["inAnyJob"]).IsNull().Because(said);
    }

    [Test]
    public async Task TheBundledNodeAndItsDescendantsAreContained()
    {
        // ⚠️ The gate, not a degraded branch. Until 2026-08-16 a missing payload
        // took a fallback that asserted the tree was absent as a whole and
        // reported this test as PASSED -- so a run that never started the
        // bundled node produced the same four numbers as one that did, which is
        // the exact defect `SuiteEnvironment` exists to make impossible. It is
        // now a loud skip, and a failure under BROWSERAI_RELEASE_RUN=1; the
        // "built but incomplete" case the fallback used to catch is
        // `CapabilityState.Partial`, which fails in either mode.
        await SuiteEnvironment.RequireRepositoryPayloadAsync();

        var node = RepositoryPayload.Layout.NodeExecutable;

        _ = await RunAsync("job-node-tree", node, ["-e", NodeTreeScript, "{ready}"], expectedMinimumProcesses: 3);
    }

    /// <summary>
    /// Runs one arm of the acceptance test end to end.
    /// </summary>
    /// <param name="label">Names the scratch directory, so a leftover says which arm left it.</param>
    /// <param name="command">The executable the launcher starts inside the job.</param>
    /// <param name="arguments">Its arguments. A literal <c>{ready}</c> is replaced by the ready-file path.</param>
    /// <param name="expectedMinimumProcesses">The smallest tree that counts as having been built at all.</param>
    /// <param name="mode">
    /// The launcher's mode and the arguments before its own, <c>job-launcher</c> when
    /// <see langword="null"/>; a planted run names one of the planted modes.
    /// </param>
    /// <returns>The launcher's report.</returns>
    private static async Task<JsonObject> RunAsync(
        string label,
        string command,
        string[] arguments,
        int expectedMinimumProcesses,
        string[]? mode = null)
    {
        using var scratch = await ScratchDirectory.CreateAsync(label);

        var readyFile = Path.Combine(scratch.Path, "ready.json");
        var donePath = Path.Combine(scratch.Path, "done");
        var reportPath = Path.Combine(scratch.Path, "report.json");

        // The suite's own job, so an assertion that throws below cannot leave
        // the launcher -- or anything under it -- running.
        using var scope = new JobObjectScope();

        var launcher = await scope.LaunchAsync(
            ProbePath,
            scratch.Path,
            [
                .. mode ?? ["job-launcher"],
                scratch.Path,
                readyFile,
                // One budget: the launcher's ready-wait is this test's patience.
                // It used to be a 60 s constant inside the probe, under a host
                // advertising 90 s -- see JobProbe.Launcher's remarks.
                ReportPatience.TotalSeconds.ToString(CultureInfo.InvariantCulture),
                command,
                .. arguments.Select(argument => argument.Replace("{ready}", readyFile, StringComparison.Ordinal)),
            ]);

        // Recorded now, while the process is certainly the one we started. The
        // pair (pid, creation time) is the identity from here on.
        var launcherCreated = ProcessIdentity.CreationTimeOf(launcher.Id);

        await LauncherWait.ForDoneAsync(donePath, ReportPatience, scratch.Path, launcher.Id, launcherCreated);

        var report = (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(reportPath))!;
        var walk = report["walk"]!.AsArray();

        // 95 d: a failure names the rows it is about and keeps the report the scratch
        // directory takes with it. See ContainmentReport.
        var kept = ContainmentReport.KeptPathFor(label);

        try
        {
            // The flags, read back inside the launcher from the job that actually
            // contained this tree.
            await Assert.That((uint)report["limitFlags"]!).IsEqualTo(0x00002000u);
            await Assert.That((uint)report["uiRestrictions"]!).IsEqualTo(0u);
            await Assert.That((bool)report["handleIsInheritable"]!).IsFalse();

            // A tree that never came up would satisfy "no escapees" vacuously.
            await Assert.That(walk.Count).IsGreaterThanOrEqualTo(expectedMinimumProcesses).Because(ContainmentReport.Kept(kept));
            await Assert.That((int)report["escapees"]!).IsEqualTo(0).Because(ContainmentReport.EscapeesIn(walk, kept));

            foreach (var row in walk)
            {
                var pid = (int)row!["pid"]!;
                var verdict = (string)row["verdict"]!;
                var said = row.ToJsonString() + Environment.NewLine + ContainmentReport.Kept(kept);

                await Assert.That(pid).IsNotEqualTo(0).Because(said);

                // ⚠️ THE KERNEL'S MEMBERSHIP SNAPSHOT IS ASSERTED FOR EVERY ROW,
                // whatever the per-row query managed. This is the containment claim
                // and it does not weaken below: a process the walk reached must have
                // been in the job's own member list, taken either side of the walk.
                await Assert.That((bool)row["inJobProcessIdList"]!).IsTrue().Because(said);

                if (verdict is nameof(ProcessQueryVerdict.Verdict.Exited))
                {
                    // It was gone before the row could be queried, which is
                    // containment holding and not failing: an exited process is
                    // neither a survivor nor an escapee, and the teardown assertion
                    // below covers it in the only sense that remains. The note says
                    // what happened, so this branch is visible in the report and
                    // not silent.
                    await Assert.That((string)row["note"]!).Contains("between the walk and the query").Because(said);
                    continue;
                }

                // IsProcessInJob for every pid in the descendant tree, and the
                // kernel's own member list for the same pid. Either alone can be
                // incomplete; disagreeing is the interesting case.
                await Assert.That(verdict).IsEqualTo(nameof(ProcessQueryVerdict.Verdict.Queried)).Because(said);
                await Assert.That((bool?)row["inOurJob"] is true).IsTrue().Because(said);
                await Assert.That((string)row["note"]!).IsEmpty().Because(said);
            }

            // The cross-check in the other direction: a job member the walk never
            // reached would mean the seeding failed.
            await Assert.That(report["jobMembersTheWalkMissed"]!.AsArray().Count).IsEqualTo(0).Because(ContainmentReport.Kept(kept));

            var recorded = walk
                .Select(row => ((int)row!["pid"]!, (long)row["createdFileTime"]!))
                .ToList();

            // The event under test. TerminateProcess, not a graceful stop: the
            // launcher runs no code after this line, so nothing but the kernel
            // closing its last job handle can be what cleans up.
            ProcessIdentity.Terminate(launcher.Id, launcherCreated);

            // The launcher goes in the same list. TerminateProcess only INITIATES
            // termination and returns immediately, so a check made on the next line
            // races the kernel -- measured 2026-08-16, the tree was already gone
            // while the launcher had not finished terminating.
            recorded.Add((launcher.Id, launcherCreated));

            var survivors = await WaitForNoneAliveAsync(recorded, TeardownPatience);

            await Assert.That(string.Join(", ", survivors)).IsEmpty().Because(ContainmentReport.Kept(kept));

            return report;
        }
        catch
        {
            await ContainmentReport.KeepAsync(reportPath, kept);
            throw;
        }
    }

    /// <summary>
    /// A child inherits this launch's three pipe ends and <b>nothing else in the
    /// process</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Asserted through the consequence and not through the handle
    /// table.</b> A Windows pipe reaches EOF when its <i>last</i> write handle
    /// closes, which is not the same event as the process that created it
    /// letting go. So the test creates a pipe that belongs to nobody in
    /// particular, makes its write end inheritable exactly as
    /// <c>ChildPipes.Create</c> makes a launch's child ends inheritable, launches
    /// a child through the product's own launcher, and then drops its own copy of
    /// that write end. If the child inherited it, the read below never returns.
    /// </para>
    /// <para>
    /// <b>That is the defect, not a proxy for it.</b>
    /// <c>bInheritHandles: true</c> duplicates every inheritable handle in the
    /// process, and with several sessions opening at once the pipe ends of a
    /// launch in flight on another thread are exactly this: inheritable, and
    /// nothing to do with the child being started. The consequence was a sibling
    /// session's stdout read that never saw EOF, a graceful close that always
    /// fell through to the job kill, and this process's own stdout handle sitting
    /// inside a Chromium tree
    /// ([the adversarial review](../../docs/reviews/2026-08-18-adversarial-processes.md),
    /// finding 5).
    /// </para>
    /// <para>
    /// <b>The wait is a hang detector and nothing asserts on its length.</b> EOF
    /// on a pipe whose last writer has closed is immediate; reaching
    /// <see cref="TestDefaults.ProcessHang"/> means the handle is in the child,
    /// which is the failure this exists to catch and is precisely a hang with no
    /// error anywhere.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AChildInheritsThisLaunchsPipesAndNoOtherInheritableHandle()
    {
        using var scope = new JobObjectScope();

        // Created BEFORE the launch, so it is one of the process's inheritable
        // handles at the instant CreateProcessW runs -- which is what a
        // concurrent launch's child ends are.
        using var sibling = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);

        // cmd.exe with no arguments reads its stdin forever, and the scope holds
        // the write end, so it stays alive without a timer or a script.
        var child = await scope.LaunchAsync(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            Path.GetTempPath());

        await Assert.That(ProcessIdentity.IsAlive(child.Id, ProcessIdentity.CreationTimeOf(child.Id))).IsTrue();

        // Our copy of the write end goes. Every writer is now gone unless the
        // child has one.
        sibling.DisposeLocalCopyOfClientHandle();

        var read = await ReadOneByteOnAThreadOfItsOwnAsync(sibling).WaitAsync(TestDefaults.ProcessHang);

        await Assert.That(read).IsEqualTo(-1);
    }

    /// <summary>Reads one byte of an anonymous pipe on a thread of its own.</summary>
    /// <remarks>
    /// <b>KEPT, for the maintainer's review</b> (2026-10-11, decision 54): an anonymous pipe
    /// has no overlapped read, and this arm's subject is one, so the read is synchronous
    /// and is made on a thread of its own and not on a worker of the pool. <i>Previously
    /// <c>Task.Run(sibling.ReadByte)</c>, the same read on a pool thread.</i>
    /// </remarks>
    /// <param name="pipe">The pipe.</param>
    /// <returns>The byte, or -1 at the end of the pipe.</returns>
    private static Task<int> ReadOneByteOnAThreadOfItsOwnAsync(Stream pipe)
    {
        var read = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
#pragma warning disable RS0030 // Kept at the maintainer's 76 b: the arm's subject is an anonymous pipe, which has no overlapped read, so a thread of its own reads it.
                _ = read.TrySetResult(pipe.ReadByte());
#pragma warning restore RS0030
            }
#pragma warning disable CA1031 // The thread's boundary: the failure goes to the caller, who awaits it.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                _ = read.TrySetException(failure);
            }
        })
        {
            IsBackground = true,
            Name = "BrowserAI suite anonymous pipe read",
        };

        thread.Start();

        return read.Task;
    }

    private static async Task<List<int>> WaitForNoneAliveAsync(List<(int ProcessId, long CreatedFileTime)> recorded, TimeSpan patience)
    {
        var deadline = Stopwatch.StartNew();
        var survivors = new List<int>();

        while (true)
        {
            survivors = [.. recorded.Where(entry => ProcessIdentity.IsAlive(entry.ProcessId, entry.CreatedFileTime)).Select(entry => entry.ProcessId)];

            if (survivors.Count is 0 || deadline.Elapsed > patience)
            {
                return survivors;
            }

            await Task.Delay(100);
        }
    }

}
