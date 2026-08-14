<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# BrowserAI — working instructions

BrowserAI is a Windows-only, NativeAOT .NET MCP server that proxies a bundled `@playwright/mcp` child over stdio. [`README.md`](README.md) is the charter and the source of truth for design; this file is the standing rules for working in the repository.

**Status: design phase, nothing is built.** Work settled in intent but not yet done lives in [`TODO.md`](TODO.md); open design questions and known hazards stay in the README.

## Before changing `upstream-review.json` — stop and read the procedure

[`upstream-review.json`](upstream-review.json) records the upstream versions a human has actually **reviewed**. A test asserts those equal the versions the build **resolved**, so a red marker test is not a stale file to fix — it is a review that has not happened yet.

**Do not edit that file to make a test pass.** Read [`UPSTREAM-REVIEW.md`](UPSTREAM-REVIEW.md) and follow it: diff upstream's `tests/`, diff `config.d.ts`, check `browsers.json` and the CLI surface, then record what changed, what was adopted, **and what was declined and why**. A `notes` field left empty or unchanged is a review that did not happen.

A `PreToolUse` hook will interrupt an edit to that file and repeat this. The hook is a reminder, not the authority — this rule is.

## Versioning: everything floats, the build freezes it

Every dependency resolves to latest at build time and is frozen into the artifact. **Version numbers in the README and in `upstream-review.json` are provenance stamps, not targets** — the build does not read them.

- **Never pin a dependency to work around a break.** Fix forward; make the new version work.
- **Never assert a version or a "latest" claim from memory.** If it was not looked up this session, say plainly that it is unverified. A confident stale version is worse than an admitted gap. Route package questions to the `nuget` MCP server, .NET/C# questions to `microsoft-learn`, and anything else to `context7` — but prefer a vendor's own server over `context7`, which is metered.
- **Stamp what you verify.** The README's convention is `Verified <date> @ <version>`; a bare "Default: X" claim cannot tell you when it was last true.

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
- Rewriting `tools/list` (filter, rename, re-describe, inject `handle`) is in scope. Composing new tools out of several upstream calls is not, and would require reopening the charter.

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
