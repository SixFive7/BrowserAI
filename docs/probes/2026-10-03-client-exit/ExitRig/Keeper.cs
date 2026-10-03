// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ExitRig;

/// <summary>
/// Probes for option c: a process the server starts OUTSIDE its own kill-on-close job, either as a
/// direct child ("child") or orphaned through a launcher that exits at once ("orphan"), which
/// watches the server by handle and then pretends to need 5 s of graceful shutdown.
/// </summary>
static class Keeper
{
    /// <summary>Started by the server. mode = child | orphan.</summary>
    public static int StartFromServer(string mode, string logDir, Log log)
    {
        string exe = Environment.ProcessPath!;
        int me = Environment.ProcessId;
        if (mode == "child")
        {
            int pid = StartDetachedConsole(exe, $"\"{exe}\" keeper --logdir \"{logDir}\" --watch-pid {me} --how child", out string err);
            log.W("server", "KEEPER", $"mode=child pid={pid} {err}");
            return pid;
        }
        int lpid = StartDetachedConsole(exe, $"\"{exe}\" launcher --logdir \"{logDir}\" --watch-pid {me}", out string err2);
        log.W("server", "KEEPER", $"mode=orphan launcherPid={lpid} {err2}");
        return lpid;
    }

    static int StartDetachedConsole(string exe, string cmdLine, out string error)
    {
        error = "";
        var si = new Native.STARTUPINFOW { cb = Marshal.SizeOf<Native.STARTUPINFOW>() };
        var cmd = new StringBuilder(cmdLine, 32768);
        if (!Native.CreateProcessW(exe, cmd, IntPtr.Zero, IntPtr.Zero, false, Native.CREATE_NO_WINDOW | Native.CREATE_UNICODE_ENVIRONMENT, IntPtr.Zero, null, ref si, out var pi))
        {
            error = $"CreateProcessW err {Marshal.GetLastWin32Error()}";
            return -1;
        }
        Native.CloseHandle(pi.hThread);
        Native.CloseHandle(pi.hProcess);
        return pi.dwProcessId;
    }

    /// <summary>The launcher: starts the keeper and exits at once, so the keeper's parent pid points at a dead process.</summary>
    public static int Launcher(string[] a)
    {
        var o = Args.Parse(a, 1);
        var log = new Log(Path.Combine(o["logdir"], $"launcher-{Environment.ProcessId}.log"));
        string exe = Environment.ProcessPath!;
        int pid = StartDetachedConsole(exe, $"\"{exe}\" keeper --logdir \"{o["logdir"]}\" --watch-pid {o["watch-pid"]} --how orphan", out string err);
        log.W("launcher", "STARTED_KEEPER", $"pid={pid} {err} {Native.OwnJobInfo()}");
        return 0;
    }

    /// <summary>The keeper: holds a handle on the server, and when it goes, runs a 5 s "graceful shutdown".</summary>
    public static int Run(string[] a)
    {
        var o = Args.Parse(a, 1);
        var log = new Log(Path.Combine(o["logdir"], $"keeper-{Environment.ProcessId}.log"));
        int watch = int.Parse(o["watch-pid"], CultureInfo.InvariantCulture);
        log.W("keeper", "START", $"how={o.GetValueOrDefault("how", "?")} ppid={Native.ParentPid()} watch={watch} {Native.OwnJobInfo()}");
        using var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.SYNCHRONIZE, false, watch);
        if (h.IsInvalid) { log.W("keeper", "WATCH_FAILED", $"err={Marshal.GetLastWin32Error()}"); return 3; }
        Native.WaitForSingleObject(h, Native.INFINITE);
        Native.GetExitCodeProcess(h, out uint code);
        log.W("keeper", "SERVER_GONE", $"serverExitCode={code}");
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000) { Thread.Sleep(100); log.W("keeper", "SHUTTING_DOWN", $"ms={sw.ElapsedMilliseconds}"); }
        log.W("keeper", "DONE", "graceful work finished; exiting 88");
        return 88;
    }
}
