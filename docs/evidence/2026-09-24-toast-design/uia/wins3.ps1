# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

Add-Type -Path (Join-Path $PSScriptRoot 'W3.cs')
foreach ($l in [W3]::InRegion(3300, 1600)) { $p = [regex]::Match($l,'pid=(\d+)').Groups[1].Value; $n = (Get-Process -Id $p -ErrorAction SilentlyContinue).ProcessName; "$l proc=$n" }
