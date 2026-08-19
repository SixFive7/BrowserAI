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
