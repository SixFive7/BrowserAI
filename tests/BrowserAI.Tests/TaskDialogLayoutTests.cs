// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using BrowserAI.App.Interop;
using BrowserAI.App;
using BrowserAI.Registration;
using BrowserAI.Runtime;
using BrowserAI.Tests.Harness;
using W = Windows.Win32;

namespace BrowserAI.Tests;

/// <summary>
/// The two hand-written task dialog structures, against Microsoft's own
/// metadata -- and the subsystem each shipped executable declares.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b><c>TASKDIALOGCONFIG</c> is <c>#pragma pack(1)</c>, and that is the one
/// fact this whole file exists for.</b> The natural C# layout pads every pointer
/// to eight bytes and produces a 184-byte structure that Windows reads as though
/// it were the 160-byte one: every field after the first mismatch means
/// something else, and the failure is not a diagnostic -- the call returns
/// <c>E_INVALIDARG</c>, or renders a dialog whose title is its content. That is
/// the classic way to get the raw task dialog wrong, and it is checked against
/// the vendor and not against the last person who read the header.
/// </para>
/// <para>
/// <b>The literal is written out as well as compared</b>, for the same reason
/// <c>InteropLayoutTests</c> does it: comparing only the two would move both
/// sides at once if a future <c>CsWin32</c> generated a different shape, and
/// report agreement. 160 and 12 are the numbers this repository measured on
/// 2026-09-15, on x64.
/// </para>
/// </remarks>
internal sealed class TaskDialogLayoutTests
{
    /// <summary>
    /// The configuration structure is the size Windows says, and the constant
    /// the marshalling reads is that size too.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheDialogConfigurationIsPackedTheWayWindowsPacksIt()
    {
        var ours = Marshal.SizeOf<TaskDialogInterop.TaskDialogConfig>();
        var windows = Marshal.SizeOf<W.UI.Controls.TASKDIALOGCONFIG>();

        await Assert.That(ours).IsEqualTo(TaskDialogInterop.ConfigSize);
        await Assert.That(ours).IsEqualTo(windows);
        await Assert.That(ours).IsEqualTo(160);

        // ⚠️ The positive control for the claim above: a naturally packed
        // version of the same fields is a DIFFERENT size, so this arm is
        // capable of failing. Without it, a `Pack = 1` silently dropped from
        // the declaration would have to be caught by the 160 alone -- which it
        // would be, but nothing would say why the number was chosen.
        await Assert.That(Marshal.SizeOf<NaturallyPacked>()).IsNotEqualTo(ours);
    }

    /// <summary>One button is the size Windows says.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task OneButtonIsTheSizeWindowsSaysItIs()
    {
        var ours = Marshal.SizeOf<TaskDialogInterop.TaskDialogButton>();

        await Assert.That(ours).IsEqualTo(TaskDialogInterop.ButtonSize);
        await Assert.That(ours).IsEqualTo(Marshal.SizeOf<W.UI.Controls.TASKDIALOG_BUTTON>());
        await Assert.That(ours).IsEqualTo(12);
    }

    /// <summary>
    /// Every field sits where Windows puts it, not merely the total.
    /// </summary>
    /// <remarks>
    /// <b>A size that agrees says nothing about a field that moved</b>: two
    /// pointers swapped leave the total unchanged and turn the window title into
    /// the instruction. The offsets are read out of both structures by name, so
    /// this is the assertion that would catch a reordering.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryFieldSitsWhereWindowsPutsIt()
    {
        (string Ours, string Windows)[] fields =
        [
            (nameof(TaskDialogInterop.TaskDialogConfig.Size), "cbSize"),
            (nameof(TaskDialogInterop.TaskDialogConfig.Parent), "hwndParent"),
            (nameof(TaskDialogInterop.TaskDialogConfig.Instance), "hInstance"),
            (nameof(TaskDialogInterop.TaskDialogConfig.Flags), "dwFlags"),
            (nameof(TaskDialogInterop.TaskDialogConfig.CommonButtons), "dwCommonButtons"),
            (nameof(TaskDialogInterop.TaskDialogConfig.WindowTitle), "pszWindowTitle"),
            (nameof(TaskDialogInterop.TaskDialogConfig.MainInstruction), "pszMainInstruction"),
            (nameof(TaskDialogInterop.TaskDialogConfig.Content), "pszContent"),
            (nameof(TaskDialogInterop.TaskDialogConfig.ButtonCount), "cButtons"),
            (nameof(TaskDialogInterop.TaskDialogConfig.Buttons), "pButtons"),
            (nameof(TaskDialogInterop.TaskDialogConfig.DefaultButton), "nDefaultButton"),
            (nameof(TaskDialogInterop.TaskDialogConfig.RadioButtonCount), "cRadioButtons"),
            (nameof(TaskDialogInterop.TaskDialogConfig.RadioButtons), "pRadioButtons"),
            (nameof(TaskDialogInterop.TaskDialogConfig.DefaultRadioButton), "nDefaultRadioButton"),
            (nameof(TaskDialogInterop.TaskDialogConfig.VerificationText), "pszVerificationText"),
            (nameof(TaskDialogInterop.TaskDialogConfig.ExpandedInformation), "pszExpandedInformation"),
            (nameof(TaskDialogInterop.TaskDialogConfig.ExpandedControlText), "pszExpandedControlText"),
            (nameof(TaskDialogInterop.TaskDialogConfig.CollapsedControlText), "pszCollapsedControlText"),
            (nameof(TaskDialogInterop.TaskDialogConfig.Footer), "pszFooter"),
            (nameof(TaskDialogInterop.TaskDialogConfig.Callback), "pfCallback"),
            (nameof(TaskDialogInterop.TaskDialogConfig.CallbackData), "lpCallbackData"),
            (nameof(TaskDialogInterop.TaskDialogConfig.Width), "cxWidth"),
        ];

        // Not empty, and not a subset somebody trimmed: the two unions are the
        // only fields deliberately absent from the list.
        await Assert.That(fields.Length).IsEqualTo(22);

        foreach (var (ours, windows) in fields)
        {
            await Assert.That(Marshal.OffsetOf<TaskDialogInterop.TaskDialogConfig>(ours))
                .IsEqualTo(Marshal.OffsetOf<W.UI.Controls.TASKDIALOGCONFIG>(windows));
        }
    }

    /// <summary>
    /// The configuration app is a Windows-subsystem binary and the server is a
    /// console one, read out of the executables themselves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is the property the whole two-binary design rests on.</b> A
    /// non-silent <c>Setup.exe</c> starts the main executable with
    /// <c>CREATE_UNICODE_ENVIRONMENT</c> and nothing else; a console-subsystem
    /// binary started that way from a windowless parent is given a console, and
    /// on this machine that was a Windows Terminal window over the user's work
    /// serving nobody for 215 seconds. A Windows-subsystem binary is never
    /// allocated one.
    /// </para>
    /// <para>
    /// <b>The server's half matters just as much and in the other direction.</b>
    /// A client speaks to it over stdio, and
    /// <c>RegistrationTarget</c> refuses to register a file at the server's name
    /// that is not a console binary -- so a server accidentally built
    /// <c>WinExe</c> would install fine and register nothing.
    /// </para>
    /// <para>
    /// <b>Read off the Debug outputs, which always exist</b>, because the
    /// subsystem comes from <c>OutputType</c> and is identical in every
    /// configuration. The published AOT binaries are checked the same way when
    /// they are present, which is what would catch a link that did not honour
    /// it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheAppIsAWindowBinaryAndTheServerIsAConsoleOne()
    {
        await Assert.That(PeSubsystem.Of(Built("BrowserAI.App", "BrowserAI.exe")))
            .IsEqualTo(PeSubsystem.WindowsGui);

        await Assert.That(PeSubsystem.Of(Built("BrowserAI", "BrowserAI.Server.exe")))
            .IsEqualTo(PeSubsystem.WindowsCui);

        if (File.Exists(PublishedSlice.Executable))
        {
            await Assert.That(PeSubsystem.Of(PublishedSlice.Executable)).IsEqualTo(PeSubsystem.WindowsCui);
        }

        if (File.Exists(PublishedSlice.AppExecutable))
        {
            await Assert.That(PeSubsystem.Of(PublishedSlice.AppExecutable)).IsEqualTo(PeSubsystem.WindowsGui);
        }
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
    /// once. <i>Added 2026-09-16.</i>
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
    /// <b>The control is the SERVER.</b> A reader that had stopped finding
    /// resources would report every property absent, which is indistinguishable
    /// from a binary that declares none -- so the arm also reads a binary that is
    /// <i>known</i> to declare no common controls, and requires the reader to
    /// come back with a manifest that says so and not with nothing.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheAppsEmbeddedManifestDeclaresCommonControlsLongPathsAndPerMonitorV2()
    {
        foreach (var binary in Candidates("BrowserAI.App", "BrowserAI.exe", PublishedSlice.AppExecutable))
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

        // ⚠️ THE CONTROL. The server is a console binary with a manifest of its
        // own that declares no common controls, so a reader that had stopped
        // working cannot look like a binary that simply declares less.
        var server = EmbeddedManifest.Of(Built("BrowserAI", "BrowserAI.Server.exe"));

        await Assert.That(server is null ? "the server carries no RT_MANIFEST" : string.Empty).IsEmpty();
        await Assert.That(server!).Contains("<assembly");
        await Assert.That(server!.Contains("Microsoft.Windows.Common-Controls", StringComparison.Ordinal)).IsFalse();
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
                $"'{PublishedSlice.AppExecutable}' is not there. Publish the configuration app: "
                + "dotnet publish src/BrowserAI.App/BrowserAI.App.csproj -c Release -r win-x64",
                PublishedSlice.AppExecutable);
        }

        using var output = ScratchDirectory.Create("app-apartment");

        var report = Path.Combine(output.Path, "report.json");

        var start = new ProcessStartInfo(PublishedSlice.AppExecutable)
        {
            ArgumentList = { "--report", report },
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // ⚠️ CODEX_HOME ON THE CHILD, AND ONLY ON THE CHILD -- 2026-09-24. Since
        // the window reads every client, `--report` runs `codex mcp list --json`,
        // which reads whichever configuration CODEX_HOME names. Reading the
        // maintainer's own is not something an arm should do, and starting his
        // CLI against it is how a file nobody meant to touch acquires a log
        // directory. Set on the child's environment and never on this process's,
        // so no other arm inherits it and this file needs no [NotInParallel].
        start.Environment[CodexRegistration.HomeVariable] =
            Directory.CreateDirectory(Path.Combine(output.Path, "codex")).FullName;

        using var process = Process.Start(start);

        await Assert.That(process is null ? "the published app would not start" : string.Empty).IsEmpty();

        await process!.WaitForExitAsync().WaitAsync(TestDefaults.ProcessHang);

        await Assert.That(process.ExitCode).IsEqualTo(0);
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
