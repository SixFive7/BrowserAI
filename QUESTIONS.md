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

### 2. What should CI actually run? — **BUILT 2026-08-18, exactly as taken**

`SaturationTests` is `[NotInParallel]` and takes **80–96 seconds alone** — it is
most of the suite's wall clock.

⚠️ **Corrected 2026-08-19 (previously "There is no CI today; adding one is on the
queue").** There has been since 2026-08-18:
[`.github/workflows/build.yml`](.github/workflows/build.yml), `runs-on:
windows-latest`, on push and on pull request, running the **whole** suite with
`SaturationTests` in it. The decision below was not merely taken, it was built, and
this entry went on describing a repository with no CI in it.

**Taken:** CI runs the full suite including saturation, on Windows, on push and
pull request. Rationale: 54% of this project's enforcement is TEST or RELEASE
phase, so a CI that skips the expensive half re-creates the gap it exists to
close. Cost is roughly two minutes per run.

**One thing the entry did not price, and the workflow now carries it.**
`BROWSERAI_RELEASE_RUN` is deliberately **not** set in CI — it is named below as the
precedent for a stricter tier, and the reason it is off is that the stricter tier
wants things the runner does not have. The workflow says so at the top rather than
leaving a reader to infer it from a variable that is absent.

**To reverse:** move saturation behind a label or a nightly schedule. The
`BROWSERAI_RELEASE_RUN=1` switch already exists as a precedent for a stricter
tier.

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
  `lock.json`. Never the profile.
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
`SessionLock.ProbeForHolder` opens `lock.json` **in front of** the per-directory
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
`lock.json`, 61 ms apart.

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
refuses — *"has no `lock.json`, so it is not a BrowserAI session"* — and the model
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

### 12. The CsWin32 metadata licence — the text, gathered 2026-08-19, and the question it leaves for a lawyer

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
