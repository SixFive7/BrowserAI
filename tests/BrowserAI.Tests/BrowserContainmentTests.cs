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

        await Assert.That((bool?)child["navigated"]).IsTrue();

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
        var failures = new List<string>();
        await DeleteWhenReleasedAsync(profile, failures, TeardownPatience);

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
    /// </remarks>
    private const string DriverScript = """
        const cp = require('child_process');
        const fs = require('fs');
        const path = require('path');

        const [readyFile, cli, configFile, browsersRoot] = process.argv.slice(2);

        const child = cp.spawn(process.execPath, [cli, '--config', configFile, '--sandbox'], {
          stdio: ['pipe', 'pipe', 'pipe'],
          env: { ...process.env, PLAYWRIGHT_BROWSERS_PATH: browsersRoot, PLAYWRIGHT_SKIP_BROWSER_GC: '1' },
        });

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
                Capabilities = BrowserConfiguration.BaseCapabilities,
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

    /// <summary>
    /// Deletes a profile once whatever held it has let go, and reports what would
    /// not go.
    /// </summary>
    /// <remarks>
    /// Bounded and retried on the <b>whole</b> tree rather than per file, because
    /// a terminated process is signalled before the kernel has torn its handles
    /// down: <c>TerminateProcess</c> returning is not proof that a mapped file has
    /// been released. What is being measured is whether the profile becomes
    /// deletable at all, not how fast.
    /// </remarks>
    private static async Task DeleteWhenReleasedAsync(string directory, List<string> failures, TimeSpan patience)
    {
        var waited = Stopwatch.StartNew();

        while (true)
        {
            failures.Clear();
            TreeDelete.Remove(directory, failures);

            if (failures.Count is 0 || waited.Elapsed > patience)
            {
                return;
            }

            await Task.Delay(200);
        }
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
