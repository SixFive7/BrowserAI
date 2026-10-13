<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# The map at any number of conversations, 2026-10-13

**Asked for by the maintainer on 2026-10-13, in his words verbatim: _"Feature request. Right now
I see "There are too many conversations and sessions to draw as a map, so they are listed.". Can
we make it so that this still draws on a map. The page can become longer (not wider) if need
be."_** The map was built to be drawn at every count the same day, and the four questions a tall
map left open were put to him with these screenshots, as 164 to 167. He answered, in his words
verbatim: _"164 c"_, _"165 a"_, _"166 a"_ and _"167 a"_.
[The decision's rows](../../../DECISIONS.md#the-map-at-any-number-of-conversations-decided-2026-10-13)
say what each direction was, what was built and which tests hold it; this directory is what he
chose from.

| Path | What it shows |
|---|---|
| [`shots/firefox-03-rows-full-light.png`](shots/firefox-03-rows-full-light.png) | Three rows and five sessions, the whole page: the map as it was before, BrowserAI's card in the middle of its column |
| [`shots/firefox-16-rows-full-light.png`](shots/firefox-16-rows-full-light.png) | Sixteen rows and twenty-five sessions, the whole page: past both of the old limits, eight rows and twelve sessions, where the page used to show the list |
| [`shots/firefox-40-rows-1-top-light.png`](shots/firefox-40-rows-1-top-light.png) | Forty rows and sixty-three sessions, the first window as the page opens: BrowserAI's card at the top of its column, 164 c |
| [`shots/firefox-40-rows-2-middle-chosen-light.png`](shots/firefox-40-rows-2-middle-chosen-light.png) | Scrolled to the middle with a session there chosen: BrowserAI's card and the inspector stay at the top of the window, the inspector pointing at the card, 164 c and 165 a |
| [`shots/firefox-40-rows-2-middle-chosen-dark.png`](shots/firefox-40-rows-2-middle-chosen-dark.png) | The same window in the dark setting |
| [`shots/firefox-40-rows-3-bottom-light.png`](shots/firefox-40-rows-3-bottom-light.png) | Scrolled to the bottom, the kept sessions' rows: BrowserAI's card still in view, and the legend and the column heads scrolled away, 167 a |
| [`shots/firefox-40-rows-4-middle-browserai-chosen-light.png`](shots/firefox-40-rows-4-middle-browserai-chosen-light.png) | BrowserAI chosen, scrolled to the middle: the column ringed, its card's tab reading *Selected*, and the inspector pointing at the card |
| [`shots/firefox-40-rows-not-taken-card-in-the-middle-top.png`](shots/firefox-40-rows-not-taken-card-in-the-middle-top.png) | 164 b, not taken: BrowserAI's card left in the middle of its column, so the first window shows an empty column |
| [`shots/firefox-16-rows-narrow-1100-list.png`](shots/firefox-16-rows-narrow-1100-list.png) | Sixteen rows in a window 1,100 pixels wide: the list, as before, since the map needs 1,200 |
| [`shots/chromium-40-rows-1-top-light.png`](shots/chromium-40-rows-1-top-light.png) | The first window of forty rows in Chromium |
| [`tools/TallMapShots.cs`](tools/TallMapShots.cs) | The rig they were taken with, which also timed the script's drawing at forty, a hundred and two hundred rows |

**How they were taken.** The product's composers and stylesheet, served by the suite's page rig
from a scratch data root with invented conversations and sessions, `ConversationsMapTests.Rows`,
and captured headless through BrowserAI's own tools at 1440 by 1170, the window
[the home's layouts](../home-layouts-2026-10-11/README.md) were made for, in the provisioned
`chromium-1247` and `firefox-1553`. The rig ran twice:
the first run's Chromium pictures showed the column with no card behind it, because Chromium
drew no background for a glow sized with `min()`, and they are not kept; the second run, from
which every picture here comes, drew the glow at a fixed height. The rig took the same set in both
engines; Firefox's is kept because it is the maintainer's browser. What the drawing cost is in
[the kb](../../../kb/windows/loopback-page.md#the-conversations-pages-map-at-forty-a-hundred-and-two-hundred-rows----measured-2026-10-13).

**The directions with no picture** were described to him and not drawn: 164 a, one point every
line fans into; 164 d, the card held in the middle of the window; 164 e, the card again beside
each group of rows; 165 b and c, an inspector beside the card chosen or one with a way back to
it; 166 b to d, a cap past 200 rows, a way to draw the rest, or a filter; and 167 b and c, the
legend held under the top bar or the column heads drawn again.
