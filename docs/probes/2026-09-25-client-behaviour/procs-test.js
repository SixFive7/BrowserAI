// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Positive control for the by-path process query appdrv.js uses: node.exe must be found,
// this very process among them.
'use strict';
const { spawnSync } = require('child_process');
const target = process.argv[2] || 'C:\\Program Files\\nodejs\\node.exe';
const esc = target.replace(/\\/g, '\\\\').replace(/'/g, "''");
const script = [
  "$ErrorActionPreference='Stop'",
  `$ps = @(Get-CimInstance Win32_Process -Filter "ExecutablePath = '${esc}'")`,
  '$out = foreach ($p in $ps) { $parent = Get-CimInstance Win32_Process -Filter "ProcessId = $($p.ParentProcessId)"; [pscustomobject]@{ pid=$p.ProcessId; ppid=$p.ParentProcessId; path=$p.ExecutablePath; created=$p.CreationDate.ToUniversalTime().ToString(\'o\'); parentPath=$parent.ExecutablePath } }',
  'ConvertTo-Json -InputObject @($out) -Depth 3 -Compress',
].join('\n');
const r = spawnSync('pwsh.exe', ['-NoProfile', '-NonInteractive', '-Command', script], { windowsHide: true, encoding: 'utf8', timeout: 60000 });
const list = JSON.parse((r.stdout || '[]').trim() || '[]');
console.log(`status=${r.status} found=${list.length} includesSelf=${list.some((p) => p.pid === process.pid)} stderr=${(r.stderr || '').trim().slice(0, 200)}`);
