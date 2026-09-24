# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

param([int]$X, [int]$Y)
Add-Type @"
using System.Runtime.InteropServices;
public static class Dpi2 { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[void][Dpi2]::SetProcessDPIAware()
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase
$A = [System.Windows.Automation.AutomationElement]
$e = $A::FromPoint((New-Object System.Windows.Point($X, $Y)))
$walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
$chain = @(); $cur = $e
while ($cur -ne $null -and -not $cur.Equals($A::RootElement)) { $chain += $cur; $cur = $walker.GetParent($cur) }
"CHAIN (leaf first):"
foreach ($c in $chain) { "  {0} name='{1}' id='{2}' class='{3}' pid={4}" -f $c.Current.ControlType.ProgrammaticName, $c.Current.Name, $c.Current.AutomationId, $c.Current.ClassName, $c.Current.ProcessId }
$top = $chain[-1]
"SUBTREE of top:"
$all = $top.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($d in $all) {
  $pats = ($d.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName -replace 'PatternIdentifiers.Pattern','' }) -join ','
  "  {0} name='{1}' id='{2}' class='{3}' patterns=[{4}]" -f $d.Current.ControlType.ProgrammaticName, $d.Current.Name, $d.Current.AutomationId, $d.Current.ClassName, $pats
}
