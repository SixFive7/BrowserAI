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
