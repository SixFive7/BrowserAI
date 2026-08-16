<!--
SPDX-FileCopyrightText: Copyright 2026 Jori Huisman
SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
-->

# BrowserAI — implementation plan

What to build, and what is known to go wrong while building it.

This plan is **consumed as the code is written**. Every section is a requirement
waiting to become a file in this repository; when it has, the section has done its
job. That is what separates it from [`README.md`](README.md), which carries the
architecture, the scope boundary and the reasoning behind each decision and stays
true for as long as the decision does — and from [`kb/`](kb/README.md), which
carries the measurements and goes stale when upstream ships rather than when we
change our minds.

Work settled in intent but not yet done is tracked in [`TODO.md`](TODO.md).

---

## How this plan ends

**The plan is consumed and then deleted.** Each section below is one file. As the
code that satisfies a section lands, the section is marked `built` and records
what implements it. When every section is `built`, the whole plan is audited once
with extreme scrutiny for anything missed — and if it is complete, it is deleted.

That is why it is a folder rather than a document. A section that is its own file
is marked done and removed on its own, so the ending is a folder that empties as
the build proceeds rather than one irreversible final act. It is also why the
`Implemented by` column exists: without it, the final audit means re-reading
everything cold and hoping.

| # | Section | Covers | Status | Implemented by |
|---|---|---|---|---|
| — | [Build order](plan/build-order.md) | The ordered list of what to build, in sequence, each step with a done-test | — | — |
| — | [Pre-release checklist](plan/pre-release.md) | What must pass before a release may be cut. The only gate that exists | — | — |
| A | [Ship and own the runtime](plan/A-runtime.md) | Node, the vendored packages, first-run browser provisioning, packaging | not built | — |
| B | [Be the MCP server](plan/B-mcp-server.md) | Transport, protocol version negotiation, registration | not built | — |
| C | [Sessions](plan/C-sessions.md) | `browserai_init` and the session family, the session directory as identity, lifetime | not built | — |
| D | [Locking and single-instance](plan/D-locking.md) | The three lock scopes, ownership, the stray sweep, never by image name | not built | — |
| E | [Lifecycle and observability](plan/E-lifecycle.md) | Job objects, stderr, exit codes, stdio ownership, logging | not built | — |
| F | [Artifact management](plan/F-artifacts.md) | Routing on the way in, typed folders, the artifact index | not built | — |
| G | [Updates](plan/G-updates.md) | Velopack, the landmines, rollback, install layout | not built | — |
| H | [The model-facing surface](plan/H-model-surface.md) | Tool descriptions, instructions, the error catalogue, discoverability | not built | — |
| — | [Implementation stack](plan/stack.md) | Chosen components, and the places the SDK must be deviated from | part built | *Toolchain rows and the build-configuration table:* `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `src/BrowserAI/app.manifest`, and publish-gated AOT in `src/BrowserAI/BrowserAI.csproj`. The nine SDK deviations and the version-from-git-tags rule are **not** built |
| — | [Testing](plan/testing.md) | The four layers, our own harness, the upstream-review gate, the release gate | part built | *Framework prohibitions only:* TUnit as the sole test dependency in `Directory.Packages.props`, its analyzers pinned per-rule in `.editorconfig`, and `tests/BrowserAI.Tests/BuildConfigurationTests.cs`. None of the five layers, the harness, the gate or the release gate exist yet |
| — | [Hazard index](plan/hazards.md) | Every known failure mode, in one checkable list | living | — |

The hazard index is the one section that is **not** consumed. It outlives the
build: rows gain a status and the evidence that closed them rather than being
deleted.

**`part built` is a third status, added 2026-08-16 with build-order step 1.** It
was not in the original vocabulary, and the alternative was worse: several
[build-order](plan/build-order.md) steps consume *part* of a section — step 1
takes the toolchain rows out of [stack](plan/stack.md) and the framework
prohibitions out of [testing](plan/testing.md), leaving the nine SDK deviations
and all five test layers untouched. Marking either section `built` would be a
false record, and the `Implemented by` column is what the final audit reads, so
a section that says `built` while half of it is unwritten defeats the one thing
that makes deleting the plan safe. A `part built` row states what has landed and
what has not, and **only a section with no `not built` remainder may be deleted.**

---

## Reading order

If you are starting cold: [`README.md`](README.md) for what was decided and why,
then [build order](plan/build-order.md) for what happens first, then the section
that step names.

Do not read this plan front to back. It is a reference for the step you are on.
