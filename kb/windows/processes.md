<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Processes: stdio, files and the interop surface

**Versions in force** unless an entry says otherwise: Windows 11 Pro 26200 · .NET SDK 10.0.400, runtime 10.0.11 · `Serilog.Sinks.Console` 3.1.2 · `Microsoft.Extensions.Logging` 10.0.x · `Microsoft.Windows.CsWin32` 0.3.298.
Measured on [the reference machine](../README.md#the-reference-machine).

Measured facts about how Windows starts a process, what its standard streams and
exit code really do, how a file write becomes durable, and the Win32 interop
surface a supervisor needs to drive all three. Containment is in
[Job objects and process containment](job-objects.md); the build tooling around
this code is in [The build toolchain](../toolchain.md).

## stdio, exit codes and process startup

**`Console` stdio is wrong by default in both directions.** Measured:
`Console.Out` writes **CP437**, not UTF-8 (`é` → `0x82`); `Console.InputEncoding`
also defaults to CP437; **any** `TextWriter` emits CRLF; and a hand-rolled
`new StreamWriter(stream, Encoding.UTF8)` emits a **BOM**. On a JSON-RPC channel
each of the three corrupts the stream on first contact. `[STABLE]`

> **The charter does not date this measurement.** The date is `[UNVERIFIED]`; the
> observations are carried forward as written.

> **Corroborated 2026-08-16 — and the date above stays `[UNVERIFIED]`.** Two
> unrelated 2018-era VB.NET codebases independently hand-reconstruct CP437 over
> a raw console handle to make output appear at all — a console updater and a
> certificate tool, by different authors, neither published. Both carry `Const
> MY_CODE_PAGE As Integer = 437`, `CreateFile("CONOUT$")`, `New
> IO.StreamWriter(FileStream, Encoding.GetEncoding(437))`, `Console.SetOut`, the
> WinUpdate one commented *"VS console redirection fix"*. Two authors reaching
> independently for the same workaround is evidence that **the default really is
> CP437**; it is not evidence about *when* the entry above was measured, so that
> gap is unchanged. **Note what they built:** it is exactly the hand-rolled
> `StreamWriter` the entry above warns about, and it emitted no BOM only because
> the encoding was CP437 rather than UTF-8 — swap the encoding and the identical
> code corrupts a JSON-RPC stream on its first byte. Read from source, not run.
> `[MACHINE]` for the two codebases, **which are not published, so that half is
> not reproducible from here**; the underlying default is `[STABLE]` and is
> reproducible anywhere — write a non-ASCII character to `Console.Out` from a
> fresh console app and read the bytes.

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

Read from source 2026-08-16, not run, at **3.1.2** —
`src/Serilog.Sinks.Console/Sinks/SystemConsole/ConsoleSink.cs:35-38` and
`…/Platform/WindowsConsole.cs` in the public `serilog/serilog-sinks-console`
repository — cross-checked against its `main` the same day. **This one is fully
reproducible**: clone that repository at the tag and read those two files. **The two differ, and the newer one is worse:** 3.1.2 wraps the entire
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
buffered target has nothing else to flush it. Shipped pattern, read 2026-08-16
in a long-lived in-house VB.NET updater stack: its NLog config declares
`<targets async="true">`; **all four** of its loader executables end their
unhandled-exception handler with `Logger.Fatal(...)` then `Environment.Exit(1)`;
and **`LogManager.Shutdown` and `LogManager.Flush` appear nowhere in the whole
repository** — grepped, zero hits. `[STABLE]` for the mechanism, which follows
from `Environment.Exit` not joining a sink's worker thread; `[MACHINE]` for the
observation, and **that codebase is not published**. The general check is two
greps on any codebase using a buffered sink: one for `Environment.Exit`, one for
the sink's flush call, and the finding is the absence of the second near the
first.

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
`NativeErrorCode == 1223` (`ERROR_CANCELLED`). Shipped bug, read 2026-08-16 in
the same unpublished updater stack: a loader filters
`Catch ex As ComponentModel.Win32Exception When ex.ErrorCode = &H80004005` around
an elevating `Process.Start`, inside a `For i = 1 To 10` retry — so *every*
elevation failure was read as a refusal and re-prompted, up to ten UAC dialogs for
a cause that was never the user. Verified against MS Learn 2026-08-16. `[STABLE]`

**`Console.ReadKey()` with stdin redirected throws immediately; it does not
hang.** ***Corrected 2026-08-18 (previously "`Console.ReadKey()` inside a `catch`
in a non-interactive process hangs forever, with no output — there is no console
input to read, and nothing times out. It presents exactly as 'the server is
stuck'", carried as `[STABLE]` and never run.)*** Measured 2026-08-18 on .NET 10,
stdin redirected — which is BrowserAI's own configuration under an MCP client:
the call threw `System.InvalidOperationException`, *"Cannot read keys when either
application does not have a console or when console input has been redirected.
Try Console.Read."*, with no delay. **The rule that a `catch` must not call
console input survives, and the real failure is worse in a different way**: a
throw inside a `catch` replaces the original exception with a new one, so the
cause is not merely delayed, it is destroyed. **What was measured and what was
not**: the redirected-stdin arm was run here; the *console-attached-but-nobody-typing*
arm — which is the case the old sentence actually describes, and which would
block — was **not** run and is not established. Shipped instance, read
2026-08-16 in an unpublished C# directory-cleanup tool that runs as a scheduled
non-interactive job — two calls, both inside `catch` blocks; that read
established that the calls exist, never what they do. Note the shape — both calls sit in the
*unknown-exception* arm, below the specific `UnauthorizedAccessException` and
`DirectoryNotFoundException` handlers, so they fire only on the cases nobody
anticipated: the population least likely to have been exercised in testing and
most likely to be hit in the field. `[STABLE]`

**`Process.ExitCode` throws after `Dispose()`, and
`Process.GetProcessById(pid).ExitCode` always throws.** .NET is *worse* here than
PowerShell, which merely returns `$null`. Cache the value as an `int` the moment
the child exits. `[STABLE]`

> **An uncached exit code does not fail — it reads back empty, and a hard startup
> failure then logs identically to a clean shutdown.** Observed over **five days**
> in the PowerShell launcher this project replaces: the process handle was not
> captured before `WaitForExit`, so `.ExitCode` read `$null`, and the supervisor's
> "child finished" record was byte-identical whether the child had served a
> session or died on its first line. What it hid was total: a CLI flag deleted
> upstream (`--output-mode`, removed in `@playwright/mcp` 0.0.79) made the child
> print `error: unknown option` and exit **1**, with **all four supervised servers
> dead**, and no signal anywhere said so. Recorded 2026-08-13; `[MACHINE]` as an
> observation, `[STABLE]` as a mechanism — the null read follows from the handle
> being gone, on every Windows and every PowerShell. Re-establish by calling
> `WaitForExit` on a `Process` whose handle has been released and reading
> `.ExitCode`. **This is the entry behind the rule that a child's exit code is
> cached as an `int` the moment it is available**, and behind treating "the log
> looks the same either way" as a defect rather than as tidiness.

**`WaitForExit(int)` does not drain the async readers** — only `WaitForExit()`
and `WaitForExitAsync(ct)` do, so the timeout overload truncates stderr.
`[STABLE]`

**Redirecting a child's streams does not stop a *grandchild* inheriting the
stderr pipe, and the parent then blocks until the grandchild exits.** Measured
2026-08-12/13 on the PowerShell launcher this project replaces: a detached
installer started through `Start-Process` with redirection configured inherited
the same stderr pipe handle, so the supervisor reading that pipe never saw EOF
and **every spawn cost 11.71 s — the entire browser download — falling to 0.37 s
once the inheritance was cut.** The pipe stays open because a handle to its write
end is still held, not because anything is still writing; the process that was
redirected has already exited. `[MACHINE]` for the two figures, `[STABLE]` for
the mechanism, which is ordinary Windows handle inheritance. Re-establish by
timing a spawn whose child starts a long-running grandchild, with the parent
reading stderr to EOF, against the same spawn with inheritance suppressed. **The
general form is worth more than the numbers: a redirected stream is drained when
the last holder of its write end closes it, which is not the same event as the
child exiting** — so a supervisor that waits on EOF is waiting on the whole
process tree unless it prevents the handle travelling.

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

## Files, durable writes and deletes

**`Directory.GetFiles` is top-level only, and a recursive enumeration aborts on
the first `UnauthorizedAccessException` rather than skipping the node.** MS Learn,
on the `AllDirectories` overloads: *"`UnauthorizedAccessException` errors may make
the enumeration incomplete. You can catch these exceptions by first enumerating
directories and then enumerating files."* The failure is silent in the worst way —
a partially-walked tree is indistinguishable from a fully-walked smaller one. A
robust recursive delete therefore needs a hand-rolled **post-order** walk with
per-node exception discrimination: deepest child first, so a non-recursive
`Directory.Delete` always sees an empty directory. Reference implementation, read
2026-08-16 in that same unpublished cleanup tool — **the shape is recorded here
rather than the path, because a path nobody else can open is not a
re-establishment route** — recurse subdirectories, then yield files, then yield
the directory itself, with
`UnauthorizedAccessException` and `DirectoryNotFoundException` caught and logged
**per node**, and an optional ACL-reset retry on the denied node. Directly relevant
to `browserai_reinstall_browser`, session destroy, and the per-run instance
directory. Verified against MS Learn 2026-08-16. `[STABLE]`

**`Directory.Delete(path, recursive: true)` does make partial progress — it
deletes what it can and throws ONE exception naming ONE node.** Measured twice
2026-08-16 on **.NET 10.0.11**, Windows 11 Pro 26200, from PowerShell against
`[System.IO.Directory]::Delete($p, $true)`. Two trees, identical layout. With one
file held `FileShare.None`: threw `IOException` naming that file, and survivors
were exactly that file and the two directories above it — the top-level JSON file
and a sibling subdirectory that sorts *after* the held one were both gone. With a
subdirectory the caller may not read (`icacls /deny (OI)(CI)(RX,DE,DC)`): threw
`UnauthorizedAccessException` naming that directory, and again everything else
went. **A hand-rolled post-order walk left the same nodes behind in both cases** —
so the on-disk outcome is not what separates the two primitives. What separates
them is the report: the framework named one node where the per-node walk named
**four** and **two**. The enumeration entry above is unaffected —
`EnumerateFileSystemEntries(…, AllDirectories)` did throw
`UnauthorizedAccessException` and yielded nothing, re-measured in the same pass.
To re-establish: build a tree of a top-level file, a subdirectory holding two
files, and a second subdirectory sorting after it; make one node undeletable
(`[System.IO.File]::Open(..., 'None')` for the held-file arm, `icacls /deny` for
the unreadable arm); call `[System.IO.Directory]::Delete($root, $true)`, catch,
and list what survived. **The second subdirectory is the load-bearing part of the
fixture** — it is what shows the walk continued past the failure, and a tree with
only the locked node cannot tell partial progress from none.
`[FLOATS]` (a BCL implementation detail, and the SDK rolls forward)

**Windows refuses to remove a directory that is a live process's current
directory — and does not refuse to delete the files inside it.** Measured twice
2026-08-16, same runtime, against a childless holder started with
`-WorkingDirectory`. `Directory.Delete(path, recursive: true)` **emptied the
directory completely** and only then threw `IOException` on the node itself; what
survived was an empty directory. `Directory.Move(path, aside)` was refused with
`IOException` **and the contents untouched**, and succeeded the moment the holder
exited. So a working-directory lock is a liveness signal for a *rename* and not
for a *delete*, and BrowserAI's instance sweep claims by renaming
([`TreeDelete`](../../src/BrowserAI/Runtime/TreeDelete.cs)). ⚠️ **The first
arm of this measurement was run against a `cmd /c ping` holder and is not
evidence**: killing `cmd.exe` leaves `ping.exe` alive holding the same cwd, so the
"holder is dead" half never tested what it claimed. Re-established with a
`pwsh -Command Start-Sleep` holder, which has no children. To re-establish:
`Start-Process pwsh -ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 45'
-WorkingDirectory $tree -PassThru`, then try the delete and the move, then
`Stop-Process` and try the move again. `[STABLE]`

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

**A working reference implementation was read rather than designed**, verified
2026-08-16 in an unpublished first-party C# test rig: its `WriteAllTextDurable`
does all three steps — a temp file **in the same
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

**A durable write of this project's own `lock.json` costs 16.1 ms and 18.2 ms.**
Measured 2026-08-16, two runs of **100 sequential rewrites** through the product's
path — a temp file in the same directory opened `FileShare.None` with
`FileOptions.WriteThrough`, `Flush(flushToDisk: true)`, then
`File.Move(overwrite: true)` — at 1.61 s and 1.82 s wall including process start.
The design recorded this cost as
unmeasured and said to measure it before ever trading the guarantee away; this is
that measurement, and at ~17 ms against a file written on `init`, on `resume` and
on a purpose change, there is nothing to trade. Reproduce with
`BrowserAI.TestProbe.exe session-rewrite <dir> <ready-file> 100 <done-file>` and
time it. `[MACHINE]`

**A rename cannot replace a file whose handle is open — under *any* share mode —
and it fails `ERROR_ACCESS_DENIED` rather than a sharing violation.** Measured
2026-08-16 across `FileShare.Read`, `FileShare.Read | FileShare.Delete` and
`FileShare.ReadWrite | FileShare.Delete`: all three refuse
`File.Move(source, target, overwrite: true)` with HRESULT `0x80070005`, because
`MoveFileEx` with `MOVEFILE_REPLACE_EXISTING` needs DELETE on the destination.
`[STABLE]` for the refusal; `[FLOATS]` for the .NET exception type, which is
**`UnauthorizedAccessException` and therefore *not* an `IOException`** — a retry
written to catch `IOException` alone never retries the case that actually
happens. Reproduce:
`SessionLockTests.ARenameCannotReplaceALockFileWhoseOwnHandleIsStillOpen`, which
walks all three share modes on every run.

> **This is what forces close → rename → re-open under a mutex**, and it is worth
> stating because the obvious repair does not exist. [The session design](../../ARCHITECTURE.md#sessions)
> makes an open handle on `lock.json` the lock, and [the locking design](../../ARCHITECTURE.md#locking-ownership-and-the-sweep)
> requires the record to arrive by atomic rename. The natural guess is that
> adding `FILE_SHARE_DELETE` to the lock handle reconciles them — it does not;
> the rename is refused identically. So the handle has to be closed for the
> rename, and the only thing that makes the resulting gap unobservable is that
> every BrowserAI takes the per-directory mutex before create-or-take.

**The rename refuses *readers* too, for the same reason and in the same
direction: a file being replaced is DELETE-PENDING, and every new open of that
name is refused `ERROR_ACCESS_DENIED`.** Measured 2026-08-18 at
`SuiteParallelism.Unbounded`, twice in twenty-eight full-suite runs and at two
different call sites — `File.Move(temp, lock.json, overwrite: true)` refused on a
destination this process had just closed its own handle to, and
`new FileStream(lock.json, FileMode.Open, FileAccess.Read, FileShare.ReadWrite |
FileShare.Delete)` refused while another process was renaming over it. **Sharing
the delete does not help the reader**, and that is the half the entry above does
not cover: `FILE_SHARE_DELETE` is what lets the *other* process's rename proceed,
not what lets this one's open succeed while it is proceeding. The window is one
syscall wide.

> **It is the same `UnauthorizedAccessException`, so the same handler trap
> applies, and this time on the read path where nobody had looked.** Before
> 2026-08-18 both of BrowserAI's *writers* waited it out and neither of its
> *readers* did: `SessionLock.ReadRecord` and the acquire path's own open threw
> straight past `Contended`, which handles a sharing violation and a
> `LockFileException` and not this — so one BrowserAI asking whether a session
> was locked, at the instant another rewrote its own lock, threw out of
> `TryAcquire`. `SessionIndex.FollowOne` did not throw and was worse: it caught
> the denial and reported a perfectly good entry as `EntryUnreadable`.
>
> **The distinction that makes the fix safe is who is entitled to the open.** A
> reader of a record, and a holder re-opening under the per-directory mutex, are
> entitled — a denial is a window that closes, so they wait. `InstanceDirectory`'s
> claim, `LiveInstances`' registration and `FirefoxProfile`'s probe are **not** —
> each exists to discover whether something else holds the thing, so for them the
> refusal *is* the answer and a retry would invert the mechanism. `RenameWindow`
> holds that table and only the first group goes through it. `[STABLE]` for the
> delete-pending refusal, `[MACHINE]` for the observed frequency.

**⚠️ `MoveFileEx` with `MOVEFILE_REPLACE_EXISTING` does NOT keep the destination
name bound throughout: a reader can see the name vanish and come back.**
Measured 2026-08-18 at `SuiteParallelism.Unbounded`, by probing the filesystem at
the instant a read returned no record:

| What was asked | What the machine said |
|---|---|
| does the name resolve | **False** |
| the file's length | — (it did not exist) |
| does the directory exist | True |
| is one of the writer's `lock.json.new-<guid>` temps on disk | **1** |
| does an immediate re-read find a record | **True** |

**All five together are the finding, and no single one of them is.** The name was
genuinely unbound — not a zero-length file, not a missing directory, which are
the other two conditions a null record can mean. A rewrite was **demonstrably**
in flight, because the writer's own temp existed at that instant. And the record
was back on the very next read.

**The sequence a reader can observe across one replace is therefore `denied →
absent → the new record`**, and the practical consequence is sharp: a retry that
handles only the *denial* converts a throw into a **null**, which is the more
dangerous of the two because null means *not locked*.

> **Why the name is unbound at all, which is a hypothesis and is labelled as
> one.** [The entry above](#files-durable-writes-and-deletes) measures that a
> rename over a file with an open handle is **refused**, so the writer's own
> retry loop is running; the reader's handle then closes, the delete that the
> refused attempt left pending completes, and the name is free until the writer's
> *next* retry lands — a gap governed by that loop's 5→100 ms backoff, which fits
> the observed width. `[UNVERIFIED]`: nothing has instrumented the writer and the
> reader on one timeline to confirm it. **The observation is not a hypothesis;
> the explanation is.**
>
> **The fix, if it is ever wanted, is POSIX-semantics rename.**
> `SetFileInformationByHandle` with `FileRenameInfoEx` and
> `FILE_RENAME_POSIX_SEMANTICS | FILE_RENAME_REPLACE_IF_EXISTS` (Windows 10 RS1
> and later) replaces a file that has open handles without refusing and without
> unlinking the name — the old file simply becomes nameless and its handles stay
> valid. .NET exposes no overload for it, so it is a P/Invoke against the most
> safety-critical primitive in this product, and it needs its own measurement
> before it is trusted. Not taken; see the hazard index.

> ⚠️ **Corrected 2026-08-18 (previously "BrowserAI is safe under the absence
> today … every ungated one fails in the safe direction — the sweep's
> `SessionDirectoryFrom` … and `ActOn` …").** The judgement was right about the
> two readers it named and wrong as a claim about all of them, and the reason is
> worth stating: it was written by checking the readers on the *sweep* path,
> which is where the danger was expected, and generalised to *every* ungated
> reader without enumerating them. An [adversarial
> review](../../docs/reviews/2026-08-18-adversarial-locking.md) enumerated all
> thirteen. **Eleven fail safe. Two act on the absence:**
>
> - **`SessionIndex.Locate`** → a `null` record becomes `NotASession` →
>   `SessionIndexEntry.IsRemovable` → `Sweep` **deletes the index entry for a live
>   session**. The R7 re-check does not close it: it re-reads microseconds later,
>   inside the same window, because the window's width is governed by the
>   *writer's* 5→100 ms backoff. Nothing re-asserts the entry afterwards —
>   `Record` is called from `OpenAsync` only — so the session stays invisible to
>   `browserai_list` and to `LiveSessions()` for the rest of its life. Nothing
>   downstream treats an index entry as authority, so the outcome is a wrong
>   *report* rather than a wrong destructive action, which is what keeps it out of
>   the first class.
> - **`SessionManager.Existing`**, `init`'s existence guard → `null` means *"the
>   directory is free, proceed"*. The gated `TryAcquire` downstream stops it
>   becoming two owners; what gets through is a **stale-locked** directory being
>   rebound, because `Compose` takes `Mode` and `Browser` from the request. So
>   `init` can silently re-bind a closed session's browser family over a profile
>   on disk belonging to the other one — the exact thing `resume` refuses
>   explicitly.
>
> **What still holds, and is the reason the original judgement was nearly
> right.** A denial was an unhandled exception escaping `TryAcquire`; an absence
> is a documented return value every caller already handles — `SessionLock` even
> carries the sentence *"which removed its `lock.json` between the refusal and
> the read"* for exactly this. **Every rewrite happens under the per-directory
> mutex**, so no gated reader can see the window at all. The sweep's
> `SessionDirectoryFrom` reads a missing lock as *"not a BrowserAI session
> directory"* and **spares** the process, and `ActOn` gates the kill on
> `SessionLock.TryHoldUnowned`, which takes that same mutex. **No ungated reader
> reaches a wrongful kill or a wrongful delete of a tree.** Both rows above are
> in the hazard index.
>
> **What defending it would cost, so the gap is a decision and not an oversight.**
> A reader cannot tell a transient absence from a real one without waiting, and
> waiting is the common path: `browserai_list` over ten destroyed-but-indexed
> sessions would pay the full budget ten times over. The cheap discriminator is
> the writer's own temp file — which is what the test uses — but reaching for it
> from `RenameWindow` couples it to two different temp-naming conventions.
>
> `[STABLE]` for the unbound window and the sequence; `[MACHINE]` for the
> frequency, three occurrences in thirty-six full-suite runs, all from the one
> arm in this suite where a reader spins on a file another process is rewriting a
> hundred times. Reproduce with
> `SessionLockTests.ARewriteIsNeverObservedTorn`, which probes all five columns
> above on every null and fails on one with no rewrite in flight.

**A wall-clock retry budget measures the machine, not the file, and the attempt
count is what tells you which.** Measured 2026-08-18 at
`SuiteParallelism.Unbounded`, from the writer's side of the same rename:
*"could not be replaced after **3 attempts over 2.3 s**"* — from a loop whose own
sleeps total **15 ms** across those three attempts. So 99.3% of that budget went
somewhere other than the retries: the process was not being scheduled. The file
was very likely free for most of it.

> **This is the shape to hunt in shipped code, and it had been raised once
> already for the same reason without the lesson being taken.** The budget had
> gone from *five attempts over 150 ms* to *two seconds* on 2026-08-16 after
> exhausting under full-suite load, and then exhausted again at higher
> parallelism — because the number was chosen against how long the *contention*
> lasts, when what expires it is how long the *scheduler* makes you wait. A
> budget for a live-system transient has to be sized against the second of those:
> the corrected figure is **thirty seconds**, which is 2,000× the sleep the loop
> actually asks for. `[MACHINE]` for the 3-in-2.3 s observation.
>
> **And the message has to say which.** *"Something else is holding it open"* was
> a claim the code could not support: many attempts over the budget means
> contention, few attempts over the budget means starvation, and only the attempt
> count separates them. It is in the message now, with the sentence that reads
> it.

**A file that has just been renamed into place can still be briefly unopenable.**
Observed once on 2026-08-16, roughly one run in a dozen: a probe report written
temp-then-renamed failed `File.ReadAllTextAsync` on the destination with *"the
process cannot access the file … because it is being used by another process"*,
on a file no BrowserAI process had ever opened. The atomicity of the rename is
not in question — what it guarantees is that a reader sees the old bytes or the
new ones, never that the open succeeds. Something outside this repository holds a
freshly-created file for a moment; the retry budget below exists for the same
condition seen from the writing side. **Anything polling for a file another
process produced must retry the *open* as well as the existence check**, and open
`FileShare.ReadWrite | FileShare.Delete` so it cannot itself refuse a writer or a
rename in flight. Reproduce by running the full suite repeatedly and watching
`SessionLockTests`; `Harness/ProbeReport.cs` is where the retry lives.
`[MACHINE]`

**Five attempts over 150 ms is not a large enough retry budget for that rename.**
Measured 2026-08-16: with a second process reading the destination in a tight
loop and the rest of the suite running beside it, the 5-attempt / 10-20-40-80 ms
budget taken from the C# prior art above **exhausted and threw**. Bounded by
total elapsed time instead — 2 s, backing off 5 ms doubling to a 100 ms cap —
and green across two full-suite runs. `[MACHINE]` for the numbers; the shape is
general.

> **It was invisible until it was made visible, which is the more useful half.**
> The rewriting probe's streams were drained and discarded by the test rig, so an
> exhausted budget presented as the *host* waiting out its own ninety-second
> patience and failing on a wholly unrelated assertion. Twice. The probe now
> catches, writes the failure into its report file, and exits non-zero, and the
> host asserts on that field — after which the same run named the cause on the
> first attempt. A child whose output goes nowhere is a child whose crash is
> indistinguishable from slowness.

**Concurrent renames over one destination all succeed, and leave one valid
file.** Measured 2026-08-16, twice: **8 processes × 250 re-assertions = 2,000
`File.Move(temp, entry, overwrite: true)` calls at the same destination**,
released simultaneously from a shared start gate, each writing its own
GUID-named temp in the target directory first. Both runs ended with **one file,
content byte-exact, zero rename failures logged, and all eight processes exit
0**. That is the measurement behind [the session index taking no lock at
all](../../ARCHITECTURE.md#locking-ownership-and-the-sweep): `MoveFileEx` with
`MOVEFILE_REPLACE_EXISTING` replaces the directory entry in one step, so
concurrent writers serialise in the filesystem and a concurrent reader sees the
old file or the new one and never a torn one. `[STABLE]` for the mechanism,
`[MACHINE]` for the counts. Reproduce with
`SessionIndexTests.TwoProcessesWritingOneEntryConcurrentlyLeaveOneValidFile`,
which runs the same 8 × 250 on every build; raise `Writers` and `WritesEach` to
push it further.

> **Note what the writers do *not* do**, because it is what makes this cheap: no
> mutex, no read-before-write, no compare. Every writer writes unconditionally,
> and the content is a pure function of the file's own name, so the winner of any
> race wrote exactly what every loser was about to. A "skip if already correct"
> fast path was deliberately left out — after the first write nobody would
> contend, and the concurrency test would then prove nothing while still passing.

**A non-durable temp-and-rename write costs 1.7 ms alone and 9.2 ms under 8-way
contention** — against ~17 ms for the durable `lock.json` write above. Measured
2026-08-16, two runs each: **1.72 and 1.84 ms** per write with one writer,
**9.15 and 9.26 ms** with eight writers on one name. Each write is a temp file
created and written, `File.Move(overwrite: true)`, and the temp deleted in a
`finally` — no `WriteThrough`, no `Flush(flushToDisk: true)`. So durability is
roughly a **10× multiplier on an uncontended small write**, and contention on a
single name is roughly **5×** on top of the base cost. That ratio is why
[the session index is written without either](../../ARCHITECTURE.md#locking-ownership-and-the-sweep)
while `lock.json` keeps both: an index entry regenerates itself from the
directory it names, and a lock record cannot. `[MACHINE]`

**`Utf8JsonWriter`'s default encoder escapes `+`**, so every ISO 8601 timestamp
with a positive UTC offset is written with its sign as a `+` escape.
Measured 2026-08-16 while writing `lock.json`: the file round-trips perfectly,
parses everywhere, and is unreadable by the person the file exists for.
`JavaScriptEncoder.UnsafeRelaxedJsonEscaping` — the same encoder
[the server transport already takes](../mcp/sdk.md) — removes it. **It was caught
only by an assertion on the literal bytes**; every assertion on the parsed value
passed, in both directions, which is exactly the shape of a defect that ships.
`[FLOATS]` — the encoder's safe list belongs to `System.Text.Json`, which the SDK
floats. Reproduce:
`LockRecordTests.TimestampsAreWrittenAsIso8601WithAnExplicitOffset`.

**`LoggerFactory.Dispose()` never disposes a provider *instance* handed to
`AddProvider`.** Measured 2026-08-16 on `Microsoft.Extensions.Logging` 10.0.x, by
planting a provider that counts its own disposals:
`LoggerFactory.Create(b => b.AddProvider(instance))` followed by
`factory.Dispose()` reported **0 disposals** — a DI container does not dispose an
instance it did not create. The consequence in this repository was a
`ProcessLog.Dispose()` that closed nothing: its rolling file handle survived, and
the log could not afterwards be opened with `FileShare.None`. It cost nothing in
`Main`, which exits immediately after; it was found the first time something
short-lived opened a process log and then read it back, which is a Velopack hook.
`SessionLogging` was already immune because it disposes its file **explicitly
after** the factory — a second call that reads as redundancy and is the actual
mechanism. `[FLOATS]` on the logging packages. Reproduce:
`ProcessLogTests.DisposingTheProcessLogReleasesTheFileHandle`, which fails at the
exclusive open if the explicit `_writer.Dispose()` is removed.

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
making logging able to block — the one thing [the observability design](../../ARCHITECTURE.md#process-containment-and-observability)
says the sink may never do. `[FLOATS]` for the .NET half, which could change
with any SDK; `[STABLE]` for the Win32 guarantee.

## Saturation: the 100-process design point

**80 concurrent headless Chromium instances start cleanly on this machine, and
that is a negative result about a failure we were chasing.** Measured
2026-08-18, sweeping N = 5, 10, 20, 40, 60, 80 real
`chrome.exe --headless=new` launches, each with its own `--user-data-dir` and
`about:blank` — the exact command line
`StraySweepTests.TheSweeperFindsARealBrowserItLaunchedItselfInTheInteractiveSession`
uses — held for 20 s and then counted:

| Concurrent browsers | Died at launch | Machine processes | Free physical | Commit |
|---:|---:|---:|---:|---|
| 5 | 0 | 669 | 75.9 GiB | 86.7 / 141.2 GiB |
| 20 | 0 | 834 | 73.1 GiB | 91.5 / 141.2 GiB |
| 40 | 0 | 1,070 | 69.2 GiB | 97.9 / 141.2 GiB |
| 60 | 0 | 1,267 | 66.4 GiB | 102.0 / 141.2 GiB |
| **80** | **0** | **1,436** | 65.3 GiB | 103.6 / 141.2 GiB |

**Zero launch failures at any level**, at nearly double the process count the
saturation test's own 802 produces.

> **So "the machine was carrying too many browsers" is not the explanation for
> that test's intermittent death, and it was the leading one.** What this sweep
> does *not* reproduce is the suite's other axis: it ran on an otherwise-idle
> machine, so every browser had CPU. The suite starves CPU rather than memory or
> handles, and that remains the open candidate — along with **desktop heap**,
> which no documented API reports and which none of the columns above would show.
> Recorded here as a bounded negative result rather than a diagnosis. `[MACHINE]`
>
> **To re-establish**, for each N: give every instance its own `--user-data-dir`
> under a scratch root and start
> `chrome.exe --headless=new --user-data-dir=<own> --no-first-run
> --no-default-browser-check --disable-component-update --enable-logging
> --log-file=<own> --v=1 about:blank`; let them settle 20 s; count how many have
> exited and read the log of each that has. Take the machine figures from
> `GetPerformanceInfo` — `MachineLoad.Describe()` in the suite's harness prints
> exactly these columns. **Clean up by pid and verify by full image path**: stop
> each browser you started, then re-enumerate for any process whose image path is
> the provisioned `chrome.exe` and stop those too, because the children of a
> headless browser do not always go with it — 102 survived one level here. Never
> by image name; that is [the rule](../../HAZARDS.md) a measurement does not get
> an exception to.

**A record on stderr is not durable, and a record in the process log is — the
two diagnostic channels differ and only the file's guarantee is written down.**
Measured 2026-08-18 on two consecutive CI runs. `ProcessLog` wires stderr through
`AddConsole`; `RollingFileWriter` is one unbuffered `WriteFile` per record against
a `FILE_APPEND_DATA` handle. A process ended with `TerminateProcess` therefore
keeps everything the file sink wrote and **loses whatever the console queue still
held** — and the two runs lost *different* amounts of the tail, which is the
signature of a queue rather than of a call that never happened.

**That the console provider queues at all is first-party documented rather than
inferred**, which matters because the observation above would otherwise have only
an explanation nobody had checked. `ConsoleLoggerOptions.QueueFullMode` exists,
its default is `Wait`, and `ConsoleLoggerQueueFullMode.Wait` is defined as
*"Blocks the logging threads once the queue limit is reached"* — a queue that can
fill, and that blocks *the logging threads* rather than the writing one, is
drained by something else. Verified against MS Learn 2026-08-18. The same page
gives the other half of the shape: the full mode is `Wait`, **not** `DropWrite`,
so records are not discarded under pressure and process death is the only way
this loses one.

> **The consequence for tests, and it cost two red CI runs:** an assertion of the
> form *"the product recorded X"* must read the process log, not stderr, whenever
> the process is killed rather than shut down. A developer's machine drains that
> queue before the kill and stderr looks complete, so the defect is invisible
> until the suite meets a smaller machine. `ProcessLogRecords.ForPid` is the
> reader, and it is scoped to one pid because the log is machine-wide.
> `[STABLE]` for the asymmetry, which follows from the two sinks' designs;
> `[MACHINE]` for the observation that four cores under 431 tests is enough to
> expose it.

**100 concurrent BrowserAI processes with 24 live Chromium trees is 802
processes, and this machine carries it.** Measured 2026-08-17 by
`SaturationTests`, which starts 100 published binaries at once, gives each its
own session with a real `node.exe`, and has 24 of them launch, close and
relaunch a real headless Chromium. Every peer answered; every session was
claimed by exactly one process and its `lock.json` named that process's pid;
every job object was pairwise disjoint; nothing survived teardown; the shared
process log held no torn record. **82 s** wall, alone on the machine. The 802
figure is the survivor census from the fault-injection run in which teardown was
deliberately skipped, so it is a count of what was actually live rather than an
estimate. `[MACHINE]`

**At that size the machine has no headroom left, and it shows up as other
processes' hang detectors, not as anything BrowserAI does.** Measured the same
day, same test, run *inside* the full 419-test suite instead of alone: seven
unrelated tests failed, every one of them on a 30-second in-process bound —
*"No frame arrived on this pipe within 30 s"* between two objects in the same
process, and a rig teardown reporting its server task still running after 30 s.
Nothing failed in the product. **The lesson is about what a hang detector can
mean**: at a ~25× CPU overcommit a thirty-second silence between two in-process
objects is no longer evidence of a deadlock, so a suite that saturates the
machine cannot also use short wall-clock silences as deadlock detection.
Dropping the browser subset from 24 to **8** — 100 BrowserAI processes, 100 node
children, ~64 Chromium — is green in-suite and costs **105 s** for the whole
suite, of which 96 s is this one test. `[MACHINE]`

**A pid alone is not an identity at this scale, and the margin is not
theoretical.** The first version of the disjointness assertion compared job
membership by pid and reported **twelve** processes apparently shared between
two jobs on its very first run — every one a pid Windows had recycled between
two peers reading their membership. Twenty-four Chromium trees closing at once
frees on the order of two hundred pids within a second. Keyed on
`(pid, creationFileTime)` the same run is clean. Measured 2026-08-17. `[STABLE]`
for the mechanism; the count is `[MACHINE]`.

**`Directory.Move` is refused with `ERROR_ACCESS_DENIED` on a directory whose
files were written milliseconds earlier, and nobody has identified the holder.**
***Corrected 2026-08-18 (previously "…and the holder is a scanner rather than a
process anyone can name", which asserted in the heading what the body concedes
was never established).*** The refusal rates below are measured; **the cause is
not**. Windows refuses to rename a directory while any handle is open below it —
that half is documented — and a real-time anti-malware filter opening every file
just after it is written is a *plausible* holder that was never confirmed. **The
distinction is load-bearing**, because the rule drawn from it is that a rename
used as a *commit* gets a bounded retry while a rename used as a *liveness test*
gets none: if the holder is transient and foreign the retry is right, and if it
is ever something of ours the retry masks a defect. **The tool to settle it
already ships** — `Interop/RestartManager.cs` exists to answer *who holds this*;
call `RmGetList` on the path at the instant of the denial. Measured 2026-08-17 in two independent places under a
fully parallel suite: the first-run cache's publish-by-rename failed in **five of
twenty-one** runs with *"Access to the path '…\.staging-&lt;guid&gt;' is
denied"*, and `InstanceDirectoryTests`' planted abandoned directory failed to be
reclaimed in **one of ten**. Neither reproduced once at four-way parallelism.
**The two correct answers are different, and which one applies depends on what
the rename means.** Where the rename is a *commit* — nothing else can be holding
the tree, so a refusal is transient — a bounded retry is right, and it is the
same shape `InstallationMarker` already uses against the same class of
transient. Where the rename is a *liveness test*, as in
`InstanceDirectory.Claim`, a retry is wrong: a genuinely live directory always
refuses, so the retry's budget would be paid once per live instance on every
startup — minutes, at the design point. To re-establish: write a file into a
directory and rename the directory in the same millisecond, on a machine under
heavy I/O with real-time scanning on. `[MACHINE]`

**A suite of 419 tests run all-at-once is bounded by CPU oversubscription, not
by thread-pool injection.** Measured 2026-08-17. Raising the parallel limit from
4 to unbounded took the suite from **33.7 s** to **~20 s**, and produced
multi-second latencies on *in-process* round trips: 1.51 s and 2.27 s against an
800 ms budget, for work that normally answers in milliseconds. The obvious
suspect was the thread pool's hill-climbing injection — the pool starts at
`Environment.ProcessorCount` and adds roughly one thread per 500 ms — but
`ThreadPool.SetMinThreads(1024, 1024)` **did not help**: the same measurement
came back **worse**, at 2.27 s where it had been 1.51 s. The cause is plain
arithmetic: 416 runnable threads over 32 cores is a 13× overcommit, and one
round trip through the in-process rig is four thread handoffs. **A wall-clock
assertion over an async pipeline cannot survive this and should not be written**;
the fix was a `TimeProvider` seam on the one timer in the product, so the test
advances the clock itself. `[MACHINE]`

## The Win32 interop surface

**`NtQueryInformationProcess` reads a parent PID in ~0.77 µs/call**, against
~3.3 ms for `Process.GetProcessById` and milliseconds for WMI. `dotnet/runtime`
itself uses it. `[MACHINE]` for the numbers, `[STABLE]` for the API.

**`LibraryImport` does not support `StringBuilder`**, so a `CreateProcessW`
command line must be passed as a writable `char[]`/`Span<char>` — the API mutates
the buffer, and a `string` literal is not valid. `[STABLE]`

**`[DllImport]` works under NativeAOT on Windows, and ILC generates its
marshalling stubs ahead of time.** `Corrected 2026-08-17` (previously
*"`DllImport` is the wrong choice under NativeAOT because it relies on runtime
IL-stub generation"*, carried here as `[STABLE]` and never measured). Measured
2026-08-17 on **SDK 10.0.400 / ILC 10.0.11**, `win-x64`, `net10.0-windows`: a
probe with **38 `[DllImport]` declarations** across kernel32, user32, ntdll and
rstrtmgr — `SetLastError = true` throughout, and including `StringBuilder`
marshalling, `SafeHandle` returns, struct byref and a managed callback delegate
passed to `EnumWindows` — published with **zero trim or AOT warnings** and
passed all **41** runtime checks, in a 1,209,856-byte binary. `SYSLIB1054` did
not fire at default analyzer settings either.

**All seven hand-written interop structs match Microsoft's own Win32 metadata
exactly.** Measured 2026-08-17 against `Microsoft.Windows.CsWin32` **0.3.298**,
which generates from the same metadata Windows ships: `STARTUPINFOW` **104**,
`STARTUPINFOEXW` **112**, `PROCESS_INFORMATION` **24**, `SECURITY_ATTRIBUTES`
**24**, `IO_COUNTERS` **48**, `JOBOBJECT_BASIC_LIMIT_INFORMATION` **64**,
`JOBOBJECT_EXTENDED_LIMIT_INFORMATION` **144**; `offsetof(Affinity)` **48**,
`offsetof(LimitFlags)` **16**, `offsetof(IoInfo)` **64**, agreeing both ways.
**A size check alone is not sufficient and this was demonstrated rather than
argued**: reordering `Affinity` after `PriorityClass`/`SchedulingClass` leaves
the struct at 64 bytes and slides that field to offset 56, so the size assertion
passes and only the offset assertion fails. `[STABLE]` — these are the x64
Windows ABI and do not move. Re-establish by running
`InteropLayoutTests`, which is now a permanent guard rather than a
re-measurement, and which reads the shipped `private` nested types by reflection
rather than copies of them.

**This does not change the rule, only its reason.** `[LibraryImport]` remains
correct here because it is Microsoft's documented first recommendation for .NET
7+, because the marshalling it emits is ordinary C# that can be read and stepped
through, and because `SYSLIB1054` exists to move code toward it. What the old
sentence would have caused is a wrong answer to a *different* question: a
generator that emits `[DllImport]` (which is what `Microsoft.Windows.CsWin32`
does, and will keep doing — [#593](https://github.com/microsoft/CsWin32/issues/593)
and [#1333](https://github.com/microsoft/CsWin32/issues/1333) are both closed
*not planned*) is **not** ruled out by AOT. Note that CsWin32 #1333's own
opening post repeats the same misconception, which is a fair guess at where it
entered this repository. `[STABLE]` — re-establish by publishing any AOT project
containing a `[DllImport]` and reading the ILC output. The probe used here was
38 declarations across `kernel32`, `user32`, `ntdll` and `rstrtmgr` with
`SetLastError=true`, covering `StringBuilder` marshalling, `SafeHandle` returns,
struct byref and an `EnumWindows` callback delegate; it published with zero
warnings and passed all 41 runtime checks. **The shape is recorded rather than
the path** — the probe lived outside the repository and a path nobody else can
open is not a re-establishment route.

**`Environment.GetFolderPath(SpecialFolder.UserProfile)` does not read
`%USERPROFILE%`.** It resolves from the process token, so overriding the
environment variable in a child's block moves nothing. Measured 2026-08-16 while
trying to simulate a machine with no MCP client on it: the child was started with
`USERPROFILE` pointed at an empty scratch directory and `PATH` cut to `system32`,
and it still found the client at `<user profile>\.local\bin\claude.exe`, resolved
from the token rather than from either variable. **The attempt failed and
the run is still evidence** — it proves the `PATH`-independent fallback in
`ClientCommandLine` is load-bearing rather than decorative, because with `PATH`
stripped that fallback is what completed the registration. `[STABLE]` — a Win32
known-folder property. Consequence: **a clientless machine cannot be simulated
from the environment**; the client-absent path is exercised through the
`IRegistrationCommand` seam instead, and `Locate` returning `null` for a name that
is genuinely not on this machine is asserted by
`RegistrationTests.TheClientIsLocatedByFileNameAndNeverAsAShim`.

**A COM/interop enum value the running OS does not know throws on assignment** —
at the property set, not at load and not at compile time. The managed enum is only
an integer; the rejection happens inside the COM object receiving it, so the
compiler, the interop layer and any static analysis all see a valid value. Shipped
mitigation, read 2026-08-16 in an unpublished VB.NET Windows Update client, which
wraps `UpdateDownloader.Priority = DownloadPriority.dpExtraHigh` — a Windows Update
Agent value newer than the OS floor that project targeted — in a try/catch that
logs *"Switching from ""dpExtraHigh"" priority to ""dpHigh"" priority due to OS
incompatibility"* and downgrades. **Directly applicable here:** the job-object
information classes and the `NtQueryInformationProcess` information classes this
project P/Invokes are the same shape, so every information class used must either
be safe at our Windows floor or carry an explicit downgrade path — a value that is
merely absent on an older build fails at the call site, where nothing else will
catch it. `[STABLE]` for the mechanism, which is how COM interop works and can be
reproduced against any COM object with a version-gated enum; the shipped instance
is `[MACHINE]` and **not reproducible from this repository**.

**A running executable can be renamed, and so can every directory above it;
only deleting the image is refused.** Measured 2026-08-18 on Windows
**10.0.26200**, .NET 10, against a live process started from
`outer\inner\held.exe` with its **current directory set to `C:\`**, so that
[the separate cwd rule](#files-durable-writes-and-deletes) could not be the
cause:

| Operation, while the image is running | Outcome |
|---|---|
| `Directory.Move` of the parent (`outer\inner`) | **succeeded** |
| `Directory.Move` of the grandparent (`outer`) | **succeeded** |
| `File.Move` of the running `.exe` itself | **succeeded** |
| `File.Delete` of the running `.exe` | **refused**, `UnauthorizedAccessException` |

The image section keeps the *file* alive, not its *name*, which is why
rename-aside-then-replace is the ordinary Windows update pattern. **This
falsified a settled design row**: `browserai_reinstall_browser` closed the
download-alongside-and-swap option on *"Windows will not rename a directory
holding open executables"*, which is not so
([DECISIONS](../../DECISIONS.md)). The refusal it justified is left standing —
what a browser does when its tree is renamed underneath it has **not** been
measured — but the option is open rather than impossible. `[STABLE]`.
Re-establish by starting any long-running exe from a nested directory with its
cwd elsewhere, then renaming the parent, the grandparent and the image.

**Windows does not reuse a pid while any handle to that process is open, and
the control shows reuse is otherwise quick.** Measured 2026-08-18 on Windows
**10.0.26200**, spawning `cmd.exe /c exit` in a loop and recording each pid.
*Control arm* — the process handle released as soon as the child exited:
**2,009 distinct pids over 2,010 spawns, and the 2,010th repeated one** (59424).
*Claim arm* — an `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` handle taken on
each child **after it had already exited** and never closed: **6,030 spawns,
6,030 handles held, 0 failed opens, and not one pid repeated** — three times the
control's budget without a single collision. Before this it was asserted as a
bare platform fact at four sites and measured at none. `[STABLE]` for the kernel
behaviour, `[MACHINE]` for the 2,010. **The control is the load-bearing half**:
without it a run with no repeats is indistinguishable from a pid space too large
to wrap during the test. Re-establish by spawning a trivial child in a loop
twice — once disposing the handle at exit, once keeping an `OpenProcess` handle
in a list — and comparing the spawn count at the first repeated pid. This is
what `(pid, creationFileTime)` and `LaunchedProcess`'s held handle rest on.

**KnownDLLs makes `[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]`
inert for 39 of this product's 43 P/Invoke declarations, and load-bearing for
the other 4.** Measured 2026-08-18 on Windows **10.0.26200**, .NET 10, with
genuine `System32` copies of `kernel32.dll`, `user32.dll` and `rstrtmgr.dll`
planted beside a probe executable and the same calls declared twice, once with
the attribute and once without:

| Library | Declarations | Without the attribute | With it |
|---|--:|---|---|
| `kernel32.dll` | 33 | `C:\WINDOWS\System32` | `C:\WINDOWS\System32` |
| `user32.dll` | 5 | `C:\WINDOWS\System32` | `C:\WINDOWS\System32` |
| `ntdll.dll` | 1 | `C:\WINDOWS\SYSTEM32` | `C:\WINDOWS\SYSTEM32` |
| `rstrtmgr.dll` | 4 | **the application directory** | `C:\WINDOWS\SYSTEM32` |

`kernel32` and `user32` are **KnownDLLs** — resolved from the `\KnownDlls`
section objects before any path search runs, confirmed against
`HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\KnownDLLs` on this
machine — and `ntdll` is mapped by the loader before user code runs. Only
`rstrtmgr.dll` is neither, and it is the only one whose resolution the attribute
changes. **The rule survives unchanged and every declaration keeps the
attribute**: it costs nothing, it is correct for the one library here that is not
a KnownDLL, and the next library added may not be one either. **The trap is the
audit, not the rule** — anyone testing this by planting a fake `kernel32.dll`
sees nothing happen and concludes the attribute is decorative. `[STABLE]` for
KnownDLLs, `[MACHINE]` for the list membership. Re-establish by copying a
`System32` DLL beside a probe and reading `GetModuleFileNameW(GetModuleHandleW(name))`
after a call, with and without the attribute.

**`Marshal.GetLastPInvokeError()` survives managed work and is destroyed by the
next P/Invoke, and without `SetLastError = true` there is nothing to read at
all.** Measured 2026-08-18 on .NET 10, `win-x64`, against a deliberately failing
`OpenProcess` on pid 4 (`ERROR_ACCESS_DENIED`, 5), reading the error after each
of seven intervening operations:

| Between the call and the read | Error read back |
|---|--:|
| nothing | **5** |
| a 5,000-append `StringBuilder` (pure allocation) | **5** |
| `GC.Collect()` | **5** |
| a `MemoryStream` written and disposed | **5** |
| `Console.Out.Flush()` | **5** |
| a second failing P/Invoke of our own | **5** |
| **`File.Exists` on a path that is not there** | **0** |
| *declared without `SetLastError`, nothing in between* | **0** |

So the danger is narrower and sharper than "anything at all": the captured value
is a thread-local that **only another capturing P/Invoke overwrites**, and pure
managed work — allocation, a GC, a managed `Dispose` — leaves it intact. What
destroys it is a `Dispose`, a log call or a guard that *itself reaches the
platform*, and `File.Exists` alone is enough. **The rule stands and is now
measured rather than asserted**; what changes is which intervening statements are
actually dangerous. **The second row is the trap nobody was looking for**: the 11
of this product's 43 declarations that omit `SetLastError = true` make
`Marshal.GetLastPInvokeError()` return a confident **0**, which reads as success
rather than as "not captured". `[STABLE]` for the mechanism, `[FLOATS]` for the
BCL call sites that do the clobbering. Re-establish by calling a failing import
with capture on, then reading after each candidate statement in turn.

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
