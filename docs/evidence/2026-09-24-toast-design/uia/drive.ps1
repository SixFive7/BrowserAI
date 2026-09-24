# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Drives ONE research toast through UI Automation. Refuses to touch any toast whose
# accessible name does not carry the research marker 'Q254'.
#   -Select '<item>'   choose a dropdown item first
#   -Invoke '<button name>' | 'X' | 'BODY'
param([string]$Select, [string]$Invoke, [int]$X = 3600, [int]$Y = 1850)
Add-Type -Path (Join-Path $PSScriptRoot 'W4.cs')
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
[void][W4]::SetProcessDPIAware()
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$h = [IntPtr]::Zero
foreach ($yy in @($Y, ($Y + 100), ($Y + 200), ($Y - 100), ($Y - 200))) {
  $p = New-Object W4+POINT; $p.X = $X; $p.Y = $yy
  $cand = [W4]::GetAncestor([W4]::WindowFromPoint($p), 2)
  if ([W4]::Describe($cand) -match "title='New notification'") { $h = $cand; break }
}
if ($h -eq [IntPtr]::Zero) { "NO-TOAST-WINDOW under the probe points"; exit 2 }
$root = $AE::FromHandle($h)
$toast = $root.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, 'PriorityToastView')))
if ($toast -eq $null) { "NO-TOAST-VIEW"; exit 3 }
$name = $toast.Current.Name
if ($name -notlike '*Q254*') { "REFUSED: the toast on screen is not a research toast"; exit 4 }
"TOAST: " + $name.Substring(0, [Math]::Min(90, $name.Length)) + '...'
if ($Select) {
  $picker = $toast.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, 'Picker')))
  $ec = $picker.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
  $ec.Expand(); Start-Sleep -Milliseconds 700
  $item = $picker.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $Select)))
  if ($item -eq $null) { $item = $root.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $Select)), (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))))) }
  if ($item -eq $null) { "SELECT: item '$Select' not found"; exit 5 }
  $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Start-Sleep -Milliseconds 300
  try { $ec.Collapse() } catch {}
  Start-Sleep -Milliseconds 300
  $sel = $picker.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
  "SELECTED: picker now shows '" + $picker.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()[0].Current.Name + "'"
}
if ($Invoke -eq 'X') {
  $b = $toast.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, 'DismissButton')))
  "INVOKE: X ('" + $b.Current.Name + "')"
  $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
} elseif ($Invoke -eq 'BODY') {
  "INVOKE: body"
  $toast.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
} elseif ($Invoke) {
  $b = $toast.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $Invoke)), (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))))
  if ($b -eq $null) { "INVOKE: button '$Invoke' not found"; exit 6 }
  "INVOKE: button '$Invoke'"
  $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
"DONE " + (Get-Date).ToString('o')
