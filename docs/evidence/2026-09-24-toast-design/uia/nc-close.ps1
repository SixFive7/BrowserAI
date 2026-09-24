# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

Add-Type -Path (Join-Path $PSScriptRoot 'W4.cs')
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
[void][W4]::SetProcessDPIAware()
$AE = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]
function Open() { $p = New-Object W4+POINT; $p.X = 3640; $p.Y = 900; return ([W4]::Describe([W4]::GetAncestor([W4]::WindowFromPoint($p), 2)) -match "title='Notification Centre'") }
"open-before=" + (Open)
if (Open) {
  $tray = $AE::RootElement.FindFirst($TS::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, 'Shell_TrayWnd')))
  $clock = $tray.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, 'SystemTray.OmniButton')))
  $clock.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 1000
}
"open-after=" + (Open)
