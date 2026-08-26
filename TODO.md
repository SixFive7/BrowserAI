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

## Fingerprinting

- [ ] **Measure fingerprint parity against a normal user browser.** The
      maintainer's requirement, verbatim: *"I would like the web servers to not
      see the difference."* Nobody has established what the difference currently
      is, so there is nothing yet to decide about — this item is the measurement,
      not the fix.

      **What is already measured**, 2026-08-19, and it is a narrow slice:
      Chromium's user agent differs by headedness —
      `Chrome/152.0.0.0` headed against `HeadlessChrome/152.0.0.0` headless —
      while `navigator.webdriver` is `false` in both; Firefox's user agent is
      **byte-identical** across headedness and `navigator.webdriver` is `true` in
      both. So the two families fail parity in opposite places, and neither is
      covered by fixing the other.

      **What is NOT measured and belongs in this item.** Each of these is a
      channel a server can read, and none of them has been looked at even once:

      - **TLS, and JA3/JA4 in particular.** The ClientHello a Playwright-launched
        browser sends, against the one the same build sends when a human starts
        it. This is the channel a user-agent string cannot influence at all.
      - **The automation command-line flags.** Playwright launches with a large
        argv of its own; what matters is which of those flags are *observable
        from the page* — through feature detection, through an absent or present
        API, or through behaviour — rather than the argv itself.
      - **Screen and window metrics.** `screen.*`, `window.outer*`, the
        device pixel ratio, and what a headless browser reports for a screen it
        does not have.
      - **Canvas, WebGL and font fingerprints.** Whether the provisioned build's
        rendering path produces the same hashes as an installed browser on the
        same machine, headed and headless.
      - **Absent extensions.** A normal profile has some; a provisioned one has
        none, and upstream additionally launches with `--disable-extensions`.
      - **Empty history and a fresh profile.** Zero visited links, no
        autofill, no service workers, and a `localStorage` a site has never
        written to. **BrowserAI's profiles do persist** across a resume — that
        was corrected on 2026-08-19 — so this is a *first-run* difference rather
        than a permanent one, and its shape over a session's life is part of the
        measurement.

      **How to do it:** run one page against a real installed browser and against
      each of BrowserAI's four combinations (two families × headed and headless)
      on the same machine, capture every channel above, and diff. **The control is
      the installed browser** — a difference between two BrowserAI configurations
      says nothing about what a server sees. Results go in
      [`kb/chromium/fingerprinting.md`](kb/chromium/fingerprinting.md), which
      already holds the call-site inventory and the 486-field differ result and is
      where the rest of this belongs.

      **Decide nothing until it is measured.** The low-hanging half — whether the
      user agent and `navigator.webdriver` can be set through the generated child
      config — was researched separately on 2026-08-19 and is a question for the
      maintainer rather than an item here.

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

⚠️ **The last twelve were triaged on 2026-08-23, and this is what came out of
it.** `locking B3, B4, B5` and `processes 4, 6-14` had sat unread since the day
they were written. Reading them produced **two fixes** — B3's shared mutex
namespace and processes 8's title guard, both cheap only *because* the tree had
moved underneath them — **three declines with reasons**, **one finding closed by
work that had landed since**, and **seven hazard rows**. Two of those seven were
below, because their remedy was a decision somebody had to take rather than a
change somebody had to make; **both have since been taken, and neither is below
any more**. The other five are hazards and nothing else; they are in the
[index](HAZARDS.md#hazard-index) and not here, because this file is work settled
in intent and they are not settled.

⚠️ ***Corrected 2026-08-24 (previously "Two of those seven are below … The other
five are hazards and nothing else").*** The second item was *"decide whether
`SessionLock` gets a per-session lock"*, and it is decided: the maintainer took
direction 1 — a per-session lock every mutating path and both disposal paths take
— and it shipped the same day with a **deterministic** same-process interleaving
behind it, which is the thing that item recorded as the reason to doubt direction
1 at all. **Five of the seven have now closed** *(previously "Three")*, the last
two of them in the change the note below records; the row is `closed` in the
[index](HAZARDS.md#hazard-index) and the reasoning is in
[`docs/reviews/`](docs/reviews/README.md).

⚠️ **The other is gone too, taken 2026-08-24, and for that one it is worth
saying how rather than only that.** *Previously "**Decide what a torn log record
should do**", with four directions and a recommendation of the first —
throw on a partial write.* **None of the four was chosen.** All four repaired a
completion loop whose premise was that the shared log is written lock-free; the
maintainer replaced the premise. Every write to that file now takes a
cross-process byte-range claim, so there is no per-call size bound to exceed and
nothing that can interleave — the torn record is dissolved rather than made loud,
and neither the truncation nor the new record-length limit any of the four
directions cost was needed. [Hazard row](HAZARDS.md#hazard-index), closed;
[review](docs/reviews/2026-08-18-adversarial-processes.md) finding 9. The
`FILE_SHARE_DELETE` row beside it closed in the same change.

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

- [ ] **The justification sweep's residue: 28 assumed justifications named and
      not settled.** ⚠️ ***The predicate is one per italicised or named claim in
      the three lists below, counted 2026-08-19 after the ones settled since.***
      *The item said **24** when it was written on 2026-08-18 and did not state
      what it was counting, so this is a **different question over the same
      list** rather than a correction of it — which is the trap this repository
      has already fallen into once and now has a rule against. The list is the
      artefact; the number is derived from it and must be re-derived, never
      decremented.* The sweep ran 2026-08-18; what it *settled* is in `git log`
      rather than here, and what it did not is below. Three read-only inventories
      plus a first-hand pass over [`Interop/`](src/BrowserAI/Interop), the build
      files and [`build/`](build) examined **598 load-bearing justifications**:
      **309 measured here**, **226 cited to a source**, **63 assumed**. Of the
      63, **13 were settled by measurement and 11 relabelled** in the same pass,
      **and two more were measured later the same day — 15 settled in all**. The
      rest, each stated as fact, load-bearing, undated and uncited, and each
      cheap:

      ***Corrected 2026-08-18 (previously "the four highest-value remainders
      have their own items above")*** — **two of the four are now measured and
      their items are deleted**, which is what this file's own rule asks for.
      `browser_annotate`'s window and its unbounded wait went to
      [kb](kb/playwright/tools-and-artifacts.md#what-browser_annotate-actually-does--measured-2026-08-18),
      cited from `SessionToolPolicy.IsWithheldFromTheSurface` — the citation moved
      there when the refusal became a withholding — and covered by re-verification
      row 94; the DPAPI claim that removed the permission layer went to
      [kb](kb/chromium/profiles.md#chromiums-cookie-store-and-what-it-takes-to-read-one--measured-2026-08-18),
      cited from all six documents that used to cite each other, and covered by
      row 95. **Both confirmed the decision they had been holding up**, which is
      the outcome that makes an unmeasured justification easiest to leave
      standing and is exactly why they were the two worth taking first. The two
      remainders with items above are the browser-reinstall rename and
      headless-with-storage.

      ***A third is done, 2026-08-19.*** `LongPathsEnabled` is now recorded where
      the long-path guarantee is claimed — read off the reference machine as `1`
      (`REG_DWORD`) on Windows 10.0.26200 and stamped `[MACHINE]`
      ([kb](kb/toolchain.md#what-a-nativeaot-publish-emits)) — with the half that
      is still unknown named rather than implied: **nothing has run against
      `LongPathsEnabled = 0`**, and the product makes no check and emits no
      diagnostic that would name it.

      **In [`kb/`](kb/README.md)** — a measurement store, so an assumed entry
      there is a category violation. *"The damage is a lost session rather than corruption"* is why nothing
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

      ***Two of those are done, 2026-08-19.*** The *"39 binaries"* sentence no
      longer asserts Firefox's measured count of Chromium — Chromium's is
      unmeasurable while the check does not run at all, and saying so is the
      honest form; it gained a [hazard row](HAZARDS.md#hazard-index), because
      offering `browser: "firefox"` moved the measured half onto a shipped path.
      And the Firefox provisioning pair was **re-measured rather than adjusted**:
      it had been the Firefox archive and directory alone beside Chromium's
      whole-run figures, which is a different predicate wearing the same units.

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

- [ ] **Answer the CsWin32 metadata licence question before any move into
      `src/`.** The generator is MIT; **the metadata it generates from is not.**
      `Microsoft.Windows.SDK.Win32Metadata` and `WDK.Win32Metadata` ship under
      Windows SDK licence terms and `SDK.Win32Docs` under
      `aka.ms/WinSDKLicenseURL` — all three carrying **no SPDX licence expression
      at all** on nuget.org, checked 2026-08-17 and **re-verified 2026-08-19**.
      Generated code, doc comments included, compiles into whatever references it.
      Today it is referenced from the test project only, `PrivateAssets="all"`, as
      a struct-layout oracle, and nothing it produces ships — which **sidesteps the
      question rather than answering it**. Whether those terms create a notices
      obligation for *shipped* generated code is not assessed and **must not be
      asserted either way** until it is.

      ⚠️ **Half of this is done as of 2026-08-19, and the half that is left is not
      ours to do.** The terms have been fetched, quoted verbatim with their URLs and
      fetch date, and turned into five ordered questions — [QUESTIONS.md
      §12](QUESTIONS.md#12-the-cswin32-metadata-licence--moot-2026-08-20-and-the-entry-stays). What that gathering found and this bullet did not
      know: the two metadata packages ship the **same byte-identical** Windows 10
      SDK EULA (`EULAID:WIN10SDK.RTM.AUG_2018_en-US`), while `win32metadata`'s own
      `README.md` says `Windows.Win32.winmd` — the only file CsWin32 reads — is
      **MIT**. The package's declaration and the repository's declaration disagree
      about the same file, and that disagreement is now the first question rather
      than an unknown. **This item stays open**, because what remains is a legal
      reading and nobody here may supply one.

      A second cost, recorded because it is real: CsWin32 pins three **prerelease**
      transitive packages, the only prerelease versions anywhere in the repository.
      They are build-time only, so the *GA is a hard floor* rule is not violated in
      the artifact.

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
      describes, which sees messages in wire order.

      ✅ **The first of the two things is settled, 2026-08-19: it emits none.**
      All four occurrences of `notifications/progress` in the shipped payload are
      the MCP SDK's own schema and capability arms; `sendNotification` appears
      once, as the capability handed *to* a tool handler, and nothing in
      `@playwright/mcp` or `playwright-core`'s MCP layer calls it
      ([kb](kb/mcp/sdk.md#lossless-passthrough-cancellation-notifications-and-error-frames),
      re-verification row 104, with a positive control). **So the defect is real
      and unreachable through this product's child**, and the decorator would be
      a component built for a notification nobody sends.

      ⚠️ **What is left is a decision and only a decision**, and it is now a
      cheaper one: whether to build ahead of the bump that makes this reachable,
      or to let row 104 be the thing that re-opens it. Nothing further to
      measure.

- [ ] **Review the *no automated checks* decision once the product is finished.**
      The [release checklist](RELEASING.md) is the only gate that exists; it works
      when it is invoked, and nothing makes it fire. That trade is right while the
      suite's shape and the release cadence are predicted rather than observed —
      many commits without re-running everything, and no hosted CI. **Re-open it
      against the finished product and a real cadence, not against a guess about
      them.**

      ⚠️ ***Corrected 2026-08-20 (previously "Half the premise expired on
      2026-08-18, and this item did not notice. **There is hosted CI**:
      `build.yml` runs the whole suite, `SaturationTests` included, on every push
      and every pull request"). The premise expired and then came back.*** Hosted
      CI existed for two days, 2026-08-18 to 2026-08-20, and was removed at the
      maintainer's decision. **Both `previously` clauses are here on purpose:** a
      reader who learned either state needs to know it was reviewed and replaced
      rather than lost, and this entry has now been wrong in both directions
      within three days. The original sentence is true again — the release
      checklist is the only gate that exists and nothing makes it fire — so what
      is left to decide is exactly what it always said, and the *whole* of it
      rather than the narrowed remainder. **Nothing here is a task; the whole
      remainder is the decision.** The condition that ends the arrangement is
      already named in [the release gate](RELEASING.md#the-release-gate): the day
      a second person can cut a release, the assumption breaks and the gate has to
      move into automation. Bringing CI back is
      [its own item](#continuous-integration), and it is not this one.

## Upstream asks

- [ ] **Ask `@playwright/mcp` for an option that emits absolute paths in tool
      results.** Every file the child produces is named in the answer with a path
      relative to the child's working directory, and the six shapes all come from
      two call sites in `Response`. A client that is not a process with a working
      directory cannot resolve those, which is every LLM reading a tool result.
      There is no configuration key for it today; verified against the shipped
      bundle at `@playwright/mcp` 0.0.79 / `playwright-core`
      1.63.0-alpha-2026-08-05, 2026-08-25.

      ⚠️ **BrowserAI no longer works around it at all, 2026-08-26** *(previously
      "BrowserAI works around it by naming every artifact absolutely in its own
      result note and saying so in one sentence, which is a workaround rather
      than a fix")*. The result note is deleted with artifact routing: every
      answer is the child's own bytes, so those six relative pointers now reach a
      model unaccompanied. **That makes the ask stronger rather than weaker** —
      there is no workaround left to weigh against it — and it is the one thing
      an upstream option would fix that nothing on this side can.

      **File this text, unchanged:**

      > **Title:** Option for absolute paths in tool result links
      >
      > Tool results name generated files with paths relative to the server's
      > working directory, for example `- [Snapshot](./page-2026-08-25T09-14-22-104Z.yml)`.
      >
      > A client that is not a process with a working directory cannot resolve
      > these. This is the normal case when the consumer is a model rather than
      > a shell.
      >
      > Request: a config option such as `pointerPaths: "absolute" | "relative"`,
      > default `relative`, that makes the path returned by
      > `Response._computeRelativeTo` absolute.
      >
      > It would cover the screenshot, PDF and storage-state links, the snapshot
      > link, the console log link, the download line, binary response body
      > lines, and the trace links. They all resolve through the same two call
      > sites.

---

## Continuous integration

- [ ] **Bring CI back — but not on GitHub Actions by default, and not before the
      infrastructure exists.** Removed 2026-08-20. **The maintainer's decision,
      verbatim:** *"Remove CI completely. Let all the tests run on my machine
      only. I want no CI and no github runner. Add to the todo that we will add CI
      back in later. But that requires me adding infrastructure for self-hosted
      runners and I am considering leaving github before we do so. Double check we
      do not lose anything unique only build in the CI before removing it."*

      **Two preconditions, and the second is why this item must not assume a
      provider.** It needs **self-hosted runner infrastructure** that does not
      exist yet, and the maintainer is **considering leaving GitHub before that
      happens** — so whatever is written must be portable, and a `.github/`
      directory is a guess about the answer rather than a step towards it. What
      was deleted is recoverable in full from
      `git show 7f296b2:.github/workflows/build.yml` if the answer does turn out
      to be Actions; it is a good specification of the steps whatever ends up
      running them.

      **What is unverified anywhere while CI is gone.** This is the item's real
      content — the audit taken on the day of removal, split by whether the thing
      dies, is preservable locally, or was already covered locally.

      | What CI did that a local `dotnet test` does not | Verdict |
      |---|---|
      | **A different machine — four cores, cold caches, a service window station with no interactive desktop, and a volume with 8.3 generation off.** It found four defects a developer machine structurally could not: the `browserai_destroy` survivor arm (nine local greens against three consecutive CI reds, Firefox still holding mapped files); the `SessionLock` re-open sharing violation (run `32203064556` attempt 1), whose fix is specified and deliberately not yet made; a `RenameWindow` `ERROR_SHARING_VIOLATION`; and the console-logger queue drain, which cost two red runs and is invisible on a machine fast enough to drain the queue before the kill | **Dies.** Not preservable. This row is the whole of the loss and the rest of the table is bookkeeping |
      | **A contributor's pull request, built before merge.** For a public repository this was the workflow's founding reason: 54% of this project's enforcement is a test or a release-phase check, and a pull request could break any of it with nothing to say so | **Dies.** No local substitute exists — a maintainer running the suite on his own machine cannot run it on a change he has not pulled |
      | **`BROWSERAI_EXPECTED_ABSENT`, the capability pin.** The workflow's test step was its only consumer anywhere in the repository | **Dies as a declaration; the mechanism is kept, correct and inert.** Unset means *declares nothing*, which is already the developer-machine behaviour, so `SuiteEnvironment.ReconcileDeclaredAbsence` stays right and `SuiteCoverageTests.EveryAbsentCapabilityIsOneThisRunsEnvironmentDeclared` now asserts nothing on every run. **Restoring the third arm is part of this item:** `TheWorkflowStillDeclaresWhatItExpectsToBeAbsent` read `build.yml` and was deleted rather than re-pointed, because a version aimed at a file that does not exist can have no positive control |
      | **The `SessionDirectoryGuardTests` branch for a volume with 8.3 generation *off*.** CI's checkout volume had it off; this machine's system volume has it on | **Preservable locally, and nothing routine does it.** Three of this machine's four volumes do not shorten, so running the suite from one exercises the other branch. Until something does, [re-verification row 98](kb/re-verification.md) is verified on one branch per run rather than both |
      | **The cold CDN download on every push** — Chromium ~203.8 MB and Firefox ~125.7 MB, uncached on purpose | **Already covered locally, at a lower cadence.** `FirstRunProvisioningTests` runs against an empty root, `FirstRunCache` asks the CDN at most once an hour, and a release run always asks it. What dies is the per-push frequency and a second, independent network path to the CDN |
      | **`dotnet restore --force-evaluate` then `--locked-mode`, and the lock-file drift report** | **Already covered locally, and more strictly.** [Release checklist item 1](RELEASING.md#1-everything-re-resolved-to-latest-and-green) runs both commands and takes both diffs with `--exit-code`, which the workflow's bare `git diff` did not |
      | **`fetch-depth: 0`, so MinVer derives a real version from tags** | **Already covered locally.** A developer clone carries its tags; `New-Release.ps1` refuses a derived `0.0.0` and names this exact fix in its own error text |
      | **The suite-coverage block, published to the job summary** | **Already covered locally.** `SuiteCoverage.ReportWhatThisRunExercised` writes it to the real stdout handle and to `.work/suite-coverage.txt` on every run; only the rendering died |
      | **`upload-artifact` keeping `TestResults/`** | **Already covered locally** — they are on disk |
      | **`dotnet --info`, core count and OS recorded per run** | **Dies, and it is worth nothing without the different machine above** |
      | **Every step under `pwsh`** | **Not a loss — it was a weakness.** A single-shell run bakes in the drive-letter casing that happens to agree, which is why that defect was reported twice from a machine and never once from a build. The gate that replaced CI runs the suite from **both** PowerShell and Git Bash, which is strictly stronger |
