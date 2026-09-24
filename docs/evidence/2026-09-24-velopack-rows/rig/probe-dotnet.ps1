# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

$ErrorActionPreference='Continue'
'start' | Set-Content 'C:\Source\SixFive7\BrowserAI\.work\velopack-rows\logs\dotnet-probe2.txt'
try { $o = & 'C:\Program Files\dotnet\dotnet.exe' --version 2>&1; "out=[$o] exit=$LASTEXITCODE" | Add-Content 'C:\Source\SixFive7\BrowserAI\.work\velopack-rows\logs\dotnet-probe2.txt' } catch { "caught: $_" | Add-Content 'C:\Source\SixFive7\BrowserAI\.work\velopack-rows\logs\dotnet-probe2.txt' }
"errors: $($Error | Out-String)" | Add-Content 'C:\Source\SixFive7\BrowserAI\.work\velopack-rows\logs\dotnet-probe2.txt'
try { $psi=[Diagnostics.ProcessStartInfo]::new('C:\Program Files\dotnet\dotnet.exe','--version'); $psi.RedirectStandardOutput=$true; $psi.UseShellExecute=$false; $pp=[Diagnostics.Process]::Start($psi); $s=$pp.StandardOutput.ReadToEnd(); $pp.WaitForExit(); "dotnet via Process: [$s] code=$($pp.ExitCode)" | Add-Content 'C:\Source\SixFive7\BrowserAI\.work\velopack-rows\logs\dotnet-probe2.txt' } catch { "caught2: $_" | Add-Content 'C:\Source\SixFive7\BrowserAI\.work\velopack-rows\logs\dotnet-probe2.txt' }
'DONE' | Add-Content 'C:\Source\SixFive7\BrowserAI\.work\velopack-rows\logs\dotnet-probe2.txt'