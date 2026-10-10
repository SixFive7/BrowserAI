// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BrowserAI.Hosting;
using BrowserAI.Interop;
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
        using var feed = ScratchDirectory.Create("real-install-feed");

        var paths = new LocalAppDataPaths(dataRoot.Path);
        var planted = Plant(paths);

        // ⚠️ CODEX_HOME IS THE THIRD VARIABLE SINCE 2026-09-24, AND IT IS THE ONE
        // THAT STOPS A WRITE. The install and uninstall hooks register with Codex
        // now, and Codex has no scope flag: `codex mcp add` writes whichever
        // configuration CODEX_HOME names. Left alone, this arm's real installer
        // would run the maintainer's own codex.exe against his own ~\.codex and,
        // finding no entry there, write one -- which is not something a test may
        // do to somebody's machine, and is not something the clearance snapshot
        // would have caught before this line existed.
        //
        // AND THE UPDATE FEED SINCE 2026-10-09, an empty scratch folder: the install
        // hook reads the installer's BROWSERAI_UPDATE_FEED once and writes it into the
        // task's action as --update-source, beside the data root, and this arm holds
        // both arguments where the hook wrote them.
        var codexHome = Directory.CreateDirectory(Path.Combine(clientConfig.Path, "codex")).FullName;

        using var sandbox = new EnvironmentScope(new Dictionary<string, string?>
        {
            [RegistrationTests.ConfigDirectoryVariable] = OnboardedClientConfig.Seed(clientConfig.Path),
            [BrowserAiPaths.AppRootOverride] = dataRoot.Path,
            [RegistrationTests.CodexHomeVariable] = codexHome,
            [UpdateConfiguration.FeedVariable] = feed.Path,
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

        // ⚠️ AND THE USER'S OWN PATH, since Q294 b -- 2026-09-24. The install hook
        // puts the install's `current\` folder on the real HKCU\Environment Path and
        // the uninstall hook takes exactly that entry off; a hook cannot be handed a
        // scratch PATH from here, so this arm writes the real one and holds it
        // byte-identical, and the gate's clearance reading holds it again.
        var pathBefore = RegistryUserPathStore.User.Read();

        try
        {
            await InstallTwiceAndUninstall(setup, installRoot, dataRoot, logs, planted, new ClientSandbox(clientConfig.Path, codexHome, feed.Path));
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

        // And the user's PATH: text and kind, exactly as before the first install.
        await Assert.That(RegistryUserPathStore.User.Read()).IsEqualTo(pathBefore);
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

    // RETIRED 2026-10-09: TheInstalledMainExecutableServesItsPageAndShowsNoWindowOfItsOwn,
    // which held that a person's start of the installed main executable served its
    // page itself, counted in the live-instance census while it served, and exited a
    // minute after its last tab. A person's start serves nothing since the one resident
    // background (S a, D13 a): it runs the task, asks the background for a tab and
    // exits, so the census of a person's start and the coordinator's minute went with
    // it. What still holds moved to the background and is held in
    // RealInstallerTests.Scheduler.cs by
    // APersonsStartRunsTheTaskAndTheBackgroundServesThePageAndKeepsASessionAcrossItsRelays:
    // the page answering at the address the start was handed, no visible window of
    // BrowserAI's own on the desktop, and the tab's listener stopping a minute after
    // its last tab while the background stays.

    /// <summary>Reads both of a launched child's output pipes to their end.</summary>
    /// <param name="process">The child.</param>
    /// <returns>A task that completes when both pipes have closed.</returns>
    private static Task DrainAsync(LaunchedProcess process) =>
        Task.WhenAll(process.StandardOutput.CopyToAsync(Stream.Null), process.StandardError.CopyToAsync(Stream.Null));

    /// <summary>
    /// The input framework's two windows on a desktop with no taskbar, measured
    /// 2026-09-24 in the app's own pid and not the app's user interface.
    /// </summary>
    private static readonly string[] InputIndicatorClasses = ["UAC_InputIndicatorOverlayWnd", "UAC Input Indicator"];

    /// <summary>Polls a condition until it holds or the hang detector runs out.</summary>
    /// <param name="condition">The condition.</param>
    /// <returns>Whether it held.</returns>
    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TestDefaults.ProcessHang;

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        return true;
    }

    /// <summary>
    /// Two installs of one pack id into two roots share one Add/Remove key, and
    /// uninstalling either root deletes it -- in both orders.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Re-verification row 123, made an arm on 2026-09-24 -- Q275 a.</b> It was a
    /// manual row because the only id it could be measured under was the shipping
    /// one, whose key a real install depends on; under the suite's test id no key
    /// but <c>BrowserAI.app.test</c> is ever written, which is how the 2026-09-24
    /// re-check was taken by hand and what makes it runnable here. Velopack
    /// creates the key from the pack id alone (<c>registry.rs:40</c> at 1.2.158)
    /// and deletes it with an unconditional <c>delete_subkey_all(&amp;app_id)</c>
    /// (<c>registry.rs:65</c>), so the second install rewrites the first one's entry
    /// and the first uninstall takes both.
    /// </para>
    /// <para>
    /// <b>Both orders, because they are two claims.</b> Uninstalling the root the
    /// key names deleting it is what anybody would expect; uninstalling the root the
    /// key does NOT name deleting it too is the surprising half, and the half that
    /// would take a real install's entry away. Silent installs only, the sandbox
    /// every arm in this class uses, and the real key and Start Menu read before and
    /// after and required byte-identical.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TwoRootsOfOnePackIdShareOneUninstallKeyAndEitherUninstallDeletesIt()
    {
        var setup = SuiteEnvironment.RequireReleaseInstaller();

        using var namedRootFirst = ScratchDirectory.CreateUnderProfile("two-roots-1a");
        using var namedRootSecond = ScratchDirectory.CreateUnderProfile("two-roots-1b");
        using var otherRootFirst = ScratchDirectory.CreateUnderProfile("two-roots-2a");
        using var otherRootSecond = ScratchDirectory.CreateUnderProfile("two-roots-2b");
        using var dataRoot = ScratchDirectory.CreateUnderProfile("two-roots-data");
        using var clientConfig = ScratchDirectory.Create("two-roots-client");
        using var logs = ScratchDirectory.Create("two-roots-logs");

        // The same three variables every arm here sets, for the reason the first
        // arm gives: the hooks register with both clients and write a record.
        using var sandbox = new EnvironmentScope(new Dictionary<string, string?>
        {
            [RegistrationTests.ConfigDirectoryVariable] = OnboardedClientConfig.Seed(clientConfig.Path),
            [BrowserAiPaths.AppRootOverride] = dataRoot.Path,
            [RegistrationTests.CodexHomeVariable] = Directory.CreateDirectory(Path.Combine(clientConfig.Path, "codex")).FullName,
        });

        var realKeyBefore = ReadUninstallKey($@"{ReleaseLayout.UninstallKeyPath}\{ReleaseLayout.PackId}");
        var startMenuBefore = ReadStartMenuShortcuts();
        var pathBefore = RegistryUserPathStore.User.Read();
        var realTaskBefore = ScheduledTasks.DefinitionOf(RealSignInTask);

        try
        {
            // The key's own root first, then the root the key no longer names.
            await InstallTwoRootsAndUninstall(setup, namedRootFirst.Path, namedRootSecond.Path, uninstallTheNamedRootFirst: true, logs.Path, dataRoot.Path);
            await InstallTwoRootsAndUninstall(setup, otherRootFirst.Path, otherRootSecond.Path, uninstallTheNamedRootFirst: false, logs.Path, dataRoot.Path);
        }
        finally
        {
            foreach (var root in new[] { namedRootFirst, namedRootSecond, otherRootFirst, otherRootSecond })
            {
                await ReclaimAsync(root.Path);
            }
        }

        // The real entry and the real Start Menu are what they were, byte for byte.
        await Assert.That(ReadUninstallKey($@"{ReleaseLayout.UninstallKeyPath}\{ReleaseLayout.PackId}")).IsEqualTo(realKeyBefore);
        await Assert.That(Describe(ReadStartMenuShortcuts())).IsEqualTo(Describe(startMenuBefore));
        await Assert.That(ReadUninstallKey(ReleaseLayout.TestUninstallKey)).IsEqualTo("<absent>");

        // And so is the user's PATH, after four installs and four uninstalls.
        await Assert.That(RegistryUserPathStore.User.Read()).IsEqualTo(pathBefore);

        // And the real install's sign-in task, whether or not there is one.
        await Assert.That(ScheduledTasks.DefinitionOf(RealSignInTask)).IsEqualTo(realTaskBefore);
    }

    /// <summary>
    /// The real install's sign-in task: the shipping pack id and the default install
    /// root, which is where a person's BrowserAI is.
    /// </summary>
    private static string RealSignInTask { get; } = SignInTask.NameFor(
        ReleaseLayout.PackId,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ReleaseLayout.PackId));

    /// <summary>One order of the two-roots measurement.</summary>
    /// <param name="setup">The test-id installer.</param>
    /// <param name="first">The root installed first.</param>
    /// <param name="second">The root installed second, which the key ends up naming.</param>
    /// <param name="uninstallTheNamedRootFirst">Whether the first uninstall is the root the key names.</param>
    /// <param name="logs">Where Velopack's own logs go.</param>
    /// <param name="dataRoot">The scratch data root the installer named, which each task's action carries.</param>
    /// <returns>The assertion task.</returns>
    private static async Task InstallTwoRootsAndUninstall(
        string setup,
        string first,
        string second,
        bool uninstallTheNamedRootFirst,
        string logs,
        string dataRoot)
    {
        var order = uninstallTheNamedRootFirst ? "named-first" : "other-first";

        await Assert.That(await RunAsync(setup, ["--silent", "--log", Path.Combine(logs, $"{order}-install-1.log"), "--installto", first])).IsEqualTo(0);
        await Assert.That(InstallLocationOfTheTestKey()).IsEqualTo(Normalised(first));

        // ⚠️ THE SECOND INSTALL REWRITES THE ONE KEY, and the first root is still a
        // complete install that no Add/Remove entry points at any more.
        await Assert.That(await RunAsync(setup, ["--silent", "--log", Path.Combine(logs, $"{order}-install-2.log"), "--installto", second])).IsEqualTo(0);
        await Assert.That(InstallLocationOfTheTestKey()).IsEqualTo(Normalised(second));
        await Assert.That(IsACompleteInstall(first)).IsTrue();

        // ⚠️ EACH ROOT PUT ITS OWN FOLDER ON THE PATH -- Q294 b -- and the uninstall
        // below takes off its own and never the other's, which is the property that
        // keeps the test pack away from a real install's entry.
        var firstEntry = Path.Combine(first, RegistrationTarget.CurrentDirectoryName);
        var secondEntry = Path.Combine(second, RegistrationTarget.CurrentDirectoryName);

        await Assert.That(PathEntriesNaming(firstEntry)).IsEqualTo(1);
        await Assert.That(PathEntriesNaming(secondEntry)).IsEqualTo(1);

        // ⚠️ AND EACH ROOT REGISTERED ITS OWN SIGN-IN TASK -- Q282 a -- named for the
        // test pack's id and that root's key, starting that root's one executable as
        // the background, for the data root the installer named. The uninstalls below
        // take each off with its root. Corrected 2026-10-09 (previously "starting that
        // root's app with the sign-in argument", `--sign-in $(Arg0)`): the task starts
        // the resident background since S a.
        var background = SignInTask.ArgumentsFor(dataRoot, updateSource: null);

        await Assert.That(SignInCommandOf(first, background)).IsEqualTo(Path.Combine(first, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.AppFileName));
        await Assert.That(SignInCommandOf(second, background)).IsEqualTo(Path.Combine(second, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.AppFileName));

        var (goesFirst, staysBehind) = uninstallTheNamedRootFirst ? (second, first) : (first, second);

        await Assert.That(await RunAsync(Path.Combine(goesFirst, "Update.exe"), ["--uninstall", "--silent"])).IsEqualTo(0);
        await WaitOutTheDeferredRemoval(Path.Combine(goesFirst, RegistrationTarget.CurrentDirectoryName));

        // ⚠️ THE CLAIM: either uninstall deletes the one key, while the other root
        // still holds a complete install.
        await Assert.That(ReadUninstallKey(ReleaseLayout.TestUninstallKey)).IsEqualTo("<absent>");
        await Assert.That(IsACompleteInstall(staysBehind)).IsTrue();

        // The PATH lost the uninstalled root's entry and kept the other's.
        await Assert.That(PathEntriesNaming(Path.Combine(goesFirst, RegistrationTarget.CurrentDirectoryName))).IsEqualTo(0);
        await Assert.That(PathEntriesNaming(Path.Combine(staysBehind, RegistrationTarget.CurrentDirectoryName))).IsEqualTo(1);

        // So did the scheduler: the uninstalled root's task is gone, the other's stays.
        await Assert.That(SignInCommandOf(goesFirst, background)).IsNull();
        await Assert.That(SignInCommandOf(staysBehind, background)).IsNotNull();

        // And the root left behind still uninstalls cleanly, finding no key.
        await Assert.That(await RunAsync(Path.Combine(staysBehind, "Update.exe"), ["--uninstall", "--silent"])).IsEqualTo(0);
        await WaitOutTheDeferredRemoval(Path.Combine(staysBehind, RegistrationTarget.CurrentDirectoryName));
        await Assert.That(ReadUninstallKey(ReleaseLayout.TestUninstallKey)).IsEqualTo("<absent>");
        await Assert.That(PathEntriesNaming(Path.Combine(staysBehind, RegistrationTarget.CurrentDirectoryName))).IsEqualTo(0);
        await Assert.That(SignInCommandOf(staysBehind, background)).IsNull();
    }

    /// <summary>
    /// The command the test pack's sign-in task for one root starts, read back from
    /// the scheduler, or <see langword="null"/> when there is no such task.
    /// </summary>
    /// <remarks>
    /// <b>Read from what the scheduler stored</b>, so it is the installed hook's
    /// registration that is asserted and not the product's composition of it. The
    /// arguments are held to what the caller expects here too, since a command alone
    /// would pass a task that starts something else. <i>Corrected 2026-10-09
    /// (previously held to <c>--sign-in $(Arg0)</c>, the coordinator's)</i>: the
    /// task starts the background, with the installer's settings as arguments.
    /// </remarks>
    /// <param name="installRoot">The scratch root the test pack was installed into.</param>
    /// <param name="arguments">The action's arguments the hook should have written.</param>
    /// <returns>The command, or <see langword="null"/>.</returns>
    private static string? SignInCommandOf(string installRoot, string arguments)
    {
        if (StoredTaskOf(SignInTask.NameFor(ReleaseLayout.TestPackId, installRoot)) is not { } task)
        {
            return null;
        }

        return task.Arguments == arguments
            ? task.Command
            : $"<a task whose arguments are '{task.Arguments}'>";
    }

    /// <summary>The scratch client configurations and the update feed an arm's installer is given.</summary>
    /// <param name="ClaudeConfig">The directory <c>CLAUDE_CONFIG_DIR</c> names, seeded as onboarded.</param>
    /// <param name="CodexHome">The directory <c>CODEX_HOME</c> names.</param>
    /// <param name="UpdateFeed">The empty folder <c>BROWSERAI_UPDATE_FEED</c> names.</param>
    private sealed record ClientSandbox(string ClaudeConfig, string CodexHome, string UpdateFeed);

    /// <summary>
    /// What a Task Scheduler definition says about the three things the hooks set: the
    /// command, its arguments and what the scheduler does with a second start.
    /// </summary>
    /// <param name="Command">The action's command.</param>
    /// <param name="Arguments">The action's arguments, <c>$(Arg0)</c> unexpanded.</param>
    /// <param name="InstancesPolicy">
    /// <c>MultipleInstancesPolicy</c>, or <see cref="IgnoreNew"/> when the definition
    /// leaves it out, which is the scheduler's own default.
    /// </param>
    private sealed record StoredTask(string Command, string Arguments, string InstancesPolicy)
    {
        /// <summary>The policy under which a start while one runs starts nothing (S a).</summary>
        public const string IgnoreNew = "IgnoreNew";

        /// <summary>Reads the three out of a definition.</summary>
        /// <param name="xml">A Task Scheduler 1.2 definition, as stored or as the hook saved it.</param>
        /// <returns>What it says.</returns>
        public static StoredTask Parse(string xml)
        {
            var task = System.Xml.Linq.XDocument.Parse(xml);

            string? valueOf(string name) =>
                task.Descendants().FirstOrDefault(element => element.Name.LocalName == name)?.Value;

            return new(valueOf("Command") ?? "<no command>", valueOf("Arguments") ?? "<no arguments>", valueOf("MultipleInstancesPolicy") ?? IgnoreNew);
        }
    }

    /// <summary>What the scheduler stored for a task, or <see langword="null"/> when there is no such task.</summary>
    /// <param name="name">The task's name.</param>
    /// <returns>The task.</returns>
    private static StoredTask? StoredTaskOf(string name) =>
        ScheduledTasks.DefinitionOf(name) is { } xml ? StoredTask.Parse(xml) : null;

    /// <summary>A command and its arguments as one comparable string, the command normalised.</summary>
    /// <param name="parts">The command, then each argument.</param>
    /// <returns>One line each.</returns>
    private static string Spelled(IReadOnlyList<string> parts) =>
        string.Join("\n", parts.Select((part, at) => at is 0 && Path.IsPathRooted(part) ? Normalised(part) : part));

    /// <summary>
    /// The command and arguments a scratch Claude Code configuration registers
    /// BrowserAI with, at user scope, or <see langword="null"/> when it registers none.
    /// </summary>
    /// <remarks>
    /// <b>Read as a file and parsed</b>, the way the clearance snapshot reads the real
    /// one (Q281 a): asking the client would start it.
    /// </remarks>
    /// <param name="configDirectory">The directory <c>CLAUDE_CONFIG_DIR</c> named.</param>
    /// <returns>The entry, <see cref="Spelled"/>.</returns>
    private static string? ClaudeEntryIn(string configDirectory)
    {
        var file = Path.Combine(configDirectory, OnboardedClientConfig.FileName);

        if (!File.Exists(file))
        {
            return null;
        }

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));

        if (!document.RootElement.TryGetProperty("mcpServers", out var servers)
            || !servers.TryGetProperty(McpRegistrar.ServerName, out var entry))
        {
            return null;
        }

        var command = entry.TryGetProperty("command", out var named) ? named.GetString() : null;
        List<string> arguments = entry.TryGetProperty("args", out var listed) && listed.ValueKind is System.Text.Json.JsonValueKind.Array
            ? [.. listed.EnumerateArray().Select(argument => argument.GetString() ?? "<null>")]
            : [];

        return Spelled([command ?? "<no command>", .. arguments]);
    }

    /// <summary>
    /// The command and arguments a scratch Codex configuration registers BrowserAI
    /// with, or <see langword="null"/> when it registers none.
    /// </summary>
    /// <remarks>
    /// <b>Read as text</b>, the <c>[mcp_servers.browserai]</c> table alone, as the
    /// clearance snapshot reads the real one (Q292 a). Codex writes a path as a TOML
    /// literal string, in single quotes, and may write any other value as a basic one,
    /// so both are read.
    /// </remarks>
    /// <param name="codexHome">The directory <c>CODEX_HOME</c> named.</param>
    /// <returns>The entry, <see cref="Spelled"/>.</returns>
    private static string? CodexEntryIn(string codexHome)
    {
        var file = Path.Combine(codexHome, "config.toml");

        if (!File.Exists(file))
        {
            return null;
        }

        var text = File.ReadAllText(file);
        var header = Regex.Match(text, $@"(?m)^[ \t]*\[mcp_servers\.{McpRegistrar.ServerName}\][ \t]*\r?$");

        if (!header.Success)
        {
            return null;
        }

        var rest = text[(header.Index + header.Length)..];
        var next = Regex.Match(rest, @"(?m)^[ \t]*\[");
        var table = next.Success ? rest[..next.Index] : rest;

        var command = TomlCommand().Match(table) is { Success: true } named ? TomlStrings(named.Groups["value"].Value).FirstOrDefault() : null;
        var arguments = TomlArguments().Match(table) is { Success: true } listed ? TomlStrings(listed.Groups["items"].Value) : [];

        return Spelled([command ?? "<no command>", .. arguments]);
    }

    /// <summary>Every TOML string in a value, basic or literal, in order.</summary>
    /// <param name="value">The value's text.</param>
    /// <returns>The strings.</returns>
    private static List<string> TomlStrings(string value) =>
        [.. TomlString().Matches(value).Select(match => match.Groups["basic"].Success
            ? Regex.Unescape(match.Groups["basic"].Value)
            : match.Groups["literal"].Value)];

    [GeneratedRegex(@"(?m)^[ \t]*command[ \t]*=[ \t]*(?<value>.+)$")]
    private static partial Regex TomlCommand();

    [GeneratedRegex(@"(?ms)^[ \t]*args[ \t]*=[ \t]*\[(?<items>.*?)\]")]
    private static partial Regex TomlArguments();

    [GeneratedRegex(@"""(?<basic>(?:[^""\\]|\\.)*)""|'(?<literal>[^']*)'")]
    private static partial Regex TomlString();

    /// <summary>The application id an install's processes run under, out of its own manifest.</summary>
    /// <remarks>
    /// <c>vpk</c> writes <c>shortcutAumid</c> into the package's manifest and
    /// <c>VelopackApp.Run()</c> sets it on every installed process, the hooks
    /// included, which is the id the toasts' activator is derived from
    /// (re-verification row 156). Read and never composed, so a change in how
    /// <c>vpk</c> names it moves this with it.
    /// </remarks>
    /// <param name="installRoot">The install root.</param>
    /// <returns>The id.</returns>
    private static string AppUserModelIdOf(string installRoot)
    {
        var manifest = System.Xml.Linq.XDocument.Load(Path.Combine(installRoot, RegistrationTarget.CurrentDirectoryName, "sq.version"));

        return manifest.Descendants().FirstOrDefault(element => element.Name.LocalName == "shortcutAumid")?.Value
            ?? throw new InvalidOperationException($"The manifest under '{installRoot}' names no shortcutAumid, so the toasts' activator cannot be found.");
    }

    /// <summary>
    /// What the user's own classes register as the toasts' activator for an
    /// application id, or <see langword="null"/> when neither half is there.
    /// </summary>
    /// <param name="aumid">The application id.</param>
    /// <returns>The class the id names, the class derived from it, and what COM starts for it, upper-cased.</returns>
    private static string? ToastActivatorOf(string aumid)
    {
        var activator = ToastActivatorRegistration.ClassFor(aumid).ToString("B").ToUpperInvariant();

        using var server = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"Software\Classes\CLSID\{activator}\LocalServer32");
        using var id = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"Software\Classes\AppUserModelId\{aumid}");

        var command = server?.GetValue(string.Empty) as string;
        var named = id?.GetValue(ToastActivatorRegistration.CustomActivatorValue) as string;

        return command is null && named is null
            ? null
            : $"{named ?? "<no CustomActivator>"} -> {activator}: {command ?? "<no LocalServer32>"}".ToUpperInvariant();
    }

    /// <summary>What <see cref="ToastActivatorOf"/> reads for an install's own activator.</summary>
    /// <param name="aumid">The application id.</param>
    /// <param name="app">The one executable the activator starts.</param>
    /// <returns>The reading.</returns>
    private static string ExpectedToastActivator(string aumid, string app)
    {
        var activator = ToastActivatorRegistration.ClassFor(aumid).ToString("B").ToUpperInvariant();

        return $"{activator} -> {activator}: {ToastActivatorRegistration.CommandFor(app)}".ToUpperInvariant();
    }

    /// <summary>
    /// An installed server started with the installer's variable takes the general
    /// exit, and a byte-identical copy outside any install takes the one the build
    /// carries for that variable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The installed half of re-verification row 126, made an arm on 2026-09-24
    /// -- Q275 a.</b> <c>VelopackApp.Run()</c> clears <c>VELOPACK_FIRSTRUN</c> on
    /// an installed process and only there (<c>VelopackApp.cs:227-238</c> at
    /// 1.2.158), so the server a real <c>Setup.exe</c> installed never sees it, and
    /// it ends through the general exit -- launcher gone, standard input a console --
    /// which is <c>Startup[9]</c>. The copy outside any install still sees it.
    /// </para>
    /// <para>
    /// ⚠️ <b>The copy takes the general exit too, since the pack is this tree's
    /// build -- Q287 a, 2026-09-24.</b> <i>Previously: "What the copy logs depends on
    /// the BUILD, and the arm reads which build it has"</i> -- the binaries were the
    /// last packed test pack's, a 1.1.0 build still carrying the installer exit
    /// Q276 deleted, <c>Startup[8]</c>, so the expectation was read out of the
    /// copy's bytes (N74). Every gate packs the test installer from its own publish
    /// now, so the copy is this build, and <c>Startup[9]</c> is typed.
    /// </para>
    /// <para>
    /// <b>Through the orphan-console rig, silent install only</b>, so neither start
    /// shows a window, and the real key and Start Menu are held byte-identical.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnInstalledServerNeverSeesTheInstallersVariableAndACopyOutsideAnyInstallDoes()
    {
        var setup = SuiteEnvironment.RequireReleaseInstaller();

        using var installRoot = ScratchDirectory.CreateUnderProfile("firstrun-installed");
        using var outside = ScratchDirectory.CreateUnderProfile("firstrun-outside");
        using var installedData = ScratchDirectory.CreateUnderProfile("firstrun-installed-data");
        using var outsideData = ScratchDirectory.CreateUnderProfile("firstrun-outside-data");
        using var dataRoot = ScratchDirectory.CreateUnderProfile("firstrun-hooks-data");
        using var clientConfig = ScratchDirectory.Create("firstrun-client");
        using var logs = ScratchDirectory.Create("firstrun-logs");

        using var sandbox = new EnvironmentScope(new Dictionary<string, string?>
        {
            [RegistrationTests.ConfigDirectoryVariable] = OnboardedClientConfig.Seed(clientConfig.Path),
            [BrowserAiPaths.AppRootOverride] = dataRoot.Path,
            [RegistrationTests.CodexHomeVariable] = Directory.CreateDirectory(Path.Combine(clientConfig.Path, "codex")).FullName,
        });

        var realKeyBefore = ReadUninstallKey($@"{ReleaseLayout.UninstallKeyPath}\{ReleaseLayout.PackId}");
        var startMenuBefore = ReadStartMenuShortcuts();

        try
        {
            await Assert.That(await RunAsync(setup, ["--silent", "--log", Path.Combine(logs.Path, "setup.log"), "--installto", installRoot.Path])).IsEqualTo(0);

            var installed = Path.Combine(installRoot.Path, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.AppFileName);
            var copy = Path.Combine(outside.Path, RegistrationTarget.AppFileName);

            await Assert.That(File.Exists(installed)).IsTrue();

            File.Copy(installed, copy);

            // The installed server: Run() cleared the variable, so the general exit.
            using (var run = OrphanedConsoleStart.Begin(installed, installedData.Path, startedByTheInstaller: true, TestDefaults.ProcessHang))
            {
                await Assert.That(run.Started).IsTrue();
                await Assert.That(run.WaitUntilItSaysOneOf(TestDefaults.ProcessHang, GeneralExitSentence, InstallerExitSentence, "Watching the MCP client"))
                    .IsEqualTo(GeneralExitSentence)
                    .Because(run.Records());
                await Assert.That(run.WaitUntilItExits(TestDefaults.ProcessHang)).IsTrue();
            }

            // The copy outside any install: the variable is still set when the
            // server reads it, and since Q276 deleted the installer exit it takes the
            // general exit too. ⚠️ *Previously the expectation was read out of the
            // copy's own bytes -- whether it carried the installer exit's sentence --
            // because the pack was a 1.1.0 build that still had it.* The pack is this
            // tree's build since Q287 a, so the expectation is typed and the
            // installer exit's sentence stays only as a sentence the run would stop on.
            using (var run = OrphanedConsoleStart.Begin(copy, outsideData.Path, startedByTheInstaller: true, TestDefaults.ProcessHang))
            {
                await Assert.That(run.Started).IsTrue();
                await Assert.That(run.WaitUntilItSaysOneOf(TestDefaults.ProcessHang, GeneralExitSentence, InstallerExitSentence, "Watching the MCP client"))
                    .IsEqualTo(GeneralExitSentence)
                    .Because(run.Records());
                await Assert.That(run.WaitUntilItExits(TestDefaults.ProcessHang)).IsTrue();
            }

            await Assert.That(await RunAsync(Path.Combine(installRoot.Path, "Update.exe"), ["--uninstall", "--silent"])).IsEqualTo(0);
            await WaitOutTheDeferredRemoval(Path.Combine(installRoot.Path, RegistrationTarget.CurrentDirectoryName));
        }
        finally
        {
            await ReclaimAsync(installRoot.Path);
        }

        await Assert.That(ReadUninstallKey($@"{ReleaseLayout.UninstallKeyPath}\{ReleaseLayout.PackId}")).IsEqualTo(realKeyBefore);
        await Assert.That(Describe(ReadStartMenuShortcuts())).IsEqualTo(Describe(startMenuBefore));
    }

    /// <summary>What the general exit says: <c>Startup[14]</c> since 2026-10-08.</summary>
    /// <remarks>
    /// <i>Corrected 2026-10-08 (previously <c>Startup[9]</c>, "no client to serve and
    /// is exiting", a launcher gone and a console standard input), D7 a: the one
    /// executable serves a client only under <c>--mcp</c> with a pipe on standard
    /// input, the rig starts it with <c>--mcp</c>, and a console is not a pipe.</i>
    /// </remarks>
    private const string GeneralExitSentence = "standard input is not a pipe, so there is no client to serve";

    /// <summary>
    /// What the installer exit, <c>Startup[8]</c>, said while a build carried it.
    /// </summary>
    /// <remarks>
    /// Kept after Q276 deleted that exit on 2026-09-24 as a sentence the arm stops
    /// on, so a build that grew it back fails naming it. <i>Previously kept because
    /// "the last packed test pack is a 1.1.0 build that still carries it"</i>, which
    /// stopped being true with Q287 a.
    /// </remarks>
    private const string InstallerExitSentence = "started by the installer";

    /// <summary>The <c>InstallLocation</c> the test id's key names, or <c>&lt;absent&gt;</c>.</summary>
    /// <returns>The value, normalised for comparison.</returns>
    private static string InstallLocationOfTheTestKey()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ReleaseLayout.TestUninstallKey);

        return key?.GetValue("InstallLocation") is string location ? Normalised(location) : "<absent>";
    }

    /// <summary>A path as the comparisons above need it: no trailing separator, upper-case.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The comparable form.</returns>
    private static string Normalised(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToUpperInvariant();

    /// <summary>Whether a root still holds both binaries and its <c>Update.exe</c>.</summary>
    /// <param name="root">The install root.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool IsACompleteInstall(string root) =>
        File.Exists(Path.Combine(root, "Update.exe"))
        && File.Exists(Path.Combine(root, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.AppFileName));

    /// <summary>Velopack's own log for one pack id, the file it rotated out included, as text.</summary>
    /// <remarks>
    /// <b>Where Velopack's logger writes on Windows</b> at 1.2.161:
    /// <c>%LOCALAPPDATA%\velopack\velopack_&lt;pack id&gt;.log</c>, read in
    /// <c>WindowsVelopackLocator</c>'s source, and rotated to <c>.old</c> past a size.
    /// Read with every share, because another run's <c>Update.exe</c> may be writing it.
    /// </remarks>
    /// <param name="packId">The pack id.</param>
    /// <returns>The text, the rotated file's first.</returns>
    private static string VelopackLogOf(string packId)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "velopack");
        var text = new StringBuilder();

        foreach (var name in new[] { $"velopack_{packId}.log.old", $"velopack_{packId}.log" })
        {
            var path = Path.Combine(folder, name);

            if (!File.Exists(path))
            {
                continue;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            _ = text.Append(reader.ReadToEnd());
        }

        return text.ToString();
    }

    /// <summary>The body of the arm, so the reclaim below can be a finally.</summary>
    /// <param name="setup">The test-id installer.</param>
    /// <param name="installRoot">The scratch install root.</param>
    /// <param name="dataRoot">The scratch data root.</param>
    /// <param name="logs">Where Velopack's own logs go.</param>
    /// <param name="planted">Each planted path and the SHA-256 it held.</param>
    /// <param name="sandbox">The scratch client configurations and update feed the installer was given.</param>
    /// <returns>The assertion task.</returns>
    private static async Task InstallTwiceAndUninstall(
        string setup,
        ScratchDirectory installRoot,
        ScratchDirectory dataRoot,
        ScratchDirectory logs,
        IReadOnlyList<(string Path, string Sha256)> planted,
        ClientSandbox sandbox)
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
        //
        // ⚠️ Narrowed 2026-10-08, D7 a (previously both binaries): there is one
        // file, the one a hook runs as and a client is given, and the retired
        // server's name must not come back with an install.
        await Assert.That(File.Exists(
            Path.Combine(installRoot.Path, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.AppFileName))).IsTrue();
        await Assert.That(File.Exists(
            Path.Combine(installRoot.Path, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.RetiredServerFileName))).IsFalse();

        // And the registration the hook wrote names the one executable.
        var written = await File.ReadAllTextAsync(RegistrationRecord.PathFor(dataRoot.Path));

        await Assert.That(written).Contains(RegistrationTarget.AppFileName);
        await Assert.That(written).DoesNotContain(RegistrationTarget.RetiredServerFileName);

        // ⚠️ AND THE SHAPE OF THE RECORD, SINCE Q287 a -- 2026-09-24. *Previously
        // "AND NOT WHAT SHAPE THE RECORD IS IN, WHICH IS A LIMIT OF THIS ARM": the
        // hooks that ran were the last PACKED test installer's, a 1.1.0 build
        // writing `schemaVersion: 1` on the day the per-client record landed, and
        // the shape was left to RegistrationTests.* Every gate packs the test
        // installer from the publish it tests now, so the hooks that just ran are
        // this tree's, and what they wrote is asserted here as a real Setup.exe
        // ran it: the per-client record, one entry per client in the product's own
        // order, each what was asked for, each naming this install's server.
        using (var record = System.Text.Json.JsonDocument.Parse(written))
        {
            var root = record.RootElement;

            await Assert.That(root.GetProperty("schemaVersion").GetInt32()).IsEqualTo(RegistrationRecord.CurrentSchemaVersion);
            await Assert.That(root.GetProperty("intent").GetString()).IsEqualTo(nameof(RegistrationIntent.Install));
            await Assert.That(root.GetProperty("browserAiVersion").GetString()).IsEqualTo(PublishedSlice.BakedVersion());

            var clients = root.GetProperty("clients").EnumerateArray().ToList();

            await Assert.That(string.Join(",", clients.Select(client => client.GetProperty("key").GetString())))
                .IsEqualTo(string.Join(",", RegistrationClient.All.Select(client => client.Key)));

            var server = Path.Combine(installRoot.Path, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.AppFileName);

            foreach (var client in clients)
            {
                await Assert.That(client.GetProperty("isWhatWasAskedFor").GetBoolean())
                    .IsTrue()
                    .Because(client.GetProperty("detail").GetString() ?? "<no detail>");
                await Assert.That(Normalised(client.GetProperty("command").GetString() ?? "<none>")).IsEqualTo(Normalised(server));
            }
        }

        await Unchanged(dataRoot.Path, planted);

        // The hook wrote its record into the DATA root, which is the other half
        // of the same decision: it is still there after an install that deleted
        // the install root twice over.
        await Assert.That(File.Exists(RegistrationRecord.PathFor(dataRoot.Path))).IsTrue();

        // ⚠️ THIS INSTALL'S FOLDER IS ON THE USER'S PATH, ONCE -- Q294 b. Two installs
        // over one root put it there once: the second found it already there.
        var entry = Path.Combine(installRoot.Path, RegistrationTarget.CurrentDirectoryName);

        await Assert.That(PathEntriesNaming(entry)).IsEqualTo(1);

        // ⚠️ WHAT THE HOOKS PUT ON THE MACHINE FOR THE ONE BACKGROUND -- S a, D7 a and
        // T, 2026-10-09. Read first and held together, so that one run names every
        // piece that is wrong.
        //
        // The task: named for the suite's pack id and this install root, starting the
        // one executable as the background, never a second copy, with the two settings
        // the hook read once out of the installer's environment written into its action
        // as arguments, this arm's scratch data root and its empty update feed. Read
        // back from what the scheduler stored, so it is the hook's registration and
        // not the product's composition of it that is held; and none under the
        // shipping id for this root.
        //
        // The definition a person's start registers a missing task from, under the
        // install root and outside current\: the arguments the hook read exist nowhere
        // else once the installer has gone.
        //
        // The clients, in their sandboxed configurations: each names the one
        // executable as a relay, with the data root the installer named, read out of
        // the files the real clients wrote and not out of the record.
        //
        // The toasts' activator: the class derived from the install's own application
        // id, under the user's own classes, starting the one executable.
        var app = Path.Combine(installRoot.Path, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.AppFileName);
        var taskName = SignInTask.NameFor(ReleaseLayout.TestPackId, installRoot.Path);
        var action = SignInTask.ArgumentsFor(dataRoot.Path, sandbox.UpdateFeed);
        var task = StoredTaskOf(taskName);
        var saved = Path.Combine(installRoot.Path, SignInTask.SavedDefinitionFileName);
        var kept = File.Exists(saved) ? StoredTask.Parse(await File.ReadAllTextAsync(saved)) : null;
        var aumid = AppUserModelIdOf(installRoot.Path);

        IReadOnlyList<string> relay = [app, RegistrationTarget.McpArgument, SignInTask.DataRootArgument, dataRoot.Path];

        using (Assert.Multiple())
        {
            await Assert.That(task?.Command is { } command ? Normalised(command) : "<no task>").IsEqualTo(Normalised(app));
            await Assert.That(task?.Arguments).IsEqualTo(action);
            await Assert.That(task?.InstancesPolicy).IsEqualTo(StoredTask.IgnoreNew);
            await Assert.That(ScheduledTasks.DefinitionOf(SignInTask.NameFor(ReleaseLayout.PackId, installRoot.Path))).IsNull();

            await Assert.That(kept?.Command is { } savedCommand ? Normalised(savedCommand) : "<no saved definition>").IsEqualTo(Normalised(app));
            await Assert.That(kept?.Arguments).IsEqualTo(action);
            await Assert.That(kept?.InstancesPolicy).IsEqualTo(StoredTask.IgnoreNew);

            await Assert.That(ClaudeEntryIn(sandbox.ClaudeConfig)).IsEqualTo(Spelled(relay));
            await Assert.That(CodexEntryIn(sandbox.CodexHome)).IsEqualTo(Spelled(relay));

            await Assert.That(ToastActivatorOf(aumid)).IsEqualTo(ExpectedToastActivator(aumid, app));
        }

        // ⚠️ THE HOOK'S OWN LINES ARE IN THE INSTALLER'S LOG -- 2026-10-10, the texts
        // review's "installer's-log Mirror". VelopackStartup.Mirror wrote them into a
        // list that a hook process exits before anything reads, so none reached a log:
        // 0 of the 332 hooks this machine's Velopack logs recorded carried one, read the
        // same day. The task's name holds this install root's key, so the line found is
        // this arm's and no other run's. Planted red against the hooks as they were.
        await Assert.That(VelopackLogOf(ReleaseLayout.TestPackId))
            .Contains($"-- sign-in task ({RegistrationIntent.Install}): {TaskChange.Registered}. The task '{taskName}' is registered.");

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

        // And the uninstall hook took this install's folder off the PATH.
        await Assert.That(PathEntriesNaming(entry)).IsEqualTo(0);

        // ⚠️ AND EVERYTHING ELSE THE INSTALL PUT ON THE MACHINE -- 2026-10-09: the task,
        // the definition beside the install, both clients' registrations and the
        // toasts' activator.
        using (Assert.Multiple())
        {
            await Assert.That(ScheduledTasks.DefinitionOf(taskName)).IsNull();
            await Assert.That(File.Exists(saved)).IsFalse();
            await Assert.That(ClaudeEntryIn(sandbox.ClaudeConfig)).IsNull();
            await Assert.That(CodexEntryIn(sandbox.CodexHome)).IsNull();
            await Assert.That(ToastActivatorOf(aumid)).IsNull();
        }
    }

    /// <summary>How many entries of the real user PATH name a folder.</summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The count.</returns>
    private static int PathEntriesNaming(string folder) =>
        RegistryUserPathStore.User.Read() is { } value
            ? UserPath.Segments(value.Text).Count(segment => UserPath.Names(segment, folder))
            : 0;

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
        // THE INSTALL'S OWN UPDATE.EXE FIRST -- 2026-10-09. An arm that put a stand-in
        // at its path and died before putting it back would otherwise run the
        // stand-in, a copy of cmd.exe, as the uninstaller; one that cannot be put back
        // is not run at all, and the steps below take back what they can by name.
        var theInstallersOwn = PutTheUpdaterBack(installRoot);
        var update = Path.Combine(installRoot, "Update.exe");

        if (theInstallersOwn && File.Exists(update))
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

        // ⚠️ THE SCRATCH ROOT'S OWN PATH ENTRY, EXACTLY, AND NO OTHER -- Q294 b. An arm
        // that died between its install and its uninstall would otherwise leave a
        // folder that is gone on the person's PATH. The entry is this root's and can be
        // nobody else's; the product's own remover is the one used, so a real
        // install's entry is out of its reach for the reason it is out of the hooks'.
        _ = UserPath.Remove(RegistryUserPathStore.User, Path.Combine(installRoot, RegistrationTarget.CurrentDirectoryName));

        // ⚠️ THE SCRATCH ROOT'S OWN SIGN-IN TASK, BY ITS NAME -- Q282 a. The test
        // pack's install hook registers `BrowserAI.app.test sign-in <this root's
        // key>`, and an arm that died before its uninstall would leave it in the
        // person's scheduler. The name is this root's under the test id and can be
        // nobody else's.
        _ = ScheduledTasks.Instance.Remove(SignInTask.NameFor(ReleaseLayout.TestPackId, installRoot));

        try
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(ReleaseLayout.TestUninstallKey, throwOnMissingSubKey: false);
        }
#pragma warning disable CA1031 // Same: the next run's capability check is what reports a key that would not go.
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        // ⚠️ NOT THE TOASTS' ACTIVATOR, said here because it is the one thing an
        // install leaves that this reclaim cannot take back. The install hook
        // registers a class under the user's own classes for the suite pack's
        // application id, and the uninstall above removes it; but no test may write
        // under those classes (tests/BrowserAI.Tests/BannedSymbols.txt bans
        // ToastActivatorRegistration.UserClasses), so a run whose uninstall never ran
        // leaves that class behind, and no clearance reading looks there.
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
        _ = SuiteEnvironment.RequireReleaseInstaller();

        // ⚠️ THE TWIN, SINCE Q287 a -- 2026-09-24. *Previously the shipping pack in
        // Releases, which a release cut left beside the test pack of the same
        // publish.* The gate packs the test installer from its own publish now, and
        // the last release's shipping pack beside it would be two publishes; so
        // `New-Release.ps1 -TestPackOnly` packs the same directory once more under
        // the shipping id into test-pack's `twin` directory, and a release cut still
        // leaves its own shipping pack of the same version in Releases.
        var mine = ReleaseLayout.FullPackage(test: true);

        await Assert.That(mine is null ? "no test .nupkg" : string.Empty).IsEmpty();

        var shipping = ReleaseLayout.ShippingTwinOf(mine!);

        await Assert.That(shipping is null ? $"no shipping-id package of {mine!.Name}'s version under {ReleaseLayout.TwinDirectory} or {ReleaseLayout.Directory}" : string.Empty)
            .IsEmpty();

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
    /// The suite's installer carries, byte for byte, the two binaries this run
    /// tested.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Q287, decided 2026-09-24 by the maintainer, verbatim: <i>"Q287 a"</i> --
    /// the real-installer arms exercise THIS tree's hooks.</b> Until that day they
    /// installed the last pack a release cut had left in <c>Releases\test-pack</c>, a
    /// 1.1.0 build, so every hook behaviour added since was untested by a real
    /// <c>Setup.exe</c>. Every gate driver now packs the test installer from the
    /// publish it tests, and the <c>release installer</c> capability is absent when
    /// the pack is not byte for byte the published slices -- which
    /// <see cref="PublishedSlice.EnsureFresh"/> in turn holds to this tree.
    /// </para>
    /// <para>
    /// <b>The comparison is watched in both directions over archives this arm
    /// composes</b>, because a live pair that agrees is indistinguishable from a
    /// comparison that stopped reading. <b>Planted red 2026-09-24</b>: the live half
    /// against a 1.1.0 test pack, which named the server as not the published one.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSuitesInstallerIsTheBuildThisRunTested()
    {
        using var scratch = ScratchDirectory.Create("test-pack-bytes");

        // One executable since 2026-10-08, D7 a (previously the server and the app,
        // each compared on its own).
        var app = Path.Combine(scratch.Path, "app.bin");

        await File.WriteAllTextAsync(app, "the one executable's bytes");

        (string Entry, string Published)[] binaries = [("lib/app/BrowserAI.exe", app)];

        var same = Path.Combine(scratch.Path, "same.nupkg");
        var other = Path.Combine(scratch.Path, "other.nupkg");
        var missing = Path.Combine(scratch.Path, "missing.nupkg");

        await PackageAsync(same, ("lib/app/BrowserAI.exe", "the one executable's bytes"));
        await PackageAsync(other, ("lib/app/BrowserAI.exe", "the one executable's bytes, one release ago"));
        await PackageAsync(missing, ("lib/app/payload/payload.json", "a package with no executable"));

        await Assert.That(ReleaseLayout.MismatchBetween(same, binaries)).IsNull();
        await Assert.That(ReleaseLayout.MismatchBetween(other, binaries)).Contains("BrowserAI.exe");
        await Assert.That(ReleaseLayout.MismatchBetween(missing, binaries)).Contains("carries no lib/app/BrowserAI.exe");

        // ---- The live half: this run's installer is this run's build.
        _ = SuiteEnvironment.RequireReleaseInstaller();

        await Assert.That(ReleaseLayout.TestPackMismatch).IsNull();
    }

    /// <summary>Writes a package holding the given entries.</summary>
    private static async Task PackageAsync(string path, params (string Entry, string Text)[] entries)
    {
        await using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var (entry, text) in entries)
        {
            await using var writer = new StreamWriter(await archive.CreateEntry(entry).OpenAsync());
            await writer.WriteAsync(text);
        }
    }

    /// <summary>
    /// The pack carries the one executable alone and names it as its main one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-10-08 (previously "The packed release carries both
    /// executables and names the configuration app as its main one", read from the
    /// shipping package, <c>FullPackage(test: false)</c>)</b>: the shipping package
    /// under <c>Releases\</c> is the last release's, 1.1.0 with two binaries, until a
    /// release is cut from a tree that has one, so the arm read the past and went red
    /// on every gate after D7 a, first in lane REC's gate on <c>6694dda8</c>. It reads
    /// the test pack, which every gate packs from the tree it tests with the same
    /// <c>--mainExe</c> and <c>--shortcuts</c> the shipping pack takes
    /// (<c>build/New-Release.ps1</c>).
    /// </para>
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
    public async Task ThePackNamesTheOneExecutableAsItsMainExeAndCarriesItAlone()
    {
        _ = SuiteEnvironment.RequireReleaseInstaller();

        var package = ReleaseLayout.FullPackage(test: true);

        await Assert.That(package is null ? "no test pack .nupkg" : string.Empty).IsEmpty();

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
        await Assert.That(text).DoesNotContain($"<mainExe>{RegistrationTarget.RetiredServerFileName}</mainExe>");
        await Assert.That(text).Contains("<shortcutLocations>StartMenuRoot</shortcutLocations>");

        // The one executable, at the root of the application directory, and not
        // the retired server beside it (D7 a, 2026-10-08; previously both
        // binaries). The stub is another file and is Velopack's, not ours.
        var app = archive.Entries.Select(entry => entry.FullName).ToList();

        await Assert.That(app).Contains($"lib/app/{RegistrationTarget.AppFileName}");
        await Assert.That(app).DoesNotContain($"lib/app/{RegistrationTarget.RetiredServerFileName}");

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
