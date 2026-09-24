# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Opens the Notification Centre, finds the research toast's group, invokes its 'Ask me later'
# button ONLY (never a settings button), then closes the flyout. Dumps the group for evidence.
param([string]$Button = 'Ask me later', [string]$Dump)
Add-Type -Path (Join-Path $PSScriptRoot 'W4.cs')
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
[void][W4]::SetProcessDPIAware()
$AE = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]
$tray = $AE::RootElement.FindFirst($TS::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, 'Shell_TrayWnd')))
$clock = $tray.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, 'SystemTray.OmniButton')))
$p = New-Object W4+POINT; $p.X = 3640; $p.Y = 900
$h = [IntPtr]::Zero
for ($attempt = 1; $attempt -le 2 -and $h -eq [IntPtr]::Zero; $attempt++) {
  $clock.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Milliseconds 100
    $c = [W4]::GetAncestor([W4]::WindowFromPoint($p), 2)
    if ([W4]::Describe($c) -match "CoreWindow") { $h = $c; break }
  }
}
if ($h -eq [IntPtr]::Zero) { "NC DID NOT OPEN"; exit 3 }
"NC window: " + [W4]::Describe($h)
$root = $AE::FromHandle($h)
$groups = $root.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Group)))
$group = $null; foreach ($g in $groups) { if ($g.Current.Name -like '*Q254*') { $group = $g; break } }
if (-not $group) { "NO Q254 GROUP"; $clock.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); exit 2 }
$lines = @(); foreach ($d in $group.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) { $pats = ($d.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName -replace 'PatternIdentifiers.Pattern','' }) -join ','; $lines += ("{0} id='{1}' name='{2}' patterns=[{3}]" -f $d.Current.ControlType.ProgrammaticName, $d.Current.AutomationId, $d.Current.Name, $pats) }
if ($Dump) { $lines | Set-Content -Path $Dump -Encoding UTF8 }
$item = $null; foreach ($li in $group.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))) { if ($li.Current.Name -like '*Q254*') { $item = $li; break } }
if ($item) { try { $item.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); "EXPANDED the toast in the Notification Centre"; Start-Sleep -Milliseconds 700 } catch { "expand failed: $($_.Exception.Message)" } }
$target = $group.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $Button)), (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))))
if ($target) { $target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); "INVOKED '$Button' in the Notification Centre " + (Get-Date).ToUniversalTime().ToString('o') } elseif ($item) { $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); "BUTTON '$Button' not found after expand; INVOKED the toast body in the Notification Centre " + (Get-Date).ToUniversalTime().ToString('o') } else { "nothing invoked" }
Start-Sleep -Milliseconds 800
$h2 = [W4]::GetAncestor([W4]::WindowFromPoint($p), 2)
if ([W4]::Describe($h2) -match "Notification Centre") { $clock.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); "closed the flyout" } else { "flyout already closed" }
