<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# BrowserAI application icon -- ten candidates

2026-09-15. Scratch only: everything here is under `.work/2026-09-15-icons/`, which is gitignored.
Nothing was written to the repository tree, to an install root, to the registry or to `~/.claude.json`.

> ⚠️ **Added 2026-09-16, and the paragraph above is now false in its first half.**
> This file and what it describes were moved into the repository when the scratch
> directory was wiped; the SVG masters, both contact sheets, `render.mjs` and
> `Make-Ico.ps1` are beside it, the forty per-size rasters and the two proof
> `.ico` files were not retained, and every absolute `.work\...` path below is
> where a thing *was*. The second half still holds: nothing was written to an
> install root, to the registry or to `~/.claude.json`. The body is left exactly
> as it was written -- see [`README.md`](README.md) for what is here now.

These are candidates for `BrowserAI.exe`, the native config app -- so the chosen one ends up in the
Start Menu, the Velopack installer, Add/Remove Programs, the window title bar and the taskbar. That
is why every candidate is shown at 16, 32 and 48 px beside its 256 px master, on both a light and a
dark sheet: Windows draws the taskbar icon over both.

- Contact sheets: `contact-sheet.png` (light), `contact-sheet-dark.png` (dark)
- Masters: `svg/candidate-01.svg` ... `svg/candidate-10.svg`
- Renders: `png/candidate-NN-{256,048,032,016}.png`
- Proof icon: `candidate-01.ico`

## Licensing -- all ten

**Every design here is original work, drawn from scratch as SVG primitives (`rect`, `circle`,
`path` with line/arc/quadratic segments, `linearGradient`, `clipPath`) in this session. No design
traces, copies or evokes a third-party mark.** Specifically: no Chrome / Chromium / Edge / Firefox
/ Safari logo or its colour ring, no Playwright mask, no Anthropic or Claude mark, no Windows logo,
no Model Context Protocol mark, and no emoji or other glyph taken from a font. The only lettering
is candidate 07's "B", and it is a hand-built path of straight segments and semicircular arcs with
no typeface behind it -- so there is no font licence in the chain and no rasterised glyph to trace
back. There are no external references (`<image>`, `@import`, web fonts) and no `<text>` elements
in any file, so each SVG is self-contained and renders identically anywhere. The icons are
therefore unencumbered and can ship under whatever licence BrowserAI itself ships under.

## Renderer

**Headless Chromium 1243 (`chrome-win64\chrome.exe` from the machine's existing
`%LocalAppData%\ms-playwright` cache), driven by `playwright-core` 1.63.0-alpha-2026-08-05 from
`C:\Source\SixFive7\BrowserAI\.work\probe2\node_modules\playwright-core`, on Node 26.7.0.**
Nothing was downloaded; no browser was installed; the product's own browsers root was not touched.
`playwright-core` expects revision 1237 and the cache holds 1243 -- passing `executablePath`
explicitly bypasses the revision check and 1243 drives fine.

Why not the alternatives: ImageMagick is **not** installed (the `convert.exe` that answers on `PATH`
is `C:\Windows\system32\convert.exe`, the FAT-to-NTFS converter -- a trap worth knowing about);
`rsvg-convert` and `inkscape` are absent; Python has Pillow but no `cairosvg`. Chromium's own
`--screenshot` flag was tried first and **hung holding the caller's pipe without ever writing a
file** -- the renderer child keeps the inherited stdout handle open, so the call never sees EOF. That
tree had to be killed by PID. Driving Chromium over CDP through `playwright-core` has none of that
behaviour.

### The exact command that turns one SVG into the four PNGs

```
node C:\Source\SixFive7\BrowserAI\.work\2026-09-15-icons\render.mjs candidate-07
```

The argument is an optional stem filter; omit it to render all ten. It writes
`png\candidate-07-256.png`, `-048`, `-032` and `-016`, each rendered **natively at that size** by
Chromium's own vector rasteriser (not downscaled from the 256), with `omitBackground: true` so the
corners stay transparent, and `deviceScaleFactor: 1` so 16 means 16.

If you run it from a script instead of a terminal, launch it with `CreateNoWindow = $true` -- that
is what this session did, and it is the house rule for every process this project starts:

```powershell
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName  = 'C:\Program Files\nodejs\node.exe'
$psi.Arguments = '"C:\Source\SixFive7\BrowserAI\.work\2026-09-15-icons\render.mjs" candidate-07'
$psi.UseShellExecute = $false
$psi.CreateNoWindow  = $true
[System.Diagnostics.Process]::Start($psi).WaitForExit()
```

### Rebuilding the contact sheets

```
python C:\Source\SixFive7\BrowserAI\.work\2026-09-15-icons\make_sheet.py
```

Pillow, reading `png\`. The numerals and labels on the sheets use Segoe UI from
`C:\Windows\Fonts` -- allowed because the sheet is a preview for one person and is never shipped.
**No candidate SVG uses a font.**

---

## The candidates

### 01 -- Window & spark  *(flat tile, indigo + amber)*

- **Metaphor.** A browser window with one active tab, and a spark in the content area: the page is
  being acted on by something intelligent. The small second spark is the "and it keeps going" beat.
- **Palette.** Indigo gradient tile `#4F46E5 → #1E1B4B`, near-white window `#F7F8FF`, periwinkle
  chrome bar `#A5B4FC`, amber spark `#FDE68A → #F59E0B`.
- **Why it is logical.** It says *browser* and *AI* in the two shapes everybody already reads, with
  no explanation needed. It is the safest choice and the most obviously an app icon.
- **At 16 px.** Strongest of the ten. The tile, the pale window and one amber dot survive; the tab,
  the two chrome dots and the small spark all vanish, which costs nothing.
- **Licensing.** Original; the spark is four quadratic curves, not an emoji or a font glyph.

### 02 -- Pointer on a circuit  *(line-art, monochrome graphite on bone)*

- **Metaphor.** The agent's cursor, with the traces and pads of a board running off its shoulder --
  a machine is holding the mouse.
- **Palette.** Bone `#EFEDE7` tile, single graphite `#1F2933`. **Monochrome-friendly (1 of 3):** the
  whole design is one colour and works as a flat silhouette.
- **Why it is logical.** BrowserAI's entire job is that a program, not a person, is doing the
  clicking. This is that sentence as a picture.
- **At 16 px.** Weak-to-moderate. The arrow survives; the three nodes fuse into a smear on its right
  flank. Consider dropping the lowest trace if this one is chosen.
- **Licensing.** Original; the arrow is my own 7-point polygon, not a system cursor bitmap.

### 03 -- Globe with a reading eye  *(gradient, emerald → teal)*

- **Metaphor.** The web, being *read*. The meridians place it as the internet; the eye in the middle
  is the agent looking at the page, not a person.
- **Palette.** `#34D399 → #10B981 → #0E7490` sphere, `#ECFEFF` wireframe at 45 %, `#F0FDFA` sclera,
  `#083344` pupil.
- **Why it is logical.** `browser_snapshot` and `browser_take_screenshot` are the two things this
  server does most. This is an icon about perception, which is the honest emphasis.
- **At 16 px.** Moderate. It collapses to a green disc with a dark centre -- still distinctive in a
  taskbar, but the eye reads as a dot, not an eye. The wireframe is gone by 32 px.
- **Licensing.** Original; no globe clip-art, no browser-vendor colour ring.

### 04 -- The tab that is a speech bubble  *(flat, plum + coral + cream)*

- **Metaphor.** A page whose tab has become a speech bubble with its tail dipped into the document:
  the browser is answering, not just displaying.
- **Palette.** Plum gradient tile `#5B1A46 → #2A0A20`, cream page `#FDF6E3`, coral bubble `#FF6B5B`,
  muted mauve text bars `#C7A3B6`.
- **Why it is logical.** BrowserAI is an **MCP server** -- a conversational surface over a browser.
  This is the only candidate that puts the conversation in the metaphor.
- **At 16 px.** Moderate. A coral bar over a cream bar on dark. Distinctive by colour, but the three
  dots and the tail are gone and it stops reading as speech.
- **Licensing.** Original; the bubble is one hand-written path, the dots are circles.

### 05 -- Compass  *(warm geometric, copper on cream)*

- **Metaphor.** Navigation, plainly. `browser_navigate` is the first call any session makes.
- **Palette.** Cream `#FAF3E3` tile, copper ring gradient `#F59E0B → #92400E`, needle in `#B45309`
  and `#3B2412`, horizontal needle `#D97706`.
- **Why it is logical.** It is the one candidate that is warm, not cool, and the only one
  that would not look out of place beside a file manager or a terminal. It reads as a *tool*.
- **At 16 px.** **Weakest of the ten** and I would not pick it for that reason alone. The ring
  becomes a 1 px circle and the needle a speck; the four ticks disappear entirely. It is beautiful
  at 256 and mush at 16. If it is chosen, it needs a hand-tuned 16 px variant with the ring dropped.
- **Licensing.** Original; no compass rose traced from anything.

### 06 -- Robot behind a visor  *(character flat, slate + sky)*

- **Metaphor.** A robot's face where the visor is a browser window -- chrome bar, three window dots,
  and the pupils looking out through it.
- **Palette.** Slate head gradient `#3A4A61 → #0F172A`, sky visor `#38BDF8`, deep chrome bar
  `#0C4A6E`, ice highlights `#E0F2FE`.
- **Why it is logical.** It is the only candidate with a *face*, which is what makes it memorable in
  a Start Menu list of grey tool icons. And the window-as-visor is exactly what the product is: the
  agent sees the world through a browser.
- **At 16 px.** Strong -- the blue band and two pupils survive and it still reads as a face. **But:**
  the head is dark and the icon has no tile, so on a dark taskbar the silhouette's edge is soft and
  the icon reads as a floating blue band. Acceptable, but it is the one dark-on-dark risk here.
- **Licensing.** Original; not derived from any existing robot or assistant mark.

### 07 -- Monogram "B" of browser panes  *(negative space, rose)*

- **Metaphor.** A "B" whose two lobes are stacked browser panes -- square-cut left edge like a window
  frame, semicircular right caps -- under a detached chrome bar with its three window dots.
- **Palette.** One colour: rose `#E11D48` with everything else knocked out in `#FFF1F2`.
  **Monochrome-friendly (2 of 3):** it is literally two colours and works as a stencil.
- **Why it is logical.** It is the only candidate that would work as a *brand* and not only an
  app icon -- favicon, README badge, GitHub avatar -- and it stays legible when someone renders it in
  one colour on a wiki.
- **At 16 px.** Moderate-to-good. The chrome bar and the B both survive as forms, but the counters
  nearly close and it tips toward "pink square with a pale blob". Widening the counters by ~4 px
  would fix it if this wins.
- **Licensing.** Original. The "B" is `h`/`a`/`z` path data written by hand -- **no typeface was used
  and no glyph was rasterised**, which is the point of drawing it instead of setting it.

### 08 -- Lens over a page  *(duotone, ink + magenta)*

- **Metaphor.** A magnifying lens over a document, and what the lens shows is not bigger text but
  *different* text -- the page as the machine reads it.
- **Palette.** Ink tile `#0B1020`, white page `#F8FAFC`, slate content bars `#94A3B8`, magenta lens
  `#DB2777` with `#F472B6` inside.
- **Why it is logical.** This is the accessibility-tree snapshot, which is the single most
  characteristic thing BrowserAI returns: the human sees a page, the agent sees a structure.
- **At 16 px.** Good. Pale page plus a magenta dot bottom-right; the structure survives even though
  the magnified bars do not.
- **Licensing.** Original; the lens is a circle, a ring and a capped line.

### 09 -- Caret in a tab frame  *(mono terminal, phosphor green)*

- **Metaphor.** A browser window frame with a real tab on it, containing a shell prompt: a chevron
  and a block caret. The browser as something you *command*.
- **Palette.** Near-black `#08120B` tile, one phosphor green `#22C55E`. **Monochrome-friendly (3 of
  3):** a single ink, and it works as a silhouette.
- **Why it is logical.** BrowserAI is a developer tool that a coding agent speaks to over stdio. Of
  the ten, this is the one that admits that and looks like it belongs next to a terminal.
- **At 16 px.** Moderate. The green frame and the tab survive; the chevron and the caret block fuse
  into one smudge inside the frame. Dropping the caret block for the 16 px variant would fix it.
- **Licensing.** Original; the chevron is a two-segment stroked path, not a `>` from a font.

### 10 -- Hand pointer on a node graph  *(duotone, violet + lime)*

- **Metaphor.** The hand cursor that appears over a link, next to a small node graph -- a pointer
  driven by something that plans.
- **Palette.** Violet gradient tile `#7C3AED → #3B0764`, cream hand `#FFF7ED`, lime graph `#BEF264`
  and `#A3E635`.
- **Why it is logical.** The hand cursor is the universal sign for "this is clickable", and the
  graph beside it is who is doing the clicking. Also the most colourful of the ten, which matters in
  a crowded taskbar.
- **At 16 px.** Moderate. The pale hand reads; the graph reduces to two or three lime specks.
- **Caution, and it is why this one went through three drafts.** A raised finger is easy to get
  wrong. The first two drafts read as an obscene gesture at 256 px and were discarded; this version
  has a single raised index at the *left* of a domed fist, a thumb bump on the left, and two short
  knuckle grooves that stop short of reading as fingers. **Look at it once at full size before
  choosing it** -- if it is even slightly ambiguous to you, it will be to somebody.
- **Licensing.** Original; built from rounded rectangles, not traced from any cursor set or emoji.

---

## Shortlist

Best at 16 px, which is where an app icon actually lives: **01**, then **06** and **08**.
Most distinctive as a mark: **07**. Most honest about what the product is: **08** or **09**.
Prettiest at 256 and worst at 16: **05**.

---

## The `.ico` pipeline

`Make-Ico.ps1` packs the four rendered PNGs of one candidate into a multi-size Windows icon:
16, 32 and 48 as 32-bit BGRA DIB entries (`BITMAPINFOHEADER`, doubled height, bottom-up rows, an
all-zero AND mask -- the alpha channel carries the shape), and **256 as the PNG file verbatim**,
which is the Vista+ PNG-compressed entry and is what keeps the file to ~37 KB instead of ~280 KB.

```
pwsh -NoProfile -File C:\Source\SixFive7\BrowserAI\.work\2026-09-15-icons\Make-Ico.ps1 -Candidate 7 -Verify
```

It was proved on candidate 1 only, as asked -- the other nine have their PNGs but no `.ico`.
`-Verify` re-reads the file it just wrote and lists the directory entries:

```
wrote C:\Source\SixFive7\BrowserAI\.work\2026-09-15-icons\candidate-01.ico (37846 bytes, 4 entries)
reserved=0 type=1 count=4
 16x16  planes=1 bpp=32 bytes=1128    offset=70      payload=BMP/DIB (biSize=40)
 32x32  planes=1 bpp=32 bytes=4264    offset=1198    payload=BMP/DIB (biSize=40)
 48x48  planes=1 bpp=32 bytes=9640    offset=5462    payload=BMP/DIB (biSize=40)
256x256 planes=1 bpp=32 bytes=22744   offset=15102   payload=PNG
System.Drawing round-trip at 32: 32x32
```

The 256 entry records `0x00` for width and height, which is how the ICO format spells 256 -- that is
correct, not a defect. Verified a second time with an independent reader (Pillow), which reports
`sizes [(16,16),(32,32),(48,48),(256,256)]`, transparent corners on the 32 (`alpha=0`) and amber
`(250,210,103,255)` at its centre, so the alpha and the channel order are both right.

**One bug, recorded because it produced a file that looked plausible:** returning the DIB
buffer from a PowerShell function as `return $ms.ToArray()` lets the pipeline unroll the byte array
into 1,128 separate objects. `.Length` still read 1128, so the directory entries were all correct
and only the payload was short -- the first attempt wrote a 22,817-byte file whose entries claimed
37,776 bytes of data. `return ,$ms.ToArray()` is the fix.

## What I could not do

- **I could not check these against the real Windows shell** -- no icon was installed, no `.ico` was
  registered, and nothing was written outside this scratch directory, so the 16 and 32 px renders
  here are Chromium's rasteriser, not Windows' own icon scaler. They will be close but not
  identical; the winner should be looked at once in a real Start Menu before it ships.
- **I did not build the other nine `.ico` files**, by instruction; `Make-Ico.ps1 -Candidate N` does
  it for any of them the moment one is chosen.
- **I did not hand-tune any 16 px variant.** Every 16 px render here is the 256 px master scaled by
  the rasteriser, so candidates 02, 05 and 09 are being judged at their worst; each of their notes
  says what a tuned variant would drop.
