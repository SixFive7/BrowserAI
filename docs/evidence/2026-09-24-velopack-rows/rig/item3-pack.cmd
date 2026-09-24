@rem SPDX-FileCopyrightText: 2026 Jori Huisman
@rem SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
@echo off
"C:\Users\jori\.dotnet\tools\vpk.exe" pack --packId BrowserAI.app.r124 --packVersion 1.1.0 --packTitle "BrowserAI (r124 probe)" --packAuthors "Jori Huisman" --packDir "C:\Source\SixFive7\BrowserAI\.work\velopack-rows\pack124-src" --mainExe BrowserAI.Server.exe --outputDir "C:\Source\SixFive7\BrowserAI\.work\velopack-rows\pack124" --channel win > "C:\Source\SixFive7\BrowserAI\.work\velopack-rows\logs\I3-vpk-pack.log" 2>&1
echo EXIT=%ERRORLEVEL%>> "C:\Source\SixFive7\BrowserAI\.work\velopack-rows\logs\I3-vpk-pack.log"
