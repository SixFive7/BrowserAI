// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Logging;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Registration;

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
/// <b>The install root is derived from the running image, not from the
/// locator.</b> Velopack invokes hooks on <c>--mainExe</c>, which is
/// <c>&lt;root&gt;\current\BrowserAI.exe</c>, so
/// <see cref="Environment.ProcessPath"/>'s grandparent is the root — and
/// <see cref="RegistrationTarget"/> establishes both from the same string, in
/// one check, so the path that is registered and the directory the record lands
/// in cannot disagree. Consulting <c>VelopackLocator</c> inside a fast-exit
/// callback would add an unproven dependency to the one code path that must not
/// have any.
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
    public static RegistrationReport Run(RegistrationIntent intent, string version) =>
        Run(intent, version, Environment.ProcessPath, new ClientCommandLine());

    /// <summary>
    /// Runs one pass against a supplied image path and client seam.
    /// </summary>
    /// <param name="intent">Which hook is asking.</param>
    /// <param name="version">The version Velopack handed the callback.</param>
    /// <param name="imagePath">The running image, or what stands in for it.</param>
    /// <param name="commands">The seam over starting the client.</param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// The overload the suite drives. It is the same body: the only things a
    /// test replaces are the two that need an installed Velopack layout and
    /// somebody else's executable.
    /// </remarks>
    public static RegistrationReport Run(RegistrationIntent intent, string version, string? imagePath, IRegistrationCommand commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        // Resolved first, and used for both the log and the record, so that a
        // pass which refused to register still writes its refusal somewhere a
        // person will find it. A refused path yields no root -- there is no
        // install to write into -- so those records go to the default layout,
        // which is where a default install is.
        var root = RegistrationTarget.TryResolve(imagePath, out var target, out _)
            ? target!.InstallRoot
            : null;

        var paths = new LocalAppDataPaths(root);

        try
        {
            using var log = ProcessLog.Create(paths, LogLevel.Information);
            var logger = log.Factory.CreateLogger("BrowserAI.Registration");

            RegistrationHookLog.HookRunning(logger, intent, version, imagePath ?? "<unknown>");

            var report = McpRegistrar.Apply(intent, imagePath, commands, logger);

            WriteRecord(paths.RootAppDir, report, intent, version, logger);

            return report;
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

            return report;
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
