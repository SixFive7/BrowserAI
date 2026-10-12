// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.App;
using BrowserAI.App.Page;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

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

        await using var rig = await PageRig.CreateAsync();
        using var scratch = await ScratchDirectory.CreateAsync($"page-browser-{browser}");

        // The setup page, where the button the arm clicks is since 2026-10-11 (40 a): the
        // home's cards lead to it, and the registration moved off the home with it.
        var first = await rig.HandOutAsync(PageKind.Setup);
        var session = Path.Combine(scratch.Path, "page-session");

        await using var client = await RawStdioClient.StartAsync(PublishedSlice.Executable, PublishedSlice.Mcp, scratch.Path, PublishedSlice.InheritedEnvironment());

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
            await Assert.That(await evaluateAsync("() => document.querySelector('.version').textContent")).Contains("9.0.0");
            await Assert.That(await evaluateAsync("() => document.querySelector('h1').textContent")).Contains("Claude Code and Codex");

            // A button, clicked in the page, posts through the gate: the registration,
            // which the rig's first read refused, is read again, and this time it reads.
            // Corrected 2026-10-10 (previously the update check's button, deleted with
            // the page's own update machinery under the maintainer's "9 a").
            var registration = (FakeRegistration)rig.Registration;

            await Assert.That(await WaitForAsync(async () =>
                (await evaluateAsync("() => document.querySelector('button[data-action=\"read-registration\"]') ? 'there' : ''")).Contains("there", StringComparison.Ordinal))).IsTrue();

            var reads = registration.Reads;

            registration.State = new AppState
            {
                Version = "9.0.0",
                InstallRoot = null,
                DataRoot = scratch.Path,
                ServerCommand = null,
                ServerRefusal = null,
                Clients = [],
            };

            _ = await evaluateAsync("() => { document.querySelector('button[data-action=\"read-registration\"]').click(); return 'clicked'; }");

            // Until the read arrives, or the page says the gate refused its request,
            // so a refusal fails here at once and not when the hang detector runs out.
            await Assert.That(await WaitForAsync(async () =>
                registration.Reads > reads
                || (await evaluateAsync("() => document.getElementById('banner').hidden ? '' : document.getElementById('banner').textContent"))
                    .Contains("did not take", StringComparison.Ordinal))).IsTrue();
            await Assert.That(registration.Reads).IsGreaterThan(reads);

            // And the answer came back into the page through its stream.
            await Assert.That(await WaitForAsync(async () =>
                (await evaluateAsync("() => document.querySelector('main').textContent")).Contains("Read at", StringComparison.Ordinal))).IsTrue();

            // The old tab's close is wrapped, so that a call to it can be seen in an
            // engine that keeps the tab (see the remarks).
            _ = await evaluateAsync(
                "() => { const close = window.close.bind(window); window.close = () => { document.documentElement.dataset.closeAsked = 'yes'; close(); }; return 'wrapped'; }");

            // A second Start Menu click: a new tab with a new address. The first tab
            // is told it was replaced and asks to close itself.
            var second = await rig.HandOutAsync();

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

    /// <summary>
    /// In a real browser the conversations page draws a line for each conversation and each
    /// session, a click on a card shows what it is beside the map, and when a new state
    /// arrives the keyboard focus is on the card it was on and the choice is kept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>46 a, the maintainer's choice of 2026-10-11</b>: the views are live, and the focus
    /// stays where it was when a new state arrives. The write-up of that day found the focus
    /// lost on every state, because the focused element was replaced with the page's main
    /// part. <b>40 a</b>: C's map, its lines drawn by the page's script from where the cards
    /// are, and its inspector for whatever is clicked.
    /// </para>
    /// <para>
    /// <b>The new state is told from the old by a mark</b> the arm sets on the old main
    /// part, which a replacement takes away; the change that sends it is a session's browser
    /// closing, which the inspector it chose then says.
    /// </para>
    /// </remarks>
    /// <param name="browser">The provisioned family.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments(ProvisionedBrowsers.Chromium)]
    [Arguments(ProvisionedBrowsers.Firefox)]
    public async Task InARealBrowserTheMapDrawsItsLinesAndANewStateKeepsTheFocusAndTheChoice(string browser)
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

        await using var rig = await PageRig.CreateAsync();
        using var scratch = await ScratchDirectory.CreateAsync($"page-map-{browser}");

        rig.Sessions.Snapshot = DashboardTests.Busy();

        var address = await rig.HandOutAsync(PageKind.Sessions);
        var session = Path.Combine(scratch.Path, "map-session");
        var node = "n-s-" + BrowserAI.App.Page.CensusPageSessions.IdOf(DashboardTests.CheckoutRounding);

        await using var client = await RawStdioClient.StartAsync(PublishedSlice.Executable, PublishedSlice.Mcp, scratch.Path, PublishedSlice.InheritedEnvironment());

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);

        _ = await CallAsync(client, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = session,
            ["purpose"] = "the suite opening BrowserAI's own map in a headless browser",
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
                    [SessionToolSurface.WhyParameter] = "the suite reading what BrowserAI's own map shows",
                }));

            // A window wide enough for the map, which a narrower one shows as a list.
            _ = await CallAsync(client, "browser_resize", new JsonObject
            {
                ["width"] = 1440,
                ["height"] = 900,
                [SessionToolSurface.SessionParameter] = session,
                [SessionToolSurface.WhyParameter] = "the suite giving the map the width it is drawn at",
            });

            _ = await CallAsync(client, "browser_navigate", new JsonObject
            {
                ["url"] = address,
                [SessionToolSurface.SessionParameter] = session,
                [SessionToolSurface.WhyParameter] = "the suite opening BrowserAI's conversations page the way --sessions does",
            });

            await Assert.That(await WaitForAsync(() => rig.Page.Tabs?.Connected is 1)).IsTrue();

            // One line from each conversation to BrowserAI and one on from it to each session
            // and to a conversation's "No browser yet": three conversations, three sessions,
            // one kept, and one with no browser.
            await Assert.That(await WaitForAsync(async () =>
                (await evaluateAsync("() => 'wires=' + document.querySelectorAll('#wires path.wire').length")).Contains("wires=7", StringComparison.Ordinal))).IsTrue();

            // A click on a session's card shows what it is, and the card keeps the focus.
            _ = await evaluateAsync($"() => {{ const card = document.getElementById('{node}'); card.focus(); card.click(); return 'clicked'; }}");

            await Assert.That(await evaluateAsync($"() => 'shown=' + !document.getElementById('inspect-{node[2..]}').hidden")).Contains("shown=true");
            await Assert.That(await evaluateAsync($"() => 'pressed=' + document.getElementById('{node}').getAttribute('aria-pressed')")).Contains("pressed=true");

            // A new state: the session's browser closes.
            _ = await evaluateAsync("() => { document.querySelector('#main > *').dataset.old = 'yes'; return 'marked'; }");

            rig.Sessions.Snapshot = DashboardTests.Busy(checkoutOpen: false);
            rig.Clock.Advance(BrowserAI.App.Page.PageService.HoldsWatchPeriod);

            await Assert.That(await WaitForAsync(async () =>
                (await evaluateAsync("() => document.querySelector('[data-old]') ? 'old' : 'replaced'")).Contains("replaced", StringComparison.Ordinal))).IsTrue();

            await Assert.That(await evaluateAsync("() => 'focus=' + (document.activeElement ? document.activeElement.id : '')")).Contains($"focus={node}");
            await Assert.That(await evaluateAsync($"() => 'shown=' + !document.getElementById('inspect-{node[2..]}').hidden")).Contains("shown=true");
            await Assert.That(await evaluateAsync($"() => document.getElementById('inspect-{node[2..]}').textContent")).Contains("No browser open");
            await Assert.That(await evaluateAsync("() => 'wires=' + document.querySelectorAll('#wires path.wire').length")).Contains("wires=7");
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

    /// <summary>
    /// In a real browser every line of the map says its state in a word, a conversation's half
    /// of its line carries a strand for each state its sessions have, the legend draws a
    /// sample of each line, an address that names a session opens with it chosen and a new
    /// state keeps it, the inspector points at the card chosen, and the arrow keys move
    /// between cards.
    /// </summary>
    /// <remarks>
    /// <b>60, the maintainer's choice of 2026-10-11, variant C</b>, in his words verbatim
    /// <i>"60 I like C"</i>, of the variants put to him after he found the four lines hard to
    /// tell apart and the cards' being clickable unclear; and the session page dropped, its
    /// links opening the map with the session chosen. The markup each of these is drawn from
    /// is <see cref="ConversationsMapTests"/>'.
    /// </remarks>
    /// <param name="browser">The provisioned family.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments(ProvisionedBrowsers.Chromium)]
    [Arguments(ProvisionedBrowsers.Firefox)]
    public async Task InARealBrowserEachLineNamesItsStateAndAConversationsHalfCarriesEveryStateOfItsSessions(string browser)
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

        await using var rig = await PageRig.CreateAsync();
        using var scratch = await ScratchDirectory.CreateAsync($"page-lines-{browser}");

        rig.Sessions.Snapshot = ConversationsMapTests.Mixed();

        var address = await rig.HandOutAsync(PageKind.Sessions);
        var session = Path.Combine(scratch.Path, "lines-session");
        var pricing = BrowserAI.App.Page.CensusPageSessions.IdOf(ConversationsMapTests.Pricing);

        await using var client = await RawStdioClient.StartAsync(PublishedSlice.Executable, PublishedSlice.Mcp, scratch.Path, PublishedSlice.InheritedEnvironment());

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);

        _ = await CallAsync(client, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = session,
            ["purpose"] = "the suite reading the lines of BrowserAI's own map in a headless browser",
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
                    [SessionToolSurface.WhyParameter] = "the suite reading what BrowserAI's own map draws",
                }));

            async Task pressAsync(string key) =>
                _ = await CallAsync(client, "browser_press_key", new JsonObject
                {
                    ["key"] = key,
                    [SessionToolSurface.SessionParameter] = session,
                    [SessionToolSurface.WhyParameter] = "the suite moving between the map's cards the way a keyboard does",
                });

            async Task openAsync(string url)
            {
                _ = await CallAsync(client, "browser_navigate", new JsonObject
                {
                    ["url"] = url,
                    [SessionToolSurface.SessionParameter] = session,
                    [SessionToolSurface.WhyParameter] = "the suite opening BrowserAI's conversations page",
                });

                await Assert.That(await WaitForAsync(() => rig.Page.Tabs?.Connected is 1)).IsTrue();
            }

            _ = await CallAsync(client, "browser_resize", new JsonObject
            {
                ["width"] = 1440,
                ["height"] = 900,
                [SessionToolSurface.SessionParameter] = session,
                [SessionToolSurface.WhyParameter] = "the suite giving the map the width it is drawn at",
            });

            await openAsync(address);

            // Five lines on from BrowserAI, each named: a closed browser, a window and a hidden
            // one for the first conversation, the second's "No browser yet", and the kept session.
            await Assert.That(await WaitForAsync(async () =>
                (await evaluateAsync("() => 'labels=' + document.querySelectorAll('#wires .lab').length")).Contains("labels=5", StringComparison.Ordinal))).IsTrue();
            await Assert.That(await evaluateAsync("() => [...document.querySelectorAll('#wires .lab text')].map((text) => text.textContent).sort().join('|')"))
                .Contains("Hidden|Kept|No browser|No browser|On screen");

            // The first conversation's half carries one strand for each of its three states.
            await Assert.That(await evaluateAsync("() => 'strands=' + document.querySelectorAll('#wires .route[data-part=\"left\"][data-row=\"c-401-0\"] path.wire').length")).Contains("strands=3");
            await Assert.That(await evaluateAsync("() => [...document.querySelectorAll('#wires .route[data-part=\"left\"][data-row=\"c-401-0\"] path.wire')].map((wire) => wire.getAttribute('class')).join('|')"))
                .Contains("wire w|wire h|wire c");
            await Assert.That(await evaluateAsync("() => 'wires=' + document.querySelectorAll('#wires path.wire').length")).Contains("wires=9");

            // The legend draws a sample of each of the four lines, with its word.
            await Assert.That(await evaluateAsync("() => [...document.querySelectorAll('.legend-row svg.sample')].map((box) => box.dataset.sample + ':' + box.querySelectorAll('path.wire').length + ':' + box.querySelectorAll('.lab').length).join('|')"))
                .Contains("w:1:1|h:1:1|c:1:1|k:1:1");

            // A card chosen: the inspector points at it.
            _ = await evaluateAsync("() => { document.getElementById('n-c-401-0').click(); return 'clicked'; }");

            await Assert.That(await evaluateAsync("() => 'pointing=' + document.getElementById('inspector').classList.contains('pointing')")).Contains("pointing=true");

            // The arrow keys move between cards: right from a conversation is BrowserAI, and right
            // again a session of that conversation's row.
            _ = await evaluateAsync("() => { document.getElementById('n-c-401-0').focus(); return 'focused'; }");
            await pressAsync("ArrowRight");

            await Assert.That(await evaluateAsync("() => 'focus=' + (document.activeElement ? document.activeElement.id : '')")).Contains("focus=n-hub");

            await pressAsync("ArrowRight");

            await Assert.That(await evaluateAsync("() => 'focus=' + (document.activeElement ? document.activeElement.id : '')")).Contains("focus=n-s-");

            // An address that names a session opens with that session chosen, and a new state
            // keeps it.
            await openAsync(address + "&id=" + pricing);

            await Assert.That(await WaitForAsync(async () =>
                (await evaluateAsync($"() => 'shown=' + !document.getElementById('inspect-s-{pricing}').hidden + ',pressed=' + document.getElementById('n-s-{pricing}').getAttribute('aria-pressed')"))
                    .Contains("shown=true,pressed=true", StringComparison.Ordinal))).IsTrue();

            _ = await evaluateAsync("() => { document.querySelector('#main > *').dataset.old = 'yes'; return 'marked'; }");

            rig.Sessions.Snapshot = ConversationsMapTests.Mixed(historyOpen: true);
            rig.Clock.Advance(BrowserAI.App.Page.PageService.HoldsWatchPeriod);

            await Assert.That(await WaitForAsync(async () =>
                (await evaluateAsync("() => document.querySelector('[data-old]') ? 'old' : 'replaced'")).Contains("replaced", StringComparison.Ordinal))).IsTrue();
            await Assert.That(await evaluateAsync($"() => 'shown=' + !document.getElementById('inspect-s-{pricing}').hidden + ',pressed=' + document.getElementById('n-s-{pricing}').getAttribute('aria-pressed')"))
                .Contains("shown=true,pressed=true");
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
