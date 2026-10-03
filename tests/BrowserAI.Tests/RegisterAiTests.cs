// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BrowserAI.App;
using BrowserAI.Hosting;
using BrowserAI.Registration;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Tests;

/// <summary>
/// BrowserAI's side of RegisterAI: the command lines it builds, what it makes of every
/// answer, what it does when there is no answer, and the schema the shipped tool
/// writes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q332, decided 2026-10-01 by the maintainer, verbatim: <i>"Go for only the small
/// command line program."</i></b> Registration is RegisterAI's, run as a program, and
/// these arms drive it through <see cref="FakeRegisterAi"/>, which decides the way
/// RegisterAI does, except the last, which runs the RegisterAI the payload carries.
/// </para>
/// <para>
/// <b>Q350, verbatim: <i>"Q350 a"</i></b>: one test that fails when the tool's output
/// schema is not the one this build reads. That is
/// <see cref="ThePayloadsRegisterAiWritesTheSchemaThisReaderReads"/>.
/// </para>
/// </remarks>
internal sealed class RegisterAiTests
{
    /// <summary>The top-level keys the reader takes out of every document.</summary>
    private static readonly string[] EnvelopeKeys = ["tool", "schema", "verb", "exitCode", "error", "results"];

    /// <summary>The keys the reader takes out of an entry.</summary>
    private static readonly string[] EntryKeys = ["state", "command", "resolvesTo"];

    /// <summary>The keys the reader takes out of an advice item.</summary>
    private static readonly string[] AdviceKeys = ["code", "text", "command"];

    /// <summary>The action words the registrar matches on.</summary>
    private static readonly string[] ActionWords = ["none", "added", "replaced", "removed", "refused-foreign", "refused-unreadable", "client-not-found", "failed"];

    /// <summary>The one argument that asks a RegisterAI for its version.</summary>
    private static readonly string[] VersionOnly = ["--version"];

    /// <summary>The state words the reader matches on.</summary>
    private static readonly string[] StateWords = ["absent", "ours", "ours-stale", "foreign", "unreadable", "unknown"];

    /// <summary>
    /// Q347 a: an install or an update over an entry of ours that already names this
    /// server leaves it exactly as it is, and only the window's explicit register
    /// passes <c>--replace</c>.
    /// </summary>
    /// <remarks>
    /// <b>The maintainer's decision, verbatim: <i>"Q347 a"</i>.</b> Until 2026-10-03 an
    /// install removed and re-added the entry, which also dropped any argument a person
    /// had added to it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnInstallOverAnEntryOfOursThatMatchesLeavesItAsItIs()
    {
        using var install = ScratchDirectory.Create("registerai-matching");

        var image = InstalledLayout.Create(install.Path);
        var server = InstalledLayout.ServerIn(install.Path);
        var tool = new FakeRegisterAi();
        var (logger, _) = Capture();

        tool.Register("claude-code", server);

        foreach (var intent in new[] { RegistrationIntent.Install, RegistrationIntent.Update })
        {
            var report = McpRegistrar.Apply(RegistrationClient.ClaudeCode, intent, image, tool, logger);

            await Assert.That(report.Status).IsEqualTo(RegistrationStatus.AlreadyRegistered);
            await Assert.That(report.Detail).Contains("left exactly as it is");
            await Assert.That(tool.Calls[^1].Contains("--replace")).IsFalse();
        }

        // The window's register is a person asking for exactly the rewrite.
        var again = McpRegistrar.Apply(RegistrationClient.ClaudeCode, RegistrationIntent.Install, image, tool, logger, replace: true);

        await Assert.That(again.Status).IsEqualTo(RegistrationStatus.Registered);
        await Assert.That(tool.Calls[^1].Contains("--replace")).IsTrue();
        await Assert.That(tool.UserEntry("claude-code")).IsEqualTo(server);
    }

    /// <summary>
    /// Every action RegisterAI reports reads as the status and the sentence BrowserAI
    /// has always given for it, and a refusal or a failure carries the line a person
    /// can run.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryAnswerReadsAsTheStatusAndSentenceBrowserAiGives()
    {
        using var install = ScratchDirectory.Create("registerai-answers");
        using var elsewhere = ScratchDirectory.Create("registerai-answers-other");

        var image = InstalledLayout.Create(install.Path);
        var server = InstalledLayout.ServerIn(install.Path);
        var theirs = InstalledLayout.ServerIn(elsewhere.Path);
        var (logger, log) = Capture();

        _ = InstalledLayout.Create(elsewhere.Path);

        // Nothing there: added.
        var tool = new FakeRegisterAi();
        var added = McpRegistrar.Apply(RegistrationClient.ClaudeCode, RegistrationIntent.Install, image, tool, logger);

        await Assert.That(added.Status).IsEqualTo(RegistrationStatus.Registered);
        await Assert.That(added.Detail).Contains($"pointing at '{server}'");
        await Assert.That(tool.UserEntry("claude-code")).IsEqualTo(server);

        // Removed, then nothing to remove.
        await Assert.That(McpRegistrar.Apply(RegistrationClient.ClaudeCode, RegistrationIntent.Uninstall, image, tool, logger).Status)
            .IsEqualTo(RegistrationStatus.Unregistered);

        var nothing = McpRegistrar.Apply(RegistrationClient.ClaudeCode, RegistrationIntent.Uninstall, image, tool, logger);

        await Assert.That(nothing.Status).IsEqualTo(RegistrationStatus.NothingToUnregister);
        await Assert.That(nothing.IsWhatWasAskedFor).IsTrue();

        // Ours and stale: an older file under this root, repaired.
        tool.Register("claude-code", Path.Combine(install.Path, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.AppFileName));

        await Assert.That(McpRegistrar.Apply(RegistrationClient.ClaudeCode, RegistrationIntent.Update, image, tool, logger).Status)
            .IsEqualTo(RegistrationStatus.Registered);
        await Assert.That(log.Logged("Repaired the MCP registration")).IsTrue();

        // Another install's: refused, for an install and an uninstall alike, and kept.
        foreach (var intent in new[] { RegistrationIntent.Install, RegistrationIntent.Uninstall })
        {
            tool.Register("claude-code", theirs);

            var refused = McpRegistrar.Apply(RegistrationClient.ClaudeCode, intent, image, tool, logger);

            await Assert.That(refused.Status).IsEqualTo(RegistrationStatus.Refused);
            await Assert.That(refused.Detail).Contains("Another BrowserAI is registered at");
            await Assert.That(refused.Detail).Contains(theirs);
            await Assert.That(refused.Command).IsEqualTo(theirs);
            await Assert.That(refused.IsWhatWasAskedFor).IsFalse();
            await Assert.That(tool.UserEntry("claude-code")).IsEqualTo(theirs);
        }

        // A configuration nobody could read: refused, never read as empty.
        var blind = new FakeRegisterAi { Unreadable = { "claude-code" } };
        var unreadable = McpRegistrar.Apply(RegistrationClient.ClaudeCode, RegistrationIntent.Install, image, blind, logger);

        await Assert.That(unreadable.Status).IsEqualTo(RegistrationStatus.Refused);
        await Assert.That(unreadable.Detail).Contains("unknown");

        // No client: reported, the install fine, and the line to run by hand.
        var bare = new FakeRegisterAi { Missing = { "claude-code", "codex" } };
        var notFound = McpRegistrar.Apply(RegistrationClient.All, RegistrationIntent.Install, image, bare, logger);

        foreach (var pass in notFound)
        {
            await Assert.That(pass.Report.Status).IsEqualTo(RegistrationStatus.ClientNotFound);
            await Assert.That(pass.Report.IsWhatWasAskedFor).IsTrue();
        }

        await Assert.That(notFound[0].Report.Detail).Contains("claude mcp add browserai --scope user");
        await Assert.That(notFound[1].Report.Detail).Contains("codex mcp add browserai --");
        await Assert.That(log.Records.Any(record => record.EventId.Id is 5 && record.Level is LogLevel.Warning)).IsTrue();

        // A client whose write did not hold: failed, in its own words, with the line.
        var failing = new FakeRegisterAi { Failing = { "claude-code" } };
        var failed = McpRegistrar.Apply(RegistrationClient.ClaudeCode, RegistrationIntent.Install, image, failing, logger);

        await Assert.That(failed.Status).IsEqualTo(RegistrationStatus.Failed);
        await Assert.That(failed.IsWhatWasAskedFor).IsFalse();
        await Assert.That(failed.Detail).Contains("the fake client failed");
        await Assert.That(failed.Detail).Contains("claude mcp add browserai --scope user");
    }

    /// <summary>
    /// A RegisterAI that is missing, hangs, prints something else, speaks another schema
    /// or does not understand its command line fails every client, with the line a
    /// person can run, and never the install.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARegisterAiThatGivesNoAnswerFailsEveryClientAndNeverTheInstall()
    {
        using var install = ScratchDirectory.Create("registerai-no-answer");
        using var data = ScratchDirectory.Create("registerai-no-answer-data");

        var image = InstalledLayout.Create(install.Path);
        var (logger, _) = Capture();

        IRegisterAi[] tools =
        [
            new RegisterAiTool(Path.Combine(install.Path, "current", "payload", "registerai", "RegisterAI.exe")),
            new FakeRegisterAi { Answer = new ToolRun(null, string.Empty, string.Empty, TimedOut: true, null) },
            new FakeRegisterAi { Answer = new ToolRun(0, "not a document", "a warning", TimedOut: false, null) },
            new FakeRegisterAi { Answer = new ToolRun(0, """{"tool":"registerai","schema":2,"exitCode":0,"results":[]}""", string.Empty, TimedOut: false, null) },
            new FakeRegisterAi { Answer = new ToolRun(2, """{"tool":"registerai","schema":1,"verb":"register","exitCode":2,"error":"--frob is not an option.","results":[]}""", string.Empty, TimedOut: false, null) },
        ];

        string[] reasons = ["is not there", "did not finish in time", "printed no document BrowserAI could read", "writes schema 2", "did not understand"];

        for (var index = 0; index < tools.Length; index++)
        {
            var passes = McpRegistrar.Apply(RegistrationClient.All, RegistrationIntent.Install, image, tools[index], logger);

            await Assert.That(passes.Count).IsEqualTo(RegistrationClient.All.Count);

            foreach (var pass in passes)
            {
                await Assert.That(pass.Report.Status).IsEqualTo(RegistrationStatus.Failed);
                await Assert.That(pass.Report.Detail).Contains(reasons[index]);
                await Assert.That(pass.Report.Detail).Contains("mcp add browserai");
            }
        }

        // And through the hook, which writes its record and returns.
        var outcome = HookRegistration.Run(
            RegistrationIntent.Install,
            "9.9.9",
            image,
            tools[0],
            new LocalAppDataPaths(data.Path),
            new ScratchUserPath(),
            new ScratchLogonTasks(),
            ScratchLogonTasks.AppId);

        await Assert.That(outcome.IsWhatWasAskedFor).IsFalse();
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(data.Path, RegistrationRecord.FileName))).Contains("\"outcome\": \"Failed\"");
    }

    /// <summary>
    /// An image with no folder of its own gives a RegisterAI path that is not fully
    /// qualified, and such a path is refused before anything starts, so the tool is never
    /// looked for in a working directory, on PATH or beside the host running this code.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-03, when the ordinary gate found the fallback.</b>
    /// <c>UpdateTests.NoProductPathIsResolvedFromAppContextBaseDirectory</c> went red on
    /// <c>Beside</c> falling back to <c>AppContext.BaseDirectory</c>, which is inside
    /// <c>current\</c>. Red against that fallback, which gave a fully qualified path.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnImageWithNoFolderGivesARegisterAiThatIsRefusedBeforeItStarts()
    {
        string?[] images = [null, string.Empty, "BrowserAI.exe"];

        foreach (var image in images)
        {
            var tool = RegisterAiTool.Beside(image);

            await Assert.That(Path.IsPathFullyQualified(tool.Executable)).IsFalse().Because(tool.Executable);

            var run = tool.Run(VersionOnly, McpRegistrar.ToolBudget);

            await Assert.That(run.Failure).IsNotNull();
            await Assert.That(run.Failure!).Contains("is not there");
        }
    }

    /// <summary>
    /// A tool that throws is caught and reported; a path that is not an install is
    /// refused and the tool is never started.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AThrowingToolIsCaughtAndARefusedPathStartsNothing()
    {
        using var install = ScratchDirectory.Create("registerai-throwing");

        var image = InstalledLayout.Create(install.Path);
        var (logger, log) = Capture();

        var thrown = McpRegistrar.Apply(RegistrationClient.ClaudeCode, RegistrationIntent.Install, image, new FakeRegisterAi { Throws = true }, logger);

        await Assert.That(thrown.Status).IsEqualTo(RegistrationStatus.Failed);
        await Assert.That(thrown.Detail).Contains("asked to throw");
        await Assert.That(log.Records.Any(record => record.EventId.Id is 8)).IsTrue();

        var untouched = new FakeRegisterAi();
        var refused = McpRegistrar.Apply(RegistrationClient.ClaudeCode, RegistrationIntent.Install, @"C:\install\BrowserAI.exe", untouched, logger);

        await Assert.That(refused.Status).IsEqualTo(RegistrationStatus.Refused);
        await Assert.That(untouched.Calls).IsEmpty();
        await Assert.That(log.Records.Single(record => record.EventId.Id is 6).Level).IsEqualTo(LogLevel.Error);
    }

    /// <summary>
    /// A project registration gives each client its own spelling of the server, and a
    /// bare name goes out with the folder it has to be found in.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AProjectRegistrationGivesEachClientItsOwnSpelling()
    {
        using var install = ScratchDirectory.Create("registerai-project");
        using var project = ScratchDirectory.Create("registerai-project-repo");

        var image = InstalledLayout.Create(install.Path);
        var server = InstalledLayout.ServerIn(install.Path);
        var tool = new FakeRegisterAi();
        var (logger, _) = Capture();

        var codex = McpRegistrar.ApplyToProject(RegistrationClient.Codex, register: true, project.Path, image, tool, logger);
        var call = tool.Calls.Single();

        await Assert.That(codex.Status).IsEqualTo(RegistrationStatus.Registered);
        await Assert.That(FakeRegisterAi.Command(call)).IsEqualTo(RegistrationTarget.ServerFileName);
        await Assert.That(FakeRegisterAi.Option(call, "--path-folder")).IsEqualTo(Path.GetDirectoryName(server));
        await Assert.That(FakeRegisterAi.Option(call, "--project")).IsEqualTo(project.Path);

        tool.Calls.Clear();

        var claude = McpRegistrar.ApplyToProject(RegistrationClient.ClaudeCode, register: true, project.Path, image, tool, logger);
        var claudeCall = tool.Calls.Single();

        await Assert.That(claude.Status).IsEqualTo(RegistrationStatus.Registered);
        await Assert.That(FakeRegisterAi.Command(claudeCall)).IsEqualTo(RegistrationClient.ClaudeCode.ProjectCommandFor(server, install.Path).Command);
        await Assert.That(FakeRegisterAi.Option(claudeCall, "--path-folder")).IsNull();
        await Assert.That(claude.Detail).Contains(".mcp.json");
    }

    /// <summary>
    /// The state the window and the report show: one status run for every client's user
    /// scope, one per client that has a project file at or above the working folder, and
    /// each answer read as BrowserAI names its states.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheStateIsReadWithOneRunAndOneMorePerProject()
    {
        using var install = ScratchDirectory.Create("registerai-state");
        using var repo = ScratchDirectory.Create("registerai-state-repo");

        _ = InstalledLayout.Create(install.Path);

        var server = InstalledLayout.ServerIn(install.Path);
        var deep = Directory.CreateDirectory(Path.Combine(repo.Path, "src", "deep")).FullName;
        var tool = new FakeRegisterAi { Missing = { "codex" } };

        await File.WriteAllTextAsync(RegistrationClient.ClaudeCode.ProjectFileIn(repo.Path), "{}");

        tool.Register("claude-code", server);
        tool.RegisterIn("claude-code", repo.Path, server);

        var readings = RegistrationReader.Read(tool, RegistrationClient.All, install.Path, server, deep);

        await Assert.That(tool.Verbs).IsEquivalentTo(["status", "status"]);
        await Assert.That(FakeRegisterAi.Option(tool.Calls[0], "--client")).IsEqualTo("all");
        await Assert.That(FakeRegisterAi.Option(tool.Calls[1], "--project")).IsEqualTo(repo.Path);

        var claude = readings[0];

        await Assert.That(claude.ClientPath).IsNotNull();
        await Assert.That(claude.UserScope.Ownership).IsEqualTo(RegistrationOwnership.OursAndPresent);
        await Assert.That(claude.ProjectDirectory).IsEqualTo(repo.Path);
        await Assert.That(claude.ProjectScope!.Ownership).IsEqualTo(RegistrationOwnership.OursAndPresent);

        // Codex was not found: no client, nothing read as registered, and no project run.
        var codex = readings[1];

        await Assert.That(codex.ClientPath).IsNull();
        await Assert.That(codex.UserScope.Ownership).IsEqualTo(RegistrationOwnership.Absent);
        await Assert.That(codex.Unanswered).IsNull();
        await Assert.That(ClientState.From(codex, serverComposed: true).StatusSentence()).Contains("was not found on this machine");
    }

    /// <summary>
    /// A RegisterAI that cannot be asked leaves every client unknown: the sentence says
    /// why, the short form says unknown, and no action is offered.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARegisterAiThatCannotBeAskedLeavesEveryClientUnknownAndOffersNothing()
    {
        using var install = ScratchDirectory.Create("registerai-unknown");

        _ = InstalledLayout.Create(install.Path);

        var missing = new RegisterAiTool(Path.Combine(install.Path, "current", "payload", "registerai", "RegisterAI.exe"));
        var readings = RegistrationReader.Read(missing, RegistrationClient.All, install.Path, InstalledLayout.ServerIn(install.Path), install.Path);

        foreach (var reading in readings)
        {
            var state = ClientState.From(reading, serverComposed: true);

            await Assert.That(reading.Unanswered!).Contains("is not there");
            await Assert.That(state.StatusSentence()).Contains("could not ask RegisterAI");
            await Assert.That(state.StatusSentence()).DoesNotContain("was not found on this machine");
            await Assert.That(state.ShortStatus).IsEqualTo("unknown");
            await Assert.That(state.MayRegister).IsFalse();
            await Assert.That(state.MayUnregister).IsFalse();
            await Assert.That(state.MayRegisterInProject).IsFalse();
        }
    }

    /// <summary>
    /// Q350 a: the RegisterAI the payload carries writes the schema this build reads,
    /// with every key the reader takes out of a document, and a real status document
    /// reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The maintainer's decision, verbatim: <i>"Q350 a"</i></b> -- a full row in the
    /// drift check and the review file, and one test that fails when the tool's output
    /// schema is not the expected one. A new RegisterAI that renames a key or a word
    /// carries a new schema number, and this is where that becomes a red build and not
    /// a registration that silently reads nothing.
    /// </para>
    /// <para>
    /// <b>The status run reads a scratch configuration.</b> Claude Code's entry is read
    /// from <c>CLAUDE_CONFIG_DIR</c>, set for that one child, and Codex is not asked.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePayloadsRegisterAiWritesTheSchemaThisReaderReads()
    {
        SuiteEnvironment.RequireRepositoryPayload();

        var exe = RepositoryPayload.RegisterAi;

        await Assert.That(File.Exists(exe))
            .IsTrue()
            .Because($"the payload holds no '{exe}'. Run: pwsh -File build/Get-RegisterAi.ps1, or build/Build-Payload.ps1");

        var described = await RunAsync(exe, ["describe"], null);

        using var describe = JsonDocument.Parse(described.Output);

        var root = describe.RootElement;
        var output = root.GetProperty("output");
        var definitions = output.GetProperty("$defs");

        await Assert.That(root.GetProperty("schema").GetInt32()).IsEqualTo(ToolDocuments.Schema);
        await Assert.That(output.GetProperty("properties").GetProperty("schema").GetProperty("const").GetInt32()).IsEqualTo(ToolDocuments.Schema);

        var envelope = Required(output);
        var place = Required(definitions.GetProperty("place"));
        var status = place.Concat(Required(definitions.GetProperty("statusResult"))).ToList();
        var change = place.Concat(Required(definitions.GetProperty("changeResult"))).ToList();
        var entry = Required(definitions.GetProperty("entry"));
        var advice = Required(definitions.GetProperty("advice"));

        await Assert.That(string.Join(", ", EnvelopeKeys.Except(envelope))).IsEmpty();
        await Assert.That(string.Join(", ", ToolDocuments.StatusResultKeys.Except(status))).IsEmpty();
        await Assert.That(string.Join(", ", ToolDocuments.ChangeResultKeys.Except(change))).IsEmpty();
        await Assert.That(string.Join(", ", EntryKeys.Except(entry))).IsEmpty();
        await Assert.That(string.Join(", ", AdviceKeys.Except(advice))).IsEmpty();

        // The words the adapter matches on are the ones RegisterAI publishes.
        var actions = root.GetProperty("actions").EnumerateArray().Select(word => word.GetProperty("name").GetString()).ToList();
        var states = root.GetProperty("states").EnumerateArray().Select(word => word.GetProperty("name").GetString()).ToList();

        await Assert.That(string.Join(", ", ActionWords.Except(actions))).IsEmpty();
        await Assert.That(string.Join(", ", StateWords.Except(states))).IsEmpty();

        // A real document reads.
        using var scratch = ScratchDirectory.Create("registerai-schema");

        var run = await RunAsync(
            exe,
            ["status", "--name", McpRegistrar.ServerName, "--client", "claude-code", "--timeout", "10"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CLAUDE_CONFIG_DIR"] = scratch.Path,
                ["CODEX_HOME"] = Directory.CreateDirectory(Path.Combine(scratch.Path, "codex")).FullName,
            });

        await Assert.That(ToolDocuments.TryRead(run, out var document, out var problem)).IsTrue().Because(problem);
        await Assert.That(document!.For(RegistrationClient.ClaudeCode.ToolId)!.Before.State).IsEqualTo("absent");

        // The control: the same document under another schema number is refused.
        var bumped = run with { Output = run.Output.Replace("\"schema\": 1", "\"schema\": 2", StringComparison.Ordinal) };

        await Assert.That(bumped.Output).IsNotEqualTo(run.Output);
        await Assert.That(ToolDocuments.TryRead(bumped, out _, out var refusal)).IsFalse();
        await Assert.That(refusal).Contains("schema 2");
    }

    private static List<string> Required(JsonElement schema) =>
        [.. schema.GetProperty("required").EnumerateArray().Select(name => name.GetString()!)];

    private static (ILogger Logger, CapturingLoggerProvider Log) Capture()
    {
        var provider = new CapturingLoggerProvider();
        return (provider.CreateLogger("BrowserAI.Registration"), provider);
    }

    /// <summary>Runs the payload's tool once, with no window, reading both pipes as UTF-8.</summary>
    private static async Task<ToolRun> RunAsync(string exe, string[] arguments, IReadOnlyDictionary<string, string>? environment)
    {
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"'{exe}' did not start.");

        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync().WaitAsync(TestDefaults.ProcessHang);

        return new ToolRun(process.ExitCode, await output, await error, TimedOut: false, null);
    }
}
