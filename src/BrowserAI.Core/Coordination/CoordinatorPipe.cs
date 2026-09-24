// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using Microsoft.Extensions.Logging;

namespace BrowserAI.Coordination;

/// <summary>
/// The coordinator's own pipe: holding it is being the coordinator, and every
/// verb it takes goes into an inbox.
/// </summary>
/// <remarks>
/// <para>
/// <b>It acknowledges first and posts second.</b> The answer carries this
/// process's pid, and the verb reaches the inbox only once the client has read
/// the whole answer and closed its end, the order a server pipe's <c>stop</c> uses.
/// A second start is gone by the time the coordinator acts, which is what it wants:
/// it has handed over and exited.
/// </para>
/// <para>
/// <b>Everything about the pipe itself is a server pipe's</b>: raw
/// <c>CreateNamedPipeW</c> with <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c>,
/// <c>PIPE_REJECT_REMOTE_CLIENTS</c>, one instance and a DACL whose one entry is
/// the current user, through <see cref="ServerPipe.OpenNamed(string, IPipeAnswers, ILogger)"/>.
/// </para>
/// </remarks>
internal sealed class CoordinatorPipe : IDisposable
{
    private readonly ServerPipe _pipe;

    private CoordinatorPipe(ServerPipe pipe) => _pipe = pipe;

    /// <summary>The pipe's full name.</summary>
    public string Name => _pipe.Name;

    /// <summary>Creates the coordinator's pipe for an install root and starts serving it.</summary>
    /// <param name="installRoot">The install root, or the data root of a process that is not installed.</param>
    /// <param name="inbox">Where each verb goes once acknowledged.</param>
    /// <param name="logger">Where the pipe reports.</param>
    /// <returns>The pipe. Holding it is being the coordinator.</returns>
    /// <exception cref="IOException">
    /// The pipe was not created: <c>0x800700E7</c> when a coordinator already holds
    /// the name, and <c>0x80070005</c> when somebody else created it first.
    /// </exception>
    public static CoordinatorPipe Open(string installRoot, CoordinatorInbox inbox, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(logger);

        return new CoordinatorPipe(ServerPipe.OpenNamed(CoordinatorProtocol.NameFor(installRoot), new Answers(inbox, logger), logger));
    }

    /// <summary>Stops serving, which is letting another process become the coordinator.</summary>
    public void Dispose() => _pipe.Dispose();

    /// <summary>The coordinator's two verbs.</summary>
    /// <param name="inbox">Where each goes.</param>
    /// <param name="logger">Where each is recorded.</param>
    private sealed class Answers(CoordinatorInbox inbox, ILogger logger) : IPipeAnswers
    {
        public IReadOnlyList<string> Verbs { get; } = [CoordinatorProtocol.ShowVerb, CoordinatorProtocol.RecheckVerb];

        public ServerPipeReply? Answer(string verb, int? clientProcessId)
        {
            if (CoordinatorProtocol.Parse(verb) is not { } taken)
            {
                return null;
            }

            CoordinatorLog.Asked(logger, verb, clientProcessId ?? 0);

            return new ServerPipeReply(
                CoordinatorProtocol.Acknowledged(taken, Environment.ProcessId),
                () => inbox.Post(taken, clientProcessId));
        }
    }
}

/// <summary>Source-generated log messages for the coordinator's pipe and its starts.</summary>
internal static partial class CoordinatorLog
{
    /// <summary>A verb reached the coordinator.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="verb">What was asked.</param>
    /// <param name="from">The pid that asked, or zero when Windows would not say.</param>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "The coordinator was asked to '{Verb}' by pid {From}.")]
    public static partial void Asked(ILogger logger, string verb, int from);

    /// <summary>This process became the coordinator.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="name">The pipe it holds.</param>
    /// <param name="mode">How it was started.</param>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "This BrowserAI is the coordinator: it holds {Name}. Started as {Mode}.")]
    public static partial void Became(ILogger logger, string name, string mode);

    /// <summary>Another process holds the pipe, and this one handed over to it.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="verb">What it asked.</param>
    /// <param name="coordinator">The coordinator's pid.</param>
    /// <param name="milliseconds">How long the hand-over took.</param>
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Another BrowserAI is the coordinator (pid {Coordinator}); this start asked it to '{Verb}' in {Milliseconds:F1} ms and exits.")]
    public static partial void HandedOver(ILogger logger, string verb, int coordinator, double milliseconds);

    /// <summary>This start could neither become the coordinator nor hand over.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="why">What stopped each.</param>
    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "This BrowserAI could not become the coordinator and could not hand over to one: {Why}")]
    public static partial void Neither(ILogger logger, string why);

    /// <summary>What the sign-in step found and did.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="outcome">How it ended.</param>
    /// <param name="why">The sentence.</param>
    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Information,
        Message = "Sign-in step: {Outcome}. {Why}")]
    public static partial void SignedIn(ILogger logger, string outcome, string why);

    /// <summary>The coordinator is done and lets its pipe go.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="why">Why it stops.</param>
    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Information,
        Message = "The coordinator stops: {Why}")]
    public static partial void Stopping(ILogger logger, string why);
}
