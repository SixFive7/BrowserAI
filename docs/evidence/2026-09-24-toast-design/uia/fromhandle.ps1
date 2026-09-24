# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Finds the toast window under a screen point and dumps its UIA subtree (raw view).
param([int]$X = 3600, [int]$Y = 1850)
Add-Type -Path (Join-Path $PSScriptRoot 'W4.cs')
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
[void][W4]::SetProcessDPIAware()
$p = New-Object W4+POINT; $p.X = $X; $p.Y = $Y
$h = [W4]::GetAncestor([W4]::WindowFromPoint($p), 2)
"window: " + [W4]::Describe($h)
$e = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$w = [System.Windows.Automation.TreeWalker]::RawViewWalker
function Walk($el, $depth) {
  $c = $w.GetFirstChild($el)
  while ($c -ne $null) {
    $pats = ($c.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName -replace 'PatternIdentifiers.Pattern','' }) -join ','
    ("  " * $depth) + ("{0} name='{1}' id='{2}' class='{3}' patterns=[{4}]" -f $c.Current.ControlType.ProgrammaticName, $c.Current.Name, $c.Current.AutomationId, $c.Current.ClassName, $pats)
    if ($depth -lt 12) { Walk $c ($depth + 1) }
    $c = $w.GetNextSibling($c)
  }
}
"root: name='$($e.Current.Name)' class='$($e.Current.ClassName)'"
Walk $e 1
