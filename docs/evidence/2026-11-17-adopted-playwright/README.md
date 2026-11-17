<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-11-17 - the workarounds and rows re-checked on the adopted Playwright

**The rigs run in the batch that adopted `@playwright/mcp` 0.0.85**, by the maintainer's
178 b and 179 a, against the payload that commit 2780f5b9 records: `@playwright/mcp`
**0.0.85**, `playwright-core` **1.66.0-alpha-2026-11-09**, Chrome for Testing
**158.0.8127.18** (`chromium-1254`), Firefox **159.0** (`firefox-1573`) and node
**v26.13.1**. Where a run names the earlier payload it is the one 2.0.0 ships, kept at
`.work\payload-0.0.83-registerai-0.3.0`: `@playwright/mcp` 0.0.83, `playwright-core`
1.64.0-alpha-1790635538000, Chrome for Testing 155.0.8059.12 (`chromium-1247`), Firefox
156.0 (`firefox-1553`) and node v24.21.0. Every browser was headless, every run took the
suite lock through one holder, one rig at a time at below-normal priority, between
17:13Z and 17:49Z.

## What is here

| Folder | What it is |
|---|---|
| `stdin-end/` | Twelve runs of a child whose standard input is closed after one `browser_navigate`, three per family on each payload: the rig's own result and the child's `DEBUG=pw:browser` standard error, whole |
| `stdin-end-close-in-flight/` | Six more runs on 0.0.85, three per family, in which a `browser_close` is sent 100 ms before the input closes, so that the close is still in flight when it does |
| `safe-mode/` | The environment block of the Firefox browser process each payload's `playwright-core` started, read from the process with nothing set in node's own environment |
| `profiler/` | The thread names of the Chrome for Testing browser process in five launches, and the two runs of `StackProfilerTests` with BrowserAI's switch removed: planted red and green |
| `blank-page/` | The `issue`, `navigate` and `repro` stages of [the blank-page rig](../../probes/2026-10-13-blank-page/README.md), each with its summary |
| `tall/` | A full-page screenshot of a page 16,400 px tall on the raw child, Chrome for Testing and Firefox, and the two readers of the lifetime rig over the Chromium images |
| `webp/` | Row 122's bracket on the raw child: 16,383 and 16,384 px, `webp`, `png` and `jpeg` |
| `rows/` | Rows 121, 140 and 141 of [the re-verification index](../../../kb/re-verification.md), each by the method the row gives |
| `isolated-context/` | What `browser_tabs`' new `isolatedContext` does in a child configured like BrowserAI's with the network capture on |
| `launcher/` | What each of the three cleans took out of `HKCU\Software\Mozilla\Firefox\Launcher` |
| `webmcp/` | Seven rigs of the WebMCP launch, taken after the page-tool arms went red on 0.0.85 in the gate at `675f92c2`, and three runs of `PageToolTests` with `WebMcpLaunchTests`: the WebMCP switch absent, spelled alone, and as the fix spells it; and Firefox's `ModelContext.webidl` as read on its main branch |
| `rig/` | Every script that produced the above, as text, as it ran |

## What the runs found

**At the end of its input the child closes its browser once, gracefully, and kills
nothing, unless a close is already in flight.** On 0.0.85 all six runs logged `<gracefully close start>` and `<gracefully close
end>` and no `<forcefully close>`, no `cmd.exe` or `taskkill.exe` joined the child's job,
and node exited with code 0 359 to 408 ms after the input closed for Chromium and 988 to
1,895 ms for Firefox. On 0.0.83 all six took the force branch: `<forcefully close>` and
`<will force kill>` 2 to 6 ms after the graceful start, `cmd.exe` joining the job
36 to 54 ms after the input closed and `taskkill.exe` 208 to 920 ms after. `leftInJob` is
how many processes the job still held the instant node was seen gone, before the job was
closed.

With a `browser_close` sent 100 ms before the input closed, 0.0.85 took the force branch
in all six runs: `<forcefully close>` 99 to 124 ms after the close's own graceful start,
`cmd.exe` joining the job 14 to 119 ms after the input closed and `taskkill.exe` 135 to 292
ms after, and node exiting 1,642 to 2,392 ms after. A second graceful close of a browser
already closing is the force branch, read in `processLauncher`'s `gracefullyClose` in both
bundles, and the end of the input is that second close.

**Playwright puts `MOZ_DISABLE_SAFE_MODE_KEY=1` into Firefox's environment itself.** With
the variable absent from node's environment, the browser process 0.0.85 started carried
`MOZ_DISABLE_SAFE_MODE_KEY=1`; the one 0.0.83 started did not, and carried
`MOZ_HEADLESS=1` as its only `MOZ_` variable.

**Chrome for Testing 158 ran no stack sampling profiler.** `StackSamplingProfiler` was
absent from the browser process with Playwright's `--disable-stack-profiler`, with the
switch taken out through `ignoreDefaultArgs`, and with it taken out and `--browser-test`
added; Chrome for Testing 155 ran it with no switch and did not with BrowserAI's. The arm,
with BrowserAI's switch removed, failed when a scratch edit pointed the session at
`chromium-1247` and took Playwright's switch out, and passed without the edit.

**The blank page is still needed for a Firefox restore.** Each stage gave what it gave on
2026-10-13: `repro.mjs` timed out waiting for `load` on `about:neterror` after 15 s,
`restore.mjs` timed out at 30 s, Chromium reported `load` after 32 ms; a restore with the
blank page left out timed out at 30,004 ms and with it kept launched in 1,863 ms.

**Chromium still repeats past 16,384 px.** Both Chromium images are 16,400 px tall and
their last 16 rows are the first 16 again; no row above the line repeats the row 1,000
above it. Firefox's capture of the same page came back with no error.

**A `webp` past 16,383 px is an error now.** Chrome for Testing 158 answered the 16,384 px
`webp` with `isError` and *"Protocol error (Page.captureScreenshot): Unable to encode
screenshot"*, and wrote no file; 16,383 px gave a valid 8,732-byte image and `png` and
`jpeg` were fine at 16,384 px. `google-chrome-157/` is the same rig run first with
`chrome-for-testing` as the browser name, which `@playwright/mcp` does not know and answers
with the machine's Google Chrome; it gave the same error, from Google Chrome 157.

**Rows 121, 140 and 141 hold.** The plain launch logged one `sandbox_win.cc` line and the
launch with `--disable-field-trial-config` none, twice each; a 0-byte and a truncated
executable gave `EFTYPE`, garbage `UNKNOWN`, and both controls failed the way the row
says; six captures across two processes gave one SHA-256 and the changed page another.

**A tab in an isolated context is missing from the network capture, and its cookie
list starts with the default context's cookies.** In both families the archive held the
default context's page and none of the isolated one's, the isolated tab's cookie list
showed the default context's cookie beside its own, the default tab's showed only its
own, and after a relaunch on the profile only the default context's cookie was there.

**A page's tools reach the child only through the browser's own WebMCP, launched on.** On
0.0.85 a Chromium launched without `--enable-features=WebMCP`, or a Firefox without both
`dom.modelcontext.enabled` and `dom.modelcontext.testing.enabled`, made Playwright throw
*"WebMCP is not enabled"*, which its listing swallows. With them, a page on 127.0.0.1 got the
browser's own interface, `document.modelContext` in Chromium and `navigator.modelContext` in
Firefox, and a tool registered through it was listed and answered; Firefox's interface had
`getTools` and `invokeTool` only with the `testing` preference. A page's own
`document.modelContext` object on a `data:` URL offered nothing either way. Chromium kept only
the last `--enable-features`: `chrome://version/?show-variations-cmd` read `WebMCP` alone with
`--enable-features=WebMCP`, and Playwright's two features beside it with the merged spelling.
Chromium's `registerTool` did not list a name with a space or a `!`, or a second tool of one
name in a frame, and a frame of the same origin registered its own. The arms: with the switch
absent, 9 of 12 red; spelled alone, both `WebMcpLaunchTests` arms red, each naming Playwright's
two features as dropped; as fixed, 26 of 26 green with `ConfigRoundTripTests`,
`PasswordPromptTests` and `HeadedUserAgentTests`.

## What was cut, and what is not here

`originals.sha256` names every file cut and the digest of each original: the test host's
log lines out of the five arm logs, and terminal escape sequences out of `rows/row140.log`.
Every other file is whole.

**Not here, because it measured nothing:** a first pair of safe-mode and profiler runs
read the console host node starts beside itself, the first child the rigs found; the
runs kept are the second pair, which take the child whose image is under the browsers
root. And a first green run of the arm ran on the planted builds, because the copies the
edit was undone from kept their old write times and the builds skipped the files; the
files were stamped and the run taken again, `profiler/arm-green-stage.log`.

**The registry.** Firefox 159 wrote five values naming the new `firefox-1573` path into
`HKCU\Software\Mozilla\Firefox\Launcher` before each of the three cleans, and each clean
removed those five and nothing else.
