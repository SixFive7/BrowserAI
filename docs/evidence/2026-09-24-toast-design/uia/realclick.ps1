# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# A REAL mouse click on one research toast button, located by UI Automation. Refuses any toast
# without the 'Q254' marker. Restores the cursor position afterwards.
param([string]$Button)
Add-Type -Path (Join-Path $PSScriptRoot 'W4.cs'); Add-Type -Path (Join-Path $PSScriptRoot 'Click.cs')
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
[void][W4]::SetProcessDPIAware()
$AE = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]
$p = New-Object W4+POINT; $p.X = 3600; $p.Y = 1850
$h = [W4]::GetAncestor([W4]::WindowFromPoint($p), 2)
if ([W4]::Describe($h) -notmatch "title='New notification'") { "NO TOAST"; exit 2 }
$toast = $AE::FromHandle($h).FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, 'PriorityToastView')))
if ($toast.Current.Name -notlike '*Q254*') { "REFUSED"; exit 4 }
$b = $toast.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $Button)), (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))))
$r = $b.Current.BoundingRectangle
$x = [int]($r.X + $r.Width / 2); $y = [int]($r.Y + $r.Height / 2)
[Click]::At($x, $y)
"REAL CLICK on '$Button' at ($x,$y) " + (Get-Date).ToUniversalTime().ToString('o')
