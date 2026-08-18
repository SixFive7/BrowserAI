<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Adversarial review: session locking, ownership and the sweep

Read-only review, 2026-08-18. Every finding is an interleaving, not a smell. Line
numbers are from the tree as read; where I am reasoning rather than citing a
measurement, the sentence says so.

Ranking is by outcome, not by likelihood:

- **A — wrong answer**: two owners, a wrongful destroy, a tree reported installed
  that is not, a live session reported absent.
- **B — degraded**: a spurious refusal, a slow path, a leak.
- **C — attacked and held.** Listed because a report of findings alone cannot be
  told apart from a shallow one.
- **D — the proposed pre-gate probe**, attacked before it is built.

---

## A1. `destroy` gives the directory back before it deletes it

`SessionManager.DestroyAsync`:

```
:482   if (taken.Acquired is not { } held) { return "not destroyed"; }
:490   held.Dispose();                                  // ownership released HERE
:492   var size = SessionLayout.SizeOnDisk(location.FullPath);
:500   TreeDelete.Remove(location.FullPath, failures);  // tree deleted HERE
:502   _index.Forget(location);
```

The comment at `:487-489` justifies the release — *"Windows will not remove a file
this process is holding open, and the lock has done its job the moment ownership
is proven"*. The first half is true of `lock.json`. The second is the defect:
ownership was proven for an instant and then handed back, and the destructive act
happens outside it. No gate is held either — `TryAcquire` releases the
per-directory mutex before returning (`SessionLock.cs:236`) and `held.Dispose()`
disposes it (`SessionLock.cs:326`).

**The interleaving.** P1 and P2 are two BrowserAI processes; S is one session
directory with a stale lock (its previous holder exited cleanly).

| t | P1 (`browserai_destroy S`) | P2 (`browserai_resume S`) |
|---|---|---|
| t1 | `TryAcquire(S)` → `Reclaimed`, holds `S\lock.json` | |
| t2 | `held.Dispose()` — `S\lock.json` released | |
| t3 | | `TryAcquire(S)` → gate free, `OpenHeld` succeeds → **`Reclaimed`**, P2 owns S |
| t4 | `SizeOnDisk(S)` — full recursive walk of S | `ConnectChild` → node → Chromium launches into `S\profile` |
| t5 | `TreeDelete.Remove(S)` — deletes `S\profile`, `S\output`, `S\lock.json` | browser writing into `S\profile` |
| t6 | `_index.Forget(S)` — removes **P2's** index entry | `_index.Record(S)` (may land either side) |

P2 has a live session whose directory has been deleted underneath it, and its
browser is writing into a tree that no longer exists. P1 reports `Destroyed` with
a partial-failure list naming P2's open files (`:508-514`) — which reads as *"a
virus scanner had them open"*, the exact wrong diagnosis.

**The window is not microseconds.** `SessionLayout.SizeOnDisk` (`SessionLayout.cs:99-111`)
enumerates every file under the session with `RecurseSubdirectories = true`. A
Chromium profile is tens of thousands of files. The window is a full directory
walk of a hot tree, plus the tree delete itself.

**Bound.** Requires two processes naming the same directory, one destroying. That
is not exotic: the charter's design point is ~100 processes and sessions are
addressed by path, so two agents pointed at one directory is the case the lock
exists for.

**What removes it.** Hold `held` across the walk and the delete, and delete
`lock.json` *last* rather than releasing first. `TreeDelete.Remove` already reports
per-node failures, so `lock.json` refusing to go while we hold it is a solvable
ordering problem, not a reason to release. Alternatively hold the per-directory
gate across `:490-:502`; that is a longer hold than the gate is documented for,
but it is bounded and it is exactly what the gate's 60 s hang-detector sizing can
absorb.

---

## A2. The provisioning mutex: `abandoned` is still read as "the tree is unusable"

`BrowserProvisioner.Install`:

```
:693   if (acquisition is MutexAcquisition.AcquiredAbandoned)
:699       ProvisioningLog.AbandonedInstallFound(...);
:700       TreeDelete.Remove(directory, []);      // deletes UNCONDITIONALLY
:706   var result = IsComplete(directory) ? "already installed" : RunInstaller(...);
```

The 2026-08-17 fix (`:713-745`) removed one false inference — *"I lost the mutex,
therefore a download is in flight"* — after establishing that a holder keeps the
mutex through `Prune`, which is slow. **The symmetric inference is still here**:
*"the holder died, therefore whatever is in the directory is unmarked and
unusable"*. `IsComplete` — one `File.Exists` on `INSTALLATION_COMPLETE`
(`:539-540`) — is the discriminator, and it is asked on the line *after* the tree
is deleted.

**The interleaving.**

| t | P0 | P1 (long-running, sessions open) |
|---|---|---|
| t1 | takes `Global\BrowserAI-Provision-<h>`, finds tree complete, calls `Prune` | browsers running out of `<browsers>\chromium-1234` |
| t2 | killed mid-`Prune` (Velopack `force_stop_package`, editor closed, job object) — mutex abandoned | |
| t3 | | `init` → `Ensure` → not complete? no — but any later `Install` acquires → **`AcquiredAbandoned`** |
| t4 | | `:700` `TreeDelete.Remove` deletes the **complete, in-use** revision tree |
| t5 | | `:706` `IsComplete` now false → `RunInstaller` re-downloads 203.8 MB |

`TreeDelete.Remove(directory, [])` discards its failure list. Windows refuses to
delete a mapped executable, so `chrome.exe` survives; the `.pak` files, locales and
ICU data do not. *(Reasoning, not measured: the mapped-image refusal is standard
Windows behaviour, and `TreeDelete` is per-node with a try/catch, so partial
deletion of a live tree is the expected shape rather than an all-or-nothing.)*
Every running browser then fails on its next resource load, and every session on
the machine is affected because the browsers root is shared.

**Bound.** The holder must die *after* writing the marker and *before* releasing.
That interval is `Prune` — which the file itself documents at `:722-724` as
*"walks every process on the machine and is slow exactly when the machine is
busy"* — plus the return path. It is the same interval the 2026-08-17 defect lived
in.

**What removes it.** One reordering: `if (acquisition is AcquiredAbandoned && !IsComplete(directory))`.
An abandoned mutex over a *marked* tree means the holder died during `Prune`, and
the correct recovery is to re-run `Prune`, not to delete the install.

---

## A3. `reinstall` deletes the revision directory with no provisioning mutex

`BrowserProvisioner.ReinstallAsync:487` calls `TreeDelete.Remove(directory, failures)`
outside the per-family mutex. The guard is in `SessionManager.ReinstallBrowserAsync`
and it checks **processes running from the tree** (`:600` `BrowserProcesses.RunningFrom(directory)`).

A concurrent *installer* is not a process running from the tree — it is `node.exe`
from the payload directory, extracting *into* it. `RunningFrom` returns empty and
the guard passes.

**The interleaving.**

| t | P1 | P2 |
|---|---|---|
| t1 | `init` → `Ensure(chromium)` → `Install` on a `LongRunning` thread → takes the provisioning mutex → `RunInstaller` extracting into `<browsers>\chromium-1234` | |
| t2 | | `browserai_reinstall_browser` → `RunningFrom` = 0 → proceeds |
| t3 | | `ReinstallAsync:487` `TreeDelete.Remove` deletes P1's partially-extracted files |
| t4 | extraction completes, writes `INSTALLATION_COMPLETE` into the gutted tree | |
| t5 | | `WaitAsync` → `Install` → mutex held → `WaitForAnotherProcess` → `IsComplete` **true** → *"chromium was provisioned by another BrowserAI process"* |

Both processes report success. `IsComplete` reports installed forever. The first
launch produces `spawn EFTYPE`, after which — per the note at
`BrowserProvisioner.cs:536-538` — upstream writes `DEPENDENCIES_VALIDATED` into
the corrupt directory and suppresses revalidation for thirty days. That is a
durable confident wrong answer, which is the class this repository exists to
remove.

The doc at `SessionManager.cs:583-589` gives two reasons for taking no lock, and
both are wrong:

- *"the delete is itself the guard"* — it guards against **running executables**,
  not against a **writer**. The one case it cannot see is the one that corrupts.
- *"Taking the provisioning mutex here instead would deadlock against the
  installer, which takes it on its own thread"* — a different thread taking a
  mutex is not a deadlock, it is a wait. The real obstacle is that
  `ReinstallBrowserAsync` is `async` and a named mutex is thread-affine
  (`MachineMutex.cs:46-53`), which is a solvable shape (`Task.Factory.StartNew`
  with `LongRunning`, exactly as `BrowserProvisioner.Start:628-634` already does).

**Bound.** Requires a reinstall concurrent with a first-run download. At the
design point, `init` provisions on a background thread and returns immediately
(`Ensure`), so no sequencing is needed — this is the same shape as the
2026-08-17 hang, which was found by running the suite with every test at once.

---

## A4. Path aliasing gives one directory two gates, and the gate is what hides both rename windows

`SessionPath.Resolve:105` is `Path.GetFullPath` → `TrimEnd` → `ToUpperInvariant`
→ SHA-256. `Path.GetFullPath` does **not** resolve:

- the `\\?\` prefix — `\\?\C:\a\b` stays `\\?\C:\a\b`
- 8.3 short names — `C:\PROGRA~1\x`
- junctions and symlinks — `D:\link\s` vs `C:\real\s`
- `subst` drives and mapped network drives — `Z:\s` vs `\\host\share\s`

Each pair yields two `Key`s, two `MutexName`s and two `IndexKey`s — but
`Path.Combine(fullPath, "lock.json")` names **one physical file**. So the *handle*
still refuses a second simultaneous holder, and that is why this is not an
immediate "two owners" — but the *gate* no longer serialises anything, and the
gate is the sole reason the close/rename/re-open gap is unobservable
(`SessionLock.cs:47-53`).

**Interleaving (a) — reclaim of a live session.**

| t | P1 (`C:\Program Files\ai\s1`, live) | P2 (`C:\PROGRA~1\ai\s1`) |
|---|---|---|
| t1 | `browserai_set_purpose` → `Rewrite` → takes gate **G1** | |
| t2 | `:267` `_held.Dispose()` — the file is now unheld | |
| t3 | | `TryAcquire` → takes gate **G2** (uncontended) → `OpenHeld` **succeeds** |
| t4 | | `previous` = P1's record, `previousRunning` = `true` → writes its own record → **`Reclaimed`, P2 owns it** |
| t5 | `WriteDurably` → `Replace` refused (P2 holds the destination) → retries for 30 s | launches a browser into `s1\profile` |
| t6 | `Reclaim` → `OpenHeld` → sharing violation → `IOException` *"this session no longer holds its directory"* (`:304-306`) | |

At t6 `SetPurpose` (`SessionManager.cs:535`) lets that `IOException` out of the
tool call. The `LiveSession` stays in `_live`, its child stays connected, its
browser stays running. **Two BrowserAI processes are now driving one browser
profile**, which is the single safety property the product is built around, and
neither of them is told.

**Interleaving (b) — "free, and never locked".** Same setup, but P2's `OpenHeld`
lands in the *unbound* window rather than the unheld one — the kb measures the
observable sequence across one replace as `denied → absent → the new record`
(`kb/windows/processes.md:374-436`). Then `TakeOrReport:523-527` catches
`FileNotFoundException` and sets `previous = null`, commented *"Free, and never
locked. Nothing to reclaim, nothing to report."* P2 writes a fresh record:
`Created = now`, `PurposeHistory` reset. The incumbent's record, purpose and
history are destroyed, and the caller is told the directory was never locked.

**Bound.** Requires two callers to spell one directory two ways. `\\?\C:\...`
needs no filesystem setup at all — it is a legal fully-qualified path that passes
`SessionManager.Resolve:1177` and canonicalises differently. Existing coverage
(`SessionPathTests.ThreeSpellingsOfOneDirectoryProduceOneIdentity`, per
`HAZARDS.md:160`) covers a trailing separator, a case change and a `..` segment —
none of the four aliases above.

**What removes it.** Canonicalise through the filesystem's own final name —
`File.GetFinalPathNameByHandle` on an open handle to the directory, or
`Path.GetFullPath` on a `DirectoryInfo.ResolveLinkTarget` — and hash *that*. It
costs one open per `Resolve`, on a path that is about to be opened anyway. A
cheaper partial: reject `\\?\` and `\\.\` prefixes outright and normalise 8.3
components, which closes the two that need no setup.

*(`Sessions/CLAUDE.md:8` names the adjacent risk — a new component canonicalising
on its own. The risk here is the reverse and is not named: one canonicaliser,
two caller spellings.)*

---

## A5. The one ungated reader of `lock.json` that ACTS on the absence

**Exhaustive enumeration.** Every reader of `lock.json` in `src/`:

| Reader | Gated? | Null ⇒ |
|---|---|---|
| `SessionLock.OpenHeld` ← `TakeOrReport:520` | yes | — |
| `SessionLock.OpenHeld` ← `TakeOrReport:554` (re-open) | yes | — |
| `SessionLock.OpenHeld` ← `Rewrite:272` | yes | — |
| `SessionLock.OpenHeld` ← `Reclaim:297` | yes | — |
| `SessionLock.OpenHeld` ← `TryHoldUnowned:412` | yes | `FileNotFoundException` → *"not a BrowserAI session"* → **spare** ✓ |
| `SessionLock.ReadRecord` ← `Contended:631` | yes (inside `TakeOrReport`) | message only ✓ |
| `SessionLock.ReadRecord` ← **`SessionIndex.Locate:435`** | **no** | `NotASession` → `IsRemovable` → **entry deleted** ✗ |
| `SessionLock.ReadRecord` ← `SessionManager.ExplainUnknownSession:168` | no | *"names no session"* refusal — wrong report, no action |
| `SessionLock.ReadRecord` ← `SessionManager.ResumeAsync:376` | no | throws *"nothing to resume"* — spurious refusal |
| `SessionLock.ReadRecord` ← `SessionManager.DestroyAsync:472` | no | refuses to destroy ✓ (safe direction) |
| `SessionLock.ReadRecord` ← `SessionManager.SetPurpose:541` | no | throws — spurious refusal |
| `SessionLock.ReadRecord` ← **`SessionManager.Existing:1014`** | **no** | *"the directory is free"* → **init proceeds** ✗ (see A6) |
| `File.Exists` ← `StraySweep.SessionDirectoryFrom:582, :589` | no | **spare** ✓ |

The kb's bounding claim (`kb/windows/processes.md`, *"BrowserAI is safe under the
absence today"*) names exactly two of these — `SessionDirectoryFrom` and `ActOn`
— and asserts *"every ungated one fails in the safe direction"*. **Two do not.**

### The index one

`SessionIndex.Locate:435` → `ReadRecord` null → `NotASession` (`:444-447`) →
`SessionIndexEntry.IsRemovable` (`:645-648`) → `Sweep:270` `TryDelete(entry.EntryFile)`.

The R7 re-check (`Sweep:259-263`, `ReFollow`) **does not close this**. R7 was
written for a different race — a directory created between the enumeration and the
delete — and it re-reads microseconds later. The unbound window's width is
governed by the *writer's* 5→100 ms backoff (the kb's own `[UNVERIFIED]`
explanation, `processes.md:398-407`, which fits the observed width). Both reads
therefore land inside one window. *(Reasoning: the two reads are separated by one
`File.Exists` plus one open; the window is milliseconds. I did not instrument it.)*

**The interleaving.**

| t | P1 (live session S, driving a browser) | P2 (starting up) |
|---|---|---|
| t1 | `browserai_set_purpose S` → `Rewrite` → gate → `_held.Dispose()` → `Replace` retrying | |
| t2 | | `StraySweep.Pass` → `_index.Sweep()` → `Locate(S)` → `ReadRecord` → **absent** → `NotASession` |
| t3 | | `ReFollow(S)` — still inside the window → still `NotASession` |
| t4 | | `TryDelete(<index>\<sha256 of S>)` — **S's inventory line is gone** |
| t5 | `Replace` lands, `OpenHeld` re-takes the file, session healthy | |

**Nothing re-asserts it.** `_index.Record` is called from `OpenAsync:816` only —
i.e. on `init` and `resume`. The `Rewrite` path does not call it. S is live and
will be driven for hours without another `init` or `resume`, so it stays invisible
for the rest of its life.

Downstream wrong answers, all in other processes:

- `browserai_list` (`SessionManager.List:432`) omits a live session.
- `LiveSessions()` (`:682`) omits it, so
  `ReinstallBrowserAsync` takes the *no claimants* branch (`SessionManager.cs:625-632`)
  and answers `UnattributableBrowserRunning` — *"live browsers, and no session
  anywhere accounts for them"* — about a fully accounted-for session.
- `StraySweep.AttributeByProfileLock:392` walks `_index.Follow()` for Firefox
  directories; a missing entry means that session's Firefox can never be
  attributed. (Fail-safe — it is reported and left running.)

**Bound.** The window is the writer's rename; the outcome is invisibility, not
destruction — nothing downstream treats an index entry as authority
(`SessionIndex.cs:24-29`), which is what keeps this out of A1's class.

**What removes it.** Either (i) make `NotASession` non-removable when the
directory exists and one of this product's own `lock.json.new-<guid>` temps is on
disk — the kb names that as the cheap discriminator and declines it because it
couples two temp-naming conventions, which is a smaller cost here than in
`RenameWindow` since `SessionIndex` already knows the convention; or (ii) call
`_index.Record` on the `Rewrite` path so the entry self-heals as the design
assumes; or (iii) have `ReFollow` wait one backoff step before re-reading, which
turns a same-window double read into a cross-window one.

---

## A6. `init`'s existence guard is ungated and acts on the absence

`SessionManager.InitAsync:320` → `Existing:1008` → `ReadRecord` → `record is null ? null : refusal`.
A `null` means *"the directory is free, proceed"* — and `null` is precisely what
the unbound window returns.

The gated `TryAcquire` downstream stops this becoming two owners: it re-opens
under the gate and will see the record. So the reachable outcomes are:

- Directory held by a **live** session in another process → sharing violation →
  `Held` → refused. Safe.
- Directory holds a **stale** lock (holder exited) → `TryAcquire` → **`Reclaimed`**
  → `init` returns success.

The second is a wrong answer. `Compose` (`SessionLock.cs:676-693`) takes `Mode`
and `Browser` from the **request** and carries over only `Created` and
`PurposeHistory` from `previous`. So `init` silently rebinds a closed session's
mode and browser family over a profile on disk that belongs to the other browser —
the exact thing `SessionErrors.SessionAlreadyExists` exists to prevent and that
`resume` refuses explicitly (`SessionManager.cs:366-367`,
*"the browser is bound at init and the profile on disk belongs to it"*).
`SessionLayout.Create` is non-destructive (`SessionLayout.cs:81-89`), so no files
are lost; the damage is a session whose record and profile disagree.

**Bound.** Requires the init to land inside another process's rename of that same
`lock.json`, on a directory with a stale lock. **What removes it:** move the
`Existing` check inside `TakeOrReport`, where the record is already read under the
gate — `previous is not null && !createdHere` is the same predicate, computed from
a gated read.

---

## B1. The gate does not outlast the waits taken inside it — the test checks one of three

`SessionLockTests.cs:379-385`:

```csharp
await Assert.That(LockScopes.PerDirectoryGate).IsGreaterThan(RenameWindow.Budget)
```

60 s > 30 s. But **one gate hold contains up to three serial 30 s budgets**:

`TakeOrReport`, inside the gate:

| step | bound |
|---|---|
| `:520` `OpenHeld` → `RenameWindow.WaitOut` | ≤ 30 s |
| `:553` `WriteDurably` → `Replace` retry loop (`:800`, `MoveBudget = RenameWindow.Budget`) | ≤ 30 s |
| `:554` `OpenHeld` → `RenameWindow.WaitOut` | ≤ 30 s |
| **total** | **≤ 90 s against a 60 s gate** |

`Rewrite`, inside the gate:

| step | bound |
|---|---|
| `:271` `WriteDurably` → `Replace` | ≤ 30 s |
| `:272` `OpenHeld` | ≤ 30 s |
| `:283` `Reclaim` → `OpenHeld` | ≤ 30 s |
| **total** | **≤ 90 s** |

The comment at `LockScopes.cs:124-134` reasons about the pair
(`RenameWindow` inside `PerDirectoryGate`) and the test encodes the pair. It is the
*sum* that has to be less than the gate, and it is 1.5× the gate. So a single
holder that is legitimately waiting out three rename windows makes every peer's
`TryAcquire` return `Busy` at 60 s — and the `Busy` message
(`SessionLock.cs:206-208`) offers *"a process is wedged holding it"* as one of two
explanations, which would again be a diagnosis the code cannot support.

**What removes it.** Either assert the sum (`3 × RenameWindow.Budget < PerDirectoryGate`)
and re-size, or — better — give the gate a *deadline* rather than a per-call
budget: pass a remaining-time value into `RenameWindow.WaitOut` so the whole
critical section is bounded once. The second also fixes B2 by construction.

---

## B2. Everything else that blocks inside the per-directory gate

The prompt asks for waits the pair-test cannot know about. Inside one hold of
`LockScopes.PerDirectoryGate`:

| call | site | why it can block |
|---|---|---|
| `Flush(flushToDisk: true)` + `FileOptions.WriteThrough` | `SessionLock.cs:750-759` | synchronous disk round trip. Measured ~17 ms locally (kb); unbounded on a slow or remote volume |
| `File.Move(temp, lockFile, overwrite: true)` | `:788` | retried to `MoveBudget` |
| `File.Delete(temp)` | `:824`, in `WriteDurably`'s `finally` | filesystem call, inside the gate |
| `ProcessLiveness.IsAlive` | `:542` | `OpenProcess` + `GetProcessTimes` + `WaitForSingleObject` |
| `ProcessLiveness.ClientProcessName()` | `:691` via `Compose` | `NtQueryInformationProcess` + `OpenProcess` + `QueryFullProcessImageNameW` on the parent — reads the parent's image path, which can be on a remote volume |
| `Directory.CreateDirectory` / `FileStream(CreateNew)` for the temp | `:746-750` | filesystem |

**The one that matters is the volume.** `SessionPath.Resolve` accepts a UNC path —
`Path.GetFullPath(@"\\host\share\s")` is fully qualified and does not end in `:`,
so it passes both `SessionManager.Resolve:1177` and `SessionPath.cs:112`. The kb's
own measurement for a filesystem call against an unreachable UNC host is
**21,037 ms**, and 22,225 ms for a dead hostname
(`HAZARDS.md:179`) — the number `StraySweep.IsRootedLocalDriveLetterPath`
(`StraySweep.cs:245-250`) was written to defend the *sweep* against. There is no
equivalent guard on the *session directory* path.

Three such calls inside one gate hold exceed 60 s on their own, without any
contention at all. Every peer on that directory then gets `Busy`.

**Bound.** Per-directory: only contenders for that same directory are affected,
because the mutex name is path-derived. Nothing machine-wide is starved.
**What removes it:** apply the same rooted-local-drive-letter test to the session
`directory` argument, or explicitly decide that UNC sessions are supported and
size the gate against a network round trip.

---

## B3. `LiveInstances` uses the per-directory namespace for a different scope

`LiveInstances.MutexNameFor:232` is `SessionPath.Resolve(rootAppDir).MutexName`,
i.e. `Global\BrowserAI-<sha256(rootAppDir)[..32]>` — **the same construction and
the same namespace as a session's per-directory gate**. `LockScopes` documents
three scopes (`LockScopes.cs:12-27`); this is a fourth, sharing the first's names.

A session opened on the install root itself — `%LOCALAPPDATA%\BrowserAI` — collides
exactly. Sessions live wherever the caller says and nothing refuses that path.
Consequences:

- `LiveInstances.Join` waits `LockScopes.LiveInstanceGate` = 5 s
  (`LockScopes.cs:178`). Queued behind a 60 s session gate it fails, and the doc at
  `LiveInstances.cs:80-85` is explicit that a failed join costs the process its
  ability to update — **silently, for the process's whole life**.
- `AmIAlone`'s census holds the same object, blocking that directory's `TryAcquire`.

**Bound.** Requires a session on the install root. **What removes it:** a distinct
prefix, e.g. `Global\BrowserAI-Live-<hash>`, mirroring what `BrowserProvisioner`
already does with `Global\BrowserAI-Provision-` (`BrowserProvisioner.cs:205`).

---

## B4. Same-process `destroy` racing `set_purpose` leaks the lock permanently

Nothing serialises tool calls: `_live` is a `ConcurrentDictionary`
(`SessionManager.cs:80`) and is the only synchronisation in the manager.

| t | thread A (`set_purpose`) | thread B (`destroy`) |
|---|---|---|
| t1 | `:534` `_live.TryGetValue` → `live` | |
| t2 | `Rewrite:253` `ObjectDisposedException.ThrowIf` — passes | |
| t3 | `:255` acquires `_gate` | |
| t4 | | `:459` `_live.TryRemove` → same `live` |
| t5 | | `live.DisposeAsync` → `Lock.Dispose` → `:317` sets `_disposed`, `:324` `_held.Dispose()`, `:326` `_gate.Dispose()` **while A holds it** |
| t6 | `:267` `_held.Dispose()` (already disposed — no-op), `:271` `WriteDurably`, `:272` `_held = OpenHeld(...)` — **re-opens `lock.json` into a disposed object** | |
| t7 | `:289` `_gate.Release()` → `ObjectDisposedException` | `:500` `TreeDelete.Remove` — cannot delete `lock.json` |

End state: `lock.json` is held by a `FileStream` on a disposed `SessionLock` that
nothing will ever close. For the rest of the process's life every `TryAcquire` on
that directory returns `Held`, naming a pid that has no session — and the destroy
reports a partial failure blaming *"something still has them open"*.

**Bound.** Same process, two concurrent tool calls on one session. **What removes
it:** take the gate before the `_disposed` check in `Rewrite`, or give `LiveSession`
a per-session lock that `SetPurpose` and `DisposeAsync` both take.

---

## B5. `InstanceDirectory`'s liveness test has exactly one holder, and it is not the sessions

`InstanceDirectory.Claim:180-195` uses `Directory.Move` as the liveness test:
the rename is refused while a process holds the directory as its current
directory. The measurement at `:31-46` was made *"against a process started with
the directory as its cwd"*.

Exactly one process does that: the **surface child**, `Program.cs:194` passes
`instance` as `workingDirectory`. Session children do **not** — `SessionManager.cs:773-777`
passes `artifacts.OutputRoot`, which is `<session>\output`
(`ArtifactRouter.cs:151`).

The age guard is `Directory.GetLastWriteTimeUtc(directory) > cutoff` at 5 minutes
(`:72`, `:145`). A directory's mtime updates when an entry is created or removed
in it — i.e. when a session config is written — not when files below it change.
So a run that opens its sessions at startup and drives them for hours has an
instance directory older than the cutoff within five minutes.

**The interleaving.** If the surface child dies (crash, `browser_close`
side-effect, OOM) while BrowserAI keeps serving: nothing holds the instance
directory, its mtime is stale, and the next BrowserAI's `CreateFresh → Sweep`
renames it aside and deletes it — taking every live session's generated
`playwright-mcp-<hash>.json` with it. Subsequent `init`/`resume` on that process
writes its config into a path whose parent no longer exists.

**Bound.** Degraded rather than wrong: the configs have already been read by the
running children, so live sessions keep working; only new ones fail.
**What removes it:** have the BrowserAI process itself hold a handle in its
instance directory (the same `.live` mechanism `LiveInstances` already uses), so
the liveness signal does not depend on one child staying alive.

---

## C. Claims I tried to break and could not

**C1. R1 — the sweep cannot kill a browser a peer is mid-`init` on.** `ActOn`
(`StraySweep.cs:533`) gates the kill on `TryHoldUnowned`, which takes the
per-directory gate *and* holds a `ReadWrite/FileShare.Read` handle across the
termination (`SessionLock.cs:355-360`). On the other side, `OpenAsync` holds that
same handle continuously from `TryAcquire` (`SessionManager.cs:754`) through
`ConnectChild` and into `_live`, and the browser is launched inside that window;
the `finally` at `:840-865` releases it only on a failed open. I could not
construct an ordering in which the handle is free while a browser exists. The raw
`File.Exists` in `SessionDirectoryFrom` (`:582`, `:589`) fails to **spare**, and
`TryHoldUnowned` deliberately does not rewrite the record, so the janitor cannot
destroy the evidence either. This one holds as documented.

**C2. Identity is `(pid, creationFileTime)` everywhere I could find.** I checked
every `ProcessId` use in `src/`:

- `SessionLock.cs:542` — `IsAlive(pid, ProcessCreatedFileTime)`.
- `SessionManager.cs:695` (`LiveSessions`) — same pair.
- `StraySweep.cs:460-463` — Restart Manager holders matched on
  `holder.ProcessId == candidate.ProcessId && holder.StartedFileTime == candidate.CreatedFileTime`.
- `BrowserProcesses.StrayCandidate` — a `SafeProcessHandle` is held from detection
  through termination, which makes the pid unrecyclable in the first place
  (`:104`), **plus** a creation-time re-check immediately before
  `TerminateProcess` (`:325-346`).
- `RevisionPrune.cs:178-181` — matches by image-path prefix, never by pid.
- `LiveInstances` — an open file handle, not a pid, and `:33-35` says why.
- `LockRecord` persists both fields and `LockRecord.Read:432` requires the pid.

I found no bare-pid identity comparison. The one asymmetry:
`ProcessLiveness.IsAlive:75-80` returns `false` when `OpenProcess` fails for *any*
reason, so a holder running as another user or at higher integrity reads as dead.
That is the wrong direction on its face — but nothing acts on `previousRunning`.
It reaches `Reclaimed`'s message (`SessionLock.cs:602-623`) and `LiveSessions`'
listing only; the *decision* to reclaim was already made by the kernel's answer to
`OpenHeld`. `SessionLockResult.HolderRunning` is never branched on. So the
mis-read is a wrong sentence, not a wrong action.

**C3. The index's lock-free write.** I tried to break it and could not. The
content is a pure function of the file's own name, so every racing writer writes
identical bytes (measured, 8 × 250 concurrent renames, `processes.md:490-505`).
`FollowOne`'s open (`SessionIndex.cs:341-343`) shares `Delete` so it cannot block
a peer's re-assertion, goes through `RenameWindow` for the denial, and — the part
that matters — a `FileNotFoundException` from the *entry's own* unbind window is
an `IOException`, so it is caught at `:352` and returns `EntryUnreadable`, which
`IsRemovable` **excludes**. Safe direction. The two deliberately-kept states
(`LockUnreadable`, `VolumeMissing`) are correct and asserted. `IsKey`/`IsLitter`
(`:507-518`) cannot match anything this product did not write. The only defect on
this path is A5, and it is in the *pointed-at* `lock.json`, not in the entry.

**C4. R3 — abandoned mutexes.** Handled as an acquisition at every site:
`SessionLock.cs:211-219`, `SessionLock.cs:397-404`, `StraySweep.cs:180-192`,
`BrowserProvisioner.cs:693`. `MachineMutex.Acquire:125-130` converts the exception
correctly and the comment says why folding it into `Acquired` would be wrong.
`RevisionPrune.Run:97-135` acquires every other family's mutex at zero timeout, so
it cannot deadlock against itself, and the `familyAlreadyHeld` skip is not
load-bearing — a Windows mutex is re-entrant per thread and the acquire/release
counts balance even if the skip were missed.

**C5. `LiveInstances` census/join.** Both `Join` (`:98-122`) and `AmIAlone`
(`:155-196`) take the same per-root mutex around the whole operation, which is
what makes "either you are visible to the census or you have not joined yet"
exhaustive. Every uncertainty returns `false` (`:198-204`, `:234-255`). `IsHeld`'s
catch order looks wrong — `FileNotFoundException` *is* an `IOException` — but the
first catch is filtered on HRESULT 32/33, so a missing file falls through to the
right handler. I could not construct an ordering in which two processes both
conclude they are alone.

**C6. `Rewrite`'s failure path does not release the lock.** The comment at
`SessionLock.cs:277-281` describes a real fixed defect, and the fix is correct:
`Reclaim` re-opens before the original failure is rethrown, and if the re-open
also fails it throws a *different* exception that names the state
(`:304-306`) rather than handing back an object that reports ownership it does not
have. The only residue is that on that second failure `_held` keeps a disposed
stream and `_disposed` stays 0 — but every method that touches `_held` is
reachable only through `Rewrite` (guarded) or `Dispose` (idempotent, and disposing
an already-disposed `FileStream` is a no-op). No wrong answer follows.

---

## D. The proposed redesign — probe before the gate

> *A contender should PROBE for the holder before taking the per-directory gate,
> since the sharing violation alone already proves ownership, removing the queue
> entirely.*

The probe is `new FileStream(lock.json, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)`
with no mutex. Three outcomes, and they are not symmetric.

### The probe is a sound ownership test

A sharing violation is the kernel's answer and needs no gate — the gate never made
it more true. This half of the proposal is right, and it is worth having: at the
design point, 100 contenders produce a slowest refusal of 3,349 ms
(`LockScopes.cs:113`) purely because each enters the gate in turn to discover the
file is held. A probe answers all of them with one failed open.

One caveat even here: `Contended` then calls `ReadRecord` (`SessionLock.cs:631`)
to say *who*, and that read is now ungated, so it can return `null` in the unbind
window and print *"held by another process, which removed its lock.json between
the refusal and the read"* (`:646-649`) about a holder that removed nothing.
Degraded, not wrong — the sentence already hedges.

### The probe is an unsound freedom test, and that is where it breaks

**(a) Two contenders can both proceed, with no absence and no aliasing needed.**
This is the case to reject the proposal on. If the gate is skipped on the "free"
path — which is the whole point of removing the queue:

| t | A | B |
|---|---|---|
| t1 | probes, opens, closes → "free" | |
| t2 | | probes, opens, closes → "free" |
| t3 | `WriteDurably` → rename lands → `OpenHeld` → **A holds it** | |
| t4 | | `WriteDurably` → rename **refused** (A's handle is open — measured, any share mode, `ERROR_ACCESS_DENIED`) → retries to `MoveBudget` |
| t5 | anything closes A's handle for an instant — a `Rewrite`, a teardown, a `destroy`'s `held.Dispose()` | |
| t6 | | B's next retry lands. B renames over A's record and re-opens. **B holds `lock.json`; A holds a handle to a now-nameless file** |

Both report ownership. A's handle stays valid — that is what a Windows rename over
an open file does to the loser — so A's own `_held` never tells it anything. A
finds out only on its next `Rewrite`, whose `Replace` fails and whose `Reclaim`
then meets B's sharing violation. In the interval, two processes drive one
profile.

The mechanism this exposes: **with the gate removed, the rename retry loop becomes
the serialiser**, and a retry loop is not a lock — it hands the name to whoever is
retrying at the moment the incumbent lets go.

**(b) The unbind window turns "free" into "fresh".** A probe that lands in the
transient absence gets `FileNotFoundException`, and the measured sequence across
one replace is `denied → absent → the new record`
(`kb/windows/processes.md:374-436`). The kb states the consequence in one line:
*"a retry that handles only the denial converts a throw into a null, which is the
more dangerous of the two because null means not locked."* Today `TakeOrReport`'s
absence is invisible because the rewriter holds the gate; the proposal removes
precisely the thing that makes it invisible. The contender writes
`Created = now` with an empty `PurposeHistory` over a live session's record.

**(c) `UnauthorizedAccessException` is undecidable for this caller.** Delete-pending
and a permanent ACL denial are the same exception. `RenameWindow` resolves that for
*entitled* callers by waiting — and a pre-gate probe is, by `RenameWindow`'s own
table (`RenameWindow.cs:68-77`), the archetype of a **not entitled** caller: it
exists to discover whether something else holds the thing, so for it *the refusal
is the answer* and waiting would invert the mechanism. So the probe cannot
distinguish "somebody is mid-rename" from "I may never open this". Reporting
either as *held* is the safe direction but names the wrong holder; reporting
either as *free* is (a) and (b) again.

### Everything else that can move between probe and gate

| what moves | probe said | truth at the gate | verdict |
|---|---|---|---|
| holder exits | held | free | **retry** — a spurious refusal, self-corrects |
| new holder arrives | free | held | **retry**, *provided* the gated re-open in `TakeOrReport` is kept; **wrong** if the free path skips the gate |
| file rewritten (same holder, new record) | holder H, record R1 | holder H, record R2 | **cosmetic** — the message quotes a stale purpose |
| directory moved or deleted | free/held | gone | **refusal** — `Directory.Exists` (`SessionLock.cs:152`) already races this and already answers `DirectoryMissing` |
| pid recycled | record names pid N | pid N is a stranger | **not wrong** — the probe reports a *record*, and liveness is `IsAlive(pid, creationFileTime)` |
| directory replaced by a junction to a different tree | free | different directory | **wrong**, but that is A4 and is not caused by the probe |

### Verdict

Adopt the probe as a **fast refusal in front of an unchanged `TryAcquire`**, never
as a replacement for the gate on the free path. The queue the proposal wants to
remove is the queue of contenders discovering the file is held — and those are
exactly the callers the probe answers correctly and cheaply. The callers it
answers *incorrectly* are the ones that would then go on to write, and for those
the gate is the only thing standing between the design and two owners.

Concretely: probe → on sharing violation, return `Contended` immediately without
ever creating the mutex; on anything else, fall through to today's
`MachineMutex.Create` → `Acquire(PerDirectoryGate)` → `TakeOrReport`. That keeps
the measured 100-contender behaviour, removes the mutex acquire from the 99% path,
and changes nothing about the write path's guarantees.

---

## Summary table

| # | Finding | Class | Bound |
|---|---|---|---|
| A1 | `destroy` releases the lock before deleting the tree | **wrong / data loss** | needs a peer `init`/`resume` in a directory-walk-wide window |
| A2 | `AcquiredAbandoned` deletes a complete, in-use browser tree | **wrong / data loss** | holder must die after the marker, during `Prune` |
| A3 | `reinstall` deletes without the provisioning mutex | **wrong, durable** | needs a concurrent first-run download |
| A4 | Path aliasing gives one directory two gates | **wrong — two owners** | needs two spellings; `\\?\` needs no setup |
| A5 | `SessionIndex.Sweep` deletes a live session's entry on a transient absence | **wrong report, no self-heal** | invisibility only; nothing treats the index as authority |
| A6 | `init`'s `Existing` guard acts on a transient absence | **wrong** | rebinds mode/browser on a stale-locked directory only |
| B1 | The gate is 60 s and holds up to 90 s of waits | degraded | per-directory; spurious `Busy` |
| B2 | UNC session paths put 21 s calls inside the gate | degraded | per-directory |
| B3 | `LiveInstances` shares the per-directory mutex namespace | degraded | needs a session on the install root |
| B4 | `destroy` racing `set_purpose` leaks the lock forever | degraded → unusable directory | same process, concurrent calls |
| B5 | Instance-directory liveness rests on the surface child alone | degraded | needs the surface child to die first |
