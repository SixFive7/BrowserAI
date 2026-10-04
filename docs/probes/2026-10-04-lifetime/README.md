<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-04 -- a desktop nobody is looking at, and a page 50,000 px tall

Re-establishes the headed half of
[re-verification row 152](../../../kb/re-verification.md), recorded in
[What `--enable-automation` changes that a page or a server can see](../../../kb/playwright/configuration.md#what---enable-automation-changes-that-a-page-or-a-server-can-see----measured-2026-09-24),
and establishes
[A full-page screenshot past 16,384 px](../../../kb/playwright/tools-and-artifacts.md#a-full-page-screenshot-past-16384-px-repeats-its-first-16384-rows-in-chromium-and-firefox-refuses-one-past-32767-px----measured-2026-10-04)
and re-verification row 190. Evidence:
[`docs/evidence/2026-10-04-lifetime/`](../../evidence/2026-10-04-lifetime/README.md).

**Why it exists.** Q382 b: the headed half of row 152 needs a headed browser,
and nothing a lane runs may put a window on the maintainer's screen, so it runs
on a desktop of the rig's own. Q380 a: a full-page screenshot of a very tall
page was seen to repeat every 16,384 px outside BrowserAI, and this takes it
through the published `BrowserAI.Server.exe`.

## What is here

| File | What it does |
|---|---|
| `HiddenDesktop.ps1` | Runs one command on a new desktop in this window station, created without `DESKTOP_SWITCHDESKTOP`, inside a kill-on-close job. Once a second it reads the job's process ids and lists the visible top-level windows those pids own on the private desktop and on the desktop the script runs on. A window of the job on the script's own desktop is recorded as `LEAK` and the job is terminated at once |
| `desk-probe.ps1` | The positive control for the script above: it writes the name of its own thread's desktop and shows one tool window far off any monitor, shown without activation, for four seconds |
| `site.mjs` | A page server on `127.0.0.1`. `/tall?h=N` is N px of 100 px bands, each a solid colour whose red and green encode the band's index, with the index as text beside it |
| `mcp-client.mjs` | A minimal MCP client for `BrowserAI.Server.exe` over stdio |
| `tall.mjs` | One Q380 run: init a headless session, open `/tall`, take `browser_take_screenshot` with `fullPage: true` and a `filename`, copy the file out, destroy the session |
| `tall_check.py` | Reads each 100 px band of the image and says which band it shows |
| `tall_repeat.py` | Whether every pixel row at or below 16,384 px is the same as the row 16,384 px above it, with rows 1,000 px apart above the seam as the control |

## How it was run

Every run held the suite lock and ran through `HiddenDesktop.ps1`, so each
browser, `node` and `BrowserAI.Server.exe` it started was on the private
desktop and in its job:

```
pwsh -NoProfile -File HiddenDesktop.ps1 -Purpose <id> -App <node.exe> `
     -CommandLine "<node.exe> <rig> <args>" -WorkingDirectory <dir> -Log <dir>\desktop.log
```

- **Row 152's headed arms** are `fp.mjs` from
  [`docs/evidence/2026-09-23-password-prompt/`](../../evidence/2026-09-23-password-prompt/README.md),
  with its `REPO` pointed at the worktree whose `payload\` it should use and
  `chromium-1246` changed to the provisioned revision, as the 2026-10-03 batch
  patched it: `funnel headed-plain`, `funnel headed-auto --enable-automation`,
  `pw headed-plain`, `pw headed-nab --nab`, `pw headed-auto --enable-automation`,
  `pw headed-auto-nab --enable-automation --nab` and `direct plain`. Compare the
  arms' `fingerprint.json` property by property, and each arm against the same
  arm in the 2026-09-24 batch.
- **Q380** is `tall.mjs server=<BrowserAI.Server.exe> browser=<family> height=50000 run=<tag> out=<dir> sessions=<dir>`,
  then `tall_check.py` and `tall_repeat.py` over the image.

## What keeps it off the rest of the machine

- **Nothing reaches the screen**: the desktop is never switched to, and the
  window census is the check, run once a second over both desktops by the pids
  the job reports. Run `desk-probe.ps1` through it first: the probe's window
  must be listed on the private desktop and nothing on the other.
- **It selects no process by image name.** The census and the termination act
  on the job's own process list.
- `browserai_destroy` removes every session `tall.mjs` creates. A Firefox run
  writes the values Firefox keeps under
  `HKCU\Software\Mozilla\Firefox\Launcher` for the provisioned `firefox.exe`;
  export the key before and after.
- `fp.mjs`'s `direct` arm ends the Chrome it started by the pid it was given.
