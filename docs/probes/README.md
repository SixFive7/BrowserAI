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

**Because `build/` is code this repository builds and runs, and everything under
it is read by the tree-as-text scans that govern product code.** These rigs are
scratch from before several of those rules, and **six of the eleven trip
`NeverByImageNameTests`** — `Get-Process`, `Win32_Process`, and in
[`2026-09-14-firstrun/observe.ps1`](2026-09-14-firstrun/README.md) a real
`GetProcessesByName`.

⚠️ **Every one of those uses is by pid or by parent pid, and not one matches on
an image name**, so the rigs satisfy the RULE — *never match, count or terminate
by name* — and fail the SCAN, which is a substring scan that cannot tell the two
apart and [deliberately refuses an exclusion list](../../tests/BrowserAI.Tests/NeverByImageNameTests.cs):
*"the alternative would create the one place in the repository where the rule
does not apply, which is a worse trade"*. `observe.ps1` is the tolerated case the
rule's own remark names — it **observes** a name so it can record a tree, and
terminates nothing.

**So the boundary being kept here is code-we-run against records-of-what-was-run,
and it is the boundary the scan already draws** — `RepositoryLayout.SourceAndScriptFiles`
is `src`, `tests` and `build`, and has never read `docs/`. Nothing here is built,
nothing in the suite invokes it, and a rig is not sanctioned to be re-run
unaltered. **This is not a hiding place and must not become one**: the day a rig
here is wanted as a maintained tool, it moves to `build/` and is made to satisfy
the scans — which for the six means finding a pid-keyed spelling PowerShell does
not have, or deciding how the scan should read a filter rather than an API. That
decision belongs to whoever owns the rule. *Placed here 2026-09-16, when the
scratch directory was retired; `build/probes/` existed for one commit,
`bc68db0`, and went red on exactly this.*

| Probe | Re-establishes | Trips the scan |
|---|---|:-:|
| [`2026-08-18-truncation`](2026-08-18-truncation/README.md) | The client's silent per-string truncation budget | |
| [`2026-08-19-rename-under-browser`](2026-08-19-rename-under-browser/README.md) | What a running browser refuses to let you rename | yes |
| [`2026-08-27-desktop-heap`](2026-08-27-desktop-heap/README.md) | The desktop-heap ceiling nothing reports | yes |
| [`2026-09-14-firstrun`](2026-09-14-firstrun/README.md) | A non-silent install starting the app in a console window | yes |
| [`2026-09-14-webp-ask`](2026-09-14-webp-ask/README.md) | A WebP screenshot past 16,383 px coming back empty | yes |
| [`2026-09-15-consoleprobe`](2026-09-15-consoleprobe/README.md) | A parked read on stdin that nothing wakes | |
| [`2026-09-15-corpse`](2026-09-15-corpse/README.md) | An app still alive long past logging that it is exiting | yes |
| [`2026-09-15-install`](2026-09-15-install/README.md) | Installing the published release and watching it | |
| [`2026-09-16-garbage`](2026-09-16-garbage/README.md) | What old versions and old install paths leave on a machine | |
| [`2026-09-16-icon`](2026-09-16-icon/README.md) | Rendering the shipped assets, and reading the icon back out | |
| [`2026-09-16-release`](2026-09-16-release/README.md) | What Setup asks before installing over an install | |
