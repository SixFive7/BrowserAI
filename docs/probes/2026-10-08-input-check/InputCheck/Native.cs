// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace InputCheck;

/// <summary>
/// The rig's own reads: thread and process cycle counts and times, and the system
/// timer's resolution. None of these is in the product.
/// </summary>
internal static partial class Native
{
    /// <summary>The cycles the calling thread has used, user and kernel together.</summary>
    /// <returns>The count.</returns>
    public static ulong ThreadCycles() =>
        QueryThreadCycleTime(GetCurrentThread(), out var cycles) ? cycles : throw new InvalidOperationException("QueryThreadCycleTime failed: " + Marshal.GetLastPInvokeError());

    /// <summary>The cycles a process has used, every thread together.</summary>
    /// <param name="process">The process, or null for this one.</param>
    /// <returns>The count.</returns>
    public static ulong ProcessCycles(SafeProcessHandle? process = null)
    {
        var answered = process is null
            ? QueryProcessCycleTime(GetCurrentProcess(), out var cycles)
            : QueryProcessCycleTime(process, out cycles);

        return answered ? cycles : throw new InvalidOperationException("QueryProcessCycleTime failed: " + Marshal.GetLastPInvokeError());
    }

    /// <summary>The calling thread's kernel and user time, in 100 ns units.</summary>
    /// <returns>Both times.</returns>
    public static (long Kernel, long User) ThreadTimes() =>
        GetThreadTimes(GetCurrentThread(), out _, out _, out var kernel, out var user)
            ? (kernel, user)
            : throw new InvalidOperationException("GetThreadTimes failed: " + Marshal.GetLastPInvokeError());

    /// <summary>A process's kernel and user time, in 100 ns units.</summary>
    /// <param name="process">The process, or null for this one.</param>
    /// <returns>Both times.</returns>
    public static (long Kernel, long User) ProcessTimes(SafeProcessHandle? process = null)
    {
        long kernel;
        long user;

        var answered = process is null
            ? GetProcessTimes(GetCurrentProcess(), out _, out _, out kernel, out user)
            : GetProcessTimes(process, out _, out _, out kernel, out user);

        return answered ? (kernel, user) : throw new InvalidOperationException("GetProcessTimes failed: " + Marshal.GetLastPInvokeError());
    }

    /// <summary>The system timer's resolution, coarsest, finest and current, in 100 ns units.</summary>
    /// <returns>The three, or null when ntdll does not answer.</returns>
    public static (uint Coarsest, uint Finest, uint Current)? TimerResolution() =>
        NtQueryTimerResolution(out var coarsest, out var finest, out var current) is 0
            ? (coarsest, finest, current)
            : null;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentThread();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryThreadCycleTime(nint threadHandle, out ulong cycleTime);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryProcessCycleTime(nint processHandle, out ulong cycleTime);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "QueryProcessCycleTime", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryProcessCycleTime(SafeProcessHandle processHandle, out ulong cycleTime);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetThreadTimes(nint thread, out long creation, out long exit, out long kernel, out long user);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "GetProcessTimes", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryTimerResolution(out uint coarsest, out uint finest, out uint current);
}
