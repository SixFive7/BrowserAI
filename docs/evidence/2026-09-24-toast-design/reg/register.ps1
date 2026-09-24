# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Q254 scratch registrations. Every key and file this writes is appended to created.txt,
# and cleanup.ps1 removes exactly those plus the platform's own per-AUMID residue.
param(
  [ValidateSet('shortcut','shortcut-activator','clsid','protocol','aumid-reg','aumid-reg-activator','aumid-lnk-activator')][string]$What
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = (Resolve-Path (Join-Path $here '..\proto\publish\ToastProto.exe')).Path
$ledger = Join-Path $here 'created.txt'
$clsid = '{6E64C7E9-5743-476D-A2EA-6502822581A4}'
$lnk = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\BrowserAI Q254 test.lnk'
function Note($s) { Add-Content -Path $ledger -Value ("{0:o} {1}" -f (Get-Date), $s) }
switch ($What) {
  { $_ -in 'shortcut','shortcut-activator' } {
    $sh = New-Object -ComObject WScript.Shell
    $l = $sh.CreateShortcut($lnk); $l.TargetPath = $exe; $l.WorkingDirectory = (Split-Path $exe); $l.IconLocation = "$exe,0"; $l.Save()
    Add-Type -Path (Join-Path $here 'LnkWriter.cs') -ErrorAction SilentlyContinue
    $act = if ($What -eq 'shortcut-activator') { $clsid } else { $null }
    $r = [LnkWriter]::Set($lnk, 'BrowserAI.Q254Test.Lnk', $act)
    Note "FILE $lnk (aumid=BrowserAI.Q254Test.Lnk activator=$act) $r"
    "shortcut: $lnk $r"
  }
  'clsid' {
    $k = "HKCU:\Software\Classes\CLSID\$clsid\LocalServer32"
    New-Item -Path $k -Force | Out-Null
    Set-Item -Path $k -Value "`"$exe`" -ToastActivated"
    Note "KEY HKCU:\Software\Classes\CLSID\$clsid"
    "clsid: $k = $((Get-Item $k).GetValue(''))"
  }
  'protocol' {
    $k = 'HKCU:\Software\Classes\browserai-q254test'
    New-Item -Path "$k\shell\open\command" -Force | Out-Null
    Set-Item -Path $k -Value 'URL:BrowserAI Q254 test'
    New-ItemProperty -Path $k -Name 'URL Protocol' -Value '' -PropertyType String -Force | Out-Null
    Set-Item -Path "$k\shell\open\command" -Value "`"$exe`" `"%1`""
    Note "KEY $k"
    "protocol: $k -> $((Get-Item "$k\shell\open\command").GetValue(''))"
  }
  { $_ -in 'aumid-reg','aumid-reg-activator' } {
    $k = 'HKCU:\Software\Classes\AppUserModelId\BrowserAI.Q254Test.Reg'
    New-Item -Path $k -Force | Out-Null
    New-ItemProperty -Path $k -Name 'DisplayName' -Value 'BrowserAI Q254 test (registry id)' -PropertyType String -Force | Out-Null
    $icon = (Resolve-Path (Join-Path $here '..\..\..\assets\BrowserAI.ico')).Path
    New-ItemProperty -Path $k -Name 'IconUri' -Value $icon -PropertyType String -Force | Out-Null
    if ($What -eq 'aumid-reg-activator') { New-ItemProperty -Path $k -Name 'CustomActivator' -Value $clsid -PropertyType String -Force | Out-Null }
    Note "KEY $k"
    "aumid-reg: $k"; Get-ItemProperty $k | Select-Object DisplayName,IconUri,CustomActivator | Format-List | Out-String
  }
  'aumid-lnk-activator' {
    # The configuration the real product would have: a shortcut carrying the AUMID and NO
    # ToastActivatorCLSID, plus a registry AppUserModelId key naming the activator.
    $k = 'HKCU:\Software\Classes\AppUserModelId\BrowserAI.Q254Test.Lnk'
    New-Item -Path $k -Force | Out-Null
    New-ItemProperty -Path $k -Name 'CustomActivator' -Value $clsid -PropertyType String -Force | Out-Null
    Note "KEY $k"
    "aumid-lnk-activator: $k"; Get-ItemProperty $k | Select-Object CustomActivator | Format-List | Out-String
  }
}
