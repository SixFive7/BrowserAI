<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Payload sizes, first-run provisioning and timings

**Versions in force** unless an entry says otherwise: `@playwright/mcp` 0.0.79 · `playwright-core` 1.63.0-alpha-2026-08-05 · Chrome for Testing 152.0.7977.8 (`chromium-1237`) · Firefox 153.0 (`firefox-1539`) · `ffmpeg` revision 1011 · `winldd` revision 1007 · Node v24.19.0 LTS · Windows 11 Pro 26200.
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

**The six rows above total ~806 MB installed, ~239 MB compressed** — 7z LZMA2
`-mx=5`. That is the figure for a **bundled** build, browsers inside the
installer, and it excludes BrowserAI's own binary. **Nothing ships in that shape
today.** Browsers are [provisioned on first run](#first-run-provisioning), so the
installed payload is what `current\` holds, and it has now been **weighed rather
than added up**: an installed `current\` measures **130,434,952 B = 124.39 MiB
across 200 files** (`BrowserAI.exe` 17,853,952 · `payload\` 111,984,018 ·
`BrowserAI.xml` 596,517 · `sq.version` 465) — **466 B more** than the *packaged*
130,434,486 in [row 85](../re-verification.md), which is not a
discrepancy: 465 of those bytes are `sq.version`, which Velopack writes into
`current\` at install and which the package does not carry. **Disk after first
run is 130,434,952 + 451,389,780 =
581,824,732 B = 554.87 MiB ≈ 582 MB**. The ~806 MB total is kept
because a bundled build is the fallback if the Chrome-for-Testing redistribution
question is ever resolved favourably — but it is **not** the figure for disk
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
> addend was a fortnight old. The replacement is a **weight, not a sum** — the directory that
> actually ships, measured whole — so the next stale term cannot hide inside it.
> The old figure is retained above in this note rather than deleted, because a
> reader who learned `116.40` needs to find out it was reviewed and replaced.

**BrowserAI's own binary is ~17.0 MiB and it moves on every commit.** Three
publishes present on the machine on **2026-08-17** measured **17,853,952 B**
(the one inside the 0.9.x install above, 17.03 MiB), **17,911,808 B**
(`artifacts/publish-release`) and **17,954,304 B** (the current Release publish)
— `PublishAot`, win-x64, self-contained, all three. **Do not treat any of them as
*the* size.** The spread across three artifacts of the same product on one day is
the point: this is the most volatile figure in this article, and it floats on
**our own product** rather than on an upstream, which is why no marker is stamped
on it — the checkable figure is the packaged `current\` above, which is a
directory somebody can weigh, and it is carried by
[row 85](../re-verification.md) with the rest of the update lane's
numbers.

> ⚠️ **Corrected 2026-08-17 (previously "**BrowserAI's own binary is 9.76 MiB** —
> 10,233,856 bytes … measured by [the 2026-08-15 spike](../mcp/sdk.md#driving-the-whole-sdk-aot-passthrough-filters-and-cancellation)").**
> The spike's measurement stays true of the spike and is still recorded there;
> what was wrong was carrying it forward as the product's size. It had already
> been contradicted in this repository without this line being swept — the
> build order's own step 5 recorded **10,461,696 bytes** the following day —
> which is the same half-done correction the entry it replaced was itself
> written to fix.

The trimmed self-contained fallback at ~70 MB is
still `[UNVERIFIED]`, nothing having been built in that configuration.

> ⚠️ **Where ~380 MB came from, and why it is retired.** The update budget was
> written against a "~380 MB footprint dominated by Chromium", with ~600–700 MB
> transient disk during a swap and a full re-extraction of ~380 MB per update.
> That figure is `806 − 427`: the payload of an intermediate design that had
> dropped full Chromium but still shipped `chrome-headless-shell`. **`current\`
> contains no Chromium of any kind today**, so the number and its "dominated by
> Chromium" description are both dead. Against a ~117 MB payload the swap holds
> old + new `current\` simultaneously, so the transient budget is **~235 MB** and
> a re-extraction is ~117 MB; browsers live outside `current\` and are untouched
> by the swap (verified 2026-08-15,
> [kb: Velopack](../packaging/velopack.md#where-state-may-live--the-finding-the-provisioning-design-rests-on)).
> The **compressed** size of the current payload has never been measured — the
> ~239 MB above is for the browser-dominated tree, and `node.exe` is now the bulk
> of what is left — so every full-package download figure remains `[UNVERIFIED]`.

**Verified 2026-08-16 @ Node v24.19.0 / `@playwright/mcp` 0.0.79, by assembling
the payload** ([build-order step 3](../../plan/build-order.md#3-the-payload-build)).
Both rows hold to the byte, and the unit in this table is **MiB** rather than MB:
`node.exe` is **92,825,416 B = 88.53 MiB**, and `node_modules` is
**18,993,773 B = 18.11 MiB** — of which `playwright-core` is 13.18 MiB,
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
[§A](../../plan/A-runtime.md#a-ship-and-own-the-runtime) requires Node's full
`LICENSE` to ship — it aggregates the OpenSSL, ICU, V8, zlib and c-ares terms —
and the obvious build, one `GET` of `win-x64/node.exe`, ships no licence at all
and reports nothing. Re-establish by listing both URLs. `[FLOATS]`

**A single `node.exe` drives the full MCP protocol** — no npm, no `node_modules`
belonging to Node, no `.cmd` shims. Verified by execution. Node **v26 is Current
rather than LTS and its `node.exe` is 10 MB larger**. `[FLOATS]`

**The vendored JS tree contains zero native binaries** and is portable as-is.
**It also declares no install script**: verified 2026-08-16 across the resolved
`package-lock.json`, the only `hasInstallScript` entry is `fsevents`, which is
`optional` and `darwin`-only and is therefore never installed on Windows. A
vendoring build that runs `npm install` without `--ignore-scripts` — which is
what `build/Build-Payload.ps1` does deliberately, so an upstream change is not
suppressed — currently executes nothing. `[FLOATS]`
**`ffmpeg` is required for video capture** — without it the `video` artifact type
throws. `[FLOATS]`

## First-run provisioning

**Settled 2026-08-15 and re-measured 2026-08-16 by exact `content-length` from
the CDN: 203.8 MB down.** `chrome-win64.zip` 202,283,919 B + `ffmpeg-win64.zip`
1,411,741 B + `winldd-win64.zip` 128,684 B = **203,824,344 B**, all three
byte-identical to the 2026-08-15 figures at the same revisions (chromium
**1237** / 152.0.7977.8, ffmpeg **1011**, winldd **1007** — the revision did not
move). Arithmetic for slower links: **2 m 43 s at 10 Mbps, 27 m 11 s at 1 Mbps**.
Peak disk during provisioning is **~640 MiB**, while the archive and the
extracted tree coexist. **This file is where that number lives** — the rest of
the repository cites it rather than restating it. Re-establish with a `HEAD` on
the three URLs below. `[FLOATS]`

> ⚠️ **Corrected 2026-08-16 @ chromium rev 1237 (previously "433 MiB on disk …
> chromium 428 MiB + ffmpeg 4 + winldd 1").** **On disk it is 430.48 MiB**, and
> the old figure was three rounded components added up. Measured twice by
> provisioning into an empty root and summing the files: **451,389,780 B across
> 318 files** — `chromium-1237` 447,613,809 B (426.88 MiB), `ffmpeg-1011`
> 3,517,342 B (3.35 MiB), `winldd-1007` 258,560 B (0.25 MiB) and `.links` 69 B.
> Note that 426.88 is exactly what [the component table](#component-sizes) already
> recorded for full Chromium, so the two halves of this file disagreed by
> 2.5 MiB.
>
> **The downstream "≈ 570 MB after first run" survives, and the reason is worth
> stating because it nearly produced a second wrong number.** That figure adds a
> payload quoted in **MB** to browsers quoted in **MiB**, and the payload's
> components are themselves MiB — so the honest sum is 116.40 + 430.48 =
> **546.88 MiB, which is 573 MB**. The recorded ≈ 570 was right by way of two
> conflations that cancelled. It is now stated in one unit above, and the first
> attempt at this correction wrote "≈ 548 MB" by mixing them the other way.

**End to end it takes 12.6 s and 12.0 s on a ~300 Mbps link**, measured twice on
2026-08-16 into an empty browsers root, exit 0 both times. Phase boundaries from
the installer's own output, timestamped per line: Chromium's download and
extraction together take **0.3 s → 11.7 s**, `ffmpeg` a further **0.5 s** and
`winldd` **0.4 s**. Re-establish by timing
`node.exe cli.js install-browser chromium --no-shell --no-progress` against a
fresh directory. `[FLOATS]` `[MACHINE]`

> ⚠️ **Corrected 2026-08-16 (previously "20.3 s on a 300 Mbps link, measured
> 2026-08-14 … an upper bound rather than a measurement").** It was an upper
> bound because that run also fetched `chrome-headless-shell`, which is
> [no longer provisioned](../../README.md#settled-2026-08-15). The two runs above
> are of what BrowserAI actually downloads, so the figure is now a measurement of
> the thing rather than of a superset of it.

**Firefox 153.0 (rev 1539) is 125,706,704 B down and 352,898,062 B — 336.55 MiB
— on disk**, provisioned in **6.2 s** on the same link, measured 2026-08-16 by
`install-browser firefox --no-shell --no-progress`. BrowserAI creates no Firefox
sessions ([step 17](../../plan/build-order.md#17-firefox)); the tree exists
because [§E](../../plan/E-lifecycle.md#zero-process-leakage-the-job-object-contract)'s
containment contract is stated against **both** families and the acceptance test
needs a real one. `[FLOATS]`

**⚠️ Chrome for Testing has exactly one mirror, so the retry rotation does not
help it.** Read 2026-08-16 out of `playwright-core/lib/coreBundle.js`: `cftUrl`
returns `{ path: "builds/cft/${browserVersion}/win64/chrome-win64.zip", mirrors:
["https://cdn.playwright.dev"] }` — one host, and **no**
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
configured server (`10.20.30.254`) — and a **tight retry loop makes it worse**,
because the failures are negatively cached: twenty queries with no delay left
both names unresolvable for the following minute, six queries three seconds apart
resolved on the first attempt. It presents as `EAI_AGAIN` then `ENOTFOUND` in the
installer's output and looks exactly like an outage. `[MACHINE]`

> **What this supersedes, kept so the old numbers are recognisable rather than
> mysterious.** The download was previously stated four ways across the
> repository — 202.3 MB, 323.5 MB, ~300 MB, and ~0.9 GB peak disk — and this
> file called the size `[UNVERIFIED]` on the grounds that only a run could settle
> it and no arithmetic may. A run settled it. 202.3 MB was **one term** of the
> 2026-08-14 sum, whose total was *"chromium 202.3 MB + shell 119.7 MB + ffmpeg +
> winldd = 323.5 MB down, ~700 MiB on disk"*, and the superseded slow-link
> figures — 4 m 19 s at 10 Mbps, 43 m at 1 Mbps — belonged to that larger total.
> The [2026-08-15 decision](../../README.md#settled-2026-08-15) to run full Chromium
> in every mode stopped provisioning the shell, which is what moved the number:
> the old measurement was never wrong, it stopped applying.

**`PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1` does not stop the explicit installer.**
Measured 2026-08-16 @ `playwright-core` 1.63.0-alpha-2026-08-05: with the
variable set and `PLAYWRIGHT_BROWSERS_PATH` pointed at an **empty** directory,
`cli.js install-browser ffmpeg --no-progress` downloaded and extracted anyway.
The flag is read in exactly two places in `lib/coreBundle.js` —
`installBrowsersForNpmInstall`, the npm postinstall path, and
`ensureConfiguredBrowserInstalled`, the server's start-up auto-install — and
`registry.install()`, which the `install-browser` command calls, does not
consult it. **Both halves matter to us:** the variable is
[mandated in the child's environment](../../README.md#the-five-rules-that-make-floating-safe)
to stop the child provisioning behind our back, and it does still close that
door; but a build or a provisioning subsystem that relied on it as a global
kill-switch would be relying on something that was never true. Re-establish by
running the command above against a fresh directory. `[FLOATS]`

**Installing `ffmpeg` on Windows pulls `winldd` with it**, unasked — the same
run produced both `ffmpeg-1011` and `winldd-1007`, which is why a browsers root
seeded by hand needs all three directories rather than just Chromium's.
`[FLOATS]`

**In-session recovery is proven.** The same child navigates successfully once the
install lands, with no restart. `[FLOATS]`

**The revision is pinned for free and is never looked up online.**
`playwright-core/browsers.json` carries the revision and `browserVersion`; the URL
is built by substituting that version into a template that **307s** to Google's
bucket. That file is inside the artifact and **no "latest" lookup exists anywhere
in the registry code**, so a release knows forever which browser it wants. Old
builds still resolve back to **Chrome 115 (Jul 2023)** — about three years of
evidence — but **Google documents no retention policy**, so it is evidence and
not a guarantee. `[FLOATS]`

**Egress hosts:** `cdn.playwright.dev`, `storage.googleapis.com`,
`playwright.download.prss.microsoft.com`. `HTTPS_PROXY` / `HTTP_PROXY` /
`NO_PROXY` / `ALL_PROXY` and **`NODE_EXTRA_CA_CERTS`** are honoured on the
download path; **SOCKS is not supported** there. `[FLOATS]`

**`PLAYWRIGHT_BROWSERS_PATH` must be absolute.** A relative value resolves
against `INIT_CWD` — inherited from any npm ancestor — before `cwd`. `[FLOATS]`

**Layout under the browsers root, verified by execution:** `[FLOATS]`

```
<browsers-root>\
  chromium_headless_shell-1237\chrome-headless-shell-win64\chrome-headless-shell.exe
  chromium-1237\chrome-win64\chrome.exe
  ffmpeg-1011\ffmpeg-win64.exe
```

Note the asymmetry: the **outer** directory uses underscores, the **inner** one
dashes, so a path built consistently is wrong. **No sentinel file is needed to
launch** — not `INSTALLATION_COMPLETE`, not `DEPENDENCIES_VALIDATED`; the only
launch-time check is file accessibility of the executable.

**`.links/` lives in the browsers root and nowhere else.** Read 2026-08-16 in
`playwright-core/lib/coreBundle.js` at `playwright-core`
1.63.0-alpha-2026-08-05: every reference is `path.join(registryDirectory,
'.links')` — in `install()`, `uninstall()` and `listInstalledBrowsers()` — so it
is **never** written into `node_modules`, and a payload build has nothing to
strip. Each file is named for the SHA-1 of an installing `playwright-core`
package directory and contains that directory's absolute path, one per line;
verified by running the installer from a fresh tree and reading the file it
produced. It therefore records the machine that **installed** the browser, which
under [first-run provisioning](#first-run-provisioning) is the user's machine
rather than a build machine. **Do not delete it:** the stale-browser GC treats a
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
requires; and `.wasm`/`vite` assets loaded by path. SEA would also save nothing —
its output *is* a copy of `node.exe` plus the blob. `vercel/pkg` was archived
**2024-01-13**. Bun and Deno both carry open issues on the Playwright
browser-launch path. `[FLOATS]`

## Timings: spawn, resume, idle close, proxy overhead

`[MACHINE]` for every number, `[FLOATS]` for what they are numbers *about*.

> ⚠️ **Only the resume figure carries a date in the charter.** Spawn, navigation,
> idle close and proxy overhead are all recorded undated, so treat their dates as
> `[UNVERIFIED]` and re-stamp each at the next run. The numbers themselves are
> carried forward exactly as written — none has been adjusted.

**Child spawn costs ~300 ms.** That is the baseline a flat 5 s discovery probe
would be paid against ([the protocol split](../mcp/protocol.md#the-protocol-split)), and the
per-instance price of one node child per handle.

**A real navigation costs 0.43 s** — `browser_navigate` to
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
> So the shape of the old claim holds — an idle session falls back to roughly the
> node child's own footprint — while the totals are ~496 MB → ~118 MB rather than
> 329 → 110, and the relaunch is **2.2× the recorded figure**. The old numbers
> carried no date and no version, which is why nobody could tell whether they had
> moved or had always been wrong.

**The relaunch is upstream's own behaviour, not something a caller or a proxy has
to arrange.** Playwright creates the browser lazily on first use, so the call
after a close simply works: no error, no `"browser is closed"` text on any path,
and a snapshot immediately afterwards returns the new page. This is the
measurement [the browser-idle timer](../../plan/C-sessions.md#lifetime-one-timer-and-reclaim-is-forever)
rests on — if the relaunch were not implicit, the timer would be a way of
breaking a session rather than a way of reclaiming memory.

⚠️ **`browser_close`'s own result text reads as though it closed a tab, and it
does not.** It answers *"No open tabs. Navigate to a URL to create one."* with
`await page.close()` as the code it ran — yet every process under the browsers
root is gone afterwards, because closing the last page tears the persistent
context down and the browser with it. A reader who trusted the wording would
conclude the timer does nothing. Called again with no browser open it answers the
same text, is **not** an error, and costs 156–514 ms — so a close that races
anything costs a round trip rather than a failure.

**How to re-establish all of the above:** drive a real child directly —
`node <payload>/mcp/node_modules/@playwright/mcp/cli.js --config <cfg> --sandbox`
with `PLAYWRIGHT_BROWSERS_PATH` set — through `initialize` → `browser_navigate`
→ `browser_close` → `browser_navigate`, counting processes whose
**`ExecutablePath` is under the browsers root** at each step and reading
`WorkingSet64`. Never match a process by image name: a foreign Firefox
and Chrome are on this machine. The *behaviour* half is asserted on every build
by `BrowserIdleTimerTests.AnIdleSessionLosesItsBrowserKeepsItsNodeChildAndTheNextCallStillWorks`;
only the numbers need the manual run.

**Resume costs 515 ms and loses only `sessionStorage`.** Measured 2026-08-14:
after killing the node child, a resume against the recorded directory preserved
cookies, localStorage, IndexedDB, service workers and CacheStorage. This is the
measurement the no-expiry-timer decision rests on — the durable thing is the
profile, not the process.

**Proxying costs ~50 ms on a 500 KB payload.** From an equivalent Node prototype:
images passed through byte-identical (**509,620** base64 bytes), error shapes
preserved, ~50 ms added latency, ~300 ms one-off child spawn. It measured a
**Node** prototype rather than the C# proxy, so it is `[UNVERIFIED]` as a
prediction of BrowserAI's own overhead — a precedent, not a measurement of this
product.

**Suite costs, for cadence decisions:** real-child contract 2–5 s, smoke 10–30 s,
update 1–3 min. Estimates, not stopwatch figures. `[UNVERIFIED]`
