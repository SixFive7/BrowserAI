# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Lane c survival probe driver, 2026-10-03. Registers the scratch task, starts the coordinator
# stand-in through it, runs the batch, then kills the coordinator stand-in by its recorded
# pid@creation and watches every browser stand-in the host still holds go with it.
# The task is removed at the end, whatever happened.
param(
  [string[]] $Scenarios = @('b1', 'b2', 'e5', 'd1', 'd2', 'n1', 'n2'),
  [int] $Reps = 3
)
$ErrorActionPreference = 'Stop'
$Scenarios = @($Scenarios | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$S = 'C:\Source\SixFive7\BrowserAI\.work\c-scratch\m1'
$rig = "$S\rig\bin\ExitRig.exe"
$rigw = "$S\rig\binw\ExitRig.exe"
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmss')
$batch = "surv-$stamp"
$name = "BrowserAI.c-probe coordinator $stamp"
$L = "$S\runs\$batch"
$hp = "c-probe-host-$stamp"
$cp = "c-probe-ctl-$stamp"
New-Item -ItemType Directory -Force $L | Out-Null
$out = "$L\driver.log"
function Say($s) { $line = "{0} {1}" -f (Get-Date).ToUniversalTime().ToString('o'), $s; Add-Content -LiteralPath $out -Value $line; $line }
try {
  Say (& "$S\tools\task.ps1" -Do register -Name $name -Exe $rigw -Arguments "coord --logdir `"$L`" --host-pipe $hp --ctl-pipe $cp --max-minutes 90")
  Say (& "$S\tools\task.ps1" -Do run -Name $name)
  $ok = $false
  foreach ($i in 1..50) { Start-Sleep -Milliseconds 200; $p = & $rig ctl --pipe $hp --request ping; if ($p -like 'pong*') { $ok = $true; break } }
  Say "host ping: $p"
  if (-not $ok) { throw 'host never answered' }
  $identity = Get-Content -LiteralPath "$L\coord-identity.txt"
  Say "coordinator identity: $identity"
  Say (& "$S\tools\genc.ps1" -Batch $batch -HostPipe $hp -Scenarios $Scenarios -Reps $Reps)
  Say 'batch starting'
  & $rig batch "$S\batches\$batch.json" | Out-Null
  Say 'batch done'
  $list = & $rig ctl --pipe $hp --request list
  Say "host list at the end: $list"
  # Start two more browser stand-ins that nothing released, so the coordinator's death has
  # something to take, and record every pair still alive.
  Say (& $rig ctl --pipe $hp --request "start final-a $L")
  Say (& $rig ctl --pipe $hp --request "start final-b $L")
  $list = & $rig ctl --pipe $hp --request list
  $pairs = ($list -split ';' | Where-Object { $_ -like 'final-*' } | ForEach-Object { ($_ -split '=')[1] }) -join ','
  $hostPair = ''
  Say "final pairs: $pairs"
  # The watcher opens its handles first, so each exit is seen and not inferred from a failed open.
  $w = Start-Process -FilePath $rig -ArgumentList @('watch', '--pairs', $pairs, '--ms', '15000') -PassThru -WindowStyle Hidden -RedirectStandardOutput "$L\final-watch.out"
  Start-Sleep -Milliseconds 700
  Say (& $rig kill --pair $identity)
  $w.WaitForExit(30000) | Out-Null
  Say ((Get-Content -LiteralPath "$L\final-watch.out") -join ' | ')
  $after = & $rig ctl --pipe $hp --request ping --timeout-ms 2000
  Say "host after the coordinator was killed: $after"
}
catch { Say "DRIVER ERROR: $($_.Exception.Message)" }
finally {
  try { Say (& "$S\tools\task.ps1" -Do remove -Name $name) } catch { Say "TASK REMOVE FAILED: $($_.Exception.Message)" }
  Say 'driver done'
}
