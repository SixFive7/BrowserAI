// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Probe F: "An .exe and never a .cmd shim. A shim cannot be started without cmd.exe."
//  1. Can CreateProcessW start a .cmd DIRECTLY?  If it "can", WHAT process runs?
//  2. If a shell does get interposed, what does it do to arguments?
// POSITIVE CONTROL throughout: the identical call against a real .exe.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

var dir = Path.Combine(AppContext.BaseDirectory, "rig");
Directory.CreateDirectory(dir);

// The shim reports the command line cmd.exe itself received (%CMDCMDLINE%, a cmd
// built-in), and every argument as it arrived.
var cmd = Path.Combine(dir, "shim.cmd");
File.WriteAllText(cmd,
    "@echo off\r\n" +
    "echo SHIM-RAN\r\n" +
    "echo   CMDCMDLINE=%CMDCMDLINE%\r\n" +
    "echo   ARGS=%*\r\n" +
    "echo   ARG1=[%~1]\r\n");

// The control: a real .exe that prints its argv, built from this same probe.
var argvExe = Environment.ProcessPath!;

Console.WriteLine($"rig: {dir}");
Console.WriteLine($"probe exe: {argvExe}");
Console.WriteLine();

if (args.Length > 0 && args[0] == "--argv")
{
    Console.WriteLine("ARGV-EXE-RAN");
    Console.WriteLine($"  GetCommandLine={Environment.CommandLine}");
    for (var i = 1; i < args.Length; i++) Console.WriteLine($"  ARG{i}=[{args[i]}]");
    return 0;
}

var hostile = new[] { "%USERNAME%", "a & b", @"C:\Program Files\x y\claude.exe" };

Section("1a. CreateProcessW P/Invoke, lpApplicationName = the .cmd  (what the Win32 doc forbids)");
CreateProcessDirect(cmd, null);

Section("1b. CreateProcessW P/Invoke, lpApplicationName = NULL, command line = \"<the .cmd>\"");
CreateProcessDirect(null, $"\"{cmd}\"");

Section("1c. CONTROL: CreateProcessW P/Invoke against a real .exe");
CreateProcessDirect(argvExe, $"\"{argvExe}\" --argv control");

Section("2a. Process.Start UseShellExecute=false against the .cmd -- and WHICH image ran");
StartAndIdentify(cmd, ["plain"]);

Section("2b. CONTROL: Process.Start UseShellExecute=false against the real .exe");
StartAndIdentify(argvExe, ["--argv", "control"]);

Section("3a. HOSTILE ARGUMENTS through the .cmd shim");
StartAndIdentify(cmd, hostile);

Section("3b. CONTROL: the identical hostile arguments straight to the .exe");
StartAndIdentify(argvExe, ["--argv", .. hostile]);

return 0;

static void Section(string s) { Console.WriteLine(); Console.WriteLine("================ " + s); }

static void StartAndIdentify(string file, string[] argv)
{
    var psi = new ProcessStartInfo(file)
    {
        UseShellExecute = false, RedirectStandardOutput = true,
        RedirectStandardError = true, CreateNoWindow = true,
    };
    foreach (var a in argv) psi.ArgumentList.Add(a);
    Console.WriteLine($"  FileName={file}");
    Console.WriteLine($"  ArgumentList=[{string.Join(" | ", argv)}]");
    try
    {
        using var p = Process.Start(psi)!;
        string image;
        try { image = p.MainModule?.FileName ?? "<null>"; } catch (Exception e) { image = $"<unreadable: {e.GetType().Name}>"; }
        var o = p.StandardOutput.ReadToEnd();
        var e2 = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Console.WriteLine($"  RESULT: started. pid={p.Id} exit={p.ExitCode}");
        Console.WriteLine($"  WHICH IMAGE ACTUALLY RAN: {image}");
        foreach (var line in o.Split('\n', StringSplitOptions.RemoveEmptyEntries)) Console.WriteLine("    out| " + line.TrimEnd());
        foreach (var line in e2.Split('\n', StringSplitOptions.RemoveEmptyEntries)) Console.WriteLine("    err| " + line.TrimEnd());
    }
    catch (Win32Exception w) { Console.WriteLine($"  RESULT: REFUSED. Win32 error {w.NativeErrorCode}: {w.Message}"); }
    catch (Exception x) { Console.WriteLine($"  RESULT: REFUSED. {x.GetType().Name}: {x.Message}"); }
}

static void CreateProcessDirect(string? app, string? commandLine)
{
    Console.WriteLine($"  lpApplicationName={(app is null ? "NULL" : app)}");
    Console.WriteLine($"  lpCommandLine={(commandLine is null ? "NULL" : commandLine)}");
    var si = new STARTUPINFOW { cb = (uint)Marshal.SizeOf<STARTUPINFOW>() };
    var pi = default(PROCESS_INFORMATION);
    // CreateProcessW writes into the command line buffer, so it must be writable.
    var buffer = commandLine is null ? null : (commandLine + "\0").ToCharArray();
    unsafe
    {
        fixed (char* pCmd = buffer)
        {
            var ok = CreateProcessW(app, pCmd, IntPtr.Zero, IntPtr.Zero, false,
                                    0x08000000 /*CREATE_NO_WINDOW*/, IntPtr.Zero, null, ref si, out pi);
            if (!ok)
            {
                var err = Marshal.GetLastWin32Error();
                Console.WriteLine($"  RESULT: CreateProcessW FAILED. GetLastError={err} ({new Win32Exception(err).Message})");
                return;
            }
        }
    }
    try
    {
        using var p = Process.GetProcessById((int)pi.dwProcessId);
        string image;
        try { image = p.MainModule?.FileName ?? "<null>"; } catch (Exception e) { image = $"<unreadable: {e.GetType().Name}>"; }
        Console.WriteLine($"  RESULT: CreateProcessW SUCCEEDED. pid={pi.dwProcessId}");
        Console.WriteLine($"  WHICH IMAGE ACTUALLY RAN: {image}");
        p.WaitForExit(10000);
    }
    catch (Exception e) { Console.WriteLine($"  RESULT: CreateProcessW SUCCEEDED, pid={pi.dwProcessId}, but it had already exited ({e.GetType().Name})"); }
    finally { CloseHandle(pi.hProcess); CloseHandle(pi.hThread); }
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct STARTUPINFOW
{
    public uint cb; public IntPtr lpReserved, lpDesktop, lpTitle;
    public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
    public ushort wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
}

[StructLayout(LayoutKind.Sequential)]
struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public uint dwProcessId, dwThreadId; }

static partial class Native { }

partial class Program
{
    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool CreateProcessW(
        string? lpApplicationName, char* lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment,
        string? lpCurrentDirectory, ref STARTUPINFOW lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr h);
}
