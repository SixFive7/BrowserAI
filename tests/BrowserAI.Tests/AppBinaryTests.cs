// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;
using System.Text.Json;
using BrowserAI.App;
using BrowserAI.Interop;
using BrowserAI.Runtime;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// What the one shipped executable declares about itself: its subsystem, its
/// embedded manifest, and the apartment its main thread runs in.
/// </summary>
/// <remarks>
/// <i>Renamed 2026-10-03 (previously <c>TaskDialogLayoutTests</c>, whose summary was
/// "The two hand-written task dialog structures, against Microsoft's own metadata --
/// and the subsystem each shipped executable declares").</i> The configuration
/// window is deleted (Q319 b), and with it the task dialog's two structures and the
/// three arms that held their layout against Microsoft's metadata. What is left is
/// about the binaries.
/// </remarks>
internal sealed class AppBinaryTests
{
    /// <summary>
    /// The one executable is a Windows-subsystem binary, read out of the file
    /// itself, and the configuration app builds no executable of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is the property the whole design rests on.</b> A non-silent
    /// <c>Setup.exe</c> starts the main executable with
    /// <c>CREATE_UNICODE_ENVIRONMENT</c> and nothing else; a console-subsystem
    /// binary started that way from a windowless parent is given a console, and
    /// on this machine that was a Windows Terminal window over the user's work
    /// serving nobody for 215 seconds. A Windows-subsystem binary is never
    /// allocated one.
    /// </para>
    /// <para>
    /// ⚠️ <b>One file since 2026-10-08, D7 a, the maintainer's words verbatim:
    /// <i>"d7 a"</i></b> (previously
    /// <c>TheAppIsAWindowBinaryAndTheServerIsAConsoleOne</c>, which held the
    /// configuration app windowless and <c>BrowserAI.Server.exe</c> console). A
    /// client still speaks stdio to the one file: every client gave a windowless
    /// build of the server a pipe on all three standard handles, 54 of 54 runs on
    /// 2026-10-04, and <c>RegistrationTarget</c> now refuses a file at the
    /// registered name that is NOT windowless. <b>Planted red 2026-10-08</b> against
    /// the tree before the change, where <c>src/BrowserAI</c> built
    /// <c>BrowserAI.Server.exe</c> and no <c>BrowserAI.exe</c>.
    /// </para>
    /// <para>
    /// <b>Read off the Debug output, which always exists</b>, because the
    /// subsystem comes from <c>OutputType</c> and is identical in every
    /// configuration. The published AOT binary is checked the same way when it is
    /// present, which is what would catch a link that did not honour it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheOneExecutableIsAWindowsSubsystemBinary()
    {
        await Assert.That(PeSubsystem.Of(Built("BrowserAI", "BrowserAI.exe")))
            .IsEqualTo(PeSubsystem.WindowsGui);

        if (File.Exists(PublishedSlice.Executable))
        {
            await Assert.That(PeSubsystem.Of(PublishedSlice.Executable)).IsEqualTo(PeSubsystem.WindowsGui);
        }

        // The configuration app is a library now, so its project builds no
        // executable that could be packed or started by mistake.
        await Assert.That(File.Exists(Path.Combine(
            RepositoryLayout.Root.FullName, "src", "BrowserAI.App", "bin", "Debug", "net10.0-windows", "BrowserAI.exe"))).IsFalse();
    }

    /// <summary>
    /// The argument chooses what a start of the one executable does, and a start
    /// with no argument never serves stdio.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>D7 a, 2026-10-08.</b> The server half runs for <c>--mcp</c>, a client's
    /// registration; for <c>--host</c> with a pipe, the session host the
    /// coordinator starts; and for <c>--sweep</c>, a kb re-verification row's one
    /// pass. Every other start is the configuration app's: no argument (the Start
    /// Menu, <c>Setup.exe</c> after a non-silent install, a double-click),
    /// <c>--sessions</c>, <c>--report</c>, the logon task's <c>--sign-in</c>. The
    /// windowless file reads end of input at once when it is started with no
    /// handles, measured 6 of 6 on 2026-10-04, so a start with no argument may never
    /// be a server.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-08</b> with <c>ServesStdio</c> answering
    /// <see langword="true"/> for a start with no argument, the shape the server
    /// had while it was its own file.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheArgumentChoosesTheModeAndNoArgumentIsAPersonsStart()
    {
        await Assert.That(Program.ServesStdio([Program.McpArgument])).IsTrue();
        await Assert.That(Program.ServesStdio([Program.McpArgument, Program.RelayArgument, @"\\.\pipe\x"])).IsTrue();
        await Assert.That(Program.ServesStdio([Program.HostArgument, @"\\.\pipe\x"])).IsTrue();
        await Assert.That(Program.ServesStdio([Program.SweepArgument])).IsTrue();

        await Assert.That(Program.ServesStdio([])).IsFalse();
        await Assert.That(Program.ServesStdio(["--sessions"])).IsFalse();
        await Assert.That(Program.ServesStdio(["--report", "report.json"])).IsFalse();
        await Assert.That(Program.ServesStdio(["--sign-in", "$(Arg0)"])).IsFalse();
        await Assert.That(Program.ServesStdio(["--write-address", "address.txt"])).IsFalse();

        // A pipe name with no argument before it is not the host's mode.
        await Assert.That(Program.ServesStdio([Program.HostArgument])).IsFalse();
    }

    /// <summary>
    /// The suite's stdio client refuses to start the published binary as a person's
    /// start, and starts nothing when it refuses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Added 2026-10-08, after the suite put BrowserAI's page on the
    /// maintainer's screen.</b> The gate on <c>f68ae4cf</c> started the published
    /// binary with no argument from <c>ProtocolSplitTests</c>' new-opening arm: a
    /// person's start, which became a coordinator over the default data root and
    /// opened the page in his browser with the shell at 15:47:57Z
    /// (<see cref="RawStdioClient.RefusalFor"/>). A client that only speaks MCP over
    /// stdio has no use for any other mode, so the refusal costs no arm anything.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-08</b> with <c>RefusalFor</c> answering
    /// <see langword="null"/> for every start, and again with <c>Start</c> no longer
    /// asking it. The second half is read off the source and never run, because a run
    /// of a <c>Start</c> that had stopped asking would itself be the incident.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSuitesStdioClientNeverStartsThePublishedBinaryAsAPersonsStart()
    {
        var refusal = RawStdioClient.RefusalFor(PublishedSlice.Executable, []);

        await Assert.That(refusal).IsNotNull();
        await Assert.That(refusal!).Contains(Program.McpArgument);
        await Assert.That(RawStdioClient.RefusalFor(PublishedSlice.Executable, ["--sessions"])).IsNotNull();
        await Assert.That(RawStdioClient.RefusalFor(PublishedSlice.Executable, ["--write-address", "address.txt"])).IsNotNull();

        // Every mode that serves stdio is started, and so is anything that is not
        // the published binary.
        await Assert.That(RawStdioClient.RefusalFor(PublishedSlice.Executable, PublishedSlice.Mcp)).IsNull();
        await Assert.That(RawStdioClient.RefusalFor(PublishedSlice.Executable, [Program.HostArgument, @"\\.\pipe\x"])).IsNull();
        await Assert.That(RawStdioClient.RefusalFor(PublishedSlice.Executable, [Program.SweepArgument])).IsNull();
        await Assert.That(RawStdioClient.RefusalFor(Path.Combine(Environment.SystemDirectory, "cmd.exe"), [])).IsNull();

        // And Start asks before it starts anything. Read as text and never run:
        // run, a Start that had stopped asking would start a real person's start.
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryLayout.Root.FullName, "tests", "BrowserAI.Tests", "Harness", "RawStdioClient.cs"));
        var start = source.IndexOf("public static RawStdioClient Start(", StringComparison.Ordinal);
        var asks = source.IndexOf("RefusalFor(command, arguments)", start, StringComparison.Ordinal);
        var launches = source.IndexOf("JobLauncher.Start(", start, StringComparison.Ordinal);

        await Assert.That(start).IsGreaterThanOrEqualTo(0);
        await Assert.That(asks).IsGreaterThan(start);
        await Assert.That(launches).IsGreaterThan(asks);
    }

    /// <summary>
    /// The configuration app's embedded manifest declares the three things
    /// without which it has no window, no long paths and no correct scaling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The Common-Controls dependency is the one whose absence presents as
    /// <i>the app starts and nothing happens</i>.</b>
    /// <c>TaskDialogIndirect</c> lives in the side-by-side version 6
    /// <c>comctl32</c>, which a process does not get by default: without the
    /// dependency the loader binds version 5, the export is absent, and the call
    /// fails at run time with <b>no compile-time signal of any kind</b>. Nothing
    /// in this repository could see that until this arm -- it was read by hand,
    /// once. <i>Added 2026-09-16.</i> <i>Corrected 2026-10-03 (previously as written):
    /// the task dialog is deleted with the configuration window. The dependency stays
    /// for the folder picker the browser tab asks the coordinator to open: without
    /// it a process draws its common controls from version 5, with no visual styles,
    /// per Microsoft's "Enabling Visual Styles"
    /// (learn.microsoft.com/windows/win32/controls/cookbook-overview, read
    /// 2026-10-03).</i>
    /// </para>
    /// <para>
    /// <b>Out of the BUILT file, never out of <c>app.manifest</c>.</b> The source
    /// file is what a person edits; a project that stopped carrying
    /// <c>ApplicationManifest</c> would leave it sitting in the tree saying the
    /// right thing about a binary that no longer carries any of it. The published
    /// binary is asserted as well when this machine has one, because that is the
    /// file that ships.
    /// </para>
    /// <para>
    /// <b>The control is the test probe.</b> A reader that had stopped finding
    /// resources would report every property absent, which is indistinguishable
    /// from a binary that declares none -- so the arm also reads a binary that is
    /// <i>known</i> to declare no common controls, and requires the reader to
    /// come back with a manifest that says so and not with nothing. <i>Corrected
    /// 2026-10-08 (previously "The control is the SERVER", whose own manifest
    /// declared no common controls): there is one executable since D7 a, and it
    /// carries the configuration app's manifest, so the control moved to the
    /// suite's own probe, a console binary built beside the test host.</i>
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheOneExecutablesEmbeddedManifestDeclaresCommonControlsLongPathsAndPerMonitorV2()
    {
        foreach (var binary in Candidates("BrowserAI", "BrowserAI.exe", PublishedSlice.Executable))
        {
            var manifest = EmbeddedManifest.Of(binary);

            await Assert.That(manifest is null ? $"{binary} carries no RT_MANIFEST at all" : string.Empty).IsEmpty();

            // The dependency, by name AND by version: a 5.0.0.0 entry would
            // satisfy a name-only check and bind the wrong library.
            var declared = manifest!;

            await Assert.That(declared).Contains("Microsoft.Windows.Common-Controls");
            await Assert.That(declared).Contains("6.0.0.0");
            await Assert.That(declared).Contains("6595b64144ccf1df");

            await Assert.That(declared).Contains("<longPathAware");
            await Assert.That(declared).Contains("true</longPathAware>");

            await Assert.That(declared).Contains("<dpiAwareness");
            await Assert.That(declared).Contains("PerMonitorV2</dpiAwareness>");

            // And never elevation: this product installs per-user precisely so
            // that nothing it does can need a UAC prompt.
            await Assert.That(declared).Contains("asInvoker");
        }

        // ⚠️ THE CONTROL. The test probe is a console binary whose manifest
        // declares no common controls, so a reader that had stopped working cannot
        // look like a binary that simply declares less.
        var probe = EmbeddedManifest.Of(Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe"));

        await Assert.That(probe is null ? "the test probe carries no RT_MANIFEST" : string.Empty).IsEmpty();
        await Assert.That(probe!).Contains("<assembly");
        await Assert.That(probe!.Contains("Microsoft.Windows.Common-Controls", StringComparison.Ordinal)).IsFalse();
    }

    /// <summary>
    /// The published configuration app really does run <c>Main</c> in a
    /// single-threaded apartment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b><c>[STAThread]</c> under NativeAOT was an assumption, not a
    /// measurement, and two things depend on it with no diagnostic if it is
    /// wrong.</b> The folder picker's <c>BIF_NEWDIALOGSTYLE</c> silently falls
    /// back to the pre-Vista dialog on a thread that is not in one -- no error, a
    /// different window -- and the version 6 common controls a task dialog is made
    /// of expect an STA. <i>Added 2026-09-16.</i>
    /// </para>
    /// <para>
    /// <b>Off the PUBLISHED binary, because that is where the question is
    /// real.</b> The suite's own host is an ordinary CoreCLR process and says
    /// nothing about what ILC did with the attribute, so the arm runs
    /// <c>BrowserAI.exe --report</c> -- the application's testable
    /// non-interactive path -- and reads the apartment out of the file it writes.
    /// </para>
    /// <para>
    /// <b>It writes nothing but its own report.</b> <c>--report</c> reads state
    /// and exits; the one side effect is a line in the machine-wide process log,
    /// which every other arm that starts a product child also writes.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePublishedConfigurationAppRunsInASingleThreadedApartment()
    {
        if (!File.Exists(PublishedSlice.AppExecutable))
        {
            throw new FileNotFoundException(
                $"'{PublishedSlice.AppExecutable}' is not there. Publish the one executable: {PublishedSlice.PublishCommand}",
                PublishedSlice.AppExecutable);
        }

        using var output = ScratchDirectory.Create("app-apartment");

        var report = Path.Combine(output.Path, "report.json");

        // ⚠️ CODEX_HOME ON THE CHILD, AND ONLY ON THE CHILD -- 2026-09-24. Since
        // the window reads every client, `--report` runs `codex mcp list --json`,
        // which reads whichever configuration CODEX_HOME names. Reading the
        // maintainer's own is not something an arm should do, and starting his
        // CLI against it is how a file nobody meant to touch acquires a log
        // directory. Set on the child's environment and never on this process's,
        // so no other arm inherits it and this file needs no [NotInParallel].
        var environment = PublishedSlice.InheritedEnvironment();

        environment[RegistrationTests.CodexHomeVariable] =
            Directory.CreateDirectory(Path.Combine(output.Path, "codex")).FullName;

        // ⚠️ ON A PRIVATE DESKTOP SINCE 2026-09-24, Q278 (previously Process.Start
        // with CreateNoWindow = true). This is a Windows-subsystem binary, where
        // the flag does nothing; `--report` is windowless only because Main returns
        // before the dialog is built, which is an ordering inside the app and not a
        // property of the launch. A desktop nobody is looking at keeps that true
        // whatever Main does next. The working directory is this host's, as it was.
        using var desktop = PrivateDesktop.Create("app-report");
        using var job = JobObject.CreateKillOnClose();
        using var process = desktop.Launch(
            job,
            PublishedSlice.AppExecutable,
            ["--report", report],
            Environment.CurrentDirectory,
            environment);

        var drained = Task.WhenAll(
            process.StandardOutput.CopyToAsync(Stream.Null),
            process.StandardError.CopyToAsync(Stream.Null));

        await Assert.That(await process.WaitForExitAsync(TestDefaults.ProcessHang)).IsTrue();
        await drained;

        await Assert.That(process.TryReadExitCode()).IsEqualTo(0);
        await Assert.That(File.Exists(report)).IsTrue();

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(report));

        // Not vacuous: this is a report from THIS build, carrying the field.
        await Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32())
            .IsEqualTo(StatusReport.SchemaVersion);

        await Assert.That(document.RootElement.GetProperty("apartment").GetString()).IsEqualTo("STA");
    }

    /// <summary>
    /// A binary in every place this repository builds one, skipping the places
    /// this machine has not built.
    /// </summary>
    /// <param name="project">The project directory's name.</param>
    /// <param name="executable">The file name.</param>
    /// <param name="published">Where its publish lands.</param>
    /// <returns>Every one that exists, with the Debug build always included.</returns>
    private static IEnumerable<string> Candidates(string project, string executable, string published)
    {
        yield return Built(project, executable);

        if (File.Exists(published))
        {
            yield return published;
        }
    }

    /// <summary>
    /// Where a project's Debug build puts its executable.
    /// </summary>
    private static string Built(string project, string executable)
    {
        var path = Path.Combine(
            RepositoryLayout.Root.FullName, "src", project, "bin", "Debug", "net10.0-windows", executable);

        return File.Exists(path)
            ? path
            : throw new FileNotFoundException(
                $"'{path}' is not there, so this arm cannot read the subsystem of a binary this repository builds on every run. "
                + "Either the project was renamed, in which case update this test, or the build did not produce an executable, "
                + "which is a defect rather than a reason to skip.",
                path);
    }

    /// <summary>
    /// The same fields with the default packing, as the control for the claim
    /// that <c>Pack = 1</c> is what makes the size right.
    /// </summary>
    /// <remarks>
    /// Deliberately <b>not</b> the real declaration with the attribute removed:
    /// a copy cannot be used by mistake, and a reader meeting it here is meeting
    /// a fixture and not a second definition of a shipped type.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct NaturallyPacked
    {
        public uint Size;
        public nint Parent;
        public nint Instance;
        public uint Flags;
        public uint CommonButtons;
        public nint WindowTitle;
        public nint MainIcon;
        public nint MainInstruction;
        public nint Content;
        public uint ButtonCount;
        public nint Buttons;
        public int DefaultButton;
        public uint RadioButtonCount;
        public nint RadioButtons;
        public int DefaultRadioButton;
        public nint VerificationText;
        public nint ExpandedInformation;
        public nint ExpandedControlText;
        public nint CollapsedControlText;
        public nint FooterIcon;
        public nint Footer;
        public nint Callback;
        public nint CallbackData;
        public uint Width;
    }
}
