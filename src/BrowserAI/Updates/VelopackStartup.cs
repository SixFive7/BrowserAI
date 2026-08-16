// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Logging;

namespace BrowserAI.Updates;

/// <summary>
/// The first thing this process does, and the one place
/// <see cref="VelopackApp"/> is built.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b><see cref="VelopackApp.SetAutoApplyOnStartup"/> is called with
/// <see langword="false"/>, and it is mandatory.</b> The default is
/// <see langword="true"/>: on finding a staged package, <c>Run()</c> applies it,
/// <c>exit(0)</c>s and relaunches — <b>detached, with no inherited stdio</b>. An
/// MCP client would see its server exit at handshake time and its pipes die,
/// which is indistinguishable from a crash. <c>ExoFabric/UCC</c> never calls it
/// and runs the default; that is survivable for a foreground tray app and fatal
/// for a stdio child.
/// </para>
/// <para>
/// <b>It runs before anything else, including logging, and that ordering is
/// forced.</b> This call is also what handles the installer's own hook
/// invocations — <c>--veloapp-install</c> and friends, which are fast-exit
/// callbacks with 15–60 s timeouts — so anything that runs first runs inside
/// every hook as well. A logger is attached rather than constructed here for the
/// same reason: the bridge is a delegate over an <see cref="ILogger"/> the
/// caller already owns.
/// </para>
/// <para>
/// <b>One <see cref="VelopackApp.SetLogger"/> call is enough at 1.2.0.</b>
/// [§G landmine 8](../../plan/G-updates.md) required two registrations, one for
/// the runtime <c>UpdateManager</c> and one for the startup hooks; that was
/// fixed upstream and a single registration now reaches the installer, the
/// hooks, <c>UpdateManager</c> and the bridged Rust output
/// ([kb](../../kb/packaging/velopack.md#8-ivelopacklogger-needs-two-registrations)).
/// </para>
/// <para>
/// <b>No hook does any work.</b> They exist to log. The logon scheduled task
/// that <c>--veloapp-install</c> was going to register
/// [is dropped](../../plan/build-order.md#16-the-stray-sweep), and a hook that
/// left a helper running under the install root would be killed by
/// <c>force_stop_package</c> immediately afterwards anyway — it runs after every
/// hook returns.
/// </para>
/// </remarks>
internal static class VelopackStartup
{
    /// <summary>
    /// Runs Velopack's startup handling. Call this before anything else in
    /// <c>Main</c>.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <param name="log">Where Velopack's own output goes.</param>
    public static void Run(string[] args, Action<VelopackLogLevel, string, Exception?> log)
    {
        ArgumentNullException.ThrowIfNull(log);

        VelopackApp.Build()

            // ⚠️ The single most important line in this file. See the remarks.
            .SetAutoApplyOnStartup(false)
            .SetArgs(args ?? [])
            .SetLogger(new DelegateVelopackLogger(log))
            .OnFirstRun(version => log(VelopackLogLevel.Information, $"First run of BrowserAI {version}.", null))
            .OnRestarted(version => log(VelopackLogLevel.Information, $"BrowserAI {version} restarted after an update.", null))
            .OnAfterInstallFastCallback(version => log(VelopackLogLevel.Information, $"Installed BrowserAI {version}.", null))
            .OnAfterUpdateFastCallback(version => log(VelopackLogLevel.Information, $"Updated to BrowserAI {version}.", null))
            .OnBeforeUpdateFastCallback(version => log(VelopackLogLevel.Information, $"BrowserAI {version} is being replaced.", null))
            .OnBeforeUninstallFastCallback(version => log(VelopackLogLevel.Information, $"BrowserAI {version} is being uninstalled.", null))
            .Run();
    }

    /// <summary>
    /// Bridges <see cref="IVelopackLogger"/> onto whatever the host is already
    /// writing to.
    /// </summary>
    /// <param name="write">Where a record goes.</param>
    private sealed class DelegateVelopackLogger(Action<VelopackLogLevel, string, Exception?> write) : IVelopackLogger
    {
        public void Log(VelopackLogLevel logLevel, string? message, Exception? exception) =>
            write(logLevel, message ?? string.Empty, exception);
    }
}
