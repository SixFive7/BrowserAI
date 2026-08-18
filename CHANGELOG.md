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

### Fixed

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
  request. [`.github/workflows/build.yml`](.github/workflows/build.yml) builds
  the payload, provisions both browsers, publishes the NativeAOT binary and runs
  the **whole** suite including `SaturationTests`, on every push and pull request.
  It states its own cost in its header: about 204 MB of first-run browser
  download per run, uncached on purpose, because a cache key would have to be
  guessed ahead of the resolve it depends on.
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
