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
/// takes it when it is woken through <see cref="Arrived"/>. <i>Corrected 2026-10-03
/// (previously "whether it is waiting on Arrived or running the window, whose timer
/// asks for TakeShow every 200 ms or so", with a paragraph on taking a show out of
/// turn while the window was open): the configuration window is gone, the browser
/// tab's address is handed out on the pipe's own thread, and every verb is taken in
/// turn.</i>
/// </para>
/// </remarks>
/// <param name="startTheHost">
/// Starts the session host when none runs and says whether one runs now, or
/// <see langword="null"/> for a coordinator that has none to start (Q366 b).
/// </param>
internal sealed class CoordinatorInbox(Func<bool>? startTheHost = null) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<CoordinatorArrival> _pending = [];
    private readonly EventWaitHandle _arrived = new(initialState: false, EventResetMode.AutoReset);

    /// <summary>
    /// Starts the session host when none runs, on the pipe's own thread, before the
    /// <c>host</c> verb is answered.
    /// </summary>
    /// <remarks>
    /// <b>Not queued like the other verbs, and that is the point of it.</b> A server
    /// waits a few seconds for the host's pipe and then serves its client itself, and
    /// the coordinator's own thread may be inside the window the whole time, where a
    /// queued verb waits for the window to close. Starting it here answers the server
    /// with a host that runs, or with a refusal it can act on at once.
    /// </remarks>
    /// <returns>Whether a host runs now.</returns>
    public bool StartTheHost() => startTheHost?.Invoke() ?? false;

    /// <summary>Set each time a verb arrives; reset by the wait that sees it.</summary>
    public WaitHandle Arrived => _arrived;

    /// <summary>Whether no verb is waiting.</summary>
    public bool IsEmpty
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count is 0;
            }
        }
    }

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

    /// <summary>Wakes whoever waits, with no verb: something else the coordinator watches changed.</summary>
    /// <remarks>
    /// <b>One handle for the coordinator to wait on, not two.</b> The page's tabs
    /// arriving and leaving, its linger running out and its work for the
    /// coordinator's own thread all wake the loop through this, so the loop's wait
    /// keeps the pipe's handle and 63 processes' and nothing more.
    /// </remarks>
    public void Wake() => _ = _arrived.Set();

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

    /// <inheritdoc />
    public void Dispose() => _arrived.Dispose();
}
