<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-08 -- step 0 of the one-binary build: Velopack, toasts, the Task Scheduler and the input reads

**What this is.** Step 0 of [the one-binary design](../../design/one-binary/README.md),
taken 2026-10-08 before any product code of the build, on Windows 11 Pro 10.0.26300,
in four parts, each by its own agent of the root session and each in its own
directory here:

| Part | What it is | Taken |
|---|---|---|
| [`research/`](research/FINDINGS.md) | **Read only.** Velopack 1.2.161's restart, failure paths, hooks and local source, read in its source at tag `1.2.161` (commit `92d6a1c91716729d449034df5c50307dcce39493`) and in `velopack/velopack.docs` at `1ca8eea6017fb9c5743e070575a9f28da08c26c1`; toasts that update in place or stay on screen, from Microsoft's documentation and the Windows SDK 10.0.26100.0 headers; the Task Scheduler's instance policy, End, the interactive token and the foreground; and what the input reads cost. No window, no toast, no task and no install | before 14:01Z |
| `tasks/` | **Measured with stand-ins**: twenty run requests at once under `IgnoreNew`, a missing and a disabled task, End and Stop with and without a top-level window and against a child, the scheduler's share of a start, the cost of the input reads, and a child left behind when the task's process exits | 14:10Z to 14:40Z |
| `velopack/` | **Measured with a stand-in under its own pack id, `BrowserAI.Measure`**: a local folder as the source and the pre-release it offers, a silent apply with a restart, a failed apply, the update hook's 15 s and what it starts, and the download's side effects; and Velopack's own checks, read | 14:09Z to 14:21Z |
| `screen/` | **Measured on the maintainer's own screen, with his leave** ("do the things that disturb me now", 13:46:28Z): a reminder toast with a live countdown, its replacements, and a browser window started by a process the Task Scheduler started against one started from a shell | 13:58Z to 14:19Z |

**153 files beside this README, 2,490,254 bytes as this repository stores them.** Each part has its agent's own
report as `FINDINGS.md`; none of the four agents could write a report file, so the
root session saved each from the agent's reply, and the first line of each says so.
Nothing of `BrowserAI.app` was touched by any part.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: Velopack](../../../kb/packaging/velopack.md) | "A local folder, a silent apply with a restart, a failed apply, and the update hook" |
| [kb: notifications](../../../kb/windows/notifications.md) | "A toast that counts down in place, stays until acted on, and is replaced" |
| [kb: processes](../../../kb/windows/processes.md) | "What the Task Scheduler does with a second run, a missing or disabled task, End, and a child left behind", "Reading the window in front and the time of the last input", and what a browser started by a task-started process did with the foreground |
| [kb: re-verification](../../../kb/re-verification.md) | The rows those entries carry |
| [the one-binary design](../../design/one-binary/README.md) | The update's handover and after-update mode, "never a second copy", the errors for a missing and a disabled task, never stopping the background with End, the hidden top-level window, the toasts and the input check |

## What is in each part

- **`research/`**: the report, with every source pinned and every line cited, and
  the two clone logs.
- **`tasks/`**: the report; `src/`, the stand-in (`StandIn/Program.cs.txt`, a NativeAOT
  Windows-subsystem program that logs every message and its own exit) and the driver
  (`Rig/`), with the analysis and the import-table reader; `runs/20261008T1411/`, each
  case's `summary.tsv` or `results.tsv`, the processes the run recorded
  (`registry.tsv`), the shared job's other members before the End cases, and the
  final proof that nothing was left; and `runs.zip`, **every case's logs and per-round
  tables, 1,543 files**, SHA-256
  `57944dcbce606f66587b0cd3209c44051ab88721a07132220977fd91434d8716`.
- **`velopack/`**: the report and `tables.md`, the per-run tables; the stand-in under
  `app/`; the driver `orchestrate.ps1.txt`, `pack.ps1.txt`, the analysis and the
  proof that everything was removed; `runs/*/analysis.txt`, the per-process digest;
  and `runs.zip`, **every run's journals, its slice of Velopack's log and the
  install's state before and after, 242 files**, SHA-256
  `b113b8affaec2702e68106a98179eb96cc28615d045da4ec625c7e3069d207c7`.
- **`screen/`**: the report; `part1/`, the toast script, its monitor, both logs and
  the five cropped PNGs (`toast1-t02s.png`, `toast1-t30s.png` and `toast1-t60s.png`
  of the ready toast counting down, `toast2-installing.png` and
  `toast3-installed.png`); `launcher/`, the launcher that started each browser; and
  `runs/`, every launcher's log, the twelve counted runs and the one invalid run the
  report excludes; with the task definition that was registered and removed.

## What was changed, and what was left out

- **Every source file and script is stored with `.txt` appended**, as a record and not
  as this repository's code; each part's `originals.sha256` names each one with the
  digest of what it was. The four reports are stored as Markdown with the two SPDX
  header lines prepended, and their originals' digests are there too.
- **The profile path, the user name and the machine name** are replaced by
  `%USERPROFILE%`, `%USERNAME%` and `%COMPUTERNAME%` wherever they appeared, under a
  `.trimmed.` name beside this README and under the original name inside a
  `runs.zip`.
- **The terminal escapes of one log are removed**: vpk's coloured output in
  `velopack/pack-first.log`, stored as `pack-first.trimmed.log`, with the original's
  digest in that part's `originals.sha256`.
- ⚠️ **In `screen/` the title of every window of the person at the machine is cut**,
  replaced by "(a window of the person at the machine, its title cut)": the
  measurement read the window in front, and he was working while it ran, so his
  windows' titles named his pages and his projects. The titles of the measurement's
  own windows, the toasts' and the browsers' it started, are left as read.
- **Left out by directory**, with no digest per file: the two clones of Velopack's
  source and the clone of its documentation that were read, 1,177, 1,177 and 343
  files; the feed folders the stand-in was packed into, 147 files and 341,227,488
  bytes, and the publish output of each packed version, 119 files and 59,972,687
  bytes; the scratch copies of `chromium-1247` and `firefox-1553` the screen part
  started; and build output.
- **Left out one by one**, each in its part's `left-out.sha256`: a copy of the Windows
  App SDK's `AppNotificationUtility.cpp` the research read, a third party's code; and
  the screen part's exports of `HKCU\Software\Mozilla` and `HKCU\Software\Chromium`
  before and after, which hold the installed Firefox's own state. The report says what
  changed in them and that it was removed.

## Privacy

Nothing here names the user profile or the machine, and no window title of the person
at the machine is left. A scan for the profile path, the user name, the machine name,
the maintainer's e-mail addresses and the names of his other projects found nothing in
the files beside this README or inside either `runs.zip`, and its positive control
found every planted needle. The user's SID appears in the registered task's definition
and in the tasks part's final proof, as it already does in
[`2026-09-24-ipc-review`](../2026-09-24-ipc-review/README.md).
