// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.Globalization;
using BrowserAI.App.Page;
using BrowserAI.Proxy;
using BrowserAI.Relay;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>
/// The dashboard's update page: what holds a downloaded update, split three ways
/// with a countdown each, what installing now does, and the button that does it.
/// </summary>
/// <remarks>
/// <para>
/// <b>T and H1, decided 2026-10-08 by the maintainer, his words verbatim:</b>
/// <i>"the install now button takes you to the browser interface gui of the
/// coordinator where it can better explain what the risks of forcing the update now
/// are together with an overview of who is still using it"</i>, and <i>"the
/// coordinator can show what is holding up the update clearly split by browsers,
/// relays, etc..."</i>
/// </para>
/// <para>
/// <b>The page is rendered from snapshots</b>, the contract the background fills,
/// and served through the page's own listener with a scripted background behind it;
/// nothing here reaches a browser, a session or the screen.
/// </para>
/// <para>
/// <i>Corrected 2026-10-11 throughout, by the maintainer's 40 a and 42 a:</i> the page is
/// direction A's update page, a countdown above three cards, and it names a conversation
/// where it named an agent's connection. Each arm says what it held before.
/// </para>
/// </remarks>
internal sealed class UpdatePageTests
{
    /// <summary>The instant every rig's clock starts at.</summary>
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    /// <summary>
    /// A held update is shown as a countdown above three cards, windows on the screen,
    /// hidden browsers and conversations, each thing with its countdown as a deadline the
    /// page's script counts down; a window says to close it, each conversation says the
    /// reconnect its app will need, and what installing now does is said beside the button.
    /// </summary>
    /// <remarks>
    /// <i>Corrected 2026-10-11 (previously the heading "Update", the held sentence "BrowserAI
    /// 1.2.0 is downloaded and ready to install. It installs by itself once BrowserAI has been
    /// idle.", a list of agents, and each session's purpose in bold above its folder)</i>.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheUpdatePageSplitsWhatHoldsTheUpdateAndGivesEachItsCountdown()
    {
        var html = Render(Held());
        var at = Now.AddMinutes(50).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

        await Assert.That(html).Contains("<h1 class=\"page-title\">Update 1.2.0 is ready to install</h1>");
        await Assert.That(html).Contains("It installs by itself once nothing uses BrowserAI, so no agent is cut off in the middle of its work.");
        await Assert.That(html).Contains($"<div class=\"clock\">{Countdown(Now.AddMinutes(50), "50:00")}</div>");
        await Assert.That(html).Contains($"Installs by itself at about {at}");
        await Assert.That(html).Contains("if nothing below is used before then. Using anything below starts its countdown again.");

        var hidden = Section(html, "hidden");

        await Assert.That(hidden).Contains("<b>hidden</b>");
        await Assert.That(hidden).Contains("&lt;b&gt;Reads&lt;/b&gt; the release notes");
        await Assert.That(hidden).Contains(Countdown(Now.AddMinutes(5), "5:00"));
        await Assert.That(hidden).Contains("Closes by itself if no call names it.");

        // #78, 2026-10-10: a window's countdown starts again on a call that names its
        // session and on the person's input in it, as browserai_init says.
        var windows = Section(html, "windows");

        await Assert.That(windows).Contains("Close this window to let the update go ahead, or it closes by itself if nobody types or clicks in it and no call names it.");
        await Assert.That(windows).Contains(Countdown(Now.AddMinutes(50), "50:00"));

        var conversations = Section(html, "conversations");

        await Assert.That(conversations).Contains("<b>Claude Code in one</b>");
        await Assert.That(conversations).Contains("<p class=\"sub\">Claude Code 2.1.290</p>");
        await Assert.That(conversations).Contains("<b>Codex in two</b>");
        // #84, 2026-10-10: a client's ping does not start a conversation's ten minutes again.
        // Since 2026-10-12 the length is the relay's countdown, the page facts' 10 min here
        // (previously "for ten minutes", written in the string).
        await Assert.That(conversations).Contains("Each holds the update for 10 min after its app last sent BrowserAI anything but a ping.");
        await Assert.That(conversations).Contains(Countdown(Now.AddMinutes(8), "8:00"));
        await Assert.That(conversations).Contains("Holds the update until its countdown runs out, if its app sends nothing but pings.");
        await Assert.That(conversations).Contains(PageContent.Text(UpdatePageContent.ReconnectSentence(RelayReconnect.McpReconnect)));
        await Assert.That(conversations).Contains("Idle: it no longer holds the update.");
        await Assert.That(conversations).Contains(PageContent.Text(UpdatePageContent.ReconnectSentence(RelayReconnect.NewConversation)));
        await Assert.That(conversations).Contains("A call is running now, so it holds the update.");
        await Assert.That(conversations).Contains(PageContent.Text(UpdatePageContent.ReconnectSentence(RelayReconnect.None)));
        await Assert.That(conversations).DoesNotContain("agent");

        var install = Section(html, "install-now");

        foreach (var effect in UpdatePageContent.InstallNowEffects("1.2.0"))
        {
            await Assert.That(install).Contains(PageContent.Text(effect));
        }

        await Assert.That(install).Contains("data-action=\"install-now\" data-version=\"1.2.0\">Install BrowserAI 1.2.0 now</button>");
        await Assert.That(html.Split("data-action=\"install-now\"").Length - 1).IsEqualTo(2).Because("one at the countdown and one after what it does");
    }

    /// <summary>
    /// Each conversation is named the way the person sees it, a title in quotes and
    /// BrowserAI's own words as they are, with its app under it; a VS Code window's tabs are
    /// listed together under the window where the first of them stands; and a renamed
    /// conversation is a new state for the page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>1.2 a and 1.5 a, the maintainer's answer of 2026-10-10, verbatim: <i>"1.1-2.3 I
    /// accept all your recommendations"</i></b>. Two windows on one folder are two groups
    /// with one label, which is what the person has on the screen too.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a page that listed every agent in its own
    /// order with no window, and against a signature that left the name out, which kept
    /// a renamed conversation off an open tab. <i>Corrected 2026-10-11 (previously
    /// EachAgentIsNamedByItsConversationAndAWindowsTabsAreListedTogether, with the client's
    /// protocol name and folder beside each name)</i>, 42 a: the app, by its name.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EachConversationIsNamedAsThePersonSeesItAndAWindowsTabsAreListedTogether()
    {
        var window = new ClientWindow("1200-134360600000000000", @"C:\Source\BrowserAI");
        var second = new ClientWindow("1300-134360600000000001", @"C:\Source\BrowserAI");

        static HoldingRelay claude(string folder, RelayReconnect reconnect, string id, ConversationName name, ClientWindow? window) =>
            new("claude-code 2.1.296", folder, Now.AddMinutes(8), CallInFlight: false, reconnect, id, name, window)
            {
                ClientName = KnownClients.ClaudeCode,
                ClientVersion = "2.1.296",
            };

        var holds = new UpdateHoldSnapshot(Now, UpdateHoldState.Held, "1.2.0", [], [],
        [
            claude(@"C:\Source\BrowserAI", RelayReconnect.None, "s1", new ConversationName("Fix the <login> bug", IsTitle: true), window),
            claude(@"C:\Source\one", RelayReconnect.McpReconnect, "s2", new ConversationName("Claude Code in one", IsTitle: false), null),
            claude(@"C:\Source\BrowserAI", RelayReconnect.None, "s3", new ConversationName("unnamed conversation in BrowserAI", IsTitle: false), window),
            claude(@"C:\Source\BrowserAI", RelayReconnect.None, "s4", new ConversationName("Second window", IsTitle: true), second),
            new HoldingRelay("someclient 1", null, Now.AddMinutes(8), CallInFlight: false, RelayReconnect.Unknown) { ClientName = "someclient", ClientVersion = "1" },
        ]);

        var conversations = Section(Render(holds), "conversations");

        var first = conversations.IndexOf("<b>" + PageContent.Text("\"Fix the <login> bug\"") + "</b>", StringComparison.Ordinal);
        var unnamedTab = conversations.IndexOf("<b>unnamed conversation in BrowserAI</b>", StringComparison.Ordinal);
        var terminal = conversations.IndexOf("<b>Claude Code in one</b>", StringComparison.Ordinal);
        var secondWindow = conversations.IndexOf("<b>" + PageContent.Text("\"Second window\"") + "</b>", StringComparison.Ordinal);
        var stranger = conversations.IndexOf("<b>someclient</b>", StringComparison.Ordinal);

        await Assert.That(new[] { first, unnamedTab, terminal, secondWindow, stranger }.All(at => at >= 0)).IsTrue().Because(conversations);

        // The first window's two tabs together, where its first tab stands; then the
        // terminal; then the second window; then the app that gave no record.
        await Assert.That(conversations.IndexOf("VS Code window on BrowserAI", StringComparison.Ordinal)).IsLessThan(first);
        await Assert.That(first).IsLessThan(unnamedTab);
        await Assert.That(unnamedTab).IsLessThan(terminal);
        await Assert.That(terminal).IsLessThan(secondWindow);
        await Assert.That(secondWindow).IsLessThan(stranger);
        await Assert.That(conversations.Split("VS Code window on BrowserAI").Length - 1).IsEqualTo(2).Because("two windows on one folder are two groups");

        // Each with its app, by the app's own name.
        await Assert.That(conversations.Split("<p class=\"sub\">Claude Code 2.1.296</p>").Length - 1).IsEqualTo(4);
        await Assert.That(conversations).DoesNotContain("claude-code");

        // A renamed conversation is a new state for an open tab.
        var renamed = holds with { Relays = [.. holds.Relays.Select(relay => relay.Conversation is "s1" ? relay with { Label = new ConversationName("Renamed", IsTitle: true) } : relay)] };

        await Assert.That(UpdatePageContent.Signature(renamed, Now)).IsNotEqualTo(UpdatePageContent.Signature(holds, Now));
    }

    /// <summary>
    /// An app is called the same on the update page and on the conversations page: Claude
    /// Code or Codex by its own name and its version, any other app by the name it gave, and
    /// one that gave none as an unnamed app, which no version follows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Item 85 of the texts review of 2026-10-10</b>: the update page said <i>unnamed
    /// client</i> and the sessions page <i>A client that has not said what it is</i> with
    /// the version after it, for one relay. A version with no name tells a person
    /// nothing.
    /// </para>
    /// <para>
    /// <i>Corrected 2026-10-11 (previously AClientIsCalledTheSameOnTheUpdatePageAndTheSessionsPage,
    /// which held the protocol's own name, claude-code, and unnamed client)</i>: 42 a, the
    /// app, and no client, on the page. The update core's own words for its log and the
    /// toast are <see cref="BackgroundUpdates.ClientOf"/>'s and did not move.
    /// </para>
    /// </remarks>
    /// <param name="name">What the client called itself, or <see langword="null"/>.</param>
    /// <param name="version">Its version, or <see langword="null"/>.</param>
    /// <param name="expected">What both pages call it.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments("claude-code", "2.1.296", "Claude Code 2.1.296")]
    [Arguments("claude-code", null, "Claude Code")]
    [Arguments("codex-mcp-client", "0.165.0", "Codex 0.165.0")]
    [Arguments("someclient", "1", "someclient 1")]
    [Arguments(null, "2.1.296", PageWords.UnnamedApp)]
    [Arguments(null, null, PageWords.UnnamedApp)]
    public async Task AnAppIsCalledTheSameOnTheUpdatePageAndTheConversationsPage(string? name, string? version, string expected)
    {
        var relay = new HoldingRelay(ClientNames.Of(name, version), @"C:\project", Now.AddMinutes(8), CallInFlight: false, RelayReconnect.Unknown)
        {
            ClientName = name,
            ClientVersion = version,
        };

        var update = Section(Render(new UpdateHoldSnapshot(Now, UpdateHoldState.Held, "1.2.0", [], [], [relay])), "conversations");

        await Assert.That(update).Contains("<p class=\"sub\">" + PageContent.Text(expected) + "</p>").Because(update);
        await Assert.That(PageWords.AppWithVersion(name, version)).IsEqualTo(expected);

        var snapshot = CensusPageSessions.Compose(
            Now,
            [
                ("background", DashboardTests.Description(900, Coordination.ServerDescription.Roles.Host, [])),
                ("relay:1", new Coordination.ServerDescription(301, 1, new Coordination.ClientIdentity(name, Title: null, version), @"C:\project", null, 0, [], Coordination.ServerDescription.Roles.Relay)),
            ],
            []);

        var conversations = PageContent.Fragment(new PageView(Facts(), snapshot, null), PageKind.Sessions, 1, Occasion.Ordinary, Now);
        var list = conversations[conversations.IndexOf("<div class=\"list-view\">", StringComparison.Ordinal)..];

        await Assert.That(list).Contains(PageContent.Text(expected)).Because(list);
        // The word, not the letters: an app may call itself someclient.
        await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(list, @"\bclient\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).IsFalse().Because(list);
    }

    /// <summary>
    /// A wait no countdown leads says what it waits for, and a session or a window set
    /// never to close says so where its countdown would be.
    /// </summary>
    /// <remarks>
    /// <i>Corrected 2026-10-11 (previously each wait was one sentence under the held one,
    /// and an empty list said "No agent is connected to BrowserAI.")</i>: the countdown's
    /// place says what is waited for, and a conversation, never an agent, is connected.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AWaitNoCountdownLeadsSaysWhatItWaitsFor()
    {
        var empty = new UpdateHoldSnapshot(Now, UpdateHoldState.Held, "1.2.0", [], [], []);

        var window = Render(empty with { VisibleWindows = [new HoldingSession(@"C:\w", null, null)] });

        await Assert.That(window).Contains("<b>Waiting for a window</b>");
        await Assert.That(window).Contains(PageContent.Text("A window set never to close by itself holds the update. It installs once you close that window and nothing else uses BrowserAI."));
        await Assert.That(Section(window, "windows")).Contains("Never closes");
        await Assert.That(Section(window, "windows")).Contains("Set never to close by itself. Close this window to let the update go ahead.");
        await Assert.That(Section(window, "windows")).Contains("No purpose recorded");

        var session = Render(empty with { HiddenSessions = [new HoldingSession(@"C:\h", null, null)] });

        await Assert.That(session).Contains(PageContent.Text("A hidden browser set never to close by itself holds the update. It installs once an agent closes that session and nothing else uses BrowserAI."));
        await Assert.That(Section(session, "hidden")).Contains(PageContent.Text("Set never to close: an agent has to close it."));

        var call = Render(empty with { Relays = [new HoldingRelay("Codex", null, Now.AddMinutes(-1), CallInFlight: true, RelayReconnect.NewConversation)] });

        await Assert.That(call).Contains(PageContent.Text("Every countdown has run out, and a call is still running: it installs once that call has finished."));

        var closing = Render(empty with { HiddenSessions = [new HoldingSession(@"C:\h", null, Now.AddSeconds(-1))] });

        await Assert.That(closing).Contains(PageContent.Text("Every countdown has run out: it installs once the last browser has closed."));
        await Assert.That(Section(closing, "hidden")).Contains("Closing now");

        var nothing = Render(empty);

        await Assert.That(nothing).Contains(PageContent.Text("Nothing uses BrowserAI now: it installs in a moment."));
        await Assert.That(Section(nothing, "hidden")).Contains("No hidden browser is open.");
        await Assert.That(Section(nothing, "windows")).Contains("No window is open.");
        await Assert.That(Section(nothing, "conversations")).Contains("No conversation is connected.");
    }

    /// <summary>
    /// With nothing waiting the page says which version is installed, or that this
    /// BrowserAI is not installed, as the home does; while it installs it says
    /// so and offers no button; and where reading what holds the update failed it
    /// says that, and where the log is.
    /// </summary>
    /// <remarks>
    /// <i>Corrected 2026-10-10 (previously "and where nothing reports what holds an
    /// update it says that, and claims nothing else")</i>: the page always has the
    /// update core since the one background (#73), so a snapshot it does not have is a
    /// read that failed, and a background that is not installed holds nothing (#71).
    /// <i>Corrected 2026-10-11 (previously each was a paragraph under the heading
    /// "Update")</i>: the heading says it, 40 a.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePageSaysWhenNothingWaitsWhenItIsInstallingAndWhenTheReadFailed()
    {
        var none = Render(UpdateHoldSnapshot.Nothing(Now));

        await Assert.That(none).Contains("<h1 class=\"page-title\">No update is waiting</h1>");
        await Assert.That(none).Contains("<p class=\"lede\">BrowserAI 9.0.0 is installed.</p>");
        await Assert.That(none).Contains("When an update waits");
        await Assert.That(none).Contains("This page shows what is still using BrowserAI, with a countdown for each, and a button to install it now.");
        await Assert.That(none).DoesNotContain("data-action=\"install-now\"");

        var uninstalled = Render(UpdateHoldSnapshot.Nothing(Now), installed: false);

        await Assert.That(uninstalled).Contains("<p class=\"lede\">This BrowserAI is not installed, so there is nothing to update.</p>");
        await Assert.That(uninstalled).DoesNotContain("is installed.</p>");

        var installing = Render(new UpdateHoldSnapshot(Now, UpdateHoldState.Installing, "1.2.0", [], [], []));

        await Assert.That(installing).Contains("<h1 class=\"page-title\">Update 1.2.0 is installing</h1>");
        await Assert.That(installing).Contains("<p class=\"lede\">This page stops when BrowserAI closes, and BrowserAI starts again by itself.</p>");
        await Assert.That(installing).DoesNotContain("data-action=\"install-now\"");

        var unread = Render(null);

        await Assert.That(unread).Contains("<p>BrowserAI could not read what holds an update. Its log, in <code>C:\\data\\logs</code>, says why.</p>");
        await Assert.That(unread).DoesNotContain("does not report");
        await Assert.That(unread).DoesNotContain("data-action=\"install-now\"");
    }

    /// <summary>
    /// A held or installing update always names its version, so no page has to word
    /// one that does not.
    /// </summary>
    /// <remarks>
    /// <b>#62, #64 and #94 of the texts review, 2026-10-10</b>: the page wrote
    /// <i>"BrowserAI the new version is downloaded"</i>, <i>"is installing now"</i> and
    /// <i>"Install BrowserAI the new version now"</i> for a version the background
    /// never leaves out. The contract now refuses such a snapshot, and the fallback is
    /// gone.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AHeldOrInstallingUpdateAlwaysNamesItsVersion()
    {
        _ = Assert.Throws<ArgumentException>(() => _ = new UpdateHoldSnapshot(Now, UpdateHoldState.Held, null, [], [], []));
        _ = Assert.Throws<ArgumentException>(() => _ = new UpdateHoldSnapshot(Now, UpdateHoldState.Installing, null, [], [], []));
        _ = Assert.Throws<ArgumentException>(() => _ = new UpdateHoldSnapshot(Now, UpdateHoldState.Held, string.Empty, [], [], []));

        await Assert.That(UpdateHoldSnapshot.Nothing(Now).Version).IsNull();
    }

    /// <summary>
    /// A version older than the one installed is said to be older, and what installing
    /// it does, on the update page and on the home alike; a newer one is not.
    /// </summary>
    /// <remarks>
    /// <b>Q308 a, the maintainer's words of 2026-10-03 verbatim: <i>"Q308 a"</i></b>:
    /// automatic rollback stays, and the interface says when the offered version is
    /// older. Built again 2026-10-10 from what the update core reads, after the page's
    /// own check, which said it, was deleted under his "9 a".
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnOlderVersionIsSaidToBeOlderOnTheUpdatePageAndTheHome()
    {
        const string Said = "<p class=\"warning\">It is older than the installed 9.0.0. Installing it goes back to the earlier version.</p>";

        var older = Held() with { Version = "8.5.0", Older = true };

        await Assert.That(Render(older)).Contains("<h1 class=\"page-title\">Update 8.5.0 is ready to install</h1>");
        await Assert.That(Render(older)).Contains(Said);
        await Assert.That(Home(older)).Contains(Said);

        await Assert.That(Render(Held())).DoesNotContain("older than");
        await Assert.That(Home(Held())).DoesNotContain("older than");

        // ⚠️ And what installing now does names the version that installs, round 2 of
        // the texts review, 2026-10-10, #118 (previously "once the new version is
        // installed", above an older one). Planted red against "the new version".
        await Assert.That(Section(Render(older), "install-now")).Contains("BrowserAI starts again by itself once 8.5.0 is installed, and a notification says so.");
        await Assert.That(Section(Render(older), "install-now")).Contains("Claude Code in VS Code reconnects by itself; in a terminal, run /mcp there, choose BrowserAI, then Reconnect.");
        await Assert.That(Section(Render(older), "install-now")).DoesNotContain("new version");

        // An open tab hears of it: the watch's signature tells the two apart.
        await Assert.That(UpdatePageContent.Signature(older, Now)).IsNotEqualTo(UpdatePageContent.Signature(older with { Older = false }, Now));
    }

    /// <summary>
    /// The update page is served at its own route and begins with the way back, and its
    /// button asks the background to install now: a refusal is a sentence on the page,
    /// and an install that started is said to every tab once, by the background's own
    /// stop, and never by the page as well.
    /// </summary>
    /// <remarks>
    /// <i>Corrected 2026-10-10 (previously "and an install that started tells every
    /// tab and ends their streams")</i>: #105 of the texts review. The page's own
    /// sentence and the background's were released by the same stop and raced, so a tab
    /// showed whichever came first, and only the background's says how to get the page
    /// back. <i>Corrected 2026-10-11 (previously the title "BrowserAI update" and the
    /// navigation's link marked as this page)</i>: 40 a, a way back and no row of links.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheUpdatePageIsServedAndItsButtonAsksTheBackgroundToInstallNow()
    {
        var holds = new ScriptedHolds(Held()) { Answer = "An agent's call is still running, so nothing was installed." };

        await using var rig = await PageRig.CreateAsync(holds: holds);

        var address = await rig.HandOutAsync(PageKind.Update);

        await Assert.That(address).Contains("update?tab=1");

        var page = await PageRig.GetAsync(address);

        await Assert.That(page.Status).IsEqualTo(200).Because(page.Raw);
        await Assert.That(page.Body).Contains("<title>Update - BrowserAI</title>");
        await Assert.That(page.Body).Contains("data-page=\"update\"");
        await Assert.That(page.Body).Contains(DashboardTests.Crumbs(1));
        await Assert.That(page.Body).Contains("<span aria-current=\"page\">Update</span>");
        await Assert.That(page.Body).Contains("data-action=\"install-now\" data-version=\"1.2.0\"");

        using var stream = await rig.StreamAsync(rig.Gate.Root, 1, "update");

        var refused = await rig.ActAsync("""{"action":"install-now","version":"1.2.0"}""");

        await Assert.That(refused.Status).IsEqualTo(204);
        await Assert.That(await StateContainingAsync(stream, "An agent&#x27;s call is still running, so nothing was installed.")).IsNotNull();
        await Assert.That(string.Join(",", holds.Asked)).IsEqualTo("1.2.0");

        holds.Answer = null;
        _ = await rig.ActAsync("""{"action":"install-now","version":"1.2.0"}""");

        // The page's own note while the request runs, then a new state once the
        // background has the install, and no word of its own to the tabs.
        await Assert.That(await StateContainingAsync(stream, "Closing every browser and disconnecting every conversation, then installing BrowserAI 1.2.0.")).IsNotNull();

        var handedOver = await stream.NextAsync();

        await Assert.That(handedOver?.Name).IsEqualTo(PageEvents.State).Because("the page tells no tab the install has started: the background's stop does");
        await Assert.That(string.Join(",", holds.Asked)).IsEqualTo("1.2.0,1.2.0");

        // The background's stop, as Program.Background says it, is the one sentence.
        // ⚠️ Since the texts polish of 2026-10-10, pages #131 and #132 (previously "... so
        // this tab has stopped. Open BrowserAI from the Start Menu again once the installed
        // notification has appeared."), and a build that is not installed is not sent to
        // the Start Menu, which starts another BrowserAI.
        const string Stopped = "BrowserAI is installing an update, so this page has stopped. When a notification says the update is installed, open BrowserAI from the Start Menu again.";

        await Assert.That(Program.StopSentence(BrowserAI.Background.BackgroundState.Updating, @"C:\install")).IsEqualTo(Stopped);
        await Assert.That(Program.StopSentence(BrowserAI.Background.BackgroundState.Stopping, @"C:\install"))
            .IsEqualTo("BrowserAI's background has stopped, so this page has stopped. Open BrowserAI from the Start Menu to start it again.");
        await Assert.That(Program.StopSentence(BrowserAI.Background.BackgroundState.Stopping, null))
            .IsEqualTo("BrowserAI's background has stopped, so this page has stopped.");

        rig.Page.Tell(Stopped);

        var closing = await stream.NextNamedAsync(PageEvents.Closing);

        await Assert.That(closing).IsNotNull();
        await Assert.That(closing!.Member("sentence")).IsEqualTo(Stopped);
    }

    // RETIRED 2026-10-10: WithNothingReportingTheInstallRequestIsRefused. The page is
    // always given the update core since the one background (Program.Background sets
    // it, and PageService now requires it), so there is no page with nothing reporting
    // what holds an update, and no request to refuse at the door; an install-now is
    // the update core's to answer (#73 of the texts review).

    /// <summary>
    /// Every tab is sent a new state once what holds the update has changed, read
    /// once a second while a listener is up; a second that changes nothing sends
    /// nothing.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ATabIsSentANewStateWhenWhatHoldsTheUpdateChanges()
    {
        var holds = new ScriptedHolds(Held());

        await using var rig = await PageRig.CreateAsync(holds: holds);

        _ = await rig.HandOutAsync(PageKind.Update);

        using var stream = await rig.StreamAsync(rig.Gate.Root, 1, "update");

        // The stream's own first state.
        await Assert.That(await StateContainingAsync(stream, "Claude Code 2.1.290")).IsNotNull();

        holds.Snapshot = Held() with
        {
            Relays = [.. Held().Relays, Relay("codex-mcp-client", "0.162.0", @"C:\Source\four", Now.AddMinutes(9), callInFlight: false, RelayReconnect.NewConversation)],
        };

        rig.Clock.Advance(PageService.HoldsWatchPeriod);

        var changed = await StateContainingAsync(stream, "Codex 0.162.0");

        await Assert.That(changed).IsNotNull();
        await Assert.That(changed!).Contains("<b>Codex in four</b>");
    }

    /// <summary>
    /// The home's update card says what is waiting and leads to the update page, and
    /// never that no release feed is set, because the background has one; where the read
    /// failed, it says that, and where the log is.
    /// </summary>
    /// <remarks>
    /// The background builds its page with no feed of its own since 2026-10-08, and the
    /// page's own check, offer and install are deleted (2026-10-10, the maintainer's
    /// "9 a"). The sentence it showed, "No release feed is set for this build", was
    /// written for a build with no feed at all. <i>Corrected 2026-10-11 (previously
    /// TheStatusPageSaysWhatTheBackgroundHoldsAndNeverThatNoFeedIsSet, over the status
    /// page's update section and its link "See what holds it, or install it now")</i>: the
    /// home's card and its button, 40 a.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheHomesUpdateCardSaysWhatTheBackgroundHoldsAndNeverThatNoFeedIsSet()
    {
        const string NoFeed = "No release feed is set for this build";

        var held = Home(Held());

        await Assert.That(held).Contains("<p class=\"big\">1.2.0 is ready</p>");
        await Assert.That(held).Contains("<a id=\"l-update\" class=\"btn primary\" href=\"update?tab=3\">See what holds it</a>");
        await Assert.That(held).DoesNotContain(NoFeed);
        await Assert.That(held).DoesNotContain("data-action=\"check-updates\"");

        var none = Home(UpdateHoldSnapshot.Nothing(Now));

        await Assert.That(none).Contains("<p class=\"big\">No update is waiting</p>");
        await Assert.That(none).DoesNotContain(NoFeed);

        var installing = Home(new UpdateHoldSnapshot(Now, UpdateHoldState.Installing, "1.2.0", [], [], []));

        await Assert.That(installing).Contains("<p class=\"big\">1.2.0 is installing</p>");
        await Assert.That(installing).DoesNotContain(NoFeed);

        // #66, 2026-10-10 (previously "where nothing reports, the stage's own sentence
        // stands"): a read that failed is said as one.
        var unread = Home(null);

        await Assert.That(unread).Contains("<p>BrowserAI could not read what holds an update. Its log, in <code>C:\\data\\logs</code>, says why.</p>");
        await Assert.That(unread).DoesNotContain(NoFeed);
    }

    /// <summary>
    /// A home tab is sent a new state when an update becomes held, by the same
    /// once-a-second watch that keeps the update page current.
    /// </summary>
    /// <remarks><i>Corrected 2026-10-11 (previously AStatusTabIsSentANewStateWhenAnUpdateBecomesHeld)</i>: the home, 40 a.</remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AHomeTabIsSentANewStateWhenAnUpdateBecomesHeld()
    {
        var holds = new ScriptedHolds(UpdateHoldSnapshot.Nothing(Now));

        await using var rig = await PageRig.CreateAsync(holds: holds);

        _ = await rig.HandOutAsync(PageKind.Status);

        using var stream = await rig.StreamAsync(rig.Gate.Root, 1);

        await Assert.That(await StateContainingAsync(stream, "No update is waiting")).IsNotNull();

        holds.Snapshot = Held();
        rig.Clock.Advance(PageService.HoldsWatchPeriod);

        await Assert.That(await StateContainingAsync(stream, "1.2.0 is ready")).IsNotNull();
    }

    /// <summary>What does not change, for an install at the given place.</summary>
    private static PageFacts Facts(bool installed = true) => new()
    {
        Version = "9.0.0",
        InstallRoot = installed ? @"C:\install" : null,
        DataRoot = @"C:\data",
        LogDirectory = @"C:\data\logs",
        ServerCommand = null,
        ServerRefusal = null,
        RelayHold = RelayConstants.IdleCountdown,
    };

    /// <summary>The home's main part, rendered for tab 3.</summary>
    private static string Home(UpdateHoldSnapshot? holds) =>
        PageContent.Fragment(new PageView(Facts(), SessionsSnapshot.Empty, null, Holds: holds), PageKind.Status, 3, Occasion.Ordinary, Now);

    private static string Render(UpdateHoldSnapshot? holds, bool installed = true) =>
        UpdatePageContent.Render(new PageView(Facts(installed), SessionsSnapshot.Empty, null, Holds: holds) { Now = Now }, Now);

    /// <summary>A countdown as the page writes it.</summary>
    private static string Countdown(DateTimeOffset ends, string text) =>
        string.Create(CultureInfo.InvariantCulture, $"<span class=\"countdown\" data-ends-at=\"{ends.ToUnixTimeMilliseconds()}\">{text}</span>");

    /// <summary>One section of the page, by its id.</summary>
    private static string Section(string html, string id)
    {
        var start = html.IndexOf($"<section id=\"{id}\"", StringComparison.Ordinal);

        return start < 0 ? string.Empty : html[start..html.IndexOf("</section>", start, StringComparison.Ordinal)];
    }

    /// <summary>
    /// A held update: a hidden browser closing in five minutes, a visible window in
    /// fifty, a terminal relay in eight, a Codex relay already out, and a VS Code
    /// relay past its countdown with a call running.
    /// </summary>
    private static UpdateHoldSnapshot Held() => new(
        Now,
        UpdateHoldState.Held,
        "1.2.0",
        [new HoldingSession(@"C:\work\hidden", "<b>Reads</b> the release notes", Now.AddMinutes(5))],
        [new HoldingSession(@"C:\work\window", "A form the person fills in", Now.AddMinutes(50))],
        [
            Relay("claude-code", "2.1.290", @"C:\Source\one", Now.AddMinutes(8), callInFlight: false, RelayReconnect.McpReconnect),
            Relay("codex-mcp-client", "0.161.0", @"C:\Source\two", Now.AddMinutes(-1), callInFlight: false, RelayReconnect.NewConversation),
            Relay("claude-code", "2.1.290", @"C:\Source\three", Now.AddMinutes(-2), callInFlight: true, RelayReconnect.None),
        ]);

    /// <summary>A relay that holds an update, with its app's own name and version beside the protocol's words, as the update core passes it.</summary>
    private static HoldingRelay Relay(string name, string version, string folder, DateTimeOffset idleAt, bool callInFlight, RelayReconnect reconnect) =>
        new(ClientNames.Of(name, version), folder, idleAt, callInFlight, reconnect)
        {
            ClientName = name,
            ClientVersion = version,
        };

    private static async Task<string?> StateContainingAsync(RawEventStream stream, string text)
    {
        while (await stream.NextNamedAsync(PageEvents.State) is { } state)
        {
            var html = state.Member("html");

            if (html.Contains(text, StringComparison.Ordinal) || html.Contains(PageContent.Text(text), StringComparison.Ordinal))
            {
                return html;
            }
        }

        return null;
    }

    /// <summary>What holds the update, and the background's answer to an install-now, as the arm sets them.</summary>
    private sealed class ScriptedHolds(UpdateHoldSnapshot snapshot) : IUpdateHolds
    {
        public UpdateHoldSnapshot Snapshot { get; set; } = snapshot;

        /// <summary>What <see cref="InstallNowAsync"/> answers: <see langword="null"/> once the install started.</summary>
        public string? Answer { get; set; }

        public ConcurrentQueue<string> Asked { get; } = new();

        public Task<UpdateHoldSnapshot> ReadAsync() => Task.FromResult(Snapshot);

        public UpdateHoldSnapshot ReadCountdown() => Snapshot;

        public Task<string?> InstallNowAsync(string version, CancellationToken cancellationToken)
        {
            Asked.Enqueue(version);
            return Task.FromResult(Answer);
        }
    }
}
