<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

I've kept the full report below because the harness refused my write of `.work/zoomout/b/REPORT.md` ("Subagents should return findings as text"). The root can save it from here. Probes, logs and raw evidence are in `C:\Source\SixFive7\BrowserAI\.work\zoomout\b\`.

# Track B: a local web management interface for BrowserAI

- **Scope:** research only, 2026-09-25.
- **Machine and toolchain:** Windows 11 Pro 25H2 build 26200.9550; .NET SDK 10.0.401 with ILCompiler and runtime packs 10.0.12.
- **Repository:** `next` = `a81bad8`, tree untouched (`git status` clean).
- **What is measured:** everything without a source attached was measured on this machine today.
- **How the probes ran:** every probe bound 127.0.0.1 only and showed no window on your desktop. I stopped all of them.
- **Not started:** `BrowserAI.exe` and `BrowserAI.Server.exe`.

## 1. The answer

**Move the management interface to HTML, but host it in a WebView2 window the coordinator owns, with no port (direction C). Don't serve it to the default browser on a loopback port (direction B).**

It fixes the growth problem for the same reasons a web UI does. Tables, sections, detail views, dark mode, scaling and accessibility come with the engine, and a new page costs markup rather than Win32 plumbing. What it does not do is give BrowserAI a port that other users and web pages can reach, which is what a browser tab needs.

The deciding facts:

1. **By default, every .NET server stack bound to 127.0.0.1 answers a DNS-rebinding Host header** (measured).
   - This held for a raw socket, HttpListener with an IP prefix, the Kestrel empty builder and the Kestrel slim builder. All of them returned the status JSON to `Host: attacker.example:<port>`.
   - The only setup that refused it was http.sys with a `localhost` prefix. That setup listens on every interface of the machine: a request sent from this machine to its LAN address with `Host: localhost:<port>` got 200.
   - So a browser tab needs a Host check, a per-launch secret, an exact Origin check, a CSP and no CORS, and those have to stay correct as long as the product ships.
   - The MCP ecosystem shipped this class of hole four times in 2025 (section 4.3).
2. **WebView2 works from BrowserAI's NativeAOT app with techniques this repository already uses:** hand-declared `[GeneratedComInterface]` COM, and the loader linked statically the way SQLite is.
   - Compiled into a copy of the real `BrowserAI.exe`, it costs **+93,184 bytes (+0.85%)** and stays one file (measured).
   - A page loaded and talked back to the host in **327 to 338 ms warm and 562 ms cold** (measured on a private desktop; the window was never shown).
3. **The coordinator's lifecycle and the phase 2 and 3 designs stay as they are with a window.**
   - A second start grants the foreground and sends `show`, and the toast's "Review" opens the window at Sessions.
   - A browser tab needs three new things instead:
     - a "page still open" signal;
     - a grace timer in a coordinator that was designed with no timer;
     - a different hand-off: the process you started has to open the browser itself, because only it holds the foreground right.
4. **Q311's folder picker only works well with a window BrowserAI owns.**
   - A page in the default browser cannot give the server a path.
   - A picker the coordinator opens after a click in the browser cannot take the foreground. That follows from the documented `SetForegroundWindow` rules; I did not measure it because it would put a window on screen.

Each native alternative is honest, and each is worse on the problem you raised:

- **Trimmed task dialog (Q307 direction 4):** cheapest today. It has no dark mode, overflows at 150%, and cannot hold a multi-select list of about 22 servers without adding a Win32 list view.
- **Avalonia:** testable headless and themed. It adds about 25 MB and three native DLLs (a minimal app measured 36.4 MB in 4 files), and its build sends telemetry unless you opt out.
- **WinUI 3:** cannot be a single file, by design.

## 2. What the interface has to cover

Derived from `src/BrowserAI.App`, `TODO.md`, the 2026-09-24 ledger and `.work/STATE.md`.

| Area | Today (task dialog) | Pending |
|---|---|---|
| Status | Version, install and data roots, server command or refusal, one sentence per client, the "this folder registers" line | Q309 b; the Q303 render defects |
| Registration, user scope | Per client: Register, Repair or Register again; Unregister. Foreign and unreadable entries are shown, never touched | Nothing new |
| Registration, project scope | Per client: register in a project, remove from this project, remove from a project | **Q311**: one folder picker covering every client |
| Updates | Check for updates, install, result sentence; a hung check shows "Checking" for up to 45 minutes with no cancel | **Q308**, **Q310**, a cancel for the hung check |
| Sessions | None | **Phase 4**: every live server (22 on 2026-09-23), client, purpose, last used, browser open, Codex and recently-active warnings, close and force close |
| Entry points | Start Menu, first run, after update, a second start's `show` | **Phase 3**: the toast's "Review" |
| Later | None | **Track C**: room for the Playwright dashboard |

## 3. Measurements

### 3.1 Server stacks, standalone NativeAOT probes

- **Setup:** 20 runs after 2 warm-ups. Each probe binds 127.0.0.1:0, serves 1 KB of JSON and is started without a window.
- **"Start to listening":** from `Process.Start` to the probe's announced port.
- **Warnings:** all five published with zero trim or AOT warnings.

| Probe | Bytes | vs baseline | Start to listening p50 (p10 to p90) | First request | Steady request | Working set | Private |
|---|---:|---:|---:|---:|---:|---:|---:|
| baseline | 1,206,272 | 0 | 39.66 ms (36.95 to 67.73) | n/a | n/a | n/a | n/a |
| raw socket | 1,327,616 | +121,344 | 45.96 ms (42.44 to 77.14) | 0.704 ms | 0.286 ms | 10.4 MB | 3.7 MB |
| HttpListener (http.sys) | 2,757,120 | +1,550,848 | 48.82 ms (44.97 to 51.97) | 0.872 ms | 0.329 ms | 12.7 MB | 4.1 MB |
| Kestrel, `CreateEmptyBuilder` + middleware | 8,519,680 | +7,313,408 | 66.06 ms (57.06 to 81.64) | 1.217 ms | 0.321 ms | 22.2 MB | 24.4 MB |
| Kestrel, `CreateSlimBuilder` + minimal APIs | 12,911,104 | +11,704,832 | 82.18 ms (71.68 to 98.48) | 3.002 ms | 0.390 ms | 25.8 MB | 25.5 MB |

Server start cost over the process floor: raw socket +6 ms, HttpListener +9 ms, Kestrel empty +26 ms, Kestrel slim +43 ms.

### 3.2 The same servers compiled into a copy of the real `BrowserAI.exe`

- **Method:** I copied `src/BrowserAI.App` and `src/BrowserAI.Core` with the repository's own props files into `.work/zoomout/b/appcopy` and published it NativeAOT once per variant.
- **Why the deltas are real:** the unchanged copy is 10,914,304 B, exactly the ledger's figure for the app at `21de415`.
- **None of these binaries was run** (session rule 6).

| Variant | `BrowserAI.exe` | Delta |
|---|---:|---:|
| unchanged | 10,914,304 | 0 |
| raw socket | 10,927,104 | +12,800 (+0.12%) |
| HttpListener | 11,077,632 | +163,328 (+1.50%) |
| Kestrel, empty builder | 14,405,120 | +3,490,816 (+31.98%) |
| Kestrel, slim builder + minimal APIs | 17,633,792 | +6,719,488 (+61.57%) |
| **WebView2 host, loader linked statically** | **11,007,488** | **+93,184 (+0.85%)** |

The in-app deltas are far below the standalone ones. My inference, not measured separately, is that the app already links `System.Net.Http` (through Velopack) and the source-generated COM runtime (the task scheduler, N164).

Two build findings from the Kestrel variants:

- **Minimal APIs hit a wall in this repository.** Their generated `GeneratedRouteBuilderExtensions.g.cs` calls `Debug.Assert`, which `src/BrowserAI/BannedSymbols.txt` bans, so RS0030 fires 12 times. Under `TreatWarningsAsErrors` the build is red, and the only way out is a suppression, which CLAUDE.md forbids.
- The ASP.NET Core framework reference also triggers NU1510 on the app's explicit `Microsoft.Extensions.Logging` reference, which is an error under the same setting.

### 3.3 What each server answers by default

Sent with a raw socket, so every header is exactly as written. "200" means the status JSON came back.

| Request | Raw socket | HttpListener `127.0.0.1` prefix | HttpListener `localhost` prefix | Kestrel empty | Kestrel slim |
|---|---|---|---|---|---|
| A. `Host: 127.0.0.1:P` | 200 | 200 | **400** | 200 | 200 |
| B. `Host: localhost:P` | 200 | 200 | 200 | 200 | 200 |
| C. `Host: attacker.example:P` (rebinding) | **200** | **200** | 400 | **200** | **200** |
| D. `Host: attacker.example` | 200 | 200 | 400 | 200 | 200 |
| E. HTTP/1.0 with no Host | 200 | 200 | 400 | 200 | 200 |
| F. GET with an attacker Origin | 200 | 200 | 400 | 200 | 200 |
| G. `text/plain` POST with an attacker Origin | 405 | 405 | 400 | 200 | 405 |
| H. OPTIONS preflight from an attacker | 405 | 405 | 400 | 200 | 405 |
| I. same port on `[::1]` | refused | refused | 400 (listening) | refused | refused |
| J. same port on this machine's LAN IPv4 | refused | refused | **400 (listening)** | refused | refused |
| K. as J, with `Host: localhost:P` | not run | refused | **200** | not run | not run |

- From this non-elevated token, the `+` and `*` prefixes were refused with "Access is denied" (error 5). The IP and `localhost` prefixes registered without any URL ACL.
- A second process of the same user tried to register the longer prefix `http://127.0.0.1:P/api/`. It was refused ("Access is denied"), and the first process kept answering.
- Microsoft documents the IP-prefix behaviour: an IP host is an "IP-bound weak wildcard" that "matches any host name for the specified IP interface ... Do not rely on IP-bound weak-wildcard host specifiers to enforce security" (UrlPrefix Strings).
- `netsh http show iplisten` is empty, so http.sys listens on every address. The firewall is on for all profiles. Whether row K is reachable from another machine was not tested.

### 3.4 Browser-side protections do not replace server checks

- **Chrome 142** (shipped 2025-10-28) puts requests from public sites to loopback behind a Local Network Access permission. WebSocket and WebTransport were added in Chrome 147.
- **Firefox**, your default browser (http UserChoice is `FirefoxURL-...`), has Local Network Access in Beta and Nightly, rolling out to release users with Enhanced Tracking Protection set to Strict.
- The "0.0.0.0 day" does not affect Windows.

### 3.5 WebView2 from NativeAOT (`probes/wv2probe`)

- **How the probe works:** a WinExe that creates a window without WS_VISIBLE and never calls ShowWindow. It drives WebView2 through hand-declared `[GeneratedComInterface]` interfaces, loads content with `NavigateToString` (no port), and waits for the page's `postMessage`.
- **How it was run:** on a private desktop opened without `DESKTOP_SWITCHDESKTOP`, inside a kill-on-close job.
- **Clean-up checked afterwards:** no desktops left, and all six browser pids gone.

| Build | Exe | Loader | Launch to page ready | Processes while open | Private memory | Peak job commit |
|---|---:|---|---:|---:|---:|---:|
| window only | 1,233,408 | none | n/a | 1 | n/a | n/a |
| WebView2, loader DLL beside | 2,028,032 | 163,680 B | 561.8 ms cold; 327.0 / 329.5 / 338.0 ms warm | 7 (host + 6 `msedgewebview2`) | 143.6 to 146.4 MB | 145.0 to 148.4 MB |
| WebView2, loader linked | 2,049,536 | inside the exe | 384.5 ms (new data folder); 340.7 ms | 7 | 143.0 to 146.0 MB | 144.6 to 148.7 MB |

- **Theme:** the page reported `prefers-color-scheme: dark` = true, following this machine's `AppsUseLightTheme = 0`, with no code on our side.
- **After the host exits:** 5 WebView2 processes were still alive at that instant; the rig's job then ended them. They run from the WebView2 runtime folder, so they are outside Velopack's kill set and outside the coordinator's path scan.
- **Runtime installed here:** Evergreen 153.0.4234.48, per-machine.
- **Probe size:** 454 lines. That is 241 for the window, loop and P/Invokes, 85 for the handler classes and 128 for the interface declarations, with the slot order taken from `WebView2.h` in SDK 1.0.4191.47.

### 3.6 Native frameworks

| Option | Result |
|---|---|
| **Avalonia 12.1.3**, NativeAOT, minimal window | Exe 17,522,688 B, plus `libSkiaSharp.dll` 11,628,896, `av_libglesv2.dll` 5,394,096 and `libHarfBuzzSharp.dll` 1,816,088. Total **36,361,768 B in 4 files**, zero warnings. Published, not run. |
| WinUI 3 / Windows App SDK 2.5.1 | Not built. Microsoft Learn: `dotnet publish` "cannot produce a single-file EXE for WinUI 3 apps -- the native Windows App SDK runtime dependencies must remain as separate files". |
| WinForms, WPF | Microsoft Learn: trimming support is "disabled in the .NET SDK" for both, so no NativeAOT. |

## 4. Security model for a local web UI

### 4.1 Who can reach it

| Who | Loopback TCP port | Coordinator pipe today |
|---|---|---|
| Any web page you visit | Cross-site POSTs arrive; DNS rebinding reads and writes (measured); clickjacking if the page can be framed | none |
| Other local users | Can connect: TCP has no per-user ACL | Refused: DACL `D:P(A;;GA;;;<user SID>)` and `PIPE_REJECT_REMOTE_CLIENTS` |
| The same user's processes | Full reach; out of scope under the charter's non-goal | Same |
| Another machine | None when bound to 127.0.0.1; the http.sys `localhost` prefix is a LAN listener (row K) | Refused |

The charter's non-goal covers the agent running as the same user. It does not cover web pages or other users, and a loopback server would be the first thing in BrowserAI that either can reach.

### 4.2 What an attacker gains

- **Stop sessions.** Browsers die mid-task, because the job is `KILL_ON_JOB_CLOSE`. Codex threads lose MCP until a reload.
- **Force an update apply.** The same kill, plus a restart.
- **Unregister or re-register BrowserAI** at user scope in both clients.
- **Write registrations into arbitrary repositories, if any endpoint accepts a path.** A registration is code execution the next time a client starts, because the client runs the entry's command line. The damage stays limited only while no endpoint accepts a path or a command. That has to be a tested invariant.
- **Read** purposes, working directories, repository names and install paths.
- **With the Playwright dashboard exposed (track C):** live screencasts of, and control over, browsers that may be signed in to real accounts.

### 4.3 Incidents in tools with a local HTTP endpoint

| Tool | What happened | Fix |
|---|---|---|
| MCP Inspector, CVE-2025-49596 (CVSS 9.4) | Unauthenticated local proxy; DNS rebinding or 0.0.0.0 from any page ran commands | 0.14.1: session token, Host and Origin checks |
| Claude Code IDE extensions, CVE-2025-52882 (CVSS 8.8) | The localhost WebSocket accepted any Origin | VS Code 1.0.24, JetBrains 0.1.9 (2025-06-13) |
| MCP TypeScript SDK CVE-2025-66414, Python SDK CVE-2025-66416 | DNS rebinding protection off by default | TS 1.24.0, Python 1.23.0 |
| Ollama, CVE-2024-28224 (CVSS 8.8) | DNS rebinding reached the full API | 0.1.29 |
| Transmission, CVE-2018-5702 | After rebinding, the page read the anti-CSRF header from a 409 and got remote code execution | Host whitelist |
| Zoom (macOS), CVE-2019-13450 | Any page drove the local web server on port 19421 and turned the camera on; the server survived uninstall | Removed |

Transmission shows that a token the server hands to unauthenticated requests protects nothing. All six together show that the Host check and the token are both needed.

### 4.4 Current guidance

- **MCP specification 2025-11-25, Streamable HTTP:** servers MUST validate Origin (403 when present and invalid), SHOULD bind only to 127.0.0.1 when local, and SHOULD authenticate every connection.
- **Jupyter:**
  - token on by default since Notebook 4.3, because "access to the Jupyter notebook server means access to running arbitrary code";
  - a Host check against DNS rebinding since 5.6.0;
  - since 5.7.2, the browser is opened through a redirect file so the token is not visible on the command line.
- **Playwright's own dashboard server** (bundled `playwright-core` 1.64.0-alpha): a Host allow-list on loopback and an unguessable GUID path for its WebSocket. It opens itself in a Chromium `--app=` window.
- **Winsock:** "All server applications must set SO_EXCLUSIVEADDRUSE".

### 4.5 Controls a browser tab would need (direction B)

1. Bind 127.0.0.1 on an ephemeral port. Use `SO_EXCLUSIVEADDRUSE` for a raw socket; for HttpListener use an IP prefix plus the app's own Host check.
2. Refuse any Host except exactly `127.0.0.1:P`, including on SSE and WebSocket requests.
3. Mint a one-time code through the pipe. The pipe is the only user-only channel BrowserAI has. The code rides in the URL the starter opens, is exchanged once for a session secret, and expires after first use or 60 s.
4. Keep the secret out of cookies. Cookies are per host, not per port, so every other 127.0.0.1 origin is the same site and receives them. Hold it in `sessionStorage` and send it as a custom header, which forces a preflight and rules out CSRF. SSE carries it in the URL.
5. Check Origin exactly, port included, on every write, and add `Sec-Fetch-Site: same-origin` as a second test.
6. No CORS headers at all. CSP `default-src 'self'; frame-ancestors 'none'` with no inline script; `nosniff`; `no-referrer`; `no-store`.
7. No endpoint accepts a filesystem path or a command, held by a test that walks the route table.
8. Render model-written and repository-supplied strings with `textContent`, never `innerHTML`.

## 5. WebView2 facts

- **Availability:**
  - Preinstalled on all Windows 11 devices, and installed "to all eligible Windows 10 devices" in the December 2022 rollout.
  - It "may be missing on clean Windows 10 installs, Windows Server, or LTSC editions", and Microsoft asks apps to check at startup.
  - Edge and the WebView2 runtime keep receiving updates on Windows 10 22H2 "until at least October 2028".
  - Edge moved to a two-week major release cadence from version 152.
- **NativeAOT:**
  - The WinForms and WPF wrapper `Microsoft.Web.WebView2.Core.dll` relies on built-in COM, which NativeAOT does not support.
  - The WinUI projection became AOT-compatible from package 1.0.2798 (WebView2Feedback #4800 and #4783).
  - For a plain Win32 NativeAOT app, the working route is the one measured here: `[GeneratedComInterface]` and `[GeneratedComClass]` over our own declarations (the task scheduler's technique), plus `CreateCoreWebView2EnvironmentWithOptions`.
- **SDK version:** `Microsoft.Web.WebView2` 1.0.4191.47, published 2026-08-28 (nuget, looked up today). Its licence is BSD-3-Clause-style, so the static loader needs a notice in `THIRD-PARTY-NOTICES.txt`.
- **No port:** content comes through `NavigateToString`, `SetVirtualHostNameToFolderMapping` or `WebResourceRequested`; messages go through `PostWebMessageAsJson` and `WebMessageReceived`.
- **User data folder:**
  - The Win32 default is `<exe path>.WebView2` next to the executable. For a Velopack install that is inside `current\`, which is destroyed on every update, so it must be set under the data root.
  - The `WEBVIEW2_*` environment variables can override the folder and the arguments. That is same-user and out of scope, but worth a sentence in the design.
- **Posture:**
  - load only our own page;
  - cancel navigation to any other origin and hand it to the default browser;
  - check the message source;
  - turn off DevTools and the context menu;
  - render untrusted strings as text.

## 6. Native framework facts

- **Plain Win32:**
  - The task dialog has no documented dark mode; community projects subclass it or use undocumented `DarkMode_*` themes.
  - A multi-select Sessions list needs a Win32 list view window: `WM_NOTIFY` handling, columns, checkboxes, DPI and keyboard support.
- **Avalonia 12.1.3** (published 2026-09-22):
  - Strengths:
    - NativeAOT publishes clean;
    - `HeadlessUnitTestSession` runs windows and input without a screen and works with any test framework;
    - the Fluent theme follows the OS.
  - Costs:
    - about +25 MB by the standalone figure (not measured in-app);
    - three native DLLs, so no longer one file;
    - a floating major version under the repository's float rule;
    - **build telemetry**: `Avalonia.BuildServices` 11.3.2 runs before every compile and sends anonymous data (hashed project and machine names, a machine GUID, OS, versions) unless `AVALONIA_TELEMETRY_OPTOUT=1` is set;
    - its WebView control is in the paid Accelerate edition.
- **WinUI 3 / Windows App SDK 2.5.1** (published 2026-09-17): NativeAOT has been supported since 1.6, but deployment is a folder of files, there is no headless test platform, and it uses CsWinRT projections.

## 7. UX and lifecycle

- **Opening the page:**
  - Direction B:
    - The Start Menu start hands over to the coordinator, asks the pipe for a one-time URL, opens the browser itself (it holds the foreground right) and exits.
    - After a toast click, the COM-activated process has to open the URL. Whether it holds the foreground right was left unmeasured by Q254.
  - Direction C: both entry points work exactly as designed today and in Q254.
  - A tray icon would make the coordinator resident, which reverses Q280 b.
- **Coordinator not running:** any start becomes the coordinator by taking the pipe, as in phase 2.
  - In B it must stay alive while a page is connected. A reload drops the SSE connection briefly, so it needs a grace timer after the last disconnect, which `CoordinatorLoop`'s design does not have ("No timer").
  - In C the window's lifetime is the process's lifetime, as today.
- **Updates:**
  - Applying waits until nothing else runs and the coordinator's own window is closed. In B that rule becomes "no page connected", with census membership while a page is open.
  - A person-initiated install restarts through Velopack. In B the old tab dies and a new one opens; in C the window closes and reopens, as today.
- **Accessibility, dark mode, scaling:**
  - The task dialog has no dark mode, and the Q303 render showed it overflowing its Close button at 150% on 1920x1080.
  - Web pages and WebView2 follow the OS theme (measured for WebView2) and reflow.
  - Win32 controls, browser and WebView2 accessibility trees, and Avalonia's automation peers all work with screen readers. The web options depend on semantic HTML, which is easy to assert as text.
- **Q311's folder picker:**
  - A web page cannot hand the server a path:
    - `<input webkitdirectory>` uploads files with relative names;
    - `showDirectoryPicker()` returns a handle without a path and is Chromium-only (the default browser here is Firefox).
  - In B, a picker the coordinator opens cannot take the foreground. The coordinator neither received the last input nor is the foreground process, so Windows flashes the taskbar button instead.
  - Workarounds in B, none measured:
    - a topmost owner window (visible, not focused);
    - a `browserai:` protocol link, which shows a browser prompt per origin, and an ephemeral port means a new origin every run;
    - a pasted-path text box, which makes the API accept paths.
  - In C the picker is owned by BrowserAI's own foreground window. This also allows `IFileOpenDialog` with `FOS_PICKFOLDERS`, which fixes Q303's "old tree dialog" defect, behind a layout oracle.
  - None of this was measured: every piece needs a real window on the interactive desktop.

## 8. Directions

| | A. Native, trimmed + list-view Sessions | B. Browser tab on 127.0.0.1 | **C. WebView2 window, no port** | D. Avalonia | E. C now, B possible later |
|---|---|---|---|---|---|
| Size | ~0 (not measured) | **+163 KB** HttpListener, +12.8 KB raw, +3.5 to +6.7 MB Kestrel | **+93 KB** | ~+25 MB and 3 DLLs | as C |
| Files shipped | 1 | 1 | 1 | 4 | 1 |
| Time to UI | instant | +6 to +43 ms server, plus the browser | 327 to 562 ms | not measured | as C |
| Memory while open | small (not measured) | 4 to 26 MB server + one tab | ~145 MB, 7 processes | not measured | as C |
| New attack surface | none | **every web page and local user, permanently** | none | none | none until B is switched on |
| Dark mode, scaling | none; overflows at 150% | yes | yes | yes | yes |
| 22-row Sessions list | needs a second technology | natural | natural | natural | natural |
| Q311 picker | owned | focus problem | owned | owned | owned |
| Phase 2 and 3 designs | unchanged | hand-off, lifetime and timer change | unchanged | unchanged | unchanged |
| Headless tests | pure functions + synthetic dispatch | **best** (HTTP) | HTML builder + bridge + one private-desktop arm | **best native** (Avalonia.Headless) | as C |
| Dependency | none | the user's browser | WebView2 runtime | none | as C |
| Dashboard room | link | link or iframe | iframe | link (its WebView is paid) | iframe |
| Next page costs | Win32 plumbing | markup | markup | XAML | markup |

### 8.1 What each direction removes and adds

Today's line counts (`wc -l`):

- `Program.cs` 870, of which `ConfigurationSession` is about 443.
- `ConfigurationDialog.cs` 448, `Coordinator.cs` 326, `ClientState.cs` 313, `StatusReport.cs` 178, `AppState.cs` 139.
- `Interop/Foreground.cs` 87, `Interop/ShellInterop.cs` 273, `Interop/TaskDialogInterop.cs` 343.
- `Ui/BackgroundWork.cs` 214, `Ui/TaskDialogPage.cs` 638, `app.manifest` 70, csproj 93.
- Tests: `ConfigurationAppTests` 1,220, `TaskDialogLayoutTests` 406, `CoordinatorTests` 911, `SignInStepTests` 223, `SignInTaskTests` 249, `InteropLayoutTests` 417.

**A. Native, trimmed.** Removes nothing.
- Page builders and page navigation: +300 to 500.
- Show-details area: +50 to 100.
- `Ui/SessionsWindow.cs` plus list-view interop: +600 to 1,200.
- Tests: +400 to 800, and the list view is reachable only through synthetic messages.

**B. Browser tab.** Removes `TaskDialogPage.cs` (638), `TaskDialogInterop.cs` (343), the task-dialog rendering in `ConfigurationDialog.cs` (448, replaced), most of `ConfigurationSession` (~443, replaced), and three struct-layout test arms. Adds:
- listener, Host, Origin and secret checks, CSP and routes: 400 to 600;
- endpoints: 300 to 450;
- an HTML builder as a pure function of `AppState`: 400 to 600;
- CSS and JS: 300 to 600;
- SSE plus the page-connected lifetime and grace timer: 150 to 250;
- a pipe verb that mints the URL: 50 to 100;
- tests, HTTP plus per-route security: 900 to 1,500.

The net product code is about equal to what it removes, plus a permanent security layer.

**C. WebView2 (recommended).**
- Removes `TaskDialogPage.cs`, `TaskDialogInterop.cs`, the task-dialog rendering in `ConfigurationDialog.cs`, the identifier arithmetic and click plumbing in `ConfigurationSession`, and three layout arms.
- Keeps `Foreground`, `ShellInterop`, `BackgroundWork` (polled from a window timer), `AppState`, `ClientState`, `StatusReport` and `Coordinator`.
- Adds:
  - `Interop/WebView2Interop.cs`: 200 to 280;
  - `Ui/WebViewHost.cs` (window, `WM_SIZE`, `WM_DPICHANGED`, runtime check, data folder): 250 to 350;
  - handler classes: 120 to 180;
  - HTML builder and assets: 700 to 1,100;
  - JSON dispatcher: 250 to 350;
  - a static-loader `NativeLibrary` item and a notice.
- Tests:
  - HTML builder, ported from `ConfigurationAppTests`: similar size;
  - dispatcher: 300 to 500;
  - IID and slot oracle against `WebView2.h` or the SDK `.winmd`: 100 to 200;
  - one private-desktop smoke arm: 100 to 150.

**D. Avalonia.** Removes the same task-dialog stack. Adds:
- 800 to 1,500 lines of XAML and C#;
- three floating packages;
- the telemetry opt-out with a test that it is set;
- Avalonia.Headless tests: 600 to 1,000.

**E. C now, B later.** C, with the dispatcher's messages designed as transport-free JSON commands so the hardened listener of B can sit in front of them later. No extra code today.

### 8.2 The test suite in each direction (never a window, never the foreground)

- **A:** unchanged approach. Pure functions, `TaskDialogHost.Dispatch`, and renders only in re-verification rigs.
- **B:**
  - Fully headless: an in-process server on 127.0.0.1:0, one raw HTTP exchange per arm (0.29 to 0.39 ms measured).
  - Security arms walk the route table, so a new route cannot skip a check.
  - Opening the browser is a seam that only records the URL.
  - Page JS stays thin. If it needs tests, the payload already ships node; `Microsoft.Playwright` stays banned.
- **C:**
  - The HTML is a pure function of `AppState`, the successor of `ConfigurationDialog.Page`.
  - The bridge is JSON in, JSON out.
  - One arm starts the published app in a self-test mode on the suite's existing `PrivateDesktop`, where `WindowWatch` does not see it by design, and waits for the page's ready message. Measured: 0.33 to 0.56 s and about 145 MB.
  - It needs a `SuiteCapability` row for machines without the runtime.
- **D:** Avalonia.Headless.
- **B, C and E all touch house rules:** `.html`, `.css` and `.js` files join the SPDX-header scan and the product-voice corpus (`IsProductVoice`).

## 9. Recommendation

**C, built so that E stays open**, in this order:

1. **Sessions first.** Phase 4 needs it, and it is the page the task dialog cannot carry.
2. **Then the rest:** Home (status, Q309 b, Q310), then one section per client, then Q311's single picker.
3. **Retire the task dialog** once the WebView2 window covers everything it did. A machine without the runtime gets one `MessageBoxW` sentence with Microsoft's bootstrapper link and the `--report` path.
4. **The page never supplies a path or a command.** The picker is native.
5. **Untrusted strings are text, never markup**, whatever UI is chosen (section 11.1).

If you want a real browser tab (B):

- Use **HttpListener on `http://127.0.0.1:<ephemeral>/`**: +163 KB, parsing done by http.sys in the kernel, and a second process could not claim a sub-path.
- Apply every control in 4.5.
- Decide the coordinator's page-connected lifetime and grace timer explicitly.
- Avoid Kestrel: +3.5 to +6.7 MB, and its minimal-API generated code trips the banned-API rule.

## 10. Risks

- **C:**
  - the runtime can be missing (Windows 10 LTSC, Server, clean installs) or disabled by policy;
  - about 145 MB and 7 processes while the window is open;
  - the runtime moves every two weeks, though its COM interfaces are immutable and new members arrive as new interfaces;
  - a wrong vtable slot fails only at run time, so it needs an oracle test;
  - the data folder must never land under the install root;
  - WebView2 processes can outlive the window briefly.
- **B:**
  - one missed check on one route reopens the whole class (four MCP-ecosystem CVEs in 2025);
  - the coordinator gains a timer and a notion of connected pages;
  - stale tabs after every update, and duplicate tabs;
  - the toast's foreground hand-off is unmeasured;
  - the picker focus problem.
- **A:** growth continues in Win32 code, there is no dark mode, Sessions forces a second native technology, and states already overflow at 150%.
- **D:** about +25 MB, 3 DLLs, build telemetry, floating majors, and far more framework than the "very minimal" GUI you described on 2026-09-15.
- **Every web option:** HTML injection from purposes, directories and repository files if a string is ever written as markup; the wording and SPDX scans must be extended to the new file types.

## 11. Findings outside the question

1. **The current task dialog renders a repository-controlled string inside hyperlink-enabled content, and its link handler runs whatever a non-`folder:` link names.**
   - Where the string enters: `ConfigurationDialog.cs:263` writes `this folder (...) registers: {project}`. `{project}` is the raw `command` of the `browserai` entry in the nearest project file, whoever wrote it (`McpRegistryView.cs:232`).
   - Why it is a link: `TaskDialogPage.cs:258` turns hyperlinks on.
   - Why a link runs something: `Program.cs:488-497` (`OnLink`) passes any non-`folder:` href to `ShellInterop.OpenUrl`, which calls `ShellExecuteW(0, "open", url, ...)` (`ShellInterop.cs:238`).
   - Microsoft's own warning on this flag: "Enabling hyperlinks when using content from an unsafe source may cause security vulnerabilities."
   - Not reproduced, because that needs a window. Phase 4's model-written purposes would be the same class of problem in a task dialog, and XSS in a web page.
2. **The coordinator is short-lived today.** `Coordinator.cs:168` returns `NothingPending` as soon as nothing is staged, and `Main` exits with it. "The natural host for a local web server" therefore means a lifetime change, which is a design decision.
3. **Minimal APIs conflict with this repository's own rules** (3.2).
4. **Avalonia's build sends telemetry unless opted out, and in this session it did** (section 14).
5. **http.sys with a `localhost` prefix is a LAN listener filtered only by the Host header** (row K).

## 12. What I could not verify, and why

- **Focus and foreground:**
  - whether a picker the coordinator opens after a click in the browser comes to the front;
  - whether Firefox raises its window or opens a background tab when BrowserAI opens a URL;
  - the topmost-owner workaround.
  
  All of these need a real window on your desktop.
- **Cross-user behaviour** (another user connecting to the port, taking an http.sys prefix, reading the browser's command line): no second account, so this rests on documentation only.
- **Whether another machine can reach row K:** no second machine.
- **The real `BrowserAI.exe` with a server or WebView2 compiled in:** built, never run (rule 6). Runtime figures come from the standalone probes.
- **WebView2 on the interactive desktop:** DPI above 100%, Narrator, focus, and Windows 10 were not tested.
- **Avalonia:** startup and memory not measured (never run), nor its in-app size delta.
- **WinUI 3 size:** not built.
- **Task dialog memory and the list view's size delta:** not measured.
- **Whether Avalonia's telemetry send actually happened:** the id file was created; the network call was not observed.

## 13. Open questions for you

**Q-B1. Which technology carries the management interface?**
- *Primer:* the task dialog already holds 6 to 12 command links, has no dark mode and overflows at 150%. Phase 4 needs a multi-select list of about 22 rows.
- (a) Native, trimmed, plus a list view for Sessions.
- (b) A browser tab on 127.0.0.1 with every control in 4.5.
- (c) A WebView2 window, no port.
- (d) Avalonia.
- (e) (c) now, with messages designed so (b) can be added later.
- **Recommendation: (e).**

**Q-B2. With (c) or (e): what does a machine without the WebView2 runtime get?**
- *Primer:* the runtime is inbox on Windows 11 and was pushed to Windows 10, but Microsoft says it can be missing on LTSC, Server and clean installs.
- (a) The full task dialog as a fallback, which means two UIs to maintain.
- (b) One `MessageBoxW` sentence with the bootstrapper link and the `--report` path.
- (c) The install hook chain-installs the Evergreen bootstrapper.
- (d) Refuse to open the window.
- **Recommendation: (b).**

**Q-B3. Only if (b) wins Q-B1: the shape of the loopback server.**
- *Primer:* a browser tab needs a listener, a secret handed to the browser, and a rule for when the coordinator may exit. The recommended choice is first on each line.
- Stack: **HttpListener** (+163 KB), raw socket (+12.8 KB, HTTP parsing written by hand), or Kestrel (+3.5 to +6.7 MB, trips the banned-API rule).
- Port: **ephemeral per start**, or fixed per user.
- Secret: **a one-time code exchanged for a `sessionStorage` secret sent as a header**, or an HttpOnly cookie, which every 127.0.0.1 port receives.
- Lifetime: **while an SSE connection is open, plus a grace timer**, or until the page's own Close button, or resident (reverses Q280 b).

**Q-B4. Where does Q311's picker live?**
- *Primer:* a page cannot give the server a path, and a coordinator-opened picker cannot take the foreground after a browser click.
- (a) A native picker owned by BrowserAI's window.
- (b) An unowned picker, with the page telling you to find it.
- (c) A `browserai:` protocol link, with a browser prompt per port.
- (d) A pasted path, which widens what a forged request can do.
- **Recommendation: (a)**, using `IFileOpenDialog` with `FOS_PICKFOLDERS` behind a layout oracle, which retires the old tree dialog.

**Q-B5. Which page moves first?**
- (a) Sessions first; the task dialog keeps the rest until each page moves.
- (b) Everything at once.
- (c) Home first.
- **Recommendation: (a).**

**Q-B6. The hyperlink finding in 11.1.**
- *Primer:* the current dialog renders a repository-controlled command in hyperlink-enabled content and runs whatever a link names. Phase 4 would add model-written purposes.
- (a) Fix it now in the task dialog: escape non-product strings, and limit `OnLink` to `folder:` links under known roots plus the guide URL.
- (b) Fix it only as part of the move.
- (c) Leave it: the charter's non-goal was written about callers, not content.
- **Recommendation: (a)**, with a test that plants `<A HREF` in a project file and asserts it is not a link, because the task dialog stays in service until the move is done.

## 14. Incidents and residue

- **Avalonia build telemetry.**
  - My two Avalonia builds (about 04:40Z and 04:41Z) ran `Avalonia.BuildServices` 11.3.2's telemetry task, which sends anonymous build data because `AVALONIA_TELEMETRY_OPTOUT` was not set.
  - It created `%LOCALAPPDATA%\AvaloniaUI\BuildServices\id` (16 bytes, at 04:40:22Z; the folder did not exist before). I removed it at about 04:45Z.
  - Whether the data reached Avalonia's servers was not observed. This was an unintended transmission I cannot undo.
- **NuGet HTTP cache.**
  - Packages went into `.work/zoomout/b/packages`, but NuGet's HTTP cache is global. 57 files (297,106,440 bytes) appeared in `%LOCALAPPDATA%\NuGet\v3-cache` after 03:50Z, from my restores and the nuget MCP lookups; other agents may have added some.
  - I left them in place because another agent may be reading the cache. `dotnet nuget locals http-cache --clear` removes them, but it clears the whole cache.
- **dotnet CLI telemetry:** the machine does not set `DOTNET_CLI_TELEMETRY_OPTOUT`. My first five read-only `dotnet` commands ran without it; every build and publish set it.
- **One stray probe:** the http.sys prefix test aborted before stopping its probe (pid 7536). I stopped it by pid after checking its image path.
- **One measurement failure:** the first measurement run failed on a sharing violation, which I fixed.

Final state at the end of the work:

- no process from `.work/zoomout/b` running (checked by executable path and command line);
- no private desktop left;
- no WebView2 process from the probes;
- `%LOCALAPPDATA%\AvaloniaUI` gone;
- `git status` clean at `a81bad8`.

Nothing was posted or installed globally, no window was shown on your desktop, and neither BrowserAI executable was started.

## 15. Sources (all read 2026-09-25)

**Microsoft Learn**
- ASP.NET Core Native AOT: https://learn.microsoft.com/aspnet/core/fundamentals/native-aot?view=aspnetcore-10.0 and https://learn.microsoft.com/aspnet/core/release-notes/aspnetcore-8.0
- Native AOT: https://learn.microsoft.com/dotnet/core/deploying/native-aot/
- Trimming incompatibilities: https://learn.microsoft.com/dotnet/core/deploying/trimming/incompatibilities
- UrlPrefix strings: https://learn.microsoft.com/windows/win32/http/urlprefix-strings
- http.sys routing: https://learn.microsoft.com/windows/win32/http/routing-incoming-requests
- HTTP.sys in ASP.NET Core: https://learn.microsoft.com/aspnet/core/fundamentals/servers/httpsys?view=aspnetcore-10.0
- SO_EXCLUSIVEADDRUSE: https://learn.microsoft.com/windows/win32/winsock/using-so-reuseaddr-and-so-exclusiveaddruse
- SetForegroundWindow: https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setforegroundwindow
- TASKDIALOGCONFIG: https://learn.microsoft.com/windows/win32/api/commctrl/ns-commctrl-taskdialogconfig
- WebView2 distribution: https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution
- WebView2 evergreen vs fixed version: https://learn.microsoft.com/microsoft-edge/webview2/concepts/evergreen-vs-fixed-version
- WebView2 in WinUI: https://learn.microsoft.com/windows/apps/develop/ui/controls/webview2
- Edge supported operating systems: https://learn.microsoft.com/deployedge/microsoft-edge-supported-operating-systems
- Edge support lifecycle: https://learn.microsoft.com/deployedge/microsoft-edge-support-lifecycle
- WebView2 user data folder: https://learn.microsoft.com/microsoft-edge/webview2/concepts/user-data-folder
- WebView2 local content: https://learn.microsoft.com/microsoft-edge/webview2/concepts/working-with-local-content
- WebView2 IDL (environment overrides): https://learn.microsoft.com/microsoft-edge/webview2/reference/win32/webview2-idl?view=webview2-1.0.4191.47
- Windows App SDK 1.6 release notes: https://learn.microsoft.com/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-6
- Windows App SDK self-contained deployment: https://learn.microsoft.com/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps

**Package versions** (nuget MCP, looked up today)
- Microsoft.Web.WebView2 1.0.4191.47 (2026-08-28)
- Avalonia 12.1.3 (2026-09-22)
- Microsoft.WindowsAppSDK 2.5.1 (2026-09-17)
- Photino.NET 4.0.16 (2025-01-23, not used)

**Libraries and specifications**
- WebView2 AOT issue: https://github.com/MicrosoftEdge/WebView2Feedback/issues/4800 (comments read through the GitHub MCP server)
- Avalonia headless testing: context7 `/avaloniaui/avalonia-docs`
- Avalonia pricing (Accelerate, WebView): https://avaloniaui.net/pricing
- Avalonia build telemetry: `Avalonia.BuildServices` 11.3.2 README inside the package
- MCP specification 2025-11-25, transports: https://modelcontextprotocol.io/specification/2025-11-25/basic/transports

**Incidents**
- MCP Inspector: https://www.oligo.security/blog/critical-rce-vulnerability-in-anthropic-mcp-inspector-cve-2025-49596 and https://www.tenable.com/blog/how-tenable-research-discovered-a-critical-remote-code-execution-vulnerability-on-anthropic
- Claude Code IDE extensions: https://github.com/advisories/GHSA-9f65-56v6-gxw7 and https://securitylabs.datadoghq.com/articles/claude-mcp-cve-2025-52882/
- MCP SDKs: https://osv.dev/vulnerability/CVE-2025-66414 and https://bugzilla.redhat.com/show_bug.cgi?id=2418445
- Ollama: https://www.nccgroup.com/research-blog/technical-advisory-ollama-dns-rebinding-attack-cve-2024-28224/
- Transmission: https://github.com/transmission/transmission/pull/468 and https://seclists.org/oss-sec/2018/q1/34
- Zoom: https://www.rapid7.com/db/vulnerabilities/zoom-cve-2019-13450/

**Browser behaviour and other tools**
- Jupyter configuration: https://jupyter-notebook.readthedocs.io/en/5.7.6/config.html
- Jupyter security: https://jupyter-notebook.readthedocs.io/en/4.x/security.html
- Chrome Local Network Access: https://developer.chrome.com/blog/local-network-access and https://chromestatus.com/feature/5152728072060928
- Firefox Local Network Access: https://fosdem.org/2026/schedule/event/QCSKWL-firefox-local-network-access/ and https://firefox-admin-docs.mozilla.org/reference/policies/localnetworkaccess/
- 0.0.0.0 day: https://www.oligo.security/blog/0-0-0-0-day-exploiting-localhost-apis-from-the-browser
- Task dialog dark-mode workaround: https://github.com/SFTRS/DarkTaskDialog

**Repository files read:** `src/BrowserAI.App/*`, `src/BrowserAI.Core/Coordination/*`, `NamedPipes.cs`, `McpRegistryView.cs`, `DECISIONS.md`, `ARCHITECTURE.md`, `TESTING.md`, `TODO.md`, `kb/windows/processes.md`, `.work/STATE.md`, `.work/q254-design/REPORT.md`, `.work/window-render/reports/summary.json`, and the bundled `playwright-core` `coreBundle.js`.

## 16. Evidence files

All under `C:\Source\SixFive7\BrowserAI\.work\zoomout\b\`:

- `probes\` (baseline, rawsocket, httplistener, kestrel-empty, kestrel-slim, wv2probe, avaprobe) and `driver\` (the measurement driver and the private-desktop launcher)
- `logs\measure-2.tsv` (3.1)
- `appcopy\`, `logs\publish-app-summary.txt` and `logs\publish-app-*.log` (3.2; the RS0030 and NU1510 lines are in `publish-app-KESTREL_SLIM.log`)
- `logs\security-1.txt`, `logs\security-2.txt`, `logs\hijack-test.txt` and `hijack-test.ps1` (3.3)
- `logs\wv2-dll.txt`, `logs\wv2-static.txt` and `logs\publish-wv2-summary.txt` (3.5)
- `logs\avaprobe-sizes.txt` (3.6)
- `wv2pkg\` (the WebView2 SDK 1.0.4191.47 package from nuget.org)