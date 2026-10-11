// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Coordination;

/// <summary>
/// The one handle the background's own thread waits on beside its stop, set when the
/// page has work for that thread.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Corrected 2026-10-10 (previously "The verbs the coordinator's pipe has
/// taken and the coordinator has not yet acted on, and the one handle that is set
/// when another arrives.")</b>. The coordinator's pipe went with S a on 2026-10-08,
/// and nothing posted a verb after that day. What was there for the verbs was
/// deleted on 2026-10-10 by the maintainer's decision <i>"9 a"</i>:
/// <c>CoordinatorArrival</c>, <c>Post</c>, <c>TryTake</c>, <c>IsEmpty</c> and
/// <c>StartTheHost</c> with the delegate that started the session host. What is left
/// is <see cref="ArrivedAsync"/> and <see cref="Wake"/>, which the background's loop in
/// <c>Program.Background.cs</c> and the page use. The paragraph below
/// is the record of the inbox.
/// </para>
/// <para>
/// <b>Two threads meet here and nowhere else.</b> The pipe's thread posts a verb
/// once its client has read the acknowledgement; the coordinator's own thread
/// takes it when it is woken through <c>Arrived</c>. <i>Corrected 2026-10-03
/// (previously "whether it is waiting on Arrived or running the window, whose timer
/// asks for TakeShow every 200 ms or so", with a paragraph on taking a show out of
/// turn while the window was open): the configuration window is gone, the browser
/// tab's address is handed out on the pipe's own thread, and every verb is taken in
/// turn.</i>
/// </para>
/// </remarks>
internal sealed class CoordinatorInbox : IDisposable
{
    private readonly SemaphoreSlim _arrived = new(0, 1);

    /// <summary>Completes once the page has woken the background since the last wait that saw it.</summary>
    /// <remarks>
    /// <b>Awaited since 2026-10-11</b>, the maintainer's decision 54, in his words verbatim:
    /// <i>"Never block a worker anywhere."</i> <i>Corrected that day (previously
    /// <c>Arrived</c>, a handle the background's main thread waited on beside its stop).</i>
    /// Two wakes before a wait are one, as they were.
    /// </remarks>
    /// <param name="cancellationToken">Ends the wait, which the background's stop does.</param>
    /// <returns>The wait.</returns>
    public Task ArrivedAsync(CancellationToken cancellationToken) => _arrived.WaitAsync(cancellationToken);

    /// <summary>Wakes whoever waits, with no verb: something else the coordinator watches changed.</summary>
    /// <remarks>
    /// <b>One wake for the coordinator to wait on, not two.</b> The page's tabs arriving
    /// and leaving and its linger running out all wake the loop through this.
    /// <i>Corrected 2026-10-11 (previously "... and its work for the coordinator's own
    /// thread all wake the loop through this, so the loop's wait keeps the pipe's handle
    /// and 63 processes' and nothing more"): the page has no work for a thread of the
    /// background's since that day, and the loop waits on no handle.</i> A wake after the
    /// inbox has gone wakes nothing, and a wake while one is pending is that one.
    /// </remarks>
    public void Wake()
    {
        try
        {
            _ = _arrived.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake is pending already, and the loop's next look sees both.
        }
        catch (ObjectDisposedException)
        {
            // The background has gone: nothing waits.
        }
    }

    /// <inheritdoc />
    public void Dispose() => _arrived.Dispose();
}
