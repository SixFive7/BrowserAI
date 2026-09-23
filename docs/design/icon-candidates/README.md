<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Icon candidates -- 2026-09-15

The ten designs drawn for `BrowserAI.exe` on 2026-09-15, and the two contact
sheets they were judged on. **Candidate 3 -- the globe with a reading eye -- is
the one the maintainer chose on 2026-09-16 (Q196)**, and it ships as
[`assets/icon.svg`](../../../assets/icon.svg); what that master is packed and
rasterised into is in [`assets/README.md`](../../../assets/README.md). The other
nine are kept because a choice with nothing beside it is not a choice anybody
can revisit.

| File | What it is |
|---|---|
| [`NOTES.md`](NOTES.md) | The session's own record: what each candidate is, how it reads at 16 px, the renderer, and a per-candidate licensing line |
| [`svg/candidate-01.svg`](svg/candidate-01.svg) ... [`svg/candidate-10.svg`](svg/candidate-10.svg) | The ten masters, 256×256 |
| [`contact-sheet.png`](contact-sheet.png), [`contact-sheet-dark.png`](contact-sheet-dark.png) | All ten at 256/48/32/16, light and dark -- Windows draws the taskbar icon over both |
| [`render.mjs`](render.mjs) | Rasterises every candidate at every size through headless Chromium |
| [`Make-Ico.ps1`](Make-Ico.ps1) | Packs one candidate's PNGs into a multi-size `.ico`; it is what produced [`assets/BrowserAI.ico`](../../../assets/BrowserAI.ico) |

**Licensing -- all ten.** Every design here is original work, drawn from scratch
as SVG primitives in one session. No design traces, copies or evokes a
third-party mark: no browser vendor's logo or colour ring, no Playwright mask,
no Anthropic or Claude mark, no Windows logo, no Model Context Protocol mark,
and no emoji or glyph taken from a font. The only lettering anywhere is
candidate 7's "B", a hand-built path of straight segments and semicircular
arcs with no typeface behind it. No SVG here references anything external --
no `<image>`, no `@import`, no web font, no `<text>` -- so each is
self-contained and renders identically anywhere. The per-candidate detail is in
[`NOTES.md`](NOTES.md#licensing----all-ten).

⚠️ **What was not retained, and why.** The forty per-size rasters
(`png/candidate-NN-{016,032,048,256}.png`) and the two proof `.ico` files that
`NOTES.md` describes were left in the scratch directory: they are outputs of
[`render.mjs`](render.mjs) and [`Make-Ico.ps1`](Make-Ico.ps1) over the SVGs
beside them, and `HouseRuleTests.NoTextFileInTheTreeCarriesAControlByte` caps
how many binary files this tree may hold. Re-render them rather than look for
them. The contact sheets are kept because they are the artefact the decision
was actually made on, and nothing in the tree reproduces them.
