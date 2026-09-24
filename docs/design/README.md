<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# docs/design

**A decision's alternatives, kept beside the choice.** Where
[`DECISIONS.md`](../../DECISIONS.md) records what was chosen, this is what it was
chosen *from*: the candidates that were drawn, rendered or mocked up, and the rig
that produced them. One directory per decision, named for what it is and the day
it was taken.

**A choice with nothing beside it is not a choice anybody can revisit**, which is
the whole reason this directory exists. A year from now the question is never
*what did we pick*, which the charter answers; it is *what else was on the table,
and what ruled the others out*.

⚠️ **These are renderings, not specifications.** Where a rendering and the
decision disagree -- a placeholder string, a demo default, a stand-in identity --
the decision is right and the directory's own README says where the two part
company. Implementing from an image is how a placeholder ships.

⚠️ **Unlike [`docs/evidence/`](../evidence/README.md), nothing here is
byte-exact.** An image may be cropped, and a mock-up may carry numbers nobody
measured. What each directory promises is that the alternatives are all present
and that the differences from the real thing are named.

| Design | The decision it sits beside |
|---|---|
| [`icon-candidates`](icon-candidates/README.md) | The application icon, chosen 2026-09-16 (Q196): ten candidates, two contact sheets and the renderer |
| [`toast-2026-09-24`](toast-2026-09-24/README.md) | The update toast, chosen 2026-09-24 (Q254): three renderings, the XML behind each and the rig, including the platform limit that ruled out five buttons |
