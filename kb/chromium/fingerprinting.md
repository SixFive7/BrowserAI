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
