<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-04 -- a hidden Chromium's user agent, Chromium's screenshot line, and how a browser ends, before and through 6 b, 8 b and 9 d

**What this is.** Lane behave's five batches, each in one hold of the suite lock
and each run on a desktop of its own that was never put on the screen, through a
published `BrowserAI.Server.exe`: before the lane's change, and through it. Taken
at `@playwright/mcp` **0.0.83**, `playwright-core` **1.64.0-alpha-1790635538000**,
node **v24.21.0**, Chrome for Testing **155.0.8059.12** (`chromium-1247`) and
Firefox **156.0** (`firefox-1553`), on Windows 11 Pro 10.0.26300. The rig is
[`docs/probes/2026-10-04-behave/`](../../probes/2026-10-04-behave/README.md).
**382 files, 3,031,609 bytes.**

| Batch | When | The server | Runs |
|---|---|---|---|
| `before/` | 13:50:26Z to 13:54:54Z | Published from `5bf02f48`, before the change | The desktop's positive control, the mechanisms of 6 b through the payload's `cli.js` (`raw-ua-arms`), 6 user-agent runs, 3 screenshot runs, 4 closed windows, 2 kills, 2 last tabs and 2 of the session's own closes |
| `before2/` | 13:55:44Z to 13:59:18Z | The same | The closes and the screenshots again, with the site's heights made exact and the exit watch's arming race closed: 4 screenshot runs, Firefox's included, 4 closed windows, 2 kills, 2 last tabs and 2 of the session's own closes; and 6 b's mechanisms again with a service worker and a restored tab (`raw2-ua-arms`) |
| `after-ab/` | 14:30:15Z to 14:31:49Z | `5bf02f48` with 6 b and 9 d | 6 user-agent runs, 2 restores read for their first requests' user agent, and 3 screenshot runs |
| `after-c/` | 15:06:01Z to 15:09:19Z | `5bf02f48` with 6 b, 9 d and 8 b | 4 closed windows, 2 kills, 2 last tabs, 2 of the session's own closes, 1 screenshot run and 2 user-agent runs |
| `after-d/` | 15:22:49Z to 15:24:24Z | The change as committed, on top of `47f0b690` | The 4 closed windows again, with the final wording of that refusal |

Every server up to `after-c` reported itself as `1.1.1-alpha.0.206`, because the
version counts commits and the change was not committed until `after-d`, whose
server reported `1.1.1-alpha.0.212`.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: a hidden Chromium sends the headed user agent](../../../kb/chromium/fingerprinting.md#a-hidden-chromium-sends-the-headed-user-agent-derived-from-the-browser----built-and-measured-2026-10-04) and re-verification row 196 | `before*/raw*-ua-arms/`, `*/ua-*` and `after-ab/restore-*` |
| [kb: the exact line of Chromium's screenshot limit](../../../kb/playwright/tools-and-artifacts.md#the-exact-line-the-other-direction-an-element-a-jpeg-and-browserais-refusal----measured-2026-10-04) and re-verification row 197 | `*/tall-*`, read by `analyze_tall.py` |
| [kb: switching, closing the window and the last tab](../../../kb/playwright/provisioning-and-timings.md#switching-between-a-window-and-none-closing-the-window-and-closing-the-last-tab----measured-2026-10-04) and re-verification row 195 | `*/windowclose-*`, `*/kill-*`, `*/lasttab-*` and `*/ownclose-*` |
| [`HAZARDS.md`](../../../HAZARDS.md#hazard-index) | The Q380 row's closure, and the high-entropy client hints of a hidden session |

## What is here

| Path | What it holds |
|---|---|
| `<batch>/<batch>.batch.log` | The hold's own log: the lock, every run's start and end, and the registry exports' exit codes |
| `<batch>/desk-probe/` | The private desktop's positive control: the probe's desktop and the census that found its window there and nowhere else |
| `<batch>/<run>/rig/result.trimmed.json` | Every call the run made, with its arguments, its time and up to 6,000 characters of its answer; the census of the server's process tree; every request the site served, with its headers; and per scenario what was read: the user agent and hints a page, a worker and a fetch saw, each screenshot's outcome, the exit line of the browser's main process, and every tab's stores before the close and after the resume |
| `<batch>/<run>/rig/exit-*.txt`, `exit-*.txt.armed` | `exit-watch.ps1`'s line: the pid and creation time it held, and the exit code or why it read none; and when it held the handle |
| `<batch>/<run>/rig/analysis.json` | `analyze_tall.py` over a screenshot run's images, for the `before*` batches. ⚠️ In `before/` the site rounded every height up to a whole band of 100 px, so those images are 16,400 px where 16,384 and 16,385 were asked for; `before2/` is the run with exact heights. The JPEG rows in these files were counted by byte identity and a key block, and a JPEG's first row past the line reads as not repeated, which is the blend the kb entry describes |
| `<batch>/<run>/rig/calls.log` or `calls.trimmed.log`, `server.stderr.trimmed.log`, `desktop.log` | Every call and the start of its answer; what the server wrote to standard error; and the private desktop's census, once a second |
| `before*/raw*-ua-arms/rig/` | `results.json`: three asks of `chrome.exe --headless --dump-dom` and their times, and per arm what the page, the worker, the service worker and the server saw; per arm `config.json`, `child.stderr.log` and the snapshots `output/` holds |
| `originals.sha256`, `left-out.sha256` | What was trimmed and what was left out, below |

**No census recorded a `LEAK`** in any batch.

## What was cut

- **The user profile path**, `C:\Users\<name>`, replaced by `%USERPROFILE%` in
  127 files, and terminal colour escapes from the Firefox screenshot runs' call
  logs, one of them cut short before its closing letter, stored under
  `.trimmed.` names with the originals' digests in `originals.sha256`.
- **Left out whole, with digests in `left-out.sha256`**: every screenshot the
  runs copied out, 94 files, PNG and JPEG, up to 4.0 MB each. The rig takes
  each again in seconds, and `analyze_tall.py` reads one the same way.
- **Left out by directory**: the browser profiles of the `raw*-ua-arms` runs, 15
  directories of 2,529 files and 122,895,227 bytes, which are what Chromium wrote
  for a local page and nothing else.
- **Not here**: the eight exports of `HKCU\Software\Mozilla\Firefox\Launcher`
  taken before and after each of the five holds, because they list every
  Firefox this machine has run under the user profile path. In every hold the
  names of the values were the same before and after, and only the two timestamps
  Firefox keeps for the provisioned `firefox.exe` changed. Their SHA-256, before
  and after: `before`
  `0873745301d5c297f059c50abb0d702a05ef736db8b83357d3a8a96e07d21862` and
  `b53c259307cab4c6c7ecc4f8af7f8ebc334d3b1101a30dec57e55bdda0dd689f`;
  `before2` `b53c259307cab4c6c7ecc4f8af7f8ebc334d3b1101a30dec57e55bdda0dd689f`
  and `3bb011bdbf5d6a55480f640021d51a63c1563f8a8288cf91d0a13c913053b8a4`;
  `after-ab` `f374cb0d57bb4f509883742c96e4e388c99ee9175ea1cad7157ada37e816a8aa`
  and `e946199b273353a259b191d84a60431691e1645202903bf31c9cafa9736d22f9`;
  `after-c` `4c9e45d0a4c3a1eedc491763cfe23f47d5a37aed73ddd77587f85f8c560b9f05`
  and `e7e3187087050b4109a3ea711f7532ab190577bc761a128f824f1963c7b4b4f1`;
  `after-d` `83f38a8d8e8a95853eb89cb2c0f2722137e1d99d01ce825e4c3313f716907721`
  and `a5a434f36c9799177dee9be72ca46560e1690cf54ffd2ee5f7a86aa12997b40f`.

## Privacy

Every page was served from `127.0.0.1` by the rig, the only account was the rig's
own `tester` with the password `not-a-secret`, and no file here holds a cookie
value from anywhere else.
