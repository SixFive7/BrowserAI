// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Interop;
using BrowserAI.Logging;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Registration;

/// <summary>Everything one Velopack lifecycle hook did.</summary>
/// <remarks>
/// <b>Two answers rather than one, since 2026-09-15.</b> A hook used to do
/// exactly one thing — point a client at this build, or unpoint it — and the
/// uninstall hook now also decides what becomes of the data root. The second
/// answer is returned rather than only logged because the log it would be
/// written into is inside the directory it is about: on a removal that file is
/// gone, so the outcome goes to <c>VelopackStartup</c>, which mirrors it into
/// the installer's own log.
/// </remarks>
/// <param name="Registration">What the registration pass concluded.</param>
/// <param name="Disposal">
/// What was decided about the data root, or <see langword="null"/> when the hook
/// was not an uninstall or could not get far enough to ask.
/// </param>
internal sealed record HookOutcome(RegistrationReport Registration, DataRootDisposalReport? Disposal);

/// <summary>
/// The whole body of a Velopack lifecycle hook: open a log, register or
/// unregister, write down what happened, and get out.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>A hook opens its own log rather than using the process's.</b>
/// <c>Program.Main</c> buffers Velopack's own records and replays them once the
/// install root is known — which works for an ordinary start and cannot work for
/// a hook, because <c>VelopackApp.Run()</c> <b>exits the process</b> when it has
/// served one. Anything a hook merely buffers is discarded at that exit. So the
/// destination is established here, inside the hook, and every record is on disk
/// before the callback returns.
/// </para>
/// <para>
/// ⚠️ <b>The log and the record go to the DATA root, and the image path decides
/// only what gets registered — corrected 2026-09-15 (previously "the install
/// root is derived from the running image … so the path that is registered and
/// the directory the record lands in cannot disagree").</b> They cannot
/// disagree, and they were both wrong: the install root is the directory
/// <c>Setup.exe</c> renames aside and deletes and uninstall empties, so a
/// registration record written there is destroyed by exactly the events somebody
/// would read it after. The data root is
/// <see cref="Hosting.LocalAppDataPaths.Default"/> — reachable from a hook
/// without a locator, which is the property that made it possible to stop
/// deriving one. The image path is still what
/// <see cref="RegistrationTarget"/> judges, because what a client is pointed at
/// is a fact about the binary.
/// </para>
/// <para>
/// <b>Consulting <c>VelopackLocator</c> inside a fast-exit callback would add an
/// unproven dependency to the one code path that must not have any</b>, and it
/// is not needed for either question: the registered path comes from
/// <see cref="Environment.ProcessPath"/> and the data root is a constant.
/// </para>
/// <para>
/// <b>Nothing here throws.</b> A hook that throws breaks the installer; a hook
/// that swallows leaves a product nobody can reach and nothing to say so. The
/// third option is what this is: catch, log, record, return.
/// </para>
/// </remarks>
internal static class HookRegistration
{
    /// <summary>
    /// Runs one pass on behalf of a hook, against the real client and the real
    /// filesystem.
    /// </summary>
    /// <param name="intent">Which hook is asking.</param>
    /// <param name="version">The version Velopack handed the callback.</param>
    /// <returns>What happened.</returns>
    public static HookOutcome Run(RegistrationIntent intent, string version) =>
        Run(
            intent,
            version,
            Environment.ProcessPath,
            new ClientCommandLine(),
            new LocalAppDataPaths(LocalAppDataPaths.Overridden()),
            DataRootDisposal.IsSilent(ProcessLiveness.ParentCommandLine()),
            message => UserPrompt.AskYesNo(DataRootDisposal.PromptTitle, message));

    /// <summary>
    /// Runs one pass against a supplied image path and client seam.
    /// </summary>
    /// <param name="intent">Which hook is asking.</param>
    /// <param name="version">The version Velopack handed the callback.</param>
    /// <param name="imagePath">The running image, or what stands in for it.</param>
    /// <param name="commands">The seam over starting the client.</param>
    /// <param name="paths">
    /// Where the log and the record go, and — on an uninstall — what is offered
    /// for deletion. <b>Required rather than defaulted</b>: a test that forgot
    /// it would write into the developer's own data root and offer to delete it.
    /// </param>
    /// <param name="silent">
    /// Whether the uninstall that started this hook was unattended. Ignored by
    /// every other intent.
    /// </param>
    /// <param name="ask">The question seam. See <see cref="DataRootDisposal.Choose"/>.</param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// The overload the suite drives. It is the same body: the only things a
    /// test replaces are the ones that need an installed Velopack layout,
    /// somebody else's executable, and a human at the screen.
    /// </remarks>
    public static HookOutcome Run(
        RegistrationIntent intent,
        string version,
        string? imagePath,
        IRegistrationCommand commands,
        IAppPaths paths,
        bool silent = true,
        Func<string, bool>? ask = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(paths);

        try
        {
            RegistrationReport report;
            DataRootDisposalReport? disposal = null;

            // The log's own scope, closed before anything is deleted: the file is
            // inside the data root, and a handle this process still holds would
            // be the one node a removal could not remove.
            using (var log = ProcessLog.Create(paths, LogLevel.Information))
            {
                var logger = log.Factory.CreateLogger("BrowserAI.Registration");

                RegistrationHookLog.HookRunning(logger, intent, version, imagePath ?? "<unknown>");

                report = McpRegistrar.Apply(intent, imagePath, commands, logger);

                WriteRecord(paths.RootAppDir, report, intent, version, logger);

                // ⚠️ LAST, AND ONLY ON AN UNINSTALL. It is the only part of a
                // hook that may wait for a human, so everything an uninstall must
                // not skip is already on disk above it: a hook killed at its
                // 60-second budget for want of an answer has still unregistered
                // the client and still written its record, and the data root is
                // kept by the same default every other unanswered case takes.
                if (intent is RegistrationIntent.Uninstall)
                {
                    disposal = DataRootDisposal.Choose(paths, silent, ask ?? (_ => false), logger);
                }
            }

            return new HookOutcome(report, disposal is null ? null : DataRootDisposal.Remove(disposal));
        }
#pragma warning disable CA1031 // The outermost boundary of a fast-exit callback. Nothing may escape into the installer, including a failure to open a log.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            // The log itself could not be opened, so there is nowhere to say
            // this. The record is the only remaining channel and it is tried
            // anyway -- a silent install is the one outcome this whole mechanism
            // exists to prevent.
            var report = new RegistrationReport(
                RegistrationStatus.Failed,
                $"The registration hook could not even open its log: {failure.Message}. BrowserAI may be installed and unregistered.",
                null,
                imagePath);

            TryWriteWithoutALogger(paths.RootAppDir, report, intent, version);

            // The data root is KEPT on this path and nothing is asked. A hook
            // that could not open a log is a hook that cannot report what it
            // deleted, and an unreportable deletion of somebody's browsers is the
            // one outcome that must not be reachable.
            return new HookOutcome(report, null);
        }
    }

    private static void WriteRecord(string root, RegistrationReport report, RegistrationIntent intent, string version, ILogger logger)
    {
        var path = RegistrationRecord.PathFor(root);

        try
        {
            _ = RegistrationRecord.Write(root, report, intent, version, DateTimeOffset.Now);
            RegistrationLog.RecordWritten(logger, path, report.Status);
        }
#pragma warning disable CA1031 // A record that cannot be written must not turn an otherwise successful registration into a failed install.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            RegistrationLog.RecordNotWritten(logger, path, failure);
        }
    }

    private static void TryWriteWithoutALogger(string root, RegistrationReport report, RegistrationIntent intent, string version)
    {
        try
        {
            _ = RegistrationRecord.Write(root, report, intent, version, DateTimeOffset.Now);
        }
#pragma warning disable CA1031 // Last resort. If this fails too there is nothing left that could report it, and an installer must still succeed.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}

/// <summary>Source-generated log messages for the hook itself.</summary>
internal static partial class RegistrationHookLog
{
    /// <summary>
    /// The first line a hook writes, and often the only evidence that a hook ran
    /// at all.
    /// </summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="intent">Which hook.</param>
    /// <param name="version">The version Velopack passed.</param>
    /// <param name="imagePath">The image the hook is running as, which is what gets registered.</param>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Velopack {Intent} hook running for BrowserAI {Version}. image={ImagePath}")]
    public static partial void HookRunning(ILogger logger, RegistrationIntent intent, string version, string imagePath);
}
