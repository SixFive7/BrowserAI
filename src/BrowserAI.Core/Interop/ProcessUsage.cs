// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Interop;

/// <summary>What Windows says one process has used: its CPU time and its memory.</summary>
/// <param name="CpuTicks">Its kernel and user time since it started, in 100-nanosecond ticks.</param>
/// <param name="WorkingSet">Its working set, in bytes: the memory of it in RAM.</param>
/// <param name="PrivateWorkingSet">The part of its working set no other process holds, in bytes.</param>
/// <param name="SharedCommit">The commit charge of the shared memory it maps, in bytes.</param>
internal readonly record struct ProcessUse(long CpuTicks, long WorkingSet, long PrivateWorkingSet, long SharedCommit)
{
    /// <summary>The part of its working set it shares with other processes, its program files' pages among them.</summary>
    public long SharedWorkingSet => Math.Max(0, WorkingSet - PrivateWorkingSet);
}

/// <summary>One member of a job, read.</summary>
/// <param name="ProcessId">Its pid.</param>
/// <param name="CreatedFileTime">Its creation time, which with the pid is its identity.</param>
/// <param name="Use">What it used.</param>
/// <param name="ImagePath">The executable it runs, when it was asked for and could be read.</param>
internal readonly record struct JobMemberUse(int ProcessId, long CreatedFileTime, ProcessUse Use, string? ImagePath);

/// <summary>
/// What a process has used, read through <c>GetProcessTimes</c> and <c>GetProcessMemoryInfo</c>
/// with <c>PROCESS_MEMORY_COUNTERS_EX2</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-10-12 for the page's CPU and memory</b>, the maintainer's 59 b, 62 a and 65 a.
/// <c>PROCESS_MEMORY_COUNTERS_EX2</c> carries <c>PrivateWorkingSetSize</c> and
/// <c>SharedCommitUsage</c> beside the working set, and needs Windows 10 22H2 or Windows 11
/// 22H2, each with the September 2023 cumulative update, as Microsoft's page for the structure
/// says; where Windows refuses it, a read answers nothing and the page says it could not
/// measure. <c>GetProcessMemoryInfo</c> is <c>K32GetProcessMemoryInfo</c> in
/// <c>kernel32.dll</c> since Windows 7, the function's page says, so no <c>psapi.dll</c> is
/// loaded.
/// </para>
/// <para>
/// <b>A member of a job is read only once it is known to be one.</b> The pid comes from the
/// job's own list, and a process that ended between the list and the open may have left its
/// pid to a stranger, so each opened process is asked whether it is in the job before
/// anything of it is counted, and its creation time is read off the same handle.
/// </para>
/// <para>
/// <b>Every call here waits on nothing</b>: each asks the kernel about a process and answers
/// from what the kernel keeps. None has an asynchronous form, so the reader runs them on a
/// thread of its own and no pool worker makes them (54).
/// </para>
/// </remarks>
internal static partial class ProcessUsage
{
    private const uint ProcessQueryLimitedInformation = 0x00001000;

    /// <summary>
    /// The longest image path a member is asked for, in characters: the extended limit and not
    /// <c>MAX_PATH</c>, as <c>BrowserProcesses</c> sizes it, since the browsers root is under the
    /// caller's own profile and can be deep.
    /// </summary>
    private const int ImagePathCapacity = 32768;

    /// <summary>Reads one process through a handle held open with at least <c>PROCESS_QUERY_LIMITED_INFORMATION</c>, a <see cref="HeldProcess"/>'s.</summary>
    /// <param name="process">The handle.</param>
    /// <returns>What it used, or <see langword="null"/> when Windows would not say.</returns>
    public static ProcessUse? Read(SafeWaitHandle process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!GetProcessTimes(process, out _, out _, out var kernel, out var user))
        {
            return null;
        }

        var counters = new ProcessMemoryCountersEx2 { Size = (uint)Unsafe.SizeOf<ProcessMemoryCountersEx2>() };

        return K32GetProcessMemoryInfo(process, ref counters, counters.Size)
            ? Use(kernel + user, counters)
            : null;
    }

    /// <summary>Reads the process this code runs in.</summary>
    /// <returns>What it used, or <see langword="null"/> when Windows would not say.</returns>
    public static ProcessUse? OfThisProcess()
    {
        // The pseudo-handle, which is not a handle anything owns or closes.
        var self = GetCurrentProcess();

        if (!GetProcessTimes(self, out _, out _, out var kernel, out var user))
        {
            return null;
        }

        var counters = new ProcessMemoryCountersEx2 { Size = (uint)Unsafe.SizeOf<ProcessMemoryCountersEx2>() };

        return K32GetProcessMemoryInfo(self, ref counters, counters.Size)
            ? Use(kernel + user, counters)
            : null;
    }

    /// <summary>Reads one member of a job, by the pid the job's own list gave.</summary>
    /// <param name="job">The job.</param>
    /// <param name="processId">The pid.</param>
    /// <param name="wantsImage">Asked with the pid and the creation time read off the handle: whether to read the executable it runs as well.</param>
    /// <returns>The member, or <see langword="null"/> when it has gone, is no longer in the job, or Windows would not say.</returns>
    public static JobMemberUse? ReadMember(JobObject job, int processId, Func<int, long, bool> wantsImage)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(wantsImage);

        using var process = OpenProcess(ProcessQueryLimitedInformation, bInheritHandle: false, (uint)processId);

        if (process.IsInvalid
            || !GetProcessTimes(process, out var created, out _, out var kernel, out var user)
            || !job.Contains(process))
        {
            return null;
        }

        var counters = new ProcessMemoryCountersEx2 { Size = (uint)Unsafe.SizeOf<ProcessMemoryCountersEx2>() };

        if (!K32GetProcessMemoryInfo(process, ref counters, counters.Size))
        {
            return null;
        }

        return new JobMemberUse(processId, created, Use(kernel + user, counters), wantsImage(processId, created) ? ImagePathOf(process) : null);
    }

    private static ProcessUse Use(long cpuTicks, in ProcessMemoryCountersEx2 counters) =>
        new(cpuTicks, (long)counters.WorkingSetSize, (long)counters.PrivateWorkingSetSize, (long)counters.SharedCommitUsage);

    private static string? ImagePathOf(SafeProcessHandle process)
    {
        // UTF-16 code units, which a ushort carries blittably: a char is not, under the
        // generated marshalling this project keeps.
        var buffer = new ushort[ImagePathCapacity];
        var length = (uint)buffer.Length;

        return QueryFullProcessImageNameW(process, 0, ref buffer[0], ref length)
            ? new string(MemoryMarshal.Cast<ushort, char>(buffer.AsSpan(0, (int)length)))
            : null;
    }

    // The documented layout of PROCESS_MEMORY_COUNTERS_EX2, 96 bytes on x64, held against
    // Microsoft's metadata by InteropLayoutTests. CS0649 is disabled across it: the kernel
    // fills it, and most fields exist to make it the size Windows expects.
#pragma warning disable CS0649
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx2
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
        public nuint PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }
#pragma warning restore CS0649

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(SafeProcessHandle hProcess, out long lpCreationTime, out long lpExitTime, out long lpKernelTime, out long lpUserTime);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, EntryPoint = "GetProcessTimes")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(SafeWaitHandle hProcess, out long lpCreationTime, out long lpExitTime, out long lpKernelTime, out long lpUserTime);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, EntryPoint = "GetProcessTimes")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(nint hProcess, out long lpCreationTime, out long lpExitTime, out long lpKernelTime, out long lpUserTime);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool K32GetProcessMemoryInfo(SafeProcessHandle hProcess, ref ProcessMemoryCountersEx2 ppsmemCounters, uint cb);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, EntryPoint = "K32GetProcessMemoryInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool K32GetProcessMemoryInfo(SafeWaitHandle hProcess, ref ProcessMemoryCountersEx2 ppsmemCounters, uint cb);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, EntryPoint = "K32GetProcessMemoryInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool K32GetProcessMemoryInfo(nint hProcess, ref ProcessMemoryCountersEx2 ppsmemCounters, uint cb);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageNameW(SafeProcessHandle hProcess, uint dwFlags, ref ushort lpExeName, ref uint lpdwSize);
}
