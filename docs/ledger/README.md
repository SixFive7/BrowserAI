<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# docs/ledger

**A session's own running record of what was decided and why, snapshotted.**
Not a review, not a changelog, not a design document: a ledger is written while
the work is happening, by whoever is coordinating it, and it is the only place
that records the question that was asked, the options that were weighed, the
answer that came back and the things that were noticed and set aside.

⚠️ **ONE EXCEPTION, GRANTED AND INSTRUCTED BY THE MAINTAINER ON 2026-09-23,
in his words:** *"When it comes to no semantic differences and only removing traces of AI
(both in wording and character use) then I hereby grant and instruct you the
right and instruction to edit sealed documents."* A snapshot here was swept for
characters a person does not type and for nothing else. **No fact, number, date,
name or claim moved**, and a ledger that has been swept says exactly what it
said. The rule below is otherwise unchanged and the grant does not widen it.

**Both snapshots moved a second time on the same day, under the same grant, for
the WORDING half.** Nine edits between them: eight removed a decorative star
standing in front of a sentence that was already bold, and one changed a heading
from *TWO GAME-CHANGERS* to *TWO CORRECTIONS* because the body beneath it labels
them Correction 1 and Correction 2. **No maintainer-verbatim line was touched**,
and no fact, number, date, name or claim moved. The heading change moves one
GitHub anchor, which nothing in the tree links to; that was checked, not
assumed.

⚠️ **A SECOND EXCEPTION, DECIDED BY THE MAINTAINER ON 2026-09-24 AS Q295 a, in
his words:** *"Q295 a - make sure to make a safety backup before messing with
the history. leave statusai out of scope"* -- the history was rewritten to
correct the author and committer address on 22 commits, so they and every commit
descending from them took new hashes, and the 2026-09-23 snapshot's 61 citations
of those commits were re-pointed at the new ones with no maintainer-verbatim
line touched, its header saying so beside the body's new sha256 and the old one,
and its eight changed headings moving eight GitHub anchors that nothing in the
tree links to, which was checked.

**It is a record, so it is read-only.** Nothing here is edited after the
snapshot -- not to fix a typo, not to reconcile it with what the code ended up
doing. A ledger that disagrees with the tree is telling you something about the
day it was written. Where it was wrong, the correction belongs in the document
that owns the claim, with a `previously` clause, and not here.

⚠️ **A ledger whose live copy is still being appended to may be RE-SNAPSHOTTED,
and that is not an edit** -- *added 2026-09-18, when the first one was*. The rule
above is about the copy: nothing already here may be rewritten. Taking the copy
again, from a live file that has only grown, replaces a shorter prefix with a
longer whole, and the header says which commit each snapshot was taken at.
**Check it; do not trust it**: the previous body must be a byte-exact
prefix of the new one -- on the 2026-09-18 re-snapshot it was, growing by 61,187
bytes and 118 lines, and on the 2026-09-22 one by 41,332 bytes and 76 lines,
each time with nothing above them touched. **Nothing enforces this** --
`AppendOnlyRecordTests` seals `docs/reviews/` and released `CHANGELOG` sections,
and a ledger is deliberately outside it, because a sealed prefix would forbid
the re-snapshot instead of the edit.

⚠️ **A snapshot taken after the live copy is gone is the LAST one, and it says
so in its own header** -- *added 2026-09-22, when the first one was*. The
2026-09-15 ledger's live copy was `.work/STATE.md`, and the scratch folder it
sat in is ephemeral by charter; it was deleted in the same commit that took this
snapshot. From that commit the snapshot is not a copy of the record, it **is**
the record, and there is nothing left to re-snapshot it from. **A new session
does not reopen a closed ledger**: it opens a new file here, named for the day
it was opened, and this table gains a row. The reason is the rule at the top --
appending to a closed ledger from a different session's live file would rewrite
a body instead of extending one, and no prefix check could tell the difference.

⚠️ **A ledger is not a decision of record.** [`DECISIONS.md`](../../DECISIONS.md)
is the charter; [`HAZARDS.md`](../../HAZARDS.md) is what is known to be
dangerous; [`kb/`](../../kb/README.md) is what has been measured. If a ledger is
the only place something is written down, it has not been written down -- move it
to whichever of those three owns it. The ledger then records that it was moved.

| Ledger | Session |
|---|---|
| [`2026-09-23-development-session.md`](2026-09-23-development-session.md) | **CLOSED** -- the session opened 2026-09-23 and snapshotted 2026-09-24, once and finally: the *no trace of AI* directive and both halves of the sweep it ordered (12,518 characters, 4,952 wording rewrites, 398 anchors, eleven seals re-recorded under the maintainer's grant), the triage of T1 to T11, the 27 assumed justifications settled to zero, and then the research the next version rests on -- what each client does with a stdio server that exited (Q254, Q261), the password-save prompt and what the automation switch costs (Q255, Q259), registering with a second client (Q258), and Playwright's descriptor registry (T7). *The name is the day it was opened, which is the convention above; it is the first ledger here that is not a release session.* |
| [`2026-09-22-release-session.md`](2026-09-22-release-session.md) | **CLOSED** -- the session opened 2026-09-22 and snapshotted 2026-09-23, once and finally: the next-day batch (the Velopack 1.2.0 → 1.2.158 review, the measurement that a browser which ends itself loses exactly what a killed one loses, two event-id collisions and the update-check documentation), then the cutting, re-cutting and publishing of **1.1.0** with the full-packages-only decision (Q227) and the heading-date rule (Q232), the first real update observed on a real install, and then the release's asset set decided one asset at a time -- the portable archive dropped, the resolved-set manifest moved into the repository, `RELEASES` and `assets.win.json` measured out, the upload set turned into declared data, `v1.1.0` trimmed to three assets after the fact (Q234), and the feed's hosting settled where it already was (Q237). *The name is the day it was opened, which is the convention above and is not the day it closed.* |
| [`2026-09-15-release-session.md`](2026-09-15-release-session.md) | **CLOSED** -- the 2026-09-15 session and every batch since, snapshotted 2026-09-16, re-snapshotted 2026-09-18 and finally 2026-09-22: the 1.0.0 re-cut, the icon choice (Q196), the release-body shape (Q197), the first-run and installer measurements, the machine sweep, the retirement of the scratch directory -- then the second and third re-ships, the `playwright-core` pull-forward and its written exit (Q210), the re-verification batch taken against chromium 1245 and firefox 1548 -- and then WebMCP: the page-tool pass-through decision (Q218), `browserai_page_tool`, the onboarding guard (Q221), the owed re-verification rows, and the questions left open at the close (Q222-Q224). *The name is the day it was opened and is left alone: it is what every link to it says.* |
