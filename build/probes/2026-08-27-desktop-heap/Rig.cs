// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Probe rig for QUESTIONS.md section 8 -- the silent Chromium death.
// Not product code: nothing builds this, and nothing in the suite runs it.
// House rules honoured: nothing is ever killed by image name, every launch
// sets CreateNoWindow (0x08000000), and every STARTUPINFO field carries the flag
// that makes CreateProcessW read it.
//
// Corrected 2026-09-16 (previously "Lives in .work/ and is invisible to the
// repository's tree-as-text scans (.work is gitignored and RepositoryLayout
// prunes it)"). It was moved out of .work/ into build/probes/ when the scratch
// directory was wiped, so it IS read by every scan over
// RepositoryLayout.SourceAndScriptFiles now -- which is the reason the
// sentence above says "honoured" rather than "honoured anyway".
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HeapRig
{
    public static class Native
    {
        public const int UOI_NAME = 2;
        public const int UOI_HEAPSIZE = 5;
        public const int UOI_IO = 6;

        public const uint DESKTOP_ALL = 0x000F01FF;
        public const uint WINSTA_ALL = 0x000F037F;

        public const uint GENERIC_ALL = 0x10000000;

        public const int STARTF_USESTDHANDLES = 0x00000100;
        // Renamed 2026-09-16 from the Win32 spelling. The name is load-bearing
        // rather than stylistic: HouseRuleTests.EveryProcessLaunchInTheTree-
        // SuppressesTheConsoleWindow scans with ONE needle so that it covers the
        // managed property and the native flag alike, and
        // src/BrowserAI/Interop/JobLauncher.cs names the same constant the same way
        // for the same reason. Same value, same two launch sites, no behaviour
        // change -- and the rig now passes the scan it always claimed to satisfy.
        public const uint CreateNoWindow = 0x08000000;
        public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        public const uint CREATE_SUSPENDED = 0x00000004;

        public const uint GR_GDIOBJECTS = 0;
        public const uint GR_USEROBJECTS = 1;

        public static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        public const uint WS_POPUP = 0x80000000;
        public const uint WS_CHILD = 0x40000000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateDesktopW(string desktop, string device, IntPtr devmode, int flags, uint access, IntPtr sa);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateDesktopExW(string desktop, string device, IntPtr devmode, int flags, uint access, IntPtr sa, uint heapKb, IntPtr reserved);

        /// <summary>
        /// Create a desktop, optionally with a heap size of our own choosing.
        /// <paramref name="device"/> is always NULL, and it has to be a real
        /// NULL: PowerShell hands a <c>$null</c> string across as an empty one,
        /// which CreateDesktop reads as a display device named "" and refuses
        /// with ERROR_INVALID_PARAMETER. Cost two runs to find.
        /// </summary>
        public static IntPtr MakeDesktop(string name, uint access, uint heapKb)
        {
            return heapKb == 0
                ? CreateDesktopW(name, null, IntPtr.Zero, 0, access, IntPtr.Zero)
                : CreateDesktopExW(name, null, IntPtr.Zero, 0, access, IntPtr.Zero, heapKb, IntPtr.Zero);
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseDesktop(IntPtr desktop);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr OpenDesktopW(string desktop, int flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowStationW(string station, int flags, uint access, IntPtr sa);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseWindowStation(IntPtr station);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetProcessWindowStation(IntPtr station);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetProcessWindowStation();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetThreadDesktop(IntPtr desktop);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetThreadDesktop(uint threadId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetUserObjectInformationW(IntPtr obj, int index, IntPtr info, uint length, out uint needed);

        // CharSet.Unicode is load-bearing: the default ANSI marshalling reads a
        // UTF-16 "WinSta0" as the single character "W".
        [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public delegate bool EnumNamesProc([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr param);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindowStationsW(EnumNamesProc callback, IntPtr param);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDesktopsW(IntPtr station, EnumNamesProc callback, IntPtr param);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowExW(
            uint exStyle, string className, string windowName, uint style,
            int x, int y, int width, int height,
            IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(IntPtr window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindowExW(IntPtr parent, IntPtr childAfter, string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetWindowTextW(IntPtr window, [Out] char[] text, int max);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetGuiResources(IntPtr process, uint flags);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [StructLayout(LayoutKind.Sequential)]
        public struct PERFORMANCE_INFORMATION
        {
            public int cb;
            public IntPtr CommitTotal;
            public IntPtr CommitLimit;
            public IntPtr CommitPeak;
            public IntPtr PhysicalTotal;
            public IntPtr PhysicalAvailable;
            public IntPtr SystemCache;
            public IntPtr KernelTotal;
            public IntPtr KernelPaged;
            public IntPtr KernelNonpaged;
            public IntPtr PageSize;
            public int HandleCount;
            public int ProcessCount;
            public int ThreadCount;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION info, int cb);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public int cb;
            public IntPtr lpReserved;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpDesktop;
            public IntPtr lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcessW(
            string applicationName,
            System.Text.StringBuilder commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref STARTUPINFO startupInfo,
            out PROCESS_INFORMATION processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateProcess(IntPtr process, uint exitCode);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateJobObjectW(IntPtr sa, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
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
        public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        public const int JobObjectExtendedLimitInformation = 9;
        public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    }

    /// <summary>
    /// A process launched onto a named desktop whose two output pipes are
    /// drained on their own threads, so the caller may poll for liveness and
    /// still read both streams to end of file afterwards.
    /// </summary>
    public sealed class CapturedProcess : IDisposable
    {
        private IntPtr process;
        private IntPtr thread;
        private AnonymousPipeServerStream stdout;
        private AnonymousPipeServerStream stderr;
        private AnonymousPipeServerStream stdin;
        private Task<string> outText;
        private Task<string> errText;
        private System.Diagnostics.Stopwatch clock;

        public int Pid;
        public bool Created;
        public int CreateError;
        public int ExitCode = -1;
        public double ElapsedMs;
        public string StdOut = "";
        public string StdErr = "";
        public bool Drained;

        public static CapturedProcess Start(
            string executable,
            string commandLine,
            string workingDirectory,
            string desktop,
            IntPtr job)
        {
            var launched = new CapturedProcess();
            launched.stdout = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            launched.stderr = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            launched.stdin = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);

            var si = new Native.STARTUPINFO();
            si.cb = Marshal.SizeOf(typeof(Native.STARTUPINFO));

            // NULL and "" are different instructions: NULL inherits the caller's
            // desktop, an empty string tells Windows to work one out for itself.
            si.lpDesktop = string.IsNullOrEmpty(desktop) ? null : desktop;

            // The three handle fields below are read only because of this flag,
            // and the flag promises Windows all three. Both halves, together.
            si.dwFlags = Native.STARTF_USESTDHANDLES;
            si.hStdInput = launched.stdin.ClientSafePipeHandle.DangerousGetHandle();
            si.hStdOutput = launched.stdout.ClientSafePipeHandle.DangerousGetHandle();
            si.hStdError = launched.stderr.ClientSafePipeHandle.DangerousGetHandle();

            Native.PROCESS_INFORMATION pi;
            var command = new System.Text.StringBuilder(commandLine);

            launched.clock = System.Diagnostics.Stopwatch.StartNew();

            var created = Native.CreateProcessW(
                executable,
                command,
                IntPtr.Zero,
                IntPtr.Zero,
                true,
                Native.CreateNoWindow | Native.CREATE_UNICODE_ENVIRONMENT | Native.CREATE_SUSPENDED,
                IntPtr.Zero,
                workingDirectory,
                ref si,
                out pi);

            launched.Created = created;

            if (!created)
            {
                launched.CreateError = Marshal.GetLastWin32Error();
                launched.stdout.Dispose();
                launched.stderr.Dispose();
                launched.stdin.Dispose();
                return launched;
            }

            launched.Pid = pi.dwProcessId;
            launched.process = pi.hProcess;
            launched.thread = pi.hThread;

            if (job != IntPtr.Zero)
            {
                Native.AssignProcessToJobObject(job, pi.hProcess);
            }

            ResumeThread(pi.hThread);

            launched.stdout.DisposeLocalCopyOfClientHandle();
            launched.stderr.DisposeLocalCopyOfClientHandle();
            launched.stdin.DisposeLocalCopyOfClientHandle();

            var outPipe = launched.stdout;
            var errPipe = launched.stderr;
            launched.outText = Task.Run(() => new StreamReader(outPipe).ReadToEnd());
            launched.errText = Task.Run(() => new StreamReader(errPipe).ReadToEnd());

            return launched;
        }

        /// <summary>Has it gone, right now?</summary>
        public bool HasExited()
        {
            if (process == IntPtr.Zero)
            {
                return true;
            }

            return Native.WaitForSingleObject(process, 0) == 0;
        }

        /// <summary>Wait up to this long; true when it exited on its own.</summary>
        public bool WaitFor(int milliseconds)
        {
            if (process == IntPtr.Zero)
            {
                return true;
            }

            return Native.WaitForSingleObject(process, (uint)milliseconds) == 0;
        }

        /// <summary>Terminate by handle, never by name.</summary>
        public void Kill()
        {
            if (process != IntPtr.Zero && !HasExited())
            {
                Native.TerminateProcess(process, 0xDEAD);
            }
        }

        /// <summary>Read the exit code and both streams to end of file.</summary>
        public void Finish(int drainMilliseconds)
        {
            if (process == IntPtr.Zero)
            {
                return;
            }

            clock.Stop();
            ElapsedMs = clock.Elapsed.TotalMilliseconds;

            uint code;
            if (Native.GetExitCodeProcess(process, out code))
            {
                ExitCode = unchecked((int)code);
            }

            Drained = Task.WaitAll(new Task[] { outText, errText }, drainMilliseconds);
            StdOut = outText.IsCompleted ? outText.Result : "<not drained>";
            StdErr = errText.IsCompleted ? errText.Result : "<not drained>";
        }

        public void Dispose()
        {
            if (thread != IntPtr.Zero)
            {
                Native.CloseHandle(thread);
                thread = IntPtr.Zero;
            }

            if (process != IntPtr.Zero)
            {
                Native.CloseHandle(process);
                process = IntPtr.Zero;
            }

            if (stdout != null) { stdout.Dispose(); }
            if (stderr != null) { stderr.Dispose(); }
            if (stdin != null) { stdin.Dispose(); }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr thread);
    }

    public static class Rig
    {
        /// <summary>The desktop heap size Windows gave this desktop, in KB.</summary>
        public static uint HeapSizeKb(IntPtr desktop)
        {
            var buffer = Marshal.AllocHGlobal(4);
            try
            {
                uint needed;
                if (!Native.GetUserObjectInformationW(desktop, Native.UOI_HEAPSIZE, buffer, 4, out needed))
                {
                    throw new InvalidOperationException("UOI_HEAPSIZE failed: " + Marshal.GetLastWin32Error());
                }

                return (uint)Marshal.ReadInt32(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public static string NameOf(IntPtr obj)
        {
            uint needed;
            Native.GetUserObjectInformationW(obj, Native.UOI_NAME, IntPtr.Zero, 0, out needed);
            var buffer = Marshal.AllocHGlobal((int)Math.Max(needed, 2));
            try
            {
                if (!Native.GetUserObjectInformationW(obj, Native.UOI_NAME, buffer, Math.Max(needed, 2), out needed))
                {
                    return "<unreadable:" + Marshal.GetLastWin32Error() + ">";
                }

                return Marshal.PtrToStringUni(buffer) ?? "";
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public static List<string> WindowStations()
        {
            var names = new List<string>();
            Native.EnumWindowStationsW((n, p) => { names.Add(n); return true; }, IntPtr.Zero);
            return names;
        }

        public static List<string> Desktops(IntPtr station)
        {
            var names = new List<string>();
            Native.EnumDesktopsW(station, (n, p) => { names.Add(n); return true; }, IntPtr.Zero);
            return names;
        }

        public static string PerformanceLine()
        {
            Native.PERFORMANCE_INFORMATION info;
            info.cb = Marshal.SizeOf(typeof(Native.PERFORMANCE_INFORMATION));
            if (!Native.GetPerformanceInfo(out info, Marshal.SizeOf(typeof(Native.PERFORMANCE_INFORMATION))))
            {
                return "GetPerformanceInfo failed: " + Marshal.GetLastWin32Error();
            }

            var page = (long)info.PageSize;
            var mib = 1024L * 1024L;
            return string.Format(
                "processes={0} threads={1} handles={2} physicalFreeMiB={3} physicalTotalMiB={4} commitMiB={5} commitLimitMiB={6}",
                info.ProcessCount,
                info.ThreadCount,
                info.HandleCount,
                (long)info.PhysicalAvailable * page / mib,
                (long)info.PhysicalTotal * page / mib,
                (long)info.CommitTotal * page / mib,
                (long)info.CommitLimit * page / mib);
        }

        /// <summary>Start a process on a desktop and leave it running.</summary>
        public static int Start(string executable, string commandLine, string workingDirectory, string desktop, IntPtr job, out int createError)
        {
            var si = new Native.STARTUPINFO();
            si.cb = Marshal.SizeOf(typeof(Native.STARTUPINFO));
            si.lpDesktop = string.IsNullOrEmpty(desktop) ? null : desktop;

            Native.PROCESS_INFORMATION pi;
            var command = new System.Text.StringBuilder(commandLine);

            var created = Native.CreateProcessW(
                executable,
                command,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                Native.CreateNoWindow | Native.CREATE_UNICODE_ENVIRONMENT | Native.CREATE_SUSPENDED,
                IntPtr.Zero,
                workingDirectory,
                ref si,
                out pi);

            if (!created)
            {
                createError = Marshal.GetLastWin32Error();
                return 0;
            }

            createError = 0;

            if (job != IntPtr.Zero)
            {
                Native.AssignProcessToJobObject(job, pi.hProcess);
            }

            ResumeThread(pi.hThread);
            Native.CloseHandle(pi.hThread);
            Native.CloseHandle(pi.hProcess);
            return pi.dwProcessId;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr thread);

        public static IntPtr CreateKillOnCloseJob()
        {
            var job = Native.CreateJobObjectW(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
            {
                throw new InvalidOperationException("CreateJobObject failed: " + Marshal.GetLastWin32Error());
            }

            var limits = new Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            limits.BasicLimitInformation.LimitFlags = Native.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

            var size = Marshal.SizeOf(typeof(Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(limits, buffer, false);
                if (!Native.SetInformationJobObject(job, Native.JobObjectExtendedLimitInformation, buffer, (uint)size))
                {
                    throw new InvalidOperationException("SetInformationJobObject failed: " + Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return job;
        }

        /// <summary>
        /// Fill the current thread's desktop heap with message-only windows and
        /// report where it stopped and why.
        /// </summary>
        public static string Consume(int titleChars, int cap, List<IntPtr> keep)
        {
            var title = titleChars > 0 ? new string('x', titleChars) : "h";
            var created = 0;
            var lastError = 0;

            while (created < cap)
            {
                var window = Native.CreateWindowExW(
                    0,
                    "STATIC",
                    title,
                    Native.WS_CHILD,
                    0, 0, 0, 0,
                    Native.HWND_MESSAGE,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);

                if (window == IntPtr.Zero)
                {
                    lastError = Marshal.GetLastWin32Error();
                    break;
                }

                keep.Add(window);
                created++;
            }

            var user = Native.GetGuiResources(Native.GetCurrentProcess(), Native.GR_USEROBJECTS);
            return string.Format("created={0} lastError={1} userObjects={2} cap={3}", created, lastError, user, cap);
        }

        /// <summary>
        /// Every message-only window of one class, as the sweep's own walk sees
        /// them. Only the first few are described: the filler's windows carry
        /// 2,048-character titles and printing 4,637 of them wrote a 12 MB line.
        /// </summary>
        public static string ProbeMessageWindows(string className)
        {
            var found = new List<string>();
            var count = 0;
            var previous = IntPtr.Zero;

            while (true)
            {
                var window = Native.FindWindowExW(Native.HWND_MESSAGE, previous, className, null);
                if (window == IntPtr.Zero)
                {
                    break;
                }

                count++;

                if (found.Count < 8)
                {
                    uint pid;
                    Native.GetWindowThreadProcessId(window, out pid);
                    var text = new char[512];
                    var length = Native.GetWindowTextW(window, text, text.Length);
                    var title = new string(text, 0, Math.Max(length, 0));
                    if (title.Length > 120)
                    {
                        title = title.Substring(0, 120) + "...";
                    }

                    found.Add(pid + ":" + title);
                }

                previous = window;
            }

            return count + "|" + string.Join(";", found);
        }
    }
}
