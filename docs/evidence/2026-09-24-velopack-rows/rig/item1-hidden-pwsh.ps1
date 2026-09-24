# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Item 1: exactly Invoke-OrdinaryGate.ps1's launch shape (Start-Process pwsh -WindowStyle Hidden ... -RedirectStandardOutput),
# with a 20 s sleep. Observes, never touches: windows of that pid and of any WindowsTerminal/OpenConsole/conhost created meanwhile.
$ErrorActionPreference = 'Stop'
$b = 'C:\Source\SixFive7\BrowserAI\.work\velopack-rows'
Add-Type -Path "$b\Rig.cs"
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public static class Dpi1 { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }'
[void][Dpi1]::SetProcessDPIAware()
$out = "$b\logs\I1-hidden-pwsh.txt"
$lines = [System.Collections.Generic.List[string]]::new()
$sw = [Diagnostics.Stopwatch]::StartNew()
function Say($s) { $lines.Add(('{0,8:F0}ms {1} {2}' -f $sw.Elapsed.TotalMilliseconds, (Get-Date).ToUniversalTime().ToString('HH:mm:ss.fff'), $s)); [IO.File]::WriteAllLines($out, $lines) }
$before = @{}; foreach ($q in [VR.Rig]::Snapshot()) { $before[$q.Pid] = $q.Exe }
$p = Start-Process pwsh -WindowStyle Hidden -ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 20' -RedirectStandardOutput "$b\logs\I1-hidden-pwsh-stdout.log" -PassThru
Say "launched pwsh pid=$($p.Id) (Start-Process -WindowStyle Hidden -RedirectStandardOutput)"
$watch = @{ $p.Id = 'pwsh (launched)' }
$seen = @{}
$shot = $false
while ($sw.Elapsed.TotalSeconds -lt 23) {
    foreach ($q in [VR.Rig]::Snapshot()) {
        if ($watch.ContainsKey($q.Pid) -or $before.ContainsKey($q.Pid)) { continue }
        if ($q.Exe -in @('WindowsTerminal.exe','OpenConsole.exe','conhost.exe') -or $watch.ContainsKey($q.Ppid)) {
            $watch[$q.Pid] = $q.Exe
            $a = [VR.Proc]::new(); $a.Pid = $q.Pid; [void][VR.Rig]::Attach($a)
            Say "new process pid=$($q.Pid) exe=$($q.Exe) ppid=$($q.Ppid) created=$([VR.Rig]::Ft($a.Created)) path=$($a.Path)"
        }
    }
    foreach ($w in [VR.Rig]::Windows()) {
        if (-not $watch.ContainsKey($w.Pid)) { continue }
        $k = "$($w.Hwnd)|$($w.Visible)"
        if (-not $seen.ContainsKey($k)) { $seen[$k] = 1; Say ("window pid={0} ({1}) hwnd=0x{2:X} class='{3}' title='{4}' visible={5} rect={6},{7}-{8},{9}" -f $w.Pid, $watch[$w.Pid], $w.Hwnd.ToInt64(), $w.Class, $w.Title, $w.Visible, $w.L, $w.T, $w.R, $w.B) }
    }
    if (-not $shot -and $sw.Elapsed.TotalSeconds -gt 3) {
        $vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
        $bmp = [System.Drawing.Bitmap]::new($vs.Width, $vs.Height); $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($vs.Left, $vs.Top, 0, 0, $bmp.Size); $bmp.Save("$b\logs\I1-screenshot.png", [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
        Say "screenshot $($vs.Width)x$($vs.Height) at virtual origin $($vs.Left),$($vs.Top) -> logs\I1-screenshot.png"; $shot = $true
    }
    Start-Sleep -Milliseconds 100
}
$p.Refresh(); Say "pwsh exited=$($p.HasExited) code=$(if ($p.HasExited) { $p.ExitCode } else { 'n/a' })"
Say "visible windows seen for watched pids: $(@($seen.Keys | Where-Object { $_ -like '*|True' }).Count)"
Say 'DONE'
