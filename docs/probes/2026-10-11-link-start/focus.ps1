# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Brings the scratch Firefox's window to the foreground without synthesising any input:
# it attaches input queues and calls SetForegroundWindow, which was allowed for this
# measurement so the chain under test begins with Firefox in front. It never calls
# SendInput or keybd_event and moves nothing of the real mouse or keyboard. The window is
# found by its title, which carries the probe page's unique name, so his own Firefox
# windows are never matched.
[CmdletBinding()]
param([string] $TitleContains = 'Link probe page')

$ErrorActionPreference = 'Stop'

Add-Type -Namespace L55 -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
[DllImport("user32.dll")] public static extern uint GetCurrentThreadId();
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int InternalGetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
public delegate bool EnumProc(IntPtr h, IntPtr p);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
'@

function Get-ClassName([IntPtr] $h) { $b = New-Object System.Text.StringBuilder 256; [void][L55.Win]::GetClassName($h, $b, 256); $b.ToString() }
function Get-Title([IntPtr] $h) { $b = New-Object System.Text.StringBuilder 512; [void][L55.Win]::InternalGetWindowText($h, $b, 512); $b.ToString() }

$found = $null
$cb = [L55.Win+EnumProc] {
    param([IntPtr] $h, [IntPtr] $p)
    if ($script:found) { return $true }
    if (-not [L55.Win]::IsWindowVisible($h)) { return $true }
    if ((Get-ClassName $h) -ne 'MozillaWindowClass') { return $true }
    $title = Get-Title $h
    if ($title -like "*$TitleContains*") {
        $owner = 0; [void][L55.Win]::GetWindowThreadProcessId($h, [ref] $owner)
        $script:found = [pscustomobject]@{ H = $h; Title = $title; Pid = [int] $owner }
    }
    return $true
}
[void][L55.Win]::EnumWindows($cb, [IntPtr]::Zero)

$before = [L55.Win]::GetForegroundWindow()
$beforePid = 0; [void][L55.Win]::GetWindowThreadProcessId($before, [ref] $beforePid)

if ($found) {
    if ([L55.Win]::IsIconic($found.H)) { [void][L55.Win]::ShowWindow($found.H, 9) }
    $fgThread = [L55.Win]::GetWindowThreadProcessId($before, [ref]([uint32] 0))
    $me = [L55.Win]::GetCurrentThreadId()
    [void][L55.Win]::AttachThreadInput($fgThread, $me, $true)
    [void][L55.Win]::BringWindowToTop($found.H)
    [void][L55.Win]::SetForegroundWindow($found.H)
    [void][L55.Win]::AttachThreadInput($fgThread, $me, $false)
    Start-Sleep -Milliseconds 300
}

$after = [L55.Win]::GetForegroundWindow()
$afterPid = 0; [void][L55.Win]::GetWindowThreadProcessId($after, [ref] $afterPid)

[pscustomobject]@{
    firefoxHwnd = if ($found) { '0x{0:X}' -f [int64] $found.H } else { $null }
    firefoxPid = if ($found) { $found.Pid } else { 0 }
    firefoxTitle = if ($found) { $found.Title } else { $null }
    foregroundPidBefore = [int] $beforePid
    foregroundPidAfter = [int] $afterPid
    foregroundTitleAfter = Get-Title $after
    isFirefoxForeground = ($found -and $afterPid -eq $found.Pid)
} | ConvertTo-Json -Compress
