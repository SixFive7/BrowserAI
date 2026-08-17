// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text.Json.Nodes;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The empty-root run: a browsers directory with nothing in it, a real install,
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
/// <para>
/// ⚠️ <b>It pays that price at most once an hour, and the two modes prove
/// different things.</b> Added 2026-08-17 on the maintainer's instruction —
/// <i>"only download once per hour. I don't want to hammer the servers"</i> —
/// because a suite that was run once a day is about to be run dozens of times.
/// <see cref="FirstRunCache"/> keeps the tree the last CDN run produced;
/// <see cref="FirstRunCache.Plan"/> decides which mode this run is in, and
/// <see cref="FirstRunCache.Record"/> puts the answer in the coverage block that
/// every run prints. <b>A release run always downloads</b>, so no release is cut
/// on cached evidence.
/// </para>
/// <list type="table">
/// <listheader>
/// <term>Asserted below</term>
/// <description>Cold (CDN) · Cached</description>
/// </listheader>
/// <item>
/// <term><c>init</c> answers at once and reports <c>downloading</c></term>
/// <description>real · real — the cached mode drives the <i>loser</i> of the
/// machine-wide mutex, which is a production path nothing else covers end to
/// end</description>
/// </item>
/// <item>
/// <term>Every browser tool is refused with the size and a route out</term>
/// <description>real · real</description>
/// </item>
/// <item>
/// <term>BrowserAI's own tools keep answering meanwhile</term>
/// <description>real · real</description>
/// </item>
/// <item>
/// <term>The same child navigates once the marker lands, no restart</term>
/// <description>real · real, against a real Chromium</description>
/// </item>
/// <item>
/// <term>The layout is what the payload pins, and no headless shell exists</term>
/// <description>real · <b>a byte copy of what a CDN run produced ≤ 1 h
/// ago</b></description>
/// </item>
/// <item>
/// <term>Upstream's installer works, the mirror answers, the revision resolves</term>
/// <description>real · <b>not exercised</b></description>
/// </item>
/// </list>
/// <para>
/// <b>So the last row is the whole cost, and it is stated rather than
/// discovered.</b> A cached run cannot tell you that Playwright's CDN is up,
/// that <c>cftUrl</c> still resolves, or that <c>install-browser --no-shell</c>
/// still does what its name says. It can tell you everything BrowserAI does with
/// the result, which is the larger half of this test and the half that changes
/// when our code changes.
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
    public async Task AnEmptyBrowsersRootIsProvisionedAndTheSameChildThenNavigates()
    {
        SuiteEnvironment.RequirePublishedSlice();

        PublishedSlice.EnsureFresh();

        // Decided before anything is started, because the decision changes what
        // has to be held before BrowserAI's first init.
        var plan = FirstRunCache.Plan();

        using var scratch = ScratchDirectory.Create("first-run");

        // A BrowserAI whose whole app root is new, so its browsers directory is
        // empty in the only way that counts: nothing has ever been installed
        // there and no marker exists to short-circuit the check.
        var appRoot = Path.Combine(scratch.Path, "app-root");
        var browsers = Path.Combine(appRoot, "browsers");
        var session = Path.Combine(scratch.Path, "first-run-session");

        _ = Directory.CreateDirectory(appRoot);

        await Assert.That(Directory.Exists(browsers)).IsFalse();

        // ⚠️ On a cached run the machine-wide provisioning mutex is taken FIRST,
        // and everything below then runs against BrowserAI's cross-process
        // path: it finds the mutex held, declines to start a second download of
        // the same 203.8 MB, and watches for the marker the holder will write.
        // The holder is this test, and what it writes is last hour's tree.
        //
        // Nothing about the assertions changes. What changes is who fills the
        // directory -- upstream's installer, or a copy -- which is exactly the
        // distinction the class remarks tabulate.
        using var elsewhere = plan.Source is FirstRunSource.Cache
            ? ProvisioningClaim.Take(browsers, SessionManager.SupportedBrowser)
            : null;

        if (elsewhere is not null)
        {
            // A claim that silently failed would let BrowserAI download while
            // this test believed it was reading a cache -- green, and 203.8 MB
            // heavier than the run says it is.
            await Assert.That(elsewhere.Held).IsTrue();
        }

        var environment = PublishedSlice.InheritedEnvironment();
        environment[BrowserAiPaths.AppRootOverride] = appRoot;

        await using var client = RawStdioClient.Start(
            PublishedSlice.Executable,
            [],
            scratch.Path,
            environment);

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);

        var startedUtc = DateTimeOffset.UtcNow;

        // ⚠️ MEASURED AND RECORDED, NEVER ASSERTED ON. The whole-run clock below
        // feeds the first-run cache's coverage row; nothing compares it to a
        // constant.
        var clock = Stopwatch.StartNew();

        var init = await CallAsync(client, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = session,
            ["purpose"] = "the first-run session, created before any browser exists",
            ["mode"] = "headless",
        });

        // ⚠️ The bullet this whole step turns on, and it is asserted on STATE
        // rather than on a stopwatch.
        //
        // Deleted 2026-08-18: `Assert.That(initElapsed).IsLessThan(20 s)`, with
        // the note "a 204 MB download is running and init answered anyway; if it
        // had waited, this number would be the download's". The state assertion
        // on the next line says the same thing and says it better: an init that
        // had waited for the download would report `ready`, not `downloading`, so
        // the word IS the proof that it did not wait. Twenty seconds, meanwhile,
        // is a number a starved machine reaches while the product is behaving
        // perfectly -- and this test runs beside 418 others.
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

        // On a cached run the tree arrives here, markers last, which is where a
        // real install's would have arrived. On a CDN run this is a no-op and
        // upstream's installer is already several seconds into the download.
        if (plan.Entry is { } cached)
        {
            FirstRunCache.SeedInto(browsers, cached);
        }

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

        // ⚠️ ffmpeg gets its own wait, because Chromium's marker says nothing
        // about it and asserting on it here was a race the suite lost.
        //
        // `browsers.json` lists chromium at index 0 and ffmpeg at index 4, and
        // upstream installs in registry order, one component at a time. The kb's
        // own phase boundaries, timestamped from the installer's output on a
        // ~300 Mbps link, are the measurement: chromium 0.3 s → 11.7 s, then
        // ffmpeg a further 0.5 s, then winldd 0.4 s
        // ([kb](../../kb/playwright/provisioning-and-timings.md#first-run-provisioning)).
        // So the marker waited on above lands at the exact instant ffmpeg's
        // download BEGINS, and the only slack this assertion ever had was
        // however long the navigation took. It held on a fast link and lost on a
        // slow one -- seen once in five runs, 2026-08-17 -- and it was a race
        // rather than a slow test: the fix is to wait for the thing being
        // asserted, not to give the whole sequence longer.
        //
        // It also makes the two assertions beneath it stronger rather than
        // merely later. ffmpeg is the LAST component this install fetches, so a
        // headless shell that was going to appear would already have appeared by
        // the time ffmpeg's marker lands.
        //
        // ⚠️ The ordering survives a cached run and is not weakened by it.
        // FirstRunCache.SeedInto copies every byte before it writes a single
        // marker, so chromium's marker still cannot precede ffmpeg's bytes; and
        // a cache carrying a chromium_headless_shell-* is refused outright, so
        // the negative assertion below cannot pass because the cache is missing
        // something rather than because --no-shell works.
        var ffmpeg = await WaitForAnyMarkerAsync(browsers, "ffmpeg-*", Patience);

        await Assert.That(ffmpeg).IsNotNull();

        // And what landed is what the payload pins, not something that happened
        // to be lying around: ffmpeg and winldd come with it on Windows, and the
        // headless shell deliberately does not.
        await Assert.That(File.Exists(Path.Combine(installed, "chrome-win64", "chrome.exe"))).IsTrue();
        await Assert.That(Directory.EnumerateDirectories(browsers, "chromium_headless_shell-*").Any()).IsFalse();

        _ = await client.CloseAndWaitForExitAsync(TestDefaults.ProcessHang);

        // Published only by the run that actually paid for the bytes, and after
        // the client is gone so nothing still holds a file in the tree. A cached
        // run deliberately touches nothing: the TTL runs from the download, so a
        // cache refreshed by use would never expire and the cold path would
        // never run again.
        var note = plan.Source is FirstRunSource.Cdn
            ? FirstRunCache.Publish(browsers, startedUtc, FirstRunCache.Root)
            : "The cached tree is untouched, so its hour still runs from the download that produced it.";

        FirstRunCache.Record(plan, clock.Elapsed, note);
    }

    /// <summary>
    /// Waits for <b>any</b> directory matching <paramref name="pattern"/> to
    /// carry the completion marker upstream writes last.
    /// </summary>
    /// <remarks>
    /// <b>The marker, not the directory.</b> A component's directory exists from
    /// the moment its archive starts extracting, so a check on the directory
    /// alone would swap one race for a narrower one. The pattern is a glob rather
    /// than a composed name because the revision is upstream's to move and this
    /// assertion is about the component being installed at all.
    /// </remarks>
    /// <param name="root">The browsers root.</param>
    /// <param name="pattern">The directory glob, for example <c>ffmpeg-*</c>.</param>
    /// <param name="patience">How long to wait.</param>
    /// <returns>The installed directory, or <see langword="null"/> if none arrived.</returns>
    private static async Task<string?> WaitForAnyMarkerAsync(string root, string pattern, TimeSpan patience)
    {
        var waited = Stopwatch.StartNew();

        while (waited.Elapsed < patience)
        {
            var landed = Directory.Exists(root)
                ? Directory.EnumerateDirectories(root, pattern)
                    .FirstOrDefault(directory => File.Exists(Path.Combine(directory, BrowsersManifest.InstallationCompleteMarker)))
                : null;

            if (landed is not null)
            {
                return landed;
            }

            await Task.Delay(250);
        }

        return null;
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
