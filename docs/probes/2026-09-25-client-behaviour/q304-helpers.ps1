# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Q304 a helpers. Read-only against the machine except where a function says otherwise.
#   Get-FreshEnvironment -Out <file>   CreateEnvironmentBlock(own token, bInherit=FALSE) as JSON:
#                                      the environment a process started from the registry's
#                                      current state gets (what Explorer rebuilds on the broadcast).
#   Get-ShellPath -Out <file>          the PATH in the running shell's (explorer's) own environment
#                                      block, read from its PEB; the shell is found by
#                                      GetShellWindow, never by image name.
#   Get-UserPathRaw -Out <file>        HKCU\Environment Path: kind, byte length and SHA-256 of the
#                                      RAW stored bytes (RegGetValueW, RRF_NOEXPAND), the text, and
#                                      the SHA-256 of the UTF-16 text the clearance script hashes.
#   Get-Clearance -Out <file>          readings 1, 2, 4, 5 (every BrowserAI*.lnk), 7 and 8 of
#                                      build/Get-ClearanceSnapshot.ps1. Readings 3 and 6 are the
#                                      real ~/.claude.json and ~/.codex/config.toml, which this
#                                      researcher may not touch, so they are not read.
param([Parameter(Mandatory)] [string] $Do, [Parameter(Mandatory)] [string] $Out)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class Q304Native
{
    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool CreateEnvironmentBlock(out IntPtr env, IntPtr token, bool inherit);
    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool DestroyEnvironmentBlock(IntPtr env);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);

    public static Dictionary<string, string> FreshEnvironment()
    {
        IntPtr token;
        if (!OpenProcessToken(GetCurrentProcess(), 0x0008 | 0x0002, out token)) throw new System.ComponentModel.Win32Exception();
        try
        {
            IntPtr block;
            if (!CreateEnvironmentBlock(out block, token, false)) throw new System.ComponentModel.Win32Exception();
            try
            {
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var offset = 0;
                while (true)
                {
                    var s = Marshal.PtrToStringUni(IntPtr.Add(block, offset));
                    if (string.IsNullOrEmpty(s)) break;
                    offset += (s.Length + 1) * 2;
                    var eq = s.IndexOf('=', 1);
                    if (eq > 0) result[s.Substring(0, eq)] = s.Substring(eq + 1);
                }
                return result;
            }
            finally { DestroyEnvironmentBlock(block); }
        }
        finally { CloseHandle(token); }
    }

    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(IntPtr p, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr p, int cls, ref PBI info, int len, out int ret);
    [StructLayout(LayoutKind.Sequential)]
    struct PBI { public IntPtr ExitStatus; public IntPtr PebBaseAddress; public IntPtr AffinityMask; public IntPtr BasePriority; public IntPtr UniqueProcessId; public IntPtr InheritedFromUniqueProcessId; }

    static IntPtr ReadPtr(IntPtr p, IntPtr addr)
    {
        var buf = new byte[8]; IntPtr read;
        if (!ReadProcessMemory(p, addr, buf, (IntPtr)8, out read)) throw new System.ComponentModel.Win32Exception();
        return (IntPtr)BitConverter.ToInt64(buf, 0);
    }

    // x64 only: PEB+0x20 = ProcessParameters; RTL_USER_PROCESS_PARAMETERS+0x80 = Environment,
    // +0x3F0 = EnvironmentSize.
    public static string[] ShellEnvironment(out uint pid)
    {
        var hwnd = GetShellWindow();
        if (hwnd == IntPtr.Zero) throw new InvalidOperationException("GetShellWindow returned nothing");
        GetWindowThreadProcessId(hwnd, out pid);
        var p = OpenProcess(0x0400 | 0x0010, false, pid);
        if (p == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        try
        {
            var pbi = new PBI(); int ret;
            var st = NtQueryInformationProcess(p, 0, ref pbi, Marshal.SizeOf(typeof(PBI)), out ret);
            if (st != 0) throw new InvalidOperationException("NtQueryInformationProcess " + st);
            var parameters = ReadPtr(p, IntPtr.Add(pbi.PebBaseAddress, 0x20));
            var env = ReadPtr(p, IntPtr.Add(parameters, 0x80));
            var size = (long)ReadPtr(p, IntPtr.Add(parameters, 0x3F0));
            var buf = new byte[size]; IntPtr read;
            if (!ReadProcessMemory(p, env, buf, (IntPtr)size, out read)) throw new System.ComponentModel.Win32Exception();
            var text = Encoding.Unicode.GetString(buf, 0, (int)read);
            return text.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        }
        finally { CloseHandle(p); }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    static extern int RegGetValueW(IntPtr hkey, string sub, string value, uint flags, out uint type, byte[] data, ref uint size);

    public static byte[] RawUserPath(out uint type)
    {
        var hkcu = new IntPtr(unchecked((int)0x80000001));
        const uint RRF_RT_ANY = 0x0000ffff, RRF_NOEXPAND = 0x10000000;
        uint size = 0;
        var rc = RegGetValueW(hkcu, "Environment", "Path", RRF_RT_ANY | RRF_NOEXPAND, out type, null, ref size);
        if (rc != 0) throw new System.ComponentModel.Win32Exception(rc);
        var data = new byte[size];
        rc = RegGetValueW(hkcu, "Environment", "Path", RRF_RT_ANY | RRF_NOEXPAND, out type, data, ref size);
        if (rc != 0) throw new System.ComponentModel.Win32Exception(rc);
        Array.Resize(ref data, (int)size);
        return data;
    }
}
'@

function Hex([byte[]] $b) { [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($b)) }

switch ($Do) {
    'fresh-env' {
        $e = [Q304Native]::FreshEnvironment()
        # A credential in the user's environment is not needed by any process this rig
        # starts and is not written to disk: dropped here, and only its NAME recorded.
        $o = [ordered]@{}; $dropped = @()
        foreach ($k in ($e.Keys | Sort-Object)) { if ($k -match '(?i)token|secret|password|apikey|api_key') { $dropped += $k } else { $o[$k] = $e[$k] } }
        $o['Q304_DROPPED_VARIABLE_NAMES'] = ($dropped -join ',')
        [System.IO.File]::WriteAllText($Out, ($o | ConvertTo-Json -Depth 3), (New-Object System.Text.UTF8Encoding $false))
    }
    'shell-path' {
        $pid0 = [uint32]0
        $vars = [Q304Native]::ShellEnvironment([ref] $pid0)
        $path = ($vars | Where-Object { $_ -match '^(?i)path=' } | Select-Object -First 1) -replace '^(?i)path=', ''
        $o = [ordered]@{ at = [DateTime]::UtcNow.ToString('o'); shellPid = $pid0; shellImage = (Get-Process -Id $pid0).Path; pathEntries = @($path.Split(';')); entriesNamingBrowserAI = @($path.Split(';') | Where-Object { $_ -match 'BrowserAI' }) }
        [System.IO.File]::WriteAllText($Out, ($o | ConvertTo-Json -Depth 3), (New-Object System.Text.UTF8Encoding $false))
    }
    'user-path' {
        $type = [uint32]0
        $raw = [Q304Native]::RawUserPath([ref] $type)
        $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment')
        $text = [string] $k.GetValue('Path', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        $o = [ordered]@{
            at = [DateTime]::UtcNow.ToString('o')
            regType = $type
            kind = [string]$k.GetValueKind('Path')
            rawBytes = $raw.Length
            rawSha256 = (Hex $raw)
            chars = $text.Length
            textUtf16Sha256 = (Hex ([System.Text.Encoding]::Unicode.GetBytes($text)))
            entriesNamingBrowserAI = @($text.Split(';') | Where-Object { $_ -match 'BrowserAI' })
            text = $text
        }
        [System.IO.File]::WriteAllText($Out, ($o | ConvertTo-Json -Depth 3), (New-Object System.Text.UTF8Encoding $false))
        [System.IO.File]::WriteAllBytes($Out + '.raw', $raw)
    }
    'clearance' {
        $lines = @()
        $uninstall = 'Software\Microsoft\Windows\CurrentVersion\Uninstall'
        foreach ($id in 'BrowserAI.app', 'BrowserAI.app.test') {
            $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$uninstall\$id")
            if ($key) { $lines += "ARP $id PRESENT"; foreach ($n in ($key.GetValueNames() | Sort-Object)) { $lines += ('  {0} {1} = {2}' -f $n, $key.GetValueKind($n), $key.GetValue($n)) } }
            else { $lines += "ARP $id ABSENT" }
        }
        $staging = Join-Path $env:TEMP 'velopack_BrowserAI.app'
        $lines += ('TEMP velopack_BrowserAI.app: ' + $(if (Test-Path $staging) { 'PRESENT' } else { 'ABSENT' }))
        $programs = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
        $lnks = @(Get-ChildItem -LiteralPath $programs -Filter 'BrowserAI*.lnk' -File -ErrorAction SilentlyContinue | Sort-Object Name)
        if ($lnks.Count -eq 0) { $lines += 'StartMenu BrowserAI*.lnk: none' }
        foreach ($l in $lnks) { $lines += ('StartMenu {0} len={1} sha256={2}' -f $l.Name, $l.Length, (Get-FileHash -LiteralPath $l.FullName -Algorithm SHA256).Hash) }
        $lines += '--- HKCU\Environment Path, read raw ---'
        $type = [uint32]0
        $raw = [Q304Native]::RawUserPath([ref] $type)
        $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment')
        $text = [string] $k.GetValue('Path', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        $lines += ('  kind={0} chars={1} sha256={2}' -f $k.GetValueKind('Path'), $text.Length, (Hex ([System.Text.Encoding]::Unicode.GetBytes($text))))
        $lines += ('  raw regType={0} bytes={1} sha256={2}' -f $type, $raw.Length, (Hex $raw))
        $ours = @($text.Split(';') | Where-Object { $_ -match 'BrowserAI' })
        $lines += '  entries naming BrowserAI: ' + $(if ($ours.Count -gt 0) { $ours -join ' | ' } else { 'none' })
        $lines += '--- Task Scheduler root folder, the sign-in tasks ---'
        $scheduler = New-Object -ComObject Schedule.Service
        $scheduler.Connect()
        $all = @($scheduler.GetFolder('\').GetTasks(1))
        $real = @($all | Where-Object { $_.Name -like 'BrowserAI.app sign-in *' } | Sort-Object Name)
        if ($real.Count -eq 0) { $lines += '  BrowserAI.app sign-in: none' }
        foreach ($t in $real) { $lines += ('  {0} sha256={1}' -f $t.Name, (Hex ([System.Text.Encoding]::Unicode.GetBytes([string]$t.Xml)))) }
        $test = @($all | Where-Object { $_.Name -like 'BrowserAI.app.test *' } | Sort-Object Name)
        $lines += '  BrowserAI.app.test tasks: ' + $(if ($test.Count -gt 0) { ($test | ForEach-Object { $_.Name + ' action=' + ([xml]$_.Xml).Task.Actions.Exec.Command + ' ' + ([xml]$_.Xml).Task.Actions.Exec.Arguments }) -join ' | ' } else { 'none' })
        [System.IO.File]::WriteAllText($Out, (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding $false))
    }
    default { throw "unknown -Do $Do" }
}
Write-Output "wrote $Out"
