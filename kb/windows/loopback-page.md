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
inside a kill-on-close job. *Corrected 2026-10-12 by addition (previously the
sentence above, without an exception): the measurement of what a folder link
brings to the front, taken 2026-10-11 for the maintainer's 55, ran on his
desktop by its nature, in a scratch Firefox profile, and the windows it opened
were the rig's own.*

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
  one minute covers. *Corrected 2026-10-12 by addition: since the maintainer's
  69 d the listener stays up while the background runs, so no minute is counted
  after the last tab, and nothing relies on the gap.*
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
  *Corrected 2026-10-12 by addition (previously "nothing relies on this either
  way"): the maintainer's 69 b relies on the first half since. A listener that
  starts takes the port the last one held when it is free, so a browser's
  "Always allow" for the address outlives a restart of the background, and the
  open page reconnects to it by itself.*
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
  a CORS header. *Corrected 2026-10-12 by addition (previously the sentence above,
  without an exception)*: since the maintainer's 74 a, a page opened by the
  address with the token is answered `303 See Other` with an empty body,
  `Set-Cookie: browserai-<port>=<token>; Path=/; HttpOnly; SameSite=Strict` and
  `Location` the same address without the token, and reaches no route; a target
  with no token is admitted by that cookie with every other rule kept, and is
  refused with no cookie, another port's, a wrong token in it, `Sec-Fetch-Site`
  `cross-site` or `same-site`, a rebound `Host`, or a write with no `Origin`. No
  other answer carries `Set-Cookie`.
- **A 404 with an empty body, and no route reached**: no token; a wrong token of
  the right length; the token with no closing slash, behind a dot segment, or
  percent-encoded; an absolute-form target; `Host: localhost`; an attacker's
  `Host`; HTTP/1.0 with no `Host`; another `Origin` on a read or a write; a write
  with no `Origin`; a form content type; a body one byte over 64 KB; a chunked
  body; a preflight; `PUT`; a route that does not exist.
- ⚠️ **The body one byte over 64 KB is declared and not sent since 2026-10-13**,
  *added that day by addition*, because sent with the request its `404` did not
  always reach the client. The gate refuses that request on its `Content-Length`
  and answers before reading the body, and Kestrel then closes the connection, as
  the request asks. Sent with its 65,537 bytes, 400 times through the arm's raw
  client, the request came back empty 48 times and never as anything but `404`;
  400 more, read to the end, ended 287 times in `SocketError.ConnectionReset`
  after the `404`, 43 times in it with nothing read, and 70 times in an orderly
  close after the `404`. Bytes of the body still arriving or unread at the close
  make Windows answer it with a reset, and a reset discards what the client has
  not read. The header block alone, declaring the same length, was answered `404`
  and closed in order 400 times of 400. Measured from the test host over the
  product at `44368995`, under the versions above, read again that day. A
  browser whose write is refused for its size can see a failed request where the
  `404` would be, and the refusal is the same either way. **Re-establish** the
  loss by sending `RawHttp.Post` with a body of `PageGate.MaximumBody + 1` bytes
  to a `PageListener` 400 times and counting the empty answers; the arm itself
  holds the other half, through `RawHttp.PostDeclaring`.
- **Answered by Kestrel before the gate, empty, and no route reached**: two
  `Host` headers, `400`; an HTTP/1.1 request with no `Host`, `400`; a header
  block over the 8 KB limit, `431`; bytes that are not HTTP, `400`. The
  prototype's raw socket answered these with its own `404`.
- ⚠️ **The header block over the limit loses no answer: measured 2026-10-13**,
  *added that day by addition*, for the maintainer's 158 a, after
  `PageListenerTests.WhatKestrelRefusesItselfIsAnsweredEmptyAndReachesNoRoute`,
  which sends it, was suspected of the reset that lost the body's `404`. A header
  block of `PageListener.MaximumHeaderBytes` and 1,024 bytes more, sent 400 times
  through the arm's raw client, was answered an empty `431` 400 times of 400; 400
  more, read to the end on a socket of their own, closed in order 400 times, with
  no `SocketError.ConnectionReset`, and no route was reached. Measured from the test
  host over the listener as it stands at `40911cbb`, under the versions above, read
  again that day. **Re-establish** by sending `RawHttp.Get` with an `X-Padding`
  header of `PageListener.MaximumHeaderBytes + 1024` bytes to a `PageListener` 400
  times and counting the answers that are not an empty `431`, then 400 more on a
  raw socket read to the end, counting the resets.
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
- *Added 2026-10-12 by addition, measured by the same arm since the maintainer's
  74 a.* **Both engines follow the swap**: a tab opened by the address with the
  token shows the address without it, `document.cookie` holds no token, because
  the cookie is `HttpOnly`, and the page's event stream and its writes go through
  the cookie. And the page draws its folder buttons as links of the install's
  scheme with the token in them, the one line about the browser's question under
  the first, in both families; no link is followed, since the suite registers no
  scheme for one to start.

**Re-establish** with the two arms of `PageBrowserTests`, which hold that the
replaced page asks to close and accept either answer from the engine; the
history readings are a one-off assertion `() => history.length` in the tab the
arm opens, and the hang is the arm's own first form, which waited for the tab to
go.

## A folder link comes to the front when the process the link starts opens it -- measured 2026-10-11

`[FLOATS]` **In Firefox 157.0.1, a link of a scheme registered under
`HKCU\Software\Classes`, clicked on a page served from `127.0.0.1`, is started
by Explorer, and a folder window or Windows' folder picker that the started
process opens itself comes to the front of the browser. The picker shown by
another process stays behind the browser, also when the started process has
handed that process the right to come forward.** Measured 2026-10-11 on Windows
11 Pro 10.0.26300, in the maintainer's installed Firefox 157.0.1 with a scratch
profile, for his 55; the decision taken from it is
[75 a](../../DECISIONS.md#the-folder-links-decided-2026-10-11). The rig
registered the scheme `browserai-probe` for the run and removed it after, served
a page with one link per shape, and clicked each link inside the page through
WebDriver BiDi, which moves neither the mouse nor the keyboard. An observer
recorded every window that appeared, its owner, and the window that held the
foreground. A stand-in for the background, a process of the rig's own that its
observer started once the browser was in front, answered the started process over
a named pipe.

| Link | What the started process did | The window | Where it came |
|---|---|---|---|
| A | opened the folder itself, `ShellExecuteW` with `explore` | Explorer's folder window | **in front**, 867 ms after the started process began; clicked again with that window still open, the same window came forward in 100 ms |
| C | showed the picker itself, `SHBrowseForFolderW` with no owner | the picker | **in front**, 274 ms |
| E, the control | asked the stand-in to open the folder and handed it nothing: the stand-in's `AllowSetForegroundWindow` on itself failed with error 5 | Explorer's folder window | in front, 695 ms |
| B | handed the stand-in the right with `AllowSetForegroundWindow`, which succeeded, then asked it to open the folder | Explorer's folder window | in front, 570 ms |
| D | handed the stand-in the right, which succeeded, then asked it to show the picker | the picker | **behind the browser**, which kept the foreground until the picker was cancelled |

- **Every started process had `explorer.exe` for its parent**, 6 of 6, and began
  53 to 71 ms after its click, 362 ms for the first click of the run. Firefox
  does not start the handler: it calls `CoAllowSetForegroundWindow` on Explorer
  and hands the link to it (`ShellExecuteByExplorer`), read in its source at
  `FIREFOX_157_0_1_RELEASE` (`0c469c23`), `uriloader/exthandler/win/nsMIMEInfoWin.cpp`
  and `widget/windows/ShellHeaderOnlyUtils.h`.
- ⚠️ **The control did not show the fault 55 was asked about.** E is what the page
  did then, a folder opened by a process with no right to come forward, and its
  folder came to the front: Explorer opened it, for a stand-in the rig's observer
  had started, and the observer was started from a shell. The background is started by the Task Scheduler, and
  [a process a task starts may not take the foreground](processes.md#a-process-a-task-starts-may-not-take-the-foreground);
  a stand-in started that way was not measured. So this says what comes forward
  for the process a link starts, and why the background's folder stays behind is
  not established here.
- **Whether Firefox asks again on another port was not measured.** The scratch
  profile had the prompt switched off for the probe's scheme, and the step that
  would have opened the page again on its first port with a new path timed out
  in the driver. Firefox's source answers it: *Always allow* is stored as the
  permission `open-protocol-handler^<scheme>` on the page's principal, with no
  expiry (`toolkit/mozapps/handling/ContentDispatchChooser.sys.mjs`), and the
  permission manager keys every type but two storage types by the full origin,
  port included (`extensions/permissions/PermissionManager.cpp`). A new port is a
  new origin and is asked again; a new path on the same port is not. This is what
  [69 d with fallback b](../../DECISIONS.md#the-folder-links-decided-2026-10-11)
  rests on.
- The rig's attempt to bring Firefox forward before each click failed every time:
  its `focus.ps1` declares `GetCurrentThreadId` from `user32.dll`, which does not
  export it. The browser the driver started was in front for each click without it.

Evidence: [`docs/evidence/2026-10-11-link-start/`](../../docs/evidence/2026-10-11-link-start/README.md),
the observer's record, each started process's log, the stand-in's, and the
report the rig's `stop.ps1` wrote. **Re-establish** with
[the rig](../../docs/probes/2026-10-11-link-start/README.md), in a scratch Firefox
profile; a new Firefox or Windows build is a new measurement.

## The conversations page's map at forty, a hundred and two hundred rows -- measured 2026-10-13

`[MACHINE]` The page as the maintainer's request of 2026-10-13 left it, which draws its map at
every count ([the decision](../../DECISIONS.md#the-map-at-any-number-of-conversations-decided-2026-10-13)),
served by the suite's page rig with invented conversations and sessions and read in a headless
browser of the provisioned set through BrowserAI's own tools, at a window of 1440 by 1170:
`chromium-1247` (Chrome for Testing 155.0.8059.12) and `firefox-1553` (Firefox 156.0), with
`playwright-core` 1.64.0-alpha-1790635538000, `@playwright/mcp` 0.0.83, and BrowserAI
2.0.1-alpha.0.46 built from the tree of `fbbd0e91`. A **redraw** is the script's work on a window
resize: the line samples, the lines and their words, and the inspector's pointer. A **replace** is
the page's main part set again from its own markup and redrawn, with the layout after it, which
is what a new state costs a tab less the reading of the event.

| Rows | Sessions | Lines | Words on lines | Elements | Main part, Chromium and Firefox | Page height |
|--:|--:|--:|--:|--:|--:|--:|
| 40 | 63 | 122 | 67 | 8,409 | 547,875 and 558,059 characters | 5,854 px |
| 100 | 158 | 302 | 167 | 20,706 | 1,350,997 and 1,376,275 characters | 13,705 px |
| 200 | 317 | 601 | 334 | 41,264 | 2,695,707 and 2,744,714 characters | 26,815 px |

The median and the slowest of seven runs, in milliseconds; Firefox's clock read whole
milliseconds here:

| Rows | Chromium redraw | Chromium replace | Firefox redraw | Firefox replace |
|--:|--:|--:|--:|--:|
| 40 | 13.3, 14.2 | 36.6, 55.7 | 26, 28 | 61, 68 |
| 100 | 37.4, 52.1 | 101.6, 129.3 | 65, 75 | 150, 165 |
| 200 | 96.8, 118.9 | 235.8, 265.0 | 147, 170 | 294, 309 |

Firefox's page heights were within 4 px of Chromium's, and the script marked the map tall at
every count above.

⚠️ **A gradient sized with `min()` drew nothing in Chromium.** The column's background was first
`radial-gradient(120% min(70%, 320px) at 50% 0%, ...)`: Firefox drew the card and its glow, and
Chromium 155 drew no background for the column at all, so the map's own colour showed through it
and the grey of BrowserAI's three counts vanished into it, in every Chromium screenshot of the
first run. With the size written `120% 320px`, both engines drew it. What in the declaration
Chromium refused was not isolated further.

**Re-establish** with [the rig](../../docs/design/map-at-any-count-2026-10-13/tools/TallMapShots.cs):
copy it into `tests/BrowserAI.Tests`, publish the tree, run the suite filtered to its class under
the suite lock, and read `.work/shots/timings-chromium.txt` and `timings-firefox.txt`. A new
browser, a new machine or a change to the page is a new measurement.

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
