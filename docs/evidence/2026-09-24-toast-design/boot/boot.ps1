# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Headless: how cheaply and how stably a process can learn when this boot began.
$sw = [Diagnostics.Stopwatch]::StartNew()
$boots = New-Object System.Collections.Generic.List[datetime]
for ($i = 0; $i -lt 100000; $i++) { $boots.Add([DateTime]::UtcNow.AddMilliseconds(-[Environment]::TickCount64)) }
$elapsed = $sw.Elapsed
$min = ($boots | Measure-Object -Property Ticks -Minimum).Minimum
$max = ($boots | Measure-Object -Property Ticks -Maximum).Maximum
"TickCount64 route: 100000 reads in {0:F1} ms ({1:F3} us each, PowerShell overhead included); boot={2:o}; spread={3:F1} ms" -f $elapsed.TotalMilliseconds, ($elapsed.TotalMilliseconds * 1000 / 100000), $boots[0], (($max - $min) / 10000.0)
$sw.Restart(); $os = Get-CimInstance Win32_OperatingSystem; $w = $sw.Elapsed
"WMI Win32_OperatingSystem.LastBootUpTime: {0:o} (local) in {1:F1} ms" -f $os.LastBootUpTime, $w.TotalMilliseconds
"difference WMI - TickCount64 route: {0:F0} ms" -f (($os.LastBootUpTime.ToUniversalTime() - $boots[0]).TotalMilliseconds)
$hb = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -Name HiberbootEnabled -ErrorAction SilentlyContinue).HiberbootEnabled
"Fast Startup (HiberbootEnabled): $hb"
