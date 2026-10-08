<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-08 -- what a stdio MCP server can see of the client that started it

**What this is.** 41 counted runs, taken 2026-10-08 on Windows 11 Pro 10.0.26300 by an
agent of the root session, of what a stdio server can observe to tell five clients
apart: Claude Code's terminal UI, its VS Code transport and `claude -p` (2.1.294, and
the extension's own 2.1.292), and `codex exec` and `codex app-server` (0.161.0, and the
Codex desktop app's 0.159.0-alpha.12.1). A stub server recorded everything it was sent
and everything it could read of its own start, its environment and its parent; every
client ran under a scratch configuration against local API stubs, and no window was
shown. It is what H1-T of [the one-binary design](../../design/one-binary/README.md)
needs to name, before an update, the sessions that will need a person to reconnect
them. **23 files beside this README, 494,220 bytes as this repository stores them**, of which `runs.zip` is 297,828.
⚠️ It records the measurement; which test the relay uses was not chosen when it was
persisted.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: protocol](../../../kb/mcp/protocol.md) | "What a stdio server can see of the client that started it" |
| [kb: re-verification](../../../kb/re-verification.md) | The row that entry carries |
| [the one-binary design](../../design/one-binary/README.md) | H1-T |

## What is here

| Path | What it is |
|---|---|
| `FINDINGS.md` | The agent's report, saved by the root session from its reply because the agent could not write a file |
| `runs/all-runs.tsv` | One row per run: the client, the arm, every signal the stub read, and what the test made of it |
| `runs/all-arms-summary.txt` | Every signal per arm, counted |
| `runs.zip` | **Every run's captures, 330 files**: what the stub was sent and answered (`*.wire.jsonl`, `*.initialize.json`, `*.end.json`), what it read of its own start (`*.start.json`, cut as below), each run's result, and each batch's definition, progress and what the model stub saw; SHA-256 `71ec1a632aa40aa7efbdf0072cf09cd1d16f45bda4b9b367f044eea7a10b9ecb` |
| `rig/` | The stub (`IdRig/IdStub.cs.txt`), the harness, the batch writer, the test (`classify.trimmed.js.txt`) and the table builder |
| `originals.sha256`, `left-out.sha256` | What was changed, each with the SHA-256 of what it was; nothing was left out one by one |

## What was changed, and what was left out

- ⚠️ **Each `*.start.json` keeps only the client's signals.** The stub recorded its
  whole environment and every ancestor process. In the copy here every environment
  variable keeps its name, and only the values of `AI_AGENT`, `CLAUDECODE`,
  `CLAUDE_CODE_ENTRYPOINT`, `CLAUDE_AGENT_SDK_VERSION`, `MCP_CONNECTION_NONBLOCKING`,
  `CLAUDE_PROJECT_DIR`, `SHELL` and the rig's own marker are kept; every other value,
  the messaging token Claude Code hands each server among them, reads "(value cut)".
  Of the ancestors only the first, the client, is kept: the ones above it are the rig
  and the shell of the agent that ran it.
- **Every source file and script is stored with `.txt` appended**, as a record and not
  as this repository's code.
- **The profile path, the user name and the machine name** are replaced by
  `%USERPROFILE%`, `%USERNAME%` and `%COMPUTERNAME%`. Every change is in
  `originals.sha256` with the original's digest.
- **Left out by directory**, with no digest per file: the client binaries and their
  packages, 50 files and 1,291,701,496 bytes; the clients' scratch configuration and
  homes, 3,874 files and 100,195,458 bytes under the Claude Code runs' `cfg` and 1,070
  files and 47,248,481 bytes under the Codex runs' homes, and the Codex scratch homes
  with their background servers, 352 files; the harness's own logs, 42 files, which
  record every environment the harness passed; the client-side logs, 84 files; the
  requests each client sent the local API stub, which carry its whole system prompt;
  build output; the scratch app data; the project folder; and a version probe.

## Privacy

Nothing here names the user profile or the machine, and no environment value but the
client signals above is kept. A scan for the profile path, the user name, the machine
name, the maintainer's e-mail addresses and the names of his other projects found
nothing in the files beside this README or inside `runs.zip`, and its positive
control found every planted needle. The API keys the stubs saw are the literals
`stub-key-not-real` and `not-a-real-key`.
