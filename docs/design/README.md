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
| [`toast-2026-09-24`](toast-2026-09-24/README.md) | The update toast, chosen 2026-09-24 (Q254): three renderings, the XML behind each and the rig, including the platform limit that ruled out five buttons. *Replaced 2026-10-08 by the four toasts of T in [`one-binary`](one-binary/README.md)* |
| [`coordinator-owned-browsers`](coordinator-owned-browsers/README.md) | Option c, built 2026-10-03 (Q366 b and Q364): the session host the coordinator starts, the five directions it was chosen from, the measurement taken before any code, and the decisions taken for review. *Since 2026-10-08 its host and coordinator become the one background of [`one-binary`](one-binary/README.md)* |
| [`one-binary`](one-binary/README.md) | One windowless binary, decided 2026-10-08 and built by 2026-10-09 ("go with everything"), *changed 2026-10-09 (previously "decided 2026-10-08 and being built") when the build's final two-shell gate read 1190 of 1190 in both halves*: D1 to D14 and the questions they raised, E1 and E2, F1 to F5, H1, H1-T, H2, P, R, S, T, U1, U2 and the protocol pin, each with his words, its date and the alternatives it was chosen from, and the plan's text of 2026-10-04 kept beside what replaced it. *Changed 2026-10-08 (previously "Not a decision yet: the plan for one windowless binary, put to the maintainer on 2026-10-04 to take apart before anything is built (his "11 a"), with the alternatives beside each choice, its weakest points first, and the fourteen decisions it needs")* |
| [`dashboard-2026-10-11`](dashboard-2026-10-11/README.md) | BrowserAI's page, chosen 2026-10-11 ("40-46 your recommendations", which for 40 is f: direction A, overview and details, with C's live map as its conversations view) and built the same day: the five directions as mockups with their screenshots, the page as it stood rebuilt beside them, the write-up that put them to the maintainer with its seven questions, and the build's own screenshots with every place it departs from the mockups |
| [`map-variants-2026-10-11`](map-variants-2026-10-11/README.md) | The map's lines, chosen 2026-10-11 ("60 I like C": variant C, named lines) and built on 2026-10-12: the four variants of the conversations page's map as self-contained mockups with their screenshots, and the drafts before them. *Kept here since 2026-10-12, the maintainer's 106 b* |
| [`home-layouts-2026-10-11`](home-layouts-2026-10-11/README.md) | The home's layout, chosen 2026-10-11 ("61 I like 1": layout 1, the side column) and built on 2026-10-12: the four layouts as self-contained mockups in three states each, with their screenshots at his screen's size and at three others. *Kept here since 2026-10-12, the maintainer's 106 b* |
| [`map-at-any-count-2026-10-13`](map-at-any-count-2026-10-13/README.md) | The map at any number of conversations, asked for and built 2026-10-13, and the four questions a tall map left, answered that day ("164 c", "165 a", "166 a" and "167 a"): the build's screenshots at three to forty rows in Firefox, one in Chromium, the direction not taken that could be drawn, and the rig that took them and timed the page |
