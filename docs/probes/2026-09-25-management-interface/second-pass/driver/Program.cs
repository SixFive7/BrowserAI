// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// b2 measurement driver. JIT-compiled: it is the instrument, not the subject.
//
//   driver desk <timeoutSec> <readyFile> <sampleDelaysMsCsv> <exe> [args...]
//
// Starts <exe> on a PRIVATE desktop that is never switched to (the handle is
// opened WITHOUT DESKTOP_SWITCHDESKTOP), inside a job object with
// KILL_ON_JOB_CLOSE, so whatever it starts ends with the job and nothing can
// reach the user's screen or take the foreground. Processes are identified by
// job membership (pids from the job object) only, never by image name.
//
// When <readyFile> appears, the job is sampled at each delay in
// <sampleDelaysMsCsv> (milliseconds after ready): process count, per-process
// image file name, private bytes, working set, and the job's peak commit.
// Samples are written to <readyFile>.mem.txt. On timeout the job is closed,
// which ends everything in it.
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

if (args.Length < 5 || args[0] != "desk")
{
    Console.Error.WriteLine("usage: driver desk <timeoutSec> <readyFile> <sampleDelaysMsCsv> <exe> [args...]");
    return 64;
}

var timeout = TimeSpan.FromSeconds(int.Parse(args[1], CultureInfo.InvariantCulture));
var readyFile = Path.GetFullPath(args[2]);
var delays = args[3].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(d => int.Parse(d, CultureInfo.InvariantCulture)).OrderBy(d => d).ToList();
var exe = Path.GetFullPath(args[4]);
var rest = args[5..];

File.Delete(readyFile);
File.Delete(readyFile + ".mem.txt");

var name = $"BrowserAI-b2-{Guid.NewGuid():N}";

// READOBJECTS | CREATEWINDOW | CREATEMENU | HOOKCONTROL | ENUMERATE | WRITEOBJECTS | READ_CONTROL; no SWITCHDESKTOP (0x0100).
const uint access = 0x0001 | 0x0002 | 0x0004 | 0x0008 | 0x0040 | 0x0080 | 0x00020000;
var desk = N.CreateDesktopW(name, 0, 0, 0, access, 0);
if (desk == 0)
{
    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateDesktopW");
}

var job = N.CreateJobObjectW(0, null);
var limits = default(N.JOBOBJECT_EXTENDED_LIMIT_INFORMATION);
limits.BasicLimitInformation.LimitFlags = 0x2000; // KILL_ON_JOB_CLOSE
if (!N.SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<N.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
{
    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject");
}

var commandLine = new StringBuilder();
Append(commandLine, exe);
foreach (var a in rest)
{
    Append(commandLine, a);
}

var clock = Stopwatch.StartNew();
var pi = Start(exe, commandLine.ToString(), name, job);
Console.WriteLine($"# {DateTimeOffset.UtcNow:O} desktop={name} pid={pi.dwProcessId} exe={exe}");

double readyAt = -1;
var mem = new StringBuilder();
var next = 0;
var exited = false;

while (clock.Elapsed < timeout)
{
    if (N.WaitForSingleObject(pi.hProcess, 0) == 0)
    {
        exited = true;
        break;
    }

    if (readyAt < 0 && File.Exists(readyFile))
    {
        readyAt = clock.Elapsed.TotalMilliseconds;
    }

    if (readyAt >= 0 && next < delays.Count && clock.Elapsed.TotalMilliseconds >= readyAt + delays[next])
    {
        mem.AppendLine(Sample(job, delays[next]));
        next++;
    }

    Thread.Sleep(readyAt < 0 ? 2 : 20);
}

_ = N.GetExitCodeProcess(pi.hProcess, out var code);
var endAt = clock.Elapsed.TotalMilliseconds;
var left = JobPids(job);
var peak = PeakJobMemory(job);

Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"launch_to_ready_ms={readyAt:F1} exited={exited} exit_code={code} end_ms={endAt:F1} still_in_job_at_end={left.Count} peak_job_commit_MB={peak / 1048576.0:F1}"));
if (mem.Length > 0)
{
    File.WriteAllText(readyFile + ".mem.txt", mem.ToString());
    Console.Write(mem.ToString());
}

// Closing the job ends anything still in it; then the desktop goes.
_ = N.CloseHandle(pi.hThread);
_ = N.CloseHandle(pi.hProcess);
_ = N.CloseHandle(job);
Thread.Sleep(300);
var closedDesktop = N.CloseDesktop(desk);
Console.WriteLine($"job closed; desktop closed={closedDesktop}");
return exited ? 0 : 3;

static void Append(StringBuilder b, string argument)
{
    if (b.Length != 0)
    {
        b.Append(' ');
    }

    if (argument.Length != 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
    {
        b.Append(argument);
        return;
    }

    b.Append('"');
    for (var i = 0; i < argument.Length; i++)
    {
        var slashes = 0;
        while (i < argument.Length && argument[i] == '\\')
        {
            i++;
            slashes++;
        }

        if (i == argument.Length)
        {
            b.Append('\\', slashes * 2);
            break;
        }

        if (argument[i] == '"')
        {
            b.Append('\\', (slashes * 2) + 1).Append('"');
        }
        else
        {
            b.Append('\\', slashes).Append(argument[i]);
        }
    }

    b.Append('"');
}

static N.PROCESS_INFORMATION Start(string exe, string commandLine, string desktop, nint job)
{
    var desktopName = Marshal.StringToHGlobalUni(desktop);
    try
    {
        // STARTF_USESHOWWINDOW with SW_SHOWNOACTIVATE is irrelevant on a desktop nobody sees; left default.
        var si = new N.STARTUPINFOW { cb = Marshal.SizeOf<N.STARTUPINFOW>(), lpDesktop = desktopName };
        const uint flags = 0x00000004 /* CREATE_SUSPENDED */ | 0x00000400 /* CREATE_UNICODE_ENVIRONMENT */ | 0x08000000 /* CREATE_NO_WINDOW */;
        if (!N.CreateProcessW(exe, new StringBuilder(commandLine), 0, 0, false, flags, 0, Path.GetDirectoryName(exe), ref si, out var pi))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessW");
        }

        if (!N.AssignProcessToJobObject(job, pi.hProcess))
        {
            var error = Marshal.GetLastWin32Error();
            _ = N.TerminateProcess(pi.hProcess, 1);
            throw new Win32Exception(error, "AssignProcessToJobObject");
        }

        _ = N.ResumeThread(pi.hThread);
        return pi;
    }
    finally
    {
        Marshal.FreeHGlobal(desktopName);
    }
}

static string Sample(nint job, int delay)
{
    var pids = JobPids(job);
    long totalPrivate = 0;
    long totalWs = 0;
    var rows = new List<string>();
    foreach (var pid in pids)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            totalPrivate += p.PrivateMemorySize64;
            totalWs += p.WorkingSet64;
            string image;
            try
            {
                image = Path.GetFileName(p.MainModule?.FileName ?? p.ProcessName);
            }
            catch (Exception)
            {
                image = p.ProcessName;
            }

            rows.Add(string.Create(CultureInfo.InvariantCulture, $"{image}:{p.PrivateMemorySize64 / 1048576.0:F1}/{p.WorkingSet64 / 1048576.0:F1}"));
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    var peak = PeakJobMemory(job);
    return string.Create(CultureInfo.InvariantCulture, $"sample_at_ready_plus_ms={delay} processes_in_job={pids.Count} private_MB={totalPrivate / 1048576.0:F1} working_set_MB={totalWs / 1048576.0:F1} peak_job_commit_MB={peak / 1048576.0:F1} per_process_private/ws_MB=[{string.Join(" ", rows)}]");
}

static List<int> JobPids(nint job)
{
    var size = 8 + (8 * 4096);
    var buffer = Marshal.AllocHGlobal(size);
    try
    {
        if (!N.QueryInformationJobObject(job, 3, buffer, (uint)size, out _))
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

static long PeakJobMemory(nint job)
{
    var info = default(N.JOBOBJECT_EXTENDED_LIMIT_INFORMATION);
    return N.QueryInformationJobObject(job, 9, ref info, (uint)Marshal.SizeOf<N.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>(), out _)
        ? (long)info.PeakJobMemoryUsed
        : -1;
}

internal static class N
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFOW
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
    public struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
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
    public struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateDesktopW(string name, nint device, nint devmode, uint flags, uint access, nint attributes);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseDesktop(nint desktop);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateJobObjectW(nint attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetInformationJobObject(nint job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool QueryInformationJobObject(nint job, int infoClass, nint info, uint length, out uint returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool QueryInformationJobObject(nint job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint length, out uint returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AssignProcessToJobObject(nint job, nint process);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CreateProcessW(string application, StringBuilder commandLine, nint processAttributes, nint threadAttributes, bool inherit, uint flags, nint environment, string? directory, ref STARTUPINFOW startup, out PROCESS_INFORMATION info);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint ResumeThread(nint thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateProcess(nint process, uint code);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetExitCodeProcess(nint process, out uint code);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(nint handle);
}
