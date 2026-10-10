<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-10 -- an apply whose last rename fails, and what puts the program back

What the kb entry
[An apply whose last rename fails leaves no program, and the installer puts it back](../../../kb/packaging/velopack.md#an-apply-whose-last-rename-fails-leaves-no-program-and-the-installer-puts-it-back----measured-2026-10-10)
and the corrected [hazard row](../../../HAZARDS.md#hazard-index) were read from, and
what the recovery note in [`README.md`](../../../README.md#install) rests on. Lane
VELO took it on 2026-10-10 between 00:47Z and 01:03Z, under the suite and installer
locks, with a stand-in under its own pack id, `BrowserAI.Measure`, installed with its
`Setup.exe --silent` into `%LOCALAPPDATA%\BrowserAI.Measure`, updated the way
BrowserAI updates, `WaitExitThenApplyUpdates(silent: true, restart: true)`, and
uninstalled; nothing of `BrowserAI.app` was touched. Velopack and vpk 1.2.161,
Windows 11 Pro 10.0.26300.9550.

The research that read the same failure in Velopack's source first, the day before,
is beside it as `reading/findings.txt`.

| Path | What it is |
|---|---|
| `reports/all-20261010T004730Z-report.txt` | The first session, round by round: the control and four rounds with the stock 1.2.161 `Update.exe` and a file of the new version held open, three with no sharing and one with read, write and delete sharing; then a control and three rounds with a fixed `Update.exe` built one blank line before the fix's commit |
| `reports/commit-20261010T005755Z-report.txt`, `reports/commit-extra-20261010T010104Z-report.txt` | The control and four rounds with a build of the fix's own commit, `84af7e5` on the maintainer's fork |
| `runs.zip` | The three sessions' folders whole: the orchestrator's and the rounds' records, every process's journal, each round's slice of Velopack's log and the per-app Velopack log; and the console output of the three sessions and of the three packs |
| `rig/orchestrate.ps1.txt` | The driver: install, update with the hold, uninstall while broken, repair, the markers inside and outside the install folder, final uninstall, and the cleanup checks |
| `rig/holder.ps1.txt` | The windowless process that finds the new version's folder by its `sq.version` and holds `measure-lock.txt` open for 60 s, with the sharing it is told |
| `rig/pack.ps1.txt`, `rig/report.ps1.txt`, `rig/outside-child.ps1.txt`, `rig/control.json.txt`, `rig/Directory.*.props.txt` | The packs, the report, a child started from outside the install folder, the stand-in's control file and its build props |
| `rig/app/*.txt` | The stand-in, a .NET 10 Windows program with Velopack at exactly 1.2.161 |
| `myapp/report.txt`, `myapp/runs.zip`, `myapp/rig/` | The same rig under the app name `MyApp`, the run the upstream report was written from, 2026-10-10 at 01:18Z: a control, one round with the stock `Update.exe` and the file held, and one with the fix's own build, and the rig as it ran; `myapp/originals.sha256` for its changed bytes |
| `reading/findings.txt` | The source reading of 2026-10-09: how 1.2.161 does the swap, the upstream issues and pull requests, and the directions offered |
| `originals.sha256` | Every file whose bytes were changed below, with the original's size and SHA-256 |

## How these bytes depart from the ones taken

- **The account's name is replaced** by `<user>` wherever it appeared, in the user
  profile's paths and as a file's owner, in the reports and in every file inside
  `runs.zip`; `originals.sha256` carries each original's digest and size and how
  many places were replaced. Nothing else in them was changed.
- **The scripts and sources are stored as text** under a `.txt` name, so that nothing
  in the suite that reads the tree's code reads them as this repository's own.
- **The lane's own report is not here.** It carried the drafts of the upstream issue
  and pull request, which were posted on 2026-10-10 as
  [velopack/velopack#1086](https://github.com/velopack/velopack/issues/1086) and
  [#1087](https://github.com/velopack/velopack/pull/1087) and are read there; the measurements it
  reports are the run reports above.
- Everything else is as written, line endings aside, which this repository stores as
  LF the way [the index](../README.md) says of every batch.
