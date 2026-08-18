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

### 1. Are parameter descriptions truncated?

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

**To settle it:** publish a parameter description just over 2048 bytes and read
what reaches the model. Nobody has done that.

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
  agent runs as the same Windows user. Change control moved to the release gate,
  where four golden snapshots already covered more. What survives is `session`
  staying mandatory — that is *routing* — and one `browser_annotate` refusal
  wherever no window was promised, as a **liveness** guard with no security
  claim. See [ARCHITECTURE](ARCHITECTURE.md#sessions) and
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
