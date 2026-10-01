<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

I couldn't write `.work/zoomout/c/REPORT.md`: the harness refuses report files from sub-agents and said to put the content in this reply, so the full report is below. The four rigs and their evidence are under `C:\Source\SixFive7\BrowserAI\.work\zoomout\c\`, and the root can save this text as REPORT.md.

# Track C: Playwright's dashboard inside BrowserAI's web interface

Researched 2026-09-25, 03:40Z to 04:15Z, on Windows 11 Pro 10.0.26200. Everything under test is what the payload ships: `@playwright/mcp` 0.0.82, `playwright-core` 1.64.0-alpha-1789764292000 and node v24.21.0, read from the payload's own package files this session. The browsers were Playwright's `chromium_headless_shell-1246` from `%LOCALAPPDATA%\ms-playwright`, never BrowserAI's install or data root. I edited no tracked file and wrote only under `.work/zoomout/c/`.

## 1. Answer

Exposing the dashboard would take little code to start and a lot to live with, and the costs fall on what BrowserAI is for.

- **It is not a viewer.** Opening the page connects to every live browser in the registry. Before anyone clicks, it runs JavaScript in every listed page and fetches that page's favicon from the site. One click attaches, and from then on the page can:
  - type, click and navigate;
  - open and close tabs;
  - close the whole browser;
  - pause the browser's Playwright debugger.
- **None of it passes through BrowserAI.** It talks straight to the browser's own pipe, so the lock, the required `why` and the session record never see it. The session's own Playwright trace doesn't record it as an action either (measured).
- **Two effects hurt an agent without any error:**
  - A pause left behind when the dashboard tab goes away parks the agent's next tool call indefinitely. I measured this through a real `@playwright/mcp` child.
  - Closing a session's browser from the dashboard drops the agent's tabs. The next call answers with a fresh `about:blank` (measured).
- **It can't be contained from outside:**
  - Its port has no authentication, because `GET /` hands out the websocket key to anyone.
  - It is a per-user singleton shared with every other Playwright dashboard on the machine.
  - The only switches that limit it to BrowserAI's browsers are two upstream test variables, `PWTEST_SERVER_REGISTRY` and `PWTEST_SOCKETS_DIR`.
- **"Reload hangs" is only half right.** A plain reload worked 20 of 20 times in headless Chromium. The real defect: when any viewer disconnects, every other open viewer stops receiving updates. It is still on upstream `main`.

**Recommendation:**
- Don't host or proxy the dashboard.
- Build BrowserAI's own sessions view from what it already knows: each server's `describe` and the session directories.
- Link each session to its artifacts and to Playwright's **trace viewer**. The trace viewer is read-only and only serves files from the trace's own folder (measured).
- Add a live look at headless sessions only if the maintainer wants one, and only through the process that holds the session, so it gets logged with a `why`.
- Everything the dashboard can do is already possible for any same-user process, because `@playwright/mcp` binds every browser. That is a separate open question (Q-C2).

## 2. Method and safety

I wrote four rigs in `.work/zoomout/c/rig/`:
- All are headless.
- All point `PWTEST_SERVER_REGISTRY` and `PWTEST_SOCKETS_DIR` into `.work/zoomout/c`, and each refuses to run if either variable is missing.
- All set `TEMP`/`TMP` to `.work/zoomout/c/tmp`.
- The dashboard always ran with `--port=0 --host=127.0.0.1`, so it never opened its app window.
- I never called its `reveal` method, because it opens Explorer (F7).
- Every browser the dashboard listed or touched was launched by a rig.

| Rig | What it drives | Log / results |
|---|---|---|
| `dashboard-probe.cjs` | Its own headless persistent Chromium, bound the way `@playwright/mcp` binds; the dashboard; a raw websocket client; a headless viewer of the real UI | `evidence/probe.log`, `evidence/probe-results.json` |
| `mcp-pause-probe.cjs` | The payload's own `@playwright/mcp/cli.js` over stdio JSON-RPC, which is the child BrowserAI forwards to; the dashboard; the real UI reloaded 10 times | `evidence/probe2.log`, `evidence/probe2-results.json` |
| `singleton-probe.cjs` | Two `--port` dashboards and one `--kill` | `evidence/probe3.log` |
| `trace-viewer-probe.cjs` | `playwright-core/cli.js show-trace --host 127.0.0.1 --port 0` on the trace probe 1 wrote, with `CLAUDECODE=1` so upstream doesn't open a browser; a headless viewer | `evidence/probe4.log`, `evidence/trace-viewer.png` |

**Real registry:**
- At 03:52:35Z, before any rig ran, `%LOCALAPPDATA%\ms-playwright\b` held **8** names (`evidence/real-registry-before.txt`).
- After each rig it held the same 8, with none missing and none added. The final check at 04:15:11Z found 8, 0 missing.
- No `pw-*` named pipe existed before or after. As a positive control, the same enumeration listed 1,195 pipes.
- No rig process was left alive. The same query, run while probe 2 was going, found the rig's own node and headless shell.

## 3. The session design, checked against ARCHITECTURE.md

The brief's summary is correct except for one detail.

- The directory is the session ("The session directory is the identity, the handle and the lock", ARCHITECTURE.md:39).
- There is one server process per client connection, holding one child per open session, each in its own job (:23-37).
- Each session has:
  - `browserai.lock`, held open for the session's life; holding it is the whole of ownership;
  - `browserai.data`, which logs every forwarded **and** refused call;
  - a required `why` on every call that names a session (:315-336, :398-429, :503-523).
- **Correction: the live marker belongs to the server, not the session.** It is `<pid>-<guid>.live` in the live-instance census, and since 2026-09-24 it also names that server's pipe (:1212-1223).
- The stray sweep runs machine-wide over browsers started from BrowserAI's provisioned binaries, and only acts where it can take the attributed directory's lock (:1028-1039).
- Only `browserai_destroy` deletes a session (:483-486).
- A profile can hold real logins. Same-user file access defeats any tool-level guard; this was measured 2026-08-18, and the non-goal is recorded at DECISIONS.md:263-290.

## 4. What the dashboard is, from the bundled code

Line numbers refer to `payload/mcp/node_modules/playwright-core/lib/coreBundle.js` (77,300 lines). I compared the relevant files with upstream `main` at `48844a5` on 2026-09-25.

**Starting:**
- `lib/entry/dashboardApp.js` is three lines calling `tools.openDashboardApp()` (:77003). Its arguments are `--port`, `--host`, `--sessionName`, `--workspaceDir`, `--pageId`, `--annotate` and `--kill`.
- Before anything else, it takes a **per-user singleton** on the pipe `\\.\pipe\pw-<sha1(USERNAME)[0..8]>-dashboard-app` (:8556). Only `PWTEST_SOCKETS_DIR` moves that name.
- If the name is taken, it forwards its arguments to the owner, prints `Dashboard is running pid=N` and exits. This applies to `--port` too: upstream PR #41466 (2026-06-26) routed `--port` through the singleton.
- With `--port` it serves HTTP and prints `Listening on <url>`.
- Without `--port` it calls `launchApp2`, a **headed** Chromium at 100,100, 1280x800 (:76902). It only runs headless when the test variable `PWTEST_DASHBOARD_APP_BIND_TITLE` is set.
- It exits when its stdin closes.
- The entry file is not in `playwright-core`'s `exports` map. The public routes are `playwright-cli show` and `openDashboardForContext`, and the latter always opens a headed window.

**What it lists:**
- `RegistrySessionProvider` (:76637) watches the registry: `PWTEST_SERVER_REGISTRY` if set, otherwise `%LOCALAPPDATA%\ms-playwright\b`.
- It calls `serverRegistry.list()` on every registry add, remove or change, and again whenever a viewer connects.
- `list()` probes each descriptor's pipe and unlinks every descriptor it can't reach.
- For each reachable descriptor, it `require()`s that descriptor's own `playwrightLib` path and connects a full Playwright client (:74750).
- On every tab change, for every page of every listed browser, it runs `page.title()` and a `page.evaluate` that fetches the page's favicon (`faviconUrl`, :75954).

**What it can do.** These are websocket methods on `DashboardConnection` (:75984). `dispatch` (:76043) will call any method name the client sends, including `onclose`.

| Capability | Methods |
|---|---|
| View | `selectTab` (starts a 1280x800 JPEG screencast); `setVisible`; `screenshot` (returns a PNG **plus an ARIA snapshot**) |
| Input | `mousemove`, `mousedown`, `mouseup`, `wheel`, `keydown`, `keyup` |
| Navigate | `navigate(url)`, `back`, `forward`, `reload` |
| Tabs | `newTab`, `closeTab` |
| Close | `closeSession` (closes every context, then the browser) |
| Debugger | `debuggerPause`, `debuggerResume`, `debuggerStep` on the context's server-side `Debugger` |
| Record | `startRecording`, `stopRecording`, `readStream` (writes a webm into `%TEMP%\playwright-recordings-*`) |
| OS | `reveal(path)` runs `explorer /select,<path>` (:76128). The UI never calls it; any client can |

It doesn't trace and has no cookie or storage API. All its actions are flagged `internal: true`, which keeps them out of traces and out of the debugger's call list.

**What it serves:**
- Static files from `lib/vite/dashboard` (916 KB, already in the payload), plus one websocket at a random guid.
- **The guid is the only authentication, and `GET /` redirects to `/index.html?ws=<guid>`, so it hands the guid to anyone.**
- A `Host` check blocks DNS rebinding when the server is bound to loopback.
- The websocket accepts any `Origin`, and pages carry no `X-Frame-Options` or CSP.
- It binds to `localhost` by default; `--host 0.0.0.0` switches the Host check off.

**On reload:**
- The page opens a new websocket, which gets a new connection over the **one shared provider**.
- When the old socket closes, its `onclose` (:76023) calls `provider.dispose()`. That runs `removeAllListeners()` (:76674), disconnects every tracker and stops the watcher, for **every** connection.
- Whether the reloaded tab survives depends on whether the old close lands before the new connection's first update.
- `registrySessionProvider.ts` on `main` is unchanged.

## 5. The five named claims, verified

| Claim | Verdict | Evidence | Severity |
|---|---|---|---|
| Reaps the machine-wide registry when it lists | **True, and more often than stated.** It lists on every registry change and every viewer connect, and also **connects to every live browser** it lists | Probe 1 E4: 5 of 5 planted dead descriptors were unlinked at the first listing, the live one survived, and the first list arrived 67 ms after connect | **Low** as reaping: T7 already runs the same `list()` at every close. **Medium** because of a Windows false positive upstream has diagnosed but not fixed (below). **High** if hosted over the default registry: it would list, attach to, run script in and reap other tools' and agents' browsers |
| Can take over live browsers | **True**, on a browser it didn't launch | Probe 1: typed "hi" into the owner page's input (E7), navigated it to `/other` (E7), opened a tab (E9), closed the browser in 21 ms (E13). Probe 2 did the same through a real `@playwright/mcp` child (E22, E23) | **High** (F1, F3) |
| Bypasses session locks | **True, and it also bypasses the `why` record and the Playwright trace** | Probe 1's trace (E8) holds only the owner's 3 actions. The dashboard's typing, navigation, screenshot and new tab show up only as side effects: a `/other` request, a page event, and five 404 console errors (`evidence/trace-viewer.png`) | **High** for the product's promise. It is not a new door, because `@playwright/mcp` binds every browser unconditionally (`browserFactory.ts` on `main`) |
| Loads endlessly on reload | **Half right.** The mechanism is real, but a single-tab reload did not reproduce | Probe 1 E12: 10 of 10 reloads listed the session in about 210 ms. Probe 2 E24: 10 of 10 listed in about 118 ms **and** picked up a later registry change in about 108 ms. What does reproduce, 2 of 2 times: **a second viewer stops receiving updates when another viewer closes**. Raw websocket E11; real UI tabs E12, where the screenshot is byte-identical to the first load, SHA-256 `8e76b12a...` | **Medium** for a product page |
| Would expose live screencasts on a local port | **True** | Probe 1 E3/E4: `GET /` returned 302 with the guid; a foreign `Origin` was accepted; no frame or CSP headers; Host `evil.example` and `attacker.localtest.me` got 403; path traversal got 404. E6: first frame 25 ms after attach, 7 frames in 3 s on a static page, JPEG 7,093 B | **Medium to high as a product default.** A loopback TCP port is reachable by every local user. The browser's own pipe is effectively per-user under Windows' default DACL (per Microsoft Learn; libuv's creation call not checked). Hosting the dashboard therefore **crosses the Windows user boundary**, which the charter's same-user argument does not cover |

The Windows false positive: upstream PR #42128 (Yury Semikhatsky, 2026-08-05) says Windows pipe servers pre-post only 4 accept instances, so concurrent probes can fail transiently and unlink a **live** descriptor. The PR closed unmerged on 2026-08-13.

## 6. New findings

- **F1: a pause outlives the dashboard, and the agent's next call parks. High.**
  - `debuggerPause` arms `_pauseAt.next` on the context's server-side `Debugger` (:13271). A dashboard that closes, reloads or crashes never resumes it.
  - Probe 1 E10: after the dashboard's socket closed, the owner's `page.evaluate` did not return in 12 s; an owner-side `resume()` released it.
  - Probe 2 E22, through the payload's `@playwright/mcp`: `browser_navigate` had no answer at 15 s. A concurrent `browser_snapshot` answered with a `### Paused` section naming the parked navigation. A concurrent `browser_resume` released it about 50 ms later, at +15,053 ms.
  - BrowserAI's forward path has no timeout by design (ARCHITECTURE.md:212-215).
  - This is the same kind of liveness failure that got `browser_annotate` and `browser_webmcp_call` denied.
- **F2: `browser_resume` also parks. Low today, medium if a dashboard is exposed.**
  - It is `allow` in `tool-verdicts.json`.
  - Upstream deliberately makes it wait for "the next pause or context closure" (issue #41304, fixed 2026-06-16 by PR #41293). A BrowserAI context only closes when the session ends.
  - Measured in probe 2 E22: no answer within 20 s after it had released the navigation.
  - Today only something outside BrowserAI can arm a pause, so the in-band way out of F1 is itself unbounded.
- **F3: a close from the dashboard loses state without an error. High.**
  - Probe 2 E23: after `closeSession`, the child's next `browser_tabs` answered `0: (current) [](about:blank)`. The agent's tab was gone, and a second descriptor showed a fresh browser on the same profile.
  - Logins in the profile survive. Open pages and in-page state don't.
  - BrowserAI's record shows nothing.
- **F4: listing alone changes what the agent sees. Medium.**
  - Probe 1 E5: 0 favicon requests before the dashboard opened, **14** after about 2.6 s of listing with nothing selected, and **+2** per owner navigation.
  - On a page with no icon link the request goes to `/favicon.ico`.
  - Probe 2 E21: the MCP child's `browser_console_messages` went from **0 to 3** errors reading `Failed to load resource: ... 404 @ .../favicon.ico`, which the agent reads as the page's own errors.
  - The requests also reach the site under test, and a HAR capture records them.
- **F5: dashboard actions are missing from the trace. Medium.** `createBeforeActionTraceEvent` drops internal calls (:26166). Measured in E8.
- **F6: the singleton collides with everyone. Medium.**
  - Probe 3: a second `--port` dashboard printed `Dashboard is running pid=8184` and exited 0 in about 0.4 s **without serving its port**.
  - A `--kill` then stopped the first one, and its port refused connections.
  - So a BrowserAI-hosted dashboard would never start if the user already runs one. Any `playwright-cli show --kill` stops it, and so does `playwright-cli kill-all`, which matches `*dashboardApp.js*` in command lines machine-wide (:73003).
  - The fix is a test hook, `PWTEST_SOCKETS_DIR`.
- **F7: focus. Medium.** Read from code; nothing here was run, because each would put a window on screen.
  - `--port` mode opens no window of its own.
  - `reveal` opens Explorer at a path the client chooses, a UNC path included. Any local process can call it.
  - A `--port` instance that loses the singleton to a user's app-mode dashboard makes that window come to the front (:77080).
  - `newTab` on a headed session may open a foreground window.
- **F8: each listing carries every descriptor in full. Low.** That includes `userDataDir`, launch options, pipe names and `workspaceDir`, plus the user's `homeDir` in `clientInfo`. Measured in E4.
- **F9: it runs other tools' code. Low to medium.** It `require()`s whatever `playwrightLib` each descriptor names (:74751). Over the default registry, that is code from other tools' installs. Read from code.
- **F10: every BrowserAI session has the same name to Playwright's tools.**
  - The child's MCP client name is `BrowserAI` (`src/BrowserAI/Proxy/ChildConnection.cs:222`), and it becomes the browser's registry title (measured through the MCP child in probe 2 E20).
  - The dashboard shows N entries all titled `BrowserAI`.
  - `playwright-cli attach BrowserAI` and `@playwright/mcp --endpoint BrowserAI` attach to whichever one `serverRegistry.find()` returns first (read from code, not run).
  - This is true today, whatever is decided here.
- **F11: updates.** A dashboard started from `current\payload\node\node.exe` is a process running from the install. Phase 2's apply loop waits until none are left, and Velopack force-stops them. The coordinator would have to stop it first.
- **F12: the one real loss.** For headless sessions the screencast works (E6). The dashboard is the only ready-made way to watch a headless session live.

## 7. Each invariant, and whether BrowserAI can contain the conflict

| Invariant | Conflict | Severity | Containable? |
|---|---|---|---|
| Lock and ownership | Acts through the browser's pipe; the lock is never consulted | High | **No.** The pipe is upstream's and always bound. A proxy only protects people who use BrowserAI's page |
| `why` record | No row is written, and the trace drops it too (F5) | High | **No** for dashboard actions. A BrowserAI-built view can log its own reads (3b) |
| Stray sweep | None in `--port` mode (it runs node, not a provisioned browser). App mode starts a headed provisioned Chromium outside any session, which the sweep would report loudly on every pass (per the 2026-08-18 annotate measurement) | Low / Medium | Yes: `--port` only, and the coordinator owns its lifetime |
| Profiles with logins | Can drive a logged-in profile to any site and read it by screenshot and ARIA snapshot (E7). New across users; not new within one user | Medium to high | Partly: a token on BrowserAI's own page, but the dashboard's own port stays open |
| Headless vs headed | Headless: the screencast is a real gain (F12). Headed: remote control; `newTab` might take focus (F7) | Low / Medium | Yes for viewing; F7 unverified |
| Nothing takes focus | `reveal`, losing the singleton to an app-mode dashboard, possibly `newTab` (F7) | Medium | Partly: a proxy can drop `reveal`; the singleton needs `PWTEST_SOCKETS_DIR` |
| Claude Code and Codex at once | One list across all servers and install roots, all titled `BrowserAI`, owning client unknown. A human in the dashboard races the agent on the same page, and the agent's element refs go stale. A pause parks whichever client calls next | Medium | Partly: only the server's `describe` knows which client owns a session |

**The five proposed containments, weighed:**
1. **Read-only view.** Upstream offers no switch for it. A websocket-filtering proxy could allow only `selectTab` and `setVisible`, but it can't stop the side effects listing already causes (F4) or close the dashboard's own port.
2. **Filtering to BrowserAI's own sessions.** Only possible by setting `PWTEST_SERVER_REGISTRY` for every child, the reaper and the dashboard. That:
   - puts a test hook into the product;
   - reverses T7's record that nothing in the product ever sets it;
   - hides BrowserAI's sessions from a user's own `playwright-cli show`.

   It would also stop BrowserAI reaping other tools' descriptors.
3. **Proxying through the coordinator with its own authentication.** Covers BrowserAI's page. It can't cover the dashboard's own port, which hands out its key to anyone. A proxy that fans one upstream socket out to many viewers would also hide the multi-viewer defect. That is several hundred lines of websocket code for a view that still touches every page.
4. **Launching with a redirected registry.** Same as 2, plus `PWTEST_SOCKETS_DIR` for F6. Both are upstream test hooks with no compatibility promise. The project already turned down relying on one for `browser_annotate`'s headless path.
5. **A view BrowserAI builds from its own records.** The only option with no bypass, no side effects on pages and no test hooks. It loses F12.

## 8. Upstream status (all read 2026-09-25)

- **Versions:**
  - `@playwright/mcp`: npm `latest` is **0.0.82**, published 2026-09-18T23:38:28Z; the GitHub release is 2026-09-18T23:35:04Z. The payload is on the newest release.
  - `playwright-core`: npm `latest` is **1.63.0**, `next` is 1.64.0-alpha-2026-09-24, and `@playwright/mcp@latest` pins exactly 1.64.0-alpha-1789764292000 (`npm view`).
  - Newest Playwright GitHub release: v1.63.0, 2026-09-04.
- **Status:**
  - The dashboard and `Browser.bind` shipped in **Playwright 1.59** (v1.59.0, 2026-04-01). The release notes say: "Run `playwright-cli show` to open the Dashboard that lists all the bound browsers ... and allows interacting with them."
  - The docs present it with no experimental label: getting-started-cli.md on `main`, and the `playwright-cli` README ("see and control all running browser sessions ... take over mouse and keyboard input").
  - Its websocket protocol and entry file are internal.
  - The 1.60 to 1.63 release notes don't mention it.
- **Recent changes:**
  - The server source last changed 2026-07-23 (#41711, a debugger actions panel). At least 40 commits touched it between 2026-04-18 and 2026-07-23; the list was capped at 40.
  - The pinned alpha matches `main` for the dashboard.
- **Issues and PRs:**
  - Open: #41528, "View Multiple Sessions w/show".
  - #42128: the Windows transient-probe eviction fix, closed unmerged 2026-08-13. The closing note said the fix had landed in libuv, but libuv #5224 (merged 2026-08-06) fixed a crash, not the eviction.
  - Also closed unmerged: #41895 (probe retry) and #42290 (stale tab selection).
  - I found no issue about the shared-provider disposal.
- **Direction:**
  - Upstream is widening it: every MCP and CLI browser is bound "so you can see what your agents are doing".
  - `PLAYWRIGHT_DASHBOARD=1` adds test browsers.
  - The MCP README steers coding agents toward the CLI.
  - Nothing points to a read-only mode, an opt-out from binding, or authentication.

## 9. Directions

Line counts are `wc -l` on this tree; "est." marks my estimates.

**1. Expose as is.**
- Adds: a live view of every session, plus control and pause.
- Bypasses the lock and `why` record: yes.
- Liveness hazards F1 and F2 are one click away inside the product.
- Collides with other dashboards (F6).
- Cost, est.:
  - 150-250 lines to host and stop it, next to `src/BrowserAI.App/Coordinator.cs` (326);
  - 300-500 lines of tests;
  - about 100 lines for a golden snapshot of the websocket method list;
  - 6-9 HAZARDS rows.
- Removes nothing.
- Testing without a window: `--port`, both `PWTEST_*` into scratch, a raw websocket plus a headless viewer, the way these rigs do it. Never call `reveal`.

**2. Contained variant.**
- Adds: the same view, read-only for BrowserAI's users and limited to BrowserAI's sessions.
- The bypass stays open through the dashboard's own port. A proxy drops `debuggerPause`, but a pause is still reachable directly.
- Relies on both `PWTEST_*` test hooks.
- Cost, est.: 1,000-1,800 lines including a websocket proxy. It changes `src/BrowserAI/Protocol/ChildEnvironment.cs` (339) and reverses T7's record.
- Removes BrowserAI's reaps of other tools' descriptors.
- Testing: as for 1, plus proxy allowlist tests.

**3. Own sessions view plus trace viewer. Recommended now.**
- Adds: each session with its client, purpose, whether it's busy, its artifacts and its traces.
- No bypass, no liveness hazard, no test hooks.
- What it builds on:
  - Rows come from `describe`, already built in `src/BrowserAI.Core/Coordination/` (ServerPipe 450, ServerPipeClient 418, ServerDescription 237, ServerPipeProtocol 254).
  - The trace viewer is already in the payload (1.7 MB).
- Cost, est.:
  - 100-200 lines for artifact listing and root-restricted file serving;
  - 60-120 lines for the trace route;
  - the call log, one of two ways: a `history` pipe verb (about 150-200 lines), or moving the read path of `Storage/SessionStore.cs` (574) and `Sqlite.cs` (755) plus the vendored SQLite into the app, which the two-binary split was made to avoid;
  - in total about 400-900 lines plus Track B's UI.
- Removes nothing.
- Testing: HTTP and JSON against the host, a headless Chromium for the page, and the trace route's in-folder 200 and out-of-folder 403, as probe 4 measured.

**3b. Add a live look through the holder. Optional.**
- Adds: a current picture of a headless session.
- No bypass: the process holding the session takes the screenshot and logs a `why`.
- The screenshot call can itself be paused, so a look needs a time bound.
- Cost, est.: 200-300 more lines.
  - a `peek` pipe verb;
  - BrowserAI making a call to its own child for the first time (`BrowserProxy.cs` is 1,916 lines).
- Testing: the published binary with a real headless child, and a planted pause to prove a look can't hang.

**4. Don't expose; document upstream's own dashboard. Do this in any case.**
- Adds nothing inside the product; the pre-existing bind exposure stays as it is.
- Cost: 20-60 lines of docs, plus HAZARDS rows for the bind (Q-C2) and F2.
- Nothing to test.

**5. Open upstream's app for one session, on request.**
- Adds: upstream's full dashboard, one click away.
- Bypasses the lock and `why` when used, and brings the liveness hazards back.
- In app mode it starts a provisioned Chromium the sweep can't attribute.
- Cost, est.: 60-120 lines plus a private-desktop test.
- Testing: only on the suite's private-desktop host (the WindowWatch/Q279 rig).

**Recommendation:** do 3 now as phase 4's content, and 4's documentation whatever else is chosen. Add 3b only if watching headless sessions matters. Don't build 1 or 2. Keep 5 at most as a later escape hatch.

## 10. Risks, whichever way it goes

- **Pre-existing:** every BrowserAI browser is bound, discoverable and attachable by any same-user tool today (F10). A user's own `playwright-cli show` already has all the hazards above against BrowserAI's sessions.
- **Upstream churn:** at least 40 commits to the dashboard between April and July. Its protocol and entry file are internal, and the four golden snapshots wouldn't catch a change in it.
- **T7 plus the dashboard:** both list the registry, and on Windows concurrent probing can unlink live descriptors (#42128). BrowserAI doesn't read the registry, so the cost is a session vanishing from Playwright's own lists.
- **3b changes doctrine:** BrowserAI would originate calls to its own child ("nothing between the two servers except the session system and the reason system"), and every look adds a log row.

## 11. What I couldn't verify

- **A headed reload in a desktop browser, which is what the maintainer did.** No windows were allowed; headless reloads worked 20 of 20.
- **`reveal`, losing the singleton to an app-mode dashboard, and `newTab` on a headed session.** Each would put a window on screen.
- **Firefox sessions** weren't run.
- **The actual pipe DACL.** I took the default from Microsoft Learn and didn't read libuv's creation call.
- **Cross-user reach** is reasoned, not tested; this machine has one user.
- **Codex and Claude Code** weren't driven; that part is analysis.
- **The eviction risk above eight concurrent listers** (T7's open item) wasn't measured.
- **The original "loads endlessly" observation:** its probe isn't in the tree (see section 12).

## 12. Tangential: records that need a correction by addition

- **kb/playwright/tools-and-artifacts.md:817-823** says the dashboard "cannot be reloaded". Measured here: 20 of 20 headless reloads listed and stayed live. The failure is a second viewer after another viewer leaves. The mechanism the kb describes is right; its trigger is incomplete.
- **`docs/evidence/2026-09-23-server-registry/` holds no reload evidence:**
  - `dashboard.png` and `dashboard-after-reload.png` are byte-identical (SHA-256 `d0f35108...`).
  - `docs/probes/2026-09-24-playwright-dashboard/dashboard-shot.cjs` never calls `reload()`; the word appears only in its output file name.
  - The README lists `dashboard-demo.log` and `dashboard-demo-relaunch.log`, and neither file exists.
- **`browser_resume` (`allow`) is unbounded** in long-lived sessions (F2), and HAZARDS doesn't record it.
- **T7's accepted risk has an upstream diagnosis** (#42128) that DECISIONS and the kb don't record.

## 13. Open questions for the maintainer

**Q-C1. Should BrowserAI's web interface expose Playwright's dashboard?**
- **Primer:** the dashboard is upstream's live view and control panel for every bound browser, and it already ships in the payload. Hosting it is easy. It can't be made read-only or authenticated from outside, it acts without BrowserAI's lock or `why`, and two of its effects hang or silently reset an agent's work.
- **Directions:** 1 as is; 2 contained; 3 own view plus trace viewer; 3b plus a look through the holder; 4 don't expose, document upstream's; 5 open upstream's app on request.
- **Recommendation:** **3**, with 4's documentation. Add 3b only if a live view of headless sessions matters.

**Q-C2. Should BrowserAI sessions stay visible to Playwright's own tools?**
- **Primer:** `@playwright/mcp` binds every browser, and `main` has no opt-out. Every session is titled `BrowserAI` and its pipe is listed in the machine-wide registry, so `playwright-cli show`, `attach BrowserAI` and `--endpoint BrowserAI` all reach it today. Hostile callers are out of scope by charter, but honest mistakes are in scope: a human pausing a session or closing its browser.
- **Directions:**
  - (a) Leave it and add a HAZARDS row.
  - (b) A private registry through `PWTEST_SERVER_REGISTRY`. That is a test hook, and it also hides sessions from users who want to see them.
  - (c) An upstream ask for a supported way to skip binding or set the registry path. I'd draft it and show it to him before anything is posted.
  - (d) Send a per-session MCP client name (one line at `ChildConnection.cs:222`), so each session gets a distinct title and `attach BrowserAI` stops being ambiguous.
- **Recommendation:** (a) now, (c) drafted for his go-ahead, and (d) if he wants upstream's dashboard to be usable against BrowserAI's sessions.

**Q-C3. What should `browser_resume` be?**
- **Primer:** it is `allow`. Upstream makes it wait for the next pause or the context's close, and a BrowserAI context only closes when the session ends. Measured: no answer in 20 s after it released the parked call.
- **Directions:**
  - (a) Keep `allow` and add a HAZARDS row.
  - (b) `deny` it on liveness grounds like `browser_annotate`. A paused session would then have no in-band way out except destroy.
  - (c) Include it in the upstream ask.
- **Recommendation:** (a) plus (c). Nothing inside BrowserAI can arm a pause today; revisit if any dashboard is exposed.

**Q-C4. Report the multi-viewer defect upstream?**
- **Primer:** `main` still tears down one shared provider when any viewer leaves.
- **Directions:** draft an issue using this rig for his go-ahead, or leave it.
- **Recommendation:** only if he picks direction 1, 2 or 5. Otherwise it's upstream's defect, not BrowserAI's problem.

## 14. Sources

| Source | Read | Version |
|---|---|---|
| `payload/mcp/node_modules/playwright-core/lib/{coreBundle.js,serverRegistry.js}`, `lib/vite/dashboard/*` | 2026-09-25 | 1.64.0-alpha-1789764292000 |
| `payload/mcp/node_modules/@playwright/mcp/{cli.js,README.md,package.json}` | 2026-09-25 | 0.0.82 |
| github.com/microsoft/playwright, commits on `packages/playwright-core/src/tools/dashboard` and `packages/dashboard` | 2026-09-25 | main |
| github.com/microsoft/playwright/blob/main/packages/playwright-core/src/tools/dashboard/registrySessionProvider.ts | 2026-09-25 | main 48844a5 |
| github.com/microsoft/playwright/blob/main/packages/playwright-core/src/tools/mcp/browserFactory.ts | 2026-09-25 | main 48844a5 |
| raw.githubusercontent.com/microsoft/playwright/main/docs/src/getting-started-cli.md | 2026-09-25 | main |
| raw.githubusercontent.com/microsoft/playwright/main/docs/src/release-notes-js.md (1.59 section) | 2026-09-25 | main |
| github.com/microsoft/playwright/releases; releases/tag/v1.63.0 | 2026-09-25 | v1.59.0 2026-04-01, v1.63.0 2026-09-04 |
| github.com/microsoft/playwright-mcp/releases | 2026-09-25 | v0.0.82 2026-09-18 |
| `npm view @playwright/mcp` (dist-tags, time, dependencies); `npm view playwright-core dist-tags` | 2026-09-25 | registry |
| raw.githubusercontent.com/microsoft/playwright-cli/main/README.md | 2026-09-25 | main |
| github.com/microsoft/playwright/issues/41528 | 2026-09-25 | open |
| github.com/microsoft/playwright/pull/42128 and its comments | 2026-09-25 | closed unmerged 2026-08-13 |
| github.com/microsoft/playwright/pull/41895, pull/42290 | 2026-09-25 | closed unmerged |
| github.com/microsoft/playwright/issues/41304 (fixed by PR #41293) | 2026-09-25 | closed 2026-06-16 |
| github.com/libuv/libuv/pull/5224 | 2026-09-25 | merged 2026-08-06 |
| learn.microsoft.com/windows/win32/ipc/named-pipe-security-and-access-rights | 2026-09-25 | n/a |
| ARCHITECTURE.md, DECISIONS.md, HAZARDS.md, kb/playwright/tools-and-artifacts.md, docs/ledger/2026-09-23 and 2026-09-24 session files, `src/**` via `wc -l` | 2026-09-25 | branch `next`, a81bad8 |

## 15. Evidence (`C:\Source\SixFive7\BrowserAI\.work\zoomout\c\`)

- `rig/`: the four probes.
- `evidence/`:
  - `probe.log`, `probe-results.json`
  - `owner-descriptor.json`, `owner-trace.zip`, `owner-trace/`
  - `screencast-frame.jpg`, `ui-first-load.png`, `ui-second-tab-after-first-closed.png`
  - `probe2.log`, `probe2-results.json`, `probe2-console-after-listing.txt`, `probe2-snapshot-while-parked.txt`
  - `probe3.log`
  - `probe4.log`, `trace-viewer.png`
  - `real-registry-*.txt`: the real registry by name, before and after each rig.
- Scratch left behind, about 11 MB and gitignored: `reg*`, `sockets*`, `tmp`, `mcp`, and the `profile-*` browser profiles.