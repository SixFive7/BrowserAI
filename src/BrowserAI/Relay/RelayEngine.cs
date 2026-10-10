// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Relay;

/// <summary>
/// The relay: what <c>BrowserAI.exe --mcp</c> runs between one client and the one
/// background process that holds every session.
/// </summary>
/// <remarks>
/// <para>
/// <b>One relay per client process, and it never starts anything</b> (S and R,
/// decided 2026-10-08). It answers the handshake, the tool list and <c>ping</c> at
/// once from the binary, with no background at all (D6 a); passes every tool call to
/// the background byte for byte once it has one; holds a call for up to
/// <see cref="RelayConstants.HoldBound"/> while it has none; and answers with a
/// sentence from <see cref="RelayErrors"/> whenever the background cannot, so a model
/// is never left with a call that simply never returns.
/// </para>
/// <para>
/// <b>One loop owns every piece of state.</b> The client's input, the background's
/// pipe, the timers and the finder's answers are all turned into events on one
/// channel, and <see cref="RunAsync"/> handles them one at a time, so no state is
/// shared between threads and the order of what reaches the client is the order of
/// the loop. The finder runs on the thread pool, because it may block, and the
/// background's pipe is written by a task of its own, so a background that has
/// stopped reading can never stall an answer to the client.
/// </para>
/// <para>
/// <b>Every timer is on the <see cref="TimeProvider"/> it is given</b>, and every
/// number comes from <see cref="RelayConstants"/>, so the suite drives each one with a
/// clock it moves itself and no assertion waits on the wall clock.
/// </para>
/// <para>
/// <b>Nothing from the environment, and no frame's contents, reaches a log, a report
/// or the background.</b> A Claude Code server inherits a messaging socket and its
/// secret token in its environment (the root's measurement, 2026-10-08), so the relay
/// reads no environment variable at all, and its log records carry a frame's method
/// and id and never its <c>params</c>, <c>result</c> or <c>error</c>.
/// </para>
/// <para>
/// ⚠️ <i>Corrected 2026-10-10 by addition (previously the paragraph above said only
/// "the relay reads no environment variable at all", and the entrypoint was already
/// read for the classifier on 2026-10-08):</i> the engine itself reads none, and the
/// classifier it is handed reads exactly four, each by name, off the loop:
/// <c>CLAUDE_CODE_ENTRYPOINT</c>, <c>CLAUDE_CONFIG_DIR</c>, <c>CLAUDE_CODE_SESSION_ID</c>
/// and <c>USERPROFILE</c> (<see cref="ClientRecognition.Read(string?, Interop.ParentReading?, Func{string, string?})"/>). The greeting carries
/// the folder and the session id they name, which the background needs to name the
/// conversation and cannot read itself; no log record carries either, and nothing
/// enumerates the environment, so the messaging socket and its token stay where they
/// were.
/// </para>
/// </remarks>
internal sealed partial class RelayEngine
{
    private readonly Stream _clientInput;
    private readonly Stream _clientOutput;
    private readonly IBackgroundFinder _finder;
    private readonly IHandshake _handshake;
    private readonly Func<JsonObject> _toolList;
    private readonly Func<string?, ClientReading> _readClient;
    private readonly RelayFacts _facts;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;

    /// <summary>
    /// Every event, in the order it was posted. Created able to count what it holds,
    /// which a single-reader channel cannot do (<c>CanCount</c> is false for it,
    /// read on .NET 10.0.12 on 2026-10-08), for <see cref="EventsWaiting"/>.
    /// </summary>
    private readonly Channel<RelayEvent> _events = Channel.CreateUnbounded<RelayEvent>();

    private readonly Dictionary<RelayTimer, ITimer> _timers = [];
    private readonly Lock _settleGate = new();

    private TaskCompletionSource _settled = Settled();
    private CancellationToken _stopping;
    private int _pending;
    private int _started;
    private RelayEnd? _end;

    /// <summary>Creates a relay over one client's streams.</summary>
    /// <param name="clientInput">What the client writes: one JSON-RPC message per line. Its end ends the relay. Not disposed here.</param>
    /// <param name="clientOutput">Where the client reads: production hands <c>StdioChannel.Output</c>. Only this engine writes to it. Not disposed here.</param>
    /// <param name="finder">How the background is reached, and why it is not.</param>
    /// <param name="handshake">What answers <c>initialize</c>, <c>ping</c> and every other request that needs no background.</param>
    /// <param name="toolList">The rewritten tool list, as <c>tools/list</c> answers it: asked once, when it is first needed.</param>
    /// <param name="readClient">
    /// What the relay reads about a client of a given <c>clientInfo.name</c>: what it
    /// needs once an update has ended its relay (H1-T a), where it keeps the
    /// conversation, and its VS Code window (2026-10-10). Asked off the loop after each
    /// <c>initialize</c>, and sent in the greeting and nowhere else.
    /// </param>
    /// <param name="facts">What the relay knows about itself and its client.</param>
    /// <param name="clock">The clock every countdown runs on.</param>
    /// <param name="logger">Where the relay logs.</param>
    public RelayEngine(
        Stream clientInput,
        Stream clientOutput,
        IBackgroundFinder finder,
        IHandshake handshake,
        Func<JsonObject> toolList,
        Func<string?, ClientReading> readClient,
        RelayFacts facts,
        TimeProvider clock,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(clientInput);
        ArgumentNullException.ThrowIfNull(clientOutput);
        ArgumentNullException.ThrowIfNull(finder);
        ArgumentNullException.ThrowIfNull(handshake);
        ArgumentNullException.ThrowIfNull(toolList);
        ArgumentNullException.ThrowIfNull(readClient);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _clientInput = clientInput;
        _clientOutput = clientOutput;
        _finder = finder;
        _handshake = handshake;
        _toolList = toolList;
        _readClient = readClient;
        _facts = facts;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>The timers the loop arms.</summary>
    private enum RelayTimer
    {
        /// <summary>The next look for the background.</summary>
        Look,

        /// <summary>The earliest deadline of a held call.</summary>
        Hold,

        /// <summary>The next liveness probe.</summary>
        Probe,

        /// <summary>The moment the background has been silent for the hang bound.</summary>
        Hang,

        /// <summary>The trailing activity report.</summary>
        Report,
    }

    /// <summary>How many events are queued and not yet handled, for an arm that has to order two of them.</summary>
    internal int EventsWaiting => _events.Reader.Count;

    private DateTimeOffset Now => _clock.GetUtcNow();

    /// <summary>
    /// Runs the relay until the client's input ends, the token fires, or the
    /// background ends it for an update.
    /// </summary>
    /// <remarks>
    /// <b>The background's pipe is closed on the way out, whichever way out it is.</b>
    /// The client's input is not waited for: a read parked on a console stdin cannot be
    /// woken by anything this process does (measured 2026-09-15, in
    /// <c>JsonLinesTransport.DisposeAsync</c>), so the relay's exit never depends on it.
    /// </remarks>
    /// <param name="cancellationToken">Ends the relay: production fires it when the client's process has gone.</param>
    /// <returns>How it ended.</returns>
    public async Task<RelayEnd> RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) is not 0)
        {
            throw new InvalidOperationException("A relay engine runs once.");
        }

        _idleAt = Now;
        _reportedIdleAt = _idleAt;

        using var stopping = new CancellationTokenSource();
        _stopping = stopping.Token;

        StartReadingTheClient();

        using var stop = cancellationToken.Register(static state => ((RelayEngine)state!).Post(new StopRequested()), this);

        try
        {
            while (_end is null && await _events.Reader.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
            {
                while (_end is null && _events.Reader.TryRead(out var happened))
                {
                    try
                    {
                        await HandleAsync(happened).ConfigureAwait(false);
                    }
                    finally
                    {
                        Leave();
                    }
                }
            }

            if (_end?.Reason is RelayEnding.ForAnUpdate)
            {
                await AnswerWhatArrivedAfterTheEndAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await ShutDownAsync().ConfigureAwait(false);

            // Last: whatever still holds the token, a parked read of the client's input
            // or a finder call, is told the relay has gone.
            await stopping.CancelAsync().ConfigureAwait(false);
        }

        var end = _end ?? new RelayEnd(RelayEnding.ClientWentAway, null);
        RelayLog.Ended(_logger, end.Reason);
        return end;
    }

    /// <summary>
    /// Completes when every event posted so far has been handled, every finder call
    /// started so far has come back and every frame queued for the background has
    /// been written.
    /// </summary>
    /// <remarks>
    /// <b>What an arm waits on after it moves the clock</b>, so that it asserts on what
    /// the relay did and not on how long a thread took. A frame the client or the
    /// background writes counts from the moment the relay has read it, so an arm that
    /// writes one waits for its answer, not for this.
    /// </remarks>
    /// <returns>A task that completes when the relay is settled.</returns>
    internal Task SettledAsync()
    {
        lock (_settleGate)
        {
            return _pending is 0 ? Task.CompletedTask : _settled.Task;
        }
    }

    private static TaskCompletionSource Settled()
    {
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        settled.SetResult();
        return settled;
    }

    private Task HandleAsync(RelayEvent happened) => happened switch
    {
        ClientFrameArrived arrived => OnClientFrameAsync(RelayFrame.Read(arrived.Bytes), arrivedAt: null),
        ClientInputEnded => OnClientInputEnded(),
        BackgroundFrameArrived frame when frame.Epoch == _epoch => OnBackgroundFrameAsync(RelayFrame.Read(frame.Bytes)),
        BackgroundEnded ended when ended.Epoch == _epoch => OnBackgroundEndedAsync(),
        LookFinished look => OnLookFinishedAsync(look.Attempt),
        Classified classified => OnClassifiedAsync(classified.Reading),
        Explained explained => OnExplainedAsync(explained),
        TimerFired fired => OnTimerAsync(fired.Timer),
        StopRequested => OnStopRequested(),
        _ => Task.CompletedTask,
    };

    private Task OnTimerAsync(RelayTimer timer) => timer switch
    {
        RelayTimer.Look => OnLookTimer(),
        RelayTimer.Hold => OnHoldTimerAsync(),
        RelayTimer.Probe => OnProbeTimer(),
        RelayTimer.Hang => OnHangTimerAsync(),
        RelayTimer.Report => OnReportTimer(),
        _ => Task.CompletedTask,
    };

    private Task OnClientInputEnded()
    {
        if (_end is null)
        {
            RelayLog.ClientWentAway(_logger);
            _end = new RelayEnd(RelayEnding.ClientWentAway, null);
        }

        return Task.CompletedTask;
    }

    private Task OnStopRequested()
    {
        _end ??= new RelayEnd(RelayEnding.Cancelled, null);
        return Task.CompletedTask;
    }

    /// <summary>Queues one event for the loop.</summary>
    /// <param name="happened">The event.</param>
    private void Post(RelayEvent happened)
    {
        Enter();

        if (!_events.Writer.TryWrite(happened))
        {
            Leave();
        }
    }

    /// <summary>Counts one piece of work the loop has not finished.</summary>
    private void Enter()
    {
        lock (_settleGate)
        {
            if (_pending++ is 0)
            {
                _settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    /// <summary>Counts one piece of work finished.</summary>
    private void Leave()
    {
        TaskCompletionSource? settled = null;

        lock (_settleGate)
        {
            if (--_pending is 0)
            {
                settled = _settled;
            }
        }

        _ = settled?.TrySetResult();
    }

    /// <summary>Arms one timer for a moment, or queues its event now when the moment has come.</summary>
    /// <param name="timer">Which timer.</param>
    /// <param name="at">When it fires.</param>
    private void Arm(RelayTimer timer, DateTimeOffset at)
    {
        var delay = at - Now;

        if (delay <= TimeSpan.Zero)
        {
            Disarm(timer);
            Post(new TimerFired(timer));
            return;
        }

        if (!_timers.TryGetValue(timer, out var armed))
        {
            armed = _clock.CreateTimer(
                static state => ((TimerState)state!).Fire(),
                new TimerState(this, timer),
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            _timers[timer] = armed;
        }

        _ = armed.Change(delay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Stops one timer from firing.</summary>
    /// <param name="timer">Which timer.</param>
    private void Disarm(RelayTimer timer)
    {
        if (_timers.TryGetValue(timer, out var armed))
        {
            _ = armed.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Reads the client's frames on a task of their own, until its input ends.</summary>
    private void StartReadingTheClient() =>
        _ = Task.Run(async () =>
        {
            try
            {
                await RelayLines.ReadAsync(_clientInput, frame => Post(new ClientFrameArrived(frame)), _stopping).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
            {
                // The input ended in a way other than a clean end of file, and it
                // ended all the same: the event below says so.
            }
            finally
            {
                Post(new ClientInputEnded());
            }
        });

    /// <summary>Stops the timers, closes the pipe and refuses every event that arrives after.</summary>
    /// <returns>A task that completes once the pipe is closed.</returns>
    private async Task ShutDownAsync()
    {
        _ = _events.Writer.TryComplete();

        while (_events.Reader.TryRead(out _))
        {
            Leave();
        }

        foreach (var timer in _timers.Values)
        {
            await timer.DisposeAsync().ConfigureAwait(false);
        }

        _timers.Clear();

        await DropTheLinkAsync().ConfigureAwait(false);
    }

    /// <summary>One thing the loop handles.</summary>
    private abstract record RelayEvent;

    /// <summary>A frame from the client, as its bytes.</summary>
    /// <param name="Bytes">The frame.</param>
    private sealed record ClientFrameArrived(byte[] Bytes) : RelayEvent;

    /// <summary>The client's input ended.</summary>
    private sealed record ClientInputEnded : RelayEvent;

    /// <summary>A frame from one connection to the background.</summary>
    /// <param name="Epoch">The connection it came on.</param>
    /// <param name="Bytes">The frame.</param>
    private sealed record BackgroundFrameArrived(int Epoch, byte[] Bytes) : RelayEvent;

    /// <summary>One connection to the background ended.</summary>
    /// <param name="Epoch">The connection.</param>
    private sealed record BackgroundEnded(int Epoch) : RelayEvent;

    /// <summary>A look for the background came back.</summary>
    /// <param name="Attempt">The finder's attempt, completed one way or another.</param>
    private sealed record LookFinished(Task<Stream?> Attempt) : RelayEvent;

    /// <summary>The classifier said what this client needs once an update has ended its relay, and where it keeps its conversation.</summary>
    /// <param name="Reading">The classifier's answer, completed one way or another.</param>
    private sealed record Classified(Task<ClientReading> Reading) : RelayEvent;

    /// <summary>The finder said why there is no background.</summary>
    /// <param name="Epoch">The connection state the question was asked in.</param>
    /// <param name="AskedAt">When the question was asked, which is the newest deadline the answer may decide.</param>
    /// <param name="Reading">The finder's answer, completed one way or another.</param>
    /// <param name="Interrupted">The calls a closed pipe left unanswered, which this answer decides the sentence for.</param>
    private sealed record Explained(int Epoch, DateTimeOffset AskedAt, Task<BackgroundAbsence> Reading, List<Forwarded> Interrupted) : RelayEvent;

    /// <summary>A timer fired.</summary>
    /// <param name="Timer">Which one.</param>
    private sealed record TimerFired(RelayTimer Timer) : RelayEvent;

    /// <summary>The token handed to <see cref="RunAsync"/> fired.</summary>
    private sealed record StopRequested : RelayEvent;

    /// <summary>What a timer's callback needs: the engine, and which timer it is.</summary>
    /// <param name="Engine">The engine.</param>
    /// <param name="Timer">The timer.</param>
    private sealed record TimerState(RelayEngine Engine, RelayTimer Timer)
    {
        public void Fire() => Engine.Post(new TimerFired(Timer));
    }
}
