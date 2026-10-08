<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

The measurement is done: all five clients can be told apart, but only by combining what the client sends at the handshake with the command line of the process that started the server. The protocol alone cannot split the three Claude Code modes, and nothing on the wire or in the environment splits Codex exec from Codex app-server.

**Runs and versions.** 41 runs started the stub and are counted: 21 Claude Code, 20 Codex. At least 3 per client, plus extra arms that test the weak spots (listed under "Arms" below the table). One more Codex terminal UI run is not counted because its background server never started (path too long, explained under "Other findings").
- **Claude Code 2.1.294**: the installed CLI. npm read today: `next` 2.1.294, `latest` 2.1.293, `stable` 2.1.285.
- **Claude Code 2.1.292**: the newest installed VS Code extension's own bundled binary.
- **codex-cli 0.161.0**: npm `latest`, read today.
- **codex-cli 0.159.0-alpha.12.1**: the binary the running Codex desktop app uses as its app-server (byte-identical, SHA-256 `1722907a...`).

No real model turns. `-p` and `codex exec` each made one turn against a local stub; the terminal UI, the stream-json transport and the app-server needed none.

Arms in the table, beyond the five:
- **(1) terminal UI**: 4 clean runs, plus 3 with `CLAUDE_CODE_ENTRYPOINT=claude-vscode` already set in the environment that started it ("inherited").
- **(3) `claude -p`**: 4 from pipes, 3 typed in a terminal (a pseudoconsole), and 3 with the environment a Bash tool inside a VS Code session would pass on (nested).
- **(5) app-server**: 4 with 0.161.0 and 4 with the desktop app's binary.

## Signals per client

| Signal | (1) Terminal UI | (2) VS Code transport | (3) `claude -p` | (4) `codex exec` | (5) `codex app-server` |
|---|---|---|---|---|---|
| clientInfo name / title | `claude-code` / `Claude Code` 4/4 | same 4/4 | same 10/10 | `codex-mcp-client` / `Codex` 4/4 | same 8/8 |
| clientInfo version | 2.1.294 | 2.1.292 (extension's own binary) | 2.1.294 | 0.161.0 | 0.161.0 / 0.159.0-alpha.12.1 |
| protocolVersion, capabilities | `2025-11-25`, roots + elicitation (same in all 21 Claude Code runs) | same | same | `2025-06-18`, `codex/auth-change` + elicitation (same in all 20 Codex runs) | same |
| Messages received | `server/discover` probe, then `initialize`, `tools/list` (21/21); `roots/list` answered with the cwd | same | same | `initialize`, `tools/list`; no roots | same |
| `CLAUDE_CODE_ENTRYPOINT` | `cli` 4/4; an inherited value is kept: `claude-vscode` 3/3 | `claude-vscode` 4/4 (the extension sets it) | `sdk-cli` 7/7; nested: `claude-vscode` 3/3 | not set | not set |
| Variables the client adds | `AI_AGENT=claude-code_<ver>_harness`, `CLAUDECODE=1`, session id, messaging pipe and token, `CLAUDE_PROJECT_DIR`, `SHELL`, two minor ones; identical in (1), (2), (3) | same | same | none; only an allowlist of 19 passes, a marker variable is dropped 20/20 | same |
| Starting environment passed through | everything, marker 21/21 | everything, including `MCP_CONNECTION_NONBLOCKING`, `CLAUDE_AGENT_SDK_VERSION`, `VSCODE_*` | everything | no | no |
| Parent process and its arguments | `claude.exe`, no arguments 7/7 | `claude.exe --output-format stream-json --verbose --input-format stream-json ...` 4/4 | `claude.exe -p ...` 10/10 | `codex.exe exec ...` 4/4 | `codex.exe app-server` 8/8 |
| Console, startup flags, job object, pipes | No console window in any run (41/41). Claude Code: flags `0x101` hidden, server in `claude.exe`'s job, named pipes `\uv\N-<claude pid>`, stdin closed on exit 21/21 | same | same | Codex: flags `0x100`, its own kill-on-close job per server, unnamed pipes, server terminated (no EOF) 20/20 | same |

Every row below the first two is identical within a family. Only clientInfo, `CLAUDE_CODE_ENTRYPOINT` and the parent's arguments carry information about which mode it is.

**What cannot be told apart without reading the parent:**
- **(1), (2) and (3)** are identical on the wire in 21/21. The version differs only because the extension bundles its own binary, so it is not a signal.
- **`CLAUDE_CODE_ENTRYPOINT`** separates them in the 15 clean runs, but reads `claude-vscode` in 6/6 inherited runs. Claude Code keeps a value that is already set (read in the 2.1.294 bundle: only an inherited `cli` is rewritten to `sdk-cli`). For a terminal UI that is the harmful direction: it would tell the person nothing is needed when they must reconnect.
- **(4) and (5)** are identical on the wire and in the environment in 20/20. The host's own clientInfo (`idrig_host`) is not forwarded to the server, 8/8.

## Recommended test for the relay

At startup, read the client name from clientInfo. Then read the parent process: its pid, keep it only if it was created before the relay, and its command line through `NtQueryInformationProcess` class 60 with limited-query access, split by Windows argument rules.

- **`claude-code`**:
  - None of `-p`, `--print`, `--input-format`, `--output-format`, `--sdk-url`, `--init-only` present: **(1) terminal UI**.
  - `-p` or `--print` present: **(3)**.
  - Otherwise, entrypoint `claude-vscode`: **(2)**.
  - Otherwise: another headless host, named by its entrypoint value.
  - If the parent cannot be read, fall back to the entrypoint (`cli` = 1, `sdk-cli` = 3, `claude-vscode` = 2), knowing about the inherited-value misread.
- **`codex-mcp-client`**: take the first argument that is not an option, skipping the values of `-c`, `--enable`, `-m`, `-p`, `-C` and similar.
  - `exec`, `e` or `review`: **(4)**.
  - `app-server`: **(5)**, unless `--managed-daemon` is present, which means the Codex terminal UI's shared background server.
  - No subcommand: the Codex terminal UI started with `--no-daemon`.
- **Any other name**: unknown client, name both remedies.

**Score.** Correct in 41/41 runs (script: `rig\classify.js`). It also classified two live processes on this machine correctly:
- This VS Code session's `claude.exe` (pid 40232, extension 2.1.288): (2).
- The running Codex desktop app-server (pid 15308): (5). Its real command line is `-c features.code_mode_host=true app-server --analytics-default-enabled -c ...`, so the subcommand is the third argument, which is why options must be skipped.

The environment-only test scores 15/41.

## What it cannot tell
- **Hosts I did not run**: the Agent SDK (`sdk-ts`, `sdk-py`), the Claude desktop app, JetBrains, the Codex IDE extension. Their re-launch behaviour is unknown.
- **The relay must do the reading itself**: the test assumes the server's parent is the client. That held in 37/41; the four exceptions are the Codex terminal UI, whose parent is the shared background server. If BrowserAI's relay starts a child server, the relay has to identify the client and pass the result down.
- **Shared Codex background server**: a server under it cannot tell which terminal session, if any, is attached.
- **Flags are not a contract**: the test relies on client command-line flags, which can change in any release.

## Other findings
- **Codex 0.161.0's terminal UI keeps servers alive after it closes.** By default it runs servers inside a background server it installs under `CODEX_HOME`. After `/quit` the stub stayed alive 30.1 to 30.2 s, until `codex app-server daemon stop` (3/3). With `--no-daemon` the server ended 0.11 to 0.14 s before the terminal UI did (3/3). A helper process (`codex app-server daemon pid-update-loop`) survives even `daemon stop` (4/4). This bears on whether an update is held after the person closed Codex; I only measured 30 seconds.
- **Long `CODEX_HOME` paths break the background server**: it fails with "path must be shorter than SUN_LEN". That is the uncounted run.
- **Claude Code 2.1.292 and 2.1.294 probe with `server/discover` first** (protocol `2026-07-28`, clientInfo in `_meta`) and fall back to `initialize` (21/21). I did not measure how BrowserAI answers that probe.
- **Every Claude Code server receives a messaging pipe and its token** (`CLAUDE_CODE_MESSAGING_SOCKET` / `_TOKEN`), so BrowserAI must never log its environment.

## Decision for you: which test the relay applies
The test needs a Win32 read of the parent and depends on undocumented flags, so it is a design choice. Options:
- **a.** clientInfo plus the parent's command line: 41/41.
- **b.** clientInfo plus the entrypoint variable only: wrong for an inherited terminal UI (3/3), cannot split Codex.
- **c.** clientInfo only: never wrong, but Claude Code sessions get both remedies.
- **d.** a, falling back to b when the parent cannot be read, then to c.

I recommend **d**. For Claude Code hosts that were not measured, I recommend naming both remedies until someone runs them.

## Deviations and checks
- **(2) was not driven through VS Code**, because nothing may show a window. I ran the extension's own 2.1.292 binary with the argument list read off the live extension session, the environment its `extension.js` sets, and the extension host's variables copied from this session, minus `VSCODE_PID` and `VSCODE_IPC_HOOK`, which point at the real VS Code.
- **No lock was needed**: nothing started a browser or BrowserAI.
- **Leftover processes**: the only processes still alive after the runs were four `pid-update-loop` helpers my Codex runs started (39868, 87876, 30052, 85236). I ended each by its recorded pid after checking its creation time and that its image was under scratch. A final query that proved it can see a live scratch process now finds none, and none of the 9 recorded pids (the four helpers and five API stubs) exists.
- **Real configuration untouched**: `~/.claude.json`, `~/.claude/settings.json`, `~/.codex/config.toml` and Codex's real logs, state and model cache contain no run marker (0 hits each; the same search finds 3 in scratch copies). The real `~/.codex` files that changed during the runs were written by the Codex desktop app, which has been running since 2026-10-03. That attribution is an inference: they carry none of my markers. The installed BrowserAI's logs were read only.
- **Window check**: every process the harness created had no window, and every server had none (41/41). I could not watch the screen, so a brief flash from a process a client itself started is not ruled out.
- **Scratch**: everything is under `C:\Source\SixFive7\BrowserAI\.work\client-id\` (1.4 GB, mostly client binary copies). I deleted the Codex background-server package copies, the npm cache and the tarball. The evidence exists only in `.work`; cutting it into `docs/evidence` is your call.

Files are in `C:\Source\SixFive7\BrowserAI\.work\client-id\`:
- `rig\IdRig\IdStub.cs` (the stub)
- `rig\classify.js` (the test, run over every run)
- `runs\all-runs.tsv` (one row per run)
- `runs\all-arms-summary.txt` (every signal, per arm)
- `runs\<batch>\<arm>-r<n>\` (raw per-run captures)
