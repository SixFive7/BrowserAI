<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# BrowserAI.Server.exe instances — measured 2026-09-23 ~18:20 local (UTC+2)

Sources: `Get-CimInstance Win32_Process` (pid/parent/CreationDate/ExecutablePath);
`~/.claude/sessions/<pid>.json` (sessionId, cwd, entrypoint, version, status);
`~/.claude/projects/<slug>/<sessionId>.jsonl` (last conversation entry);
`C:\Users\jori\AppData\Local\BrowserAI\logs\browserai-*.log` filtered on the
`pid=<pid>@<processCreatedFileTime>` identity pair (plain `pid=` alone is
AMBIGUOUS — pids are reused; see note at the bottom).

All 22 servers run `C:\Users\jori\AppData\Local\BrowserAI.app\current\BrowserAI.Server.exe`
with an empty command line, each with exactly one `node.exe` child (46760 has two).
All 22 parents are live `claude.exe`. Log timestamps are UTC; process times are local.

| server | started (local) | node kid | parent claude | claude binary | session id | cwd | last transcript entry (UTC) | idle? |
|---|---|---|---|---|---|---|---|---|
| 88232  | 09-21 00:08:52 | 47092  | 84124  | VS Code ext 2.1.278 | 2f0c8f4e | PortainerCompose  | 2026-09-22T13:15:57Z | idle 29 h |
| 46760  | 09-21 00:08:57 | 33396,92364 | 91932 | VS Code ext 2.1.278 | 7ab37784 | StationeersPlus | 2026-09-23T16:22:48Z | BUSY |
| 112980 | 09-21 16:38:15 | 113532 | 114988 | VS Code ext 2.1.278 | 278efa76 | BrowserAI | 2026-09-23T16:24:56Z | BUSY (this investigation) |
| 123300 | 09-21 16:39:45 | 96112  | 48692  | ~/.local/bin 2.1.275 | 9cec43d7 | Downloads | 2026-09-21T20:37:44Z | idle 46 h |
| 134344 | 09-22 15:20:49 | 78028  | 151320 | ~/.local/bin 2.1.278 | f5f0e268 | Downloads | 2026-09-23T16:15:37Z | idle ~5 min |
| 70216  | 09-22 15:30:18 | 117576 | 62540  | VS Code ext 2.1.278 | 2c0334f6 | PortainerCompose | 2026-09-22T14:06:37Z | idle 28 h |
| 136536 | 09-22 15:30:21 | 127492 | 156932 | VS Code ext 2.1.278 | 13471a60 | PortainerCompose | 2026-09-22T17:41:07Z | idle 25 h |
| 153624 | 09-22 15:30:24 | 148708 | 124552 | VS Code ext 2.1.278 | 88ad24c5 | PortainerCompose | 2026-07-30T15:19:58Z | never used since resume |
| 92748  | 09-22 15:30:26 | 140412 | 31480  | VS Code ext 2.1.278 | 6475e522 | PortainerCompose | 2026-09-16T05:46:23Z | never used since resume |
| 40456  | 09-22 15:30:29 | 129956 | 120548 | VS Code ext 2.1.278 | 3f601ad4 | PortainerCompose | 2026-06-21T06:49:06Z | never used since resume |
| 118492 | 09-22 15:30:31 | 129668 | 133116 | VS Code ext 2.1.278 | 2e943f9d | PortainerCompose | 2026-06-25T22:54:11Z | never used since resume |
| 152868 | 09-22 15:30:46 | 119248 | 153064 | VS Code ext 2.1.278 | a983831d | PortainerCompose | 2026-06-25T15:27:03Z | never used since resume |
| 152476 | 09-22 15:30:48 | 122912 | 107052 | VS Code ext 2.1.278 | 47bc8018 | PortainerCompose | 2026-06-20T21:37:15Z | never used since resume |
| 142648 | 09-22 15:30:51 | 122212 | 155776 | VS Code ext 2.1.278 | e4b2706c | PortainerCompose | 2026-06-10T09:23:53Z | never used since resume |
| 157248 | 09-22 15:30:53 | 133904 | 125356 | VS Code ext 2.1.278 | aa2bef6d | PortainerCompose | 2026-05-26T12:50:41Z | never used since resume |
| 132432 | 09-22 15:30:56 | 146480 | 114836 | VS Code ext 2.1.278 | 0e8f8c4e | PortainerCompose | 2026-05-26T12:57:31Z | never used since resume |
| 132196 | 09-22 15:30:57 | 114892 | 117928 | VS Code ext 2.1.278 | ce2f7f34 | PortainerCompose | 2026-05-26T12:55:24Z | never used since resume |
| 118300 | 09-22 15:30:59 | 115876 | 146740 | VS Code ext 2.1.278 | 24ccbcec | PortainerCompose | 2026-05-26T12:53:55Z | never used since resume |
| 135776 | 09-22 15:31:00 | 121576 | 153980 | VS Code ext 2.1.278 | bb6fd5be | PortainerCompose | 2026-05-26T12:54:43Z | never used since resume |
| 141104 | 09-22 15:31:06 | 109324 | 127348 | VS Code ext 2.1.278 | 593a0fb5 | PortainerCompose | 2026-05-23T20:23:55Z | never used since resume |
| 126204 | 09-22 15:31:15 | 55320  | 138992 | VS Code ext 2.1.278 | 33cd2480 | PortainerCompose | 2026-05-23T00:14:50Z | never used since resume |
| 91000  | 09-22 21:14:24 | 25372  | 138580 | VS Code ext 2.1.278 | 86a38f2b | PortainerCompose | 2026-09-22T20:06:37Z | idle 22 h |

## Session directories each server has served (whole log retention, 2026-09-15 onward)

Only 4 of 22 have ever opened a session. The other 18 emitted 12 startup lines each and nothing more.

| server | session directories | state now |
|---|---|---|
| 46760 | StationeersPlus `.work\2026-09-20-spraypaintplus-v1.12.0\browser-post`; `.work\2026-09-21-compliance-pass\browser-meta-render-check`; `.work\2026-09-23-release-verify\browser` | all destroyed; ALSO holds the lock on `.work\2026-09-20-spraypaintplus-v1.12.0\browser` (alive, no browser process) |
| 88232 | `C:\Users\jori\Downloads\tmp-nuvio-selfhost-research\browser-nuvio-faq` | destroyed |
| 112980 | `C:\Source\SixFive7\BrowserAI\.work\2026-09-21-social\session` | destroyed |
| 134344 | `...\tmp-mvno-nummers\browser-acm-nummerformulier`; `...\browser-eurlex-coin`; `...\browsertest-termen` | all destroyed (last one 2026-09-23T16:14:42Z) |

## The chrome tree the maintainer saw

From `browserai-20260923-000.log`, sweep by pid 63008 at 2026-09-23T15:38:44Z (17:38:44 local):

    candidates=8 terminated=0 spared=1 byProfileLock=0 unattributable=7
    rejectedTitles=3 indexRemoved=0 liveMarkers=[reclaimed=0 held=24 ...]

    PID 93052, 59508, 88512, 109488, 94268, 55548, 154328
    — all C:\Users\jori\AppData\Local\BrowserAI\browsers\chromium-1245\chrome-win64\chrome.exe

All seven confirmed GONE (`Get-Process -Id`, with a live-pid positive control).
683 processes scanned machine-wide: zero under the BrowserAI data root.

## Stale-record inventory

- `BrowserAI.app\live`: 23 markers, 22 servers + 1 = pid 95472 `BrowserAI.exe` (the
  configuration app, running since 2026-09-17 20:06). No stale markers.
- `BrowserAI\instances`: 30 dirs, 22 live. 8 orphans from today's 17:36 suite run:
  105728, 113648, 134072, 143112, 18580, 25816, 65804, 77332. Every `profile/` under
  every instance dir is EMPTY — the real profile lives in the session directory.
- `BrowserAI\live` (data root) is empty; the markers live under the APP root.

## Pid reuse in the log — a trap

`grep "pid=112980@"` alone returns lines from 2026-09-16, before that process existed
(it started 2026-09-21 16:38). The log writes `pid=<pid>@<processCreatedFileTime>`;
only the pair is an identity. Filtering on the pid alone attributed saturation-test
sessions to four servers that never touched them.

## The index test — what `browserai_list` reported vs what the index file held

Index = `C:\Users\jori\AppData\Local\BrowserAI\index\`, one file per session,
named by the SHA-256 of the upper-cased trimmed path, containing that path.

BEFORE (6 entries, read directly off disk):

    2026-09-17 21:14:33  ...\PortainerCompose\.work\2026-09-17-cs2-maps-1000\verify-browser   [dir exists]
    2026-09-17 20:45:02  ...\PortainerCompose\.work\2026-09-17-cs2-maps-1000\browser          [dir exists]
    2026-09-21 02:02:42  ...\StationeersPlus\.work\2026-09-20-spraypaintplus-v1.12.0\browser  [dir exists]
    2026-09-23 17:36:12  ...\BrowserAI\.work\test-scratch\sessions-83b7ffa8.../alpha          [DIR GONE]
    2026-09-23 17:36:21  ...\BrowserAI\.work\test-scratch\sessions-83b7ffa8.../gamma-moved    [DIR GONE]
    2026-09-23 17:36:22  ...\BrowserAI\.work\test-scratch\sessions-83b7ffa8.../gamma-copy     [DIR GONE]

`C:\Source\SixFive7\BrowserAI\.work\test-scratch\` does not exist at all — the
17:36 suite run deleted its scratch tree and left three index entries behind.

`browserai_list C:\Source\SixFive7` reported exactly 3 sessions — the three that
exist — with correct purposes, sizes and in-use state. It did not report the three
dead ones and did not error on them.

AFTER (same directory re-read immediately after the call): 3 entries, exactly the
three live ones. The stale entries were pruned BY THE READ.

`browserai_list C:\Users\jori\Downloads` → "No BrowserAI sessions under ...".

Disk cross-check (`find /c/Source /c/Users/jori/Downloads -name browserai.data`)
returns exactly those same 3 — no session on disk was missed by the index, and no
index entry now lacks a directory. The index and the filesystem agree.

Lock files (`browserai.lock`, which names its holder):
- cs2-maps `browser` and `verify-browser`  -> processId 39664 (DEAD server) — content stale,
  kernel says unheld, and `browserai_list` correctly reports "in use: no".
- spraypaint `browser` -> processId 46760 @134344157375808467 = the LIVE server owned by
  the StationeersPlus session; `browserai_list` reports "in use: YES". Agrees.
