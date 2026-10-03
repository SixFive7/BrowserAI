// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Launches the WebView2 probe on a private desktop that is never switched to
// (the handle is opened WITHOUT DESKTOP_SWITCHDESKTOP), inside a job object
// with KILL_ON_JOB_CLOSE, so whatever it starts ends with the job and nothing
// can reach the user's screen. Processes are identified by job membership only.
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

internal static class Wv2Launch
{
    public static int Run(string root, string build, string userData, int runs)
    {
        var exe = Path.Combine(root, "out", build, "wv2probe.exe");
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"# {build} measured {DateTimeOffset.UtcNow:O}; exe {new FileInfo(exe).Length} B; user data {userData}");

        for (var run = 0; run < runs; run++)
        {
            var resultFile = Path.Combine(root, "run", $"{build}-{run}.txt");
            File.Delete(resultFile);

            var name = $"BrowserAI-trackb-{Guid.NewGuid():N}";
            // READOBJECTS | CREATEWINDOW | CREATEMENU | HOOKCONTROL | ENUMERATE | WRITEOBJECTS | READ_CONTROL; no SWITCHDESKTOP.
            const uint access = 0x0001 | 0x0002 | 0x0004 | 0x0008 | 0x0040 | 0x0080 | 0x00020000;
            var desk = CreateDesktopW(name, 0, 0, 0, access, 0);
            if (desk == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateDesktopW");
            }

            var job = CreateJobObjectW(0, null);
            var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            limits.BasicLimitInformation.LimitFlags = 0x2000; // KILL_ON_JOB_CLOSE
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject");
            }

            var clock = Stopwatch.StartNew();
            var pi = Start(exe, $"\"{exe}\" \"{resultFile}\" \"{userData}\"", name, job);
            var sampled = string.Empty;
            string? seen = null;

            while (clock.Elapsed < TimeSpan.FromSeconds(40))
            {
                if (File.Exists(resultFile))
                {
                    try
                    {
                        seen = File.ReadAllText(resultFile);
                    }
                    catch (IOException)
                    {
                        seen = null;
                    }

                    if (seen is not null && seen.Contains("\tmessage ", StringComparison.Ordinal))
                    {
                        break;
                    }
                }

                if (WaitForSingleObject(pi.hProcess, 0) == 0)
                {
                    break;
                }

                Thread.Sleep(5);
            }

            var readyAt = clock.Elapsed.TotalMilliseconds;

            // Sample while the probe lingers: every process in OUR job, by pid.
            Thread.Sleep(1000);
            var pids = JobPids(job);
            long totalPrivate = 0;
            long totalWs = 0;
            var names = new List<string>();
            foreach (var pid in pids)
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    totalPrivate += p.PrivateMemorySize64;
                    totalWs += p.WorkingSet64;
                    names.Add(p.ProcessName);
                }
                catch (ArgumentException)
                {
                }
            }

            var peak = PeakJobMemory(job);
            sampled = $"processes_in_job={pids.Count} [{string.Join(",", names.GroupBy(n => n).Select(g => $"{g.Key}x{g.Count()}"))}] private_MB={totalPrivate / 1048576.0:F1} working_set_MB={totalWs / 1048576.0:F1} peak_job_commit_MB={peak / 1048576.0:F1}";

            var exited = WaitForSingleObject(pi.hProcess, 15000) == 0;
            _ = GetExitCodeProcess(pi.hProcess, out var code);
            var exitAt = clock.Elapsed.TotalMilliseconds;
            var left = JobPids(job).Count;

            // Closing the job ends anything still in it; then the desktop goes.
            _ = CloseHandle(pi.hThread);
            _ = CloseHandle(pi.hProcess);
            _ = CloseHandle(job);
            Thread.Sleep(200);
            _ = CloseDesktop(desk);

            var text = File.Exists(resultFile) ? File.ReadAllText(resultFile).Trim() : "(no result file)";
            report.AppendLine(CultureInfo.InvariantCulture, $"## run {run}: launch-to-ready {readyAt:F1} ms; exited={exited} code={code} at {exitAt:F1} ms; processes still in job at exit {left}");
            report.AppendLine("   " + sampled);
            foreach (var line in text.Split('\n'))
            {
                report.AppendLine("   probe: " + line.TrimEnd());
            }
        }

        Console.Write(report.ToString());
        return 0;
    }

    private static PROCESS_INFORMATION Start(string exe, string commandLine, string desktop, nint job)
    {
        var desktopName = Marshal.StringToHGlobalUni(desktop);
        try
        {
            var si = new STARTUPINFOW { cb = Marshal.SizeOf<STARTUPINFOW>(), lpDesktop = desktopName };
            const uint flags = 0x00000004 /* CREATE_SUSPENDED */ | 0x00000400 /* CREATE_UNICODE_ENVIRONMENT */;
            if (!CreateProcessW(exe, new StringBuilder(commandLine), 0, 0, false, flags, 0, Path.GetDirectoryName(exe), ref si, out var pi))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessW");
            }

            if (!AssignProcessToJobObject(job, pi.hProcess))
            {
                var error = Marshal.GetLastWin32Error();
                _ = TerminateProcess(pi.hProcess, 1);
                throw new Win32Exception(error, "AssignProcessToJobObject");
            }

            _ = ResumeThread(pi.hThread);
            return pi;
        }
        finally
        {
            Marshal.FreeHGlobal(desktopName);
        }
    }

    private static List<int> JobPids(nint job)
    {
        var size = 8 + (8 * 1024);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryInformationJobObject(job, 3, buffer, (uint)size, out _))
            {
                return [];
            }

            var count = Marshal.ReadInt32(buffer, 4);
            var list = new List<int>();
            for (var i = 0; i < count; i++)
            {
                list.Add((int)Marshal.ReadInt64(buffer, 8 + (i * 8)));
            }

            return list;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static long PeakJobMemory(nint job)
    {
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        return QueryInformationJobObject(job, 9, ref info, (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>(), out _)
            ? (long)info.PeakJobMemoryUsed
            : -1;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFOW
    {
        public int cb;
        public nint lpReserved;
        public nint lpDesktop;
        public nint lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateDesktopW(string name, nint device, nint devmode, uint flags, uint access, nint attributes);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(nint desktop);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObjectW(nint attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(nint job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(nint job, int infoClass, nint info, uint length, out uint returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(nint job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint length, out uint returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(nint job, nint process);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(string application, StringBuilder commandLine, nint processAttributes, nint threadAttributes, bool inherit, uint flags, nint environment, string? directory, ref STARTUPINFOW startup, out PROCESS_INFORMATION info);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(nint thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(nint process, uint code);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(nint process, out uint code);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);
}
