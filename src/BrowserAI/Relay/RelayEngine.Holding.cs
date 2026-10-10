// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using ModelContextProtocol.Protocol;

namespace BrowserAI.Relay;

/// <summary>
/// Calls the relay holds while it has no background, and what it tells each one
/// when holding cannot help.
/// </summary>
internal sealed partial class RelayEngine
{
    /// <summary>Calls waiting for a background, in the order they arrived.</summary>
    private readonly List<HeldCall> _held = [];

    /// <summary>Calls a closed pipe interrupted, waiting for the finder to say whether it crashed.</summary>
    private readonly Dictionary<RequestId, Forwarded> _awaitingExplanation = [];

    /// <summary>Whether the finder is being asked now, on behalf of the held calls of this connection state.</summary>
    private bool _explaining;

    /// <summary>
    /// Whether a call arrived, or a deadline came, while the finder was being asked:
    /// the answer in flight was read before it, so the finder is asked again once that
    /// answer is in.
    /// </summary>
    private bool _explainAgain;

    /// <summary>
    /// Holds a call, with a deadline of its arrival plus the hold bound: half of
    /// Codex's 300 s per tool call (D8 a).
    /// </summary>
    /// <param name="frame">The call.</param>
    /// <param name="id">Its id.</param>
    /// <param name="tool">The tool it named.</param>
    /// <param name="at">When it arrived.</param>
    private void Hold(RelayFrame frame, RequestId id, string tool, DateTimeOffset at)
    {
        _held.Add(new HeldCall(frame, id, tool, at));
        ArmHoldTimer();
        LookSooner();
    }

    /// <summary>Arms the hold timer for the earliest deadline nobody has asked about yet.</summary>
    private void ArmHoldTimer()
    {
        var next = DateTimeOffset.MaxValue;

        foreach (var call in _held)
        {
            if (!call.DeadlineAsked && call.Deadline < next)
            {
                next = call.Deadline;
            }
        }

        if (next == DateTimeOffset.MaxValue)
        {
            Disarm(RelayTimer.Hold);
        }
        else
        {
            Arm(RelayTimer.Hold, next);
        }
    }

    /// <summary>
    /// Some held call has reached its deadline: the finder is asked again, and the
    /// answer decides its sentence; or, for a background that was found and has not
    /// finished its greeting, the call is answered with the hang sentence.
    /// </summary>
    /// <returns>A task that completes once the due calls are answered or asked about.</returns>
    private async Task OnHoldTimerAsync()
    {
        var now = Now;
        var due = _held.Where(call => !call.DeadlineAsked && call.Deadline <= now).ToList();

        if (due.Count > 0)
        {
            if (_phase is LinkPhase.Greeting or LinkPhase.Replaying)
            {
                foreach (var call in due)
                {
                    _ = _held.Remove(call);
                    await AnswerInPlaceAsync(call.Id, RelayErrors.Hung(call.Tool, wasPassedOn: false, _facts.LogPath, _facts.DeveloperStart), "the background did not finish its greeting in time").ConfigureAwait(false);
                }
            }
            else
            {
                foreach (var call in due)
                {
                    call.DeadlineAsked = true;
                }

                ExplainUnlessAsked();
            }
        }

        ArmHoldTimer();
    }

    /// <summary>
    /// Asks the finder why there is no background; or, when it is being asked already,
    /// asks again once that answer is in.
    /// </summary>
    /// <remarks>
    /// <b>One question at a time, and never an answer older than the call it
    /// decides.</b> Found as a flake, 1 run in 10 of the crash arm: a call arrived while
    /// the finder was still answering for an earlier one, the answer said "starting"
    /// because it was read before the crash was recorded, and the call was held for
    /// the whole hold bound where it should have been answered at once.
    /// </remarks>
    private void ExplainUnlessAsked()
    {
        if (_explaining)
        {
            _explainAgain = true;
            return;
        }

        Explain([]);
    }

    /// <summary>Asks the finder why there is no background, on the thread pool.</summary>
    /// <param name="interrupted">The calls a closed pipe left unanswered, which the answer decides the sentence for.</param>
    private void Explain(List<Forwarded> interrupted)
    {
        _explaining = true;

        var epoch = _epoch;
        var askedAt = Now;
        var lastPid = _backgroundPid;

        Enter();

        var reading = Task.Run(() => _finder.Explain(lastPid), CancellationToken.None);

        _ = reading.ContinueWith(
            (done, state) =>
            {
                var engine = (RelayEngine)state!;
                engine.Post(new Explained(epoch, askedAt, done, interrupted));
                engine.Leave();
            },
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// The finder said why there is no background: the calls a closed pipe interrupted
    /// are answered, and so is every held call that waiting cannot help or whose
    /// deadline has come.
    /// </summary>
    /// <param name="explained">The finder's answer.</param>
    /// <returns>A task that completes once those calls are answered.</returns>
    private async Task OnExplainedAsync(Explained explained)
    {
        BackgroundAbsence absence;

        if (explained.Reading.IsCompletedSuccessfully)
        {
            absence = await explained.Reading.ConfigureAwait(false);
        }
        else
        {
            // Said to the model as a task nobody could read, with nothing of the
            // failure in the sentence: the exception goes to the log alone.
            RelayLog.ExplainFailed(_logger, explained.Reading.Exception);
            absence = new BackgroundAbsence.NotRunning(TaskState.Unknown, string.Empty, null);
        }

        foreach (var call in explained.Interrupted)
        {
            if (!_awaitingExplanation.Remove(call.Id))
            {
                // Cancelled by the client meanwhile: no answer, as MCP has it.
                continue;
            }

            await AnswerInPlaceAsync(
                call.Id,
                absence is BackgroundAbsence.Crashed crashed
                    ? RelayErrors.CrashedDuringTheCall(call.Tool, crashed.At, crashed.ExitCode, crashed.LogPath, _facts.DeveloperStart)
                    : RelayErrors.StoppedDuringTheCall(call.Tool, _facts.DeveloperStart),
                "the background's pipe closed while the call was running").ConfigureAwait(false);
        }

        if (explained.Epoch != _epoch)
        {
            // Asked before the relay connected or lost a connection since: about held
            // calls it says nothing true any more.
            return;
        }

        _explaining = false;

        if (_phase is not (LinkPhase.NotLooking or LinkPhase.Looking or LinkPhase.Connecting))
        {
            _explainAgain = false;
            return;
        }

        var atOnce = AnswersAtOnce(absence);

        foreach (var call in _held.ToList())
        {
            // A deadline is answered only from a question asked at or after it: item 5
            // of the build brief, "At a deadline, Explain() again".
            if (!atOnce && call.Deadline > explained.AskedAt)
            {
                continue;
            }

            _ = _held.Remove(call);

            await AnswerInPlaceAsync(
                call.Id,
                atOnce ? AtOnce(absence, call.Tool) : AtTheDeadline(absence, call.Tool),
                atOnce ? "waiting cannot help" : "no background appeared while it was held").ConfigureAwait(false);
        }

        ArmHoldTimer();

        if (_explainAgain)
        {
            _explainAgain = false;

            if (_held.Count > 0)
            {
                Explain([]);
            }
        }
    }

    /// <summary>
    /// Whether waiting cannot change the reason: a recorded crash, a refused root, a
    /// build that is not installed, an update installing (D8 a, R, 9 a, D11, U2).
    /// </summary>
    /// <param name="absence">The reason.</param>
    /// <returns><see langword="true"/> when every held call is answered now.</returns>
    private static bool AnswersAtOnce(BackgroundAbsence absence) =>
        absence is BackgroundAbsence.Crashed or BackgroundAbsence.RootRefused or BackgroundAbsence.NotInstalled or BackgroundAbsence.UpdateInstalling;

    /// <summary>The sentence for a reason that is answered at once.</summary>
    /// <param name="absence">The reason.</param>
    /// <param name="tool">The tool the call named.</param>
    /// <returns>The sentence.</returns>
    private string AtOnce(BackgroundAbsence absence, string tool) => absence switch
    {
        BackgroundAbsence.Crashed crashed => RelayErrors.Crashed(crashed.At, crashed.ExitCode, crashed.LogPath, _facts.DeveloperStart),
        BackgroundAbsence.RootRefused refused => RelayErrors.RootRefused(tool, refused.Refusal, refused.LogPath),
        BackgroundAbsence.NotInstalled build => RelayErrors.NotInstalled(tool, build.Executable, build.DataRoot),
        _ => RelayErrors.UpdateInstalling(tool, null, _clientName),
    };

    /// <summary>The sentence for a call whose deadline came with no background.</summary>
    /// <remarks>
    /// A background process that exists and never opened its pipe is answered with
    /// <see cref="RelayErrors.NoPipe"/>, which names the process for the person to end.
    /// <i>Corrected 2026-10-10 (previously "answered as a hang, since it is running and
    /// not answering"), the texts review's #115: the hang's Start Menu start ends nothing
    /// for a process with no pipe.</i> A clean end with nothing started since reads as
    /// not running, with nothing said about the task.
    /// </remarks>
    /// <param name="absence">The reason.</param>
    /// <param name="tool">The tool the call named.</param>
    /// <returns>The sentence.</returns>
    private string AtTheDeadline(BackgroundAbsence absence, string tool) => absence switch
    {
        BackgroundAbsence.NotRunning notRunning => RelayErrors.NotRunning(tool, notRunning.Task, notRunning.TaskName, notRunning.Detail),
        BackgroundAbsence.Starting starting => RelayErrors.NoPipe(tool, starting.ProcessId, _facts.LogPath, _facts.DeveloperStart),
        _ => RelayErrors.NotRunning(tool, TaskState.Unknown, string.Empty, null),
    };

    /// <summary>One held call.</summary>
    /// <param name="frame">The call, as the client wrote it.</param>
    /// <param name="id">Its id.</param>
    /// <param name="tool">The tool it named.</param>
    /// <param name="arrivedAt">When it arrived.</param>
    private sealed class HeldCall(RelayFrame frame, RequestId id, string tool, DateTimeOffset arrivedAt)
    {
        public RelayFrame Frame { get; } = frame;

        public RequestId Id { get; } = id;

        public string Tool { get; } = tool;

        /// <summary>When it is answered if no background has appeared.</summary>
        public DateTimeOffset Deadline { get; } = arrivedAt + RelayConstants.HoldBound;

        /// <summary>Whether its deadline has come and the finder has been asked about it.</summary>
        public bool DeadlineAsked { get; set; }
    }
}
