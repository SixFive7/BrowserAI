<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# BrowserAI's page, redesigned 2026-10-11

**Chosen 2026-10-11 by the maintainer, in his words verbatim: _"40-46 your
recommendations"_**, and built the same day. The seven numbers, 40 to 46, are the seven questions at the
end of the write-up below, in their order, and each was answered by
the direction recommended beside it. [The decision's rows](../../../DECISIONS.md#the-dashboard-decided-2026-10-11)
say what was built for each and which tests hold it; this directory is what it was chosen from
and what it looks like.

| Number | The question | The answer, the recommendation beside it |
|---|---|---|
| 40 | Which direction? | **f**: direction A, Overview and details, with C's live map as its conversations view |
| 41 | May a person delete a session folder from the page? | **a**: no; sizes shown, and *Open folder* to delete one in Explorer |
| 42 | Which words? | **a**: the mockups' words, conversation, app, session, browser and set up |
| 43 | Which new facts may the page show? | **b**: each session folder's size and the folders not open now, where updates come from and when BrowserAI last checked, and when the background started and how; not the agents' own reasons |
| 44 | The colour scheme | **a**: follow Windows' light or dark setting through the browser, and store nothing |
| 45 | Pids and command lines | **a**: folded under *Details for troubleshooting* on the setup and install pages |
| 46 | A live list of conversations and sessions | **a**: live, pushed on every change, with the keyboard focus kept where it was |

## What is here

| Path | What it is |
|---|---|
| `README.md` | This record: the choice, what was built and where it departs from the mockups, and the write-up the directions were put with |
| [`built/`](built/home-light.png) | The build's own page: screenshots of the product's composers and stylesheet with invented data |
| [`a-overview/`](a-overview/index.html), [`b-sidebar/`](b-sidebar/index.html), [`c-map/`](c-map/index.html), [`d-activity/`](d-activity/index.html), [`e-one-page/`](e-one-page/index.html) | The five directions: each a self-contained `index.html`, its parts in `src/` and its screenshots in `shots/` |
| [`current/`](current/status.html) | The page as it stood before, rebuilt from its stylesheet and the composers' markup, with its screenshots in `current/shots/` |
| `shared/` | The invented data and its four cases, the line icons, and the mockup-only control |
| `tools/` | `build.py`, which inlines each direction's parts into its `index.html`; `serve.py`, the server on 127.0.0.1 the mockups' screenshots were taken through; and `ScreenshotRig.cs`, the test the build's screenshots were served from, run once from the suite's project and taken out again |

## What was built

**The home** is direction A's overview: a headline that answers the main question, a notice for
each thing that needs the person, and five cards, *Right now*, *Update*, *Claude Code and Codex*,
*Storage* and *This install*, each leading to a page that begins with a way back. **The
conversations page** is direction C's map inside A's page: conversations on the left, BrowserAI in
the middle and browser sessions on the right, joined by lines, with an inspector beside the map
for the card chosen. Below 1200 pixels of window, or with more than eight rows or twelve sessions,
it is A's list. The other pages are A's: one session, Claude Code and Codex, storage, this
install, the update and what is new.

**The screenshots in [`built/`](built/home-light.png) are the build's own page**: the product's
composers and stylesheet, served by the product's `PageService` through the suite's page rig
with invented data, a busy afternoon of three conversations, four sessions and an update
waiting, and captured headless through BrowserAI's own tools. Every name, title, folder and size
in them is invented, as in the mockups.

| Screenshot | What it shows | Beside the mockup |
|---|---|---|
| [`built/home-light.png`](built/home-light.png) | The home at 1440x900 in Windows' light setting | `a-overview/shots/a-home-light.png` |
| [`built/home-dark.png`](built/home-dark.png) | The same in the dark setting | `a-overview/shots/a-home-dark.png` |
| [`built/conversations-map.png`](built/conversations-map.png) | The conversations page as a map, with nothing chosen | `c-map/shots/c-map-light.png` |
| [`built/conversations-inspector.png`](built/conversations-inspector.png) | The map with a session chosen and the inspector saying what it is | `c-map/shots/c-map-inspector.png` |
| [`built/conversations-dark.png`](built/conversations-dark.png) | The map in the dark setting | `c-map/shots/c-map-dark.png` |
| [`built/conversations-narrow.png`](built/conversations-narrow.png) | The same page at 390x844, where it is the list | `a-overview/shots/a-conversations.png`, `c-map/shots/c-map-narrow.png` |
| [`built/session.png`](built/session.png) | One session's page | `a-overview/shots/a-session.png` |
| [`built/update.png`](built/update.png) | The update page with an update waiting | `a-overview/shots/a-update.png` |
| [`built/setup.png`](built/setup.png) | Claude Code and Codex | `a-overview/shots/a-apps.png` |
| [`built/storage.png`](built/storage.png) | Storage | `a-overview/shots/a-storage.png` |
| [`built/install.png`](built/install.png) | This install, its details for troubleshooting open | none: A's install page had no screenshot |
| [`built/changelog.png`](built/changelog.png) | What is new, for the release 1.1.0 | none |
| [`built/home-narrow.png`](built/home-narrow.png) | The home at 390x844 | `a-overview/shots/a-home-narrow.png` |

## Where the build departs from the mockups

The mockups are renderings, not specifications (see [the index](../README.md)). Where the build
and a mockup disagree, the build is what was decided, and each place is named here with the reason.

1. **The words of a decision that came before the mockups stay.** Every setup button names its app, *Set up again for all my Claude Code projects*, where A's setup page wrote *Set up again*, because the maintainer asked on 2026-09-24 for separate control of each app and no button on the page applies to both. The update page's button keeps *Install BrowserAI 1.2.0 now*, its label since 2026-10-08, where A wrote *Install 1.2.0 now*.
2. **The update page with nothing waiting says *No update is waiting***, where A said *BrowserAI is up to date* with the newest version named: a check that failed also leaves nothing waiting, and the page says when BrowserAI last checked instead.
3. **The storage page lists the session folders alone.** A's storage page had cards for the browsers' and the logs' sizes, a *Last used* column and a column of what each folder holds, and 43 b let the page show the sessions' sizes and nothing more. Its summary cards are the folders together, those open now and those not open now.
4. **The inspector stands beside the map at every width the map is drawn at, with no close button**, where C's opened beside a card it was asked for: the map is drawn only where there is room for both, and a card chosen again, or Escape, clears it.
5. **The map has no tabs of its own.** C's page carried four tabs for the rest of the page; inside A, the way back and the home's cards do that.
6. **A session's page lists what its folder holds by its traces alone**, where A's listed the kinds of file a folder can carry, a transcript, a saved login, a network recording and the browser profile: 43 b passed no inventory of a folder to the page. For the same reason no page names a session's browser, Chromium or Firefox, nor why a browser that is not open was closed, which A's lines did.
7. **The mockup-only control is gone**, the dark box that switched the invented data and the scheme. The scheme is Windows' own (44 a), and the data is what the background holds.
8. **No colour carries a meaning alone**, as C asked: each line of the map carries a pattern, each card its state in words, and every chip its words beside its colour.
9. **The update page does not say when the update was downloaded**, which A's lede did: the update core passes the page no such moment, and 43 b named three facts and not this one.
10. **The update page keeps the sentences it had under each thing that holds the update**, the app, how it holds the update and the reconnect it will need, where A's items carried one line each; and it names each hidden browser's and window's purpose, which A left out.
11. **A session's page has no *Latest call* box**: that is the agents' own reason for a call, which 43 b left out because an agent writes it and it can quote what a page showed.
12. **A conversation's title is in quotes**, *"Fix the checkout total rounding"*, where the mockups wrote it bare: the maintainer's 1.2 a of 2026-10-10 put a title in quotes and BrowserAI's own words for an unnamed conversation as they are, so a person can tell the two apart.
13. **The map's hub column is headed *BrowserAI***, where C wrote *Where they run*, and a conversation in a terminal or in Codex has no group label of its own above it, where C drew one: only a VS Code window gathers its tabs under a label, as on the update page.
14. **No summary links under the map**: C's three links to the update, the setup and storage are the home's cards inside A.

---

## The write-up the directions were put with

*Kept as it was put to the maintainer on 2026-10-11, but for two sentences that named how the work was arranged and the report of the work at its end, which are left out.*

Written 2026-10-11 against master `3a64fbe8`. The page code last changed at `07ea5d60`, and the installed 1.1.1-alpha.0.335 was packed from `492238a7`, which carries the same page code. **Research and design only: nothing in the product changed.**

The maintainer's words, verbatim: *"I really do not like the dashboard. It is ugly and it does not explain what is what. Make it far more intuitive, beautifull and have it navigate differently and intuitively. Also, think what would be the most usefull on the opening page."*

### How to look at the mockups

Each direction is one self-contained file. Open it straight from disk in any browser; it loads nothing from the network.

| Direction | Mockup | Screenshots |
|---|---|---|
| A, Overview and details | `a-overview/index.html` | `a-overview/shots/` (11) |
| B, Settings app | `b-sidebar/index.html` | `b-sidebar/shots/` (11) |
| C, Live map | `c-map/index.html` | `c-map/shots/` (8) |
| D, Today | `d-activity/index.html` | `d-activity/shots/` (8) |
| E, One page | `e-one-page/index.html` | `e-one-page/shots/` (8) |
| The page today, rebuilt | `current/*.html` | `current/shots/` (8) |

A small dark box at the bottom right, marked **Mockup only**, switches the invented data between four cases (*Busy afternoon*, *Quiet, nothing running*, *Something needs you*, *Just installed*) and the scheme between System, Light and Dark. It is not part of any design. The address does the same: `?state=quiet`, `?state=attention`, `?state=firstrun`, `?theme=dark`. Every name, title, folder, size and time in the mockups is invented, and the countdowns run.

Screenshots were taken headless through BrowserAI's own tools at 1440x900, plus one at 390x844 per direction. The mockups use no inline style attribute, no event-handler attribute and no external URL (checked with a planted control), so their markup could be served under the page's content security policy as it stands; their CSS and script sit inline only because a mockup must be one file.

---

### 1. The page today

#### How the "before" pictures were made

The page is composed in C# as HTML strings (`PageContent`, `UpdatePageContent`, `ChangelogPageContent`), styled by a 26-line stylesheet in `PageAssets.Css`, and filled by one script that keeps an event stream open. **The screenshots in `current/shots/` are not captures of the installed BrowserAI.** They are the product's own stylesheet, copied verbatim from `PageAssets.Css`, over markup written to the exact shape the three composers emit, with invented data, served from scratch on 127.0.0.1. The script was left out: without the background it can only show its *not answering* banner, and with it, it replaces `<main>` with the same markup. A capture of the live page would need the installed `BrowserAI.exe` started with `--write-address`, which was not done for these pictures, and it would show the maintainer's real conversation titles.

| Screenshot | What it shows |
|---|---|
| `current/shots/current-status-1440.png`, `current-status-full.png`, `current-status-dark-1440.png` | The opening page, an update waiting, both clients registered |
| `current/shots/current-sessions-1440.png`, `current-sessions-full.png` | The sessions page with the background, four sessions and four connections |
| `current/shots/current-update-1440.png`, `current-update-full.png` | The update page with what holds an update |
| `current/shots/current-changelog-1440.png` | The changelog page |

#### Pages and what is on each

| Page | Address | Opened by | What is on it |
|---|---|---|---|
| **Status**, the opening page | `/<token>/?tab=N` | a Start Menu start, the installer once, the broken-install toast | *BrowserAI {version}* (*Updated to BrowserAI {version}* in the first tab after an update); the first-run sentence; the last action's note; **Updates**: whether a downloaded update waits, with a link to the update page; **Registration**: *Read at HH:mm:ss* with *Read again*, then Claude Code and Codex, each a state sentence and up to four buttons with a grey explanation each, and a restart hint; **Where things are**: install folder, data folder and log folder, each with *Open*, and the command line a client starts |
| **Sessions** | `sessions?tab=N` | its link, `BrowserAI.exe --sessions` | an introduction; *Read at HH:mm:ss* with *Refresh*; **BrowserAI's background, pid N** with every session it holds (purpose, *browser open* or *no browser open*, folder, *Open its folder*, *Driven by claude-code through its connection, pid N* or *Kept: its client has gone...* with the idle close time, traces by file name); then every connected client (conversation name, protocol name and version, pid, *Started in*, the sessions it drives), a VS Code window's tabs grouped under the window |
| **Update** | `update?tab=N` | the ready toast's *Install now* | the waiting version and a countdown to its install; **Hidden browser sessions**, **Visible windows** and **Agents**, each with its countdown and the reconnect a client needs afterwards; what installing now does; *Install BrowserAI {version} now* |
| **Changelog** | `changelog?tab=N` | the installed toast's *Changelog* | the shipped changelog's section for the installed version, each entry folded under its headline |

On every page: a row of four text links (Status, Sessions, Update, Changelog), a banner for what the stream says last, the broken-install notice first while it stands, and a footer link to the README.

#### Every action a person can take

- **Registration, per client** (each button names its client): *Register for all my {client} projects* / *Register again for all my...* / *Repair the {client} registration* (one button, its label following the state); *Unregister from {client}*; *Register in a project for {client}* (Windows' folder picker); *Remove BrowserAI from that project for {client}* (only when a project registration of ours is found at or above the background's working folder); *Remove from a project for {client}* (folder picker). And *Read again*.
- **Folders**: *Open* for the install, data and log folders; *Open its folder* for each session.
- **Sessions**: *Refresh*.
- **Update**: *Install BrowserAI {version} now*.
- **Not offered**: any close (the maintainer's 17 a); the trace viewer (the service accepts `open-trace`, no page renders it, and the desktop host answers that this build does not open the viewer yet); a delete of anything.

#### How navigation works

- Four plain links, each a full page load. The address carries `?tab=N`: every address handed out takes the next number, and a tab that connects with a higher number tells every older tab to close itself (Q337 a, *newest tab wins*). That is the `?tab=1` the maintainer saw.
- Each tab keeps one event stream open. On every change the background sends the whole `<main>` again, and the script replaces it, keeping open *Show details* and ticked boxes. Countdowns run in the script from a deadline in the markup.
- The **status page starts RegisterAI on every load** to read the registrations, and shows *Reading how BrowserAI is registered...* until the read arrives.
- The **sessions page is not live**: it is read when the page loads and on *Refresh*; a push redraws the old read.
- The **update page is watched once a second** and pushed when what holds the update changes.
- The listener ends one minute after the last tab closes (Q336 a), so a reload or a link keeps working.

#### Constraints every direction keeps

- **Server**: Kestrel from the empty builder with one plain request handler and a switch on the route (Q340 b, Q341 b, Q343). A new page is a new `case`; a query value is read with `PageGate.QueryValue`.
- **Address and gate**: `http://127.0.0.1:<port>/<token>/` with a 256-bit token per listener, handed out only through the pipe (Q334 a, Q335 a); every request passes `PageGate`: GET or POST, our exact `Host`, the token in constant time, `Sec-Fetch-Site`, and on a POST our `Origin`, JSON, at most 64 KB. No cookies.
- **Content security policy**: `default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; form-action 'none'; frame-ancestors 'none'; base-uri 'none'`. So: no inline script or style, **no style attributes**, no fonts at all (a font, even served from the page itself, falls under `default-src 'none'`), no frames, no form submissions; icons as inline SVG or `data:` images. Script may still set styles through the DOM, which the map in C does.
- **Every port is a new origin**: each listener picks a new port, so the browser's storage for the page starts empty every time. A preference the page should remember (a theme switch, a folded panel) has to live in the background.
- **Offline**: nothing from a CDN; system fonts only (Segoe UI Variable, Cascadia Mono or Consolas, and Sitka for D, all shipped with Windows).
- **Toasts open pages**: the ready toast opens the update page, the installed toast the changelog page, the broken-install toast the status page, and `--sessions` the sessions page. Every direction keeps an address for each.
- **Decided about the page**: no close of a session or a connection (17 a); no tab after an update, only the installed toast (20, which superseded Q338 b); the first tab after an update still says *Updated to BrowserAI {version}*; a broken install is said first on every page (10 b); an error is one sentence with the raw text under *Show details* (Q309 b); every registration button names its client; one registration action at a time; the folder picker can open behind the browser (Q311); Playwright's own dashboard is not exposed (Q317); the view-only look at a headless session is not built (it takes what happened in the page away from the agent's next answer, measured 2026-10-03).
- **Only an agent deletes a session** (DECISIONS, *Who deletes a session*). Every mockup offers *Open folder* and no delete.
- **Wording and characters**: the house rules hold for every string the product says, and the suite asserts every sentence and button, so any redesign rewrites `PageServiceTests` (598 lines), `UpdatePageTests` (614), `ChangelogPageTests` (158), `PageRegistrationTests` (659) and the browser-driven `PageBrowserTests`.
- **Accessibility**: today's page is semantic (headings, lists, buttons, `aria-current`, `role="alert"` and `role="status"`). It loses the keyboard focus whenever a state replaces `<main>`, because the focused element is destroyed with it. A redesign should update parts of the page, not the whole `<main>`.

#### What the page can show

| Fact | On the page today | Known to BrowserAI, not on the page |
|---|---|---|
| Version, install, data and log folders, the command a client starts | yes | |
| Sessions held now: purpose, folder, browser open, visible or hidden, kept, driving client and its relay's pid, idle close time, traces | yes | |
| Each connection: conversation name, protocol name and version, pid, folder, VS Code window, call running, countdown, reconnect needed | yes | |
| What holds an update, its countdowns, an older version | yes | |
| Each client's registration, all scopes, and why one could not be read | yes | |
| The broken install's first difference | yes | |
| The shipped changelog section | yes | |
| Where updates come from (GitHub releases or a local folder) and when BrowserAI last checked | | `UpdateSource`, `last-update-check.txt` |
| When and how the background started | | the background record, `HowStarted` |
| The browsers downloaded and their size | | the browsers folder, `BrowserProvisioner` |
| Every session folder on disk, not only the open ones | | `SessionIndex` |
| What each folder holds: size, transcript, saved login, network recording, traces | | `SessionInventory`, what `browserai_list` and `browserai_catch_up` report |
| Each call and the agent's reason for it | | each session's log, which `browserai_catch_up` reads back |
| A history of the day in one place | | nowhere: only the logs, line by line |

#### Why it reads badly today

1. **The opening page does not show what is running.** It shows the version, the update, eight registration buttons and three paths. What a person opens it for is a click away.
2. **Every session is listed twice**: once under *BrowserAI's background* and again under the client that drives it.
3. **Names a person does not use**: *claude-code 2.1.300*, *codex-mcp-client 0.165.0*, *pid 30412*, *background*, *driven by*, *through its connection*, *the session index*. And one thing has two names: *agent* on the update page, *client* on the sessions page.
4. **Registration is a wall**: eight buttons with long labels for two clients, each with a sentence beside it, when most visits need none of them.
5. **Paths everywhere** in a monospace font, at the same weight as the facts.
6. **No visual hierarchy or state**: browser-default buttons and links, one orange colour for every warning, everything a sentence of equal weight. Dark mode is the browser's default colours.
7. **Navigation is four equal links**, two of which (Update, Changelog) are empty or near empty unless a toast sent you there.
8. **Nothing says what a session, a connection or the background is.**

---

### 2. What the opening page should answer first

Who opens it: mostly the maintainer, from the Start Menu or from a toast, sometimes with nothing wrong and sometimes because something just happened on his screen; once, the installer; later, someone who installed BrowserAI for their agents and has never read the README. Ranked by what a person needs on the first screen, argued from what the product does:

1. **Does anything need me?** First, and only when true. The page knows three things a person must act on: a **broken install** (every session is refused until a reinstall, so nothing else on the page matters), a **client whose registration is stale, foreign or unreadable** (its agents cannot reach BrowserAI at all), and an **update held by a window set never to close** (it waits for ever). When nothing needs the person, this is one calm word in the header, not a section.
2. **What is running right now, for which conversation, and is a window on my screen?** BrowserAI runs from sign-in, so a person meets it when something happens: a Chromium window appears, a toast says an update waits, a conversation says it is using a browser. The questions then are *which conversation opened this window*, *which conversation uses which browser*, *is anything still working*. A visible window takes the screen and the focus and holds every update for up to an hour of idleness, so it gets its own mark. Today none of this is on the opening page.
3. **Is it working, and which version?** The page is served by the background, so a page that loads proves BrowserAI is running: a header line, not a section. *Working* for an agent also means its app is set up, so the state of Claude Code and Codex sits beside it, one line each.
4. **When does the update install, and what will it interrupt?** Only while one waits. It installs by itself once nothing uses BrowserAI; the person needs the time and the cost (every connection ends; a terminal Claude Code needs */mcp, BrowserAI, Reconnect*; a Codex conversation needs a new one). A card on the opening page, the detail where the toast's *Install now* leads.
5. **How much disk does it take, and can I clean up?** Session folders pile up, nothing expires, and a folder can hold a login, a transcript with typed passwords or a network recording with tokens. A summary on the opening page and a list behind it; acting on it is question 2 below.
6. **Where things are, the logs, what is new**: reference, for troubleshooting and after an update.

**First run is the exception**: the installer opens the page once, and then the first question is what was set up (which app is ready, which was not found, and that open sessions see BrowserAI after a restart). Every mockup shows that case under *Just installed*.

---

### 3. Five directions

They differ in how a person moves through the page and in what the opening page is. All five use the same vocabulary, which is question 3 below: a **conversation** is a chat in Claude Code or Codex; a **session** is the folder an agent made for its browser work; a **browser** is a session's browser while it runs, hidden or a window on your screen; Claude Code and Codex are **set up** for all your projects or in one project.

#### A. Overview and details

- **Navigation**: the opening page is an overview of cards. Each card answers one question, and its title leads to a page with the detail; every detail page begins with a breadcrumb back and a sentence that says what it lists. The overview is the menu, so there is nothing to learn. It is the pattern of a phone's settings or a banking app: one summary, a tap for more.
- **Opening page**: a headline that answers the main question (*4 conversations are using BrowserAI*, *All quiet*, *3 things need you*, *BrowserAI is installed*), any problem as a banner with its one action, then five cards: **Right now** (four counts and a row per conversation with its browser's state, kept sessions included), **Update** (countdown and a line per thing that holds it), **Claude Code and Codex** (one line per app), **Storage** (total, a bar, three parts), **This install** (version, running since, update source, three folder buttons).
- **Explains**: a caption under every card title; the conversations page opens with three definitions (Conversation, Session, Browser); a session's page lists what its folder holds and what each kind can carry; empty states say what would appear and when.
- **Style**: warm light neutral with white cards and the icon's emerald; dark is green-black with mint. Chips: amber for a window on your screen, green for hidden or working, grey for closed.
- **Screenshots**: `a-home-light.png`, `a-home-dark.png`, `a-conversations.png`, `a-session.png`, `a-update.png`, `a-apps.png`, `a-storage.png`, `a-home-quiet.png`, `a-home-attention.png`, `a-home-firstrun.png`, `a-home-narrow.png`.
- **For it**: answers the ranking on the first screen at 1440x900; today's four routes map one to one (status to overview, sessions to conversations, update, changelog), so the toasts, `--sessions` and the tab model stay as they are; it grows by *See all*; it works at phone width.
- **Against**: an action is two clicks away (setup, a session's folder); the overview is busy when everything is present at once; it looks like many dashboards.
- **Cost**: medium. Eight views over four new routes (one session, apps, storage, install); the stylesheet grows from 26 to about 350 lines; the script keeps its job and its countdowns. New data is optional: the overview works without sizes, the last check and the update source.

#### B. Settings app

- **Navigation**: a sidebar that is always there: Home, Conversations, Browsers and sessions, Update, Claude Code and Codex, Storage, Folders and logs, What is new, with counts and a badge where something waits. It is Windows 11 Settings, which every Windows user already knows, and the sidebar always shows where you are and what else exists. An icon rail at tablet width, a section menu at phone width.
- **Opening page, Home**: three tiles (BrowserAI's state, the update, the two apps), the *Right now* rows, then rows into the other sections.
- **Explains**: a sentence under every section title, a description line under every row (the Settings pattern), expanders holding the detail and the actions, problems as Windows-style info bars.
- **Style**: Fluent: a soft Mica-like background, thin-bordered cards with small corners, the icon's teal as the accent; dark as Windows' dark mode.
- **Screenshots**: `b-home-light.png`, `b-home-dark.png`, `b-conversations.png`, `b-sessions.png`, `b-session.png`, `b-update.png`, `b-apps.png`, `b-storage.png`, `b-home-attention.png`, `b-home-quiet.png`, `b-home-narrow.png`.
- **For it**: the most familiar; actions sit in context in expanders, with no extra pages; room for sections that do not exist yet.
- **Against**: the most chrome for the least content; a copy of Windows Settings inside a browser tab can feel off; eight sections for content that fills four; Home is an index more than an answer.
- **Cost**: medium plus. A's views, plus the sidebar with counts that follow every push, and the narrow-width menu.

#### C. Live map

- **Navigation**: the opening page is one picture: conversations on the left, BrowserAI in the middle, browser sessions on the right, and a line from each conversation through BrowserAI to the browser it opened, coloured and patterned by state. Click any card and an inspector opens beside it saying what that thing is. Four tabs for the rest (Update, Claude Code and Codex, Storage, What is new). It shows the relationships the other pages can only describe: which conversation owns that window is a line, not a pid.
- **Opening page**: a headline, a legend, the map, and three summary links under it (update, setup, storage).
- **Explains**: the picture is the explanation; the inspector starts with *what is this* for each kind; a kept session is a dotted line to a card that says its conversation ended; a conversation with no browser ends in *No browser yet*.
- **Style**: dark-first, a control room on a fine grid, a glowing hub in the icon's gradient, lines in amber, green and grey; a light variant.
- **Screenshots**: `c-map-dark.png`, `c-map-light.png`, `c-map-inspector.png`, `c-update.png`, `c-setup.png`, `c-map-attention.png`, `c-map-quiet.png`, `c-map-narrow.png`.
- **For it**: explains *what is what* best; the most distinctive and the most striking; the map needs no new data (each session already names the relay pid that drives it).
- **Against**: a picture does not scale, and past about eight conversations it grows tall and crowded; **at phone width it becomes a plain list and the lines, which carry the meaning, are gone** (`c-map-narrow.png`); the script draws the lines from the cards' measured places after every push, which no unit test can assert, so the browser-driven arm has to grow; colour carries meaning, so each line also carries a pattern and each card a text state.
- **Cost**: medium to high. Views as A for the tabs, plus a layout script of about 80 lines and the inspector.

#### D. Today

- **Navigation**: the opening page is what happened today, newest first, in sentences (*Compare pricing pages opened a Chromium window on your screen for pricing-compare*), each call with the agent's own reason for it, and a link to its subject; filters (Browsers, Conversations, Update, Problems); a **Now** column beside it with what is on your screen, what is running, the update countdown, and links to Sessions, Claude Code and Codex, Storage, Folders and logs. It tells the story in order, so cause and effect show: why a browser closed, when the update came, which conversation opened what.
- **Explains**: every event is a full sentence; idle closes and kept sessions are explained when they happen; each box of the Now column explains itself in a line.
- **Style**: editorial: warm paper, a serif face for headings (Sitka, which Windows ships), a time rail with coloured dots, the agent's reasons in italics.
- **Screenshots**: `d-today-light.png`, `d-today-dark.png`, `d-sessions.png`, `d-update.png`, `d-setup.png`, `d-today-attention.png`, `d-today-quiet.png`, `d-today-narrow.png`.
- **For it**: the only one that answers *what happened while I was away*; shows the agent's intent per call, which no page shows today.
- **Against**: it needs data the page does not have: a record of events (kept in the background's memory and lost at sign-out, or read back from the logs) and each call's reason from the session logs, which an agent writes and which can quote page content; a busy hour makes a long feed; the state now is split between the feed and the column.
- **Cost**: high. A's views, plus an event record in the background (a connection opened or ended, a browser opened or closed with its reason, a session kept, an update found, downloaded or installing, a registration changed, a refusal), pushed to the tabs, plus reading the latest reason per session.

#### E. One page

- **Navigation**: none. One page: a sentence at the top answers the main question, three chips under it carry the update, setup and storage answers, and below it are panels whose headers already carry their answer on the right (*4 conversations, 3 browsers, 1 on your screen*, *Installs in 48:05*, *Both set up for all your projects*, *2.0 GB on this PC*). A panel opens in place; a sticky row of jump links leads to each. The ready toast's *Install now* opens the page with the Update panel open and in view, the installed toast's *Changelog* with What is new open.
- **Explains**: a three-part glossary at the top of the Now panel; a summary in every header; the explanations inside each panel.
- **Style**: a bold band in the icon's green-to-teal gradient for the sentence, then calm white panels; dark deepens the band.
- **Screenshots**: `e-light.png`, `e-light-full.png` (the whole page, panels closed), `e-all-open-full.png` (every panel open), `e-dark.png`, `e-update.png` (as the toast would open it), `e-attention.png`, `e-quiet.png`, `e-narrow.png`.
- **For it**: the least to learn and the least to build; with every panel shut the headers still answer everything; panels can be HTML `details` elements, which the page's script already keeps open across a push.
- **Against**: one long page when there is a lot; one session's detail has no page of its own; the band takes about 240 px on every visit.
- **Cost**: low. One route (the other three keep working and only choose which panel is open), about 200 lines of CSS, almost no new script. New data optional.

---

### 4. Comparison

| | A, Overview and details | B, Settings app | C, Live map | D, Today | E, One page |
|---|---|---|---|---|---|
| **Navigation** | cards on a home, detail pages, breadcrumb back | a sidebar of eight sections, always shown | one picture and an inspector, four tabs | a timeline with filters and a Now column, three pages | none: one page, panels open in place |
| **First-time user** | high | high, for a Windows user | high for *what uses what*, lower for the rest | medium: it asks for reading | high |
| **Beauty** | calm, polished | native, tidy | the most striking | distinctive, editorial | bold band, calm below |
| **How much it explains** | a caption per card, definitions on the conversations page, holdings per session | a line under every title and row | the picture, plus *what is this* in the inspector | every event a sentence; the least on the state now | a glossary and a summary in every header |
| **Build cost** | medium | medium plus | medium to high | high | low |
| **Data the page lacks** | optional: sizes, sessions on disk, last check, update source | as A | none for the map; optional as A | an event record and each call's reason | optional: sizes |
| **Phone width** | adapts | adapts: icon rail, then a menu | becomes a list, lines lost | adapts, Now first | adapts |
| **Today's push model** | as is, per route | as is, plus badge counts | as is, plus a redraw of the lines | events must be pushed | as is, one route |
| **Ten or more conversations** | *See all* | long lists, fine | crowded | a long feed | a long panel |

#### What changes for each feature the page has today

| Today | A | B | C | D | E |
|---|---|---|---|---|---|
| Version heading | header and headline | sidebar head and Home tile | top bar and hub card | masthead | the sentence |
| *Updated to BrowserAI {version}* | a banner on the overview | an info bar on Home | an alert above the map | the first event | the sentence says it |
| First-run sentence | green banner and the setup card | info bar | alert | first event | sentence, setup panel open |
| Status page's update section | Update card | Home tile | hub badge and summary link | Now column box | chip and panel header |
| Update page | its own page, *Install now* at the top | Update section with expanders | Update tab | its own page | Update panel |
| Eight registration buttons | one card per app on its own page, project actions below, technical lines folded | one expander per app | one card per app in a tab | one sheet per app | one row per app in a panel |
| Where things are | This install card and page | Folders and logs section | the hub's inspector | Folders and logs page | Folders and logs panel |
| Sessions page | Conversations and browsers, **each session once**, under its conversation; kept sessions apart | Conversations, and Browsers and sessions | the map itself | Sessions page and the feed | Now panel |
| *Open its folder* | each session row and page | each row | the inspector | each row | each row |
| Traces by name | the session's page | the session's page | the inspector | Sessions page | not shown |
| Pids, protocol names | folded under *Details for troubleshooting* | gone | gone | gone | gone |
| Broken-install notice | red banner first, reinstall steps on the install page | info bar | alert, and the hub says so | Now column box | the sentence turns red, notice and panel |
| Note after an action | under the card that acted | in the section | in the tab | in the page | in the panel |
| Banner, `?tab=N`, newest tab wins, linger | unchanged | unchanged | unchanged | unchanged | unchanged |
| What the toasts open | Update page; What is new | the same sections | Update tab; What is new tab | Update page; What is new | this page with that panel open |

---

### 5. Recommendation

**A, Overview and details.** It answers the ranking in order on the first screen without scrolling at 1440x900: what needs you, what is running and what is on your screen, the update, the setup, storage and the install. It explains each part where it stands. It keeps every address the toasts, `--sessions` and the tab model already use, so the background's plumbing does not move. It is the one that still works when there are ten conversations and twenty session folders, and at phone width. Its cost is the views, not new machinery, and it can ship in two steps: first the overview, conversations, a session, update, setup, install and what is new from the data the page already has; then storage, once the page is given sizes and the sessions on disk.

**If the least code matters more, E** gives most of A's first screen for the smallest change: the same answers in its sentence and its panel headers, one route, and panels the script already knows how to keep open. **C's map is worth keeping in reserve** as a view inside A's *Conversations and browsers* page for the day a picture of who uses what is wanted; it needs no new data. B costs more than A for a familiar look, and D's history is the most expensive and asks for data that does not exist yet.

---

### 6. Questions only the maintainer can answer

1. **Which direction?** The five above: **a** A, Overview and details (recommended); **b** B, Settings app; **c** C, Live map; **d** D, Today; **e** E, One page; or **f** a mix you name, such as A with C's map as its conversations view.
2. **May a person delete a session folder from the page?** Today only the agent that made a session deletes it (*Who deletes a session*), and every mockup offers *Open folder* and no delete; the question *can I clean up?* in section 2 points at it. **a** No: read-only, sizes shown, *Open folder* to delete it yourself in Explorer (recommended for the first build, because the decision stands as written); **b** yes, for a session that is not open, after naming what its folder holds (what `browserai_catch_up` names) and a confirmation, through the same delete `browserai_destroy` uses; **c** **b** plus a clean-up list of folders not used for some days, largest first, to select and delete together.
3. **Which words?** Today the update page says *agent* and the sessions page *client* for the same thing, and both say *pid*, *background* and *driven by*. The mockups say **conversation** for a chat in Claude Code or Codex, **app** for Claude Code and Codex themselves, **session** for the folder, **browser** for its running browser (hidden, or a window on your screen) and **set up** for registered. **a** The mockups' words (recommended); **b** keep today's words; **c** other words you give.
4. **Which new facts may the page show?** Each needs the background to pass it to the page. (1) Each session folder's size and the folders not open now: a walk of every folder, done when the page is drawn or kept in memory. (2) Where updates come from and when BrowserAI last checked. (3) When the background started, and how. (4) Each session's latest reason, the agent's own words from the session's log, which can quote what a page showed. **a** All four; **b** 1 to 3 only (recommended: 4 is agent-written text on a page a person may share); **c** none, the redesign uses only what the page has today.
5. **The colour scheme**: today the page follows Windows' light or dark setting through the browser. **a** Keep following Windows only (recommended; nothing to store); **b** add a switch on the page, which the background has to remember, because every listener is a new origin and the browser's storage for the page starts empty each time.
6. **Pids and command lines**: **a** folded under *Details for troubleshooting* on the setup and install pages (as in A); **b** gone from the page, kept in the log and the README; **c** as today, beside each entry.
7. **A live list of conversations and sessions**: today the sessions page is read at load and on *Refresh*. The mockups assume it follows every change, as the update page does. **a** Live, pushed on every change (recommended); **b** as today, with *Refresh*.

---

### Files

| Path | What it is |
|---|---|
| `README.md` | This write-up |
| `current/page.css` | `PageAssets.Css` at `3a64fbe8`, verbatim |
| `current/status.html`, `sessions.html`, `update.html`, `changelog.html` | Today's four pages rebuilt to the composers' markup, with invented data |
| `<direction>/index.html` | The mockup, one file, nothing external |
| `<direction>/src/` | Its parts: `index.html`, `style.css`, `app.js` |
| `shared/data.js`, `shared/icons.js`, `shared/mock.css` | The invented data and its four cases, the line icons (none copies a vendor mark; BrowserAI's own icon from `assets/icon.svg`), the mockup-only control |
| `tools/build.py` | Inlines `src/` and `shared/` into each `index.html` |
| `tools/serve.py` | The 127.0.0.1 server the screenshots were taken through (BrowserAI's browser server refuses `file:` addresses) |
