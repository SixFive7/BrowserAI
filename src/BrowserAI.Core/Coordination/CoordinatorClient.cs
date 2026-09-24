// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Coordination;

/// <summary>Grants another process the right to bring its window to the foreground.</summary>
/// <remarks>
/// <b>A seam, because the real call is the one thing here a test may not make.</b>
/// A process the task scheduler started holds no foreground right, measured three
/// times on 2026-09-24, so a second start that holds one grants it to the
/// coordinator before it asks for the window
/// (<c>AllowSetForegroundWindow</c>, in the configuration app). The suite records
/// the grant it would have made.
/// </remarks>
internal interface IForegroundGrant
{
    /// <summary>Grants the right to one process.</summary>
    /// <param name="processId">The coordinator's pid.</param>
    /// <returns>Whether Windows accepted the grant.</returns>
    bool Allow(int processId);
}

/// <summary>How one hand-over to the coordinator ended.</summary>
internal enum HandOverOutcome
{
    /// <summary>The coordinator acknowledged the verb.</summary>
    Answered,

    /// <summary>No pipe of the name exists: nobody is the coordinator.</summary>
    NoCoordinator,

    /// <summary>The coordinator answered with a refusal.</summary>
    Refused,

    /// <summary>The pipe was there and no whole answer came back inside the bound.</summary>
    NoAnswer,
}

/// <summary>What one hand-over came back with.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Why">One sentence, for every outcome.</param>
/// <param name="CoordinatorProcessId">The coordinator's pid, when it was learned.</param>
/// <param name="Elapsed">How long it took, connect included.</param>
internal sealed record HandOver(HandOverOutcome Outcome, string Why, int? CoordinatorProcessId, TimeSpan Elapsed);

/// <summary>
/// Asks the coordinator of an install root to show its window or to look again.
/// </summary>
/// <remarks>
/// <b>The pid comes from the pipe before a byte is sent</b>
/// (<c>GetNamedPipeServerProcessId</c>), and a show grants that pid the foreground
/// right there, so the grant is in place before the coordinator acts on the verb.
/// Nothing else is asked of the pipe's owner: its DACL admits the current user and
/// nobody else, and a process of the same user could impersonate anything a check
/// here could ask for.
/// </remarks>
internal static class CoordinatorClient
{
    /// <summary>Hands one verb to the coordinator.</summary>
    /// <param name="installRoot">The install root, or the data root of a process that is not installed.</param>
    /// <param name="verb">What to ask.</param>
    /// <param name="grant">The foreground grant for a show, or <see langword="null"/> for none.</param>
    /// <param name="bound">The whole call's bound, <see cref="ServerPipeProtocol.CallBound"/> by default.</param>
    /// <param name="cancellationToken">Ends the call early.</param>
    /// <returns>What came back.</returns>
    public static async Task<HandOver> SendAsync(
        string installRoot,
        CoordinatorVerb verb,
        IForegroundGrant? grant,
        TimeSpan? bound = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);

        var clock = Stopwatch.StartNew();
        var name = CoordinatorProtocol.NameFor(installRoot);
        var spelling = CoordinatorProtocol.Spelling(verb);

        var exchange = await ServerPipeClient.ExchangeAsync(
            name,
            CoordinatorProtocol.Request(verb),
            spelling,
            clock,
            bound ?? ServerPipeProtocol.CallBound,
            serving =>
            {
                if (verb is CoordinatorVerb.Show && grant is not null && serving is { } coordinator)
                {
                    _ = grant.Allow(coordinator);
                }

                return null;
            },
            cancellationToken).ConfigureAwait(false);

        if (exchange.Body is not { } body)
        {
            return new HandOver(
                exchange.Failure is ServerPipeOutcome.NoPipe ? HandOverOutcome.NoCoordinator : HandOverOutcome.NoAnswer,
                exchange.Why ?? $"'{name}' gave no answer.",
                exchange.ServerProcessId,
                clock.Elapsed);
        }

        return CoordinatorProtocol.TryReadAcknowledgement(body, verb, out var acknowledgedBy, out var why)
            ? new HandOver(
                HandOverOutcome.Answered,
                string.Create(CultureInfo.InvariantCulture, $"The coordinator, pid {acknowledgedBy}, took '{spelling}'."),
                acknowledgedBy,
                clock.Elapsed)
            : new HandOver(HandOverOutcome.Refused, why, exchange.ServerProcessId, clock.Elapsed);
    }
}

/// <summary>How a start of the app settled who the coordinator is.</summary>
internal enum CoordinatorStartOutcome
{
    /// <summary>This process holds the pipe and is the coordinator.</summary>
    Coordinator,

    /// <summary>Another process is the coordinator and took this start's verb.</summary>
    HandedOver,

    /// <summary>Neither: the pipe could not be created and nobody answered on it.</summary>
    Neither,
}

/// <summary>What one start of the app settled.</summary>
/// <param name="Outcome">How it settled.</param>
/// <param name="Pipe">The coordinator's pipe, when this process holds it. The caller owns it.</param>
/// <param name="HandOver">The hand-over, when there was one.</param>
/// <param name="Why">One sentence, for every outcome.</param>
internal sealed record CoordinatorStart(
    CoordinatorStartOutcome Outcome,
    CoordinatorPipe? Pipe,
    HandOver? HandOver,
    string Why)
{
    /// <summary>How many times a start tries both halves before it settles on neither.</summary>
    /// <remarks>
    /// <b>Three, because one race has a second round.</b> A coordinator can exit
    /// between this start's failed create and its connect: the connect then finds no
    /// pipe, and creating it again is the right answer. A third round covers that
    /// race happening twice in a row; past it, something other than a race is
    /// holding the name.
    /// </remarks>
    public const int Attempts = 3;

    /// <summary>
    /// Becomes the coordinator of an install root, or hands one verb to the
    /// process that already is.
    /// </summary>
    /// <param name="installRoot">The install root, or the data root of a process that is not installed.</param>
    /// <param name="inbox">Where the verbs go if this process becomes the coordinator.</param>
    /// <param name="verb">What to hand over if it does not.</param>
    /// <param name="grant">The foreground grant for a show, or <see langword="null"/>.</param>
    /// <param name="logger">Where the outcome is recorded.</param>
    /// <param name="bound">
    /// Each hand-over's bound. <see cref="ServerPipeProtocol.CallBound"/> in the
    /// product, which passes none; the suite's arms pass their own hang detector,
    /// since their host is under a load no second start is, the way
    /// <see cref="ServerPipeClient.DescribeAsync"/>'s callers do.
    /// </param>
    /// <returns>What was settled. <see cref="Pipe"/> is the caller's to dispose.</returns>
    public static CoordinatorStart Settle(
        string installRoot,
        CoordinatorInbox inbox,
        CoordinatorVerb verb,
        IForegroundGrant? grant,
        ILogger logger,
        TimeSpan? bound = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(logger);

        var reasons = new List<string>();
        var spelling = CoordinatorProtocol.Spelling(verb);

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            if (TryHold(installRoot, inbox, logger, reasons) is { } held)
            {
                return held;
            }

            // Synchronous on purpose: this runs on a start's own thread before
            // anything else it does, and the bound is the call's.
            var handed = CoordinatorClient.SendAsync(installRoot, verb, grant, bound).GetAwaiter().GetResult();

            if (handed.Outcome is HandOverOutcome.Answered)
            {
                var coordinator = handed.CoordinatorProcessId ?? 0;
                var milliseconds = handed.Elapsed.TotalMilliseconds;

                CoordinatorLog.HandedOver(logger, spelling, coordinator, milliseconds);

                return new CoordinatorStart(CoordinatorStartOutcome.HandedOver, null, handed, handed.Why);
            }

            reasons.Add(handed.Why);

            if (handed.Outcome is not HandOverOutcome.NoCoordinator)
            {
                // Somebody holds the name and did not take the verb. Trying to
                // create it again would meet the same holder.
                break;
            }
        }

        var why = string.Join(" Then: ", reasons);

        CoordinatorLog.Neither(logger, why);

        return new CoordinatorStart(CoordinatorStartOutcome.Neither, null, null, why);
    }

    /// <summary>Creates the pipe, and so becomes the coordinator, or records why not.</summary>
    /// <param name="installRoot">The root the pipe is named for.</param>
    /// <param name="inbox">Where its verbs go.</param>
    /// <param name="logger">Where the pipe reports.</param>
    /// <param name="reasons">Where a refusal is recorded.</param>
    /// <returns>The settled start, or <see langword="null"/> when the name is held.</returns>
    private static CoordinatorStart? TryHold(string installRoot, CoordinatorInbox inbox, ILogger logger, List<string> reasons)
    {
        CoordinatorPipe? pipe = null;

        try
        {
            pipe = CoordinatorPipe.Open(installRoot, inbox, logger);

            var held = new CoordinatorStart(CoordinatorStartOutcome.Coordinator, pipe, null, $"This process holds {pipe.Name}.");

            // Handed to the caller with the record; nothing here disposes it now.
            pipe = null;

            return held;
        }
        catch (IOException taken)
        {
            reasons.Add($"'{CoordinatorProtocol.NameFor(installRoot)}' could not be created ({taken.Message})");
            return null;
        }
        finally
        {
            pipe?.Dispose();
        }
    }
}
