// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Protocol;
using BrowserAI.Sessions;
using BrowserAI.Updates;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Relay;

/// <summary>The client's half: what arrives on its input, and everything written to its output.</summary>
internal sealed partial class RelayEngine
{
    private RelayFrame? _initialize;
    private string? _clientName;
    private string? _clientVersion;
    private RelayReconnect _reconnect = RelayReconnect.Unknown;
    private byte[]? _toolsResult;
    private bool _clientGone;

    /// <summary>
    /// Whether this connection has asked for the tool list since its last
    /// <c>initialize</c>: set on arrival, cleared by a handshake.
    /// </summary>
    private bool _listed;

    /// <summary>Whether the one stale-list refusal of this connection has been made.</summary>
    private bool _staleListRefused;

    /// <summary>The tool list, asked of the provider once.</summary>
    private JsonObject Tools => field ??= _toolList();

    /// <summary>What every tool in the list takes, for the refusals that name every tool.</summary>
    private ToolSignatures Signatures => field ??= ToolSignatures.From(Tools);

    private static bool IsPing(RelayFrame frame) =>
        frame.Kind is FrameKind.Request && frame.Method is RequestMethods.Ping;

    private static JsonNode? Member(JsonNode? node, string name) =>
        node is JsonObject owner && owner.TryGetPropertyValue(name, out var value) ? value : null;

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool Flag(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    private static int? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    /// <summary>Handles one client frame.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="arrivedAt">
    /// <see langword="null"/> for a frame that has just arrived, which counts as
    /// activity unless it is a <c>ping</c>; otherwise the moment a frame held
    /// through an update agreement first arrived, which is handled now and counted
    /// then.
    /// </param>
    /// <returns>A task that completes once the frame is handled.</returns>
    private async Task OnClientFrameAsync(RelayFrame frame, DateTimeOffset? arrivedAt)
    {
        var at = arrivedAt ?? Now;

        // ⚠️ U1, decided 2026-10-08: every message the client sends counts as
        // activity except `ping`. A client that pings an idle server to keep it
        // alive would otherwise hold every update for ever.
        if (arrivedAt is null && !IsPing(frame))
        {
            Activity();
        }

        if (_ending)
        {
            await AnswerAfterTheEndAsync(frame).ConfigureAwait(false);
            return;
        }

        switch (frame.Kind)
        {
            case FrameKind.Request:
                await OnClientRequestAsync(frame, at).ConfigureAwait(false);
                break;

            case FrameKind.Notification:
                await OnClientNotificationAsync(frame, at).ConfigureAwait(false);
                break;

            case FrameKind.Response:
                // The client answering something the background asked it.
                PassOnOrDrop(frame, at);
                break;

            default:
                await AnswerUnreadableAsync(frame).ConfigureAwait(false);
                break;
        }
    }

    private async Task OnClientRequestAsync(RelayFrame frame, DateTimeOffset at)
    {
        if (frame.Id is not { } id)
        {
            return;
        }

        switch (frame.Method)
        {
            case RequestMethods.Initialize:
                await OnInitializeAsync(frame).ConfigureAwait(false);
                break;

            case RequestMethods.ToolsList:
                // Set on ARRIVAL, as the old server set it: the question is whether
                // the client asked, not whether an answer went out.
                _listed = true;
                await AnswerToolListAsync(id).ConfigureAwait(false);
                break;

            case RequestMethods.ToolsCall:
                await OnToolsCallAsync(frame, id, at).ConfigureAwait(false);
                break;

            default:
                await AnswerLocallyAsync(frame).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// Answers <c>initialize</c> from the binary, keeps it for every background this
    /// relay will greet, and starts looking for one.
    /// </summary>
    /// <param name="frame">The client's <c>initialize</c>.</param>
    /// <returns>A task that completes once it is answered.</returns>
    private async Task OnInitializeAsync(RelayFrame frame)
    {
        _initialize = frame;

        var client = Member(RelayFrame.Node(frame.Params), "clientInfo");
        _clientName = Text(Member(client, "name"));
        _clientVersion = Text(Member(client, "version"));

        // "Since the handshake" is what the stale-list refusal counts from, so a
        // second handshake on the same connection starts the question again.
        _listed = false;
        _staleListRefused = false;

        await AnswerLocallyAsync(frame).ConfigureAwait(false);

        // After the answer and never in front of it (D6 a): the client's first turn
        // waits on the handshake and on nothing behind it. The look follows the
        // classification at once, because the greeting carries what it says.
        Classify(_clientName);
    }

    /// <summary>Asks the classifier, on the thread pool, what this client needs once an update has ended its relay.</summary>
    /// <param name="clientName">What the client put in <c>clientInfo.name</c>, if anything.</param>
    private void Classify(string? clientName)
    {
        Enter();

        var reading = Task.Run(() => _reconnectOf(clientName), CancellationToken.None);

        _ = reading.ContinueWith(
            static (done, state) =>
            {
                var engine = (RelayEngine)state!;
                engine.Post(new Classified(done));
                engine.Leave();
            },
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Keeps what the classifier said, for the next greeting, and makes the first look.</summary>
    /// <param name="reading">The classifier's completed call.</param>
    /// <returns>A task that completes once it is kept.</returns>
    private async Task OnClassifiedAsync(Task<RelayReconnect> reading)
    {
        if (reading.IsCompletedSuccessfully)
        {
            _reconnect = await reading.ConfigureAwait(false);
        }
        else
        {
            RelayLog.ClassifyFailed(_logger, reading.Exception);
            _reconnect = RelayReconnect.Unknown;
        }

        if (_phase is LinkPhase.NotLooking)
        {
            Look();
        }
    }

    /// <summary>The name the greeting carries for what a client needs after an update.</summary>
    /// <param name="reconnect">The classification.</param>
    /// <returns>The name of the <see cref="RelayReconnect"/> member.</returns>
    private static string NameOf(RelayReconnect reconnect) => reconnect switch
    {
        RelayReconnect.None => nameof(RelayReconnect.None),
        RelayReconnect.McpReconnect => nameof(RelayReconnect.McpReconnect),
        RelayReconnect.NewConversation => nameof(RelayReconnect.NewConversation),
        _ => nameof(RelayReconnect.Unknown),
    };

    /// <summary>Answers <c>tools/list</c> from the list built into the binary.</summary>
    /// <param name="id">The request.</param>
    /// <returns>A task that completes once it is answered.</returns>
    private async Task AnswerToolListAsync(RequestId id)
    {
        _toolsResult ??= Encoded(Tools);
        await WriteToClientAsync(RelayWire.VerbatimResult(id, _toolsResult)).ConfigureAwait(false);
    }

    private static byte[] Encoded(JsonNode node)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = JsonLines.CreateWriter(buffer))
        {
            node.WriteTo(writer);
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// One tool call: refused once if this connection never listed, held for an
    /// update agreement, answered in place for a hung background, passed on, or
    /// held for a background that is not there.
    /// </summary>
    /// <param name="frame">The call, as the client wrote it.</param>
    /// <param name="id">Its id.</param>
    /// <param name="at">When it arrived.</param>
    /// <returns>A task that completes once it is handled.</returns>
    private async Task OnToolsCallAsync(RelayFrame frame, RequestId id, DateTimeOffset at)
    {
        // Read leniently: a name that is not a string is the background's to refuse
        // by name, and here it only labels a sentence.
        var tool = RelayFrame.StringMember(frame.Params, "name"u8) ?? "<none>";

        if (await RefuseAStaleListAsync(id, tool).ConfigureAwait(false))
        {
            return;
        }

        if (_agreed)
        {
            _deferred.Add(new Deferred(frame, at));
            return;
        }

        if (_hung)
        {
            await AnswerInPlaceAsync(id, RelayErrors.Hung(tool, wasPassedOn: false, _facts.LogPath), "the background is not answering").ConfigureAwait(false);
            return;
        }

        switch (_phase)
        {
            case LinkPhase.Ready:
                PassOn(frame, id, tool);
                break;

            case LinkPhase.Greeting or LinkPhase.Replaying:
                Hold(frame, id, tool, at);
                break;

            default:
                Hold(frame, id, tool, at);
                ExplainUnlessAsked();
                break;
        }
    }

    /// <summary>
    /// Refuses the first call of a connection that has not asked for the tool list
    /// since its handshake, once: Q261 b, moved from the server to the relay.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The relay is what knows whether its client has listed</b>, so the refusal
    /// moved here with the one-binary build ("What moves" in the plan). Measured
    /// 2026-09-24 at Claude Code 2.1.281, 3 of 3: a client that starts a dead stdio
    /// server again sends <c>initialize</c> and <c>tools/call</c> and no
    /// <c>tools/list</c>, so it calls from a list another BrowserAI gave it.
    /// </para>
    /// <para>
    /// The list-changed notification goes first, so a client that acts on it has it
    /// before the refusal; the refusal is what recovers the turn, and it is never a
    /// wall: the next call goes through.
    /// </para>
    /// </remarks>
    /// <param name="id">The call.</param>
    /// <param name="tool">The tool it named.</param>
    /// <returns><see langword="true"/> when the call was refused and must not go further.</returns>
    private async Task<bool> RefuseAStaleListAsync(RequestId id, string tool)
    {
        if (_listed || _staleListRefused)
        {
            return false;
        }

        _staleListRefused = true;

        await WriteToClientAsync(RelayWire.Notification(NotificationMethods.ToolListChangedNotification, null)).ConfigureAwait(false);
        await AnswerInPlaceAsync(
            id,
            SessionErrors.ToolListPredatesThisServer(tool, _facts.Build, _clientName, throughTheSessionHost: false, Signatures),
            "the connection never asked for the tool list").ConfigureAwait(false);

        return true;
    }

    private async Task OnClientNotificationAsync(RelayFrame frame, DateTimeOffset at)
    {
        if (frame.Method is NotificationMethods.InitializedNotification)
        {
            // Answered locally, which for a notification is swallowed: the SDK's
            // server is told, and the background gets the relay's own when it is
            // greeted.
            await AnswerLocallyAsync(frame).ConfigureAwait(false);
            return;
        }

        if (frame.Method is NotificationMethods.CancelledNotification && DropAnUnansweredCall(frame))
        {
            return;
        }

        PassOnOrDrop(frame, at);
    }

    /// <summary>
    /// Drops a cancelled call nothing has answered yet, with no answer, as MCP has it;
    /// or forgets a passed-on one, whose cancellation then goes on to the background.
    /// </summary>
    /// <param name="frame">The client's <c>notifications/cancelled</c>.</param>
    /// <returns><see langword="true"/> when the cancellation was used up here.</returns>
    private bool DropAnUnansweredCall(RelayFrame frame)
    {
        if (RelayFrame.IdMember(frame.Params, "requestId"u8) is not { } id)
        {
            return false;
        }

        var held = _held.FindIndex(call => call.Id == id);

        if (held >= 0)
        {
            _held.RemoveAt(held);
            ArmHoldTimer();
            RelayLog.HeldCallCancelled(_logger, id);
            return true;
        }

        var deferred = _deferred.FindIndex(waiting => waiting.Frame.Kind is FrameKind.Request && waiting.Frame.Id == id);

        if (deferred >= 0)
        {
            _deferred.RemoveAt(deferred);
            RelayLog.HeldCallCancelled(_logger, id);
            return true;
        }

        if (_awaitingExplanation.Remove(id))
        {
            RelayLog.HeldCallCancelled(_logger, id);
            return true;
        }

        if (_forwarded.Remove(id))
        {
            UpdateProbing();
        }

        return false;
    }

    /// <summary>
    /// Passes a client notification or answer on when the relay is connected, holds it
    /// while the relay has agreed to end, and drops it otherwise.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <param name="at">When it arrived.</param>
    private void PassOnOrDrop(RelayFrame frame, DateTimeOffset at)
    {
        if (_agreed)
        {
            _deferred.Add(new Deferred(frame, at));
            return;
        }

        if (_phase is LinkPhase.Ready)
        {
            _link?.Send(frame.Bytes);
        }
    }

    /// <summary>Answers a frame through the handshake: the SDK's own server, in memory.</summary>
    /// <param name="frame">The frame.</param>
    /// <returns>A task that completes once the answer, if any, is written.</returns>
    private async Task AnswerLocallyAsync(RelayFrame frame)
    {
        byte[]? answer;

        try
        {
            answer = await _handshake.AnswerAsync(frame.Bytes, _stopping).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is JsonException or ArgumentException)
        {
            // The relay could read the frame and the SDK could not: the client is
            // answered as it would be for a frame nobody can read.
            await AnswerUnreadableAsync(frame).ConfigureAwait(false);
            return;
        }

        if (answer is not null)
        {
            await WriteToClientAsync(answer).ConfigureAwait(false);
        }
        else if (frame.Kind is FrameKind.Request)
        {
            RelayLog.HandshakeUnanswered(_logger, frame.Method ?? "<none>");
        }
    }

    /// <summary>Answers a frame that is not a JSON-RPC message, when it carries an id to answer.</summary>
    /// <param name="frame">The frame.</param>
    /// <returns>A task that completes once the answer, if any, is written.</returns>
    private async Task AnswerUnreadableAsync(RelayFrame frame)
    {
        RelayLog.UnreadableClientFrame(_logger, frame.Bytes.Length);

        var id = frame.Id
            ?? (JsonLines.TryRecoverRequestId(new ReadOnlySequence<byte>(frame.Bytes), out var recovered) ? recovered : null);

        if (id is { } answerable)
        {
            await WriteToClientAsync(RelayWire.Error(answerable, (int)McpErrorCode.ParseError, RelayErrors.UnreadableFrame())).ConfigureAwait(false);
        }
    }

    /// <summary>Answers a call in the background's place, with a sentence.</summary>
    /// <param name="id">The call.</param>
    /// <param name="sentence">The sentence.</param>
    /// <param name="reason">Why, for the log, which names the call by its id alone.</param>
    /// <returns>A task that completes once the answer is written.</returns>
    private async Task AnswerInPlaceAsync(RequestId id, string sentence, string reason)
    {
        RelayLog.AnsweredWithoutTheBackground(_logger, id, reason);
        await WriteToClientAsync(RelayWire.ToolError(id, sentence)).ConfigureAwait(false);
    }

    /// <summary>Writes one frame to the client.</summary>
    /// <remarks>
    /// <b>A client whose output cannot be written has gone</b>, so the relay ends
    /// instead of answering into a broken pipe.
    /// </remarks>
    /// <param name="frame">The frame, without its newline.</param>
    /// <returns>A task that completes once the frame is written, or the client is known gone.</returns>
    private async Task WriteToClientAsync(ReadOnlyMemory<byte> frame)
    {
        if (_clientGone)
        {
            return;
        }

        try
        {
            await RelayLines.WriteAsync(_clientOutput, frame, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is IOException or ObjectDisposedException or NotSupportedException)
        {
            _clientGone = true;
            RelayLog.ClientOutputFailed(_logger, failure);
            _end ??= new RelayEnd(RelayEnding.ClientWentAway, null);
        }
    }
}
