// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using Microsoft.Extensions.Logging;
using Velopack;

namespace BrowserAI.Updates;

/// <summary>What the start Velopack made after an apply found.</summary>
internal enum AfterUpdateOutcome
{
    /// <summary>This process is the version the apply was for: the update is installed.</summary>
    Installed,

    /// <summary>This process is another version, the one the apply replaced: the update failed.</summary>
    Failed,

    /// <summary>The argument or this build's own version is not a version at all, so nothing can be said.</summary>
    NotAVersion,
}

/// <summary>
/// The start Velopack makes once an apply has run: <c>--after-update &lt;version&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The after-update work happens in Velopack's restart and never in
/// <c>--veloapp-updated</c></b>, RESOLUTIONS of 2026-10-08: the hook runs inside
/// a 15-second limit and anything it starts from the install root is ended by the
/// kill pass that follows it, measured 2026-10-08 at Velopack 1.2.161, 3 runs of
/// 3 (step 0 of the one-binary build, <c>.work/step0-velopack/FINDINGS.md</c>).
/// The restart comes after that pass.
/// </para>
/// <para>
/// ⚠️ <b>A failed apply starts the OLD version with the SAME arguments</b>, and
/// with the same <c>VELOPACK_RESTART=true</c>, measured the same day 3 runs of 3:
/// nothing in the arguments or the environment says which way it went. So the
/// background puts the version it is installing into the arguments
/// (<see cref="RestartArguments"/>), and this start compares it with its own:
/// the same version is an install, any other is a failure. <b>Every BrowserAI
/// process is gone either way</b>, so the start asks the Task Scheduler for the
/// background in both outcomes.
/// </para>
/// </remarks>
internal static class AfterUpdate
{
    /// <summary>The argument the restart carries, followed by the version the apply was for.</summary>
    public const string Argument = "--after-update";

    /// <summary>What the background hands Velopack to start BrowserAI with after an apply.</summary>
    /// <param name="version">The version being installed.</param>
    /// <returns><c>--after-update</c> and the version.</returns>
    public static IReadOnlyList<string> RestartArguments(string version)
    {
        ArgumentException.ThrowIfNullOrEmpty(version);
        return [Argument, version];
    }

    /// <summary>The version an after-update start was told to expect.</summary>
    /// <param name="arguments">The process's arguments.</param>
    /// <returns>The value after <see cref="Argument"/>, or <see langword="null"/> when it is absent or has no value.</returns>
    public static string? TargetIn(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        for (var index = 0; index + 1 < arguments.Count; index++)
        {
            if (string.Equals(arguments[index], Argument, StringComparison.Ordinal))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

    /// <summary>Tells an install from a failure.</summary>
    /// <remarks>
    /// <b>Compared the way Velopack compares versions</b>, through its own
    /// <see cref="SemanticVersion"/>: equal when the four numbers and the
    /// pre-release labels are, the labels without regard to case and build metadata
    /// ignored (Velopack 1.2.161, <c>SemanticVersion.cs</c>, <c>CompareTo</c> and
    /// <c>Equals</c>).
    /// </remarks>
    /// <param name="target">The version the apply was for.</param>
    /// <param name="running">The version this process is.</param>
    /// <returns>What the start found.</returns>
    public static AfterUpdateOutcome Judge(string target, string running)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(running);

        if (!SemanticVersion.TryParse(target, out var wanted) || !SemanticVersion.TryParse(running, out var current))
        {
            return AfterUpdateOutcome.NotAVersion;
        }

        return wanted == current ? AfterUpdateOutcome.Installed : AfterUpdateOutcome.Failed;
    }

    /// <summary>Where Velopack writes its own account of an apply for one pack.</summary>
    /// <remarks>
    /// <c>%LocalAppData%\velopack\velopack_&lt;packId&gt;.log</c>, read at Velopack
    /// 1.2.161 (<c>src/lib-rust/src/logging.rs:57-72</c>), and a failed apply's
    /// account was found at the end of it on 2026-10-08, 3 runs of 3 (step 0 of the
    /// one-binary build, <c>.work/step0-velopack/FINDINGS.md</c>).
    /// </remarks>
    /// <param name="localAppData">The user's local application data folder.</param>
    /// <param name="packId">The pack id the install came from.</param>
    /// <returns>The log's path.</returns>
    public static string VelopackLogPath(string localAppData, string packId)
    {
        ArgumentException.ThrowIfNullOrEmpty(localAppData);
        ArgumentException.ThrowIfNullOrEmpty(packId);

        return Path.Combine(localAppData, "velopack", $"velopack_{packId}.log");
    }

    /// <summary>
    /// Raises the installed or the failed toast for an after-update start, then asks
    /// for the background.
    /// </summary>
    /// <param name="target">The version the apply was for.</param>
    /// <param name="running">The version this process is.</param>
    /// <param name="velopackLog">Velopack's log for this pack, named in the log line of a failure.</param>
    /// <param name="toasts">The update toasts.</param>
    /// <param name="askForBackground">Asks the Task Scheduler for the background; called in every outcome.</param>
    /// <param name="logger">Where the outcome is recorded.</param>
    /// <returns>What the start found.</returns>
    public static AfterUpdateOutcome Report(
        string target,
        string running,
        string velopackLog,
        IUpdateToasts toasts,
        Action askForBackground,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(askForBackground);
        ArgumentNullException.ThrowIfNull(logger);

        var outcome = Judge(target, running);

        try
        {
            switch (outcome)
            {
                case AfterUpdateOutcome.Installed:
                    BackgroundUpdateLog.AfterUpdateInstalled(logger, target);
                    toasts.Installed(target);
                    break;

                case AfterUpdateOutcome.Failed:
                    BackgroundUpdateLog.AfterUpdateFailed(logger, target, running, velopackLog);
                    toasts.Failed(target);
                    break;

                default:
                    BackgroundUpdateLog.AfterUpdateNotAVersion(logger, target, running);
                    break;
            }
        }
#pragma warning disable CA1031 // A toast that cannot be raised is a log line: the background must still be asked for, or BrowserAI stays down after the update.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            BackgroundUpdateLog.ToastFailed(logger, outcome.ToString(), failure);
        }

        askForBackground();
        return outcome;
    }
}
