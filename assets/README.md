<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# assets

**Every file here is original work and carries no third-party mark and no font
glyph.** [`icon.svg`](icon.svg) is the master — a globe with a reading eye,
candidate 3 of the ten drawn on 2026-09-15 and the one the maintainer chose on
2026-09-16 (Q196) — drawn from scratch as SVG primitives: `circle`, `path` with
quadratic segments, `linearGradient` and `clipPath`. There is no `<text>`
element, no `<image>`, no `@import` and no web font in it, so it is
self-contained, renders identically anywhere, and there is no typeface in its
licence chain. It evokes no browser vendor's mark, no Playwright mask, no
Anthropic or Claude mark, no Windows logo and no Model Context Protocol mark.

| File | What it is | How it was made |
|---|---|---|
| [`icon.svg`](icon.svg) | The master, 256×256 | Hand-written |
| [`BrowserAI.ico`](BrowserAI.ico) | What both executables, the Setup stub, the Add/Remove entry and the Start Menu shortcut carry | 16/32/48 as 32-bit BGRA `BITMAPINFOHEADER` entries and 256 as the PNG file verbatim, packed by `.work/2026-09-15-icons/Make-Ico.ps1` |
| [`icon-256.png`](icon-256.png) | The master rasterised | Headless Chromium, natively at that size |
| [`icon-128.png`](icon-128.png) | What [`../README.md`](../README.md) shows beside its title | The same, at 128 |
| [`social-preview.png`](social-preview.png) | 1280×640, for the repository's **Social preview** setting | The same pipeline, with the icon and one line of text |

**Each raster is rendered natively at its own size rather than downscaled from
the 256**, by Chromium's own vector rasteriser, with `omitBackground` so the
corners stay transparent and `deviceScaleFactor: 1` so 16 means 16. The renderer
is `.work/2026-09-16-icon/render-assets.mjs`, which drives the Chromium already
in this machine's `%LocalAppData%\ms-playwright` cache through `playwright-core`
— nothing is downloaded and the product's own browsers root is not touched.

⚠️ **The social preview is the one file here with lettering in it**, and the
lettering is rasterised system text rather than a path: the card asks for
`'Segoe UI', system-ui, sans-serif` and keeps whatever Chromium resolved. No font
file is redistributed and no glyph is traced, but it is the one asset whose look
depends on the machine that rendered it. The icon itself has no lettering at all
and that property is the point of it.

**What enforces what.** `ReleaseScriptTests.TheShippedIconIsTheOneTheMaintainerChose`
holds the `.ico`'s directory shape — four entries, 16/32/48 as 32-bit DIBs and
256 as a PNG — and that `icon-256.png` really is 256×256;
`DocumentationLinkTests.EveryImageReferenceResolvesToTheAssetItNames` holds that
every relative image reference in the repository points at a file that is here.
**Nothing checks that the `.ico` and `icon.svg` are the same drawing** — that
would be a render comparison on every build — so
[the pre-cut item](../RELEASING.md#7-build-clean) is a line a person reads, and
the SPDX header on `icon.svg` is a habit rather than a mechanism, because the
header scan's corpus is this repository's prose kinds and an `.svg` is not one.
