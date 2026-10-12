<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# The map's lines, four variants, 2026-10-11

**Chosen 2026-10-11 by the maintainer, 60, in his words verbatim: _"60 I like C"_**, and
built on 2026-10-12. [The decision's rows](../../../DECISIONS.md#the-maps-lines-the-session-page-and-what-is-new-decided-2026-10-11-and-2026-10-12)
say what was built and which tests hold it; this directory is what he chose from. His words
that asked for the variants, verbatim: *"But the visual line differences between: Window on
your screen / Hidden browser / No browser open / Kept, no conversation / Are very difficult to
see. Use more different lines and colors here. But keep it in style and pretty. Also, it is
not clear you can select the browser session. Communicate that better."*

**Kept here since 2026-10-12, the maintainer's 106, in his words verbatim: _"106 your
recommendation"_**, which was b: the three sets of mockups of 2026-10-11 are kept in
`docs/design/` with their screenshots, this one, [the home's four layouts](../home-layouts-2026-10-11/README.md)
and [the page's five directions](../dashboard-2026-10-11/README.md).

| Path | What it is |
|---|---|
| [`a-clear-lines/`](a-clear-lines/index.html) | Variant A, clear lines: four line types with an end mark each, and a chevron on every card |
| [`b-stations/`](b-stations/index.html) | Variant B, stations: an icon on every line, and the lines of the card under the pointer light up |
| [`c-named-lines/`](c-named-lines/index.html) | **Variant C, named lines, the one he chose**: each line carries its state in a word, and each card a Details tab |
| [`d-state-cards/`](d-state-cards/index.html) | Variant D, state on the card: a glowing, a hollow, a dashed and a dotted line, a matching socket on each card, and memory live on each session card |
| `*/shots/` | Each variant at 1440 by 900 in light and in dark and with the pointer on a card, and its inspector at 360 px wide; B and D also at 1,200 px wide; and B with a conversation chosen, with the keyboard's focus on a card, and its inspector for a session whose browser is closed and for one that holds traces |
| [`drafts/`](drafts/c-draft-1.png) | Earlier renderings of the variants, made before the four above: two each of B, C and D, and one of A in light and one in dark |

**Each variant is one self-contained `index.html`**: the page's stylesheet as `PageAssets.Css`
stood at `801e21af`, the icons of `PageIcons.cs` that day, the conversations
page's markup as `ConversationsContent` composed it, and invented data. Open it in a browser;
it loads nothing from anywhere else and follows the browser's light or dark setting. It opens
with one session chosen, and `?select=none` opens it with nothing chosen. Each variant changes
only how the four line states are drawn and how a card says it can be chosen.

**Where the build departs from variant C** is in the decision's rows and in the build's
choices under them: a conversation's half of its line is a strand per state, the point where
differing lines leave BrowserAI is neutral, the map's narrowest columns are 696 px where the
variant's were 706, and the inspector's note on deleting keeps the dropped session page's
words.

**The copies are the scratch files of 2026-10-11, byte for byte.** The rule for copying them
was to put a straight character wherever a mockup used a typographic one; these pages were
written with straight characters only, so no character needed replacing, and the
repository's character and wording scans read them like every other text file. Left out: the
generator in `tools/`, which read the product's source of that day, and the preview server's
log and pid file.
