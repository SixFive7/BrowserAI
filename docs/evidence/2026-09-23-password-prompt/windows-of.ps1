# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Pid-keyed window enumeration. Never selects a process by image name: the caller
# hands in a root pid, this walks Win32_Process by ParentProcessId to build the
# tree, then enumerates every top-level window and keeps the ones whose owning
# thread's process is in that set. Class and title are recorded for each.
param(
    [Parameter(Mandatory = $true)][int]$RootPid,
    [string]$Label = 'unlabelled',
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class WinEnum
{
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder s, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder s, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int index);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static List<IntPtr> TopLevel()
    {
        var found = new List<IntPtr>();
        EnumWindows((h, p) => { found.Add(h); return true; }, IntPtr.Zero);
        return found;
    }

    public static List<IntPtr> Children(IntPtr parent)
    {
        var found = new List<IntPtr>();
        EnumChildWindows(parent, (h, p) => { found.Add(h); return true; }, IntPtr.Zero);
        return found;
    }

    public static uint PidOf(IntPtr h) { uint pid; GetWindowThreadProcessId(h, out pid); return pid; }

    public static string ClassOf(IntPtr h) { var b = new StringBuilder(512); GetClassName(h, b, b.Capacity); return b.ToString(); }

    public static string TitleOf(IntPtr h) { var b = new StringBuilder(2048); GetWindowTextW(h, b, b.Capacity); return b.ToString(); }
}
'@ -ErrorAction Stop

# Build the pid set: the root plus every descendant, by ParentProcessId only.
$all = Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, CommandLine
$byParent = @{}
foreach ($p in $all) {
    if (-not $byParent.ContainsKey([int]$p.ParentProcessId)) { $byParent[[int]$p.ParentProcessId] = @() }
    $byParent[[int]$p.ParentProcessId] += [int]$p.ProcessId
}
$pids = [System.Collections.Generic.HashSet[int]]::new()
$queue = [System.Collections.Generic.Queue[int]]::new()
[void]$pids.Add($RootPid)
$queue.Enqueue($RootPid)
while ($queue.Count -gt 0) {
    $cur = $queue.Dequeue()
    if ($byParent.ContainsKey($cur)) {
        foreach ($c in $byParent[$cur]) { if ($pids.Add($c)) { $queue.Enqueue($c) } }
    }
}

$rows = @()
foreach ($h in [WinEnum]::TopLevel()) {
    $pid_ = [int][WinEnum]::PidOf($h)
    if (-not $pids.Contains($pid_)) { continue }
    $r = New-Object WinEnum+RECT
    [void][WinEnum]::GetWindowRect($h, [ref]$r)
    $rows += [pscustomobject]@{
        level   = 'top'
        hwnd    = '0x{0:X}' -f $h.ToInt64()
        pid     = $pid_
        class   = [WinEnum]::ClassOf($h)
        title   = [WinEnum]::TitleOf($h)
        visible = [WinEnum]::IsWindowVisible($h)
        rect    = '{0},{1},{2},{3}' -f $r.Left, $r.Top, $r.Right, $r.Bottom
    }
    # One level of children for each visible top-level window of ours: the
    # Chromium save-password bubble is a child window of the browser frame.
    if ([WinEnum]::IsWindowVisible($h)) {
        foreach ($c in [WinEnum]::Children($h)) {
            $cpid = [int][WinEnum]::PidOf($c)
            if (-not $pids.Contains($cpid)) { continue }
            $cr = New-Object WinEnum+RECT
            [void][WinEnum]::GetWindowRect($c, [ref]$cr)
            $rows += [pscustomobject]@{
                level   = 'child-of-' + ('0x{0:X}' -f $h.ToInt64())
                hwnd    = '0x{0:X}' -f $c.ToInt64()
                pid     = $cpid
                class   = [WinEnum]::ClassOf($c)
                title   = [WinEnum]::TitleOf($c)
                visible = [WinEnum]::IsWindowVisible($c)
                rect    = '{0},{1},{2},{3}' -f $cr.Left, $cr.Top, $cr.Right, $cr.Bottom
            }
        }
    }
}

# Command lines of the processes in the tree, keyed by pid and never selected by
# image name: the pid set above decided membership, this only reads what those
# pids were started with.
$cmdlines = @()
foreach ($p in $all) {
    if ($pids.Contains([int]$p.ProcessId)) {
        $cmdlines += [pscustomobject]@{ pid = [int]$p.ProcessId; commandLine = $p.CommandLine }
    }
}

$result = [pscustomobject]@{
    label     = $Label
    rootPid   = $RootPid
    takenAt   = (Get-Date).ToString('o')
    pidsInTree = ($pids | Sort-Object)
    commandLines = $cmdlines
    windows   = $rows
}

$json = $result | ConvertTo-Json -Depth 6
if ($OutFile) { Set-Content -Path $OutFile -Value $json -Encoding utf8 }
$json
