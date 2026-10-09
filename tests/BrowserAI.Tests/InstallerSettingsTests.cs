// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Interop;
using BrowserAI.Registration;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>
/// The hooks' one read of the installer's settings: the installer's environment
/// first, and for a setting it does not name, what the install wrote into its task.
/// </summary>
/// <remarks>
/// <para>
/// <b>Step 5 of the one-binary build, 2026-10-08</b>: no running BrowserAI reads a
/// <c>BROWSERAI_</c> variable, and the hooks read the installer's environment once,
/// in <see cref="InstallerSettings.Read"/>, and write <c>--data-root</c> and
/// <c>--update-source</c> into the task's action. H2 a, the maintainer's words of
/// 2026-10-08 verbatim, <i>"h2 a"</i>, fixes his install's update folder at install
/// time.
/// </para>
/// <para>
/// <b>In process, with the environment a seam and the saved definition a real file</b>
/// in a scratch install root, so nothing here sets a process-wide variable and no arm
/// runs beside nothing.
/// </para>
/// </remarks>
internal sealed class InstallerSettingsTests
{
    private const string Folder = @"C:\BrowserAI-feed";

    /// <summary>
    /// An update hook, which runs with no <c>BROWSERAI_</c> variable, keeps the data
    /// root and the update folder the install wrote into its task.
    /// </summary>
    /// <remarks>
    /// <b>Why it has none</b>: Velopack runs the update hook under <c>Update.exe</c>,
    /// which the background started, and the background's environment is the one the
    /// Task Scheduler built. Read from the environment alone, the first update would
    /// register the task again with neither setting, and the updated background would
    /// serve the default data root and check GitHub where H2 a fixed a folder.
    /// <b>Planted red 2026-10-08</b> with the saved definition left unread: both
    /// settings came back empty.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnUpdateHookWithNoInstallersVariablesKeepsWhatTheInstallWroteIntoItsTask()
    {
        using var install = ScratchDirectory.Create("installer-settings-update");
        var dataRoot = Path.Combine(install.Path, "Tom & Jerry's data");

        Save(install.Path, SignInTask.ArgumentsFor(dataRoot, Folder));

        var read = InstallerSettings.Read(install.Path, NoVariables);

        await Assert.That(read.DataRoot).IsEqualTo(dataRoot);
        await Assert.That(read.UpdateSource).IsEqualTo(Folder);

        // And it is the same read a person's start makes for its data root.
        await Assert.That(SignInTask.DataRootIn(SignInTask.SavedDefinition(install.Path))).IsEqualTo(dataRoot);
    }

    /// <summary>
    /// The installer's environment wins over what the install saved, and with neither
    /// there is nothing: the default data root and the production feed.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheInstallersEnvironmentWinsAndWithNeitherThereIsNothing()
    {
        using var install = ScratchDirectory.Create("installer-settings-environment");
        var saved = Path.Combine(install.Path, "saved");
        var named = Path.Combine(install.Path, "named");

        Save(install.Path, SignInTask.ArgumentsFor(saved, Folder));

        var read = InstallerSettings.Read(
            install.Path,
            name => name switch
            {
                LocalAppDataPaths.RootVariable => named,
                UpdateConfiguration.FeedVariable => @"C:\another-feed",
                _ => null,
            });

        await Assert.That(read.DataRoot).IsEqualTo(named);
        await Assert.That(read.UpdateSource).IsEqualTo(@"C:\another-feed");

        // A fresh install has no saved definition: nothing named, nothing saved.
        using var fresh = ScratchDirectory.Create("installer-settings-fresh");
        var nothing = InstallerSettings.Read(fresh.Path, NoVariables);

        await Assert.That(nothing.DataRoot).IsNull();
        await Assert.That(nothing.UpdateSource).IsNull();

        // And a task written with neither setting names neither.
        Save(fresh.Path, SignInTask.ArgumentsFor(null, null));

        var plain = InstallerSettings.Read(fresh.Path, NoVariables);

        await Assert.That(plain.DataRoot).IsNull();
        await Assert.That(plain.UpdateSource).IsNull();
    }

    /// <summary>
    /// A relative data root is ignored, not resolved, from the environment and from a
    /// saved definition alike, and a definition that is not XML names nothing.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARelativeRootIsIgnoredAndADefinitionThatIsNotXmlNamesNothing()
    {
        using var install = ScratchDirectory.Create("installer-settings-relative");

        Save(install.Path, SignInTask.ArgumentsFor("relative\\root", null));

        var read = InstallerSettings.Read(install.Path, name => name == LocalAppDataPaths.RootVariable ? "also\\relative" : null);

        await Assert.That(read.DataRoot).IsNull();

        await File.WriteAllTextAsync(Path.Combine(install.Path, SignInTask.SavedDefinitionFileName), "not a task <");

        await Assert.That(SignInTask.DataRootIn(SignInTask.SavedDefinition(install.Path))).IsNull();
        await Assert.That(SignInTask.UpdateSourceIn(SignInTask.SavedDefinition(install.Path))).IsNull();
    }

    /// <summary>An environment that names no variable.</summary>
    private static readonly Func<string, string?> NoVariables = static _ => null;

    /// <summary>Saves a definition the way the hooks do, beside the install.</summary>
    /// <param name="installRoot">The install root.</param>
    /// <param name="arguments">The action's arguments.</param>
    private static void Save(string installRoot, string arguments) =>
        File.WriteAllText(
            Path.Combine(installRoot, SignInTask.SavedDefinitionFileName),
            SignInTask.DefinitionFor(Path.Combine(installRoot, "current", "BrowserAI.exe"), NamedPipes.CurrentUserSid(), installRoot, arguments));
}
