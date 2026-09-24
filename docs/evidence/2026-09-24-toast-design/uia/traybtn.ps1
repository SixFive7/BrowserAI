# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Finds the taskbar element that opens the Notification Centre. Prints only candidates.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$tray = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, 'Shell_TrayWnd')))
$all = $tray.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($e in $all) {
  $n = $e.Current.Name; $id = $e.Current.AutomationId; $c = $e.Current.ClassName
  if ($n -match 'otification|Clock|Klok|Melding|clock' -or $id -match 'Notification|Clock|SystemTrayIcon|ShowDesktop|Notification' -or $c -match 'Clock') {
    $pats = ($e.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName -replace 'PatternIdentifiers.Pattern','' }) -join ','
    "{0} name='{1}' id='{2}' class='{3}' patterns=[{4}]" -f $e.Current.ControlType.ProgrammaticName, $n, $id, $c, $pats
  }
}
