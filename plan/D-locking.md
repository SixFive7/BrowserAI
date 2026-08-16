<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# D. Locking and single-instance

Keyed on the **resolved absolute directory**, not on a repository name. Must handle: stale locks from crashed processes, alive-but-orphaned holders, and PID recycling. The existing launcher's mutex + sibling-lockfile + signature-check pattern solves all three and is worth porting rather than redesigning.

**You cannot put a path in a mutex name.** Backslashes are illegal after the `Global\` prefix — `"Global\C:\Source\..."` throws `DirectoryNotFoundException`. Canonicalise and hash instead: `Path.GetFullPath` → `TrimEnd('\')` → `ToUpperInvariant()` → SHA-256 → hex, then `$"Global\BrowserAI-{hash[..32]}"`. (The real length limit is ~32,000 characters, not the documented 260, but hashing is required regardless.) `Global\` also needs `SeCreateGlobalPrivilege`, which interactive users have and low-integrity/AppContainer processes do not. All three are in [kb: object names and window scoping](../kb/windows/detection.md#windows-object-names-and-window-scoping).

Prefer a `FileStream` with `FileShare.None` for the sibling lockfile over the current signature-heuristic approach: the OS releases the handle on process death, so "stale" and "alive" become distinguishable without guessing. Keep the mutex as well — it gives the fast no-IO path.

## `Global\` only, and there is no fallback

**Settled 2026-08-16.** Every named object BrowserAI creates carries the `Global\` prefix, and **if the machine-wide object cannot be created there is no lock, and therefore no session.** `init` fails as a hard blocker, and the reason travels to the calling LLM — the privilege that is missing and the fact that no session was created — rather than being logged and swallowed.

**A `Local\` degraded mode was considered and rejected.** `Local\` names are scoped to the logon session, so falling back to it does not weaken the lock evenly: it removes it precisely where it is needed. Two logons on one machine — a Remote Desktop session beside the console one, a service account beside an interactive user — get two distinct kernel objects for the same directory and neither can see the other, which is the *only* arrangement in which two BrowserAIs contend without either being able to detect it. Single-logon contention, the case `Local\` still covers, is the case a `Global\` lock never had trouble with. The sweep half of this is already recorded as [race R4](C-sessions.md#race-conditions-and-what-closes-each): a `Local\` prefix silently yields per-session mutexes and lets two sweeps run.

**And there is no second line of defence to fall back onto.** Chromium's own profile `lockfile` exists for us only as a consequence of a separate packaging decision — [full Chromium in every mode](../README.md#settled-2026-08-15). `chrome-headless-shell`, which is what upstream reaches for by default, writes no `lockfile` at all: measured 2026-08-14, two headless instances opened the same profile directory, both launched, both worked, and **no error was raised anywhere** ([kb: image-path detection](../kb/windows/detection.md#process-image-path--the-fully-documented-detection-path)). A guarantee that holds only because of a choice made elsewhere in the plan is not one to degrade the lock against.

A lock that narrows its own scope when it cannot get the scope it asked for reports success while guarding nothing. That is the failure class this project exists to eliminate, so the answer is to refuse rather than to descend.

> **This closes the `SeCreateGlobalPrivilege` hazard**, which was recorded with no remedy: `Global\` fails under low-integrity and AppContainer processes, and until 2026-08-16 the plan said only that it does. The remedy is the refusal above, and the row is updated to record what closed it.

## Acquisition is fast, and never waits

**Settled 2026-08-16. A lock attempt is zero-timeout.** On contention BrowserAI returns immediately with an error naming the holder — PID, process creation time, when the lock was taken, and the recorded `purpose` — which is [error 8](H-model-surface.md#h4-the-error-catalogue). **Whether to retry, and how long to wait, is the calling LLM's decision.**

The reason is that BrowserAI cannot know what a wait costs its caller. A timer inside the server converts a fact the agent could have acted on — *"this directory is busy, and here is who has it"* — into an unexplained delay, which is exactly what corrupts whatever timing the calling agent is managing. The same argument already governs [first-run provisioning](A-runtime.md#first-run-browser-provisioning): return the fact, let the caller decide what a wait means for its own work. It also removes a whole class of design question — no retry count, no backoff curve, no configurable timeout, and no hidden interaction between a lock wait and the browser-idle timer.

**One exception, and it is bounded by its own size.** The **per-directory `Global\BrowserAI-{hash}` mutex guarding create-or-take** is held for milliseconds, so it keeps a short internal bounded wait rather than failing on contention. Asking an LLM to retry a 5 ms operation would be absurd; the caller-decides rule exists for waits an agent can reason about, and this is not one.

The discriminator is the *duration the object is held for*, and it is already the column that separates [the three lock scopes](C-sessions.md#the-stray-sweep-and-the-concurrency-it-must-survive): **milliseconds is internal, a session's life is the caller's business.** Applied across all three that gives a rule with no exceptions to remember — the per-directory mutex waits briefly, `lock.json` never waits and returns the holder, and the sweep mutex is [try-acquire-and-skip at zero timeout](C-sessions.md#the-stray-sweep-and-the-concurrency-it-must-survive) because a skipped sweep is not a missed one.

> **This exception is narrower than it first reads, and the difference matters.** It covers the create-or-take mutex only. **[The session index takes no lock at all](#the-session-index-on-disk)** — create and delete are atomic per file and entries are re-asserted idempotently, so there is nothing there to wait on. An earlier phrasing of this decision described the bounded wait as being "around index writes", which would have put a machine-wide lock on the hot path of every session start: precisely the trade the index was designed to avoid.

## Durable `lock.json` writes

**Settled 2026-08-16.** `lock.json` is written with `FileOptions.WriteThrough`, flushed with `Flush(true)` to force the disk cache through, and put in place by an atomic `File.Move` over the previous copy.

> **Supersedes the earlier scheme**, which said `lock.json` was *"rewritten in place on the handle we already own; a reader that catches a torn write retries once."* That is a recovery strategy for a torn read and not a durability guarantee at all: a plain `Write` returns once the bytes are in the file-system cache, so a power loss or a bugcheck between the write and the flush leaves a file that the writer believes it wrote. `lock.json` is the entire ownership guard — [the directory is the identity](C-sessions.md#the-session-directory-is-the-identity) and this file is what proves who owns it — so the one file whose loss cannot be reconstructed is the one being written with the weakest guarantee available. The retry-once behaviour is superseded rather than kept: with an atomic rename a reader sees either the old file or the new one, and never a torn one, so there is nothing to retry.

> **Measured 2026-08-16, and the paragraph this replaces said it was unmeasured.** A durable write of `lock.json` costs **16.1 ms and 18.2 ms** on this machine, over two runs of a hundred sequential rewrites through the real path. It is written on `init`, on `resume` and on a purpose change, never on the per-call hot path, so there is nothing here to trade away and the question is settled rather than deferred. Numbers, method and how to re-run in [kb: durable writes](../kb/windows/processes.md#stdio-exit-codes-and-process-startup).

### The rename and the held handle collide, and the mutex is what resolves it

**Found by building it, 2026-08-16.** [§C](C-sessions.md#the-session-directory-is-the-identity) makes the *open handle* on `lock.json` the lock. This section makes the *atomic rename* the way the record is put in place. Those two requirements cannot both be satisfied by one handle, and neither section said so.

**Measured: a rename cannot replace a file whose handle is open, under any share mode.** `FileShare.Read`, `Read | Delete` and `ReadWrite | Delete` all refuse `File.Move(temp, lock.json, overwrite: true)` with **`ERROR_ACCESS_DENIED`** — not the sharing violation one would expect — because `MoveFileEx` with `MOVEFILE_REPLACE_EXISTING` needs DELETE on the destination. So the obvious repair, adding `FILE_SHARE_DELETE` to the lock handle, does not exist.

The resolution is **close → rename → re-open, entirely inside the per-directory mutex.** That is what the mutex was always for — it guards create-or-take, and this is the create-or-take critical section — but it is now also the thing that makes the design *possible* rather than merely orderly: for the few hundred microseconds in which no handle guards the name, no other BrowserAI can be looking, because every one of them takes this mutex first.

Two consequences worth carrying:

- **A reader must open `FileShare.ReadWrite | FileShare.Delete`.** `ReadWrite` because the holder has the file open for *write* and a reader that does not share write is refused outright — which would turn *"somebody owns this"* into *"this file cannot be read"*, the wrong answer in the dangerous direction. `Delete` because a reader without it blocks every rewrite.
- **The rename's retry budget is bounded by time, not by attempts.** Five attempts over 150 ms — the shape the [C# prior art](../kb/windows/detection.md#named-mutexes-and-lock-files--first-party-prior-art-in-c) uses — was measured exhausting under load with a concurrent reader. Two seconds, backing off 5 ms to a 100 ms cap. And **a failed rewrite must not also release the lock**: the handle is dropped before the replacement, so an exception on the way through left the session silently unowned until the recovery path was added.

## The job object is unnamed

**Settled 2026-08-16, and it is a namespace rule rather than a lifecycle one**, which is why it sits here beside the mutex naming and not in [the job object contract](E-lifecycle.md#zero-process-leakage-the-job-object-contract). Pass `NULL` for `lpName` to `CreateJobObjectW`. The plan already required the *handle* to be non-inheritable; it never said the *object* must be anonymous, and those are two different holes.

A named job object is reachable by any process on the machine that can guess or read the name: `OpenJobObject` takes a name and returns a handle, and a handle to our job is a handle to every browser process BrowserAI has spawned. With `JOB_OBJECT_ALL_ACCESS` that is `TerminateJobObject` on someone else's browser tree, and it is available to anything running as the same user — which, on a developer's machine, is every other tool the developer runs. An unnamed job has no such door: the only handles that exist are the one BrowserAI holds and any it duplicates, and it duplicates none.

Note the asymmetry with the mutexes above, because it looks like an inconsistency and is not. A mutex is named **because** it must be found by another process — that is the whole mechanism. A job object is never looked up by anyone; membership is conferred at `CreateProcessW` and revoked by closing the handle. It has no reason to have a name, and a name it does not need is a name that can only be used against it.

## Never by image name

**Killing a user's own `chrome.exe` or `firefox.exe` must be impossible by construction, not merely avoided.** This is a structural rule, not a review item, because a review already passed on code that would have violated it: our own Chromium probes counted and killed by image name — harmless for Chromium on that machine, and it would have killed ~40 personal `firefox.exe` processes if adapted naively ([kb: the legacy setup](../kb/history.md#the-legacy-setup-and-this-machine)).

The invariant: **BrowserAI can only terminate a process that belongs to a job object it created, or whose identity it verified against a path it owns.** Two mechanisms, no third — *for processes BrowserAI terminates*:

- **The job object** covers everything spawned in this process's lifetime. Closing the handle terminates exactly its members — no name, no PID list, no filter, so there is nothing to get wrong. A user's browser cannot be a member; it was never assigned.
- **Path-keyed identification** covers anything that outlived us. The match is on *our own* session directory, which by construction cannot name a personal profile in `%LOCALAPPDATA%\Google\Chrome\User Data`.

Forbidden outright, and enforceable as an analyzer at error severity: `Process.GetProcessesByName`, `taskkill /IM`, and any WMI or toolhelp query filtered by executable name. Assert zero occurrences in the tree.

> ⚠️ `--user-data-dir` alone is **not** an ownership signal. Measured on the maintainer's machine 2026-08-15: Discord, VS Code, Signal, Teams, WhatsApp, Steam, ChatGPT and four `msedgewebview2.exe` processes all pass it. Only an exact match against a directory BrowserAI created is safe.

## The session index on disk

The index is the only inventory — [there is no root to scan](F-artifacts.md), because there is no default directory. That makes it load-bearing, so it is built to **fail safe rather than to be correct under every race**.

```
%LOCALAPPDATA%\BrowserAI\index\<sha256-of-canonical-path>
```

One file per session directory, named for the hash of its canonical path, containing that path and nothing else. **Files, not the Windows registry** — an earlier draft chose the registry for atomic single-value writes under ~100 concurrent processes, and that argument evaporated when [enumeration replaced inventory lookup in the sweep](C-sessions.md#the-stray-sweep-and-the-concurrency-it-must-survive). The index is now read only by `browserai_list` and by cleanup, so contention is low, and files win on being inspectable, deletable and free of profile roaming.

**Canonicalisation must match the lock's**, or the same directory gets two identities: `Path.GetFullPath` → `TrimEnd('\')` → `ToUpperInvariant()` → SHA-256 → hex. One function, used by the mutex name, the lock and the index alike, and tested to agree with itself.

Four properties, each deliberate:

- **Written on every `init` *and* every `resume`, idempotently.** Re-asserting rather than writing-once is what makes a lost entry self-heal: losing one costs a single sweep cycle of invisibility, not a permanently orphaned directory. This is what lets the store skip locking entirely.
- **Self-cleaning.** An entry whose directory is gone, or whose directory **has no `lock.json` at all**, is removed on the next sweep. The index shrinks as sessions are destroyed without anyone maintaining it.

  > ⚠️ **Corrected 2026-08-16 at [step 11](build-order.md#11-the-session-index) (previously "or whose directory has no *readable* `lock.json`").** *Readable* was the wrong word and it removes exactly the entries that can never come back. A directory whose `lock.json` is **present but unparseable** is a session — a broken one — and `SessionLock.TryAcquire` refuses it (`Unreadable`), so there is no `init` and no `resume` that would re-assert its entry. The self-healing argument, which is what licenses removal at all, does not hold for it: sweeping it makes a directory that still exists **permanently invisible to the only inventory there is**, which is [the founding failure shape](../README.md#read-this-before-designing-anything) rather than a tidy index. The refusal is measured on every build by `SessionIndexTests.AnEntryWhoseLockFileCannotBeParsedIsKeptBecauseNothingElseCanRestoreIt`, which asserts the `Unreadable` outcome in the same test that asserts the entry survives.
  >
  > **A second state is kept for the same reason: an unmounted volume.** `Directory.Exists` cannot tell *destroyed* from *not plugged in*, and a session on a disconnected drive or an unreachable share has not been destroyed. The predicate therefore checks the path root before concluding the directory is gone. Kept, with the reason on the entry.
  >
  > **And the sweep clears the store's own rename litter** — a `<key>.new-<guid>` temp left by a process killed between the write and the rename — bounded by both the name pattern and an age, so it can never touch a file this product did not write or one a live writer is about to rename.
- **Never trusted, only followed.** Every entry is verified by opening the `lock.json` it points at. A personal Chrome profile contains none and cannot be mistaken for ours however it was reached — including the near-miss of Chromium's own `lockfile`, which the match must not accept.

  > **Following is verification of the entry too, not only of what it names.** An entry must be **absolute** (a relative pointer would resolve against whatever working directory the reader happens to have) and its **file name must be the hash of its own content** (a mismatch is a second inventory line for a directory that already has a correctly-named one, and no sweep would ever converge on it). Both fail as *unusable* and are removed.
- **No lock, by design.** Create and delete are atomic per file, so there is no read-modify-write to synchronise. A wrongly-deleted entry is restored by the next `init` or `resume`. Locking it would put a machine-wide lock on the hot path of every session start to close a race whose cost is one cycle of invisibility.

  > **Measured 2026-08-16, twice: 8 processes × 250 re-assertions = 2,000 renames over one name, one valid file, zero failures, every process exit 0.** The write is a GUID-named temp in the index directory and `File.Move(overwrite: true)`, so concurrent writers serialise in the filesystem and a reader sees the old entry or the new one and never a torn one ([kb](../kb/windows/processes.md#stdio-exit-codes-and-process-startup)). There is deliberately **no** "already correct, skip the write" fast path: after the first write nobody would contend, and the concurrency test would then pass while proving nothing.
- **A fifth property, added at [step 11](build-order.md#11-the-session-index) because building it made the omission obvious: the write is neither durable nor fatal.** No `WriteThrough` and no `Flush(flushToDisk: true)` — measured at **1.7 ms alone and 9.2 ms under 8-way contention, against ~17 ms for the durable `lock.json` write** ([kb](../kb/windows/processes.md#stdio-exit-codes-and-process-startup)). Durability protects a fact that cannot be reconstructed, and an index entry is a pure function of a directory that is about to be used again. For the same reason **recording never throws**: a failure is logged at Warning and the session proceeds, because a session that failed to open because its *inventory line* could not be written would make a self-healing store load-bearing in the one direction it was designed not to be.

> **This is not the registry that was dropped**, and the distinction is structural rather than nominal. That registry held handle mappings, config and liveness — state two BrowserAIs could disagree about, where a stale entry was a bug and every write needed a mutex. This holds one immutable fact per file: *a session directory once existed here*. It cannot be wrong in a way that matters.

## Firefox: the preflight, and a second detection path

Firefox needs both halves of the stray story rebuilt, because neither Chromium mechanism transfers.

**The preflight is mandatory, not defence in depth.** Playwright's `isProfileLocked` checks only Chromium's `lockfile` and never Firefox's `parent.lock` ([kb: profiles](../kb/chromium/profiles.md)), so a collision raises a **native modal on the user's desktop that blocks for up to three minutes** — an invisible hang in a background server. Before launching Firefox, open `<profile>\parent.lock` for write; on `ERROR_SHARING_VIOLATION`, refuse with [error 11](H-model-surface.md#h4-the-error-catalogue) rather than launching.

Taking our own lock first already makes the collision unreachable — but that is **coverage by ordering**, and ordering is exactly the kind of guarantee that survives a refactor unnoticed. The preflight makes it explicit, and a test asserts it fires when the lock is held.

**Detection needs a different mechanism.** Firefox publishes no `Chrome_MessageWindow` equivalent, and its `parent.lock` is **never deleted** — Mozilla keeps it deliberately, using the mtime to detect startup crashes — so unlike Chromium's, its existence proves nothing. Only a sharing violation does.

| Step | Chromium | Firefox |
|---|---|---|
| Is a stray running at all? | image path under our binary | **identical** — we provision Firefox too |
| Which profile is it on? | exact-title `Chrome_MessageWindow` lookup | **`parent.lock` sharing violation → `RmGetList`** |
| PID → identity | `(pid, creationFileTime)` | identical |

Only the middle row differs. [Detection by image path](C-sessions.md#detection-is-documented-attribution-may-fail-and-must-fail-safe) already covers Firefox for free, because we provision its binary as well — so the Firefox-specific work is attribution alone. `RmStartSession` → `RmRegisterResources(parent.lock)` → `RmGetList` returns `RM_UNIQUE_PROCESS { dwProcessId, ProcessStartTime }`, and that start time is the PID-reuse guard. Mozilla's own `ProfileUnlockerWin::TryToTerminate` does exactly this.

> ⚠️ **Corrected 2026-08-16 @ [step 17](build-order.md#17-firefox) (previously "…and is worth copying line for line").** **Do not copy it.** Mozilla's source is **MPL-2.0**; this repository is `LicenseRef-BrowserAI-FSL-1.1-MIT-5yr`, and taking its text would put a file of ours under terms the charter does not carry. What is reproduced is the *sequence*, which is the documented API contract and belongs to nobody — the same route [step 9](build-order.md#9-lossless-passthrough) took when it implemented parse-error recovery from the MCP SDK's observed behaviour rather than from its Apache-2.0 code, and said so. The design is unchanged; only the instruction was wrong.
>
> **And attribution is never actionable on its own.** `RmGetList` answers about whatever file it is handed, so pointing it at a personal Firefox profile names the user's own browser — verified against exactly that on the maintainer's machine. A holder becomes a stray only where it is **already** a candidate of the image-path scan and its start time matches, and only where the session's own `lock.json` can be taken. Three guards, and the Restart Manager is the weakest of them.

**Restart registration is disabled for every Firefox launch** via `firefoxUserPrefs: { "toolkit.winRegisterApplicationRestart": false }`. The pref is observed at runtime and calls `UnregisterApplicationRestart` — the one place browser resurrection can be prevented outright rather than cleaned up after ([kb: resurrection](../kb/chromium/resurrection.md)).
