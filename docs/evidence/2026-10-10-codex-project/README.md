<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-10 -- a committed Codex project entry, at codex-cli 0.162.0-alpha.2

**Which spelling of a Codex project entry, committed to a repository, starts BrowserAI on
every developer's PC**, for the maintainer's decision of 2026-10-10, verbatim: *"21
refusing installing into a non-standard folder so the project specific setups always
resolve on every dev's pc."* Measured on this machine between 12:59Z and 14:03Z,
Windows 11 Pro 10.0.26300, against codex-cli **0.162.0-alpha.2**, the CLI inside the
Codex desktop app 26.1002.7124.0, with one round against **0.159.0-alpha.12.1**, the CLI
at `~\.codex\plugins\.plugin-appserver\`, and six checks of Claude Code **2.1.296**.
Every Codex and Claude Code call ran under a scratch `CODEX_HOME` and
`CLAUDE_CONFIG_DIR`, with a scratch `LOCALAPPDATA` and `USERPROFILE`, and no BrowserAI,
browser or installer was started: every server is a stand-in named `BrowserAI.exe`.

Cited by [kb](../../../kb/mcp/protocol.md#codex-expands-nothing-in-a-servers-command-and-finds-a-bare-name-on-the-servers-path----measured-2026-09-24),
[re-verification row 161](../../../kb/re-verification.md) and the two corrections in
[`2026-09-24-codex-expansion`](../2026-09-24-codex-expansion/README.md), the batch this
one measures again.

## What it found

| The project entry | Started | What the server received |
|---|---|---|
| A variable in `command`: `${LOCALAPPDATA}`, `$LOCALAPPDATA`, `%LOCALAPPDATA%` and `~`, each with `/` and `\`, at project and at user scope | **0 of 48**, and 0 of 8 at 0.159; each *"MCP startup failed: The system cannot find the path specified. (os error 3)"* | |
| The bare `BrowserAI.exe`, the install on Codex's own PATH, which is Q294 b's entry | 24 of 24; **0 of 6 for an app-server started before the install**, *"MCP startup failed: program not found"*; and **the other copy, 3 of 3**, when another `BrowserAI.exe` came first on that PATH | the arguments the entry configures |
| The same name with the entry's own `env = { PATH = '~\AppData\Local\BrowserAI.app\current' }` | **33 of 33**, the install's copy each time: an app-server started before the install, another copy first on Codex's PATH, and a space and `& ! ^ % ( )` in the profile path | the arguments the entry configures, under a PATH of that one entry as written |
| `cmd.exe /d /v:on /c !LOCALAPPDATA!\BrowserAI.app\current\BrowserAI.exe --mcp` | 27 of 27 | **a split argument list wherever the profile path holds a space**, 6 of 6 |
| A committed `.codex/browserai.cmd`, named relatively | 15 of 15 for a thread at the project root; **0 of 3 for a thread in a subfolder with the app-server outside the project** | the arguments the entry configures |

**A project's entries load only in a trusted project, and a full-access app-server trusts
one itself.** No trust entry under a read-only sandbox: 0 of 12 entries loaded;
`trust_level = "untrusted"`: 0 of 12; workspace-write requested on a machine with no
Windows sandbox set up: 0 of 12. No trust entry under full access: the app-server wrote
`trust_level = "trusted"` for the project's folder into the scratch home's `config.toml`
at `thread/start`, 3 of 3, and loaded 12 of 12. **No form opened a window**: no process of
any app-server tree owned a top-level window in 48 sessions. **Claude Code 2.1.296
expanded `${LOCALAPPDATA}` in a project `.mcp.json` command**, 6 of 6 connected, 3 of
them with a space in the expanded path.

The launch chain is byte-identical at the measured build's tag `rust-v0.162.0-alpha.2`,
at the newest release `rust-v0.162.1` and at the newest pre-release
`rust-v0.163.0-alpha.5`, so this run speaks for those two by source identity and not by
a run of them. `findings.txt` has the file and line of every source reading.

## How it was measured

`rig/` drives `codex app-server --listen stdio://` with a scratch `CODEX_HOME` per run
and a model provider at `127.0.0.1:9`, and starts no turn, so no model was asked
anything. Every configured server is `stub/ProbeStub.cs`, built as a Windows-subsystem
`BrowserAI.exe` the way the real one is, with a console-subsystem copy as the control;
it records its image, command line, arguments, folder and parent, and answers one tool,
`probe_whoami`. Each session sends `initialize` and `thread/start`, waits for each
server's startup status, takes a process-tree census and a window census, asks for the
status list with and without the thread, calls `probe_whoami` on every server that is
ready, then closes the app-server's input and ends what is left of its own tree, by pid.
`rig/matrix.sh`, `matrix2.sh` and `matrix4.sh` ran three rounds against 0.162.0-alpha.2,
`matrix3.sh` one round against 0.159.0-alpha.12.1, and `rig/claude-check.js` the six
Claude Code checks; `rig/summarize.js` wrote the summaries. Processes were read from one
read of the process table, reduced to the app-server's pid tree or the stand-in's
scratch paths, and windows were counted by `rig/WinProbe.cs` keyed by owning pid, whose
positive control found a window of its own that is never shown.

## Files

| Path | What it is |
|---|---|
| `findings.txt` | The account written when the measurement was taken: the versions and where each was read, the source readings at three tags with file and line, the issues and the documentation read, every case and its result, the corrections to the 2026-09-24 batch, and what could not be established |
| `summary.txt`, `summary.json` | The 48 sessions at 0.162.0-alpha.2, case by case |
| `summary-codex-0.159.txt`, `summary-codex-0.159.json` | The 9 sessions at 0.159.0-alpha.12.1 |
| `summary-claude.txt` | The six Claude Code checks |
| `rig/*.txt` | The rig, each script under its own name with `.txt` appended |
| `stub/ProbeStub.cs.txt` | The stand-in server |
| `raw/` | The versions read, GitHub's release list, the issues and searches, the matrix and trial logs, the desktop bundle's excerpts, the totals, and the check at 14:03:44Z that no app-server and no stand-in was left running |
| `originals.sha256`, `left-out.sha256` | The SHA-256 and byte count of every file as taken, and of every file left out one at a time |

## How these bytes depart from the ones taken

- **The rig and the stand-in are stored as text** under a `.txt` name, so that nothing in
  the suite that reads this repository's code reads them as its own. `lib.js.txt` points
  at a `FINDINGS.md`, which is `findings.txt` here.
- **`findings.txt` was never a file before this one.** The account was returned as text,
  and it is stored as returned with the delivery's HTML escaping of `<`, `>` and `&`
  undone, without the opening and closing parts about who did the work and what was left
  to whom, and with three cuts inside it: the first sentence named the part of the build
  that measured it, one heading named who would decide, and the path of another
  application's own copy of Codex is cut to `<another application>`. `originals.sha256`
  gives the SHA-256 of sections 1 to 10 as returned, before the cuts.
- **The same folder name is cut** in `raw/versions.json` and `rig/versions.js`, stored as
  `raw/versions.trimmed.json` and `rig/versions.trimmed.js.txt`.
- **`raw/issue-2680-body.md` is stored as `raw/issue-2680-body.md.txt`**: it is the issue's
  body as GitHub returned it, and under its own name this repository's rules for its own
  Markdown would read it.
- **Left out by directory**: `runs/`, 66 run folders and 184 MB, because every record in
  them carries this machine's PATH uncut, which is what the summaries cut it down from;
  `src/`, sparse clones of openai/codex at the three tags and the 0.155 files, 365 MB,
  where the tags and commits in `findings.txt` are the record; `crates/`, `which` 8.0.0
  and `env_home` 0.1.0 as crates.io publishes them, with their digests in `findings.txt`;
  and the scratch profiles, homes and copies of the stand-in the runs used.
- **Left out one at a time**, with their digests in `left-out.sha256`: the two builds of
  the stand-in and the window counter, which build from the sources here;
  `raw/selftest-gui/`, the stand-in's record of the window counter's positive control,
  which carries this machine's PATH; and `raw/summarize-newest.out`, byte-identical to
  `summary.txt`.
- Line endings are this repository's, LF, as [the index](../README.md) says of every
  batch.
