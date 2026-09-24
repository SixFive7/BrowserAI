# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Dumps the UI Automation tree of any on-screen toast whose text carries the research marker.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$root = $A::RootElement
$tops = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($t in $tops) {
  $n = $t.Current.Name; $c = $t.Current.ClassName
  if ($n -eq 'New notification' -or $c -eq 'Windows.UI.Core.CoreWindow') {
    $pidOf = $t.Current.ProcessId
    $pname = (Get-Process -Id $pidOf -ErrorAction SilentlyContinue).Path
    "TOP name='$n' class='$c' pid=$pidOf path=$pname"
    $all = $t.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) {
      $pats = ($e.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName -replace 'PatternIdentifiers.Pattern','' }) -join ','
      "  {0} name='{1}' id='{2}' class='{3}' patterns=[{4}]" -f $e.Current.ControlType.ProgrammaticName, $e.Current.Name, $e.Current.AutomationId, $e.Current.ClassName, $pats
    }
  }
}
