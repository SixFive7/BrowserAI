// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Updates;

namespace BrowserAI.Registration;

/// <summary>
/// What the installer's environment named for this install, read once by the install
/// and update hooks and written into the task's action and the registrations.
/// </summary>
/// <remarks>
/// <para>
/// <b>The design's settings rule, kept by every decision of 2026-10-07 and
/// 2026-10-08</b>: a BrowserAI setting is a command-line argument of the process that
/// uses it, and no running BrowserAI process reads a <c>BROWSERAI_</c> variable. The
/// reason is measured: the task-started host wrote to the default data root although
/// the installer had <c>BROWSERAI_ROOT</c> set to scratch (the one-binary
/// measurement, risk 1). The installer's hooks are the one exception, because
/// Velopack gives a hook no channel but its environment.
/// </para>
/// <para>
/// <b>H2 a and RESOLUTIONS 17</b>: his install takes its updates from a folder on
/// this machine, fixed at install time by the update source the hooks read here and
/// write into the task's action as <c>--update-source</c>.
/// </para>
/// </remarks>
/// <param name="DataRoot">The data root the installer named, or <see langword="null"/> for the default.</param>
/// <param name="UpdateSource">The update source the installer named, or <see langword="null"/> for the production feed.</param>
internal sealed record InstallerSettings(string? DataRoot, string? UpdateSource)
{
    /// <summary>The installer named nothing.</summary>
    public static InstallerSettings None { get; } = new(null, null);

    /// <summary>
    /// The arguments a client's registration starts the relay with: <c>--mcp</c>, and
    /// the data root when the installer named one.
    /// </summary>
    public IReadOnlyList<string> RelayArguments =>
        DataRoot is { Length: > 0 } root
            ? [RegistrationTarget.McpArgument, SignInTask.DataRootArgument, root]
            : [RegistrationTarget.McpArgument];

    /// <summary>What the install's hooks saved beside it: what a running BrowserAI reads, never the environment.</summary>
    /// <remarks>
    /// <b>For the dashboard's Register and Repair</b> (2026-10-10), which run in the
    /// background, where no <c>BROWSERAI_</c> variable is read: the definition the hooks
    /// saved carries the data root and the update source the installer named, exactly
    /// as <see cref="Read"/> falls back to them.
    /// </remarks>
    /// <param name="installRoot">The install whose saved definition is read; <see langword="null"/> when there is none.</param>
    /// <param name="packId">The pack id the install came from: the shipping install's saved data root is never read.</param>
    /// <returns>What the install saved, or <see cref="None"/>.</returns>
    public static InstallerSettings SavedFor(string? installRoot, string? packId)
    {
        var saved = installRoot is { Length: > 0 } root ? SignInTask.SavedDefinition(root) : null;

        return new(StandardLocation.DataRootFor(packId, SignInTask.DataRootIn(saved)), SignInTask.UpdateSourceIn(saved));
    }

    /// <summary>
    /// The hooks' one read of the installer's environment, and for a setting it does
    /// not name, the value the install wrote into its task.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The only read of a <c>BROWSERAI_</c> variable in the product</b>, held so by
    /// <c>HouseRuleTests.NoRunningBrowserAiReadsABrowserAiVariable</c>, and called only
    /// by <see cref="HookRegistration"/>. A relative data root is ignored, not
    /// resolved, for the reason a relative <c>PLAYWRIGHT_BROWSERS_PATH</c> is refused:
    /// it would land somewhere nobody chose and report nothing. <i>Moved here
    /// 2026-10-08 from <c>LocalAppDataPaths.Overridden</c>, which every start called
    /// and which went with step 5 of the one-binary build.</i>
    /// </para>
    /// <para>
    /// ⚠️ <b>The saved definition is what keeps a setting fixed at install time.</b>
    /// Velopack runs the update hook under <c>Update.exe</c>, which the background
    /// started, and the background's environment is the one the Task Scheduler
    /// built, with no <c>BROWSERAI_</c> variable in it. Read from the environment
    /// alone, the first update would register the task again with neither the data
    /// root nor H2 a's update folder, and the updated background would serve the
    /// default root and check GitHub. The uninstall hook, run from Windows' own list
    /// of programs, has no installer's environment either, and stops and offers to
    /// delete the root the install was really using.
    /// </para>
    /// </remarks>
    /// <param name="installRoot">The install the hook runs in, whose saved definition is read; <see langword="null"/> when there is none.</param>
    /// <param name="packId">
    /// The pack id the install came from. ⚠️ <b>The shipping install reads no data root at
    /// all since 2026-10-10</b>, neither the installer's <c>BROWSERAI_ROOT</c> nor one its task
    /// saved: the maintainer's 21 and the way to do it he took that day, relayed in his
    /// words verbatim, <i>"that proposal sounds go"</i>. Its data is in
    /// <c>%LOCALAPPDATA%\BrowserAI</c>, and the suite's test pack keeps the variable. The
    /// update source stays read for both (H2 a).
    /// </param>
    /// <param name="environment">The environment, a seam for the suite; the process's own when <see langword="null"/>.</param>
    /// <returns>What the installer named, or the install saved.</returns>
    public static InstallerSettings Read(string? installRoot, string? packId, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;

        var saved = installRoot is { Length: > 0 } root ? SignInTask.SavedDefinition(root) : null;

        return new(
            StandardLocation.DataRootFor(
                packId,
                environment(LocalAppDataPaths.RootVariable) is { Length: > 0 } named && Path.IsPathFullyQualified(named) ? named : SignInTask.DataRootIn(saved)),
            environment(UpdateConfiguration.FeedVariable) is { Length: > 0 } source ? source : SignInTask.UpdateSourceIn(saved));
    }
}
