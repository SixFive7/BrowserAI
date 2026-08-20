// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using BrowserAI.Hosting;
using BrowserAI.Interop;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// The Firefox half of the locking design: the <c>parent.lock</c> preflight,
/// Restart Manager attribution, and restart registration turned off on every
/// launch.
/// </summary>
/// <remarks>
/// <para>
/// <b>The preflight prevents a hang rather than a wrong answer, so the test
/// asserts on the clock.</b> Playwright's <c>isProfileLocked</c> checks only
/// Chromium's <c>lockfile</c> and never Firefox's <c>parent.lock</c>
/// ([kb](../../kb/chromium/profiles.md#the-dialog-hazard--worse-than-a-dialog-appears)),
/// so a collision is answered by Firefox itself — with a native modal on the
/// Windows desktop, against a three-minute launch timeout, on a machine with
/// nobody at the keyboard. A refusal that took three minutes would satisfy every
/// other assertion here and would have prevented nothing.
/// </para>
/// <para>
/// <b>Nothing in this file matches, counts or terminates a process by image
/// name — including the arms that deliberately look at the developer's own
/// Firefox.</b> Every pid acted on is one this test recorded at spawn and
/// re-validated against its creation time; every browser identified as ours is
/// identified by full image path against the binary BrowserAI provisioned. That
/// is not a stylistic preference here: this machine is running dozens of the
/// maintainer's own <c>firefox.exe</c> out of <c>C:\Program Files</c>, and they
/// are the reason the rule exists.
/// </para>
/// <para>
/// ⚠️ <b>A machine-wide reading in a Firefox test must be scoped to the Firefox
/// executable, never to the browsers root.</b> The root holds Chromium too, and
/// <see cref="BrowserProcesses.RunningFrom"/> is a prefix match on it — so a
/// "did a browser appear" question asked of the root is answered by every
/// Chromium renderer, GPU and utility process that the seven unrelated launch
/// sites elsewhere in this suite start, four tests at a time. That is a Firefox
/// assertion failing for a Chromium reason, and it was a 4-in-15 flake until
/// 2026-08-17.
/// The <c>stray-sweep</c> group serialises the tests that start a <i>Firefox</i>
/// and deliberately does not constrain the ones that start a Chromium; the
/// scoping is what makes that division correct rather than lucky. The full
/// account, with the measurements, is on the assertion itself.
/// </para>
/// </remarks>
internal sealed class FirefoxTests
{
    /// <summary>
    /// Playwright's own <c>DEFAULT_PLAYWRIGHT_LAUNCH_TIMEOUT</c>, which is what
    /// the desktop modal blocks against.
    /// </summary>
    private static readonly TimeSpan ModalTimeout = TimeSpan.FromMinutes(3);

    /// <summary>
    /// The hang detector for a whole conversation with a real Firefox: start the
    /// process, hand shake, launch the browser, navigate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Strictly larger than <see cref="ModalTimeout"/>, and that is a
    /// correctness property rather than slack.</b> A harness bound at or below
    /// Playwright's own launch timeout always wins the race, so the test reports
    /// <i>"the budget expired"</i> in place of the diagnosis the product and
    /// upstream were about to give.
    /// </para>
    /// <para>
    /// <b>Measured 2026-08-17 under a fully parallel suite:</b> exactly that.
    /// <c>AFirefoxWeLaunchedIsAttributedToItsSessionAndIsNotRegisteredForRestart</c>
    /// failed at <b>3m01s</b> with <i>"'tools/call' (id 2) did not complete
    /// before this client's whole-conversation budget of 180 s expired"</i>, the
    /// peer still running and its stderr empty — which names nothing. Five runs
    /// of every Firefox test together on an idle machine were clean at 7–9 s,
    /// so the launch was not conflicting with the other real Firefox in the
    /// suite; it was being cut off.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-18 (previously <c>ModalTimeout + 2 minutes</c>,
    /// a whole-conversation budget).</b> Two things changed and both matter.
    /// <see cref="RawStdioClient"/> now applies its bound <b>per exchange</b>
    /// rather than from <c>Start()</c>, so this no longer has to be padded to
    /// cover however many calls a test happens to make; and the value is now the
    /// suite's shared <see cref="TestDefaults.BrowserHang"/>, so a Firefox launch
    /// and a Chromium one are watched by the same number rather than by two that
    /// can drift apart.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan LaunchPatience = TestDefaults.BrowserHang;

    /// <summary>
    /// The preflight refuses a held profile, immediately, with no Firefox
    /// process and no window anywhere.
    /// </summary>
    [Test]
    public async Task ThePreflightRefusesAHeldProfileBeforeAnyFirefoxOrWindowExists()
    {
        using var scratch = ScratchDirectory.Create("firefox-preflight");
        using var scope = new JobObjectScope();

        var session = NewSession(scratch, "held");
        var profile = Path.Combine(session.FullPath, SessionLayout.ProfileFolderName);
        var config = BrowserConfiguration.ForSession(session, headed: false, ProvisionedBrowsers.Firefox, tracing: false, BrowserConfiguration.DefaultConsoleLevel);

        _ = Directory.CreateDirectory(profile);

        // A separate process holding parent.lock exactly the way Firefox holds
        // it -- read and write, no sharing at all. In another process because
        // that is the whole condition: a handle held on another thread of this
        // one would not be refused to this one, and the Restart Manager would
        // name the test host rather than the holder.
        var ready = Path.Combine(scratch.Path, "holder.json");
        var holder = scope.Launch(
            PlantedProbe.ExecutablePath,
            scratch.Path,
            "hold-file",
            FirefoxProfile.LockFileIn(profile),
            ready);

        var holderCreated = ProcessIdentity.CreationTimeOf(holder.Id);
        _ = await ProbeReport.ReadAsync(ready, TestDefaults.ProcessHang);

        // What the desktop and the Firefox process table looked like before
        // anything was asked. Both are re-read afterwards, so what is asserted is
        // the difference rather than an absolute.
        var firefoxBefore = OurFirefoxProcessIds();
        var windowsBefore = TopLevelWindows.All().ToHashSet();

        // ⚠️ MEASURED AND RECORDED, NEVER ASSERTED ON. What the refusal costs is
        // evidence a person re-establishing the Restart Manager's price can read
        // (it lands in `Record` below); it is not a bound, and no assertion in
        // this file compares it to anything.
        var clock = Stopwatch.StartNew();
        var refusal = FirefoxProfileLockedException.For(config);
        clock.Stop();

        // The refusal itself.
        await Assert.That(refusal).IsNotNull();
        await Assert.That(refusal!.Message).Contains("is held open by another process");
        await Assert.That(refusal.Message).Contains($"PID {holder.Id.ToString(CultureInfo.InvariantCulture)}");
        await Assert.That(refusal.Message).Contains("up to three minutes");
        await Assert.That(refusal.Message).Contains(FirefoxProfile.LockFileName);

        // ⚠️ Deleted 2026-08-18: a stopwatch around the call above, asserted
        // under a 15 s "preflight budget" and again under ModalTimeout, with the
        // note "anything that reached Firefox at all could not answer inside five
        // seconds".
        //
        // It was a PROMPTNESS assertion wearing a hang detector's name, and the
        // property it claimed to establish -- that the refusal was decided
        // without launching anything -- is asserted directly, twice, immediately
        // below: no browser in this test's job, and no Firefox out of BrowserAI's
        // root anywhere on the machine. A preflight that reached Firefox would
        // leave a process; those two readings are what say it did not, and they
        // say it whatever the machine's load. Fifteen seconds, by contrast, is a
        // number a starved box can reach while the product is behaving perfectly
        // -- and the Restart Manager walk inside the refusal is exactly the kind
        // of machine-wide handle enumeration that goes slow when the machine is
        // busy. Measured on this machine: 638 ms for the walk, 1,367 ms end to
        // end with 42 of the developer's own Firefox processes to enumerate.
        //
        // ⚠️ No Firefox started, asserted two ways because neither is sufficient
        // alone. This one is exact and it is the WIDER of the two: everything
        // this test could have started is inside this scope's job, whatever image
        // it runs, and the kernel's own membership list is what says so. It is
        // immune to whatever else on the machine starts while this runs, which is
        // the property the machine-wide reading below cannot have.
        //
        // The membership is intersected with the image-path scan rather than
        // compared against the holder's pid alone: a console process started
        // through CreateProcessW brings a `conhost.exe` into the job with it,
        // which is Windows rather than a launch.
        var inScope = scope.ProcessIds().ToHashSet();
        var browsersInScope = BrowserProcesses.RunningFrom(BrowserAiPaths.BrowsersDirectory)
            .Where(process => inScope.Contains(process.ProcessId))
            .Select(process => process.ImagePath);

        await Assert.That(string.Join(", ", browsersInScope)).IsEmpty();

        // ⚠️ The machine-wide reading, and it is scoped to the FIREFOX EXECUTABLE
        // rather than to the browsers root. That distinction is the whole of a
        // 2-in-5 flake, so it is stated rather than left to look like a
        // narrowing.
        //
        // The reading exists to catch a Firefox that escaped the job above. Asked
        // of the browsers ROOT it also caught every CHROMIUM on the machine, and
        // `BrowserProcesses.RunningFrom` is a prefix match on that root, so it
        // matched Chromium's browser process and each of its GPU, renderer and
        // utility helpers as they appeared.
        //
        // Seven places in this suite launch a real Chromium out of that root, and
        // they are counted rather than estimated because the first version of
        // this note guessed: `SliceRun`'s shared capture (serving eight tests),
        // `SessionRun`'s shared capture (twelve), the three `BrowserIdleTimerTests`
        // arms that drive a live browser, `FirstRunProvisioningTests`, and
        // `BrowserContainmentTests`' Chromium arm. Everything else that looks
        // like a browser test drives a FAKE child through `RigSessionEnvironment`
        // and starts nothing. Not one of those seven is in this test's constraint
        // group -- the eighth real launcher, `StraySweepTests`' interactive-session
        // arm, is -- and the suite runs four tests at a time. So any Chromium
        // coming up during the ~600 ms this test is measuring made the difference
        // non-empty and failed an assertion about Firefox for a reason that had
        // nothing to do with Firefox.
        //
        // Measured 2026-08-17, on an unmodified tree: two failures in five full
        // runs, and with the pids resolved to image paths the intruder was
        // literally `…\browsers\chromium-1237\chrome-win64\chrome.exe` -- once as
        // a single arrival, once as five at a time, which is a Chromium tree
        // coming up. The file's own note here used to read "that one caught a
        // Firefox another test in this file was launching in parallel, and the
        // tests that start one are serialised now"; that was true and it was only
        // half the set. Serialising the Firefox launchers never constrained the
        // Chromium ones, and nothing should: they are unrelated to this
        // assertion, and pulling seven more launch sites into a `NotInParallel`
        // group to fix a Firefox test would cost the suite its parallelism to
        // answer a question it should not have been asking.
        //
        // So the two readings now divide honestly. In-job: any image, exact,
        // catches anything this test started. Machine-wide: Firefox only, catches
        // the one thing the job could miss -- a Firefox that escaped it. The
        // developer's own Firefox runs from `C:\Program Files` and cannot match a
        // path under BrowserAI's browsers root.
        //
        // ⚠️ AND SCOPED TO A DIRECT CHILD OF THIS PROCESS, which is what took this
        // test out of the `stray-sweep` group on 2026-08-17. Narrowing the image
        // match to `firefox.exe` closed the Chromium half of the race and left the
        // Firefox half: the two other tests that start a real Firefox could still
        // falsify a machine-wide reading, so all three were serialised -- and that
        // chain, 13.05 s of which is one containment test, WAS the suite's whole
        // critical path at 20.4 s of a 20.6 s run.
        //
        // The preflight is a static call on this thread. Anything it launched
        // would be a DIRECT CHILD of the test host, because it has no node child
        // and no BrowserAI to launch one through. Every other Firefox in this
        // suite is a child of a `node.exe`, which is itself a child of a probe or
        // of the rig -- never of this process. So the parent pid is an exact
        // discriminator: it cannot miss what this test could produce, and it
        // cannot see what another test produces.
        //
        // Matched by full image path against the binary BrowserAI provisioned --
        // never by image name, which on this machine would name dozens of the
        // developer's own browser. Reported with that path rather than as a bare
        // pid, because by the time anyone reads the failure the process is gone
        // and a number names nothing.
        var appeared = OurFirefoxProcesses()
            .Where(process => !firefoxBefore.Contains(process.ProcessId))
            .Where(process => ParentProcess.IdOf(process.ProcessId) == Environment.ProcessId)
            .Select(process => $"{process.ProcessId.ToString(CultureInfo.InvariantCulture)} {process.ImagePath}")
            .ToList();

        await Assert.That(string.Join(", ", appeared)).IsEmpty();

        // And no window appeared for one. Every window that is new since the
        // snapshot is resolved to its owner, and none of them may be one of our
        // Firefox processes -- which is what a profile dialog would be. A bare
        // window count would be flaky on a live desktop and would prove less;
        // scoping the owners to the browsers root rather than to Firefox would
        // reintroduce the same Chromium race the reading above just lost, and
        // leaving them unscoped by parent would reintroduce the Firefox half.
        var ours = OurFirefoxProcesses()
            .Where(process => ParentProcess.IdOf(process.ProcessId) == Environment.ProcessId)
            .Select(process => process.ProcessId)
            .ToHashSet();

        var newWindowsOfOurs = TopLevelWindows.All()
            .Where(window => !windowsBefore.Contains(window))
            .Where(window => ours.Contains(TopLevelWindows.ProcessIdOf(window)))
            .ToList();

        await Assert.That(newWindowsOfOurs.Count).IsEqualTo(0);

        // Nothing was even prepared: the guard runs before the config is
        // written, so a caller cannot be left with a file describing a launch
        // that never happened.
        var configFile = Path.Combine(scratch.Path, "playwright-mcp.json");
        await Assert.That(File.Exists(configFile)).IsFalse();

        // Recorded rather than skipped: the half above needs no payload and is
        // worth running without one, but a run that took the shorter path must
        // still say so in the coverage block -- and must fail a release run.
        if (SuiteEnvironment.HasRepositoryPayload())
        {
            // The same refusal through the function every child launch actually
            // passes through, so the guard is proven where it lives rather than
            // only where it is convenient to call.
            var thrown = Assert.Throws<FirefoxProfileLockedException>(() => ChildLaunch.Create(
                RepositoryPayload.Layout,
                BrowserAiPaths.BrowsersDirectory,
                scratch.Path,
                configFile,
                config));

            await Assert.That(thrown!.Message).Contains("is held open by another process");
            await Assert.That(File.Exists(configFile)).IsFalse();
        }

        // The control, and without it this test passes for a generator that
        // refuses everything. The holder is terminated by the (pid, creation
        // time) pair recorded when it was started -- never by anything that
        // could name another process.
        ProcessIdentity.Terminate(holder.Id, holderCreated);
        await WaitUntilFreeAsync(profile, TestDefaults.ProcessHang);

        await Assert.That(FirefoxProfileLockedException.For(config)).IsNull();

        if (SuiteEnvironment.HasRepositoryPayload())
        {
            var options = ChildLaunch.Create(
                RepositoryPayload.Layout,
                BrowserAiPaths.BrowsersDirectory,
                scratch.Path,
                configFile,
                config);

            await Assert.That(options.Command).IsEqualTo(RepositoryPayload.Layout.NodeExecutable);
            await Assert.That(File.Exists(configFile)).IsTrue();
        }

        Record("preflight", new JsonObject
        {
            ["refusalMilliseconds"] = clock.Elapsed.TotalMilliseconds,
            ["modalWouldHaveBlockedMilliseconds"] = ModalTimeout.TotalMilliseconds,
            // Named for what it counts. It used to say "browserProcesses" while
            // counting everything under the browsers root, Chromium included,
            // which is the reading this test stopped making.
            ["firefoxProcessesStarted"] = appeared.Count,
            ["windowsOfOursThatAppeared"] = newWindowsOfOurs.Count,
            ["holderPid"] = holder.Id,
        });
    }

    /// <summary>
    /// A <c>parent.lock</c> that exists and is not held attributes nothing, and
    /// the developer's own Firefox is attributed to none of our sessions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The negative subject is real rather than synthesised.</b> This machine
    /// runs the maintainer's own Firefox out of <c>C:\Program Files</c> — dozens
    /// of processes, real profiles, real windows, a live <c>parent.lock</c>.
    /// That is a far stronger foreign browser than anything a test could plant:
    /// it exercises the Restart Manager path for real and then has to be
    /// rejected by the image-path guard, rather than failing the first filter
    /// and never reaching the second.
    /// </para>
    /// <para>
    /// ⚠️ <b>The cost of the conditional arm, stated rather than hidden.</b> On
    /// a machine with no other Firefox running, the foreign half proves nothing
    /// and says so through the recorded census. The unconditional half — a
    /// session's own unheld lock file attributing nobody — runs everywhere.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AnUnheldLockAttributesNobodyAndAForeignFirefoxIsAttributedToNoSession()
    {
        using var scratch = ScratchDirectory.Create("firefox-attribution-negative");

        var session = NewSession(scratch, "quiet");
        var profile = Path.Combine(session.FullPath, SessionLayout.ProfileFolderName);

        _ = Directory.CreateDirectory(profile);

        // A profile that has been used and closed. Firefox NEVER deletes
        // parent.lock -- it reads the mtime to detect a startup crash -- so this
        // is the ordinary state of every session between runs, and a check on
        // existence rather than on the live handle would refuse all of them.
        await File.WriteAllTextAsync(FirefoxProfile.LockFileIn(profile), string.Empty);

        await Assert.That(File.Exists(FirefoxProfile.LockFileIn(profile))).IsTrue();
        await Assert.That(FirefoxProfile.HoldersOf(profile).Count).IsEqualTo(0);
        await Assert.That(FirefoxProfile.Inspect(profile).MayLaunch).IsTrue();

        // A profile that has never been used at all.
        var fresh = Path.Combine(scratch.Path, "never-used");
        _ = Directory.CreateDirectory(fresh);

        await Assert.That(FirefoxProfile.HoldersOf(fresh).Count).IsEqualTo(0);
        await Assert.That(FirefoxProfile.Inspect(fresh).MayLaunch).IsTrue();

        // The real foreign browser. Its profiles are read-only inputs here: the
        // Restart Manager is asked who holds each lock, and NOTHING opens,
        // writes or touches any of them.
        var foreignProfiles = ForeignFirefoxProfiles();
        var foreignHolders = new List<FileHolder>();
        var queries = Stopwatch.StartNew();

        foreach (var foreign in foreignProfiles)
        {
            foreignHolders.AddRange(FirefoxProfile.HoldersOf(foreign));
        }

        queries.Stop();

        // ⚠️ What the Restart Manager costs, recorded because the sweep pays it
        // per session directory and this is a machine with dozens of live
        // Firefox processes for it to walk. A query that costs seconds is a
        // design constraint rather than a detail: the sweep runs at every
        // BrowserAI startup, and ~100 of those are a normal working day.
        var perQuery = foreignProfiles.Count is 0
            ? 0
            : queries.Elapsed.TotalMilliseconds / foreignProfiles.Count;

        var ours = BrowserProcesses.RunningFrom(BrowserAiPaths.BrowsersDirectory)
            .Select(process => process.ProcessId)
            .ToHashSet();

        foreach (var holder in foreignHolders)
        {
            // Guard one: it is not running a binary BrowserAI provisioned, so it
            // is not a candidate and no amount of attribution could make it one.
            await Assert.That(ours.Contains(holder.ProcessId)).IsFalse();
        }

        // Guard two: no session of ours claims any of them. Asked the way the
        // sweep asks it -- of our own session's lock -- rather than by comparing
        // paths.
        var claimed = FirefoxProfile.HoldersOf(profile).Select(holder => holder.ProcessId).ToHashSet();

        foreach (var holder in foreignHolders)
        {
            await Assert.That(claimed.Contains(holder.ProcessId)).IsFalse();
        }

        Record("attribution-negative", new JsonObject
        {
            ["millisecondsPerRestartManagerQuery"] = perQuery,
            ["foreignProfilesExamined"] = foreignProfiles.Count,
            ["foreignHoldersFound"] = foreignHolders.Count,
            ["foreignHolderPids"] = new JsonArray([.. foreignHolders.Select(holder => (JsonNode)holder.ProcessId)]),
            ["holdersThatWereOurs"] = 0,
            ["holdersAttributedToOneOfOurSessions"] = 0,
        });
    }

    /// <summary>
    /// A real Firefox, launched the way the product launches one: attributed to
    /// its own session through <c>RmGetList</c>, not registered for restart, and
    /// swept correctly at both ends of the ownership test.
    /// </summary>
    [Test]
    // One key, two reasons, because a test may carry only one: this pass sweeps
    // (so it must not run beside another sweep) and it launches a real Firefox
    // (so it must not run beside the preflight test, which asserts that no
    // Firefox appeared while it ran). The sweep group is the wider of the two,
    // so every Firefox test joins it.
    [NotInParallel("stray-sweep")]
    public async Task AFirefoxWeLaunchedIsAttributedToItsSessionAndIsNotRegisteredForRestart()
    {
        // ⚠️ The cost of the alternative, stated rather than hidden: with no
        // payload or no provisioned Firefox this arm proves nothing, and the
        // guarantee would rest on the recorded measurement in
        // kb/chromium/resurrection.md alone. So it reports as SKIPPED rather
        // than as a pass, and a release run refuses outright.
        SuiteEnvironment.RequireProvisionedFirefox();

        using var scratch = ScratchDirectory.Create("firefox-attribution");

        // Two sessions: one this Firefox will run in, and one that stands beside
        // it with a lock file and no browser. The second is what makes
        // "attributed to its own session" mean something more than "attributed
        // to the only session there is".
        var session = NewSession(scratch, "driven");
        var bystander = NewSession(scratch, "bystander");
        var profile = Path.Combine(session.FullPath, SessionLayout.ProfileFolderName);
        var bystanderProfile = Path.Combine(bystander.FullPath, SessionLayout.ProfileFolderName);

        _ = Directory.CreateDirectory(bystanderProfile);
        await File.WriteAllTextAsync(FirefoxProfile.LockFileIn(bystanderProfile), string.Empty);

        var config = BrowserConfiguration.ForSession(session, headed: false, ProvisionedBrowsers.Firefox, tracing: false, BrowserConfiguration.DefaultConsoleLevel);
        var configFile = Path.Combine(scratch.Path, "playwright-mcp.json");

        // The product's own launch funnel, which also runs the preflight against
        // a free profile -- so this test is the other half of the one above.
        var options = ChildLaunch.Create(
            RepositoryPayload.Layout,
            BrowserAiPaths.BrowsersDirectory,
            scratch.Path,
            configFile,
            config);

        // The hand-written raw client: no SDK and no product protocol type
        // between the assertion and the wire, and its job object is what makes a
        // failed assertion below unable to leave a browser running.
        await using var child = RawStdioClient.Start(
            options.Command,
            options.Arguments,
            options.WorkingDirectory,
            options.Environment,
            LaunchPatience);

        _ = await child.InitializeAsync("2025-11-25");

        var navigated = await child.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = "data:text/html,<h1>ok</h1>" },
        });

        // A browser that never came up would satisfy every "nothing is
        // registered" assertion below vacuously.
        await Assert.That((bool?)navigated["isError"]).IsNotEqualTo(true);

        var inTheJob = child.JobProcessIds().ToHashSet();
        var browsers = BrowserProcesses.RunningFrom(BrowserAiPaths.BrowsersDirectory)
            .Where(process => inTheJob.Contains(process.ProcessId))
            .ToList();

        await Assert.That(browsers.Count).IsGreaterThan(0);
        await Assert.That(browsers.All(process => string.Equals(process.ImagePath, BrowserAiPaths.FirefoxExecutable, StringComparison.OrdinalIgnoreCase))).IsTrue();

        // ⚠️ Restart registration, asked of the live processes. Upstream writes
        // firefoxUserPrefs into the profile's user.js BEFORE the browser starts,
        // so the preference is in force at the moment Firefox decides whether to
        // register -- and the control for this assertion is
        // `BrowserContainmentTests`, which launches Firefox from a hand-written
        // config with no preference and measures exactly one registration.
        var registered = browsers
            .Select(process => (process.ProcessId, process.ImagePath, Result: RestartRegistration.Of(process.ProcessId)))
            .Where(entry => entry.Result is not RestartRegistration.NotRegistered)
            .ToList();

        var offenders = registered.Select(entry =>
            $"pid {entry.ProcessId.ToString(CultureInfo.InvariantCulture)} answered 0x{entry.Result:X8} rather than ERROR_NOT_FOUND: {entry.ImagePath}");

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // ⚠️ The preference reached the CHILD, asked of the child's own resolved
        // configuration rather than of the profile on disk. Measured 2026-08-16
        // and it is not where this test first looked: upstream writes
        // firefoxUserPrefs into `user.js` only on the **BiDi** Firefox path, and
        // the classic one -- which is what `@playwright/mcp` takes -- delivers
        // them over juggler as `Browser.enable { userPrefs }` instead. There is
        // no `user.js` in the profile at all. `loadConfig` is a bare JSON.parse
        // with no schema validation, so a key upstream renamed would be dropped
        // in silence; this is the half of the assertion that would catch that,
        // and the registration count above is the half that catches the pref
        // being honoured but not working.
        var resolved = await ResolvedConfigAsync(child);
        var prefs = resolved["browser"]?["launchOptions"]?["firefoxUserPrefs"];

        await Assert.That(prefs).IsNotNull();
        await Assert.That((bool?)prefs![FirefoxProfile.RestartRegistrationPreference]).IsFalse();

        // Attribution. The Restart Manager names the holder of this session's
        // profile lock, and the pair (pid, start time) matches a process we
        // independently know is running our own binary.
        await Assert.That(File.Exists(FirefoxProfile.LockFileIn(profile))).IsTrue();

        var holders = FirefoxProfile.HoldersOf(profile);

        await Assert.That(holders.Count).IsGreaterThan(0);
        await Assert.That(holders.Any(holder =>
            browsers.Any(process =>
                process.ProcessId == holder.ProcessId
                && process.CreatedFileTime == holder.StartedFileTime))).IsTrue();

        // The bystander's lock exists and is held by nobody, so it attributes
        // nothing -- including this browser.
        var bystanderHolders = FirefoxProfile.HoldersOf(bystanderProfile);

        await Assert.That(bystanderHolders.Count).IsEqualTo(0);

        var attributedPid = holders.First(holder =>
            browsers.Any(process => process.ProcessId == holder.ProcessId && process.CreatedFileTime == holder.StartedFileTime)).ProcessId;

        // ---- The sweep, three ways -------------------------------------------

        using var indexRoot = ScratchDirectory.Create("firefox-sweep-index");
        var paths = new LocalAppDataPaths(indexRoot.Path);
        var index = new SessionIndex(paths, NullLogger.Instance);

        // 1. An index that does not know this session. The browser is foreign to
        //    every session there is, so it is attributed to none of them and
        //    reported rather than touched.
        index.Record(bystander);

        var unknown = await SweepAsync(index);

        await Assert.That(unknown.Candidates).IsGreaterThan(0);
        await Assert.That(unknown.AttributedByProfileLock).IsEqualTo(0);
        await Assert.That(unknown.Unattributable.Select(entry => entry.ProcessId)).Contains(attributedPid);
        await Assert.That(unknown.Terminated).IsEmpty();

        // 2. The session is known and its lock is held by this test, which is
        //    what a live session looks like. Attribution succeeds and the kill
        //    is refused on the ownership test -- R1.
        index.Record(session);

        var taken = SessionLock.TryAcquire(
            session,
            new SessionLockRequest { Browser = ProvisionedBrowsers.Firefox, Purpose = "the live session that owns this browser" },
            NullLogger.Instance);

        await Assert.That(taken.Acquired).IsNotNull();

        StraySweepResult live;

        using (taken.Acquired)
        {
            live = await SweepAsync(index);
        }

        await Assert.That(live.AttributedByProfileLock).IsGreaterThan(0);
        await Assert.That(live.Terminated.Select(entry => entry.ProcessId)).DoesNotContain(attributedPid);
        await Assert.That(live.Spared.Any(entry => entry.ProcessId == attributedPid)).IsTrue();
        await Assert.That(ProcessIdentity.IsAlive(attributedPid, holders.First(holder => holder.ProcessId == attributedPid).StartedFileTime)).IsTrue();

        // 3. The lock released -- a session whose BrowserAI died. Now the
        //    browser is a stray, and both guards agree: our binary, and a
        //    directory whose lock this sweeper can take itself.
        var swept = await SweepAsync(index);

        await Assert.That(swept.AttributedByProfileLock).IsGreaterThan(0);
        await Assert.That(swept.Terminated.Select(entry => entry.ProcessId)).Contains(attributedPid);

        // ⚠️ What the lock file does when its holder dies, re-measured here
        // rather than carried over, because the whole preflight rests on it.
        // [kb](../../kb/windows/detection.md) records that Firefox never deletes
        // parent.lock — unlike Chromium's lockfile, which the kernel removes on
        // FILE_FLAG_DELETE_ON_CLOSE — so its existence proves nothing and only a
        // sharing violation does. That is what this asserts: the file is still
        // there after the process holding it was terminated, which is precisely
        // the state an existence check would misread as "a browser is running".
        var lockSurvived = await LockFileStillThereAsync(profile, attributedPid, holders, TestDefaults.ProcessHang);

        Record("attribution", new JsonObject
        {
            ["browserProcessesInTheJob"] = browsers.Count,
            ["registeredForRestart"] = registered.Count,
            ["holdersOfTheSessionProfile"] = holders.Count,
            ["holdersOfTheBystanderProfile"] = bystanderHolders.Count,
            ["attributedByProfileLockWhenUnknown"] = unknown.AttributedByProfileLock,
            ["attributedByProfileLockWhenKnown"] = live.AttributedByProfileLock,
            ["terminatedOnceTheLockWasReleased"] = swept.Terminated.Count,
            ["lockFileSurvivedItsHoldersDeath"] = lockSurvived,
        });

        await Assert.That(lockSurvived).IsTrue();

        // And the file that outlived its holder does not lock anything: the
        // preflight reads the handle rather than the name, so the very next
        // launch on this profile is allowed. Without this the assertion above
        // would be indistinguishable from a design that refuses forever after
        // one crash.
        await Assert.That(FirefoxProfile.HoldersOf(profile).Count).IsEqualTo(0);
        await Assert.That(FirefoxProfile.Inspect(profile).MayLaunch).IsTrue();
    }

    /// <summary>
    /// Whether <c>parent.lock</c> is still on disk once its holder has gone.
    /// </summary>
    /// <remarks>
    /// <b>The wait is on the process, not on the lock, and that is a cost
    /// decision.</b> <c>TerminateProcess</c> returning is not proof that the
    /// kernel has torn the process's handles down, so something has to be
    /// waited on — but a Restart Manager query costs <b>638 ms</b> on this
    /// machine, so polling one every 100 ms would put ten seconds of
    /// machine-wide handle enumeration inside a suite whose in-process rigs
    /// assert ten-second budgets. Liveness is a handle check that costs
    /// microseconds and answers the same question.
    /// </remarks>
    private static async Task<bool> LockFileStillThereAsync(
        string profile,
        int processId,
        IReadOnlyList<FileHolder> holders,
        TimeSpan patience)
    {
        var clock = Stopwatch.StartNew();
        var created = holders.First(holder => holder.ProcessId == processId).StartedFileTime;

        while (clock.Elapsed < patience && ProcessIdentity.IsAlive(processId, created))
        {
            await Task.Delay(100);
        }

        return File.Exists(FirefoxProfile.LockFileIn(profile));
    }

    /// <summary>
    /// The child's own merged configuration, as <c>browser_get_config</c>
    /// reports it.
    /// </summary>
    /// <remarks>
    /// The tool's body is <c>JSON.stringify(context.config, null, 2)</c>, and
    /// upstream's response builder wraps every text section in a
    /// <c>### &lt;title&gt;</c> heading before it reaches the wire — so the JSON
    /// is cut out of the answer rather than parsed from it whole.
    /// </remarks>
    private static async Task<JsonObject> ResolvedConfigAsync(RawStdioClient child)
    {
        var answer = await child.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_get_config",
            ["arguments"] = new JsonObject(),
        });

        var text = string.Concat((answer["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));

        var start = text.IndexOf('{', StringComparison.Ordinal);
        var end = text.LastIndexOf('}');

        return (start >= 0 && end > start
            ? JsonNode.Parse(text[start..(end + 1)])?.AsObject()
            : null)
            ?? throw new InvalidOperationException($"browser_get_config returned no JSON object: {text}");
    }

    /// <summary>
    /// Runs one pass over the Firefox image only, retrying while some other
    /// process on the machine happens to be sweeping.
    /// </summary>
    /// <remarks>
    /// <b>The image list is the Firefox executable and nothing else</b>, so this
    /// sweep cannot form an opinion about any other browser on the machine —
    /// including a Chromium another test has open.
    /// </remarks>
    private static async Task<StraySweepResult> SweepAsync(SessionIndex index)
    {
        var deadline = DateTime.UtcNow + TestDefaults.ProcessHang;

        while (true)
        {
            var result = await OnItsOwnThreadAsync(() => new StraySweep(
                [BrowserAiPaths.FirefoxExecutable],
                index,
                NullLogger.Instance,
                [BrowserAiPaths.FirefoxExecutable]).Run());

            if (result.Outcome is not StraySweepOutcome.Skipped || DateTime.UtcNow > deadline)
            {
                return result;
            }

            await Task.Delay(25);
        }
    }

    /// <summary>
    /// Runs blocking work on a dedicated thread rather than on the pool.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>A sweep is several hundred blocking syscalls and one Restart
    /// Manager query per known session, and the pool grows by about one thread a
    /// second.</b> Run through <c>Task.Run</c>, it occupies a worker for as long
    /// as it takes and the suite's in-process rigs — which answer in
    /// milliseconds and assert budgets in tens of seconds — start failing
    /// somewhere else entirely. That is a measured failure in this repository
    /// rather than a precaution: <c>StraySweepTests</c> records a file-I/O loop
    /// on a pool thread taking <c>FakeChildHarnessTests</c> from 8 ms to 2.9 s.
    /// </remarks>
    /// <typeparam name="T">What the work produces.</typeparam>
    /// <param name="work">The blocking work.</param>
    /// <returns>Its result.</returns>
    private static Task<T> OnItsOwnThreadAsync<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(work());
            }
#pragma warning disable CA1031 // Whatever the work threw belongs to the awaiter, not to this thread.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                completion.SetException(failure);
            }
        })
        {
            IsBackground = true,
            Name = "firefox-test sweep",
        };

        thread.Start();
        return completion.Task;
    }

    private static SessionPath NewSession(ScratchDirectory scratch, string label)
    {
        var path = SessionPath.Resolve(Path.Combine(scratch.Path, label));
        SessionLayout.Create(path);

        var result = SessionLock.TryAcquire(
            path,
            new SessionLockRequest { Browser = ProvisionedBrowsers.Firefox, Purpose = $"firefox session {label}" },
            NullLogger.Instance);

        // Taken and released: what a BrowserAI that exited leaves behind is a
        // browserai.json with nothing holding it.
        result.Acquired?.Dispose();

        return path;
    }

    /// <summary>
    /// Every process running <b>the Firefox BrowserAI provisioned</b>, right now.
    /// </summary>
    /// <remarks>
    /// <b>The exact executable, not the browsers root.</b> The root also holds
    /// Chromium, and a prefix match on it answers a question about Chromium's
    /// helper processes while wearing the name of a Firefox check — which is the
    /// flake recorded on the assertion in
    /// <see cref="ThePreflightRefusesAHeldProfileBeforeAnyFirefoxOrWindowExists"/>.
    /// An image <i>name</i> is not an option and never was: it would name the
    /// dozens of the developer's own <c>firefox.exe</c> running out of
    /// <c>C:\Program Files</c>.
    /// </remarks>
    /// <returns>The matching processes.</returns>
    private static List<RunningImage> OurFirefoxProcesses() =>
        [.. BrowserProcesses.RunningFrom(BrowserAiPaths.BrowsersDirectory)
            .Where(process => string.Equals(process.ImagePath, BrowserAiPaths.FirefoxExecutable, StringComparison.OrdinalIgnoreCase))];

    /// <summary>The pids of <see cref="OurFirefoxProcesses"/>.</summary>
    /// <returns>The pids.</returns>
    private static List<int> OurFirefoxProcessIds() =>
        [.. OurFirefoxProcesses().Select(process => process.ProcessId)];

    /// <summary>
    /// Every Firefox profile on this machine that is <b>not</b> BrowserAI's.
    /// </summary>
    /// <remarks>
    /// Read-only: these directories belong to the person using the machine, and
    /// nothing here opens, writes or locks anything inside them. The Restart
    /// Manager is asked about the path; it does not open the file either.
    /// </remarks>
    private static List<string> ForeignFirefoxProfiles()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Mozilla",
            "Firefox",
            "Profiles");

        if (!Directory.Exists(root))
        {
            return [];
        }

        try
        {
            return [.. Directory.EnumerateDirectories(root)];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static async Task WaitUntilFreeAsync(string profile, TimeSpan patience)
    {
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < patience && !FirefoxProfile.Inspect(profile).MayLaunch)
        {
            await Task.Delay(25);
        }
    }

    /// <summary>
    /// The run's own numbers, written where a person re-establishing
    /// [rows 1 and 9](../../kb/re-verification.md) can read them.
    /// </summary>
    private static void Record(string label, JsonObject summary)
    {
        summary["utc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        var path = Path.Combine(RepositoryLayout.Root.FullName, ".work", $"firefox-{label}.json");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, summary.ToJsonString());
    }
}
