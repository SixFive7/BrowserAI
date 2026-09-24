# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Opens the Notification Centre through the taskbar clock (UIA Invoke), finds the research
# toast by its marker, invokes -Invoke ('BODY' or a button name) on it, and reports.
param([string]$Invoke = 'BODY')
Add-Type -Path (Join-Path $PSScriptRoot 'W4.cs')
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
[void][W4]::SetProcessDPIAware()
$AE = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]
$tray = $AE::RootElement.FindFirst($TS::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, 'Shell_TrayWnd')))
$clock = $tray.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, 'SystemTray.OmniButton')))
$clock.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 1500
$found = $null
foreach ($y in 300, 600, 900, 1200, 1500) {
  $p = New-Object W4+POINT; $p.X = 3640; $p.Y = $y
  $h = [W4]::GetAncestor([W4]::WindowFromPoint($p), 2)
  $d = [W4]::Describe($h)
  if ($d -match 'CoreWindow') {
    $root = $AE::FromHandle($h)
    $all = $root.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) {
      if ($e.Current.Name -like '*Q254*' -and $e.Current.ControlType -ne [System.Windows.Automation.ControlType]::Text -and $e.Current.ControlType -ne [System.Windows.Automation.ControlType]::Group) {
        $ok = $false; try { [void]$e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern); $ok = $true } catch {}
        if ($ok) { $found = $e; break }
      }
    }
    if (-not $found) {
      foreach ($e in $all) { if ($e.Current.Name -like '*Q254*') { "  candidate: {0} id='{1}' class='{2}' name='{3}'" -f $e.Current.ControlType.ProgrammaticName, $e.Current.AutomationId, $e.Current.ClassName, $e.Current.Name.Substring(0, [Math]::Min(60, $e.Current.Name.Length)) } }
    }
    "NC window: $d"
    if ($found) { break }
  }
}
if (-not $found) { "NOT-FOUND the research toast in the Notification Centre"; $clock.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); exit 2 }
"FOUND " + $found.Current.ControlType.ProgrammaticName + " '" + $found.Current.Name.Substring(0, [Math]::Min(80, $found.Current.Name.Length)) + "...'"
if ($Invoke -eq 'BODY') { $found.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); "INVOKED body" }
else {
  $b = $found.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $Invoke)))
  $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); "INVOKED $Invoke"
}
Start-Sleep -Milliseconds 800
# Close the flyout if it is still open, by the same clock button.
$p = New-Object W4+POINT; $p.X = 3640; $p.Y = 900
if ([W4]::Describe([W4]::GetAncestor([W4]::WindowFromPoint($p), 2)) -match 'CoreWindow') { $clock.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); "closed the flyout" }
"DONE " + (Get-Date).ToString('o')
