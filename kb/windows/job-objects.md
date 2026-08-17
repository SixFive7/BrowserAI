<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Job objects and process containment

**Versions in force** unless an entry says otherwise: Windows 11 Pro 26200 · Node **v24.19.0 LTS** (the bundled runtime) and **26.7.0** (libuv 1.52.1, the one the 2026-08-15 measurements ran on) · Chrome for Testing 152.0.7977.8 (`chromium-1237`) · Firefox 153.0 (`firefox-1539`) · `@playwright/mcp` 0.0.79 · `playwright-core` 1.63.0-alpha-2026-08-05 · .NET SDK 10.0.400, runtime 10.0.11.
Measured on [the reference machine](../README.md#the-reference-machine).

How Windows job objects hold a process tree, what can escape one, and what a
supervisor has to do to make "kill the parent and nothing survives" true. The
process-level facts this rests on — startup, stdio and the interop surface — are
in [Processes: stdio, files and the interop surface](processes.md); detection of
a browser that got away anyway is in [Detecting stray browsers](detection.md).

First measured 2026-08-15 in a throwaway harness that no longer exists, then
**re-established 2026-08-16 against the product's own job object, launcher and
delete routine** — which is the version to trust and the one with a route. Where
an entry names a test, run the suite; where it cites the 2026-08-15 numbers, they
are the spike's and are kept only because the later run agrees with them.

**The headline: containment holds.** 16 runs, **106 spawned processes, 0
escapees, 0 survivors**, across real Chromium and Firefox trees. `[FLOATS]`

**Re-established 2026-08-16 against the product's own job, launcher and delete
routine**, by `BrowserContainmentTests` — two runs of each family, on browsers
BrowserAI provisioned into its own root:

| Browser | Processes in the job | Escapees | Survivors after an external kill | Registered for restart | Profile deleted cleanly |
|---|--:|--:|--:|--:|---|
| Chromium 152.0.7977.8 (rev 1237) | 11, then 10 | 0 | 0 | **0** | yes |
| Firefox 153.0 (rev 1539) | 10, then 10 | 0 | 0 | **1** | yes |

The tree under test is launcher → `node` → `cli.js` → browser → helpers, which is
one level deeper than production, and the launcher is `TerminateProcess`d from
outside so that nothing but the kernel closing its last job handle can be what
cleaned up. **The profile delete is the half a survivor count cannot make**: a
process gone from the job's list but still holding a mapped file leaves a
directory Windows refuses to remove, so a profile that deletes cleanly is the
observable difference between *reported dead* and *nothing is left*. `[FLOATS]`

> ⚠️ **A live browser is not a static tree, and the cross-check had to be
> re-stated for it.** The probe's *"job members the walk missed"* check compared
> the toolhelp walk against a membership list read **after** it, and against a
> real Chromium that reports a phantom every time: helpers are started and
> retired continuously, so a process born during the walk is in the later list
> and not in the walk. Node trees never produced one, which is why step 6 never
> saw it. The check now uses the **intersection** of two lists taken either side
> of the walk — the members that were present throughout — and the per-row
> membership check uses the union. Measured 2026-08-16; one phantom,
> reproducibly, before the change.

**Firefox registers itself for restart and Chromium does not**, asked of the live
processes with `GetApplicationRestartSettings` rather than argued from a command
line's length. Every Chromium process in the tree answers `0x80070490`
(`HRESULT_FROM_WIN32(ERROR_NOT_FOUND)`); **exactly one** Firefox process answers
`S_OK`. That is `toolkit.winRegisterApplicationRestart` doing what
[kb: resurrection](../chromium/resurrection.md#the-mechanism-and-what-is-still-unproven)
says it does, on a build BrowserAI provisioned. Containment is unaffected —
`KILL_ON_JOB_CLOSE` happens now and Windows' restart happens after a reboot or an
update — but **the product cannot ship Firefox
sessions without turning that pref off in the profile**, or a machine update will
resurrect a browser no session claims. Asserted on both sides by
`BrowserContainmentTests`, so the day Mozilla changes it the suite says so.
`[FLOATS]`

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
kill-by-enumeration cannot win that race.** Shipped mitigation, read 2026-08-16
in a long-lived in-house VB.NET updater stack: a loader wraps its entire
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
inside it is already contained. `[STABLE]` for the race, which follows from the
kernel's own semantics; `[MACHINE]` for the observation, and **the code it was
read in is not published, so that half is not reproducible from this
repository** — the retry-count comment is quoted in full above because it is the
whole of the evidence.

**Containment holds from a published NativeAOT binary, against a real browser.**
Measured 2026-08-16 at
the first published-AOT vertical slice,
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
uses the **working-directory lock as the liveness check**, with no pid to
recycle and no name to match. `[STABLE]` for the lock; re-establish with
`InstanceDirectoryTests`.

> ⚠️ **Corrected 2026-08-16 (previously "— Windows refuses to delete a directory
> that is some process's current directory, so the delete simply fails for a live
> run and succeeds for an abandoned one").** Measured twice: Windows refuses to
> remove the **directory node** and does **not** refuse to delete the files
> inside it, so `Directory.Delete(path, recursive: true)` emptied a live run's
> directory completely and failed only afterwards — the sweep was not skipping
> live runs, it was gutting them. The lock is a real liveness signal; the
> operation that tests it has to be `Directory.Move`, which is refused **with the
> contents untouched**. See the entry on it under
> [Files, durable writes and deletes](processes.md#files-durable-writes-and-deletes).

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

**Node's `child_process` has no job object support at all**, and Node's `spawn`
cannot execute `.cmd` shims without `shell: true` — a live Claude Code bug for
plugin-shipped servers using bare `npx`
([#58510](https://github.com/anthropics/claude-code/issues/58510)). Every Node
process supervisor on Windows falls back to `taskkill /T /F` or a native addon,
and none survives a hard kill of the supervisor. `[FLOATS]`

**No credible NuGet job-object wrapper exists** — the candidates have <6K
downloads and the newest was published in **2017**. `dotnet/runtime`
[#126273](https://github.com/dotnet/runtime/issues/126273) proposed built-in
support and was closed as not planned. The hand-rolled surface is ~60 lines.
`[FLOATS]`
