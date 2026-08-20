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

## [Unreleased]

### Removed

- **Continuous integration, completely.** `.github/workflows/build.yml` was added
  on 2026-08-18 and deleted on 2026-08-20 without ever being released, at the
  maintainer's decision, verbatim: *"Remove CI completely. Let all the tests run on
  my machine only. I want no CI and no github runner. Add to the todo that we will
  add CI back in later. But that requires me adding infrastructure for self-hosted
  runners and I am considering leaving github before we do so."* `.github/` is gone
  entirely rather than left as an empty husk. **The whole gate is now
  [the release checklist](RELEASING.md#the-release-gate)**, which gained a
  requirement in item 8: the suite is run from **PowerShell and from Git Bash**,
  and both totals recorded, because the drive letter's case is inherited from the
  shell and a single-shell run bakes in whichever spelling agrees — which is
  precisely what the removed CI did, running `pwsh` end to end.

  **What was audited before deleting it, because "do not lose anything unique" was
  the load-bearing half of the instruction.** Most of what the workflow did is
  already covered locally and more strictly — the two-step
  `restore --force-evaluate` / `--locked-mode` and both lock diffs are release
  checklist item 1 with `--exit-code`; the cold CDN download is
  `FirstRunProvisioningTests` against an empty root, hourly and always on a release
  run; the coverage block is written to stdout and to `.work/suite-coverage.txt` on
  every run; MinVer has its tags in any developer clone. **Three things genuinely
  die**, and they are [`TODO.md`](TODO.md#continuous-integration)'s content: a
  *different machine* — four cores, cold caches, no interactive desktop, a volume
  with 8.3 generation off — which found four defects this machine structurally
  cannot, including the `browserai_destroy` survivor arm that passed nine local
  runs and failed three consecutive CI ones; **a contributor's pull request built
  before merge**, which on a public repository is what the workflow was for; and
  `BROWSERAI_EXPECTED_ABSENT`, whose only consumer anywhere was that file.

- **`SuiteCoverageTests.TheWorkflowStillDeclaresWhatItExpectsToBeAbsent`, the
  capability pin's third arm.** It read `build.yml` scoped to the step that ran the
  suite, so deleting the declaration was a red build rather than a silent
  switch-off. **Deleted rather than re-pointed, and the reason is a house rule:** a
  search that returns zero needs a positive control. The old test had one —
  *this really is the step that runs the suite* — and a re-pointed version aimed at
  "any pipeline definition" could have none, so it would pass over an empty
  directory, over a typo in its own path, and over a pipeline shape it does not
  recognise, indistinguishably. **The mechanism itself is kept, correct and
  inert:** an unset `BROWSERAI_EXPECTED_ABSENT` declares nothing, which is already
  what a developer machine does, and
  `TheExpectedAbsentDeclarationIsReconciledAgainstWhatIsAbsent` still exercises
  every branch in-process. Restoring the arm against whatever runs the suite next
  is part of the CI item.

### Added

- **A reinstall now takes the machine's browsers root for the whole call, and
  `browserai_init` and `browserai_resume` are refused while it holds it.**
  `browserai_reinstall_browser` already refused while sessions were open, and two
  agents on one machine could still race past it: the reinstall establishes that
  nothing is running out of the tree, and the other one's `init` launches a
  browser into that tree while the recursive delete is part way through it. The
  census was *right* when it was asked, which is why nothing caught this.

  **The claim is a lock file at `<browsers root>\reinstall.lock`, held open
  `FileShare.Read` for the whole operation.** A file rather than a named mutex
  because the claim spans a 203.8 MB download inside an `async` method and a named
  mutex is owned by the thread that waited on it; **and not a named semaphore**,
  which does span threads, because a semaphore's count is not restored when its
  holder dies — one crashed reinstall would then refuse every `init` on the
  machine until a reboot. Windows closes a file handle however the process ends,
  which is the crash recovery this needs and cannot write itself. Held-ness is a
  sharing violation and never the file's existence, exactly as `lock.json` works.

  **It is mutual against itself** — a second reinstall is refused for the same
  reason a session is, which the maintainer asked for in the same breath:
  *"Including any reinstall sessions."* **And it does not drain.** A reinstall
  that takes the claim and then finds sessions open releases it and refuses
  immediately, naming how many are running; it never holds the machine waiting for
  a browser a human may never close.

  **The lock order is fixed and has no cycle.** The claim is outermost and the
  per-family provisioning mutexes are taken under it, never the other way round;
  `init` and `resume` only *probe* it and never acquire it, so they can never take
  it from a racing reinstall; and every acquisition on both sides is non-blocking,
  so even an inverted order would produce a refusal rather than a hang.

- **The family reinstall's session gate is now unconditional.** It used to be
  asked only *inside* `if (running.Count is not 0)`, so a session that was open
  with its browser not currently launched let the delete through — and the browser
  it is about to launch lands in a tree being removed. The maintainer's decision,
  verbatim: *"No reinstall if there is any session running system wide."* The
  family filter survives: only a session of the family being reinstalled can hold
  an executable out of that family's tree, so a live Chromium session still does
  not block a Firefox reinstall. `shared` counts every family, as it already did.

### Changed

- **Provisioning is stopped when it stops making progress, not when it has taken
  too long.** `ProvisioningTimers.AbsoluteCap` — 45 minutes on the whole install —
  is replaced by `StallCap`, ten minutes with **nothing written at all**. A total
  cap can only ever fire on a link that is slow and *working*: 203,824,344 B in
  2,700 s is 0.60 Mbps, and a link that has died is caught by upstream's own
  30-second socket timeout twenty times sooner. It punished the one case it could
  reach. The 60-minute crash tripwire went with it — with no total there is no
  number a tripwire could be "outside" — and the *waiting* process now runs the
  same stall detector against the same bytes, so a holder on a slow link is no
  longer reported as stuck at sixty minutes.

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
  and that wait was measured at **470 s** before upstream gives up by itself — so
  any cap at or under 7 m 50 s kills a healthy install that is correctly queueing
  behind another. It is deliberately ten times `UpdateService.StallBudget`, whose
  60 s is right there and wrong here: a Velopack download has no directory lock in
  front of it.

  **Scanning `%TEMP%` instead was considered and is measurably wrong**: on this
  machine that scan found a `playwright-download-PRU23e` abandoned three days
  earlier holding 128,684 B of somebody else's archive, counted as our progress.

- **The refusal a browser call meets while provisioning runs is a progress
  report.** Bytes written by this attempt against the measured download total, the
  percentage, elapsed, the observed rate, and what the remaining bytes come to at
  that rate — labelled as arithmetic rather than as a promise. The old sentence
  quoted the size and said *"wait about ten seconds"*, which read identically at
  8 s in and at 25 minutes in, so a model had no way to tell a download that was
  working from one that was not and its only recourse was to keep calling.
  `@playwright/mcp` emits no progress notifications at all, so this refusal is the
  whole mechanism and no protocol work exists to do. `FirstRunDownloadSizes` is now
  derived from a byte count rather than hand-written beside one.

### Fixed

- **The mode table claimed a persistence property the code has never had.**
  `README.md`'s third column read *"Stored credentials — No / No / Yes"* and
  `SessionMode.cs` described `interactive` as *"a human can type a password this
  session will not keep"*. **All three modes persist.** `BrowserConfiguration.
  ForSession` writes `browser.userDataDir` as `<session>\profile` in every mode
  and never writes upstream's `isolated` key, so cookies and `localStorage`
  survive a `browserai_resume` in a `headless` session exactly as they do in a
  `persistent` one. `storage` is a **tool filter, not a persistence switch** — it
  decides whether the 17 cookie, `localStorage` and `storageState` tools exist in
  that session's child at all. The correction points the safe way: a caller who
  believed a mode discarded credentials would leave a signed-in profile behind
  thinking it had not.

- **Two doc comments claimed a refusal that has not existed since 2026-08-18.**
  `BrowserConfiguration.UnionCapabilities` and `BrowserProxy.AnswerToolsListAsync`
  both ended *"a call that its session's mode does not permit is refused at call
  time instead"*. The `(tool, mode)` matrix was removed; what replaced it is the
  child's own capability set, so such a call is **forwarded** and upstream answers
  that the tool does not exist. Both sentences survived the removal by describing
  a fallback that had gone.

### Added

- **`--storage-state` together with `--user-data-dir` is a silent no-op** — exit
  0, empty stderr, no state applied. `storageState` is the **only** one of
  `BrowserNewContextParams`' 32 keys absent from
  `BrowserTypeLaunchPersistentContextParams`' 49; `tObject` iterates the declared
  schema and drops undeclared keys without error; and `createPersistentBrowser`
  spreads `...contextOptions` straight through, so it looks accepted at every
  visible layer. BrowserAI is on the persistent side by construction and writes no
  `storageState`; the entry is for the next reader who reaches for it to seed a
  signed-in session.

- **`--caps` accepts any word at all.** `--caps bogus` exits 0 with no
  diagnostic, because the option is parsed by `commaSeparatedList` where
  `--codegen`, `--console-level` and `--image-responses` all use `enumParser`. It
  is also why `--caps storage` works although the help documents only `vision`,
  `pdf` and `devtools`.

- **Two browsers on one profile directory: Chromium refuses in 5,036 ms naming
  the cause, Firefox hangs for 180,402 ms with an error that never mentions the
  profile.** Upstream's `isProfileLocked` probes `<userDataDir>\lockfile` —
  Chromium's name — while Firefox uses `parent.lock`, so the guard never fires and
  Firefox's own lock blocks the juggler handshake until the launch timeout. The
  5,036 ms is `isProfileLocked5Times`' own five one-second retries succeeding.
  **BrowserAI's own path is covered** by `ChildLaunch.Create`'s `parent.lock`
  preflight, which runs before the config is written and before anything spawns;
  upstream's bug is recorded rather than worked around.

- **The user agent is settable from the config on both families and
  `navigator.webdriver` is not, on Firefox.** Research for a decision the
  maintainer has not taken: `browser.contextOptions.userAgent` turns Chromium's
  `HeadlessChrome/152.0.0.0` into `Chrome/152.0.0.0` and replaces Firefox's UA
  outright, with no Playwright driving and no init script. Firefox's
  `navigator.webdriver` stays `true` under `dom.webdriver.enabled: false`, while
  `general.useragent.override` through the same `firefoxUserPrefs` object *does*
  take effect — which is the control that makes the negative mean something.
  **Nothing was implemented.**

- **Firefox is owed Chromium's rename measurement, and now has it — plus the two
  shared trees and the browsers root, which nobody had asked about at all.** The
  browser-tree rename refusal behind `browserai_reinstall_browser` was measured
  against **Chromium only**, and the entry said so. Measured 2026-08-19 the same
  way: a live headless **Firefox 153.0** (playwright firefox v1539) refuses
  `Directory.Move` of `firefox` with a sharing violation and of `firefox-1539`
  with `ERROR_ACCESS_DENIED`, and both succeed the second the browser is gone —
  **identical to Chromium, error for error and in the same order**, over two runs.
  **So there is no product finding and nothing changed**: the refusal was already
  family-agnostic, and it now rests on a measurement of both families rather than
  a generalisation from one. The Chromium arm was re-run first and reproduced
  exactly, so the record is reproducible from what was written rather than only
  from the day it was taken.

  **What was new is at the edges.** `ffmpeg-1011` and `winldd-1007` rename
  **freely** while a browser of either family is live — neither is running, since
  `ffmpeg-win64.exe` exists only during a recording — while the **browsers root
  itself** is refused under both families, with the same error as the revision
  directory. That is *further up* than the 2026-08-18 running-executable
  measurement predicts, where a running image's grandparent moved without
  complaint, so whatever a browser holds is not just the directory its image sits
  in. Recorded rather than acted on, and explicitly not a licence to narrow
  `shared`'s wider refusal. **And Playwright's Firefox brings no Remote Agent up**
  — `--remote-debugging-port` never answers, because Playwright drives it over the
  juggler pipe — so the Firefox arm proves liveness by content-process tree where
  Chromium's uses DevTools, which is a weaker signal and is written down as one
  ([kb](kb/windows/processes.md#the-same-measurement-for-firefox-and-for-what-both-families-share--2026-08-19),
  re-verification row 103).

- **The CsWin32 metadata licence terms are in the repository, quoted verbatim, and
  the question is now a legal one rather than a research task.** The
  [`TODO.md`](TODO.md) item had stood on *"whether those terms create a notices
  obligation for shipped generated code is not assessed and must not be asserted
  either way"* — with nobody having read the terms. They are now in
  [QUESTIONS.md §12](QUESTIONS.md#12-the-cswin32-metadata-licence--moot-2026-08-20-and-the-entry-stays):
  the four packages and their resolved versions, how each declares its licence, the
  operative clauses quoted with URLs and the date fetched, what the text does and
  does not say about generated code, and five ordered questions for a lawyer.
  **The finding that was not expected:** the two metadata packages ship the same
  **byte-identical** Windows 10 SDK EULA (`EULAID:WIN10SDK.RTM.AUG_2018_en-US`,
  SHA-256 `0e97876e…`), while `win32metadata`'s own `README.md` says
  `Windows.Win32.winmd` — the only file the generator reads — is **MIT**. Two
  current Microsoft statements about one file, and they disagree. **No conclusion
  is drawn and none may be**: the exposure is still zero because CsWin32 is
  test-only at `PrivateAssets="all"` and nothing it emits ships.

- **`browserai_reinstall_browser` gained a third value, `shared`, and it is the
  only route to repairing `ffmpeg`.** `ffmpeg` and `winldd` are downloaded into
  the browsers root by **both** families, each carries its own
  `INSTALLATION_COMPLETE`, and a family reinstall deletes only that family's
  revision directory — so a corrupted `ffmpeg`, which the `video` artifact type
  needs, was **permanent through the product's own surface**. `shared` deletes
  both trees and runs one `install-browser ffmpeg`, measured 2026-08-19 to
  rebuild whichever of the two is missing because each carries its own marker
  ([kb](kb/playwright/provisioning-and-timings.md#one-install-browser-ffmpeg-rebuilds-both-shared-components--2026-08-19),
  re-verification row 100). The completeness check stays **per component**: a run
  that exits 0 having left one unmarked is reported here rather than met later as
  `spawn EFTYPE`.

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
  asserts they differ, because reading one list for both is what would have bound
  a session for life to a codec. **Explicitly rejected:** having a family
  reinstall also verify and repair the shared components — that re-introduces a
  repair tool which can break something working, which is why the argument is
  required at all. Still no force flag.

- **Firefox is a browser you can ask for.** `browserai_init` accepts
  `browser: "firefox"` beside `chromium`, which stays the default. Everything
  below the front door was already family-parameterised — provisioning, the
  config generator, the `parent.lock` preflight, Restart Manager attribution, the
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

### Removed

- **`BuildConfigurationTests.NoSourceFileIsInvisibleToGit` is deleted, deliberately,
  and this entry exists so nobody re-adds it believing it was an oversight.** It
  listed every `.cs` under `src/` and `tests/` and asserted each appeared in
  `git ls-files`. It existed because of a real loss: the .NET template's unanchored
  `artifacts/` rule matched `src/BrowserAI/Artifacts/` on case-insensitive Windows,
  and **five product source files were ignored while the build, the suite and
  `git status --porcelain` all read green**. **The maintainer's call, over a
  recommendation to widen it past `*.cs` rather than remove it** — *"I do not think
  we need this test at all."* Gone with it: `TrackedFilesAsync`, the harness that
  shelled out to git and served nothing else.

  **What is now unenforced, said plainly rather than left to be found.** Nothing
  compares the files on disk against what git can see, so a source file swallowed
  by an ignore rule is invisible again exactly as it was on 2026-08-15, and every
  surface signal reads healthy while it is — an ignored file is not untracked, so a
  clean `git status` is what a swallowed file *produces*. **64 unanchored directory
  rules remain** in the upstream half of `.gitignore`; the predicate is *a line
  above the BrowserAI marker that ends in `/`, does not begin with `/` and is not a
  negation*, re-counted 2026-08-19, with `/artifacts/` and `/.artifacts/` the only
  two anchored ones as the positive control. ⚠️ *Previously published as "nineteen"
  with no predicate written down, and no predicate reproduces nineteen — the figure
  is replaced rather than corrected.* Both `.gitignore` comments that named the test
  now record the deletion and what it costs, and the upstream-refresh procedure ends
  in **run `git check-ignore -v` by hand** where it used to end in *run the suite*.
  Reasoning and reversal in
  [QUESTIONS.md judgement call E](QUESTIONS.md#e-nosourcefileisinvisibletogit-was-deleted-and-this-is-not-the-entry-you-think-it-is).

### Changed

- **`QUESTIONS.md` had gone stale, and it is the document the maintainer reviews
  from — so staleness there costs more than anywhere else.** Two entries were found
  wrong **by accident**, which is the only reason the rest were read. Swept entry by
  entry on 2026-08-19: **sixteen checked — nine numbered, five lettered, and the
  block of settled bullets — and six were wrong.** Each corrected in place with a
  `previously` clause; nothing deleted for being merely settled.

  **Item 6** said the per-directory gate is **60 seconds**; it is **120**, raised on
  2026-08-18 because the property that has to hold is the gate against the **sum**
  of the waits taken inside it, not the largest. **Item 7** said the deeper
  probe-before-gate fix *"was NOT taken"*; it was, the next day, and the entry's
  framing was wrong as well as its verdict — the probe already existed as a fast
  refusal in front of the gate, and the mechanism was not the TOCTOU window the
  entry predicted. A contender cannot **take** a directory inside the writer's
  rename-reopen gap, because taking one needs the gate the writer is holding; it can
  **look**, and `ProbeForHolder`'s `FileAccess.ReadWrite` handle is refused by a
  holder sharing only `Read`. Detecting an owner and blocking one are the same
  capability, so it is absorbed on the gated side instead. **Item 2** said *"there is
  no CI today"* — there had been for a day. **Judgement call A** said closing the
  `#anchor` gap was *"on the queue"* — it closed on 2026-08-18. **Judgement calls C
  and D** still described a known-intermittent suite and a contingency for missing 20
  consecutive green; the suite reached 20 of 20 at `Unbounded` on 2026-08-18 and the
  contingency never fired.

- **`browserai_destroy` now returns `isError: true` when it could not remove
  everything, and the error carries the whole report.** Previously both arms
  returned `isError: false`: a call that removed a nine-thousand-file profile and
  could not remove eleven locked files looked, to a model scanning result shapes,
  exactly like one that removed the lot. **The maintainer's call, taken over the
  recommendation to leave it and over the stated objection that an error invites a
  retry** — and the refinement that answers the objection is in the text. After
  the tally and the listing, the arm now says the session **is** destroyed (its
  record gone, the index having forgotten it, what is listed being residue on
  disk), says **not** to call `browserai_destroy` again because there is no
  session left for it to destroy and it will refuse, and says what to do instead:
  wait for whatever holds those files to exit and delete them, or leave them.
  The summary, the count, the listing and the truncation notice are unchanged, and
  the roll-up warning arm is untouched. `QUESTIONS.md` §11 carries the decision,
  the objection and how to reverse it.

  **Every destroy assertion in the suite was re-read rather than only the ones
  that went red**, which is what found the one that could only fail on a slow
  machine: `FirefoxSessionTests` still carried a bare `isError` assertion beside
  the contract check, and against Firefox on a four-core runner that is an
  assertion about how fast a browser lets go of its profile. The flag is now part
  of `DestroyAnswer.AccountsForWhatItLeftAsync`, asserted **in both directions**
  — a survivor arm reporting success and a clean destroy reporting failure are
  equally red — so no test holds destroy to a promise of its own.

- **CI declares which capabilities it expects to be absent, and an undeclared
  absence is a red build.** The capability gate made a degraded run *loud*; it
  never made one *noticed*. CI has run with `packed release` and `client CLI`
  ABSENT since the day it existed, so a provisioning step that started failing
  soft would have produced a green run, one more `ABSENT` line in a block nobody
  diffs, and a set of tests that skipped instead of running — the founding failure
  shape of this project, one layer above the gate written to remove it.
  `BROWSERAI_EXPECTED_ABSENT` on the workflow's test step is the declaration;
  `SuiteCoverageTests.EveryAbsentCapabilityIsOneThisRunsEnvironmentDeclared`
  fails on an absence it does not name **and** on a name in it that is `PRESENT`,
  because a declaration wider than the truth is standing permission for that
  capability to disappear later. An unset variable declares nothing, so a
  developer machine behaves exactly as before and a clean clone still runs.
  `.TheWorkflowStillDeclaresWhatItExpectsToBeAbsent` reads `build.yml` itself,
  scoped to the step that runs the suite, so deleting the line is a red build
  rather than a silent switch-off — and
  `.TheExpectedAbsentDeclarationIsReconciledAgainstWhatIsAbsent` exercises every
  branch in-process, so the mechanism does not first run on the build that needs
  it. Five faults were planted and all five went red: an undeclared absence, an
  over-broad declaration, a typo in the declaration, the declaration deleted from
  the workflow, and a typo committed to the workflow.

- **A recorded hazard was measured and turned out not to be one: two family
  installers cannot extract into one shared component directory, because
  upstream serialises every install on a lock BrowserAI never knew about.**
  `ReinstallSharedAsync`'s remarks called the race *"reachable in the shipped
  product"*. The **concurrency** is reachable — `BrowserProvisioner.MutexNameFor`
  hashes the browsers root **and the family**, so a chromium install and a
  firefox install run at once by design, and both lay down `ffmpeg` and `winldd`
  in the one root. The **race** is not: `registry.install()` takes a
  `proper-lockfile` directory lock at `<PLAYWRIGHT_BROWSERS_PATH>\__dirlock`
  before it touches any executable and holds it for the whole install.

  **Measured five ways on 2026-08-19** rather than reasoned from the source:
  chromium and firefox started **8 ms apart** into an empty root finished with
  four trees, every one carrying `INSTALLATION_COMPLETE` and no lock left behind;
  three concurrent `install-browser ffmpeg` runs over three rounds came back 3/3
  green and byte-identical; a held lock stopped an installer **dead for 30 s** —
  no directory, no download, no output — and it completed 8 s after the release;
  an abandoned lock, which is what a killed installer leaves, was reclaimed as
  stale at no measurable cost.

  **What is real is a wait, and it is now written down as one.** The retry budget
  is **470 s**, after which upstream fails the install outright with its own
  `ELOCKED` box, having written nothing. 203.8 MB in 470 s is 3.5 Mbps, so on a
  slower link a firefox install started beside a chromium one **fails rather than
  queues** — loudly, recoverably, and inside the 45-minute `AbsoluteCap`, because
  the wait happens before the browser's directory appears and `ExtractionCap` has
  not started. The remarks are corrected in place with what they previously said,
  the hazard index carries the row, and
  `PayloadTests.UpstreamStillSerialisesEveryInstallOnOneLockOverTheWholeBrowsersRoot`
  reads the four anchors out of the assembled bundle and asserts their order — so
  a `playwright-core` bump that drops the lock or moves it inside the
  per-executable loop is a red build rather than a rediscovery.

  **The three mutexes that method holds are still load-bearing**, for a reason
  upstream's lock cannot cover: it serialises *installs* against each other and
  knows nothing about the recursive **delete** performed first, which takes no
  `__dirlock` and never could.

- **Every row of the [hazard index](HAZARDS.md) is now adjudicated, and the
  count of the ones that are not is asserted on every build at zero.** 55 rows
  read `open` with `—` for evidence — rows nobody had decided either way, carried
  since before `v1.0.0`. Adjudicating is not closing: **29 gained a named
  mechanism and closed, 26 gained a stated reason and stayed open**, and eight of
  those twenty-six say in as many words that they can never close, because what
  they describe is an upstream behaviour, a platform property or a deliberate
  trade. Nothing was closed that could not name what goes red if the hazard
  returns.

  **The tally moved from [`TODO.md`](TODO.md) into the index itself**, because
  the item that carried it was work-not-yet-done and the work is done — and
  because **at zero the sentence is a stronger mechanism than the backlog it
  replaced**. Counting down it said *somebody should decide these*; at zero it
  says **a row that arrives `open` with `—` fails the build**, so a hazard has to
  be adjudicated when it is written down rather than accumulated for a later
  pass. `RecordedCountTests.TheHazardTallyIsWhatTheIndexHolds` reads the sentence
  as its anchor and re-counts through the same `HazardIndex` parser
  `HazardIndexTests` uses.

  ⚠️ **Two of the findings are worth more than the rows they came from.** The
  `ContentBlock` converter *throwing* on an unknown content type had been closed
  by the product for three days and nobody had read the row: its two neighbours
  closed on 2026-08-16 on one sentence, and
  `LosslessPassthroughTests.AnUnknownContentTypeSurvivesTheTrip` is a test
  written for exactly this row. And *screenshots are not byte-stable* — the claim
  the whole canned-blob testing practice rests on — **has never been measured**,
  which is why that row stayed open.

  **`RecordedCountTests`' own non-vacuity guards had to be corrected in the same
  pass**, and it is the transferable half: `published.Count > 4` and
  `unadjudicated.Count > 20` were floors placed under the very number they were
  watching be counted *down*, so finishing a third category would have turned the
  test red **because the work got done** — and the obvious fix then looks like
  weakening an assertion. They are floors under the corpus now: the table still
  parses, every row lands in exactly one of the two states, and both states are
  populated. None of the three moves when a row is adjudicated.

- **`browserai_reinstall_browser` now takes one required argument, naming the
  family.** ⚠️ *Previously it took none, "because there is nothing to name: the
  install is shared by every session on this machine."* **The stated reason
  expired rather than being overruled** — with two families provisioned there are
  two trees, two revisions and two mutexes, and the caller's broken browser is
  exactly one of them.

  Two alternatives were weighed and rejected in writing. **Reinstalling both**
  keeps the no-arguments property and makes the blast radius worse in the one
  situation the tool exists for: a caller with a broken Firefox pays 331 MB,
  loses a working Chromium for the length of its own re-download, and a network
  failure part-way ends the call with two broken browsers instead of one. A
  repair tool must not be able to break something that was working.
  **Defaulting to Chromium** is worse still — a broken Firefox, a healthy
  Chromium deleted and fetched again, and an answer reporting a successful
  reinstall. Required rather than optional-with-no-default follows `mode` on
  `init`, this product's settled shape for an argument whose omission cannot be
  answered honestly.

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

- **A session directory on a network path is refused, and a mapped drive letter
  counts as one.** One `File.Exists` against a share that has stopped answering
  costs a measured **22,210 ms**, and several such calls happen inside the
  per-directory gate — so the caller who names the dead share is not the one who
  waits; every other process contending for that directory is. `browserai_init`
  and `browserai_resume` now refuse before anything is created and before the
  gate is taken.

  ⚠️ **It refuses network *semantics*, not the `\host\share` spelling, and that
  distinction is the whole point.** A `net use Z:` mapping is a rooted local
  drive-letter path by every character in it, resolves through the same
  redirector and costs the same twenty-two seconds — measured 2026-08-19 through
  a real redirector alias, which is the first time this repository has had that
  number for a drive letter rather than for a UNC path
  ([kb](kb/windows/detection.md#a-mapped-drive-letter-is-a-network-path-and-costs-the-same-22-seconds)).
  A check on the string shape looks closed and leaves the hole open.

  **The guard cannot pay the cost it prevents.** The network question is answered
  by characters and then by `GetDriveTypeW`, both of which read the object
  manager rather than the filesystem — `GetDriveTypeW` answered `DRIVE_REMOTE`
  in 0.9 ms against a letter whose `File.Exists` had just taken 22 s. That
  corrects a sentence this repository had carried since the log writer was
  built: *"telling the difference needs GetDriveType — a filesystem call, which
  on a disconnected mapping can block for exactly as long as the thing being
  avoided."* It was reasoning rather than a measurement, and it was wrong.

  `browserai_destroy` and `browserai_list` are deliberately **not** guarded, so a
  session created on a share by an older build can still be seen and still be
  removed.

- **A second spelling of one session directory is refused, with the spelling to
  use instead.** `Path.GetFullPath` resolves neither `\?\`, junctions, `subst`
  nor mapped drives, so two spellings of one directory produced **two mutex names
  and one `lock.json`** — the per-directory gate stopped serialising while every
  signal still read healthy, which
  [the adversarial review](docs/reviews/2026-08-18-adversarial-locking.md) traced
  to two processes driving one browser profile in one interleaving and a
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
  on its other three; the GitHub Windows runner does not shorten on the volume it
  checks out onto, so the test that builds an 8.3 alias had nothing to build and
  its own positive control caught it. **It is not skipped there**: a volume with
  no short names is a volume on which the hazard does not exist, and the test
  asserts that instead, plus the backstop that would catch a short spelling if
  one ever arrived unexpanded. Which branch ran is printed, and the
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
  nothing to refuse. Measured rather than assumed, and the review is corrected in
  place; its conclusion is untouched, because it needed one unresolved alias and
  has three.

### Changed

- **The charter said there is no CI, and there has been since 2026-08-18.**
  `DECISIONS.md`'s *Automated checks* row read *"None. No CI, no scheduled job,
  no git hook"* while `build.yml` was running the whole suite on every push. The
  fact is corrected and the decision is not: what the row argued is now true of
  the **release checklist** alone, and what is left open is narrower — whether
  the release-phase checks CI deliberately does not run (the packed release, the
  real client, `BROWSERAI_RELEASE_RUN=1`) should move into automation too. The
  `TODO.md` item that was waiting on *"a real cadence rather than a guess"* says
  so as well; half its premise had expired without it noticing.

- **The browser-reinstall row rests on a measurement of Chromium rather than on
  a retracted sentence about Windows.** The row had closed *download alongside
  and swap* on *"Windows will not rename a directory holding open executables"*;
  that was measured false on 2026-08-18 and retracted, leaving the refusal
  standing on the admission that **nothing had measured what Chromium then
  does**. It has now. Against a live headless Chromium 152.0.7977.8 running as
  ten processes with its own working directory deliberately elsewhere,
  `Directory.Move` of `chrome-win64` was refused with a **sharing violation** and
  of `chromium-1237` with **`ERROR_ACCESS_DENIED`** — and **both succeeded in the
  same script seconds later with the browser killed first**, which is the control
  that makes the two refusals Chromium's rather than the tree's. So the 2026-08-18
  retraction was right that Windows has no such general rule and **too broad in
  the other direction**. Nothing is relaxed; the position is now evidence.
  ([kb](kb/windows/processes.md#the-win32-interop-surface), re-verification row
  103.) The mechanism is `[UNVERIFIED]` — the two refusals carry different Win32
  errors, so they are not the same cause — and Firefox was not tested.

- **`@playwright/mcp` emits no progress notifications at all**, which settles the
  first of the two things the relayed-ordering decision was waiting on. All four
  occurrences of `notifications/progress` in the shipped payload are the MCP
  SDK's own schema and capability arms; `sendNotification` appears once, as the
  capability handed *to* a tool handler, and nothing in `@playwright/mcp` or
  `playwright-core`'s MCP layer calls it. Measured with a positive control, so a
  zero is an absence rather than a failed search. **The SDK's fire-and-forget
  reordering is therefore real and unreachable through this product's own child**,
  and the transport decorator that would fix it would be a component built for a
  notification nobody sends. Re-verification row 104 is what re-opens it.

- **`LongPathsEnabled` is recorded where the long-path guarantee is claimed.**
  `longPathAware` in the manifest is necessary and not sufficient — Win32 honours
  it only when the machine-wide registry value is also `1`, and a default install
  leaves it `0` — so every long-path measurement in `kb/` was conditional on a
  value nobody had written down. Read off the reference machine as `1`
  (`REG_DWORD`) on Windows 10.0.26200 and stamped `[MACHINE]`, with the half that
  is still unknown named rather than implied: nothing has run against
  `LongPathsEnabled = 0`, and the product neither checks it nor emits a
  diagnostic that would name it.

- **`CreateProcessW`'s two buffers are declared as spans rather than as one
  `char`.** `ref char lpCommandLine`, called as `ref commandLine[0]`, was weaker
  than Microsoft's own Win32 metadata for the same call in three ways at once: no
  length, an empty buffer that became an `IndexOutOfRangeException` at the indexer
  rather than the `null` the API accepts, and nothing saying which of the two
  buffers Windows writes back into. It is now `Span<char>` for the command line
  and `ReadOnlySpan<char>` for the environment — pinned, not copied, so the
  mutation still lands in our array. **Nothing was known to be wrong with the old
  shape and nothing changed at the call site**; it was a signature that presents
  as a plausible wrong answer rather than as an error. The invariants it silently
  relied on are now asserted: `InteropLayoutTests.TheTwoBuffersHandedToCreateProcessAreTerminatedAndNeverEmpty`
  is red if either buffer stops being NUL-terminated or becomes empty — and the
  empty environment block is the one that matters, because it would reach Windows
  as `null` and mean *inherit the parent's environment*, silently.

- **`.gitignore`: the three owed items, and one of them was a claim that was not
  true.** The upstream `VisualStudio.gitignore` half was re-fetched and compared
  against upstream HEAD (blob `d5a18de`, unmoved since 2026-04-17) — it has not
  changed. But the marker comment invited a **wholesale paste**, and this half is
  *not* verbatim: `artifacts/` and `.artifacts/` are root-anchored here, because
  unanchored they matched `src/BrowserAI/Artifacts/` on case-insensitive Windows
  and made five product source files invisible to git. The refresh procedure now
  says paste, re-apply, run the suite —
  `BuildConfigurationTests.NoSourceFileIsInvisibleToGit` makes forgetting a red
  build, and the comment stops the next person rediscovering why. `.vscode/mcp.json`
  is re-admitted below the marker, since
  [github/gitignore#4735](https://github.com/github/gitignore/pull/4735) is still
  open; for a project that **is** an MCP server, a workspace registration used for
  testing was silently untracked. `/staging/` was already settled and deleted on
  2026-08-16, with the reason recorded in the file — the `TODO.md` item was stale.

### Fixed

- **The reclaim pass had a bullet with no input for three days, and it read as
  though it worked.** The suite's own specification asks that *anything the
  previous run recorded is terminated by `(pid, creationFileTime)` from its own
  spawn record*. Nothing wrote a record. So a run killed mid-test left a process
  the next run could not identify — only a directory it could not delete, which
  surfaced as a locked file and named the wrong cause. `SpawnRecord` writes
  `.work\spawn-record.txt` from the two places the harness starts processes, and
  the pass reads it **before** it touches the tree, because a live process is
  what holds the files a delete cannot take. Identity is checked again before
  anything is acted on, and the regression test's middle case is the test host's
  own pid with a deliberately wrong creation time — a reclaim that matched on the
  number alone would end the run rather than fail it.

- **The failed-rewrite recovery of `lock.json` has a test.** A rewrite drops the
  handle before the replacement, because Windows will not rename over a file this
  process holds — so an exception in between left the session *silently unowned*
  while the caller was told only that a write had failed. It shipped that way
  once. The seam turned out to be neither of the two `TODO.md` predicted: not an
  injectable file operation and not a probe process holding the replacement path,
  but an **ACL** — denying `CreateFiles` on the session directory stops
  `WriteDurably`'s temp file ever existing, so the rewrite fails before any
  rename, deterministically, with nothing injected. Ownership is then asserted
  from the kernel rather than from the object under test: a stranger's
  write-access open of `lock.json` is refused while, and only while, a handle is
  really there. The second arm denies read as well, so the recovery fails too,
  and requires the answer to say the session no longer holds its directory rather
  than hand back something that reports ownership it does not have.

- **The two ungated `lock.json` readers that ACTED on an absence rather than
  reporting it are closed, one pass each.** An adversarial review enumerated
  thirteen readers that take no lock; eleven fail safe. The other two both read
  the instant in which `lock.json`'s *name is unbound* while another process
  renames a new record over it.

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

- **Two BrowserAI processes wrote holder statements into one `lock.json`, and
  what let them was a peer's *probe* — the handle it holds while it looks.** The
  probe opens `lock.json` `FileAccess.ReadWrite` in front of the per-directory
  gate, which is what makes it a sound ownership test; the same access is what an
  open sharing only `Read` is refused by. So a contender passing over the file in
  the microseconds between a writer's rename and that writer's own re-open
  refused the re-open, and the writer gave up a directory whose record already
  named it. Observed in CI on 2026-08-19, run 32203064556 attempt 1, on a 4-core
  hosted runner: `Reclaimed taken=true holderPid=2652` from one contender and
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
  owner, so it may not wait, and a peer's probe there still reads as `Contended`.
  That is narrower — a wrong sentence rather than a wrong owner, correct again on
  the next call — and it is now a row of its own in the
  [hazard index](HAZARDS.md#hazard-index) rather than an unstated residue.

- **The suite was red from Git Bash and green from PowerShell, on the same
  commit, and now the wrong comparison is red in both.** Two assertions in
  `SessionDirectoryGuardTests` compared a path composed in the test host against
  the accepted spelling inside a refusal, which comes back through
  `GetFinalPathNameByHandleW` — and Windows always answers with an **upper-case**
  drive letter while a process keeps whatever casing its shell gave it
  (`C:\…` from `pwsh`, `c:\…` from Git Bash). Measured at `cc45900`: total 484,
  **2 failed from Git Bash and 0 from PowerShell**. `Sessions\CLAUDE.md`
  predicted this exact defect and recorded that nothing asserted it; that is no
  longer true.

  **Eleven sites carried the shape, and all eleven were test assertions** — eight
  in `SessionDirectoryGuardTests`, one each in `HeadlessBinaryTests`,
  `StraySweepTests` and `SessionPathTests`, against paths read back through
  `GetFinalPathNameByHandleW`, `QueryFullProcessImageNameW`, `GetShortPathNameW`,
  `QueryDosDeviceW` and `GetCurrentDirectoryW`. Product code had none: every path
  comparison in `src\` upper-cases both sides or asks for `OrdinalIgnoreCase`,
  and the one deliberate ordinal path comparison is `ArtifactRouting.PrefixOf`,
  which separates names the product generated from names a caller chose. One of
  the eleven was choosing a *branch* rather than passing an assertion:
  `An83Spelling…` decided whether this volume generates short names by comparing
  `GetShortPathNameW`'s answer ordinally, so a re-spelled drive letter would have
  sent it down the arm that then asserts a tilde.

  **The mechanism is `DriveLetterCase`**, over which six of that class's tests are
  parameterised: the `Lower` arm composes a spelling **no Windows API ever
  returns**, so a comparison that is not case-insensitive fails on every machine
  and in every shell. It was planted and watched red *from PowerShell*,
  reproducing the two Git Bash failures exactly. ⚠️ **CI runs `pwsh` end to end
  and therefore cannot see the shell-dependent form of this at all** — which is
  why it has been reported twice from a machine and never once from a build.

- **A durable write that landed and could not be re-opened said *nothing was
  changed*, which was false at the moment it was said.**
  `SessionLock.TryAcquire` closes its handle on `lock.json`, renames a
  fully-formed record over the name and re-opens it — and the write and the
  re-open shared one `catch`. So a failure on the second one answered *"the
  directory was not taken and nothing was changed"* about a machine where
  `lock.json` had just been replaced with a record naming this process as the
  holder. A caller acting on that sentence reads the reclaim it meets on its next
  call as somebody else's crashed session rather than as its own last attempt.
  Now one `catch` per operation: a write that never landed still says nothing was
  changed, and one that did says the record **was** written, who it names, that
  nothing holds the directory, and what the next call will therefore report. **It
  deliberately does not try to undo the write** — restoring the previous record
  means a second write along the path that just refused us, and the answer would
  then have to describe a rollback that half happened. `SessionLockTests.AWriteThatLandedSaysSoAndOnlyAWriteThatDidNotSaysNothingChanged`
  holds both arms and provokes each with an ACL denying exactly one right, rather
  than with a fault-injection seam in shipped locking code. This is the second,
  separable half of the interleaving recorded below; **the window itself is
  untouched and still open.**

- ⚠️ **Not fixed, and recorded loudly: two BrowserAI processes appended holder
  statements to one `lock.json`, in CI, on 2026-08-19.** This is the interleaving
  the 2026-08-18 adversarial review predicted from reading and which nothing had
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
  cheaper defect **is** visible …" with no fix): that half **is now fixed** — see
  the entry below — and only the window itself is still open.* Both halves have
  a [hazard row](HAZARDS.md#hazard-index).
  `SessionLockTests.UnderConcurrentProcessesExactlyOneAcquiresAndEveryOtherIsToldWho`
  caught it, and it was diagnosable only because a whole-set dossier was added to
  that test on 2026-08-18 for exactly this occasion.

- **A probe wrote its report in place, so `File.Exists` was a readiness signal
  that became true in the middle of the write.** Found on 2026-08-19 by running
  the whole suite three times in a row to prove it green: one run failed with
  *"the process cannot access the file … because it is being used by another
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

- **A screenshot comes back inline again, and the defect was ours.** Upstream
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
  the wrong axis anyway. Measured off the wire against the published binary:
  the same 1280×720 viewport costs 5,105 B on a near-blank page and 52,648 B on
  one with two dozen paragraphs — a 12× spread on the wire — while the model-side
  cost is `⌈w/28⌉ × ⌈h/28⌉` = **1,196 visual tokens either way**. A gate on bytes
  would fire on the page that costs nothing extra and stay silent on the one that
  does ([kb](kb/playwright/tools-and-artifacts.md#the-inline-screenshot-and-what-it-costs--measured-2026-08-18)).

  One divergence is recorded rather than fixed: upstream shrinks anything over
  1,568 px on a side or ~1.15 MP first, and BrowserAI sends what is on disk.
  Matching it would mean a PNG/JPEG/WebP resampler inside the proxy, which is the
  scope boundary's own example of what this product must not grow.

### Removed

- **`browser_annotate` is gone from the model-facing surface.** Not refused —
  **absent**: filtered out of `tools/list` in every mode, so it costs a model no
  attention and no description budget for a call that cannot succeed. Filtering
  the surface is in scope by the charter, where renaming is not.

  Yesterday's measurement earned a refusal; read again it withdraws the tool.
  **It has no self-timeout** — the control run stood silent for a full 90 s, and
  the wait is `await new Promise(resolve => client.on("exit", …))`, whose only
  bounded arm is the daemon failing to start at 15 s. Its window is a **second,
  non-headless Chromium**. There is **no configuration in which it runs
  headless**, because the dashboard's headedness is
  `headless: !!process.env.PWTEST_DASHBOARD_APP_BIND_TITLE` — an upstream *test*
  variable no session config reaches. And it **escapes the session's
  containment**: a `detached`, `unref`'d, per-user-singleton daemon writing its
  profile into `%TEMP%`, of whose 18-process tree a parent walk found **zero**
  after the probe exited. None of that is about whether a window was promised, so
  the per-mode refusal it used to get was the wrong shape.

  **A caller that names it anyway is refused rather than forwarded**, because a
  model knows upstream's tool names from everywhere except this server's list,
  and forwarding one would hang an unattended run — the thing this product exists
  not to do.

  **What it would take to bring it back** is recorded in
  [DECISIONS](DECISIONS.md#licence-release-policy-and-the-tool-surface) and beside
  the code: a bounded call, a dashboard inside the session's own containment, and
  a headless path that does not turn on a `PWTEST_*` variable. No two of the three
  are enough.

  **What went with it**, rather than being left unreachable: the mode-keyed
  refusal row `SessionErrors.AnnotationWouldHangAWindowlessSession` (replaced by
  `AnnotationIsNotInTheSurface`, which names the absence first); the mode
  parameter on `SessionToolPolicy.Decide`; `SessionToolPolicy.Note` and
  `SessionToolSurface.AppendModeNote`, the entire description-rewrite path — so
  **every upstream description now passes through byte for byte**, which is a
  stronger property than the append-only rule it replaces and is asserted as one.
  The surface is **58** upstream tools where it was 59, in every mode, and the
  `annotations` artifact folder stays declared because that set is derived from
  upstream's bundle rather than from what this build calls.


- **The sweep's two highest-value assumptions, measured — and both decisions
  they were holding up survive.** They were the worst kind of unmeasured
  justification: each already justified a decision that had been *taken*, so
  neither could fail loudly, and confirming them was the only thing that could
  distinguish a sound decision from a lucky one.

  **`browser_annotate` on a `headless` session does open a window, and does not
  return.** Three runs against a real child on the config this product generates:
  a **visible** `Chrome_WidgetWin_1` at `100,100,1280x800` took the
  **foreground** within 1.2 s every time, and in the control arm the call was
  still silent 90 s later — the other two returned only in the same 40 ms tick
  their window disappeared, which is the human path. **So the only refusal left
  in the product is earned**, and the sentence behind it — undated, uncited and
  never measured, and contradicted on its face by this repository's own
  measurement that headless Chromium shows nothing — now carries a date, four
  versions, a method and a re-verification row. The mechanism is worse than the
  sentence said: the window belongs to a **second, non-headless Chromium** under
  a **detached, per-user-singleton** dashboard daemon, launched headed on an
  upstream *test* environment variable that no session configuration reaches, and
  writing its profile into `%TEMP%` rather than into any session directory. That
  is a hazard nobody had written down, and it now has a row.

  **The DPAPI claim that deleted the whole tool-permission layer holds.** The
  removal rested on *"the agent chooses the session directory, the profile with
  its cookie database sits inside it, and the agent runs as the same Windows
  user, so DPAPI decrypts for it"* — repeated across six documents and
  terminating in a `kb/` line with no date, no version and no method. The unasked
  question was **App-Bound Encryption**, which since Chrome 127 binds cookie
  decryption to the browser's own code identity. Measured against a session
  BrowserAI configured and nothing else: `os_crypt.app_bound_encrypted_key`
  **absent**, cookie scheme tag **`v10`** and not `v20`, and from a *separate*
  process as the same user `CryptUnprotectData` with no entropy returned the
  32-byte key and AES-256-GCM recovered the value. **DPAPI alone — no elevation,
  no service, no admin.** Stronger than "ABE could not apply here": this machine
  *has* a registered Chrome elevation service, from the operator's own install,
  and the provisioned build still produced a DPAPI-only key.

  Both are stamped in [`kb/`](kb/README.md) with how to re-establish them, cited
  from the code and from all six documents that used to cite each other, and
  covered by re-verification rows 94 and 95 — each naming what a bump would
  silently invalidate, because both are facts about upstream that today's suite
  would stay green through.

- **The justification sweep: 598 load-bearing reasons sorted, 63 assumed, 13
  settled by measurement and 11 relabelled.** Every mechanism in this repository
  protects a claim about *behaviour* — a test fails, a snapshot diffs, an
  analyzer errors. A claim about a *reason* is invisible to all of them, and a
  rule with a measured reason reads exactly like a rule with a plausible one.
  Nothing below could ever have gone red.

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
  beside a probe, `kernel32`, `user32` and `ntdll` loaded from `System32`
  with or without the attribute; only `rstrtmgr.dll` — the one library here that
  is not a KnownDLL — loaded from the application directory without it. The rule
  is kept on every declaration; the trap was the audit, since anyone testing it
  with a fake `kernel32.dll` sees nothing happen and concludes it is decorative.

  **`Debug.Assert` does not raise a modal dialog.** That is .NET Framework's
  `DefaultTraceListener` and has not been true on .NET Core. Measured on .NET 10
  with stdio redirected: a Debug build wrote the assertion to stderr and **died
  at once with exit code 35**, and a Release build ran straight past it, because
  `[Conditional("DEBUG")]` compiles the call out. So the shipped artifact carries
  a guard that does nothing and the suite one that kills the server. The ban
  stands, for better reasons than it had.

  **Also settled by measurement**: a held handle stops pid reuse — with the
  handle released a pid repeated after 2,010 spawns, with one held there was no
  repeat in 6,030, and the *control* is what makes that mean anything;
  `Marshal.GetLastPInvokeError()` survives allocation, a GC, a `MemoryStream` and
  a `Console.Out.Flush()` and is destroyed only by another capturing P/Invoke,
  while the 11 declarations without `SetLastError` return a confident `0`;
  `Console.Out` really does write CP437 and CRLF under redirection, putting
  `82 2E` on the wire for two non-ASCII characters; `UseSystemResourceKeys` saves
  **161,280 bytes** against a **111,984,018-byte** payload, both halves of a
  trade that had a word for a numerator and an unweighed denominator;
  `Console.OpenStandardOutput` returns a `WindowsConsoleStream` and not a
  `FileStream`, so the `FileOptions.Asynchronous` the comments named was never
  involved; and the SDK's version decoration appends `+<sha>`, not `.<sha>`.

  **Relabelled rather than settled**, because an admitted gap beats a confident
  sentence: *"every Node process supervisor on Windows"* (never surveyed, and it
  is the "nobody else solved this" half of the decision that chose C# for the
  whole product) · the *~640 MiB* provisioning peak (arithmetic that does not
  reach its own number, shipped as a refusal constant) · *"the holder is a
  scanner"* (rates measured, cause never established, and the retry rule
  depends on which it is) · the thread pool's injection rate (two articles a
  factor of two apart, and one of them measures injection away as the mechanism)
  · and the drift check *"runs by construction"* (it cannot fire during the quiet
  month that defines the gap it answers).

  **Three citations pointed at `plan/`**, deleted with the implementation plan on
  2026-08-17 and never re-aimed; and `Interop/CLAUDE.md` published a count of 41
  beside its own 43, under a stated re-count predicate that returns 45 because
  two doc comments mention the attribute in prose.

- **The client's *"2KB each"* is per string, and the gate was measuring the wrong
  unit.** `ClientTruncationBudget` had said for four days that the per-string
  reading was an **assumption** — the competing reading being one 2 KB bucket per
  whole serialized tool, under which `browserai_init`'s entry was over the line
  and silently truncated on every session, and under which trimming a description
  would have moved text from one capped bucket into the same capped bucket. It is
  now measured, @ **Claude Code 2.1.234**, by pointing the client at a local
  recorder through `ANTHROPIC_BASE_URL` and reading the `tools` array it sends to
  the Messages API — what the model receives, rather than what a model recalls.
  **Per string, 2,048 UTF-16 characters, cut at `> 2048`.** A probe tool with a
  4,578-byte entry arrived intact, as did entries of 17 KB and 20 KB; a
  2,048-character description weighing 6,004 bytes arrived whole, so **bytes are
  never counted**; there is no whole-surface total either — 202 tools and 348,314
  bytes of entries went through untouched; and
  `inputSchema.properties[*].description` is **not truncated at all**, 20,000
  characters included. The cut appends the literal `… [truncated]`, which the
  model can see and a server never can. `browserai_init` was never truncated.
  What changed as a result: `ClientTruncationBudget.Bytes` is now `.Characters`
  and the gate counts characters — a byte gate can only fail strings the client
  delivers whole — the parameter cap is relabelled a **house limit** rather than a
  client limit, and questions 1 and 10 in [`QUESTIONS.md`](QUESTIONS.md) are
  answered. Recorded with the method in
  [kb](kb/mcp/protocol.md#what-2kb-each-means--measured-2026-08-18--claude-code-21234)
  and re-verification row 92, because every figure in it floats with a client
  version this project does not control.

- **BrowserAI advertised an MCP capability it does not implement.** The SDK's
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

- **The test double was more capable than the thing it doubles.**
  `FakePlaywrightChild` answered `initialize` with
  `{"tools":{"listChanged":true},"logging":{}}` against a real child that
  advertises `{"tools":{}}`, so the whole in-process layer ran against
  capabilities production can never produce. It now answers the snapshot's own
  string, and `UpstreamSnapshotTests.TheDoubleAdvertisesWhatTheRealChildDoes`
  holds the two together. No test depended on the lie.

- **`browserProvisioning` said `downloading` when this process had started no
  download.** A provisioner that loses the machine-wide provisioning mutex is
  watching for another process's completion marker — and cannot see whether that
  process is downloading, extracting, or walking every process on the machine
  inside its revision prune. It rendered *"… is being downloaded into '…'"*
  anyway. The attempt now carries a phase and the sentence says what this process
  is actually doing. **The state word was left alone here** — *and renamed to
  `provisioning` later the same day; see Changed, below.* This entry stands as
  written because it is what shipped in this order: the sentence was a claim
  about the world and was fixed first, the word was a product-voice decision and
  waited for one.

- **A supported configuration warned on every startup.**
  `warn: velopack: Failed to initialize WindowsVelopackLocator` fired on every
  start of a binary Velopack did not install — every test host, and the
  configuration CI runs in — a hundred per saturation run, on the stream this
  project relies on for diagnosis. Demoted to `Debug` on three independent
  conditions; a genuine locator failure carries different text at `Error` and is
  untouched.

- **`HazardIndexTests.EveryRowIsOpenOrClosedAndNothingElse` did not enforce the
  invariant its name promises.** It asked whether the `Status` cell *contained*
  "open" or "closed", so a row reading `**half closed**` passed it for eight days
  while being in neither tally. It now matches the leading word exactly. The row
  is adjudicated on its evidence: **closed**, with the pre-check's limit stated —
  the hazard is that disk exhaustion mid-provision is *success-shaped*, and the
  source fix removes the shape.

- **Four counts in prose were wrong**, including one where a correction had
  replaced a right number with a wrong one by measuring a different predicate over
  the same table. `TODO.md`'s hazard tally said 3 rows were `open` while carrying
  evidence and 57 open in total; it was 4 and 58. `DECISIONS.md` said the
  installer is *"~117 MB"*, which was the installed payload rather than the
  installer: re-measured off the artifact `build/New-Release.ps1` packed for
  `v1.0.0`, it is **53,567,930 bytes — 51.1 MiB**.

### Changed

- **`lock.json` is schema 2: every field is an ordered list of timestamped
  statements, and `acknowledgeCopy` is gone.** The record was a snapshot with a
  history bolted onto `purpose` alone; it is now append-only, so a session says
  **how it got here** and not only where it is. `created` and `lastUsed` are no
  longer stored — they are exactly the earliest and the latest statement, and a
  stored copy could only ever disagree with what it summarises. **A statement is
  appended only when the value changes**, so `mode`, `browser`, `directory` and
  `browserAiVersion` stay one statement long for a session that is not moved,
  copied or run under a new build.
  **Growth is capped at 32 statements per field**, trimmed out of the *middle*:
  the first statement is never dropped, because `created` is read from it and a
  trim at the front would silently move a session's creation date. Worst case is
  ~70 KB, dominated by 32 purposes at their 2,000-character cap — schema 1's
  `purposeHistory` had no cap at all. **There is no converter and no
  back-compatibility**: a version-1 file is refused with the fix in the message,
  and the version is now checked in a pass of its own *before* anything else is
  parsed, because a schema-1 file is well-formed JSON whose keys this build still
  recognises by name and a version checked last reported it as damage.
  **Every refusal the strict parser made still holds** — unknown key at any of the
  three levels, missing key, unknown schema version, non-round-trippable timestamp
  — and one is new: an **empty statement list**, which has no current value and
  would otherwise surface as an index-out-of-range a long way from the file.

  **The payoff is that `browserai_resume` stops refusing a copied directory.**
  `acknowledgeCopy=true` existed because taking a copy over overwrote the only
  evidence that it *was* a copy, so the caller had to be made to say it knew. Now
  the resume appends its path to a `directory` history that still carries the
  original and **returns that history**: where the directory has been, when, and
  that the recorded purpose describes the original's work. That is strictly more
  than the refusal ever conveyed. The `DirectoryIsACopy` row is deleted from the
  error catalogue rather than orphaned, and **BrowserAI now has zero confirmation
  flags** — `ModelSurfaceTests.NoAuthoredToolAsksTheCallerToConfirmAnything`
  keeps it there, with the deleted flag's own name as the matcher's positive
  control.

- **A contender asks the kernel who holds a session before it queues for the
  right to ask.** Every process that wanted to know who held a directory took
  `LockScopes.PerDirectoryGate` — losers included, whose entire remaining
  business is to print *held by PID n, since t, for this purpose*. So a refusal
  waited behind the whole queue of peers rather than behind one critical section,
  and the cost is super-linear in contenders. `SessionLock.ProbeForHolder` opens
  `lock.json` **in front of** the gate; the sharing violation is the kernel's
  answer and no mutex made it more true. Measured before and after against a
  directory a live holder already had, 3 runs at each N on an idle machine:
  slowest refusal **329 → 30 ms** at 16 contenders, **2,084 → 203 ms** at the
  charter's design point of 100, **4,267 → 449 ms** at 200 — and the shape
  changed from `p50 ≈ max/2`, which is a queue draining one entrant at a time, to
  a cluster. **The free path is unchanged and that is the design, not caution**:
  a probe is a sound *ownership* test and an unsound *freedom* test, so anything
  that is not a sharing violation — absent, mid-rename, `UnauthorizedAccess`,
  unparseable, or an open that succeeded — falls through to the untouched
  `MachineMutex.Create` → `Acquire` → `TakeOrReport`. With the gate skipped there
  instead, both contenders probe free, the loser's rename retry loop becomes the
  serialiser, and it takes the name off a live holder's still-open handle;
  [the adversarial review](docs/reviews/2026-08-18-adversarial-locking.md) found
  that before it was built, and
  `SessionLockTests.AProbeThatFindsTheDirectoryFreeStillProvesItAtTheGate` is
  what stops it being built later. **The cold race is deliberately not improved**
  — with the directory empty at `t=0` every contender correctly probes *looks
  free* and every one falls through, so 100 contenders racing an unheld directory
  measure the same as before. The queue that goes away is the one that forms
  around a session somebody already has, which is every moment of a session's
  life after the first.

- **`browserProvisioning` now answers `provisioning` where it answered
  `downloading`.** One word covers five phases — waiting on another process's
  provisioning mutex, deleting an abandoned tree, downloading, extracting, and
  pruning superseded revisions — and **only one of them is a download**. The
  entry below fixed the *sentence* on 2026-08-18 and left the word, recording it
  in [`QUESTIONS.md`](QUESTIONS.md) §9 as the maintainer's call; the call is
  taken, and §9 is answered. Nothing about the bucketing moved: every consumer
  still branches on *installed* / *not yet* / *failed*, and the mutex-loser still
  belongs in the middle — which is why **no fourth word was added** for it, since
  no caller acts differently on the distinction. `ProvisioningState.Downloading`
  is `ProvisioningState.Provisioning`. **Both unfinished detail sentences gained
  an explicit *"wait and call the same tool again on the same session"*,** because
  `downloading` implied that recovery by itself and `provisioning` does not — a
  rename that leaves the reader with a state and no action is a rename that made
  things worse. No external consumer parses the word; this build has never
  shipped one.

### Added

- **A budget gate over every model-facing string, measured off the wire.**
  `ModelSurfaceTests.EveryModelFacingStringFitsTheClientsSilentTruncationBudget`
  drives the published NativeAOT binary over real JSON-RPC with a real
  `@playwright/mcp` child and measures three surfaces: the server `instructions`,
  every tool `description`, and **every parameter `description` inside every
  `inputSchema`** — the last of which was asserted by nothing, and is where
  BrowserAI's own injected `session` description lands on 59 upstream tools at
  once. Enumerated dynamically, so a tool added next year is covered; measured in
  characters *and* UTF-8 bytes; hard at 100% with no warning tier, because under
  the cut the text simply never reaches the model. Every length is printed sorted
  on a passing run, to `.work/description-budget.txt`. **The reading of the
  documentation's *"2KB each"* has since been measured** — see the entry below —
  so the gate now fails on characters rather than on whichever count is larger.

- **`RecordedCountTests`, which generalises the one place counting discipline was
  mechanised.** Every count a surviving document publishes about this repository
  is now checked against a live scan, through the *same* implementation that
  produces the figure: the hazard tally in `TODO.md` — **category by category, not
  just the total**, because a wrong total is only visible against the sum — the
  fragment count in `CLAUDE.md`, the tool-surface numbers in `DECISIONS.md`, and
  `kb/README.md`'s claim that no article carries `[STALE]`, with a positive
  control. Four counts it cannot mechanise are named in the class and say why.


- **The per-directory session gate refused sessions that nothing was wrong
  with, and told the caller so in as many words.**
  `LockScopes.PerDirectoryGate` was five seconds because the section it guards
  takes milliseconds — but every process naming one directory enters that gate
  *in turn* just to discover the file is held, so the wait is behind the whole
  queue. Measured on an **idle** machine: 100 contenders, the charter's design
  point, put the slowest refusal at **3,349 ms against a 5,000 ms timeout** — a
  margin of 1.49× — and 200 contenders produced **73 `Busy` refusals of 796**,
  every one at the timeout exactly. `Busy` withholds the holder's identity, which
  is the one thing the lock exists to report, and its message asserted *"something
  is wrong that waiting longer will not fix"* about a machine where waiting was
  the entire remedy. The gate was also **smaller than a wait taken inside it** —
  `RenameWindow.Budget` became 30 s on 2026-08-18 and is consumed under this
  mutex — so one entitled reader could starve every peer into a wrong answer.
  Now sixty seconds, with `SessionLockTests.TheGateOutlastsEveryWaitTakenInsideIt`
  failing the build if either number crosses the other, and re-measured at 200
  contenders with zero `Busy`. `Updates.LiveInstances` shared the constant and is
  split out at its existing five seconds, because raising it would have put a
  sixty-second stall on the startup path.
- **The suite's torn-log check had never matched a log record.**
  `SaturationTests`' record-header expression required two spaces before `pid=`,
  but `FileLoggerProvider` pads level names to five characters and then writes
  two more — so `INFO` and `WARN` are followed by three, and only `TRACE`,
  `DEBUG` and `ERROR` ever matched. Against a real hundred-process run: 2,217
  records, 2,117 `INFO` and 100 `WARN`, **zero matches**. The check that proves a
  hundred concurrent appenders cannot corrupt one file was passing by seeing
  nothing, and the count beside it — meant to stop exactly that — counted every
  log file on the machine, so a developer's months of history satisfied it before
  the test began. On a fresh CI runner it read 6; with the log directory moved
  aside locally, 0. Fixed, scoped to the run's own pids, and the same run now
  reads 2,217 headers from 100 distinct pids with none torn.
- **A test asserted a durable property on a channel that is not durable.**
  `ProtocolSplitTests` read the protocol-negotiation record from stderr, which
  `AddConsole` hands to a background processor thread, while `SliceRun` ends
  BrowserAI with `TerminateProcess` on purpose to prove containment — so the
  queue's tail went with it. Two CI runs lost different amounts, which is a queue
  and not a missing call. It now reads the process log, which
  `RollingFileWriter` writes unbuffered per record, scoped to that run's pid.
- **Five product source files were outside every scan built on the repository
  walk.** The prune list matched a directory *name* at any depth and
  case-insensitively, so `src\BrowserAI\Artifacts\` matched the root's
  `artifacts\` build output and was removed from the link check, the new
  fragment check and the SPDX house rule alike. No symptom: a prune reports
  nothing when it removes the wrong thing. Found because the fragment scan
  counted 552 where an independent count of the same corpus counted 554, and the
  walk now asserts it loses nothing the source enumeration finds.
- **A test asserted that the developer's screen was busy.** The message-window
  test proved `EnumWindows` had really enumerated by requiring more than fifty
  top-level windows — true of a desktop somebody is using and false on a CI agent,
  where a service window station holds a handful, so it would have gone red on a
  machine where nothing was wrong. The probe now publishes a second window that
  is top-level and never shown, and the assertion is disjointness by handle
  identity instead.
- **A session another BrowserAI was writing to could not be opened.** Every
  `lock.json` is replaced by an atomic rename, and while that rename is in flight
  Windows refuses every other open of the file — with `ACCESS_DENIED`, not the
  sharing violation the code was watching for. So `browserai_init` or
  `browserai_resume` against a directory a second BrowserAI happened to be
  touching at that instant failed with an unhandled exception, and the session
  list reported a perfectly good session as unreadable. Both sides of the rename
  wait the window out now. It needed two processes and a coincidence measured in
  microseconds, which is why it took running the whole test suite at once to find
  it.
- **A child that took longer than sixty seconds to start was reported as a
  protocol failure.** BrowserAI never set the MCP SDK's
  `InitializationTimeout`, so it inherited the default — sixty seconds, chosen by
  nobody here and documented nowhere — for a handshake whose far side is
  `node.exe` loading a bundled runtime. On a loaded machine that is reachable,
  and the failure it produced said `Initialization timed out` with no elapsed
  time, no child identity and none of the child's stderr in it. It is now an
  explicit ten-minute hang detector with the reasoning written on it: a child
  that has not spoken in ten minutes is not starting slowly, it is not starting.
- **`browserai_reinstall_browser` could delete the browser tree and then wait an
  hour with nothing installed.** Provisioning that could not take the
  machine-wide mutex assumed another process was mid-download and watched for the
  marker it would write. The holder is not always downloading — it keeps the
  mutex through its revision prune, which walks every process on the machine —
  so a caller that had just deleted the tree waited out the full 60-minute
  deadline for a marker nobody was going to write. It now watches for the mutex
  as well, and installs when the holder lets go without having completed the
  tree; a genuine second downloader is still never started, because the marker is
  still checked first. Reachable without any unusual sequencing: `init`
  provisions on a background thread and returns immediately.

### Added

- **Both intermittent failures now name their own state.** The race probe
  reported an outcome and a message; it now reports the holder's pid *and*
  creation time, whether that holder was running, the gate's timeout beside the
  elapsed figure, anything `TryAcquire` threw, and `lock.json` as it stood at
  that instant — and every assertion in the race carries all sixteen contenders'
  reports rather than the one that tripped. `JobObjectScope.SaidBy` waited for
  nothing and so reported *"it wrote nothing to either stream"* for a drain the
  thread pool had not scheduled; it now drains to end-of-file and tells the two
  silences apart. The sweep's real-browser arm launches Chromium with
  `--enable-logging --log-file --v=1`, because on Windows a browser that will not
  start writes to a log file and not to stderr, and puts that log and the
  machine's process, handle and commit figures into the failure.
- **CI's four skipped tests are settled rather than rediscovered.**
  [`TESTING.md`](TESTING.md#continuous-integration) records why a green CI run
  reports skips, which four they are, that the no-skip rule is about `[Skip]` in
  the tree and is enforced by `HouseRuleTests`, and that zero skipped is a
  release requirement met by cutting from a machine with every capability —
  never by softening the gate to tidy a badge.
- **The build runs on a machine nobody owns.** There was no `.github/` at all,
  so every test and every release-phase check ran only when somebody remembered
  to run it locally — invisible, on a public repository, to anyone opening a pull
  request. `.github/workflows/build.yml` builds the payload, provisions both
  browsers, publishes the NativeAOT binary and runs the **whole** suite including
  `SaturationTests`, on every push and pull request. It states its own cost in its
  header: about 204 MB of first-run browser download per run, uncached on purpose,
  because a cache key would have to be guessed ahead of the resolve it depends on.

  ⚠️ **Reversed on 2026-08-20, before this section was ever released, and both
  entries are kept.** See *Removed* below. The link to the workflow file is gone
  from this entry because the file is; the entry stays because a reader of the
  release notes should see that CI existed and why, not only that it does not.
- **The `#anchor` half of every relative link is checked too.** The link test
  resolved the path and said, honestly, that it did not resolve the fragment. A
  documentation restructure then retitled four headings and moved 53 anchored
  links across 20 files, four of them under `src\`, and not one would have gone
  red. 554 fragments are now checked against GitHub's own heading-to-slug rule,
  which is itself asserted against worked examples rather than trusted.
- **`Interop\`, `Sessions\` and `Runtime\` have working instructions of their
  own**, twelve lines each, carrying only what the mechanisms cannot say — and a
  second `PreToolUse` hook puts the two invariants no analyzer can fully catch in
  front of whoever edits the first two.
- **BrowserAI registers itself with your MCP client when it installs, and
  unregisters when it goes.** One registration at *user* scope — available in
  every repository, with no `.mcp.json`, no hook registration and no file added
  to any project. Installing is the whole of setup. It registers
  `current\BrowserAI.exe` directly, so an update replaces the binary without the
  registration ever needing to change; measured across an update and a rollback,
  the entry does not move.
- **A registration that could not happen says so, and never breaks the install.**
  No MCP client on the machine, a client that refuses, one that hangs, one that
  cannot be started — each leaves a line in the process log *and*
  `mcp-registration.json` beside the installed `current\` folder, naming the
  outcome and the one command to run by hand. Nothing about a failed
  registration is silent, and nothing about it can fail an installation.
- **Installing, updating, repairing and reinstalling all leave exactly one
  registration.** An install re-points a stale one; an update repairs a missing
  one and never overwrites arguments you added yourself.
- **The version comes from the git tag and is typed nowhere.** A build on
  `v0.1.0` is `0.1.0`; five commits later, with no new tag, it is
  `0.1.1-alpha.0.5`. Nothing to edit, forget, or get out of step with the tag,
  and an untagged build says so in its own version string — which is the whole
  of *never self-update from a build that is not a release*.
- **A build that cannot work out what version it is now fails.** A shallow clone
  or an unfetched tag makes the derivation fall back to `0.0.0`, and a binary
  that does not know what it is cannot be rolled back to or bisected against.
  The build refuses it and the message names the remedy, which is never
  guessable from the number itself.
- **This changelog**, and `build/Get-ReleaseNotes.ps1`, which extracts the
  unreleased section, refuses to produce release notes when it is empty, and
  stamps it under the version being cut.
- **The running build's version is the first line of every process log**, so
  *"which version was running when this happened"* is answerable for past runs
  as well as the current one. The process log survives an update, so a machine
  that updated itself records both versions and the moment it changed. *Now
  demonstrated rather than asserted: after a real update and a real rollback, one
  log file carries `BrowserAI 0.9.0 started` and `BrowserAI 0.9.1 started`.*
- **Silent background self-update, per-user, with a rollback that works.**
  BrowserAI installs to `%LocalAppData%` with no elevation, checks its feed off
  the message loop so a tool call stays answerable while a package is in flight,
  and swaps itself between sessions — in normal use there is no *restart to
  apply* prompt at all. A BrowserAI-only release is a **97,216-byte** delta
  against a 46.8 MiB full package.
- **An update is never applied while another BrowserAI is running.** Applying
  terminates every process under the install root, which at the concurrency this
  is designed for is every other agent's browser. The last instance to exit
  applies what the others staged.
- **Rollback is publishable as well as acceptable.** The client allows a version
  downgrade and the release script permits *monotonic **or** an explicit rollback
  republish* — both halves, because either alone is a rollback that can be
  accepted but never emitted, or emitted and never accepted.
- **`build/New-Release.ps1`**, which publishes, packs and refuses: on a `vpk`
  that does not match the Velopack library, on a version that is `0.0.0` or
  carries build metadata, on a non-monotonic release nobody stated, on anything
  in ILC's raw output, and on this build's own version string appearing in
  decorated form anywhere in the linked binary.
- **Velopack's MIT licence and a trademark disclaimer now ship inside the
  package**, in `THIRD-PARTY-NOTICES.txt` beside the binary. Both were absent
  from an otherwise releasable package: Velopack is compiled *into*
  `BrowserAI.exe`, so its licence never leaves the NuGet cache, and no upstream
  file carries a trademark disclaimer at all. The licence is copied from the
  commit the resolved package records as its source, never transcribed, and a
  Velopack bump is a red build until it has been re-fetched.
- **A release now records the resolved set beside its artifact**, emitted rather
  than assembled by hand: the three `packages.lock.json`, the payload's
  `package-lock.json`, `payload.json`, `browsers.json`, and a `manifest.json`
  stating the version, the tag, the package's SHA-256 and the resolved version
  read back out of each copy.

### Changed

- **`CLAUDE.md` is 50 lines instead of 89, and every rule names its mechanism.**
  It is the first thing an agent reads and roughly half its rules had no
  mechanism at all — indistinguishable, on the page, from the half that did.
  *Prefer a mechanism over a habit* was the best line in it and was buried in the
  last third; it is now the frame, and the rules sit in two lists that say which
  kind they are. The six-line `[STALE]` defence went (it is defined properly in
  `kb/README.md`'s conventions table) and the daily drift check went from 21
  lines to four plus a pointer, because its resolution table and its Dependabot
  reasoning already exist verbatim inside `drift-check.json`.
- **The charter is split in two, and `README.md` is a README again.** It was
  84 KB and opened with a table of settled decisions, so a first-time visitor
  scrolled past four years of argument to find out how to install anything. What
  it is, what it does, how to install it, how to use it, the scope boundary and
  the licence stay; every settled decision with an argument attached moves to a
  new [`DECISIONS.md`](DECISIONS.md), whose four date-titled tables are now
  titled by topic. No reasoning was dropped in the move and every inbound link
  was repointed.
- **The hazard index says what each step was rather than where it stood.**
  `HAZARDS.md`'s evidence cells carried 55 references to a build order that was
  deleted with the rest of the plan; each is now that step's own title, recovered
  from git history. No hazard was re-adjudicated.

### Fixed

- **Closing the process log did not close its file.** Disposing the logging stack
  was written to close the rolling log handle through the logger factory, and the
  factory never disposes a provider it was handed rather than asked to create —
  so the handle survived, measured at zero disposals. It cost nothing while the
  only caller was a process about to exit, and it surfaced the moment something
  short-lived opened a log and read it back. The writer is now closed explicitly,
  and a test fails if that call is removed.
- **A second BrowserAI starting up deleted a running one's working files.** The
  per-run instance directory holds the generated config and the surface child's
  browser profile, and the startup sweep reclaimed abandoned ones with
  `Directory.Delete(path, recursive: true)` on the belief that Windows refuses to
  delete a directory a live process is sitting in. Windows refuses to remove the
  *directory* and does not refuse to delete the *files inside it*, so the sweep
  emptied a live run's directory completely, failed on the empty node that was
  left, and swallowed the exception — every start, against any instance more than
  five minutes old, with nothing written anywhere. The liveness check is now a
  rename, which is refused with the contents untouched and is also an atomic
  claim between two BrowserAIs sweeping the same root.
- **An instance directory that would not go now says which file held it.** The
  same delete went through the framework primitive, which reports one node out of
  however many survived, inside a catch that discarded even that. It now uses the
  product's one recursive delete — post-order, per node — and logs every survivor
  by name at Warning. The next run's sweep still tries again; what changed is
  that a leftover is attributable.
- **A `session.json` or a session roll-up that could not be written was
  silent — while the same answer named its path.** Both writes are best-effort by
  design, because a virus scanner holding a file open must not turn a screenshot
  that was taken into a screenshot that failed. Both discarded the answer that
  said whether it happened, so a caller was handed the path of a file that might
  be stale or absent. The answer now says so, in the line that names the file,
  for the artifact index and for the per-root roll-up in `init`, `resume` and
  `destroy` alike.

- **Two licences that had to travel with the binary were not travelling.**
  `ModelContextProtocol` and `ModelContextProtocol.Core` are Apache-2.0 and
  seventeen `Microsoft.Extensions.*` assemblies are MIT; all nineteen are
  compiled into `BrowserAI.exe` exactly as Velopack is, and a NuGet package's
  licence stays in the machine's package cache — it is never copied to a publish
  output, so *linked in* and *its notice ships* are independent facts and the
  second was false for both. Apache-2.0 §4(a) requires a copy of the licence to
  reach every recipient, which is stricter than MIT's notice clause rather than
  looser. All of it now ships in `THIRD-PARTY-NOTICES.txt` beside the binary.
  Upstream's MCP `LICENSE` turned out to grant **three** licences rather than
  one — Apache-2.0, MIT for contributions whose authors never consented to
  relicensing, and CC-BY-4.0 for documentation — so it is reproduced whole, and
  its Apache half **ends at *END OF TERMS AND CONDITIONS* and omits the appendix
  its own §4 points at**, which is upstream's file as published and is left
  unaltered. **The `Microsoft.Extensions.*` list is derived from
  `packages.lock.json` rather than typed**, so a package entering the closure on
  a later bump is a red build here rather than a licence nobody noticed had
  arrived.
- **A suite run that exercised nothing reported exactly what a real one
  reports.** With the whole publish directory moved aside — no binary, no
  browser ever started — the suite returned `329 total · 328 succeeded ·
  1 skipped · exit 0`, character for character a healthy run's summary, because
  thirty-five guards returned early after asserting something weaker and every
  one reported as a pass. They now report as **skipped**, so the run's own
  counts differ; every run ends with a block naming what it did and did not
  exercise; and with `BROWSERAI_RELEASE_RUN=1` a missing capability is a
  **failure** naming the command that produces it. This was the project's
  founding failure class living inside its own release gate.

- **37 MB of every release was a zip nobody reads.** The published artifact
  carried `payload\.cache\node-<ver>-win-x64.zip`, the download cache the payload
  build keeps so a re-run does not re-fetch Node. Excluding it took the full
  package from **85,348,009** to **49,043,498 bytes** — 42.5% of every download,
  for a file that is never opened at runtime and compresses to nothing because it
  is already compressed.

- **`UseSystemResourceKeys` is asserted rather than merely set.** It strips the
  framework's exception message strings, and this product's error text is read
  by a model deciding what to do next. The property was correct and guarded by
  nothing, which is the state a size optimisation walks into.

- **Every session's `lock.json` would have recorded the wrong version.** The
  build stamp was read from the assembly version, which the versioning mechanism
  fixes at `{Major}.0.0.0` by design — so the whole 0.x line would have written
  `0.0.0.0`, and the whole 1.x line `1.0.0.0`, into the one record a support
  question starts from. It now records the version the build was actually
  derived as. Measured on the artifact rather than reasoned about: at `v0.1.0`
  the assembly version really is `0.0.0.0`.

### Changed

- **The stray-browser sweep now has one trigger instead of two.** The logon
  scheduled task is dropped: it cannot be registered without elevation on a
  UAC-filtered administrator token, and a per-user install has no elevation to
  offer. BrowserAI's own startup sweep already covers the case that matters — a
  stray browser matters when something is about to contend for its profile lock,
  and that is exactly when a client starts.
- **One place answers "what version is this binary".** Two implementations
  disagreed — one read the informational version, the other the assembly
  version — and the version now has a single source that reads the informational
  version and never the assembly version.
- **The SDK is forbidden from decorating the version string**, repository-wide.
  Left on, it appends the 40-character commit sha to a version that has already
  been published, which is invisible until an update path that *matches*
  versions rather than comparing them starts downloading the binary it is
  already running, on a loop, forever.

## [0.1.0] - 2026-08-16

The first tagged version. *No artifact has been published from it* — packaging
and updates are still to come — so this section records what the product does at
the tag rather than what anyone has installed.

### Added

- **Browser automation for AI agents on Windows, as one MCP server that brings
  its own everything.** BrowserAI ships its own Node runtime and its own
  `@playwright/mcp`, downloads its own browser on first use, and proxies
  JSON-RPC to it. Nothing on the machine has to be installed, on `PATH`, or of
  any particular version.
- **Sessions that survive the agent that made them.** A session is a directory
  the caller names: the browser profile, its downloads, its artifacts, its log
  and the lock that owns it all live inside it. Close the agent, come back
  tomorrow, `browserai_resume` the same directory, and the cookies, the logins
  and the local storage are still there.
- **Three session modes, one table.** `headless`, `interactive` and `persistent`
  differ in what they may do, and the same table renders the server
  instructions, the tool descriptions, the refusal messages and the enforcement
  — so a mode cannot mean one thing to a model and another to the code.
- **Every tool call is judged against the mode of the session it names.**
  Deny-by-default in two dimensions: a tool nobody has classified is refused
  everywhere, and a mode with no policy row permits nothing.
- **Artifacts land inside the session, not wherever the browser felt like.**
  Screenshots, PDFs, downloads and traces are routed by type into folders under
  the session directory, never overwrite each other, and every answer says where
  the file went — as an absolute path and as a session-relative one.
- **A path that would escape the session is refused with a sentence saying
  why**, decided on the string and without touching the filesystem.
- **First-run browser provisioning that does not block the conversation.**
  `browserai_init` returns immediately and says a download is running; browser
  tools are refused with a recovery until it lands; the same child then
  navigates with no restart. A run that fails halfway removes its partial tree
  rather than leaving something that looks installed.
- **Firefox as well as Chromium**, including the profile-lock preflight that
  turns *"a modal dialog blocks startup for three minutes"* into a refusal in
  milliseconds.
- **Nothing is left running.** Every child and every browser it starts is
  created inside a job object, so killing BrowserAI — however abruptly — takes
  the whole tree with it. A stray browser from a previous crash is found on
  startup and ended only when the session directory that owns it is provably
  free.
- **Answers arrive exactly as the browser produced them.** The proxy splices the
  child's own bytes into the caller's frame rather than re-serialising, so
  escapes, numeric form, key order, unknown fields and unknown content types all
  survive unchanged.
- **Failures say what happened and what to do next.** One error catalogue,
  written for the model that has to act on it, with every row provoked by a real
  condition rather than asserted to exist.
- **The log is a log.** Everything outside a session goes to one rolling process
  log that survives updates; everything inside one goes to a log beside that
  session's lock. Nothing anywhere can write to the protocol channel by
  accident.
- **Upstream cannot move underneath it silently.** Four snapshots of
  `@playwright/mcp`'s surface are regenerated from the resolved package on every
  build and diffed against committed copies, and the build fails with the diff
  itself when anything moves.
- **Every dependency floats to latest and the build freezes what it resolved**,
  so the resolved set is recorded beside the artifact rather than remembered.
