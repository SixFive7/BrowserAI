<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Upstream configuration facts

**Versions in force** unless an entry says otherwise: `@playwright/mcp` 0.0.79 · `playwright-core` 1.63.0-alpha-2026-08-05, read from the resolved bundle · Chrome for Testing 152.0.7977.8 (`chromium-1237`) · Firefox 153.0 (`firefox-1539`) · Windows 11 Pro 26200.
Measured on [the reference machine](../README.md#the-reference-machine).

All `[FLOATS]`, all read from the shipped
`playwright-core/lib/coreBundle.js` or the shipped binaries unless noted.

## Silent config failures

**`chromiumSandbox: true` in a config file is discarded.** With it set
explicitly, the browser and every child still ran `--no-sandbox`. Only the CLI
`--sandbox` flag enabled it. `validateBrowserConfig` *intends*
`chromiumSandbox = true` on non-Linux, so this is upstream behaviour
contradicting upstream intent — and it means the default posture is unsandboxed.

> `Verified 2026-08-16 @ @playwright/mcp 0.0.79 / playwright-core
> 1.63.0-alpha-2026-08-05.` Re-measured three ways from the resolved browser
> command line of a live Chromium rather than from the config: **nothing set →
> `--no-sandbox` present** (6 processes carried it); **`chromiumSandbox: true` in
> the config file, no flag → still present** (6 processes); **`--sandbox` on the
> command line → absent from the browser and from every one of its children** (0
> occurrences anywhere in the tree). Re-establish with `SandboxFlagTests`, which
> runs the first-and-third case through the product and the second directly
> against the child.
>
> **And the mechanism is now measured rather than inferred**, which is what
> explains the contradiction. Upstream declares **both** `--sandbox` and
> `--no-sandbox`, and commander gives `sandbox` a default of **`false`** rather
> than leaving it undefined — read back twice from the shipped bundle by parsing
> an empty argv through `tools.decorateMCPCommand`, `opts.sandbox === false`
> while `opts.headless === undefined`. `configFromCLIOptions` then sets
> `launchOptions.chromiumSandbox` whenever `cliOptions.sandbox !== undefined`,
> and the CLI stage merges **last**, so it always overwrites the config file's
> value and `validateBrowserConfig`'s non-Linux `chromiumSandbox = true` branch
> is unreachable on the MCP path. That also predicts the fix precisely: any
> future config key upstream reads *after* the CLI merge would work, and this one
> never can. `[FLOATS]`
>
> ⚠️ **Fixed upstream 2026-08-17, and not yet in a version this build
> resolves.** [microsoft/playwright#42288](https://github.com/microsoft/playwright/pull/42288)
> — *fix(mcp): do not clobber chromiumSandbox from the config file* — deletes the
> normaliser named above outright (`options.sandbox = options.sandbox === true ?
> undefined : false`, four lines, zero additions) and adds two tests citing
> [playwright-mcp#1716](https://github.com/microsoft/playwright-mcp/issues/1716),
> which was closed `COMPLETED` by the merge. **Everything measured above still
> describes the shipped tree**: `@playwright/mcp` 0.0.79 pins `playwright-core`
> 1.63.0-alpha-2026-08-05, and both predate the merge — re-resolved and confirmed
> unmoved on 2026-08-19.
>
> **What changes when it arrives, and it is a mechanism change rather than an
> outcome change on Windows.** `--sandbox` used to work by mapping to
> `undefined` and falling through to `validateBrowserConfig`'s non-Linux
> `chromiumSandbox = true`; after the fix commander leaves the flag `undefined`
> when it is absent and `true` when it is passed, so `configFromCLIOptions` sets
> the key **explicitly**. BrowserAI passes `--sandbox` on the command line and is
> therefore sandboxed on both sides of the change — which is why this is a
> re-verification row rather than a defect. The half that does invert is the
> config-file key, which starts working; nothing in this product sets it.

**`loadConfig` is a bare `JSON.parse` with no schema validation**, so a renamed
or removed key is silently ignored. `--output-mode` was a no-op for its entire
life — a hardcoded literal in 0.0.78's bundle, never read from config — and was
then removed outright in 0.0.79, where passing it produces `error: unknown
option` and exit 1. The two failure classes are asymmetric and both are live: a
**CLI flag fails loudly**, a **JSON config key fails silently**.

## Defaults that are not what they look like

**`validateBrowserConfig` defaults to `chromium` *and* sets `channel: "chrome"`**
when no `browserName` is given — i.e. the user's **installed Google Chrome**, not
anything we shipped. Verified empirically: with an *empty* browsers directory,
`initialize`, `tools/list` and `browser_navigate` all succeeded.

**Binary selection** (`getExecutableName`): a channel that is a chromium alias
(`chrome-for-testing`) → `chromium`; any other channel → that channel; otherwise
`headless ? "chromium-headless-shell" : "chromium"`. So **headless does not force
the shell — absence of a channel does**, and `chrome-for-testing` yields the full
binary even headless.

> **The alias list is exactly one entry**, read from the resolved bundle
> 2026-08-16: `chromiumAliases = ["chrome-for-testing"]`, referenced by
> `isChromiumAlias` and by `resolveBrowsers`. It is also what upstream's own
> `--browser chromium` resolves to — `resolveBrowserParam` returns
> `{ browserName: "chromium", channel: "chrome-for-testing" }` — so the channel
> BrowserAI generates is upstream's own spelling rather than a synonym.
> Re-establish by grepping the bundle for `chromiumAliases`; a second alias would
> not break anything, but the *first* one being renamed would break every launch.
> `[FLOATS]`
>
> **Confirmed end to end 2026-08-16:** a launch with
> `browserName: "chromium"`, `channel: "chrome-for-testing"` and
> `headless: true` resolved
> `…\BrowserAI\browsers\chromium-1237\chrome-win64\chrome.exe`, with `--headless`
> on its command line and no `chromium_headless_shell-1237` directory present at
> all. Asserted every run by `HeadlessBinaryTests`.
>
> **And the failure when the tree is empty is loud, which is the whole premise.**
> With the same generated config and an empty `PLAYWRIGHT_BROWSERS_PATH` root,
> `initialize` and `tools/list` still succeed — as they did in the 2026-08-13
> measurement — and `browser_navigate` returns `isError: true` with
> *`Browser "chrome-for-testing" is not installed; expected executable at
> <root>\chromium-1237\chrome-win64\chrome.exe. Run `npx @playwright/mcp
> install-browser chrome-for-testing` to install`*. Note the remediation string
> names a package BrowserAI does not ship; replacing it is
> first-run provisioning's
> job. `[FLOATS]`

> ✅ **`--browser chromium` supplies a channel, so it takes the alias branch.**
> `Corrected 2026-08-17 (previously "This selector is authoritative, and one
> observation disagrees with it … `[UNVERIFIED]` as to which branch the 0.0.79
> run took")`. `resolveBrowserParam` is the stage between the CLI and this
> selector, and for the single value `"chromium"` it substitutes
> `channel: "chrome-for-testing"` — which `isChromiumAlias` then matches, so
> `getExecutableName` returns before it ever reaches its `headless ? …` line.
> [kb: detection](../windows/detection.md#enumeration-works--and-it-moves-the-safety-boundary)
> recorded `--headless --browser chromium` spawning full `chrome.exe`, and that
> is what this selector predicts once the stage above it is read. **Nothing is
> retracted on either side**; what was missing was one function.
>
> The `headless ? "chromium-headless-shell" : "chromium"` line is the
> **fall-through**, reachable only when no channel is set at all, which no
> `--browser` value produces. It is still true that BrowserAI gets the full
> binary **because it sets the channel** — it sets `browserName` *and* an explicit
> chromium-alias channel in every mode — and the shell branch is not something to
> rely on being unreachable by accident. `[FLOATS]` — re-establish by reading
> `resolveBrowserParam`, `configFromCLIOptions` and `getExecutableName` together
> in the resolved bundle, never `getExecutableName` alone.

**On Windows `headless` defaults to `false`.** `resolveCLIConfigForMCP` sets it
only to `os.platform() === "linux" && !process.env.DISPLAY`.

> **It is a default, not an override, and the distinction is load-bearing.**
> Read from the resolved bundle 2026-08-16, the assignment is guarded:
> `if (browser.launchOptions.headless === void 0) browser.launchOptions.headless
> = …`. So a config file's `headless: true` **survives** on Windows — confirmed
> by a launch whose browser command line carried `--headless`. Read the entry
> above as *"no key means a window appears"*, never as *"upstream overwrites
> your key"*. Unlike `chromiumSandbox`, commander leaves `opts.headless`
> **undefined** when the flag is absent, which is why the CLI stage does not
> stamp on this one. `[FLOATS]`

**`isolated` is not auto-defaulted on the MCP path.** The auto-default block
(`!options.profile && !options.persistent && !userDataDir && ...`) lives in
`resolveCLIConfigForCLI`, the `playwright` CLI daemon path — not in
`resolveCLIConfigForMCP`. It is also structurally impossible for us:
`validateBrowserConfig` throws on `isolated` + `userDataDir`. Note the legacy
setup set it explicitly in three of its four modes.

**`outputMaxSize` has no default at any merge stage.** `defaultConfig` contains
only `browser: {launchOptions:{}, contextOptions:{}}` and `timeouts: {action:
5e3, navigation: 6e4, expect: 5e3, settle: 500}`; `mergeConfig` filters through
`pickDefined`, which drops `undefined`. When set, `_enforceOutputBudget()` runs on
**every tool response**, recursively lists the whole output directory, and unlinks
oldest-mtime-first past the threshold, sparing only the current response's writes.
Unlink failures go to a debug log. Settable via
`PLAYWRIGHT_MCP_OUTPUT_MAX_SIZE`, so stripping the flag is not enough.

**Inline images are always downscaled.** `scaleImageToFitMessage` shrinks to fit
1568 px and ~1.15 megapixels, unconditionally and with no config. The **file
written to disk is full resolution** — the cap is on the copy entering the
model's context.

**`--console-level` defaults to `info`**, which silently drops `debug` messages.

**An unset `userDataDir` puts every run's profile in a directory keyed by the
client's working directory.** `createUserDataDir` is
`path.join(defaultCacheDirectory(), "ms-playwright-mcp",
"mcp-<channel-or-browserName>-<sha256(clientInfo.cwd).slice(0,7)>")` with an
eager `mkdir(..., {recursive: true})`, and it is reached from exactly one call
site — `config.browser.userDataDir ?? await createUserDataDir(...)`, inside the
launch path. So **a distinct client cwd is a distinct profile directory, created
the moment a browser launches, and never cleaned up.**

> **Measured 2026-08-16 @ `@playwright/mcp` 0.0.79 / `playwright-core`
> 1.63.0-alpha-2026-08-05.** On this machine the pile reached **159 directories
> / 877 MB** before BrowserAI began setting the key — it had been 27 / 193 MB on
> 2026-08-14 and 47 / 318 MB earlier the same day, because every suite run with a
> fresh scratch directory adds one. **Setting `browser.userDataDir` avoids the
> function entirely**, verified by deleting `%LOCALAPPDATA%\ms-playwright-mcp\`
> and running the whole suite twice: absent both times. Re-establish the same
> way — delete it, run the browser-touching tests, and look. Note the constraint
> that comes with the key: `validateBrowserConfig` throws on `isolated` together
> with `userDataDir`, so the two can never both be set. `[FLOATS]`
>
> ⚠️ **A hand-written config in a test is subject to the same default.** The
> first check found one directory recreated, by the one test that writes its own
> config to exercise `chromiumSandbox`. A config that launches a browser and does
> not name a `userDataDir` writes there, whoever wrote it.

**There is no trace option at 0.0.79 — not on the CLI and not in the config.**
`tracesDir` is computed internally as `path.resolve(outputDir, "traces")` and is
not configurable; the nearest surviving feature is **`saveSession`**
(`--save-session`), *"whether to save the Playwright MCP session into the output
directory"*.

> **Measured 2026-08-16** by grepping the resolved bundle for `saveTrace` (no
> hits) and reading the committed `cli-help.txt` and `config-schema.d.ts`
> snapshots (no trace key in either). This matters because
> [the session modes](../../ARCHITECTURE.md#sessions) make
> `tracing` a boolean modifier on every session mode, and it therefore maps to
> `saveSession` rather than to a trace. Re-establish with the same grep at each
> bump; a restored trace option is a reason to revisit the mapping. `[FLOATS]`

**`browser_get_config` answers Markdown with JSON inside it, not JSON.** The tool
body is `response.addTextResult(JSON.stringify(context.config, null, 2))`, but
the response builder prefixes every text section with `### <title>` before it
reaches the wire unless the response is `_raw`.

> **Measured 2026-08-16** — the first version of `ConfigRoundTripTests` parsed
> the whole text and failed with *"'#' is an invalid start of a value"*. Anything
> reading this tool must slice the JSON out (first `{` to last `}`); a heading
> cannot contain a brace. Re-establish by calling the tool and looking at the
> first line. `[FLOATS]`

## Browser provisioning

**Downloads retry 5 times, rotating mirrors** —
`downloadURLs[(attempt - 1) % downloadURLs.length]`. This is why
`PLAYWRIGHT_DOWNLOAD_HOST` must be stripped: it collapses the mirror list to one
host, so all five attempts hit the same dead server.

> ⚠️ **Qualified 2026-08-16: Chrome for Testing already has only one mirror**, so
> for the browser itself the rotation is a no-op and the five retries all hit
> `cdn.playwright.dev`. The full finding, with the URL shapes, is in
> [kb: first-run provisioning](provisioning-and-timings.md#first-run-provisioning).
> The strip is still right — it is what keeps the rotation working for `ffmpeg`,
> `winldd` and `firefox` — but "five attempts at five hosts" was never true of
> the 202 MB half of the download.

**The per-socket stall timeout is upstream's `NET_DEFAULT_TIMEOUT = 3e4`**, read
2026-08-16 out of the resolved bundle, applied as
`+(getFromENV("PLAYWRIGHT_DOWNLOAD_CONNECTION_TIMEOUT") || "0") || NET_DEFAULT_TIMEOUT`
and passed down to `request.setTimeout`. **BrowserAI sets nothing**, so the
figure stays upstream's rather than being duplicated into a constant of ours that
would drift the day theirs moved; the variable is absent from the installer's
environment by construction, because
`src/BrowserAI/Protocol/ChildEnvironment.cs` is an allowlist. The three caps
BrowserAI *does* own — 45 minutes absolute, 10 minutes on extraction, 60 as a
crash tripwire — are all far above it, so a stalled socket is upstream's retry
loop's business and not ours. Re-establish by grepping `NET_DEFAULT_TIMEOUT` in
`coreBundle.js`. `[FLOATS]`

**The four download-host variants, named.** Measured 2026-08-16 from the
resolved payload rather than from memory —
`grep -o "PLAYWRIGHT_[A-Z_]*DOWNLOAD_HOST"
payload/mcp/node_modules/playwright-core/lib/coreBundle.js | sort -u` returns
exactly `PLAYWRIGHT_DOWNLOAD_HOST`, `PLAYWRIGHT_CHROMIUM_DOWNLOAD_HOST`,
`PLAYWRIGHT_FIREFOX_DOWNLOAD_HOST` and `PLAYWRIGHT_WEBKIT_DOWNLOAD_HOST`, and
nothing else. Every document that says *"and its three per-browser variants"*
means these; the strip list in `src/BrowserAI/Protocol/ChildEnvironment.cs`
carries all four by name and refuses them even under a different casing, because
a Windows environment block is case-insensitive. Held at `playwright-core`
**1.63.0-alpha-2026-08-05**; re-run the grep at each bump, since a fifth browser
would add a fifth. `[FLOATS]`

**`INSTALLATION_COMPLETE` short-circuits without validating anything.** Written
last, so an *interrupted* install self-heals. But a browser corrupted **after** a
successful install never re-downloads — `spawn EFTYPE` forever — and upstream's
remediation string points at `npx @playwright/mcp install-browser chromium`, a
package we do not ship resolving a different revision.

**The remediation string's exact shape, because BrowserAI replaces it.** Read
2026-08-16 in `playwright-core/lib/coreBundle.js`, `throwIfExecutableMissing`:

```
`${label} is not installed${location}. Run \`${command}\` to install`
```

with `label` = `Browser "${target}"` or `FFmpeg`, `location` =
`; expected executable at ${path}` when the error carries one, and `command`
chosen by a ternary on `config.skillMode` between
`npx @playwright/mcp install-browser ${target}` and
`playwright-cli install-browser ${target}`. ⚠️ **`target` is the resolved
*channel*, not the browser family** —
`config.browser.launchOptions?.channel ?? config.browser.browserName` — so a
BrowserAI caller is told to install **`chrome-for-testing`**, which is not a
`browserName` at all. `src/BrowserAI/Runtime/ProvisioningRemediation.cs` matches
the whole `Run \`…install-browser…\` to install` clause, so **both** branches of
that ternary are covered by one pattern; a reword upstream would make the strip
stop firing silently, which is why this shape has
[a row of its own](../re-verification.md). `[FLOATS]`

**⚠️ `browser_get_config` needs the browser executable to exist.** Measured
2026-08-16 @ `@playwright/mcp` 0.0.79, twice, by driving `cli.js` directly with
`PLAYWRIGHT_BROWSERS_PATH` at an **empty** directory: the call answers
`isError: true` with `Browser "chrome-for-testing" is not installed; expected
executable at …`, from `throwIfExecutableMissing`. It does **not** launch
anything — which is why the round trip is cheap on a provisioned machine — but
the binary has to be there. **This contradicts
[The provisioning design](../../ARCHITECTURE.md#the-runtime-it-ships)'s claim that the
tool keeps working during first-run provisioning**, and the plan was corrected
rather than the measurement: BrowserAI refuses every upstream tool while a
download is running, including this one, because upstream's error would advise
provisioning a browser that is already being provisioned. Re-establish with
`cli.js --config <cfg>` against a fresh browsers root and one
`browser_get_config` call. `[FLOATS]`

**Integrity is ours to provide.** Playwright validates only `content-length`;
upstream closed and locked the request for checksums
([microsoft/playwright#39559](https://github.com/microsoft/playwright/issues/39559)).

**`winldd` dependency validation is a permanent no-op for Chromium.** Upstream
passes `["chrome-win"]` while Chromium extracts to `chrome-win64`
(`EXECUTABLE_PATHS.chromium["win-x64"] = ["chrome-win64","chrome.exe"]`), so it
checks a directory that does not exist. Same for `chromium-headless-shell` vs
`chrome-headless-shell-win64`. Firefox passes `["firefox"]`, the real directory,
so it **does** run — **39 binaries and +329 ms for Firefox**, cached in
`DEPENDENCIES_VALIDATED` with
`kMaximumReValidationPeriod = 30 * 24 * 60 * 60 * 1e3`, i.e. a recurring monthly
cost. If upstream ever fixes the directory name, Chromium starts validating on
cold start too — a latency regression from a one-character fix.

⚠️ **Corrected 2026-08-19 (previously "Chromium starts validating 39 binaries on
cold start").** **39 is Firefox's measured count and Chromium's has never been
measured** — it cannot be, from here, precisely because the check does not run
against a directory that exists. What is known about Chromium is the shape of the
regression, not its size; treat a Chromium figure as `[UNVERIFIED]` until the
directory name is fixed upstream and the count is taken. The Firefox number
matters more than it did: `browserai_init` has offered `browser: "firefox"` since
2026-08-19, so this cost is now paid on a caller's cold start rather than only by
the suite. [HAZARDS](../../HAZARDS.md#hazard-index) carries the row.

## Environment, merge order and startup output

**The merge order is config file → environment → CLI**, and `@playwright/mcp`
reads **40** `PLAYWRIGHT_MCP_*` variables in its config env mapping — `BROWSER`,
`HEADLESS`, `USER_DATA_DIR`, `EXECUTABLE_PATH`, `OUTPUT_DIR`, `ISOLATED`,
`CONFIG`, `SECRETS_FILE`, `STORAGE_STATE`, `CAPS` and 30 more. **The real total
is 42**: `PLAYWRIGHT_MCP_PING_TIMEOUT_MS` and `PLAYWRIGHT_MCP_EXTENSION_TOKEN`
are read *outside* that mapping. An allowlist test must derive the count from the
resolved bundle and never carry a literal.

**`capabilities` replaces, it does not merge.** `mergeConfig` spreads defined
overrides, so passing `--caps` on the command line **silently wipes** the config
file's capability list — and `PLAYWRIGHT_MCP_CAPS` triggers the identical wipe,
which is an environment route to a bug that a "never pass `--caps`" rule does not
close.

**`PLAYWRIGHT_SKIP_VALIDATE_HOST_REQUIREMENTS` writes a line to stderr when
set** — enough on its own to trip an error-shaped-stderr classifier.

**Playwright's stale-browser GC deletes any registry directory not referenced by
a `.links` entry.** Against a browsers tree we installed, the blast radius is
"deletes our own Chromium", so `PLAYWRIGHT_SKIP_BROWSER_GC=1` is mandatory and
pruning old revisions becomes the caller's job.

⚠️ **Corrected 2026-08-16 @ `@playwright/mcp` 0.0.79 (previously "A healthy start
prints `Session: <path>` to stderr, every time").** It prints that line **only
when `saveSession` is on**, and a healthy start with it off writes **nothing at
all** to stderr. Measured twice each way on 2026-08-16 — `node.exe cli.js
--config <abs> --sandbox`, `initialize` → `browser_navigate
data:text/html,<h1>ok</h1>` against `chromium-1237`, whole stderr buffer read to
EOF after a graceful stdin close. With `saveSession: true` stderr is **exactly one
line**, `Session: <outputDir>\session-<epoch-ms>`. With `saveSession: false` it is
empty. The mechanism is in `coreBundle.js`: `this._sessionLog =
this._config.saveSession ? await SessionLog.create(this._config, clientInfo.cwd) :
void 0`, and `SessionLog.create` is the only `console.error(`Session:
${sessionFolder}`)` call site in the bundle.

The old sentence was true of the setup it was written against — all four of that
launcher's `config.json` files set `saveSession: true` — and it is **not** true of
BrowserAI's default, which writes the key from the `tracing` modifier and leaves
it off. Nothing about the classifier changes either way: silence is benign too.
`[FLOATS]`, [row 33](../re-verification.md).

**Which error shapes actually reach stderr at 0.0.79, measured twice each,
2026-08-16.** The dead `--output-mode` flag still does: stderr carries `error:
unknown option '--output-mode'` and the process exits **1**. The missing browser
**does not**: against an empty browsers root, `initialize` succeeds, `tools/list`
succeeds, `browser_navigate` returns a JSON-RPC **success** whose body is
`isError: true` carrying `Browser "chrome-for-testing" is not installed; expected
executable at …`, the process exits **0**, and stderr is **empty**. That is the
founding failure shape arriving from upstream, and it means the second ported
regex's `is not installed` phrase has no stderr occurrence to match in this
version. It is kept regardless: the regexes are
[ported verbatim](../../ARCHITECTURE.md#process-containment-and-observability) precisely so that a transcription
difference cannot be a silent behaviour change, and a phrase that fires on no
current output costs nothing while a deleted one cannot be recovered by anybody
who did not know it was there. `[FLOATS]`.

## Policy

**Chrome for Testing reads policy from
`HKLM|HKCU\SOFTWARE\Policies\Google\Chrome for Testing`** — verified from Unicode
strings in the shipped `chrome.exe`/`chrome.dll`. Not `Policies\Chromium`, not
`Policies\Google\Chrome`. A perfectly isolated namespace: nothing set there can
reach the user's Chrome. Recorded as a reusable lever even though no policy
solves the resurrection problem.

⚠️ **`GetUserDataDirFromRegistryPolicyIfSet` reads
`SOFTWARE\Policies\<brand>\UserDataDir` and *overrides the command line***, in
`chrome_elf` before the browser parses argv. If that key is ever set, per-session
profile isolation collapses silently. Measured absent everywhere on this machine
(all three brands, HKLM and HKCU, including `WOW6432Node`). **Assert at startup
that the resolved user-data-dir is what we passed.** `[MACHINE]` for the absence,
`[FLOATS]` for the mechanism.

## Shutdown

**`setupExitWatchdog`** hooks `stdin` close, `SIGINT` and `SIGTERM`, calls
`gracefullyCloseAll()`, and hard-exits after 15 s
(`setTimeout(() => process.exit(0), 15e3)`). Closing stdin is therefore the
graceful teardown path and needs no killing at all.
