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
the payload** ([`build/Build-Payload.ps1`](../../build/Build-Payload.ps1)).
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
[Shipping our own runtime](../../ARCHITECTURE.md#the-runtime-it-ships) requires Node's full
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
extracted tree coexist. ***Relabelled 2026-08-18: that is arithmetic, not a
measurement*** — nobody has sampled free space across a provisioning run, and the
sum does not land where the number does (203,824,344 B is 194.4 MiB, plus the
430.48 MiB extracted tree, is ~625 MiB; 640 is 5 × 128 MiB, a round number the
arithmetic does not give). It also assumes an ordering nobody observed: that the
archive is fully present before extraction begins and is removed afterwards.
**It matters because it ships as a refusal** — `SessionManager.RequiredFreeBytes`
is `640L * 1024 * 1024` and a session is declined against it. The margin is in
the safe direction and the constant is left alone. **This file is where that
number lives** — the rest of the repository cites it rather than restating it.
Settle it by sampling free space every 250 ms across the run already timed twice
at 12.6 s and 12.0 s. The component byte counts above **are** measured;
re-establish those with a `HEAD` on the three URLs below. `[FLOATS]` for the
components, `[UNVERIFIED]` for the peak.

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
> [no longer provisioned](../../DECISIONS.md#processes-browsers-and-session-modes). The two runs above
> are of what BrowserAI actually downloads, so the figure is now a measurement of
> the thing rather than of a superset of it.

### Firefox, measured the same way — 2026-08-19

**Firefox provisioning is 127,247,129 B down = 127.2 MB, and 356,674,059 B =
340.15 MiB on disk across 71 files.** Measured 2026-08-19 at Firefox rev
**1539** / 153.0, ffmpeg **1011**, winldd **1007**, by two clean runs of
`node.exe cli.js install-browser firefox --no-shell --no-progress` into an empty
`PLAYWRIGHT_BROWSERS_PATH` — **byte-identical across both runs** — with the wire
figure taken from the exact `content-length` of each archive, which is how
[Chromium's 203.8 MB](#first-run-provisioning) was taken:

| Archive / directory | Down (`content-length`) | On disk | Files |
|---|---:|---:|---:|
| `firefox-win64.zip` → `firefox-1539` | 125,706,704 B | 352,898,062 B (336.55 MiB) | 63 |
| `ffmpeg-win64.zip` → `ffmpeg-1011` | 1,411,741 B | 3,517,342 B (3.35 MiB) | 4 |
| `winldd-win64.zip` → `winldd-1007` | 128,684 B | 258,560 B (0.25 MiB) | 3 |
| `.links` | — | 95 B | 1 |
| **total** | **127,247,129 B = 127.2 MB** | **356,674,059 B = 340.15 MiB** | **71** |

⚠️ **Corrected 2026-08-19 (previously "Firefox 153.0 (rev 1539) is 125,706,704 B
down and 352,898,062 B — 336.55 MiB — on disk … BrowserAI creates no Firefox
sessions").** Both halves needed work. The **numbers were the Firefox archive and
the Firefox directory alone**, while Chromium's were stated for the whole
provisioning run — so the two were not comparable, and the smaller pair was the
one about to be quoted at a caller. `install-browser firefox` fetches the same
three archives `install-browser chromium` does; `ffmpeg` and `winldd` are shared
by both families and land in the same root. And the second half stopped being
true on 2026-08-19, when `browserai_init` began accepting `browser: "firefox"`.

**Beside an existing Chromium, Firefox downloads 125,706,704 B and nothing
else — 125.7 MB, not 127.2.** Measured 2026-08-19 on a third run: `ffmpeg-1011`
and `winldd-1007` copied into an empty root **with their `INSTALLATION_COMPLETE`
markers**, then `install-browser firefox`, which printed exactly one
`Downloading` line and left the root at the same 356,674,059 B. So the family has
**two honest figures and they answer different questions** — 127.2 MB is what a
machine with no browsers at all pays for Firefox, and 125.7 MB is what a machine
that already has Chromium pays. `BrowserProvisioner.FirstRunDownloadSizes` quotes
**127.2 MB**: it is the upper bound, it is the same predicate as
[Chromium's 203.8 MB](#first-run-provisioning) — one family into an empty root —
and the 1.5 MB between them cannot change a caller's decision about waiting.
CI quotes the incremental one, because there the Chromium step runs first.

**`.links` is path-dependent and is not a constant.** It holds the absolute path
of the `playwright-core` package that requested the install — 95 B from this
repository's assembled payload, 69 B in [the Chromium
measurement](#first-run-provisioning) taken from a shorter one. Compare the three
component subtrees, not the root total, when comparing across machines.

**Against Chromium: 62.4% of the download and 79.0% of the disk.** Slow-link
arithmetic, stated as arithmetic: **1 m 42 s at 10 Mbps, 16 m 58 s at 1 Mbps**.
Peak disk while archive and tree coexist would be ~461 MiB, which is *arithmetic
and not a measurement* for exactly the reason [the Chromium
figure](#first-run-provisioning) is — nobody has sampled free space across a run.
`SessionManager.RequiredFreeBytes` stays at 640 MiB for both families: it is
sized on the larger, both of Firefox's halves are smaller, and a per-family bound
would refuse nothing this one permits.

**End to end it took 7.30 s and 6.60 s** on the same ~300 Mbps link, exit 0 both
times, against Chromium's 12.6 s and 12.0 s. Phase boundaries from the
installer's own timestamped output: Firefox's download and extraction together
**1.1 s → 5.9 s** and **0.3 s → 5.2 s**, `ffmpeg` a further 0.4–0.5 s, `winldd`
0.4 s. `[FLOATS]` `[MACHINE]`

**Re-establish** by running that command against a fresh directory and summing
the files, and `HEAD`ing the three URLs under
`https://cdn.playwright.dev/dbazure/download/playwright/builds/{firefox/1539,ffmpeg/1011,winldd/1007}/`.
The revisions come from the payload's own `browsers.json`; never type one.

### One `install-browser ffmpeg` rebuilds both shared components — 2026-08-19

**`install-browser ffmpeg` downloads `ffmpeg` and `winldd` together, and
re-downloads whichever of the two is missing.** Measured 2026-08-19 at
`@playwright/mcp` 0.0.79 / `playwright-core` 1.63.0-alpha-2026-08-05, against
this repository's assembled payload:

| Run | Root before | What it printed | Root after |
|---|---|---|---|
| 1 | empty | `Downloading FFmpeg … 1011`, then `Downloading Winldd … 1007` | `ffmpeg-1011` and `winldd-1007`, each with its own `INSTALLATION_COMPLETE` |
| 2 | `winldd-1007` deleted, `ffmpeg-1011` complete | `Downloading Winldd … 1007` only | both complete again, `ffmpeg-1011` untouched |

**This is why `browserai_reinstall_browser`'s `shared` target passes one name and
still checks two markers.** Asking for both by name would re-download whatever
was already there; trusting the one command's exit code would trust upstream's
grouping, which is the thing that can move. `ProvisionedBrowsers.SharedInstallTarget`
carries the citation, and `BrowserProvisioner.RebuildShared` verifies each
component's own marker after the run. `[FLOATS]`

**Re-establish** with `node.exe cli.js install-browser ffmpeg --no-shell
--no-progress` into an empty `PLAYWRIGHT_BROWSERS_PATH`, then delete
`winldd-<rev>` and run it again. **What would falsify it is upstream regrouping
the two, and it fails in the safe direction here** — the per-component marker
check turns a `winldd` that stopped arriving into a reported failure rather than
into a tree marked complete.

### Two installers cannot extract into one root — upstream's `__dirlock` — 2026-08-19

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
install and a firefox install run concurrently by design — and **both** lay down
`ffmpeg` and `winldd` in the one root. Nothing on this side serialises that pair.
Upstream does.

Measured five ways, all on 2026-08-19 against the assembled payload:

| | What was done | What happened |
|---|---|---|
| **A** | `__dirlock` created and its mtime kept fresh by a probe; `install-browser ffmpeg` started against that root | **Nothing at all for 30 s** — no directory, no download, not one line of output — and the process stayed alive. It completed **8 s after** the probe removed the lock. The wait is therefore *before* any work, not during it |
| **B** | `install-browser chromium` and `install-browser firefox` started **8 ms apart** into one empty root | Clean serialisation. `__dirlock` held continuously from t+3.1 s; chromium exited 0 at t+10.2 s having downloaded chromium, `ffmpeg` **and** `winldd`; firefox then took the lock, downloaded **only** `firefox-win64.zip` because the two shared markers were already there, and exited 0 at t+17.8 s. Final root: `chromium-1237`, `firefox-1539`, `ffmpeg-1011`, `winldd-1007`, **all four with `INSTALLATION_COMPLETE`**, no `__dirlock` left behind |
| **C** | the same lock held fresh **for as long as the installer would wait** | Gave up at **470 s — 7 min 50 s** — exited **1**, and wrote **nothing at all** into the root. The message is upstream's own boxed `An active lockfile is found at: <path>` with `wait a few minutes if other Playwright is installing browsers in parallel` and the `rm -R <path>` escape |
| **D** | three `install-browser ffmpeg` started together into one empty root, **three rounds** — a race into the shared component directories themselves | 3/3 exit 0 every round, both trees complete and byte-identical every round (`ffmpeg-1011` 3,517,342 B, `winldd-1007` 258,560 B), no `ELOCKED`, no residue |
| **E** | `__dirlock` created and then **never refreshed**, which is the state a killed installer leaves — BrowserAI closes the installer's job on a cap or on `Dispose` | Reclaimed as stale and the install completed **in 13 s total**, against ~10 s for the same install with no lock present. An abandoned lock costs the next installer the staleness window and nothing else |

**So the corruption is upstream's to prevent and upstream prevents it.**
*Corrected 2026-08-19: `ReinstallSharedAsync`'s remarks previously called two
family installs racing into one shared component directory "reachable in the
shipped product". The concurrency is reachable; the race is not.*

**What is real is a wait, and it belongs to the waiter — measurement C.** 20
attempts at a 1.27579 factor comes out at **470 s**, after which upstream fails
the install outright rather than queueing further. **A first-run chromium
download can outlast that**: 203.8 MB in 470 s is 3.5 Mbps, and
`ProvisioningTimers.AbsoluteCap` is deliberately sized for links down to
0.60 Mbps — so on any link slower than ~3.5 Mbps, a firefox install started
beside a chromium install fails with `ELOCKED` instead of waiting for it.

**It fails loudly, writes nothing, and the next attempt succeeds.** BrowserAI's
own caps do not fire and do not need adjusting: the wait happens before the
browser's directory appears, so `ProvisioningTimers.ExtractionCap` has not
started and only the 45-minute `AbsoluteCap` covers it — and 470 s is inside it.
What the caller sees is upstream's box, which names the path and says to wait a
few minutes. `[FLOATS]`

**Re-establish** measurement A: `mkdir <root>\__dirlock`, keep touching it faster
than `proper-lockfile`'s 10 s staleness window, run `install-browser ffmpeg
--no-shell --no-progress` against that root, and check the root stays empty.
**The refresh is the control** — without it the lock goes stale in ten seconds and
the installer proceeds, which measurement E is. What would falsify all of this is
upstream dropping the lockfile, moving it inside the per-executable loop, or
scoping it to something narrower than the root;
`PayloadTests.UpstreamStillSerialisesEveryInstallOnOneLockOverTheWholeBrowsersRoot`
reads the four anchors out of the assembled bundle and asserts their order, so a
removal is a red build rather than a rediscovery.

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
> The [2026-08-15 decision](../../DECISIONS.md#processes-browsers-and-session-modes) to run full Chromium
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
[mandated in the child's environment](../../DECISIONS.md#the-five-rules-that-make-floating-safe)
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

### What grows on disk while an install runs, and when — 2026-08-19

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
[recorded above](#first-run-provisioning)** — 202,283,919 B and 125,706,704 B —
so the on-disk sample and the CDN figure are the same number reached two ways.
The root totals land on the recorded ones once `.links` is accounted for:
Chromium's 451,389,780 B is exact, and Firefox's 356,674,033 B is 26 B under the
recorded 356,674,059 B because **`.links` is path-dependent** and was 69 B in
this run against 95 B in the run that produced the recorded figure — which is the
thing [the Firefox section](#firefox-measured-the-same-way--2026-08-19) already
warns about.

**Upstream downloads into `os.tmpdir()`, not into the browsers root.**
`downloadBrowserWithProgressBar` does
`mkdtemp(path.join(os.tmpdir(), "playwright-download-"))` and writes
`playwright-download-<name>-<platform>-<revision>.zip` into it, then
`removeFolders([uniqueTempDir])` in a `finally`. So on Windows the location is
whatever `TEMP` says, and **BrowserAI sets `TEMP` and `TMP` for the installer
child** to `<browsers root>\.downloads\<family>` —
`BrowserProvisioner.DownloadDirectoryName` plus the family, one subdirectory each
because the provisioning mutex is keyed on the family and two installs run at once
by design — which is what makes one recursive weigh of the browsers root cover
both phases.
Proven rather than assumed: the redirected chromium run above produced
451,389,780 B, byte-identical to the run with the default temp.

**Scanning `%TEMP%` instead would have been wrong, and it was measurably wrong on
this machine.** The default-temp run started with `tempdl = 128,684 B` and
`dirs = 2` before it had downloaded anything: a `playwright-download-PRU23e`
abandoned on 2026-08-16 was still there, holding a stale `winldd-win64.zip`.
Upstream's `finally` does not run when a killed installer is killed, and
BrowserAI closes the installer's job on a cap — so its own residue would
accumulate there too.

**There is no other progress signal, and that is the second half of the
measurement.** `--no-progress` sets `PLAYWRIGHT_DOWNLOAD_NO_PROGRESS=1`, which
makes `downloadFile`'s `reportProgress` false, so the parent's
`getBasicDownloadProgress()` never prints a percentage line: the installer's
whole stdout for a chromium install is **six lines**, one `Downloading …` and one
`… downloaded to …` per archive. And `@playwright/mcp` emits no MCP progress
notifications at all ([kb](../mcp/sdk.md#lossless-passthrough-cancellation-notifications-and-error-frames)).

**Re-establish** by sampling `PLAYWRIGHT_BROWSERS_PATH` and the temp directory
recursively every 250 ms across `node.exe cli.js install-browser <family>
--no-shell --no-progress` into an empty root, once with `TEMP` redirected and
once without. **The control is the redirected/default pair** — a single run
cannot tell "the redirect works" from "the download happened to land here". What
would falsify it is upstream downloading straight into the registry directory, or
`extractZip` starting to stream rather than writing whole files, either of which
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

**The download is worth 3.6–4.4 s of a ~36 s run — 10 to 12% — while the test
that performs it takes 12 to 14 s longer than its seeded form.** Means: 32.44 s
seeded, against 36.08 s for the pre-cache baseline and 36.84 s for a cold run
that also publishes. The gap between 13 s of test and 4 s of suite is the
parallelism: with the suite capped at four concurrent tests and ~130 s of total
test work, it is **work-bound rather than critical-path-bound**, so removing 13 s
of work returns about a quarter of it to the clock. A test's own duration is
therefore not its cost to the suite, and this is the second time that distinction
has mattered here — the first being a slice test that took 2.6 ms on a run that
really did launch a browser. `[MACHINE]`

**What publishing and seeding each cost is not separable from this data**, and is
recorded as unmeasured rather than divided out: the single cold-with-publish run
(17.13 s) sits above a baseline whose own spread is 13.77–15.92 s, so the
451,389,838 B same-volume copy is inside that difference and cannot be read off
it. The seeded figure of 3.49–3.88 s is a whole first-run sequence — published
binary start, `initialize`, `init`, two refusals, a `browserai_list`, the copy,
and one real navigation against a real Chromium — not a copy time. `[MACHINE]`

**A cached run really does not reach the network, measured at the adapter rather
than inferred from the code.** `Get-NetAdapterStatistics` sampled either side of
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
> **Sum one physical adapter, not all of them**, when re-establishing this — the
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
> reason**, which is worth knowing before treating a mismatch as corruption —
> and is why the cache's own completeness check compares a tree against the stamp
> *it* was published with rather than against a figure written down here.

**Re-establish** by running the suite twice within the hour and reading the
`first-run bytes` row of the coverage block, or `.work/suite-coverage.txt`, which
names the source, the age of the tree used, and the elapsed time. The mechanism
is [in TESTING.md](../../TESTING.md#the-first-run-download-runs-at-most-once-an-hour).

## Timings: spawn, resume, idle close, proxy overhead

`[MACHINE]` for every number, `[FLOATS]` for what they are numbers *about*.

> ⚠️ **Only the resume figure carries a date in the charter.** Spawn, navigation,
> idle close and proxy overhead are all recorded undated, so treat their dates as
> `[UNVERIFIED]` and re-stamp each at the next run. The numbers themselves are
> carried forward exactly as written — none has been adjusted.

**A real browser reaches MCP-ready in 0.9–1.2 s (Chromium) and 3.1–4.0 s
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

**Firefox is ~3.4× slower to first answer**, consistently, across every pair —
which is the transferable half, and it is the same direction as
[the cost ratios](#firefox-against-chromium-the-standing-cost-ratios) below. The
absolute numbers are this machine's.

**The headroom is the point, not the latency.** Playwright's own
`DEFAULT_PLAYWRIGHT_LAUNCH_TIMEOUT` is `3 * 60 * 1e3`. The slowest observed run
used **2.2%** of the 180 s the harness then waited. A launch timeout is
therefore not a knob worth tuning, and a launch that approaches it is not slow —
it is stuck, and should be read as a failure rather than as a machine having a
bad day. `[MACHINE]` for the times and counts; `[FLOATS]` for the ratio and the
headroom, both of which move with a browser revision.

> ⚠️ **Corrected 2026-08-18 (previously "against a 180 s default patience …
> the harness waits the same 180 s").** *The measured times above are unchanged
> and were not re-run; what changed is the harness they were measured against.*
> `BrowserContainmentTests.ReportPatience` is now `TestDefaults.BrowserHang`,
> **thirty minutes**, so the slowest observed run uses 0.22% of it rather than
> 2.2%. The reason is the one this paragraph already gives, applied properly: a
> harness bound *equal* to Playwright's own launch timeout always wins the race
> against it, so upstream's diagnosis is replaced by *"the budget expired"* in
> the one case upstream had something to say — observed at exactly 3m00s on a
> Firefox launch, 2026-08-17. And at unbounded suite parallelism 180 s was
> reachable by a launch that was merely starved: one run in four
> ([kb](../toolchain.md#running-419-tests-at-once-what-starves-and-by-how-much)).

**Re-establish by running the suite.** `BrowserContainmentTests` records
`readyMilliseconds` beside its containment counts **on every run, including the
ones that pass** — which is deliberate: a bound can only be called too tight
against a distribution, and a distribution cannot be reconstructed from the runs
that failed.

> **Recorded here rather than left in the test output, and the earlier reasoning
> for leaving it out was wrong.** These numbers were measured before and kept out
> of the knowledge base on the grounds that an entry marked as floating creates a
> re-verification obligation. That is backwards: the obligation is the feature,
> and this is the cheapest kind of row there is — the fact is asserted by a test
> that already runs on every build, so the row costs a line and nothing else.
> [Row 89](../re-verification.md) carries it. *(Written in words rather than in
> the marker, which is the rule for prose about the convention: the counter reads
> the token and cannot tell a mention from a stamp.)*

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
measurement [the browser-idle timer](../../ARCHITECTURE.md#sessions)
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

## Firefox against Chromium: the standing cost ratios

**~2× RAM, ~10× first navigate, ~24× idle CPU, ~20× profile disk.** Measured
2026-08-14 against Chromium as the unit. This is the whole of the evidence behind
Chromium being the default family, and the reason a Firefox session is an
explicit request rather than an equal option.

`[UNVERIFIED]` **as to method, and that qualifier is the point of the entry.**
The figures come from a measurement session whose harness was not preserved, so
they cannot be reproduced as written — treat them as order-of-magnitude guidance
and **re-measure before any decision turns on them**. They are recorded rather
than dropped because they were being cited in design discussion while living
nowhere in the repository, which is the worse of the two failures: a number with
a stated weakness can be checked, and a number carried only in conversation
cannot. `[FLOATS]` — every one of the four moves with a browser revision.

**To re-establish:** open one session per family through the product, drive the
same navigation in each, and compare resident set, wall time to first paint,
idle CPU over a fixed window with no page activity, and profile-directory size
on disk. The **ratio** is the transferable half; the absolute numbers are
whichever machine ran them. The independently measured
[time-to-MCP-ready figures](#timings-spawn-resume-idle-close-proxy-overhead)
above agree in direction — Firefox ~3.4× slower to first answer — which is
corroboration of the sign, not of the magnitudes.
