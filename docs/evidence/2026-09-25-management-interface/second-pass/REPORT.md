<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

## Track B2: the management interface without WebView2 (research only, 2026-10-01)

Nothing tracked was edited. No window was shown: every browser ran on a private desktop inside a kill-on-close job. Neither BrowserAI executable was started. Scratch is `C:\Source\SixFive7\BrowserAI\.work\zoomout\b2\` (index in `notes\index.txt`).

Tags: (m) measured today, (e) my estimate, (r) read from source or documentation today, (b) carried from the first report.

Under test:
- Chrome for Testing 154.0.8037.0 (chromium-1246, the revision the payload names).
- Playwright's Firefox build 156.0.
- node v24.21.0 and playwright-core 1.64.0-alpha-1789764292000 from the repository's `payload\`.
- .NET SDK 10.0.401, Windows 11 build 26300, 32 logical processors, 128 GB.

Stock Firefox 157 was not run.

---

## 1. The two direct answers

**"Can't we just open the system browser and remove the WebView complexity?"** Yes. It is the smallest option, the best tested, and can be made safe by structure. I recommend it (section 7).
- **Cost:** a listener on 127.0.0.1 while a page is open, one bounded wait in the coordinator, and a different way to choose a folder.
- **Size:** my prototype of the whole listener is 333 lines and adds 94 to 100 KB to `BrowserAI.exe` (m).
- **Holds up:** it refused every hostile request I sent, raw and from two real browsers (m).

**"We already ship Playwright. Or can't we use that, as we need to control the update while it happens?"** The update does not rule it out. Other things do.
- **It works.** A NativeAOT process started the provisioned Chromium as an app window and talked to it over the DevTools pipe, with no port, no node and no Playwright (m).
- **The update is fine for P1.** The browser runs from the data root, so the apply gate does not count it and Velopack does not kill it. It exits by itself 133 ms after BrowserAI.exe's pipe closes, and comes back in about 0.35 s after the restart (m).
- **No option keeps a live BrowserAI window during the file swap.** Velopack needs nothing running from the install root. A browser tab is the only surface that stays on screen through it.
- **What does rule P1 out as the only interface:** on a fresh install no browser has been downloaded yet, so the first-run window has no engine. See 3.1 for the rest.
- **P2 and P3 work against the update design.** Their host processes run from the install root, so the interface is itself something the update has to stop.

---

## 2. Option S: the system browser

### 2.1 The design

- **Listener:** one raw TCP socket on 127.0.0.1, exclusive, on a port Windows picks.
- **Address:** `http://127.0.0.1:<port>/<token>/`, where the token is 256 random bits made per listener.
- **Hand-off:** the address travels only through the coordinator's pipe (user-only DACL). The process the person started asks for it, opens it with the shell and exits.
- **Page:** server-rendered HTML plus about 100 lines of script: one `EventSource` for live state, `fetch` POSTs for actions.

**One gate runs before any route.** Anything it does not admit gets the same bare `404`.

1. Method is GET or POST.
2. Exactly one `Host` header, equal to `127.0.0.1:<port>`.
3. Path starts with `/<token>/`, compared in constant time.
4. `Sec-Fetch-Site`, when present, is `same-origin` or `none`.
5. POST: `Origin` is exactly ours, `Content-Type` is `application/json`, body at most 64 KB. A GET that carries an `Origin` must carry ours.
6. Optional: the connecting process belongs to the same Windows user (2.6).

The server never sets or reads a cookie, sends no CORS header, changes nothing on a GET, and puts one fixed block of security headers on every response.

**The first report's eight controls:**

| Control (first report 4.5) | Verdict | What carries it now |
|---|---|---|
| 1. Loopback, ephemeral port, exclusive | Keep | The raw socket gives all three |
| 2. Exact Host | Keep | One comparison; held against rebinding with the token already leaked (m) |
| 3. One-time code exchanged for a session secret | Replaced | The address is the secret; it dies with the listener |
| 4. Secret in sessionStorage plus a custom header | Replaced | No cookie, no storage, no header (m: cookie empty, storage 0 in both browsers) |
| 5. Exact Origin and Sec-Fetch-Site on writes | Keep, widened | Checked on every request that carries them |
| 6. No CORS, CSP, nosniff, no-referrer, no-store | Keep | One constant; never an inline script |
| 7. No endpoint takes a path or a command | Split | Never a command. A path is your call (2.4) |
| 8. Untrusted strings as text | Keep | One encoder; the CSP stops a missed escape from running |

On control 7: the registrar writes BrowserAI's own command, not one the caller supplies (r: `McpRegistrar.ApplyToProject`). A forged path could at worst add BrowserAI's own entry to a project that did not ask for it.

A one-time code with a header secret is the alternative if "someone saw my address bar" matters. It costs script-held secrets and a harder reload.

**Evidence** (`run\s1`, `s2`, `f*`, `z1`):
- **Raw requests, 24 of 24 as designed (m).** Page with token 200; write 204; no token, wrong token, `localhost` Host, attacker Host, no Host, two Hosts, foreign Origin, no Origin, form content type, preflight, PUT, 9 KB header, oversized body, chunked body and non-HTTP bytes all 404; `[::1]` and the LAN address refused.
- **Rebinding in a real browser, token leaked (m).** Chromium 154 and Firefox 156 were told `attacker.example` is 127.0.0.1: both were refused on Host.
- **Another site that knows the whole address (m).** Seven request shapes, in both browsers: no-cors POST, no-cors GET, CORS GET, JSON POST, iframe, form POST, WebSocket. All refused; the write counter never moved.
- **Fuzz (m).** 4,000 mutated requests: the listener stayed up. 2,465 got 404 and 783 were dropped. The 760 answered were mutations that had left the request valid (8 of 8 sampled); 200 writes were admitted and the counter read 200.

### 2.2 The coordinator's lifetime rule

The coordinator stops when all three hold:
1. No newer package is staged, or it has just handed one to Update.exe.
2. No page is connected.
3. The reconnect wait has run out: a fixed time since the last page disconnected, or since an address was last handed out, whichever is later.

Around that:
- **While a page is connected** it stays in the live census and applies nothing by itself. The page offers "Install now".
- **The listener and token** exist from the first hand-out until 2 and 3 hold. A coordinator that is only waiting to apply has no listener.
- **"Page connected" is an open event-stream socket.** Its close completes a pending read, so no ping is needed. A closed tab was seen in 20.8 and 212 ms (Chromium) and 24 to 45 ms (Firefox, n=28) (m).
- **This is the one change to "No timer".** The existing wait gets a timeout only while no page is connected and the listener is open. I suggest 30 s for a first connect and 5 s for a reconnect (e).

### 2.3 Reload, duplicate tabs, stale tabs, reconnect

- **Reload:** the address still works. The gap between old stream closing and new one opening was 4.8 to 11.3 ms in Chromium (20 reloads) and 19 to 135 ms in Firefox (310 reloads) (m).
- **Duplicate tabs:** both stay live (m: second tab connected, pages=2). To keep one tab, the coordinator can tell the older page to close itself when a new address is handed out.
- **A page can close its own tab** while its history has one entry (m in Chromium 154 and Firefox 156; r: Firefox's `IsScriptClosable`). Actions should therefore be `fetch` calls, never form navigations.
- **Stale tab after an update:** avoidable. In 12 of 12 rounds a new process took the same exclusive port on the first attempt, 36 to 46 ms after the old one was killed. The open page reconnected by itself, 3.0 s later in Chromium and 5.0 to 5.3 s in Firefox, and its write then worked (m, `run\rc*`, `rf*`).
  - Velopack passes restart arguments through (r: kb, measured 2026-08-15), so the restarted app can be handed its old port and token.
  - If the port is taken, fall back to a new address in a new tab.
- **Warts I could not remove:**
  - A tab the browser discards or restores after the listener is gone shows the browser's own "cannot connect" page.
  - A tab left open blocks automatic applies, as an open window does today.

### 2.4 The folder picker and focus (your default browser is Firefox)

A page cannot hand over a path from a native picker, and a picker the hidden coordinator opens appears behind the browser with a flashing taskbar button (r: SetForegroundWindow rules). Workable shapes:

- **(a) No native picker.** The page shows the folder BrowserAI was started in (the starter sends its working directory through the pipe) and a box to paste a folder into, validated by the same `CanonicalPath` rules sessions use. Focus never leaves the tab.
- **(b) A folder browser in the page.** The server lists directories and the page sends back ids, so no free-text path crosses the wire. About 150 to 250 lines (e).
- **(c) A `browserai:` link** that starts BrowserAI.exe from the browser. That process was started by the foreground process, so its picker gets focus. It costs a registered protocol (section 4), and Firefox asks each time because the port changes.
- **(d) The coordinator opens the picker anyway.** Behind the browser.

When the tab opens: the starter holds the foreground right and passes it on. A second firefox.exe calls `SetForegroundWindow` on the running instance before handing over the URL (r: `nsWinRemoteClient.cpp`). Not measured.

### 2.5 The listener stack

Raw socket.
- **HttpListener:**
  - It cannot bind exclusively and gives no disconnect signal, so 2.2 would need pings.
  - Microsoft Learn now says "We recommend that you don't use the HttpListener class for new development" (r, .NET 10 page).
  - It adds 163,328 bytes (m, reproduced).
- **Kestrel:** 3.5 to 6.7 MB, and its generated code trips the banned-API rule (b).
- **Banned symbols:** neither list bans sockets (r).
- **Trim and AOT warnings:** 0, standalone and in the app copy (m).
- **In-app size (m, never run):**
  - whole prototype +94,208 bytes;
  - with the same-user check +99,840 (+0.91%);
  - the first report's bare socket +12,800.
- **Runtime:** 3 ms from `Main` to listening; 3.3 MB private while idle (m).

### 2.6 Risk that remains

**Web pages.** A page on any site can make the browser send requests to loopback ports.
- It can learn a port answers and hold some of the 32 connection slots.
- It cannot read or change anything (2.1).
- Chrome since 142 (b) and Firefox by default (r: `network.lna.blocking` true in main) also put public-site-to-loopback requests behind a prompt. The design does not lean on that.
- What is left:
  - a bug in about 330 lines;
  - the address in browser history while the listener lives;
  - any extension with access to all sites, which can read and click the page as you can. That is a trust dependency a window of our own does not have.
- The worst a management action does is stop sessions, change registrations or install from the configured feed. None runs a caller-chosen command.

**Other local users.**
- Without the token they get 404.
- The token never leaves your session: the pipe is user-only, and a process's access list here names only the user, SYSTEM and the logon session (m), so another standard user cannot read the browser's command line.
- The optional same-user check finds the connecting pid in the TCP table and compares its token user with ours. It admitted 88 of 88 Chromium connections and every Firefox connection, at 0.29 to 0.73 ms each (m). With it another user gets the 404 even holding the token.
- They can still fill connection slots.
- They can bind the port number after the listener exits. A stale tab then reaches their process holding nothing valid: phishing at a 127.0.0.1 address, not access.
- None of the cross-user half was tested: one account.

### 2.7 Lines, and tests without a window

**Add (e, grounded in the prototype):**
- listener, gate, parser: 300 to 350;
- same-user check: 150 to 170 (optional);
- routes: 300 to 450;
- HTML builder: 400 to 600;
- CSS: 100 to 200;
- script: 80 to 150;
- coordinator changes (page count, wait, an `open` verb): 150 to 250;
- resume after update: 40 to 80 (optional);
- folder choice: 100 to 250.

About 1,500 to 2,400 in all.

**Remove:**
- `TaskDialogPage.cs` 638, `TaskDialogInterop.cs` 343, `ConfigurationDialog.cs` 448;
- about 443 of `Program.cs`;
- `Foreground.cs` 87, most of `BackgroundWork.cs` 214, the picker half of `ShellInterop.cs`.

About 2,200 to 2,400.

**Tests:** all in-process, with no desktop at all.
- The listener on port 0 and raw exchanges.
- A gate table walked over every route.
- A parser fuzz.
- Lifetime by opening and closing sockets.
- "Open the browser" as a seam that records the address.

`TaskDialogLayoutTests` (406) goes; `ConfigurationAppTests` (1,220) ports to the HTML builder.

---

## 3. Option P: the browser BrowserAI ships

### 3.1 P1: BrowserAI.exe starts the provisioned Chromium itself

**The pipe.**
- (r, Chromium main `41d4e76`) `--remote-debugging-pipe` uses "stdio pipes [in=3, out=4] or ... the remote pipes specified in the 'remote-debugging-io-pipes' switch", which is "a comma separated list of two pipe handles serialized as unsigned integers". `AdoptPipes` requires each to be a pipe and relies on inherited handles keeping their values.
- (m) A NativeAOT process did it with two anonymous pipes and `CreateProcessW` with an exact handle list, which `JobLauncher` already builds for three handles. First reply 266 to 314 ms after launch.
- A policy that disables remote debugging would block it, as it would block Playwright (r).

**A minimal client.**
- Messages are JSON ended by a NUL byte (r).
- My client is 195 lines plus 124 for the launch.
- It uses about ten methods: `Target.getTargets`, `Target.attachToTarget`, `Runtime.addBinding`, `Fetch.enable`, `Fetch.fulfillRequest`, `Fetch.failRequest`, `Page.navigate`, `Runtime.evaluate`, `Browser.close`.
- In-app cost: +40,960 bytes (m).

**Content without a port.**
- Every request of a made-up https origin is answered over the pipe.
- The page, its script and stylesheet loaded as a secure context with a strict CSP (m).
- Page to host is a binding; a round trip took 0.20 to 0.31 ms (m).
- Navigation elsewhere is failed by the host (m: `ERR_BLOCKED_BY_CLIENT`).

**The window** (m, captured with PrintWindow: `run\a1-window0.png`, `run\a2-window0.png`).
- No address bar, no tabs.
- The title is the page's `<title>` and the icon is the page's favicon, so both can be ours.
- For the first 100 ms or so the title is derived from the start URL and looks wrong.
- No first-run or default-browser prompt appeared, even without the flags that suppress them.
- **Branding:** by default a permanent, non-closeable infobar reads "Chrome for Testing v154.0.8037.0 is only for automated testing. For regular browsing, use a standard version of Chrome that updates automatically." with a "Download Chrome" link.
  - `--disable-infobars` removes it (m). Playwright relies on the same switch.
  - The executable calls itself "Google Chrome for Testing" in Task Manager.
- **Taskbar identity:** the window's app id is `ChromeForTesting.<name from the URL>.<profile folder>.Default`, with no relaunch command. Setting it from our process succeeded and read back (m). Whether the taskbar honours it was not measurable.

**Profile, closing, second start** (m).
- It needs its own profile folder under the data root: 7.5 to 12.6 MB after a run. A throwaway profile costs about 25 ms more per start.
- Closing the window ends the browser in 120 to 200 ms; the host sees pipe EOF in 30 to 55 ms.
- A second chrome.exe on the same profile exits in 76 ms without using its pipe, and the first browser opens another window. That only matters if something other than the coordinator starts it.

**Memory and start-up** (m, warm disk cache, fast machine).

| Flags | Page ready after launch | Processes | Private memory |
|---|---|---|---|
| Quiet set, warm profile (6 runs) | 332 to 366 ms, median 342 | 8 to 9 | 228 to 240 MB |
| Quiet set, fresh profile (5 runs) | 359 to 404 ms, median 369 | 8 to 9 | 232 to 241 MB |
| Minimal flags | about 420 ms | 11 to 12 | about 350 MB, settling to about 270 MB |

For comparison, WebView2 was 327 to 338 ms warm and 143 to 146 MB in 7 processes (b). A true cold start was not measured.

**Background network** (m, eight idle minutes on our own page, behind a sink proxy).

| Flags | Requests | Where |
|---|---|---|
| Minimal | 29 | `clients2.google.com` (extension update), `accounts.google.com/ListAccounts`, `android.clients.google.com/checkin`, `update.googleapis.com`, `safebrowsing.googleapis.com`; plus two DNS queries for `www.google.com` that went past the proxy |
| Playwright's quiet set | 23 | `accounts.google.com`, `www.google.com/async/folae`, `android.clients.google.com`, `update.googleapis.com`, `clients2.google.com/time` |
| Quiet set plus `--host-resolver-rules="MAP * ~NOTFOUND"` | 0 | 0 TCP connects and 0 bytes sent in 334 s; the same five URLs were tried and died at name resolution |

- Playwright's quiet flags do not silence Chrome for Testing. This confirms the earlier researcher.
- The made-up host name was itself tried three times before the resolver rule went in.

**The house rule.**
- **Letter:** not broken. No Playwright package is referenced; the enforcing test reads package references.
- **Purpose:** touched. The test's own comment calls the danger "a second, silent way to reach the browser". P1 puts a browser-driving client into the product.
- It would need a written carve-out: the client lives only in the app project, never reaches a session browser, and no tool reaches it.

**The stray sweep** (r: `StraySweep.cs`; m: the title).
- The browser's message window carries the profile path as its title, which is what the sweep reads.
- The main process would be spared, because that folder holds no `browserai.lock`.
- Its helpers would be listed as unattributable on every pass with no session open. That is exactly the pattern the log sentence tells a person to look at.
- **Clean way round:** a fixed profile path the sweep knows by name, spared with its own reason. About 60 to 120 lines (e).
- **A related cost:** `browserai_reinstall_browser` refuses while anything runs from the tree (r), so an open management window blocks a model's repair.

**First run, pruning, update, Firefox-only.**
- **First run:** no browser exists until a session first provisions one. The Chromium download is 207.3 MB (r: README) and the tree is 433.6 MiB on disk (m). The window cannot open without a fallback: a native one, or the system browser.
- **Pruning:** an old revision with a live process is kept and named (r: `RevisionPrune`). The window has to accept any installed revision after an update moves the payload.
- **Update:** the window closes with BrowserAI.exe and reopens after the restart.
- **Firefox-only user:** gets that download and that disk only for the settings window.
- **Patch level:** the browser is 154.0.8037.0 while Chrome for Testing stable is 154.0.8037.92 (r).

### 3.2 P2: the bundled node and playwright-core

This is what upstream does: `launchPersistentContext` with `--app=data:text/html,` and `--test-type=`, minus `--enable-automation` (r: `coreBundle.js`).

Measured over 6 runs, with the page served through `page.route`:
- 683 to 705 ms from node start to page ready;
- 375 to 417 MB in 10 to 11 processes, of which node is 131 to 166 MB;
- round trip 1.1 to 1.3 ms.

Against it:
- `node.exe` resolves to `current\payload\node\node.exe` (r: `PayloadLayout`), under the install root. The apply gate counts it and Velopack kills it.
- The README forbids driving Playwright "via `Microsoft.Playwright` / Playwright for .NET or any other binding". P2 is that, in JavaScript.

### 3.3 P3: an ordinary headed session

Not sensible. Not measured.
- A normal browser window with address bar and tabs.
- The page has no channel back to the app except polling `browser_evaluate` or a port.
- Server and node run from the install root, so the interface shows up as a live session blocking the update it manages.
- A broken payload would leave no way to show the repair interface.

---

## 4. Option L: a local HTML file plus `browserai:` links

**What browsers do** (r, not run: it needs a registered protocol and a real desktop).
- **Firefox** asks "Allow this file to open the browserai link with BrowserAI?" and offers "Always allow this file to open browserai links". It can remember that for `file` principals.
- **Chrome** asks "Open BrowserAI?" and may offer "Always allow ... to open links of this type" for trustworthy origins. For local files I believe that is one shared `file://` origin, so every local HTML file. That reading is from memory, unconfirmed.

**What a hostile page can do.**
- Any site can raise the same prompt. Any local program or document link can start the handler with no prompt.
- Microsoft's own page warns that quotes in the URI can add command-line parameters.
- Velopack honours a hook switch only as the first argument (r: `VelopackApp.cs`). The registration must therefore be `BrowserAI.exe --link "%1"`, and the handler must act only on a valid one-time token.

**Staying current.**
- A file is a snapshot. Every click starts a process, which rewrites the file.
- The page can only poll by reloading a script file.
- There is no answer channel, and the tokens sit in a file on disk.

**Verdict:** do not build the interface on it. Its one real strength is that the launched process can take focus. I would not register a protocol for that alone.

---

## 5. Comparison

| | S: system browser | P1: Chromium, own pipe | P2: node + playwright-core | L: file + protocol | WebView2 (b) | Native, trimmed (b) |
|---|---|---|---|---|---|---|
| Exe size | +94 to +100 KB (m); bare socket +13 KB | +41 KB (m) | about 0, plus a script (e) | about 0 (e) | +93 KB | about 0 |
| Other disk | none | 433.6 MiB tree, 207.3 MB download; profile 8 to 13 MB (m) | same as P1 | none | runtime, preinstalled on Windows 11 | none |
| Memory while open | listener 3.3 MB (m) plus one tab (not measured) | 228 to 241 MB, 8 to 9 processes (m) | 375 to 417 MB, 10 to 11 processes (m) | one tab | about 145 MB, 7 processes | small |
| Start-up | listener 3 ms (m); tab not measured | 332 to 404 ms warm (m) | 683 to 705 ms (m) | a tab; each click starts a process | 327 to 562 ms | instant |
| New attack surface | loopback listener while a page is open; the browser's extensions | none on the network | none on the network | a permanent protocol handler; tokens in a file | none | none |
| Dependencies | any browser | provisioned tree; two Chromium switches; a debugging policy | node, playwright-core, a process under the install root | protocol registration | Evergreen runtime (154.0.4258.48 here) | none |
| Focus, second start | starter opens the tab (not measured) | grant chain to the browser (not measured) | as P1, one more hop | launched process has focus | as today | as today |
| Folder choice | in the page | native dialog owned by the window (not measured), or in the page | as P1 | native, focused | native, owned | native, owned |
| During an update | tab stays and can resume (m) | closes in 133 ms, reopens in about 0.35 s (m) | killed by the apply; node counts in the gate (r) | page goes stale | closes and reopens | closes and reopens |
| Tests | in-process HTTP, no desktop | fake pipe peer plus one private-desktop arm | script plus private desktop | text | HTML, bridge, one private-desktop arm | pure functions |
| Product lines | +1,500 to 2,400, -2,200 to 2,400 (e) | about +1,500 to 2,400 new, about 2,000 moved to Core, fallback kept (e) | as P1 plus the script (e) | +600 to 1,000 (e) | +1,500 to 2,300 (b) | +950 to 1,800 (b) |

P3 is left out as not sensible. Avalonia (b): about +25 MB and three DLLs.

---

## 6. Recommendation

**Build S**, with these structural choices:
- a raw socket;
- the address as the only secret;
- one gate;
- no cookies or storage;
- bare 404 for everything else;
- a listener that exists only while a page is open;
- the same-user check;
- the folder chosen in the page.

Write the HTML builder and the actions so they do not know their host. Changing the host later then costs only the transport.

**On WebView2, plainly.** It is still the stronger choice on two counts:
- **No listener.** Nothing for a web page, an extension or another user to reach.
- **It keeps what phases 2 and 3 already settled.** A second start raises a window, the toast opens a window, and the picker is owned.

S is stronger on code size, dependencies and tests. The gap is small either way.
- If your dislike of WebView2 is its machinery and the Microsoft runtime, S answers it.
- If it is "a browser engine inside my app", P1 has the same shape with more parts: our own protocol client, a 434 MiB dependency that is absent at first run, an infobar held off by a switch, and a sweep exception.

I would not build P1, P2, P3 or L.

**Costs of S:**
- the line counts in 2.7;
- one bounded wait in a coordinator documented as having no timer;
- new interop for the same-user check;
- `.html`, `.css` and `.js` files joining the SPDX and wording scans.

**Risks of S:** those in 2.6, plus the tab warts in 2.3.

---

## 7. What I could not verify

- **Anything on the real desktop:**
  - whether the tab comes to the front;
  - how a P1 window looks on the taskbar, and whether an app id set from outside is honoured;
  - focus of any picker;
  - the toast click.
- **Anything across users:** one account.
- **Stock browsers:** stock Firefox 157, Chrome stable and Edge. `Sec-Fetch-Site: none` for a shell-opened address is inferred from browser-initiated navigations.
- **Cold start after a reboot.** Every timing is from a 32-core, 128 GB machine.
- **BrowserAI.exe with any of this compiled in:** built, never run.
- **The repository's analyzers at error severity.** The app copy was built with the first report's relaxed switches, and the prototypes use `DllImport`. Zero banned symbols and zero trim or AOT warnings are established; the full analyzer set is not.
- **Whether `--disable-infobars` and `--remote-debugging-io-pipes` stay supported upstream.**
- **A harness oddity.** In 9 of 31 Firefox runs, Playwright's `goto` never resolved on a later tab. In the two runs with a diagnostic, the page reported itself complete and connected. I treat it as a harness fault, unexplained.

---

## 8. Open questions for the maintainer

**Q1. Which host carries the interface?**
- *Primer:* the task dialog has outgrown itself and you rejected WebView2.
- (a) System browser tab (S).
- (b) Provisioned Chromium as an app window (P1), with a fallback for first run.
- (c) WebView2.
- (d) Native, trimmed.
- (e) S now, with the page host-neutral.
- **Recommendation: (e).**

**Q2. How does the page prove it is yours?**
- *Primer:* something must tell your tab from any other client of the port.
- (a) The address is the secret.
- (b) A one-time code traded for a secret the script holds.
- (c) A cookie.
- **Recommendation: (a).** Never (c): cookies are shared across every 127.0.0.1 port.

**Q3. Other local users.**
- *Primer:* TCP has no per-user access list.
- (a) Rely on the token.
- (b) Add the same-user check: about 160 lines and two new native libraries.
- (c) Both, and state the residual in HAZARDS.
- **Recommendation: (c).**

**Q4. The coordinator's wait.**
- *Primer:* a reload leaves zero pages for 5 to 135 ms, so the coordinator must wait before it concludes the page is gone.
- (a) 30 s for a first connect, 5 s for a reconnect.
- (b) One value.
- (c) No wait, and a reload shows an error.
- (d) Stay resident.
- **Recommendation: (a)**, recorded as the one named exception to "No timer".

**Q5. One tab or many?**
- *Primer:* a second Start Menu click can only open a new tab; a page cannot raise an existing one.
- (a) Newest tab wins and the older closes itself.
- (b) Allow several.
- (c) Refuse the second.
- **Recommendation: (a).**

**Q6. Across an update.**
- *Primer:* the restarted app can take its old port and token (12 of 12 measured).
- (a) Resume in the same tab, falling back to a new one.
- (b) Always a new tab.
- (c) No tab after an update.
- **Recommendation: (a).**

**Q7. Choosing a folder (Q311).**
- *Primer:* see 2.4.
- (a) Started-in folder plus a pasted path.
- (b) A folder browser in the page.
- (c) A `browserai:` link to a focused native picker.
- (d) A native picker that opens behind the browser.
- **Recommendation: (a) now, (b) if you want a picker.**

**Q8. The toast's "Review" button (phase 3).**
- *Primer:* the clicked process must open the address and needs the foreground right. Q254 left that unmeasured, and I could not measure it either.
- (a) Measure it on your desktop before phase 3 is built.
- (b) Have the toast open the http address directly, which keeps a listener alive as long as the toast.
- (c) Use a protocol activation.
- **Recommendation: (a).**

**Q9. The task dialog's hyperlink finding** (first report Q-B6). Still open. S replaces that code, but the dialog stays in service until then.

---

## 9. Findings outside the question

- **Drift:** npm `latest` for `@playwright/mcp` is 0.0.83; the payload carries 0.0.82.
- **Registry residue:** `HKCU\Software\Mozilla\Firefox\Launcher`, the key your own Firefox uses, held 50 values. Twenty-five of them name five BrowserAI-provisioned Firefox revisions (1539, 1542, 1544, 1548, 1549). Every Firefox build leaves five values per executable path and nothing removes them. No tracked document records this.
- **Listener severity:** the first report recommended HttpListener for a browser tab; Microsoft now advises against it. It also rated "an endpoint accepts a path" as code execution; the command written is always BrowserAI's own.
- **Newer versions since the first report:** WebView2 SDK 1.0.4258.31, Velopack 1.2.161, Firefox 157.0 (released 2026-09-29).

---

## 10. Residue and incidents

- **Registry:** the scratch Firefox created five values under `HKCU\Software\Mozilla\Firefox\Launcher`. I removed exactly those five; the other 45 are untouched. Chrome for Testing also wrote its `StabilityMetrics` key, as every session browser does.
- **Network:** browser runs outside the sink made their usual background requests for the few seconds each lasted. Those were `h1`, `a1`, `a2`, `s1`, `s2`, `p2-1` to `p2-6`, `rc0` and `rc2`. In `n0` and `n1`, two DNS queries for `www.google.com` reached the system resolver.
- **A read-only check went too wide:** one `find` walked your real Firefox profile folder and printed a profile folder name into my tool output. I stopped it. Nothing was written.
- **NuGet:** four metadata files in the global HTTP cache were refreshed; no package was downloaded.
- **End state:**
  - no process from scratch is running (checked by path);
  - 63 private desktops each reported closed;
  - `git status` is clean;
  - the real `ms-playwright` folder was not written by me.
- **Scratch left:** 396 MB, of which 349 MB is `b2\browsers\firefox-1549`.

---

## 11. Sources (all read 2026-10-01)

- **Chromium `main` at `41d4e764`** (raw.githubusercontent.com/chromium/chromium): `devtools_pipe_handler.cc`, `devtools_agent_host_impl.cc`, `content_switches.cc`, `remote_debugging_server.cc`, `chrome_devtools_manager_delegate.cc`, `infobar_utils.cc`, `chrome_for_testing_infobar_delegate.cc`, `web_app_startup_utils.cc`, `startup_browser_creator.cc`, `external_protocol_handler.cc`, `generated_resources.grd`.
- **Firefox `main`** (raw.githubusercontent.com/mozilla-firefox/firefox, commit not recorded): `ContentDispatchChooser.sys.mjs`, `handlerDialog.ftl`, `nsGlobalWindowOuter.cpp`, `BrowsingContext.cpp`, `StaticPrefList.yaml`, `all.js`, `nsWinRemoteClient.cpp`.
- **Velopack `develop`:** `src/lib-csharp/VelopackApp.cs`.
- **Microsoft Learn:** SetForegroundWindow; Registering an Application to a URI Scheme; Process Security and Access Rights; the HttpListener class (.NET 10).
- **Versions:**
  - googlechromelabs.github.io/chrome-for-testing/last-known-good-versions.json (stamped 2026-10-01T09:25Z);
  - registry.npmjs.org dist-tags for `@playwright/mcp` and `playwright-core`;
  - product-details.mozilla.org;
  - api.nuget.org flat container.
- **Firefox Local Network Access history:** github.com/mdn/browser-compat-data/pull/30685 (merged 2026-09-28).
- **Repository:** `Coordinator.cs`, `Program.cs` of the app, `Coordination\*`, `StraySweep.cs`, `JobLauncher.cs`, `ProvisionedBrowsers.cs`, `RevisionPrune.cs`, `BrowserProvisioner.cs`, `SessionManager.cs`, `PayloadLayout.cs`, `BrowserProcesses.cs`, both `BannedSymbols.txt`, `ForbiddenDependencyTests.cs`, `README.md`, `DECISIONS.md`, `ARCHITECTURE.md`, `TODO.md`, `kb\packaging\velopack.md`, the payload's `coreBundle.js`, and the two earlier reports.

## 12. Evidence

All under `C:\Source\SixFive7\BrowserAI\.work\zoomout\b2\`:
- `notes\index.txt` (what each file is) and `notes\hardened-flags.txt`
- `probes\p1probe\`, `probes\sprobe\`, `stest\`, `driver\`
- `ffcheck.mjs`, `resume.mjs`, `p2.mjs`, `fuzz.mjs`, `netlog-summary.mjs`, `netlog-bytes.mjs`
- `run\*.result.txt`, `run\*.sink.tsv`, `run\*.netlog-summary.tsv`
- `run\a1-window0.png`, `run\a2-window0.png`, `run\a1-icon0.png`
- `logs\run-*.log`, `logs\publish-app-summary.txt`
- `appcopy\`, `src-chromium\`, `src-firefox\`, `src-velopack\`