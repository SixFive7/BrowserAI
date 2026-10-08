<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-08 -- what the visible-input check costs

Establishes
[What the visible-input check costs, and the timer it runs on](../../../kb/windows/processes.md#what-the-visible-input-check-costs-and-the-timer-it-runs-on----measured-2026-10-08).
Evidence:
[`docs/evidence/2026-10-08-input-check/`](../../evidence/2026-10-08-input-check/README.md).

**Why it exists.** F4 a, decided 2026-10-08 by the maintainer: a person's keyboard
or mouse input in a visible session's window counts as activity for that session,
on his condition, verbatim, *"Make sure the keyboard and mouse input check does not
lag the system."* The build was to measure what the check costs and record it,
and to drop the feature if it showed any measurable lag.

## What is here

| File | What it does |
|---|---|
| `InputCheck/InputCheck.csproj` | The rig, published NativeAOT for win-x64 as the product is, so the check runs on the thread pool the product runs on. It compiles in four product files as they stand in `src/`: `SessionTimes.cs`, `VisibleInputWatch.cs`, `InputActivity.cs` and `CoalescableTimer.cs`, so what is measured is the shipped check and the shipped timer |
| `InputCheck/Program.cs` | `measure --out DIR --seconds N`: each read on its own and the product's whole check, ten million times each in a tight loop, with the thread's cycles and its kernel and user time either side; a million hot checks in batches of a hundred and one at a time; then four timed processes started together, which it waits for and summarises. `arm` is one of those four: `timer` is the product's watch with one session at the shipped interval and tolerance, `exact` the same with no tolerance, `wake` the same timer with a tick that does nothing, and `control` no timer at all |
| `InputCheck/Native.cs` | The rig's own reads: `QueryThreadCycleTime`, `QueryProcessCycleTime`, `GetThreadTimes`, `GetProcessTimes` and the system timer's resolution |
| `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props` | Empty but for one property, so the repository's own build settings and package versions do not reach the rig. `BrowserAI.slnx` does not name the project, so the repository's build does not reach it either |

## What keeps it off the rest of the machine

- **No input and no focus.** Nothing here sends input, moves the mouse, sets the
  window in front or shows a window. The reads only read, which is why input
  latency itself is not measured; the kb entry says what is.
- **It selects no process by image name.** The four timed processes are its own
  children, started by path with `CreateNoWindow` and waited on, and their cycles
  and times are read through the handles that started them. None of the eight
  spellings
  [`ProcessSelection`](../../../tests/BrowserAI.Tests/Harness/ProcessSelection.cs)
  keys on is in the rig, by a search that found one in
  `2026-09-14-firstrun/observe.ps1`, the positive control.
- The sessions it registers carry odd pids, and a session told anyway would only
  count it.

## Running it

From this directory, with the .NET 10 SDK and the MSVC linker NativeAOT needs:

```
dotnet publish InputCheck -c Release -r win-x64
InputCheck\bin\Release\net10.0-windows\win-x64\publish\InputCheck.exe measure --out <a directory> --seconds 720
```

It was started detached with `Start-Process -WindowStyle Hidden`, so no console
window opened. `--seconds 0` runs the loops and skips the timed processes, and
`--calls N` sets how many calls each tight loop makes, ten million by default.

## How it was run, and what it found

Two runs on 2026-10-08 are the evidence, both at the product's tree of that day with
the shipped interval of 2 s and tolerance of 1 s:

- **15:21Z to 15:33Z**, `measure --seconds 720`: the loops at ten million calls and the
  four timed processes. The product's check cost **67.9 ns** hot with one session and
  78.7 ns with a hundred; each read 5.4 to 23.5 ns. Over twelve minutes the product's
  watch cost its process **36,208,300 cycles over the process with no timer**, 102,864
  a tick for 352 ticks, of which the timer that woke for nothing cost 86,386 a tick;
  `GetProcessTimes` read one 15.6 ms step for each timer process. With the shipped
  tolerance the ticks came 2,044.5 ms apart at the median, against 2,000.0 ms with
  none. No read came back unknown.
- **15:38Z**, `measure --seconds 0 --calls 100000000`: the same loops a hundred million
  times each, so the thread's kernel time is sampled often enough to read: none for
  `GetForegroundWindow`, `GetLastInputInfo` and `GetTickCount`, and 1.0 to 3.5 % of
  the loops with `GetWindowThreadProcessId` in them.

The kb entry has the whole of both. `--calls` was added between the two runs; with no
option the rig is the one that took the first. Two shorter trial runs while the rig was
being written are not used.
