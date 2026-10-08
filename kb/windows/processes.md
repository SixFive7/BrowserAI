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

> **Corroborated 2026-08-16 -- and the date above stays `[UNVERIFIED]`.** Two
> unrelated 2018-era VB.NET codebases independently hand-reconstruct CP437 over
> a raw console handle to make output appear at all -- a console updater and a
> certificate tool, by different authors, neither published. Both carry `Const
> MY_CODE_PAGE As Integer = 437`, `CreateFile("CONOUT$")`, `New
> IO.StreamWriter(FileStream, Encoding.GetEncoding(437))`, `Console.SetOut`, the
> WinUpdate one commented *"VS console redirection fix"*. Two authors reaching
> independently for the same workaround is evidence that **the default really is
> CP437**; it is not evidence about *when* the entry above was measured, so that
> gap is unchanged. **What they built:** it is exactly the hand-rolled
> `StreamWriter` the entry above warns about, and it emitted no BOM only because
> the encoding was CP437 and not UTF-8 -- swap the encoding and the identical
> code corrupts a JSON-RPC stream on its first byte. Read from source, not run.
> `[MACHINE]` for the two codebases, **which are not published, so that half is
> not reproducible from here**; the underlying default is `[STABLE]` and is
> reproducible anywhere -- write a non-ASCII character to `Console.Out` from a
> fresh console app and read the bytes.

**A logging library's type initializer can write to the protocol channel.**
`Serilog.Sinks.Console`'s `ConsoleSink` has a **static constructor** calling
`WindowsConsole.EnableVirtualTerminalProcessing()`, which calls `SetConsoleMode`
on `GetStdHandle(-11)` (`STD_OUTPUT_HANDLE`) -- before any log line is written, and
reachable by merely touching the type. When stdout is a pipe, `GetConsoleMode`
fails, the guard `stdout != INVALID_HANDLE_VALUE && GetConsoleMode(...)` goes
false, and it **silently no-ops** -- so the behaviour is invisible under MCP and
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

Read from source 2026-08-16, not run, at **3.1.2** --
`src/Serilog.Sinks.Console/Sinks/SystemConsole/ConsoleSink.cs:35-38` and
`.../Platform/WindowsConsole.cs` in the public `serilog/serilog-sinks-console`
repository -- cross-checked against its `main` the same day. **This one is fully
reproducible**: clone that repository at the tag and read those two files. **The two differ, and the newer one is worse:** 3.1.2 wraps the entire
P/Invoke body in `#if PINVOKE`, defined only for `net45` and `netcoreapp1.1`
(csproj lines 29-34), so a modern consumer resolving the `netstandard2.0` asset
gets an empty method -- the hazard is real but dormant there. **Upstream `main` has
dropped the guard entirely**: the `GetStdHandle` / `GetConsoleMode` /
`SetConsoleMode` calls are unconditional, and they are `DllImport`, not
`LibraryImport`. Re-establish by reading those two files **at the version actually
referenced**, never at whichever copy is on disk. `[FLOATS]` for Serilog's code;
`[STABLE]` for the mechanism -- a type initializer runs before first use, and
`GetConsoleMode` on a pipe fails, on every Windows.

**An async log sink plus `Environment.Exit` drops the final buffered messages**,
so every `Logger.Fatal(...)` → `Exit(1)` path loses precisely the line describing
the crash. `Environment.Exit` does not wait for a sink's own worker thread, and a
buffered target has nothing else to flush it. Shipped pattern, read 2026-08-16
in a long-lived in-house VB.NET updater stack: its NLog config declares
`<targets async="true">`; **all four** of its loader executables end their
unhandled-exception handler with `Logger.Fatal(...)` then `Environment.Exit(1)`;
and **`LogManager.Shutdown` and `LogManager.Flush` appear nowhere in the whole
repository** -- grepped, zero hits. `[STABLE]` for the mechanism, which follows
from `Environment.Exit` not joining a sink's worker thread; `[MACHINE]` for the
observation, and **that codebase is not published**. The general check is two
greps on any codebase using a buffered sink: one for `Environment.Exit`, one for
the sink's flush call, and the finding is the absence of the second near the
first.

**`UseShellExecute` defaults to `True` on .NET Framework and `False` on .NET
Core**, changed in .NET Core 2.1 and recorded as a
[breaking change](https://learn.microsoft.com/dotnet/core/compatibility/fx-core#core-net-libraries).
`True` routes the launch through the graphical shell, which **silently detaches
the child and makes stream redirection impossible** -- `RedirectStandardOutput` and
friends require `UseShellExecute = false`, and `ProcessStartInfo.Environment`
throws `InvalidOperationException` at `Start()` if it is true. The trap is porting
supervision code from an older project, where the *absence* of an assignment meant
the opposite thing. Verified against MS Learn 2026-08-16. `[STABLE]`

**`Win32Exception.ErrorCode` is the HRESULT, not the Win32 code.** `ErrorCode` is
inherited from `ExternalException` and documented as *"the HRESULT of the error"*;
the Win32 number lives on `NativeErrorCode`. In practice `ErrorCode` reads
`0x80004005` (`E_FAIL`, *"unspecified failure"*) for almost every
`Win32Exception`, so **an exception filter keyed on it matches everything**. The
value that actually means "the user cancelled the UAC prompt" is
`NativeErrorCode == 1223` (`ERROR_CANCELLED`). Shipped bug, read 2026-08-16 in
the same unpublished updater stack: a loader filters
`Catch ex As ComponentModel.Win32Exception When ex.ErrorCode = &H80004005` around
an elevating `Process.Start`, inside a `For i = 1 To 10` retry -- so *every*
elevation failure was read as a refusal and re-prompted, up to ten UAC dialogs for
a cause that was never the user. Verified against MS Learn 2026-08-16. `[STABLE]`

**`Console.ReadKey()` with stdin redirected throws immediately; it does not
hang.** ***Corrected 2026-08-18 (previously "`Console.ReadKey()` inside a `catch`
in a non-interactive process hangs forever, with no output -- there is no console
input to read, and nothing times out. It presents exactly as 'the server is
stuck'", carried as `[STABLE]` and never run.)*** Measured 2026-08-18 on .NET 10,
stdin redirected -- which is BrowserAI's own configuration under an MCP client:
the call threw `System.InvalidOperationException`, *"Cannot read keys when either
application does not have a console or when console input has been redirected.
Try Console.Read."*, with no delay. **The rule that a `catch` must not call
console input survives, and the real failure is worse in a different way**: a
throw inside a `catch` replaces the original exception with a new one, so the
cause is not merely delayed, it is destroyed. **Both arms are now measured, and they do opposite things -- the
console-attached one added 2026-09-23 on .NET 10 (SDK 10.0.401), Windows
10.0.26200.9457 (previously "the *console-attached-but-nobody-typing* arm
... was **not** run and is not established").** With a real console attached
and nobody typing, `Console.ReadKey(true)` **blocked for 45 s and was still
blocked when it was killed** -- `IsInputRedirected=False`, no return, no
throw, no output, nothing timing out: exactly *"the server is stuck"*, and
exactly what the original sentence described. With stdin redirected, which
is BrowserAI's own configuration under an MCP client, the same binary threw
`InvalidOperationException` **3 ms** in. **So the rule that a `catch` must
not call console input is load-bearing in both configurations and for two
different reasons**: redirected, the throw destroys the original exception;
console-attached, the process stops for good. `[STABLE]` for the APIs,
`[MACHINE]` for the 45 s, which is a floor and not a measurement of the
wait. Shipped instance, read
2026-08-16 in an unpublished C# directory-cleanup tool that runs as a scheduled
non-interactive job -- two calls, both inside `catch` blocks; that read
established that the calls exist, never what they do. The shape: both calls sit in the
*unknown-exception* arm, below the specific `UnauthorizedAccessException` and
`DirectoryNotFoundException` handlers, so they fire only on the cases nobody
anticipated: the population least likely to have been exercised in testing and
most likely to be hit in the field. `[STABLE]`

**`Process.ExitCode` throws after `Dispose()`, and
`Process.GetProcessById(pid).ExitCode` always throws.** .NET is *worse* here than
PowerShell, which merely returns `$null`. Cache the value as an `int` the moment
the child exits. `[STABLE]`

> **An uncached exit code does not fail -- it reads back empty, and a hard startup
> failure then logs identically to a clean shutdown.** Observed over **five days**
> in the PowerShell launcher this project replaces: the process handle was not
> captured before `WaitForExit`, so `.ExitCode` read `$null`, and the supervisor's
> "child finished" record was byte-identical whether the child had served a
> session or died on its first line. What it hid was total: a CLI flag deleted
> upstream (`--output-mode`, removed in `@playwright/mcp` 0.0.79) made the child
> print `error: unknown option` and exit **1**, with **all four supervised servers
> dead**, and no signal anywhere said so. Recorded 2026-08-13; `[MACHINE]` as an
> observation, `[STABLE]` as a mechanism -- the null read follows from the handle
> being gone, on every Windows and every PowerShell. Re-establish by calling
> `WaitForExit` on a `Process` whose handle has been released and reading
> `.ExitCode`. **This is the entry behind the rule that a child's exit code is
> cached as an `int` the moment it is available**, and behind treating "the log
> looks the same either way" as a defect and not as tidiness.

**`WaitForExit(int)` does not drain the async readers** -- only `WaitForExit()`
and `WaitForExitAsync(ct)` do, so the timeout overload truncates stderr.
`[STABLE]`

**Redirecting a child's streams does not stop a *grandchild* inheriting the
stderr pipe, and the parent then blocks until the grandchild exits.** Measured
2026-08-12/13 on the PowerShell launcher this project replaces: a detached
installer started through `Start-Process` with redirection configured inherited
the same stderr pipe handle, so the supervisor reading that pipe never saw EOF
and **every spawn cost 11.71 s -- the entire browser download -- falling to 0.37 s
once the inheritance was cut.** The pipe stays open because a handle to its write
end is still held, not because anything is still writing; the process that was
redirected has already exited. `[MACHINE]` for the two figures, `[STABLE]` for
the mechanism, which is ordinary Windows handle inheritance. Re-establish by
timing a spawn whose child starts a long-running grandchild, with the parent
reading stderr to EOF, against the same spawn with inheritance suppressed. **The
general form is worth more than the numbers: a redirected stream is drained when
the last holder of its write end closes it, which is not the same event as the
child exiting** -- so a supervisor that waits on EOF is waiting on the whole
process tree unless it prevents the handle travelling.

**stderr survives the child.** The anonymous pipe exists before `CreateProcess`
and the kernel buffers it: **5 lines survived a 3 s delay *and* child exit**. The
real risk runs the other way -- a full pipe blocks the child. `[STABLE]` for the
mechanism; **the charter does not date the measurement**, so the date is
`[UNVERIFIED]`.

**stdin EOF fires instantly when the parent holding the pipe is
`TerminateProcess`d**, which is what makes EOF a usable backstop for reaping
instances. Measured; **undated in the charter**. `[STABLE]`

**`ProcessStartInfo.Environment` is pre-populated with the inherited block and
assignment *merges*** -- an allowlist requires `Clear()` first. **`WorkingDirectory`
left unset passes `null` to `CreateProcess`**, so the child inherits the parent's
cwd, whatever the MCP client happened to have. **`ArgumentList` and `Arguments`
are mutually exclusive**; setting both is undefined behaviour. `[STABLE]`

**`Process.ExitCode` throwing after `Dispose()` is now reproduced, not
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
asserts none of them reach the child. Written the other way round -- assert only
that the forced variables are present -- it would pass against a transport that
never called `Clear()`, on any machine that happened not to have them set.
`[STABLE]`

### Redirecting a child's streams does not suppress its console window -- measured 2026-08-23

**`CreateNoWindow` is the only thing that does, and whether its absence is
*visible* depends on the parent, which is why it hides.** Measured 2026-08-23 on
Windows 11 Pro 26200 with .NET 10, launching `pwsh` through
`ProcessStartInfo` with `UseShellExecute = false` and both output streams
redirected on every arm.

| Parent | `CreateNoWindow` | What the child got | New visible top-level windows |
|---|---|---|---|
| **has a console** | `false` | **joined the parent's** -- its console process list went 3 → 4 | 0 |
| **has a console** | `true` | a private console, process list **1** | 0 |
| **no console** (`FreeConsole` first) | `false` | **a new console** | **2** |
| **no console** (`FreeConsole` first) | `true` | a private console | 0 |

The two windows are `CASCADIA_HOSTING_WINDOW_CLASS` titled with the child's
image path, and a `PseudoConsoleWindow`. **The class name matters more than it
looks:** on Windows 11 the default console host is Windows Terminal, so a sweep
for the classic `ConsoleWindowClass` finds **nothing** and reports a clean
screen while two windows are on it. The figures above come from a diff of every
visible top-level window before and during the launch, for that reason.

**What this explains.** A suite run from a terminal never shows the defect,
because the child joins the terminal's own console; the identical run started by
a *windowless* parent -- an agent harness, a scheduled task, a service -- flashes
a terminal per launch. Two of this repository's ten launch sites had omitted the
flag and it was found twice by a human noticing the flicker, never by a run.
`HouseRuleTests.EveryProcessLaunchInTheTreeSuppressesTheConsoleWindow` is the
mechanism now. `[FLOATS]` -- it rests on Windows' default console host, which
changed once already.

**To re-establish it:** from a parent that has called `FreeConsole`, start any
console-subsystem child twice with the flag set and unset, and diff
`EnumWindows` over visible top-level windows around each launch. Read the class
names instead of filtering for one, or the measurement answers zero both times.

### A read parked on standard input is woken by neither cancelling it nor disposing the stream -- measured 2026-09-15

**Measured 2026-09-15 on Windows 11 Pro 26200 with .NET 10**, against
`Console.OpenStandardInput()` behind a `System.IO.Pipelines.PipeReader`, with the
read allowed to park for 1.5 s first. Probe:
[`docs/probes/2026-09-15-consoleprobe/`](../../docs/probes/2026-09-15-consoleprobe/README.md);
output: [`docs/evidence/2026-09-15-fix/consoleprobe/`](../../docs/evidence/2026-09-15-fix/README.md).
`[FLOATS]`

| stdin | `GetConsoleMode` succeeds | `cts.Cancel()` completed it within 3 s | `stream.Dispose()` completed it within 3 s |
|---|---|---|---|
| a windowless console | **yes** | **no** -- still `WaitingForActivation` | **no** -- still `WaitingForActivation` |
| a redirected pipe nobody writes to | no | **no** | **no** |

**The stream type is the same either way** --
`System.ConsolePal+WindowsConsoleStream`, for a console and for a pipe -- and so
is the answer. What differs between the two is only whether end-of-file ever
arrives: a client closes its end of a pipe, and nothing ever closes a console.

**Three things follow, and the third is the one that cost a release.**

- **Cancellation cannot reach it.** `Stream`'s base `ReadAsync` checks the token
  once and then queues a blocking `Read` to the thread pool, so once the syscall
  is entered the token is a value nobody reads again.
- **Disposing the stream does not close the handle.** `WindowsConsoleStream`'s
  disposal drops its copy of the handle and leaves the console input handle open,
  which is correct -- it is a standard handle it does not own -- and means the
  parked `ReadFile` has nothing to fail against.
- **So anything that `await`s that read has made its own completion conditional
  on the peer.** BrowserAI's caller-facing transport did exactly that in v1.0.0:
  `DisposeAsync` closed its own end and then awaited the read loop, which is
  sound for the child leg (this process owns the pipe and the child's exit closes
  it) and unreachable for the caller leg. A post-install start has a console
  nobody writes to, so the process stood for 215 s holding a browser server.

**Abandoning the read is safe, and that is the other half of the measurement.**
The parked read sits on a thread-pool thread, which is a background thread: the
same probe returned from `Main` without awaiting it and **the process exited
anyway**, on both arms. So a teardown that gives up on such a read leaks a pooled
buffer and no process lifetime.

**To re-establish it:** park a `PipeReader.ReadAsync` on
`Console.OpenStandardInput()`, wait for the read to be genuinely in flight, then
cancel the token and dispose the stream in turn, reporting the task's status
after each with a bound. Run it twice -- once with stdin inherited from a
`cmd.exe` started with `CreateNoWindow` (a console with no window), once with
stdin redirected to a pipe nothing writes to -- because a measurement taken only
on the console arm cannot tell a console-specific behaviour from a general one.
Write the report to a file and not to stdout: on the console arm there is
nowhere for stdout to go that anybody will read.

### `SW_SHOWNOACTIVATE` keeps a headed Chromium off the foreground, and Firefox never takes it -- measured 2026-08-24

> ⚠️ *Corrected 2026-10-03 @ Chrome for Testing 155.0.8059.12 (`chromium-1247`)
> and Firefox 156.0 (`firefox-1553`), by addition (previously, in the table
> below, "**Firefox** -- `firefox-1539` | did not | **did not either**"). The
> heading stays as it was because other records link to it.* **Firefox 1553 TAKES
> the foreground, with the flag and without it. Chromium 1247 still takes it only
> without.** Measured in the condition this entry asks for:
> `SPI_GETFOREGROUNDLOCKTIMEOUT` read as 2,147,483,647 ms, and the foreground
> window, confirmed by `GetForegroundWindow` and its pid, owned by VS Code, an
> ancestor of the launching PowerShell. Each browser was started through
> `CreateProcessW` with a hand-built `STARTUPINFOW`, and the foreground and the
> launched tree's visible windows were read about every 280 ms for 5 s.
>
> | Browser | With `STARTF_USESHOWWINDOW` + `SW_SHOWNOACTIVATE` | Without it |
> |---|---|---|
> | **Chromium** -- `chromium-1247` | **did NOT take the foreground**, while its own *about:blank* window was visible from 0.9 s | **TOOK the foreground** at 0.6 s |
> | **Firefox** -- `firefox-1553` | **TOOK the foreground** at 0.8 s: an untitled `MozillaDialogClass` window, then the *Nightly* window | **TOOK the foreground** at 0.8 s, the same two windows |
>
> A second run of the Firefox arm with the flag took the foreground again, at
> 1.1 s. **The Chromium flag arm now carries the visible-window control this entry
> says it lacked**, so its *did not take the foreground* is told apart from
> *showed nothing*. A first run the same day, taken while a window of an unrelated
> process held the foreground, gave the same four answers, which the reasoning
> below says it should not have; why the lock let those windows through there is
> not established. Each arm put a headed browser on the maintainer's screen for
> about five seconds, with his approval for this measurement, and the logs say
> what appeared and when: [the batch](../../docs/evidence/2026-10-03-reverify-0.0.83/README.md).

**`CREATE_NO_WINDOW` and the show-window flag answer two different questions, and
this is the one about a GUI child.** The entry above is about a *console* child's
window; this one is about a browser's first real window, which `CREATE_NO_WINDOW`
says nothing about. Measured 2026-08-24 on the reference machine, driving
`CreateProcessW` directly with a hand-built `STARTUPINFOW` instead of through the
product, against the provisioned browsers. The foreground was read with
`GetForegroundWindow` and attributed with `GetWindowThreadProcessId`, polled every
250 ms.

| Browser | With `STARTF_USESHOWWINDOW` + `SW_SHOWNOACTIVATE` | Without it |
|---|---|---|
| **Chromium** -- `chromium-1237`, `chrome-win64\chrome.exe` | **did NOT take the foreground** | **TOOK the foreground** |
| **Firefox** -- `firefox-1539` | did not | **did not either** |

`[FLOATS]` -- each row is a property of the browser build Playwright pins, and
Chromium's is the half a revision could move.

⚠️ **The Chromium row rests on exactly ONE discriminating trial, and the condition
it needed matters far more than the count.** Three further trials each way
returned *no steal* on **both** arms and therefore discriminate nothing. The
reason is machine configuration, not the browser:
[`SPI_GETFOREGROUNDLOCKTIMEOUT` on this machine is 2,147,483,647 ms](detection.md#this-machines-foreground-lock-is-effectively-infinite-so-it-cannot-see-a-focus-steal----measured-2026-08-24)
-- about 24.8 days -- so Windows refuses a foreground change in the general case and
both arms look identical. The one trial that separated them did so through the
lock's own exception: the foreground window belonged to **VS Code, an ancestor of
the launching process**, so the child inherited the right to take the foreground.
**Anyone re-running this has to arrange that condition.** With an unrelated
window in the foreground it reproduces the null on both arms and reads as *the
flag does nothing*, which is the wrong conclusion and an easy one.

**Firefox's row is a real negative and not a void trial**, and the positive
control is what makes it one: a **visible top-level window belonging to the
launched process appeared at 0.25 s and 0.75 s** in the two arms --
`EnumWindows` filtered to that process with `IsWindowVisible` -- while the
foreground stayed where it was, in the *same* discriminating condition as the
Chromium trial. So Firefox shows a window on launch and does not take the
foreground either way, and the flag changes nothing for it.

⚠️ **What the Chromium row does NOT establish, said here instead of left to be
assumed:** no visible-window control is recorded for it. Its without-flag arm
proves a window appears at all, and on the with-flag arm *did not take the
foreground* is not separated from *showed nothing this time*. Whoever repeats
this should carry the Firefox arm's control across to it.

**To re-establish it:** read `SPI_GETFOREGROUNDLOCKTIMEOUT` first and write the
value down -- it is what tells a null trial from a negative result afterwards.
Then put a window owned by an **ancestor of the launching process** in the
foreground (VS Code, when the launcher is started from its terminal) and confirm
that with `GetForegroundWindow` + `GetWindowThreadProcessId` and not by eye.
Launch the browser twice through `CreateProcessW` with a hand-built
`STARTUPINFOW` -- `STARTF_USESHOWWINDOW` with `SW_SHOWNOACTIVATE`, then neither --
polling the foreground every 250 ms for a few seconds and attributing each
foreground window to a pid. Keep the visible-window control on **every** arm, or
a browser that showed nothing is indistinguishable from one that behaved.

### Exit code 1 is not a crash -- what each way of ending a process leaves behind -- measured 2026-08-29

**Nothing on this machine crashes with exit code 1. A `1` means somebody called
`TerminateProcess(handle, 1)`.** Measured 2026-08-29, Windows 11 26200, every arm
run against a real victim process and read back with `GetExitCodeProcess`. It
exists because a browser that exited 1 was chased for eleven days as a crash
([question 8](../../QUESTIONS.md)), and the one measurement that would have
retired that reading in an afternoon is this table. `[STABLE]` for the mechanisms,
which are Windows' and .NET's own; `[MACHINE]` for nothing here.

| How the process ended | Exit code |
|---|---|
| `TerminateProcess(handle, 1)` | **1** |
| `taskkill /F /PID <pid>` | **1** |
| `Stop-Process -Force`, which is .NET's `Process.Kill()` | **−1** (`0xFFFFFFFF`) |
| the last handle to a `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` job closing, 3 of 3 | **0** |
| `taskkill` without `/F` against a console process with no window | **ignored** -- it posts `WM_CLOSE` and the process runs on |
| a Chromium `CHECK` crash, crashpad connected and a 2,553,376-byte minidump written | `0x80000003` |
| a Chromium killed by desktop-heap exhaustion, 37 deaths over two rigs | `0x80000003` or `0xE0000008` |

> **The two rows that carry the weight are the two 1s and the job row.**
> `taskkill /F` and a hand-rolled `TerminateProcess(h, 1)` are
> **indistinguishable by exit code**, so a 1 says *ended from outside* and says
> nothing whatever about by whom. *Corrected 2026-09-17 (previously "Since
> 2026-08-29 one of the two `TerminateProcess` callers on this machine names
> itself")* -- **there are two callers on this machine and BOTH name themselves**,
> which is what turned an eleven-day hunt into one grep. The suite's own
> spawn-record reclaim writes a `WARN` under
> `BrowserAI.Tests.SpawnRecordReclaim`; **the product's own stray sweep writes
> one under `BrowserAI.Sweep`**, and `StrayCandidate.TryTerminate` is where its
> `TerminateProcess(handle, 1)` lives. See [QUESTIONS.md](../../QUESTIONS.md)
> §8a. That narrows the reading; it does not change the table, which is about
> Windows. And a process taken down by kill-on-job-close
> exits **0** -- it is indistinguishable from a clean shutdown, which is the
> opposite trap: a survivor check that reads exit codes cannot tell containment
> from a graceful exit. `[STABLE]`
>
> ✅ **THE WILD EXIT 1 OF 2026-09-17 IS ATTRIBUTED, AND THE 2026-08-29 READING
> OF IT WAS WRONG IN BOTH DIRECTIONS** -- added 2026-09-17. That day's ledger
> concluded *"exit code 1 = external `TerminateProcess(handle,1)` ... via the
> HARNESS OWN spawn-record reclaim"* and ruled the **product** sweep out *"BY
> DESIGN (attribution needs the window this browser never published) -- read not
> run"*. The recurrence on 2026-09-17 was read out of the machine-wide process
> log, not reasoned about, and it was the **product sweep**:
>
> ```
> 2026-09-17T12:02:54.5368021Z  made=2026-09-17T12:02:54.5367549Z  WARN   pid=80428@134341201739882745  BrowserAI.Sweep[5]  Terminated a stray browser: pid=90216 image=C:\Users\jori\AppData\Local\BrowserAI\browsers\chromium-1244\chrome-win64\chrome.exe session=C:\Source\SixFive7\BrowserAI\.work\test-scratch\sweep-real-browser-29e865154e604cc18ac7a4f7e16f6421\real. Its session directory was unlocked, so nothing owned it.
> ```
>
> **The reclaim is excluded by its own announcements, not by argument.** The same
> run's reclaim pass announced exactly three terminations, at
> `12:02:43.7558029Z`, `12:02:43.9795544Z` and `12:02:44.3334638Z` under owner
> `77360`, and its subjects were `2072`, `87348` and `90064`. The browser that
> died was `90216`, and it had not been started yet: its own `--log-file` opens
> at `12:02:54.411Z`, **ten seconds after** the pass finished, and
> `ScratchRoot.EnsureReclaimed` runs once per process.
>
> **The 2026-08-29 exclusion was backwards.** *Attribution needs a window* is
> exactly why the sweep killed it: with no `Chrome_MessageWindow` there is no pid
> to tie to a profile, so the sweep fell back to the session directory the
> browser's command line names -- and found it **unlocked**, which is its
> definition of a stray. The killer was a second **product** `BrowserAI.Server.exe`
> (pid `80428`) started by a different arm of the same suite run, sweeping at
> startup **25 ms** after it logged its own child launch and **84 ms** after the
> browser's last log line. Nothing about the desktop heap, and nothing about the
> harness.
>
> **What this says about the table:** a `1` still means *ended from outside* and
> still names nobody, and on this machine both callers announce themselves -- so
> the honest procedure for a wild `1` is to grep the process log for the window,
> under both `BrowserAI.Sweep` and `BrowserAI.Tests.SpawnRecordReclaim`, before
> reasoning about anything. `[STABLE]`
>
> **A handled crash and an unhandled one leave the same exit code.** `chrome.exe
> --headless=new ... --crash-test` writes a real dump into the profile's
> `Crashpad\reports` *and* exits `0x80000003`, so the presence of a crash handler
> changes what is recorded and not what the parent reads. A healthy launch starts
> **two** `--type=crashpad-handler` children and creates
> `Crashpad\{reports, attachments, settings.dat}`; every one of the nine
> desktop-heap deaths measured the same day wrote exactly one dump, so the
> handler was connected in all of them and the code was `0x80000003` anyway.
>
> **To re-establish**, for each row: start a victim that will not exit on its own
> (`pwsh -NoProfile -NonInteractive -Command "Start-Sleep -Seconds 90"`), keep the
> process **handle** from `CreateProcessW` so the exit code is readable after
> death -- `Process.ExitCode` throws once the object is disposed -- end it the row's
> way, `WaitForSingleObject`, then `GetExitCodeProcess`. For the job row, create
> the job with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, `AssignProcessToJobObject`
> the victim, then `CloseHandle` the job. For the Chromium rows use the
> provisioned `chrome.exe` with the saturation entry's command line and add
> `--crash-test` for the crash arm. **Never by image name**, on any of them.

### A windowless program started with no standard handles reads end of input at once -- measured 2026-10-04

`[FLOATS]` on .NET for what the runtime makes of a missing handle, the windowless stub
of [the windowless server entry](../mcp/protocol.md) built with SDK 10.0.401 and
ILCompiler 10.0.12; `[STABLE]` for what Windows hands such a start. Windows 11 Pro
10.0.26300. Everything it was read from:
[`docs/evidence/2026-10-04-onebinary-measure`](../../docs/evidence/2026-10-04-onebinary-measure/README.md),
`runs/nohandles/`.

**A start with no standard handles is how the Task Scheduler, an installer and a
double-click start a program, and a windowless MCP server started that way stops by
itself.** The stub was launched 6 times with no standard handles: 3 times with the
three handles NULL, 3 times with no `STARTF_USESTDHANDLES` at all. `GetStdHandle`
returned NULL every time, .NET returned `Stream.Null` without a word, the MCP server
read end of input at once, and the process exited by itself in 52 to 78 ms, 6 of 6.
Every client gave its server a pipe on stdin, in all 54 runs of the same day, so a
pipe on stdin is what tells a client's start from the others, and an argument is what
says which was meant. ⚠️ **A check of the shape "no client, and stdin is a console"
can never fire in a windowless start**, because stdin there is NULL and not a console;
BrowserAI's server made that check for a person who double-clicked it.

**The feedback cursor.** Microsoft documents that "If a GUI process is being started
and neither STARTF_FORCEONFEEDBACK or STARTF_FORCEOFFFEEDBACK is specified, the
process feedback cursor is used", and that "The system turns the feedback cursor off
after the first call to GetMessage"
([STARTUPINFOW](https://learn.microsoft.com/windows/win32/api/processthreadsapi/ns-processthreadsapi-startupinfow),
read 2026-10-08). Neither client passes either flag. No busy cursor was seen in any of
the 27 windowless client runs, the pointer over the arrow shape in 23 of them; in a
separate launcher the forced flag showed it 4 of 4 times, and the clients' flags 0 of
7 times while the stub ran. A person was using the machine, so this is *not
observed*, never *absent*. One `GetMessage` call at start would rule it out, as
documented.

**Re-establish it** with the batch's rig, `rig/ExitRig/Launch.cs.txt` and
`Feedback.cs.txt`, starting the windowless stub with NULL handles and with no handle
flag, and reading the stub's own report of its handles and its exit.

## Files, durable writes and deletes

**`Directory.GetFiles` is top-level only, and a recursive enumeration aborts on
the first `UnauthorizedAccessException` instead of skipping the node.** MS Learn,
on the `AllDirectories` overloads: *"`UnauthorizedAccessException` errors may make
the enumeration incomplete. You can catch these exceptions by first enumerating
directories and then enumerating files."* The failure is silent in the worst way --
a partially-walked tree is indistinguishable from a fully-walked smaller one. A
recursive delete therefore needs a hand-rolled **post-order** walk with
per-node exception discrimination: deepest child first, so a non-recursive
`Directory.Delete` always sees an empty directory. Reference implementation, read
2026-08-16 in that same unpublished cleanup tool -- **the shape is recorded here
and not the path, because a path nobody else can open is not a
re-establishment route** -- recurse subdirectories, then yield files, then yield
the directory itself, with
`UnauthorizedAccessException` and `DirectoryNotFoundException` caught and logged
**per node**, and an optional ACL-reset retry on the denied node. Directly relevant
to `browserai_reinstall_browser`, session destroy, and the per-run instance
directory. Verified against MS Learn 2026-08-16. `[STABLE]`

**`Directory.Delete(path, recursive: true)` does make partial progress -- it
deletes what it can and throws ONE exception naming ONE node.** Measured twice
2026-08-16 on **.NET 10.0.11**, Windows 11 Pro 26200, from PowerShell against
`[System.IO.Directory]::Delete($p, $true)`. Two trees, identical layout. With one
file held `FileShare.None`: threw `IOException` naming that file, and survivors
were exactly that file and the two directories above it -- the top-level JSON file
and a sibling subdirectory that sorts *after* the held one were both gone. With a
subdirectory the caller may not read (`icacls /deny (OI)(CI)(RX,DE,DC)`): threw
`UnauthorizedAccessException` naming that directory, and again everything else
went. **A hand-rolled post-order walk left the same nodes behind in both cases** --
so the on-disk outcome is not what separates the two primitives. What separates
them is the report: the framework named one node where the per-node walk named
**four** and **two**. The enumeration entry above is unaffected --
`EnumerateFileSystemEntries(..., AllDirectories)` did throw
`UnauthorizedAccessException` and yielded nothing, re-measured in the same pass.
To re-establish: build a tree of a top-level file, a subdirectory holding two
files, and a second subdirectory sorting after it; make one node undeletable
(`[System.IO.File]::Open(..., 'None')` for the held-file arm, `icacls /deny` for
the unreadable arm); call `[System.IO.Directory]::Delete($root, $true)`, catch,
and list what survived. **The second subdirectory is the load-bearing part of the
fixture** -- it is what shows the walk continued past the failure, and a tree with
only the locked node cannot tell partial progress from none.
`[FLOATS]` (a BCL implementation detail, and the SDK rolls forward)

**Windows refuses to remove a directory that is a live process's current
directory -- and does not refuse to delete the files inside it.** Measured twice
2026-08-16, same runtime, against a childless holder started with
`-WorkingDirectory`. `Directory.Delete(path, recursive: true)` **emptied the
directory completely** and only then threw `IOException` on the node itself; what
survived was an empty directory. `Directory.Move(path, aside)` was refused with
`IOException` **and the contents untouched**, and succeeded the moment the holder
exited. So a working-directory lock is a liveness signal for a *rename* and not
for a *delete*, and BrowserAI's instance sweep claims by renaming
([`TreeDelete`](../../src/BrowserAI.Core/Runtime/TreeDelete.cs)). ⚠️ **The first
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
basis."* `Flush()` and `FlushAsync` do **not** close the gap -- `FlushAsync`'s own
remarks say it *"flushes the .NET stream buffers to the file, but does not flush
intermediate file buffers in the operating system."* Surviving a power cut needs
`FileStream.Flush(flushToDisk: true)`, which reaches `FlushFileBuffers`, or
`FileOptions.WriteThrough` / `FILE_FLAG_WRITE_THROUGH` set at open time, and then
an atomic `File.Move` into place so no reader ever observes a half-written file.
Verified against MS Learn 2026-08-16. `[STABLE]`

**A working reference implementation was read, not designed**, verified
2026-08-16 in an unpublished first-party C# test rig: its `WriteAllTextDurable`
does all three steps -- a temp file **in the same
directory**, opened `FileShare.None` with `FileOptions.WriteThrough`, then
`stream.Flush(flushToDisk: true)`, then `File.Move(temp, full, overwrite: true)` --
with the reasoning recorded inline at lines 229-247 and a `finally` that removes
the temp on every exit path. Two details to take:

- **`File.Move(overwrite: true)`, not `File.Replace`.** `Replace` **requires the
  destination to already exist**, and the first write of a lock file or a crash
  marker is exactly the case where it does not. `Move` maps to `MoveFileEx` with
  `MOVEFILE_REPLACE_EXISTING`, which covers both. `[STABLE]`
- **The temp file must be in the target's own directory** -- a rename is only
  atomic within one volume, and only cheap within one directory. The rename is
  also retried (5 attempts, escalating sleep) against `IOException` /
  `UnauthorizedAccessException`, because something holding the destination open is
  a live condition, not a bug.

> **Provenance note, 2026-08-16 -- first recorded as a missing file, and that was
> wrong.** This entry arrived citing `TestRig\rig-lock.ps1:212-245`, which is
> absent from `HEAD`, and it was written up here as a reference that did not
> exist. **It did exist.** `rig-lock.ps1` was added 2026-08-09 in `a5968c5a` at
> +833 lines and **deleted 2026-08-15** in `902082cb`, *"TestRig: the PowerShell
> rig is retired"* -- rewritten in C# at the path above, not fabricated and not
> lost. A `HEAD`-only search cannot distinguish "never existed" from "retired
> yesterday", and this entry asserted the first from evidence that only supported
> the second. **Search the history, not just the tree, before recording an absence
> as a finding.** *(A content grep for `Flush(true)` also missed the successor --
> the call is written `Flush(flushToDisk: true)`. A named argument defeats a
> literal grep, so a negative grep result is not an absence either.)*

**A durable write of this project's own `browserai.json` costs 16.1 ms and 18.2 ms.**
Measured 2026-08-16, two runs of **100 sequential rewrites** through the product's
path -- a temp file in the same directory opened `FileShare.None` with
`FileOptions.WriteThrough`, `Flush(flushToDisk: true)`, then
`File.Move(overwrite: true)` -- at 1.61 s and 1.82 s wall including process start.
The design recorded this cost as
unmeasured and said to measure it before ever trading the guarantee away; this is
that measurement, and at ~17 ms against a file written on `init`, on `resume` and
on a purpose change, there is nothing to trade. Reproduce with
`BrowserAI.TestProbe.exe session-rewrite <dir> <ready-file> 100 <done-file>` and
time it. `[MACHINE]`

**A rename cannot replace a file whose handle is open -- under *any* share mode --
and it fails `ERROR_ACCESS_DENIED` and not a sharing violation.** Measured
2026-08-16 across `FileShare.Read`, `FileShare.Read | FileShare.Delete` and
`FileShare.ReadWrite | FileShare.Delete`: all three refuse
`File.Move(source, target, overwrite: true)` with HRESULT `0x80070005`, because
`MoveFileEx` with `MOVEFILE_REPLACE_EXISTING` needs DELETE on the destination.
`[STABLE]` for the refusal; `[FLOATS]` for the .NET exception type, which is
**`UnauthorizedAccessException` and therefore *not* an `IOException`** -- a retry
written to catch `IOException` alone never retries the case that actually
happens. Reproduce:
`SessionLockTests.ARenameCannotReplaceALockFileWhoseOwnHandleIsStillOpen`, which
walks all three share modes on every run.

> **This is what forces close → rename → re-open under a mutex**, and it is
> stated because the obvious repair does not exist. [The session design](../../ARCHITECTURE.md#sessions)
> makes an open handle on `browserai.json` the lock, and [the locking design](../../ARCHITECTURE.md#locking-ownership-and-the-sweep)
> requires the record to arrive by atomic rename. The natural guess is that
> adding `FILE_SHARE_DELETE` to the lock handle reconciles them -- it does not;
> the rename is refused identically. So the handle has to be closed for the
> rename, and the only thing that makes the resulting gap unobservable is that
> every BrowserAI takes the per-directory mutex before create-or-take.

**The rename refuses *readers* too, for the same reason and in the same
direction: a file being replaced is DELETE-PENDING, and every new open of that
name is refused `ERROR_ACCESS_DENIED`.** Measured 2026-08-18 at
`SuiteParallelism.Unbounded`, twice in twenty-eight full-suite runs and at two
different call sites -- `File.Move(temp, browserai.json, overwrite: true)` refused on a
destination this process had just closed its own handle to, and
`new FileStream(browserai.json, FileMode.Open, FileAccess.Read, FileShare.ReadWrite |
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
> `LockFileException` and not this -- so one BrowserAI asking whether a session
> was locked, at the instant another rewrote its own lock, threw out of
> `TryAcquire`. `SessionIndex.FollowOne` did not throw and was worse: it caught
> the denial and reported a perfectly good entry as `EntryUnreadable`.
>
> **The distinction that makes the fix safe is who is entitled to the open.** A
> reader of a record, and a holder re-opening under the per-directory mutex, are
> entitled -- a denial is a window that closes, so they wait. `InstanceDirectory`'s
> claim, `LiveInstances`' registration and `FirefoxProfile`'s probe are **not** --
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
| the file's length | -- (it did not exist) |
| does the directory exist | True |
| is one of the writer's `browserai.json.new-<guid>` temps on disk | **1** |
| does an immediate re-read find a record | **True** |

**All five together are the finding, and no single one of them is.** The name was
genuinely unbound -- not a zero-length file, not a missing directory, which are
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
> *next* retry lands -- a gap governed by that loop's 5→100 ms backoff, which fits
> the observed width. `[UNVERIFIED]`: nothing has instrumented the writer and the
> reader on one timeline to confirm it. **The observation is not a hypothesis;
> the explanation is.**
>
> **The fix, if it is ever wanted, is POSIX-semantics rename.**
> `SetFileInformationByHandle` with `FileRenameInfoEx` and
> `FILE_RENAME_POSIX_SEMANTICS | FILE_RENAME_REPLACE_IF_EXISTS` (Windows 10 RS1
> and later) replaces a file that has open handles without refusing and without
> unlinking the name -- the old file simply becomes nameless and its handles stay
> valid. .NET exposes no overload for it, so it is a P/Invoke against the most
> safety-critical primitive in this product, and it needs its own measurement
> before it is trusted. Not taken; see the hazard index.

> ⚠️ **Corrected 2026-08-18 (previously "BrowserAI is safe under the absence
> today ... every ungated one fails in the safe direction -- the sweep's
> `SessionDirectoryFrom` ... and `ActOn` ...").** The judgement was right about the
> two readers it named and wrong as a claim about all of them, and the reason is
> stated here: it was written by checking the readers on the *sweep* path,
> which is where the danger was expected, and generalised to *every* ungated
> reader without enumerating them. An [adversarial
> review](../../docs/reviews/2026-08-18-adversarial-locking.md) enumerated all
> thirteen. **Eleven fail safe. Two acted on the absence, and both were closed
> on 2026-08-19 -- one each, neither in a pass with anything else in it:**
>
> - **`SessionIndex.Locate`** → a `null` record becomes `NotASession` →
>   `SessionIndexEntry.IsRemovable` → `Sweep` **deletes the index entry for a live
>   session**. The R7 re-check does not close it: it re-reads microseconds later,
>   inside the same window, because the window's width is governed by the
>   *writer's* 5→100 ms backoff. Nothing re-asserts the entry afterwards --
>   `Record` is called from `OpenAsync` only -- so the session stays invisible to
>   `browserai_list` and to `LiveSessions()` for the rest of its life. Nothing
>   downstream treats an index entry as authority, so the outcome is a wrong
>   *report* and not a wrong destructive action, which is what keeps it out of
>   the first class. ✅ **Closed 2026-08-19 by the discriminator this article
>   names below**: `SessionIndex.Absent` looks for
>   `SessionLayout.NewLockFilePattern` beside the gap and answers
>   `SessionIndexEntryState.RecordInFlight`, which is not removable. The coupling
>   the paragraph below worried about was avoided by putting the name in
>   `SessionLayout` instead of reaching for it from `RenameWindow`: one helper
>   composes it, both readers match it.
> - **`SessionManager.Existing`**, `init`'s existence guard → `null` means *"the
>   directory is free, proceed"*. The gated `TryAcquire` downstream stops it
>   becoming two owners; what gets through is a **stale-locked** directory being
>   rebound, because `Compose` takes `Mode` and `Browser` from the request. So
>   `init` can silently re-bind a closed session's browser family over a profile
>   on disk belonging to the other one -- the exact thing `resume` refuses
>   explicitly. ✅ **Closed 2026-08-19, and not the way the hazard row predicted.**
>   `SessionLockRequest.RefuseAnExistingRecord` makes `TakeOrReport` answer
>   `AlreadyASession` from the record it has already read under the gate;
>   `SessionManager.Existing` **stays** where it is, because it is what gives
>   `init` one answer for a lost session, a closed one and one this process holds
>   -- moving it would let the pre-gate probe answer first with a shorter sentence,
>   which is a regression the code records having already made once. The rebinding
>   needed the reclaim, and `init` no longer has a path to one.
>
> **What still holds, and is the reason the original judgement was nearly
> right.** A denial was an unhandled exception escaping `TryAcquire`; an absence
> is a documented return value every caller already handles -- `SessionLock` even
> carries the sentence *"which removed its `browserai.json` between the refusal and
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
> the writer's own temp file -- which is what the test uses -- but reaching for it
> from `RenameWindow` couples it to two different temp-naming conventions.
> ⚠️ **Corrected 2026-08-19 (previously this paragraph ended there, as the reason
> the gap was left).** Neither closure paid that cost: one asks a *directory
> listing* for a pattern `SessionLayout` owns, and the other asks nothing extra at
> all, because the record was already read under the gate. **The reasoning that
> made this look expensive was about waiting, and neither answer waits.**
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
*"could not be replaced after **3 attempts over 2.3 s**"* -- from a loop whose own
sleeps total **15 ms** across those three attempts. So 99.3% of that budget went
somewhere other than the retries: the process was not being scheduled. The file
was very likely free for most of it.

> **This is the shape to hunt in shipped code, and it had been raised once
> already for the same reason without the lesson being taken.** The budget had
> gone from *five attempts over 150 ms* to *two seconds* on 2026-08-16 after
> exhausting under full-suite load, and then exhausted again at higher
> parallelism -- because the number was chosen against how long the *contention*
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
process cannot access the file ... because it is being used by another process"*,
on a file no BrowserAI process had ever opened. The atomicity of the rename is
not in question -- what it guarantees is that a reader sees the old bytes or the
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
total elapsed time instead -- 2 s, backing off 5 ms doubling to a 100 ms cap --
and green across two full-suite runs. `[MACHINE]` for the numbers; the shape is
general.

> **It was invisible until it was made visible, which is the more useful half.**
> The rewriting probe's streams were drained and discarded by the test rig, so an
> exhausted budget presented as the *host* waiting out its own ninety-second
> patience and failing on a wholly unrelated assertion. Twice. The probe now
> catches, writes the failure into its report file, and exits non-zero, and the
> host asserts on that field -- after which the same run named the cause on the
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

> **What the writers do *not* do is what makes this cheap**: no
> mutex, no read-before-write, no compare. Every writer writes unconditionally,
> and the content is a pure function of the file's own name, so the winner of any
> race wrote exactly what every loser was about to. A "skip if already correct"
> fast path was deliberately left out -- after the first write nobody would
> contend, and the concurrency test would then prove nothing while still passing.

**A non-durable temp-and-rename write costs 1.7 ms alone and 9.2 ms under 8-way
contention** -- against ~17 ms for the durable `browserai.json` write above. Measured
2026-08-16, two runs each: **1.72 and 1.84 ms** per write with one writer,
**9.15 and 9.26 ms** with eight writers on one name. Each write is a temp file
created and written, `File.Move(overwrite: true)`, and the temp deleted in a
`finally` -- no `WriteThrough`, no `Flush(flushToDisk: true)`. So durability is
roughly a **10× multiplier on an uncontended small write**, and contention on a
single name is roughly **5×** on top of the base cost. That ratio is why
[the session index is written without either](../../ARCHITECTURE.md#locking-ownership-and-the-sweep)
while `browserai.json` keeps both: an index entry regenerates itself from the
directory it names, and a lock record cannot. `[MACHINE]`

**`Utf8JsonWriter`'s default encoder escapes `+`**, so every ISO 8601 timestamp
with a positive UTC offset is written with its sign as a `+` escape.
Measured 2026-08-16 while writing `browserai.json`: the file round-trips perfectly,
parses everywhere, and is unreadable by the person the file exists for.
`JavaScriptEncoder.UnsafeRelaxedJsonEscaping` -- the same encoder
[the server transport already takes](../mcp/sdk.md) -- removes it. **It was caught
only by an assertion on the literal bytes**; every assertion on the parsed value
passed, in both directions, which is exactly the shape of a defect that ships.
`[FLOATS]` -- the encoder's safe list belongs to `System.Text.Json`, which the SDK
floats. Reproduce:
`LockRecordTests.TimestampsAreWrittenAsIso8601WithAnExplicitOffset`.

**`LoggerFactory.Dispose()` never disposes a provider *instance* handed to
`AddProvider`.** Measured 2026-08-16 on `Microsoft.Extensions.Logging` 10.0.x, by
planting a provider that counts its own disposals:
`LoggerFactory.Create(b => b.AddProvider(instance))` followed by
`factory.Dispose()` reported **0 disposals** -- a DI container does not dispose an
instance it did not create. The consequence in this repository was a
`ProcessLog.Dispose()` that closed nothing: its rolling file handle survived, and
the log could not afterwards be opened with `FileShare.None`. It cost nothing in
`Main`, which exits immediately after; it was found the first time something
short-lived opened a process log and then read it back, which is a Velopack hook.
`SessionLogging` was already immune because it disposes its file **explicitly
after** the factory -- a second call that reads as redundancy and is the actual
mechanism. `[FLOATS]` on the logging packages. Reproduce:
`ProcessLogTests.DisposingTheProcessLogReleasesTheFileHandle`, which fails at the
exclusive open if the explicit `_writer.Dispose()` is removed.

**.NET's `FileMode.Append` loses records when several processes share a file;
`FILE_APPEND_DATA` does not.** Measured 2026-08-16 while building the process
log: **eight processes each writing 25 records lost 70 of the 200.** Every write
returned success and the file grew, so nothing anywhere reported it -- the lost
records were simply absent. The cause is that .NET's append mode seeks to the
end *at open* and then tracks the position itself, so two writers that opened at
the same length overwrite each other. `FileShare.ReadWrite` permits the sharing
and guarantees nothing about it.

One fix is the platform's own guarantee and not a lock: a handle opened via
`CreateFileW` with **`FILE_APPEND_DATA` and without `FILE_WRITE_DATA`** has its
writes placed at the end of the file by the filesystem, atomically, regardless
of how many other handles are open. **Requesting `GENERIC_WRITE` silently
forfeits it**, because `GENERIC_WRITE` expands to include `FILE_WRITE_DATA`. The
same eight-by-twenty-five run then loses nothing, repeated three times.
`[FLOATS]` for the .NET half, which could change with any SDK; `[STABLE]` for the
Win32 guarantee.

⚠️ **Corrected 2026-08-24 (previously "The fix is ..." and "a lock would have
worked while also making logging able to block -- the one thing the observability
design says the sink may never do"). BrowserAI does not use it any more.** The
measurement above is untouched and still forbids `FileMode.Append`; what was
wrong was treating the append guarantee as the whole answer, because **it is per
`WriteFile` call**. A completion loop above it -- reissue on a short write -- voids
it: the second call appends after whatever another process wrote in between, the
record is torn, and every call returns success. So the machinery was deleted and
the shared log is now written under a cross-process byte-range lock taken with
`LockFileEx`, blocking, with the length read, the instant stamped and the bytes
written inside one claim. **A lock can block and this one is bounded by
construction**: the hold is a single write of a few hundred bytes, and the kernel
releases a byte-range lock however the holder dies -- which is the same property
that made `MaintenanceLock` a file, not a semaphore.

### Locking a log file without locking its readers out -- 2026-08-24

**A Windows byte-range lock is enforced against `ReadFile` as well as
`WriteFile`, so the obvious spelling -- lock `[0, ∞)` -- makes every concurrent
reader of the file fail with `ERROR_LOCK_VIOLATION`.** For a log that is read
while it is being written, that trades one silent failure for a loud one. **The
region is therefore one byte at offset `long.MaxValue`**, past any offset a file
can have: locking beyond the end of file is explicitly legal, costs nothing, and
overlaps no byte anybody reads, so the lock is a pure semaphore.

Two declaration traps came with it, both build-time and both recorded here
because each looks like a defect in the code and not a rule of the toolchain:

- **`LibraryImport` refuses to marshal `System.Threading.NativeOverlapped`** --
  `error SYSLIB1051`, asking for `DisableRuntimeMarshallingAttribute` on the
  whole assembly to satisfy one parameter. `NativeFile` declares its own
  five-field `Overlapped` instead.
- **CsWin32 refuses to generate `OVERLAPPED` at all** -- `error PInvoke003: This
  API will not be generated. Use System.Threading.NativeOverlapped instead` -- so
  the [layout oracle](../../tests/BrowserAI.Tests/NativeMethods.txt) cannot
  supply it, and that one struct is checked against the framework's definition
  and not against the Win32 metadata. `[FLOATS]` for both, which are
  properties of the SDK and of the generator and not of Windows.

**And the delete half, which is the other direction of the same question.** A
handle opened **without** `FILE_SHARE_DELETE` refuses `File.Delete` on that path
with `ERROR_SHARING_VIOLATION`, which .NET surfaces as **`IOException`** --
measured 2026-08-24 against the live process log. That is *not* the same
exception as the rename refusal recorded above, which is `ERROR_ACCESS_DENIED`
and surfaces as `UnauthorizedAccessException`; a handler written for one does not
catch the other, and the two sit a page apart in the same design. `[STABLE]` for
the Windows refusal, and the .NET mapping floats with the framework exactly as
the rename's does. Reproduce:
`ProcessLogTests.TheSharedLogCannotBeUnlinkedWhileAWriterHoldsIt`, which asserts
the refusal **and** that a concurrent reader is still admitted -- the second half
being what a share mode narrow enough to fix the first would have broken.

### Reading a file somebody is still writing: the reader's own share mode is what refuses it -- measured 2026-08-30

**`File.ReadAllText` cannot read a file that has a live writer, and the writer's
permissiveness has nothing to do with it.** A share mode is a statement about
what *other* handles may do, and Windows checks it in **both** directions: the
opener's requested access must be permitted by every existing handle's share
mode, **and** the opener's own share mode must permit every access those handles
already hold. `File.ReadAllText` opens `FileAccess.Read, FileShare.Read`. A
writer holds `GENERIC_WRITE`. `FileShare.Read` does not include
`FILE_SHARE_WRITE`, so the second check fails and the open is refused with
`ERROR_SHARING_VIOLATION` -- *"the process cannot access the file ... because it is
being used by another process"* -- **even when the writer shared everything**.

That last clause is the trap, and it is spelled out because the natural
reading of a sharing violation is *the other process is being restrictive*.
Node's `fs.openSync` goes through libuv, which asks for
`FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE` indiscriminately and
says so in a comment -- deliberately, to match POSIX, so that a file can be read
and deleted while it is open. A .NET reader still loses to it. **The fix is
entirely on the reader**: `FileShare.ReadWrite | FileShare.Delete`, which permits
the writer to go on writing and permits whoever owns the tree to delete it out
from under the read.

⚠️ **And the length a directory enumeration reports for such a file can be
`0` while the file holds data.** `FileInfo.Length` is the size the enumeration
carried when it produced that `FileInfo` -- cached, never re-read -- and NTFS
updates a directory entry lazily while a handle is open. Measured 2026-08-30
through `DirectoryInfo.EnumerateFiles("*")`, a file holding **63** bytes written
and flushed behind a live writer's handle reported **0**; the same file queried
by its own exact name in a separate probe minutes earlier reported **63**. So
*which* enumeration shape hits the stale entry is **not established here**, and
nothing should rely on either outcome -- the only trustworthy size for a file
somebody is holding is `stream.Length` off a handle you opened yourself.

`[STABLE]` for both Windows behaviours. Reproduce with
`LauncherEvidenceTests.AFileALiveWriterIsHoldingComesBackReadableAndAtItsTrueLength`,
whose companion arm holds the other end -- a file opened `FileShare.None` by
anybody is unreadable to a reader sharing everything, which is the correct
answer and must stay one. The wider version, against a real `node` writer and not
a `FileStream`, is two files and four lines: hold the log open with
`fs.openSync(path, 'a')` and `fs.writeSync`, then try `File.ReadAllText` and the
share mode above from another process and compare.

**Where this cost something:** `LauncherWait.Evidence` inlines every file in the
launcher's scratch tree into a containment failure, because that tree is deleted
when the test unwinds. It read with `File.ReadAllText`. On the 2026-08-30
release gate's sixth run -- a Firefox stall the driver's `stderr` tee had been
armed for two hours earlier -- all three capture files came back
`(unreadable: ... used by another process)` with `(0 bytes)` beside each, and the
tree was then removed. **Every file such a dump is written to read is one
somebody is still holding, by construction**: it is taken at the moment a launch
did *not* happen.

### An unhandled exception DOES unwind, and `FailFast` and a stack overflow do not -- measured 2026-09-23

`[FLOATS]` .NET 10.0.401 CoreCLR, Windows 10.0.26200.

**An ordinary unhandled exception runs the `AppDomain.UnhandledException`
handler FIRST and then unwinds.** Out of `Main` and off a background thread
alike, the handler ran, and then every `finally` and every `Dispose` ran too.
**The control is the same code caught by an outer handler**, which ran all three.

⚠️ **What runs nothing at all is `Environment.FailFast` and a
`StackOverflowException`.** On both, the handler did not run, no `finally` ran and
no `Dispose` ran.

**So the property a crash log rests on is ORDERING, not the absence of
unwinding.** A `finally` cannot be relied on -- two of the three shapes skip it
entirely -- and the handler runs before anything that might. `ProcessLog` says so
in place, and `TaskDialogPage` already carried the neighbouring case, that the
runtime cannot unwind into native frames.

**Re-establish** with a `net10.0` console executable that registers the handler,
opens something disposable inside a `try`/`finally`, and then throws, calls
`FailFast` and recurses without a base case, one arm each. The rig is
`.work/assumed-2026-09-23/src/probes/`; the output is `probe-E-unwind.txt`.

### `CreateProcessW` on a `.cmd` succeeds and runs `cmd.exe` instead -- measured 2026-09-23

`[FLOATS]` Windows 10.0.26200, .NET 10.0.401.

**The launch does not fail. Windows supplies the interpreter and does not say
so.** `CreateProcessW` against a `.cmd` succeeded with `lpApplicationName` set and
with a command line alone, and in both arms **the process that ran was
`C:\WINDOWS\system32\cmd.exe`** -- the child's own `%CMDCMDLINE%` read
`C:\WINDOWS\system32\cmd.exe /c "..."`. MS Learn states the rule from the other
end: *"To run a batch file, you must start the command interpreter; set
lpApplicationName to cmd.exe and set lpCommandLine to ... /c plus the name of the
batch file."*

⚠️ **And the shell costs exactly what a byte-for-byte argument
contract cannot afford**, measured in the same run: through the shim a literal
`%USERNAME%` arrived **expanded**, and `a & b` and a path containing a space were
re-quoted into one mangled argument with *"The filename, directory name, or volume
label syntax is incorrect."* on stderr. The control is the identical
`ProcessStartInfo.ArgumentList` straight to a real `.exe`, which delivered all
three byte-for-byte.

**Why it matters here:** a registered client path is exactly the kind of argument
that carries spaces, which is why the client is started as an `.exe` found by its
file name and never through a shim: by BrowserAI's own code until 2026-10-03, and by
RegisterAI since *(corrected 2026-10-03, previously "which is why
`McpClientRegistration` registers an `.exe` and never a shim")*.

**Re-establish** with a `.cmd` that echoes `%CMDCMDLINE%` and its arguments, one
arm through `CreateProcessW` and one through a real executable, with the same
argument list. The rig is `.work/assumed-2026-09-23/src/probes/`; the output is
`probe-F-cmdshim.txt`.

**A record on stderr is not durable, and a record in the process log is -- the
two diagnostic channels differ and only the file's guarantee is written down.**
Measured 2026-08-18 on two consecutive CI runs. `ProcessLog` wires stderr through
`AddConsole`; `RollingFileWriter` is one unbuffered `WriteFile` per record against
a `FILE_APPEND_DATA` handle. A process ended with `TerminateProcess` therefore
keeps everything the file sink wrote and **loses whatever the console queue still
held** -- and the two runs lost *different* amounts of the tail, which is the
signature of a queue and not of a call that never happened.

**That the console provider queues at all is first-party documented, not
inferred**, which matters because the observation above would otherwise have only
an explanation nobody had checked. `ConsoleLoggerOptions.QueueFullMode` exists,
its default is `Wait`, and `ConsoleLoggerQueueFullMode.Wait` is defined as
*"Blocks the logging threads once the queue limit is reached"* -- a queue that can
fill, and that blocks *the logging threads* and not the writing one, is
drained by something else. Verified against MS Learn 2026-08-18. The same page
gives the other half of the shape: the full mode is `Wait`, **not** `DropWrite`,
so records are not discarded under pressure and process death is the only way
this loses one.

*Moved 2026-09-23 from the desktop-heap section, where it was filed by accident and had no honest anchor: nothing in it is about desktop heap, and two comments in `src/` cite this section for exactly this claim.*

## Saturation: the 100-process design point

**80 concurrent headless Chromium instances start cleanly on this machine, and
that is a negative result about a failure we were chasing.** Measured
2026-08-18, sweeping N = 5, 10, 20, 40, 60, 80 real
`chrome.exe --headless=new` launches, each with its own `--user-data-dir` and
`about:blank` -- the exact command line
`StraySweepTests.TheSweeperFindsARealBrowserItLaunchedItselfInTheInteractiveSession`
uses -- held for 20 s and then counted:

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
> machine, so every browser had CPU. The suite starves CPU, not memory or
> handles, and that remains the open candidate -- along with **desktop heap**,
> which no documented API reports and which none of the columns above would show.
> Recorded here as a bounded negative result, not a diagnosis. `[MACHINE]`
>
> ⚠️ ***Corrected 2026-08-27 (previously the sentence ended at "none of the
> columns above would show")*** -- desktop heap was run deliberately on that date
> and it kills a browser exactly this way; the next section carries the
> measurement. *"No documented API reports it"* was half wrong: `UOI_HEAPSIZE`
> reports the **size**, and it is the **usage** nothing reports.
>
> **To re-establish**, for each N: give every instance its own `--user-data-dir`
> under a scratch root and start
> `chrome.exe --headless=new --user-data-dir=<own> --no-first-run
> --no-default-browser-check --disable-component-update --enable-logging
> --log-file=<own> --v=1 about:blank`; let them settle 20 s; count how many have
> exited and read the log of each that has. Take the machine figures from
> `GetPerformanceInfo` -- `MachineLoad.Describe()` in the suite's harness prints
> exactly these columns. **Clean up by pid and verify by full image path**: stop
> each browser you started, then re-enumerate for any process whose image path is
> the provisioned `chrome.exe` and stop those too, because the children of a
> headless browser do not always go with it -- 102 survived one level here. Never
> by image name; that is [the rule](../../HAZARDS.md) a measurement does not get
> an exception to.

## Desktop heap: the ceiling nothing reports, measured

**The section above named desktop heap as the surviving candidate and said no
documented API reports it. Both halves are now qualified.** `GetUserObjectInformationW`
with `UOI_HEAPSIZE` reports a desktop's heap **size** -- it is documented, it works,
and it is the *usage* that nothing reports. Measured 2026-08-27 against the
provisioned `chromium-1237` (152.0.7977.8), Windows 11 26200, across **80** real
browser launches. `[MACHINE]`

**A desktop created with `CreateDesktopW` inside `WinSta0` gets 20,480 KB -- the
same allocation as `WinSta0\Default`.** So a rig desktop is the interactive
desktop's equal in size and entirely separate in fact, which is what makes this
measurable at all without touching the session anybody is using.
`CreateDesktopExW` takes a heap size and honours it exactly: 512 KB and 2,048 KB
both read back verbatim.

**Window text lives in the desktop heap, and that is the lever.** A message-only
`STATIC` window costs about **402 bytes** with a one-character title (1,289 of
them fill 512 KB; 5,218 fill 2,048 KB) and about **4,522 bytes** with a
2,048-character one (114 fill 512 KB; **4,637 fill 20,480 KB**) -- a difference of
almost exactly two bytes per character. One process can therefore spend a whole
interactive-sized heap in 2.7 seconds and 4,640 USER handles, well inside the
10,000-object per-process quota.

**What a browser does when it lands on that desktop**, three launches at each
level:

| Windows held | Free heap | Chromium |
|---:|---:|---|
| 4,637 | 0 KB | **died 8 of 8** -- exit `0x80000003`, **0 bytes on stdout and 0 on stderr**, both drained to EOF, 5 or 6 log lines, no message window |
| 4,636 | ≈4.4 KB | died 3 of 3 -- but 61 to 63 log lines and 440 bytes of stderr, having reached its GPU child |
| 4,635 | ≈8.8 KB | died 2 of 3, 104 to 107 log lines |
| 4,634 | ≈13.2 KB | **lived 3 of 3**, 676 to 678 log lines, five `Chrome_MessageWindow`s |
| 4,632 down to 3,600 | 22 KB to 4.6 MB | lived 3 of 3 at each of eight further levels |

> **Chromium needs on the order of ten kilobytes of free desktop heap to reach a
> running state, and the total silence belongs to the last four of them.** One
> window of headroom buys sixty-three log lines and a stderr message. `[MACHINE]`
>
> **The refusal does not reliably say why.** Filling with 2,048-character titles,
> the `CreateWindowExW` that was refused reported `GetLastError` = **0** -- on a
> 512 KB heap and on the 20,480 KB one alike. Filling with one-character titles
> it reported `ERROR_NOT_ENOUGH_MEMORY`; filling a 20,480 KB heap with 23,718
> small windows across four processes it reported `ERROR_NO_MORE_USER_HANDLES`.
> **Three different answers to one shortage**, and the least informative of them
> is the one the failing regime gives.
>
> **Why the browser says nothing.** `WindowImpl::Init` in
> `ui/gfx/win/window_impl.cc` has two crash sites on the null-window path and no
> error return -- a `NOTREACHED()` on the branch where the last error was zero,
> and `CheckWindowCreated` after it. A check does not log, so the account of the
> failure is a crash dump nobody collected. Read from Chromium's tree
> 2026-08-27; **INFERRED** as to which of the two fires, on the strength of the
> zero last error above, since nothing here read a stack.
>
> **The exit code is the least stable part of it.** Twenty-eight deaths produced
> **two** codes: `0x80000003` where the heap was simply full, and `0xE0000008` --
> Chromium's own out-of-memory exception code -- in the arm where the heap was
> handed back mid-startup. Neither is the `1` recorded from the wild, and eighty
> launches produced no `1` at all.
>
> ⚠️ **Extended 2026-08-29, and the `1` is now accounted for elsewhere.** The
> arm this rig had never run is the other half of the dynamic: pre-fill to a
> level the browser survives, then take the last three windows at a chosen
> instant of its startup, so it crosses the cliff **while starting** and not
> before or after. Ten instants, one launch each, pre-filled to 4,634 -- a level
> re-calibrated the same day and still living 3 of 3 at 676 to 678 log lines:
> `+0, +5, +10, +15, +20, +50, +120 ms` gave `0x80000003` with a six-line log;
> `+30` and `+80 ms` gave `0xE0000008` with nine and ten; `+200 ms` **left the
> browser alive** with 396 log lines and **29 crash dumps**, because by then the
> shortage kills its child processes instead of it. **Still no `1`, at any
> instant** -- and [the terminator table](#exit-code-1-is-not-a-crash----what-each-way-of-ending-a-process-leaves-behind----measured-2026-08-29)
> above says why there could not be one: a `1` is what `TerminateProcess(handle, 1)`
> leaves, and nothing in this rig issues one. `[MACHINE]` for the instants, whose
> boundaries are this machine's startup timing.
>
> **To re-establish**, with no registry change anywhere and the interactive
> desktop untouched: `CreateDesktopW("<name>", NULL, NULL, 0, DESKTOP_ALL, NULL)`
> -- the device argument has to be a real NULL, and a `$null` handed across from
> PowerShell arrives as an empty string, which `CreateDesktop` reads as a display
> device named "" and refuses with `ERROR_INVALID_PARAMETER`. Read the size back
> with `UOI_HEAPSIZE`. Start a filler process onto it with `STARTUPINFO.lpDesktop`
> set to `"WinSta0\\<name>"`, creating `CreateWindowExW(0, "STATIC", <2048-char
> title>, WS_CHILD, 0,0,0,0, HWND_MESSAGE, ...)` until it is refused, and start a
> second filler to confirm the first stopped on the heap and not on its own
> quota -- a second one that manages zero is the ceiling. Then launch the
> provisioned `chrome.exe` onto the same desktop with the command line the
> saturation entry above gives, reading both pipes to end of file on their own
> threads. Ask a probe **already
> sitting on that desktop** whether any `Chrome_MessageWindow` exists -- message
> windows are scoped to a window station and desktop, so a probe anywhere else
> answers about somewhere else. Clean up by pid and by `CloseDesktop`, then
> enumerate the desktops of `WinSta0` and confirm yours is not among them. The
> rig that produced this is
> [`docs/probes/2026-08-27-desktop-heap/`](../../docs/probes/2026-08-27-desktop-heap/README.md)
> -- `Rig.ps1` and `Rig.cs` -- and the logs it wrote are in
> [`docs/evidence/2026-08-27-desktop-heap/`](../../docs/evidence/2026-08-27-desktop-heap/README.md).
> *Corrected 2026-09-16 (previously "in a scratch directory this machine
> deletes").* The procedure above is still written to stand without the rig,
> and that has not changed: read it first.

> **The consequence for tests, and it cost two red CI runs:** an assertion of the
> form *"the product recorded X"* must read the process log, not stderr, whenever
> the process is killed instead of shut down. A developer's machine drains that
> queue before the kill and stderr looks complete, so the defect is invisible
> until the suite meets a smaller machine. `ProcessLogRecords.For` is the
> reader, and it is scoped to one process's whole identity -- `(pid,
> creationFileTime)` -- because the log is machine-wide.
> *Corrected 2026-08-29 (previously "`ProcessLogRecords.ForPid` is the reader,
> and it is scoped to one pid because the log is machine-wide").* Scoping to the
> pid alone left the second half of the identity read past and never compared,
> and the log is kept thirty days: **demonstrated live on 2026-08-29, a read
> scoped to a live test host's pid came back holding records written on
> 2026-08-24 by a different process wearing that number.** The pid-only entry
> point is gone, not caveated.
> `[STABLE]` for the asymmetry, which follows from the two sinks' designs;
> `[MACHINE]` for the observation that four cores under 431 tests is enough to
> expose it.

**100 concurrent BrowserAI processes with 24 live Chromium trees is 802
processes, and this machine carries it.** Measured 2026-08-17 by
`SaturationTests`, which starts 100 published binaries at once, gives each its
own session with a real `node.exe`, and has 24 of them launch, close and
relaunch a real headless Chromium. Every peer answered; every session was
claimed by exactly one process and its `browserai.json` named that process's pid;
every job object was pairwise disjoint; nothing survived teardown; the shared
process log held no torn record. **82 s** wall, alone on the machine. The 802
figure is the survivor census from the fault-injection run in which teardown was
deliberately skipped, so it is a count of what was actually live, not an
estimate. `[MACHINE]`

**At that size the machine has no headroom left, and it shows up as other
processes' hang detectors, not as anything BrowserAI does.** Measured the same
day, same test, run *inside* the full 419-test suite instead of alone: seven
unrelated tests failed, every one of them on a 30-second in-process bound --
*"No frame arrived on this pipe within 30 s"* between two objects in the same
process, and a rig teardown reporting its server task still running after 30 s.
Nothing failed in the product. **The lesson is about what a hang detector can
mean**: at a ~25× CPU overcommit a thirty-second silence between two in-process
objects is no longer evidence of a deadlock, so a suite that saturates the
machine cannot also use short wall-clock silences as deadlock detection.
Dropping the browser subset from 24 to **8** -- 100 BrowserAI processes, 100 node
children, ~64 Chromium -- is green in-suite and costs **105 s** for the whole
suite, of which 96 s is this one test. `[MACHINE]`

**A pid alone is not an identity at this scale, and the margin is not
theoretical.** The first version of the disjointness assertion compared job
membership by pid and reported **twelve** processes apparently shared between
two jobs on its very first run -- every one a pid Windows had recycled between
two peers reading their membership. Twenty-four Chromium trees closing at once
frees on the order of two hundred pids within a second. Keyed on
`(pid, creationFileTime)` the same run is clean. Measured 2026-08-17. `[STABLE]`
for the mechanism; the count is `[MACHINE]`.

**`Directory.Move` is refused with `ERROR_ACCESS_DENIED` on a directory whose
files were written milliseconds earlier, and nobody has identified the holder.**
***Corrected 2026-08-18 (previously "...and the holder is a scanner rather than a
process anyone can name", which asserted in the heading what the body concedes
was never established).*** The refusal rates below are measured; **the cause is
not**. Windows refuses to rename a directory while any handle is open below it --
that half is documented -- and a real-time anti-malware filter opening every file
just after it is written is a *plausible* holder that was never confirmed. **The
distinction is load-bearing**, because the rule drawn from it is that a rename
used as a *commit* gets a bounded retry while a rename used as a *liveness test*
gets none: if the holder is transient and foreign the retry is right, and if it
is ever something of ours the retry masks a defect. **The tool to settle it
already ships** -- `Interop/RestartManager.cs` exists to answer *who holds this*;
call `RmGetList` on the path at the instant of the denial. Measured 2026-08-17 in two independent places under a
fully parallel suite: the first-run cache's publish-by-rename failed in **five of
twenty-one** runs with *"Access to the path '...\.staging-&lt;guid&gt;' is
denied"*, and `InstanceDirectoryTests`' planted abandoned directory failed to be
reclaimed in **one of ten**. Neither reproduced once at four-way parallelism.
**The two correct answers are different, and which one applies depends on what
the rename means.** Where the rename is a *commit* -- nothing else can be holding
the tree, so a refusal is transient -- a bounded retry is right, and it is the
same shape `InstallationMarker` already uses against the same class of
transient. Where the rename is a *liveness test*, as in
`InstanceDirectory.Claim`, a retry is wrong: a genuinely live directory always
refuses, so the retry's budget would be paid once per live instance on every
startup -- minutes, at the design point. To re-establish: write a file into a
directory and rename the directory in the same millisecond, on a machine under
heavy I/O with real-time scanning on. `[MACHINE]`

**A suite of 419 tests run all-at-once is bounded by CPU oversubscription, not
by thread-pool injection.** Measured 2026-08-17. Raising the parallel limit from
4 to unbounded took the suite from **33.7 s** to **~20 s**, and produced
multi-second latencies on *in-process* round trips: 1.51 s and 2.27 s against an
800 ms budget, for work that normally answers in milliseconds. The obvious
suspect was the thread pool's hill-climbing injection -- the pool starts at
`Environment.ProcessorCount` and adds roughly one thread per 500 ms -- but
`ThreadPool.SetMinThreads(1024, 1024)` **did not help**: the same measurement
came back **worse**, at 2.27 s where it had been 1.51 s. The cause is plain
arithmetic: 416 runnable threads over 32 cores is a 13× overcommit, and one
round trip through the in-process rig is four thread handoffs. **A wall-clock
assertion over an async pipeline cannot survive this and should not be written**;
the fix was a `TimeProvider` seam on the one timer in the product, so the test
advances the clock itself. `[MACHINE]`

## Reading the process log by pid alone misattributed four live servers -- measured 2026-09-23

`[STABLE]` on the Windows property, `[MACHINE]` for the count. Windows
**10.0.26200**, BrowserAI **1.0.0** running with **1.1.0** staged, 22 live
servers. The census it came out of:
[`docs/evidence/2026-09-23-instances`](../../docs/evidence/2026-09-23-instances/README.md).

**The writer was never the problem.** Every record this product writes carries
`pid=<n>@<createdFileTime>`, and
[`FileLoggerProvider`](../../src/BrowserAI.Core/Logging/FileLoggerProvider.cs)
explains at length why: Windows re-uses pids, the machine-wide log outlives its
processes by thirty days, and the pair is this repository's standing identity for
a process.

⭐ **The reader was.** A census that grouped 22 live servers by the number alone
**misattributed four of them**, and it did so in two different ways at once.
Records written by a process that had exited hours earlier were read as the
history of a live server that had inherited its pid -- the reuse the writer's
comment predicts. And **a bare number is also a PREFIX of a longer one**: a search
for `pid=178` matches `pid=178@...` and `pid=1786@...` equally, so a grep can
merge two processes that never shared a number at all.

⚠️ **And the log carries a second, bare shape that looks the same.** The writer
field is the pair, but message text is not: `BrowserAI {Version} started.
pid={ProcessId} ...` and the stray sweep's `Terminated a stray browser:
pid={ProcessId} ...` write a **bare** pid inside the sentence, because there the
number names *some other process* whose creation time the sentence does not carry.
So one grep for `pid=` returns two kinds of thing, and only one of them is an
identity.

**What a reader has to do**: match on the pair, and anchor it -- `pid=<n>@`, never
`pid=<n>`. That is what `ProcessLiveness.IsAlive(int, long)` takes, and it is
spelled identically in `browserai.lock`'s `processCreatedFileTime`, so a log line
and a lock record name the same writer with the same characters.

**Re-establish it** by reading `server-identities.txt` in the batch -- each live
server as pid and creation time -- against the process log's own records for those
pids. ⚠️ **A deliberate reproduction needs a pid to come round**, which nothing can
schedule, so this is a kb entry and not a suite arm; what the suite does hold is
that the pair is what liveness takes.

## A per-server named pipe answers from memory and cannot tear -- measured 2026-09-24

`[MACHINE]` for every number, `[STABLE]` for the Windows behaviour. Windows 11 Pro
**10.0.26200**, AMD Ryzen 9 5950X (32 logical), 128 GB, .NET SDK **10.0.401**,
NativeAOT win-x64, Defender real-time protection on. Measured by the IPC review
with a prototype published NativeAOT, whose stand-in servers held their live
marker exactly as `LiveInstances.Join` does (`CreateNew`, `ReadWrite`,
`FileShare.Read`, buffer size 1); sizes were measured on copies of the real
server at `245edac`. Everything it was read from:
[`docs/evidence/2026-09-24-ipc-review`](../../docs/evidence/2026-09-24-ipc-review/README.md).
It settled Q268 and, with the lifecycle research, Q284 -- the maintainer's words
verbatim, *"Q284 a"*: one raw named pipe per server, answering `describe` from
memory and `stop` by acknowledging and then stopping. The product's half is
[`Coordination/ServerPipe.cs`](../../src/BrowserAI.Core/Coordination/ServerPipe.cs)
and [`Coordination/ServerPipeClient.cs`](../../src/BrowserAI.Core/Coordination/ServerPipeClient.cs).

### A record rewritten in place is read torn, and the torn read parses

⭐ **The failure a file-based record has, and the reason the pipe was chosen.** A
holder rewrote a JSON record in place in its own marker file, and 3,000,000 reads
were taken against it at the fastest rate the holder could write:

| Framing | Reads | Detected | **Accepted with wrong values** |
|---|---:|---:|---:|
| none, the JSON alone | 3,000,000 | 0 | **22** |
| a CRC-32 in a header | 3,000,000 | 374 | 0 |
| a sequence counter either side | 3,000,000 | 1,708,003 | 0 |
| a byte-range lock | 3,000,000 | 1,663,733 | 0 |

**Every one of the 22 parsed as valid JSON**, so no parser would have caught
them: they are values from two different writes stitched into one well-formed
document. At the realistic rate, ten writes a second, the unframed record was
read wrong **twice in 1,976 writes** over four runs (once in each of two runs,
60 s and 17.6 s long, and never in the other two), and the CRC framing accepted
none. Writing a temporary file and renaming it over the record accepted none
either, and cost two other things: a read went from about 6 µs to about 60 µs at
p50, and the rename itself failed with `ERROR_ACCESS_DENIED` **88 times in
17,812** at full rate while readers held the file. The first such run's holder
died of the unhandled failure, which is why the prototype's retry exists.

**Re-establish it** with `torn` in the batch's prototype (`proto/Bench.cs`,
`proto/Holder.cs`): a holder process rewriting its own marker, and a reader
counting parses that succeed with values no single write produced.

### The pipe has nothing to tear, and a page of 100 servers costs 22 ms

A pipe carries the bytes the writer wrote, in order, from a snapshot the writer
built in its own memory, so there is no in-place rewrite for a reader to catch
half done. What asking costs, over stand-in servers each asked once per round:

| Asking 100 servers | Round, one after another (p50) | Round, eight at once (p50) | One server, one after another, p50 / p99 |
|---|---:|---:|---:|
| the census alone: is the marker held | 5.67 ms | 2.15 ms | 48.2 / 137.4 µs |
| **the raw pipe, `describe`** | **21.94 ms** | **4.95 ms** | **208.7 / 387.2 µs** |
| the framework's pipe, synchronous | 23.86 ms | 4.55 ms | 224.3 / 412.9 µs |
| the framework's pipe, asynchronous | 22.86 ms | 5.20 ms | 223.8 / 383.4 µs |
| reading the working directory and parent from outside the process | 27.04 ms | 13.07 ms | 255.4 / 510.0 µs |
| a CRC-framed record file | 180.16 ms | 18.37 ms | 196.7 / 7,962.2 µs |

With eight in flight the raw pipe's per-server p99 was **1,989.9 µs**, the
slowest pipe percentile measured in the batch, and it is what
`ServerPipeProtocol.CallBound`, 500 ms, is derived from: more than 250 times it.
A description is small: **419 bytes** of JSON with no sessions, 1,082 with three,
4,858 with twenty, built in 1.51, 2.60 and 10.23 µs.

### What the raw call costs the binary, and what only it can set

| One probe each, NativeAOT, on the real server at `245edac` | `BrowserAI.Server.exe` | Over the baseline |
|---|---:|---:|
| baseline, the same hook with nothing behind it | 19,318,784 B | -- |
| `NamedPipeServerStream`, synchronous | 19,440,640 B | **+121,856 B** |
| `NamedPipeServerStream`, asynchronous | 19,447,808 B | +129,024 B |
| **raw `CreateNamedPipeW`** | **19,322,368 B** | **+3,584 B** |
| a CRC-framed record | 19,319,808 B | +1,024 B |
| a named stop event | 19,320,320 B | +1,536 B |

**The product as built is larger than the probe**, because it carries the
description, the dispatch and the logging as well as the call: the published
server went from **19,317,760 to 19,361,280 bytes (+43,520)** between `acaaf86`
and the first build of the pipe on 2026-09-24, measured by this writer.

**What Windows gives a pipe by default, read off the handle with
`GetSecurityInfo`.** A pipe created with no security attributes, and the
framework's server stream by default, byte for byte the same, has **five**
allow entries: `SYSTEM`, the administrators and the owner with full access, and
**`Everyone` and `ANONYMOUS LOGON` with read**. The framework's `CurrentUserOnly`
option writes one entry for the user, `0x1F019F`. The product writes its own:
`D:P(A;;GA;;;<the user's SID>)`, one entry.

**`PIPE_REJECT_REMOTE_CLIENTS` is visible from both ends**: `GetNamedPipeInfo`
answers flags `0x9` on the server end and **`0x8` on the client end** of a pipe
created with it, `0x1` and `0x0` without. The framework's server stream has no way
to set it.

**`FILE_FLAG_FIRST_PIPE_INSTANCE` is the defence against a name somebody else
created first**, measured by this writer the same day. Against a name another
creator had opened with unlimited instances and its default DACL, a creation
carrying the flag was refused with **`ERROR_ACCESS_DENIED` (5)**, and the same
creation without it **succeeded**: it joined the other creator's pipe as a second
instance, and a client could then have reached either. Against a name this
process already served with one instance, a second creation was refused with
**`ERROR_PIPE_BUSY` (231, `0x800700E7`)** with or without the flag, and with a
maximum of two instances as well; the review's two framework servers measured the
same `0x800700E7`.

### What a client sees when the server fails

| The server | What the client saw |
|---|---|
| dies after writing half its answer | **no answer, through the length prefix**, in 32.1 to 36.4 ms: 3 of 3 on the raw pipe and 3 of 3 on the framework's |
| accepts and never answers | its own read timeout and no more: 256.0 ms against 250, 1,012.7 ms against 1,000 |
| answers once and never listens again | the second client's 500 ms connect ran out at 508.9 to 515.3 ms |
| was killed | **the framework's client waited out its whole 500 ms connect**, 493.7 to 510.5 ms, because it retries a name nobody serves until its timeout |
| eight clients at once, one instance | all eight served, one after another, the last in 1.08 ms |

**So the census stays the liveness signal.** Against a listener that had
stopped answering, asking whether its marker was held said *held* in **240 µs**
while a `describe` with 500 ms timeouts gave up at 504 ms; against a killed
holder the census said *free* in **85 µs**. The pipe tells a server that answers
from one that is alive and does not; only the census tells a server that has
gone from one that has not. BrowserAI's own client asks the census first and
does not wait on a name nobody serves.

### A second start finds the first through a pipe, and learns its pid

Twenty processes started at once, three rounds for each shape, and every round
produced **exactly one winner**:

| Single-instance shape | Winner decided | A second start handed over | It learned the winner's pid |
|---|---:|---:|---|
| a named mutex and a named event | 0.09 to 0.15 ms | 0.12 to 15.64 ms | **no**: an event carries none |
| a pipe created with `FILE_FLAG_FIRST_PIPE_INSTANCE` | 0.51 to 0.86 ms | 2.76 to 31.18 ms, p50 13.79 to 16.20 | **yes**, in every reply |

After the winner was killed, the next start won in 0.06 to 0.07 ms through the
mutex and in 0.17 to 0.19 ms through the pipe. The pid is what the Q284 design
needs, because a second start has to grant the first the right to take the
foreground before it asks it to show its window.

### A pipe created with `PIPE_UNLIMITED_INSTANCES` holds more than 255 callers -- measured 2026-10-03

`[STABLE]` Windows 11 Pro 10.0.26300, .NET 10.0.12, one process, 2026-10-03
between 13:22Z and 13:24Z.
[Evidence](../../docs/evidence/2026-10-03-pipe-instances/README.md),
[rig](../../docs/probes/2026-10-03-pipe-instances/README.md).

**255 is the value of `PIPE_UNLIMITED_INSTANCES`, and it is not a ceiling.**
Microsoft's documentation of `CreateNamedPipeW`, `nMaxInstances`, read the same
day: *"Acceptable values are in the range 1 through PIPE_UNLIMITED_INSTANCES
(255). If this parameter is PIPE_UNLIMITED_INSTANCES, the number of pipe instances
that can be created is limited only by the availability of system resources."*
Until that day BrowserAI's remarks read the number as *"Windows' own ceiling of
255"*, and its server pipe carried a wait and a warning, event 7, for a caller
past it; the remark is corrected, and the wait and the warning are gone.

The probe creates instances exactly as `NamedPipes` does -- `PIPE_ACCESS_DUPLEX`,
`FILE_FLAG_FIRST_PIPE_INSTANCE` on the first instance only,
`PIPE_REJECT_REMOTE_CLIENTS` as the whole pipe mode, 64 KiB out and 4 KiB in, a
default timeout of 0 and a DACL whose one entry is the current user -- and opens
each client the way `NamedPipes.OpenClient` does. It listens the way
`ServerPipe` does: a client connects to the one listening instance, the next
instance is made, and the connected one is kept, so every instance stays
connected until the target is reached.

| `nMaxInstances` | Target | Held connected at once | One more caller | Bytes through the last connection | Wall time |
|---|--:|--:|---|---|--:|
| 255 | 300 | **300** | connected | both ways | 36.3 ms |
| 255 | 600 | **600** | connected | both ways | 27.2 ms |
| 255 | 1,000 | **1,000** | connected | both ways | 32.3 ms |
| 255 | 2,000 | **2,000** | connected | both ways | 46.4 ms |
| **254**, the positive control | 300 | **254**, and the 255th instance refused with `ERROR_PIPE_BUSY` (231), *"All pipe instances are busy"* | - | - | 38.1 ms |

The control is what makes *no refusal* mean something: the same probe, with the
one number changed to a real cap, sees the refusal at exactly the instance the cap
predicts. Each held connection cost the probe two handles, 4,247 at 2,000
against 248 before. **What it does not measure** is where system resources run
out, which was not pushed for on a machine other work shares, and the cost of
the thread `ServerPipe` gives every connection, which the probe has no
equivalent of. `ServerPipeTests.APipeHoldsMoreCallersThan255AtOnceAndStillAnswersTheNext`
holds 300 silent callers through the product's own pipe on every run.

**Re-establish it** with the rig: `dotnet run instances.cs -- 300 600 1000 2000`,
then `dotnet run instances.cs -- --max 254 300` for the control. Each target runs
on a fresh pipe name and closes every handle before the next.

### Inside a client's job

`JOBREPORT` in the batch: a prototype started from the review's own shell was in a
job whose limit flags were `0x3C00`, which is `JOB_OBJECT_LIMIT_BREAKAWAY_OK`,
`JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK` and `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`,
and a child it started with `Process.Start` was **in no job**. Whose job that was
is not in the file, so this establishes the shape and not the owner.

### What crosses a process boundary in BrowserAI -- read 2026-09-24

The review counted **17** inter-process mechanisms and found **no named pipe, no
named event and no shared memory** among them before this change; its list is not
in the batch. This writer's own reading of the tree at `acaaf86`, taken to put a
list beside the count, finds these, and the absence holds for every row:

| Between | Mechanism | What it carries | How liveness is read |
|---|---|---|---|
| BrowserAI processes | a live marker, `<install root>\live\<pid>-<guid>.live` | membership of the census | a held handle, `FileShare.Read` |
| BrowserAI processes | `Global\BrowserAI-Live-<hash>` | one join, census or reclaim at a time | the mutex |
| BrowserAI processes | `instance.live` in each run's directory | that a run directory is in use | a held handle |
| BrowserAI processes | `browserai.lock` in a session directory | who owns the session, as `(pid, creation time)` | a held handle |
| BrowserAI processes | `browserai.data` | the session's statements and its call log, in SQLite | none: a record |
| BrowserAI processes | the session index, one file per session under the data root | which sessions exist | none: a record |
| BrowserAI processes | `Global\BrowserAI-<hash>` | one create-or-take of one directory at a time | the mutex |
| BrowserAI processes | `Global\BrowserAI-Sweep` | one stray sweep at a time | the mutex |
| BrowserAI processes | `Global\BrowserAI-Provision-<hash>` | one browser download at a time | the mutex |
| BrowserAI processes | `reinstall.lock` in the browsers root | shared by every session, exclusive to a reinstall | a held handle |
| BrowserAI processes | the process log, `browserai-*.log` | every process's records | an append under a byte-range lock |
| BrowserAI processes | `mcp-registration.json` | the last registration outcome | none: a record |
| the server and its children | stdio, JSON Lines | the MCP conversation with each `@playwright/mcp` child | end of stream |
| the server and its children | a job object per child | that nothing outlives the server | the kernel |
| the server and its children | the environment block and a generated config file | how each child is started | none |
| the server and its client | stdio, JSON Lines | the MCP conversation | end of stream, and a handle on the launcher |
| BrowserAI and browsers | profile locks, and window titles read across processes | whose browser a stray is | a held file, a readable title |
| BrowserAI and the updater | `Update.exe`'s command line and `--waitPid` | an apply | the asking pid's exit |
| BrowserAI and its clients | the clients' own configuration files | where the server is registered | none |

**Re-establish it** by reading the tree for every named object, every file
another process opens and every process started. The number of rows is a count
under this table's own predicate, and the review's 17 was taken under its own.

### In 332 of 365 starts, an installed server's working directory was a repository

The review read **143 of 162** starts of the installed server with a working
directory that is a repository. Re-read by this writer the same day, read-only,
over the process log's `Startup[1]` records for images under
`BrowserAI.app\current\`, from 2026-09-16T04:00Z to 2026-09-24T13:29Z: **365
distinct starts** by pid and creation time, **332** whose working directory holds a
`.git`, **334** inside one, **6** in `C:\Windows\System32`, and 2 whose directory
no longer exists. `[MACHINE]`, and deliberately counted and not listed: the
directories are the maintainer's projects. It is what makes the working directory
worth describing, because the sessions page can name the project a server was
started for before that server has made a single call.

**Re-establish it** with the batch's `cwdcount.py`: every `Startup[1]` record,
deduplicated by pid and creation time, each working directory tested for `.git`
on the day of reading.

## What starts first after sign-in, and what a task-started process may do -- measured 2026-09-24

`[MACHINE]` for every time and count; `[STABLE]` only where a paragraph says so.
Windows 11 Pro **10.0.26200**, the morning's sign-in after a restart. The order was
re-read by this writer, read-only, from the System and Shell-Core logs, the processes
still running, the task scheduler and BrowserAI's own process log; the foreground
and single-instance figures are the lifecycle researcher's. Everything it was read
from, with the maintainer's own programs counted and never named:
[`docs/evidence/2026-09-24-coordinator-lifecycle`](../../docs/evidence/2026-09-24-coordinator-lifecycle/README.md).
It answers the maintainer's *"Research how we can reliably start earlier than vscode
starting dozens of servers simultainiously"*: **nothing guarantees going first, and
a logon task comes closest.**

### The order this machine started things in

| From sign-in | What | Read from |
|---|---|---|
| 0 | the sign-in, 07:38:51.685Z | System log, Winlogon 7001 |
| +1.061 s | the first process the task scheduler started, a Windows task host | a running process whose parent is the `svchost.exe` hosting `Schedule` |
| **+1.249 s** | the first process a third-party logon task started | the same |
| +1.396 s | `explorer.exe` | a running process |
| +1.5 to +3.9 s | Explorer's own 67 log-on tasks | Shell-Core 62170 and 62171 |
| +1.9 to +5.0 s | a `RunOnce` key, 2 commands | Shell-Core 9705 to 9708 |
| +20.2 s | Explorer's `DesktopStartupApps` phase begins; it ends at +334.7 s | Shell-Core 9648 and 9649 |
| +20.8 to +161.1 s | two `Run` keys, 2 commands and then 10, **started one after another**: Explorer waits for each, and 3 of the 10 held it for 30.0 to 30.1 s | Shell-Core 9705 to 9708 |
| +161.1 to +245.3 s | 6 packaged apps' startup tasks, one after another | Shell-Core 62408 and 62409 |
| +245.3 to +246.4 s | a third `Run` key, 1 command | Shell-Core 9705 to 9708 |
| **+274.5 s** | `Code.exe`, with `explorer.exe` as its parent | a running process |
| **+283.7 s** | VS Code's first BrowserAI server, 07:43:35.335Z, and **12 of them by +300.0 s** | the process log's `Startup[1]` records |

**The event names the key and never the hive**, so which `Run` key is the user's is
not in this table. **Nothing records the Startup folder by itself**: `Code.exe`'s
start falls inside Explorer's startup phase, after the `Run` keys and the packaged
tasks, and the user's Startup folder holds a `Visual Studio Code.lnk`, which is
consistent with that shortcut starting it; a click on a pinned icon would have given
it the same parent. **The task scheduler's own log is switched off on this machine**
(`Microsoft-Windows-TaskScheduler/Operational`, `IsEnabled` false), so a task is timed
by the process it started, and `Get-ScheduledTaskInfo` gives its last run in whole
seconds only.

**One server came first this morning, and nothing here says what started it.** At
+104.8 s, 07:40:36.476Z, a BrowserAI **1.0.0** server started with `C:\Windows\System32`
as its working directory for a client that named itself `claude-code 2.1.281`, where
every one of VS Code's servers met `claude-code 2.1.280`. Its census found only itself,
so it applied the pending 1.1.0: staged in 0.6 s, handed to `Update.exe` at +107.3 s,
the update hook running at +108.8 s. VS Code's twelve servers, three minutes later,
were all 1.1.0. The client's process is gone, and neither the `Run` keys nor any
logon task that ran names a Claude Code executable.

**So a logon task is the earliest mechanism an installer can register**: its process
started 0.15 s before `explorer.exe`, 103.5 s before that first server and 282.4 s
before VS Code's first. It is not a guarantee: a client started by a task or a `Run`
key of its own can start a server at any point in the table, and the first one this
morning was started by something no record here names.

### A logon task registers without elevation when its trigger names the user

A non-elevated token may register a logon trigger **scoped to the registering user**
and is refused one **for any user**, `0x80070005`; the table and the deleted
definition that met the refusal in 2026-08 are in
[detection](detection.md#the-logon-sweep-task). **A scoped trigger fired within two
seconds of the unscoped ones**: the only task this morning
whose trigger was scoped to the user carries a 15 s delay and last ran at 07:39:08Z,
so its trigger fired between +1.3 and +2.3 s, against +0.3 to +1.3 s for the tasks
whose trigger is for any user. The record bounds the difference at two seconds and
does not measure it.

### A process a task starts may not take the foreground

Three times the researcher started the probe through a registered task, between
12:07Z and 12:11Z, and each time it read **`AllowSetForegroundWindow` on its own pid
as false, error 5**, with the foreground lock timeout at 2147483647 ms and the
foreground window belonging to `Code.exe`. The same probe started from the
researcher's own shell read **true**. `[STABLE]` for what a false means: the call
[fails when the calling process cannot set the foreground window](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-allowsetforegroundwindow).
With this machine's lock
[effectively infinite](detection.md#this-machines-foreground-lock-is-effectively-infinite-so-it-cannot-see-a-focus-steal----measured-2026-08-24),
**a coordinator the logon task starts can bring its own window forward only when a
process that holds the right grants it first**, which is why Q284 a has a second start
learn the first one's pid before it asks for the window.

⚠️ *Added 2026-10-08 by addition.* **A browser such a process starts can still come
to the front.** On the maintainer's screen, with his leave, a launcher started through
a scheduled task with the sign-in task's principal and settings, `IRegisteredTask::Run`
with `TASK_LOGON_INTERACTIVE_TOKEN`, its parent the `svchost.exe` hosting the Schedule
service, started a browser directly with `CreateProcessW`, `STARTF_USESHOWWINDOW` and
`SW_SHOWDEFAULT` (what Node's spawn passes), in a kill-on-close job; the same launcher
started from a shell was the control, the runs interleaved. Chromium was
`chromium-1247` and Firefox `firefox-1553`, the foreground lock time-out 2147483647 ms,
2026-10-08 between 14:14Z and 14:19Z, Windows 11 Pro 10.0.26300.9550.

| Browser | Started by | Runs | Its window came to the front and took the keyboard focus | Opened behind the window in front, visible and not minimized |
|---|---|---|---|---|
| Chromium | the scheduled task | 3 | **3 of 3**, 0.32 to 1.34 s after the launch | 0 |
| Chromium | the shell | 3 | **3 of 3**, 0.31 to 0.37 s | 0 |
| Firefox | the scheduled task | 3 | 1 of 3, from 2.80 s, the foreground going back to his own window at 7.47 s | 2 |
| Firefox | the shell | 3 | 1 of 3, from 2.08 s | 2 |

**How the launcher was started made no difference.** Chromium's window comes to the
front and takes the keyboard focus about 0.3 s after the launch, 6 of 6, so a person
typing at that moment types into it; Firefox's came to the front 2 of 6 times and
opened behind 77 to 118 other windows 4 of 6, the person's own window keeping the
focus. Why Firefox splits was not measured; the person was switching windows while it
ran, and Firefox shows its window 1.4 to 2.8 s after the launch, against Chromium's
0.3 s. No browser window was ever minimized, and no taskbar flash was recorded, with no
positive control for one. **BrowserAI's own chain was not measured**: these were
direct launches, not through Node and `@playwright/mcp` with Playwright's flags. So
the probe's `AllowSetForegroundWindow` reading above says what the process may do,
and says nothing about a browser it starts. Everything it was read from:
[`docs/evidence/2026-10-08-step0`](../../docs/evidence/2026-10-08-step0/README.md),
`screen/runs/`; re-establish it with `screen/part2.ps1.txt` and the launcher in
`screen/launcher/`, which register the task, run each case and delete the task, and
snapshot and clean `HKCU\Software\Mozilla\Firefox\Launcher` around any Firefox run.

### A single instance through a mutex and a message-only window

The researcher's prototype, published NativeAOT with the Windows subsystem: the first
start creates `Local\CLProbe-<name>` and a message-only window and waits; a second
start finds the mutex taken, finds the window with `FindWindowExW(HWND_MESSAGE, ...)`,
grants the first one's pid the foreground with `AllowSetForegroundWindow`, posts a
registered `show` message and exits. Twenty second starts, one at a time, from a
harness holding foreground rights:

| | Result |
|---|---|
| second starts that handed over | **20 of 20**, every `AllowSetForegroundWindow` true, every post delivered |
| a second start finding the mutex taken, from entering `Main` | 236 to 509 µs, median 267 µs |
| post to the first start's window procedure | 30 to 525 µs, median 40 µs |
| **launch to the first start showing** | **10.67 to 23.06 ms, median 11.35 ms** |
| launch to the second start's exit | 13.99 to 26.07 ms, median 14.82 ms |

Medians are over the 20, the mean of the two middle values. **Q284 a took the pipe
for this job instead**, measured in
[a second start finds the first through a pipe](#a-second-start-finds-the-first-through-a-pipe-and-learns-its-pid):
a pipe's name is machine-wide and its reply carries the pid, where a message-only
window is found only from the same desktop.

**Re-establish it** with the batch's `writer/sign-in.ps1.txt`, read-only, against the
morning of a sign-in, and `proto/measure.ps1.txt` over a publish of `proto/`; a
task-started reading needs a task registered with the probe's `fg-info` mode as its
action, removed afterwards.

## A process the Task Scheduler starts keeps its job's processes through every client's exit -- measured 2026-10-03

`[FLOATS]` on the clients' releases for the seven exits, keyed like
[what each client does to a stdio server](../mcp/protocol.md#what-each-client-does-to-a-stdio-server-when-the-session-ends----measured-2026-10-03),
whose rig this one extends; `[MACHINE]` for the job the scheduler put the task's
process in. Windows 11 Pro 10.0.26300; Claude Code **2.1.288** (the CLI) and
**2.1.287** (the binary the VS Code extension ships), codex-cli
**0.155.0-alpha.9.2** and **0.160.0**. Taken 2026-10-03 between 13:38:40Z and
13:41:19Z, with stand-ins and not BrowserAI's binaries. Everything it was read from:
[`docs/evidence/2026-10-03-coordinator-survival`](../../docs/evidence/2026-10-03-coordinator-survival/README.md);
the rig: [`docs/probes/2026-10-03-coordinator-survival`](../../docs/probes/2026-10-03-coordinator-survival/README.md).
It answers the precondition of Q366 b: **whether a process the coordinator starts is
out of every client's reach.** It is.

A *coordinator* stand-in, built for the Windows subsystem, was started by a scratch
per-user task with the sign-in task's settings, run on demand the way a blocked
server runs that task. It created a kill-on-close job and started a *host* in it;
the host started one *browser* per request in a kill-on-close job of its own, nested
in the coordinator's. Each run's dummy server, started by a real client, asked the
host for a browser named after the run, and started the stand-in it always started
in a job of its own, as BrowserAI's server does. Then the client was ended.

| Exit | Server | Its own stand-in | The host's browser after the settle, and 3 s later |
|---|---|---|---|
| Claude Code CLI, stdin closed, so its own `taskkill /T /F` | killed 3/3 | dead 3/3 | **alive 3/3** |
| Claude Code CLI terminated | killed 3/3 | dead 3/3 | **alive 3/3** |
| the VS Code extension host exiting, through a stand-in, with 2.1.287 | killed 3/3 | dead 3/3 | **alive 3/3** |
| `codex exec` 0.155.0-alpha.9.2 finishing | killed 3/3 | dead 3/3 | **alive 3/3** |
| the codex 0.155.0-alpha.9.2 app-server, stdin closed | killed 3/3 | dead 3/3 | **alive 3/3** |
| `codex exec` 0.160.0 finishing | killed 3/3 | dead 3/3 | **alive 3/3** |
| the codex 0.160.0 app-server, stdin closed | killed 3/3 | dead 3/3 | **alive 3/3** |

Every released browser then exited, code 0, 21 of 21.

**Why it holds, as far as the clients go.** Claude Code reaches its server through
the server's tree, with `taskkill /T`, and through its own job when it is killed;
Codex reaches it through a job of its own per server, which allows no breakaway.
A process the Task Scheduler starts is in neither: its tree begins at the scheduler's
service, and its job is one the scheduler made. So a job of that process's own, and
everything in it, is reached by none of the three. **A keeper the server starts is
not**: it inherits the server's job, and Codex's ends it, 12 of 12 in the same day's
`keeper` arm ([`analysis-keeper.tsv`](../../docs/evidence/2026-10-03-client-exit/analysis-keeper.tsv)).

**And the containment half, which is what keeps it safe.** With the batch done, the
host was asked for two more browsers, a watcher opened handles to both and checked
each against its recorded creation time, and the coordinator stand-in was terminated
by its recorded pid and creation time about 700 ms after the watcher started. Both
browsers exited, code 0, 690 ms into the watch, and the host no longer answered on its
pipe 2 s later. **The coordinator going takes everything it started**, however it
goes: its job's one handle closes with it. The watch's clock and the kill's were not
one clock, so this places the exits at the kill and no closer.

**The scheduler's own job** `[MACHINE]`. The task's process was already in a job when
it started, one the Task Scheduler put it in with six other processes, limit flags
`0x0`: no kill on close and no breakaway rule. Kill-on-close jobs nested under it
worked, three levels deep: the scheduler's, the coordinator's (`0x2000`, read back by
the host) and each browser's (`0x2000`). Which processes the six others were was not
recorded. *Corrected 2026-10-08 by addition: they were not this task's.* The Task Scheduler
puts every process it starts in the session, from every task, into one job it shares,
measured with stand-ins on 2026-10-08 in [the entry below](#what-the-task-scheduler-does-with-a-second-run-a-missing-or-disabled-task-end-and-a-child-left-behind----measured-2026-10-08);
the six were that job's other members, started by other tasks.

**What it does not establish.** The real binaries: the coordinator, host and browser
were stand-ins, and the real ones are measured by the suite's arms over the published
slice. Signing out and shutting down, which end the coordinator's job by ending its
session and were not run. The Codex desktop app and the real VS Code extension host,
whose exits were driven through the CLI and a stand-in.

**Re-establish it** with the probe record's `tools\drive.ps1`, which registers the
scratch task, runs the batch, kills the coordinator and removes the task whatever
happened, and `tools\analyzec.py` over the run; compare against the batch's
`summary-survival.tsv`. Every client runs from the client-exit rig's copies, under a
scratch configuration against a local stub.

## The real programs through the real Task Scheduler -- measured 2026-10-04

`[MACHINE]` for every time. How the clients end a server floats with their releases, as
the 2026-10-03 entry above says, and that entry's row carries it.
Windows 11 Pro 10.0.26300; BrowserAI **1.1.1-alpha.0.197**, both programs published
from `e30380ac`, lane c's option c as built, with `@playwright/mcp` 0.0.83 and
RegisterAI 0.2.0, packed as the suite's test pack with vpk **1.2.161** and installed
the way `RealInstallerTests` installs it, `--silent --installto` a root under
`%LOCALAPPDATA%\BrowserAI-test-scratch`; the clients Claude Code **2.1.288** and
codex-cli **0.160.0**. Taken 2026-10-04 between 01:56Z and 02:21Z under the suite lock
and the installer lock. Everything it was read from:
[`docs/evidence/2026-10-04-onebinary-measure`](../../docs/evidence/2026-10-04-onebinary-measure/README.md),
`runs/m2b/` and `m2-*`. It repeats the stand-in measurement of 2026-10-03 above with
the real programs, and it holds.

**Each run** waited until nothing of the test install ran, so that every run had to
start a coordinator; client A opened a session with a hidden Chromium on a local page
and then ended one of four ways; 6 s later the watcher read which processes were
alive; then client B called `browser_snapshot` on the same session, with no resume,
and `browserai_destroy`.

| Exit, 3 runs each | Client A | Front asked to host up, from the front's own log | How client A's front ended | 6 s later | B took over |
|---|---|---|---|---|---|
| Claude Code, normal exit | 2.1.288 `-p` | 523, 575, 534 ms | by itself, "the client's input ended", exit 0 | coordinator, host and browser alive, 3 of 3 | 3 of 3, with `claude -p` |
| Claude Code killed | 2.1.288, VS Code transport, terminated | 518, 521, 567 ms | Claude Code's job closing, exit 0, 3 ms after the client died | 3 of 3 | 3 of 3 |
| `codex exec` ending | 0.160.0 | 673, 565, 619 ms | Codex's job, exit 1 | 3 of 3 | 3 of 3, with `codex exec` |
| app-server ending, stdin closed | 0.160.0 | 536, 518, 572 ms | Codex's job, exit 1 | 3 of 3 | 3 of 3, with `codex exec` |

**In all 12 runs:**

- **The start path.** Client A's front logged `StartedThroughTheTask`.
  `BrowserAI.exe --sign-in --start-host` appeared 106 to 145 ms after the front
  started, its parent the `svchost.exe` hosting the Schedule service, inside a job the
  Task Scheduler made, and it started `BrowserAI.Server.exe --host` 163 to 203 ms after
  the front. Counting the two runs of a first attempt, the task started **14 of 14
  times, in 514 to 674 ms**, and no front fell back to serving in-process.
- **Codex's first turn** had BrowserAI's tools, 3 of 3 under `codex exec`.
- **The takeover.** Client B's front reached the same host and no second coordinator
  started; the same Chromium main process was alive when B began, and the snapshot
  showed the page's own heading. `browserai_destroy` closed the browser 0.8 to 1.8 s
  later, exit 0.
- **The end.** The host ended 60 to 75 s after client B, exit 0, and the coordinator
  11 to 19 ms after the host, exit 0.
- **Windows.** No visible window appeared anywhere on the desktop; the watched
  processes owned 144 windows, all invisible and all Chromium's own headless ones; no
  BrowserAI process owned a window of any kind; and every front had its own windowless
  console, 24 of 24 client runs.

⭐ **A process the Task Scheduler starts never sees the client's environment.** The
host wrote to the default data root, `%LOCALAPPDATA%\BrowserAI`, although the
installer had `BROWSERAI_ROOT` pointing at scratch. The task's process gets the
environment the Task Scheduler builds for the user, so a setting meant for it has to
travel as an argument or over a pipe, and a test cannot sandbox its data root through
a variable.

**Every new front's stray sweep warned** that it found 7 or 8 browser processes it
could not attribute while a session was kept, 13 of 13; nothing was terminated. A
sweep allowed to act on processes it cannot attribute would end kept sessions.

**Cleanup, checked.** `Update.exe --uninstall --silent` exited 0 in 1.9 s; the task,
the PATH entry, the shortcut, the test's uninstall key and both scratch roots were
gone; the clearance snapshot, the list of 204 scheduled tasks, the real `mcpServers` in
`~/.claude.json` and `~/.codex/config.toml`, and the user PATH by kind, length and
SHA-256 were identical before and after; and the installed BrowserAI 1.1.0's eight
servers and their `node` children were still running with the same pids and creation
times.

**What it does not establish.** The one binary, which did not exist; any way the
task's start can fail, which is the gap that matters once nothing falls back; codex
0.155 in this measurement; either client's terminal UI; and whether the real
`~/.claude/` folder was touched by the real `claude.exe` the install's RegisterAI ran,
which was not compared.

**Re-establish it** with the batch's `rig/m2*` scripts and `rig/m2.js.txt` against a
test pack installed under the installer lock, with each client's driver from the
client-exit rig; compare against `runs/m2b/m2-runs.tsv` and the clearance snapshots
in `m2-snap/`.

## A pipe call between two of BrowserAI's processes, idle and under a full suite's load -- measured 2026-10-04

`[MACHINE]`. Windows 11 Pro 10.0.26300, BrowserAI **1.1.1-alpha.0.192** published from
`1ee00ec0` and installed by hand in scratch with its own pipe, census, host and log,
so that no suite arm could meet it. Callers were the product's own
`CoordinatorClient.SendAsync`, through a probe linking the published
`BrowserAI.Core.dll`, and fresh `BrowserAI.exe` second starts, which log their own
hand-over time; the probes waited up to 30 s, so a slow call was measured and not cut
off. Taken 2026-10-04 between 00:59Z and 01:56Z under the suite lock. Everything it
was read from:
[`docs/evidence/2026-10-04-startup-measure`](../../docs/evidence/2026-10-04-startup-measure/README.md),
`q381/`. It is what Q381 and D3 of [the one-binary design](../../docs/design/one-binary/README.md)
rest on: **whether 500 ms is a fair limit on one such call.**

| Caller | Idle: p50 / p99 / max (n) | Under load: p50 / p99 / max (n) | Over 500 ms under load |
|---|---|---|---|
| A separate process, warm, `recheck` | 0.55 / 12.9 / 29.9 ms (600) | 0.6 to 1.1 / 82 to 193 / 1,234 ms (1,852) | 1 |
| A separate process, warm, `host`, the host running | 0.56 / 1.9 / 22.8 ms (600) | 0.6 to 1.0 / 55 to 145 / 1,234 ms (1,879) | 1 |
| In process, as the test arm calls it, its pool idle | 0.48 / 9.7 / 32.5 ms (600) | 0.6 to 0.8 / 62 to 137 / 339 ms (1,856) | 0 |
| A fresh `BrowserAI.exe` second start | max 0.9 ms `recheck` (30), max 0.8 ms `host` (30) | p50 0.7, max 142.7 ms `recheck` (58); p50 0.7, max 55.1 ms `host` (57) | 0 |
| A fresh .NET process, JIT-compiled, one call | p50 21, max 36 ms (60) | p50 28 to 42, max 1,017 ms (115) | 2 |
| In process, with 32, 64 or 128 pool threads blocked | the first call 1,009 to 1,014 ms, 3 of 3; later calls under 50 ms | not run under load | -- |

"Under load" is two full suite runs on the same machine. **The two separate-process
calls over 500 ms happened at the same instant in two processes**, 01:46:53.666Z,
1,234 ms each: one machine-wide stall of about 1.2 s in about seven minutes of load.
**The suite's own arm went red in one of the two runs**, "did not answer 'recheck'
inside 500 ms" at 650 ms, while nine probe calls in other processes at that moment
took 0.5 to 0.7 ms. So the red came mostly from the test host itself, one .NET process
running about 900 tests whose arm blocks one pool thread while it waits for another,
on code compiled at first use, which the blocked-pool row shows costs about a second;
the product's precompiled callers stayed under 143 ms; and the machine can still stall
a call past 500 ms, rarely. With no process on the other end, opening the pipe fails at
once, 7 to 10 ms, "No pipe named ... exists".

**Re-establish it** with the batch's `probe/` and `rig/q381sum.py.txt` against a
scratch install's coordinator, idle and then under a full suite run, and the second
starts' own log lines; compare against `q381/idle/` and `q381/load/`.

## What the Task Scheduler does with a second run, a missing or disabled task, End, and a child left behind -- measured 2026-10-08

`[STABLE]` for the scheduler's behaviour, which no version this project floats can
move; `[MACHINE]` for every time. Windows 11 Pro **10.0.26300.9550** (26H2), an AMD
Ryzen 9 5950X; .NET SDK 10.0.401 with runtime 10.0.12. Taken 2026-10-08 between
14:10Z and 14:40Z with stand-ins built for it and no BrowserAI binary: a NativeAOT
Windows-subsystem stand-in that logs every window message, thread message, console
event and its own exit, and a driver that talks to the scheduler through
`Schedule.Service`, the same methods BrowserAI calls through its own vtables. Every
task was registered with the sign-in task's principal and settings
(`SignInTask.DefinitionFor`: the user by SID, least privilege, no battery
conditions, start on demand, no time limit, priority 5, `Parallel`), changed per
case and with no trigger, and removed afterwards. Everything it was read from, with
the stand-in and the driver:
[`docs/evidence/2026-10-08-step0`](../../docs/evidence/2026-10-08-step0/README.md),
`tasks/`. It is what the one-binary design's "never a second copy", its errors for a
missing and a disabled task, and its rule never to stop the background with End rest
on.

**Microsoft documents three of the four answers only in part**, read 2026-10-08:
`IgnoreNew` "Does not start a new instance if an existing instance of the task is
running"
([MultipleInstancesPolicy](https://learn.microsoft.com/windows/win32/taskschd/taskschedulerschema-multipleinstancespolicy-settingstype-element)),
and no page says what a `Run` it ignores returns; End sends `WM_CLOSE` and then calls
`TerminateProcess` when `AllowHardTerminate` is true, its default
([AllowHardTerminate](https://learn.microsoft.com/windows/win32/api/taskschd/nf-taskschd-itasksettings-get_allowhardterminate)),
and no page says how long it waits, which windows get the message, or what happens to
children; and a disabled task's `Run` returns `SCHED_E_TASK_DISABLED`
([IRegisteredTask::Run](https://learn.microsoft.com/windows/win32/api/taskschd/nf-taskschd-iregisteredtask-run)).

### `IgnoreNew` starts one instance from twenty requests at once, and answers every one of them `S_OK`

| Case | Rounds | Instances started per round | What each of the 20 requesters got back |
|---|---|---|---|
| `IRegisteredTask::Run`, nothing running | 10 | **1, in 10 of 10** | `S_OK`, each with its own running-task object and instance id, 20 distinct per round. The one that started read `State` 4 with `EnginePID` the stand-in's pid; the other 19 failed every read and `Refresh` with `0x8004130B` (`SCHED_E_TASK_NOT_RUNNING`) |
| `Run`, one already running | 10 | **0, in 10 of 10** | `S_OK` from all 20, each running-task object failing with `0x8004130B` |
| `schtasks /run`, nothing running | 10 | **1, in 10 of 10** | exit 0 from all 20 and `SUCCESS: Attempted to run the scheduled task`; 15 to 19 of the 20 printed `INFO: ... is currently running.` before it |
| `schtasks /run`, one already running | 10 | **0, in 10 of 10** | exit 0 from all 20, each printing both lines |

The COM requests were 20 processes released by one named event, entering `Run`
within 0.1 to 2.7 ms of each other, each call taking 3.5 to 55.7 ms, and the winner
changed from round to round. ⚠️ **A requester cannot tell from the result whether it
started the instance**: the `HRESULT` and the exit code are the same for the winner
and the ignored, and only the returned object's `State` tells, through COM; through
`schtasks` nothing does.

### A missing task and a disabled one, 5 runs each

| Request | Result | Anything started |
|---|---|---|
| `ITaskFolder::GetTask` on a name never registered | `0x80070002`, "The system cannot find the file specified.", in 0.2 to 5.3 ms | no |
| `schtasks /run` on that name | exit 1, `ERROR: The system cannot find the file specified.` | no |
| `Run` on a task object taken before the task was deleted | `0x80070002` | no |
| `Run` on a disabled task | `0x80041326`, "The task is disabled." | no |
| `RunEx` with `TASK_RUN_IGNORE_CONSTRAINTS` on it | `0x80041326` | no |
| `schtasks /run`, and `schtasks /run /i`, on it | exit 1, `ERROR: The scheduled task "<name>" could not run because it is disabled.` | no |

The disabled task read `State` 1 and `Enabled` false, and its `LastTaskResult` stayed
`0x00041303`, never run.

### End and Stop close the process's top-level windows, then terminate it about a second later, and touch nothing else

31 requests against a running instance whose stand-in had a message-only window, and
in two variants a hidden top-level window as well, and which had started one ordinary
child with the same windows:

| Request | The stand-in's windows | Runs | The request returned | The stand-in ended after the request | Exit code | It received first | Its child |
|---|---|---|---|---|---|---|---|
| `schtasks /end` | message-only | 5 | exit 0, 41.2 to 73.2 ms | 1,039.2 to 1,071.0 ms | `0x42B` | nothing | alive 5 of 5 |
| `IRunningTask::Stop` | message-only | 6 | `S_OK`, 22.3 to 29.2 ms | 1,028.4 to 1,051.9 ms | `0x42B` | nothing | alive 6 of 6 |
| `schtasks /end` | and a hidden top-level window that exits on `WM_CLOSE` | 5 | exit 0, 40.1 to 76.5 ms | 57.1 to 96.5 ms | 16, its own | `WM_COMMAND`, `WM_COMMAND`, `WM_CLOSE`, twice | alive 5 of 5 |
| `IRunningTask::Stop` | the same | 5 | `S_OK`, 20.0 to 24.1 ms | 44.7 to 51.8 ms | 16 | the same | alive 5 of 5 |
| `schtasks /end` | and a hidden top-level window that ignores `WM_CLOSE` | 5 | exit 0, 38.8 to 61.7 ms | 1,045.2 to 1,051.2 ms | `0x42B` | the same | alive 5 of 5 |
| `IRunningTask::Stop` | the same | 5 | `S_OK`, 18.4 to 34.1 ms | 1,024.2 to 1,036.6 ms | `0x42B` | the same | alive 5 of 5 |

⭐ **A process with no top-level window gets no notice at all**: no window message,
no thread message, no console event and no `ProcessExit`, and it is terminated 1.02 to
1.07 s after the request with exit code `0x42B` (1067, `ERROR_PROCESS_ABORTED`). One
with a top-level window receives, within 45 to 96 ms, `WM_COMMAND` (wParam 0,
lParam 7), `WM_COMMAND` (wParam 0, lParam 2) and `WM_CLOSE`, and the same three again
about a millisecond later; a message-only window received nothing in any of the 31.
A process that exits on `WM_CLOSE` keeps its own exit code, and one that ignores it is
terminated on the same deadline. **The child was ended in 0 of 31**, though it had a
hidden top-level window of its own: End and Stop end the task's process, never its
tree and never its job. After every request the task read Ready, no instance, and
`LastTaskResult` `0x00041306`, `SCHED_S_TASK_TERMINATED`. Which process sends the
messages was not established: none of the scheduler service's binaries imports
`PostMessageW`, and `taskhostw.exe` does.

### A child the task's process leaves behind is not ended, and the task no longer counts as running

The stand-in started a child and exited 500 ms later with code 0, and the task was
read once a second for 150 s, 5 runs per way of starting the child:

| How the child was started | Ended by the scheduler | Alive at the end of the 150 s |
|---|---|---|
| ordinary, `CREATE_NO_WINDOW`, as `Process.Start` does | never | 5 of 5 |
| detached, `DETACHED_PROCESS` and `CREATE_NEW_PROCESS_GROUP` | never | 5 of 5 |
| detached and `CREATE_BREAKAWAY_FROM_JOB` | never | 5 of 5 |

**The breakaway was refused 5 of 5**, `CreateProcess` failing with error 5, so all 15
children ran inside the scheduler's job, and **none of the 15 was ended**, at any of
2,217 samples from the stand-in's exit to 150 s after it. From the first sample the
task read Ready, no instance, `LastTaskResult` 0: an instance ends when the action's
own process exits, and the scheduler does not follow that process's children. So a
child the task-started process starts and then exits for, such as `Update.exe`, keeps
running untouched. A run request made while such a child lives was not measured.

### The scheduler puts every process it starts in the session into one job

The job the stand-ins started in held, besides them, seven processes this
measurement did not start: two the scheduler started at the 2026-10-03 sign-in, four
children of one of those, and one more program. Its counters read 2,500 processes in
total at 14:10Z and 2,730 at 14:39Z, limit flags `0x0`: **one job for every task's
process in the session, shared, with no breakaway**. That is why a breakaway is
refused, and it names the "six other processes" the 2026-10-03 entry above could not.
End and Stop were run against it only behind a gate: the scheduler's own binaries
were read first, and none of `schedsvc.dll`, `ubpm.dll`, `taskcomp.dll` and
`wptaskscheduler.dll` imports or contains a call that terminates a job; and before
every request the stand-in's job had to hold only the stand-in, its child and the
seven recorded pids, and after it all seven had to be alive with unchanged start
times. They were, after each of the 31 and at the end. An S4U task, which would have
had a job of its own in session 0, was refused to the non-elevated token,
`0x80070005`, 4 of 4.

### The scheduler's own share of a start is about 4 ms

| Path | Runs | From the run request to the stand-in's first log line | Medians |
|---|---|---|---|
| `Run`, one request at a time | 20 | 18.4 to 49.1 ms, median 19.6 | `Run` returned in 1.1 ms; the process created at 3.7 ms; its start to `Main` 15.1 ms; `Main` to the line 0.7 ms |
| `schtasks /run`, timed from launching `schtasks.exe` | 10 | 43.5 to 85.1 ms, median 67.6 | `schtasks.exe` exited at 27.9 ms; the process created at 53.4 ms; its start to `Main` 12.4 ms |
| `Run`, the winner of a twenty-way race | 10 | 24.3 to 50.3 ms after the release, median 30.0 | |
| `schtasks`, the winner of a twenty-way race | 10 | 78.0 to 124.5 ms, median 105.4 | |

`EnginePID` was the stand-in's own pid, 20 of 20. Set against the 514 to 674 ms the
real programs took on 2026-10-04 (the entry above), almost all of that is the program
starting after it was created.

### Sign-out, read 2026-10-08 and not measured

An interactive-token task runs "only in an existing interactive session"
([LogonType](https://learn.microsoft.com/windows/win32/taskschd/taskschedulerschema-logontype-principaltype-element)),
and at sign-out every process in the session ends. A process with no visible window
is told only through a hidden top-level window that handles `WM_QUERYENDSESSION` and
`WM_ENDSESSION`, created with `CreateWindowEx` and a `dwExStyle` of 0
([SetConsoleCtrlHandler](https://learn.microsoft.com/windows/console/setconsolectrlhandler));
`CTRL_LOGOFF_EVENT` "is received only by services"
([HandlerRoutine](https://learn.microsoft.com/windows/console/handlerroutine)); a
message-only window "does not receive broadcast messages"
([window features](https://learn.microsoft.com/windows/win32/winmsg/window-features));
and such a process "cannot cancel shutdown" and is ended if it has not answered within
5 seconds
([shutdown changes for Windows Vista](https://learn.microsoft.com/windows/win32/shutdown/shutdown-changes-for-windows-vista)).
`WM_QUERYENDSESSION` never arrived in the measurement above, because nothing signed
out.

**Re-establish it** with the batch's `tasks/src/` (the stand-in and the driver, both
as `.txt`), which registers each task, runs each case and removes the task whatever
happens; take the measurement of End only behind the same gate, because the job it
runs in is shared with processes nobody here started. Compare against each case's
`results.tsv` or `summary.tsv` under `tasks/runs/`.

## Reading the window in front and the time of the last input -- measured 2026-10-08

`[MACHINE]`. The same machine and day as the entry above, a stand-in and no hooks of
any kind. It is what the one-binary design's check of a person's input in a visible
window rests on (F4): **two reads per tick, however many windows are open, and what
they cost.**

One tick is `GetForegroundWindow`, `GetWindowThreadProcessId` and `GetLastInputInfo`.
`QueryPerformanceCounter` resolves only 100 ns on this machine and a tick is shorter,
so ticks were timed with a serialised `rdtsc` calibrated against it, at 3.40 GHz, in
10 ns steps; two clock reads cost a median of 20 ns and are inside every figure.

| Run | Ticks | Median | p99 | Mean |
|---|---|---|---|---|
| a tight loop, started directly | 5 x 100,000 | **50 ns**, 5 of 5 | 90 to 100 ns | 49.4 to 53.9 ns |
| a tight loop, started by a task | 5 x 100,000 | 50 ns, 5 of 5 | 90 ns, 5 of 5 | 48.3 to 51.3 ns |
| one tick every 2 s for 300 s | 5 x 150 | **2.22 to 2.43 microseconds** | 12.4 to 360 microseconds | 4.2 to 10.1 microseconds |

Per call in the tight loop, `GetForegroundWindow` took a median of 30 ns,
`GetWindowThreadProcessId` 30 ns and `GetLastInputInfo` 20 to 30 ns. The 750 ticks at
the 2 s pace, pooled, had a median of 2.34 microseconds, a p99 of 65.2 and a maximum
of 734. **Over the five minutes the calls cost about 0.7 ms of CPU**:
`QueryProcessCycleTime` read 3.13 to 3.45 ms for the ticking processes against 2.38
and 2.72 ms for controls that woke every 2 s and called nothing, while
`GetProcessTimes` read zero for all of them, below its 15.625 ms step. Waking every
2 s costs about 2.5 ms on its own. No tick read a missing foreground window.

**What Microsoft documents about the reads**, read 2026-10-08: `GetLastInputInfo` is
session-wide for the calling session and its tick "is not guaranteed to be
incremental"
([GetLastInputInfo](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getlastinputinfo));
its `dwTime` is a 32-bit tick count that wraps after 49.7 days, so an idle time is a
32-bit difference against `GetTickCount`
([GetTickCount](https://learn.microsoft.com/windows/win32/api/sysinfoapi/nf-sysinfoapi-gettickcount));
"The foreground window can be NULL in certain circumstances"
([GetForegroundWindow](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getforegroundwindow));
hooks "tend to slow down the system"
([hooks overview](https://learn.microsoft.com/windows/win32/winmsg/about-hooks)); and a
periodic timer costs power, where a coalescable one is recommended
([waitable timer objects](https://learn.microsoft.com/windows/win32/sync/waitable-timer-objects)).
No page says what the three reads cost.

**Re-establish it** with the batch's `tasks/src/` tick modes against a no-op control,
reading `QueryProcessCycleTime` for both; compare against `tasks/runs/.../m5/`.

## The task scheduler from a NativeAOT process, through COM -- measured 2026-09-24

`[STABLE]` for what the scheduler does and `[MACHINE]` for every time and size; the two
toolchain refusals at the end float, because they are properties of the SDK and of the
generator and not of Windows, and they are marked where they stand. Windows 11 Pro
**10.0.26200**, .NET SDK
**10.0.401**, NativeAOT win-x64, a non-elevated token. Measured by the writer of the
coordinator core with a scratch rig between 22:22Z and 22:23Z on 2026-09-24, which was
already 2026-09-25 on this machine's clock, and the product's remarks carry that local
date. It decided how the per-user logon task of Q282 a is registered and started:
through the scheduler's own COM interface, in
[`Interop/TaskScheduler.cs`](../../src/BrowserAI.Core/Interop/TaskScheduler.cs) and
[`Registration/LogonTasks.cs`](../../src/BrowserAI.Core/Registration/LogonTasks.cs).

**The rig.** A NativeAOT console driver registered a task in the scheduler's root
folder from XML -- a logon trigger and an interactive-token principal, both naming the
user by SID, `MultipleInstancesPolicy` `Parallel`, `ExecutionTimeLimit` `PT0S`,
`Priority` 5 -- read it back, ran it on demand twice and deleted it. The action was a
NativeAOT Windows-subsystem probe that writes its own command line beside itself and
exits, at a path with a space in it, written into `<Command>` unquoted. The driver ran
twice: once with its main thread in the multi-threaded apartment, once with
`[STAThread]`.

| What | Result |
|---|---|
| `CoCreateInstance` of `CLSID_TaskScheduler`, then `Connect` | `S_OK` in 2.8 ms; connected at 15.6 ms |
| `RegisterTask` from XML into `\`, non-elevated | **21.9 ms** in the first run, **19.8 ms** in the second |
| `IRegisteredTask::Run` with one `BSTR`, `--coordinate` | returned in 1.0 to 1.1 ms, and the probe recorded `--sign-in --coordinate` |
| `IRegisteredTask::Run` with `VT_EMPTY` | returned in 1.2 to 1.3 ms, and the probe recorded `--sign-in $(Arg0)`: with no value the placeholder is passed as it is written |
| `DeleteTask` | 2.4 to 2.5 ms |
| `GetTask` or `DeleteTask` of a task that is not there | `0x80070002`, which .NET raises as `FileNotFoundException` |
| `CoInitializeEx` for the MTA, on a main thread the runtime had already put there | `S_FALSE` |
| `CoInitializeEx` for the MTA, on an `[STAThread]` main thread | `RPC_E_CHANGED_MODE`, `0x80010106`, and every call above still worked |
| a `<Command>` holding a space, unquoted | the probe started, and its own command line showed the path quoted |

**What the scheduler stores is not what it was given.** Read back with `GetXml`, the
trigger's `<UserId>` had become `DOMAIN\user` where the principal kept the SID; every
element equal to its default was gone (`Enabled`, `RunLevel` `LeastPrivilege`,
`AllowStartOnDemand`); and it had added a `<URI>` naming the task and an
`<IdleSettings>` block and moved `<Settings>` ahead of `<Triggers>`. A comparison of the
definition a hook wrote with the definition the scheduler holds compares two different
documents. The gate's clearance hashes the stored XML, read the same way either side of
a run.

**Only `IRegisteredTask::Run` passes a value to the action.** Read 2026-10-03, not
measured: the documentation of
[`IRegisteredTask::Run`](https://learn.microsoft.com/windows/win32/api/taskschd/nf-taskschd-iregisteredtask-run)
pairs a single `BSTR` with the name `Arg0` and substitutes it wherever an action
property says `$(Arg0)`, and
[`schtasks /run`](https://learn.microsoft.com/windows-server/administration/windows-commands/schtasks-run)
takes a task name, a computer and a user, and no value. The two `Run` rows above are
the first half measured, and the second is what the page does not say: with no value,
the placeholder arrives literally. A logon trigger supplies no value either, so a start
at sign-in is expected to carry `$(Arg0)` as written, an argument the configuration app
does not know and ignores. That start was not run, because it takes a real sign-in.

**A task started on demand is the scheduler's child, and this user cannot open its
parent.** `CoordinatorWakeTests.ATaskStartedOnDemandRunsTheAppWithCoordinateAsTheSchedulersChild`
measured it on 2026-09-24 against the published configuration app: the process the
task started had as its parent the pid the service control manager gives for the
`Schedule` service, and that parent's image could not be read. The writer opened the
same pid by hand with the query right the same night and was refused with error 5.
That is the whole of Q283 a's reason: a process whose parent is the scheduler is
outside every client's process tree, and `taskkill /T`, which ends a process and the
processes it started, does not reach it from a client's server.

**What the COM code costs the binaries** `[MACHINE]`, on this tree's publishes: the
configuration app went from **10,674,176 to 10,834,944 bytes (+160,768)** when the
task-scheduler interop arrived with the logon task, and the server from **19,402,752
to 19,592,192 bytes (+189,440)** when the blocked server's wake linked it. At the end
of the coordinator core the app was **10,914,304** bytes and the server **19,592,704**.

**Two toolchain refusals, and why `VARIANT` is written by hand** `[FLOATS]`, both
measured by the writer on 2026-09-24 at SDK 10.0.401 and `Microsoft.Windows.CsWin32`
**0.3.335**; the build output was not kept, and the second message is quoted in
[`NativeMethods.txt`](../../tests/BrowserAI.Tests/NativeMethods.txt). The framework's
`ComVariant` cannot cross a `[GeneratedComInterface]` method by value without
`DisableRuntimeMarshallingAttribute` on the whole assembly, `error SYSLIB1051`. And
CsWin32 refuses to generate `VARIANT` in its COM-interface mode, `error PInvoke003:
This API will not be generated. Use object instead of VARIANT when in COM interface
mode`, so the layout oracle cannot supply it either. The product declares a 24-byte
blittable `TaskSchedulerInterop.Variant`, and `InteropLayoutTests` holds it against the
size of `ComVariant`, the way `NativeFile.Overlapped` is held against the framework's
`NativeOverlapped`: the same two refusals, met first for `OVERLAPPED`
([above](#locking-a-log-file-without-locking-its-readers-out----2026-08-24)).
Re-verification row 116a.

**Re-establish it** with a task named for the suite's test pack and removed in the same
run: `SignInTaskTests.TheRealSchedulerKeepsTheTaskAndRemovesItAgain` registers and
reads back a real definition, and the wake arm above starts one on demand. The rig
itself is not kept; it is one `RegisterTask`, one `GetXml`, two `Run` calls and one
`DeleteTask` against the root folder, timed with a stopwatch, and an action that
writes `Environment.CommandLine` to a file.

### An exited process is not in the process list, even while its handle is held -- measured 2026-09-24

`[STABLE]`. Windows 11 Pro 10.0.26200, PowerShell 7, at about 23:36Z on 2026-09-24 by
the file's own time; the line the rig printed says 2026-09-25, which was the local
date. A `cmd.exe /d /c exit 3` was started and its handle held open by the script for
the whole reading. After it exited, **it was not in `EnumProcesses`' list**;
`OpenProcess` on its pid with `PROCESS_QUERY_LIMITED_INFORMATION` and `SYNCHRONIZE`
still succeeded; **`QueryFullProcessImageNameW` on that handle failed**; and a wait on
it with a zero timeout was already signalled. So a scan that lists processes and then
reads each one's image cannot find a process that has exited, whoever still holds it,
and `BrowserProcesses.HeldUnder` carries no filter of its own for one. It was measured
because a planted defect that counted exited processes stayed green.

**Re-establish it** with the same four calls against any child you start and keep a
handle to: list, open, query the image, wait with a zero timeout.

### One wait holds 64 handles -- measured 2026-09-25

`[STABLE]` for the limit. `WaitHandle.WaitAny` over 65 handles threw
`NotSupportedException`, *"The number of WaitHandles must be less than or equal to
64."*, at once, measured by a planted defect in the coordinator's loop at 00:31Z on
2026-09-25. That is why `CoordinatorLoop.ProcessesPerWait` is 63: the pipe's inbox is
the 64th, and a pass holding more processes than that waits on the first 63 and still
holds and counts the rest. `CoordinatorTests.MoreProcessesThanOneWaitCanHoldAreAllWaitedForSixtyThreeAtATime`
is the arm.

## A new environment's Path is the machine's entries, then the user's, and a running program keeps its own -- measured 2026-09-24

`[MACHINE]` for the counts; `[STABLE]` for what a running program keeps, which is
documented. Windows 11 Pro **10.0.26200**, three reads with nothing writing either value.
**`CreateEnvironmentBlock` for this session's own token, with `bInherit` false, returned a
`Path` that is exactly the machine's entries and then the user's**: the 31 non-empty entries
of `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment\Path`, expanded, in order,
then the 20 of the 22 non-empty entries of `HKCU\Environment\Path` that the machine value
does not already hold -- 51 entries, compared case-insensitively as one string, 3 of 3.
The user value ends in `;`, and the block holds no empty entry for it. A first read, taken
while a real-installer arm had its own entry on the user value, agreed on the order and is
not counted. **`UserPath.SearchDirectories` reads the two values in that order**, so the
first folder it finds holding a name is the first a new program would find: dropping a user
entry the machine value already holds never changes which folder comes first.

**What a running program keeps** `[STABLE]`: *"By default, each process receives a copy of
the environment block for its parent process"*
([User Environment Variables](https://learn.microsoft.com/windows/win32/shell/user-environment-variables)),
and the documented way to publish a change to the registry values is a `WM_SETTINGCHANGE`
broadcast with `lParam` set to `"Environment"`, which *"allows applications, such as the
shell, to pick up your updates"*
([Environment Variables](https://learn.microsoft.com/windows/win32/procthread/environment-variables)).
So a program started before the install hook wrote BrowserAI's entry holds a Path without
it, and so does everything it starts, unless it acts on the broadcast. **That a Codex
already running does not act on it is read from its launcher and not measured**, which is
why [the hazard index](../../HAZARDS.md#hazard-index) carries it open and README tells a
person to restart Codex after installing.

**What the broadcast costs** `[MACHINE]`: `SendMessageTimeoutW` to `HWND_BROADCAST` with
`WM_SETTINGCHANGE`, `"Environment"`, `SMTO_ABORTIFHUNG` and a 1,000 ms timeout -- the call
`EnvironmentBroadcast` makes -- returned nonzero 5 of 5 and took **261 to 436 ms**, with
669 top-level windows on the machine by `EnumWindows`. The documented ceiling is the
timeout once per window that is slow to answer, *"up to the value of uTimeout multiplied
by the number of top-level windows"*
([SendMessageTimeoutW](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw)),
and the hooks that send it run under Velopack's own timeouts, 15 s for the update hook
([velopack](../packaging/velopack.md#nativeaot-hooks-and-vpk-output)).

**How to re-establish it:** open the session's own token with `TOKEN_QUERY`, call
`CreateEnvironmentBlock` with `bInherit` false, walk the block to `Path=`, and compare it with
both registry values read with `DoNotExpandEnvironmentNames` and expanded. Take it while
nothing is installing: every real-installer arm writes the user value and takes it back.
For the cost, time the same `SendMessageTimeoutW` call; it changes no value, and every
install and uninstall of the suite's test pack sends it anyway.

## The Win32 interop surface

**`NtQueryInformationProcess` reads a parent PID in ~0.77 µs/call**, against
~3.3 ms for `Process.GetProcessById` and milliseconds for WMI. `dotnet/runtime`
itself uses it. `[MACHINE]` for the numbers, `[STABLE]` for the API.

**`LibraryImport` does not support `StringBuilder`**, so a `CreateProcessW`
command line must be passed as a writable `char[]`/`Span<char>` -- the API mutates
the buffer, and a `string` literal is not valid. `[STABLE]`

**`[DllImport]` works under NativeAOT on Windows, and ILC generates its
marshalling stubs ahead of time.** `Corrected 2026-08-17` (previously
*"`DllImport` is the wrong choice under NativeAOT because it relies on runtime
IL-stub generation"*, carried here as `[STABLE]` and never measured). Measured
2026-08-17 on **SDK 10.0.400 / ILC 10.0.11**, `win-x64`, `net10.0-windows`: a
probe with **38 `[DllImport]` declarations** across kernel32, user32, ntdll and
rstrtmgr -- `SetLastError = true` throughout, and including `StringBuilder`
marshalling, `SafeHandle` returns, struct byref and a managed callback delegate
passed to `EnumWindows` -- published with **zero trim or AOT warnings** and
passed all **41** runtime checks, in a 1,209,856-byte binary. `SYSLIB1054` did
not fire at default analyzer settings either.

**All seven hand-written interop structs match Microsoft's own Win32 metadata
exactly.** Measured 2026-08-17 against `Microsoft.Windows.CsWin32` **0.3.298**,
which generates from the same metadata Windows ships: `STARTUPINFOW` **104**,
`STARTUPINFOEXW` **112**, `PROCESS_INFORMATION` **24**, `SECURITY_ATTRIBUTES`
**24**, `IO_COUNTERS` **48**, `JOBOBJECT_BASIC_LIMIT_INFORMATION` **64**,
`JOBOBJECT_EXTENDED_LIMIT_INFORMATION` **144**; `offsetof(Affinity)` **48**,
`offsetof(LimitFlags)` **16**, `offsetof(IoInfo)` **64**, agreeing both ways.
**A size check alone is not sufficient and this was demonstrated, not
argued**: reordering `Affinity` after `PriorityClass`/`SchedulingClass` leaves
the struct at 64 bytes and slides that field to offset 56, so the size assertion
passes and only the offset assertion fails. `[STABLE]` -- these are the x64
Windows ABI and do not move. Re-establish by running
`InteropLayoutTests`, which is now a permanent guard and not a
re-measurement, and which reads the shipped `private` nested types by reflection
instead of copies of them.

**This does not change the rule, only its reason.** `[LibraryImport]` remains
correct here because it is Microsoft's documented first recommendation for .NET
7+, because the marshalling it emits is ordinary C# that can be read and stepped
through, and because `SYSLIB1054` exists to move code toward it. What the old
sentence would have caused is a wrong answer to a *different* question: a
generator that emits `[DllImport]` (which is what `Microsoft.Windows.CsWin32`
does, and will keep doing -- [#593](https://github.com/microsoft/CsWin32/issues/593)
and [#1333](https://github.com/microsoft/CsWin32/issues/1333) are both closed
*not planned*) is **not** ruled out by AOT. CsWin32 #1333's own
opening post repeats the same misconception, which is a fair guess at where it
entered this repository. `[STABLE]` -- re-establish by publishing any AOT project
containing a `[DllImport]` and reading the ILC output. The probe used here was
38 declarations across `kernel32`, `user32`, `ntdll` and `rstrtmgr` with
`SetLastError=true`, covering `StringBuilder` marshalling, `SafeHandle` returns,
struct byref and an `EnumWindows` callback delegate; it published with zero
warnings and passed all 41 runtime checks. **The shape is recorded and not
the path** -- the probe lived outside the repository and a path nobody else can
open is not a re-establishment route.

**`Environment.GetFolderPath(SpecialFolder.UserProfile)` does not read
`%USERPROFILE%`.** It resolves from the process token, so overriding the
environment variable in a child's block moves nothing. Measured 2026-08-16 while
trying to simulate a machine with no MCP client on it: the child was started with
`USERPROFILE` pointed at an empty scratch directory and `PATH` cut to `system32`,
and it still found the client at `<user profile>\.local\bin\claude.exe`, resolved
from the token, not from either variable. **The attempt failed and
the run is still evidence** -- it proves the `PATH`-independent fallback in
`ClientCommandLine` is load-bearing and not decorative, because with `PATH`
stripped that fallback is what completed the registration. `[STABLE]` -- a Win32
known-folder property. Consequence: **a clientless machine cannot be simulated
from the environment**; the client-absent path is exercised through the
`IRegistrationCommand` seam instead, and `Locate` returning `null` for a name that
is genuinely not on this machine is asserted by
`RegistrationTests.TheClientIsLocatedByFileNameAndNeverAsAShim`. *Corrected
2026-10-03, by addition: the three names in that paragraph went to RegisterAI with
the rest of BrowserAI's registration code. RegisterAI keeps the same fallback,
`%USERPROFILE%\.local\bin`, and reads the profile from the token the same way;
its own tests simulate a clientless machine through the machine they hand it, and
BrowserAI's through `FakeRegisterAi`, which can leave a client missing.*

**A COM/interop enum value the running OS does not know throws on assignment** --
at the property set, not at load and not at compile time. The managed enum is only
an integer; the rejection happens inside the COM object receiving it, so the
compiler, the interop layer and any static analysis all see a valid value. Shipped
mitigation, read 2026-08-16 in an unpublished VB.NET Windows Update client, which
wraps `UpdateDownloader.Priority = DownloadPriority.dpExtraHigh` -- a Windows Update
Agent value newer than the OS floor that project targeted -- in a try/catch that
logs *"Switching from ""dpExtraHigh"" priority to ""dpHigh"" priority due to OS
incompatibility"* and downgrades. **Directly applicable here:** the job-object
information classes and the `NtQueryInformationProcess` information classes this
project P/Invokes are the same shape, so every information class used must either
be safe at our Windows floor or carry an explicit downgrade path -- a value that is
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
([DECISIONS](../../DECISIONS.md)). `[STABLE]`.
Re-establish by starting any long-running exe from a nested directory with its
cwd elsewhere, then renaming the parent, the grandparent and the image.

⚠️ **And it does not generalise to Chromium, which is the case the design row is
actually about. Measured 2026-08-19** *(previously this paragraph ended "what a
browser does when its tree is renamed underneath it has **not** been measured --
but the option is open rather than impossible", and it is the second half of that
sentence that turns out to be too broad)*. A live headless Chromium 152.0.7977.8
from `chromium-1237`, started with its own current directory deliberately in the
repository root and running as **ten processes**:

| Operation, while that Chromium is live | Outcome |
|---|---|
| `Directory.Move` of `chrome-win64` -- the directory holding `chrome.exe` | **refused**, `IOException`, *"being used by another process"* (sharing violation) |
| `Directory.Move` of `chromium-1237` -- the revision directory above it | **refused**, `IOException`, *"Access to the path ... is denied"* |
| the same two renames, browser killed first | **both succeeded** |

**The control is the load-bearing half**: with the browser gone both renames
succeed in the same script, in the same second, so the refusal is Chromium's and
not an ambient condition on the tree. **What it means for the design option:**
swapping a browser tree *under a live browser* is not available, which is what
`browserai_reinstall_browser`'s refusal already assumes; swapping one while
nothing is running works, and that is the only state the tool acts in anyway.
`[MACHINE]` for the process count, `[FLOATS]` for the browser revision.
Re-establish with
[`docs/probes/2026-08-19-rename-under-browser/rename-under-chromium.ps1`](../../docs/probes/2026-08-19-rename-under-browser/README.md)'s
shape: start the
provisioned `chrome.exe` headless with a scratch `--user-data-dir`, try both
renames, then kill it and try both again as the control. **Re-run 2026-08-19 and
reproduced exactly** -- ten processes, both refusals, both controls -- so the entry
above is reproducible from what is written and not only from the day it was
taken.

### The same measurement for Firefox, and for what both families share -- 2026-08-19

⚠️ **Corrected 2026-08-19 (previously this entry ended "Measured for Chromium
only; Firefox was not tested").** It has been, the same way and on the same day,
and so have the two shared component trees and the browsers root itself, because
*"a live browser holds its tree"* is a claim about a product that provisions two
families and four directories, not about one of them.

**Firefox matches Chromium exactly, refusal for refusal and error for error.**
Measured 2026-08-19 on Windows **10.0.26200**, .NET 10, against a live headless
Firefox **153.0** (playwright firefox **v1539**, BuildID 20260723193615) started
from `firefox-1539\firefox\firefox.exe` with its own current directory
deliberately in the repository root, so [the separate cwd
rule](#files-durable-writes-and-deletes) could not be the cause. Two independent
runs, at five and seven processes:

| Operation, while that Firefox is live | Outcome |
|---|---|
| `Directory.Move` of `firefox` -- the directory holding `firefox.exe` | **refused**, `IOException`, *"being used by another process"* (sharing violation) |
| `Directory.Move` of `firefox-1539` -- the revision directory above it | **refused**, `IOException`, *"Access to the path ... is denied"* |
| the same two renames, browser killed first | **both succeeded** |

Same two operations, same two *different* Win32 errors, in the same order, and
the same control. **So there is no product finding here and nothing to change:**
the family-agnostic refusal in `browserai_reinstall_browser` was already the right
shape for both families, and it now rests on a measurement of both and not on
a generalisation from one.

**The layouts are asymmetric, and a re-run has to get them right.** Chromium
is `chromium-<rev>\chrome-win64\chrome.exe`; Firefox is
`firefox-<rev>\firefox\firefox.exe`. *The directory holding the executable* is
`chrome-win64` for one and `firefox` for the other, and it is the inner directory
that takes the sharing violation in both.

**The liveness signal is not the same for the two, and the Firefox one is
weaker.** Chromium's arm proves the browser is up over its DevTools HTTP endpoint --
`/json/version`, plus creating a new target. **Playwright's Firefox build never
brings a Remote Agent up**: `--remote-debugging-port 9413` was passed and
`http://127.0.0.1:9413/json/version` never answered across 150 attempts over 30 s,
while the browser was demonstrably alive the whole time. Playwright drives its
Firefox over the **juggler** pipe and not CDP, so there is no HTTP endpoint to
ask. The Firefox arm therefore proves liveness by process tree -- the parent plus
its content and GPU children, four to six of them -- which is a real browser but is
not a protocol handshake. `[STABLE]` for the refusals; the absent remote agent is
`[FLOATS]` against the Playwright Firefox build.

**The shared components behave in the opposite way, and that is the new fact.**
Measured 2026-08-19 with a live browser of each family in turn:

| Operation, while a browser of that family is live | Chromium live | Firefox live |
|---|---|---|
| `Directory.Move` of `ffmpeg-1011` | **succeeded** | **succeeded** |
| `Directory.Move` of `winldd-1007` | **succeeded** | **succeeded** |
| `Directory.Move` of the browsers **root** itself | **refused**, *"Access to the path ... is denied"* | **refused**, *"Access to the path ... is denied"* |

**Neither shared tree is held by a running browser**, because neither is running:
`ffmpeg-win64.exe` exists only while a recording is in flight and `winldd` is an
install-time dependency validator. So a shared-component swap under a live browser
is available where a browser-tree swap is not -- which is a genuine asymmetry in
what the reinstall tool *could* do, and is recorded, not acted on. **It does
not license widening `shared`'s refusal**, which is deliberately wider than a
family's for a different reason: *a process is running from this tree* and *a
session is using it* are the same question for a browser and are not for `ffmpeg`
([DECISIONS](../../DECISIONS.md#open-design-decisions)).

**The mechanism is still `[UNVERIFIED]`, and this run made it stranger, not
clearer.** A directory cannot be renamed while a process has it as a current
directory, or while anything holds a handle to it without `FILE_SHARE_DELETE`;
which of those the browsers are doing is still not established, and the two
refusals carrying *different* Win32 errors still says they are not the same cause.
**What is new is that the refusal reaches further up than the plain-executable
measurement predicts.** That one found `Directory.Move` of a running image's
**grandparent** succeeded; here the grandparent of the executable's directory --
the browsers root -- is refused for both families, and refused with the *same*
error as the revision directory. So whatever a browser holds, it is not just the
directory its image sits in, and the general rule for a running `.exe` does not
describe it at any level.

Re-establish with
[`rename-under-firefox.ps1` and `rename-shared-components.ps1`](../../docs/probes/2026-08-19-rename-under-browser/README.md),
which are the Chromium script's shape with
the paths and the liveness check changed. **Both restore what they renamed in a
`finally`, and both re-assert the executables are present at the end** -- they
rename the *shared* provisioned browsers root that every browser-touching test on
the machine reads, so a script that dies half-way breaks the suite instead of
failing its own assertion. That is also why none of this is automated: see
re-verification row 103.

> ✅ *Verified 2026-10-03 @ Chrome for Testing 155.0.8059.12 (`chromium-1247`)
> and Firefox 156.0 (`firefox-1553`).* **Every refusal and every control holds,
> error for error.** A live headless Chromium at eleven processes refused
> `chrome-win64` with the sharing violation and `chromium-1247` with *access
> denied*, and both renamed once it was gone; a live headless Firefox at seven
> processes did the same for `firefox` and `firefox-1553`. With either family
> live, `ffmpeg-1011` and `winldd-1007` renamed and the browsers root was refused;
> with the browser gone, all three renamed. Firefox 1553 brought no Remote Agent up
> either, so its arm proved liveness by process tree again. The three scripts ran
> with the revision constants moved and a scratch profile and working directory,
> under the suite lock, and every renamed directory was back in place at the end:
> [the batch](../../docs/evidence/2026-10-03-reverify-0.0.83/README.md).

**Windows does not reuse a pid while any handle to that process is open, and
the control shows reuse is otherwise quick.** Measured 2026-08-18 on Windows
**10.0.26200**, spawning `cmd.exe /c exit` in a loop and recording each pid.
*Control arm* -- the process handle released as soon as the child exited:
**2,009 distinct pids over 2,010 spawns, and the 2,010th repeated one** (59424).
*Claim arm* -- an `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` handle taken on
each child **after it had already exited** and never closed: **6,030 spawns,
6,030 handles held, 0 failed opens, and not one pid repeated** -- three times the
control's budget without a single collision. Before this it was asserted as a
bare platform fact at four sites and measured at none. `[STABLE]` for the kernel
behaviour, `[MACHINE]` for the 2,010. **The control is the load-bearing half**:
without it a run with no repeats is indistinguishable from a pid space too large
to wrap during the test. Re-establish by spawning a trivial child in a loop
twice -- once disposing the handle at exit, once keeping an `OpenProcess` handle
in a list -- and comparing the spawn count at the first repeated pid. This is
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

`kernel32` and `user32` are **KnownDLLs** -- resolved from the `\KnownDlls`
section objects before any path search runs, confirmed against
`HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\KnownDLLs` on this
machine -- and `ntdll` is mapped by the loader before user code runs. Only
`rstrtmgr.dll` is neither, and it is the only one whose resolution the attribute
changes. **The rule survives unchanged and every declaration keeps the
attribute**: it costs nothing, it is correct for the one library here that is not
a KnownDLL, and the next library added may not be one either. **The trap is the
audit, not the rule** -- anyone testing this by planting a fake `kernel32.dll`
sees nothing happen and concludes the attribute is decorative. `[STABLE]` for
KnownDLLs, `[MACHINE]` for the list membership. Re-establish by copying a
`System32` DLL beside a probe and reading `GetModuleFileNameW(GetModuleHandleW(name))`
after a call, with and without the attribute.

### `System32` is not a superset of "no attribute", and the module cache hides it -- measured 2026-08-26

**`[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]` makes a native
library sitting beside the host UNFINDABLE, and it does not fall back.** The
entry above measures the attribute against libraries that live *in* `System32`,
where it is inert or protective; this is the other case, and it answers the
opposite way. Measured 2026-08-26 on .NET **10.0.11**, `win-x64`, against a
purpose-built `probe_native.dll` (one exported function, compiled with the same
MSVC the publish uses) copied into the test host's own output directory, with
four declarations of the same entry point differing only in the attribute:

| Attribute | Result |
|---|---|
| `DllImportSearchPath.System32` | **`DllNotFoundException` (0x8007007E)** |
| `DllImportSearchPath.SafeDirectories` | loaded |
| `DllImportSearchPath.AssemblyDirectory` | loaded |
| none at all | loaded |

`SafeDirectories` is `LOAD_LIBRARY_SEARCH_DEFAULT_DIRS`, which covers the
**application directory** as well as `System32`; the System32-only flag covers
neither the application directory nor a fallback to the runtime's ordinary
probing. `[MACHINE]` for the runtime version, `[STABLE]` for the flag semantics.

⚠️ **The trap is the per-module cache, and it is why a mixed file tests clean.**
In the first arrangement of the same probe the four calls ran in the order
*none · System32 · SafeDirectories · AssemblyDirectory* and **all four returned
the value** -- the resolved library is cached per module name, so once any one
declaration has loaded it the rest bind to the cached handle whatever their
attribute says. Reordering so that `System32` ran **first** in a fresh process
produced the table above. A file that mixed the values would therefore pass or
fail purely on which declaration happened to be called first.

**What this decided.** `Storage/Sqlite.cs` carries `SafeDirectories` and not
the `System32` every declaration under [`Interop/`](../../src/BrowserAI/Interop)
carries, and the difference is not a style choice: `e_sqlite3` is not an OS
component, so under a CoreCLR host it can only ever be a loose DLL beside the
host, which `System32` refuses to find. Under the published binary the attribute
is inert in a third way again -- the symbol is resolved by the linker at publish
time and nothing is loaded at all. `SafeDirectories` is also the strongest value
CA5393 accepts, since `AssemblyDirectory` and `ApplicationDirectory` are both on
its unsafe list. Re-establish by compiling a one-function DLL, copying it beside
a console host, and calling it **first** through a `System32`-only declaration.

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
managed work -- allocation, a GC, a managed `Dispose` -- leaves it intact. What
destroys it is a `Dispose`, a log call or a guard that *itself reaches the
platform*, and `File.Exists` alone is enough. **The rule stands and is now
measured, not asserted**; what changes is which intervening statements are
actually dangerous. **The second row is the trap nobody was looking for**: the 11
of this product's 43 declarations that omit `SetLastError = true` make
`Marshal.GetLastPInvokeError()` return a confident **0**, which reads as success
and not as "not captured". `[STABLE]` for the mechanism, `[FLOATS]` for the
BCL call sites that do the clobbering. Re-establish by calling a failing import
with capture on, then reading after each candidate statement in turn.

**`WaitForSingleObject` needs `SYNCHRONIZE`, which
`PROCESS_QUERY_LIMITED_INFORMATION` does not imply.** Measured 2026-08-16 while
writing the containment harness: a handle opened with query rights alone makes
the wait return `WAIT_FAILED`, and a liveness check written as *"anything other
than `WAIT_OBJECT_0` means still running"* then reports every process it can open
as alive **forever**. It presented as a containment defect in the product --
30 seconds of polling, then "the launcher survived" -- and the product was fine.
The shape is the point: a failed call read as one of the two normal answers is
worse than an exception, so `ProcessIdentity.IsAlive` refuses to interpret
`WAIT_FAILED` at all. And `OpenProcess` succeeding proves nothing,
because a handle held by anyone keeps the pid and the object alive after the
process is gone. Re-establish by removing `SYNCHRONIZE` from the access mask.
`[STABLE]`
