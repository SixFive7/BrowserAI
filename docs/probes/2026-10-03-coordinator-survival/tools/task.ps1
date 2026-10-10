# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Option c survival probe, 2026-10-03: a scratch per-user scheduled task that starts the probe's
# coordinator stand-in on demand, with the settings of BrowserAI's own sign-in task
# (SignInTask.DefinitionFor) and NO trigger, so nothing ever starts it again by itself.
# Usage: task.ps1 -Do register|run|remove|show -Name <name> [-Exe <path> -Arguments <args>]
param(
  [Parameter(Mandatory)] [ValidateSet('register', 'run', 'remove', 'show')] [string] $Do,
  [Parameter(Mandatory)] [string] $Name,
  [string] $Exe,
  [string] $Arguments
)
$ErrorActionPreference = 'Stop'
$log = 'C:\Source\SixFive7\BrowserAI\.work\c-scratch\m1\task-actions.log'
function Note($s) { Add-Content -LiteralPath $log -Value ("{0} {1}" -f (Get-Date).ToUniversalTime().ToString('o'), $s) }
$service = New-Object -ComObject Schedule.Service
$service.Connect()
$folder = $service.GetFolder('\')
switch ($Do) {
  'register' {
    $sid = ([System.Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
    $exeX = [System.Security.SecurityElement]::Escape($Exe)
    $argX = [System.Security.SecurityElement]::Escape($Arguments)
    $xml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Author>BrowserAI lane c probe</Author>
    <Description>Scratch probe, 2026-10-03. Starts a coordinator stand-in on demand. Removed when the probe ends.</Description>
  </RegistrationInfo>
  <Triggers />
  <Principals>
    <Principal id="Author">
      <UserId>$sid</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <StartWhenAvailable>false</StartWhenAvailable>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>5</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>$exeX</Command>
      <Arguments>$argX</Arguments>
    </Exec>
  </Actions>
</Task>
"@
    # 6 = TASK_CREATE_OR_UPDATE, 3 = TASK_LOGON_INTERACTIVE_TOKEN
    $null = $folder.RegisterTask($Name, $xml, 6, $null, $null, 3, $null)
    Note "registered '$Name' exe=$Exe args=$Arguments"
    "registered"
  }
  'run' {
    $task = $folder.GetTask($Name)
    $running = $task.Run($null)
    Note "ran '$Name' enginePid=$($running.EnginePID) instanceGuid=$($running.InstanceGuid)"
    "ran enginePid=$($running.EnginePID)"
  }
  'remove' {
    $folder.DeleteTask($Name, 0)
    Note "removed '$Name'"
    "removed"
  }
  'show' {
    $task = $folder.GetTask($Name)
    "state=$($task.State) lastRun=$($task.LastRunTime) lastResult=$($task.LastTaskResult)"
  }
}
