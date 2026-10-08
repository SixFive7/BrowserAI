<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-08 - lane S1's gate at 079e3d1, the first after the tool list was compiled into the binary

**What this is.** The two-shell ordinary gate lane S1 ran after it pushed
`e1274c2f`, `9f46f830` and `079e3d1c` to `master`, under the push rule of
2026-10-04 (push first, gate after, fix forward). It ran from the lane's worktree,
`.work\wt\s1b`, checked out at `079e3d1c` with nothing uncommitted. `e1274c2f` is the
commit that answers `tools/list` from the list compiled into the binary and starts
no Playwright until a session opens.

| Run | When | Build | Result |
|---|---|---|---|
| `079e3d1-ps/` | 2026-10-08, 23:18:18Z to 23:23:27Z | `079e3d1c`, 1.1.1-alpha.0.240, PowerShell half, `FULL RUN`, publish `FRESH`, release installer `PRESENT` | **1075 of 1079** |
| `079e3d1-bash/` | 23:23:27Z to 23:28:48Z | the same commit and binary, Git Bash half, `FULL RUN`, publish `FRESH`, release installer `PRESENT` | **1073 of 1079** |

**Four reds in both halves are `e1274c2f`'s, tests it left holding the child it
deleted, and one more is the worktree's.**
`SandboxFlagTests.NoProcessOfOurBrowserRunsWithTheSandboxDisabled` counted at least
two `node.exe` children and found one, the session's.
`UpdateInProgressTests.AServerStartedWhileItsInstallsUpdaterRunsListsItsToolsRefusesCallsAndServesOnceTheUpdaterHasGone`
held a child from the payload in the job of a server started during an update, and
found none. `ErrorCatalogueTests.EveryRowInTheCatalogueWasTriggeredBySomethingAbove`
counted 40 rows and found 41: `InstallIsBroken` arrived without moving it.
`AppBinaryTests.TheOneExecutableIsAWindowsSubsystemBinary` found a `BrowserAI.exe`
under `src\BrowserAI.App\bin\Debug`, which this worktree had built before
`f68ae4cf` made that project a library, and which no build removes.

**Two reds in the Git Bash half only.**
`BuiltInToolListTests.ThePublishedServerAnswersTheToolListWithNothingFromThePayloadRunning`
found pid 69008, the payload's `node.exe`, in the published server's job after
`tools/list`; the PowerShell half passed it. The arm read image paths only, so what
that process ran is not in the log. And
`SessionHostCoordinatorTests.WithNothingStagedTheCoordinatorStaysWhileItsSessionHostRunsAndStopsOnceItExits`
read the census `Alone`, with the sentence the row on that arm records.

## Cited by

| Record | What it takes from here |
|---|---|
| [`HAZARDS.md`](../../../HAZARDS.md#hazard-index) | The closed row on the published arm and the registry reap, and a sighting in the row on the coordinator's census, which the relay closed by deletion |

## What was cut

The user-profile path, replaced by `%USERPROFILE%`, in the files that held it,
which are stored under `.trimmed.` names with the originals' digests in
`originals.sha256`. `cut_gate_079e3d1.py` is the script that cut them. Nothing
else was changed.

## Privacy

Nothing here names the user, the user profile or the machine.
