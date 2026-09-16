<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# docs/ledger

**A session's own running record of what was decided and why, snapshotted.**
Not a review, not a changelog, not a design document: a ledger is written while
the work is happening, by whoever is coordinating it, and it is the only place
that records the question that was asked, the options that were weighed, the
answer that came back and the things that were noticed and set aside.

**It is a record, so it is read-only.** Nothing here is edited after the
snapshot — not to fix a typo, not to reconcile it with what the code ended up
doing. A ledger that disagrees with the tree is telling you something about the
day it was written. Where it was wrong, the correction belongs in the document
that owns the claim, with a `previously` clause, and not here.

⚠️ **A ledger is not a decision of record.** [`DECISIONS.md`](../../DECISIONS.md)
is the charter; [`HAZARDS.md`](../../HAZARDS.md) is what is known to be
dangerous; [`kb/`](../../kb/README.md) is what has been measured. If a ledger is
the only place something is written down, it has not been written down — move it
to whichever of those three owns it. The ledger then records that it was moved.

| Ledger | Session |
|---|---|
| [`2026-09-15-release-session.md`](2026-09-15-release-session.md) | The 2026-09-15/16 release session: the 1.0.0 re-cut, the icon choice (Q196), the release-body shape (Q197), the first-run and installer measurements, the machine sweep, and the retirement of the scratch directory |
