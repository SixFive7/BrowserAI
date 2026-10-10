<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-08 -- the Playwright installer that died with its own lock gone

What the corrected [hazard row](../../../HAZARDS.md#hazard-index) *an external deletion
of `__dirlock`, deleter not identified* rests on, and the measurement F the kb entry
[Two installers cannot extract into one root](../../../kb/playwright/provisioning-and-timings.md#two-installers-cannot-extract-into-one-root----upstreams-__dirlock----2026-08-19)
gained on 2026-10-10. On 2026-10-08 at 15:48:40Z, in the first-run arm of a gate on
`f68ae4cf`, Playwright's browser installer exited 1 with a stack through
`setLockAsCompromised`, and the record BrowserAI kept of it,
`BrowserProvisioner[64]`, held only the last 800 characters of what the installer
wrote, which began inside the first frame. A research agent of the root session then
reproduced the failure and searched the logs for what deleted the lock, on the night
of 2026-10-08 and the early morning of 2026-10-09. Both accounts are here as the agent
wrote them, with what each was read from.

| Path | What it is |
|---|---|
| `findings-repro.txt` | The reproduction and the first log check: three planted rounds that deleted `__dirlock` two seconds after the installer took it, and a control |
| `findings-deleter.txt` | The second log check: which processes could have deleted the lock, the product code that deletes under a browsers folder in both builds of that day, and the correction of the first check's lead |
| `repro/round-1.err` to `round-3.err`, `.out`, `.exitcode`, and `control.*` | Each round's installer output and exit code, BrowserAI's own payload run with Node v24.21.0 as `cli.js install-browser chromium --no-shell --no-progress` into a fresh root of its own |
| `repro/repro.log` | The driver's record of when each lock appeared, was deleted and the installer exited |
| `rig/run-repro.ps1.txt` | The driver, run under the suite lock |
| `rig/born-before.sh.txt`, `rig/*.py.txt` | What listed the test hosts alive at the moment the lock vanished, and the extractors of the log and transcript windows below |
| `log-check/timeline.txt`, `born-before.txt`, `tail-records.txt` | The window the lock was deleted in, 15:48:32Z to 15:48:37Z, every test host alive in it, and the first-run app root's own last records |
| `log-check/span-*.txt`, `window-*.txt`, `sess-*.txt`, `win-*.txt`, `source-excerpts.txt` | The log spans, the transcript windows of the lanes that were running, and the source excerpts the second check read |
| `originals.sha256` | Every file whose bytes were changed below, with the original's size and SHA-256 |

## How these bytes depart from the ones taken

- **The account's name is replaced** by `<user>` in the user profile's paths and as a
  file's owner; `originals.sha256` carries each original's digest and size.
- **No control byte is left.** The terminal's colour codes are removed from the four
  rounds' `.out` files, which are Playwright's coloured download lines, and the one
  other control byte, a `^A` a transcript extract had turned into byte 0x01, is
  written as `<0x01>`. Both are counted in `originals.sha256` with the files above.
- **The two findings and the scripts are stored as text** under a `.txt` name.
- **Not here:** a copy of `HAZARDS.md` the agent kept to compare against, and its
  draft of the hazard row. The row as written is in the hazard index.
- Everything else is as written, line endings aside.
