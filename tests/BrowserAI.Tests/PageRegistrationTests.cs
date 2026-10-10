// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.App;
using BrowserAI.App.Interop;
using BrowserAI.App.Page;
using BrowserAI.Interop;
using BrowserAI.Registration;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// The browser tab's registration section: what it says about each client, which
/// buttons it offers, and what each button does, both clients at both scopes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q315 a and Q319 b, the maintainer's words verbatim: <i>"Q315 a"</i> and
/// <i>"Q319 b"</i></b>: the tab replaces the configuration window, and no release is
/// cut until it has. This is the window's registration, moved: the per-client state
/// and the five actions, Q289 b's picker for removing a project entry, Q309 b's
/// errors, Q311's folder picker opened by the coordinator, and Q314 b's sentence
/// about a Codex that predates the install.
/// </para>
/// <para>
/// <b>The registrar is the product's</b> wherever an arm is about what a click does:
/// <see cref="RegisterAiPageRegistration"/> over <see cref="FakeRegisterAi"/>, a
/// scratch install and a state the arm built, which is the seam the window's session
/// had. The picker is a stand-in that opens nothing, and every request goes through
/// the page's own listener and gate over raw HTTP.
/// </para>
/// </remarks>
internal sealed class PageRegistrationTests
{
    /// <summary>
    /// Each client's section says what that client has, every button names its
    /// client and carries its key, and a state offers only what it allows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The maintainer's words of 2026-09-24 verbatim: <i>"I easy I want separate
    /// control over system level registration between codex and claude."</i></b> No
    /// button applies to both clients, and the label of the register button follows
    /// the state, as the window's link did.
    /// </para>
    /// <para>
    /// <b>The first run claims the install and nothing else.</b> The 2026-09-24
    /// rendering found the window's first run saying every client had been
    /// registered whatever had happened; the tab says what each client has, read.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EachClientSaysWhatItHasAndEveryButtonNamesItsClient()
    {
        using var install = ScratchDirectory.Create("page-registration-state");
        using var project = ScratchDirectory.Create("page-registration-project");

        _ = InstalledLayout.Create(install.Path);

        var server = InstalledLayout.ServerIn(install.Path);
        var claude = ClientFor(
            RegistrationClient.ClaudeCode,
            server,
            RegistrationOwnership.OursAndPresent,
            projectDirectory: project.Path,
            projectScope: new RegistrationView(
                RegistrationScope.Project,
                RegistrationClient.ClaudeCode.ProjectFileIn(project.Path),
                "<script>alert(1)</script>",
                RegistrationOwnership.OursAndPresent,
                null));
        var registration = new FakeRegistration { State = StateFor(install.Path, server, [claude, ClientFor(RegistrationClient.Codex, null, RegistrationOwnership.Absent)]) };

        using var rig = new PageRig(registration: registration, occasion: Occasion.FirstRun);

        var address = rig.HandOut();

        using var stream = await rig.StreamAsync(address, 1);

        var page = await PageRig.GetAsync(address);

        await Assert.That(page.Status).IsEqualTo(200).Because(page.Raw);
        await Assert.That(page.Body).Contains(PageContent.Text(PageContent.FirstRunSentence));
        await Assert.That(page.Body).DoesNotContain("has registered itself");

        var html = await StateContainingAsync(stream, "Registered for all your Claude Code projects.");

        await Assert.That(html).IsNotNull();
        await Assert.That(html!).Contains("Not registered for your Codex projects.");

        // The register button's label follows the state, and each names its client.
        await Assert.That(html).Contains(Button("register", "claude-code", "Register again for all my Claude Code projects"));
        await Assert.That(html).Contains(Button("unregister", "claude-code", "Unregister from Claude Code"));
        await Assert.That(html).Contains(Button("unregister-from-project", "claude-code", "Remove BrowserAI from that project for Claude Code"));
        await Assert.That(html).Contains(PageContent.Text(RegistrationClient.ClaudeCode.ProjectFileIn(project.Path)));
        await Assert.That(html).Contains(Button("register", "codex", "Register for all my Codex projects"));
        await Assert.That(html).Contains(Button("register-in-project", "codex", "Register in a project for Codex"));
        await Assert.That(html).Contains(Button("unregister-from-a-project", "codex", "Remove from a project for Codex"));
        await Assert.That(html).DoesNotContain("data-action=\"unregister\" data-client=\"codex\"");
        await Assert.That(html).DoesNotContain("data-action=\"unregister-from-project\" data-client=\"codex\"");

        // What a project file says is text, and Q314 b's sentence is Codex's alone.
        await Assert.That(html).Contains("&lt;script&gt;alert(1)&lt;/script&gt;");
        await Assert.That(html).DoesNotContain("<script>alert(1)</script>");
        await Assert.That(html.Split(PageContent.Text(PageContent.CodexStartedBeforeTheInstall)).Length - 1).IsEqualTo(1);

        // Every registration button carries exactly one client's key, and its label names that client.
        foreach (var (action, client, label) in Buttons(html))
        {
            var named = RegistrationClient.All.Single(each => string.Equals(each.Key, client, StringComparison.Ordinal));

            await Assert.That(label).Contains(named.DisplayName).Because($"{action} for {client}");
            await Assert.That(RegistrationClient.All.Count(each => label.Contains(each.DisplayName, StringComparison.Ordinal))).IsEqualTo(1);
        }

        // A foreign entry and an unreadable one offer nothing for that client.
        registration.State = StateFor(
            install.Path,
            server,
            [
                ClientFor(RegistrationClient.ClaudeCode, @"D:\someone\else\current\BrowserAI.Server.exe", RegistrationOwnership.Foreign),
                ClientFor(RegistrationClient.Codex, server, RegistrationOwnership.OursAndPresent) with
                {
                    UserScope = new RegistrationView(RegistrationScope.User, "<constructed>", null, RegistrationOwnership.Absent, "'config.toml' is not readable TOML, so what is registered there is unknown."),
                },
            ]);

        await Assert.That((await rig.ActAsync("""{"action":"read-registration"}""")).Status).IsEqualTo(204);

        html = await StateContainingAsync(stream, "Another BrowserAI is registered with Claude Code");

        await Assert.That(html).IsNotNull();
        await Assert.That(html!).Contains("is not readable TOML");
        await Assert.That(html).DoesNotContain("data-action=\"register\" data-client=");
        await Assert.That(html).DoesNotContain("data-action=\"unregister\" data-client=");

        // A read that fails is one sentence with the raw text under Show details, Q309 b.
        registration.State = null;

        await Assert.That((await rig.ActAsync("""{"action":"read-registration"}""")).Status).IsEqualTo(204);

        html = await StateContainingAsync(stream, "BrowserAI could not read how it is registered.");

        await Assert.That(html).IsNotNull();
        await Assert.That(html!).Contains("<details><summary>Show details</summary><pre>The registration could not be read.</pre></details>");
    }

    /// <summary>
    /// A registration button runs the registrar for its own client and says what
    /// happened, an action the page did not offer is refused, and a failure is one
    /// sentence with the registrar's text under Show details.
    /// </summary>
    /// <remarks>
    /// <b>Q309 b, the maintainer's words verbatim: <i>"Q309 b"</i></b>, put to him as
    /// <i>"One plain sentence, with the raw text, or Codex's own first error line,
    /// under "Show details"."</i> The registrar's text carries RegisterAI's error, what
    /// the client printed and the command to run by hand.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AButtonRunsTheRegistrarForItsClientAndAFailureIsOneSentenceWithDetails()
    {
        using var install = ScratchDirectory.Create("page-registration-click");

        var image = InstalledLayout.Create(install.Path);
        var server = InstalledLayout.ServerIn(install.Path);
        var tool = new FakeRegisterAi();

        tool.Register("claude-code", server);

        var state = StateFor(
            install.Path,
            server,
            [
                ClientFor(RegistrationClient.ClaudeCode, server, RegistrationOwnership.OursAndPresent),
                ClientFor(RegistrationClient.Codex, null, RegistrationOwnership.Absent),
            ]);

        using var rig = new PageRig(registration: new RegisterAiPageRegistration(tool, image, () => state, NullLogger.Instance));

        var address = rig.HandOut();

        using var stream = await rig.StreamAsync(address, 1);

        _ = await PageRig.GetAsync(address);
        await Assert.That(await StateContainingAsync(stream, "Registered for all your Claude Code projects.")).IsNotNull();

        // Nothing runs until a button is clicked.
        await Assert.That(tool.Calls.Where(call => call[0] is "register" or "unregister")).IsEmpty();

        // Codex, registered for all projects: RegisterAI is asked about Codex alone,
        // with the explicit flag that rewrites an entry of ours (Q347 a).
        await Assert.That((await rig.ActAsync("""{"action":"register","client":"codex"}""")).Status).IsEqualTo(204);

        var html = await StateContainingAsync(stream, "Registered 'browserai' with Codex");

        await Assert.That(html).IsNotNull();
        await Assert.That(html!).Contains(PageContent.Text(RegistrationClient.Codex.RestartHint));

        var register = tool.Calls.Single(call => call[0] is "register");

        await Assert.That(FakeRegisterAi.Option(register, "--client")).IsEqualTo("codex");
        await Assert.That(FakeRegisterAi.Option(register, "--scope")).IsEqualTo("user");
        await Assert.That(register).Contains("--replace");
        await Assert.That(tool.UserEntry("codex")).IsEqualTo(server);
        await Assert.That(tool.UserEntry("claude-code")).IsEqualTo(server);

        // Claude Code, removed: Codex's entry is not touched.
        await Assert.That((await rig.ActAsync("""{"action":"unregister","client":"claude-code"}""")).Status).IsEqualTo(204);
        await Assert.That(await StateContainingAsync(stream, "Removed 'browserai' from Claude Code")).IsNotNull();
        await Assert.That(tool.UserEntry("claude-code")).IsNull();
        await Assert.That(tool.UserEntry("codex")).IsEqualTo(server);

        // A failure: one sentence, and the registrar's text under Show details.
        tool.Failing.Add("codex");

        await Assert.That((await rig.ActAsync("""{"action":"register","client":"codex"}""")).Status).IsEqualTo(204);

        html = await StateContainingAsync(stream, "BrowserAI could not register itself with Codex.");

        await Assert.That(html).IsNotNull();
        await Assert.That(html!).Contains("<details><summary>Show details</summary><pre>");
        await Assert.That(html).Contains(PageContent.Text("codex mcp add browserai"));

        // A client the page was not shown, an action the state does not offer, and a
        // request with no client are refused like an unknown action, and run nothing.
        var calls = tool.Calls.Count;

        string[] refused =
        [
            """{"action":"register","client":"cursor"}""",
            """{"action":"unregister","client":"codex"}""",
            """{"action":"unregister-from-project","client":"claude-code"}""",
            """{"action":"register"}""",
            """{"action":"register","client":"claude-code","command":"calc.exe"}""",
        ];

        foreach (var body in refused.Take(4))
        {
            var answer = await rig.ActAsync(body);

            await Assert.That(answer.Status).IsEqualTo(404).Because(body);
            await Assert.That(answer.Body).IsEmpty();
        }

        await Assert.That(tool.Calls.Count).IsEqualTo(calls);

        // A command sent with a register is not read: the registrar composes the one it writes.
        tool.Failing.Clear();

        await Assert.That((await rig.ActAsync(refused[4])).Status).IsEqualTo(204);
        await Assert.That(await StateContainingAsync(stream, "Registered 'browserai' with Claude Code")).IsNotNull();
        await Assert.That(FakeRegisterAi.Command(tool.Calls[^1])).IsEqualTo(server);
    }

    /// <summary>
    /// A project folder comes from Windows' picker, asked only on its button's
    /// click and opened by the coordinator, and never from the page; removing from a
    /// picked folder removes only an entry of ours.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q311, the maintainer's words verbatim: <i>"Q311 b or c or a signal to the
    /// backend app to open a picker. Whatever is the least amount of complexity and
    /// code."</i></b> The page asks; the coordinator opens the picker. A folder sent in
    /// the request is never read.
    /// </para>
    /// <para>
    /// <b>Q289 b, verbatim: <i>"Q289 b"</i></b>: removing from a picked folder is safe
    /// because only an entry this install wrote is ever removed. Another install's
    /// entry is refused and stays, and for Codex the person's own entry stays too.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AProjectFolderComesFromWindowsPickerAndOnlyOurEntryIsRemovedFromIt()
    {
        using var install = ScratchDirectory.Create("page-registration-pick");
        using var project = ScratchDirectory.Create("page-registration-pick-repo");
        using var elsewhere = ScratchDirectory.Create("page-registration-pick-other");

        var image = InstalledLayout.Create(install.Path);
        var server = InstalledLayout.ServerIn(install.Path);

        _ = InstalledLayout.Create(elsewhere.Path);

        var theirs = InstalledLayout.ServerIn(elsewhere.Path);
        var tool = new FakeRegisterAi();

        tool.Register("codex", server);
        tool.RegisterIn("codex", project.Path, RegistrationTarget.AppFileName);

        var state = StateFor(
            install.Path,
            server,
            [
                ClientFor(RegistrationClient.ClaudeCode, null, RegistrationOwnership.Absent),
                ClientFor(RegistrationClient.Codex, server, RegistrationOwnership.OursAndPresent),
            ]);

        using var rig = new PageRig(registration: new RegisterAiPageRegistration(tool, image, () => state, NullLogger.Instance));

        var address = rig.HandOut();

        using var stream = await rig.StreamAsync(address, 1);

        _ = await PageRig.GetAsync(address);
        await Assert.That(await StateContainingAsync(stream, "Registered for all your Codex projects.")).IsNotNull();

        // Nothing is asked when the page loads.
        await Assert.That(rig.Host.Prompts).IsEmpty();

        // Register in a project for Claude Code: the picker is asked once, naming the
        // client, and RegisterAI writes into the folder picked, not the one sent.
        rig.Host.Pick = FolderPick.Of(project.Path);

        await Assert.That((await rig.ActAsync("""{"action":"register-in-project","client":"claude-code","folder":"C:\\Windows"}""")).Status).IsEqualTo(204);
        await Assert.That(await StateContainingAsync(stream, RegistrationClient.ClaudeCode.ProjectHint)).IsNotNull();
        await Assert.That(rig.Host.Prompts.Count).IsEqualTo(1);
        await Assert.That(rig.Host.Prompts.Single()).Contains("Claude Code");

        var written = tool.Calls[^1];

        await Assert.That(written[0]).IsEqualTo("register");
        await Assert.That(FakeRegisterAi.Option(written, "--client")).IsEqualTo("claude-code");
        await Assert.That(FakeRegisterAi.Option(written, "--scope")).IsEqualTo("project");
        await Assert.That(FakeRegisterAi.Option(written, "--project")).IsEqualTo(project.Path);
        await Assert.That(tool.Entries.ContainsKey(("claude-code", "project", @"C:\Windows"))).IsFalse();

        // Remove from a picked project for Codex: the project's entry goes, and the
        // person's own Codex entry stays.
        await Assert.That((await rig.ActAsync("""{"action":"unregister-from-a-project","client":"codex"}""")).Status).IsEqualTo(204);
        await Assert.That(await StateContainingAsync(stream, "from '")).IsNotNull();

        var removed = tool.Calls[^1];

        await Assert.That(removed[0]).IsEqualTo("unregister");
        await Assert.That(FakeRegisterAi.Option(removed, "--client")).IsEqualTo("codex");
        await Assert.That(FakeRegisterAi.Option(removed, "--project")).IsEqualTo(project.Path);
        await Assert.That(FakeRegisterAi.Option(removed, "--owned-root")).IsEqualTo(install.Path);
        await Assert.That(tool.Entries.ContainsKey(("codex", "project", project.Path))).IsFalse();
        await Assert.That(tool.UserEntry("codex")).IsEqualTo(server);

        // Another install's entry in the picked folder is refused and stays.
        tool.RegisterIn("claude-code", project.Path, theirs);

        await Assert.That((await rig.ActAsync("""{"action":"unregister-from-a-project","client":"claude-code"}""")).Status).IsEqualTo(204);

        var refused = await StateContainingAsync(stream, "BrowserAI changed nothing for Claude Code.");

        await Assert.That(refused).IsNotNull();
        await Assert.That(refused!).Contains("never adopts, overwrites or removes");
        await Assert.That(tool.Entries[("claude-code", "project", project.Path)]).IsEqualTo(theirs);

        // A picker that could not give a path says so and runs nothing; a cancel says
        // nothing new and runs nothing.
        var calls = tool.Calls.Count;

        rig.Host.Pick = FolderPick.Broke("Windows could not give a path for that folder.");

        await Assert.That((await rig.ActAsync("""{"action":"register-in-project","client":"codex"}""")).Status).IsEqualTo(204);
        await Assert.That(await StateContainingAsync(stream, "The folder that was chosen could not be used")).IsNotNull();

        rig.Host.Pick = FolderPick.Cancelled;

        await Assert.That((await rig.ActAsync("""{"action":"register-in-project","client":"codex"}""")).Status).IsEqualTo(204);
        await Assert.That(await WaitForAsync(() => rig.Host.Prompts.Count is 5)).IsTrue();
        await Assert.That(tool.Calls.Count).IsEqualTo(calls);
    }

    /// <summary>
    /// While Windows' folder picker is open the page says so and where it may be, and
    /// runs no other registration, for either client, until the picker is answered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What the window's owned picker gave, moved with its deletion on
    /// 2026-10-03.</b> A picker owned by the window disabled it, so nothing under the
    /// picker could be clicked, and
    /// <c>HouseRuleTests.EveryFolderPickerIsOwnedByTheDialogThatOpenedIt</c> held the
    /// owner. The coordinator opens the picker with no owner (Q311) and the page's
    /// buttons stay live in the browser, so the refusal is the page's own.
    /// </para>
    /// <para>
    /// <b>Where the picker opens is not asserted, and cannot be from here</b>: by
    /// Windows' focus rules it may open behind the browser, the page's sentence says
    /// so, and looking at it on the real desktop is in TODO.md.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WhileThePickerIsOpenThePageSaysSoAndRunsNoOtherRegistration()
    {
        using var install = ScratchDirectory.Create("page-registration-open");
        using var project = ScratchDirectory.Create("page-registration-open-repo");

        var image = InstalledLayout.Create(install.Path);
        var server = InstalledLayout.ServerIn(install.Path);
        var tool = new FakeRegisterAi();

        var state = StateFor(
            install.Path,
            server,
            [
                ClientFor(RegistrationClient.ClaudeCode, null, RegistrationOwnership.Absent),
                ClientFor(RegistrationClient.Codex, null, RegistrationOwnership.Absent),
            ]);

        using var rig = new PageRig(registration: new RegisterAiPageRegistration(tool, image, () => state, NullLogger.Instance));

        var address = rig.HandOut();

        using var stream = await rig.StreamAsync(address, 1);

        _ = await PageRig.GetAsync(address);
        await Assert.That(await StateContainingAsync(stream, "Register in a project for Claude Code")).IsNotNull();

        var open = new TaskCompletionSource<FolderPick>(TaskCreationOptions.RunContinuationsAsynchronously);

        rig.Host.Open = open;

        await Assert.That((await rig.ActAsync("""{"action":"register-in-project","client":"claude-code"}""")).Status).IsEqualTo(204);

        var waiting = await StateContainingAsync(stream, "Windows' folder picker is open for Claude Code. If it is not in front, it is behind this browser window.");

        await Assert.That(waiting).IsNotNull();
        await Assert.That(waiting!).DoesNotContain("data-action=\"register");
        await Assert.That(waiting).DoesNotContain("data-action=\"unregister");

        // The page says so as the registration starts, and the picker is asked from the
        // registration's own task a moment later.
        await Assert.That(await WaitForAsync(() => rig.Host.Prompts.Count is 1)).IsTrue();

        // A click the page no longer shows, for the other client and for this one, is
        // taken and runs nothing: no second picker, and nothing asked of RegisterAI.
        await Assert.That((await rig.ActAsync("""{"action":"register","client":"codex"}""")).Status).IsEqualTo(204);
        await Assert.That((await rig.ActAsync("""{"action":"register-in-project","client":"codex"}""")).Status).IsEqualTo(204);
        await Assert.That((await rig.ActAsync("""{"action":"register","client":"claude-code"}""")).Status).IsEqualTo(204);
        await Assert.That(rig.Host.Prompts.Count).IsEqualTo(1);
        await Assert.That(Changes(tool)).IsEmpty();

        // Answered, the registration it was opened for runs, and it alone.
        rig.Host.Open = null;
        open.SetResult(FolderPick.Of(project.Path));

        await Assert.That(await StateContainingAsync(stream, RegistrationClient.ClaudeCode.ProjectHint)).IsNotNull();

        // One more click once nothing is running, so that anything the refused
        // clicks had started would have finished by the time the count is read.
        await Assert.That((await rig.ActAsync("""{"action":"register","client":"codex"}""")).Status).IsEqualTo(204);
        await Assert.That(await StateContainingAsync(stream, "Registered 'browserai' with Codex")).IsNotNull();

        var ran = Changes(tool);

        await Assert.That(rig.Host.Prompts.Count).IsEqualTo(1);
        await Assert.That(ran.Count).IsEqualTo(2);
        await Assert.That(FakeRegisterAi.Option(ran[0], "--client")).IsEqualTo("claude-code");
        await Assert.That(FakeRegisterAi.Option(ran[0], "--project")).IsEqualTo(project.Path);
        await Assert.That(FakeRegisterAi.Option(ran[1], "--client")).IsEqualTo("codex");
        await Assert.That(FakeRegisterAi.Option(ran[1], "--scope")).IsEqualTo("user");
    }

    /// <summary>
    /// Register and Repair write the arguments the install itself registers with: the
    /// data root an install made with <c>BROWSERAI_ROOT</c> names, read from the
    /// definition its hooks saved, so a repair keeps it; an install that named none
    /// registers <c>--mcp</c> alone.
    /// </summary>
    /// <remarks>
    /// <b>Found by the texts review of 2026-10-10</b>: the page registered <c>--mcp</c>
    /// alone, so a Repair rewrote a hook's <c>--mcp --data-root</c> entry without its
    /// root, and the client then started a relay that no background serves.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task RegisterAndRepairKeepTheDataRootTheInstallRegistersWith()
    {
        using var install = ScratchDirectory.Create("page-registration-root");

        var image = InstalledLayout.Create(install.Path);
        var server = InstalledLayout.ServerIn(install.Path);
        var root = Path.Combine(install.Path, "a data root");
        var tool = new FakeRegisterAi();

        tool.Register("claude-code", server);

        var state = StateFor(
            install.Path,
            server,
            [
                ClientFor(RegistrationClient.ClaudeCode, server, RegistrationOwnership.OursAndStale),
                ClientFor(RegistrationClient.Codex, null, RegistrationOwnership.Absent),
            ]);

        using var rig = new PageRig(registration: new RegisterAiPageRegistration(tool, image, () => state, NullLogger.Instance));

        var address = rig.HandOut();

        using var stream = await rig.StreamAsync(address, 1);

        _ = await PageRig.GetAsync(address);

        // An install whose hooks named no data root: --mcp alone.
        await Assert.That((await rig.ActAsync("""{"action":"register","client":"codex"}""")).Status).IsEqualTo(204);
        await Assert.That(await StateContainingAsync(stream, "Registered 'browserai' with Codex")).IsNotNull();
        await Assert.That(string.Join(" ", AfterTheCommand(Changes(tool)[^1]))).IsEqualTo("--mcp");

        // The hooks saved a definition naming the data root: the Repair keeps it.
        await File.WriteAllTextAsync(
            Path.Combine(install.Path, SignInTask.SavedDefinitionFileName),
            SignInTask.DefinitionFor(server, NamedPipes.CurrentUserSid(), install.Path, SignInTask.ArgumentsFor(root, null)));

        await Assert.That((await rig.ActAsync("""{"action":"register","client":"claude-code"}""")).Status).IsEqualTo(204);
        await Assert.That(await StateContainingAsync(stream, "Registered 'browserai' with Claude Code")).IsNotNull();
        await Assert.That(string.Join(" ", AfterTheCommand(Changes(tool)[^1]))).IsEqualTo($"--mcp --data-root {root}");
    }

    /// <summary>
    /// The status page names the whole command a client starts: BrowserAI.exe with
    /// <c>--mcp</c>, and the install's data root when it names one, as every
    /// registration writes it. Started with no argument the same file opens the page.
    /// </summary>
    /// <remarks>
    /// <b>#67 of the texts review, 2026-10-10</b>: the page named the file alone, while
    /// the sentence after a Codex project registration says the entry names it with
    /// <c>--mcp</c>.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheStatusPageNamesTheWholeCommandAClientStarts()
    {
        const string Server = @"C:\Users\someone\AppData\Local\BrowserAI.app\current\BrowserAI.exe";

        await Assert.That(Where(Server, [RegistrationTarget.McpArgument]))
            .Contains("<p>The server a client starts: <code>&quot;C:\\Users\\someone\\AppData\\Local\\BrowserAI.app\\current\\BrowserAI.exe&quot; --mcp</code></p>");

        await Assert.That(Where(Server, [RegistrationTarget.McpArgument, SignInTask.DataRootArgument, @"C:\Users\someone\BrowserAI data"]))
            .Contains("&quot;C:\\Users\\someone\\AppData\\Local\\BrowserAI.app\\current\\BrowserAI.exe&quot; --mcp --data-root &quot;C:\\Users\\someone\\BrowserAI data&quot;</code>");
    }

    /// <summary>The status page's main part for a server command and its arguments.</summary>
    private static string Where(string server, IReadOnlyList<string> arguments) =>
        PageContent.Fragment(
            new PageView(
                new PageFacts
                {
                    Version = "9.0.0",
                    InstallRoot = @"C:\Users\someone\AppData\Local\BrowserAI.app",
                    DataRoot = @"C:\data",
                    LogDirectory = @"C:\data\logs",
                    ServerCommand = server,
                    ServerArguments = arguments,
                    ServerRefusal = null,
                },
                SessionsSnapshot.Empty,
                null),
            PageKind.Status,
            1,
            Occasion.Ordinary,
            DateTimeOffset.UnixEpoch);

    /// <summary>What a registration passes after the command it names.</summary>
    private static IEnumerable<string> AfterTheCommand(IReadOnlyList<string> call) =>
        call.SkipWhile(argument => argument is not "--").Skip(2);

    /// <summary>Every call that asked RegisterAI to change an entry, in order.</summary>
    private static List<IReadOnlyList<string>> Changes(FakeRegisterAi tool) =>
        [.. tool.Calls.Where(call => call[0] is "register" or "unregister")];

    /// <summary>The HTML of one registration button as <see cref="PageContent"/> writes it.</summary>
    private static string Button(string action, string client, string label) =>
        $"<button type=\"button\" data-action=\"{action}\" data-client=\"{client}\">{PageContent.Text(label)}</button>";

    /// <summary>Every registration button in a fragment: its action, its client and its label.</summary>
    private static IEnumerable<(string Action, string Client, string Label)> Buttons(string html) =>
        System.Text.RegularExpressions.Regex.Matches(html, "<button type=\"button\" data-action=\"(?<action>[a-z-]+)\" data-client=\"(?<client>[a-z-]+)\">(?<label>[^<]*)</button>")
            .Select(match => (match.Groups["action"].Value, match.Groups["client"].Value, match.Groups["label"].Value));

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

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        return true;
    }

    /// <summary>A state carrying the given clients exactly.</summary>
    private static AppState StateFor(string installRoot, string server, IReadOnlyList<ClientState> clients) =>
        new()
        {
            Version = "9.9.9",
            InstallRoot = installRoot,
            DataRoot = Path.Combine(installRoot, "data"),
            ServerCommand = server,
            ServerRefusal = null,
            Clients = clients,
        };

    /// <summary>One client's state, constructed and not read.</summary>
    private static ClientState ClientFor(
        RegistrationClient who,
        string? registered,
        RegistrationOwnership ownership,
        string? clientPath = @"C:\double\client.exe",
        string? projectDirectory = null,
        RegistrationView? projectScope = null) =>
        new()
        {
            Client = who,
            ClientPath = clientPath,
            ServerComposed = true,
            UserScope = new RegistrationView(RegistrationScope.User, "<constructed>", registered, ownership, null),
            ProjectDirectory = projectDirectory,
            ProjectScope = projectScope,
        };
}
