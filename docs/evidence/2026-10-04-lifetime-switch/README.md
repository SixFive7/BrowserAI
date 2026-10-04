<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-04 -- a switch between a window and none, a closed window, the last tab and a resume conflict

**What this is.** The runs of the lifetime review lane's two holds of the suite
lock that [`2026-10-04-lifetime`](../2026-10-04-lifetime/README.md) left out:
every switch of a session between headed and headless through `browser_close`
and `browserai_resume`, a headed window closed the way a person closes it, the
only tab closed with `browser_tabs`, and `browserai_resume` asked for other
settings while the browser was up. Every run drove the published
`BrowserAI.Server.exe` over stdio on a desktop of its own that was never put on
the screen. Taken at `@playwright/mcp` **0.0.83**, `playwright-core`
**1.64.0-alpha-1790635538000**, node **v24.21.0**, Chrome for Testing
**155.0.8059.12** (`chromium-1247`) and Firefox **156.0** (`firefox-1553`), on
Windows 11 Pro 10.0.26300, with `BrowserAI.Server.exe` **1.1.1-alpha.0.197**,
published from `e30380a` in the lifetime lane's worktree and the same binary in
both holds. **139 files, 3,386,894 bytes.** Persisted on 2026-10-04 by lane
behave, on the maintainer's decision 10 a, in his words verbatim: *"10 a"*.

| Hold | When | Runs here |
|---|---|---|
| `main/` | 02:22:24Z to 02:32:35Z | 18 of the hold's 29: one smoke run, 12 switches, two closed windows, two last tabs and one conflict. The other 11 are row 152's and Q380's, in [`2026-10-04-lifetime`](../2026-10-04-lifetime/README.md) |
| `v2/` | 03:02:48Z to 03:09:22Z | All 15: one smoke run, 12 switches and two closed windows, with the rig's second version |

**The second version** changed two things and nothing else: no page on the
rig's site writes any state when it loads, so a page the browser reloads while
it restores a session cannot write a value back and make it look kept, and every
read also reports the page's navigation type. The rig writes every store
through `browser_evaluate`. Both versions of the rig are in
[`docs/probes/2026-10-04-lifetime/`](../../probes/2026-10-04-lifetime/README.md), under `switch/`.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: switching between a window and none](../../../kb/playwright/provisioning-and-timings.md#switching-between-a-window-and-none-closing-the-window-and-closing-the-last-tab----measured-2026-10-04) and re-verification row 194 | `*/switch-*`: what a switch keeps and what it costs, and the program each mode runs |
| The same kb entry and re-verification row 195 | `*/windowclose-*` and `main/lasttab-*`: what closing the window and closing the last tab did before BrowserAI acted on either |

## What is here

| Path | What it holds |
|---|---|
| `main/main.batch.log`, `v2/v2.batch.log` | Each hold's own log: the lock, every run's start and end, the smoke run's gate, and the two registry exports' exit codes |
| `*/switch-summary.json` | One record per switch, from `analyze_switch.py`: the tabs before and after, which stores came back, the history length, the user agent before and after, the program each mode ran and the timings |
| `*/<run>/rig/result.trimmed.json` | Every call the run made, with its arguments, its time and up to 6,000 characters of its answer; the process census before and after; and every request the site served, with its headers |
| `*/<run>/rig/calls.log` | Every call and the start of its answer, one line each |
| `*/<run>/rig/server.stderr.trimmed.log` | What `BrowserAI.Server.exe` wrote to standard error |
| `*/<run>/desktop.log` | The private desktop's census, once a second, of every visible top-level window the run's processes owned, on that desktop and on the one the maintainer uses |
| `originals.sha256`, `left-out.sha256` | What was trimmed, below; nothing was left out |

**No census recorded a `LEAK`** in any of the 33 runs: no window any of their
processes owned appeared on the desktop the maintainer uses. The private
desktop's positive control is in the other batch's `desk-probe/`.

## What was cut

- **The user profile path**, `C:\Users\<name>`, replaced by `%USERPROFILE%` in
  66 files, stored under `.trimmed.` names with the originals' digests in
  `originals.sha256`.
- **Left out whole**: nothing in these runs. The sessions were destroyed by the
  rig at the end of each run, and each session's `browserai_destroy` answer is in
  its `result.trimmed.json`.
- **Not here**: two batch logs of one line each, attempts to queue for the lock
  that went no further, and a copy of the private desktop's positive control,
  which is in the other batch. The four exports of
  `HKCU\Software\Mozilla\Firefox\Launcher`, taken before and after each hold,
  are not here because they list every Firefox this machine has run, the
  maintainer's own included, under the user profile path; between before and
  after, each hold changed the two timestamps Firefox keeps for the provisioned
  `firefox.exe`, which were there already, and added nothing. Their SHA-256: `e7d744b064e719539bf0720a80deb6159f2ba6d66f2975ae9db52494df43b186`
  and `f48685b35119a422f1f868b7b19041970d0b419e186a9db852553665a584a92a` for
  `main`, `7526e2fb876c1bc9693df129cb80c4e3d702ea353d216c350ece619beeabb45e` and
  `8e79eb9f20606e60ec998af08fadec18d32e8525c65798a29adef4587d327f6b` for `v2`.

## Privacy

Every page was served from `127.0.0.1` by the rig, the only account was the rig's
own `tester` with the password `not-a-secret`, and no file here holds a cookie
value from anywhere else.
