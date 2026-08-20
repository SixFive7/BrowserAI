<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Reviews

Findings that are **too long to inline and too valuable to lose**. Everything
here was produced in `.work/`, which is gitignored — these are the copies that
survive a clean.

**These are records, not work lists.** Anything actionable is lifted into
[`TODO.md`](../../TODO.md) or [`HAZARDS.md`](../../HAZARDS.md); a finding that
lives only here is a finding nobody will act on.

| File | What it is |
|---|---|
| `2026-08-18-adversarial-locking.md` | Adversarial review of session locking and provisioning. Four wrong-answer findings, thirteen `lock.json` readers enumerated, and the probe-before-gate redesign attacked before it was built |
| `2026-08-18-adversarial-processes.md` | Adversarial review of process supervision, containment and attribution. Fourteen findings, four Tier-1 |
| `2026-08-18-truncation-findings.md` | What Claude Code's *"2KB each"* actually means, measured off the client's own outbound API request |
| `2026-08-18-truncation-prompt-for-sibling-project.md` | The same, written self-contained for another MCP server's maintainer. Includes the full re-run recipe |
| `2026-08-19-auth-transfer-and-session-modes.md` | Can a human's interactive login be handed to an unattended headless run? Measured yes, on one route of three — and copying the profile directory is **not** it. Also three claims this repository makes about its own modes that the code does not make |

**Both adversarial reviews state what they tried to break and could not**, which
is the half that makes the rest trustworthy. A review that reports only findings
is indistinguishable from a shallow one.

## What has been acted on, as of 2026-08-18

**The review files are not edited when a finding is fixed** — they are dated
records of what was true when they were written, and rewriting one would destroy
the only account of the reasoning. This table is where the status lives. `git log`
carries the fix; [`HAZARDS.md`](../../HAZARDS.md) carries what is bounded rather
than closed.

⚠️ **That was prose until 2026-08-20, and prose did not hold it.** A rename sweep
reached these files and rewrote them; it was caught by a human reading the diff.
`AppendOnlyRecordTests` now seals every file in this directory **except this
one** — by character count and SHA-256, on the prefix, so an addendum may be
appended and a body may not be rewritten. This index is deliberately *not*
sealed: the status table above is meant to move. See
[the release gate](../../TESTING.md#the-dated-records-are-append-only); a new
review is registered in the seal list in the same change that adds it.

| Finding | State |
|---|---|
| locking **A1** — `destroy` releases the lock before deleting the tree | **fixed**, `SessionDestroyTests` |
| locking **A2** — `AcquiredAbandoned` deletes a complete, in-use tree | **fixed**, `ProvisioningTests` |
| locking **A3** — `reinstall` deletes with no provisioning mutex | **fixed**, `ReinstallBrowserTests` |
| locking **A4** — path aliasing gives one directory two gates | **closed by refusal** 2026-08-19 — `SessionDirectoryGuard` refuses an aliased spelling at `init` and `resume`, and one of A4's four aliases was measured away: `Path.GetFullPath` **does** expand 8.3 short names on .NET 10 ([kb](../../kb/windows/detection.md#a-mapped-drive-letter-is-a-network-path-and-costs-the-same-22-seconds)). Two gaps stayed open as hazard rows |
| locking **A5** — `SessionIndex.Sweep` deletes a live session's entry | open, hazard row; the kb claim it falsified is corrected |
| locking **A6** — `init`'s `Existing` guard acts on a transient absence | open, hazard row |
| locking **B1** — the gate is outlasted by the waits inside it | **fixed**: the sum is asserted and the gate re-sized |
| locking **B2** — unbounded calls inside the gate, incl. UNC | **network half closed** 2026-08-19, the rest open. The caller-visible decision was taken: a network session directory is refused, **by semantics rather than by spelling** — a mapped drive letter costs the same measured 22 s and passes every string test |
| locking **B3**, **B4**, **B5** | not yet triaged |
| locking **D** — the probe-before-gate redesign | not adopted; the verdict stands as written |
| processes **1** — the client watch acts on a bare pid | **fixed**, `ProcessLivenessTests`, plus a mechanism for the next one |
| processes **2** — `TreeDelete` follows directory junctions | **fixed**, `TreeDeleteTests`; the ban message now names both properties |
| processes **3** — `RevisionPrune` decides on a stale census | **narrowed**, hazard row; the held handle was declined with a reason |
| processes **5** — concurrent launches cross-inherit pipes | **fixed**, `JobContainmentTests` |
| processes **4**, **6**–**14** | not yet triaged |
