<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-11 -- whether a relay's standard handles can be reopened overlapped

Establishes
[Both clients hand a server synchronous pipe ends, and no call makes them overlapped](../../../kb/windows/processes.md#both-clients-hand-a-server-synchronous-pipe-ends-and-no-call-makes-them-overlapped----measured-2026-10-11).
Evidence:
[`docs/evidence/2026-10-11-reopen/`](../../evidence/2026-10-11-reopen/README.md).

**Why it exists.** The maintainer's 76 b, 2026-10-11, kept four of the five waits the
conversion to asynchronous code left and asked for the fifth, the relay's standard input
and output, to be measured before he decides: whether `ReOpenFile` with
`FILE_FLAG_OVERLAPPED` can reopen the handle kinds a relay is actually handed, and what
those kinds are under Claude Code and Codex.

## What is here

| File | What it does |
|---|---|
| `reopen-probe/Program.cs` | `standins <directory>` makes each handle kind the way its source makes it, in the rig's own process: Claude Code's from libuv's `uv__create_pipe_pair` with the flags Bun passes for descriptors 0 to 2, the same opened overlapped as a control, Codex's from Rust's `child_pipe`, both ends of `CreatePipe`, three disk files, `CONIN$`, `CONOUT$` and `NUL`. On each it tries `ReOpenFile` three ways, a second open by name where the pipe has one, `CreateIoCompletionPort` and clearing the mode in place, and round-trips data through whatever handle it gets. On each synchronous read end it also makes six calls while a read is parked on another thread, hands `ReadFile` an `OVERLAPPED`, and calls `CancelSynchronousIo` on a parked reader. `relays <directory> <exe>` reads every running process whose image is exactly `<exe>`: its parent, and its three standard handles, duplicated out of it |
| `reopen-probe/Native.cs` | The declarations it calls: kernel32, and ntdll's `NtCreateNamedPipeFile`, `NtOpenFile`, `NtQueryInformationFile`, `NtSetInformationFile`, `NtQueryObject` and `NtQueryInformationProcess` |
| `reopen-probe/reopen-probe.csproj` | A console program with no package and no product file in it |
| `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props` | Empty, so the repository's own build settings and package versions do not reach the rig. `BrowserAI.slnx` does not name the project, so the repository's build does not reach it either |

## What keeps it off the rest of the machine

- **No window, no input, and no process started.** It is one console program, run from a
  shell with no window, and every handle of the `standins` mode is its own and is closed
  before it exits.
- **A running relay is only read.** Each standard handle is duplicated into the rig with
  `DUPLICATE_SAME_ACCESS` and never `DUPLICATE_CLOSE_SOURCE`, queried and closed; the
  relay keeps its own handle throughout, so closing the copy ends nothing. No reopen is
  made on a relay's handle. Its stdin gets only `GetFileType`, `FileAccessInformation` and
  `NtQueryObject`, the three calls the `standins` mode measured to answer at once while a
  synchronous read is parked, because the relay's reading thread is parked there for its
  whole life and any other call would wait behind it. stdout and stderr are queried only
  when `CompareObjectHandles` says neither is stdin's object.
- **It selects processes by full image path and never by name.** `relays` enumerates
  every process with `Process.GetProcesses()`, one of the eight spellings
  [`ProcessSelection`](../../../tests/BrowserAI.Tests/Harness/ProcessSelection.cs) keys on,
  and keeps those whose full path, read with `QueryFullProcessImageNameW`, equals the
  installed `BrowserAI.exe`, a path BrowserAI owns. It stops nothing.

## Running it

From `reopen-probe`, with the .NET 10 SDK:

```
dotnet build -c Release
dotnet bin\Release\net10.0\reopen-probe.dll standins <a directory>
dotnet bin\Release\net10.0\reopen-probe.dll relays <a directory> %LOCALAPPDATA%\BrowserAI.app\current\BrowserAI.exe
```

Each mode writes one JSON file into the directory and prints its path.

## How it was run, and how the stored copy differs

Three runs on 2026-10-11, from Git Bash with no window, each under `timeout`:

- **23:21:00Z**, `standins`, with a copy that had no overlapped control's pending-read
  calls and no lock-free reading of a relay's stdin.
- **23:22:53Z**, `standins` again with those added. Every outcome but the control's new
  calls matches the first run's, compared apart from timings and pids.
- **23:23:30Z**, `relays` against `%LOCALAPPDATA%\BrowserAI.app\current\BrowserAI.exe`,
  BrowserAI 1.1.1-alpha.0.346, with `CompareObjectHandles` added before it. Eleven
  processes ran from that path: seven relays of Claude Code 2.1.296, three of Codex and
  the background server the Task Scheduler started, which has no standard handles.

The stored copy is the one that took the third run, byte for byte, except for the two
licence lines at the head of each file and the comment in the project file and in each
`Directory.*` file, which ran as a bare `<Project></Project>`. The copy that took the
second run differs from it only by `CompareObjectHandles`, in the `relays` mode. A copy of
the stored rig was built in a scratch directory, and builds.
