<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# TODO

Central work list for BrowserAI. One file, so nothing survives only as a comment
somewhere.

**What belongs here:** work settled in intent but not yet done.

**What does not:** open design questions and known hazards. Those live in the
README, under [Open design decisions](README.md#open-design-decisions) and the
[hazard index](plan/hazards.md#hazard-index). An item moves here once the decision
behind it is made.

**Format:** `- [ ] **Title.** Why it matters, then what to actually do.` Carry the
README's provenance convention — a claim about an external source needs the date
and version it was true at.

## What the plan's final audit found unbuilt — 2026-08-16

Every item below is a requirement a [plan](PLAN.md) section makes that nothing in
the tree satisfies, found by [the final audit](PLAN.md#the-final-audit-ran-on-2026-08-16-and-the-plan-is-not-deleted).
They are here rather than in the plan because that is this file's job, and
because **the plan sections they came from are not deleted**: a section with an
unbuilt remainder stays on disk, so each item below is still readable at its
source.

**Two failure shapes, and the second is new.** The first is the one that already
cost this project two whole steps ([17a](plan/build-order.md#17a-the-browser-idle-timer-and-teardown),
[17b](plan/build-order.md#17b-the-stderr-classifier)): *a section mandates it and
no build-order step owns it.* The second appeared only under this audit — *a
rule a section states, correct in the tree today, guarded by nothing*, so it can
be undone by an edit that leaves every signal green. `UseSystemResourceKeys` was
one of those and was closed; the ones below sit in the same paragraph lists as it
did.

- [x] ~~**BrowserAI registers itself as an MCP server. Nothing does this, and it is
      the charter's founding promise.**~~ ✅ **Built 2026-08-16**, and the
      decision it was waiting on was taken **in the maintainer's absence** —
      they were asked twice, they are away, and the product is unusable
      meanwhile. **Scope: user. Mechanism: the client's own
      `claude mcp add --scope user`.** User scope *is* §B's sentence — one
      registration, available in every repository, no file in any of them — and
      the three alternatives lost for reasons written on the code: writing the
      client's config file directly means owning another product's format and
      merge semantics; a registry key is read by only some clients; a documented
      manual step abandons the promise that distinguishes this from the setup it
      replaces. `src/BrowserAI/Registration/{McpClientRegistration,
      RegistrationTarget, IRegistrationCommand, ClientCommandLine, McpRegistrar,
      RegistrationRecord, HookRegistration}.cs`, served from the install, update
      and uninstall hooks in `src/BrowserAI/Updates/VelopackStartup.cs`.
      **`McpClientRegistration` is the one place that decides *how*** — the
      requirement was that whoever revisits this finds a file, not a decision
      smeared across an installer.
      <br><br>
      **Four properties, each with the measurement behind it.** *Never the
      stub*: `RegistrationTarget` refuses any path whose parent directory is not
      `current\`, and a real install measured **392,704 b** at the root against
      **17,911,808 b** inside it. *Idempotent, because the client is not* —
      measured @ 2.1.233, a duplicate `add` exits **1** — so an install
      removes-then-adds and an update adds-if-absent, which is also what stops a
      background update deleting arguments a user put on their own entry.
      *Visible on every failure and never fatal*: no client, a refusal, a timeout
      and a throw each write a log record **and** `<root>\mcp-registration.json`
      beside `current\`, carrying the command to run by hand; nothing can throw
      into the installer. *Survives an update*: measured rather than assumed —
      0.9.0 → 0.9.1 and a rollback back to 0.9.0, the entry unchanged both times,
      then `mcpServers` empty after the uninstall.
      <br><br>
      **Gated by `tests/BrowserAI.Tests/RegistrationTests.cs` — 14 tests, three
      of which drive the real client** against a scratch `CLAUDE_CONFIG_DIR`,
      behind the new `SuiteCapability.ClientCommandLine`, so a machine without a
      client skips and a release run fails rather than reporting the same summary
      either way. **The maintainer's own configuration was never written to and
      that is asserted, not intended**: SHA-256 identical across the whole
      session, and the live test proves the negative by looking for its own
      GUID-bearing install path in the user's file.
      <br><br>
      **Two things measurement contradicted, both corrected in the same commit.**
      `ProcessLog.Dispose()` claimed to close its file handle through the logger
      factory and did not — `LoggerFactory` never disposes a provider *instance*,
      measured at **0 disposals** — so a hook that opened a log and read it back
      could not; it now disposes the writer explicitly, with
      `ProcessLogTests.DisposingTheProcessLogReleasesTheFileHandle` red without
      it. And **`Environment.GetFolderPath(UserProfile)` does not read
      `%USERPROFILE%`**, which silently defeated an attempt to simulate a machine
      with no client on it — recorded with the gap named rather than papered
      over. Both are in [kb](kb/windows/processes.md#interop-and-the-toolchain)
      with [rows 87–88](kb/README.md#re-verification-index). The original text
      follows.
      <br><br>
      **BrowserAI registers itself as an MCP server. Nothing does this, and it is
      the charter's founding promise.**
      [§B](plan/B-mcp-server.md)'s first sentence is *"registered once at system
      or user scope, available in every repository, with no per-repo files"*, and
      [`PLAN.md`](PLAN.md)'s §B row hands the remainder to *"a §G/installer
      concern"*. **[§G](plan/G-updates.md) never received it** — it never
      mentions registering an MCP server with a client, and its landmine 3
      (*"register `current\BrowserAI.exe`, never the execution stub"*) is about
      which path Velopack stamps as `--mainExe`, a different thing. Verified
      across the repository: nothing in `src/`, `build/` or `tests/` writes a
      client configuration, calls `claude mcp add`, or touches a registry key;
      `vpk pack` runs with `--shortcuts None`; every Velopack hook exists only to
      log, and `VelopackStartup` says so in as many words. **What ships today is
      an installed, self-updating, self-sweeping binary that no client is
      configured to talk to** — and [README §1](README.md) opens by rejecting
      exactly that world, where onboarding *"requires a repo, a `.mcp.json`, hook
      registrations"*. It cannot move earlier than the installed layout
      ([step 19](plan/build-order.md#19-velopack-package-update-roll-back)) and
      cannot move later than the release, which is why it fell between them.
      **Decide the scope first** (user or machine), then which clients are
      written, then whether the installer or the binary performs it — and gate it
      with a test, because nothing about its absence is red.

- [x] ~~**Nothing prunes a superseded browser revision.**~~ ✅ **Built 2026-08-16
      by an agent an API limit killed mid-edit, restored 2026-08-17, and
      audited 2026-08-17 — the audit is what this line records.**
      `src/BrowserAI/Runtime/RevisionPrune.cs` runs from
      `BrowserProvisioner.Install` after a successful provision, inside that
      family's mutex. **Against §A it does what it must**: it deletes only a
      directory whose name carries a prefix the resolved manifest itself names
      and whose revision the manifest no longer carries, so the current revision,
      `.links` — whose deletion is the exact hazard
      `PLAYWRIGHT_SKIP_BROWSER_GC=1` exists to prevent — and anything a person
      put there are all left alone; it holds **every** family's provisioning
      mutex at a zero timeout for the whole pass; and it retains, names and logs
      a revision a live process is running out of, proven against a **real**
      planted process rather than a stub.
      <br><br>
      **What the audit found, and it is the reason this entry exists.**
      `PruneLog.PassFailed` — *"Pruning superseded browser revisions failed. The
      browser that was just provisioned is installed and usable"* — was declared
      and **called from nowhere**, and the call site sat bare inside
      `Install`'s catch-all. **Planted 2026-08-17**: with the `catch` removed,
      a prune that throws makes the provisioner log
      `Provisioning chromium failed` at **Error**, carrying the pruner's
      exception, over a 203.8 MB download that had just succeeded —
      `RevisionPruneTests.APruneThatThrowsLeavesTheProvisionItFollowedSuccessful`
      red at *"Expected to be true"*, then reverted, 7 of 7 green. **The plant
      also corrected the fix's own justification**: the status the caller sees
      stays `Installed` either way, because `Peek` reads the completion marker
      before the cached result — so what the guard prevents is a confident wrong
      answer in the log, not a wrong answer to the model, and the test asserts
      the absence of event 70 as well as the presence of event 86.
      **`BrowserProvisioner.PruneSupersededRevisions()` was deleted**: it was
      public, had **zero** callers, and its own doc claimed one — *"so the pass
      can be driven on its own, by the suite"*.
      <br><br>
      **Three documents were contradicted by the tree and were corrected with
      it**, all of which asserted the obligation as outstanding:
      `src/BrowserAI/Protocol/ChildEnvironment.cs`,
      [pre-release item 4](PRE-RELEASE.md) and
      [`UPSTREAM-REVIEW.md`](UPSTREAM-REVIEW.md)'s `browsers.json` row. The
      [hazard index](plan/hazards.md) gained the two rows it had never had: the
      obligation itself, **closed** with the tests that hold it, and the
      consequence nobody had written down — **a rollback re-downloads 203.8 MB**,
      because the build it rolls back from has already pruned the revision the
      older one names, accepted with the alternative (keep every revision
      forever) named beside it.
      <br><br>
      **The maintainer's own browsers root was measured either side of every
      run: 768 MB before, 768 MB after**, four revisions unchanged. Every test
      drives a scratch root. The original text follows.
      <br><br>
      **Nothing prunes a superseded browser revision.**
      [§A](plan/A-runtime.md#first-run-browser-provisioning): *"`PLAYWRIGHT_SKIP_BROWSER_GC=1`
      is mandated, so pruning old revisions becomes BrowserAI's job."* No code
      prunes anything. `browserai_reinstall_browser` deletes the **whole** tree
      and re-downloads, which is a different operation. The obligation is
      asserted in three surviving places and satisfied in none —
      `src/BrowserAI/Protocol/ChildEnvironment.cs`,
      [pre-release item 4](PRE-RELEASE.md) (*"the old revision sits on disk
      until something prunes it"*) and
      [`UPSTREAM-REVIEW.md`](UPSTREAM-REVIEW.md). **Every `browsers.json` bump
      strands ~430 MiB per machine, forever**, and the hazard index has a
      **closed** row for turning the GC off and no row at all for the obligation
      that turning it off creates. Prune on a successful provision, keeping the
      revision the shipped manifest names.

- [x] ~~**`init`'s description carries neither the real-Chrome-profile warning nor
      the retention policy.**~~ ✅ **Both added 2026-08-17, and the first draft
      did not fit.** `SessionToolSurface`'s `init` description gains two
      sentences: the security one says any path is accepted and none validated,
      that a directory already holding a browser profile — *"the user's real
      Chrome profile, or a copy"* — becomes this session's, and that a
      `persistent` session then drives its live cookies and logins, as can any
      agent given the path; the retention one says nothing expires, that
      BrowserAI never deletes a session directory, and names
      `browserai_destroy` and `browserai_list`, because a retention policy with
      no way to act on it is a fact rather than guidance.
      <br><br>
      **The budget was measured before and after, and it is the finding.** The
      description was **1,519 bytes of 2,048** with 529 of headroom; the two
      sentences as first written cost **773** and took it to **2,292** — which
      the client truncates in silence, removing the tail of the retention policy
      and reporting nothing. They were rewritten to **472** rather than anything
      else being cut, and the description now measures **1,991**, confirmed by
      an exact-value assertion run once and then replaced by the cap.
      **57 bytes of headroom**, and both required sentences are at the end of the
      string, so an overflow deletes exactly the two things that were required.
      The description is composed from `SessionModes.Table`, so a fourth mode
      grows it with nobody editing the file — which is why the cap assertion is
      in the same test rather than left to the surface-wide one.
      <br><br>
      **Gated by `ModelSurfaceTests.TheCreationToolsDescriptionCarriesTheProfileWarningAndTheRetentionPolicy`**,
      six declared phrases rather than a whole-string comparison, read out of the
      advertised surface rather than off the class. **Planted by deleting both
      sentences**: red, naming all six by name, then reverted, 7 of 7 green.
      [Hazard row 113](plan/hazards.md) — *"the mitigation is descriptive … not
      enforced"* — is closed with it, stating plainly that its other half
      (`SessionManager` event 40, the resolved absolute directory at open) has no
      test of its own. The original text follows.
      <br><br>
      **`init`'s description carries neither the real-Chrome-profile warning nor
      the retention policy.** [§C](plan/C-sessions.md#where-guidance-lives-three-channels-two-of-them-capped)
      requires both *in the creation tool's description specifically* — *"the
      spec requires retention to be stated here"* — and
      [README](README.md) makes the same demand independently: *"the `init` tool
      description is a security surface … say plainly what pointing at an
      existing browser profile does."* `SessionToolSurface`'s `init` description
      carries argument meanings and the mode table and neither of these.
      Retention is stated on `resume` and on `list`, which is everywhere except
      where it was required. **No test asserts description *content***, only the
      byte cap and the mode rendering, so nothing goes red. Add the two
      sentences, and assert them.

- [x] ~~**Two of the four rules under [*"what the build itself must fail on"*](plan/testing.md#what-the-build-itself-must-fail-on)
      are asserted by nothing.**~~ ✅ **Closed 2026-08-16, and two more with
      them.** `BuildConfigurationTests` gains
      `WarningsAreErrorsForEveryProject`, `UnreachableCodeIsPromotedToAnErrorByName`,
      `AotAndTrimAnalysisIsScopedPerAssemblyAndNeverRepoWide`,
      `TheSdkFloorRollsForwardAndTheRunnerIsTheOneTUnitNeeds` and
      `TheApplicationManifestIsLongPathAwareAndNeverAsksForElevation` — the last
      two because `global.json` and `src/BrowserAI/app.manifest` were outside
      every scan the suite made, and [§stack](plan/stack.md#the-build-configuration-this-plan-has-never-mentioned)
      requires four settings between them.
      <br><br>
      **Proven by removing all five properties at once**: 12 tests, 5 failed, 7
      passed, each naming its own property — *"Expected to contain
      Directory.Build.props"*, *"Expected to be true"* (CS0162), *"Expected to be
      empty"* (a repo-wide `SuppressTrimAnalysisWarnings`), *"Expected to be
      equal to \"latestMajor\" but received \"&lt;absent&gt;\""* and *"Expected to be
      equivalent to [true]"* (longPathAware) — then reverted, 12 of 12 green.
      `RepositoryLayout` gained `RepositoryWideBuildFiles`, which names
      `Directory.Build.targets` **although it does not exist**, and `BuildFiles`
      now also reads the importable fragments under `build/`: a scan that
      enumerates only what is present cannot fail when the forbidden thing
      arrives in a new file. The original text follows.
      <br><br>
      **Two of the four rules under [*"what the build itself must fail on"*](plan/testing.md#what-the-build-itself-must-fail-on)
      are asserted by nothing.** Both are correct in the tree today and would
      survive their own deletion green: **`TreatWarningsAsErrors` and the
      `CS0162` promotion** (`BuildConfigurationTests.NoBuildFileSuppressesWarnings`
      forbids `NoWarn` and `WarningsNotAsErrors` and does not look for the
      absence of either property), and **"AOT and trim warning suppression is
      scoped per-assembly, never repo-wide"** (a
      `<SuppressTrimAnalysisWarnings>true</SuppressTrimAnalysisWarnings>` added
      to `Directory.Build.props` is invisible to every scan the suite makes).
      This is the same shape as `UseSystemResourceKeys`, which sat in the same
      list and **was** closed — so the pattern is established and two of four
      were simply missed.

- [x] ~~**Two tests still degrade to a weaker assertion and report `passed`,
      outside `SuiteEnvironment`'s gate.**~~ ✅ **Both inside the gate
      2026-08-16.** `JobContainmentTests.TheBundledNodeAndItsDescendantsAreContained`
      calls `RequireRepositoryPayload`;
      `StraySweepTests.TheSweeperFindsARealBrowserItLaunchedItselfInTheInteractiveSession`
      calls `RequireProvisionedChromium`. **Proven by removing each capability
      and running the test alone** — `payload/` renamed aside: *skipped*
      (exit 0, `skipped: 1`) and, with `BROWSERAI_RELEASE_RUN=1`, *failed*
      (exit 2); `chromium-1237` renamed aside: the same two outcomes, each
      naming the missing path and the command that produces it. Both
      capabilities were restored and verified afterwards.
      <br><br>
      **The distinction the degraded branches drew was kept rather than
      dropped**: `SuiteEnvironment.StateOf` now answers
      `CapabilityState.Partial` for a browser revision directory that exists
      without its executable, which fails in **both** modes — so *"nobody
      provisioned it"* is still told apart from *"it was provisioned and the
      binary is missing"*, which is the half that reads as a clean machine. The
      original text follows.
      <br><br>
      **Two tests still degrade to a weaker assertion and report `passed`,
      outside `SuiteEnvironment`'s gate.** `JobContainmentTests.TheBundledNodeAndItsDescendantsAreContained`
      and `StraySweepTests.TheSweeperFindsARealBrowserItLaunchedItselfInTheInteractiveSession`
      each fall back to a trivial assertion when the payload or a provisioned
      Chromium is absent, so neither appears in the coverage block and neither
      turns red under `BROWSERAI_RELEASE_RUN=1`. The second is **R5's only
      real-browser arm** — the only proof that a real Chromium publishes what
      attribution reads. `RequireRepositoryPayload` and
      `RequireProvisionedChromium` already exist for exactly this; the gate was
      built and these two were missed, which means
      [pre-release item 8](PRE-RELEASE.md)'s *"every layer ran"* still has
      two holes.

- [x] ~~**`InstanceDirectory` uses the one primitive [§E](plan/E-lifecycle.md#deleting-a-tree-that-fights-back)
      says never to use.**~~ ✅ **Fixed 2026-08-16**, and the measurement taken to
      prove it found something worse underneath.
      `src/BrowserAI/Runtime/InstanceDirectory.cs` now goes through `TreeDelete`
      and **logs every node that survived** at Warning;
      `InstanceDirectoryTests.ADirectoryWithAFileSomethingHoldsIsEmptiedAroundItAndTheSurvivorIsNamed`
      holds a file `FileShare.None` in the profile and requires the survivor
      named in the log. Planting the shipped body back turns it red:
      *"Expected to not be empty"* at `Assert.That(reported).IsNotEmpty()`.
      <br><br>
      **What the measurement contradicted, and it is not a footnote.** The class
      claimed *"Windows refuses to delete a directory that is some process's
      current directory, so `Delete` simply fails for a run that is still
      going"*. Measured twice on .NET 10.0.11 against a live holder:
      `Directory.Delete(path, recursive: true)` **empties the directory
      completely** and only then fails on the node — so the startup sweep was
      deleting a running BrowserAI's generated config, surface-child profile,
      output and downloads folders, on every start, against any instance older
      than five minutes, in silence. `Directory.Move` refuses the same directory
      **with its contents untouched** and succeeds once the holder exits, so the
      sweep now claims by renaming aside and walks only what it won.
      `InstanceDirectoryTests.ARunThatIsStillGoingKeepsItsInstanceDirectoryAndEverythingInIt`
      is red without it: *"Expected to be true"* at
      `Assert.That(File.Exists(config)).IsTrue()`.
      <br><br>
      **A second document lost an argument to the same measurement.**
      [§E](plan/E-lifecycle.md#deleting-a-tree-that-fights-back) and
      `TreeDelete`'s doc comment both said the framework primitive gives *"an
      exception and no partial progress"*. It does make partial progress, and
      leaves the same nodes behind that the hand-rolled walk does — the
      difference is that it names **one** node where the walk named four and
      two. Both corrected carrying their previous text; the fact is in
      [kb](kb/windows/processes.md#interop-and-the-toolchain) with
      [row 86](kb/README.md#re-verification-index). `TreeDelete`'s *"third
      caller … at §G"* is corrected to `InstanceDirectory`, and the
      [hazard row](plan/hazards.md) is closed for all three callers with a
      second row added for the live-run gutting. The original text follows.
      <br><br>
      **`InstanceDirectory` uses the one primitive [§E](plan/E-lifecycle.md#deleting-a-tree-that-fights-back)
      says never to use.** `src/BrowserAI/Runtime/InstanceDirectory.cs` calls
      `Directory.Delete(path, recursive: true)` inside a swallow-all `catch`, on
      a path that runs at **every clean exit** and **every startup sweep**, while
      `src/BrowserAI/Runtime/TreeDelete.cs` — written for this, post-order, per
      node, reporting what survived — sits with two callers and not this one. It
      fails whole rather than per node and reports nothing, so an instance
      directory 99% deletable is not deleted at all and nobody learns which file
      held it. **And `TreeDelete`'s own doc comment promises a third caller
      *"at §G"*** that shipped and did not arrive, because the Velopack swap is
      `force_stop_package`, upstream's own binary. Fix the call site; correct the
      comment; the hazard row for the `AllDirectories` abort is currently marked
      closed while a live instance of it is in the tree.

- [x] ~~**Two guards that are claimed to exist and do not.**~~ ✅ **Both closed
      2026-08-16.** **The toolhelp half is banned rather than the claim
      weakened**, because [§D](plan/D-locking.md) asks for zero occurrences
      asserted: `NeverByImageNameTests`' needle list gains `szExeFile`, the one
      PROCESSENTRY32 member a toolhelp walk can learn a name from. The **walk**
      is deliberately not banned — `JobProbe` enumerates pids through toolhelp
      and declares that member as `ImageNameWeDoNotRead` precisely so it cannot
      be compared — and banning it would have created the one exclusion
      `build/BannedSymbols.txt` refuses to have. That file now carries a
      `Corrected 2026-08-16` note saying the coverage it claimed was absent for
      as long as the sentence existed.
      <br><br>
      **`ArtifactRouter.TryWrite`'s answer now reaches the answer**, at both call
      sites: a `session.json` that could not be written appends *"COULD NOT BE
      WRITTEN"* to the note that names its path, and a roll-up that could not be
      written does the same in `init`/`resume`'s description and in `destroy`'s
      summary. Proven by planting the two discards back —
      `ArtifactRoutingTests.AnIndexThatCouldNotBeWrittenIsNamedInTheAnswerRatherThanImplied`
      and `.ARollUpThatCouldNotBeWrittenIsNamedInTheAnswerRatherThanImplied`,
      both *"Expected to contain \"COULD NOT BE WRITTEN\""*. The original text
      follows.
      <br><br>
      **Two guards that are claimed to exist and do not.**
      `build/BannedSymbols.txt` states that *"a toolhelp walk that matches
      `szExeFile` … is covered by `NeverByImageNameTests`"*; it is not —
      [§D](plan/D-locking.md) forbids *"any WMI **or toolhelp** query filtered by
      executable name"* and asks for zero occurrences asserted, and the test's
      needle list has `taskkill`, `GetProcessesByName`, `Win32_Process` and
      `Get-Process` and nothing for toolhelp. Separately,
      `ArtifactRouter.TryWrite` returns a bool whose own doc says *"it must not
      be silent either, so the answer carries what could not be written"*, and
      **both call sites discard it** — a failed `session.json` or roll-up write
      is currently silent, in a project whose first rule is that silent failure
      is the enemy.

- [x] ~~**Load-bearing single lines in [§G](plan/G-updates.md) that no test
      touches.**~~ ✅ **All five guarded 2026-08-17, in `UpdateTests`, and
      proven by removing all five at once**: 21 tests, 5 failed, 15 passed, 1
      skipped, each failure naming its own line — *"Expected to contain
      \".SetAutoApplyOnStartup(false)\""*, *"…\"AllowVersionDowngrade = true\""*,
      *"…\"ExplicitChannel = feed.Channel\""*, *"…\"Copy-Item -LiteralPath $full
      -Destination $ArchiveDir -Force\""*, and, for the ban rather than the
      line, *"LocalAppDataPaths.cs resolves a path from
      AppContext.BaseDirectory"* after planting a use of it in the one class
      §G forbids it in. Reverted, 21 of 21 green, and `git diff --stat` showed
      the test file alone — which is what proves the revert rather than a
      re-read.
      <br><br>
      **Two of the five assert more than the line.** `SetAutoApplyOnStartup` is
      paired with an **ordering** check, because the hazard is a reorder: the
      same call serves the installer's fast-exit hooks, so four startup steps —
      `InstallLocation.RootAppDir`, `new LocalAppDataPaths(`, `ProcessLog.Create(`
      and `Environment.GetEnvironmentVariable(` — are asserted to appear *after*
      it in `Program.cs` by position. `AllowVersionDowngrade` is asserted
      **together with its pipeline half** (`Test-ReleaseVersion.ps1`'s
      `RollbackRepublish`, `'rollback'`, `'monotonic'`), because §G's requirement
      is that the two agree and either alone is the `ExoFabric/UCC` state.
      <br><br>
      **The `ExplicitChannel` scan was too broad on the first draft, and the
      failure is worth keeping.** A scan for the bare name fails on
      `UpdateFeed`'s own refusal messages, which name
      `UpdateOptions.ExplicitChannel` in the sentence telling a caller where the
      channel belongs — so the needle is the **assignment**, exactly as
      `ReleaseScriptTests` scopes its `--msi` check to the argument array. A test
      that fails on the documentation of the rule it enforces trains the next
      person to delete the explanation. The original text follows.
      <br><br>
      **Load-bearing single lines in [§G](plan/G-updates.md) that no test
      touches.** `SetAutoApplyOnStartup(false)` — which the code itself calls
      *"the single most important line in this file"* — `AllowVersionDowngrade`
      (§G: *"both halves have to agree"*, and only the pipeline half is driven),
      `UpdateOptions.ExplicitChannel` (the three feed-URL shapes are tested, the
      assignment that consumes them is not), *"never `AppContext.BaseDirectory`"*
      (no source scan, although `MachineMutex` and never-by-image-name are both
      guarded exactly that way), and the `.nupkg` archive step. A deletion or a
      reorder in `Main` above the first of these is caught by nothing.

- [x] ~~**All 27 `Packaging and updates` rows in the [hazard index](plan/hazards.md)
      still read `open` with `—` for evidence**~~ ✅ **Adjudicated one at a time
      2026-08-17: 21 of the 27 are now closed with evidence and 6 are left open,
      because they are open.** The closures rest on what actually exists — the
      feed-URL refusal and the `ExplicitChannel` assignment, the
      `SetAutoApplyOnStartup` call and its position, the solitude gate against
      `force_stop_package` (measured at step 19 against **two real installed
      instances**, both offered 0.9.1, neither applying), `WaitExitThenApplyUpdates`
      with a requested shutdown rather than `Environment.Exit`, the three
      download timers, the archive step and its refusal, both halves of the
      rollback pair, `InstallLocation` for state outside `current\`, the
      `AppContext.BaseDirectory` scan, and the **real delta** — 97,216 b against
      49,043,493 b, applied by a client that logged `deltas=1`.
      <br><br>
      **What was left open, and why, because that is the half that makes the
      rest believable.** The production feed URL (the row's gate is the one
      `[Skip]`ped test, which blocks the release by design); the transient
      disk budget of the swap, unmeasured; the Rust binaries' own Windows floor,
      unmeasured; SmartScreen on an unsigned `Setup.exe`, unmitigated; the
      repair-install that destroys the root, unguarded; and the shortcuts row,
      **half** of which is guarded — `--shortcuts None` is asserted and was read
      back out of a real install's `sq.version`, while nothing removes
      `%LOCALAPPDATA%\velopack\` at uninstall and nobody has looked at what it
      holds. Those last two carry that text in the evidence column rather than
      `—`, because *what is guarded and what is not* is worth more than an em
      dash.
      <br><br>
      **The eight named rows outside packaging are closed too**, by line number
      at the audit commit: 100 and 101 (the protocol split, both halves asserted
      by `ProtocolSplitTests`), 156 (`T:System.Console` banned outright, so
      CP437, CRLF and a BOM are unreachable), 176 (R5 session-0 blindness —
      closed by *deleting* the only caller that could land in session 0), 185
      (no queue exists for an abrupt exit to discard), 186 (the same ban, and it
      is the stronger half of it), 188 (`LockRecord` parses strictly and names
      the unrecognised key). 184 was already closed by the tree-delete work.
      Every closure names what would re-open it where it does not rest on a
      test. The original text follows.
      <br><br>
      **All 27 `Packaging and updates` rows in the [hazard index](plan/hazards.md)
      still read `open` with `—` for evidence**, after
      [step 19](plan/build-order.md#19-velopack-package-update-roll-back) ran the
      whole lane for real and closed most of them. Six more rows across §B, §C
      and §E are in the same state — 100, 101, 156, 176, 184, 185, 186, 188 — and
      **row 184 is worse than stale**, since it is marked closed for
      `browserai_destroy` while `InstanceDirectory` is a live instance of it.
      The hazard index is the one file that outlives the plan; a row that never
      gained its evidence is the failure the file exists to prevent.

- [x] ~~**The hazard index cannot outlive the plan until its links are repointed.**~~
      ✅ **Repointed 2026-08-17, and it is now self-contained.** Every link into
      the twelve consumed sections is the text it was linking with, the
      *Section shorthands* legend **defines all ten shorthands in words** and
      hands the map to [`PLAN.md`](PLAN.md), and what remains points only at
      `kb/`, `README.md`, `PLAN.md` and the code — which is also why every row's
      evidence now names files and test methods rather than plan anchors.
      **Counted by a third method, stated so it can be reproduced:** every
      markdown inline link whose target names one of the twelve consumed files,
      counted per occurrence — **146 links on 109 lines, 107 of them table
      rows**. It does not reproduce 135/106/104-of-139 or 136/107/105-of-135 and
      is not meant to: the file has gained rows since both, two of them on the
      day of the third count. All three are left as measured.
      **One link lost its anchor rather than gaining a corrected one**, and says
      so on the row: the kb heading *"New defect: `Setup.exe -- <args>` hangs
      forever"* slugs differently for the repository's own sweep, which reads
      `<args>` as an HTML tag, and for GitHub, which reads it as four
      characters. Naming the heading in the sentence works for both readers.
      Link sweep: **34 markdown files, 0 broken links**, relative paths and
      `#anchor` slugs both. The original text follows.
      <br><br>
      **The hazard index cannot outlive the plan until its links are repointed.**
      [`plan/hazards.md`](plan/hazards.md) carries **135 links into the twelve
      files that are consumed**, on 106 lines, across 104 of its 139 rows —
      including its own *Section shorthands* legend, one line that links all nine
      lettered sections and gives every `§X` token below it its meaning.
      ⚠️ **2026-08-16: the file has moved since these were counted** — one row
      rewritten and one added, both for the tree-delete pair. Re-counted by a
      different method (table lines carrying six or more cells): **136 links on
      107 lines across 105 of 135 rows**. That method does not reproduce the
      audit's 139, so the two counts are not comparable and the audit's figures
      are left exactly as measured rather than adjusted. Whenever
      the plan does go, that is a rewrite pass over the surviving file, and **no
      step owns it.**

- [x] ~~**Decide whether [`PRE-RELEASE.md`](PRE-RELEASE.md) is consumed
      or survives**~~ ✅ **Decided and done 2026-08-17: it survives, and it has
      moved out of `plan/`.** It is `PRE-RELEASE.md` at the repository root
      beside [`UPSTREAM-REVIEW.md`](UPSTREAM-REVIEW.md), which is the precedent
      the audit named. **The release gate moved into it whole rather than being
      copied** — [`plan/testing.md`](plan/testing.md#the-release-gate) keeps the
      heading and points here, so every link that existed still resolves, and
      the file's own rule (*"this file points; it does not restate"*) is honoured
      by *owning* the sequence instead of duplicating it. Repointed in the same
      commit: three build scripts (including the `manifest.json` string shipped
      beside every `.nupkg`), six documents and six test files, with the relative
      depths in the `.cs` doc comments corrected rather than shifted.
      **Why now rather than on deletion day:** doing it then would have meant
      editing the only release gate this project has in the same pass as twelve
      file removals. The original text follows.
      <br><br>
      **Decide whether [`PRE-RELEASE.md`](PRE-RELEASE.md) is consumed
      or survives — raised at [step 20](plan/build-order.md#20-the-first-release),
      recommended at the audit, still the maintainer's.** It is the only release
      gate that exists, four of its fourteen items are executable nowhere else,
      three build scripts name it by filename and one **writes `"pre-release.md
      item 11"` into every `manifest.json` shipped beside a `.nupkg`**. It also
      carries **18 outbound links into files that would be deleted**, ten into
      [testing](plan/testing.md), and its own rule is *"this file points; it does
      not restate"* — so surviving means absorbing
      [testing's release gate](plan/testing.md#the-release-gate) first, then
      moving to `PRE-RELEASE.md` at the root beside
      [`UPSTREAM-REVIEW.md`](UPSTREAM-REVIEW.md), which is the precedent.

## What is left after the classification pass — 2026-08-17

[`PLAN.md`](PLAN.md#the-three-buckets--every-outstanding-item-sorted-2026-08-17)
sorts every outstanding item into **(a)** closable now, **(b)** blocked on the
maintainer and **(c)** deferred by a recorded decision. Fourteen closed that
day. What follows is everything that did not, so this file stays the work list
rather than becoming a second copy of the classification — **go there for the
reasoning, come here for the queue.**

- [ ] **The failed-rewrite recovery of `lock.json` has no test.** §D:
      *"a failed rewrite must not also release the lock — the handle is dropped
      before the replacement, so an exception on the way through left the
      session silently unowned until the recovery path was added."* **It shipped
      broken once**, which is precisely the shape that earns a regression test,
      and it has none. What is missing is a seam: the rename is
      `SessionLock`'s own and nothing can make it fail on demand, so provoking
      the failure means either an injectable file operation or a probe process
      that holds the replacement path at the right moment. Do the second if the
      first would put a test-only interface on the product's hot path.

- [ ] **61 hazard rows still read `open` with `—` for evidence.** In
      [the one file that outlives the plan](plan/hazards.md), and the file's own
      rule is that *"a row marked `closed` with `—` here is not closed"* — the
      converse is what this is: rows nobody has adjudicated either way. Counted
      2026-08-17 by category: **Bundling and AOT 14, Process and OS (Windows)
      13, Child runtime and configuration 10, Tooling and CI 8, Protocol and
      SDK 7, Packaging and updates 6, Handle routing and instance lifetime 3.**
      The previous round did 27 packaging rows and 8 named ones and left 6 open
      *because they are open*, which is the standard to hold to: **an honest
      `open` with a reason beats a `closed` with a weak one.** Many of these
      will close against tests that now exist; some are upstream behaviours that
      cannot close at all and should say so.

      **Corrected 2026-08-17 (previously "59 … Packaging and updates 4").**
      Re-counted off the file rather than off this entry: the total is **61**
      and packaging is **6**. Every other category was right. **The tell was in
      this paragraph the whole time** — it says the previous round *left 6 open
      because they are open*, two lines under a tally that said 4, so the entry
      contradicted itself and the contradiction was the accurate half. Nothing
      here can go red over a count, which is the same shape as the entry below
      this one: a number nobody re-derives is indistinguishable from a measured
      one. Re-derive with a category tally, not a total — a wrong total is
      visible only against the sum.

- [ ] **Watch [microsoft/playwright-mcp#1716](https://github.com/microsoft/playwright-mcp/issues/1716)
      and act on what upstream decides.** Filed 2026-08-17; the record of what
      was reported and why is in the closed item directly below this one.

      **Why this is a standing item rather than a filed-and-forgotten one.**
      The report proposes a fix, and **any of the three plausible outcomes
      changes something here**:

      - **Fixed upstream.** `chromiumSandbox` in a config file starts working,
        which means the key silently changes meaning between two versions that
        BrowserAI floats across. `BrowserConfiguration` does not set it and
        `SandboxFlagTests` asserts the flag reaches every node child on the
        **command line**, so nothing breaks — but the [re-verification
        index](kb/README.md#re-verification-index) should gain a row, because
        this is exactly the class of upstream change [the golden
        snapshot cannot see](plan/testing.md#the-upstream-review-gate): the
        tool surface does not move, the config schema does not move, and the
        behaviour behind one key inverts.
      - **Declined, or closed as intended.** Then the non-Linux `= true` arm of
        `validateBrowserConfig` is dead code by design rather than by accident,
        and that is worth writing into [kb](kb/playwright/configuration.md) as
        a settled upstream position rather than leaving it recorded here as a
        defect. **A declined report with a reason is worth as much as a fix**,
        and it is the half that never gets written down.
      - **No response.** The most likely outcome, and the one needing an
        explicit decision rather than drift: either let it sit, or fix it
        forward with a PR. **Do not work around it** — BrowserAI is already
        immune, so a workaround would be code carrying risk for no benefit.

      **What to check, and how.** `gh issue view 1716 --repo microsoft/playwright-mcp
      --comments`. The natural moment is [the daily drift check](CLAUDE.md#the-daily-drift-check):
      when `@playwright/mcp` moves, this is one of the things the bump can
      silently invalidate, which is the same argument the re-verification index
      exists for. **A version bump that closes this issue and a version bump
      that ignores it look identical from the registry.**

- [x] ~~**Report the `chromiumSandbox` defect to `@playwright/mcp`, from the
      maintainer's own account.**~~ ✅ **Filed 2026-08-17 as
      [microsoft/playwright-mcp#1716](https://github.com/microsoft/playwright-mcp/issues/1716)**,
      from SixFive7. Measured against **0.0.79** on 2026-08-16 and
      confirmed at build-order steps 7 and 12.

      **The cause was re-traced immediately before filing, and it changed** —
      see the corrected chain below. The version that would have gone out an
      hour earlier blamed commander for a default it does not set, which is a
      claim the maintainer would have disproved in one command before finding
      the real cause four lines away. **Verifying a finding because it is about
      to leave the building is the cheapest place this project has ever caught
      a wrong reason**, and the only one where the cost of not catching it
      would have been someone else's afternoon.

      **What is left here is the record, not a task.** BrowserAI itself was
      never exposed: it passes `--sandbox` on the command line and
      `SandboxFlagTests` asserts the flag survives to every node child.

      **The defect.** `"launchOptions": { "chromiumSandbox": true }` in a config
      file is parsed, validated, and then **discarded**. Only the `--sandbox`
      CLI flag enables the sandbox. So a configuration that sets the key
      believes it has a sandbox and does not, and **nothing anywhere reports
      it** — no warning, no error, no difference in any health signal. It is
      this project's founding failure class, arriving from upstream, on a
      security control.

      **The cause, so the report does not need them to find it.** Traced
      through the shipped bundle 2026-08-17 and measured; four links, each
      quoted from `playwright-core/lib/coreBundle.js` at 0.0.79:

      1. `decorateMCPCommand`'s action normalises the flag —
         `options.sandbox = options.sandbox === true ? void 0 : false;`
      2. `configFromCLIOptions` is guarded and looks correct —
         `if (cliOptions.sandbox !== void 0) launchOptions.chromiumSandbox = cliOptions.sandbox;`
      3. `mergeConfig` runs **CLI last** (file → env → CLI) and merges through
         `pickDefined`, so `undefined` is dropped and `false` is kept.
      4. `validateBrowserConfig` defaults only what is still unset —
         `if (browserName === "chromium" && launchOptions.chromiumSandbox === void 0)`,
         whose non-Linux arm is `= true`.

      **Step 1 is the defect: it collapses *"no flag"* into *"`--no-sandbox`"*.**
      Measured over `tools.decorateMCPCommand` with the action replaced, so the
      raw commander values are read
      ([`.work/sandbox-probe/probe.js`](.work/sandbox-probe/probe.js)):
      empty argv → `undefined`, `--sandbox` → `true`, `--no-sandbox` → `false`.
      The ternary turns the first and third into the same `false`, which is
      defined, so it survives `pickDefined`, overwrites the file at step 3 and
      leaves step 4's `= true` arm **unreachable**. The key cannot work, rather
      than happening not to.

      **Corrected 2026-08-17 (previously "commander gives `sandbox` a default
      of `false` rather than leaving it `undefined`").** Commander leaves it
      `undefined`, exactly like `headless`, which the same probe reads as the
      control. The earlier observation of `false` was real — it was read after
      upstream's own ternary had run — but the *attribution* was wrong, and it
      was the attribution that was about to be filed. **A report blaming
      commander would not have reproduced**, and would have cost the maintainer
      the time it takes to disprove a wrong cause before finding the real one
      four lines away.

      **How to show it in two commands.** Launch with the key set in a config
      file, then read the browser's **resolved command line** — not the config,
      which reads back fine. `--no-sandbox` is present. BrowserAI's
      `SandboxFlagTests` does exactly this and asserts the flag is absent from
      **every** node child, because only the command line proves it.

      **Why it needs the maintainer.** It is an account and a public report to
      own, not a code change. **Not urgent for this product** — BrowserAI
      already passes `--sandbox` on the command line and tests that it survives
      to the browser — but it is a live silent failure for everyone else who
      sets that key, and this project has the measurement in hand.

- [ ] **Audit the repository for justifications that are stated as fact and were
      never measured.** A new failure shape, found 2026-08-17, and it is worse
      than the ones already catalogued because **nothing in this project can
      catch it**.

      **What happened.** *"`[DllImport]` relies on runtime IL-stub generation
      that NativeAOT does not do"* is **false on Windows** — ILC compiles those
      stubs ahead of time, re-measured 2026-08-17 with a **38**-declaration probe
      (across kernel32, user32, ntdll and rstrtmgr) that published
      with zero warnings and ran correctly. That sentence was written into
      **three source files** as the justification for `[LibraryImport]`, and
      into every agent brief of the build. It survived twenty-two build steps,
      a plan audit that enumerated ~450 requirements, and 392 tests.

      **Why nothing caught it.** Every mechanism this repository has protects
      *claims about behaviour*: a test fails, a snapshot diffs, an analyzer
      errors, a marker test goes red. **This was a claim about a reason.** The
      rule it justified was correct — `[LibraryImport]` really is right, and is
      Microsoft's documented first recommendation — so nothing behaved wrongly
      and nothing could be red. A false *why* attached to a true *what* is
      invisible to every gate here, and it propagates: it was copied into
      comments precisely **because** it sounded like the kind of thing worth
      writing down.

      **Why it matters even when the conclusion survives.** A reason is what the
      next person reasons *from*. Someone deciding whether a generated
      `[DllImport]` library is viable would have concluded "impossible under
      AOT" and stopped — which is exactly the question that surfaced this, and
      the wrong answer was one search away from being acted on.

      **What to do.** Sweep the load-bearing justifications — the `<remarks>`
      blocks and MSBuild comments that explain *why* a rule exists, especially
      where they assert a platform or toolchain behaviour — and sort each into
      measured here, cited to a source, or **assumed**. The third bucket is the
      finding. Two known instances are already corrected (this one, and
      `ILLinkTreatWarningsAsErrors` being credited with a failure that
      `TreatWarningsAsErrors` causes); **the sweep is to find out whether they
      were the only two.** Start with `src/BrowserAI/Interop/`, `Directory.Build.props`
      and `src/BrowserAI/BrowserAI.csproj`, which is where toolchain reasoning
      is densest.

      **The general form, worth keeping even after the sweep:** *a rule with a
      measured reason and a rule with a plausible reason are indistinguishable
      until someone acts on the reason instead of the rule.*

- [x] ~~**Decide the Win32 interop question, and consider Vanara alongside
      CsWin32.**~~ ✅ **Decided 2026-08-17. The threshold is STRUCK, the
      hand-written `[LibraryImport]` declarations stay, and CsWin32 is adopted
      for one job only — a *test-only layout oracle*.**
      [`plan/stack.md`](plan/stack.md) set the threshold **before any code
      existed** — *"once a seventh Win32 API is needed, adopt
      `Microsoft.Windows.CsWin32`"* — and it was long past: **41
      `[LibraryImport]` declarations across 9 files** in
      `src/BrowserAI/Interop/`, with a further **42 across 9 files** in
      `tests/`, so a rewrite would have been **83 across 18**. A rule that is
      stated, correct-sounding and quietly not followed is the state this
      repository dislikes most, so it wanted a decision either way. This is it.

      **The comparison, looked up 2026-08-17 via the NuGet v3 API and the
      GitHub API, and re-verified in this session rather than carried over:**

      | Candidate | Licence | Latest stable | Published | AOT | Size Δ | Upstream | Verdict |
      |---|---|---|---|---|---|---|---|
      | **Hand-written `[LibraryImport]`** | — | — | — | native, and proven by 396 tests | 0 | ours | ✅ **kept** |
      | `Microsoft.Windows.CsWin32` | MIT *(generator only — see the open question below)* | `0.3.298` | 2026-06-17 | emits **`[DllImport]`, never `[LibraryImport]`** (measured **30/0** in generated output); publishes clean and runs | +31 KB † | active, pushed 2026-08-14 | ⚠️ **test-only** |
      | `TerraFX.Interop.Windows` | MIT | `10.0.26100.6` | 2025-12-12 | `[DllImport]`, blittable; publishes clean | +90 KB † | repo pushed 2026-07-20, but **no release in 8 months** | ❌ |
      | `Vanara.PInvoke.*` | MIT | `5.0.7` | 2026-08-15 | **disqualified** | +1.35 MB † | very active, pushed 2026-08-15 | ❌ **disqualified** |
      | `PInvoke.*` (`dotnet/pinvoke`) | MIT | `0.7.124` | **2022-06-30** | not assessed | — | **archived 2023-07-26** | ❌ |

      † The three size deltas are from the commissioning research pass against
      a 1,106,944-byte empty-AOT baseline and were **not re-measured here**.
      Everything else in the table was.

      **The axis that was thought disqualifying was resting on a false
      premise.** ① said *"a candidate built on runtime-marshalled `[DllImport]`
      is out regardless of how good its API is"*. `[DllImport]` is **not**
      runtime-marshalled under NativeAOT on Windows — ILC compiles its stubs
      ahead of time, measured with a 38-declaration probe, and the three places
      this repository asserted otherwise are corrected. **So CsWin32 emitting
      `[DllImport]` never disqualified it**, and the axis that actually decides
      is ④ floating, ② size, and the value of a rewrite — which is negative.

      **CsWin32 will never emit `LibraryImport`, and the reason is structural
      rather than a backlog item.** Issues
      [#593](https://github.com/microsoft/CsWin32/issues/593) (2022-08-10) and
      [#1333](https://github.com/microsoft/CsWin32/issues/1333) (2025-01-21)
      were both closed **not planned**: Roslyn does not chain source
      generators, so a generator emitting `LibraryImport` would produce code
      that nothing then processes. Worth noting that **#1333's own opening post
      repeats this repository's false premise verbatim** (*"`DllImport` will
      need to create an IL stub runtime … allowing for NativeAOT
      compilation"*), which is a fair guess at where it entered here.

      **Vanara's failure is this project's named enemy, and it deserves saying
      so.** A narrow raw-P/Invoke slice publishes clean and runs — **that is
      the trap, not a reprieve.** The moment a Vanara *helper* is used, which
      is the entire reason to choose Vanara,
      `QueryInformationJobObject<T>` produces **32 ILC diagnostics** (counted
      in the probe logs: 13× IL3050, 9× IL2075, 3× IL2067, 3× IL2072, 2×
      IL2070, 1× IL2057, 1× IL2077) and the published binary dies at runtime
      with `NotSupportedException: '…JOBOBJECT_BASIC_ACCOUNTING_INFORMATION' is
      missing structure marshalling data. This can happen for code that is not
      compatible with AOT.` **A library whose safe subset is silent and whose
      value-add is fatal is exactly the shape of every defect in the charter's
      opening table.** It also drags in `Vanara.PInvoke.Gdi32` transitively and
      its `Kernel32.FileAccess` enum lacks `SYNCHRONIZE`. *(The runtime
      exception, the Gdi32 drag and the enum gap are the research pass's
      findings and were not re-run here; the 32 diagnostics were re-counted.)*

      **Both awkward cases survive, and one is a finding against our own
      code.**

      - **`FILE_APPEND_DATA` without `FILE_WRITE_DATA` is safe from every
        candidate.** No candidate forces `GENERIC_WRITE`; CsWin32's generated
        `CreateFile` takes a raw `uint dwDesiredAccess` and returns a
        `SafeFileHandle`, and `FILE_APPEND_DATA = 0x4` is a distinct member of
        its `FILE_ACCESS_RIGHTS` enum. The
        [70-records-in-200 fix](kb/windows/processes.md#interop-and-the-toolchain)
        was never at risk.
      - **CsWin32's `CreateProcessW` is better than ours, and we are not
        adopting it.** It generates
        `ref Span<char> lpCommandLine`, which carries a length, validates the
        null terminator and writes the mutation back.
        `JobLauncher.cs:468` declares `ref char lpCommandLine` and the call
        site passes `ref commandLine[0]` — **no length, no terminator check**.
        Nothing is known to be wrong with it and 396 tests pass over it, but it
        is a weaker signature than the vendor's for the same call. **Recorded
        as a finding against our own code, not as an argument for adoption.**

      **What was built instead, and it is the option nobody had listed.**
      CsWin32 is referenced **from the test project only**, `PrivateAssets="all"`,
      as a *layout oracle*: `tests/BrowserAI.Tests/InteropLayoutTests.cs` asks
      Microsoft's own Win32 metadata what each hand-written struct should
      weigh, and asserts it. All **seven** already match exactly — `STARTUPINFOW`
      104, `STARTUPINFOEXW` 112, `PROCESS_INFORMATION` 24,
      `SECURITY_ATTRIBUTES` 24, `IO_COUNTERS` 48,
      `JOBOBJECT_BASIC_LIMIT_INFORMATION` 64,
      `JOBOBJECT_EXTENDED_LIMIT_INFORMATION` 144 — with
      `offsetof(Affinity)` 48, `offsetof(LimitFlags)` 16 and `offsetof(IoInfo)`
      64 agreeing both ways.

      **Why it earns the dependency.** A mis-shaped struct is the one defect
      class here that *cannot present as an error*: the kernel reads whatever
      is at the offset it expects and returns a plausible wrong answer. **Two
      faults were injected and both were caught** — ① two `nuint` fields to
      `uint`, size 64 → 48, 2 of 4 tests red; ② `Affinity` reordered, which
      **leaves the size at 64** and slides its offset 48 → 56, where the size
      assertion *passed* and only the offset assertion caught it. The second is
      the case a size-only oracle misses, and it is why the offsets are
      asserted. Both reverted; `JobObject.cs` is byte-identical to its
      committed state.

      **It reads the shipped types, not copies of them.** The structs are
      `private` nested types and are reached by reflection. An oracle built on
      copies would assert that the copies match Windows and say nothing about
      what the product marshals through — the two would be free to drift, which
      is the defect being guarded against.

      **It does not ship, verified rather than asserted:** 0 CsWin32 entries in
      `src/BrowserAI/packages.lock.json`, no `win32` artifact anywhere in the
      publish output, absent from `THIRD-PARTY-NOTICES.txt` exactly as MinVer
      is, and the generated code compiles under the full analyzer stack with 0
      warnings. Suite: **396 tests, 0 failed, 0 skipped.**

      **What the oracle does NOT catch, and no reader should infer otherwise:
      access masks.** `FILE_APPEND_DATA` vs `GENERIC_WRITE` is a semantic
      choice, not a layout fact, and no size or offset assertion can see it.
      Only `ProcessLogTests.ConcurrentProcessesDoNotLoseEachOthersRecords`
      covers that.

      **⚠️ One thing flagged and deliberately NOT resolved.** CsWin32 the
      generator is MIT, but **the metadata it generates from is not**:
      `Microsoft.Windows.SDK.Win32Metadata` and `WDK.Win32Metadata` ship under
      Microsoft Windows SDK licence terms and `SDK.Win32Docs` under
      `aka.ms/WinSDKLicenseURL` — all three carry **no SPDX licence expression
      at all** on nuget.org, checked 2026-08-17. Generated code, doc comments
      included, compiles into whatever references it. **Test-only adoption
      sidesteps this entirely rather than answering it.** Whether those terms
      would create a notices obligation for *shipped* generated code is **not
      assessed, and must not be asserted either way** until it is. It becomes a
      live question the moment anyone proposes moving CsWin32 into
      `src/BrowserAI/`.

      **⚠️ A second cost, recorded because it is real:** CsWin32 pins three
      **prerelease** transitive packages — `SDK.Win32Metadata 70.0.11-preview`,
      `WDK.Win32Metadata 0.13.25-experimental`, `SDK.Win32Docs 0.1.42-alpha` —
      which are now in `tests/BrowserAI.Tests/packages.lock.json`. They are
      build-time only and nothing ships, so the README's *"GA is a hard floor"*
      rule is not violated in the artifact, but this is the only place in the
      repository where a prerelease version appears at all, and it arrived
      because CsWin32 pins it rather than because anything here chose it.

- [x] ~~**Four things only the maintainer can do.**~~ ✅ **All four done on
      2026-08-17.** Split out because a bundled row cannot be ticked.
      **Done:** the release feed is live (GitHub Releases on the now-public
      repository, `releases/latest/download/`, resolving HTTP 200 with a
      manifest whose SHA-256 matches the package byte for byte); the tag was
      cut — **`v1.0.0`**, which took the suite to **392 tests, 0 failed, 0
      skipped**; and **the `Microsoft.Windows.CsWin32` question is decided** —
      the threshold is struck and CsWin32 is adopted as a test-only layout
      oracle, recorded in full above; and **the sandbox defect is filed**, as
      [microsoft/playwright-mcp#1716](https://github.com/microsoft/playwright-mcp/issues/1716),
      after its cause was re-traced and corrected on the way out the door.

      **Nothing is blocked on the maintainer any more.** This row existed
      because four items needed an account, a public repository or a decision
      no test could make; all four are spent. What is left on the board is
      work, and every piece of it is doable without asking anyone.

- [ ] **The reclaim pass's second bullet is unbuilt, and it is the only one
      left.** [testing](plan/testing.md) asks that *"anything the previous run
      recorded is terminated by `(pid, creationFileTime)` from its own spawn
      record"*. **No spawn record is persisted across runs** — verified
      2026-08-17, nothing in `src/` or `tests/` writes one — so a run killed
      mid-test leaves a process the next run cannot identify, only a directory
      it cannot delete. The other three bullets are built and the pass is now
      itself a test, which is what makes this gap visible: the survivors are
      **named** rather than silently skipped.

## At v1 launch

- [x] ~~**Decide how the logon sweep task actually gets registered — it cannot be
      registered non-elevated on this machine.**~~ ✅ **Decided 2026-08-16 at
      [step 19](plan/build-order.md#19-velopack-package-update-roll-back): the
      task is DROPPED**, and the code, the tests and five documents went with it.
      Of the three options below, the third is taken — **BrowserAI's own startup
      sweep already covers the case that matters**, because a stray matters when
      something is about to contend for a lock and that is exactly when a client
      starts. Deleted: `src/BrowserAI/Runtime/LogonSweepTask.cs`,
      `tests/BrowserAI.Tests/LogonSweepTaskTests.cs`. Swept:
      [§C](plan/C-sessions.md#the-stray-sweep-and-the-concurrency-it-must-survive)
      (the trigger paragraph and rows R4, R5, R9),
      [step 16](plan/build-order.md#16-the-stray-sweep),
      [README](README.md), [kb: detection](kb/windows/detection.md#the-logon-sweep-task),
      [kb: Velopack](kb/packaging/velopack.md#nativeaot-hooks-and-vpk-output) and
      [rows 80–81](kb/README.md#re-verification-index). **`--sweep` is kept**: it
      has one caller left, [row 78](kb/README.md#re-verification-index), which is
      the only route to the published-AOT column of the sweep-pass table. The
      original text follows.
      <br><br>
      Measured 2026-08-16 at
      [step 16](plan/build-order.md#16-the-stray-sweep) from a medium-integrity,
      UAC-filtered administrator token: `schtasks /Create /XML` **and** the
      `Schedule.Service` COM API both answer `Access is denied` / `0x80070005`,
      in the task-library root and in a new `\BrowserAI\` folder alike, and a
      **minimal** definition — one logon trigger, one `cmd.exe` action — fails
      identically. It is machine policy rather than anything about our XML
      ([kb](kb/windows/detection.md#the-logon-sweep-task),
      [row 80](kb/README.md#re-verification-index)). Whether elevation fixes it
      is **unverified**: a UAC prompt cannot be answered from a non-interactive
      session.

      Step 16 built `LogonSweepTask` and asserts its definition; nothing
      registers it. [Step 19](plan/build-order.md#19-velopack-package-update-roll-back)
      is where it would be, and it has to choose: register during an elevated
      install and accept that a per-user install has no task; fall back to
      `HKCU\…\Run`, which the user can always write but which gives one pass at
      logon and no ten-minute re-check; or drop the second trigger and rely on
      BrowserAI's own startup sweep, which is the primary one anyway. **The
      startup sweep already covers the case that matters** — a stray matters when
      something is about to contend for a lock — so the honest question is what
      the task buys for the week in which nobody starts a client.

- [x] ~~**Decide whether `BrowserAI.exe --sweep` flashes a console window.**~~
      ✅ **Moot 2026-08-16, and withdrawn rather than answered.** The question
      only ever existed because a **Task Scheduler action** would run the binary
      under a logged-on user; with [the task dropped](#at-v1-launch) nothing
      launches `--sweep` except a person measuring
      [row 78](kb/README.md#re-verification-index) from a terminal that already
      has a console. A flash in that case is what was asked for. **It is still
      unmeasured, and that is now correct rather than owed**: there is no
      unattended caller left for it to bother. The original text follows.
      <br><br>
      BrowserAI is a console subsystem binary, and a Task Scheduler action
      running one under a logged-on user normally shows a window for the life of
      the process. The pass is ~26 ms, so it would be a flash rather than a
      window — but a flash every logon, and again ten minutes later, is the kind
      of thing a developer files a bug about. `<Hidden>` in the task definition
      hides the *task* in the UI and not the window.

- [x] ~~**Decide whether `ModelContextProtocol` and the `Microsoft.Extensions.*`
      packages owe a notice in the artifact too.**~~ ✅ **Decided 2026-08-16 at
      the plan's final audit: they do, and they ship.** The reasoning that put
      Velopack's MIT text in `THIRD-PARTY-NOTICES.txt` applies to both without a
      word changed — same mechanism, same absence — and Apache-2.0 §4(a) is the
      stricter of the two clauses rather than the looser. The product is
      publicly distributed now, which is what turned a defensible deferral into
      one that had to be closed. [`PRE-RELEASE.md`](PRE-RELEASE.md)
      item 13 now names **six** obligations with a `Corrected` note carrying its
      previous text, `README.md` and
      [`kb/packaging/dependencies.md`](kb/packaging/dependencies.md) carry the
      same correction, and `ThirdPartyNoticeTests` grew three assertions.
      **Two things measurement contradicted while closing it, both recorded
      where they belong:** upstream's MCP `LICENSE` is **12,227 bytes and grants
      three licences**, not one — Apache-2.0, MIT for contributions whose
      authors never consented to relicensing, and CC-BY-4.0 for documentation —
      so reproducing the Apache half alone would have dropped terms that cover
      part of the code; and that Apache half **ends at `END OF TERMS AND
      CONDITIONS` and omits the appendix its own §4 points at**, which is
      upstream's file as published and is reproduced unaltered rather than
      completed from the canonical text. The count below was also wrong:
      **seventeen** `Microsoft.Extensions.*` packages are in the closure, not
      four, under **two** different `.NET Foundation` copyright lines because
      they come from two repositories — so the list is derived from
      `src/BrowserAI/packages.lock.json` by the test rather than typed, and a
      package entering the closure on a later bump is a red build. The original
      text follows.
      <br><br>
      Raised 2026-08-16 while
      closing item 13's two missing notices, and **deliberately not decided
      there**: the checklist names four obligations and shipping a fifth on my
      own reading would be changing a settled judgement without anyone deciding
      it. The question is real, though. `ModelContextProtocol` is **Apache-2.0**
      and is compiled into `BrowserAI.exe` exactly as Velopack is; §4(a) says a
      redistributor *"must give any other recipients of the Work or Derivative
      Works a copy of this License"*, and no copy travels — the reasoning that
      put Velopack's MIT text into `THIRD-PARTY-NOTICES.txt` applies to it
      unchanged. Four `Microsoft.Extensions.*` assemblies are linked in under
      MIT with the same notice clause.
      [README → Third-party components](README.md#third-party-components) states
      that row's obligation as *"mid-transition from MIT; unrelicensed
      contributions remain MIT. Vendored fixture files keep their upstream
      headers"*, which answers a different question — it is about relicensing,
      not about whether a licence copy ships. **Settle it, then either add the
      rows to `ThirdPartyNoticeTests.Obligations` and the text to the notices
      file, or write down why linked-in NuGet packages are treated differently
      from Velopack.** The mechanism is already data-driven, so the change is a
      row and a paragraph.

- [ ] **Widen the invisible-source check beyond `*.cs`, or decide not to.**
      Step 14 was bitten by the template's unanchored `artifacts/` rule matching
      `src/BrowserAI/Artifacts/` on case-insensitive Windows: five product
      source files ignored, while `dotnet build`, the suite and
      `git status --porcelain` all read green.
      `BuildConfigurationTests.NoSourceFileIsInvisibleToGit` now closes it —
      **for `.cs` files under `src/` and `tests/`.**

      **Nineteen unanchored directory rules remain** in the upstream half,
      swept 2026-08-16: `[Dd]ebug/`, `[Rr]elease/`, `[Rr]eleases/`, `[Oo]ut/`,
      `[Ll]og/`, `[Ll]ogs/`, `[Oo]bj/`, `bld/`, `[Ww][Ii][Nn]32/`, the three
      `[Aa][Rr][Mm]` forms, `Generated Files/`, `[Tt]est[Rr]esult*/`,
      `[Dd]ebugPS/`, `[Rr]eleasePS/`, `BenchmarkDotNet.Artifacts/`, `ipch/`,
      `_ReSharper*/`. A source folder named `Logs\`, `Out\` or `Release\`
      would be swallowed exactly as `Artifacts\` was — and a folder holding
      only data would not be caught, because the check keys on `.cs`.

      The honest options are to widen the check to *any* file under `src/` or
      `tests/` that git ignores and that is not under `obj\` or `bin\` — the
      query returns nothing today, so it would land green — or to decide the
      `.cs` scope is enough because a source folder that contains no source is
      not a source folder. **Decide it rather than leaving it implied**, and do
      it while the reasoning is still on the page: this rule has now cost the
      project once, and it was predicted below before it did.

- [ ] **Make the marker entry adjudicate what moved — at the first real bump,
      not before.** [The gate](plan/testing.md#what-the-marker-records) requires
      each `upstream-review.json` entry to gain `snapshots` (per snapshot,
      `unchanged` or an adjudication) and `reverification` (an outcome for every
      *manual* row, by name), with a test asserting the entry is consistent with
      what the build observed. Build-order step 4 built everything else in that
      section and deliberately not this. **At a baseline there is nothing to
      adjudicate**: satisfying such a test today means typing an adjudication of
      no change for four snapshots and an outcome for roughly forty manual rows,
      most of which name code that does not exist before
      [step 12](plan/build-order.md#12-the-session-tools-and-config-generation) —
      a review that did not happen, written to make a suite green, which is the
      one act [the procedure](UPSTREAM-REVIEW.md) exists to forbid. Do it on the
      first bump, when there is something true to write; the marker test fires
      on exactly that event, so nothing is relying on anyone remembering.

- [x] ~~**Make `dotnet test` run the tests again.**~~ ✅ **Withdrawn 2026-08-16,
      the same day it was raised — there was never anything to fix.** The
      finding behind it was a single transient observation written up as a
      standing property of the toolchain. It does not reproduce:
      `dotnet test BrowserAI.slnx` returns **51 passed, exit 0** at `e5f4684`,
      and a fresh `git worktree --detach` of **`b8a6553`** — the exact commit the
      entry named as its proof that *"it is not ours"* — returns **30 passed,
      exit 0**. The claimed consequence was false too: steps 1 and 2 were
      evidenced with `dotnet test` reporting 5 and then 13 passing tests, so it
      is not the case that every done-test's evidence came from the executable.
      Retained struck through rather than deleted, because
      [kb row 54](kb/README.md#re-verification-index) and
      [the kb entry](kb/windows/processes.md#interop-and-the-toolchain) are
      retracted in place for the same reason: a reader who saw the original must
      be able to find the retraction.

- [x] **Answer a frame that fails to parse, instead of only logging it.** ✅ Done
      2026-08-16 at [step 9](plan/build-order.md#9-lossless-passthrough), as
      planned. `JsonLines.TryRecoverRequestId` scans a frame that failed to parse
      for a top-level `id`, left to right, stopping at the first thing it cannot
      read — which succeeds on the common case, because every well-behaved
      encoder writes the id near the front. `JsonLinesTransport` answers
      `-32700` when one is recovered and drops-and-logs when none is, because
      inventing an id would resolve a request the caller never made. **Only on
      the caller's leg:** a response is not a thing one answers, so a `-32700`
      aimed at a child would name a request it has no record of. Written from
      the SDK's *behaviour*, not its code — it is Apache-2.0 and this repository
      is not — and the one difference worth noting is that ours needs
      `MaxDepth = int.MaxValue` for the same reason theirs does, which is a
      property of the problem rather than of their solution. Proven by
      `LosslessPassthroughTests.AFrameThatFailsToParseIsAnsweredRatherThanOnlyLogged`
      and `…WithNoRecoverableIdIsDroppedLoudlyRatherThanAnswered`. The message
      text is transport-level and deliberately not catalogue prose; §H.4 is
      [step 13](plan/build-order.md)'s.

- [ ] **Decide whether relayed notifications need their order preserved.** Found
      2026-08-16 at [step 9](plan/build-order.md#9-lossless-passthrough) and
      recorded rather than fixed. The child→caller progress relay preserves the
      `progressToken` and the params byte for byte, and **does not preserve
      order**: the SDK's message loop dispatches inbound notifications
      fire-and-forget, and two `notifications/progress` written by the double in
      order were observed reaching the caller as 2 then 1
      ([kb](kb/mcp/sdk.md#added-2026-08-16--lossless-passthrough-at-220),
      [row 63a](kb/README.md#re-verification-index)). **It cannot be fixed from a
      notification handler** — the reordering has already happened by the time
      the handler runs — so a fix means the `ITransport` decorator
      [deviation 7](plan/stack.md#nine-places-where-the-sdk-must-be-deviated-from)
      originally described, which sees messages in wire order. Two things to
      settle before writing it: whether `@playwright/mcp` emits progress at all
      (not measured), and whether a caller that renders a jumping progress value
      is a defect worth a component. Step 9's done-test does not ask for
      ordering, and claiming it without asserting it would have been the
      [step-8 lesson](plan/build-order.md#8-the-harness-and-the-fake-child)
      repeated.

- [ ] **Find out why `dotnet test` runs zero tests, or record that it stays
      that way.** Measured 2026-08-16 at
      [step 9](plan/build-order.md#9-lossless-passthrough), **five ways**:
      `dotnet test` reports *"Zero tests ran"*, exit 5, against the solution and
      the project, at the working tree, at `c9d30d4`, **and in a fresh worktree
      of `b8a6553`** — the commit that returned 30 passed hours earlier. The
      same assembly run as `BrowserAI.Tests.exe` runs all 106. Versions are
      identical either side (SDK 10.0.302, .NET 10.0.11, TUnit 1.65.0, MTP 2.3.3
      from the committed lock), so **nothing in this repository and no package
      float explains it**.

      > ⚠️ **Corrected 2026-08-16 (previously: "it is now stable where it was
      > previously transient … the machine moved", and "from both shells").**
      > **The discriminator is which shell issues the command, not time and not
      > the machine.** Minutes after that was written, the same command was run
      > three times from the **root session's** shell against the same commit:
      > 106 passed / exit 0, `Discovered 106 tests`, 106 passed / exit 0 — and
      > again at 215 and 239 tests since. Every zero-test observation on record
      > was made inside a **sub-agent's** shell, by two different agents hours
      > apart, including both times a clean worktree of `b8a6553` was cited as
      > proof. Both sets of measurements are real; the generalisation was not.
      > This correction reached
      > [the kb entry](kb/windows/processes.md#interop-and-the-toolchain) in
      > `c299cab` and **missed this item**, which is its own small instance of
      > the same lesson: a correction that does not sweep every place the claim
      > was written is a correction that half happened.

      MTP's own diagnostic shows the host
      launched with `--server dotnettestcli --dotnet-test-pipe …` and the log
      ending immediately after startup configuration, which points at the
      `dotnet test` ↔ MTP handshake rather than at discovery
      ([kb](kb/windows/processes.md#interop-and-the-toolchain)). **Do not "fix"
      it by removing the MTP runner entry from `global.json`** — TUnit is
      MTP-only and there is no VSTest path to fall back to. Until it is
      understood, done-test evidence comes from the executable and the step that
      relies on it says so.

- [x] ~~**Capture ILC's raw output and fail the publish if it is non-empty.**~~
      ✅ **Done 2026-08-16 at
      [step 19](plan/build-order.md#19-velopack-package-update-roll-back)**, in
      the release script this item was waiting for:
      `build/New-Release.ps1` publishes with `-v:normal`, tees the whole log to
      `.work/release-publish.log`, and refuses on `will always throw`, on
      `(warning|error) IL[0-9]{4}`, or on an AOT/trim analysis warning. Measured
      clean on the first real release: **379 lines read, 0 complaints.**

      > ⚠️ **The obvious pattern fails every publish, and it took a run to find
      > out.** Keying on `\bIL[0-9]{4}\b` matches csc's own command line, which
      > at `-v:normal` carries `/nowarn:1701,1702,NU5105,IL2121,...` — so the
      > check matched a **suppression list** and refused a clean build. The
      > severity word is what makes a match a diagnostic
      > ([kb](kb/packaging/velopack.md#the-ilc-output-check-needs-the-severity-word-not-the-code)).

      > ⚠️ **`Corrected 2026-08-17`: the sentence below credits the wrong
      > property.** It says `ILLinkTreatWarningsAsErrors=true` is what makes an
      > `IL2xxx`/`IL3xxx` warning fail the publish. It is not. Measured on SDK
      > 10.0.400 / ILC 10.0.11 across five variants, that property had **no
      > observable effect at all** — with it set alone the publish emitted 14
      > ILC warnings and **exited 0** with a working binary, identical to
      > setting nothing. `TreatWarningsAsErrors`, in `Directory.Build.props`,
      > is both necessary and sufficient, and BrowserAI sets it, so **the
      > outcome described below is real and nothing is broken** — only the
      > attribution was wrong.

      Original text follows.
      <br><br>
      Build-order step 1 asked for two things and only one of them is a property.
      The property half landed 2026-08-16: `SuppressTrimAnalysisWarnings=false`,
      `TrimmerSingleWarn=false` and `ILLinkTreatWarningsAsErrors=true` in
      `src/BrowserAI/BrowserAI.csproj`, so any `IL2xxx`/`IL3xxx` **warning**
      fails the publish. **That does not cover the case the requirement was
      written for.** ILC reports an always-throwing method as neither a warning
      nor an error — [kb: SDK](kb/mcp/sdk.md#added-2026-08-16--not-part-of-the-2026-08-15-spike)
      records a publish that exited 0, emitted zero warnings, produced an
      artifact, and printed `Method '...' will always throw because: Failed to
      load assembly '...'`. No MSBuild property catches that, because it is not a
      diagnostic; only reading ILC's console output does. The check therefore
      needs a publish wrapper, and there is no build script yet to hang it on —
      so it lands with [step 19](plan/build-order.md), where packaging first
      needs one. Until then
      [re-verification row 27](kb/README.md#re-verification-index) carries it as
      *manual*, which is accurate rather than reassuring.

- [x] **Write `CHANGELOG.md`. The release refuses without it.** ✅ Done
      2026-08-16 at [step 18](plan/build-order.md#18-versions-from-git-tags-and-the-changelog).
      Format is [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) with
      `### Added` / `### Fixed` / `### Changed` subheads, which is the house form
      (SpawnSpotter, FluxTone, HitsterCardGenerator, DownloadDeleter); a section
      is headed by the **bare bracketed version** — `## [0.1.0] - 2026-08-16` —
      because the tag carries the `v` and the heading is composed from a version
      string that has none. The mechanism is OutlookAI's, the only mechanised
      changelog in the estate, ported to `build/Get-ReleaseNotes.ps1`: extract
      the `## [Unreleased]` section by regex, **refuse on empty with a real
      error**, then stamp the version being cut below the heading. **Empty means
      no list items**, not no characters — a section holding only its subheads is
      exactly what a changelog nobody wrote looks like. Entries were written from
      the work as it landed rather than reconstructed: `[Unreleased]` describes
      step 18 and `[0.1.0]` describes what the tag contains. Six tests in
      `tests/BrowserAI.Tests/ChangelogTests.cs` drive the script both ways,
      including that a refused version leaves the file byte-identical and that a
      stamped release leaves an empty section behind, so the next one must be
      written too.

- [x] **Decide how a git tag becomes a version string.** ✅ Done 2026-08-16 at
      [step 18](plan/build-order.md#18-versions-from-git-tags-and-the-changelog).
      The mechanism is **MinVer**, taken from `SixFive7/SpawnSpotter` — the only
      repository in the estate that derives its version from tags, and the closest
      structural match. It resolved to **7.0.0** through the float, on the product
      project only, with `MinVerTagPrefix` of `v`. Measured on the artifact rather
      than read from documentation: at the annotated tag **`v0.1.0` the build
      stamps `0.1.0`**, and **five commits later with no new tag it stamps
      `0.1.1-alpha.0.5`** — the shape `vpk` accepts, and the untagged suffix that
      makes *never self-update from a build that is not a release* readable off
      the version string. With no reachable tag MinVer produced
      `0.0.0-alpha.0.71` and the build **refused it**, naming `fetch-depth: 0` as
      the remedy, which is [kb](kb/packaging/velopack.md#versions-from-git-tags--minver-700--2026-08-16)
      and `BuildVersionTests`. Two traps came with it and are closed:
      `AssemblyVersion` is `{Major}.0.0.0` by design, so nothing reads it (the
      product's own `SessionLock` did, and would have stamped `0.0.0.0` into every
      `lock.json` of the 0.x line); and the SDK's `SourceRevisionId` decoration is
      off repository-wide.

- [x] ~~**Check the *published* binary's version string, in the release script that
      does not exist yet.**~~ ✅ **Done 2026-08-16 at
      [step 19](plan/build-order.md#19-velopack-package-update-roll-back)** —
      the release script exists now, and it sweeps the whole linked binary for
      `<core>+<sha>` in both the UTF-16 and ASCII readings.

      > ⚠️ **Corrected as it was written: FrameLink's rule as stated below —
      > *fail the build on ANY decorated string* — can never go green in this
      > repository.** The first publish carried **six**, and **not one of them
      > was ours**: `1.2.0+f2edcbc` (Velopack), `2.2.0+6fa3825…`
      > (ModelContextProtocol) and four `Microsoft.Extensions.*` at 10.0.10,
      > 10.0.11 and 10.8.3 — each decorated by its own publisher's SourceLink and
      > linked in by ILC. That rule is only sound for a binary with no
      > third-party dependency carrying one, which this is not and will not
      > become. **The sound version is narrower and is still a sweep:** fail on a
      > decorated string whose version *core* is the version being packed. That
      > is ours — the entry assembly, or a referenced project of ours sharing the
      > derived version — and it is the only string that can reach the feed
      > comparison, because the updater matches `BuildVersion.Current` against
      > the served version. A third-party decoration is inert
      > ([kb](kb/packaging/velopack.md#the-framelink-version-string-sweep-is-too-broad-to-use-as-written)).

      Original text follows.
      <br><br>
      Decided 2026-08-16 at
      [step 18](plan/build-order.md#18-versions-from-git-tags-and-the-changelog),
      and carried here rather than built, because there is nothing to hang it on:
      `build/` has a payload script and a snapshot script, and no release script.
      `SixFive7/FrameLink` greps **every** version string in the linked binary and
      fails the build on a decorated one (`build.sh`, exit 7), which is a stronger
      check than this repository's, because a referenced project carrying a
      decorated string is linked into the same AOT binary and nothing else would
      say so. BrowserAI's guard is one layer weaker on purpose: the property is
      set repository-wide, `BuildVersionTests` asserts the **shipped attribute**
      carries no build metadata, and `ThePublishedBinaryReportsADerivedVersionOverTheWire`
      asks the published binary itself over the wire. What is missing is the
      sweep over strings the entry assembly did not contribute. It belongs beside
      the `will always throw` grep of the ILC output, which is owed to the same
      absent script and is already carried above — one publish wrapper answers
      both.

- [x] **Review `.gitignore`.** ✅ Done 2026-08-15, ahead of v1, because the
      Velopack spike produced a real `vpk pack` to check the guesses against.
      Fixed: bare `Setup.exe` never matched (the real name carries the app id and
      channel) → `*-Setup.exe`; added the `releases.*.json` / `assets.*.json` feed
      manifests, which were missing entirely; corrected the payload comment, which
      still described a ~806 MB tree with browsers in it; and `Verify.XunitV3` →
      `Verify.TUnit`, left over from before the framework decision. Kept
      `/payload/`, `/staging/`, `/.staging/` deliberately — vpk emits none of
      them, but the build will need somewhere to assemble a payload, so they are
      reviewed rather than deleted on the theory that they are unused.

      **Still owed at v1:** re-fetch the upstream `VisualStudio.gitignore` half
      wholesale, and settle `.vscode/mcp.json` (upstream PR
      [#4735](https://github.com/github/gitignore/pull/4735), open since
      2025-09-23; if unmerged, add `!.vscode/mcp.json` below the marker). Original
      note follows.

- [ ] ~~**Review `.gitignore`.**~~ *(largely discharged above; the upstream-half
      refresh and the `.vscode/mcp.json` decision remain.)* It was written before any code existed, so the
      project-specific half is predictive rather than observed. Four things to
      check:

    - **Refresh the upstream half wholesale.** Lines 1–429 are
      `github/gitignore`'s `VisualStudio.gitignore` verbatim — blob
      `d5a18deed8813c6c817c9090bf0443d7fad48a9d`, verified identical to upstream
      `main` on 2026-08-14, last changed upstream 2026-04-17 (PR #4269).
      Everything below the marker comment is ours. Re-fetch and replace the top
      half in one paste; never merge it by hand.

    - **`.vscode/mcp.json` is currently ignored**, by the template's `.vscode/*`
      rule, which un-ignores only `settings.json`, `tasks.json`, `launch.json`,
      `extensions.json` and `*.code-snippets`. For a project that *is* an MCP
      server, a workspace registration used for testing would be silently
      untracked. Upstream PR
      [#4735](https://github.com/github/gitignore/pull/4735) proposes fixing
      this and has been open since 2025-09-23; if it has not merged by v1, add
      `!.vscode/mcp.json` below the marker. Note `.mcp.json` at the repository
      root — the file the current setup actually uses — is tracked and
      unaffected.

    - **Replace the guessed paths with the real ones.** `/payload/`,
      `/staging/`, `/.staging/`, `/Releases/`, `/RELEASES`, `Setup.exe` and
      `*-Portable.zip` were inferred from the README's install layout, not
      observed from a build. Check what the build and `vpk` actually emit, keep
      what matches, delete the rest. **Payload half done 2026-08-16** with
      build-order step 3: `/payload/` is real and its layout is now written into
      `.gitignore`; `.links/` matches nothing because playwright-core writes it
      into the browsers root, and is kept only as a backstop; the
      `!/mcp/node_modules/` negation was pointed at a path that does not exist
      and now names `/payload/mcp/node_modules/`. **Still owed:** `/staging/`
      and `/.staging/`, which nothing emits, and the Velopack half, which needs
      [step 19](plan/build-order.md).

    - **Settle the two deferred decisions**, both written into the file as
      comments. First, whether the pinned `@playwright/mcp` tree is committed
      for reproducibility rather than installed into staging at build time — the
      `!/mcp/node_modules/` negation is written out ready to uncomment.

      **The second is settled, and it happened rather than being decided.**
      Build-order step 14 created `src/BrowserAI/Artifacts/`, the template's
      unanchored `artifacts/` rule matched it on case-insensitive Windows, and
      **five product source files were ignored while the build, the suite and
      `git status --porcelain` all read green** — which is
      [the founding failure class](plan/build-order.md#every-done-test-ends-with-a-clean-working-tree)
      applied to a repository, and precisely the shape this note predicted. Both
      rules are now root-anchored (`/artifacts/`, `/.artifacts/`), where the SDK
      actually puts that folder, and the prediction is now a test:
      `BuildConfigurationTests.NoSourceFileIsInvisibleToGit` lists every `.cs`
      under `src/` and `tests/` against `git ls-files`, so an ignore rule that
      swallows source is red rather than silent. Planted and reverted
      2026-08-16: with the unanchored rule back, it fails naming all five
      files.

- [x] ~~**Set `userDataDir`, so a run's browser profile stops landing in a
      directory BrowserAI does not own.**~~ ✅ **Done at
      [step 12](plan/build-order.md#12-the-session-tools-and-config-generation),
      2026-08-16.** Every generated config now carries one: a session's is
      `<session-dir>\profile\`, and the run's own child gets one under its
      instance directory — so *nothing* the product starts can fall back.
      **The pile had grown from the 27 profiles / 193 MB recorded below to 159 /
      877 MB** by the time the key landed. Deleted once, then the whole suite run
      twice: `%LOCALAPPDATA%\ms-playwright-mcp\` stayed absent both times. The
      first attempt at that check found **one** directory recreated, by
      `SandboxFlagTests.TheConfigKeyIsStillDiscardedByUpstream`, which writes its
      own config to exercise upstream's handling of `chromiumSandbox`; it carries
      a `userDataDir` now for the same reason the product's does. The original
      text follows.
      <br><br>
      Build-order step 7 generates only the
      keys that decide *which browser runs*, so no `userDataDir` is set and
      upstream falls back to its own default: one
      `%LOCALAPPDATA%\ms-playwright-mcp\mcp-chrome-for-testing-<hash>\` profile
      per distinct configuration, never cleaned up. **Measured 2026-08-16 on this
      machine: 27 profiles, 193 MB**, some of them spike leftovers and several
      added by that step's own suite. It is not a defect in step 7 — the
      directory is the *session's* at
      [§C](plan/C-sessions.md#the-session-directory-is-the-identity) and the key
      belongs to the full generator at
      [step 12](plan/build-order.md#12-the-session-tools-and-config-generation) —
      but between now and then every suite run adds to the pile, so it is
      recorded rather than left to be discovered. **Note the constraint that
      comes with it:** `validateBrowserConfig` throws on `isolated` together with
      `userDataDir`, so the two can never both be set. Nothing existing needs
      deleting before step 12; whether to sweep the 193 MB is the maintainer's
      call, since it is outside the repository and partly predates this work.

- [x] ~~**Call `SessionIndex.Record` from `init` and from `resume`. Nothing calls
      it today.**~~ ✅ **Done at
      [step 12](plan/build-order.md#12-the-session-tools-and-config-generation),
      2026-08-16.** `SessionManager.OpenAsync` is the one path both tools reach,
      and it calls `Record` on every open, idempotently. `Forget` was added
      alongside it and is called by `browserai_destroy`: a sweep would remove the
      entry anyway, so this only decides *when* — and a destroyed session
      lingering in the only inventory there is would be a confident wrong answer.
      `SessionToolTests.ListReportsWhatIsUnderAPathAndNothingElse` is what proves
      the wiring, because `browserai_list` reads the index and nothing else. The
      original text follows.
      <br><br>
      [Build-order step 11](plan/build-order.md#11-the-session-index)
      built the index and deliberately wired it to nothing: `browserai_init`,
      `browserai_resume` and the sweep are
      [steps 12](plan/build-order.md#12-the-session-tools-and-config-generation)
      and [16](plan/build-order.md#16-the-stray-sweep), and a store called from a
      layer that does not exist would have to be called from somewhere that does
      — which means either `SessionLock`, coupling the lock layer to the
      app-paths seam for a fact it has no use for, or a call site invented for
      the test. So the store is **proven and unwired**, and the suite exercises
      it directly with the real `SessionLock` driving the `Acquired`/`Reclaimed`
      pair that stands in for init and resume. **The cost is one sentence and it
      is real: today no product code path writes an index entry, so a session
      created by the product would not be listed by `browserai_list` if that tool
      existed.** Step 12 must call `Record` on both paths — every `init` *and*
      every `resume`, idempotently, which is
      [§D](plan/D-locking.md#the-session-index-on-disk)'s first property and the
      whole reason a lost entry self-heals. Step 16 must call `Sweep`.

- [x] ~~**Create the per-session log file, `<session-dir>\browserai.log`.**~~
      ✅ **Done at
      [step 12](plan/build-order.md#12-the-session-tools-and-config-generation),
      2026-08-16.** `src/BrowserAI/Logging/SessionLogFile.cs` writes it with the
      same `FILE_APPEND_DATA` open the process log uses, and `ProcessLog.
      OpenSessionLog` builds a per-session `ILoggerFactory` over **three**
      destinations — that file, the machine-wide process log (non-owning, or the
      first session to end would close the shared handle) and stderr. `debug` on
      `init` or `resume` sets that factory's minimum level and nothing else's,
      which is the whole point of the argument.
      <br><br>
      **The log is opened *before* the lock is taken, and that ordering is the
      finding.** The first version acquired first and passed the process logger
      to `SessionLock`, so the session's own file began mid-story with no
      acquisition in it — caught by
      `SessionToolTests.ASessionWritesItsOwnLogBesideItsLockFile`, which requires
      `Session lock acquired` and the `{session=…}` scope to be in the file
      itself. The same fix is what puts the moved-directory line where somebody
      looking into that directory will find it.
      <br><br>
      **It also contradicts [§C](plan/C-sessions.md#the-session-directory-is-the-identity),
      which says `lock.json` is "the only file at the root".** It is now one of
      two; §C is corrected rather than the file moved, because §E names the path
      and the reason for a flat root is that the file that matters is
      unmissable — which two files still satisfy and a subfolder would not. The
      original text follows.
      <br><br>
      [§E](plan/E-lifecycle.md) puts it at the session root beside `lock.json`,
      holding *anything a session did*, so `browserai_destroy` removes it with
      everything else. [Build-order step 2](plan/build-order.md#2-stdout-is-owned-and-nothing-else-can-reach-it)
      deferred it to [step 10](plan/build-order.md#10-the-session-directory-lockjson-and-the-three-lock-scopes),
      and step 10 **deliberately did not build it**: at that step a session does
      exactly one thing — it gets locked — so a file created by the lock layer
      and written by nothing would be a mechanism that only looks like one, which
      is the same call step 2 made about a no-op `Flush()`. What step 10 *did*
      build is the half that is real today: `SessionLock` pushes a logging scope,
      so every record written while a lock is held carries
      `{session=<path>}` — asserted to appear **exactly once** per line,
      because two providers share one external scope provider and a naive wiring
      would push it twice. Do the file itself at
      [step 12](plan/build-order.md#12-the-session-tools-and-config-generation),
      when there is a session lifetime to log into it, and note that §E's routing
      is *by whether a session exists* rather than by which factory a caller
      happens to hold — so the seam is a router over the two destinations, not a
      second `ILoggerFactory` for callers to choose between.

- [x] ~~**Give `AnOversizedPayloadArrivesByteIdentical` a reason for taking two
      minutes, or make it stop.**~~ ✅ **Closed 2026-08-16 at
      [step 20](plan/build-order.md#20-the-first-release) — by measurement, not
      by work.** ⚠️ **Corrected (previously: "timed alone, on an otherwise idle
      machine, it is 1 m 59 s, and it dominates the whole suite's 2 m 15 s").**
      Re-measured from the TRX of a full run: **105 ms**, in a suite of **33 s**.
      Three whole-suite runs at 32.978 / 38.314 / 33.741 s are independently
      incompatible with any single test taking 119 s, so this is not one lucky
      run. What changed in between is not established and is deliberately not
      guessed at — the suite gained a `ParallelLimiter` capped at 4 the same day
      ([`SuiteParallelism`](tests/BrowserAI.Tests/SuiteParallelism.cs)), and the
      original 1 m 59 s was taken under the unconstrained parallelism that also
      produced a 56.5 s run with 20 timing failures. **The advice in the original
      item still stands for whoever meets a slow test next: time it at several
      body sizes before changing anything, because a fixed cost and a quadratic
      one look identical at one data point.** Original text follows.
      <br><br>
      Noticed 2026-08-16 while running the suite for
      [step 10](plan/build-order.md#10-the-session-directory-lockjson-and-the-three-lock-scopes),
      and **it is not step 10's doing** — timed alone, on an otherwise idle
      machine, it is 1 m 59 s, and it dominates the whole suite's 2 m 15 s. The
      test is a 2 MiB body through the in-process rig, which
      [step 8](plan/build-order.md#8-the-harness-and-the-fake-child) built
      specifically so that this layer runs "in milliseconds, in parallel", so
      either something in the pipe hop is quadratic or the payload is larger than
      the point being made needs. **Not investigated**, deliberately: it is green
      and it is out of step 10's scope. Whoever picks it up should time it at
      several body sizes before changing anything — a fixed cost and a quadratic
      one look identical at one data point.

- [x] ~~**Ship Velopack's MIT notice and a trademark disclaimer inside the
      package, and make both a test.**~~ ✅ **Done 2026-08-16**, the same day it
      was raised. `THIRD-PARTY-NOTICES.txt` at the repository root carries both:
      the trademark disclaimer in
      [README → Third-party components](README.md#third-party-components)'s own
      terms, and Velopack's whole MIT licence **copied rather than transcribed**
      from the commit `velopack.nuspec` records as the source of the resolved
      package (`f2edcbca`, fetched 2026-08-16 against Velopack 1.2.0). The
      `AddNoticesToPublish` target in `src/BrowserAI/BrowserAI.csproj` publishes
      it unconditionally — unlike the payload, whose absence is a real state.
      <br><br>
      **`ThirdPartyNoticeTests` asserts all four obligations against three
      subjects**, because each can be right while the next is wrong: the
      repository's own file, the publish output `vpk` is handed, and the entry
      list of the packed `.nupkg` under `lib/app/`. The set is **data**, so a
      fifth obligation is a red build rather than a discovery at the next
      release. And the Velopack version stamped in the notices is asserted
      against `src/BrowserAI/packages.lock.json`, so a bump is red until the
      licence has been re-fetched — a licence text is a measurement and
      everything here floats. Proven against a real `vpk pack`:
      `Releases/BrowserAI-0.1.2-full.nupkg`, all six notice paths present.
      The original text follows.
      <br><br>
      Found 2026-08-16 by the first run of
      [the pre-release checklist](PRE-RELEASE.md), reading item 13 against
      the packaged `.nupkg` rather than against the source tree. Two of the four
      obligations are **absent from the artifact**: Velopack is a NuGet
      dependency, so its licence stays in the package cache and is never copied
      to the publish output; and no file anywhere carries the trademark
      disclaimer Apache-2.0 §6 makes necessary, since the inherited `browser_*`
      names surface upstream branding directly in BrowserAI's own API. The other
      two are present and correct — Node's full 160,552-byte `LICENSE` and the
      intact `node_modules` tree with `@playwright/mcp`'s and `playwright-core`'s
      Apache-2.0 notices. **These attach at first installer handoff**, so this
      blocks a release rather than a v1. Make it a test over the produced
      `.nupkg`'s entry list: nothing else looks, which is exactly why it survived
      to a release gate.

- [x] ~~**Emit the resolved-set manifest from `build/New-Release.ps1`.**~~
      ✅ **Done 2026-08-16**, the same day. `build/Write-ReleaseManifest.ps1`
      copies the six files and writes `manifest.json` beside them — the version,
      the tag from `git describe --tags --long`, the package's SHA-256, and the
      resolved version **read back out of each copy** rather than typed.
      `New-Release.ps1` calls it as step 8 and returns the path as
      `ResolvedSet`. In its own script for the same reason
      `Test-ReleaseVersion.ps1` is: **so the suite can drive it**, which
      `ReleaseScriptTests` does both ways, including that a missing file refuses
      rather than writing a partial.
      <br><br>
      ⚠️ **The first real run failed where the test passed, and the fixture was
      the reason.** `package-lock.json` records the root project under the
      **empty-string key**, and PowerShell's `ConvertFrom-Json` refuses an empty
      property name without `-AsHashtable`. The synthetic lock the suite drove
      it with had no root entry — a fixture simpler than the real input, which
      is the shape of test that proves nothing. Both are fixed: the script reads
      that lock as a hashtable, and the fixture now carries the `""` key.
      Emitted for real at
      `Releases/archive/BrowserAI-0.1.2-manifest/` (6 files + `manifest.json`,
      2,357 b), stating ModelContextProtocol 2.2.0 · Velopack 1.2.0 · MinVer
      7.0.0 · TUnit 1.65.0 · `@playwright/mcp` 0.0.79 · `playwright-core`
      1.63.0-alpha-2026-08-05 · node v24.19.0 · chromium 1237 · firefox 1539.
      The original text follows.
      <br><br>
      Found 2026-08-16 by the same run: [item 11](PRE-RELEASE.md) requires the
      resolved set recorded beside the artifact, and **nothing produces one** —
      the item named a file that has never existed, so it could be neither
      satisfied nor failed. The script already knows the version, the sizes and
      the archived path and writes none of it down. Copy the six files the item
      now names (three `packages.lock.json`, `build/payload/package-lock.json`,
      `payload/payload.json`, `upstream-snapshots/browsers.json`) beside the
      archived `.nupkg`, plus the derived version and its tag. Assembled by hand
      once, at `.work/step20/manifest/`; **a hand-assembled manifest is one
      nobody assembles twice**, which is the whole reason this is an item.

- [x] ~~**Assert `UseSystemResourceKeys` is unset.**~~ ✅ **Done 2026-08-16**,
      the same day.
      `BuildConfigurationTests.UseSystemResourceKeysIsExplicitlyFalseEverywhereItAppears`
      reads every build file, refuses any value other than `false` wherever it
      appears, **and requires the declaration to be present in
      `Directory.Build.props`**. The second half is the load-bearing one: the
      framework default is already off, so a file that never mentions the
      property passes a "not true" check while telling the next reader nothing
      — and it is the deletion, not a `true`, that this was written to catch.
      The original text follows.
      <br><br>
      [Testing](plan/testing.md#what-the-build-itself-must-fail-on) requires it
      in those words — *"Assert the property is unset, so it cannot arrive later
      as somebody's size optimisation"* — and `grep -rn "ResourceKeys" tests/`
      returns nothing. Found 2026-08-16 at
      [step 20](plan/build-order.md#20-the-first-release), while looking for the
      evidence [item 7](PRE-RELEASE.md) asks for. The property is correctly
      `false` at `Directory.Build.props:160`; what is missing is the thing that
      keeps it that way. It strips the framework's exception message strings,
      which is a few kilobytes against a ~117 MB payload and an
      [error catalogue](plan/H-model-surface.md#h4-the-error-catalogue) silently
      emptied. `BuildConfigurationTests` is where it goes.

- [x] ~~**Make a degraded smoke run a red build at release time.**~~
      ✅ **Done 2026-08-16**, the same day, and the measurement that opened it
      also narrowed it. `tests/BrowserAI.Tests/Harness/SuiteEnvironment.cs` is
      the single gate all **thirty-five** guards across thirteen files now route
      through — thirty-three early returns plus two positive-form arms in
      `FirefoxTests` that step 20's count missed. Three behaviours: an ordinary
      run reports **skipped** rather than passed, so the run's own summary
      carries a count a healthy run does not; a run with
      `BROWSERAI_RELEASE_RUN=1` **fails**, naming the command that produces what
      is missing; and a **partial** installation — a publish directory with no
      binary in it — fails in either mode, which is the old per-site
      `IsAbsentAsAWhole` assertion kept and centralised. Every run ends with a
      coverage block naming each capability `PRESENT`/`ABSENT`/`PARTIAL` and
      every test that degraded, on stdout and at `.work/suite-coverage.txt`.
      <br><br>
      **Measured before and after, by moving things aside rather than by
      reasoning:**
      <br><br>

      | Run | total | passed | failed | skipped | exit |
      |---|---|---|---|---|---|
      | before, healthy | 329 | 328 | 0 | 1 | 0 |
      | **before, publish moved aside** | 329 | 328 | 0 | **1** | **0** |
      | after, healthy | 341 | 340 | 0 | 1 | 0 |
      | **after, publish moved aside** | 341 | 314 | 0 | **27** | 0 |
      | **after, publish aside + release run** | 341 | 313 | **27** | 1 | **2** |
      | after, `payload/` moved aside | 341 | 249 | 80 | **12** | 2 |

      ⚠️ **The subject of the defect was the published slice, not the payload,
      and the original item named both.** With `payload/` moved aside the suite
      already failed **80 tests** before any of this, because the fake-child and
      tool-surface layers need `node.exe` and were never guarded. The publish's
      absence was the silent one, and its summary was character-identical to a
      healthy run's. Both are gated now regardless: a guard nobody accounts for
      is how this one was missed.
      <br><br>
      ⚠️ **Two things the build found that reasoning would not have.**
      `Console.WriteLine` from an `[After(TestSession)]` hook **reaches nothing**
      under TUnit 1.65.0 / MTP — the hook runs and the text appears in no log —
      so the block is written through the real standard-output handle, which
      nothing has replaced. And `[CallerMemberName]` names the wrong thing when
      a guard sits in a helper: the first degraded run reported a skipped test
      called `RunAsync`, which is a private method of `StraySweepTests`. It
      reads `TestContext.Current.Metadata.TestName` now and falls back to the
      caller. The original text follows.
      <br><br>
      **33 tests
      across 13 files** open with `if (!PublishedSlice.IsPresent)` or
      `if (!RepositoryPayload.IsPresent)` and return after asserting a weaker
      property. That is deliberate and correct — a clean clone must be able to
      run the suite, and `IsAbsentAsAWhole` distinguishes *nobody published* from
      *the publish is broken*. **But it means "the smoke layer ran against a real
      browser" and "the publish directory does not exist" produce an identical
      green summary**, which is this project's founding failure class inside its
      own release gate. Found 2026-08-16 at
      [step 20](plan/build-order.md#20-the-first-release), where the answer was a
      paragraph of instructions in [item 8](PRE-RELEASE.md) telling a person
      to check two paths by hand. A release-only assertion that
      `PublishedSlice.IsPresent` — an environment variable the release run sets,
      or a test that reads one — turns it back into a mechanism.

## Decided 2026-08-16 — encoded the same day

Nine decisions from the lesson sweep landed in
[README → Settled 2026-08-16](README.md#settled-2026-08-16): lock scope (`Global\`
only, no `Local\` fallback), lock acquisition never waiting, git detection out of
scope, move-versus-copy on a renamed session directory, logging placement, no
automated checks, git-tag version numbers, the plan's delete-when-complete
lifetime, and fix-forward blocking releases indefinitely. The charter is the live
copy; only the work they create is listed here and under
[At v1 launch](#at-v1-launch).

- [ ] **Give `[STALE]` a row in `kb/README.md`'s conventions table.** Resolved
      2026-08-16 in favour of **keeping** it, and
      [`CLAUDE.md`](CLAUDE.md) now says so; the `kb/` half is owed. Today it is
      defined in prose *below* the table and used by no article, which reads as a
      dead marker — it is not. It is the sanctioned alternative to the one thing
      the whole convention exists to forbid, updating a measurement by reasoning,
      so deleting the definition would leave guessing as the only exit from an owed
      re-check — and [`UPSTREAM-REVIEW.md`](UPSTREAM-REVIEW.md) already instructs a
      reviewer to apply it, so deletion would strand a procedure step too. Give it
      a row beside `[FLOATS]`, `[STABLE]`, `[MACHINE]` and
      `[UNVERIFIED]`, meaning *a re-check is owed and has not happened*. That no
      entry carries one is the healthy state, not evidence of disuse.

- [x] **No scheduled anything — a decision, not an omission.** ✅ 2026-08-16: no
      CI, no scheduled job, no git hook. The pre-release checklist is the only gate
      that exists. This permanently closes the struck *"Add a scheduled
      upstream-drift check"* under [Later](#later); the
      [daily drift directive](CLAUDE.md#the-daily-drift-check) is unaffected,
      because it is a rule an agent runs rather than a job something schedules. The
      cost is recorded in the charter rather than softened — the gate works when it
      is invoked, and nothing makes it fire — and the decision is marked for review
      once the product is finished.

## Decided 2026-08-14 — encoded 2026-08-15

All three are now proper README sections. Retained here only as a record of what
was decided when; the README is the live copy and the two have diverged where
later measurement overruled the original.

- [x] **First-run browser provisioning** → [PLAN §A → first-run browser provisioning](plan/A-runtime.md#first-run-browser-provisioning).
      Changed since: `chrome-headless-shell` is no longer provisioned, and the
      manifest/health-check layer was dropped by decision — the recovery is manual
      and the error text carries it.
- [x] **Instance lifetime** →
      [The session directory is the identity](plan/C-sessions.md#the-session-directory-is-the-identity),
      [Lifetime](plan/C-sessions.md#lifetime-one-timer-and-reclaim-is-forever),
      [Finding sessions](plan/C-sessions.md#finding-sessions-without-a-registry).
      Changed substantially since: the central registry is **dropped**, the bearer
      token is **dropped**, labels are **dropped**, and every expiry timer except
      browser-idle is **dropped**. The directory is the identity, the handle and
      the lock.
- [x] **Three browsers, three collision behaviours** →
      [kb: stray detection](kb/windows/detection.md) and
      [kb: profile fallback](kb/chromium/profiles.md). These
      are measured facts, not design, so they belong in the knowledge base rather
      than the charter.

<details>
<summary>Original 2026-08-14 text, kept for provenance</summary>

- [ ] **Encode: first-run browser provisioning.** BrowserAI does **not** bundle the
      full Chrome for Testing browser — the redistribution position is unresolved
      and the only on-point public statement is adverse (a Google engineer, 2023:
      *"Chrome for Testing is a flavor of Google Chrome, so
      google.com/chrome/terms applies"*, which forbids redistribution). It
      downloads on first run instead, as Playwright itself does. Removes 427 MB of
      the 806 MB payload.

    - **The exact version is pinned for free.** `playwright-core/browsers.json`
      carries the revision and `browserVersion`; the CFT URL is
      `https://cdn.playwright.dev/builds/cft/<browserVersion>/win64/chrome-win64.zip`,
      which 307s to Google's bucket. No "latest" lookup exists anywhere in the
      registry code. Old versions still resolve back to Chrome 115 (Jul 2023),
      though Google documents no retention policy.
    - **Integrity must be ours.** Playwright validates only `content-length`, and
      upstream closed and locked the request for checksums
      ([#39559](https://github.com/microsoft/playwright/issues/39559)). Hash each
      archive at build time into a manifest shipped in the artifact, verify after
      download, delete and fail closed on mismatch. Without this, "exactly the
      bytes we tested" is untrue.
    - **Measured 2026-08-14:** chromium 202.3 MB + shell 119.7 MB + ffmpeg + winldd
      = **323.5 MB down, ~700 MiB on disk**, 20.3 s end to end on a 300 Mbps link.
      Arithmetic for slower links: 4 m 19 s at 10 Mbps, 43 m at 1 Mbps.
    - **Timers:** stall 30 s (Playwright's own `NET_DEFAULT_TIMEOUT`, leave it),
      absolute cap 45 min, extraction cap 10 min, outer deadline 60 min as a crash
      tripwire.
    - **`init` must not block.** Return the handle immediately with
      `browserProvisioning: "downloading"`; browser-needing calls return the
      in-progress error; `browser_get_config` still works. **In-session recovery is
      proven** — the same child navigates successfully once the install lands, no
      restart needed.
    - **Strip upstream's remediation string.** It says `Run npx @playwright/mcp
      install-browser chromium`, which BrowserAI does not ship and which would
      resolve a different package and a different revision. A model will act on it.
    - **The failure is invisible by default.** A missing browser gives
      `initialize` OK, `tools/list` OK, stderr empty, and `isError: true` in a
      success-shaped body. A *partial* install gives `spawn EFTYPE` — and
      Playwright then writes `DEPENDENCIES_VALIDATED` into the corrupt directory,
      suppressing revalidation for 30 days. Check `INSTALLATION_COMPLETE` and the
      manifest hash; Playwright never checks the former at launch.
    - **Environment:** strip `PLAYWRIGHT_DOWNLOAD_HOST` and its three per-browser
      variants (they replace the mirror list with one host and destroy failover);
      pass through `HTTPS_PROXY`/`HTTP_PROXY`/`NO_PROXY`/`ALL_PROXY` and
      **`NODE_EXTRA_CA_CERTS`** (needed under TLS inspection). SOCKS is not
      supported by the download path. Egress needs three hosts:
      `cdn.playwright.dev`, `storage.googleapis.com`,
      `playwright.download.prss.microsoft.com`.
    - Browsers live at `%LocalAppData%\BrowserAI\browsers\`, resolved from
      `VelopackLocator.Current.RootAppDir` — **never** inside `current\`, or every
      update re-downloads 700 MB. With `PLAYWRIGHT_SKIP_BROWSER_GC=1` mandated,
      pruning old revisions becomes BrowserAI's job.

- [ ] **Encode: instance lifetime and the session registry.**

    - **Reclaim is forever, and the registry is on disk.** A torn-down handle stays
      resumable against its recorded config and directory. The durable thing is the
      profile, not the process — measured 2026-08-14, a resume after killing the
      node child preserves cookies, localStorage, IndexedDB, service workers and
      CacheStorage, and loses **only `sessionStorage`**, in ~515 ms.
    - **An explicit `resume` tool** alongside `init`. Its job is *legibility*, not
      enforcement: reclaiming becomes a deliberate act with a visible warning
      instead of a silent resurrection. **The lockfile is what actually prevents
      two agents colliding.** Spec-sanctioned family — SEP-2567 names `destroy_*`
      and `list_*` companions to a creation tool.
    - **`init` REFUSES a directory that already has an instance.** Decided
      2026-08-14, and deliberately *not* idempotent. A silent reuse would let an
      agent believe it created something fresh when it inherited another agent's
      live session — the surprise this design exists to prevent. Instead `init`
      fails with an error naming the existing handle and directing the caller to
      `resume`. **Being made to say "resume" is the point:** it converts an
      accidental collision into a stated intent, and an agent that did not expect
      an existing session now knows one exists. Clean separation follows: `init`
      = "create", `resume` = "reclaim", and neither can be mistaken for the other.
    - **`resume`'s warning text is a security surface**, like `init`'s description.
      It is the only place a model is told that reclaiming may stomp another
      agent's work. Write it with that weight and pin it with a test, as
      `SixFive7/OutlookAI` does for its instructions string.
    - **Version the registry schema from day one**, as a required top-level field.
      It is an on-disk format that outlives releases and will be read by a newer
      BrowserAI than wrote it. Unversioned means the first format change is a field
      migration with no way to detect the old shape — and a registry that cannot be
      read is a machine full of orphaned locks.
    - **Two locks, different scopes.** One machine-wide mutex around each registry
      read-modify-write, held for milliseconds; one per-directory profile lock held
      for the instance's life. Conflating them serialises every session.
      `Global\BrowserAI-{sha256(canonical path)[..32]}`; atomic registry writes
      (temp + rename); `Global\` needs `SeCreateGlobalPrivilege`.
    - **Our own lockfile goes in the profile directory**, `FileShare.None` so the
      OS releases it on death, and it **records its holder** — PID, process
      creation time, handle, session type, BrowserAI version. A stale lock then
      yields *"held by PID 1234 since 14:02, no longer running — reclaiming"*
      instead of a bare refusal.
    - **The registry is free crash recovery.** On startup, reap entries whose
      recorded process is gone. That is §D's "alive-but-orphaned holder" solved as
      a side effect.
    - **Version the registry schema from day one.** It outlives releases and is
      read by a newer BrowserAI than wrote it.
    - **Timers (values still open):** browser-idle ~10 min → `browser_close`,
      keeping the node child (measured 329 → 110 MB, 186 ms to relaunch);
      handle-idle ~60 min → full teardown, release the lock, mark reclaimable;
      teardown budget 15 s graceful then job-object kill, matching the child's own
      `setupExitWatchdog`. A never-used handle has no browser at all (~123 MB).

      > ✅ **Built and superseded at
      > [step 17a](plan/build-order.md#17a-the-browser-idle-timer-and-teardown),
      > 2026-08-16.** Only the browser-idle timer exists — the handle-idle one was
      > dropped when [reclaim became forever](plan/C-sessions.md#lifetime-one-timer-and-reclaim-is-forever),
      > and this bullet is the last place it is still written down as planned.
      > **Both numbers in it are wrong and are corrected in
      > [kb](kb/playwright/provisioning-and-timings.md#timings-spawn-resume-idle-close-proxy-overhead):**
      > re-measured twice, the fall is ~496 MB → ~118 MB and the relaunch costs
      > ~0.41 s rather than 186 ms.
    - **The client watcher is stdin EOF + an `OpenProcess` handle on the client
      PID.** Never ping-based: `ping` is removed at 2026-07-28.

      > ✅ **Built at [step 17a](plan/build-order.md#17a-the-browser-idle-timer-and-teardown), 2026-08-16**, as
      > `src/BrowserAI/Interop/ClientLiveness.cs`. ⚠️ **One thing it does that
      > this bullet does not say, and it is load-bearing:** cancelling the
      > server's own token does **not** end `RunAsync` over real stdio, so the
      > watcher closes BrowserAI's protocol channel instead — producing the same
      > end-of-input the client's own exit would have.
    - **A server cannot ask the agent anything.** Measured: elicitation reaches the
      *human* via a TUI modal and auto-cancels in ~7 ms under `-p`; nothing at any
      spec revision injects text into a model's context unprompted. The only
      workable variant is prepending a line to the **next** tool result.
    - **Eyes open:** forever + on-disk makes a handle a durable bearer token, a
      deliberate departure from the spec's "bounded lifetime" advice for
      unauthenticated servers. Use ≥128 bits of entropy.

- [ ] **Encode: three browsers, three different collision behaviours.** On a
      profile-lock collision — full Chromium returns a clean *"Browser is already
      in use"*; **`chrome-headless-shell` returns nothing at all** and both
      instances share the profile; and **Firefox puts a native modal dialog on the
      user's desktop** (observed 2026-08-14). Playwright's `isProfileLocked`
      pre-flight is Chromium-shaped, checking `lockfile`. All three matter: the
      second is why BrowserAI's own lock is load-bearing, and the third would be an
      invisible hang in a background MCP server.

</details>

## Resolved 2026-08-14 → 2026-08-15

- [x] **The no-timer proposal, and the registry.** ✅ Adopted in full and encoded.
      One timer only — browser-idle — and **the registry is dropped**: the
      directory is the identity, the handle and the lock, and `lock.json` inside it
      is the authority. Labels are gone with it. See
      [The session directory is the identity](plan/C-sessions.md#the-session-directory-is-the-identity)
      and [Lifetime](plan/C-sessions.md#lifetime-one-timer-and-reclaim-is-forever).

## Open after 2026-08-15

- [x] **Post-reboot resurrection: mechanism excluded, prevention dropped.** ✅
      Measured 2026-08-15 and encoded in
      [kb: resurrection](kb/chromium/resurrection.md).
      `RegisterApplicationRestart` **was never succeeding** — Playwright's command
      line overshoots the 1023-character limit by 531–807 in every shippable
      configuration, verified on live processes with a registering positive control.
      No lever ships; a test asserting the browser is unregistered replaces it. By
      elimination the resurrection came from the Windows sign-in restore path, which
      is `[UNVERIFIED]` without a reboot — the command-line fingerprint that would
      settle it is recorded in the KB.

- [x] **When does the stray sweep run?** ✅ Settled and encoded as
      [The stray sweep](plan/C-sessions.md#the-stray-sweep-and-the-concurrency-it-must-survive).
      Two triggers — BrowserAI startup and a logon scheduled task — each looking
      twice, with twelve races enumerated and a test against each. Detection is
      enumeration rather than inventory lookup
      ([kb: enumeration](kb/windows/detection.md#enumeration-works--and-it-moves-the-safety-boundary)),
      so the sweep and the pointer store are now independent.

- [x] **The four named modes become three plus a modifier.** ✅ Settled and encoded
      2026-08-15 as [Three modes](plan/C-sessions.md#three-modes-and-tracing-as-a-modifier),
      with the eight-combination table, the reason rows 3–4 stay closed, and
      discoverability as a hard requirement across four model-facing channels
      generated from one table.

- [x] **"Reap" was the wrong word.** ✅ Encoded 2026-08-15 in §E. Confirmed by
      measurement — 16 runs, 106 processes, 0 survivors — so a dead BrowserAI leaves
      no running children at all. Only a stale lockfile survives, which is a file
      problem. The registry lost its last independent justification here.

- [x] **`winldd`, explained.** ✅ No action required; it is informational. Upstream
      passes `["chrome-win"]` while Chromium extracts to `chrome-win64`, so the
      dependency check is a **permanent no-op for Chromium** (and for
      `chromium-headless-shell`, which extracts to `chrome-headless-shell-win64`).
      Firefox passes `["firefox"]`, the real directory, so it runs — 39 binaries,
      +329 ms, cached 30 days. ✅ The promised line is now written: it is the
      *"one specific thing to watch in the `playwright-core` diff"* paragraph in
      [`UPSTREAM-REVIEW.md`](UPSTREAM-REVIEW.md), and the standing check is
      [re-verification row 10](kb/README.md#re-verification-index) — if upstream
      fixes the directory name, Chromium suddenly starts validating 39 binaries on
      cold start, and a one-character upstream fix becomes a latency regression.

- [x] **Label reuse.** ✅ Moot. Labels are gone — the directory is the identity.

- [x] **Never kill by image name.** ✅ Encoded 2026-08-15 as
      [§D → Never by image name](plan/D-locking.md#never-by-image-name), with the
      two-mechanism invariant (job object for the living, path-keyed identification
      for survivors), the forbidden-API list at analyzer-error severity, and the
      measured warning that `--user-data-dir` alone is **not** an ownership signal —
      Discord, VS Code, Signal, Teams, WhatsApp, Steam, ChatGPT and four WebView2
      processes all carry it on this machine.

- [x] **`--output-max-size`.** ✅ Resolved and encoded 2026-08-15. Verified in
      `coreBundle.js`: `defaultConfig` contains only `browser` and `timeouts`, and
      `mergeConfig` filters through `pickDefined`, so **no default is applied at any
      merge stage** — the open half of the question is closed. It runs on every tool
      response, recursively lists the whole output directory, and unlinks
      oldest-first past the threshold, sparing only the current response's writes.
      Never set; env var stripped; retention is the calling agent's decision.

- [x] **First-run download self-healing.** ✅ Decided: stay with Playwright's
      built-in capabilities, no manifest and no health-check layer. The consequence
      is stated plainly in
      [§A](plan/A-runtime.md#first-run-browser-provisioning) rather than softened —
      a tree corrupted *after* a successful install never re-downloads, because
      `INSTALLATION_COMPLETE` short-circuits without validating. Recovery is the
      `browserai_reinstall_browser` tool plus error text that names the path.
      **Built 2026-08-16** ([step 15](plan/build-order.md#15-first-run-provisioning-and-browserai_reinstall_browser)),
      and one half of it turned out to be the *other* direction: an install that
      was **interrupted** does self-heal, because the marker is written last —
      what does not is one corrupted afterwards. So BrowserAI checks the marker
      before it calls a browser present, which upstream never does at launch, and
      the error text now says which of the two recoveries applies to which case.

- [x] **Record why an instance exists.** ✅ `purpose` is a **required** field on
      `init`, appended on `resume`, updatable via `browserai_set_purpose`, stored in
      `lock.json`, and played back on a refused `init`, on `resume` and in `list`.
      Encoded, including the caution that it is a channel between agents and must be
      capped, sanitised and framed as data rather than instruction.

- [x] **Per-`init` browser choice.** ✅ On `init` only; `resume` reads it from
      `lock.json` and refuses it as an argument, because a profile is
      browser-specific. Firefox ships in v1.

- [x] **Firefox registers itself for Windows restart, and [step 17](plan/build-order.md#17-firefox) turned it off.** ✅
      **Built and measured on both sides 2026-08-16.** The config generator writes
      `firefoxUserPrefs: { "toolkit.winRegisterApplicationRestart": false }` into every
      Firefox config, and a live launch from it leaves **0 of 7** processes registered
      where an upstream-default launch leaves **1 of 7**. The delivery route is not the
      obvious one: `firefoxUserPrefs` reaches `user.js` only on the **BiDi** launcher's
      path, and `@playwright/mcp` takes the classic one, which sends them over juggler
      as `Browser.enable { userPrefs }` — a driven profile has no `user.js` at all. So
      this is an *unregistration* shortly after startup rather than a prevention, and
      the width of that window is `[UNVERIFIED]`. Original note follows.

- [x] ~~**Firefox registers itself for Windows restart, and [step 17](plan/build-order.md#17-firefox) has to turn that off.**~~
      Measured 2026-08-16 on a Firefox BrowserAI provisioned: exactly one process
      in the tree answers `GetApplicationRestartSettings` with `S_OK`, while every
      Chromium process answers `ERROR_NOT_FOUND`. That is
      `toolkit.winRegisterApplicationRestart` (default `true`) doing what
      [kb: resurrection](kb/chromium/resurrection.md#the-mechanism-and-what-is-still-unproven)
      says it does. **Containment is unaffected** — `KILL_ON_JOB_CLOSE` happens
      now and Windows' restart happens after a reboot or an update — but a Firefox
      session shipped without setting that pref to `false` in the profile means a
      machine update resurrects a browser no session claims, with no lock and
      nothing to attribute it to. Both directions are asserted by
      `BrowserContainmentTests`, so a change in either is a red build rather than
      a surprise.

- [ ] **`browserai_init` still refuses `browser: "firefox"`, and two decisions stand between it and not doing so.**
      Recorded 2026-08-16 at [step 17](plan/build-order.md#17-firefox), which built
      §D's Firefox half — the preflight, `RmGetList` attribution and the
      restart-registration preference — and deliberately did not open the choice,
      because that is [§C](plan/C-sessions.md)'s *per-`init` browser choice* rather
      than §D's. What is owed:
      **(a)** [row 6](plan/H-model-surface.md#h4-the-error-catalogue) quotes a
      download size, and the only one this build knows is Chromium's 203.8 MB —
      naming it for a Firefox install would be a measured-looking number that was
      never measured; and
      **(b)** `browserai_reinstall_browser` takes no arguments *because there is
      nothing to name*, which stops being true with two trees on disk.
      Everything else is already family-parameterised: provisioning, the config
      generator, the launch preflight and the sweep all read the family from the
      session's own `lock.json`, so a record naming Firefox is honoured on `resume`
      instead of being silently run as Chromium against a Firefox profile.

- [x] **Firefox's `parent.lock` preflight.** ✅ Encoded 2026-08-15 as
      [§D → Firefox: the preflight, and a second detection path](plan/D-locking.md#firefox-the-preflight-and-a-second-detection-path)
      — the preflight is mandatory rather than defence in depth, and detection
      needs only a different attribution step, because image-path detection
      already covers Firefox for free. Original note follows.

- [x] ~~**Firefox's `parent.lock` preflight.**~~ The one piece of Firefox support that
      is designed but not yet written into the charter as a requirement. Playwright's
      `isProfileLocked` checks only Chromium's `lockfile`, so without our own
      preflight a collision puts a **native modal on the desktop blocking up to
      3 minutes** — an invisible hang in a background server. Our lock is taken
      before launch, so the ordering already covers it, but coverage-by-ordering
      needs a test that fails if the ordering changes. Firefox also needs its own
      stray detection: no `Chrome_MessageWindow` equivalent, so it is `parent.lock`
      sharing-violation → Restart Manager `RmGetList` for the PID.

## Later

- [ ] **Review the "no automated checks" decision once the product is finished.**
      Taken 2026-08-16 with the cost stated plainly: the
      [pre-release checklist](PRE-RELEASE.md) is the only gate, it works when
      it is invoked, and nothing makes it fire. That trade is right while both the
      suite and the release cadence are predicted rather than observed — many
      commits without re-running everything, and no hosted CI — and it is
      explicitly marked for review when they are neither. Re-open it against the
      finished product and a real cadence, not against a guess about them.

- [x] **Upstream-drift check.** ✅ Done 2026-08-15, but **not** as a scheduled job.
      A [`CLAUDE.md` directive](CLAUDE.md#the-daily-drift-check) runs it at most
      once per working day, recording the result in
      [`drift-check.json`](drift-check.json). A poller is unnecessary here: this
      project is built entirely through an agent, so a session-start rule fires by
      construction — the check happens because the work happens. First run: zero
      drift across all five upstreams. The note below is kept for the reasoning,
      which still applies.

- [ ] ~~**Add a scheduled upstream-drift check.**~~ *(superseded by the above; text
      retained for the Dependabot/Renovate analysis.)* Every dependency floats to
      latest *at build time*, and the marker test in
      [`upstream-review.json`](upstream-review.json) fires when a build happens
      — but nothing makes a build happen. Releases are manual, so a quiet month
      is a month in which upstream can move unobserved. This is the missing half
      of the marker: the marker catches "we are about to ship a version nobody
      reviewed"; this catches "upstream moved while nobody was looking".

    - **Dependabot cannot do this job.** Verified against `dependabot-core`'s
      own test table on 2026-08-14: a NuGet `Version="*"` is rewritten to `*`,
      producing a byte-identical file and therefore no PR. npm `"latest"` is
      skipped outright by a dist-tag guard. Dependabot bumps *declared floors*,
      and this project declares none — so `SixFive7/Jeeves`' stated mechanism
      ("Dependabot keeps the floor rising") does not transfer. Renovate can
      reach it only through a `customManagers` regex rule pointed at
      `upstream-review.json`, and has no NuGet lock-file support
      ([#6610](https://github.com/renovatebot/renovate/issues/6610), open).

    - **A working implementation already exists**, written and executed
      2026-08-14: it resolves all five upstreams from the npm and NuGet
      registries and the Node dist index, compares against
      `upstream-review.json`, and reported 0 drift in ~2 s. Two design points
      worth preserving — the drift issue's open/closed state *is* the drift
      state, so the job closes its own issue once the review lands and an open
      issue proves the review has not happened; and it throws on a marker entry
      with no resolver, so a newly-added upstream cannot be silently unwatched.

    - **Prior art says a poller is not optional.** Debian `debian/watch` +
      `uscan`, Homebrew `livecheck` + 3-hourly autobump, and Nix
      `update-flake-lock` are all the same shape: a declared marker plus a
      scheduled checker. Floating with no lock refresh, no poller and a manual
      release is a combination with no located precedent, because that
      combination contains no detector.
