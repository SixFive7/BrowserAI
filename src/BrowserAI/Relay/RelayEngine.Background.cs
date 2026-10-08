// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using System.Threading.Channels;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Relay;

/// <summary>The background's half: finding it, greeting it, passing calls to it, and noticing when it stops answering.</summary>
internal sealed partial class RelayEngine
{
    private const string MethodPrefix = "browserai/";
    private const string HelloMethod = "browserai/hello";
    private const string ReadyToEndMethod = "browserai/ready-to-end";
    private const string CalledOffMethod = "browserai/called-off";
    private const string EndMethod = "browserai/end";
    private const string ActivityMethod = "browserai/activity";
    private const string WithdrawMethod = "browserai/withdraw";

    private readonly Dictionary<RequestId, Forwarded> _forwarded = [];
    private readonly HashSet<RequestId> _answeredByRelay = [];
    private readonly HashSet<RequestId> _probes = [];

    private LinkPhase _phase = LinkPhase.NotLooking;
    private BackgroundLink? _link;
    private int _epoch;
    private int? _backgroundPid;
    private RequestId? _helloId;
    private RequestId? _replayId;
    private DateTimeOffset _lookAt;

    private bool _probing;
    private bool _hung;
    private long _probeCount;
    private DateTimeOffset _lastLife;
    private DateTimeOffset _nextProbeAt;

    /// <summary>Where the relay stands with its background.</summary>
    private enum LinkPhase
    {
        /// <summary>No <c>initialize</c> yet, so no greeting to replay: the relay does not look.</summary>
        NotLooking,

        /// <summary>No background; the next look is armed.</summary>
        Looking,

        /// <summary>The finder is trying the pipe.</summary>
        Connecting,

        /// <summary>The pipe is open and the greeting is sent and not yet answered.</summary>
        Greeting,

        /// <summary>The greeting was accepted and the client's <c>initialize</c> is replayed and not yet answered.</summary>
        Replaying,

        /// <summary>Calls are passed on.</summary>
        Ready,
    }

    /// <summary>Whether the background is to be asked whether it is alive.</summary>
    /// <remarks>
    /// While a call it was given is outstanding, while it has not finished its
    /// greeting, and while it is reported hung, so that the first answer clears that
    /// (D10, R).
    /// </remarks>
    private bool WantsProbes =>
        _link is not null && (_hung || _forwarded.Count > 0 || _phase is LinkPhase.Greeting or LinkPhase.Replaying);

    /// <summary>Asks the finder for the pipe, on the thread pool.</summary>
    private void Look()
    {
        _phase = LinkPhase.Connecting;
        Enter();

        var stopping = _stopping;
        var attempt = Task.Run(() => _finder.TryConnectAsync(stopping), CancellationToken.None);

        _ = attempt.ContinueWith(
            static (done, state) =>
            {
                var engine = (RelayEngine)state!;
                engine.Post(new LookFinished(done));
                engine.Leave();
            },
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Arms the next look at the pace item 6 sets: faster while a call is held.</summary>
    private void ScheduleLook()
    {
        _phase = LinkPhase.Looking;
        _lookAt = Now + (_held.Count > 0 ? RelayConstants.LookWhileHolding : RelayConstants.LookWhileIdle);
        Arm(RelayTimer.Look, _lookAt);
    }

    /// <summary>Brings the next look forward when a call has just become held.</summary>
    private void LookSooner()
    {
        if (_phase is LinkPhase.Looking && _lookAt > Now + RelayConstants.LookWhileHolding)
        {
            _lookAt = Now + RelayConstants.LookWhileHolding;
            Arm(RelayTimer.Look, _lookAt);
        }
    }

    private Task OnLookTimer()
    {
        if (_phase is LinkPhase.Looking && Now >= _lookAt)
        {
            Look();
        }

        return Task.CompletedTask;
    }

    private async Task OnLookFinishedAsync(Task<Stream?> attempt)
    {
        Stream? pipe = null;

        if (attempt.IsCompletedSuccessfully)
        {
            pipe = await attempt.ConfigureAwait(false);
        }
        else if (attempt.IsFaulted)
        {
            RelayLog.LookFailed(_logger, attempt.Exception);
        }

        if (_phase is not LinkPhase.Connecting || _end is not null)
        {
            if (pipe is not null)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
            }

            return;
        }

        if (pipe is null)
        {
            ScheduleLook();
            return;
        }

        Greet(pipe);
    }

    /// <summary>
    /// Opens a connection over a pipe the finder found and sends the relay's greeting.
    /// </summary>
    /// <remarks>
    /// <b>The greeting carries every fact about the client</b>, because the background
    /// never sees the client's environment: the build, the relay's and the client's
    /// process ids, the client's name and version, what the client needs once an
    /// update has ended its relay, its folder, the data root and the moment the
    /// relay's activity countdown runs out. Nothing in it is read from this process's
    /// environment.
    /// </remarks>
    /// <param name="pipe">The connected pipe.</param>
    private void Greet(Stream pipe)
    {
        _epoch++;
        _explaining = false;
        _link = new BackgroundLink(this, _epoch, pipe);
        _phase = LinkPhase.Greeting;

        const string Hello = RelayWire.IdPrefix + "hello";
        _helloId = new RequestId(Hello);

        _link.Send(RelayWire.Request(
            Hello,
            HelloMethod,
            new JsonObject
            {
                ["build"] = _facts.Build,
                ["relayPid"] = _facts.RelayPid,
                ["clientPid"] = _facts.ClientPid,
                ["client"] = new JsonObject { ["name"] = _clientName, ["version"] = _clientVersion },
                ["reconnect"] = NameOf(_reconnect),
                ["folder"] = _facts.Folder,
                ["dataRoot"] = _facts.DataRoot,
                ["idleAt"] = RelayWire.Instant(_idleAt),
            }));

        // The greeting tells the background the countdown, so it is the report the
        // next activity report is spaced from.
        _reportedIdleAt = _idleAt;
        _lastReportAt = Now;

        UpdateProbing();
        ArmHoldTimer();
    }

    private async Task OnBackgroundFrameAsync(RelayFrame frame)
    {
        switch (frame.Kind)
        {
            case FrameKind.Response:
                await OnBackgroundResponseAsync(frame).ConfigureAwait(false);
                break;

            case FrameKind.Request when frame.Method!.StartsWith(MethodPrefix, StringComparison.Ordinal):
                OnBackgroundOwnRequest(frame);
                break;

            case FrameKind.Notification when frame.Method!.StartsWith(MethodPrefix, StringComparison.Ordinal):
                await OnBackgroundOwnNotificationAsync(frame).ConfigureAwait(false);
                break;

            case FrameKind.Request or FrameKind.Notification:
                // Progress, a request the background makes of the client, anything
                // of the protocol's own: the client's, byte for byte.
                await WriteToClientAsync(frame.Bytes).ConfigureAwait(false);
                break;

            default:
                RelayLog.UnreadableBackgroundFrame(_logger, frame.Bytes.Length);
                break;
        }
    }

    private async Task OnBackgroundResponseAsync(RelayFrame frame)
    {
        if (frame.Id is not { } id)
        {
            RelayLog.UnreadableBackgroundFrame(_logger, frame.Bytes.Length);
            return;
        }

        if (id == _helloId)
        {
            await OnHelloAnsweredAsync(frame).ConfigureAwait(false);
            return;
        }

        if (id == _replayId)
        {
            await OnReplayAnsweredAsync(frame).ConfigureAwait(false);
            return;
        }

        if (_probes.Remove(id))
        {
            SignOfLife();
            return;
        }

        if (RelayWire.IsRelays(id))
        {
            // An answer to something of the relay's own that nobody waits for any
            // more. Never the client's.
            return;
        }

        if (_answeredByRelay.Remove(id))
        {
            RelayLog.LateAnswerDropped(_logger, id);
            return;
        }

        if (_forwarded.Remove(id))
        {
            UpdateProbing();
        }

        await WriteToClientAsync(frame.Bytes).ConfigureAwait(false);
    }

    private async Task OnHelloAnsweredAsync(RelayFrame frame)
    {
        _helloId = null;
        SignOfLife();

        if (frame.IsError)
        {
            await RefusedAsync(RelayFrame.Node(frame.Payload)).ConfigureAwait(false);
            return;
        }

        _backgroundPid = Number(Member(RelayFrame.Node(frame.Payload), "pid"));
        RelayLog.Connected(_logger, _backgroundPid);

        // The client's own handshake, under an id of the relay's, so the background
        // meets the client exactly as the client introduced itself; its answer is
        // the relay's and never reaches the client, which has had one already.
        _phase = LinkPhase.Replaying;
        var replay = RelayWire.IdPrefix + "initialize-" + _epoch.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _replayId = new RequestId(replay);
        _link?.Send(_initialize!.WithId(replay)!);
    }

    private async Task OnReplayAnsweredAsync(RelayFrame frame)
    {
        _replayId = null;
        SignOfLife();

        if (frame.IsError)
        {
            await RefusedAsync(RelayFrame.Node(frame.Payload)).ConfigureAwait(false);
            return;
        }

        _link?.Send(RelayWire.Notification(NotificationMethods.InitializedNotification, null));
        _phase = LinkPhase.Ready;

        // In the order they arrived, which is the order the client asked in.
        var held = _held.ToList();
        _held.Clear();
        ArmHoldTimer();

        foreach (var call in held)
        {
            PassOn(call.Frame, call.Id, call.Tool);
        }

        UpdateProbing();
        ReportActivity();
    }

    /// <summary>
    /// The background refused the greeting: every held call is answered with its
    /// sentence, or with the update sentence when it is installing one, and the relay
    /// looks again.
    /// </summary>
    /// <param name="error">The refusal's <c>error</c> member.</param>
    /// <returns>A task that completes once every held call is answered.</returns>
    private async Task RefusedAsync(JsonNode? error)
    {
        var sentence = Text(Member(error, "message")) ?? "it gave no reason.";
        var refusal = Text(Member(Member(error, "data"), "refusal"));

        // The kind and never the sentence: a log record names, it does not quote.
        RelayLog.Refused(_logger, refusal is "build" or "dataRoot" or "updating" or "stopping" ? refusal : "other");

        var held = _held.ToList();
        _held.Clear();
        ArmHoldTimer();

        foreach (var call in held)
        {
            await AnswerInPlaceAsync(
                call.Id,
                refusal is "updating"
                    ? RelayErrors.UpdateInstalling(call.Tool, null, _clientName)
                    : RelayErrors.BackgroundRefused(call.Tool, sentence),
                "the background refused this relay").ConfigureAwait(false);
        }

        await DropTheLinkAsync().ConfigureAwait(false);
        ScheduleLook();
    }

    /// <summary>Passes a call to the background as the client wrote it, and remembers it until it is answered.</summary>
    /// <param name="frame">The call.</param>
    /// <param name="id">Its id.</param>
    /// <param name="tool">The tool it named.</param>
    private void PassOn(RelayFrame frame, RequestId id, string tool)
    {
        _forwarded[id] = new Forwarded(id, tool);
        _link?.Send(frame.Bytes);
        UpdateProbing();
    }

    private void OnBackgroundOwnRequest(RelayFrame frame)
    {
        if (frame.Id is not { } id)
        {
            return;
        }

        if (frame.Method is ReadyToEndMethod)
        {
            AnswerReadyToEnd(id);
            return;
        }

        RelayLog.UnknownBackgroundMethod(_logger, frame.Method!);
        _link?.Send(RelayWire.Error(id, (int)McpErrorCode.MethodNotFound, $"This relay does not serve '{frame.Method}'."));
    }

    private async Task OnBackgroundOwnNotificationAsync(RelayFrame frame)
    {
        switch (frame.Method)
        {
            case CalledOffMethod:
                await OnCalledOffAsync().ConfigureAwait(false);
                break;

            case EndMethod:
                await OnEndAsync(RelayFrame.Node(frame.Params)).ConfigureAwait(false);
                break;

            default:
                RelayLog.UnknownBackgroundMethod(_logger, frame.Method!);
                break;
        }
    }

    /// <summary>
    /// The pipe closed under the relay: every call it was carrying is answered, once
    /// the finder has said whether the background crashed.
    /// </summary>
    /// <returns>A task that completes once the relay is looking again.</returns>
    private async Task OnBackgroundEndedAsync()
    {
        var interrupted = _forwarded.Values.ToList();
        _forwarded.Clear();

        RelayLog.Disconnected(_logger, interrupted.Count);

        foreach (var call in interrupted)
        {
            _awaitingExplanation[call.Id] = call;
        }

        // What the relay held for an update agreement is the client's still, and goes
        // the way any message goes when there is no background.
        var deferred = _deferred.ToList();
        _deferred.Clear();
        _agreed = false;
        _withdrawn = false;
        _helloId = null;
        _replayId = null;

        await DropTheLinkAsync().ConfigureAwait(false);
        ScheduleLook();

        if (interrupted.Count > 0 || _held.Count > 0 || deferred.Count > 0)
        {
            Explain(interrupted);
        }

        foreach (var waiting in deferred)
        {
            await OnClientFrameAsync(waiting.Frame, waiting.ArrivedAt).ConfigureAwait(false);
        }
    }

    /// <summary>Closes the connection, if there is one, and forgets what was outstanding on it.</summary>
    /// <returns>A task that completes once the pipe is closed.</returns>
    private async Task DropTheLinkAsync()
    {
        if (_link is not { } link)
        {
            return;
        }

        _link = null;
        _epoch++;
        _explaining = false;
        _hung = false;
        _probes.Clear();
        _answeredByRelay.Clear();
        UpdateProbing();

        await link.CloseAsync().ConfigureAwait(false);
    }

    /// <summary>Starts or stops asking the background whether it is alive.</summary>
    private void UpdateProbing()
    {
        if (WantsProbes && !_probing)
        {
            _probing = true;
            _lastLife = Now;
            _nextProbeAt = _lastLife + RelayConstants.ProbeInterval;
            Arm(RelayTimer.Probe, _nextProbeAt);
            Arm(RelayTimer.Hang, _lastLife + RelayConstants.HangBound);
        }
        else if (!WantsProbes && _probing)
        {
            _probing = false;
            Disarm(RelayTimer.Probe);
            Disarm(RelayTimer.Hang);
        }
    }

    private Task OnProbeTimer()
    {
        if (!_probing || _link is null)
        {
            return Task.CompletedTask;
        }

        var now = Now;

        if (now >= _nextProbeAt)
        {
            var id = RelayWire.IdPrefix + "ping-" + (++_probeCount).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _ = _probes.Add(new RequestId(id));
            _link.Send(RelayWire.Request(id, RequestMethods.Ping, null));

            _nextProbeAt += RelayConstants.ProbeInterval;

            if (_nextProbeAt <= now)
            {
                _nextProbeAt = now + RelayConstants.ProbeInterval;
            }
        }

        Arm(RelayTimer.Probe, _nextProbeAt);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The background has answered nothing the relay asked for the hang bound: every
    /// call it was carrying is answered with the hang sentence, and it is reported hung
    /// until it answers again. Nothing is ended.
    /// </summary>
    /// <returns>A task that completes once every call is answered.</returns>
    private async Task OnHangTimerAsync()
    {
        if (!_probing || _hung)
        {
            return;
        }

        if (Now - _lastLife < RelayConstants.HangBound)
        {
            Arm(RelayTimer.Hang, _lastLife + RelayConstants.HangBound);
            return;
        }

        _hung = true;

        var calls = _forwarded.Values.ToList();
        _forwarded.Clear();

        foreach (var call in calls)
        {
            // Kept, so the answer that may still come is dropped and the call keeps
            // the one answer it has had.
            _ = _answeredByRelay.Add(call.Id);
            await AnswerInPlaceAsync(call.Id, RelayErrors.Hung(call.Tool, wasPassedOn: true, _facts.LogPath), "the background stopped answering").ConfigureAwait(false);
        }

        var held = _phase is LinkPhase.Greeting or LinkPhase.Replaying ? _held.ToList() : [];

        foreach (var call in held)
        {
            _ = _held.Remove(call);
            await AnswerInPlaceAsync(call.Id, RelayErrors.Hung(call.Tool, wasPassedOn: false, _facts.LogPath), "the background stopped answering").ConfigureAwait(false);
        }

        ArmHoldTimer();
        RelayLog.Hung(_logger, RelayConstants.HangBound.TotalSeconds, calls.Count + held.Count);
        UpdateProbing();
    }

    /// <summary>The background answered something: it is alive, and the hang clock starts again.</summary>
    private void SignOfLife()
    {
        _lastLife = Now;

        if (_hung)
        {
            _hung = false;
            RelayLog.HangCleared(_logger);
        }

        UpdateProbing();

        if (_probing)
        {
            Arm(RelayTimer.Hang, _lastLife + RelayConstants.HangBound);
        }
    }

    /// <summary>A call passed on to the background and not yet answered.</summary>
    /// <param name="Id">Its id.</param>
    /// <param name="Tool">The tool it named.</param>
    private sealed record Forwarded(RequestId Id, string Tool);

    /// <summary>
    /// One connection to the background: a task that reads its frames into the loop,
    /// and a task that writes what the loop queues.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The writer is a task of its own so the loop never waits on the
    /// background.</b> A background that has stopped reading lets its pipe fill, and a
    /// loop that wrote into it would stop answering the client's handshake as well.
    /// </para>
    /// <para>
    /// <b>Closing the pipe is what ends the reader</b>, so the finder hands over a
    /// stream whose disposal ends a read in progress, as a
    /// <see cref="System.IO.Pipes.NamedPipeClientStream"/> opened with
    /// <see cref="System.IO.Pipes.PipeOptions.Asynchronous"/> does. A reader that
    /// outlives its connection does no harm: what it reads carries a connection the
    /// loop has already left, and is dropped.
    /// </para>
    /// </remarks>
    private sealed class BackgroundLink
    {
        private readonly RelayEngine _engine;
        private readonly int _epoch;
        private readonly Stream _pipe;
        private readonly Channel<byte[]> _outbox =
            Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        public BackgroundLink(RelayEngine engine, int epoch, Stream pipe)
        {
            _engine = engine;
            _epoch = epoch;
            _pipe = pipe;

            _ = Task.Run(ReadAsync, CancellationToken.None);
            _ = Task.Run(WriteAsync, CancellationToken.None);
        }

        /// <summary>Queues one frame for the background.</summary>
        /// <param name="frame">The frame, without its newline.</param>
        public void Send(byte[] frame)
        {
            _engine.Enter();

            if (!_outbox.Writer.TryWrite(frame))
            {
                _engine.Leave();
            }
        }

        /// <summary>Closes the pipe: the reader stops, and nothing queued after this is written.</summary>
        /// <returns>A task that completes once the pipe is closed.</returns>
        public async Task CloseAsync()
        {
            _ = _outbox.Writer.TryComplete();

            try
            {
                await _pipe.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // A pipe that fails to close is closed as far as this relay is
                // concerned: nothing is written to it or read from it again.
            }
        }

        private async Task ReadAsync()
        {
            try
            {
                await RelayLines.ReadAsync(_pipe, frame => _engine.Post(new BackgroundFrameArrived(_epoch, frame)), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
            {
                // A pipe that broke ended the connection as surely as one that
                // closed: the event below says so either way.
            }
            finally
            {
                _engine.Post(new BackgroundEnded(_epoch));
            }
        }

        private async Task WriteAsync()
        {
            var broken = false;

            await foreach (var frame in _outbox.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                try
                {
                    if (!broken)
                    {
                        await RelayLines.WriteAsync(_pipe, frame, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (Exception failure) when (failure is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
                {
                    broken = true;
                    _engine.Post(new BackgroundEnded(_epoch));
                }
                finally
                {
                    _engine.Leave();
                }
            }
        }
    }
}
