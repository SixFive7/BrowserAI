# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$tops = $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($t in $tops) {
  $p = Get-Process -Id $t.Current.ProcessId -ErrorAction SilentlyContinue
  if ($p.ProcessName -match 'Shell|explorer|Notification|Toast') { "{0} name='{1}' class='{2}' pid={3}" -f $p.ProcessName, $t.Current.Name, $t.Current.ClassName, $t.Current.ProcessId }
}
