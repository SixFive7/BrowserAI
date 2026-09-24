# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch driver for row 130: a NON-SILENT test-pack Setup.exe over a root that already holds an install.
# Reads the dialog through UI Automation, then either invokes a button by name or leaves it alone
# and measures what happens on its own.
param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][string]$Tag,
    [ValidateSet('Invoke','Leave')][string]$Mode = 'Invoke',
    [string]$Button = 'Cancel',
    [int]$Seconds = 400,
    [string]$Setup = 'C:\Source\SixFive7\BrowserAI\Releases\test-pack\BrowserAI.test-installer.exe'
)
$ErrorActionPreference = 'Stop'
$base = 'C:\Source\SixFive7\BrowserAI\.work\velopack-rows'
Add-Type -Path "$base\Rig.cs"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$sandbox = "$base\sandbox"
$ov = [System.Collections.Generic.Dictionary[string,string]]::new()
$ov['CLAUDE_CONFIG_DIR'] = "$sandbox\client"; $ov['CODEX_HOME'] = "$sandbox\codex"; $ov['BROWSERAI_ROOT'] = "$sandbox\data"
$ov['VELOPACK_FIRSTRUN'] = $null; $ov['VELOPACK_RESTART'] = $null
$out = "$base\logs\$Tag-dialog.txt"
$lines = [System.Collections.Generic.List[string]]::new()
$sw = [Diagnostics.Stopwatch]::StartNew()
function Say($s) { $lines.Add(('{0,9:F1}ms {1} {2}' -f $sw.Elapsed.TotalMilliseconds, (Get-Date).ToString('HH:mm:ss.fff'), $s)); [IO.File]::WriteAllLines($out, $lines) }

function Dump($el, $depth) {
    if ($depth -gt 10) { return }
    $c = $el.Current
    $inv = $false; try { $inv = [bool]$el.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::IsInvokePatternAvailableProperty) } catch {}
    $r = $c.BoundingRectangle
    Say (('  ' * $depth) + "[$($c.ControlType.ProgrammaticName -replace 'ControlType\.','')] name='$($c.Name -replace "`r?`n",' / ')' class='$($c.ClassName)' id='$($c.AutomationId)' invoke=$inv rect=$([int]$r.Width)x$([int]$r.Height)")
    $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
    $child = $walker.GetFirstChild($el)
    while ($child) { Dump $child ($depth + 1); $child = $walker.GetNextSibling($child) }
}

$setupLog = "$base\logs\$Tag-setup.log"
$setupProc = [VR.Rig]::StartDetached($Setup, "--verbose --log `"$setupLog`" --installto `"$Root`"", (Split-Path $Setup), $ov)
Say "launched Setup pid=$($setupProc.Pid) created=$([VR.Rig]::Ft($setupProc.Created)) root=$Root mode=$Mode"

$dlg = $null
while ($sw.Elapsed.TotalSeconds -lt 60 -and -not $dlg) {
    [VR.Rig]::Refresh($setupProc); if ($setupProc.Exited -ne 0) { break }
    $dlg = [VR.Rig]::Windows() | Where-Object { $_.Pid -eq $setupProc.Pid -and $_.Visible -and $_.Class -eq '#32770' } | Select-Object -First 1
    if (-not $dlg) { Start-Sleep -Milliseconds 20 }
}
if (-not $dlg) { Say "NO DIALOG; setup exited=$([VR.Rig]::Ft($setupProc.Exited)) code=$($setupProc.ExitCode)"; Say 'DONE'; return }
$dialogShownAt = $sw.Elapsed.TotalMilliseconds
Start-Sleep -Milliseconds 400   # let the dialog finish laying out before it is read
foreach ($w in ([VR.Rig]::Windows() | Where-Object { $_.Pid -eq $setupProc.Pid })) {
    Say ("TOP hwnd=0x{0:X} class='{1}' title='{2}' visible={3} rect={4},{5}-{6},{7} size={8}x{9}" -f $w.Hwnd.ToInt64(), $w.Class, $w.Title, $w.Visible, $w.L, $w.T, $w.R, $w.B, ($w.R - $w.L), ($w.B - $w.T))
}
Say 'UIA:'
Dump ([System.Windows.Automation.AutomationElement]::FromHandle($dlg.Hwnd)) 1

if ($Mode -eq 'Invoke') {
    $cond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $Button)
    $target = [System.Windows.Automation.AutomationElement]::FromHandle($dlg.Hwnd).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($target) {
        $pat = $null
        if ($target.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pat)) { $pat.Invoke(); Say "invoked '$Button' through InvokePattern" }
        else { Say "'$Button' found but has no InvokePattern; posting WM_CLOSE instead"; [void][VR.Rig]::PostClose($dlg.Hwnd) }
    } else { Say "'$Button' not found; posting WM_CLOSE instead"; [void][VR.Rig]::PostClose($dlg.Hwnd) }
}

$goneAt = $null
while ($sw.Elapsed.TotalSeconds -lt $Seconds) {
    [VR.Rig]::Refresh($setupProc)
    if (-not $goneAt) {
        $still = [VR.Rig]::Windows() | Where-Object { $_.Hwnd -eq $dlg.Hwnd -and $_.Visible }
        if (-not $still) { $goneAt = $sw.Elapsed.TotalMilliseconds; Say ("dialog gone, {0:F1} s after it was first seen" -f (($goneAt - $dialogShownAt) / 1000)) }
    }
    if ($setupProc.Exited -ne 0) { break }
    Start-Sleep -Milliseconds 50
}
[VR.Rig]::Refresh($setupProc)
Say ("Setup exited={0} code={1} lifeS={2:F3} (dialog first seen at +{3:F1} ms after launch)" -f [VR.Rig]::Ft($setupProc.Exited), $setupProc.ExitCode, ([VR.Rig]::Ms($setupProc.Created, $setupProc.Exited) / 1000), $dialogShownAt)
Say 'DONE'
