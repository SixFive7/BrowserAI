<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Narrow adversarial re-review — 2026-08-24, tree `a74281a`

Read-only. Nothing was built and nothing was run; every claim below is source-level and
carries `file:line`. Scope is the seams `dbf1346` and `a74281a` touched and where they
meet code that predates them.

> ⚠️ **One span was rewritten when this record was copied out of `.work/` and registered
> here, and it is named here rather than left to a diff nobody keeps.** Item 9 of
> *Attacked and held* enumerated the upstream pointer shapes and spelled a Markdown link
> inline. `DocumentationLinkTests` reads raw text and does not skip code spans, so that
> span was a relative link into `docs/reviews/` pointing at a file that does not exist,
> and registering the record unchanged would have reddened the build. The shape is
> described in words there instead. Nothing else in this file was altered.

---

## Wrong

### W1 — HIGH · F7 · The pointer scan compares a **JSON-escaped** answer against a **raw** file name, so ordinary names containing `&`, `'` or `+` are never pinned and the sweep moves them out from under upstream's own download pointer

`BrowserProxy.cs:641` and `BrowserProxy.cs:670` both pass `response.Result?.ToJsonString()`
— no `JsonSerializerOptions`, therefore `JavaScriptEncoder.Default`, which escapes `&`,
`'`, `+`, `<`, `>`, backtick and every non-ASCII character as `\uXXXX`.

`ArtifactRouter.NoteWhatTheAnswerPublished` (`ArtifactRouter.cs:795-818`) compares that
string against `Path.GetFileName(file)` — the raw name off disk — through `NamesTheFile`
(`ArtifactRouter.cs:860-872`), which is `String.IndexOf` on the literal.

**The positive control is inside the same file.** `ArtifactRouter.cs:172-180` sets
`Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping` for the artifact index with the
comment *"The default encoder additionally escapes characters a path may legitimately
carry"*. The fact is known here and is not used by the scan two hundred lines away.

**Input that breaks it.** A browser-initiated download whose site-chosen name is
`AT&T.pdf`, `its.pdf` with an apostrophe, `a+b.pdf`, or any non-ASCII name. Upstream
answers `- Downloaded file AT&T.pdf to "./AT&T.pdf"` — the exact shape
`ArtifactRouter.cs:875-885` quotes from the resolved bundle. The serialised answer carries
`AT\u0026T.pdf`; `IndexOf` finds no occurrence; the name never reaches `_published`; the
sweep at `ArtifactRouter.cs:944-972` moves the file into `downloads\` and upstream's
`./AT&T.pdf` pointer names nothing. That is verbatim the defect the whole `_published`
mechanism exists to close, still reachable with an ASCII-only, perfectly legal Windows
filename.

Pre-existing — the old bare `Contains` had it too — so this is not a regression. But F7
rewrote this function and its new remarks reason explicitly about the serialised form
(`ArtifactRouter.cs:881-885`: *"the answer this reads is the SERIALISED result, so a quote
arrives as `\"` and a separator as `\\`"*) without reaching the escaping of the name
itself. The comment now reads as though serialisation had been accounted for.

---

### W2 — HIGH/MEDIUM · F7 · The new eviction can drop a name that is still pointed at, under two concurrent `tools/call`s on one session

`ArtifactRouter.cs:986-992` evicts from `_published` and `_recorded` every name absent from
`present`, where `present` is built from `loose` — the enumeration taken at
`ArtifactRouter.cs:902`, at the *top* of `SweepOutputRoot`.

`NoteWhatTheAnswerPublished` (`:795`) and `SweepOutputRoot` (`:894`) take `_gate`
separately — `:812`, `:915`, `:949`, `:986` — so two `Complete` calls interleave.

**The interleaving.** Call B enters `SweepOutputRoot` and snapshots `loose` at `:902`,
before call A's console log exists. Call A then publishes `console-X.log` at `:816`. Call B
reaches `:990` and removes `console-X.log` from `_published`, because its snapshot does not
carry it. Call A's answer has just told the caller
`- New console entries: console-X.log#L1-L24`; the next sweep finds the file unpinned and
moves it into `output\console\`, and the pointer names nothing. That is the original
reproduced defect, reintroduced.

Nothing serialises forwarded calls per session: `BrowserProxy.AnswerToolsCallAsync` has no
per-session gate, and `ChildConnection.AskAsync` (`ChildConnection.cs:218-248`) allocates a
fresh id per request and awaits. `ArtifactRouter`'s own `Lock _gate`
(`ArtifactRouter.cs:184`) exists precisely because concurrency is expected here.

Before `dbf1346` the set was monotone and this could not happen; the eviction is what
introduces it. **I did not verify from source that the MCP SDK dispatches two `tools/call`
frames concurrently** — that is the one link in the chain I could not close read-only. If
it serialises them, this is unreachable.

---

### W3 — MEDIUM · F7/F8 · `SweepOutputRoot` adds to `_reserved` and never releases — the F8 leak survives in the sweep path, and `Release`'s new *Corrected* clause claims it does not

`_reserved` has exactly three sites: added at `ArtifactRouter.cs:354` (`Plan`), added at
`ArtifactRouter.cs:952` (`SweepOutputRoot`), removed at `ArtifactRouter.cs:535` (`Release`,
which removes `plan.AbsolutePath` only). **Line 952's additions are never removed.**

**Break 1 — a failed move reserves a name forever.** `File.Move` throws at `:958`, the
handler `continue`s at `:964`, and `target` stays in `_reserved` with no file there. The
next sweep of the same loose name calls `Unique` → `Taken` (`:723`,
`_reserved.Contains(candidate) || File.Exists(candidate)`) is true → it lands as `-2`, and
`Record` at `:967-972` reports `renamedFrom: <name>`. That is *"the answer reporting a
rename that no file on disk justifies"* — the F8 symptom, in the method F7 edited.

**Break 2 — a deleted sorted file keeps its name.** After a successful sort the caller
deletes `downloads\report.pdf`; the reservation persists, so the next download of that name
is suffixed. `Release`'s new remark (`ArtifactRouter.cs:519-523`) states the opposite:
*"a file the caller later deletes stops holding a name it no longer occupies — which is
behaviour the reservation set on its own was never able to give."* True of plan-reserved
names; false of sweep-reserved ones, and the clause does not distinguish.

---

### W4 — MEDIUM · F9 · The filter moved above the **record** open but not above the **entry-file** open, which carries the same `RenameWindow` budget

`SessionIndex.FollowOne` opens each index entry through `RenameWindow.WaitOut` at
`SessionIndex.cs:438`, and the subtree filter is at `SessionIndex.cs:502` — *below* it.
`RenameWindow.Budget` is 30 s (`RenameWindow.cs:173`) and the one-argument `WaitOut`
retries `UnauthorizedAccessException` for the whole budget (`RenameWindow.cs:268-271`).

So one denied or delete-pending **index entry file** anywhere on the machine still adds up
to 30 s to a `browserai_list` scoped to an unrelated tree, and to the roll-up on every
`init` and every `resume`. The number of opens per call is unchanged — one per index entry
on the host; only the *parse* moved.

`ARCHITECTURE.md:561-568` and `SessionIndex.cs:225-236` attribute the budget exposure
entirely to the `browserai.json` open and present the fix as having removed it. A reader
takes from both that a subtree-scoped call can no longer be delayed by a stranger's file.
It can.

---

### W5 — MEDIUM-LOW · F5 · `ERROR_LOCK_VIOLATION` is classified as a holder, and no BrowserAI holder can produce it — so a foreign byte-range lock is reported as a reinstall in progress

`MaintenanceDenial`'s remark (`MaintenanceLock.cs:489-494`) asserts that
`ERROR_SHARING_VIOLATION` and `ERROR_LOCK_VIOLATION` *"are the only codes a holder produces
on this open"*. `RenameWindow.IsSharingViolation` (`RenameWindow.cs:248-253`) accepts both;
`TakeShared` (`MaintenanceLock.cs:208-210`) and `TryTakeExclusive` (`:279-281`) map both to
`Contended`; `TheRootCouldNotBeClaimed` (`SessionManager.cs:1335-1341`) then falls through
to `ReinstallHoldsTheRoot`.

But `reinstall.lock` is claimed by **share mode only** (`MaintenanceLock.cs:185-189`), and
the tree's only byte-range lock is `NativeFile.TakeGate` / `LockFileEx`
(`NativeFile.cs:203`), reachable solely through `OpenForLockedAppend` on **log** files —
grepped `.Lock(`, `LockFileEx` and `FileStream.Lock` across `src/`, and that is the only
site. So no `MaintenanceLock` holder ever produces code 33; 33 can only come from a foreign
process, and it is routed to *"BrowserAI is replacing the browsers … right now"* with a
progress clause counting from zero.

The wider version — code 32 raised by an AV scanner, a backup agent or an indexer holding
`reinstall.lock` — was **considered and deliberately left unhedged**
(`SessionManager.cs:1322-1329`), and I am not re-litigating that trade. What is wrong is the
arithmetic sentence in the enum: it establishes *not 32/33 ⇒ not a holder*, which is the
direction the `Unreachable` arm needs, and is then relied on for the converse, which it does
not establish and which is provably false for 33.

---

### W6 — LOW/MEDIUM · F9 doc · `SessionIndex.Follow`'s remark claims `HouseRuleTests` asserts three named callers keep using `Follow`. It asserts no such thing

`SessionIndex.cs:203-208`: *"**Three callers must keep using this one** … `Sweep` …
`SessionManager.LiveSessions` … and `StraySweep` … `HouseRuleTests` asserts that rather than
leaving it to a reader."*

`HouseRuleTests.NoIndexWalkFiltersBySubtreeAfterFollowingTheEntry` asserts (a) no
`foreach (… .Follow())` body contains `IsUnder(`, and (b) `walks >= 2`. It never names
`Sweep`, `LiveSessions` or `StraySweep`, and its own comment concedes `Sweep` is invisible to
it — *"it calls `Follow()` on itself and reads the result into a local, which is a shape this
scan does not see"*. Two of the three callers are held only by a `>= 2` lower bound on a loop
shape; the third is held by nothing.

---

### W7 — LOW/MEDIUM · F9 doc · `FollowUnder` is **not** `Follow` filtered, and the file says both things two paragraphs apart

`SessionIndex.cs:229-231`: *"the entries this returns are exactly the entries `Follow` would
have returned for the same subtree, followed the same way; `SessionIndexTests` asserts that
equivalence directly."*

`SessionIndex.cs:232-242`, immediately below: *"An entry this cannot compare is **returned
rather than dropped**."*

The code agrees with the second: every refusal above `SessionIndex.cs:502` returns before the
`under` test, so `FollowUnder(p)` is `Follow().Where(e => e.Session is null || IsUnder(e.Session, p))`.
**Falsifying input:** an index entry that is empty, relative or mis-hashed and points nowhere
near `p` — returned by `FollowUnder(p)`, not returned by `Follow()` filtered by `IsUnder`.

`SessionIndexTests.FollowingOneSubtreeReturnsExactlyWhatFollowingEverythingWouldHaveReturnedForIt`
builds `expected` as `Follow().Where(entry => entry.Session is { } session && …)` — which drops
exactly the divergent class — and never plants one. So *"asserts that equivalence directly"*
names a test that excludes the only case where the equivalence fails.

Harmless in the product today: `List` (`SessionManager.cs:821`) and `Beneath`
(`SessionManager.cs:2195`) both drop `Session is null` on the next line.

---

### W8 — LOW/MEDIUM · F9 doc · `FollowUnder`'s parameter contract names a producer that one of its two callers does not use

`SessionIndex.cs:245-248`: *"A case-folded, separator-terminated path prefix, as
`SessionManager.Subtree` produces it."*

`Beneath` (`SessionManager.cs:2186-2189`) re-derives the prefix itself —
`root.ToUpperInvariant()`, then append a separator — and never calls `Subtree`; in particular
it skips `Subtree`'s `Path.GetFullPath` (`SessionManager.cs:2141`). Benign today, because
`root` is `Path.GetDirectoryName(location.FullPath)` off an already canonical `SessionPath`.
But `IsUnder`'s own new remark (`SessionIndex.cs:398-406`) says *"**Do not re-derive it** …
Two spellings of this predicate is the class of defect this repository keeps re-finding, which
is why the copy that used to live in `SessionManager` was deleted rather than left beside this
one"* — and a second spelling of the **prefix** is still standing in `SessionManager`,
unmentioned. The duplication pre-dates `dbf1346`; the sentence that says it was removed does
not.

---

### W9 — LOW · F8 · The release record fires on every forwarded call, including every call that reserved nothing, and its own remark says otherwise

`BrowserProxy.cs:678-682`: the `finally` calls `Release(plan)` — a no-op when
`plan is not { Writes: true }` (`ArtifactRouter.cs:528-531`) — and then logs
**unconditionally**. The message (`BrowserProxy.cs:1239`) is *"'{Tool}' on the session at
{Session} released its in-flight filename reservation."*, written for `browser_click`,
`browser_navigate`, `browser_snapshot` and every other call that named no file. Its own remark
(`BrowserProxy.cs:1227`) says *"it fires on every call that named a file, which is most
screenshots."*

In a session log that is model-facing evidence, this asserts an event that did not occur.

**Second-order.** `ArtifactRoutingTests.ACancelledCallGivesItsReservedNameBackSoTheRetryIsNotSuffixed`
waits on that exact string (`ArtifactRoutingTests.cs:865`). Sound today only because the
screenshot is the rig's first forwarded call; any earlier forwarded call in the same rig would
satisfy the wait before the cancellation and make the test pass vacuously.

---

### W10 — LOW/MEDIUM · `SuiteFilter` · `BROWSERAI_RELEASE_RUN=1` makes `FILTERED` a failing test only for filters that happen to select the refusing test

The refusal lives in `SuiteCoverageTests.ARunThatWasFilteredIsNeverARelease`
(`SuiteCoverageTests.cs:448-461`) — an ordinary `[Test]`. A run with
`BROWSERAI_RELEASE_RUN=1` and `--treenode-filter /*/*/SessionLockTests/*` is filtered, is a
claimed release, and is **green**: nothing selected the assertion.

`TESTING.md:394` states it unqualified: *"**`BROWSERAI_RELEASE_RUN=1` makes `FILTERED` a
failing test**, and `UNREAD` with it"*. Nothing in `TESTING.md`, `RELEASING.md` or
`SuiteFilter` records the exception.

The `filter` row itself survives any filter — `[After(TestSession)]` runs regardless — so the
*premise* half of the mechanism is intact, which is what `SuiteFilter.cs:87-95` claims for it.
What does not survive is the *refusal*, in exactly the class of run it guards. The real gate is
unfiltered (`RELEASING.md:374`, `:379`), so no shipped release is affected.

---

### W11 — LOW · `SuiteFilter` · the child positive control's console assertion is a tautology, under a comment claiming it is the decisive half

`SuiteCoverageTests.cs:578`:
`await Assert.That(console).Contains(SuiteEnvironment.ReleaseRunVariable).Because(console);`
sits under *"⚠️ AND THE REFUSAL FIRED."*

`SuiteFilter.RowFor`'s `Filtered` arm (`SuiteFilter.cs:346-352`) ends with
`"{SuiteEnvironment.ReleaseRunVariable}=1 makes this state a failure."`, and
`SuiteCoverage.ReportWhatThisRunExercised` writes the whole block through the **real** standard
output handle (`SuiteCoverageTests.cs:66-70`), which the parent redirects into `console`. A
filtered child therefore prints that variable name whether or not any assertion fired.

The weight is carried entirely by `exitCode != 0` and by `decision=Refuse` in the report file —
both sound. The console assertion adds nothing and reads as though it added the half the exit
code cannot.

---

### W12 — LOW · `SuiteFilter` · the child run clobbers the parent's `.work/suite-coverage.txt` mid-run

`SuiteCoverage.ReportPath` (`SuiteCoverageTests.cs:25-26`) is repository-rooted and not
per-process. The child's `[After(TestSession)]` writes a one-test `FILTERED` block over it while
the parent is still running; the parent rewrites it at its own session end, so the copy a gate
log appends is the parent's. Anyone reading the file during the window gets the child's.
Related: this is a second test host in the same app root while the first is running, which is
the arrangement `CLAUDE.md` says must be serialised — benign here only because the child's
single selected test does no product I/O.

---

### W13 — LOW · F1 doc · the *"takes no lock"* sweep corrected four sites and missed a fifth, and the missed one carries the mechanism claim the same commit corrected

`tests/BrowserAI.Tests/Harness/SessionRun.cs:173-175`: *"the arm that proves it takes no lock,
because anything that did would be refused by the holder above."*

Both halves are now false. `catch_up` → `InUse` (`SessionManager.cs:709`) →
`ProbeLivenessUnderTheGate` (`SessionLock.cs:1032`) takes the per-directory gate; and
`SessionToolSurface.cs:96-102` corrected exactly this reasoning in the same commit —
*"`LockScopes.PerDirectoryGate` does not refuse a second writer, it waits 120 seconds for it."*
The arm still passes because `InUse`'s first branch (`SessionManager.cs:939-942`)
short-circuits for a session this process drives, so it never reaches the gate and does not
prove what the comment says it proves.

---

## Unproven

- **U1 — the lister does not queue, but it now makes others queue.** `SessionLock.cs:1010-1015`
  says the zero timeout means it *"never waits, never queues and therefore does not re-create
  the contention `ProbeForHolder` was extracted to remove."* The lister's own exposure is gone.
  It now *holds* the gate for one `CreateFile`/`CloseHandle` per entry, so a peer's `Rewrite` or
  `TryAcquire` on that directory can wait behind it. Small, and I could not measure it
  read-only; the sentence covers the lister and is silent about the peer.
- **U2 — restricted tokens.** `MachineMutex.Create` needs a `Global\` object; without
  `SeCreateGlobalPrivilege` it throws and `ProbeLivenessUnderTheGate`
  (`SessionLock.cs:1046-1058`) returns `Undetermined`, so every `in use:` line becomes `UNKNOWN`
  where it previously read `YES`/`no`. `MachineMutex.cs:36` says such a machine has no sessions
  at all, which limits this to listing sessions created elsewhere. I did not establish
  reachability.
- **U3 — a throwing logger in the `finally`.** `ProxyLog.ReservationReleased` runs at
  `BrowserProxy.cs:681`, inside the `finally`. If any provider in this tree throws once
  disposed, it would replace an in-flight `OperationCanceledException`. I did not establish
  that any provider does; this is a shape, not a defect.
- **U4 — `Judge` depends on TUnit spelling "no filter" as `null`.** `SuiteFilter.cs:245-251`
  treats `Global: "", Session: ""` as `Filtered` (with an empty filter quoted) and
  `Global: null, Session: ""` as `Disagreed`. Both would refuse every release. It reads `null`
  at TUnit 1.65.0 — the gate prints `FULL RUN` — but nothing in the type pins the spelling.

---

## Attacked and held

The list is the point: it is what makes the section above worth reading.

**F8 — the `finally`**

1. **Ordering vs `Complete`.** `Complete` is inside the `try` at `BrowserProxy.cs:641` and
   `:670`; `Release` is in the `finally` at `:680`. The artifact is recorded before the name is
   given back on every path — the remediation return at `:654`, the ordinary return at `:673`,
   the child-failure path at `:676`. A concurrent call cannot take the name before the record
   exists.
2. **Double release.** `Complete` releases internally at `ArtifactRouter.cs:423-427` when the
   file is absent, and the `finally` releases the same plan again. `HashSet.Remove` is
   idempotent; no defect.
3. **Exception coverage.** `Complete` throwing, `WithNote` throwing, either `SendMessageAsync`
   throwing, the idle-timer scope, the 1,000 ms regex timeout and `OperationCanceledException`
   all unwind through the same `finally`. Release-after-a-successful-write is safe because
   `Taken` (`ArtifactRouter.cs:723`) also tests `File.Exists`.
4. **Cancellation with a written file.** If the child wrote the file before the cancel, the
   retry is correctly suffixed, because `File.Exists` holds the name even though the reservation
   is gone. The claim is exactly as strong as stated.

**F2 — the rewrite branch**

5. **Provenance.** `Remediate` deep-clones at `BrowserProxy.cs:734` and mutates only the copy,
   so `response.Result?.ToJsonString()` at `:641` really is the child's own answer, not the
   rewritten one. The comment's claim holds.
6. **Note parity.** `WithNote` (`BrowserProxy.cs:883-899`) appends both the note node and the
   image node, so the remediation path carries the inline image the ordinary path carries. What
   it loses is only the verbatim splice — already conceded on that branch — and the
   `InlineImageRestored` debug record.
7. **Gate robustness.** `result["isError"]?.GetValueKind() is not JsonValueKind.True`
   (`BrowserProxy.cs:727-729`) survives `"isError": 5`, `"isError": "true"`, a missing key and
   an explicit null. `GetValue<bool>()` would have thrown on the first.
8. **The conceded bypass.** Page content can still reach the rewrite through an `isError`
   answer — the code says so at `BrowserProxy.cs:712-720` and the test sets `isError`
   deliberately. Now harmless, because that branch runs `Complete`.

**F7 — the delimiter**

9. **Every upstream pointer shape recorded in this tree.** A Markdown link with target
   `./page-….yml` (written out in words rather than spelled inline — see the note at the
   top of this file),
   `- New console entries: console-….log#L1-L24`, `- Downloaded file x.pdf to \"./x.pdf\"`, and
   a Windows separator arriving as `\\`. All put a non-`[A-Za-z0-9_.-]` character on both sides,
   so `NamesTheFile` (`ArtifactRouter.cs:860-872`) matches all of them.
10. **Both-sides-must-be-boundaries is the right form.** It correctly rejects `report.pdf`
    inside `quarterly-report.pdf` (left neighbour `-`) and inside `report.pdf.bak` (right
    neighbour `.`). I could not construct a legitimate pointer it rejects, other than through
    W1's escaping.
11. **Eviction, single-threaded.** I could not construct a sequence in which a name still
    pointed at is dropped. A published file is never moved by the sweep
    (`ArtifactRouter.cs:941` `continue`), so it stays in `loose` and therefore in `present`. The
    stated cost — delete-and-recreate before any answer names it again — is the only
    single-threaded loss, and it is written down.

**F9 — the filter's position**

12. **Set equivalence for every state a caller sees.** Every early return above
    `SessionIndex.cs:502` produces `Session is null`, and both callers drop those on the next
    line, so the reported sets really are identical. (The API-level divergence is W7.)
13. **Prefix spelling and separators.** `Subtree` (`SessionManager.cs:2141-2147`) and
    `SessionPath.Key` (`SessionPath.cs:52`) both `ToUpperInvariant`; `IsUnder`
    (`SessionIndex.cs:408`) compares `Ordinal` on a separator-terminated key. `C:\Foo`,
    `C:\Foo\` and a volume root `C:\` all produce what they produced before, and the predicate
    is the same instance the deleted copy was.
14. **`ReFollow`.** `FollowOne(entry.EntryFile, entry.Key, under: null)!`
    (`SessionIndex.cs:357`) — with `under` null the method cannot return null, so the
    null-forgiving operator is sound.

**F1 — the gated probe**

15. **Namespace.** `location.MutexName` is `Global\BrowserAI-<32 hex>` (`LockScopes.cs:91`); the
    live-instance set is `Global\BrowserAI-Live-<32 hex>` (`LiveInstances.cs:724`) and a hex
    digest can never spell `Live-`, so the locking-B3 collision cannot recur. The sweep is
    `Global\BrowserAI-Sweep` (`LockScopes.cs:111`). The gate taken is the same one `TryAcquire`
    (`SessionLock.cs:263`) and `Rewrite`/`TryHoldUnowned` (`:619`) take, which is what makes it
    the discriminator.
16. **Acquire/release discipline.** `ProbeLivenessUnderTheGate` (`SessionLock.cs:1032-1085`)
    acquires and releases on one thread with no `await` between; treats `AcquiredAbandoned` as
    acquired and releases it; releases in an inner `finally` and disposes in an outer one; and
    its catch list is exactly the four types `MachineMutex.Create` documents
    (`MachineMutex.cs:99-103`). Re-entrancy is unreachable — no caller holds the gate on the way
    in.
17. **Failure direction.** Every way the gate can fail returns `Undetermined` with a reason
    (`SessionLock.cs:1053-1071`), never `NotHeld`. The one direction that costs a caller a
    session is unreachable from this code.
18. **`catch_up` on a busy session.** Still answers; `InUse` is a report line only, and the zero
    timeout means the tool cannot be refused into a failure.

**F5 — carrying the kernel's answer out**

19. **Definite assignment.** `TakeShared` (`MaintenanceLock.cs:174-216`) and `TryTakeExclusive`
    set `denial`/`detail` before the `try` and reassign in the only catch, and the only
    `return null` is inside that catch — so `claim is null` implies `denial != None`, and
    `TheRootCouldNotBeClaimed`'s `else` arm (`SessionManager.cs:1335-1341`) is never reached
    with `None`.
20. **Ordering in the census.** `TheRootIsBusy` checks `Unreachable` at
    `SessionManager.cs:1398` — **before** `LiveSessions()` at `:1403` — so a census of zero over
    a file nothing could open can no longer conclude *"another reinstall has it"*. That is the
    fix and it is placed correctly.
21. **The `Unreachable` direction of the arithmetic.** *Not 32/33 ⇒ not a holder* is sound on
    this open's share mode, so the new row is never shown to a caller who really is behind a
    reinstall. Only the converse (W5) fails.

**`SuiteFilter`**

22. **The latch cannot read too early.** Taken in `[Before(TestSession)]`
    (`SuiteCoverageTests.cs:40-41`), published through `Volatile` (`SuiteFilter.cs:169-172`),
    with `TestSessionContext.Current` carried as a separate witness so a too-early null is
    `Unread` and never `FULL RUN` (`SuiteFilter.cs:247`). `TheRunReportsWhetherItWasFiltered`
    re-reads the seam from inside a test and compares it to the latch, so a stale latch is a red
    run.
23. **The latch cannot read too late.** It is a `[Before(TestSession)]` hook, so no test can
    observe the pre-latch state; and if the hook never runs, `Taken: false` → `Unread` → a
    release refuses. Every failure direction is fail-safe.
24. **A full run cannot be misread as filtered.** `Judge`'s `NotFiltered` arm requires both seams
    null (`SuiteFilter.cs:248`), which is what an unfiltered TUnit 1.65.0 run produces — subject
    to U4.
25. **Recursion in the child control.** The child's filter names
    `ARunThatWasFilteredIsNeverARelease` and the launcher is
    `AFilteredChildRunReadsAsFilteredAndIsRefusedAsARelease`, so it cannot select its own
    launcher; `BROWSERAI_FILTER_PROBE` (`SuiteFilter.cs:167`) is the second bolt and is set only
    by the launcher. Both hold.
26. **What the child control does prove.** The report-file assertions — `verdict=FILTERED`,
    `global=<the exact filter>`, `session=<the same>`, `decision=Refuse` — plus `exitCode != 0`
    are sound and together do establish that a genuinely filtered run reads as filtered and that
    the refusal fires. Only the console assertion (W11) is empty.
27. **Pipe safety.** `ReadToEndAsync` on both streams is started before `WaitForExitAsync`, so
    the control cannot wedge on a full pipe; the bound is `TestDefaults.ProcessHang`, a named
    constant, so it is a hang detector rather than a promptness claim.

**House rules and hygiene**

28. **The new scan does not match itself.** `HouseRuleTests` composes its needles
    (`"IsUnder" + "("`, `"Fol" + "low"`), and neither `FollowUnder(` nor `Walk`'s own `foreach`
    contains `.Follow()`, so there is no false positive in `SessionIndex` itself.
29. **Scratch discipline.** Both commits keep every produced artifact in `.work/`; nothing new
    was written outside the repository.
30. **Cost claims are hedged rather than invented.** `ARCHITECTURE.md:544-559`,
    `SessionManager.cs:899-908` and `SessionLock.cs:1017-1023` all say the create/close pair is
    unmeasured and refuse to guess, and the 0.035/0.049 ms figures are labelled the file half
    only. That is the rule being followed, not broken.

---

## Documents sampled against the code

**Verified accurate:** `ARCHITECTURE.md:531-559` (the probe and its corrected cost);
`SessionManager.cs:859-875` and `:899-935` (the `InUse` corrections); `SessionLock.cs:407-416`
(the `Rewrite` "unobservable" correction); `SessionToolSurface.cs:78-102` (the
gate-does-not-refuse correction, in the right direction); `ProvisioningRemediation.cs:41-70`
(the `isError` gate, and the note that the gate lives in the proxy); `SessionErrors.cs:510-517`
and `:553-582` (the split row and its opposite recovery); `ArtifactRouter.cs:200-210` and
`:775-790` (the monotone and substring corrections); `MaintenanceLock.cs:193-207` (the
`Describe` correction — `Describe` does return the last writer's line, so it genuinely cannot
say which).

**Found inaccurate:** `SessionIndex.cs:203-208` (W6), `:229-231` (W7), `:245-248` (W8);
`ArtifactRouter.cs:519-523` (W3); `BrowserProxy.cs:1227` (W9); `MaintenanceLock.cs:489-494`
(W5); `ARCHITECTURE.md:561-568` and `SessionIndex.cs:225-236` (W4); `TESTING.md:394` (W10);
`SessionRun.cs:173-175` (W13).

---

## What this pass did not check

No build, no test run, no measurement. The HAZARDS tallies and the 811 fragment count were not
re-counted by hand — both are mechanised by `RecordedCountTests` against the same implementation
that produces them, and the recorded gate is green. The MCP SDK's request-dispatch concurrency
was not read, which is the open link in W2. F3, F4 and F6 are out of scope and were not looked
at.
