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

---

## Open questions

### 1. Are parameter descriptions truncated? — **ANSWERED 2026-08-18: no**

Claude Code's [MCP documentation](https://code.claude.com/docs/en/mcp) states, verbatim:

> Claude Code truncates tool descriptions and server instructions at 2KB each.
> Keep them concise to avoid truncation, and put critical details near the start.

It says **nothing** about `inputSchema.properties[*].description`. So the tool and
instructions surfaces are cited; the parameter surface is a genuine unknown.

**Taken:** gate all three at 2048 bytes, and say in the constant that two surfaces
are documented and the third is a conservative assumption. Over-gating a surface
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

### 2. What should CI actually run?

`SaturationTests` is `[NotInParallel]` and takes **80–96 seconds alone** — it is
most of the suite's wall clock. There is no CI today; adding one is on the queue.

**Taken:** CI runs the full suite including saturation, on Windows, on push and
pull request. Rationale: 54% of this project's enforcement is TEST or RELEASE
phase, so a CI that skips the expensive half re-creates the gap it exists to
close. Cost is roughly two minutes per run.

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
it did not read that far. Closing it is on the queue.

### B. Where the three directory-scoped `CLAUDE.md` files go

**Taken:** `src/BrowserAI/Interop/`, `src/BrowserAI/Sessions/`, and
`src/BrowserAI/Runtime/` — measured as carrying 59% of all prohibition language
in the tree (110, 63 and 32 instances). Each is capped at 20 lines and contains
only rules true of *every* file in that directory, each naming its mechanism.

**To reverse:** delete the files. Nothing depends on them.

### C. Pushing before the engineering queue is finished

You chose: restructure → push → engineering. That means the pushed repo will
carry a **known-intermittent suite** at `Limit => Unbounded`, deliberately, and
an open engineering queue.

**Taken:** push anyway, and make the state legible — `TODO.md` names what is
open, and `SuiteParallelism` says in as many words that unbounded is a race
detector and that a future reader must not "fix" red runs by capping.

**To reverse:** nothing published is irreversible except the history itself,
which you have already accepted.

### D. If the timing work cannot reach 20 consecutive green

**Taken, if it comes to it:** do not cap, do not skip, do not push a suite I
cannot describe. Record the surviving failures by name with their mechanisms in
`HAZARDS.md`, push with the restructure, and leave the streak unmet and stated.
An honest "19 of 20, and here is the one" beats a green number obtained by
removing the test that produced it.

---

## Settled, recorded here only so it is not re-litigated

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

### 6. The per-directory gate at 60 seconds

Raised from 5 s. That is 2× `RenameWindow.Budget` and roughly 18× the measured
queue at the charter's 100-process design point. `TheGateOutlastsEveryWaitTakenInsideIt`
now fails the build if either number crosses the other — which is the part that
matters more than the value.

**Defensible, not uniquely correct.** Reversible in one line, guarded by a test.

### 7. The deeper fix that was NOT taken

A loser holds the gate **only to name the holder** — the sharing violation alone
already proves ownership. A contender that probed *before* taking the gate would
remove the queue entirely, and with it the whole class of defect. It was not
taken because it has TOCTOU subtleties and reopens the most safety-critical path
in the product.

**This is the real fix, and it deserves a decision rather than a drive-by.**

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
