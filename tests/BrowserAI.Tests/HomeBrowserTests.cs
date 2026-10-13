// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.App.Page;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The home in a real browser, Chromium and Firefox: each new reading of the CPU and memory
/// is written where its figure stands, and the after-update notice's dismiss folds it away,
/// marks it seen, and the home is drawn without it once the fold has finished.
/// </summary>
/// <remarks>
/// <para>
/// <b>What raw HTTP cannot show, as in <see cref="PageBrowserTests"/></b>: that the page's
/// script reads the <c>usage</c> event and writes its words into every element carrying the
/// figure's name, and that the dismiss posts its action and the state that follows replaces
/// the page only once the fold has finished. Headless, through BrowserAI's own published
/// server, with the page served from this test host.
/// </para>
/// <para>
/// <b>The fold is held, not timed.</b> The arm pauses the notice's transitions the moment the
/// dismiss starts them, and learns that the state has arrived from a reading sent after it on
/// the same stream, whose figure the page writes at once; it then finishes the transitions
/// itself. Nothing in it depends on how long the fold takes.
/// </para>
/// <para>
/// <b>The maintainer's 59 b and 57 a, 2026-10-11</b>, his words verbatim: <i>"Show this all
/// with realtime updates."</i> and <i>"57 a - also allow dismissing the banner in a nice
/// way."</i>
/// </para>
/// </remarks>
internal sealed class HomeBrowserTests
{
    [Test]
    [Arguments(ProvisionedBrowsers.Chromium)]
    [Arguments(ProvisionedBrowsers.Firefox)]
    public async Task InARealBrowserEachReadingIsWrittenWhereItsFigureStandsAndTheDismissFoldsTheNoticeAway(string browser)
    {
        await SuiteEnvironment.RequirePublishedSliceAsync();

        if (browser is ProvisionedBrowsers.Firefox)
        {
            await SuiteEnvironment.RequireProvisionedFirefoxAsync();
        }
        else
        {
            await SuiteEnvironment.RequireProvisionedChromiumAsync();
        }

        PublishedSlice.EnsureFresh();

        await using var rig = await PageRig.CreateAsync(occasion: Occasion.AfterUpdate);
        using var scratch = await ScratchDirectory.CreateAsync($"home-browser-{browser}");

        rig.Usage.Reading = new UsageReading(new ProcessCounters(0, 46 * 1024 * 1024, 30 * 1024 * 1024), [], [], 4);

        var address = await rig.HandOutAsync();
        var session = Path.Combine(scratch.Path, "home-session");

        await using var client = await RawStdioClient.StartAsync(PublishedSlice.Executable, PublishedSlice.Mcp, scratch.Path, PublishedSlice.InheritedEnvironment());

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);

        _ = await CallAsync(client, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = session,
            ["purpose"] = "the suite opening BrowserAI's own home in a headless browser",
            ["browser"] = browser,
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        try
        {
            async Task<string> evaluateAsync(string function) =>
                TextOf(await CallAsync(client, "browser_evaluate", new JsonObject
                {
                    ["function"] = function,
                    [SessionToolSurface.SessionParameter] = session,
                    [SessionToolSurface.WhyParameter] = "the suite reading what BrowserAI's own home shows",
                }));

            _ = await CallAsync(client, "browser_navigate", new JsonObject
            {
                ["url"] = address,
                [SessionToolSurface.SessionParameter] = session,
                [SessionToolSurface.WhyParameter] = "the suite opening BrowserAI's home the way a Start Menu click does",
            });

            await Assert.That(await WaitForAsync(() => rig.Page.Tabs?.Connected is 1)).IsTrue();
            await Assert.That(await evaluateAsync("() => document.querySelector('[data-usage=\"own\"]').textContent")).Contains("46 MB");

            // The page's watch reads what holds the update and the sessions once a second, and
            // its first second sends the tab a new state. That is let happen and drawn before
            // anything is marked, so the only event left to change the page is a reading's.
            _ = await evaluateAsync("() => { document.querySelector('#main > *').dataset.first = 'yes'; return 'marked'; }");

            rig.Clock.Advance(PageService.UsageInterval);

            await Assert.That(await WaitForAsync(async () =>
                (await evaluateAsync("() => document.querySelector('[data-first]') ? 'old' : 'replaced'")).Contains("replaced", StringComparison.Ordinal))).IsTrue();

            // A reading two seconds on, of 0.4 s of CPU on four processors: the page is
            // not drawn again, and the figure changes where it stands.
            _ = await evaluateAsync("() => { document.querySelector('#main > *').dataset.old = 'yes'; return 'marked'; }");

            rig.Usage.Reading = new UsageReading(new ProcessCounters(4_000_000, 52 * 1024 * 1024, 30 * 1024 * 1024), [], [], 4);
            rig.Clock.Advance(PageService.UsageInterval);

            await Assert.That(await WaitForAsync(async () =>
                (await evaluateAsync("() => document.querySelector('[data-usage=\"cpu\"]').textContent + ' ' + document.querySelector('[data-usage=\"own\"]').textContent"))
                    .Contains("5.0% 52 MB", StringComparison.Ordinal))).IsTrue();
            await Assert.That(await evaluateAsync("() => document.querySelector('[data-old]') ? 'kept' : 'replaced'")).Contains("kept");
            await Assert.That(await evaluateAsync("() => 'paths=' + document.querySelectorAll('[data-usage-chart=\"cpu\"] svg path').length")).DoesNotContain("paths=0");

            // The dismiss, pressed in the page: the notice folds and its action is taken. The
            // fold is held where it is, so the state the action sends arrives while it folds:
            // that state waits for the fold to finish, and is drawn once it has.
            await Assert.That(await evaluateAsync("() => document.getElementById('fold-updated') ? 'notice' : 'none'")).Contains("notice");
            await Assert.That(await evaluateAsync("() => { document.getElementById('b-dismiss-updated').click(); const folding = document.getElementById('fold-updated').getAnimations(); folding.forEach((animation) => animation.pause()); return 'folding=' + folding.length; }"))
                .DoesNotContain("folding=0").Because("the notice folds away with a transition, which a reduced-motion setting would switch off");

            // The record is written after the state is sent to every tab, so once it says seen
            // the state is in this tab's stream, and a reading sent after it arrives after it.
            // ⚠️ Read through the product's own reader since 2026-10-13: the record is replaced
            // by a move, and a read that meets the move, or another process holding the new
            // file, is refused with a sharing violation, which the reader answers as no record
            // yet and the bare read of the records gate at 3626ec67 threw.
            await Assert.That(await WaitForAsync(async () => await UpdateNoticeFile.ReadAsync(rig.Facts.DataRoot) is { Version: "9.0.0", Seen: true })).IsTrue();

            rig.Usage.Reading = new UsageReading(new ProcessCounters(8_000_000, 61 * 1024 * 1024, 30 * 1024 * 1024), [], [], 4);
            rig.Clock.Advance(PageService.UsageInterval);

            await Assert.That(await WaitForAsync(async () =>
                (await evaluateAsync("() => document.querySelector('[data-usage=\"own\"]').textContent")).Contains("61 MB", StringComparison.Ordinal))).IsTrue();
            await Assert.That(await evaluateAsync("() => document.getElementById('fold-updated') ? 'notice' : 'none'"))
                .Contains("notice").Because("a state that arrives while the notice folds waits for the fold to finish");

            _ = await evaluateAsync("() => { document.getElementById('fold-updated').getAnimations().forEach((animation) => animation.finish()); return 'finished'; }");

            await Assert.That(await WaitForAsync(async () =>
                (await evaluateAsync("() => document.getElementById('fold-updated') ? 'notice' : 'none'")).Contains("none", StringComparison.Ordinal))).IsTrue();
            await Assert.That(await evaluateAsync("() => document.getElementById('banner').hidden ? 'quiet' : document.getElementById('banner').textContent")).Contains("quiet");
        }
        finally
        {
            _ = await CallAsync(client, SessionToolSurface.Destroy, new JsonObject
            {
                ["directory"] = session,
                [SessionToolSurface.WhyParameter] = "the home suite finished with its session",
            });
        }
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition) =>
        await WaitForAsync(() => Task.FromResult(condition()));

    private static async Task<bool> WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + TestDefaults.BrowserHang;

        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        return true;
    }

    private static string TextOf(JsonObject result) =>
        string.Join(
            "\n",
            (result["content"]?.AsArray() ?? [])
                .Where(block => (string?)block!["type"] == "text")
                .Select(block => (string?)block!["text"] ?? string.Empty));

    private static async Task<JsonObject> CallAsync(RawStdioClient client, string tool, JsonObject arguments)
    {
        var envelope = await client.EnvelopeAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

        return envelope["result"]?.AsObject()
            ?? throw new InvalidOperationException(
                $"'{tool}' answered with a JSON-RPC error and no result: {envelope.ToJsonString()}");
    }
}
