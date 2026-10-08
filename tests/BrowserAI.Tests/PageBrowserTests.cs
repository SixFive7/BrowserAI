// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>
/// The page in a real browser, Chromium and Firefox: its script connects, a button
/// posts through the gate, the answer comes back into the page, and a tab a newer
/// one replaced asks to close itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>What raw HTTP cannot show.</b> The gate admits a write only with our exact
/// <c>Origin</c>, <c>Sec-Fetch-Site: same-origin</c> and a JSON content type, and a
/// socket sends whatever it is told to. Whether a real browser's <c>fetch</c> sends
/// exactly that, whether the page's script runs under its own content security
/// policy, and whether <c>window.close()</c> closes the tab it is in are the
/// browser's to answer, in both engines this product provisions.
/// </para>
/// <para>
/// <b>Headless, through BrowserAI's own published server</b>, the way the
/// 2026-10-01 prototype drove its pages through Playwright: the browser is the
/// session's, so nothing reaches the screen, and the page is served from this test
/// host with the update machinery, the servers and the desktop replaced by
/// stand-ins.
/// </para>
/// <para>
/// ⚠️ <b>Whether the replaced tab really goes is the engine's to decide, and the
/// arm holds what the page decides</b>: that its script asks. The HTML standard lets a
/// script close a tab only when a script opened it or its session history holds one
/// document, which a tab the shell opens for a person's start does. A tab the arm can
/// reach is neither in Chromium: measured 2026-10-03, the session's first tab and a
/// new tab given the address both held two entries, the blank page they began on and
/// the page, and Chromium left them open after <c>window.close()</c>, while Firefox
/// closed them. A new blank tab whose one entry a script replaced with the address
/// did not connect at all in either engine within the minute that run waited. So the
/// arm wraps <c>window.close</c> in the old tab before it is replaced, and takes either
/// answer: the tab gone, or the wrapper called.
/// </para>
/// </remarks>
internal sealed class PageBrowserTests
{
    /// <summary>
    /// In a real browser the page connects, a button posts through the gate and its
    /// answer comes back into the page, and the tab asks to close itself once a newer
    /// one has replaced it.
    /// </summary>
    /// <param name="browser">The provisioned family.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments(ProvisionedBrowsers.Chromium)]
    [Arguments(ProvisionedBrowsers.Firefox)]
    public async Task InARealBrowserThePageConnectsPostsThroughTheGateAndAsksToCloseWhenReplaced(string browser)
    {
        SuiteEnvironment.RequirePublishedSlice();

        if (browser is ProvisionedBrowsers.Firefox)
        {
            SuiteEnvironment.RequireProvisionedFirefox();
        }
        else
        {
            SuiteEnvironment.RequireProvisionedChromium();
        }

        PublishedSlice.EnsureFresh();

        using var rig = new PageRig();
        using var scratch = ScratchDirectory.Create($"page-browser-{browser}");

        rig.Updates.Check = _ => Task.FromResult<UpdateCandidate?>(new UpdateCandidate
        {
            Version = "9.1.0",
            IsDowngrade = false,
            DeltaCount = 0,
            FullPackageSize = 1,
        });

        var first = rig.HandOut();
        var session = Path.Combine(scratch.Path, "page-session");

        await using var client = RawStdioClient.Start(PublishedSlice.Executable, PublishedSlice.Mcp, scratch.Path, PublishedSlice.InheritedEnvironment());

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);

        _ = await CallAsync(client, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = session,
            ["purpose"] = "the suite opening BrowserAI's own page in a headless browser",
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
                    [SessionToolSurface.WhyParameter] = "the suite reading what BrowserAI's own page shows",
                }));

            _ = await CallAsync(client, "browser_navigate", new JsonObject
            {
                ["url"] = first,
                [SessionToolSurface.SessionParameter] = session,
                [SessionToolSurface.WhyParameter] = "the suite opening BrowserAI's own page the way a person's start does",
            });

            // The page's own script opened its stream: the coordinator counts a tab.
            await Assert.That(await WaitForAsync(() => rig.Page.Tabs?.Connected is 1)).IsTrue();
            await Assert.That(await evaluateAsync("() => document.querySelector('h1').textContent")).Contains("BrowserAI 9.0.0");

            // A button, clicked in the page, posts through the gate.
            _ = await evaluateAsync("() => { document.querySelector('button[data-action=\"check-updates\"]').click(); return 'clicked'; }");

            // Until the check arrives, or the page says the gate refused its request,
            // so a refusal fails here at once and not when the hang detector runs out.
            await Assert.That(await WaitForAsync(async () =>
                rig.Updates.Checks is 1
                || (await evaluateAsync("() => document.getElementById('banner').hidden ? '' : document.getElementById('banner').textContent"))
                    .Contains("did not take", StringComparison.Ordinal))).IsTrue();
            await Assert.That(rig.Updates.Checks).IsEqualTo(1);

            // And the answer came back into the page through its stream.
            await Assert.That(await WaitForAsync(async () =>
                (await evaluateAsync("() => document.querySelector('main').textContent")).Contains("BrowserAI 9.1.0 is available.", StringComparison.Ordinal))).IsTrue();

            // The old tab's close is wrapped, so that a call to it can be seen in an
            // engine that keeps the tab (see the remarks).
            _ = await evaluateAsync(
                "() => { const close = window.close.bind(window); window.close = () => { document.documentElement.dataset.closeAsked = 'yes'; close(); }; return 'wrapped'; }");

            // A second Start Menu click: a new tab with a new address. The first tab
            // is told it was replaced and asks to close itself.
            var second = rig.HandOut();

            _ = await CallAsync(client, "browser_tabs", new JsonObject
            {
                ["action"] = "new",
                ["url"] = second,
                [SessionToolSurface.SessionParameter] = session,
                [SessionToolSurface.WhyParameter] = "the suite opening the newer address in a new tab, the way a second Start Menu click does",
            });

            // The old tab is gone, or it is still there and its script asked to close it.
            await Assert.That(await WaitForAsync(async () =>
            {
                var tabs = await TabsAsync(client, session);

                if (IndexOf(tabs, first) is not { } index)
                {
                    return true;
                }

                await SelectAsync(client, session, index);

                return (await evaluateAsync("() => 'asked=' + (document.documentElement.dataset.closeAsked ?? 'no')")).Contains("asked=yes", StringComparison.Ordinal);
            })).IsTrue();

            await Assert.That(rig.Page.Tabs?.Connected).IsEqualTo(1);

            await SelectAsync(client, session, IndexOf(await TabsAsync(client, session), second)!.Value);
            await Assert.That(await evaluateAsync("() => document.body.dataset.tab")).Contains("2");
        }
        finally
        {
            _ = await CallAsync(client, SessionToolSurface.Destroy, new JsonObject
            {
                ["directory"] = session,
                [SessionToolSurface.WhyParameter] = "the page suite finished with its session",
            });
        }
    }

    /// <summary>The session's tabs, as <c>browser_tabs</c> lists them.</summary>
    /// <param name="client">The session's client.</param>
    /// <param name="session">The session.</param>
    /// <returns>The list's text.</returns>
    private static async Task<string> TabsAsync(RawStdioClient client, string session) =>
        TextOf(await CallAsync(client, "browser_tabs", new JsonObject
        {
            ["action"] = "list",
            [SessionToolSurface.SessionParameter] = session,
            [SessionToolSurface.WhyParameter] = "the suite reading which tabs are open",
        }));

    /// <summary>Makes one of the session's tabs the one later calls act on.</summary>
    /// <param name="client">The session's client.</param>
    /// <param name="session">The session.</param>
    /// <param name="index">The tab's index in the list.</param>
    /// <returns>The work.</returns>
    private static async Task SelectAsync(RawStdioClient client, string session, int index) =>
        _ = await CallAsync(client, "browser_tabs", new JsonObject
        {
            ["action"] = "select",
            ["index"] = index,
            [SessionToolSurface.SessionParameter] = session,
            [SessionToolSurface.WhyParameter] = "the suite turning to one of BrowserAI's own tabs",
        });

    /// <summary>The index of the tab a list shows at an address, or <see langword="null"/>.</summary>
    /// <remarks>Each tab is one line: a dash, its index and a colon, then on one of them <c>(current)</c>, then its title in square brackets and its address in parentheses.</remarks>
    /// <param name="list">The list's text.</param>
    /// <param name="address">The address.</param>
    /// <returns>The index.</returns>
    private static int? IndexOf(string list, string address)
    {
        foreach (var line in list.Split('\n'))
        {
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("- ", StringComparison.Ordinal)
                && trimmed.Contains("(" + address + ")", StringComparison.Ordinal)
                && trimmed.IndexOf(':', StringComparison.Ordinal) is > 2 and var colon
                && int.TryParse(trimmed.AsSpan(2, colon - 2), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index))
            {
                return index;
            }
        }

        return null;
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
