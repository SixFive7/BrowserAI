<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Changelog

Everything notable that has happened to BrowserAI. The format is
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the versions are
[semantic](https://semver.org/spec/v2.0.0.html) — three parts, because that is
[the shape `vpk` accepts](kb/packaging/velopack.md#nativeaot-hooks-and-vpk-output).

**A version here is a git tag, and nothing else.** The build derives its version
from the nearest `v*` tag ([`STACK.md`](STACK.md)); no number is typed
in a project file, in this file, or anywhere else. A section heading carries the
bare version and the tag carries the `v`.

**Entries are written as the work lands, never reconstructed at release time.**
[The release checklist](RELEASING.md) refuses a release whose
`[Unreleased]` section is empty, and `build/Get-ReleaseNotes.ps1` is what
enforces it — a checklist satisfied by fifteen minutes of `git log` archaeology
has been satisfied in form only.

**Every entry opens with one icon from a fixed palette, then a bold
one-sentence headline, then the whole of what happened.** The icon is chosen
for what the entry *is* rather than for the group it sits under, so a fix that
removes something carries the removal icon and a test that closes a hole
carries the test one. [`build/New-ReleaseNotes.ps1`](build/New-ReleaseNotes.ps1)
reads that shape to fold each detail behind a *read more* in the GitHub
release body; nothing else depends on it.

| Icon | Meaning | Icon | Meaning |
|---|---|---|---|
| ✨ | new capability | 🐛 | fix |
| 🔧 | behaviour or configuration change | 🔒 | security or permissions |
| 🗑️ | removal or deprecation | 💥 | breaking, or the reader must act |
| 📝 | documentation | ✅ | tests and the gate |
| 📦 | packaging, installer, release pipeline | ⚡ | performance |
| ♻️ | refactor with no behaviour change | ⬆️ | dependency move |

## [Unreleased]

## [1.1.0] - 2026-09-22

A web page can offer its own tools to the browser. From this version they can be
called. A name invented by a page is still refused at the door; the page's tool
is reached through one tool of ours, and that tool is judged like every other.

The rest is upkeep and repair. Velopack moved for the first time since this
project started. The installer is a 64-bit program now, and Windows will show
the app's size in Add/Remove Programs, where it showed nothing. One note told a
caller its cookies had survived a crash. The measurement says they may not have,
and the note says that now.

### Added

- ✨ **A web page's own tools are callable, through one tool of ours that is judged like the rest.**
  `@playwright/mcp` 0.0.82 let a page put tools on the child's `tools/list` — names,
  descriptions and `inputSchema`s written by whoever wrote the page — and BrowserAI's
  deny-by-default refused every one of them at the door. That is the rule working exactly
  as designed, against an adversary it was never written for, and it left a real capability
  unreachable: a page's own search, form submit or lookup could be read about and not used.

  `browserai_page_tool` is the eighth authored tool. It takes `session`, `why`, the page's
  own `name` for the tool, an `arguments` object, and an optional `page` URL. It lists the
  session child's current tools, resolves the name to the wire name upstream built from it,
  and forwards the call — returning the child's answer byte-identical, because what comes
  back is the page's own words and nothing here rewrites them.

  **Page tools are reached as a CLASS rather than through rows, and that is the decision.**
  [`tool-verdicts.json`](tool-verdicts.json) is keyed by name, `ToolVerdictTests` holds it
  against the golden snapshot in both directions, and a page's names are invented by the
  page — so a row per page tool is not a thing that can exist. What the class judgement
  buys back is everything a row would have given: the call is bounded, the name is
  re-resolved against the live tab every time, the caller's `why` is recorded on the
  session, and `session` and `why` never reach the page. **The door is not widened.** A
  `webmcp_*` name arriving straight from a client still has no row and is still refused;
  this tool is not a new door, it is the only door. The maintainer's framing, the
  alternative not taken and why are in
  [`DECISIONS.md`](DECISIONS.md#a-web-pages-own-tools-are-reached-through-one-tool-of-ours).

  **Resolution is by wire name and `annotations.title` is the cross-check, which is the
  opposite way round from how it reads.** Upstream builds the entry with
  `title: tool.title || tool.name`, so a page that sets its own display title puts THAT in
  the annotations while the snapshot block a model reads goes on printing the name —
  measured 2026-09-21 against a page registering `{ name: "raw_name_here", title: "Human
  Title" }`. Matching on the title would have made every titled page tool uncallable. What
  the title is good for is the case where it matches and the wire name is not the one the
  rule builds: that is either a page with a display title or upstream having changed how it
  builds names, BrowserAI cannot tell them apart from here, and the refusal says both.

  **Bounded at 60 seconds, by BrowserAI, because nothing upstream bounds it at all.**
  `callWebMCPTool` awaits a handler the page supplied with `kNoTimeout` under it; measured
  silent at 61 s. Sixty seconds is twice Playwright's own default for an operation on a
  page, so a page tool doing one ordinary round trip to its own backend is not cut off, and
  short enough that a hung call is recovered inside one turn — nothing measured sits
  between about half a second and never. **It is a real recovery rather than a give-up, and
  that was measured before the number was chosen**: a pending page tool does not block the
  child, `browser_snapshot` answered in 4–7 ms beside one, and navigating away released
  the abandoned call in 11–13 ms. The refusal says all three, because what the timeout
  does NOT do is stop the page's code.

  Six refusals, each with a different recovery and each provoked by a real condition in
  `ErrorCatalogueTests`: the name is not on this page (listing what is), two tools share it,
  the tab has moved since you read it, the page you named could not be checked, the title
  matches but the wire name does not follow the rule, and the page never answered.
  `PageToolTests` drives every one of them against real pages that really register WebMCP
  tools, through the published binary.

- ✅ **The scratch configuration the suite hands the real client is seeded as already onboarded.**
  Three arms point `CLAUDE_CONFIG_DIR` at a fresh scratch directory and then start the real
  client — the sandbox that keeps them off the maintainer's own registration. That
  emptiness is also, to the client, a first run, and a first run is where onboarding and
  sign-in live. It has never fired; the guard went in anyway, because this suite runs on
  the maintainer's desktop and the failure mode is a browser window appearing on it.

  `OnboardedClientConfig.Seed` writes `hasCompletedOnboarding: true` into
  `$CLAUDE_CONFIG_DIR\.claude.json` before any `claude` runs. **The value is the client's
  own throwaway-config recipe**, read out of the shipped bundle rather than invented here —
  the same three keys `claude plugin eval` writes for its sandbox.
  `HouseRuleTests.EveryScratchClientConfigurationIsSeededAsOnboardedBeforeTheClientRuns`
  refuses a site that skips the seam, and was planted red against all three.

  **The guard is asserted, never measured against the flow**, and its
  [hazard row](HAZARDS.md#hazard-index) is `open` saying so: proving it works means running
  the sign-in flow to watch it suppressed, which is the event being guarded against. There
  is no non-interactive signal to set instead — `claude mcp add` has no such flag, the one
  onboarding-named variable in the bundle turns onboarding **on**, and the published
  documentation does not document the marker at all.

- ✅ **A log event id must be unique in its class, and the scan found a second collision on its first run.**
  `ProxyLogTests.EveryLogEventIdIsUniqueInItsClassAndNoRetiredIdIsInUse` reads every
  `[LoggerMessage]` under `src/` as text, groups the ids by the class that declares them,
  and refuses a repeat. It also refuses any id a class's own `RETIRED-EVENT-IDS:` marker
  names. **An id is a key somebody's saved log query is written against**, which is the whole reason the rule exists and the reason the retired
  list is read out of the comment rather than typed into the test: a second copy is a
  second thing to keep in step, and the comment is what a reader of an old log meets.

  **It was written for a collision found by reading and it found another one by running.**
  `ProxyLog` reused retired id 16 for twenty-two days, which a person noticed on 2026-09-22.
  On this test's first run against the real tree it reported `ClientLivenessLog` declaring
  **id 76 twice** -- `ClientWaitCannotBeInterpreted` and `ClientHasAlreadyExited` -- which
  nobody had noticed at all. Two instances of one defect class in one tree is the argument
  for a scan over the argument that the first was a one-off. Both events have left that id
  and it is retired; the entry below is what happened to them.

  **The marker is read per class**, not out of one file. It was written reading
  `ProxyLog`'s comment alone, which was right while `ProxyLog` was the only class with a
  retired id and silently left every other class uncovered. A second class retired one the
  same day, so the scan now finds every marker in `src/` and scopes each to the class it
  is declared in, with a control over two markers in one file.

  **The marker parse is held in both directions**, because the same comment contains a
  correction paragraph naming 16 as an id that is in use again: a parse that took numbers
  out of prose would retire 16 and fail the tree. Controls are synthetic and cover a clean
  pair, a duplicated id, the same id in two different classes (not a collision, because an
  id is scoped to its class), and the marker reading a list rather than a sentence.

### Changed

- 🐛 **Two client-liveness events shared event id 76, so both moved off it and the id is retired.**
  `ClientHasAlreadyExited` took 76 in `ec6d858` on 2026-09-15 and
  `ClientWaitCannotBeInterpreted` took the same 76 in `bf27512` one day later, read out of
  `git log -S` rather than remembered. `ClientWaitCannotBeInterpreted` is 77 now and
  `ClientHasAlreadyExited` is 78. **Nothing holds 76, and nothing may.**

  ⚠️ **Both are in `v1.0.0`**, so a log from a shipped binary carries 76 for two different
  events and nothing but the message text tells them apart. That is not repairable and is
  recorded in the code rather than closed. **Retiring the id is what stops it spreading:**
  had 76 kept its first meaning, a query written against a v1.0.0 log would go on being
  answered by every release after it, in the one direction a reader cannot detect. Now the
  ambiguity ends at v1.0.0.

  **This is the maintainer's decision and not the repair that was first offered.** The
  batch moved the later event only, on the principle that the first meaning owns the id;
  that was put to him as one of four directions and he chose to retire the id instead. The
  class carries its own `RETIRED-EVENT-IDS: 76` line, and the scan reads those markers per
  class as of this release rather than out of one file.

  It was found by the new event-id scan on its first run, against a tree nobody had changed
  for it. **The renumber was planted red the way the rule asks**: 76 went onto the retired
  list while the event still carried it, the scan named the file, the line, the class and
  the member, and only then did the event move. The one place in the suite that reads an id
  back off a real log record moved with it, and gained a second assertion that nothing
  emits 76 at all.

- 📦 **Every release packs full packages only, so an update downloads the whole thing.**
  The maintainer's decision, in his words: *"always produce full packages only. The sizes
  are so small, and internet speeds nowadays are so fast that we don't want to exert any
  effort in creating deltas. Full downloads are always just easier."*

  `build/New-Release.ps1` passes `--delta None`, which is `vpk`'s own name for it.
  **The mode was resolved from the tool rather than from memory**: `vpk pack --help`
  documents `--delta <MODE>` and does not list the modes, so handing it a value it cannot
  parse makes it name them.

  ⚠️ **It looked like this was already true and it was not.** No release before this one
  ever carried a delta, and not because anybody chose it: the clean re-pack empties
  `Releases/`, so `vpk` never had a previous package to compare against. The default is
  `BestSpeed`, so the first cut that left one there would have started emitting deltas
  with nothing to say so. That is why it is an argument the script passes.

  **What it costs is measured rather than waved at**: a same-tree delta is **138,791 b**
  against a full package of **55,022,705 b**, so every install now downloads about 55 MB
  where it could have downloaded 139 KB. What it buys is one artifact per release and a
  rollback that is always a plain download.

  `ReleaseScriptTests.EveryReleasePacksFullPackagesOnlyAndTheFeedCarriesNoDeltaRow` holds
  both halves, because either is satisfiable without the other: that the script passes the
  argument, which a scan can see, and that the argument means what the script assumes,
  which only `vpk` can say. It packs a real 162 KB executable twice into a feed that
  already holds the previous full package. **The positive control is the whole point** --
  the same two packs without the argument do produce a delta, so "no delta" is the option
  working rather than `vpk` having nothing to compare against.

  Four documents were re-scoped rather than left to rot, each with what it previously said:
  the testing layer table and the paragraph under it, re-verification rows 24 and 85, the
  rollback item in the release checklist, and the kb section that measures the delta. **No
  number was retracted.** They record what `vpk` does when asked, which is what makes this
  a choice and not a limitation.

- 🔧 **The relaunch note stops promising that stored state survived, because it may not have.**
  `SessionManager.ChildWasRelaunched` is what `browserai_resume` returns after it finds a
  session's browser server dead and starts a new one. It read *"The session's directory,
  profile and log are unchanged, so cookies and stored state are still there"*. The first
  half is true and the second is not: **the profile really is intact on disk, and what is
  on disk is not what was written.**

  It now says the directory, profile and log are on disk and unchanged, that a browser
  which did not shut down cleanly had no chance to flush, that recent cookie and
  `localStorage` writes may be gone while `IndexedDB` and `CacheStorage` survive, and that
  the caller should read any stored value back before relying on it.

  ⚠️ **It deliberately does not say "killed", and that is the half the measurement
  settled.** The obvious narrowing -- blame the kill -- was tested and refused: nine runs
  at chromium 1246 and firefox 1549 in which the browser server ended **itself**, four by
  `process.exit(0)` and four by `process.abort()` against one kill control, lost exactly
  the same stores as the control, per family, with no arm distinguishable from it. A
  wording scoped to a kill would be false on a crash, which is the case a caller is likelier
  to meet.

  **`DeadChildTests.TheRelaunchNoteDoesNotPromiseThatStoredStateSurvived` is the guard**,
  and it is a guard on the text rather than on the constant. Every other arm in that file
  asserts against `SessionManager.ChildWasRelaunched` the symbol, so all of them stayed
  green through the wording that shipped; this one was watched red against it.

- ⬆️ **Velopack rolled 1.2.0 to 1.2.158, and the Windows installer is a 64-bit bootstrapper now.**
  The first move this dependency has made since it was first reviewed. 158 commits, and one
  release object in between in any form. **No product code changed to absorb it and nothing
  was pinned back**: the `vpk` CLI surface was diffed by running both versions, and no flag
  this project passes was removed or renamed. **One test did change**, and it changed because
  the gate found it rather than because anybody predicted it -- see the stub naming below.

  **What arrives with it and is visible to a user.** `Setup.exe` and the root stub are
  built x64 for a win-x64 pack rather than i686, measured on the packed artifact as PE
  machine `Amd64` at 62,585,861 bytes against 59,490,421 before. `EstimatedSize` is written
  as a `REG_DWORD` instead of a `REG_QWORD`, so Windows will show the app's size in
  Add/Remove Programs where it showed nothing -- confirmed against the real install, whose
  key carries `REG_QWORD 140209` today. `Update.exe --uninstall` shows an indeterminate
  progress dialog, suppressed by `--silent` as before. The delta patch format is zstd only;
  the bsdiff fallback is gone.

  **What is declined and why.** Velopack Flow, the hosted release service that arrives as
  four new top-level `vpk` commands, because releases go to GitHub Releases through `gh`
  and a second publish path in the most hazard-dense area of the product buys nothing. The
  Windows installer channel-override tag readers, because there is one track and the
  channel is set explicitly for the reason landmine 1 gives. The two renamed MSI flags are
  inert here: `--msi` is never passed.

  **The one thing in this tree that had to move, and the gate is what found it.** Upstream
  names the stub embedded in the `.nupkg` after `packTitle ?? packId` now instead of after
  `mainExe`. The shipping pack is unaffected, because its title and its main executable's
  base name are both `BrowserAI`; the **suite's own** pack is titled `BrowserAI (suite)`, so
  its stub is named for that, and
  `RealInstallerTests.TheSuitesPackAndTheShippingPackDifferOnlyWhereTheIdAppears` went red on
  the first gate run after the bump because the two packs' entry names stopped matching. The
  name normalisation was widened to that one entry, keyed on the `_ExecutionStub.exe` suffix
  rather than on the title wherever it appears -- stripping `BrowserAI` from names would
  collapse `BrowserAI.exe` and `BrowserAI.Server.exe` into one key in one pack and leave them
  alone in the other. Two controls hold both directions. Nothing the user installs is renamed.

  **The rename arrives in a second place, and the same arm found that on the next run.**
  `vpk` writes one content-type declaration per extension in first-seen order, so the pack
  whose stub sorts ahead of the `.xml` files meets an `.exe` first: `[Content_Types].xml`
  carries the same 24 declarations in the same 1,692 bytes in a different order. **That is
  not nondeterminism and it was checked rather than assumed** -- every shipping pack on this
  machine, 20 of them across both Velopack versions and back to `0.1.1`, hashes that part
  identically, and the suite's pack of the same run is the only outlier. That one part is
  compared as a set of declarations now, with controls in three directions: a re-ordering is
  not an offence, a missing declaration is, and a read that came back empty is never "the
  same".

  **Our own open ask is still open.** `velopack/velopack#1056`, asking for a way to start
  the installed app without a console window after a non-silent install, has no comments,
  no linked pull request and no change in 1.2.158, so the windowless-exit path stays
  exactly as necessary as it was.

- 📝 **A browser server that ends itself loses the same stores as one that is killed.**
  The durability table recorded on 2026-09-22 that a relaunch after the child was *killed*
  loses persistent stores a clean handover keeps, and read the mechanism as the kill. That
  reading invited an escape -- *a browser that dies of its own accord flushes on the way
  out* -- and it is now closed by measurement.

  Nine runs, three ways for the child to go, two families: killed by pid, ended by
  `process.exit(0)`, and ended by `process.abort()`. **Chromium lost the cookie and
  `localStorage` on 5 of 5. Firefox lost `localStorage` and kept the cookie on 5 of 5.**
  `IndexedDB`, `CacheStorage` and the service-worker registration survived in all nine, and
  the relaunch came back in 290 to 344 ms every time. No arm is distinguishable from the
  control.

  **One fact about upstream came out of building the rig**, and it is worth knowing for a
  tool this product forwards with an `allow` verdict: `browser_run_code_unsafe` describes
  itself as executing arbitrary JavaScript in the Playwright server process, and does it
  through `vm.runInContext` against a context holding `page` and one promise and nothing
  else. `process`, `setTimeout` and `require` are all undefined there. The description is
  accurate about the risk and misleading about the default scope.

- 📝 **The feed is asked once per server start, and a hundred browsers in one session ask nothing.**
  Asked by the maintainer as *"is there not a risk of flooding the release page with update
  checks if an agent starts 100+ browsers in sustained bursts?"* There is no timer, no
  interval and no retry: `UpdateService.StartInBackground` runs one pass and the type holds
  no `PeriodicTimer`. Sessions and browsers are children of one server per client
  connection, so they cost no requests at all.

  **Two of the three guards are this repository's own**, which is the half that had been
  written down as an inference and is now read: `StartInBackground` returns before any
  network call when the build was not installed by the installer, and again when it carries
  a pre-release suffix. Velopack supplies only the predicate. So a checkout, a `dotnet run`,
  every test host and every suite-started server ask for nothing.

  **The monitor is the asset's own download counter and it discriminates.** On 2026-09-22
  `releases.win.json` read 48 against 1 for every other asset on the release, while the
  suite had started thousands of servers in the same window. The flat 1 on the packages
  says something else worth having: no install anywhere has ever applied an update, because
  nothing has ever fetched a package.

  The pathological case is named and accepted: a client spawning a fresh server per task in
  bursts costs one 260-byte request per start and inflates that counter, degrading the
  monitor rather than the service. The alternative offered and declined was a machine-wide
  check stamp in the shared data root.

- 🔧 **`webmcp: true` is written into every generated config rather than left to upstream's default.**
  The key switches on the page-provided tools and the
  `- webmcp tools (page-provided, untrusted):` block that every `browser_snapshot` answer
  carries. Upstream's default is on, so nothing about what reaches a model changes —
  what changes is that a stance is recorded where a reader can find it, the way
  `allowUnrestrictedFileAccess` and `timeouts.idle` already are: an omission records no
  decision, `browser_get_config` cannot read back a key the file never carried, and the day
  upstream's default moves this is a red build rather than a capability that quietly went.

  **`false` was the recommendation until `browserai_page_tool` existed**, on the ground that
  the block was page-authored text with no capability behind it. There is a capability
  behind it now, and the block is the only catalogue there is: with the key `false` there is
  no block, no tab-header count and no dynamic tools, so a caller would have the tool and no
  way to know what to name. What it costs is unchanged and is still
  [a hazard row](HAZARDS.md#hazard-index) — the page's own words still reach the model,
  exactly as before.

- 📝 **Three decisions about page-provided tools, with the directions not taken beside them.**
  [`DECISIONS.md`](DECISIONS.md#a-web-pages-own-tools-are-reached-through-one-tool-of-ours)
  carries the maintainer's framing verbatim, why a fourth verdict class is not a thing that
  can exist, and **direction A recorded as the follow-on not taken** — a marker rule at
  the door, strictly more capable, costing *every advertised name has a row* the word
  *row*. It also records that **BrowserAI does not declare `listChanged`**: its list does
  not change, because `tools/list` is answered from the run's own child, which never
  navigates — measured 78 before a page and 78 after — and clients cache the
  capability at `initialize`, so declaring it is cheap and in practice irreversible.
  **Direction B is recorded as the direction not taken**: announce it, merge each session's
  page tools into the advertised list, forward the notifications. `VerticalSliceTests`'
  literal `{"tools":{}}` assertion is the mechanism that keeps the first half true.

  [`HAZARDS.md`](HAZARDS.md#hazard-index) gains two rows and amends one. The untrusted-text
  row is amended **by addition**: the block is now the discovery channel, and it reaches the
  model exactly as it did before. The new rows are late binding — the same wire name is
  a different page's code after a navigation, mitigated by `page` and by re-resolving every
  call, closed by neither — and the unbounded upstream call, mitigated by our timeout,
  with the measured fact that the abandoned evaluate stays on the tab until something
  navigates it. Two `[FLOATS]` facts gain
  [re-verification rows](kb/re-verification.md): the name rule and the no-block measurement.

- 🔧 **A session's deletion, what a destroy takes, its upload root and its move are all said now.**
  Four things were already true of the product and said in no string a model reads.
  BrowserAI deletes nothing on a schedule and nothing at a size — `browserai_init` said
  that half as *"nothing here expires"* — and a retention policy with no owner is half a
  sentence, so a session that signed into something stays signed into it, on disk, until
  somebody notices. `browserai_destroy` said it *"deletes the whole directory"*, which a
  caller reads as a claim about the session rather than about the screenshots it was asked
  to produce. `browser_file_upload` can only reach a file inside the session's `output`
  folder, which is this project's `allowUnrestrictedFileAccess: false` rather than
  upstream's default, and a caller cannot guess it. And a session directory moves by hand
  while no browser is open on it and resumes at its new path, which nothing said at all.

  Each is placed where it is read at the moment it matters: the deletion responsibility in
  the server `instructions`, on `browserai_init` and on `browserai_destroy`; what a destroy
  takes on `browserai_destroy` and `browserai_init`; the upload root on `browserai_init`,
  beside the sentence that the directory IS the session; moving by hand on
  `browserai_resume`.

  THE UPLOAD SENTENCE IS NOT ON THE TOOL IT IS ABOUT. `browser_file_upload` is upstream's
  and every upstream description passes through this proxy byte for byte, so the sentence
  goes on the one description BrowserAI writes that a caller reads first. The arm that
  asserts it asserts in the same breath that `browser_file_upload` is still upstream's own
  bytes.

  SIX SENTENCES IN THE `instructions` WERE TIGHTENED TO PAY FOR THE DELETION LINE, AND
  NOTHING WAS DROPPED. The string was 2,022 characters of the client's 2,048 — measured off
  the published binary's own `initialize` response — with 26 to spend and a clause that
  costs 105. Every rule is still in it: the no-default rule, the `why` rule, the
  route/network mocking warning with its `on screen` clause intact, the never-install-browsers
  sentence verbatim, the full-page cost line. What moved is wording, and `ServerInstructions`
  records each change beside the one it replaced. Measured after: `instructions` 2,026
  characters of 2,048, `browserai_init` 1,877, `browserai_resume` 1,367,
  `browserai_destroy` 1,072.

  The move and copy tools were considered and deferred, and so was every way of widening
  the sandbox; both are recorded in `DECISIONS.md` with the maintainer's words and the
  alternatives that were weighed.

- ✅ **Four arms hold the session-lifetime rules on the published binary's own wire.**
  `ModelSurfaceTests.TheAgentIsToldThatDestroyingTheSessionsItMakesIsItsOwnJob`,
  `.TheOnlyFolderAFileCanBeUploadedFromIsNamedWhereTheSessionDirectoryIs`,
  `.DestroyingASessionIsSaidToTakeTheScreenshotsAndDownloadsWithIt` and
  `.MovingASessionDirectoryByHandIsOnTheResumeDescription`, each planted and watched red
  before the sentences went in — 18 of the 19 required phrases named as absent, and the
  other two present already, which is the positive control that the scan can find a phrase
  that is there.

  OFF THE WIRE RATHER THAN OFF THE CONSTANTS, for this file's standing reason: these
  strings are assembled from concatenated constants and interpolated tables, and a sentence
  that exists in source and never reaches `tools/list` is the failure being guarded
  against. Phrases rather than whole sentences, because the wording is not the maintainer's
  the way the browser-installation sentence is — what must survive a re-draft is the rule,
  not the draft.

- 💥 **The payload could not be rebuilt until the override's exit was adjudicated on 2026-09-21.**
  ✅ **ADJUDICATED AND CLOSED THE SAME DAY, 2026-09-21** -- see the roll entry below.
  This entry stands as the record of the state the day opened in; nothing in it is
  retracted, and the answer was to roll rather than to widen the comparison.

  `@playwright/mcp` moved 0.0.81 -> 0.0.82 on 2026-09-18, and 0.0.82 declares
  `playwright-core` and `playwright` at `1.64.0-alpha-1789764292000` -- a 13-digit
  epoch-milliseconds alpha rather than the `alpha-YYYY-MM-DD` shape upstream publishes
  daily. The dated override's exit is a wrapper pin at or above `1.64.0-alpha-2026-09-17`,
  and both instruments that read it share one regex and REFUSE anything else by design,
  because silently calling an unorderable shape 'lower' would keep an override alive past
  its own exit.

  So on a rebuild `build/Build-Payload.ps1` would throw before assembling anything and
  `PayloadTests.TheDatedPlaywrightCoreOverrideIsStillNeeded` would go red with a
  `FormatException`, each naming the version and saying to adjudicate the override in
  `DECISIONS.md` by hand. Neither says the exit has fired and neither says it has not. Both
  are green on the committed tree today, which still records 0.0.81 and
  `1.64.0-alpha-2026-09-14`, so no gate is red and nothing is blocked until somebody
  rebuilds.

  IN SUBSTANCE THE WRAPPER HAS CAUGHT UP AND PASSED THE OVERRIDE, and that was measured
  rather than reasoned. 1789764292000 ms is 2026-09-18T20:44:52Z; that `playwright-core` was
  published 2026-09-18T20:49:55Z against the override's 2026-09-17T05:26:56Z; and its
  `lib/coreBundle.js` carries the fix the override was taken for -- `file-paths` 3
  occurrences and `filePaths` 8, identical to the override's own bundle, against 0 and 2 in
  `1.64.0-alpha-2026-09-14`, which is the negative control proving the grep discriminates
  rather than matching everything. The condition the exception was written to expire on has
  occurred, and the instrument built to announce it cannot say so.

  Nothing is adopted, the override stands untouched, and the comparison was not edited to
  make it answer: a shape it cannot order is a shape a human has to adjudicate, which is
  what it was written to say and what `PayloadTests.TheExpiryComparisonFiresInBothDirections`
  already asserted for this class. Reported rather than taken, read out of the fetched
  tarball's own `browsers.json`: adopting 0.0.82 would move chromium 1245 -> 1246 at the
  same 154.0.8037.0, firefox 1548 -> 1549 with browserVersion 155.0 -> 156.0, and webkit
  2361 -> 2365 at the same 26.6, with ffmpeg 1011 and winldd 1007 unmoved. The daily cadence
  is unchanged and still dated, so the pinned alpha is an out-of-band build and the wrapper
  pinned from a different shape family than the one the ordering was written against.

- 📝 **The charter's version-chain example prints the chain that ships, and an arm holds it there.**
  `DECISIONS.md` section 2 argues that the version chain floats by printing a worked
  example, and that example had read `@playwright/mcp` 0.0.79 -> `playwright-core`
  1.63.0-alpha-2026-08-05 -> chromium rev 1237 since the charter was written, under a
  sentence calling `chromium-1237` the one at the end of our chain. Self-consistent, true of
  one day in August, and false of what ships for weeks.

  It is corrected by addition rather than by replacement, because the 0.0.79 chain is a true
  record of what `launch.ps1` resolved on the day the argument was made, and the argument is
  about the shape of the chain rather than about any link in it. The chain as it ships now
  stands beside it, read from the payload lock and the committed `browsers.json` snapshot
  rather than from memory: `@playwright/mcp` 0.0.81 -> `playwright-core`
  1.64.0-alpha-2026-09-17, pulled forward by the dated override over the wrapper's own
  1.64.0-alpha-2026-09-14, -> chromium rev 1245 (154.0.8037.0). The middle link is the one
  variation the old chain could not show, and the text says so rather than leaving a reader
  to notice that a pin is above a pin.

  The mechanised half is `PayloadTests.TheWorkedExampleStatesTheChainTheCommittedRecordsState`,
  which reads the numbers out of the fenced block the prose introduces and holds each to the
  record it came from -- two out of `build/payload/package-lock.json`, the revision and the
  browser version out of `upstream-snapshots/browsers.json`. Every source is committed, so
  the arm runs on a clean clone with no payload assembled. It was watched red three ways:
  five links at once, each naming its own source; a deleted line, where a pattern that
  stopped matching reports that it can no longer tell rather than reporting nothing; and a
  reworded anchor, which throws and says to re-anchor rather than to delete. The wrapper's
  own declared pin is read only while an override is in force, because it is the override
  that gives the example a fourth number to print.

  WHAT IS DELIBERATELY NOT READ is the 0.0.79 chain above it. It is a record of one day, and
  holding a record to today's manifest would demand it be rewritten at every roll, which is
  the same exemption `ThirdPartyNoticeTests` gives a correction stamp's previously span. The
  0.0.79-era chain was swept for elsewhere with a positive control -- the sweep had to find
  the known instance before a zero anywhere else meant anything -- and section 2 is the only
  place that printed it as current. Every other occurrence is a dated provenance stamp,
  including the kb "Versions in force" headers, which say in `kb/README.md`'s own words that
  they record what entries were measured under; rolling one would falsify the record rather
  than update it.

  `BrowserAiPaths.BrowserVersionOf` joins `RevisionOf` beside it, so the suite still has one
  reader of that snapshot rather than two. A revision bump at an unchanged browser version
  is a rebuild of the same browser and a browser version move is a new browser, and a
  document that prints the pair cannot be held to that difference off the revision alone.


- 📝 **The README licensing tables name Firefox, the third Playwright package, and today's revision.**
  Yesterday's notices correction closed two omissions in the file that ships and left the
  table in `README.md` that says the same things standing, one document across. It read
  "full chromium 1237" against a payload that resolves 1245, carried a
  `chromium-headless-shell` row for a tree nothing has provisioned since `--no-shell` on
  2026-08-16, and had no Firefox row at all although Firefox has been a provisioned family
  since 2026-08-19.

  Every cell is corrected by addition from the licensing re-read of 2026-09-17 rather than
  by reasoning from it. The Chromium row states 1245 and says the reading was re-taken
  there -- the only licence-adjacent file among that tree's 308 is ABOUT, 257 bytes. A
  Firefox row sits beside it: MPL-2.0 headline with Apache and BSD terms besides, no
  standalone licence file in 61 files, the terms inside `omni.ja` as `license.html`, which
  is what about:license renders. The headless-shell row is struck with the reason, and its
  1237 is deliberately left where it is, because a number about a tree nobody has must not
  be rolled as though somebody did. The redistribution table above them names `playwright`,
  and the sentence saying upstream publishes no NOTICE goes with it: two of the three ship
  one, 254 bytes, byte for byte each other's.

  The mechanised half reads the same sources the notices arm does and one more. A new arm
  holds every package in `build/payload/package-lock.json` to being named somewhere in the
  section, every family in `ProvisionedBrowsers.Families` to a row of its own, and every
  revision a row states to what the committed `upstream-snapshots/browsers.json` says --
  which the build regenerates from the resolved payload, so all three sources are committed
  and the whole arm runs on a clean clone. It was watched red on all four of today's
  defects at once: "the payload ships 'playwright' and README.md's third-party components
  section does not name it", "'firefox' is a provisioned family and README.md's 'What the
  user's machine downloads' table has no row of its own naming it", "README.md's ... table
  says 'chromium 1237' and the committed browsers.json snapshot says 1245", and the fourth
  saying no revision stands beside firefox at all.

  What it cannot read is the prose inside a cell, and the README says so rather than
  implying it: whether the terms a row names are the terms in that tree is a measurement,
  dated in the row that states it and re-taken by row 26 of the re-verification index. A
  correction stamp's `previously "..."` span is cut out before the revisions are read,
  because holding a record of what a cell used to say to today's manifest would demand the
  record be rewritten at every roll, which is the opposite of what a stamp is for.

- 📝 **The session ledger is snapshotted again, and a re-snapshot is not an edit.**
  `docs/ledger/2026-09-15-release-session.md` was taken on the 16th and the live copy has
  grown by 118 lines since: two more re-ships, the `playwright-core` pull-forward and its
  written exit, the re-verification batch against chromium 1245 and firefox 1548, and the
  follow-ups through Q215. The body is replaced verbatim with `.work/STATE.md` and the
  two-line header now names the commit it was taken at.

  The ledger README says nothing here is edited after the snapshot, and that sentence read
  as forbidding this. It does not, and it says so now: the rule is about the copy, and
  taking the copy again from a file that has only grown replaces a shorter prefix with a
  longer whole. That was checked rather than trusted -- the previous body is a byte-exact
  prefix of the new one, 61,187 bytes shorter, with nothing above the new material touched.
  Nothing enforces it and the README says that too: a sealed prefix would forbid the
  re-snapshot rather than the edit, which is why a ledger sits outside the append-only
  record test.

  Scanned for credentials before it landed, with a positive control first, as the 16th did:
  seven shapes -- GitHub PAT, AWS access key, private-key header, bearer token, generic
  secret assignment, Slack token and any e-mail address at all. The control, a planted
  example of each, matched 7 of 7. The ledger matched 0 of 7. The first attempt at that
  scan matched only 4 of 7 on the control, because three of the patterns begin with a
  hyphen or use PCRE inline flags and grep read them as options: a pattern that can never
  match reports clean, which is the whole reason the control is run first.

- 🗑️ **The idle-CPU axis is retired as not established, and the other three are unchanged.**
  Yesterday's re-take withdrew the number and left what to do about the axis open. It is
  closed now: idle CPU is not a row of the Firefox-against-Chromium cost ratios any more,
  and it is a row in what this project has not established instead. The evidence is four
  things. Three readings, two sign reversals, the second inside a single day: about 24
  times in August, 0.77 times on 2026-09-16, 1.31 times on 2026-09-17. Distributions that
  overlap completely, Chromium 360 to 955 ms against Firefox 266 to 781, with Firefox's
  lowest round below Chromium's lowest and Chromium's highest above Firefox's highest. A
  control the run carried and nothing else on this axis has ever had: Chromium is
  byte-identical across revisions 1244 and 1245, so its own column between the two runs
  measures the rig, and it moved minus 41 per cent there against 0.9 per cent on resident
  set and 0.0006 on profile disk. An axis whose control moves 41 per cent cannot resolve a
  31 per cent difference, so more rounds of the same instrument will not settle it.

  One thing is established and is kept rather than retired: Firefox does not burn an order
  of magnitude more idle CPU than Chromium, which is what all three readings agree on and
  what refutes the recorded 24 times. Re-opening the question needs a different instrument
  -- a longer window, or CPU sampled rather than differenced, on a machine with nothing
  else running -- and not another six rounds.

  Nothing consumes the axis, and that was checked rather than assumed. The four ratios
  stopped being the ground under the default browser family on 2026-09-17, when that
  decision was re-grounded on the maintainer's own reason. A grep found two sentences that
  still argued from the sign reversal, in the charter's browser-families row and in
  SessionManager's doc comment, and both are corrected by addition: each said idle CPU
  reversed sign while explaining why the cost argument no longer carries the default, and
  each now rests on the three axes that are measured. The conclusion does not move.

- 📝 **The third-party notices name the third Playwright package and both browsers we provision.**
  `THIRD-PARTY-NOTICES.txt` said "the two Playwright packages" while three ship. `playwright`
  is `@playwright/mcp`'s other exact dependency and has been in the payload since the first
  build of one; its LICENSE and NOTICE are byte for byte the same as `playwright-core`'s, so
  what was missing was a name rather than a licence. It is in the table now with its three
  paths, the prose says three, and the file states the resolved versions of `playwright` and
  `playwright-core` because an npm override separated them -- both read back out of the
  payload lock rather than typed, so a roll is red until this file has been read against the
  tree it describes.

  Firefox has been provisioned on demand since 2026-08-19 and the notices named it only in a
  list of things no copy of which ships, which answers what we owe and does not answer what
  somebody has just installed. A "Browsers provisioned on first run" block now names each
  family, what it is, where it is fetched to, and where its own terms live: for Chromium the
  ABOUT file pointing at Google's Chrome Terms of Service, and for Firefox the terms inside
  `omni.ja` as `license.html`, because there is no standalone licence file in that tree at
  all.

  Neither omission was findable by anything that existed, and that is what actually changed.
  The obligations were a list of paths somebody typed, so the list could only be wrong in the
  direction of naming a path that is not there. The new arm enumerates
  `build/payload/package-lock.json` and `ProvisionedBrowsers.Families` instead, so a fourth
  package in the payload or a third browser family is a red build. It was watched red on
  exactly the two omissions and the three paths under them: "the payload ships 'playwright'
  and THIRD-PARTY-NOTICES.txt does not name it" and "'firefox' is a provisioned family and
  THIRD-PARTY-NOTICES.txt has no entry for it".

- 📦 **The release manifest records the pulled-forward dependency, and copies the file that pins it.**
  The manifest could already say a human had HELD an upstream BACK. Since 2026-09-17 the
  payload does the opposite: an npm overrides entry in `build/payload/package.json` ships
  `playwright-core` 1.64.0-alpha-2026-09-17 underneath an `@playwright/mcp` 0.0.81 that
  declares 1.64.0-alpha-2026-09-14 for itself, and `playwright` ships at the declared
  version beside it, so a release carries two Playwright versions at once and nothing in
  it said so. `pulledForward` is emitted on every release now, null when no override is in
  force and a block per overridden package when one is. Every number in it is read rather
  than typed: the shipped version and each package's declared version out of the payload
  lock, the pin out of the payload manifest, which is also the exit condition -- the day
  every declarer names a version at or above the pin, the override is deleted.

  The eighth file is `build/payload/package.json` itself, and it is copied for a reason
  the lock cannot serve: npm writes no overrides block into the lock it produces, measured
  2026-09-17, so a reader holding the lock alone sees a resolved version and nothing saying
  anybody chose it. The two keys are deliberately not merged. `override` is a version held
  back and `pulledForward` is one shipped ahead, they can both be in force at once, and the
  manifest's own schema text says which is which -- every manifest written before today
  reads `override: null` and is silent about the other direction, which is the state this
  closes. Both arms were watched red first: the eight-file arm on `Expected to be empty but
  received "payload.package.json"`, and the no-override control on `Expected to contain
  ""pulledForward": null"`.

- 📝 **Resume is two paths now, and each was timed at 1245 and 1548.**
  Re-verification row 38. The load-bearing half held exactly and now on two families:
  cookie, localStorage, IndexedDB, CacheStorage and one service-worker registration all
  survived, sessionStorage alone did not, identically on Chromium and on Firefox, which is
  this row's first Firefox reading. That is the measurement the no-expiry-timer decision
  rests on, so it holding on a second family is worth more than the cost figure moving.

  The cross-process cost moved 336 and 367 ms to 375 and 379, about 9 per cent, and
  Chromium cannot be the reason: 1245 and 1244 are the same 308 files at the same sizes
  with chrome.exe identical to the byte. What changed under the number is the server, or
  the machine, which drifted 15 to 20 per cent the same day on an unchanged binary, and
  two readings cannot tell those apart, so no attempt is made to. Firefox resumes within 4
  per cent of Chromium at 389 ms although it is 4.37 times slower to first navigate, which
  is the first evidence rather than argument that a resume is about the directory and not
  about the browser.

  The second path is new since this morning: browserai_resume relaunches a child that has
  died, so the one-server shape that used to be a 7.68 ms no-op leaving a wedged session is
  now a resume in its own right. Timed for the first time here at 345.77 and 330.92 ms,
  with the next browser_navigate returning a real page in 444 and 426 ms where it had never
  returned in 900,000 ms. That confirms the prediction the wedge row wrote down and nobody
  had yet run, and the wedge section -- which still read as current -- now says by addition
  that everything in it is true of the slice it was measured against and false of this one.
  Not re-measured, and the entry says so where it says it: a forward made WITHOUT a resume,
  and therefore the door-refusal added for that case.

- 📝 **The cost ratios are re-taken at 1245 and 1548, and the idle-CPU axis is withdrawn.**
  Re-verification row 34, at six rounds per family rather than the stated three, because
  the axis that had flipped sign is the one three rounds cannot settle. RAM holds at
  1.19x, profile disk at 2.76x on every one of six rounds, and processes at 0.78x. First
  navigate moved 4.62x to 4.37x, inside the per-round band of both runs. Second navigate
  moved 0.90x to 0.68x, which retires this article's own sentence calling that row the
  one where the two families are genuinely close. Idle CPU read 0.77x in the morning and
  1.31x in the evening -- a second sign reversal inside one day -- and what follows is a
  conclusion about the instrument rather than about either browser: Chromium's six rounds
  span 360 to 955 ms against Firefox's 266 to 781, the two distributions overlap
  completely, and across 24x, 0.77x and 1.31x exactly one thing is established, which is
  that Firefox does not burn an order of magnitude more idle CPU than Chromium. Which
  burns more is not established and more rounds of the same instrument will not establish
  it; what to do about the axis is left open rather than decided.

  The run carries a control the morning's could not: Chromium did not change. 1245 and
  1244 hold the same 308 files at the same sizes with chrome.exe identical to the byte, so
  Chromium's own column between the two runs measures the rig rather than the browser --
  profile disk repeatable to 0.0006 per cent, resident set to 0.9 per cent, first navigate
  to 20 per cent, second navigate to 27, and idle CPU to minus 41. Both families also ran
  15 to 20 per cent slower in the evening while the ratio moved 5 per cent, which is
  evidence for this section's own claim that the ratio is the transferable half rather
  than an assertion of it. The procedure gains a sentence with it: both families must run
  in one sitting, because the two behind this section were six hours apart.

- 📝 **Four claims in the payload licensing row did not survive a re-read at 1245 and 1548.**
  Re-verification row 26 said `winldd` ships no licence file and full Chromium ships no
  OSS one. Both still hold, and so do `ffmpeg`, Node and `@playwright/mcp`. The Chromium
  half could not have moved and the reason is evidence rather than an assurance:
  `chromium-1245` and `chromium-1244` hold the same 308 files at the same 308 sizes and
  `chrome.exe` is the same SHA-256 in both, because the archive is keyed on
  `browserVersion` and 154.0.8037.0 did not move with the revision. It was re-read
  anyway. What did not survive is everything the debt note had not scoped. The row said
  no `NOTICE` file is published upstream so Apache-2.0 section 4(d) has nothing to
  propagate; `playwright-core` and `playwright` each ship one, 254 bytes and identical to
  each other, plus three per-bundle sidecar licences apiece, and the shipped
  `THIRD-PARTY-NOTICES.txt` has named that path since the day it was written, so the
  defect was in the knowledge base and never in the artifact. The
  `chromium-headless-shell` row describes a tree nothing provisions: `--no-shell` has
  been passed since 2026-08-16, two days after the row was read, and no
  `chromium_headless_shell-*` directory exists at any revision. A third Playwright
  package, `playwright`, ships and was never listed, while the notices file says "the two
  Playwright packages" — its terms and NOTICE text are byte-identical to
  `playwright-core`'s, so a name is missing rather than a licence, and that is reported
  rather than fixed because what ships beside the binary is the maintainer's call. And
  Firefox, a provisioned family since 2026-08-19, was never listed either: 61 files and
  no standalone licence among them, with the terms inside `omni.ja` as `license.html`,
  byte-identical across 1544 and 1548. The whole 1544-to-1548 move turns out to be five
  entries inside one `omni.ja`, four of them Playwright's own juggler files, which is the
  entirety of the 902 bytes row 21 measured on disk.

- 📝 **Disk after a first run is a measured number again: 598 MB.**
  The total had gone `[STALE]` that morning because both of its addends had
  moved under it. It is re-derived from two figures taken the same day and
  nothing else, which is the property the old sum lacked: the 1.0.0 release cut
  that afternoon installed itself on the reference machine, so `current\` was
  weighed as the thing the sentence describes rather than reconstructed —
  143,574,278 B across 207 files — and `chromium-1245` weighs 454,699,952 B
  across 308 files, for 598,274,230 B = 570.56 MiB. The stale stamp is left
  standing above it as the record of what the debt was, and the entry says out
  loud that the total will go stale again on the next build, because the
  `current\` term moves with every publish. That is a property of a derived
  total rather than a defect in this one.

- ⬆️ **TUnit moved 1.68.4 -> 1.68.17 and this time the testing platform moved with it, 2.4.0 -> 2.4.1.**
  `dotnet restore --force-evaluate` on 2026-09-21 re-resolved the float and moved one
  project's lock and no other: `tests/BrowserAI.Tests/packages.lock.json`. TUnit,
  `TUnit.Assertions`, `TUnit.Core` and `TUnit.Engine` went 1.68.4 -> 1.68.17;
  `Microsoft.Testing.Platform`, `.MSBuild`, `.Extensions.Telemetry` and
  `.Extensions.TrxReport.Abstractions` went 2.4.0 -> 2.4.1, `.Extensions.TrxReport` went
  2.3.3 -> 2.4.1, and `Microsoft.Testing.Extensions.CodeCoverage` went 18.10.0 -> 18.11.2.

  THE PLATFORM MOVED BECAUSE TUNIT LET IT, WHICH IS THE SAME MECHANISM THAT HELD IT BACK
  LAST TIME AND NOT A DIFFERENT ONE. The 2026-09-17 entry above records
  `Microsoft.Testing.Platform` staying at 2.4.0 while 2.4.1 already existed, because TUnit
  1.68.4 declared an exact `Microsoft.Testing.Platform 2.4.0` and NuGet resolves the lowest
  applicable version. Read out of the restored package on 2026-09-21, `tunit.engine.nuspec`
  at 1.68.17 declares `Microsoft.Testing.Platform 2.4.1`, `Microsoft.Testing.Platform.MSBuild`
  2.4.1 and `Microsoft.Testing.Extensions.TrxReport.Abstractions` 2.4.1 in every one of its
  four target-framework groups. So the float is doing exactly what it did in September and
  the number it lands on is upstream's choice rather than ours.

  The two-step the build uses was run in full: `--force-evaluate` then `--locked-mode`, both
  exit 0, so the lock describes the tree it produced. Solution build after the move:
  0 warnings, 0 errors. The three `src/` locks did not move at all, which is the sentence
  worth having -- `ModelContextProtocol`, `Velopack`, the ILCompiler and ILLink packages and
  CsWin32 all re-resolved to what they already recorded.

- ⬆️ **The payload rolled to `@playwright/mcp` 0.0.82, and three browser revisions came with it.**
  `playwright-core` and `playwright` both resolve to `1.64.0-alpha-1789764292000` now,
  which is one Playwright version in the payload again rather than two. The browsers moved
  with it, confirmed from the rebuilt payload's own `browsers.json`: chromium **1245 ->
  1246** at the same `browserVersion` 154.0.8037.0, firefox **1548 -> 1549** with
  `browserVersion` **155.0 -> 156.0**, webkit **2361 -> 2365** at the same 26.6, with
  `ffmpeg` 1011 and `winldd` 1007 unmoved. Only chromium and firefox are provisioned by
  this product; webkit is never installed.

  EVERY MACHINE RE-PROVISIONS, AND THE COST WAS RE-MEASURED RATHER THAN CARRIED FORWARD.
  Two clean runs per family through the preserved rig, byte-identical within each pair,
  plus a `HEAD` on each of the four archives. **Chromium did not move at all, for the
  third roll running** -- `cftUrl()` is keyed on `browserVersion` rather than on the
  revision, so 207,274,189 B on the wire and 458,475,923 B across 316 files on disk are
  identical to 1245 and 1244. **Firefox is the first roll here that is a new browser
  rather than a rebuild**: 130,934,199 B on the wire (+1,431,551) and 365,579,913 B
  across 71 files on disk (+3,458,122), with its own tree going 61 -> 63 files.
  `BrowserProvisioner.FirstRunDownloadBytes` quotes that figure to every caller refused
  while provisioning runs, so it moved with the measurement and the anchor test holds the
  two together.

  THE REVIEW FOUND TWO UPSTREAM BEHAVIOUR CHANGES NO SNAPSHOT COULD HAVE SHOWN, and both
  were measured rather than read. `tools/list` is no longer a static surface: the child
  declares `listChanged` and appends the current page's own WebMCP tools to its list, so a
  **web page** can add tools with its own descriptions and schemas. And every
  snapshot-bearing tool result now carries the page's tool listing inside the snapshot.
  **Through BrowserAI the first is closed twice over and the second is not**, which is
  written up below.

- 🗑️ **The dated `playwright-core` override is removed, four days after it was taken.**
  It pinned `playwright-core` to `1.64.0-alpha-2026-09-17` underneath `@playwright/mcp`
  0.0.81, to pull [microsoft/playwright#42673](https://github.com/microsoft/playwright/pull/42673)
  -- `--file-paths=absolute`, this project's own ask #42497 -- one build ahead of what the
  wrapper declared. 0.0.82 declares a `playwright-core` whose own bundle carries that fix,
  measured by the same grep and the same negative control the override was taken on:
  `PLAYWRIGHT_MCP_FILE_PATHS` 2, `file-paths` 3, `filePaths` 8, identical to the override's
  own bundle against 0 and 2 in the build the wrapper used to pin. So the lag the exception
  existed for closed, which is exactly the condition it was dated against.

  THE EXIT DID NOT FIRE THE WAY IT WAS WRITTEN TO, AND THE INSTRUMENTS WERE RIGHT. Both
  compared versions through a regex matching `<major>.<minor>.<patch>[-alpha-YYYY-MM-DD]`,
  and 0.0.82 pins a 13-digit epoch-milliseconds alpha from a different shape family, so
  each threw and said a human must adjudicate. That was the designed behaviour, not a
  defect -- silently calling an unorderable shape lower is what keeps an override alive
  past its own exit -- and it is what put the decision in front of a person on the right
  day. **Widening the regex was available and was declined.**

  `TODO.md`'s standing watch item is closed with its condition, its date and the commit;
  ask #1 is closed with it, because the fix now arrives by the wrapper's own pin.
  `DECISIONS.md` records the second exception as **ENDED** by addition -- opened
  2026-09-17, closed 2026-09-21 -- and **still counts two**, because an exception that ran
  its course is a worked example of how one is allowed to work rather than a slot that
  reopens. The sentence that neither is a precedent stands. `drift-check.json`'s
  `_how_to_resolve` is back to the plain rule.

- ✅ **The exit test, its positive control and the version ordering underneath them are retired.**
  `PayloadTests.TheDatedPlaywrightCoreOverrideIsStillNeeded`,
  `.TheExpiryComparisonFiresInBothDirections` and
  `.TheResolvedPlaywrightCoreIsWhateverTheOverrideSays` are deleted, with
  `Compare-PlaywrightVersion` and `Split-Version` in `build/Build-Payload.ps1`.
  **Deleting a test for a mechanism that no longer exists is not a skip**, and each
  deletion is recorded where the test was referenced rather than removed without trace:
  `TODO.md`, `DECISIONS.md`, `RELEASING.md` and the paragraph in this README that says
  what every move in the test count was planted against.

  NOTHING IN THIS TREE RANKS A PLAYWRIGHT VERSION NOW. Every comparison left is for
  **identity** against another recorded string, so a version is an opaque string and its
  shape is upstream's business. **The property that survives is the one that was always
  the real one**, and it came back to exactly where it stood before 2026-09-17:
  `PayloadTests.TheLockRecordsUpstreamsOwnExactPinOfPlaywrightCore` asserts the resolved
  `playwright-core` **equals** the declared one again -- watched red against a doctored
  lock -- and `build/Build-Payload.ps1` makes the same check against the live resolution.
  That is also what catches an override being added back, which is a mechanism this rule
  did not have while the exception stood.

  `SessionPolicyTests.TheWebMcpCallIsWithheldOnLivenessAndTheWebMcpListIsNot` is retired
  too, for the same reason and a different cause: every premise it rested on is gone.

- 🔒 **A web page can add tools to the child's `tools/list`, and BrowserAI's surface does not move.**
  Measured three ways on the resolved payload, against a page registering two WebMCP tools
  with deliberately unmistakable descriptions. The **child**'s `tools/list` went 72 -> 74,
  it sent `notifications/tools/list_changed`, and the page's own tool names, annotations,
  descriptions and `inputSchema`s arrived inside the snapshot of every snapshot-bearing
  result. With `webmcp: false` written: 72 -> 72, no notification, no header line, no
  snapshot block.

  THROUGH THE PUBLISHED SERVER, BOTH HALVES OF THE EXPOSURE ARE CLOSED AND NEITHER
  MECHANISM WAS BUILT FOR THIS. BrowserAI's `tools/list` was **78 before the page and 78
  after** -- nothing was added -- because `tools/list` is answered from the run's own
  child, which never navigates and so has no page to collect from. And a `tools/call`
  naming `webmcp_probe_tool_alpha` was refused at the door with the unjudged-tool
  sentence, nothing forwarded and nothing started, which is deny-by-default meeting a name
  **a web page invented**.

  WHAT IS NOT CLOSED IS THE TEXT, AND IT IS REPORTED RATHER THAN DECIDED. Tool results are
  forwarded verbatim by design, so page-authored descriptions and schemas now reach a
  caller on every snapshot-bearing call. `webmcp: false` removes all of it and BrowserAI
  writes no `webmcp` key today, so upstream's default is in force. That is a product
  stance under the written-rather-than-omitted doctrine, the same class as `timeouts.idle`
  and `imageResponses`, and it is the maintainer's. The rig is
  `docs/probes/2026-09-21-webmcp`, the transcripts are in `docs/evidence/2026-09-21-webmcp`,
  and the finding is re-verification row 133.

- 🔧 **Two tools left the surface and nobody here judged them out.**
  `@playwright/mcp` 0.0.82 marked `browser_webmcp_list` and `browser_webmcp_call`
  `skillOnly`, so both left the exposed maximum while keeping capability `core`: 74 -> 72
  exposed, 27 -> 25 default, 9 -> 11 skill-only, every one of the three moved by the same
  two tools. **No tool was added and none was renamed**; the internal registry is unmoved
  at 83 and every surviving entry is byte-identical in position.

  Their verdict rows are deleted, which is `UPSTREAM-REVIEW.md`'s own instruction for a
  tool upstream removed and the direction `ToolVerdictTests` refuses on purpose -- a row
  naming a tool the snapshot does not carry is a judgement about nothing. **THE DENY'S
  REASONING IS PRESERVED RATHER THAN LOST**, in `tool-verdicts.json` and
  `upstream-review.json`, because it is what a future judgement would need:
  `browser_webmcp_call` was denied on **liveness**, and re-reading the 0.0.82 bundle
  confirms the call path is still unbounded while the new 5 s frame timeout bounds only
  the listing. If upstream puts these back on the wire, that deny stands until a human
  re-judges it.

  BrowserAI now advertises **71 of the 72** tools a fully-capable child exposes, and the
  withheld set is back to one, `browser_annotate`. Every published count moved with it and
  was re-counted off the regenerated snapshot rather than decremented.

- 📝 **Four re-verification rows are marked `[STALE]` and owed, and the reason is on each.**
  Row 26 is payload licensing as shipped, and firefox **156.0** is exactly the case where
  the terms inside `omni.ja`'s `license.html` can change. Row 34 is the Firefox-against-
  Chromium cost ratios, where the Chromium side is the same binary and the Firefox side is
  a new browser -- the shape that moves a ratio. Row 38 is resume and relaunch timings
  under both families. Row 85 is the update lane's own numbers, every one of which is
  derived from a payload that moved 18,659,660 -> 18,697,570 bytes.

  Each needs a measurement session rather than a read, and **none was adjusted from the
  previous figures**. Rows 10, 11, 12, 17, 19, 21, 48 and 69 were answered in this batch:
  three unchanged, four re-counted off the snapshot, and row 21 re-measured because the
  product quotes it. Row 17 was taken with the previous bundle as its positive control,
  fetched with `npm pack`, and it returned exactly what the row recorded.

- 📝 **The session ledger is snapshotted for the last time, and the scratch folder is
  empty.** `docs/ledger/2026-09-15-release-session.md` was taken on the 16th, re-taken on
  the 18th, and is re-taken here at `75011ea` with everything the live copy gained since:
  the WebMCP measurements, the page-tool pass-through decision and `browserai_page_tool`,
  the onboarding guard, the four owed re-verification rows, and the questions left open at
  the close. The body is replaced verbatim with `.work/STATE.md` and the previous body is
  a byte-exact prefix of it, 41,332 bytes and 76 lines shorter, with nothing above the new
  material touched. It was scanned for credentials first, against a positive control
  carrying all seven shapes the scan looks for: the control matched 7 of 7 and the ledger 0
  of 7.

  **What makes this one final is that the live copy is gone.** `.work/` is ephemeral by
  charter, and it is emptied in this same commit -- `STATE.md` and one screenshot of a
  GitHub settings page whose only load-bearing fact, the `og:image` URL, is already in the
  ledger. So from here the snapshot is not a copy of the record, it **is** the record, and
  `docs/ledger/README.md` says both halves: this is the last snapshot of that session, and
  a new session opens a new ledger beside it rather than appending to a closed one, because
  appending from a different session's live file would rewrite a body rather than extend
  one and no prefix check could tell the difference.

## [1.0.0] - 2026-09-17

BrowserAI is a Windows MCP server that gives an AI agent a real browser, either
Chromium or Firefox. It carries its own copy of everything it needs, so there is
nothing else to install.

This is the first version fit for real use. It replaces the build of the same
number published on 2026-08-17. That build was downloadable for a month. Nobody
is known to have installed it apart from the maintainer, and it did not exit when
it had nobody left to serve.

There are two programs in the release. `BrowserAI.Server.exe` is the MCP server
your client starts. `BrowserAI.exe` is a small configuration app. It opens when
the install finishes, and it registers the server with your client.

Read [`README.md`](README.md) first.

### Added

- ✨ **BrowserAI ships as two executables from this release.** BrowserAI ships as
  two executables from this release, and the reason is a window nothing could
  suppress. A non-silent `Setup.exe` finishes by starting the main executable
  itself, through `shared::start_package`, with `CREATE_UNICODE_ENVIRONMENT`
  **and nothing else** — `show_window = true` is hardcoded at
  `util_windows.rs:106` — and the stub and `Update.exe start` launch it with no
  flags at all. A console-subsystem binary started that way from a windowless
  parent is given a console, and on this machine the default terminal turns that
  into a **1506×1490 Windows Terminal window** over the user's work, serving
  nobody. **Nothing suppresses it**: 36 `vpk` options, 16 manifest fields, 7
  environment variables and every hook return were enumerated and only
  `--silent` skips the start, as a side effect of hiding every dialog and
  answering yes to every prompt. A **Windows-subsystem** binary is never
  allocated a console at all (*Learn: windows/console/creation-of-a-console*),
  so the main executable is now one. That the window is then **useful** was the
  second decision and it followed the first.

- ✨ **`BrowserAI.exe` is the configuration app, and it opens after the install.** `BrowserAI.exe`
  is the configuration app — the Velopack main executable, the root stub, the
  Start Menu entry, the icon, and the owner of all four installer hooks. It is
  deliberately very small: it shows the installed version, where the install and
  the data live as links that open Explorer, what is registered with Claude
  Code, and it offers five things, each behind **one explicit click** — check
  for updates (and then install one), register for all your Claude Code
  projects, unregister, register in a project you pick, and open the logs.
  **Nothing runs on open except reading state**, and a first run shows what
  happened rather than asking anything.

- ✨ **The two registration scopes are offered in OutlookAI's own words.** The
  two scopes are offered in OutlookAI's words, because two products in one
  estate describing one mechanism differently is how a person learns it twice.
  *"For all my Claude Code projects"* is user scope — one entry in
  `~/.claude.json`, available in every repository, no file in any of them.
  *"Register in a project…"* is a folder picker and a committed `.mcp.json` at
  that folder's root, written with `claude mcp add --scope project` run **in
  that directory**, because that is the only thing that decides where the file
  lands. The command written there is **portable** —
  `${LOCALAPPDATA}/BrowserAI.app/current/BrowserAI.Server.exe`, which Claude
  Code expands itself — and only when that form expands to the install this
  process is running out of; a non-default install root gets its absolute path
  and the person is told why. Both actions are followed by the restart hint
  verbatim, and the project one adds that Claude Code will ask for approval once
  per project.

- ✨ **`BrowserAI.exe --report <path>` writes a JSON status report and exits.** It
  is a support artifact — the file somebody attaches when they say *it is not
  working* — and it is the configuration app's **testable non-interactive
  path**: a window application whose only entry point opens a window is one
  nothing can assert about. It renders the same `AppState` the dialog does, so a
  report that disagreed with the screen would be a red rather than a discovery.

- ♻️ **`BrowserAI.Core` is the library both executables share.** `BrowserAI.Core`,
  the library both executables link: the data root, the registration, the update
  feed, the live-instance census and the log. 33 files moved into it verbatim,
  namespaces unchanged. What did **not** move is the proxy, the sessions, the
  browsers and the storage layer — none of it reachable from a configuration
  dialog, and linking it into a second AOT binary would pay for the SQLite
  static library, the MCP SDK and the whole session machinery to compile a
  window that shows a version number. Two edges were turned around to make the
  cut acyclic and both are recorded where they landed:
  `LocalAppDataPaths.RootVariable` owns `BROWSERAI_ROOT` and `Program` aliases
  it, and `Sessions.SessionLayout` owns the session file names and
  `Storage.LockFile` / `Storage.SessionStore` alias them.

- ✨ **`Runtime/PeSubsystem` reads a binary's subsystem out of its own PE headers.** `Runtime/PeSubsystem`
  — eight bytes read at three documented offsets, no Win32 — and
  **`TaskDialogLayoutTests`**, which holds the hand-written `TASKDIALOGCONFIG`
  against Microsoft's own metadata through `CsWin32`: 160 bytes, 22 fields
  compared by offset, with a naturally packed copy of the same fields as the
  positive control at 184. **A size that agrees says nothing about a field that
  moved** — two swapped pointers leave the total unchanged and turn the window
  title into the instruction.

- ✅ **`RealInstallerTests.TheInstalledMainExecutableOpensOneDialogAndNoConsoleWindow` is new.** `RealInstallerTests.TheInstalledMainExecutableOpensOneDialogAndNoConsoleWindow`
  — the arm the whole design was cut for. It installs silently into a scratch
  root, launches `current\BrowserAI.exe` from a parent with no window, and holds
  that the process owns **exactly one visible top-level window**, of class
  `#32770`, no console window of its own, and exits **0** on `WM_CLOSE`. It
  polls rather than sleeping once: two runs of the same probe by hand disagreed
  at a fixed 2.5 s and agreed at 500 ms when polled. ⚠️ **The console half is by
  pid and that is weaker than it looks, and the remark says so**: with the
  default terminal set to Windows Terminal a console shows up as a window owned
  by *Windows Terminal's* process, which is exactly what reported a clean screen
  while two windows were on it. What carries that guarantee is the subsystem
  read out of the binary — the cause rather than the symptom.

- ✅ **`ConfigurationAppTests` covers the configuration app.** `ConfigurationAppTests`,
  which asserts every sentence, link and button of the dialog without opening
  one, and
  **`BuildConfigurationTests.TheConfigurationAppDeclaresTheVersionSixCommonControls`**,
  which refuses an app manifest without `Microsoft.Windows.Common-Controls`
  6.0.0.0. ⚠️ **That is the one entry whose absence has no compile-time signal
  at all**: the loader binds `comctl32` version 5, `TaskDialogIndirect` is not
  exported, and the application starts and nothing happens.

- ✅ **`HouseRuleTests.EveryArmInAFileThatOverridesTheEnvironmentRunsBesideNothing` is new.** `HouseRuleTests.EveryArmInAFileThatOverridesTheEnvironmentRunsBesideNothing`
  — a tree-as-text scan holding that every arm in a file under `tests/` that
  constructs an `EnvironmentScope` carries `[NotInParallel]` **with no key**. A
  keyed one does not satisfy it. **The file is the unit rather than the arm**,
  deliberately: the scope is usually opened through a helper — this repository's
  is a private factory returning one, reached by a target-typed `new(` that a
  scan looking for `new EnvironmentScope(` walks straight past — so an
  arm-by-arm rule would need call graphs out of text, and the arm that forgets
  is by definition the one nobody classified. **Watched red twice against the
  real tree**: with the keyed attribute of this morning restored, naming
  `RealInstallerTests`' arm; and with `RegistrationTests`' class attribute
  removed, naming all **20** of its arms one by one. Synthetic keyed, absent,
  keyless-on-the-arm, keyless-on-the-class, through-a-factory and no-scope
  controls run on every pass. **This is the assertable half of a race no timing
  test can plant** — the race needs one arm's few-second window to overlap
  another arm's browser launch, which is a scheduler outcome rather than a call,
  and a test that provoked it by timing would be the promptness claim
  `NoAssertionBoundsAMeasuredDurationWithANumberItInvented` forbids. It sits
  where `EveryRawHandleThatOutlivesItsExpressionIsRefCounted` sits and makes the
  same weaker claim in the same words: the attribute is *there*, never that the
  sandbox is correct. **It is not a second exception to the rule that a
  behaviour change is watched red** — the scan itself was.

- ✅ **The corpus every tree-as-text rule reads is now asserted against
  `git ls-files`, in both directions.** `HouseRuleTests.TheScannedCorpusIsExactlyWhatGitSaysTheRepositoryHolds`
  compares `RepositoryLayout`'s walk against `git ls-files --cached --others
  --exclude-standard`, filtered through one shared predicate so the two sides
  cannot ask different questions. **The remark it replaces was false by 520
  files**: the walk claimed to yield "the same 215 files as `git ls-files`",
  verified once by hand on 2026-08-17, and while agent worktrees sat under
  `.claude\worktrees\` — ignored by git, not pruned by the walk — every scan
  built on that list read a second checkout as repository content. The fragment
  scan counted **2,378** against a real **797**, and three gate arms went red
  for a reason no message named. **`.claude` is deliberately still not pruned**:
  `settings.json` and `hooks\` are committed, so a prune would have traded one
  blind spot for another. Git is an **oracle** here and never a source of truth
  — absent, the new `SuiteCapability.Git` reads ABSENT in the coverage block and
  the arm skips loudly, and a release run fails. Planted red both ways before it
  was trusted.

- ✅ **`SaturationTests`' torn-record arm is scoped to the run's own pids.** `SaturationTests`'
  torn-record arm is scoped to the run's own pids, and a second arm plants the
  fault it can no longer plant live. The hundred-process arm reads the
  machine-wide process log with **no time filter** — deliberately, because NTFS
  does not keep an mtime current while a hundred handles are open on the file
  and a filter there once hid the only file that mattered — so **one torn record
  written by any BrowserAI from any checkout failed it on every later run until
  the log rolled, and no checkout could clear it**. A sibling's tear at 03:38
  failed a run two hours later. The strength is unchanged for this run's writes:
  a tear counts if **either** end of it is one of this run's pids, because our
  write failing to be atomic against a stranger's is the same defect seen from
  the other side. Watched red with a synthetic tear in the shared log (1 m 21 s
  to fail), then green with the same plant in place.

- 📝 **The `--treenode-filter` trap, in
  [`kb/toolchain.md`](kb/toolchain.md).** The
  alternation character is an OR **inside one path segment** and never between
  path patterns: six class patterns joined with it select the **whole assembly**
  in one arrangement and the **first class only** in another, both reporting a
  clean pass, and one of them did so while two of the tests it claimed to cover
  were red. The proof it is not an OR at all is a pair that individually match
  nothing and together match everything. The correct syntax, the counts side by
  side and a re-establishment procedure are in the article; the rule — **a
  filtered run is a development convenience, never a verification** — is in
  [`CLAUDE.md`](CLAUDE.md), in the list of rules that need a person, with the
  reason no mechanism can close it stated there rather than implied.

- 📝 **The 2026-08-24 adversarial review is a dated record rather than a file in
  `.work\`.** It
  is the read-only pass over the seams where the 2026-08-20/24 changes — the
  mode removal, the injected `why`, the durable action log,
  `browserai_catch_up`, the reader/writer maintenance lock and the
  instance-directory marker — meet the code that predates them: nine findings
  and fourteen things attacked that held. It went into `docs/reviews/` **and
  into the append-only seal in the same change**, which is what
  `AppendOnlyRecordTests`' second arm exists to force — the newest review is the
  one likeliest to be registered by nobody, and an unsealed record is one
  nothing would notice being rewritten. Nothing in it has been acted on yet, and
  the index's status table says so.

- ✅ **Every run says whether it could have seen a browser take the foreground.** Every
  run now says whether it could have seen a browser take the foreground —
  because on this machine it could not. `JobLauncher` sets
  `STARTF_USESHOWWINDOW` with `SW_SHOWNOACTIVATE`, and that is measured; what
  was never checked is whether *this* machine can tell the difference.
  `SPI_GETFOREGROUNDLOCKTIMEOUT` reads **2,147,483,647 ms — about 24.8 days —**
  here, so Windows refuses a foreground change in the general case and both arms
  of a focus experiment answer *no steal*. The consequence runs the wrong way
  round the usual portability rule: **a change that reintroduced focus stealing
  would pass here and fail on a default install**, and the local answer is
  *clean* rather than *unknown*.

  So the coverage block gained a `foreground lock` row beside `first-run bytes`,
  carrying the value and one of four states — `CAN SEE` (the lock never
  applies), `IF IDLE` (it expires inside the budget an experiment here may
  take), `BLIND` (it outlasts that budget — this machine) and `UNREAD` (Windows
  refused the call). In the `BLIND` band it adds three lines saying the run
  **did not answer** the question, and naming the exception — a foreground
  window owned by an ancestor of the launching process — that makes a null trial
  read as a pass.

  **It reports and it never repairs.** Nothing calls
  `SPI_SETFOREGROUNDLOCKTIMEOUT`: the timeout is a machine-wide user preference,
  and writing to it would edit the developer's desktop and make every
  `[MACHINE]` figure already recorded incomparable with the next one. Nothing
  starts a browser or touches the foreground either. **The band edge derives
  from `TestDefaults.BrowserHang`** rather than being written at the comparison,
  because *can this machine discriminate?* is exactly *can the lock expire
  inside the time an experiment here may take?*. It is a row and not a
  `SuiteCapability` for the reason `first-run bytes` is: every capability names
  a command that produces it, and the only thing that would turn this one green
  is a setting the suite may not change — so a capability would make every
  release from this machine unreachable with no permitted remedy.
  `ForegroundLockTests` holds the bands, the boundary and both directions of the
  warning; `SuiteCoverageTests` holds that the row reaches the block. The hazard
  row stays **open**: the machine is exactly as blind as it was, and what
  changed is that a green run no longer reads as an assurance it cannot give.

- 📝 **The server `instructions` now say what a full-page screenshot costs, before
  a model reaches for one.** One
  sentence: *"'fullPage: true' costs the per-image token maximum on any page
  worth using it on: it leaves at full document height and is downscaled to that
  ceiling."* The arithmetic behind it, measured 2026-08-20: a viewport shot at
  the 1920x1080 default arrives as **2,691 visual tokens**; the same page with
  `fullPage: true` over a 3,637 px document leaves as 1920x3637, which is
  `⌈1920/28⌉ × ⌈3637/28⌉ =` **8,970 patches**, and the API downscales that to
  its per-image ceiling of **4,784**. Break-even is a document about 1,960 px
  tall, so *every* full-page shot of a page long enough to want one costs the
  maximum — and returns less detail than the viewport shot it replaced, because
  BrowserAI diverges before upstream's `scaleImageToFitMessage` and appends what
  is on disk.

  **It is in the `instructions` and not on `browser_take_screenshot`**, which is
  the instinctive place to put it and the wrong one: every upstream description
  passes through this proxy byte for byte, and the append path that would have
  made this possible was deleted on 2026-08-18.
  `ModelSurfaceTests.TheFullPageScreenshotCostIsInTheInstructionsAndNotOnTheToolsDescription`
  holds both halves, and was watched red on each of them separately — once with
  the sentence removed, once with it appended to the tool. **The string now
  stands at 2,028 characters of the 2,048 the client silently truncates at**
  (2,038 bytes, which is not the figure the client counts), leaving **20** —
  measured off the published binary's own `initialize` response, *previously
  1,876 of 2,048*.

- ✅ **The dated records are append-only, and a test says so.** Released
  `CHANGELOG.md` sections and every body under `docs/reviews/` are sealed by
  `AppendOnlyRecordTests` — prefix, character count and SHA-256 — so an addendum
  may be appended and a body may not be rewritten or truncated. **The failure it
  exists for had already happened**: the `lock.json` → `browserai.json` rename
  swept the whole tree, reached both, and produced a 2026-08-18 review claiming
  a filename that did not exist for another two days. Nothing failed; a human
  reading the diff caught it, and the rule was prose in
  `docs/reviews/README.md`. It was planted by re-applying that exact sweep and
  watched red on four records at once, `CHANGELOG.md#0.1.0` among them.

  **It is deliberately narrow.** `docs/reviews/README.md` is *not* sealed — it
  carries the status table, which is meant to move as findings are acted on —
  and the changelog's `[Unreleased]` section is not sealed either, because it is
  not a record of anything until a release stamps it. A typo fix in a review
  stays legitimate: it means changing the seal in the same commit, which is a
  line in the diff rather than a silent rewrite. [Release checklist item
  10](RELEASING.md#10-the-changelogs-unreleased-section-is-not-empty) now
  registers the section a release stamps.

- ✨ **Six per-run arguments, and four opinions that stopped being arguments.** `viewport`,
  `locale`, `timezone`, `ignoreHTTPSErrors` and `captureNetwork` join `headed`,
  `tracing` and `debug` on both `browserai_init` and `browserai_resume`; every
  one of them is regenerated at each child launch and written to nothing, so a
  session created headless at one viewport is resumed headed at another without
  being destroyed first.

  **`viewport` defaults to 1920×1080, and the number that decided it is the
  token cost of a screenshot.** Measured end to end through BrowserAI: 1920×1080
  arrives as **2,691 visual tokens**, 1280×720 as 1,196, and 2560×1440 as
  **4,784 — exactly the per-image cap, with zero headroom**. What is set is what
  the model receives: BrowserAI's image handling diverges before upstream's
  `scaleImageToFitMessage`, so nothing downscales it on the way back. A value
  that does not parse or is out of bounds is **refused rather than rounded**,
  because a size a caller did not choose is one every later screenshot is
  silently taken at.

  **`locale` and `timezone` are read from the host machine** rather than
  hard-coded. Upstream leaves them unset, which gives the browser's own `en-US`
  whatever the machine is — so a site that localises by `Accept-Language` shows
  an agent something a person at the same desk would never see. Windows's own
  time-zone identifier is converted to IANA, which is what Playwright accepts;
  where that conversion is unavailable the key is **omitted rather than
  guessed**, because a Windows identifier fails the launch rather than
  degrading.

  **`captureNetwork` writes an HTTP Archive, and sets `serviceWorkers: "block"`
  with it.** The block is not optional: a request served out of a worker's cache
  never reaches the network layer the archive is written from, so without it the
  capture is **silently incomplete** — in the direction that matters, because a
  worker serves the repeat requests. The description carries three things a
  caller has to know before turning it on: it **changes what the site does**, it
  takes effect at the **next browser launch** and is never retroactive, and the
  file is a **plaintext credential dump**. **Each launch gets its own
  timestamped filename** under `output\network\`: `recordHar` truncates whatever
  path it is given at every context creation, and the config is regenerated per
  launch, so the overwrite-on-resume is avoidable rather than documentable. The
  answer `browserai_init` returns names the file and says both things.

  ⚠️ **`permissions: ["clipboard-read"]` is hard-coded, and for Chromium only.**
  `clipboard-write` is already granted without asking, so naming it would be an
  opinion with no effect. **Firefox does not know the permission at all**: a
  context created with it fails at `initializeServer` with `Unknown permission:
  clipboard-read` and the browser exits, so writing it for both families makes
  every Firefox session **unusable** rather than degraded. Measured 2026-08-20
  against the provisioned `firefox-1539` — by writing it for both families and
  watching a real front-door navigation go red — [recorded in
  kb](kb/playwright/configuration.md#silent-config-failures) with a
  re-verification row. It is family-scoped exactly as `channel` is.

  **`codegen: "none"` and `snapshot.boxes: true` are hard-coded with no
  argument.** `codegen` strips a `### Ran Playwright code` block from every
  response, for a feature this product does not have and no reader exists for.
  `snapshot.boxes` costs nothing until something reads the snapshot — a response
  carries a link rather than the text — and every session is granted the
  `vision` capability, whose six `browser_mouse_*_xy` tools take viewport
  coordinates a snapshot without boxes gives a model no way to compute.

- ✨ **`browserai_catch_up`, the seventh authored tool.** It answers *what were
  we doing here, and what is here now* for one session, from **two sources that
  routinely disagree** — which is the whole point rather than a caveat. The
  **log** says what BrowserAI *did*: every browser call and every purpose
  change, in order, with what the caller said each was for. The **directory**
  says what is *true now*: age, when it was last touched, total size, and a
  breakdown by artifact kind.

  **The disagreement that matters is credentials.** Cookies arrive from
  *navigation* rather than from tools, so a session whose log shows no
  `browser_cookie_*` call at all can hold a live signed-in profile — a log-only
  answer would say *"no credential tools were used"* about exactly that
  directory. It reports the profile's cookie store when there is one, and names
  any **HTTP Archive** it finds: a HAR records every request and response
  including headers, so every bearer token and session cookie that crossed the
  wire is in it in clear text.

  **Its description says when to call it**: on arriving at a session someone
  else was driving, and before destroying one — because the size and the
  breakdown are the only things that say what is about to be deleted.

  ⚠️ **It is the one session-scoped tool with no `why`**, and that is deliberate
  twice over: a tool whose whole purpose is to tell you what happened must not
  itself become the most recent thing that happened, and writing an entry would
  mean replacing `browserai.json` — which a session another live BrowserAI is
  driving refuses through its own `FileShare.Read`, and that is precisely the
  case it exists for. It is **read-only and takes no lock it can be refused
  by**, asserted byte-for-byte against a record a live session is holding.

  ⚠️ ***Corrected 2026-08-24 (previously "writing an entry would mean taking the
  per-directory gate — which a session another live BrowserAI is driving would
  refuse … It is **read-only and takes no lock at all**").*** Two wrong claims
  in one paragraph, both corrected elsewhere in the same commit and missed here:
  `LockScopes.PerDirectoryGate` does not refuse a second writer, it **waits**
  120 seconds for one — what refuses one is the holder's share mode on
  `browserai.json`; and since 2026-08-24 `catch_up` does take a lock, briefly —
  `SessionManager.InUse` reaches `SessionLock.ProbeLivenessUnderTheGate`, which
  holds that gate at a **zero** timeout for one open and close, so it can be
  undetermined but never queued.

- ✨ **One time-ordered log, inside `browserai.json`.** `browserai_init`'s
  `purpose`, every purpose change on `browserai_resume`, every explicit
  `browserai_set_purpose` and every browser call the session forwarded are
  entries in the **same ordered list** — so a reader sees *the human changed the
  purpose here* sitting between the calls it explains, rather than two streams
  nobody merges. `browserai.json` moved to **schema 4**; there is no converter
  and the recovery is the one it has always carried.

  **It is inside the record rather than in a sibling append-only file, and that
  was the maintainer's decision over a recommendation of the sibling.** The
  argument that decided it is one the recommendation did not weigh: a session
  directory is moved and copied by people, `browserai.json` is already the thing
  that makes a copy self-describing, and **one file cannot be half-copied**. The
  cost is a whole-record durable write — `WriteThrough`, flush, atomic rename,
  re-open — on **every forwarded browser call**, where an append to a sibling
  would have been `O(entry)`. [QUESTIONS.md
  §14](QUESTIONS.md#14-the-one-time-ordered-log-lives-inside-browseraijson--decided-by-the-maintainer-over-my-recommendation)
  records both sides and what reversing it would cost.

  ⚠️ **A call whose entry cannot be written is refused, and never reaches the
  browser** — `SessionErrors.SessionLogCouldNotBeWritten`, the catalogue's 27th
  row. The value of one log is that reading it back tells you what the session
  did; a gap nobody is told about is worse than a refusal somebody can act on.
  **A call BrowserAI refuses leaves no entry**, which is the same rule from the
  other side: this records what the session *did*, and the refusals are in
  `browserai.log` beside it.

  **What is stored for an argument, since nothing instructed it.** Every
  argument **name**, always — a reader must see that a password field was filled
  even when the value is not there. Then: `value` and `text` are **never**
  stored, at any length, recorded as `<withheld, N characters>` — they are the
  two scalar parameters upstream uses for something a person typed or a server
  set, on `browser_cookie_set`, `browser_localstorage_set`,
  `browser_sessionstorage_set` and `browser_type`, and **the list is asserted
  against the golden snapshot** so an upstream rename is a red build; an object
  or an array becomes a **shape**, `<object, N keys>` / `<array, N items>`,
  which is what `browser_fill_form`'s `fields` and `browser_route`'s `headers`
  become; and everything else is stored verbatim to 200 characters and then
  **cut with a count**, which is what turns a `browser_evaluate` body from a
  transcript into a summary. ⚠️ **It is not a redaction boundary**: the log sits
  beside the profile whose cookie database holds the same credentials. What it
  buys is that a password is not written into the one file a model is invited to
  read back.

  **The log is capped at 250 entries and trimmed out of the middle**, keeping
  entry zero — `browserai_init`, the only statement of why the directory exists
  — exactly as the statement lists keep their first statement. A record at the
  cap says so, and every answer that reads one says entries *may* have been
  elided.

  **`browserai_list`'s "last used" now means what it says.** It is read from the
  log's newest entry when there is one: a session driven for an hour without its
  purpose or its holder changing appended nothing to any statement list, so the
  figure used to be *when the session was opened*. `created` is deliberately
  unchanged — it is the first statement of every field, and the trim never
  removes one.

- ✨ **A required `why` on every call that names a session.** Every upstream
  browser tool, plus `browserai_resume`, `browserai_destroy` and
  `browserai_set_purpose`. It rides the same path `session` does — the injection
  mutates the `JsonNode` the child sent rather than rebuilding it, so unknown
  members survive — and it is stripped from a clone of the request before the
  call is forwarded, because the child has never heard of it. **The golden
  snapshot is unaffected**: it records what upstream offers, captured from the
  child before the rewrite.

  **Not on `browserai_list` or `browserai_reinstall_browser`**, which are
  directory- and machine-scoped: there is no session record to write into. **Not
  on `browserai_init`**, which asks for a `purpose` instead — two mandatory
  free-text fields on one call gets one thoughtful answer and one restatement.

  **The description does the steering, and parameter descriptions are
  uncapped**, so the long text is there rather than in the tool description that
  is capped at 2,048 characters. It asks for **why rather than what**, because
  the tool name already says what: *"checking whether the login survived the
  redirect"* beats *"clicking the submit button"*. A call that omits it is
  refused **before anything is forwarded**, and the refusal —
  `SessionErrors.WhyMissing`, the catalogue's 26th row — says what to write
  rather than only that something is missing, because a model told *"'why' is
  required"* retries with a restatement that satisfies the schema and records
  nothing.

  **Where it goes for now:** the session's own `browserai.log`, written before
  the call is forwarded so that a call which never returns still left a record
  of what it was for. A closed session has no log of its own open, so
  `browserai_set_purpose` against one writes to the machine-wide process log
  with the directory named.

- ✨ **Ten tools, reachable for the first time in this product's history or its
  predecessor's.** Upstream's
  `network` capability — `browser_route`, `browser_route_list`,
  `browser_unroute`, `browser_network_state_set` — its `pdf` capability
  (`browser_pdf_save`) and its `testing` capability (`browser_generate_locator`,
  `browser_verify_element_visible`, `browser_verify_text_visible`,
  `browser_verify_list_visible`, `browser_verify_value`) had **never been named
  in a generated config at all**, so those ten tools did not exist in any
  session's child and a caller naming one was told by upstream that it did not
  know the tool. The advertised surface is now **68 of the 69** a fully-capable
  child exposes; it was 58 of 59.

  **Recorded as a deliberate grant rather than left as a consequence.** They
  arrived because session modes were deleted, and a side effect nothing names
  reads as an accident at the next review — so the list is
  `SessionToolSurface.NewlyGrantedTools`, asserted by name in process by
  `ModelSurfaceTests` and off the wire by `VerticalSliceTests`, against a
  hand-written expectation that fails whichever side moves. The decision went
  against a recommendation to weigh the three new capabilities separately;
  [QUESTIONS.md
  §13](QUESTIONS.md#13-the-ten-newly-granted-tools--decided-by-the-maintainer-over-my-recommendation)
  records what that recommendation was and why it was wrong about the framing.

  ⚠️ **`browser_run_code_unsafe` is not among them and never was.** It is
  `core`, so it has been reachable in every session this product has ever opened
  — `headless` included — and it reaches the cookie jar.

- 📝 **A warning in the server `instructions` that response mocking can make a page
  lie to a human.** `browser_route`
  installs a rule and the browser then renders the mock as if it came from the
  server: the address bar keeps the real origin, and nothing on screen says a
  rule is in force, so a person watching a headed window is looking at something
  the agent made up. **It is in BrowserAI's own string rather than appended to
  the tool's description**, because every upstream description passes through
  byte for byte and the one rewrite path that could have done it was deleted on
  2026-08-18. The space it occupies is the space the three mode lines used to.

- ✨ **`browserai_list` now says whether each session it reports is being driven,
  three-valued.** It
  carried mode, browser, purpose, dates and size and performed no liveness check
  at all, so a caller could not tell an abandoned session from one another agent
  was inside — which is the distinction that matters most in the turn before
  `browserai_destroy`. Each entry now carries an `in use:` line: **YES** when
  this BrowserAI is driving it or when something holds its `lock.json`, **no**
  when nothing held it at the instant of the look, and **UNKNOWN**, with the
  reason, when the question could not be settled.

  **Through the pre-gate probe, `SessionLock.ProbeLiveness`, and never the
  process-liveness check.** It needs no process handle, so a token that may not
  open the peer cannot defeat it, and it asks about the resource rather than
  about a pid Windows may have recycled. Cost is **one
  `CreateFile`/`CloseHandle` per entry** — measured at 0.035 ms free and 0.049
  ms held, against 0.6–2.3 ms for the `SizeOnDisk` walk the same loop already
  performs per entry
  ([kb](kb/windows/detection.md#the-pre-gate-probe-as-a-liveness-report--measured-2026-08-20))
  — and the loop is over the session *index*, which the `directory` argument
  filters rather than walks, so a drive-root listing adds one open per known
  session and never one per file on the volume.

  ⚠️ **The holder is deliberately not named, and that is the trap this was built
  around.** A sharing violation says the file is held and never by whom; the
  record inside can describe a previous holder. Printing *"held by PID n"* would
  publish, on every listing, the wrong *sentence* the ownership work recorded on
  2026-08-19.
  `SessionListTests.ListSaysWhichSessionsAreInUseAndNeverPrintsCouldNotTellAsFree`
  asserts that absence as well as the four positive answers, each with its own
  control.

- ✨ **Liveness is three-valued: `Alone`, `NotAlone(n)` or `Undetermined(why)`.** `LiveInstances`
  answered `false` for *not alone* and for *could not tell* at once — a failed
  join, an expired gate, an unreadable marker. That was written for the updater,
  where both mean *do not apply* and the safe direction is the same one; for
  anything that repairs rather than refrains it is the unsafe direction, because
  the refusal is permanent and has nothing to act on. `Census` now returns the
  reason — a path, a mutex name, an exception's own message — so a refusal built
  on one can be diagnosed.

  **The updater did not move, and that is a guarantee rather than an
  intention.** `UpdateService`'s call site is untouched, `AmIAlone` is one
  expression over `Census` with exactly one `true` arm, and two tests hold it:
  one over all three states directly, one through `UpdateService` itself
  requiring an undetermined census to stage and apply nothing, exactly as a
  not-alone census does.

- 🔒 **A reinstall takes the machine's browsers root for the whole call.** A
  reinstall now takes the machine's browsers root for the whole call, and
  `browserai_init` and `browserai_resume` are refused while it holds it.
  `browserai_reinstall_browser` already refused while sessions were open, and
  two agents on one machine could still race past it: the reinstall establishes
  that nothing is running out of the tree, and the other one's `init` launches a
  browser into that tree while the recursive delete is part way through it. The
  census was *right* when it was asked, which is why nothing caught this.

  **The claim is a lock file at `<browsers root>\reinstall.lock`, held open
  `FileShare.Read` for the whole operation.** A file rather than a named mutex
  because the claim spans a 203.8 MB download inside an `async` method and a
  named mutex is owned by the thread that waited on it; **and not a named
  semaphore**, which does span threads, because a semaphore's count is not
  restored when its holder dies — one crashed reinstall would then refuse every
  `init` on the machine until a reboot. Windows closes a file handle however the
  process ends, which is the crash recovery this needs and cannot write itself.
  Held-ness is a sharing violation and never the file's existence, exactly as
  `lock.json` works.

  **It is mutual against itself** — a second reinstall is refused for the same
  reason a session is, which the maintainer asked for in the same breath:
  *"Including any reinstall sessions."* **And it does not drain.** A reinstall
  that takes the claim and then finds sessions open releases it and refuses
  immediately, naming how many are running; it never holds the machine waiting
  for a browser a human may never close.

  **The lock order is fixed and has no cycle.** The claim is outermost and the
  per-family provisioning mutexes are taken under it, never the other way round;
  `init` and `resume` only *probe* it and never acquire it, so they can never
  take it from a racing reinstall; and every acquisition on both sides is
  non-blocking, so even an inverted order would produce a refusal rather than a
  hang.

- 🔒 **The family reinstall's session gate is now unconditional.** It used to be
  asked only *inside* `if (running.Count is not 0)`, so a session that was open
  with its browser not currently launched let the delete through — and the
  browser it is about to launch lands in a tree being removed. The maintainer's
  decision, verbatim: *"No reinstall if there is any session running system
  wide."* The family filter survives: only a session of the family being
  reinstalled can hold an executable out of that family's tree, so a live
  Chromium session still does not block a Firefox reinstall. `shared` counts
  every family, as it already did.

- 📝 **`--storage-state` together with `--user-data-dir` is a silent no-op.** `--storage-state`
  together with `--user-data-dir` is a silent no-op — exit 0, empty stderr, no
  state applied. `storageState` is the **only** one of
  `BrowserNewContextParams`' 32 keys absent from
  `BrowserTypeLaunchPersistentContextParams`' 49; `tObject` iterates the
  declared schema and drops undeclared keys without error; and
  `createPersistentBrowser` spreads `...contextOptions` straight through, so it
  looks accepted at every visible layer. BrowserAI is on the persistent side by
  construction and writes no `storageState`; the entry is for the next reader
  who reaches for it to seed a signed-in session.

- 📝 **`--caps` accepts any word at all.** `--caps bogus` exits 0 with no
  diagnostic, because the option is parsed by `commaSeparatedList` where
  `--codegen`, `--console-level` and `--image-responses` all use `enumParser`.
  It is also why `--caps storage` works although the help documents only
  `vision`, `pdf` and `devtools`.

- 📝 **Two browsers on one profile directory: Chromium refuses and Firefox hangs.** Two
  browsers on one profile directory: Chromium refuses in 5,036 ms naming the
  cause, Firefox hangs for 180,402 ms with an error that never mentions the
  profile. Upstream's `isProfileLocked` probes `<userDataDir>\lockfile` —
  Chromium's name — while Firefox uses `parent.lock`, so the guard never fires
  and Firefox's own lock blocks the juggler handshake until the launch timeout.
  The 5,036 ms is `isProfileLocked5Times`' own five one-second retries
  succeeding. **BrowserAI's own path is covered** by `ChildLaunch.Create`'s
  `parent.lock` preflight, which runs before the config is written and before
  anything spawns; upstream's bug is recorded rather than worked around.

- 📝 **The user agent is settable from the config on both families.** The user
  agent is settable from the config on both families and `navigator.webdriver`
  is not, on Firefox. Research for a decision the maintainer has not taken:
  `browser.contextOptions.userAgent` turns Chromium's `HeadlessChrome/152.0.0.0`
  into `Chrome/152.0.0.0` and replaces Firefox's UA outright, with no Playwright
  driving and no init script. Firefox's `navigator.webdriver` stays `true` under
  `dom.webdriver.enabled: false`, while `general.useragent.override` through the
  same `firefoxUserPrefs` object *does* take effect — which is the control that
  makes the negative mean something. **Nothing was implemented.**

- 📝 **Firefox is owed Chromium's rename measurement, and now has it.** Firefox
  is owed Chromium's rename measurement, and now has it — plus the two shared
  trees and the browsers root, which nobody had asked about at all. The
  browser-tree rename refusal behind `browserai_reinstall_browser` was measured
  against **Chromium only**, and the entry said so. Measured 2026-08-19 the same
  way: a live headless **Firefox 153.0** (playwright firefox v1539) refuses
  `Directory.Move` of `firefox` with a sharing violation and of `firefox-1539`
  with `ERROR_ACCESS_DENIED`, and both succeed the second the browser is gone —
  **identical to Chromium, error for error and in the same order**, over two
  runs. **So there is no product finding and nothing changed**: the refusal was
  already family-agnostic, and it now rests on a measurement of both families
  rather than a generalisation from one. The Chromium arm was re-run first and
  reproduced exactly, so the record is reproducible from what was written rather
  than only from the day it was taken.

  **What was new is at the edges.** `ffmpeg-1011` and `winldd-1007` rename
  **freely** while a browser of either family is live — neither is running,
  since `ffmpeg-win64.exe` exists only during a recording — while the **browsers
  root itself** is refused under both families, with the same error as the
  revision directory. That is *further up* than the 2026-08-18
  running-executable measurement predicts, where a running image's grandparent
  moved without complaint, so whatever a browser holds is not just the directory
  its image sits in. Recorded rather than acted on, and explicitly not a licence
  to narrow `shared`'s wider refusal. **And Playwright's Firefox brings no
  Remote Agent up** — `--remote-debugging-port` never answers, because
  Playwright drives it over the juggler pipe — so the Firefox arm proves
  liveness by content-process tree where Chromium's uses DevTools, which is a
  weaker signal and is written down as one
  ([kb](kb/windows/processes.md#the-same-measurement-for-firefox-and-for-what-both-families-share--2026-08-19),
  re-verification row 103).

- 📝 **The CsWin32 metadata licence terms are in the repository, quoted verbatim.** The
  CsWin32 metadata licence terms are in the repository, quoted verbatim, and the
  question is now a legal one rather than a research task. The
  [`TODO.md`](TODO.md) item had stood on *"whether those terms create a notices
  obligation for shipped generated code is not assessed and must not be asserted
  either way"* — with nobody having read the terms. They are now in
  [QUESTIONS.md
  §12](QUESTIONS.md#12-the-cswin32-metadata-licence--moot-2026-08-20-and-the-entry-stays):
  the four packages and their resolved versions, how each declares its licence,
  the operative clauses quoted with URLs and the date fetched, what the text
  does and does not say about generated code, and five ordered questions for a
  lawyer. **The finding that was not expected:** the two metadata packages ship
  the same **byte-identical** Windows 10 SDK EULA
  (`EULAID:WIN10SDK.RTM.AUG_2018_en-US`, SHA-256 `0e97876e…`), while
  `win32metadata`'s own `README.md` says `Windows.Win32.winmd` — the only file
  the generator reads — is **MIT**. Two current Microsoft statements about one
  file, and they disagree. **No conclusion is drawn and none may be**: the
  exposure is still zero because CsWin32 is test-only at `PrivateAssets="all"`
  and nothing it emits ships.

- ✨ **`browserai_reinstall_browser` gained a third value, `shared`.** `browserai_reinstall_browser`
  gained a third value, `shared`, and it is the only route to repairing
  `ffmpeg`. `ffmpeg` and `winldd` are downloaded into the browsers root by
  **both** families, each carries its own `INSTALLATION_COMPLETE`, and a family
  reinstall deletes only that family's revision directory — so a corrupted
  `ffmpeg`, which the `video` artifact type needs, was **permanent through the
  product's own surface**. `shared` deletes both trees and runs one
  `install-browser ffmpeg`, measured 2026-08-19 to rebuild whichever of the two
  is missing because each carries its own marker
  ([kb](kb/playwright/provisioning-and-timings.md#one-install-browser-ffmpeg-rebuilds-both-shared-components--2026-08-19),
  re-verification row 100). The completeness check stays **per component**: a
  run that exits 0 having left one unmarked is reported here rather than met
  later as `spawn EFTYPE`.

  **Its refusal is deliberately stricter than a family's, and that is the
  decision.** A family reinstall is gated on a process running out of that tree,
  and for a family that is the same question as *a session is driving this
  browser* — `chrome.exe` lives for the session's life and holds its own image
  open. For the shared components the two come apart: `ffmpeg-win64.exe` exists
  only while a recording runs, so a process-only gate answers *nothing is using
  it* on a machine full of live sessions and the tree is then deleted under the
  next one to record. So `shared` refuses while **any** session is open, of
  **either** family, and still reports a process out of either tree that no
  session accounts for. A refusal saying *close your sessions* is recoverable in
  a turn; a shared tree corrupted by the operation that exists to repair it is
  not.

  **`shared` is not a browser and `browserai_init` still refuses it** — the two
  accepted sets are now different lists, and
  `FirefoxSessionTests.TheAdvertisedSurfaceOffersBothFamiliesAndMakesReinstallNameOne`
  asserts they differ, because reading one list for both is what would have
  bound a session for life to a codec. **Explicitly rejected:** having a family
  reinstall also verify and repair the shared components — that re-introduces a
  repair tool which can break something working, which is why the argument is
  required at all. Still no force flag.

- ✨ **Firefox is a browser you can ask for.** `browserai_init` accepts `browser:
  "firefox"` beside `chromium`, which stays the default. Everything below the
  front door was already family-parameterised — provisioning, the config
  generator, the `parent.lock` preflight, Restart Manager attribution, the
  restart-registration preference and the stray sweep all read the family from
  the session's own `lock.json` — so what changed is the door, and what was owed
  before it could open was a **measurement** and a **decision**.

  **The measurement: Firefox is 127.2 MB down and 340.15 MiB on disk.**
  127,247,129 B from the CDN's own `content-length` (`firefox-win64.zip`
  125,706,704 + `ffmpeg-win64.zip` 1,411,741 + `winldd-win64.zip` 128,684) and
  356,674,059 B across 71 files, from two clean provisioning runs into an empty
  root that were byte-identical, in 7.30 s and 6.60 s
  ([kb](kb/playwright/provisioning-and-timings.md#firefox-measured-the-same-way--2026-08-19)).
  The refusal a caller reads while a browser is still downloading quotes this
  figure; before today it would have quoted Chromium's 203.8 MB for a Firefox
  install, which is a measured-looking number measured of something else.

  ⚠️ **The Firefox pair that was already recorded was not comparable to
  Chromium's and has been replaced** — *previously "125,706,704 B down and
  352,898,062 B — 336.55 MiB — on disk"*. Those were the Firefox archive and the
  Firefox directory alone, where Chromium's counted a whole provisioning run;
  `install-browser firefox` fetches the same three archives. Re-measured, never
  adjusted.

  **The tool surface does not depend on the family.** Measured the same day
  against real children of the resolved payload: 42 tools at BrowserAI's base
  capability set and 59 with `storage`, identical names, identical order,
  byte-identical schemas under `chromium` and `firefox`
  ([kb](kb/playwright/tools-and-artifacts.md#does-the-surface-differ-by-browser-family--measured-2026-08-19)).
  It matters because BrowserAI builds its one static tool list from a single
  Chromium-configured surface child and the MCP spec forbids the set varying per
  connection — so every tool-surface number in this repository is now a measured
  claim about both families rather than an assumed one.

  `FirefoxSessionTests.AFirefoxSessionRunsFromInitThroughAnArtifactToDestroy`
  drives the front door end to end against a real Firefox — init, navigate,
  screenshot, destroy — and asserts by **full image path** that the browser that
  came up was Firefox, because every other assertion in it is satisfied just as
  happily by a Chromium.

- ✅ **A budget gate over every model-facing string, measured off the wire.** `ModelSurfaceTests.EveryModelFacingStringFitsTheClientsSilentTruncationBudget`
  drives the published NativeAOT binary over real JSON-RPC with a real
  `@playwright/mcp` child and measures three surfaces: the server
  `instructions`, every tool `description`, and **every parameter `description`
  inside every `inputSchema`** — the last of which was asserted by nothing, and
  is where BrowserAI's own injected `session` description lands on 59 upstream
  tools at once. Enumerated dynamically, so a tool added next year is covered;
  measured in characters *and* UTF-8 bytes; hard at 100% with no warning tier,
  because under the cut the text simply never reaches the model. Every length is
  printed sorted on a passing run, to `.work/description-budget.txt`. **The
  reading of the documentation's *"2KB each"* has since been measured** — see
  the entry below — so the gate now fails on characters rather than on whichever
  count is larger.

- ✅ **`RecordedCountTests`, which generalises the one place counting discipline was
  mechanised.** Every
  count a surviving document publishes about this repository is now checked
  against a live scan, through the *same* implementation that produces the
  figure: the hazard tally in `TODO.md` — **category by category, not just the
  total**, because a wrong total is only visible against the sum — the fragment
  count in `CLAUDE.md`, the tool-surface numbers in `DECISIONS.md`, and
  `kb/README.md`'s claim that no article carries `[STALE]`, with a positive
  control. Four counts it cannot mechanise are named in the class and say why.

- 🐛 **The per-directory session gate refused sessions that nothing was wrong with.** The
  per-directory session gate refused sessions that nothing was wrong with, and
  told the caller so in as many words. `LockScopes.PerDirectoryGate` was five
  seconds because the section it guards takes milliseconds — but every process
  naming one directory enters that gate *in turn* just to discover the file is
  held, so the wait is behind the whole queue. Measured on an **idle** machine:
  100 contenders, the charter's design point, put the slowest refusal at **3,349
  ms against a 5,000 ms timeout** — a margin of 1.49× — and 200 contenders
  produced **73 `Busy` refusals of 796**, every one at the timeout exactly.
  `Busy` withholds the holder's identity, which is the one thing the lock exists
  to report, and its message asserted *"something is wrong that waiting longer
  will not fix"* about a machine where waiting was the entire remedy. The gate
  was also **smaller than a wait taken inside it** — `RenameWindow.Budget`
  became 30 s on 2026-08-18 and is consumed under this mutex — so one entitled
  reader could starve every peer into a wrong answer. Now sixty seconds, with
  `SessionLockTests.TheGateOutlastsEveryWaitTakenInsideIt` failing the build if
  either number crosses the other, and re-measured at 200 contenders with zero
  `Busy`. `Updates.LiveInstances` shared the constant and is split out at its
  existing five seconds, because raising it would have put a sixty-second stall
  on the startup path.

- ✅ **The suite's torn-log check had never matched a log record.** `SaturationTests`'
  record-header expression required two spaces before `pid=`, but
  `FileLoggerProvider` pads level names to five characters and then writes two
  more — so `INFO` and `WARN` are followed by three, and only `TRACE`, `DEBUG`
  and `ERROR` ever matched. Against a real hundred-process run: 2,217 records,
  2,117 `INFO` and 100 `WARN`, **zero matches**. The check that proves a hundred
  concurrent appenders cannot corrupt one file was passing by seeing nothing,
  and the count beside it — meant to stop exactly that — counted every log file
  on the machine, so a developer's months of history satisfied it before the
  test began. On a fresh CI runner it read 6; with the log directory moved aside
  locally, 0. Fixed, scoped to the run's own pids, and the same run now reads
  2,217 headers from 100 distinct pids with none torn.

- ✅ **A test asserted a durable property on a channel that is not durable.** `ProtocolSplitTests`
  read the protocol-negotiation record from stderr, which `AddConsole` hands to
  a background processor thread, while `SliceRun` ends BrowserAI with
  `TerminateProcess` on purpose to prove containment — so the queue's tail went
  with it. Two CI runs lost different amounts, which is a queue and not a
  missing call. It now reads the process log, which `RollingFileWriter` writes
  unbuffered per record, scoped to that run's pid.

- ✅ **Five product source files were outside every scan built on the repository
  walk.** The
  prune list matched a directory *name* at any depth and case-insensitively, so
  `src\BrowserAI\Artifacts\` matched the root's `artifacts\` build output and
  was removed from the link check, the new fragment check and the SPDX house
  rule alike. No symptom: a prune reports nothing when it removes the wrong
  thing. Found because the fragment scan counted 552 where an independent count
  of the same corpus counted 554, and the walk now asserts it loses nothing the
  source enumeration finds.

- ✅ **A test asserted that the developer's screen was busy.** The message-window
  test proved `EnumWindows` had really enumerated by requiring more than fifty
  top-level windows — true of a desktop somebody is using and false on a CI
  agent, where a service window station holds a handful, so it would have gone
  red on a machine where nothing was wrong. The probe now publishes a second
  window that is top-level and never shown, and the assertion is disjointness by
  handle identity instead.

- 🐛 **A session another BrowserAI was writing to could not be opened.** Every
  `lock.json` is replaced by an atomic rename, and while that rename is in
  flight Windows refuses every other open of the file — with `ACCESS_DENIED`,
  not the sharing violation the code was watching for. So `browserai_init` or
  `browserai_resume` against a directory a second BrowserAI happened to be
  touching at that instant failed with an unhandled exception, and the session
  list reported a perfectly good session as unreadable. Both sides of the rename
  wait the window out now. It needed two processes and a coincidence measured in
  microseconds, which is why it took running the whole test suite at once to
  find it.

- 🐛 **A child that took longer than sixty seconds to start was reported as a
  protocol failure.** BrowserAI
  never set the MCP SDK's `InitializationTimeout`, so it inherited the default —
  sixty seconds, chosen by nobody here and documented nowhere — for a handshake
  whose far side is `node.exe` loading a bundled runtime. On a loaded machine
  that is reachable, and the failure it produced said `Initialization timed out`
  with no elapsed time, no child identity and none of the child's stderr in it.
  It is now an explicit ten-minute hang detector with the reasoning written on
  it: a child that has not spoken in ten minutes is not starting slowly, it is
  not starting.

- 🐛 **`browserai_reinstall_browser` could delete the browser tree and wait an hour.** `browserai_reinstall_browser`
  could delete the browser tree and then wait an hour with nothing installed.
  Provisioning that could not take the machine-wide mutex assumed another
  process was mid-download and watched for the marker it would write. The holder
  is not always downloading — it keeps the mutex through its revision prune,
  which walks every process on the machine — so a caller that had just deleted
  the tree waited out the full 60-minute deadline for a marker nobody was going
  to write. It now watches for the mutex as well, and installs when the holder
  lets go without having completed the tree; a genuine second downloader is
  still never started, because the marker is still checked first. Reachable
  without any unusual sequencing: `init` provisions on a background thread and
  returns immediately.

- ✅ **Both intermittent failures now name their own state.** The race probe
  reported an outcome and a message; it now reports the holder's pid *and*
  creation time, whether that holder was running, the gate's timeout beside the
  elapsed figure, anything `TryAcquire` threw, and `lock.json` as it stood at
  that instant — and every assertion in the race carries all sixteen contenders'
  reports rather than the one that tripped. `JobObjectScope.SaidBy` waited for
  nothing and so reported *"it wrote nothing to either stream"* for a drain the
  thread pool had not scheduled; it now drains to end-of-file and tells the two
  silences apart. The sweep's real-browser arm launches Chromium with
  `--enable-logging --log-file --v=1`, because on Windows a browser that will
  not start writes to a log file and not to stderr, and puts that log and the
  machine's process, handle and commit figures into the failure.

- ✅ **CI's four skipped tests are settled rather than rediscovered.** [`TESTING.md`](TESTING.md#continuous-integration)
  records why a green CI run reports skips, which four they are, that the
  no-skip rule is about `[Skip]` in the tree and is enforced by
  `HouseRuleTests`, and that zero skipped is a release requirement met by
  cutting from a machine with every capability — never by softening the gate to
  tidy a badge.

- ✅ **The build runs on a machine nobody owns.** There was no `.github/` at all,
  so every test and every release-phase check ran only when somebody remembered
  to run it locally — invisible, on a public repository, to anyone opening a
  pull request. `.github/workflows/build.yml` builds the payload, provisions
  both browsers, publishes the NativeAOT binary and runs the **whole** suite
  including `SaturationTests`, on every push and pull request. It states its own
  cost in its header: about 204 MB of first-run browser download per run,
  uncached on purpose, because a cache key would have to be guessed ahead of the
  resolve it depends on.

  ⚠️ **Reversed on 2026-08-20, before this section was ever released, and both
  entries are kept.** See *Removed* below. The link to the workflow file is gone
  from this entry because the file is; the entry stays because a reader of the
  release notes should see that CI existed and why, not only that it does not.

- ✅ **The `#anchor` half of every relative link is checked too.** The link test
  resolved the path and said, honestly, that it did not resolve the fragment. A
  documentation restructure then retitled four headings and moved 53 anchored
  links across 20 files, four of them under `src\`, and not one would have gone
  red. 554 fragments are now checked against GitHub's own heading-to-slug rule,
  which is itself asserted against worked examples rather than trusted.

- 📝 **`Interop\`, `Sessions\` and `Runtime\` have working instructions of their own.** `Interop\`,
  `Sessions\` and `Runtime\` have working instructions of their own, twelve
  lines each, carrying only what the mechanisms cannot say — and a second
  `PreToolUse` hook puts the two invariants no analyzer can fully catch in front
  of whoever edits the first two.

- ✨ **BrowserAI registers itself with your MCP client when it installs, and
  unregisters when it goes.** One
  registration at *user* scope — available in every repository, with no
  `.mcp.json`, no hook registration and no file added to any project. Installing
  is the whole of setup. It registers `current\BrowserAI.exe` directly, so an
  update replaces the binary without the registration ever needing to change;
  measured across an update and a rollback, the entry does not move.

- 📦 **A registration that could not happen says so, and never breaks the install.** No
  MCP client on the machine, a client that refuses, one that hangs, one that
  cannot be started — each leaves a line in the process log *and*
  `mcp-registration.json` beside the installed `current\` folder, naming the
  outcome and the one command to run by hand. Nothing about a failed
  registration is silent, and nothing about it can fail an installation.

- 📦 **Installing, updating, repairing and reinstalling all leave exactly one
  registration.** An
  install re-points a stale one; an update repairs a missing one and never
  overwrites arguments you added yourself.

- 📦 **The version comes from the git tag and is typed nowhere.** A build on
  `v0.1.0` is `0.1.0`; five commits later, with no new tag, it is
  `0.1.1-alpha.0.5`. Nothing to edit, forget, or get out of step with the tag,
  and an untagged build says so in its own version string — which is the whole
  of *never self-update from a build that is not a release*.

- 📦 **A build that cannot work out what version it is now fails.** A shallow
  clone or an unfetched tag makes the derivation fall back to `0.0.0`, and a
  binary that does not know what it is cannot be rolled back to or bisected
  against. The build refuses it and the message names the remedy, which is never
  guessable from the number itself.

- 📝 **This changelog.** This changelog, and `build/Get-ReleaseNotes.ps1`, which
  extracts the unreleased section, refuses to produce release notes when it is
  empty, and stamps it under the version being cut.

- ✨ **The running build's version is the first line of every process log.** The
  running build's version is the first line of every process log, so *"which
  version was running when this happened"* is answerable for past runs as well
  as the current one. The process log survives an update, so a machine that
  updated itself records both versions and the moment it changed. *Now
  demonstrated rather than asserted: after a real update and a real rollback,
  one log file carries `BrowserAI 0.9.0 started` and `BrowserAI 0.9.1 started`.*

- 📦 **Silent background self-update, per-user, with a rollback that works.** BrowserAI
  installs to `%LocalAppData%` with no elevation, checks its feed off the
  message loop so a tool call stays answerable while a package is in flight, and
  swaps itself between sessions — in normal use there is no *restart to apply*
  prompt at all. A BrowserAI-only release is a **97,216-byte** delta against a
  46.8 MiB full package.

- 📦 **An update is never applied while another BrowserAI is running.** Applying
  terminates every process under the install root, which at the concurrency this
  is designed for is every other agent's browser. The last instance to exit
  applies what the others staged.

- 📦 **Rollback is publishable as well as acceptable.** The client allows a
  version downgrade and the release script permits *monotonic **or** an explicit
  rollback republish* — both halves, because either alone is a rollback that can
  be accepted but never emitted, or emitted and never accepted.

- 📦 **`build/New-Release.ps1` builds a release end to end.** `build/New-Release.ps1`,
  which publishes, packs and refuses: on a `vpk` that does not match the
  Velopack library, on a version that is `0.0.0` or carries build metadata, on a
  non-monotonic release nobody stated, on anything in ILC's raw output, and on
  this build's own version string appearing in decorated form anywhere in the
  linked binary.

- 📦 **Velopack's MIT licence and a trademark disclaimer now ship inside the package.** Velopack's
  MIT licence and a trademark disclaimer now ship inside the package, in
  `THIRD-PARTY-NOTICES.txt` beside the binary. Both were absent from an
  otherwise releasable package: Velopack is compiled *into* `BrowserAI.exe`, so
  its licence never leaves the NuGet cache, and no upstream file carries a
  trademark disclaimer at all. The licence is copied from the commit the
  resolved package records as its source, never transcribed, and a Velopack bump
  is a red build until it has been re-fetched.

- 📦 **A release now records the resolved set beside its artifact.** A release
  now records the resolved set beside its artifact, emitted rather than
  assembled by hand: the three `packages.lock.json`, the payload's
  `package-lock.json`, `payload.json`, `browsers.json`, and a `manifest.json`
  stating the version, the tag, the package's SHA-256 and the resolved version
  read back out of each copy.

- 📦 **BrowserAI has an icon, and it is candidate 3 of the ten drawn on
  2026-09-15.** A globe with a reading eye, chosen by the maintainer on
  2026-09-16 (Q196): `browser_snapshot` and `browser_take_screenshot` are the two
  things this server does most, so an icon about perception is the honest
  emphasis. [`assets/BrowserAI.ico`](assets/BrowserAI.ico) replaces the candidate
  1 placeholder and is one file — both executables, the Setup stub, the
  Add/Remove entry and the Start Menu shortcut carry it, and nothing else
  changed. It is four entries: 16, 32 and 48 as 32-bit BGRA `BITMAPINFOHEADER`
  DIBs with an all-zero mask, and 256 as the PNG file verbatim, which is what
  keeps the file at 46,729 bytes instead of a third of a megabyte. **Each raster
  is rendered natively at its own size** by headless Chromium's vector
  rasteriser rather than downscaled from the 256. The master is persisted beside
  it as [`assets/icon.svg`](assets/icon.svg), with
  [`assets/icon-256.png`](assets/icon-256.png),
  [`assets/icon-128.png`](assets/icon-128.png) — which
  [`README.md`](README.md) now shows beside its title, the first image this
  project has ever published — and
  [`assets/social-preview.png`](assets/social-preview.png) at 1280×640 for the
  repository's **Social preview** setting, which has no API and reaches the world
  only when the maintainer drags the file into that field.
  [`assets/README.md`](assets/README.md) records what each file is, how it was
  made and what holds it. **Every one of them is original work**: SVG primitives
  drawn from scratch, no `<text>`, no `<image>`, no web font, no third-party
  mark and no glyph taken from a typeface — the social preview's lettering is
  rasterised system text and is the one asset whose look depends on the machine
  that rendered it.

- ✅ **Every asset reference in the prose resolves, and the exclusion hiding
  them is narrowed.**
  `DocumentationLinkTests.NotThisRepositorysKind` takes every asset extension out
  of the link scan, and it was justified by an assertion that the repository
  tracked no such file — which **had already gone quiet**: `assets\BrowserAI.ico`
  was committed on 2026-09-15 and `TheAssetExclusionHidesNothing` never saw it,
  because `assets` was not one of the directories that arm walked. The claim was
  true of everywhere it looked and false of the repository.
  `EveryAssetReferenceInTheProseResolvesToTheFileItNames` now resolves every
  image, every asset-kind link and every HTML `src` and `href` in every `.md`
  file — the HTML half is not decoration, because an image that has to sit
  **beside** a heading cannot be written in Markdown at all, so the one image
  this repository publishes is an `<img>` tag a Markdown-only scan would have
  missed entirely. `TheAssetExclusionHidesNothing` now asserts the property the
  exclusion actually needs: that every file of an excluded kind lives in
  `assets\`, where something resolves references to it. Planted red by pointing
  `README.md` at a file one character away from the one that is there, and again
  with a stray asset outside `assets\`.

- ✅ **The shipped icon's shape is a gate, and its planted red is a doctored
  file.**
  `ReleaseScriptTests.TheShippedIconIsTheOneTheMaintainerChose` reads the icon
  directory out of the bytes — four entries, the sizes in order, 32-bit in one
  plane, a 40-byte `BITMAPINFOHEADER` declaring the **doubled** height a mask
  entry declares, and a PNG-compressed 256 whose own `IHDR` agrees with the
  directory — and holds the three published rasters at the sizes their names
  claim. Not through `System.Drawing`, which answers *a 32×32 icon came back* for
  any file with one usable entry in it: a file missing the 256 looks perfect to a
  loader and blurred on a 4K display. **Candidate 1 had the identical directory
  shape**, so putting it back would not have moved one assertion — a check that
  cannot fail against the file it replaced is not evidence — so the controls take
  the real bytes and break one property each. The arm was also watched red
  against the real file with its entry count doctored from four to three.
  [`RELEASING.md`](RELEASING.md) item 7 now names candidate 3 and the two files
  that must agree, and says plainly that **nothing holds the drawing in the
  `.ico` to be the drawing in the SVG** — that is a render comparison on every
  build to answer a question a person answers by looking.

- ✅ **Nothing in a release body may carry a character a person would not type.**
  The maintainer's release directive, in his words: *"Ensure there is no trace
  of AI both in wording and character use."* He added it after reading the
  published `v1.0.0` body: *"the intro text of the release post is very much
  reading like AI."*
  `ChangelogTests.NothingThatReachesAReleaseBodyCarriesACharacterAPersonWouldNotType`
  is the **character half**, and [`RELEASING.md`](RELEASING.md) says plainly
  that the wording half needs a reader and always will. Eight code points are
  refused: the em dash `U+2014`, the en dash `U+2013`, the four curly quotes,
  the ellipsis `U+2026` and the non-breaking space `U+00A0`. It is a **deny
  list**, so the twelve palette icons and any other legitimate symbol are
  allowed without being listed in code, and a **backticked code span is exempt**
  because a span quotes something that exists rather than choosing a style. The
  scope is what a release body is actually made of and is stated rather than
  implied: every section preamble, every entry headline, the legend, and a body
  generated from the fixture, which is how the generator's own fixed text is
  covered. **An entry's detail is out of scope by construction** — since
  2026-09-16 the body carries headlines and a `read more` link, so the 674 em
  dashes in this file's details never reach a reader of the release page.
  Planted red against the `1.0.0` preamble, which carried three of them.

- 📝 **Setup asks before installing over an existing install, and says
  `Repair` on a re-ship.** Measured 2026-09-16
  against the published `v1.0.0` installer and filed in
  [`kb/packaging/velopack.md`](kb/packaging/velopack.md) with re-verification row
  130. The trigger is `!is_dir_empty(&root_path)` — **the directory, not the
  version** — so every existing user meets a `#32770` titled *"BrowserAI is
  already installed"* that waits indefinitely; only the affirmative button's
  label varies with the comparison (`Update` / `Downgrade` / **`Repair`** when
  equal), and taking it runs the full install over an emptied root. Two edges
  worth having written down: `--silent` skips the prompt entirely, which is why
  the suite's own installer arms never meet it, and **Cancel exits 0**, so
  nothing reading only an exit code can tell a cancelled install from a completed
  one.

- ✨ **Every file a tool result names is now named absolutely.** A screenshot,
  a PDF, a snapshot, a console log, a download, a saved response body and a
  trace all used to come back as `output\page-2026-09-17T14-18-13-427Z.png`, a
  path relative to the browser server's working directory. The reader of a tool
  result is a model rather than a process with a working directory, so that
  named nothing it could open. BrowserAI answered this with a note of its own
  until 2026-08-26, when artifact routing was deleted and every answer became
  the child's own bytes; from that day the relative pointers reached a caller
  with nothing beside them. This is that hole closed, and the fix is upstream's:
  the generated child config now writes `filePaths: "absolute"`, from PR
  microsoft/playwright#42673, merged 2026-09-16 and closing
  microsoft/playwright#42497, which is **this project's own request**, filed
  2026-08-27. Every pointer shape was measured before and after, and again end
  to end through the published server: the screenshot, PDF and storage-state
  links, the snapshot link, both console log pointers, the download line, the
  binary response body line, the network-requests link and the four trace links
  are all absolute, including the two the pull request's own text did not name.
  One shape was not driven and is recorded as owed rather than claimed: the
  paused-debugger location, which needs a paused session to provoke. The key is
  written explicitly rather than left to a default, because upstream's default
  is the opposite of what this product wants, and `PLAYWRIGHT_MCP_FILE_PATHS` is
  refused for every child so that an inherited variable cannot quietly put it
  back.

- ✨ **`browser_emulate_media` arrives and is allowed.** It emulates the CSS
  media features a page responds to on the page a session already owns:
  `prefers-color-scheme`, `prefers-reduced-motion`, `forced-colors`,
  `prefers-contrast`, and the print or screen media type. It is the only way to
  see a site's dark mode or its print stylesheet without starting a second
  session, it reaches nothing outside the page, and it returns immediately. The
  maintainer's verdict is `allow`, and the reasoning is in the tool's own row in
  `tool-verdicts.json`. BrowserAI's `tools/list` now carries 72 tools of the 74
  a fully capable child exposes, two fewer only because `browser_annotate` and
  `browser_webmcp_call` are still withheld for liveness.

- 📝 **The gate now says to publish the slice again after a `src/` change.**
  It always had to be done and the only place it was ever written down was the
  refusal it produces. A gate attempt on 2026-09-17 cost 34 reds reading *the
  published binary … is older than 7 source file(s), so this test would prove
  nothing about the code in the tree* — around thirty arms drive the published
  NativeAOT binary rather than the tree, and `PublishedSlice.EnsureFresh`
  refuses all of them together. The two-shell gate paragraph in `CLAUDE.md` and
  in `TESTING.md` now carries the step and the two commands that refusal names,
  `dotnet publish src/BrowserAI/BrowserAI.csproj -c Release -r win-x64
  --self-contained` and the same shape over `src/BrowserAI.App`. It also names
  the early signal, which is the run's own `publish freshness` row reading
  `STALE` with the newest input beside it, and says plainly that a release
  publish is not a substitute: `New-Release.ps1` stages into
  `artifacts\publish-<exe stem>` and never writes `src\<project>\bin\`.

- 📝 **The disk total after a first run is stamped stale rather than left reading as current.**
  `kb/playwright/provisioning-and-timings.md` published *disk after first run is
  130,434,952 + 451,389,780 = 581,824,732 B*, and both addends had moved under
  it. The chromium term is `chromium-1237`; the family is at 1245 and weighs
  454,699,952 B across 308 files, measured the same day on the reference
  machine. The `current\` term is the one-executable layout of 2026-08-17, and
  an install has held two binaries since 2026-09-15, so the figure names a
  directory that no longer exists in that shape. The sum is left exactly as it
  was measured and marked `[STALE]` with both reasons, which is what the marker
  is for — and it is the same defect the correction directly beneath it already
  records against the sentence this one replaced, a derived total carrying no
  date of its own.

### Changed

- 📦 **A pack built for the gate is not a release, so it is not given a release body.** `New-Release.ps1`
  is run twice for every release the suite is part of: once on the version being
  cut, and once on whatever MinVer derives from a commit past the tag —
  `1.0.1-alpha.0.19` today — to produce the pack the capability-gated arms
  install. **No pre-release version has a changelog section and none ever
  will**, so the body step is skipped for one, out loud, instead of refusing.
  Found by running the script rather than by reading it: the pack succeeded and
  the new step exited 1 naming a section nobody had written. The stale
  single-binary pack in `Releases/` was replaced in the same pass — it carried
  `BrowserAI.exe` at 19,221,504 bytes, which is the SERVER under the old name,
  and both real-installer arms were red against it — and the pack now holds
  `BrowserAI.exe` at 10,382,848 and `BrowserAI.Server.exe` at 19,181,568, which
  is the two-binary layout the installer arms assert.

- ✅ **An empty `[Unreleased]` is legal while a release is being cut, not only once it is tagged.** A
  release is not a commit — it is a stamp, then a gate, then a tag — and the
  exemption added earlier on 2026-09-15 keyed on the tag being **exactly at
  HEAD**, which is true only at the end of that. So
  `ChangelogTests.TheChangelogHasAnUnreleasedSectionWithEntriesInIt` was red
  through every step before it, which is exactly where a release stands while
  somebody is running the six-run gate. The second way in is the rule the first
  was standing in for: **nothing has landed that the changelog has not been
  written for**, read from git as `src/` and `tests/` against the last commit
  that touched `CHANGELOG.md`. A docs-only or release-machinery commit during a
  cut leaves it standing; the first product or test change after it takes it
  away and demands an entry again. Without git there is no exemption, which is
  the safe direction and the answer the arm gave before any of this.

- 📦 **The GitHub release body is generated from the section rather than cut out of it.** The
  published v1.0.0 body was the stamped section truncated at a heading boundary
  — **110,225 characters** ending mid-argument, opening with four warning icons,
  with a permalink line stuck on at the cut — because 236,567 characters do not
  fit in a field that holds 125,000.
  [`build/New-ReleaseNotes.ps1`](build/New-ReleaseNotes.ps1) reads the entry
  shape above and emits each headline as a line with its detail folded behind a
  `read more`, a footer carrying the palette legend read out of the changelog
  itself, and a link to the section at the tag whose anchor is computed by the
  **same slug rule** every relative link in this repository is checked with.
  [`build/New-Release.ps1`](build/New-Release.ps1) runs it last and writes the
  body beside the release manifest, so the body travels with the evidence
  instead of being produced by hand at publish time.

  ⚠️ **The size guard changes the document, and for 1.0.0 it is the branch
  taken**: folded is **280,063** characters, headlines alone is **19,340**, and
  the script says which shape it produced. The limit itself is a number this
  project carries and nobody here has established — GitHub's own documentation
  for *Create a release* states no maximum — which
  [kb](kb/toolchain.md#gh-for-a-release-body-the-size-limit-is-carried-rather-than-measured-and-the-rendering-is-checkable--2026-09-15)
  says in as many words, with a re-verification row naming the experiment that
  was deliberately not run. The rendering is checked against GitHub's own
  renderer before publishing — `gh api -X POST markdown -f mode=gfm` — which
  confirms the two properties the fold depends on: the `<details>` inside the
  `<li>`, the headline a `<strong>`.

  Planted red, each against its own mutation: with the fold removed the body arm
  failed on the whole document; with the guard removed the fallback arm read
  *"is FOLDED: 291 characters against a limit of 400"*; with the slug stripping
  underscores the anchor arm failed on the `one_off` heading **alone**, its
  other two arguments staying green.

- 📝 **Every entry in this changelog is an icon, a one-sentence headline and a fold.** **236
  entries re-shaped**, 1.0.0 and 0.1.0 alike, into `- <icon> **Headline.** <the
  whole of what happened>`. Nothing was rewritten: where a headline is new, the
  sentence it replaces is the first thing in the detail, word for word, with its
  bold markers dropped because the bold is the headline's now — checked
  mechanically over every entry rather than by reading. The groups are the
  Keep-a-Changelog set in its fixed order, **once each**: the 1.0.0 section
  carried `Changed` four times, `Added` three, `Fixed` three and `Removed`
  three, one run per batch that landed, so a reader looking for what was removed
  had to find three lists of it. Everything that had accumulated under
  `[Unreleased]` merges into those groups, because it all ships in the 1.0.0
  re-ship, and the section opens with a preamble written for somebody who has
  never seen the project.

  **The palette is a legend at the top and the icon is chosen for what the entry
  IS**, not for the group it sits under — twelve icons, approved as written.
  Five arms hold the shape, each with synthetic controls the tree can never
  produce: no icon, an icon outside the palette, a headline nobody made bold, a
  headline of two sentences, and one a single character over budget. **The
  100-character budget is chosen rather than measured and says so**: where
  GitHub wraps is a question about a browser's layout at a font size, and the
  markdown API returns HTML rather than a line box.

- 📦 **The production-feed check reads the body, because a 200 was measured
  carrying the wrong one.** For
  about two minutes after the 2026-09-15 release replaced its assets,
  `releases/latest/download/releases.win.json` answered **HTTP 200 with the
  previous manifest** — `Age: 2701`, while `gh api .../releases/latest` was
  correct throughout — so a post-publish check reading only the status code
  would have reported a feed serving a package nobody could download.
  `UpdateTests.TheProductionFeedUrlResolvesOverHttpAndReturnsAManifest` now
  requires the body to name the pack id this build installs under and a version
  no older than the first ever published under it. Its positive control is the
  body that really was served: the August manifest, pack id `BrowserAI` at
  1.0.0, recovered verbatim from the archived release evidence — it parses,
  carries `Assets` and meets the version floor, and fails on the id alone.
  [`RELEASING.md`](RELEASING.md#the-release-gate) now says the post-publish
  check is to poll the body until it names the version just published, and that
  no other post-publish check counts until it does.

- 📝 **`RELEASING.md`: moving a tag does not draft the release; deleting one does.** Corrected
  by addition after both halves were measured on 2026-09-15: a delete drafted
  the only published release and the public feed answered 404 for about **twenty
  minutes**; a `git tag -f` plus `git push --force origin refs/tags/…` left the
  release published throughout and the feed was down for **7.7 s**, which is
  GitHub re-resolving `releases/latest` rather than a drafting.

- 📝 **`TESTING.md` says to wait for `.work\test-scratch` to clear between two suite runs.** `TESTING.md`:
  between two suite runs, wait for `.work\test-scratch` to be released rather
  than for the first run to report. On the 2026-09-15 release gate, **137** rig
  directories were still handle-held after run 1 had printed its summary.
  Nothing enforces it — it is a property of two runs, and no test inside either
  can see the other.

- ✅ **A suite race the packed release let loose reddened arms that did not hold the variable.** A
  suite race the packed release let loose, and the arm holding the variable was
  not the arm that went red. `RealInstallerTests` opens an `EnvironmentScope`
  over `BROWSERAI_ROOT` and `CLAUDE_CONFIG_DIR` — both **process-wide**, because
  the readers are executables the suite composes no command line for — and
  carried `[NotInParallel]` on the MCP client's key. A key holds an arm apart
  from the arms carrying the **same key**; the readers of a process-wide
  variable are **every child every other arm starts**, which hold no key, open
  no scope and cannot be enumerated. On the 2026-09-15 release gate, run 1 of 6:
  `FileAccessRootTests` (×2) and `FirefoxSessionTests` launched browsers inside
  that window, their children read the override, saw an **empty** scratch data
  root, correctly reported first use of Chromium, began a **203.8 MB**
  provisioning download into the installer arm's own scratch root and refused
  the call. Three red arms, none of them the one that opened the scope, and
  **the release stopped at checklist item 8**. It had never fired before because
  the arm needs a pack in `Releases/` to run at all, and this was the second
  full suite in which it did. **The honest denominator is 1 red in 2 live runs;
  that is not a rate and is not offered as one.** The fix is the keyless
  `[NotInParallel]` — *runs beside nothing at all* — applied uniformly to
  **every arm in a file that constructs a scope**, which is `RealInstallerTests`
  and `RegistrationTests`, both on the class so a later arm inherits it rather
  than having to remember it.

- ✅ **The same race had a second face, it passed, and nothing would have said
  so.** While
  three foreign children wrote a browser into the installer arm's data root,
  that arm was asserting the root **byte-identical** — and passing, because it
  hashed only the four files it had planted. It now reads the whole tree after
  each install and after the uninstall, and names anything that is neither
  planted nor written by the install itself: `mcp-registration.json` at the root
  and the hook's own rolled `logs\browserai-{yyyyMMdd}-{nnn}.log`, matched on
  that real shape rather than on `browserai-*.log`, which would have exempted
  the arm's own planted marker and let any foreign log through. Planted red by
  dropping `browsers\chromium-1244\chrome.exe` into the root between the install
  and the assertion: *"(35 bytes) is in the data root and neither this arm nor
  the install put it there"*.

- ✅ **Both halves of the race fix were watched against a live reproduction of it.** Both
  halves of the fix were then watched against a live reproduction of the race,
  which is stronger evidence than either plant. One full suite run on 2026-09-15
  with the keyed attributes deliberately put back came home **5 red**: the three
  original arms
  (`FileAccessRootTests.AWriteOutsideTheSessionIsRefusedAndOneInsideItLands`,
  `.EveryPointerARealChildPublishesResolvesBecauseNothingMovesIt`,
  `FirefoxSessionTests.AFirefoxSessionRunsFromInitThroughAnArtifactToDestroy`)
  with the **identical** message, naming a 203.8 MB download into
  `real-install-data-1665c0ba…\browsers\chromium-1244`; the new scan, naming the
  keyed arm; and **the installer arm itself**, whose data-root assertion listed
  **sixteen** foreign files — `browsers\reinstall.lock`, three `index\` entries,
  three `instances\{pid}-{guid}\` directories with their `instance.live` and two
  Playwright configuration files each, and three `live\` markers. Under the old
  hash-what-was-planted check that same directory read *unchanged*. **The race
  is reproducible by putting the key back and is not reproducible on demand**:
  it needs one arm's few-second window to overlap another arm's browser launch,
  and it did not fire on the first of the two full runs that had the arm live.

- 💥 **The MCP server is `BrowserAI.Server.exe`, and older registrations name a file that is gone.** The
  MCP server is `BrowserAI.Server.exe`, and every registration written before
  this release names a file that is no longer there. The names swapped because
  Velopack derives the root stub, the Start Menu shortcut, the icon and all four
  hook invocations from `--mainExe` and from nothing else, so whichever binary a
  person launches has to be the one named there. **The update hook repairs its
  own stale entry**: an entry named `browserai` whose command is under our
  install root and names a file that is gone is re-pointed at
  `current\BrowserAI.Server.exe`; one that still resolves is left exactly as it
  is, arguments and all; and one whose command is **not** under our install root
  is another BrowserAI's — reported with its location and never adopted,
  overwritten or removed. Left alone, an entry naming `current\BrowserAI.exe`
  after an update is a file the client can still launch, and launching it shows
  a window.

- 🔧 **`RegistrationTarget` composes the server's path instead of copying its own.** `RegistrationTarget`
  composes the server's path instead of copying its own, and the guarantee that
  replaces the old one is two checks and a refusal. It used to register
  `Environment.ProcessPath` verbatim, so *"the registered path and the running
  binary cannot disagree: they are the same string"*. The hooks run in the
  configuration app now, so what a client is given is the app's directory plus
  the server's name — and a composed path is a guess until something checks it.
  The sibling **must exist**, and its PE optional header **must declare the
  console subsystem**; either failing is a refusal naming the file, in the
  process log and in `mcp-registration.json`. ⚠️ **A name check would not have
  done**: a file called `BrowserAI.Server.exe` that is really the configuration
  app passes every check an extension can make and fails at the worst possible
  moment — a client starts it expecting stdio, a window appears, and the client
  waits forever for a handshake a dialog will never send.

- 🔧 **The configuration app clears `VELOPACK_FIRSTRUN` and `VELOPACK_RESTART` from its environment.** The
  configuration app clears `VELOPACK_FIRSTRUN` and `VELOPACK_RESTART` from its
  own environment, and this is a defect the two-binary design creates. The
  installer starts the app with `VELOPACK_FIRSTRUN=true`; a child inherits its
  parent's environment block; so clicking *Register* would start `claude.exe`
  carrying it, and anything **that** process started — including
  `BrowserAI.Server.exe` — would carry it too. The server exits 0 on that
  variable deliberately, because a server the installer started has no client.
  It would then have exited 0 for a client that really was there, on first run,
  presenting as *failed to connect* with nothing in any log to say why. Cleared
  once after being read, rather than filtered at each launch site: there is more
  than one place this process starts something, and a filter that has to be
  remembered at each of them is the shape of the defect rather than its fix.

- 🔧 **The configuration app is a member of the live-instance census for as long as
  its window is open.** Velopack's
  `run_hook` ends in `force_stop_package`, which kills every process whose
  **image path** is under the install root — the name is never consulted — so an
  update applied by a *server* while the window is open would take the window
  with it, mid-click. The server's update lane defers while the census is
  non-empty and this marker is what makes it non-empty. The app's own apply is
  the opposite of the server's lane: `restart: true`, which is Velopack's
  pattern for a foreground application, and the button says in so many words
  that Claude Code sessions using BrowserAI lose the server until they are
  restarted.

- 📦 **`build/New-Release.ps1` publishes both projects into one pack directory.** `build/New-Release.ps1`
  publishes both projects into one pack directory, and HALT-A runs once per
  publish. The loop is the point: two binaries are linked into one release by
  two ILC passes, and a scan that read one of the two logs would ship a binary
  nobody had checked while reporting that ILC's output was clean. One log per
  binary, named for it; both `obj\Release` trees swept, or one `IlcCompile`
  stays skippable and leaves a log with nothing of ILC's in it; the
  decorated-version scan reads both binaries; and the pack refuses a directory
  holding one of the two.

- 📦 **`--shortcuts StartMenuRoot`, corrected from `None`.** The old reason —
  *"this is a background stdio server that a human never launches"* — was true
  of the only binary there was and is false of the one `--mainExe` now names.
  Without an entry the configuration app could be seen exactly once, on the
  install that started it. Never the default `Desktop,StartMenuRoot`: a desktop
  icon for something opened twice a year is clutter, and the test refuses
  `Desktop` so a dropped argument cannot restore it.

- 📦 **`assets/BrowserAI.ico` is wired into the pack and into both executables.** `assets/BrowserAI.ico`
  is wired into the pack and into both executables — the Setup stub, the
  Add/Remove entry, the Start Menu shortcut and Explorer. ⚠️ **It is a
  placeholder.** Ten candidates were drawn on 2026-09-15 and candidate 1 is in
  the tree so that the packaging is complete and exercised; the chosen one
  replaces **that one file** and nothing else changes.
  [`RELEASING.md`](RELEASING.md) carries a pre-cut check that says so.

- ✅ **`ChangelogTests` was red on every release commit, and the check was what was wrong.** `ChangelogTests.TheChangelogHasAnUnreleasedSectionWithEntriesInIt`
  was red on every release commit, by construction, and it was the check rather
  than the tree that was wrong. Stamping a release moves every entry under the
  new version's heading and leaves `## [Unreleased]` **empty** — so the guard
  against a changelog nobody wrote was firing on the output of the step that
  writes one, and stayed red on `master` until the next change landed. It now
  accepts an empty section **iff the newest dated section's version is exactly
  the tag at HEAD** — `git describe --tags --exact-match` semantics, distance
  zero, so one commit past the release loses the exemption; `## [1.0.0] - …`
  against `v1.0.0`, the heading bare and the tag carrying the `v`, which is the
  sibling arm's rule read from the other end. A tag at HEAD naming some *other*
  version is a disagreement rather than an exemption and stays red, and the
  accepted refusal still has to be the empty-section one **and** the entries
  have to have moved rather than vanished. **Without git there is no
  exemption**: an export makes exactly the demand this arm made before, which is
  the direction that can only get stricter. Decided by the maintainer as Q183a.
  **Watched both ways on this tree**: red today at `97661d4`, whose
  `[Unreleased]` is empty and which the `v1.0.0` tag does not point at — *"the
  tag exactly at HEAD is 'none', and the newest dated section is '1.0.0'"* — and
  green with a throwaway tag at HEAD and the heading aligned to it. Four
  synthetic controls run unconditionally rather than inside the branch they
  describe, which would have made them unfailable on every day but one.

- 📝 **[`RELEASING.md`](RELEASING.md#the-release-gate) states the order of the last six steps.** [`RELEASING.md`](RELEASING.md#the-release-gate)
  states the order of the last six steps, because running them in the
  checklist's numeric order is not the same as running them in an order that can
  be green. Corrected by addition after a release attempt in which items 9 and
  10 preceded item 8 and **both of the resulting reds were artifacts of that
  order rather than defects**: stamping the changelog empties `[Unreleased]`,
  which the changelog check then refused, and replacing the `v1.0.0` tag turned
  the only published release into a **Draft**, which took
  `releases/latest/download/releases.win.json` to **404** — *"about 20 minutes,
  and the start of that window is a bound rather than a measurement"*. The order
  recorded is: pack for the gate → **item 8 at the pre-stamp content** → item 10
  stamp and seal → item 9 tag → **clean re-pack** → publish → feed verified. Two
  facts the sequence turns on are recorded with it: `Test-ReleaseVersion.ps1`
  **refuses** an equal version rather than calling it monotonic, so a re-pack
  must be a new version or an explicit rollback republish; and a `Releases/`
  still holding older artifacts makes `vpk` write **seven** rows into a feed
  whose published shape is one.

- 📝 **[`README.md`](README.md#status)'s test-count sentence, re-measured with a release pack present.** [`README.md`](README.md#status)'s
  test-count sentence, re-measured with a release pack present, which is the
  arrangement in which the two pack-gated arms run rather than skip.

- 📝 **Three documents said this product had never been distributed, and it had been.** Three
  documents said this product had never been distributed, and it had been
  publicly downloadable for a month. `CLAUDE.md`,
  [`README.md`](README.md#status) and [`RELEASING.md` item
  13](RELEASING.md#13-third-party-notices-ship) each carried a dated 2026-08-24
  correction reading *"nothing has been distributed"* / *"that handoff has not
  happened yet"*. A **non-draft** GitHub release has stood at `v1.0.0` since
  2026-08-17T01:54Z carrying `BrowserAI-win-Setup.exe`,
  `BrowserAI-1.0.0-full.nupkg` and `releases.win.json`, downloaded **1**, **0**
  and **687** times — that last one an installed BrowserAI polling its feed
  rather than a person. **All three were corrected by addition**, because a
  reader who learned the old sentence has to learn it was reviewed and found
  wrong rather than find it quietly gone. **The 2026-08-24 correction was
  derived from `git tag --list` and a gitignored `Releases/`** — the two places
  that cannot see a GitHub release — which is how a claim gets re-checked,
  re-stamped and left wrong, and it is why the replacement sentences name `gh
  release view v1.0.0` as their oracle instead. **The half that was load-bearing
  survives, narrowed**: a download is not an install, so every design argument
  reasoned from *"sessions exist in the wild"* is still reasoning past the
  evidence, and no session in the wild has been observed. **Redistribution
  obligations attached on 2026-08-17 rather than today**, and whether the
  artifact published that day carried the six notices `ThirdPartyNoticeTests`
  enumerates **was not re-checked** — nobody has looked, which is recorded here
  rather than resolved by reasoning.

- 🔒 **One of the two WebMCP tools upstream added is refused, and the other is
  not.** `@playwright/mcp`
  0.0.81 added `browser_webmcp_list` and `browser_webmcp_call`, both in
  upstream's default surface. **`browser_webmcp_call` is withheld from
  `tools/list` and refused if a caller names it anyway**, on liveness: it runs a
  tool the *page* supplies and waits for it with no timeout at all — measured
  2026-09-15 at **45,002 ms** against a page whose tool never answered, where a
  well-behaved tool on the same page answered in **521 ms** — so an unattended
  run that called it could hang until it was killed, and any page can arrange
  that. **`browser_webmcp_list` is allowed**: upstream bounds it at five
  seconds, it answers in 2 ms, and it is how a caller finds out what a page
  offers before acting on it with the ordinary tools. This is the first release
  to withhold more than one tool; `browser_annotate` is the other, for the same
  reason. **What a deny does not close, stated because it reaches you anyway:**
  upstream writes `- N webmcp tools available on the page` into every tab header
  whose count is non-zero. That line carries the count and none of the page's
  text.

- ⬆️ **`@playwright/mcp` 0.0.81 and `playwright-core` 1.64.0-alpha-2026-09-14 adopted.** `@playwright/mcp`
  0.0.81 and `playwright-core` 1.64.0-alpha-2026-09-14 adopted, one day after
  0.0.80 and reviewed the same way. All four golden snapshots moved, which had
  never happened before. Two upstream changes worth knowing about and neither
  visible in a schema: `checkFile` now resolves symlinks before comparing, which
  tightens the containment BrowserAI relies on and needed nothing done; and
  eleven tools' `filename` descriptions now say a relative name resolves against
  the workspace root, which **reads** as a change to where artifacts land and is
  not one — measured identical on 0.0.80 and 0.0.81 against a probe with the two
  directories pointed apart. The adjudication, the declines and every
  re-verification row are in [`upstream-review.json`](upstream-review.json).

- 🔧 **The generated child config now writes `timeouts.idle` explicitly.** The
  generated child config now writes `timeouts.idle` explicitly, at upstream's
  own one-hour default. It cannot fire — BrowserAI closes an idle browser after
  ten minutes and both timers are reset by a tool call — and it is written
  precisely because it cannot: an omitted key records no decision and cannot be
  read back out of a running child, so the day upstream moves its default, a
  build here goes red instead of quietly changing behaviour. Nothing a caller
  can observe changes.

- 🔒 **BrowserAI refuses to start when its *install* root is outside your Windows profile.** BrowserAI
  now refuses to start when its *install* root is outside your Windows profile,
  as well as its data root. The live-instance census — which is what decides
  whether applying an update is safe — is keyed to the install root, and
  `Setup.exe --installto` could put it somewhere two Windows users share, where
  the machine-wide mutex behind it silently stops working and an update apply
  then terminates the other user's browsers. The refusal names **both** roots
  and the remedy that can move the one at fault, because `BROWSERAI_ROOT` moves
  only the data root and `--installto` only the install root. **What you have to
  do: nothing**, unless you deliberately install outside your own profile, which
  is now refused rather than accepted silently.

- 📦 **The release downloads are called `BrowserAI.exe` and `BrowserAI.zip`.** *(Previously
  `BrowserAI-win-Setup.exe` and `BrowserAI.app-win-Portable.zip`.)* The
  packaging tool names its output after the pack id, which exists to answer a
  question about install directories and has no business on a releases page; the
  feed-internal package names are deliberately untouched, because the updater
  resolves those by name.

- 📦 **BrowserAI's data moved out of the install directory, and the installer's
  own name changed with it.** The
  program installs into `%LocalAppData%\BrowserAI.app`; the browsers it
  downloads, the index of your session directories, its log and its registration
  record live in `%LocalAppData%\BrowserAI` **beside** it. The download is
  called `BrowserAI.exe`. **Why:** `Setup.exe` renames a non-empty install
  directory aside and deletes it — which is what running the installer a second
  time does — and uninstalling empties it, so under the old layout a repair
  install cost 768 MB of browsers and every session's entry in the index. **What
  you have to do: nothing, and there is nothing to migrate** — this is the first
  release, and no build carrying the old layout has ever been distributed.

- 📦 **Uninstalling now asks whether to delete your data, and keeps it by
  default.** The
  prompt names the directory and how much it holds; *No* is the default and an
  unattended uninstall — `QuietUninstallString`, `winget`, any script passing
  `--silent` — keeps without asking. An update never asks and never touches it.
  Reinstalling finds everything exactly as it was.

- 🔧 **BrowserAI no longer keeps running when the installer starts it.** A
  non-silent install finishes by launching the program, which left a server and
  a browser-server child running until the machine was rebooted, serving nobody,
  with a console window on screen (measured 2026-09-14). It now writes one log
  line and exits before starting anything. The same applies in general: a
  BrowserAI whose launcher has gone **and** whose input is a console rather than
  a pipe has no way of ever being told the conversation is over, so it exits
  cleanly instead of waiting for ever.

- 💥 **Every machine re-provisions its browser on first run after this.** Two
  reviews in two days moved the pinned revisions twice, and what ships is the
  second: **Chromium 1237 → 1244** (152.0.7977.8 → 154.0.8037.0) and **Firefox
  1539 → 1544** (153.0 → 155.0) — 0.0.79 → 0.0.80 took them to 1243 and 1542,
  and 0.0.80 → 0.0.81 took them the rest of the way. Nothing in the payload
  changed — browsers are provisioned on first run, not built into the installer
  — but the first session after upgrading downloads again, measured at **203.8
  MB in 23.6 s** on the gate machine, because a cached tree holding 1237 is
  refused rather than reused. **A rollback re-downloads too**, which is the half
  that is easy to leave out: going back to a build pinning 1237 finds 1244 on
  disk and fetches 1237 again. The old revision does not accumulate —
  `RevisionPrune` removes it on the next successful provision — so the cost is
  bandwidth and one slow first call, not disk.

- ⬆️ **`@playwright/mcp` 0.0.80, `playwright-core` 1.63.0-alpha-2026-08-31 and Node
  v24.21.0 adopted.** Upstream's
  own tree changed in nothing but packaging between 0.0.79 and 0.0.80 — `tests/`
  and `config.d.ts` are byte-identical — so the whole of it is the Playwright
  roll. Of the four golden snapshots, `cli-help.txt` and `config-schema.d.ts`
  did not move a byte; `tools-list.json` gained two tools and `browsers.json`
  the revisions above. Node stayed on the Krypton LTS line, with OpenSSL 3.5.8
  the only component that moved with it. The adjudication, the declines and
  every re-verification row are in
  [`upstream-review.json`](upstream-review.json).

- ⬆️ **Upstream now honours `chromiumSandbox` from a config file.** Upstream now
  honours `chromiumSandbox` from a config file, having deleted the CLI-stage
  line that always overwrote it
  ([microsoft/playwright#42288](https://github.com/microsoft/playwright/pull/42288)).
  **BrowserAI's behaviour is unchanged** and deliberately so: it passes
  `--sandbox` on the command line, so its browsers were sandboxed before the fix
  and are sandboxed after it. What changed is that the flag is no longer the
  *only* thing that works — it is now belt and braces, and it stays, because
  moving the decision into the config file would rest this product's security
  posture on a default that has just been measured to move. This repository had
  carried the pending fix, its mechanism and its predicted outcome since
  2026-08-17, and both halves of the prediction held.

- ⬆️ **Every source and test project re-resolved.** `Microsoft.Extensions`,
  `Microsoft.DotNet.ILCompiler` and `Microsoft.NET.ILLink.Tasks` 10.0.11 →
  10.0.12, **MinVer 7.0.0 → 8.0.0**, and in the test projects TUnit 1.65.68 →
  1.67.0, `Microsoft.Testing.Platform` 2.3.3 → 2.4.0 and CsWin32 0.3.321 →
  0.3.333. The NativeAOT publish under the new ILCompiler is clean — **zero
  warnings and zero ILC, trim or AOT complaints**. MinVer 8's one breaking
  change is that `MinVerDefaultPreReleasePhase` now errors instead of warning;
  this tree has never set it, and the derived version keeps its shape exactly
  (`1.0.1-alpha.0.206` from `v1.0.0-206-g5f12649`).

- ✅ **Every run now states the publish freshness it established, instead of only
  refusing when it did not.** `PublishedSlice.EnsureFresh`
  has compared the published NativeAOT binary against every input that goes into
  it since the beginning, and it did exactly two things: it threw, or it said
  nothing — while the coverage block's `published slice` row reported `PRESENT`,
  which is a claim about existence. So twelve green gate logs carried no
  sentence about whether the binary they drove belonged to the tree they were
  reading, and on 2026-08-30 a gate runner with a staleness suspicion reached
  for the nearest figure to hand: **a commit date**. Commit `56383c9`'s
  **01:20:40**, touching `src/BrowserAI/Sessions/SessionLock.cs`, went beside
  the binary's **01:14:16.500**, and four gate sets — twelve full runs — were
  reported as having driven a stale binary while passing the check. **Every
  reading in that account was true and the conclusion was false**: that file's
  own timestamp was **01:12:22.665**, before the publish, and `git commit`
  records when it ran rather than touching a working-tree file. Dissolving it
  took an investigation that one printed line would have ended. A `publish
  freshness` row now sits directly below the capability rows, reading `FRESH`,
  `STALE` or `NOT ESTABLISHED`, with both modification times in **UTC to the
  millisecond** — the misreading turned on a gap of one minute 53.8 seconds, and
  the two figures put side by side that day were a local-time file stamp and a
  commit date with no zone named in either. **The row and the guard are one
  comparison and not two:** `Measure` walks the inputs once, `RefusalFor`
  renders that reading as the exception and `RowFor` renders it as the row, and
  the live arm asserts the tie in both directions rather than leaving it to the
  construction.

- 📝 **The six-run-gate hazard closed on the condition it wrote for itself.** The
  six-run-gate hazard closed on the condition it wrote for itself, hours after
  re-opening on the condition it wrote for itself. That row closed on 2026-08-24
  on a cause rather than on a test, so it was obliged to name what would re-open
  it; the 2026-08-30 release gate produced that exact shape and it re-opened;
  and the re-opening named in turn what would close it — *the quiet-machine gate
  the maintainer has ordered coming back green, six for six, with the repaired
  tee never needed*. That gate ran at `5237e9d` on a machine measured quiet by
  process path at seven instants, and came back **651 of 651 in every one of the
  six runs, nothing failed and nothing skipped**, at 1 m 53.316 s, 1 m 49.647 s,
  2 m 08.276 s, 2 m 09.551 s, 2 m 02.417 s and 2 m 05.281 s, with
  `initializeServer` occurring **zero** times across the six logs. **The product
  was held constant across both gates:** the slice these runs drove is the same
  file the failing run drove, because nothing under `src/`, `build/` or
  `third-party/` moved between `56383c9` and `5237e9d`. **What the closure does
  not establish is a cause.** The maintainer's Firefox went away in the same act
  as another repository's suite and the load the two made together; the commit
  charge reads 34.2 % to 35.1 % against the failing gate's 49.5 % to 51.2 % and
  returned HEALTHY on both, so it separates this band from the 2026-08-24 kernel
  leak and separates nothing inside it. **And one clean set of six against a
  shape seen at one in six is about one flake-lifetime** — the row says so in
  its own cell rather than leaving a reader to work it out, and the re-opener it
  names is unchanged. The recorded same-slot observation is downgraded rather
  than dropped: both stalls were the sixth run from Git Bash, and this set's
  sixth run from Git Bash was green, so two for two is now two of three. `open`
  falls to 44 and `closed` rises to 147, which are the figures that stood before
  the row crossed the other way the same morning — **the first row in this table
  to move twice in one day, and the first to return to a status it had already
  left.**

- ✅ **A stale-publish alarm was a commit date read as a file timestamp.** A
  stale-publish alarm that was a commit date read as a file timestamp, and the
  finding now sits where the substitution gets made. A gate runner put the
  published binary's `LastWriteTime` of 01:14:16.500 beside commit `56383c9`'s
  date of 01:20:40, saw that commit touching
  `src/BrowserAI/Sessions/SessionLock.cs`, and reported that four gate sets —
  twelve full runs — had driven a stale binary while passing
  `PublishedSlice.EnsureFresh`. **Every part of that reading is true and the
  conclusion is false.** Both sides of the comparison are modification times,
  `git commit` records when it ran and never touches a working-tree file, and
  `SessionLock.cs`'s own timestamp was 01:12:22.665 — one minute 53.8 seconds
  *before* the publish. The batch edited, published, gated and committed in that
  order, which is the order its own commit message says it took. Re-measured
  over the whole freshness corpus on 2026-08-30: **0 of 95 inputs newer than the
  binary**. **What made the misreading available is that the check is silent
  when it passes** — it throws or it says nothing at all, and the coverage
  block's `published slice PRESENT` row is a claim about existence rather than
  about freshness — so a log full of green runs offers no sentence to check a
  staleness suspicion against, and the nearest thing to hand is a commit date.
  Both halves are written into the doc comment on `EnsureFresh`. Making a run
  state its own freshness margin is a change to the coverage block rather than
  to that method, and it is deliberately not made here. **A third paragraph
  records why the check is on timestamps at all**, which the file had never
  said: two publishes of an identical input set, taken on 2026-08-30 with
  nothing between them, produced binaries of the same length and **different
  SHA-256** — so a content hash cannot answer *is this binary from this source*
  for this toolchain, and the modification times are what is left.

- ✅ **A hazard row the parser could not read left both tallies in the same
  instant, and nothing said so.** `HazardIndex.Rows`
  keeps a pipe-leading line only when it splits into exactly eight fields, and
  discards every other one in silence — which is right for the header, for the
  separator and for the three-column table above the index explaining what each
  column is for, and catastrophic for a row somebody wrote a bare `|` into. On
  2026-08-30 `FileShare.ReadWrite | FileShare.Delete`, written into an evidence
  cell to record what the fix below had opened a file with, split its line into
  nine fields and deleted the row the same commit had just re-opened. **That is
  the one failure this table's counting mechanism cannot describe**: a dropped
  line is absent from the `open` tally and the `closed` tally at once, so
  `RecordedCountTests` can only ever see that a number moved, never which row
  went missing. **The red established it is worse than that.** With the guard
  stashed and the offending row planted, all nine tests of the two classes that
  read this table passed — the tally included — because a row that arrives
  malformed never registers in either direction, so no number moves at all and
  there is nothing left to disagree with.
  `HazardIndexTests.EveryLineOfTheTableSplitsIntoTheFieldsTheParserReads` now
  names the line, the field count, the leading cell and the three ways out, and
  **the escape it recommends was made true rather than suggested**:
  `HazardIndex.SplitRow` reads `\|` as GitHub-Flavoured Markdown's own literal
  pipe, so the same planted row written that way parses into eight fields and is
  *counted* — which is the second red, the tally going to 46 `open` against a
  published 45. Without it the advice would have moved the row from one silent
  skip to another. **The guard is a second walk of the table rather than a
  second reading of it**: `HazardIndex.TableLines` follows the contiguous run of
  pipe-leading lines below the header, `Rows` scans the whole file, the two
  share the split and nothing else, and their counts are asserted equal — which
  is the half that catches a skip the field count cannot see, such as a row
  whose `Area` cell is all dashes, or one written with a leading space that ends
  the region early and takes every row below it out of the guard's sight. **No
  hazard row was added, by that file's own precedent**: a row records a blind
  spot, and a test closes it.

- ✅ **The failure dump could not read the files it exists to inline.** The
  failure dump could not read the files it exists to inline, and the one run
  that needed it printed three sharing violations instead.
  `LauncherWait.Evidence` walks the launcher's scratch tree and reads every file
  into the failure message — with `File.ReadAllText`, which asks
  `FileShare.Read`. Windows checks a reader's share mode against the accesses a
  live writer already holds, so a reader that does not permit writing is refused
  **however permissive the writer was**: node's `fs.openSync` shares read, write
  and delete precisely so that a log can be tailed while it is written, and the
  reader lost to it anyway. On the 2026-08-30 release gate's sixth run — the
  Firefox stall this instrument had been armed for two hours earlier — all three
  capture files came back `(unreadable: … used by another process)`, and the
  scratch tree was then deleted as designed. It opens `FileShare.ReadWrite |
  FileShare.Delete` now, and **the byte count comes off the handle**: the old
  dump printed whatever the directory enumeration had cached, and watching the
  red proved that number wrong as well as unmeasured — `(0 bytes)` against 63
  real ones, character for character the line the gate printed. A file nothing
  can open still reads `(unreadable: …)` and now carries **no length at all**,
  because nothing measured one. Both arms of `LauncherEvidenceTests` were
  watched red against the old body. **The driver closes its `cli-stderr.log` tee
  once the child it was teeing ends**, and that is the smaller half and is
  written down as the smaller half: the launcher kills the driver with
  `TerminateProcess`, where no handler of any kind runs, and a browser that
  never comes up never ends its child — so on the failure worth diagnosing the
  file is still open when the dump reads it, which is why the reader is where
  the fix had to go. The close is on `'close'` rather than `'exit'`, because
  `'exit'` fires while `stderr` may still have chunks to deliver and the tidy-up
  would drop the last thing upstream said.

- 📝 **The six-run-gate hazard row is `open` again, on the condition it wrote for
  itself.** It
  closed 2026-08-24 against a kernel-level memory leak on this machine — a cause
  outside this repository entirely — and because it closed on a cause rather
  than on a test it was required to name what would re-open it: the same shape,
  inside a gate run, with nothing external to explain it, **on a machine whose
  memory is healthy**, with the commit charge beside the run named as the one
  reading that separates the two. The 2026-08-30 release gate met every clause.
  Run 6 of six went **647 total, 1 failed** on the same Firefox containment arm
  at **3 m 03 s** — Playwright's own `initializeServer` budget and not one of
  ours — while the harness had spent 3 minutes of a 30-minute one; containment
  held with 0 escapees and all 8 processes in the job; and the commit-charge row
  that landed two hours earlier read **HEALTHY, 49.9 % → 51.1 % of 141,229
  MiB**, against **137.4 GB of a 157.7 GB limit** in the instance the leak
  explains. **The 2026-08-24 diagnosis is not withdrawn** — it still accounts
  for its own instance, and what the healthy reading falsifies is that cause
  applying to this recurrence. It is the first row in the hazard index ever to
  cross back: rows that are `open` 44 → 45, `closed` 147 → 146, and rows that
  are `open` while carrying `—` still 0. A row that re-opens because its closure
  was written to be falsifiable is that closure working.

- 🔧 **A process re-taking a session directory it last held now says so.** A
  process re-taking a session directory it itself last held now says so, instead
  of reporting a reclaim from a live stranger. `destroy` and `set_purpose` both
  dispose the live session and re-acquire, so the guard on disk names the very
  process about to take the directory — and every one of those was logged
  *"previous holder was PID n, still running: True"*, which is true word by word
  and false as a whole. It names the one event on that path worth waking up for,
  and it had happened five times in eight thousand. Counted 2026-08-30 over the
  machine-wide process log, predicate quoted: since the 2026-08-26 logging
  cutover that file holds **8,423** `Session lock reclaimed` lines, **8,418** of
  them carrying `still running: True`, and **zero** `Session lock acquired`
  lines — in the two 2026-08-29 files alone it is **2,081 of 2,081**. There is a
  distinct record now, with its own event id and no `still running` clause,
  taken on `(pid, creationFileTime)` so a stranger wearing this process's number
  still reads as the takeover it is. **The outcome did not move**: `Reclaimed`
  and `HolderRunning: true` are answers about the directory rather than about
  who is asking, and both are still right. What was wrong was the sentence.

- ✅ **The suite's coverage block records the machine's commit charge.** The
  suite's coverage block records the machine's commit charge, at both ends of
  the session. The hazard row that closed on 2026-08-24 against a kernel-level
  leak outside this repository closed by naming the one reading that separates
  that cause from a live one — *the commit charge beside the run* — and then
  nothing took it, so for six days no gate could ask its own question. The row
  reads `HEALTHY` / `TIGHT` / `CRITICAL` / `UNREADABLE`, judged on the worse of
  the two readings, and the two loud bands say in the run's own output why the
  run's timings are not safely attributable to the code. **No assertion reads
  the numbers** — they are properties of whatever else the machine is running —
  so the bands are exercised from synthetic readings and the live arm checks
  only that the row is produced.

- ✅ **The browser-containment driver reads its child's `stderr` instead of buffering it.** The
  browser-containment driver reads its child's `stderr` instead of piping it
  into a buffer nobody drains. It spawned `cli.js` with all three streams piped
  and read only `stdout`, so upstream's account of every launch that did not
  happen was discarded — which is why the 2026-08-18 dump could name a failure
  and not say why, and why the 2026-08-29 one printed `child-stderr.log (0
  bytes)` and nothing else. It is teed to `cli-stderr.log` in the scratch
  directory the failure dump already walks, so no wiring was needed on the host
  side, and written synchronously per chunk because the process is killed from
  outside and a stream's buffered tail would die with it. It closes a second
  failure mode nobody had measured: a pipe whose reader never reads it fills,
  and a child blocked writing into a full pipe hangs indistinguishably from the
  browser stall the arm exists to catch.

- ✅ **A payload that re-resolved makes the published binary stale.** A publish
  copies the resolved payload beside the executable, so a re-resolve that moved
  `@playwright/mcp`, `playwright-core` or `node` left every slice arm driving a
  published tree carrying the old one — reading as fresh, with the lock in the
  tree saying otherwise. `build/payload/package-lock.json` is watched now: it is
  the committed record of exactly that resolution, and it is watched by name
  because the walk prunes every directory called `payload` and is blind to it by
  construction. Found during release preparation on 2026-08-29 and benign on the
  day, only because that re-resolve had come back byte for byte.

- 📝 **The silent Chromium death was reproduced on purpose, and desktop heap is the cause.** The
  silent Chromium death was reproduced on purpose, and desktop heap is the cause
  of everything about it except its exit code. `QUESTIONS.md` §8 had been open
  for nine days on three sightings and one recurrence that diagnosed itself down
  to a five-line browser log; the reproduction ran on 2026-08-27. A desktop of
  its own inside `WinSta0` — 20,480 KB, the same allocation `WinSta0\Default`
  gets, so the interactive desktop was never touched — filled to the byte by one
  process, and the provisioned Chromium launched onto it with the sweep test's
  own command line. **Eighty launches, twenty-eight deaths**: exit `0x80000003`,
  nothing on either stream, the same five log lines in the same order ending at
  `scheduler_loop_quarantine_config.cc:195`, no message window, and a machine
  reporting 66% of RAM free. **The threshold is a cliff** — 4,634 windows held
  and the browser lives 3 of 3, 4,637 and it dies 8 of 8 — so the total silence
  is a property of the last four kilobytes rather than of the shortage. The exit
  code is the one thing that did not come out: the wild figure was `1` and the
  rig produced `0x80000003` and Chromium's own out-of-memory code `0xE0000008`,
  never a `1`, which is said as an unexplained gap rather than smoothed over.
  The measurement is in [kb](kb/windows/processes.md) with a re-establishment
  procedure that stands without the scratch rig, the failure mode is a hazard
  row, and the entry stays open on a narrower question than it was opened for —
  **not what kills the browser, but why the wild exit code was 1.** Nothing in
  the product detects or names this yet; that is a decision, and it is stated as
  one.

- 📦 **The release manifest says whether the release was a crunch override.** The
  release manifest can say whether the release was a crunch override, and it
  always says something. `DECISIONS.md` stated in bold that *"a release whose
  manifest does not say it was overridden is a release claiming it was not"*
  while no manifest could express one — so by that sentence's own logic every
  release claimed it was not overridden, including one that was.
  `build/Write-ReleaseManifest.ps1` takes five `-Override*` parameters and
  always emits the key: `"override": null` for an ordinary release, a five-part
  block for a held one. **An absent key is not a statement; `null` is.** A
  half-stated override refuses the manifest rather than writing half an account
  of the decision.

- 📦 **The release manifest records seven files, not six: `tool-verdicts.json` is
  the seventh.** It
  states which tools a build forwards and which upstream versions that judgement
  was made against, so a release that cannot produce it cannot answer why it
  refused a tool the next release allows. The manifest also carries the file's
  own `judgedAgainst` pair, read back out of the copy rather than transcribed.

- ⬆️ **Every release builds against the latest Playwright, and the one override is
  a human's.** Written
  down rather than assumed: a human may force a crunch override and **an agent
  may never**, and an override has to leave a trace in the release artifact —
  the manifest states the version that shipped, and the checklist item asks for
  what was held, at what version, and why.

- 📝 **Adversarial and hostile-caller defence is a stated non-goal.** The premise
  is that the model tries to behave and what BrowserAI steers is honest
  mistakes; guarding against a caller that owns the session directory, the
  profile and the same Windows user is *"hopeless and thus meaningless"*.
  Several arguments over the last week would have been a paragraph rather than a
  day had this been written down, and it retires a standing question about
  headless-with-storage that had been open since 2026-08-18.

- 📝 **Two hazards that lost their protection when the filename gate went are
  adjudicated and stay open.** A
  reused output name still overwrites and a name Windows will not keep verbatim
  is still stored as Windows rewrites it. The decision is **steer only, plus an
  upstream ask** — no door refusal on the string alone — and both rows say so
  now, in place, without closing.

- 📝 **A just-answered call can read as still in flight, and that is now written
  down.** The
  log row is settled in a `finally` that runs after the answer is sent, so a
  second agent's `browserai_catch_up` landing in that window is told no answer
  was recorded about a call that has just been answered. The ordering is
  deliberate — settling first would risk a `successful` row for an answer the
  caller never received — and the window is a hazard row rather than a fix.

- 🔒 **`browser_start_recording` and `browser_stop_recording` are judged `allow`.** `browser_start_recording`
  and `browser_stop_recording` are judged `allow`, and the advertised surface
  goes 68 → 70. The two tools `@playwright/mcp` 0.0.80 added were withheld from
  nothing — an unjudged tool is advertised and then refused, so the surface had
  already grown and the door had not. They were measured before they were
  judged: `browser_start_recording` returns in **40–46 ms** and arms a recorder
  rather than waiting for a human, every other tool answers normally while a
  recording is live, and `browser_stop_recording` returns Playwright code — so
  the liveness shape that withholds `browser_annotate` does not reach them.
  `browser_annotate` is still the only withheld tool. `judgedAgainst` moves to
  `@playwright/mcp` 0.0.80 / `playwright-core` 1.63.0-alpha-2026-08-31.

- 📝 **The server instructions told models something false about screenshots.** The
  server instructions were telling models something false about screenshots, and
  the correction hands them a lever they did not have. The `fullPage` sentence
  said an image *"is downscaled to that ceiling"*. Upstream deleted
  `scaleImageToFitMessage`, so **nothing downscales an inline image at any
  size** — measured through a raw child at three viewports, both page shapes and
  three encodings, with the inline block byte-identical to the file in every
  case and a `fullPage` shot of a 20,016 px document coming back at 1280x20016.
  Cost follows pixels with no ceiling anywhere. The new sentence says that, and
  says the thing the old one never mentioned: **passing `filename` returns a
  link and no inline image at all**, which costs zero image tokens. The 1,568 px
  bound in the vertical slice is now the viewport on both sides, and asserts
  byte-identity rather than a ceiling.

- 🔧 **The refusal for an unjudged tool used to tell the caller to send it
  again.** It
  ended *"every tool in that list reaches the browser, and a name that is not in
  it never will"* — and an unjudged name **is** in `tools/list`, because only a
  `deny` row is filtered out. Read against a list the caller can see its own
  name in, that is an instruction to retry, and a model that believes it retries
  until something else stops it. It now says that being listed is not being
  judged, that retrying will fail the same way until a human adjudicates it, and
  not to retry. The `deny` refusal is unchanged. The state is unreachable in a
  release — an unjudged tool is a red build — so the window this sentence is
  read in is exactly the one between an upstream roll and its adjudication.

- 📦 **`build/Build-Payload.ps1` sets `PLAYWRIGHT_SKIP_BROWSER_GC=1`, as the product always has.** `build/Build-Payload.ps1`
  sets `PLAYWRIGHT_SKIP_BROWSER_GC=1`, which the product has always set and the
  script never did. Rebuilding the payload for the 0.0.80 review ran upstream's
  stale-browser collector and it **deleted `firefox-1539`** — a complete
  provisioned tree nothing in that script installs. `ChildEnvironment.Forced`
  has protected every child since it was written; this was the second place that
  starts an upstream process, kept by habit in one of the two.

- 📝 **Two upstream asks were transferred by upstream itself.** Two upstream asks
  were transferred by upstream itself, and the watch item that was reserving
  that decision has fired. `playwright-mcp#1725` is now
  [playwright#42497](https://github.com/microsoft/playwright/issues/42497) —
  open, triaged, with a maintainer's PR open and set to close it — and
  `playwright-mcp#1726` is now
  [playwright#42496](https://github.com/microsoft/playwright/issues/42496),
  **closed `not_planned`**. The two Q128 hazard rows that were steering toward
  that second one keep their status and lose their recorded upstream exit; both
  say so in place. A third ask was filed in the monorepo the same day:
  [playwright#42717](https://github.com/microsoft/playwright/issues/42717), a
  `webp` screenshot past 16,383 px coming back as a zero-byte image with
  `isError: false`.

- 🔧 **An aliased session directory is resolved rather than refused.** An aliased
  session directory is resolved rather than refused, and the network refusal now
  runs at every door. `\\?\C:\work\sess`, a `subst`ed drive letter, a junction,
  a directory symlink and a mount point are all taken as the directory they
  name: BrowserAI records the spelling the filesystem itself uses and says so
  once, in the `init` or `resume` answer, instead of spending a turn refusing
  and asking the caller to type the answer back. Every one of those answers was
  already being computed to build that refusal, so this costs no syscall at all.
  **What is still refused is refused everywhere** — at `browserai_destroy`,
  `browserai_set_purpose`, `browserai_catch_up` and `browserai_list` as well as
  at `init` and `resume`: a UNC path, a mapped drive letter, the `\\.\` device
  namespace, and a name Windows would silently rewrite (a segment ending in a
  dot or a space, a reserved device name such as `NUL`, an alternate data
  stream, a wildcard, a control character).

  ⚠️ **Every session recorded from a shell that spelled the drive letter
  lower-case gains one `directory` statement on its next resume, and that is the
  whole of what this looks like from the outside.** Windows reports every path
  with an upper-case drive letter, so `c:\work\sess` becomes `C:\work\sess` in
  answers and in `browserai.data`. Nothing hashed moves — the identity is
  case-folded — so no mutex, no index entry and no lock file changes for an
  ordinary local directory. A session whose own path traverses a junction, a
  `subst` or a `\\?\` spelling **does** change identity: its index entry is
  swept rather than orphaned, and the next `init` or `resume` records it again.

- 🔧 **`browserai_list` pointed at an alias of a tree now finds the sessions under
  it.** It
  had a path chain of its own — `Path.GetFullPath` plus an upper-cased prefix,
  with no alias resolution — because the shared one refused a volume root and a
  volume root is exactly what a caller passes to see everything. So a listing of
  `D:\link\work` where the sessions live under `C:\real\work` answered *"No
  BrowserAI sessions under '…'. That is an answer rather than an error"*:
  confident, wrong, and not a refusal, so there was nothing to correct.

- ⚡ **`browserai_list` no longer parses every session record on the machine.** `browserai_list`
  no longer opens and strictly parses every session record on the machine to
  print the few under a prefix — and neither does `init` or `resume`.
  [Adversarial review
  F9](docs/reviews/2026-08-24-adversarial-since-the-mode-drop.md). The subtree
  filter ran on the wrong side of the parse: `SessionIndex.Follow` opened each
  entry's `browserai.json` and parsed it strictly — up to 250 log entries and
  all their arguments — and `IsUnder` was applied to what came back. Each of
  those opens goes through `RenameWindow.WaitOut`, whose budget is **30
  seconds** and which retries a denial, so one session *anywhere on the machine*
  whose record is denied by an ACL or held by a scanner could add that to a call
  scoped to a completely unrelated tree. **The sharper half is the one the
  review does not name**: the roll-up runs the same walk on every
  `browserai_init` and every `browserai_resume`, so this was on the session-open
  path rather than only on a listing. `SessionIndex.FollowUnder` applies the
  prefix **above** the open, at the one point where everything already done is
  the entry's own verification and nothing has been opened inside the session.
  **The predicate is unchanged and only its position is**, so the reported set
  is bit-identical; `IsUnder` moved with it rather than being spelled twice.
  `Follow()` keeps its exact semantics and stays whole-machine for the index's
  own sweep, the reinstall census and the stray sweep. *Corrected:
  `SessionIndex`'s contract sentence "Every entry is verified by opening the
  `browserai.json` it points at", which becomes "no entry is ever **reported as
  a session** without opening it" — a subtree read decides which entries to ask
  about and never which answers to trust; and "the only way this store is ever
  read", which is now one of two.* **The header-only-read fork was declined, and
  the reason is the symmetry:** it *could* have been planted red, precisely
  because it changes what is reported — a record whose log is malformed would
  read as a session to the listing and be refused by `TryAcquire`, which is two
  readers of one file that disagree. The fork that is safe is the fork that is
  invisible, so the red-today test is the rule itself:
  `HouseRuleTests.NoIndexWalkFiltersBySubtreeAfterFollowingTheEntry`, planted
  red on both real offenders, with both synthetic controls and a non-vacuity
  floor over the two whole-machine walks that must stay whole-machine. ⚠️
  `SessionIndexTests.FollowingOneSubtreeReturnsExactlyWhatFollowingEverythingWouldHaveReturnedForIt`
  and `.FollowingOneSubtreeOpensNoRecordOutsideIt` ship with the change, name an
  API that did not exist before it, and say in their own remarks that they are
  **weaker than a red test** and why.

- 🔧 **The machine-wide log is one shared file under a cross-process write gate.** The
  machine-wide log is one shared file under a cross-process write gate, and a
  session's records no longer go into it. Two changes that are one decision.
  **First, scope:** anything attributable to a session is written to that
  session's own `browserai.log` and to nothing else —
  `ProcessLog.OpenSessionLog` no longer adds a second provider over the
  machine-wide writer, and the proxy's per-call refusals are logged through the
  session's own logger where one is in hand. What stays central is what no
  session owns: the stray sweep, which is machine-wide by design because it
  hunts browsers belonging to *any* session, plus startup, updates,
  provisioning, the server transport, the MCP server, and the two proxy refusals
  with no session directory to be written into — a call naming no session, and a
  call naming one that does not exist. ⚠️ **That reduces the shared file's write
  rate; it does not dissolve the contention**, and the lock rather than the
  scoping is what makes the remainder safe. **Second, the lock**, chosen over
  one file per process at the maintainer's decision, verbatim: *"Simple to read,
  simple to write."* `NativeFile.TakeGate` takes an exclusive byte-range claim
  through `LockFileEx`, one byte past any possible end of file — a lock over the
  data would be enforced against `ReadFile` too and would refuse every
  concurrent reader of the log. A file lock rather than a named object for the
  reason `MaintenanceLock` already gives: the kernel releases it however the
  holder dies, where a semaphore's count is not restored. **The length is read,
  the instant is stamped and the bytes are written inside one claim**, so write
  order and timestamp order coincide and the file is sorted *by construction*
  rather than by anybody sorting it. **Every record now carries both of its
  times** — the leading column is when it reached the file, `made=` is when it
  was created, and the two diverge only under contention, which is the one thing
  a single timestamp cannot show. **And it names its writer as
  `pid=<n>@<createdFileTime>`**, never a bare pid: the log keeps thirty days and
  Windows reuses pids, so a bare one eventually names a stranger. The FILETIME
  is spelled the way `browserai.json` spells `processCreatedFileTime`, so a log
  line and a lock record name the same writer with the same characters, and it
  is the pair `ProcessLiveness.IsAlive` takes.

- 🔧 **Rotation happens exactly at the cap.** *Corrected in `RollingFileWriter`
  (previously "The starting size is read once. It drifts under concurrency,
  which only means the roll happens at approximately the cap rather than exactly
  at it — and paying a metadata query per record to fix that would be a worse
  trade").* The length is now the file's own, read through the open handle
  inside the write gate, so nothing can have appended between the read and the
  decision and there is no per-process counter left to drift. It is not a
  metadata query — the handle is already open — and no file in the directory
  ever exceeds 8 MiB, with one stated exception: a record larger than the cap on
  its own is written rather than dropped, and lands alone in its own file.

- ♻️ **`BrowserAiPaths` no longer claims to answer "the directory the product would
  actually have used".** It
  resolves through the product's own `LocalAppDataPaths` — which is the part
  worth keeping — but constructs it with **no** root argument, so it always
  answers the per-user default under `%LOCALAPPDATA%`. `Program.Main` honours
  `BROWSERAI_ROOT` and takes it over that default, and
  `PublishedSlice.InheritedEnvironment` copies the whole environment into the
  published child, so the two disagree in exactly the case the suite creates on
  purpose: a test that points a real BrowserAI at an empty browsers root.
  **Making it honour the override was considered and deliberately not taken** —
  the members are read by assertions about the developer's real provisioned
  tree, and following the variable would re-point them at the rig the variable
  was set to create. The comment changed; the resolution did not.

- ✅ **The release gate's two shells are two instruments by construction.** The
  release gate's two shells are now two instruments by construction, and a run
  says which drive-letter spelling it actually received. The gate runs the suite
  from PowerShell and from Git Bash because the two hand the test host different
  spellings of the drive letter — `C:\…` against `c:\…` — and an assertion
  comparing a composed path against one Windows re-spelled is green from one and
  red from the other. **That property held run to run rather than by
  construction**: on the 2026-08-24 gate all six runs received `C:`, three of
  them silently duplicating the other three, and every signal the gate publishes
  read exactly as it reads when both spellings really were exercised.

  Measured the same day: the lower-case spelling is not a property of Git Bash
  but of whatever started it. A bash that **inherits** its working directory
  hands a child `c:\…`; the same shell after **any** `cd` — `/c/…`, `c:/…`,
  `C:/…`, `c:\…` alike — hands it `C:\…`, because MSYS resolves the real path
  and the mount manager answers upper. So `cd` cannot be the lever. Both
  invocations in
  [Testing](TESTING.md#how-the-suite-is-run-detached-teed-and-the-log-polled)
  now hand `dotnet test` an **absolute, explicitly-spelled** path to the
  solution, which carries the spelling through `MSBuildProjectDirectory` and
  `TargetPath` into the test host's own `AppContext.BaseDirectory` whatever the
  working directory says — MSYS re-spells a command path and a `cd`, and leaves
  a path passed as an argument alone.

  **The forcing has its own check, because a forcing that silently fails to take
  is the same trap in a new coat.** Each half declares what it forced in
  `BROWSERAI_DRIVE_CASE`, the coverage block carries a **`drive letter`** row on
  every run naming the spelling, the base directory it was read off and whether
  the declaration held, and a run that did not receive what it declared is a
  failing test. Unset declares nothing and asserts nothing, which is what an
  ordinary developer run has always done — so the fault is planted in both
  directions by a pure arm beside the live one. **This is not `DriveLetterCase`
  restated**: that spells every guard path both ways *inside* a run and covers
  the class of defect; this covers the gate's claim about itself.

- 📝 **`browser_get_config` DOES redact `secrets`, and three places said otherwise.** `browser_get_config`
  DOES redact `secrets`, and the claim that it does not is corrected in three
  places. *Previously, in `DECISIONS.md`, `kb/playwright/tools-and-artifacts.md`
  and re-verification row 71: "its handler is `JSON.stringify(context.config,
  null, 2)` with no filtering, so it emits `config.secrets` in plaintext if that
  key is ever set", `Verified 2026-08-13 @ 0.0.79`.* The handler reading was
  correct and the conclusion drawn from it was wrong, because the redaction is
  not in the handler: every response leaves through
  `sanitizeUnicode(this._context.redactSecrets(serializedText))`, one layer
  above it. **Measured 2026-08-20** against the bundled child started with
  `secrets: {"MY_TOKEN": "sk-live-…", "OTHER": "hunter2"}` — the answer carries
  `"MY_TOKEN": "<secret>MY_TOKEN</secret>"` and neither literal value appears
  anywhere in the frame. Upstream's own `config.d.ts`, in the copy sitting in
  `payload/`, has said so all along.

  **This is not an endorsement of setting `secrets`, and the correction must not
  be read as one.** `redactSecrets` is `text.replaceAll(secretValue, …)` — a
  substring match on the **value**, over the whole response. Measured in the
  same run with a third secret whose value was `chromium`: `"browserName":
  "chromium"` came back as `"browserName": "<secret>COMMON</secret>"` and
  `chromiumSandbox` as `<secret>COMMON</secret>Sandbox`. A short or common value
  corrupts unrelated text, an empty value is skipped outright, and a value the
  page never renders verbatim is not redacted at all — *"a convenience and not a
  security feature"*, in upstream's words. BrowserAI still never writes the key
  and never passes `--secrets`, so on every ordinary call there is nothing to
  redact.

  **The same claim in
  `docs/reviews/2026-08-19-auth-transfer-and-session-modes.md` was deliberately
  left standing.** It is a dated record of what this repository believed on the
  day it was written, and correcting it there would destroy the account rather
  than improve it — which is the rule the append-only seal above now enforces.

- 🔒 **BrowserAI refuses to serve out of an app root that is not inside the current
  user's Windows profile.** The
  maintainer's decision of 2026-08-20, answering [`QUESTIONS.md`](QUESTIONS.md)
  §12 with *"L1 a"* — direction (a), refuse at startup. `%LocalAppData%` gives
  every user their own browsers directory, session index, log and `live\`
  markers; `BROWSERAI_ROOT` and the installer's install-to flag both defeat
  that. Measured on 2026-08-20, sharing a root is unsafe in a way nothing
  reports at run time: the **file** locks span users, because a share mode is
  enforced against handles rather than tokens, but the **`Global\` mutexes do
  not** — the DACL the kernel puts on one names LOCAL SYSTEM, the creating logon
  session and the creating user, with **no group ACE at all**. The user who
  loses the race cannot join the live set, creates no marker, and is invisible
  to the other's census; that census answers *alone*, and an apply then runs
  `force_stop_package`, which terminates every process under the install root.

  **The check runs before anything creates state** — before the stray sweep, the
  live marker, the instance directory and every session — so a refused root
  gains a log line and nothing else, and the test asserts exactly that: `logs\`
  is the only thing under the root afterwards. The refusal is a log record and a
  non-zero exit, because `stdout` is the protocol channel and `System.Console`
  is banned outright, and it carries the remedy rather than only the verdict.

  **It resolves through the filesystem rather than comparing strings.** Both the
  root and the profile go through `VolumeIdentity.DeepestExistingFinalName` — a
  walk extracted from `SessionDirectoryGuard`, so there is one implementation of
  *what does the filesystem call this* rather than two — and four arms of
  `InstallRootScopeTests` are about the false positive: a real `mklink /J`
  junction, a real `subst`ed drive letter, a real 8.3 short name and the
  extended-length prefix. **The arm that matters most points the other way**: a
  junction *inside* the profile whose target is outside it, which every string
  comparison accepts and which is a genuinely shared root. All four were watched
  red against a string-only implementation.

  **It narrows the hazard rather than closing it, and the row stays `open`.**
  *Outside the profile* is not the same predicate as *shared*: a single-user
  install at `D:\Tools\BrowserAI` is now refused for nothing, which §12's own
  table named as the cost of taking direction (a); and a profile directory whose
  ACL an administrator widened to a group is inside the profile and still
  accepted. A root whose final name cannot be read is **served with a warning**
  rather than refused, because a background MCP server that will not start on a
  locked-down machine is a worse failure than the one being prevented. All three
  gaps are stated on `InstallRootScope` itself.

- 🔧 **The claim on the browsers root is a reader/writer lock.** The claim on the
  browsers root is a reader/writer lock: every session holds it shared, and a
  reinstall holds it exclusively. The maintainer's design of 2026-08-20,
  verbatim: *"any init or resume should take a system level lock. No matter the
  browser type. These locks are cumulative. And reinstalling the browser should
  be an exclusive lock."* `browserai_init` and `browserai_resume` open
  `<browsers root>\reinstall.lock` `FileAccess.Read` / `FileShare.Read` and hold
  it for the session's whole life; `browserai_reinstall_browser` opens it
  `FileAccess.ReadWrite` / `FileShare.Read`. **Windows' sharing rules give the
  semantics directly** — an open is refused when its access is outside an
  existing handle's share mode *or* when its share mode is narrower than an
  existing handle's granted access, so any number of readers coexist with no
  count kept anywhere, and one reader is enough to refuse the writer.

  **The kernel is the gate now; the session census is only a sentence.** What
  used to decide the refusal was `SessionManager.LiveSessions`, which walked
  this process's sessions and the index; it survives to *name* what the caller
  must close, and the exclusive open decides. That is strictly stronger — a
  session whose index entry was swept, or whose process this one cannot see,
  still holds a handle.

  **The family filter is gone, and its removal is the maintainer's "no matter
  the browser type".** A live Firefox session now refuses a Chromium reinstall.
  The old reasoning — only a session of that family can hold an executable out
  of that family's tree — was not wrong and is no longer the question: the claim
  is one file at the root of the browsers directory and knows nothing about
  families, and listing only the matching family would name none of the sessions
  the caller has to close.

  **No intent marker, no drain, and writer starvation accepted**, all three his:
  *"I do not want the intent marker. If anything is busy then the reinstall
  should be refused with the list… But it should not start a drain/preventstart
  process of sorts. Keep it simple. Let the user solve the open sessions
  block."* A machine that always has one session open never lets a reinstall
  through, and that is a decision rather than a defect to be mitigated.

  **A reader that dies releases the claim with nothing running to clean up**,
  which is why this is a file and not a named semaphore — a semaphore's count is
  not restored on its holder's death, so one crashed session would refuse every
  reinstall until a reboot.
  `ReinstallBrowserTests.AReaderThatDiesReleasesTheRootWithNothingRunningToCleanUp`
  kills a real process holding the shared claim by closing a job object — a
  `TerminateProcess` with no unwinding at all — and proves the root goes free.

- 🔧 **Every refusal a caller meets during a long operation now says how far in it
  is.** The
  maintainer's instruction of 2026-08-20: *"Also make sure that the reinstall
  reports the progress just like the first run provisioning. Close both gaps."*
  First-run provisioning already reported bytes, elapsed and the observed rate;
  the other two refusals reported nothing.

  **The reinstall refusal** — what `init`, `resume` and a second reinstall meet
  while one is running — now carries what the download staging directory weighs,
  how long the claim has been held, and the rate those two give. Both figures
  are read off the filesystem, because a peer cannot see the reinstalling
  process's own provisioner at all: the staging directory is `<browsers
  root>\.downloads`, which the installer's `TEMP` points at, and the elapsed
  time is the claim file's last write. **Zero staged bytes is reported as a
  phase and never as a stall** — it is the delete, which comes first, or an
  extraction already under way, and the sentence says which two things it cannot
  tell apart.

  **The update refusal** — an update downloaded, staged and not applied because
  something else is live — now names *at least N other BrowserAI process(es)*
  rather than "another BrowserAI is running", distinguishes that from a census
  that could not be taken at all (a permanent block rather than a queue, and it
  says why), and states that nothing more has to be downloaded, with the package
  size and the seconds it took.

- ✅ **The provisioning stall detector runs on an injected clock and byte source.** The
  provisioning stall detector runs on an injected clock and an injected byte
  source, and its own flaky test is fixed by that rather than by weakening it.
  `ProvisioningTests.ASlowInstallThatKeepsWritingIsNotStoppedHoweverLongItTakes`
  went red once in nine consecutive full-suite runs on 2026-08-20 — the day CI
  was removed and the local suite became the only gate — with the product
  behaving perfectly: sixty real writes 25 ms apart against a real one-second
  cap is a ratio, and a ratio between two real clocks is still a race at
  unbounded suite parallelism.

  **Both of the detector's inputs are seams, because one alone would not have
  been enough.** `ProvisioningTimers.Clock` is a `TimeProvider`, and the **poll
  wait** goes through it too — a loop whose arithmetic reads an injected clock
  and whose sleep reads the wall clock cannot be driven, and would look
  deterministic while racing exactly as before.
  `BrowserProvisioner.WeighBrowsersRoot` is the byte source, because the
  detector judges an install on bytes on disk as well as on time. The two
  together let the test drive the loop in **lockstep**: the product asks what
  the root weighs, and answering is where the test moves the clock.

  **The assertion is stronger rather than weaker**, which was the constraint. It
  survives **1,000 polls each one tick short of the whole budget** — nearly
  seven simulated days against a ten-minute cap, in milliseconds of wall clock —
  and `.TheStallCapFiresOnTheFirstPollAfterTheBudgetPassesWithNoBytes` pins the
  other side to the exact poll the detector fires on, which no wall-clock test
  of this could have asserted. Both were watched red against a planted
  total-time cap and a planted one-budget-late cap. **There is no real duration
  anywhere in either arm**, so the flake is impossible rather than unlikely.

- 💥 **A session's record is `browserai.json`, renamed from `lock.json`.** A
  session's record is `browserai.json`, renamed from `lock.json`, and there is
  no compatibility read. The maintainer's decision of 2026-08-20, verbatim:
  *"nothing is in production yet. The only version that exists is the alpha
  version that we are building and testing in this session, so rename all the
  locks in code to browserai.json and don't take into account any legacy setup
  anywhere."* The file had stopped being only a lock long before the name did:
  every field of it is an ordered list of timestamped statements about how the
  session got here — mode, browser, purpose, holder, client — and
  `browserai_list` and `browserai_resume` read it for those rather than for
  ownership, so the old name described the smallest thing it does. The
  **handle** is still the lock, and every type that says so keeps its name:
  `SessionLock`, `LockRecord`, `LockScopes` and `SessionPath.LockFile` are about
  the lock, which is unchanged.

  **No fallback, no migration and no compatibility read**, which is the half
  worth stating: a directory holding the old name is not a session to this
  build, and `browserai_init` will make a new record beside it rather than
  adopting it. That is safe only because the old name never shipped — `v0.1.0`
  and `v0.1.3` are the only tags, and neither is installed anywhere but this
  machine.

  **The historical record keeps the old name.** `CHANGELOG.md`'s released
  sections and everything under `docs/reviews/` are dated accounts of what was
  true when they were written; a 2026-08-18 review saying `browserai.json` would
  be claiming a filename that did not exist for another two days, which is the
  same defect as a measurement updated by reasoning instead of re-measurement.
  `docs/reviews/` states that rule about itself and it is kept here.

- 🔒 **`allowUnrestrictedFileAccess` is set in every generated child config, always.** `allowUnrestrictedFileAccess`
  is set in every generated child config, always, with no argument that can turn
  it off. The maintainer's answer of 2026-08-20, asked whether it should be
  always on, per mode or per call: *"a always"*. Upstream's default is `false`,
  and leaving it there was a **live regression against all four pre-BrowserAI
  ways of running this child**: `checkUrlAllowed` refuses the `file:` protocol
  outright, so `browser_navigate` cannot open a local page at all, and
  `checkFile` refuses any path outside `<session>\output` and the child's
  working directory, so `browser_file_upload` cannot reach a file the caller
  already has.

  **Upstream calls it a convenience defence rather than a secure boundary**, in
  `config.d.ts`'s own words — *"a guardrail to prevent the LLM from accidentally
  wandering outside its intended workspace … not a secure boundary; a deliberate
  attempt to reach other directories can be easily worked around, so always rely
  on client-level permissions for true security"* — and BrowserAI's caller
  already holds file tools of its own. That is the same reasoning that removed
  the `(tool, mode)` permission matrix on 2026-08-18: what the guardrail
  withholds is reachable one tool call away, so all it can do here is refuse the
  caller a thing it is entitled to while proving nothing.
  `ConfigRoundTripTests.EveryGeneratedConfigLiftsUpstreamsWorkspaceGuardrail`
  checks the value for both families, every mode and the run's own browser-less
  child; the key is in `RequiredSessionOpinions`, so the round trip through
  `browser_get_config` fails if the child ever stops honouring it.

- 🔧 **Provisioning is stopped when it stops making progress, not when it has taken
  too long.** `ProvisioningTimers.AbsoluteCap`
  — 45 minutes on the whole install — is replaced by `StallCap`, ten minutes
  with **nothing written at all**. A total cap can only ever fire on a link that
  is slow and *working*: 203,824,344 B in 2,700 s is 0.60 Mbps, and a link that
  has died is caught by upstream's own 30-second socket timeout twenty times
  sooner. It punished the one case it could reach. The 60-minute crash tripwire
  went with it — with no total there is no number a tripwire could be "outside"
  — and the *waiting* process now runs the same stall detector against the same
  bytes, so a holder on a slow link is no longer reported as stuck at sixty
  minutes.

  **Progress is bytes on disk under the browsers root, and it is one number only
  because the download target moved under it.** Measured 2026-08-19, sampling
  every 250 ms across real installs: upstream downloads into
  `os.tmpdir()\playwright-download-XXXXXX\` and creates the revision directory
  only when it starts unzipping, so **nothing under the browsers root grows for
  the whole download** — a detector reading the root alone would kill every
  install on a slow link. BrowserAI now sets `TEMP` and `TMP` for the installer
  child to `<browsers root>\.downloads`. **Every one of the 41 chromium and 27
  firefox samples differed from the one before it**, in both phases.

  **Ten minutes is set by upstream's own lock rather than by taste.**
  `registry.install()` waits on `<root>\__dirlock` *before* it writes anything,
  and that wait was measured at **470 s** before upstream gives up by itself —
  so any cap at or under 7 m 50 s kills a healthy install that is correctly
  queueing behind another. It is deliberately ten times
  `UpdateService.StallBudget`, whose 60 s is right there and wrong here: a
  Velopack download has no directory lock in front of it.

  **Scanning `%TEMP%` instead was considered and is measurably wrong**: on this
  machine that scan found a `playwright-download-PRU23e` abandoned three days
  earlier holding 128,684 B of somebody else's archive, counted as our progress.

- 🔧 **The refusal a browser call meets while provisioning runs is a progress
  report.** Bytes
  written by this attempt against the measured download total, the percentage,
  elapsed, the observed rate, and what the remaining bytes come to at that rate
  — labelled as arithmetic rather than as a promise. The old sentence quoted the
  size and said *"wait about ten seconds"*, which read identically at 8 s in and
  at 25 minutes in, so a model had no way to tell a download that was working
  from one that was not and its only recourse was to keep calling.
  `@playwright/mcp` emits no progress notifications at all, so this refusal is
  the whole mechanism and no protocol work exists to do. `FirstRunDownloadSizes`
  is now derived from a byte count rather than hand-written beside one.

- 📝 **`QUESTIONS.md` had gone stale, and it is the document the maintainer reviews from.** `QUESTIONS.md`
  had gone stale, and it is the document the maintainer reviews from — so
  staleness there costs more than anywhere else. Two entries were found wrong
  **by accident**, which is the only reason the rest were read. Swept entry by
  entry on 2026-08-19: **sixteen checked — nine numbered, five lettered, and the
  block of settled bullets — and six were wrong.** Each corrected in place with
  a `previously` clause; nothing deleted for being merely settled.

  **Item 6** said the per-directory gate is **60 seconds**; it is **120**,
  raised on 2026-08-18 because the property that has to hold is the gate against
  the **sum** of the waits taken inside it, not the largest. **Item 7** said the
  deeper probe-before-gate fix *"was NOT taken"*; it was, the next day, and the
  entry's framing was wrong as well as its verdict — the probe already existed
  as a fast refusal in front of the gate, and the mechanism was not the TOCTOU
  window the entry predicted. A contender cannot **take** a directory inside the
  writer's rename-reopen gap, because taking one needs the gate the writer is
  holding; it can **look**, and `ProbeForHolder`'s `FileAccess.ReadWrite` handle
  is refused by a holder sharing only `Read`. Detecting an owner and blocking
  one are the same capability, so it is absorbed on the gated side instead.
  **Item 2** said *"there is no CI today"* — there had been for a day.
  **Judgement call A** said closing the `#anchor` gap was *"on the queue"* — it
  closed on 2026-08-18. **Judgement calls C and D** still described a
  known-intermittent suite and a contingency for missing 20 consecutive green;
  the suite reached 20 of 20 at `Unbounded` on 2026-08-18 and the contingency
  never fired.

- 🔧 **`browserai_destroy` returns `isError: true` when it could not remove everything.** `browserai_destroy`
  now returns `isError: true` when it could not remove everything, and the error
  carries the whole report. Previously both arms returned `isError: false`: a
  call that removed a nine-thousand-file profile and could not remove eleven
  locked files looked, to a model scanning result shapes, exactly like one that
  removed the lot. **The maintainer's call, taken over the recommendation to
  leave it and over the stated objection that an error invites a retry** — and
  the refinement that answers the objection is in the text. After the tally and
  the listing, the arm now says the session **is** destroyed (its record gone,
  the index having forgotten it, what is listed being residue on disk), says
  **not** to call `browserai_destroy` again because there is no session left for
  it to destroy and it will refuse, and says what to do instead: wait for
  whatever holds those files to exit and delete them, or leave them. The
  summary, the count, the listing and the truncation notice are unchanged, and
  the roll-up warning arm is untouched. `QUESTIONS.md` §11 carries the decision,
  the objection and how to reverse it.

  **Every destroy assertion in the suite was re-read rather than only the ones
  that went red**, which is what found the one that could only fail on a slow
  machine: `FirefoxSessionTests` still carried a bare `isError` assertion beside
  the contract check, and against Firefox on a four-core runner that is an
  assertion about how fast a browser lets go of its profile. The flag is now
  part of `DestroyAnswer.AccountsForWhatItLeftAsync`, asserted **in both
  directions** — a survivor arm reporting success and a clean destroy reporting
  failure are equally red — so no test holds destroy to a promise of its own.

- ✅ **CI declares which capabilities it expects to be absent, and an undeclared
  absence is a red build.** The
  capability gate made a degraded run *loud*; it never made one *noticed*. CI
  has run with `packed release` and `client CLI` ABSENT since the day it
  existed, so a provisioning step that started failing soft would have produced
  a green run, one more `ABSENT` line in a block nobody diffs, and a set of
  tests that skipped instead of running — the founding failure shape of this
  project, one layer above the gate written to remove it.
  `BROWSERAI_EXPECTED_ABSENT` on the workflow's test step is the declaration;
  `SuiteCoverageTests.EveryAbsentCapabilityIsOneThisRunsEnvironmentDeclared`
  fails on an absence it does not name **and** on a name in it that is
  `PRESENT`, because a declaration wider than the truth is standing permission
  for that capability to disappear later. An unset variable declares nothing, so
  a developer machine behaves exactly as before and a clean clone still runs.
  `.TheWorkflowStillDeclaresWhatItExpectsToBeAbsent` reads `build.yml` itself,
  scoped to the step that runs the suite, so deleting the line is a red build
  rather than a silent switch-off — and
  `.TheExpectedAbsentDeclarationIsReconciledAgainstWhatIsAbsent` exercises every
  branch in-process, so the mechanism does not first run on the build that needs
  it. Five faults were planted and all five went red: an undeclared absence, an
  over-broad declaration, a typo in the declaration, the declaration deleted
  from the workflow, and a typo committed to the workflow.

- 📝 **A recorded hazard was measured and turned out not to be one.** A recorded
  hazard was measured and turned out not to be one: two family installers cannot
  extract into one shared component directory, because upstream serialises every
  install on a lock BrowserAI never knew about. `ReinstallSharedAsync`'s remarks
  called the race *"reachable in the shipped product"*. The **concurrency** is
  reachable — `BrowserProvisioner.MutexNameFor` hashes the browsers root **and
  the family**, so a chromium install and a firefox install run at once by
  design, and both lay down `ffmpeg` and `winldd` in the one root. The **race**
  is not: `registry.install()` takes a `proper-lockfile` directory lock at
  `<PLAYWRIGHT_BROWSERS_PATH>\__dirlock` before it touches any executable and
  holds it for the whole install.

  **Measured five ways on 2026-08-19** rather than reasoned from the source:
  chromium and firefox started **8 ms apart** into an empty root finished with
  four trees, every one carrying `INSTALLATION_COMPLETE` and no lock left
  behind; three concurrent `install-browser ffmpeg` runs over three rounds came
  back 3/3 green and byte-identical; a held lock stopped an installer **dead for
  30 s** — no directory, no download, no output — and it completed 8 s after the
  release; an abandoned lock, which is what a killed installer leaves, was
  reclaimed as stale at no measurable cost.

  **What is real is a wait, and it is now written down as one.** The retry
  budget is **470 s**, after which upstream fails the install outright with its
  own `ELOCKED` box, having written nothing. 203.8 MB in 470 s is 3.5 Mbps, so
  on a slower link a firefox install started beside a chromium one **fails
  rather than queues** — loudly, recoverably, and inside the 45-minute
  `AbsoluteCap`, because the wait happens before the browser's directory appears
  and `ExtractionCap` has not started. The remarks are corrected in place with
  what they previously said, the hazard index carries the row, and
  `PayloadTests.UpstreamStillSerialisesEveryInstallOnOneLockOverTheWholeBrowsersRoot`
  reads the four anchors out of the assembled bundle and asserts their order —
  so a `playwright-core` bump that drops the lock or moves it inside the
  per-executable loop is a red build rather than a rediscovery.

  **The three mutexes that method holds are still load-bearing**, for a reason
  upstream's lock cannot cover: it serialises *installs* against each other and
  knows nothing about the recursive **delete** performed first, which takes no
  `__dirlock` and never could.

- 📝 **Every row of the [hazard index](HAZARDS.md) is now adjudicated.** Every
  row of the [hazard index](HAZARDS.md) is now adjudicated, and the count of the
  ones that are not is asserted on every build at zero. 55 rows read `open` with
  `—` for evidence — rows nobody had decided either way, carried since before
  `v1.0.0`. Adjudicating is not closing: **29 gained a named mechanism and
  closed, 26 gained a stated reason and stayed open**, and eight of those
  twenty-six say in as many words that they can never close, because what they
  describe is an upstream behaviour, a platform property or a deliberate trade.
  Nothing was closed that could not name what goes red if the hazard returns.

  **The tally moved from [`TODO.md`](TODO.md) into the index itself**, because
  the item that carried it was work-not-yet-done and the work is done — and
  because **at zero the sentence is a stronger mechanism than the backlog it
  replaced**. Counting down it said *somebody should decide these*; at zero it
  says **a row that arrives `open` with `—` fails the build**, so a hazard has
  to be adjudicated when it is written down rather than accumulated for a later
  pass. `RecordedCountTests.TheHazardTallyIsWhatTheIndexHolds` reads the
  sentence as its anchor and re-counts through the same `HazardIndex` parser
  `HazardIndexTests` uses.

  ⚠️ **Two of the findings are worth more than the rows they came from.** The
  `ContentBlock` converter *throwing* on an unknown content type had been closed
  by the product for three days and nobody had read the row: its two neighbours
  closed on 2026-08-16 on one sentence, and
  `LosslessPassthroughTests.AnUnknownContentTypeSurvivesTheTrip` is a test
  written for exactly this row. And *screenshots are not byte-stable* — the
  claim the whole canned-blob testing practice rests on — **has never been
  measured**, which is why that row stayed open.

  **`RecordedCountTests`' own non-vacuity guards had to be corrected in the same
  pass**, and it is the transferable half: `published.Count > 4` and
  `unadjudicated.Count > 20` were floors placed under the very number they were
  watching be counted *down*, so finishing a third category would have turned
  the test red **because the work got done** — and the obvious fix then looks
  like weakening an assertion. They are floors under the corpus now: the table
  still parses, every row lands in exactly one of the two states, and both
  states are populated. None of the three moves when a row is adjudicated.

- 💥 **`browserai_reinstall_browser` now takes one required argument, naming the
  family.** ⚠️
  *Previously it took none, "because there is nothing to name: the install is
  shared by every session on this machine."* **The stated reason expired rather
  than being overruled** — with two families provisioned there are two trees,
  two revisions and two mutexes, and the caller's broken browser is exactly one
  of them.

  Two alternatives were weighed and rejected in writing. **Reinstalling both**
  keeps the no-arguments property and makes the blast radius worse in the one
  situation the tool exists for: a caller with a broken Firefox pays 331 MB,
  loses a working Chromium for the length of its own re-download, and a network
  failure part-way ends the call with two broken browsers instead of one. A
  repair tool must not be able to break something that was working. **Defaulting
  to Chromium** is worse still — a broken Firefox, a healthy Chromium deleted
  and fetched again, and an answer reporting a successful reinstall. Required
  rather than optional-with-no-default follows `mode` on `init`, this product's
  settled shape for an argument whose omission cannot be answered honestly.

  **The refusal narrowed with it.** The running-process check was always
  per-directory, so naming the family makes it per-family in effect: an open
  Chromium session no longer blocks a Firefox reinstall, and the sessions listed
  in the refusal are now only those of the family being reinstalled — listing a
  live Chromium beside a blocked Firefox reinstall told the caller to close the
  wrong browser. Still no force flag; still no session argument.

  `ffmpeg` and `winldd` are shared by both families, each carries its own
  completion marker, and neither is touched by either family's reinstall — so a
  corrupt `ffmpeg` is not repairable through this tool. Recorded as a limitation
  rather than left to be discovered as one.

- 🔒 **A session directory on a network path is refused, and a mapped drive letter
  counts as one.** One
  `File.Exists` against a share that has stopped answering costs a measured
  **22,210 ms**, and several such calls happen inside the per-directory gate —
  so the caller who names the dead share is not the one who waits; every other
  process contending for that directory is. `browserai_init` and
  `browserai_resume` now refuse before anything is created and before the gate
  is taken.

  ⚠️ **It refuses network *semantics*, not the `\host\share` spelling, and that
  distinction is the whole point.** A `net use Z:` mapping is a rooted local
  drive-letter path by every character in it, resolves through the same
  redirector and costs the same twenty-two seconds — measured 2026-08-19 through
  a real redirector alias, which is the first time this repository has had that
  number for a drive letter rather than for a UNC path
  ([kb](kb/windows/detection.md#a-mapped-drive-letter-is-a-network-path-and-costs-the-same-22-seconds)).
  A check on the string shape looks closed and leaves the hole open.

  **The guard cannot pay the cost it prevents.** The network question is
  answered by characters and then by `GetDriveTypeW`, both of which read the
  object manager rather than the filesystem — `GetDriveTypeW` answered
  `DRIVE_REMOTE` in 0.9 ms against a letter whose `File.Exists` had just taken
  22 s. That corrects a sentence this repository had carried since the log
  writer was built: *"telling the difference needs GetDriveType — a filesystem
  call, which on a disconnected mapping can block for exactly as long as the
  thing being avoided."* It was reasoning rather than a measurement, and it was
  wrong.

  `browserai_destroy` and `browserai_list` are deliberately **not** guarded, so
  a session created on a share by an older build can still be seen and still be
  removed.

- 🔒 **A second spelling of one session directory is refused, with the spelling to
  use instead.** `Path.GetFullPath`
  resolves neither `\?\`, junctions, `subst` nor mapped drives, so two spellings
  of one directory produced **two mutex names and one `lock.json`** — the
  per-directory gate stopped serialising while every signal still read healthy,
  which [the adversarial review](docs/reviews/2026-08-18-adversarial-locking.md)
  traced to two processes driving one browser profile in one interleaving and a
  destroyed session history in the other.

  Refusal rather than canonicalisation, taken as a decision: canonicalising
  through the filesystem's own final name is correct and **rewrites the identity
  of every mutex name, index key and lock path in the product**, giving every
  session directory in existence a new identity on the day it ships. The refusal
  names the accepted form, so the next call is the same call with one argument
  replaced. Reasoning, and what it knowingly leaves open, in
  [`DECISIONS.md`](DECISIONS.md#refusing-network-paths-and-aliased-spellings-at-the-door).

  ⚠️ **And 8.3 generation turned out to be per-volume, which CI found rather
  than a document.** The developer machine shortens on its system volume and not
  on its other three; the GitHub Windows runner does not shorten on the volume
  it checks out onto, so the test that builds an 8.3 alias had nothing to build
  and its own positive control caught it. **It is not skipped there**: a volume
  with no short names is a volume on which the hazard does not exist, and the
  test asserts that instead, plus the backstop that would catch a short spelling
  if one ever arrived unexpanded. Which branch ran is printed, and the
  [re-verification row](kb/re-verification.md) says plainly that a green CI run
  does not re-verify the .NET behaviour.

  **Both tools now say so to the model, and one sentence had to be corrected
  rather than extended.** `browserai_init`'s description said *"Any path is
  accepted and none is validated"* and `browserai_resume`'s said *"This never
  refuses a directory for what it is"* — neither is true any more. The fact the
  first sentence was carrying is untouched and is still stated: nothing about
  what the directory **contains** is looked at, so pointing a session at a real
  Chrome profile still works and still does everything the surrounding warning
  describes. `ModelSurfaceTests` requires the fact rather than the sentence,
  which is what let the phrase move with it.

  ⚠️ **One of the review's four aliases turned out not to be one.**
  `Path.GetFullPath` **does** expand 8.3 short names on .NET 10 — in full for an
  existing path, and prefix-only-with-the-tail-preserved for one `init` has not
  created yet — so a short spelling arrives already canonical and there is
  nothing to refuse. Measured rather than assumed, and the review is corrected
  in place; its conclusion is untouched, because it needed one unresolved alias
  and has three.

- 📝 **The charter said there is no CI, and there has been since 2026-08-18.** `DECISIONS.md`'s
  *Automated checks* row read *"None. No CI, no scheduled job, no git hook"*
  while `build.yml` was running the whole suite on every push. The fact is
  corrected and the decision is not: what the row argued is now true of the
  **release checklist** alone, and what is left open is narrower — whether the
  release-phase checks CI deliberately does not run (the packed release, the
  real client, `BROWSERAI_RELEASE_RUN=1`) should move into automation too. The
  `TODO.md` item that was waiting on *"a real cadence rather than a guess"* says
  so as well; half its premise had expired without it noticing.

- 📝 **The browser-reinstall row rests on a measurement of Chromium.** The
  browser-reinstall row rests on a measurement of Chromium rather than on a
  retracted sentence about Windows. The row had closed *download alongside and
  swap* on *"Windows will not rename a directory holding open executables"*;
  that was measured false on 2026-08-18 and retracted, leaving the refusal
  standing on the admission that **nothing had measured what Chromium then
  does**. It has now. Against a live headless Chromium 152.0.7977.8 running as
  ten processes with its own working directory deliberately elsewhere,
  `Directory.Move` of `chrome-win64` was refused with a **sharing violation**
  and of `chromium-1237` with **`ERROR_ACCESS_DENIED`** — and **both succeeded
  in the same script seconds later with the browser killed first**, which is the
  control that makes the two refusals Chromium's rather than the tree's. So the
  2026-08-18 retraction was right that Windows has no such general rule and
  **too broad in the other direction**. Nothing is relaxed; the position is now
  evidence. ([kb](kb/windows/processes.md#the-win32-interop-surface),
  re-verification row 103.) The mechanism is `[UNVERIFIED]` — the two refusals
  carry different Win32 errors, so they are not the same cause — and Firefox was
  not tested.

- 📝 **`@playwright/mcp` emits no progress notifications at all.** `@playwright/mcp`
  emits no progress notifications at all, which settles the first of the two
  things the relayed-ordering decision was waiting on. All four occurrences of
  `notifications/progress` in the shipped payload are the MCP SDK's own schema
  and capability arms; `sendNotification` appears once, as the capability handed
  *to* a tool handler, and nothing in `@playwright/mcp` or `playwright-core`'s
  MCP layer calls it. Measured with a positive control, so a zero is an absence
  rather than a failed search. **The SDK's fire-and-forget reordering is
  therefore real and unreachable through this product's own child**, and the
  transport decorator that would fix it would be a component built for a
  notification nobody sends. Re-verification row 104 is what re-opens it.

- 📝 **`LongPathsEnabled` is recorded where the long-path guarantee is claimed.** `longPathAware`
  in the manifest is necessary and not sufficient — Win32 honours it only when
  the machine-wide registry value is also `1`, and a default install leaves it
  `0` — so every long-path measurement in `kb/` was conditional on a value
  nobody had written down. Read off the reference machine as `1` (`REG_DWORD`)
  on Windows 10.0.26200 and stamped `[MACHINE]`, with the half that is still
  unknown named rather than implied: nothing has run against `LongPathsEnabled =
  0`, and the product neither checks it nor emits a diagnostic that would name
  it.

- ♻️ **`CreateProcessW`'s two buffers are declared as spans rather than as one
  `char`.** `ref
  char lpCommandLine`, called as `ref commandLine[0]`, was weaker than
  Microsoft's own Win32 metadata for the same call in three ways at once: no
  length, an empty buffer that became an `IndexOutOfRangeException` at the
  indexer rather than the `null` the API accepts, and nothing saying which of
  the two buffers Windows writes back into. It is now `Span<char>` for the
  command line and `ReadOnlySpan<char>` for the environment — pinned, not
  copied, so the mutation still lands in our array. **Nothing was known to be
  wrong with the old shape and nothing changed at the call site**; it was a
  signature that presents as a plausible wrong answer rather than as an error.
  The invariants it silently relied on are now asserted:
  `InteropLayoutTests.TheTwoBuffersHandedToCreateProcessAreTerminatedAndNeverEmpty`
  is red if either buffer stops being NUL-terminated or becomes empty — and the
  empty environment block is the one that matters, because it would reach
  Windows as `null` and mean *inherit the parent's environment*, silently.

- 🔧 **`.gitignore`: the three owed items, and one of them was a claim that was not
  true.** The
  upstream `VisualStudio.gitignore` half was re-fetched and compared against
  upstream HEAD (blob `d5a18de`, unmoved since 2026-04-17) — it has not changed.
  But the marker comment invited a **wholesale paste**, and this half is *not*
  verbatim: `artifacts/` and `.artifacts/` are root-anchored here, because
  unanchored they matched `src/BrowserAI/Artifacts/` on case-insensitive Windows
  and made five product source files invisible to git. The refresh procedure now
  says paste, re-apply, run the suite —
  `BuildConfigurationTests.NoSourceFileIsInvisibleToGit` makes forgetting a red
  build, and the comment stops the next person rediscovering why.
  `.vscode/mcp.json` is re-admitted below the marker, since
  [github/gitignore#4735](https://github.com/github/gitignore/pull/4735) is
  still open; for a project that **is** an MCP server, a workspace registration
  used for testing was silently untracked. `/staging/` was already settled and
  deleted on 2026-08-16, with the reason recorded in the file — the `TODO.md`
  item was stale.

- 💥 **`lock.json` is schema 2: every field is an ordered list of statements.** `lock.json`
  is schema 2: every field is an ordered list of timestamped statements, and
  `acknowledgeCopy` is gone. The record was a snapshot with a history bolted
  onto `purpose` alone; it is now append-only, so a session says **how it got
  here** and not only where it is. `created` and `lastUsed` are no longer stored
  — they are exactly the earliest and the latest statement, and a stored copy
  could only ever disagree with what it summarises. **A statement is appended
  only when the value changes**, so `mode`, `browser`, `directory` and
  `browserAiVersion` stay one statement long for a session that is not moved,
  copied or run under a new build. **Growth is capped at 32 statements per
  field**, trimmed out of the *middle*: the first statement is never dropped,
  because `created` is read from it and a trim at the front would silently move
  a session's creation date. Worst case is ~70 KB, dominated by 32 purposes at
  their 2,000-character cap — schema 1's `purposeHistory` had no cap at all.
  **There is no converter and no back-compatibility**: a version-1 file is
  refused with the fix in the message, and the version is now checked in a pass
  of its own *before* anything else is parsed, because a schema-1 file is
  well-formed JSON whose keys this build still recognises by name and a version
  checked last reported it as damage. **Every refusal the strict parser made
  still holds** — unknown key at any of the three levels, missing key, unknown
  schema version, non-round-trippable timestamp — and one is new: an **empty
  statement list**, which has no current value and would otherwise surface as an
  index-out-of-range a long way from the file.

  **The payoff is that `browserai_resume` stops refusing a copied directory.**
  `acknowledgeCopy=true` existed because taking a copy over overwrote the only
  evidence that it *was* a copy, so the caller had to be made to say it knew.
  Now the resume appends its path to a `directory` history that still carries
  the original and **returns that history**: where the directory has been, when,
  and that the recorded purpose describes the original's work. That is strictly
  more than the refusal ever conveyed. The `DirectoryIsACopy` row is deleted
  from the error catalogue rather than orphaned, and **BrowserAI now has zero
  confirmation flags** —
  `ModelSurfaceTests.NoAuthoredToolAsksTheCallerToConfirmAnything` keeps it
  there, with the deleted flag's own name as the matcher's positive control.

- 🔧 **A contender asks the kernel who holds a session before it queues for the
  right to ask.** Every
  process that wanted to know who held a directory took
  `LockScopes.PerDirectoryGate` — losers included, whose entire remaining
  business is to print *held by PID n, since t, for this purpose*. So a refusal
  waited behind the whole queue of peers rather than behind one critical
  section, and the cost is super-linear in contenders.
  `SessionLock.ProbeForHolder` opens `lock.json` **in front of** the gate; the
  sharing violation is the kernel's answer and no mutex made it more true.
  Measured before and after against a directory a live holder already had, 3
  runs at each N on an idle machine: slowest refusal **329 → 30 ms** at 16
  contenders, **2,084 → 203 ms** at the charter's design point of 100, **4,267 →
  449 ms** at 200 — and the shape changed from `p50 ≈ max/2`, which is a queue
  draining one entrant at a time, to a cluster. **The free path is unchanged and
  that is the design, not caution**: a probe is a sound *ownership* test and an
  unsound *freedom* test, so anything that is not a sharing violation — absent,
  mid-rename, `UnauthorizedAccess`, unparseable, or an open that succeeded —
  falls through to the untouched `MachineMutex.Create` → `Acquire` →
  `TakeOrReport`. With the gate skipped there instead, both contenders probe
  free, the loser's rename retry loop becomes the serialiser, and it takes the
  name off a live holder's still-open handle; [the adversarial
  review](docs/reviews/2026-08-18-adversarial-locking.md) found that before it
  was built, and
  `SessionLockTests.AProbeThatFindsTheDirectoryFreeStillProvesItAtTheGate` is
  what stops it being built later. **The cold race is deliberately not
  improved** — with the directory empty at `t=0` every contender correctly
  probes *looks free* and every one falls through, so 100 contenders racing an
  unheld directory measure the same as before. The queue that goes away is the
  one that forms around a session somebody already has, which is every moment of
  a session's life after the first.

- 🔧 **`browserProvisioning` now answers `provisioning` where it answered
  `downloading`.** One
  word covers five phases — waiting on another process's provisioning mutex,
  deleting an abandoned tree, downloading, extracting, and pruning superseded
  revisions — and **only one of them is a download**. The entry below fixed the
  *sentence* on 2026-08-18 and left the word, recording it in
  [`QUESTIONS.md`](QUESTIONS.md) §9 as the maintainer's call; the call is taken,
  and §9 is answered. Nothing about the bucketing moved: every consumer still
  branches on *installed* / *not yet* / *failed*, and the mutex-loser still
  belongs in the middle — which is why **no fourth word was added** for it,
  since no caller acts differently on the distinction.
  `ProvisioningState.Downloading` is `ProvisioningState.Provisioning`. **Both
  unfinished detail sentences gained an explicit *"wait and call the same tool
  again on the same session"*,** because `downloading` implied that recovery by
  itself and `provisioning` does not — a rename that leaves the reader with a
  state and no action is a rename that made things worse. No external consumer
  parses the word; this build has never shipped one.

- 📝 **`CLAUDE.md` is 50 lines instead of 89, and every rule names its mechanism.** It
  is the first thing an agent reads and roughly half its rules had no mechanism
  at all — indistinguishable, on the page, from the half that did. *Prefer a
  mechanism over a habit* was the best line in it and was buried in the last
  third; it is now the frame, and the rules sit in two lists that say which kind
  they are. The six-line `[STALE]` defence went (it is defined properly in
  `kb/README.md`'s conventions table) and the daily drift check went from 21
  lines to four plus a pointer, because its resolution table and its Dependabot
  reasoning already exist verbatim inside `drift-check.json`.

- 📝 **The charter is split in two, and `README.md` is a README again.** It was
  84 KB and opened with a table of settled decisions, so a first-time visitor
  scrolled past four years of argument to find out how to install anything. What
  it is, what it does, how to install it, how to use it, the scope boundary and
  the licence stay; every settled decision with an argument attached moves to a
  new [`DECISIONS.md`](DECISIONS.md), whose four date-titled tables are now
  titled by topic. No reasoning was dropped in the move and every inbound link
  was repointed.

- 📝 **The hazard index says what each step was rather than where it stood.** `HAZARDS.md`'s
  evidence cells carried 55 references to a build order that was deleted with
  the rest of the plan; each is now that step's own title, recovered from git
  history. No hazard was re-adjudicated.

- 🔧 **The stray-browser sweep now has one trigger instead of two.** The logon
  scheduled task is dropped: it cannot be registered without elevation on a
  UAC-filtered administrator token, and a per-user install has no elevation to
  offer. BrowserAI's own startup sweep already covers the case that matters — a
  stray browser matters when something is about to contend for its profile lock,
  and that is exactly when a client starts.

- ♻️ **One place answers "what version is this binary".** Two implementations
  disagreed — one read the informational version, the other the assembly version
  — and the version now has a single source that reads the informational version
  and never the assembly version.

- 📦 **The SDK is forbidden from decorating the version string.** The SDK is
  forbidden from decorating the version string, repository-wide. Left on, it
  appends the 40-character commit sha to a version that has already been
  published, which is invisible until an update path that *matches* versions
  rather than comparing them starts downloading the binary it is already
  running, on a loop, forever.

- 📦 **The RID section a restore writes into `BrowserAI.Core`'s lock file is
  committed, not reverted.** Q199, decided by the maintainer on 2026-09-16, the
  third time the same diff had been met in one night. The section is the empty
  `"net10.0-windows7.0/win-x64": {}` that an `-r win-x64` restore adds. Every
  publish of either executable is RID-specific, and a RID-specific restore
  records that section for every project it reaches —
  [`src/BrowserAI/packages.lock.json`](src/BrowserAI/packages.lock.json) and
  `BrowserAI.App` have carried theirs since they were written, and
  `BrowserAI.Core` is the library both of them publish, so the section is **what
  the restore genuinely resolves** rather than an artifact of one publish shape.
  The rule it replaces was *revert it, never commit it*, kept by a person
  remembering, and it had already failed once: the section reached `HEAD` in a
  `git add -A` and was reverted in `ac244ff` under a sentence calling it an
  artifact nobody asked for. **The file has two states and the last restore
  wins** — measured 2026-09-16 *after* the decision: a RID restore writes the
  section (`fab160c4…`), a non-RID solution restore removes it (`7f30ec57…`),
  which is what `dotnet test` performs, and each state is byte-stable under
  repetition of its own kind. So committing it moves which end of the oscillation
  is the dirty one rather than ending it, and **that is still the right way
  round**: the diff that matters is the one a commit follows, a publish is
  followed by a release commit and a suite run is followed by reading a log. The
  cost is named rather than hidden — `git status` shows the file modified after
  every suite run, including all six of a release gate, and the release
  checklist's own re-pack restores with the RID and leaves the tree clean before
  the release commit is written. The way that would end the oscillation outright,
  declaring the RID on `BrowserAI.Core` so every restore resolves the same set,
  is written down in [`TESTING.md`](TESTING.md) and belongs to whoever owns the
  build.
  [`RELEASING.md`](RELEASING.md) item 7 and [`TESTING.md`](TESTING.md) are
  corrected by addition, each quoting in full what it said before.

- 📝 **First-run provisioning is measured again at the new browser revisions.**
  Re-verification row 21 went stale one day after it was taken, when the
  `playwright-core` pull-forward moved chromium 1244 to 1245 and firefox 1544 to
  1548. Both families were provisioned twice into an empty root through the same
  rig, and each pair came back identical to the other. Chromium did not move at
  all, which is worth the sentence: its archive is keyed on the browser version
  rather than on the revision, and 1245 carries the same 154.0.8037.0 as 1244,
  so the same archive is fetched and the tree is the same tree to the byte and
  the file. Firefox moved by 327 bytes on the wire and 902 on disk across the
  same 61 files, so the quoted download size for that family goes from
  129,502,321 to 129,502,648 bytes and the Chromium one is re-measured and
  unchanged at 207,274,189. `ffmpeg` 1011 and `winldd` 1007 are byte-identical,
  which is the control for the two revisions that moved. The timings moved in
  both directions on a roll that moved almost no bytes, so they are the link and
  the machine on the day rather than anything about a revision. The run is kept
  as evidence this time; the previous one was scratch and went with it. Two of
  the three sections that went stale that day are still owed: the Firefox
  against Chromium cost ratios and the resume cost, rows 34 and 38.

- ✅ **The one suite arm that launches an unowned browser runs beside nothing.**
  Q212 = a. The wild exit 1 this repository has chased since 2026-08-26 was
  named on 2026-09-17 and it was this product's own stray sweep: a second
  `BrowserAI.Server.exe`, started by a different arm of the same run, sweeping
  at startup, finding a browser it could not attribute because no message window
  had been published yet, falling back to the session directory the command line
  names, finding it unlocked and terminating it with exit code 1. The rig's
  scratch session directory holds no lock by construction, so any product server
  starting while that browser is alive kills it, which makes it deterministic
  rather than rare. The arm carries a keyless `[NotInParallel]` now: the key it
  already had holds the arms that run a sweep apart from each other, and the
  arms that matter are the dozens that start a server, none of which carries
  that key. What it costs is measured rather than estimated, three runs each:
  the arm alone is 1.528, 1.513 and 1.476 seconds of total run time against a
  0.747, 0.732 and 0.763 second zero-test baseline through the same invocation,
  so about three quarters of a second of critical path. Nothing mechanises the
  rule and the reason is written down where a reader will meet it: the predicate
  that matters is that the browser's profile sits in a directory nothing holds a
  lock on, which is a property of the running rig rather than of its text, and
  the readable approximation of it would also fire on an arm that was
  deliberately taken out of a serialisation key on a measured argument. The
  product is unchanged.

- 🐛 **A browser call into a session whose server has gone comes back now.**
  Q211 = c. Measured on 2026-09-17:
  with a session's node child killed under a live BrowserAI, one
  `browser_navigate` was still outstanding after 900,000 ms, the server alive
  and nothing in any log after the transport's own end of stream. The cause is
  read from the MCP SDK's shipped code rather than guessed: it faults every
  pending request when the transport's channel completes, once, and a request
  registered after that moment is faulted by nothing, so it waits on the
  caller's token and on nothing else. BrowserAI asks whether the child is still
  there before it forwards, and refuses with a sentence that says the browser
  server for this session has ended, that nothing was forwarded and nothing in
  the browser changed, and that `browserai_resume` starts a replacement. No
  timeout was added anywhere: the wait was not slow, it was endless. A call
  already in flight when the child dies is a different path and has always come
  back as an error; that is unchanged. The refusal is recorded on the session
  like every other refused call. The arm was planted red and hit the suite's
  own five minute hang detector before the check went in, and answers in two
  seconds with it; two controls stand beside it, a resume of a healthy session
  and a call held open on a healthy child, because a liveness question answered
  too readily would turn every slow page action into a refusal.

- 🔧 **`browserai_resume` repairs a session whose browser server has died.**
  Q211 = a. Until now it asked one question, *do I already own this directory*,
  and the answer is still yes when the `node` child behind the session has been
  killed: it answered "This session is already open in this BrowserAI; nothing
  was changed" in 7.68 ms about a session that could no longer do anything at
  all. It now asks the second question too, *is the child behind it still
  there*, and starts a replacement when it is not, with the same launch options
  the session was opened with. Liveness is read from the transport's own closed
  state and from the child's process handle, never from a pid lookup by name, so
  a child on its way out counts as alive until one of those two says otherwise
  and a resume of a healthy session still changes nothing. The answer says what
  the replacement did not bring back: the profile is on disk so cookies and
  stored state survive, and no page is open, so navigate again before acting on
  what you see. Nothing about the session's identity moves, because the lock is
  still held and the record is untouched. A replacement that will not start is
  its own refusal, and it says the session is still open rather than inviting a
  fresh init.

- ⬆️ **`playwright-core` is pulled one build ahead of the wrapper that pins it,
  as a dated exception.** `@playwright/mcp` `latest` is
  0.0.81 and pins `playwright-core` 1.64.0-alpha-2026-09-14 exactly. The first
  build carrying `--file-paths=absolute` is 1.64.0-alpha-2026-09-17, so the
  payload manifest now carries an npm `overrides` entry that resolves that one
  instead. This collides head on with the rule that everything floats and
  nothing is ever pinned to work around a break, so it is recorded as an
  exception rather than absorbed. It is the maintainer's decision, and it is a
  different kind of thing from the one exception that already existed: the
  vendored SQLite pin holds a version still because nothing floats it, and this
  moves one forward because a wrapper is one release behind. Both now sit side
  by side in `DECISIONS.md`, which says in its own words that neither is a
  precedent for a third. The override brought two browser revisions with it,
  Chromium 1244 to 1245 and Firefox 1544 to 1548, so a first run downloads a new
  Chromium once.

- 📝 **The resume wedge is measured, and nothing in the product bounds it.**
  Q207 = b. Killing a session's `node` child under a **live** BrowserAI was
  recorded on 2026-09-16 and not diagnosed: `browserai_resume` answered the
  no-op in 7.8 ms and the next browser call had not returned after 3 min 8 s,
  when the probe was stopped. Re-run on 2026-09-17 with a clock on it, bounded
  at **fifteen minutes** because that is the largest timeout in the product plus
  five minutes of margin rather than a number a probe felt like waiting: **the
  call never returned**. The 2026-09-16 readings are corroborated to the
  millisecond, the no-op at **7.68 ms** and the refusal wording byte-for-byte.
  **Which product timer governs it: none.** `ChildConnection.AskAsync` awaits
  `SendRequestAsync` under the caller's token and nothing else;
  `ChildInitializationHang` is ten minutes and governs `initialize` only, and
  was crossed with no effect; `BrowserIdleTimer.DefaultIdlePeriod` has no
  browser left to close; `LockScopes.PerDirectoryGate` is released before the
  call is forwarded. **The process log carries one line and then fifteen minutes
  of silence** — *"playwright-mcp[surface]: the peer closed its end of the
  connection"*, 347 ms before the kill even reported complete — so the transport
  knows the peer is gone and the pending request is never told. **A second
  client cannot recover it either**, which is the half the first run left open:
  its `browserai_resume` is refused in 15.3 ms by the ordinary in-use refusal
  naming the wedged pid. **No product change is taken**; four directions are in
  [`QUESTIONS.md`](QUESTIONS.md) and the decision is the maintainer's. The
  transcript is persisted this time, at
  [`docs/evidence/2026-09-17-resume-wedge`](docs/evidence/2026-09-17-resume-wedge/README.md),
  and the rig that produced it is `wedge-probe.js`, beside the probe it extends.

- 📝 **The wild exit 1 has a name at last, and it is this product's own stray sweep.**
  The signature has been chased since 2026-08-26: exit code `1`, nothing on
  either stream, both pipes at EOF, five lines in the browser's own
  `--log-file`, no message window. It was blamed on desktop-heap exhaustion
  until `DesktopHeapProbe` fired on the failure path on 2026-09-17 and said the
  heap had room, and before that on the test harness's own spawn-record reclaim,
  which reproduced it 18 of 18 on 2026-08-29. **It was neither.** Read out of the
  machine-wide process log rather than reasoned about:
  `BrowserAI.Sweep[5]`, `2026-09-17T12:02:54.5368021Z`, *"Terminated a stray
  browser: pid=90216 ... Its session directory was unlocked, so nothing owned
  it"* — written by a second **product** `BrowserAI.Server.exe` that a different
  arm of the same run had started 173 ms earlier, sweeping at startup 84 ms
  after the dead browser's last log line. `StrayCandidate.TryTerminate` calls
  `TerminateProcess(handle, 1)`, which is the `1`. **The harness reclaim is
  excluded by its own announcements rather than by argument** — the 2026-08-29
  fix announced exactly three terminations that run, ten seconds earlier, naming
  three other pids — which is that fix working in the direction nobody designed
  it for: it was built to stop the harness killing a live run and what it did was
  prove the harness innocent. **The 2026-08-29 exclusion of the product sweep was
  backwards**: it ruled the sweep out because *attribution needs the window this
  browser never published*, and the missing window is exactly why the sweep fell
  back to the session directory and found it unlocked. The arm's rig holds no
  `browserai.lock` by construction, so **any** product server starting while that
  browser is alive kills it: deterministic rather than rare, a suite isolation
  defect rather than a wild death, and posed as a question with three directions
  and a plantable red rather than fixed here. The suite's own spawn record could
  not be consulted — it lives under `.work\`, which is cleared at the end of every
  batch — and the machine-wide log is what survived.

- 📝 **The 1.0.0 release note opens in plain words now.**
  The preamble is what a reader of the release page meets first, and the one
  that shipped read like something generated: an em-dash aside dropped into the
  first sentence, *"brings its own copy"*, *"The release holds two
  executables"*, and four claims in one bolded opening line. It is rewritten in
  short sentences: what BrowserAI is, that this is the first version fit for
  real use and what it replaces, the two programs in the release, and where to
  start. No em dashes, and ASCII throughout.
  [`build/New-ReleaseNotes.ps1`](build/New-ReleaseNotes.ps1)'s own fixed text
  was read with the same eye: the footer now says *"The full changelog for this
  release"* rather than *"Every entry in full, with its evidence"*, which is a
  sentence nobody says out loud. **The `1.0.0` seal is re-taken** at 310,216
  characters, and `AppendOnlyRecordTests` carries the previous values and the
  order that lifted it, because a re-seal nobody explains is rewriting history
  with an extra step.

- 🔧 **The icon legend is a compact table now, in the release body and in this file.**
  The maintainer's words: *"The legend at the bottom of the release notes that
  explains the icons is missing newlines. Give it a nice yet compact layout."*
  The release body carries this file's own legend rather than one of its own,
  and until today that legend was a single paragraph of twelve entries separated
  by an interpunct, which
  [`build/New-ReleaseNotes.ps1`](build/New-ReleaseNotes.ps1) then flattened
  further by joining its wrapped lines with spaces. A reader of the release page
  met one unbroken line. It is a Markdown table now: **two icon-and-meaning
  pairs per row, six rows for the twelve icons, under a one-word heading row**,
  in both places, and the generator emits it line for line. **A legend that is
  not a table is refused rather than flattened**, which is the half that makes
  this a rule instead of a preference: the body has no legend of its own, so the
  read is the only place the shape can be held. Planted red on the fixture
  (*"Expected to contain `table`"*, against a generator that accepted the
  paragraph) and on the tree (*"Expected to be equal to ... but received
  `\"\"`"*). Rendered once through GitHub's own renderer: one `<table>`, six
  `<tbody>` rows, 24 `<td>` cells.
- 📝 **Two records catch up: where the probe rigs live, and what upstream did with the first ask.**
  [`docs/probes/`](docs/probes/README.md) keeps all fourteen rigs, decided by the
  architect on 2026-09-17 after the scan that used to flag seven of them was
  narrowed to read the filter rather than the API. The blind spot is **one file
  wide instead of seven rigs wide**, and `2026-09-14-firstrun/observe.ps1` is the
  one true positive: it watches for a console host appearing anywhere on the
  machine, which no pid or path form expresses, so re-spelling it would falsify
  the record of method rather than fix anything. [`CLAUDE.md`](CLAUDE.md) says so
  by addition with the open question it replaces quoted. And
  [`TODO.md`](TODO.md)'s ask #1 records that
  [`dgozman`'s request for a repro was finally answered](https://github.com/microsoft/playwright/issues/42497#issuecomment-5713988873)
  on 2026-09-17, and that the fix is being adopted by overriding `playwright-core`
  to the alpha that carries it rather than by waiting for `@playwright/mcp` to
  roll — a dated exception with a written exit. The row stays open, and what it
  waits for is the review rather than the roll.

- 📝 **Chromium stays the default browser, on a reason rather than on four numbers that moved.**
  The maintainer's ground, in his words: *"the reason for the default is that
  chrome is the most widely used"*. Recorded as a **decision, not a
  measurement**, which is the point of writing it down this way. What it
  replaces is the only ground that was on offer anywhere: the four
  Firefox-against-Chromium cost ratios, which
  [`kb/playwright/provisioning-and-timings.md`](kb/playwright/provisioning-and-timings.md)
  described as *"the whole of the evidence behind Chromium being the default
  family"*. Re-measured on 2026-09-16 with a preserved rig
  ([re-verification row 34](kb/re-verification.md)), **three of the four
  collapsed by between 1.7x and 7x and the fourth reversed sign** — RAM 2x to
  1.19x, first navigate 10x to 4.62x, profile disk 20x to 2.76x, and idle CPU
  ~24x to **0.77x**, which says Firefox burns *less*. **Nothing about the
  default changes.** The claim that those ratios justified it is retired in the
  kb, in the re-verification row, in
  [`DECISIONS.md`](DECISIONS.md), in `SessionManager.DefaultBrowser`'s own
  remarks and in [`README.md`](README.md), each corrected by addition with the
  previous text quoted. No market-share figure is cited: one would be external
  and would float, and the decision does not need it.
- 📝 **The upstream record catches up: one ask granted, one fix declined, one
  count reconciled.** Three corrections by addition, each re-read from the
  API on 2026-09-17 rather than carried over.
  **(1)** [microsoft/playwright#42497](https://github.com/microsoft/playwright/issues/42497)
  — absolute paths in tool results — **closed `completed`**, by the merge of
  [PR #42673](https://github.com/microsoft/playwright/pull/42673) at
  2026-09-16T15:38:22Z rather than by a reply. The flag, the `filePaths` config key
  and `PLAYWRIGHT_MCP_FILE_PATHS` are in `playwright-core`
  **1.64.0-alpha-2026-09-17** (`next`) and in **no released `@playwright/mcp`** —
  `latest` is 0.0.81, pinning **1.64.0-alpha-2026-09-14 exactly** — so there is no
  drift by the build rule and the row becomes *resolved upstream, adoption pending
  the roll*, with the adoption plan written out so the roll is a review and not a
  design.
  **(2)** [PR #42721](https://github.com/microsoft/playwright/pull/42721), the WebP
  16,383 px fix, was **closed unmerged** at 2026-09-16T00:15:20Z — `dcrousso`:
  *"this is really an upstream issue and should be fixed there instead"* — so the
  fix is expected in **Chromium** (CL 8416650, status NEW) and will arrive through
  a browser-revision bump rather than a `playwright-core` change. The settlement
  condition is unchanged and the two hazard rows stand.
  **(3)** `ChildEnvironment`'s opening paragraph said **43** `PLAYWRIGHT_MCP_*`
  variables with **two** outside the config mapping, stamped at
  1.63.0-alpha-2026-08-31, while re-verification row 17 said **45** with **three**
  at the version that ships. Both were right about their own version and neither
  could see the other; the comment is reconciled to the row, and
  `RecordedCountTests.TheUpstreamVariableCountInTheDocCommentIsWhatRowSeventeenSays`
  now holds it there — planted red at *"Expected to be equal to `45` but received
  `43`"*.
  **And one thing is written down before it happens:** the next `@playwright/mcp`
  roll brings a new **default-surface** tool, `browser_emulate_media`
  (`capability: 'core'`, taking `browser_*` from 83 to 84 with none removed or
  renamed). [`tool-verdicts.json`](tool-verdicts.json) is deny-by-default and
  refuses a name it has no row for at startup, so **the suite will be red on
  exactly that pending judgement** — which is the mechanism working, and a verdict
  is the maintainer's to give.

- ✅ **`NeverByImageNameTests` reads the filter rather than the API.**
  Fourteen of the fifteen files it was flagging never violated anything. The scan
  asked whether a file contained one of five substrings — `taskkill`,
  `GetProcessesByName`, `Win32_Process`, `Get-Process`, `szExeFile` — which cannot
  tell `Get-Process -Id $pid` from `Get-Process chrome`. Those are opposite things:
  one names a pid the caller already holds, the other picks a stranger out of the
  machine by what its executable is called.
  [`ProcessSelection`](tests/BrowserAI.Tests/Harness/ProcessSelection.cs) now reads
  the selection — a `-Name` parameter, a bare positional name, `taskkill /IM`,
  `GetProcessesByName`, an `szExeFile` read, a `Name` clause inside a WMI query, or
  a `Name` compared with a comparison operator in a file that enumerates processes
  — and lets every pid form through. **Q203**, decided 2026-09-17. A narrowing
  needs both directions, so each shape has a synthetic control that must be caught
  *and* the pid-keyed spelling of the same call that must pass, plus the mixed line
  (a pid filter that also names an image, which is still a violation) and the
  file-scoped gate that keeps `$_.Name -eq` over a **directory** listing out of it.
  **Measured on the corpus it was built for**, the predicate being *a file among
  the extensions the scan reads whose code text selects a process by its image
  name*: over the rigs in [`docs/probes/`](docs/probes/README.md) the old scan
  flagged **15 of 36 files in 7 of 14 rigs** and the new one flags **1 of 36 in 1
  of 14**.
  ⚠️ **That one is real and the move to `build/probes/` is therefore not
  taken.** `2026-09-14-firstrun/observe.ps1` calls `GetProcessesByName` over a
  literal watch list — matching and counting by name, which the rule forbids as
  against the observing it permits — and it cannot be re-spelled pid-keyed,
  because what it watches for is a console host appearing anywhere on the machine.
  The move was performed and reverted; `docs/probes/README.md` and
  [`CLAUDE.md`](CLAUDE.md) are corrected by addition, and both said every use was
  by pid or parent pid, which was true of fourteen files and false of this one.
  **Three false positives outside the rigs were found and removed by the same
  change**, in `build/New-Release.ps1`, `build/Write-ReleaseManifest.ps1` and three
  test files: `WHERE` as a query marker matches `Where-Object` and LINQ's
  `.Where(` under case-insensitive matching, so WQL is recognised by its `FROM`
  clause instead.

- ✅ **A pid that vanishes between the walk and the query is *exited*, not
  *unknown*.**
  Unknown is what the host was reading as a containment failure. `JobContainmentTests.ADescendantTreeIsContainedAndNothingSurvivesTheLauncher`
  went red on 2026-09-16 on a **docs-only** commit, *after* `escapees == 0` had
  already passed: a row came back with a null `inOurJob` because `OpenProcess`
  returned `ERROR_INVALID_PARAMETER` for a descendant that had exited between the
  toolhelp walk and the per-row query. The rig had one spelling for two opposite
  answers — *could not be read* and *is no longer there* — and an exited process
  is neither a survivor nor an escapee. `ProcessQueryVerdict.ForFailedOpen` now
  decides which it was, **once, from what Windows said**: `87` and `5` are
  `Exited`, and **everything else stays `Unreadable`**, keeps its note and still
  reddens the run, so this is a classification rather than a retry or a
  suppression. `inJobProcessIdList` — the kernel's own membership snapshots taken
  either side of the walk — is still asserted for every row including an exited
  one, and `escapees`, `jobMembersTheWalkMissed` and the survivor check are
  untouched. **Q202**, decided 2026-09-17. Planted red three ways and watched, with
  the error numbers *measured* rather than quoted. [`HAZARDS.md`](HAZARDS.md)
  carries the row.


- 🔧 **`BrowserAI.Core` declares its RID, and the lock file has one state.**
  It had two, and the last restore won. `src/BrowserAI.Core/packages.lock.json`
  had **two stable states and the last restore won**: a RID-specific restore (every
  publish, and `build/New-Release.ps1`) wrote a `net10.0-windows7.0/win-x64`
  section, and a solution restore (what `dotnet test` performs) removed it again —
  so the tree opened dirty after every publish *and* after every suite run, and
  Q199 could only choose which of the two to commit. Declaring
  `<RuntimeIdentifier>win-x64</RuntimeIdentifier>` on the library ends it at the
  source: five restore shapes measured on 2026-09-17 all write `fab160c4…`,
  including the two that used to disagree
  (`dotnet restore src/BrowserAI/BrowserAI.csproj -r win-x64 --force-evaluate`
  against `dotnet restore BrowserAI.slnx --force-evaluate`, each forced to
  re-resolve so that a no-op restore could not be mistaken for agreement). The
  other state, `7f30ec57…`, is no longer reachable. Solution build after the
  change: 0 warnings, 0 errors; the only cost is one directory level in the
  library's own build output, which nothing in this tree reads by path. **Q201**,
  decided 2026-09-17. [`RELEASING.md`](RELEASING.md) item 5 and
  [`TESTING.md`](TESTING.md) are corrected by addition — both said the file would
  show modified after a run, and neither is true now.

- ⬆️ **TUnit moved 1.67.0 → 1.68.4 and Microsoft.Testing.Platform deliberately
  did not move at all.** `dotnet restore --force-evaluate` on 2026-09-17 re-resolved
  the float and took TUnit across two releases: 1.68.0 (2026-09-15) and 1.68.4
  (2026-09-16). One change in that span touches this tree and it is an analyzer
  loosening — [*Fix TUnit0023 false positives for disposal through casts*](https://github.com/thomhurst/TUnit/pull/6818)
  — which can only turn a red build green, never the reverse; TUnit's analyzers run
  at **error** severity here, so a *tightening* would have been the thing to read
  carefully and this is its opposite. Nothing else in 1.68.x is reachable: the
  mocking fix is `TUnit.Mocks`, the video recorder is `TUnit.Playwright` (which
  `ForbiddenDependencyTests.NoProjectDrivesPlaywrightDirectly` forbids outright),
  and 1.68.4 itself is a documentation skill plus a `mockolate` bump.
  **`Microsoft.Testing.Platform` stayed at 2.4.0 even though 2.4.1 exists**, published
  2026-09-16T14:22Z — three and a half hours *after* TUnit 1.68.4, which declares an
  exact `Microsoft.Testing.Platform 2.4.0` dependency, and NuGet resolves the lowest
  applicable version. So the `[After(TestSession)]` hook that writes the coverage
  block and the `ITestExecutionFilter` read behind `BROWSERAI_RELEASE_RUN` are on
  byte-identical platform code, and the float is not dead — it resolved, and what it
  resolved to is 2.4.0. Solution build after the move: 0 warnings, 0 errors.

### Removed

- 🗑️ **`browserai-sessions.json`, the per-root roll-up, and every mechanism that
  wrote it.** It
  was the one file this product wrote to a path the caller never chose — the
  *parent* of the session directory it was given, so a session at
  `C:\repo\.browserai` put it at `C:\repo\browserai-sessions.json`, unignored,
  in somebody's repository root, against the promise in
  [`README.md`](README.md), [`ARCHITECTURE.md`](ARCHITECTURE.md) and the
  installer's own registration message that BrowserAI writes no per-repository
  files. **Nothing read it**: not the product, not a script, not a hook — only a
  test helper that parsed it back to assert what the code had just written. Its
  content was already delivered live and current by the sibling-sessions line in
  the `init`/`resume` answer, from the same index walk, and by `browserai_list`
  at any scope. Gone with it: `SessionRollUp` entire, its schema version and the
  forward-compatibility argument that only a written file could need,
  `SessionManager.RefreshRollUp` and its three call sites, and the `(rolled up
  in …)` / `COULD NOT BE WRITTEN` clauses those answers carried. **`destroy` no
  longer walks the machine-wide index at all** — it followed it only to rewrite
  this file — and the walk that stays returns directories rather than a record
  per sibling, which drops a recursive `SizeOnDisk` enumeration per session from
  every session open. Three promise sentences that were false became true
  without being edited. *The feature was extracted from artifact routing on
  2026-08-26, the day the rest of that machinery was deleted, and was never
  chartered; the rule it broke is written down now in
  [`DECISIONS.md`](DECISIONS.md#shape-and-packaging) so its absence is a
  decision rather than an omission.*

- 🗑️ **`consoleLevel`, and the four-level choice behind it.** The console level
  is now `debug` **always**, with no argument. Measured: `error`→`debug` costs
  **+1 character** on a navigation response and **+5** otherwise, because the
  events line in a tool response is a *pointer* — `path#L1-L20` — and never the
  message text, so the whole cost of the most verbose setting is the width of a
  larger line number. And the read-level knob already exists one layer up:
  `browser_console_messages` takes its own level, so a caller that wants only
  errors asks for only errors **at the moment it asks** — where a capture level
  chosen hours earlier at `init` cannot be raised retroactively.

- 🗑️ **Session modes.** `headless`, `interactive` and `persistent` are gone, and
  so is the `mode` argument on `browserai_init` and the refusal
  `browserai_resume` made when it was given one. **A mode was two things** —
  whether a window appeared, and which upstream capabilities the session's child
  was launched with — and the two went different ways. **The window became a
  per-run argument**, `headed`, on `init` and on `resume`: it is regenerated at
  every child launch beside `tracing` and `debug`, and is **not** written to the
  session record, so a session created headless is watched headed tomorrow
  without being destroyed and recreated first. **The capability set became every
  capability upstream declares.**

  **Why, and it is the reason the two 2026-08-18 and 2026-08-19 corrections had
  already established twice over:** a capability withheld from a session was
  never a boundary against the *caller*. The calling agent chooses the session
  directory, the profile and its cookie database are created inside it, and the
  agent runs as the same Windows user — [measured
  2026-08-18](kb/chromium/profiles.md#chromiums-cookie-store-and-what-it-takes-to-read-one--measured-2026-08-18)
  — and could in any case reach whatever a missing capability withheld by
  resuming the same directory as a different mode. What it *cost* was real: a
  `headless` session that needed to read one cookie had to be destroyed and
  recreated. **The one argument the modes had left was headless-with-storage** —
  "full credential access with no visible signal" — and it did not survive the
  observations that `headless` already persisted cookies on disk, that
  `browser_run_code_unsafe` is `core` and reaches the cookie jar in every
  session, and that a window is a signal to a **human**, who is absent in the
  case that argument was about.

  **`browserai.json` moved to schema 3** in the same change, because `mode` was
  a recorded field and a field nobody reads is worse than no field. There is no
  converter and there will not be one: a version-2 record is refused with the
  recovery it has always carried — delete it and call `browserai_init` on the
  directory again; the profile, output and downloads beside it are untouched and
  the new session goes on using them. **What is lost is the recorded purpose and
  the history.**

  **What went with it:** `SessionMode.cs` entirely — the enum, the one table and
  the six consumers that rendered it; the mode line in `browserai_init`'s and
  `browserai_resume`'s answers; the `mode:` column in `browserai_list`; the mode
  in `browserai_destroy`'s summary and in `browserai_init`'s already-a-session
  refusal; the `mode` field in the per-root roll-up, which moved to schema 2
  with it. `browser` is now the only thing `init` binds permanently and the only
  argument `resume` refuses — for the reason that always separated it from
  `mode`: a profile on disk belongs to the browser that made it.

- 🗑️ **Continuous integration, completely.** `.github/workflows/build.yml` was
  added on 2026-08-18 and deleted on 2026-08-20 without ever being released, at
  the maintainer's decision, verbatim: *"Remove CI completely. Let all the tests
  run on my machine only. I want no CI and no github runner. Add to the todo
  that we will add CI back in later. But that requires me adding infrastructure
  for self-hosted runners and I am considering leaving github before we do so."*
  `.github/` is gone entirely rather than left as an empty husk. **The whole
  gate is now [the release checklist](RELEASING.md#the-release-gate)**, which
  gained a requirement in item 8: the suite is run from **PowerShell and from
  Git Bash**, and both totals recorded, because the drive letter's case is
  inherited from the shell and a single-shell run bakes in whichever spelling
  agrees — which is precisely what the removed CI did, running `pwsh` end to
  end.

  **What was audited before deleting it, because "do not lose anything unique"
  was the load-bearing half of the instruction.** Most of what the workflow did
  is already covered locally and more strictly — the two-step `restore
  --force-evaluate` / `--locked-mode` and both lock diffs are release checklist
  item 1 with `--exit-code`; the cold CDN download is
  `FirstRunProvisioningTests` against an empty root, hourly and always on a
  release run; the coverage block is written to stdout and to
  `.work/suite-coverage.txt` on every run; MinVer has its tags in any developer
  clone. **Three things genuinely die**, and they are
  [`TODO.md`](TODO.md#continuous-integration)'s content: a *different machine* —
  four cores, cold caches, no interactive desktop, a volume with 8.3 generation
  off — which found four defects this machine structurally cannot, including the
  `browserai_destroy` survivor arm that passed nine local runs and failed three
  consecutive CI ones; **a contributor's pull request built before merge**,
  which on a public repository is what the workflow was for; and
  `BROWSERAI_EXPECTED_ABSENT`, whose only consumer anywhere was that file.

- 🗑️ **`SuiteCoverageTests.TheWorkflowStillDeclaresWhatItExpectsToBeAbsent` is deleted.** `SuiteCoverageTests.TheWorkflowStillDeclaresWhatItExpectsToBeAbsent`,
  the capability pin's third arm. It read `build.yml` scoped to the step that
  ran the suite, so deleting the declaration was a red build rather than a
  silent switch-off. **Deleted rather than re-pointed, and the reason is a house
  rule:** a search that returns zero needs a positive control. The old test had
  one — *this really is the step that runs the suite* — and a re-pointed version
  aimed at "any pipeline definition" could have none, so it would pass over an
  empty directory, over a typo in its own path, and over a pipeline shape it
  does not recognise, indistinguishably. **The mechanism itself is kept, correct
  and inert:** an unset `BROWSERAI_EXPECTED_ABSENT` declares nothing, which is
  already what a developer machine does, and
  `TheExpectedAbsentDeclarationIsReconciledAgainstWhatIsAbsent` still exercises
  every branch in-process. Restoring the arm against whatever runs the suite
  next is part of the CI item.

- 🗑️ **`BuildConfigurationTests.NoSourceFileIsInvisibleToGit` is deleted, deliberately.** `BuildConfigurationTests.NoSourceFileIsInvisibleToGit`
  is deleted, deliberately, and this entry exists so nobody re-adds it believing
  it was an oversight. It listed every `.cs` under `src/` and `tests/` and
  asserted each appeared in `git ls-files`. It existed because of a real loss:
  the .NET template's unanchored `artifacts/` rule matched
  `src/BrowserAI/Artifacts/` on case-insensitive Windows, and **five product
  source files were ignored while the build, the suite and `git status
  --porcelain` all read green**. **The maintainer's call, over a recommendation
  to widen it past `*.cs` rather than remove it** — *"I do not think we need
  this test at all."* Gone with it: `TrackedFilesAsync`, the harness that
  shelled out to git and served nothing else.

  **What is now unenforced, said plainly rather than left to be found.** Nothing
  compares the files on disk against what git can see, so a source file
  swallowed by an ignore rule is invisible again exactly as it was on
  2026-08-15, and every surface signal reads healthy while it is — an ignored
  file is not untracked, so a clean `git status` is what a swallowed file
  *produces*. **64 unanchored directory rules remain** in the upstream half of
  `.gitignore`; the predicate is *a line above the BrowserAI marker that ends in
  `/`, does not begin with `/` and is not a negation*, re-counted 2026-08-19,
  with `/artifacts/` and `/.artifacts/` the only two anchored ones as the
  positive control. ⚠️ *Previously published as "nineteen" with no predicate
  written down, and no predicate reproduces nineteen — the figure is replaced
  rather than corrected.* Both `.gitignore` comments that named the test now
  record the deletion and what it costs, and the upstream-refresh procedure ends
  in **run `git check-ignore -v` by hand** where it used to end in *run the
  suite*. Reasoning and reversal in [QUESTIONS.md judgement call
  E](QUESTIONS.md#e-nosourcefileisinvisibletogit-was-deleted-and-this-is-not-the-entry-you-think-it-is).

- 🗑️ **`browser_annotate` is gone from the model-facing surface.** Not refused —
  **absent**: filtered out of `tools/list` in every mode, so it costs a model no
  attention and no description budget for a call that cannot succeed. Filtering
  the surface is in scope by the charter, where renaming is not.

  Yesterday's measurement earned a refusal; read again it withdraws the tool.
  **It has no self-timeout** — the control run stood silent for a full 90 s, and
  the wait is `await new Promise(resolve => client.on("exit", …))`, whose only
  bounded arm is the daemon failing to start at 15 s. Its window is a **second,
  non-headless Chromium**. There is **no configuration in which it runs
  headless**, because the dashboard's headedness is `headless:
  !!process.env.PWTEST_DASHBOARD_APP_BIND_TITLE` — an upstream *test* variable
  no session config reaches. And it **escapes the session's containment**: a
  `detached`, `unref`'d, per-user-singleton daemon writing its profile into
  `%TEMP%`, of whose 18-process tree a parent walk found **zero** after the
  probe exited. None of that is about whether a window was promised, so the
  per-mode refusal it used to get was the wrong shape.

  **A caller that names it anyway is refused rather than forwarded**, because a
  model knows upstream's tool names from everywhere except this server's list,
  and forwarding one would hang an unattended run — the thing this product
  exists not to do.

  **What it would take to bring it back** is recorded in
  [DECISIONS](DECISIONS.md#licence-release-policy-and-the-tool-surface) and
  beside the code: a bounded call, a dashboard inside the session's own
  containment, and a headless path that does not turn on a `PWTEST_*` variable.
  No two of the three are enough.

  **What went with it**, rather than being left unreachable: the mode-keyed
  refusal row `SessionErrors.AnnotationWouldHangAWindowlessSession` (replaced by
  `AnnotationIsNotInTheSurface`, which names the absence first); the mode
  parameter on `SessionToolPolicy.Decide`; `SessionToolPolicy.Note` and
  `SessionToolSurface.AppendModeNote`, the entire description-rewrite path — so
  **every upstream description now passes through byte for byte**, which is a
  stronger property than the append-only rule it replaces and is asserted as
  one. The surface is **58** upstream tools where it was 59, in every mode, and
  the `annotations` artifact folder stays declared because that set is derived
  from upstream's bundle rather than from what this build calls.

- 📝 **The sweep's two highest-value assumptions are measured.** The sweep's two
  highest-value assumptions, measured — and both decisions they were holding up
  survive. They were the worst kind of unmeasured justification: each already
  justified a decision that had been *taken*, so neither could fail loudly, and
  confirming them was the only thing that could distinguish a sound decision
  from a lucky one.

  **`browser_annotate` on a `headless` session does open a window, and does not
  return.** Three runs against a real child on the config this product
  generates: a **visible** `Chrome_WidgetWin_1` at `100,100,1280x800` took the
  **foreground** within 1.2 s every time, and in the control arm the call was
  still silent 90 s later — the other two returned only in the same 40 ms tick
  their window disappeared, which is the human path. **So the only refusal left
  in the product is earned**, and the sentence behind it — undated, uncited and
  never measured, and contradicted on its face by this repository's own
  measurement that headless Chromium shows nothing — now carries a date, four
  versions, a method and a re-verification row. The mechanism is worse than the
  sentence said: the window belongs to a **second, non-headless Chromium** under
  a **detached, per-user-singleton** dashboard daemon, launched headed on an
  upstream *test* environment variable that no session configuration reaches,
  and writing its profile into `%TEMP%` rather than into any session directory.
  That is a hazard nobody had written down, and it now has a row.

  **The DPAPI claim that deleted the whole tool-permission layer holds.** The
  removal rested on *"the agent chooses the session directory, the profile with
  its cookie database sits inside it, and the agent runs as the same Windows
  user, so DPAPI decrypts for it"* — repeated across six documents and
  terminating in a `kb/` line with no date, no version and no method. The
  unasked question was **App-Bound Encryption**, which since Chrome 127 binds
  cookie decryption to the browser's own code identity. Measured against a
  session BrowserAI configured and nothing else:
  `os_crypt.app_bound_encrypted_key` **absent**, cookie scheme tag **`v10`** and
  not `v20`, and from a *separate* process as the same user `CryptUnprotectData`
  with no entropy returned the 32-byte key and AES-256-GCM recovered the value.
  **DPAPI alone — no elevation, no service, no admin.** Stronger than "ABE could
  not apply here": this machine *has* a registered Chrome elevation service,
  from the operator's own install, and the provisioned build still produced a
  DPAPI-only key.

  Both are stamped in [`kb/`](kb/README.md) with how to re-establish them, cited
  from the code and from all six documents that used to cite each other, and
  covered by re-verification rows 94 and 95 — each naming what a bump would
  silently invalidate, because both are facts about upstream that today's suite
  would stay green through.

- 📝 **The justification sweep: 598 load-bearing reasons sorted.** The
  justification sweep: 598 load-bearing reasons sorted, 63 assumed, 13 settled
  by measurement and 11 relabelled. Every mechanism in this repository protects
  a claim about *behaviour* — a test fails, a snapshot diffs, an analyzer
  errors. A claim about a *reason* is invisible to all of them, and a rule with
  a measured reason reads exactly like a rule with a plausible one. Nothing
  below could ever have gone red.

  **The one where the reason was wrong and so was the rule's ground.**
  [`DECISIONS.md`](DECISIONS.md) closed *download alongside and swap* for
  `browserai_reinstall_browser` with *"Windows will not rename a directory
  holding open executables"*, cited to an article about mutex naming that does
  not discuss renames. Measured against a live process whose working directory
  was deliberately elsewhere: `Directory.Move` of the running image's **parent**
  and **grandparent** both succeeded, and renaming the **running `.exe`** itself
  succeeded; only `File.Delete` of it was refused. **The refusal is left
  standing** — what a browser does when its tree is renamed underneath it has
  never been measured — but it is no longer resting on an impossibility.

  **`[DefaultDllImportSearchPaths(System32)]` is inert for 39 of the 43
  declarations it is written over.** With genuine `System32` copies planted
  beside a probe, `kernel32`, `user32` and `ntdll` loaded from `System32` with
  or without the attribute; only `rstrtmgr.dll` — the one library here that is
  not a KnownDLL — loaded from the application directory without it. The rule is
  kept on every declaration; the trap was the audit, since anyone testing it
  with a fake `kernel32.dll` sees nothing happen and concludes it is decorative.

  **`Debug.Assert` does not raise a modal dialog.** That is .NET Framework's
  `DefaultTraceListener` and has not been true on .NET Core. Measured on .NET 10
  with stdio redirected: a Debug build wrote the assertion to stderr and **died
  at once with exit code 35**, and a Release build ran straight past it, because
  `[Conditional("DEBUG")]` compiles the call out. So the shipped artifact
  carries a guard that does nothing and the suite one that kills the server. The
  ban stands, for better reasons than it had.

  **Also settled by measurement**: a held handle stops pid reuse — with the
  handle released a pid repeated after 2,010 spawns, with one held there was no
  repeat in 6,030, and the *control* is what makes that mean anything;
  `Marshal.GetLastPInvokeError()` survives allocation, a GC, a `MemoryStream`
  and a `Console.Out.Flush()` and is destroyed only by another capturing
  P/Invoke, while the 11 declarations without `SetLastError` return a confident
  `0`; `Console.Out` really does write CP437 and CRLF under redirection, putting
  `82 2E` on the wire for two non-ASCII characters; `UseSystemResourceKeys`
  saves **161,280 bytes** against a **111,984,018-byte** payload, both halves of
  a trade that had a word for a numerator and an unweighed denominator;
  `Console.OpenStandardOutput` returns a `WindowsConsoleStream` and not a
  `FileStream`, so the `FileOptions.Asynchronous` the comments named was never
  involved; and the SDK's version decoration appends `+<sha>`, not `.<sha>`.

  **Relabelled rather than settled**, because an admitted gap beats a confident
  sentence: *"every Node process supervisor on Windows"* (never surveyed, and it
  is the "nobody else solved this" half of the decision that chose C# for the
  whole product) · the *~640 MiB* provisioning peak (arithmetic that does not
  reach its own number, shipped as a refusal constant) · *"the holder is a
  scanner"* (rates measured, cause never established, and the retry rule depends
  on which it is) · the thread pool's injection rate (two articles a factor of
  two apart, and one of them measures injection away as the mechanism) · and the
  drift check *"runs by construction"* (it cannot fire during the quiet month
  that defines the gap it answers).

  **Three citations pointed at `plan/`**, deleted with the implementation plan
  on 2026-08-17 and never re-aimed; and `Interop/CLAUDE.md` published a count of
  41 beside its own 43, under a stated re-count predicate that returns 45
  because two doc comments mention the attribute in prose.

- ✅ **The client's *"2KB each"* is per string, and the gate was measuring the wrong
  unit.** `ClientTruncationBudget`
  had said for four days that the per-string reading was an **assumption** — the
  competing reading being one 2 KB bucket per whole serialized tool, under which
  `browserai_init`'s entry was over the line and silently truncated on every
  session, and under which trimming a description would have moved text from one
  capped bucket into the same capped bucket. It is now measured, @ **Claude Code
  2.1.234**, by pointing the client at a local recorder through
  `ANTHROPIC_BASE_URL` and reading the `tools` array it sends to the Messages
  API — what the model receives, rather than what a model recalls. **Per string,
  2,048 UTF-16 characters, cut at `> 2048`.** A probe tool with a 4,578-byte
  entry arrived intact, as did entries of 17 KB and 20 KB; a 2,048-character
  description weighing 6,004 bytes arrived whole, so **bytes are never
  counted**; there is no whole-surface total either — 202 tools and 348,314
  bytes of entries went through untouched; and
  `inputSchema.properties[*].description` is **not truncated at all**, 20,000
  characters included. The cut appends the literal `… [truncated]`, which the
  model can see and a server never can. `browserai_init` was never truncated.
  What changed as a result: `ClientTruncationBudget.Bytes` is now `.Characters`
  and the gate counts characters — a byte gate can only fail strings the client
  delivers whole — the parameter cap is relabelled a **house limit** rather than
  a client limit, and questions 1 and 10 in [`QUESTIONS.md`](QUESTIONS.md) are
  answered. Recorded with the method in
  [kb](kb/mcp/protocol.md#what-2kb-each-means--measured-2026-08-18--claude-code-21234)
  and re-verification row 92, because every figure in it floats with a client
  version this project does not control.

- 🗑️ **BrowserAI advertised an MCP capability it does not implement.** The SDK's
  `McpServerImpl` gates `Tools`, `Prompts`, `Resources` and `Completion` on
  configuration, and `ConfigureLogging` on nothing at all — so `initialize`
  answered `{"tools":{},"logging":{}}` from a server that has never emitted a
  `notifications/message`, and a client calling `logging/setLevel` got `{}` and
  then silence for ever. `BrowserProxy.UnadvertiseLogging` removes it in an
  outgoing message filter, which is the only route: the options object cannot
  un-set it and the property is `[Obsolete("MCP9005")]`. What this server
  advertises is now byte-identical to what the child advertises, and
  `VerticalSliceTests` asserts the whole object off the published binary's wire
  rather than only that `tools` is present.

- ✅ **The test double was more capable than the thing it doubles.** `FakePlaywrightChild`
  answered `initialize` with `{"tools":{"listChanged":true},"logging":{}}`
  against a real child that advertises `{"tools":{}}`, so the whole in-process
  layer ran against capabilities production can never produce. It now answers
  the snapshot's own string, and
  `UpstreamSnapshotTests.TheDoubleAdvertisesWhatTheRealChildDoes` holds the two
  together. No test depended on the lie.

- 🐛 **`browserProvisioning` said `downloading` when this process had started no
  download.** A
  provisioner that loses the machine-wide provisioning mutex is watching for
  another process's completion marker — and cannot see whether that process is
  downloading, extracting, or walking every process on the machine inside its
  revision prune. It rendered *"… is being downloaded into '…'"* anyway. The
  attempt now carries a phase and the sentence says what this process is
  actually doing. **The state word was left alone here** — *and renamed to
  `provisioning` later the same day; see Changed, below.* This entry stands as
  written because it is what shipped in this order: the sentence was a claim
  about the world and was fixed first, the word was a product-voice decision and
  waited for one.

- 🐛 **A supported configuration warned on every startup.** `warn: velopack:
  Failed to initialize WindowsVelopackLocator` fired on every start of a binary
  Velopack did not install — every test host, and the configuration CI runs in —
  a hundred per saturation run, on the stream this project relies on for
  diagnosis. Demoted to `Debug` on three independent conditions; a genuine
  locator failure carries different text at `Error` and is untouched.

- ✅ **`HazardIndexTests.EveryRowIsOpenOrClosedAndNothingElse` enforces its own name now.** `HazardIndexTests.EveryRowIsOpenOrClosedAndNothingElse`
  did not enforce the invariant its name promises. It asked whether the `Status`
  cell *contained* "open" or "closed", so a row reading `**half closed**` passed
  it for eight days while being in neither tally. It now matches the leading
  word exactly. The row is adjudicated on its evidence: **closed**, with the
  pre-check's limit stated — the hazard is that disk exhaustion mid-provision is
  *success-shaped*, and the source fix removes the shape.

- 📝 **Four counts in prose were wrong.** Four counts in prose were wrong,
  including one where a correction had replaced a right number with a wrong one
  by measuring a different predicate over the same table. `TODO.md`'s hazard
  tally said 3 rows were `open` while carrying evidence and 57 open in total; it
  was 4 and 58. `DECISIONS.md` said the installer is *"~117 MB"*, which was the
  installed payload rather than the installer: re-measured off the artifact
  `build/New-Release.ps1` packed for `v1.0.0`, it is **53,567,930 bytes — 51.1
  MiB**.

- 🗑️ **The free-space check is gone, and nothing asks a volume how much room it has.**
  The maintainer's decision, in his words: *"Remove the free space check.
  Checking for free space is out of scope and makes our project more
  complicated. I do not want to check for that at all."* Until today
  `browserai_init` asked the volume for its free bytes and refused below
  **640 MiB**, naming the number, before it created anything.
  `SessionManager.RequiredFreeBytes`, the refusal it drove,
  `SessionErrors.InsufficientDisk` and the injected free-bytes reader the suite
  triggered it through are all deleted; the error catalogue is **25 rows**
  rather than 26. **The removal was planted red first**, two ways: the same
  condition the old arm provoked — a volume reporting 12 MiB free — asserted
  *not* to refuse (*"Expected to not be equal to True but received True"*), and
  a new tree-wide scan,
  `HouseRuleTests.NothingAsksAVolumeHowMuchRoomItHas`, which named four files
  before the change and none after. **The old refusal test was deleted rather
  than skipped**: it asserted behaviour that no longer exists, which is not a
  gap in coverage. **What it costs is written down rather than implied** — a
  machine that runs out of room now finds out partway through a 207.3 MB
  download instead of in a sentence at `init`, which is exactly how any volume
  that could not answer the question in one call already behaved. The ~635 MiB
  arithmetic peak stays in
  [`kb/playwright/provisioning-and-timings.md`](kb/playwright/provisioning-and-timings.md)
  as a budget for a reader, gating nothing, and the open ask to sample free
  space across a run is withdrawn as moot.

### Fixed

- 🐛 **A launcher that had exited but whose pid still opened was read as a live client.** `ClientLivenessWatcher`
  treated `OpenProcess` succeeding as *there is somebody there*. Windows keeps a
  process object for as long as any handle anywhere names it — the console host
  holds one for a process that ran in a console — so a launcher that was already
  gone went on answering, the watch attached to an already-signalled handle, and
  `Program.Main` was told the client could be watched: **the no-client fast exit
  was skipped for a corpse**, and the run swept the machine, took the live
  marker and served nobody. The fix is one question after the identity pairing —
  `WaitForSingleObject(handle, 0)` — and an already-signalled handle takes the
  same path as a pid that cannot be opened at all, under a record of its own
  (`Startup[76]`) so the log says which of the two routes it took; a wait that
  cannot be interpreted is read as neither. Measured through the one-launcher
  rig: the launcher is gone **30 ms before** the product writes its first
  record, the decision lands at **0.116 s**, and the data root afterwards holds
  `logs\` alone.

  ⚠️ **It was found as a flake, and what made it one was that the machine
  decided the condition.**
  `InstallerHandoffTests.ARunWithNobodyToServeStartsNothingAndCreatesNothingButItsLog`
  went red once in four full runs on 2026-09-15, at the whole of
  `TestDefaults.ProcessHang`, and the hazard row written that morning said no
  rig could close it. `OrphanedConsoleStart` now produces either shape on
  request — `LauncherCorpse.Freed` for a pid nothing holds,
  `LauncherCorpse.Openable` for one the test host keeps a handle on — so the arm
  is deterministic: planted against the published pre-fix binary it failed
  **every** time in under 400 ms rather than once in four. Both arms wait for
  *either* decision now, so losing that race is a named failure in a second
  instead of ten minutes of silence.

  Three arms, all watched red first:
  `ProcessLivenessTests.AWatchIsRefusedWhenThePidOpensAndItsProcessHasAlreadyExited`
  (*"Expected to be null but found BrowserAI.Interop.ClientLivenessWatcher"*,
  254 ms),
  `InstallerHandoffTests.ThePublishedBinaryTreatsALauncherThatExitedButStillOpensAsNobodyToServe`
  and `.ARunWithNobodyToServeStartsNothingAndCreatesNothingButItsLog` (both
  *'received "Watching the MCP client"'*). Green after the fix, and 8 of 8
  filtered runs of the class since.

- 🐛 **BrowserAI 1.0.0 did not exit when it had nobody to serve, and every install left orphans.** BrowserAI
  1.0.0 did not exit when it had nobody to serve, and every non-silent install
  ended with an orphaned server, an orphaned browser process and a terminal
  window on the user's screen. The decision was right and unreachable:
  `Program.Main` logged *"BrowserAI has no client to serve and is exiting"* 1.89
  s into the first run and then stood for **213.6 s** more, 10 threads, 224
  handles, holding a `node.exe` and a `conhost.exe`, until it was killed by pid.
  The cause was one `await` — `JsonLinesTransport.DisposeAsync` closed its own
  end of the channel and then waited for its read loop, on the strength of a
  comment that said closing the peer wakes a read blocked in a syscall. That is
  true of the **child** leg, where this process owns the pipe and the child's
  exit closes it, and false of the **caller** leg, where the other end of stdin
  belongs to whoever started us. Measured on .NET 10 against
  `Console.OpenStandardInput()`: once a read has parked, **neither cancelling
  the token nor disposing the stream completes it** — 3 s each, both still
  `WaitingForActivation`, with a console stdin and with a pipe stdin alike
  ([kb](kb/windows/processes.md#a-read-parked-on-standard-input-is-woken-by-neither-cancelling-it-nor-disposing-the-stream--measured-2026-09-15)).
  `ShutdownPeerAsync` now **answers** whether closing the peer ends the read,
  and a loop the peer will never end is **abandoned** rather than awaited: safe,
  because the parked read is on a thread-pool thread and does not hold the
  process open — measured by returning from `Main` with one parked and watching
  the process exit anyway. A loop that has already finished is still awaited,
  whoever the peer is, so the ordinary shutdown is unchanged and a read loop
  that faulted is still reported. Two arms, both watched red first:
  `DirectStdioServerTransportTests.DisposingDoesNotWaitForAReadTheCallerWillNeverEnd`
  in process (red at 5 m 00 s, the whole of `TestDefaults.InProcessHang`), and
  `InstallerHandoffTests.ThePublishedBinaryExitsWhenItsLauncherIsGoneAndStdinIsAConsole`
  over the published binary (red at 10 m 00 s, the whole of
  `TestDefaults.ProcessHang`).

- ⚡ **A run with nobody to serve started a browser server before working out nobody was there.** A
  run with nobody to serve started a browser server first and worked out that
  nobody was there 506 ms later. On the same install, the `playwright-mcp` child
  was launched, the machine-wide stray sweep ran and the update lane opened
  **before** the no-client question was asked — so the run with the least reason
  to cost anything cost the most, and left a second orphan behind when it did
  not exit. The decision now sits immediately after the installer exit, before
  the root judgement, the sweep, the live marker, the instance directory and the
  child. The watch itself is attached there rather than a second predicate being
  asked beside it, because two answers about one `OpenProcess` is two sources of
  truth; what it *does* is registered on the same cancellation token once there
  is a transport to close, and `CancellationToken.Register` on an
  already-cancelled token runs the callback there and then, which closes the
  window the move would otherwise have opened.
  `InstallerHandoffTests.ARunWithNobodyToServeStartsNothingAndCreatesNothingButItsLog`
  asserts the product's own records name no child, no sweep and no feed check,
  and that `logs\` is the only thing under the root; watched red against v1.0.0,
  which named all three.

- ✅ **The suite's installer arm destroyed the maintainer's Add/Remove Programs entry.** The
  suite's own installer arm destroyed the maintainer's Add/Remove Programs entry
  on every run that had a pack to install. Velopack writes one uninstall key per
  pack id per user, named for the id and never for the location: an install
  under `--installto` still rewrites `HKCU\…\Uninstall\BrowserAI.app` to point
  at the scratch root, and `Update.exe uninstall` from that root calls
  `delete_subkey_all(<id>)` **unconditionally**, with no comparison against
  `InstallLocation` anywhere in it. Measured as *no `BrowserAI.app` key after
  six installer-arm runs* — with no real install present to lose, which is the
  only reason nobody noticed. `build/New-Release.ps1` now packs a **second**
  installer from the same publish directory, at the same version, on the same
  channel, under `BrowserAI.app.test` and into `Releases/test-pack/` —
  `$testPackArgs` is `$packArgs` with the id and the output directory replaced,
  so a packing decision cannot reach one pack and not the other.
  `RealInstallerTests` installs that one and the shipping installer is never
  executed by the suite again; the arm reads the real key before and after and
  asserts it byte-identical (or still absent), reclaims the test id's key in a
  `finally`, and `ReleaseLayout.Judge` refuses the capability when one survives
  a run — naming the key and the `reg delete` that clears it. A real install is
  never judged: a key whose `InstallLocation` exists and is not the suite's
  scratch root is `Real`, asserted over constructed inputs rather than by
  writing a registry key to provoke it.
  `RealInstallerTests.TheSuitesPackAndTheShippingPackDifferOnlyWhereTheIdAppears`
  compares the two `.nupkg`s entry by entry and requires every difference to
  mention one of the two ids, with a synthetic both-directions control.

- ✅ **The release gate's ILC check could not fail.** `IlcCompile` is an MSBuild
  target with `Inputs` and `Outputs`, so a publish whose managed assemblies have
  not moved skips it and relinks the previous run's native object: the publish
  succeeds, the binary is good, and HALT-A — the scan of ILC's own console
  output — sweeps a log ILC never wrote and reports clean. Measured 2026-09-15
  at `-v:normal`: **75 lines** with the pass skipped against **95** with it, and
  **389** for the release script's own publish into a cleared output directory.
  Clearing `$PackDir` was already there and is not enough; the up-to-date check
  is on `obj\<config>\<tfm>\<rid>\native\BrowserAI.obj`, and no MSBuild property
  disables it. The script now removes every `obj\Release\*\win-x64\native`
  before publishing, and `build/Test-IlcFullPass.ps1` refuses a log carrying no
  full pass — a separate script precisely so the refusal can be driven, which
  `ReleaseScriptTests.APublishLogWithNoIlcPassIsRefusedAndOneWithAPassIsAccepted`
  does with a skipped log, a full log and a log carrying neither. The marker
  rather than a line count: `Generating native code` is ILC's own line.

- ✅ **Release checklist item 8's code fence set the release variable and nothing else.** Release
  checklist item 8's own code fence set the release variable and did nothing
  else the item asks for. It handed `dotnet test` no explicitly-spelled absolute
  path, so both halves inherited whatever spelling started the shell — **the
  exact 2026-08-24 failure shape the bullet three above it was written to
  close**, reproduced inside the instrument that bullet points at. It set no
  `BROWSERAI_DRIVE_CASE`, so the `drive letter` row could report only what a run
  happened to get and never whether that was what anyone asked for. And it
  appended no coverage block to the log, so six release logs would have carried
  no `release run` row, no `first-run bytes` row and no `filter` row — while the
  two paragraphs immediately below it name that block as the check on all three.
  The fence is now Testing's own two invocations with the release variable set
  inside the detached shell beside the drive-case one, and the previous form is
  quoted in place rather than deleted.

- ✅ **The harness's process-log reader answered with a stranger's records.** The
  harness's process-log reader answered with a stranger's records, because it
  matched half an identity. `ProcessLogRecords` is what lets a test assert *the
  product recorded X* against the durable file rather than against stderr, and
  it selected a writer by matching ` pid=<n>@` — the pid alone, with the
  creation FILETIME behind the `@` read past and never compared. The type's own
  remarks had said in as many words that a bare pid does not identify a writer;
  the method's name, `ForPid`, was the accurate description of what it did. The
  log is machine-wide and kept for thirty days, and Windows reuses pids well
  inside that window, so the scope was answerable by whoever last wore the
  number: **demonstrated live on 2026-08-29**, a read scoped to the running test
  host's own pid came back holding records written on 2026-08-24 by a different
  process wearing it. The reader takes `(pid, creationFileTime)` now and matches
  both halves plus the separator that ends them, so a FILETIME that merely
  *begins* with the right one is not a match either. **The pid-only entry point
  is gone rather than caveated**, because a reader that can be handed half an
  identity will be, and neither of the two callers had to be taught anything —
  both already held the identity they were asking about. **The red was planted
  rather than watched live, and that is a property of the subject rather than a
  shortcut**: whether this machine's log holds a stranger wearing this run's pid
  depends on the box and on the last thirty days, so a live arm would pass by
  matching nothing on most machines and go red only by luck. The control hands
  the reader a directory of three records differing in nothing but the FILETIME
  and, with the old marker restored, it returned all three; the planted lines go
  through the same header expression the file's live arms use, so the control
  cannot outlive the record format it is written against.

- ✅ **The suite's reclaim pass could terminate a live run's processes machine-wide.** The
  suite's own reclaim pass could terminate a live run's processes with exit code
  1, machine-wide, and left no record that it had. `.work\spawn-record.txt` is
  how a killed run's leftovers are named for the next one, and the pass that
  reads it runs on first use of a scratch root **in each process** rather than
  once per run. A row named its subject and nothing else, so a second harness
  process reading a live run's record ended that run's browsers, probes and
  slices — and then `TreeDelete`d the scratch tree they were using. **Reproduced
  18 of 18 on 2026-08-29**, and it is the mechanism that finally explained an
  exit code chased for eleven days as a crash: nothing on this machine crashes
  with a 1, and the kill leaves silent pipes, five browser log lines and no
  message window, which is the same shape a desktop heap spent to the byte
  leaves. **A row now names its owner** — the identity of the process that
  started the recorded process and holds the job object containing it — and the
  pass terminates a subject only when that owner is neither this process nor any
  process still running, checked by pid *and* creation time so a recycled pid
  cannot impersonate a dead owner. **Owner is not "the run"**, deliberately: a
  run spans `dotnet test`, a test host and sometimes a second
  `BrowserAI.Tests.exe`, so there is no one pid whose death ends it, whereas
  there is always exactly one process whose exit closes the job containing a
  given child. Rows the pass declines are **written back verbatim** rather than
  the file being emptied, because sparing the process and blanking the record
  would take the recovery with it. **And the pass says what it did**: one `WARN`
  per terminated process in the machine's process log, naming the identity, the
  owner it found gone, the record it was honouring and the exit code read from
  the constant the call hands it — silent when it ended nothing, since every run
  runs this pass. Three tests, each watched red: the kill with the owner gate
  removed, the recovery with the owner check inverted, and the announcement
  **both ways** — suppressed, and firing on a pass that ended nothing. The
  machine-wide interlock was considered and **not** taken, so two concurrent
  suite runs remain undefined in every other respect;
  [`QUESTIONS.md`](QUESTIONS.md) §8a records what that leaves.

- ✅ **The guard on the `[STALE]` marker forbade the one resolution its own failure
  message prescribed.** `RecordedCountTests`
  held `kb/README.md`'s claim that no article carries the marker by matching the
  **bare token** anywhere under `kb/`, and asserting unconditionally that
  nothing matched. Both halves were defects. It could not tell a stamp from a
  mention, so an article that merely *discussed* the marker turned the suite red
  — the entry recording the update lane's owed re-check had to avoid spelling
  the token at all, and a writer who spelled it in prose reddened a run against
  a correct tree. And being unconditional, it made the kb rule's own escape
  hatch — *re-run the measurement, or mark the entry* — a red build, while its
  message told the reader that the sentence in `kb/README.md` was what had to
  change. **Changing that sentence could not have satisfied it.** Now
  `TheStaleMarkerCountInTheArticleIndexIsWhatTheArticlesHold`: the match is the
  **backticked** marker, which is how the conventions table spells all five and
  how every one of the 487 marker occurrences under `kb/` was written on the day
  this was narrowed; and the count is held against `kb/README.md`'s published
  clause rather than against zero, so **stamping an entry and moving that
  sentence are one edit and pass as a pair**. Neither half passes alone. This is
  the trap [the re-verification index](kb/re-verification.md) already met for
  the floats marker and answered by narrowing the counter's **scope** — an
  answer not available here, because the article that has to discuss this marker
  is a real article full of real measurements, so the narrowing is by **shape**
  instead. Watched in four directions before it went in: the old scan red on a
  planted bare-token mention and the new one green on the same plant, then a
  synthetic backticked stamp red against an unmoved claim, green with the claim
  moved to match, and red again with the claim moved and no stamp standing.

- ⚡ **Starting the server opened the SQLite store of every session on the
  machine.** `Program.Main`
  starts the stray sweep, one pass followed every entry in the machine-wide
  index through `SessionLock.ReadRecord`, and each of those opens left a
  `browserai.data-shm` and a `browserai.data-wal` in a directory nobody had
  named — measured through the published binary, a cleanly-closed session at two
  files and four after a stranger sent nothing but `initialize`. The sweep goes
  **probe-first** now: `SessionIndex` walks at a stated depth, and the sweep's
  is the guard — one `CreateFile` on `browserai.lock`, with two file checks
  behind it. Nothing was given up, because the record was never part of the
  sweep's decision: the removable set is bit-identical, and the one entry whose
  record is still read in full is the one a pass is about to act on, where there
  is nothing to open.

- 🐛 **An idle browser close left no trace in the only record there is.** The
  timer talks to the child directly, and while `browserai.log` existed the event
  survived there; that file is gone, so an autonomous close became an
  unexplained gap in wall-clock time followed by a silent relaunch — under a
  heading that tells its reader *"this is what BrowserAI did"*. It writes a row
  now, the way every forwarded call's row is written: `in-flight` before the
  call reaches the child, settled from the child's own answer, with a `why` that
  names the timer rather than borrowing a caller's voice. **A record that will
  not write does not stop the close** — the one place this tree inverts that
  rule, because there is no caller to refuse to.

- 🐛 **An unjudged `browserai_` name was refused without being recorded.** The
  short-circuit in front of the verdict door was a prefix test, so
  `browserai_zzz` never reached the door: it was refused by the session
  manager's default arm and, having resolved no session, wrote no log row —
  while an unjudged *upstream* name was recorded on the session it named. It is
  an exact match against the seven authored tools now, so both are
  deny-by-defaulted at the door and both land in the log. **The `answer` rows of
  `tool-verdicts.json` become load-bearing at run time as a consequence**, where
  they had been build-and-test-time data. A call with no resolvable session
  still writes nothing and cannot; that residual is asserted rather than
  described.

- 🔒 **A `purpose` or a `why` could carry invisible supplementary-plane text into
  another agent's context.** `RecordText.Sanitise`
  iterated `char`, and `char.GetUnicodeCategory` answers `Surrogate` for either
  half of a supplementary-plane character and never `Format` — so the `Cf` drop
  its own remarks describe covered the basic plane alone, and the whole TAG
  block (U+E0020–U+E007F, the canonical invisible-text range) survived a round
  trip through the record. It enumerates runes now, and a lone surrogate is
  dropped rather than replayed.

- 🐛 **`browserai_catch_up` served an out-of-range `page` as a different page.** The
  number was narrowed to `int` in an unchecked context, so 2^32 + 1 became page
  1 and the answer said *"page 1 of 1"* with no error at all; the mirror case
  wrapped negative and quoted a number the caller never sent. The bound is
  compared as a `long`, the refusal quotes what arrived, and the narrowing
  happens only after the bound holds.

- 🔒 **A refusal echoed the caller's own control characters back into the model
  reading it.** The
  sentence named `U+0007` in words and then carried the byte twice. Every
  catalogue row that quotes a caller's raw spelling now renders it through
  `RecordText.Escape`, which **shows** a code point rather than stripping it — a
  caller has to be able to see which character was the problem.

- 🐛 **A refusal leaked a C# parameter name.** `browserai_init` on a volume root
  answered *"… must be a real directory on the volume. (Parameter
  'canonical')"*, because `ArgumentException.Message` appends one whenever it is
  set and the catalogue interpolates that message verbatim.

- 🐛 **A session directory too deep to hold its own `browserai.data` was accepted, created and locked.** A
  session directory too deep to hold its own `browserai.data` was accepted,
  created and locked, and then failed with a message about the browser and a
  recovery that told the caller to re-provision an install that was never
  broken. It is refused at the door now, naming the budget: `MAX_PATH` less the
  longest name anything puts inside a session directory. `CreateProcessW`'s
  `lpCurrentDirectory` bounds `output\`; SQLite's Win32 VFS bounds
  `browserai.data-shm`, which is longer, and is what actually failed first.

- 🐛 **A duplicate tool name in `tool-verdicts.json` exited naming neither the file nor the row.** A
  duplicate tool name inside one half of `tool-verdicts.json` exited the process
  with a message naming neither the file nor the row. The loader checked for a
  name in *both* halves and not for a name twice in *one*, so the frozen
  dictionary threw instead. It is a named refusal now — and the quieter half was
  worse: `TryGetProperty` answers the **last** duplicate, so a doctored file
  could have carried two verdicts for one tool and been read silently.

- 🐛 **`browserai_list` on a drive letter with nothing mounted on it answered "no sessions".** `browserai_list`
  on a drive letter with nothing mounted on it answered "no sessions" — true,
  and useless to a caller who typed the wrong letter. The empty answer now says
  whether the directory is there at all.

- 🔒 **Five `PLAYWRIGHT_MCP_*` variables are refused by name rather than merely absent.** Five
  `PLAYWRIGHT_MCP_*` variables are refused by name rather than merely absent,
  `ALLOW_UNRESTRICTED_FILE_ACCESS` first among them: it switches off the only
  containment this product has left, and the allowlist made it absent by
  construction while `ChildEnvironment.Refused` did not name it — which is
  exactly the difference that list exists to record.

- ✅ **A suite arm lost the machine-wide sweep gate to a BrowserAI another test started.** A
  suite arm that needed its own stray sweep to have run was losing the
  machine-wide gate to a real BrowserAI another test had just started, once in
  five full runs. The sweep's pass may now be handed a patience, the suite's own
  passes **wait** on the gate instead of asking again in a loop, and a scan over
  `src\` holds that the product never waits — ninety-nine peers queueing to redo
  one pass is the thundering herd the zero timeout exists to prevent. Reproduced
  deterministically by holding the gate from another process, which is also how
  the fix was watched.

- ✅ **The two literals the session guard is made of are now held by a test.** `LockFile.Hold`'s
  `FileShare.Read` is one writer per directory; the probe's
  `FileAccess.ReadWrite` is what a holder's share mode can refuse. Widening the
  first lets two BrowserAIs drive one profile and narrowing the second reports
  every driven session as free — and **both perturbations leave a file that
  opens**, so every behavioural test in the suite stayed green under each.

- ✅ **The re-verification index's gate now reads around a `previously "…"` clause.** The
  re-verification index's gate now reads around a `previously "…"` clause, the
  way the hazard index's already did. A superseded test name quoted the way
  `CLAUDE.md` requires — verbatim, in backticks — failed one gate and passed the
  other, so two rows of that index had been left quoting dead names *without*
  backticks and explaining the gate in prose. Both read as corrections again,
  and the clause has one definition both gates ask.

- 🐛 **`browserai_list` said `in use: no` about a session another agent was driving
  right now.** [Adversarial
  review F1](docs/reviews/2026-08-24-adversarial-since-the-mode-drop.md). Since
  2026-08-20 every forwarded browser call runs `SessionLock.Append`, which is
  `Rewrite`: the ownership handle is dropped at the top of the replacement and
  taken back at the bottom, with the per-directory gate held throughout. So a
  *busy* session's `browserai.json` is periodically **present and unheld**, and
  the bare probe reads that as a directory nobody has. The listing landed in
  that window and printed `no` — the one direction that costs a caller a session
  it was about to `browserai_destroy`. `SessionLock.ProbeLivenessUnderTheGate`
  asks the same question with that directory's own gate held at a **zero**
  timeout: the gate is the discriminator and cannot be wrong, and a gate it
  could not take is `UNKNOWN` with a reason rather than `no`. The zero timeout
  is what keeps the listing out of the queue `ProbeForHolder` was extracted to
  remove. **`browserai_catch_up` is gated too** — one opener, two callers, and
  two tools printing different `in use:` lines about one session in the same
  second is the failure that extraction's own remarks exist to prevent.
  *Corrected with it: `SessionLock`'s comment that "the gate is what makes the
  gap unobservable" (true of readers that take the gate, and one did not),
  `SessionManager.InUse`'s "through the pre-gate probe", `browserai_catch_up`'s
  "takes no lock" in four places including its model-facing description, and
  `README.md`'s "`no` when nothing held it at the instant of the look".* ⚠️
  **The per-entry cost figures in `ARCHITECTURE.md`, `SessionManager` and
  [kb](kb/windows/detection.md) are now incomplete and have NOT been adjusted**
  — a mutex create, acquire, release and close are in the per-entry cost and the
  create/close pair is **unmeasured**.
  `SessionListTests.ASessionWhoseGateIsHeldByAPeerIsReportedUnknownRatherThanFree`,
  planted red. The zero-timeout property could not be planted red at all — a
  120-second acquire would still return inside the rig's patience — so
  `SessionLockTests.TheListingProbeTakesTheGateWithoutWaitingForIt` is a
  source-level guard and says in its own remarks that it is the weaker thing.

- 🔒 **Page content could switch the artifact-pointer protection off.** [Adversarial
  review F2](docs/reviews/2026-08-24-adversarial-since-the-mode-drop.md). The
  provisioning answer-rewrite returned before `live.Artifacts.Complete`, so a
  call whose answer tripped it **pinned no name, recorded no artifact and
  carried no note** — and upstream builds the `Error`, `Page`, `Snapshot` and
  `Events` sections into **one** result, so an ordinary failed call against a
  live tab carries the page's own `<title>` beside the console and snapshot
  pointers. A page whose title quoted upstream's install advice therefore
  disabled the protection and the next sweep moved the file the answer had just
  linked to. That branch now runs `Complete` like every other answered call,
  with the child's own answer going in and the note appended to the node. **The
  scan is also gated on `isError`**, which upstream sets from the presence of an
  `Error` section and from nothing else — `sections.some(s => s.isError) ? {
  isError: true } : {}` beside `isError: title === "Error"`, read out of the
  resolved bundle and recorded in [kb](kb/playwright/configuration.md) — so the
  gate is lossless on the path the rewrite exists for and takes every ordinary
  answer out of reach of page text. *Corrected: `BrowserProxy.Remediate`'s claim
  that "on the paths where it appears at all the answer is already a failure
  with no bytes worth preserving", and `ProvisioningRemediation`'s "the rewrite
  fires only when the marker is present — every other answer, including every
  other error, goes through untouched".* ⚠️ **The gate does not close the bypass
  on its own and is not claimed to**:
  `ArtifactPointerTests.APointerSurvivesAnAnswerThatAlsoTrippedTheProvisioningRewrite`
  sets `isError` deliberately, so half 1 cannot make it pass.
  `ProvisioningRemediationTests.APageQuotingUpstreamsAdviceInASuccessfulAnswerIsForwardedUntouched`
  is the other, and both were planted red.

- 🐛 **Every failure to open `reinstall.lock` was reported as a reinstall in
  progress.** [Adversarial
  review F5](docs/reviews/2026-08-24-adversarial-since-the-mode-drop.md).
  `MaintenanceLock.TakeShared` caught `IOException` and
  `UnauthorizedAccessException` together and returned one bit, so an ACL denial,
  a full volume and a path that is too long all told the caller *"BrowserAI is
  replacing the browsers under '…' on this machine right now"* — complete with a
  progress clause counting from zero — and to wait minutes for a download that
  was not running. The kernel had already answered: a sharing violation on this
  open is a holder and **nothing else**, because the reader asks
  `Read`/`FileShare.Read` and a second reader is compatible with it. Both takes
  now carry a `MaintenanceDenial` and Windows' own message out, and
  `SessionErrors.TheBrowsersRootCouldNotBeClaimed` is a **new catalogue row**
  rather than a clause on the existing one: **two recoveries are two rows**, and
  waiting clears one and will never clear the other. It names the causes and
  refuses to pick one, because they are not distinguishable from a caught
  `IOException`. **`browserai_reinstall_browser`'s half is included**: a census
  of zero over a file nothing could open was concluding *another reinstall has
  it*, so the unreachable arm is answered before `LiveSessions()` is consulted.
  The contended sentence is deliberately left unhedged. *Corrected:
  `MaintenanceLock`'s catch comment that "`Describe` says which for the
  sentence" (it returns the last writer's line and nothing truncates the file
  when a reinstall ends, so it cannot), and `TheRootIsBusy`'s "if it finds none,
  another reinstall has it".* The catalogue census moves **27 → 28**.
  `ErrorCatalogueTests.AnInitThatCannotOpenTheBrowsersClaimIsNotToldAReinstallIsRunning`,
  planted red against a real ACL denial.

- 🐛 **A pinned artifact name was matched undelimited and never given back.** [Adversarial
  review F7](docs/reviews/2026-08-24-adversarial-since-the-mode-drop.md).
  `NoteWhatTheAnswerPublished` asked whether the answer *contained* each loose
  file's name, so `report.pdf` was pinned by an answer that only ever said
  `quarterly-report.pdf`, and a one-character name was pinned by any answer at
  all; and `_published` was monotone across **files** as well as calls, so a
  name pinned once pinned every later file that shared it, for the session's
  life, with **no answer naming it**. The match is delimited now, and a name is
  dropped once nothing loose in the output root carries it. **The review's own
  recommended fix — requiring the name to look generator-produced — was declined
  with the reason:** upstream publishes a pointer to a browser-initiated
  download too, `- Downloaded file <name> to "./<name>"` with the *site's*
  unprefixed name, so a prefix rule would move every real download out from
  under upstream's own pointer. That is verified in the resolved bundle and
  recorded in [kb](kb/playwright/tools-and-artifacts.md). *Corrected: the
  "substring rather than a parse" defence, which covered generated names and
  said nothing about the one artifact class this same file records upstream as
  not naming; and "Monotone on purpose … never removed".* ⚠️ **The residue is
  stated rather than closed** — a short, word-shaped name a page renders in
  prose is still delimited and still pins, and the harm is bounded to
  classification inside the session tree, with the absolute path in the note
  either way.
  `ArtifactPointerTests.AFileWhoseNameOnlyOccursInsideALongerOneIsStillSorted`
  and `.APinnedNameIsNotInheritedByALaterFileThatHappensToShareIt`, both planted
  red, with the control arm that upstream's own download pointer still resolves.

- 🐛 **A cancelled `tools/call` leaked its filename reservation for the life of the
  session.** [Adversarial
  review F8](docs/reviews/2026-08-24-adversarial-since-the-mode-drop.md).
  `_reserved` loses an entry only through `Release`, and every release site was
  on a path that **returns** — so a caller that cancelled a screenshot left
  `login.png` reserved forever, and the retry came back as `login-2.png` **with
  the answer reporting a rename that no file on disk justified**, which is the
  exact class this product exists to remove. `AnswerToolsCallAsync` is wrapped
  from the plan onward and the three release sites are folded into one
  `finally`. Releasing after a *successful* write is deliberate and safe:
  `Taken` is `_reserved.Contains(candidate) || File.Exists(candidate)`, so a
  file that is on disk holds its own name — and a file the caller later deletes
  stops holding a name it no longer occupies, which the reservation set alone
  could never give. A new `ProxyLog.ReservationReleased` at Debug is the only
  evidence the cancellation path leaves, because the SDK sends no frame at all
  for a request cancelled by `notifications/cancelled`. *Corrected:
  `ArtifactRouter.Release`'s summary, "for a call that never reached the child",
  and `ARCHITECTURE.md`'s "Never overwrite" paragraph, which was missing the
  clause that a reservation is given back **however** the call ends.* ⚠️ **The
  test asserts cancellation only and says so** — the idle-timer scope and the
  remediation regex's 1,000 ms match timeout are covered by the same `finally`,
  neither is deterministically reachable, and a test that provoked one by timing
  is the promptness assertion this suite forbids.
  `ArtifactRoutingTests.ACancelledCallGivesItsReservedNameBackSoTheRetryIsNotSuffixed`,
  planted red on the suffix.

- 🐛 **A torn log record is no longer possible.** A torn log record is no longer
  possible, and the machinery that made it possible is deleted rather than
  repaired. [Adversarial review finding
  9](docs/reviews/2026-08-18-adversarial-processes.md). `NativeFile.Append`
  looped on a short write, and `FILE_APPEND_DATA` atomicity is **per `WriteFile`
  call** — so the second call landed after whatever another of the ~100
  processes had written in between, the record was torn and interleaved, and
  every call returned success. Four directions were recorded for this and **none
  of them was taken**: all four repaired a loop whose premise was a lock-free
  design. Under a real lock there is no per-call size bound and nothing can
  interleave, so resuming a short write at the right offset is correct by
  construction and `RandomAccess.Write` does it. `OpenForAtomicAppend` and
  `Append` are gone; **no truncation and no new record-length limit were
  needed**, which is what every recorded direction cost.

- 🐛 **Nothing can unlink the live machine-wide log out from under its writers.** [Adversarial
  review finding 10](docs/reviews/2026-08-18-adversarial-processes.md).
  `FILE_SHARE_DELETE` let anything on the machine delete or rename it while a
  hundred BrowserAIs held it open, after which every write **succeeded** into an
  unlinked file object, `RollingFileWriter.CurrentFile` went on naming a path
  that no longer existed, and the writer's own catch never fired because nothing
  had failed. Delete sharing is now an argument each caller states: withheld for
  the machine-wide log, granted for a session's own `browserai.log`, which
  `browserai_destroy` must be able to remove under a live session. ⚠️ **The cost
  is stated where it is decided and was accepted knowingly: the MACHINE's
  central log — not one process's own file — cannot be deleted or renamed while
  any BrowserAI runs**, `SweepExpired` included, and that pass now tolerates the
  refusal instead of reporting it.

- 🐛 **A `browserai_destroy` racing a `browserai_set_purpose` leaked the session directory.** A
  `browserai_destroy` racing a `browserai_set_purpose` on one session leaked
  that session's directory for the life of the process. Nothing above
  `SessionManager` serialises tool calls — `_live` is a `ConcurrentDictionary`
  and is the only synchronisation there is — so two calls naming one session
  reach one `SessionLock` concurrently, which is the design rather than an
  accident. `Rewrite` tested `_disposed` and *then* took the per-directory gate,
  and `Dispose` disposed that gate underneath it: the rewrite re-opened
  `browserai.json` into a disposed lock, `_gate.Release()` threw, and for the
  rest of the process's life **every** `SessionLock.TryAcquire` on that
  directory answered `Held`, naming a pid with no session — while the destroy
  reported a partial failure blaming *"something still has them open"*. It is
  the one finding of [the 2026-08-18 adversarial locking
  review](docs/reviews/2026-08-18-adversarial-locking.md) whose failure does not
  heal: nothing releases that handle short of ending the process.

  **The fix is a per-session lock that every mutating path and both disposal
  paths hold for their whole body** — `Rewrite`, `Append`, `ReleaseAndDelete`
  and `Dispose`, including the caller delegates they invoke. The smaller change,
  taking the gate before the `_disposed` check, was **declined**: the disposal
  disposes the gate itself, so a rewrite blocked on it wakes holding a disposed
  object, and a check that races is still a race. It is not a fourth lock scope
  — `LockScopes` still names three machine-wide objects in one place, and this
  one has no name, no kernel object and no reach outside its instance.

  **The interleaving is forced rather than raced for, and the seams were already
  there.** `Rewrite` calls the caller's `update` delegate past the disposal
  check and under the gate; `ReleaseAndDelete` calls `delete` after the handle
  is closed and before the gate is released. Both tests place the second thread
  from inside those delegates, so no sleep, retry or stress loop is involved and
  the join can only go one way: against the defect the disposal contends with
  nothing and returns in microseconds, against the fix it cannot return at all.
  The first was watched red on all three of its assertions — the join, the
  `ObjectDisposedException` the rewrite threw, and **the leak itself**, a
  stranger's exclusive open of `browserai.json` refused after the session that
  owned it had been disposed.

- 🐛 **One junction above the install root made the stray sweep structurally blind.** One
  junction above the install root made the stray sweep structurally blind, and
  nothing distinguished that from a clean machine. Every path BrowserAI composes
  goes through `Path.Combine`, which never resolves a link;
  `QueryFullProcessImageNameW` answers with the path the object manager
  resolved, reparse processing already done. The comparison between them is
  *exact*, so on any machine with a relocated user profile, a redirected
  `AppData`, a `subst`ed drive letter or an 8.3 component above the root,
  **every process missed, on every pass, for good** — `candidates=0` forever,
  reported as a clean machine. The same mismatch emptied the live set
  `RevisionPrune` deletes a superseded browser tree on, which turned a race into
  a certainty: every superseded tree looked idle while browsers ran out of it.

  Measured rather than reasoned about, on 2026-08-24 and for the first time: a
  process launched through a real `mklink /J` junction is reported under the
  **target** spelling, having never named it
  ([kb](kb/windows/detection.md#a-process-reports-the-junctions-target-not-the-spelling-it-was-launched-by--measured-2026-08-24)).

  **What the sweep may match widened; what it may terminate did not**, and the
  two were kept apart deliberately because this code decides what may be killed.
  Only paths BrowserAI itself composed are ever resolved — never one a foreign
  process reported, so the process list cannot steer what gets opened. A
  resolved spelling names *the same file* the composed path already named, so
  the match stays exact, stays full-path, and is still never a prefix and never
  an image name. And every guard between a candidate and a kill is untouched:
  the second independent guard, the held process handle, the creation-time
  re-check and the `browserai.json` lock the sweeper has to be able to take
  itself.

  **The tripwire is the other half of the fix.** A pass now reports how many
  executables it watched, how many the filesystem spells differently, and — as a
  **warning**, not only a census number — every one whose spelling it could not
  establish at all. That is the sibling of `TitledWindows`, which exists for
  exactly this reason one column over: a pass that cannot match anything must
  never read like a machine with nothing on it.

  Two residues are named rather than left to be rediscovered. A **symlinked
  executable leaf** is still invisible, because what is resolved is the
  containing directory — opening a mapped image would end the ancestor walk with
  no answer at all on precisely the machine where a browser is running. And a
  root whose spelling cannot be established falls back to the composed one; the
  sweep says so, and the prune census has nowhere to say it.

- 🐛 **The instance directory's liveness rested on one child.** The instance
  directory's liveness rested on one child, and the blast radius was every
  session in the run. A run's instance directory holds the generated Playwright
  config of *every* live session, and exactly one process ever held it open: the
  surface child, which is given it as a working directory. Session children are
  given the session's own output root instead. So a surface child that died
  while the run kept serving left the directory unheld — and a directory's
  `GetLastWriteTimeUtc` does not move when files inside it are written, so five
  minutes later another BrowserAI's startup sweep renamed it aside and deleted
  it. Live sessions kept working, because their configs had already been read;
  every new one failed, and the run's own tidy-up then reported nothing at all,
  because a missing directory is deliberately never a failure.

  **BrowserAI now holds a marker inside its own instance directory** —
  `instance.live`, opened `ReadWrite`/`FileShare.Read` and held for the whole
  life of the process, the same mechanism the live-instance set and the browsers
  root's maintenance claim already use. It is taken by BrowserAI rather than by
  any child, so the signal no longer depends on one child staying alive, and the
  kernel releases it however the process dies. A sharing violation is a fact
  Windows enforces; a timestamp and one process's working directory were an
  inference. The sweep now says *this belongs to a BrowserAI that is still
  running* instead of *something refused my rename*, and the five-minute age
  guard, which used to cover the whole interval until a child started, now
  covers the two statements between creating the directory and marking it.

  **Found independently by both 2026-08-18 adversarial reviews** and carried as
  two hazard rows for five days before they were recognised as one.

- 🐛 **The pointers BrowserAI handed the model did not resolve.** The pointers
  BrowserAI handed the model did not resolve — two of them, and they were the
  same defect twice. Upstream writes two artifacts BrowserAI's inbound routing
  cannot reach, because neither comes from a `filename` argument: the **console
  log** and the **snapshot `.yml`**. It publishes a pointer to each *inside the
  answer* — a Markdown link to `./page-<stamp>.yml`, and `- New console entries:
  console-<stamp>.log#L1-L24` — and both are relative to the child's working
  directory, which is the output root. **BrowserAI's after-the-fact sweep moved
  both into typed folders**, so every one of those pointers named a file that
  was no longer there.

  **The console half compounded, because the file is still open.** Reproduced
  2026-08-20 against a real Chromium through the published binary: after the
  first sweep the child appended again, recreated the log at the output root,
  and the next sweep collided with the moved copy and landed it as `-2`. The
  answer then said `console-<stamp>.log#L25-L28` about a file with **24 lines in
  it**, while those four entries sat in `console-<stamp>-2.log` at *its* lines 1
  to 4. A third call produced `-3`. **Bare upstream does not have this** —
  nothing there moves the file.

  **The fix is a mechanical rule rather than a list of prefixes.**
  `ArtifactRouter.NoteWhatTheAnswerPublished` reads the child's own result
  before the sweep runs and marks every loose file whose name it mentions; the
  sweep then **records those where they are instead of moving them**, so the
  caller still gets the absolute path, the index still gets an entry, and the
  note says plainly that the file was left where the browser wrote it. A list of
  the two prefixes would have been right today and silently wrong the first time
  upstream published a pointer to a third.

  ⚠️ **The set is monotone, and that is the half a careless fix would miss.**
  The console log is named only in the answer that *creates* entries and in none
  of the answers that follow, so a set scoped to one call leaves the file
  movable on the very next call — which reintroduces the whole defect. Both
  tests were planted red first and reported the exact symptom above:
  `ArtifactPointerTests.EveryPointerARealChildPublishesResolves` drives a real
  browser and checks every `#Lx-Ly` against the lines of the file it names, and
  `.ANamedFileSurvivesEveryLaterSweepAndAnUnnamedOneIsStillSorted` holds the
  monotone half and carries the control — a download nothing named is still
  sorted, so this is a rule about pointers rather than the sweep being switched
  off.

- 🐛 **755 stale `.live` markers had accumulated, and nothing ever reclaimed them.** 755
  stale `.live` markers had accumulated, because the only code that reclaimed
  them ran somewhere nothing ever reaches. Reclaim lived inside the updater's
  *am I alone?* census, which `UpdateService` calls only after an update has
  been found **and** downloaded — which had never once happened on the machine
  this product is developed on. Two days of ordinary work left 755 unheld files
  in `%LocalAppData%\BrowserAI\live\`, and every census that ever did run would
  have had to open all of them.

  **Reclaim is now a routine of its own and runs from two places, both with the
  same mutex discipline.** `LiveInstances.ReclaimStaleMarkers` takes the same
  per-root gate a join and a census take, at **zero timeout**: one process
  reclaims and every other pays an acquire and leaves. It runs from the stray
  sweep — already machine-wide, already mutex-serialised, already skipping
  instantly when a peer holds its own gate — and from **startup**, on a
  background thread, so that a sweep declining to run for reasons that have
  nothing to do with markers cannot cost a machine its reclaim. **Nothing waits
  for it**, and it is deliberately not folded into `LiveInstances.Join`'s hold:
  walking 755 markers inside a five-second-gated critical section that a hundred
  starting processes queue on is how a join times out, and a process that could
  not join is invisible to a peer's census.

  ⚠️ **A marker is stale only when it is NOT HELD; existence is not held-ness**
  — the same rule `MaintenanceLock` and `SessionLock` state about their own
  files. Reclaiming a live instance's marker would make that instance invisible
  to every later census and therefore killable by an apply, so the negative is
  proved with a positive control rather than argued:
  `UpdateTests.AHeldMarkerSurvivesTheReclaimAndTheSameMarkerGoesOnceItIsReleased`
  holds one marker open, runs the reclaim, requires it to survive, releases it,
  runs the reclaim again and requires it to go — so a pass that removed nothing
  at all could not pass either half.
  `StraySweepTests.TheSweepReclaimsStaleLiveMarkersAndLeavesAHeldOneAlone` does
  the same through the sweep.

- 📝 **The mode table claimed a persistence property the code has never had.** `README.md`'s
  third column read *"Stored credentials — No / No / Yes"* and `SessionMode.cs`
  described `interactive` as *"a human can type a password this session will not
  keep"*. **All three modes persist.** `BrowserConfiguration. ForSession` writes
  `browser.userDataDir` as `<session>\profile` in every mode and never writes
  upstream's `isolated` key, so cookies and `localStorage` survive a
  `browserai_resume` in a `headless` session exactly as they do in a
  `persistent` one. `storage` is a **tool filter, not a persistence switch** —
  it decides whether the 17 cookie, `localStorage` and `storageState` tools
  exist in that session's child at all. The correction points the safe way: a
  caller who believed a mode discarded credentials would leave a signed-in
  profile behind thinking it had not.

- 📝 **Two doc comments claimed a refusal that has not existed since 2026-08-18.** `BrowserConfiguration.UnionCapabilities`
  and `BrowserProxy.AnswerToolsListAsync` both ended *"a call that its session's
  mode does not permit is refused at call time instead"*. The `(tool, mode)`
  matrix was removed; what replaced it is the child's own capability set, so
  such a call is **forwarded** and upstream answers that the tool does not
  exist. Both sentences survived the removal by describing a fallback that had
  gone.

- 🐛 **The reclaim pass had a bullet with no input for three days, and it read as
  though it worked.** The
  suite's own specification asks that *anything the previous run recorded is
  terminated by `(pid, creationFileTime)` from its own spawn record*. Nothing
  wrote a record. So a run killed mid-test left a process the next run could not
  identify — only a directory it could not delete, which surfaced as a locked
  file and named the wrong cause. `SpawnRecord` writes `.work\spawn-record.txt`
  from the two places the harness starts processes, and the pass reads it
  **before** it touches the tree, because a live process is what holds the files
  a delete cannot take. Identity is checked again before anything is acted on,
  and the regression test's middle case is the test host's own pid with a
  deliberately wrong creation time — a reclaim that matched on the number alone
  would end the run rather than fail it.

- ✅ **The failed-rewrite recovery of `lock.json` has a test.** A rewrite drops
  the handle before the replacement, because Windows will not rename over a file
  this process holds — so an exception in between left the session *silently
  unowned* while the caller was told only that a write had failed. It shipped
  that way once. The seam turned out to be neither of the two `TODO.md`
  predicted: not an injectable file operation and not a probe process holding
  the replacement path, but an **ACL** — denying `CreateFiles` on the session
  directory stops `WriteDurably`'s temp file ever existing, so the rewrite fails
  before any rename, deterministically, with nothing injected. Ownership is then
  asserted from the kernel rather than from the object under test: a stranger's
  write-access open of `lock.json` is refused while, and only while, a handle is
  really there. The second arm denies read as well, so the recovery fails too,
  and requires the answer to say the session no longer holds its directory
  rather than hand back something that reports ownership it does not have.

- 🐛 **The two ungated `lock.json` readers that acted on an absence are closed.** The
  two ungated `lock.json` readers that ACTED on an absence rather than reporting
  it are closed, one pass each. An adversarial review enumerated thirteen
  readers that take no lock; eleven fail safe. The other two both read the
  instant in which `lock.json`'s *name is unbound* while another process renames
  a new record over it.

  **`SessionIndex` deleted a live session's index entry.** A `null` record was
  `NotASession`, which `IsRemovable` includes, so a sweep dropped a session that
  was doing nothing worse than setting its own purpose — and nothing re-asserts
  an entry, so it stayed invisible to `browserai_list` for the rest of its life.
  `SessionIndex.Absent` now looks for the durable write's own temp file beside
  the gap and answers `RecordInFlight`, which is not removable. **The signal is
  positive rather than a timing guess:** the temp is created before the rename
  and deleted after it lands, so it is on disk for the whole window. It cannot
  fail dangerously — a temp left by a dead writer keeps an entry one sweep
  longer.

  **`browserai_init` could rebind a closed session's browser family.**
  `SessionManager.Existing` read `null` as *free, proceed*, and the reclaim
  downstream takes `mode` and `browser` from the request — so an `init` landing
  in the window bound a Firefox session's record to Chromium over the profile on
  disk. `SessionLockRequest.RefuseAnExistingRecord` now asks the same question
  inside `TakeOrReport`, where the record has already been read under the gate
  and a peer replacing one is holding that gate.

  **The ungated look deliberately stays**, which is where this and the hazard
  row's prescribed remedy part company: it is what gives `init` the *same*
  refusal for a lost session, a neatly closed one and one this very process has
  open. Moving it inside would let the pre-gate probe answer first for a live
  session with a shorter sentence about who holds the file — a regression
  `InitAsync`'s own comment records having been made once and reverted.

- 🐛 **Two BrowserAI processes wrote holder statements into one `lock.json`.** Two
  BrowserAI processes wrote holder statements into one `lock.json`, and what let
  them was a peer's *probe* — the handle it holds while it looks. The probe
  opens `lock.json` `FileAccess.ReadWrite` in front of the per-directory gate,
  which is what makes it a sound ownership test; the same access is what an open
  sharing only `Read` is refused by. So a contender passing over the file in the
  microseconds between a writer's rename and that writer's own re-open refused
  the re-open, and the writer gave up a directory whose record already named it.
  Observed in CI on 2026-08-19, run 32203064556 attempt 1, on a 4-core hosted
  runner: `Reclaimed taken=true holderPid=2652` from one contender and
  `Unreadable taken=false` from 2652 itself, 61 ms apart, with `holder` and
  `purpose` each carrying two statements.

  **It is fixed on the gated side because it provably cannot be fixed on the
  probe's.** To be refused by a holder a probe must ask for access outside
  `Read`, and a handle whose granted access is outside `Read` is exactly what an
  open sharing only `Read` is refused by — *detecting an owner and blocking one
  are the same capability*. `SessionLock.ReopenHeld` now takes the three opens
  that follow this class's own write (`TakeOrReport` after `WriteDurably`,
  `Rewrite` after the same, and `Reclaim` after a rewrite that threw) through
  `RenameWindow.WaitOutWhereNoOwnerIsPossible`, which waits a sharing violation
  out as well as a delete-pending destination. **The licence is a precondition,
  not a guess:** each of the three holds the gate and has just written a record
  naming itself, and becoming an owner means passing through `TakeOrReport`,
  which needs that gate. Still bounded at `RenameWindow.Budget` — a handle that
  outlasts thirty seconds is a different fault and is still reported.

  **The two ownership tests are deliberately unchanged**, and one of them meets
  the same handle: `TakeOrReport`'s open *before* the write can be facing a real
  owner, so it may not wait, and a peer's probe there still reads as
  `Contended`. That is narrower — a wrong sentence rather than a wrong owner,
  correct again on the next call — and it is now a row of its own in the [hazard
  index](HAZARDS.md#hazard-index) rather than an unstated residue.

- ✅ **The suite was red from Git Bash and green from PowerShell on the same commit.** The
  suite was red from Git Bash and green from PowerShell, on the same commit, and
  now the wrong comparison is red in both. Two assertions in
  `SessionDirectoryGuardTests` compared a path composed in the test host against
  the accepted spelling inside a refusal, which comes back through
  `GetFinalPathNameByHandleW` — and Windows always answers with an
  **upper-case** drive letter while a process keeps whatever casing its shell
  gave it (`C:\…` from `pwsh`, `c:\…` from Git Bash). Measured at `cc45900`:
  total 484, **2 failed from Git Bash and 0 from PowerShell**.
  `Sessions\CLAUDE.md` predicted this exact defect and recorded that nothing
  asserted it; that is no longer true.

  **Eleven sites carried the shape, and all eleven were test assertions** —
  eight in `SessionDirectoryGuardTests`, one each in `HeadlessBinaryTests`,
  `StraySweepTests` and `SessionPathTests`, against paths read back through
  `GetFinalPathNameByHandleW`, `QueryFullProcessImageNameW`,
  `GetShortPathNameW`, `QueryDosDeviceW` and `GetCurrentDirectoryW`. Product
  code had none: every path comparison in `src\` upper-cases both sides or asks
  for `OrdinalIgnoreCase`, and the one deliberate ordinal path comparison is
  `ArtifactRouting.PrefixOf`, which separates names the product generated from
  names a caller chose. One of the eleven was choosing a *branch* rather than
  passing an assertion: `An83Spelling…` decided whether this volume generates
  short names by comparing `GetShortPathNameW`'s answer ordinally, so a
  re-spelled drive letter would have sent it down the arm that then asserts a
  tilde.

  **The mechanism is `DriveLetterCase`**, over which six of that class's tests
  are parameterised: the `Lower` arm composes a spelling **no Windows API ever
  returns**, so a comparison that is not case-insensitive fails on every machine
  and in every shell. It was planted and watched red *from PowerShell*,
  reproducing the two Git Bash failures exactly. ⚠️ **CI runs `pwsh` end to end
  and therefore cannot see the shell-dependent form of this at all** — which is
  why it has been reported twice from a machine and never once from a build.

- 🐛 **A durable write that landed said *nothing was changed*.** A durable write
  that landed and could not be re-opened said *nothing was changed*, which was
  false at the moment it was said. `SessionLock.TryAcquire` closes its handle on
  `lock.json`, renames a fully-formed record over the name and re-opens it — and
  the write and the re-open shared one `catch`. So a failure on the second one
  answered *"the directory was not taken and nothing was changed"* about a
  machine where `lock.json` had just been replaced with a record naming this
  process as the holder. A caller acting on that sentence reads the reclaim it
  meets on its next call as somebody else's crashed session rather than as its
  own last attempt. Now one `catch` per operation: a write that never landed
  still says nothing was changed, and one that did says the record **was**
  written, who it names, that nothing holds the directory, and what the next
  call will therefore report. **It deliberately does not try to undo the write**
  — restoring the previous record means a second write along the path that just
  refused us, and the answer would then have to describe a rollback that half
  happened.
  `SessionLockTests.AWriteThatLandedSaysSoAndOnlyAWriteThatDidNotSaysNothingChanged`
  holds both arms and provokes each with an ACL denying exactly one right,
  rather than with a fault-injection seam in shipped locking code. This is the
  second, separable half of the interleaving recorded below; **the window itself
  is untouched and still open.**

- 📝 **Not fixed, and recorded loudly: two processes appended to one `lock.json` in CI.** Not
  fixed, and recorded loudly: two BrowserAI processes appended holder statements
  to one `lock.json`, in CI, on 2026-08-19. This is the interleaving the
  2026-08-18 adversarial review predicted from reading and which nothing had
  ever produced. Run 32203064556 attempt 1, 16 contenders on a 4-core hosted
  runner: 2652 acquired and wrote its record, 696 reclaimed the same directory
  **61 ms later**, and 2652 returned `Unreadable` with the sentence *"the
  directory was not taken and nothing was changed"* — while the record on disk
  carried its holder statement and its purpose. It did not reproduce in six
  consecutive local full runs and a CI re-run of the same commit was green.
  **The fix is already specified in [`TODO.md`](TODO.md) and is deliberately not
  being made in this pass**, because it restructures the refusal on the
  most-exercised path in the product. A second, separable and cheaper defect was
  visible in the same evidence: `SessionLock.TryAcquire` put the durable write
  and the reopen in one `catch`, so a failure *after* a successful write claimed
  nothing changed. ⚠️ *Corrected 2026-08-19 (previously "A second, separable and
  cheaper defect **is** visible …" with no fix): that half **is now fixed** —
  see the entry below — and only the window itself is still open.* Both halves
  have a [hazard row](HAZARDS.md#hazard-index).
  `SessionLockTests.UnderConcurrentProcessesExactlyOneAcquiresAndEveryOtherIsToldWho`
  caught it, and it was diagnosable only because a whole-set dossier was added
  to that test on 2026-08-18 for exactly this occasion.

- ✅ **A probe wrote its report in place, so `File.Exists` became true mid-write.** A
  probe wrote its report in place, so `File.Exists` was a readiness signal that
  became true in the middle of the write. Found on 2026-08-19 by running the
  whole suite three times in a row to prove it green: one run failed with *"the
  process cannot access the file … because it is being used by another
  process"*, and the failure named
  `BrowserIdleTimerTests.KillingTheClientTearsTheSessionDownWithoutWaitingForEof`
  rather than the harness. ⚠️ **The sharing violation was the lucky half** — a
  reader arriving one instant later would have parsed a truncated report and
  failed on an assertion about the product. `ClientProbe` now writes a
  `.writing` sibling and publishes it with a retried rename, which is the
  convention this project's other two probes already followed and documented,
  and the test reads through `ProbeReport.ReadAsync`, which exists for exactly
  this. It was the last unguarded reader — `ProbeChild` and
  `SdkStdioClientTransportTests` already caught both failures and retried. The
  hazard has a row of its own, closed by the same commit.

- 🐛 **A screenshot comes back inline again, and the defect was ours.** Upstream
  answers `browser_take_screenshot` with an `image` content block as well as a
  file, guarded by `if (!params.filename)` — and BrowserAI's artifact routing
  always supplies a `filename`, to give the artifact a name a human can read a
  month later. So **the guard was never true and no screenshot came back inline
  in any mode**, where bare `@playwright/mcp` returns one: the model paid an
  extra file read on the most-used artifact tool, on every call, and nothing
  anywhere reported the difference.

  The block is now appended to the same answers this build already rewrites,
  **read back off disk after the child wrote it** — the same bytes a reader
  following the path in the note would find, with upstream's own
  `image/<fileType>` media type — under the caller-visible condition upstream
  tests: *the caller named no file*. The legible filename is kept; the two were
  never actually in tension.

  **`browser_take_screenshot` and nothing else.** `registerImageResult` has
  exactly one call site in the whole resolved bundle and that is it.
  `browser_pdf_save` generates a name in exactly the same way and gets no image
  block: a PDF is not an image, upstream never registered one, and no client is
  obliged to render `application/pdf` in an image block.

  **No size threshold, because upstream has none** and because a byte count is
  the wrong axis anyway. Measured off the wire against the published binary: the
  same 1280×720 viewport costs 5,105 B on a near-blank page and 52,648 B on one
  with two dozen paragraphs — a 12× spread on the wire — while the model-side
  cost is `⌈w/28⌉ × ⌈h/28⌉` = **1,196 visual tokens either way**. A gate on
  bytes would fire on the page that costs nothing extra and stay silent on the
  one that does
  ([kb](kb/playwright/tools-and-artifacts.md#the-inline-screenshot-and-what-it-costs--measured-2026-08-18)).

  One divergence is recorded rather than fixed: upstream shrinks anything over
  1,568 px on a side or ~1.15 MP first, and BrowserAI sends what is on disk.
  Matching it would mean a PNG/JPEG/WebP resampler inside the proxy, which is
  the scope boundary's own example of what this product must not grow.

- 🐛 **Closing the process log did not close its file.** Disposing the logging
  stack was written to close the rolling log handle through the logger factory,
  and the factory never disposes a provider it was handed rather than asked to
  create — so the handle survived, measured at zero disposals. It cost nothing
  while the only caller was a process about to exit, and it surfaced the moment
  something short-lived opened a log and read it back. The writer is now closed
  explicitly, and a test fails if that call is removed.

- 🐛 **A second BrowserAI starting up deleted a running one's working files.** The
  per-run instance directory holds the generated config and the surface child's
  browser profile, and the startup sweep reclaimed abandoned ones with
  `Directory.Delete(path, recursive: true)` on the belief that Windows refuses
  to delete a directory a live process is sitting in. Windows refuses to remove
  the *directory* and does not refuse to delete the *files inside it*, so the
  sweep emptied a live run's directory completely, failed on the empty node that
  was left, and swallowed the exception — every start, against any instance more
  than five minutes old, with nothing written anywhere. The liveness check is
  now a rename, which is refused with the contents untouched and is also an
  atomic claim between two BrowserAIs sweeping the same root.

- 🐛 **An instance directory that would not go now says which file held it.** The
  same delete went through the framework primitive, which reports one node out
  of however many survived, inside a catch that discarded even that. It now uses
  the product's one recursive delete — post-order, per node — and logs every
  survivor by name at Warning. The next run's sweep still tries again; what
  changed is that a leftover is attributable.

- 🐛 **A `session.json` that could not be written was silent.** A `session.json`
  or a session roll-up that could not be written was silent — while the same
  answer named its path. Both writes are best-effort by design, because a virus
  scanner holding a file open must not turn a screenshot that was taken into a
  screenshot that failed. Both discarded the answer that said whether it
  happened, so a caller was handed the path of a file that might be stale or
  absent. The answer now says so, in the line that names the file, for the
  artifact index and for the per-root roll-up in `init`, `resume` and `destroy`
  alike.

- 📦 **Two licences that had to travel with the binary were not travelling.** `ModelContextProtocol`
  and `ModelContextProtocol.Core` are Apache-2.0 and seventeen
  `Microsoft.Extensions.*` assemblies are MIT; all nineteen are compiled into
  `BrowserAI.exe` exactly as Velopack is, and a NuGet package's licence stays in
  the machine's package cache — it is never copied to a publish output, so
  *linked in* and *its notice ships* are independent facts and the second was
  false for both. Apache-2.0 §4(a) requires a copy of the licence to reach every
  recipient, which is stricter than MIT's notice clause rather than looser. All
  of it now ships in `THIRD-PARTY-NOTICES.txt` beside the binary. Upstream's MCP
  `LICENSE` turned out to grant **three** licences rather than one — Apache-2.0,
  MIT for contributions whose authors never consented to relicensing, and
  CC-BY-4.0 for documentation — so it is reproduced whole, and its Apache half
  **ends at *END OF TERMS AND CONDITIONS* and omits the appendix its own §4
  points at**, which is upstream's file as published and is left unaltered.
  **The `Microsoft.Extensions.*` list is derived from `packages.lock.json`
  rather than typed**, so a package entering the closure on a later bump is a
  red build here rather than a licence nobody noticed had arrived.

- ✅ **A suite run that exercised nothing reported exactly what a real one
  reports.** With
  the whole publish directory moved aside — no binary, no browser ever started —
  the suite returned `329 total · 328 succeeded · 1 skipped · exit 0`, character
  for character a healthy run's summary, because thirty-five guards returned
  early after asserting something weaker and every one reported as a pass. They
  now report as **skipped**, so the run's own counts differ; every run ends with
  a block naming what it did and did not exercise; and with
  `BROWSERAI_RELEASE_RUN=1` a missing capability is a **failure** naming the
  command that produces it. This was the project's founding failure class living
  inside its own release gate.

- 📦 **37 MB of every release was a zip nobody reads.** The published artifact
  carried `payload\.cache\node-<ver>-win-x64.zip`, the download cache the
  payload build keeps so a re-run does not re-fetch Node. Excluding it took the
  full package from **85,348,009** to **49,043,498 bytes** — 42.5% of every
  download, for a file that is never opened at runtime and compresses to nothing
  because it is already compressed.

- ✅ **`UseSystemResourceKeys` is asserted rather than merely set.** It strips
  the framework's exception message strings, and this product's error text is
  read by a model deciding what to do next. The property was correct and guarded
  by nothing, which is the state a size optimisation walks into.

- 🐛 **Every session's `lock.json` would have recorded the wrong version.** The
  build stamp was read from the assembly version, which the versioning mechanism
  fixes at `{Major}.0.0.0` by design — so the whole 0.x line would have written
  `0.0.0.0`, and the whole 1.x line `1.0.0.0`, into the one record a support
  question starts from. It now records the version the build was actually
  derived as. Measured on the artifact rather than reasoned about: at `v0.1.0`
  the assembly version really is `0.0.0.0`.

- 📝 **Six stale sentences are corrected and one new hazard is written down.** The
  `--shortcuts None` claim in [`HAZARDS.md`](HAZARDS.md) had been false since the
  Start Menu entry landed; [`RELEASING.md`](RELEASING.md) still told a maintainer
  to **uninstall their working BrowserAI** before cutting a release, which stopped
  being necessary the day the suite's pack got its own id; `kb/mcp/protocol.md`
  named the registered path as `current\BrowserAI.exe` and said there was
  *nothing to correct* on an update, both of which the two-binary split made
  false; and `BrowserAI.exe --sweep` — named in the server's own source, in
  `kb/windows/detection.md` and in re-verification row 78 — **opens a window**
  now instead of measuring anything, so that procedure was broken rather than
  untidy. `kb/packaging/velopack.md` gets the same treatment in two places. The
  new hazard is residue nothing owns: every browser this product launches leaves
  a JSON descriptor in `%LOCALAPPDATA%\ms-playwright\b\`, and **26,891 of them
  (42.6 MiB) have accumulated since 2026-08-14** from running the suite. There is
  no environment variable that moves that directory, so it is recorded with what
  is owed rather than guessed at.

- ✅ **The app's embedded manifest and its apartment are asserted off the binary
  that ships.** Two properties the whole window depends on were read once, by
  hand, and then trusted. The **`Microsoft.Windows.Common-Controls` 6.0.0.0**
  dependency is the one whose absence makes `TaskDialogIndirect` fail at run time
  with **no compile-time signal of any kind** — the loader binds version 5, the
  export is absent, and it presents as *the app starts and nothing happens*. It
  is now read out of `RT_MANIFEST` id 1 of the built and published binaries,
  together with `longPathAware`, `PerMonitorV2` and `asInvoker`, with the server's
  own manifest as the control so a reader that stopped finding resources cannot
  look like a binary that declares less. And **`[STAThread]` under NativeAOT was
  an assumption**: the folder picker's `BIF_NEWDIALOGSTYLE` silently falls back to
  the pre-Vista dialog off an STA and the version 6 common controls expect one, so
  `--report` now writes the apartment and an arm runs the **published** binary and
  requires it. **Measured 2026-09-16 at .NET 10 / ILC 10.0.12: it is `STA`.** The
  report's schema is 2.

- 🐛 **The installer's own variables are cleared after Velopack has read them,
  not before.** `VELOPACK_FIRSTRUN` and `VELOPACK_RESTART` are cleared so that
  no child of the configuration app inherits them — click *Register* with
  `VELOPACK_FIRSTRUN` still set and `claude.exe` starts carrying it, and so does
  everything it starts, including the MCP server, which exits 0 on that variable
  by design. But the clearing ran **before** `VelopackApp.Run()`, and `Run()`
  decides whether to invoke `OnFirstRun` and `OnRestarted` by reading exactly
  those two variables — so both callbacks were unreachable in this binary: two
  log lines that could never be written, with a remark beside them describing
  behaviour that did not happen. It runs after `Run()` now and still before
  anything is started, which the hook path never reaches at all because `Run()`
  exits the process when it serves one.

- 🔧 **The dialog's icon is loaded at the dialog's DPI instead of at the classic
  size.** `LoadIconW` has no size parameter: it answers the 32×32 image out of
  the group, which a Per-Monitor-V2 process then draws **stretched** — on a 200%
  display, thirty-two pixels blown up to sixty-four, beside text that is not.
  The icon ships larger images, so the load is `LoadIconWithScaleSize` at
  `SM_CXICON` for the window's own DPI, with the unscaled load kept as the
  fallback it has always been. The DPI is the window's where there is a window
  and the system's on the first page, which is built before the window exists —
  so a dialog dragged to a second monitor comes back at that monitor's DPI. The
  loading call is `LoadImageW`, **not** `LoadIconWithScaleSize`: the comctl32
  function the documentation points at for this is exported **by ordinal only**,
  and naming it in a `LibraryImport` fails at the call with
  `EntryPointNotFoundException` from inside `Show()` — which takes the window
  with it. That was found by the gate, not by review: the published app exited
  `0xC0000409` and left the reason in its own process log.

- 🐛 **The release-notes generator refuses the two shapes it used to crash on or
  drop.** An entry written above a section's first `### ` heading made
  `$group` null and `Set-StrictMode` turned `$null.Entries.Add(…)` into *"The
  property 'Entries' cannot be found on this object"* — a stack trace naming a
  variable nobody reading a changelog has heard of. A paragraph written *under*
  a group heading was worse: neither preamble nor entry, it was silently dropped
  from the body. Both are refused now, in the script's own words, naming the
  line. Refusing rather than carrying the paragraph is the choice: prose already
  has a place — the section preamble, above the first heading, which the body
  does render — and inventing a rendering for a shape nothing else reads would
  widen the format past what `ChangelogTests` holds it to.

- 🔧 **A wait on the client's handle that cannot be interpreted now records the
  value it got.** `WaitForSingleObject` answers one of four things and only
  `WAIT_FAILED` sets a last error; `WAIT_ABANDONED` reaches the same branch
  carrying whatever error was left in the thread, which can read as *The
  operation completed successfully* — a sentence that looks like a defect in the
  logging rather than a state of the client. The record carries the raw value
  beside the message now, under its own event id, so the two are distinguishable
  after the fact.

- ✅ **A release date set at the cut is reported as a heading change, not as a
  rewritten record.** A sealed record starts at its own heading, so
  `## [1.0.0] - 2026-09-15` is inside the 281,709 characters
  `AppendOnlyRecordTests` seals — and setting the real date at the cut breaks
  the seal. The failure said *REWRITTEN … a dated record says what was true when
  it was written*, which is the right sentence for a sweep and exactly the wrong
  one for the one edit [the checklist](RELEASING.md) requires: it reads as
  *revert this*. Each seal now carries a second digest, of the same prefix
  **without its first line**, so the test can tell the two apart and say **the
  HEADING LINE changed and nothing else did** with the seal line to paste. A body
  edit under an untouched heading still says *REWRITTEN*. `RELEASING.md` says the
  same in item 10 and in the order section: the date change and the re-seal are
  one commit.

- 📦 **A release cut over a local feed still holding this machine's gate packs is
  refused.** `Releases/` is where every gate pack lands, and a gate pack is cut
  at whatever MinVer derives from a commit past the tag — so between releases
  the local feed holds `1.0.1-alpha.0.19`, `1.0.1-alpha.0.2` and a manifest
  naming them. Cutting `1.0.0` against that is *lower than the published
  version*, and `build/Test-ReleaseVersion.ps1` called it a **rollback** and
  advised `-RollbackRepublish` — which would have published a release into a
  feed whose manifest and asset list name packages nobody ever released. It now
  refuses a **release** candidate whenever the local feed's highest version is a
  **pre-release** newer than it, and the refusal names the four files to delete
  — `*.nupkg`, `releases.win.json`, `RELEASES`, `assets.win.json` — and the two
  directories that must survive, `archive/` and `test-pack/`.
  [`RELEASING.md`](RELEASING.md) step 5 says the same. The rule is narrow by
  construction: a genuine rollback over published releases still reads as one, a
  pre-release gate pack over the same directory is still monotonic, and so is a
  release over only older pre-releases.

- 🔧 **The update check runs off the UI thread, under the same deadline the
  server uses.** `CheckAsync` and `DownloadAsync` were called with
  `.GetAwaiter().GetResult()` **inside the dialog's callback**, with
  `CancellationToken.None`. `VelopackUpdateClient` builds its `UpdateManager`
  from a bare URL, so Velopack's own `SimpleWebSource` supplies the `HttpClient`
  timeout — **thirty minutes** — and a feed that answered slowly froze the
  window for that long: no repaint, no cursor, no close button. The server's
  lane wrapped the identical calls in `UpdateService.CrashTripwire` from the day
  it was written; this one had nothing. Both now run on the thread pool under
  that same constant, and the dialog enables `TDF_CALLBACK_TIMER` and polls on
  `TDN_TIMER` — so it shows *Checking for updates…*, stays fully usable, and
  redraws itself with the answer, or with *the update check did not finish
  within N seconds and was stopped* if the deadline passes. The deadline is
  enforced by the poll rather than by the token, because
  `UpdateManager.CheckForUpdatesAsync` takes no token at all: what ends is the
  **waiting**, and the orphaned request finishes into nothing.

- 🐛 **The folder picker is owned by the dialog, and an unresolvable folder
  is no longer a silent cancel.** Two defects in one button. The picker was
  opened with a **zero owner**, because the dialog's window was private — so it
  was modal to nothing: the task dialog's command links stayed live underneath
  it, a second click re-entered the command handler, and a `TDM_NAVIGATE_PAGE`
  from there rebuilds the page out from under a modal child. `TaskDialogHost`
  exposes its window now and the picker is given it. Separately,
  `SHGetPathFromIDListW` has **no length parameter** — it assumes `MAX_PATH` and
  answers `FALSE` for anything longer — and the path was being written into the
  same 260-character buffer the shell was given for the display name. The picker
  then answered `null`, which the caller read as *cancelled*, so choosing a deep
  folder closed the picker, wrote nothing and said nothing. The call is
  `SHGetPathFromIDListEx` into a 32,768-character buffer, and the answer is three
  states rather than a nullable string: picked, cancelled, or failed with a
  sentence the dialog puts in its note.

- 🐛 **A click that throws no longer takes the whole window with it.**
  Every action the configuration app offers runs inside the task dialog's
  `[UnmanagedCallersOnly]` callback, and **an exception out of one of those is a
  `FailFast`, not an exception**: the runtime cannot unwind into native frames,
  so the process is terminated where it stands — the window vanishes mid-click
  with no dialog, no log line and no exit code anything could read. Three calls
  reachable from a click could produce one: `Directory.CreateDirectory` for the
  log directory, `Path.Combine` outside the registry reader's own `try` when
  `CLAUDE_CONFIG_DIR` holds an invalid path, and `Path.GetFullPath` on a picked
  project directory. The three delegate invocations are now inside one
  `try`/`catch`, which records the failure to the process log, puts it in the
  dialog's note, re-renders, and returns the `S_OK`/`S_FALSE` the notification
  requires — so the window stays open and says what happened. The dispatch was
  lifted out of the unmanaged entry point to make any of this assertable:
  `Callback` resolves the instance and forwards to `Dispatch`, because an
  `[UnmanagedCallersOnly]` method cannot be called from C# at all and a decision
  written inside one is a decision no test can ever reach.

- 🔒 **Neither an install nor an uninstall touches a `browserai` entry it did
  not write.** Three sentences in this codebase said
  BrowserAI *"neither adopts, overwrites nor deletes"* an entry belonging to
  another install — `RegistrationOwnership`'s own summary,
  `AppState.MayRemove`'s remark, and the registration row in
  [`DECISIONS.md`](DECISIONS.md) — and **one intent out of three was keeping
  them.** `Repair`, the update hook's path, read the client's configuration and
  refused what it did not own; `Reassert`, the **install** hook's, ran
  `mcp remove` and then `mcp add` with no check at all, and `Remove`, the
  **uninstall** hook's, ran `mcp remove` unconditionally. So installing this
  BrowserAI beside another one deleted that one's registration and wrote its own
  over the top, and uninstalling this one deleted the other's outright.
  `McpRegistrar.Apply` now reads `McpRegistryView.User` before **every** intent
  and takes the two refusals — a configuration nobody could read, and an entry
  outside this install root — in one place for all three. Absent, ours-and-stale
  and ours-and-present behave exactly as they did. There is no exit code on that
  path and that is deliberate: these run inside Velopack fast-exit callbacks,
  where a non-zero result fails somebody's install, so what carries the outcome
  is `isWhatWasAskedFor: false` in `mcp-registration.json` with the foreign path
  named in the detail, plus a warning in the installer's own log.

- 📦 **The suite's installer is titled `BrowserAI (suite)` and no longer
  owns the real Start Menu entry.** The two packs were split by pack id on
  2026-09-15 to stop the suite's installer arm rewriting and then deleting the
  real install's Add/Remove entry — and the **title** was left shared, which is
  the name Velopack actually gives the shortcut (`shortcuts.rs`, read at 1.2.0:
  the link file is `<title>.lnk`, never `<packId>.lnk`). Shortcut creation is
  not gated on `--silent`, and the uninstall removes shortcuts **by target**, so
  the arm wrote `%APPDATA%\Microsoft\Windows\Start Menu\Programs\BrowserAI.lnk`
  pointing at its own scratch root over the real install's entry, and its
  uninstall then deleted it. `build/New-Release.ps1` now replaces **three**
  elements rather than two when it builds the second pack — the id, the title
  and the output directory — and the arm reads the user's Start Menu before and
  after for the same reason it already read the Add/Remove key: nothing under
  the shipping title may point into the scratch root, `BrowserAI (suite).lnk`
  may not outlive its own uninstall, and the whole set must be byte-identical
  across the run.
- 🐛 **Three invisible backspace bytes are gone, and one of them had silenced a
  whole scan.** A byte search over everything git tracks found `0x08` in three
  files, each of them a `\b` that something expanded before the file was written.
  The expensive one was `BrowserIdleTimerTests.ClockAssignment`, whose pattern
  asked for a **literal backspace** before `Clock` — so
  `TheShippedClockIsTheRealOneAndNothingInTheProductReplacesIt`, the arm that
  exists because a test clock leaking into a shipped build stops the only timer
  in the product from ever firing and **nothing anywhere would go red**, was
  asserting emptiness over a result set nothing could enter. The scan is
  re-pointed and carries a positive control it is asserted against before the
  emptiness is believed; over the product tree it still finds nothing, which is
  now a measurement rather than an artefact. The other two were prose: a comment
  in `SessionToolTests` and the browsers root in [`HAZARDS.md`](HAZARDS.md), both
  reading `BrowserAI` followed by `rowsers` where `BrowserAI\browsers\` was
  meant. The hazard row is a dated record maintained by addition and the path was
  repaired in place, because restoring a character a tool ate is a typo-class
  correction and not a change to what the row claims.

- ✅ **No text file in the tree may carry a C0 control byte.**
  `HouseRuleTests.NoTextFileInTheTreeCarriesAControlByte` reads every file the
  walk reaches and refuses anything below `0x20` but tab, line feed and carriage
  return. **The byte is invisible in every editor, every diff and every review**,
  which is why this is a mechanism rather than a habit: a reader sees the escape
  they meant to type where the file holds one character that is not it, and a
  regex that cannot match is a green test forever. A file of one of the prose
  kinds is always read, NUL included; anything else is binary by git's own
  heuristic — a NUL in the first 8,000 bytes — and is skipped, which today is
  `assets\BrowserAI.ico` and nothing else. Both counts are asserted, so a corpus
  quietly re-classifying itself as binary cannot empty the scan, and the control
  is written to disk rather than passed as a string so the reading half is
  exercised too.

- 🐛 **An "ours" registration that names the wrong binary is stale, not
  present.** `McpRegistryView.Classify` answered `OursAndPresent` for any file
  under our install root that **existed**, and every registration written before
  the 2026-09-15 two-binary split names `current\BrowserAI.exe` — which is now
  the **configuration app**. So a pre-split 1.0.0 install that later updates kept
  an entry pointing at the window: `McpRegistrar.Repair` leaves an
  ours-and-present entry exactly as it is by design, arguments and all, so the
  client started a dialog and waited forever for a JSON-RPC handshake a window
  will never send. **There was nothing in any log, because nothing had failed** —
  which is the whole reason this survived: the install works, the update works,
  the registration is there, and the server does not answer. *Present* now means
  *is the MCP server*, read out of the file's own PE optional header
  (`IMAGE_SUBSYSTEM_WINDOWS_CUI`, 3) — the same discriminator
  `RegistrationTarget` already used when it composed the path, so there is one
  answer to the question rather than two. Anything else under our root is
  `OursAndStale`, which `Repair` re-points and the window offers *Register* out
  of; a file that is not a portable executable at all gets the same answer, for
  the same reason. **The two causes stay one state because the remedy is
  identical**, and only the sentence differs: the dialog says *"Registered to the
  wrong binary"* when the file is there and *"which is not there any more"* when
  it is not, because telling somebody a file is missing when they can see it is
  the fastest way to lose their trust in a status line. Planted red over
  constructed inputs — a Windows-subsystem binary at the registered path, then a
  file that is not a PE — and again on the sentence.

- 📦 **A release body is one shape now: a headline a line, each linked to its own
  lines of the changelog.** The maintainer's choice (Q197 b). Every entry is
  `- <icon> **<headline>** [read more](…/CHANGELOG.md?plain=1#L<first>-L<last>)`,
  a line range into the source view of the changelog **as the tag carries it**,
  which GitHub highlights exactly. The `<details>` fold is gone with the second
  shape that came with it: the fold put the detail in the release a second time,
  which is what made the body enormous, and over the size limit the whole
  document silently became headlines with nothing to click. **Re-measured
  2026-09-16 over the 1.0.0 section as it stands: folded is 288,437 characters
  and would have fallen back to 19,780; linked is 41,288**, a third of GitHub's
  125,000, with a range on every one of the 227 entries. The size guard survives
  as a pathological fallback — the links are dropped and the footer's section
  link is the only way in — and the script still says which shape it produced.
  **The line numbers are only true of one file, so the generator refuses anything
  else**: the changelog on disk must match `HEAD`, and a tag `v<version>`, if it
  exists, must be at `HEAD`, or it refuses naming both commits. A dirty tree
  elsewhere is reported rather than refused, because this runs inside
  `New-Release.ps1` after a publish that leaves restore artifacts behind and none
  of those can move a line number. An untracked changelog — a fixture under
  scratch — is a state rather than a failure and the run says so. **The highlight
  was verified on github.com in a real browser rather than assumed**: `curl`
  cannot show it, because it is applied client-side and the served HTML carries
  only the first 1,000 lines of a 3,682-line file. At
  `?plain=1#L3496-L3532`, **exactly 37 elements carried a highlighted class**,
  which is 3532 − 3496 + 1. The measurement is filed in
  [`kb/toolchain.md`](kb/toolchain.md) beside the release-body section it belongs
  to, with what `curl` can and cannot establish and how to re-run it, and it
  shares re-verification row 128 rather than taking one of its own.

- 📝 **A publish leaves `BrowserAI.Core`'s lock file modified, whichever publish
  it is.** A RID-specific restore adds an empty
  `"net10.0-windows7.0/win-x64": {}` section to
  `src/BrowserAI.Core/packages.lock.json`. It was first seen after a standalone
  `dotnet publish -r win-x64` and reverted in `ac244ff`; **`build/New-Release.ps1`
  produces the identical diff**, watched on a full pack run on 2026-09-16, so
  routing publishes through the release script does not avoid it — and publishes
  should go through it anyway, because it is what stages each publish, reads both
  ILC logs and refuses a missing executable by name.
  [`RELEASING.md`](RELEASING.md) and [`TESTING.md`](TESTING.md) now say to revert
  the diff rather than commit it, and say plainly that **nothing enforces it**: an
  arm holding the file free of that section would be red for the whole window
  between item 7's publish and item 8's run, which is a gate that cannot pass
  after doing what the checklist just told it to do. The two ways it could be
  closed — accept the section as the resolution, or restore `--locked-mode` so a
  rewrite fails instead of happening quietly — are recorded as open. The
  measurement is filed in [`kb/toolchain.md`](kb/toolchain.md) under the NuGet
  section, with both publishes named and the times they were watched at.

- 🐛 **A read-only file no longer defeats the delete every tree delete goes through.**
  `Runtime/TreeDelete` called `File.Delete` on the attribute as it found it, and
  Windows refuses that with `ERROR_ACCESS_DENIED` — the same code a held handle
  produces, so the list of nodes it could not remove read like a lock and was an
  attribute. It now clears `FileAttributes.ReadOnly` and deletes again, and only
  after a delete has already been refused, so the ordinary path is one call and
  unchanged and a genuine sharing violation is still reported rather than
  retried into silence. What it buys is ordinary content: anything a session
  downloaded, or a user dropped into a directory `browserai_destroy` is handed,
  was being reported as something the product could not remove when it could.
  **It was found by a release gate going red at the head of its own first run,
  on a tree nobody had changed** — git writes every loose object read-only, so a
  scratch directory holding a real repository survived six refused objects deep,
  and the documented between-runs clear had been removing the evidence with
  `Remove-Item -Force` on every pair of runs for as long as anybody had typed
  it. `TreeDeleteTests.AReadOnlyFileIsRemovedRatherThanReportedAsANodeThatWouldNotGo`
  was planted red against the real shape, with a held file in the same tree as
  the control so that clearing an attribute cannot become swallowing a hold.
  The end-to-end half is stronger than the arm: a run that executes the rig now
  leaves the scratch root empty.

- ✅ **The dated dependency override cannot be forgotten: two instruments go red
  on the day it expires.** An exception with a written exit is worth
  nothing if the exit lives only in a document, so the exit is a build failure
  instead. `PayloadTests.TheDatedPlaywrightCoreOverrideIsStillNeeded` reads the
  committed lock, so it runs from a clean clone on every build and needs no
  payload assembled; `build/Build-Payload.ps1` reads the live resolution and
  refuses to assemble a payload past the exit. Each carries its own ordering of
  the two version shapes upstream publishes and refuses any third rather than
  guessing that a shape it cannot order is lower, which is how an override
  outlives its own exit. Both were planted red before the override landed: the
  test against a lock doctored so the wrapper already pins the override, and the
  script against an override lowered to the declared pin. The failure names the
  file, the key, and what to record when it is deleted.

- 🐛 **The coverage block stops printing a download size somebody typed.**
  Every run of the suite said *downloaded 203.8 MB from the CDN* — a literal in
  `FirstRunCache`, in the one place on the screen that reads like a measurement,
  beside the elapsed seconds and the file count the run really did observe. The
  2026-09-16 re-measurement moved
  `BrowserProvisioner.FirstRunDownloadBytes` to **207,274,189 B** and moved every
  other quotation of the figure; this one could not be corrected by re-running
  anything, which is what makes a number written at a sentence worse than one
  nobody wrote down. It is rendered through `DownloadSizeFor` now, the same path
  the provisioning refusal uses. Planted red and watched: *"Expected to contain
  \"207.3 MB\" … but received \"downloaded 203.8 MB from the CDN because some
  reason\""*. **The other surviving mentions of the old figure are deliberately
  untouched** — they are comments and prose, and which of them read as
  measurements is a separate decision.


- 📦 **`New-Release.ps1` clears its own test feed before it packs into it.**
  A stale pre-release can never refuse a cut again. The entry
  below records the checklist correction that met this failure; this is the fix it
  said belonged to whoever owns the script. `Releases/test-pack/` is a **second
  Velopack feed**, every gate pack writes a pre-release into it, and a gate runs
  far more often than a release is cut — so at the moment of a cut it holds
  versions above the release and `vpk` refuses, *after* the shipping artifacts
  have already been built. [`build/Clear-TestPackFeed.ps1`](build/Clear-TestPackFeed.ps1)
  now runs immediately before the test pack and deletes exactly what that pack
  regenerates: `BrowserAI.app.test-*.nupkg`, `releases.win.json`, `RELEASES`,
  `assets.win.json`, the two renamed downloads, **and the two pre-rename names** a
  run that died between the pack and the rename leaves instead. It is **not** a
  directory wipe — a file under `test-pack/` that no pack regenerates survives —
  and an absent or empty directory is reported rather than refused, because the
  first cut on a fresh clone meets both. **Q200**, decided 2026-09-17.
  [`RELEASING.md`](RELEASING.md) item 5's manual step is corrected by addition and
  kept as a description of the failure mode; the shipping feed is still cleared by
  hand and `Test-ReleaseVersion.ps1`'s refusal still covers it.

- 📦 **The release checklist now clears the suite's feed as well as the real
  one.** `Releases/test-pack/` is a **second Velopack feed**, not just a directory
  the checklist keeps, and every gate pack writes a pre-release into it. At the
  moment a release is cut it therefore holds versions newer than the release, and
  `vpk` refuses it the same way it refuses one in `Releases/` — *"There is a
  release in channel win which is equal or greater to the current version
  1.0.0"*. Because the running order packs for the gate first, **this refused
  every release cut, and it refused this one**: it fired *after* the real pack
  had succeeded, so the non-zero exit named the suite's installer while the
  release itself was already on disk. [`RELEASING.md`](RELEASING.md) item 5 is
  corrected by addition with the file names to clear, and the better fix —
  `New-Release.ps1` clearing its own regenerated, never-published test output —
  is written down there as the script owner's to take rather than taken by a
  release executor.

## [0.1.0] - 2026-08-16

### Added

- ✨ **Browser automation for AI agents on Windows, as one MCP server that brings
  its own everything.** BrowserAI
  ships its own Node runtime and its own `@playwright/mcp`, downloads its own
  browser on first use, and proxies JSON-RPC to it. Nothing on the machine has
  to be installed, on `PATH`, or of any particular version.

- ✨ **Sessions that survive the agent that made them.** A session is a directory
  the caller names: the browser profile, its downloads, its artifacts, its log
  and the lock that owns it all live inside it. Close the agent, come back
  tomorrow, `browserai_resume` the same directory, and the cookies, the logins
  and the local storage are still there.

- ✨ **Three session modes, one table.** `headless`, `interactive` and
  `persistent` differ in what they may do, and the same table renders the server
  instructions, the tool descriptions, the refusal messages and the enforcement
  — so a mode cannot mean one thing to a model and another to the code.

- 🔒 **Every tool call is judged against the mode of the session it names.** Deny-by-default
  in two dimensions: a tool nobody has classified is refused everywhere, and a
  mode with no policy row permits nothing.

- ✨ **Artifacts land inside the session, not wherever the browser felt like.** Screenshots,
  PDFs, downloads and traces are routed by type into folders under the session
  directory, never overwrite each other, and every answer says where the file
  went — as an absolute path and as a session-relative one.

- 🔒 **A path that would escape the session is refused with a sentence saying why.** A
  path that would escape the session is refused with a sentence saying why,
  decided on the string and without touching the filesystem.

- ✨ **First-run browser provisioning that does not block the conversation.** `browserai_init`
  returns immediately and says a download is running; browser tools are refused
  with a recovery until it lands; the same child then navigates with no restart.
  A run that fails halfway removes its partial tree rather than leaving
  something that looks installed.

- ✨ **Firefox as well as Chromium.** Firefox as well as Chromium, including the
  profile-lock preflight that turns *"a modal dialog blocks startup for three
  minutes"* into a refusal in milliseconds.

- ✨ **Nothing is left running.** Every child and every browser it starts is
  created inside a job object, so killing BrowserAI — however abruptly — takes
  the whole tree with it. A stray browser from a previous crash is found on
  startup and ended only when the session directory that owns it is provably
  free.

- ✨ **Answers arrive exactly as the browser produced them.** The proxy splices
  the child's own bytes into the caller's frame rather than re-serialising, so
  escapes, numeric form, key order, unknown fields and unknown content types all
  survive unchanged.

- ✨ **Failures say what happened and what to do next.** One error catalogue,
  written for the model that has to act on it, with every row provoked by a real
  condition rather than asserted to exist.

- ✨ **The log is a log.** Everything outside a session goes to one rolling
  process log that survives updates; everything inside one goes to a log beside
  that session's lock. Nothing anywhere can write to the protocol channel by
  accident.

- ✅ **Upstream cannot move underneath it silently.** Four snapshots of
  `@playwright/mcp`'s surface are regenerated from the resolved package on every
  build and diffed against committed copies, and the build fails with the diff
  itself when anything moves.

- ⬆️ **Every dependency floats to latest and the build freezes what it resolved.** Every
  dependency floats to latest and the build freezes what it resolved, so the
  resolved set is recorded beside the artifact rather than remembered.
