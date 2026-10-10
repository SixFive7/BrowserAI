<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-10 -- when RegisterAI's output ends, against when it exits

**Does anything hold RegisterAI's output pipe open after it exits, when it registers
BrowserAI with Codex?** Asked for [the hazard row](../../../HAZARDS.md#hazard-index) of a
red in the gate at `a557aa0f` that day, in which BrowserAI read no document from a
RegisterAI that had exited 0 and written its entry. Measured on this machine between
14:41Z and 14:44Z, Windows 11 Pro 10.0.26300, with the payload's RegisterAI 0.3.0 and the
Codex it found, codex-cli 0.159.0-alpha.12.1 at `~\.codex\plugins\.plugin-appserver\`,
each run under a scratch `CODEX_HOME` and `CLAUDE_CONFIG_DIR`, started by the probe and
not inside a test host.

`probe.py.txt` starts `RegisterAI.exe register --name browserai --client codex --scope user`
with both output pipes redirected and no window, reads each pipe on a thread of its own,
and times each pipe's end against the process's exit. A pipe still open half a second
after the exit would have had the processes below RegisterAI's pid read from the process
table; none was.

| Round | Runs | Both pipes ended | Document |
|---|---|---|---|
| 1, 14:41Z | 10 | at RegisterAI's exit, 10 of 10 | 2,724 bytes that parse, 10 of 10 |
| 2, 14:43Z | 10 | at RegisterAI's exit, 10 of 10 | 2,724 bytes that parse, 10 of 10 |
| 3, 14:43Z | 10 | at RegisterAI's exit, 10 of 10 | 2,724 to 2,726 bytes that parse, 10 of 10, each kept |

Each run took 229 to 256 ms. **So nothing outlived RegisterAI holding its output, 30 of
30**, with no test host around it; that it never does under a full run's load is not shown.

## Files

| Path | What it is |
|---|---|
| `probe.py.txt` | The probe as it ran in round 3; rounds 1 and 2 ran it without the two lines that keep each document and the suffix to its scratch folder |
| `round-1.log`, `round-2.log`, `round-3.log` | One line per run and the round's total |
| `round-3-document-01.json` | The document of round 3's first run, as RegisterAI printed it |
| `originals.sha256` | The SHA-256 of the probe under its own name |

The probe is stored as text under a `.txt` name, so that nothing in the suite that reads
this repository's code reads it as its own. Line endings are this repository's, LF.
