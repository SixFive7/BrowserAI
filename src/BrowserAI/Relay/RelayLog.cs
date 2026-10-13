// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Relay;

/// <summary>Source-generated log messages for the relay.</summary>
/// <remarks>
/// <b>A record names a frame by its method and its id and by nothing else.</b> A
/// frame's <c>params</c>, <c>result</c> and <c>error</c> are the client's data or the
/// background's, and none of them is ever written here; neither is any environment
/// variable, since a Claude Code server's environment carries a messaging socket's
/// secret token (a measurement of 2026-10-08). Counts, ids, methods, the
/// relay's own reasons and the exceptions of its own calls are what remains.
/// ⚠️ <i>Added 2026-10-11 by addition, 47 b:</i> one error is quoted, the background's
/// refusal of a connection whose first message did not come in time, by its kind and its
/// sentence. Both are BrowserAI's own words, a fixed sentence with a number in it, and
/// they are the reason the close that follows it happened.
/// </remarks>
internal static partial class RelayLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Relay: connected to the background, which answered as pid {BackgroundPid}.")]
    public static partial void Connected(ILogger logger, int? backgroundPid);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Relay: the background refused this relay's greeting, for the reason it calls {Refusal}.")]
    public static partial void Refused(ILogger logger, string refusal);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Relay: the background's pipe closed with {Outstanding} call(s) outstanding.")]
    public static partial void Disconnected(ILogger logger, int outstanding);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Error,
        Message = "Relay: the background answered no liveness probe for {Seconds} s, so {Calls} call(s) were answered with the hang sentence; nothing was stopped.")]
    public static partial void Hung(ILogger logger, double seconds, int calls);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Information,
        Message = "Relay: the background answered again, so it is no longer reported hung.")]
    public static partial void HangCleared(ILogger logger);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Information,
        Message = "Relay: answered call {Id} without the background, because {Reason}.")]
    public static partial void AnsweredWithoutTheBackground(ILogger logger, RequestId id, string reason);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Information,
        Message = "Relay: told the background it is ready to end for an update; its countdown ran out at {IdleAt}.")]
    public static partial void Agreed(ILogger logger, DateTimeOffset idleAt);

    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Information,
        Message = "Relay: the client sent a message after the relay agreed to end, so the agreement was withdrawn.")]
    public static partial void Withdrawn(ILogger logger);

    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Information,
        Message = "Relay: the background called the update off; {Count} held message(s) are passed on in order.")]
    public static partial void CalledOff(ILogger logger, int count);

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Warning,
        Message = "Relay: ending for an update (now: {Now}); {Answered} call(s) answered with the update sentence.")]
    public static partial void EndingForAnUpdate(ILogger logger, bool now, int answered);

    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Information,
        Message = "Relay: the client closed its input, so the relay ends.")]
    public static partial void ClientWentAway(ILogger logger);

    [LoggerMessage(
        EventId = 12,
        Level = LogLevel.Warning,
        Message = "Relay: a {Bytes}-byte frame from the client could not be read as JSON-RPC.")]
    public static partial void UnreadableClientFrame(ILogger logger, int bytes);

    [LoggerMessage(
        EventId = 13,
        Level = LogLevel.Warning,
        Message = "Relay: a {Bytes}-byte frame from the background could not be read as JSON-RPC and was dropped.")]
    public static partial void UnreadableBackgroundFrame(ILogger logger, int bytes);

    [LoggerMessage(
        EventId = 14,
        Level = LogLevel.Error,
        Message = "Relay: asking why there is no background failed, so the task is reported as unknown.")]
    public static partial void ExplainFailed(ILogger logger, Exception? exception);

    [LoggerMessage(
        EventId = 15,
        Level = LogLevel.Warning,
        Message = "Relay: looking for the background failed, and it is looked for again.")]
    public static partial void LookFailed(ILogger logger, Exception? exception);

    [LoggerMessage(
        EventId = 16,
        Level = LogLevel.Warning,
        Message = "Relay: the background sent '{Method}', which this relay does not know.")]
    public static partial void UnknownBackgroundMethod(ILogger logger, string method);

    [LoggerMessage(
        EventId = 17,
        Level = LogLevel.Warning,
        Message = "Relay: the client's output could not be written, so the relay ends.")]
    public static partial void ClientOutputFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 18,
        Level = LogLevel.Information,
        Message = "Relay: the client cancelled call {Id}, which nothing had answered, so it is dropped with no answer.")]
    public static partial void HeldCallCancelled(ILogger logger, RequestId id);

    [LoggerMessage(
        EventId = 19,
        Level = LogLevel.Information,
        Message = "Relay: the background answered call {Id} after the relay had answered it, so the late answer was dropped.")]
    public static partial void LateAnswerDropped(ILogger logger, RequestId id);

    [LoggerMessage(
        EventId = 20,
        Level = LogLevel.Warning,
        Message = "Relay: a request the relay answers itself got no answer: {Method}.")]
    public static partial void HandshakeUnanswered(ILogger logger, string method);

    [LoggerMessage(
        EventId = 21,
        Level = LogLevel.Information,
        Message = "Relay: ended ({Reason}); the background's pipe is closed.")]
    public static partial void Ended(ILogger logger, RelayEnding reason);

    /// <summary>The classifier failed, logged by its exception's type and code and never its message.</summary>
    /// <remarks>
    /// The classifier reads the client's command line and environment, so a message it
    /// put in an exception could carry either; the type and the code say what failed
    /// without repeating what it read.
    /// </remarks>
    /// <param name="logger">Where to log.</param>
    /// <param name="exceptionType">The exception's type name.</param>
    /// <param name="hresult">The exception's code.</param>
    [LoggerMessage(
        EventId = 22,
        Level = LogLevel.Warning,
        Message = "Relay: telling what this client needs after an update failed ({ExceptionType}, {HResult}), so its greeting says Unknown.")]
    public static partial void ClassifyFailed(ILogger logger, string exceptionType, int hresult);

    /// <summary>The background refused this connection because its first message did not come in time (47 b, 2026-10-11).</summary>
    /// <remarks>
    /// <b>26, and not the next number of this class</b>: 23 and 24 are <c>FinderLog</c>'s and 25 is
    /// <c>RelayModeLog</c>'s, which write under the same category, and <c>ProcessLogTests</c> holds every line of the
    /// relay's category to an id of its own. The gate of 2026-10-11 found the 23 this line first took.
    /// </remarks>
    /// <param name="logger">Where to log.</param>
    /// <param name="refusal">The refusal's kind, <see cref="RelayProtocol.RefusedForNoFirstMessage"/>.</param>
    /// <param name="sentence">The background's sentence.</param>
    [LoggerMessage(
        EventId = 26,
        Level = LogLevel.Warning,
        Message = "Relay: the background refused this connection, for the reason it calls {Refusal}: {Sentence}")]
    public static partial void ClosedForNoFirstMessage(ILogger logger, string refusal, string sentence);

    /// <summary>
    /// The record of the tool list this relay's client was answered could not be written
    /// (127 c, 2026-10-12), logged by the exception's type and code, as
    /// <see cref="ClassifyFailed"/> is.
    /// </summary>
    /// <param name="logger">Where to log.</param>
    /// <param name="exceptionType">The exception's type name.</param>
    /// <param name="hresult">The exception's code.</param>
    [LoggerMessage(
        EventId = 28,
        Level = LogLevel.Warning,
        Message = "Relay: the record of the tool list this client was answered could not be written ({ExceptionType}, {HResult}); the relay it starts next reads the record as it was before.")]
    public static partial void ToolListNotRecorded(ILogger logger, string exceptionType, int hresult);
}
