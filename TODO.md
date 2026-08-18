<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# TODO

Central work list for BrowserAI. One file, so nothing survives only as a comment
somewhere.

**What belongs here:** work settled in intent but not yet done.

**What does not:** open design questions and known hazards. Those live in
[`DECISIONS.md`](DECISIONS.md) under [Open design decisions](DECISIONS.md#open-design-decisions), and in the
[hazard index](HAZARDS.md#hazard-index). An item moves here once the decision
behind it is made, and is **deleted** when it is done — `git log` is the record of
what was done; this file is the record of what is not.

**Format:** `- [ ] **Title.** Why it matters, then what to actually do.` A claim
about an external source needs the date and version it was true at.

---

## Adversarial review, 2026-08-18 — what is left of it

Two adversarial readers were asked to **break** the design by reasoning rather
than by load, on the maintainer's argument that *"just running 100 concurrent
browsers is not enough of a test to find all concurrency bugs."* He was right:
reading found ~18 findings in about half an hour each, against seven from a night
of load testing. Full reasoning, with every interleaving spelled out, in
[`docs/reviews/`](docs/reviews/README.md), whose index carries **the status of
every finding** — fixed, narrowed, or open.

**The seven wrong-answer defects are fixed**, each with a regression test that
was watched red first, and each is in `git log` rather than here. Two of them
could not be reproduced as interleavings at all and are tested as invariants
instead, which the tests say out loud. What remains below is what the same review
found and this pass did **not** do; the bounded ones also have rows in the
[hazard index](HAZARDS.md#hazard-index), because a thing that is open needs to be
findable from more than one direction.

- [ ] **Path aliasing defeats the per-directory gate.** `Path.GetFullPath` does
      not resolve `\\?\`, 8.3 names, junctions, `subst` or mapped drives, so two
      spellings give two mutex names and **one `lock.json`**. `\\?\C:\...`
      needs no filesystem setup at all.

- [ ] **Two of thirteen ungated `lock.json` readers ACT on an absence rather than
      reporting it.** `SessionIndex.Locate` → `IsRemovable` → `Sweep` **deletes
      the index entry** for a live session; `SessionManager.Existing` reads null
      as *"free, proceed"* and can rebind a stale-locked session's browser
      family. **The kb claim they falsified is corrected** — `kb/windows/processes.md`
      said *"every ungated one fails in the safe direction"* and now enumerates
      all thirteen — and each reader has a [hazard row](HAZARDS.md#hazard-index)
      naming what would close it. What is left here is the code: make
      `NotASession` non-removable while a `lock.json.new-<guid>` temp is on disk,
      and move `Existing`'s check inside `TakeOrReport` where the record is
      already read under the gate. **Neither is a change to make in the same pass
      as something else** — the second restructures the refusal on the
      most-exercised path in the product.

- [ ] **The probe-before-gate redesign, attacked before it was built.** It is a
      sound *ownership* test and an unsound *freedom* test: with the gate skipped
      on the free path, both contenders probe, A wins the rename, and B's retry
      loop hands B the name the instant anything closes A's handle — A then holds
      a valid handle to a nameless file. **Adopt the probe as a fast refusal in
      front of an unchanged `TryAcquire`, never as a replacement for the gate.**

- [ ] **Watch [microsoft/playwright-mcp#1716](https://github.com/microsoft/playwright-mcp/issues/1716)
      and act on what upstream decides.** The report: `"launchOptions":
      { "chromiumSandbox": true }` in a config file is parsed, validated and then
      **discarded** — only the `--sandbox` CLI flag enables the sandbox, so a
      configuration setting that key believes it has a sandbox and does not, with
      no warning and no difference in any health signal. Measured at
      `@playwright/mcp` 0.0.79. BrowserAI is not exposed: it passes `--sandbox` on
      the command line, and `SandboxFlagTests` asserts the flag reaches every node
      child.

      **All three outcomes need something.** *Fixed* — the key changes meaning
      between two versions BrowserAI floats across, so the
      [re-verification index](kb/re-verification.md) needs a row; it is exactly
      what [the golden snapshot cannot
      see](TESTING.md#the-upstream-review-gate), because the tool surface and the
      config schema both hold still while the behaviour behind one key inverts.
      *Declined* — the dead arm of upstream's validation is then by design, which
      belongs in [kb](kb/playwright/configuration.md) as a settled position rather
      than sitting here as a defect. **A declined report with a reason is worth as
      much as a fix, and it is the half nobody writes down.** *No response*, the
      likeliest — choose between letting it sit and fixing it forward with a PR,
      but **do not work around it**: BrowserAI is already immune, so a workaround
      is risk for no benefit.

      Check with `gh issue view 1716 --repo microsoft/playwright-mcp --comments`
      at [the daily drift check](CLAUDE.md#the-daily-drift-check). A bump that
      closes this issue and one that ignores it look identical from the registry.

- [ ] **The browser-reinstall row was settled on an impossibility that is not
      one.** [`DECISIONS.md`](DECISIONS.md) closed *download alongside and swap*
      with *"Windows will not rename a directory holding open executables"*.
      **Measured 2026-08-18 and it is false**: with a live process's working
      directory deliberately elsewhere, `Directory.Move` of the running image's
      parent and grandparent both succeeded, and so did renaming the running
      `.exe`; only *deleting* it was refused
      ([kb](kb/windows/processes.md#the-win32-interop-surface)). **This is the
      one case the justification sweep found where the reason was wrong AND the
      rule it justified rests on the wrong ground.** The refusal is deliberately
      left standing — a browser whose tree is renamed underneath it holds open
      handles into a path that no longer has that name, and **nothing has
      measured what Chromium then does**. Two things are owed: measure that, then
      re-decide the row on evidence rather than leaving a settled position
      resting on a retracted sentence.

- [ ] **The only refusal left in the product may be aimed at nothing.**
      `SessionToolPolicy.Decide` refuses `browser_annotate` on any mode that did
      not promise a window, entirely because
      [kb](kb/playwright/tools-and-artifacts.md) says *"the window appears in
      headless too"* — a sentence with **no date, no version and no method**,
      whose two list-neighbours both quote upstream source. A measurement in the
      same knowledge base points the other way: headless Chromium created 308
      top-level windows across a suite run and **showed zero**
      ([kb](kb/windows/detection.md)). Settle it with the rig that already
      exists: drive the resolved child at `headless: true`, call
      `browser_annotate` under a hard timeout with the `SetWinEventHook` watcher
      that measured the 308, and record whether a window is realised and whether
      the call returns. **Either outcome is worth having** — it either earns the
      product's last refusal or retires it.

- [ ] **The reason that deleted the whole tool-permission layer is itself
      unmeasured.** The matrix went on 2026-08-18 because *"the agent runs as the
      same Windows user, so DPAPI decrypts for it — an agent holding any file
      tool reads what the matrix declined to return"*. That sentence is now in
      [`DECISIONS.md`](DECISIONS.md), [`README.md`](README.md),
      [`ARCHITECTURE.md`](ARCHITECTURE.md), [`TESTING.md`](TESTING.md),
      [`QUESTIONS.md`](QUESTIONS.md), `SessionToolPolicy.cs`, `SessionMode.cs`,
      `SessionPolicyTests.cs` and [kb](kb/playwright/tools-and-artifacts.md) —
      **and the kb entry, where the chain terminates, carries no date, no version
      and no method.** Six documents citing each other is a circle, not a source.
      **The specific risk has a name**: Chromium's App-Bound Encryption binds
      cookie decryption to the browser's own code identity, and whether Chrome
      for Testing 152 / `chromium-1237` enables it under a custom
      `--user-data-dir` is exactly the unasked question. Settle it from a second
      process as the same user: `CryptUnprotectData` the `os_crypt.encrypted_key`
      out of `Local State` and decrypt one row of `Network\Cookies`. **The
      removal may well still be right on cost alone. It was not made on cost.**

- [ ] **Headless-with-storage is still refused on a reason the same pass
      declared void.** [`DECISIONS.md`](DECISIONS.md) keeps it out because *"it
      is the one combination granting full credential access with no visible
      signal"* — a security reason of precisely the class the 2026-08-18 removal
      above ruled out. The pass that retired the tool matrix, the
      `browser_get_config` secrets guard and the annotate permission judgement
      did not revisit this row; it simply was not looked at. **It cannot be both
      ways**: either the reason above holds and this row falls with the rest, or
      it does not and the removal needs re-opening. Nothing to measure — this is
      a consistency decision that is owed either way.

- [ ] **The justification sweep's residue: 24 assumed justifications named and
      not settled.** The sweep ran 2026-08-18; what it *settled* is in `git log`
      rather than here, and what it did not is below. Three read-only inventories
      plus a first-hand pass over [`Interop/`](src/BrowserAI/Interop), the build
      files and [`build/`](build) examined **598 load-bearing justifications**:
      **309 measured here**, **226 cited to a source**, **63 assumed**. Of the
      63, **13 were settled by measurement and 11 relabelled** in the same pass;
      the four highest-value remainders have their own items above. The rest,
      each stated as fact, load-bearing, undated and uncited, and each cheap:

      **In [`kb/`](kb/README.md)** — a measurement store, so an assumed entry
      there is a category violation. The long-path guarantee rests on
      `app.manifest` alone and **nothing in the tree mentions `LongPathsEnabled`**,
      the registry value Windows also requires; it is `1` on this machine, so
      every long-path measurement here is conditional on a value nobody recorded.
      *"The damage is a lost session rather than corruption"* is why nothing
      defends against Velopack's `force_stop_package`, while another article
      asserts concurrent profile writers cause *"silent corruption"* — neither
      was run. *"The 156 denials do not matter"* infers the denied set is all
      SYSTEM from the denial itself, and leans on a claim marked `[UNVERIFIED]`
      elsewhere. *"Schemas are deferred"* is the whole reason `ServerInstructions`
      exists, and the 2026-08-18 capture that could have retired it was performed
      and the sentence left standing beside it. Then: `spawn EFTYPE` forever ·
      screenshots not byte-stable · the stale-browser GC blast radius · *"39
      binaries"*, which is Firefox's measured count asserted of Chromium ·
      *"768 MB"*, a derived sum with no date, the same construction this
      repository has already retracted twice · and `Console.ReadKey`'s
      console-attached arm, the redirected arm now being measured.

      **In [`src/`](src/BrowserAI)** — the update stall budget is sized off
      Playwright's socket timeout, a different downloader in a different runtime ·
      the crash tripwire's *"nothing that is working can reach it"* covers the
      download and not the unbounded `CheckAsync` · *"the SDK refuses to negotiate
      BELOW a pinned version"* decides how much the explicit check is doing ·
      *"durable"* is used four times meaning *survives this process* against a kb
      entry that reserves the word for *survives the machine* · *"an unhandled
      exception is not guaranteed to unwind the stack"* is the entire crash-log
      design · *"a shim cannot be started without `cmd.exe`"* cites two kb entries
      about other subjects · *"nothing a child writes to stderr can be lost"* is
      broader than both the measurement and the code, which abandons the pump
      after 2 s · `win` as Velopack's default channel · *"a rollback always
      reports zero"* · the `NUL.png` and trailing-dot filename refusals.

      **In the top-level documents** — SmartScreen *"instant reputation"* for
      ~$10/mo is the basis on which somebody will one day spend money, and
      *instant* is the historically EV-only property · `claude mcp list` exit
      codes carry **no client version**, in a repository that stamps every other
      client fact · *"upstream renamed one of its own tools inside four months"*
      is labelled *the deciding argument* for never renaming and names no version
      pair · the flat-namespace reason for the `browserai_` prefix, when the one
      client that matters delivers tools as `mcp__<server>__<tool>` · Chrome's
      `ProcessSingleton` forwarding · MTP IDE discovery for two vendors with no
      versions · two [`STACK.md`](STACK.md) rows justifying components **nothing
      in the tree references** · *"a one-step locked build passes while resolving
      nothing"*, where the cited NU1512 describes a loud failure and the
      conclusion drawn is a silent one · Node v26's `node.exe` being *"10 MB
      larger"* · and the single 2023 statement by an unnamed Google engineer that
      is the entire basis for *Chrome for Testing may not be redistributed*, and
      therefore for first-run provisioning existing at all.

      **What the sweep could not cover.** [`tests/`](tests) was read only where a
      claim pointed into it, [`docs/reviews/`](docs/reviews/README.md) was not
      swept, and the three inventories judged a citation sound on the
      *specificity* of the pointer without opening most upstream sources — so
      **the cited-to-a-source count is an upper bound and the assumed count a
      lower one**. Two of the three found exactly that whenever they did follow a
      chain to its end.

- [ ] **The failed-rewrite recovery of `lock.json` has no test.** A failed rewrite
      must not also release the lock: the handle is dropped before the
      replacement, so an exception on the way through leaves the session silently
      unowned. **It shipped broken once**, which is the shape that earns a
      regression test. What is missing is a seam — the rename is
      [`SessionLock`](src/BrowserAI/Sessions/SessionLock.cs)'s own and nothing can
      make it fail on demand, so provoking it needs either an injectable file
      operation or a probe process holding the replacement path at the right
      moment. **Prefer the probe if the alternative puts a test-only interface on
      the product's hot path.**

- [ ] **53 rows of the [hazard index](HAZARDS.md) are `open` and carry `—` for evidence.**
      The file's rule is that a row marked `closed` with `—` is not closed; this
      is the converse — rows nobody has adjudicated either way. By category, using
      the index's own `Area` cells verbatim: Bundling and AOT 13, Child runtime and
      configuration 10, Process and OS (Windows) 9, Protocol and SDK 7, Tooling and
      CI 7, Packaging and updates 4, Handle routing and instance lifetime 3.
      9 more are `open` while carrying evidence, so 62 are `open` in total, against
      90 `closed`.

      ***Corrected 2026-08-18 (previously "54 rows that are `open` and carry `—`",
      with "Bundling and AOT 14" and "8 more are `open` while carrying
      evidence")*** — re-counted by the test, not adjusted: the justification
      sweep measured the `UseSystemResourceKeys` saving, so that row moved from
      `open` with `—` to `open` with evidence. **The total of 62 did not move**,
      which is the predicate doing its job rather than an oversight. Many will close against tests that now exist; some are upstream
      behaviours that cannot close at all and should say so. **An honest `open` with
      a reason beats a `closed` with a weak one.**

      **Every number in the paragraph above is asserted on each build** by
      `RecordedCountTests.TheHazardTallyInTodoIsWhatTheIndexHolds`, which reads the
      sentence as its anchor and re-counts the table through the same
      `HazardIndex` parser `HazardIndexTests` uses — the categories individually,
      the total separately, and the sum of the categories against the total.
      Rewording the sentence fails the build rather than quietly unhooking the
      check.

      ***Corrected 2026-08-18 (previously "Three more read `open` while carrying
      evidence, so 57 are open in total")*** — re-counted, not adjusted: it was 4
      and 58 when the drift was found, and 5 and 59 once that day's own work
      had added five rows to the index. The row that moved is the CVE-response row, corrected from
      `closed 2026-08-16` to `open` on 2026-08-17 without anybody touching this
      tally. That is the fourth wrong count of the day and the reason the
      paragraph now has a test.

      ***Corrected again 2026-08-18 (previously "5 more are `open` while carrying
      evidence, so 59 are `open` in total", then briefly 6 and 60)*** — the
      adversarial-review work added **four** `open`-with-evidence rows: the
      revision prune's launch-path window, the two ungated `lock.json` readers
      that act on an absence, and the unbounded calls inside the per-directory
      gate. **The category numbers above did not move**, and that is the predicate
      doing its job rather than an oversight: they count rows that are `open`
      **and** carry `—`, and all four carry evidence. Caught by the test in the
      same run that added them, which is what it is for.

      **When correcting a count here, quote the predicate before the number** —
      not *"54 rows"* but *"54 rows that are `open` **and** carry `—`"*. That has
      already broken a correct figure once: a re-count measured rows that were
      `open` at all, a different predicate over the same table, and the five-row
      gap looked like a stale count rather than a different question.

- [ ] **No spawn record is persisted across runs, so the suite's reclaim pass
      cannot finish its job.** [Testing](TESTING.md) asks that anything the
      previous run recorded is terminated by `(pid, creationFileTime)` from its own
      spawn record, and nothing writes one — so a run killed mid-test leaves a
      process the next run cannot identify, only a directory it cannot delete. The
      other three bullets are built and the pass is itself a test now, which is
      what makes this gap visible: the survivors are **named** rather than silently
      skipped.

- [ ] **`JobLauncher` declares `ref char lpCommandLine` where Microsoft's own
      Win32 metadata generates `ref Span<char>`.**
      [`JobLauncher.cs`](src/BrowserAI/Interop/JobLauncher.cs) passes
      `ref commandLine[0]` — **no length, no terminator check**, and no write-back
      of the mutation `CreateProcessW` is documented to make. Nothing is known to
      be wrong with it and the whole suite runs over it, but it is a weaker
      signature than the vendor's for the same call, and that is the kind of
      difference that presents as a plausible wrong answer rather than as an error.

- [ ] **Answer the CsWin32 metadata licence question before any move into
      `src/`.** The generator is MIT; **the metadata it generates from is not.**
      `Microsoft.Windows.SDK.Win32Metadata` and `WDK.Win32Metadata` ship under
      Windows SDK licence terms and `SDK.Win32Docs` under
      `aka.ms/WinSDKLicenseURL` — all three carrying **no SPDX licence expression
      at all** on nuget.org, checked 2026-08-17. Generated code, doc comments
      included, compiles into whatever references it. Today it is referenced from
      the test project only, `PrivateAssets="all"`, as a struct-layout oracle, and
      nothing it produces ships — which **sidesteps the question rather than
      answering it**. Whether those terms create a notices obligation for *shipped*
      generated code is not assessed and **must not be asserted either way** until
      it is.

      A second cost, recorded because it is real: CsWin32 pins three **prerelease**
      transitive packages, the only prerelease versions anywhere in the repository.
      They are build-time only, so the *GA is a hard floor* rule is not violated in
      the artifact.

- [ ] **Widen the invisible-source check beyond `*.cs`, or decide not to.**
      `BuildConfigurationTests.NoSourceFileIsInvisibleToGit` lists every `.cs`
      under `src/` and `tests/` against `git ls-files`. It exists because an
      unanchored `artifacts/` rule from the upstream template matched a real
      product directory on case-insensitive Windows and **five source files were
      ignored while the build, the suite and `git status --porcelain` all read
      green.** **Nineteen unanchored directory rules remain** in the upstream half
      of `.gitignore` — `[Ll]og/`, `[Oo]ut/`, `[Rr]elease/`, `[Oo]bj/`,
      `Generated Files/` and the rest — so a source folder named `Logs\`, `Out\` or
      `Release\` would be swallowed the same way, and a folder holding only data
      would not be caught at all. Either widen the check to *any* file under `src/`
      or `tests/` that git ignores and that is not under `obj\` or `bin\` — the
      query returns nothing today, so it lands green — or decide the `.cs` scope is
      enough because a source folder containing no source is not a source folder.
      **Decide it rather than leaving it implied.**

- [ ] **Make the marker entry adjudicate what moved — at the first real bump, not
      before.** [The gate](TESTING.md#what-the-marker-records) requires each
      [`upstream-review.json`](upstream-review.json) entry to gain `snapshots` (per
      snapshot: `unchanged`, or an adjudication) and `reverification` (an outcome
      for every *manual* row, by name), with a test asserting the entry matches
      what the build observed. Everything else in that section is built and this
      deliberately is not: **at a baseline there is nothing to adjudicate**, so
      satisfying the test today means typing an adjudication of no change for four
      snapshots and an outcome for roughly forty manual rows — a review that did
      not happen, written to make a suite green, which is the one act
      [the procedure](UPSTREAM-REVIEW.md) exists to forbid. The marker test fires
      on exactly the event that makes it writable.

- [ ] **Decide whether relayed notifications need their order preserved.** The
      child→caller progress relay preserves the `progressToken` and the params byte
      for byte, and **does not preserve order**: the SDK dispatches inbound
      notifications fire-and-forget, and two `notifications/progress` written in
      order were observed arriving as 2 then 1
      ([kb](kb/mcp/sdk.md#lossless-passthrough-cancellation-notifications-and-error-frames)).
      It cannot be fixed from a notification handler — the reordering has already
      happened by the time one runs — so a fix means the `IClientTransport`
      decorator [deviation 7](STACK.md#nine-places-where-the-sdk-must-be-deviated-from)
      describes, which sees messages in wire order. Settle two things first:
      whether `@playwright/mcp` emits progress at all (not measured), and whether a
      caller rendering a jumping progress value is a defect worth a component.

- [ ] **Three `.gitignore` items are owed.** *(a)* Re-fetch the upstream
      `VisualStudio.gitignore` half wholesale and replace it in one paste — never
      merge it by hand; everything below the marker comment is ours. *(b)* Settle
      `.vscode/mcp.json`, which the template's `.vscode/*` rule ignores: for a
      project that **is** an MCP server, a workspace registration used for testing
      would be silently untracked. Upstream
      [github/gitignore#4735](https://github.com/github/gitignore/pull/4735)
      proposes fixing it and has been open since 2025-09-23; if it has not merged,
      add `!.vscode/mcp.json` below the marker. *(c)* `/staging/` and `/.staging/`
      were inferred from a predicted install layout and nothing emits them — keep
      them deliberately or delete them, but not by accident.

- [ ] **`browserai_init` still refuses `browser: "firefox"`.** Everything else is
      already family-parameterised — provisioning, the config generator, the launch
      preflight and the sweep all read the family from the session's own
      `lock.json`, so a record naming Firefox is honoured on `resume` rather than
      silently run as Chromium against a Firefox profile. Two things are owed:
      **(a)** the error-catalogue row for a browser that is still downloading
      quotes a download size, and the only one measured is Chromium's — naming it
      for a Firefox install would be a measured-looking number that was never
      measured; **(b)** `browserai_reinstall_browser` takes no arguments *because
      there is nothing to name*, which stops being true with two browser trees on
      disk.

- [ ] **Review the *no automated checks* decision once the product is finished.**
      The [release checklist](RELEASING.md) is the only gate that exists; it works
      when it is invoked, and nothing makes it fire. That trade is right while the
      suite's shape and the release cadence are predicted rather than observed —
      many commits without re-running everything, and no hosted CI. **Re-open it
      against the finished product and a real cadence, not against a guess about
      them.** The condition that ends the arrangement is already named in
      [the release gate](RELEASING.md#the-release-gate): the day a second person
      can cut a release, the assumption breaks and the gate has to move into
      automation.
