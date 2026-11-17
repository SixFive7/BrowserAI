<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-11-17 -- a dead stdio server under codex-cli 0.169.1 (195 a)

**What this is.** The maintainer's 195 a, in his words verbatim: *"195 a"*: the Codex half
of [`2026-09-23-client-reconnect`](../2026-09-23-client-reconnect/README.md) taken again
with that batch's own rig, against **codex-cli 0.169.1**, the newest stable release, read
off GitHub's API that day (`rust-v0.169.1`, published 2026-11-17T22:21:10Z). The binary
is the release asset `codex-x86_64-pc-windows-msvc.exe`, SHA-256
`f76f94d08b0793bfa129ae62c09341ee32ae96d57e69968ae6b8d7c71a20d8c4`, the digest GitHub
publishes beside it, taken from its `.tar.gz`, whose digest matched too; its own
`--version` answered `codex-cli 0.169.1`. It ran from scratch, never from the copy under
`~\.codex\plugins`. Every run had `CODEX_HOME` and `CLAUDE_CONFIG_DIR` in scratch and the
recorded rig's local Responses stub, so no model was called, at below-normal priority, one
run at a time, on 2026-11-17 between 22:53Z and 22:55Z, Windows 11 Pro 10.0.26300.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: protocol](../../../kb/mcp/protocol.md#codex-never-re-launches-on-the-failure-path-and-does-on-the-next-refresh) | The verification of 2026-11-17 |
| [`HAZARDS.md`](../../../HAZARDS.md#hazard-index) | The same, on the row for a Codex thread that loses its server |
| [kb: re-verification](../../../kb/re-verification.md) | Row 149 |
| [`TODO.md`](../../../TODO.md) | The watch on openai/codex#11489 and #16899 |

## The runs

| Runs | Shape | Server | What happened, 3 of 3 |
|---|---|---|---|
| `E1` to `E3` | `codex exec`, the stub asks for two calls | exits 0 after the first call | the second call answered *"Transport closed"*; one server started |
| `K1` to `K3` | the same | exits 3, a crash, after the first call | the same |
| `T1` to `T3` | `codex app-server`, two turns in one thread | exits 3 after the first call | the second turn's call failed with *"Transport closed"*; one server started |
| `X1` to `X3` | `codex app-server`, calls through `mcpServer/tool/call`, no model | exits 3 after the first call | the second call answered *"Transport closed"*; after `config/mcpServer/reload` a second server started and answered the third call |

`X` is the positive control: it shows the rig sees a server started again when Codex
does start one. Each run's `logs/<run>.launches.log` has one `LAUNCH` line per server
process and the server's own exit code; `logs/<run>.driver.log` has every JSON-RPC line
of the app-server runs; `logs/<run>-stub.events.log` has what the stub answered;
`out/<run>.jsonl` has what `codex exec` printed.

## The rig

The recorded batch's `rig/server.js`, `rig/openaistub.js`, `steps-turn.json` and
`steps-reload.json`, unchanged, and its `appserver.js` with one line added,
`windowsHide: true` on the spawn, kept here as `rig/appserver.js.txt`.
`rig/cx-exec.sh.txt` and `rig/cx-app.sh.txt` are the recorded `cprobe.sh`, `tprobe.sh`
and `dprobe.sh` with the scratch root and the Codex path moved, `CLAUDE_CONFIG_DIR` set,
`codex_mcp=trace` added to the tracing filter, and one script for both app-server shapes.

## What was cut

Every request the stub received carries Codex's own prompt, so the nine
`*-stub.requests.jsonl` captures are left out, and so are the six `codex exec` tracing
logs, `out/<run>.err.txt`; [`left-out.sha256`](left-out.sha256) lists each with its
SHA-256. The scratch `CODEX_HOME` is left out by directory: 3,235 files, 226,119,236
bytes, nearly all of them nine clones of Codex's plugin repository it made under `.tmp`
at its starts. The three rig files kept under `.txt` names have their digests in
[`originals.sha256`](originals.sha256). Line endings are this repository's, LF.
