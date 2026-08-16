// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text.Json.Nodes;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The empty-root run: a browsers directory with nothing in it, a real download,
/// and the same child navigating afterwards.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one test that downloads, and it is the reason the rest may
/// not.</b> Every other provisioning assertion in this suite runs against a
/// double, which can say nothing about whether upstream's installer works, which
/// mirror answers, whether the revision in the payload's <c>browsers.json</c>
/// still resolves, or whether the marker lands where BrowserAI looks for it.
/// Only a run against an empty root can, and the maintainer's decision of
/// 2026-08-16 is that provisioning happens for real rather than being seeded from
/// the spike leftovers under <c>%LOCALAPPDATA%\ms-playwright</c>.
/// </para>
/// <para>
/// <b>It is driven through the published binary rather than in process</b>,
/// because the property being proven is what a <i>caller</i> experiences: an
/// <c>init</c> that answers at once, a browser call refused with a size and a
/// route out, and the same session — same child, no restart — navigating once
/// the install lands.
/// </para>
/// <para>
/// <b>The download costs about 204 MB and, on the maintainer's link, about
/// twelve seconds.</b> That is stated rather than hidden: it is the price of the
/// only evidence there is that the batteries-included premise is alive.
/// </para>
/// </remarks>
internal sealed class FirstRunProvisioningTests
{
    /// <summary>
    /// How long the whole first-run sequence gets. Generous on purpose: the
    /// arithmetic for a 1 Mbps link is 27 minutes, and a deadline that fires on
    /// a slow connection would report as a product defect.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(45);

    [Test]
    public async Task AnEmptyBrowsersRootDownloadsAndTheSameChildThenNavigates()
    {
        if (!PublishedSlice.IsPresent)
        {
            await Assert.That(PublishedSlice.IsAbsentAsAWhole).IsTrue();
            return;
        }

        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create("first-run");

        // A BrowserAI whose whole app root is new, so its browsers directory is
        // empty in the only way that counts: nothing has ever been installed
        // there and no marker exists to short-circuit the check.
        var appRoot = Path.Combine(scratch.Path, "app-root");
        var browsers = Path.Combine(appRoot, "browsers");
        var session = Path.Combine(scratch.Path, "first-run-session");

        _ = Directory.CreateDirectory(appRoot);

        await Assert.That(Directory.Exists(browsers)).IsFalse();

        var environment = PublishedSlice.InheritedEnvironment();
        environment[BrowserAiPaths.AppRootOverride] = appRoot;

        await using var client = RawStdioClient.Start(
            PublishedSlice.Executable,
            [],
            scratch.Path,
            environment);

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);

        var clock = Stopwatch.StartNew();

        var init = await CallAsync(client, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = session,
            ["purpose"] = "the first-run session, created before any browser exists",
            ["mode"] = "headless",
        });

        var initElapsed = clock.Elapsed;

        // ⚠️ The bullet this whole step turns on. A 204 MB download is running
        // and init answered anyway; if it had waited, this number would be the
        // download's.
        await Assert.That(initElapsed).IsLessThan(TimeSpan.FromSeconds(20));
        await Assert.That((bool?)init["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(init)).Contains("browserProvisioning: downloading");

        // A browser-needing call is refused rather than hanging, and the refusal
        // is §H.4 row 6 -- with the size, so a caller can decide what waiting
        // costs it.
        var refused = await CallAsync(client, "browser_navigate", new JsonObject
        {
            ["url"] = SliceRun.TargetUrl,
            [SessionToolSurface.SessionParameter] = session,
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).Contains(BrowserProvisioner.FirstRunDownloadSize);

        // ⚠️ browser_get_config is refused as well, and that is a CORRECTION to
        // §A found by this very test. It said the tool keeps working; measured
        // 2026-08-16 @ 0.0.79 against the child directly, twice, it does not --
        // it resolves the browser executable before answering. So row 6 covers
        // it too, and the property §A wanted is delivered by BrowserAI's own
        // tools below.
        var config = await CallAsync(client, "browser_get_config", new JsonObject
        {
            [SessionToolSurface.SessionParameter] = session,
        });

        await Assert.That((bool?)config["isError"]).IsTrue();
        await Assert.That(TextOf(config)).Contains(BrowserProvisioner.FirstRunDownloadSize);

        // What does keep working: the session is listable, resumable and
        // re-purposable throughout, because none of those needs a browser.
        var listed = await CallAsync(client, SessionToolSurface.List, new JsonObject
        {
            ["directory"] = scratch.Path,
        });

        await Assert.That((bool?)listed["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(listed)).Contains(session);

        // Now wait for the real thing: the marker upstream writes last, into the
        // directory the payload's own browsers.json names.
        var installed = Path.Combine(browsers, $"chromium-{BrowserAiPaths.ChromiumRevision}");
        var landed = await WaitForMarkerAsync(installed, Patience);

        await Assert.That(landed).IsTrue();

        // ⚠️ In-session recovery, and this is the assertion the plan asks for by
        // name. Same session, same child, no restart and no second init: the
        // very call that was refused above now succeeds.
        var navigate = await CallAsync(client, "browser_navigate", new JsonObject
        {
            ["url"] = SliceRun.TargetUrl,
            [SessionToolSurface.SessionParameter] = session,
        });

        await Assert.That((bool?)navigate["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(navigate)).Contains("Page URL: data:text/html");

        // And what landed is what the payload pins, not something that happened
        // to be lying around: ffmpeg and winldd come with it on Windows, and the
        // headless shell deliberately does not.
        await Assert.That(File.Exists(Path.Combine(installed, "chrome-win64", "chrome.exe"))).IsTrue();
        await Assert.That(Directory.EnumerateDirectories(browsers, "ffmpeg-*").Any()).IsTrue();
        await Assert.That(Directory.EnumerateDirectories(browsers, "chromium_headless_shell-*").Any()).IsFalse();

        _ = await client.CloseAndWaitForExitAsync(TimeSpan.FromSeconds(30));
    }

    private static async Task<bool> WaitForMarkerAsync(string directory, TimeSpan patience)
    {
        var marker = Path.Combine(directory, BrowsersManifest.InstallationCompleteMarker);
        var waited = Stopwatch.StartNew();

        while (waited.Elapsed < patience)
        {
            if (File.Exists(marker))
            {
                return true;
            }

            await Task.Delay(250);
        }

        return false;
    }

    private static async Task<JsonObject> CallAsync(RawStdioClient client, string tool, JsonObject arguments)
    {
        var envelope = await client.EnvelopeAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

        return envelope["result"]?.AsObject()
            ?? throw new InvalidOperationException($"'{tool}' answered with a JSON-RPC error: {envelope.ToJsonString()}");
    }

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));
}
