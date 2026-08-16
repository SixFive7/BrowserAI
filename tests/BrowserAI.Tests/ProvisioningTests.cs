// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text.Json.Nodes;
using BrowserAI.Protocol;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Tests;

/// <summary>
/// First-run provisioning: <c>init</c> does not wait for it, browser calls are
/// refused rather than blocked, and the same child works once it lands.
/// </summary>
/// <remarks>
/// <para>
/// <b>The installer is a double here and the download is real elsewhere.</b>
/// Everything these tests exercise is product code — the marker check, the
/// machine-wide mutex, the phase watcher, all three caps, the removal of a
/// partial tree, the refusal text, the recovery — and the one thing they cannot
/// say anything about is whether upstream's installer works. That is
/// <see cref="FirstRunProvisioningTests"/>, which runs against an empty browsers
/// root through the published binary and downloads for real.
/// </para>
/// <para>
/// <b>The refusal is the feature, not a limitation.</b> A blocking <c>init</c>
/// would hold a caller for minutes with nothing to read; these assert the other
/// shape — the session opens, the call is refused with a size and a route out,
/// and the very next attempt on the same session succeeds.
/// </para>
/// </remarks>
internal sealed class ProvisioningTests
{
    [Test]
    public async Task InitReturnsImmediatelyAndSaysTheBrowserIsDownloading()
    {
        await using var sessions = RigSessionEnvironment.Create(
            installer: (browser, root) => FakeInstaller.Succeeding(Path.Combine(root, RigSessionEnvironment.ChromiumDirectoryName), TimeSpan.FromSeconds(30)));

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var clock = Stopwatch.StartNew();
        var answer = await CallAsync(rig, SessionToolSurface.Init, Init(sessions, "downloading"));
        clock.Stop();

        // The whole design in one assertion: the installer this rig was given
        // takes thirty seconds and init did not wait for it.
        await Assert.That(clock.Elapsed).IsLessThan(TimeSpan.FromSeconds(10));
        await Assert.That((bool?)answer["isError"]).IsFalse();

        // The state is a word rather than a sentence to parse, and it is the
        // word §A names.
        await Assert.That(TextOf(answer)).Contains("browserProvisioning: downloading");
    }

    [Test]
    public async Task EveryBrowserToolIsRefusedWithRowSixIncludingTheConfigOne()
    {
        await using var sessions = RigSessionEnvironment.Create(
            Answering,
            installer: (browser, root) => FakeInstaller.Succeeding(Path.Combine(root, RigSessionEnvironment.ChromiumDirectoryName), TimeSpan.FromSeconds(30)));

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);
        var directory = Init(sessions, "refused-while-downloading")["directory"]!.GetValue<string>();

        _ = await CallAsync(rig, SessionToolSurface.Init, Init(sessions, "refused-while-downloading"));

        var navigate = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["url"] = "data:text/html,<h1>ok</h1>",
            ["session"] = directory,
        });

        var text = TextOf(navigate);

        await Assert.That((bool?)navigate["isError"]).IsTrue();

        // §H.4 row 6, and each clause is something a model acts on: the size it
        // is waiting for, where it is going, and that the same call will work.
        await Assert.That(text).Contains(BrowserProvisioner.FirstRunDownloadSize);
        await Assert.That(text).Contains("call the same tool again on the same session");
        await Assert.That(text).Contains(RigSessionEnvironment.ChromiumDirectoryName);

        // And the child was never asked, which is the difference between
        // refusing and forwarding into a launch that blocks for the whole
        // download.
        await Assert.That(sessions.SessionChildren.Sum(child => child.MethodsReceived.Count(method => method is "tools/call"))).IsEqualTo(0);

        // ⚠️ browser_get_config too, and this is a CORRECTION to §A rather than
        // an implementation choice. Measured 2026-08-16 @ 0.0.79, twice,
        // against the child directly: the tool resolves the browser executable
        // before it answers and fails `throwIfExecutableMissing` when the root
        // is empty. Letting it through would hand the caller upstream's "not
        // installed" advice -- provision it -- while provisioning is already
        // running, instead of row 6, which says how big the download is and that
        // the same call will work shortly.
        var config = await CallAsync(rig, "browser_get_config", new JsonObject { ["session"] = directory });

        await Assert.That((bool?)config["isError"]).IsTrue();
        await Assert.That(TextOf(config)).Contains(BrowserProvisioner.FirstRunDownloadSize);

        // Still nothing reached the child, which is what makes the refusal
        // cheaper as well as more useful than upstream's own error.
        await Assert.That(sessions.SessionChildren.Sum(child => child.MethodsReceived.Count(method => method is "tools/call"))).IsEqualTo(0);

        // And what DOES answer while the download runs is BrowserAI's own
        // tools, which is the property §A was reaching for.
        var listed = await CallAsync(rig, SessionToolSurface.List, new JsonObject { ["directory"] = sessions.Root });

        await Assert.That((bool?)listed["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(listed)).Contains(directory);
    }

    [Test]
    public async Task TheSameChildNavigatesOnceTheInstallLandsWithNoRestart()
    {
        // The install lands when this test says so and not a millisecond
        // earlier: with a duration instead, a fast machine finishes the download
        // before the first call arrives and the test fails on the refusal rather
        // than on the recovery it is about. Observed 2026-08-16.
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sessions = RigSessionEnvironment.Create(
            Answering,
            installer: (browser, root) => FakeInstaller.SucceedingWhenReleased(Path.Combine(root, RigSessionEnvironment.ChromiumDirectoryName), release.Task),
            timers: new ProvisioningTimers { Poll = TimeSpan.FromMilliseconds(20) });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);
        var directory = Init(sessions, "recovers-in-session")["directory"]!.GetValue<string>();

        _ = await CallAsync(rig, SessionToolSurface.Init, Init(sessions, "recovers-in-session"));

        var childrenAfterInit = sessions.SessionChildren.Count;
        var refused = await CallAsync(rig, "browser_navigate", Navigate(directory));

        await Assert.That((bool?)refused["isError"]).IsTrue();

        // The install lands. Nothing is restarted, nothing is re-created, and no
        // tool is called to make it happen.
        release.SetResult();
        _ = await sessions.Provisioner.WaitAsync(SessionManager.SupportedBrowser);

        var accepted = await CallAsync(rig, "browser_navigate", Navigate(directory));

        await Assert.That((bool?)accepted["isError"]).IsNotEqualTo(true);

        // ⚠️ The load-bearing half. "It works afterwards" would also be true of a
        // design that quietly replaced the child, and that design would lose the
        // session's whole browser state on every first run. No child was added,
        // and exactly one tools/call reached one of them -- the refused attempt
        // never left BrowserAI.
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(childrenAfterInit);
        await Assert.That(sessions.SessionChildren.Sum(child => child.MethodsReceived.Count(method => method is "tools/call"))).IsEqualTo(1);
    }

    [Test]
    public async Task AnInstallerThatExitsCleanWithoutTheMarkerIsAFailureAndLeavesNothingBehind()
    {
        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));
        using var scratch = ScratchDirectory.Create("provision-no-marker");

        var root = Path.Combine(scratch.Path, "browsers");
        var directory = Path.Combine(root, RigSessionEnvironment.ChromiumDirectoryName);

        using var provisioner = new BrowserProvisioner(RepositoryPayload.Layout, root, log)
        {
            StartInstaller = (_, _) => FakeInstaller.ExitingCleanWithoutTheMarker(directory),
        };

        var status = await provisioner.WaitAsync(SessionManager.SupportedBrowser);

        // Exit code 0 and a directory full of files is not an install. Upstream
        // never makes this check at launch, which is why a tree in this state
        // produces `spawn EFTYPE` forever and never re-downloads.
        await Assert.That(status.State).IsEqualTo(ProvisioningState.Failed);
        await Assert.That(status.Detail).Contains(BrowsersManifest.InstallationCompleteMarker);

        // And the partial tree is gone rather than left to be mistaken for one.
        await Assert.That(Directory.Exists(directory)).IsFalse();
    }

    [Test]
    public async Task TheAbsoluteCapStopsADownloadThatNeverProgresses()
    {
        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));
        using var scratch = ScratchDirectory.Create("provision-absolute-cap");

        var root = Path.Combine(scratch.Path, "browsers");
        FakeInstaller? started = null;

        using var provisioner = new BrowserProvisioner(
            RepositoryPayload.Layout,
            root,
            log,
            new ProvisioningTimers
            {
                AbsoluteCap = TimeSpan.FromMilliseconds(200),
                Poll = TimeSpan.FromMilliseconds(20),
            })
        {
            StartInstaller = (_, _) => started = FakeInstaller.Hanging(),
        };

        var status = await provisioner.WaitAsync(SessionManager.SupportedBrowser);

        await Assert.That(status.State).IsEqualTo(ProvisioningState.Failed);
        await Assert.That(status.Detail).Contains("cap");

        // The installer was STOPPED rather than merely abandoned. A watcher that
        // gave up without closing the job would leave a 200 MB download running
        // with nobody left to receive it — which is the exact shape a cap exists
        // to prevent, and it is invisible in the status.
        await Assert.That(started!.WasStopped).IsTrue();
    }

    [Test]
    public async Task TheExtractionCapStartsWhenTheBrowserDirectoryAppears()
    {
        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));
        using var scratch = ScratchDirectory.Create("provision-extraction-cap");

        var root = Path.Combine(scratch.Path, "browsers");
        var directory = Path.Combine(root, RigSessionEnvironment.ChromiumDirectoryName);
        FakeInstaller? started = null;

        using var provisioner = new BrowserProvisioner(
            RepositoryPayload.Layout,
            root,
            log,
            new ProvisioningTimers
            {
                // Deliberately far apart: an extraction cap that only fired
                // because the absolute one did would pass this test while
                // measuring nothing.
                AbsoluteCap = TimeSpan.FromMinutes(45),
                ExtractionCap = TimeSpan.FromMilliseconds(200),
                Poll = TimeSpan.FromMilliseconds(20),
            })
        {
            StartInstaller = (_, _) => started = FakeInstaller.StallingInExtraction(directory),
        };

        var status = await provisioner.WaitAsync(SessionManager.SupportedBrowser);

        await Assert.That(status.State).IsEqualTo(ProvisioningState.Failed);
        await Assert.That(status.Detail).Contains("Extracting");
        await Assert.That(started!.WasStopped).IsTrue();
        await Assert.That(Directory.Exists(directory)).IsFalse();
    }

    [Test]
    public async Task TwoProvisionersOverOneRootProduceOneInstall()
    {
        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));
        using var scratch = ScratchDirectory.Create("provision-one-install");

        var root = Path.Combine(scratch.Path, "browsers");
        var directory = Path.Combine(root, RigSessionEnvironment.ChromiumDirectoryName);

        // Counted per test rather than off FakeInstaller's own total: the suite
        // runs in parallel and other tests are creating doubles at the same
        // moment, so a global counter measures the run instead of this pair.
        var starts = 0;

        // Two provisioners is what two BrowserAI processes look like from the
        // machine's point of view: they share a browsers root and know nothing
        // about each other except through the Global\ mutex.
        using var first = new BrowserProvisioner(RepositoryPayload.Layout, root, log, Quick())
        {
            StartInstaller = (_, _) =>
            {
                _ = Interlocked.Increment(ref starts);
                return FakeInstaller.Succeeding(directory, TimeSpan.FromMilliseconds(300));
            },
        };

        using var second = new BrowserProvisioner(RepositoryPayload.Layout, root, log, Quick())
        {
            StartInstaller = (_, _) =>
            {
                _ = Interlocked.Increment(ref starts);
                return FakeInstaller.Succeeding(directory, TimeSpan.FromMilliseconds(300));
            },
        };

        var both = await Task.WhenAll(
            first.WaitAsync(SessionManager.SupportedBrowser),
            second.WaitAsync(SessionManager.SupportedBrowser));

        await Assert.That(both[0].State).IsEqualTo(ProvisioningState.Installed);
        await Assert.That(both[1].State).IsEqualTo(ProvisioningState.Installed);

        // One download, not two. The loser watches for the winner's marker
        // rather than fetching a second copy of 203.8 MB into the same
        // directory, which is precisely how a half-extracted tree acquires an
        // INSTALLATION_COMPLETE.
        await Assert.That(starts).IsEqualTo(1);
    }

    [Test]
    public async Task TheMutexNameIsPerBrowsersRootRatherThanPerBrowser()
    {
        // ⚠️ Found by the suite rather than by review. Keyed on the family
        // alone, every rig in this suite serialised against every other one, and
        // the losers sat watching for a marker in their OWN root that the winner
        // was never going to write — reported as "downloading" until the outer
        // deadline, on tests that had nothing to do with provisioning.
        var oneRoot = BrowserProvisioner.MutexNameFor(@"C:\a\browsers", "chromium");
        var anotherRoot = BrowserProvisioner.MutexNameFor(@"C:\b\browsers", "chromium");
        var anotherBrowser = BrowserProvisioner.MutexNameFor(@"C:\a\browsers", "firefox");

        await Assert.That(oneRoot).IsNotEqualTo(anotherRoot);
        await Assert.That(oneRoot).IsNotEqualTo(anotherBrowser);

        // Global\, with no Local\ fallback anywhere in this product: a
        // logon-session-scoped name would let a Remote Desktop session and the
        // console session install into one directory at once, each reporting
        // success.
        await Assert.That(oneRoot).StartsWith(LockScopes.GlobalPrefix);

        // Case-folded, because Windows paths are.
        await Assert.That(BrowserProvisioner.MutexNameFor(@"C:\A\Browsers", "Chromium")).IsEqualTo(oneRoot);
    }

    [Test]
    public async Task TheInstallerInheritsNoDownloadHostAndNoStallTimeoutOverride()
    {
        var environment = ChildEnvironment.Build(
            [new KeyValuePair<string, string>(ChildLaunch.BrowsersPathVariable, @"C:\browsers")]);

        // Upstream's per-socket stall timeout is 30 s and BrowserAI sets
        // nothing, so the figure stays upstream's rather than being duplicated
        // into a constant of ours that would drift the day theirs moved.
        await Assert.That(environment.ContainsKey(BrowserProvisioner.UpstreamStallTimeoutVariable)).IsFalse();
        await Assert.That(BrowserProvisioner.UpstreamStallTimeout).IsEqualTo(TimeSpan.FromSeconds(30));

        // And the mirror list survives: retries rotate through it, so a single
        // download host turns five attempts into five attempts at one dead
        // server.
        foreach (var name in ChildEnvironment.Refused.Where(name => name.Contains("DOWNLOAD_HOST", StringComparison.Ordinal)))
        {
            await Assert.That(environment.ContainsKey(name)).IsFalse();
        }

        await Assert.That(environment[ChildLaunch.BrowsersPathVariable]).IsEqualTo(@"C:\browsers");
    }

    [Test]
    public async Task ARelativeBrowsersRootIsRefusedRatherThanResolved()
    {
        using var log = LoggerFactory.Create(builder => _ = builder.AddProvider(new TUnitLoggerProvider()));

        // A relative PLAYWRIGHT_BROWSERS_PATH resolves against INIT_CWD —
        // inherited from whatever npm ancestor last ran — before the child's own
        // directory, so it lands a 430 MiB tree somewhere nobody chose and
        // reports nothing.
        var refused = Assert.Throws<ArgumentException>(() =>
        {
            using var never = new BrowserProvisioner(RepositoryPayload.Layout, "browsers", log);
        });

        await Assert.That(refused!.Message).Contains("INIT_CWD");
    }

    /// <summary>
    /// Programs the double to answer the two tools these tests call, because it
    /// answers only tools a test asked for.
    /// </summary>
    /// <remarks>
    /// Both results are deliberately trivial: what is under test is whether the
    /// call reaches the child at all, and a rich payload would only make the
    /// failure harder to read.
    /// </remarks>
    private static void Answering(FakePlaywrightChild child)
    {
        child.Tools["browser_navigate"] = new FakeToolBehaviour
        {
            RawResult = """{"content":[{"type":"text","text":"Page URL: data:text/html,<h1>ok</h1>"}],"isError":false}""",
        };

        child.Tools["browser_get_config"] = new FakeToolBehaviour
        {
            RawResult = """{"content":[{"type":"text","text":"### Config the child resolved"}],"isError":false}""",
        };
    }

    private static ProvisioningTimers Quick() => new()
    {
        Poll = TimeSpan.FromMilliseconds(20),
        OuterDeadline = TimeSpan.FromSeconds(30),
    };

    private static JsonObject Init(RigSessionEnvironment sessions, string name) => new()
    {
        ["directory"] = Path.Combine(sessions.Root, name),
        ["purpose"] = "a session created while the browser is still downloading",
        ["mode"] = "headless",
    };

    private static JsonObject Navigate(string directory) => new()
    {
        ["url"] = "data:text/html,<h1>ok</h1>",
        ["session"] = directory,
    };

    private static async Task<JsonObject> CallAsync(McpTestHarness rig, string tool, JsonObject arguments) =>
        await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));
}
