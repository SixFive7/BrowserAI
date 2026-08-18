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

**Both adversarial reviews state what they tried to break and could not**, which
is the half that makes the rest trustworthy. A review that reports only findings
is indistinguishable from a shallow one.
