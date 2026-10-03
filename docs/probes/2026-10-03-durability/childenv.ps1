# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03). The child environment BrowserAI builds
# (src\BrowserAI\Protocol\ChildEnvironment.cs on origin/next 5f1166c: InheritedWhenSet and
# Forced), with PLAYWRIGHT_BROWSERS_PATH pointed at this research's own byte-identical copy of
# chromium-1247 and firefox-1553, and TEMP, TMP, LOCALAPPDATA and PWTEST_SERVER_REGISTRY moved
# into the scratch so nothing lands in the real ones.
$script:InheritedWhenSet = @('SystemRoot','windir','SystemDrive','COMSPEC','PATH','PATHEXT','NUMBER_OF_PROCESSORS',
    'PROCESSOR_ARCHITECTURE','PROCESSOR_IDENTIFIER','OS','TEMP','TMP','USERPROFILE','LOCALAPPDATA','APPDATA',
    'HOMEDRIVE','HOMEPATH','PUBLIC','ProgramData','ALLUSERSPROFILE','ProgramFiles','ProgramFiles(x86)',
    'ProgramW6432','CommonProgramFiles','CommonProgramFiles(x86)','CommonProgramW6432','USERNAME','USERDOMAIN',
    'COMPUTERNAME','SESSIONNAME','HTTP_PROXY','HTTPS_PROXY','NO_PROXY','ALL_PROXY','NODE_EXTRA_CA_CERTS',
    'PWTEST_SERVER_REGISTRY')

function New-ChildEnv([string] $temp, [string] $local, [string] $reg, [hashtable] $extra) {
    $d = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($n in $script:InheritedWhenSet) { $v = [Environment]::GetEnvironmentVariable($n); if ($null -ne $v) { $d[$n] = $v } }
    $d['PLAYWRIGHT_SKIP_BROWSER_GC'] = '1'
    $d['PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD'] = '1'
    $d['MOZ_DISABLE_SAFE_MODE_KEY'] = '1'
    $d['PLAYWRIGHT_BROWSERS_PATH'] = 'C:\Source\SixFive7\BrowserAI\.work\durability\browsers'
    $d['TEMP'] = $temp; $d['TMP'] = $temp; $d['LOCALAPPDATA'] = $local; $d['PWTEST_SERVER_REGISTRY'] = $reg
    $d['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    if ($extra) { foreach ($k in $extra.Keys) { $d[$k] = [string]$extra[$k] } }
    return $d
}
