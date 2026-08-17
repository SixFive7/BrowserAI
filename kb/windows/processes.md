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

**`Console.ReadKey()` inside a `catch` in a non-interactive process hangs
forever, with no output** — there is no console input to read, and nothing times
out. It presents exactly as "the server is stuck". Shipped instance, read
2026-08-16 in an unpublished C# directory-cleanup tool that runs as a scheduled
non-interactive job — two calls, both inside `catch` blocks. Note the shape — both calls sit in the
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
files were written milliseconds earlier, and the holder is a scanner rather than
a process anyone can name.** Windows refuses to rename a directory while any
handle is open below it, and a real-time anti-malware filter opens every file
just after it is written. Measured 2026-08-17 in two independent places under a
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
