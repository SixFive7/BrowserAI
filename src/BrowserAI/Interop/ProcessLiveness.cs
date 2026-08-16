// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Interop;

/// <summary>
/// Whether a process recorded earlier is still the process that was recorded —
/// answered by <c>(pid, creationFileTime)</c>, never by a pid alone and never by
/// a name.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what turns a stale lock into a sentence instead of a refusal.</b>
/// <c>lock.json</c> keeps its holder record after the holder dies, on purpose,
/// so a second BrowserAI can say <i>"held by PID 1234 since 14:02, no longer
/// running — reclaiming"</i> rather than simply failing. That sentence is only
/// safe if "no longer running" is answered correctly, and a pid on its own
/// cannot answer it: Windows reuses pids, and a reclaim keyed on a pid alone
/// eventually reads a stranger as the previous holder.
/// </para>
/// <para>
/// <b>Nothing here matches, counts or terminates by image name, and there is no
/// terminate at all.</b> <see cref="ClientProcessName"/> reads the parent's
/// image path for one purpose — writing a human-readable name into the record so
/// that a person reading <c>lock.json</c> knows which client opened the session.
/// It is display data. The rule forbids <i>choosing</i> a process by name; it
/// does not forbid observing one, and the distinction is kept sharp here because
/// this is exactly the file where it would erode.
/// </para>
/// <para>
/// <b>A failed wait is never interpreted.</b> <c>SYNCHRONIZE</c> is not implied
/// by <c>PROCESS_QUERY_LIMITED_INFORMATION</c>, and without it
/// <c>WaitForSingleObject</c> returns <c>WAIT_FAILED</c>. Read as "alive" that
/// makes every reclaim impossible; read as "gone" it makes every reclaim
/// succeed, which is worse. It throws instead.
/// </para>
/// </remarks>
internal static partial class ProcessLiveness
{
    private const uint ProcessQueryLimitedInformation = 0x00001000;
    private const uint Synchronize = 0x00100000;
    private const uint WaitObject0 = 0x00000000;
    private const uint WaitTimeout = 0x00000102;
    private const int ProcessBasicInformationClass = 0;

    /// <summary>This process's creation time, to be recorded beside its pid.</summary>
    /// <returns>A Windows FILETIME.</returns>
    /// <exception cref="Win32Exception">The query failed.</exception>
    public static long CreationTimeOfThisProcess() =>
        GetProcessTimes(GetCurrentProcess(), out var creation, out _, out _, out _)
            ? creation
            : throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not read this process's own creation time.");

    /// <summary>
    /// Whether the exact process recorded as <paramref name="processId"/> +
    /// <paramref name="createdFileTime"/> is still running.
    /// </summary>
    /// <param name="processId">The pid recorded when the lock was taken.</param>
    /// <param name="createdFileTime">Its creation time, recorded at the same moment.</param>
    /// <returns><see langword="true"/> only if that process is alive.</returns>
    /// <exception cref="Win32Exception">The wait could not be interpreted.</exception>
    public static bool IsAlive(int processId, long createdFileTime)
    {
        if (processId <= 0)
        {
            return false;
        }

        using var handle = OpenProcess(Synchronize | ProcessQueryLimitedInformation, bInheritHandle: false, (uint)processId);

        if (handle.IsInvalid)
        {
            // No such pid, or a pid this token cannot open at all. Either way it
            // is not a BrowserAI of ours holding this directory.
            return false;
        }

        var alive = GetProcessTimes(handle.DangerousGetHandle(), out var creation, out _, out _, out _)
            && creation == createdFileTime
            && WaitForSingleObject(handle.DangerousGetHandle(), 0) switch
            {
                WaitObject0 => false,
                WaitTimeout => true,
                var result => throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    $"Waiting on process {processId} returned 0x{result:X8}, so whether it is still holding this session is unknown."),
            };

        GC.KeepAlive(handle);
        return alive;
    }

    /// <summary>
    /// The image name of the process that started this one — the MCP client —
    /// for the record only.
    /// </summary>
    /// <returns>
    /// The name without its extension, or <see langword="null"/> when the parent
    /// cannot be read. A missing name is recorded as missing rather than guessed.
    /// </returns>
    public static string? ClientProcessName()
    {
        try
        {
            var parent = ParentProcessId();

            if (parent <= 0)
            {
                return null;
            }

            using var handle = OpenProcess(ProcessQueryLimitedInformation, bInheritHandle: false, (uint)parent);

            if (handle.IsInvalid)
            {
                return null;
            }

            var path = ImagePathOf(handle);
            GC.KeepAlive(handle);

            return path is null ? null : Path.GetFileNameWithoutExtension(path);
        }
#pragma warning disable CA1031 // A display string that cannot be read is recorded as absent; it can never be a reason to refuse a session.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private static int ParentProcessId()
    {
        var status = NtQueryInformationProcess(
            GetCurrentProcess(),
            ProcessBasicInformationClass,
            out var information,
            Marshal.SizeOf<ProcessBasicInformation>(),
            out _);

        // NTSTATUS: below zero is a failure, and there is no GetLastError to
        // consult. Reported as "no parent" rather than as pid 0, which is the
        // System process and would name the wrong thing in the record.
        return status < 0 ? 0 : (int)information.InheritedFromUniqueProcessId;
    }

    private static unsafe string? ImagePathOf(SafeProcessHandle handle)
    {
        // MAX_PATH is not the limit here -- the app manifest is longPathAware
        // and a client can live anywhere -- so the buffer is sized for the
        // extended limit rather than for the documented one.
        var buffer = new char[32768];
        var length = (uint)buffer.Length;

        fixed (char* start = buffer)
        {
            return QueryFullProcessImageNameW(handle.DangerousGetHandle(), 0, start, ref length)
                ? new string(buffer, 0, (int)length)
                : null;
        }
    }

    // The documented x64 layout, spelled with the real C types: the compiler
    // supplies the same padding the header does.
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public int ExitStatus;
        public nint PebBaseAddress;
        public nuint AffinityMask;
        public int BasePriority;
        public nuint UniqueProcessId;
        public nuint InheritedFromUniqueProcessId;
    }

    // System32 only, on every P/Invoke in this repository (CA5392). Without it
    // the loader searches the application directory first, and a DLL dropped
    // beside the binary would be loaded in preference to the real kernel32.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentProcess();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(
        IntPtr hProcess,
        out long lpCreationTime,
        out long lpExitTime,
        out long lpKernelTime,
        out long lpUserTime);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageNameW(
        IntPtr hProcess,
        uint dwFlags,
        char* lpExeName,
        ref uint lpdwSize);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        out ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);
}
