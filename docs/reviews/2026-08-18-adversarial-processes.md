<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Adversarial read of the process/containment surface

Read-only review, 2026-08-18. Method: reading for a specific ordering, a specific
OS behaviour, or a failure injected at a specific instant — not load.

**Evidence labels.** `[READ]` = I read it in this tree, or in a `kb/` measurement
made here. `[REASONED]` = I inferred it from documented Windows/BCL behaviour and
did **not** measure it on this machine; each carries the experiment that would
settle it. Nothing below is a measurement I did not make.

---

## Tier 1 — wrongful kill or lost data

### 1. The client-liveness watch acts on a bare pid, and its firing kills every session

`src/BrowserAI/Interop/ProcessLiveness.cs:147-160`

    return status < 0 ? 0 : (int)information.InheritedFromUniqueProcessId;

`src/BrowserAI/Interop/ClientLiveness.cs:91` then `:118`

    var parent = ProcessLiveness.ParentProcessId();
    ...
    var handle = OpenProcess(Synchronize | ProcessQueryLimitedInformation, bInheritHandle: false, (uint)processId);

There is **no creation-time pairing anywhere on this path**. `Interop/CLAUDE.md:10`
states the rule this breaks verbatim: *"A process is (pid, creationFileTime), never
a bare pid"*. The type's own remarks (`ClientLiveness.cs:20-23`) claim a guarantee
it does not have — *"Holding the handle is also what stops Windows recycling the
pid underneath the watch"*. That is true only **from the moment the handle is
opened**. It says nothing about the interval between the parent exiting and
`OpenProcess` running, and `InheritedFromUniqueProcessId` is a **stale field**: the
kernel records the creator's pid at creation and never updates or invalidates it.
`[READ]` for the code; `[REASONED]` for the staleness, which is documented
behaviour of `ProcessBasicInformation` and the reason every parent-pid recipe adds
a start-time comparison.

**The interleaving.**

1. An MCP client launches BrowserAI through a wrapper (`cmd /c`, a shim, a launcher
   that spawns and returns) — the exact arrangement `ClientLiveness.cs:27-30` names
   as this watcher's whole reason to exist.
2. The wrapper exits before BrowserAI reaches `Program.cs:258`.
3. Windows recycles that pid to an unrelated process. Seconds, at the design point.
4. `OpenProcess` succeeds against the stranger. `ClientLivenessLog.WatchingClient`
   logs *"Watching the MCP client, pid N"*.
5. The stranger exits. `Fire()` runs. Event 73 is emitted verbatim: *"The MCP
   client, pid N, has exited … Every session's child, its browser and its job go
   down with this process."*

Every live session's browser is torn down mid-task and the log asserts a cause that
is false. The mirror failure is as bad: if the recycler is long-lived the watch
never fires, and the mechanism silently provides nothing **in precisely the case it
was built for** — a wrapper whose pipe outlives the conversation, where the
stdin-EOF backstop is by construction unavailable.

Second consumer, same defect: `ProcessLiveness.ClientProcessName()` (`:109-127`)
reads the image name off the same recycled pid, and it is written into `lock.json`
as the client's identity — so the ownership record misattributes too.

The fix is one syscall: `GetProcessTimes` on the opened handle, refuse if the
"parent" started after us.

### 2. `TreeDelete` recurses through directory reparse points; the primitive it replaced does not

`src/BrowserAI/Runtime/TreeDelete.cs:109-112`

    foreach (var child in SafeEnumerate(() => Directory.EnumerateDirectories(directory)))
    {
        Remove(child, failures);
    }

`Directory.EnumerateDirectories` returns junctions and directory symlinks as
ordinary directories. The walk descends into them and calls `File.Delete` on the
**target's** contents, outside the tree it was asked to remove. `[READ]`

`Directory.Delete(path, recursive: true)` — banned repository-wide by
`build/BannedSymbols.txt`, with a ban message listing only *reporting* as the thing
it does worse — **checks `FILE_ATTRIBUTE_REPARSE_POINT` during its walk and removes
the link without following it**. `[REASONED]` from the BCL's
`FileSystem.RemoveDirectoryRecursive` on Windows, which branches on the reparse
attribute and calls `RemoveDirectory` on the link itself. So the hand-rolled
replacement is *less* safe than the banned call in exactly this dimension, and
neither `TreeDelete`'s 60 lines of remarks, nor `Runtime/CLAUDE.md`'s bullet, nor
`HAZARDS.md:192` mentions reparse points at all.

**Reachability, worst first.** `browserai_destroy` deletes a **caller-named**
session directory. A session directory is the user's own choice of path and holds a
browser profile — a tree in which junctions are ordinary (a `Cache` relocated to
another volume, a `downloads` junction to the real Downloads folder, a redirected
`AppData` beneath it). One junction and `browserai_destroy` empties the target.
`RevisionPrune` and `InstanceDirectory` are the other two callers.

**Settles it in one command:** `mkdir t\a`, `mklink /J t\link C:\somewhere\with\files`,
then `TreeDelete.Remove("t")` and check whether the target still has its files.

### 3. `RevisionPrune` decides on a census it does not hold, then deletes

`src/BrowserAI/Runtime/RevisionPrune.cs:146` (census) → `:184-191` (delete)

    if (Live(browsersDirectory, logger) is not { } live) { ... }
    ...
    var holders = live.Where(image => image.ImagePath.StartsWith(candidate + Path.DirectorySeparatorChar, ...)).ToArray();
    if (holders.Length is not 0) { ...retain... }
    ...
    TreeDelete.Remove(candidate, failures);

`Live` goes to `BrowserProcesses.RunningFrom`, which at
`src/BrowserAI/Interop/BrowserProcesses.cs:77` opens each process with
`using var handle = …` and **closes it before returning**. Unlike `ScanFor` — whose
whole design note (`BrowserProcesses.cs:104-106`) is that the held handle is what
closes race R2 — the prune's census is a bare snapshot. `[READ]`

The prune holds the two per-family **provisioning** mutexes
(`RevisionPrune.cs:105-124`), so no install can race it. **Nothing on the launch
path takes those mutexes**: `ChildLaunch.Create` and `JobLauncher.Start` touch no
mutex at all. A *launch* out of a superseded revision is entirely unguarded.

**The interleaving.** Instance B (payload rev 1240) finishes provisioning and
prunes. Instance A (payload rev 1237, not yet updated — the normal state during a
Velopack rollout, and the reason `LiveInstances` exists at all) is between
`SessionLock.TryAcquire` and `CreateProcessW`. B's census sees no process in
`chromium-1237`. B deletes it. A's Chromium starts, or is already running and
lazily opens a `.pak` / `icudtl.dat` that is now gone. Running `.exe` / `.dll`
images are refused by the image section and land in `failures`; everything Chromium
opens lazily and closes does not. The outcome is a partially gutted live browser
tree and a `RevisionWouldNotGo` warning that reads as a stuck file rather than as
damage.

**Second, independent hole in the same census.** `BrowserProcesses.cs:79-82` skips
every process this token cannot open, and the type's remarks justify it with
*"nothing BrowserAI launched can be in that set, because it runs as the user and
non-elevated"*. That reasons about **this** process. An elevated BrowserAI, or one
in another logon session, has browsers a non-elevated prune cannot open — invisible,
so the revision reads idle and is deleted underneath them. The product already
anticipates exactly that arrangement: `LockScopes.cs:51-59` chooses `Global\` with
no `Local\` fallback specifically because two logon sessions must be able to see
each other.

### 4. One junction above the install root silently empties the sweep's candidate set — and arms finding 3

`src/BrowserAI/Hosting/LocalAppDataPaths.cs:39-47` composes every path from
`Environment.GetFolderPath(LocalApplicationData)` with `Path.Combine`, which never
resolves links.

`src/BrowserAI/Interop/BrowserProcesses.cs:143` matches **exactly**:

    if (path is null || !wanted.Contains(path) || !GetProcessTimes(...))

against `ImagePathOf` at `:216`, `QueryFullProcessImageNameW(handle, 0, …)`.

`QueryFullProcessImageNameW` reports the name of the image's file object as the
object manager resolved it — i.e. **after** reparse processing — so a junction
anywhere above the browsers root yields the target path, not the spelling
`Path.Combine` produced. `[REASONED]` from Windows path resolution; `[READ]` for
both string sources.

**Reachability.** A user profile relocated to another volume by junction
(`C:\Users\x` → `D:\Users\x`), a redirected `AppData`, or an installation root
passed to `LocalAppDataPaths(rootAppDir)` that the Velopack locator spells
differently. Casing is handled (`OrdinalIgnoreCase`); it is **link resolution** that
is not, and an 8.3 (`JORIHU~1`) registry value would do it too.

**Consequences, compounding.**

- `StraySweep` reports `candidates=0` forever. The census line prints
  `processes=N/M candidates=0` and **nothing** distinguishes "no strays on this
  machine" from "detection is structurally blind". Contrast `TitledWindows`, which
  exists (`StraySweepResult` remarks) precisely because *"a walk that found dozens
  of windows and none named is what a broken title read looks like — and it would
  otherwise be indistinguishable from a clean machine."* The identical tripwire was
  not applied to the image-path match, which is the half the product calls *"the
  whole detection surface"* (`ProvisionedBrowsers.cs:12`).
- The **same** mismatch makes `RevisionPrune.Live()` return empty for every
  revision, so finding 3 stops being a race and becomes deterministic: every
  superseded tree looks idle while browsers run out of it.

**Settles it in one command:** `mklink /J %LOCALAPPDATA%\BrowserAI D:\BrowserAI`,
start a session, and compare `ProvisionedBrowsers.Executables(...)` against
`QueryFullProcessImageNameW` on the live `chrome.exe`.

---

## Tier 2 — silent containment escape and cross-session leakage

### 5. Two concurrent launches in one process cross-inherit each other's pipes

`src/BrowserAI/Interop/JobLauncher.cs:384-392` creates all six pipe ends with
`InheritHandle = 1` and clears inheritance on **the parent's three only**.
`:102-108` then calls `CreateProcessW(..., bInheritHandles: true, ...)`, which
duplicates **every inheritable handle in the process** into the new child — not
just this launch's three.

`DirectStdioClientTransport.cs:78-82` states the concurrency plainly — *"with
several sessions in one process"* — and `SessionManager.cs:80` holds a
`ConcurrentDictionary<string, LiveSession>`. There is no lock anywhere between
`ChildPipes.Create` and `CreateProcessW`: the only `SemaphoreSlim` in `Protocol/`
is `JsonLinesTransport._sendLock`. `BrowserProvisioner` (`:984`) is a third
concurrent launcher. `[READ]`

**The interleaving.** Thread A returns from `ChildPipes.Create()`; its three
child-side ends are inheritable and open. Thread B, mid-`browserai_init` for a
different session, calls `CreateProcessW`. B's `node.exe`, and therefore B's whole
Chromium tree, inherits **A's stdout and stderr write ends**.

The consequence, in the words of `JobLauncher.cs:123-125`: *"A stdout read that
never sees EOF because the parent still holds the write end is a hang with no error
anywhere."* The comment closes the case where **we** hold it and leaves the case
where a **sibling session's browser tree** holds it — for that sibling's entire
life. `kb/windows/processes.md:153-167` already carries the measured general form
(*"a redirected stream is drained when the last holder of its write end closes it,
which is not the same event as the child exiting"*), applied to grandchildren and
not to concurrent siblings.

Two further consequences of the same line:

- Closing A's stdin — upstream's graceful teardown, `LiveSession.cs:144-147` — no
  longer produces EOF for A's node, so every close falls through to the 5 s
  `ShutdownTimeout` and the job kill.
- **Our own stdout handle** is duplicated into every child too, if the handle
  inherited from the MCP client is itself marked inheritable (it usually is — that
  is how it reached us). The child's `STARTUPINFO` points elsewhere, so nothing
  writes to it by accident, but the protocol channel is now *present* in a Chromium
  tree. That is a route to stdout no banned-symbol analyzer can see.

`System.Diagnostics.Process` on Windows serialises pipe-creation-plus-`CreateProcess`
behind a process-wide lock for exactly this reason `[REASONED]`. The structural fix
is already half-built: `ProcessAttributeList` exists and is initialised with
`dwAttributeCount: 1` (`:297`, `:314`); raising it to 2 and adding
`PROC_THREAD_ATTRIBUTE_HANDLE_LIST` with the three pipe ends makes the inherited set
exact and closes all three consequences at once.

### 6. The job handle crosses `CreateProcessW` as a raw value with no ref-count and no keep-alive

`src/BrowserAI/Interop/JobLauncher.cs:323`

    Marshal.WriteIntPtr(storage, job.Handle.DangerousGetHandle());

`GC.KeepAlive(job)` is at `:330` — inside `WithJob`, before `CreateProcessW` at
`:102`. `Start` never keeps `job` alive across the call, and `ProcessAttributeList`
holds only the raw `nint`, so it does not root the `JobObject`. There is no
`DangerousAddRef` / `DangerousRelease` pair anywhere. `[READ]`

Two ways this bites, both answering *"what if the job handle is closed while a child
is mid-spawn?"*:

- **Concurrent dispose.** `JobObject.Dispose()` is `Handle.Dispose()` — a
  `CloseHandle` — and the session teardown path calls it. A teardown racing an
  in-flight launch closes the handle whose numeric value is already in native
  storage. `CreateProcessW` then either fails with `ERROR_INVALID_HANDLE` (benign)
  **or**, if that handle value has been recycled by another thread opening any
  kernel object in the window, names a different object. If the recycled value is
  another session's job handle, the new process is created in **the wrong session's
  job** — a containment misattribution nothing later looks for, because
  `Contains` / `ProcessIds` are only ever asked of the job the caller believes it
  has. `[REASONED]`; Windows recycles handle values aggressively and this is the
  standard argument for `DangerousAddRef`.
- **Collection.** `job` is a parameter with no use after `:85`. A last-use visible
  to the JIT/ILC means the `SafeJobHandle` is finalisable across `CreateProcessW`,
  and its finaliser is a `CloseHandle` on a `KILL_ON_JOB_CLOSE` job. Today every
  caller roots the object (`ChildProcessSession` takes it at `:108`;
  `NodeInstallerRun` stores `_job`), so this is latent rather than live — but latent
  by the caller's grace, not by construction, and `LaunchedProcess` and
  `JobObject.Contains` both bother with `GC.KeepAlive` for weaker cases.

### 7. The sweep's remediation is one `TerminateProcess`, not a tree teardown

`StraySweep.ActOn` ends at `candidate.TryTerminate` →
`src/BrowserAI/Interop/BrowserProcesses.cs:344`, a single `TerminateProcess(_handle, 1)`.

A stray exists only if containment already failed — the job died and its members did
not — so by construction the escaped tree has **no** job holding it. Only the process
owning the singleton window is attributable; every helper of that same escaped tree
lands in `unattributable` and is reported and left alive **by design**
(`StraySweep.cs`, the `CouldNotAttribute` block). Chromium children usually follow
their browser process out through the mojo pipe `[REASONED]`; nothing here checks,
and the Firefox path (`AttributeByProfileLock` → the same `ActOn`) kills the one
process holding `parent.lock` with no equivalent argument made anywhere.

The census reports `terminated=1 unattributable=N` on one line, so it is not silent
— but `SweepLog.Terminated`'s sentence, *"Terminated a stray browser"*, reads as a
tree having been dealt with when at most one process was.

---

## Tier 3 — availability and degraded diagnosis

### 8. The title guard rejects the UNC *spelling*, not UNC *semantics*

`src/BrowserAI/Sessions/StraySweep.cs:238-243`

    public static bool IsRootedLocalDriveLetterPath(string title) =>
        title is { Length: >= 3 } && char.IsAsciiLetter(title[0]) && title[1] is ':'
        && title[2] is '\\' or '/' && !title.Contains('\0', StringComparison.Ordinal);

The remarks call this *"the sweep's single largest availability risk, and it is
closed by a string check"*, with a measured 21,037 ms for one `File.Exists` against
a dead share. A drive letter mapped by `net use Z: \\dead\share` passes the guard and
costs the same 21 s, at `SessionDirectoryFrom`'s `File.Exists`. The loop deliberately
evaluates **every** title, not only candidates' — so any process on the machine that
registers `Chrome_MessageWindow` (the class is forgeable, `MessageWindows.cs:66-67`)
with a `Z:\…` title stalls the whole pass and holds the machine-wide sweep mutex
while doing it. `[READ]` for the code and for the 21 s figure as a measurement
recorded in this tree.

`RollingFileWriter.IsNetworkPath` (`:191-194`) has the identical gap and **states
it** at `:180-187`, naming `GetDriveType` and why it was rejected. The same sentence
is missing here, where the input is untrusted rather than configured.

### 9. `NativeFile.Append`'s completion loop breaks the atomicity the file exists to provide

`src/BrowserAI/Interop/NativeFile.cs:76-89`

    while (!bytes.IsEmpty)
    {
        if (!WriteFile(handle, ref ..., (uint)bytes.Length, out var written, IntPtr.Zero)) { throw ... }
        if (written is 0) { throw ... }
        bytes = bytes[(int)written..];
    }

`FILE_APPEND_DATA` atomicity is **per `WriteFile` call**. A short write makes the
loop issue a second call, appended at wherever the end is *then* — after whatever
another of the ~100 processes wrote in between. The record is torn and interleaved,
and every call returned success. `[READ]` for the loop; `[REASONED]` for the
consequence, which follows directly from the guarantee being per-call.

Three routes to a short write: a quota boundary; a disk-full boundary; and — the
reachable one — **a log directory on a mapped network drive**, which `IsNetworkPath`
explicitly does not catch (finding 8). Over SMB the append-atomicity guarantee is not
offered at all, so on that path the entire premise of the file is void while
`RefusedNetworkDirectory` stays `false` and nothing reports anything.

The `written is 0` guard correctly prevents the infinite loop. It is the
`0 < written < length` case that is wrong, and it is silent.

### 10. `FILE_SHARE_DELETE` lets anything unlink the live process log under every writer

`src/BrowserAI/Interop/NativeFile.cs:58` — `FileShareRead | FileShareWrite | FileShareDelete`.

Any process may then delete or rename `browserai-*.log` while ~100 BrowserAIs hold it
open. Every subsequent `WriteFile` **succeeds** into an unlinked file object; records
go nowhere; `RollingFileWriter.CurrentFile` keeps reporting a path that no longer
exists; and `Write`'s catch (`:139-148`) never fires because nothing failed. `[READ]`
for the share mode; `[REASONED]` for unlink-with-open-handle, which is standard NTFS
behaviour once `FILE_SHARE_DELETE` was granted.

Reachable from inside the product: `RollingFileWriter.SweepExpired` (`:243-265`) runs
in the constructor on every start and deletes on `GetLastWriteTimeUtc < cutoff` — a
restored-from-backup file, or a clock that moved, is enough.

This is the one failure the type's own remarks rule out — *"a sink that truncates on
start has deleted the previous crash before anyone looks at it"* — arriving through
the share mode instead of the open mode.

### 11. The instance-directory liveness test rests on the surface child's cwd alone

`InstanceDirectory.cs:180-195` claims a candidate by `Directory.Move`, on the stated
ground that *"the rename … fails while any process holds the directory as its current
directory"*. `HAZARDS.md:193` records the measurement that established this and marks
the row closed.

The only holder is the surface child, launched at `Program.cs:194` with
`workingDirectory = instance`. **Session children do not hold it** —
`SessionManager.cs:785` passes `artifacts.OutputRoot`, and `ArtifactRouter.cs:151`
puts that under the *session* directory, not the instance one. Meanwhile the instance
directory holds the generated config for **every** live session in the run
(`SessionManager.cs:767-769`). `[READ]`

So if the surface child dies while the run keeps serving — a node crash, an OOM —
nothing holds the instance directory. `Directory.GetLastWriteTimeUtc` on a directory
does not move when files *inside* it are written, only when entries are added or
removed, so five minutes later another BrowserAI's startup sweep (`CreateFresh` →
`Sweep`, `:86`) renames it aside and `TreeDelete`s it. Every live session in the
surviving run loses its config; the next child (re)start fails inside
`BrowserConfiguration.WriteTo` with a missing directory. The run's own
`InstanceDirectory.Delete` at `Program.cs:315` then finds nothing and reports nothing,
because `DirectoryNotFoundException` is explicitly *"never a failure"*
(`TreeDelete.cs:120-125`).

The liveness signal is a property of one process's cwd; the blast radius is every
session in the run.

### 12. A title longer than 32,768 characters is silently truncated to a prefix

`src/BrowserAI/Interop/MessageWindows.cs:187-193` — `Math.Min(length, MaximumTitleLength) + 1`,
then `new string(buffer, 0, copied)`. No caller learns that `length` exceeded the cap.
`[READ]`

The consequence is bounded by the ownership test rather than by anything here: a
truncated title still has to resolve to a directory holding a takeable `lock.json`, so
today it resolves in the **refuse** direction. Worth naming because the type's premise
is that everything on this path fails safe, and a silent prefix is the one shape of
that path that is not obviously refusal-only.

### 13. `WaitForExitAsync` registers a non-owning wait over a handle another thread may close

`src/BrowserAI/Interop/LaunchedProcess.cs:106`

    signal.SafeWaitHandle = new SafeWaitHandle(handle.DangerousGetHandle(), ownsHandle: false);

`Dispose()` (`:144`) closes `handle` with no coordination against an in-flight
registration. `Dispose` is idempotent on itself but not against `WaitForExitAsync`. A
teardown racing a shutdown wait leaves the thread pool waiting on a closed — possibly
recycled — handle, so `WaitForExitAsync` reports the exit of some other object. That
feeds the "did the child exit gracefully" decision and therefore whether the job is
closed early. `[READ]` for the code, `[REASONED]` for the recycling.

`GC.KeepAlive(handle)` at `:125` covers collection, not disposal. Different problems.

### 14. stdout: the analyzer's blind spot is third-party code on a background thread

`Console` appears nowhere in `src/` outside `StdioChannel.cs:84` — I grepped, it is
clean — and `LogToStandardErrorThreshold = LogLevel.Trace` (`ProcessLog.cs:87`) closes
the logging stack. The residual routes I can name:

- **The inherited stdout handle in every child** — see finding 5. This is the one
  concrete route, and `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` closes it.
- **Third-party managed code running after the channel opens.**
  `StdioChannel.OpenStandardStreams()` is deliberately last (`Program.cs:229-231`),
  but `UpdateService.StartInBackground` (`Program.cs:288`) and
  `StraySweep.StartInBackground` (`:170`) then run Velopack and SDK code on background
  threads with stdout live. Velopack's startup path *is* routed
  (`VelopackStartup.cs:104`, `SetLogger`); I did not trace whether
  `VelopackUpdateClient`'s `UpdateManager` is. **I did not demonstrate a byte reaching
  stdout by this route** — this is a structural gap, not a defect.
- NativeAOT fatal errors, unhandled-exception printing and `FailFast` all go to
  stderr / WER `[REASONED]`, so those are not routes.

---

## Claims I attacked and could not break

Stated with the reason, because "I found nothing" and "it holds" are different
answers.

- **Escape by `CREATE_BREAKAWAY_FROM_JOB`.** `JobObject.CreateKillOnClose` sets
  `KILL_ON_JOB_CLOSE` and nothing else, and a breakaway request against a job granting
  neither breakaway flag turns `CreateProcessW` into `ERROR_ACCESS_DENIED` rather than
  an escape. `LimitFlags` and `UiRestrictions` are read **back** from the kernel
  (`JobObject.cs:134-171`) rather than trusted. The remarks correctly identify that
  `BREAKAWAY_OK` would *cause* a Firefox escape rather than merely permit one, and
  that a UI-restriction class would stop Chromium's sandbox job nesting. I could not
  construct a member that leaves.
- **The window-pid → candidate-pid join cannot be poisoned.** `Pass` runs `ScanFor`
  **before** `Walk` (first two lines of `Pass`), and `ScanFor` holds an open handle on
  every candidate. A candidate's pid therefore cannot be recycled between the scan and
  the join, so a window claiming a candidate's pid really is owned by that candidate.
  Reversing those two statements would break it; today the ordering is right.
- **Redirecting attribution from outside the candidate process.** `attributed` is keyed
  on the window's owning pid, so forging a `Chrome_MessageWindow` gives you an entry
  for *your* pid, which is not in the candidate set. A `Chrome_MessageWindow` whose
  title is not a rooted drive-letter path `continue`s **before** the dictionary
  assignment, so the well-known second window (`DeviceMonitorMessageWindow`) cannot
  clobber a good title.
- **Escaping the title into a UNC or device path through `Path.GetFullPath`.** `\\?\`,
  `\\.\` and `\\?\UNC\` are recognised only at offset 0; a string beginning `X:\`
  cannot normalise into one, and `..` traversal cannot produce a leading double
  separator. Repeated separators collapse to a local path.
- **The rewrite gap in `SessionLock`.** `Rewrite` closes `lock.json`, renames and
  re-opens — a real interval in which the directory is unheld — but it holds the
  per-directory `Global\BrowserAI-{hash}` mutex across all of it, and `TryHoldUnowned`
  (`:386-401`) takes the **same** named mutex before its own open, with a 60 s wait.
  The sweeper cannot observe the gap. `Reclaim` (`:295-310`) correctly refuses to hand
  back an object that would report ownership it lost.
- **Ordering of "lock held" against "browser alive".** `OpenAsync` takes the lock
  before `ChildLaunch.Create`, and `LiveSession.DisposeAsync` (`:142-154`) disposes the
  child — closing the job, killing the browser — **before** `Lock.Dispose()`. There is
  no instant in a clean open or close at which a live browser sits in an unlocked
  directory. This is the interleaving I most expected to find and it is closed.
- **`FILETIME` reassembly in the Restart Manager path.** `RestartManager.cs:~207` is
  `((long)StartTimeHigh << 32) | StartTimeLow` with both operands `uint`, so the low
  word is zero-extended and there is no sign-extension collision. `RM_UNIQUE_PROCESS`
  is laid out as three `uint`s with the documented reason (a `long` would insert four
  bytes of padding), and the value compares directly against `GetProcessTimes`.
- **Last-error discipline in `MessageWindows.Walk`.** `FindWindowExW` carries
  `SetLastError = true`; `GetWindowThreadProcessId` does not, so it cannot overwrite
  the managed last-error slot, and the `ERROR_INVALID_WINDOW_HANDLE` discriminator is
  read from the call that produced it.
- **`JOBOBJECT_BASIC_PROCESS_ID_LIST` under-reporting.** `ProcessIds()`
  (`JobObject.cs:204-253`) grows on `ERROR_MORE_DATA` **and** re-checks
  `inList < assigned`, which is the case a caller that only checked the return value
  would miss.
- **A `KILL_ON_JOB_CLOSE` job kept alive by a leaked handle.** The job handle is created
  with `NULL` security attributes and is unnamed, so there is no inheritance route and
  no `OpenJobObject` route, and `HandleIsInheritable` reads the flag back. `NativeFile`,
  `FileStream` and `Mutex` all produce non-inheritable handles, so the only inheritable
  handles in the process are the pipes — which is what makes finding 5 a pipe problem
  and not a job problem.

## What I did not cover

`BrowserProvisioner`'s mid-download crash states beyond the launch path;
`SessionIndex.Sweep`; `JsonLinesTransport` framing; the artifact router; the
update / Velopack path past `VelopackStartup`. Named rather than left to read as a
clean bill.
