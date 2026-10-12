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
        await using var rig = await PageRig.CreateAsync();

        var page = await PageRig.GetAsync(await rig.HandOutAsync());

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
        // Corrected 2026-10-12 (previously "BrowserAI is not answering this page. If it does
        // not come back, open BrowserAI from the Start Menu again."), the maintainer's 71 a,
        // and again that day by the reader's pass (previously "and it opens this page again in
        // a new tab"), since the start opens the home.
        await Assert.That(page.Body).Contains("data-unanswered=\"" + PageContent.Text("BrowserAI is not answering this page. Start BrowserAI from the Start Menu, and it opens its page again in a new tab.") + "\"");
        await Assert.That(script.Body).Contains("say(unanswered)");
        await Assert.That(script.Body).DoesNotContain("Start Menu");

        await using (var notInstalled = await PageRig.CreateAsync(installed: false))
        {
            var bare = await PageRig.GetAsync(await notInstalled.HandOutAsync());

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
        await using var rig = await PageRig.CreateAsync();

        _ = await rig.HandOutAsync();

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
        await using var rig = await PageRig.CreateAsync();

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

        var page = await PageRig.GetAsync(await rig.HandOutAsync(PageKind.Sessions));
        var body = page.Body;
        var list = body[body.IndexOf("<div class=\"list-view\">", StringComparison.Ordinal)..];

        await Assert.That(page.Status).IsEqualTo(200).Because(page.Raw);
        await Assert.That(body).Contains(PageContent.Text("Every chat in Claude Code or Codex that uses BrowserAI, and the browser sessions it opened. Nothing can be closed from this page: an agent closes its own browser, and a conversation ends when its app closes it."));

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
        await Assert.That(list).Contains(PageContent.Text("BrowserAI keeps the browser as it was, so a conversation can pick the session up again. If none does, the browser closes by itself after its idle time."));
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
        await using var rig = await PageRig.CreateAsync();

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

        var page = await PageRig.GetAsync(await rig.HandOutAsync(PageKind.Sessions));
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
        await using var rig = await PageRig.CreateAsync();

        var server = Server(301, "claude-code", null);

        rig.Sessions.Snapshot = new SessionsSnapshot(Now, [server], []);

        _ = await PageRig.GetAsync(await rig.HandOutAsync(PageKind.Sessions));

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
        await using var rig = await PageRig.CreateAsync();

        _ = await rig.HandOutAsync();

        using var first = await rig.StreamAsync(rig.Gate.Root, 1);

        await Assert.That(await first.NextNamedAsync(PageEvents.State)).IsNotNull();

        // A second Start Menu click: a new address, the same listener.
        var second = await rig.HandOutAsync();

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

    // RETIRED 2026-10-12: TheListenerStopsAMinuteAfterTheLastTabLeavesAndTheNextStartOpensANewOne,
    // which held Q336 a's minute after the last tab, asked through TryStopAsync on every wake.
    // The maintainer's 69 d of 2026-10-11, in his words verbatim "69 d with fallback to b",
    // keeps the listener up while the background runs, so a browser's Always allow for its
    // address lasts; the arm below holds what replaced it.

    /// <summary>
    /// The tab's listener stays up while the background runs, however long after its last tab
    /// the background's loop asks; a background that starts again over the same data root takes
    /// the port the last one held and goes on numbering tabs; and with that port taken by another
    /// program it takes one Windows picks and remembers it.
    /// </summary>
    /// <remarks>
    /// <b>69 d with fallback b, the maintainer's words of 2026-10-11 verbatim: <i>"69 d with
    /// fallback to b"</i>.</b> Firefox remembers <i>Always allow</i> for opening BrowserAI per
    /// address, port included, so the address a folder link is clicked from has to outlive the
    /// listener's last tab and the background's restart. The memory is the product's file under
    /// the rig's scratch data root.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheListenerStaysUpWhileTheBackgroundRunsAndARestartTakesItsLastPortAgain()
    {
        using var scratch = await ScratchDirectory.CreateAsync("page-memory");
        var memory = PageListenerMemoryFile.In(scratch.Path);
        int port;
        string firstToken;

        await using (var rig = await PageRig.CreateAsync(memory: memory))
        {
            var first = await rig.HandOutAsync();

            port = rig.Gate.Port;
            firstToken = rig.Gate.Token;

            // A tab connects and leaves, and ten minutes pass with the background's loop asking
            // on every one of them.
            using (var tab = await rig.StreamAsync(rig.Gate.Root, 1))
            {
                await Assert.That(await tab.NextNamedAsync(PageEvents.State)).IsNotNull();
            }

            await Assert.That(await WaitForAsync(() => rig.Page.Tabs?.Connected is 0)).IsTrue();

            for (var minute = 0; minute < 10; minute++)
            {
                rig.Clock.Advance(TimeSpan.FromMinutes(1));
                await Assert.That(await rig.Page.TryStopAsync(final: false)).IsFalse().Because($"minute {minute + 1} after the last tab left");
            }

            await Assert.That(rig.Page.IsServing).IsTrue();
            await Assert.That((await PageRig.GetAsync(first)).Status).IsEqualTo(200);

            // The next person's start: the same listener, the next tab.
            await Assert.That(await rig.HandOutAsync()).IsEqualTo(rig.Gate.Root + "?tab=2");

            // The background's own end stops it.
            await Assert.That(await rig.Page.TryStopAsync(final: true)).IsTrue();
            await Assert.That(rig.Page.IsServing).IsFalse();
        }

        // The memory under the data root names the port and the last tab.
        var remembered = await memory.ReadAsync();

        await Assert.That(remembered.Port).IsEqualTo(port);
        await Assert.That(remembered.Tab).IsEqualTo(2);

        // The background starts again over the same data root: the same port, a new token,
        // and the tabs numbered on from the last.
        await using (var again = await PageRig.CreateAsync(memory: memory))
        {
            var address = await again.HandOutAsync();

            await Assert.That(again.Gate.Port).IsEqualTo(port);
            await Assert.That(again.Gate.Token).IsNotEqualTo(firstToken);
            await Assert.That(address).IsEqualTo(again.Gate.Root + "?tab=3");
        }

        // That port taken by another program: a port Windows picks, remembered for the next.
        using var holder = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp)
        {
            ExclusiveAddressUse = true,
        };

        holder.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port));
        holder.Listen();

        using var logs = new CapturingLoggerProvider();

        await using var moved = await PageRig.CreateAsync(logs: logs, memory: memory);

        _ = await moved.HandOutAsync();

        await Assert.That(moved.Gate.Port).IsNotEqualTo(port);
        await Assert.That((await memory.ReadAsync()).Port).IsEqualTo(moved.Gate.Port);
        await Assert.That(logs.Logged($"last port, {port}, is taken")).IsTrue();
    }

    /// <summary>
    /// The first visit, by the address with the token, is answered with the token in this
    /// port's cookie and the same address without it; the plain address, its stylesheet, its
    /// script, its event stream and its actions are admitted by the cookie alone, and refused
    /// without it.
    /// </summary>
    /// <remarks>
    /// <b>74 a, the maintainer's words of 2026-10-11 verbatim: <i>"70 a - can we hide the secret
    /// from the visible uri? It looks ugly."</i> and <i>"74 a"</i>.</b> What a real browser makes
    /// of the answer, following it and sending the cookie, is
    /// <see cref="PageBrowserTests"/>'.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFirstVisitTradesTheTokenInTheAddressForACookieAndAPlainAddress()
    {
        await using var rig = await PageRig.CreateAsync();

        var address = await rig.HandOutAsync(PageKind.Sessions);
        var uri = new Uri(address);
        var port = uri.Port;
        var token = rig.Gate.Token;
        var host = $"Host: 127.0.0.1:{port}";

        await Assert.That(uri.PathAndQuery).IsEqualTo($"/{token}/sessions?tab=1");

        var swap = await RawHttp.SendAsync(port, RawHttp.Get(port, uri.PathAndQuery, host, "Sec-Fetch-Site: none"));

        await Assert.That(swap.Status).IsEqualTo(303).Because(swap.Raw);
        await Assert.That(swap.Header("Location")).IsEqualTo("/sessions?tab=1");
        await Assert.That(swap.Header("Set-Cookie")).IsEqualTo($"browserai-{port}={token}; Path=/; HttpOnly; SameSite=Strict");
        await Assert.That(swap.Body).IsEmpty();

        var cookie = $"Cookie: browserai-{port}={token}";
        var page = await RawHttp.SendAsync(port, RawHttp.Get(port, "/sessions?tab=1", host, "Sec-Fetch-Site: none", cookie));

        await Assert.That(page.Status).IsEqualTo(200).Because(page.Raw);
        await Assert.That(page.Body).Contains("<h1 class=\"page-title\">Conversations and browsers</h1>");
        await Assert.That(page.Values("Set-Cookie")).IsEmpty();

        // Every relative address the page names resolves under the plain one.
        await Assert.That(page.Body).Contains("href=\"page.css\"");
        await Assert.That(page.Body).Contains("src=\"page.js\"");

        foreach (var route in new[] { "/page.css", "/page.js" })
        {
            var asset = await RawHttp.SendAsync(port, RawHttp.Get(port, route, host, "Sec-Fetch-Site: same-origin", cookie));

            await Assert.That(asset.Status).IsEqualTo(200).Because(route);
        }

        using (var stream = await RawEventStream.OpenThroughTheCookieAsync(port, token, 1, "sessions"))
        {
            await Assert.That(stream.Status).IsEqualTo(200);
            await Assert.That(await stream.NextNamedAsync(PageEvents.State)).IsNotNull();
        }

        var acted = await RawHttp.SendAsync(port, RawHttp.Post("/action", """{"action":"measure-sessions"}""", host, $"Origin: http://127.0.0.1:{port}", "Sec-Fetch-Site: same-origin", "Content-Type: application/json", cookie));

        await Assert.That(acted.Status).IsEqualTo(204).Because(acted.Raw);

        // The plain address with no cookie, or another port's, is the gate's bare 404.
        foreach (var (shape, request) in new[]
        {
            ("no cookie", RawHttp.Get(port, "/sessions?tab=1", host, "Sec-Fetch-Site: none")),
            ("another port's cookie", RawHttp.Get(port, "/sessions?tab=1", host, "Sec-Fetch-Site: none", $"Cookie: browserai-{port + 1}={token}")),
            ("an older listener's token", RawHttp.Get(port, "/sessions?tab=1", host, "Sec-Fetch-Site: none", $"Cookie: browserai-{port}={PageGate.NewToken()}")),
        })
        {
            var refused = await RawHttp.SendAsync(port, request);

            await Assert.That(refused.Status).IsEqualTo(404).Because(shape);
            await Assert.That(refused.Body).IsEmpty().Because(shape);
        }

        // An event stream and an action by the token's address are as they were: the page's
        // script asks for them by the plain address, and an older tab by the token's.
        using (var byTheToken = await rig.StreamAsync(rig.Gate.Root, 1, "sessions"))
        {
            await Assert.That(await byTheToken.NextNamedAsync(PageEvents.State)).IsNotNull();
        }

        await Assert.That((await rig.ActAsync("""{"action":"measure-sessions"}""")).Status).IsEqualTo(204);
    }

    /// <summary>
    /// A linger tick that arrives after the tabs were disposed wakes nothing, where it set
    /// the background's inbox after the background had closed it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-10-11, by the sweep of every timer callback in <c>src</c></b> that
    /// followed <c>BrowserProvisioner.PollTicker</c>'s crash of the release gate's second
    /// PowerShell run at <c>d1bae44f</c>. The linger's tick calls the wake it was given,
    /// which in the product is <see cref="CoordinatorInbox.Wake"/>, and the background
    /// disposes its inbox right after its page. <see cref="PageTabs.Dispose"/> disposed
    /// the timer and set nothing a tick could read, and a system timer's
    /// <c>Dispose</c> does not wait for a tick already handed to the thread pool, so a
    /// tick that late set a closed event on a pool thread, which ends the process. Read in
    /// the code and not seen in a run.
    /// </para>
    /// <para>
    /// <b>In the product's order</b>: the tabs go first and the inbox after them, as
    /// <c>Program.Background</c> disposes them, and <see cref="ManualClock.FireAfterDispose"/>
    /// delivers the late tick when the arm asks.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ALingerTickThatArrivesAfterTheTabsAreDisposedWakesNothing()
    {
        var clock = new ManualClock();
        var inbox = new CoordinatorInbox();
        var tabs = new PageTabs(clock, PageTabs.ProductLinger, inbox.Wake);

        // A hand-out arms the linger.
        await Assert.That(tabs.HandOut()).IsEqualTo(1);

        tabs.Dispose();
        inbox.Dispose();

        // Today this throws the inbox's ObjectDisposedException out of the tick itself.
        var late = clock.FireAfterDispose();

        await Assert.That(late).IsEqualTo(1)
            .Because("the tabs arm one linger timer, and its tick has to have run for this arm to be about anything");
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
        using var scratch = await ScratchDirectory.CreateAsync("page-opener");
        using var logs = new CapturingLoggerProvider();

        var logger = logs.CreateLogger("opener");
        var token = PageGate.NewToken();
        var address = $"http://127.0.0.1:49152/{token}/?tab=1";
        var opened = new List<string>();

        Task<bool> open(string url)
        {
            opened.Add(url);
            return Task.FromResult(true);
        }

        await Assert.That(await PageOpener.DeliverAsync(address, null, open, logger)).IsTrue();
        await Assert.That(opened).IsEquivalentTo([address]);

        var file = Path.Combine(scratch.Path, "address.txt");

        await Assert.That(await PageOpener.DeliverAsync(address, file, open, logger)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(file)).IsEqualTo(address);
        await Assert.That(opened.Count).IsEqualTo(1);

        await Assert.That(await PageOpener.DeliverAsync(null, null, open, logger)).IsFalse();
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
