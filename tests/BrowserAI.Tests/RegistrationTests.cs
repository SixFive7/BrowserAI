// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Registration;
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
/// <see cref="IRegistrationCommand"/> runs against a double, because a Velopack
/// fast-exit hook is a context no test host can enter — the same reason the
/// update lane is driven through <c>IUpdateClient</c>. What only the real client
/// can answer is asked of the real client, in the same run, against a
/// <b>scratch configuration directory</b>.
/// </para>
/// <para>
/// ⚠️ <b>Nothing here may touch the maintainer's own MCP configuration, and that
/// is asserted rather than intended.</b> The real-client arms point
/// <c>CLAUDE_CONFIG_DIR</c> at a scratch directory under <c>.work\</c> and then
/// prove the negative: the user's own configuration file does not contain the
/// path this test registered, and that path carries a GUID, so it cannot be
/// there by coincidence.
/// </para>
/// <para>
/// ⚠️ <b><c>[NotInParallel]</c> with no key, on the class, which in TUnit means
/// these arms run beside nothing at all.</b> <i>Corrected 2026-09-15 (previously
/// a <c>ClientGroup</c> key on the two arms that open a scope: "they both write
/// one process-wide environment variable — <c>CLAUDE_CONFIG_DIR</c> — and
/// then start a process that reads it. Two at once would each register into the
/// other's scratch directory … every member writes the same variable and
/// nothing else in the suite starts the client".)</i> Every sentence of that was
/// true and the conclusion did not follow: a key holds an arm apart from the
/// arms carrying the <b>same key</b>, and a process-wide variable is read by
/// <b>every child any arm in the suite starts</b> — none of which holds a key,
/// and none of which can be enumerated. The measured failure is
/// <see cref="RealInstallerTests"/>' — the sibling member of the old group,
/// whose <c>BROWSERAI_ROOT</c> reached three unrelated arms' browsers. The same
/// hazard is here: a <c>claude</c> CLI started by anything else during the
/// window would read this scratch configuration directory. <b>The class carries
/// it rather than the two arms</b>, so an arm added later inherits the rule
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
    /// executable and <b>exits in 59 ms</b> while the app runs on — so a client
    /// registered against it watches its MCP server die at the handshake, every
    /// time, with nothing in any log to say why.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheExecutionStubIsRefusedAndTheBinaryInsideCurrentIsRegistered()
    {
        const string Root = @"C:\Users\someone\AppData\Local\BrowserAI";

        await Assert.That(RegistrationTarget.TryResolve($@"{Root}\current\BrowserAI.exe", out var target, out _)).IsTrue();
        await Assert.That(target!.Command).IsEqualTo($@"{Root}\current\BrowserAI.exe");
        await Assert.That(target.InstallRoot).IsEqualTo(Root);

        // The stub, which sits directly under the root.
        await Assert.That(RegistrationTarget.TryResolve($@"{Root}\BrowserAI.exe", out var stub, out var refusal)).IsFalse();
        await Assert.That(stub).IsNull();
        await Assert.That(refusal).Contains("current");
        await Assert.That(refusal).Contains("59 ms");
    }

    /// <summary>A path that cannot be resolved is refused rather than guessed at.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APathThatIsNotAnInstalledBrowserAiIsRefused()
    {
        await Assert.That(RegistrationTarget.TryResolve(null, out _, out var noPath)).IsFalse();
        await Assert.That(noPath).Contains("no path");

        await Assert.That(RegistrationTarget.TryResolve(@"current\BrowserAI.exe", out _, out var relative)).IsFalse();
        await Assert.That(relative).Contains("fully qualified");

        // Case is not what decides it: the directory is `current` either way.
        await Assert.That(RegistrationTarget.TryResolve(@"C:\x\CURRENT\BrowserAI.exe", out var shouting, out _)).IsTrue();
        await Assert.That(shouting!.InstallRoot).IsEqualTo(@"C:\x");
    }

    // ---- Idempotence, which the client does not supply ----------------------

    /// <summary>
    /// Install, update, repair and reinstall converge on exactly one
    /// registration.
    /// </summary>
    /// <remarks>
    /// <b>The client's <c>add</c> is not idempotent</b> — measured 2026-08-16 @
    /// 2.1.233, a second one exits 1 — so this property belongs to BrowserAI and
    /// is asserted here over a double that models exactly that behaviour.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task InstallUpdateRepairAndReinstallProduceExactlyOneRegistration()
    {
        var client = new FakeClientCommandLine();
        var (logger, _) = Capture();

        const string Command = @"C:\install\current\BrowserAI.exe";

        await Assert.That(McpRegistrar.Apply(RegistrationIntent.Install, Command, client, logger).Status)
            .IsEqualTo(RegistrationStatus.Registered);

        await Assert.That(McpRegistrar.Apply(RegistrationIntent.Update, Command, client, logger).Status)
            .IsEqualTo(RegistrationStatus.AlreadyRegistered);

        await Assert.That(McpRegistrar.Apply(RegistrationIntent.Install, Command, client, logger).Status)
            .IsEqualTo(RegistrationStatus.Registered);

        await Assert.That(McpRegistrar.Apply(RegistrationIntent.Update, Command, client, logger).Status)
            .IsEqualTo(RegistrationStatus.AlreadyRegistered);

        await Assert.That(client.Registered.Count).IsEqualTo(1);
        await Assert.That(client.Registered[McpClientRegistration.ServerName]).IsEqualTo(Command);
    }

    /// <summary>
    /// An install re-points an existing registration; an update leaves one
    /// alone.
    /// </summary>
    /// <remarks>
    /// <b>The only judgement that differs between the two intents, and both
    /// directions cost something real.</b> Re-pointing on every update would
    /// silently delete arguments a user added to their own registration;
    /// never re-pointing would leave a stale path after a
    /// <c>Setup.exe --installto</c> elsewhere, which is a registration that
    /// launches nothing.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnInstallRePointsAStaleRegistrationAndAnUpdateNeverOverwritesOne()
    {
        var client = new FakeClientCommandLine();
        var (logger, _) = Capture();

        client.Registered[McpClientRegistration.ServerName] = @"C:\somewhere\old\current\BrowserAI.exe";

        const string Moved = @"D:\moved\current\BrowserAI.exe";

        await Assert.That(McpRegistrar.Apply(RegistrationIntent.Update, Moved, client, logger).Status)
            .IsEqualTo(RegistrationStatus.AlreadyRegistered);
        await Assert.That(client.Registered[McpClientRegistration.ServerName]).IsEqualTo(@"C:\somewhere\old\current\BrowserAI.exe");

        await Assert.That(McpRegistrar.Apply(RegistrationIntent.Install, Moved, client, logger).Status)
            .IsEqualTo(RegistrationStatus.Registered);
        await Assert.That(client.Registered[McpClientRegistration.ServerName]).IsEqualTo(Moved);

        // An install removes before it adds; an update never removes.
        await Assert.That(client.Verbs).IsEquivalentTo(AddRemoveAdd);
    }

    /// <summary>
    /// An uninstall removes the registration, and an already-absent one is not a
    /// failure.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnUninstallRemovesTheRegistrationAndAnAbsentOneIsNotAFailure()
    {
        var client = new FakeClientCommandLine();
        var (logger, log) = Capture();

        const string Command = @"C:\install\current\BrowserAI.exe";

        _ = McpRegistrar.Apply(RegistrationIntent.Install, Command, client, logger);

        var removed = McpRegistrar.Apply(RegistrationIntent.Uninstall, Command, client, logger);

        await Assert.That(removed.Status).IsEqualTo(RegistrationStatus.Unregistered);
        await Assert.That(client.Registered).IsEmpty();

        var again = McpRegistrar.Apply(RegistrationIntent.Uninstall, Command, client, logger);

        await Assert.That(again.Status).IsEqualTo(RegistrationStatus.NothingToUnregister);
        await Assert.That(again.IsWhatWasAskedFor).IsTrue();
        await Assert.That(log.Logged("There was no 'browserai' registered")).IsTrue();
    }

    // ---- Failing visibly, and never failing the install ---------------------

    /// <summary>
    /// A machine with no MCP client is reported, with the command to run once
    /// there is one.
    /// </summary>
    /// <remarks>
    /// <b>Warning, not error, and never a throw.</b> An installer that failed
    /// because the user has no MCP client would be worse than the state it was
    /// protecting against — but an installed BrowserAI nothing is configured to
    /// talk to is exactly the state this whole mechanism exists to end, so it is
    /// never silent either.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AMachineWithNoClientIsReportedRatherThanFailedOrIgnored()
    {
        var client = new FakeClientCommandLine { Executable = null };
        var (logger, log) = Capture();

        var report = McpRegistrar.Apply(RegistrationIntent.Install, @"C:\install\current\BrowserAI.exe", client, logger);

        await Assert.That(report.Status).IsEqualTo(RegistrationStatus.ClientNotFound);
        await Assert.That(report.IsWhatWasAskedFor).IsTrue();
        await Assert.That(report.Detail).Contains("claude mcp add browserai --scope user");
        await Assert.That(client.Invocations).IsEmpty();

        var warning = log.Records.Single(record => record.EventId.Id is 5);

        await Assert.That(warning.Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(warning.Message).Contains("nothing is configured to talk to it");
    }

    /// <summary>
    /// A client that refuses, one that hangs, and one that cannot be started at
    /// all are each named with the command to run by hand.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AClientThatDoesNotDoWhatWasAskedIsNamedWithTheManualCommand()
    {
        const string Command = @"C:\install\current\BrowserAI.exe";

        var refused = new FakeClientCommandLine { Always = new CommandOutcome(2, "some other failure", TimedOut: false, null) };
        var (logger, log) = Capture();

        var report = McpRegistrar.Apply(RegistrationIntent.Install, Command, refused, logger);

        await Assert.That(report.Status).IsEqualTo(RegistrationStatus.Failed);
        await Assert.That(report.IsWhatWasAskedFor).IsFalse();
        await Assert.That(report.Detail).Contains("exited 2");
        await Assert.That(report.Detail).Contains("some other failure");
        await Assert.That(report.Detail).Contains($"mcp add {McpClientRegistration.ServerName} --scope user");
        await Assert.That(log.Records.Any(record => record.Level is LogLevel.Error)).IsTrue();

        var hanging = new FakeClientCommandLine { Always = new CommandOutcome(-1, string.Empty, TimedOut: true, null) };
        var stalled = McpRegistrar.Apply(RegistrationIntent.Install, Command, hanging, logger);

        await Assert.That(stalled.Status).IsEqualTo(RegistrationStatus.Failed);
        await Assert.That(stalled.Detail).Contains("did not finish within 10s");

        var dead = new FakeClientCommandLine { Always = new CommandOutcome(-1, string.Empty, TimedOut: false, "Access is denied") };
        var unstartable = McpRegistrar.Apply(RegistrationIntent.Install, Command, dead, logger);

        await Assert.That(unstartable.Status).IsEqualTo(RegistrationStatus.Failed);
        await Assert.That(unstartable.Detail).Contains("Access is denied");
    }

    /// <summary>
    /// Nothing a client does can throw into the installer.
    /// </summary>
    /// <remarks>
    /// A hook that throws breaks the install. This is the boundary that makes
    /// that impossible, so it is asserted rather than reviewed.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AThrowingClientIsCaughtRatherThanBreakingTheInstall()
    {
        var client = new FakeClientCommandLine { Throws = true };
        var (logger, log) = Capture();

        var report = McpRegistrar.Apply(RegistrationIntent.Install, @"C:\install\current\BrowserAI.exe", client, logger);

        await Assert.That(report.Status).IsEqualTo(RegistrationStatus.Failed);
        await Assert.That(report.Detail).Contains("asked to throw");
        await Assert.That(log.Records.Any(record => record.EventId.Id is 8)).IsTrue();
    }

    /// <summary>
    /// A refusal to register still produces a log record and a report; it never
    /// registers the wrong thing quietly.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARefusedPathIsLoggedAtErrorAndTheClientIsNeverStarted()
    {
        var client = new FakeClientCommandLine();
        var (logger, log) = Capture();

        var report = McpRegistrar.Apply(RegistrationIntent.Install, @"C:\install\BrowserAI.exe", client, logger);

        await Assert.That(report.Status).IsEqualTo(RegistrationStatus.Refused);
        await Assert.That(client.Invocations).IsEmpty();
        await Assert.That(log.Records.Single(record => record.EventId.Id is 6).Level).IsEqualTo(LogLevel.Error);
    }

    // ---- The state a person can find ---------------------------------------

    /// <summary>
    /// The hook writes its outcome where a person looking for it will find it:
    /// in the <b>data</b> root, and nothing at all under the install root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Re-pointed 2026-09-15 (previously "beside <c>current\</c> rather
    /// than inside it … an update replaces that directory wholesale").</b> The
    /// right rule, aimed one level too low. A sibling of <c>current\</c> is still
    /// inside the install root, and the two events a person reads this file
    /// after are the two that empty it: a repair install, which renames the root
    /// aside and deletes it, and an uninstall. The assertion is the stronger one
    /// now — the record and the log are not under the install root at all, and
    /// the hook leaves that root untouched.
    /// </para>
    /// <para>
    /// <b>The data root is a scratch directory here, and it has to be.</b> The
    /// product resolves a constant under <c>%LocalAppData%</c>; a test that let
    /// it do so would write a registration record into the developer's own data
    /// root — which is why the overload the suite drives takes the seam rather
    /// than defaulting it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheHookWritesItsOutcomeIntoTheDataRootAndNothingUnderTheInstallRoot()
    {
        using var install = ScratchDirectory.Create("registration-hook");
        using var data = ScratchDirectory.Create("registration-hook-data");

        var command = Path.Combine(install.Path, "current", "BrowserAI.exe");
        var client = new FakeClientCommandLine();

        var report = HookRegistration.Run(
            RegistrationIntent.Install,
            "9.9.9",
            command,
            client,
            new LocalAppDataPaths(data.Path)).Registration;

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
        await Assert.That(text).Contains($"Registered '{McpClientRegistration.ServerName}'");

        // ⚠️ THE HALF THAT IS RED AGAINST THE OLD LAYOUT: the install root the
        // image path names is a directory Setup.exe renames aside and deletes,
        // and the hook leaves nothing whatever in it.
        var underTheInstall = Directory.EnumerateFileSystemEntries(install.Path)
            .Select(Path.GetFileName)
            .ToList();

        await Assert.That(string.Join(", ", underTheInstall)).IsEmpty();
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

        var command = Path.Combine(install.Path, "current", "BrowserAI.exe");
        var client = new FakeClientCommandLine { Executable = null };

        var report = HookRegistration.Run(
            RegistrationIntent.Install,
            "9.9.9",
            command,
            client,
            new LocalAppDataPaths(data.Path)).Registration;

        await Assert.That(report.Status).IsEqualTo(RegistrationStatus.ClientNotFound);

        var written = await File.ReadAllTextAsync(Path.Combine(data.Path, RegistrationRecord.FileName));

        await Assert.That(written).Contains("\"outcome\": \"ClientNotFound\"");
        await Assert.That(written).Contains("claude mcp add browserai --scope user");
    }

    // ---- The data root at uninstall -----------------------------------------

    /// <summary>
    /// A silent uninstall keeps the data root and never puts a window on
    /// anybody's screen.
    /// </summary>
    /// <remarks>
    /// <b>Both halves, and the second is the one that would break a scripted
    /// uninstall.</b> A dialog inside <c>Update.exe --uninstall --silent</c> is a
    /// 60-second stall ending in the hook being killed — so the assertion is not
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
            Path.Combine(install.Path, "current", "BrowserAI.exe"),
            new FakeClientCommandLine(),
            paths,
            silent: true,
            ask: _ =>
            {
                asked++;
                return true;
            });

        // Nothing was registered with the double first, so the registration half
        // reports that there was nothing to remove -- which is still what was
        // asked for, and is the half this arm does not judge.
        await Assert.That(outcome.Registration.IsWhatWasAskedFor).IsTrue();
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
            Path.Combine(install.Path, "current", "BrowserAI.exe"),
            new FakeClientCommandLine(),
            paths,
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
    /// nothing left behind — the hook's own open log included.
    /// </summary>
    /// <remarks>
    /// <b>The log is the interesting node and it is why the removal is split
    /// from the decision.</b> The hook writes its process log inside the data
    /// root, so a delete performed while that file was open would leave exactly
    /// one directory standing — the one holding the record of the decision. The
    /// product closes the log first; this asserts the consequence rather than
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
            Path.Combine(install.Path, "current", "BrowserAI.exe"),
            new FakeClientCommandLine(),
            paths,
            silent: false,
            ask: _ => true);

        await Assert.That(outcome.Registration.IsWhatWasAskedFor).IsTrue();
        await Assert.That(outcome.Disposal!.Choice).IsEqualTo(DataRootChoice.Remove);

        await Assert.That(string.Join(Environment.NewLine, outcome.Disposal.Failures)).IsEmpty();
        await Assert.That(File.Exists(planted)).IsFalse();
        await Assert.That(Directory.Exists(paths.LogDirectory)).IsFalse();
        await Assert.That(Directory.Exists(data.Path)).IsFalse();
    }

    /// <summary>
    /// An update never asks about the data root and never touches it — which is
    /// the founding promise of separating the two directories at all.
    /// </summary>
    /// <remarks>
    /// <b>By construction rather than by care, and this is what holds the
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
            Path.Combine(install.Path, "current", "BrowserAI.exe"),
            new FakeClientCommandLine(),
            paths,
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
    /// <b>The install hook creates the data root itself</b> — it opens a log
    /// there — so by the time an uninstall runs, the directory always exists.
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
            Path.Combine(install.Path, "current", "BrowserAI.exe"),
            new FakeClientCommandLine(),
            paths,
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
    /// <b>Velopack passes the hook nothing</b> — not a flag, not an environment
    /// variable — so <c>Update.exe</c>'s own command line is the only place the
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

    // ---- The real client ----------------------------------------------------

    /// <summary>
    /// The real client still says what its exit codes cannot.
    /// </summary>
    /// <remarks>
    /// <b>The one assertion the double cannot make.</b> Every failure the client
    /// has exits 1 — a duplicate <c>add</c> and a broken configuration are
    /// indistinguishable by exit code — so the product discriminates on
    /// upstream's English. This is what turns a wording change into a red test
    /// instead of a registration silently reported as failed in the field.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheClientStillSaysWhatTheExitCodesCannot()
    {
        var client = SuiteEnvironment.RequireClientCommandLine();

        using var config = ScratchDirectory.Create("registration-wording");
        using var install = ScratchDirectory.Create("registration-wording-install");

        var command = Path.Combine(install.Path, "current", "BrowserAI.exe");
        var commands = new ClientCommandLine();

        using (PointTheClientAt(config.Path))
        {
            var first = commands.Run(client, McpClientRegistration.AddArguments(command), McpClientRegistration.Budget);
            await Assert.That(first.Succeeded).IsTrue();

            var duplicate = commands.Run(client, McpClientRegistration.AddArguments(command), McpClientRegistration.Budget);

            await Assert.That(duplicate.Succeeded).IsFalse();
            await Assert.That(McpClientRegistration.MeansAlreadyRegistered(duplicate.ExitCode, duplicate.Output)).IsTrue();

            var removed = commands.Run(client, McpClientRegistration.RemoveArguments(), McpClientRegistration.Budget);
            await Assert.That(removed.Succeeded).IsTrue();

            var absent = commands.Run(client, McpClientRegistration.RemoveArguments(), McpClientRegistration.Budget);

            await Assert.That(absent.Succeeded).IsFalse();
            await Assert.That(McpClientRegistration.MeansNothingToRemove(absent.ExitCode, absent.Output)).IsTrue();

            // And the two are told apart, which is the property that matters:
            // an "already exists" is not read as "nothing to remove" or the
            // reverse.
            await Assert.That(McpClientRegistration.MeansNothingToRemove(duplicate.ExitCode, duplicate.Output)).IsFalse();
            await Assert.That(McpClientRegistration.MeansAlreadyRegistered(absent.ExitCode, absent.Output)).IsFalse();
        }
    }

    /// <summary>
    /// The whole mechanism against the real client: registered at user scope,
    /// idempotent, removable — and the maintainer's own configuration untouched.
    /// </summary>
    /// <remarks>
    /// <b>This is the proof that the charter's promise is kept</b> — one
    /// registration, at user scope, available in every repository, with no file
    /// written into any of them. The entry is asserted in the client's own
    /// configuration file rather than in the client's report of it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRealClientRegistersBrowserAiAtUserScopeAndNothingElseIsTouched()
    {
        _ = SuiteEnvironment.RequireClientCommandLine();

        using var config = ScratchDirectory.Create("registration-live");
        using var install = ScratchDirectory.Create("registration-live-install");

        var command = Path.Combine(install.Path, "current", "BrowserAI.exe");
        var commands = new ClientCommandLine();
        var (logger, _) = Capture();
        var file = Path.Combine(config.Path, ConfigFileName);

        using (PointTheClientAt(config.Path))
        {
            var installed = McpRegistrar.Apply(RegistrationIntent.Install, command, commands, logger);

            await Assert.That(installed.Status).IsEqualTo(RegistrationStatus.Registered);
            await Assert.That(File.Exists(file)).IsTrue();

            var written = await File.ReadAllTextAsync(file);

            await Assert.That(written).Contains($"\"{McpClientRegistration.ServerName}\"");
            await Assert.That(written).Contains(command.Replace(@"\", @"\\", StringComparison.Ordinal));

            // Idempotence, against the client rather than against the double.
            await Assert.That(McpRegistrar.Apply(RegistrationIntent.Update, command, commands, logger).Status)
                .IsEqualTo(RegistrationStatus.AlreadyRegistered);
            await Assert.That(McpRegistrar.Apply(RegistrationIntent.Install, command, commands, logger).Status)
                .IsEqualTo(RegistrationStatus.Registered);

            var afterFour = await File.ReadAllTextAsync(file);

            await Assert.That(Occurrences(afterFour, $"\"{McpClientRegistration.ServerName}\"")).IsEqualTo(1);

            var removed = McpRegistrar.Apply(RegistrationIntent.Uninstall, command, commands, logger);

            await Assert.That(removed.Status).IsEqualTo(RegistrationStatus.Unregistered);

            var afterRemoval = await File.ReadAllTextAsync(file);

            await Assert.That(afterRemoval).DoesNotContain($"\"{McpClientRegistration.ServerName}\"");

            await Assert.That(McpRegistrar.Apply(RegistrationIntent.Uninstall, command, commands, logger).Status)
                .IsEqualTo(RegistrationStatus.NothingToUnregister);
        }

        // ⚠️ The negative that matters. The registered path carries this run's
        // GUID, so its absence from the user's own configuration is proof rather
        // than an argument -- and it survives the client rewriting that file for
        // its own reasons while the test runs, which a hash comparison would not.
        var mine = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify),
            ConfigFileName);

        if (File.Exists(mine))
        {
            var untouched = await File.ReadAllTextAsync(mine);

            await Assert.That(untouched).DoesNotContain(install.Path.Replace(@"\", @"\\", StringComparison.Ordinal));
            await Assert.That(untouched).DoesNotContain(install.Path);
        }
    }

    /// <summary>
    /// The product finds the real client the way it says it does.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheClientIsLocatedByFileNameAndNeverAsAShim()
    {
        var located = SuiteEnvironment.RequireClientCommandLine();

        await Assert.That(Path.IsPathFullyQualified(located)).IsTrue();
        await Assert.That(Path.GetFileName(located)).IsEqualTo(McpClientRegistration.ClientExecutable);
        await Assert.That(File.Exists(located)).IsTrue();

        // A .cmd shim cannot be started without cmd.exe, which stack.md
        // deviation 1 forbids -- so the name searched for carries its extension
        // and a shim can never be found by it.
        await Assert.That(McpClientRegistration.ClientExecutable).EndsWith(".exe");

        await Assert.That(new ClientCommandLine().Locate("browserai-no-such-client.exe")).IsNull();
    }

    // ---- Helpers ------------------------------------------------------------

    /// <summary>
    /// The verbs an update-then-install produces against one client: an update
    /// never removes, an install always does.
    /// </summary>
    private static readonly string[] AddRemoveAdd = ["add", "remove", "add"];

    /// <summary>
    /// Reads a log file the way everything else in this suite does — sharing
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
    /// inherits this process's environment block, and the alternative — an
    /// environment overlay on the product's own command runner — would be a seam
    /// that exists for no reason but this test. It is restored however the test
    /// ends, and the whole class is <c>[NotInParallel]</c> with no key —
    /// <i>corrected 2026-09-15 (previously "The arms that use it are
    /// <c>[NotInParallel]</c> on one group so they cannot overwrite each other's
    /// value")</i>, because overwriting each other's value was never the only
    /// way this goes wrong: any child of any arm reads it too.
    /// </remarks>
    /// <param name="directory">The scratch configuration directory.</param>
    /// <returns>The scope that restores whatever was there before.</returns>
    private static EnvironmentScope PointTheClientAt(string directory) => new(ConfigDirectoryVariable, directory);
}
