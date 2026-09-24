# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Item 2: a REAL mouse click on Cancel in the "already installed" dialog, over a silent scratch install of the test pack.
$ErrorActionPreference = 'Stop'
$b = 'C:\Source\SixFive7\BrowserAI\.work\velopack-rows'
$root = "$b\root-d"
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class M {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
}
'@
[void][M]::SetProcessDpiAwarenessContext([IntPtr]::new(-4))
Add-Type -Path "$b\Rig.cs"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$out = "$b\logs\I2-cancel-click.txt"
$lines = [System.Collections.Generic.List[string]]::new()
$sw = [Diagnostics.Stopwatch]::StartNew()
function Say($s) { $lines.Add(('{0,8:F0}ms {1} {2}' -f $sw.Elapsed.TotalMilliseconds, (Get-Date).ToUniversalTime().ToString('HH:mm:ss.fff'), $s)); [IO.File]::WriteAllLines($out, $lines) }
$ov = [System.Collections.Generic.Dictionary[string,string]]::new()
$ov['CLAUDE_CONFIG_DIR'] = "$b\sandbox\client"; $ov['CODEX_HOME'] = "$b\sandbox\codex"; $ov['BROWSERAI_ROOT'] = "$b\sandbox\data"
$ov['VELOPACK_FIRSTRUN'] = $null; $ov['VELOPACK_RESTART'] = $null
$setup = 'C:\Source\SixFive7\BrowserAI\Releases\test-pack\BrowserAI.test-installer.exe'

# 1. silent install into root-d (the thing the dialog will find)
$p = [VR.Rig]::StartDetached($setup, "--silent --log `"$b\logs\I2-install-setup.log`" --installto `"$root`"", (Split-Path $setup), $ov)
while ($true) { [VR.Rig]::Refresh($p); if ($p.Exited -ne 0) { break }; Start-Sleep -Milliseconds 50 }
Say "silent install exit=$($p.ExitCode); sq.version mtime=$((Get-Item "$root\current\sq.version").LastWriteTimeUtc.ToString('o')) sha=$((Get-FileHash "$root\current\sq.version").Hash.Substring(0,16))"
$beforeList = (Get-ChildItem $root -Recurse -File | Measure-Object -Property Length -Sum)
Say "root-d before: files=$($beforeList.Count) bytes=$($beforeList.Sum)"

# 2. non-silent Setup over it
$s = [VR.Rig]::StartDetached($setup, "--verbose --log `"$b\logs\I2-dialog-setup.log`" --installto `"$root`"", (Split-Path $setup), $ov)
$dlg = $null
while ($sw.Elapsed.TotalSeconds -lt 60 -and -not $dlg) { $dlg = [VR.Rig]::Windows() | Where-Object { $_.Pid -eq $s.Pid -and $_.Visible -and $_.Class -eq '#32770' } | Select-Object -First 1; if (-not $dlg) { Start-Sleep -Milliseconds 20 } }
if (-not $dlg) { Say 'NO DIALOG'; return }
Start-Sleep -Milliseconds 500
$ae = [System.Windows.Automation.AutomationElement]::FromHandle($dlg.Hwnd)
$cancel = $ae.FindFirst([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Cancel'))
$r = $cancel.Current.BoundingRectangle
$x = [int]($r.X + $r.Width / 2); $y = [int]($r.Y + $r.Height / 2)
Say "dialog hwnd=0x$('{0:X}' -f $dlg.Hwnd.ToInt64()) Cancel rect=$([int]$r.X),$([int]$r.Y) $([int]$r.Width)x$([int]$r.Height) -> click point $x,$y"

# 3. the click, under .work\ui.lock
$lock = 'C:\Source\SixFive7\BrowserAI\.work\ui.lock'
$fsl = $null
while (-not $fsl) { try { $fsl = [IO.File]::Open($lock, 'CreateNew', 'Write', 'None') } catch { Say 'ui.lock busy, retrying'; Start-Sleep -Milliseconds 250 } }
try {
    $pt = [M+POINT]::new(); $pt.X = $x; $pt.Y = $y
    $hit = [M]::WindowFromPoint($pt); $hp = 0; [void][M]::GetWindowThreadProcessId($hit, [ref]$hp)
    if ($hp -ne $s.Pid) { [void][M]::SetWindowPos($dlg.Hwnd, [IntPtr]::new(-1), 0, 0, 0, 0, 0x0043); Start-Sleep -Milliseconds 200; $hit = [M]::WindowFromPoint($pt); [void][M]::GetWindowThreadProcessId($hit, [ref]$hp); Say "dialog was not on top; set TOPMOST; now under point: pid=$hp" }
    if ($hp -eq $s.Pid) {
        $old = [M+POINT]::new(); [void][M]::GetCursorPos([ref]$old)
        [void][M]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 60
        [M]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 40; [M]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
        Say "CLICKED Cancel with the mouse at $x,$y (window under point belonged to Setup pid $hp); cursor restored to $($old.X),$($old.Y)"
        Start-Sleep -Milliseconds 60; [void][M]::SetCursorPos($old.X, $old.Y)
    } else { Say "NOT CLICKED: the window under $x,$y belongs to pid $hp, not Setup $($s.Pid); posting WM_CLOSE instead"; [void][VR.Rig]::PostClose($dlg.Hwnd) }
} finally { $fsl.Dispose(); Remove-Item -LiteralPath $lock -Force; Say 'ui.lock released' }

while ($sw.Elapsed.TotalSeconds -lt 90) { [VR.Rig]::Refresh($s); if ($s.Exited -ne 0) { break }; Start-Sleep -Milliseconds 20 }
Say "Setup exited=$([VR.Rig]::Ft($s.Exited)) code=$($s.ExitCode)"
Say "log tail: $((Get-Content "$b\logs\I2-dialog-setup.log" | Select-Object -Last 1))"
$afterList = (Get-ChildItem $root -Recurse -File | Measure-Object -Property Length -Sum)
Say "root-d after: files=$($afterList.Count) bytes=$($afterList.Sum); sq.version mtime=$((Get-Item "$root\current\sq.version").LastWriteTimeUtc.ToString('o')) sha=$((Get-FileHash "$root\current\sq.version").Hash.Substring(0,16)); renamed-aside dirs: $(@(Get-ChildItem $b -Directory -Filter 'root-d.*').Count); 'Renaming' in log: $([bool](Select-String -Path "$b\logs\I2-dialog-setup.log" -Pattern 'Renaming existing directory' -Quiet))"
Say 'DONE'
