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
using BrowserAI.Updates;

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
/// failure path -- it is what a repair install, an overwrite install and a
/// re-run of the same installer all do, and until 2026-09-15 it took 768 MB of
/// provisioned browsers and the session index with it every time.
/// </para>
/// <para>
/// <b>The second install is the test; the first one only creates something to
/// destroy.</b> So the arm asserts a positive control from Velopack's own log --
/// <c>Renaming existing directory</c> -- because an installer that quietly
/// skipped the destructive branch would leave the markers alone for the wrong
/// reason and report exactly the same pass.
/// </para>
/// <para>
/// ⚠️ <b>The installer this arm runs is packed under a TEST id, and the shipping
/// one is never executed by the suite at all -- 2026-09-15.</b> Velopack writes
/// one Add/Remove Programs key per pack id per user, named for the id and never
/// for the location: `--installto` still rewrites
/// <c>HKCU\...\Uninstall\&lt;packId&gt;</c> to the scratch root, and
/// <c>Update.exe uninstall</c> from that root calls
/// <c>delete_subkey_all(&lt;id&gt;)</c> unconditionally, with no comparison
/// against <c>InstallLocation</c>. So this arm under the shipping id destroys a
/// real install's entry -- measured on this machine as <i>no `BrowserAI.app` key
/// after six installer-arm runs</i>, and that was with no real install present
/// to lose. <c>build/New-Release.ps1</c> packs a second installer from the same
/// publish, at the same version, on the same channel, with the id, the title and
/// the output directory as the only deltas.
/// </para>
/// <para>
/// ⚠️ <b>And the TITLE, which is the same defect one file later -- 2026-09-16.</b>
/// <i>Corrected 2026-09-16 (previously "with the id and the output directory as
/// the only deltas")</i>: Velopack names the Start Menu shortcut
/// <c>&lt;packTitle&gt;.lnk</c> and not <c>&lt;packId&gt;.lnk</c>, does not
/// gate shortcut creation on <c>--silent</c>, and removes shortcuts by target at
/// uninstall -- so the id split left the two packs still sharing one
/// <c>BrowserAI.lnk</c>, which this arm repointed at its scratch root and its own
/// uninstall then deleted. The suite's pack is titled <c>BrowserAI (suite)</c>,
/// and the arm reads the user's Start Menu before and after for the same reason
/// it reads the Add/Remove key.
/// </para>
/// <para>
/// ⚠️ <b>Everything it touches is scratch, and the sandbox is not a
/// convenience.</b> The hooks inherit this process's environment, so
/// <c>CLAUDE_CONFIG_DIR</c> points the registration at a scratch configuration
/// directory -- without it the install hook would rewrite the maintainer's own
/// <c>~/.claude.json</c> and the uninstall hook would then remove the entry it
/// found there. <c>BROWSERAI_ROOT</c> does the same for the data root, so the
/// markers this plants are in a directory of its own and not in the real
/// one. It installs only under <c>--installto</c>, and it uninstalls what it
/// installed.
/// </para>
/// <para>
/// ⚠️ <b><c>[NotInParallel]</c> with no key, which in TUnit means this runs
/// beside nothing at all.</b> <i>Corrected 2026-09-15 (previously
/// <c>[NotInParallel(RegistrationTests.ClientGroup)]</c>, "it shares the MCP
/// client's serialisation key ... because the hooks start the real client and
/// that variable is process-wide").</i> A key serialises this arm against the
/// other arms holding the <b>same</b> key, and the readers of a process-wide
/// environment variable are not those arms -- <b>they are every arm in the suite
/// that launches a product child</b>, none of which opens a scope and none of
/// which can be enumerated. Measured on the 2026-09-15 release gate, run 1:
/// while this arm held <c>BROWSERAI_ROOT</c> at its own empty scratch data root,
/// three children launched by <c>FileAccessRootTests</c> (×2) and
/// <c>FirefoxSessionTests</c> inherited it, correctly reported first use, started
/// a <b>203.8 MB</b> provisioning download into this arm's scratch root and
/// refused the call -- three red arms, none of them this one, and the gate
/// stopped. The key was the defect; exclusivity is the fix, and
/// <see cref="HouseRuleTests.EveryArmInAFileThatOverridesTheEnvironmentRunsBesideNothing"/>
/// is what keeps the next file from re-learning it.
/// </para>
/// <para>
/// <b>The second face of the same race was silent, and closing it is the other
/// half of this arm.</b> <see cref="Unchanged"/> hashed only the files this arm
/// planted, so the foreign download landing elsewhere in the same root passed
/// unnoticed -- a byte-identical claim being made about a directory another test
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
            [RegistrationTests.ConfigDirectoryVariable] = OnboardedClientConfig.Seed(clientConfig.Path),
            [BrowserAiPaths.AppRootOverride] = dataRoot.Path,
        });

        // ⚠️ THE REAL INSTALL'S OWN ADD/REMOVE ENTRY, READ BEFORE ANYTHING RUNS.
        // This is the entry the shipping pack id would have had rewritten and
        // then deleted, and the whole reason the installer this arm runs is
        // packed under another id. Asserted, not logged: a claim about a
        // key nobody compared is the shape of claim this repository exists to
        // eliminate. Absent is a perfectly good before-state and must still be
        // absent afterwards.
        var realKeyBefore = ReadUninstallKey($@"{ReleaseLayout.UninstallKeyPath}\{ReleaseLayout.PackId}");

        // ⚠️ AND THE START MENU, for exactly the same reason -- 2026-09-16.
        // Velopack names a shortcut `<packTitle>.lnk` and removes shortcuts by
        // TARGET at uninstall, so a test pack sharing the shipping title
        // rewrites the real install's `.lnk` to point at this scratch root and
        // then deletes it. The Add/Remove half of that was found and split by
        // the pack id; this half survived the split and is read here for the
        // same reason the key is: an unread claim about somebody's Start Menu is
        // not a claim.
        var startMenuBefore = ReadStartMenuShortcuts();

        try
        {
            await InstallTwiceAndUninstall(setup, installRoot, dataRoot, logs, planted);
        }
        finally
        {
            // ⚠️ IN A FINALLY, so a red assertion above does not leave an
            // install, a scratch tree and an Add/Remove entry behind for the
            // next run to trip over. Every step reports and does not throw: this
            // runs on the failure path, and a cleanup that throws replaces the
            // reason the arm went red.
            await ReclaimAsync(installRoot.Path);
        }

        // And the real entry is what it was, byte for byte -- or is still absent.
        var realKeyAfter = ReadUninstallKey($@"{ReleaseLayout.UninstallKeyPath}\{ReleaseLayout.PackId}");

        await Assert.That(realKeyAfter).IsEqualTo(realKeyBefore);

        var startMenuAfter = ReadStartMenuShortcuts();

        // Nothing under the SHIPPING title may point into this arm's scratch
        // root -- which is what a shared title produced, and what is asserted,
        // not reasoned about.
        var repointed = startMenuAfter
            .Where(shortcut => Mentions(shortcut.Value, installRoot.Path))
            .Select(shortcut => shortcut.Key)
            .Order(StringComparer.Ordinal);

        await Assert.That(string.Join(", ", repointed)).IsEmpty();

        // The suite's own shortcut does not outlive the suite's own uninstall.
        await Assert.That(startMenuAfter.ContainsKey($"{ReleaseLayout.TestPackTitle}.lnk")).IsFalse();

        // And the set is what it was, byte for byte -- the strongest form of
        // "the real install's Start Menu entry was not touched", and the one
        // that keeps meaning something on a machine that acquires one.
        await Assert.That(Describe(startMenuAfter)).IsEqualTo(Describe(startMenuBefore));
    }

    /// <summary>
    /// Every <c>BrowserAI*.lnk</c> directly under the per-user Start Menu
    /// programs directory, with its bytes.
    /// </summary>
    /// <remarks>
    /// <b>The bytes and not a resolved target, deliberately.</b> Resolving a
    /// shortcut means <c>IShellLink</c>, which means COM on the test host for a
    /// question a substring answers: a <c>.lnk</c> embeds its target path
    /// literally, so <see cref="Mentions"/> over the file finds a shortcut
    /// pointing into a scratch root without any of that. It is also what makes
    /// the before/after comparison a byte comparison and not a comparison of
    /// two things COM was asked about.
    /// </remarks>
    /// <returns>The file name of each, and its content.</returns>
    private static Dictionary<string, byte[]> ReadStartMenuShortcuts()
    {
        var found = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var programs = ReleaseLayout.StartMenuPrograms;

        if (!Directory.Exists(programs))
        {
            return found;
        }

        foreach (var file in Directory.EnumerateFiles(programs, "BrowserAI*.lnk", SearchOption.TopDirectoryOnly))
        {
            try
            {
                found[Path.GetFileName(file)] = File.ReadAllBytes(file);
            }
#pragma warning disable CA1031 // A shortcut that cannot be read is recorded as unreadable, not dropped from the comparison.
            catch (IOException)
#pragma warning restore CA1031
            {
                found[Path.GetFileName(file)] = Encoding.UTF8.GetBytes("<unreadable>");
            }
        }

        return found;
    }

    /// <summary>A shortcut set as one comparable string, so a failure names what moved.</summary>
    /// <param name="shortcuts">What <see cref="ReadStartMenuShortcuts"/> found.</param>
    /// <returns>One line per shortcut: its name and the SHA-256 of its bytes.</returns>
    private static string Describe(Dictionary<string, byte[]> shortcuts) =>
        shortcuts.Count == 0
            ? "<none>"
            : string.Join(
                "\n",
                shortcuts
                    .OrderBy(shortcut => shortcut.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(shortcut => $"{shortcut.Key}\t{Convert.ToHexString(SHA256.HashData(shortcut.Value))}"));

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
    /// screen and hand the start to Velopack -- which is the very thing whose
    /// flags cannot be influenced from here. Launching the same file the same
    /// way, from a parent with no window, exercises the property under test and
    /// nothing else.
    /// </para>
    /// <para>
    /// ⚠️ <b>The console check is BY PID, and that is weaker than it looks --
    /// said here and not left to be discovered.</b> With the default
    /// terminal set to Windows Terminal, a console allocated to a process shows
    /// up as a window owned by <b>Windows Terminal's</b> process, not by ours:
    /// scanning for <c>ConsoleWindowClass</c> is exactly what reported a clean
    /// screen while two windows were on it. So this arm does not claim to detect
    /// a console by looking for its window. What carries that guarantee is
    /// <c>TaskDialogLayoutTests.TheAppIsAWindowBinaryAndTheServerIsAConsoleOne</c>,
    /// which reads the subsystem out of the binary -- the cause and not the
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
            [RegistrationTests.ConfigDirectoryVariable] = OnboardedClientConfig.Seed(clientConfig.Path),
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

                // ⚠️ AND IT IS IN THE LIVE-INSTANCE CENSUS WHILE THE WINDOW IS
                // OPEN, which is what stops a server's update lane applying an
                // update out from under somebody who is reading the dialog:
                // Velopack's apply ends in `force_stop_package`, which kills by
                // image path under the install root and would take the window
                // with it, mid-click.
                //
                // Asserted from OUTSIDE the process, on the marker file it holds
                // -- the same file `LiveInstances.Census` counts -- because the
                // census is a property of the directory and not of any one
                // process's opinion of itself.
                var live = LiveInstances.DirectoryUnder(installRoot.Path);

                await Assert.That(Directory.Exists(live)).IsTrue();
                await Assert.That(Directory.EnumerateFiles(live, "*.live").Any()).IsTrue();

                await Assert.That(TopLevelWindows.Close(dialog)).IsTrue();

                await Assert.That(await WaitForExitAsync(process)).IsTrue();
                await Assert.That(process.ExitCode).IsEqualTo(0);

                // And it leaves the census on the way out. The marker is
                // released by the handle closing, so this is a property of the
                // process ending and not of any cleanup it performs.
                await Assert.That(Directory.EnumerateFiles(live, "*.live").Any()).IsFalse();
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
    /// Waits for the dialog to appear, polling instead of sleeping once.
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
        // current\ -- the configuration app, which is what the stub beside that
        // directory points at, and the MCP server, which is what a client is
        // actually given.
        //
        // ⚠️ Widened 2026-09-15 (previously the app's name alone, with the
        // comment "the binary is where a client is pointed:
        // <root>\current\BrowserAI.exe, never the stub beside it"). That
        // sentence named the right file for the wrong reason from the day the
        // names swapped: a client is pointed at the SERVER, composed from the
        // app's directory and refused if it is not there -- so an install with
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
    /// This runs on the failure path -- that is what it is for -- so it must work
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
    /// directory that is gone, and would refuse the capability on the next run --
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

        // ⚠️ THE SHORTCUT, BY TARGET AND NEVER BY NAME -- 2026-09-16. A `.lnk`
        // that names this scratch root is one this arm's installer wrote and
        // cannot be anybody's; a `.lnk` matched on its name alone could be the
        // maintainer's, and deleting that is the defect this whole split exists
        // to stop. The read happens before the tree goes, which is why the paths
        // are gathered first and removed after.
        foreach (var (name, bytes) in ReadStartMenuShortcuts())
        {
            if (!Mentions(bytes, installRoot))
            {
                continue;
            }

            try
            {
                File.Delete(Path.Combine(ReleaseLayout.StartMenuPrograms, name));
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
    /// <b>A string and not a snapshot object, so the assertion's failure
    /// message shows what moved.</b> An absent key answers <c>&lt;absent&gt;</c>
    /// and not empty, because a key that exists carrying nothing and a key
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
    /// arguments that differ -- and <i>that</i> is a scan over the script. This is
    /// the bytes.
    /// </para>
    /// <para>
    /// <b>What may differ is named by a rule and not by a list.</b> An entry
    /// whose bytes differ has to <i>mention the id</i>, in UTF-8 or in UTF-16,
    /// in one of the two packages -- which is what a <c>.nuspec</c>, a Velopack
    /// manifest and a stub's embedded metadata all do. Anything else differing
    /// means the two packs were not built from one publish, and a list of
    /// expected file names would have gone stale the first time upstream added
    /// one.
    /// </para>
    /// <para>
    /// ⚠️ <b>The suite pack's TITLE joins that rule and the shipping pack's
    /// deliberately does not -- 2026-09-16.</b> The two packs differ in a second
    /// name since the Start Menu split (<see cref="ReleaseLayout.TestPackTitle"/>),
    /// so an entry carrying it has to be allowed to differ. But the shipping
    /// title is <c>BrowserAI</c>, which appears in every binary in the package:
    /// admitting it would exempt everything and turn this arm into an assertion
    /// that two files exist. <c>BrowserAI (suite)</c> appears in nothing else, so
    /// it is the half that can safely be a licence. The control below is the
    /// proof: two entries that both say <c>BrowserAI</c> and differ elsewhere are
    /// still reported.
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

        var (differing, offenders) = Compare(left, right);

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

        var (_, caught) = Compare(plain, doctored);
        await Assert.That(caught.Count).IsEqualTo(1);

        var (moved, spared) = Compare(named, namedToo);
        await Assert.That(spared.Count).IsEqualTo(0);
        await Assert.That(moved).IsEqualTo(1);

        // ⚠️ THE TITLE HALF OF THE CONTROL, in both directions. The suite's
        // title exempts; the shipping title -- which every binary in the package
        // carries -- must not, or the rule above licences everything.
        var titled = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["a.txt"] = Encoding.UTF8.GetBytes($"called {ReleaseLayout.PackTitle}") };
        var titledToo = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["a.txt"] = Encoding.UTF8.GetBytes($"called {ReleaseLayout.TestPackTitle}") };

        var (retitled, allowed) = Compare(titled, titledToo);
        await Assert.That(allowed.Count).IsEqualTo(0);
        await Assert.That(retitled).IsEqualTo(1);

        var underShippingTitle = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["a.txt"] = Encoding.UTF8.GetBytes($"{ReleaseLayout.PackTitle} says see") };
        var underShippingTitleToo = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["a.txt"] = Encoding.UTF8.GetBytes($"{ReleaseLayout.PackTitle} says sea") };

        var (_, stillCaught) = Compare(underShippingTitle, underShippingTitleToo);
        await Assert.That(stillCaught.Count).IsEqualTo(1);

        // ⚠️ THE STUB-NAME CONTROL, added 2026-09-22 with Velopack 1.2.158.
        // The two packs' stubs are named from their TITLES since velopack#985,
        // so the names have to normalise together -- and nothing else may.
        await Assert.That(NormaliseEntryName($"lib/app/{ReleaseLayout.PackTitle}_ExecutionStub.exe", ReleaseLayout.PackId))
            .IsEqualTo(NormaliseEntryName($"lib/app/{ReleaseLayout.TestPackTitle}_ExecutionStub.exe", ReleaseLayout.TestPackId));

        // And the other direction, which is the half that keeps this narrow: an
        // ordinary binary carrying the shipping title is NOT rewritten, so the
        // two executables cannot collapse into one key.
        await Assert.That(NormaliseEntryName($"lib/app/{ReleaseLayout.PackTitle}.exe", ReleaseLayout.PackId))
            .IsEqualTo($"lib/app/{ReleaseLayout.PackTitle}.exe");

        await Assert.That(NormaliseEntryName($"lib/app/{ReleaseLayout.PackTitle}.Server.exe", ReleaseLayout.PackId))
            .IsNotEqualTo(NormaliseEntryName($"lib/app/{ReleaseLayout.PackTitle}.exe", ReleaseLayout.PackId));

        // ⚠️ THE CONTENT-TYPES CONTROL, in three directions, because that
        // exemption is the one that could quietly swallow a real change.
        const string Xml = """<Default Extension="xml" ContentType="application/octet" />""";
        const string Exe = """<Default Extension="exe" ContentType="application/octet" />""";
        const string Ico = """<Default Extension="ico" ContentType="application/octet" />""";

        var inOneOrder = Encoding.UTF8.GetBytes($"<Types>{Ico}{Xml}{Exe}</Types>");
        var inTheOther = Encoding.UTF8.GetBytes($"<Types>{Ico}{Exe}{Xml}</Types>");
        var missingOne = Encoding.UTF8.GetBytes($"<Types>{Ico}{Exe}</Types>");
        var nothingAtAll = Encoding.UTF8.GetBytes("<Types></Types>");

        // Re-ordered: the same declarations, so not an offence.
        await Assert.That(SameDeclarations(inOneOrder, inTheOther)).IsTrue();

        // A declaration that is genuinely absent from one side IS an offence.
        await Assert.That(SameDeclarations(inOneOrder, missingOne)).IsFalse();

        // And a read that came back empty is never "the same", which is what
        // stops a parse that stopped matching from exempting everything.
        await Assert.That(SameDeclarations(nothingAtAll, nothingAtAll)).IsFalse();

        // The exemption is scoped to that ONE part: the same re-ordering in any
        // other entry is still reported.
        var elsewhere = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["a.xml"] = inOneOrder };
        var elsewhereToo = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["a.xml"] = inTheOther };

        var (_, notExempt) = Compare(elsewhere, elsewhereToo);
        await Assert.That(notExempt.Count).IsEqualTo(1);
    }

    /// <summary>
    /// The packed release carries both executables and names the configuration
    /// app as its main one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Read out of the package and not out of the script that wrote
    /// it.</b> <c>ReleaseScriptTests</c> holds what
    /// <c>build/New-Release.ps1</c> passes; this holds what came out the other
    /// end, and the two are different claims -- a `vpk` that silently ignored an
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
        // makes the package an install and not two executables.
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
            entries[NormaliseEntryName(entry.FullName, id)] = bytes.ToArray();
        }

        return entries;
    }

    /// <summary>
    /// Takes the two things out of an entry's NAME that are allowed to differ
    /// between the two packs: the pack id, and the stub's base name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The stub half arrived with Velopack 1.2.158 and is the only thing
    /// in this repository that bump changed -- 2026-09-22.</b> Upstream named the
    /// stub embedded in the <c>.nupkg</c> after <c>mainExe</c> until
    /// <see href="https://github.com/velopack/velopack/pull/985">velopack#985</see>,
    /// and names it after <c>packTitle ?? packId</c> since. Both packs are built
    /// from one publish with one <c>--mainExe</c>, so the entry used to be
    /// <c>BrowserAI_ExecutionStub.exe</c> in both; the two packs carry
    /// deliberately different TITLES, so it is
    /// <c>BrowserAI_ExecutionStub.exe</c> and
    /// <c>BrowserAI (suite)_ExecutionStub.exe</c> now. This arm went red on
    /// exactly that, on the first gate run after the bump, which is the
    /// mechanism working.
    /// </para>
    /// <para>
    /// <b>It rewrites the ONE entry and not the title wherever it appears</b>,
    /// for the same reason <see cref="Licensed"/> refuses the shipping title: that
    /// title is <c>BrowserAI</c>, and stripping it from names would turn
    /// <c>BrowserAI.exe</c> and <c>BrowserAI.Server.exe</c> into the same key in
    /// one pack and leave them untouched in the other. Keying on the
    /// <c>_ExecutionStub.exe</c> suffix names what upstream actually varies and
    /// nothing else.
    /// </para>
    /// <para>
    /// <b>The bytes are still compared.</b> This is a rule about the NAME; the
    /// stub's contents still have to mention something in <see cref="Licensed"/>
    /// or the entry is reported, which is what keeps the arm from becoming an
    /// assertion that two files exist.
    /// </para>
    /// </remarks>
    /// <param name="name">The entry's full name inside the package.</param>
    /// <param name="id">The pack id to normalise out of it.</param>
    /// <returns>The name both packs should agree on.</returns>
    private static string NormaliseEntryName(string name, string id) =>
        StubEntryName.Replace(name.Replace(id, "<id>", StringComparison.Ordinal), "${dir}<stub>_ExecutionStub.exe");

    /// <summary>The embedded execution stub, whose base name follows the pack title.</summary>
    private static readonly Regex StubEntryName =
        new(@"(?<dir>^|.*/)[^/]+_ExecutionStub\.exe$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The OPC part listing one content type per extension.</summary>
    private const string ContentTypesPart = "[Content_Types].xml";

    /// <summary>One <c>Default</c> or <c>Override</c> declaration.</summary>
    private static readonly Regex ContentTypeDeclaration =
        new(@"<(?:Default|Override)\b[^>]*/?>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <param name="left">One pack's content-types part.</param>
    /// <param name="right">The other's.</param>
    /// <returns>Whether they declare the same things, in any order.</returns>
    private static bool SameDeclarations(byte[] left, byte[] right)
    {
        static List<string> declarations(byte[] bytes) =>
        [
            .. ContentTypeDeclaration
                .Matches(Encoding.UTF8.GetString(bytes))
                .Select(match => match.Value)
                .Order(StringComparer.Ordinal),
        ];

        var a = declarations(left);
        var b = declarations(right);

        // Non-vacuous: an empty read on both sides would report "the same".
        return a.Count > 0 && a.SequenceEqual(b, StringComparer.Ordinal);
    }

    /// <summary>
    /// The strings whose presence licenses an entry to differ between the two
    /// packs.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The shipping TITLE is deliberately not in here.</b> It is
    /// <c>BrowserAI</c>, which every binary in the package carries, so admitting
    /// it would exempt every entry and leave an arm that asserts nothing. The
    /// suite's title appears in nothing but the metadata the rename touches,
    /// which is why that one is safe. See
    /// <c>TheSuitesPackAndTheShippingPackDifferOnlyWhereTheIdAppears</c>, whose
    /// control holds both halves.
    /// </remarks>
    private static readonly string[] Licensed =
    [
        ReleaseLayout.PackId,
        ReleaseLayout.TestPackId,
        ReleaseLayout.TestPackTitle,
    ];

    /// <summary>
    /// Compares two packages' entries and reports the ones that differ without
    /// mentioning either id or the suite pack's title.
    /// </summary>
    /// <param name="left">The shipping package's entries.</param>
    /// <param name="right">The test package's entries.</param>
    /// <returns>How many entries differed, and which of those are offences.</returns>
    private static (int Differing, List<string> Offenders) Compare(
        Dictionary<string, byte[]> left,
        Dictionary<string, byte[]> right)
    {
        var differing = 0;
        var offenders = new List<string>();

        foreach (var (name, bytes) in left.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!right.TryGetValue(name, out var other) || bytes.AsSpan().SequenceEqual(other))
            {
                continue;
            }

            // ⚠️ THE CONTENT-TYPES PART IS A SET, AND ITS ORDER IS A FUNCTION OF
            // THE ENTRY NAMES -- 2026-09-22, with Velopack 1.2.158. `vpk` emits
            // one declaration per extension in FIRST-SEEN order, so the pack
            // whose stub is named `BrowserAI (suite)_ExecutionStub.exe` meets an
            // `.exe` before its first `.xml` and the shipping pack does not:
            // `...,ico,exe,xml,...` against `...,ico,xml,exe,...`, same 24
            // declarations, same 1,692 bytes, different order. THIS IS NOT
            // NONDETERMINISM AND WAS CHECKED , NOT ASSUMED: every shipping
            // pack on this machine, 20 of them across both Velopack versions,
            // hashes to the same `92451fc6...`; the suite's pack of the same run is
            // the only outlier. So it is velopack#985's stub rename arriving in a
            // second place, and comparing these bytes would be comparing an
            // ordering the title difference caused.
            //
            // The SET is still compared, so a declaration added, removed or
            // re-typed in one pack and not the other is still an offence.
            if (name is ContentTypesPart && SameDeclarations(bytes, other))
            {
                continue;
            }

            differing++;

            if (!Array.Exists(Licensed, text => Mentions(bytes, text) || Mentions(other, text)))
            {
                offenders.Add(
                    $"{name} differs ({bytes.Length} vs {other.Length} bytes) and neither copy mentions any of"
                    + $" '{string.Join("', '", Licensed)}', so the two packs did not come from one publish");
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

            File.WriteAllText(path, $"planted by the suite at {DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)} -- {path}");
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
    /// touched"* and cannot answer *"is this still my directory"* -- and on the
    /// 2026-09-15 release gate it was not: three children of other arms had
    /// inherited <c>BROWSERAI_ROOT</c> and were writing a 203.8 MB Chromium
    /// download into <c>browsers\chromium-1244</c> while this arm reported the
    /// root byte-identical. It passed, and nothing would have said so.
    /// </para>
    /// <para>
    /// <b>Watched red against a live reproduction and not only a plant.</b>
    /// A full suite run on 2026-09-15 with the keyed attribute deliberately put
    /// back reproduced the race and this assertion named <b>sixteen</b> foreign
    /// files in the data root: <c>browsers\reinstall.lock</c>, three
    /// <c>index\</c> entries, three <c>instances\{pid}-{guid}\</c> directories
    /// carrying <c>instance.live</c> and two Playwright configuration files
    /// each, and three <c>live\</c> markers -- every one of them written by
    /// another arm's product child that had inherited <c>BROWSERAI_ROOT</c>. The
    /// hash loop alone had reported that same directory unchanged.
    /// </para>
    /// <para>
    /// <b>What the install is entitled to leave, named and not globbed
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
    /// The shape <c>RollingFileWriter</c> composes, and not
    /// <c>browserai-*.log</c>. The looser glob would also match this arm's own
    /// planted <c>browserai-planted.log</c> -- which is checked by hash above and
    /// must not be exempted here -- and would let any foreign file called
    /// <c>browserai-anything.log</c> through.
    /// </remarks>
    [GeneratedRegex(@"^browserai-\d{8}-\d{3}\.log$", RegexOptions.IgnoreCase)]
    private static partial Regex RolledProcessLog();
}
