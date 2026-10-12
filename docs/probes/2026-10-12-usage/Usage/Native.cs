// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;
using BrowserAI.Interop;

namespace Usage;

/// <summary>
/// The rig's own calls: starting a browser suspended inside a job and letting it go, and
/// this process's cycles and times. None of these is how the product starts a browser; the
/// product's launcher starts a child already in its job, which a reading cannot tell apart.
/// </summary>
internal static partial class Native
{
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateNoWindow = 0x08000000;

    /// <summary>Starts a program suspended, puts it in a job, and lets it run.</summary>
    /// <param name="job">The job.</param>
    /// <param name="image">The program.</param>
    /// <param name="commandLine">Its whole command line.</param>
    /// <param name="directory">Its working directory.</param>
    /// <returns>Its pid.</returns>
    public static int StartInJob(JobObject job, string image, string commandLine, string directory)
    {
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
        var line = (commandLine + "\0").ToCharArray();

        if (!CreateProcessW(image, line, 0, 0, bInheritHandles: false, CreateSuspended | CreateNoWindow, 0, directory, ref startup, out var started))
        {
            throw new InvalidOperationException("CreateProcessW failed: " + Marshal.GetLastPInvokeError());
        }

        try
        {
            if (!AssignProcessToJobObject(job.Handle, started.Process))
            {
                throw new InvalidOperationException("AssignProcessToJobObject failed: " + Marshal.GetLastPInvokeError());
            }

            _ = ResumeThread(started.Thread);
            return started.ProcessId;
        }
        finally
        {
            _ = CloseHandle(started.Thread);
            _ = CloseHandle(started.Process);
        }
    }

    /// <summary>The cycles this process has used, every thread together.</summary>
    /// <returns>The count.</returns>
    public static ulong ProcessCycles() =>
        QueryProcessCycleTime(GetCurrentProcess(), out var cycles) ? cycles : throw new InvalidOperationException("QueryProcessCycleTime failed: " + Marshal.GetLastPInvokeError());

    /// <summary>This process's kernel and user time together, in 100 ns units.</summary>
    /// <returns>The time.</returns>
    public static long ProcessTime() =>
        GetProcessTimes(GetCurrentProcess(), out _, out _, out var kernel, out var user) ? kernel + user : throw new InvalidOperationException("GetProcessTimes failed: " + Marshal.GetLastPInvokeError());

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public nint Reserved2;
        public nint StdInput;
        public nint StdOutput;
        public nint StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessW(
        string lpApplicationName,
        char[] lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        nint lpEnvironment,
        string lpCurrentDirectory,
        ref StartupInfo lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(SafeJobHandle hJob, nint hProcess);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint ResumeThread(nint hThread);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryProcessCycleTime(nint hProcess, out ulong cycleTime);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(nint hProcess, out long lpCreationTime, out long lpExitTime, out long lpKernelTime, out long lpUserTime);
}
