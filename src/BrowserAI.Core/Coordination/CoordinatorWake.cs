// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Registration;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Coordination;

/// <summary>How one wake of the coordinator went.</summary>
internal enum WakeOutcome
{
    /// <summary>A coordinator was serving, and took a <c>recheck</c>.</summary>
    Rechecked,

    /// <summary>No coordinator was serving, and the logon task was started to make one.</summary>
    Started,

    /// <summary>Neither: the log says why, and the next blocked pass tries again.</summary>
    Failed,
}

/// <summary>What one wake did, in one sentence.</summary>
/// <param name="Outcome">How it went.</param>
/// <param name="Why">The sentence.</param>
internal sealed record WakeReport(WakeOutcome Outcome, string Why);

/// <summary>What a server does when its update is staged and blocked by other processes.</summary>
/// <remarks>
/// <b>A seam, so that the update lane's arms can see the call</b> without a
/// coordinator or a task scheduler behind them; the product's is
/// <see cref="CoordinatorWake"/>.
/// </remarks>
internal interface ICoordinatorWake
{
    /// <summary>Makes sure a coordinator looks at the install again.</summary>
    /// <returns>What happened.</returns>
    WakeReport Wake();
}

/// <summary>
/// A blocked server's wake: a <c>recheck</c> to the coordinator when one serves,
/// and otherwise the per-user logon task, started on demand with
/// <c>--coordinate</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q283 a, decided 2026-09-24 by the maintainer, in his words: <i>"Q283 a"</i>.</b>
/// When the update lane's census says a staged package may not be applied, the
/// server starts the scheduled task, so the coordinator is the task scheduler's
/// child and not this server's, and outlives the client: a client kills its
/// server's process tree when it exits
/// ([kb](../../../kb/mcp/protocol.md#what-a-client-does-when-the-server-exits-and-what-the-pipe-decides----measured-2026-09-24)).
/// When a coordinator already serves the install's pipe, the server sends it
/// <c>recheck</c> and starts nothing.
/// </para>
/// <para>
/// <b>A pipe that answers nothing is not a reason to start another
/// coordinator</b>: a start of the task would meet the same held name and hand over
/// to the same silent holder. It is reported, and the next blocked pass asks again.
/// </para>
/// </remarks>
/// <param name="installRoot">The install root the census and the pipe are keyed to.</param>
/// <param name="appId">The pack id the logon task is named for, or <see langword="null"/> when this process is not installed.</param>
/// <param name="tasks">The task scheduler.</param>
/// <param name="logger">Where the wake is recorded.</param>
internal sealed class CoordinatorWake(string installRoot, string? appId, ILogonTasks tasks, ILogger logger) : ICoordinatorWake
{
    /// <inheritdoc />
    public WakeReport Wake()
    {
        WakeReport report;

        try
        {
            report = Ask();
        }
#pragma warning disable CA1031 // The update lane's boundary: a wake that fails is a sentence, and the staged package waits for the next pass.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            report = new WakeReport(WakeOutcome.Failed, $"the wake itself failed: {failure.Message}");
        }

        var outcome = report.Outcome.ToString();

        CoordinatorLog.Woke(logger, outcome, report.Why);

        return report;
    }

    /// <summary>The recheck, or the task, or why neither.</summary>
    /// <returns>What happened.</returns>
    private WakeReport Ask()
    {
        WakeReport report;

        var handed = CoordinatorClient.SendAsync(installRoot, CoordinatorVerb.Recheck, grant: null).GetAwaiter().GetResult();

        if (handed.Outcome is HandOverOutcome.Answered)
        {
            report = new WakeReport(WakeOutcome.Rechecked, $"a coordinator serves this install and was asked to look again. {handed.Why}");
        }
        else if (handed.Outcome is not HandOverOutcome.NoCoordinator)
        {
            report = new WakeReport(WakeOutcome.Failed, $"the coordinator's pipe is there and did not take the recheck, so no second coordinator was started: {handed.Why}");
        }
        else if (appId is not { Length: > 0 })
        {
            report = new WakeReport(WakeOutcome.Failed, "no coordinator serves this install, and this process has no pack id to name the logon task by, so none was started.");
        }
        else
        {
            var name = SignInTask.NameFor(appId, installRoot);
            var run = tasks.Run(name, CoordinatorProtocol.CoordinateArgument);

            report = run.Change is TaskChange.Started
                ? new WakeReport(WakeOutcome.Started, $"no coordinator served this install, so the logon task was started to be one. {run.Detail}")
                : new WakeReport(WakeOutcome.Failed, $"no coordinator serves this install and the logon task did not start: {run.Detail}");
        }

        return report;
    }
}
