<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Reviewing an upstream version bump

**Read this before editing [`upstream-review.json`](upstream-review.json).** This is the procedure that file's failure is asking you to run.

## Why this exists

Every dependency floats to latest at build time ([README → Versioning policy](README.md#versioning-policy-everything-floats-the-build-freezes-it)). Our suite going green after a bump means **our assumptions still hold**. It cannot mean **we noticed what upstream learned**.

The golden `tools/list` snapshot catches surface changes — a new tool, a renamed one, a changed schema. It is blind to:

- behaviour changes behind an identical schema
- a new or renamed **config key** — and `loadConfig` is a bare `JSON.parse` with no schema validation, so a renamed key is discarded in silence
- a changed **default** (this is the `channel: "chrome"` class of failure)
- a fixed bug that changes response shape
- a new capability worth enabling, or worth deliberately declining

So the marker gates adoption. When the build resolves a version newer than the reviewed one, the marker test fails and [the release gate](TESTING.md#the-release-gate) cannot pass.

**This is now a proof, not a speed bump.** An earlier design gated the marker behind a human approval prompt; it was abandoned on 2026-08-15 after measurement showed it inert against sub-agents and, against a human, evidence only that someone clicked. [The gate is mechanical](TESTING.md#the-upstream-review-gate): four snapshots — `tools/list`, `--help`, `config.d.ts`, `browsers.json` — are regenerated from the resolved payload and diffed, the suite runs in full, and the marker entry must adjudicate every snapshot that changed and answer every manual re-verification row by name. **A review that ignores what the build observed fails the build.** What is left to a person is judgement — whether a change touches an abstraction of ours — not vigilance.

## What a review consists of

Read these, in this order. The first two carry most of the value.

| Read | Catches |
|---|---|
| `git diff <old>..<new> -- tests/` in the upstream repo | **What upstream now asserts.** Better signal than the changelog — tests are executable, changelogs are editorial. This is the whole point of the exercise |
| `git diff <old>..<new> -- config.d.ts` (`@playwright/mcp`) | Renamed or removed config keys. **Highest-value diff of the set**, because this failure is silent by construction |
| `browsers.json` | A moved browser revision. **Nothing in the payload changes** — browsers are [provisioned on first run](ARCHITECTURE.md#the-runtime-it-ships), not built into the installer — but every machine re-downloads **203.8 MB** and re-extracts 433 MiB. **Updated 2026-08-17 (previously "and the old revision sits on disk until something prunes it").** `RevisionPrune` does, on the next successful provision — so the cost of a moved revision is the download, and the one to state in the release notes is that a **rollback** re-downloads too. (This row previously said "~700 MB", which was MiB-on-disk for *both* browsers back when the payload carried them.) |
| CLI surface: `--help` on old vs. new | Flags that vanished. This is the `--output-mode` class |
| Release notes / changelog | Intent and rationale the diffs do not carry |
| For `ModelContextProtocol`: `Directory.Packages.props` | Whether the SDK changed *its own* test framework. `Verified 2026-08-14 @ 2.2.0`: `xunit.v3`. We are on TUnit deliberately, and a move upstream is worth knowing about |
| **[`kb/`](kb/README.md) → [Re-verification index](kb/re-verification.md)** | **Measured facts this project depends on that a bump can silently invalidate.** Work the table top-down; the first three each falsify a design decision if they move. Each row links to the article that established it. This is the half of the review that catches "upstream did not change its surface, it changed its behaviour" |

**One specific thing to watch in the `playwright-core` diff.** Upstream passes `["chrome-win"]` to the `winldd` dependency check while Chromium extracts to `chrome-win64`, so for Chromium the check is a **permanent no-op**. `Verified 2026-08-15 @ playwright-core 1.63.0-alpha-2026-08-05`. If that one-character mismatch is ever fixed, Chromium starts validating **39 binaries on every cold start** — +329 ms, cached for 30 days — and a bug fix upstream arrives here as a latency regression with nothing announcing it. The check is [re-verification row 10](kb/re-verification.md); this paragraph is why that row exists.

## What to write down

Update the entry in [`upstream-review.json`](upstream-review.json):

- `reviewed` — the version you actually reviewed, matching what the build resolved
- `date` — today, ISO 8601
- `notes` — **not optional.** What changed, what was adopted, and **what was explicitly declined and why**

A decline with a reason is worth as much as an adoption: it stops the same question being re-litigated at the next bump. An empty note is a review that did not happen, and it is visible as such in the diff.

If the review surfaces work that is settled in intent but not yet done, it belongs in [`TODO.md`](TODO.md). If it surfaces a new failure mode, it belongs in the plan's [hazard index](HAZARDS.md#hazard-index), and the fact behind it in the [`kb/`](kb/README.md) article that owns the topic — that list is what a reviewer checks the implementation against, and it says plainly: *if you find a new hazard, add it here*.

**If a re-verification comes back different, update the [`kb/`](kb/README.md) article that carries the fact by re-running the measurement — never by reasoning.** An entry whose number was adjusted to match an expectation reads identically to one that was measured, which makes it worse than no entry at all. If a check is owed and has not been run, mark it `[STALE]` and say so; a gap that announces itself is recoverable, a confident wrong number is not.

## If the diff is large

A big upstream jump is not a reason to skip the review; it is the reason the review exists. Split it: bump to an intermediate version, review, land it green, then bump again. Adopting several versions in one step and reviewing none of them is the exact state this file was built to prevent.

## If a change breaks us

**Fix forward. Do not pin back.** The response to a breaking upstream change is to make the newest version work — that is [rule 4](README.md#the-five-rules-that-make-floating-safe) of the versioning policy, and "pin it back for now" is the failure the policy exists to prevent. A red suite blocks the *release*, never the *update*.
