<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

I could not write `REPORT.md`: the harness refused it with "Subagents should return findings as text, not write report files". The full report is below. The 80-row map is also saved as data in `C:\Source\SixFive7\BrowserAI\.work\zoomout\d\feature-map.tsv`. Raw measurements are in `C:\Source\SixFive7\BrowserAI\.work\zoomout\d\bench\out\` and the HN transcript is `C:\Source\SixFive7\BrowserAI\.work\zoomout\d\hn-discussion.txt`.

## 1. The answer

**No. Stagehand v4 should not replace Playwright in BrowserAI, and it should not be added as a second engine now.** The measured gains are narrow and the losses are wide.

- **It is not a replacement for what BrowserAI needs.**
  - It is an SDK for writing browser agents in code, Chromium only, with no Firefox and no bundled browser.
  - It has no published local MCP server. The only local MCP option is a three-tool server (`run`, `snapshot`, `screenshot`). The vendor calls it experimental and says it "ships ... from the repository rather than publishing it".
- **Tool coverage: of BrowserAI's 80 tools, Stagehand has 24, has 30 partly, and lacks 26.**
  - Of the 72 upstream tools: 23 has, 28 partly, 21 lacks.
  - 20 of those 21 are tools BrowserAI forwards today.
- **"2x faster" is a claim about remote browsers.** Their own README says "2x faster execution than Playwright cloud equivalent browsers". Locally the result is mixed (section 6):
  - It wins per click and per keystroke.
  - It is 4x slower to launch, 1.4x to 5.6x slower per snapshot, and takes 4 to 9 s to close.
- **"80% more token efficient" does not hold when comparing one snapshot with another.**
  - Across nine pages its snapshot was 47% smaller than BrowserAI's current snapshot (bounding boxes on), and 13% smaller than Playwright's without boxes.
  - Per page it ranged from 54% smaller to 67% larger.
  - Most of the difference is information it leaves out: link URLs and boxes.
- **Measured defects that would land on BrowserAI's session model:**
  - Every close ends Chrome by force, and the profile records `Crashed`.
  - The extension is permanently registered into every profile it touches.
  - A page `alert()` hangs the page.
  - The browser has a debugging port that any local process can use without a credential.
  - It sends telemetry to a placeholder endpoint by default.

**Recommendation: stay on Playwright and watch Stagehand with written triggers (D4). Take three cheap ideas into the current stack (D3): the settle timeout, the snapshot-boxes default, and an upstream ask for closed shadow roots.**

## 2. What HN 49756671 is

- **The post:** "We made Playwright 2x faster and 80% more token efficient".
  - Submitted 2026-09-18T16:25:23Z by `wittydeveloper`, who posts as the maker ("We built Stagehand").
  - 153 points, 40 visible comments, 9 dead or flagged, 1 deleted.
  - It links to https://github.com/browserbase/stagehand.
  - news.ycombinator.com returned HTTP 429, so I read it through the Algolia and Firebase HN APIs on 2026-09-25.
- **The project: Stagehand v4, by Browserbase, Inc.** The name is their trademark.
- **License:** MIT.
- **Repository:** created 2024-03-24. 25,368 stars, 1,735 forks, 102 open issues and 265 open PRs. `main` is at `514970e` (2026-09-24).
- **Versions:** npm `@browserbasehq/stagehand` `latest` is **4.1.0** (2026-09-09T08:11:14Z). 4.0.0 shipped 2026-08-10; `v3-latest` is 3.7.3.
- **Announcement:** the Browserbase changelog, dated 2026-08-10.

**The discussion's objections, with what I found:**
- **cl685** asked what was lost against CDP, including environments that cannot load extensions.
  - The maker's answer: it "still uses CDP but communicates from an extension".
  - Chrome 137 removed `--load-extension` from branded Chrome. Stagehand now depends on CDP `Extensions.loadUnpacked`, which the protocol marks experimental.
- **youngtaff** asked whether CDP runs over a pipe or a WebSocket. The reply is dead. Measured: a WebSocket to a TCP port on 127.0.0.1, used both by the SDK and by the extension.
- **sa46** asked "What round trip are we talking about?" for local runs. The reply is dead. My local measurements are in section 6.
- **bradleyy** asked about DevTools-level diagnostics. The reply is dead. The SDK has "no CDP escape hatch", and `page.on()` supports `"console"` only.
- **Phemist** asked for network filtering. The reply was flagged. The SDK has no network events; only the separate `browse` CLI can capture traffic.
- **squidproquo** asked how the "trimming" works. The reply is dead. Measured: an accessibility tree with layout nodes; URLs and XPaths are kept in side maps the model does not see.
- **pixelstack** said the speed-up needs the paid product.
  - The README ties 2x to cloud browsers.
  - Caching, the Model Gateway, stealth fingerprints, proxies and session replay are cloud-only.
- **shashanoid** wrote "playwright is playwright. Dead bot giveaway". On one Chrome binary, the signals a page can read were the same under both drivers.
- **throw03172019** worried about exposed API tokens. The model calls run inside the extension's service worker, so model keys live in the browser.
- **ulrikrasmussen** asked about the action cache. The maker's answer: "with a purely local browser, the cache option has no effect".
- **vishalanton** asked if a Playwright test suite would run 2x faster. The maker said "Exactly". Their own migration guide says Stagehand has no fixtures, no `expect()`, no retries, no reporter, no codegen, no trace viewer, no auto-waiting and no strict mode.
- **Six of the nine dead items are the maker's replies to the most technical questions**, so those questions stand unanswered on the public page.

## 3. What BrowserAI depends on, confirmed in the repo (`a81bad8`, branch `next`)

- **Bundle:** `payload/payload.json` has `@playwright/mcp` 0.0.82, `playwright-core` 1.64.0-alpha-1789764292000 and node v24.21.0. npm `latest` is also 0.0.82 (2026-09-18), so there is no drift.
- **Provisioned browsers:**
  - `chromium`: Chrome for Testing 154.0.8037.0, revision 1246, channel `chrome-for-testing`.
  - `firefox`: revision 1549, Firefox 156.0.
  - The shared `ffmpeg` 1011 and `winldd` 1007.
  - Headless shell and WebKit are in the manifest but never provisioned.
- **Tool surface:** `tool-verdicts.json` has 72 upstream rows (71 allow, 1 deny: `browser_annotate`) and 8 answer rows. I counted them with a script.
- **Known pain points found in the records:**
  - The Firefox safe-mode hang (TODO Q312, Q313).
  - Descriptor residue in `ms-playwright\b` (kb).
  - Provisioning: 207.3 MB and about 10.8 s.
  - Fingerprint parity (TODO T1).
  - A Google reCAPTCHA on every test arm (kb, 2026-09-24).

**House rules a replacement would break or make moot:**
- **"Never drive Playwright directly":** moot, but its purpose (never reimplement the tool layer) breaks if BrowserAI writes its own tools over the Stagehand SDK.
- **"Never hand-write a schema, never rename":** survives only by proxying the facade's 3 tools. Offering anything like today's 72 means hand-writing schemas and composing calls.
- **Golden snapshot and upstream review:** the machinery reads Playwright internals (`coreBundle`, capabilities, `skillOnly`, artifact prefixes). It would have to be rebuilt around an unpublished 3-tool package.
- **Verdicts, deny by default:** the mechanism survives; every row is re-judged.
- **`CreateNoWindow`:** Stagehand spawns Chrome and `taskkill` without `windowsHide`. Their CLI has an open issue for exactly that (#2918). BrowserAI would have to launch Chrome itself.
- **Never terminate by image name:** passes, since Stagehand kills by pid, but always forcefully.
- **Unaffected:** stdout, SQLite, SPDX, TUnit, severities, floating versions.

## 4. What Stagehand v4 is technically

- **Architecture (local):**
  - The Node SDK starts Chrome with `--remote-debugging-port` (random, 127.0.0.1), `--remote-allow-origins=*`, `--enable-unsafe-extension-debugging` and WebMCP features.
  - It installs its MV3 extension "Stagehand Runtime" through `Extensions.loadUnpacked`. The extension has permissions `debugger`, `offscreen`, `scripting` and `tabs`, host access to `<all_urls>`, and a content script in every frame.
  - The SDK attaches to the extension's service worker and speaks JSON-RPC through `Runtime.evaluate` and `Runtime.addBinding`.
  - The service worker opens its own CDP WebSocket back to the same browser and drives pages with "understudy", a CDP engine of 12,175 lines. The extension totals 23,346 lines of TypeScript.
- **Protocol:** CDP only. No WebDriver BiDi. The `Extensions` domain is experimental.
- **Browsers:** Chromium only. The docs: "Stagehand has no Firefox or WebKit support, and no bundled browser download step". On Windows it looks for installed Google Chrome, `CHROME_PATH`, or `executablePath`.
- **Windows:**
  - It worked here headless against Chrome for Testing 154. Headed mode was not tested (no windows allowed).
  - Their CI has one Windows job: 3 Go unit tests for process management.
  - Playwright runs its primary and MCP suites on `windows-latest`.
- **MCP:**
  1. The hosted `mcp.browserbase.com`: cloud, needs an API key.
  2. The self-hostable server is **archived**. Its last release is `@browserbasehq/mcp` 3.0.0 (2026-03-31), built on Stagehand ^3.0.8.
  3. The v4 facade server (`run`, `snapshot`, `screenshot`) is `"private"`, npm returns 404, and it opens a visible (headed) Chrome by default. `run` code executes inside the extension's service worker, capped at 60 s.
- **Page model:**
  - `snapshot()` returns a tree with IDs like `[0-12]`, plus `xpathMap` and `urlMap`. The facade returns only the tree, so the model never sees link targets.
  - Measured: the snapshot includes closed shadow-root content.
- **AI primitives:** `act`, `observe` and `extract` send the page tree to a second LLM from inside the extension. `agent()` was removed in v4.
- **Runtime:** TypeScript on Node >= 22.18; Python and Go SDKs speak the same protocol.
- **npm tree:** 26,565,003 bytes in 42 packages, against 18,693,512 bytes in 3 for `@playwright/mcp`.
- **How BrowserAI would talk to it**, if at all. Each option requires launching Chrome itself and handing Stagehand a TCP `cdpUrl`; there is no pipe option.
  - Vendor the private facade and proxy its 3 tools.
  - Write its own host and tools, which breaks house rules.
  - Shell out to the `browse` CLI.

## 5. The 80-tool map

**Mapped 80 of 80. Has 24, Partial 30, Lacks 26.**

| Group | Has | Partial | Lacks |
|---|--:|--:|--:|
| Upstream 72 | 23 | 28 | 21 |
| BrowserAI 8 | 1 | 2 | 5 |

- 20 of the 21 upstream "Lacks" are `allow` tools BrowserAI forwards today.
- **Has:** a first-class Stagehand API does it.
- **Partial:** only by composing calls, with reduced meaning, or only in the CLI.
- **Lacks:** not possible without writing CDP code, and the SDK exposes none.

Evidence is Stagehand at `514970e`, read 2026-09-25:
- **MIG** = `docs/v4/migrations/playwright.mdx` (line numbers).
- **PAGE, CTX, LOC, SH, WMCP** = the v4 reference pages.
- **FAC** = `integrations/core/src/facade/contract.ts`.
- **CLI** = `packages/cli/README.md`.
- **BENCH** = measured here.

| # | Tool | What Stagehand offers | Outcome | Evidence |
|--:|---|---|---|---|
| 1 | browser_close | `page.close()`, `browser.close()` | Has | PAGE, CTX |
| 2 | browser_resize | `setViewportSize(w,h)` | Has | PAGE |
| 3 | browser_get_config | nothing; the facade's `session_info` is runner-only | Lacks | FAC |
| 4 | browser_console_messages | `page.on("console")` subscription; no history, no level filter | Partial | PAGE, MIG 251 |
| 5 | browser_cookie_list | `context.cookies(urls?)` | Has | CTX |
| 6 | browser_cookie_get | `cookies()` then pick by name | Has | CTX |
| 7 | browser_cookie_set | `addCookies()` | Has | CTX |
| 8 | browser_cookie_delete | `clearCookies({name,domain,path})` | Has | CTX |
| 9 | browser_cookie_clear | `clearCookies()` | Has | CTX |
| 10 | browser_resume | no pause, no Inspector | Lacks | MIG 310 |
| 11 | browser_highlight | `highlight({durationMs})`, temporary only | Partial | LOC |
| 12 | browser_hide_highlight | no removal; expires after its duration | Partial | LOC |
| 13 | browser_annotate (deny) | no dashboard | Lacks | MIG 310 |
| 14 | browser_handle_dialog | no dialog API; measured: hangs the page | Lacks | MIG 294, BENCH |
| 15 | browser_emulate_media | no emulation API | Lacks | MIG 558, #2744 |
| 16 | browser_evaluate | `page.evaluate`; element only via selector | Has | PAGE |
| 17 | browser_file_upload | `setInputFiles()`; no file-chooser event | Has | LOC, MIG 304 |
| 18 | browser_drop | no external drop | Lacks | PAGE |
| 19 | browser_find | `browse snapshot --filter` | Partial (CLI only) | CLI |
| 20 | browser_fill_form | facade batch of fill/select; checkboxes by click | Partial | FAC |
| 21 | browser_press_key | `keyPress()` | Has | PAGE |
| 22 | browser_type | `fill()`, `type()`, then `keyPress` to submit | Has | LOC |
| 23 | browser_mouse_move_xy | `page.hover(x,y)` | Has | PAGE |
| 24 | browser_mouse_click_xy | `page.click(x,y,{button,clickCount})` | Has | PAGE |
| 25 | browser_mouse_drag_xy | `dragAndDrop(x1,y1,x2,y2)` | Has | PAGE |
| 26 | browser_mouse_down | not exposed | Lacks | PAGE, source |
| 27 | browser_mouse_up | not exposed | Lacks | PAGE, source |
| 28 | browser_mouse_wheel | `page.scroll(x,y,dx,dy)` | Has | PAGE |
| 29 | browser_navigate | `goto()`; defaults to `domcontentloaded` | Has | PAGE, MIG 230 |
| 30 | browser_navigate_back | `goBack()` | Has | PAGE |
| 31 | browser_network_requests | CLI capture only | Partial (CLI only) | MIG 251/558, CLI |
| 32 | browser_network_request | CLI capture files | Partial (CLI only) | CLI |
| 33 | browser_network_state_set | no offline mode | Lacks | source |
| 34 | browser_pdf_save | no `pdf()`; PR #2819 open | Lacks | MIG 602 |
| 35 | browser_start_recording | no codegen | Lacks | MIG 23 |
| 36 | browser_stop_recording | same | Lacks | MIG 23 |
| 37 | browser_route | no mocking; `setDomainPolicy` fails open and is bypassable | Lacks | MIG 279, #3002, #2945 |
| 38 | browser_route_list | no routes | Lacks | CTX |
| 39 | browser_unroute | no routes | Lacks | MIG 279 |
| 40 | browser_run_code_unsafe | facade `run` code: a Playwright-shaped subset | Partial | FAC, SH |
| 41 | browser_take_screenshot | `screenshot()`; elements only by clip | Has | PAGE |
| 42 | browser_snapshot | `snapshot()`: different format, no URLs, closed shadow roots included | Has | PAGE, BENCH |
| 43 | browser_click | `click({button,clickCount})`; no modifiers, no auto-wait, clicks the first match | Partial | LOC, MIG 228/452 |
| 44 | browser_drag | `centroid()` plus `dragAndDrop` | Partial | MIG 637 |
| 45 | browser_hover | `hover()` | Has | LOC |
| 46 | browser_select_option | `selectOption()` | Has | LOC |
| 47 | browser_generate_locator | an XPath from `xpathMap`; `observe()` needs a model | Partial | PAGE, SH |
| 48 | browser_storage_state | no `storageState()`; cookies only | Partial | MIG 286 |
| 49 | browser_set_storage_state | cookies plus evaluate | Partial | MIG 286 |
| 50 | browser_tabs | `pages`, `newPage`, `setActivePage`, `close` | Has | CTX |
| 51 | browser_start_tracing | no Playwright trace | Lacks | MIG 310/954 |
| 52 | browser_stop_tracing | same | Lacks | MIG 954 |
| 53 | browser_verify_element_visible | no `expect()`, no `getByRole` | Partial | MIG 255/612 |
| 54 | browser_verify_text_visible | `locator("text=").isVisible()` | Partial | LOC |
| 55 | browser_verify_list_visible | composed from reads | Partial | MIG 255 |
| 56 | browser_verify_value | `inputValue()`, `isChecked()` | Partial | LOC |
| 57 | browser_start_video | no video | Lacks | MIG 954 |
| 58 | browser_stop_video | no video | Lacks | MIG 954 |
| 59 | browser_video_chapter | no video | Lacks | MIG 954 |
| 60 | browser_video_show_actions | `browse cursor` overlay only | Partial (CLI only) | CLI |
| 61 | browser_video_hide_actions | the cursor overlay has no off switch | Lacks | CLI |
| 62 | browser_wait_for | `waitForTimeout`, `waitForSelector("text=",{state})` | Has | PAGE |
| 63-67 | browser_localstorage_list/get/set/delete/clear | `page.evaluate` only | Partial (5 rows) | PAGE |
| 68-72 | browser_sessionstorage_list/get/set/delete/clear | `page.evaluate` only | Partial (5 rows) | PAGE |
| 73 | browserai_init | a profile directory or a named CLI session; no purpose, record or lock | Partial | source, CLI |
| 74 | browserai_resume | same profile; the extension persists and every close records `Crashed` | Partial | BENCH |
| 75 | browserai_list | nothing | Lacks | CLI |
| 76 | browserai_destroy | only temporary profiles are removed | Lacks | source |
| 77 | browserai_catch_up | no session log | Lacks | SH |
| 78 | browserai_set_purpose | nothing | Lacks | none |
| 79 | browserai_page_tool | `page.tools()`, `invoke()`, `result()`, `cancel()` | Has | PAGE, WMCP |
| 80 | browserai_reinstall_browser | no provisioning | Lacks | MIG 154 |

BrowserAI's own 8 tools are engine-agnostic C# and could all be kept. `browserai_reinstall_browser` survives only if BrowserAI keeps its own Chrome for Testing download. Through the facade, only `snapshot`, `screenshot` and `run` are tools; everything else is code the agent writes.

## 6. Performance and complexity claims

### The vendor's evidence

- **Changelog (2026-08-10) and HN text:** claims only, no method given.
- **stagehand.dev homepage:** 4.7 s against 9.0 s, click 97 against 364 ms, type 291 ms against 1.5 s, tokens 7.4k against 35.7k. No method given.
- **The evals page (updated 2026-09-03):** ranks models against harnesses on Online-Mind2Web. **It does not compare Stagehand with Playwright.**
- **Remote-browser figures quoted by a third-party blog (2026-09-20):** 628 to 323 ms per click, with a 42.2 ms round trip. Its own caveat: "one run, not a benchmark". I did not find the original Browserbase post.
- **Their eval harness:** runs `@playwright/mcp@latest`, often attached over CDP to Browserbase remote sessions. The data is in a private Braintrust project.
- **Where the 7.4k tokens come from:** `extract()` sends the whole page tree to Stagehand's own LLM (`extractService.ts:140`). The agent sees only the result; the tree tokens move to a second model with its own key and bill.

### Measured here

**Setup:**
- Machine: Windows 11 Pro 10.0.26200, Ryzen 9 5950X, 128 GiB RAM, RTX 5080. The machine is shared with other agents, so timings carry noise.
- Browser: the **same Chrome for Testing 154.0.8037.0** binary for both tools, headless, 1920x1080, under `.work`.
- Playwright side: `@playwright/mcp` 0.0.82 with `playwright-core` 1.64.0-alpha-1789764292000, in BrowserAI's configuration.
- Stagehand side: `@browserbasehq/stagehand` 4.1.0.
- All hosts ran on node v26.7.0.
- Token counts use the OpenAI `o200k_base` tokenizer as a proxy; it is not Anthropic's tokenizer.

**Launch to first page** (warm profile, 5 runs, medians):

| Path | Median | Range |
|---|--:|--:|
| Playwright MCP child, spawned the way BrowserAI does | 736 ms | 688-776 |
| Stagehand in a spawned Node host | 2,960 ms | 1,095-3,343 |
| playwright-core in-process | 339 ms | |
| Stagehand in-process | 2,656 ms | |

`Stagehand.create()` is bimodal: 32-79 ms, or 1.9-2.1 s. The slow path matches its service-worker wake logic; the cause was not isolated.

**Close and profile integrity:**
- Stagehand took 3.9 to 9.0 s to close (13 runs). It never sends `Browser.close`: it runs `taskkill /T` and forces the kill after 3 s.
- **7 of 7** Stagehand profiles with a Preferences file recorded `exit_type: "Crashed"`.
- Playwright took 155-184 ms, and **6 of 6** profiles recorded `Normal`.
- Stagehand also writes its extension permanently into each profile's Secure Preferences, as an unpacked extension pointing at the npm path.

**Snapshot tokens** (medians, 6-10 runs per page):

| Page | Playwright, boxes on (BrowserAI today) | Playwright, boxes off | Stagehand | Playwright ms | Stagehand ms |
|---|--:|--:|--:|--:|--:|
| form | 820 | 460 | 482 | 6 | 12 |
| table (500 rows) | 109,675 | 61,837 | 50,325 | 275 | 403 |
| iframes | 346 | 200 | 258 | 12 | 16 |
| shadow | 129 | 70 | 117 | 5 | 24 |
| example.com | 155 | 96 | 94 | 5 | 20 |
| Wikipedia article | 24,305 | 16,812 | 14,296 | 113 | 637 |
| HN front page | 20,355 | 13,490 | 9,595 | 73 | 357 |
| GitHub repo page | 19,804 | 12,829 | 17,246 | 195 | 1,083 |
| **All nine pages** (including blank) | **175,611** | **105,804** | **92,453** | | |

- Across all nine pages, Stagehand is 47% below boxes-on and 13% below boxes-off.
- **Link URLs** are 17-25% of Playwright's snapshot on link-heavy pages; Stagehand prints none. Without them the sizes are close (HN 10,062 against 9,028).
- **Stagehand's snapshot includes closed shadow-root content that Playwright's omits.** This is the one real content gain.
- **Tool definitions:** Playwright's 72 tools cost 8,178 tokens; the facade's 3 tools cost 983 plus 209 for instructions. Claude Code defers MCP schemas, which reduces this cost; by how much was not measured.

**Per action:**
- In the libraries, Stagehand was faster for click (8.7 against 16.8 ms) and typing (7.1 against 22.3 ms), and slower for fill (10.2 against 3.5 ms).
- 30 clicks in one `experimentalBatch` call took 184 ms.
- **Through BrowserAI's configuration, `browser_click` costs 532 ms, and that is Playwright MCP's 500 ms settle timeout:** 126 ms at settle 100, 25 ms at settle 0. Every click was counted by the page itself.

**Memory:**
- Stagehand used +9% to +16% private memory per browser: 455-603 MiB against 392-541 MiB.
- It adds an extension renderer process of about 84 MiB.

**A page dialog:**
- Stagehand: a click that triggered `alert()` hung for 20 s, and every later call on that page hung until the tab navigated away.
- Playwright MCP returned in 59 ms with a modal state, and `browser_handle_dialog` accepted it.

**What a page can see:**
- Identical under both drivers: `navigator.webdriver` false, user agent, brands, WebGL, a CDP console probe, no automation globals.
- Screen metrics and languages differed. Those come from launch configuration and are headless giveaways in both.

**The debugging port:**
- Stagehand listened on 127.0.0.1:55965 with `--remote-allow-origins=*`. `curl` from an unrelated process read `/json/version` (including the debugger WebSocket URL) and `/json/list` without any credential.
- Playwright MCP uses `--remote-debugging-pipe` and has no listener.

**Telemetry:**
- Stagehand's browser requested `https://example.com/v1/traces` during a default session.
- The schema has no off switch. The docs TODO says the endpoint will become Browserbase's, with "the opt-out story" still to be settled.
- Their docs say spans cover "every operation and ... every log record", sampled at 100%.

**Download size:**
- Stagehand bundles no browser, so BrowserAI would still download the same 207.3 MB of Chrome for Testing (454.7 MB on disk here, 10.7 s).
- Firefox would be dropped.

## 7. Downsides in depth

**Maturity:**
- v4 is 6 weeks old. There have been four majors in 22 months, and v3 and v4 were each ground-up rewrites.
- Over 90 days:

| | Stagehand | Playwright |
|---|--:|--:|
| Commits | 175 | 643 |
| Authors | 17 | 62 |
| Largest single author | 74 of 175 (42%) | |
| Issues opened | 35 | 456 |
| Issues closed | 24 | 453 |
| Open PRs now | 265 | 18 |

- npm downloads for the month: Stagehand 5.1M against 364M for `playwright-core` and 23.8M for `@playwright/mcp`. v4 was only 22% of Stagehand's own downloads last week.

**Funding and license:**
- Browserbase raised a $40M Series B in 2025, $67.5M in total. Stagehand is the open-source front of a paid cloud.
- MIT, with dependencies under MIT, Apache-2.0 and BSD-2. All compatible with BrowserAI's FSL variant.
- "Stagehand" is a trademark.

**Security:**
- History:
  - 3.0.4 shipped the Shai-Hulud 2.0 worm (GHSA-mmp7-557c-74pq, critical, November 2025).
  - Releases now go out through OIDC with provenance.
  - No code advisories were found.
- Security model:
  - The cross-user TCP control port.
  - An `<all_urls>`/`debugger` extension persisted in every profile.
  - Model-written code runs inside that extension, next to the model keys.
  - Telemetry on by default.
  - The domain policy fails open and is bypassable (#3002, #2945).

**Standards risk:**
- Chromium-only CDP, with no BiDi path.
- It depends on Google continuing to allow programmatic unpacked-extension loading. `--load-extension` is already gone from branded Chrome, and `Extensions.loadUnpacked` is experimental.
- It depends on MV3 service-worker lifetime.

**Anti-bot:** no local gain.
- It sends `Runtime.enable` just like Playwright.
- Its stealth features are Browserbase cloud "fingerprint" settings.

**Existing sessions:**
- Chromium profiles would gain the extension and a crash on every close.
- Firefox sessions become unresumable.
- `captureNetwork` (HAR), `tracing` and `timezone` would be lost.
- Every prompt naming a `browser_*` tool breaks.

**What Playwright gives BrowserAI that nobody lists:**
- Auto-waiting and strict mode.
- Modal states for dialogs and file choosers.
- The settle step, so a snapshot after an action shows the page after it.
- Graceful shutdown.
- A pipe with no listening port.
- Trace viewer, codegen, video, PDF, HAR, routing and device emulation.
- Patched Firefox and WebKit builds.
- A tool registry that can be snapshotted and diffed.
- The dashboard that track C is studying.
- Windows CI for the MCP server.
- A user base about 71 times larger.

## 8. Directions, with what each removes or adds (`wc -l` at `a81bad8`)

For scale: `src` is 51,784 C# lines in 126 files, and `tests` is 88,561 lines in 181 files.

**D1. Replace Playwright.**
- Gains: in-browser batching, closed shadow roots, WebMCP `cancel()`, a smaller snapshot than today's, a 3-tool surface.
- Losses: Firefox, 20 forwarded tools, profile integrity, dialogs, the pipe transport, and a published MCP.
- Removes or rewrites about **12,127 lines in 22 `src` files (23% of `src`)**:
  - `BrowserProvisioner.cs` 2,525
  - `BrowserProxy.cs` 1,916
  - `BrowserConfiguration.cs` 1,212
  - `StraySweep.cs` 1,008
  - `SessionToolSurface.cs` 840
  - `ToolVerdicts.cs` 512
  - `RevisionPrune.cs` 439
  - `ChildConnection.cs` 400
  - `ChildProcessSession.cs` 378
  - `ChildEnvironment.cs` 339
  - `FirefoxProfile.cs` 335
  - `PageTools.cs` 304
  - `MessageWindows.cs` 301
  - `RenameWindow.cs` 280
  - `ServerRegistryReap.cs` 257
  - `ServerInstructions.cs` 225
  - `ProvisionedBrowsers.cs` 183
  - `ChildLaunch.cs` 168
  - `BrowsersManifest.cs` 165
  - `ProvisioningRemediation.cs` 128
  - `PayloadLayout.cs` 124
  - `StandardErrorClassifier.cs` 88
- **Tests:** 13,185 lines in 18 Playwright-specific files, for example `StraySweepTests` 1,854, `ModelSurfaceTests` 1,645 and `ProvisioningTests` 1,219.
- **Build and data:**
  - `upstream-snapshots.mjs` 517
  - `Build-Payload.ps1` 443
  - `Update-UpstreamSnapshots.ps1` 209
  - `UpstreamSnapshots.targets` 99
  - `upstream-snapshots/` 2,950
  - `tool-verdicts.json` 264
  - `upstream-review.json` (92,772 bytes)
  - `UPSTREAM-REVIEW.md` 62
- **Knowledge:** kb/playwright 4,022 lines and kb/chromium 466.
- **Adds:**
  - A vendored private facade: 4,893 lines of TypeScript, including a 3,460-line Playwright-compatibility runtime.
  - The 26.6 MB Stagehand tree.
  - A Chrome launcher of BrowserAI's own that hands Stagehand a TCP `cdpUrl`.
  - A new 3-tool golden snapshot.

**D2. A second engine beside Playwright.**
- Loses nothing.
- Ships every Stagehand defect behind a flag and doubles the payloads, surfaces and upstream reviews.
- Adds a second payload, a second child path through `BrowserProxy.cs` (1,916) and `SessionToolSurface.cs` (840), second verdicts and snapshots, a containment path for the TCP port, extension and forced close, and two more upstreams in `drift-check.json`.
- My estimate, not measured: +3,000 to 6,000 lines of C# and tests.

**D3. Adopt specific ideas, not code.**
- The ideas:
  - (a) Tune `timeouts.settle`.
  - (b) Turn `snapshot.boxes` off by default, with per-call `boxes:true`. Measured on the table page: 101,700 tokens become 53,862 with a per-call override.
  - (c) Draft an upstream Playwright issue about closed shadow roots, shown to you before anything is posted.
  - (d) Point models at batching through `browser_run_code_unsafe`, which BrowserAI already forwards.
- Changes:
  - `BrowserConfiguration.cs` around lines 861-871 (boxes) and 894-896 (timeouts).
  - Assertions in `ConfigRoundTripTests.cs`.
  - README line 195.
  - Possibly `ServerInstructions.cs`.
- About 20 to 100 lines added; nothing removed.
- Risks:
  - A low settle value can return a snapshot of an unfinished page.
  - Vision users would have to ask for boxes.

**D4. Stay and watch, with triggers.** Revisit when Stagehand has:
- a published local MCP;
- a graceful close on Windows;
- a pipe or authenticated transport;
- a telemetry opt-out;
- dialog, network-event and route APIs;
- Windows CI;
- Firefox or BiDi;
- a v4 line that holds about six months.

It costs one watch note (TODO or `drift-check.json`, your choice) and no code.

**D5 (found). Leave cloud and anti-bot needs to Browserbase's hosted MCP, outside BrowserAI.**
- It adds a README paragraph at most.
- It costs money, and pages and credentials leave the machine.

**Recommendation: D4 plus D3.** D1 fails on coverage, platform and profile integrity. D2 doubles the maintenance burden that started this zoom-out, for gains D3 gets without an engine. D5 is optional documentation.

## 9. Risks

- **If Stagehand is adopted in any form:**
  - Forced closes put profile data at risk.
  - Other local users can control a session browser through the TCP port.
  - Telemetry leaves the machine once the endpoint is filled in.
  - Pages with dialogs hang.
  - It depends on an experimental CDP domain and on Google's extension policy.
  - BrowserAI would vendor a private, experimental package.
  - The vendor's incentive is its cloud.
  - One maintainer dominates, and there has been a rewrite every 8 to 12 months.
- **D3:** a stale snapshot if settle is set too low; a changed default for users of the coordinate tools.
- **D4:** the closed shadow-root and batching advantages remain Stagehand's until Playwright adds them.

## 10. What I could not verify, and why

- **The cloud 2x claim and the original 628 ms post:** they need a Browserbase account, sign-ups were not allowed, and the post was not found.
- **The facade's MCP-level latency and output:** it is unpublished, and building it needs the monorepo, pnpm 11.10 and turbo. My per-action numbers are SDK-level.
- **The quality and cost of `act`, `observe` and `extract`:** they need a model API key.
- **Headed behaviour and branded Chrome:** no windows were allowed, and I did not launch the installed Chrome.
- **Real anti-bot services:** I made no third-party requests from your address. A no-automation control (`chrome --headless --dump-dom`) hung with "Sandbox cannot access executable ... Access is denied". I killed it by pid, and it produced no data.
- **Service-worker lifetime** past about 30 s, and the cause of the 2 s `create()` delay.
- **The text of the dead HN replies:** not public without a login.
- **Tokens:** counted with OpenAI's tokenizer, not Anthropic's.

## 11. Open questions for you

1. **Does Stagehand have a place in BrowserAI's next direction?**
   - *Primer:* the HN post claims speed and token wins. Measured locally, the wins are narrow, and the losses cover 21 upstream tools, Firefox, profile integrity, dialogs and a local security boundary.
   - *Directions:* a) replace; b) second engine; c) ideas only; d) watch with triggers; e) point cloud needs at Browserbase's hosted MCP.
   - *Recommendation:* c and d together.
2. **Should `snapshot.boxes` stay on by default?**
   - *Primer:* README line 195 and `BrowserConfiguration.cs:861-867` say the boxes cost is deferred because a response carries a link. That is true for actions. But `browser_snapshot` returns the snapshot inline (kb 2026-09-21, and measured here), and boxes add 45-78% on large pages. A per-call `boxes` argument overrides the config (measured).
   - *Directions:* a) keep boxes on and correct the records; b) default off, and tell the coordinate tools to ask for `boxes:true`; c) off only in guidance.
   - *Recommendation:* b. Correct the two records either way.
3. **Should the 500 ms settle timeout move?**
   - *Primer:* it is most of the 532 ms a click costs today. It exists so the snapshot after an action shows the page after it, and TODO already lists `--timeout-settle` as a candidate.
   - *Directions:* a) keep 500; b) lower it to about 100-200 ms once a set of test cases shows no stale snapshots; c) make it a per-run argument.
   - *Recommendation:* b, measured first.
4. **Closed shadow roots?**
   - *Primer:* Stagehand's snapshot shows content inside closed shadow roots and Playwright's does not (measured).
   - *Directions:* a) accept the gap; b) draft an upstream Playwright issue and show it to you before anything is posted; c) work around it in BrowserAI, which is not possible without reimplementing the snapshot, and the scope forbids that.
   - *Recommendation:* b.
5. **Anti-bot and cloud browsers?**
   - *Primer:* Stagehand's anti-bot features live in Browserbase's paid cloud, and locally its signals matched Playwright's.
   - *Directions:* a) out of scope, with T1 continuing on Playwright; b) a README note pointing at Browserbase's hosted MCP; c) integrate a cloud provider.
   - *Recommendation:* a, with b if you want it.

## 12. Tangential findings

- **BrowserAI's records about boxes contradict the product** (question 2).
- **Chrome for Testing 154 contacts Google in the background under both drivers.** Measured with a playwright-core library launch and with Stagehand, not with BrowserAI's exact MCP configuration. It requested:
  - `android.clients.google.com` c2dm/register3 and checkin
  - `accounts.google.com/ListAccounts`
  - `www.google.com/async/folae`
  - `update.googleapis.com`
  - `clients2.google.com/time`

  This matters for T1 and for privacy.
- **Headless screen metrics give the browser away** in both stacks: `outerWidth` 0 under Playwright, and an 800x600 screen with a 1920x1080 window under Stagehand.
- **The machine is as I found it:**
  - The real `%LOCALAPPDATA%\ms-playwright` was untouched: `b` had 8 entries and `.links` 6, before and after, same mtimes. My 24 descriptors went to scratch through `PWTEST_SERVER_REGISTRY` and `LOCALAPPDATA`.
  - Nothing from my runs appeared in the real TEMP.
  - No bench processes are left.
  - `git status` is clean.
  - Scratch `.work/zoomout/d` is 1.3 GB (a 455 MB browser, profiles, npm trees and the Stagehand clone at `514970e`), and can be deleted when no longer needed.

**Sources**, all read 2026-09-25:
- HN item 49756671, through the Algolia and Firebase APIs.
- github.com/browserbase/stagehand at `514970e`, and the npm registry and download API.
- browserbase.com/changelog/stagehand-v4; stagehand.dev, its `/evals` and `/blog/what-is-stagehand` pages.
- The archived browserbase/mcp-server-browserbase.
- The agmazon.com 2026-09-20 article.
- Stagehand issues #2148, #2486, #2693, #2724, #2732, #2744, #2782, #2918, #2945, #2982, #3002, and PRs #2819 and #2952.
- GHSA-mmp7-557c-74pq, OSV MAL-2025-191198, GHSA-7mvr-c777-76hp, GHSA-6fg3-hvw7-2fwq.
- The Chromium `--load-extension` PSA, and the devtools-protocol `browser_protocol.json`.
- browserbase.com/blog/series-b-and-beyond.
- microsoft/playwright commits and workflows.
- The BrowserAI files at `a81bad8` named above.

Reproduction scripts are in `C:\Source\SixFive7\BrowserAI\.work\zoomout\d\bench\` (`bench-*.mjs`, `knobs.mjs`, `port-probe*.mjs`), with results in `out\`.