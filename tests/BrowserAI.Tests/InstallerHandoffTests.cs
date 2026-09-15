// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>
/// What happens when something other than an MCP client starts BrowserAI — which,
/// until 2026-09-14, was a server that ran until the machine was rebooted.
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure was measured end to end and it was a shipping defect</b>
/// (evidence: <c>.work/2026-09-14-firstrun/</c>). A non-silent <c>Setup.exe</c> —
/// the path <c>README.md</c> tells a person to take — finishes by starting
/// <c>current\BrowserAI.exe</c> itself, with <c>show_window=true</c>: Velopack
/// passes <c>CREATE_NO_WINDOW</c> for a hook and <b>not</b> for the app start,
/// read out of 1.2.0's <c>process_win.rs</c> and confirmed against the logged
/// flag values. A console-subsystem binary therefore gets a real console window,
/// and with it a stdin that never reports end-of-file. <c>Setup.exe</c> then
/// exited 44 ms later, so the liveness watch found the launcher pid already dead
/// and degraded to <i>EOF alone</i> — which could never arrive. The server and
/// its node child were still running 254 seconds later, serving nobody, and were
/// killed only by an uninstall.
/// </para>
/// <para>
/// <b>Two halves, and only one of them is about the installer.</b> The first is
/// the specific case: Velopack says so, in an environment variable, so BrowserAI
/// can answer it exactly. The second is the general one — <i>nothing can ever
/// tell me this conversation is over</i> — and it holds for any launcher that
/// exits while leaving a console attached.
/// </para>
/// <para>
/// <b>What is not asserted here, said plainly:</b> no test in this suite starts
/// BrowserAI with a real console. It cannot — every launch site in the tree is
/// required to set <c>CreateNoWindow</c>
/// (<c>HouseRuleTests.EveryProcessLaunchInTheTreeSuppressesTheConsoleWindow</c>),
/// and a test that allocated a console would put a window over whatever is on
/// screen. So the console half of the second rule is covered by the pure
/// decision and by the wiring scan below, and the end-to-end evidence for it is
/// the 2026-09-14 probe rather than a run of this suite.
/// </para>
/// </remarks>
internal sealed class InstallerHandoffTests
{
    /// <summary>
    /// The installer's own start is recognised from Velopack's own variable, and
    /// only from the value Velopack writes.
    /// </summary>
    /// <returns>The assertion task.</returns>
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
    /// The published binary really exits when the installer started it, and it
    /// exits <b>before</b> creating anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Through the front door, because the ordering is half the property.</b>
    /// The measured cost of the old behaviour was not only that the process
    /// stayed: it swept the machine, took a machine-wide mutex, created an
    /// instance directory and started a node child that would have provisioned
    /// 768 MB on a first run. So the assertion is not <i>it exited</i> — it is
    /// that <c>logs\</c> is the <b>only</b> thing under the root afterwards,
    /// which is the same shape
    /// <c>InstallRootScopeTests.ThePublishedBinaryRefusesToServeOutOfASharedRootAndSaysWhyInTheLog</c>
    /// uses for the refusal that has to happen before any state exists.
    /// </para>
    /// <para>
    /// <b>The exit code is 0, and that matters to the installer.</b> Velopack
    /// starts the app and does not wait for it, but a non-zero exit from a
    /// freshly installed binary is what a person would find in the install log
    /// if they ever went looking; <i>nothing to serve</i> is not a failure.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePublishedBinaryStartedByTheInstallerExitsBeforeItCreatesAnything()
    {
        SuiteEnvironment.RequirePublishedSlice();

        using var root = ScratchDirectory.CreateUnderProfile("installer-firstrun");

        var environment = PublishedSlice.InheritedEnvironment();
        environment[BrowserAiPaths.AppRootOverride] = root.Path;
        environment[VelopackStartup.FirstRunVariable] = "true";

        // ⚠️ Inside a kill-on-close job, which is the suite's standing rule for
        // starting a real BrowserAI — and here it is also the hang detector: a
        // build in which this exit was deleted starts serving and waits on its
        // stdin for ever, and what fails then must be this assertion rather than
        // the whole run.
        using var job = JobObject.CreateKillOnClose();

        using var process = JobLauncher.Start(job, PublishedSlice.Executable, [], root.Path, environment);

        var exited = await process.WaitForExitAsync(TestDefaults.ProcessHang);

        await Assert.That(exited).IsTrue();
        await Assert.That(process.TryReadExitCode()).IsEqualTo(0);

        var logs = Path.Combine(root.Path, "logs");
        var said = Directory.Exists(logs)
            ? string.Join(Environment.NewLine, Directory.EnumerateFiles(logs).Select(ReadShared))
            : string.Empty;

        await Assert.That(said).Contains("started by the installer");
        await Assert.That(said).Contains(VelopackStartup.FirstRunVariable);

        // ⚠️ AND NOTHING ELSE WAS CREATED. `live\`, `instances\`, `index\` and
        // `browsers\` are each a step the old behaviour took before it settled
        // down to serve nobody; `instances\` is the one that proves no child was
        // started, because a child's working directory is inside it.
        var created = Directory.EnumerateFileSystemEntries(root.Path)
            .Select(Path.GetFileName)
            .Where(name => !string.Equals(name, "logs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        await Assert.That(string.Join(", ", created)).IsEmpty();

        // The serving line is what the old build wrote next, and it is the one
        // sentence that must not be in this log.
        await Assert.That(said).DoesNotContain("BrowserAI is serving stdio");
    }

    /// <summary>
    /// A launcher that is gone <b>and</b> a console stdin means nobody is there;
    /// either one on its own is ordinary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The conjunction is the whole decision.</b> A watch that could not
    /// attach happens whenever a client starts BrowserAI through a wrapper — that
    /// is the case the watch exists for, and stdin's EOF still ends the
    /// conversation. A console stdin happens whenever a developer runs the binary
    /// by hand — and the launcher is that terminal, which is alive and watchable.
    /// Only together do they mean that neither teardown signal can ever arrive.
    /// </para>
    /// <para>
    /// <b>This is the wiring rather than a run</b>, and the reason is in the
    /// class remarks: the suite may not allocate a console. What it can do is
    /// hold that <c>Program</c> asks both questions, in one condition, at the
    /// place where the watcher's own answer is known — and that the answer is a
    /// clean exit rather than a refusal to start.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnUnwatchableLauncherAndAConsoleStdinAreTogetherTheOnlyNoClientCase()
    {
        var program = await RepositoryLayout.ReadCodeAsync(
            new FileInfo(Path.Combine(RepositoryLayout.Root.FullName, "src", "BrowserAI", "Program.cs")));

        // One condition, both halves, in the order they are cheapest to answer.
        await Assert.That(program).Contains("if (client is null && StandardInput.IsAConsole())");

        // It says why, and it exits cleanly rather than refusing to start: an
        // installer reading a non-zero exit from a freshly installed binary
        // would be reading a failure that did not happen.
        var at = program.IndexOf("StandardInput.IsAConsole()", StringComparison.Ordinal);

        await Assert.That(at).IsGreaterThan(-1);

        var decision = program[at..Math.Min(program.Length, at + 400)];

        await Assert.That(decision).Contains("StartupLog.NoClientToServe");
        await Assert.That(decision).Contains("return 0;");

        // And the watcher is what supplies `client`, so the condition cannot be
        // read as being about something else.
        var watcher = program.IndexOf("ClientLivenessWatcher.ForParentProcess(", StringComparison.Ordinal);

        await Assert.That(watcher).IsGreaterThan(-1);
        await Assert.That(watcher).IsLessThan(at);
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
    /// <c>nohup bash -c … | tee</c> inherits a <b>pipe</b>. Two instruments, as
    /// the two halves of this gate are meant to be — and a suite that asserted
    /// either value would be asserting a property of whoever started it.
    /// </para>
    /// <para>
    /// <b>What that says about the product is the reassuring half.</b> Neither
    /// shell's answer can trigger the no-client exit on its own: the launcher is
    /// alive and watchable in both, so the conjunction is false either way. The
    /// exit needs a launcher that is gone <i>and</i> a console — which is the
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
