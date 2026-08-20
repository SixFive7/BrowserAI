<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# The tool surface and the artifacts it writes

**Versions in force** unless an entry says otherwise: `@playwright/mcp` 0.0.79 · `playwright-core` 1.63.0-alpha-2026-08-05 · Windows 11 Pro 26200.
Measured on [the reference machine](../README.md#the-reference-machine).

## The tool surface and the package shape

Read from the shipped tree during the 2026-08-13 feasibility research unless a
later date is given. `@playwright/mcp` 0.0.79.

**`@playwright/mcp` is a 20-line shim.** The whole package is `cli.js`,
`index.js` and type definitions. `index.js` in full:

```js
const { tools } = require('playwright-core/lib/coreBundle');
module.exports = { createConnection: tools.createConnection };
```

The implementation is `playwright-core/lib/coreBundle.js` — **3.4 MB**,
esbuild-bundled. `[FLOATS]`

**Three tool counts, and a golden test written against the wrong one fails on
day one.** **78** entries in the internal registry array; **69** the maximum ever
exposed over MCP (9 are `skillOnly` and always stripped); **24** the default with
no `capabilities` set. The founding-bug reproduction saw the 24 over a real
`tools/list`. `Verified 2026-08-16 @ @playwright/mcp 0.0.79 / playwright-core
1.63.0-alpha-2026-08-05`: all three re-measured, the 78 and the 9 by reading
`browserTools` in-process and the 24 and 69 over a real `tools/list`. They now
regenerate into
[`upstream-snapshots/tools-list.json`](../../upstream-snapshots/tools-list.json)
on every build, so a move is a diff rather than a memory. `[FLOATS]`

**The `storage` capability is 17 tools** — the cookie / localStorage /
`storageState` set. The legacy `interactive` server ran without it, so in that
process they did not exist at all.

### The per-capability breakdown, counted

**Measured 2026-08-16 @ `@playwright/mcp` 0.0.79 / `playwright-core`
1.63.0-alpha-2026-08-05.** Re-establish it by regenerating the snapshot:
`pwsh -File build/Update-UpstreamSnapshots.ps1 -Accept`, which reads
`browserTools` from the resolved bundle and cross-checks every number against a
real `tools/list`. `[FLOATS]`

| Capability | Tools it carries | Of those, `skillOnly` | Surface with it alone |
|---|---|---|---|
| `core` | 21 | 2 | unconditional |
| `core-input` | 7 | 5 | unconditional |
| `core-navigation` | 4 | 2 | unconditional |
| `core-tabs` | 1 | 0 | unconditional |
| `core-install` | **0** | — | unconditional, and carries nothing |
| `config` | 1 | 0 | 25 |
| `network` | 4 | 0 | 28 |
| `pdf` | 1 | 0 | 25 |
| `storage` | 17 | 0 | 41 |
| `testing` | 5 | 0 | 29 |
| `vision` | 6 | 0 | 30 |
| `devtools` | 11 | 0 | 35 |
| **all twelve** | **78** | **9** | **69** |

**The `core` family is unconditional, and that is why every column above starts
at 24.** `filteredTools(config)` is
`browserTools.filter(t => t.capability.startsWith("core") || config.capabilities?.includes(t.capability)).filter(t => !t.skillOnly)`,
so the five `core*` capabilities are on whatever `capabilities` says — setting
`capabilities: ["config"]` yields **25** tools, not 1. Naming a `core*`
capability explicitly therefore does nothing, and **no configuration can reduce
the surface below the base 24**. `[FLOATS]`

**The nine `skillOnly` tools, by name:** `browser_console_clear`,
`browser_network_clear` (`core`); `browser_press_sequentially`,
`browser_keydown`, `browser_keyup`, `browser_check`, `browser_uncheck`
(`core-input`); `browser_navigate_forward`, `browser_reload`
(`core-navigation`). They are in the registry, they are never in a `tools/list`,
and the property is `tool.skillOnly` on the registry entry rather than anything
on the schema. `[FLOATS]`

**What BrowserAI's own capability sets expose, measured over the wire rather
than added up:** `config` + `vision` + `devtools` gives **42**, adding `storage`
gives **59**, and adding `network`, `pdf` and `testing` on top of that gives
**69** — the whole exposable surface, which is what
[every session now gets](../../ARCHITECTURE.md#sessions). The first two are the
same numbers the `createConnection` experiment below produced from two
connections in one process, which is a second, independent route to them.
⚠️ *Corrected 2026-08-20 (previously "What BrowserAI's own modes expose … and
`persistent` adding `storage` gives **59**"): session modes were deleted, so 42
and 59 are now historical capability sets rather than things a session can be,
and 69 is what a child is launched with.* `[FLOATS]`

> **Re-established a third way 2026-08-16, and it is now the one that runs on
> every build.** `UpstreamSurface.For(capabilities)` reproduces
> `filteredTools`'s rule from the committed snapshot — the `core*` family or-ed
> with the configured list, in the snapshot's own tool order — and
> `UpstreamSnapshotTests.TheCapabilityFilterReproducesTheRecordedSurfaces`
> asserts it against the snapshot's recorded `defaultSurface` (24, name for name
> and in order) before asserting 42 and 69. *(Corrected 2026-08-20, previously
> "42 and 59": the second arm now asks the product's own
> `GrantedCapabilities`, which is every capability upstream declares.)* The
> reproduction check is what stops
> the helper being a second implementation nobody validates: without it, a
> surface assertion built on it would be measuring the helper.
> `VerticalSliceTests` then compares the **published binary's** real
> `tools/list` against the computed 59 as one joined string, which is what makes
> ordering part of the contract rather than an accident. `[FLOATS]`

> ⚠️ **Corrected 2026-08-16 (previously: "A per-capability breakdown is not
> recorded anywhere in this repository … `[UNVERIFIED]` — the numbers were never
> observed, not merely lost. Count them from the resolved bundle at the next
> review rather than from memory.")** They have now been counted from the
> resolved bundle, which is what build-order step 4 was told to expect. The
> `[UNVERIFIED]` marker is gone because the numbers were observed, not because
> anybody reasoned about them.

### Does the surface differ by browser family? — measured 2026-08-19

**No, at any capability set: it does not depend on `browserName` at all.**
Measured 2026-08-19 @ `@playwright/mcp` 0.0.79 / `playwright-core`
1.63.0-alpha-2026-08-05 / Chrome for Testing 152.0.7977.8 (`chromium-1237`) /
Firefox 153.0 (`firefox-1539`) by spawning four real children of the resolved
payload — `chromium` and `firefox` × BrowserAI's base and union capability sets —
and diffing the `tools/list` each answered:

| Config | Tools | Names | Order | Schemas |
|---|---:|---|---|---|
| `chromium` + `config,vision,devtools` | 42 | — | — | — |
| `firefox` + `config,vision,devtools` | 42 | identical | identical | identical |
| `chromium` + `…,storage` | 59 | — | — | — |
| `firefox` + `…,storage` | 59 | identical | identical | identical |

Zero names present in one and absent from the other, and zero shared names whose
serialised tool object differed. **The mechanism is visible in the source and the
measurement is what makes it a fact rather than a reading:** `filteredTools`
([above](#the-per-capability-breakdown-counted)) filters on `tool.capability` and
`tool.skillOnly` and consults nothing else — there is no `browserName` in it.

**Why it was asked, and what it buys.** BrowserAI's static tool list is built
from one surface child, which is Chromium-configured, and the MCP spec forbids
the tool set varying per connection — so a family-dependent surface would mean
Firefox sessions advertising tools their child does not have, or the reverse.
Every tool-surface number in this repository is a claim about **both** families,
and it is now measured rather than assumed.

**Re-establish it** by giving [`build/upstream-snapshots.mjs`](../../build/upstream-snapshots.mjs)'s
`session()` helper a config carrying `browser.browserName: "firefox"` — plus the
`firefoxUserPrefs` launch option in place of `channel`, because upstream's
`validateBrowserConfig` drops a channel for a non-chromium family — and diffing
its `tools/list` against the one the same helper already takes. Note that no
browser is launched to answer `tools/list`, so the comparison needs the payload
and not a provisioned Firefox. `[FLOATS]`

**One node process can serve several configurations.** Verified: two connections
built through the programmatic `createConnection` API produced correctly
divergent surfaces — **42 vs 59 tools** — with no module-global browser state and
browsers created lazily on first tool call. It is reachable only through that
API, which is why the charter rejects it on scope rather than on capability.
`[FLOATS]`

**`playwright-core` whitelists `"./lib/coreBundle"` in its `exports` map**, so
`require('playwright-core/lib/coreBundle')` is a supported import, not a blocked
deep path. It exposes `browserTools` (a flat array of plain, inert objects),
`filteredTools`, `createConnection` and `BrowserBackend`. `defineTool` is the
identity function — no class, no registry, no side effect. No type definitions
and no semver guarantee attach to it. `[FLOATS]`

**The `playwright` package (4.85 MB) is a declared dependency that is never
loaded.** Prunable, but `npm ls` then calls the tree broken. `[FLOATS]`

**`core-install` is declared in `config.d.ts` but no tool carries it** in 0.0.79 —
a dead capability string; setting it does nothing. `[FLOATS]`

**Upstream publishes daily alpha builds of `playwright-core`.**
`@playwright/mcp@latest` is the released dist-tag; the `playwright-core` alpha
beneath it arrives as that package's own **exact** dependency (no `^`, no `~`),
which is what makes the browser revision pinned while the package is not.
`[FLOATS]`

## Tools that reach credentials

**`browser_run_code_unsafe` returns an `httpOnly` cookie against the default
surface.** Demonstrated 2026-08-14: with the default **24-tool** surface and zero
`browser_cookie_*` tools exposed, `async (page) => page.context().cookies()`
returned an `httpOnly` bearer token. The tool is in `core`, so **no capability
setting removes it**. It was the *only* hole — `browser_evaluate` →
`document.cookie` returns `""`, and `browser_network_request` strips `Cookie` and
`Set-Cookie`. `[FLOATS]`

**`browser_storage_state` and the cookie tools return `httpOnly` cookies** —
session bearer tokens JavaScript cannot read. Any mode permitted to call them is
credential-bearing. `[FLOATS]`

**`browser_storage_state` never captures IndexedDB.** It calls `storageState()`
with no options, so `{indexedDB: true}` is never passed. A persistent profile
carries IndexedDB, so a "saved" session silently omits it and the tool is
*weaker* than doing nothing. `[FLOATS]`

**`browser_get_config` DOES redact.** ⚠️ *Corrected 2026-08-20 @
`@playwright/mcp` 0.0.79 / `playwright-core` 1.63.0-alpha-2026-08-05 (previously
"**`browser_get_config` does not redact.** Its handler is
`JSON.stringify(context.config, null, 2)` with no filtering, so it emits
`config.secrets` in plaintext if that key is ever set. It is not set today.")* —
the handler reading was right and the conclusion drawn from it was wrong,
because the redaction is not in the handler. Every response leaves through
`sanitizeUnicode(this._context.redactSecrets(serializedText))`, so the whole
serialised answer is rewritten after the handler has produced it. **Measured**
against the bundled child started with
`secrets: {"MY_TOKEN": "sk-live-9f2b7c41e0aa", "OTHER": "hunter2"}`: the answer
carries `"MY_TOKEN": "<secret>MY_TOKEN</secret>"` and neither literal value
appears anywhere in the frame. **It is still not set today**, and this is still
not a reason to set it — see the substring measurement two entries down.
`[FLOATS]`

**`browser_annotate` opens a dashboard window and blocks until a human finishes
drawing** — and **the window is realised, visible and takes the foreground on a
`headless` session too**, because the dashboard is a *second* browser that
upstream launches headed unconditionally. Measured end to end 2026-08-18 against
a real child; method, timings and the process tree in
[what `browser_annotate` actually does](#what-browser_annotate-actually-does--measured-2026-08-18).
`[FLOATS]`

**`config.secrets` is a real key, and `browser_get_config` names it without
disclosing it.** `--secrets <path>` is on the CLI and
`secrets?: Record<string, string>` is in `config.d.ts`.
`Verified 2026-08-16 @ @playwright/mcp 0.0.79` from the committed `cli-help.txt`
and `config-schema.d.ts` snapshots. ⚠️ *Corrected 2026-08-20 (previously "so
`browser_get_config` can disclose one … the handler serialises the whole config
with no filtering … the answer is forwarded byte-identical on every ordinary
call and refused only if a `secrets` key comes back")* — the values are replaced
by `<secret>NAME</secret>` before the response leaves the child, and the refusal
that clause describes was removed on 2026-08-18. **The key names are still in
the clear**, which is the disclosure that survives: the answer tells the caller
which secrets this child was configured with. BrowserAI never writes the key and
never passes the flag, so on every ordinary call there is nothing to redact and
the answer is forwarded byte-identical. `[FLOATS]`

⚠️ **Redaction is a substring match on the VALUE, so it both over- and
under-fires.** `redactSecrets` runs over the whole serialised response:

```js
redactSecrets(text) {
  for (const [secretName, secretValue] of Object.entries(this.config.secrets ?? {})) {
    if (!secretValue)
      continue;
    text = text.replaceAll(secretValue, `<secret>${secretName}</secret>`);
  }
  return text;
}
```

**Measured 2026-08-20** with a third secret
whose value was `chromium`: the same `browser_get_config` answer came back with
`"browserName": "<secret>COMMON</secret>"` and `"chromiumSandbox"` mangled into
`"<secret>COMMON</secret>Sandbox"`. So a short or common value corrupts unrelated
text, an empty value is skipped outright, and a value the page never renders
verbatim — encoded, split across nodes, or hashed — is not redacted at all.
Upstream says as much in `config.d.ts`: *"a convenience and not a security
feature"*. `[FLOATS]`

### What a BrowserAI session permits, after its own filtering

**Re-measured 2026-08-20 @ `@playwright/mcp` 0.0.79 / `playwright-core`
1.63.0-alpha-2026-08-05: 68 of 69, one row.** ⚠️ **Corrected 2026-08-20
(previously three rows, 58 / 58 / 58 of 58, headed "What BrowserAI's own modes
permit"; corrected twice on 2026-08-18 before that — from 41 / 41 / 58 to
58 / 59 / 59 of 59, and then to 58 / 58 / 58 of 58).** **Session modes were
deleted and every capability is granted to every session**, so there is one row
rather than three and the denominator moved from the 59-tool union to the whole
69-tool exposable surface: `network`, `pdf` and `testing` reached a child for the
first time and brought ten tools with them. Upstream's own per-capability
surfaces are 42 and 69 above; this is what survives BrowserAI's own decision, out
of the **68-tool surface** it advertises to every caller — 69 minus the one it
withholds. Re-establish by running
`SessionPolicyTests.ASessionPermitsEveryToolItAdvertisesAndTheOneThatWouldHangIsNotAdvertised`,
which computes the surface from the committed snapshot, applies the product's own
withholding predicate, and asks its decision function about every name that
survives. `[FLOATS]`

| Session | Advertised | Permitted | Refused, and why |
|---|---:|---:|---|
| any | **68** | **68** | nothing it advertises |

**The 69th tool is `browser_annotate`, and it is not refused conditionally — it
is not offered at all.** It is filtered out of `tools/list` in every session, and
a caller that names it anyway is refused wherever it is named, because the daemon
lands in `%TEMP%` and outlives its parent on a headed run exactly as it does on a
headless one. The measurement is
[what `browser_annotate` actually does](#what-browser_annotate-actually-does--measured-2026-08-18);
the decision and what it would take to reverse are in
[DECISIONS](../../DECISIONS.md#licence-release-policy-and-the-tool-surface).
⚠️ *Corrected 2026-08-18 (previously "`headless` **58** — `browser_annotate`,
whose window appears even here … `interactive` **59** — nothing; `persistent`
**59** — nothing").*

**The ten that arrived on 2026-08-20**, none of which had ever been reachable in
this product or its predecessor: `browser_route`, `browser_route_list`,
`browser_unroute`, `browser_network_state_set` (`network`); `browser_pdf_save`
(`pdf`); `browser_generate_locator`, `browser_verify_element_visible`,
`browser_verify_text_visible`, `browser_verify_list_visible`,
`browser_verify_value` (`testing`). ⚠️ **`browser_run_code_unsafe` is not among
them** — it is `core`, so it was in all three of the old modes' surfaces
including `headless`'s 41.

⚠️ **Corrected 2026-08-18 (previously "`headless` **41** — the 17 `storage`
tools; `browser_annotate` … `interactive` **41** — the 17 `storage` tools;
`browser_run_code_unsafe`, which reaches the same cookies through the Playwright
server process … `persistent` **58** — `browser_annotate`", and beside the table
"the classification behind them is 69 names in five classes — 49 ordinary, 17
`storage`, and one each of `ArbitraryCode`, `HumanPresent` and `Configuration`").**
The `(tool, mode)` permission matrix was removed. **It was never a boundary
against the caller:** the calling agent chooses the session directory, the profile
and its cookie database are created inside it, and the agent runs as the same
Windows user, so DPAPI decrypts for it — an agent holding any file tool reads what
the matrix declined to return — **measured 2026-08-18 and not merely argued**, in
[Chromium's cookie store, and what it takes to read one](../chromium/profiles.md#chromiums-cookie-store-and-what-it-takes-to-read-one--measured-2026-08-18).
The one decision left is **liveness, not security**: `browser_annotate` blocks
until a human draws, and the dashboard window appears on a windowless session
too, so an unattended run that called it would hang until it was killed — also
[measured 2026-08-18](#what-browser_annotate-actually-does--measured-2026-08-18),
after standing undated for the life of the decision it justified. Later the same
day that measurement withdrew the tool from the surface entirely rather than
refusing it per mode, for the reason under the table.

**The `storage` tools are still absent from a windowless session's child**, and
that is a different mechanism which did not change: a `headless` or `interactive`
session's child is started **without the `storage` capability**, so those 17 tools
do not exist in that process at all. They are still *advertised*, because the MCP
spec forbids the tool set varying per connection — so calling one on such a
session now reaches the child and gets upstream's *"unknown tool"* rather than a
BrowserAI sentence naming the mode that would permit it. That is the one thing the
removal cost a model, and it is recorded here rather than argued away.
`browser_run_code_unsafe` was never coverable that way in any case: it is in
`core`, which upstream ors in unconditionally, so **it is reachable from every
mode** — as it always was from `headless` and `persistent`. `[FLOATS]`

## What `browser_annotate` actually does — measured 2026-08-18

**Measured 2026-08-18 @ `@playwright/mcp` 0.0.79 / `playwright-core`
1.63.0-alpha-2026-08-05 / Chrome for Testing 152.0.7977.8 (`chromium-1237`) /
Node v24.19.0**, on [the reference machine](../README.md#the-reference-machine),
with an interactive desktop and the developer's editor holding the foreground.
Three runs. **This entry exists because the sentence it replaces had no date, no
version and no method** — it was the sole justification for the only refusal left
in the product, and one of the two highest-value residues of the 2026-08-18
justification sweep. It is confirmed on both halves, and the mechanism is worse
than the sentence said. `[FLOATS]`

**Both halves hold. The refusal is earned.**

| Run | Annotate budget | Window realised? | Foreground taken? | Call returned? |
|---|---:|---|---|---|
| 1 | 90 s | yes, +1.2 s | yes, same millisecond | **no — silent for the whole 90 s** |
| 2 | 45 s | yes, +1.2 s | yes | only at +42.4 s, in the same 40 ms tick its window disappeared |
| 3 | 60 s | yes, +1.2 s | yes | only at +36.5 s, likewise |

Runs 2 and 3 returned `"### Result\nNo annotations were submitted."`, which is
what upstream answers when the dashboard goes away without a submission. **Run 1
is the control that makes the other two readable**: its window stood for the full
90 s and the call never returned, so there is no self-timeout in this path and
the two early returns were something closing the window — a human at the
keyboard, on a machine where the window had just stolen the foreground. **Nothing
here instrumented *who* closed it**, and it does not matter to the conclusion: the
call ends when the window ends, and on an unattended run nothing ends the window.

**The window is not the headless browser deciding to show itself.** It is a
**second, non-headless Chromium**, and the process tree taken while the call was
blocked says so — 18 descendants, walked by `ParentProcessId` only:

```
probe node
└─ node cli.js  (@playwright/mcp, the session's child)
   ├─ chrome.exe  --headless …  --user-data-dir=<session>\profile     ← the session's own browser
   ├─ node …\playwright-core\lib\entry\dashboardApp.js --pageId=…      ← the DAEMON, detached
   │  └─ chrome.exe  (no --headless)  --user-data-dir=%TEMP%\playwright_chromiumdev_profile-…
   │                                                                   ← the window: 1280x800 at 100,100
   └─ node …\entry\dashboardApp.js --pageId=… --annotate               ← the CLIENT the handler waits on
```

The visible window's owning pid is the second `chrome.exe`, its class is
`Chrome_WidgetWin_1`, its rect is exactly `100,100,1280x800` — which is
`--window-position=100,100 --window-size=1280,800` from upstream's `launchApp2` —
and its image is the browser **BrowserAI itself provisioned**, reached because
`findChromiumChannelBestEffort` resolves the registry `chromium` under the
`PLAYWRIGHT_BROWSERS_PATH` the child was given. In `launchApp2` the headedness is
`headless: !!process.env.PWTEST_DASHBOARD_APP_BIND_TITLE` — **an upstream test
variable and nothing else**, so no session-level configuration reaches it and
`browser.launchOptions.headless` is not consulted on this path at all.

**Three consequences that were not in the sentence, and each is its own fact:**

- **The dashboard daemon is a per-USER singleton on a named pipe**,
  `\\.\pipe\pw-<sha1(USERNAME)[0..8]>-dashboard-app` from `makeSocketPath`, with
  `PWTEST_SOCKETS_DIR` the only thing that moves it. Two BrowserAI sessions
  calling `browser_annotate` at once do not get two dashboards; the second
  connects to the first's. It is also **not scoped to a session directory**, so
  it can meet a dashboard a human started outside BrowserAI entirely. `[FLOATS]`
- **The dashboard's browser writes outside every session directory**, into
  `%TEMP%\playwright_chromiumdev_profile-*`, and the daemon is spawned
  `detached: true, stdio: "ignore"` and `unref`'d — so it does not die with the
  child that started it. It is contained only because it is a **descendant of a
  process in BrowserAI's job object**; nothing else would collect it. `[FLOATS]`
- **There is exactly one bounded failure arm.** `runAnnotateClient` gives up
  connecting to the daemon after **15 s** and exits 1, which the handler turns
  into `Annotation client exited with code 1`. That arm is reached only when the
  dashboard fails to start at all; once it starts, the wait is unbounded by
  construction — `await new Promise(resolve => client.on("exit", …))`. `[FLOATS]`

**How to re-establish.** Write the config BrowserAI generates for a `headless`
session (`capabilities: ["config","vision","devtools"]`,
`launchOptions.headless: true`, `channel: "chrome-for-testing"`), start the
resolved `cli.js` under it with `PLAYWRIGHT_BROWSERS_PATH` pointing at the
provisioned browsers root, `browser_navigate` to a `data:` URL, then call
`browser_annotate` **under a hard timeout** while a `SetWinEventHook` watcher on
`EVENT_OBJECT_CREATE`/`EVENT_OBJECT_SHOW`/`EVENT_SYSTEM_FOREGROUND` plus a 40 ms
`EnumWindows` poll records what reaches the screen — the same watcher that
measured [the 308](../windows/detection.md#what-a-suite-run-puts-on-the-screen),
keyed on `(handle, event)` and never on the handle alone. **Four things the rig
must do, each learned by needing it:** check `\\.\pipe\pw-*-dashboard-app` is
absent *before* starting, or the probe drives a dashboard somebody else owns;
put the whole tree in a **kill-on-close job object**, because when the probe
exits the intermediate node goes with it and a `ParentProcessId` walk can no
longer reach the browser underneath — the job collected 18 processes a walk
found 0 of; take the tree snapshot **while the call is blocked**; and bound the
call, because it will not bound itself.

⚠️ **A run of this probe puts a focus-stealing window on the operator's screen
for the whole budget.** That is the finding, not a side effect.

**What was decided on the strength of this, the same day.** The tool is
**withheld from `tools/list` in every mode** and refused wherever a caller names
it anyway. The three things that would have to change before it could come back
— a bounded call, a daemon inside the session's own containment, and a headless
path that does not turn on an upstream test variable — are recorded in
[DECISIONS](../../DECISIONS.md#licence-release-policy-and-the-tool-surface) and
beside the code in `SessionToolPolicy.IsWithheldFromTheSurface`. Nothing about
this entry is superseded by that: it is the evidence the decision rests on, and
re-implementing the feature starts by re-running it.

## The inline screenshot, and what it costs — measured 2026-08-18

**Measured 2026-08-18 @ `@playwright/mcp` 0.0.79 / `playwright-core`
1.63.0-alpha-2026-08-05 / Chrome for Testing 152.0.7977.8 (`chromium-1237`) /
Node v24.19.0**, off the wire against the published BrowserAI binary and a real
child, on [the reference machine](../README.md#the-reference-machine). Three
pages, one screenshot each, no `filename` argument.

**Upstream returns a screenshot twice — as a file and as an `image` content
block — and the second half is conditional on the caller naming no file.** The
handler ends:

```js
await response2.addFileResult(resolvedFile, data);
if (!params2.filename)
  await response2.registerImageResult(data, fileType);
```

That is the **only** `registerImageResult` call site in the whole resolved
bundle, so `browser_take_screenshot` is the only tool that ever answers with an
image. `Verified 2026-08-18 @ playwright-core 1.63.0-alpha-2026-08-05` against
`coreBundle.js`. `[FLOATS]`

**The bytes are the file's bytes, and the media type is `image/${fileType}`**,
where `fileType = params.type ?? fromExtension(filename) ?? "png"` — the same
expression that decides the extension on disk. Serialisation is
`content.push({ type: "image", data: scaledData.toString("base64"), mimeType: ... })`,
gated once more on `config.imageResponses !== "omit"`. **BrowserAI never writes
that key and its child-environment allowlist does not pass
`PLAYWRIGHT_MCP_IMAGE_RESPONSES`, so the gate is open in every session this
product opens.** `[FLOATS]`

⚠️ **One divergence, deliberate and recorded rather than fixed.** Upstream passes
the bytes through `scaleImageToFitMessage` first, which shrinks anything over
**1,568 px on a side or ~1.15 MP** and *returns the buffer untouched otherwise*
(`shrink = min(1568/w, 1568/h, sqrt(1.15·1024·1024/pixels))`, and `shrink > 1`
returns early). BrowserAI appends what is on disk, so for an image inside that
budget the two are byte-identical and for a larger one — a `fullPage` screenshot
of a long page — BrowserAI sends the unscaled original where upstream would have
sent a shrunk copy. Re-implementing the scaler would mean decoding and
resampling PNG, JPEG and WebP inside the proxy, which is the scope boundary's own
example of what this product must not grow. `[FLOATS]`

### What it costs

| Page (1280×720 viewport) | Bytes on disk | base64 characters | Whole `tools/call` frame | Frame without the image |
|---|---:|---:|---:|---:|
| `<h1>ok</h1>` — near blank | 5,105 | 6,808 | 7,625 | 817 |
| 24 paragraphs of prose | 52,648 | 70,200 | 71,014 | 814 |
| 120 solid colour bands | 4,417 | 5,892 | 6,710 | 818 |

**The wire cost varies 12× across those three; the token cost does not vary at
all.** An image block is billed by *patches*, not by bytes:
`⌈width / 28⌉ × ⌈height / 28⌉` visual tokens, so a 1280×720 screenshot is
`46 × 26 =` **1,196 visual tokens** whatever it compresses to, and stays under
both the standard tier's 1,568 px long edge and the high-resolution tier's
2,576 px, so nothing downscales it.
`Verified 2026-08-18 @ platform.claude.com/docs/en/build-with-claude/vision`.
`[FLOATS]`

**Which is why there is no size threshold in the routing.** A byte-count gate
would fire on the page that costs the model nothing extra and stay silent on the
one that does; the only figure that would justify a gate is the pixel count, and
that is the caller's viewport rather than anything a proxy should second-guess.
Upstream has no threshold either.

**How to re-establish.** Start the published binary, `browserai_init` a
`headless` session, `browser_navigate` to the page, then call
`browser_take_screenshot` **with no `filename`**; read `bytes on disk` from the
path in BrowserAI's own note, `base64 characters` from `content[].data`, and the
frame sizes from the raw response. `VerticalSliceTests.AScreenshotComesBackInlineAsWellAsAsAFileWithALegibleName`
does exactly this against the real child and prints the first three columns on a
passing run.

## Artifacts and output-directory behaviour

All read from the shipped bundle or observed against a real child. `[FLOATS]`

**Playwright writes every artifact flat into one directory with a generated
name**, mixing machine churn with hand-named work. Fixed generator prefixes make
classification exact rather than heuristic.

> ⚠️ **Corrected 2026-08-16 @ `@playwright/mcp` 0.0.79 / `playwright-core`
> 1.63.0-alpha-2026-08-05 (previously: "**Nine fixed generator prefixes**:
> `console`, `download`, `network`, `page`, `request`, `response`, `result`,
> `storage-state`, `video`").** There are **eleven**, plus one empty prefix. The
> nine above were counted by hand on 2026-08-13; deriving the set from the
> resolved bundle for the first time found two more, and both were invisible to a
> scan looking for `prefix: "<literal>"`:
>
> | Missed prefix | Written by | Why the hand count missed it |
> |---|---|---|
> | `element` | `browser_take_screenshot` with a `target` | the expression is `prefix: target ? "element" : "page"` — a ternary, not a literal |
> | `annotations` | `browser_annotate` | the expression is a template literal, `` prefix: `annotations${multi ? "-" + idx : ""}` `` |
>
> A third site, `prefix: this._filePrefix`, is an indirection: it resolves to
> `"console"` through `new LogFile(context, wallTime, "console", "Console")`.
>
> **The full set is now `""`, `annotations`, `console`, `download`, `element`,
> `network`, `page`, `request`, `response`, `result`, `storage-state`,
> `video`** — regenerated into
> [`upstream-snapshots/tools-list.json`](../../upstream-snapshots/tools-list.json)
> under `artifactPrefixes` on every build, so a twelfth is a diff rather than a
> memory. Re-establish with
> `pwsh -File build/Update-UpstreamSnapshots.ps1 -Accept`. `[FLOATS]`

**The empty prefix is the traces template, and `traces\` is upstream's folder
rather than ours.** The call is
`context.outputFile({ prefix: "", suggestedFilename: "traces", ext: "" }, { origin: "code" })`,
which resolves to `<outputDir>/traces`. So it is correct that `traces` is *not* a
generator prefix — the template supplies its own name — and wrong to describe the
folder as one we chose: upstream computes that path and BrowserAI cannot
configure it. `Verified 2026-08-16 @ playwright-core 1.63.0-alpha-2026-08-05`
against `coreBundle.js`. `[FLOATS]`

**The generated name format is `page-2026-08-14T04-11-50-882Z.png`** — a
timestamp, which is precisely what made 346 accumulated session directories
untriageable. The template is
``template.suggestedFilename || `${prefix}-${date.toISOString().replace(/[:.]/g,"-")}${ext ? "." + ext : ""}` ``,
so **a supplied `suggestedFilename` replaces the whole generated name, prefix
included.** `[FLOATS]`

**A caller-supplied `filename` and a generated name resolve against *different*
roots**, which is the fact routing turns on. Measured 2026-08-16 by reading
`coreBundle.js`:

| Path | Function | Resolves against |
|---|---|---|
| `filename` given (`suggestedFilename`) | `workspaceFile(name, cwd)` | `path.resolve(options.cwd, name)` — **the child's cwd** |
| no `filename` | `outputFile(name)` | `path.resolve(config.outputDir, name)` — **the configured output directory** |

That is the whole reason ten repositories currently run a `deny` hook on
`browser_take_screenshot`, and it is closed by setting the child's
`WorkingDirectory` instead of by a hook. Setting the working directory **to the
output directory** makes the two roots coincide, which matters for the check
below. `[FLOATS]`

**Upstream refuses a path outside its own roots, and the message names them.**
`checkFile` returns early for `origin: "code"`, `allowUnrestrictedFileAccess` or
`skillMode`, and otherwise throws
`File access denied: <path> is outside allowed roots. Allowed roots: <outputDir>, <cwd>`.
So a caller-supplied `filename` is already confined by upstream — but only to
those two roots, and only with a message a model has to parse. `[FLOATS]`

**A download lands in the output directory, not in `downloadsPath`.**
`_downloadStarted` calls
`outputFile({ suggestedFilename: sanitize(download.suggestedFilename()), prefix: "download", ext: "bin" }, { origin: "code" })`
and then `download.saveAs(...)`, so the saved copy is
`<outputDir>\<site-suggested-name>` and carries the `download-` prefix **only
when the site suggests no name at all**. `launchOptions.downloadsPath` is where
Playwright keeps the raw artifact, not where the visible file ends up. `[FLOATS]`

**`browser_take_screenshot`'s image format comes from `type` before the file
name.** `fileType = params.type ?? <from filename extension> ?? "png"`, so
supplying a `.png` name to a call that asked for `jpeg` yields jpeg bytes in a
file called `.png`. Any proxy that supplies a name must read `type` first.
`[FLOATS]`

**`browser_start_video` throws on any extension but `.webm`** —
`if (!outputFile.endsWith(".webm")) throw new Error("File must have .webm extension")`
in `FfmpegVideoRecorder`'s constructor. `[FLOATS]`

**Eleven tools carry a `filename` argument and two of them are reads.**
`browser_run_code_unsafe` ("Load code from the specified file") and
`browser_set_storage_state` ("Path to the storage state file to restore from")
both route through `resolveClientFilename` → `workspaceFile`, so they read
relative to the child's cwd and are subject to the same `checkFile`. The other
nine write. Counted 2026-08-16 from the committed `tools-list.json` snapshot;
`ArtifactRoutingTests.EveryToolCarryingAFilenameHasBeenJudged` re-counts it on
every build. `[FLOATS]`

**Sorting the output root costs ~118 µs per call on this machine.** Measured
2026-08-16, 5,000 iterations twice: a non-recursive
`Directory.EnumerateFiles` over an empty directory holding twelve subdirectories
returned in **115.3 µs** and **120.1 µs** per call (NTFS, Defender on).
That is the per-`tools/call` price of classifying the artifacts that cannot be
routed inbound — a download, whose name the site chose, and an annotation, whose
name upstream chose. `[MACHINE]`

**Pre-creating the typed folders costs 4× what creating three does.** Measured
2026-08-16, 120 sessions per pass, twice: a session directory plus the three
`profile` / `output` / `downloads` folders takes **2.50–2.63 ms**; the same plus
all eleven typed artifact folders takes **10.39–10.46 ms**. Reclaiming the whole
tree afterwards costs proportionally more again. At roughly 120 sessions per
suite run that is about a second each way, which is why BrowserAI creates a typed
folder on first use rather than up front — and why a folder that exists in a
session directory means an artifact of that kind was actually produced.
`[MACHINE]`

**`_meta.json`, `_meta.cwd` and `_meta.raw` are read by the child before zod
parsing** and stripped before the tool sees them. Undocumented but real, and
available for a proxy to inject (JSON error format, relative-path base).

**Killed children leak `browser@<guid>` descriptors.** Each is a JSON file in the
browsers-registry root holding the absolute `userDataDir` and `workspaceDir`;
`BrowserServer.stop()` removes them only when there is **no** `userDataDir`. **28
were observed and removed on 2026-08-14** (`[MACHINE]` for the count). The
registry root sits at `%LocalAppData%\BrowserAI\browsers\`, outside `current\` under the current design — a tree
that should be read-only and is wiped on update.

**Real screenshots are not byte-stable across runs**, so passthrough-fidelity
assertions need a canned blob from a fake child rather than a live capture.
