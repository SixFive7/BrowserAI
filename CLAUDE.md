<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# BrowserAI — working instructions

BrowserAI is a Windows-only, NativeAOT .NET MCP server that proxies a bundled `@playwright/mcp` child over stdio. [`README.md`](README.md) is the charter — architecture, scope and the reasoning behind every decision. [`PLAN.md`](PLAN.md) is what to build, and is consumed as the code gets written. This file is the standing rules for working in the repository.

**Status: design phase, nothing is built.** Work settled in intent but not yet done lives in [`TODO.md`](TODO.md); open design questions stay in the README, and the [hazard index](PLAN.md#hazard-index) is in the plan.

**Measured facts go in the knowledge base at [`kb/`](kb/README.md), not in the README and not in the plan.** The README says what we decided; the plan says what to build; `kb/` says what we measured about Chromium, Firefox, Playwright, Node and Windows. It is a directory tree with one article per topic and a topic-sorted index at [`kb/README.md`](kb/README.md) — put a new fact in the article it belongs to, never in a new top-level file. Every entry carries a date, the versions it held under, and how to re-establish it. **Never update a result by reasoning — re-run the measurement, or mark the entry `[STALE]`.** An adjusted number is indistinguishable from a measured one, which makes it worse than a gap. New `[FLOATS]` entries need a row in the [re-verification index](kb/README.md#re-verification-index), or nobody will ever re-check them.

## Before changing `upstream-review.json` — stop and read the procedure

[`upstream-review.json`](upstream-review.json) records the upstream versions a human has actually **reviewed**. A test asserts those equal the versions the build **resolved**, so a red marker test is not a stale file to fix — it is a review that has not happened yet.

**Do not edit that file to make a test pass.** Read [`UPSTREAM-REVIEW.md`](UPSTREAM-REVIEW.md) and follow it: diff upstream's `tests/`, diff `config.d.ts`, check `browsers.json` and the CLI surface, then record what changed, what was adopted, **and what was declined and why**. A `notes` field left empty or unchanged is a review that did not happen.

A `PreToolUse` hook will interrupt an edit to that file and repeat this. The hook is a reminder, not the authority — this rule is.

## Versioning: everything floats, the build freezes it

Every dependency resolves to latest at build time and is frozen into the artifact. **Version numbers in the README, in [`PLAN.md`](PLAN.md#implementation-stack), in [`kb/`](kb/README.md) and in `upstream-review.json` are provenance stamps, not targets** — the build does not read them.

- **Never pin a dependency to work around a break.** Fix forward; make the new version work.
- **Never assert a version or a "latest" claim from memory.** If it was not looked up this session, say plainly that it is unverified. A confident stale version is worse than an admitted gap. Route package questions to the `nuget` MCP server, .NET/C# questions to `microsoft-learn`, and anything else to `context7` — but prefer a vendor's own server over `context7`, which is metered.
- **Stamp what you verify.** The README's convention is `Verified <date> @ <version>`; a bare "Default: X" claim cannot tell you when it was last true.

### The daily drift check

**Once per day of work, before anything substantive, check whether upstream moved.** Read [`drift-check.json`](drift-check.json). If `lastChecked` is today, skip it and say nothing. Otherwise resolve all five upstreams, compare against `upstream-review.json`, and write the result back.

Resolve them **the way the build resolves them**, which is not the way a registry query defaults:

| Upstream | How |
|---|---|
| `@playwright/mcp` | npm dist-tag `latest` |
| `playwright-core` | **`@playwright/mcp`'s own exact dependency** — never npm `latest`. Upstream publishes daily alphas and pins one exactly; on 2026-08-15 `latest` was `1.62.1` while the shipping version was `1.63.0-alpha-2026-08-05` |
| `ModelContextProtocol`, `Velopack` | nuget.org, stable only, via the `nuget` MCP server |
| `node` | `nodejs.org/dist/index.json`, newest entry carrying an `lts` field. The Current line is deliberately not tracked |

**Rules that make this worth having:**

- **Only stamp `lastChecked` after a lookup actually returned a version.** A date written from intent reads identically to a real one and silences the next check for a day. This is the same failure as editing `upstream-review.json` to make a test pass.
- **Never block work on it.** Drift is information, not a gate. Report it, offer to run [the review procedure](UPSTREAM-REVIEW.md), and carry on with what was asked.
- **Drift is not a bump.** Finding a newer version does not license editing `upstream-review.json` — that still requires the review.
- **A confirmed move puts the [re-verification index](kb/README.md#re-verification-index) in play.** That table is where the `[FLOATS]` facts a bump can silently invalidate are listed, and it is the half of the review the golden snapshot cannot do.

**Why a directive rather than a scheduled job.** The obvious answer is a CI poller, and it is not needed here: this project is built entirely through an agent, so a rule that fires at the start of a working session runs by construction — the check happens because the work happens. Dependabot cannot do the job in any case (verified 2026-08-14 against `dependabot-core`'s own test table: a NuGet `Version="*"` is rewritten to `*` and produces no PR, and npm `"latest"` is skipped by a dist-tag guard — it bumps declared floors, and this project declares none).

## Testing is a hard requirement, not a phase

The suite is the only thing standing between an upstream change and a shipped regression. Floating without a suite that catches breakage is strictly worse than pinning.

- **No release with a red test.** Not "a known failure", not "unrelated to this change".
- **No release with a skipped, quarantined or conditionally-ignored test.** A `Skip` in the tree at release time is a red build wearing a disguise.
- **An unclassified tool fails the build.** Every tool in the surface carries an explicit session-type classification, deny-by-default.
- Framework is **TUnit** — `await Assert.That(actual).IsEqualTo(expected)`. **Never add FluentAssertions** (relicensed at 8.0.0 to a commercial tier). **Never add `Microsoft.NET.Test.Sdk`** — TUnit is MTP-only and conflicts with it.
- **TUnit's analyzers run at error severity.** A TUnit assertion that is not awaited passes silently. That is green-when-broken, which is the exact failure class this project exists to eliminate, so the analyzer is not optional.

## The scope boundary

BrowserAI is a **proxy**. It spawns `@playwright/mcp` and forwards JSON-RPC.

- **Never hand-write a tool schema in a `.cs` file.** Every schema originates from the child's `tools/list` at runtime. If a tool definition is being typed into C#, the boundary has been crossed.
- **Never drive Playwright directly** — no `Microsoft.Playwright`, no reimplementation of the snapshot/ref system, response formatting or error shaping.
- Rewriting `tools/list` (filter, re-describe, inject the `session` parameter) is in scope. **Renaming is not** — upstream names pass through byte-for-byte, settled in [`README.md` → Tool naming](README.md#settled-2026-08-14), because a `deny` hook keyed on `browser_take_screenshot` exists in ten repositories and a rename map is a second surface to re-review on every bump. Composing new tools out of several upstream calls is also out of scope, and would require reopening the charter.

## Silent failure is the enemy

Every defect in the charter's opening table reported healthy while broken. Observability is a feature requirement here, not a nicety.

- `stdout` is the protocol channel. **Nothing anywhere in the process may call `Console.WriteLine`**, including inside a `catch`. UTF-8, LF, no BOM, owned by one wrapper type.
- Cache a child's `ExitCode` as an `int` immediately — `Process.ExitCode` throws after `Dispose()`.
- Prefer a mechanism over a habit. If a rule can be a failing test, a hook or an analyzer, make it one.

## Style

- Newest stable idioms the GA toolchain compiles; familiar-but-dated patterns are style defects.
- Analyzers and `.editorconfig` at error severity. **Never weaken a severity to make code pass** — fix the code.
- Source files carry the two-line SPDX header used at the top of this file. Use the `LicenseRef-` form; the bare `FSL-1.1-MIT` identifier is forbidden by the licence.

## Scratch work

Agent-produced temporary files go in `.work/` at the repository root (gitignored, created on demand). Do not scatter them elsewhere on the machine. This supersedes any user-global temp-directory convention while working in this repository.
