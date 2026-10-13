// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// The restore-stall rig's job objects, its reading of a job's members and the
// suite lock's directory, compiled by run.ps1 with Add-Type. Every member asks
// the kernel one thing and returns. Nothing here waits, sleeps or blocks on a
// handle, and nothing selects a process by its image: a process is read only
// because the job this launcher created holds it.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

public static class RigJob
{
    private const uint KillOnJobClose = 0x2000;
    private const int ExtendedLimitInformation = 9;
    private const int BasicAccountingInformation = 1;
    private const int BasicProcessIdList = 3;
    private const uint QueryLimitedInformation = 0x1000;
    private const int ProcessBasicInformationClass = 0;
    private const int ProcessCommandLineInformationClass = 60;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const int ErrorAlreadyExists = 183;

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformationBlock
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicAccounting
    {
        public long TotalUserTime, TotalKernelTime, ThisPeriodTotalUserTime, ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public UIntPtr UniqueProcessId;
        public UIntPtr InheritedFromUniqueProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryCounters
    {
        public uint cb;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
        public UIntPtr PrivateUsage;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributes, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimitInformationBlock info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length, out uint returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(IntPtr job, int infoClass, ref BasicAccounting info, uint length, out uint returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo", SetLastError = true)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, out MemoryCounters counters, uint size);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, ref ProcessBasicInformation info, int length, out int returned);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, IntPtr info, int length, out int returned);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateDirectoryW(string path, IntPtr security);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool RemoveDirectoryW(string path);

    // Every pid this launcher has described in full, keyed with its creation time, so
    // that a census line carries a process's image and command line once.
    private static readonly HashSet<string> Described = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>A job that ends every member when its last handle closes.</summary>
    public static IntPtr Create()
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);

        if (job == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateJobObjectW failed with " + Marshal.GetLastWin32Error());
        }

        var information = new ExtendedLimitInformationBlock();
        information.BasicLimitInformation.LimitFlags = KillOnJobClose;

        if (!SetInformationJobObject(job, ExtendedLimitInformation, ref information, (uint)Marshal.SizeOf(typeof(ExtendedLimitInformationBlock))))
        {
            var error = Marshal.GetLastWin32Error();
            CloseHandle(job);
            throw new InvalidOperationException("SetInformationJobObject failed with " + error);
        }

        return job;
    }

    /// <summary>Puts a process this launcher started into the job, by the handle its start returned.</summary>
    public static int AssignHandle(IntPtr job, IntPtr process) =>
        AssignProcessToJobObject(job, process) ? 0 : Marshal.GetLastWin32Error();

    /// <summary>Ends a process this launcher started and could not put into a job, by its own handle.</summary>
    public static bool EndStarted(IntPtr process) => TerminateProcess(process, 1);

    /// <summary>Ends every member of the job and closes it; returns how many were still running.</summary>
    public static uint Terminate(IntPtr job)
    {
        var active = Accounting(job).ActiveProcesses;
        TerminateJobObject(job, 1);
        CloseHandle(job);
        return active;
    }

    /// <summary>The suite lock's protocol: true when this call made the directory, false when it was there.</summary>
    public static bool TryCreateDirectory(string path)
    {
        if (CreateDirectoryW(path, IntPtr.Zero))
        {
            return true;
        }

        var error = Marshal.GetLastWin32Error();

        if (error == ErrorAlreadyExists)
        {
            return false;
        }

        throw new InvalidOperationException("CreateDirectoryW failed with " + error + " for " + path);
    }

    /// <summary>Removes an empty directory; false when it is not empty or not there.</summary>
    public static bool RemoveEmptyDirectory(string path) => RemoveDirectoryW(path);

    /// <summary>
    /// One census line: the time, the job's counts, and every member with its parent,
    /// creation time, processor time and working set. A member seen for the first time
    /// also carries its image path and command line.
    /// </summary>
    public static string Snapshot(IntPtr job)
    {
        var accounting = Accounting(job);
        var line = new StringBuilder(512);

        line.Append("{\"t\":\"").Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append('"')
            .Append(",\"active\":").Append(accounting.ActiveProcesses)
            .Append(",\"total\":").Append(accounting.TotalProcesses)
            .Append(",\"procs\":[");

        var first = true;

        foreach (var processId in Members(job))
        {
            var handle = OpenProcess(QueryLimitedInformation, false, processId);

            if (handle == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                long creation, exit, kernel, user;

                if (!GetProcessTimes(handle, out creation, out exit, out kernel, out user))
                {
                    continue;
                }

                var basic = new ProcessBasicInformation();
                int returned;
                var parent = NtQueryInformationProcess(handle, ProcessBasicInformationClass, ref basic, Marshal.SizeOf(typeof(ProcessBasicInformation)), out returned) == 0
                    ? (long)basic.InheritedFromUniqueProcessId.ToUInt64()
                    : -1;

                MemoryCounters memory;
                var workingSet = GetProcessMemoryInfo(handle, out memory, (uint)Marshal.SizeOf(typeof(MemoryCounters)))
                    ? memory.WorkingSetSize.ToUInt64()
                    : 0UL;

                if (!first)
                {
                    line.Append(',');
                }

                first = false;

                line.Append("{\"pid\":").Append(processId)
                    .Append(",\"ppid\":").Append(parent)
                    .Append(",\"created\":").Append(creation)
                    .Append(",\"cpuMs\":").Append((kernel + user) / 10000)
                    .Append(",\"ws\":").Append(workingSet);

                var key = processId.ToString(CultureInfo.InvariantCulture) + "@" + creation.ToString(CultureInfo.InvariantCulture);

                if (Described.Add(key))
                {
                    line.Append(",\"exe\":").Append(Quote(ImageOf(handle)))
                        .Append(",\"cmd\":").Append(Quote(CommandLineOf(handle)));
                }

                line.Append('}');
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        return line.Append("]}").ToString();
    }

    private static BasicAccounting Accounting(IntPtr job)
    {
        var accounting = new BasicAccounting();
        uint returned;
        QueryInformationJobObject(job, BasicAccountingInformation, ref accounting, (uint)Marshal.SizeOf(typeof(BasicAccounting)), out returned);
        return accounting;
    }

    private static List<int> Members(IntPtr job)
    {
        var members = new List<int>();
        const int Capacity = 4096;
        var size = 8 + (Capacity * IntPtr.Size);
        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            uint returned;

            if (!QueryInformationJobObject(job, BasicProcessIdList, buffer, (uint)size, out returned))
            {
                return members;
            }

            var count = Marshal.ReadInt32(buffer, 4);

            for (var index = 0; index < count; index++)
            {
                members.Add((int)Marshal.ReadIntPtr(buffer, 8 + (index * IntPtr.Size)).ToInt64());
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return members;
    }

    private static string ImageOf(IntPtr handle)
    {
        var name = new StringBuilder(1024);
        var size = name.Capacity;
        return QueryFullProcessImageNameW(handle, 0, name, ref size) ? name.ToString() : null;
    }

    private static string CommandLineOf(IntPtr handle)
    {
        int needed;
        var status = NtQueryInformationProcess(handle, ProcessCommandLineInformationClass, IntPtr.Zero, 0, out needed);

        if (status != StatusInfoLengthMismatch || needed <= 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal(needed);

        try
        {
            if (NtQueryInformationProcess(handle, ProcessCommandLineInformationClass, buffer, needed, out needed) != 0)
            {
                return null;
            }

            // A UNICODE_STRING: its length in bytes, then the pointer to its characters.
            var length = (ushort)Marshal.ReadInt16(buffer, 0);
            var characters = Marshal.ReadIntPtr(buffer, IntPtr.Size);
            return length == 0 ? string.Empty : Marshal.PtrToStringUni(characters, length / 2);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string Quote(string text)
    {
        if (text == null)
        {
            return "null";
        }

        var quoted = new StringBuilder(text.Length + 2).Append('"');

        foreach (var character in text)
        {
            if (character == '\\')
            {
                quoted.Append("\\\\");
            }
            else if (character == '"')
            {
                quoted.Append("\\\"");
            }
            else if (character < ' ')
            {
                quoted.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
                quoted.Append(character);
            }
        }

        return quoted.Append('"').ToString();
    }
}
