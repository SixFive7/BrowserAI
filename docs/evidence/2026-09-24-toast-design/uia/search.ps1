# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Walks the RAW view breadth-first to a bounded depth, printing only elements whose Name
# carries the research marker, plus their ancestry. Prints nothing about other applications.
param([string]$Needle = 'Q254', [int]$MaxDepth = 6)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$w = [System.Windows.Automation.TreeWalker]::RawViewWalker
$queue = New-Object System.Collections.Queue
$queue.Enqueue(@($A::RootElement, 0, ''))
$visited = 0
while ($queue.Count -gt 0 -and $visited -lt 20000) {
  $item = $queue.Dequeue(); $el = $item[0]; $d = $item[1]; $path = $item[2]
  $visited++
  $child = $w.GetFirstChild($el)
  while ($child -ne $null) {
    try { $n = $child.Current.Name; $c = $child.Current.ClassName; $procId = $child.Current.ProcessId } catch { $n = ''; $c = ''; $procId = 0 }
    $p = "$path > [$c]"
    if ($n -like "*$Needle*") { "FOUND depth=$($d+1) pid=$procId class='$c' name='$n'"; "   path: $p" }
    if ($d + 1 -lt $MaxDepth) { $queue.Enqueue(@($child, ($d + 1), $p)) }
    $child = $w.GetNextSibling($child)
  }
}
"visited=$visited"
