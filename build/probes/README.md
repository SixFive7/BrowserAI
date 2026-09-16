<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# build/probes

**The rigs that re-establish a measurement.** Where a kb entry, a hazard row or
a re-verification row says *re-establish it with …*, this is the thing it names.
One directory per measurement, named `<date>-<name>`, each with a `README.md`
saying what it re-establishes and which row cites it.

**Nothing here builds and nothing here runs in the suite.** No project
references these files, `dotnet build` never sees them, and no test invokes one.
They are read by the same tree-as-text scans as everything else under `build/`,
so they carry the SPDX header, and a `.cs` rig here obeys the house rules the
product obeys — `CREATE_NO_WINDOW` on every launch, never a kill by image name,
every `STARTUPINFO` field paired with its flag.

⚠️ **A rig is the shape that produced a number, never the authority for it.**
Every entry these support is written to stand on its own procedure; read the
entry first. A rig that no longer runs does not weaken the measurement, and
editing one to make it run again does not re-take it — only a run does that, and
a run gets a date.

⚠️ **Several of these touch machine-wide state**: the shared provisioned
browsers root, the real Add/Remove key, the real client registration. Each
README says so where it applies. Read it before running anything.

| Probe | Re-establishes |
|---|---|
| [`2026-08-18-truncation`](2026-08-18-truncation/README.md) | The client's silent per-string truncation budget |
| [`2026-08-19-rename-under-browser`](2026-08-19-rename-under-browser/README.md) | What a running browser refuses to let you rename |
| [`2026-08-27-desktop-heap`](2026-08-27-desktop-heap/README.md) | The desktop-heap ceiling nothing reports |
| [`2026-09-14-firstrun`](2026-09-14-firstrun/README.md) | A non-silent install starting the app in a console window |
| [`2026-09-14-webp-ask`](2026-09-14-webp-ask/README.md) | A WebP screenshot past 16,383 px coming back empty |
| [`2026-09-15-consoleprobe`](2026-09-15-consoleprobe/README.md) | A parked read on stdin that nothing wakes |
| [`2026-09-15-corpse`](2026-09-15-corpse/README.md) | An app still alive long past logging that it is exiting |
| [`2026-09-15-install`](2026-09-15-install/README.md) | Installing the published release and watching it |
| [`2026-09-16-garbage`](2026-09-16-garbage/README.md) | What old versions and old install paths leave on a machine |
| [`2026-09-16-icon`](2026-09-16-icon/README.md) | Rendering the shipped assets, and reading the icon back out |
| [`2026-09-16-release`](2026-09-16-release/README.md) | What Setup asks before installing over an install |

The records these produced are in
[`docs/evidence/`](../../docs/evidence/README.md).
