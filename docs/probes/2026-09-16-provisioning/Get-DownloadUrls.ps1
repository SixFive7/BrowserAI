# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Derive the CDN URLs the way playwright-core builds them, reading the revision
# and browserVersion out of the payload's own browsers.json. Never type one.
param([Parameter(Mandatory)][string]$Payload)

$browsers = Get-Content (Join-Path $Payload 'mcp\node_modules\playwright-core\browsers.json') -Raw | ConvertFrom-Json
function Desc([string]$name) { $browsers.browsers | Where-Object { $_.name -eq $name } }

$dbazure = 'https://cdn.playwright.dev/dbazure/download/playwright'
$cft     = 'https://cdn.playwright.dev'   # cftUrl() overrides the mirror list to this one host

$chromium = Desc 'chromium'
$firefox  = Desc 'firefox'
$ffmpeg   = Desc 'ffmpeg'
$winldd   = Desc 'winldd'

[pscustomobject]@{
  chromium = [pscustomobject]@{ revision = $chromium.revision; browserVersion = $chromium.browserVersion; url = "$cft/builds/cft/$($chromium.browserVersion)/win64/chrome-win64.zip" }
  firefox  = [pscustomobject]@{ revision = $firefox.revision;  browserVersion = $firefox.browserVersion;  url = "$dbazure/builds/firefox/$($firefox.revision)/firefox-win64.zip" }
  ffmpeg   = [pscustomobject]@{ revision = $ffmpeg.revision;   url = "$dbazure/builds/ffmpeg/$($ffmpeg.revision)/ffmpeg-win64.zip" }
  winldd   = [pscustomobject]@{ revision = $winldd.revision;   url = "$dbazure/builds/winldd/$($winldd.revision)/winldd-win64.zip" }
} | ConvertTo-Json -Depth 5
