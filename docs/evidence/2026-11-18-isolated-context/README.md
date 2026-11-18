<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-11-18 -- a tab in an isolated context, the HTTP Archive and the cookies (208, 210)

**What this is.** The research behind the maintainer's 208 a + b and 210 a, in his words
verbatim: *"208 a + b (until the upstream fix comes in)"* and *"210 a"*: what `browser_tabs`'
`isolatedContext`, new in `@playwright/mcp` 0.0.84, does to a session's HTTP Archive and to its
cookies. `@playwright/mcp` **0.0.85**, `playwright-core` **1.66.0-alpha-2026-11-09** (npm gitHead
`84e2bdc`) and node **v26.13.1**, all from the payload BrowserAI adopted on 2026-11-17, against
Chrome for Testing **158.0.8127.18** (`chromium-1254`), Firefox **159.0** (`firefox-1573`) and
Google Chrome **157.0.8103.79**, upstream's default channel. Every run started the payload's own
`cli.js`, headless, one browser at a time, at below-normal priority, with `TEMP`, `TMP` and
`LOCALAPPDATA` in scratch and the shared browsers root read and not written, from 2026-11-17T23:06Z
to 23:15Z and on 2026-11-18 at 03:09Z and from 03:48Z to 03:51Z, on Windows 11 Pro 10.0.26300. No BrowserAI process ran:
*BrowserAI's configuration* is what `BrowserConfiguration.Generate` wrote for a capture session
at `869409fa`, with `locale` `en-US` in place of this machine's, and *upstream's defaults* is a
configuration that sets `recordHar.path` and nothing else. Reported upstream as
[microsoft/playwright#44199](https://github.com/microsoft/playwright/issues/44199) on
2026-11-18T03:21:32Z, after a search for an existing report found none.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: tools and artifacts](../../../kb/playwright/tools-and-artifacts.md#a-tab-in-an-isolated-context-is-missing-from-the-sessions-http-archive----measured-2026-11-18) | The measurement |
| [`HAZARDS.md`](../../../HAZARDS.md#hazard-index) | The row for a tab in an isolated context missing from a capture session's archive |
| [kb: re-verification](../../../kb/re-verification.md) | Row 228 |
| [`DECISIONS.md`](../../../DECISIONS.md) | 208 a + b and 210 a |
| [`TODO.md`](../../../TODO.md) | The watch on microsoft/playwright#44199 |
| [`2026-11-17-adopted-playwright`](../2026-11-17-adopted-playwright/README.md) | The correction of its cookie claim |

## The runs

| Run | Browser and configuration | Server | What it showed |
|---|---|---|---|
| `R1` | Chrome for Testing, BrowserAI's, two isolated contexts | each cookie on its own page | both isolated contexts emptied the archive within 1 ms and appended their logs, two documents back to back for 23 ms, then the default context's write left page a only; each isolated tab's first request carried no cookie, and both listed none before it |
| `R2` | Firefox, the same | the same | the same, for 48 ms |
| `R3` | Google Chrome, upstream's defaults | the same | the isolated context's page, then 26 ms later the default context's only; no cookie on the isolated tab's first request |
| `R4`, `R5` | Chrome for Testing and Firefox, BrowserAI's | the 2026-11-17 rig's, which set `cookie_a` on every path but page B | the isolated tab's first request carried no cookie, and its own `favicon.ico` request was answered with `cookie_a`, which is where the cookie of 2026-11-17 came from |
| `R6` | Chrome for Testing, BrowserAI's | each cookie on its own page | no archive before any close; the isolated context alone closed wrote page b; `browser_close` then left page a only |
| `R7` | Google Chrome, upstream's defaults, `--isolated` | the same | each context wrote twice; page a at the end |
| `R8`, `R9` | Chrome for Testing and Firefox, BrowserAI's, resuming `R1`'s and `R2`'s profiles | the same | the default context listed the profile's `cookie_a` before any request; the new isolated tab listed none and sent none; the profile kept the default context's cookie only |
| `R10` | Google Chrome, upstream's defaults with a storage state | the same | the one way an isolated context starts with a cookie: the storage state's seed, on its first request; BrowserAI writes no storage state |
| `U1`, `U2` | Google Chrome, the script the issue carries, as drafted and as posted | a page per path, no cookies | after `browser_close` the archive held `/default-context-page`; with the isolated context closed first it held `/isolated-context-page` until `browser_close` replaced it |

**When the archive is written at all**, three runs of `rig/har-at-close.mjs.txt` on 2026-11-18
between 03:48Z and 03:51Z, Chrome for Testing with BrowserAI's launch arguments and a capture
session's `serviceWorkers` and `recordHar`, no isolated context, each `config.json` beside its
run, and the archive read after the page loaded and before anything closed:

| Run | How the child ended | The archive |
|---|---|---|
| `har-at-close/close` | `browser_close`, then the end of its input | none before the close; after it 5,517 bytes holding the page |
| `har-at-close/eof` | the end of its input alone, which since 0.0.85 closes the browser gracefully; the child exited 0 | none, before or after |
| `har-at-close/kill` | its whole process tree ended by its pid with `taskkill /T /F` | none, before or after; no process was left with the run's folder in its command line |

Each run's `runs/<run>/result.json` holds every tool answer, every request the server saw with its
`Cookie` header and every reading of the archive; `runs/<run>.driver.log` is the driver's own
output; `runs/<run>/harwatch.jsonl` (`R1`, `R2`, `R3`, `R7`) is every write to the archive's path
with its time, taken by a hook in the server process that copies each version aside
(`output/*.har.v<n>.json`) and changes nothing it observes; `runs/<run>/output/` is what the
session wrote, the final archive among it.

## The rig

`rig/repro.mjs.txt` drives `R1` to `R10`, `rig/har-watch.cjs.txt` is the hook, `rig/below-normal.mjs.txt`
starts a command at below-normal priority, `rig/upstream-repro.mjs.txt` and
`runs/U2-upstream-final/repro.mjs.txt` are one file, the issue's script as posted, which `U2` ran
(`U1` ran the draft's script, kept in the left-out draft), `rig/har-at-close.mjs.txt` drives the
three runs below,
`rig/seed-state.json.txt` is `R10`'s storage state, and `rig/launcher-key.ps1.txt` read the Firefox
`Launcher` key before and after the Firefox runs. Saved under their own names they run as they
ran. The Firefox runs changed the data of the two values `Launcher` and `Browser` that already
existed for `firefox-1573`, which every Firefox launch updates, and added and removed none; the
key's state before is `launcher-1573-before.json` and `launcher-snapshot.json`.

## What was cut

Every browser profile, the scratch `LOCALAPPDATA` and `TEMP` folders and the `.playwright-mcp`
folders of the runs are left out by directory, with their file counts and sizes, in
[`left-out.sha256`](left-out.sha256); from `U1`'s and `U2`'s `TEMP` only the configuration and
the archive the script read are kept, beside its output. The research's report, the issue's
drafts and the text as posted are left out with their SHA-256, the posted text being the issue
itself, and so are the folder of upstream sources and npm metadata it read, by directory, and its
own tools. The eight files kept under `.txt` names have their digests in
[`originals.sha256`](originals.sha256). Line endings are this repository's, LF.
