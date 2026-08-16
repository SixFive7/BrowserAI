<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Windows processes: containment, stdio and interop

Measured facts about how Windows creates, contains and reaps processes, and
about the .NET and interop surface used to do it. Detection of processes that
escaped anyway is in [Detecting stray browsers](detection.md).

## Job objects and process containment

Measured 2026-08-15. Harness: `.work/jobtest/`.

**The headline: containment holds.** 16 runs, **106 spawned processes, 0
escapees, 0 survivors**, across real Chromium and Firefox trees. `[FLOATS]`

**Job membership is inherited automatically.** MS Learn,
[Job Objects](https://learn.microsoft.com/windows/win32/procthread/job-objects#managing-processes-in-jobs):
*"After a process is associated with a job, by default any child processes it
creates using CreateProcess are also associated with the job."* Escaping requires
`CREATE_BREAKAWAY_FROM_JOB` **and** `JOB_OBJECT_LIMIT_BREAKAWAY_OK` on the job,
or `JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK`. This is the inverse of Linux
process-group semantics. `[STABLE]`

**A denied breakaway fails the launch rather than escaping.** Measured:
`CreateProcessW` returns `ERROR_ACCESS_DENIED` (5). libuv's own source gives the
same reason for avoiding the flag (`src/win/process.c:1124`). **This is the fact
the whole guarantee rests on** — a job granting no breakaway flags converts every
escape attempt into a launch failure. `[STABLE]`

> ⚠️ **That sentence is about the immediate job, and it is not the whole
> answer.** With a permissive job nested *inside* ours the same call **succeeds**
> — see the entry below, measured 2026-08-16. Read as universal it produces a
> test that asserts error 5 in the production configuration and fails, which is
> exactly what happened while writing `JobContainmentTests`.

**A breakaway from a nested permissive job succeeds, and lands in our job
anyway.** Measured twice, 2026-08-16, by `JobContainmentTests.
ADescendantTreeIsContainedAndNothingSurvivesTheLauncher`, which makes the same
`CreateProcessW` call from one process in two states: `[STABLE]`

| The process's jobs, innermost first | Result | Where the new process ends up |
|---|---|---|
| ours (`KILL_ON_JOB_CLOSE` only) | `ERROR_ACCESS_DENIED` (5), **no process created** | — |
| a libuv-shaped job (`KILL_ON_JOB_CLOSE \| BREAKAWAY_OK \| SILENT_BREAKAWAY_OK`) nested inside ours | **success, error 0** | **in our job**, confirmed by `IsProcessInJob` and by the job's own pid list |

Both outcomes are correct and neither is an escape. The breakaway is granted by
the inner job, walks up the hierarchy, and stops at the first job that does not
permit it — ours. **The observable that matters is where the process ended up,
never the return value**: a check written as *"error 0 means we leaked"* reports
a defect in the exact configuration production always runs in, because libuv's
job is always in the chain.

**The product implementation is measured, not only the prototype.** 2026-08-16,
`JobContainmentTests`, both arms run twice with identical results — job created
by `src/BrowserAI/Interop/JobObject.cs`, child started by
`JobLauncher.Start`: `[FLOATS]`

| Arm | Processes walked | Job pid-list | Escapees | Job members the walk missed | Survivors after the launcher is `TerminateProcess`d |
|---|---|---|---|---|---|
| Probe tree (child + 3 grandchildren + a breakaway, inside a libuv-shaped job) | **10** | 10 | **0** | **0** | **0** |
| Bundled `node.exe` **v24.19.0** + 2 `child_process.spawn` grandchildren | **4** | 4 | **0** | **0** | **0** |

Read back from the kernel in both arms: `LimitFlags` `0x2000`,
`JobObjectBasicUIRestrictions` `0`, handle **not** inheritable. Re-establish by
running the suite; the launcher writes its whole report to
`<scratch>\report.json`.

> **What the node arm does and does not close.** [The Node gap](../README.md)
> notes that the 2026-08-15 containment measurements ran on **26.7.0** while the
> shipped runtime is **v24.19.0**. Containment through the bundled runtime's own
> `child_process.spawn` tree is now measured on v24.19.0 and holds. What was
> **not** separately confirmed is that libuv still creates its permissive global
> job under that version — the test observes containment, not libuv's internals.
> The probe arm reproduces that job shape explicitly, so the nested-permissive
> case is covered either way; the libuv source claim itself remains as it was.

**Nested jobs cannot launder a process out.** MS Learn,
[Nested Jobs](https://learn.microsoft.com/windows/win32/procthread/nested-jobs):
a breakaway *"moves up the hierarchy until it reaches a job that does not permit
breakaway."* Depth 4 measured (outer → ours → libuv's → Chromium sandbox).
`KILL_ON_JOB_CLOSE` on the outer job reaches child jobs in the hierarchy. Jobs
nest only if **neither sets UI limits** — so never call
`SetInformationJobObject` with `JobObjectBasicUIRestrictions`. `[STABLE]`

**libuv puts a permissive job in our chain.** `src/win/process.c:69-106` creates a
global job with `BREAKAWAY_OK | SILENT_BREAKAWAY_OK | DIE_ON_UNHANDLED_EXCEPTION
| KILL_ON_JOB_CLOSE` and assigns every non-detached child to it. Playwright
spawns the browser with `detached: process.platform !== "win32"` — so **not**
detached on Windows, so the browser lands in libuv's job. Containment held
through it, which is the strongest available confirmation: that is exactly the
configuration that would leak if our job permitted breakaway. Firefox stacks a
second such job via its launcher process. `[FLOATS]`

**Neither browser requests breakaway on a browser path.** Chromium: every caller
of `CREATE_BREAKAWAY_FROM_JOB` is installer, updater or remote-desktop code
(`chrome/installer/*`, `chrome/browser/updater/scheduler_impl.cc`,
`remoting/host/win/wts_session_process_delegate.cc`). No renderer, GPU, utility,
network-service or crashpad path. `crashpad_handler` is an ordinary child
(`crashpad_client_win.cc:437-463`). Firefox's launcher uses `CREATE_SUSPENDED |
CREATE_UNICODE_ENVIRONMENT` only. `[FLOATS]`

**Firefox actively checks and declines.** `nsWindowsRestart.cpp`'s
`NeedToBreakAwayFromJob()` returns false unless the job carries **both**
`KILL_ON_JOB_CLOSE` and `BREAKAWAY_OK`. Ours carries only the first. Consequence:
setting `BREAKAWAY_OK` would not merely permit an escape, it would **cause** one.
`[FLOATS]`

**Two implementation mistakes, both proven fatal by measurement:** `[STABLE]`

| Mistake | Measured |
|---|---|
| `Process.Start` then `AssignProcessToJobObject` | **2 escapees** — the child spawns grandchildren before the assign lands |
| Inheritable job handle (`bInheritHandle=TRUE`) | **All children survived** — ours is no longer the last handle, so `KILL_ON_JOB_CLOSE` never fires |

The second is one flag away at all times: redirecting stdio forces
`bInheritHandles=TRUE`.

**`PROC_THREAD_ATTRIBUTE_JOB_LIST` beats `CREATE_SUSPENDED`.** Both measured at 0
escapees, but the attribute makes membership part of process creation, so the
race window does not exist rather than being closed afterwards — and it cannot
leak a suspended process if we die mid-sequence. `.NET` can express neither;
`ProcessStartInfo` has no creation-flags surface. A P/Invoke is mandatory.
Measured with real sandboxed Chromium: 9 processes, 0 escapees. `[STABLE]`

**BrowserAI inside someone else's job works**, measured in all three ancestor
configurations — `KILL_ON_JOB_CLOSE` only, `+ BREAKAWAY_OK`, and
`+ SILENT_BREAKAWAY_OK`. The third is the realistic case: any MCP client that
spawns BrowserAI through Node `child_process` puts us in libuv's job. `[STABLE]`

**Firefox background tasks and the crash reporter fail inside our job** with
`ERROR_ACCESS_DENIED`, because `BackgroundTasksRunner` and
`nsExceptionHandler.cpp` request breakaway. This is the correct trade — a failed
helper launch beats an escaped `firefox.exe --backgroundtask`. Not a bug to fix.
`[FLOATS]`

**Playwright's own force-kill is `taskkill /pid <pid> /T /F`** — by PID with the
tree flag, never by image name. Upstream is clean on that axis.
(`coreBundle.js:9046`) `[FLOATS]`

**A supervisor can respawn its child while you are killing it, and
kill-by-enumeration cannot win that race.** Shipped mitigation, read 2026-08-16:
`C:\Source\ExoFabric\Updater\NetLoader2\Application.xaml.vb:98` wraps its entire
enumerate-and-kill sweep in `For i = 1 To 2`, commented *"Need two runs because
any subprocess (like WyUpdate) might be started again if the main assembly is not
already killed."* Two passes is not a fix, it is a wider window: enumeration reads
a list the supervisor is still mutating, so correctness rests on the supervisor
happening not to respawn during pass two. **This is the strongest available
argument for a job object over enumeration, and it is a different argument from
the one this section already makes** — the escapee counts above say enumeration
*misses* processes, which sounds like something a better sweep could fix. This
says enumeration **cannot be made complete at any repetition count**, because the
process set is adversarial rather than merely large. `KILL_ON_JOB_CLOSE` has no
such race: the kernel tears the whole job down at once, and anything respawned
inside it is already contained. Re-establish by reading that file. `[STABLE]` for
the race; `[MACHINE]` for the observation.

**Containment holds from a published NativeAOT binary, against a real browser.**
Measured 2026-08-16 at
[build-order step 7](../../plan/build-order.md#7-vertical-slice-a-published-aot-binary-proxies-a-real-child),
which is the run that closes the caveat step 6 left open — `[LibraryImport]` and
`PROC_THREAD_ATTRIBUTE_JOB_LIST` had until then only been exercised under the
test host, never after ILC. The published `BrowserAI.exe` was started inside a
job the suite owns, brought up `node.exe` v24.19.0 and Chromium 152.0.7977.8 (7
Chromium processes: browser, two crashpad handlers, a GPU process, two utility
processes and renderers), and was then **`TerminateProcess`d from outside**, so
no `finally`, no shutdown hook and no handler of ours ran. **Every recorded pid
was gone**, with pid identity re-checked against its creation time before the
survivor check acted on it. `VerticalSliceTests.KillingThePublishedBinaryLeavesNoNodeAndNoBrowser`
re-runs it. `[FLOATS]`

> **A `conhost.exe` joins the job for each child.** `JobLauncher` passes
> `CREATE_NO_WINDOW`, which still allocates a console, so the job's member list
> carries a `conhost.exe` per launched process. It is contained like everything
> else; it is noted because a member list read for the first time otherwise
> reads as a leak. `[MACHINE]`

**A cleanup path in a `finally` is one this design guarantees will sometimes not
run.** Observed the same day, and it is the containment contract biting its own
author: BrowserAI deleted its per-run instance directory in a `finally`, the
acceptance test terminates BrowserAI from outside on every run, and nineteen
suite runs left nineteen directories behind. The fix is a sweep at startup that
uses the **working-directory lock as the liveness check** — Windows refuses to
delete a directory that is some process's current directory, so the delete
simply fails for a live run and succeeds for an abandoned one, with no pid to
recycle and no name to match. `[STABLE]` for the lock; re-establish with
`InstanceDirectoryTests`.

**A process's command line can be read by pid without a PEB walk.**
`NtQueryInformationProcess` with `ProcessCommandLineInformation` (class **60**,
Windows 8.1+) returns a `UNICODE_STRING` and needs only
`PROCESS_QUERY_LIMITED_INFORMATION` — no `ReadProcessMemory`, no 32/64-bit
pointer arithmetic. The documented two-call shape applies: the sizing call
returns `STATUS_INFO_LENGTH_MISMATCH` (`0xC0000004`) with the required length,
and a fixed buffer guess truncates, because a Chromium browser command line runs
to several kilobytes of switches. Paired with `QueryFullProcessImageNameW` this
is the sanctioned alternative to matching a process by image name: the full path
is compared against a path BrowserAI owns. Used by `ProcessCommandLine` in the
suite; it is what makes *"`--no-sandbox` is absent"* an assertion about the
browser rather than about our config file. `[STABLE]`

## stdio, exit codes and process startup

**`Console` stdio is wrong by default in both directions.** Measured:
`Console.Out` writes **CP437**, not UTF-8 (`é` → `0x82`); `Console.InputEncoding`
also defaults to CP437; **any** `TextWriter` emits CRLF; and a hand-rolled
`new StreamWriter(stream, Encoding.UTF8)` emits a **BOM**. On a JSON-RPC channel
each of the three corrupts the stream on first contact. `[STABLE]`

> **The charter does not date this measurement.** The date is `[UNVERIFIED]`; the
> observations are carried forward as written.

> **Corroborated 2026-08-16 — and the date above stays `[UNVERIFIED]`.** Two
> 2018-era repositories on this machine independently hand-reconstruct CP437 over
> a raw console handle to make output appear at all:
> `C:\Source\ExoFabric\WinUpdater\WinUpdate\WinUpdate.vb:18,71-77` and
> `C:\Source\ExoFabric\Certifier\Certifier\Main.vb:22` — both `Const
> MY_CODE_PAGE As Integer = 437`, `CreateFile("CONOUT$")`, `New
> IO.StreamWriter(FileStream, Encoding.GetEncoding(437))`, `Console.SetOut`, the
> WinUpdate one commented *"VS console redirection fix"*. Two authors reaching
> independently for the same workaround is evidence that **the default really is
> CP437**; it is not evidence about *when* the entry above was measured, so that
> gap is unchanged. **Note what they built:** it is exactly the hand-rolled
> `StreamWriter` the entry above warns about, and it emitted no BOM only because
> the encoding was CP437 rather than UTF-8 — swap the encoding and the identical
> code corrupts a JSON-RPC stream on its first byte. Read from source, not run.
> `[MACHINE]` for the repositories; the underlying default is `[STABLE]`.

**A logging library's type initializer can write to the protocol channel.**
`Serilog.Sinks.Console`'s `ConsoleSink` has a **static constructor** calling
`WindowsConsole.EnableVirtualTerminalProcessing()`, which calls `SetConsoleMode`
on `GetStdHandle(-11)` (`STD_OUTPUT_HANDLE`) — before any log line is written, and
reachable by merely touching the type. When stdout is a pipe, `GetConsoleMode`
fails, the guard `stdout != INVALID_HANDLE_VALUE && GetConsoleMode(...)` goes
false, and it **silently no-ops** — so the behaviour is invisible under MCP and
appears only in interactive diagnostics. Separately, `SelectOutputStream` returns
`Console.Out` whenever `_standardErrorFromLevel` is null: the only safe
configuration for a stdio protocol server is
`standardErrorFromLevel: LogEventLevel.Verbose`, because nothing is `< Verbose`,
so every level routes to `Console.Error`.

> **This is the shape that no "never call `Console.WriteLine`" rule catches.**
> The write is a *third party's type initializer*; it targets the **handle**, not
> the `TextWriter`, so nothing about `Console.Out` ownership constrains it; and it
> fails silently in exactly the configuration we ship, so an interactive smoke
> test is the only place it would ever be seen working. The rule that does catch
> it is broader than the charter's: nothing may touch stdout's handle either, and
> **a dependency's static constructor counts as our code.**

Read from source 2026-08-16, not run. Local checkout **3.1.2**
(`C:\Source\SixFive7\serilog-sinks-console\src\Serilog.Sinks.Console\Sinks\SystemConsole\ConsoleSink.cs:35-38`,
`…\Platform\WindowsConsole.cs`), cross-checked against upstream `main` the same
day. **The two differ, and the newer one is worse:** 3.1.2 wraps the entire
P/Invoke body in `#if PINVOKE`, defined only for `net45` and `netcoreapp1.1`
(csproj lines 29-34), so a modern consumer resolving the `netstandard2.0` asset
gets an empty method — the hazard is real but dormant there. **Upstream `main` has
dropped the guard entirely**: the `GetStdHandle` / `GetConsoleMode` /
`SetConsoleMode` calls are unconditional, and they are `DllImport`, not
`LibraryImport`. Re-establish by reading those two files **at the version actually
referenced**, never at whichever copy is on disk. `[FLOATS]` for Serilog's code;
`[STABLE]` for the mechanism — a type initializer runs before first use, and
`GetConsoleMode` on a pipe fails, on every Windows.

**An async log sink plus `Environment.Exit` drops the final buffered messages**,
so every `Logger.Fatal(...)` → `Exit(1)` path loses precisely the line describing
the crash. `Environment.Exit` does not wait for a sink's own worker thread, and a
buffered target has nothing else to flush it. Shipped pattern, read 2026-08-16 in
`C:\Source\ExoFabric\Updater`: `Updater\Example NLog.config:9` declares
`<targets async="true">`; all four loaders end their unhandled-exception handler
with `Logger.Fatal(...)` then `Environment.Exit(1)`
(`NetLoader2\Application.xaml.vb:12-14`, and the same three lines in `NetLoader1`,
`NetLoader3` and `DomainLoader`); and **`LogManager.Shutdown` and
`LogManager.Flush` appear nowhere in the repository** — grepped, zero hits.
Re-establish with those two greps. `[STABLE]` for the mechanism; `[MACHINE]` for
the observation.

**`UseShellExecute` defaults to `True` on .NET Framework and `False` on .NET
Core**, changed in .NET Core 2.1 and recorded as a
[breaking change](https://learn.microsoft.com/dotnet/core/compatibility/fx-core#core-net-libraries).
`True` routes the launch through the graphical shell, which **silently detaches
the child and makes stream redirection impossible** — `RedirectStandardOutput` and
friends require `UseShellExecute = false`, and `ProcessStartInfo.Environment`
throws `InvalidOperationException` at `Start()` if it is true. The trap is porting
supervision code from an older project, where the *absence* of an assignment meant
the opposite thing. Verified against MS Learn 2026-08-16. `[STABLE]`

**`Win32Exception.ErrorCode` is the HRESULT, not the Win32 code.** `ErrorCode` is
inherited from `ExternalException` and documented as *"the HRESULT of the error"*;
the Win32 number lives on `NativeErrorCode`. In practice `ErrorCode` reads
`0x80004005` (`E_FAIL`, *"unspecified failure"*) for essentially every
`Win32Exception`, so **an exception filter keyed on it matches everything**. The
value that actually means "the user cancelled the UAC prompt" is
`NativeErrorCode == 1223` (`ERROR_CANCELLED`). Shipped bug, read 2026-08-16:
`C:\Source\ExoFabric\Updater\NetLoader2\Application.xaml.vb:234` filters
`Catch ex As ComponentModel.Win32Exception When ex.ErrorCode = &H80004005` around
an elevating `Process.Start`, inside a `For i = 1 To 10` retry — so *every*
elevation failure was read as a refusal and re-prompted, up to ten UAC dialogs for
a cause that was never the user. Verified against MS Learn 2026-08-16. `[STABLE]`

**`Directory.GetFiles` is top-level only, and a recursive enumeration aborts on
the first `UnauthorizedAccessException` rather than skipping the node.** MS Learn,
on the `AllDirectories` overloads: *"`UnauthorizedAccessException` errors may make
the enumeration incomplete. You can catch these exceptions by first enumerating
directories and then enumerating files."* The failure is silent in the worst way —
a partially-walked tree is indistinguishable from a fully-walked smaller one. A
robust recursive delete therefore needs a hand-rolled **post-order** walk with
per-node exception discrimination: deepest child first, so a non-recursive
`Directory.Delete` always sees an empty directory. Reference implementation, read
2026-08-16:
`C:\Source\ExoFabric\Zombieraser\Zombieraser\Program.cs:213-289` (`GetTreeRobust`)
— recurse subdirectories, then yield files, then yield the directory itself, with
`UnauthorizedAccessException` and `DirectoryNotFoundException` caught and logged
**per node**, and an optional ACL-reset retry on the denied node. Directly relevant
to `browserai_reinstall_browser`, session destroy, and the Velopack `current\`
swap. Verified against MS Learn 2026-08-16. `[STABLE]`

**`Console.ReadKey()` inside a `catch` in a non-interactive process hangs
forever, with no output** — there is no console input to read, and nothing times
out. It presents exactly as "the server is stuck". Shipped instance, read
2026-08-16: `C:\Source\ExoFabric\Zombieraser\Zombieraser\Program.cs:247,277`, in a
scheduled non-interactive job. Note the shape — both calls sit in the
*unknown-exception* arm, below the specific `UnauthorizedAccessException` and
`DirectoryNotFoundException` handlers, so they fire only on the cases nobody
anticipated: the population least likely to have been exercised in testing and
most likely to be hit in the field. `[STABLE]`

**A plain file write is not durable when it returns.** The bytes are in the system
cache; MS Learn,
[Flushing System-Buffered I/O Data to Disk](https://learn.microsoft.com/windows/win32/fileio/flushing-system-buffered-i-o-data-to-disk):
*"the system usually buffers the data and writes the data to the disk on a regular
basis."* `Flush()` and `FlushAsync` do **not** close the gap — `FlushAsync`'s own
remarks say it *"flushes the .NET stream buffers to the file, but does not flush
intermediate file buffers in the operating system."* Surviving a power cut needs
`FileStream.Flush(flushToDisk: true)`, which reaches `FlushFileBuffers`, or
`FileOptions.WriteThrough` / `FILE_FLAG_WRITE_THROUGH` set at open time, and then
an atomic `File.Move` into place so no reader ever observes a half-written file.
Verified against MS Learn 2026-08-16. `[STABLE]`

**A working reference implementation exists on this machine, in C#**, verified
2026-08-16 by reading it:
`C:\Source\SixFive7\StationeersPlus\TestRig\src\TestRig.Core\Infrastructure\SystemFileSystem.cs:248-296`
(`WriteAllTextDurable`) does all three steps — a temp file **in the same
directory**, opened `FileShare.None` with `FileOptions.WriteThrough`, then
`stream.Flush(flushToDisk: true)`, then `File.Move(temp, full, overwrite: true)` —
with the reasoning recorded inline at lines 229-247 and a `finally` that removes
the temp on every exit path. Two details worth taking:

- **`File.Move(overwrite: true)`, not `File.Replace`.** `Replace` **requires the
  destination to already exist**, and the first write of a lock file or a crash
  marker is exactly the case where it does not. `Move` maps to `MoveFileEx` with
  `MOVEFILE_REPLACE_EXISTING`, which covers both. `[STABLE]`
- **The temp file must be in the target's own directory** — a rename is only
  atomic within one volume, and only cheap within one directory. The rename is
  also retried (5 attempts, escalating sleep) against `IOException` /
  `UnauthorizedAccessException`, because something holding the destination open is
  a live condition rather than a bug.

> **Provenance note, 2026-08-16 — first recorded as a missing file, and that was
> wrong.** This entry arrived citing `TestRig\rig-lock.ps1:212-245`, which is
> absent from `HEAD`, and it was written up here as a reference that did not
> exist. **It did exist.** `rig-lock.ps1` was added 2026-08-09 in `a5968c5a` at
> +833 lines and **deleted 2026-08-15** in `902082cb`, *"TestRig: the PowerShell
> rig is retired"* — rewritten in C# at the path above, not fabricated and not
> lost. A `HEAD`-only search cannot distinguish "never existed" from "retired
> yesterday", and this entry asserted the first from evidence that only supported
> the second. **Search the history, not just the tree, before recording an absence
> as a finding.** *(A content grep for `Flush(true)` also missed the successor —
> the call is written `Flush(flushToDisk: true)`. A named argument defeats a
> literal grep, so a negative grep result is not an absence either.)*

**`Process.ExitCode` throws after `Dispose()`, and
`Process.GetProcessById(pid).ExitCode` always throws.** .NET is *worse* here than
PowerShell, which merely returns `$null`. Cache the value as an `int` the moment
the child exits. `[STABLE]`

**`WaitForExit(int)` does not drain the async readers** — only `WaitForExit()`
and `WaitForExitAsync(ct)` do, so the timeout overload truncates stderr.
`[STABLE]`

**stderr survives the child.** The anonymous pipe exists before `CreateProcess`
and the kernel buffers it: **5 lines survived a 3 s delay *and* child exit**. The
real risk runs the other way — a full pipe blocks the child. `[STABLE]` for the
mechanism; **the charter does not date the measurement**, so the date is
`[UNVERIFIED]`.

**stdin EOF fires instantly when the parent holding the pipe is
`TerminateProcess`d**, which is what makes EOF a usable backstop for reaping
instances. Measured; **undated in the charter**. `[STABLE]`

**`ProcessStartInfo.Environment` is pre-populated with the inherited block and
assignment *merges*** — an allowlist requires `Clear()` first. **`WorkingDirectory`
left unset passes `null` to `CreateProcess`**, so the child inherits the parent's
cwd, whatever the MCP client happened to have. **`ArgumentList` and `Arguments`
are mutually exclusive**; setting both is undefined behaviour. `[STABLE]`

**Node's `child_process` has no job object support at all**, and Node's `spawn`
cannot execute `.cmd` shims without `shell: true` — a live Claude Code bug for
plugin-shipped servers using bare `npx`
([#58510](https://github.com/anthropics/claude-code/issues/58510)). Every Node
process supervisor on Windows falls back to `taskkill /T /F` or a native addon,
and none survives a hard kill of the supervisor. `[FLOATS]`

## Interop and the toolchain

**`NtQueryInformationProcess` reads a parent PID in ~0.77 µs/call**, against
~3.3 ms for `Process.GetProcessById` and milliseconds for WMI. `dotnet/runtime`
itself uses it. `[MACHINE]` for the numbers, `[STABLE]` for the API.

**`LibraryImport` does not support `StringBuilder`**, so a `CreateProcessW`
command line must be passed as a writable `char[]`/`Span<char>` — the API mutates
the buffer, and a `string` literal is not valid. `DllImport` is the wrong choice
under NativeAOT because it relies on runtime IL-stub generation. `[STABLE]`

**A COM/interop enum value the running OS does not know throws on assignment** —
at the property set, not at load and not at compile time. The managed enum is only
an integer; the rejection happens inside the COM object receiving it, so the
compiler, the interop layer and any static analysis all see a valid value. Shipped
mitigation, read 2026-08-16:
`C:\Source\ExoFabric\WinUpdater\WinUpdater\WinUpdater.vb:71-76` wraps
`UpdateDownloader.Priority = DownloadPriority.dpExtraHigh` — a Windows Update
Agent value newer than the OS floor that project targeted — in a try/catch that
logs *"Switching from ""dpExtraHigh"" priority to ""dpHigh"" priority due to OS
incompatibility"* and downgrades. **Directly applicable here:** the job-object
information classes and the `NtQueryInformationProcess` information classes this
project P/Invokes are the same shape, so every information class used must either
be safe at our Windows floor or carry an explicit downgrade path — a value that is
merely absent on an older build fails at the call site, where nothing else will
catch it. Re-establish by reading that file. `[STABLE]`

**`WaitForSingleObject` needs `SYNCHRONIZE`, which
`PROCESS_QUERY_LIMITED_INFORMATION` does not imply.** Measured 2026-08-16 while
writing the containment harness: a handle opened with query rights alone makes
the wait return `WAIT_FAILED`, and a liveness check written as *"anything other
than `WAIT_OBJECT_0` means still running"* then reports every process it can open
as alive **forever**. It presented as a containment defect in the product —
30 seconds of polling, then "the launcher survived" — and the product was fine.
The shape is the point: a failed call read as one of the two normal answers is
worse than an exception, so `ProcessIdentity.IsAlive` refuses to interpret
`WAIT_FAILED` at all. Note also that `OpenProcess` succeeding proves nothing,
because a handle held by anyone keeps the pid and the object alive after the
process is gone. Re-establish by removing `SYNCHRONIZE` from the access mask.
`[STABLE]`

**A double hyphen in an XML comment in `Directory.Build.props` presents as
`NETSDK1207: Ahead-of-time compilation is not supported for the target
framework`.** Measured twice, 2026-08-16, SDK **10.0.302**: XML forbids `--`
inside a comment, MSBuild then cannot load the file, and the project builds
*without* it — so `TargetFramework` is never set and the AOT check fails on a
framework nobody chose. **The two entry points disagree, and only one is
useful:** `dotnet build` reports NETSDK1207 from
`Microsoft.NET.Sdk.FrameworkReferenceResolution.targets(120,5)`, while
`dotnet msbuild <project> -getProperty:TargetFramework` reports the real cause,
`MSB4024 … An XML comment cannot contain '--'`, with the line and column. Reach
for `-getProperty` whenever a shared props file has just been edited and the
error names something unrelated. `[FLOATS]` for the SDK version; `[STABLE]` for
the XML rule.

**`BannedApiAnalyzers` merges every additional file named `BannedSymbols.txt`.**
Measured 2026-08-16 by planting one call per project: with
`build/BannedSymbols.txt` supplied to all projects from `Directory.Build.props`
and `src/BrowserAI/BannedSymbols.txt` supplied only to the product, the product
project reports **both** files' bans on the same build. That is what lets the
repository-wide rule and the product-only rules live in separate files instead of
being duplicated. Re-establish by planting a banned call and reading the RS0030
message, which quotes the entry's own text. `[FLOATS]`

**No credible NuGet job-object wrapper exists** — the candidates have <6K
downloads and the newest was published in **2017**. `dotnet/runtime`
[#126273](https://github.com/dotnet/runtime/issues/126273) proposed built-in
support and was closed as not planned. The hand-rolled surface is ~60 lines.
`[FLOATS]`

**Floating NuGet is two restore steps, not one.** `dotnet restore
--force-evaluate` resolves the float; a second, locked-mode restore verifies it.
They are mutually exclusive in one invocation: **with a lock file present and no
`--force-evaluate`, NuGet does not re-resolve and the float is silently dead**
([NU1512](https://learn.microsoft.com/nuget/reference/errors-and-warnings/nu1512),
warned by default from the .NET 11 SDK). `git diff --exit-code --
"**/packages.lock.json"` after the resolve is then the cheapest available drift
detector. `[FLOATS]`

**Central package management refuses a floating version by default.** With
`ManagePackageVersionsCentrally` set, a `PackageVersion` of `Version="*"` fails
restore outright: *"NU1011: The following PackageVersion items cannot specify a
floating version"*. The enabling property is
`CentralPackageFloatingVersionsEnabled`, and without it the two properties the
plan named produce a `Directory.Packages.props` that reads exactly like the
float and cannot restore at all. Measured 2026-08-16 on SDK **10.0.302** while
building the skeleton. Re-establish by deleting the property and restoring.
`[FLOATS]`

**npm keys a lock file's root package on the empty string, and PowerShell's
`ConvertFrom-Json` refuses that outright.** Measured 2026-08-16 on npm **11.19.0**
and PowerShell **7**, while building the payload: `package-lock.json`
`lockfileVersion` 3 opens `"packages": { "": { … } }`, and parsing it raises *"The
provided JSON includes a property whose name is an empty string, this is only
supported using the -AsHashTable switch."* It is a hard parse failure rather than
a dropped key, so it surfaces immediately — but only if something parses the lock
at all, and the natural first version of a payload build does not. Re-establish
by piping any npm lock through `ConvertFrom-Json` with and without
`-AsHashtable`. `[FLOATS]`

**`npm ci` does not rewrite the lock**, verified in the same run by comparing the
file byte for byte either side of the call — which is what makes it usable as the
npm half of the two-restore pattern above: `npm install` from a **deleted** lock
and an empty `node_modules` resolves the `latest` dist-tag, `npm ci` then proves
the resulting lock reproduces that tree on its own. Deleting the lock first is
what guarantees the re-resolution. **Whether `npm install` re-resolves a dist-tag
dependency with a lock already present was not measured** — the payload build
never gets into that state, so the question is open rather than answered.
`[FLOATS]`

**.NET's `FileMode.Append` loses records when several processes share a file;
`FILE_APPEND_DATA` does not.** Measured 2026-08-16 while building the process
log: **eight processes each writing 25 records lost 70 of the 200.** Every write
returned success and the file grew, so nothing anywhere reported it — the lost
records were simply absent. The cause is that .NET's append mode seeks to the
end *at open* and then tracks the position itself, so two writers that opened at
the same length overwrite each other. `FileShare.ReadWrite` permits the sharing
and guarantees nothing about it.

The fix is the platform's own guarantee rather than a lock: a handle opened via
`CreateFileW` with **`FILE_APPEND_DATA` and without `FILE_WRITE_DATA`** has its
writes placed at the end of the file by the filesystem, atomically, regardless
of how many other handles are open. **Requesting `GENERIC_WRITE` silently
forfeits it**, because `GENERIC_WRITE` expands to include `FILE_WRITE_DATA`. The
same eight-by-twenty-five run then loses nothing, repeated three times.

This matters beyond the log. **The design has ~100 concurrent BrowserAI
processes sharing one process log**, and a lock would have worked while also
making logging able to block — the one thing [§E](../../plan/E-lifecycle.md)
says the sink may never do. `[FLOATS]` for the .NET half, which could change
with any SDK; `[STABLE]` for the Win32 guarantee.

**`$(IntermediateOutputPath)` is empty in a `.targets` file imported from the
project body.** Measured 2026-08-16 on SDK **10.0.302** while wiring the
upstream-snapshot gate: a stamp written to `$(IntermediateOutputPath)x.stamp`
landed in the **project directory**, not in `obj\`. The property is defined by
`Microsoft.Common.CurrentVersion.targets`, which the SDK imports *after* the
project body, so a `PropertyGroup` in an imported `.targets` evaluates it to
nothing and the path degrades to a bare filename. `$(BaseIntermediateOutputPath)`
comes from `Microsoft.Common.props` at the top and is set. The failure is
quiet in the worst way: the build works, incrementality works, and the only
symptom is an untracked file that `git status --porcelain` reports — which is
why every build-order step ends by running exactly that. Re-establish by
pointing a `Touch` task at `$(IntermediateOutputPath)` from an imported
`.targets` and looking at where the file lands. `[FLOATS]`

**Two PowerShell traps, both measured 2026-08-16 while making a build script's
output into a build error message.** `Get-Command 'git' -CommandType
Application` returns **two** entries on a Git-for-Windows machine —
`cmd\git.exe` and `mingw64\bin\git.exe` are both on `PATH` — so `$git.Source`
is one string naming two executables and invoking it fails with *"The term
'C:\…\mingw64\bin\git.exe C:\…\cmd\git.exe' is not recognized"*. `Select-Object
-First 1` is required rather than tidy. And **PowerShell 7 emits ANSI colour
escapes even when its output is redirected into a pipe**, which arrives in an
MSBuild `<Error>` as line noise around the diff it is supposed to be carrying;
`$PSStyle.OutputRendering = 'PlainText'` is the switch. `[MACHINE]` for the
duplicate git, `[FLOATS]` for the rendering default.

**A committed byte copy and its regenerated twin are not governed by the same
line-ending rules, and `git add` is where they diverge.** Measured 2026-08-16
with `git hash-object --path`: a two-line CRLF file hashes to its **raw** bytes
under a path matched by `upstream-snapshots/** -text`, and to the **LF-converted**
form under any other path in this repository, where `* text=auto eol=lf` and
`*.json text` apply. `git add` on the second prints *"CRLF will be replaced by
LF the next time Git touches it"* and stores the converted blob. So a
regenerate-and-diff gate over committed copies of upstream files needs its
directory exempted, or the comparison is between a normalised side and an
unnormalised one — **permanently red on a difference that is not a difference**,
whose tempting fix (normalise the generator's output too) makes an upstream
line-ending change invisible instead.

> **All four snapshots are LF today** — `config.d.ts` and
> `playwright-core/browsers.json` as npm installs them, `cli.js --help`'s
> output, and our generated JSON — so the exemption currently changes nothing
> and is a guard against an upstream that changes its mind. **The conversion it
> guards against is not hypothetical in this repository:** every
> `dotnet restore` prints *"in the working copy of
> `src/BrowserAI/packages.lock.json`, CRLF will be replaced by LF"*, because
> NuGet writes those files CRLF and `*.json text` normalises them on the way in.
> Nothing byte-compares a lock file, so there it is harmless. Re-establish by
> counting CR **bytes**: `tr -cd '\r' < file | wc -c`. **Not** with `grep -c
> $'\r'`, which in Git Bash reported every line of an all-LF file as a match
> and produced a confident wrong answer that survived into four documents
> before a byte count contradicted it. `[FLOATS]`

**`dotnet test` transiently reported zero tests once, and does not reproduce.**
Observed 2026-08-16 during
[build-order step 5](../../plan/build-order.md#5-the-two-custom-transports) on
SDK **10.0.302** / .NET **10.0.11**, TUnit **1.65.0**,
`Microsoft.Testing.Platform` **2.3.3**: exit **5**, *"Zero tests ran"*, in about
250 ms, with `--diagnostic` showing the host's log stopping right after
`Setting PlatformExitProcessOnUnhandledException` and a command line ending
`--server dotnettestcli --dotnet-test-pipe testingplatform.pipe.<guid>` — the
handshake `dotnet test` alone uses.

> ⚠️ **Corrected 2026-08-16 (previously: "`dotnet test` runs zero tests against
> this suite … It is not caused by anything in this repository, and that had to
> be proven rather than assumed. A clean `git worktree` of `b8a6553` … reproduces
> it exactly").** It does not reproduce. Re-run the same day against the same
> machine and the same SDK: `dotnet test BrowserAI.slnx` at `e5f4684` returned
> **51 passed, exit 0**, and a fresh `git worktree --detach` of **`b8a6553`** —
> the exact commit the entry named as its proof — returned **30 passed, exit 0**.
> The original entry's load-bearing sentence was therefore false, and so was its
> consequence that *"the evidence recorded for every build-order done-test since
> step 1 came from the executable"*: steps 1 and 2 were evidenced with
> `dotnet test` reporting 5 and then 13 passing tests.
>
> **What went wrong is worth more than the entry was.** A single failing
> observation was written up as a standing property of the toolchain, complete
> with a reproduction that had not been re-run at the moment it was cited. That
> is the same shape as the `grep -c $'\r'` error two entries above, and it is the
> failure this whole directory exists to prevent — the difference between *"I saw
> this once"* and *"this is how it behaves"* is a second run, and it costs
> seconds.

**What to do if it recurs:** run `dotnet test`, then `BrowserAI.Tests.exe`, then
`dotnet test` again. Two disagreeing runs of the same command are a transient;
a stable disagreement between the two commands is the real thing and earns a new
entry with the versions it held under. Do not remove `{ "test": { "runner":
"Microsoft.Testing.Platform" } }` from `global.json` reaching for a fix — it is
the documented MTP opt-in, TUnit is MTP-only, and there is no VSTest mode to
fall back to. `[MACHINE]` for the single observation; nothing here is
`[FLOATS]`, because no standing behaviour was established.

**It recurred, it is now stable, and the retraction above still stands.**
Measured 2026-08-16 during
[build-order step 9](../../plan/build-order.md#9-lossless-passthrough), following
the procedure the paragraph above prescribes. `dotnet test` reports *"Zero tests
ran"*, `error: 1`, exit **5**, in 177–646 ms:

| Run | Result |
|---|---|
| `dotnet test BrowserAI.slnx`, Git Bash, working tree | zero |
| the same, repeated | zero |
| the same, from PowerShell 7 | zero |
| `dotnet test tests/BrowserAI.Tests/BrowserAI.Tests.csproj` | zero |
| `dotnet test BrowserAI.slnx --list-tests` | *"Discovered 0 tests"* |
| the working tree with every step-9 change stashed, i.e. `c9d30d4` | zero |
| **a fresh `git worktree --detach` of `b8a6553`** | **zero** |
| `BrowserAI.Tests.exe` | 106 passed, exit 0 |
| `dotnet BrowserAI.Tests.dll --list-tests` | 88 found *(before the step-9 tests were written)* |

**The last three rows are the whole finding.** The same commit that returned
**30 passed** hours earlier now returns zero, from a clean worktree, while the
same built assembly run directly finds and runs everything. So **nothing in this
repository causes it**, and the earlier retraction was not wrong — both
measurements were real and the machine moved between them. Versions are
identical either side: SDK **10.0.302**, .NET **10.0.11**, and the committed lock
file still resolves TUnit **1.65.0** and `Microsoft.Testing.Platform` **2.3.3**,
so it is not a package float.

The cause is not established. `--diagnostic` shows the host launched with
`--server dotnettestcli --dotnet-test-pipe testingplatform.pipe.<guid>` and the
log ending immediately after `Setting
PlatformExitProcessOnUnhandledException` — the same fingerprint as the transient,
which is consistent with a defect in the `dotnet test` ↔ MTP handshake rather
than in discovery. **Do not write a cause into this entry without measuring
one.** [`TODO.md`](../../TODO.md) carries the investigation, and build-order
step 9's evidence came from `BrowserAI.Tests.exe`, which is stated on that step
rather than left implicit. `[MACHINE]` — it is a fact about this machine on this
date, and the identical tree behaved differently on the same day.

**`Process.ExitCode` throwing after `Dispose()` is now reproduced rather than
quoted**, by
`DirectStdioClientTransportTests.ProcessExitCodeThrowsAfterDisposeWhichIsWhyTheSessionCachesIt`:
a probe run to completion, its exit code read (2), the `Process` disposed, and
`InvalidOperationException` on the next read. If a future runtime made the
cached value survive disposal, that test says so and the caching in
`ChildProcessSession` becomes belt-and-braces instead of load-bearing.
`[STABLE]`

**`ProcessStartInfo.Environment` merging is now reproduced too**, by
`DirectStdioClientTransportTests.TheChildsEnvironmentIsExactlyTheAllowlist`,
which plants eleven refused variables *in the test host* before spawning and
asserts none of them reach the child. Written the other way round — assert only
that the forced variables are present — it would pass against a transport that
never called `Clear()`, on any machine that happened not to have them set.
`[STABLE]`

### Diagnostic severity: what actually enforces a rule, and what only looks like it

All four measured 2026-08-16 on SDK **10.0.302**, by planting the failure and
rebuilding with `--no-incremental` rather than by reading documentation. They
matter here because [style is law in this project](../../CLAUDE.md#style) and a
severity that is quietly inert is the same defect as a config key
`loadConfig` discards.

**`NoWarn` beats `WarningsAsErrors`, and it beats an `.editorconfig` severity
too.** A method holding a statement after a `return` was compiled three ways.
With `TreatWarningsAsErrors` plus `WarningsAsErrors` naming `CS0162`: **1
error**. Adding `<NoWarn>CS0162</NoWarn>`: **0 warnings, 0 errors** — the
unreachable code compiled. Adding `dotnet_diagnostic.CS0162.severity = error`
on top of that NoWarn, and forcing a full rebuild: still **0 warnings, 0
errors**. So naming a warning in `WarningsAsErrors` does **not** protect it from
a later bulk suppression, which is what
[plan/build-order.md asserted](../../plan/build-order.md) and what this
measurement corrected. What naming it there does buy is survival if
`TreatWarningsAsErrors` is ever turned off — a smaller claim, and a true one.
The protection that works has to sit outside the compiler's precedence order
entirely, and here it is a test:
`BuildConfigurationTests.NoBuildFileSuppressesWarnings` fails on any `NoWarn` or
`WarningsNotAsErrors` in a project or shared-props file. `[FLOATS]`

**Bulk `.editorconfig` analyzer configuration is ignored once `AnalysisMode` is
set as an MSBuild property.** `dotnet_analyzer_diagnostic.category-<X>.severity`
had no effect at all: set to `none` for the TUnit assertion category, the rule
kept firing at error. The **per-rule** form is honoured in the same build —
`dotnet_diagnostic.TUnitAssertions0002.severity = none` did suppress it. This is
documented behaviour rather than a bug, and it is worth a measured entry because
the failing form fails *silently*: a category line reads as protection, is
ignored, and nothing reports that. Anything in this repository's
`.editorconfig` that must actually hold is therefore written per-rule.
`[FLOATS]`

**IDE0005 will not run on build without `GenerateDocumentationFile`.** With
`EnforceCodeStyleInBuild` on and IDE0005 escalated, the build fails with a
diagnostic named `EnableGenerateDocumentationFile` telling you to set the
property ([dotnet/roslyn#41640](https://github.com/dotnet/roslyn/issues/41640)).
It is an error rather than a quiet skip, which is the good outcome; the trap is
that the fix also turns on CS1591, so every publicly visible member then needs
an XML doc comment or the build is red under `TreatWarningsAsErrors`. `[FLOATS]`

**NativeAOT embeds `ApplicationManifest` into the published binary.** Verified by
reading the bytes of a `PublishAot` win-x64 publish: `longPathAware`,
`asInvoker` and the Windows 10/11 `supportedOS` GUID are all present in
`BrowserAI.exe`. This is not inherited from an apphost — the publish output is
the native exe, a `.pdb` and the XML doc file, with no managed `.dll` beside it.
It matters because the long-path guarantee is otherwise unfalsifiable: session
directories are caller-chosen and unbounded, and a manifest that silently failed
to embed would present as a path failure deep inside a browser profile tree.
`[FLOATS]`
