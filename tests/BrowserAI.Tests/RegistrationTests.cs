// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Registration;
using BrowserAI.Runtime;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Tests;

/// <summary>
/// The charter's founding sentence, which nothing implemented until 2026-08-16:
/// <i>"registered once at system or user scope, available in every repository,
/// with no per-repo files"</i>
/// ([DECISIONS](../../DECISIONS.md#locking-logging-versioning-and-registration)).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two layers, and the split is the whole design.</b> Everything above
/// <see cref="IRegisterAi"/> runs against <see cref="FakeRegisterAi"/>, because a
/// Velopack fast-exit hook is a context no test host can enter -- the same reason the
/// update lane is driven through <c>IUpdateClient</c>. What only the real tool and the
/// real clients can answer is asked of them, in the same run, against a
/// <b>scratch configuration directory</b>.
/// </para>
/// <para>
/// <b>Since 2026-10-03 the registering is RegisterAI's</b>, and its own suite holds what
/// it does against each client: the wording, the exit codes, the readers and the
/// search. <i>Previously the layer below the seam was each client's own command line,
/// and this class held the decisions above it against a double of each client; those
/// arms moved to RegisterAI with the code, and <see cref="RegisterAiTests"/> holds
/// what BrowserAI makes of RegisterAI's answers.</i>
/// </para>
/// <para>
/// ⚠️ <b>Nothing here may touch the maintainer's own MCP configuration, and that
/// is asserted, not intended.</b> The real-client arms point
/// <c>CLAUDE_CONFIG_DIR</c> at a scratch directory under <c>.work\</c> and then
/// prove the negative: the user's own configuration file does not contain the
/// path this test registered, and that path carries a GUID, so it cannot be
/// there by coincidence.
/// </para>
/// <para>
/// ⚠️ <b><c>[NotInParallel]</c> with no key, on the class, which in TUnit means
/// these arms run beside nothing at all.</b> <i>Corrected 2026-09-15 (previously
/// a <c>ClientGroup</c> key on the two arms that open a scope: "they both write
/// one process-wide environment variable -- <c>CLAUDE_CONFIG_DIR</c> -- and
/// then start a process that reads it. Two at once would each register into the
/// other's scratch directory ... every member writes the same variable and
/// nothing else in the suite starts the client".)</i> Every sentence of that was
/// true and the conclusion did not follow: a key holds an arm apart from the
/// arms carrying the <b>same key</b>, and a process-wide variable is read by
/// <b>every child any arm in the suite starts</b> -- none of which holds a key,
/// and none of which can be enumerated. The measured failure is
/// <see cref="RealInstallerTests"/>' -- the sibling member of the old group,
/// whose <c>BROWSERAI_ROOT</c> reached three unrelated arms' browsers. The same
/// hazard is here: a <c>claude</c> CLI started by anything else during the
/// window would read this scratch configuration directory. <b>The class carries
/// it and not the two arms</b>, so an arm added later inherits the rule
/// instead of having to remember it, and
/// <see cref="HouseRuleTests.EveryArmInAFileThatOverridesTheEnvironmentRunsBesideNothing"/>
/// fails the build if this file ever loses it.
/// </para>
/// </remarks>
[NotInParallel]
internal sealed class RegistrationTests
{
    /// <summary>
    /// The client's configuration-directory override, which is what makes a real
    /// registration testable without writing into the user's own file.
    /// </summary>
    internal const string ConfigDirectoryVariable = "CLAUDE_CONFIG_DIR";

    /// <summary>
    /// The variable that moves Codex's configuration, which is what makes a real
    /// Codex registration testable without writing into the user's own.
    /// </summary>
    /// <remarks>
    /// <i>Here since 2026-10-03; it was <c>CodexRegistration.HomeVariable</c>, which
    /// went with the switch to RegisterAI.</i>
    /// </remarks>
    internal const string CodexHomeVariable = "CODEX_HOME";

    /// <summary>The file the client keeps its user-scoped configuration in.</summary>
    private const string ConfigFileName = ".claude.json";

    // ---- What is registered: never the execution stub -----------------------

    /// <summary>
    /// The stub is refused and the binary inside <c>current\</c> is taken,
    /// decided from the path alone.
    /// </summary>
    /// <remarks>
    /// §G landmine 3. The stub is <b>392,704 bytes</b> beside a
    /// <b>17,853,952-byte</b> binary, is compiled as a Windows-subsystem
    /// executable and <b>exits in 59 ms</b> while the app runs on -- so a client
    /// registered against it watches its MCP server die at the handshake, every
    /// time, with nothing in any log to say why.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheExecutionStubIsRefusedAndTheBinaryInsideCurrentIsRegistered()
    {
        using var install = ScratchDirectory.Create("registration-stub");

        var root = install.Path;
        var app = InstalledLayout.Create(root);

        await Assert.That(RegistrationTarget.TryResolve(app, out var target, out _)).IsTrue();
        await Assert.That(target!.Command).IsEqualTo(InstalledLayout.ServerIn(root));
        await Assert.That(target.InstallRoot).IsEqualTo(root);

        // The stub, which sits directly under the root. It is refused on the
        // SHAPE of its path, before anything on disk is opened, which is why
        // this arm never has to write one.
        await Assert.That(RegistrationTarget.TryResolve(
            Path.Combine(root, RegistrationTarget.AppFileName), out var stub, out var refusal)).IsFalse();
        await Assert.That(stub).IsNull();
        await Assert.That(refusal).Contains("current");
        await Assert.That(refusal).Contains("59 ms");
    }

    /// <summary>
    /// The sibling server is what a client is given, and the app that composed
    /// it is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The guarantee this replaces was stronger and is gone, 2026-09-15
    /// (previously: "BrowserAI registers the executable it is itself running
    /// from ... the registered path and the running binary cannot disagree: they
    /// are the same string").</b> The hooks run on the Velopack main exe, and
    /// from this day the main exe is the configuration app. So the path handed
    /// to a client is <b>composed</b> -- the running image's directory plus the
    /// server's file name -- and a composed path is a guess until something
    /// checks it.
    /// </para>
    /// <para>
    /// <b>What replaces it is two checks and a refusal</b>: the file must be
    /// there, and it must declare the console subsystem. Registering the app
    /// under the server's name would put a window on the screen every time a
    /// client opened a session, and the client would then wait forever for a
    /// handshake from a process that is showing a dialog.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSiblingServerIsRegisteredRatherThanTheAppThatComposedIt()
    {
        using var install = ScratchDirectory.Create("registration-sibling");

        var app = InstalledLayout.Create(install.Path);

        await Assert.That(RegistrationTarget.TryResolve(app, out var target, out var refusal)).IsTrue();
        await Assert.That(refusal).IsEmpty();
        await Assert.That(target!.Command).IsEqualTo(InstalledLayout.ServerIn(install.Path));
        await Assert.That(target.Command).IsNotEqualTo(app);
        await Assert.That(target.InstallRoot).IsEqualTo(install.Path);
    }

    /// <summary>
    /// A <c>current\</c> with no server in it registers nothing and says which
    /// file it went looking for.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnInstallWithNoServerBesideTheAppIsRefusedByName()
    {
        using var install = ScratchDirectory.Create("registration-no-server");

        var app = InstalledLayout.CreateWithoutTheServer(install.Path);

        await Assert.That(RegistrationTarget.TryResolve(app, out var target, out var refusal)).IsFalse();
        await Assert.That(target).IsNull();
        await Assert.That(refusal).Contains(RegistrationTarget.ServerFileName);
        await Assert.That(refusal).Contains(InstalledLayout.ServerIn(install.Path));
    }

    /// <summary>
    /// A file wearing the server's name that is not a console binary is refused,
    /// and so is one that is not an executable at all.
    /// </summary>
    /// <remarks>
    /// <b>The name is not the check and cannot be.</b> Both arms here put a file
    /// at exactly the path the composition produces; what separates them from
    /// the passing case is a field the linker writes, which is why
    /// <see cref="PeSubsystem"/> reads it instead of trusting the extension.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFileAtTheServersNameThatIsNotAConsoleBinaryIsRefused()
    {
        using var install = ScratchDirectory.Create("registration-wrong-subsystem");

        var app = InstalledLayout.CreateWithoutTheServer(install.Path);
        var server = InstalledLayout.ServerIn(install.Path);

        // A second copy of the configuration app, renamed.
        InstalledLayout.WritePortableExecutable(server, PeSubsystem.WindowsGui);

        await Assert.That(RegistrationTarget.TryResolve(app, out var windows, out var windowsRefusal)).IsFalse();
        await Assert.That(windows).IsNull();
        await Assert.That(windowsRefusal).Contains("Windows-subsystem");

        InstalledLayout.WriteSomethingThatIsNotAnExecutable(server);

        await Assert.That(RegistrationTarget.TryResolve(app, out var garbage, out var garbageRefusal)).IsFalse();
        await Assert.That(garbage).IsNull();
        await Assert.That(garbageRefusal).Contains("no readable PE header");
    }

    /// <summary>A path that cannot be resolved is refused and not guessed at.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APathThatIsNotAnInstalledBrowserAiIsRefused()
    {
        await Assert.That(RegistrationTarget.TryResolve(null, out _, out var noPath)).IsFalse();
        await Assert.That(noPath).Contains("no path");

        await Assert.That(RegistrationTarget.TryResolve(@"current\BrowserAI.exe", out _, out var relative)).IsFalse();
        await Assert.That(relative).Contains("fully qualified");

        // Case is not what decides it: the directory is `current` either way.
        using var shoutingInstall = ScratchDirectory.Create("registration-shouting");
        var shoutingCurrent = Directory.CreateDirectory(Path.Combine(shoutingInstall.Path, "CURRENT"));

        InstalledLayout.WritePortableExecutable(
            Path.Combine(shoutingCurrent.FullName, RegistrationTarget.AppFileName), PeSubsystem.WindowsGui);
        InstalledLayout.WritePortableExecutable(
            Path.Combine(shoutingCurrent.FullName, RegistrationTarget.ServerFileName), PeSubsystem.WindowsCui);

        await Assert.That(RegistrationTarget.TryResolve(
            Path.Combine(shoutingCurrent.FullName, RegistrationTarget.AppFileName), out var shouting, out _)).IsTrue();
        await Assert.That(shouting!.InstallRoot).IsEqualTo(shoutingInstall.Path);
    }

    // ---- What a project file is given -----------------------------------------

    /// <summary>
    /// The portable form is what a project file gets, and it expands to the
    /// install it was written for.
    /// </summary>
    /// <remarks>
    /// <b>An absolute path under one person's profile is wrong on every
    /// teammate's machine</b>, which makes committing one worse than committing
    /// nothing. The expansion is asserted against this machine's own
    /// <c>%LOCALAPPDATA%</c> so that the form cannot drift from what the client
    /// would resolve.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheProjectScopeCommandIsPortableAndExpandsToTheDefaultInstall()
    {
        const string PackId = "BrowserAI.app";

        var portable = RegistrationClient.PortableCommandFor(PackId);

        await Assert.That(portable).StartsWith("${LOCALAPPDATA}/");
        await Assert.That(portable).EndsWith(RegistrationTarget.ServerFileName);
        await Assert.That(portable).DoesNotContain(BackslashText);

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            PackId,
            RegistrationTarget.CurrentDirectoryName,
            RegistrationTarget.ServerFileName);

        // Claude Code's project file gets the portable spelling for the install at
        // its default place, and the absolute path, with the reason, anywhere else.
        var atHome = RegistrationClient.ClaudeProjectCommandFor(expected, Path.GetDirectoryName(Path.GetDirectoryName(expected)));

        await Assert.That(atHome.Command).IsEqualTo(portable);
        await Assert.That(atHome.Note).IsNull();

        var moved = RegistrationClient.ClaudeProjectCommandFor(@"D:\elsewhere\current\BrowserAI.Server.exe", @"D:\elsewhere");

        await Assert.That(moved.Command).IsEqualTo(@"D:\elsewhere\current\BrowserAI.Server.exe");
        await Assert.That(moved.Note!).Contains("not at its default location");
    }

    /// <summary>A single backslash, spelled once.</summary>
    private const string BackslashText = "\\";

    // ---- The state a person can find ---------------------------------------

    /// <summary>
    /// The hook writes its outcome where a person looking for it will find it:
    /// in the <b>data</b> root, and nothing at all under the install root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Re-pointed 2026-09-15 (previously "beside <c>current\</c> rather
    /// than inside it ... an update replaces that directory wholesale").</b> The
    /// right rule, aimed one level too low. A sibling of <c>current\</c> is still
    /// inside the install root, and the two events a person reads this file
    /// after are the two that empty it: a repair install, which renames the root
    /// aside and deletes it, and an uninstall. The assertion is the stronger one
    /// now -- the record and the log are not under the install root at all, and
    /// the hook leaves that root untouched.
    /// </para>
    /// <para>
    /// <b>The data root is a scratch directory here, and it has to be.</b> The
    /// product resolves a constant under <c>%LocalAppData%</c>; a test that let
    /// it do so would write a registration record into the developer's own data
    /// root -- which is why the overload the suite drives takes the seam instead
    /// of defaulting it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheHookWritesItsOutcomeIntoTheDataRootAndNothingUnderTheInstallRoot()
    {
        using var install = ScratchDirectory.Create("registration-hook");
        using var data = ScratchDirectory.Create("registration-hook-data");


        var command = InstalledLayout.Create(install.Path);
        var tool = new FakeRegisterAi();

        var before = Directory
            .EnumerateFileSystemEntries(install.Path, "*", SearchOption.AllDirectories)
            .Select(entry => Path.GetRelativePath(install.Path, entry))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var report = HookRegistration.Run(
            RegistrationIntent.Install,
            "9.9.9",
            command,
            tool,
            new LocalAppDataPaths(data.Path),
            new ScratchUserPath(),
            new ScratchLogonTasks(),
            ScratchLogonTasks.AppId).For(RegistrationClient.ClaudeCode.Key);

        await Assert.That(report.Status).IsEqualTo(RegistrationStatus.Registered);

        var record = Path.Combine(data.Path, RegistrationRecord.FileName);

        await Assert.That(File.Exists(record)).IsTrue();
        await Assert.That(record.Contains(@"\current\", StringComparison.OrdinalIgnoreCase)).IsFalse();

        var written = await File.ReadAllTextAsync(record);

        await Assert.That(written).Contains("\"outcome\": \"Registered\"");
        await Assert.That(written).Contains("\"scope\": \"user\"");
        await Assert.That(written).Contains("\"browserAiVersion\": \"9.9.9\"");
        await Assert.That(written).Contains("\"isWhatWasAskedFor\": true");

        // The hook's own log is written inside the hook, because VelopackApp.Run
        // exits the process when it has served one -- anything merely buffered
        // is discarded at that exit.
        var logs = Directory.EnumerateFiles(Path.Combine(data.Path, "logs"), "*.log").ToList();

        await Assert.That(logs).IsNotEmpty();

        var text = string.Join("\n", logs.Select(ReadShared));

        await Assert.That(text).Contains("Velopack Install hook running for BrowserAI 9.9.9");
        await Assert.That(text).Contains($"Registered '{McpRegistrar.ServerName}'");

        // ⚠️ THE HALF THAT IS RED AGAINST THE OLD LAYOUT: the install root the
        // image path names is a directory Setup.exe renames aside and deletes,
        // and the hook leaves nothing whatever in it.
        //
        // ⚠️ Narrowed 2026-09-15 (previously the install root was expected to be
        // EMPTY). It is not empty any more and cannot be: the two executables
        // have to be on disk for the composed server path to be checkable at
        // all, so this arm builds a `current\` before it runs. What is asserted
        // is the property that was always meant -- the hook ADDS nothing -- and it
        // is taken as a difference against what was there first, so a file the
        // hook writes anywhere under that root fails it exactly as before.
        var afterwards = Directory
            .EnumerateFileSystemEntries(install.Path, "*", SearchOption.AllDirectories)
            .Select(entry => Path.GetRelativePath(install.Path, entry))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        afterwards.ExceptWith(before);

        await Assert.That(string.Join(", ", afterwards.Order(StringComparer.Ordinal))).IsEmpty();
    }

    /// <summary>
    /// A hook whose registration failed still says so on disk.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFailedRegistrationIsRecordedRatherThanLeavingTheInstallSilent()
    {
        using var install = ScratchDirectory.Create("registration-hook-failed");
        using var data = ScratchDirectory.Create("registration-hook-failed-data");


        var command = InstalledLayout.Create(install.Path);
        var tool = new FakeRegisterAi { Missing = { "claude-code", "codex" } };

        var report = HookRegistration.Run(
            RegistrationIntent.Install,
            "9.9.9",
            command,
            tool,
            new LocalAppDataPaths(data.Path),
            new ScratchUserPath(),
            new ScratchLogonTasks(),
            ScratchLogonTasks.AppId).For(RegistrationClient.ClaudeCode.Key);

        await Assert.That(report.Status).IsEqualTo(RegistrationStatus.ClientNotFound);

        var written = await File.ReadAllTextAsync(Path.Combine(data.Path, RegistrationRecord.FileName));

        await Assert.That(written).Contains("\"outcome\": \"ClientNotFound\"");
        await Assert.That(written).Contains("claude mcp add browserai --scope user");
    }

    // ---- Both clients, one pass each -----------------------------------------

    /// <summary>
    /// One hook registers <b>every</b> client, and the record carries an entry
    /// for each of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q258 step 2, and the maintainer's ask is the reason it is two and not
    /// one:</b> <i>"I want the system level and repo level registration to also
    /// work for codex and not only for claude code."</i> A hook that registered
    /// one client and left the other to a person is the state this closes.
    /// </para>
    /// <para>
    /// ⚠️ <b>The record's shape is the assertion, not the hook's return value.</b>
    /// <c>mcp-registration.json</c> is what a person opens when a client cannot
    /// see BrowserAI, and a file carrying one outcome for two clients cannot say
    /// <i>which</i> one did not happen -- which is the whole reason the entries
    /// are keyed. Both keys are read off <see cref="RegistrationClient.All"/>, so
    /// a third client added later fails this arm until the record carries it.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-09-24</b> by a hook that registered only the first
    /// client (<c>.Take(1)</c> over the client list): the record carried no
    /// <c>codex</c> entry, and the arm failed on exactly that.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task OneHookRegistersEveryClientAndTheRecordCarriesAnEntryForEach()
    {
        using var install = ScratchDirectory.Create("registration-both");
        using var data = ScratchDirectory.Create("registration-both-data");

        var command = InstalledLayout.Create(install.Path);
        var tool = new FakeRegisterAi();

        var outcome = HookRegistration.Run(
            RegistrationIntent.Install,
            "9.9.9",
            command,
            tool,
            new LocalAppDataPaths(data.Path),
            new ScratchUserPath(),
            new ScratchLogonTasks(),
            ScratchLogonTasks.AppId);

        var written = await File.ReadAllTextAsync(Path.Combine(data.Path, RegistrationRecord.FileName));

        // One entry per client, keyed by the product's own keys.
        foreach (var who in RegistrationClient.All)
        {
            await Assert.That(written).Contains($"\"key\": \"{who.Key}\"");
            await Assert.That(written).Contains($"\"displayName\": \"{who.DisplayName}\"");
            await Assert.That(outcome.For(who.Key).Status).IsEqualTo(RegistrationStatus.Registered);
        }

        // ⚠️ AND RegisterAI WAS RUN ONCE FOR BOTH, since 2026-10-03: one register,
        // every client, this install's root as what makes an entry ours, and the
        // budget that bounds the whole pass.
        var call = tool.Calls.Single();

        await Assert.That(call[0]).IsEqualTo("register");
        await Assert.That(FakeRegisterAi.Option(call, "--client")).IsEqualTo("all");
        await Assert.That(FakeRegisterAi.Option(call, "--scope")).IsEqualTo("user");
        await Assert.That(FakeRegisterAi.Option(call, "--owned-root")).IsEqualTo(install.Path);
        await Assert.That(FakeRegisterAi.Option(call, "--timeout")).IsEqualTo("12");
        await Assert.That(FakeRegisterAi.Command(call)).IsEqualTo(InstalledLayout.ServerIn(install.Path));

        // The whole pass is what was asked for, which is the one aggregate the
        // file still carries: two outcomes cannot be summarised by one word, and
        // a boolean is the only honest reduction of them.
        await Assert.That(written).Contains("\"isWhatWasAskedFor\": true");
        await Assert.That(written).Contains("\"schemaVersion\": 2");
    }

    /// <summary>
    /// A client this machine does not have is named in the record with its own
    /// refusal, and the hook still succeeds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The failure this closes is a silent one, and it is the reason the
    /// entries are per client.</b> With one outcome for two clients, a machine
    /// with no Codex would either read as a failed registration -- which it is
    /// not, because Claude Code was registered -- or read as a clean pass, with
    /// nothing anywhere to say that the second client was never reached.
    /// </para>
    /// <para>
    /// <b>The missing client is RegisterAI's answer, since 2026-10-03.</b> Finding a
    /// client is RegisterAI's job now, so the fake says Codex was not found and this
    /// arm holds what BrowserAI makes of that. <i>Previously the client set was
    /// supplied, because Codex's discovery looked in three places below the seam
    /// this repository had then.</i>
    /// </para>
    /// <para>
    /// <b>Planted red 2026-09-24</b> by the same one-client hook: there was no
    /// Codex pass to report at all, and asking for it threw.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AClientThisMachineDoesNotHaveIsNamedInTheRecordAndNeverFailsTheHook()
    {
        using var install = ScratchDirectory.Create("registration-absent");
        using var data = ScratchDirectory.Create("registration-absent-data");

        var command = InstalledLayout.Create(install.Path);
        var tool = new FakeRegisterAi { Missing = { "codex" } };

        var outcome = HookRegistration.Run(
            RegistrationIntent.Install,
            "9.9.9",
            command,
            tool,
            new LocalAppDataPaths(data.Path),
            new ScratchUserPath(),
            new ScratchLogonTasks(),
            ScratchLogonTasks.AppId);

        await Assert.That(outcome.For(RegistrationClient.ClaudeCode.Key).Status).IsEqualTo(RegistrationStatus.Registered);
        await Assert.That(outcome.For(RegistrationClient.Codex.Key).Status).IsEqualTo(RegistrationStatus.ClientNotFound);

        // Never a failed hook: an absent client is a machine that has none, not
        // an install that went wrong.
        await Assert.That(outcome.IsWhatWasAskedFor).IsTrue();

        var written = await File.ReadAllTextAsync(Path.Combine(data.Path, RegistrationRecord.FileName));

        // The named refusal, in the record, naming every place that was looked
        // and the command a person can run instead.
        await Assert.That(written).Contains("\"outcome\": \"ClientNotFound\"");
        await Assert.That(written).Contains("has not registered itself with Codex");
        await Assert.That(written).Contains("codex mcp add browserai --");

        // And the other client's entry is untouched beside it.
        await Assert.That(written).Contains("\"outcome\": \"Registered\"");
    }

    // ---- The data root at uninstall -----------------------------------------

    /// <summary>
    /// A silent uninstall keeps the data root and never puts a window on
    /// anybody's screen.
    /// </summary>
    /// <remarks>
    /// <b>Both halves, and the second is the one that would break a scripted
    /// uninstall.</b> A dialog inside <c>Update.exe --uninstall --silent</c> is a
    /// 60-second stall ending in the hook being killed -- so the assertion is not
    /// only that the directory survived, it is that the question was never
    /// asked at all.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASilentUninstallKeepsTheDataRootAndNeverAsks()
    {
        using var install = ScratchDirectory.Create("uninstall-silent");
        using var data = ScratchDirectory.Create("uninstall-silent-data");


        var paths = new LocalAppDataPaths(data.Path);
        var planted = PlantADataRoot(paths);
        var asked = 0;

        var outcome = HookRegistration.Run(
            RegistrationIntent.Uninstall,
            "9.9.9",
            InstalledLayout.Create(install.Path),
            new FakeRegisterAi(),
            paths,
            new ScratchUserPath(),
            new ScratchLogonTasks(),
            ScratchLogonTasks.AppId,
            silent: true,
            ask: _ =>
            {
                asked++;
                return true;
            });

        // Nothing was registered with the double first, so the registration half
        // reports that there was nothing to remove -- which is still what was
        // asked for, and is the half this arm does not judge.
        await Assert.That(outcome.IsWhatWasAskedFor).IsTrue();
        await Assert.That(asked).IsEqualTo(0);

        await Assert.That(outcome.Disposal).IsNotNull();
        await Assert.That(outcome.Disposal!.Choice).IsEqualTo(DataRootChoice.Keep);
        await Assert.That(outcome.Disposal.Asked).IsFalse();

        await Assert.That(File.Exists(planted)).IsTrue();
        await Assert.That(Directory.Exists(paths.BrowsersDirectory)).IsTrue();
    }

    /// <summary>
    /// The prompt's default is <i>keep</i>: an answer of no leaves everything
    /// exactly where it was.
    /// </summary>
    /// <remarks>
    /// <b>The question is asserted as well as the answer</b>, because a prompt
    /// that did not name the directory or say what it holds is a prompt answered
    /// by habit. The size is the whole of what makes it a real question: saying
    /// yes means a ~768 MB download next time.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnUninstallAnsweredNoKeepsTheDataRootAndTheQuestionNamedIt()
    {
        using var install = ScratchDirectory.Create("uninstall-keep");
        using var data = ScratchDirectory.Create("uninstall-keep-data");


        var paths = new LocalAppDataPaths(data.Path);
        var planted = PlantADataRoot(paths);
        var questions = new List<string>();

        var outcome = HookRegistration.Run(
            RegistrationIntent.Uninstall,
            "9.9.9",
            InstalledLayout.Create(install.Path),
            new FakeRegisterAi(),
            paths,
            new ScratchUserPath(),
            new ScratchLogonTasks(),
            ScratchLogonTasks.AppId,
            silent: false,
            ask: message =>
            {
                questions.Add(message);
                return false;
            });

        await Assert.That(questions.Count).IsEqualTo(1);
        await Assert.That(questions[0]).Contains(data.Path);
        await Assert.That(questions[0]).Contains("KB");
        await Assert.That(questions[0]).Contains("Delete it as well?");

        await Assert.That(outcome.Disposal!.Choice).IsEqualTo(DataRootChoice.Keep);
        await Assert.That(outcome.Disposal.Asked).IsTrue();
        await Assert.That(outcome.Disposal.Bytes).IsGreaterThan(0);

        await Assert.That(File.Exists(planted)).IsTrue();
    }

    /// <summary>
    /// An answer of yes removes the data root, through <c>TreeDelete</c>, with
    /// nothing left behind -- the hook's own open log included.
    /// </summary>
    /// <remarks>
    /// <b>The log is the interesting node and it is why the removal is split
    /// from the decision.</b> The hook writes its process log inside the data
    /// root, so a delete performed while that file was open would leave exactly
    /// one directory standing -- the one holding the record of the decision. The
    /// product closes the log first; this asserts the consequence and not
    /// the arrangement.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnUninstallAnsweredYesRemovesTheWholeDataRootIncludingTheHooksOwnLog()
    {
        using var install = ScratchDirectory.Create("uninstall-remove");
        using var data = ScratchDirectory.Create("uninstall-remove-data");


        var paths = new LocalAppDataPaths(data.Path);
        var planted = PlantADataRoot(paths);

        var outcome = HookRegistration.Run(
            RegistrationIntent.Uninstall,
            "9.9.9",
            InstalledLayout.Create(install.Path),
            new FakeRegisterAi(),
            paths,
            new ScratchUserPath(),
            new ScratchLogonTasks(),
            ScratchLogonTasks.AppId,
            silent: false,
            ask: _ => true);

        await Assert.That(outcome.IsWhatWasAskedFor).IsTrue();
        await Assert.That(outcome.Disposal!.Choice).IsEqualTo(DataRootChoice.Remove);

        await Assert.That(string.Join(Environment.NewLine, outcome.Disposal.Failures)).IsEmpty();
        await Assert.That(File.Exists(planted)).IsFalse();
        await Assert.That(Directory.Exists(paths.LogDirectory)).IsFalse();
        await Assert.That(Directory.Exists(data.Path)).IsFalse();
    }

    /// <summary>
    /// An update never asks about the data root and never touches it -- which is
    /// the founding promise of separating the two directories at all.
    /// </summary>
    /// <remarks>
    /// <b>By construction and not by care, and this is what holds the
    /// construction.</b> Only <see cref="RegistrationIntent.Uninstall"/> reaches
    /// the disposal; an install and an update come back with no disposal report
    /// at all, so there is no path on which a wrong answer could delete
    /// somebody's browsers mid-upgrade.
    /// </remarks>
    /// <param name="intent">The lifecycle event that is not an uninstall.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments(RegistrationIntent.Install)]
    [Arguments(RegistrationIntent.Update)]
    public async Task AnUpgradeNeverAsksAboutTheDataRootAndNeverTouchesIt(RegistrationIntent intent)
    {
        using var install = ScratchDirectory.Create("upgrade-keeps");
        using var data = ScratchDirectory.Create("upgrade-keeps-data");


        var paths = new LocalAppDataPaths(data.Path);
        var planted = PlantADataRoot(paths);
        var asked = 0;

        var outcome = HookRegistration.Run(
            intent,
            "9.9.9",
            InstalledLayout.Create(install.Path),
            new FakeRegisterAi(),
            paths,
            new ScratchUserPath(),
            new ScratchLogonTasks(),
            ScratchLogonTasks.AppId,
            silent: false,
            ask: _ =>
            {
                asked++;
                return true;
            });

        await Assert.That(outcome.Disposal).IsNull();
        await Assert.That(asked).IsEqualTo(0);
        await Assert.That(File.Exists(planted)).IsTrue();
        await Assert.That(Directory.Exists(paths.BrowsersDirectory)).IsTrue();
    }

    /// <summary>
    /// A data root holding nothing but this hook's own log is not worth a
    /// question.
    /// </summary>
    /// <remarks>
    /// <b>The install hook creates the data root itself</b> -- it opens a log
    /// there -- so by the time an uninstall runs, the directory always exists.
    /// Asking about a few kilobytes of log and a registration record, which are
    /// the two files somebody reads <i>after</i> an uninstall, would be a modal
    /// window with nothing in it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnUninstallOfABrowserAiThatNeverRanAsksNothing()
    {
        using var install = ScratchDirectory.Create("uninstall-unused");
        using var data = ScratchDirectory.Create("uninstall-unused-data");


        var paths = new LocalAppDataPaths(data.Path);
        var asked = 0;

        var outcome = HookRegistration.Run(
            RegistrationIntent.Uninstall,
            "9.9.9",
            InstalledLayout.Create(install.Path),
            new FakeRegisterAi(),
            paths,
            new ScratchUserPath(),
            new ScratchLogonTasks(),
            ScratchLogonTasks.AppId,
            silent: false,
            ask: _ =>
            {
                asked++;
                return true;
            });

        await Assert.That(asked).IsEqualTo(0);
        await Assert.That(outcome.Disposal!.Choice).IsEqualTo(DataRootChoice.Keep);
        await Assert.That(outcome.Disposal.Asked).IsFalse();

        // The record survives, which is the point of not asking: it is what a
        // person reads when a client still shows BrowserAI after an uninstall.
        await Assert.That(File.Exists(RegistrationRecord.PathFor(data.Path))).IsTrue();
    }

    /// <summary>
    /// Silence is read off the parent's command line, by token, and an unknown
    /// parent is silent.
    /// </summary>
    /// <remarks>
    /// <b>Velopack passes the hook nothing</b> -- not a flag, not an environment
    /// variable -- so <c>Update.exe</c>'s own command line is the only place the
    /// answer exists. Windows keeps the two shapes apart in the registry:
    /// <c>UninstallString</c> is <c>Update.exe --uninstall</c> and
    /// <c>QuietUninstallString</c> is the same with <c>--silent</c>. The last two
    /// arguments are the controls that matter: a path containing <c>-s</c> is not
    /// the flag, and an unreadable parent must read as silent, because the
    /// consequence of the other answer is a modal window nobody is there to
    /// close.
    /// </remarks>
    /// <param name="commandLine">What the parent was started with.</param>
    /// <param name="silent">What that means.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments(@"""C:\Users\x\AppData\Local\BrowserAI.app\Update.exe"" --uninstall", false)]
    [Arguments(@"""C:\Users\x\AppData\Local\BrowserAI.app\Update.exe"" --uninstall --silent", true)]
    [Arguments(@"""C:\Users\x\AppData\Local\BrowserAI.app\Update.exe"" --uninstall -s", true)]
    [Arguments(@"""C:\Users\x\AppData\Local\BrowserAI.app\Update.exe"" --uninstall --SILENT", true)]
    [Arguments(@"C:\tools\update-server\Update.exe --uninstall", false)]
    [Arguments(null, true)]
    public async Task SilenceIsReadOffTheParentsCommandLineByToken(string? commandLine, bool silent) =>
        await Assert.That(DataRootDisposal.IsSilent(commandLine)).IsEqualTo(silent);

    /// <summary>
    /// Plants what a used BrowserAI leaves behind: a browsers tree, an index and
    /// a file to watch.
    /// </summary>
    /// <param name="paths">The scratch data seam.</param>
    /// <returns>The planted file's path.</returns>
    private static string PlantADataRoot(LocalAppDataPaths paths)
    {
        _ = Directory.CreateDirectory(paths.BrowsersDirectory);
        _ = Directory.CreateDirectory(paths.IndexDirectory);

        var planted = Path.Combine(paths.BrowsersDirectory, "chromium-0000", "chrome.exe");

        _ = Directory.CreateDirectory(Path.GetDirectoryName(planted)!);
        File.WriteAllText(planted, "not really a browser, but it is 768 MB in spirit");

        return planted;
    }

    // ---- The RegisterAI the payload carries, against the real clients ------

    /// <summary>
    /// The RegisterAI the payload carries registers BrowserAI with the real Claude Code
    /// and the real Codex, leaves a matching entry alone on an update and on a second
    /// install, removes both on an uninstall, and touches nothing of the person's own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-10-03 with the switch to RegisterAI (Q332, Q349 a).</b> What
    /// RegisterAI does against each client is held by its own suite; what this holds is
    /// BrowserAI's half, end to end: the command line the registrar builds, run by the
    /// file the build put in the payload, against both real clients, read back by
    /// BrowserAI.
    /// </para>
    /// <para>
    /// <b>Both clients point at scratch, and the class runs beside nothing</b>, for the
    /// reason on the class: <c>CLAUDE_CONFIG_DIR</c> and <c>CODEX_HOME</c> are
    /// process-wide, and RegisterAI and every client it starts inherit them.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePayloadsRegisterAiRegistersBrowserAiWithBothRealClients()
    {
        _ = SuiteEnvironment.RequireClientCommandLine();
        _ = SuiteEnvironment.RequireCodexCommandLine();
        SuiteEnvironment.RequireRepositoryPayload();

        await Assert.That(File.Exists(RepositoryPayload.RegisterAi)).IsTrue().Because($"the payload holds no '{RepositoryPayload.RegisterAi}'. Run: pwsh -File build/Get-RegisterAi.ps1");

        using var config = ScratchDirectory.Create("registerai-live");
        using var install = ScratchDirectory.Create("registerai-live-install");

        var image = InstalledLayout.Create(install.Path);
        var server = InstalledLayout.ServerIn(install.Path);
        var tool = new RegisterAiTool(RepositoryPayload.RegisterAi);
        var (logger, _) = Capture();

        using (PointTheClientAt(config.Path))
        {
            var claudeFile = Path.Combine(config.Path, ConfigFileName);
            var codexFile = Path.Combine(Environment.GetEnvironmentVariable(CodexHomeVariable)!, "config.toml");

            foreach (var pass in McpRegistrar.Apply(RegistrationClient.All, RegistrationIntent.Install, image, tool, logger))
            {
                await Assert.That(pass.Report.Status).IsEqualTo(RegistrationStatus.Registered).Because(pass.Report.Detail);
            }

            await Assert.That(await File.ReadAllTextAsync(claudeFile)).Contains(server.Replace(@"\", @"\\", StringComparison.Ordinal));
            await Assert.That(await File.ReadAllTextAsync(codexFile)).Contains("[mcp_servers.browserai]");

            // Q347 a, against the real clients: an update and a second install leave
            // an entry of ours that already names this server exactly as it is.
            foreach (var intent in new[] { RegistrationIntent.Update, RegistrationIntent.Install })
            {
                foreach (var pass in McpRegistrar.Apply(RegistrationClient.All, intent, image, tool, logger))
                {
                    await Assert.That(pass.Report.Status).IsEqualTo(RegistrationStatus.AlreadyRegistered).Because(pass.Report.Detail);
                }
            }

            await Assert.That(Occurrences(await File.ReadAllTextAsync(claudeFile), $"\"{McpRegistrar.ServerName}\"")).IsEqualTo(1);
            await Assert.That(Occurrences(await File.ReadAllTextAsync(codexFile), "[mcp_servers.browserai]")).IsEqualTo(1);

            foreach (var pass in McpRegistrar.Apply(RegistrationClient.All, RegistrationIntent.Uninstall, image, tool, logger))
            {
                await Assert.That(pass.Report.Status).IsEqualTo(RegistrationStatus.Unregistered).Because(pass.Report.Detail);
            }

            await Assert.That(await File.ReadAllTextAsync(claudeFile)).DoesNotContain($"\"{McpRegistrar.ServerName}\"");
            await Assert.That(await File.ReadAllTextAsync(codexFile)).DoesNotContain("[mcp_servers.browserai]");

            foreach (var pass in McpRegistrar.Apply(RegistrationClient.All, RegistrationIntent.Uninstall, image, tool, logger))
            {
                await Assert.That(pass.Report.Status).IsEqualTo(RegistrationStatus.NothingToUnregister).Because(pass.Report.Detail);
            }
        }

        // ⚠️ The negative that matters: the registered path carries this run's GUID,
        // so its absence from the person's own configuration is proof and not an
        // argument.
        var mine = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify),
            ConfigFileName);

        if (File.Exists(mine))
        {
            await Assert.That(await File.ReadAllTextAsync(mine)).DoesNotContain(install.Path.Replace(@"\", @"\\", StringComparison.Ordinal));
        }

        await Assert.That(TheMaintainersOwnCodexConfiguration()).DoesNotContain(install.Path);
    }

    // ---- The scratch configuration the real clients are pointed at -----------

    /// <summary>
    /// The scratch configuration a real-client arm hands over already carries
    /// what the client reads as "onboarding is finished".
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The behavioural half of the onboarding guard.</b>
    /// <see cref="HouseRuleTests.EveryScratchClientConfigurationIsSeededAsOnboardedBeforeTheClientRuns"/>
    /// holds that every site goes through the seam; this holds that the seam
    /// writes the file the client would read, in the directory the client would
    /// read it from, <b>before</b> the scope is anything a child could inherit.
    /// It runs on every build and needs no client, because what it asserts is a
    /// file on disk and not a behaviour of somebody else's binary.
    /// </para>
    /// <para>
    /// ⚠️ <b>Asserted, not measured against the flow.</b> Nothing here shows that
    /// an unseeded directory would have opened a sign-in window -- establishing
    /// that means running the flow on the maintainer's desktop, which is the
    /// event being guarded against. The reasoning, the bundle source it was read
    /// out of and the absence of any non-interactive signal to set instead are on
    /// <see cref="OnboardedClientConfig"/>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheScratchConfigurationIsSeededWithWhatTheClientReadsAsOnboarded()
    {
        using var config = ScratchDirectory.Create("registration-onboarded");

        // A fresh scratch directory is empty, which is the state being guarded
        // against: to the client that is a machine nobody has ever signed in on.
        await Assert.That(Directory.EnumerateFileSystemEntries(config.Path)).IsEmpty();

        using (PointTheClientAt(config.Path))
        {
            var seeded = Path.Combine(config.Path, OnboardedClientConfig.FileName);

            await Assert.That(File.Exists(seeded)).IsTrue();

            var written = await File.ReadAllTextAsync(seeded);

            await Assert.That(written).Contains($"\"{OnboardedClientConfig.Marker}\":true");

            // The client prefers `.config.json` in the same directory when it
            // exists, and a scratch directory holds none -- so the file above is
            // the one it resolves. Read out of the bundle, quoted on
            // OnboardedClientConfig.
            await Assert.That(File.Exists(Path.Combine(config.Path, ".config.json"))).IsFalse();

            // The variable really is pointing at that directory while the scope
            // is open, which is what makes the seeding reachable at all.
            await Assert.That(Environment.GetEnvironmentVariable(ConfigDirectoryVariable))
                .IsEqualTo(config.Path);
        }

        // And the file name the rest of this class uses is the same one.
        await Assert.That(ConfigFileName).IsEqualTo(OnboardedClientConfig.FileName);
    }

    /// <summary>
    /// The maintainer's own Codex configuration, or nothing when there is none.
    /// </summary>
    /// <remarks>
    /// <b>Read, never written, and read only to prove an absence.</b> The path
    /// is the default home's, composed from the profile and never from
    /// <c>CODEX_HOME</c>, which is exactly what these arms move.
    /// </remarks>
    /// <returns>Its text.</returns>
    private static string TheMaintainersOwnCodexConfiguration()
    {
        var file = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify),
            ".codex",
            "config.toml");

        return File.Exists(file) ? File.ReadAllText(file) : string.Empty;
    }

    // ---- Helpers ------------------------------------------------------------

    /// <summary>
    /// Reads a log file the way everything else in this suite does -- sharing
    /// with a writer that may still hold it.
    /// </summary>
    /// <remarks>
    /// The process log is machine-wide, so any BrowserAI on this machine may
    /// have it open; <c>File.ReadAllText</c> asks for <c>FileShare.Read</c>,
    /// which denies write to an existing writer and is refused.
    /// </remarks>
    /// <param name="path">The log file.</param>
    /// <returns>Its text.</returns>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private static (ILogger Logger, CapturingLoggerProvider Log) Capture()
    {
        var provider = new CapturingLoggerProvider();
        return (provider.CreateLogger("BrowserAI.Registration"), provider);
    }

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        var at = text.IndexOf(needle, StringComparison.Ordinal);

        while (at >= 0)
        {
            count++;
            at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    /// <summary>
    /// Points the client at a scratch configuration directory for the life of
    /// the returned scope.
    /// </summary>
    /// <remarks>
    /// <b>Process-wide, because that is the only channel there is.</b> The child
    /// inherits this process's environment block, and the alternative -- an
    /// environment overlay on the product's own command runner -- would be a seam
    /// that exists for no reason but this test. It is restored however the test
    /// ends, and the whole class is <c>[NotInParallel]</c> with no key --
    /// <i>corrected 2026-09-15 (previously "The arms that use it are
    /// <c>[NotInParallel]</c> on one group so they cannot overwrite each other's
    /// value")</i>, because overwriting each other's value was never the only
    /// way this goes wrong: any child of any arm reads it too.
    /// </remarks>
    /// <remarks>
    /// ⚠️ <b>The directory is SEEDED as already onboarded on the way through, and
    /// that is what makes the seeding inseparable from the use</b> -- an empty
    /// configuration directory is, to the real client, a machine nobody has ever
    /// signed in on. See <see cref="OnboardedClientConfig"/> for what is written
    /// and the bundle source it was read out of, and
    /// <see cref="HouseRuleTests.EveryScratchClientConfigurationIsSeededAsOnboardedBeforeTheClientRuns"/>
    /// for the scan that refuses a site which skips it.
    /// </remarks>
    /// <remarks>
    /// ⚠️ <b>BOTH CLIENTS SINCE 2026-09-24, AND THE SECOND ONE IS THE DANGEROUS
    /// HALF.</b> The hooks register Codex as well now, and Codex's scope IS
    /// <c>CODEX_HOME</c>: a hook arm that left it alone would run the real
    /// <c>codex.exe</c> against the maintainer's own <c>~\.codex</c> and, finding
    /// no entry there, WRITE one. Claude Code's override only has to stop a
    /// foreign-entry refusal; this one stops a write into somebody's
    /// configuration. The directory is created because
    /// <c>codex mcp add</c> refuses a home that is not there.
    /// </remarks>
    /// <param name="directory">The scratch configuration directory.</param>
    /// <returns>The scope that restores whatever was there before.</returns>
    private static EnvironmentScope PointTheClientAt(string directory)
    {
        var codexHome = Directory.CreateDirectory(Path.Combine(directory, "codex")).FullName;

        return new EnvironmentScope(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [ConfigDirectoryVariable] = OnboardedClientConfig.Seed(directory),
            [CodexHomeVariable] = codexHome,
        });
    }
}
