# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

$env:npm_config_cache = 'C:\Source\SixFive7\BrowserAI\.work\state-across-close\npm-cache'
$env:npm_config_update_notifier = 'false'
$env:npm_config_fund = 'false'
$env:npm_config_audit = 'false'
$env:LOCALAPPDATA = 'C:\Source\SixFive7\BrowserAI\.work\state-across-close\localappdata'
$env:TEMP = 'C:\Source\SixFive7\BrowserAI\.work\state-across-close\temp'
$env:TMP = 'C:\Source\SixFive7\BrowserAI\.work\state-across-close\temp'
$env:PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD = '1'
$env:GIT_TERMINAL_PROMPT = '0'
$env:GCM_INTERACTIVE = 'never'
Set-Location 'C:\Source\SixFive7\BrowserAI\.work\state-across-close\mcp083'
& npm install '@playwright/mcp@0.0.83' --ignore-scripts --no-audit --no-fund *> 'C:\Source\SixFive7\BrowserAI\.work\state-across-close\mcp083\npm-install.log'
"EXIT=$LASTEXITCODE" | Add-Content 'C:\Source\SixFive7\BrowserAI\.work\state-across-close\mcp083\npm-install.log'
