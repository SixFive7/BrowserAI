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
    /// The front door, end to end, against a real Firefox: create, navigate,
    /// take an artifact, destroy.
    /// </summary>
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
            ["mode"] = "headless",
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

        // An artifact, routed by BrowserAI into the session's own output tree.
        // No `filename`, which is upstream's own condition for returning the
        // image in the answer as well as writing it.
        var screenshot = await client.EnvelopeAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_take_screenshot",
            ["arguments"] = new JsonObject { [SessionToolSurface.SessionParameter] = session },
        });

        await Assert.That((bool?)screenshot["result"]?["isError"]).IsNotEqualTo(true);

        var artifact = ArtifactPathIn(screenshot);

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
            ["directory"] = session,
        });

        await Assert.That((bool?)destroyed["isError"]).IsNotEqualTo(true);
        await Assert.That(Directory.Exists(session)).IsFalse();
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
    /// one.
    /// </summary>
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
            .IsEquivalentTo(ProvisionedBrowsers.Families);
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
            ["mode"] = "headless",
            ["browser"] = "webkit",
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();

        var text = TextOf(refused);

        foreach (var family in ProvisionedBrowsers.Families)
        {
            await Assert.That(text).Contains(family);
        }

        await Assert.That(text).Contains("Nothing was changed");

        // Case is normalised rather than refused: what is written to lock.json
        // and read back forever is the canonical spelling.
        var created = await CallAsync(rig.Client, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = Path.Combine(sessions.Root, "shouty"),
            ["purpose"] = "a family named in the wrong case",
            ["mode"] = "headless",
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

    /// <summary>
    /// The absolute path BrowserAI's own note names, out of a <c>tools/call</c>
    /// envelope.
    /// </summary>
    /// <remarks>
    /// The same reader <see cref="SliceRun"/> uses, and for the same reason: the
    /// generated name depends on the last URL and a per-stem counter, so a test
    /// that composed the path would assert its own arithmetic instead of the
    /// claim, which is that the path in the note is the path the file is at.
    /// </remarks>
    /// <param name="envelope">The whole response envelope.</param>
    /// <returns>The path, or an empty string when the note names none.</returns>
    private static string ArtifactPathIn(JsonObject envelope)
    {
        const string Marker = "  file: ";

        var text = string.Join(
            "\n",
            (envelope["result"]?["content"]?.AsArray() ?? [])
                .Where(block => (string?)block!["type"] == "text")
                .Select(block => (string?)block!["text"] ?? string.Empty));

        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith(Marker, StringComparison.Ordinal))
            {
                return line[Marker.Length..].Trim();
            }
        }

        return string.Empty;
    }
}
