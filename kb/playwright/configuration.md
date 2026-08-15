<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Upstream configuration facts

All `[FLOATS]`, all read from the shipped
`playwright-core/lib/coreBundle.js` or the shipped binaries unless noted.

## Silent config failures

**`chromiumSandbox: true` in a config file is discarded.** With it set
explicitly, the browser and every child still ran `--no-sandbox`. Only the CLI
`--sandbox` flag enabled it. `validateBrowserConfig` *intends*
`chromiumSandbox = true` on non-Linux, so this is upstream behaviour
contradicting upstream intent — and it means the default posture is unsandboxed.

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

**On Windows `headless` defaults to `false`.** `resolveCLIConfigForMCP` sets it
only to `os.platform() === "linux" && !process.env.DISPLAY`.

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

## Browser provisioning

**Downloads retry 5 times, rotating mirrors** —
`downloadURLs[(attempt - 1) % downloadURLs.length]`. This is why
`PLAYWRIGHT_DOWNLOAD_HOST` must be stripped: it collapses the mirror list to one
host, so all five attempts hit the same dead server.

**`INSTALLATION_COMPLETE` short-circuits without validating anything.** Written
last, so an *interrupted* install self-heals. But a browser corrupted **after** a
successful install never re-downloads — `spawn EFTYPE` forever — and upstream's
remediation string points at `npx @playwright/mcp install-browser chromium`, a
package we do not ship resolving a different revision.

**Integrity is ours to provide.** Playwright validates only `content-length`;
upstream closed and locked the request for checksums
([microsoft/playwright#39559](https://github.com/microsoft/playwright/issues/39559)).

**`winldd` dependency validation is a permanent no-op for Chromium.** Upstream
passes `["chrome-win"]` while Chromium extracts to `chrome-win64`
(`EXECUTABLE_PATHS.chromium["win-x64"] = ["chrome-win64","chrome.exe"]`), so it
checks a directory that does not exist. Same for `chromium-headless-shell` vs
`chrome-headless-shell-win64`. Firefox passes `["firefox"]`, the real directory,
so it **does** run — 39 binaries, +329 ms, cached in `DEPENDENCIES_VALIDATED` with
`kMaximumReValidationPeriod = 30 * 24 * 60 * 60 * 1e3`, i.e. a recurring monthly
cost. If upstream ever fixes the directory name, Chromium starts validating 39
binaries on cold start — a latency regression from a one-character fix.

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

**A healthy start prints `Session: <path>` to stderr, every time.** Any
classifier treating stderr output as an error signal fires on every clean launch;
the legacy setup's did.

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
