// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;
using BrowserAI.App;
using BrowserAI.App.Interop;
using BrowserAI.Registration;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The configuration app's state and its report, asserted without a window.
/// </summary>
/// <remarks>
/// <para>
/// <b>What each client's registration says, and what it offers, is a pure function
/// of an <c>AppState</c></b>, read once and rendered twice: by the browser tab's
/// registration section and by <c>--report</c>. <i>Corrected 2026-10-03 (previously
/// "Everything the dialog says is a pure function of an AppState ... covered by
/// RealInstallerTests.TheInstalledMainExecutableOpensOneDialogAndNoConsoleWindow
/// and by TaskDialogLayoutTests"): the configuration window is deleted (Q319 b), its
/// arms went with it, and what a click does is <c>PageRegistrationTests</c>'.</i>
/// </para>
/// <para>
/// ⚠️ <b>Nothing here starts the app.</b> The arms below read the same state
/// object <c>--report</c> serialises and the page renders, so a divergence
/// between the two is a red and not a support artifact that disagrees with
/// the screen.
/// </para>
/// </remarks>
internal sealed class ConfigurationAppTests
{
    /// <summary>
    /// Each client's status sentence names the states a person can be in, and
    /// never offers to change one it does not own.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>PER CLIENT SINCE 2026-09-24, and the one real change is
    /// <c>MayRegister</c> over an entry that is already ours and present.</b> It
    /// used to be false there, which meant a person whose registration was
    /// correct had no way to make BrowserAI rewrite it -- the re-register
    /// affordance the research found missing. It is true now, the LABEL is what
    /// changes with the state, and the two states that must still offer nothing
    /// are unchanged: foreign is somebody else's and unreadable is nobody's
    /// business to write over.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheStatusSentenceAndTheOfferedActionsFollowTheStatePerClient()
    {
        using var install = ScratchDirectory.Create("app-state");

        var app = InstalledLayout.Create(install.Path);
        var server = InstalledLayout.ServerIn(install.Path);

        var absent = Claude(StateFor(install.Path, server, null, RegistrationOwnership.Absent));

        await Assert.That(absent.StatusSentence()).Contains("Not registered for your Claude Code projects");
        await Assert.That(absent.ShortStatus).IsEqualTo("not registered");
        await Assert.That(absent.MayRegister).IsTrue();
        await Assert.That(absent.MayUnregister).IsFalse();

        var ours = Claude(StateFor(install.Path, server, server, RegistrationOwnership.OursAndPresent));

        await Assert.That(ours.StatusSentence()).Contains("Registered for all your Claude Code projects");
        await Assert.That(ours.ShortStatus).IsEqualTo("registered");
        await Assert.That(ours.MayUnregister).IsTrue();

        // ⚠️ THE RE-REGISTER AFFORDANCE. Offered over an entry that is already
        // correct, because the entry is ours and a person who edited it by hand
        // needs a way back to what BrowserAI writes.
        await Assert.That(ours.MayRegister).IsTrue();

        var stale = Claude(StateFor(install.Path, server, server + ".gone", RegistrationOwnership.OursAndStale));

        await Assert.That(stale.StatusSentence()).Contains("not there any more");
        await Assert.That(stale.ShortStatus).IsEqualTo("needs repair");
        await Assert.That(stale.MayRegister).IsTrue();
        await Assert.That(stale.MayUnregister).IsFalse();

        // ⚠️ STALE AND NOT MISSING -- 2026-09-16. An entry naming
        // `current\BrowserAI.exe` in a pre-split install points at a file that
        // IS there and is the configuration app, so "which is not there any
        // more" would be a sentence a person could check and find false. The
        // offer is the same one either way: registering again re-points it.
        var wrongBinary = Claude(StateFor(install.Path, server, app, RegistrationOwnership.OursAndStale));

        await Assert.That(wrongBinary.StatusSentence()).Contains("to the wrong binary");
        await Assert.That(wrongBinary.StatusSentence()).Contains(app);
        await Assert.That(wrongBinary.StatusSentence()).DoesNotContain("not there any more");
        await Assert.That(wrongBinary.MayRegister).IsTrue();
        await Assert.That(wrongBinary.MayUnregister).IsFalse();

        // ⚠️ THE ONE THAT MUST NOT OFFER ANYTHING. A foreign entry is another
        // BrowserAI's, and neither registering over it nor removing it is this
        // product's to do.
        var foreign = Claude(StateFor(install.Path, server, @"D:\someone\else\current\BrowserAI.Server.exe", RegistrationOwnership.Foreign));

        await Assert.That(foreign.StatusSentence()).Contains("Another BrowserAI is registered with Claude Code at");
        await Assert.That(foreign.StatusSentence()).Contains(@"D:\someone\else");
        await Assert.That(foreign.ShortStatus).IsEqualTo("another BrowserAI");
        await Assert.That(foreign.MayRegister).IsFalse();
        await Assert.That(foreign.MayUnregister).IsFalse();

        // No client at all: said before anything about registration, because it
        // is the thing a person can act on. And it is said about THAT client, not
        // about the machine, which is the correction two clients force.
        var clientless = ours with { ClientPath = null };

        await Assert.That(clientless.StatusSentence()).Contains("Claude Code was not found on this machine");
        await Assert.That(clientless.ShortStatus).IsEqualTo("not found");
        await Assert.That(clientless.MayRegister).IsFalse();
        await Assert.That(clientless.MayUnregister).IsFalse();

        // An unreadable configuration is never read as "not registered", and
        // nothing is offered on top of it.
        var unreadable = ours with
        {
            UserScope = ours.UserScope with { Unreadable = "'x' is not readable JSON, so what is registered there is unknown." },
        };

        await Assert.That(unreadable.StatusSentence()).Contains("unknown");
        await Assert.That(unreadable.ShortStatus).IsEqualTo("unknown");
        await Assert.That(unreadable.MayRegister).IsFalse();
        await Assert.That(unreadable.MayUnregister).IsFalse();
    }

    /// <summary>
    /// A project registration can be removed, and the link for it appears only
    /// when there is one of ours to remove.
    /// </summary>
    /// <remarks>
    /// <b>The gap this closes is named in the decision:</b> project-scope
    /// unregister did not exist for either client. It is offered without a folder
    /// picker, because the folder is the one the walk already found, and naming it
    /// in the link is what stops the action being pointed somewhere else.
    /// <i>A picker was added beside it the same day, Q289 b. Corrected 2026-10-03
    /// (previously this arm also read the window's links): the page's button for it
    /// is <c>PageRegistrationTests</c>', and this arm holds the offer.</i>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task RemovingAProjectRegistrationIsOfferedOnlyWhereThereIsOneOfOurs()
    {
        using var install = ScratchDirectory.Create("app-project-unregister");

        _ = InstalledLayout.Create(install.Path);

        var server = InstalledLayout.ServerIn(install.Path);
        var folder = install.Path;

        var ours = ClientFor(
            RegistrationClient.ClaudeCode,
            server,
            RegistrationOwnership.OursAndPresent,
            projectDirectory: folder,
            projectScope: new RegistrationView(
                RegistrationScope.Project,
                RegistrationClient.ClaudeCode.ProjectFileIn(folder),
                server,
                RegistrationOwnership.OursAndPresent,
                null));

        await Assert.That(ours.MayUnregisterFromProject).IsTrue();

        // Foreign: the entry is another install's, in somebody's repository, and
        // deleting it would be this product uninstalling another.
        var foreign = ours with
        {
            ProjectScope = ours.ProjectScope! with { Ownership = RegistrationOwnership.Foreign },
        };

        await Assert.That(foreign.MayUnregisterFromProject).IsFalse();

        // Nothing found at all, which is what the Start Menu's working directory
        // produces.
        var none = ours with { ProjectScope = null, ProjectDirectory = null };

        await Assert.That(none.MayUnregisterFromProject).IsFalse();

        // And not offered for the client that has none.
        await Assert.That(ClientFor(RegistrationClient.Codex, null, RegistrationOwnership.Absent).MayUnregisterFromProject).IsFalse();
    }

    /// <summary>
    /// <c>--report</c> writes the state as JSON, and the schema is what the
    /// page shows.
    /// </summary>
    /// <remarks>
    /// <i>Renamed 2026-10-03 (previously
    /// <c>TheReportCarriesEveryAnswerTheWindowWouldHaveShown</c>), when the browser
    /// tab replaced the window as what renders the same state.</i>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheReportCarriesEveryAnswerThePageShows()
    {
        using var install = ScratchDirectory.Create("app-report");
        using var output = ScratchDirectory.Create("app-report-out");

        _ = InstalledLayout.Create(install.Path);

        var server = InstalledLayout.ServerIn(install.Path);
        var state = StateFor(install.Path, server, server, RegistrationOwnership.OursAndPresent);
        var path = StatusReport.Write(state, Path.Combine(output.Path, "nested", "report.json"));

        await Assert.That(File.Exists(path)).IsTrue();

        var text = await File.ReadAllTextAsync(path);

        // LF and a trailing newline, like every other file this repository
        // writes.
        await Assert.That(text).DoesNotContain("\r\n");
        await Assert.That(text).EndsWith("\n");

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;

        await Assert.That(root.GetProperty("schemaVersion").GetInt32()).IsEqualTo(StatusReport.SchemaVersion);
        await Assert.That(root.GetProperty("product").GetString()).IsEqualTo("BrowserAI");
        await Assert.That(root.GetProperty("version").GetString()).IsEqualTo(state.Version);
        await Assert.That(root.GetProperty("installRoot").GetString()).IsEqualTo(install.Path);
        await Assert.That(root.GetProperty("dataRoot").GetString()).IsEqualTo(state.DataRoot);
        await Assert.That(root.GetProperty("serverCommand").GetString()).IsEqualTo(server);

        // The one sentence the state leads with, in the file that stands in for
        // the page.
        await Assert.That(root.GetProperty("status").GetString()).IsEqualTo(state.StatusSentence());

        // ONE ENTRY PER CLIENT SINCE 2026-09-24, and nothing at the top level
        // describes a client any more: this file is what somebody attaches when
        // they say it is not working, and with two clients the useful sentence is
        // almost always about which ONE of them is wrong.
        var clients = root.GetProperty("clients").EnumerateArray().ToList();

        await Assert.That(clients.Count).IsEqualTo(RegistrationClient.All.Count);

        for (var index = 0; index < clients.Count; index++)
        {
            await Assert.That(clients[index].GetProperty("key").GetString())
                .IsEqualTo(RegistrationClient.All[index].Key);
            await Assert.That(clients[index].GetProperty("displayName").GetString())
                .IsEqualTo(RegistrationClient.All[index].DisplayName);
            await Assert.That(clients[index].GetProperty("status").GetString())
                .IsEqualTo(state.Clients[index].StatusSentence());

            // Absent is a null, not a missing property: a reader that has to
            // tell "no project file" from "this build did not write the field"
            // has nothing to go on when the field is simply gone.
            await Assert.That(clients[index].GetProperty("projectScope").ValueKind).IsEqualTo(JsonValueKind.Null);
        }

        var first = clients[0];

        await Assert.That(first.GetProperty("clientFound").GetBoolean()).IsTrue();

        var user = first.GetProperty("userScope");

        await Assert.That(user.GetProperty("scope").GetString()).IsEqualTo("User");
        await Assert.That(user.GetProperty("command").GetString()).IsEqualTo(server);
        await Assert.That(user.GetProperty("ownership").GetString()).IsEqualTo("OursAndPresent");
        await Assert.That(user.GetProperty("unreadable").ValueKind).IsEqualTo(JsonValueKind.Null);

        // And the argument is only honoured when it carries a path.
        await Assert.That(App.Program.ReportPathFrom(["--report", "x"])).IsEqualTo("x");
        await Assert.That(App.Program.ReportPathFrom(["--report"])).IsNull();
        await Assert.That(App.Program.ReportPathFrom([])).IsNull();
    }

    /// <summary>
    /// The nearest project file is found by walking up, and its absence is not a
    /// failure.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheNearestProjectFileIsFoundByWalkingUpwards()
    {
        using var scratch = ScratchDirectory.Create("app-project-walk");

        var deep = Directory.CreateDirectory(Path.Combine(scratch.Path, "a", "b", "c"));

        // ⚠️ THE WALK DOES NOT STOP AT THE SCRATCH ROOT, and asserting that it
        // finds NOTHING here would be asserting something about the machine:
        // this scratch tree lives inside a repository that has a `.mcp.json` of
        // its own, so the walk correctly climbs past it and finds that one.
        // What is asserted is the property the walk has -- the NEAREST file
        // wins -- which is machine-independent, and that nothing inside the
        // scratch tree is claimed before anything is planted there.
        var above = ClientState.NearestProject(RegistrationClient.ClaudeCode, deep.FullName);

        if (above is not null)
        {
            await Assert.That(above.StartsWith(scratch.Path, StringComparison.OrdinalIgnoreCase)).IsFalse();
        }

        await File.WriteAllTextAsync(
            RegistrationClient.ClaudeCode.ProjectFileIn(Path.Combine(scratch.Path, "a")),
            "{ \"mcpServers\": { \"browserai\": { \"command\": \"x\" } } }");

        var found = ClientState.NearestProject(RegistrationClient.ClaudeCode, deep.FullName);

        await Assert.That(found).IsEqualTo(Path.Combine(scratch.Path, "a"));

        // The walk looks for the CLIENT's own file, so a folder carrying one
        // client's project registration is not a project registration for the
        // other -- which is the thing a shared marker would get wrong in a
        // repository that registers only one of them.
        await Assert.That(ClientState.NearestProject(RegistrationClient.Codex, deep.FullName))
            .IsNotEqualTo(Path.Combine(scratch.Path, "a"));

        Directory.CreateDirectory(Path.Combine(scratch.Path, "a", "b", ".codex"));
        await File.WriteAllTextAsync(
            RegistrationClient.Codex.ProjectFileIn(Path.Combine(scratch.Path, "a", "b")),
            "[mcp_servers.browserai]\ncommand = \"x\"\n");

        await Assert.That(ClientState.NearestProject(RegistrationClient.Codex, deep.FullName))
            .IsEqualTo(Path.Combine(scratch.Path, "a", "b"));
    }

    /// <summary>
    /// A folder that could not be turned into a path is not a cancel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Three outcomes, because two of them used to be one.</b> The picker
    /// answered <see langword="null"/> both when the person closed it and when
    /// <c>SHGetPathFromIDListW</c> refused the chosen item -- and the caller read
    /// both as a cancel, so the second closed the picker, wrote nothing and said
    /// nothing at all. The buffer was <c>MAX_PATH</c>, so any folder past 260
    /// characters took that path. <i>Split 2026-09-16.</i>
    /// </para>
    /// <para>
    /// <b>Over constructed inputs, because nothing here can open a modal
    /// window.</b> <c>Decide</c> is the whole of what the picker makes of the two
    /// shell answers, and the P/Invokes around it are the part no run can reach.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFolderThatCouldNotBeTurnedIntoAPathIsNotACancel()
    {
        // Nothing chosen: a cancel, with nothing to say about it.
        await Assert.That(ShellInterop.Decide(chosen: false, resolved: false, null).Outcome)
            .IsEqualTo(FolderPickOutcome.Cancelled);

        // Chosen and resolved: the path, verbatim.
        var picked = ShellInterop.Decide(chosen: true, resolved: true, @"C:\projects	hing");

        await Assert.That(picked.Outcome).IsEqualTo(FolderPickOutcome.Picked);
        await Assert.That(picked.Path).IsEqualTo(@"C:\projects	hing");
        await Assert.That(picked.Reason).IsNull();

        // Chosen and NOT resolved: a failure that carries a sentence, and never
        // a cancel.
        var broke = ShellInterop.Decide(chosen: true, resolved: false, null);

        await Assert.That(broke.Outcome).IsEqualTo(FolderPickOutcome.Failed);
        await Assert.That(broke.Path).IsNull();
        await Assert.That(broke.Reason).IsNotNull();
        await Assert.That(broke.Reason!).Contains("path");

        // Chosen, reported as resolved, and empty: the same, because a folder
        // with no path is not a folder anything can be written into.
        var empty = ShellInterop.Decide(chosen: true, resolved: true, string.Empty);

        await Assert.That(empty.Outcome).IsEqualTo(FolderPickOutcome.Failed);
        await Assert.That(empty.Reason).IsNotNull();
    }

    /// <summary>
    /// A state with the given registration for Claude Code and an unregistered
    /// Codex beside it.
    /// </summary>
    /// <remarks>
    /// <b>Two clients in every state since 2026-09-24</b>, because one is no
    /// longer a shape the product can be in: <c>AppState.Read</c> builds a
    /// <c>ClientState</c> for every member of <c>RegistrationClient.All</c>, and a
    /// helper that built one would let an arm pass against a state the product
    /// cannot be in.
    /// </remarks>
    private static AppState StateFor(string installRoot, string server, string? registered, RegistrationOwnership ownership) =>
        StateFor(
            installRoot,
            server,
            [
                ClientFor(RegistrationClient.ClaudeCode, registered, ownership),
                ClientFor(RegistrationClient.Codex, null, RegistrationOwnership.Absent),
            ]);

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

    /// <summary>The Claude Code half of a state, which is what most arms are about.</summary>
    private static ClientState Claude(AppState state) => state.For(RegistrationClient.ClaudeCode.Key);

    /// <summary>The Codex half of a state.</summary>
    private static ClientState Codex(AppState state) => state.For(RegistrationClient.Codex.Key);
}
