<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-04 -- a windowless MCP server under both clients, and the start through the real Task Scheduler

**What this is.** The two measurements the one-windowless-binary plan of 2026-10-04
stood on, taken 2026-10-04 between 01:19Z and 02:21Z on Windows 11 Pro 10.0.26300 by
the lane that was asked whether the plan's two premises hold. **Measurement 1**: a stub
built from BrowserAI's own stdio code, published NativeAOT as a windowless program and
as a console one, served Claude Code 2.1.288 (`-p` and the VS Code stream-json
transport) and codex-cli 0.155.0-alpha.9.2 and 0.160.0 (`exec` and `app-server`), 54
runs, 3 per cell, with a desktop-wide window watch; and the windowless stub started
with no standard handles, 6 runs. **Measurement 2**: the suite's test pack of
BrowserAI 1.1.1-alpha.0.197, both programs from `e30380ac`, installed into a scratch
root, its front starting the coordinator through the install's own logon task, 12
counted runs over four client exits, and the takeover of the kept session by a second
client. **189 files beside this README, 1,976,349 bytes as this repository stores them**, of which `runs.zip` is
973,332. Every client ran under a scratch configuration against a local API stub, and
the installed BrowserAI 1.1.0 was never touched.

The lane's own report could not be written to a file by the lane, so it survives as
the report the root session saved, `.work\reports-2026-10-04\onebinary-measure.md`,
which is scratch; what a record rests on is in the kb entries below.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: protocol](../../../kb/mcp/protocol.md) | "A windowless server under both clients": every figure of measurement 1 |
| [kb: processes](../../../kb/windows/processes.md) | "A windowless program started with no standard handles reads end of input at once", and "The real programs through the real Task Scheduler" |
| [kb: re-verification](../../../kb/re-verification.md) | The rows those entries carry |
| [the one-binary design](../../design/one-binary/README.md) | Weakest points 3, 4 and 11, the modes, the five risks |

## What is here

| Path | What it is |
|---|---|
| `runs/m1/cells.tsv`, `runs/m1/runs.tsv`, `runs/m1/batch.json` | Measurement 1, the client cells that ended normally or by stdin: one row per cell and per run, with the subsystem, what was served, the 70 KB check, what the model got, stderr, the exit code, the killer, every timing, the window counts, the cursor and the start flags |
| `runs/m1k/...` | The same for the killed-client cells |
| `runs/nohandles/` | The six starts with no standard handles, with the stub's own report of each |
| `runs/feedback1/` to `feedback3/`, `runs/smoke/` | The feedback-cursor launcher runs and the smoke run |
| `runs/m2b/m2-runs.tsv`, `progress.log`, `page.log`, `policy.json` | Measurement 2, counted batch `m2b`: one row per run |
| `runs/m2-watch/alive.json` | Which processes were alive 6 s after each client exit |
| `runs.zip` | **Every run of `m1`, `m1k` and `m2b` and the root watch's logs, 631 files**, each run's harness log, result, stub logs and reports, the configuration the client was handed and what the model saw, with the client-side logs and the clients' scratch homes left out (below); SHA-256 `bbfe4afc2ce8556304408c79cc772b516086b0a406cea1a7330b068d698b8a76` |
| `m2-snap/{before,installed,before-uninstall,after}/` | The read-only clearance snapshots: the uninstall keys, the real client registrations by hash, the Start Menu, the user PATH by hash, the BrowserAI processes, the task list's BrowserAI entries |
| `m2-logs/` | The installer's and the uninstaller's logs, and the product's own log lines of the 57 test-install processes |
| `logs/` | The publish, pack and build logs, and the second analysis's output |
| `stub/` | The stub: BrowserAI's five stdio files copied byte for byte and the stub's own `Program.cs` and project |
| `rig/`, `apistub/` | The rig: the client-exit harness this lane extended (`WindowWatch`, `RootWatch`, `Feedback` and `Launch` are its additions), measurement 2's drivers and snapshot scripts, the two analyses, and the local API stubs |
| `code/releases.win.json`, `m2-emptyfeed/` | The production feed as read on 2026-10-04, and the empty local feed every front was given |
| `isolation/` | The SHA-256 of each client and stub binary, and of the real client configuration before and after |
| `originals.sha256`, `left-out.sha256` | What was changed and what was left out one by one, each with its SHA-256 |

## What was changed, and what was left out

- **Every source file and script is stored with `.txt` appended**, `Program.cs.txt`
  for `Program.cs`, so that nothing here is built, scanned as this repository's own
  code, or held to its source-file rules; `originals.sha256` names each one with the
  digest of what it was.
- **One directory is renamed**: `runs/smoke/con` is stored as
  `runs/smoke/console-form`, because `con` is a name Windows reserves for a device and
  git cannot open a file under it.
- **The profile path and the user name** are replaced by `%USERPROFILE%` and
  `%USERNAME%` wherever they appeared, in the files beside this README under a
  `.trimmed.` name and inside `runs.zip` under the original name; `originals.sha256`
  carries each original's SHA-256.
- **Left out by directory**, with no digest per file: the clients' scratch homes, 3,852
  files and 186,665,435 bytes under the Codex runs' `home` and 181 files and 9,058,441
  bytes under the Claude Code runs' `cfg`; the client-side logs, 296 files and
  51,008,356 bytes of `client-stderr.log`, `client-stdout*.log` and
  `claude-debug.log`, most of it Codex trace output; build output, 268 files and
  298,879,988 bytes; the scratch app data, 35 files and 132,777 bytes; the smoke batch
  before measurement 1, 51 files; the first measurement-2 attempt, 31 files, void
  because its page server lived inside the driver; an empty project folder, 18 files;
  the install hook's scratch client homes, 5 files; and a version probe's scratch, 3
  files.
- **Left out one by one**, each in `left-out.sha256`: the three Velopack 1.2.161
  source files that were read for risk 6, which are a third party's code and can be
  read again at the tag; the six hash lists of the real `~/.codex` tree, which name
  the maintainer's own files; the four copies of the full scheduled-task list, which
  name other programs on the machine; and the empty `.out` files of the batch
  drivers.

## Privacy

Nothing here names the user profile or the machine. A scan for the profile path, the
user name, the machine name, the maintainer's e-mail addresses and the names of his
other projects found nothing in the files beside this README or inside `runs.zip`,
and its positive control found every planted needle. The names that remain are the
maintainer's as publisher, which every installer and SPDX header in the repository
already carries, and the API key the stubs saw, the literal `stub-key-not-real`.
