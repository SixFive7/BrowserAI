<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

I couldn't write `FINDINGS.md`: the harness blocks sub-agents from writing report files. Its full intended content is below, so it can be saved at `C:\Source\SixFive7\BrowserAI\.work\step0-screen\FINDINGS.md`. Every file named here is under that `step0-screen` folder, and all of it is still there except what the cleanup section says was deleted.

# Step 0 screen measurements, 2026-10-08

**Setup.** Windows 11 Pro 10.0.26300.9550 (26H2). Primary display 3840x2160, dark theme, transparency on; display scale was not re-measured today. Windows PowerShell 5.1 (`powershell.exe` 10.0.26100.8972), PowerShell 7.6.6, .NET SDK 10.0.401 / runtime 10.0.12. Browsers were chromium-1247 ("Google Chrome for Testing") and firefox-1553 ("Nightly"), copied to `browsers\`. The maintainer was using the machine throughout: VS Code, his own Firefox and Sublime Text were each in front at different moments. Times run from 13:58Z to 14:19Z.

## Part 1: update toast with a live countdown

**Method** (`part1\part1.ps1`, run with `powershell` under Windows PowerShell's app id, as `show.ps1` does):
- The toast and its 60 updates were built as the brief says: Tag `update`, Group `browserai-step0`, status counting from "Installs in 10:00" down to "Installs in 9:00", sequence numbers 2 to 61.
- After that came the two replacements with the same Tag and Group, then `History.Clear(appId)`.
- The "Dismiss" buttons use `activationType="background"`, so every toast in the sequence stays a valid reminder.
- A monitor thread (`part1\Monitor.cs`) sampled every ~40 ms:
  - pixel changes in the bottom-right 900x700 of the screen;
  - the peak meter of every audio session, ignoring one session (Elgato Wave Link) that was already sounding before the first toast;
  - the foreground window;
  - the history count around every `Show`.
- **Added beyond the brief:** the first run's two replacements behaved differently, so I re-ran just the replacements (`part1\part1b.ps1`). Run R used the brief's order, run S the swapped order, each with a 3 s countdown. This cost about 22 s of extra screen time.

**Answers**
1. **Does it stay on screen past the usual few seconds? Yes.** It slid in 0.2 s after `Show` and stayed 62 s, until the first replacement. The captures at 2.5 s, 30.5 s and 60.5 s all show it.
2. **Does the countdown move in place without re-alerting? Yes.**
   - All 60 `Update` calls returned `Succeeded`, each taking 1 to 5 ms.
   - Each update changed only the progress line: 64 to 275 px per frame, 798 px on the first one.
   - There was no slide-in and no sound. The meter can hear a toast: the first `Show` registered on it (explorer's system-sounds session, peak 0.32, 1.76 to 3.04 s).
   - The captures read "9:58 / 0:02 of 10:00", "9:30 / 0:30", "9:00 / 1:00", and the bar grows.
3. **Do both button labels show in full? Yes.** "Install now" and "Wait for inactivity" are both whole, about 162 px each in a 362 px toast. So are "Changelog" and "Dismiss".
4. **Does each replace in place, and does each re-alert?**
   - **In the Notification Centre: in place every time.** The history held exactly 1 toast before and after all 6 replacements, and runs R and S read back the new title each time.
   - **On screen: never in place.** The old popup disappears within one ~40 ms frame.
   - **4 of 6 replacements re-alerted:** a new popup slid in (~250 ms) with the same sound as the first toast.
   - **2 of 6 never popped:** no popup and no sound; the new toast went only to the Notification Centre.
   - Neither order nor content explains the split: "Installing" was silent once and re-alerted twice, "installed" re-alerted twice and was silent once.

| Run | Replacement | Old popup gone | New popup | Sound |
|---|---|---|---|---|
| 1 | ready -> Installing, 63.58 s | 63.63 s | none | none |
| 1 | Installing -> installed, 68.59 s | (none on screen) | 68.72-68.98 s | 68.66-69.94 s |
| R | ready -> Installing, 5.12 s | 5.18 s | 5.27-5.54 s | from 5.24 s |
| R | Installing -> installed, 8.11 s | 8.16 s | none | none |
| S | ready -> installed, 17.33 s | 17.41 s | 17.49-17.74 s | from 17.47 s |
| S | installed -> Installing, 20.34 s | 20.38 s | 20.46-20.71 s | from 20.45 s |

**Images.** Each is cropped inside the toast's 1 px border (the border is translucent and shows the desktop), with the corners made transparent at a 10 px radius. Every edge pixel is transparent or a body colour (channel values 32 to 41). The toast's own background is acrylic, a heavy blur of what is behind it, and nothing legible survives in it.
- `part1\toast1-t02s.png`, `toast1-t30s.png`, `toast1-t60s.png` (362x271)
- `part1\toast2-installing.png` (362x136): from run R, because in run 1 that replacement never popped.
- `part1\toast3-installed.png` (362x136): from run 1.
- All 11 uncropped captures were deleted.

**Raw results:** `part1\part1-log.txt`, `part1\part1b-log.txt`. The popup is a `Windows.UI.Core.CoreWindow` titled "New notification", owned by ShellExperienceHost.exe (pid 52060).

## Part 2: browser window started by a Task Scheduler process

**Launcher** (`launcher\Program.cs`, `launcher\Native.cs`, a .NET 10 WinExe):
- Before the launch it records the foreground window, its process, and the keyboard focus.
- It creates a kill-on-close job, starts the browser suspended with `CreateProcessW` (`STARTF_USESHOWWINDOW` + `SW_SHOWDEFAULT`, which is what Node's spawn passes), assigns it to the job and resumes it.
- At 1, 3 and 6 s it records the foreground, the focus, every top-level window of the job's processes (visible / minimized), and the z-order of the browser window against the window that was in front before.
- Between samples it logs foreground events and shell-hook activations and flashes, through a hook window that is never shown.
- At 8 s it terminates its own job and waits on every process handle it holds.

**How the runs were started:**
- **Scheduler runs:** a temporary task `BrowserAI-measure-20261008T141445Z-window`, registered through `Schedule.Service` with `TASK_LOGON_INTERACTIVE_TOKEN`. Principal and Settings are copied from `SignInTask.DefinitionFor`. The action is `launcher.exe $(Arg0)`, started with `IRegisteredTask::Run(<run id>)`.
  - Its parent was pid 2984, the svchost hosting the `Schedule` service, in session 1 on `WinSta0\Default`.
  - The task had no trigger (`SignInTask` has a `LogonTrigger`), so a leftover could never fire at sign-in.
- **Control runs:** `Process.Start` from this shell's PowerShell 7.
- **Order:** interleaved, scheduler then control.
- `SPI_GETFOREGROUNDLOCKTIMEOUT` reads 2147483647 ms on this machine.

| # | Browser | Started by | Foreground before | 1 s | 3 s | 6 s | Browser window |
|---|---|---|---|---|---|---|---|
| 1 | Chromium | scheduler | VS Code | VS Code (window not shown yet) | Chromium | Chromium | front + focus from 1.34 s (cold first start) |
| 2 | Chromium | shell | his Firefox | Chromium | Chromium | Chromium | front + focus from 0.35 s |
| 3 | Chromium | scheduler | his Firefox | Chromium | Chromium | Chromium | front + focus from 0.34 s |
| 4 | Chromium | shell | his Firefox | Chromium | Chromium | Chromium | front + focus from 0.31 s |
| 5 | Chromium | scheduler | his Firefox | Chromium | Chromium | Chromium | front + focus from 0.32 s |
| 6 | Chromium | shell | his Firefox | Chromium | Chromium | Chromium | front + focus from 0.37 s |
| 7 | Firefox | scheduler | his Firefox | his Firefox (not shown yet) | Nightly | Nightly | front + focus from 2.80 s; foreground went back to his Firefox at 7.47 s |
| 8 | Firefox | shell | his Firefox | his Firefox | his Firefox | his Firefox | behind: shown 1.64 s, z 358 vs 56, 77 visible windows between |
| 9 | Firefox | scheduler | his Firefox | his Firefox | his Firefox | Sublime (he switched) | behind: shown 1.47 s, z 356 vs 54, 77 between |
| 10 | Firefox | shell | Sublime | Sublime | Sublime | Sublime | behind: shown 1.44 s, z 511 vs 54, 118 between |
| 11 | Firefox | scheduler | his Firefox | his Firefox | his Firefox | his Firefox | behind: shown 1.84 s, z 357 vs 52, 78 between |
| 12 | Firefox | shell | his Firefox | his Firefox (not shown yet) | Nightly | Sublime (he switched at 4.75 s) | front + focus from 2.08 s; still above his Firefox at 6 s |

- No browser window was ever minimized.
- **No taskbar flash was recorded in any run, but that is not established.** The hook did deliver window-created and activation messages, but I ran no positive control for a flash.
- In the four "behind" runs Windows fired foreground events for Firefox's windows, yet the sampled foreground never was Firefox and no activation followed. The table is built from the samples and the z-order, not from those events.

**Conclusion**
- **How the launcher was started made no difference.** Chromium was in front 3 of 3 from the scheduler and 3 of 3 from the shell; Firefox was in front 1 of 3 each way.
- **Chromium's window comes to the front and takes keyboard focus, 6 of 6**, about 0.3 s after launch. Someone typing at that moment types into it.
- **Firefox came to the front 2 of 6 times and opened behind 4 of 6:** visible, not minimized, under 77 to 118 windows, with the person's own window keeping focus.
- **Why Firefox splits was not measured.** One possible explanation, unverified: a new process loses the right to take the foreground once the person touches the keyboard or mouse. Firefox shows its window 1.4 to 2.8 s after launch (Chromium 0.3 s), and the maintainer was switching windows during these runs. Logging `GetLastInputInfo` would settle it.
- **BrowserAI's real launch chain was not measured.** These runs start the browser directly, not through the server, `@playwright/mcp` under Node, and Playwright's own flags.

## Proof everything I started is gone
- **Task:** deleted after the last run.
  - `GetTask` now fails with 0x80070002, `schtasks /Query` exits 1, and `Get-ScheduledTask` lists no task of that name.
  - **Another task, `BrowserAI-measure-20261008T1411-m1-schtasks-none`, is registered and Running. It is not mine, and I left it alone.**
- **Processes:**
  - Every run ended its browser through its own job, and in 11 of 12 runs every process handle then showed the process gone.
  - In run 1 the check ran a moment too early, so I looked up its 16 pids afterwards: all gone. I fixed the launcher to wait before the other 11 runs.
  - Afterwards I looked up all 180 pids the launchers recorded. 179 are gone. Pid 107044 is now a pwsh.exe started at 16:20:36 local, after that run ended, so the number was reused and it is not a browser.
  - Both orchestrator processes are gone.
- **Toasts:** `History.Clear(appId)` ran at the end of runs 1, R and S. The history read 0 after each, and 0 again at 14:24:24Z.
- **Scratch browsers:** the `browsers\` copies (372 files) and all 13 profile folders are deleted. No reparse points were inside them.
- **Registry:** `HKCU\Software\Mozilla` and `HKCU\Software\Chromium` were exported before and after (`reg\`). The scratch Firefox added the following, and I removed all of it:
  - `HKCU\Software\Mozilla\Firefox\Launcher`: `<scratch>|Image`, `|Blocklist`, `|Launcher`, `|Browser`, `|Telemetry`
  - `HKCU\Software\Mozilla\Firefox\DllPrefetchExperiment`: `<scratch>`
  - `HKCU\Software\Mozilla\Firefox\PreXULSkeletonUISettings`: `<scratch>|Progress`
  - The empty key `HKCU\Software\Mozilla\Firefox\Installer\457C709E0CC9E390`, created 14:17:35Z, the second the first Firefox run started.
  - `<scratch>` is `C:\Source\SixFive7\BrowserAI\.work\step0-screen\browsers\firefox-1553\firefox\firefox.exe`.
  - `step0-screen` now appears 0 times in that export (7 before removal).
  - The remaining differences belong to the installed `C:\Program Files\Mozilla Firefox`, which he was using. I left them untouched. The Chromium key did not change.
- **Left in place:** Windows' own records of the scratch exe paths: 4 `MuiCache` values and one audio `PolicyConfig\PropertyStore` key, found by `reg query HKCU /f step0-screen /s`, with a positive control.

## Deviations
- **I did not take the suite lock (`.work\locks\suite`) for Part 2.** The browsers ran from copies outside the app root, as the brief directed. The locks folder is empty now; I can't say whether a suite run held it between 13:58Z and 14:19Z.
- **UI Automation found nothing on this build.** `EnumWindows` doesn't list the toast popup, and `FromPoint` returned the VS Code window underneath. I placed the crops from pixel edges instead and deleted the dumps, which held only other windows.
- **One invalid extra run** (`runs\invalid-direct-chromium-comma-path`). A comma-joined argument gave Chromium a bad profile path; its "Profile error occurred" dialog took the foreground at 0.27 s. It is excluded from the table, and `direct-chromium-1` was re-run correctly.

## Re-running
- **Part 1:** `powershell -NoProfile -ExecutionPolicy Bypass -File part1\part1.ps1` (and `part1b.ps1`); crop with `part1\crop.ps1`.
- **Part 2:**
  1. Copy chromium-1247 and firefox-1553 into `browsers\`.
  2. Build `launcher\launcher.csproj` in Release.
  3. Run `part2.ps1 -Step register`, then `-Step run -Runs sched-chromium-1,direct-chromium-1,...`, then `-Step delete`.
  4. Snapshot and clean the Mozilla registry key as above.
