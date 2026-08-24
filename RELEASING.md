<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Releasing

The checklist a release must pass, and the gate it enforces.

**This is the only gate that exists.** No GitHub Actions, no scheduled task, no
git hook. Nothing else stands between a change and a shipped release.

⚠️ **That was briefly untrue and is true again. *Corrected 2026-08-20 (previously
the same sentence, which had been left standing while hosted CI existed).*** A
GitHub Actions workflow ran the whole suite on every push and pull request from
2026-08-18; it was removed on 2026-08-20 at the maintainer's decision, verbatim:
*"Remove CI completely. Let all the tests run on my machine only. I want no CI and
no github runner."* **So this file is once again the entire gate, and it is now
the entire gate for the suite as well as for the release** — which is a stronger
claim than the sentence carried before CI existed, because between those two dates
a push at least built. What was lost by removing it, and what is unverified
anywhere while it is gone, is [the audit in `TODO.md`](TODO.md#continuous-integration).

Every item is **executed and evidenced**, and **any failing item blocks the
release**. A failure is a work item, never a waiver — the response to a breaking
upstream change is to make the newest version work
([rule 4](DECISIONS.md#the-five-rules-that-make-floating-safe)), and where there
is no forward fix, **blocking the release indefinitely is the intended answer.**

**Green is necessary and not sufficient.** This checklist decides whether a
release is *permitted*, never whether one *happens*. A human decides when a green
build becomes a release — [DECISIONS → Release trigger](DECISIONS.md#licence-release-policy-and-the-tool-surface)
and [the release gate](#the-release-gate). Nothing here overrides that,
and item 14 is where it lands.

**This file points; it does not restate — except for the gate, which it owns.**
[The release gate](#the-release-gate) below is the six-step sequence, and this
file is where it lives: a checklist cannot be the only gate that exists while the
sequence it enforces lives somewhere else. [Testing](TESTING.md) keeps the
heading and points here. Everything else still points: the enumerated suite is
Testing's, and where a rule is already specified, the item below names the
evidence to record and links to the rule. **A second copy of a fact in this
repository is a defect.**

### The release gate

**Releases are triggered manually, by a person, through the agent. There is no release pipeline, no scheduled publish, and no auto-merge on green.** That simplification is affordable *only* because the gate itself is mechanical: when to release is a human decision, whether a release is permitted is not.

The sequence, in order, no step skippable:

1. **Resolve.** The build takes the latest of every dependency and records what it got — `packages.lock.json`, the resolved `package-lock.json`, browser revisions from the resolved `browsers.json`.
2. **Build.** NativeAOT (or trimmed self-contained), analyzers at error severity. A warning-as-error is a red build.
3. **Run everything.** All five layers, including the two marked *mandatory before release*. Not a subset, not "the fast ones", not "the ones related to this change". This is also where [the upstream-review gate](TESTING.md#the-upstream-review-gate) fires: if the resolved version moved past the reviewed one, or a snapshot changed without an adjudication, or a manual re-verification row has no outcome, the suite is red and there is nothing to decide at step 5.
4. **Green, or stop.** A failure is a work item, never a waiver. If upstream broke something, the fix is to make the new version work — [rule 4](DECISIONS.md#the-five-rules-that-make-floating-safe).
5. **A human decides.** Green is necessary and not sufficient: a green build is *releasable*, not *released*.
6. **Cut it.** `vpk pack`, publish, and record the resolved set alongside the artifact so the release can state exactly what it contains.

**Why manual is right here, and the condition under which it stops being right.** With one person releasing on a single track, a release pipeline is ceremony around a decision that person makes anyway — and updates are already the most hazard-dense area of this product without adding pipeline-authored releases to them. The honest cost: **the gate is only as good as the person invoking it.** It rests entirely on step 3 being *run* rather than assumed. The day a second person can cut a release, that assumption breaks and the gate has to move into automation.

**What manual does not mean.** It does not mean the suite runs when someone remembers. Steps 1–4 are the ordinary build and run on every build, whether or not a release is in view. Manual governs step 5 alone.


### Evidence, and what does not count

Record, for each item: **what was run, and what it returned.** A version number,
a count, a diff, an exit code, a file size.

What does not count: a restatement of the rule, *"as expected"*, or a date
written from intent. [`drift-check.json`](drift-check.json) already carries
this rule for one field — *a date written from intent reads identically to a real
one and silences the next check for a day* — and it applies to every line of this
file. An item whose evidence is *"I believe this is fine"* is not evidence; it is
worse than a gap, because a gap announces itself.

**Where the evidence goes:** beside the release, with the resolved-set manifest
that [rule 1](DECISIONS.md#the-five-rules-that-make-floating-safe) already
requires. The adjudications in items 3–6 go in the
[`upstream-review.json`](upstream-review.json) entry, which is where the suite
reads them from. Not in this file — this file is the list, not the log.

> **Which items have a command, and which are a judgement.** Items 1, 2, 4, 7,
> 8, 9, 10 and 12 all have something to run — `build/New-Release.ps1` carries 7
> and 12 and part of 9. Items 3, 5, 6, 11, 13 and 14 are read and judged by a
> person, and no amount of tooling changes that: each is an adjudication of what
> a diff *means*, not a check of whether one exists.

---

## Resolve

### 1. Everything re-resolved to latest, and green

The versioning policy is that everything floats and the build freezes it. **A
release may only be cut when everything has been re-resolved to latest and every
check passes.** No pinning to an old version without a deliberate decision to do so.

**NuGet is two steps, and they are mutually exclusive in one invocation:**

```
dotnet restore --force-evaluate     # resolve the float
dotnet restore --locked-mode        # verify what it resolved
```

With a lock file present and no `--force-evaluate`, NuGet **does not re-resolve**
and the float is silently dead ([NU1512](https://learn.microsoft.com/nuget/reference/errors-and-warnings/nu1512);
warned by default from the .NET 11 SDK). **A one-step locked build passes while
resolving nothing** — the `browserName: "chromium"` failure shape, applied to the
build.

The rest of the resolve:

- **npm** — reinstall the vendored tree from the `latest` dist-tag and record the
  resolved `package-lock.json`. `playwright-core` arrives as `@playwright/mcp`'s
  own exact dependency, never npm `latest`.
- **Node** — the newest entry in `nodejs.org/dist/index.json` carrying an `lts`
  field.
- **Browser revisions** — read from the resolved `browsers.json`, never a
  hand-typed URL.

**Evidence:** the two lock diffs, **taken with `--exit-code` so that "no output"
is a recorded `0` rather than an absence** — a bare `git diff` prints nothing
whether it found nothing or was never run, which is the failure shape this whole
file is about:

```
git diff --exit-code --stat -- "**/packages.lock.json"      # the three NuGet locks
git diff --exit-code --stat -- build/payload/package-lock.json   # the npm lock
```

The diff *is* the drift report, and it is the cheapest detector this policy has.
Record both, empty or not; an empty diff is a result. Plus the resolved version
of each of the five upstreams and the browser revision.

> **Corrected 2026-08-16 on the first run of this checklist (previously:
> `git diff -- "**/packages.lock.json"`, and nothing about the npm lock).** Two
> defects. The command has no `--exit-code`, so its evidence is the absence of
> output — indistinguishable from a command nobody ran. And it names only the
> NuGet half, while the item's own body requires the npm tree to be reinstalled
> from the `latest` dist-tag: the committed provenance stamp that reinstall
> writes is `build/payload/package-lock.json`, and it had no line here at all.

> **If this item is doing real work at release time, the working rhythm has
> drifted, and that is itself a finding.** The standing rule is that **updating
> everything is the first step of touching this project, not a step before
> release** — re-resolve, fix the fallout, then do the work. Doing it here for
> the first time is how one upgrade nobody ever takes gets built.

### 2. No pin anywhere

Every package version lives in `Directory.Packages.props` as `*`. A `Version=` on
a `PackageReference`, or a version literal in any `.csproj`, is a pin — and a
pin is invisible once it exists, because a stale number reads exactly like a
current one.

**Evidence:** `BuildConfigurationTests.NoProjectFileContainsAVersionLiteral`, run,
with its output.

---

## Adjudicate

### 3. Upstream drift adjudicated

Resolve the five upstreams **the way the build resolves them** — the table in
[`CLAUDE.md` → the daily drift check](CLAUDE.md#the-daily-drift-check). A
registry query's defaults are not that: on 2026-08-15, npm `latest` for
`playwright-core` was `1.62.1` while the shipping version was
`1.63.0-alpha-2026-08-05`.

**Drift blocks the release.** If any resolved version is newer than the reviewed
one in [`upstream-review.json`](upstream-review.json), run
[`UPSTREAM-REVIEW.md`](UPSTREAM-REVIEW.md) before going further. Finding a
newer version does **not** license editing the marker; the review does.

The marker test enforces this and is red until the entry adjudicates what moved.
**A red marker is not a stale file to fix.** If the diff is large, split it: bump
to an intermediate version, review, land it green, then bump again.

**Evidence:** the resolved-versus-reviewed pair for each of the five upstreams,
and the marker test's result. [`drift-check.json`](drift-check.json) stamped
with `lastChecked` **only after a lookup actually returned a version.**

### 4. The four snapshots adjudicated

`tools-list.json`, `cli-help.txt`, `config-schema.d.ts`, `browsers.json` —
regenerated from the resolved payload and diffed. The mechanism is
[the upstream-review gate](TESTING.md#the-upstream-review-gate); read it there.

**Evidence:** for each of the four, `unchanged`, or the marker entry's
adjudication of exactly what moved. A snapshot that changed without an
adjudication fails the gate, so this item is answered by the suite being green —
what is recorded here is the adjudication text, not a second assertion.

A moved `browsers.json` deserves its own line in the release notes: every machine
re-downloads the browser and re-extracts it. **Updated 2026-08-17 (previously
"and the old revision sits on disk until something prunes it").** Something does:
`RevisionPrune` runs on the next successful provision and reclaims the ~430 MiB
the old revision holds, so what the note has to carry is the download, not the
disk. The one consequence worth a sentence is the other direction — a **rollback**
to the previous build re-downloads 203.8 MB, because the revision it names has
already been pruned.

### 5. Upstream tool-description drift adjudicated

**New, and it closes a gap nothing else covers.** BrowserAI's tool descriptions
are **append-only** on top of upstream's
([DECISIONS → Tool naming](DECISIONS.md#licence-release-policy-and-the-tool-surface)). Upstream can reword
the text underneath ours, leaving our sentence **contradicting or duplicating**
it — and nothing notices, because both halves remain individually valid and the
composed result is only ever read by a model.

`tools-list.json` already carries descriptions, so a rewording **is** a diff
there. **No second snapshot is needed, and adding one would be a second copy of
the same fact.** What this item adds is the adjudication rule:

> **A description-only diff is never cosmetic.** Read the new upstream wording
> and the composed description — ours appended to theirs, exactly as the model
> will see it — together, and record whether our sentence still holds.

The other direction — *ours breaks theirs*, our rewrite dropping warning text a
model relies on — is a test, not a checklist item:
`ModelSurfaceTests.EveryLoadBearingUpstreamPhraseSurvivesOurRewrite` declares the
phrases that must survive the append-only rewrite, per tool. A build gate needs
no evidence here beyond the suite being green.

**Evidence:** for every tool whose description moved, the composed description as
the model will see it, and a yes/no on whether the appended text still reads
correctly beside the new wording.

### 6. The re-verification index answered

[`kb/` → re-verification index](kb/re-verification.md) lists the
measured facts a version bump can silently invalidate — the half of the review no
snapshot can do.

- **Automated rows are answered by the suite** and need nobody.
- **Every manual row must be answered by name, with an outcome**, in the marker
  entry — **for each upstream that item 3 found had moved.** The obligation is
  created by a bump, not by a release.
- **A row that is neither automated nor answered fails the gate**, once a bump
  has put it in play.
- **Where nothing moved, this item is answered by item 3's zero-drift result and
  by `ReVerificationIndexTests` being green**, and the marker carries no
  `reverification` block at all.

**Never update a measured fact by reasoning. Re-run the measurement, or mark the
entry `[STALE]`.** An adjusted number is indistinguishable from a measured one,
which makes it worse than a gap.

**Evidence:** where an upstream moved, the marker entry's `reverification`
block, one outcome per manual row. Where none moved, item 3's resolved-versus-
reviewed pairs plus `ReVerificationIndexTests`' result.

> ⚠️ **Corrected 2026-08-16 on the first run of this checklist (previously:
> "Every manual row must be answered by name, with an outcome, in the marker
> entry", unconditionally).** As written this item could not be evidenced at a
> zero-drift release without doing the one thing the project forbids. There are
> **93 numbered rows** in the index and the great majority are manual, so a
> literal reading demands an adjudication of *no change* for every one of them
> against upstreams that did not move — which
> [Testing](TESTING.md#what-the-marker-records) names exactly: *"a review that
> did not happen, typed out to make a suite green, which is the same act as
> editing the marker to make a test pass."* Testing already scopes the
> `reverification` block to **the first real bump**; this item did not, and the
> two documents contradicted each other. The measurement wins: scoping the
> obligation to what moved is what both can mean at once.

---

## Build and run

### 7. Build clean

NativeAOT publish, analyzers at error severity. **A warning-as-error is a red
build**, and a severity is never weakened to make code pass. ILC output empty.
`UseSystemResourceKeys` never set — it strips the exception messages this project
exists to be able to read.

**Evidence:** the publish command, its exit code, and the warning count, which is
zero — **plus the two things an exit code does not establish**:

- **ILC's own output, read and reported empty.** `build/New-Release.ps1` prints
  `ILC output is clean (<n> lines read, 0 complaints)`; that line is the
  evidence, and the publish's exit code is not, because the failure this exists
  for exited 0 with an artifact on disk.
- **`UseSystemResourceKeys` unset**, quoted from `Directory.Build.props`.

> **Corrected 2026-08-16 on the first run of this checklist (previously: "the
> publish command, its exit code, and the warning count, which is zero").** The
> item's body demands *ILC output empty* and *`UseSystemResourceKeys` never
> set*, and its evidence line asked for neither — so an item that is
> specifically about a publish that exits 0 while ILC complains was to be
> evidenced by that publish's exit code.
>
> ✅ **The `UseSystemResourceKeys` half now has the test
> [Testing](TESTING.md#what-the-build-itself-must-fail-on) requires**, closed
> the same day it was raised:
> `BuildConfigurationTests.UseSystemResourceKeysIsExplicitlyFalseEverywhereItAppears`
> reads every build file, refuses any value other than `false`, **and requires
> the declaration to be present in `Directory.Build.props`** — absent would pass
> a "not true" check while saying nothing to the next reader. Quoting the
> property here is now corroboration rather than the only thing that looks.

### 8. Run everything

All five layers, including the two marked *mandatory before release*. **Not a
subset, not "the fast ones", not "the ones related to this change".** The layers,
their cadences and the enumerated tests are in [Testing](TESTING.md) — this item
does not restate them.

Five things to record rather than assume, because each is easy to skim past.
*Corrected 2026-08-24 (previously "Three things") — the list had reached four
before this and nobody had re-read the number:*

- **The skipped count, which must be zero.** No `Skip`, no quarantine, no
  conditional ignore anywhere in the tree. A `Skip` at release time is a red
  build wearing a disguise, and flakiness is a defect to fix rather than a state
  to tolerate.
- **The suite ran from PowerShell *and* from Git Bash, and both were green.**
  Added 2026-08-20, when CI was removed and this checklist became the only place
  the suite is run. **It is not a preference and not redundancy.** The drive
  letter's case is inherited from the shell that started the test host — `C:\…`
  from PowerShell, `c:\…` from Git Bash — and a run from one shell alone bakes in
  whichever spelling happens to agree. That is not hypothetical: the same commit
  was 484 passed from PowerShell and 484 with **two failures** from Git Bash
  ([kb](kb/windows/detection.md#windows-re-spells-a-paths-drive-letter-a-process-never-re-spells-its-own)),
  and the hosted CI that has now been removed could never have seen it, because
  every step of it ran under `pwsh`. `DriveLetterCase` is the mechanism that makes
  the wrong comparison red from either shell; running both is what catches the
  next defect of this shape before `DriveLetterCase` has been extended to cover
  it. **Record both totals**, not one.

  ⚠️ **And record the `drive letter` row from every run, because until
  2026-08-24 the difference held by luck.** *Corrected 2026-08-24 (previously
  this bullet ended at "Record both totals", with the differing spelling stated
  as a property of the two shells).* On that day's gate **all six runs received
  `C:`** and the gate read exactly as a genuine two-instrument gate reads. Both
  invocations in
  [Testing](TESTING.md#the-two-spellings-are-forced-and-the-run-says-which-one-it-got)
  now force a spelling and declare it in `BROWSERAI_DRIVE_CASE`, a run that did
  not receive what it declared is **red**, and each run's log ends with the
  coverage block that names the spelling it got. Three logs reading `LOWER c:\`
  and three reading `UPPER C:\` is the evidence; six of either is a gate that
  ran one instrument twice.
- **Three runs from each shell, and this is the only place that is owed.**
  *Written down 2026-08-24; the practice is older than the sentence, and its
  absence here is why it was being applied to every intermediate batch as well.*
  A flake that appears once in three is invisible to a single run — which is how
  the probe-report race was found on 2026-08-19, by running the whole suite three
  times in a row — and a release is the one moment where paying six runs for that
  is proportionate. [Ordinary work is one run per shell](TESTING.md#continuous-integration).
- **Every tool in the snapshot, with its schema.** A tool upstream adds, removes
  or re-shapes fails the build, and `upstream-review.json` holds the release
  until a human has adjudicated it. ⚠️ *Corrected 2026-08-18 (previously "**Every
  tool classified.** An unclassified tool fails the build. That rule is what
  turns an upstream addition into a red run instead of a **security incident**").*
  The tool-permission policy was removed — it was never a boundary against the
  caller — and the golden snapshot was doing this job all along, over the schemas
  as well as the names.
- **The smoke layer ran against a real browser**, not against an empty browsers
  directory that would let the batteries-included premise be silently dead code.
  **Run the suite with `BROWSERAI_RELEASE_RUN=1` set**, which is what makes this
  answerable at all — **detached, teed to a log, and the log polled**, which is
  [the shape every run here takes](TESTING.md#how-the-suite-is-run-detached-teed-and-the-log-polled)
  and where the reasons live. ⚠️ *Corrected 2026-08-23 (previously a two-line
  block that set `$env:BROWSERAI_RELEASE_RUN` and then ran
  `tests/BrowserAI.Tests/bin/Debug/net10.0-windows/BrowserAI.Tests.exe` in the
  foreground.)* The variable has to be set **inside the detached shell**, not in
  the one that starts it, or the test host never sees it and the run reports
  `release run  no` while looking exactly like one that did:

  ```powershell
  $log = ".work\suite\release-$(Get-Date -Format yyyyMMdd-HHmmss).log"
  Start-Process pwsh -PassThru -WindowStyle Hidden -WorkingDirectory $PWD `
      -ArgumentList '-NoProfile','-Command',
      "`$env:BROWSERAI_RELEASE_RUN = '1'; dotnet test 2>&1 | Tee-Object -LiteralPath '$log'"
  ```

  ```bash
  log=.work/suite/release-$(date +%Y%m%d-%H%M%S).log
  nohup bash -c "BROWSERAI_RELEASE_RUN=1 dotnet test 2>&1 | tee $log" >/dev/null 2>&1 </dev/null &
  ```

  **The coverage block's `release run` row is the check on this**, and it is why
  it exists: it says `YES` or `no` in every run, so a release cut from a run that
  never saw the variable is visible in its own evidence rather than inferred from
  the command somebody remembers typing.

  Under that variable every capability guard — the published slice, the
  repository payload, a provisioned Chromium, a provisioned Firefox, a packed
  `.nupkg` — is a **failure** rather than a skip, so a release cut from a
  machine that never started a browser is a red run naming what was missing.
  Without it the same guards report **skipped**, which the first bullet above
  already refuses.

  Every run, release or not, ends with a **coverage block** naming each
  capability `PRESENT` / `ABSENT` / `PARTIAL`, every test that took a degraded
  path, and whether this was a release run. It is printed to the run's output
  and written to `.work/suite-coverage.txt`.

  ⚠️ **The same variable is what makes the first-run download real.** Ordinary
  runs download 203.8 MB from Playwright's CDN [at most once an
  hour](TESTING.md#the-first-run-download-runs-at-most-once-an-hour) and seed
  from a cached tree in between; under `BROWSERAI_RELEASE_RUN=1` the cache is
  bypassed unconditionally, so **no release can be cut on evidence that came out
  of `.work\`.** The coverage block's `first-run bytes` row says which happened,
  in every run.

  ⚠️ **Do not read a slice test's own duration as the signal.** The rig shares
  one `SliceRun`, so its cost lands on whichever test triggered it first:
  measured 2026-08-16, `TheResolvedBrowserIsOurChromiumAndNotTheHeadlessShell`
  took **2.6 ms** on a run that really did launch a browser. A short duration
  there means nothing either way, and a rule of thumb that says otherwise is a
  second false green.

**Evidence:** total, passed, failed and skipped counts from the run's own output,
the exit code, and the coverage block, which states what was exercised.

> **Corrected 2026-08-16, on the day the first run raised it (previously: a
> paragraph instructing a person to `ls` two paths outside the run, plus
> *"the mechanical fix, not built here"*).** It is built now.
> `SuiteEnvironment` is the single gate the thirty-five guards route through;
> `SuiteCoverageTests` prints the block and exercises the release branch on
> every ordinary run, so the switch is not code that first runs on release day.
>
> ⚠️ **The measurement narrowed which absence was ever silent, and the original
> wording named both.** Taken at `c21fea7`, before the fix:
>
> | What was moved aside | total | passed | failed | skipped | exit |
> |---|---|---|---|---|---|
> | nothing | 329 | 328 | 0 | 1 | 0 |
> | the whole publish directory | 329 | 328 | 0 | 1 | **0** |
> | `payload/` | 329 | 247 | **80** | 2 | 2 |
>
> **The published slice's absence produced a character-identical summary**;
> `payload/` produced eighty failures, because the fake-child and tool-surface
> layers need `node.exe` and were never guarded. So the founding failure class
> was real and its subject was the publish alone. Both are gated now regardless
> — a guard nobody accounts for is how this one was missed.

> **This whole checklist rests on this item being *run* rather than assumed.**
> [The release gate](#the-release-gate) says exactly that about its own
> step 3, and it is the honest cost of the decision to have no automation.

---

## Version and record

### 9. The version is derived, and `0.0.0` is refused

Versions come from **git tags** — three parts plus a pre-release suffix, the
shape the packager accepts. Nothing hand-edited. `0.0.0` means the derivation
found no tag: a build that does not know what it is, and therefore a build that
cannot be rolled back to or bisected against. **Refuse it.**

**The refusal is the build's, not this checklist's**: MinVer
derives the version and `RefuseAVersionDerivedFromNoTag` in
`src/BrowserAI/BrowserAI.csproj` fails the build on anything beginning `0.0.0`,
naming `fetch-depth: 0` as the remedy. A release cut from a green build has
already passed this item; what is recorded here is which version that was.

**Also check the release is not being cut from a pre-release build.** An
untagged build carries its own `-alpha.N.M` suffix, which is the whole of *never
self-update from a build that is not a release* — so a version with a suffix
means the tag for this release has not been created yet.

**Evidence:** the version the build stamped, and the tag it came from — **two
commands, because the first does not answer the second**:

```
dotnet msbuild src/BrowserAI/BrowserAI.csproj -t:MinVer -getProperty:MinVerVersion
git describe --tags --long
```

> **Corrected 2026-08-16 on the first run of this checklist (previously: the
> `msbuild` line alone "answers both without building anything").** It answers
> one. `MinVerVersion` is a version string and carries no tag name, so the tag a
> release was cut from could not be recorded from it. `git describe --tags
> --long` prints `<tag>-<commits>-g<sha>`, which is the tag, the distance and
> the commit in one line — and the distance is what makes the pre-release
> suffix legible rather than mysterious.

### 10. The changelog's unreleased section is not empty

**Refuse to release on an empty unreleased section.** A release with nothing to
say is a release nobody can describe afterwards — and the first thing a rollback
needs is a statement of what changed.

**This item has a command:**

```
pwsh -File build/Get-ReleaseNotes.ps1 -StampVersion <the version item 9 recorded>
```

It extracts the `## [Unreleased]` section, **exits non-zero if it holds no list
items**, and only then stamps the version below the heading. Empty means no
entries rather than no characters, because a section holding nothing but its
`### Added` subheads is what a changelog nobody wrote looks like. Run it without
`-StampVersion` first: that is the same refusal with nothing written.

**What the command cannot check is the half that matters** — that the entries
were written as the work landed rather than reconstructed here. A changelog
assembled from `git log` at this moment satisfies the script and has satisfied
this item in form only.

**Stamping creates a dated record, so seal it in the same commit.** The section
the command just wrote is a released section from that moment on, and
`AppendOnlyRecordTests.EveryDatedRecordIsSealedAndNothingSealedHasVanished`
fails until it is registered — by design, so the newest release notes are not the
one thing nothing protects. Add a `new("CHANGELOG.md#<version>", …)` line to
`AppendOnlyRecordTests.Sealed`; the sibling test's failure message prints the
character count and digest to use. See
[the release gate](TESTING.md#the-dated-records-are-append-only).

**Evidence:** the unreleased section's contents, moved under the version being
cut, and the seal line added beside it.

### 11. The resolved set is recorded beside the artifact

`packages.lock.json`, the resolved `package-lock.json`, the browser revisions
from the resolved `browsers.json`, and the Node version.

**An artifact that cannot state exactly what went into it is not releasable** —
that is what makes a rollback meaningful and a regression bisectable
([rule 1](DECISIONS.md#the-five-rules-that-make-floating-safe)).

**`build/New-Release.ps1` emits it**, beside the archived `.nupkg`, at
`<ArchiveDir>/BrowserAI-<version>-manifest/`. It holds exactly these, copied
rather than transcribed, plus a `manifest.json` stating the version, the tag,
the package's SHA-256 and the resolved version each copied file carries:

| In the manifest | From |
|---|---|
| `packages.lock.json` ×3 | `src/BrowserAI/`, `tests/BrowserAI.Tests/`, `tests/BrowserAI.TestProbe/` |
| `package-lock.json` | `build/payload/` — the committed provenance stamp the payload build writes |
| `payload.json` | `payload/` — Node's version, LTS name, archive SHA-256 and both tree sizes |
| `browsers.json` | `upstream-snapshots/` — the browser revisions, from the resolved payload |
| The derived version and its tag | item 9 |
| The full `.nupkg` and its size | item 12 |

**Evidence:** the manifest's path, and the resolved version each file states.

> **Corrected twice on 2026-08-16, both on the day the run raised it
> (previously: "Evidence: the manifest, beside the artifact", then "nothing
> emits this manifest, so it is assembled by hand").** The first wording named
> an artifact that had never existed and that nothing produced, so the item
> could be neither satisfied nor failed; the second described the hand-assembly
> that satisfied it once, at `.work/step20/manifest/`, which is a checklist item
> nobody satisfies twice.
>
> ✅ **It is emitted.** `build/Write-ReleaseManifest.ps1` copies the six files
> and writes `manifest.json`; `build/New-Release.ps1` calls it as its eighth
> step and returns the path as `ResolvedSet`. It lives in its own script for the
> same reason `Test-ReleaseVersion.ps1` does — **so the suite can drive it** —
> and `ReleaseScriptTests` runs it both ways, including that **a missing file
> refuses rather than writing a partial**, because a manifest holding five of
> six reads exactly like a complete one a year later.

### 12. The rollback path is publishable

The mechanics are tested by the update layer ([Testing](TESTING.md)) and
implemented in [`src/BrowserAI/Updates/`](ARCHITECTURE.md#updates). Two halves
live **outside** a test run, and both must be true at release time:

- **The full `.nupkg` for this release is archived.** Velopack prunes `packages\`
  to the current full package and deltas are forward-only, so an unarchived
  release is one you cannot roll back to without a fresh full download.
- **The release-validation rule permits a rollback republish.** Written as
  *"monotonic **or** an explicit rollback republish"*. Get this wrong in the
  strict direction and the client accepts a rollback the build refuses to emit —
  a pipeline that has made rolling back impossible while every component
  individually supports it, which is a real and observed state rather than a
  hypothetical one.

**Evidence:** the archived package path, and the validation rule's text.

### 13. Third-party notices ship

Redistribution obligations attach at **first installer handoff**, independent of
BrowserAI's own licence. ⚠️ **That handoff has not happened yet** — *corrected
2026-08-24, and it strengthens this item rather than relaxing it*: `v1.0.0` is a
tag and a packed artifact in a gitignored `Releases/`, and nothing has been given
to anyone. This checklist is what makes the first handoff correct rather than a
record of one already made. Verified against
[README → Third-party components](README.md#third-party-components):

- **Node's full `LICENSE`** — it aggregates OpenSSL, ICU, V8, zlib and c-ares
  terms. *"A single `node.exe`, nothing else"* drops it. **Not optional.**
- The vendored `node_modules` tree **intact**, which ships `@playwright/mcp`'s
  and `playwright-core`'s Apache-2.0 `LICENSE` and satisfies §4.
- Velopack's MIT notice.
- **`ModelContextProtocol`'s and `ModelContextProtocol.Core`'s Apache-2.0
  licence, whole.** §4(a) requires a redistributor to give every recipient a
  copy of the licence, and upstream's own file grants three — Apache-2.0, MIT
  for contributions never relicensed, and CC-BY-4.0 for documentation — so
  reproducing the Apache half alone would drop terms that cover part of the
  code.
- **The MIT notice for every `Microsoft.Extensions.*` assembly linked in** —
  seventeen of them at 2026-08-16, two referenced directly and the rest arriving
  transitively, under two different `.NET Foundation` copyright lines because
  they come from two repositories.
- **A short trademark disclaimer in the installed artifact.** Apache-2.0 §6
  grants no trademark rights, and the inherited `browser_*` names surface
  upstream branding directly in BrowserAI's own API.

The last four have no upstream file of their own — a NuGet package compiled
*into* `BrowserAI.exe` leaves its licence in the machine's package cache, which
is never copied to a publish output — and all four ship in
`THIRD-PARTY-NOTICES.txt` beside the binary, published by the
`AddNoticesToPublish` target.

> ✅ **Corrected 2026-08-16: this item names six
> obligations, not four (previously the list held only Node, the vendored tree,
> *"Velopack's MIT notice"* and the trademark disclaimer, and the paragraph
> beneath it read *"The last two have no upstream file of their own — Velopack
> is compiled into `BrowserAI.exe`, so its licence never leaves the NuGet
> cache — and both ship in `THIRD-PARTY-NOTICES.txt`"*).** The reasoning that
> put Velopack's text in the artifact applies unchanged to the MCP SDK and to
> the `Microsoft.Extensions.*` family: same mechanism, same absence, and
> Apache-2.0 §4(a) is stricter than MIT's notice clause rather than looser. It
> was raised once and deliberately left undecided, because shipping a fifth
> obligation on one reading would have changed a settled table without anyone
> deciding it; the repository is public now, so the table was changed
> deliberately instead. The count in this item is the only
> place it is written down as prose — the enforcing list is
> `ThirdPartyNoticeTests.Obligations`, and the `Microsoft.Extensions.*` half is
> derived from `src/BrowserAI/packages.lock.json` rather than typed, so a
> package that enters the closure on a later bump is a red build here rather
> than a licence nobody noticed had arrived.

Nothing the user's machine downloads on first run creates an obligation for us —
we ship no copy of it. That is not a side benefit of first-run provisioning; it
is the reason for it.

**Evidence:** the paths of each notice file inside the packaged artifact.

> ✅ **`ThirdPartyNoticeTests` covers all of it, closed 2026-08-16 the same day
> the first run found two of four absent (previously: "and no test covers
> them").** The obligations are **data**, so a fifth is a red build rather than
> a discovery at the next release, and three subjects are asserted because each
> can be right while the next is wrong: the repository's own notices file, the
> publish output `vpk` is handed, and the entry list of the packed `.nupkg`
> under `lib/app/`. The Velopack version stamped in the notices is asserted
> against `src/BrowserAI/packages.lock.json`, so a bump is red until the licence
> text has been re-fetched from the new package's own commit. What is recorded
> here is still the paths, read from the package.

---

## The decision

### 14. A human decides

Green is **releasable**, not **released**. There is no release pipeline, no
scheduled publish and no auto-merge on green. Nothing in items 1–13 authorises a
release; they only permit one.

**Evidence:** a human said so.

---

## Two things to inherit rather than rediscover

### Nothing makes this gate fire

It works when it is invoked, and **there is no mechanism anywhere that invokes
it.** That is deliberate: it allows many commits without re-running everything
each time, and it keeps the project off hosted CI.

⚠️ ***Corrected 2026-08-20 (previously the same paragraph, left standing through
the two days hosted CI existed).*** The arrangement was suspended on 2026-08-18
and restored on 2026-08-20 at the maintainer's decision. It now holds more
completely than when it was written: for those two days a push at least built the
payload, provisioned both browsers and ran the suite on a machine nobody owned,
and nothing does that now. [`TODO.md`](TODO.md#continuous-integration) carries what
that costs.

The cost, stated plainly so it is inherited as a decision:

- **The gate is only as good as the person invoking it.** It rests entirely on
  the run happening rather than being assumed.
- The one gap it leaves — *upstream moved while nobody was looking* — is covered
  by the [daily drift check](CLAUDE.md#the-daily-drift-check), which is a
  directive rather than a job, and which fires by construction because this
  project is built entirely through an agent: the check happens because the work
  happens.

**Review this after the product is finished.** The condition that ends the
arrangement is already named in
[the release gate](#the-release-gate): the day a second person can cut
a release, the assumption breaks and the gate has to move into automation.

### Being green is necessary and not sufficient

Stated twice on purpose, because the two halves fail differently. A red item
means there is **nothing to decide**. A green run means there is **something to
decide**, and the deciding is a human's. This checklist never says *ship it*.
