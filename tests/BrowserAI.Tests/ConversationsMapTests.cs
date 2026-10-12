// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.RegularExpressions;
using BrowserAI.App.Page;
using BrowserAI.Coordination;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>
/// The conversations page's map as the maintainer chose it on 2026-10-11, variant C,
/// named lines, with the session page it replaced folded into its inspector.
/// </summary>
/// <remarks>
/// <para>
/// <b>60, the maintainer's choice, in his words verbatim: <i>"60 I like C"</i></b>, of
/// the map variants put to him after his words of the same day: <i>"I like the
/// conversations and browsers view. But the visual line differences between: Window on
/// your screen / Hidden browser / No browser open / Kept, no conversation / Are very
/// difficult to see. Use more different lines and colors here. But keep it in style and
/// pretty. Also, it is not clear you can select the browser session. Communicate that
/// better."</i>
/// </para>
/// <para>
/// <b>And the session page goes</b>, at his question of the same moment: <i>"Also, the
/// details button on the browser session box to the right navigates to a page that, for
/// as far as I can see, does not have more details than the side box we navigated from.
/// Is this correct? If so, drop the page or integrate that last bit into the box and drop
/// the page."</i> What the page said that the inspector did not is in the inspector now,
/// every link to a session opens the map with that session chosen, and a folder that is
/// not open says what it is on its storage row.
/// </para>
/// <para>
/// <b>Every arm here reads the markup over raw HTTP</b>, through the product's
/// <see cref="PageService"/> with the rig's stand-ins. What only a browser can show, the
/// lines the script draws from the markup, is in <see cref="PageBrowserTests"/>.
/// </para>
/// </remarks>
internal sealed class ConversationsMapTests
{
    /// <summary>The instant every rig's clock starts at.</summary>
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    /// <summary>A session whose browser is closed, used first by the mixed conversation.</summary>
    internal const string History = @"C:\Code\market-notes\.work\pricing-history";

    /// <summary>A session with a window on the screen, used second by the mixed conversation.</summary>
    internal const string Pricing = @"C:\Code\market-notes\.work\pricing-compare";

    /// <summary>A hidden session, used third by the mixed conversation.</summary>
    internal const string Archive = @"C:\Code\market-notes\.work\pricing-archive";

    /// <summary>A session kept after its conversation ended.</summary>
    internal const string Login = @"C:\Code\acme-shop\.work\login-flow-check";

    /// <summary>A session folder the background does not hold now.</summary>
    internal const string Invoice = @"C:\Code\billing\.work\invoice-export";

    /// <summary>
    /// A conversation's half of its line carries the state of every session it uses, in the
    /// order window, hidden, no browser, and not the state of its first session alone.
    /// </summary>
    /// <remarks>
    /// <b>The defect of the map as it was built on 2026-10-11</b>: the half between a
    /// conversation and BrowserAI was drawn in its first session's state, so a conversation
    /// whose first session had no browser open drew a line that said nothing of the window it
    /// had on the screen. The script draws one strand for each state the row names.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AConversationsHalfOfItsLineCarriesTheStateOfEverySessionItUses()
    {
        await using var rig = await PageRig.CreateAsync();

        rig.Sessions.Snapshot = Mixed();

        var page = (await PageRig.GetAsync(await rig.HandOutAsync(PageKind.Sessions))).Body;

        await Assert.That(page).Contains("data-row=\"c-401-0\" data-line=\"w h c\"").Because("the conversation uses a window, a hidden browser and a closed one, its closed one first");
        await Assert.That(page).Contains("data-row=\"c-402-0\" data-line=\"n\"").Because("a conversation with no session draws the no-browser line");

        // A conversation whose sessions all share one state draws that state alone.
        rig.Sessions.Snapshot = Mixed(historyOpen: true);

        var again = (await PageRig.GetAsync(await rig.HandOutAsync(PageKind.Sessions))).Body;

        await Assert.That(again).Contains("data-row=\"c-401-0\" data-line=\"w h\"");
    }

    /// <summary>
    /// Every card on the map says it can be chosen, and which one is, the legend says what
    /// each line is and where a chosen card's details open, and the inspector with nothing
    /// chosen says how to choose.
    /// </summary>
    /// <remarks>
    /// <b>60, his words</b>: <i>"it is not clear you can select the browser session.
    /// Communicate that better."</i> Variant C gives every card a tab that reads
    /// <i>Details</i>, and <i>Selected</i> on the card chosen, and a sample of each line in the
    /// legend for the script to draw.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryCardOnTheMapSaysItCanBeChosenAndTheInspectorSaysHowToChoose()
    {
        await using var rig = await PageRig.CreateAsync();

        rig.Sessions.Snapshot = Mixed();

        var page = (await PageRig.GetAsync(await rig.HandOutAsync(PageKind.Sessions))).Body;
        var map = MapOf(page);
        var cards = Regex.Matches(map, "<button type=\"button\" class=\"[^\"]*node[^\"]*\"[^>]*>.*?</button>", RegexOptions.Singleline);

        // Two conversations, BrowserAI, four sessions.
        await Assert.That(cards.Count).IsEqualTo(7);

        foreach (Match card in cards)
        {
            await Assert.That(card.Value).Contains(Cue).Because(card.Value);
        }

        foreach (var kind in new[] { "w", "h", "c", "k" })
        {
            await Assert.That(page).Contains($"<svg class=\"sample\" data-sample=\"{kind}\"").Because("the legend's sample of the line " + kind);
        }

        foreach (var words in new[] { "Window on your screen", "Hidden browser", "No browser open", "Kept, no conversation" })
        {
            await Assert.That(page).Contains("</svg>" + words + "</span>").Because(words);
        }

        await Assert.That(page).Contains("Click any card on the map. Its details open here.</p>");
        await Assert.That(page).Contains("<p class=\"none-title\">Click any card on the map</p>");
        await Assert.That(page).Contains("Conversations on the left, BrowserAI in the middle and browser sessions on the right can all be clicked. Each one opens here.");
        await Assert.That(page).Contains("<kbd>Tab</kbd> and the arrow keys move between cards. <kbd>Enter</kbd> shows one, and <kbd>Esc</kbd> clears it.");
    }

    /// <summary>
    /// A link to a session opens the conversations page with that session chosen, an address
    /// of the session page that was dropped lands there too, no page links to that page any
    /// more, and a session that is not open is said so above the map.
    /// </summary>
    /// <remarks>
    /// <b>The session page is dropped</b>, the maintainer's question of 2026-10-11 answered by
    /// his own second option. An address an older tab kept still lands on the page that now
    /// holds what it held (40 a): the conversations page, with the session chosen.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ALinkToASessionOpensTheMapWithThatSessionChosen()
    {
        await using var rig = await PageRig.CreateAsync(registration: new FakeRegistration { State = DashboardTests.Apps() });

        rig.Sessions.Snapshot = Mixed();
        rig.Folders.Folders = [new SessionFolder(CensusPageSessions.IdOf(Invoice), Invoice, 1024 * 1024)];
        _ = await rig.HandOutAsync(PageKind.Sessions);

        var pricing = CensusPageSessions.IdOf(Pricing);

        await Assert.That(PageContent.SessionRoute(pricing, 3)).IsEqualTo("sessions?tab=3&amp;id=" + pricing);

        foreach (var route in new[] { $"sessions?tab=1&id={pricing}", $"session?tab=1&id={pricing}" })
        {
            var page = await PageRig.GetAsync(rig.Root + route);

            await Assert.That(page.Status).IsEqualTo(200).Because(route);
            await Assert.That(page.Body).Contains("data-page=\"sessions\"").Because(route);
            await Assert.That(page.Body).Contains("<h1 class=\"page-title\">Conversations and browsers</h1>").Because(route);
            await Assert.That(page.Body).Contains($"id=\"n-s-{pricing}\" data-inspect=\"s-{pricing}\" data-line=\"w\" aria-pressed=\"true\"").Because(route);
            await Assert.That(page.Body).Contains($"<section class=\"inspect\" id=\"inspect-s-{pricing}\">").Because(route);
            await Assert.That(page.Body).Contains("id=\"inspect-none\" hidden>").Because(route);
            await Assert.That(Regex.Count(page.Body, "aria-pressed=\"true\"")).IsEqualTo(1).Because(route);
        }

        // With nothing named, nothing is chosen.
        var plain = (await PageRig.GetAsync(rig.Root + "sessions?tab=1")).Body;

        await Assert.That(plain).DoesNotContain("aria-pressed=\"true\"");
        await Assert.That(plain).Contains("<div class=\"inspect-none\" id=\"inspect-none\">");

        // A folder the background does not hold, and a name nothing knows: each said once the
        // measurement is in, which the stream's states carry.
        var invoice = CensusPageSessions.IdOf(Invoice);

        _ = await PageRig.GetAsync(rig.Root + $"sessions?tab=1&id={invoice}");

        using (var stream = await rig.StreamAsync(rig.Gate.Root, 1, "sessions", "id=" + invoice))
        {
            var closed = await DashboardTests.StateContainingAsync(stream, "is not open now");

            await Assert.That(closed).IsNotNull();
            await Assert.That(closed!).Contains("invoice-export is not open now, so this page does not show it. The Storage page lists its folder and its size.");
            await Assert.That(closed).Contains("href=\"storage?tab=1\"");
        }

        using (var stream = await rig.StreamAsync(rig.Gate.Root, 1, "sessions", "id=0000000000000000"))
        {
            var unknown = await DashboardTests.StateContainingAsync(stream, "holds no session by that name");

            await Assert.That(unknown).IsNotNull();
            await Assert.That(unknown!).Contains("BrowserAI holds no session by that name now, and its last measurement of the session folders found none.");
        }

        // No page links to the session page.
        foreach (var kind in Enum.GetValues<PageKind>())
        {
            var page = (await PageRig.GetAsync(rig.Root + PageNames.RouteOf(kind) + "?tab=1")).Body;

            await Assert.That(page).DoesNotContain("href=\"session?").Because(PageNames.Of(kind));
        }
    }

    /// <summary>
    /// What the session page said that the inspector did not is in the inspector: what a
    /// trace is, that the page deletes nothing and who may delete the folder, and a
    /// conversation that uses the session is a card to choose, not a page to open.
    /// </summary>
    /// <remarks>
    /// <b>The three lines the session page added</b>, read off it before it was dropped: the
    /// traces' one-line explanation, the notice that only its agent or the person deletes the
    /// folder, and the sentence for a browser that is not open, which the inspector's browser
    /// line already says of a held session and a storage row now says of a folder not open.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheInspectorSaysWhatTheSessionPageSaid()
    {
        await using var rig = await PageRig.CreateAsync();

        rig.Sessions.Snapshot = Mixed(traces: true);
        rig.Folders.Folders = [new SessionFolder(CensusPageSessions.IdOf(Pricing), Pricing, 188 * 1024 * 1024)];

        _ = await PageRig.GetAsync(await rig.HandOutAsync(PageKind.Sessions));

        using var stream = await rig.StreamAsync(rig.Gate.Root, 1, "sessions");

        var html = await DashboardTests.StateContainingAsync(stream, "188 MB");

        await Assert.That(html).IsNotNull();

        var pricing = PanelOf(html!, "s-" + CensusPageSessions.IdOf(Pricing));
        var history = PanelOf(html!, "s-" + CensusPageSessions.IdOf(History));

        await Assert.That(pricing).Contains("<dt>Traces</dt><dd>checkout-total.zip<span class=\"dd-note\">Recordings Playwright can replay, in the session's output folder.</span></dd>");
        await Assert.That(pricing).Contains("<dt>Size on disk</dt><dd>188 MB</dd>");
        await Assert.That(pricing).Contains("<p class=\"keep-note\">BrowserAI never deletes a session folder on its own, and this page deletes nothing. The agent that made this one can delete it, and so can you: open its folder and delete it in Explorer.</p>");
        await Assert.That(pricing).Contains("<button type=\"button\" class=\"pick inline\" id=\"iu-s-" + CensusPageSessions.IdOf(Pricing) + "\" data-inspect=\"c-401-0\">");
        await Assert.That(pricing).Contains("This window is on your screen. While it is open, it holds back any update. Closing it ends the browser and keeps the session.");
        await Assert.That(history).Contains("No browser open. A call that names the session opens it again.");

        // The conversation lists its sessions as cards to choose.
        var conversation = PanelOf(html!, "c-401-0");

        await Assert.That(conversation).Contains($"<button type=\"button\" class=\"pick\" id=\"ip-c-401-0-{CensusPageSessions.IdOf(Pricing)}\" data-inspect=\"s-{CensusPageSessions.IdOf(Pricing)}\">");

        // And nothing in the inspector leads to a session's page.
        await Assert.That(html!).DoesNotContain("il-session-");
        await Assert.That(html).DoesNotContain(">Details</a>");
    }

    /// <summary>
    /// A storage row of a folder not open now says what it is, and a row of a session open
    /// now leads to the map with that session chosen.
    /// </summary>
    /// <remarks>
    /// <b>The session page's last use</b>: a folder not open now had its own page, which said
    /// its browser was not open and how a conversation opens it again. Its row says that now.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AStorageRowSaysWhatAFolderNotOpenIsAndAnOpenOneLeadsToTheMap()
    {
        await using var rig = await PageRig.CreateAsync();

        rig.Sessions.Snapshot = Mixed();
        rig.Folders.Folders =
        [
            new SessionFolder(CensusPageSessions.IdOf(Pricing), Pricing, 188 * 1024 * 1024),
            new SessionFolder(CensusPageSessions.IdOf(Invoice), Invoice, 214 * 1024 * 1024),
        ];

        _ = await PageRig.GetAsync(await rig.HandOutAsync(PageKind.Storage));

        using var storage = await rig.StreamAsync(rig.Gate.Root, 1, "storage");

        var html = await DashboardTests.StateContainingAsync(storage, "214 MB");

        await Assert.That(html).IsNotNull();

        var open = RowOf(html!, "pricing-compare");
        var closed = RowOf(html!, "invoice-export");

        await Assert.That(open).Contains($"href=\"sessions?tab=1&amp;id={CensusPageSessions.IdOf(Pricing)}\"");
        await Assert.That(closed).DoesNotContain("href=\"sessions?");
        await Assert.That(closed).Contains("Not open now. A conversation that names this session opens it again.");
    }

    /// <summary>
    /// The list the page shows in a narrow window or on a crowded day carries each session's
    /// folder, size and traces under a fold of its own, and no link to a session's page.
    /// </summary>
    /// <remarks>
    /// <b>The session page was the list's way to a session's details</b>, its <i>Details</i>
    /// button on every row. With the page gone, the row folds them away itself.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheListCarriesEachSessionsFolderSizeAndTracesInAFoldOfItsOwn()
    {
        await using var rig = await PageRig.CreateAsync();

        rig.Sessions.Snapshot = Mixed(traces: true);
        rig.Folders.Folders = [new SessionFolder(CensusPageSessions.IdOf(Pricing), Pricing, 188 * 1024 * 1024)];

        _ = await PageRig.GetAsync(await rig.HandOutAsync(PageKind.Sessions));

        using var stream = await rig.StreamAsync(rig.Gate.Root, 1, "sessions");

        var html = await DashboardTests.StateContainingAsync(stream, "188 MB");

        await Assert.That(html).IsNotNull();

        var list = html![html.IndexOf("<div class=\"list-view", StringComparison.Ordinal)..];
        var pricing = CensusPageSessions.IdOf(Pricing);

        await Assert.That(list).Contains($"<div class=\"sess\" id=\"sess-{pricing}\">");
        await Assert.That(list).Contains($"<details class=\"sess-more\" id=\"d-sess-{pricing}\"><summary id=\"d-sess-{pricing}-s\">Folder, size and traces</summary>");
        await Assert.That(list).Contains("<dt>Size on disk</dt><dd>188 MB</dd>");
        await Assert.That(list).Contains("Recordings Playwright can replay, in the session's output folder.");
        await Assert.That(list).DoesNotContain("ll-session-");
        await Assert.That(list).DoesNotContain(">Details</a>");
    }

    /// <summary>
    /// A session's panel says what its own processes use, its CPU and its own memory, right
    /// under its browser, and its shared memory is the one figure for all of BrowserAI,
    /// linked to where the home says it.
    /// </summary>
    /// <remarks>
    /// <b>59 b, 62 a and 65 a, the maintainer's of 2026-10-11</b>, with his words verbatim:
    /// <i>"Show this all with realtime updates. Show the separate process memory and the shared
    /// base separately. The page has to show it all clearly."</i> The lines are
    /// <see cref="UsageContent.SessionLines"/>, which the home's work made for the inspector.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASessionsPanelSaysItsCpuAndOwnMemoryAndPointsItsSharedMemoryAtTheOneTotal()
    {
        const long MiB = 1024 * 1024;
        var pricing = CensusPageSessions.IdOf(Pricing);

        // Two readings two seconds apart on four processors: the session added 2,080,000 ticks
        // of 80,000,000, which is 2.6% of the PC.
        var first = PageUsage.Next(null, new UsageReading(new ProcessCounters(0, 0, 0), [], [new SessionReading(pricing, 0, 405 * MiB, 133 * MiB, 141 * MiB)], 4), Now, TimeSpan.FromMinutes(2));
        var second = PageUsage.Next(first, new UsageReading(new ProcessCounters(0, 0, 0), [], [new SessionReading(pricing, 2_080_000, 405 * MiB, 133 * MiB, 141 * MiB)], 4), Now.AddSeconds(2), TimeSpan.FromMinutes(2));

        var html = ConversationsContent.Render(new PageView(Facts(), Mixed(), null) { Tab = 1, Now = Now.AddSeconds(2), Usage = second });
        var panel = PanelOf(html, "s-" + pricing);

        await Assert.That(panel).Contains($"<dt>CPU</dt><dd data-usage=\"s:{pricing}:cpu\">2.6%</dd>");
        await Assert.That(panel).Contains($"<dt>Own memory</dt><dd data-usage=\"s:{pricing}:own\">538 MB</dd>");
        await Assert.That(panel).Contains($"<dt>Shared memory</dt><dd><a id=\"l-shared-{pricing}\" href=\"./?tab=1#card-usage\" data-usage=\"shared-all\">About 141 MB for all of BrowserAI</a></dd>");
        await Assert.That(panel).Contains("<p class=\"kind\">Uses now</p>");

        var browser = panel.IndexOf("<dt>Browser</dt>", StringComparison.Ordinal);
        var cpu = panel.IndexOf("<dt>CPU</dt>", StringComparison.Ordinal);
        var purpose = panel.IndexOf("<dt>For</dt>", StringComparison.Ordinal);

        await Assert.That(browser).IsGreaterThan(-1);
        await Assert.That(browser).IsLessThan(cpu);
        await Assert.That(cpu).IsLessThan(purpose);

        // Before the first reading every figure says it is being measured.
        var none = PanelOf(ConversationsContent.Render(new PageView(Facts(), Mixed(), null) { Tab = 1, Now = Now }), "s-" + pricing);

        await Assert.That(none).Contains($"<dt>CPU</dt><dd data-usage=\"s:{pricing}:cpu\">Measuring</dd>");
    }

    // ---- The rig's data -----------------------------------------------------------

    /// <summary>What does not change, for an arm that draws a page straight from a view.</summary>
    private static PageFacts Facts() => new()
    {
        Version = "9.0.0",
        InstallRoot = @"C:\install",
        DataRoot = @"C:\data",
        LogDirectory = @"C:\data\logs",
        ServerCommand = null,
        ServerRefusal = null,
        RelayHold = BrowserAI.Relay.RelayConstants.IdleCountdown,
    };

    /// <summary>The tab every card on the map carries, which says it can be chosen and whether it is.</summary>
    internal const string Cue = "<span class=\"cue\" aria-hidden=\"true\"><span class=\"off\">Details</span><span class=\"on\">Selected</span>";

    /// <summary>
    /// A conversation in a terminal using three sessions, its closed one first, then a window
    /// and a hidden browser; a Codex conversation with none; and a session kept after its
    /// conversation ended.
    /// </summary>
    /// <param name="historyOpen">Whether the first session's browser is open, hidden.</param>
    /// <param name="traces">Whether the window's session holds a trace.</param>
    /// <returns>The snapshot.</returns>
    internal static SessionsSnapshot Mixed(bool historyOpen = false, bool traces = false)
    {
        IReadOnlyDictionary<string, IReadOnlyList<TraceEntry>>? recorded = traces
            ? new Dictionary<string, IReadOnlyList<TraceEntry>>(StringComparer.OrdinalIgnoreCase)
            {
                [Pricing] = [new TraceEntry("t-1", "checkout-total.zip", Pricing + @"\output\checkout-total.zip")],
            }
            : null;

        var composed = CensusPageSessions.Compose(
            Now,
            [
                ("background", DashboardTests.Description(900, ServerDescription.Roles.Host,
                [
                    new HeldSession(History, "reading last year's prices from the web archive", BrowserOpen: historyOpen, DrivenBy: "claude-code", DrivenThrough: 401, IdleCloseAt: historyOpen ? Now.AddMinutes(9) : null),
                    new HeldSession(Pricing, "comparing the three pricing pages side by side", BrowserOpen: true, Headed: true, DrivenBy: "claude-code", DrivenThrough: 401, IdleCloseAt: Now.AddMinutes(48)),
                    new HeldSession(Archive, "saving the pricing pages as they are today", BrowserOpen: true, DrivenBy: "claude-code", DrivenThrough: 401, IdleCloseAt: Now.AddMinutes(4)),
                    new HeldSession(Login, "checking that the sign-in survives a reload", BrowserOpen: true, Kept: true, IdleCloseAt: Now.AddMinutes(17)),
                ])),
                ("relay:a", DashboardTests.Description(401, ServerDescription.Roles.Relay, [], "claude-code", @"C:\Code\market-notes")),
                ("relay:b", DashboardTests.Description(402, ServerDescription.Roles.Relay, [], "codex-mcp-client", @"C:\Code\docs-site")),
            ],
            [],
            recorded);

        return composed with
        {
            Servers =
            [
                .. composed.Servers.Select(server => server.Description.ProcessId switch
                {
                    401 => server with { Conversation = new ConversationName("Compare pricing pages", IsTitle: true), Reconnect = RelayReconnect.McpReconnect },
                    402 => server with { Conversation = new ConversationName("Update the install guide screenshots", IsTitle: true), Reconnect = RelayReconnect.NewConversation },
                    _ => server,
                }),
            ],
        };
    }

    /// <summary>The map's part of a page: from the map's opening tag to the inspector's.</summary>
    internal static string MapOf(string page)
    {
        var start = page.IndexOf("<div class=\"map\" id=\"map\">", StringComparison.Ordinal);
        var end = page.IndexOf("<aside class=\"inspector\"", StringComparison.Ordinal);

        return start >= 0 && end > start ? page[start..end] : string.Empty;
    }

    /// <summary>One panel of the inspector, by the key of the card it is for.</summary>
    internal static string PanelOf(string html, string key)
    {
        var start = html.IndexOf("id=\"inspect-" + key + "\"", StringComparison.Ordinal);

        if (start < 0)
        {
            return string.Empty;
        }

        // A panel ends where the next one begins, or where the inspector does: a panel may
        // hold sections of its own.
        var next = html.IndexOf("<section class=\"inspect\" id=\"inspect-", start, StringComparison.Ordinal);
        var aside = html.IndexOf("</aside>", start, StringComparison.Ordinal);
        var end = next > start && (aside < 0 || next < aside) ? next : aside;

        return end > start ? html[start..end] : html[start..];
    }

    /// <summary>One row of the storage page's table, by the folder's name.</summary>
    private static string RowOf(string html, string name)
    {
        var at = html.IndexOf(name, StringComparison.Ordinal);

        if (at < 0)
        {
            return string.Empty;
        }

        var start = html.LastIndexOf("<tr>", at, StringComparison.Ordinal);
        var end = html.IndexOf("</tr>", at, StringComparison.Ordinal);

        return start >= 0 && end > start ? html[start..end] : string.Empty;
    }
}
