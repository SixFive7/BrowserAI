<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# TODO

Central work list for BrowserAI. One file, so nothing survives only as a comment
somewhere.

**What belongs here:** work settled in intent but not yet done.

**What does not:** open design questions and known hazards. Those live in the
README under [Open design decisions](README.md#open-design-decisions), and in the
[hazard index](HAZARDS.md#hazard-index). An item moves here once the decision
behind it is made, and is **deleted** when it is done — `git log` is the record of
what was done; this file is the record of what is not.

**Format:** `- [ ] **Title.** Why it matters, then what to actually do.` A claim
about an external source needs the date and version it was true at.

---

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

- [ ] **Audit the repository for justifications stated as fact and never
      measured.** Every mechanism here protects claims about *behaviour* — a test
      fails, a snapshot diffs, an analyzer errors. A claim about a **reason** is
      invisible to all of them.

      The instance that surfaced it: *"`[DllImport]` relies on runtime IL-stub
      generation that NativeAOT does not do"* is false on Windows — ILC compiles
      those stubs ahead of time, re-measured with a 38-declaration probe that
      published with zero warnings and ran correctly. The rule it justified was
      **right**, so nothing behaved wrongly and nothing could be red, and it was
      copied into three source files precisely *because* it sounded like the kind
      of thing worth writing down. It matters even though the conclusion survives:
      a reason is what the next person reasons *from*, and anyone asking whether a
      generated `[DllImport]` library is viable under AOT would have concluded
      "impossible" and stopped.

      **What to do.** Sweep the load-bearing justifications — the `<remarks>`
      blocks and MSBuild comments explaining *why* a rule exists, especially where
      they assert a platform or toolchain behaviour — and sort each into
      **measured here**, **cited to a source**, or **assumed**. The third bucket
      is the finding. Two instances are already corrected; the sweep is to find
      out whether they were the only two. Start with
      [`src/BrowserAI/Interop/`](src/BrowserAI/Interop), `Directory.Build.props`
      and [`BrowserAI.csproj`](src/BrowserAI/BrowserAI.csproj), where toolchain
      reasoning is densest.

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

- [ ] **54 rows of the [hazard index](HAZARDS.md) read `open` with `—` for
      evidence.** The file's rule is that a row marked `closed` with `—` is not
      closed; this is the converse — rows nobody has adjudicated either way. By
      category: Bundling and AOT 14, Child runtime and configuration 10, Process
      and OS 9, Tooling and CI 7, Protocol and SDK 7, Packaging and updates 4,
      Handle routing and lifetime 3. Three more read `open` while carrying
      evidence, so 57 are open in total. Many will close against tests that now
      exist; some are upstream behaviours that cannot close at all and should say
      so. **An honest `open` with a reason beats a `closed` with a weak one.**

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
