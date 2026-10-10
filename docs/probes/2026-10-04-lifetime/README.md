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
and nothing run here may put a window on the maintainer's screen, so it runs
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

## `switch/`: a switch between a window and none, and the corners around it

*Added 2026-10-04 by addition, by the behaviour work, on the maintainer's decision 10 a,
in his words verbatim: "10 a".* Until then the runs below were in a scratch
directory only. Establishes
[Switching between a window and none, closing the window, and closing the last tab](../../../kb/playwright/provisioning-and-timings.md#switching-between-a-window-and-none-closing-the-window-and-closing-the-last-tab----measured-2026-10-04)
and re-verification rows 194 and 195. Evidence:
[`docs/evidence/2026-10-04-lifetime-switch/`](../../evidence/2026-10-04-lifetime-switch/README.md).

| File | What it does |
|---|---|
| `switch/switch.mjs` | One run through the published `BrowserAI.Server.exe`. `switch`: sign in, open three tabs, write every store through `browser_evaluate`, type into a form, `browser_close`, `browserai_resume` with `headed` toggled, and read it all back from every tab; `windowclose`: the same session headed, its window closed by `close-windows.ps1`, then what the next calls find; `lasttab`: the only tab closed with `browser_tabs`, then the switch; `conflict`: `browserai_resume` with a different `headed`, the same one, none, and a viewport, while the browser is up. It records a census of the server's process tree, by parent pid, before and after |
| `switch/switch.v1.mjs` | The first version, which the `main` hold ran under the name `switch.mjs`, beside the site that is `../site.mjs` here. To run it again, copy it, `../site.mjs` and `switch/mcp.mjs` into a directory of their own as `switch.mjs`, `site.mjs` and `mcp.mjs` |
| `switch/site.mjs` | The second version's site: the same pages as `../site.mjs`, none of which writes anything when it loads |
| `switch/mcp.mjs` | The MCP client, with the process census `../mcp-client.mjs` leaves out |
| `switch/close-windows.ps1` | Posts `WM_CLOSE` to every visible top-level window on the desktop it runs on, the way a person closes one, and refuses to run on a desktop whose name does not start with `BrowserAI-lifetime-` |
| `switch/batch.ps1` | Runs a plan under the suite lock, each run through `../HiddenDesktop.ps1`, with `HKCU\Software\Mozilla\Firefox\Launcher` exported before and after; the smoke run's `gate_check.py` verdict decides whether the rest of the plan runs |
| `switch/plan-all.json`, `switch/plan-v2.json` | The two holds' plans, 29 and 15 runs; `plan-all.json` holds row 152's and Q380's runs too |
| `switch/analyze_switch.py` | Writes `switch-summary.json` for a hold and prints a line per switch |
| `switch/gate_check.py` | The smoke run's verdict |

**Re-establish** by copying `switch/` and `../HiddenDesktop.ps1` into one
directory, pointing `batch.ps1`'s `$S`, `$rig` and `$node` at a scratch
directory, that directory and a payload's `node.exe`, and the plan's `server=`
at a published `BrowserAI.Server.exe`; then run
`pwsh -File batch.ps1 -Name <hold> -Plan plan-v2.json` and
`python analyze_switch.py <scratch>\runs\<hold>`, and compare
`switch-summary.json` with the one in the evidence, field by field.

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
