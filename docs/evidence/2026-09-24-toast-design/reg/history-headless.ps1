# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Headless: reads (and with -Clear, clears) the Action Center history of the scratch AUMIDs.
# Runs in Windows PowerShell 5.1 (console), shows nothing, takes no focus.
param([switch]$Clear)
[void][Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
foreach ($id in 'BrowserAI.Q254Test.Lnk','BrowserAI.Q254Test.Reg') {
  $h = [Windows.UI.Notifications.ToastNotificationManager]::History.GetHistory($id)
  "history[$id] count=$($h.Count) tags=" + (($h | ForEach-Object { $_.Tag }) -join ',')
  if ($Clear -and $h.Count -gt 0) { [Windows.UI.Notifications.ToastNotificationManager]::History.Clear($id); "cleared $id" }
}
