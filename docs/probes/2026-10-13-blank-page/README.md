<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-13 -- a Firefox session's restore with Playwright's blank page kept, and the reproduction for Playwright

⚠️ **Written 2026-10-13 before it ran, and run the same day**, four batches under the suite's
lock, each in `.work\blank-page\runs\<name>`: `blank`, 15:31:48Z to 15:49:52Z; `repro`, 15:49:52Z
to 15:50:50Z; `navigate`, 15:52:02Z to 15:52:24Z; and `issue`, 16:02:21Z to 16:03:24Z. *Corrected
2026-10-13 (previously "Written 2026-10-13 and not run yet. No number in this file comes from
it.")*. What they measured is in
[the timings article](../../../kb/playwright/provisioning-and-timings.md) and the batches are cut
into [`docs/evidence/2026-10-13-blank-page`](../../evidence/2026-10-13-blank-page/README.md).

**Why it exists.** The maintainer's 161 of 2026-10-13, in his words verbatim, *"161 your
recommendation"*, puts Playwright's blank page back into a Firefox session's launch, so the
persistent launch waits for that page and not for a restored tab that Firefox's error page
holds without a `load`
([the timings article](../../../kb/playwright/provisioning-and-timings.md#a-firefox-session-resumed-onto-a-page-that-can-neither-load-nor-come-back-from-the-cache-waits-out-the-180-s-launch----measured-2026-10-13)),
and has the resume close the page once a reopened tab is up and say what came back. The
recommendation he took asked for a measurement first: which tab Firefox selects after a restore with
the blank page back, and whether closing the page is safe, so that the reopened tabs stay,
Playwright's context still answers, and the next close and resume reopens the same set. One
replay of 2026-10-13 kept the page and started in 1,070 ms, and Firefox saved the restored tab
beside the blank one; nothing after the launch was read. This rig reads it. Its second stage is
the reproduction the report to Playwright carries, the maintainer's direction d of the same
decision.

## What it measures

**The `blank` stage**, through the payload's own `@playwright/mcp` child, the way the
restore-stall rig's replays start it, with BrowserAI's Firefox config of 2026-10-13 and the one
change of 161: nothing in `ignoreDefaultArgs`, so the blank page is kept. Each repetition gives
a fresh profile its pages in a first run of the child, closes the browser cleanly, ends the
pages' servers where the arm says so, and resumes twice, each time with a new child on the same
profile. Each resume:

1. lists the tabs, the call that starts the browser, and times it;
2. lists them again every 250 ms for 12 s, which says when each reopened tab is listed and with
   which address and title, and whether the blank page is the first tab;
3. reads which tab Firefox selected, from `sessionstore-backups/recovery.jsonlz4` once Firefox
   has written it since the launch, and the current tab's `document.visibilityState`,
   `document.documentURI` and title through `browser_evaluate`;
4. makes the first reopened page current with `browser_tabs` `select` and closes the blank page
   with `close`, as `FirefoxRestore` does;
5. reads the current tab again and takes a `browser_snapshot`; reads every tab's address and
   `document.documentURI` at once with one `browser_run_code_unsafe` call, the snippet
   `FirefoxRestore.ErrorPageRead` copied, which is how the resume tells a tab on Firefox's
   error page by its `about:neterror` address and not by its title, whose words follow
   Firefox's language; and then selects every other tab once to read it with
   `browser_evaluate`, so the cost per tab of the one read and of a read tab by tab are both
   in the row;
6. lists the tabs, waits for the next session write, closes the browser cleanly, and reads
   `sessionstore.jsonlz4`, the session the next resume reopens.

| Arm | Pages in the first run | Whose server ends before the resumes | What it separates |
|---|---|---|---|
| `one-dead` | one, `no-store` | that one | the case of the hazard row, with the blank page back |
| `one-alive` | one, `no-store` | none | the same with nothing ended |
| `three-mixed` | three, `no-store`, the first in the blank page and two in tabs of their own | the first | which tab is current and which Firefox selects with more than one reopened, and the order they are listed in |
| `none` | none: the first run starts the browser and closes it | - | a session whose saved state is the blank page alone |
| `fresh` | no first run at all | - | a profile with no session file, which the resume reads once and does not wait on |

Three rounds by default, arms interleaved in every round, so each arm has three repetitions and
six resumes.

**The `repro` stage**, through `playwright-core` alone, the script the report to Playwright
carries: a persistent Firefox profile given one page served with `cache-control: no-store`,
closed, and launched again after the page's server ended, with
`browser.sessionstore.resume_session_once` in `firefoxUserPrefs` and `about:blank` in
`ignoreDefaultArgs`, at a 30 s timeout. Beside it, the same with the blank page kept, with the
server still answering and with the page cacheable; and, with no restore at all, a `goto` to a
refused address followed by `waitForLoadState('load')`, in Firefox and in Chromium, which says
whether a missing `load` on an error page reaches anything but the persistent launch. The
script the report carries is the first variant, with `import { firefox } from 'playwright-core'`
for the line that loads the payload's copy here.

**The `navigate` stage**, written after the `repro` stage, whose `goto` to a refused address
failed and left the page on `about:blank` in both browsers, so its wait for `load` said nothing
about an error page: through `playwright-core` alone, with every launch option at its default,
a page on a live address navigates itself to a refused one with `window.location.href`, which
is a navigation Firefox shows its error page for, as it does for a restored tab, and then waits
for `load`, in Firefox and in Chromium.

**The `issue` stage** runs the two scripts the report carries, `issue\repro.mjs` and
`issue\restore.mjs`, which are the code of the report below the two-line header, each from a
folder of the batch whose `node_modules` holds a `playwright-core` that re-exports the
payload's, so that the bare import resolves as it does after an `npm install`. `go.mjs` starts
each one once the step's node process is in its job, since neither waits for a go file of its
own. `issue\repro.mjs` runs a second time with Chromium, the one copy with lines changed: the
import, and the launch, which takes `channel: 'chromium'` because the scratch root holds
Chromium's full build and no headless shell.

## What is here

| File | What it does |
|---|---|
| `run.ps1` | The launcher: checks the rig's files, takes the suite lock and proves the take, copies the payload's pinned browsers into the scratch browsers root unless they are there already, runs each step as one node process in a `KILL_ON_JOB_CLOSE` job of its own with a census of it, takes out the Firefox launcher values the run added, and releases the lock, on failure too |
| `blank.mjs` | One repetition of the `blank` stage |
| `repro.mjs` | The `repro` stage |
| `navigate.mjs` | The `navigate` stage |
| `issue/repro.mjs`, `issue/restore.mjs` | The two scripts the report to Playwright carries, as the `issue` stage runs them |
| `go.mjs` | Waits for the go file, then runs a script as `node <file>` would |
| `summarize.mjs` | The tables of a batch, and what each script of an `issue` batch printed |
| `cut.mjs` | Cuts the batches into an evidence directory, the way the restore-stall rig's does |

`RigJob.cs` and `lib.mjs` are the restore-stall rig's, in
[`../2026-10-13-restore-stall`](../2026-10-13-restore-stall/README.md), used as they stand and
not copied: the job objects and the lock's atomic create, and the MCP client, the page server,
the port probe, Firefox's session files decoded and the child's environment block.

## What keeps it off the rest of the machine

- **It runs under the suite's lock and never beside a suite run**, because it starts browsers,
  and it proves its take before it starts any.
- **It never touches the installed BrowserAI**: it starts no BrowserAI at all, only the payload's
  own child and node.
- **Its browsers are copies**, in the restore-stall rig's scratch root,
  `%USERPROFILE%\Downloads\tmp-restore-stall\data-root\browsers`, made from the provisioned
  browsers root, which is only read. `PWTEST_SERVER_REGISTRY` sends Playwright's server
  registry to scratch.
- **Every process it starts is in a job it created, from birth**, as in the restore-stall rig.
  Nothing is ended by pid, and nothing is selected by image name.
- **Nothing shows a window**: every browser is headless, every node launch passes
  `windowsHide: true` with its standard streams piped, and the one `Start-Process` passes
  `-NoNewWindow`.
- **The child's environment is BrowserAI's block**, `childEnvironment` in `lib.mjs`, with
  `MOZ_DISABLE_SAFE_MODE_KEY` set, and the `repro`, `navigate` and `issue` stages' node
  processes inherit the launcher's, with the same variable set, because `playwright-core`
  1.64.0-alpha does not set it itself. For the `issue` stage `TEMP` and `TMP` name the batch's
  `tmp` folder, where `restore.mjs` makes its profile under `os.tmpdir()`; the other two stages
  that launch through `playwright-core` alone leave them as they are, so the browsers they
  launch put their temporary profile under the user's `%TEMP%`, which Playwright removes when
  the browser closes.
- ⚠️ **Firefox records every executable path it runs from** under
  `HKCU\Software\Mozilla\Firefox\Launcher`; the launcher takes out afterwards only the values
  that name the scratch folder and were not there before, and reports every other change in
  `registry-<stage>.txt` without touching it.

## Running it

In a worktree with the payload copied in as `payload\`, from PowerShell, detached, with the
output in a file:

```
robocopy <the payload folder> payload /E
pwsh -NoProfile -NonInteractive -File docs\probes\2026-10-13-blank-page\run.ps1 -Stage blank -Name blank
<payload>\node\node.exe docs\probes\2026-10-13-blank-page\summarize.mjs .work\blank-page\runs\blank
pwsh -NoProfile -NonInteractive -File docs\probes\2026-10-13-blank-page\run.ps1 -Stage repro -Name repro
<payload>\node\node.exe docs\probes\2026-10-13-blank-page\summarize.mjs .work\blank-page\runs\repro
pwsh -NoProfile -NonInteractive -File docs\probes\2026-10-13-blank-page\run.ps1 -Stage navigate -Name navigate
pwsh -NoProfile -NonInteractive -File docs\probes\2026-10-13-blank-page\run.ps1 -Stage issue -Name issue
node docs\probes\2026-10-13-blank-page\cut.mjs docs\evidence\<date>-blank-page .work\blank-page\runs\<name> ...
```

`-HeldBy <pid>` runs a stage inside a hold of the lock taken by a longer-lived holder, which is
how the four batches of 2026-10-13 ran.

**What it costs.** A resume is about 45 s: 12 s of lists, up to 25 s twice for a session write,
and the rest calls of a second or less; a repetition is two resumes and a first run, about a
minute and a half, so the default batch of fifteen repetitions holds the lock for about 25
minutes. The `repro` stage is four persistent launches, one of which waits out its 30 s, and two
browsers that each wait up to 30 s for a `load`: about two minutes. One headless browser and a
node child at a time. Measured on 2026-10-13, launcher included: the `blank` batch took 18 min 4 s,
`repro` 57 s, `navigate` 22 s and `issue` 62 s.
