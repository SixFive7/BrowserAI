// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Registration;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// The Codex half of registration: the command shapes, the scope lever, the
/// ownership rule and the refusal.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's ask, 2026-09-24, verbatim:</b> <i>"I want the system level
/// and repo level registration to also work for codex and not only for claude
/// code."</i> Q258.
/// </para>
/// <para>
/// ⚠️ <b>NOTHING HERE RUNS THE REAL CLIENT, AND THAT IS ON PURPOSE FOR THIS
/// FILE.</b> Every arm drives <c>FakeClientCommandLine</c>, so the assertions are
/// about the arguments and the environment BrowserAI HANDS a client -- which is
/// the half a test can hold at any client version. The arms that drive a real
/// <c>codex.exe</c> run under a scratch <c>CODEX_HOME</c> and skip loudly when no
/// binary is found; they are separate for that reason.
/// </para>
/// <para>
/// ⚠️ <b>THE MAINTAINER'S OWN <c>~/.codex</c> IS NEVER IN THE PATH OF ANY CALL
/// HERE.</b> The double runs nothing, and the real-client arms force
/// <c>CODEX_HOME</c> at a scratch directory.
/// </para>
/// </remarks>
internal sealed class CodexRegistrationTests
{
    /// <summary>A server path, spelled once.</summary>
    private const string ServerPath = @"C:\x\BrowserAI.Server.exe";

    /// <summary>What a register call must be, exactly.</summary>
    private static readonly string[] ExpectedAdd = ["mcp", "add", "browserai", "--", ServerPath];

    /// <summary>What an unregister call must be, exactly.</summary>
    private static readonly string[] ExpectedRemove = ["mcp", "remove", "browserai"];

    /// <summary>What the list read must be, exactly.</summary>
    private static readonly string[] ExpectedList = ["mcp", "list", "--json"];

    /// <summary>What the single-server read must be, exactly.</summary>
    private static readonly string[] ExpectedGet = ["mcp", "get", "browserai", "--json"];

    /// <summary>What re-registering over an entry of ours asks the client, in order.</summary>
    private static readonly string[] RemoveThenAdd = ["remove", "add"];

    /// <summary>A first project registration into a repository with no home: the write alone.</summary>
    private static readonly string[] AddOnly = ["add"];

    /// <summary>That, then a second pass over the home it created: read, remove ours, add.</summary>
    private static readonly string[] AddListRemoveAdd = ["add", "list", "remove", "add"];

    /// <summary>The three intents, as one array so the analyzer is satisfied.</summary>
    private static readonly RegistrationIntent[] EveryIntent =
        [RegistrationIntent.Install, RegistrationIntent.Update, RegistrationIntent.Uninstall];
    /// <summary>
    /// The register and unregister commands are Codex's own, and the <c>--</c>
    /// separator is there.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-09-24</b> by dropping the separator, which is the one
    /// element whose absence is invisible until a path begins with a dash.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCommandsAreCodexsOwnAndTheSeparatorIsThere()
    {
        await Assert.That(CodexRegistration.AddArguments(ServerPath)).IsEquivalentTo(ExpectedAdd);
        await Assert.That(CodexRegistration.RemoveArguments()).IsEquivalentTo(ExpectedRemove);

        // The read surface, which is what keeps this product out of the TOML.
        await Assert.That(CodexRegistration.ListArguments()).IsEquivalentTo(ExpectedList);
        await Assert.That(CodexRegistration.GetArguments()).IsEquivalentTo(ExpectedGet);

        // ⚠️ AND NO --scope ANYWHERE, which is the structural difference from
        // Claude Code. A flag that does not exist would be accepted by the double
        // and rejected by the client.
        await Assert.That(CodexRegistration.AddArguments("x")).DoesNotContain("--scope");
        await Assert.That(CodexRegistration.RemoveArguments()).DoesNotContain("--scope");
    }

    /// <summary>
    /// A project registration is the same command with <c>CODEX_HOME</c> moved,
    /// and the registrar is what moves it.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>THIS IS THE ARM THAT WOULD HAVE CAUGHT THE WORST MISTAKE AVAILABLE
    /// HERE.</b> Codex takes no scope flag, so a project registration that forgot
    /// the environment variable would succeed, exit 0, and write the USER's
    /// configuration -- a silent scope change with no failure anywhere. Asserting
    /// the environment is asserting the only thing that distinguishes the two.
    /// <b>Planted red</b> by returning an empty environment for the project case.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AProjectRegistrationMovesTheHomeVariableAndNothingElse()
    {
        const string Project = @"C:\repo\project";

        var user = RegistrationClient.Codex.ProjectEnvironment(Project);

        await Assert.That(user.ContainsKey(CodexRegistration.HomeVariable)).IsTrue();
        await Assert.That(user[CodexRegistration.HomeVariable]).IsEqualTo(@"C:\repo\project\.codex");

        // One variable. A registration that ran with a scrubbed environment would
        // not be the registration a person gets running the same command.
        await Assert.That(user.Count).IsEqualTo(1);

        // And the working directory is NOT the lever for this client, which is
        // the other half of the same asymmetry.
        await Assert.That(RegistrationClient.Codex.ProjectWorkingDirectory(Project)).IsNull();
        await Assert.That(RegistrationClient.ClaudeCode.ProjectWorkingDirectory(Project)).IsEqualTo(Project);
        await Assert.That(RegistrationClient.ClaudeCode.ProjectEnvironment(Project).Count).IsEqualTo(0);

        // The directory must exist before the client is run, and the residue the
        // run leaves is named so the registrar can remove it.
        await Assert.That(RegistrationClient.Codex.ProjectDirectoryToCreate(Project)).IsEqualTo(@"C:\repo\project\.codex");
        await Assert.That(RegistrationClient.Codex.ProjectResidue(Project)).IsEqualTo(@"C:\repo\project\.codex\tmp\arg0");
        await Assert.That(RegistrationClient.ClaudeCode.ProjectDirectoryToCreate(Project)).IsNull();
        await Assert.That(RegistrationClient.ClaudeCode.ProjectResidue(Project)).IsNull();
    }

    /// <summary>
    /// What Codex reports is read through its own JSON, and ownership is the
    /// shared rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three readings, and the third is the one that matters.</b> Ours, foreign
    /// and absent -- and <c>Classify</c> is the same function Claude Code's view
    /// uses, called and not copied, so there is one answer to <i>may I delete
    /// this</i>.
    /// </para>
    /// <para>
    /// <b>Both wrapper shapes are accepted</b>, a bare array and an object with a
    /// <c>servers</c> key, because a client bump that adds a wrapper must not read
    /// as nothing registered.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WhatCodexReportsIsReadThroughItsOwnJsonAndOwnershipIsTheSharedRule()
    {
        const string Root = @"C:\Users\someone\AppData\Local\BrowserAI";
        const string Ours = @"C:\Users\someone\AppData\Local\BrowserAI\current\BrowserAI.Server.exe";

        var ours = CodexRegistryView.Parse(
            OneServer(Ours),
            "where",
            Root,
            RegistrationScope.User);

        // ⚠️ OURS AND STALE, NOT OURS AND PRESENT, AND THE DIFFERENCE IS THE
        // SHARED RULE WORKING. `Classify` asks `PeSubsystem.IsConsole` about the
        // path: present means the file is THERE and is a console binary, because an
        // install updated across the 2026-09-15 split has `BrowserAI.exe` sitting
        // where an old registration names it and that file is now the configuration
        // APP. This path is fictional, so it is ours by root and stale by subsystem --
        // which is exactly the reading a registration pointing at a deleted install
        // gets, and it is NOT Foreign, which is the claim.
        await Assert.That(ours.Ownership).IsEqualTo(RegistrationOwnership.OursAndStale);
        await Assert.That(ours.Command).IsEqualTo(Ours);
        await Assert.That(ours.Unreadable).IsNull();

        var foreign = CodexRegistryView.Parse(
            Wrapped(@"D:\elsewhere\BrowserAI.Server.exe"),
            "where",
            Root,
            RegistrationScope.User);

        await Assert.That(foreign.Ownership).IsEqualTo(RegistrationOwnership.Foreign);

        // Another server's entry is not ours to see at all.
        var absent = CodexRegistryView.Parse(
            """[{"name":"something-else","transport":{"command":"c:/x.exe"}}]""",
            "where",
            Root,
            RegistrationScope.User);

        await Assert.That(absent.Ownership).IsEqualTo(RegistrationOwnership.Absent);
        await Assert.That(absent.Unreadable).IsNull();

        // ⚠️ AND OUTPUT THAT IS NOT JSON IS UNREADABLE AND NOT ABSENT. A client
        // that printed a usage message must not read as an empty registry to
        // something that deletes registrations.
        var unreadable = CodexRegistryView.Parse("codex: unrecognized subcommand", "where", Root, RegistrationScope.User);

        await Assert.That(unreadable.Unreadable).IsNotNull();
        await Assert.That(unreadable.Ownership).IsEqualTo(RegistrationOwnership.Absent);
    }

    /// <summary>
    /// A client that cannot be asked is unreadable, and the registrar refuses
    /// instead of writing.
    /// </summary>
    /// <remarks>
    /// <b>The failure this closes is the dangerous one.</b> A non-zero
    /// <c>mcp list</c> that read as <i>nothing is registered</i> would license an
    /// install to overwrite, and an uninstall to delete, whatever is actually
    /// there.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AClientThatCannotBeAskedIsUnreadableAndNothingIsWritten()
    {
        var commands = new FakeClientCommandLine
        {
            Executable = @"C:\codex\codex.exe",
            Always = new CommandOutcome(1, "codex: something went wrong", TimedOut: false, Failure: null),
        };

        var view = CodexRegistryView.Read(commands, @"C:\codex\codex.exe", @"C:\root", home: null, RegistrationScope.User);

        await Assert.That(view.Unreadable).IsNotNull();
        await Assert.That(view.Ownership).IsEqualTo(RegistrationOwnership.Absent);

        // The reading was attempted through the client and not through a file.
        await Assert.That(commands.Invocations.Any(call => call.Contains("list"))).IsTrue();
    }

    /// <summary>
    /// The registrar refuses a foreign Codex entry, for both intents, and names
    /// the Codex command.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-09-24</b> by letting the Codex client through the same
    /// path Claude Code takes, which registered over a foreign entry. The ownership
    /// gate is shared, and this arm is what says it is shared for both clients and
    /// not only for the one it was written against.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AForeignCodexEntryIsNeverTouchedByEitherIntent()
    {
        var commands = new FakeClientCommandLine { Executable = @"C:\codex\codex.exe" };

        // A real layout, because the registrar resolves its own image before it asks
        // anything: an image path that is not there is refused for THAT reason, and
        // the arm would then assert nothing about ownership at all.
        using var install = ScratchDirectory.Create("codex-foreign");
        _ = InstalledLayout.Create(install.Path);
        var server = InstalledLayout.ServerIn(install.Path);

        using var elsewhere = ScratchDirectory.Create("codex-foreign-other");
        _ = InstalledLayout.Create(elsewhere.Path);
        var theirs = InstalledLayout.ServerIn(elsewhere.Path);

        var foreign = new RegistrationView(
            RegistrationScope.User,
            "codex",
            theirs,
            RegistrationOwnership.Foreign,
            null);

        foreach (var intent in EveryIntent)
        {
            var report = McpRegistrar.Apply(
                RegistrationClient.Codex,
                intent,
                server,
                commands,
                NullLogger.Instance,
                _ => foreign);

            await Assert.That(report.Status).IsEqualTo(RegistrationStatus.Refused);
            await Assert.That(report.Detail).Contains("never adopts, overwrites or removes");
        }

        // Nothing was written, for any of the three.
        await Assert.That(commands.Invocations.Any(call => call.Contains("add") || call.Contains("remove"))).IsFalse();
    }

    /// <summary>
    /// The discovery order, and what the refusal says when no shape found a
    /// client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>PATH FIRST IS NOT AN OPTIMISATION, IT IS THE ONLY SHAPE A TEST CAN
    /// FORCE.</b> The other two read real machine locations, so an arm that
    /// asserted them would pass or fail on whether Codex happens to be installed.
    /// What is asserted here is that a client on PATH wins, and that the refusal
    /// NAMES all four places -- because a refusal that says only <i>not found</i>
    /// is what makes a person conclude the product does not support Codex.
    /// </para>
    /// <para>
    /// <b>Planted red</b> by shortening the refusal to the PATH clause, which is
    /// what it would say if the desktop-manifest shape had never been added.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePathShapeWinsAndTheRefusalNamesEveryPlaceItLooked()
    {
        var found = new FakeClientCommandLine { Executable = @"C:\tools\codex.exe" };

        await Assert.That(CodexRegistration.Locate(found)).IsEqualTo(@"C:\tools\codex.exe");

        // The refusal, which is what a machine with no Codex gets.
        var detail = CodexRegistration.NotFoundDetail(@"C:\x\BrowserAI.Server.exe");

        await Assert.That(detail).Contains("codex.exe");
        await Assert.That(detail).Contains("PATH");
        await Assert.That(detail).Contains(ClientCommandLine.FallbackDirectory);
        await Assert.That(detail).Contains(CodexRegistration.DesktopManifestFile);
        await Assert.That(detail).Contains(CodexRegistration.NpmPackageDirectory);
        await Assert.That(detail).Contains(@"codex mcp add browserai -- ""C:\x\BrowserAI.Server.exe""");
    }

    /// <summary>
    /// Codex answers the two idempotence questions differently from Claude Code,
    /// and says so.
    /// </summary>
    /// <remarks>
    /// Measured 2026-09-24 @ codex-cli 0.155.0-alpha.9.2: three consecutive
    /// <c>mcp add</c> calls exit 0 and a <c>mcp remove</c> of an absent server
    /// exits 0, so there is no already-exists or nothing-to-remove failure to
    /// recognise. <b>The predicates exist and answer <see langword="false"/></b>,
    /// which is how a client whose answer is <i>that cannot happen</i> tells the
    /// registrar so instead of leaving the caller to know it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task CodexAnswersTheIdempotenceQuestionsWithFalseAndClaudeCodeDoesNot()
    {
        await Assert.That(CodexRegistration.MeansAlreadyRegistered(1, "already exists")).IsFalse();
        await Assert.That(CodexRegistration.MeansNothingToRemove(1, "No MCP server named")).IsFalse();

        // The control: the other client's predicates DO fire on its own words, so
        // the two falses above are a property of Codex and not of a broken reader.
        await Assert.That(McpClientRegistration.MeansAlreadyRegistered(1, "MCP server browserai already exists in user config")).IsTrue();
        await Assert.That(McpClientRegistration.MeansNothingToRemove(1, @"No MCP server named ""browserai""")).IsTrue();
    }

    /// <summary>
    /// Both clients are in the list the hooks and the dialog iterate, in a stable
    /// order.
    /// </summary>
    /// <remarks>
    /// <b>Order is asserted because a record on disk and a dialog must read the
    /// same way</b>, and because the maintainer asked for separate control per
    /// client: two rows that swapped between runs would make the second row's
    /// button ambiguous.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task BothClientsAreListedInAStableOrderWithDistinctKeys()
    {
        await Assert.That(RegistrationClient.All.Count).IsEqualTo(2);
        await Assert.That(RegistrationClient.All[0].Key).IsEqualTo("claude-code");
        await Assert.That(RegistrationClient.All[1].Key).IsEqualTo("codex");

        // Distinct keys, because the record on disk is keyed by them.
        await Assert.That(RegistrationClient.All.Select(client => client.Key).Distinct().Count()).IsEqualTo(2);

        // And one server name between them: a caller reading two clients'
        // configurations should see one product.
        await Assert.That(RegistrationClient.All.Select(client => client.ServerName).Distinct().Single()).IsEqualTo("browserai");
    }

    /// <summary>
    /// An uninstall over nothing runs nothing and says so, for every client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The defect this closes arrived with the second client.</b> An uninstall
    /// used to run the client's remove whatever the ownership read had said, and
    /// leave the verdict to the exit code: Claude Code exits 1 with <i>No MCP
    /// server named</i>, which reads as nothing to remove. Codex exits 0 on a
    /// remove of a server that is not there -- measured at 0.155.0-alpha.9.2 --
    /// so the same path wrote <i>Removed 'browserai' from Codex</i> into the
    /// record on every machine where Codex had never been registered.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-09-24</b> by running it against the code before the
    /// fix: the first client in the loop ran a <c>remove</c> the reading had
    /// already answered. The Codex half -- <c>Unregistered</c> over nothing -- is
    /// what the real-client arm in <c>RegistrationTests</c> was watched failing
    /// on.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnUninstallOverNothingRunsNothingAndSaysSoForEveryClient()
    {
        using var install = ScratchDirectory.Create("uninstall-nothing");

        var image = InstalledLayout.Create(install.Path);

        foreach (var who in RegistrationClient.All)
        {
            var commands = new FakeClientCommandLine();

            var report = McpRegistrar.Apply(
                who,
                RegistrationIntent.Uninstall,
                image,
                commands,
                NullLogger.Instance,
                _ => new RegistrationView(RegistrationScope.User, "<constructed>", null, RegistrationOwnership.Absent, null));

            await Assert.That(report.Status).IsEqualTo(RegistrationStatus.NothingToUnregister);
            await Assert.That(report.Detail).Contains(who.DisplayName);
            await Assert.That(commands.Verbs).IsEmpty();
        }
    }

    // ---- Project scope through the registrar, both clients -------------------

    /// <summary>
    /// A project registration through the registrar writes the project's own
    /// home, creates it first, and never touches the user's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>THE END-TO-END HALF OF THE WORST MISTAKE AVAILABLE HERE.</b>
    /// <c>AProjectRegistrationMovesTheHomeVariableAndNothingElse</c> holds that
    /// the client's members name the right home; this holds that the registrar
    /// actually APPLIES it to every call it makes -- the read that decides
    /// ownership and the write -- because Codex takes no scope flag, and a call
    /// that lost the variable would exit 0 having written the user's own
    /// configuration.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-09-24</b> by running the project write with an empty
    /// environment: the project's home held no entry afterwards, because the
    /// write had gone to the inherited one.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AProjectRegistrationThroughTheRegistrarWritesTheProjectHomeAndNeverTheUsers()
    {
        using var install = ScratchDirectory.Create("codex-project-register");
        using var project = ScratchDirectory.Create("codex-project-repo");

        var image = InstalledLayout.Create(install.Path);
        var server = InstalledLayout.ServerIn(install.Path);
        var home = CodexRegistration.ProjectHome(project.Path);
        var commands = new FakeClientCommandLine { Executable = @"C:\codex\codex.exe" };

        // Not created beforehand: Codex refuses a home that is not there, so
        // creating it is the registrar's job and part of what is asserted.
        await Assert.That(Directory.Exists(home)).IsFalse();

        var report = McpRegistrar.ApplyToProject(
            RegistrationClient.Codex, register: true, project.Path, image, commands, NullLogger.Instance);

        await Assert.That(report.Status).IsEqualTo(RegistrationStatus.Registered);
        await Assert.That(Directory.Exists(home)).IsTrue();

        // The project's home holds the entry, and the user's holds nothing.
        await Assert.That(commands.CodexHomes.ContainsKey(home)).IsTrue();
        await Assert.That(commands.CodexHomes[home]["browserai"]).IsEqualTo(server);
        await Assert.That(commands.CodexRegistered.Count).IsEqualTo(0);

        // ⚠️ A FRESH REPOSITORY NEEDS NO OWNERSHIP READ -- 2026-09-24. Its home is
        // not there, so nothing is registered in it, and asking Codex is refused
        // outright (measured: "CODEX_HOME points to ..., but that path does not
        // exist"). The one call is the write.
        await Assert.That(commands.Verbs).IsEquivalentTo(AddOnly);

        // A second registration over the home that now exists DOES read, and then
        // removes and re-adds its own entry -- so this pass is where the read and
        // the write can both be seen carrying the lever.
        var again = McpRegistrar.ApplyToProject(
            RegistrationClient.Codex, register: true, project.Path, image, commands, NullLogger.Instance);

        await Assert.That(again.Status).IsEqualTo(RegistrationStatus.Registered);
        await Assert.That(commands.Verbs).IsEquivalentTo(AddListRemoveAdd);
        await Assert.That(commands.CodexRegistered.Count).IsEqualTo(0);

        // Every call, in both passes, carried the lever, and none carried a scope
        // flag the client does not have.
        foreach (var environment in commands.Environments)
        {
            await Assert.That(environment.TryGetValue(CodexRegistration.HomeVariable, out var forced) ? forced : "<none>")
                .IsEqualTo(home);
        }

        await Assert.That(commands.Invocations.Any(call => call.Contains("--scope"))).IsFalse();
    }

    /// <summary>
    /// Registering over a project entry of ours removes it first, so a stale one
    /// is actually rewritten and not reported as written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The failure this closes was in the hand-written path the registrar
    /// replaced.</b> Claude Code's <c>mcp add --scope project</c> over an existing
    /// entry exits 1 with <i>already exists</i>, which the client's own predicate
    /// reads as success -- so a person who clicked register to repair a stale
    /// project entry was told the file had been written and was left with the
    /// stale path. The user-scope install has removed first since 2026-09-16
    /// (<c>Reassert</c>); the project path does the same now.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-09-24</b> by deleting the remove-first: the report
    /// still said Registered, and the only verb the client was asked was the
    /// add that meets <i>already exists</i>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARegisterOverAProjectEntryOfOursRemovesItBeforeWriting()
    {
        using var install = ScratchDirectory.Create("project-reassert");
        using var project = ScratchDirectory.Create("project-reassert-repo");

        var image = InstalledLayout.Create(install.Path);
        var server = InstalledLayout.ServerIn(install.Path);

        // Ours, and stale: the entry names the configuration APP, which is under
        // this install root and is not the MCP server -- the state every
        // pre-split registration is in.
        await File.WriteAllTextAsync(
            McpRegistryView.ProjectConfigFile(project.Path),
            new System.Text.Json.Nodes.JsonObject
            {
                ["mcpServers"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["browserai"] = new System.Text.Json.Nodes.JsonObject { ["command"] = image },
                },
            }.ToJsonString());

        var commands = new FakeClientCommandLine();
        commands.Registered["browserai"] = image;

        var report = McpRegistrar.ApplyToProject(
            RegistrationClient.ClaudeCode, register: true, project.Path, image, commands, NullLogger.Instance);

        await Assert.That(report.Status).IsEqualTo(RegistrationStatus.Registered);
        await Assert.That(commands.Verbs).IsEquivalentTo(RemoveThenAdd);
        await Assert.That(commands.Registered["browserai"]).IsEqualTo(server);

        // Both calls ran IN the repository, which is Claude Code's scope lever:
        // a call that ran anywhere else would have written somewhere else.
        await Assert.That(commands.Directories.All(directory => directory == project.Path)).IsTrue();
        await Assert.That(commands.Invocations.All(call => call.Contains("project"))).IsTrue();
    }

    /// <summary>
    /// A project unregister refuses an entry this install did not write, and
    /// says so when there is nothing to remove.
    /// </summary>
    /// <remarks>
    /// <b>The same gate the user scope has, now applied to a file in somebody's
    /// repository.</b> An entry naming another install's server is that install's
    /// to remove, and removing it would be this product uninstalling another one
    /// from a repository its owner committed.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AProjectUnregisterRefusesAForeignEntryAndSaysWhenThereIsNothing()
    {
        using var install = ScratchDirectory.Create("project-unregister");
        using var project = ScratchDirectory.Create("project-unregister-repo");
        using var elsewhere = ScratchDirectory.Create("project-unregister-other");

        var image = InstalledLayout.Create(install.Path);
        _ = InstalledLayout.Create(elsewhere.Path);

        var home = CodexRegistration.ProjectHome(project.Path);
        _ = Directory.CreateDirectory(home);

        var commands = new FakeClientCommandLine { Executable = @"C:\codex\codex.exe" };
        commands.CodexHomes[home] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["browserai"] = InstalledLayout.ServerIn(elsewhere.Path),
        };

        var refused = McpRegistrar.ApplyToProject(
            RegistrationClient.Codex, register: false, project.Path, image, commands, NullLogger.Instance);

        await Assert.That(refused.Status).IsEqualTo(RegistrationStatus.Refused);
        await Assert.That(refused.Detail).Contains("never adopts, overwrites or removes");
        await Assert.That(commands.CodexHomes[home].ContainsKey("browserai")).IsTrue();
        await Assert.That(commands.Verbs.Contains("remove")).IsFalse();

        // Nothing there: said, and nothing is run to remove it.
        commands.CodexHomes[home].Clear();
        commands.Invocations.Clear();

        var nothing = McpRegistrar.ApplyToProject(
            RegistrationClient.Codex, register: false, project.Path, image, commands, NullLogger.Instance);

        await Assert.That(nothing.Status).IsEqualTo(RegistrationStatus.NothingToUnregister);
        await Assert.That(commands.Verbs.Contains("remove")).IsFalse();
    }

    /// <summary>
    /// The residue a Codex project run leaves is removed innermost first and
    /// only while it is empty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Measured 2026-09-24 at 08:20Z @ codex-cli 0.155.0-alpha.9.2</b>,
    /// with <c>CODEX_HOME</c> at a scratch project: an add, a list and a remove
    /// left <c>tmp\</c> and <c>tmp\arg0\</c>, BOTH DIRECTORIES and both empty. The
    /// code written before that measurement called <c>File.Delete</c> on the
    /// path, which throws on a directory, so it would have left the residue on
    /// every run while looking as though it removed it.
    /// </para>
    /// <para>
    /// <b>Only while empty, because a <c>tmp</c> with somebody else's file in it
    /// is not ours.</b> The second half plants one and requires it to survive.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-09-24</b> by restoring the <c>File.Delete</c>: the empty
    /// <c>arg0</c> directory survived the run.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheResidueOfACodexProjectRunIsRemovedOnlyWhileItIsEmpty()
    {
        using var install = ScratchDirectory.Create("codex-residue");
        using var project = ScratchDirectory.Create("codex-residue-repo");

        var image = InstalledLayout.Create(install.Path);
        var home = CodexRegistration.ProjectHome(project.Path);
        var residue = RegistrationClient.Codex.ProjectResidue(project.Path)!;
        var tmp = Path.GetDirectoryName(residue)!;

        // The shape the real client left: two empty directories.
        _ = Directory.CreateDirectory(residue);

        var commands = new FakeClientCommandLine { Executable = @"C:\codex\codex.exe" };

        _ = McpRegistrar.ApplyToProject(
            RegistrationClient.Codex, register: true, project.Path, image, commands, NullLogger.Instance);

        await Assert.That(Directory.Exists(residue)).IsFalse();
        await Assert.That(Directory.Exists(tmp)).IsFalse();

        // The home itself stays: it is where the client's own file lives.
        await Assert.That(Directory.Exists(home)).IsTrue();

        // And a tmp with anything else in it keeps that thing, and itself.
        _ = Directory.CreateDirectory(residue);
        var keep = Path.Combine(tmp, "somebody-elses.txt");
        await File.WriteAllTextAsync(keep, "not ours");

        _ = McpRegistrar.ApplyToProject(
            RegistrationClient.Codex, register: true, project.Path, image, commands, NullLogger.Instance);

        await Assert.That(Directory.Exists(residue)).IsFalse();
        await Assert.That(File.Exists(keep)).IsTrue();
    }

    /// <summary>One <c>mcp list --json</c> answer, as a bare array.</summary>
    /// <remarks>
    /// <b>Built and not written as a literal</b>, because a Windows path inside a
    /// JSON string needs its backslashes doubled and a literal that lost the
    /// doubling is invalid JSON that reads, through this view, as UNREADABLE --
    /// which is a different assertion from the one the arm means to make.
    /// </remarks>
    /// <param name="command">The command the entry names.</param>
    /// <returns>The JSON.</returns>
    private static string OneServer(string command) =>
        $"[{{\"name\":\"browserai\",\"enabled\":true,\"transport\":{{\"command\":{System.Text.Json.JsonSerializer.Serialize(command)},\"args\":[]}}}}]";

    /// <summary>The same, inside the object wrapper the client may use.</summary>
    /// <param name="command">The command the entry names.</param>
    /// <returns>The JSON.</returns>
    private static string Wrapped(string command) =>
        $"{{\"servers\":{OneServer(command)}}}";
}
