// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Coordination;

/// <summary>One verb that reached the coordinator, and who sent it.</summary>
/// <param name="Verb">What was asked.</param>
/// <param name="From">The pid on the other end of the pipe, when Windows said.</param>
internal sealed record CoordinatorArrival(CoordinatorVerb Verb, int? From);

/// <summary>
/// The verbs the coordinator's pipe has taken and the coordinator has not yet
/// acted on, and the one handle that is set when another arrives.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two threads meet here and nowhere else.</b> The pipe's thread posts a verb
/// once its client has read the acknowledgement; the coordinator's own thread
/// takes it, whether it is waiting on <see cref="Arrived"/> or running the window,
/// whose timer asks for <see cref="TakeShow"/> every 200 ms or so.
/// </para>
/// <para>
/// <b>A show can be taken out of turn, and only a show.</b> While the window is
/// open, the coordinator's thread is inside the dialog, and a second start asking
/// for the window has to be answered from there; a recheck waits for the window to
/// close, because nothing is applied while it is open.
/// </para>
/// </remarks>
internal sealed class CoordinatorInbox : IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<CoordinatorArrival> _pending = [];
    private readonly EventWaitHandle _arrived = new(initialState: false, EventResetMode.AutoReset);

    /// <summary>Set each time a verb arrives; reset by the wait that sees it.</summary>
    public WaitHandle Arrived => _arrived;

    /// <summary>Posts a verb and wakes whoever waits.</summary>
    /// <param name="verb">What was asked.</param>
    /// <param name="from">Who asked, when known.</param>
    public void Post(CoordinatorVerb verb, int? from)
    {
        lock (_gate)
        {
            _pending.Add(new CoordinatorArrival(verb, from));
        }

        _ = _arrived.Set();
    }

    /// <summary>Takes the oldest verb.</summary>
    /// <param name="arrival">The verb, when there was one.</param>
    /// <returns>Whether there was one.</returns>
    public bool TryTake(out CoordinatorArrival? arrival)
    {
        lock (_gate)
        {
            if (_pending.Count is 0)
            {
                arrival = null;
                return false;
            }

            arrival = _pending[0];
            _pending.RemoveAt(0);
            return true;
        }
    }

    /// <summary>Takes every waiting show at once, leaving the rechecks where they are.</summary>
    /// <returns>Whether any show was waiting.</returns>
    public bool TakeShow()
    {
        lock (_gate)
        {
            return _pending.RemoveAll(arrival => arrival.Verb is CoordinatorVerb.Show) > 0;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _arrived.Dispose();
}
