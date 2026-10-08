<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

I ran all six measurements and cleaned up after myself: no temporary task or process of mine is left. The Write tool refused to create FINDINGS.md, so the full text is below for you to save as `C:\Source\SixFive7\BrowserAI\.work\step0-tasks\FINDINGS.md`. The raw data it cites is already on disk under `C:\Source\SixFive7\BrowserAI\.work\step0-tasks\runs\20261008T1411\`.

The decision to review first: End and Stop could only be tested on a job the Task Scheduler shares with 7 processes I did not start (details under "The shared job"). I ran them only after safeguards and a single first test, and all 7 were unchanged after every request.

---

# Step 0: Task Scheduler measurements for BrowserAI's next build

Measured 2026-10-08, 14:10Z to 14:40Z.
- **Machine:** Windows 11 Pro 26H2 build 26300.9550, AMD Ryzen 9 5950X (16 cores, 32 threads).
- **Toolchain:** .NET SDK 10.0.401, runtime 10.0.12, repository at `890f499b`.
- **What ran:** only stand-ins built for this run, `src/StandIn` (NativeAOT, WinExe) and the driver `src/Rig`. No BrowserAI binary ran.
- **Data:** `runs/20261008T1411/`, one directory per case. All times are UTC.

## Answers

### 1. IgnoreNew: 20 run requests at the same moment

| Case | Rounds | Instances started per round | What each of the 20 requesters got back |
|---|---|---|---|
| ITaskService, nothing running | 10 | **1, in 10 of 10** | `S_OK` from every `IRegisteredTask::Run`, each with its own `IRunningTask` and InstanceGuid (20 distinct per round). The started one read `State` 4, with `EnginePID` equal to the stand-in's pid. The other 19 failed every property read and `Refresh` with `0x8004130B` (SCHED_E_TASK_NOT_RUNNING). |
| ITaskService, one running | 10 | **0, in 10 of 10** | `S_OK` from all 20, each `IRunningTask` failing with `0x8004130B` |
| `schtasks /run`, nothing running | 10 | **1, in 10 of 10** | Exit 0 from all 20, stderr empty, stdout `SUCCESS: Attempted to run the scheduled task "<name>".` In 15 to 19 of the 20 that line was preceded by `INFO: scheduled task "<name>" is currently running.` |
| `schtasks /run`, one running | 10 | **0, in 10 of 10** | Exit 0 from all 20, each printing both lines |

- **Timing of the burst.**
  - COM: 20 separate processes, each already connected, released by one named event. They entered `Run` within 0.1 to 2.7 ms of each other (0.1 ms in 15 of the 20 rounds). Each call took 3.5 to 55.7 ms.
  - schtasks: 20 `schtasks.exe` processes were created suspended and resumed within 0.1 to 0.3 ms of each other. Each exited 28.2 to 141.4 ms after its resume.
  - The winning COM request varied: requester 20, 20, 6, 19, 13, 19, 5, 9, 20, 19.
- **A requester cannot tell from its result whether it was the one that started the instance.** The HRESULT and the exit code are identical for the winner and for the 19 that were ignored.
  - Through COM, the returned `IRunningTask` does tell: `State` reads 4 for the started instance and fails with `0x8004130B` for an ignored one.
  - Through schtasks nothing tells. Output without the INFO line came 1 to 5 times per round while exactly one instance started.

### 2. Missing and disabled tasks (5 runs each)

| Request | Result | Anything started |
|---|---|---|
| `ITaskFolder::GetTask` on a name never registered | `0x80070002` "The system cannot find the file specified.", 0.2 to 5.3 ms | no |
| `schtasks /run` on that name | exit 1, stderr `ERROR: The system cannot find the file specified.` | no |
| `Run` on an `IRegisteredTask` obtained before the task was deleted | `0x80070002` | no |
| `Run` on a disabled task | `0x80041326` "The task is disabled." | no |
| `RunEx` with `TASK_RUN_IGNORE_CONSTRAINTS` on it | `0x80041326` | no |
| `schtasks /run` on it | exit 1, stderr `ERROR: The scheduled task "<name>" could not run because it is disabled.` | no |
| `schtasks /run /i` on it | the same | no |

The disabled task read `State` 1 and `Enabled` false. Its `LastTaskResult` stayed `0x00041303` (has not run). "Anything started" was read from the stand-in logs 3 s after each request.

### 3. End and Stop on a running instance (31 requests)

The task used BrowserAI's settings (Parallel). The stand-in had a message-only window, plus a hidden top-level window in the "top" variants. It started one ordinary child with the same windows.

| Request | Stand-in's windows | Runs | Request returned | Stand-in ended, after the request | Exit code | Received first | Its child |
|---|---|---|---|---|---|---|---|
| `schtasks /end` | message-only | 5 | exit 0, 41.2 to 73.2 ms | 1,039.2 to 1,071.0 ms (median 1,043.6) | `0x0000042B` | nothing | alive 5/5 |
| `IRunningTask::Stop` | message-only | 6 | S_OK, 22.3 to 29.2 ms | 1,028.4 to 1,051.9 ms (median 1,032.3) | `0x0000042B` | nothing | alive 6/6 |
| `schtasks /end` | + hidden top-level, exits on WM_CLOSE | 5 | exit 0, 40.1 to 76.5 ms | 57.1 to 96.5 ms (median 68.8) | 16, its own | WM_COMMAND, WM_COMMAND, WM_CLOSE, twice | alive 5/5 |
| `IRunningTask::Stop` | the same | 5 | S_OK, 20.0 to 24.1 ms | 44.7 to 51.8 ms (median 49.2) | 16 | the same | alive 5/5 |
| `schtasks /end` | + hidden top-level, ignores WM_CLOSE | 5 | exit 0, 38.8 to 61.7 ms | 1,045.2 to 1,051.2 ms (median 1,046.8) | `0x0000042B` | the same | alive 5/5 |
| `IRunningTask::Stop` | the same | 5 | S_OK, 18.4 to 34.1 ms | 1,024.2 to 1,036.6 ms (median 1,029.7) | `0x0000042B` | the same | alive 5/5 |

- **What End and Stop do:** a close request to the task process's top-level windows, then TerminateProcess about 1 s after the request.
  - **No top-level window:** the process receives nothing at all, no window message, thread message, console event or ProcessExit. It is killed 1.02 to 1.07 s after the request with exit code `0x42B` (1067, ERROR_PROCESS_ABORTED).
  - **With a top-level window:** within 45 to 96 ms that window receives `WM_COMMAND` (wParam 0, lParam 7), then `WM_COMMAND` (wParam 0, lParam 2), then `WM_CLOSE`, and the same three again about 1 ms later.
  - The message-only window received nothing in all 31 requests.
  - A process that exits on WM_CLOSE keeps its own exit code. One that ignores it is killed on the same roughly 1 s deadline.
- **The stand-in's child was ended in 0 of 31.** It received nothing, even with a hidden top-level window of its own, and lived until my quit signal at least 5 s later. End and Stop end the task's process, not its process tree and not its job.
- **After every request:** the task read Ready (3), 0 instances, `LastTaskResult` `0x00041306` (SCHED_S_TASK_TERMINATED). `schtasks /end` printed `SUCCESS: The scheduled task "<name>" has been terminated successfully.`
- **For BrowserAI:** a background with no top-level window is killed with no warning. One with a hidden top-level window gets WM_CLOSE and has under a second to close its sessions.

### 4. Start time, from the run request to the stand-in's first log line

| Path | Runs | First line after the request | Medians |
|---|---|---|---|
| `Run` with a BSTR, one at a time | 20 | **18.4 to 49.1 ms, median 19.6** | `Run` returned 1.1 ms; process created 3.7 ms; NativeAOT start to `Main` 15.1 ms; `Main` to the line 0.7 ms |
| `schtasks /run`, timed from launching schtasks.exe | 10 | **43.5 to 85.1 ms, median 67.6** | schtasks.exe exited 27.9 ms; process created 53.4 ms; start to `Main` 12.4 ms |
| `Run`, winner of the 20-way race (measurement 1) | 10 | 24.3 to 50.3 ms after the release, median 30.0 | |
| schtasks, winner of the 20-way race | 10 | 78.0 to 124.5 ms, median 105.4 | |

- **Ranges for the COM path:** creation 3.2 to 32.8 ms after the request (one outlier, rep 13); creation to `Main` 13.8 to 17.4 ms.
- **Against the 514 to 674 ms of 2026-10-04:** the scheduler's own share is about 4 ms to process creation. Almost all of BrowserAI's figure is the program starting after it was created.
- `EnginePID` equalled the stand-in's own pid in 20 of 20 runs.

### 5. Reading the window in front and the last input time

One tick is `GetForegroundWindow` + `GetWindowThreadProcessId` + `GetLastInputInfo`, with no hooks of any kind. `QueryPerformanceCounter` resolves only 100 ns here and a tick is shorter, so ticks were timed with an `lfence; rdtsc` stub calibrated against it (3.40 GHz). This CPU's TSC advances in 10 ns steps.

| Run | Ticks | Median | p99 | Mean |
|---|---|---|---|---|
| tight loop, started directly | 5 × 100,000 | **50 ns** (5/5) | **90 to 100 ns** | 49.4 to 53.9 ns |
| tight loop, started by a task | 5 × 100,000 | 50 ns (5/5) | 90 ns (5/5) | 48.3 to 51.3 ns |
| one tick every 2 s for 300 s | 5 × 150 | **2.22 to 2.43 µs** | 12.4 to 360 µs | 4.2 to 10.1 µs |

- **Clock overhead:** the tick figures include two clock reads, median 20 ns.
- **Per call, tight loop:** `GetForegroundWindow` median 30 ns (p99 80 to 90 ns); `GetWindowThreadProcessId` 30 ns; `GetLastInputInfo` 20 to 30 ns.
- **Cold ticks:** the 750 ticks at the 2 s cadence, pooled, had a median of 2.34 µs, p99 65.2 µs and max 734 µs.
- **CPU over the 5 minutes, read from each process's own counters:**
  - `GetProcessTimes` read 0 kernel and 0 user time for all 5 ticking processes and for 2 no-op controls, so the cost is below its 15.625 ms step.
  - `QueryProcessCycleTime` gave **3.13 to 3.45 ms** for the ticking processes against **2.38 and 2.72 ms** for controls that woke every 2 s and called nothing.
  - So the three calls cost about 0.7 ms per 5 minutes, and waking every 2 s costs about 2.5 ms on its own.
- No tick read a null foreground window.

### 6. A child left behind when the task-started process exits

This was the coordinator's added question. The stand-in (BrowserAI's settings) started a child, exited 500 ms later with code 0 (545 to 849 ms after the run request), and the task was then sampled once a second for 150 s.

| How the child was started | Runs | Ended by the scheduler | Alive at the end of 150 s |
|---|---|---|---|
| ordinary: CREATE_NO_WINDOW, as Process.Start does | 5 | never | 5/5 |
| detached: DETACHED_PROCESS + CREATE_NEW_PROCESS_GROUP | 5 | never | 5/5 |
| detached + CREATE_BREAKAWAY_FROM_JOB | 5 | never | 5/5 |

- **Breakaway was refused in 5 of 5:** CreateProcess failed with error 5, because the scheduler's job allows no breakaway (limit flags 0x0). The stand-in then started the child without the flag, so all 15 children ran inside the scheduler's job.
- **The scheduler ended none of the 15, immediately or later.** Each was alive at all 2,217 samples, from 0.0 to 1.4 s after the stand-in exited through 149.5 to 149.9 s. Each exited only on my quit signal, with code 0.
- **The task did not show as running while the child lived.** From the first sample on, it read Ready (3), 0 instances, `LastTaskResult` 0 (the stand-in's own exit code). The instance ends when the action's process exits; the scheduler does not follow that process's children.
- **For Update.exe:** a child the task-started background starts and then exits for keeps running, untouched, for at least 150 s. Meanwhile the task reads Ready. Not measured: a run request made while such a child lives. I infer it would start a new instance, since none is counted.

## What surprised me

1. The scheduler puts every process it starts in this session, from every task, into **one job**.
   - Its counters: TotalProcesses 2,500 at 14:10Z and 2,730 at 14:39Z, limit flags 0x0.
   - Besides mine it holds 7 processes I did not start. 12088 and 19764 were started by the scheduler at the 2026-10-03 12:07Z sign-in; 31376, 31432, 30948 and 25992 are 19764's children; 23488 is `wave3-bridge.exe`.
   - These are the "six others" the 2026-10-03 survival probe could not name, plus wave3-bridge.
   - This is why breakaway is refused, and why End could not be tested risk-free.
2. **IgnoreNew answers every ignored request with S_OK and a fresh InstanceGuid.** Only the returned object shows it was ignored.
3. **End gives a process about 1 s, and the warning goes only to top-level windows.** It is two WM_COMMAND messages and a WM_CLOSE, sent twice. A process without a top-level window gets no notice at all. The sender was not identified: none of the scheduler service binaries imports PostMessageW, while `taskhostw.exe` does.
4. **Neither End nor the main process exiting touches the main process's child**, and the instance ends with the main process.
5. **`TASK_RUN_IGNORE_CONSTRAINTS` does not override Disabled.**
6. **A tick is about 50 ns hot but 2.3 µs at a 2 s cadence.**
7. **The S4U logon type is refused for a non-elevated token** (0x80070005).

## The shared job, and why End and Stop were run at all

If End or Stop had terminated the job, 7 processes I did not start would have died. Before any request:

- **(a) A private job in session 0.** I tried an S4U task, since a job cannot span sessions. Registration was refused 4 of 4 times with 0x80070005, so nothing was registered.
- **(b) The scheduler's own binaries, read-only.** I read the import tables of `schedsvc.dll`, `ubpm.dll`, `taskcomp.dll` and `wptaskscheduler.dll`.
  - `ubpm.dll` creates the job (CreateJobObjectW, AssignProcessToJobObject) and the processes (CreateProcessAsUserW).
  - None of the four imports or even contains TerminateJobObject or NtTerminateJobObject. Their only termination call is TerminateProcess, and `ubpm.dll` cannot call OpenProcess.
- **(c) A gate on every request.**
  - The stand-in's own job, read just before the request, had to hold only the stand-in, its child and exactly those 7 recorded pids.
  - After the request all 7 had to be present with an unchanged start time, or everything aborted.
  - The first request was a single IRunningTask::Stop, followed by an independent CIM check.

Result: 31 of 31 requests ended only the stand-in, and the 7 were unchanged after each one and again at the end.

## Method

- **The stand-in** (`src/StandIn/Program.cs`, GUI subsystem verified) writes `<role>-<pid>.log`, flushed after every line.
  - It logs, in order: START (time `Main` was entered, process creation time), INFO (ppid, session 1, desktop WinSta0\Default), its own job, and window creation.
  - It runs a message loop that logs every window message and thread message, plus a console control handler and ProcessExit.
  - The hidden top-level window is WS_POPUP with WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE, never shown; `IsWindowVisible` was false in every log.
  - It starts its child through CreateProcessW and exits on a named quit event or a maximum lifetime.
- **The driver** (`src/Rig`) talks to the scheduler through `Schedule.Service` IDispatch. BrowserAI calls the same methods through GeneratedComInterface vtables. The HRESULTs come from the scheduler either way.
- **Tasks:** `BrowserAI-measure-20261008T1411-<case>`, registered with TASK_CREATE_OR_UPDATE and InteractiveToken.
  - The definition copies SignInTask.DefinitionFor: user by SID, LeastPrivilege, the two battery settings false, start on demand, no StartWhenAvailable, PT0S time limit, Priority 5, Parallel.
  - Per case it changed IgnoreNew (m1), Enabled false (m2) or S4U (the refused attempt), and it had no trigger.
- **Clocks:** both sides used GetSystemTimePreciseAsFileTime. Exit times came from GetProcessTimes on handles opened by recorded pid with the creation time verified.
- **m1 per round:** the task was re-registered with the round's log directory, then the burst, an 8 s settle, a count of START lines, the quit signal, and a check that everything exited with no late start.

## Files

Under `runs/20261008T1411/`:
- `pilot/`
- `m1-{com,schtasks}-{none,running}/summary.tsv`, plus per round `requests.tsv` or `schtasks.tsv` and the logs
- `m2/results.tsv`
- `m3-*/results.tsv` and logs (`m3-s4u-*` are the refused attempts)
- `m4-{com,schtasks}/results.tsv`
- `m5/` (benches with raw ns values, tickers, no-op controls) and `m5-task/`
- `m6-*/results.tsv` and `rNN/samples.tsv`
- `registry.tsv`, the foreign-member snapshots, `final-proof.txt`

Under `src/`: the two projects, `analyze.py`, `peimports.py` and `final-proof.ps1`.

## Cleanup proof (`final-proof.txt`, 14:40Z)

- **Positive control:** a disabled task that never ran was found by all three listings (COM GetTasks with hidden tasks, Get-ScheduledTask across all folders, `schtasks /query`): 1, 1, 1.
- **After the cleanup** the same three listings show 0, 0, 0.
- **Processes:** 1,017 were recorded by this run (requesters, schtasks, tickers, stand-ins, children). 0 are still alive with their recorded creation time.
- **Nothing running from `.work\step0-tasks`:** 0, with a positive control that found a 4 s stand-in (1, then 0).
- **Background work:** every background chain finished and all three monitors ended.

## Decisions taken for review

1. **Ran End and Stop on the shared job** after (a), (b) and (c) above. Rejected: skipping measurement 3; running it ungated; elevating to use S4U.
2. **No trigger in the task definitions.** An on-demand run does not read the trigger, and a leftover task can then start nothing by itself.
3. **Added a hidden top-level window** next to the brief's message-only window. Without it the close request would have been invisible.
4. **TSC timing for measurement 5**, because QPC resolves only 100 ns here and a tick takes about 50 ns.
5. **10 rounds per case in measurement 1**, more than the 5 asked for. **Added `RunEx` with `TASK_RUN_IGNORE_CONSTRAINTS` to measurement 2.**
6. **Breakaway falls back to no breakaway when refused**, so the coordinator's third variant still has a child to watch.

## Deviations

- FINDINGS.md was not written, because the tool refused; this text is it.
- The dotnet build used `.work`-local isolation stubs for Directory.Build.props, Directory.Build.targets and Directory.Packages.props. It restored from the existing NuGet cache with no download seen, and like any build it may have used %TEMP%.
- While registered, tasks live in `C:\Windows\System32\Tasks`.
- Not covered: sign-out and shutdown (WM_QUERYENDSESSION never arrived), Velopack's real Update.exe, and the exact source of the WM_COMMAND messages.
