// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using BrowserAI.Interop;
using BrowserAI.Runtime;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The job-object containment contract's acceptance test against <b>real
/// browsers</b>: Chromium and Firefox, every descendant in the job, nothing alive
/// after the launcher is killed from outside, and every profile directory
/// deleting cleanly afterwards.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the half a job-object test without a browser cannot reach.</b> The
/// contract is stated as <i>16 runs, 106 processes, 0 escapees, 0 survivors</i>
/// against real Chromium and Firefox trees
/// ([kb](../../kb/windows/job-objects.md));
/// <see cref="JobContainmentTests"/> proves the flags, the ownership and
/// containment through the bundled runtime, and this proves it through the thing
/// the guarantee is actually about.
/// </para>
/// <para>
/// <b>The intuition runs backwards, which is why this is measured rather than
/// argued.</b> On Windows, job membership is inherited automatically by every
/// descendant created with <c>CreateProcess</c>, so a component that spawns
/// children "the normal way" is precisely the case that works; escaping requires
/// an explicit opt-in <b>that our job must grant</b>, and a process requesting
/// <c>CREATE_BREAKAWAY_FROM_JOB</c> from a job that does not permit it fails with
/// <c>ERROR_ACCESS_DENIED</c> rather than escaping. That is the inverse of Linux
/// process-group semantics. It matters here because the production chain already
/// contains a permissive job: libuv creates a global one with
/// <c>JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK</c> and Playwright spawns the browser
/// with <c>detached: false</c> on Windows, and Firefox's launcher stacks a second.
/// Containment holding through both is the strongest available confirmation,
/// because that is the exact configuration that would leak if our job were
/// misconfigured.
/// </para>
/// <para>
/// <b>Firefox is the harder arm and it is not decoration.</b> It stacks a second
/// permissive job of its own on top of libuv's, and its background tasks and
/// crash reporter are the only code in either browser family that asks to break
/// away — so it is the exact configuration that would leak if our job were
/// misconfigured. BrowserAI does not create Firefox <i>sessions</i> yet
/// ([TODO.md](../../TODO.md)); what is under test here is containment, which is
/// <see cref="Interop.JobObject"/>'s and applies to anything the launcher starts.
/// </para>
/// <para>
/// <b>The profile delete is the assertion a survivor count cannot make.</b> A
/// process that is gone from the job's list but still holds a mapped file leaves
/// a directory Windows refuses to remove, and that is the observable difference
/// between "the kernel reported them dead" and "nothing is left".
/// </para>
/// <para>
/// <b>Nothing here matches a process by image name at any step.</b> Every pid is
/// recorded when it is spawned and re-validated against its recorded creation
/// time before anything acts on it, and the one place an image is looked at —
/// deciding whether a process came out of our own browsers root — compares the
/// full path.
/// </para>
/// </remarks>
internal sealed class BrowserContainmentTests
{
    private static readonly string ProbePath = Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe");

    /// <summary>
    /// How long the launcher gets to bring a browser up and report on it.
    /// Generous: a cold Chromium on a loaded machine is the normal reason this is
    /// slow, and a tight deadline reports as a containment failure.
    /// </summary>
    private static readonly TimeSpan ReportPatience = TestDefaults.BrowserHang;

    /// <summary>
    /// How long every member of the tree gets to be gone. <c>KILL_ON_JOB_CLOSE</c>
    /// is a kernel operation, so this is scheduling latency rather than a
    /// shutdown sequence.
    /// </summary>
    private static readonly TimeSpan TeardownPatience = TestDefaults.ProcessHang;

    [Test]
    public async Task AChromiumTreeIsContainedAndItsProfileDeletesCleanly() =>
        await RunAsync("chromium", BrowserAiPaths.ExpectedChromiumExecutable);

    /// <remarks>
    /// <para>
    /// ⚠️ <b>Not serialised against anything, and the history of that is worth
    /// more than the current state.</b> This arm carried
    /// <c>[NotInParallel("stray-sweep")]</c> until 2026-08-17 — not for anything
    /// it does, but because <see cref="FirefoxTests"/>' preflight test asked the
    /// <i>machine</i> whether a Firefox had appeared, and this arm starts one.
    /// Two rounds of narrowing that reading fixed it: first to the Firefox
    /// executable rather than the browsers root, which stopped every Chromium in
    /// the suite falsifying it, and then to a <b>direct child of the test
    /// host</b>, which is what this arm's Firefox — a grandchild of a probe, by
    /// way of <c>node.exe</c> — can never be.
    /// </para>
    /// <para>
    /// <b>It is worth stating what that cost while it stood.</b> This test is
    /// <b>13.05 s</b>, and the chain it was pinned into spanned <b>20.4 s of a
    /// 20.6 s run</b> — so one test's machine-wide question was the suite's
    /// entire critical path. Serialising to protect an over-wide observation is
    /// never free, and here the bill was most of the wall clock.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFirefoxTreeIsContainedAndItsProfileDeletesCleanly() =>
        await RunAsync("firefox", BrowserAiPaths.FirefoxExecutable);

    /// <summary>
    /// The driver lets go of <c>cli-stderr.log</c> when the child it was teeing
    /// ends, so the one failure it can see itself does not leave the file held.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It lives in this class because <see cref="DriverScript"/> does</b>, and
    /// it is the only test here that starts no browser: the child is a dozen
    /// lines of JavaScript that answers two frames and leaves. What it drives is
    /// the driver, in the tree shape the arms above use, with the browser taken
    /// out — so a change to the tee is caught in seconds rather than by a
    /// containment run.
    /// </para>
    /// <para>
    /// <b>The observation is a plain <c>File.ReadAllText</c>, deliberately.</b>
    /// That asks for <c>FileShare.Read</c>, which a live writer refuses, so the
    /// read succeeding <i>is</i> the measurement that nothing is holding the
    /// file. It is also the exact call
    /// <see cref="LauncherWait.Evidence"/> used to make and no longer does —
    /// the two halves of this pair meet here, and this one asserts the half the
    /// other cannot: a reader that shares everything can no longer tell a closed
    /// file from a held one.
    /// </para>
    /// <para>
    /// ⚠️ <b>The liveness assertion is not decoration.</b> A driver that died
    /// would release the handle too, and then a green here would be saying
    /// nothing at all. It is read after the wait rather than before it, so the
    /// case it exists for — the driver dying <i>during</i> the wait — is the case
    /// it catches.
    /// </para>
    /// <para>
    /// <b>Watched red 2026-08-30 against the committed driver</b>, twice over.
    /// Through this arm, with the two handlers below deleted: <b>1 of 1 failed at
    /// 10 m 00.4 s</b>, the whole of <see cref="TeardownPatience"/>, reporting
    /// <i>"the driver was still holding … with the child it was teeing long
    /// gone"</i>. And by hand outside the suite, which is where the shape was
    /// established first: the same child, the same four arguments,
    /// <c>File.ReadAllText</c> refused for the whole wait with <i>"because it is
    /// being used by another process"</i> while the driver stayed alive — and the
    /// enumerated length of that held file read <b>0</b> against 61 real bytes,
    /// the same phantom the 2026-08-29 dump printed, arriving here out of the
    /// driver's own tee.
    /// </para>
    /// <para>
    /// <b>What this cannot reach is the failure the arms above are about</b>, and
    /// no test can: the launcher kills the driver with <c>TerminateProcess</c>,
    /// where no handler runs, and a browser that stalls forever never ends its
    /// child at all. On both of those the file is still open when the dump reads
    /// it. That is why the reader was fixed and why this is the smaller half.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheDriversStderrTeeIsClosedOnceTheChildItWasTeeingIsGone()
    {
        SuiteEnvironment.RequireRepositoryPayload();

        using var scratch = ScratchDirectory.Create("containment-driver-tee");

        var readyFile = Path.Combine(scratch.Path, "ready.json");
        var driver = Path.Combine(scratch.Path, "drive-a-browser.js");
        var child = Path.Combine(scratch.Path, "a-child-that-answers-and-leaves.js");
        var tee = Path.Combine(scratch.Path, "cli-stderr.log");

        await File.WriteAllTextAsync(driver, DriverScript);
        await File.WriteAllTextAsync(child, ChildThatAnswersAndLeaves);

        using var scope = new JobObjectScope();

        var launched = scope.Launch(
            RepositoryPayload.Layout.NodeExecutable,
            scratch.Path,
            driver,
            readyFile,
            child,
            // The child ignores both, and they are passed anyway because the
            // driver's argv is the contract under test.
            Path.Combine(scratch.Path, "playwright-mcp.config.json"),
            BrowserAiPaths.BrowsersDirectory);

        var created = ProcessIdentity.CreationTimeOf(launched.Id);

        // The driver writes this once it has an answer, which is also when the
        // child leaves -- so reaching it means the round trip happened rather
        // than the child having failed to start.
        await LauncherWait.ForDoneAsync(readyFile, TeardownPatience, scratch.Path, launched.Id, created);

        var waited = Stopwatch.StartNew();
        string? teed = null;

        while (waited.Elapsed < TeardownPatience)
        {
            try
            {
                teed = await File.ReadAllTextAsync(tee);
                break;
            }
            catch (IOException)
            {
                await Task.Delay(100);
            }
        }

        await Assert.That(ProcessIdentity.IsAlive(launched.Id, created))
            .IsTrue()
            .Because(
                "the driver is gone, so whether it closed the tee is not what this measured"
                + Environment.NewLine + LauncherWait.Evidence(scratch.Path));

        await Assert.That(teed)
            .IsNotNull()
            .Because(
                $"the driver was still holding '{tee}' after {waited.Elapsed.TotalSeconds:F1} s, with the child it was teeing long gone"
                + Environment.NewLine + LauncherWait.Evidence(scratch.Path));

        await Assert.That(teed).Contains(TeeSentinel).Because(teed);
    }

    private static async Task RunAsync(string browser, string expectedExecutable)
    {
        // ⚠️ The cost of the alternative, stated rather than hidden: on a machine
        // where this family has never been provisioned, the arm proves nothing
        // and the guarantee for it would rest on the recorded measurement in
        // kb/windows/processes.md alone. So it reports as SKIPPED rather than as
        // a pass, and a release run refuses -- an unprovisioned family is the
        // batteries-included premise being dead code with the suite green.
        if (browser is "firefox")
        {
            SuiteEnvironment.RequireProvisionedFirefox();
        }
        else
        {
            SuiteEnvironment.RequireProvisionedChromium();
        }

        using var scratch = ScratchDirectory.Create($"browser-containment-{browser}");

        var readyFile = Path.Combine(scratch.Path, "ready.json");
        var donePath = Path.Combine(scratch.Path, "done");
        var profile = Path.Combine(scratch.Path, "profile");
        var output = Path.Combine(scratch.Path, "output");
        var downloads = Path.Combine(scratch.Path, "downloads");
        var driver = Path.Combine(scratch.Path, "drive-a-browser.js");
        var configFile = Path.Combine(scratch.Path, "playwright-mcp.config.json");

        _ = Directory.CreateDirectory(profile);
        _ = Directory.CreateDirectory(output);
        _ = Directory.CreateDirectory(downloads);

        await File.WriteAllTextAsync(driver, DriverScript);
        await File.WriteAllBytesAsync(configFile, Config(browser, profile, output, downloads));

        // The suite's own job, so an assertion that throws below cannot leave a
        // browser -- or anything under it -- running.
        using var scope = new JobObjectScope();

        var launcher = scope.Launch(
            ProbePath,
            scratch.Path,
            [
                "job-launcher",
                scratch.Path,
                readyFile,
                // The launcher's ready-wait is this test's patience and nothing
                // else, so there is one budget rather than a hidden tighter one.
                ReportPatience.TotalSeconds.ToString(CultureInfo.InvariantCulture),
                RepositoryPayload.Layout.NodeExecutable,
                driver,
                readyFile,
                RepositoryPayload.Layout.PlaywrightMcpCli,
                configFile,
                BrowserAiPaths.BrowsersDirectory,
            ]);

        // Recorded now, while the process is certainly the one we started. The
        // pair (pid, creation time) is the identity from here on.
        var launcherCreated = ProcessIdentity.CreationTimeOf(launcher.Id);

        await LauncherWait.ForDoneAsync(donePath, ReportPatience, scratch.Path, launcher.Id, launcherCreated);

        var report = (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(scratch.Path, "report.json")))!;
        var walk = report["walk"]!.AsArray();

        // The flags, read back inside the launcher from the job that actually
        // contained this browser.
        await Assert.That((uint)report["limitFlags"]!).IsEqualTo(0x00002000u);
        await Assert.That((uint)report["uiRestrictions"]!).IsEqualTo(0u);
        await Assert.That((bool)report["handleIsInheritable"]!).IsFalse();

        // A tree that never came up would satisfy "no escapees" vacuously. A
        // real browser is node + the browser + its helpers, so the floor is well
        // above the two processes a failed launch would produce.
        await Assert.That(walk.Count).IsGreaterThanOrEqualTo(4);
        await Assert.That((int)report["escapees"]!).IsEqualTo(0);

        foreach (var row in walk)
        {
            await Assert.That((bool?)row!["inOurJob"] is true).IsTrue();
            await Assert.That((bool)row["inJobProcessIdList"]!).IsTrue();
            await Assert.That((string)row["note"]!).IsEmpty();
        }

        // The cross-check in the other direction: a job member the walk never
        // reached would mean the seeding failed.
        await Assert.That(report["jobMembersTheWalkMissed"]!.AsArray().Count).IsEqualTo(0);

        // And the tree really is a browser out of BrowserAI's own root, rather
        // than four processes that happened to start. Two independent facts:
        // the driver navigated a real page, and at least one member of THIS
        // job's walk is running an image under the browsers root -- intersected
        // with the walk, so a browser another test has open cannot satisfy it.
        var child = report["childReport"]!;

        // ⚠️ The driver's own answer and the launcher's tree are inlined into
        // this failure, because without them it says `Expected to be true but
        // found False` and nothing else — about a browser, in a scratch
        // directory the test then deletes.
        //
        // Added 2026-08-18, after this arm failed at 3m04s with exactly that
        // message. Three minutes is Playwright's own
        // DEFAULT_PLAYWRIGHT_LAUNCH_TIMEOUT, so the answer sitting unread in the
        // report was upstream's account of a launch that did not happen — the
        // one thing worth having, and the one thing not printed.
        await Assert.That((bool?)child["navigated"])
            .IsTrue()
            .Because(
                $"the driver did not navigate. Its answer was: {(string?)child["answer"] ?? "<none>"}"
                + Environment.NewLine + LauncherWait.Evidence(scratch.Path));

        var walked = walk.Select(row => (int)row!["pid"]!).ToHashSet();

        // Matched on the full image PATH, which is the documented detection
        // route; an image name would find the user's own Chrome just as readily.
        var fromOurRoot = BrowserProcesses.RunningFrom(BrowserAiPaths.BrowsersDirectory)
            .Where(process => walked.Contains(process.ProcessId))
            .ToList();

        await Assert.That(fromOurRoot.Count).IsGreaterThan(0);
        await Assert.That(fromOurRoot.All(process => process.ImagePath.StartsWith(BrowserAiPaths.BrowsersDirectory, StringComparison.OrdinalIgnoreCase))).IsTrue();

        // ⚠️ Restart registration, asked of the live process rather than argued
        // from a length — and the two browsers do NOT answer the same way.
        // Windows resurrects a registered process after a reboot or an update,
        // and [the maintainer's own browsers came back that
        // way](../../kb/chromium/resurrection.md): no session, no lock, nothing
        // to attribute them to.
        var registered = fromOurRoot
            .Select(process => (process.ProcessId, process.ImagePath, Result: RestartRegistration.Of(process.ProcessId)))
            .Where(entry => entry.Result is not RestartRegistration.NotRegistered)
            .ToList();

        if (browser is "chromium")
        {
            // The family BrowserAI actually launches, and the done-test's own
            // bullet: every process answers ERROR_NOT_FOUND. The recorded reason
            // is that Playwright's command line overshoots
            // RegisterApplicationRestart's 1023-character limit so the
            // registration fails — that is an argument, and this is the
            // observation. An upstream that trimmed its argument list would flip
            // it with nothing else changing.
            var offenders = registered.Select(entry => $"pid {entry.ProcessId} answered 0x{entry.Result:X8} rather than ERROR_NOT_FOUND: {entry.ImagePath}");

            await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
        }
        else
        {
            // ⚠️ Measured 2026-08-16 and it contradicts the assumption the bullet
            // was written under: **Firefox registers itself for restart** —
            // exactly one process in the tree answers S_OK — which is
            // `toolkit.winRegisterApplicationRestart` doing what
            // [kb](../../kb/chromium/resurrection.md) says it does, on a build
            // BrowserAI provisioned. Containment is unaffected, because
            // KILL_ON_JOB_CLOSE happens now and Windows' restart happens after a
            // reboot or an update — but it means Firefox sessions cannot be
            // offered without turning that pref off in the profile, or a machine
            // update will resurrect a browser no session claims. That is what
            // `FirefoxProfile` writes and `FirefoxTests` asserts; this arm
            // launches Firefox WITHOUT it, which is why it still answers S_OK.
            //
            // Asserted rather than merely noted, so the day Mozilla changes it
            // this test says so instead of going quietly green.
            await Assert.That(registered.Count).IsEqualTo(1);
        }

        var recorded = walk
            .Select(row => ((int)row!["pid"]!, (long)row["createdFileTime"]!))
            .ToList();

        // The event under test. TerminateProcess, not a graceful stop: the
        // launcher runs no code after this line, so nothing but the kernel
        // closing its last job handle can be what cleans up.
        ProcessIdentity.Terminate(launcher.Id, launcherCreated);
        recorded.Add((launcher.Id, launcherCreated));

        var survivors = await WaitForNoneAliveAsync(recorded, TeardownPatience);

        await Assert.That(string.Join(", ", survivors)).IsEmpty();

        // ⚠️ The half a survivor count cannot make. A browser that is gone from
        // the process table but still holds a mapped file leaves a directory
        // Windows will not remove, so a profile that deletes cleanly is the
        // observable difference between "reported dead" and "nothing is left".
        // §E's own routine does the deleting, so this also exercises the
        // per-node try/catch rather than a second implementation.
        var failures = await ScratchDirectory.RemoveTreeWhenReleasedAsync(profile, TeardownPatience);

        await Assert.That(string.Join(Environment.NewLine, failures)).IsEmpty();
        await Assert.That(Directory.Exists(profile)).IsFalse();

        // The run's own numbers, written where a person re-establishing
        // [row 2a](../../kb/re-verification.md) can read them. The
        // assertions above are the gate; this is the evidence, and a measurement
        // recorded in a document with no reproducible source is the tally this
        // project keeps having to correct.
        Record(
            browser,
            walk.Count,
            (int)report["escapees"]!,
            survivors.Count,
            fromOurRoot.Count,
            registered.Count,
            (double)report["readyMilliseconds"]!,
            (double)report["readyPatienceMilliseconds"]!);
    }

    /// <remarks>
    /// ⚠️ <b><c>readyMilliseconds</c> is recorded on every run, including the
    /// ones that pass, and that is the point of it.</b> A bound can only be
    /// called too tight against a distribution, and a distribution cannot be
    /// reconstructed from the runs that failed. Measured 2026-08-17 on this
    /// machine: unloaded, this whole test is 5.3-5.7 s over eight consecutive
    /// runs -- the launcher's ready-wait is a small fraction of that, against a
    /// patience of 180 s.
    /// </remarks>
    private static void Record(
        string browser,
        int processes,
        int escapees,
        int survivors,
        int fromOurRoot,
        int restartRegistered,
        double readyMilliseconds,
        double readyPatienceMilliseconds)
    {
        var summary = new JsonObject
        {
            ["browser"] = browser,
            ["processesInTheJob"] = processes,
            ["escapees"] = escapees,
            ["survivorsAfterAnExternalKill"] = survivors,
            ["processesRunningFromOurBrowsersRoot"] = fromOurRoot,
            ["processesRegisteredForRestart"] = restartRegistered,
            ["profileDeletedCleanly"] = true,
            ["readyMilliseconds"] = readyMilliseconds,
            ["readyPatienceMilliseconds"] = readyPatienceMilliseconds,
            ["utc"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };

        var path = Path.Combine(RepositoryLayout.Root.FullName, ".work", $"containment-{browser}.json");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, summary.ToJsonString());
    }

    /// <summary>
    /// The whole browser-driving half, as a script the bundled <c>node</c> runs
    /// inside the launcher's job.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It speaks MCP by hand over a pipe rather than importing anything.</b>
    /// What is under test is containment, so the fewer layers between the job and
    /// the browser the better — and this way the tree is exactly the production
    /// shape with one extra node in front: launcher → driver → <c>cli.js</c> →
    /// browser → helpers. An extra level makes the test stricter, never weaker.
    /// </para>
    /// <para>
    /// It reports what it navigated and how many processes came out of the
    /// browsers root, so the assertion that a real browser was up is made from
    /// evidence rather than from a process count that a failed launch could also
    /// produce.
    /// </para>
    /// <para>
    /// ⚠️ <b>It reads the child's <c>stderr</c> and tees it to
    /// <c>cli-stderr.log</c>, and until 2026-08-30 it did neither.</b> The
    /// child was spawned with all three streams piped and only <c>stdout</c>
    /// was ever read, so upstream's account of every launch that did not happen
    /// went into a pipe nobody drained. That is the same gap
    /// <see cref="BrowserAI.TestProbe.JobProbe"/> closed one level up on
    /// 2026-08-17 — its comment there says a discarded stream is "the whole
    /// difference between a diagnosable failure and a three-minute mystery" —
    /// and it was still open one level down, which is why the 2026-08-18 dump
    /// could name a failure and not say why. It was open again on 2026-08-29:
    /// a Firefox arm stalled 32 s between creating its profile database and
    /// initialising NSS and then went silent for two minutes, on a machine four
    /// independent instruments say was quiet, and the only account of it was in
    /// that pipe.
    /// </para>
    /// <para>
    /// <b>The file goes in the scratch directory rather than through the host</b>,
    /// because <see cref="LauncherWait.Evidence"/> already inlines every file it
    /// finds there, truncated, into the failure message — so a tee is the whole
    /// change and nothing on the C# side needs to know this file exists.
    /// </para>
    /// <para>
    /// <b>An unread pipe is a second failure mode and this closes that too.</b>
    /// A pipe whose reader never reads it fills, and a child writing into a full
    /// pipe blocks in its write rather than reporting anything — so a
    /// sufficiently chatty <c>cli.js</c> would have hung here in a way
    /// indistinguishable from the browser stall this arm exists to catch.
    /// Unobserved rather than hypothetical: nothing in this tree has ever
    /// measured how much upstream writes before it comes up.
    /// </para>
    /// <para>
    /// <b>Added 2026-08-30: the tee is closed on the one failure this process
    /// can see itself, and the comment beside it says why that is the smaller
    /// half.</b> When the child ends, the handle goes; when the launcher kills
    /// this process from outside — which is the event the arms above are about —
    /// nothing runs at all, so the file is still open when
    /// <see cref="LauncherWait.Evidence"/> reads it. That is why the pair
    /// exists and why the reader, not the writer, carries the weight of it. Held
    /// by
    /// <see cref="TheDriversStderrTeeIsClosedOnceTheChildItWasTeeingIsGone"/>.
    /// </para>
    /// </remarks>
    private const string DriverScript = """
        const cp = require('child_process');
        const fs = require('fs');
        const path = require('path');

        const [readyFile, cli, configFile, browsersRoot] = process.argv.slice(2);

        // Beside the ready file, which is the scratch directory the host's
        // failure dump walks -- so this needs no wiring on the C# side at all.
        // `dirname(readyFile)` rather than `process.cwd()`: the working
        // directory is the launcher's choice and this is the host's.
        const stderrLog = fs.openSync(path.join(path.dirname(readyFile), 'cli-stderr.log'), 'a');

        // Closing is NOT about flushing -- writeSync below already put every
        // chunk in the OS, which is what makes them another process's to read.
        // What it buys is the handle going away, so the directory entry carries
        // the true size and nothing is holding a file in a tree somebody is
        // about to delete.
        //
        // ⚠️ It cannot cover the path this test is actually about, and that is
        // stated here because it is where the decision was made. The launcher
        // kills this process with TerminateProcess from outside -- that is the
        // event the whole test is about -- and no 'exit' handler, no finally and
        // no signal handler runs then; a browser that stalls and never comes up
        // reaches none of these events either. So on the failure worth
        // diagnosing this file IS still open when the host reads it, which is
        // why LauncherWait.Evidence has to be able to open a file somebody is
        // holding, and why THAT half is the load-bearing one and this is the
        // tidy-up.
        let teeOpen = true;
        function closeTee() {
          if (!teeOpen) return;
          teeOpen = false;
          try { fs.closeSync(stderrLog); } catch { /* a close that fails must no more fail the run than a write that does */ }
        }

        const child = cp.spawn(process.execPath, [cli, '--config', configFile, '--sandbox'], {
          stdio: ['pipe', 'pipe', 'pipe'],
          env: { ...process.env, PLAYWRIGHT_BROWSERS_PATH: browsersRoot, PLAYWRIGHT_SKIP_BROWSER_GC: '1' },
        });

        // writeSync per chunk, deliberately, and not a WriteStream. This process
        // is killed by TerminateProcess from outside -- that is the event the
        // whole test is about -- and a stream's buffered tail dies with it,
        // which would lose exactly the last thing upstream said before it
        // stopped saying anything. A synchronous write is in the OS by the time
        // it returns.
        child.stderr.on('data', (chunk) => {
          // The flag is not belt and braces: a closed fd number is handed
          // straight to the next open, so a late chunk would be written into
          // whatever file inherited it.
          if (!teeOpen) return;
          try { fs.writeSync(stderrLog, chunk); } catch { /* the dump is diagnostics; losing it must never fail the run */ }
        });

        // ⚠️ 'close' and not 'exit'. 'exit' fires while stderr may still have
        // buffered chunks to deliver; 'close' fires once the child is gone AND
        // its stdio streams are done. Closing on 'exit' would drop the last
        // thing upstream said into an EBADF in the catch above -- the one line
        // worth having, lost by the tidy-up meant to preserve it.
        child.on('close', closeTee);

        // Re-thrown rather than swallowed. With no listener at all this event
        // crashes the driver, which is the honest fast failure: the launcher
        // sees the driver gone and says so. A listener that merely returned
        // would trade a loud death for a quiet hang, so this one adds the close
        // and leaves the death exactly where it was.
        child.on('error', (failure) => { closeTee(); throw failure; });

        let buffer = '';
        const pending = new Map();
        child.stdout.on('data', (chunk) => {
          buffer += chunk.toString('utf8');
          let index;
          while ((index = buffer.indexOf('\n')) >= 0) {
            const line = buffer.slice(0, index).trim();
            buffer = buffer.slice(index + 1);
            if (!line) continue;
            try {
              const message = JSON.parse(line);
              if (message.id && pending.has(message.id)) {
                pending.get(message.id)(message);
                pending.delete(message.id);
              }
            } catch { /* not a frame we asked for */ }
          }
        });

        let nextId = 1;
        function send(method, params) {
          const id = nextId++;
          return new Promise((resolve) => {
            pending.set(id, resolve);
            child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
          });
        }

        (async () => {
          await send('initialize', {
            protocolVersion: '2025-11-25',
            capabilities: {},
            clientInfo: { name: 'containment-driver', version: '1' },
          });
          child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');

          const answer = await send('tools/call', {
            name: 'browser_navigate',
            arguments: { url: 'data:text/html,<h1>ok</h1>' },
          });

          const text = JSON.stringify(answer);
          const navigated = !!answer.result && answer.result.isError !== true && text.includes('data:text/html');

          // The driver reports what it did and nothing about the process table.
          // Whether a browser out of BrowserAI's own root is in this job is the
          // HOST's question, answered with the product's own image-path
          // enumeration -- which is also the only version of that question this
          // repository permits.
          fs.writeFileSync(readyFile, JSON.stringify({ navigated, answer: text.slice(0, 400), childPid: child.pid }));
          setInterval(() => {}, 1e9);
        })();
        """;

    /// <summary>
    /// What the stand-in child writes to <c>stderr</c>, and the only thing
    /// <see cref="TheDriversStderrTeeIsClosedOnceTheChildItWasTeeingIsGone"/>
    /// looks for in the tee.
    /// </summary>
    private const string TeeSentinel = "a child that answers and leaves: the account a stall would have taken with it";

    /// <summary>
    /// A stand-in for <c>cli.js</c> that says one thing on <c>stderr</c>, answers
    /// the frames the driver sends, and leaves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It is not a fake MCP server and must not become one.</b> All the driver
    /// needs to reach its ready file is a reply carrying the id it sent; nothing
    /// here reads a method or a parameter beyond deciding which frame is the last
    /// one, and <see cref="FakePlaywrightChild"/> is where a real double lives.
    /// </para>
    /// <para>
    /// <b><c>fs.writeSync(2, …)</c> rather than <c>process.stderr.write</c>:</b>
    /// stderr on a pipe is asynchronous in node and <c>process.exit</c> does not
    /// wait for it, so the sentinel would be racing the exit below. That is the
    /// driver's own reasoning about its tee, one level further down.
    /// </para>
    /// <para>
    /// <b>The exit rides the write callback</b>, which fires once the reply is in
    /// the OS, so leaving cannot truncate the answer the driver is waiting on.
    /// That is a completion signal and not a delay — there is no duration
    /// anywhere in this script, which is what keeps the arm above a hang detector
    /// rather than a race.
    /// </para>
    /// </remarks>
    private const string ChildThatAnswersAndLeaves = $$"""
        const fs = require('fs');

        fs.writeSync(2, '{{TeeSentinel}}\n');

        let buffer = '';
        process.stdin.on('data', (chunk) => {
          buffer += chunk.toString('utf8');
          let index;
          while ((index = buffer.indexOf('\n')) >= 0) {
            const line = buffer.slice(0, index).trim();
            buffer = buffer.slice(index + 1);
            if (!line) continue;
            let message;
            try { message = JSON.parse(line); } catch { continue; }
            if (!message.id) continue;
            const last = message.method === 'tools/call';
            const reply = JSON.stringify({ jsonrpc: '2.0', id: message.id, result: { ok: true } }) + '\n';
            process.stdout.write(reply, () => { if (last) process.exit(0); });
          }
        });
        """;

    /// <summary>The config the driven child is started with.</summary>
    /// <remarks>
    /// <b>Chromium uses the product's own generator</b>, so the Chromium arm
    /// contains the browser BrowserAI would actually launch, with the
    /// chromium-alias channel that makes the headless shell unreachable.
    /// <b>Firefox does not, because there is nothing to reuse:</b> BrowserAI
    /// creates no Firefox sessions at all yet ([TODO.md](../../TODO.md)), so this
    /// arm spells the
    /// minimum that selects it and nothing else — the family, the profile and
    /// headless. In particular no channel, because <c>chromiumAliases</c> is a
    /// Chromium concept with no Firefox equivalent.
    /// </remarks>
    private static byte[] Config(string browser, string profile, string output, string downloads)
    {
        if (browser is "chromium")
        {
            return BrowserConfiguration.Generate(new BrowserConfigurationRequest
            {
                Headless = true,
                UserDataDirectory = profile,
                OutputDirectory = output,
                DownloadsDirectory = downloads,
                Capabilities = BrowserConfiguration.GrantedCapabilities,
            }).Json;
        }

        return System.Text.Encoding.UTF8.GetBytes($$"""
                {
                  "browser": {
                    "browserName": "firefox",
                    "userDataDir": {{JsonValue.Create(profile)!.ToJsonString()}},
                    "launchOptions": {
                      "headless": true,
                      "downloadsPath": {{JsonValue.Create(downloads)!.ToJsonString()}}
                    }
                  },
                  "capabilities": ["config", "vision", "devtools"],
                  "outputDir": {{JsonValue.Create(output)!.ToJsonString()}},
                  "saveSession": false,
                  "console": { "level": "info" }
                }
                """);
    }

    private static async Task<List<int>> WaitForNoneAliveAsync(List<(int ProcessId, long CreatedFileTime)> recorded, TimeSpan patience)
    {
        var deadline = Stopwatch.StartNew();

        while (true)
        {
            var survivors = recorded
                .Where(entry => ProcessIdentity.IsAlive(entry.ProcessId, entry.CreatedFileTime))
                .Select(entry => entry.ProcessId)
                .ToList();

            if (survivors.Count is 0 || deadline.Elapsed > patience)
            {
                return survivors;
            }

            await Task.Delay(100);
        }
    }

}
