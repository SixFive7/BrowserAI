<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# docs/probes

**The rigs that produced a measurement.** Where a kb entry, a hazard row or a
re-verification row says *re-establish it with …*, this is the thing it names.
One directory per measurement, named `<date>-<name>`, each with a `README.md`
saying what it re-establishes and which row cites it. What they produced is in
[`docs/evidence/`](../evidence/README.md).

⚠️ **A rig is the shape that produced a number, never the authority for it.**
Every entry these support is written to stand on its own procedure; read the
entry first. A rig that no longer runs does not weaken the measurement, and
editing one to make it run again does not re-take it — only a run does that, and
a run gets a date.

⚠️ **Several of these touch machine-wide state**: the shared provisioned
browsers root, the real Add/Remove key, the real client registration. Each
README says so where it applies. Read it before running anything.

## Why this is under `docs/` and not under `build/`

⚠️ **ONE FILE KEEPS THESE HERE NOW, AND IT IS A REAL VIOLATION — *corrected
2026-09-17 (previously "**Because `build/` is code this repository builds and
runs, and everything under it is read by the tree-as-text scans that govern
product code.** These rigs are scratch from before several of those rules, and
**7 of the 14 directories hold at least one file that would trip
`NeverByImageNameTests`** — the predicate being a file among the extensions that
scan reads (`.cs`, `.ps1`, `.psm1`, `.mjs`, `.js`) containing one of its five
forbidden needles. `Get-Process` in 10 files, `Win32_Process` in 7, and in
`2026-09-14-firstrun/observe.ps1` a real `GetProcessesByName`; `taskkill` and
`szExeFile` appear in none", and "⚠️ **Every one of those uses is by pid or by
parent pid, and not one matches on an image name**, so the rigs satisfy the RULE
— *never match, count or terminate by name* — and fail the SCAN, which is a
substring scan that cannot tell the two apart")*.**

**The scan reads the FILTER rather than the API from 2026-09-17** — **Q203**,
implemented in
[`ProcessSelection`](../../tests/BrowserAI.Tests/Harness/ProcessSelection.cs) with
synthetic controls pointing both ways — so the fourteen false positives are gone.
**Re-measured through the real scan on the day it landed**, the predicate being
*a file among the extensions the scan reads whose code text selects a process by
its image name*:

| | Files flagged | Rigs flagged |
|---|--:|--:|
| **Old**, five substrings anywhere in the file | **15 of 36** | **7 of 14** |
| **New**, a name FILTER rather than the API | **1 of 36** | **1 of 14** |

**Fourteen of the fifteen were false positives and the fifteenth is not.**
[`2026-09-14-firstrun/observe.ps1`](2026-09-14-firstrun/README.md) really does
call `GetProcessesByName`, over a literal watch list, which is *matching and
counting by name* — the thing
[the rule's own remark](../../tests/BrowserAI.Tests/NeverByImageNameTests.cs)
forbids, as against the *observing* it permits. The previous sentence here said
every use was by pid or parent pid; that was true of fourteen files and false of
this one.

⚠️ **And it cannot be re-spelled pid-keyed without changing what the rig
measured.** Its watch list is `conhost`, `OpenConsole`, `WindowsTerminal`,
`node`, `chrome`, `firefox`, `headless_shell` and four install names — it is
watching for a **console host appearing anywhere on the machine**, which is the
whole finding, and no pid or path form expresses that. Editing it to satisfy the
scan would make the record no longer the thing that was run, which this file's own
warning above forbids: *only a run re-takes a measurement, and a run gets a date.*

**So the boundary being kept here is code-we-run against records-of-what-was-run,
and it is the boundary the scan already draws** — `RepositoryLayout.SourceAndScriptFiles`
is `src`, `tests` and `build`, and has never read `docs/`. Nothing here is built,
nothing in the suite invokes it, and a rig is not sanctioned to be re-run
unaltered. **This is not a hiding place and must not become one**, and it is now
one file wide rather than seven rigs wide. *Placed here 2026-09-16, when the
scratch directory was retired; `build/probes/` existed for one commit, `bc68db0`,
and went red on exactly this.* **The move to `build/probes/` was performed and
reverted on 2026-09-17** — thirteen rigs pass the new scan and `observe.ps1` does
not, and splitting the collection across two homes, or rewriting the rig, are both
decisions for whoever owns the rule rather than for the batch that improved the
scan.

| Probe | Re-establishes | Trips the scan |
|---|---|:-:|
| [`2026-08-18-truncation`](2026-08-18-truncation/README.md) | The client's silent per-string truncation budget | |
| [`2026-08-19-rename-under-browser`](2026-08-19-rename-under-browser/README.md) | What a running browser refuses to let you rename |  |
| [`2026-08-27-desktop-heap`](2026-08-27-desktop-heap/README.md) | The desktop-heap ceiling nothing reports |  |
| [`2026-09-14-firstrun`](2026-09-14-firstrun/README.md) | A non-silent install starting the app in a console window | yes |
| [`2026-09-14-webp-ask`](2026-09-14-webp-ask/README.md) | A WebP screenshot past 16,383 px coming back empty |  |
| [`2026-09-15-consoleprobe`](2026-09-15-consoleprobe/README.md) | A parked read on stdin that nothing wakes | |
| [`2026-09-15-corpse`](2026-09-15-corpse/README.md) | An app still alive long past logging that it is exiting |  |
| [`2026-09-15-install`](2026-09-15-install/README.md) | Installing the published release and watching it | |
| [`2026-09-16-garbage`](2026-09-16-garbage/README.md) | What old versions and old install paths leave on a machine | |
| [`2026-09-16-icon`](2026-09-16-icon/README.md) | Rendering the shipped assets, and reading the icon back out | |
| [`2026-09-16-provisioning`](2026-09-16-provisioning/README.md) | What a first-run browser download costs on the wire, on disk and on the clock | |
| [`2026-09-16-release`](2026-09-16-release/README.md) | What Setup asks before installing over an install | |
| [`2026-09-16-resume`](2026-09-16-resume/README.md) | What a resume costs and which stores survive it |  |
| [`2026-09-17-cost-ratios`](2026-09-17-cost-ratios/README.md) | Firefox against Chromium on RAM, first paint, idle CPU and profile disk |  |
