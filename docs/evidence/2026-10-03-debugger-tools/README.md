<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 -- a pause armed from inside a session, every way out of it, and the 72-tool audit (Q321)

**What this is.** What the debugger-tools rig recorded on 2026-10-03: nineteen
scenarios, three runs per family, that arm a debugger pause in an
`@playwright/mcp` child's browser and try each way out of the call it parks;
plus the tool lists of 0.0.82 and 0.0.83, their difference, and an audit of
every tool's verdict. Taken at `@playwright/mcp` **0.0.82** (`playwright-core`
1.64.0-alpha-1789764292000) from the payload, with 0.0.83 installed into scratch
for its tool list only, Chromium and Firefox headless, on Windows 11. **24 files
beside this README, 1,534,895 bytes as cut**, of which `runs-payload.zip` is
941,579. The rig is a probe record at
[`docs/probes/2026-10-03-debugger-tools`](../../probes/2026-10-03-debugger-tools/README.md).

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: tools and artifacts](../../../kb/playwright/tools-and-artifacts.md#a-pause-armed-from-inside-a-session-and-every-way-out----measured-2026-10-03) | Every number in the entry. The review of 0.0.83 re-took the resume and the armed close in its own batch, [`2026-10-03-debugger-0.0.83`](../2026-10-03-debugger-0.0.83/README.md), and cites this one for the child's exit on a closed stdin |
| [kb: re-verification](../../../kb/re-verification.md) | Row 172 |
| [kb: what is not established](../../../kb/not-established.md) | The dashboard section's row on arming a pause from inside a session, now closed, and the rows this batch leaves open |

## What is here

| Path | What it is |
|---|---|
| `tools/payload-0.0.82-agg.tsv` | **The result**: one line per scenario, family and step, with how many of three answered, were errors or showed `### Paused`, the latency range and the distinct answer texts |
| `tools/payload-0.0.82-runs.tsv`, `payload-0.0.82-steps.tsv` | The same per run and per step |
| `tools/audit-72.tsv` | Every tool of the golden snapshot: its capability, its verdict today, whether 0.0.83 has it, what it overlaps in BrowserAI, a note and the researcher's recommendation. A recommendation is not a decision |
| `tools/tools-0.0.82.json`, `tools-0.0.83.json`, `diff-082-083.tsv`, `diff-082-083.json` | Both tool lists as the children answered them, and their difference: two input schemas changed, `browser_find` and `browser_wait_for` |
| `runs-payload.zip` | Every run, 1,578 files, byte for byte: each `result.json`, the child's log, its output directory (snapshots, console logs, the traces of the `artifacts` scenario and their screencast frames) and its config; SHA-256 `c1eaffa6ebdacac2e724dc8ad70fc6a74b74788d75d574f7ded5fdf7f563f25e` |
| `logs/` | Each batch's log |
| `left-out.sha256` | What was left out whole, with digests |

## What was cut, and what was left out

- **The first sitting, `runs\invalid-0.0.82-no-setTimeout`**: its arming code
  called `setTimeout`, which the `browser_run_code_unsafe` sandbox does not
  define, so it measured nothing. Its digests are in `left-out.sha256`.
- **Three intermediate tables**, `partial-*.tsv`, which the final ones replace.
- **The scratch installs**: `@playwright/mcp` 0.0.83, the npm cache, the scratch
  registry and app-data folders, and the read-only baseline snapshots of the
  real Mozilla folders, which name the user's own profiles.

## Privacy

No file here names the user, the user profile or the machine. The privacy scan
found only names of the form `page@<hash>` in screencast file names, which are
not addresses; its positive control found all nineteen planted needles.
