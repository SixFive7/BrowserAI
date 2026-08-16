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

- [ ] **Give `AnOversizedPayloadArrivesByteIdentical` a reason for taking two
      minutes, or make it stop.** Noticed 2026-08-16 while running the suite for
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
      [pre-release checklist](plan/pre-release.md) is the only gate, it works when
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
