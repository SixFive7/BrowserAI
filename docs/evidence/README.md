<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# docs/evidence

**What a measurement was actually taken from.** A kb entry, a hazard row, a
`docs/reviews/` body or a doc comment says what it found; the batch here is the
logs, the captures, the registry exports and the machine inventories that finding
was read out of. One directory per batch, named `<date>-<batch>`, each with a
`README.md` naming what cites it and — where a file was too large to keep — what
was cut and the SHA-256 of what it was cut from.

**These are records, not inputs.** Nothing builds them, nothing in the suite
reads them, and re-running a probe produces new ones rather than overwriting
these. The probes themselves live in
[`docs/probes/`](../probes/README.md); where a batch has one, its
README links to it.

**Everything here came out of `.work/` on 2026-09-16**, when the scratch
directory was wiped. Until that day these files were gitignored, which is why
several of the records that cite them say so in the past tense.

⚠️ **Two things about these bytes are not the bytes as taken, and both are
deliberate.** [`.gitattributes`](../../.gitattributes) normalises line endings to
LF for everything this repository tracks, so a capture written with CRLF is
stored with LF — the text is what it was, the line endings are this
repository's. And where a file was too large to keep, what is here is a
*trimmed* copy under a `.trimmed.` name that carries the SHA-256 of the original
and names every cut it made; the untrimmed original is never here. Any other
departure is called out in the batch's own README —
[`2026-08-26-post-course-correction`](2026-08-26-post-course-correction/README.md)
is the only one, and it is one byte, twice.

**`.gitignore` un-ignores this subtree explicitly**, at the very end of the
file, because `*.log` and `releases.*.json` are ignored everywhere else for
reasons that have nothing to do with a record. Adding a pattern below that
negation silently drops evidence from the tree.

| Batch | What it is | Cited by |
|---|---|---|
| [`2026-08-15-jobtest`](2026-08-15-jobtest/README.md) | Sixteen runs of the job-object prototype | [`JobProbe.cs`](../../tests/BrowserAI.TestProbe/JobProbe.cs) |
| [`2026-08-16-step20-manifest`](2026-08-16-step20-manifest/README.md) | The release manifest built by hand, once | [`Write-ReleaseManifest.ps1`](../../build/Write-ReleaseManifest.ps1), [`RELEASING.md`](../../RELEASING.md) |
| [`2026-08-26-chromium-death`](2026-08-26-chromium-death/README.md) | A Chromium that died with nothing in the log | `QUESTIONS.md` |
| [`2026-08-26-post-course-correction`](2026-08-26-post-course-correction/README.md) | Every transcript that review cites | [`docs/reviews/2026-08-26-post-course-correction.md`](../reviews/2026-08-26-post-course-correction.md) |
| [`2026-08-27-desktop-heap`](2026-08-27-desktop-heap/README.md) | The desktop-heap ceiling run | [kb](../../kb/windows/processes.md#desktop-heap-the-ceiling-nothing-reports-measured), `QUESTIONS.md` |
| [`2026-08-30-release-gate`](2026-08-30-release-gate/README.md) | Four gate logs from the six-run gate | [`HAZARDS.md`](../../HAZARDS.md#hazard-index) |
| [`2026-09-14-firstrun`](2026-09-14-firstrun/README.md) | A non-silent install, end to end | [kb](../../kb/packaging/velopack.md#a-non-silent-install-starts-the-app-in-a-console-window-and-nobody-is-on-the-other-end-of-it--measured-2026-09-14), [`HAZARDS.md`](../../HAZARDS.md#hazard-index), `Program.cs`, `InstallerHandoffTests` |
| [`2026-09-14-probes-m2c`](2026-09-14-probes-m2c/README.md) | What a snapshot of a dense page costs | [`ServerInstructions.cs`](../../src/BrowserAI/Proxy/ServerInstructions.cs) |
| [`2026-09-14-webp-ask`](2026-09-14-webp-ask/README.md) | A WebP screenshot past 16,383 px | [kb](../../kb/playwright/tools-and-artifacts.md#a-webp-screenshot-past-16383-px-comes-back-as-zero-bytes-with-iserror-false--measured-2026-09-14), [`TODO.md`](../../TODO.md) |
| [`2026-09-15-app`](2026-09-15-app/README.md) | Two binaries in one pack | [kb](../../kb/packaging/velopack.md#two-binaries-in-one-pack-measured-end-to-end--2026-09-15) |
| [`2026-09-15-fix`](2026-09-15-fix/README.md) | Three repros and the parked-stdin captures | [kb](../../kb/windows/processes.md#a-read-parked-on-standard-input-is-woken-by-neither-cancelling-it-nor-disposing-the-stream--measured-2026-09-15), `OrphanedConsoleStart`, `InstallerHandoffTests`, `DirectStdioServerTransportTests` |
| [`2026-09-15-install`](2026-09-15-install/README.md) | Installing the published v1.0.0 and watching it | [kb](../../kb/packaging/velopack.md#re-measured-2026-09-15-against-the-published-v100-and-the-paragraph-above-was-half-wrong) |
| [`2026-09-16-garbage`](2026-09-16-garbage/README.md) | What old versions left on the maintainer's machine | [`docs/ledger/2026-09-15-release-session.md`](../ledger/2026-09-15-release-session.md), [re-verification row 66](../../kb/re-verification.md) |
| [`2026-09-16-release-body`](2026-09-16-release-body/README.md) | What a release body renders as | [`RELEASING.md`](../../RELEASING.md) |
| [`2026-09-17-reverify`](2026-09-17-reverify/README.md) | The re-verification batch, the registry key it cleared, and one browser that died with the wild signature | [`HAZARDS.md`](../../HAZARDS.md#hazard-index), [`TESTING.md`](../../TESTING.md) |
| [`2026-09-17-resume-wedge`](2026-09-17-resume-wedge/README.md) | A session whose child was killed, and the browser call that never came back | [kb](../../kb/playwright/provisioning-and-timings.md#the-resume-wedge-measured--2026-09-17), [`HAZARDS.md`](../../HAZARDS.md#hazard-index), [`QUESTIONS.md`](../../QUESTIONS.md) |
| [`2026-09-17-file-paths`](2026-09-17-file-paths/README.md) | Which artifact pointers are absolute, before and after, and end to end | [kb](../../kb/playwright/tools-and-artifacts.md#every-artifact-pointer-a-tool-result-carries-is-absolute--measured-2026-09-17), [`TODO.md`](../../TODO.md) |
| [`2026-09-17-provisioning-1245`](2026-09-17-provisioning-1245/README.md) | First-run provisioning re-measured at chromium 1245 and firefox 1548 | [kb](../../kb/playwright/provisioning-and-timings.md#first-run-provisioning), [re-verification row 21](../../kb/re-verification.md) |
| [`2026-09-21-provisioning-1246`](2026-09-21-provisioning-1246/README.md) | The same figures again at chromium 1246 and firefox 1549, taken in the batch that rolled `@playwright/mcp` rather than left owed | [kb](../../kb/playwright/provisioning-and-timings.md#first-run-provisioning), [re-verification row 21](../../kb/re-verification.md) |
| [`2026-09-21-webmcp`](2026-09-21-webmcp/README.md) | What a page's own WebMCP tools reach, and whose text comes back | [kb](../../kb/playwright/tools-and-artifacts.md#a-page-can-add-tools-to-the-childs-toolslist-and-its-own-text-reaches-a-caller--measured-2026-09-21) |
| [`2026-09-23-release-body`](2026-09-23-release-body/README.md) | The `1.1.0` release body as published, and what GitHub's renderer made of it | [`RELEASING.md`](../../RELEASING.md) |
| [`2026-09-23-release-manifest`](2026-09-23-release-manifest/README.md) | The resolved set `1.1.0` was cut from, which used to survive only as a release asset | [`RELEASING.md` item 11](../../RELEASING.md#11-the-resolved-set-is-recorded-beside-the-artifact) |
| [`2026-09-23-feed-hosting-research`](2026-09-23-feed-hosting-research/README.md) | Who reads which feed file, what hosting the feed anywhere else would cost, and why the automatic source-code links cannot be removed | [`DECISIONS.md`](../../DECISIONS.md#locking-logging-versioning-and-registration), [`RELEASING.md`](../../RELEASING.md#what-a-release-publishes), [kb](../../kb/packaging/velopack.md#where-the-feed-and-the-package-are-hosted-and-the-four-places-they-could-have-gone-instead--read-2026-09-23) |
