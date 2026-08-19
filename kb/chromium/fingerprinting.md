<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Automation fingerprinting

**Versions in force** unless an entry says otherwise: Windows 11 Pro 26200 · Chrome for Testing 152.0.7977.8 (`chromium-1237`) · `chromium-headless-shell` revision 1237 · `@playwright/mcp` 0.0.79 · `playwright-core` 1.63.0-alpha-2026-08-05.
Measured on [the reference machine](../README.md#the-reference-machine).

Measured 2026-08-15: **65 headless launches across four phases**, each launch
serialising a large fixed set of JS-visible properties out of a page and
differencing the arms field by field, with replicates and interleaved arm order.
**The harness was a spike and is not in this repository**, so nothing here is
reproducible by running something we ship. What is reproducible is the method,
which is stated in enough detail below to rebuild: enumerate the surface, take
8–10 replicates per arm with alternating lead, and **split each arm against
itself as a control** — that last step is what tells a real difference from
harness noise, and it is what caught the two false positives recorded below.

**`--browser-test` is not web-detectable.** **0 deterministic differences** across
486 leaf fields (chrome.exe) / 487 (headless shell), with 8–10 replicates per arm,
interleaved with alternating lead. Four self-controls (each arm split against
itself) also returned 0 with the same noise structure, proving the differ was
sensitive enough to have caught a single changed bit. `[FLOATS]`

Surface covered: full `window` enumeration (1,242 keys), `navigator` prototype
chain and descriptor shapes, `window.chrome` shape, 14 permission states vs
`Notification.permission`, plugin/mimeType arrays, WebGL1+2 (unmasked
vendor/renderer, 67 extensions, 31 params, 12 precision formats), canvas PNG and
pixel hashes, font metrics, OfflineAudioContext hash, WebGPU adapter, media
capabilities, WebRTC candidate types, storage estimate, high-entropy UA hints,
`matchMedia` × 18, Intl/timezone, native `Function.prototype.toString` of 8
patchable natives, `Error.stack` shape, and the `console.debug` / `Error.stack`
CDP timing tricks.

Also measured identical: HTTP headers (30 headers × 13 requests × 5 arms,
including all Client Hints after `Accept-CH`/`Critical-CH`), and reachability of
`chrome://version` from web content (`fetch` → `TypeError`, `window.open` →
`null`, iframe → empty, `pushState` → `SecurityError`), byte-identically in both
arms.

**Two Phase-1 candidates were artefacts of the harness itself** and vanished in
Phase 2: a
6-byte `performance.memory` delta caused by a per-run URL tag of differing length
landing on the JS heap, and a Compute Pressure `fair`/`nominal` split caused by
CPU-heavy benchmarks running inside the observation window. Worth recording
because both looked like real signals.

**The switch is not propagated to renderers.** Absent from
`render_process_host_impl.cc`'s `kSwitchNames[]`, absent from
`runtime_features.cc`'s `switchToFeatureMapping`, absent from
`bad_flags_prompt.cc`'s `kBadFlags` (so no "unsupported flag" infobar). Measured:
it appears on exactly one line of the process tree, and both renderer command
lines are byte-identical between arms. `[FLOATS]`

**Call-site inventory: 11 files**, not the 9 an earlier pass found — the two
missed are Fuchsia-only (`fuchsia_web/webengine/...`), including the only
renderer-side consumer anywhere in Chromium, which is not built for Windows. The
one that deserved the closest look was
`content/browser/in_memory_federated_permission_context.cc` — auto-completing
FedCM requests *is* web-observable — and it is ruled out because Chrome's
`ProfileImpl`/`OffTheRecordProfileImpl` override
`GetFederatedIdentity*PermissionContext()`, so the in-memory context is
content_shell-only. `[FLOATS]`

**The only real behavioural delta is the memory-pressure monitor.** With the
switch, `CreateMemoryPressureMonitor` returns `nullptr`, so the browser never
fires `MemoryPressureListener` → `ChildProcess::OnMemoryPressure` → Blink cache
purge / V8 pressure. That chain fires only under **genuine OS memory pressure**
and its absence is observable only by waiting for a purge that never comes — not
a static fingerprint bit. Compute Pressure does not expose it:
`PressureObserver.knownSources === ['cpu']`, and `observe('memory')` throws.
`[FLOATS]`

**Baseline exposure, identical in both arms — this is the context that makes the
question near-moot:** `[FLOATS]`

- The user agent contains the literal string **`HeadlessChrome`**. That is a
  one-line detection, already present.
- Playwright passes **43 switches** plus `about:blank`. It does **not** pass
  `--enable-automation`. `--disable-blink-features=AutomationControlled` is added
  by the **MCP config layer** (`coreBundle.js:71899`), not by `chromiumSwitches`.
- `navigator.webdriver === false` — `runtime_features.cc:377-379` maps
  `kEnableAutomation`, `kHeadless` **and** `kRemoteDebuggingPipe` to
  `EnableAutomationControlled`, and the MCP-added blink flag cancels it.
- `chrome-headless-shell` is far more exposed than full `chrome.exe`:
  `window.chrome` absent entirely, `plugins.length === 0`, SwiftShader renderer,
  and `Notification.permission === 'denied'` while `permissions.query()` reports
  `'prompt'` — the classic mismatch tell.

**The padding alternative measures identically** — 0 differences across 486
fields. Unknown switches are not in `kSwitchNames` and not in `kBadFlags`, so they
are web-invisible by the same mechanism. `[FLOATS]`

**No evidence any bot-detection vendor or anti-detect project references
`--browser-test`.** Zero hits across GitHub code search (rebrowser-patches,
patchright, puppeteer-extra, undetected-chromedriver, nodriver) and four web
searches spanning DataDome, Castle, CloakBrowser and BotBrowser writeups. **This
is absence of evidence, not evidence of absence** — stated as such. The
literature's switch-detection surface is `--enable-automation`, `--headless` and
`--disable-blink-features=AutomationControlled`, all detectable via their
*effects*, and all already in play here regardless. `[FLOATS]`

**Residual risk measurement cannot rule out:** real OS memory pressure was never
induced (deliberately); headful was not tested (hard constraint, though nothing
renderer-side depends on the switch); one Chromium version; and no real
bot-detection service was exercised (local-only constraint).

## The user agent and `navigator.webdriver`, through the config alone — measured 2026-08-19

**Asked because the maintainer asked it:** can the user agent and
`navigator.webdriver` be set to a normal browser's values *through the generated
child config*, with no Playwright driving and no init script? Measured by
driving the payload's own `cli.js` over stdio with a hand-written config and
reading both values back through `browser_evaluate`, at `@playwright/mcp` 0.0.79
/ `playwright-core` 1.63.0-alpha-2026-08-05, Chromium 152.0.7977.8
(`chromium-1237`) and Firefox 153.0 (`firefox-1539`), headless:

| Arm | `navigator.userAgent` | `navigator.webdriver` |
|---|---|---|
| chromium, nothing set | `… HeadlessChrome/152.0.0.0 Safari/537.36` | `false` |
| chromium, `browser.contextOptions.userAgent` set | `… Chrome/152.0.0.0 Safari/537.36` — **the value we asked for** | `false` |
| firefox, nothing set | `… rv:153.0) Gecko/20100101 Firefox/153.0` | **`true`** |
| firefox, `browser.contextOptions.userAgent` set to a distinct string | `BrowserAI-probe/1.0 distinct-context-option` — **the value we asked for** | **`true`** |
| firefox, `firefoxUserPrefs["dom.webdriver.enabled"] = false` | unchanged | **`true`** — the pref does nothing |
| firefox, `firefoxUserPrefs["general.useragent.override"]` set — **the control** | `BrowserAI-probe/1.0 distinct-pref` | `true` |

**Three findings, and the control is what makes the third one mean anything.**

1. **`browser.contextOptions.userAgent` works, for both families.** It is a
   plain config key: `configFromCLIOptions` maps `--user-agent` onto the same
   key, `userAgent` is declared in **both**
   `BrowserNewContextParams` and `BrowserTypeLaunchPersistentContextParams`, and
   `createPersistentBrowser` spreads `contextOptions` into the launch — so unlike
   [`storageState`](../playwright/configuration.md#silent-config-failures) it is
   not dropped on the persistent path.
2. **Chromium's user agent is the only thing headedness changes here**, and the
   difference is the one token `HeadlessChrome` against `Chrome`. Firefox's is
   byte-identical across headedness, so Firefox needs no override for this.
3. **`navigator.webdriver` is not reachable from the config on Firefox.**
   `dom.webdriver.enabled: false` left it `true`. **The control is the row above
   it**: `general.useragent.override` set through the *same* `firefoxUserPrefs`
   object *did* take effect, so the prefs channel demonstrably reaches Firefox
   and this particular pref does not govern the flag in Playwright's build.
   Turning it off would need an init script, which is outside what a config can
   do.

**The staleness trap, stated because it is the reason not to do the obvious
thing.** A hardcoded UA goes stale the moment `browsers.json` moves, and it moves
with every `@playwright/mcp` bump — and it fails **silently and in the wrong
direction**: a server would see `Chrome/152.0.0.0` from a browser that is
actually 154, which is a *worse* signal than an honest `HeadlessChrome`. Two
derivations avoid it and neither is free:

- **From the payload.** `browsers.json` carries `browserVersion` beside the
  revision — `152.0.7977.8` here — and the measured UA reports
  `Chrome/152.0.0.0`, i.e. the major followed by `.0.0.0`. So the string is
  composable with no launch. What it pins is the *shape* of Chrome's reduced UA
  (`Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like
  Gecko) Chrome/<major>.0.0.0 Safari/537.36`), which is one revision's worth of
  evidence and an assumption about a policy this repository has not measured.
- **From the browser.** Read the headless UA the browser itself produces and
  replace the token `HeadlessChrome` with `Chrome`. Every version number then
  comes from the browser and nothing can go stale — but the value is only
  available *after* a launch, and `contextOptions.userAgent` is applied at launch,
  so it needs either a probe launch per revision or a cached value beside the
  browsers root.

**Nothing is implemented.** This entry is the research; the decision is the
maintainer's, and the wider parity measurement it belongs to is the open item in
[`../../TODO.md`](../../TODO.md). `[FLOATS]`

**Re-establish** by driving `cli.js` with a config carrying each arm and
evaluating `JSON.stringify({ ua: navigator.userAgent, webdriver: navigator.webdriver })`.
**Keep the `general.useragent.override` arm** — without a pref that is known to
work, a `dom.webdriver.enabled` that changes nothing cannot be told from a prefs
channel that was never wired up.
