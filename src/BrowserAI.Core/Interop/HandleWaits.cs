// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Interop;

/// <summary>
/// Waits on a kernel object without a thread: the thread pool's wait registration watches
/// the handle, and its callback ends the task.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's decision 54, in his words verbatim: <i>"Never block a worker
/// anywhere."</i></b> A registration is serviced by the pool's wait thread, which watches up
/// to 63 handles at once and runs nothing else, so a wait here holds no worker for any part
/// of its length. <c>WaitHandle.WaitOne</c> and its kin are banned repository-wide since
/// 2026-10-11 (<c>build/BannedSymbols.txt</c>), and this is what replaces them.
/// </para>
/// <para>
/// ⚠️ <b>Never for a mutex.</b> A wait that is satisfied acquires the object for the thread
/// that waited, and here that is the pool's wait thread, which cannot release it.
/// <c>Sessions.MachineMutex</c> holds its mutex on a thread of its own for that reason.
/// </para>
/// <para>
/// <b>The registration holds a reference on the handle for as long as it is registered</b>,
/// which <c>ThreadPool.RegisterWaitForSingleObject</c> takes for a
/// <see cref="WaitHandle"/> itself, so disposing the handle while a wait is pending cannot
/// close it under the wait thread.
/// </para>
/// </remarks>
internal static class HandleWaits
{
    /// <summary>Whether a handle is signalled within a bound, waited for without a thread.</summary>
    /// <param name="handle">The handle: a process, an event, a waitable timer. Never a mutex.</param>
    /// <param name="timeout">How long to wait; <see cref="Timeout.InfiniteTimeSpan"/> for no bound.</param>
    /// <param name="cancellationToken">Ends the wait early, with <see cref="OperationCanceledException"/>.</param>
    /// <returns><see langword="true"/> when it was signalled, <see langword="false"/> when the bound ran out first.</returns>
    public static async Task<bool> WhenSignaledAsync(WaitHandle handle, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        cancellationToken.ThrowIfCancellationRequested();

        var signaled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = ThreadPool.RegisterWaitForSingleObject(
            handle,
            static (state, timedOut) => ((TaskCompletionSource<bool>)state!).TrySetResult(!timedOut),
            signaled,
            timeout,
            executeOnlyOnce: true);

        try
        {
            await using (cancellationToken.Register(static (state, token) => ((TaskCompletionSource<bool>)state!).TrySetCanceled(token), signaled).ConfigureAwait(false))
            {
                return await signaled.Task.ConfigureAwait(false);
            }
        }
        finally
        {
            _ = registration.Unregister(null);
        }
    }
}
