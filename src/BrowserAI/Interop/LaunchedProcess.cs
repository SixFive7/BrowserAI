// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Interop;

/// <summary>
/// A child process started by <see cref="JobLauncher"/>: its id, its three
/// streams, and an exit code that survives the handle being closed.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately not <see cref="System.Diagnostics.Process"/>. The
/// framework type cannot start a process into a job, and adopting one after the
/// fact by pid would hand back a <i>second</i> handle to the same process while
/// leaving the first — the one that keeps the pid from being reused — invisible.
/// </para>
/// <para>
/// Holding the process handle open is what makes every pid recorded here safe to
/// act on: Windows will not reuse a pid while a handle to it exists, so
/// "terminate pid 1234" cannot land on a stranger. <b>Measured 2026-08-18</b>
/// rather than assumed, with the control that makes it mean something: with the
/// handle released at exit a pid repeated after 2,010 spawns, and with a handle
/// held there was no repeat in 6,030
/// (<see href="../../../kb/windows/processes.md">kb</see>).
/// </para>
/// </remarks>
/// <param name="handle">The process handle. This object owns it.</param>
/// <param name="id">The child's process id.</param>
/// <param name="standardInput">The write end of the child's stdin pipe.</param>
/// <param name="standardOutput">The read end of the child's stdout pipe.</param>
/// <param name="standardError">The read end of the child's stderr pipe.</param>
internal sealed partial class LaunchedProcess(
    SafeProcessHandle handle,
    int id,
    Stream standardInput,
    Stream standardOutput,
    Stream standardError) : IDisposable
{
    private const uint WaitObject0 = 0x00000000;
    private const uint WaitTimeout = 0x00000102;
    private const uint WaitFailed = 0xFFFFFFFF;

    private int _disposed;

    /// <summary>The child's process id.</summary>
    public int Id { get; } = id;

    /// <summary>The child's standard input, ours to write.</summary>
    public Stream StandardInput { get; } = standardInput;

    /// <summary>The child's standard output, ours to read.</summary>
    public Stream StandardOutput { get; } = standardOutput;

    /// <summary>The child's standard error, ours to read.</summary>
    public Stream StandardError { get; } = standardError;

    /// <summary>Whether the child has already exited.</summary>
    /// <exception cref="Win32Exception">The wait failed.</exception>
    public bool HasExited => Wait(0);

    /// <summary>
    /// The child's exit code, or <see langword="null"/> if it is still running.
    /// </summary>
    /// <remarks>
    /// Callers cache this as an <see cref="int"/> the moment it exists. The
    /// framework's equivalent throws once its <c>Process</c> is disposed, which
    /// turns "why did the child die" into an exception on the reporting path.
    /// </remarks>
    /// <returns>The exit code, or <see langword="null"/>.</returns>
    /// <exception cref="Win32Exception">The query failed.</exception>
    public int? TryReadExitCode()
    {
        if (!HasExited)
        {
            return null;
        }

        if (!GetExitCodeProcess(handle, out var code))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not read the exit code of process {Id}.");
        }

        return (int)code;
    }

    /// <summary>Waits for the child to exit, or for the timeout to pass.</summary>
    /// <param name="timeout">How long to wait.</param>
    /// <returns><see langword="true"/> if the child exited within the timeout.</returns>
    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        if (HasExited)
        {
            return true;
        }

        // A thread-pool wait registration rather than a blocking wait on a
        // pooled thread: with ~100 concurrent BrowserAI processes, a five-second
        // blocking shutdown wait per child is five seconds of a thread each.
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var signal = new ManualResetEvent(initialState: false);
        var placeholder = signal.SafeWaitHandle;

        // ownsHandle: false -- the process handle belongs to this object and
        // outlives the wait.
        signal.SafeWaitHandle = new SafeWaitHandle(handle.DangerousGetHandle(), ownsHandle: false);
        placeholder.Dispose();

        var registration = ThreadPool.RegisterWaitForSingleObject(
            signal,
            static (state, timedOut) => ((TaskCompletionSource<bool>)state!).TrySetResult(!timedOut),
            completion,
            timeout,
            executeOnlyOnce: true);

        try
        {
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            // Before the ManualResetEvent above is disposed, which is why this
            // is a finally and not a using.
            _ = registration.Unregister(null);
            GC.KeepAlive(handle);
        }
    }

    /// <summary>
    /// Closes the streams and the process handle. It does <b>not</b> terminate
    /// the child — that is the job object's business, and this object has no
    /// kill path at all by design.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        StandardInput.Dispose();
        StandardOutput.Dispose();
        StandardError.Dispose();
        handle.Dispose();
    }

    private bool Wait(uint milliseconds)
    {
        var result = WaitForSingleObject(handle, milliseconds);
        GC.KeepAlive(handle);

        return result switch
        {
            WaitObject0 => true,
            WaitTimeout => false,
            WaitFailed => throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Waiting on process {Id} failed."),
            _ => throw new Win32Exception($"Waiting on process {Id} returned an unexpected result 0x{result:X8}."),
        };
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(SafeProcessHandle hHandle, uint dwMilliseconds);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(SafeProcessHandle hProcess, out uint lpExitCode);
}
