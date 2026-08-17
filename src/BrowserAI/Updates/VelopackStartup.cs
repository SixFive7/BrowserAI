// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Registration;
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
/// which is indistinguishable from a crash. A shipped product examined for this
/// project never calls it and runs the default; that is survivable for the
/// foreground tray app it is, and fatal for a stdio child.
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
/// <b>One <see cref="VelopackApp.SetLogger"/> call is enough at 1.2.0.</b> The
/// landmine list this product was built against required two registrations, one
/// for the runtime <c>UpdateManager</c> and one for the startup hooks; that was
/// fixed upstream and a single registration now reaches the installer, the
/// hooks, <c>UpdateManager</c> and the bridged Rust output
/// ([kb](../../../kb/packaging/velopack.md#8-ivelopacklogger-needs-two-registrations)).
/// </para>
/// <para>
/// <b>Three hooks do one job, and it is the charter's founding promise.</b>
/// <c>--veloapp-install</c> and <c>--veloapp-updated</c> register BrowserAI with
/// the MCP client and <c>--veloapp-uninstall</c> removes it, through
/// <see cref="Registration.HookRegistration"/> — the charter's
/// <i>"registered once at system or user scope, available in every repository,
/// with no per-repo files"</i>
/// ([README](../../../README.md#settled-2026-08-16)). Before
/// 2026-08-16 every hook here existed only to log, and what shipped was an
/// installed, self-updating, self-sweeping binary that no client was configured
/// to talk to.
/// </para>
/// <para>
/// <b>Corrected 2026-08-16 (previously "No hook does any work. They exist to
/// log").</b> That was true and is not any more. The reason it was true survives
/// unchanged and still binds: the logon scheduled task
/// [is dropped](../../../kb/windows/detection.md#the-logon-sweep-task), and a hook that
/// left a <i>helper running</i> under the install root would be killed by
/// <c>force_stop_package</c> immediately afterwards anyway — it runs after every
/// hook returns. Registration is not that shape: it starts one short-lived
/// process that lives outside the install root, waits for it, and returns.
/// </para>
/// <para>
/// <b>What a hook may cost.</b> These are fast-exit callbacks with real
/// timeouts — <c>--veloapp-install</c> 30 s, <c>--veloapp-updated</c> 15 s,
/// <c>--veloapp-uninstall</c> 60 s
/// ([kb](../../../kb/packaging/velopack.md#nativeaot-hooks-and-vpk-output)) — and
/// anything slow or interactive in one is a broken install. The registration
/// call is measured at 613–645 ms with a 10 s budget of its own, and it can
/// neither prompt nor block: see <see cref="Registration.McpClientRegistration"/>.
/// </para>
/// <para>
/// <b><c>OnBeforeUpdate</c> deliberately registers nothing.</b> It runs as the
/// <i>outgoing</i> version, whose <c>current\</c> is about to be replaced; the
/// incoming one's <c>--veloapp-updated</c> is the hook that owns the question,
/// and having both act would be two passes racing over one entry.
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
    /// <remarks>
    /// The lifecycle hooks below take no injected seam. They are served only
    /// inside a real installed process — every test host reaches
    /// <see cref="Registration.HookRegistration"/> directly, which is why that
    /// type carries the overload that takes an image path and a command seam.
    /// </remarks>
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

            // ⚠️ THE THREE THAT DO WORK. Their records are written inside the
            // callback rather than buffered, because VelopackApp.Run() exits the
            // process once it has served a hook and anything buffered dies with
            // it.
            .OnAfterInstallFastCallback(version => Register(RegistrationIntent.Install, Describe(version), log))
            .OnAfterUpdateFastCallback(version => Register(RegistrationIntent.Update, Describe(version), log))
            .OnBeforeUninstallFastCallback(version => Register(RegistrationIntent.Uninstall, Describe(version), log))

            .OnBeforeUpdateFastCallback(version => log(VelopackLogLevel.Information, $"BrowserAI {version} is being replaced.", null))
            .Run();
    }

    /// <summary>
    /// The version a hook was handed, as the string everything downstream
    /// records.
    /// </summary>
    /// <param name="version">What Velopack passed the callback.</param>
    /// <returns>The full semantic version, or a placeholder.</returns>
    /// <remarks>
    /// <c>ToFullString()</c> rather than <c>ToString()</c>, for the same reason
    /// <see cref="InstallLocation.InstalledVersion"/> uses it: the pre-release
    /// suffix is what makes <i>never self-update from a build that is not a
    /// release</i> readable off a version string, and the shorter rendering drops
    /// it.
    /// </remarks>
    private static string Describe(SemanticVersion? version) => version?.ToFullString() ?? "<unknown>";

    /// <summary>
    /// One hook's whole body: register or unregister, and mirror the answer into
    /// Velopack's own log as well as BrowserAI's.
    /// </summary>
    /// <param name="intent">Which hook is running.</param>
    /// <param name="version">The version Velopack passed the callback.</param>
    /// <param name="log">Velopack's logger, which reaches the installer's log file.</param>
    /// <remarks>
    /// <b>The answer goes to two places on purpose.</b>
    /// <see cref="Registration.HookRegistration"/> writes BrowserAI's process log
    /// and the registration record; this line puts the same conclusion into the
    /// installer's own log, which is the file somebody debugging a failed install
    /// opens first and the only one that exists before BrowserAI has ever run.
    /// </remarks>
    private static void Register(RegistrationIntent intent, string version, Action<VelopackLogLevel, string, Exception?> log)
    {
        var report = HookRegistration.Run(intent, version);

        log(
            report.IsWhatWasAskedFor ? VelopackLogLevel.Information : VelopackLogLevel.Warning,
            $"BrowserAI {version} — MCP registration ({intent}): {report.Status}. {report.Detail}",
            null);
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
