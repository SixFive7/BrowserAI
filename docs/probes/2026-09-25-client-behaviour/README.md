<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-25 -- real clients against a stand-in for an updating server, and a Codex started on either side of an install

Establishes
[What each client does when `tools/list` fails at the first connection](../../../kb/mcp/protocol.md#what-each-client-does-when-toolslist-fails-at-the-first-connection----measured-2026-09-25),
the measured half of the Codex bare-name route in the same article, and
re-verification row 173. Evidence:
[`docs/evidence/2026-09-25-client-behaviour/`](../../evidence/2026-09-25-client-behaviour/README.md).

**Why it exists.** Q296 asked what an updating BrowserAI should answer to
`tools/list`, and Q304 whether a Codex started before the install can reach a
project entry that names the server by its bare file name. Both are about the
real clients, so both drive them, with no credential and no model.

## What is here

| File | What it does |
|---|---|
| `updstub.js` | The stand-in: a stdio MCP server that answers like an updating BrowserAI in each shape the runs compare |
| `ccstub.js`, `cxstub.js` | Stand-ins for the Anthropic and OpenAI APIs, so the real clients run with no credential. `apistub.q261.orig.js` and `openaistub.q254.orig.js` are the earlier stubs they were derived from |
| `cc-run.sh`, `cx-exec-run.sh`, `cx-app-run.sh` | One run each of Claude Code, `codex exec` and `codex app-server` against the stand-in, under scratch homes |
| `appdrv.js` | Drives `codex app-server` over stdio JSON-RPC through a list of steps |
| `passthru.js` | A pass-through in front of the real BrowserAI server that records every frame |
| `standin.js` | The stand-in `Update.exe`, staged the way `UpdateInProgressTests` stages it, so the real server enters its updating mode |
| `cc-*.js`, `cx-*.js`, `app-summary.js`, `index.js`, `texts.js` | What each client saw and sent, per run, and the index and texts files of the evidence |
| `q304-*.sh`, `q304-*.ps1`, `q304-*.js`, `lockholder.ps1`, `runhidden.js` | The Q304 install under the suite's installer lock, the app-servers started before and after it with the environment Windows builds for a new process, and the PATH readings |
| `procs-test.js` | A check of the process query the driver uses |

## What keeps it off the rest of the machine

- Every client ran under scratch `CLAUDE_CONFIG_DIR` and `CODEX_HOME`, against a
  local stub, with a key that is not a key.
- The Q304 install was the suite's own test pack, under `.work\installer.lock`,
  into a scratch root, with the hooks pointed at scratch configurations; it was
  uninstalled at the end, and the user PATH read back byte-identical.
- **It selects no process by image name.** Of the eight spellings
  [`ProcessSelection`](../../../tests/BrowserAI.Tests/Harness/ProcessSelection.cs)
  keys on, `Win32_Process` appears filtered on the full executable path under
  the scratch install root and on a parent pid, and `Get-Process -Id` on a pid
  the rig holds. ⚠️ **The real scan flags `appdrv.js` and `q304-helpers.ps1`
  anyway**, measured by copying the rig under `build/`: a JSON-RPC
  notification's server `name` and scheduled tasks' `Name`, each compared to a
  string, which the scan reads as a process name. Both are false positives;
  [the directory's README](../README.md) records them.

## Running it

Nobody ran it when this record was written; what follows is read from the
files. Start the two stubs, then a run script per arm. **The paths are compiled
in**: the scratch root `C:\Source\SixFive7\BrowserAI\.work\client-behaviour`, and
in the Q304 scripts a scratch install root under the user profile, cut here to
`%USERPROFILE%` as the README of the evidence says. A run from elsewhere needs
them changed and is a new measurement with a date of its own.

## How the stored copies differ from the ones that ran

The two SPDX lines were added to every file. In five files the user-profile path
was replaced by `%USERPROFILE%`: `cx-app-run.sh`, `cx-exec-run.sh`,
`q304-app.sh`, `q304-hookenv.js` and `q304-prep.sh`. Nothing else moved.
