// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Net;
using System.Text.RegularExpressions;
using BrowserAI.App;
using BrowserAI.App.Interop;
using BrowserAI.App.Page;
using BrowserAI.Registration;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// The page's folder links: a folder button and a picker's button drawn as a link carrying the
/// page's token, the background's answer to a link, and the lines the page says about them.
/// </summary>
/// <remarks>
/// <para>
/// <b>55 a, with 70 a, 71 a, 73 b and 75 a, the maintainer's words of 2026-10-11 verbatim:
/// <i>"55 a"</i>, <i>"70 a"</i>, <i>"71 a - maybe communicate on the page that you need to
/// restart from the start menu?"</i>, <i>"73 b"</i> and <i>"75 a"</i>.</b> Every arm drives the
/// product's <see cref="PageService"/> through the page rig, its listener in this process; the
/// link start that a click makes Windows start is <see cref="LinkStartTests"/>', and what a real
/// browser draws is <see cref="PageBrowserTests"/>'.
/// </para>
/// <para>
/// <b>Nothing here registers a scheme or opens a folder</b>: the facts name the test pack's
/// scheme, and the page's desktop is the rig's, which records and opens nothing.
/// </para>
/// </remarks>
internal sealed partial class PageLinkTests
{
    /// <summary>The scheme the rig's facts name: the test pack's, never the shipping one.</summary>
    private const string Scheme = "browserai-test";

    /// <summary>
    /// On a page drawn with links, every folder button and each button that opens Windows'
    /// folder picker is a link of the install's scheme carrying the page's token, with its id,
    /// look and label; and the page says once that the browser asks first. A build that
    /// registered no scheme draws them as buttons, and says nothing of a browser asking.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFolderButtonIsALinkCarryingThePagesTokenWhereTheInstallRegisteredAScheme()
    {
        await using var rig = await PageRig.CreateAsync(
            registration: new FakeRegistration { State = DashboardTests.Apps() },
            facts: facts => facts with { LinkScheme = Scheme });

        rig.Sessions.Snapshot = DashboardTests.Busy();

        var home = await PageRig.GetAsync(await rig.HandOutAsync());
        var token = rig.Gate.Token;

        await Assert.That(home.Status).IsEqualTo(200).Because(home.Raw);
        await Assert.That(home.Body).Contains(
            $"<a id=\"b-open-folder-data\" class=\"btn\" href=\"{Scheme}:open-folder?folder=data&amp;t={token}\" data-link=\"open-folder\">");
        await Assert.That(home.Body).Contains($"href=\"{Scheme}:open-folder?folder=install&amp;t={token}\"");
        await Assert.That(home.Body).Contains($"href=\"{Scheme}:open-folder?folder=logs&amp;t={token}\"");
        await Assert.That(home.Body).DoesNotContain("data-action=\"open-folder\"");

        // The line under the first one, which the page's script puts there.
        await Assert.That(home.Body).Contains($"data-link-hint=\"{PageContent.Text(PageContent.LinkHint)}\"");

        // A session's folder on the conversations page, by its opaque name.
        var conversations = await PageRig.GetAsync(rig.Root + "sessions?tab=1");
        var checkout = CensusPageSessions.IdOf(DashboardTests.CheckoutRounding);

        await Assert.That(conversations.Body).Contains($"href=\"{Scheme}:open-session?session={checkout}&amp;t={token}\"");
        await Assert.That(conversations.Body).DoesNotContain("data-action=\"open-session\"");

        // The setup page's two pickers, by the app's key and the action, once the setup is read.
        using var setupStream = await rig.StreamAsync(rig.Gate.Root, 1, "setup");

        _ = await PageRig.GetAsync(rig.Root + "setup?tab=1");

        var setup = await DashboardTests.StateContainingAsync(setupStream, ":pick?action=register-in-project");

        await Assert.That(setup).IsNotNull();

        foreach (var href in Hrefs().Matches(setup!).Select(match => WebUtility.HtmlDecode(match.Groups["href"].Value)).Where(href => href.Contains(":pick?", StringComparison.Ordinal)))
        {
            var ask = PageLinks.Parse(href, Scheme);

            await Assert.That(ask).IsNotNull().Because(href);
            await Assert.That(ask!.Verb).IsEqualTo(LinkVerb.Pick);
            await Assert.That(ask.Token).IsEqualTo(token);
            await Assert.That(ask.Action is "register-in-project" or "unregister-from-a-project").IsTrue().Because(href);
        }

        await Assert.That(setup).Contains($"{Scheme}:pick?action=register-in-project&amp;client=claude-code&amp;t={token}");

        // ⚠️ The setup page says the picker comes forward, and not that it may be behind.
        await Assert.That(setup).DoesNotContain("it can appear behind this browser window");

        // Every link the pages drew reads back as what it opens, with the page's token.
        foreach (var body in new[] { home.Body, conversations.Body, setup! })
        {
            foreach (var href in Hrefs().Matches(body).Select(match => WebUtility.HtmlDecode(match.Groups["href"].Value)).Where(href => href.StartsWith(Scheme + ":", StringComparison.Ordinal)))
            {
                await Assert.That(PageLinks.Parse(href, Scheme)?.Token).IsEqualTo(token).Because(href);
            }
        }

        // A build that registered no scheme draws buttons that post their action.
        await using var bare = await PageRig.CreateAsync();

        var plain = await PageRig.GetAsync(await bare.HandOutAsync());

        await Assert.That(plain.Body).Contains("<button type=\"button\" id=\"b-open-folder-data\" class=\"btn\" data-action=\"open-folder\" data-folder=\"data\">");
        await Assert.That(plain.Body).DoesNotContain("data-link-hint");
        await Assert.That(plain.Body).DoesNotContain("data-link=");
    }

    /// <summary>
    /// A link is read only in the shape the page draws: its scheme, one of the three verbs,
    /// exactly the members the verb needs and the token; anything else reads as no link.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ALinkIsReadOnlyInTheShapeThePageDrawsIt()
    {
        var token = PageGate.NewToken();

        (string Link, LinkVerb? Expected)[] table =
        [
            ($"{Scheme}:open-folder?folder=data&t={token}", LinkVerb.OpenFolder),
            ($"{Scheme}://open-folder/?folder=logs&t={token}", LinkVerb.OpenFolder),
            ($"BROWSERAI-TEST:open-session?session=0123456789abcdef&t={token}", LinkVerb.OpenSession),
            ($"{Scheme}:pick?action=register-in-project&client=codex&t={token}", LinkVerb.Pick),
            ($"{Scheme}:pick?action=unregister-from-a-project&client=claude-code&t={token}", LinkVerb.Pick),
            ($"browserai:open-folder?folder=data&t={token}", null),
            ($"{Scheme}:open-folder?folder=data", null),
            ($"{Scheme}:open-folder?folder=data&t=", null),
            ($"{Scheme}:open-folder?folder=data&t={token}&extra=1", null),
            ($"{Scheme}:open-folder?folder=data&folder=logs&t={token}", null),
            ($"{Scheme}:open-folder?folder=C:\\Windows&t={token}", null),
            ($"{Scheme}:open-folder?folder=..%5C..&t={token}", null),
            ($"{Scheme}:delete?folder=data&t={token}", null),
            ($"{Scheme}:pick?action=register&client=codex&t={token}", null),
            ($"{Scheme}:pick?action=register-in-project&t={token}", null),
            ($"{Scheme}:open-folder?folder={new string('x', 600)}&t={token}", null),
            (string.Empty, null),
            ("https://example.com/", null),
        ];

        foreach (var (link, expected) in table)
        {
            var ask = PageLinks.Parse(link, Scheme);

            await Assert.That(ask?.Verb).IsEqualTo(expected).Because(link.Length > 120 ? link[..120] : link);

            if (expected is not null)
            {
                await Assert.That(ask!.Token).IsEqualTo(token);
            }
        }
    }

    /// <summary>
    /// The background answers a link with one of BrowserAI's own folders, by the page's names
    /// for them or a session's opaque name, and only when the link carries the token of the
    /// listener up now; a path, an unknown name or another token opens nothing.
    /// </summary>
    /// <remarks>
    /// <b>70 a</b>: the link carries the page's own token, compared in constant time with the
    /// listener's, and names what it opens the way the buttons named it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ALinkWithThePagesTokenOpensOnlyBrowserAisOwnFolders()
    {
        await using var rig = await PageRig.CreateAsync(facts: facts => facts with { LinkScheme = Scheme });

        rig.Sessions.Snapshot = DashboardTests.Busy();

        // Before any listener there is no page a link could be from.
        await Assert.That(await rig.Page.LinkAsync(new LinkAsk(LinkVerb.OpenFolder, PageGate.NewToken(), Folder: "data"))).IsTypeOf<LinkAnswer.Refused>();

        _ = await PageRig.GetAsync(await rig.HandOutAsync(PageKind.Sessions));

        var token = rig.Gate.Token;

        foreach (var (folder, expected) in new[] { ("install", rig.Facts.InstallRoot!), ("data", rig.Facts.DataRoot), ("logs", rig.Facts.LogDirectory) })
        {
            var answer = await rig.Page.LinkAsync(new LinkAsk(LinkVerb.OpenFolder, token, Folder: folder));

            await Assert.That(answer).IsTypeOf<LinkAnswer.Open>().Because(folder);
            await Assert.That(((LinkAnswer.Open)answer).Directory).IsEqualTo(expected);
            await Assert.That(Directory.Exists(expected)).IsTrue().Because($"the {folder} folder is made before it is opened, as the button's was");
        }

        var checkout = CensusPageSessions.IdOf(DashboardTests.CheckoutRounding);
        var session = await rig.Page.LinkAsync(new LinkAsk(LinkVerb.OpenSession, token, Session: checkout));

        await Assert.That(session).IsTypeOf<LinkAnswer.Open>();
        await Assert.That(((LinkAnswer.Open)session).Directory).IsEqualTo(DashboardTests.CheckoutRounding);

        // Refused: a wrong token of the right length, the token and more, a path or an
        // unknown name in place of a folder's, and a session nothing holds.
        LinkAsk[] refused =
        [
            new(LinkVerb.OpenFolder, new string('A', token.Length), Folder: "data"),
            new(LinkVerb.OpenFolder, token + "A", Folder: "data"),
            new(LinkVerb.OpenFolder, PageGate.NewToken(), Folder: "data"),
            new(LinkVerb.OpenFolder, token, Folder: @"C:\Windows"),
            new(LinkVerb.OpenFolder, token, Folder: "browsers"),
            new(LinkVerb.OpenSession, token, Session: "0000000000000000"),
            new(LinkVerb.OpenSession, token, Session: DashboardTests.CheckoutRounding),
        ];

        foreach (var ask in refused)
        {
            await Assert.That(await rig.Page.LinkAsync(ask)).IsTypeOf<LinkAnswer.Refused>().Because(PageLinks.Described(ask));
        }

        // Nothing reached the desktop: the link start opens the folder, never the background.
        await Assert.That(rig.Host.Opened).IsEmpty();
    }

    /// <summary>
    /// A link to Windows' folder picker holds the setup the way the button did: the setup page
    /// says the picker is open and starts no other setup, and the folder chosen reaches the
    /// registrar; a choice of nothing, or a link start that went, changes nothing and frees
    /// the setup again.
    /// </summary>
    /// <remarks>
    /// <b>75 a</b>: the link start shows the picker itself and sends back what was chosen on the
    /// same connection, which <see cref="LinkAnswer.Pick.Finish"/> stands for here.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APickHoldsTheSetupUntilItsChoiceAndHandsTheChosenFolderToTheRegistrar()
    {
        using var install = await ScratchDirectory.CreateAsync("page-link-pick");
        using var project = await ScratchDirectory.CreateAsync("page-link-pick-repo");

        var image = await InstalledLayout.CreateAsync(install.Path);
        var server = InstalledLayout.ServerIn(install.Path);
        var tool = new FakeRegisterAi();
        var state = new AppState
        {
            Version = "9.9.9",
            InstallRoot = install.Path,
            DataRoot = Path.Combine(install.Path, "data"),
            ServerCommand = server,
            ServerRefusal = null,
            Clients =
            [
                ClientFor(RegistrationClient.ClaudeCode, null, RegistrationOwnership.Absent),
                ClientFor(RegistrationClient.Codex, server, RegistrationOwnership.OursAndPresent),
            ],
        };

        await using var rig = await PageRig.CreateAsync(
            registration: new RegisterAiPageRegistration(tool, image, () => Task.FromResult(state), NullLogger.Instance),
            facts: facts => facts with { LinkScheme = Scheme });

        var address = await rig.HandOutAsync(PageKind.Setup);

        using var stream = await rig.StreamAsync(address, 1, "setup");

        _ = await PageRig.GetAsync(address);
        await Assert.That(await DashboardTests.StateContainingAsync(stream, "Every new Codex conversation can use BrowserAI, in any folder.")).IsNotNull();

        var token = rig.Gate.Token;
        var answer = await rig.Page.LinkAsync(new LinkAsk(LinkVerb.Pick, token, Action: "register-in-project", Client: "claude-code"));

        await Assert.That(answer).IsTypeOf<LinkAnswer.Pick>();

        var pick = (LinkAnswer.Pick)answer;

        await Assert.That(pick.Prompt).Contains("Claude Code");
        await Assert.That(pick.Prompt).Contains(RegistrationClient.ClaudeCode.ProjectFileName);

        // While it is open: the page says so, without sending the person to look behind the
        // browser, and no other setup starts, by a link or by the button's action.
        var open = await DashboardTests.StateContainingAsync(stream, "Windows' folder picker is open for Claude Code.");

        await Assert.That(open).IsNotNull();
        await Assert.That(open!).DoesNotContain("behind this browser window");
        await Assert.That(await rig.Page.LinkAsync(new LinkAsk(LinkVerb.Pick, token, Action: "register-in-project", Client: "codex"))).IsTypeOf<LinkAnswer.Refused>();
        await Assert.That((await rig.ActAsync("""{"action":"register","client":"claude-code"}""")).Status).IsEqualTo(204);
        await Assert.That(tool.Calls).IsEmpty();
        await Assert.That(rig.Host.Prompts).IsEmpty();

        // The choice: RegisterAI writes into the folder chosen, and the setup is free again.
        var said = await pick.Finish(FolderPick.Of(project.Path));

        await Assert.That(said).IsNotEmpty();

        var written = tool.Calls[^1];

        await Assert.That(written[0]).IsEqualTo("register");
        await Assert.That(FakeRegisterAi.Option(written, "--client")).IsEqualTo("claude-code");
        await Assert.That(FakeRegisterAi.Option(written, "--scope")).IsEqualTo("project");
        await Assert.That(FakeRegisterAi.Option(written, "--project")).IsEqualTo(project.Path);

        // A second pick, answered with nothing chosen, as a link start that went is: nothing
        // runs, and the setup is free once more.
        var calls = tool.Calls.Count;
        var again = await rig.Page.LinkAsync(new LinkAsk(LinkVerb.Pick, token, Action: "unregister-from-a-project", Client: "codex"));

        await Assert.That(again).IsTypeOf<LinkAnswer.Pick>();
        await Assert.That(await ((LinkAnswer.Pick)again).Finish(FolderPick.Cancelled)).IsEqualTo("No folder was chosen, so nothing changed.");
        await Assert.That(tool.Calls.Count).IsEqualTo(calls);
        await Assert.That(await rig.Page.LinkAsync(new LinkAsk(LinkVerb.Pick, token, Action: "register-in-project", Client: "codex"))).IsTypeOf<LinkAnswer.Pick>();

        // A pick for an action the page does not offer that app, or for an app it does not
        // know, opens no picker.
        await Assert.That(await rig.Page.LinkAsync(new LinkAsk(LinkVerb.Pick, token, Action: "register-in-project", Client: "nobody"))).IsTypeOf<LinkAnswer.Refused>();
    }

    /// <summary>
    /// The banner a page shows once BrowserAI stops answering it says what starts BrowserAI
    /// again, for an install; a build that is not installed says only that it stopped.
    /// </summary>
    /// <remarks>
    /// <b>71 a</b>, in the maintainer's words: <i>"maybe communicate on the page that you need
    /// to restart from the start menu?"</i>. A folder link clicked while no background answers
    /// writes one line in the log and opens nothing, so this sentence is what the person reads.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePageThatStopsAnsweringSaysToStartBrowserAiFromTheStartMenu()
    {
        await using var installed = await PageRig.CreateAsync(facts: facts => facts with { LinkScheme = Scheme });

        var page = await PageRig.GetAsync(await installed.HandOutAsync());

        await Assert.That(page.Body).Contains($"data-unanswered=\"{PageContent.Text(PageContent.Unanswered(installed: true))}\"");
        await Assert.That(PageContent.Unanswered(installed: true)).Contains("Start BrowserAI from the Start Menu");

        await using var bare = await PageRig.CreateAsync(installed: false);

        var plain = await PageRig.GetAsync(await bare.HandOutAsync());

        await Assert.That(plain.Body).Contains("data-unanswered=\"BrowserAI is not answering this page.\"");
    }

    /// <summary>One client's state, constructed and not read.</summary>
    private static ClientState ClientFor(RegistrationClient who, string? registered, RegistrationOwnership ownership) =>
        new()
        {
            Client = who,
            ClientPath = @"C:\double\client.exe",
            ServerComposed = true,
            UserScope = new RegistrationView(RegistrationScope.User, "<constructed>", registered, ownership, null),
            ProjectDirectory = null,
            ProjectScope = null,
        };

    /// <summary>Every link's address in a page, as it is written in the markup.</summary>
    [GeneratedRegex("<a [^>]*href=\"(?<href>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex Hrefs();
}
