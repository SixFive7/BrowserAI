// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.App.Page;
using BrowserAI.Coordination;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>
/// The page the browser tab shows, end to end through its own listener: what it
/// says, what its buttons do, which tab wins, and when the coordinator behind it
/// may stop.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q315 a, the maintainer's words verbatim: <i>"Q315 a"</i></b>: a tab in the
/// person's own browser replaces the configuration window. Every arm here drives
/// the product's <see cref="PageService"/> over raw HTTP on <c>127.0.0.1</c>, the way
/// the page's own script does, with the update machinery, the running servers and
/// the desktop replaced by stand-ins, so nothing reaches the network, a server or
/// the screen.
/// </para>
/// <para>
/// <b>The clock is a <see cref="ManualClock"/></b> wherever a minute matters, so
/// Q336 a's linger is moved by the arm and never waited out.
/// </para>
/// </remarks>
internal sealed class PageServiceTests
{
    /// <summary>The instant every rig's clock starts at, so a time the arm writes and a time the page reads are on one clock.</summary>
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    /// <summary>
    /// The home says which BrowserAI this is and the install page where everything is,
    /// with the command a client starts folded under its details; a link out opens a new
    /// tab with no referrer, and no page carries an inline script or style for the
    /// content security policy to have to stop.
    /// </summary>
    /// <remarks>
    /// <i>Corrected 2026-10-11 (previously
    /// TheStatusPageSaysWhatIsInstalledAndWhereThingsAreAndCarriesNothingInline, which
    /// held the version as the status page's heading and the folders and the command on
    /// it)</i>: 40 a moved where things are behind the home's <i>This install</i> card, and
    /// 45 a folded the command line under the details for troubleshooting.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheHomeSaysWhatIsInstalledTheInstallPageWhereThingsAreAndNeitherCarriesAnythingInline()
    {
        using var rig = new PageRig();

        var page = await PageRig.GetAsync(rig.HandOut());

        await Assert.That(page.Status).IsEqualTo(200).Because(page.Raw);
        await Assert.That(page.Header("Content-Type")).IsEqualTo("text/html; charset=utf-8");
        await Assert.That(page.Body).Contains("<span class=\"version\">9.0.0</span>");
        await Assert.That(page.Body).Contains("<dt>Version</dt><dd>9.0.0, ");

        var install = await PageRig.GetAsync(rig.Root + "install?tab=1");

        await Assert.That(install.Body).Contains(PageContent.Text(rig.Facts.InstallRoot));
        await Assert.That(install.Body).Contains(PageContent.Text(rig.Facts.DataRoot));
        await Assert.That(install.Body).Contains(PageContent.Text(rig.Facts.LogDirectory));

        var trouble = install.Body[install.Body.IndexOf("<details class=\"trouble\"", StringComparison.Ordinal)..];

        await Assert.That(trouble).Contains(PageContent.Text(rig.Facts.ServerCommand));
        await Assert.That(install.Body.IndexOf(PageContent.Text(rig.Facts.ServerCommand), StringComparison.Ordinal))
            .IsGreaterThan(install.Body.IndexOf("<details class=\"trouble\"", StringComparison.Ordinal)).Because("the command a client starts is folded (45 a)");

        await Assert.That(page.Body).Contains($"href=\"{PageContent.GuideUrl}\" target=\"_blank\" rel=\"noopener noreferrer\"");
        await Assert.That(page.Body).Contains("<script src=\"page.js\" defer></script>");
        await Assert.That(page.Body).Contains("data-tab=\"1\"");

        // Nothing for the policy to have to refuse: one script element and it has a
        // source, no style attribute, and no inline handler.
        await Assert.That(page.Body.Split("<script").Length).IsEqualTo(2);
        await Assert.That(page.Body).DoesNotContain(" style=");
        await Assert.That(page.Body).DoesNotContain(" onclick=");
        await Assert.That(page.Body).DoesNotContain(" onerror=");

        var script = await PageRig.GetAsync(rig.Root + "page.js");

        await Assert.That(script.Status).IsEqualTo(200);
        await Assert.That(script.Header("Content-Type")).IsEqualTo("text/javascript; charset=utf-8");
        await Assert.That(script.Body).Contains("new EventSource('events?tab='");

        // ⚠️ What the banner says once BrowserAI stops answering is the page's, since the
        // texts polish of 2026-10-10 (previously the script's own sentence, with the Start
        // Menu advice on every build): an installed build points at the Start Menu, and a
        // build that is not installed, whose Start Menu entry starts another, does not.
        await Assert.That(page.Body).Contains("data-unanswered=\"" + PageContent.Text("BrowserAI is not answering this page. If it does not come back, open BrowserAI from the Start Menu again.") + "\"");
        await Assert.That(script.Body).Contains("say(unanswered)");
        await Assert.That(script.Body).DoesNotContain("Start Menu");

        using (var notInstalled = new PageRig(installed: false))
        {
            var bare = await PageRig.GetAsync(notInstalled.HandOut());

            await Assert.That(bare.Body).Contains("data-unanswered=\"" + PageContent.Text("BrowserAI is not answering this page.") + "\"");
        }

        var style = await PageRig.GetAsync(rig.Root + "page.css");

        await Assert.That(style.Status).IsEqualTo(200);
        await Assert.That(style.Header("Content-Type")).IsEqualTo("text/css; charset=utf-8");
    }

    // RETIRED 2026-10-10, with the page's own update machinery, under the maintainer's
    // "9 a": ACheckSaysWhenTheOfferedVersionIsOlderAndAFailureIsOneSentenceWithTheRawTextUnderDetails
    // (Q308 a and Q309 b), ACheckTheFeedNeverAnswersEndsAtTheServersOwnTripwire,
    // AFolderFeedWithNoReleaseListIsNotUpToDateAndAHungCheckCanBeGivenUp and
    // AStagedUpdateIsInstalledFromItsLinkAfterEveryServerIsAskedToStop (Q310 a). Each
    // held a check, an offer or an install of the page's own, which nothing could run
    // since the background built the page with no feed of its own (2026-10-08); the
    // background checks, and the update page's Install now installs through its update
    // core. TheOldUpdateActionsAreActionsThePageDoesNotHave holds that they are gone.

    /// <summary>
    /// The page has no update check, no offer of a downloaded package and no install of
    /// its own, so a request for any of them is refused the way an action the page does
    /// not have is, and nothing runs.
    /// </summary>
    /// <remarks>
    /// <b>The maintainer's "9 a", relayed 2026-10-10</b>: code nothing uses goes before
    /// the build is installed. The background has built this page with no feed of its
    /// own since 2026-10-08, so its check, its staged offer and its install could not
    /// run; the background checks, and the update page's Install now installs, through
    /// the update core.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheOldUpdateActionsAreActionsThePageDoesNotHave()
    {
        using var rig = new PageRig();

        _ = rig.HandOut();

        foreach (var body in new[]
        {
            """{"action":"check-updates"}""",
            """{"action":"stop-check"}""",
            """{"action":"install-update","version":"9.2.0"}""",
        })
        {
            var answer = await rig.ActAsync(body);

            await Assert.That(answer.Status).IsEqualTo(404).Because(body);
            await Assert.That(answer.Body).IsEmpty();
        }
    }

    // RETIRED 2026-10-10: TheSessionsPageListsEveryServerWithItsWarningsAndClosesOnlyTheSelected,
    // which held a page of servers that each held their own sessions, with the Codex
    // and the recently-active warnings, the servers that did not answer, and a close of
    // the selected ones. The one background lists only itself and the clients connected
    // to it, so no such server reaches the page, and the maintainer's 17 a removed the
    // close those warnings were about.

    /// <summary>
    /// The conversations page lists each session once, under the conversation that uses
    /// it, and the sessions BrowserAI keeps apart, with what each is; it offers no box and
    /// no close, says no pid, and a close sent anyway is refused like an action the page
    /// does not have.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>17 a, the maintainer's answer of 2026-10-10, verbatim: <i>"17 a"</i></b>, to
    /// whether the page should offer a close the background always refused: the boxes,
    /// the button and the close path went, because a relay ends with its client.
    /// </para>
    /// <para>
    /// <i>Corrected 2026-10-11 (previously
    /// TheSessionsPageListsTheBackgroundAndItsClientsAndOffersNoClose, which held the
    /// background first with every session it holds and each client again with the
    /// sessions it drives, each with its pid)</i>: 40 a draws each session once, under its
    /// conversation, and 42 a says no pid and no client in the main view.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheConversationsPageListsEachSessionOnceUnderItsConversationAndOffersNoClose()
    {
        using var rig = new PageRig();

        var host = Description(
            900,
            ServerDescription.Roles.Host,
            [
                new HeldSession(@"C:\sessions\driven", "reads the docs", BrowserOpen: true, DrivenBy: "claude-code", DrivenThrough: 301, IdleCloseAt: Now.AddMinutes(9)),
                new HeldSession(@"C:\sessions\kept", "checks the shop", BrowserOpen: true, Kept: true, IdleCloseAt: Now.AddMinutes(7)),
                new HeldSession(@"C:\sessions\window", "fills the form", BrowserOpen: true, Headed: true, Kept: true),
            ]);

        rig.Sessions.Snapshot = CensusPageSessions.Compose(
            Now,
            [
                (@"C:\install\live\301-0.live", Description(301, ServerDescription.Roles.Relay, [])),
                (@"C:\install\live\900-0.live", host),
                (@"C:\install\live\302-0.live", Description(302, ServerDescription.Roles.Relay, []) with { Client = new ClientIdentity("codex-mcp-client", "Codex", "0.155.0") }),
                (@"C:\install\live\303-0.live", Description(303, ServerDescription.Roles.Relay, [])),
            ],
            []);

        var page = await PageRig.GetAsync(rig.HandOut(PageKind.Sessions));
        var body = page.Body;
        var list = body[body.IndexOf("<div class=\"list-view\">", StringComparison.Ordinal)..];

        await Assert.That(page.Status).IsEqualTo(200).Because(page.Raw);
        await Assert.That(body).Contains(PageContent.Text("Every chat in Claude Code or Codex that uses BrowserAI, and the browser sessions it opened. Nothing here can be closed from this page: an agent closes its own browser, and a conversation ends when its app closes it."));

        // Each session once in the list, the driven one inside its conversation's card.
        foreach (var name in new[] { "driven", "kept", "window" })
        {
            await Assert.That(list.Split($"<p class=\"sess-name\">{name}</p>").Length - 1).IsEqualTo(1).Because(name);
        }

        var first = list[list.IndexOf("<article class=\"card convo\" id=\"convo-c-301-1\">", StringComparison.Ordinal)..];

        await Assert.That(first[..first.IndexOf("</article>", StringComparison.Ordinal)]).Contains("<p class=\"sess-name\">driven</p>");

        // The two kept sessions apart, with what keeps them.
        await Assert.That(list).Contains("Kept after their conversation ended");
        await Assert.That(list).Contains("<h3>2 sessions with no conversation</h3>");
        await Assert.That(list).Contains(PageContent.Text("BrowserAI keeps the browser as it was, so a conversation can pick the session up again. Its idle close ends it otherwise."));
        await Assert.That(list).Contains("A window on your screen, set never to close by itself.");

        // Each conversation by its app, an app that gave no name as such, and a conversation
        // with no browser says so.
        await Assert.That(list).Contains("<h3>Codex in work</h3>");
        await Assert.That(list).Contains("<h3>Unnamed app in work</h3>");
        await Assert.That(list.Split("No browser yet. This conversation is connected and has not opened a session.").Length - 1).IsEqualTo(2);

        // No pid, no box, no close.
        await Assert.That(body).DoesNotContain("pid ");
        await Assert.That(body).DoesNotContain("Driven by");
        await Assert.That(body).DoesNotContain("type=\"checkbox\"");
        await Assert.That(body).DoesNotContain("close-servers");

        // A close sent anyway is an action the page does not have.
        var answer = await rig.ActAsync("""{"action":"close-servers","servers":["301-1"]}""");

        await Assert.That(answer.Status).IsEqualTo(404);
        await Assert.That(answer.Body).IsEmpty();
    }

    /// <summary>
    /// The conversations page names each conversation the way the person sees it, with its
    /// app after it, and lists a VS Code window's tabs together under the window, where the
    /// first of them stands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>1.2 a and 1.5 a, the maintainer's answer of 2026-10-10, verbatim: <i>"1.1-2.3 I
    /// accept all your recommendations"</i></b>: a title in quotes, BrowserAI's own words
    /// as they are, and a window labelled by its folder.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a page that drew the servers in their order
    /// with no window and no name. <i>Corrected 2026-10-11 (previously
    /// TheSessionsPageNamesEachConversationAndListsAWindowsTabsUnderIt, which held each name
    /// before its client's protocol name and its pid)</i>: 42 a names the app.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheConversationsPageNamesEachConversationAndListsAWindowsTabsUnderIt()
    {
        using var rig = new PageRig();

        var window = new ClientWindow("1200-134360600000000000", @"C:\Source\BrowserAI");
        var composed = CensusPageSessions.Compose(
            Now,
            [
                (@"C:\install\live\900-0.live", Description(900, ServerDescription.Roles.Host, [])),
                (@"C:\install\live\301-0.live", Description(301, ServerDescription.Roles.Relay, []) with { Client = ClaudeCode }),
                (@"C:\install\live\302-0.live", Description(302, ServerDescription.Roles.Relay, []) with { Client = ClaudeCode }),
                (@"C:\install\live\303-0.live", Description(303, ServerDescription.Roles.Relay, []) with { Client = ClaudeCode }),
                (@"C:\install\live\304-0.live", Description(304, ServerDescription.Roles.Relay, []) with { Client = ClaudeCode }),
            ],
            []);

        rig.Sessions.Snapshot = composed with
        {
            Servers =
            [
                .. composed.Servers.Select(server => server.Description.ProcessId switch
                {
                    301 => server with { Conversation = new ConversationName("Fix the <login> bug", IsTitle: true), Window = window },
                    302 => server with { Conversation = new ConversationName("Claude Code in one", IsTitle: false) },
                    303 => server with { Conversation = new ConversationName("unnamed conversation in BrowserAI", IsTitle: false), Window = window },
                    304 => server with { Conversation = ClientFolder.Unnamed("Claude Code", @"C:\work") },
                    _ => server,
                }),
            ],
        };

        var page = await PageRig.GetAsync(rig.HandOut(PageKind.Sessions));
        var list = page.Body[page.Body.IndexOf("<div class=\"list-view\">", StringComparison.Ordinal)..];

        await Assert.That(page.Status).IsEqualTo(200).Because(page.Raw);

        var heading = list.IndexOf("VS Code window on BrowserAI", StringComparison.Ordinal);
        var titled = list.IndexOf("<h3>" + PageContent.Text("\"Fix the <login> bug\"") + "</h3>", StringComparison.Ordinal);
        var unnamed = list.IndexOf("<h3>unnamed conversation in BrowserAI</h3>", StringComparison.Ordinal);
        var terminal = list.IndexOf("<h3>Claude Code in one</h3>", StringComparison.Ordinal);
        var own = list.IndexOf("<h3>Claude Code in work</h3>", StringComparison.Ordinal);

        await Assert.That(new[] { heading, titled, unnamed, terminal, own }.All(at => at >= 0)).IsTrue().Because(list);
        await Assert.That(heading).IsLessThan(titled);
        await Assert.That(titled).IsLessThan(unnamed);
        await Assert.That(unnamed).IsLessThan(terminal);
        await Assert.That(terminal).IsLessThan(own);

        // The app after each name, a window's tab marked as VS Code's.
        await Assert.That(list).Contains("Claude Code 2.1.296, in VS Code");
        await Assert.That(list.Split("Claude Code 2.1.296<span class=\"sep\"").Length - 1).IsEqualTo(2);
    }


    // RETIRED 2026-10-08: AnInstallStopsTheSessionHostAfterTheServersAndBeforeTheHandOver,
    // which held the page's install stopping the session host through the
    // coordinator's hold on it. The session host and the hold went with the
    // coordinator when the one resident background took both their places (S a).

    /// <summary>
    /// A button names a folder, a session or a server by a name the page was given,
    /// never by a path: the three folders open where the facts say, a session opens
    /// its own directory, and a path sent in their place is a bare 404.
    /// </summary>
    /// <remarks>
    /// <b>Control 7 of the first report, split by the second</b>: no endpoint takes a
    /// command, and here no endpoint takes a path either, so a request the page did
    /// not compose can at worst open one of the folders BrowserAI already shows.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnActionNamesWhatItOpensByANameThePageWasGivenAndNeverByAPath()
    {
        using var rig = new PageRig();

        var server = Server(301, "claude-code", null);

        rig.Sessions.Snapshot = new SessionsSnapshot(Now, [server], []);

        _ = await PageRig.GetAsync(rig.HandOut(PageKind.Sessions));

        await Assert.That((await rig.ActAsync("""{"action":"open-folder","folder":"install"}""")).Status).IsEqualTo(204);
        await Assert.That((await rig.ActAsync("""{"action":"open-folder","folder":"data"}""")).Status).IsEqualTo(204);
        await Assert.That((await rig.ActAsync("""{"action":"open-folder","folder":"logs"}""")).Status).IsEqualTo(204);
        await Assert.That((await rig.ActAsync($$"""{"action":"open-session","session":"{{server.Sessions[0].Id}}"}""")).Status).IsEqualTo(204);

        await Assert.That(rig.Host.Opened.ToArray()).IsEquivalentTo(
            [rig.Facts.InstallRoot!, rig.Facts.DataRoot, rig.Facts.LogDirectory, server.Sessions[0].Directory]);

        string[] refused =
        [
            """{"action":"open-folder","folder":"C:\\Windows"}""",
            """{"action":"open-session","session":"C:\\Windows"}""",
            """{"action":"open-session","session":"0000000000000000"}""",
            """{"action":"run","command":"calc.exe"}""",
            """{"folder":"install"}""",
            """not json""",
        ];

        foreach (var body in refused)
        {
            var answer = await rig.ActAsync(body);

            await Assert.That(answer.Status).IsEqualTo(404).Because(body);
            await Assert.That(answer.Body).IsEmpty();
        }

        await Assert.That(rig.Host.Opened.Count).IsEqualTo(4);
    }

    /// <summary>
    /// The newest tab wins: once a newer tab's stream arrives, the older one is told
    /// it was replaced and its stream ends, and the older tab reloading is told so at
    /// once; a reload of the newest keeps its place.
    /// </summary>
    /// <remarks>
    /// <b>Q337 a, the maintainer's words verbatim: <i>"Q337 a"</i></b>: a second Start
    /// Menu click opens a new tab and the older tab closes itself, because a page
    /// cannot bring an existing tab forward. The page's script answers the event with
    /// <c>window.close()</c>; this arm holds the coordinator's half.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheNewestTabWinsAndAnOlderTabIsToldItWasReplaced()
    {
        using var rig = new PageRig();

        _ = rig.HandOut();

        using var first = await rig.StreamAsync(rig.Gate.Root, 1);

        await Assert.That(await first.NextNamedAsync(PageEvents.State)).IsNotNull();

        // A second Start Menu click: a new address, the same listener.
        var second = rig.HandOut();

        await Assert.That(second).IsEqualTo(rig.Gate.Root + "?tab=2");

        using var newer = await rig.StreamAsync(rig.Gate.Root, 2);

        await Assert.That(await newer.NextNamedAsync(PageEvents.State)).IsNotNull();
        await Assert.That(await first.NextNamedAsync(PageEvents.Superseded)).IsNotNull();
        await Assert.That(await first.EndAsync()).IsTrue();

        // The old tab reloading is told at once.
        using var reloaded = await rig.StreamAsync(rig.Gate.Root, 1);

        await Assert.That((await reloaded.NextAsync())?.Name).IsEqualTo(PageEvents.Superseded);
        await Assert.That(await reloaded.EndAsync()).IsTrue();

        // The newest reloading keeps its place.
        newer.Dispose();

        using var again = await rig.StreamAsync(rig.Gate.Root, 2);

        await Assert.That((await again.NextAsync())?.Name).IsEqualTo(PageEvents.State);
        await Assert.That(await WaitForAsync(() => rig.Page.Tabs?.Connected is 1)).IsTrue();
    }

    /// <summary>
    /// The tab's listener stays for a minute after the last tab leaves, so a reload
    /// keeps working, and stops once the minute is up; the next person's start opens
    /// a new one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q336 a, the maintainer's words verbatim: <i>"Q336 a - also make sure that
    /// an exist only happens after 1 min. of a tab closed so a reload keeps working
    /// (because that does not take 1 min.)"</i></b>
    /// </para>
    /// <para>
    /// ⚠️ <i>Corrected 2026-10-08 (previously "The coordinator stops a minute after
    /// the last tab leaves", driven through the coordinator's own loop, whose process
    /// then exited)</i>: the one resident background holds the page now and never
    /// ends on its own (S a), so what stops after the minute is the listener, asked
    /// through <see cref="PageService.TryStop"/> on every wake the way the background's
    /// loop asks it, and the next hand-out starts a listener again.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-08</b>, on request, with every stop
    /// final, the coordinator's shape, whose process ended with its listener: red at
    /// the person's start after the minute, whose hand-out came back with no tab.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheListenerStopsAMinuteAfterTheLastTabLeavesAndTheNextStartOpensANewOne()
    {
        using var rig = new PageRig();

        _ = rig.HandOut();

        // A tab connected, and five minutes pass.
        var tab = await rig.StreamAsync(rig.Gate.Root, 1);

        rig.Clock.Advance(TimeSpan.FromMinutes(5));
        await Assert.That(rig.Page.TryStop(final: false)).IsFalse();

        // It leaves, and 59 seconds pass.
        tab.Dispose();

        await Assert.That(await WaitForAsync(() => rig.Page.Tabs?.Connected is 0)).IsTrue();

        rig.Clock.Advance(TimeSpan.FromSeconds(59));
        await Assert.That(rig.Page.TryStop(final: false)).IsFalse();

        // It reloads inside the minute and leaves again: the minute starts over.
        using (var reload = await rig.StreamAsync(rig.Gate.Root, 1))
        {
            await Assert.That(await reload.NextNamedAsync(PageEvents.State)).IsNotNull();
        }

        await Assert.That(await WaitForAsync(() => rig.Page.Tabs?.Connected is 0)).IsTrue();

        rig.Clock.Advance(TimeSpan.FromSeconds(59));
        await Assert.That(rig.Page.TryStop(final: false)).IsFalse();

        // The minute runs out, and the listener stops.
        rig.Clock.Advance(TimeSpan.FromSeconds(1));

        await Assert.That(rig.Page.TryStop(final: false)).IsTrue();
        await Assert.That(rig.Page.IsServing).IsFalse();

        // A person's start after that is handed a tab on a new listener.
        await Assert.That(rig.Page.HandOut(PageKind.Status)).IsNotNull();
        await Assert.That(rig.Page.IsServing).IsTrue();
    }

    // RETIRED 2026-10-08: AnOpenTabDoesNotHoldAnUpdateBackAndIsToldToReopenFromTheStartMenu,
    // which drove the coordinator's loop to apply a staged update with a tab open. The
    // loop went with the coordinator (S a); the background's update core decides when
    // an update installs, and the background tells every open tab why it stops.

    /// <summary>
    /// A person's start opens the address it was handed with the shell, or writes it
    /// to the file it was asked to and opens nothing, and its record names the port
    /// and never the token.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AStartOpensItsAddressOrWritesItAndNeverLogsTheToken()
    {
        using var scratch = ScratchDirectory.Create("page-opener");
        using var logs = new CapturingLoggerProvider();

        var logger = logs.CreateLogger("opener");
        var token = PageGate.NewToken();
        var address = $"http://127.0.0.1:49152/{token}/?tab=1";
        var opened = new List<string>();

        bool open(string url)
        {
            opened.Add(url);
            return true;
        }

        await Assert.That(PageOpener.Deliver(address, null, open, logger)).IsTrue();
        await Assert.That(opened).IsEquivalentTo([address]);

        var file = Path.Combine(scratch.Path, "address.txt");

        await Assert.That(PageOpener.Deliver(address, file, open, logger)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(file)).IsEqualTo(address);
        await Assert.That(opened.Count).IsEqualTo(1);

        await Assert.That(PageOpener.Deliver(null, null, open, logger)).IsFalse();
        await Assert.That(opened.Count).IsEqualTo(1);

        await Assert.That(PageOpener.WriteAddressFrom(["--write-address", file])).IsEqualTo(file);
        await Assert.That(PageOpener.WriteAddressFrom(["--write-address"])).IsNull();

        await Assert.That(logs.Records.Count).IsEqualTo(3);
        await Assert.That(logs.Records.Any(record => record.Message.Contains(token, StringComparison.Ordinal))).IsFalse();
        await Assert.That(logs.Logged("port 49152")).IsTrue();
    }

    /// <summary>Reads states off a stream until one carries the text.</summary>
    private static async Task<string?> StateContainingAsync(RawEventStream stream, string text)
    {
        while (await stream.NextNamedAsync(PageEvents.State) is { } state)
        {
            var html = state.Member("html");

            if (html.Contains(PageContent.Text(text), StringComparison.Ordinal) || html.Contains(text, StringComparison.Ordinal))
            {
                return html;
            }
        }

        return null;
    }

    /// <summary>Polls a condition until it holds or the hang detector runs out.</summary>
    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TestDefaults.InProcessHang;

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        return true;
    }

    /// <summary>A description from a server of the given role, holding the given sessions.</summary>
    /// <summary>A relay's client, as its greeting names Claude Code.</summary>
    private static ClientIdentity ClaudeCode { get; } = new("claude-code", Title: null, "2.1.296");

    private static ServerDescription Description(int pid, string role, IReadOnlyList<HeldSession> sessions) =>
        new(
            pid,
            1,
            null,
            @"C:\work",
            null,
            0,
            sessions,
            role);

    private static ServerEntry Server(int pid, string? client, DateTimeOffset? lastCall, string? purpose = "reads the docs", int inFlight = 0)
    {
        var directory = $@"C:\sessions\s{pid.ToString(CultureInfo.InvariantCulture)}";
        var description = new ServerDescription(
            pid,
            1,
            client is null ? null : new ClientIdentity(client, client is "claude-code" ? "Claude Code" : "Codex", client is "claude-code" ? "2.1.288" : "0.155.0"),
            @"C:\work",
            lastCall,
            inFlight,
            [new HeldSession(directory, purpose, BrowserOpen: true)]);

        return CensusPageSessions.Entry($@"C:\install\live\{pid}-0.live", description, Now);
    }
}
