# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

param([int]$X, [int]$Y)
Add-Type -Path (Join-Path $PSScriptRoot 'W4.cs')
$r = [W4]::At($X, $Y); $r
$p = [regex]::Matches($r,'pid=(\d+)') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
foreach ($i in $p) { "pid $i = " + (Get-Process -Id $i -ErrorAction SilentlyContinue).ProcessName }
