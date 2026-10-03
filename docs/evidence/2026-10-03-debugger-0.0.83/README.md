<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 - what a debugger pause does to a session at `@playwright/mcp` 0.0.83

**Taken in the batch that reviewed the `@playwright/mcp` 0.0.82 -> 0.0.83 roll**,
because the same batch denied `browser_resume`, and the reason that deny gives a
caller is a measurement: it had to be measured on the version that ships. A
research rig written the same night measured these on 0.0.82; this batch ran that
rig, with its scratch paths moved, against the reviewed payload. What it found is in
[kb: `browser_resume` releases the parked call and then parks itself](../../../kb/playwright/tools-and-artifacts.md#browser_resume-releases-the-parked-call-and-then-parks-itself)
and the entry after it, in
[re-verification rows 164 and 167](../../../kb/re-verification.md), in the
[hazard index](../../../HAZARDS.md#hazard-index), and in `browser_resume`'s
`why` in [`tool-verdicts.json`](../../../tool-verdicts.json).

## What is here

| File | What it is |
|---|---|
| `runs/<scenario>-<family>-<n>.json` | Eighteen runs, each the rig's own `result.json`: per step, whether a call answered, how long it took, whether it was an error and the head of its text. Three scenarios, two families, three runs each |
| `aggregate.tsv` | One line per run, the fields the kb and the review cite, as `aggregate.cjs.txt` printed them |
| `rig.cjs.txt`, `supervise.ps1.txt`, `batch.ps1.txt` | The rig as it ran: one `@playwright/mcp` child over stdio configured the way BrowserAI configures one (a generated config, `--sandbox`, the allowlisted environment), inside a job object the supervisor creates, one browser at a time |
| `aggregate.cjs.txt` | The script that wrote `aggregate.tsv` |

**The scenarios.** `resume`: a pause armed from `browser_run_code_unsafe`, a
navigation that meets it, then `browser_resume`, then `browser_close`. `armedcloseresume`:
the same pause, but the first call to meet it is a `browser_close`, which is what
BrowserAI's own idle close sends; then a snapshot, a second close and finally
`browser_resume`. `idleresume`: the `resume` scenario with upstream's idle timer
set to 12 s, to see what ends a pending `browser_resume`.

## How these bytes differ from what was taken

- The four scripts carry this repository's two-line SPDX header, prepended, and
  are stored as `.txt` so that nothing here is mistaken for a maintained tool. Their
  digests as they ran, before the header: `rig.cjs`
  `cbec42f0d603e38634158ec155bfe5bb3ad6b9116540692da2b26227cb0c3b0f`,
  `supervise.ps1` `6c7f34b2aa43b11169b408ca836dcc5c20f4e8e349acbd6d5a5ab3b1d1eb5feb`,
  `batch.ps1` `6e473bc99846eaa5c19eb0b00597127a392e5b7cd3152ee941f41da103303808`,
  `aggregate.cjs` `5b71d635802c09c65e93ee8fd323775ab2b652804aeee3b88aeca8da3bba3d58`.
- `rig.cjs` differs from the research rig it was copied from, whose digest was
  `a929da5cbdae324c5c26d1e46c6672928be153fdd2cb31276b5711b9d232ec14`, in its
  scratch path and nothing else; `batch.ps1` in its scratch, node, package and
  browsers paths.
- The run files are renamed from `<scenario>\<family>\<n>\result.json` to one flat
  name each. Their content is as the rig wrote it, with LF line endings.

## What the runs found

All eighteen completed against `@playwright/mcp` 0.0.83, `playwright-core`
1.64.0-alpha-1790635538000 and node v24.21.0, with chromium 1247 and firefox 1553
headless.

- **`resume`, 6 of 6**: the paused navigation was released 43 to 74 ms after
  `browser_resume` was sent; `browser_resume` had no answer 20 s later; it
  answered 40 to 431 ms after a `browser_close`.
- **`idleresume`, 6 of 6**: a pending `browser_resume` answered 12,038 to 12,407 ms
  after it was sent, which is upstream's 12 s idle timer closing the browser.
- **`armedcloseresume`, 6 of 6**: the close had no answer after 10 s; a snapshot
  then failed in 34 to 233 ms with `TypeError: Cannot read properties of undefined
  (reading 'waitForInitialized')`, before and after a second close; the second close
  answered in 2 to 3 ms and changed nothing; `browser_resume` answered in 57 to
  510 ms, released the close, and the next navigation worked.
- **In all eighteen the child exited after its stdin closed**, within the rig's
  10 s window.

## What it touched

**Its own scratch.** `LOCALAPPDATA`, `APPDATA`, `TEMP` and `PWTEST_SERVER_REGISTRY`
pointed into it, the browsers were a private copy of chromium 1247 and firefox 1553
provisioned by this batch, and nothing read or wrote `%LocalAppData%\BrowserAI`.
**One thing outside it**: Firefox wrote five values under
`HKCU\Software\Mozilla\Firefox\Launcher`, each named for the private copy's
`firefox.exe`. The key was read either side of the batch, 60 values before and 65
after, and exactly those five were removed afterwards, which put it back to 60.
The batch held the machine-wide suite lock from 05:26:49Z to 05:36:07Z.
