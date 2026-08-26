// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Interop;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// Firefox as a browser a caller may <b>ask for</b>: the front door, the two
/// figures a refusal quotes, and the family argument
/// <c>browserai_reinstall_browser</c> gained the day a second tree became
/// possible.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the half <see cref="FirefoxTests"/> never covered.</b> That class
/// proves the locking design — the <c>parent.lock</c> preflight, Restart Manager
/// attribution, the restart-registration preference — by composing a Firefox
/// config in the test and launching the child through <c>ChildLaunch.Create</c>.
/// Every one of those assertions was true while <c>browserai_init</c> still
/// refused <c>browser: "firefox"</c>, because none of them goes through the
/// front door. The machinery was built and measured and had never once been
/// asked for by a caller.
/// </para>
/// <para>
/// ⚠️ <b>The end-to-end arm asserts the browser is Firefox by full image path,
/// and that assertion is what makes the rest of it mean anything.</b> A session
/// that silently ran Chromium against a Firefox-named record would navigate,
/// screenshot and destroy exactly as happily — success-shaped, wrong browser,
/// nothing anywhere saying so. Never by image name: the maintainer's own Firefox
/// runs from <c>C:\Program Files</c> on this machine, and every reading here is
/// scoped to the job this test owns as well as to the binary BrowserAI
/// provisioned.
/// </para>
/// <para>
/// <b>Not in the <c>stray-sweep</c> group, deliberately.</b> That key holds the
/// tests that <i>run a sweep</i>; this one runs none. Its Firefox is a
/// grandchild of a published <c>BrowserAI.exe</c> rather than a direct child of
/// the test host, so it cannot falsify
/// <c>FirefoxTests.ThePreflightRefusesAHeldProfileBeforeAnyFirefoxOrWindowExists</c>,
/// whose machine-wide reading is scoped to direct children for exactly this
/// reason.
/// </para>
/// </remarks>
internal sealed class FirefoxSessionTests
{
    /// <summary>The one argument the reinstall tool requires, so the assertion has no literal array in it.</summary>
    private static readonly string[] RequiredOnReinstall = ["browser"];

    /// <summary>A PNG's eight-byte signature, so a truncated screenshot cannot pass for one.</summary>
    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// How long a profile Firefox has finished with gets to become deletable.
    /// </summary>
    /// <remarks>
    /// <b>A hang detector, and nothing asserts on it.</b> The same value the two
    /// other real-browser teardowns use, for the same reason: what is being
    /// measured is whether the tree becomes deletable <i>at all</i>, and a slow
    /// machine may take as long as it likes to get there.
    /// </remarks>
    private static readonly TimeSpan TeardownPatience = TestDefaults.ProcessHang;

    /// <summary>
    /// The front door, end to end, against a real Firefox: create, navigate,
    /// take an artifact, destroy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The destroy arm asserts what <c>browserai_destroy</c> actually
    /// promises, and until 2026-08-19 it asserted something stronger that the
    /// product has never promised.</b> It required
    /// <c>Directory.Exists(session)</c> to be <see langword="false"/>. Destroy's
    /// survivor arm answers <i>"BUT N item(s) could not be removed"</i> and names
    /// them, because Windows will not unlink a file a browser is still mapping
    /// and the release lags the process by however long the kernel
    /// takes — so a legitimate outcome failed the test. It passed nine local
    /// runs and failed three consecutive CI runs on a four-core runner, against
    /// Firefox, the family slowest to let go of its profile. <b>The assertion was
    /// wrong rather than merely strict</b>, and a wrong assertion that only fires
    /// on a slower machine is the worst-shaped one there is: it reads as
    /// flakiness and gets a retry rather than a reader.
    /// </para>
    /// <para>
    /// <b>What replaced it is stronger, not weaker, and in four ways the old one
    /// could not catch.</b> The answer and the disk must <i>agree</i>: a destroy
    /// that leaves the tree standing and says nothing fails, exactly as before —
    /// but so does one that <i>reports</i> survivors it does not have, one whose
    /// survivor list is a count with nothing named under it, and one that names
    /// a path outside the directory it was given. The old assertion was blind to
    /// all four, because a gone directory satisfied it whatever the answer said.
    /// Then the two claims that answer makes are checked against the disk it
    /// made them about: the record really is gone, and what survived really was
    /// a handle on its way out rather than a leak nothing will ever release.
    /// </para>
    /// <para>
    /// <b>Deliberately not the sibling tests' shape.</b>
    /// <c>BrowserContainmentTests</c> and <c>BrowserIdleTimerTests</c> tear a
    /// real browser tree down with
    /// <see cref="ScratchDirectory.RemoveTreeWhenReleasedAsync"/> and assert the
    /// tree becomes deletable — a property of the browser's teardown, which is
    /// what those two tests are about. Used <i>instead</i> of the agreement check
    /// here it would be a weakening: the test's own delete loop would remove the
    /// tree, so a <c>browserai_destroy</c> that did nothing whatsoever and
    /// reported success would pass. It is used <b>as well</b>, for the one
    /// property it does hold to.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFirefoxSessionRunsFromInitThroughAnArtifactToDestroy()
    {
        SuiteEnvironment.RequirePublishedSlice();
        SuiteEnvironment.RequireProvisionedFirefox();

        using var scratch = ScratchDirectory.Create("firefox-front-door");

        // The job this client owns is the containment net: an assertion that
        // throws below closes it, and KILL_ON_JOB_CLOSE takes the browser with
        // it. Nothing in this file terminates anything by name.
        await using var client = RawStdioClient.Start(
            PublishedSlice.Executable,
            [],
            scratch.Path,
            PublishedSlice.InheritedEnvironment());

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);

        var session = Path.Combine(scratch.Path, "firefox-session");

        var created = await CallAsync(client, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = session,
            ["purpose"] = "the first session anybody ever asked Firefox for",
            ["browser"] = ProvisionedBrowsers.Firefox,
        });

        await Assert.That((bool?)created["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(created)).Contains(ProvisionedBrowsers.Firefox);

        // The record, read off disk rather than off the answer: `resume` reads
        // the family back out of this file, and an answer that says firefox over
        // a record that says chromium is the failure this whole feature is
        // arranged around.
        var record = SessionLock.ReadRecord(SessionPath.Resolve(session));

        await Assert.That(record).IsNotNull();
        await Assert.That(record!.Browser).IsEqualTo(ProvisionedBrowsers.Firefox);

        var navigated = await CallAsync(client, "browser_navigate", new JsonObject
        {
            ["url"] = SliceRun.TargetUrl,
            [SessionToolSurface.SessionParameter] = session,
            [SessionToolSurface.WhyParameter] = "the suite exercising this call",
        });

        await Assert.That((bool?)navigated["isError"]).IsNotEqualTo(true);

        // ⚠️ WHICH BROWSER ACTUALLY CAME UP. Scoped twice: to the processes
        // inside this client's own job, and to the full image path of the
        // Firefox BrowserAI provisioned. A Chromium started by any of the seven
        // other real-browser launch sites in this suite is outside the job; the
        // maintainer's own Firefox is outside the path.
        var inTheJob = client.JobProcessIds().ToHashSet();
        var browsers = BrowserProcesses.RunningFrom(BrowserAiPaths.BrowsersDirectory)
            .Where(process => inTheJob.Contains(process.ProcessId))
            .ToList();

        await Assert.That(browsers.Count).IsGreaterThan(0);

        var wrongFamily = browsers
            .Where(process => !string.Equals(process.ImagePath, BrowserAiPaths.FirefoxExecutable, StringComparison.OrdinalIgnoreCase))
            .Select(process => process.ImagePath);

        await Assert.That(string.Join(Environment.NewLine, wrongFamily)).IsEmpty();

        // The profile is Firefox's own, which is the other half of "this really
        // is a Firefox session": `parent.lock` is the file the preflight opens
        // and the file Playwright's own Chromium-only check never looks at.
        await Assert.That(File.Exists(FirefoxProfile.LockFileIn(Path.Combine(session, SessionLayout.ProfileFolderName)))).IsTrue();

        // An artifact, written by the child into the session's own output root.
        // No `filename`, which is upstream's own condition for returning the
        // image in the answer as well as writing it.
        var screenshot = await client.EnvelopeAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_take_screenshot",
            ["arguments"] = new JsonObject { [SessionToolSurface.SessionParameter] = session, [SessionToolSurface.WhyParameter] = "the suite exercising this call" },
        });

        await Assert.That((bool?)screenshot["result"]?["isError"]).IsNotEqualTo(true);

        var artifact = SliceRun.ArtifactPathIn(screenshot, Path.Combine(session, SessionLayout.OutputFolderName));

        await Assert.That(artifact).IsNotEmpty();
        await Assert.That(artifact.StartsWith(session, StringComparison.OrdinalIgnoreCase)).IsTrue();
        await Assert.That(File.Exists(artifact)).IsTrue();

        // A PNG rather than a file of some length: an empty or truncated
        // screenshot satisfies "the path exists" and is exactly the
        // success-shaped failure this suite is written against.
        var bytes = await File.ReadAllBytesAsync(artifact);

        await Assert.That(bytes.Length).IsGreaterThan(8);
        await Assert.That(bytes[..8]).IsEquivalentTo(PngSignature);

        var destroyed = await CallAsync(client, SessionToolSurface.Destroy, new JsonObject
        {
            ["why"] = "the suite exercising this call",
            ["directory"] = session,
        });

        // ⚠️ THE ANSWER, THE FLAG AND THE DISK MUST AGREE — IN BOTH DIRECTIONS,
        // and see `DestroyAnswer` for why this is not `Directory.Exists` is
        // false. The contract lives there rather than here so that this test and
        // the deterministic survivor in `SessionDestroyTests` cannot hold destroy
        // to two different promises — and so that the arm a fast machine never
        // reaches is exercised on every run by one that does.
        //
        // ⚠️ There is deliberately NO bare `isError` assertion beside this call
        // any more. Until 2026-08-19 this line required `isError` not to be true,
        // which against Firefox on a four-core runner asserts that the browser
        // let go of its profile in time — the same wrong-because-stronger
        // assertion, one layer along, that the remarks above are about. The flag
        // is now checked against what the answer itself says.
        await DestroyAnswer.AccountsForWhatItLeftAsync(TextOf(destroyed), (bool?)destroyed["isError"], session);

        // ⚠️ AND WHAT SURVIVED WAS A HANDLE ON ITS WAY OUT, NOT A LEAK. This is
        // the half the assertion above cannot make: destroy naming a survivor
        // honestly is correct, and a survivor nothing will ever release is a
        // session directory that stays on the caller's disk forever. The bound
        // is a hang detector -- the ordinary case returns on the first pass with
        // nothing to delete.
        var neverReleased = await ScratchDirectory.RemoveTreeWhenReleasedAsync(session, TeardownPatience);

        await Assert.That(string.Join(Environment.NewLine, neverReleased)).IsEmpty();
    }

    /// <summary>
    /// Every family this build provisions has a download size somebody measured.
    /// </summary>
    /// <remarks>
    /// <b>The mechanism the doc comment on
    /// <see cref="BrowserProvisioner.FirstRunDownloadSizes"/> promises.</b> The
    /// figure reaches a caller inside
    /// <see cref="SessionErrors.ProvisioningInProgress"/>, a sentence that reads
    /// as a measurement whatever is in it — so a third family added without one
    /// would quote another browser's number or a placeholder, and neither is
    /// distinguishable from a measurement by anyone reading the refusal. This is
    /// the same shape as the deny-by-default tool classification: a family the
    /// build does not know the cost of fails the build rather than the caller.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryFamilyThisBuildProvisionsHasAMeasuredDownloadSize()
    {
        var unmeasured = ProvisionedBrowsers.Families
            .Where(family => !BrowserProvisioner.FirstRunDownloadSizes.ContainsKey(family))
            .ToList();

        await Assert.That(string.Join(", ", unmeasured))
            .IsEmpty()
            .Because("a family with no measured download size quotes another browser's number in the refusal a caller reads. Measure it -- kb/playwright/provisioning-and-timings.md says how -- and never estimate it.");

        // The other direction: a figure for a family this build does not
        // provision is a measurement of nothing, and it is how a stale entry
        // survives a family being dropped.
        var orphaned = BrowserProvisioner.FirstRunDownloadSizes.Keys
            .Where(family => !ProvisionedBrowsers.Families.Contains(family, StringComparer.OrdinalIgnoreCase))
            .ToList();

        await Assert.That(string.Join(", ", orphaned)).IsEmpty();

        // Not vacuous: an empty family list would satisfy both halves above.
        await Assert.That(ProvisionedBrowsers.Families.Count).IsEqualTo(2);

        // And the two are different, which is the whole point of splitting the
        // const. A copy-paste that gave Firefox Chromium's string would pass
        // every assertion above.
        await Assert.That(BrowserProvisioner.DownloadSizeFor(ProvisionedBrowsers.Firefox))
            .IsNotEqualTo(BrowserProvisioner.DownloadSizeFor(ProvisionedBrowsers.Chromium));
    }

    /// <summary>
    /// Both families are offered on <c>init</c>, and the reinstall tool requires
    /// one of its own targets.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Corrected 2026-08-19 (previously both enums were asserted against
    /// <c>ProvisionedBrowsers.Families</c>).</b> The reinstall tool's argument
    /// gained a third value, <c>shared</c>, which is <c>ffmpeg</c> and
    /// <c>winldd</c> rather than a browser — so the two enums are now different
    /// lists and this test is the one that says so. Asserting them against the
    /// <i>same</i> list is what would have let <c>shared</c> reach
    /// <c>browserai_init</c> in the widening edit, and a session bound for life
    /// to a codec would have been green.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheAdvertisedSurfaceOffersBothFamiliesAndMakesReinstallNameOne()
    {
        // The rewrite is what a model receives, so the enum is read out of it
        // rather than off the class that declares it.
        var rewritten = SessionToolSurface.Rewrite([]);
        var authored = (rewritten["tools"]?.AsArray() ?? [])
            .ToDictionary(tool => (string)tool!["name"]!, tool => tool!.AsObject(), StringComparer.Ordinal);

        var init = authored[SessionToolSurface.Init]["inputSchema"]!["properties"]!["browser"]!;
        var offered = init["enum"]!.AsArray().Select(value => (string)value!).ToList();

        await Assert.That(offered).IsEquivalentTo(ProvisionedBrowsers.Families);

        var reinstall = authored[SessionToolSurface.ReinstallBrowser]["inputSchema"]!;
        var required = reinstall["required"]!.AsArray().Select(value => (string)value!).ToList();

        await Assert.That(required).IsEquivalentTo(RequiredOnReinstall);
        await Assert.That(reinstall["properties"]!["browser"]!["enum"]!.AsArray().Select(value => (string)value!))
            .IsEquivalentTo(ProvisionedBrowsers.ReinstallTargets);

        // And the two lists are asserted to DIFFER, so a future edit that made
        // them one again is red here rather than silently offering a codec as a
        // session's browser.
        await Assert.That(offered).DoesNotContain(ProvisionedBrowsers.Shared);
        await Assert.That(ProvisionedBrowsers.ReinstallTargets.Count).IsEqualTo(ProvisionedBrowsers.Families.Count + 1);
    }

    /// <summary>
    /// <c>init</c> refuses a family this build does not provision, and the
    /// refusal names the ones it does.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task InitRefusesAFamilyThisBuildDoesNotProvisionAndNamesTheOnesItDoes()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var refused = await CallAsync(rig.Client, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = Path.Combine(sessions.Root, "webkit-please"),
            ["purpose"] = "a browser nobody provisions",
            ["browser"] = "webkit",
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();

        var text = TextOf(refused);

        foreach (var family in ProvisionedBrowsers.Families)
        {
            await Assert.That(text).Contains(family);
        }

        await Assert.That(text).Contains("Nothing was changed");

        // Case is normalised rather than refused: what is written to browserai.json
        // and read back forever is the canonical spelling.
        var created = await CallAsync(rig.Client, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = Path.Combine(sessions.Root, "shouty"),
            ["purpose"] = "a family named in the wrong case",
            ["browser"] = "ChRoMiUm",
        });

        await Assert.That((bool?)created["isError"]).IsFalse();

        var record = SessionLock.ReadRecord(SessionPath.Resolve(Path.Combine(sessions.Root, "shouty")));

        await Assert.That(record!.Browser).IsEqualTo(ProvisionedBrowsers.Chromium);
    }

    /// <summary>
    /// <c>browserai_reinstall_browser</c> refuses a call that names no family.
    /// </summary>
    /// <remarks>
    /// <b>The settled no-arguments decision moved because its stated reason
    /// expired</b> — "there is nothing to name" was true of a build with one
    /// family — and this is the arm that holds the replacement to being a
    /// refusal rather than a default. A default here deletes and re-downloads a
    /// healthy tree and reports success while the broken one stays broken.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ReinstallRefusesACallThatNamesNoFamily()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var refused = await CallAsync(rig.Client, SessionToolSurface.ReinstallBrowser, []);

        await Assert.That((bool?)refused["isError"]).IsTrue();

        var text = TextOf(refused);

        await Assert.That(text).Contains("browser");

        // ⚠️ AND NOTHING WAS DELETED. A refusal that had already removed the
        // tree would satisfy the assertion above, and the tree here is the rig's
        // own seeded Chromium.
        await Assert.That(File.Exists(Path.Combine(sessions.ChromiumDirectory, BrowsersManifest.InstallationCompleteMarker))).IsTrue();
    }

    /// <summary>
    /// A family this build does not provision is refused by name, before
    /// anything is deleted.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ReinstallRefusesAFamilyThisBuildDoesNotProvision()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var refused = await CallAsync(rig.Client, SessionToolSurface.ReinstallBrowser, new JsonObject
        {
            ["browser"] = "webkit",
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).Contains("webkit");
        await Assert.That(File.Exists(Path.Combine(sessions.ChromiumDirectory, BrowsersManifest.InstallationCompleteMarker))).IsTrue();
    }

    private static async Task<JsonObject> CallAsync(RawStdioClient client, string tool, JsonObject arguments) =>
        await client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

    private static async Task<JsonObject> CallAsync(RawPipeClient client, string tool, JsonObject arguments) =>
        await client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

    private static string TextOf(JsonObject answer) =>
        string.Join(
            "\n",
            (answer["content"]?.AsArray() ?? [])
                .Where(block => (string?)block!["type"] == "text")
                .Select(block => (string?)block!["text"] ?? string.Empty));
}
