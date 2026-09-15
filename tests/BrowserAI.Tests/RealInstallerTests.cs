// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BrowserAI.Hosting;
using BrowserAI.Registration;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The real installer, run twice over its own install root, against a data root
/// that must survive both.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one thing the structural scan cannot say.</b>
/// <c>UpdateTests.NoDataPathResolvesUnderAnyInstallRoot</c> holds that BrowserAI
/// never composes a data path under an install root; it says nothing about what
/// <c>Setup.exe</c> actually does to a directory, and the whole preservation
/// decision rests on what it does: <c>install.rs</c> renames a non-empty root to
/// <c>{root}.{random16}</c> and, on success, <b>deletes it</b>. That is not a
/// failure path — it is what a repair install, an overwrite install and a
/// re-run of the same installer all do, and until 2026-09-15 it took 768 MB of
/// provisioned browsers and the session index with it every time.
/// </para>
/// <para>
/// <b>The second install is the test; the first one only creates something to
/// destroy.</b> So the arm asserts a positive control from Velopack's own log —
/// <c>Renaming existing directory</c> — because an installer that quietly
/// skipped the destructive branch would leave the markers alone for the wrong
/// reason and report exactly the same pass.
/// </para>
/// <para>
/// ⚠️ <b>The installer this arm runs is packed under a TEST id, and the shipping
/// one is never executed by the suite at all — 2026-09-15.</b> Velopack writes
/// one Add/Remove Programs key per pack id per user, named for the id and never
/// for the location: `--installto` still rewrites
/// <c>HKCU\…\Uninstall\&lt;packId&gt;</c> to the scratch root, and
/// <c>Update.exe uninstall</c> from that root calls
/// <c>delete_subkey_all(&lt;id&gt;)</c> unconditionally, with no comparison
/// against <c>InstallLocation</c>. So this arm under the shipping id destroys a
/// real install's entry — measured on this machine as <i>no `BrowserAI.app` key
/// after six installer-arm runs</i>, and that was with no real install present
/// to lose. <c>build/New-Release.ps1</c> packs a second installer from the same
/// publish, at the same version, on the same channel, with the id and the output
/// directory as the only deltas.
/// </para>
/// <para>
/// ⚠️ <b>Everything it touches is scratch, and the sandbox is not a
/// convenience.</b> The hooks inherit this process's environment, so
/// <c>CLAUDE_CONFIG_DIR</c> points the registration at a scratch configuration
/// directory — without it the install hook would rewrite the maintainer's own
/// <c>~/.claude.json</c> and the uninstall hook would then remove the entry it
/// found there. <c>BROWSERAI_ROOT</c> does the same for the data root, so the
/// markers this plants are in a directory of its own rather than in the real
/// one. It installs only under <c>--installto</c>, and it uninstalls what it
/// installed.
/// </para>
/// <para>
/// ⚠️ <b><c>[NotInParallel]</c> with no key, which in TUnit means this runs
/// beside nothing at all.</b> <i>Corrected 2026-09-15 (previously
/// <c>[NotInParallel(RegistrationTests.ClientGroup)]</c>, "it shares the MCP
/// client's serialisation key … because the hooks start the real client and
/// that variable is process-wide").</i> A key serialises this arm against the
/// other arms holding the <b>same</b> key, and the readers of a process-wide
/// environment variable are not those arms — <b>they are every arm in the suite
/// that launches a product child</b>, none of which opens a scope and none of
/// which can be enumerated. Measured on the 2026-09-15 release gate, run 1:
/// while this arm held <c>BROWSERAI_ROOT</c> at its own empty scratch data root,
/// three children launched by <c>FileAccessRootTests</c> (×2) and
/// <c>FirefoxSessionTests</c> inherited it, correctly reported first use, started
/// a <b>203.8 MB</b> provisioning download into this arm's scratch root and
/// refused the call — three red arms, none of them this one, and the gate
/// stopped. The key was the defect; exclusivity is the fix, and
/// <see cref="HouseRuleTests.EveryArmInAFileThatOverridesTheEnvironmentRunsBesideNothing"/>
/// is what keeps the next file from re-learning it.
/// </para>
/// <para>
/// <b>The second face of the same race was silent, and closing it is the other
/// half of this arm.</b> <see cref="Unchanged"/> hashed only the files this arm
/// planted, so the foreign download landing elsewhere in the same root passed
/// unnoticed — a byte-identical claim being made about a directory another test
/// was writing into. It now reads the whole tree and names anything that is
/// neither planted nor written by the install itself.
/// </para>
/// </remarks>
[NotInParallel]
internal sealed partial class RealInstallerTests
{
    /// <summary>
    /// A second install over the same root destroys the install root and leaves
    /// the data root byte-identical.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task InstallingTwiceOverOneRootLeavesTheDataRootByteIdentical()
    {
        var setup = SuiteEnvironment.RequireReleaseInstaller();

        using var installRoot = ScratchDirectory.CreateUnderProfile("real-install");
        using var dataRoot = ScratchDirectory.CreateUnderProfile("real-install-data");
        using var clientConfig = ScratchDirectory.Create("real-install-client");
        using var logs = ScratchDirectory.Create("real-install-logs");

        var paths = new LocalAppDataPaths(dataRoot.Path);
        var planted = Plant(paths);

        using var sandbox = new EnvironmentScope(new Dictionary<string, string?>
        {
            [RegistrationTests.ConfigDirectoryVariable] = clientConfig.Path,
            [BrowserAiPaths.AppRootOverride] = dataRoot.Path,
        });

        // ⚠️ THE REAL INSTALL'S OWN ADD/REMOVE ENTRY, READ BEFORE ANYTHING RUNS.
        // This is the entry the shipping pack id would have had rewritten and
        // then deleted, and the whole reason the installer this arm runs is
        // packed under another id. Asserted rather than logged: a claim about a
        // key nobody compared is the shape of claim this repository exists to
        // eliminate. Absent is a perfectly good before-state and must still be
        // absent afterwards.
        var realKeyBefore = ReadUninstallKey($@"{ReleaseLayout.UninstallKeyPath}\{ReleaseLayout.PackId}");

        try
        {
            await InstallTwiceAndUninstall(setup, installRoot, dataRoot, logs, planted);
        }
        finally
        {
            // ⚠️ IN A FINALLY, so a red assertion above does not leave an
            // install, a scratch tree and an Add/Remove entry behind for the
            // next run to trip over. Every step reports rather than throws: this
            // runs on the failure path, and a cleanup that throws replaces the
            // reason the arm went red.
            await ReclaimAsync(installRoot.Path);
        }

        // And the real entry is what it was, byte for byte — or is still absent.
        var realKeyAfter = ReadUninstallKey($@"{ReleaseLayout.UninstallKeyPath}\{ReleaseLayout.PackId}");

        await Assert.That(realKeyAfter).IsEqualTo(realKeyBefore);
    }

    /// <summary>
    /// The installed main executable opens one task dialog, owns no console
    /// window, and closes when it is asked to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is the arm the whole two-binary design was cut for.</b> A
    /// non-silent <c>Setup.exe</c> finishes by starting the main executable with
    /// <c>CREATE_UNICODE_ENVIRONMENT</c> and nothing else, and on 2026-09-15
    /// that put a <b>1506×1490 Windows Terminal window</b> on the user's screen,
    /// serving nobody, for 215 seconds. The window is gone because the main
    /// executable is a Windows-subsystem binary now, and what this asserts is
    /// the visible half of that: one dialog, and nothing else.
    /// </para>
    /// <para>
    /// ⚠️ <b>It installs SILENTLY and launches the binary itself, deliberately.</b>
    /// A non-silent install would put a progress dialog on the maintainer's
    /// screen and hand the start to Velopack — which is the very thing whose
    /// flags cannot be influenced from here. Launching the same file the same
    /// way, from a parent with no window, exercises the property under test and
    /// nothing else.
    /// </para>
    /// <para>
    /// ⚠️ <b>The console check is BY PID, and that is weaker than it looks —
    /// said here rather than left to be discovered.</b> With the default
    /// terminal set to Windows Terminal, a console allocated to a process shows
    /// up as a window owned by <b>Windows Terminal's</b> process, not by ours:
    /// scanning for <c>ConsoleWindowClass</c> is exactly what reported a clean
    /// screen while two windows were on it. So this arm does not claim to detect
    /// a console by looking for its window. What carries that guarantee is
    /// <c>TaskDialogLayoutTests.TheAppIsAWindowBinaryAndTheServerIsAConsoleOne</c>,
    /// which reads the subsystem out of the binary — the cause rather than the
    /// symptom. The by-pid check below is kept because it is free and because it
    /// would catch the one case the subsystem cannot: this process calling
    /// <c>AllocConsole</c> itself.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheInstalledMainExecutableOpensOneDialogAndNoConsoleWindow()
    {
        var setup = SuiteEnvironment.RequireReleaseInstaller();

        using var installRoot = ScratchDirectory.CreateUnderProfile("real-install-window");
        using var dataRoot = ScratchDirectory.CreateUnderProfile("real-install-window-data");
        using var clientConfig = ScratchDirectory.Create("real-install-window-client");
        using var logs = ScratchDirectory.Create("real-install-window-logs");

        using var sandbox = new EnvironmentScope(new Dictionary<string, string?>
        {
            [RegistrationTests.ConfigDirectoryVariable] = clientConfig.Path,
            [BrowserAiPaths.AppRootOverride] = dataRoot.Path,
        });

        try
        {
            await Assert.That(await RunAsync(
                setup, ["--silent", "--log", Path.Combine(logs.Path, "setup.log"), "--installto", installRoot.Path]))
                .IsEqualTo(0);

            var app = Path.Combine(installRoot.Path, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.AppFileName);

            await Assert.That(File.Exists(app)).IsTrue();

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(app)
                {
                    UseShellExecute = false,

                    // The house rule, and it is not decorative here: this arm
                    // runs from a parent that has a console, so an omission
                    // would be invisible in a suite run and visible on the
                    // maintainer's screen.
                    CreateNoWindow = true,
                    WorkingDirectory = installRoot.Path,
                },
            };

            await Assert.That(process.Start()).IsTrue();

            try
            {
                var dialog = await WaitForTheDialogAsync(process.Id);

                await Assert.That(dialog).IsNotEqualTo(nint.Zero);

                var owned = TopLevelWindows.All()
                    .Where(window => TopLevelWindows.ProcessIdOf(window) == process.Id)
                    .Select(TopLevelWindows.ClassNameOf)
                    .ToList();

                // Exactly one VISIBLE top-level window, and it is the dialog.
                // The invisible ones are the input-method windows every GUI
                // process on this machine carries.
                var visible = TopLevelWindows.All()
                    .Where(window => TopLevelWindows.ProcessIdOf(window) == process.Id && TopLevelWindows.IsVisible(window))
                    .Select(TopLevelWindows.ClassNameOf)
                    .ToList();

                await Assert.That(string.Join(", ", visible)).IsEqualTo(TaskDialogWindowClass);

                // And no console window of its own. See the remarks for exactly
                // how much this does and does not prove.
                await Assert.That(owned.Where(name =>
                        name is "ConsoleWindowClass" or "CASCADIA_HOSTING_WINDOW_CLASS" or "PseudoConsoleWindow"))
                    .IsEmpty();

                await Assert.That(TopLevelWindows.Close(dialog)).IsTrue();

                await Assert.That(await WaitForExitAsync(process)).IsTrue();
                await Assert.That(process.ExitCode).IsEqualTo(0);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }

            var update = Path.Combine(installRoot.Path, "Update.exe");

            await Assert.That(File.Exists(update)).IsTrue();
            await Assert.That(await RunAsync(update, ["--uninstall", "--silent"])).IsEqualTo(0);
            await WaitOutTheDeferredRemoval(Path.Combine(installRoot.Path, RegistrationTarget.CurrentDirectoryName));
        }
        finally
        {
            await ReclaimAsync(installRoot.Path);
        }
    }

    /// <summary>The window class a task dialog is.</summary>
    /// <remarks>
    /// <c>#32770</c> is the Windows dialog class, and a task dialog is a dialog.
    /// It is spelled once here so the assertion and the failure message cannot
    /// say different things.
    /// </remarks>
    private const string TaskDialogWindowClass = "#32770";

    /// <summary>
    /// Waits for the dialog to appear, polling rather than sleeping once.
    /// </summary>
    /// <remarks>
    /// <b>A single sleep is what made this flake by hand.</b> Two runs of the
    /// same probe on 2026-09-15 disagreed at a fixed 2.5 s and agreed at 500 ms
    /// when polled, because the window arrives whenever the shell gets round to
    /// it. The bound is a hang detector and not a promptness claim.
    /// </remarks>
    private static async Task<nint> WaitForTheDialogAsync(int processId)
    {
        var deadline = DateTime.UtcNow + TestDefaults.ProcessHang;

        while (DateTime.UtcNow < deadline)
        {
            foreach (var window in TopLevelWindows.All())
            {
                if (TopLevelWindows.ProcessIdOf(window) == processId
                    && TopLevelWindows.IsVisible(window)
                    && string.Equals(TopLevelWindows.ClassNameOf(window), TaskDialogWindowClass, StringComparison.Ordinal))
                {
                    return window;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        return nint.Zero;
    }

    /// <summary>Waits for a process to exit, bounded.</summary>
    private static async Task<bool> WaitForExitAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().WaitAsync(TestDefaults.ProcessHang);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>The body of the arm, so the reclaim below can be a finally.</summary>
    /// <param name="setup">The test-id installer.</param>
    /// <param name="installRoot">The scratch install root.</param>
    /// <param name="dataRoot">The scratch data root.</param>
    /// <param name="logs">Where Velopack's own logs go.</param>
    /// <param name="planted">Each planted path and the SHA-256 it held.</param>
    /// <returns>The assertion task.</returns>
    private static async Task InstallTwiceAndUninstall(
        string setup,
        ScratchDirectory installRoot,
        ScratchDirectory dataRoot,
        ScratchDirectory logs,
        IReadOnlyList<(string Path, string Sha256)> planted)
    {
        // ⚠️ TWICE. The first install creates a non-empty root; the second one is
        // the one that renames it aside and deletes it.
        var first = await RunAsync(setup, ["--silent", "--log", Path.Combine(logs.Path, "setup-1.log"), "--installto", installRoot.Path]);
        var second = await RunAsync(setup, ["--silent", "--log", Path.Combine(logs.Path, "setup-2.log"), "--installto", installRoot.Path]);

        await Assert.That(first).IsEqualTo(0);
        await Assert.That(second).IsEqualTo(0);

        // The positive control, out of Velopack's own log: the destructive
        // branch RAN. Without this, an installer that refused the second install
        // would leave the markers alone and pass.
        var setupLog = await File.ReadAllTextAsync(Path.Combine(logs.Path, "setup-2.log"));

        await Assert.That(setupLog).Contains("Renaming existing directory");

        // The install root really was replaced, and BOTH binaries are inside
        // current\ — the configuration app, which is what the stub beside that
        // directory points at, and the MCP server, which is what a client is
        // actually given.
        //
        // ⚠️ Widened 2026-09-15 (previously the app's name alone, with the
        // comment "the binary is where a client is pointed:
        // <root>\current\BrowserAI.exe, never the stub beside it"). That
        // sentence named the right file for the wrong reason from the day the
        // names swapped: a client is pointed at the SERVER, composed from the
        // app's directory and refused if it is not there — so an install with
        // one of the two would register nothing and say so, which is a failure
        // this arm would otherwise have watched happen and called a pass.
        foreach (var executable in new[] { RegistrationTarget.AppFileName, RegistrationTarget.ServerFileName })
        {
            await Assert.That(File.Exists(
                Path.Combine(installRoot.Path, RegistrationTarget.CurrentDirectoryName, executable))).IsTrue();
        }

        // And the registration the hook wrote names the SERVER.
        var written = await File.ReadAllTextAsync(RegistrationRecord.PathFor(dataRoot.Path));

        await Assert.That(written).Contains(RegistrationTarget.ServerFileName);

        await Unchanged(dataRoot.Path, planted);

        // The hook wrote its record into the DATA root, which is the other half
        // of the same decision: it is still there after an install that deleted
        // the install root twice over.
        await Assert.That(File.Exists(RegistrationRecord.PathFor(dataRoot.Path))).IsTrue();

        // ---- And uninstall, in the same sandbox --------------------------------
        var update = Path.Combine(installRoot.Path, "Update.exe");

        await Assert.That(File.Exists(update)).IsTrue();

        var removed = await RunAsync(update, ["--uninstall", "--silent"]);

        await Assert.That(removed).IsEqualTo(0);

        // Velopack schedules the install root's own removal for after Update.exe
        // exits, so the directory goes a moment later than the process does.
        await WaitOutTheDeferredRemoval(Path.Combine(installRoot.Path, RegistrationTarget.CurrentDirectoryName));

        // ⚠️ THE CLAIM. A silent uninstall keeps the data root, and every byte of
        // it is the byte that was planted.
        await Unchanged(dataRoot.Path, planted);
    }

    /// <summary>
    /// Takes back whatever the arm left: the install, the scratch root, and the
    /// Add/Remove entry <b>this</b> install created.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Best effort, and every step is independent of the one before it.</b>
    /// This runs on the failure path — that is what it is for — so it must work
    /// when the install half-happened, when <c>Update.exe</c> is missing, and
    /// when the uninstall already ran. Nothing here throws and nothing here
    /// asserts: the arm's own red is the report, and a cleanup that replaced it
    /// with its own would hide the finding.
    /// </para>
    /// <para>
    /// ⚠️ <b>The key it removes is the TEST id's and only ever that.</b> It is
    /// the one Velopack wrote for this install, under an id nothing else on the
    /// machine uses; the shipping id's key is read by this arm and never
    /// written. A leftover would otherwise sit in Settings pointing at a scratch
    /// directory that is gone, and would refuse the capability on the next run —
    /// which is correct, and is the state this exists to stop happening.
    /// </para>
    /// </remarks>
    /// <param name="installRoot">The scratch install root.</param>
    /// <returns>The reclaim.</returns>
    private static async Task ReclaimAsync(string installRoot)
    {
        var update = Path.Combine(installRoot, "Update.exe");

        if (File.Exists(update))
        {
            try
            {
                _ = await RunAsync(update, ["--uninstall", "--silent"]);
                await WaitOutTheDeferredRemoval(Path.Combine(installRoot, RegistrationTarget.CurrentDirectoryName));
            }
#pragma warning disable CA1031 // A cleanup on the failure path reports nothing and must replace no finding.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }

        _ = ScratchDirectory.RemoveTree(installRoot);

        try
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(ReleaseLayout.TestUninstallKey, throwOnMissingSubKey: false);
        }
#pragma warning disable CA1031 // Same: the next run's capability check is what reports a key that would not go.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>
    /// One uninstall entry as a comparable string: every value, its kind and its
    /// content, in a fixed order.
    /// </summary>
    /// <remarks>
    /// <b>A string rather than a snapshot object, so the assertion's failure
    /// message shows what moved.</b> An absent key answers <c>&lt;absent&gt;</c>
    /// rather than empty, because a key that exists carrying nothing and a key
    /// that does not exist are different states and this arm has to be able to
    /// tell them apart.
    /// </remarks>
    /// <param name="path">The key, relative to <c>HKEY_CURRENT_USER</c>.</param>
    /// <returns>Its contents, or <c>&lt;absent&gt;</c>.</returns>
    private static string ReadUninstallKey(string path)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path);

        if (key is null)
        {
            return "<absent>";
        }

        var read = new StringBuilder();

        foreach (var name in key.GetValueNames().Order(StringComparer.Ordinal))
        {
            var value = key.GetValue(name, null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames);

            _ = read.Append(CultureInfo.InvariantCulture, $"{name}\t{key.GetValueKind(name)}\t{value}\n");
        }

        foreach (var child in key.GetSubKeyNames().Order(StringComparer.Ordinal))
        {
            _ = read.Append(CultureInfo.InvariantCulture, $"[{child}]\n");
        }

        return read.ToString();
    }

    /// <summary>
    /// The shipping pack and the suite's pack are the same package under two
    /// names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The arm above proves a property of an installer nobody ships, so this
    /// is what makes that property a statement about the one that does.</b> The
    /// two packs come from one publish directory in one script run, at the same
    /// version and channel, with the id and the output directory as the only
    /// arguments that differ — and <i>that</i> is a scan over the script. This is
    /// the bytes.
    /// </para>
    /// <para>
    /// <b>What may differ is named by a rule rather than by a list.</b> An entry
    /// whose bytes differ has to <i>mention the id</i>, in UTF-8 or in UTF-16,
    /// in one of the two packages — which is what a <c>.nuspec</c>, a Velopack
    /// manifest and a stub's embedded metadata all do. Anything else differing
    /// means the two packs were not built from one publish, and a list of
    /// expected file names would have gone stale the first time upstream added
    /// one.
    /// </para>
    /// <para>
    /// <b>The comparison is watched in both directions over synthetic archives</b>,
    /// because a real pair that happens to agree is indistinguishable from a
    /// comparison that stopped looking.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSuitesPackAndTheShippingPackDifferOnlyWhereTheIdAppears()
    {
        _ = SuiteEnvironment.RequirePackagedRelease();
        _ = SuiteEnvironment.RequireReleaseInstaller();

        var shipping = ReleaseLayout.FullPackage(test: false);
        var mine = ReleaseLayout.FullPackage(test: true);

        await Assert.That(shipping is null ? "no shipping .nupkg" : string.Empty).IsEmpty();
        await Assert.That(mine is null ? "no test .nupkg" : string.Empty).IsEmpty();

        using var a = await ZipFile.OpenReadAsync(shipping!.FullName);
        using var b = await ZipFile.OpenReadAsync(mine!.FullName);

        var left = Entries(a, ReleaseLayout.PackId);
        var right = Entries(b, ReleaseLayout.TestPackId);

        // The same files, once the id is taken out of their names.
        await Assert.That(string.Join(", ", left.Keys.Except(right.Keys).Order(StringComparer.Ordinal))).IsEmpty();
        await Assert.That(string.Join(", ", right.Keys.Except(left.Keys).Order(StringComparer.Ordinal))).IsEmpty();

        var (differing, offenders) = Compare(left, right, ReleaseLayout.PackId, ReleaseLayout.TestPackId);

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // Not vacuous: the id is in there somewhere, so something must differ.
        await Assert.That(differing).IsGreaterThan(0);

        // ⚠️ THE CONTROL, over archives this test composes. A byte that differs
        // in an entry naming neither id is reported; one in an entry that names
        // an id is not. Without it, a comparison that had stopped reading would
        // report the two packages identical and pass.
        var plain = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["a.txt"] = Encoding.UTF8.GetBytes("nothing to see") };
        var doctored = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["a.txt"] = Encoding.UTF8.GetBytes("nothing to sea") };
        var named = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["a.txt"] = Encoding.UTF8.GetBytes($"id is {ReleaseLayout.PackId}") };
        var namedToo = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["a.txt"] = Encoding.UTF8.GetBytes($"id is {ReleaseLayout.TestPackId}") };

        var (_, caught) = Compare(plain, doctored, ReleaseLayout.PackId, ReleaseLayout.TestPackId);
        await Assert.That(caught.Count).IsEqualTo(1);

        var (moved, spared) = Compare(named, namedToo, ReleaseLayout.PackId, ReleaseLayout.TestPackId);
        await Assert.That(spared.Count).IsEqualTo(0);
        await Assert.That(moved).IsEqualTo(1);
    }

    /// <summary>
    /// The packed release carries both executables and names the configuration
    /// app as its main one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Read out of the package rather than out of the script that wrote
    /// it.</b> <c>ReleaseScriptTests</c> holds what
    /// <c>build/New-Release.ps1</c> passes; this holds what came out the other
    /// end, and the two are different claims — a `vpk` that silently ignored an
    /// argument would satisfy the first and fail this.
    /// </para>
    /// <para>
    /// <b><c>mainExe</c> is the field that decides five things</b>: which binary
    /// <c>Setup.exe</c> starts after a non-silent install, what the root stub is
    /// called, what <c>Update.exe start</c> launches, which binary every hook
    /// runs on, and what the Start Menu shortcut points at. Naming the
    /// console-subsystem server there is the defect the whole two-binary design
    /// exists to remove, and it would present as a terminal window on somebody
    /// else's screen.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePackedReleaseNamesTheAppAsItsMainExeAndCarriesBothBinaries()
    {
        _ = SuiteEnvironment.RequirePackagedRelease();

        var package = ReleaseLayout.FullPackage(test: false);

        await Assert.That(package is null ? "no shipping .nupkg" : string.Empty).IsEmpty();

        using var archive = await ZipFile.OpenReadAsync(package!.FullName);

        var nuspec = archive.Entries.SingleOrDefault(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));

        await Assert.That(nuspec is null ? "no .nuspec in the package" : string.Empty).IsEmpty();

        string text;

        using (var stream = await nuspec!.OpenAsync())
        using (var reader = new StreamReader(stream))
        {
            text = await reader.ReadToEndAsync();
        }

        await Assert.That(text).Contains($"<mainExe>{RegistrationTarget.AppFileName}</mainExe>");
        await Assert.That(text).DoesNotContain($"<mainExe>{RegistrationTarget.ServerFileName}</mainExe>");
        await Assert.That(text).Contains("<shortcutLocations>StartMenuRoot</shortcutLocations>");

        // Both binaries, at the root of the application directory. The stub is
        // a third file and is Velopack's, not ours.
        var app = archive.Entries.Select(entry => entry.FullName).ToList();

        foreach (var executable in new[] { RegistrationTarget.AppFileName, RegistrationTarget.ServerFileName })
        {
            await Assert.That(app).Contains($"lib/app/{executable}");
        }

        // And the payload the server needs is in there with them, which is what
        // makes the package an install rather than two executables.
        await Assert.That(app).Contains("lib/app/payload/payload.json");
    }

    /// <summary>Every entry of one package, keyed on its name with the id removed.</summary>
    /// <param name="archive">The package.</param>
    /// <param name="id">The pack id to take out of the names.</param>
    /// <returns>The entries.</returns>
    private static Dictionary<string, byte[]> Entries(ZipArchive archive, string id)
    {
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            using var bytes = new MemoryStream();

            stream.CopyTo(bytes);
            entries[entry.FullName.Replace(id, "<id>", StringComparison.Ordinal)] = bytes.ToArray();
        }

        return entries;
    }

    /// <summary>
    /// Compares two packages' entries and reports the ones that differ without
    /// mentioning either id.
    /// </summary>
    /// <param name="left">The shipping package's entries.</param>
    /// <param name="right">The test package's entries.</param>
    /// <param name="id">The shipping pack id.</param>
    /// <param name="testId">The test pack id.</param>
    /// <returns>How many entries differed, and which of those are offences.</returns>
    private static (int Differing, List<string> Offenders) Compare(
        Dictionary<string, byte[]> left,
        Dictionary<string, byte[]> right,
        string id,
        string testId)
    {
        var differing = 0;
        var offenders = new List<string>();

        foreach (var (name, bytes) in left.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!right.TryGetValue(name, out var other) || bytes.AsSpan().SequenceEqual(other))
            {
                continue;
            }

            differing++;

            if (!Mentions(bytes, id) && !Mentions(bytes, testId) && !Mentions(other, id) && !Mentions(other, testId))
            {
                offenders.Add(
                    $"{name} differs ({bytes.Length} vs {other.Length} bytes) and neither copy mentions '{id}' or '{testId}',"
                    + " so the two packs did not come from one publish");
            }
        }

        return (differing, offenders);
    }

    /// <summary>Whether some bytes carry a string, in UTF-8 or in UTF-16.</summary>
    /// <param name="bytes">The entry.</param>
    /// <param name="text">The string.</param>
    /// <returns>Whether it is in there.</returns>
    private static bool Mentions(byte[] bytes, string text) =>
        bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(text)) >= 0
        || bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes(text)) >= 0;

    /// <summary>Plants files whose content is their own name, and records their hashes.</summary>
    /// <param name="paths">The scratch data seam.</param>
    /// <returns>Each planted path and the SHA-256 it held.</returns>
    private static List<(string Path, string Sha256)> Plant(LocalAppDataPaths paths)
    {
        var planted = new List<(string Path, string Sha256)>();

        foreach (var (directory, name) in new[]
        {
            (paths.BrowsersDirectory, "chromium-0000.marker"),
            (paths.IndexDirectory, "0123456789abcdef.json"),
            (paths.LogDirectory, "browserai-planted.log"),
            (paths.RootAppDir, "a-file-at-the-root.txt"),
        })
        {
            _ = Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, name);

            File.WriteAllText(path, $"planted by the suite at {DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)} — {path}");
            planted.Add((path, Hash(path)));
        }

        return planted;
    }

    /// <summary>
    /// The data root holds the planted files, byte for byte, and nothing but
    /// them and what the install itself wrote.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The whole tree, not just the planted paths, and the difference is a
    /// measured one.</b> <i>Corrected 2026-09-15 (previously the hash loop
    /// alone).</i> Hashing what this arm planted answers *"was anything of mine
    /// touched"* and cannot answer *"is this still my directory"* — and on the
    /// 2026-09-15 release gate it was not: three children of other arms had
    /// inherited <c>BROWSERAI_ROOT</c> and were writing a 203.8 MB Chromium
    /// download into <c>browsers\chromium-1244</c> while this arm reported the
    /// root byte-identical. It passed, and nothing would have said so.
    /// </para>
    /// <para>
    /// <b>Watched red against a live reproduction rather than only a plant.</b>
    /// A full suite run on 2026-09-15 with the keyed attribute deliberately put
    /// back reproduced the race and this assertion named <b>sixteen</b> foreign
    /// files in the data root: <c>browsers\reinstall.lock</c>, three
    /// <c>index\</c> entries, three <c>instances\{pid}-{guid}\</c> directories
    /// carrying <c>instance.live</c> and two Playwright configuration files
    /// each, and three <c>live\</c> markers — every one of them written by
    /// another arm's product child that had inherited <c>BROWSERAI_ROOT</c>. The
    /// hash loop alone had reported that same directory unchanged.
    /// </para>
    /// <para>
    /// <b>What the install is entitled to leave, named rather than globbed
    /// loosely:</b> <c>mcp-registration.json</c> at the root, which the hook
    /// writes and which the assertion above requires; and the hook's own rolled
    /// process log, <c>logs\browserai-{yyyyMMdd}-{nnn}.log</c>, matched on its
    /// real shape so that a foreign file dropped into <c>logs\</c> under some
    /// other name is still an offence. Everything else is named with its size.
    /// </para>
    /// </remarks>
    /// <param name="dataRoot">The scratch data root.</param>
    /// <param name="planted">Each planted path and the SHA-256 it held.</param>
    /// <returns>The assertion task.</returns>
    private static async Task Unchanged(string dataRoot, IReadOnlyList<(string Path, string Sha256)> planted)
    {
        var moved = new List<string>();

        foreach (var (path, sha) in planted)
        {
            if (!File.Exists(path))
            {
                moved.Add($"{path} is gone");
                continue;
            }

            var now = Hash(path);

            if (!string.Equals(now, sha, StringComparison.Ordinal))
            {
                moved.Add($"{path} was {sha} and is now {now}");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, moved)).IsEmpty();

        var known = planted
            .Select(entry => entry.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var foreign = Directory.EnumerateFiles(dataRoot, "*", SearchOption.AllDirectories)
            .Where(path => !known.Contains(path))
            .Where(path => !WrittenByTheInstall(dataRoot, path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => $"{Path.GetRelativePath(dataRoot, path)} ({new FileInfo(path).Length} bytes) is in the data root and neither this arm nor the install put it there")
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, foreign)).IsEmpty();
    }

    /// <summary>Whether a file in the data root is one the install itself wrote.</summary>
    /// <param name="dataRoot">The scratch data root.</param>
    /// <param name="path">A file somewhere beneath it.</param>
    /// <returns><see langword="true"/> when the install is entitled to have left it.</returns>
    private static bool WrittenByTheInstall(string dataRoot, string path)
    {
        var relative = Path.GetRelativePath(dataRoot, path);

        if (string.Equals(relative, RegistrationRecord.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Length is 2
            && segments[0].Equals("logs", StringComparison.OrdinalIgnoreCase)
            && RolledProcessLog().IsMatch(segments[1]);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static async Task<int> RunAsync(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,

            // The house rule for every launch in this tree: redirecting the
            // streams does not suppress the console, and from a windowless test
            // host each omission puts a terminal on the maintainer's screen.
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;

        // Both streams are drained before the wait, which is the pairing the
        // banned timed overload exists to protect: a process whose output is not
        // read can fill a pipe and never exit.
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync().WaitAsync(TestDefaults.ProcessHang);
        _ = await output;
        _ = await error;

        return process.ExitCode;
    }

    /// <summary>
    /// Waits for Velopack's own deferred removal of the install root.
    /// </summary>
    /// <remarks>
    /// <b>A hang detector, not a promptness claim.</b> <c>Update.exe</c> cannot
    /// delete the directory it is running out of, so it schedules the removal and
    /// exits; the bound is <see cref="TestDefaults.ProcessHang"/> because what is
    /// being waited for is another process's teardown, and the only number that
    /// would be wrong here is one invented at this line.
    /// </remarks>
    /// <param name="directory">The directory that must go.</param>
    private static async Task WaitOutTheDeferredRemoval(string directory)
    {
        var deadline = DateTime.UtcNow + TestDefaults.ProcessHang;

        while (Directory.Exists(directory) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
        }
    }

    /// <summary>The hook's own rolled process log: <c>browserai-{yyyyMMdd}-{nnn}.log</c>.</summary>
    /// <remarks>
    /// The shape <c>RollingFileWriter</c> composes, rather than
    /// <c>browserai-*.log</c>. The looser glob would also match this arm's own
    /// planted <c>browserai-planted.log</c> — which is checked by hash above and
    /// must not be exempted here — and would let any foreign file called
    /// <c>browserai-anything.log</c> through.
    /// </remarks>
    [GeneratedRegex(@"^browserai-\d{8}-\d{3}\.log$", RegexOptions.IgnoreCase)]
    private static partial Regex RolledProcessLog();
}
