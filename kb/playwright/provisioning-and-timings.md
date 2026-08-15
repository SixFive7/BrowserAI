<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Payload sizes, first-run provisioning and timings

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

**Total payload ~806 MB installed, ~239 MB compressed** — 7z LZMA2 `-mx=5`.
NativeAOT single-file BrowserAI is estimated at ~10–15 MB and the trimmed
self-contained fallback at ~70 MB — both `[UNVERIFIED]`, nothing having been
built.

> ⚠️ **The update budget is written against a different figure: ~380 MB**,
> described in the charter as "dominated by Chromium", with **~600–700 MB**
> transient disk during a swap and a full re-extraction of ~380 MB per update.
> **How that reconciles with ~806 MB installed — and with browsers provisioned on
> first run rather than shipped — is nowhere recorded.** `[UNVERIFIED]`. Settle it
> the next time either figure is re-measured; do not settle it by arithmetic.

**A single `node.exe` drives the full MCP protocol** — no npm, no `node_modules`
belonging to Node, no `.cmd` shims. Verified by execution. Node **v26 is Current
rather than LTS and its `node.exe` is 10 MB larger**. `[FLOATS]`

**The vendored JS tree contains zero native binaries** and is portable as-is.
**`ffmpeg` is required for video capture** — without it the `video` artifact type
throws. `[FLOATS]`

## First-run provisioning

**Measured 2026-08-14: 20.3 s end to end on a 300 Mbps link** for chromium +
`ffmpeg` + `winldd`, the chromium download recorded as **202.3 MB**. Stated for
slower links: **4 m 19 s at 10 Mbps, 43 m at 1 Mbps**. `[FLOATS]`

> ⚠️ **The download size is stated four ways across the repository, and the
> charter states it three of those ways.** 202.3 MB in the sentence above; **323
> MB** where `init` is required to check free disk space; **~300 MB** in the
> legacy-setup table; peak disk during provisioning given as **~0.9 GB**.
>
> **`TODO.md` carries the same 2026-08-14 measurement in fuller form** and is the
> only record that reconciles: *"chromium 202.3 MB + shell 119.7 MB + ffmpeg +
> winldd = **323.5 MB down, ~700 MiB on disk**, 20.3 s end to end on a 300 Mbps
> link"*, with the same 4 m 19 s / 43 m arithmetic. So the charter's 202.3 MB is
> **one term of a sum**, and the slow-link figures belong to the total.
>
> **What that means for today's payload is deliberately not decided here.** The
> shell is [no longer provisioned](../../README.md#settled-2026-08-15), so whether the
> current download is still 323.5 MB, or 202.3 MB plus the small components, is a
> question one run answers and no amount of arithmetic may. Treat the size as
> `[UNVERIFIED]` until then, and re-state it in one place when it is settled.

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
launch-time check is file accessibility of the executable. `.links/` records the
**build machine's** absolute paths and is useless on the target.

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

**Browser-idle close recovers 329 MB → 110 MB, and relaunch costs 186 ms.**
Closing the browser while keeping the node child is therefore cheap enough that a
caller navigating afterwards need never see "browser is closed".

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
