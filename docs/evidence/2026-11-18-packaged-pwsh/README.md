<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-11-18 -- the Microsoft Store's PowerShell in a kill-on-close job, and what it starts

**What this is.** One run of a rig, taken 2026-11-18 from 03:26:04Z to 03:37:47Z on Windows 11
Pro 10.0.26300, for the maintainer's 188: the four `GateDriverTests` arms that run the
PowerShell gate drivers were red since PowerShell on this machine became the Microsoft Store's
package 7.7.0.0, `Microsoft.PowerShell_7.7.0.0_x64__8wekyb3d8bbwe`, installed 2026-11-13, whose
`pwsh.exe` on the search path is the app execution alias
`%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe`. The rig, `pkgjob2.cs.txt`, built as a .NET
11.0.0 file-based program, started that alias in eleven ways from an unpackaged process at
below-normal priority, each with a kill-on-close job of its own. In each arm PowerShell started
the rig again as a reporter, once through `Process.Start` with `UseShellExecute` off and once
through `Start-Process -NoNewWindow`; the reporter wrote its pid, creation time, package
identity and whether it was in a job, wrote a file in the arm's folder, one under
`%LOCALAPPDATA%\Temp` and one `HKCU` value, and started `cmd.exe`, which started `ping`.
The job's members were read, the job was closed, and every pid was read back by its creation
time for up to 30 s. Then the launcher itself was ended with the job open, for E2 and for E0,
both ways, and every pid read back by a second process. **5 files beside this README, 58,243
bytes as this repository stores them.**

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: processes](../../../kb/windows/processes.md#a-packaged-executable-is-refused-a-job-list-and-what-it-starts-leaves-the-job-unless-the-desktop-app-policy-keeps-it----measured-2026-11-18) | The arms and their outcomes, the side effects read on the descendants, and the creation window |
| [re-verification row 229](../../../kb/re-verification.md) | The fact and how to re-check it |
| [`DECISIONS.md`](../../../DECISIONS.md) | 188 a, the launch the maintainer chose to have verified |
| [`HAZARDS.md`](../../../HAZARDS.md#hazard-index) | The row on the Store's PowerShell under the gate drivers' arms |

## What is here

| Path | What it is |
|---|---|
| `pkgjob2.cs.txt` | The rig: arms E0, E2 and E6 (suspended outside any job, assigned, resumed; no desktop-app policy, `DISABLE_PROCESS_TREE`, and `DISABLE_PROCESS_TREE` with `OVERRIDE`), A0, A2 and A6 (the job list with the same three policies), P0 and P2 (a suspended `cmd.exe` in the job named as the parent), N0 and N2 (an unpackaged helper started with the job list starts PowerShell), and the control H (`cmd.exe` starting `ping` with the job list); the reporter; `die`, which runs one arm and ends its own process with the job open; and `check`, which reads the pids `die` wrote |
| `run4.sh.txt` | The run: every arm, then `die` and `check` for E2 and E0, both ways |
| `below.ps1.txt` | How every process of the run was put at below-normal priority: a Windows PowerShell 5.1 that lowers its own priority class and starts `cmd.exe`, whose children take it |
| `run4.out.txt` | What the run printed |
| `originals.sha256` | What was changed, each with the SHA-256 of what it was |

## What was changed, and what was left out

- **The rig and both scripts are stored with `.txt` appended**, as a record and not as this
  repository's code.
- **The output's line ends are made LF**, as the repository stores text.
- **Left out**: three earlier runs of the rig while it was being written. The first read each
  arm after a fixed 7 s, which the machine needed 8 to 25 s to fill; the second ran without an
  environment block of the rig's own; the third read the reports of every arm and every
  launcher-dies run out of one folder, so a report was read across runs. Their outputs stay in
  `.work/deps-scratch` on the machine that took them, which is not a record.
- **Nothing the reporters wrote is left on the machine**: the rig read each `HKCU` value and
  `%LOCALAPPDATA%\Temp` file back from outside and deleted it, and every survivor of an arm was
  ended by its pid and creation time after it was read.

## How to run it again

Build `pkgjob2.cs` with `dotnet build pkgjob2.cs -o bin` in a folder that inherits nothing of
this repository's build rules: an empty `Directory.Build.props`, `Directory.Build.targets` and
`Directory.Packages.props` and an `.editorconfig` with `root = true` beside it, because the
repository's analyzers run at error severity. Then run `run4.sh` with its two paths changed,
at below-normal priority through `below.ps1`. It needs the Store's PowerShell, or another
packaged app's alias in its place. Nothing it starts shows a window.

## Privacy

Paths name the machine's user folder and this repository's scratch folder, as other batches
here do. No other identifier of the machine is in these files.
