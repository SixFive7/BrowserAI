// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Asking about, and acting on, a process that was recorded earlier — by pid
/// <b>and</b> creation time, never by name.
/// </summary>
/// <remarks>
/// <para>
/// <b>The pair is the identity; the pid alone is not.</b> Windows reuses pids,
/// and a suite that terminates a pid it wrote down thirty seconds ago is a
/// suite that will one day terminate something else. Every call here re-reads
/// the creation time and refuses to act if it moved.
/// </para>
/// <para>
/// <b>There is no by-name variant of any of this, and there never will be.</b>
/// The rule that BrowserAI can only act on a job it created or a path it owns
/// has no exception for test code — the harness that counts <c>chrome.exe</c>
/// today is the sweep that kills the user's forty <c>firefox.exe</c> tomorrow.
/// </para>
/// </remarks>
internal static partial class ProcessIdentity
{
    private const uint ProcessQueryLimitedInformation = 0x00001000;
    private const uint ProcessTerminate = 0x00000001;

    /// <summary>
    /// Required to wait on a process handle, and <b>not</b> implied by
    /// <c>PROCESS_QUERY_LIMITED_INFORMATION</c>.
    /// </summary>
    /// <remarks>
    /// Measured 2026-08-16, and it is exactly this suite's own failure class.
    /// Without it <c>WaitForSingleObject</c> returns <c>WAIT_FAILED</c> with
    /// <c>ERROR_ACCESS_DENIED</c>; a survivor check that reads a failed wait as
    /// "still running" then reports every process it can open as alive forever,
    /// which presents as a containment defect in the product. The two are told
    /// apart below by refusing to interpret <c>WAIT_FAILED</c> at all.
    /// </remarks>
    private const uint Synchronize = 0x00100000;

    private const uint WaitObject0 = 0x00000000;
    private const uint WaitTimeout = 0x00000102;

    /// <summary>
    /// Whether the recorded process is still running — not merely whether
    /// something holds its pid.
    /// </summary>
    /// <param name="processId">The pid recorded at spawn.</param>
    /// <param name="createdFileTime">Its creation time, recorded at the same moment.</param>
    /// <returns><see langword="true"/> only if that exact process is alive.</returns>
    public static bool IsAlive(int processId, long createdFileTime)
    {
        using var handle = OpenProcess(Synchronize | ProcessQueryLimitedInformation, bInheritHandle: false, (uint)processId);

        if (handle.IsInvalid)
        {
            return false;
        }

        // The pid may have been reused already, in which case this is a
        // different process wearing the same number.
        if (!GetProcessTimes(handle, out var creation, out _, out _, out _) || creation != createdFileTime)
        {
            return false;
        }

        // A handle can outlive the process it names -- anything still holding
        // one keeps the pid and the object alive -- so "OpenProcess succeeded"
        // is not an answer. Waiting with a zero timeout is; an exit code of 259
        // is not, because a process may legitimately exit with 259.
        return WaitForSingleObject(handle, 0) switch
        {
            WaitObject0 => false,
            WaitTimeout => true,

            // Never guessed in either direction. A failed wait read as "alive"
            // makes a survivor check that can never pass; read as "gone" it
            // makes one that can never fail, which is worse.
            var result => throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                $"Waiting on process {processId} returned 0x{result:X8}, so whether it is alive is unknown."),
        };
    }

    /// <summary>
    /// Terminates the recorded process from outside, the way a crash, a session
    /// limit or Task Manager would.
    /// </summary>
    /// <param name="processId">The pid recorded at spawn.</param>
    /// <param name="createdFileTime">Its creation time, recorded at the same moment.</param>
    /// <exception cref="Win32Exception">The process could not be opened or terminated.</exception>
    /// <exception cref="InvalidOperationException">The pid has been reused, so terminating it would hit a stranger.</exception>
    public static void Terminate(int processId, long createdFileTime)
    {
        using var handle = OpenProcess(ProcessTerminate | ProcessQueryLimitedInformation, bInheritHandle: false, (uint)processId);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not open process {processId} to terminate it.");
        }

        if (!GetProcessTimes(handle, out var creation, out _, out _, out _))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not read the creation time of process {processId}.");
        }

        if (creation != createdFileTime)
        {
            throw new InvalidOperationException(
                $"Process {processId} was created at {creation} rather than the recorded {createdFileTime}: the pid has been reused and this is not our process.");
        }

        // Exit code 1 rather than 0, so a survivor check cannot mistake a
        // terminated process for one that shut down cleanly.
        if (!TerminateProcess(handle, 1))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not terminate process {processId}.");
        }
    }

    /// <summary>The creation time of a live process, for recording alongside its pid.</summary>
    /// <param name="processId">A live process.</param>
    /// <returns>Its creation time as a Windows FILETIME.</returns>
    /// <exception cref="Win32Exception">The process could not be opened or queried.</exception>
    public static long CreationTimeOf(int processId)
    {
        using var handle = OpenProcess(ProcessQueryLimitedInformation, bInheritHandle: false, (uint)processId);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not open process {processId}.");
        }

        return GetProcessTimes(handle, out var creation, out _, out _, out _)
            ? creation
            : throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not read the creation time of process {processId}.");
    }

    // System32 only, on every P/Invoke in this repository (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(
        SafeProcessHandle hProcess,
        out long lpCreationTime,
        out long lpExitTime,
        out long lpKernelTime,
        out long lpUserTime);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(SafeProcessHandle hHandle, uint dwMilliseconds);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(SafeProcessHandle hProcess, uint uExitCode);
}
