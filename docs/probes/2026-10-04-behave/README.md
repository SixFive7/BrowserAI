<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-04 -- a hidden Chromium's user agent, Chromium's screenshot line, and how a browser ends

Establishes three kb entries and re-verification rows 195 to 197:
[A hidden Chromium sends the headed user agent](../../../kb/chromium/fingerprinting.md#a-hidden-chromium-sends-the-headed-user-agent-derived-from-the-browser----built-and-measured-2026-10-04),
[The exact line, the other direction, an element, a JPEG, and BrowserAI's refusal](../../../kb/playwright/tools-and-artifacts.md#the-exact-line-the-other-direction-an-element-a-jpeg-and-browserais-refusal----measured-2026-10-04),
and the exit codes and the closes in
[Switching between a window and none, closing the window, and closing the last tab](../../../kb/playwright/provisioning-and-timings.md#switching-between-a-window-and-none-closing-the-window-and-closing-the-last-tab----measured-2026-10-04).
Evidence: [`docs/evidence/2026-10-04-behave/`](../../evidence/2026-10-04-behave/README.md).

**Why it exists.** The behaviour work built the maintainer's decisions 6 b, 8 b and 9 d,
each measured before and after through a published `BrowserAI.Server.exe`: what a
server and a page see of a hidden Chromium's user agent, the exact size at which
a Chromium screenshot starts to repeat, and the exit code a browser leaves for
each way it can end, with what the next calls say about it.

## What is here

| File | What it does |
|---|---|
| `behave.mjs` | One run through a published `BrowserAI.Server.exe`. `ua`: a page that echoes what it was sent, read by the server, the page, a worker and a fetch; `tall`: full-page and element screenshots at exact heights and widths, PNG and JPEG, to a file and inline; `windowclose`: a headed session whose window is closed by `close-windows.ps1`; `kill`: the browser's main process ended with code 1; `lasttab`: the only tab closed with `browser_tabs`; `ownclose`: the session's own `browser_close`; `restore`: two tabs closed and resumed hidden, with the user agent of the first requests the restore makes. Every run but `ua`, `tall` and `restore` watches the browser's main process with `exit-watch.ps1`, writes every store, and reads every tab back after `browserai_resume` |
| `site.mjs` | The local page server on `127.0.0.1`: `/echo` with `Accept-CH`, `/headers`, a worker and a service worker, a sign-in that sets an `HttpOnly` persistent and session cookie, a form, `/tall?h=N` and `/wide?w=N` at exactly N px in 100 px bands with a 200 px key block at the left, and `/tallel?h=N` with one element N px tall |
| `mcp.mjs` | The MCP client, with a census of the server's process tree read by parent pid |
| `exit-watch.ps1` | Opens the browser's main process by the pid and creation time the census read, refuses a pid whose creation time is more than 5 microseconds off, writes `.armed` once it holds the handle, optionally ends it with `TerminateProcess`, and writes the exit code |
| `close-windows.ps1` | Posts `WM_CLOSE` to every visible top-level window on the desktop it runs on, and refuses a desktop whose name does not start with `BrowserAI-behave-` |
| `HiddenDesktop.ps1` | The lifetime work's [`HiddenDesktop.ps1`](../2026-10-04-lifetime/README.md) with this rig's desktop prefix |
| `raw.mjs`, `raw2.mjs` | The payload's own `cli.js` over stdio with a config per arm, no BrowserAI on the path: nothing set, `--user-agent`, `contextOptions.userAgent`, headless and headed; `raw2.mjs` adds a service worker, a relaunch with a restored tab, and both mechanisms at once. Both first ask `chrome.exe --headless --dump-dom` for the user agent three times and time it |
| `batch.ps1` | Runs a plan under the suite lock, each run through `HiddenDesktop.ps1`, with `HKCU\Software\Mozilla\Firefox\Launcher` exported before and after |
| `plan-before.json`, `plan-before2.json` | Before the change, against `BrowserAI.Server.exe` published from `5bf02f48`. `before2` repeats `before` with the site's heights made exact and the exit watch's arming race closed |
| `plan-after-ab.json`, `plan-after-c.json`, `plan-after-d.json` | Through the change: `after-ab` with 6 b and 9 d, `after-c` with all three, and `after-d` the four closed windows again with the final wording of that refusal |
| `analyze_tall.py` | Reads each image's size from its header and decoded, and counts the rows (or columns) at or past 16,384 px that repeat the ones 16,384 px before them, with rows 1,000 px apart as the control; for a JPEG, a row also counts when its key block is within a mean difference of 8 |
| `analyze_behave.py` | Prints one block per run: the user agents and hints each request carried, the screenshots' outcomes, and the exit codes |
| `cut_evidence.py` | Cut the runs into the evidence batch: the user profile path replaced and colour escapes removed in `.trimmed.` copies with the originals' digests, and every image left out with its digest |

## How it was run

Every batch held the suite lock, and every run went through `HiddenDesktop.ps1`,
so each browser, `node` and `BrowserAI.Server.exe` it started was on the private
desktop and in its job:

```
pwsh -NoProfile -File batch.ps1 -Name <batch> -Plan plan-<batch>.json -Server <a published BrowserAI.Server.exe>
python analyze_behave.py <runs> <batch>
python analyze_tall.py <run>\rig\*.png <run>\rig\*.jpeg
```

`batch.ps1`'s `$S` and `$node` name this rig's scratch directory and payload;
point them at your own.

## What keeps it off the rest of the machine

- **Nothing reaches the screen**: the desktop is never switched to, and the
  window census runs once a second over both desktops by the pids the job reports.
  Each batch starts with the lifetime work's `desk-probe.ps1` as the positive
  control.
- **It selects no process by image name.** The census reads the server's own
  descendants by parent pid, and `exit-watch.ps1` acts only on the pid and
  creation time the census read for the browser's main process.
- `browserai_destroy` removes every session a run creates. A Firefox run writes
  the values Firefox keeps under `HKCU\Software\Mozilla\Firefox\Launcher` for the
  provisioned `firefox.exe`; the batches exported the key before and after and
  added no value.
