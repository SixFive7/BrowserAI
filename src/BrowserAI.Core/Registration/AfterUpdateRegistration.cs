// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using Microsoft.Extensions.Logging;

namespace BrowserAI.Registration;

/// <summary>
/// The registration pass an update ran inside its hook until 2026-11-18, run by the
/// background's first start after the update instead.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The maintainer's 174, 2026-11-18, in his words verbatim:</b> <i>"174 rec - also
/// explain to me why velopack even has this limit and cehck if we cannot disable or
/// increase that limit"</i>. Velopack kills the update hook at 15 s, a literal in its own
/// code with no option, pack flag or variable to change it, and its maintainer's reason
/// is that a longer hook would hang an install with nothing on the screen to say why
/// (velopack/velopack#303). A registration slowed by a busy or just-started machine could
/// take the hook past that limit and cut off the steps after it, so the update hook keeps
/// its local steps, which take milliseconds, and this pass runs where nothing outside
/// BrowserAI limits the wait: with RegisterAI's own budget,
/// <see cref="RegistrationBudget.Unbounded"/>.
/// </para>
/// <para>
/// <b>What it decides is what the update hook decided</b>: an update leaves an entry of
/// ours that still resolves exactly as it is, re-points one that names a file that is
/// gone and writes one that is absent (<see cref="RegistrationIntent.Update"/>), with the
/// arguments the install's hooks saved, and the record carries the outcome with the
/// update's intent, as the hook's did. <b>A failed apply starts the old version the same
/// way</b> (<see cref="Updates.AfterUpdate"/>), and its pass is right as well: it leaves
/// every entry that resolves as it is and points the rest at what is installed.
/// </para>
/// </remarks>
internal static class AfterUpdateRegistration
{
    /// <summary>Runs the update's registration pass for every client and writes the record.</summary>
    /// <param name="imagePath">The running image, which decides the command.</param>
    /// <param name="tool">RegisterAI.</param>
    /// <param name="dataRoot">The data root the record goes to.</param>
    /// <param name="version">This build's version, for the record.</param>
    /// <param name="packId">The pack id the install came from, which decides whether its saved data root is read.</param>
    /// <param name="logger">Where the pass reports.</param>
    /// <returns>One pass per client.</returns>
    public static async Task<IReadOnlyList<ClientRegistration>> RunAsync(
        string? imagePath,
        IRegisterAi tool,
        string dataRoot,
        string version,
        string? packId,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentNullException.ThrowIfNull(logger);

        var passes = await McpRegistrar.ApplyAsync(
            RegistrationClient.All,
            RegistrationIntent.Update,
            imagePath,
            tool,
            logger,
            commandArguments: await InstallerSettings.RelayArgumentsForAsync(imagePath, packId).ConfigureAwait(false),
            budget: RegistrationBudget.Unbounded).ConfigureAwait(false);

        await HookRegistration.WriteRecordAsync(dataRoot, passes, RegistrationIntent.Update, version, logger).ConfigureAwait(false);

        return passes;
    }

    /// <summary>Runs <see cref="RunAsync"/> without the caller waiting for it, and lets nothing it throws end the process.</summary>
    /// <param name="imagePath">The running image.</param>
    /// <param name="tool">RegisterAI.</param>
    /// <param name="dataRoot">The data root the record goes to.</param>
    /// <param name="version">This build's version.</param>
    /// <param name="packId">The pack id the install came from.</param>
    /// <param name="logger">Where the pass reports.</param>
    public static void StartInBackground(string? imagePath, IRegisterAi tool, string dataRoot, string version, string? packId, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(logger);

        _ = Task.Run(async () =>
        {
            try
            {
                _ = await RunAsync(imagePath, tool, dataRoot, version, packId, logger).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // A background serves its sessions whatever a registration does.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                try
                {
                    AfterUpdateRegistrationLog.Failed(logger, failure);
                }
#pragma warning disable CA1031 // A logger that throws must not defeat the catch that was reporting through it.
                catch (Exception)
#pragma warning restore CA1031
                {
                }
            }
        });
    }
}

/// <summary>Source-generated log messages for the pass after an update.</summary>
internal static partial class AfterUpdateRegistrationLog
{
    /// <summary>The pass threw, which the registrar is written never to do.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="failure">What was thrown.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "The registration pass after the update failed, so the clients' entries were left as they were.")]
    public static partial void Failed(ILogger logger, Exception failure);
}
