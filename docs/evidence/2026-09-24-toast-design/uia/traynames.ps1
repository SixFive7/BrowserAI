# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$tray = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, 'Shell_TrayWnd')))
$all = $tray.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($e in $all) { $n = $e.Current.Name; if ($e.Current.ClassName -match 'OmniButton|Clock|Notification|Bell|Badge' -or $n -match 'otification|melding') { "{0} class='{1}' name='{2}'" -f $e.Current.ControlType.ProgrammaticName, $e.Current.ClassName, ($n -replace "`r?`n",' | ') } }
