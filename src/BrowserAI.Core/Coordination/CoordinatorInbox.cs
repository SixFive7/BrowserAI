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
/// is <see cref="Arrived"/> and <see cref="Wake"/>, which the background's loop in
/// <c>Program.Background.cs</c> and <c>DesktopPageHost</c> use. The paragraph below
/// is the record of the inbox.
/// </para>
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
internal sealed class CoordinatorInbox : IDisposable
{
    private readonly EventWaitHandle _arrived = new(initialState: false, EventResetMode.AutoReset);

    /// <summary>Set each time the page wakes the thread; reset by the wait that sees it.</summary>
    /// <remarks>
    /// <i>Corrected 2026-10-10 (previously "Set each time a verb arrives; reset by the
    /// wait that sees it."), when the verbs were deleted.</i>
    /// </remarks>
    public WaitHandle Arrived => _arrived;

    /// <summary>Wakes whoever waits, with no verb: something else the coordinator watches changed.</summary>
    /// <remarks>
    /// <b>One handle for the coordinator to wait on, not two.</b> The page's tabs
    /// arriving and leaving, its linger running out and its work for the
    /// coordinator's own thread all wake the loop through this, so the loop's wait
    /// keeps the pipe's handle and 63 processes' and nothing more.
    /// <i>Corrected 2026-10-10 by addition: the loop is the background's since
    /// 2026-10-08, and its wait holds its stop and this handle.</i>
    /// </remarks>
    public void Wake() => _ = _arrived.Set();

    /// <inheritdoc />
    public void Dispose() => _arrived.Dispose();
}
