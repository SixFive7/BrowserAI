// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text.Json.Nodes;
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
/// dying without running any code — no <c>finally</c>, no shutdown hook, no
/// handler. That is why the test host starts a launcher process, and why it
/// terminates that launcher rather than asking it to exit.
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
    private static readonly TimeSpan ReportPatience = TimeSpan.FromSeconds(90);

    /// <summary>
    /// How long every member of the tree gets to be gone after the launcher is
    /// terminated. <c>KILL_ON_JOB_CLOSE</c> is a kernel operation, so this is
    /// scheduling latency rather than a shutdown sequence.
    /// </summary>
    private static readonly TimeSpan TeardownPatience = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A node child that spawns two of its own, then reports the tree. The
    /// spawn is deliberately <c>detached: false</c>-shaped — an ordinary
    /// <c>child_process.spawn</c> — because that is what puts the grandchildren
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
        SuiteEnvironment.RequireRepositoryPayload();

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
    /// <returns>The launcher's report.</returns>
    private static async Task<JsonObject> RunAsync(
        string label,
        string command,
        string[] arguments,
        int expectedMinimumProcesses)
    {
        using var scratch = ScratchDirectory.Create(label);

        var readyFile = Path.Combine(scratch.Path, "ready.json");
        var donePath = Path.Combine(scratch.Path, "done");

        // The suite's own job, so an assertion that throws below cannot leave
        // the launcher -- or anything under it -- running.
        using var scope = new JobObjectScope();

        var launcher = scope.Launch(
            ProbePath,
            scratch.Path,
            ["job-launcher", scratch.Path, readyFile, command, .. arguments.Select(argument => argument.Replace("{ready}", readyFile, StringComparison.Ordinal))]);

        // Recorded now, while the process is certainly the one we started. The
        // pair (pid, creation time) is the identity from here on.
        var launcherCreated = ProcessIdentity.CreationTimeOf(launcher.Id);

        await WaitForFileAsync(donePath, ReportPatience);

        var report = (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(scratch.Path, "report.json")))!;
        var walk = report["walk"]!.AsArray();

        // The flags, read back inside the launcher from the job that actually
        // contained this tree.
        await Assert.That((uint)report["limitFlags"]!).IsEqualTo(0x00002000u);
        await Assert.That((uint)report["uiRestrictions"]!).IsEqualTo(0u);
        await Assert.That((bool)report["handleIsInheritable"]!).IsFalse();

        // A tree that never came up would satisfy "no escapees" vacuously.
        await Assert.That(walk.Count).IsGreaterThanOrEqualTo(expectedMinimumProcesses);
        await Assert.That((int)report["escapees"]!).IsEqualTo(0);

        foreach (var row in walk)
        {
            var pid = (int)row!["pid"]!;

            // IsProcessInJob for every pid in the descendant tree, and the
            // kernel's own member list for the same pid. Either alone can be
            // incomplete; disagreeing is the interesting case.
            await Assert.That((bool?)row["inOurJob"] is true).IsTrue();
            await Assert.That((bool)row["inJobProcessIdList"]!).IsTrue();
            await Assert.That((string)row["note"]!).IsEmpty();
            await Assert.That(pid).IsNotEqualTo(0);
        }

        // The cross-check in the other direction: a job member the walk never
        // reached would mean the seeding failed.
        await Assert.That(report["jobMembersTheWalkMissed"]!.AsArray().Count).IsEqualTo(0);

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

        await Assert.That(string.Join(", ", survivors)).IsEmpty();

        return report;
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

    private static async Task WaitForFileAsync(string path, TimeSpan patience)
    {
        var deadline = Stopwatch.StartNew();

        while (deadline.Elapsed < patience)
        {
            if (File.Exists(path))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"The launcher never wrote '{path}'.");
    }
}
