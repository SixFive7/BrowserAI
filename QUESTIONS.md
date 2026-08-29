<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Questions, and decisions taken without you

Written 2026-08-18 while the maintainer slept, under a standing instruction to
continue autonomously and record anything that needed him.

**Two kinds of entry, and the second is the important one.** *Open questions* are
things nobody here can answer. *Judgement calls* are decisions taken alone that a
reasonable person could have taken differently — each says what was chosen, why,
and **how to reverse it**, because a decision you cannot cheaply undo is one that
should have waited.

⚠️ **Swept entry by entry on 2026-08-19, and this is the document where staleness
costs most** — it is what the maintainer reviews from, so an entry that has quietly
stopped describing the tree sends a decision the wrong way. **Sixteen entries were
checked against the code: nine numbered, five lettered, and the block of settled
bullets. Six were wrong.** Two of those (6 and 7) were found by accident, which is
the whole reason the other fourteen were read at all. Every correction is **in place
with a `previously` clause** rather than a rewrite: a reader who learned the old
value needs to see that it was reviewed and replaced, not silently swapped. Nothing
is deleted for being merely settled — settled entries are marked and kept, because
*we already decided that* is only useful if the decision is still findable.

---

## Open questions

### 1. Are parameter descriptions truncated? — **ANSWERED 2026-08-18: no**

Claude Code's [MCP documentation](https://code.claude.com/docs/en/mcp) states, verbatim:

> Claude Code truncates tool descriptions and server instructions at 2KB each.
> Keep them concise to avoid truncation, and put critical details near the start.

It says **nothing** about `inputSchema.properties[*].description`. So the tool and
instructions surfaces are cited; the parameter surface is a genuine unknown.

**Taken:** gate all three at 2048 bytes, and say in the constant that two surfaces
are documented and the third is a conservative assumption. ⚠️ **The unit in that
sentence is stale, and is left standing as the record of what was taken at the time:
the gate counts UTF-16 characters, not bytes, corrected 2026-08-18 by question 10
below.** Bytes was strictly stronger and so capable only of false failures, but it
was wrong about the world. Over-gating a surface
that turns out to be uncapped costs a slightly terser description; under-gating
one that is capped loses text silently, which is the failure this project exists
to eliminate.

**Answered 2026-08-18 @ Claude Code 2.1.234. They are not truncated at all** —
a parameter description of **20,000 characters** reached the model whole. The
probe published exactly what the old paragraph below asked for and more, and the
answer was read off the client's own outbound Messages API request rather than
off a model's recollection. *Previously: "To settle it: publish a parameter
description just over 2048 bytes and read what reaches the model. Nobody has done
that."*

**What changed, and what deliberately did not.** The cap stays enforced, and it
is now labelled for what it is: a **house limit**, not a client limit, on
`ClientTruncationBudget.ParameterDescriptionCharacters`. Two reasons, neither of
them the original one. It floats with a client version this project does not
control, and it is the surface BrowserAI is most exposed on — one injected
`session` description lands on fifty-nine upstream tools at once, so the day a
release does start cutting schemas, one edit becomes fifty-nine silent
truncations. **What must not survive is citing it as documented**, and the
constant now says so.

Full measurement:
[kb](kb/mcp/protocol.md#what-2kb-each-means--measured-2026-08-18--claude-code-21234).

### 2. What should CI actually run? — **BUILT 2026-08-18, REMOVED 2026-08-20**

`SaturationTests` is `[NotInParallel]` and takes **80–96 seconds alone** — it is
most of the suite's wall clock.

⚠️ **Corrected 2026-08-20 (previously "There has been since 2026-08-18:
`.github/workflows/build.yml`, `runs-on: windows-latest`, on push and on pull
request, running the **whole** suite with `SaturationTests` in it", which had
itself corrected "There is no CI today; adding one is on the queue" on 2026-08-19).
The original text is true again.** **There is no CI today.** Both `previously`
clauses are kept deliberately: this entry has been wrong in both directions inside
three days, and a reader who learned either state needs to see that it was
reviewed and replaced rather than lost. The workflow was removed at the
maintainer's decision, verbatim: *"Remove CI completely. Let all the tests run on
my machine only. I want no CI and no github runner. Add to the todo that we will
add CI back in later. But that requires me adding infrastructure for self-hosted
runners and I am considering leaving github before we do so."*

**What was taken, and it is the record of a thing that was built and then
removed:** CI ran the full suite including saturation, on Windows, on push and
pull request. Rationale: 54% of this project's enforcement is TEST or RELEASE
phase, so a CI that skips the expensive half re-creates the gap it exists to
close. Cost was roughly two minutes per run. **The reasoning is unchanged by the
removal** — it is why the question is *when* CI comes back rather than *whether*.

**One thing the entry did not price, and the workflow carried it.**
`BROWSERAI_RELEASE_RUN` was deliberately **not** set in CI — it is named below as
the precedent for a stricter tier, and the reason it was off is that the stricter
tier wants things a hosted runner does not have.

**Where the question stands now.** It is not open: it is answered, built, and
reversed by decision. What it becomes is [the TODO
item](TODO.md#continuous-integration), which carries the audit of what is
unverified anywhere while CI is gone — the one row that matters being *a different
machine*, which found four defects a developer machine structurally could not.
When CI returns it must not assume GitHub Actions.

---

## Judgement calls taken without you

### A. The README split

You chose direction 2 — a short README plus the charter reasoning elsewhere —
without specifying the cut.

**Taken:** `README.md` keeps what it is, what it does, install, use, the scope
boundary, and the licence. Everything that is a *settled decision with an
argument attached* moves to `DECISIONS.md`. The test: a first-time visitor
should reach "how do I run this" without scrolling past a decision table.

**To reverse:** it is one file split along heading boundaries; recombining is a
concatenation.

⚠️ **Corrected 2026-08-18 (previously "No links break either way, because
`DocumentationLinkTests` will not let them").** That was false, and the split
depended on it. **The link test checks the path half and deliberately not the
`#anchor` half** — it says so in its own remarks. Retitling the four
`Settled <date>` headings moved **53 anchored links across 20 files**, four of
them from `src/`, and not one would have gone red. They were resolved by hand
against GitHub's slug rule instead, and all resolve.

The lesson is the one this repository keeps relearning: **a test that reads as
covering something, and says honestly that it does not, is still read as
covering it.** The stated gap was doing no work, because the person relying on
it did not read that far.

✅ **Closed 2026-08-18. Corrected 2026-08-19 (previously "Closing it is on the
queue").** `DocumentationLinkTests.EveryLinkFragmentResolvesToAHeadingThatExists`
is that half: every `#anchor` in the corpus is resolved against the headings of the
document it points into, GitHub's slug rule included, and a same-file fragment in a
file with no headings is an offence of its own. The retitling that moved 53 anchored
links across 20 files would go red today. The gap this entry is about was the *only*
reason the split was risky, and it no longer exists.

### B. Where the three directory-scoped `CLAUDE.md` files go

**Taken:** `src/BrowserAI/Interop/`, `src/BrowserAI/Sessions/`, and
`src/BrowserAI/Runtime/` — measured as carrying 59% of all prohibition language
in the tree (110, 63 and 32 instances). Each is capped at 20 lines and contains
only rules true of *every* file in that directory, each naming its mechanism.

**To reverse:** delete the files. Nothing depends on them.

✅ **Verified 2026-08-19: still true, and still within the cap.** All three files
exist at those paths, at 15, 19 and 13 lines — the 20-line cap holds, with the
`Sessions/` one closest to it. Nothing in the build reads them, so *delete the
files* remains the whole of the reversal.

### C. Pushing before the engineering queue is finished

You chose: restructure → push → engineering. That means the pushed repo will
carry a **known-intermittent suite** at `Limit => Unbounded`, deliberately, and
an open engineering queue.

**Taken:** push anyway, and make the state legible — `TODO.md` names what is
open, and `SuiteParallelism` says in as many words that unbounded is a race
detector and that a future reader must not "fix" red runs by capping.

**To reverse:** nothing published is irreversible except the history itself,
which you have already accepted.

✅ **Settled 2026-08-19, and the cost this entry accepted was never actually
incurred.** *Previously the standing state was "the pushed repo will carry a
known-intermittent suite at `Limit => Unbounded`, deliberately, and an open
engineering queue".* The suite stopped being intermittent before anyone had to live
with it: `SuiteParallelism` records **20 consecutive green runs at Unbounded as of
2026-08-18**, against 11 red in 20 before the timing work. Unbounded stays, and the
remark still says in as many words that it is a race detector and that a future
reader must not "fix" red runs by capping. The engineering queue was open and is now
[`TODO.md`](TODO.md), which is an ordinary backlog rather than the exceptional state
this entry was authorising.

### D. If the timing work cannot reach 20 consecutive green

**Taken, if it comes to it:** do not cap, do not skip, do not push a suite I
cannot describe. Record the surviving failures by name with their mechanisms in
`HAZARDS.md`, push with the restructure, and leave the streak unmet and stated.
An honest "19 of 20, and here is the one" beats a green number obtained by
removing the test that produced it.

✅ **It did not come to it. Verified 2026-08-19: 20 of 20 green at Unbounded on
2026-08-18**, so this contingency never fired and no failure had to be written down
under it. Kept rather than deleted, because it is the standing answer for the next
time the streak is unmet — the rule it states does not expire with the run that
happened not to need it.

### E. `NoSourceFileIsInvisibleToGit` was deleted, and this is not the entry you think it is

**This one is the maintainer's, not mine, and it went against my recommendation.**
It is filed here rather than in [`DECISIONS.md`](DECISIONS.md) for the same reason
every other letter is: a reasonable person could have decided it the other way, and
what matters is that the reversal stays cheap and findable.

**The primer, for whoever finds this without the history.**
`BuildConfigurationTests.NoSourceFileIsInvisibleToGit` listed every `.cs` file under
`src/` and `tests/` and asserted each one appeared in `git ls-files`. It existed
because of a real loss: the .NET template's **unanchored `artifacts/`** rule matched
`src/BrowserAI/Artifacts/` on case-insensitive Windows, and **five product source
files were ignored while the build, the suite and `git status --porcelain` all read
green**. An ignored file is not untracked — it is invisible — so a clean tree is
exactly what a swallowed source file produces.

**What I recommended, and it was not this.** Widen it. The `.cs` scope catches a
swallowed *source* file and misses a swallowed folder holding only data, so the
proposal was to fail on any file under `src/` or `tests/` that git ignores and that
is not under `obj\` or `bin\` — a query that returns nothing today, so it would have
landed green.

**The decision: delete it.** *"I do not think we need this test at all."* Taken
2026-08-19. The test is gone, and so is `TrackedFilesAsync`, the harness that shelled
out to `git ls-files` and existed only to serve it.

**What is now unenforced, stated plainly rather than left to be discovered.**
Nothing compares the files on disk against what git can see. A source file swallowed
by an ignore rule is invisible again, exactly as it was on 2026-08-15, and every
surface signal will read healthy while it is: the build succeeds because the compiler
reads the disk, the suite passes because it reads the disk too, and
`git status --porcelain` reports clean because an ignored file is not untracked.
**64 unanchored directory rules remain** in the upstream half of `.gitignore` — the
predicate is *"a line above the BrowserAI marker that ends in `/`, does not begin with
`/`, and is not a negation"*, re-counted 2026-08-19, with `/artifacts/` and
`/.artifacts/` the only two anchored ones as the positive control. ⚠️ *Previously
published as "nineteen unanchored directory rules" in `TODO.md`, with no predicate
written down; **no predicate I could construct reproduces nineteen** — single-segment
gives 46, case-class gives 24, single-segment-and-case-class gives 16 — so the old
figure is not corrected so much as replaced by one that names what it counted.* The
rules a .NET source folder could realistically collide with are `[Ll]og/`, `[Oo]ut/`,
`[Rr]elease/` and `[Oo]bj/`; a folder named `Logs\`, `Out\` or `Release\` under
`src/` would be swallowed exactly as `Artifacts\` was.

⚠️ **Corrected 2026-08-24 (previously "Nothing compares the files on disk
against what git can see"): something does again, from the other end, and it
was not built for this.** `HouseRuleTests.TheScannedCorpusIsExactlyWhatGitSaysTheRepositoryHolds`
landed that day to assert that the corpus every tree-as-text rule reads is what
`git ls-files --cached --others --exclude-standard` says the repository holds
— it exists because `RepositoryLayout`'s own remark about that had been false
by 520 files. **A `.cs` file swallowed by an ignore rule now fails it**: the walk
reads the disk and sees the file, git excludes it as ignored, and the arm names
it in the direction it calls *invented*. That is the defect of 2026-08-15,
mechanised again, by a test whose subject is something else.

**It is not the deleted test and does not restore it, and the difference is the
part worth reading.** The new arm covers `.cs`, `.ps1`, `.psm1`, `.mjs`, `.js`
and `.md` **across the whole tree** rather than `.cs` under `src/` and `tests/`,
so it is wider where it matters most and it is **blind to every other extension**
— a swallowed `.json`, `.txt`, `.props` or `.targets` is still invisible, and
so is a data folder holding no prose. That last gap is exactly what the widening
recommended above would have closed and what the decision declined. It also
skips loudly rather than failing on a machine with no git, where the deleted test
would simply have thrown. **So the decision stands as taken**, and what changed is
that its most expensive consequence — the 2026-08-15 loss recurring silently
— now has a mechanism in front of it by accident rather than by design. Nobody
should read that as permission to stop reading this entry.

**Where the reasoning now lives, so nobody re-adds this believing it was an
oversight.** Both `.gitignore` comments that used to name the test now say it was
deliberately deleted, name this entry, and say what the loss of it means; the refresh
procedure for the upstream half now ends in *run `git check-ignore -v` over `src/` and
`tests/` by hand* rather than *run the suite*. **That is a comment where there was a
mechanism, and the comment says so about itself** — which is precisely what
[`CLAUDE.md`](CLAUDE.md) means by the second list needing a reader rather than a
build.

**To reverse:** restore the method and its `TrackedFilesAsync` helper from `d8689cd`.
Roughly 80 lines in one file, no harness, no fixture, nothing else in the tree
depends on either. Widening it to the query above is a further one-line change to
the pattern list.

---

## Settled, recorded here only so it is not re-litigated

✅ **Re-checked against the tree 2026-08-19: all six still hold.** Nothing in `src/`
implements elicitation, roots, logging or completions — the searches return nothing,
and the positive control is that the same searches over the same corpus do find
`browser_annotate`, which is what the fifth bullet is about. Kept in full: a settled
decision that stops being findable is one that gets re-litigated.

- **Elicitation: not adopted.** Always human-answered, and it returns `cancelled`
  in headless — unusable for unattended overnight runs.
- **Roots and Logging: not adopted.** Deprecated by [SEP-2577](https://modelcontextprotocol.io/seps/2577-deprecate-roots-sampling-and-logging.md),
  which says new implementations SHOULD NOT add them. The migration path for
  Roots is explicit tool parameters — which this project already does.
- **Completions: impossible.** Only `ref/prompt` and `ref/resource` exist; there
  is no `ref/tool`, so the `session` argument can never be completed.
- **Resources: output artifacts only** — screenshots, downloads, log,
  `browserai.json`. Never the profile.
- **The tool-permission policy was removed**, on 2026-08-18. *Corrected
  2026-08-18 (previously "is being removed").* What went: five `ToolClass`
  values, the 69-name classification, the `(tool, mode)` matrix, deny-by-default
  in both directions, and `Guard` — the refusal of a `browser_get_config` answer
  containing `"secrets"`. **The reason is that it was never a boundary against
  the caller**, though its own doc comment said it was: the agent chooses the
  session directory, the profile and its cookie database sit inside it, and the
  agent runs as the same Windows user. ⚠️ **That reason was unmeasured when this
  decision was taken, and was measured on 2026-08-18: it holds.** A second
  process as the same user recovered a cookie from a session BrowserAI
  configured using `CryptUnprotectData` and AES-256-GCM alone, and App-Bound
  Encryption is not in force for the provisioned Chromium
  ([kb](kb/chromium/profiles.md#chromiums-cookie-store-and-what-it-takes-to-read-one--measured-2026-08-18)).
  **Nothing is re-opened by this**; it is recorded because the decision was right
  and was still taken on an assumption, and the same day's measurement of
  `browser_annotate` ([kb](kb/playwright/tools-and-artifacts.md#what-browser_annotate-actually-does--measured-2026-08-18))
  withdrew the one tool that survived it. Change control moved to the release gate,
  where four golden snapshots already covered more. What survives is `session`
  staying mandatory — that is *routing* — and `browser_annotate` being withheld
  from `tools/list` in every mode, as a **liveness** decision with no security
  claim. *Corrected 2026-08-18, later the same day (previously "earned the one
  refusal that survived it … one `browser_annotate` refusal wherever no window was
  promised").* See [ARCHITECTURE](ARCHITECTURE.md#sessions) and
  [§trade-offs](DECISIONS.md#the-init-design-weakens-a-security-boundary).
- **Git history is accepted as-is.** Nothing in it justifies a rewrite; the
  exposure that exists is at HEAD, and has been generalized.

---

## Added 2026-08-18, after the load-failure investigation

Four decisions raised by the agent that chased the two flakes. None blocks
anything; all four are recorded rather than taken, because each is a matter of
product voice or of reopening a safety-critical path.

### 5. The Velopack warning, 100 times per saturation run

`warn: velopack: Failed to initialize WindowsVelopackLocator` fires on **every
startup of a binary that was not installed by Velopack** — a normal, supported
configuration, and the one CI runs in. It is not a channel violation (stderr is
the diagnostic channel by design) and it did not cause either CI failure. But it
is a hundred warnings per saturation run on the stream this project relies on for
diagnosis.

**Directions.** (a) Leave it. (b) **Demote "not installed" to `Debug` and keep
`Warning` for genuine locator failures.** (c) Suppress it once per process.

**Recommendation: (b), and the agent independently reached the same view.** A
supported configuration should not warn. Not taken alone because what severity a
message carries is the product's voice, and that is yours.

> ✅ **Taken 2026-08-18: (b).** `VelopackStartup.IsRoutineNotInstalledNotice`
> demotes that one record to `Debug` on three independent conditions — the level
> is `Warning` and not `Error`, the message carries upstream's leading clause,
> and this process is not an installed one. **The two cases turned out to be
> distinguishable from outside Velopack**, which was the condition on the answer:
> 1.2.0 emits that sentence at `Warn` from exactly one branch, and the record
> that tells a broken package from an absent one — *"unable to locate a valid
> manifest file"* — is a separate `Error` and is untouched.
> `UpdateTests.OnlyTheNotInstalledNoticeIsDemotedAndOnlyWhenNotInstalled` holds
> all six arms.
>
> ✅ **Verified 2026-08-19: unchanged.** `VelopackStartup.IsRoutineNotInstalledNotice`
> is still there and is still consulted from exactly one place, `Program.cs`, on the
> three conditions above.

### 6. The per-directory gate at 120 seconds

⚠️ **Corrected 2026-08-19 (previously "at 60 seconds … That is 2× `RenameWindow.Budget`
and roughly 18× the measured queue at the charter's 100-process design point").**
`LockScopes.PerDirectoryGate` is **`TimeSpan.FromSeconds(120)`**, and has been since
`71a3d81` on 2026-08-18 — *"The gate outlasts the SUM of the waits taken inside it,
not one of them"*. The raise landed after this entry was written and the entry was
never re-read, which is the failure this sweep exists to catch: the number a
maintainer reviews from was one working day stale and nothing said so.

**What the second raise was for**, and it is not "60 was too small in practice".
The test the original entry cites had been checking the gate against the *largest*
wait taken inside it. Several such waits run in series under one acquisition, so the
property that has to hold is the gate against their **sum**, and 60 s did not satisfy
it. `SessionLockTests.TheGateOutlastsEveryWaitTakenInsideIt` now fails the build if
either number crosses the other — which is still the part that matters more than the
value, and is the one sentence of the original entry that survived both raises.

**Defensible, not uniquely correct.** Reversible in one line, guarded by a test.

### 7. The deeper fix — **TAKEN 2026-08-18, and the entry below was wrong twice over**

⚠️ **Corrected 2026-08-19. Previously titled "The deeper fix that was NOT taken",
and it said: "A loser holds the gate only to name the holder … A contender that
probed *before* taking the gate would remove the queue entirely, and with it the
whole class of defect. It was not taken because it has TOCTOU subtleties and
reopens the most safety-critical path in the product. This is the real fix, and it
deserves a decision rather than a drive-by."** Both halves of that are wrong now:
the verdict, and the framing that produced it.

**The verdict: it was taken**, the day after this entry was written, in `2759aad`
— *"The gate was being taken to answer a question the kernel had answered"*.
`SessionLock.ProbeForHolder` opens `browserai.json` **in front of** the per-directory
gate, reads the sharing violation as the kernel's answer to *who owns this*, and
refuses there without ever joining the queue. The queue is gone for every contender
that can name the holder, measured before and after
([kb](kb/windows/detection.md#named-mutexes-and-lock-files)).

**The framing: the probe was not a thing to be added — it was already there.** The
entry describes a design in which every loser takes the gate purely to name a holder
the kernel has already named. Reading the code found a fast refusal in front of the
gate doing exactly that, so the "deeper fix" was largely a description of shipped
behaviour rather than a proposal. What actually needed deciding was the opposite
question: what to do about the harm that fast refusal was already causing.

**And the mechanism was not the window this entry predicted.** The entry expected
TOCTOU — a contender slipping in and *taking* a directory between the writer's
rename and its re-open. That cannot happen: taking a directory means passing through
`TakeOrReport`, which needs the per-directory gate, and the writer is holding it. So
a contender cannot **take** inside that gap. What it can do is **look** — and looking
is what does the damage. `ProbeForHolder`'s handle asks for `FileAccess.ReadWrite`,
and an open sharing only `Read` is refused while that handle lives. In CI run
`32203064556` attempt 1 the writer's own re-open was refused by a peer's *probe*, so
it surrendered a directory whose record already named it, and the next contender read
that record as a live session and reclaimed it — two holder statements in one
`browserai.json`, 61 ms apart.

**The probe cannot be made harmless, and that is a property rather than a defect.**
To be refused by a holder's `FileShare.Read` it must ask for access outside `Read`,
and a handle whose granted access is outside `Read` is exactly what an open sharing
only `Read` is refused by. **Detecting an owner and blocking one are the same
capability.** So the cost is absorbed on the gated side instead: `SessionLock.ReopenHeld`
may wait a sharing violation out, but only where the caller **holds the gate** and the
record on disk **already names it** — a precondition, not a guess, because becoming an
owner requires the gate the waiter is holding. Three call sites qualify, and it is
still bounded at `RenameWindow.Budget`; a handle outlasting thirty seconds is a
different fault and is still reported. Shipped in `6b32caa`.

**What is left open, stated rather than closed by silence:** one of the two ownership
tests still reads a peer's probe as `Contended`. That is a wrong *sentence*, not a
wrong owner, and it has its own hazard row rather than being left as unstated residue.
Widening those tests would wait a live owner out for thirty seconds and then report it
as a peer looking, which is the mechanism inverted.

### 8. The silent Chromium death — how hard to chase it

**The leading theory is disproven:** 80 concurrent headless Chromium instances
started with **zero** failures at 1,436 machine processes, nearly double what the
saturation test produces. So no ceiling was recorded, because there is none to
record at this scale. CPU starvation and desktop heap (no documented API reports
it) remain open.

It is now **self-explaining**: Chromium runs with `--enable-logging --log-file
--v=1` — on Windows it writes startup failures to a file, not stderr, and nobody
had asked for one — and the failure carries that log plus `GetPerformanceInfo`
figures.

**Directions.** (a) **Wait for the next occurrence, which will now diagnose
itself.** (b) Build a deliberate CPU-saturation reproduction.

**Recommendation: (a).** It has been seen three times in three days, the cheap
theories are dead, and a speculative reproduction is expensive against a failure
that will now explain itself the first time it recurs.

✅ **Verified 2026-08-19: still open, still correct, and it has not recurred.** The
`--enable-logging --log-file --v=1` arguments are in `StraySweepTests`, so the trap
is set; nothing in `CHANGELOG.md` or `HAZARDS.md` records another occurrence since
this was written. **Not knowing whether the silence is a fix or a quiet machine is
the expected state of direction (a)** — it buys diagnosis on the next occurrence,
not evidence that there will not be one. This entry stays open until one arrives, or
until somebody chooses (b).

⚠️ **IT RECURRED, 2026-08-26 19:43, and the trap fired exactly as designed —
which is the first new evidence this question has had since it was written.**
`StraySweepTests.TheSweeperFindsARealBrowserItLaunchedItselfInTheInteractiveSession`,
the single failure in a 626-case full run from PowerShell. **It did not recur in
the two full runs taken immediately afterwards** — one from each shell, 626 of
626 in both — which is one in three on this machine on this day and says nothing
about a rate. **The whole of what the browser said**, from its own log,
timestamps as written:

```
[68076:77224:0826/194308.000:VERBOSE1:…\policy_service_impl.cc:632] Taking initial snapshot of POLICY_DOMAIN_CHROME policies
[68076:77224:0826/194308.004:VERBOSE1:…\variations_field_trial_creator.cc:603] Applying FieldTrialTestingConfig
[68076:77224:0826/194308.007:VERBOSE1:…\variations_field_trial_creator.cc:399] VariationsSetupComplete
[68076:77224:0826/194308.026:VERBOSE1:…\scheduler_loop_quarantine_config.cc:195] No entry found for browser/global.
[68076:77224:0826/194308.026:VERBOSE1:…\scheduler_loop_quarantine_config.cc:195] No entry found for browser/*.
```

**Exit code 1. Nothing on stdout, nothing on stderr, both pipes at EOF.** It died
**26 milliseconds** into its own startup, after `VariationsSetupComplete` and
before anything that would name a subsystem.

**The machine at that instant**, from `GetPerformanceInfo`: 527 processes,
13,062 threads, 289,927 kernel handles, **82,982 MiB physical free** of 130,989,
commit 64,815 of a 141,229 limit, 32 processors.

⚠️ **What that rules out and what it leaves.** It is **not** memory: 63% of
physical RAM was free and commit was at 46% of its limit. It is **not** a process
or handle ceiling at this scale — the 80-instance experiment above reached 1,436
processes with zero failures, nearly three times this run's 527. It is **not** a
disk or path failure, because the browser got far enough to read policy and apply
a field-trial config. **CPU starvation and desktop heap both survive**, and
desktop heap is the one the figures above cannot see — it is not readable without
a kernel debugger, and *a Chromium that cannot create a window object fails
exactly this way*: no message window, no diagnostic, exit 1. The failing test is
one whose entire subject is that a message window appears.

**The question stays open and the recommendation does not change.** One
occurrence with a 26 ms log is a much better lead than three occurrences with
none, and it is still not a diagnosis. What would settle it is direction (b) — a
deliberate reproduction — now aimed specifically at **desktop heap** rather than
at CPU: launch into a session station whose heap has been consumed, and see
whether this exact shape comes out. *Nobody has run that.* Recorded here rather
than in `.work/`, because the capture that produced it lives in a scratch
directory this machine deletes.

🔬 **DIRECTION (b) WAS RUN, 2026-08-27, and the verdict is REPRODUCED
DIFFERENTLY — every element of the measured shape comes out except the exit
code.** *"Nobody has run that"* is no longer true. The rig is at
`.work/2026-08-27-desktop-heap`, in a scratch directory this machine deletes, so
everything it established is written down here and in `kb/windows/processes.md`
rather than left there.

**The rig, in two sentences.** A desktop of its own is created inside `WinSta0`
with `CreateDesktopW` — each desktop gets its own heap allocation, and
`GetUserObjectInformationW` with `UOI_HEAPSIZE` says this one got **20,480 KB**,
the identical figure to `WinSta0\Default`, so the interactive desktop is never
touched — and one filler process launched onto it through
`STARTUPINFO.lpDesktop` creates message-only windows until `CreateWindowExW`
refuses. **Window text lives in the desktop heap**, which is what makes this
cheap enough to run eighty times: 4,637 windows carrying a 2,048-character
title spend the whole 20,480 KB in 2.7 seconds and 4,640 USER handles, and the
provisioned Chromium is then launched onto that same desktop with the sweep
test's own command line, both pipes drained to end of file, `GetPerformanceInfo`
read at the instant it dies, and a probe already sitting on the desktop asked
whether a `Chrome_MessageWindow` ever appeared.

**Point by point against 2026-08-26 19:43** — eight launches onto a heap
exhausted to the byte, against sixteen healthy controls on the same desktop
before and after:

| Measured in the wild | Produced by the rig | |
|---|---|---|
| **exit code 1** | **`0x80000003`** — `STATUS_BREAKPOINT` — 8 of 8 | ✗ |
| nothing on stdout, nothing on stderr, both pipes at EOF | **0 bytes on each, both drained to EOF**, 8 of 8 | ✓ |
| the log ends after `VariationsSetupComplete`, at the two `scheduler_loop_quarantine_config.cc:195` lines, 26 ms in | **the same five lines, in the same order, and nothing after them** in 6 of 8 — the other two carry one further `webrtc_event_log_manager.cc:126` line — 10 ms from its first line to its last | ✓ |
| no message window ever created | **zero** `Chrome_MessageWindow` of any title, 8 of 8, against **five** in every one of the sixteen controls, one of them titled with the profile path | ✓ |
| 527 processes, 289,927 handles, 63% of RAM free — every ceiling ruled out | 430 processes, 262,355 handles, 66% free — **the same clean bill of health, on a machine that is out of the one resource** | ✓ |

⚠️ **The threshold is far sharper than there was any reason to expect, and the
silence belongs to the last few kilobytes rather than to the shortage.** Three
launches at each level:

| Windows held | Free heap | What the browser did |
|---:|---:|---|
| 4,637 | 0 KB | **died 8 of 8** — 5 or 6 log lines, **0 bytes on both streams** |
| 4,636 | ≈4.4 KB | died 3 of 3 — but **61 to 63 log lines and 440 bytes of stderr**, having got as far as starting its GPU child |
| 4,635 | ≈8.8 KB | died 2 of 3, 104 to 107 log lines; the third lived |
| 4,634 | ≈13.2 KB | **lived 3 of 3**, 676 to 678 log lines |
| 4,632 down to 3,600 | 22 KB to 4.6 MB | lived 3 of 3 at every one of eight further levels |

**So the shape recorded in the wild belongs to a heap spent to the byte and to
nothing less.** One window of headroom already buys sixty-three log lines and a
stderr message, which is a far more specific claim than *"the browser died when
something was tight"*.

**What is actually crashing, read from Chromium's own tree on 2026-08-27.**
`WindowImpl::Init` in `ui/gfx/win/window_impl.cc` ends its window-creation path
with two crash sites and no error return: a branch taken when the window is null
**and `GetLastError` reported nothing at all**, which ends in `NOTREACHED()`, and
`CheckWindowCreated(hwnd_, create_window_error)` after it. A `CreateWindowExW`
that returns null therefore takes Chromium into an immediate crash rather than
into a recovery path, and **the crash is a check, and a check does not log** —
which is the whole of why a browser that cannot make a window says nothing
anywhere.

**And the rig measured which of the two it is, by accident.** A refusal for want
of desktop heap does **not** reliably set a last error: filling with
2,048-character titles, the refusing `CreateWindowExW` reported **`GetLastError`
= 0** on both a 512 KB heap and the 20,480 KB one, while filling with
one-character titles reported `ERROR_NOT_ENOUGH_MEMORY`. The regime that
reproduces the wild shape is the *former*, so the site is `NOTREACHED()` on the
no-error branch rather than the check below it. **INFERRED** — it joins a
measured last error to a source branch read in the same session, and nothing
here has read a stack.

⚠️ **The exit code is the one thing that did not reproduce, and the rig's own
evidence is that the exit code is the least stable part of this failure.**
Across four exhaustion regimes and twenty-eight deaths it produced **two** codes
and never a 1: `0x80000003` where the heap was simply full, and **`0xE0000008`**
— Chromium's own out-of-memory exception code — in the arm where the heap was
handed *back* while the browser was starting, which is what a real desktop's
heap does all day. A third code in the wild is therefore consistent with
desktop-heap exhaustion rather than evidence against it. **Said where it
belongs rather than in a caveat: I could not establish what produces exit code
1, and eighty launches did not produce one.**

**Two further regimes, recorded because they are further from the wild shape
rather than nearer.** A heap spent on **23,718 small windows** instead of 4,637
large ones kills the browser earlier still — 5 of 5, `0x80000003`, and **no log
file at all** — and there `CreateWindowExW` refuses with
`ERROR_NO_MORE_USER_HANDLES` rather than `ERROR_NOT_ENOUGH_MEMORY`. The
released-mid-startup arm produces the mixed codes above with 6 or 9 log lines,
and lets the browser live outright when the release lands inside the first
40 ms. **Only exact exhaustion by large allocations produces the five-line log.**

**One fidelity note, stated because it looked like a rig artefact and is not.**
Every launch on the rig desktop writes *"Sandbox cannot access executable …
Access is denied"* to stderr — and so does every launch on `WinSta0\Default`,
measured in the same session as the control. It belongs to the provisioned tree,
not to the rig.

**The recommendation changes.** Direction (a) has done its job: the trap fired,
and what it caught is now reproducible on demand. What is left open is that the
failure still cannot name itself when it happens, which is a different question
from what causes it. Per open question, then:

- **(a) Leave it here.** The diagnosis is recorded and the sweep test goes on
  failing roughly once in three runs when it happens, with a message that says
  the browser died and cannot say why.
- **(b) Make the failure name itself.** When `WaitForAttributionAsync` finds the
  browser gone before a message window appeared, have it attempt one
  `CreateWindowExW` of its own and put the result in the message.
  `ERROR_NOT_ENOUGH_MEMORY` there *is* the diagnosis, at the only moment it can
  be taken. No new dependency, one call, and it is the only reading that
  separates this from every other reason a browser can die.
- **(c) Put a desktop-heap column in `MachineLoad.Describe()`.** Honest only up
  to a point: `UOI_HEAPSIZE` reports the *size* and no API reports the *usage*,
  so the truthful version of this collapses into (b)'s probe, run always rather
  than only on failure.
- **(d) Harden instead of diagnosing** — give the suite's browsers a desktop of
  their own with a heap of their own. It removes the failure from the suite and
  removes the suite from the population that would ever see it again, which is
  a real loss: this test is the only place the product finds out that a machine
  can run out of this.
- **(e) Chase exit code 1 until it is explained.** The gap is small, and it is
  the one thing standing between REPRODUCED DIFFERENTLY and REPRODUCED EXACTLY.
  It is also the direction with no bounded end: it needs a wild recurrence to
  catch with a probe attached, which is (b) again.

**Recommendation: (b)**, which also serves (e) — the probe that names the cause
on the next occurrence is the same probe that would capture the exit code beside
it. **This entry stays open**, because what it is now open on is narrower than
what it was opened for: not *what kills the browser* but *why the wild exit code
was 1*.

✅ **The maintainer chose (e), 2026-08-27: chase exit code 1 until it is
explained** — over the recommendation, which was (b). The two are not
alternatives and the choice says which is the subject: **(b) is (e)'s
instrument** and the entry above already said so, so (b) was built anyway, and
what (e) adds is a second, bounded effort aimed at the one number that would not
come out.

🔬 **THE PROBE IS BUILT, 2026-08-29.** `Harness/DesktopHeapProbe`, printed by
`StraySweepTests.WaitForAttributionAsync` in the branch that finds a launched
browser gone before any message window appeared — which is the branch the wild
failure takes, and the only instant at which this reading exists: a heap that was
full when the browser died is commonly not full a second later, because the
windows that filled it belong to processes that come and go.

**One `CreateWindowExW`, and the deliberate choices in it.** The class is the
system-global `STATIC` rather than one of our own, so the probe really is one
call — a `RegisterClassExW` would be a second desktop-heap allocation taken
*before* the one being measured, able to fail first and for the same reason,
which would put a second failure mode inside a diagnostic written to remove one.
The title is **2,048 characters**, which is the rig's own regime rather than a
round number: window text lives in the desktop heap, so the title length *is* the
size of the allocation, and the table above shows a heap with one window of
headroom (≈4.4 KB) still killing a browser — a probe that asked for the smallest
possible allocation would report a clean create on a desktop that is already
killing browsers. The window is destroyed immediately, because a diagnostic that
leaked one would consume the resource it was written to measure.

**Four readings, and the third is the one this entry's own measurement forced.**

| What the call did | Verdict | What the message says |
|---|---|---|
| created the window | `NOT DESKTOP HEAP` | the heap had room for the allocation an exhausted one refuses a starting Chromium, **so whatever killed the browser it was not this** — which is information, because it retires the one ceiling none of the other figures can see |
| refused, `ERROR_NOT_ENOUGH_MEMORY` (8) | `DESKTOP HEAP EXHAUSTED, named by the refusal` | *that is the diagnosis*, at the only moment it can be taken |
| refused, **`GetLastError` = 0** | `DESKTOP HEAP EXHAUSTED, and the refusal named nothing` | **read as the diagnosis rather than as a gap**, and the message says why in place: a refusal for want of desktop heap does not reliably set a last error, and with a 2,048-character title it reported 0 on both a 512 KB heap and the 20,480 KB one while one-character titles reported `ERROR_NOT_ENOUGH_MEMORY` — and the long-title regime is the one that reproduces the wild shape |
| refused, `ERROR_NO_MORE_USER_HANDLES` (1158) | `DESKTOP HEAP EXHAUSTED, named by the refusal` | out of USER handles rather than heap bytes — the many-small-windows regime, 23,718 against 4,637, which kills earlier still and with no log file at all |

Anything else refused reports `REFUSED FOR SOMETHING ELSE` and names the number
against those three signatures; a probe that could not run at all reports
`READING NOT TAKEN` rather than a clean bill of health. **The zero is this call's
own answer and not a stale one** — on .NET, and unlike .NET Framework, the error
information is cleared to 0 before a `SetLastError` callee is invoked, so a 0
afterwards is what *this* call set. That is stated in the type, because the whole
third row rests on it, and it is stated as **read rather than run**: the
behaviour is Microsoft's documented one, verified 2026-08-29, and the generated
stub is not emitted to disk in this build so nothing here has looked at it.

**Planted red, 2026-08-29**, before the probe was wired in:
`StraySweepTests.ABrowserGoneBeforeItsWindowAppearedIsAskedWhetherTheDesktopHeapWasSpent`
provokes the branch with the test probe run with no arguments at all — it falls
through its own dispatch to `Usage()`, writes nothing and is gone, so the branch
is taken on the first pass of the loop with no browser and no rig — and the
message came back carrying the exit code, the streams, the log and the machine
and saying nothing whatever about a window. **What the arm asserts is that the
reading was taken, never what it says**: the verdict is a property of the
developer's other windows, which is the same bar `MachineLoad` is held to, so it
requires the heading and *exactly one* of the five verdicts.

⚠️ **Two limits, said here rather than left to be assumed away.** The probe reads
the desktop *this thread* is on — the desktop a suite-launched browser inherits
today, and silently the wrong one if direction (d) is ever taken and browsers get
a desktop of their own. And it is one sample: a heap refilled between the death
and the probe reads clean, which is a false negative it cannot tell from a
healthy machine.

📋 **The rig chase is commissioned and has not been run.** It is the other half
of (e) and it is deliberately a separate batch, because it launches browsers and
this one held the suite gate. **Seed hypotheses, in the order they were argued:**
*crashpad* — the handler is itself a process creation that may need desktop heap,
and a handled exception exits differently from an unhandled one, so the Crashpad
directories in a wild profile against a rig one are the first thing to compare;
*wild-desktop dynamics* — the interactive desktop's heap is being handed back and
taken all day, and the rig's is exhausted and static, which is already known to
change the exit code, since the released-mid-startup arm produced `0xE0000008`;
*alternate exhaustion points* — `RegisterClassExW` refusing rather than
`CreateWindowExW`; and *GPU-child-first death*, since the 4,636-window level got
as far as starting one. **It ends when its enumerable regimes are exhausted,
with a report** — and the unbounded tail is the armed probe above, which is the
whole reason (b) was built before (e) was chased.

**This entry stays open on exit code 1 and nothing else.** What kills the browser
is answered; what the wild machine did differently is not.

✅ **ANSWERED 2026-08-29, and the answer is that the exit code was never
Chromium's. `1` is what an external `TerminateProcess(handle, 1)` leaves
behind** — the browser did not crash, and on 2026-08-26 it was not short of
desktop heap. Direction (e) is closed **REPRODUCED EXACTLY**, and the row the
table above could not fill is filled by a different mechanism from the one the
rest of the table measures.

**What each way of ending a process leaves as an exit code**, measured
2026-08-29, every arm run against a real victim and read back with
`GetExitCodeProcess`:

| How it ended | Exit code |
|---|---|
| `TerminateProcess(handle, 1)` | **1** |
| `taskkill /F /PID` | **1** |
| `Stop-Process -Force`, which is .NET's `Process.Kill()` | −1 (`0xFFFFFFFF`) |
| the last handle to a `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` job closing, 3 of 3 | **0** |
| a Chromium `CHECK` crash, crashpad connected and a 2,553,376-byte dump written | `0x80000003` |
| a Chromium killed by desktop heap: 28 deaths on 2026-08-27, 9 more on 2026-08-29 | `0x80000003` or `0xE0000008` |

**The `CHECK`-crash row is the control that ends seed hypothesis 1**, and it was
run first because the whole crashpad theory turns on it: `chrome.exe
--headless=new … --crash-test` crashes the browser process on purpose, and it
exits **`0x80000003`** *while* writing a real minidump into the profile's
`Crashpad\reports`. **A handled crash and an unhandled one leave the same exit
code**, so a crashpad handler cannot be what the wild machine had and the rig
lacked. The handler is not scarce either: a healthy launch starts **two**
`--type=crashpad-handler` children and creates `Crashpad\{reports,attachments,
settings.dat}`, and *every one of the nine desktop-heap deaths on 2026-08-29
wrote exactly one dump*, so on the rig the handler was connected, did capture
the crash, and the code was `0x80000003` anyway.

⚠️ **So the wild browser was ended from outside, and this tree has exactly two
places that end anything with a 1** — `git grep` over `src/` and `tests/`,
2026-08-29: `StrayCandidate.TryTerminate` in
[`BrowserProcesses.cs`](src/BrowserAI/Interop/BrowserProcesses.cs) and
`ProcessIdentity.Terminate` in
[`ProcessIdentity.cs`](tests/BrowserAI.Tests/Harness/ProcessIdentity.cs). **The
product's sweep is ruled out by its own design**, read from
[`StraySweep.cs`](src/BrowserAI/Sessions/StraySweep.cs) the same day: Chromium
attribution runs *process → profile* and the process publishes the path through
its message window, so a browser that never published one is `Unattributable` —
*"reported loudly and never acted on"*. The wild browser never published one;
that is the very thing the failing test reported. **READ rather than run.**

**That leaves the harness, and the reproduction is deterministic.**
`JobObjectScope.Launch` writes every process it starts — browsers included — into
`<repo>\.work\spawn-record.txt`, and `ScratchRoot.EnsureReclaimed` calls
`SpawnRecord.Reclaim(SpawnRecord.Path)` on first use of a scratch root **in each
process**, which terminates every recorded pid that is still that process with
`TerminateProcess(handle, 1)`. Start a second `BrowserAI.Tests.exe`, launch the
provisioned Chromium with the sweep test's own command line, append its
`(pid, creationFileTime)` the way the harness does, and the second host kills it:
**exit 1, 18 of 18**, across a lead sweep of nine settings.

**The reclaim fires at a stable instant, which is what makes the rest of the
signature tunable.** A warm second host empties the record at **+543 to +557 ms**
after it starts, 4 of 4; the first, cold one took +797 ms twice. Browser age at
the kill is therefore `R − lead`, and the age is what decides how much of the
wild shape comes out:

| Lead | Browser age at the kill | What came out |
|---:|---:|---|
| 0 ms | ~765 ms | exit 1, 241–242 log lines, 440 B of stderr |
| 300–360 ms | ~230–280 ms | exit 1, 55–102 log lines, 0–440 B of stderr |
| **400–430 ms** | **~140–175 ms** | **exit 1, 3 to 5 log lines, 0 bytes on stdout and 0 on stderr** |
| 460–500 ms | ~40–100 ms | exit 1, no log file written yet |

**Point by point against 2026-08-26 19:43, at lead 400:**

| Measured in the wild | Produced by the reclaim | |
|---|---|---|
| **exit code 1** | **exit code 1**, 18 of 18 | ✓ |
| nothing on stdout, nothing on stderr, both pipes at EOF | **0 bytes on each, both drained to EOF** | ✓ |
| five log lines ending at the two `scheduler_loop_quarantine_config.cc:195` lines | **the same five lines, same files, same line numbers, same order** — 13 ms end to end against the wild's 26 on a loaded machine | ✓ |
| no message window ever created | **zero** `Chrome_MessageWindow` for the profile | ✓ |
| 527 processes, 289,927 handles, 63 % of RAM free — every ceiling ruled out | **nothing is wrong with the machine, and under this mechanism nothing needs to be** | ✓ |

**Why the five lines are five.** A healthy browser writes its fifth line and then
says nothing for **26 ms** before `webrtc_event_log_manager.cc:126` — measured
2026-08-29 off a 676-line healthy log. That silent gap is the window in which a
death leaves exactly five lines, and it is the same gap the desktop-heap rig hit
from the other side. **Two entirely different mechanisms truncate the log in the
same place, and the exit code is the only thing that ever separated them** —
which is exactly why it was worth chasing.

⚠️ **What this does to the desktop-heap finding: it stands as a failure mode and
falls as the diagnosis of 2026-08-26.** Everything the 2026-08-27 rig measured is
still true and still reproducible — the calibration was re-run on 2026-08-29 and
came back identical, 4,634 windows of 2,048-character title filling a
20,480 KB desktop, the browser living 3 of 3 above the cliff at 676–678 log
lines. It is a real way for a Chromium to die silently on this machine. **It is
not what killed the browser on 2026-08-26**, because that browser exited 1 and
this one never does.

🔬 **Seed hypothesis 2 was run and is the arm nobody had: cross the cliff *during*
startup.** The 2026-08-27 rig could exhaust the heap before the browser started,
or hand it back mid-startup; the missing half was to pre-fill to a level the
browser survives and then take the last three windows at a chosen instant.
**Prediction, written before the run: early instants give `0x80000003` and a five
or six line log, later ones `0xE0000008` or life, and no instant gives a 1.**
Ten instants, one launch each, pre-filled to 4,634:

| Trip at | Exit | Log lines | Streams | Crash dumps |
|---:|---|---:|---|---:|
| +0, +5, +10, +15, +20, +50, +120 ms | `0x80000003` | 6 | 0 B / 0 B | 1 each |
| +30, +80 ms | `0xE0000008` | 9, 10 | 0 B / 0 B | 1 each |
| +200 ms | **lived** | 396 | 0 B / 859 B | **29** |

**No instant produced a 1**, which is the prediction confirmed and the seed
retired. The +200 ms row is the one worth keeping for itself: **a heap exhausted
after the browser is up kills its children instead of it** — twenty-nine dumps,
a browser still running, and a log that goes on.

**Seed hypothesis 3 was not re-run, and this says so rather than implying
coverage.** The 2026-08-27 rig already mapped the alternate sites — many small
windows refuse with `ERROR_NO_MORE_USER_HANDLES` and kill earlier with no log
file at all, large ones with `GetLastError` = 0 — and the gradient walked the
heap from 0 KB to 4.6 MB. Between them those regimes produced 28 deaths and two
exit codes. Re-running them to look for a 1 would have been the open-ended grind
the brief forbade, once a 1 was shown to need an external terminator.

⚠️ **What I could not establish, said here rather than at the end: which
terminator fired on 2026-08-26 at 19:43.** Two produce exactly this trace —
`taskkill /F`, which anybody or any agent may type at a shell, and the harness's
own reclaim — and **nothing durable records either**. `SpawnRecord.Reclaim`'s
report lives in memory on `ScratchRoot.LastPassReport` and only a *survivor*
reaches the coverage block, so a reclaim that succeeded leaves no trace at all.
The run's own capture survived in `.work/p6/chromium-death-evidence.txt` and
names no second process; it does add one number, that the test failed in
**561 ms**, which is consistent with a kill a few hundred milliseconds into a
browser that had just been launched. **There is also an argument against the
reclaim having been the one**: `EnsureReclaimed` does not only terminate — it
`TreeDelete`s every directory under the scratch root — so a mid-run reclaim
should have produced a cluster of failures rather than the single one that run
reported. That argument is not decisive, and neither is anything else here.

---

### 8a. The harness can terminate a live run's browsers with exit code 1, and nothing stops it

**Primer, for somebody who has not read the above.** Every process the suite
starts is recorded in `<repo>\.work\spawn-record.txt` as `(pid, creationFileTime)`
so that the *next* run can end what a killed run left behind. The pass that reads
it, `ScratchRoot.EnsureReclaimed`, runs on first use of a scratch root **in each
process** — not once per run — and it terminates every recorded pid that is still
that process. Within one test host that is exactly right: it runs before any
browser exists. **Across two, it is a machine-wide kill with no interlock**: a
second harness process reading a live run's record ends that run's browsers,
probes and slices with exit code 1, and then deletes the scratch tree they are
using. Measured 2026-08-29: 18 of 18. `CLAUDE.md` already warns that concurrent
suite runs eat each other and names *"a browser vanished"* as one of the shapes;
what was not known is that the eating leaves a **1**, which is indistinguishable
from a great many other things until somebody measures the alternatives.

**One second host in the tree does not trigger it, and that is measured rather
than assumed.** `SuiteCoverageTests.AFilteredChildRunReadsAsFilteredAndIsRefusedAsARelease`
starts a real `BrowserAI.Tests.exe` inside a run; with its filter it never
touches a scratch root, so the record was not emptied and a live browser
survived, 2 of 2. **That is one filter's behaviour, not a property of the
mechanism** — a filter that selected anything using `ScratchDirectory` would fire
it, and `--treenode-filter` is already documented in this repository to select
more than it looks like it selects.

**Directions.**

- **(a) Leave it.** The pass is correct for the case it was written for, and the
  hazard needs two harness processes at once, which the working rules already
  forbid. Cost: the next occurrence looks exactly like the one that took two
  rigs and eighty launches to not explain.
- **(b) Make the record self-identifying.** Write the owning run's id beside each
  line and reclaim only lines whose owner is not this run and is no longer alive.
  A live run's rows are then invisible to a second one, and the pass keeps doing
  its job for a run that really was killed.
- **(c) Interlock on a machine-wide mutex the way the sweep does**, held for the
  length of a run rather than the length of the pass, so a second harness process
  either waits or refuses. Strongest, and it changes what "run the suite twice at
  once" means from *undefined* to *serialised*.
- **(d) Make the reclaim announce itself.** A pass that terminated something
  writes it to the process log rather than to an in-memory list, so the next
  occurrence is one grep rather than one investigation. Cheapest, diagnoses
  rather than prevents, and composes with any of the above.
- **(e) Stop recording browsers.** Only the job object would contain them, which
  is what it is for. Loses the recovery the record exists for, in the one case it
  exists for.

**Recommendation: (b) with (d).** (b) removes the failure without weakening the
recovery, and (d) is what makes the *next* surprise cheap — the whole cost of
this question was that a forced termination looks like a crash until somebody
measures both. **Not taken: it is a change to the harness's kill policy and it
belongs to the maintainer.**

---

## Added 2026-08-18, from the honesty pass

### 9. What the `browserProvisioning` state word should say — **ANSWERED 2026-08-18: `provisioning`**

`init` answers with one of three words — `installed`, `downloading`, `failed` —
and the word is the surface, deliberately: *"a caller that has to parse English to
find out whether a navigation will work is one upstream wording change away from
getting it wrong."*

**`downloading` is narrower than the state it names.** One word covers waiting on
another process's provisioning mutex, deleting an abandoned tree, downloading,
extracting, and pruning superseded revisions. The waiting case is the one that
bites: that process has started nothing, cannot see what the holder is doing, and
the holder may well be finished. **The sentence beside the word has been fixed**
and now says it is watching for another process's marker rather than claiming a
download (`ProvisioningTests.AProcessWaitingOnAnotherOneDoesNotSayItIsDownloading`).
The word has not, because renaming what a model reads is product voice.

**Directions.** (a) **Leave `downloading`.** It is what every consumer branches
on — *installed* / *not yet* / *failed* — and the middle bucket is correct for
every phase it covers. (b) **Rename it `provisioning`**, which is true of all five
phases; costs an edit to `ARCHITECTURE.md`, `TESTING.md` and two test assertions,
and any external consumer parsing the word breaks once, loudly. (c) **Add a fourth
word, `waiting`**, distinguishing the mutex-loser; most precise, and it makes a
three-state surface a four-state one for a distinction no caller acts on
differently. (d) **Keep the word and lean on the sentence**, which is what is
shipped today.

**Recommendation: (b).** The word is read by a model, and `downloading` invites it
to reason about bandwidth and download time in a state where neither applies. The
migration cost is one commit and there is no external consumer to break — this
build has never shipped a caller that parses it. (d) is a defensible hold; (c) is
the only option that adds surface without adding an action.

**Answered 2026-08-18: (b).** `init` now answers `installed` / `provisioning` /
`failed`, and `ProvisioningState.Downloading` is `ProvisioningState.Provisioning`.
**(c) was declined on its own stated grounds** — no caller acts differently on the
mutex-loser, so a fourth word would be surface with no action behind it, and the
sentence beside the word already separates all five phases. Nothing about the
bucketing moved.

**One thing the directions above did not price, and it is the load-bearing half.**
`downloading` carried a recovery *inside the word*: a model reading it knows it is
waiting on bytes and that calling again later is the move. `provisioning` says
only *not yet*. So both unfinished detail sentences gained an explicit *"Browser
tools are refused until it lands and BrowserAI's own tools keep working; wait and
call the same tool again on the same session, which does not have to be
re-created"*, asserted on the branch where a reader has least to go on
(`ProvisioningTests.AProcessWaitingOnAnotherOneDoesNotSayItIsDownloading`) and on
the download branch (`.InitReturnsImmediatelyAndSaysTheBrowserIsDownloading`). A
rename that leaves a model with a state and no action is not a neutral rename.

**What it cost, against what (b) predicted:** `ARCHITECTURE.md`, `TESTING.md` and
two test assertions, as forecast — plus the enum member, the status factory, four
doc comments that asserted the word was staying, and this section. No external
consumer existed to break.

### 10. Whether the client's 2 KB cap is per string or per tool — **ANSWERED 2026-08-18: per string**

Claude Code's MCP documentation says, verbatim: *"Claude Code truncates tool
descriptions and server instructions at 2KB each."* **"Each" does not say each
what**, and the answer changes what the new budget gate means.

Everything shipped today assumes **per string**: one budget for `instructions`,
one per tool `description`, one per parameter `description`. Under that reading the
whole surface fits with room to spare — the largest string is
`browserai_init`'s description at **1,639 bytes of 2,048**.

**Under a per-tool-total reading it does not fit.** `browserai_init`'s whole
`tools/list` entry — name, title, description and the serialized `inputSchema`
with all seven parameter descriptions — is **3,428 bytes**, sliced out of the raw
frame the server wrote rather than re-serialised, and it is the only one
of the 65 over the line. If that reading is right, `browserai_init` is truncated
today, mid-schema, and trimming its description would move text from one capped
bucket into the same capped bucket rather than fixing anything.

**Answered 2026-08-18 @ Claude Code 2.1.234: the cap is PER STRING.** Direction
(a) was taken and it reported. *Previously: "Nobody has checked, and the gate says
so rather than hiding the assumption behind a passing test … Recommendation: (a),
and it is already commissioned."*

A probe tool whose **whole entry was 4,578 bytes** — a 1,500-character
description plus four 700-character parameter descriptions, every string under
the cap — arrived at the model **completely intact**; entries of 17 KB and 20 KB
did too. So **`browserai_init` is not truncated and never was**, and (c) —
splitting it in two — was correctly not taken.

**Four things nobody had asked came out of the same run**, and three of them
matter more than the original question:

- The unit is **UTF-16 characters, never bytes**. A 2,048-character description
  weighing 6,004 bytes arrived whole. The gate had been in bytes, which is
  strictly stronger and therefore capable only of false failures, but it was
  wrong about the world and is now in characters.
- The predicate is **`> 2048`** exactly — 2,047 intact, 2,048 intact, 2,049 cut.
- **Parameter descriptions are not truncated at all** (question 1 above).
- **The cut is visible to the model and invisible to the server**: the client
  appends the literal `… [truncated]`. Nothing about it reaches the server, which
  is why the gate has to be a build failure rather than a run-time check — but it
  also means *"did that arrive whole?"* is a question a model can answer.

**The method is the reusable part**, because it does not depend on a model
complying: Claude Code honours `ANTHROPIC_BASE_URL`, so pointing it at a local
recorder with a throwaway `ANTHROPIC_AUTH_TOKEN` yields the `tools` array
byte-for-byte, at no cost and with no real API call. Recipe:
[kb](kb/mcp/protocol.md#what-2kb-each-means--measured-2026-08-18--claude-code-21234).

**What is still open is only the direction of travel.** Every figure above is a
client-version fact with nothing watching it — row 92 of the
[re-verification index](kb/re-verification.md) is the only thing that will bring
it back up, and only when somebody works through that table. A release that
introduced a per-tool bucket would break `browserai_init` on day one and report
nothing.

## Added 2026-08-19, from the maintainer

### 11. Whether `browserai_destroy` should fail when survivors remain — **ANSWERED 2026-08-19: yes. His call, over my recommendation and over my stated objection**

**The primer, for whoever reads this without the investigation.** `browserai_destroy`
deletes a session directory. Windows will not unlink a file a browser is still
mapping, and the release lags the process by however long the kernel takes, so the
tool has always had two answers: everything went, or *"BUT N item(s) could not be
removed"* followed by the list. Until today **both were `isError: false`**. The
question is what the second one should be.

**What I recommended, and it was not this.** Leave it at `isError: false`. A
destroy that removed a nine-thousand-file profile and could not remove eleven
locked files has done what it was asked; the session record is gone, the index has
forgotten it, and what is left is residue. Failing the call throws the report of
the nine thousand into a channel a model reads as *this did not work*.

**My objection, stated plainly at the time: an error invites a retry, and the
retry is worse than the truth.** A model that reads `isError: true` calls the tool
again. There is no session at that directory any more, so `browserai_destroy`
refuses — *"has no `browserai.json`, so it is not a BrowserAI session"* — and the model
now has a refusal that reads like the directory was never BrowserAI's at all.
That is a worse final state than the honest partial success it replaced.

**The decision: `isError: true`, with a refinement that answers the objection.**
Put the details in the error so the model can adjust. The survivor arm now says,
after the tally and the listing:

- the session **is** destroyed — its record is gone and the index has forgotten
  it, so what is listed is residue on disk rather than a session;
- **do not call `browserai_destroy` again** — there is no session there for it to
  destroy, and it will refuse;
- what to do instead — wait for whatever still holds those files to exit and then
  delete them, or leave them, because nothing in BrowserAI reads them again.

**Why that answers the objection rather than merely softening it.** The objection
was never *an error is inaccurate*; a call that did not entirely do the thing it
is named for is not a success, and a model scanning result shapes should be able
to tell it from one that did. The objection was that the error's **only**
actionable reading is *retry*. Naming the one action that will not work, and the
one that will, removes that reading — so the failure mode the objection predicted
needs a model to act against an instruction rather than to follow the default. The
report the old defence wanted to protect is still there in full: the summary, the
tally, the listing and the truncation notice are unchanged, and the roll-up
warning arm is untouched.

**What implements it.** `SessionManager.DestroyAsync`'s survivor arm. Held by
`SessionDestroyTests.ADestroyThatCannotRemoveEverythingNamesWhatSurvivedAndSaysHowMany`,
which asserts the flag and each of the three sentences, and by
`DestroyAnswer.AccountsForWhatItLeftAsync`, which asserts that `isError` agrees
with the answer's own text in **both** directions — so a survivor arm reporting
success and a clean destroy reporting failure are equally red.
`SessionToolTests.DestroyRefusesDocumentsAndSurvivesAFileItCannotRemove` holds the
same thing through the published binary.

**How to reverse it.** One `IsError:` argument in `SessionManager.DestroyAsync`,
and the assertions that pin it: two in `SessionDestroyTests`, two in
`SessionToolTests`, one in `DestroyAnswer`. The three sentences would stay useful
either way. Nothing else in the product branches on it, and `FirefoxSessionTests`
no longer asserts the flag directly at all — it goes through `DestroyAnswer`,
which reads the contract rather than a literal.

### 12. The CsWin32 metadata licence — **MOOT 2026-08-20, and the entry stays**

⚠️ **SETTLED PERMANENTLY, 2026-08-20, at the maintainer's decision: no generated
code will ever ship.** That is direction **(a)** below, taken not as a *for now*
but as a standing rule — CsWin32 is a test-only tool and nothing it emits enters
`src/`, an artifact, or a published repository, at any future version. **So the
question this entry gathers text for is no longer open; it is unreachable.** The
licence contradiction it documents is real and unresolved, and it never has to be
resolved, because the only act that would engage it has been ruled out.

**The `PackageReference` stays exactly as it is, and this is what it is for.**
`Microsoft.Windows.CsWin32` is a direct reference of
[`tests/BrowserAI.Tests/BrowserAI.Tests.csproj`](tests/BrowserAI.Tests/BrowserAI.Tests.csproj)
with `PrivateAssets="all"`, which is what keeps it out of the product's closure:
it is a build-time analyzer for the **test project alone**, and it exists to be a
*layout oracle*. The product hand-writes its seven interop structs, and CsWin32
generates the same declarations from Microsoft's own metadata so the suite can
assert the hand-written sizes and field offsets against the generated ones rather
than against a comment. Deleting the reference would not remove a dependency from
anything shipped — nothing shipped has it — it would remove the only independent
check that the hand-written interop matches what Windows actually expects.

**Three consequences worth naming**, because *moot* is not the same as *gone*:

1. **`PrivateAssets="all"` is now load-bearing rather than tidy.**
   `ForbiddenDependencyTests` and the notice tests already read the package files
   and `packages.lock.json`; the rule this decision creates is that CsWin32 may
   never appear in `src/BrowserAI/`, and the first commit that puts it there is
   what re-opens everything below.
2. **The three prerelease transitive packages stay**, and they remain the only
   prerelease versions anywhere in the repository. They are build-time only, so
   the *GA is a hard floor* rule is not violated in the artifact — that trade is
   [its own TODO item](TODO.md) and this decision does not change it.
3. **Nothing below is deleted.** Everything after this note is the primary-source
   text, gathered 2026-08-19, and it stays as the record of *why nobody needs to
   answer it* — which is a different and more useful thing than an entry that was
   quietly dropped once it stopped mattering. A future maintainer who wants
   generated code in `src/` needs this text, and needs to reverse the decision
   above first.

**What re-opens it:** any proposal to ship generated code — vendored, committed,
emitted at build time into the product, or published in a public repository. At
that point direction **(b)**, putting questions 1–5 to a lawyer, becomes the next
step and this entry is already the brief.

---

#### The text, gathered 2026-08-19, and the question it leaves for a lawyer

**This entry contains no legal opinion and must not acquire one.** It was
commissioned to replace *"nobody has read the terms"* with the terms themselves, so
that the open question is a legal question rather than a research task. Everything
below is quoted from a primary source, with the URL and the date it was fetched.
**Nothing here is a conclusion, and the recommendation at the end is a recommendation
about who to ask, not about what the answer is.**

#### What is actually referenced, and at which versions

Read from [`tests/BrowserAI.Tests/packages.lock.json`](tests/BrowserAI.Tests/packages.lock.json)
on 2026-08-19. `Microsoft.Windows.CsWin32` is a **direct** reference of the test
project with `PrivateAssets="all"`; the other three arrive only through it.

| Package | Resolved | How its licence is declared in its own `.nuspec` |
|---|---|---|
| `Microsoft.Windows.CsWin32` — *the generator* | `0.3.298` | `<license type="expression">MIT</license>`, `licenseUrl https://licenses.nuget.org/MIT`, `requireLicenseAcceptance=false`, `developmentDependency=true` |
| `Microsoft.Windows.SDK.Win32Metadata` — *the metadata* | `70.0.11-preview` | `<license type="file">sdk_license.txt</license>`, `licenseUrl https://aka.ms/deprecateLicenseUrl`, **`requireLicenseAcceptance=true`** |
| `Microsoft.Windows.WDK.Win32Metadata` | `0.13.25-experimental` | `<license type="file">sdk_license.txt</license>`, **`requireLicenseAcceptance=true`** |
| `Microsoft.Windows.SDK.Win32Docs` — *the doc comments* | `0.1.42-alpha` | **no `<license>` element at all**; only the deprecated `licenseUrl https://aka.ms/WinSDKLicenseURL`, **`requireLicenseAcceptance=true`** |

**The two `sdk_license.txt` files are byte-identical** — SHA-256
`0e97876eaa1fc79558e0d51dc0bee286d36dca8e95f7876259ffdf947396bca1` for both, compared
2026-08-19 out of the local package cache. `https://aka.ms/WinSDKLicenseURL` resolves
(HTTP 301, checked 2026-08-19) to
`https://download.microsoft.com/download/0/F/F/0FF2B061-47DD-4F55-89B6-FD1D8C44F14D/sdk_license.rtf`,
which carries the same title and the same `EULAID`. So **all three metadata packages
are governed by one document**, and nuget.org serves that same document at
`https://www.nuget.org/packages/Microsoft.Windows.SDK.Win32Metadata/70.0.11-preview/License`
(fetched 2026-08-19).

That document is titled, verbatim:

> MICROSOFT SOFTWARE LICENSE TERMS
> MICROSOFT WINDOWS SOFTWARE DEVELOPMENT KIT (SDK) FOR WINDOWS 10

and ends `EULAID:WIN10SDK.RTM.AUG_2018_en-US`.

*(The existing note in [`Directory.Packages.props`](Directory.Packages.props) said all
three carry **no SPDX licence expression at all**, checked 2026-08-17. **Verified
2026-08-19: correct.** Two use `<license type="file">` and one uses only the
deprecated `licenseUrl`; none carries an expression.)*

#### The contradiction, and it is the whole of the question

`Microsoft.Windows.SDK.Win32Metadata`'s **package** ships the Windows SDK EULA above
as its licence file. Its **source repository's** `README.md` says the opposite about
the very file CsWin32 reads. Fetched verbatim from
`https://raw.githubusercontent.com/microsoft/win32metadata/main/README.md` on
2026-08-19:

> \# Licensing
>
> \## MIT
> \* All metadata assemblies (e.g. `Windows.Win32.winmd`)
> \* All tooling in this repository and in the [Microsoft.Windows.SDK.Win32Metadata NuGet package](https://www.nuget.org/packages/Microsoft.Windows.SDK.Win32Metadata/)
>
> \## Windows SDK
> \* All Windows headers (e.g. RecompiledIdlHeaders) and Interface Definition Language (IDL) files in this repository and in the aforementioned NuGet package.

And that repository's own `LICENSE` file
(`https://raw.githubusercontent.com/microsoft/win32metadata/main/LICENSE`, fetched
2026-08-19) is the MIT licence, opening with this disclaimer above the grant:

> DISCLAIMER: This repository does not change the licenses of the original SDK headers used to generate the metadata. Any such artifacts checked into this repository support metadata production only and do not imply a change of their original licenses.

**`Windows.Win32.winmd` is the only file in that package CsWin32 reads.** The README
says it is MIT. The package that delivers it declares the Windows SDK EULA over the
whole package and requires licence acceptance to install it. Both statements are
Microsoft's, both are current, and they are not the same statement.

#### What the SDK terms say, where they touch redistributing generated code

Quoted verbatim from `sdk_license.txt` as shipped inside
`Microsoft.Windows.SDK.Win32Metadata` 70.0.11-preview, read 2026-08-19. Emphasis is
not in the original; nothing is elided inside a quoted sentence.

**The use grant, §1.a:**

> You may install and use any number of copies of the software on your devices to design, develop and test your programs that run on a Microsoft operating system. Further, you may install, use and/or deploy via a network management system or as part of a desktop image, any number of copies of the software on computer devices within your internal corporate network to design, develop and test your programs that run on a Microsoft operating system. Each copy must be complete, including all copyright and trademark notices. You must require end users to agree to terms that protect the software as much as these license terms.

**What may be redistributed at all, §2.a.i — and the definition is a closed list:**

> a. Distributable Code. The software contains code that you are permitted to distribute in programs you develop if you comply with the terms below.
> i. Right to Use and Distribute. The code and test files listed below are "Distributable Code".
> • REDIST.TXT Files. You may copy and distribute the object code form of code listed in REDIST.TXT files plus the files listed on the REDIST.TXT list located at http://go.microsoft.com/fwlink/?LinkId=524842.

**The conditions on any such distribution, §2.a.ii:**

> ii. Distribution Requirements. For any Distributable Code you distribute, you must
> • Add significant primary functionality to it in your programs;
> • For any Distributable Code having a filename extension of .lib, distribute only the results of running such Distributable Code through a linker with your program;
> • Distribute Distributable Code included in a setup program only as part of that setup program without modification;
> • Require distributors and external end users to agree to terms that protect it at least as much as this agreement;
> […]
> • Display your valid copyright notice on your programs; and
> • Indemnify, defend, and hold harmless Microsoft from any claims, including attorneys' fees, related to the distribution or use of your programs.

**The restrictions, §2.a.iii — quoted with its typographical errors intact, because
this is the clause that bears on an MIT-converting licence:**

> iii. Distribution Restrictions. You may not
> • Alter any copyright, trademark or patent notice in the Distributable Code;
> […]
> • Distribute Distributable Code to run on a platform other than the Microsoft operating system platform;
> […]
> • Modified or distribute the source code of any Distributable Code so that any part of it becomes subject to an Excluded License. And Excluded License is on that requir3es, as a condition of use, modification or distribution, that
> • The code be disclosed or distributed in source code form; or
> • Others have the right to modify it.

**The scope clause, §7:**

> The software is licensed, not sold. This agreement only gives you some rights to use the software. Microsoft reserves all other rights. […] You may not
> […]
> • publish the software for others to copy;
> • rent, lease or lend the software;
> • transfer the software or this agreement to any third party; or
> • use the software for commercial software hosting services.

**And §6, which is the only clause that reaches the doc comments:**

> DOCUMENTATION. Any person that has valid access to your computer or internal network may copy and use the documentation for your internal, reference purposes.

#### What the text does and does not say about generated code

Stated as observations about the text, not as conclusions about their effect.

- **The document never uses the words "generate", "generated" or "projection".** It
  is written about *the software* — copies, installs, object code, `.lib` files,
  setup programs — and a source generator's output is none of those things by name.
- **"Distributable Code" is a closed list.** It is *"the code and test files listed
  below"*, and what is listed below is the contents of `REDIST.TXT`. `Windows.Win32.winmd`
  is not object code and is not in a `REDIST.TXT` list; nor is the C# CsWin32 emits
  from reading it.
- **So the emitted C# is not addressed by the redistribution clause in either
  direction.** It is not granted as Distributable Code and it is not named as
  forbidden. That silence is the question — not a permission and not a prohibition.
- **Two clauses would bite if it *were* covered**, and they are the ones to put in
  front of a lawyer first. §2.a.iii forbids distributing Distributable Code source
  *"so that any part of it becomes subject to an Excluded License"*, defined as one
  requiring *"as a condition of use, modification or distribution"* that *"others have
  the right to modify it"*. **BrowserAI ships under `LicenseRef-BrowserAI-FSL-1.1-MIT-5yr`,
  which converts to MIT after five years**, and MIT grants the right to modify. And §7
  forbids *"publish the software for others to copy"*, which is a plain description of
  a public git repository.
- **The doc-comment surface is separate and narrower.** `SDK.Win32Docs` supplies the
  `<summary>` text CsWin32 folds into the generated code. §6 permits copying
  documentation *"for your internal, reference purposes"* — a phrase that does not
  obviously describe a public repository, and the package carries no `<license>`
  element at all, only a deprecated URL.
- **Upstream does not resolve it.** Neither `microsoft/CsWin32`'s `README.md` nor
  `microsoft/win32metadata`'s says anything about the licence of emitted code
  (both fetched 2026-08-19), and a search of the CsWin32 issue tracker for a licence
  discussion returned none.

#### The question a lawyer needs to answer

Ordered so that a *no* to the first ends the enquiry.

1. **Does `Windows.Win32.winmd` reach us under MIT or under the Windows SDK terms?**
   The repository README says MIT for the metadata assemblies; the NuGet package that
   delivers that same file declares the SDK EULA over the package and demands licence
   acceptance. Which governs, and does the README's disclaimer about *original SDK
   headers* pull the winmd back under the SDK terms because the winmd is derived from
   those headers?
2. **If the SDK terms govern — is C# emitted by a generator that read the winmd a
   derivative work of "the software" at all**, given that "Distributable Code" is a
   closed `REDIST.TXT` list that does not include it, and given that what is emitted
   is API declarations: names, struct layouts and signatures?
3. **If it is — does publishing it under a licence that becomes MIT in five years
   engage §2.a.iii's Excluded License clause**, whose trigger is a licence requiring
   *"as a condition of use, modification or distribution"* that *"others have the
   right to modify it"*?
4. **Does §7's "publish the software for others to copy" reach generated declarations
   in a public repository**, separately from the Distributable Code clause?
5. **Do the doc comments need separate treatment**, given §6's *"internal, reference
   purposes"* and `SDK.Win32Docs` carrying no licence element at all?

#### Directions, and none of them is a legal opinion

**(a) Change nothing and keep the gate.** CsWin32 stays test-only with
`PrivateAssets="all"`; nothing it emits ships; questions 2 through 5 stay hypothetical
and only question 1 would ever need answering. Costs nothing, decides nothing.

**(b) Put questions 1–5 to a lawyer, then decide.** This entry is the brief. The cost
is one consultation and the delay; the return is that the interop-generator decision
stops being permanently deferred.

**(c) Emit and vendor once, and ship only the vendored file.** Run the generator,
commit its output, drop the `PackageReference`. Removes the transitive prerelease
packages and makes the shipped artifact auditable — **and changes nothing at all about
the legal question**, since it is the redistribution of the generated text that is
being asked about.

**(d) Hand-write the interop and never revisit this.** What the product does today for
its seven structs. The layout oracle stays test-only, which is exactly (a) with the
door shut.

**Recommendation: (a) now, and (b) only when something actually wants CsWin32 in
`src/`.** The gate is already holding — nothing generated ships, so the exposure is
zero — and a legal question answered years before it is acted on will need re-asking
anyway. **What has changed is that (b) is now a one-hour task rather than a research
project**, because the text is above and the questions are written. What must not
happen is the third option nobody proposed: reading the quotations here as an answer.
They are not one, and this entry stays open until a lawyer closes it.

⚠️ **Superseded 2026-08-20 (previously "this entry stays open until a lawyer closes
it").** The maintainer took **(a)** as permanent rather than as *now*: no generated
code will ever ship. The entry does not stay open — it becomes moot, which closes
it without answering it. See the note under the heading above; the recommendation
is kept verbatim because it is what was recommended and the decision went further
than it did.

---

## Added 2026-08-20, from the shared-root measurement

### 12. What BrowserAI should do when two users share one install root — **ANSWERED 2026-08-20: (a)**

⚠️ **Answered, in the maintainer's words: _"L1 a"_.** Direction **(a)** is implemented —
`Hosting/InstallRootScope.cs` refuses at startup when the app root is not inside the
current user's profile, before the stray sweep, the live marker, the instance
directory or any session is created, and the refusal names the root it found, why a
shared root is unsafe and that clearing `BROWSERAI_ROOT` restores the per-user
default. **The recommendation below is kept verbatim because it is what was
recommended**, and it recommended C-then-B with A as his alone; he took A. What A
cost is exactly what the table said it would: `D:\Tools\BrowserAI` is now refused
for nothing, and the honest predicate — reading the marker directory's DACL for a
group ACE — is still not implemented, so [the hazard row](HAZARDS.md#hazard-index)
is **narrowed rather than closed** and says so.

**The primer below is unchanged**, and everything in it is still true of a root the
refusal does not reach.

---


**The primer, for whoever reads this without the investigation.**
`%LocalAppData%` gives every Windows user their own BrowserAI state — browsers,
session index, logs, and the `live\` directory each running process announces
itself in. **Two things defeat that**: the `BROWSERAI_ROOT` environment variable
and the installer's install-to flag. Point either at a shared location and two
users share one browsers directory, one index and one marker directory. Nothing
in the tree recorded what happens then. This entry is the measurement; the
decision is yours and **nothing has been implemented on the strength of it**.

**What was measured, in full, is
[in the knowledge base](kb/windows/detection.md#two-users-and-one-install-root--what-spans-users-and-what-does-not--measured-2026-08-20).**
The short form:

- **The file locks span users.** A share mode is enforced by the kernel against
  handles and is indifferent to which token opened them, so `browserai.json`,
  `reinstall.lock` and every `.live` marker stay honestly held-or-free across
  users. Under a shared root at a volume root, a second user can additionally
  enumerate, read, write and **delete** them — `Authenticated Users` inherits
  `0x1301BF`, which carries `DELETE`. The marker reclaim is nonetheless safe
  there by construction: it acts only on *not held*, and a cross-user marker
  answers either *sharing violation* or *could not open*.
- **The `Global\` mutexes do not span users.** The DACL the kernel puts on one
  names LOCAL SYSTEM, the creating **logon session** and the creating **user**,
  and no group at all. Whichever user creates a name first owns it; the other's
  `MachineMutex.Create` is refused.
- **The consequence is silent, and it is the finding.** A process that cannot
  take the gate cannot join the live set, so it creates no marker and is
  **invisible** to the other user's census. That census then answers *Alone*, and
  an apply runs `force_stop_package`, which kills every process under the install
  root — the other user's BrowserAI and its browsers included. Three of the four
  consumers degrade to a log line; only `SessionLock.TryAcquire` reaches a caller.

⚠️ **What could not be measured, said plainly, because it bounds the answer.** No
second user account and no second logon session could be created on this machine:
the token is a filtered administrator token, `New-LocalUser` is denied, every
other local account is disabled, and a loopback network logon fails Negotiate. So
**the cross-user refusal is inferred** from a token holding no ACE on such an
object — the same code path with the same variable set the same way — and not
observed between two users. Section 6 of the article lists the rest of what is
still open, including what an elevated administrator peer can reach and what the
installer's own flag actually writes.

**Five directions.**

| # | Direction | What it costs | What it buys |
|---|---|---|---|
| **A** | **Refuse a shared root at startup** — detect that the marker directory's DACL grants a group, or that the root is outside `%LocalAppData%`, and refuse to serve | A configuration somebody deliberately chose stops working, and the detection is a heuristic: *outside `%LocalAppData%`* is not the same predicate as *shared*, and a single-user install at `D:\Tools\BrowserAI` would be refused for nothing | The dangerous case cannot arise. This is your own stated follow-up |
| **B** | **Refuse only the update apply, and keep serving** — treat a root this process could not take the gate for as permanently *not alone* | Nothing, and it is a two-line change: `LiveInstances.Join` already returns `null`, and `UpdateService` already treats a null as *do not apply* | Removes exactly the failure measured — the apply that kills a peer — and leaves every other shared-root behaviour alone. **It is already the behaviour**; what is missing is that nobody is told |
| **C** | **Say so, loudly, and change no behaviour** — a startup warning naming the root, the mutex and what is degraded | A log line nobody reads. It does not stop anything | Cheapest honest option, and the one that makes the arrangement diagnosable instead of invisible. Composable with every other row |
| **D** | **Give the objects a DACL that spans users** — create the mutexes with an explicit `Authenticated Users` ACE | A real security decision: any authenticated user could then hold a gate that stalls another user's session opening, and it is a denial-of-service surface that does not exist today. Needs `MutexAcl.Create` and a security descriptor in `MachineMutex` | Makes the arrangement actually work rather than merely fail loudly |
| **E** | **Key the marker set on the user as well as the root** — one `live\<sid>\` subdirectory per user | Two users then genuinely cannot see each other, so an apply by one still kills the other's processes. **This is worse than doing nothing** and is listed because it is the obvious-looking fix | Nothing. Named so it is not proposed later |

**Recommendation: C now, B stated explicitly, and A only if you want the
configuration closed rather than diagnosed.** B is what the product already does
and it closes the measured failure, so the gap is not behaviour — it is that
three of the four consumers fail into a log file and nothing tells the operator
the census has stopped meaning anything. C is what turns that from invisible into
diagnosable, and it costs one startup line. **A is a real option and it is yours
alone**, because it takes away a configuration somebody chose on purpose, and
because the predicate that detects *shared* honestly — reading the marker
directory's DACL for a group ACE — is a different and larger change than
comparing the root against `%LocalAppData%`. **D needs a security conversation
before it needs code.** E is a trap.

---

## Added 2026-08-20, from the session-modes deletion

### 13. The ten newly-granted tools — **DECIDED BY THE MAINTAINER, over my recommendation**

⚠️ **Taken, in the maintainer's words: _"Every capability is granted to every
session — the full union, including `network`, `pdf` and `testing`, which have
never been granted before."_** It is implemented:
`BrowserConfiguration.GrantedCapabilities` names every capability upstream
declares that carries a tool, and the advertised surface went from **58 tools to
68**.

**What I recommended instead**, recorded because it is what a reader would
otherwise assume was never considered: grant `network`, `pdf` and `testing`
**behind the same deletion but as a separate, later decision** — delete the modes
now, keep the capability set at the union the `persistent` mode already had, and
weigh the three new capabilities on their own merits with a measurement of each.
The argument was that deleting modes and widening the surface are two changes
with one blast radius, and that ten tools arriving as a *consequence* of a
simplification is exactly the shape nobody reviews.

**Why he is right and I was wrong about the framing.** The session-mode deletion
rests on one finding: a capability withheld from a session was never a boundary
against the caller, who owns the session directory. That finding does not
distinguish `storage` from `network` — it applies to every capability equally, so
holding three of them back would have been a boundary defended by nothing but
inertia, and the honest version of the change is the full union. **Splitting it
would have produced a build in which the stated reason and the actual behaviour
disagreed.**

**What my recommendation bought that this does not, stated so it is not lost.**
Nothing about the ten tools was measured before they were granted. `browser_route`
in particular changes what a page *is* rather than what the agent sees, and the
one measurement that would matter — what a mocked response does to a human
watching a headed window — was reasoned about rather than run. What went in
instead of a measurement is a **warning in the server `instructions`**, which is
BrowserAI's own string and therefore the only channel available: upstream
descriptions pass through byte for byte, and [the rewrite path that could have
appended to `browser_route`'s description was deleted on
2026-08-18](DECISIONS.md#licence-release-policy-and-the-tool-surface).

**How to reverse it.** One list, in one file: remove a capability from
`BrowserConfiguration.GrantedCapabilities`. Three tests fail and each names what
it expected — `ModelSurfaceTests.EverySessionGetsEveryCapabilityAndTheNewlyGrantedTenAreInTheSurface`
on the capability by name,
`SessionPolicyTests.ASessionPermitsEveryToolItAdvertisesAndTheOneThatWouldHangIsNotAdvertised`
on the count, and `VerticalSliceTests` off the wire. **Reversing it is not free
for a caller**, though: an agent that has learned the tools exist will keep
calling them, and upstream's answer to a tool its child was not launched with is
*unknown tool* rather than anything a model can act on.

⚠️ **`browser_run_code_unsafe` is not one of the ten and this entry must not be
read as though it were.** It is `core`; it has been reachable in every session
this product has ever opened, `headless` included, and it reaches the cookie jar
([measured 2026-08-14](DECISIONS.md#licence-release-policy-and-the-tool-surface)).
Nothing about the grant changed its availability.

### 14. The one time-ordered log lives inside `browserai.json` — **DECIDED BY THE MAINTAINER, over my recommendation**

> ⚠️ **REVERSED 2026-08-26, by the same maintainer, and the whole section below
> is kept as the record of what was decided and why.** *The heading is left
> exactly as written because [`CHANGELOG.md`](CHANGELOG.md) links to it from a
> released section that may not be rewritten; read it as the question's name, not
> as a live claim.* **`browserai.json` no longer exists.** A session directory now
> carries two files — `browserai.lock`, the guard, written once at acquisition
> and never again; and `browserai.data`, a SQLite store in WAL mode holding every
> statement the session has made about itself and every call it has logged. Four
> of this section's answers went with it, and each is corrected in place below:
> **the one-file answer**, **the argument-recording answer**, **the caps answer**
> and **the refusals-go-to-`browserai.log` answer**.
>
> **What survived is the reasoning that decided it, and that is worth saying
> plainly.** The one-file argument — *a session directory is moved and copied by
> people, and a second file is a second thing that can be copied without the
> first* — was right, and it is what the new design is built to keep: the record
> and the log are still **one** file, `browserai.data`, and it is still the file
> a copy carries. What changed is that the *guard* left it. The guard was never
> part of that argument; it was in the same file only because the record happened
> to be the thing being held open, and that accident is what made every append a
> whole-file durable rewrite plus a rename — with the ownership handle dropped and
> retaken each time. **A half-copied session was the risk this section weighed;
> a periodically-unowned live session was the one it did not.**

⚠️ **Taken, in the maintainer's words: _"The log lives INSIDE `browserai.json`,
under the same session-long lock. This is his decision over my recommendation of
a sibling append-only file; build it as decided."_** It was implemented:
`LockRecord.Log` was one ordered array carrying `browserai_init`'s purpose, every
purpose change, and every browser call the session forwarded, and the record
moved to **schema 4**. ⚠️ *Corrected 2026-08-26 (previously "It is implemented:
`LockRecord.Log` **is** one ordered array …").* `LockRecord` is deleted; the log
is the `log` table in `browserai.data`, the statements are the `statements`
table, and `PRAGMA user_version` is 1.

**What I recommended instead:** a sibling append-only file — `browserai-log.jsonl`
beside `browserai.json` — one line per entry, opened `FileShare.Read` for the life
of the session and appended to.

**The one thing that decided it, and it is not the cost.** A session directory is
moved and copied by people, and `browserai.json` is [already the thing that makes
a copy self-describing](ARCHITECTURE.md#sessions) — every field is an ordered
list of timestamped statements, so a resumed copy is *told* where it has been.
A second file is a second thing that can be copied without the first, and the
failure is silent in the worst direction: a session whose record says it was
created for one thing and whose log describes another, with nothing to say which
half is the stranger. **One file cannot be half-copied.** That is a stronger
property than anything the sibling bought, and my recommendation did not weigh it.

**What it costs, measured against the alternative rather than in the abstract.**
Every forwarded browser call now rewrites the **whole record**:
`SessionLock.Rewrite` closes the handle, writes a temp file `WriteThrough`,
`Flush(flushToDisk: true)`, renames it over `browserai.json`, and re-opens. An
append to a sibling would have been an `O(entry)` write with no rename and no
re-open. **The record is capped at 250 entries and roughly 400 KB**, so the write
does not grow without bound — but it is a full-file durable write per call, and a
session that makes two hundred calls pays it two hundred times. **Nothing here
measured it**; the cost is stated because it is real, not because it was found to
be a problem.

> ⚠️ **It was measured afterwards, and the cost was not the interesting part.**
> *Corrected 2026-08-26 (previously "Nothing here measured it").* A whole-record
> durable rewrite was **3.94 ms at 1 KB, 10.72 ms at 200 KB and 13.62 ms at
> 400 KB** — real and, as this section guessed, affordable. What the paragraph
> above did not name is what the rewrite did to the **guard**: closing the handle
> and taking it back left the directory demonstrably unowned for a few
> milliseconds *per forwarded call*, so a peer's `browserai_list` printed
> *in use: no* about a session another agent was driving, and a peer's transient
> probe handle could refuse the writer's own re-open — which it did, in CI run
> 32203064556 attempt 1, leaving two processes' holder statements in one record.
> **Both windows are gone**: the guard is written once and the store is appended
> to in place.
>
> ⚠️ **And the caps are gone, at the maintainer's decision** — *previously "The
> record is capped at 250 entries and roughly 400 KB"*. There is **no cap on
> anything**: not on the number of rows, not on a value's length, not on a
> `purpose`. `SqliteStorageTests.NothingInTheStoreIsCappedByLengthOrByCount`
> holds it. The cap existed to bound a rewrite that no longer happens, and it had
> a defect of its own on the way out: `resume` concatenated each new purpose onto
> the old one and the result was silently truncated at 2,000 characters. A
> `purpose` is a row per change now, so the concatenation and its data loss died
> together.

**The second cost, and it is the one to watch.** A call whose entry cannot be
written is **refused**, and the browser never sees it —
`SessionErrors.SessionLogCouldNotBeWritten`. That is deliberate: the value of one
time-ordered log is that reading it back tells you what the session did, and a
gap nobody is told about is worse than a refusal somebody can act on. With a
sibling file the same failure could have been a note on an otherwise successful
answer, because the record's own integrity would not have been in question.
**This is the sharpest consequence of the decision and it is not reversible
without reversing the decision.**

**How to reverse it.** `LockRecord.Log`, `LockRecord.AppendLog`,
`SessionLock.Append` and `SessionLockRequest.Entry` are the whole of it, plus one
`try`/`catch` in `BrowserProxy` and a schema bump. `SessionLogTests` holds the
behaviour and would move with it. **What would not survive the move is the copy
property above**, and whoever reverses this should say what they are doing about
it rather than discovering it later.

> ⚠️ **Reversed 2026-08-26, and the copy property was kept rather than traded
> away.** The paragraph above is the right question to have asked and it named
> the wrong file set. What moved out of the record was the **guard**, not the
> log: `browserai.data` still carries the statements and the log together, so a
> copied directory still describes itself, and what a copy is now missing is only
> the ownership handle of a process that has nothing to do with it. The reversal
> is `Storage/SessionStore.cs` and `Storage/LockFile.cs`; `SessionLogTests` moved
> with it and did not shrink.

**What I chose about argument values, since nobody instructed it.** The entry
records **every argument name, always** — a reader must be able to see that a
password field was filled even when the value is not there. The value is then:
*withheld entirely for `value` and `text`*, the two scalar parameters upstream
uses for something a person typed or a server set (`browser_cookie_set`,
`browser_localstorage_set`, `browser_sessionstorage_set`, `browser_type`),
recorded as `<withheld, N characters>` so the length survives; *summarised as a
shape for an object or an array*, `<object, N keys>` / `<array, N items>`, which
is what `browser_fill_form`'s `fields` and `browser_route`'s `headers` become;
and *cut at 200 characters with a count of what was dropped* for everything else,
which is what turns a `browser_evaluate` body from a transcript into a summary.
**The withheld list is asserted against the golden snapshot**, so an upstream
rename is a red build rather than a policy that quietly stopped matching.

> ⚠️ **REVERSED 2026-08-26: no argument is recorded at all, and `LoggedArgument`
> is deleted.** *(Previously the whole paragraph above.)* A log row is
> `(at, tool, why, outcome, settled_at, failure)` — **the caller's `why`, in its
> own words, is what the row says the call was for**, and the tool name is
> recorded verbatim, unknown and refused names included. The withheld list, the
> shape summaries, the 200-character cut and the golden-snapshot assertion behind
> them are all gone.
>
> **The reason is the doctrine rather than the cost.** *Nothing between the two
> servers except the session system and the reason system*: an argument summary
> is BrowserAI reading a caller's request and writing its own account of it, and
> a `<object, 3 keys>` in a log is a fact about a serialiser rather than about
> what the agent did. The `why` is a better answer to the same question, it is
> mandatory on every call that names a session, and nobody has to maintain a list
> of upstream parameter names to keep it honest. **What is lost is named rather
> than glossed**: a reader can no longer see *which* selector was typed into, or
> that a password field was filled at all, from the log alone. F3 — a review
> finding about argument recording — is moot for the same reason.

⚠️ **It is not a redaction boundary and must not be described as one.** The log
sits inside the session directory, and so does the browser profile whose cookie
database holds the same credentials — [measured
2026-08-18](kb/chromium/profiles.md#chromiums-cookie-store-and-what-it-takes-to-read-one--measured-2026-08-18),
recoverable by any process running as the same user. What withholding buys is
that a password is not written into the one file a model is *invited to read
back*. It buys nothing against anything that can read the directory at all.

> ⚠️ **Still true and now for a simpler reason (2026-08-26).** No value reaches
> the record, so there is nothing to withhold; the sentence above is kept because
> the *claim it refuses to make* is the one that would be made again by whoever
> proposes recording arguments next. **Guarding against a hostile caller is an
> explicit non-goal of this product** — see
> [the charter](DECISIONS.md#what-browserai-does-not-defend-against) — and this
> paragraph is where that was first argued in this repository.

---

### 14a. The refusals live in `browserai.log` beside the record — **REVERSED 2026-08-26**

**The primer, for whoever reads this without §14.** The design §14 settled
recorded what a session *did*: a row went in immediately before a call was
forwarded, and a call BrowserAI **refused** left no row at all, because a refusal
is not something the session did. The refusals went to `browserai.log`, a
per-session text log beside the record, and that split is what made
`SessionLogCouldNotBeWritten` — the refusal of a call whose row could not be
written — the sharpest consequence §14 names.

⚠️ **Both halves of that are gone.** `browserai.log` does not exist: everything
it carried is on stderr, which the session's logging stack already wrote to at
every level, and the per-session file was a second copy nobody read. And **a
refused call is now a row** — `outcome = failed`, carrying the refusal itself —
so the record answers *the agent reached for a tool this build will not forward*,
which nothing else in the directory could say once the log file went.
`SessionLogTests.ARefusedCallIsRecordedAsAFailedRowCarryingTheRefusal` holds it,
and it is an **inverted** test rather than a new one: the arm it replaced
asserted that a refusal left no row.

**What did not change**, and it is the half §14 called the sharpest consequence:
**a call whose row cannot be written is still refused rather than forwarded.**
`SessionLock.Append` throws rather than swallowing, and `SessionErrors`
still carries the row. The value of one time-ordered record is that reading it
back tells you what the session did, and a gap nobody is told about is worse than
a refusal somebody can act on.

**The outcome is three-valued now, which §14's boolean was not.** A row is
written `in-flight` **before** the call is forwarded — the property the
write-before ordering always existed for, so a call that never returns still left
a record — and updated to `successful` or `failed` on settle, with `settled_at`,
from which the duration is derivable. `browserai_catch_up` renders a stale
`in-flight` as *"no answer was recorded"*. **Failure payloads only:** the child's
error bytes, its JSON-RPC error or the transport exception go into `failure`, and
a successful call stores no payload at all.

---

## Added 2026-08-20, from the six-commit run

### 15. `AReclaimWhosePeerHoldsTheGateSkipsAtOnceAndRemovesNothing` measured 5.2 s for a zero-wait acquire — **CLOSED 2026-08-23: the clock is gone and the gate records the wait it was handed**

**The primer, for whoever reads this without the run.** That test proves the
live-marker reclaim *skips* rather than *waits* when a peer holds the gate. Both
behaviours return `Skipped`, so the only thing that can tell them apart is a
clock — and the test bounds `Stopwatch.Elapsed` by the product's own
`LockScopes.LiveInstanceGate`, five seconds. Its comment says, in as many words:
*"Nothing about a machine's load can approach it, because the work bounded is one
zero-timeout acquire."*

**That sentence is measurably false on this machine.** One full-suite run out of
seven on 2026-08-20 reported the test at **5 s 248 ms** and failed the bound. It
cannot be the acquire: `LiveInstances.ReclaimStaleMarkers` acquires with
`LockScopes.NeverWaits`, so the call returns without blocking whatever the peer
is doing. What the stopwatch measured is the **thread being descheduled** —
`Stopwatch` is wall clock, the suite runs 573 cases unbounded in parallel, and
the same suite's own duration varied between **1 m 56 s and 3 m 34 s** across six
consecutive runs from external load alone. **A wall-clock bound of five seconds
is inside that noise.** Six other full runs on the same tree, three from
PowerShell and three from Git Bash, were green.

⚠️ **It was not introduced by this work, and that was checked rather than
assumed.** Nothing changed can make a zero-wait acquire block. The obvious
counter-hypothesis is load: the one time-ordered log now writes the **whole
session record durably — `WriteThrough`, flush, atomic rename, re-open — on every
forwarded browser call**, and the suite forwards hundreds. **The suite's own cost
did not move.** The 530-case suite at `15fd054` ran in **1 m 57 s**; the 573-case
suite at the end of this work ran in **1 m 56 s** at its fastest — 43 more cases
for the same wall clock. What moved is the machine: the *identical* tree ran in
**1 m 56 s** and in **5 m 59 s** on the same afternoon, and at the slow end this
test, both real-`claude.exe` registration arms and a real-Firefox arm failed
together. The load was external and was identified rather than guessed at —
37 processes of the maintainer's own `C:\Program Files\Mozilla Firefox`, plus
Discord, Outlook and an indexer — and nothing was terminated to make a number
look better.

**So the honest statement is: a real-clock bound of five seconds, on a machine
whose suite duration varies threefold from causes outside the repository.**
Across thirteen full runs on the final tree, ten were green and three failed on
one or more of these load-sensitive arms; six of the ten green ones are
consecutive, three from each shell, which is what [the release
gate](RELEASING.md#8-run-everything) asks for.

**Nothing was changed about the test.** Weakening the bound, adding a retry or
quarantining it are all forbidden by the house rules and all wrong: the claim it
makes is the right claim, and it is the only arm that can tell an instant skip
from a five-second wait.

**Four directions.**

| # | Direction | What it costs | What it buys |
|---|---|---|---|
| **A** | **Correct the comment and leave the bound.** State that the bound is a hang detector against a *product* timeout and that a starved thread can trip it, and accept a rare red | One line, and a test that fails perhaps once in seven full runs — on a machine where the release gate is six runs | Honesty, and nothing else. The 2026-08-20 observation stops being a surprise to the next reader |
| **B** | **Bound the acquire rather than the wall clock.** Measure inside `ReclaimStaleMarkers` — return how the acquire was attempted, or have the test assert `LockScopes.NeverWaits` is what it passes — and delete the stopwatch | The claim weakens from *it did not wait* to *it asked not to wait*. A future edit that passed a real timeout would still fail, but one that added a `Thread.Sleep` beside the acquire would not | A deterministic assertion with no clock in it, which is what [TESTING.md](TESTING.md) asks for everywhere else |
| **C** | **Both: assert the argument AND keep a much larger wall-clock backstop** — say sixty seconds, which no scheduling gap on this machine approaches | Two assertions where there was one, and the backstop stops being evidence about *promptness* — it only catches an outright hang | Keeps a hang detector while removing the false positive. It is the shape the rest of the suite already uses |
| **D** | **Run this one test alone.** Exclude it from the parallel set so the clock is honest | Cannot be expressed without a mechanism the suite does not have, and `HouseRuleTests.NoTestInTheTreeIsSkipped` is deliberately hostile to per-test special cases | A clock that means what it says, at the price of a second way to run tests |

**Recommendation: C**, with A's sentence written into it either way. The property
worth keeping is *the reclaim does not wait*, and the honest way to state it is
the argument it passes plus a backstop far enough outside the noise to be about
hangs rather than about scheduling. **B alone is a real weakening** and should
not be taken without saying so in the test. **D is a trap**: it buys a clock at
the cost of a second execution mode, and the first thing that would follow it is
a second test wanting the same exemption.

⚠️ **Corrected 2026-08-23 (previously "The same question applies to nothing else
in the suite today — this is the only arm whose comment claims load cannot reach
it, and the claim has now been falsified once").** It applies to one other arm,
and that arm is *tighter*. See the closure below.

---

#### Closed 2026-08-23 — **B was taken, not the recommended C**, and the reason is in the instruction

**What was done.** The stopwatch is gone from the test entirely; there is no
backstop and no second assertion of elapsed time anywhere in that arm. In its
place `MachineMutex.Acquire` records the timeout it was **handed** —
`MachineMutex.LastAcquireTimeout`, set before the wait so it lands on every path
out of the method — and `LiveInstances.ReclaimStaleMarkers` surfaces it on
`LiveMarkerReclaim.GateWait`, read back off the gate the pass used rather than
restated at the construction site. The assertion is now
`Assert.That(skipped.GateWait).IsEqualTo(LockScopes.NeverWaits)`. A descheduled
thread cannot move it.

**That is direction B, and B alone was called "a real weakening" above.** The
table's recommendation was **C** — the argument *plus* a sixty-second backstop.
The maintainer's instruction of 2026-08-23 was explicit in the other direction:
*"remove the elapsed-time bound entirely"*. So the weakening is taken
deliberately and it is written into the test in place, in the words the entry
above asked for: the claim is now **the pass asked not to wait** rather than
**the pass did not wait**. An edit that started passing a real timeout is still
caught; an edit that put a `Thread.Sleep` beside the acquire is not, and neither
is an outright hang.

**What stops the new assertion being vacuous.** A `GateWait` that always read
zero would be indistinguishable from a working one when read from the reclaim
test, so the property is exercised in both directions on one object by
`SessionLockTests.AGateRecordsTheWaitItWasHandedRatherThanAConstant`: unasked is
`null`, `NeverWaits` records zero, `LiveInstanceGate` records five seconds. Both
arms were planted red before being restored — the reclaim arm by making the
product acquire at `LockScopes.LiveInstanceGate`, which is the exact defect it
exists to catch, and the control by pinning `LastAcquireTimeout` to
`TimeSpan.Zero`.

#### The same shape does appear elsewhere, and nothing was changed about it

`SessionLockTests.TheSweepScopeIsTryAcquireAndSkipAtZeroTimeout`, one line:

```csharp
// Zero timeout means zero. Anything that queued would show here as
// the time the winner held it.
await Assert.That((double)skipped["elapsedMilliseconds"]!).IsLessThan(1000);
```

**It is the same claim with five times less headroom**, and it is measured
across a process boundary: `SessionProbe`'s `session-sweep` mode wraps a
`Stopwatch` around one `mutex.Acquire(LockScopes.NeverWaits)` in each of **eight
separate probe processes** launched together, and the suite asserts every loser
came back inside a second. Process creation is the most contended operation on a
saturated Windows box, and the arm that was falsified at 5.2 s did strictly less
work in-process. It also invents its own number — a bare `1000` rather than
anything from `TestDefaults` — which is the second rule
[the house doctrine](TESTING.md#every-duration-is-a-hang-detector-or-it-is-a-defect)
states.

**It has not gone red on this machine and it has not been touched**, because the
instruction was to report it rather than fix it silently. The rest of the suite
was swept for the shape and is clean: the only other elapsed-time *assertions*
left are `SessionLockTests`' 120-second one, whose comment already disclaims
promptness and whose bound is far outside the noise;
`FakeChildHarnessTests`' and `ProvisioningTests`' two, which are **lower**
bounds and which load can only push further from failing (and the second reads a
`ManualClock`); and `ReinstallBrowserTests`' `TestDefaults.BrowserHang`, a
thirty-minute hang detector inside a polling loop. Everything else that times
something writes it into a report and asserts nothing about it.

**And nothing mechanises this rule.** The 2026-08-18 sweep that deleted five
promptness assertions left comments where each one had been, which is why they
were findable — but `HouseRuleTests` has two arms and neither of them is *no
test asserts on a wall clock*. The reclaim arm was added on 2026-08-20, after
that sweep, by someone who had read the doctrine. **That is what a habit looks
like**, and it is a candidate for the mechanism column in `CLAUDE.md` rather
than the reader column.
