// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Relay;

/// <summary>
/// The relay's side of an update: its activity countdown, and the two-phase
/// agreement through which an update ends it.
/// </summary>
/// <remarks>
/// <para>
/// <b>H1, as the maintainer adjusted it on 2026-10-08:</b> a relay holds an update for
/// ten minutes after its client's last message other than <c>ping</c>, and when that
/// runs out nothing happens to it, in his words <i>"the relay is NOT terminated"</i>.
/// An update ends relays only through an agreement: the background asks every relay
/// whether it is ready to end, a relay says yes only with its countdown run out and
/// nothing in flight, and from then on it holds what its client sends. Any message
/// it holds calls the update off for everyone (RESOLUTIONS 13), and only when every
/// relay has agreed does the background tell each one to end.
/// </para>
/// <para>
/// <b>The countdown is a moment, never a remaining time</b>, so the background and
/// the dashboard compute what is left on their own clocks and a report read a second
/// ago is still right.
/// </para>
/// </remarks>
internal sealed partial class RelayEngine
{
    /// <summary>Client messages held while the relay has agreed to end, in arrival order.</summary>
    private readonly List<Deferred> _deferred = [];

    /// <summary>When the activity countdown runs out.</summary>
    private DateTimeOffset _idleAt;

    /// <summary>The countdown the background was last told.</summary>
    private DateTimeOffset _reportedIdleAt;

    /// <summary>When the background was last told it, for the spacing of reports.</summary>
    private DateTimeOffset? _lastReportAt;

    /// <summary>Whether the relay has said yes to ending for an update and has not been released.</summary>
    private bool _agreed;

    /// <summary>Whether a message arrived since the yes, and the withdrawal went out.</summary>
    private bool _withdrawn;

    /// <summary>Whether the background has committed the update and told the relay to end.</summary>
    private bool _ending;

    /// <summary>The version the background named when it committed.</summary>
    private string? _endVersion;

    /// <summary>
    /// The client did something other than <c>ping</c>: the countdown starts again,
    /// and the background hears of it.
    /// </summary>
    private void Activity()
    {
        var now = Now;
        _idleAt = now + RelayConstants.IdleCountdown;

        if (!_agreed)
        {
            ReportActivity();
            return;
        }

        // ⚠️ THE FIRST MESSAGE AFTER A YES CALLS THE UPDATE OFF FOR EVERYONE, and the
        // message itself is held: it is activity, so this relay no longer meets the
        // condition it agreed under (RESOLUTIONS 13).
        if (!_withdrawn)
        {
            _withdrawn = true;
            _link?.Send(RelayWire.Notification(WithdrawMethod, new JsonObject { ["idleAt"] = RelayWire.Instant(_idleAt) }));
            _reportedIdleAt = _idleAt;
            _lastReportAt = now;
            RelayLog.Withdrawn(_logger);
        }
    }

    /// <summary>
    /// Tells the background the countdown moved, at most once per
    /// <see cref="RelayConstants.ActivityReportGap"/>, and never leaves the last move
    /// untold.
    /// </summary>
    private void ReportActivity()
    {
        if (_phase is not LinkPhase.Ready || _agreed || _link is null || _reportedIdleAt == _idleAt)
        {
            return;
        }

        var now = Now;

        if (_lastReportAt is { } last && now - last < RelayConstants.ActivityReportGap)
        {
            // The trailing report: whatever the countdown is when the gap has passed.
            Arm(RelayTimer.Report, last + RelayConstants.ActivityReportGap);
            return;
        }

        _link.Send(RelayWire.Notification(ActivityMethod, new JsonObject { ["idleAt"] = RelayWire.Instant(_idleAt) }));
        _reportedIdleAt = _idleAt;
        _lastReportAt = now;
    }

    private Task OnReportTimer()
    {
        ReportActivity();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Answers the background's question: yes only with the countdown run out and
    /// nothing in flight or held; and a yes holds everything the client sends from
    /// here on.
    /// </summary>
    /// <param name="id">The question's id.</param>
    private void AnswerReadyToEnd(RequestId id)
    {
        var callInFlight = _forwarded.Count > 0
            || _held.Count > 0
            || _awaitingExplanation.Count > 0
            || _deferred.Exists(waiting => waiting.Frame.Kind is FrameKind.Request);

        var ready = Now >= _idleAt && !callInFlight && _deferred.Count is 0;

        var answer = new JsonObject
        {
            ["ready"] = ready,
            ["idleAt"] = RelayWire.Instant(_idleAt),
        };

        if (ready)
        {
            _agreed = true;
            _withdrawn = false;
            RelayLog.Agreed(_logger, _idleAt);
        }
        else
        {
            answer["callInFlight"] = callInFlight;
        }

        _link?.Send(RelayWire.Result(id, answer));
    }

    /// <summary>
    /// The update was called off: the agreement ends, and what the relay held goes
    /// on, in the order it arrived.
    /// </summary>
    /// <returns>A task that completes once every held message has gone its way.</returns>
    private async Task OnCalledOffAsync()
    {
        if (!_agreed)
        {
            return;
        }

        _agreed = false;
        _withdrawn = false;

        var deferred = _deferred.ToList();
        _deferred.Clear();

        RelayLog.CalledOff(_logger, deferred.Count);

        foreach (var waiting in deferred)
        {
            await OnClientFrameAsync(waiting.Frame, waiting.ArrivedAt).ConfigureAwait(false);
        }

        ReportActivity();
    }

    /// <summary>
    /// The background committed the update: every call the relay holds is answered
    /// with the update sentence, every call in flight with its in-flight form, and the
    /// relay ends.
    /// </summary>
    /// <param name="parameters">The commit's <c>params</c>: <c>now</c>, and the <c>version</c> being installed.</param>
    /// <returns>A task that completes once every call is answered and the pipe is closed.</returns>
    private async Task OnEndAsync(JsonNode? parameters)
    {
        var now = Flag(Member(parameters, "now"));
        var version = Text(Member(parameters, "version"));

        _ending = true;
        _endVersion = version;
        _agreed = false;

        var answered = 0;

        foreach (var waiting in _deferred)
        {
            if (waiting.Frame.Kind is FrameKind.Request && waiting.Frame.Id is { } id)
            {
                var tool = ToolOf(waiting.Frame);
                await AnswerInPlaceAsync(id, RelayErrors.UpdateInstalling(tool, version, _clientName), "an update is installing").ConfigureAwait(false);
                answered++;
            }
        }

        _deferred.Clear();

        foreach (var call in _held)
        {
            await AnswerInPlaceAsync(call.Id, RelayErrors.UpdateInstalling(call.Tool, version, _clientName), "an update is installing").ConfigureAwait(false);
            answered++;
        }

        _held.Clear();

        // ⚠️ With `now` unset the agreement means there are none: a relay says yes
        // only with nothing in flight and holds everything after. With `now` set, a
        // person chose Install now, and a call still running is cut off and told so.
        foreach (var call in _forwarded.Values.Concat(_awaitingExplanation.Values).ToList())
        {
            await AnswerInPlaceAsync(call.Id, RelayErrors.UpdateInstallingDuringTheCall(call.Tool, version, _clientName), "an update is installing").ConfigureAwait(false);
            answered++;
        }

        _forwarded.Clear();
        _awaitingExplanation.Clear();

        RelayLog.EndingForAnUpdate(_logger, now, answered);

        _end = new RelayEnd(RelayEnding.ForAnUpdate, version);
        await DropTheLinkAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Answers what the client sent after the commit and before the relay stops
    /// reading: the frames already queued when the end was handled.
    /// </summary>
    /// <returns>A task that completes once each is answered.</returns>
    private async Task AnswerWhatArrivedAfterTheEndAsync()
    {
        while (_events.Reader.TryRead(out var happened))
        {
            try
            {
                if (happened is ClientFrameArrived arrived)
                {
                    await OnClientFrameAsync(RelayFrame.Read(arrived.Bytes), arrivedAt: null).ConfigureAwait(false);
                }
            }
            finally
            {
                Leave();
            }
        }
    }

    /// <summary>
    /// A client frame after the commit: a call gets the update sentence; anything
    /// the binary answers by itself is answered as always; a notification is dropped.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <returns>A task that completes once it is answered.</returns>
    private async Task AnswerAfterTheEndAsync(RelayFrame frame)
    {
        if (frame.Kind is not FrameKind.Request || frame.Id is not { } id)
        {
            return;
        }

        switch (frame.Method)
        {
            case RequestMethods.ToolsCall:
            {
                var tool = ToolOf(frame);
                await AnswerInPlaceAsync(id, RelayErrors.UpdateInstalling(tool, _endVersion, _clientName), "an update is installing").ConfigureAwait(false);
                break;
            }

            case RequestMethods.ToolsList:
                _listed = true;
                await AnswerToolListAsync(id).ConfigureAwait(false);
                break;

            default:
                await AnswerLocallyAsync(frame).ConfigureAwait(false);
                break;
        }
    }

    private static string ToolOf(RelayFrame frame) =>
        RelayFrame.StringMember(frame.Params, "name"u8) ?? "<none>";

    /// <summary>A client message held while the relay agreed to end.</summary>
    /// <param name="Frame">The message.</param>
    /// <param name="ArrivedAt">When it arrived, which is when it counted as activity.</param>
    private sealed record Deferred(RelayFrame Frame, DateTimeOffset ArrivedAt);
}
