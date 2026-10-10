<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-10 -- Claude Code and a bare `BrowserAI.exe` in a project entry

**Does Claude Code start a project's `.mcp.json` entry that names the bare `BrowserAI.exe`
through the PATH it was started with?** For the maintainer's 30 of 2026-10-10, verbatim in
part: *"So I'd like to go for BrowserAI.exe as a bare name in both Codex and Claude code for
cleanliness an beautiy sakes."* Measured on this machine from 16:45:45Z to 16:46:26Z,
Windows 11 Pro 10.0.26300, Claude Code **2.1.296**, under a scratch `CLAUDE_CONFIG_DIR`,
`CODEX_HOME`, `LOCALAPPDATA` and `USERPROFILE` per run, with the auto-updater and the
non-essential traffic off and the repository's own four project servers refused. Every
server is the stand-in of [`2026-10-10-codex-project`](../2026-10-10-codex-project/README.md),
named `BrowserAI.exe` and built as a Windows-subsystem binary.

Cited by [kb](../../../kb/mcp/protocol.md#claude-code-finds-a-bare-name-in-a-project-entry-on-the-path-it-was-started-with----measured-2026-10-10),
[re-verification row 211](../../../kb/re-verification.md) and
[`DECISIONS.md`](../../../DECISIONS.md#the-answers-of-the-afternoon-of-2026-10-10).

## What it found

Each run wrote a project `.mcp.json` with the bare entry, `browserai`, and an absolute
control, `abs`, approved both in the scratch user settings, and ran `claude mcp list`, which
starts every server to check it, and `claude mcp get browserai`.

| Kind | Claude Code's own PATH | The bare `browserai` | The control |
|---|---|---|---|
| `ns`, `sp`, `spx` | ends with the install's `current\` folder, the profile spelled `user`, `Jo Smith` and `Jo Smith & Co (!x^ 100%)` | connected 9 of 9, the install's copy each time, the arguments as configured | 9 of 9 |
| `before` | does not hold the folder, as for a Claude Code started before the install | *Failed to connect*, *CONNECTION_CLOSED: Connection closed*, 3 of 3, nothing started | 3 of 3 |
| `shadow` | holds another `BrowserAI.exe` first, then the install's folder | connected 3 of 3, **the other copy** each time | 3 of 3 |

No stand-in had a console window, 39 of 39 launches by its own reading. The process-tree
census the rig took once a stand-in had written its launch record came after the stand-in had
gone and saw nothing, so it says nothing about windows.

## Files

| Path | What it is |
|---|---|
| `rig/claude-bare.js.txt` | One run: the scratch homes, the project file, the two Claude Code commands, the census and the record |
| `rig/lib.js.txt`, `rig/WinProbe.cs.txt` | The helpers, copied from the Codex measurement with its scratch folder renamed, and the window counter |
| `rig/matrix.sh.txt` | Five kinds, three rounds |
| `stub/ProbeStub.cs.txt` | The stand-in server |
| `raw/matrix.log` | One block per run: what `claude mcp list` said and every launch the stand-in recorded |
| `runs/<run>/result.json` | Everything one run recorded, the stand-in's launches with the scratch folder cut to `<SCR>` |
| `originals.sha256`, `left-out.sha256` | The SHA-256 and byte count of every file as taken, and of the two builds left out |

## How these bytes depart from the ones taken

- **The rig and the stand-in are stored as text** under a `.txt` name, so that nothing in the
  suite that reads this repository's code reads them as its own.
- **Left out by directory**: each run's `stublogs/`, whose launch records carry the server's
  environment and with it this machine's PATH uncut; each run's `claude-config/`, the files
  Claude Code wrote into its scratch home, and its `proj/` and `codex-home/`, whose project
  file `result.json` repeats; and the scratch profiles and the other copy of the stand-in.
- **Left out one at a time**, with their digests in `left-out.sha256`: the stand-in's build
  and the window counter's, which build from the sources here.
- Line endings are this repository's, LF, as [the index](../README.md) says of every batch.
