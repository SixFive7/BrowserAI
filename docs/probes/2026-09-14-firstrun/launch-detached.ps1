# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Launches a process with DETACHED_PROCESS so it has NO console of its own --
# which is what Explorer does when a user double-clicks Setup.exe. Launching it
# from this terminal instead would let the installer's console-subsystem
# grandchild join THIS console, and the "terminal on the user's screen" hazard
# would be invisible. The environment block is inherited (lpEnvironment = NULL),
# so CLAUDE_CONFIG_DIR set in this session reaches Setup.exe and every hook.
param(
  [Parameter(Mandatory=$true)][string]$Exe,
  [Parameter(Mandatory=$true)][string]$Arguments,
  [string]$WorkingDirectory = $null
)

$ErrorActionPreference = 'Stop'

$code = @'
using System;
using System.Runtime.InteropServices;

namespace Probe
{
    public static class Detached
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION { public IntPtr hProcess; public IntPtr hThread; public uint dwProcessId; public uint dwThreadId; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
            public int dwX; public int dwY; public int dwXSize; public int dwYSize;
            public int dwXCountChars; public int dwYCountChars; public int dwFillAttribute;
            public int dwFlags; public short wShowWindow; public short cbReserved2;
            public IntPtr lpReserved2; public IntPtr hStdInput; public IntPtr hStdOutput; public IntPtr hStdError;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(
            string lpApplicationName, string lpCommandLine,
            IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
            bool bInheritHandles, uint dwCreationFlags,
            IntPtr lpEnvironment, string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        private const uint DETACHED_PROCESS = 0x00000008;

        // No dwFlags is set, so no STARTUPINFO field below cb is read by Windows:
        // every field stays at its default and none of them is a setting in
        // disguise.
        public static uint Start(string exe, string commandLine, string workingDirectory)
        {
            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
            PROCESS_INFORMATION pi;
            if (!CreateProcessW(exe, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                                DETACHED_PROCESS, IntPtr.Zero, workingDirectory, ref si, out pi))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var id = pi.dwProcessId;
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            return id;
        }
    }
}
'@

Add-Type -TypeDefinition $code -Language CSharp

$cmdline = '"' + $Exe + '" ' + $Arguments
if (-not $WorkingDirectory) { $WorkingDirectory = (Split-Path -Parent $Exe) }
$pidOut = [Probe.Detached]::Start($Exe, $cmdline, $WorkingDirectory)
Write-Output "LAUNCHED_PID=$pidOut"
Write-Output "COMMANDLINE=$cmdline"
Write-Output "CLAUDE_CONFIG_DIR=$env:CLAUDE_CONFIG_DIR"
