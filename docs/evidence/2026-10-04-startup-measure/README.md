<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-04 -- the first turn, a held call, Codex's `required`, the cost of a start, and a pipe call under load

**What this is.** The measurements behind Q369.1, Q369.4 and Q381, taken 2026-10-04
between 00:26Z and 02:07Z on Windows 11 Pro 10.0.26300 by the lane asked whether a
lazy start would work and what the coordinator call's 500 ms limit should be: a
stand-in server, never BrowserAI, driven by Claude Code 2.1.288 (`-p` and the VS Code
stream-json transport) and codex-cli 0.155.0-alpha.9.2 and 0.160.0 (`exec` and
`app-server`), each under a scratch configuration against a local API stub, its model
a script; the real product, BrowserAI 1.1.1-alpha.0.192 from `1ee00ec0` with RegisterAI
0.2.0 in the payload, installed by hand into a scratch root and timed from the
client's side; this machine's own process logs of 1.0.0 and 1.1.0 for 673 real starts;
and the coordinator's pipe call, idle and under two full suite runs, under the suite
lock. **381 files beside this README, 5,134,299 bytes as this repository stores them**, of which `runs.zip` is
2,767,027.

The lane's own report survives as the report the root session saved,
`.work\reports-2026-10-04\startup-measure.md`, which is scratch; what a record rests
on is in the kb entries below.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: protocol](../../../kb/mcp/protocol.md) | "When each client's first turn goes out, and a call held behind it": the first turn, the held call, the failure shapes, `required = true` and the readings of `alwaysLoad`, `codex mcp add` and `startup_readiness` |
| [kb: provisioning and timings](../../../kb/playwright/provisioning-and-timings.md) | "What a server's start costs before its first answer" |
| [kb: tools and artifacts](../../../kb/playwright/tools-and-artifacts.md) | "The upstream snapshot is the list a live child answers, 70 of 70" |
| [kb: processes](../../../kb/windows/processes.md) | "A pipe call between two of BrowserAI's processes, idle and under a full suite's load" |
| [kb: re-verification](../../../kb/re-verification.md) | The rows those entries carry |
| [the one-binary design](../../design/one-binary/README.md) | The first turn (D6), the held call (D8), the waits (D3), and the built-in tool list |

## What is here

| Path | What it is |
|---|---|
| `runs/<batch>/table.tsv`, `batch.json`, `policy.json`, `progress.log` | One table per batch: `cc1` and `cc2` are Claude Code through `--mcp-config`, `cc4` Claude Code at user scope, `cxA` and `cxB` Codex at both versions, `cxC` Codex with `required = true` |
| `part3/` | The real product: `c0` a front with the host running, `c1t` the cold path through the real task, `c2` the in-process fallback, `codex` the `codex exec` first turns, with each case's summary; `make-install.ps1.txt` and `task.ps1.txt` built the scratch install and the scratch task, and `task-actions.log` records the task's registration and removal |
| `q369/fieldstarts.out` | The 673 real starts, summarised, read from this machine's process logs by `rig/fieldstarts.py.txt` |
| `q381/` | The pipe call: `idle/`, `idle-fresh/`, `load/` and `load2/` with every call's time, the second starts, the drivers' logs and the load suite run's own log |
| `runs.zip` | **Every run of every batch and of `part3` and `q381`, 2,337 files**: each run's harness log, the stand-in's wire log, the configuration the client was handed and what the model saw, and the scratch install's own process log of the day, with the client-side logs and the clients' scratch homes left out (below); SHA-256 `ae09928555441f831d2b97daeb3a1e66848b481f6284ba869baac2c039cea6e5` |
| `rig/` | The drivers, the stand-in (`lazysrv.js.txt`), the model stubs and the table builders |
| `probe/` | The probe that called the coordinator through the published `BrowserAI.Core.dll`, named `BrowserAI.TestProbe` so that Core admits it |
| `build/`, `get-registerai.log`, `registerai-stamp.json`, `take-lock.sh.txt` | How the product was built and the suite lock taken |
| `isolation/` | The SHA-256 of each client binary, and of the real client configuration before and after |
| `originals.sha256`, `left-out.sha256` | What was changed and what was left out one by one, each with its SHA-256 |

## What was changed, and what was left out

- **Every source file and script is stored with `.txt` appended**, as a record and not
  as this repository's code; `originals.sha256` names each one with the digest of what
  it was.
- **The profile path and the user name** are replaced by `%USERPROFILE%` and
  `%USERNAME%`, in the files beside this README under a `.trimmed.` name and inside
  `runs.zip` under the original name, with each original's SHA-256 in
  `originals.sha256`.
- **Left out by directory**, with no digest per file: the requests each client sent the
  local API stub, which carry its whole system prompt; the clients' scratch homes,
  11,978 files and 522,364,017 bytes under the Codex batches' `home` and 444 files and
  11,757,596 bytes under the Claude Code runs' `cfg`; the client-side logs, 361 files
  and 110,706,065 bytes; the `openai/codex` checkout at `rust-v0.160.0` that was read,
  443 files; build output, 36 files; the scratch app data the clients wrote, 172
  files; the project folder the clients ran in; and the void batches, `cc3`, which
  also connected the repository's own MCP servers and was re-run as `cc4` from outside
  the repository, the Codex smoke batch, a void load attempt, the first real-product
  Codex attempt with a relative `CODEX_HOME`, and the first suite run under load,
  which ran unlogged.
- **Left out one by one**, each in `left-out.sha256`: the load suite run's TUnit
  report, 18,649,062 bytes, and a draft of `HAZARDS.md` the lane kept beside its
  work.

## Privacy

Nothing here names the user profile or the machine. A scan for the profile path, the
user name, the machine name, the maintainer's e-mail addresses and the names of his
other projects found nothing in the files beside this README or inside `runs.zip`,
and its positive control found every planted needle. The API key the Claude Code
stubs saw is the literal `stub-key-not-real`.
