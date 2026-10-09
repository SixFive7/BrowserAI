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
/// 2026-08-16 is that provisioning happens for real instead of being seeded from
/// the spike leftovers under <c>%LOCALAPPDATA%\ms-playwright</c>.
/// </para>
/// <para>
/// <b>It is driven through the published binary and not in process</b>,
/// because the property being proven is what a <i>caller</i> experiences: an
/// <c>init</c> that answers at once, a browser call refused with a size and a
/// route out, and the same session -- same child, no restart -- navigating once
/// the install lands.
/// </para>
/// <para>
/// <b>The download costs about 204 MB and, on the maintainer's link, about
/// twelve seconds.</b> That is stated and not hidden: it is the price of the
/// only evidence there is that the batteries-included premise is alive.
/// </para>
/// <para>
/// ⚠️ <b>It pays that price at most once an hour, and the two modes prove
/// different things.</b> Added 2026-08-17 on the maintainer's instruction --
/// <i>"only download once per hour. I don't want to hammer the servers"</i> --
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
/// <term><c>init</c> answers at once and reports <c>provisioning</c></term>
/// <description>real · real -- the cached mode drives the <i>loser</i> of the
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
/// <b>So the last row is the whole cost, and it is stated, not
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

    /// <summary>
    /// The wait for the marker ends as soon as BrowserAI's log says the install gave
    /// up, and carries what the log said.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-08</b>, after the arm below waited 34 minutes on the gate of
    /// <c>f68ae4cf</c> for a marker an installer that had exited 1 at 15:48:40Z was
    /// never going to write. <b>Planted red</b> with the log reader answering
    /// <see langword="null"/> for every root.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheWaitForTheMarkerEndsWhenTheLogReportsAFailedInstall()
    {
        using var root = ScratchDirectory.Create("first-run-failed-wait");

        var logs = Directory.CreateDirectory(Path.Combine(root.Path, "logs"));
        var never = Path.Combine(root.Path, "browsers", "chromium-0");

        await Assert.That(ProvisioningFailureIn(root.Path)).IsNull();

        // The record's shape as the gate's own log carried it, category and event id included.
        await File.WriteAllTextAsync(
            Path.Combine(logs.FullName, "browserai-20261008-000.log"),
            "2026-10-08T15:48:40.2443340Z  made=2026-10-08T15:48:40.2442817Z  ERROR  pid=1@2  BrowserAI.Runtime.BrowserProvisioner[64]  The installer for chromium exited 1 without completing.\n");

        var failure = ProvisioningFailureIn(root.Path);

        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!).Contains("BrowserProvisioner[64]");

        var (landed, reported) = await WaitForMarkerAsync(never, TestDefaults.InProcessHang, () => ProvisioningFailureIn(root.Path));

        await Assert.That(landed).IsFalse();
        await Assert.That(reported).IsEqualTo(failure);
    }

    [Test]
    public async Task AnEmptyBrowsersRootIsProvisionedAndTheSameChildThenNavigates()
    {
        SuiteEnvironment.RequirePublishedSlice();

        PublishedSlice.EnsureFresh();

        // Decided before anything is started, because the decision changes what
        // has to be held before BrowserAI's first init.
        var plan = FirstRunCache.Plan();

        using var scratch = ScratchDirectory.Create("first-run");

        // ⚠️ INSIDE THE USER'S PROFILE, and it is the only directory in this
        // test that has to be. Since 2026-08-20 a published BrowserAI refuses at
        // startup when its app root is outside the profile -- see
        // `Hosting.InstallRootScope` -- so an app root under `<repo>\.work\`,
        // which is what this was until that day, is handed to a process that
        // exits 1 before it serves anything. The session directory below stays
        // in the repository's scratch root, because a session directory is the
        // caller's own path and has nothing to do with where the app root is.
        //
        // A BrowserAI whose whole app root is new, so its browsers directory is
        // empty in the only way that counts: nothing has ever been installed
        // there and no marker exists to short-circuit the check.
        using var appRootScratch = ScratchDirectory.CreateUnderProfile("first-run");

        var appRoot = Path.Combine(appRootScratch.Path, "app-root");
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
            ? ProvisioningClaim.Take(browsers, SessionManager.DefaultBrowser)
            : null;

        if (elsewhere is not null)
        {
            // A claim that silently failed would let BrowserAI download while
            // this test believed it was reading a cache -- green, and 203.8 MB
            // heavier than the run says it is.
            await Assert.That(elsewhere.Held).IsTrue();
        }

        // The empty root through --data-root since step 5 (previously the suite's
        // BROWSERAI_ROOT, which no running BrowserAI reads any more); the harness hands
        // it on to the background it starts beside the relay.
        var environment = PublishedSlice.InheritedEnvironment();

        await using var client = RawStdioClient.Start(
            PublishedSlice.Executable,
            [.. PublishedSlice.Mcp, BrowserAiPaths.DataRootArgument, appRoot],
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
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        // ⚠️ The bullet this whole step turns on, and it is asserted on STATE
        // and not on a stopwatch.
        //
        // Deleted 2026-08-18: `Assert.That(initElapsed).IsLessThan(20 s)`, with
        // the note "a 204 MB download is running and init answered anyway; if it
        // had waited, this number would be the download's". The state assertion
        // on the next line says the same thing and says it better: an init that
        // had waited for the download would report `ready`, not `provisioning`, so
        // the word IS the proof that it did not wait. Twenty seconds, meanwhile,
        // is a number a starved machine reaches while the product is behaving
        // perfectly -- and this test runs beside 418 others.
        await Assert.That((bool?)init["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(init)).Contains("browserProvisioning: provisioning");

        // A browser-needing call is refused instead of hanging, and the refusal
        // is §H.4 row 6 -- with the size, so a caller can decide what waiting
        // costs it.
        var refused = await CallAsync(client, "browser_navigate", new JsonObject
        {
            ["url"] = SliceRun.TargetUrl,
            [SessionToolSurface.SessionParameter] = session,
            [SessionToolSurface.WhyParameter] = "the suite exercising this call",
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).Contains(BrowserProvisioner.DownloadSizeFor(SessionManager.DefaultBrowser));

        // ⚠️ browser_get_config is refused as well, and that is a CORRECTION to
        // §A found by this very test. It said the tool keeps working; measured
        // 2026-08-16 @ 0.0.79 against the child directly, twice, it does not --
        // it resolves the browser executable before answering. So row 6 covers
        // it too, and the property §A wanted is delivered by BrowserAI's own
        // tools below.
        var config = await CallAsync(client, "browser_get_config", new JsonObject
        {
            [SessionToolSurface.SessionParameter] = session,
            [SessionToolSurface.WhyParameter] = "the suite exercising this call",
        });

        await Assert.That((bool?)config["isError"]).IsTrue();
        await Assert.That(TextOf(config)).Contains(BrowserProvisioner.DownloadSizeFor(SessionManager.DefaultBrowser));

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

        // ⚠️ AND NOT A MOMENT LONGER THAN THE PRODUCT SAYS IT IS STILL TRYING --
        // 2026-10-08. On the gate of f68ae4cf upstream's installer exited 1 at
        // 15:48:40Z with its own install lock judged compromised, and this wait then
        // sat out the rest of its 45 minutes for a marker nothing was still writing,
        // until the test host was stopped by hand at 34 minutes. Corrected
        // 2026-10-08 (previously "its own lock judged compromised under the gate's
        // load"): the stack ran through the branch of the lock's refresh that finds
        // the lock directory gone, and the downloaded archive was gone from this
        // app root afterwards, so both were deleted under the install; nothing
        // points at the load. BrowserAI writes the installer's failure into its log
        // as it happens, so the wait reads that too and ends with what the log said.
        var (landed, failure) = await WaitForMarkerAsync(installed, Patience, () => ProvisioningFailureIn(appRoot));

        await Assert.That(landed).IsTrue().Because(failure ?? "the marker did not land within the patience, and BrowserAI's log reported no failure");

        // ⚠️ In-session recovery, and this is the assertion the plan asks for by
        // name. Same session, same child, no restart and no second init: the
        // very call that was refused above now succeeds.
        var navigate = await CallAsync(client, "browser_navigate", new JsonObject
        {
            ["url"] = SliceRun.TargetUrl,
            [SessionToolSurface.SessionParameter] = session,
            [SessionToolSurface.WhyParameter] = "the suite exercising this call",
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
        // and not a slow test: the fix is to wait for the thing being
        // asserted, not to give the whole sequence longer.
        //
        // It also makes the two assertions beneath it stronger, not
        // merely later. ffmpeg is the LAST component this install fetches, so a
        // headless shell that was going to appear would already have appeared by
        // the time ffmpeg's marker lands.
        //
        // ⚠️ The ordering survives a cached run and is not weakened by it.
        // FirstRunCache.SeedInto copies every byte before it writes a single
        // marker, so chromium's marker still cannot precede ffmpeg's bytes; and
        // a cache carrying a chromium_headless_shell-* is refused outright, so
        // the negative assertion below cannot pass because the cache is missing
        // something and not because --no-shell works.
        var ffmpeg = await WaitForAnyMarkerAsync(browsers, "ffmpeg-*", Patience);

        await Assert.That(ffmpeg).IsNotNull();

        // And what landed is what the payload pins, not something that happened
        // to be lying around: ffmpeg and winldd come with it on Windows, and the
        // headless shell deliberately does not.
        await Assert.That(File.Exists(Path.Combine(installed, "chrome-win64", "chrome.exe"))).IsTrue();
        await Assert.That(Directory.EnumerateDirectories(browsers, "chromium_headless_shell-*").Any()).IsFalse();

        _ = await client.CloseAndWaitForExitAsync(TestDefaults.ProcessHang);

        // ⚠️ AND THEN THE BACKGROUND, since 2026-10-09 (previously the close above
        // ended everything): the close ends the relay, and the background keeps the
        // session whose browser runs out of this tree until the client's job closes.
        await client.DisposeAsync();

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
    /// alone would swap one race for a narrower one. The pattern is a glob and
    /// not a composed name because the revision is upstream's to move and this
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

    /// <summary>
    /// Waits for the completion marker in one directory, or for BrowserAI to report
    /// that the install it was waiting on has failed.
    /// </summary>
    /// <param name="directory">The browser's directory.</param>
    /// <param name="patience">How long to wait at most: a hang detector.</param>
    /// <param name="failed">What BrowserAI reported as a failed install, or <see langword="null"/> while it reported none.</param>
    /// <returns>Whether the marker landed, and the failure that ended the wait when one did.</returns>
    internal static async Task<(bool Landed, string? Failure)> WaitForMarkerAsync(string directory, TimeSpan patience, Func<string?> failed)
    {
        var marker = Path.Combine(directory, BrowsersManifest.InstallationCompleteMarker);
        var waited = Stopwatch.StartNew();

        while (waited.Elapsed < patience)
        {
            if (File.Exists(marker))
            {
                return (true, null);
            }

            if (failed() is { } failure)
            {
                return (false, failure);
            }

            await Task.Delay(250);
        }

        return (false, null);
    }

    /// <summary>
    /// The first record in an app root's logs that says provisioning gave up: the
    /// installer exiting without completing, a cap firing, or the provisioning
    /// failing outright.
    /// </summary>
    /// <remarks>
    /// <b>Read through <see cref="FileShare.ReadWrite"/> and <see cref="FileShare.Delete"/></b>,
    /// because the process writing the log holds it open for the whole run.
    /// </remarks>
    /// <param name="appRoot">The app root whose <c>logs</c> folder is read.</param>
    /// <returns>The record, or <see langword="null"/> when there is none yet.</returns>
    internal static string? ProvisioningFailureIn(string appRoot)
    {
        var logs = Path.Combine(appRoot, "logs");

        if (!Directory.Exists(logs))
        {
            return null;
        }

        foreach (var log in Directory.EnumerateFiles(logs, "*.log"))
        {
            string text;

            try
            {
                using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                text = reader.ReadToEnd();
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var line in text.Split('\n'))
            {
                if (ProvisioningFailureEvents.Any(name => line.Contains(name, StringComparison.Ordinal)))
                {
                    return line.TrimEnd('\r');
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The provisioner's three records of an install that gave up, by the category
    /// and event id every log record carries: 64, the installer exited without
    /// completing; 65, a cap fired; 70, provisioning failed.
    /// </summary>
    private static readonly string[] ProvisioningFailureEvents =
    [
        "BrowserAI.Runtime.BrowserProvisioner[64]",
        "BrowserAI.Runtime.BrowserProvisioner[65]",
        "BrowserAI.Runtime.BrowserProvisioner[70]",
    ];

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
