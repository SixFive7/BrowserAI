// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Interop;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>
/// What happens when something other than an MCP client starts BrowserAI -- which,
/// until 2026-09-14, was a server that ran until the machine was rebooted.
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure was measured end to end and it was a shipping defect</b>
/// (evidence: <c>docs/evidence/2026-09-14-firstrun/</c>). A non-silent <c>Setup.exe</c> --
/// the path <c>README.md</c> tells a person to take -- finishes by starting
/// <c>current\BrowserAI.exe</c> itself, with <c>show_window=true</c>: Velopack
/// passes <c>CREATE_NO_WINDOW</c> for a hook and <b>not</b> for the app start,
/// read out of 1.2.0's <c>process_win.rs</c> and confirmed against the logged
/// flag values. A console-subsystem binary therefore gets a real console window,
/// and with it a stdin that never reports end-of-file. <c>Setup.exe</c> then
/// exited 44 ms later, so the liveness watch found the launcher pid already dead
/// and degraded to <i>EOF alone</i> -- which could never arrive. The server and
/// its node child were still running 254 seconds later, serving nobody, and were
/// killed only by an uninstall.
/// </para>
/// <para>
/// <b>Two halves, and only one of them is about the installer.</b> The first is
/// the specific case: Velopack says so, in an environment variable, so BrowserAI
/// can answer it exactly. The second is the general one -- <i>nothing can ever
/// tell me this conversation is over</i> -- and it holds for any launcher that
/// exits while leaving a console attached.
/// </para>
/// <para>
/// ⚠️ <b>One half since 2026-09-24 -- Q276 a.</b> The first was deleted with its
/// event: <c>VelopackApp.Run()</c> clears the variable in an installed process
/// before the server could read it, and <c>Setup.exe</c> starts the configuration
/// app and not the server, so the specific answer never answered anything. The
/// general one covers the installer's shape by itself, and the arms below hold
/// that it does, with the variable set and without it.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-09-15 (previously "no test in this suite starts
/// BrowserAI with a real console. It cannot -- every launch site in the tree is
/// required to set <c>CreateNoWindow</c> ... and a test that allocated a console
/// would put a window over whatever is on screen. So the console half of the
/// second rule is covered by the pure decision and by the wiring scan below,
/// and the end-to-end evidence for it is the 2026-09-14 probe rather than a run
/// of this suite").</b> The two premises were right and the conclusion was
/// wrong, and <b>the gap cost a shipped release</b>: v1.0.0 went out, the first
/// non-silent install produced the exact shape this paragraph said could not be
/// tested, and six green runs of this suite said nothing about it.
/// <c>CreateNoWindow</c> does not suppress the console -- it suppresses the
/// console's <b>window</b> -- so a windowless <c>cmd.exe</c> hands a child a real
/// console handle with nothing on screen. <see cref="OrphanedConsoleStart"/> is
/// that rig, and the two end-to-end arms below are what the paragraph said were
/// impossible.
/// </para>
/// </remarks>
internal sealed class InstallerHandoffTests
{
    /// <summary>
    /// The installer's own start is recognised from Velopack's own variable, and
    /// only from the value Velopack writes.
    /// </summary>
    /// <returns>The assertion task.</returns>
    /// <summary>
    /// The app clears the installer's variables <b>after</b> Velopack has read
    /// them, and before it starts anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Both halves, and until 2026-09-16 the order satisfied one of
    /// them.</b> The clearing exists because a child inherits its parent's
    /// environment: with <c>VELOPACK_FIRSTRUN</c> still set, clicking
    /// <i>Register</i> starts <c>claude.exe</c> carrying it, and anything
    /// <i>that</i> starts carries it too -- including the MCP server, which exits
    /// 0 on that variable by design. But it ran <b>before</b>
    /// <c>VelopackApp.Run()</c>, and <c>Run()</c> decides whether to invoke
    /// <c>OnFirstRun</c> and <c>OnRestarted</c> by reading exactly those two
    /// variables. Both callbacks were therefore unreachable in this binary: two
    /// log lines that could never be written, with a remark beside them
    /// describing behaviour that did not happen.
    /// </para>
    /// <para>
    /// <b>A source-order claim, so it is read out of the source.</b> Nothing
    /// observable distinguishes the two orders without an installer: the
    /// difference is two log lines in a run this suite cannot start. What is
    /// assertable is the order itself, and that is what this holds -- the clear
    /// is after the hook call and before the first thing that launches a
    /// process.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheInstallersVariablesAreClearedAfterVelopackReadsThemAndBeforeAnythingStarts()
    {
        // ⚠️ THE ONE EXECUTABLE'S Main SINCE 2026-10-08, D7 a (previously the
        // configuration app's own Main in src/BrowserAI.App/Program.cs, with the
        // first launch read as its RegisterAI lookup). The hooks, the read and the
        // clear happen once, before the arguments decide anything, and the first
        // thing that can start a child is that decision.
        var source = await File.ReadAllTextAsync(
            Path.Combine(RepositoryLayout.Root.FullName, "src", "BrowserAI", "Program.cs"));

        var run = source.IndexOf("VelopackStartup.RunAndServeLifecycleHooks(", StringComparison.Ordinal);
        var clear = source.IndexOf("        App.Program.ClearTheInstallersOwnVariables();", StringComparison.Ordinal);
        var launch = source.IndexOf("return ServesStdio(args)", StringComparison.Ordinal);

        await Assert.That(run).IsGreaterThan(-1);
        await Assert.That(clear).IsGreaterThan(-1);
        await Assert.That(launch).IsGreaterThan(-1);

        // After Run(), or Velopack never sees the variables it decides
        // OnFirstRun and OnRestarted from.
        await Assert.That(clear).IsGreaterThan(run);

        // And before anything that can start a child, or the guarantee the
        // clearing exists for is gone.
        await Assert.That(clear).IsLessThan(launch);
    }

    [Test]
    public async Task TheFirstRunVariableIsReadExactlyAsVelopackWritesIt()
    {
        var before = Environment.GetEnvironmentVariable(VelopackStartup.FirstRunVariable);

        try
        {
            Environment.SetEnvironmentVariable(VelopackStartup.FirstRunVariable, null);
            await Assert.That(VelopackStartup.StartedByTheInstaller()).IsFalse();

            Environment.SetEnvironmentVariable(VelopackStartup.FirstRunVariable, "true");
            await Assert.That(VelopackStartup.StartedByTheInstaller()).IsTrue();

            // Velopack writes the lower-case literal; a client environment that
            // happened to carry the name with another value is not the installer.
            Environment.SetEnvironmentVariable(VelopackStartup.FirstRunVariable, "TRUE");
            await Assert.That(VelopackStartup.StartedByTheInstaller()).IsTrue();

            Environment.SetEnvironmentVariable(VelopackStartup.FirstRunVariable, "1");
            await Assert.That(VelopackStartup.StartedByTheInstaller()).IsFalse();

            Environment.SetEnvironmentVariable(VelopackStartup.FirstRunVariable, string.Empty);
            await Assert.That(VelopackStartup.StartedByTheInstaller()).IsFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(VelopackStartup.FirstRunVariable, before);
        }
    }

    /// <summary>
    /// The published binary serves the client that started it, whatever the
    /// installer's variable says: the variable is no longer a reason to exit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Inverted 2026-09-24 -- Q276 a, the maintainer's words verbatim:
    /// "Q276 a".</b> <i>Previously
    /// <c>ThePublishedBinaryStartedByTheInstallerExitsBeforeItCreatesAnything</c>,
    /// which set <c>VELOPACK_FIRSTRUN=true</c> and required the server to exit 0
    /// logging <c>Startup[8]</c>, with nothing but <c>logs\</c> under its root.</i>
    /// That exit could never fire where it was meant to: <c>VelopackApp.Run()</c>
    /// clears the variable in an installed process before the server read it
    /// (<c>VelopackApp.cs:227-238</c> at 1.2.158), and <c>Setup.exe</c> has started
    /// the configuration app and not the server since 2026-09-15. So the branch and
    /// its event were deleted, the id retired, and what ends the installer's shape
    /// is the general exit alone -- launcher gone and a console on standard input --
    /// which the two arms below drive.
    /// </para>
    /// <para>
    /// <b>What is left to hold is the other direction</b>: a client that happens to
    /// hand the variable on is still served. Started from this host, which stays
    /// alive, over pipes, the server reaches its serving line and logs no installer
    /// exit, and it ends on end-of-file with exit 0. <b>Planted red 2026-09-24</b>
    /// against a published build that still carried the branch: it exited 0
    /// logging <c>Startup[8]</c> instead of serving.
    /// </para>
    /// <para>
    /// ⚠️ <b>A relay and its background since 2026-10-09</b> (previously the server
    /// was started alone and the arm waited for its <i>"BrowserAI is serving
    /// stdio"</i>, which was the in-process server's line and which nothing writes
    /// since S a). What a client starts is a relay, which reaches its background only
    /// after the handshake, so the arm speaks it: the harness starts a background
    /// beside the relay, in the same job and with the same environment, and the
    /// relay's own records must say it reached that background. Watched red with a
    /// relay that ended on the installer's variable.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePublishedBinaryServesTheClientThatStartedItWithTheInstallersVariableSet()
    {
        SuiteEnvironment.RequirePublishedSlice();

        PublishedSlice.EnsureFresh();

        using var root = ScratchDirectory.CreateUnderProfile("installer-variable-serves");

        var environment = PublishedSlice.InheritedEnvironment();
        environment[BrowserAiPaths.AppRootOverride] = root.Path;
        environment[VelopackStartup.FirstRunVariable] = "true";

        // Inside a kill-on-close job, the suite's standing rule for a real
        // BrowserAI, with the background the harness starts beside every published
        // relay, so an assertion that fails below leaves nothing running.
        await using var client = RawStdioClient.Start(PublishedSlice.Executable, PublishedSlice.Mcp, root.Path, environment);

        var relay = client.ProcessId;
        var relayCreated = ProcessIdentity.CreationTimeOf(relay);
        var logs = Path.Combine(root.Path, "logs");

        // Served: the handshake is answered, and the relay goes on to its background.
        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);

        // Until it says it reached the background, or goes: a hang detector, not a budget.
        var deadline = DateTime.UtcNow + TestDefaults.ProcessHang;
        var said = ProcessLogRecords.In(logs, relay, relayCreated);

        while (DateTime.UtcNow < deadline && client.ExitCode is null && !said.Contains(ConnectedSentence, StringComparison.Ordinal))
        {
            await Task.Delay(50);
            said = ProcessLogRecords.In(logs, relay, relayCreated);
        }

        // Read once more, so that a relay that went says what it said.
        said = ProcessLogRecords.In(logs, relay, relayCreated);

        await Assert.That(client.ExitCode).IsNull().Because(said);
        await Assert.That(said).Contains(ConnectedSentence);
        await Assert.That(said).DoesNotContain(RetiredInstallerExitSentence);

        // And it ends the way a served conversation ends: on end-of-file.
        await Assert.That(await client.CloseAndWaitForExitAsync(TestDefaults.ProcessHang)).IsTrue();
        await Assert.That(client.ExitCode).IsEqualTo(0);
    }

    /// <summary>The line a relay writes once it has reached its background and been answered.</summary>
    /// <remarks>
    /// <i>Corrected 2026-10-09 (previously <c>ServingSentence</c>, "BrowserAI is serving
    /// stdio", the in-process server's line).</i>
    /// </remarks>
    private const string ConnectedSentence = "Relay: connected to the background";

    /// <summary>
    /// What the retired installer exit, <c>Startup[8]</c>, said, which no build
    /// after Q276 writes.
    /// </summary>
    private const string RetiredInstallerExitSentence = "started by the installer";

    /// <summary>
    /// A client's start with no pipe on standard input is nobody to serve, and the
    /// one executable exits saying so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Rewritten 2026-10-08, D7 a</b> (previously
    /// <c>AnUnwatchableLauncherAndAConsoleStdinAreTogetherTheOnlyNoClientCase</c>, whose
    /// summary read "A launcher that is gone <b>and</b> a console stdin means nobody is
    /// there; either one on its own is ordinary"). The one executable serves a client
    /// only under <c>--mcp</c> with a pipe on standard input, and a console is not a
    /// pipe, so the conjunction below became one condition. The paragraphs that follow
    /// are the record of the conjunction. <b>Planted red 2026-10-08</b> against the tree
    /// before the change, whose <c>Program.cs</c> asked the console question.
    /// </para>
    /// <para>
    /// <b>The conjunction is the whole decision.</b> A watch that could not
    /// attach happens whenever a client starts BrowserAI through a wrapper -- that
    /// is the case the watch exists for, and stdin's EOF still ends the
    /// conversation. A console stdin happens whenever a developer runs the binary
    /// by hand -- and the launcher is that terminal, which is alive and watchable.
    /// Only together do they mean that neither teardown signal can ever arrive.
    /// </para>
    /// <para>
    /// <b>This is the wiring and not a run</b>, and the reason is in the
    /// class remarks: the suite may not allocate a console. What it can do is
    /// hold that <c>Program</c> asks both questions, in one condition, at the
    /// place where the watcher's own answer is known -- and that the answer is a
    /// clean exit and not a refusal to start.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AClientsStartWithNoPipeOnStandardInputIsNobodyToServe()
    {
        var program = await RepositoryLayout.ReadCodeAsync(
            new FileInfo(Path.Combine(RepositoryLayout.Root.FullName, "src", "BrowserAI", "Program.cs")));

        // ⚠️ ONE CONDITION SINCE 2026-10-08, D7 a (previously "if (client is null
        // && StandardInput.IsAConsole())", the conjunction of a launcher gone and a
        // console). The one executable serves a client only under --mcp with a pipe
        // on standard input, and a console is not a pipe, so the old shape is held
        // by the narrower rule.
        // ⚠️ And since the background and the relay, the same day, the check stands
        // on the one path that serves a client: after the task's start and the sweep
        // have branched off, and before the relay (previously "if
        // (args.Contains(McpArgument, StringComparer.Ordinal) &&
        // !StandardInput.IsAPipe())", when the in-process server's path also served
        // the session host).
        await Assert.That(program).Contains("if (!StandardInput.IsAPipe())");
        await Assert.That(program).DoesNotContain("StandardInput.IsAConsole()");

        var sweep = program.IndexOf("args.Contains(SweepArgument, StringComparer.Ordinal)", StringComparison.Ordinal);
        var check = program.IndexOf("if (!StandardInput.IsAPipe())", StringComparison.Ordinal);
        var relay = program.IndexOf("RunTheRelayAsync(args", StringComparison.Ordinal);

        await Assert.That(sweep).IsGreaterThan(-1);
        await Assert.That(check).IsGreaterThan(sweep);
        await Assert.That(relay).IsGreaterThan(check);

        // It says why, and it exits cleanly instead of refusing to start.
        var at = program.IndexOf("!StandardInput.IsAPipe()", StringComparison.Ordinal);

        await Assert.That(at).IsGreaterThan(-1);

        var decision = program[at..Math.Min(program.Length, at + 400)];

        await Assert.That(decision).Contains("StartupLog.NoPipeToServe");
        await Assert.That(decision).Contains("return 0;");

        // ⚠️ AND THE LAUNCHER IS ASKED ABOUT ONLY AFTER THE CHECK, BY THE RELAY --
        // corrected 2026-10-09 (previously "And the watcher is what supplies
        // `client`, so the condition cannot be read as being about something else",
        // which required the watch in this file and in front of the check, when the
        // check was the conjunction of a launcher gone and a console). The check is
        // one condition since 2026-10-08, and a start with no pipe ends having opened
        // nothing of its launcher's: the watch is the relay's, and the check stands in
        // front of the relay.
        await Assert.That(program).DoesNotContain("ClientLivenessWatcher");

        var relayMode = await RepositoryLayout.ReadCodeAsync(
            new FileInfo(Path.Combine(RepositoryLayout.Root.FullName, "src", "BrowserAI", "Program.Relay.cs")));

        await Assert.That(relayMode).Contains("ClientLivenessWatcher.ForParentProcess(");
    }

    /// <summary>
    /// The console question costs nothing, changes nothing, and answers the same
    /// thing twice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The value cannot be asserted, and finding that out is worth more
    /// than the assertion would have been.</b> This arm was written as
    /// <c>StandardInputUnderTheTestHostIsNotAConsole</c> and was <b>red from
    /// PowerShell and green from Git Bash</b> on the same tree, in the gate of
    /// 2026-09-15: a test host started by <c>Start-Process pwsh -WindowStyle
    /// Hidden</c> inherits a <b>console</b> standard input, and one started by
    /// <c>nohup bash -c ... | tee</c> inherits a <b>pipe</b>. Two instruments, as
    /// the two halves of this gate are meant to be -- and a suite that asserted
    /// either value would be asserting a property of whoever started it.
    /// </para>
    /// <para>
    /// <b>What that says about the product is the reassuring half.</b> Neither
    /// shell's answer can trigger the no-client exit on its own: the launcher is
    /// alive and watchable in both, so the conjunction is false either way. The
    /// exit needs a launcher that is gone <i>and</i> a console -- which is the
    /// installer's shape and nothing the suite produces.
    /// </para>
    /// <para>
    /// <b>What is left to assert is what the call site needs: that this is a
    /// question and not an action.</b> It is asked once, at a decision point, and
    /// a predicate that consumed a byte of stdin or changed a console mode would
    /// be a teardown decision with a side effect on the protocol channel.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheConsoleQuestionIsRepeatableAndHasNoSideEffect()
    {
        var first = StandardInput.IsAConsole();

        await Assert.That(StandardInput.IsAConsole()).IsEqualTo(first);
        await Assert.That(StandardInput.IsAConsole()).IsEqualTo(first);

        // And the pipe question the one executable asks since 2026-10-08, which is
        // a question about the same handle and must not consume it either.
        var pipe = StandardInput.IsAPipe();

        await Assert.That(StandardInput.IsAPipe()).IsEqualTo(pipe);
        await Assert.That(StandardInput.IsAPipe()).IsEqualTo(pipe);
    }

    /// <summary>
    /// A launcher that is gone and a console standard input: the product exits,
    /// with and without the installer's own variable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the arm that was missing on 2026-09-15, and it was watched red
    /// against the published v1.0.0 build.</b> The product decided correctly --
    /// <c>Startup[72]</c> then <c>Startup[9]</c>, 0.53 s in -- and then did not
    /// exit: alive sixty seconds later holding a <c>node.exe</c>, with the log
    /// ending at the <i>is exiting</i> line and <c>instances\</c> and
    /// <c>live\</c> still under the root, so the <c>finally</c> had not run
    /// either. Evidence: <c>docs/evidence/2026-09-15-fix/repro-red-3.txt</c>. The cause
    /// was <c>JsonLinesTransport.DisposeAsync</c> awaiting a read loop parked on
    /// a console that nothing can wake.
    /// </para>
    /// <para>
    /// <b>Both arms, because the variable is not the trigger.</b> Velopack's
    /// post-install launch sets <c>VELOPACK_FIRSTRUN=true</c>, and the real
    /// install on 2026-09-15 nevertheless reached the no-client decision and
    /// not the installer exit -- so something between the launch and the read
    /// unset it, and the exit may not depend on it. The stub
    /// <c>BrowserAI.exe</c> in an install root reaches the app through
    /// <c>Update.exe start</c>, which is console-bearing and does not set the
    /// variable at all, so the variable-less arm is the one that covers a person
    /// double-clicking the thing the installer put on their machine.
    /// </para>
    /// <para>
    /// ⚠️ <b>The arm asserts which decision was taken, not merely that the
    /// process went.</b> The rig's launcher dies about three hundred times
    /// faster than the product takes to ask about it, but the question is a
    /// race in principle -- and a build in which the launcher were still
    /// watchable would exit for the ordinary reason and pass while testing
    /// nothing.
    /// </para>
    /// <para>
    /// ⚠️ <b>Both arms take the general exit since 2026-09-24 -- Q276 a.</b>
    /// <i>Previously the arm with the variable set required the installer exit,
    /// <c>Startup[8]</c>, "started by the installer".</i> That branch was deleted
    /// with its event, because it could not fire on an installed process, so the
    /// variable changes nothing about the decision and both arms require
    /// <c>Startup[9]</c> and the absence of the retired sentence. <b>Planted red
    /// 2026-09-24</b>: against a published build that still carried the branch,
    /// the variable arm named the installer exit and failed.
    /// </para>
    /// </remarks>
    /// <param name="startedByTheInstaller">Whether Velopack's variable is set.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ThePublishedBinaryExitsWhenItsLauncherIsGoneAndStdinIsAConsole(bool startedByTheInstaller)
    {
        SuiteEnvironment.RequirePublishedSlice();

        using var root = ScratchDirectory.CreateUnderProfile("orphan-console");
        using var run = OrphanedConsoleStart.Begin(root.Path, startedByTheInstaller, TestDefaults.ProcessHang);

        await Assert.That(run.Started).IsTrue();

        // The decision first, so that a failure names which one was taken --
        // and waiting for the OTHER outcomes as well, so that a lost race is a
        // named failure in a second and not a ten-minute one that says only
        // that a sentence never arrived. The retired installer exit is one of
        // them, so a build that still carried it is named and not waited out.
        // ⚠️ Startup[14] since 2026-10-08 (previously Startup[9], "no client to
        // serve and is exiting"): the rig starts the one executable with --mcp,
        // and a console is not a pipe.
        const string Decision = "standard input is not a pipe, so there is no client to serve";

        await Assert.That(run.WaitUntilItSaysOneOf(TestDefaults.ProcessHang, Decision, RetiredInstallerExitSentence, "Watching the MCP client"))
            .IsEqualTo(Decision)
            .Because(run.Records());

        // And then the exit, which is the thing v1.0.0 did not do.
        await Assert.That(run.WaitUntilItExits(TestDefaults.ProcessHang)).IsTrue();
    }

    // ⚠️ RETIRED 2026-10-09: `ThePublishedBinaryTreatsALauncherThatExitedButStillOpensAsNobodyToServe`,
    // which held that a launcher that had exited and still opened was nobody to serve,
    // for a start with a console on standard input, and that the product said which of
    // the two dead shapes it saw before it exited. The one executable's --mcp start
    // with no pipe ends before it looks at its launcher since 2026-10-08 (Startup[14]),
    // so a console start says nothing about either shape, and its exit is held by the
    // two arms either side of this one. The corpse is still read, by the relay, of a
    // client that gave it a pipe: that is the arm below.

    /// <summary>
    /// A relay whose launcher has exited and still opens watches nothing, says which
    /// shape it saw, and serves its pipe until the pipe's end.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Carried over 2026-10-09 from
    /// <c>ThePublishedBinaryTreatsALauncherThatExitedButStillOpensAsNobodyToServe</c></b>,
    /// retired above. The corpse decision is the 2026-09-15 flake turned into a
    /// property: <c>OpenProcess</c> succeeding on a launcher that had exited was read
    /// as <i>there is somebody there</i>. For the relay the cost runs the other way: a
    /// watch armed on an exited process fires at once, and a relay whose client gave
    /// it a live pipe would end before its first call. So the relay must see the
    /// corpse, arm nothing, and be ended by its input alone.
    /// </para>
    /// <para>
    /// <b>The rig hands the relay three pipes from a launcher that is gone</b>
    /// (<see cref="OrphanedConsoleStart.BeginHandingOnPipes"/>), holds the write end
    /// of its standard input, and keeps the launcher's handle, so its pid opens onto a
    /// corpse. A background on the relay's pipe and a handshake show that the relay
    /// which watched nothing is serving; closing the write end is its end.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARelayWhoseLauncherExitedButStillOpensWatchesNothingAndServesItsInputToTheEnd()
    {
        SuiteEnvironment.RequirePublishedSlice();

        PublishedSlice.EnsureFresh();

        using var root = ScratchDirectory.CreateUnderProfile("orphan-corpse");

        var pipe = PublishedBackground.NewPipeName();

        using var run = OrphanedConsoleStart.BeginHandingOnPipes(
            root.Path,
            TestDefaults.ProcessHang,
            [Program.McpArgument, BrowserAI.Coordination.BackgroundPipe.PipeArgument, pipe]);

        await Assert.That(run.Started).IsTrue();

        // ⚠️ EITHER OUTCOME, so that losing this decision costs a second and not ten
        // minutes. Both sentences are written by the same few lines of the product,
        // so whichever arrives is the decision it took.
        var seen = run.WaitUntilItSaysOneOf(
            TestDefaults.ProcessHang,
            "has already exited",
            "Watching the MCP client");

        await Assert.That(seen).IsEqualTo("has already exited").Because(run.Records());

        // A relay that watched nothing still serves: a background on its pipe, in a
        // job of the arm's own, and the handshake that sends the relay to it.
        using var backgroundJob = JobObject.CreateKillOnClose();

        var environment = PublishedSlice.InheritedEnvironment();
        environment[BrowserAiPaths.AppRootOverride] = root.Path;

        using var background = PublishedBackground.Start(backgroundJob, root.Path, environment, pipe, []);

        // The writer owns the write end of the relay's standard input: disposing it is
        // the relay's end of input, and the finally disposes it again when an
        // assertion throws first.
        var toRelay = new StreamWriter(run.StandardInput, Utf8NoBom) { NewLine = "\n", AutoFlush = true };
        using var fromRelay = new StreamReader(run.StandardOutput, Utf8NoBom, leaveOpen: true);

        try
        {
            await ServeOneHandshakeAndEndAsync(run, toRelay, fromRelay);
        }
        finally
        {
            await toRelay.DisposeAsync();
        }

        await Assert.That(run.Records()).DoesNotContain("Watching the MCP client");
    }

    /// <summary>UTF-8 with no byte-order mark, which is what the relay reads and writes.</summary>
    private static readonly System.Text.UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// The handshake that sends a relay to its background, the relay's record that it
    /// got there, and then the relay's end of input and its exit.
    /// </summary>
    /// <param name="run">The relay's rig.</param>
    /// <param name="toRelay">The relay's standard input.</param>
    /// <param name="fromRelay">The relay's standard output.</param>
    /// <returns>The assertion task.</returns>
    private static async Task ServeOneHandshakeAndEndAsync(OrphanedConsoleStart run, StreamWriter toRelay, StreamReader fromRelay)
    {
        await toRelay.WriteLineAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "initialize",
            ["params"] = new JsonObject
            {
                ["protocolVersion"] = SliceRun.OfferedProtocolVersion,
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject { ["name"] = RawStdioClient.DefaultClientName, ["version"] = "1" },
            },
        }.ToJsonString());

        using (var deadline = new CancellationTokenSource(TestDefaults.ProcessHang))
        {
            var answer = await ReadAnswerAsync(fromRelay, 1, deadline.Token);

            await Assert.That(answer["result"]).IsNotNull().Because(answer.ToJsonString());
        }

        await toRelay.WriteLineAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" }.ToJsonString());

        await Assert.That(run.WaitUntilItSays(ConnectedSentence, TestDefaults.ProcessHang)).IsTrue().Because(run.Records());
        await Assert.That(run.IsAlive()).IsTrue().Because(run.Records());

        // And its input's end is what ends it.
        await toRelay.DisposeAsync();

        await Assert.That(run.WaitUntilItSays("Relay: the client closed its input, so the relay ends.", TestDefaults.ProcessHang)).IsTrue().Because(run.Records());
        await Assert.That(run.WaitUntilItExits(TestDefaults.ProcessHang)).IsTrue();
    }

    /// <summary>Reads lines until the answer to one request arrives.</summary>
    /// <param name="reader">The relay's standard output.</param>
    /// <param name="id">The request's id.</param>
    /// <param name="cancellationToken">The hang detector.</param>
    /// <returns>The answer's envelope.</returns>
    private static async Task<JsonObject> ReadAnswerAsync(StreamReader reader, int id, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length is not 0 && JsonNode.Parse(line) is JsonObject envelope && (int?)envelope["id"] == id)
            {
                return envelope;
            }
        }

        throw new InvalidOperationException($"The relay closed its standard output before answering request {id}.");
    }

    /// <summary>
    /// A run with nobody to serve starts no child, sweeps nothing, checks no
    /// feed and creates nothing but its log.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The ordering is the property, and it was the second finding of
    /// 2026-09-15.</b> The real install's orphan had already started its
    /// <c>playwright-mcp</c> child <b>506 ms before</b> it worked out that there
    /// was nobody to serve -- and had swept the machine and opened the update
    /// lane as well -- so the run that had least reason to cost anything cost the
    /// most, and left a second orphan behind when it hung.
    /// </para>
    /// <para>
    /// <b>Asserted on the records the product already writes</b>, which is the
    /// same shape the installer-exit arm used until Q276 deleted it with its
    /// branch on 2026-09-24: each of the three sentences is one step the old order took, and
    /// <c>instances\</c> is what proves no child was started, because a child's
    /// working directory is inside it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARunWithNobodyToServeStartsNothingAndCreatesNothingButItsLog()
    {
        SuiteEnvironment.RequirePublishedSlice();

        using var root = ScratchDirectory.CreateUnderProfile("orphan-costs");

        // ⚠️ THE OPENABLE CORPSE, deliberately -- 2026-09-15. This arm used to
        // take whichever shape the machine happened to produce, and on the one
        // full run of four in which the launcher's pid was still openable it
        // sat at the full TestDefaults.ProcessHang waiting for a record the
        // product had decided not to write. Both shapes are nobody to serve;
        // this one is the shape the rig can guarantee.
        using var run = OrphanedConsoleStart.Begin(
            root.Path,
            startedByTheInstaller: false,
            TestDefaults.ProcessHang,
            LauncherCorpse.Openable);

        await Assert.That(run.Started).IsTrue();

        await Assert.That(run.WaitUntilItSaysOneOf(
            TestDefaults.ProcessHang,
            "standard input is not a pipe, so there is no client to serve",
            "Watching the MCP client"))
            .IsEqualTo("standard input is not a pipe, so there is no client to serve");

        var said = run.Records();

        await Assert.That(said).DoesNotContain("playwright-mcp[surface]: started");
        await Assert.That(said).DoesNotContain("Stray sweep:");
        await Assert.That(said).DoesNotContain("Checking for updates at");

        // ⚠️ AND NOTHING WAS CREATED. `live\`, `instances\` and `index\` are
        // each a step the old order took before it discovered there was nobody
        // there.
        var created = Directory.EnumerateFileSystemEntries(root.Path)
            .Select(Path.GetFileName)
            .Where(name => !string.Equals(name, "logs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        await Assert.That(string.Join(", ", created)).IsEmpty();
    }

    /// <summary>Reads a log file the writer may still hold open.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its text.</returns>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
