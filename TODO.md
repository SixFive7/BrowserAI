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

- [ ] **The justification sweep's residue: 27 assumed justifications named and
      not settled.** ⚠️ ***The predicate is one per italicised or named claim in
      the three lists below, counted 2026-08-26 after the ones settled since.***
      *Re-counted rather than decremented: the three lists hold 29 named claims,
      two of which the notes below mark done, which is 27. **Previously "28
      assumed justifications … counted 2026-08-19"**, and what left the list is
      named where it left it — the filename refusals, in the `src/` list.*
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
      cited from `browser_annotate`'s own `deny` row in `tool-verdicts.json`
      ***(corrected 2026-08-26, previously "cited from
      `SessionToolPolicy.IsWithheldFromTheSurface` — the citation moved there when
      the refusal became a withholding")*** — and covered by re-verification
      row 94; the DPAPI claim that removed the permission layer went to
      [kb](kb/chromium/profiles.md#chromiums-cookie-store-and-what-it-takes-to-read-one--measured-2026-08-18),
      cited from all six documents that used to cite each other, and covered by
      row 95. **Both confirmed the decision they had been holding up**, which is
      the outcome that makes an unmeasured justification easiest to leave
      standing and is exactly why they were the two worth taking first. ⚠️ *The
      sentence that followed is corrected 2026-08-26 (previously "The two
      remainders with items above are the browser-reinstall rename and
      headless-with-storage").* **Neither has an item above any more.** The
      browser-reinstall rename shipped, and headless-with-storage was settled by
      [the charter](DECISIONS.md#what-browserai-does-not-defend-against): the
      reason it was refused on was a security reason of exactly the class that is
      now a stated non-goal, and there is no session mode left to refuse in any
      case.

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
      after 2 s · `win` as Velopack's default channel · and *"a rollback always
      reports zero"*.

      ***A fourth is off the list, 2026-08-26, and it left in two pieces.*** The
      entry was *"the `NUL.png` and trailing-dot filename refusals"*, in
      `ArtifactFilename`. **That refusal is deleted** with the whole `filename`
      gate, and what it protected against is now an
      [open hazard row](HAZARDS.md#hazard-index) rather than an assumed
      justification. **The same claim about a session DIRECTORY is now measured**
      rather than assumed: `CanonicalPath` refuses a trailing dot, a trailing
      space, a reserved device name, an alternate data stream and a wildcard
      before `Path.GetFullPath` can rewrite them, and `C:\work\NUL` was measured
      coming back as `\\.\NUL`. So the justification did not survive by being
      re-argued; the question it stood on was answered on one path and deleted on
      the other.

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

## Residue outside the app root

- [ ] **Find out whether BrowserAI can reap the browser descriptors Playwright
      leaves in `%LOCALAPPDATA%\ms-playwright\b\`, and whether reaping them is
      safe.** Every browser this product launches leaves one JSON file there
      naming the `playwrightLib` path, the window title and the whole
      `launchOptions`, and **nothing BrowserAI runs ever removes one**. Measured
      2026-09-16 on the maintainer's machine: **26,891 files, 44,652,496 bytes
      (42.6 MiB)**, oldest 2026-08-14, growing by roughly a thousand a day of
      running the suite. Every record names this repository's own payload and a
      `downloadsPath` under `.work\test-scratch`, so today it is the **suite's**
      residue rather than any install's — but the same code runs in the shipped
      product, so a heavy user accumulates the same thing with their own paths in
      it. [Hazard row](HAZARDS.md#hazard-index) ·
      [kb](kb/playwright/tools-and-artifacts.md#every-launched-browser-leaves-a-descriptor-in-localappdatams-playwrightb-and-nothing-reaps-it--measured-2026-09-16).

      ⚠️ **The cheap answer has already been checked for and is not
      there.** The directory is `defaultCacheDirectory()` + `ms-playwright\b`,
      and `computeDefaultCacheDirectory()` on Windows reads **`LOCALAPPDATA` and
      nothing else** — read in `coreBundle.js` at `playwright-core`
      1.64.0-alpha-2026-09-14. `PLAYWRIGHT_BROWSERS_PATH` does not move it and no
      other variable does, so the suite cannot point it at scratch and this is
      not a one-line fix.

      **What to actually do.** (1) Establish whether `ServerRegistry.list()` —
      which unlinks every descriptor it cannot connect to — is reachable from
      anything BrowserAI already calls, or whether the MCP child could be asked
      to run it at a point where it is about to exit anyway. (2) Establish what
      it would reap: the unlink is keyed on *cannot connect*, which is a
      machine-wide judgement rather than a session-scoped one, so a peer's live
      descriptors are the thing to prove safe before anything is called. (3) If
      neither is safe, decide whether BrowserAI removes **only the guids it
      launched itself**, which it knows, and where that would hook — the same
      place the session's browser is closed. **Never a wildcard sweep of somebody
      else's cache directory**: this product's rule about never acting on a path
      it does not own applies to a directory as much as to a process.

      **A plantable test exists for the outcome, whatever it is**: a product run
      that leaves nothing new under that directory, with the count taken before
      and after.

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

      Filed: https://github.com/microsoft/playwright-mcp/issues/1725 (2026-08-27)

      ⚠️ **TRANSFERRED BY UPSTREAM, and the watch item below fired on it.**
      *Added 2026-09-14; the line above is left standing because it is where the
      trail starts.* It is now
      [microsoft/playwright#42497](https://github.com/microsoft/playwright/issues/42497),
      retitled *"[MCP] Option for absolute paths in tool result links"* — **the
      move was upstream's own doing rather than ours**, which is the one outcome
      the watch item's options did not name.

      ✅ **GRANTED AND MERGED — *corrected 2026-09-17 (previously "**OPEN and
      TRIAGED**: `dgozman` asked for a repro on 2026-09-02 and again on
      2026-09-03, a second reporter joined the same day, and
      [PR #42673](https://github.com/microsoft/playwright/pull/42673) —
      *\"feat(mcp): add `--file-paths=absolute` for absolute paths in tool
      results\"*, by `pavelfeldman`, +67/-6 over 7 files — is **open and
      configured to close it**. Read 2026-09-15: still open, `mergeable_state`
      unstable, last touched 2026-09-14")*.**
      [PR #42673](https://github.com/microsoft/playwright/pull/42673) was merged
      by `pavelfeldman` at **2026-09-16T15:38:22Z**, and
      [#42497](https://github.com/microsoft/playwright/issues/42497) closed
      `completed` one second later, by the merge rather than by a reply —
      **nobody from this side ever answered `dgozman`'s request for a repro**,
      which is worth recording because it is not why it was granted. Read
      2026-09-17 from the API: `merged: true`, `merged_by: pavelfeldman`,
      `closed_by_pull_requests` naming #42673 as `MERGED`.

      ⚠️ **IT IS NOT REACHABLE BY THIS BUILD, AND THAT IS THE WHOLE STATE OF
      THIS ROW.** The flag, the `filePaths` config key and
      `PLAYWRIGHT_MCP_FILE_PATHS` are carried by `playwright-core`
      **1.64.0-alpha-2026-09-17**, which is that package's `next` dist-tag; and
      `@playwright/mcp` `latest` is **0.0.81**, which pins `playwright-core`
      **1.64.0-alpha-2026-09-14 exactly**. Both read 2026-09-17. So there is
      **no drift by the build rule** — the resolver takes the pin out of
      `@playwright/mcp`'s own dependencies, never npm `latest` — and nothing is
      owed until the next `@playwright/mcp` roll.

      **THE ADOPTION PLAN, written now so the roll is a review rather than a
      design**, and [the review procedure](UPSTREAM-REVIEW.md) is what executes
      it:

      1. **Write `filePaths: "absolute"` explicitly in the generated config.**
         Upstream's default is `relative`, and this project's doctrine is
         written-rather-than-omitted: a stance taken by silence is a stance
         nobody can find.
      2. **Put `PLAYWRIGHT_MCP_FILE_PATHS` in `ChildEnvironment`'s refused
         list.** The generator writes the key, so an inherited environment
         variable would be a second, invisible answer to a question the config
         already answers — which is the same argument every other
         `PLAYWRIGHT_MCP_*` refusal rests on.
      3. **`RequiredSessionOpinions`** gains it, so a config that stops carrying
         the key is a red build rather than a silent revert to `relative`.
      4. **Both snapshots move**: `cli-help.txt` for the flag and
         `config-schema.d.ts` for the key, and both are adjudicated under
         [`UPSTREAM-REVIEW.md`](UPSTREAM-REVIEW.md) rather than regenerated.

      **This row stays open until the roll**, because a resolved ask whose fix
      nobody can install is not a closed item — it is a scheduled one.

      ✅ **THE REPLY IS POSTED, AND THE ADOPTION IS NO LONGER WAITING FOR THE
      ROLL.** *Added 2026-09-17; nothing above is retracted.* Two things moved on
      the same day.

      1. **`dgozman`'s request for a repro was finally answered**, under the
         maintainer's own account, at
         [#42497 (comment)](https://github.com/microsoft/playwright/issues/42497#issuecomment-5713988873):
         an apology for the delay, how the server is started, why a relative
         link does not resolve for either the model or the human reading it, and
         a thank-you. 1,187 bytes, plain ASCII, LF only; the posted body was
         fetched back and compared against the approved text. The sentence above
         — *nobody from this side ever answered* — was true when it was written
         and is now history rather than state.
      2. **The pin is being overridden rather than waited out (Q210 = a).** The
         maintainer's instruction was to adopt the fix and re-release `v1.0.0`
         rather than wait for `@playwright/mcp` to roll, so `playwright-core` is
         to be overridden to **1.64.0-alpha-2026-09-17** underneath
         `@playwright/mcp` 0.0.81, as a **dated exception with a written exit**:
         it is deleted the day `@playwright/mcp` `latest` pins that alpha or
         later. That conflicts with *everything floats, never pin*, so it is
         recorded as an exception rather than absorbed, and it is taken through
         [the review procedure](UPSTREAM-REVIEW.md) with the four steps above
         unchanged. **The same roll brings a new tool, `browser_emulate_media`**,
         which needs a verdict before deny-by-default reddens the suite; the
         maintainer answered `allow` (Q209 = a).

      **The row still stays open**, and what it is waiting for has changed: the
      review, not the roll.

- [ ] **Ask `@playwright/mcp` for a no-clobber option on output files, and for
      names Windows will not keep verbatim to be rejected.** Both are losses
      BrowserAI stopped preventing on 2026-08-26, when its own filename gate was
      deleted in favour of upstream's file-access roots: the roots stop a write
      from leaving the session and say nothing about the name inside it. The two
      hazard rows that record the consequences stay open, and steering is what
      this side does about them. Verified against the shipped bundle at
      `@playwright/mcp` 0.0.79 / `playwright-core` 1.63.0-alpha-2026-08-05,
      2026-08-26: `Response._writeFile` calls `fs.promises.writeFile` with no
      flag, so the default `w` truncates, and the name it is handed arrives
      unchecked from `Response.resolveClientFilename`.

      **File this text, unchanged:**

      > **Title:** Option for no-clobber output files and reserved name handling
      >
      > Two requests around output filenames, both in `Response._writeFile`:
      >
      > 1. The write truncates when the target exists, so a second screenshot
      >    saved as a.png replaces the first. An option to fail on collision, or
      >    suffix the name, would avoid silent data loss.
      >
      > 2. On Windows, NUL.png and names with a trailing dot or space are
      >    accepted but stored under a different effective name. Rejecting names
      >    the OS will not keep verbatim would make results predictable.

      Filed: https://github.com/microsoft/playwright-mcp/issues/1726 (2026-08-27)

      ⚠️ **TRANSFERRED BY UPSTREAM AND THEN DECLINED.** *Added 2026-09-14.*
      It is now
      [microsoft/playwright#42496](https://github.com/microsoft/playwright/issues/42496),
      retitled *"[MCP] Option for no-clobber output files and reserved name
      handling"*, and `dgozman` **closed it `not_planned` on 2026-09-08** after
      two comments. **This ask is answered, and the answer is no.** The two
      hazard rows it was steering for keep their `open` status and lose their
      recorded closure path; both say so in their own evidence now. Nothing here
      is to be re-filed: a second report of a declined request is noise, and the
      rows already state what this side does instead.

- [ ] **Ask `@playwright/mcp` to stop returning an empty image when a WebP
      screenshot exceeds WebP's own dimension limit.** `browser_take_screenshot`
      with `type: "webp"` and `fullPage: true` over a document taller than
      **16,383 px** returns a zero-length inline image **and** writes a
      zero-byte file, with `isError: false` — a silent success that produces
      nothing. The bracket is exact: **16,383 px gives a valid 12,284-byte
      VP8X at 1280x16383, and 16,384 px gives 0 bytes**, with `png` (137,816 B)
      and `jpeg` (768,991 B) both fine at the same height. Measured 2026-09-14
      **on the raw child with no BrowserAI process on the path**, and reproduced
      on both binaries the child will drive — the provisioned `chromium-1243`
      153.0.8010.12 and the machine's own Google Chrome 153.0.8010.37 — with a
      live pid-tree walk recording which one each run actually drove. Evidence:
      [`docs/evidence/2026-09-14-webp-ask/`](docs/evidence/2026-09-14-webp-ask/README.md);
      the measurement is in
      [kb](kb/playwright/tools-and-artifacts.md#a-webp-screenshot-past-16383-px-comes-back-as-zero-bytes-with-iserror-false--measured-2026-09-14)
      and the consequence in [HAZARDS](HAZARDS.md#hazard-index). A duplicate
      search was run over both trackers with the term-AND REST API after a
      phrase-matching flaw in the first pass was exposed by its own controls;
      the nearest neighbour is
      [microsoft/playwright#13496](https://github.com/microsoft/playwright/issues/13496)
      (2022, closed), which is PNG tiling above 16,384 px in **headed** mode and
      a different defect.

      **File this text, unchanged:**

      > **Title:** [MCP] WebP screenshot above 16383 px returns an empty image
      >
      > browser_take_screenshot with type webp and fullPage on a document taller
      > than 16383 px returns an empty image and writes a zero-byte file, with no
      > error. 16383 px works, 16384 px does not. png and jpeg are fine at the
      > same heights.
      >
      > This looks like the WebP dimension limit surfacing as success. An error,
      > or a fallback to png above the limit, would make the result honest.
      >
      > Seen with @playwright/mcp 0.0.80, playwright-core
      > 1.63.0-alpha-2026-08-31, Chromium 153.0.8010.12, Windows 11.

      Filed: https://github.com/microsoft/playwright/issues/42717 (2026-09-14)

      **Filed in the monorepo with an `[MCP]` title prefix**, which is where the
      watch item below had already decided asks go — and by the time this one was
      posted the watch item's signal had fired for a second reason, so it was
      never a judgement call. The posted body is byte-identical to the draft
      above (sha256 `9011ed28b20db5e0…`, re-read from the live issue 2026-09-15),
      and the ask text carries **zero non-ASCII bytes**, checked with a control
      that planted U+2014 and U+00A0 and found them.

      ⚠️ **THAT FIX WAS CLOSED UNMERGED, AND THE SETTLEMENT CONDITION IS
      UNCHANGED — *corrected 2026-09-17 (previously "⚠️ **Somebody has already
      opened a fix, 2026-09-15.**
      [PR #42721](https://github.com/microsoft/playwright/pull/42721) —
      *\"fix(screenshot): error when webp dimensions exceed 16383px limit\"*, by
      `mohanram-dev`, who commented that it *\"validat[es] WebP's 16,383px
      specification limit across render scales and guard[s] against silent
      0-byte screenshot buffers\"* — is open and configured to close #42717. It
      is **not** by a Playwright maintainer, so it is a proposal rather than an
      outcome")*.* Read 2026-09-17 from the API: `state: closed`,
      **`merged: false`**, closed **2026-09-16T00:15:20Z** — and the reason is
      the single comment on it, by `dcrousso`, verbatim:

      > this is really an upstream issue and should be fixed there instead (and
      > also i dont think it's really all that likely/common for a screenshot to
      > be that large in the first place)

      **So the path moved and the destination did not.** The fix is now expected
      in **Chromium** rather than in Playwright — CL 8416650, *"DevTools: report
      screenshot encoding failures"*, status **NEW** as of 2026-09-16 — which
      means it arrives through a browser revision bump rather than through a
      `playwright-core` change, and there is no PR on this side to watch any
      more. **What settles this item is exactly what settled it before**: a
      released build in which a 16,384 px webp screenshot errors instead of
      returning empty, which the drift check surfaces and the review measures.
      The proposal being closed is not evidence that the behaviour is
      acceptable; the two hazard rows stand.

      ⚠️ **The second half of `dcrousso`'s comment is a judgement about
      likelihood and is recorded rather than accepted.** This project met it on
      an ordinary full-page screenshot of a long document, which is what the ask
      says.

- [x] **Watch both asks together, and be ready to move them to the monorepo.**
      ⚠️ **THE SIGNAL FIRED, 2026-09-14, and upstream took the decision this
      item was reserving.** *Everything below this paragraph is left exactly as
      it was written on 2026-08-27, because the reasoning is what makes the
      outcome readable — what follows here is the answer arriving, not a
      correction of the question.* **Both issues were TRANSFERRED to
      `microsoft/playwright` by upstream itself**, which is neither of the two
      shapes the "What moves them" paragraph below anticipated: not a close with
      a redirect, and not silence. So the move happened and nothing on this side
      had to do it — no re-filing, and nothing to close behind us, because a
      transfer carries the thread and redirects the old URL. **#1725 →
      [#42497](https://github.com/microsoft/playwright/issues/42497)**, open,
      triaged, with [PR #42673](https://github.com/microsoft/playwright/pull/42673)
      open and set to close it. **#1726 →
      [#42496](https://github.com/microsoft/playwright/issues/42496)**, closed
      `not_planned` 2026-09-08. Each ask's own entry above carries its tracking
      line.

      **What the answer settles, beyond these two.** The tracker question is
      decided by upstream's own action rather than by the balance of evidence
      below: **asks go to `microsoft/playwright` with an `[MCP]` title prefix**,
      which is how the third ask was filed on the same day without this item
      having to be re-read. The "one signal decides for both" premise held
      exactly as written — one transfer event answered both — while the
      *outcomes* diverged, which is the distinction that premise was actually
      making: it was about where a report belongs, never about whether a report
      is accepted.

      **What is left to watch, and it is a different question.**
      [PR #42673](https://github.com/microsoft/playwright/pull/42673) shipping.
      When it lands in a released `@playwright/mcp`, the
      [daily drift check](CLAUDE.md#the-daily-drift-check) surfaces the version,
      [the review](UPSTREAM-REVIEW.md) adopts `--file-paths=absolute` — a CLI
      flag, a `filePaths` config key and `PLAYWRIGHT_MCP_FILE_PATHS`, so the
      `cli-help.txt` and `config-schema.d.ts` snapshots both move and the
      environment allowlist gets a name to judge — and the first ask resolves.
      Until then nothing here is owed: the ask is being implemented by upstream
      and advocacy would only repeat it.

      ✅ **HALF OF THAT HAPPENED — *added 2026-09-17 by addition; the paragraph
      above is left standing because it is the prediction this is the outcome
      of*.** #42673 **merged** 2026-09-16T15:38:22Z and #42497 closed
      `completed`. What has **not** happened is the second clause: it is in
      `playwright-core` 1.64.0-alpha-2026-09-17 (`next`) and **not in any
      released `@playwright/mcp`** — `latest` is 0.0.81, pinning
      1.64.0-alpha-2026-09-14 exactly — so the drift check has nothing to
      surface and the adoption is still owed. The plan for it is written out in
      full under ask #1 above.

      ⚠️ **AND THE NEXT ROLL BRINGS A TOOL WITH IT, WHICH IS A SEPARATE
      DECISION AND A RED BUILD UNTIL IT IS TAKEN.** `playwright-core`
      1.64.0-alpha-2026-09-17 adds **`browser_emulate_media`** — *"Emulate CSS
      media features for the page, for example switch between the light and dark
      color scheme"* — declared `capability: 'core'`, so it is in upstream's
      **default** surface and arrives in `tools/list` the moment the payload
      rolls. That takes `browser_*` from **83 to 84 names with none removed or
      renamed**. [`tool-verdicts.json`](tool-verdicts.json) is **deny by
      default** and refuses a name it has no row for at startup, so the suite is
      red on exactly this until a human judges it — which is the mechanism
      working, not a defect. A verdict is a judgement about this product and
      belongs to the maintainer; it is named here so the roll is not the first
      time anybody hears about it. *(Read 2026-09-17 from upstream's own source
      and from `tests/mcp/capabilities.spec.ts`, which lists it among the core
      tools.)*

      **And what the declined one costs.** #42496 was the recorded closure path
      for the two Q128 hazard rows — reused-filename overwrite, and Windows
      names stored under a different effective name. **That path is gone.** The
      rows stay `open`, the steering stands, and both now say in their own
      evidence that upstream declined rather than that upstream has not answered
      yet. Nothing is re-filed.

      *The record as it stood on 2026-08-27 follows, unchanged.*

      They were filed into a tracker whose owner has asked people not to use it,
      and the call taken on 2026-08-27 was to leave them there and watch rather
      than re-file. **One signal decides for both** — they are two instances of
      the same judgment about the same tracker, filed four seconds apart, so
      whatever happens to either answers the question for the other, and
      watching them apart would only mean taking one decision twice.

      **What moves them.** Either issue closed with a redirect to the monorepo;
      or **both** left untriaged while `microsoft/playwright`'s `[MCP]:` issues
      go on being answered and closed around them. Silence in one tracker while
      the other moves is the same answer as a redirect, given slower.

      **Where they go.** [`microsoft/playwright`](https://github.com/microsoft/playwright/issues),
      one issue each, the ask bodies above unchanged, with **`[MCP]` as a title
      prefix** — that is the convention there, and it is a *title* prefix rather
      than a label:
      [#42363](https://github.com/microsoft/playwright/issues/42363) and
      [#42384](https://github.com/microsoft/playwright/issues/42384) both carry
      it in the title and both carry **no labels at all**, read 2026-08-27. Then
      close [#1725](https://github.com/microsoft/playwright-mcp/issues/1725) and
      [#1726](https://github.com/microsoft/playwright-mcp/issues/1726) with
      links to the new issues, so the trail leads forwards instead of stopping.

      **The evidence, both ways, because it does not point one way.** For
      moving:
      [playwright-mcp#1664](https://github.com/microsoft/playwright-mcp/issues/1664)
      is pinned and open — *"⚠️ Please file issues at
      https://github.com/microsoft/playwright"*, opened 2026-06-29 by a
      Playwright maintainer — and on 2026-08-27 that tracker held **three** open
      issues in total: the notice and these two. And the code both asks name is
      monorepo code already: `_computeRelativeTo` and `_writeFile` appear
      nowhere in the shipped payload except
      `playwright-core/lib/coreBundle.js`, which is where the MCP server's
      `Response` module now ships — the `@playwright/mcp` package beside it is
      `cli.js` and `index.js` over that bundle (`@playwright/mcp` 0.0.79 /
      `playwright-core` 1.63.0-alpha-2026-08-05, read 2026-08-27). **Against
      moving, and this is why they were filed where they were:**
      [playwright-mcp#1716](https://github.com/microsoft/playwright-mcp/issues/1716)
      is ours, filed there on 2026-08-17 — seven weeks *after* that notice — and
      closed as fixed **9 h 52 min later** by a Playwright maintainer. A tracker
      nobody reads does not do that, so the notice is a preference rather than a
      wall, and a first-hand exception ten days old outweighs it until something
      newer says otherwise. ⚠️ **Note which way #1716 cuts:** its fix landed in
      the monorepo, as
      [microsoft/playwright#42288](https://github.com/microsoft/playwright/pull/42288),
      so it is evidence for where the *code* lives and against where the
      *report* has to go.

      **One more to watch beside them.**
      [microsoft/playwright#42384](https://github.com/microsoft/playwright/issues/42384)
      — *`[MCP]:` run-test-mcp-server writes `.playwright-mcp/` artifacts to the
      workspace root instead of the project's configured `outputDir`* — open,
      filed 2026-08-24. It is **adjacent to the first ask and not a duplicate of
      it**: that one is about which directory artifacts land in, ours is about
      the form of the path a tool result names them by. It is worth watching
      because it is the same class of report in the other tracker, so how it is
      handled is the triage signal this item is waiting for.

- [ ] **Watch two upstream moves that would make the network service ask for a
      sandbox our provisioned tree cannot give it — and file the third ask only
      if one of them lands.** Measured 2026-08-29 and recorded in
      [kb](kb/playwright/configuration.md#the-network-service-runs-unsandboxed-and-the-cause-is---disable-field-trial-config-rather-than-anything-about-our-tree--measured-2026-08-29):
      the browser's network service runs unsandboxed at Medium integrity because
      `--disable-field-trial-config` — the first element of upstream's
      `chromiumSwitches` — turns off the field-trial config that would enable
      `NetworkServiceSandbox`, so nothing ever attempts it. **Nothing was
      changed and nothing was filed**, at the maintainer's decision on
      2026-08-29: an ACL grant today changes nothing observable, and an ask
      about a path upstream never takes would be closed as by-design and would
      spend a report we may want later.

      **What arms it, and where each becomes visible.**

      1. **`--disable-field-trial-config` leaving `chromiumSwitches`.** Visible
         in **the payload bundle diff at the next upstream review** — the array
         is in `playwright-core/lib/coreBundle.js`, which is a golden-snapshot
         input, so a `playwright-core` bump that dropped it could not land
         silently. Re-verification [row 121](kb/re-verification.md) is the
         instrument; this item is what it points at.
      2. **Chromium enabling the network-service sandbox by default**, which
         would make the switch irrelevant. Visible as **behaviour rather than as
         a diff**: the `sandbox\policy\win\sandbox_win.cc:804` refusal and the
         `network_service_instance_impl.cc:650` restart start appearing in a
         directly-launched browser's stderr, and a session gains a startup blip
         it did not have. It arrives with a Chromium revision, so the
         `playwright-core` bump that carries a new revision is when to look.

      **What fires then, both halves at once.** The ACL grant at provisioning
      becomes live and **its evidence is already in hand**: two ACEs,
      `ALL APPLICATION PACKAGES` and `ALL RESTRICTED APPLICATION PACKAGES`,
      `(OI)(CI)(RX)` on the browsers root, which is exactly what the
      `Program Files` control carries and what the forced-sandbox arm's
      capability SID matched verbatim. The regression test becomes writable at
      the same moment and not before — today it could only assert that an ACE
      exists, which is a weaker claim than the one anybody wants. And the
      **third upstream ask goes to
      [`microsoft/playwright`](https://github.com/microsoft/playwright/issues)**
      rather than to the MCP tracker, `[MCP]`-prefixed only if it turns out to
      be an MCP-layer question, which on the evidence it is not: the switch is
      `playwright-core`'s.

      **Why this is a watch and not a fix.** The outcome is not
      BrowserAI-specific — stock Chrome's own default on this machine is the
      same unsandboxed network service — and the mechanism is upstream's
      unconditional flag rather than anything this product does. What is ours is
      the *latent* half, and it stays latent until one of the two triggers
      above. The hazard row that records it is `open` and carries its evidence.

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
      | **The `CanonicalPathTests` branch for a volume with 8.3 generation *off*.** CI's checkout volume had it off; this machine's system volume has it on | **Preservable locally, and nothing routine does it.** Three of this machine's four volumes do not shorten, so running the suite from one exercises the other branch. Until something does, [re-verification row 98](kb/re-verification.md) is verified on one branch per run rather than both |
      | **The cold CDN download on every push** — Chromium ~203.8 MB and Firefox ~125.7 MB, uncached on purpose | **Already covered locally, at a lower cadence.** `FirstRunProvisioningTests` runs against an empty root, `FirstRunCache` asks the CDN at most once an hour, and a release run always asks it. What dies is the per-push frequency and a second, independent network path to the CDN |
      | **`dotnet restore --force-evaluate` then `--locked-mode`, and the lock-file drift report** | **Already covered locally, and more strictly.** [Release checklist item 1](RELEASING.md#1-everything-re-resolved-to-latest-and-green) runs both commands and takes both diffs with `--exit-code`, which the workflow's bare `git diff` did not |
      | **`fetch-depth: 0`, so MinVer derives a real version from tags** | **Already covered locally.** A developer clone carries its tags; `New-Release.ps1` refuses a derived `0.0.0` and names this exact fix in its own error text |
      | **The suite-coverage block, published to the job summary** | **Already covered locally.** `SuiteCoverage.ReportWhatThisRunExercised` writes it to the real stdout handle and to `.work/suite-coverage.txt` on every run; only the rendering died |
      | **`upload-artifact` keeping `TestResults/`** | **Already covered locally** — they are on disk |
      | **`dotnet --info`, core count and OS recorded per run** | **Dies, and it is worth nothing without the different machine above** |
      | **Every step under `pwsh`** | **Not a loss — it was a weakness.** A single-shell run bakes in the drive-letter casing that happens to agree, which is why that defect was reported twice from a machine and never once from a build. The gate that replaced CI runs the suite from **both** PowerShell and Git Bash, which is strictly stronger |
