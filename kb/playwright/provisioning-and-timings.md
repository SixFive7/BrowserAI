<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Payload sizes, first-run provisioning and timings

**Versions in force** unless an entry says otherwise: `@playwright/mcp` 0.0.79 · `playwright-core` 1.63.0-alpha-2026-08-05 · Chrome for Testing 152.0.7977.8 (`chromium-1237`) · Firefox 153.0 (`firefox-1539`) · `ffmpeg` revision 1011 · `winldd` revision 1007 · Node v24.19.0 LTS · Windows 11 Pro 26200.

⚠️ **That line is the baseline the OLDEST entries here were taken at, and it is left standing as one -- *added by addition 2026-09-21, the fourth roll since*.** What the tree resolves today, read from [the payload lock](../../build/payload/package-lock.json) and [the committed `browsers.json` snapshot](../../upstream-snapshots/browsers.json), not from memory: `@playwright/mcp` **0.0.82** · `playwright-core` **1.64.0-alpha-1789764292000** (epoch milliseconds and not a date, and nothing here parses it as one) · Chrome for Testing **154.0.8037.0** (`chromium-1246`) · Firefox **156.0** (`firefox-1549`) · `ffmpeg` revision **1011** · `winldd` revision **1007** · Node **v24.21.0** LTS. **Every dated entry below states the versions it was taken at**, which is what makes this a baseline, not a claim about any of them; an entry with no versions of its own was taken at the line above.
Measured on [the reference machine](../README.md#the-reference-machine).

## Component sizes

Measured during the 2026-08-13 research. Every row is a version-specific artifact
size, so every row is `[FLOATS]`.

| Component | Version / revision | Size |
|---|---|---:|
| `node.exe` | v24.19.0 LTS | 88.53 MB |
| `@playwright/mcp` + `playwright-core` tree | 0.0.79 | 18.11 MB |
| `chromium-headless-shell` | rev 1237 | 268.49 MB |
| `ffmpeg` | rev 1011 | 3.35 MB |
| `winldd` | rev 1007 | 0.25 MB |
| full `chromium` | rev 1237 (152.0.7977.8) | 426.88 MB |

**The six rows above total ~806 MB installed, ~239 MB compressed** -- 7z LZMA2
`-mx=5`. That is the figure for a **bundled** build, browsers inside the
installer, and it excludes BrowserAI's own binary. **Nothing ships in that shape
today.** Browsers are [provisioned on first run](#first-run-provisioning), so the
installed payload is what `current\` holds, and it has now been **weighed and not
added up**: an installed `current\` measures **130,434,952 B = 124.39 MiB
across 200 files** (`BrowserAI.exe` 17,853,952 · `payload\` 111,984,018 ·
`BrowserAI.xml` 596,517 · `sq.version` 465) -- **466 B more** than the *packaged*
130,434,486 in [row 85](../re-verification.md), which is not a
discrepancy: 465 of those bytes are `sq.version`, which Velopack writes into
`current\` at install and which the package does not carry. **Disk after first
run was 130,434,952 + 451,389,780 =
581,824,732 B = 554.87 MiB ≈ 582 MB**, and that sum is `[STALE]` since
2026-09-17: **both addends have moved and neither was re-measured into it.**
Chromium's term is `chromium-1237`; the family is at **1245** and weighs
**454,699,952 B across 308 files**, measured 2026-09-17 on the reference
machine -- 3,310,172 B more. The `current\` term is the **one-executable**
layout of 2026-08-17, and since 2026-09-15 an install holds **two** binaries
plus a second XML, so the figure it names is not the directory the sentence
describes. **It is left exactly as it was measured and not adjusted**, which
is what this marker exists instead of: a derived total carries no date of its
own, which is the failure the 2026-08-17 correction below already records
against the previous version of this same sentence -- the same defect, in the
replacement for it.

> ✅ **RE-DERIVED 2026-09-17, hours later, from two addends measured that day and
> nothing else.** The `1.0.0` release cut that afternoon installed itself on the
> reference machine, so `current\` was weighed as the thing the sentence
> describes and not reconstructed: **143,574,278 B across 207 files**
> (`BrowserAI.Server.exe` 19,210,752 · `BrowserAI.exe` 10,412,544 ·
> `payload\` 112,410,768 across 200 files · `BrowserAI.Server.xml` 1,045,834 ·
> `BrowserAI.Core.xml` 386,380 · `BrowserAI.xml` 84,908 ·
> `THIRD-PARTY-NOTICES.txt` 22,610 · `sq.version` 482). With
> `chromium-1245` at 454,699,952 B, **disk after first run is 143,574,278 +
> 454,699,952 = 598,274,230 B = 570.56 MiB ≈ 598 MB.** Both terms were measured
> within hours of each other on one machine, which is the property the stale sum
> lacked; the stamp above is left standing as the record of what the debt was.
> **It will go stale again on the next build**, because the `current\` term moves
> with every publish -- that is a property of a derived total and not a defect in
> this one.

The ~806 MB total is kept
because a bundled build is the fallback if the Chrome-for-Testing redistribution
question is ever resolved favourably -- but it is **not** the figure for disk
after first run, as this file and the charter both once said: it counts
`chrome-headless-shell` (268.49 MB), which is not provisioned at all.

> ⚠️ **Corrected 2026-08-17 (previously "the installed payload is `node.exe` +
> the JS tree + BrowserAI = **116.40 MiB** (88.53 + 18.11 + 9.76), and disk after
> first run is 116.40 + 430.48 = **546.88 MiB ≈ 573 MB**").** The arithmetic was
> right and one of its three terms had gone stale: `9.76` was the **2026-08-15
> spike** binary, taken before the proxy had sessions, artifact routing,
> provisioning, a sweeper, an update lane or a registrar in it, and the number
> outlived the artifact by two days and roughly 7 MiB. **This is the failure the
> floats-and-re-verify convention exists to catch and did not**, because a
> derived total carries no date of its own: the sum read as current while one
> addend was a fortnight old. The replacement is a **weight, not a sum** -- the directory that
> actually ships, measured whole -- so the next stale term cannot hide inside it.
> The old figure is retained above in this note and not deleted, because a
> reader who learned `116.40` needs to find out it was reviewed and replaced.

**BrowserAI's own binary is ~17.0 MiB and it moves on every commit.** Three
publishes present on the machine on **2026-08-17** measured **17,853,952 B**
(the one inside the 0.9.x install above, 17.03 MiB), **17,911,808 B**
(`artifacts/publish-release`) and **17,954,304 B** (the current Release publish)
-- `PublishAot`, win-x64, self-contained, all three. **Do not treat any of them as
*the* size.** The spread across three artifacts of the same product on one day is
the point: this is the most volatile figure in this article, and it floats on
**our own product** and not on an upstream, which is why no marker is stamped
on it -- the checkable figure is the packaged `current\` above, which is a
directory somebody can weigh, and it is carried by
[row 85](../re-verification.md) with the rest of the update lane's
numbers.

> ⚠️ **Corrected 2026-08-17 (previously "**BrowserAI's own binary is 9.76 MiB** --
> 10,233,856 bytes ... measured by [the 2026-08-15 spike](../mcp/sdk.md#driving-the-whole-sdk-aot-passthrough-filters-and-cancellation)").**
> The spike's measurement stays true of the spike and is still recorded there;
> what was wrong was carrying it forward as the product's size. It had already
> been contradicted in this repository without this line being swept -- the
> build order's own step 5 recorded **10,461,696 bytes** the following day --
> which is the same half-done correction the entry it replaced was itself
> written to fix.

The trimmed self-contained fallback at ~70 MB is
still `[UNVERIFIED]`, nothing having been built in that configuration.

> ⚠️ **Where ~380 MB came from, and why it is retired.** The update budget was
> written against a "~380 MB footprint dominated by Chromium", with ~600-700 MB
> transient disk during a swap and a full re-extraction of ~380 MB per update.
> That figure is `806 − 427`: the payload of an intermediate design that had
> dropped full Chromium but still shipped `chrome-headless-shell`. **`current\`
> contains no Chromium of any kind today**, so the number and its "dominated by
> Chromium" description are both dead. Against a ~117 MB payload the swap holds
> old + new `current\` simultaneously, so the transient budget is **~235 MB** and
> a re-extraction is ~117 MB; browsers live outside `current\` and are untouched
> by the swap (verified 2026-08-15,
> [kb: Velopack](../packaging/velopack.md#where-state-may-live----the-finding-the-provisioning-design-rests-on)).
> The **compressed** size of the current payload has never been measured -- the
> ~239 MB above is for the browser-dominated tree, and `node.exe` is now the bulk
> of what is left -- so every full-package download figure remains `[UNVERIFIED]`.

**Verified 2026-08-16 @ Node v24.19.0 / `@playwright/mcp` 0.0.79, by assembling
the payload** ([`build/Build-Payload.ps1`](../../build/Build-Payload.ps1)).
Both rows hold to the byte, and the unit in this table is **MiB** and not MB:
`node.exe` is **92,825,416 B = 88.53 MiB**, and `node_modules` is
**18,993,773 B = 18.11 MiB** -- of which `playwright-core` is 13.18 MiB,
`playwright` 4.85 MiB (the [never-loaded wrapper](tools-and-artifacts.md#the-tool-surface-and-the-package-shape))
and `@playwright/mcp` itself only **0.08 MiB**. Re-establish with
`pwsh -File build/Build-Payload.ps1`, which writes the byte counts into
`payload/payload.json`. `[FLOATS]`

**Node's `LICENSE` is not published beside `node.exe`.** Measured 2026-08-16:
`https://nodejs.org/dist/v24.19.0/win-x64/` lists exactly `node.exe`,
`node.lib`, `node_pdb.7z` and `node_pdb.zip`, and the version root
`https://nodejs.org/dist/v24.19.0/` carries only archives, installers, `docs/`
and the three `SHASUMS256.txt*` files. **There is no standalone `LICENSE` at
either path.** The only route to it is inside an archive:
`node-v24.19.0-win-x64.zip` holds `node-v24.19.0-win-x64/LICENSE` at
**160,552 B** beside the executable. That archive is **37,304,352 B =
35.58 MiB**, so taking the licence route also downloads **~53 MB less** than
fetching the bare `node.exe`. This matters because
[Shipping our own runtime](../../ARCHITECTURE.md#the-runtime-it-ships) requires Node's full
`LICENSE` to ship -- it aggregates the OpenSSL, ICU, V8, zlib and c-ares terms --
and the obvious build, one `GET` of `win-x64/node.exe`, ships no licence at all
and reports nothing. Re-establish by listing both URLs. `[FLOATS]`

**A single `node.exe` drives the full MCP protocol** -- no npm, no `node_modules`
belonging to Node, no `.cmd` shims. Verified by execution. Node **v26 is Current
and not LTS and its `node.exe` is 10 MB larger**. `[FLOATS]`

**The vendored JS tree contains zero native binaries** and is portable as-is.
**It also declares no install script**: verified 2026-08-16 across the resolved
`package-lock.json`, the only `hasInstallScript` entry is `fsevents`, which is
`optional` and `darwin`-only and is therefore never installed on Windows. A
vendoring build that runs `npm install` without `--ignore-scripts` -- which is
what `build/Build-Payload.ps1` does deliberately, so an upstream change is not
suppressed -- currently executes nothing. `[FLOATS]`
**`ffmpeg` is required for video capture** -- without it the `video` artifact type
throws. `[FLOATS]`

## First-run provisioning

> ✅ **Re-measured 2026-09-21 at chromium 1246 and firefox 1549**, on the
> `@playwright/mcp` 0.0.81 -> 0.0.82 roll that retired the dated
> `playwright-core` override. Two clean runs per family through the same rig,
> byte-identical within each pair, plus a `HEAD` on each of the four archives.
> **Chromium did not move, again and for the same reason**: 1246 carries the same
> `browserVersion` 154.0.8037.0 as 1245 and 1244, `cftUrl()` is keyed on the
> version, so the archive, the tree and the wire total are identical to the byte
> and the file for the third roll running. **Firefox moved properly this time**
> - 155.0 -> **156.0** is a new Firefox and not a rebuild - `+1,431,551` B on
> the wire and `+3,458,122` B on disk, and **two files more** instead of the
> same 61. `ffmpeg` **1011** and `winldd` **1007** did not move. The rig is
> [`docs/probes/2026-09-16-provisioning`](../../docs/probes/2026-09-16-provisioning/README.md)
> and the run is
> [`docs/evidence/2026-09-21-provisioning-1246`](../../docs/evidence/2026-09-21-provisioning-1246/README.md).
>
> ✅ **Re-measured 2026-09-17 at chromium 1245 and firefox 1548, and the
> `[STALE]` this section carried for a day is cleared.** The figures below are
> what those revisions produce. **Chromium did not move at all**, and this is
> why: `playwright-core` builds Chromium's URL with `cftUrl()`,
> keyed on `browserVersion` and not on the revision, and 1245 carries the
> **same** `browserVersion` 154.0.8037.0 as 1244 -- so the archive fetched is the
> same archive, the tree is the same tree to the byte and the file, and the only
> thing that changed is the directory it lands in. **Firefox moved by 902 bytes
> on disk and 327 on the wire.** `ffmpeg` **1011** and `winldd` **1007** did not
> move. The rig is
> [`docs/probes/2026-09-16-provisioning`](../../docs/probes/2026-09-16-provisioning/README.md)
> and the run is
> [`docs/evidence/2026-09-17-provisioning-1245`](../../docs/evidence/2026-09-17-provisioning-1245/README.md).

**Re-measured 2026-09-21 by exact `content-length` from the CDN: 207.3 MB
down.** `chrome-win64.zip` 205,733,764 B + `ffmpeg-win64.zip` 1,411,741 B +
`winldd-win64.zip` 128,684 B = **207,274,189 B**, at chromium **1246** /
154.0.8037.0, ffmpeg **1011**, winldd **1007**, under `playwright-core`
1.64.0-alpha-1789764292000 and `@playwright/mcp` 0.0.82. *Verified 2026-09-21 @
chromium 1246 -- **every byte of it is unchanged from the 2026-09-17 reading at
1245 and the 2026-09-16 one at 1244**, because all three archives are the same
archives: the revision moved and `browserVersion` did not, and Chromium's URL is
keyed on the version. Three rolls have now moved this revision and none has moved
this number, which is the pattern and not a coincidence.* *Corrected
2026-09-21 (previously "Re-measured 2026-09-17 ... at chromium **1245** ... under
`playwright-core` 1.64.0-alpha-2026-09-17 and `@playwright/mcp` 0.0.81"): every
figure in the sentence is unchanged and the versions it was taken at are not.* Arithmetic for slower
links: **2 m 46 s at 10 Mbps, 27 m 38 s at 1 Mbps**.
Peak disk during provisioning is **~640 MiB**, while the archive and the
extracted tree coexist. ***Relabelled 2026-08-18: that is arithmetic, not a
measurement*** -- nobody has sampled free space across a provisioning run, and the
sum does not land where the number does (207,274,189 B is 197.67 MiB, plus the
437.24 MiB extracted tree, is ~635 MiB; 640 is 5 × 128 MiB, a round number the
arithmetic does not give). It also assumes an ordering nobody observed: that the
archive is fully present before extraction begins and is removed afterwards.
⚠️ **It gates nothing any more, and the `[UNVERIFIED]` ask it carried is closed
as moot.** *Corrected 2026-09-17 (previously "**It matters because it ships as a
refusal** -- `SessionManager.RequiredFreeBytes` is `640L * 1024 * 1024` and a
session is declined against it. ⚠️ **The margin is still in the safe direction
and is now ~5 MiB rather than ~15 MiB** -- the arithmetic peak moved 625 →
635 MiB with the 1237 → 1244 roll, and two more rolls of that size would cross
it. **The constant is left alone here**, because moving a shipped refusal
threshold is a decision rather than a re-measurement; it is raised where
decisions are raised. ... Settle it by sampling free space every 250 ms across the
run already timed twice at 10.81 s and 10.60 s.")* The maintainer removed the
check outright on 2026-09-17, in his words: *"Remove the free space check.
Checking for free space is out of scope and makes our project more complicated.
I do not want to check for that at all."* `SessionManager.RequiredFreeBytes`,
the `init` refusal it drove and `SessionErrors.InsufficientDisk` are gone, and
`HouseRuleTests.NothingAsksAVolumeHowMuchRoomItHas` holds the absence.
**The ~635 MiB arithmetic stays here as arithmetic** -- it is what a reader
budgeting a machine wants and it is still the honest sum -- but no threshold is
derived from it, so the open ask to sample free space every 250 ms across a run
has nothing left to settle and is withdrawn, not answered. **This file is
where that number lives** -- the rest of the repository cites it instead of
restating it. The component byte counts above **are** measured; re-establish
those with a `HEAD` on the three URLs below. `[FLOATS]` for the components, and
the peak is arithmetic that nothing acts on.

> ⚠️ `Corrected 2026-09-16 @ chromium 1244 / 154.0.8037.0 · playwright-core
> 1.64.0-alpha-2026-09-14 · @playwright/mcp 0.0.81 (previously "**Settled
> 2026-08-15 and re-measured 2026-08-16 by exact `content-length` from the CDN:
> 203.8 MB down.** `chrome-win64.zip` 202,283,919 B + `ffmpeg-win64.zip`
> 1,411,741 B + `winldd-win64.zip` 128,684 B = **203,824,344 B** ... at chromium
> **1237** / 152.0.7977.8 ... **2 m 43 s at 10 Mbps, 27 m 11 s at 1 Mbps**")`.
> **Chromium's archive is the only component that moved**: 202,283,919 →
> 205,733,764 B, `+3,449,845`. `ffmpeg-win64.zip` and `winldd-win64.zip` are
> byte-identical at the same revisions, which is what a revision that did not
> move looks like and is the control for the two that did.
>
> ⚠️ **The re-establish sentence below was wrong for Chromium and is corrected
> with it.** Chromium does **not** resolve under
> `builds/chromium/<revision>/`: `playwright-core` builds its URL with
> `cftUrl()`, which is `builds/cft/<browserVersion>/win64/chrome-win64.zip`
> **keyed on the browser version and not on the revision**, off the bare
> `https://cdn.playwright.dev` mirror and not the `/dbazure/download/playwright`
> one the other three use. Confirmed against upstream's own output and not
> derived: the installer prints the URL it fetched, and it is that string to the
> byte. The three revision-keyed archives are unaffected.

✅ `Verified 2026-09-21 @ chromium 1246 -- **458,475,923 B across 316 files,
byte-identical and file-identical to the 1245 and 1244 readings**, over two clean
runs into an empty root that came back identical to each other as well
(`chromium-1246` 454,699,952 B across 308 files, `ffmpeg-1011` 3,517,342 B across
4, `winldd-1007` 258,560 B across 3, `.links` 69 B). The archive is keyed on
`browserVersion`, which has not moved across three revisions.`

✅ `Verified 2026-09-17 @ chromium 1245 -- **458,475,923 B across 316 files,
byte-identical and file-identical to the 1244 reading**, over two clean runs into
an empty root that came back identical to each other as well. The directory is
`chromium-1245` and everything inside it is what `chromium-1244` held: the
archive is keyed on `browserVersion`, which did not move.`

⚠️ `Corrected 2026-09-16 @ chromium 1244 (previously "**On disk it is
430.48 MiB** ... **451,389,780 B across 318 files** -- `chromium-1237`
447,613,809 B (426.88 MiB)")`. **On disk it is 437.24 MiB**, measured twice by
provisioning into an empty root and summing the files, **byte-identical across
both runs**, and again at 1245 on 2026-09-17: **458,475,923 B across 316 files**
-- `chromium-1245` (`chromium-1244` when this was first taken)
454,699,952 B (433.64 MiB) across 308 files, `ffmpeg-1011` 3,517,342 B
(3.35 MiB) across 4, `winldd-1007` 258,560 B (0.25 MiB) across 3, and `.links`
69 B. **Two files fewer and 7,086,143 B more**, all of it inside the Chromium
tree; the two shared components are unchanged to the byte and the file.

> **`.links` read 69 B on both families this time**, where the 2026-08-19
> Firefox run recorded 95 B -- it holds the absolute path of the `playwright-core`
> that asked for the install, so it is a property of where this repository sits
> on disk and not of a revision. It is included in the root totals above and
> is the one component of them that another machine will not reproduce; compare
> the three component subtrees, never the root total.

> ⚠️ **Corrected 2026-08-16 @ chromium rev 1237 (previously "433 MiB on disk ...
> chromium 428 MiB + ffmpeg 4 + winldd 1").** **On disk it was 430.48 MiB**, and
> the figure before that was three rounded components added up. Measured twice by
> provisioning into an empty root and summing the files: **451,389,780 B across
> 318 files** -- `chromium-1237` 447,613,809 B (426.88 MiB), `ffmpeg-1011`
> 3,517,342 B (3.35 MiB), `winldd-1007` 258,560 B (0.25 MiB) and `.links` 69 B.
> 426.88 is exactly what [the component table](#component-sizes) already
> recorded for full Chromium, so the two halves of this file disagreed by
> 2.5 MiB. **That component table is about `chromium-1237` and has not been
> re-measured at 1244**; only the provisioning figures in this section have.
>
> **The downstream "≈ 570 MB after first run" survives, and the reason is
> stated because it nearly produced a second wrong number.** That figure adds a
> payload quoted in **MB** to browsers quoted in **MiB**, and the payload's
> components are themselves MiB -- so the honest sum is 116.40 + 430.48 =
> **546.88 MiB, which is 573 MB**. The recorded ≈ 570 was right by way of two
> conflations that cancelled. It is now stated in one unit above, and the first
> attempt at this correction wrote "≈ 548 MB" by mixing them the other way.

**End to end it takes 11.28 s and 11.31 s on a ~300 Mbps link**, measured twice
on 2026-09-21 at chromium **1246** into an empty browsers root, exit 0 both
times. *Corrected 2026-09-21 @ chromium 1246 (previously "**11.29 s and 12.51 s**
... measured twice on 2026-09-17 at chromium **1245**"); corrected 2026-09-17 @
chromium 1245 (previously "**10.81 s and 10.60 s** ... measured twice on
2026-09-16")* -- and **the bytes did not move at all**, so
this is the link and the machine on the day, not anything about the
revision, which is exactly what `[MACHINE]` on this figure means. Re-establish by
timing `node.exe cli.js install-browser chromium --no-shell --no-progress`
against a fresh directory, with `PLAYWRIGHT_BROWSERS_PATH` pointed at it --
[`docs/probes/2026-09-16-provisioning`](../../docs/probes/2026-09-16-provisioning/README.md)
is the rig. `[FLOATS]` `[MACHINE]`

> ⚠️ `Corrected 2026-09-16 @ chromium 1244 (previously "**End to end it takes
> 12.6 s and 12.0 s on a ~300 Mbps link**, measured twice on 2026-08-16 ...
> Chromium's download and extraction together take **0.3 s → 11.7 s**, `ffmpeg`
> a further **0.5 s** and `winldd` **0.4 s**")`. **Faster on a larger download**,
> which is the link on the day and not a property of the revision -- this is a
> `[MACHINE]` number and the only transferable half is that it is seconds
> and not minutes.
>
> ⚠️ **The per-phase boundaries are NOT re-measured and have been dropped and not
> carried forward.** They came from the installer's own output timestamped
> per line, and that reading is not available through a pipe: Node buffers
> stdout when it is not a console, so both lines of a two-line install arrive
> together at process exit -- measured here at 5 ms apart for a download that
> took ten seconds. Whoever wants the phases back needs a console or an
> unbuffered channel, and until then a per-phase figure would be a number the
> instrument cannot produce.

> ⚠️ **Corrected 2026-08-16 (previously "20.3 s on a 300 Mbps link, measured
> 2026-08-14 ... an upper bound rather than a measurement").** It was an upper
> bound because that run also fetched `chrome-headless-shell`, which is
> [no longer provisioned](../../DECISIONS.md#processes-browsers-and-session-modes). The two runs above
> are of what BrowserAI actually downloads, so the figure is now a measurement of
> the thing and not of a superset of it.

### Firefox, measured the same way -- 2026-08-19

**Firefox provisioning is 130,934,199 B down = 130.9 MB, and 365,579,913 B =
348.64 MiB on disk across 71 files.** Re-measured 2026-09-21 at Firefox rev
**1549** / 156.0, ffmpeg **1011**, winldd **1007**, under `playwright-core`
1.64.0-alpha-1789764292000 and `@playwright/mcp` 0.0.82, by two clean runs of
`node.exe cli.js install-browser firefox --no-shell --no-progress` into an empty
`PLAYWRIGHT_BROWSERS_PATH` -- **byte-identical across both runs** -- with the wire
figure taken from the exact `content-length` of each archive, which is how
[Chromium's 207.3 MB](#first-run-provisioning) was taken:

| Archive / directory | Down (`content-length`) | On disk | Files |
|---|---:|---:|---:|
| `firefox-win64.zip` → `firefox-1549` | 129,393,774 B | 361,803,942 B (345.04 MiB) | 63 |
| `ffmpeg-win64.zip` → `ffmpeg-1011` | 1,411,741 B | 3,517,342 B (3.35 MiB) | 4 |
| `winldd-win64.zip` → `winldd-1007` | 128,684 B | 258,560 B (0.25 MiB) | 3 |
| `.links` | - | 69 B | 1 |
| **total** | **130,934,199 B = 130.9 MB** | **365,579,913 B = 348.64 MiB** | **71** |

⚠️ `Corrected 2026-09-21 @ firefox 1549 / 156.0 · playwright-core
1.64.0-alpha-1789764292000 · @playwright/mcp 0.0.82 (previously "**Firefox
provisioning is 129,502,648 B down = 129.5 MB, and 362,121,791 B = 345.35 MiB on
disk across 69 files**", measured 2026-09-17 at firefox rev **1548** / 155.0,
with `firefox-1548` at 127,962,223 B down and 358,345,820 B (341.75 MiB) on disk
across 61 files)`. **This is the first Firefox roll in this table that is a new
BROWSER and not a rebuild** -- `browserVersion` 155.0 → **156.0** -- and it
reads like one: `+1,431,551` B on the wire and `+3,458,122` B on disk, against
`+327` and `+902` for the 1544 → 1548 roll, with the file count moving 61 → 63
for the first time since 1539. The two shared components are unchanged to the
byte and the file. **Chromium moved 1245 → 1246 and produced no difference at
all, for the third roll running**, which is again the control for this pair:
both families were re-measured through the same rig in the same session, and one
of them came back identical.

⚠️ `Corrected 2026-09-17 @ firefox 1548 · playwright-core
1.64.0-alpha-2026-09-17 (previously "**Firefox provisioning is 129,502,321 B
down = 129.5 MB, and 362,120,889 B = 345.35 MiB on disk across 69 files**",
measured 2026-09-16 at firefox rev **1544**, with `firefox-1544` at
127,961,896 B down and 358,344,918 B (341.74 MiB) on disk)`. **Firefox is the
only family that moved on this roll, and it moved by almost nothing**: `+327` B
on the wire and `+902` B on disk, all of it inside the Firefox tree, with the
file count identical at 61 and the two shared components unchanged to the byte.
The rounded MB and MiB totals are unchanged; the component MiB ticks 341.74 →
341.75. **Chromium moved 1244 → 1245 and produced no difference at all**, which
is the control for this pair: both families were re-measured through the same
rig in the same session, and one of them came back identical.

⚠️ `Corrected 2026-09-16 @ firefox 1544 / 155.0 · playwright-core
1.64.0-alpha-2026-09-14 · @playwright/mcp 0.0.81 (previously "**Firefox
provisioning is 127,247,129 B down = 127.2 MB, and 356,674,059 B = 340.15 MiB on
disk across 71 files** ... at Firefox rev **1539** / 153.0", with
`firefox-win64.zip` at 125,706,704 B and `firefox-1539` at 352,898,062 B across
63 files, and `.links` at 95 B)`. **Firefox's archive is the only component that
moved**: 125,706,704 → 127,961,896 B, `+2,255,192`; the two shared components
are byte-identical at the same revisions, and `.links` moved because it records
this repository's own path and not because anything upstream did.

> **Two files fewer in both families** -- Chromium 318 → 316, Firefox 71 → 69.
> For Firefox the per-component counts exist on both sides and both losses are
> inside the browser tree, 63 → 61, with the two shared components and `.links`
> unchanged at 4, 3 and 1. Chromium's old figure was never broken out per
> component, so where its two went is not established. Nobody has established
> *which* files left in either: the counts are recorded, the cause is not.

⚠️ **Corrected 2026-08-19 (previously "Firefox 153.0 (rev 1539) is 125,706,704 B
down and 352,898,062 B -- 336.55 MiB -- on disk ... BrowserAI creates no Firefox
sessions").** Both halves needed work. The **numbers were the Firefox archive and
the Firefox directory alone**, while Chromium's were stated for the whole
provisioning run -- so the two were not comparable, and the smaller pair was the
one about to be quoted at a caller. `install-browser firefox` fetches the same
three archives `install-browser chromium` does; `ffmpeg` and `winldd` are shared
by both families and land in the same root. And the second half stopped being
true on 2026-08-19, when `browserai_init` began accepting `browser: "firefox"`.

**Beside an existing Chromium, Firefox downloads the archive and nothing else --
127,961,896 B = 128.0 MB, not 129.5.** ⚠️ **Half of that is re-measured and half
is not, and the halves are named and not blended.** The archive's own size
**is** re-measured, by `HEAD` on 2026-09-16: 127,961,896 B. That it is *the only
thing fetched* beside an existing Chromium was measured once, on 2026-08-19, by
a third run -- `ffmpeg-1011` and `winldd-1007` copied into an empty root **with
their `INSTALLATION_COMPLETE` markers**, then `install-browser firefox`, which
printed exactly one `Downloading` line -- and **that third run was NOT repeated
on 2026-09-16**. `Corrected 2026-09-16 (previously "Firefox downloads
125,706,704 B and nothing else -- 125.7 MB, not 127.2 ... and left the root at the
same 356,674,059 B")` -- the byte count moved with the archive; the *and nothing
else* is carried forward unrepeated and says so here. So the family still has
**two honest figures answering different questions** -- 129.5 MB is what a machine
with no browsers at all pays for Firefox, 128.0 MB what a machine that already
has Chromium pays. `BrowserProvisioner.FirstRunDownloadSizes` quotes
**129.5 MB**: it is the upper bound, it is the same predicate as
[Chromium's 207.3 MB](#first-run-provisioning) -- one family into an empty root --
and the 1.5 MB between them cannot change a caller's decision about waiting.
CI quotes the incremental one, because there the Chromium step runs first.

**`.links` is path-dependent and is not a constant.** It holds the absolute path
of the `playwright-core` package that requested the install -- **69 B in both
families on 2026-09-16**, from this repository's assembled payload, against the
95 B the 2026-08-19 Firefox run recorded from a longer one. Compare the three
component subtrees, not the root total, when comparing across machines.

**Against Chromium: 62.5% of the download and 79.0% of the disk.** Slow-link
arithmetic, stated as arithmetic: **1 m 44 s at 10 Mbps, 17 m 16 s at 1 Mbps**.
Peak disk while archive and tree coexist would be ~469 MiB, which is *arithmetic
and not a measurement* for exactly the reason [the Chromium
figure](#first-run-provisioning) is -- nobody has sampled free space across a run.
*Corrected 2026-09-17 (previously "`SessionManager.RequiredFreeBytes` stays at
640 MiB for both families: it is sized on the larger, both of Firefox's halves
are smaller, and a per-family bound would refuse nothing this one permits.")* --
there is no such constant now; free space is out of scope by the maintainer's
decision of 2026-09-17, and the figure above is a budget for a reader and not
a bound anything enforces. *`Corrected 2026-09-16 (previously "62.4%
of the download ... **1 m 42 s at 10 Mbps, 16 m 58 s at 1 Mbps** ... would be
~461 MiB")`, all four re-derived from the new measured pair.*

**End to end it took 7.25 s and 6.80 s** on the same ~300 Mbps link, exit 0 both
times, against Chromium's 11.28 s and 11.31 s. `[FLOATS]` `[MACHINE]`

> ⚠️ `Corrected 2026-09-21 @ firefox 1549 (previously "**End to end it took
> 6.91 s and 6.95 s** ... against Chromium's 11.29 s and 12.51 s", measured
> 2026-09-17 at rev 1548)`. **The download really did grow this time** -- 1.4 MB
> more on the wire -- and the elapsed pair still straddles the old one, which is
> the `[MACHINE]` tag earning its place: at these sizes the link's variance is
> larger than a megabyte of payload.

> ⚠️ `Corrected 2026-09-17 @ firefox 1548 (previously "**End to end it took
> 6.87 s and 6.75 s** ... against Chromium's 10.81 s and 10.60 s", measured
> 2026-09-16 at rev 1544)`. Both families are slower in this session and both
> downloads are within a kilobyte of what they were, so the four seconds' spread
> across the two sessions is the link and the machine, not the revisions --
> which is what `[MACHINE]` is here to say.

> ⚠️ `Corrected 2026-09-16 @ firefox 1544 (previously "**End to end it took
> 7.30 s and 6.60 s** ... Firefox's download and extraction together **1.1 s →
> 5.9 s** and **0.3 s → 5.2 s**, `ffmpeg` a further 0.4-0.5 s, `winldd`
> 0.4 s")`. The per-phase boundaries are dropped for
> [the same instrument reason as Chromium's](#first-run-provisioning): Node
> buffers a piped stdout, so the installer's per-line timestamps all land at
> exit.

**Re-establish** by running that command against a fresh directory and summing
the files, and `HEAD`ing the three URLs under
`https://cdn.playwright.dev/dbazure/download/playwright/builds/{firefox/1544,ffmpeg/1011,winldd/1007}/`.
**Chromium is the exception and does not resolve under that prefix** -- it is
`https://cdn.playwright.dev/builds/cft/<browserVersion>/win64/chrome-win64.zip`,
keyed on the browser version and not the revision. The revisions and the
browser version come from the payload's own `browsers.json`; never type one.
[`docs/probes/2026-09-16-provisioning`](../../docs/probes/2026-09-16-provisioning/README.md)
is the rig for both halves.

### One `install-browser ffmpeg` rebuilds both shared components -- 2026-08-19

**`install-browser ffmpeg` downloads `ffmpeg` and `winldd` together, and
re-downloads whichever of the two is missing.** Measured 2026-08-19 at
`@playwright/mcp` 0.0.79 / `playwright-core` 1.63.0-alpha-2026-08-05, against
this repository's assembled payload:

| Run | Root before | What it printed | Root after |
|---|---|---|---|
| 1 | empty | `Downloading FFmpeg ... 1011`, then `Downloading Winldd ... 1007` | `ffmpeg-1011` and `winldd-1007`, each with its own `INSTALLATION_COMPLETE` |
| 2 | `winldd-1007` deleted, `ffmpeg-1011` complete | `Downloading Winldd ... 1007` only | both complete again, `ffmpeg-1011` untouched |

**This is why `browserai_reinstall_browser`'s `shared` target passes one name and
still checks two markers.** Asking for both by name would re-download whatever
was already there; trusting the one command's exit code would trust upstream's
grouping, which is the thing that can move. `ProvisionedBrowsers.SharedInstallTarget`
carries the citation, and `BrowserProvisioner.RebuildShared` verifies each
component's own marker after the run. `[FLOATS]`

**Re-establish** with `node.exe cli.js install-browser ffmpeg --no-shell
--no-progress` into an empty `PLAYWRIGHT_BROWSERS_PATH`, then delete
`winldd-<rev>` and run it again. **What would falsify it is upstream regrouping
the two, and it fails in the safe direction here** -- the per-component marker
check turns a `winldd` that stopped arriving into a reported failure instead of
into a tree marked complete.

### Two installers cannot extract into one root -- upstream's `__dirlock` -- 2026-08-19

**Every `registry.install()` on a machine takes a `proper-lockfile` directory
lock at `<PLAYWRIGHT_BROWSERS_PATH>\__dirlock`, before it touches any executable
and for the whole install.** Read out of this repository's assembled payload at
`@playwright/mcp` 0.0.79 / `playwright-core` 1.63.0-alpha-2026-08-05:
`install-browser <x>` is `install <x>`, which is `installBrowsers` →
`registry.install(executables)`, whose body is `mkdir(registryDirectory)` →
`lock(registryDirectory, { lockfilePath: <root>\__dirlock, retries: { retries:
20, factor: 1.27579 } })` → the per-executable loop → `releaseLock()`. The lock
covers `registryDirectory`, which **is** `PLAYWRIGHT_BROWSERS_PATH`.

**This matters here because BrowserAI's own provisioning mutex is keyed on the
family** (`BrowserProvisioner.MutexNameFor` hashes `root|browser`), so a chromium
install and a firefox install run concurrently by design -- and **both** lay down
`ffmpeg` and `winldd` in the one root. Nothing on this side serialises that pair.
Upstream does.

Measured five ways, all on 2026-08-19 against the assembled payload:

| | What was done | What happened |
|---|---|---|
| **A** | `__dirlock` created and its mtime kept fresh by a probe; `install-browser ffmpeg` started against that root | **Nothing at all for 30 s** -- no directory, no download, not one line of output -- and the process stayed alive. It completed **8 s after** the probe removed the lock. The wait is therefore *before* any work, not during it |
| **B** | `install-browser chromium` and `install-browser firefox` started **8 ms apart** into one empty root | Clean serialisation. `__dirlock` held continuously from t+3.1 s; chromium exited 0 at t+10.2 s having downloaded chromium, `ffmpeg` **and** `winldd`; firefox then took the lock, downloaded **only** `firefox-win64.zip` because the two shared markers were already there, and exited 0 at t+17.8 s. Final root: `chromium-1237`, `firefox-1539`, `ffmpeg-1011`, `winldd-1007`, **all four with `INSTALLATION_COMPLETE`**, no `__dirlock` left behind |
| **C** | the same lock held fresh **for as long as the installer would wait** | Gave up at **470 s -- 7 min 50 s** -- exited **1**, and wrote **nothing at all** into the root. The message is upstream's own boxed `An active lockfile is found at: <path>` with `wait a few minutes if other Playwright is installing browsers in parallel` and the `rm -R <path>` escape |
| **D** | three `install-browser ffmpeg` started together into one empty root, **three rounds** -- a race into the shared component directories themselves | 3/3 exit 0 every round, both trees complete and byte-identical every round (`ffmpeg-1011` 3,517,342 B, `winldd-1007` 258,560 B), no `ELOCKED`, no residue |
| **E** | `__dirlock` created and then **never refreshed**, which is the state a killed installer leaves -- BrowserAI closes the installer's job on a cap or on `Dispose` | Reclaimed as stale and the install completed **in 13 s total**, against ~10 s for the same install with no lock present. An abandoned lock costs the next installer the staleness window and nothing else |

**So the corruption is upstream's to prevent and upstream prevents it.**
*Corrected 2026-08-19: `ReinstallSharedAsync`'s remarks previously called two
family installs racing into one shared component directory "reachable in the
shipped product". The concurrency is reachable; the race is not.*

**What is real is a wait, and it belongs to the waiter -- measurement C.** 20
attempts at a 1.27579 factor comes out at **470 s**, after which upstream fails
the install outright instead of queueing further. **A first-run chromium
download can outlast that**: 207.3 MB in 470 s is 3.5 Mbps (*corrected
2026-09-17, previously "203.8 MB in 470 s is 3.5 Mbps"*; both sizes give
3.5 Mbps to two figures, so the rate is unchanged and the size it is derived
from is not), and
`ProvisioningTimers.AbsoluteCap` is deliberately sized for links down to
0.60 Mbps -- so on any link slower than ~3.5 Mbps, a firefox install started
beside a chromium install fails with `ELOCKED` instead of waiting for it.

**It fails loudly, writes nothing, and the next attempt succeeds.** BrowserAI's
own caps do not fire and do not need adjusting: the wait happens before the
browser's directory appears, so `ProvisioningTimers.ExtractionCap` has not
started and only the 45-minute `AbsoluteCap` covers it -- and 470 s is inside it.
What the caller sees is upstream's box, which names the path and says to wait a
few minutes. `[FLOATS]`

**Re-establish** measurement A: `mkdir <root>\__dirlock`, keep touching it faster
than `proper-lockfile`'s 10 s staleness window, run `install-browser ffmpeg
--no-shell --no-progress` against that root, and check the root stays empty.
**The refresh is the control** -- without it the lock goes stale in ten seconds and
the installer proceeds, which measurement E is. What would falsify all of this is
upstream dropping the lockfile, moving it inside the per-executable loop, or
scoping it to something narrower than the root;
`PayloadTests.UpstreamStillSerialisesEveryInstallOnOneLockOverTheWholeBrowsersRoot`
reads the four anchors out of the assembled bundle and asserts their order, so a
removal is a red build, not a rediscovery.

**⚠️ Chrome for Testing has exactly one mirror, so the retry rotation does not
help it.** Read 2026-08-16 out of `playwright-core/lib/coreBundle.js`: `cftUrl`
returns `{ path: "builds/cft/${browserVersion}/win64/chrome-win64.zip", mirrors:
["https://cdn.playwright.dev"] }` -- one host, and **no**
`/dbazure/download/playwright` prefix, which every other component does carry.
`ffmpeg`, `winldd` and `firefox` resolve to the two-host list
(`cdn.playwright.dev` and `playwright.download.prss.microsoft.com`) under that
prefix. Since retries index `downloadURLs[(attempt - 1) % downloadURLs.length]`,
**Chromium's five attempts all hit the same host**, and the mirror rotation that
justifies stripping `PLAYWRIGHT_DOWNLOAD_HOST` protects only the three small
components. Observed directly: with this machine's resolver failing on
`cdn.playwright.dev`, all five Chromium attempts failed against that one name.
Re-establish by grepping `cftUrl` in the resolved bundle. `[FLOATS]`

**This machine's DNS resolver fails intermittently on both Playwright CDN
names.** `cdn.playwright.dev` and `playwright.download.prss.microsoft.com`
resolve reliably through `1.1.1.1` and `8.8.8.8` and intermittently through the
configured server (`10.20.30.254`) -- and a **tight retry loop makes it worse**,
because the failures are negatively cached: twenty queries with no delay left
both names unresolvable for the following minute, six queries three seconds apart
resolved on the first attempt. It presents as `EAI_AGAIN` then `ENOTFOUND` in the
installer's output and looks exactly like an outage. `[MACHINE]`

> **What this supersedes, kept so the old numbers are recognisable and not
> mysterious.** The download was previously stated four ways across the
> repository -- 202.3 MB, 323.5 MB, ~300 MB, and ~0.9 GB peak disk -- and this
> file called the size `[UNVERIFIED]` on the grounds that only a run could settle
> it and no arithmetic may. A run settled it. 202.3 MB was **one term** of the
> 2026-08-14 sum, whose total was *"chromium 202.3 MB + shell 119.7 MB + ffmpeg +
> winldd = 323.5 MB down, ~700 MiB on disk"*, and the superseded slow-link
> figures -- 4 m 19 s at 10 Mbps, 43 m at 1 Mbps -- belonged to that larger total.
> The [2026-08-15 decision](../../DECISIONS.md#processes-browsers-and-session-modes) to run full Chromium
> in every mode stopped provisioning the shell, which is what moved the number:
> the old measurement was never wrong, it stopped applying.

**`PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1` does not stop the explicit installer.**
Measured 2026-08-16 @ `playwright-core` 1.63.0-alpha-2026-08-05: with the
variable set and `PLAYWRIGHT_BROWSERS_PATH` pointed at an **empty** directory,
`cli.js install-browser ffmpeg --no-progress` downloaded and extracted anyway.
The flag is read in exactly two places in `lib/coreBundle.js` --
`installBrowsersForNpmInstall`, the npm postinstall path, and
`ensureConfiguredBrowserInstalled`, the server's start-up auto-install -- and
`registry.install()`, which the `install-browser` command calls, does not
consult it. **Both halves matter to us:** the variable is
[mandated in the child's environment](../../DECISIONS.md#the-five-rules-that-make-floating-safe)
to stop the child provisioning behind our back, and it does still close that
door; but a build or a provisioning subsystem that relied on it as a global
kill-switch would be relying on something that was never true. Re-establish by
running the command above against a fresh directory. `[FLOATS]`

**Installing `ffmpeg` on Windows pulls `winldd` with it**, unasked -- the same
run produced both `ffmpeg-1011` and `winldd-1007`, which is why a browsers root
seeded by hand needs all three directories and not just Chromium's.
`[FLOATS]`

**In-session recovery is proven.** The same child navigates successfully once the
install lands, with no restart. `[FLOATS]`

**The revision is pinned for free and is never looked up online.**
`playwright-core/browsers.json` carries the revision and `browserVersion`; the URL
is built by substituting that version into a template that **307s** to Google's
bucket. That file is inside the artifact and **no "latest" lookup exists anywhere
in the registry code**, so a release knows forever which browser it wants. Old
builds still resolve back to **Chrome 115 (Jul 2023)** -- about three years of
evidence -- but **Google documents no retention policy**, so it is evidence and
not a guarantee. `[FLOATS]`

**Egress hosts:** `cdn.playwright.dev`, `storage.googleapis.com`,
`playwright.download.prss.microsoft.com`. `HTTPS_PROXY` / `HTTP_PROXY` /
`NO_PROXY` / `ALL_PROXY` and **`NODE_EXTRA_CA_CERTS`** are honoured on the
download path; **SOCKS is not supported** there. `[FLOATS]`

**`PLAYWRIGHT_BROWSERS_PATH` must be absolute.** A relative value resolves
against `INIT_CWD` -- inherited from any npm ancestor -- before `cwd`. `[FLOATS]`

**Layout under the browsers root, verified by execution:** `[FLOATS]`

```
<browsers-root>\
  chromium_headless_shell-1237\chrome-headless-shell-win64\chrome-headless-shell.exe
  chromium-1237\chrome-win64\chrome.exe
  ffmpeg-1011\ffmpeg-win64.exe
```

The asymmetry: the **outer** directory uses underscores, the **inner** one
dashes, so a path built consistently is wrong. **No sentinel file is needed to
launch** -- not `INSTALLATION_COMPLETE`, not `DEPENDENCIES_VALIDATED`; the only
launch-time check is file accessibility of the executable.

**`.links/` lives in the browsers root and nowhere else.** Read 2026-08-16 in
`playwright-core/lib/coreBundle.js` at `playwright-core`
1.63.0-alpha-2026-08-05: every reference is `path.join(registryDirectory,
'.links')` -- in `install()`, `uninstall()` and `listInstalledBrowsers()` -- so it
is **never** written into `node_modules`, and a payload build has nothing to
strip. Each file is named for the SHA-1 of an installing `playwright-core`
package directory and contains that directory's absolute path, one per line;
verified by running the installer from a fresh tree and reading the file it
produced. It therefore records the machine that **installed** the browser, which
under [first-run provisioning](#first-run-provisioning) is the user's machine
and not a build machine. **Do not delete it:** the stale-browser GC treats a
registry directory with no `.links` entry as prunable, which is what
`PLAYWRIGHT_SKIP_BROWSER_GC=1` exists to stop. Re-establish with
`grep -n '\.links' node_modules/playwright-core/lib/coreBundle.js`. `[FLOATS]`

**`DEPENDENCIES_VALIDATED` is written into the browsers root on first launch.**
Under `Program Files` that write silently fails and the validation re-runs every
launch. Prefer `%LOCALAPPDATA%` or a `%ProgramData%` path with write ACLs.
`[FLOATS]`

**Node SEA, `pkg` and `nexe` are dead ends.** `playwright-core` violates SEA's
"no filesystem module loading" constraint in **five verified ways**: `packageRoot`
computed from `__dirname`; a runtime `require` of `browsers.json` at a computed
path; two `childProcess.fork()` calls on sibling scripts; sibling bundle
requires; and `.wasm`/`vite` assets loaded by path. SEA would also save nothing --
its output *is* a copy of `node.exe` plus the blob. `vercel/pkg` was archived
**2024-01-13**. Bun and Deno both carry open issues on the Playwright
browser-launch path. `[FLOATS]`

### What grows on disk while an install runs, and when -- 2026-08-19

**The download does not touch the browsers root at all, and the extraction is the
only thing that does.** Measured 2026-08-19 against this repository's assembled
payload at `@playwright/mcp` 0.0.79 / `playwright-core` 1.63.0-alpha-2026-08-05,
by sampling both directories every 250 ms across a real
`install-browser <family> --no-shell --no-progress` into an empty root:

| Phase | Chromium (41 samples, 10,834 ms) | Firefox (27 samples, 6,918 ms) |
|---|---|---|
| Download directory created | t+581 ms | t+529 ms |
| Archive grows | 47,892 B → **202,283,919 B**, t+1,113 → t+6,164 ms | 13,859,716 B → **125,706,704 B**, t+1,061 → t+3,184 ms |
| Revision directory appears (extraction starts) | t+6,427 ms | t+3,450 ms |
| Revision directory grows | 15,971,824 B → 447,613,878 B | 12,748,084 B → 352,898,131 B |
| Archive unlinked | t+9,653 ms | t+5,570 ms |
| `ffmpeg` and `winldd` follow | root 451,131,220 → **451,389,780 B** | root 356,415,473 → **356,674,033 B** |

**Every single sample differed from the one before it**, in both phases and in
both families. That is what makes a *stall* detector on bytes-on-disk viable
where a total-time cap was not: the signal has a 250 ms granularity on a
~300 Mbps link, and the only thing that can hold it still is a link that has
stopped.

**The two archive figures are exactly the `content-length` values
[recorded above](#first-run-provisioning)** -- 202,283,919 B and 125,706,704 B --
so the on-disk sample and the CDN figure are the same number reached two ways.
The root totals land on the recorded ones once `.links` is accounted for:
Chromium's 451,389,780 B is exact, and Firefox's 356,674,033 B is 26 B under the
recorded 356,674,059 B because **`.links` is path-dependent** and was 69 B in
this run against 95 B in the run that produced the recorded figure -- which is the
thing [the Firefox section](#firefox-measured-the-same-way----2026-08-19) already
warns about.

**Upstream downloads into `os.tmpdir()`, not into the browsers root.**
`downloadBrowserWithProgressBar` does
`mkdtemp(path.join(os.tmpdir(), "playwright-download-"))` and writes
`playwright-download-<name>-<platform>-<revision>.zip` into it, then
`removeFolders([uniqueTempDir])` in a `finally`. So on Windows the location is
whatever `TEMP` says, and **BrowserAI sets `TEMP` and `TMP` for the installer
child** to `<browsers root>\.downloads\<family>` --
`BrowserProvisioner.DownloadDirectoryName` plus the family, one subdirectory each
because the provisioning mutex is keyed on the family and two installs run at once
by design -- which is what makes one recursive weigh of the browsers root cover
both phases.
Proven, not assumed: the redirected chromium run above produced
451,389,780 B, byte-identical to the run with the default temp.

**Scanning `%TEMP%` instead would have been wrong, and it was measurably wrong on
this machine.** The default-temp run started with `tempdl = 128,684 B` and
`dirs = 2` before it had downloaded anything: a `playwright-download-PRU23e`
abandoned on 2026-08-16 was still there, holding a stale `winldd-win64.zip`.
Upstream's `finally` does not run when a killed installer is killed, and
BrowserAI closes the installer's job on a cap -- so its own residue would
accumulate there too.

**There is no other progress signal, and that is the second half of the
measurement.** `--no-progress` sets `PLAYWRIGHT_DOWNLOAD_NO_PROGRESS=1`, which
makes `downloadFile`'s `reportProgress` false, so the parent's
`getBasicDownloadProgress()` never prints a percentage line: the installer's
whole stdout for a chromium install is **six lines**, one `Downloading ...` and one
`... downloaded to ...` per archive. And `@playwright/mcp` emits no MCP progress
notifications at all ([kb](../mcp/sdk.md#lossless-passthrough-cancellation-notifications-and-error-frames)).

**Re-establish** by sampling `PLAYWRIGHT_BROWSERS_PATH` and the temp directory
recursively every 250 ms across `node.exe cli.js install-browser <family>
--no-shell --no-progress` into an empty root, once with `TEMP` redirected and
once without. **The control is the redirected/default pair** -- a single run
cannot tell "the redirect works" from "the download happened to land here". What
would falsify it is upstream downloading straight into the registry directory, or
`extractZip` starting to stream instead of writing whole files, either of which
would change *which* directory grows but not *that* one does. `[FLOATS]`
`[MACHINE]`

## What the first-run download costs the suite

**Measured 2026-08-17, eight full-suite runs, from TUnit's own per-test report.**
Nobody had asked: `FirstRunProvisioningTests` is the suite's longest test by a
factor of two and was assumed to dominate the wall clock. It does not, because
the suite runs four-wide.

| Run | Suite wall (test execution) | `FirstRunProvisioningTests` | Sum of all test durations |
|---|--:|--:|--:|
| Cold, before a cache existed | 36.50 · 35.84 · 35.91 s | 13.77 · 15.81 · 15.92 s | 132.6 · 130.1 · 129.5 s |
| Cold, and publishing the cache | 36.84 s | 17.13 s | 136.0 s |
| Seeded from the cache | 31.89 · 34.74 · 31.16 · 31.97 s | 3.49 · 3.88 · 3.36 · 3.22 s | 117.3 · 124.8 · 112.7 · 117.8 s |

**The download is worth 3.6-4.4 s of a ~36 s run -- 10 to 12% -- while the test
that performs it takes 12 to 14 s longer than its seeded form.** Means: 32.44 s
seeded, against 36.08 s for the pre-cache baseline and 36.84 s for a cold run
that also publishes. The gap between 13 s of test and 4 s of suite is the
parallelism: with the suite capped at four concurrent tests and ~130 s of total
test work, it is **work-bound and not critical-path-bound**, so removing 13 s
of work returns about a quarter of it to the clock. A test's own duration is
therefore not its cost to the suite, and this is the second time that distinction
has mattered here -- the first being a slice test that took 2.6 ms on a run that
really did launch a browser. `[MACHINE]`

**What publishing and seeding each cost is not separable from this data**, and is
recorded as unmeasured and not divided out: the single cold-with-publish run
(17.13 s) sits above a baseline whose own spread is 13.77-15.92 s, so the
451,389,838 B same-volume copy is inside that difference and cannot be read off
it. The seeded figure of 3.49-3.88 s is a whole first-run sequence -- published
binary start, `initialize`, `init`, two refusals, a `browserai_list`, the copy,
and one real navigation against a real Chromium -- not a copy time. `[MACHINE]`

**A cached run really does not reach the network, measured at the adapter and not
inferred from the code.** `Get-NetAdapterStatistics` sampled either side of
the first-run test alone, 2026-08-17:

| Mode | Bytes received across all adapters |
|---|--:|
| Seeded from the cache | **133,761** |
| `BROWSERAI_FIRST_RUN_CACHE=off`, forced cold | **425,355,150** |

A factor of **3,180**. `[MACHINE]`

> **The cold figure is ~2× the download because two adapters count the same
> bytes.** This machine carries `Ethernet` and `vEthernet (LAN-Bridge)` over it,
> and summing every adapter counts bridged traffic twice: 425,355,150 / 2 =
> 212.7 MB against a 203.8 MB payload, the remainder being TLS and TCP overhead.
> **Sum one physical adapter, not all of them**, when re-establishing this -- the
> ratio is the finding and the absolute number needs that correction.

**A cached tree is 432 MiB on disk and exactly one is kept**, pruned by the run
that publishes its replacement. Full census of what a first run produces, as
copied: **318 files, 451,389,838 B**. `[MACHINE]`

> ⚠️ **That is 58 B larger than [the figure above](#first-run-provisioning)
> (451,389,780 B), and the difference is not drift.** `.links/` holds one file
> whose *content* is the absolute path of the `playwright-core` package that
> installed the tree, so its length tracks where the installer ran from: 69 B
> from `payload\mcp\node_modules\playwright-core` at the repository root, 127 B
> from the same relative path under
> `src\BrowserAI\bin\Release\net10.0-windows\win-x64\publish\`, which is where
> the suite drives the published binary from. Both numbers are right for what
> they measured. **A census compared across two machines will differ for the same
> reason**, which matters before treating a mismatch as corruption --
> and is why the cache's own completeness check compares a tree against the stamp
> *it* was published with and not against a figure written down here.

**Re-establish** by running the suite twice within the hour and reading the
`first-run bytes` row of the coverage block, or `.work/suite-coverage.txt`, which
names the source, the age of the tree used, and the elapsed time. The mechanism
is [in TESTING.md](../../TESTING.md#the-first-run-download-runs-at-most-once-an-hour).

## Timings: spawn, resume, idle close, proxy overhead

`[MACHINE]` for every number, `[FLOATS]` for what they are numbers *about*.

> ⚠️ **Only the resume figure carries a date in the charter.** Spawn, navigation,
> idle close and proxy overhead are all recorded undated, so treat their dates as
> `[UNVERIFIED]` and re-stamp each at the next run. The numbers themselves are
> carried forward exactly as written -- none has been adjusted.

**A real browser reaches MCP-ready in 0.9-1.2 s (Chromium) and 3.1-4.0 s
(Firefox), against a 30-minute hang detector.** Measured 2026-08-17, **four runs
of each family**, through the product's own job object and launcher: the clock
starts when `CreateProcessW` returns for the launcher and stops when the driving
script has completed `initialize`, `tools/list` and a `browser_navigate` against
a real browser out of the provisioned tree, so it covers `node` start, `cli.js`
start, browser launch and one navigation.

| Browser | Time to MCP-ready | Processes in the job | Escapees | Survivors after an external kill |
|---|---|--:|--:|--:|
| Chromium 152.0.7977.8 (`chromium-1237`) | 932.7 · 977.3 · 996.0 · **1165.8 ms** | 10 · 11 · 10 · 11 | 0 | 0 |
| Firefox 153.0 (`firefox-1539`) | 3069.6 · 3206.0 · 3511.3 · **3997.9 ms** | 10 each run | 0 | 0 |

**Firefox is ~3.4× slower to first answer**, consistently, across every pair --
which is the transferable half, and it is the same direction as
[the cost ratios](#firefox-against-chromium-the-standing-cost-ratios) below. The
absolute numbers are this machine's.

**The headroom is the point, not the latency.** Playwright's own
`DEFAULT_PLAYWRIGHT_LAUNCH_TIMEOUT` is `3 * 60 * 1e3`. The slowest observed run
used **2.2%** of the 180 s the harness then waited. A launch timeout is
therefore not a knob worth tuning, and a launch that approaches it is not slow --
it is stuck, and should be read as a failure and not as a machine having a
bad day. `[MACHINE]` for the times and counts; `[FLOATS]` for the ratio and the
headroom, both of which move with a browser revision.

> ⚠️ **Corrected 2026-08-18 (previously "against a 180 s default patience ...
> the harness waits the same 180 s").** *The measured times above are unchanged
> and were not re-run; what changed is the harness they were measured against.*
> `BrowserContainmentTests.ReportPatience` is now `TestDefaults.BrowserHang`,
> **thirty minutes**, so the slowest observed run uses 0.22% of it and not
> 2.2%. The reason is the one this paragraph already gives, applied properly: a
> harness bound *equal* to Playwright's own launch timeout always wins the race
> against it, so upstream's diagnosis is replaced by *"the budget expired"* in
> the one case upstream had something to say -- observed at exactly 3m00s on a
> Firefox launch, 2026-08-17. And at unbounded suite parallelism 180 s was
> reachable by a launch that was merely starved: one run in four
> ([kb](../toolchain.md#running-419-tests-at-once-what-starves-and-by-how-much)).

**Re-establish by running the suite.** `BrowserContainmentTests` records
`readyMilliseconds` beside its containment counts **on every run, including the
ones that pass** -- which is deliberate: a bound can only be called too tight
against a distribution, and a distribution cannot be reconstructed from the runs
that failed.

> **Recorded here instead of left in the test output, and the earlier reasoning
> for leaving it out was wrong.** These numbers were measured before and kept out
> of the knowledge base on the grounds that an entry marked as floating creates a
> re-verification obligation. That is backwards: the obligation is the feature,
> and this is the cheapest kind of row there is -- the fact is asserted by a test
> that already runs on every build, so the row costs a line and nothing else.
> [Row 89](../re-verification.md) carries it. *(Written in words and not in
> the marker, which is the rule for prose about the convention: the counter reads
> the token and cannot tell a mention from a stamp.)*

**Child spawn costs ~300 ms.** That is the baseline a flat 5 s discovery probe
would be paid against ([the protocol split](../mcp/protocol.md#the-protocol-split)), and the
per-instance price of one node child per handle.

**A real navigation costs 0.43 s** -- `browser_navigate` to
`data:text/html,<h1>ok</h1>`, no network and no local server. `about:blank`
succeeds too trivially and its snapshot is empty, which is why the smoke
assertion uses a `data:` URL.

**Browser-idle close: the whole browser tree goes, the node child stays, and the
next call brings the browser back in ~0.41 s.** `[MACHINE]` `[FLOATS]`

> ⚠️ **Corrected 2026-08-16 @ `@playwright/mcp` 0.0.79 (previously "recovers
> 329 MB → 110 MB, and relaunch costs 186 ms", undated).** Both halves were in
> the right direction and neither number survived. Re-measured twice against a
> real child with `chromium-1237` / Chrome for Testing 152.0.7977.8, headless,
> on this machine:
>
> | | Run A | Run B |
> |---|---|---|
> | Browser processes after the first navigation | **8** | **7** |
> | Their total working set | **378.3 MB** | **369.4 MB** |
> | node's working set, throughout | 117.6 → 117.8 → 121.4 MB | 116.3 → 116.5 → 120.2 MB |
> | Browser processes after `browser_close` | **0** | **0** |
> | `browser_close` itself | 209 ms | 190 ms |
> | The next `browser_navigate` | **416 ms** | **409 ms** |
> | A `browser_snapshot` after it | 4.2 ms | 4.6 ms |
>
> So the shape of the old claim holds -- an idle session falls back to roughly the
> node child's own footprint -- while the totals are ~496 MB → ~118 MB and not
> 329 → 110, and the relaunch is **2.2× the recorded figure**. The old numbers
> carried no date and no version, which is why nobody could tell whether they had
> moved or had always been wrong.

**The relaunch is upstream's own behaviour, not something a caller or a proxy has
to arrange.** Playwright creates the browser lazily on first use, so the call
after a close simply works: no error, no `"browser is closed"` text on any path,
and a snapshot immediately afterwards returns the new page. This is the
measurement [the browser-idle timer](../../ARCHITECTURE.md#sessions)
rests on -- if the relaunch were not implicit, the timer would be a way of
breaking a session and not a way of reclaiming memory.

⚠️ **`browser_close`'s own result text reads as though it closed a tab, and it
does not.** It answers *"No open tabs. Navigate to a URL to create one."* with
`await page.close()` as the code it ran -- yet every process under the browsers
root is gone afterwards, because closing the last page tears the persistent
context down and the browser with it. A reader who trusted the wording would
conclude the timer does nothing. Called again with no browser open it answers the
same text, is **not** an error, and costs 156-514 ms -- so a close that races
anything costs a round trip and not a failure.

**How to re-establish all of the above:** drive a real child directly --
`node <payload>/mcp/node_modules/@playwright/mcp/cli.js --config <cfg> --sandbox`
with `PLAYWRIGHT_BROWSERS_PATH` set -- through `initialize` → `browser_navigate`
→ `browser_close` → `browser_navigate`, counting processes whose
**`ExecutablePath` is under the browsers root** at each step and reading
`WorkingSet64`. Never match a process by image name: a foreign Firefox
and Chrome are on this machine. The *behaviour* half is asserted on every build
by `BrowserIdleTimerTests.AnIdleSessionLosesItsBrowserKeepsItsNodeChildAndTheNextCallStillWorks`;
only the numbers need the manual run.

✅ **RE-ESTABLISHED 2026-09-17 at chromium 1245 and firefox 1548**, clearing the
`[STALE]` this section carried for six hours. *Previously, and kept because it is
what the debt looked like:* "⚠️ **`[STALE]` since 2026-09-17.** The
`playwright-core` pull-forward to 1.64.0-alpha-2026-09-17 is a Playwright bump
**and** a chromium revision move, 1244 → 1245, which is both halves of this
measurement's trigger. Nothing below is adjusted; the cost is what a revision
most plausibly moves, and the **durability** half is the one
[reclaim is forever](../../ARCHITECTURE.md#sessions) rests on and the one to
re-take first, with
[`docs/probes/2026-09-16-resume`](../../docs/probes/2026-09-16-resume/README.md)
as the rig. [Re-verification row 38](../re-verification.md) carries the debt."

⚠️ **"Resume" is TWO paths since 2026-09-17, and this entry now says which
one each number is of.** Until `5d0d04f` the tool asked one question -- *do I
already own this directory* -- so there was only ever one path worth timing.
It asks a second now, and starts a replacement when the child behind the session
has gone ([the architecture](../../ARCHITECTURE.md#sessions)), which is a resume
that did not exist when the figure below was last taken.

- **Path A, a different process meets the directory.** The case the feature
  exists for, and the one the headline figure is of.
- **Path B, the same process repairs a session whose child died.** New on
  2026-09-17. Where this used to be a 7.68 ms no-op that left the session
  unusable, it is now a relaunch -- [see below](#the-resume-wedge-measured----2026-09-17).

**Path A costs 494 and 456 ms on Chromium and 483 and 492 ms on Firefox, and
loses only `sessionStorage`. PATH B COSTS 406 and 401 ms on Chromium and 396 and
406 ms on Firefox -- AND LOSES MORE THAN `sessionStorage`.** Re-measured
2026-09-22 at chromium **1246** / 154.0.8037.0 and firefox **1549** / **156.0**
under `playwright-core` 1.64.0-alpha-1789764292000 and `@playwright/mcp` 0.0.82,
against a real published `BrowserAI.Server.exe`, **twice per path per family --
eight runs, and the first Firefox reading Path B has ever had**. *Corrected
2026-09-22 (previously "**Path A costs 375 ms and 379 ms on Chromium and 389 ms
on Firefox, and loses only `sessionStorage`.** Re-measured 2026-09-17 at chromium
**1245** ... twice on Chromium and, **for the first time, once on Firefox** ...
**the load-bearing half held exactly, on both families**", whose durability table
described **Path A only** and was not labelled as doing so).*

**Path A's durability claim held exactly, again, on both families.** It is the
measurement the no-expiry-timer decision rests on -- the durable thing is the
profile, not the process.

⚠️ **PATH B'S DOES NOT, AND THAT IS THE FINDING OF THIS RE-MEASUREMENT.**
A relaunch after the child was **killed** loses persistent stores that a clean
handover keeps, and *which* ones it loses varies run to run. Every Path B run
lost at least one store beyond `sessionStorage`; no Path A run lost anything
else. Same probe, same `WRITE`, same `READ`, same origin, seconds apart.

| Store | Written before | Path A -- Chromium ×2 | Path A -- Firefox ×2 | Path B -- Chromium ×2 | Path B -- Firefox ×2 |
|---|---|---|---|---|---|
| Cookie (`max-age=3600`, **persistent**) | `cookie-value` | **survived** · **survived** | **survived** · **survived** | ⚠️ **GONE** · ⚠️ **GONE** | survived · survived |
| `localStorage` | `local-value` | **survived** · **survived** | **survived** · **survived** | survived · ⚠️ **GONE** | ⚠️ **GONE** · ⚠️ **GONE** |
| `sessionStorage` | `session-value` | **gone** -- the only loss | **gone** -- the only loss | gone | gone |
| IndexedDB | `idb-value` | **survived** · **survived** | **survived** · **survived** | **survived** · **survived** | **survived** · **survived** |
| CacheStorage | `cache-value` | **survived** · **survived** | **survived** · **survived** | **survived** · **survived** | **survived** · **survived** |
| Service worker registrations | 1 | **1** · **1** | **1** · **1** | **1** · **1** | **1** · **1** |

> ⚠️ **THE COOKIE IS NOT A SESSION COOKIE AND THAT WAS CHECKED BEFORE THIS
> WAS WRITTEN DOWN.** `document.cookie = 'reverify=cookie-value; path=/;
> max-age=3600'` -- an hour's persistent cookie, read back after a
> `browser_navigate` to the same origin. A session cookie would explain the loss
> away entirely, and it is not one. `localStorage` is persistent by definition.
> So both losses are losses of durable state.
>
> **IT REPRODUCED ACROSS TWO INDEPENDENT SITTINGS.** An earlier sitting the same
> evening was **discarded as invalid** -- its driver handed all eight runs one
> literal session path, `...\resume$tag`, because a heredoc ate a level of
> backslash escaping -- and is recorded here and not deleted because its
> durability column is identical to the valid one, store for store, in all four
> Path B runs. Eight Path B runs, two sittings, one result.
>
> **WHAT IT PROBABLY IS, LABELLED AS A READING AND NOT A MEASUREMENT.** Path
> A's server closes its stdin and exits, so the browser is shut down and flushes;
> Path B kills the node children by pid and the browser dies with them, losing
> whatever the cookie jar and the `localStorage` backing store had not yet
> written. That is consistent with every run -- the two stores that are flushed
> lazily are the two that go, and IndexedDB and CacheStorage, which commit on
> transaction, never do -- but **nothing here measured a flush**, and which store
> goes on which run was not predicted in advance.
>
> ⚠️ **AND THE PRODUCT SAYS OTHERWISE, IN A MODEL-FACING STRING, ON THIS
> EXACT PATH.** `SessionManager.ChildWasRelaunched` is what a Path B resume
> returns, and it reads: *"the browser server for this session had died and was
> relaunched. The session's directory, profile and log are unchanged, **so
> cookies and stored state are still there** -- but nothing that lived in the old
> process survived it..."*. The measurement says a cookie may not be. **No product
> change is taken here** -- the wording of a model-facing string is the
> maintainer's -- and it is [an open hazard row](../../HAZARDS.md#hazard-index)
> and not a sentence quietly edited.

#### A browser server that ends ITSELF loses the same stores as one that is killed -- measured 2026-09-22

⭐ **The obvious escape from the finding above was measured and it is closed.**
The table's own reading was that Path B kills the node children *"losing whatever
the cookie jar and the `localStorage` backing store had not yet written"*, which
invites the narrowing *then it is the kill, and a browser that dies of its own
accord flushes on the way out*. That was the one thing that would have let the
model-facing string keep its promise for real crashes. **It does not happen.**
`[FLOATS]`

Nine runs through
[`docs/probes/2026-09-16-resume/selfdeath-probe.js`](../../docs/probes/2026-09-16-resume/README.md),
at chromium **1246** and firefox **1549** under `playwright-core`
1.64.0-alpha-1789764292000 and `@playwright/mcp` 0.0.82, against a real published
`BrowserAI.Server.exe`. One arrangement, three ways for the child to go:

| How the browser server went | Chromium ×2 | Firefox ×2 |
|---|---|---|
| **`kill`** -- `Stop-Process` by pid, identity verified. The control, and it is Path B | cookie **GONE**, `localStorage` **GONE** | cookie survived, `localStorage` **GONE** |
| **`exit`** -- the child called `process.exit(0)` on itself | cookie **GONE**, `localStorage` **GONE** | cookie survived, `localStorage` **GONE** |
| **`abort`** -- the child called `process.abort()` on itself | cookie **GONE**, `localStorage` **GONE** | cookie survived, `localStorage` **GONE** |

**`IndexedDB`, `CacheStorage` and the service-worker registration survived in all
nine**, and `sessionStorage` went in all nine, which is what it does on every
path. Every run confirmed all six stores present before the death. The resume
returned `SessionManager.ChildWasRelaunched` in **290-344 ms** in all nine, and
the browser tree went to **0** processes in all nine.

⭐ **Not one arm is distinguishable from the control**, and the per-family pattern
is tighter than the killed-only table above it: **Chromium loses both the cookie
and `localStorage` on 5 of 5, Firefox loses `localStorage` and keeps the cookie
on 5 of 5.** So the loss is a property of *the browser not shutting down
cleanly*, not of *who ended it* -- which is why the corrected string says exactly
that and does not say "killed". **Q223 c**, and it is what settled Q223 b.

> **What the probe had to do to make a process end itself, recorded because it
> is a fact about upstream and not about this measurement.**
> `browser_run_code_unsafe` describes itself as executing *"arbitrary JavaScript
> in the Playwright server process"* and does so through
> `vm.runInContext` against a context built as `{ page, __end__ }` **and nothing
> else** -- read 2026-09-22 out of the payload's own
> `playwright-core/lib/coreBundle.js`. `process`, `setTimeout` and `require` are
> all undefined in that snippet. **The first version of this probe died on
> `ReferenceError: setTimeout is not defined` and reported a clean run**, because
> the child it meant to end never went anywhere and the before/after reads were
> trivially equal. The route that works is `page.constructor.constructor('return
> process')()` -- `page` is a host-realm object, so its constructor's constructor
> builds a function in the host realm, which is the escape node's own
> documentation says `vm` is not a defence against. **The tool's description is
> therefore accurate about the risk and misleading about the default scope**, and
> that matters for a tool this product forwards with an `allow` verdict.

> ⚠️ **ONE THING THE SELF-DEATH ARMS DO THAT THE CONTROL DOES NOT, named and not
> smoothed over.** A server has **two** `node` children; the control kills
> both, and `process.exit`/`process.abort` end exactly **one** -- the one
> BrowserAI's transport is talking to, which answered *"The browser child did not
> answer 'tools/call': IOException: The server shut down unexpectedly"* -- leaving
> the other alive, so those arms sat out the probe's full 30 s wait instead of
> finishing in the control's 42 ms. **It changes nothing about the readings**:
> the browser tree went to zero and the relaunch happened in every arm. What the
> surviving `node` is was not diagnosed, and is recorded as not diagnosed.

**Costs, 2026-09-22, two runs each.** Read them against the control in
[the cost ratios](#firefox-against-chromium-the-standing-cost-ratios): Chromium
is **byte-identical across 1244, 1245 and 1246**, and its own first-navigate
drifted **+15.3%** between the two sittings, so most of the movement below is the
machine.

| | Chromium 1246 | Firefox 1549 | previous |
|---|---:|---:|---|
| **Path A** `browserai_resume` | **494** · **456** ms | **483** · **492** ms | 375 · 379 (C), 389 (F) |
| Path A, the next `browser_navigate` | 515 · 517 ms | 1,501 · 1,502 ms | not recorded |
| **Path B** `browserai_resume` | **406** · **401** ms | **396** · **406** ms | 346 · 331 (C), none (F) |
| Path B, the next `browser_navigate` | 526 · 573 ms | 2,455 · 2,333 ms | 444 · 426 (C) |

> ⭐ **FIREFOX RESUMES WITHIN 3% OF CHROMIUM ON PATH A AND WITHIN 2% ON PATH B**,
> although it is 4.65× slower to first navigate. That is the second reading of
> the thing the previous entry called the first evidence and not argument
> that **a resume is about the DIRECTORY and not about the browser** -- and Path
> B, which relaunches a real browser, says it too.
>
> ⭐ **PATH B IS CHEAPER THAN PATH A ON BOTH FAMILIES**, by about 90 ms on
> Chromium and 88 ms on Firefox. Path A pays for a second process meeting a
> directory it does not own; Path B is already inside the process that does.

> ⚠️ `Corrected 2026-09-17 @ chromium 1245 · firefox 1548 · playwright-core
> 1.64.0-alpha-2026-09-17 (previously "**Resume costs 336 ms and 367 ms, and
> loses only `sessionStorage`.** Re-measured 2026-09-16 at chromium **1244** ...
> under `playwright-core` 1.64.0-alpha-2026-09-14")`. **The cost moved 336 and
> 367 → 375 and 379 ms, about 9%, and the durability claim is unchanged to the
> store.** ⭐ **Chromium cannot be the reason it moved**, and that is a control
> and not an inference: `chromium-1245` and `chromium-1244` are the same 308
> files at the same sizes with `chrome.exe` identical to the byte
> ([row 21](../re-verification.md)). What did change under the number is the
> **server**: `5d0d04f` landed between the two readings, and a resume now asks
> `ChildConnection.ChildHasGone` before it answers. 9% is also inside this
> machine's own drift on the same day
> ([the cost ratios moved 15-20% on an unchanged binary](#firefox-against-chromium-the-standing-cost-ratios)),
> so the two cannot be told apart from two readings and no attempt is made to.
>
> ⭐ **Firefox is the addition, not a correction.** 389 ms, first reading,
> against 375 and 379 on Chromium -- so the resume cost is **not** a Firefox
> cost ratio at all: the family that is 4.37× slower to first navigate resumes
> within 4% of the other. That is what a resume being about the *directory*
> and not about the browser looks like, and it is the first evidence for it
> that is not an argument.
>
> ⚠️ **The re-establishment procedure said "kill the node child, resume
> against the directory", and on 2026-09-16 doing exactly that did not produce a
> resume.** That measurement stands as a record of the product of that day and
> **has since been overtaken by a fix, not by a re-measurement** -- what it
> recorded was: with the node child killed under a **live** BrowserAI, the same
> server's `browserai_resume` answers *"This session is already open in this
> BrowserAI; nothing was changed"* in **7.8 ms**, and the next browser call
> **had not returned after 3 min 8 s**. So the number above is measured the way
> it still should be: the session is created and filled by one server, that
> server exits and its node child goes with it (verified gone by pid against a
> path BrowserAI owns), and **a second BrowserAI process meets the directory**.
> That is Path A, the case the feature exists for, and it is the shape
> `SessionRun` already uses for the move-versus-copy case.
>
> **The wedge was recorded and NOT diagnosed on 2026-09-16, was diagnosed on
> 2026-09-17, and was then fixed the same day.** The section below carries all
> three states in order.

> ⚠️ `Corrected 2026-09-16 @ chromium 1244 · playwright-core
> 1.64.0-alpha-2026-09-14 · @playwright/mcp 0.0.81 (previously "**Resume costs
> 515 ms and loses only `sessionStorage`.** Measured 2026-08-14")`. The cost
> moved 515 → **336 and 367 ms**; the durability claim is unchanged and is now
> asserted store by store and not listed in prose.
>
> ⚠️ **The re-establishment procedure said "kill the node child, resume against
> the directory", and doing exactly that does not produce a resume.** Measured
> 2026-09-16: with the node child killed under a **live** BrowserAI, the same
> server's `browserai_resume` answers *"This session is already open in this
> BrowserAI; nothing was changed"* in **7.8 ms** -- it is a no-op, not a
> relaunch -- and the next browser call against that session **had not returned
> after 3 min 8 s**, when the probe was stopped. So the number above is measured
> the only way it can be: the session is created and filled by one server, that
> server exits and its node child goes with it (verified gone by pid against a
> path BrowserAI owns), and **a second BrowserAI process meets the directory**.
> That is the case the feature exists for, and it is the shape `SessionRun`
> already uses for the move-versus-copy case.
>
> **The wedge is recorded and NOT diagnosed.** Whether the hang is bounded by
> anything, and whether a session in that state can be recovered without killing
> the server, was not established -- what is established is the 7.8 ms no-op, the
> wording, and that one call did not return inside 3 min 8 s.
> [`docs/probes/2026-09-16-resume`](../../docs/probes/2026-09-16-resume/README.md)
> is the rig, and it carries both shapes: the one-server arm that wedges and the
> two-server arm that measures.

### The resume wedge, measured -- 2026-09-17

> ✅ **THE PRODUCT MOVED AND THIS SECTION DID NOT UNTIL NOW. Added 2026-09-17,
> by addition: everything below is true of the slice it was measured against and
> is false of the current one.** The same three steps -- kill both `node`
> children by pid, `browserai_resume` in the same server, then one
> `browser_navigate` -- were re-run twice against a slice carrying `5d0d04f`,
> and **there is no wedge left to measure**:
>
> | | 2026-09-17, before `5d0d04f` | 2026-09-17, after -- two runs |
> |---|---|---|
> | `browserai_resume` after the kill | **7.68 ms**, *"already open in this BrowserAI; nothing was changed"* | **345.77 ms** and **330.92 ms**, carrying `SessionManager.ChildWasRelaunched` verbatim -- *"the browser server for this session had died and was relaunched ... no page is open, there are no tabs"* |
> | the next `browser_navigate` | **never returned in 900,000 ms** | **444 ms** and **426 ms**, `isError` false, a real page and a snapshot |
> | browsers under the browsers root | 8, then 0 after the kill, then 0 for fifteen minutes | 8, then **0** after the kill, then **8** again -- a fresh tree under the replacement child |
> | node children of the server | 2, then 0, then 0 | 2, then **0**, then **1** at the five-second poll |
>
> **What this does NOT re-measure, said where it is said and not in a
> footnote:** the probe resumes before it navigates, so a forward made
> *without* a resume was not exercised, and neither was
> `SessionErrors.BrowserServerHasGone`, the door-refusal that `5d0d04f` added
> for exactly that case. **The paragraph below beginning "Which product timer
> governs it" is therefore still unrefuted** -- nothing acquired a clock; what
> changed is that the call no longer reaches a dead child. Teardown was clean in
> both runs: the session destroyed, 0 browsers left.

**Nothing bounds it.** Kill a session's `node` child under a **live** BrowserAI,
resume in the same process, then make one browser call: the call **had not
returned after 15 minutes**, and 15 minutes was chosen as the largest timeout in
the product (10 minutes) plus five minutes of margin and not as a number the
probe felt like waiting. Measured 2026-09-17 against the published slice at
`1.0.1-alpha.0.25`, chromium **1244**, `playwright-core` 1.64.0-alpha-2026-09-14,
through
[`docs/probes/2026-09-16-resume/wedge-probe.js`](../../docs/probes/2026-09-16-resume/README.md);
the transcript and the process-log window are
[`docs/evidence/2026-09-17-resume-wedge`](../../docs/evidence/2026-09-17-resume-wedge/README.md).
`[FLOATS]`

| | |
|---|---|
| `browserai_init` then one `browser_navigate` | 333 ms, 407 ms |
| node children killed, by pid against a path BrowserAI owns | 2 killed; **all 8 browser processes went with them**, measured either side |
| `browserai_resume` in the same server | **7.68 ms**, *"This session is already open in this BrowserAI; nothing was changed"* |
| the next `browser_navigate` | **never returned in 900,000 ms** |
| a second client's `browserai_resume`, 31 s in | **refused in 15.3 ms**: *"is in use by PID 61684 ... Nothing was changed. BrowserAI does not wait for a lock"* |
| the first server, across the whole 15 minutes | alive, 0 node children, 0 browsers |

> **The 7.68 ms no-op corroborates the 2026-09-16 reading of 7.8 ms** and the
> refusal wording is byte-for-byte what that run recorded. What is new is that
> the hang was bounded deliberately and still did not end.

**Which product timer governs it: none.** The forward goes through
`ChildConnection.AskAsync`, which awaits
`_client.SendRequestAsync(request, cancellationToken)` under **the caller's token
and nothing else** -- there is no per-call timeout anywhere on that path. The
three timers a reader would reach for were all crossed or are not on it:

- `ChildConnection.ChildInitializationHang` is **10 minutes** and governs the
  child's `initialize` only. It was crossed at 600 s with no effect.
- `BrowserIdleTimer.DefaultIdlePeriod` is **10 minutes** and closes an idle
  browser. There is no browser left to close, and nothing fired.
- `LockScopes.PerDirectoryGate` is **120 seconds** and is taken and released
  before the call is forwarded, so it never sees the wait.

**What the process log records across the hang: one line, then silence.** The
server *did* notice, immediately --
`BrowserAI.Protocol.ChildProcessSession[4]`, `13:08:31.858Z`,
*"playwright-mcp[surface]: the peer closed its end of the connection"*, 347 ms
before the kill was even reported complete. **After that it writes nothing at
all** for the remaining fifteen minutes: no timeout, no refusal, no warning. So
the transport knows the peer is gone and the pending request is never told.

**Why `browserai_resume` does not help, stated as a mechanism and not as a
guess:** its question is *do I already own this directory*, and the answer is
still yes -- the session is in this process's own index and the live marker is
this process's. Whether the **child** behind it is alive is a different question
and nothing asks it.

**And a second client cannot recover it either**, which is the half the
2026-09-16 run left open. The directory lock is held by the wedged server, and
`browserai_resume` from a second process is refused by the ordinary in-use
refusal naming that pid. **The only recovery measured is the one the 2026-09-16
run used**: end the first server by pid, then destroy the session from a later
process. That worked here -- 12.6 MiB removed, nothing left in the index.

**To re-establish:** run `wedge-probe.js` with the published
`BrowserAI.Server.exe`, a scratch session directory and a bound in milliseconds.
It destroys its session on the way out. **It drives the product's shared data
root**, so never run it beside a suite run. ⚠️ **No product change is taken on
this** -- what to do about it is raised in
[`QUESTIONS.md`](../../QUESTIONS.md) and belongs to whoever owns the tool
surface.

**Proxying costs ~50 ms on a 500 KB payload.** From an equivalent Node prototype:
images passed through byte-identical (**509,620** base64 bytes), error shapes
preserved, ~50 ms added latency, ~300 ms one-off child spawn. It measured a
**Node** prototype and not the C# proxy, so it is `[UNVERIFIED]` as a
prediction of BrowserAI's own overhead -- a precedent, not a measurement of this
product.

**Suite costs, for cadence decisions:** real-child contract 2-5 s, smoke 10-30 s,
update 1-3 min. Estimates, not stopwatch figures. `[UNVERIFIED]`

## Firefox against Chromium: the standing cost ratios

✅ **RE-ESTABLISHED 2026-09-22 at chromium 1246 and firefox 1549 (`browserVersion`
156.0), clearing the `[STALE]` this section carried since 2026-09-21.** All three
surviving ratios were re-taken in **one sitting**, Chromium first and then
Firefox, at six rounds for Chromium and **nine for Firefox of which eight
produced a browser** -- see the round that did not, below, which is named and not
dropped. **Two ratios are unchanged and one moved back up**: RAM **1.19×**
and profile disk **2.76×** are identical to three figures, first navigate
**4.37× → 4.65×**. *Previously* ✅ **RE-ESTABLISHED 2026-09-17 at chromium 1245
and firefox 1548**, clearing the `[STALE]` this section carried for six hours,
and taken at **six rounds per family** and not the stated three -- because
the one axis that had flipped sign is the one three rounds cannot settle, and a
second set of three costs four minutes. *Previously, and kept because it is what the debt looked like:*
"⚠️ **`[STALE]` since 2026-09-17, hours after these were taken and against
both families at once.** The `playwright-core` pull-forward moved **chromium 1244
→ 1245 and firefox 1544 → 1548**, and the header below says of these four that
*every one of them moves with a browser revision*. Nothing here is adjusted:
three rounds per family is the stated minimum, so re-taking them is a measurement
session, with
[`docs/probes/2026-09-17-cost-ratios`](../../docs/probes/2026-09-17-cost-ratios/README.md)
as the rig. [Re-verification row 34](../re-verification.md) carries the debt."

**1.19× RAM, 4.65× first navigate, 2.76× profile disk -- and idle CPU is NOT
ESTABLISHED and is no longer an axis of this section.**
*Corrected 2026-09-22 (previously "1.19× RAM, 4.37× first navigate, 2.76×
profile disk").*
*Corrected 2026-09-18 (previously "and idle CPU has no sign at this sample
size")* -- that sentence left the axis open and this one closes it: **the axis is
retired as unmeasurable on a developer machine** (Q215 = a) and is
[a row in what this project has not established](../not-established.md) and
not a ratio in the table below. **The three that remain are measured and
unchanged.** Measured 2026-09-17 against Chromium as the unit, **six
rounds per family**, through the product's own `browserai_init` →
`browser_navigate` against a local origin, at chromium **1245** / 154.0.8037.0
and firefox **1548** / 155.0 under `playwright-core` 1.64.0-alpha-2026-09-17.
`[FLOATS]` `[MACHINE]` -- the absolute numbers are this machine's. Medians, with
the observed range beside each.

**Measured 2026-09-22.** Chromium **6 rounds**, Firefox **8 rounds that produced
a browser out of 9 run**. Medians, with the observed range beside each.

| Axis | Chromium (6 rounds) | Firefox (8 of 9) | Firefox : Chromium |
|---|---:|---:|---:|
| Resident set, whole browser tree | **494.5** MB (483.5-505.4) | **587.5** MB (584.8-590.9) | **1.19×** |
| First navigate, cold -- includes the launch | **576** ms (567-597) | **2,679** ms (2,547-2,768) | **4.65×** |
| Second navigate, browser already up | **85** ms (72-109) | **62** ms (57-70) | **0.72×** |
| ~~Idle CPU over 30 s, no page activity~~ | **516** ms (405-767) | **704** ms (329-874) | ~~**1.36×**~~ -- **RETIRED 2026-09-18, NOT ESTABLISHED**, and the 2026-09-22 reading is a third confirmation and not a new number: the two columns **overlap completely again**, Firefox's lowest (329) below Chromium's lowest (405) and Firefox's highest (874) above Chromium's highest (767). The columns stand as readings; the ratio is struck |
| Profile directory on disk | **13,207,350 B** (181 files) | **36,474,446 B** (66 files) | **2.76×** on all eight |
| Processes under the browsers root | 8 · 9 · 9 · 8 · 8 · 8 | 7 every round | **0.88×** |

⚠️ **ONE FIREFOX ROUND IN NINE PRODUCED NO BROWSER AT ALL, and it is named
here and not dropped.** Round 2 of the sitting: `browserai_init` answered
normally in **459 ms**, and then **both** navigations returned only after
**180,031 ms** and **180,679 ms** -- three minutes each, which is neither
BrowserAI's own timeout nor anything this product writes. When the rig looked,
**zero** processes were running under the browsers root, resident set **0**, and
the profile had reached **1,159,208 B across 25 files** against **~36.47 MB
across 66** on every healthy round. **THE PRODUCT'S OWN STRAY SWEEP IS EXCLUDED,
from its own announcements and not by argument**: every sweep in the whole
sitting -- fifteen of them, including the failing round's own at
`23:16:43.94` -- reported `candidates=0` and terminated nothing. **What it WAS
is not established by THIS rig**, because `ratios-probe.js` destroys its session
on the way out and takes the session's own log with it, so the one record that
would have said why was deleted by the measurement.

✅ **BUT THE SAME SIGNATURE WAS CAUGHT WITH ITS ERROR TEXT ATTACHED LATER THE
SAME EVENING, in the row 38 resume runs, and the identification is stated as
INFERRED and not measured.** A Firefox `browser_navigate` there returned
after **180,023 ms** carrying:
*`TimeoutError: async initializeServer: Timeout 180000ms exceeded.`* with a call
log reading `<launching> ...\firefox-1549\firefox\firefox.exe -no-remote
-headless -profile ... -juggler-pipe about:blank`, then `<launched> pid=147320`,
then `[pid=147320][err] *** You are running in headless mode.` -- **and nothing
further**. So Firefox *started* and never finished `initializeServer`; the
juggler handshake never completed. **180,000 ms is upstream's own
`DEFAULT_PLAYWRIGHT_LAUNCH_TIMEOUT`, `3 * 60 * 1e3`, which
[this article already documented](#timings-spawn-resume-idle-close-proxy-overhead)
years of readings ago** -- and the paragraph there says a launch that approaches
it *"is not slow -- it is stuck"*. That is the number that ended both calls.
**What makes this an inference and not a measurement** is that the
cost-ratio round's own error text was destroyed with its session: what matches is
the family, the evening, the zero process count and the duration to within
**31 ms of 180,000** on one and **8 ms** on the other. **Rate: 2 stuck launches in
roughly 20 Firefox launches that evening, and 0 in roughly 20 Chromium ones.** Three more rounds were then run and all three were
clean, so the observed rate is **1 in 9**. The medians above are over the eight
rounds that produced a browser; **including the failed round would have put a
180-second navigate and a zero resident set into a median**, which is a different
claim, not a more honest one.

> ⚠️ **THE CHROMIUM CONTROL IS STRONGER THAN IT WAS: 1246 IS 1245 IS 1244.**
> All three revisions hold the same 308 files at the same 308 sizes and
> `chrome.exe` is SHA-256 `e3390ab4...` in every one
> ([the licensing read](../packaging/dependencies.md#third-party-payload-as-shipped)
> re-established that at 1246 on the same day). So Chromium's own column between
> 2026-09-17 and 2026-09-22 is **entirely the instrument**, and it says how much
> each axis drifts on a binary that cannot have changed:
>
> | Chromium's own column, one unchanged binary | 2026-09-17 | 2026-09-22 | drift |
> |---|---:|---:|---:|
> | Profile directory | 13,207,388 B | 13,207,350 B | **−0.0003%** |
> | Resident set | 499.2 MB | 494.5 MB | **−0.9%** |
> | Idle CPU over 30 s | 478 ms | 516 ms | **+8.0%** |
> | First navigate | 500 ms | 576 ms | **+15.3%** |
> | Second navigate | 72 ms | 85 ms | **+18.3%** |
> | Processes (median) | 9 | 8 | one round of six |
>
> **Read the Firefox movements against that column and almost nothing is left.**
> Firefox's first navigate moved **+22.7%** (2,184 → 2,679 ms) while Chromium's
> moved **+15.3%** on a binary that did not change; its second navigate moved
> **+25.7%** against Chromium's **+18.3%**; its resident set moved **−1.0%**
> against Chromium's **−0.9%**. **So the ratio moving 4.37× → 4.65× is mostly
> the machine and not Firefox 156.0**, and what is genuinely established is the
> band this axis has occupied across three sittings: **4.37×, 4.62×, 4.65×**.
> The two axes that did NOT drift are the two worth quoting to three figures, and
> they are the two that came back identical: **profile disk 2.76× on every one of
> eight rounds** -- Firefox's own profile grew **+26,066 B, 0.07%**, and the ratio
> did not move at the third figure -- and **RAM 1.19×**.
>
> ⚠️ **The processes row moved 0.78× → 0.88× and it is Chromium's median that
> moved, not Firefox's.** Firefox was **7 on all eight rounds**, as it was on all
> six before. Chromium went `8 8 9 9 9 9` to `8 9 9 8 8 8`, a median of 9 to a
> median of 8 -- **one round either way flips it**, which is what a median of a
> small integer does. Recorded as a number that moved, not as a finding.

> ⚠️ `Corrected 2026-09-17 @ chromium 1245 · firefox 1548 · playwright-core
> 1.64.0-alpha-2026-09-17 (previously "**1.19× RAM, 4.6× first navigate, 0.77×
> idle CPU, 2.76× profile disk.** Measured 2026-09-17 ... **three rounds per
> family** ... at chromium **1244** / 154.0.8037.0 and firefox **1544** / 155.0
> under `playwright-core` 1.64.0-alpha-2026-09-14", with Chromium 487.5 · 494.7 ·
> 507.6 MB, 413 · 417 · 1,297 ms, 39 · 57 · 66 ms, 312 · 813 · 843 ms,
> 13,207,311 B and 8 · 9 · 10 against Firefox 587.0 · 589.8 · 590.2 MB,
> 1,907 · 1,923 · 2,962 ms, 49 · 52 · 53 ms, 532 · 624 · 750 ms, 36,444,338 B
> and 7 · 7 · 7)`. **Three axes came back identical, one moved inside its own
> spread, and two moved -- one of them across 1.0 for the second time in a day.**
>
> - **RAM 1.19×, profile disk 2.76× and processes 0.78× are unchanged**, the
>   profile row to three figures on every one of six rounds.
> - **First navigate 4.62× → 4.37×**, inside the per-round band of both runs.
> - **Second navigate 0.90× → 0.68×.** *Corrected with it (previously "The
>   second-navigate row has no such spread and is where the two families are
>   genuinely close")* -- at 0.68× they are not close, and the direction has been
>   Firefox being the **faster** of the two all along once its browser is up.
> - ⚠️ **Idle CPU 0.77× → 1.31×, the second sign reversal this one axis has
>   recorded inside a single day.** What follows from that is a conclusion about
>   the axis and not about either browser.

⚠️ **This run carries a control the morning's could not, and it is the reason
the paragraph above can say which movements are real: Chromium did not change.**
`chromium-1245` and `chromium-1244` hold the **same 308 files at the same 308
sizes** and `chrome.exe` is SHA-256 `e3390ab4...` in both, because the archive is
keyed on `browserVersion` and 154.0.8037.0 did not move with the revision
([row 21](../re-verification.md), and again in
[the licensing read](../packaging/dependencies.md#third-party-payload-as-shipped)).
**So every movement in Chromium's own column between the two runs is the
instrument and not the browser**, which makes the pair a repeatability test
of this rig:

| Chromium's own column -- one unchanged binary | morning, 3 rounds | evening, 6 rounds | what that says |
|---|---:|---:|---|
| Profile directory | 13,207,311 B | 13,207,388 B | **+77 B, 0.0006% -- an instrument** |
| Resident set | 494.7 MB | 499.2 MB | **+0.9% -- an instrument** |
| First navigate | 417 ms | 500 ms | +20%, and the morning's 1,297 ms outlier did not recur |
| Second navigate | 57 ms | 72 ms | +27% |
| Idle CPU over 30 s | 813 ms | 478 ms | **−41%, on a 2.65× spread inside one session** |

**Idle CPU is not measuring the browser at this sample size, and no number of
further rounds of this instrument will fix that.** Chromium's six rounds span
**360-955 ms** -- 2.65×, on a binary that did not change -- and Firefox's span
**266-781 ms**. **The two distributions overlap completely**: Firefox's lowest
round is below Chromium's lowest and Chromium's highest is above Firefox's
highest. So the **1.31×** in the table is what two medians happen to say and is
**not a claim that Firefox burns more idle CPU**. Across three measurements this
axis has read ~24×, 0.77× and 1.31×, and exactly one thing is established by
all three together: **Firefox does not burn an order of magnitude more idle CPU
than Chromium.** Which of the two burns more, if either, is **not established**,
and the reason is the instrument -- `TotalProcessorTime` differenced across one
30-second window, on a machine with other things on it -- and not the round
count. ✅ **RETIRED 2026-09-18, and of those three answers it is the third.**
*Corrected 2026-09-18 (previously "⚠️ **What to do about that is not decided
here**: a longer window, CPU sampled rather than differenced, or the axis
retired as unmeasurable on a developer machine are three different answers, and
choosing between them belongs to whoever wants the number.")* -- the axis is
**not established** (Q215 = a) and is listed as such in
[what this project has not established](../not-established.md).

**Nothing consumes it, and that was checked, not assumed.** These four
ratios were the whole of the evidence behind Chromium being the default family
until 2026-09-17, when that decision was re-grounded on the maintainer's own
reason (Q206) and stopped resting on a measurement at all. A grep over the
repository for the axis finds it in this article, in
[row 34](../re-verification.md), in the changelog entries that record the
readings, in the rig's own README and in two sentences that ARGUE from it --
[the charter's browser-families row](../../DECISIONS.md) and `SessionManager`'s
doc comment, both of which said *idle CPU reversed sign* while explaining why
the cost argument no longer carries the default. Both are corrected by addition:
the conclusion is unchanged and the reason for it is now the sign reversals
themselves and not either direction being true.

**What survives is one sentence, and it is all three measurements together
support: Firefox does not burn an order of magnitude more idle CPU than
Chromium.** The recorded ~24× is refuted by both later readings. Which of the
two burns more, if either, is not established, and **re-opening it needs a
different instrument and not more rounds of this one** -- a longer window, or
CPU sampled and not differenced, on a machine with nothing else running.

⚠️ **The ratio is the transferable half, and this pair of runs is evidence for
that and not an assertion of it.** Both families were 15-20% slower to first
navigate in the evening than in the morning -- Chromium 417 → 500 ms on a binary
that did not change, Firefox 1,923 → 2,184 ms on one that changed five files --
and **the ratio moved by 5%**, 4.62× → 4.37×. Machine-wide drift divides out of
a ratio and does not divide out of a time.

> ⚠️ `Corrected 2026-09-17 @ chromium 1244 · firefox 1544 · playwright-core
> 1.64.0-alpha-2026-09-14 (previously "**~2× RAM, ~10× first navigate, ~24× idle
> CPU, ~20× profile disk.** Measured 2026-08-14 against Chromium as the unit",
> carrying `[UNVERIFIED]` as to method)`. **Every one of the four moved, and one
> of them changed SIGN.**
>
> - **Idle CPU is the reversal, and it is the largest single error this article
>   has carried.** The recorded ~24× said Firefox burns two dozen times
>   Chromium's idle CPU; measured over three 30-second windows with no page
>   activity, Firefox burns **less** -- 0.77× on medians, and Firefox's *worst*
>   round (750 ms) is below Chromium's *worst* (843 ms). A claim that was not
>   merely imprecise but pointed the wrong way.
> - **RAM, first navigate and profile disk all moved the same direction:
>   towards each other.** 2× → 1.19×, 10× → 4.6×, 20× → 2.76×.
> - **What survives is the SIGN on three of four axes**, which is what
>   [the time-to-MCP-ready figures](#timings-spawn-resume-idle-close-proxy-overhead)
>   already corroborated: Firefox is slower to first answer, heavier in memory
>   and larger on disk. The *magnitudes* were wrong by between 1.7× and 7×, and
>   the fourth axis was wrong outright.
>
> **This is the first time these four have been measured with a preserved
> harness.** The 2026-08-14 figures came from a session whose rig was not kept,
> which is why the entry carried `[UNVERIFIED]` as to method and told the reader
> to re-measure before any decision turned on them. The rig is now
> [`docs/probes/2026-09-17-cost-ratios`](../../docs/probes/2026-09-17-cost-ratios/README.md)
> and the entry no longer carries that marker.

⚠️ **Read the first-navigate row with its spread, not its median -- and the
spread is itself not stable.** *Corrected 2026-09-17 (previously "A single pair
would have supported any answer in that band, which is how an order-of-magnitude
claim survives being quoted. The second-navigate row has no such spread and is
where the two families are genuinely close.")* The morning's three rounds were
Chromium 413, 417 and **1,297** ms against Firefox 1,907, 1,923 and **2,962** ms
-- one outlier each, both high, per-round ratio **1.47× to 7.16×** against a
4.62× median. The evening's six have **no outlier in either family**: Chromium
490-534 ms, Firefox 2,084-2,339 ms, per-round ratio **4.00× to 4.68×**. Same
rig, same machine, **the same Chromium binary** -- so a single pair would have
supported any answer between 1.47× and 7.16× in the morning and nothing outside
4.00-4.68× in the evening. **Three rounds is a floor and not a sufficiency**,
and how much it buys is a property of the day.

⚠️ **The profile-disk row is the tight one and the only one worth quoting to
three figures**: 2.76× on all six rounds. Five of Chromium's six profiles fall
inside **6 bytes** of each other, with one round 2,051 B larger; Firefox's six
span 6,347 B. The *file* counts run the other way -- Chromium 181 files in
13.2 MB, Firefox **66** in 36.4 MB -- so a comparison by file count says the
opposite of one by bytes, and neither is wrong. ⚠️ **Firefox's profile lost a
file on this roll and gained bytes**: *corrected 2026-09-17 (previously "Firefox
67 in 36.4 MB", 36,444,338 B)* -- **67 → 66 files** and 36,444,338 →
**36,448,380 B**, measured at 1548 against the 1544 reading. Chromium's file
count is unmoved at 181, which is what a byte-identical browser should do.

**What this does NOT settle, and what has since been settled elsewhere.**
*Corrected 2026-09-17 (previously "These four were *\"the whole of the evidence
behind Chromium being the default family\"*. Three of them are now between 1.7×
and 7× smaller than the figures that argument was made from and the fourth points
the other way, so **the evidence behind that decision has changed and the
decision has not been revisited**. Nothing here re-opens it; that belongs to
whoever owns the charter, and it is raised there rather than settled here.")* It
was raised there and it was answered on 2026-09-17, in the maintainer's words:
*"the reason for the default is that chrome is the most widely used"*.
**Chromium stays the default and these four ratios are no longer offered as the
reason for it** -- the claim that they were the evidence is retired and not
re-argued, and [the charter](../../DECISIONS.md) now carries the ground as a
decision and not as a measurement. **Nothing here is retracted**: the
measurements above stand, and what they no longer do is carry a choice.

**To re-establish:** open one session per family through the product, drive the
same navigation in each, and compare resident set, wall time to first paint and
profile-directory size on disk. *Corrected 2026-09-18 (previously "... compare resident
set, wall time to first paint, idle CPU over a fixed window with no page
activity, and profile-directory size on disk")* -- **three axes, not four.** The
rig still takes an idle-CPU reading and a re-run will print one; it is not a
row of this section any more, for the reason above, and a reading printed is
not an axis re-established. **Three rounds per family is the floor and six is what this section was
last taken at** -- *corrected 2026-09-17 (previously "**Three rounds per family
minimum** -- one pair cannot distinguish a ratio from an outlier, which is the
defect the spread above exposes")*, which is still true and is no longer the
whole of it: three rounds cannot distinguish a ratio from an outlier, and six
were not enough to give the idle-CPU axis a sign. Count processes by
**`ExecutablePath` under the browsers root** and never by image name: a foreign
Firefox and Chrome are on this machine. The **ratio** is the transferable half;
the absolute numbers are whichever machine ran them. ⚠️ **Re-take both
families in one sitting.** The two runs behind this section were six hours apart
and the machine moved 15-20% between them on an unchanged binary, so a ratio
assembled from two sittings measures the gap between them as much as the gap
between the browsers.
