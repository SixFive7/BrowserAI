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

    /// <summary>The hooks' one read of the installer's environment.</summary>
    /// <returns>What it named.</returns>
    public static InstallerSettings Read() =>
        new(
            LocalAppDataPaths.Overridden(),
            Environment.GetEnvironmentVariable(UpdateConfiguration.FeedVariable) is { Length: > 0 } source ? source : null);
}
