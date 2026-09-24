# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Removes every Q254 scratch registration, and the platform's own per-AUMID residue for the
# scratch AUMIDs. Idempotent. Reports what it found and what it removed.
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $here '..\proto\publish\ToastProto.exe'
$items = @(
  (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\BrowserAI Q254 test.lnk'),
  'HKCU:\Software\Classes\CLSID\{6E64C7E9-5743-476D-A2EA-6502822581A4}',
  'HKCU:\Software\Classes\browserai-q254test',
  'HKCU:\Software\Classes\AppUserModelId\BrowserAI.Q254Test.Reg',
  'HKCU:\Software\Classes\AppUserModelId\BrowserAI.Q254Test.Lnk',
  'HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\BrowserAI.Q254Test.Reg',
  'HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\BrowserAI.Q254Test.Lnk'
)
# History is cleared headlessly by history-headless.ps1; this script launches nothing.
foreach ($i in $items) {
  if (Test-Path -LiteralPath $i) {
    Remove-Item -LiteralPath $i -Recurse -Force
    "REMOVED $i (present-after=$(Test-Path -LiteralPath $i))"
  } else { "absent  $i" }
}
