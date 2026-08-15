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
