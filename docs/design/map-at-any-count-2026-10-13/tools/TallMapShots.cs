// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.App.Page;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>
/// The rig the screenshots of the map at any number of conversations were taken with on
/// 2026-10-13: the conversations page at 3, 8, 16 and 40 rows, headless, in the published
/// server's Chromium and Firefox, and the script's drawing of the lines timed at 40, 100 and
/// 200 rows.
/// </summary>
/// <remarks>
/// A record of method, as it ran the second time but for the folder it writes to, which it named
/// by that worktree's whole path, and not part of the suite: it was copied into
/// <c>tests/BrowserAI.Tests</c>, run filtered to its class under the suite lock with the tree
/// published, and taken out again. The first run waited for lines a narrow window never draws;
/// this one waits for them only where the map is shown.
/// </remarks>
internal sealed class TallMapShotsSecondTemporary
{
    private static readonly string Out = Path.Combine(RepositoryLayout.Root.FullName, ".work", "shots");

    private const int Width = 1440;

    private const int Height = 1170;

    /// <summary>Every shot of one engine, and its timings.</summary>
    /// <param name="browser">The provisioned family.</param>
    /// <returns>The run.</returns>
    [Test]
    [Arguments(ProvisionedBrowsers.Chromium)]
    [Arguments(ProvisionedBrowsers.Firefox)]
    public async Task TakeTheShots(string browser)
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

        var real = DateTimeOffset.UtcNow;
        var holds = new NothingHolds();

        await using var rig = await PageRig.CreateAsync(
            holds: holds,
            facts: facts => facts with
            {
                Version = "2.0.0",
                StartedAt = real.AddHours(-3).AddMinutes(-12),
                StartedHow = BackgroundStart.SignIn,
                InstallRoot = @"C:\Users\sam\AppData\Local\BrowserAI.app",
                DataRoot = @"C:\Users\sam\AppData\Local\BrowserAI",
                LogDirectory = @"C:\Users\sam\AppData\Local\BrowserAI\logs",
                ServerCommand = @"C:\Users\sam\AppData\Local\BrowserAI.app\current\BrowserAI.exe",
            });

        rig.Clock.Advance(real - DateTimeOffset.UnixEpoch);

        var now = rig.Clock.GetUtcNow();

        holds.Watching = new UpdateWatch(UpdateChecks.Checking, UpdateSourceKind.GitHubReleases, null, null, now.AddMinutes(-47));
        rig.Sessions.Snapshot = ConversationsMapTests.Rows(3, now);

        using var scratch = await ScratchDirectory.CreateAsync($"tall-map-shots-2-{browser}");

        var first = await rig.HandOutAsync(PageKind.Sessions);
        var origin = new Uri(first).GetLeftPart(UriPartial.Authority);
        var session = Path.Combine(scratch.Path, "shots-session");
        var taken = new List<string>();
        var timings = new StringBuilder();

        await using var client = await RawStdioClient.StartAsync(PublishedSlice.Executable, PublishedSlice.Mcp, scratch.Path, PublishedSlice.InheritedEnvironment());

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);

        _ = await CallAsync(client, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = session,
            ["purpose"] = "screenshots of BrowserAI's own map at many rows in a headless browser, for the maintainer's review",
            ["browser"] = browser,
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
            ["viewport"] = $"{Width}x{Height}",
        });

        try
        {
            async Task<string> evaluateAsync(string function) =>
                TextOf(await CallAsync(client, "browser_evaluate", new JsonObject
                {
                    ["function"] = function,
                    [SessionToolSurface.SessionParameter] = session,
                    [SessionToolSurface.WhyParameter] = "reading the page before a screenshot",
                }));

            async Task navigateAsync(string url) =>
                _ = await CallAsync(client, "browser_navigate", new JsonObject
                {
                    ["url"] = url,
                    [SessionToolSurface.SessionParameter] = session,
                    [SessionToolSurface.WhyParameter] = "opening the page to photograph it",
                });

            async Task viewAsync(string theme, int width, int height)
            {
                _ = await CallAsync(client, "browser_emulate_media", new JsonObject
                {
                    ["colorScheme"] = theme,
                    [SessionToolSurface.SessionParameter] = session,
                    [SessionToolSurface.WhyParameter] = "drawing the page in the theme the screenshot shows",
                });
                _ = await CallAsync(client, "browser_resize", new JsonObject
                {
                    ["width"] = width,
                    ["height"] = height,
                    [SessionToolSurface.SessionParameter] = session,
                    [SessionToolSurface.WhyParameter] = "the size the screenshot shows",
                });
                await Task.Delay(TimeSpan.FromMilliseconds(400));
            }

            async Task shootAsync(string name, bool full)
            {
                if (full)
                {
                    _ = await evaluateAsync("() => { const map = document.getElementById('map'); const hub = document.getElementById('n-hub'); if (map && hub && !hub.style.flex) { hub.style.flex = map.classList.contains('tall') ? 'none' : '1 1 auto'; } return 'held'; }");
                }

                _ = await CallAsync(client, "browser_take_screenshot", new JsonObject
                {
                    ["filename"] = name,
                    ["fullPage"] = full,
                    ["scale"] = "css",
                    [SessionToolSurface.SessionParameter] = session,
                    [SessionToolSurface.WhyParameter] = "the screenshot for the maintainer's review",
                });
                taken.Add(name);

                if (full)
                {
                    _ = await evaluateAsync("() => { const hub = document.getElementById('n-hub'); if (hub && hub.dataset.keep !== 'yes') { hub.style.flex = ''; } return 'let go'; }");
                }
            }

            async Task openAsync(int rows)
            {
                rig.Sessions.Snapshot = ConversationsMapTests.Rows(rows, now);
                await navigateAsync(origin + "/sessions?tab=1");
                await Assert.That(await WaitForAsync(async () =>
                    (await evaluateAsync("() => { const layout = document.querySelector('.map-layout'); const shown = layout !== null && getComputedStyle(layout).display !== 'none'; return 'rows=' + document.querySelectorAll('.map-rows > .left').length + ' wires=' + (!shown || document.querySelectorAll('#wires path.wire').length > 0); }"))
                        .Contains($"rows={rows} wires=true", StringComparison.Ordinal))).IsTrue();
                await Task.Delay(TimeSpan.FromMilliseconds(600));
            }

            await navigateAsync(first);
            await Assert.That(await WaitForAsync(() => rig.Page.Tabs?.Connected is 1)).IsTrue();

            // The whole page at each count, light.
            await viewAsync("light", Width, Height);

            foreach (var rows in new[] { 3, 8, 16, 40 })
            {
                await openAsync(rows);
                _ = await evaluateAsync("() => { window.scrollTo(0, 0); return 'top'; }");
                await shootAsync($"{browser}-{rows:00}-rows-full-light.png", full: true);
            }

            // Forty rows a window at a time: the top, the middle with a session there chosen, and the bottom.
            _ = await evaluateAsync("() => { window.scrollTo(0, 0); return 'top'; }");
            await shootAsync($"{browser}-40-rows-1-top-light.png", full: false);

            const string ChooseInTheMiddle =
                "() => { const rights = document.querySelectorAll('.map-rows > .right'); const card = rights[19].querySelector('.node') || rights[20].querySelector('.node');"
                + " if (card.getAttribute('aria-pressed') !== 'true') { card.click(); } card.scrollIntoView({ block: 'center' }); return 'chosen ' + card.id; }";

            _ = await evaluateAsync(ChooseInTheMiddle);
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            await shootAsync($"{browser}-40-rows-2-middle-chosen-light.png", full: false);

            _ = await evaluateAsync("() => { window.scrollTo(0, document.documentElement.scrollHeight); return 'bottom'; }");
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            await shootAsync($"{browser}-40-rows-3-bottom-light.png", full: false);

            // Dark, his scheme.
            await viewAsync("dark", Width, Height);
            _ = await evaluateAsync(ChooseInTheMiddle);
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            await shootAsync($"{browser}-40-rows-2-middle-chosen-dark.png", full: false);
            _ = await evaluateAsync("() => { window.scrollTo(0, 0); return 'top'; }");
            await shootAsync($"{browser}-40-rows-full-dark.png", full: true);

            // The direction not taken: BrowserAI's card left in the middle of its column.
            await viewAsync("light", Width, Height);
            _ = await evaluateAsync("() => { const hub = document.getElementById('n-hub'); hub.dataset.keep = 'yes'; hub.style.flex = '1 1 auto'; window.scrollTo(0, 0); return 'in the middle'; }");
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            await shootAsync($"{browser}-40-rows-not-taken-card-in-the-middle-top.png", full: false);
            await shootAsync($"{browser}-40-rows-not-taken-card-in-the-middle-full.png", full: true);
            _ = await evaluateAsync("() => { const hub = document.getElementById('n-hub'); delete hub.dataset.keep; hub.style.flex = ''; return 'as drawn'; }");

            // Forty rows with BrowserAI chosen, the page scrolled to its middle: the inspector points at BrowserAI's card where it stays.
            _ = await evaluateAsync("() => { document.getElementById('n-hub').click(); window.scrollTo(0, Math.round(document.documentElement.scrollHeight / 2 - window.innerHeight / 2)); return 'chosen'; }");
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            await shootAsync($"{browser}-40-rows-4-middle-browserai-chosen-light.png", full: false);

            // A narrow window: the list, as before.
            await viewAsync("light", 1100, 900);
            await openAsync(16);
            _ = await evaluateAsync("() => { window.scrollTo(0, 0); return 'top'; }");
            await shootAsync($"{browser}-16-rows-narrow-1100-list.png", full: false);

            // What the script's drawing costs, at three counts.
            await viewAsync("light", Width, Height);

            const string Time =
                "() => { const redraw = []; const replace = []; const main = document.getElementById('main');"
                + " for (let i = 0; i < 7; i++) { const t = performance.now(); window.dispatchEvent(new Event('resize')); redraw.push(performance.now() - t); }"
                + " for (let i = 0; i < 7; i++) { const html = main.innerHTML; const t = performance.now(); main.innerHTML = html; window.dispatchEvent(new Event('resize')); void document.documentElement.scrollHeight; replace.push(performance.now() - t); }"
                + " redraw.sort((a, b) => a - b); replace.sort((a, b) => a - b);"
                + " return 'redraw-median-ms=' + redraw[3].toFixed(1) + ' redraw-max-ms=' + redraw[6].toFixed(1)"
                + " + ' replace-median-ms=' + replace[3].toFixed(1) + ' replace-max-ms=' + replace[6].toFixed(1)"
                + " + ' main-chars=' + main.innerHTML.length + ' elements=' + main.querySelectorAll('*').length"
                + " + ' wires=' + document.querySelectorAll('#wires path.wire').length + ' labels=' + document.querySelectorAll('#wires .lab').length"
                + " + ' page-height=' + document.documentElement.scrollHeight + ' map-height=' + Math.round(document.getElementById('map').getBoundingClientRect().height)"
                + " + ' tall=' + document.getElementById('map').classList.contains('tall') + ' agent=' + navigator.userAgent; }";

            foreach (var rows in new[] { 40, 100, 200 })
            {
                await openAsync(rows);
                _ = timings.Append(browser).Append(' ').Append(rows.ToString(CultureInfo.InvariantCulture)).Append(" rows: ").AppendLine(await evaluateAsync(Time));
            }

            await File.WriteAllTextAsync(Path.Combine(Out, $"timings-{browser}.txt"), timings.ToString());

            foreach (var name in taken)
            {
                await File.WriteAllBytesAsync(Path.Combine(Out, name), await File.ReadAllBytesAsync(Path.Combine(session, "output", name)));
            }
        }
        finally
        {
            _ = await CallAsync(client, SessionToolSurface.Destroy, new JsonObject
            {
                ["directory"] = session,
                [SessionToolSurface.WhyParameter] = "the screenshots are copied out",
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
