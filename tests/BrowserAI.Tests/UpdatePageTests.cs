// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.Globalization;
using BrowserAI.App.Page;
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
/// </remarks>
internal sealed class UpdatePageTests
{
    /// <summary>The instant every rig's clock starts at.</summary>
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    /// <summary>
    /// A held update is shown split into hidden browser sessions, visible windows and
    /// agents, each with its countdown as a deadline the page's script counts down;
    /// a visible window says to close it, each agent says the reconnect its client
    /// will need, and what installing now does is said beside the button.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheUpdatePageSplitsWhatHoldsTheUpdateAndGivesEachItsCountdown()
    {
        var html = Render(Held());

        await Assert.That(html).Contains("<h1>Update</h1>");
        await Assert.That(html).Contains("BrowserAI 1.2.0 is downloaded and ready to install. It installs by itself once BrowserAI has been idle.");
        await Assert.That(html).Contains($"It installs in {Countdown(Now.AddMinutes(50), "50:00")}, at about ");
        await Assert.That(html).Contains(", if nothing uses BrowserAI before then.");

        await Assert.That(Section(html, "hidden")).Contains("<strong>&lt;b&gt;Reads&lt;/b&gt; the release notes</strong><br><code>C:\\work\\hidden</code>");
        await Assert.That(Section(html, "hidden")).Contains($"Closes in {Countdown(Now.AddMinutes(5), "5:00")} if no call names it.");

        await Assert.That(Section(html, "windows")).Contains("<span class=\"warning\">Close this to let the update proceed.</span>");
        // #78, 2026-10-10: a visible window's countdown starts again on a call that
        // names its session and on the person's input in it, as browserai_init says.
        await Assert.That(Section(html, "windows")).Contains($"Closes by itself in {Countdown(Now.AddMinutes(50), "50:00")} if no call names it and nobody types or clicks in it.");

        var agents = Section(html, "agents");

        await Assert.That(agents).Contains("<strong>Claude Code 2.1.290</strong> in <code>C:\\Source\\one</code>");
        // #84, 2026-10-10: a client's ping does not start an agent's ten minutes again.
        await Assert.That(agents).Contains("An agent holds the update for ten minutes after its client last sent BrowserAI anything but a ping.");
        await Assert.That(agents).Contains($"Holds the update for {Countdown(Now.AddMinutes(8), "8:00")} more if its client sends nothing but pings.");
        await Assert.That(agents).Contains(PageContent.Text(UpdatePageContent.ReconnectSentence(RelayReconnect.McpReconnect)));
        await Assert.That(agents).Contains("Idle: it no longer holds the update.");
        await Assert.That(agents).Contains(PageContent.Text(UpdatePageContent.ReconnectSentence(RelayReconnect.NewConversation)));
        await Assert.That(agents).Contains("A call is running now, so it holds the update.");
        await Assert.That(agents).Contains(PageContent.Text(UpdatePageContent.ReconnectSentence(RelayReconnect.None)));

        var install = Section(html, "install-now");

        foreach (var effect in UpdatePageContent.InstallNowEffects("1.2.0"))
        {
            await Assert.That(install).Contains(PageContent.Text(effect));
        }

        await Assert.That(install).Contains("<button type=\"button\" data-action=\"install-now\" data-version=\"1.2.0\">Install BrowserAI 1.2.0 now</button>");
    }

    /// <summary>
    /// Each agent is named by its conversation the way the person sees it, a title in
    /// quotes and BrowserAI's own words as they are, before its client; a VS Code
    /// window's tabs are listed together under the window where the first of them
    /// stands; and a renamed conversation is a new state for the page.
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
    /// a renamed conversation off an open tab.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EachAgentIsNamedByItsConversationAndAWindowsTabsAreListedTogether()
    {
        var window = new ClientWindow("1200-134360600000000000", @"C:\Source\BrowserAI");
        var second = new ClientWindow("1300-134360600000000001", @"C:\Source\BrowserAI");

        var holds = new UpdateHoldSnapshot(Now, UpdateHoldState.Held, "1.2.0", [], [],
        [
            new HoldingRelay("claude-code 2.1.296", @"C:\Source\BrowserAI", Now.AddMinutes(8), CallInFlight: false, RelayReconnect.None, "s1", new ConversationName("Fix the <login> bug", IsTitle: true), window),
            new HoldingRelay("claude-code 2.1.296", @"C:\Source\one", Now.AddMinutes(8), CallInFlight: false, RelayReconnect.McpReconnect, "s2", new ConversationName("Claude Code in one", IsTitle: false)),
            new HoldingRelay("claude-code 2.1.296", @"C:\Source\BrowserAI", Now.AddMinutes(8), CallInFlight: false, RelayReconnect.None, "s3", new ConversationName("new conversation in BrowserAI", IsTitle: false), window),
            new HoldingRelay("claude-code 2.1.296", @"C:\Source\BrowserAI", Now.AddMinutes(8), CallInFlight: false, RelayReconnect.None, "s4", new ConversationName("Second window", IsTitle: true), second),
            new HoldingRelay("someclient 1", null, Now.AddMinutes(8), CallInFlight: false, RelayReconnect.Unknown),
        ]);

        var agents = Section(Render(holds), "agents");

        var first = agents.IndexOf("<strong>" + PageContent.Text("\"Fix the <login> bug\"") + "</strong>, claude-code 2.1.296 in <code>C:\\Source\\BrowserAI</code>", StringComparison.Ordinal);
        var unnamedTab = agents.IndexOf("<strong>new conversation in BrowserAI</strong>, claude-code 2.1.296", StringComparison.Ordinal);
        // ⚠️ BrowserAI's words for a conversation it could not name carry the folder's
        // name already, so the whole path is not said after them: round 2 of the texts
        // review, 2026-10-10, #110 (previously "... claude-code 2.1.296 in
        // <code>C:\Source\one</code>", the folder twice). Planted red against it.
        var terminal = agents.IndexOf("<strong>Claude Code in one</strong>, claude-code 2.1.296<br>", StringComparison.Ordinal);
        var secondWindow = agents.IndexOf("<strong>" + PageContent.Text("\"Second window\"") + "</strong>", StringComparison.Ordinal);
        var stranger = agents.IndexOf("<strong>someclient 1</strong>", StringComparison.Ordinal);

        await Assert.That(new[] { first, unnamedTab, terminal, secondWindow, stranger }.All(at => at >= 0)).IsTrue().Because(agents);

        // The first window's two tabs together, where its first tab stands; then the
        // terminal; then the second window; then the client named nothing.
        await Assert.That(agents.IndexOf("VS Code window on BrowserAI", StringComparison.Ordinal)).IsLessThan(first);
        await Assert.That(first).IsLessThan(unnamedTab);
        await Assert.That(unnamedTab).IsLessThan(terminal);
        await Assert.That(terminal).IsLessThan(secondWindow);
        await Assert.That(secondWindow).IsLessThan(stranger);
        await Assert.That(agents.Split("VS Code window on BrowserAI").Length - 1).IsEqualTo(2).Because("two windows on one folder are two groups");

        // A renamed conversation is a new state for an open tab.
        var renamed = holds with { Relays = [.. holds.Relays.Select(relay => relay.Conversation is "s1" ? relay with { Label = new ConversationName("Renamed", IsTitle: true) } : relay)] };

        await Assert.That(UpdatePageContent.Signature(renamed, Now)).IsNotEqualTo(UpdatePageContent.Signature(holds, Now));
    }

    /// <summary>
    /// A client is called the same thing on the update page and on the sessions page:
    /// its name and its version, its name alone, or <i>unnamed client</i>, which no
    /// version follows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Item 85 of the texts review of 2026-10-10</b>: the update page said <i>unnamed
    /// client</i> and the sessions page <i>A client that has not said what it is</i> with
    /// the version after it, for one relay. A version with no name tells a person
    /// nothing.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against the sessions page's old wording.
    /// </para>
    /// </remarks>
    /// <param name="name">What the client called itself, or <see langword="null"/>.</param>
    /// <param name="version">Its version, or <see langword="null"/>.</param>
    /// <param name="expected">What both pages call it.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments("claude-code", "2.1.296", "claude-code 2.1.296")]
    [Arguments("claude-code", null, "claude-code")]
    [Arguments(null, "2.1.296", ClientNames.Unnamed)]
    [Arguments(null, null, ClientNames.Unnamed)]
    public async Task AClientIsCalledTheSameOnTheUpdatePageAndTheSessionsPage(string? name, string? version, string expected)
    {
        var relay = new RelayState("1", name, version, @"C:\project", Now, CallInFlight: false);
        var entry = new ServerEntry(
            "301-1",
            "relay:1",
            new Coordination.ServerDescription(
                301,
                1,
                new Coordination.ClientIdentity(name, Title: null, version),
                @"C:\project",
                LastToolCall: null,
                CallsInFlight: 0,
                [],
                Coordination.ServerDescription.Roles.Relay),
            ClientKind.Other,
            RecentlyActive: false,
            []);

        await Assert.That(BackgroundUpdates.ClientOf(relay)).IsEqualTo(expected);
        await Assert.That(PageContent.ClientOf(entry)).IsEqualTo(expected);
    }

    /// <summary>
    /// A wait no countdown leads says what it waits for, and a session or a window set
    /// never to close says so where its countdown would be.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AWaitNoCountdownLeadsSaysWhatItWaitsFor()
    {
        var empty = new UpdateHoldSnapshot(Now, UpdateHoldState.Held, "1.2.0", [], [], []);

        var window = Render(empty with { VisibleWindows = [new HoldingSession(@"C:\w", null, null)] });

        await Assert.That(window).Contains(PageContent.Text("A visible window set never to close holds it: it installs once you close that window and nothing else uses BrowserAI."));
        await Assert.That(Section(window, "windows")).Contains("Set never to close by itself.");
        await Assert.That(Section(window, "windows")).Contains("<strong>No purpose recorded</strong>");

        var session = Render(empty with { HiddenSessions = [new HoldingSession(@"C:\h", null, null)] });

        await Assert.That(session).Contains(PageContent.Text("A hidden session set never to close holds it: it installs once an agent closes that session and nothing else uses BrowserAI."));
        await Assert.That(Section(session, "hidden")).Contains(PageContent.Text("Set never to close: an agent has to close it."));

        var call = Render(empty with { Relays = [new HoldingRelay("Codex", null, Now.AddMinutes(-1), CallInFlight: true, RelayReconnect.NewConversation)] });

        await Assert.That(call).Contains(PageContent.Text("Every countdown has run out, and a call is still running: it installs once that call has finished."));

        var closing = Render(empty with { HiddenSessions = [new HoldingSession(@"C:\h", null, Now.AddSeconds(-1))] });

        await Assert.That(closing).Contains(PageContent.Text("Every countdown has run out: it installs once the last browser has closed."));
        await Assert.That(Section(closing, "hidden")).Contains("Closing now.");

        var nothing = Render(empty);

        await Assert.That(nothing).Contains(PageContent.Text("Nothing uses BrowserAI now: it installs in a moment."));
        await Assert.That(Section(nothing, "hidden")).Contains("No hidden browser session is open.");
        await Assert.That(Section(nothing, "windows")).Contains("No visible window is open.");
        await Assert.That(Section(nothing, "agents")).Contains("No agent is connected to BrowserAI.");
    }

    /// <summary>
    /// With nothing waiting the page says which version is installed, or that this
    /// BrowserAI is not installed, as the status page does; while it installs it says
    /// so and offers no button; and where reading what holds the update failed it
    /// says that, and where the log is.
    /// </summary>
    /// <remarks>
    /// <i>Corrected 2026-10-10 (previously "and where nothing reports what holds an
    /// update it says that, and claims nothing else")</i>: the page always has the
    /// update core since the one background (#73), so a snapshot it does not have is a
    /// read that failed, and a background that is not installed holds nothing (#71).
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePageSaysWhenNothingWaitsWhenItIsInstallingAndWhenTheReadFailed()
    {
        var none = Render(UpdateHoldSnapshot.Nothing(Now));

        // ⚠️ Corrected 2026-10-09 (previously "... BrowserAI 9.0.0 is installed. The
        // status page checks for a newer one."): since the one resident background,
        // the status page checks for nothing itself, so the sentence ends at what is
        // installed.
        await Assert.That(none).Contains("<p>No downloaded update is waiting. BrowserAI 9.0.0 is installed.</p>");
        await Assert.That(none).DoesNotContain("data-action=\"install-now\"");

        var uninstalled = Render(UpdateHoldSnapshot.Nothing(Now), installed: false);

        await Assert.That(uninstalled).Contains("<p>This BrowserAI is not installed, so there is nothing to update.</p>");
        await Assert.That(uninstalled).DoesNotContain("is installed.</p>");

        var installing = Render(new UpdateHoldSnapshot(Now, UpdateHoldState.Installing, "1.2.0", [], [], []));

        await Assert.That(installing).Contains("BrowserAI 1.2.0 is installing now. This page stops when BrowserAI closes, and BrowserAI starts again by itself.");
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
    /// it does, on the update page and on the status page alike; a newer one is not.
    /// </summary>
    /// <remarks>
    /// <b>Q308 a, the maintainer's words of 2026-10-03 verbatim: <i>"Q308 a"</i></b>:
    /// automatic rollback stays, and the interface says when the offered version is
    /// older. Built again 2026-10-10 from what the update core reads, after the page's
    /// own check, which said it, was deleted under his "9 a".
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnOlderVersionIsSaidToBeOlderOnTheUpdatePageAndTheStatusPage()
    {
        const string Said = "<p class=\"warning\">It is older than the installed 9.0.0. Installing it goes back to the earlier version.</p>";

        var older = Held() with { Version = "8.5.0", Older = true };

        await Assert.That(Render(older)).Contains("BrowserAI 8.5.0 is downloaded and ready to install.");
        await Assert.That(Render(older)).Contains(Said);
        await Assert.That(Status(older)).Contains(Said);

        await Assert.That(Render(Held())).DoesNotContain("older than");
        await Assert.That(Status(Held())).DoesNotContain("older than");

        // ⚠️ And what installing now does names the version that installs, round 2 of
        // the texts review, 2026-10-10, #118 (previously "once the new version is
        // installed", above an older one). Planted red against "the new version".
        await Assert.That(Section(Render(older), "install-now")).Contains("BrowserAI starts again by itself once BrowserAI 8.5.0 is installed.");
        await Assert.That(Section(Render(older), "install-now")).DoesNotContain("new version");

        // An open tab hears of it: the watch's signature tells the two apart.
        await Assert.That(UpdatePageContent.Signature(older, Now)).IsNotEqualTo(UpdatePageContent.Signature(older with { Older = false }, Now));
    }

    /// <summary>
    /// The update page is served at its own route, the navigation marks it, and its
    /// button asks the background to install now: a refusal is a sentence on the page,
    /// and an install that started is said to every tab once, by the background's own
    /// stop, and never by the page as well.
    /// </summary>
    /// <remarks>
    /// <i>Corrected 2026-10-10 (previously "and an install that started tells every
    /// tab and ends their streams")</i>: #105 of the texts review. The page's own
    /// sentence and the background's were released by the same stop and raced, so a tab
    /// showed whichever came first, and only the background's says how to get the page
    /// back.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheUpdatePageIsServedAndItsButtonAsksTheBackgroundToInstallNow()
    {
        var holds = new ScriptedHolds(Held()) { Answer = "An agent's call is still running, so nothing was installed." };

        using var rig = new PageRig(holds: holds);

        var address = rig.HandOut(PageKind.Update);

        await Assert.That(address).Contains("update?tab=1");

        var page = await PageRig.GetAsync(address);

        await Assert.That(page.Status).IsEqualTo(200).Because(page.Raw);
        await Assert.That(page.Body).Contains("<title>BrowserAI update</title>");
        await Assert.That(page.Body).Contains("data-page=\"update\"");
        await Assert.That(page.Body).Contains("<a href=\"update?tab=1\" aria-current=\"page\">Update</a>");
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
        await Assert.That(await StateContainingAsync(stream, "Closing every session and connection, then installing BrowserAI 1.2.0.")).IsNotNull();

        var handedOver = await stream.NextAsync();

        await Assert.That(handedOver?.Name).IsEqualTo(PageEvents.State).Because("the page tells no tab the install has started: the background's stop does");
        await Assert.That(string.Join(",", holds.Asked)).IsEqualTo("1.2.0,1.2.0");

        // The background's stop, as Program.Background says it, is the one sentence.
        const string Stopped = "BrowserAI is installing an update, so this tab has stopped. Open BrowserAI from the Start Menu again once the installed notification has appeared.";

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

        using var rig = new PageRig(holds: holds);

        _ = rig.HandOut(PageKind.Update);

        using var stream = await rig.StreamAsync(rig.Gate.Root, 1, "update");

        // The stream's own first state.
        await Assert.That(await StateContainingAsync(stream, "Claude Code 2.1.290")).IsNotNull();

        holds.Snapshot = Held() with
        {
            Relays = [.. Held().Relays, new HoldingRelay("Codex 0.162.0", @"C:\Source\four", Now.AddMinutes(9), CallInFlight: false, RelayReconnect.NewConversation)],
        };

        rig.Clock.Advance(PageService.HoldsWatchPeriod);

        var changed = await StateContainingAsync(stream, "Codex 0.162.0");

        await Assert.That(changed).IsNotNull();
        await Assert.That(changed!).Contains("C:\\Source\\four");
    }

    /// <summary>
    /// Where the page checks for nothing itself and the background reports what holds
    /// an update, the status page's update section says what is waiting and leads to
    /// the update page, and never that no release feed is set, because the background
    /// has one; where the read failed, it says that, and where the log is.
    /// </summary>
    /// <remarks>
    /// The background builds its page with no feed of its own since 2026-10-08, and the
    /// page's own check, offer and install are deleted (2026-10-10, the maintainer's
    /// "9 a"). The sentence it showed, "No release feed is set for this build", was
    /// written for a build with no feed at all.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheStatusPageSaysWhatTheBackgroundHoldsAndNeverThatNoFeedIsSet()
    {
        const string NoFeed = "No release feed is set for this build";

        var held = Status(Held());

        await Assert.That(held).Contains("<p>BrowserAI 1.2.0 is downloaded and ready to install. It installs by itself once BrowserAI has been idle.</p>");
        await Assert.That(held).Contains("<p><a href=\"update?tab=3\">See what holds it, or install it now</a></p>");
        await Assert.That(held).DoesNotContain(NoFeed);
        await Assert.That(held).DoesNotContain("data-action=\"check-updates\"");

        var none = Status(UpdateHoldSnapshot.Nothing(Now));

        await Assert.That(none).Contains("<p>No downloaded update is waiting.</p>");
        await Assert.That(none).DoesNotContain(NoFeed);

        var installing = Status(new UpdateHoldSnapshot(Now, UpdateHoldState.Installing, "1.2.0", [], [], []));

        await Assert.That(installing).Contains("<p>BrowserAI 1.2.0 is installing now.</p>");
        await Assert.That(installing).DoesNotContain(NoFeed);

        // #66, 2026-10-10 (previously "where nothing reports, the stage's own sentence
        // stands"): a read that failed is said as one.
        var unread = Status(null);

        await Assert.That(unread).Contains("<p>BrowserAI could not read what holds an update. Its log, in <code>C:\\data\\logs</code>, says why.</p>");
        await Assert.That(unread).DoesNotContain(NoFeed);
    }

    /// <summary>
    /// A status tab is sent a new state when an update becomes held, by the same
    /// once-a-second watch that keeps the update page current.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AStatusTabIsSentANewStateWhenAnUpdateBecomesHeld()
    {
        var holds = new ScriptedHolds(UpdateHoldSnapshot.Nothing(Now));

        using var rig = new PageRig(holds: holds);

        _ = rig.HandOut(PageKind.Status);

        using var stream = await rig.StreamAsync(rig.Gate.Root, 1);

        await Assert.That(await StateContainingAsync(stream, "No downloaded update is waiting.")).IsNotNull();

        holds.Snapshot = Held();
        rig.Clock.Advance(PageService.HoldsWatchPeriod);

        await Assert.That(await StateContainingAsync(stream, "BrowserAI 1.2.0 is downloaded and ready to install.")).IsNotNull();
    }

    /// <summary>The status page's main part, rendered for tab 3 of a page that checks for nothing itself.</summary>
    private static string Status(UpdateHoldSnapshot? holds) =>
        PageContent.Fragment(
            new PageView(
                new PageFacts
                {
                    Version = "9.0.0",
                    InstallRoot = @"C:\install",
                    DataRoot = @"C:\data",
                    LogDirectory = @"C:\data\logs",
                    ServerCommand = null,
                    ServerRefusal = null,
                },
                SessionsSnapshot.Empty,
                null,
                Holds: holds),
            PageKind.Status,
            3,
            Occasion.Ordinary,
            Now);

    private static string Render(UpdateHoldSnapshot? holds, bool installed = true) =>
        UpdatePageContent.Render(
            new PageView(
                new PageFacts
                {
                    Version = "9.0.0",
                    InstallRoot = installed ? @"C:\install" : null,
                    DataRoot = @"C:\data",
                    LogDirectory = @"C:\data\logs",
                    ServerCommand = null,
                    ServerRefusal = null,
                },
                SessionsSnapshot.Empty,
                null,
                Holds: holds),
            Now);

    /// <summary>A countdown as the page writes it.</summary>
    private static string Countdown(DateTimeOffset ends, string text) =>
        string.Create(CultureInfo.InvariantCulture, $"<span class=\"countdown\" data-ends-at=\"{ends.ToUnixTimeMilliseconds()}\">{text}</span>");

    /// <summary>One section of the page, by its id.</summary>
    private static string Section(string html, string id)
    {
        var start = html.IndexOf($"<section id=\"{id}\">", StringComparison.Ordinal);

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
            new HoldingRelay("Claude Code 2.1.290", @"C:\Source\one", Now.AddMinutes(8), CallInFlight: false, RelayReconnect.McpReconnect),
            new HoldingRelay("Codex 0.161.0", @"C:\Source\two", Now.AddMinutes(-1), CallInFlight: false, RelayReconnect.NewConversation),
            new HoldingRelay("Claude Code 2.1.290 (VS Code)", @"C:\Source\three", Now.AddMinutes(-2), CallInFlight: true, RelayReconnect.None),
        ]);

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

        public UpdateHoldSnapshot Read() => Snapshot;

        public Task<string?> InstallNowAsync(string version, CancellationToken cancellationToken)
        {
            Asked.Enqueue(version);
            return Task.FromResult(Answer);
        }
    }
}
