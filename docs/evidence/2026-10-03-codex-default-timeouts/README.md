<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 - Codex's default MCP startup and tool timeouts, measured

**What this is.** The kb said Codex gives a stdio server 10 s to start and 60 s
for a tool call, and Codex's source at both tags says 30 s and 300 s. This batch
measures the defaults instead of reading them: **codex-cli 0.155.0-alpha.9.2**
and **0.160.0**, the same two binaries the Q369 work ran, each with a
`config.toml` that sets neither `startup_timeout_sec` nor `tool_timeout_sec`,
against a stand-in stdio server that answers `initialize` or a tool call late.
Taken 2026-10-03 between 19:46Z and 19:52Z. Every Codex process had its own
scratch `CODEX_HOME`, a scratch `CLAUDE_CONFIG_DIR`, an allowlisted environment
and a scripted model on `127.0.0.1`, and was started with no window and its
output redirected to files.

## What it found

| Arm | The stand-in | 0.155.0-alpha.9.2 | 0.160.0 |
|---|---|---|---|
| `runs-app/A0-*` | answers `initialize` at once | `ready` | `ready` |
| `runs-app/A15-*` | answers `initialize` after 15 s | `ready` at 15.08 s | `ready` at 15.09 s |
| `runs-app/A35-*` | answers `initialize` after 35 s | `failed` at 30.00 s: *"MCP client for `browserai` timed out after 30 seconds. Add or adjust `startup_timeout_sec` in your config.toml"* | the same, at 30.00 s |
| `runs/S0-*` | answers at once | the tool offered and called | the same |
| `runs/S15-*`, `runs/S35-*` | answers `initialize` after 15 s or 35 s | the first turn went to the model 1.2 s after the server started, with no BrowserAI tool in it, and `exec` ended about 2 s after it began | the same |
| `runs/T75-*` | answers a tool call after 75 s | the call completed and the answer reached the model, 75.9 s for the run | the same, 76.0 s |

**So the default startup timeout is 30 s, the default tool timeout is more than
75 s, and `codex exec` does not wait for a server that is slow to start.** The
`app-server` arms read the startup timeout off `mcpServer/startupStatus/updated`,
which `codex exec` never reaches: it starts its first turn about a second after
launch whether the server has answered or not. Codex's source names 300 s for a
tool call; that upper end was not measured here.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: protocol](../../../kb/mcp/protocol.md#registering-with-codex-and-what-its-startup-timeout-costs----measured-2026-09-24) | The correction of the 10 s and 60 s defaults, and what `codex exec` does with a slow server |
| [kb: re-verification](../../../kb/re-verification.md) | Row 148 |

## What is here

| Path | What it holds |
|---|---|
| `rig/standin.js` | The stand-in server: one tool, `initialize` answered after `SI_INIT_DELAY_MS`, a tool call after `SI_CALL_DELAY_MS`, every frame logged with its time since launch |
| `rig/run.js`, `rig/run-app.js` | The two drivers, `codex exec` and `codex app-server` |
| `rig/q369/appdrv.js`, `rig/q369/cxmodel.js` | The two files of the Q369 rig the drivers run unchanged: the app-server driver and the scripted OpenAI model. That lane's batch, [`2026-10-03-q369-tool-list-refresh`](../2026-10-03-q369-tool-list-refresh/README.md), does not carry them, so they are here with the SPDX header added |
| `runs/<arm>-cx<version>/` | Per `exec` run: the stand-in's log, Codex's JSON events and its trace output, what the scripted model was offered and did, and the `config.toml` the run was given. `S0-cx160` holds two runs, the rig's first check and the batch's own |
| `runs-app/<arm>-cx<version>/` | Per `app-server` run: the driver's log with every notification, the stand-in's log, the steps and the `config.toml` |
| `rig/cut_codex_timeouts.py` | The script that cut this batch |
| `originals.sha256`, `left-out.sha256` | What was changed and what was left out, each with its digest |

## What was cut, and what was left out

- **The machine name**, in the six `app-server` driver logs, where Codex prints
  it; those files are stored under `.trimmed.` names with the originals' digests
  in `originals.sha256`.
- **Left out whole, with digests**: each `app-server`'s own trace output, up to
  1.8 MB a run; the environment file each was handed; and every request body the
  scripted model received, which carries Codex's own prompt and this
  repository's `AGENTS.md`, read because the scratch project sits inside the
  repository.
- **Left out by directory**: each run's scratch `CODEX_HOME` apart from its
  `config.toml`, 993 files, which is what Codex installs into a new home (its
  bundled skills and its state). The client binaries are the Q369 lane's, with
  their digests in that batch's `isolation/`.

## Privacy

Nothing here names the user or the user profile, and the machine name is cut. No
request reached OpenAI: the model provider was a stub on `127.0.0.1`, and the
`app-server` arms pointed theirs at a closed port.
