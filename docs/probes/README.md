<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# docs/probes

**The rigs that produced a measurement.** Where a kb entry, a hazard row or a
re-verification row says *re-establish it with ...*, this is the thing it names.
One directory per measurement, named `<date>-<name>`, each with a `README.md`
saying what it re-establishes and which row cites it. What they produced is in
[`docs/evidence/`](../evidence/README.md).

⚠️ **A rig is the shape that produced a number, never the authority for it.**
Every entry these support is written to stand on its own procedure; read the
entry first. A rig that no longer runs does not weaken the measurement, and
editing one to make it run again does not re-take it -- only a run does that, and
a run gets a date.

⚠️ **Several of these touch machine-wide state**: the shared provisioned
browsers root, the real Add/Remove key, the real client registration. Each
README says so where it applies. Read it before running anything.

## Why this is under `docs/` and not under `build/`

⚠️ **ONE FILE KEEPS THESE HERE NOW, AND IT IS A REAL VIOLATION -- *corrected
2026-09-17 (previously "**Because `build/` is code this repository builds and
runs, and everything under it is read by the tree-as-text scans that govern
product code.** These rigs are scratch from before several of those rules, and
**7 of the 14 directories hold at least one file that would trip
`NeverByImageNameTests`** -- the predicate being a file among the extensions that
scan reads (`.cs`, `.ps1`, `.psm1`, `.mjs`, `.js`) containing one of its five
forbidden needles. `Get-Process` in 10 files, `Win32_Process` in 7, and in
`2026-09-14-firstrun/observe.ps1` a real `GetProcessesByName`; `taskkill` and
`szExeFile` appear in none", and "⚠️ **Every one of those uses is by pid or by
parent pid, and not one matches on an image name**, so the rigs satisfy the RULE
-- *never match, count or terminate by name* -- and fail the SCAN, which is a
substring scan that cannot tell the two apart")*.**

**The scan reads the FILTER, not the API, from 2026-09-17** -- **Q203**,
implemented in
[`ProcessSelection`](../../tests/BrowserAI.Tests/Harness/ProcessSelection.cs) with
synthetic controls pointing both ways -- so the fourteen false positives are gone.
**Re-measured through the real scan on the day it landed**, the predicate being
*a file among the extensions the scan reads whose code text selects a process by
its image name*:

| | Files flagged | Rigs flagged |
|---|--:|--:|
| **Old**, five substrings anywhere in the file | **15 of 36** | **7 of 14** |
| **New**, a name FILTER and not the API | **1 of 36** | **1 of 14** |

⚠️ **The denominators are the 2026-09-17 measurement and are left as measured.**
*Added 2026-09-21, when a fifteenth rig arrived* --
[`2026-09-21-webmcp`](2026-09-21-webmcp/README.md), two `.mjs` files taken during
the `@playwright/mcp` 0.0.82 review. **The NUMERATORS are what the decision below
rests on and neither moved**: that rig selects no process at all, by name or
otherwise, so the blind spot is still **one file wide** and it is still
`2026-09-14-firstrun/observe.ps1`. Re-counting the denominators would mean
re-running the whole scan over the whole directory, which is what a dated row is
for; incrementing them would be adjusting a measurement instead of taking one.
**A second true positive would be a new decision, not a precedent**, and
this rig is not one.

⚠️ *Added 2026-10-01, when
[`2026-09-25-dashboard-exposure`](2026-09-25-dashboard-exposure/README.md)
arrived* -- four `.cjs` files taken during the research into Playwright's
dashboard. **It selects no process either, so the blind spot is still one file
wide.** That was searched for and not run through the scan, whose corpus is
`src`, `tests` and `build` as it always was: no file in any of the five rigs
added since the 2026-09-17 measurement carries one of the eight spellings
`ProcessSelection` keys on, and the same search finds one in
`2026-09-14-firstrun/observe.ps1`. The five are `2026-09-17-file-paths`,
`2026-09-21-webmcp`, `2026-09-24-playwright-dashboard`, `2026-09-24-q261` and
this one. **The directory holds nineteen rigs now, counted as its
subdirectories**, eighteen before this one. The table further down lists
eighteen of them, because `2026-09-21-webmcp` never had a row there.

⚠️ *Corrected 2026-10-03 by addition (previously "The directory holds nineteen
rigs now").* **Twenty-one**, counted as its subdirectories when
[`2026-09-25-firefox-safe-mode`](2026-09-25-firefox-safe-mode/README.md) and
[`2026-10-03-client-exit`](2026-10-03-client-exit/README.md) arrived. Neither
selects a process by image name, and that was put to the real scan and not only
searched for: both rigs were copied under `build/`, `NeverByImageNameTests` was
run, and the copy was removed, with `observe.ps1` copied beside them as the
positive control, which the scan named. **The first rig passed. The second was
flagged in two files, and both are false positives of the scan's own wording**:
`ExitRig/Harness.cs` spells `taskkill` on lines that carry no `/PID` (a role
label for a process the harness found by parent pid, a method name and two case
labels, while the one launch passes `/PID` on the next line), and
`ExitRig/Native.cs` declares the `szExeFile` field of the toolhelp struct, which
nothing in the rig reads. ⚠️ **So the blind spot is still one TRUE positive
wide, and the scan would now flag three files here, not one.** Editing the rig
to quiet the scan would make it no longer the thing that ran, which the warning
at the top forbids. Whether these two false positives change the 2026-09-17
decision is the rule owner's to say; they are recorded here and not decided.

⚠️ *Added 2026-10-03, later the same night.* **Twenty-four**, counted as its
subdirectories, when
[`2026-10-03-hard-kill`](2026-10-03-hard-kill/README.md),
[`2026-10-03-state-across-close`](2026-10-03-state-across-close/README.md) and
[`2026-10-03-debugger-tools`](2026-10-03-debugger-tools/README.md) arrived. They
went through the same real scan, copied under `build/` with `observe.ps1` as the
positive control and removed after. **The second and third passed. The first was
flagged in three files, all false positives of the same kind**: `hk.cs` declares
the `szExeFile` field for a parent-pid walk; `orchestrate.ps1` spells `taskkill`
on lines without `/PID` while its one launch passes `/T /F /PID` with the root
it holds; and `reap-test.ps1` compares `Name -like` on a directory listing, not
on a process. **The scan would now flag six files across the two rigs here and
`observe.ps1`, and only `observe.ps1` selects a process by name.**

**Fourteen of the fifteen were false positives and the fifteenth is not.**
[`2026-09-14-firstrun/observe.ps1`](2026-09-14-firstrun/README.md) really does
call `GetProcessesByName`, over a literal watch list, which is *matching and
counting by name* -- the thing
[the rule's own remark](../../tests/BrowserAI.Tests/NeverByImageNameTests.cs)
forbids, as against the *observing* it permits. The previous sentence here said
every use was by pid or parent pid; that was true of fourteen files and false of
this one.

⚠️ **And it cannot be re-spelled pid-keyed without changing what the rig
measured.** Its watch list is `conhost`, `OpenConsole`, `WindowsTerminal`,
`node`, `chrome`, `firefox`, `headless_shell` and four install names -- it is
watching for a **console host appearing anywhere on the machine**, which is the
whole finding, and no pid or path form expresses that. Editing it to satisfy the
scan would make the record no longer the thing that was run, which this file's own
warning above forbids: *only a run re-takes a measurement, and a run gets a date.*

**So the boundary being kept here is code-we-run against records-of-what-was-run,
and it is the boundary the scan already draws** -- `RepositoryLayout.SourceAndScriptFiles`
is `src`, `tests` and `build`, and has never read `docs/`. Nothing here is built,
nothing in the suite invokes it, and a rig is not sanctioned to be re-run
unaltered. **This is not a hiding place and must not become one**, and it is now
one file wide, not seven rigs wide. *Placed here 2026-09-16, when the
scratch directory was retired; `build/probes/` existed for one commit, `bc68db0`,
and went red on exactly this.* **The move to `build/probes/` was performed and
reverted on 2026-09-17** -- thirteen rigs pass the new scan and `observe.ps1` does
not.

⚠️ **DECIDED 2026-09-17: ALL FOURTEEN STAY HERE.** *Corrected the same day
(previously "splitting the collection across two homes, or rewriting the rig, are
both decisions for whoever owns the rule rather than for the batch that improved
the scan")* -- it was put to whoever owns the rule and the answer was (a), leave
them. **The reason is that the blind spot is now one file wide instead of seven
rigs wide**, and the two alternatives each cost more than that: splitting
thirteen rigs into `build/probes/` and leaving one behind under `docs/` makes a
collection you have to look for in two places, and rewriting `observe.ps1` to be
pid-keyed falsifies the record of method for a scan's benefit, which is the thing
the warning at the top of this file forbids. **`observe.ps1` is the one true
positive and is named here so that it stays one** -- if a second rig ever trips
the scan, that is a new decision and not a precedent, and the honest move at
that point is to ask again instead of widening this paragraph.

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
| [`2026-09-17-file-paths`](2026-09-17-file-paths/README.md) | Which artifact pointers in a tool result `filePaths: "absolute"` reaches |  |
| [`2026-09-24-playwright-dashboard`](2026-09-24-playwright-dashboard/README.md) | Playwright's own dashboard over a scratch registry, and the reload that hangs its session list. ⚠️ *Corrected 2026-10-01 by addition: the rig never reloads, and a reload did not hang when it was measured on 2026-09-25. The rig's README says what it does show* | |
| [`2026-09-24-q261`](2026-09-24-q261/README.md) | Q261's refusal at the other end: a real client's re-dial, the sentence the model got, and what the notification did | |
| [`2026-09-25-dashboard-exposure`](2026-09-25-dashboard-exposure/README.md) | What Playwright's dashboard does to a browser it did not launch: listing, control, a pause left behind, a close, its port, its singleton, and the trace viewer beside it | |
| [`2026-09-25-firefox-safe-mode`](2026-09-25-firefox-safe-mode/README.md) | A headless Firefox launch stalling at 180 s when Shift is held as it starts, and the arms that rule out load, memory and CPU | |
| [`2026-10-03-client-exit`](2026-10-03-client-exit/README.md) | What Claude Code and Codex do to a stdio server when a session ends: whether it gets end of file, when it is killed, and how long it has | false positives |
| [`2026-10-03-hard-kill`](2026-10-03-hard-kill/README.md) | How old a browser's writes must be before a hard kill keeps them, and what an `@playwright/mcp` child does to its browser when its stdin ends | false positives |
| [`2026-10-03-state-across-close`](2026-10-03-state-across-close/README.md) | What a session keeps across a browser close, what the browsers' restore options bring back, and a close with no browser | |
| [`2026-10-03-debugger-tools`](2026-10-03-debugger-tools/README.md) | A pause armed from inside a session, and what each tool does to the call it parks | |
