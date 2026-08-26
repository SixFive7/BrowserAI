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

### Changed

- **The release manifest records seven files, not six: `tool-verdicts.json` is
  the seventh.** It states which tools a build forwards and which upstream
  versions that judgement was made against, so a release that cannot produce it
  cannot answer why it refused a tool the next release allows. The manifest also
  carries the file's own `judgedAgainst` pair, read back out of the copy rather
  than transcribed.

- **Every release builds against the latest Playwright, and the one override is
  a human's.** Written down rather than assumed: a human may force a crunch
  override and **an agent may never**, and an override has to leave a trace in
  the release artifact — the manifest states the version that shipped, and the
  checklist item asks for what was held, at what version, and why.

- **Adversarial and hostile-caller defence is a stated non-goal.** The premise is
  that the model tries to behave and what BrowserAI steers is honest mistakes;
  guarding against a caller that owns the session directory, the profile and the
  same Windows user is *"hopeless and thus meaningless"*. Several arguments over
  the last week would have been a paragraph rather than a day had this been
  written down, and it retires a standing question about headless-with-storage
  that had been open since 2026-08-18.

- **Two hazards that lost their protection when the filename gate went are
  adjudicated and stay open.** A reused output name still overwrites and a name
  Windows will not keep verbatim is still stored as Windows rewrites it. The
  decision is **steer only, plus an upstream ask** — no door refusal on the
  string alone — and both rows say so now, in place, without closing.

- **A just-answered call can read as still in flight, and that is now written
  down.** The log row is settled in a `finally` that runs after the answer is
  sent, so a second agent's `browserai_catch_up` landing in that window is told
  no answer was recorded about a call that has just been answered. The ordering
  is deliberate — settling first would risk a `successful` row for an answer the
  caller never received — and the window is a hazard row rather than a fix.

### Fixed

- **A suite arm that needed its own stray sweep to have run was losing the
  machine-wide gate to a real BrowserAI another test had just started**, once in
  five full runs. The sweep's pass may now be handed a patience, the suite's own
  passes **wait** on the gate instead of asking again in a loop, and a scan over
  `src\` holds that the product never waits — ninety-nine peers queueing to redo
  one pass is the thundering herd the zero timeout exists to prevent. Reproduced
  deterministically by holding the gate from another process, which is also how
  the fix was watched.

- **The two literals the session guard is made of are now held by a test.**
  `LockFile.Hold`'s `FileShare.Read` is one writer per directory; the probe's
  `FileAccess.ReadWrite` is what a holder's share mode can refuse. Widening the
  first lets two BrowserAIs drive one profile and narrowing the second reports
  every driven session as free — and **both perturbations leave a file that
  opens**, so every behavioural test in the suite stayed green under each.

- **The re-verification index's gate now reads around a `previously "…"` clause,
  the way the hazard index's already did.** A superseded test name quoted the way
  `CLAUDE.md` requires — verbatim, in backticks — failed one gate and passed the
  other, so two rows of that index had been left quoting dead names *without*
  backticks and explaining the gate in prose. Both read as corrections again, and
  the clause has one definition both gates ask.

### Changed

- **An aliased session directory is resolved rather than refused, and the network
  refusal now runs at every door.** `\\?\C:\work\sess`, a `subst`ed drive letter,
  a junction, a directory symlink and a mount point are all taken as the
  directory they name: BrowserAI records the spelling the filesystem itself uses
  and says so once, in the `init` or `resume` answer, instead of spending a turn
  refusing and asking the caller to type the answer back. Every one of those
  answers was already being computed to build that refusal, so this costs no
  syscall at all. **What is still refused is refused everywhere** — at
  `browserai_destroy`, `browserai_set_purpose`, `browserai_catch_up` and
  `browserai_list` as well as at `init` and `resume`: a UNC path, a mapped drive
  letter, the `\\.\` device namespace, and a name Windows would silently rewrite
  (a segment ending in a dot or a space, a reserved device name such as `NUL`, an
  alternate data stream, a wildcard, a control character).

  ⚠️ **Every session recorded from a shell that spelled the drive letter
  lower-case gains one `directory` statement on its next resume, and that is the
  whole of what this looks like from the outside.** Windows reports every path
  with an upper-case drive letter, so `c:\work\sess` becomes `C:\work\sess` in
  answers and in `browserai.data`. Nothing hashed moves — the identity is
  case-folded — so no mutex, no index entry and no lock file changes for an
  ordinary local directory. A session whose own path traverses a junction, a
  `subst` or a `\\?\` spelling **does** change identity: its index entry is swept
  rather than orphaned, and the next `init` or `resume` records it again.

- **`browserai_list` pointed at an alias of a tree now finds the sessions under
  it.** It had a path chain of its own — `Path.GetFullPath` plus an upper-cased
  prefix, with no alias resolution — because the shared one refused a volume root
  and a volume root is exactly what a caller passes to see everything. So a
  listing of `D:\link\work` where the sessions live under `C:\real\work` answered
  *"No BrowserAI sessions under '…'. That is an answer rather than an error"*:
  confident, wrong, and not a refusal, so there was nothing to correct.

### Removed

- **`consoleLevel`, and the four-level choice behind it.** The console level is
  now `debug` **always**, with no argument. Measured: `error`→`debug` costs **+1
  character** on a navigation response and **+5** otherwise, because the events
  line in a tool response is a *pointer* — `path#L1-L20` — and never the message
  text, so the whole cost of the most verbose setting is the width of a larger
  line number. And the read-level knob already exists one layer up:
  `browser_console_messages` takes its own level, so a caller that wants only
  errors asks for only errors **at the moment it asks** — where a capture level
  chosen hours earlier at `init` cannot be raised retroactively.

- **Session modes.** `headless`, `interactive` and `persistent` are gone, and so
  is the `mode` argument on `browserai_init` and the refusal `browserai_resume`
  made when it was given one. **A mode was two things** — whether a window
  appeared, and which upstream capabilities the session's child was launched with
  — and the two went different ways. **The window became a per-run argument**,
  `headed`, on `init` and on `resume`: it is regenerated at every child launch
  beside `tracing` and `debug`, and is **not** written to the session record, so
  a session created headless is watched headed tomorrow without being destroyed
  and recreated first. **The capability set became every capability upstream
  declares.**

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

  **`browserai.json` moved to schema 3** in the same change, because `mode` was a
  recorded field and a field nobody reads is worse than no field. There is no
  converter and there will not be one: a version-2 record is refused with the
  recovery it has always carried — delete it and call `browserai_init` on the
  directory again; the profile, output and downloads beside it are untouched and
  the new session goes on using them. **What is lost is the recorded purpose and
  the history.**

  **What went with it:** `SessionMode.cs` entirely — the enum, the one table and
  the six consumers that rendered it; the mode line in `browserai_init`'s and
  `browserai_resume`'s answers; the `mode:` column in `browserai_list`; the mode
  in `browserai_destroy`'s summary and in `browserai_init`'s already-a-session
  refusal; the `mode` field in the per-root roll-up, which moved to schema 2 with
  it. `browser` is now the only thing `init` binds permanently and the only
  argument `resume` refuses — for the reason that always separated it from
  `mode`: a profile on disk belongs to the browser that made it.

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

- **The corpus every tree-as-text rule reads is now asserted against
  `git ls-files`, in both directions.**
  `HouseRuleTests.TheScannedCorpusIsExactlyWhatGitSaysTheRepositoryHolds`
  compares `RepositoryLayout`'s walk against
  `git ls-files --cached --others --exclude-standard`, filtered through one
  shared predicate so the two sides cannot ask different questions. **The
  remark it replaces was false by 520 files**: the walk claimed to yield "the
  same 215 files as `git ls-files`", verified once by hand on 2026-08-17, and
  while agent worktrees sat under `.claude\worktrees\` — ignored by git,
  not pruned by the walk — every scan built on that list read a second
  checkout as repository content. The fragment scan counted **2,378** against a
  real **797**, and three gate arms went red for a reason no message named.
  **`.claude` is deliberately still not pruned**: `settings.json` and `hooks\`
  are committed, so a prune would have traded one blind spot for another. Git is
  an **oracle** here and never a source of truth — absent, the new
  `SuiteCapability.Git` reads ABSENT in the coverage block and the arm skips
  loudly, and a release run fails. Planted red both ways before it was trusted.

- **`SaturationTests`' torn-record arm is scoped to the run's own pids**, and a
  second arm plants the fault it can no longer plant live. The hundred-process
  arm reads the machine-wide process log with **no time filter** —
  deliberately, because NTFS does not keep an mtime current while a hundred
  handles are open on the file and a filter there once hid the only file that
  mattered — so **one torn record written by any BrowserAI from any
  checkout failed it on every later run until the log rolled, and no checkout
  could clear it**. A sibling's tear at 03:38 failed a run two hours later. The
  strength is unchanged for this run's writes: a tear counts if **either** end
  of it is one of this run's pids, because our write failing to be atomic
  against a stranger's is the same defect seen from the other side. Watched red
  with a synthetic tear in the shared log (1 m 21 s to fail),
  then green with the same plant in place.

- **The `--treenode-filter` trap, in
  [`kb/toolchain.md`](kb/toolchain.md).** The alternation character is an OR
  **inside one path segment** and never between path patterns: six class
  patterns joined with it select the **whole assembly** in one arrangement and
  the **first class only** in another, both reporting a clean pass, and one of
  them did so while two of the tests it claimed to cover were red. The proof it
  is not an OR at all is a pair that individually match nothing and together
  match everything. The correct syntax, the counts side by side and a
  re-establishment procedure are in the article; the rule — **a filtered
  run is a development convenience, never a verification** — is in
  [`CLAUDE.md`](CLAUDE.md), in the list of rules that need a person, with the
  reason no mechanism can close it stated there rather than implied.

- **The 2026-08-24 adversarial review is a dated record rather than a file in
  `.work\`.** It is the read-only pass over the seams where the 2026-08-20/24
  changes — the mode removal, the injected `why`, the durable action log,
  `browserai_catch_up`, the reader/writer maintenance lock and the
  instance-directory marker — meet the code that predates them: nine findings
  and fourteen things attacked that held. It went into `docs/reviews/` **and
  into the append-only seal in the same change**, which is what
  `AppendOnlyRecordTests`' second arm exists to force — the newest review is the
  one likeliest to be registered by nobody, and an unsealed record is one
  nothing would notice being rewritten. Nothing in it has been acted on yet, and
  the index's status table says so.

- **Every run now says whether it could have seen a browser take the
  foreground — because on this machine it could not.** `JobLauncher` sets
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

- **The server `instructions` now say what a full-page screenshot costs, before
  a model reaches for one.** One sentence: *"'fullPage: true' costs the per-image
  token maximum on any page worth using it on: it leaves at full document height
  and is downscaled to that ceiling."* The arithmetic behind it, measured
  2026-08-20: a viewport shot at the 1920x1080 default arrives as **2,691 visual
  tokens**; the same page with `fullPage: true` over a 3,637 px document leaves
  as 1920x3637, which is `⌈1920/28⌉ × ⌈3637/28⌉ =` **8,970 patches**, and the API
  downscales that to its per-image ceiling of **4,784**. Break-even is a document
  about 1,960 px tall, so *every* full-page shot of a page long enough to want
  one costs the maximum — and returns less detail than the viewport shot it
  replaced, because BrowserAI diverges before upstream's
  `scaleImageToFitMessage` and appends what is on disk.

  **It is in the `instructions` and not on `browser_take_screenshot`**, which is
  the instinctive place to put it and the wrong one: every upstream description
  passes through this proxy byte for byte, and the append path that would have
  made this possible was deleted on 2026-08-18.
  `ModelSurfaceTests.TheFullPageScreenshotCostIsInTheInstructionsAndNotOnTheToolsDescription`
  holds both halves, and was watched red on each of them separately — once with
  the sentence removed, once with it appended to the tool. **The string now
  stands at 2,028 characters of the 2,048 the client silently truncates at**
  (2,038 bytes, which is not the figure the client counts), leaving **20**
  — measured off the published binary's own `initialize` response, *previously
  1,876 of 2,048*.

- **The dated records are append-only, and a test says so.** Released
  `CHANGELOG.md` sections and every body under `docs/reviews/` are sealed by
  `AppendOnlyRecordTests` — prefix, character count and SHA-256 — so an addendum
  may be appended and a body may not be rewritten or truncated. **The failure it
  exists for had already happened**: the `lock.json` → `browserai.json` rename
  swept the whole tree, reached both, and produced a 2026-08-18 review claiming a
  filename that did not exist for another two days. Nothing failed; a human
  reading the diff caught it, and the rule was prose in
  `docs/reviews/README.md`. It was planted by re-applying that exact sweep and
  watched red on four records at once, `CHANGELOG.md#0.1.0` among them.

  **It is deliberately narrow.** `docs/reviews/README.md` is *not* sealed — it
  carries the status table, which is meant to move as findings are acted on — and
  the changelog's `[Unreleased]` section is not sealed either, because it is not
  a record of anything until a release stamps it. A typo fix in a review stays
  legitimate: it means changing the seal in the same commit, which is a line in
  the diff rather than a silent rewrite. [Release checklist item
  10](RELEASING.md#10-the-changelogs-unreleased-section-is-not-empty) now
  registers the section a release stamps.

- **Six per-run arguments, and four opinions that stopped being arguments.**
  `viewport`, `locale`, `timezone`, `ignoreHTTPSErrors` and `captureNetwork`
  join `headed`, `tracing` and `debug` on both `browserai_init` and
  `browserai_resume`; every one of them is regenerated at each child launch and
  written to nothing, so a session created headless at one viewport is resumed
  headed at another without being destroyed first.

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
  guessed**, because a Windows identifier fails the launch rather than degrading.

  **`captureNetwork` writes an HTTP Archive, and sets `serviceWorkers: "block"`
  with it.** The block is not optional: a request served out of a worker's cache
  never reaches the network layer the archive is written from, so without it the
  capture is **silently incomplete** — in the direction that matters, because a
  worker serves the repeat requests. The description carries three things a
  caller has to know before turning it on: it **changes what the site does**, it
  takes effect at the **next browser launch** and is never retroactive, and the
  file is a **plaintext credential dump**. **Each launch gets its own timestamped
  filename** under `output\network\`: `recordHar` truncates whatever path it is
  given at every context creation, and the config is regenerated per launch, so
  the overwrite-on-resume is avoidable rather than documentable. The answer
  `browserai_init` returns names the file and says both things.

  ⚠️ **`permissions: ["clipboard-read"]` is hard-coded, and for Chromium only.**
  `clipboard-write` is already granted without asking, so naming it would be an
  opinion with no effect. **Firefox does not know the permission at all**: a
  context created with it fails at `initializeServer` with `Unknown permission:
  clipboard-read` and the browser exits, so writing it for both families makes
  every Firefox session **unusable** rather than degraded. Measured 2026-08-20
  against the provisioned `firefox-1539` — by writing it for both families and
  watching a real front-door navigation go red —
  [recorded in kb](kb/playwright/configuration.md#silent-config-failures) with a
  re-verification row. It is family-scoped exactly as `channel` is.

  **`codegen: "none"` and `snapshot.boxes: true` are hard-coded with no
  argument.** `codegen` strips a `### Ran Playwright code` block from every
  response, for a feature this product does not have and no reader exists for.
  `snapshot.boxes` costs nothing until something reads the snapshot — a response
  carries a link rather than the text — and every session is granted the `vision`
  capability, whose six `browser_mouse_*_xy` tools take viewport coordinates a
  snapshot without boxes gives a model no way to compute.

- **`browserai_catch_up`, the seventh authored tool.** It answers *what were we
  doing here, and what is here now* for one session, from **two sources that
  routinely disagree** — which is the whole point rather than a caveat. The
  **log** says what BrowserAI *did*: every browser call and every purpose change,
  in order, with what the caller said each was for. The **directory** says what is
  *true now*: age, when it was last touched, total size, and a breakdown by
  artifact kind.

  **The disagreement that matters is credentials.** Cookies arrive from
  *navigation* rather than from tools, so a session whose log shows no
  `browser_cookie_*` call at all can hold a live signed-in profile — a log-only
  answer would say *"no credential tools were used"* about exactly that
  directory. It reports the profile's cookie store when there is one, and names
  any **HTTP Archive** it finds: a HAR records every request and response
  including headers, so every bearer token and session cookie that crossed the
  wire is in it in clear text.

  **Its description says when to call it**: on arriving at a session someone else
  was driving, and before destroying one — because the size and the breakdown are
  the only things that say what is about to be deleted.

  ⚠️ **It is the one session-scoped tool with no `why`**, and that is deliberate
  twice over: a tool whose whole purpose is to tell you what happened must not
  itself become the most recent thing that happened, and writing an entry would
  mean replacing `browserai.json` — which a session another live BrowserAI is
  driving refuses through its own `FileShare.Read`, and that is precisely the case
  it exists for. It is **read-only and takes no lock it can be refused by**,
  asserted byte-for-byte against a record a live session is holding.

  ⚠️ ***Corrected 2026-08-24 (previously "writing an entry would mean taking the
  per-directory gate — which a session another live BrowserAI is driving would
  refuse … It is **read-only and takes no lock at all**").*** Two wrong claims in
  one paragraph, both corrected elsewhere in the same commit and missed here:
  `LockScopes.PerDirectoryGate` does not refuse a second writer, it **waits** 120
  seconds for one — what refuses one is the holder's share mode on
  `browserai.json`; and since 2026-08-24 `catch_up` does take a lock, briefly —
  `SessionManager.InUse` reaches `SessionLock.ProbeLivenessUnderTheGate`, which
  holds that gate at a **zero** timeout for one open and close, so it can be
  undetermined but never queued.

- **One time-ordered log, inside `browserai.json`.** `browserai_init`'s
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
  would have been `O(entry)`.
  [QUESTIONS.md §14](QUESTIONS.md#14-the-one-time-ordered-log-lives-inside-browseraijson--decided-by-the-maintainer-over-my-recommendation)
  records both sides and what reversing it would cost.

  ⚠️ **A call whose entry cannot be written is refused, and never reaches the
  browser** — `SessionErrors.SessionLogCouldNotBeWritten`, the catalogue's 27th
  row. The value of one log is that reading it back tells you what the session
  did; a gap nobody is told about is worse than a refusal somebody can act on.
  **A call BrowserAI refuses leaves no entry**, which is the same rule from the
  other side: this records what the session *did*, and the refusals are in
  `browserai.log` beside it.

  **What is stored for an argument, since nothing instructed it.** Every argument
  **name**, always — a reader must see that a password field was filled even when
  the value is not there. Then: `value` and `text` are **never** stored, at any
  length, recorded as `<withheld, N characters>` — they are the two scalar
  parameters upstream uses for something a person typed or a server set, on
  `browser_cookie_set`, `browser_localstorage_set`, `browser_sessionstorage_set`
  and `browser_type`, and **the list is asserted against the golden snapshot** so
  an upstream rename is a red build; an object or an array becomes a **shape**,
  `<object, N keys>` / `<array, N items>`, which is what `browser_fill_form`'s
  `fields` and `browser_route`'s `headers` become; and everything else is stored
  verbatim to 200 characters and then **cut with a count**, which is what turns a
  `browser_evaluate` body from a transcript into a summary. ⚠️ **It is not a
  redaction boundary**: the log sits beside the profile whose cookie database
  holds the same credentials. What it buys is that a password is not written into
  the one file a model is invited to read back.

  **The log is capped at 250 entries and trimmed out of the middle**, keeping
  entry zero — `browserai_init`, the only statement of why the directory exists —
  exactly as the statement lists keep their first statement. A record at the cap
  says so, and every answer that reads one says entries *may* have been elided.

  **`browserai_list`'s "last used" now means what it says.** It is read from the
  log's newest entry when there is one: a session driven for an hour without its
  purpose or its holder changing appended nothing to any statement list, so the
  figure used to be *when the session was opened*. `created` is deliberately
  unchanged — it is the first statement of every field, and the trim never
  removes one.

- **A required `why` on every call that names a session.** Every upstream browser
  tool, plus `browserai_resume`, `browserai_destroy` and
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

  **The description does the steering, and parameter descriptions are uncapped**,
  so the long text is there rather than in the tool description that is capped at
  2,048 characters. It asks for **why rather than what**, because the tool name
  already says what: *"checking whether the login survived the redirect"* beats
  *"clicking the submit button"*. A call that omits it is refused **before
  anything is forwarded**, and the refusal — `SessionErrors.WhyMissing`, the
  catalogue's 26th row — says what to write rather than only that something is
  missing, because a model told *"'why' is required"* retries with a restatement
  that satisfies the schema and records nothing.

  **Where it goes for now:** the session's own `browserai.log`, written before the
  call is forwarded so that a call which never returns still left a record of
  what it was for. A closed session has no log of its own open, so
  `browserai_set_purpose` against one writes to the machine-wide process log with
  the directory named.

- **Ten tools, reachable for the first time in this product's history or its
  predecessor's.** Upstream's `network` capability — `browser_route`,
  `browser_route_list`, `browser_unroute`, `browser_network_state_set` — its
  `pdf` capability (`browser_pdf_save`) and its `testing` capability
  (`browser_generate_locator`, `browser_verify_element_visible`,
  `browser_verify_text_visible`, `browser_verify_list_visible`,
  `browser_verify_value`) had **never been named in a generated config at all**,
  so those ten tools did not exist in any session's child and a caller naming one
  was told by upstream that it did not know the tool. The advertised surface is
  now **68 of the 69** a fully-capable child exposes; it was 58 of 59.

  **Recorded as a deliberate grant rather than left as a consequence.** They
  arrived because session modes were deleted, and a side effect nothing names
  reads as an accident at the next review — so the list is
  `SessionToolSurface.NewlyGrantedTools`, asserted by name in process by
  `ModelSurfaceTests` and off the wire by `VerticalSliceTests`, against a
  hand-written expectation that fails whichever side moves. The decision went
  against a recommendation to weigh the three new capabilities separately;
  [QUESTIONS.md §13](QUESTIONS.md#13-the-ten-newly-granted-tools--decided-by-the-maintainer-over-my-recommendation)
  records what that recommendation was and why it was wrong about the framing.

  ⚠️ **`browser_run_code_unsafe` is not among them and never was.** It is `core`,
  so it has been reachable in every session this product has ever opened —
  `headless` included — and it reaches the cookie jar.

- **A warning in the server `instructions` that response mocking can make a page
  lie to a human.** `browser_route` installs a rule and the browser then renders
  the mock as if it came from the server: the address bar keeps the real origin,
  and nothing on screen says a rule is in force, so a person watching a headed
  window is looking at something the agent made up. **It is in BrowserAI's own
  string rather than appended to the tool's description**, because every upstream
  description passes through byte for byte and the one rewrite path that could
  have done it was deleted on 2026-08-18. The space it occupies is the space the
  three mode lines used to.

- **`browserai_list` now says whether each session it reports is being driven,
  three-valued.** It carried mode, browser, purpose, dates and size and performed
  no liveness check at all, so a caller could not tell an abandoned session from
  one another agent was inside — which is the distinction that matters most in the
  turn before `browserai_destroy`. Each entry now carries an `in use:` line:
  **YES** when this BrowserAI is driving it or when something holds its
  `lock.json`, **no** when nothing held it at the instant of the look, and
  **UNKNOWN**, with the reason, when the question could not be settled.

  **Through the pre-gate probe, `SessionLock.ProbeLiveness`, and never the
  process-liveness check.** It needs no process handle, so a token that may not
  open the peer cannot defeat it, and it asks about the resource rather than about
  a pid Windows may have recycled. Cost is **one `CreateFile`/`CloseHandle` per
  entry** — measured at 0.035 ms free and 0.049 ms held, against 0.6–2.3 ms for
  the `SizeOnDisk` walk the same loop already performs per entry
  ([kb](kb/windows/detection.md#the-pre-gate-probe-as-a-liveness-report--measured-2026-08-20)) —
  and the loop is over the session *index*, which the `directory` argument filters
  rather than walks, so a drive-root listing adds one open per known session and
  never one per file on the volume.

  ⚠️ **The holder is deliberately not named, and that is the trap this was built
  around.** A sharing violation says the file is held and never by whom; the
  record inside can describe a previous holder. Printing *"held by PID n"* would
  publish, on every listing, the wrong *sentence* the ownership work recorded on
  2026-08-19. `SessionListTests.ListSaysWhichSessionsAreInUseAndNeverPrintsCouldNotTellAsFree`
  asserts that absence as well as the four positive answers, each with its own
  control.

- **Liveness is three-valued: `Alone`, `NotAlone(n)` or `Undetermined(why)`.**
  `LiveInstances` answered `false` for *not alone* and for *could not tell* at
  once — a failed join, an expired gate, an unreadable marker. That was written
  for the updater, where both mean *do not apply* and the safe direction is the
  same one; for anything that repairs rather than refrains it is the unsafe
  direction, because the refusal is permanent and has nothing to act on. `Census`
  now returns the reason — a path, a mutex name, an exception's own message — so a
  refusal built on one can be diagnosed.

  **The updater did not move, and that is a guarantee rather than an intention.**
  `UpdateService`'s call site is untouched, `AmIAlone` is one expression over
  `Census` with exactly one `true` arm, and two tests hold it: one over all three
  states directly, one through `UpdateService` itself requiring an undetermined
  census to stage and apply nothing, exactly as a not-alone census does.

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

- **`browserai_list` no longer opens and strictly parses every session record on
  the machine to print the few under a prefix — and neither does `init` or
  `resume`.**
  [Adversarial review F9](docs/reviews/2026-08-24-adversarial-since-the-mode-drop.md).
  The subtree filter ran on the wrong side of the parse: `SessionIndex.Follow`
  opened each entry's `browserai.json` and parsed it strictly — up to 250 log
  entries and all their arguments — and `IsUnder` was applied to what came back.
  Each of those opens goes through `RenameWindow.WaitOut`, whose budget is
  **30 seconds** and which retries a denial, so one session *anywhere on the
  machine* whose record is denied by an ACL or held by a scanner could add that
  to a call scoped to a completely unrelated tree. **The sharper half is the one
  the review does not name**: the roll-up runs the same walk on every
  `browserai_init` and every `browserai_resume`, so this was on the session-open
  path rather than only on a listing. `SessionIndex.FollowUnder` applies the
  prefix **above** the open, at the one point where everything already done is
  the entry's own verification and nothing has been opened inside the session.
  **The predicate is unchanged and only its position is**, so the reported set is
  bit-identical; `IsUnder` moved with it rather than being spelled twice.
  `Follow()` keeps its exact semantics and stays whole-machine for the index's
  own sweep, the reinstall census and the stray sweep.
  *Corrected: `SessionIndex`'s contract sentence "Every entry is verified by
  opening the `browserai.json` it points at", which becomes "no entry is ever
  **reported as a session** without opening it" — a subtree read decides which
  entries to ask about and never which answers to trust; and "the only way this
  store is ever read", which is now one of two.*
  **The header-only-read fork was declined, and the reason is the symmetry:** it
  *could* have been planted red, precisely because it changes what is reported —
  a record whose log is malformed would read as a session to the listing and be
  refused by `TryAcquire`, which is two readers of one file that disagree. The
  fork that is safe is the fork that is invisible, so the red-today test is the
  rule itself: `HouseRuleTests.NoIndexWalkFiltersBySubtreeAfterFollowingTheEntry`,
  planted red on both real offenders, with both synthetic controls and a
  non-vacuity floor over the two whole-machine walks that must stay whole-machine.
  ⚠️ `SessionIndexTests.FollowingOneSubtreeReturnsExactlyWhatFollowingEverythingWouldHaveReturnedForIt`
  and `.FollowingOneSubtreeOpensNoRecordOutsideIt` ship with the change, name an
  API that did not exist before it, and say in their own remarks that they are
  **weaker than a red test** and why.

- **The machine-wide log is one shared file under a cross-process write gate, and
  a session's records no longer go into it.** Two changes that are one decision.
  **First, scope:** anything attributable to a session is written to that
  session's own `browserai.log` and to nothing else — `ProcessLog.OpenSessionLog`
  no longer adds a second provider over the machine-wide writer, and the proxy's
  per-call refusals are logged through the session's own logger where one is in
  hand. What stays central is what no session owns: the stray sweep, which is
  machine-wide by design because it hunts browsers belonging to *any* session,
  plus startup, updates, provisioning, the server transport, the MCP server, and
  the two proxy refusals with no session directory to be written into — a call
  naming no session, and a call naming one that does not exist. ⚠️ **That reduces
  the shared file's write rate; it does not dissolve the contention**, and the
  lock rather than the scoping is what makes the remainder safe.
  **Second, the lock**, chosen over one file per process at the maintainer's
  decision, verbatim: *"Simple to read, simple to write."* `NativeFile.TakeGate`
  takes an exclusive byte-range claim through `LockFileEx`, one byte past any
  possible end of file — a lock over the data would be enforced against
  `ReadFile` too and would refuse every concurrent reader of the log. A file lock
  rather than a named object for the reason `MaintenanceLock` already gives: the
  kernel releases it however the holder dies, where a semaphore's count is not
  restored. **The length is read, the instant is stamped and the bytes are
  written inside one claim**, so write order and timestamp order coincide and the
  file is sorted *by construction* rather than by anybody sorting it.
  **Every record now carries both of its times** — the leading column is when it
  reached the file, `made=` is when it was created, and the two diverge only under
  contention, which is the one thing a single timestamp cannot show. **And it
  names its writer as `pid=<n>@<createdFileTime>`**, never a bare pid: the log
  keeps thirty days and Windows reuses pids, so a bare one eventually names a
  stranger. The FILETIME is spelled the way `browserai.json` spells
  `processCreatedFileTime`, so a log line and a lock record name the same writer
  with the same characters, and it is the pair `ProcessLiveness.IsAlive` takes.

- **Rotation happens exactly at the cap.** *Corrected in
  `RollingFileWriter` (previously "The starting size is read once. It drifts under
  concurrency, which only means the roll happens at approximately the cap rather
  than exactly at it — and paying a metadata query per record to fix that would be
  a worse trade").* The length is now the file's own, read through the open handle
  inside the write gate, so nothing can have appended between the read and the
  decision and there is no per-process counter left to drift. It is not a metadata
  query — the handle is already open — and no file in the directory ever exceeds
  8 MiB, with one stated exception: a record larger than the cap on its own is
  written rather than dropped, and lands alone in its own file.

- **`BrowserAiPaths` no longer claims to answer "the directory the product would
  actually have used".** It resolves through the product's own
  `LocalAppDataPaths` — which is the part worth keeping — but constructs it with
  **no** root argument, so it always answers the per-user default under
  `%LOCALAPPDATA%`. `Program.Main` honours `BROWSERAI_ROOT` and takes it over
  that default, and `PublishedSlice.InheritedEnvironment` copies the whole
  environment into the published child, so the two disagree in exactly the case
  the suite creates on purpose: a test that points a real BrowserAI at an empty
  browsers root. **Making it honour the override was considered and deliberately
  not taken** — the members are read by assertions about the developer's real
  provisioned tree, and following the variable would re-point them at the rig
  the variable was set to create. The comment changed; the resolution did not.

- **The release gate's two shells are now two instruments by construction, and a
  run says which drive-letter spelling it actually received.** The gate runs the
  suite from PowerShell and from Git Bash because the two hand the test host
  different spellings of the drive letter — `C:\…` against `c:\…` — and an
  assertion comparing a composed path against one Windows re-spelled is green
  from one and red from the other. **That property held run to run rather than
  by construction**: on the 2026-08-24 gate all six runs received `C:`, three of
  them silently duplicating the other three, and every signal the gate publishes
  read exactly as it reads when both spellings really were exercised.

  Measured the same day: the lower-case spelling is not a property of Git Bash
  but of whatever started it. A bash that **inherits** its working directory
  hands a child `c:\…`; the same shell after **any** `cd` — `/c/…`, `c:/…`,
  `C:/…`, `c:\…` alike — hands it `C:\…`, because MSYS resolves the real path
  and the mount manager answers upper. So `cd` cannot be the lever. Both
  invocations in [Testing](TESTING.md#how-the-suite-is-run-detached-teed-and-the-log-polled)
  now hand `dotnet test` an **absolute, explicitly-spelled** path to the
  solution, which carries the spelling through `MSBuildProjectDirectory` and
  `TargetPath` into the test host's own `AppContext.BaseDirectory` whatever the
  working directory says — MSYS re-spells a command path and a `cd`, and leaves a
  path passed as an argument alone.

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

### Fixed

- **`browserai_list` said `in use: no` about a session another agent was driving
  right now.**
  [Adversarial review F1](docs/reviews/2026-08-24-adversarial-since-the-mode-drop.md).
  Since 2026-08-20 every forwarded browser call runs `SessionLock.Append`, which
  is `Rewrite`: the ownership handle is dropped at the top of the replacement and
  taken back at the bottom, with the per-directory gate held throughout. So a
  *busy* session's `browserai.json` is periodically **present and unheld**, and
  the bare probe reads that as a directory nobody has. The listing landed in that
  window and printed `no` — the one direction that costs a caller a session it
  was about to `browserai_destroy`. `SessionLock.ProbeLivenessUnderTheGate` asks
  the same question with that directory's own gate held at a **zero** timeout:
  the gate is the discriminator and cannot be wrong, and a gate it could not take
  is `UNKNOWN` with a reason rather than `no`. The zero timeout is what keeps the
  listing out of the queue `ProbeForHolder` was extracted to remove.
  **`browserai_catch_up` is gated too** — one opener, two callers, and two tools
  printing different `in use:` lines about one session in the same second is the
  failure that extraction's own remarks exist to prevent.
  *Corrected with it: `SessionLock`'s comment that "the gate is what makes the gap
  unobservable" (true of readers that take the gate, and one did not),
  `SessionManager.InUse`'s "through the pre-gate probe", `browserai_catch_up`'s
  "takes no lock" in four places including its model-facing description, and
  `README.md`'s "`no` when nothing held it at the instant of the look".*
  ⚠️ **The per-entry cost figures in `ARCHITECTURE.md`, `SessionManager` and
  [kb](kb/windows/detection.md) are now incomplete and have NOT been adjusted** —
  a mutex create, acquire, release and close are in the per-entry cost and the
  create/close pair is **unmeasured**.
  `SessionListTests.ASessionWhoseGateIsHeldByAPeerIsReportedUnknownRatherThanFree`,
  planted red. The zero-timeout property could not be planted red at all — a
  120-second acquire would still return inside the rig's patience — so
  `SessionLockTests.TheListingProbeTakesTheGateWithoutWaitingForIt` is a
  source-level guard and says in its own remarks that it is the weaker thing.

- **Page content could switch the artifact-pointer protection off.**
  [Adversarial review F2](docs/reviews/2026-08-24-adversarial-since-the-mode-drop.md).
  The provisioning answer-rewrite returned before `live.Artifacts.Complete`, so a
  call whose answer tripped it **pinned no name, recorded no artifact and carried
  no note** — and upstream builds the `Error`, `Page`, `Snapshot` and `Events`
  sections into **one** result, so an ordinary failed call against a live tab
  carries the page's own `<title>` beside the console and snapshot pointers. A
  page whose title quoted upstream's install advice therefore disabled the
  protection and the next sweep moved the file the answer had just linked to.
  That branch now runs `Complete` like every other answered call, with the
  child's own answer going in and the note appended to the node.
  **The scan is also gated on `isError`**, which upstream sets from the presence
  of an `Error` section and from nothing else —
  `sections.some(s => s.isError) ? { isError: true } : {}` beside
  `isError: title === "Error"`, read out of the resolved bundle and recorded in
  [kb](kb/playwright/configuration.md) — so the gate is lossless on the path the
  rewrite exists for and takes every ordinary answer out of reach of page text.
  *Corrected: `BrowserProxy.Remediate`'s claim that "on the paths where it appears
  at all the answer is already a failure with no bytes worth preserving", and
  `ProvisioningRemediation`'s "the rewrite fires only when the marker is present —
  every other answer, including every other error, goes through untouched".*
  ⚠️ **The gate does not close the bypass on its own and is not claimed to**:
  `ArtifactPointerTests.APointerSurvivesAnAnswerThatAlsoTrippedTheProvisioningRewrite`
  sets `isError` deliberately, so half 1 cannot make it pass.
  `ProvisioningRemediationTests.APageQuotingUpstreamsAdviceInASuccessfulAnswerIsForwardedUntouched`
  is the other, and both were planted red.

- **Every failure to open `reinstall.lock` was reported as a reinstall in
  progress.**
  [Adversarial review F5](docs/reviews/2026-08-24-adversarial-since-the-mode-drop.md).
  `MaintenanceLock.TakeShared` caught `IOException` and `UnauthorizedAccessException`
  together and returned one bit, so an ACL denial, a full volume and a path that
  is too long all told the caller *"BrowserAI is replacing the browsers under
  '…' on this machine right now"* — complete with a progress clause counting from
  zero — and to wait minutes for a download that was not running. The kernel had
  already answered: a sharing violation on this open is a holder and **nothing
  else**, because the reader asks `Read`/`FileShare.Read` and a second reader is
  compatible with it. Both takes now carry a `MaintenanceDenial` and Windows' own
  message out, and `SessionErrors.TheBrowsersRootCouldNotBeClaimed` is a **new
  catalogue row** rather than a clause on the existing one: **two recoveries are
  two rows**, and waiting clears one and will never clear the other. It names the
  causes and refuses to pick one, because they are not distinguishable from a
  caught `IOException`. **`browserai_reinstall_browser`'s half is included**: a
  census of zero over a file nothing could open was concluding *another reinstall
  has it*, so the unreachable arm is answered before `LiveSessions()` is
  consulted. The contended sentence is deliberately left unhedged.
  *Corrected: `MaintenanceLock`'s catch comment that "`Describe` says which for
  the sentence" (it returns the last writer's line and nothing truncates the file
  when a reinstall ends, so it cannot), and `TheRootIsBusy`'s "if it finds none,
  another reinstall has it".* The catalogue census moves **27 → 28**.
  `ErrorCatalogueTests.AnInitThatCannotOpenTheBrowsersClaimIsNotToldAReinstallIsRunning`,
  planted red against a real ACL denial.

- **A pinned artifact name was matched undelimited and never given back.**
  [Adversarial review F7](docs/reviews/2026-08-24-adversarial-since-the-mode-drop.md).
  `NoteWhatTheAnswerPublished` asked whether the answer *contained* each loose
  file's name, so `report.pdf` was pinned by an answer that only ever said
  `quarterly-report.pdf`, and a one-character name was pinned by any answer at
  all; and `_published` was monotone across **files** as well as calls, so a name
  pinned once pinned every later file that shared it, for the session's life,
  with **no answer naming it**. The match is delimited now, and a name is dropped
  once nothing loose in the output root carries it. **The review's own
  recommended fix — requiring the name to look generator-produced — was declined
  with the reason:** upstream publishes a pointer to a browser-initiated download
  too, `- Downloaded file <name> to "./<name>"` with the *site's* unprefixed name,
  so a prefix rule would move every real download out from under upstream's own
  pointer. That is verified in the resolved bundle and recorded in
  [kb](kb/playwright/tools-and-artifacts.md).
  *Corrected: the "substring rather than a parse" defence, which covered
  generated names and said nothing about the one artifact class this same file
  records upstream as not naming; and "Monotone on purpose … never removed".*
  ⚠️ **The residue is stated rather than closed** — a short, word-shaped name a
  page renders in prose is still delimited and still pins, and the harm is
  bounded to classification inside the session tree, with the absolute path in
  the note either way.
  `ArtifactPointerTests.AFileWhoseNameOnlyOccursInsideALongerOneIsStillSorted`
  and `.APinnedNameIsNotInheritedByALaterFileThatHappensToShareIt`, both planted
  red, with the control arm that upstream's own download pointer still resolves.

- **A cancelled `tools/call` leaked its filename reservation for the life of the
  session.**
  [Adversarial review F8](docs/reviews/2026-08-24-adversarial-since-the-mode-drop.md).
  `_reserved` loses an entry only through `Release`, and every release site was
  on a path that **returns** — so a caller that cancelled a screenshot left
  `login.png` reserved forever, and the retry came back as `login-2.png` **with
  the answer reporting a rename that no file on disk justified**, which is the
  exact class this product exists to remove. `AnswerToolsCallAsync` is wrapped
  from the plan onward and the three release sites are folded into one `finally`.
  Releasing after a *successful* write is deliberate and safe: `Taken` is
  `_reserved.Contains(candidate) || File.Exists(candidate)`, so a file that is on
  disk holds its own name — and a file the caller later deletes stops holding a
  name it no longer occupies, which the reservation set alone could never give.
  A new `ProxyLog.ReservationReleased` at Debug is the only evidence the
  cancellation path leaves, because the SDK sends no frame at all for a request
  cancelled by `notifications/cancelled`.
  *Corrected: `ArtifactRouter.Release`'s summary, "for a call that never reached
  the child", and `ARCHITECTURE.md`'s "Never overwrite" paragraph, which was
  missing the clause that a reservation is given back **however** the call ends.*
  ⚠️ **The test asserts cancellation only and says so** — the idle-timer scope
  and the remediation regex's 1,000 ms match timeout are covered by the same
  `finally`, neither is deterministically reachable, and a test that provoked one
  by timing is the promptness assertion this suite forbids.
  `ArtifactRoutingTests.ACancelledCallGivesItsReservedNameBackSoTheRetryIsNotSuffixed`,
  planted red on the suffix.

- **A torn log record is no longer possible, and the machinery that made it
  possible is deleted rather than repaired.**
  [Adversarial review finding 9](docs/reviews/2026-08-18-adversarial-processes.md).
  `NativeFile.Append` looped on a short write, and `FILE_APPEND_DATA` atomicity is
  **per `WriteFile` call** — so the second call landed after whatever another of
  the ~100 processes had written in between, the record was torn and interleaved,
  and every call returned success. Four directions were recorded for this and
  **none of them was taken**: all four repaired a loop whose premise was a
  lock-free design. Under a real lock there is no per-call size bound and nothing
  can interleave, so resuming a short write at the right offset is correct by
  construction and `RandomAccess.Write` does it. `OpenForAtomicAppend` and
  `Append` are gone; **no truncation and no new record-length limit were needed**,
  which is what every recorded direction cost.

- **Nothing can unlink the live machine-wide log out from under its writers.**
  [Adversarial review finding 10](docs/reviews/2026-08-18-adversarial-processes.md).
  `FILE_SHARE_DELETE` let anything on the machine delete or rename it while a
  hundred BrowserAIs held it open, after which every write **succeeded** into an
  unlinked file object, `RollingFileWriter.CurrentFile` went on naming a path that
  no longer existed, and the writer's own catch never fired because nothing had
  failed. Delete sharing is now an argument each caller states: withheld for the
  machine-wide log, granted for a session's own `browserai.log`, which
  `browserai_destroy` must be able to remove under a live session.
  ⚠️ **The cost is stated where it is decided and was accepted knowingly: the
  MACHINE's central log — not one process's own file — cannot be deleted or
  renamed while any BrowserAI runs**, `SweepExpired` included, and that pass now
  tolerates the refusal instead of reporting it.

### Changed

- **`browser_get_config` DOES redact `secrets`, and the claim that it does not
  is corrected in three places.** *Previously, in `DECISIONS.md`, `kb/playwright/tools-and-artifacts.md`
  and re-verification row 71: "its handler is `JSON.stringify(context.config, null, 2)`
  with no filtering, so it emits `config.secrets` in plaintext if that key is ever
  set", `Verified 2026-08-13 @ 0.0.79`.* The handler reading was correct and the
  conclusion drawn from it was wrong, because the redaction is not in the handler:
  every response leaves through
  `sanitizeUnicode(this._context.redactSecrets(serializedText))`, one layer above
  it. **Measured 2026-08-20** against the bundled child started with
  `secrets: {"MY_TOKEN": "sk-live-…", "OTHER": "hunter2"}` — the answer carries
  `"MY_TOKEN": "<secret>MY_TOKEN</secret>"` and neither literal value appears
  anywhere in the frame. Upstream's own `config.d.ts`, in the copy sitting in
  `payload/`, has said so all along.

  **This is not an endorsement of setting `secrets`, and the correction must not
  be read as one.** `redactSecrets` is `text.replaceAll(secretValue, …)` — a
  substring match on the **value**, over the whole response. Measured in the same
  run with a third secret whose value was `chromium`: `"browserName": "chromium"`
  came back as `"browserName": "<secret>COMMON</secret>"` and `chromiumSandbox`
  as `<secret>COMMON</secret>Sandbox`. A short or common value corrupts unrelated
  text, an empty value is skipped outright, and a value the page never renders
  verbatim is not redacted at all — *"a convenience and not a security feature"*,
  in upstream's words. BrowserAI still never writes the key and never passes
  `--secrets`, so on every ordinary call there is nothing to redact.

  **The same claim in `docs/reviews/2026-08-19-auth-transfer-and-session-modes.md`
  was deliberately left standing.** It is a dated record of what this repository
  believed on the day it was written, and correcting it there would destroy the
  account rather than improve it — which is the rule the append-only seal above
  now enforces.

- **BrowserAI refuses to serve out of an app root that is not inside the current
  user's Windows profile.** The maintainer's decision of 2026-08-20, answering
  [`QUESTIONS.md`](QUESTIONS.md) §12 with *"L1 a"* — direction (a), refuse at
  startup. `%LocalAppData%` gives every user their own browsers directory, session
  index, log and `live\` markers; `BROWSERAI_ROOT` and the installer's install-to
  flag both defeat that. Measured on 2026-08-20, sharing a root is unsafe in a way
  nothing reports at run time: the **file** locks span users, because a share mode
  is enforced against handles rather than tokens, but the **`Global\` mutexes do
  not** — the DACL the kernel puts on one names LOCAL SYSTEM, the creating logon
  session and the creating user, with **no group ACE at all**. The user who loses
  the race cannot join the live set, creates no marker, and is invisible to the
  other's census; that census answers *alone*, and an apply then runs
  `force_stop_package`, which terminates every process under the install root.

  **The check runs before anything creates state** — before the stray sweep, the
  live marker, the instance directory and every session — so a refused root gains
  a log line and nothing else, and the test asserts exactly that: `logs\` is the
  only thing under the root afterwards. The refusal is a log record and a non-zero
  exit, because `stdout` is the protocol channel and `System.Console` is banned
  outright, and it carries the remedy rather than only the verdict.

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

- **The claim on the browsers root is a reader/writer lock: every session holds it
  shared, and a reinstall holds it exclusively.** The maintainer's design of
  2026-08-20, verbatim: *"any init or resume should take a system level lock. No
  matter the browser type. These locks are cumulative. And reinstalling the browser
  should be an exclusive lock."* `browserai_init` and `browserai_resume` open
  `<browsers root>\reinstall.lock` `FileAccess.Read` / `FileShare.Read` and hold it
  for the session's whole life; `browserai_reinstall_browser` opens it
  `FileAccess.ReadWrite` / `FileShare.Read`. **Windows' sharing rules give the
  semantics directly** — an open is refused when its access is outside an existing
  handle's share mode *or* when its share mode is narrower than an existing
  handle's granted access, so any number of readers coexist with no count kept
  anywhere, and one reader is enough to refuse the writer.

  **The kernel is the gate now; the session census is only a sentence.** What used
  to decide the refusal was `SessionManager.LiveSessions`, which walked this
  process's sessions and the index; it survives to *name* what the caller must
  close, and the exclusive open decides. That is strictly stronger — a session
  whose index entry was swept, or whose process this one cannot see, still holds a
  handle.

  **The family filter is gone, and its removal is the maintainer's "no matter the
  browser type".** A live Firefox session now refuses a Chromium reinstall. The old
  reasoning — only a session of that family can hold an executable out of that
  family's tree — was not wrong and is no longer the question: the claim is one
  file at the root of the browsers directory and knows nothing about families, and
  listing only the matching family would name none of the sessions the caller has
  to close.

  **No intent marker, no drain, and writer starvation accepted**, all three his:
  *"I do not want the intent marker. If anything is busy then the reinstall should
  be refused with the list… But it should not start a drain/preventstart process of
  sorts. Keep it simple. Let the user solve the open sessions block."* A machine
  that always has one session open never lets a reinstall through, and that is a
  decision rather than a defect to be mitigated.

  **A reader that dies releases the claim with nothing running to clean up**, which
  is why this is a file and not a named semaphore — a semaphore's count is not
  restored on its holder's death, so one crashed session would refuse every
  reinstall until a reboot. `ReinstallBrowserTests.AReaderThatDiesReleasesTheRootWithNothingRunningToCleanUp`
  kills a real process holding the shared claim by closing a job object — a
  `TerminateProcess` with no unwinding at all — and proves the root goes free.

- **Every refusal a caller meets during a long operation now says how far in it
  is.** The maintainer's instruction of 2026-08-20: *"Also make sure that the
  reinstall reports the progress just like the first run provisioning. Close both
  gaps."* First-run provisioning already reported bytes, elapsed and the observed
  rate; the other two refusals reported nothing.

  **The reinstall refusal** — what `init`, `resume` and a second reinstall meet
  while one is running — now carries what the download staging directory weighs,
  how long the claim has been held, and the rate those two give. Both figures are
  read off the filesystem, because a peer cannot see the reinstalling process's
  own provisioner at all: the staging directory is
  `<browsers root>\.downloads`, which the installer's `TEMP` points at, and the
  elapsed time is the claim file's last write. **Zero staged bytes is reported as a
  phase and never as a stall** — it is the delete, which comes first, or an
  extraction already under way, and the sentence says which two things it cannot
  tell apart.

  **The update refusal** — an update downloaded, staged and not applied because
  something else is live — now names *at least N other BrowserAI process(es)*
  rather than "another BrowserAI is running", distinguishes that from a census that
  could not be taken at all (a permanent block rather than a queue, and it says
  why), and states that nothing more has to be downloaded, with the package size
  and the seconds it took.

- **The provisioning stall detector runs on an injected clock and an injected byte
  source, and its own flaky test is fixed by that rather than by weakening it.**
  `ProvisioningTests.ASlowInstallThatKeepsWritingIsNotStoppedHoweverLongItTakes`
  went red once in nine consecutive full-suite runs on 2026-08-20 — the day CI was
  removed and the local suite became the only gate — with the product behaving
  perfectly: sixty real writes 25 ms apart against a real one-second cap is a
  ratio, and a ratio between two real clocks is still a race at unbounded suite
  parallelism.

  **Both of the detector's inputs are seams, because one alone would not have been
  enough.** `ProvisioningTimers.Clock` is a `TimeProvider`, and the **poll wait**
  goes through it too — a loop whose arithmetic reads an injected clock and whose
  sleep reads the wall clock cannot be driven, and would look deterministic while
  racing exactly as before. `BrowserProvisioner.WeighBrowsersRoot` is the byte
  source, because the detector judges an install on bytes on disk as well as on
  time. The two together let the test drive the loop in **lockstep**: the product
  asks what the root weighs, and answering is where the test moves the clock.

  **The assertion is stronger rather than weaker**, which was the constraint. It
  survives **1,000 polls each one tick short of the whole budget** — nearly seven
  simulated days against a ten-minute cap, in milliseconds of wall clock — and
  `.TheStallCapFiresOnTheFirstPollAfterTheBudgetPassesWithNoBytes` pins the other
  side to the exact poll the detector fires on, which no wall-clock test of this
  could have asserted. Both were watched red against a planted total-time cap and a
  planted one-budget-late cap. **There is no real duration anywhere in either arm**,
  so the flake is impossible rather than unlikely.

- **A session's record is `browserai.json`, renamed from `lock.json`, and there is
  no compatibility read.** The maintainer's decision of 2026-08-20, verbatim:
  *"nothing is in production yet. The only version that exists is the alpha version
  that we are building and testing in this session, so rename all the locks in code
  to browserai.json and don't take into account any legacy setup anywhere."* The
  file had stopped being only a lock long before the name did: every field of it is
  an ordered list of timestamped statements about how the session got here — mode,
  browser, purpose, holder, client — and `browserai_list` and `browserai_resume`
  read it for those rather than for ownership, so the old name described the
  smallest thing it does. The **handle** is still the lock, and every type that
  says so keeps its name: `SessionLock`, `LockRecord`, `LockScopes` and
  `SessionPath.LockFile` are about the lock, which is unchanged.

  **No fallback, no migration and no compatibility read**, which is the half worth
  stating: a directory holding the old name is not a session to this build, and
  `browserai_init` will make a new record beside it rather than adopting it. That
  is safe only because the old name never shipped — `v0.1.0` and `v0.1.3` are the
  only tags, and neither is installed anywhere but this machine.

  **The historical record keeps the old name.** `CHANGELOG.md`'s released sections
  and everything under `docs/reviews/` are dated accounts of what was true when
  they were written; a 2026-08-18 review saying `browserai.json` would be claiming
  a filename that did not exist for another two days, which is the same defect as
  a measurement updated by reasoning instead of re-measurement. `docs/reviews/`
  states that rule about itself and it is kept here.

- **`allowUnrestrictedFileAccess` is set in every generated child config, always,
  with no argument that can turn it off.** The maintainer's answer of 2026-08-20,
  asked whether it should be always on, per mode or per call: *"a always"*.
  Upstream's default is `false`, and leaving it there was a **live regression
  against all four pre-BrowserAI ways of running this child**: `checkUrlAllowed`
  refuses the `file:` protocol outright, so `browser_navigate` cannot open a local
  page at all, and `checkFile` refuses any path outside `<session>\output` and the
  child's working directory, so `browser_file_upload` cannot reach a file the
  caller already has.

  **Upstream calls it a convenience defence rather than a secure boundary**, in
  `config.d.ts`'s own words — *"a guardrail to prevent the LLM from accidentally
  wandering outside its intended workspace … not a secure boundary; a deliberate
  attempt to reach other directories can be easily worked around, so always rely on
  client-level permissions for true security"* — and BrowserAI's caller already
  holds file tools of its own. That is the same reasoning that removed the
  `(tool, mode)` permission matrix on 2026-08-18: what the guardrail withholds is
  reachable one tool call away, so all it can do here is refuse the caller a thing
  it is entitled to while proving nothing.
  `ConfigRoundTripTests.EveryGeneratedConfigLiftsUpstreamsWorkspaceGuardrail`
  checks the value for both families, every mode and the run's own browser-less
  child; the key is in `RequiredSessionOpinions`, so the round trip through
  `browser_get_config` fails if the child ever stops honouring it.

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

- **A `browserai_destroy` racing a `browserai_set_purpose` on one session leaked
  that session's directory for the life of the process.** Nothing above
  `SessionManager` serialises tool calls — `_live` is a `ConcurrentDictionary`
  and is the only synchronisation there is — so two calls naming one session
  reach one `SessionLock` concurrently, which is the design rather than an
  accident. `Rewrite` tested `_disposed` and *then* took the per-directory gate,
  and `Dispose` disposed that gate underneath it: the rewrite re-opened
  `browserai.json` into a disposed lock, `_gate.Release()` threw, and for the rest
  of the process's life **every** `SessionLock.TryAcquire` on that directory
  answered `Held`, naming a pid with no session — while the destroy reported a
  partial failure blaming *"something still has them open"*. It is the one
  finding of [the 2026-08-18 adversarial locking
  review](docs/reviews/2026-08-18-adversarial-locking.md) whose failure does not
  heal: nothing releases that handle short of ending the process.

  **The fix is a per-session lock that every mutating path and both disposal
  paths hold for their whole body** — `Rewrite`, `Append`, `ReleaseAndDelete` and
  `Dispose`, including the caller delegates they invoke. The smaller change,
  taking the gate before the `_disposed` check, was **declined**: the disposal
  disposes the gate itself, so a rewrite blocked on it wakes holding a disposed
  object, and a check that races is still a race. It is not a fourth lock scope —
  `LockScopes` still names three machine-wide objects in one place, and this one
  has no name, no kernel object and no reach outside its instance.

  **The interleaving is forced rather than raced for, and the seams were already
  there.** `Rewrite` calls the caller's `update` delegate past the disposal check
  and under the gate; `ReleaseAndDelete` calls `delete` after the handle is
  closed and before the gate is released. Both tests place the second thread from
  inside those delegates, so no sleep, retry or stress loop is involved and the
  join can only go one way: against the defect the disposal contends with nothing
  and returns in microseconds, against the fix it cannot return at all. The
  first was watched red on all three of its assertions — the join, the
  `ObjectDisposedException` the rewrite threw, and **the leak itself**, a
  stranger's exclusive open of `browserai.json` refused after the session that
  owned it had been disposed.

- **One junction above the install root made the stray sweep structurally blind,
  and nothing distinguished that from a clean machine.** Every path BrowserAI
  composes goes through `Path.Combine`, which never resolves a link;
  `QueryFullProcessImageNameW` answers with the path the object manager resolved,
  reparse processing already done. The comparison between them is *exact*, so on
  any machine with a relocated user profile, a redirected `AppData`, a `subst`ed
  drive letter or an 8.3 component above the root, **every process missed, on
  every pass, for good** — `candidates=0` forever, reported as a clean machine.
  The same mismatch emptied the live set `RevisionPrune` deletes a superseded
  browser tree on, which turned a race into a certainty: every superseded tree
  looked idle while browsers ran out of it.

  Measured rather than reasoned about, on 2026-08-24 and for the first time: a
  process launched through a real `mklink /J` junction is reported under the
  **target** spelling, having never named it
  ([kb](kb/windows/detection.md#a-process-reports-the-junctions-target-not-the-spelling-it-was-launched-by--measured-2026-08-24)).

  **What the sweep may match widened; what it may terminate did not**, and the
  two were kept apart deliberately because this code decides what may be killed.
  Only paths BrowserAI itself composed are ever resolved — never one a foreign
  process reported, so the process list cannot steer what gets opened. A resolved
  spelling names *the same file* the composed path already named, so the match
  stays exact, stays full-path, and is still never a prefix and never an image
  name. And every guard between a candidate and a kill is untouched: the second
  independent guard, the held process handle, the creation-time re-check and the
  `browserai.json` lock the sweeper has to be able to take itself.

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

- **The instance directory's liveness rested on one child, and the blast radius
  was every session in the run.** A run's instance directory holds the generated
  Playwright config of *every* live session, and exactly one process ever held it
  open: the surface child, which is given it as a working directory. Session
  children are given the session's own output root instead. So a surface child
  that died while the run kept serving left the directory unheld — and a
  directory's `GetLastWriteTimeUtc` does not move when files inside it are
  written, so five minutes later another BrowserAI's startup sweep renamed it
  aside and deleted it. Live sessions kept working, because their configs had
  already been read; every new one failed, and the run's own tidy-up then
  reported nothing at all, because a missing directory is deliberately never a
  failure.

  **BrowserAI now holds a marker inside its own instance directory** —
  `instance.live`, opened `ReadWrite`/`FileShare.Read` and held for the whole
  life of the process, the same mechanism the live-instance set and the browsers
  root's maintenance claim already use. It is taken by BrowserAI rather than by
  any child, so the signal no longer depends on one child staying alive, and the
  kernel releases it however the process dies. A sharing violation is a fact
  Windows enforces; a timestamp and one process's working directory were an
  inference. The sweep now says *this belongs to a BrowserAI that is still
  running* instead of *something refused my rename*, and the five-minute age
  guard, which used to cover the whole interval until a child started, now covers
  the two statements between creating the directory and marking it.

  **Found independently by both 2026-08-18 adversarial reviews** and carried as
  two hazard rows for five days before they were recognised as one.

- **The pointers BrowserAI handed the model did not resolve — two of them, and
  they were the same defect twice.** Upstream writes two artifacts BrowserAI's
  inbound routing cannot reach, because neither comes from a `filename`
  argument: the **console log** and the **snapshot `.yml`**. It publishes a
  pointer to each *inside the answer* — a Markdown link to `./page-<stamp>.yml`,
  and `- New console entries: console-<stamp>.log#L1-L24` — and both are
  relative to the child's working directory, which is the output root.
  **BrowserAI's after-the-fact sweep moved both into typed folders**, so every
  one of those pointers named a file that was no longer there.

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
  of the answers that follow, so a set scoped to one call leaves the file movable
  on the very next call — which reintroduces the whole defect. Both tests were
  planted red first and reported the exact symptom above:
  `ArtifactPointerTests.EveryPointerARealChildPublishesResolves` drives a real
  browser and checks every `#Lx-Ly` against the lines of the file it names, and
  `.ANamedFileSurvivesEveryLaterSweepAndAnUnnamedOneIsStillSorted` holds the
  monotone half and carries the control — a download nothing named is still
  sorted, so this is a rule about pointers rather than the sweep being switched
  off.

- **755 stale `.live` markers had accumulated, because the only code that
  reclaimed them ran somewhere nothing ever reaches.** Reclaim lived inside the
  updater's *am I alone?* census, which `UpdateService` calls only after an update
  has been found **and** downloaded — which had never once happened on the machine
  this product is developed on. Two days of ordinary work left 755 unheld files in
  `%LocalAppData%\BrowserAI\live\`, and every census that ever did run would have
  had to open all of them.

  **Reclaim is now a routine of its own and runs from two places, both with the
  same mutex discipline.** `LiveInstances.ReclaimStaleMarkers` takes the same
  per-root gate a join and a census take, at **zero timeout**: one process
  reclaims and every other pays an acquire and leaves. It runs from the stray
  sweep — already machine-wide, already mutex-serialised, already skipping
  instantly when a peer holds its own gate — and from **startup**, on a background
  thread, so that a sweep declining to run for reasons that have nothing to do
  with markers cannot cost a machine its reclaim. **Nothing waits for it**, and it
  is deliberately not folded into `LiveInstances.Join`'s hold: walking 755 markers
  inside a five-second-gated critical section that a hundred starting processes
  queue on is how a join times out, and a process that could not join is invisible
  to a peer's census.

  ⚠️ **A marker is stale only when it is NOT HELD; existence is not held-ness** —
  the same rule `MaintenanceLock` and `SessionLock` state about their own files.
  Reclaiming a live instance's marker would make that instance invisible to every
  later census and therefore killable by an apply, so the negative is proved with
  a positive control rather than argued: `UpdateTests.AHeldMarkerSurvivesTheReclaimAndTheSameMarkerGoesOnceItIsReleased`
  holds one marker open, runs the reclaim, requires it to survive, releases it,
  runs the reclaim again and requires it to go — so a pass that removed nothing at
  all could not pass either half. `StraySweepTests.TheSweepReclaimsStaleLiveMarkersAndLeavesAHeldOneAlone`
  does the same through the sweep.

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
