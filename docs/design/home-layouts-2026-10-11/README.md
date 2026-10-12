<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# The home's four layouts, 2026-10-11

**Chosen 2026-10-11 by the maintainer, 61, in his words verbatim: _"61 I like 1"_**: layout 1,
the side column, built on 2026-10-12. The decision's rows are the section of
[`DECISIONS.md`](../../../DECISIONS.md#the-home-its-cpu-and-memory-and-the-after-update-notice-decided-2026-10-11)
headed *The home, its CPU and memory, and the after-update notice, decided 2026-10-11*;
this directory is what he chose from. The layouts were made for his screen that day,
Firefox maximised to the bottom half of a rotated side screen, a viewport of about 1440 by
1170.

**Kept here since 2026-10-12, the maintainer's 106, in his words verbatim: _"106 your
recommendation"_**, which was b: the three sets of mockups of 2026-10-11 are kept in
`docs/design/` with their screenshots, this one, [the map's four variants](../map-variants-2026-10-11/README.md)
and [the page's five directions](../dashboard-2026-10-11/README.md).

| Path | What it is |
|---|---|
| [`layouts/1-side-column/`](layouts/1-side-column/index.html) | **Layout 1, side column, the one he chose**: the headline shares a band with the after-update notice; a main column holds *Right now*, with its five counts and the button to the conversations, above the CPU and memory card, and a side column holds the update, the apps, the storage and the install |
| [`layouts/2-two-tiers/`](layouts/2-two-tiers/index.html) | Layout 2, two tiers: *Right now* beside the CPU and memory card, and the other four cards in a row under them |
| [`layouts/3-board/`](layouts/3-board/index.html) | Layout 3, board: three columns of rows, *Right now*, the CPU and memory, and BrowserAI, which holds what the other four cards hold |
| [`layouts/4-hero-band/`](layouts/4-hero-band/index.html) | Layout 4, hero band: *Right now* as a band of large counts across the top, the CPU and memory card under it, and the other four cards in a row |
| `shots/<layout>/` | Each layout at 1440 by 1170 in dark and in light, dark at 1920 by 1080, and dark at 390 by 844, its first screen and the whole page; at 1440 by 1170 also with the after-update notice in both schemes, and dark on a busy afternoon |

**Each layout is one self-contained `index.html`**, in three states of one invented
afternoon: calm, the default; `?state=notice`, with the after-update notice; and
`?state=busy`, with a window on the screen and an update waiting. `?theme=dark` or
`?theme=light` picks a scheme and `?still` stops the made-up live figures. The stylesheet
and the icons are the product's of that day, read from `PageAssets.Css` and
`PageIcons.Paths`, the markup has the shape `OverviewContent` and `PageContent` emitted,
each layout's own rules follow the page's, and every name, title, folder, size and number
is invented.

**The copies are the scratch files of 2026-10-11, byte for byte.** The rule for copying them
was to put a straight character wherever a mockup used a typographic one; these pages were
written with straight characters only, so no character needed replacing, and the
repository's character and wording scans read them like every other text file. Left out:
the generator and the screenshot scripts in `build/`, with the stylesheet the generator put
into each page, and the preview server's log and pid file.
