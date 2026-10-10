<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-08 -- whether a banner pops up when one update toast replaces another

What the kb entry
[A banner for every update toast: its own tag against one tag shared](../../../kb/windows/notifications.md#a-banner-for-every-update-toast-its-own-tag-against-one-tag-shared----measured-2026-10-08)
was read from. It is the measurement the maintainer's 2.1 a of 2026-10-10 wrote
into the record: the update toasts' design, each toast under a tag of its own with
the others removed first, popped a banner 85 times of 86 while the old banner was
on screen. The same night's shots are what the holders line's budget,
`UpdateToastContent.HoldersLineCharacters`, rests on
([kb/numbers.md](../../../kb/numbers.md)).

Taken on the maintainer's screen with his leave, 2026-10-08 from 22:45Z to 23:48Z,
by a research agent of the root session while he was away: 112 rounds, 300 shows,
Windows 11 Pro 10.0.26300.9550, Windows PowerShell 5.1 (10.0.26100.8972) under its
own application id. The rig composed the toasts the way
`src/BrowserAI.Core/Updates/UpdateToastContent.cs` did at `25acb977` and raised them
the way `UpdateToasts` does, each round in a group of its own.

| Path | What it is |
|---|---|
| `findings.txt` | The research agent's own account of the run, its results, conditions, cleanup and questions, as it wrote it |
| `rig/rig.ps1.txt` | The orchestrator, run with Windows PowerShell 5.1: `powershell -NoProfile -ExecutionPolicy Bypass -File rig.ps1 -Mode dry\|run -Plan core\|long\|<codes>` |
| `rig/Rig.cs.txt` | The monitor `rig.ps1` compiles with `Add-Type`: the pixel sampler of the screen's bottom-right corner every 40 ms, the crops, the peak meter of every audio session and the conditions read before each round |
| `rig/hand.ps1.txt` | One windowless process that raises, updates and removes toasts on the orchestrator's word, started with `CreateNoWindow` |
| `rig/evhand/*.txt` | The event hand of the last two batches: a .NET 10 process that raises toasts and records their `Dismissed`, `Failed` and `Activated` events |
| `rig/analyze.ps1.txt`, `rig/summarize.ps1.txt`, `rig/peek.ps1.txt` | What turned the logs into the two tables below, and a look at the toast history |
| `out/summary.txt` | Every count the kb entry quotes: by category and whether the old banner was on screen, by how long it had been up, by scenario and show position, every show with no banner, the conditions across all rounds, the banner geometry and the times |
| `out/tables.txt` | The first batch, `core`, round by round |
| `out/shows.csv` | All 300 shows, one row each |
| `out/runs.zip` | The six batches' logs and per-round records (`rig-run-*.log`, `rounds-run-*.jsonl`), the two dry runs, and each batch's console output |
| `out/left-out.sha256` | The 219 crops and 14 contact sheets of the screen, left out, with their digests and sizes |

## How these bytes depart from the ones taken

- **No picture of the screen is here.** The crops were taken of the corner of his
  screen where banners appear, and they stay out of the repository on purpose; each
  is named in `out/left-out.sha256` with its SHA-256 and size. The one reading taken
  off a crop, that the holders line was cut at *1 visible wi*, is quoted in the kb
  entry.
- **The scripts are stored as text** under a `.txt` name, so that nothing in the
  suite that reads the tree's code reads them as this repository's own.
- **The logs and records are zipped** into `out/runs.zip`, unchanged inside it.
- Everything else is as written, line endings aside, which this repository stores as
  LF the way [the index](../README.md) says of every batch. The registry exports
  taken before and after the run are not here: they hold the account's own
  notification settings, and the findings name the only values that moved.
