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

This is unmeasured on this machine — `WriteThrough` costs a disk round trip per write, and `lock.json` is written on `init`, on `resume` and on a purpose change, never on the per-call hot path. If it ever appears in a profile, measure it before trading the guarantee away.

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
- **Self-cleaning.** An entry whose directory is gone, or whose directory has no readable `lock.json`, is removed on the next sweep. The index shrinks as sessions are destroyed without anyone maintaining it.
- **Never trusted, only followed.** Every entry is verified by opening the `lock.json` it points at. A personal Chrome profile contains none and cannot be mistaken for ours however it was reached.
- **No lock, by design.** Create and delete are atomic per file, so there is no read-modify-write to synchronise. A wrongly-deleted entry is restored by the next `init` or `resume`. Locking it would put a machine-wide lock on the hot path of every session start to close a race whose cost is one cycle of invisibility.

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

Only the middle row differs. [Detection by image path](C-sessions.md#detection-is-documented-attribution-may-fail-and-must-fail-safe) already covers Firefox for free, because we provision its binary as well — so the Firefox-specific work is attribution alone. `RmStartSession` → `RmRegisterResources(parent.lock)` → `RmGetList` returns `RM_UNIQUE_PROCESS { dwProcessId, ProcessStartTime }`, and that start time is the PID-reuse guard. Mozilla's own `ProfileUnlockerWin::TryToTerminate` does exactly this and is worth copying line for line.

**Restart registration is disabled for every Firefox launch** via `firefoxUserPrefs: { "toolkit.winRegisterApplicationRestart": false }`. The pref is observed at runtime and calls `UnregisterApplicationRestart` — the one place browser resurrection can be prevented outright rather than cleaned up after ([kb: resurrection](../kb/chromium/resurrection.md)).
