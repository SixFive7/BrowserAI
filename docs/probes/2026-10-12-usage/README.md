<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-12 -- what a reading of BrowserAI's CPU and memory costs

Establishes
[What a reading of BrowserAI's CPU and memory costs](../../../kb/windows/processes.md#what-a-reading-of-browserais-cpu-and-memory-costs----measured-2026-10-12).
Evidence:
[`docs/evidence/2026-10-12-usage/`](../../evidence/2026-10-12-usage/README.md).

**Why it exists.** The maintainer's 59 b, 62 a and 65 a of 2026-10-11: the dashboard shows
what BrowserAI's own processes use, read every two seconds while a tab shows it, and the
build was to measure what a reading costs and record it. A reading asks Windows about every
process of every session's job, so its cost grows with the processes a session's browser
runs; this rig puts real headless Chromiums in jobs of their own and reads them the way the
background does.

## What is here

| File | What it does |
|---|---|
| `Usage/Usage.csproj` | The rig, published NativeAOT for win-x64 as the product is. It compiles in two product files as they stand in `src/`: `ProcessUsage.cs` and `JobObject.cs`, so what is measured is the shipped job read and the shipped member read |
| `Usage/Program.cs` | `measure --chrome PATH --out DIR [--sessions N] [--readings R] [--paced SECONDS]`: starts N headless Chromiums, each in a kill-on-close job of its own, waits for them to settle, reads every member once and writes its working set, private working set, shared working set and shared commit, then times one reading of every job back to back R times, and then a reading every 2 s for the paced seconds against the same wait with no reading, by this process's cycles and CPU time. A reading composes the product's calls the way `BackgroundPageUsage.ReadSession` does: the job's CPU total, its members, each member read after a membership check, and its image read once |
| `Usage/Native.cs` | The rig's own calls: starting a browser suspended, putting it in its job and letting it run, and this process's cycles and CPU time |
| `shared-probe.ps1` | The exploration that came first, the same day: one headless Chromium in a job, each member's counters printed and a thousand readings of the job timed, in C# compiled by `Add-Type` and calling Windows directly, before the product's reader existed. It took the two outputs in the evidence's `explore` folder, which decided which of the two shared figures the page shows |
| `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props` | Empty but for one property, so the repository's build settings and package versions do not reach the rig. `BrowserAI.slnx` does not name the project |

## What keeps it off the rest of the machine

- **Nothing shows a window.** Every Chromium is started with `--headless=new` and
  `CREATE_NO_WINDOW`, from a profile of its own under `--out`, on `about:blank`.
- **It ends what it started and nothing else.** Each Chromium is in a kill-on-close job the
  rig created before the browser ran, and closing the job is how the rig ends it.
- **It selects no process by image name.** It reads the members of its own jobs; an image
  path is read to tell a browser from its server, the way the product does, and compared as a
  full path under the browsers root.
- ⚠️ **It runs a browser from the shared browsers root**, so it is run under the suite's lock:
  a browser of the root that is in no session's job is what a containment arm of a running
  gate counts as an escapee.

## Running it

From this directory, with the .NET 10 SDK and the MSVC linker NativeAOT needs:

```
dotnet publish Usage -c Release -r win-x64
Usage\bin\Release\net10.0-windows\win-x64\publish\Usage.exe measure --chrome <browsers root>\chromium-1247\chrome-win64\chrome.exe --out <a directory> --sessions 1 --readings 1000 --paced 120
```

Started detached, with its output to a file, so no console window opens.
