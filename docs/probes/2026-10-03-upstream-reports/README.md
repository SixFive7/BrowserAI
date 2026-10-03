<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 -- the probes behind the three upstream posts

Establishes the 2026-10-03 check in
[A headless Firefox that starts while Shift is held never finishes launching](../../../kb/playwright/provisioning-and-timings.md#a-headless-firefox-that-starts-while-shift-is-held-never-finishes-launching----measured-2026-09-25),
[Content in a closed shadow root is missing from the snapshot](../../../kb/playwright/tools-and-artifacts.md#content-in-a-closed-shadow-root-is-missing-from-the-snapshot----measured-2026-10-03),
and re-verification rows 168 and 174. Evidence:
[`docs/evidence/2026-10-03-upstream-reports/`](../../evidence/2026-10-03-upstream-reports/README.md).

**Why it exists.** Each upstream post had to stand on a reproduction run that
night against the builds it names, and not on an older reading.

## What is here

| File | What it does |
|---|---|
| `ff-safemode.cjs`, `run-ff.sh` | One headless Firefox launch per arm, persistent and not, with and without `MOZ_SAFE_MODE_RESTART=1` and `MOZ_DISABLE_SAFE_MODE_KEY`, recording the time and the process tree at 20 s; the script runs the arms one at a time for one Playwright and Firefox pair |
| `shadow-probe.cjs`, `shadow-exact.cjs` | The closed shadow root page, read through `ariaSnapshot()`, `getByRole`, CDP's accessibility tree and a mouse click; the second re-runs the exact page the comment quotes |
| `mcp-probe.cjs` | Two `@playwright/mcp` children: the second attaches to the first's browser by client name, a pause is armed in code, and a child with `--isolated` binds too. It refuses to start unless `LOCALAPPDATA`, `TEMP`, `TMP`, `PLAYWRIGHT_BROWSERS_PATH` and `PWTEST_SOCKETS_DIR` point into its scratch folder |
| `resume-idle.cjs` | A pending `browser_resume` against a short idle timeout |
| `addmcp-test.sh`, `addmcp-repro.sh`, `sandbox-env.sh` | add-mcp 2.4.1 against Claude Code with `CLAUDE_CONFIG_DIR` set, in a sandbox home; the second runs the issue's repro as written |
| `summ.py` | Prints the safe-mode arms from their `results.jsonl` |

## What keeps it off the rest of the machine

- Every browser is headless, from a scratch browsers folder, with a scratch
  registry and scratch temp folders. The real registry was listed by name before
  and after, and not written.
- The add-mcp runs used a sandbox home and a scratch `CLAUDE_CONFIG_DIR`, never
  the real ones.
- **It selects no process by image name.** Of the eight spellings
  [`ProcessSelection`](../../../tests/BrowserAI.Tests/Harness/ProcessSelection.cs)
  keys on, only `Win32_Process` appears, in `ff-safemode.cjs`, walked from the
  root pid the probe launched by parent pid.
- A Firefox run leaves five values under
  `HKCU\Software\Mozilla\Firefox\Launcher` for the scratch copy, which nothing in
  these files removes.

## Running it

Nobody ran these when this record was written; what follows is read from the
files. The scratch root is compiled in as
`C:\Source\SixFive7\BrowserAI\.work\upstream-drafts`, and each probe takes the
package or browser it drives as an argument or from its environment. A run is a
new measurement with a date of its own.

## How the stored copies differ from the ones that ran

The two SPDX lines were added to every file. `check-drafts.py`, the text check
the drafts were run through before posting, is not here, because the drafts are
not either.
