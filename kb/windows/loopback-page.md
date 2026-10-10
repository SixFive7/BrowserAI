<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# A management page on loopback, and the windows that could show it

What was measured while choosing how to replace the configuration window: what
each .NET listener answers on `127.0.0.1` by default, a listener with one gate
attacked from raw sockets and from two real browsers, how a tab behaves across a
reload, a close and a restart of the process behind it, Chrome for Testing's own
traffic while idle, and the alternatives that were turned down. The decision is
[the management interface is a tab in the system browser](../../DECISIONS.md#the-management-interface-is-a-tab-in-the-system-browser),
and its listener is Kestrel from the empty builder (Q340). Two passes of the
zoom-out's track B: the first on **2026-09-25** and the second on **2026-10-01**,
on .NET SDK 10.0.401 with runtime packs 10.0.12, on Windows 11, build 26200.9550
as the first pass recorded it and 26300 as the second did.
[Evidence](../../docs/evidence/2026-09-25-management-interface/README.md),
[rigs](../../docs/probes/2026-09-25-management-interface/README.md). Nothing in
this article ran on the real desktop: every window was on a private desktop
inside a kill-on-close job.

## What a .NET listener on 127.0.0.1 answers by default -- measured 2026-09-25

`[FLOATS]` **Every stack bound to `127.0.0.1` answered a DNS-rebinding `Host`
header**, so a listener a browser can reach needs its own `Host` check whatever
it is built on. Five NativeAOT probes each served a status document on an
ephemeral port, and a raw socket sent each request with its headers exactly as
written:

| Request | Raw socket | HttpListener, `127.0.0.1` prefix | HttpListener, `localhost` prefix | Kestrel, empty builder | Kestrel, slim builder |
|---|---|---|---|---|---|
| `Host: 127.0.0.1:P` | 200 | 200 | **400** | 200 | 200 |
| `Host: attacker.example:P`, the rebinding case | **200** | **200** | 400 | **200** | **200** |
| HTTP/1.0 with no `Host` | 200 | 200 | 400 | 200 | 200 |
| The same port on `[::1]` | refused | refused | 400, listening | refused | refused |
| The same port on this machine's LAN address | refused | refused | **400, listening** | refused | refused |
| As above, with `Host: localhost:P` | not run | refused | **200** | not run | not run |

- **The one setup that refused the rebinding listens on every interface**:
  http.sys with a `localhost` prefix answered a request sent from this machine
  to its own LAN address. Microsoft documents an IP prefix as an *IP-bound weak
  wildcard* and says not to rely on it for security
  ([UrlPrefix Strings](https://learn.microsoft.com/windows/win32/http/urlprefix-strings),
  read 2026-09-25).
- From a non-elevated token the `+` and `*` prefixes were refused with *Access is
  denied*, and a second process of the same user could not register a longer
  prefix on a port the first held.
- **What each costs inside a copy of `BrowserAI.exe`**, published and never run,
  against 10,914,304 bytes unchanged: raw socket +12,800, HttpListener +163,328,
  Kestrel empty builder +3,490,816, Kestrel slim builder with minimal APIs
  +6,719,488. Standalone, the four server probes were listening 46.0 to 82.2 ms
  (p50) after `Process.Start`, the slim builder slowest, against 39.7 ms for a
  probe that serves nothing.
- ⚠️ **Minimal APIs cannot build in this repository**: the generated route code
  calls `Debug.Assert`, which `src/BrowserAI/BannedSymbols.txt` bans, so `RS0030`
  fires twelve times, and the ASP.NET Core framework reference raises `NU1510` on
  the app's explicit `Microsoft.Extensions.Logging` reference. Both are errors
  under this repository's settings, which is why Q340 took plain handlers.

Evidence: `first-pass/logs/security-1.txt`, `security-2.txt`, `measure-*.tsv`
and the `publish-*` summaries. **Re-establish** with the first pass's probes:
`publish-all.sh`, then each probe under the driver, as
[the rig](../../docs/probes/2026-09-25-management-interface/README.md) describes.

## A listener with one gate, against two browsers -- measured 2026-10-01

`[FLOATS]` A raw-socket prototype on `127.0.0.1`, at
`http://127.0.0.1:<port>/<token>/` with a 256-bit token per listener, ran one
gate before any route: GET or POST only, exactly one `Host` equal to
`127.0.0.1:<port>`, the token compared in constant time, `Sec-Fetch-Site` same
origin or none when present, and on a POST an exact `Origin`, a JSON content type
and at most 64 KB. Anything else got the same bare `404`. Driven headless and on
private desktops with **Chrome for Testing 154.0.8037.0** (chromium-1246) and
**Playwright's Firefox 156.0** (firefox-1549), through playwright-core
1.64.0-alpha-1789764292000 on node v24.21.0. The product's listener is Kestrel,
so these results are about the gate's rules and the browsers' behaviour, and
Kestrel's own parsing was not attacked.

- **Raw requests: 24 of 24 as designed.** The page and a write were admitted;
  no token, a wrong token, `Host: localhost`, an attacker `Host`, no `Host`, two
  `Host` headers, a foreign `Origin`, a write with no `Origin`, a form content
  type, a preflight, `PUT`, a 9 KB header, an oversized body, a chunked body and
  bytes that are not HTTP all got `404`, and `[::1]` and the LAN address refused
  the connection.
- **DNS rebinding in a real browser, with the token already leaked**: both
  browsers, told that `attacker.example` is `127.0.0.1`, were refused on `Host`.
- **Another site that knows the whole address**: seven request shapes in both
  browsers (no-cors POST and GET, a CORS GET, a JSON POST, an iframe, a form POST
  and a WebSocket) were refused, and the write counter did not move.
- **4,000 mutated requests**: the listener stayed up. 2,465 got `404`, 783 were
  dropped, and the 760 answered were mutations that had left the request valid,
  8 of 8 sampled; 200 writes were admitted and the counter read 200.
- No cookie was set and nothing was stored: `document.cookie` empty and both
  storages empty, in both browsers.
- **A same-user check** that finds the connecting pid in the TCP table and
  compares its token user admitted 88 of 88 Chromium connections and every
  Firefox one, at 0.29 to 0.73 ms each. Q335 did not take it.
- Inside a copy of `BrowserAI.exe`, never run: the whole prototype +94,208 bytes,
  +99,840 with the same-user check, and 0 trim or AOT warnings; standalone it was
  listening 3 ms after `Main` and held 3.3 MB private while idle.

### A tab across a reload, a close and a restart

- **A closed tab ends its event stream at once**: the listener saw it 20.8 and
  212 ms later in Chromium and 24 to 45 ms later in Firefox (28 closes).
- **A reload leaves no page connected for a moment**: 4.8 to 11.3 ms in Chromium
  over 20 reloads and 19 to 135 ms in Firefox over 310. This is the gap Q336's
  one minute covers.
- A second tab on the same address stays connected beside the first, and a page
  can close its own tab while its history holds one entry, in both browsers.
- **A restarted listener takes the same port back**: in 12 of 12 rounds a new
  process bound the old exclusive port at the first attempt, 36 to 46 ms after
  the old one was killed. The open page reconnected by itself at its event
  stream's retry interval, about 3.0 s after the old process exited in Chromium
  and 5.0 to 5.3 s in Firefox, also when the new listener was up only 2.5 s
  later, and its next write worked. Q338 took a new tab after an update, so
  nothing relies on this. *Corrected 2026-10-10 by addition: the update toasts of
  2026-10-08 superseded that tab, and since the maintainer's "20 nothing except for the
  toast" no tab opens after an update at all; nothing relies on this either way.*
- ⚠️ In 9 of 31 Firefox runs Playwright's `goto` never resolved on a later tab;
  the two with a diagnostic showed the page loaded and connected. Read as a fault
  of the harness, and not explained.

Evidence: `second-pass/run/s1`, `s2`, `f*`, `rc*`, `rf*` and the zipped fuzz
log `z1`. **Re-establish** with `stest`, `ffcheck.mjs`, `fuzz.mjs` and
`resume.mjs` against `probes/sprobe`; a new browser build is a new measurement.

## The product's listener under attack -- measured 2026-10-03

`[FLOATS]` **The listener BrowserAI ships is Kestrel from ASP.NET Core's empty
builder (Q340 b), and its one gate refuses every shape the prototype's refused,
with a 404 that carries nothing; what Kestrel cannot parse it answers itself,
before the gate is asked.** Measured from raw sockets against
`BrowserAI.App.Page.PageListener` running in the test host, ASP.NET Core and the
.NET runtime 10.0.12, SDK 10.0.401, Windows 11 build 26300, by
`PageListenerTests` on every run:

- **Admitted**: the page with the token and `Host: 127.0.0.1:<port>`, and a JSON
  write carrying our `Origin` and `Sec-Fetch-Site: same-origin`. Each answer
  carries the fixed security headers, and none carries `Server`, `Set-Cookie` or
  a CORS header.
- **A 404 with an empty body, and no route reached**: no token; a wrong token of
  the right length; the token with no closing slash, behind a dot segment, or
  percent-encoded; an absolute-form target; `Host: localhost`; an attacker's
  `Host`; HTTP/1.0 with no `Host`; another `Origin` on a read or a write; a write
  with no `Origin`; a form content type; a body one byte over 64 KB; a chunked
  body; a preflight; `PUT`; a route that does not exist.
- **Answered by Kestrel before the gate, empty, and no route reached**: two
  `Host` headers, `400`; an HTTP/1.1 request with no `Host`, `400`; a header
  block over the 8 KB limit, `431`; bytes that are not HTTP, `400`. The
  prototype's raw socket answered these with its own `404`.
- **The socket**: the same port on `[::1]` refused the connection. A second
  socket of the same user binding `127.0.0.1` and the port was refused, with
  `WSAEACCES` when it asked to share and `WSAEADDRINUSE` when it did not, and a
  wildcard bound to the same port succeeded beside it and received no
  connection made to `127.0.0.1`.
- ⚠️ **Exclusive use is asked for and its effect was not seen**: a listening
  socket of our own on `127.0.0.1`, bound with and without
  `SO_EXCLUSIVEADDRUSE`, got the same answer to every second bind above in both
  cases, so on this build the refusal is Windows' default and the flag adds
  nothing a same-user test can observe. What another Windows user can do is not
  measured: one account.
- **Size**: the published `BrowserAI.exe` with the page is 14,852,096 bytes,
  against 10,987,520 for the same SDK's publish of `798aa5b`, whose app code is
  the code this change was rebased onto: +3,864,576, against the 3,490,816 the first pass measured for
  Kestrel's empty builder alone.

**Re-establish** with the four arms of `PageListenerTests`, which are the
measurement and run on every build, the two sockets of our own included; the
size is two publishes, one either side of the change.

### The page in two real browsers -- measured 2026-10-03

`[FLOATS]` **In Chrome for Testing 155.0.8059.12 (`chromium-1247`) and
Playwright's Firefox 156.0 (`firefox-1553`), driven headless through a BrowserAI
session, the page's script ran under its own content security policy, its event
stream connected, a click posted through the gate and its answer came back into
the page, and a tab a newer one replaced called `window.close()`.** The write
passing the gate is the measurement of what each browser's `fetch` sends: our
exact `Origin`, `Sec-Fetch-Site: same-origin` and `Content-Type: application/json`.
`PageBrowserTests` takes it on every run, once per family.

- **Whether the replaced tab then went was the engine's.** The HTML standard
  lets a script close a tab only when a script opened it or its session history
  holds one document. Firefox closed the replaced tab both ways the arm opened
  it: as the session's own first tab, and as a new tab given the address, whose
  `history.length` read 1. Chromium closed neither. Its new tab given the address
  read `history.length` 2, the blank page the tab began on and the page, and the
  session's first tab was still open when the arm's thirty-minute hang detector
  ran out. The page then says it was replaced, which is what it is written to do
  when the browser refuses.
- A new blank tab whose one entry a script replaced with the address never
  connected in either engine within the minute that run waited. The gate's
  answer to it was not read, so why is not established.
- **A tab the shell opens for a person's start was not measured**: the suite
  cannot open one. Whether Chromium closes the tab a real start opened is for a
  person's own browser to show.

**Re-establish** with the two arms of `PageBrowserTests`, which hold that the
replaced page asks to close and accept either answer from the engine; the
history readings are a one-off assertion `() => history.length` in the tab the
arm opens, and the hang is the arm's own first form, which waited for the tab to
go.

## Chrome for Testing calls Google while idle, and one switch stops it -- measured 2026-10-01

`[FLOATS]` Chrome for Testing 154.0.8037.0 on a local page for eight idle
minutes, behind a proxy that answered nothing:

| Switches | Requests | To |
|---|---|---|
| A minimal set | 29 | `clients2.google.com`, `accounts.google.com`, `android.clients.google.com`, `update.googleapis.com`, `safebrowsing.googleapis.com`, and two DNS queries for `www.google.com` that went around the proxy |
| The set in `second-pass/notes/hardened-flags.txt`, which the report calls Playwright's | 23 | `accounts.google.com`, `www.google.com`, `android.clients.google.com`, `update.googleapis.com`, `clients2.google.com` |
| That set and `--host-resolver-rules="MAP * ~NOTFOUND"` | **0** | no connection and no byte in 334 s; the same five addresses were tried and failed at name resolution |

⚠️ **Whether a session browser `@playwright/mcp` starts makes the same calls was
not measured**: the rig started the browser itself with that switch set, and
`@playwright/mcp` was not in the path. It is in
[not established](../not-established.md#browsers-and-the-web-surface).
Evidence: `second-pass/run/n0` to `n3`, each a proxy log and a summary of the
browser's net log; the net logs themselves are left out. **Re-establish** with
`run1.sh` and the driver, as the rig describes.

## The provisioned Chromium as a window of BrowserAI's own -- measured 2026-10-01

⛔ **Not taken (Q315)**: no browser exists on a fresh install until a session
provisions one. Kept because it shows what driving that Chromium without
Playwright takes. A NativeAOT process started chromium-1246 with
`--remote-debugging-io-pipes` over two anonymous pipes, passed with an exact
handle list to `CreateProcessW`, and served its page over the pipe with no port:

- the first reply came 266 to 314 ms after launch, and the page was ready 332 to
  404 ms after launch, in 8 to 9 processes holding 228 to 241 MB private;
- a page-to-host call took 0.20 to 0.31 ms, and a navigation elsewhere failed
  with `ERR_BLOCKED_BY_CLIENT`;
- the browser exited 133 ms after the host's pipe closed, and closing the window
  ended it in 120 to 200 ms;
- Chrome for Testing shows a permanent infobar saying it is only for automated
  testing unless `--disable-infobars` is passed;
- in-app cost +40,960 bytes, for a 195-line client and 124 lines of launch.

Driven through node and playwright-core instead, the same window took 683 to
705 ms and 375 to 417 MB in 10 to 11 processes, and node runs from the install
root, where an update has to stop it.

## WebView2 and Avalonia from NativeAOT -- measured 2026-09-25

⛔ **Not taken (Q315)**. A hidden window drove WebView2 through hand-declared
`[GeneratedComInterface]` interfaces, with the loader linked into the executable:
+93,184 bytes inside a copy of `BrowserAI.exe`. Standalone, a page was ready 327
to 338 ms after launch warm and 562 ms cold with the loader beside the executable,
and 341 to 385 ms with it linked in, in 7 processes holding 143 to 146 MB private,
on the Evergreen runtime 153.0.4234.48. When the host exited, 5 WebView2 processes were still
running, from the runtime's folder, where neither Velopack nor the coordinator
looks. Avalonia 12.1.3 published a minimal window as 36,361,768 bytes in 4 files,
and the first pass found that its build sends telemetry unless it is switched off.
WinUI 3 was not built: the first pass quoted Microsoft Learn that it cannot be
published as a single file.

## Every Firefox build leaves five values under its Launcher key -- measured 2026-10-01

`[FLOATS]` `HKCU\Software\Mozilla\Firefox\Launcher`, the key a person's own
Firefox uses, held 50 values on this machine; 25 of them named five
BrowserAI-provisioned Firefox revisions under the data root (1539, 1542, 1544,
1548 and 1549), five per executable path. Running Playwright's Firefox 156.0 from
a scratch folder added five more for that path, and nothing removes them; the
researcher removed exactly those five. This is
[a hazard row](../../HAZARDS.md#hazard-index) of its own. **Re-establish**:
export the key, run a Firefox build from a new path, export it again and compare.
