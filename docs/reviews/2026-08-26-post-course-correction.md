<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# P7 — narrow adversarial re-review, post course-correction

**Read-only review of `118776e`..`4d4b34e` (P0–P6) and every seam where those
changes meet the code that predates them.** Tree at `4d4b34e`, clean, 626/0/0
both shells at hand-off. Nothing in this review wrote to the repository outside
`.work/`; every session it opened was destroyed and `browserai_list` on the
scratch root reports none left (`.work/p7/drive7-out.txt`).

**17 findings**, most severe first. Then the eight queued leads with verdicts.
Then what was checked and found sound.

Evidence lives in `.work/p7/`: `drive*-out.txt` are transcripts of the published
binary driven over stdio, `lead7/` is the release-run experiment, `dupkey.cs` is
a standalone .NET probe, `deadshare.txt` is one timing.

---

## 1. Every BrowserAI startup opens the SQLite store of EVERY session on the machine, and leaves a `-wal` and a `-shm` beside each one — unasked, and contradicting the sweep's own doc comment

**Mechanism.** `Program.Main` starts the stray sweep in the background
(`src/BrowserAI/Program.cs:226`). One pass calls
`_index?.Sweep()` (`src/BrowserAI/Sessions/StraySweep.cs:438`),
which is `SessionIndex.Sweep()` → `Follow()` → per entry
`SessionLock.ReadRecord(session)` (`src/BrowserAI/Sessions/SessionIndex.cs:625`)
→ `SessionStore.OpenForReading` (`src/BrowserAI/Sessions/SessionLock.cs:724`).
The index is machine-wide, so **one process start = one SQLite open per
registered session on the host**, and each open puts `browserai.data-shm` and
`browserai.data-wal` into a directory nobody named.

**Evidence — measured 2026-08-26 through the published binary** (`.work/p7/drive9-out.txt`):

```
while held             : [browserai.data, browserai.data-shm, browserai.data-wal, browserai.lock]
after clean close      : [browserai.data, browserai.lock]
reader started, BEFORE ANY READ:
                         [browserai.data, browserai.data-shm, browserai.data-wal, browserai.lock]
```

The third line is a second BrowserAI process that had done nothing but
`initialize` — no `tools/call` at all. The `-wal` and `-shm` are the sweep's.

**Why this is a finding rather than a note.** The tree documents this side
effect three times and every one of them attributes it to a *caller-initiated
read* of the directory the caller named:
`src/BrowserAI/Storage/CLAUDE.md` ("Reading a session
directory is not side-effect-free … It is now said model-facing too, in
`browserai_catch_up`'s own description"), `SessionStore`'s remarks, and
`browserai_catch_up`'s own description ("reading a session whose holder died
recovers its write-ahead log, which can leave a small `-shm` file beside the
record"). None of them says that **starting the server does it to every session
on the machine**. And `SessionLock.TryHoldUnowned` states the opposite property
for the sweep in as many words
(`src/BrowserAI/Sessions/SessionLock.cs:574-579`):

> **It opens the guard and never the store.** … opening the record would tell
> the sweep nothing it acts on and would create a `-shm` in a directory it is
> only visiting.

That sentence is true of `TryHoldUnowned` and false of the pass that calls it,
two hundred lines up in the same class.

**What fixing it takes.** Two halves, and they are separable.
(a) *The record*: `Storage/CLAUDE.md`, `SessionStore`'s remarks and
`ARCHITECTURE.md` gain the startup path — it is a fact about **every** process
start, not about a reader. `SessionLock.TryHoldUnowned`'s paragraph gets a
`Corrected` clause naming `Pass`'s `_index?.Sweep()`, or the sentence is
narrowed to *this method*.
(b) *The mechanism, if it is wanted*: the index sweep needs the record only to
decide `IsRemovable`, and the removable states it acts on
(`DirectoryMissing`, `VolumeMissing`, `NotASession`) are decided **before**
`ReadRecord` is reached — the record is used for the *inventory*, which a sweep
does not print. A `Follow()` overload that stops at the guard would remove the
whole side effect from the sweep and leave `browserai_list`'s (which a caller
did ask for) intact. That is a behaviour change and belongs to the maintainer.

---

## 2. `RecordText.Sanitise` keeps every supplementary-plane `Cf` character, including the whole TAG block — the invisible-text class its own doc says it removes

**Mechanism.** `src/BrowserAI/Sessions/SessionRecord.cs:417`
iterates `char`, and line 433 asks `char.GetUnicodeCategory(character)`. For a
surrogate that answers `UnicodeCategory.Surrogate`, never `Format`, so **no
supplementary-plane character is ever tested**. The `Cf` drop therefore covers
the BMP only.

**Evidence — measured 2026-08-26 through the published binary**
(`.work/p7/drive8-out.txt`), one `browserai_init` purpose read back through
`browserai_catch_up`:

```
SENT   … U+200B … U+202E … U+FEFF … U+E0001 … U+E0048 U+E0049 … U+1D173 … U+0007 … U+000D … U+2028 …
STORED … (gone) … (gone) … (gone) … U+E0001 … U+E0048 U+E0049 … U+1D173 … U+0020 … (gone) … U+0020 …
Survivors that are Cf: U+E0001 U+E0048 U+E0049 U+1D173
```

`U+E0048 U+E0049` is the invisible text "HI" in the TAG block
(U+E0020–U+E007F) — the canonical smuggling range. `U+E0001` is LANGUAGE TAG.
`U+1D173` is MUSICAL SYMBOL BEGIN BEAM, also `Cf`. All four survive into the
record and out again into another agent's context.

**Why it matters even under the charter's non-goal.** The type's own remarks say
what it is for: *"a `purpose` and a `why` are written by one model and replayed
into another's context, so they are a channel between agents … what keeps them
data rather than instructions is … that they cannot carry the characters a
terminal, a renderer or a prompt assembler acts on"*, and *"Every `Cf` is dropped
outright: U+200B, U+202E and U+FEFF are invisible by construction"*. This is the
stated predicate not being implemented, not a hostile-caller defence. It applies
to `tool` as well, which is recorded verbatim from the caller for refusals.

**What fixing it takes.** Enumerate runes rather than chars —
`text.EnumerateRunes()` with `Rune.GetUnicodeCategory(rune)` — and append the
rune. A lone unpaired surrogate then needs its own decision (drop it: it is not
text). Plant red with a supplementary-plane `Cf` first; `SessionLogTests` /
`SessionRecordTests` already own the sanitiser's arms.

---

## 3. `browserai_catch_up`'s `page` is truncated to `int` unchecked, so an out-of-range page is silently served as a *different* page

**Mechanism.** `Number()` returns a `long`
(`src/BrowserAI/Sessions/SessionManager.cs:2646`)
and `src/BrowserAI/Sessions/SessionManager.cs:752`
does `var page = (int)(asked ?? 1);` in an unchecked context (nothing in the
build sets `CheckForOverflowUnderflow`). `4294967297` truncates to `1`; the
range check on line 754 then passes, and the answer says *"page 1 of 1"*.

**Evidence — measured through the published binary** (`.work/p7/drive-out.txt`):

```
## [PAGING] page=2          isError=true  :: 'page' = 2 is outside this session's log …
## [PAGING] page=4294967297 isError=false :: … page 1 of 1, entries 1–6 of 6
```

A caller that asks for page 4,294,967,297 is told it is reading page 1 and is
not told anything is wrong. The mirror case is as bad in the other direction:
`2147483648` wraps to `-2147483648`, so the refusal quotes a number the caller
never sent.

**What fixing it takes.** Keep `asked` as a `long` through the comparison
(`pages` is derived from a `long` already), quote `asked` in the refusal rather
than the narrowed value, and only narrow after the bound holds. A planted-red
arm at `int.MaxValue + 1` and at `2^32 + 1` is a two-line test; `CatchUpTests`
already owns the paging boundaries (0, 1, last, beyond-end are all correct
today — only the wrap is not).

---

## 4. A duplicate tool name inside one half of `tool-verdicts.json` escapes the loader's named-refusal contract

**Mechanism.** `src/BrowserAI/Sessions/ToolVerdicts.cs:225-235`
checks for a name appearing in **both** `upstream` and `authored`, and there is
no check for a name appearing twice **within** one of them. `JsonDocument` keeps
both properties, `Rows()` returns two `ToolVerdict`s, and the constructor's
`ToFrozenDictionary` (`src/BrowserAI/Sessions/ToolVerdicts.cs:126`)
throws a bare `ArgumentException`.

**Evidence — measured 2026-08-26 on .NET 10** (`.work/p7/dupkey.cs`, run outside
the repo):

```
JsonDocument kept 2 properties: browser_click, browser_click
GetProperty answers: deny            (the second, not the first)
ToFrozenDictionary threw ArgumentException: An item with the same key has already been added. Key: browser_click
```

That exception reaches `Program`'s process boundary
(`src/BrowserAI/Program.cs:396`) and exits 1 with
`StartupLog.Failed` — with a message that **names neither the file nor the
row**. `ToolVerdicts`' own remarks promise the opposite: *"A missing or malformed
file is a loud failure … Every refusal below names the file and what was wrong
with it."* The second half of the measurement is worse than the crash: for any
duplicated row that does *not* reach the frozen dictionary first,
`TryGetProperty` answers the **last** one, so a doctored file could carry
`allow` and `deny` for one tool and the reader would pick one silently.

**What fixing it takes.** In `Rows()`, collect names as you go and raise
`Unreadable(origin, "'…' appears twice in '…'")` on the second. One malformed-file
arm in `ToolVerdictTests` (there are fifteen already; this is the sixteenth).

---

## 5. `Remediate`'s remaining justification names a mechanism P3 deleted

**Mechanism.** `src/BrowserAI/Proxy/BrowserProxy.cs:844-852`
carries the paragraph that decides the `install-browser` anchor is acceptable:

> **It does not close the bypass on its own, and the caller no longer relies on
> it to.** An `isError` answer against a live tab carries the page's own title
> and the console and snapshot pointers in the same result, so page content can
> still trip this. What makes that harmless is that the rewrite branch now runs
> `Complete` like every other answered call …

`Complete` does not exist — it went with the artifact machinery in `feec42b`
(the only surviving occurrence of the word in `src/BrowserAI/Proxy/` is this
comment). The rewrite branch now `return`s immediately after sending
(`src/BrowserAI/Proxy/BrowserProxy.cs:657-666`).
So the acknowledged residual risk is defended by a mechanism that is gone.

**What the real bound is, and it is nowhere written.** The scan is narrow:
`ProvisioningRemediation.Rewrite` only fires when the block contains
`install-browser`, replaces only the regex
`Run \`[^\`]*install-browser[^\`]*\` to install\.?`, and returns `null` when the
replacement changed nothing — so a page merely *mentioning* the marker is
forwarded untouched. That is the sentence that belongs there.

**What fixing it takes.** Replace the paragraph with the real bound (the
anchored regex plus the no-op return), keep the `isError` gate's own reasoning,
and re-check the hazard row for *page content that switched a protection off*
against the new text. No code change.

---

## 6. `VolumeIdentity`'s "bounded call" guarantee is not what the code keeps: only the drive-letter root is judged, never a reparse point deeper in the path

**Mechanism.** `VolumeIdentity`'s remarks say `FinalNameOf` is *"only ever called
once `Of` has said the volume is local, which is what keeps a bounded call
bounded"* (`src/BrowserAI/Interop/VolumeIdentity.cs:36-40`,
repeated at 261-264 and 341-344). `Of` reads the **drive letter's** DOS device
link and nothing else. `DeepestExistingFinalName`
(`src/BrowserAI/Interop/VolumeIdentity.cs:357-375`)
then issues up to `AncestorWalkLimit = 64` `CreateFileW`s along a path whose
middle components it has judged nothing about. A directory symbolic link or a
volume mount point anywhere in that chain, pointing at a share that has stopped
answering, is traversed by the open — and the cost is the redirector's, not the
object manager's. This runs inside `CanonicalPath.FinalName`
(`src/BrowserAI/Sessions/CanonicalPath.cs:285-291`),
which `SessionLock.TryAcquire` reaches under `LockScopes.PerDirectoryGate` —
where the caller who named it is not the one who waits.

**Evidence.** The cost class was re-measured today on this machine:
`Directory.Exists(\\p7-no-such-host-xyz\share)` = **22,157 ms**
(`.work/p7/deadshare.txt`). **The reparse point itself could not be planted**:
this account has neither `SeCreateSymbolicLinkPrivilege` nor Developer Mode, so
both `New-Item -ItemType SymbolicLink` and `cmd /c mklink /D` refused. The
mechanism is read from the code and the cost from the measurement; the
composition is not measured, and I am saying so rather than implying otherwise.

**What fixing it takes.** Three options, and only the first is cheap:
(a) **Record it.** A hazard row and a corrected clause on
`VolumeIdentity`'s "bounded" sentence — the answer is right, the cost is not
bounded, and today the code claims it is. P5 left this to P7 explicitly.
(b) Open each ancestor with `FILE_FLAG_OPEN_REPARSE_POINT` first and refuse a
reparse point whose target is a UNC path — one extra open per level.
(c) Bound the walk with a watchdog and answer `Unestablished` on timeout, which
is a clock in a place that forbids one — `TESTING.md`, *Every duration is a hang
detector or it is a defect*.
My recommendation is (a) now and (b) only if somebody meets it.

---

## 7. `PathVerdict.Unestablished` is reachable **with a successful create**, on an ordinary machine, and nothing asserts it

**Mechanism.** `AncestorWalkLimit` is 64
(`src/BrowserAI/Sessions/CanonicalPath.cs:99`). A
path with more than 64 non-existent levels exhausts the walk, `final` comes back
`null`, and `FinalName` serves the caller's own spelling with `Unestablished`
set (`src/BrowserAI/Sessions/CanonicalPath.cs:299-303`).
`SessionLayout.Create` then succeeds, because .NET creates the tree happily.

**Evidence — measured through the published binary** (`.work/p7/drive7-out.txt`),
a clean bisect at the limit:

```
=== 60 levels (path length 162) ===   isError=false   (no note)
=== 66 levels (path length 174) ===   isError=false
NOTE: BrowserAI could not confirm this directory's spelling: the filesystem would not say
what it calls 'C:\…\dw66\A\A', so 'C:\…\dw66\A\A\…\A' is being taken as spelled.
```

The session opened, the note reached the caller, and `browserai_destroy` removed
it. `.work/STATE.md:1868` records the P5 could-not-check as *"Unestablished
unexercised (honest — unreachable without Create failing too)"*. That is false:
depth alone reaches it, and no ACL, no denied ancestor and no exotic machine is
needed.

**Two smaller things in the same place.** The note quotes the ancestor the walk
gave up on — `…\dw66\A\A`, an intermediate path that means nothing to a caller.
And the walk climbs 64 levels of failed `CreateFileW` before answering, which is
64 syscalls a caller pays for a path it will then be told nothing about.

**What fixing it takes.** Correct the STATE/P5 record, and plant a test at the
boundary — `AncestorWalkLimit` levels versus `AncestorWalkLimit + 2`, which is
the control this file has never had. `CanonicalPathTests` owns it. Consider
quoting the caller's own path rather than the ancestor in the note.

---

## 8. A caller-supplied control character is echoed raw into a model-facing refusal

**Mechanism.** `SessionErrors.DirectoryUnusable`
(`src/BrowserAI/Sessions/SessionErrors.cs:172`)
interpolates the caller's `value` and `CanonicalPath.UnkeepableName`'s clause
interpolates the offending `segment`
(`src/BrowserAI/Sessions/CanonicalPath.cs:436-446`),
both unescaped.

**Evidence** (`.work/p7/drive5-out.txt`, byte-checked): an `init` with
`C:\…\se\u0007ss` answers with a message carrying **two literal U+0007 bytes**.
The message correctly names `U+0007` in words — and then embeds it twice.

This is the same channel `RecordText.Sanitise` exists to keep clean, on the
half of it that nothing sanitises: a refusal goes straight into the calling
model's context, and (for refusals at the verdict door) into
`browserai.data`'s failure payload.

**What fixing it takes.** Render the quoted path and segment through an escaper
in `SessionErrors` — the message already names the code point, so escaping the
literal costs nothing a reader needs. `ErrorCatalogueTests` is where the arm goes.

---

## 9. The idle timer closes the browser and writes no row

**Mechanism.** `LiveSession.Idle` is constructed with
`token => CloseBrowserAsync(child, token)`
(`src/BrowserAI/Sessions/LiveSession.cs:140-145`),
which talks to the child directly and never touches `Lock`. Nothing in
`CloseBrowserAsync` (`src/BrowserAI/Sessions/LiveSession.cs:204`)
appends or settles.

**Why it is a finding now and was not before.** Until P2 the session log file
carried it. `browserai.log` is gone, and
`src/BrowserAI/Sessions/CLAUDE.md` states the
consequence itself — *"That file is gone, so the record is the only place …
survives at all"*. `browserai_catch_up` tells the reader the log is
*"WHAT WAS DONE HERE — the session's own log … This is what BrowserAI did"*. An
autonomous browser close is something BrowserAI did, it is invisible in the
record, and the next call silently relaunches a browser — so a reader sees an
unexplained gap in wall-clock time and no reason for it.

**What fixing it takes.** Either a row (`tool` = `browser_close`, `why` = the
timer's own sentence, settled from the close's outcome — the vocabulary already
exists and `browserai_resume` already writes a row this way for a session it
finds open), or a sentence in `catch_up`'s own description saying the log holds
*forwarded and refused calls* and not BrowserAI's own maintenance. The first is
the honest one; it is a behaviour change and belongs to the maintainer.

---

## 10. A C# parameter name leaks into a model-facing refusal

**Evidence** (`.work/p7/drive5-out.txt`), `browserai_init` on `C:\`:

> `'directory' = 'C:\' is not a usable directory path: 'C:\' is a volume root
> rather than a session directory. A session directory must be a real directory
> on the volume. **(Parameter 'canonical')** Nothing was changed. …`

**Mechanism.** `SessionPath.For` throws
`new ArgumentException(message, nameof(canonical))`
(`src/BrowserAI/Sessions/SessionPath.cs:158-161`),
whose `.Message` appends `(Parameter 'canonical')`, and `Resolve` interpolates
`failure.Message` into the catalogue sentence
(`src/BrowserAI/Sessions/SessionManager.cs:2548-2551`).
`canonical` is an internal identifier that means nothing to a model, in the one
sentence the model is supposed to act on.

**What fixing it takes.** Either drop the `paramName` argument at that throw
site, or have `Resolve` catch the specific case and compose the sentence itself.
`ErrorCatalogueTests` gets an arm that no catalogue sentence contains
`(Parameter '`.

---

## 11. The one environment variable that turns off the only containment there is is not in `ChildEnvironment.Refused`

**Mechanism.** P3 made `allowUnrestrictedFileAccess: false` *"the only
containment this product has left"*
(`src/BrowserAI/Runtime/BrowserConfiguration.cs:48-59`).
Upstream reads `PLAYWRIGHT_MCP_ALLOW_UNRESTRICTED_FILE_ACCESS` in
`configFromEnv`, and the merge order is config file → environment → CLI, so that
variable overrides the generated key. `ChildEnvironment.Refused`
(`src/BrowserAI/Protocol/ChildEnvironment.cs:130-150`)
does not name it. Nor does it name `PLAYWRIGHT_MCP_CONFIG` (a whole different
config file), `PLAYWRIGHT_MCP_OUTPUT_DIR` (relocates the allowed root), or
`PLAYWRIGHT_MCP_INIT_SCRIPT` / `PLAYWRIGHT_MCP_INIT_PAGE` — the last of which
upstream `require()`s, which is arbitrary code in the child.

**This is not a hole today.** The allowlist is the child's entire block by
construction, through `CreateProcessW` under `CREATE_UNICODE_ENVIRONMENT`, so
all of them are already absent. The finding is against the `Refused` list's own
stated purpose, quoted verbatim from its remarks: *"Naming them is what turns
'absent because nobody added it' into 'absent because it is refused' — the
difference between a property and an accident"*. The variable that switches off
the product's only containment is exactly the one that should be named, and P3
did not add it when it made the key load-bearing.

**What fixing it takes.** Five names in `Refused`, and a sentence in its remarks
naming why the file-access one is there. `ChildEnvironmentTests` already asserts
`Refused` never reaches a child.

---

## 12. `ReVerificationIndexTests.Exists` is narrower than its sibling on three axes, and nothing says so

**Mechanism.**

| | `HazardIndexTests.Missing` (`tests/BrowserAI.Tests/HazardIndexTests.cs:187-203`) | `ReVerificationIndexTests.Exists` (`tests/BrowserAI.Tests/ReVerificationIndexTests.cs:272-286`) |
|---|---|---|
| Assemblies | both (`OurAssemblies`) | the test assembly only |
| Members | `GetMember` with `Public\|NonPublic\|Instance\|Static\|FlattenHierarchy` | `GetMethod(name)` — public, no inherited members |
| Kind | any member | methods only |
| Ambiguity | safe | `GetMethod` throws `AmbiguousMatchException` on an overload set |

P6's rider L harmonised the *`previously "…"` clause* between these two gates
(they now share `Harness/CorrectionClause.cs`) and left the resolution axis
untouched with no note saying why.

**No live row depends on it** — every row resolves today, which is why the
suite is green. It fails in the safe direction (a row naming a private method
would go red rather than pass silently), so this is a false-red risk and a
narrower claim than the sibling's, not a hole. **It should be harmonised**: the
two gates are presented in `CLAUDE.md` as one class of mechanism, and the next
person who writes a re-verification row against a product type will get a red
build for a row that is correct.

**What fixing it takes.** Give `Exists` the same binding flags and the same
two-assembly search, wrap `GetMethod` in a `GetMember` call, and keep the
existing synthetic-row controls. If it is deliberately narrow, that reason goes
in the method's own remarks — which is what the rider asked for.

---

## 13. "An override leaves a trace in the release artifact" is a claim the manifest cannot express

**Mechanism.** `DECISIONS.md:157-163` and
`RELEASING.md:164-168` both say a crunch override is stated
in the manifest, and DECISIONS puts it in bold: *"A release whose manifest does
not say it was overridden is a release claiming it was not."*
`build/Write-ReleaseManifest.ps1` emits `version`, `tag`, `package`, `sha256`
and the resolved versions read out of seven copied files. **There is no override
field and no place for one.** No manifest can ever say it was overridden, so by
that sentence's own logic every release claims it was not — including one that
was.

DECISIONS' closing paragraph does hedge correctly (*"What the build does hold is
the trace: the manifest … is copied rather than transcribed, so the version in
it is evidence"*), which is true and much weaker. The bolded sentence above it
is the one a reader will act on.

**What fixing it takes.** Either weaken the two sentences to what the script
produces — *the manifest states the version that shipped; whether that was an
override is stated in the item-8 evidence and the changelog* — or add an
optional `override` block to the manifest that the script writes when a
parameter is passed, so the claim becomes true. `ReleaseScriptTests` already
reads the wanted set and would carry the arm.

---

## 14. `CatchUp`'s remarks still describe the truncation P2 deleted

**Evidence.** `src/BrowserAI/Sessions/SessionManager.cs:731-736`,
three lines above the paging code:

> **The log is printed newest-last and truncated from the FRONT.** A caller
> arriving at a session wants the recent story; an elision is stated rather than
> presented as continuity, and the record's own cap says *may* because it cannot
> tell whether a trim has happened.

Every clause is false of the tree at `4d4b34e`: the log is printed **oldest**
first, nothing is truncated, and there is no cap anywhere (`SessionStore`'s
"No caps, anywhere" and `SqliteStorageTests.NothingInTheStoreIsCappedByLengthOrByCount`).
P6's scope B was to rewrite falsified claims; this one is inside the method it
falsifies.

**What fixing it takes.** Replace the paragraph with a `Corrected 2026-08-26
(previously "…")` clause pointing at the paging design that replaced it. No code
change. Worth a sweep of the same shape: this one survived a phase whose whole
job was to find it.

---

## 15. `browserai_list` on a drive letter that does not exist answers "no sessions" rather than "no such drive"

**Evidence** (`.work/p7/drive5-out.txt`):

```
=== list [nonexistent drive] "Q:\" isError=false 1ms
   No BrowserAI sessions under 'Q:\'. That is an answer rather than an error: …
```

**Mechanism.** `Subtree` runs the full `CanonicalPath.Of(…, Named, …)`, which
asks `VolumeIdentity.Of` and gets `VolumeKind.NoSuchDrive` — and then discards
it, because only `Network` and `Substituted` are acted on
(`src/BrowserAI/Sessions/CanonicalPath.cs:251-278`).
`SessionManager.cs:1053` prints the empty answer.

The answer is *true* rather than wrong, which is why this is low and not high.
But `CanonicalPath`'s own remark justifies dropping `NoSuchDrive` on the ground
that it *"falls through to the ordinary creation failure — which already says
what to do"*, and `list` creates nothing, so for this door there is no such
sentence. A caller that typed the wrong letter is told, confidently, that there
is nothing there.

**What fixing it takes.** Either carry `NoSuchDrive` out of the verdict so
`list` can add a clause, or add the clause in `List` from a cheap
`Directory.Exists(root)` on a path already proven local. `SessionListTests`
owns the three-valued answers already.

---

## 16. A session directory deep enough to push `output\` past `MAX_PATH` is accepted, created and locked, and then fails at child launch with the cause unnamed

**Evidence** (`.work/p7/drive6-out.txt`), 70 non-existent levels:

> The browser runtime for '…' did not start: IOException: Could not start
> '…\node.exe' in '…\output'. The directory is left as it is, nothing is
> running, and the lock has been released. …

The recovery it offers ("delete that directory and call `browserai_init` again
to re-provision") is the wrong one: nothing is broken about the install. The
real cause is that `CreateProcessW`'s `lpCurrentDirectory` is `MAX_PATH`-bound
while .NET's directory creation is not, so the session directory is creatable
and its `output\` is not usable as a working directory.

The tree already refuses names Windows would not keep verbatim
(`UnkeepableName`) precisely so that this class fails at the door rather than
half-way in. Length is the one member of that class that is not checked.

**What fixing it takes.** A length predicate in `CanonicalPath.UnkeepableName`
(or beside it) against `MAX_PATH` minus the longest suffix BrowserAI appends —
`\output` today — with a refusal that names the budget. `SessionErrors` row 7's
sentence should stop suggesting a re-provision for a launch failure whose cause
is the path.

---

## 17. The `authored` half of `tool-verdicts.json` has no runtime effect, and an unjudged `browserai_*` name is refused without a row

**Mechanism.** `BrowserProxy.AnswerToolsCallAsync` short-circuits on
`SessionToolSurface.IsAuthored(name)`, which is a **prefix test**
(`src/BrowserAI/Sessions/SessionToolSurface.cs:301-302`),
before `_verdicts.Decide` is reached; and `Rewrite` advertises the seven
authored tools from a hard-coded list
(`src/BrowserAI/Sessions/SessionToolSurface.cs:296`),
never from the file. So removing an `authored` row from `tool-verdicts.json`
changes nothing a caller can observe — only `ToolVerdictTests` would notice.

Two consequences:

- `Sessions/CLAUDE.md`'s row — *"Which tools this build forwards is a FILE, not
  code"* — is true of the upstream half and not of the authored half. Worth one
  clause.
- A name like `browserai_zzz` never reaches the verdict door: it lands on
  `InvokeAsync`'s default arm
  (`src/BrowserAI/Sessions/SessionManager.cs:403`),
  is refused with a good sentence, and **writes no log row** — while the plan's
  §1f and the door's own doctrine say a refused call is still logged. Measured:
  an unjudged *upstream* name is logged (`.work/p7/drive-out.txt`, stderr shows
  `'browser_zzz_not_a_tool' was refused …`); an unjudged authored-prefix name is
  not, because no session has been resolved at that point.

**What fixing it takes.** For the doc half, one clause. For the row half, the
honest options are (a) accept it and say so where the "every refusal is logged"
claim is made, or (b) move the `IsAuthored` short-circuit to an **exact** match
against `SessionToolSurface.Names`, so `browserai_zzz` falls through to the
verdict door, is deny-by-defaulted, and is logged against the session it named
— which also makes the `authored` rows load-bearing. (b) is a behaviour change.

---

# The eight queued leads

**1. A REAL browser-initiated download under flat output — VERIFIED, it lands
and it stays.** Driven end to end through the published binary against a local
HTTP server serving `Content-Disposition: attachment`
(`.work/p7/drive3-out.txt`, `drive4-out.txt`). The child answers
`- Downloaded file report.txt to "./report.txt"` and `output\report.txt` (22 B)
is there, **flat at the output root**, beside `console-*.log` and `page-*.yml`.
Upstream's own path is `_downloadStarted` → `context.outputFile({suggestedFilename},
{origin: 'code'})` → `download.saveAs(…)`, and `origin: 'code'` bypasses
`checkFile`, so the containment key does not interfere. The raw copy appears in
`downloads\` under a GUID name while the browser lives and is **gone after
`browser_close`** (measured both sides). `browserai_catch_up`'s inventory counts
it correctly. Nothing to fix.

**2. `file:` navigation and `browser_file_upload` under
`allowUnrestrictedFileAccess: false` — VERIFIED, both refused, with a positive
control.** Provoked against a real Chromium (`.work/p7/drive3-out.txt`):
`browser_navigate` to `file:///C:/Windows/win.ini` → `isError`,
`Access to "file:" protocol is blocked`; `browser_file_upload` with
`C:\Windows\win.ini` after opening a real file chooser → `isError`,
`File access denied: C:\Windows\win.ini is outside allowed roots. Allowed roots:
<session>\output, <session>\output`. The same call with a file inside `output\`
succeeds. The doubled root in the message is BrowserAI's claim that *"the two
coincide rather than overlapping"*, confirmed from the wire. No longer
"stated from upstream source".

**3. The unjudged-call-would-have-launched-a-browser claim — VERIFIED, it is no
longer INFERRED.** A raw `@playwright/mcp` child (BrowserAI bypassed), fresh
`userDataDir`, `chrome.exe` counted before and after
(`.work/p7/drive4-out.txt`): `0` before, `0` after the handshake, **`8` after a
`tools/call` naming `browser_zzz_not_a_tool`** — and the userDataDir went from
missing to sixteen entries — *before* the child answered
`Tool "browser_zzz_not_a_tool" not found`. The reasoning in `ToolVerdicts`'
remarks (`coreBundle.js` `:73101` before `:65533`) is right, and the line-number
citation can be replaced with, or joined by, this measurement.

**4. The junction-to-share ancestor probe cost — REAL, unbounded, and it needs a
row *and* a corrected claim.** See finding 6. The cost class re-measured today at
**22,157 ms** for one filesystem call against a dead share. **Could not be
provoked end to end**: this account cannot create a directory symlink (no
privilege, no Developer Mode), so the composition is read from the code rather
than measured. My verdict against P5's "cost not answer; no row added": it does
warrant a row, because the code *states* a bound it does not keep, which is a
stronger defect than an unbounded cost nobody claimed was bounded.

**5. `ReVerificationIndexTests.Exists` binds only PUBLIC methods — CONFIRMED,
and it is narrower on two further axes.** See finding 12. No live row depends on
it; it fails in the safe direction; it should be harmonised, because the two
gates are sold as one mechanism and P6 harmonised the other axis and left this
one silent.

**6. `PathVerdict.Unestablished` — REACHABLE, and the "unreachable" record is
wrong.** See finding 7. Measured with a clean bisect at `AncestorWalkLimit`: 60
levels no note, 66 levels the note *and* a session that opened successfully. It
needs a test, and `.work/STATE.md`'s P5 could-not-check needs correcting.

**7. `BROWSERAI_RELEASE_RUN=1` — RAN, both halves, with controls. It works.**
These were filtered runs, and for this lead **a filtered run IS the experiment**
— stated here so nobody reads them as verification of anything else.

| arm | invocation | result |
|---|---|---|
| filtered + `BROWSERAI_RELEASE_RUN=1` | `--treenode-filter /*/*/SuiteCoverageTests/AReleaseRunFailsWhereAnOrdinaryRunSkips` | **exit 10**, `Test adapter test session failure`, refusal raised from `SuiteCoverage.ReportWhatThisRunExercised` → `RefuseARunThatMayNotBeARelease` |
| control: same filter, no variable | as above | **exit 0**, `Passed!` |
| capability absent, no variable | `ThirdPartyNoticeTests/EveryNoticeIsInsideThePackedRelease` + `BROWSERAI_RELEASE_PACKAGE` pointed at a missing file | **exit 0**, `skipped: 1` |
| capability absent, with variable | as above + `BROWSERAI_RELEASE_RUN=1` | `TestFailedException: packed release is not available … This is a release run …`, coverage block reads `1 test FAILED for want of it` |

The probe report shows `verdict=FILTERED`, `global` and `session` byte-identical
to the filter, `decision=Refuse`. Logs in `.work/p7/lead7/`. Nothing to fix.

**8. The `-shm`-on-read side effect and the settle transient, as documented
versus as coded — the settle transient matches; the `-shm` claim is
understated, badly.** The settle transient is coded as documented (settle in a
`finally` after the answer is sent, `BrowserProxy.cs:674-689`) and carries its
own `open` hazard row with the choice named inside it. The `-shm` half is
finding 1: the documented subject is a caller-initiated read, and the measured
subject is **every process start, against every session on the machine**. A
clean-close session has no `-wal` and no `-shm`; starting a second BrowserAI and
sending nothing but `initialize` puts both back.

---

# Checked and found sound — do not touch

- **The lock's six properties as coded.** Write-once temp+`WriteThrough`+flush+
  rename, `Hold` at `ReadWrite`/`Share.Read`, four probe states with
  `Undetermined` never collapsing into free, `TakeAndWrite` split so *the guard
  WAS written* is a different sentence from *nothing was changed*,
  `RenameWindow.WaitOutWhereNoOwnerIsPossible` licensed only after this
  process's own write and asserted as a **pairing** over `src\` as text. The
  `HouseRuleTests` arm over the two literals resolves the argument list after
  each declaration and has a both-directions control including the
  `FileShare.ReadWrite`-contains-`FileShare.Read` trap. No interleaving I could
  construct produces two holders.
- **In-flight-before-forward under a kill.** Measured: `SIGKILL` mid-navigation
  leaves the row unsettled and a second process reads it back as
  *"— no answer was recorded: the row was written before the call was forwarded
  and nothing settled it, so the call hung, the child died, or the process ended
  first"* (`.work/p7/drive6-out.txt`). The crashed holder's hot WAL was recovered
  by the read-only open, exactly as P1 measured.
- **Byte-identity coverage.** `LosslessPassthroughTests` covers errors,
  cancellation, oversized payloads, an `isError` body, an unknown content type,
  an unknown property, images, a child that dies mid-call, a frame that fails to
  parse, and both injected parameters being stripped. The remediation branch
  keeps the child's **original** error bytes in the record even when it rewrites
  the answer — a good property that is nowhere written down.
- **Deny-by-default at the door.** Measured with a real unjudged upstream name
  through the published binary: refused, nothing reached the browser, and the
  refusal *is* recorded on the session.
- **`ChildEnvironment` as an allowlist by construction** — the block goes whole
  to `CreateProcessW`, so there is no `Clear()` to forget. (Finding 11 is about
  the `Refused` list's completeness, not about the mechanism.)
- **Flat output and the roll-up.** `output\` is flat; upstream's own
  subdirectories are the only structure; the HAR path is at the output root; the
  roll-up beside the sessions is rewritten on destroy and read `sessions: 0`
  afterwards (measured).
- **The path doors.** Every one of `init`, `resume`, `catch_up`, `destroy`,
  `set_purpose` and `list` refuses a UNC path in **≤ 1 ms** (measured), and the
  refusals name the accepted form. `\\?\C:\…`, `/`-spelling and `..` segments are
  normalised silently with one note; `\\.\`, trailing dot, trailing space,
  reserved device names with and without an extension, ADS, wildcards and control
  characters are refused before `GetFullPath`. `list` accepts a volume root and
  the identity chain refuses one.
- **`SessionIndex`'s canonical seam.** `PathOrigin.Read` per entry with no
  syscall, plus the name-is-the-hash-of-the-content check, which is what catches
  an aliased pointer written by something else.
- **`SessionLock`'s `_inProcess` discipline** — `Append`, `Settle`,
  `SettleOpening`, `AppendPurpose`, `ReleaseAndDelete` and `Dispose` all hold it
  for their whole body, and it is what makes `INSERT` + `last_insert_rowid`
  atomic on one connection.
- **The idle timer touches only the child**, so it adds no B4 exposure. (Finding
  9 is about the record, not about the locking.)
- **`Judge`'s reading of `isError` as a failed call**, and `Settle`'s swallow of
  a `SqliteException` after the answer has gone out.

## One note, below the bar for a finding

`SessionLock.Settle` catches `SqliteException` and not `ObjectDisposedException`,
while `Append` documents both and `BrowserProxy.Refused` catches both. It is not
reachable — `Settle` holds `_inProcess` and returns early on `_disposed`, and
both disposal paths take the same lock — so this is an asymmetry in the
`catch` filters rather than a defect. Worth one word if the file is being
touched anyway.
