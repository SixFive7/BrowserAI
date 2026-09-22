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

#### The order the last six steps are executed in — and it is not the numbering

⚠️ ***Corrected 2026-09-15 by addition (previously this section ended at "Manual
governs step 5 alone", and the checklist's numbering was the only order
recorded anywhere).*** Items 8–13 are **numbered** in the order a reader needs
them and **executed** in the order below, and on 2026-09-15 a release attempt
ran them in the numbered order and stopped twice on failures that were artifacts
of the order rather than defects in anything:

1. **Pack for the gate, first.** [Item 8](#8-run-everything)'s skipped count must
   be zero, and two arms — the real-installer one and the notice check that reads
   the packed `.nupkg` — are capability-gated on a pack existing in `Releases/`.
   With no pack they report *skipped*, and `BROWSERAI_RELEASE_RUN=1` turns each
   into a failure. So the pack exists before the gate runs, and it is a
   gate artifact rather than the artifact that ships.
2. **[Item 8](#8-run-everything), at the pre-stamp content.** The gate runs
   against the release content **minus** the changelog stamp, the seal and any
   correction that depends on the release having happened. That delta is what
   this checklist already accepts — see [item 10](#10-the-changelogs-unreleased-section-is-not-empty),
   whose whole output is a stamp.
3. **[Item 10](#10-the-changelogs-unreleased-section-is-not-empty): stamp, and
   seal in the same commit.** ⚠️ **For the 2026-09-15 re-ship of `1.0.0` the
   stamp is a MERGE into the existing section rather than a new one** — the
   version already has a section and `Get-ReleaseNotes.ps1 -StampVersion 1.0.0`
   refuses, correctly, so the entries that accumulated under `[Unreleased]` after
   the first cut were moved into the `1.0.0` groups by hand and the section was
   re-sealed. **From the next release onwards this step is a NEW section stamped
   by the command**, and nothing about the script changed to allow the merge.
   The body is generated here too — see
   [the body step](#the-release-body-is-generated-and-its-rendering-is-checked-before-it-is-published),
   which is what a release page now shows instead of the section cut at a
   heading boundary.

   ⚠️ **THE HEADING DATE IS INSIDE THE SEAL — *added 2026-09-16*.** A sealed
   record starts at its heading, so `## [1.0.0] - 2026-09-15` is part of the
   281,709 sealed characters. Setting the real release date therefore **breaks
   the seal**, and the date change and the re-seal are **one commit** — the same
   commit as the stamp. Split them and the gate is red in between, on a record
   nobody rewrote. `AppendOnlyRecordTests` now says so in the failure itself: when
   the heading line is the *only* thing that moved it reports **the HEADING LINE
   changed and nothing else did** with the new seal line to paste, instead of
   *REWRITTEN … revert it* — which was the right sentence for a sweep and the
   wrong one for the one edit this checklist requires.
4. **[Item 9](#9-the-version-is-derived-and-000-is-refused): create the tag**, on
   the commit the gate was run at plus the stamp.
5. **Clean re-pack.** `Releases/` is cleared of everything that is not this
   release — the archive stays — and `New-Release.ps1` is run again, so the feed
   it writes holds the rows this release actually publishes.

   ⚠️ **Four files, by name, and two directories that must survive — *added
   2026-09-16*.** Delete `Releases/*.nupkg`, `Releases/releases.win.json`,
   `Releases/RELEASES` and `Releases/assets.win.json`. **Keep `Releases/archive/`**,
   which is the rollback targets of real releases, and **keep
   `Releases/test-pack/`**, which is the suite's own installer and is never
   published. *(The two human-facing downloads, `BrowserAI.exe` and
   `BrowserAI.zip`, are rewritten by the re-pack, so they need no separate
   step.)*

   ⚠️ **AND CLEAR `Releases/test-pack/`'S CONTENTS TOO — *corrected 2026-09-16
   by addition, the same day, after this omission stopped a cut*.** The paragraph
   above is right that the **directory** must survive and silent about what is in
   it, and `Releases/test-pack/` is a **second Velopack feed** with the same
   monotonicity rule as the first. Every gate pack writes a
   `BrowserAI.app.test-<pre-release>` into it, so at the moment a release is cut
   it holds versions **newer** than the release — and `vpk` refuses:

   ```
   [FTL] There is a release in channel win which is equal or greater to the current
         version 1.0.0. Please increase the current package version or remove that release.
   New-Release.ps1 : vpk pack failed for the test pack with exit code -1, so the suite
         has no installer it may run.
   ```

   **By the running order this refuses EVERY release**, because [step 1](#the-order-the-last-six-steps-are-executed-in--and-it-is-not-the-numbering)
   packs for the gate and the gate pack is always a pre-release past the tag.
   It also fails **after** the real pack has succeeded, so the exit code says the
   release was not built when the only thing missing is the suite's own
   installer. Delete `Releases/test-pack/*.nupkg`, its `releases.win.json`,
   `RELEASES` and `assets.win.json`, and its two renamed downloads
   `BrowserAI.test-installer.exe` and `BrowserAI.test-portable.zip` — all of
   which the same run rebuilds, and none of which is ever published.

   ⚠️ **THE SCRIPT DOES THIS NOW AND THE PARAGRAPH ABOVE IS HISTORY —
   *corrected 2026-09-17 (previously "📣 **The better fix is a script change and
   it was deliberately not taken here.** `New-Release.ps1` could clear its own
   test output before packing into it, since that output is regenerated on every
   run and published on none — one `Remove-Item` where this bullet is a
   paragraph a person has to remember. That is a behaviour change to the release
   script, which belongs to whoever owns it rather than to the executor of a
   release, and **`ReleaseScriptTests.AReleaseCutOverLocalPreReleasePacksIsRefusedAndNamesWhatToClear`
   names four files and would need to name these too.**")*.** **Q200**, decided
   2026-09-17: [`build/Clear-TestPackFeed.ps1`](build/Clear-TestPackFeed.ps1) runs
   from `New-Release.ps1` immediately before the test pack and deletes exactly
   what that pack regenerates — `BrowserAI.app.test-*.nupkg`,
   `releases.win.json`, `RELEASES`, `assets.win.json`, the two renamed downloads,
   and the two **pre-rename** names a run that died between the pack and the
   rename leaves instead. **So the previous ⚠️ is now a description of a failure
   mode rather than a step**, and it is kept for the reader who meets that `vpk`
   message in an old log. Nothing here is owed by hand.

   **It is its own script for the same reason `Test-ReleaseVersion.ps1` is** — so
   the suite can drive it. `ReleaseScriptTests.TheSecondFeedIsClearedBeforeItIsPackedIntoAndNothingElseIs`
   runs it over a planted three-part layout and asserts the test feed is emptied
   *and* that the shipping feed and `archive/` are byte-identical afterwards, with
   a third direction holding that a file under `test-pack/` which no pack
   regenerates survives. `ReleaseScriptTests.TheSuitesInstallerIsPackedUnderATestIdIntoADirectoryOfItsOwn`
   holds the **order**, which is the half a driven test cannot see: a clear that
   ran after the pack would delete the installer the suite is about to run.

   ⚠️ **The refusal in `Test-ReleaseVersion.ps1` is NOT redundant now and must
   not be deleted.** It fires on the **shipping** feed, which nothing clears
   automatically and which this checklist still empties by hand four files at a
   time; and it fires before a pack rather than during one. The script change
   closes the second feed only.

   ⚠️ **The cut is REFUSED until this is done, and it says so — *added
   2026-09-16*.** `build/Test-ReleaseVersion.ps1` refuses a **release** candidate
   when the local feed's highest version is a **pre-release** newer than it, and
   names those four files in the refusal. That is the ordinary state between
   releases: every gate pack is cut at whatever MinVer derives from a commit past
   the tag, so `Releases/` fills with `1.0.1-alpha.0.N`. Before this rule the
   script called that a **rollback** and advised `-RollbackRepublish` — which
   would have published the release into a feed whose manifest and asset list
   name packages nobody ever released.
   `ReleaseScriptTests.AReleaseCutOverLocalPreReleasePacksIsRefusedAndNamesWhatToClear`
   holds the refusal, the file names, and the three controls: a real rollback
   still reads as one, a pre-release gate pack over the same directory is still
   monotonic, and a release over only *older* pre-releases is still monotonic.
6. **Publish**, and then **verify the feed over HTTP — by polling the BODY
   until it names the version just published, and not before.** The status code
   is not the check. ⚠️ ***Added 2026-09-15, measured: the release-assets CDN
   served the PREVIOUS manifest with HTTP 200 for about two minutes after the
   assets were replaced*** — `Age: 2701` on the response, while
   `gh api .../releases/latest` was correct throughout, 14:16Z–14:24Z. A
   post-publish verification taken in that window reports a feed that is serving
   a package nobody can download, and reports it green. Poll
   `releases/latest/download/releases.win.json` until `Version` is the new one;
   **no other post-publish check counts until it is**, because every one of them
   would be reading the old release.
   `UpdateTests.TheProductionFeedUrlResolvesOverHttpAndReturnsAManifest` reads the
   same body for the same two things — the pack id and a version no older than
   the first published under it — with the August manifest as its positive
   control.

**The two reds that established this, both of them 2026-09-15 gate run 1, and
both green on the very next run once the order was fixed:**

- **`ChangelogTests.TheChangelogHasAnUnreleasedSectionWithEntriesInIt`** —
  `Expected 0 but found 1`. Item 10's stamp moves every entry under the new
  version's heading and leaves `## [Unreleased]` **empty by construction**, so
  the check refuses. It can only be green **before** the stamp. *(Since
  2026-09-15 that arm also accepts an empty section on the one commit the tag is
  exactly at — which narrows the window this ordering has to protect, and does
  not remove it: the tag is created at step 4, after the gate.)* ⚠️ **Narrowed
  again later the same day, and this time the window is closed rather than
  reduced:** the arm also accepts an empty section when **nothing under `src/` or
  `tests/` has landed since the changelog was last written**, which is true for
  the whole of steps 3 to 6 — stamp, tag, re-pack, publish — and stops being true
  the moment a product or test change lands without an entry. The ordering above
  still stands and is still worth following, but a gate run taken *after* the
  stamp is no longer red for that reason.
- **`UpdateTests.TheProductionFeedUrlResolvesOverHttpAndReturnsAManifest`** —
  `Expected 200 but found 404`. Deleting the `v1.0.0` tag to move it turned the
  only published release into a **Draft**, so
  `releases/latest/download/releases.win.json` resolved to nothing. Verified
  outside the suite: `curl` → **404**, `gh release list` → one entry, *Draft*.
  It can only be green while **some** release is published.

  > ⚠️ ***Corrected 2026-09-15 by addition (previously the bullet above was the
  > whole of what this checklist said about tags and releases, and it reads as
  > though touching the tag drafts the release).*** **MOVING a tag does not
  > draft it; DELETING one does, and the difference is the whole cost.**
  > Measured the same day, both halves: a **delete** (`git push origin
  > :refs/tags/v1.0.0`) drafted the only published release and the public feed
  > answered 404 for **about twenty minutes**; a **move**
  > (`git tag -f v1.0.0 <sha>` then `git push --force origin refs/tags/v1.0.0`)
  > left the release published throughout, and the feed was measured down for
  > **7.7 s** — the window in which GitHub re-resolves `releases/latest`, not a
  > drafting. So a re-cut at the same version moves the tag and never deletes
  > it, and the twenty minutes is what deleting costs rather than what tagging
  > costs.

**They cannot both be green in the window the numbered order puts item 8 in** —
one needs pre-stamp, the other needs a live release — which is the proof that
the numbering is a reading order and this is the running one.

⚠️ **The cost of getting it wrong is outward-facing and was paid.** The public
download and update feed answered **404 for about twenty minutes**, 12:05Z–12:25:34Z
on 2026-09-15. *The end of that window is measured to the second; the start is a
bound rather than a measurement* — GitHub's event feed carries no `DeleteEvent`
for a tag, so the earliest independent artefact is the release commit reaching
`origin` at 12:04:10Z with the tag deleted immediately after.

**Two things the re-pack step turns on, recorded here because both were assumed
wrongly on the day:**

- **`build/Test-ReleaseVersion.ps1` REFUSES an equal version.** `$candidate -eq
  $highest` is an explicit refusal — *"already the newest release on this
  channel. Republishing a version over itself would leave two packages claiming
  one version"* — and `ReleaseScriptTests` holds it in both directions. The rule
  is *monotonic **or** an explicit rollback republish*, and **equal is neither**.
  It answered `monotonic` for 1.0.0 on 2026-09-15 only because the **local**
  `Releases/releases.win.json` still held 0.1.3 as its highest `Full`; a re-pack
  after the feed is correct will not get that answer twice. Nothing in this
  repository ever claimed otherwise — the belief that it did was carried in a
  briefing, not in the tree, and this paragraph exists so the next reader does
  not have to re-derive it.
- **A `Releases/` holding older artifacts produces a feed nobody can publish.**
  On 2026-09-15 `vpk` wrote **seven** rows into `releases.win.json` — the two new
  1.0.0 rows and five stale `BrowserAI` 0.1.x ones — six of which name files that
  would not be uploaded. The published August feed held **exactly one** row,
  which is the shape a feed should have.



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

#### Playwright, and the one override a human may take

**Every release builds against the latest Playwright.** `@playwright/mcp` from
the `latest` dist-tag, with the `playwright-core` alpha that arrives as its own
exact dependency. That is not a preference; it is
[the rule](DECISIONS.md#every-release-builds-against-the-latest-playwright-and-only-a-human-may-say-otherwise),
and it is the upstream this whole product exists to keep up with.

**A HUMAN may force a crunch override.** Shipping against a version that is not
the newest, because the newest breaks something and the release cannot wait, is a
judgement about a deadline and it belongs to the person holding the deadline.

⚠️ **AGENTS MAY NEVER.** Not to unblock a red suite, not to finish a batch, not
because the break is upstream's. An agent that meets a break fixes it forward or
**stops and reports** — pinning back is the failure the versioning policy exists
to prevent and the one an agent is most likely to commit, because reverting to
green is locally the cheapest correct-looking move.

**An override leaves a trace in the release artifact.** The resolved set recorded
at [item 11](#11-the-resolved-set-is-recorded-beside-the-artifact) states the
version that actually shipped, copied rather than transcribed. Say what was
held, at what version, and why, **in the manifest, in this item's evidence and in
the changelog entry** — a release whose manifest does not say it was overridden is
a release claiming it was not.

⚠️ ***Corrected 2026-08-26 (previously "in this item's evidence and in the
changelog entry", with no manifest field to put it in).*** The sentence above
asked the manifest to say something the manifest could not express: the script
emitted no override field and had no place for one. It takes five parameters now
and **always** emits the key, so an ordinary release states `"override": null`
rather than saying nothing:

```powershell
pwsh -File build/Write-ReleaseManifest.ps1 -Destination <dir> -Version <v> `
     -OverriddenPackage '@playwright/mcp' -OverrideHeldAt 0.0.79 `
     -OverrideNewest 0.0.80 -OverrideReason '<what broke>' -OverrideDecidedBy '<name>'
```

All five go together or none does; a half-stated override refuses the manifest,
because a block naming a held version and nothing else reads like a complete
account of the decision a year later.

**Evidence when it applies:** the manifest's own `override` block — which carries
the held version, the newest version, the break and the name of the human who
took the decision — quoted into this item beside the changelog entry.
*(Added 2026-08-26.)*

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

### 4. The four snapshots and the verdict file adjudicated

`tools-list.json`, `cli-help.txt`, `config-schema.d.ts`, `browsers.json` —
regenerated from the resolved payload and diffed. The mechanism is
[the upstream-review gate](TESTING.md#the-upstream-review-gate); read it there.

**Evidence:** for each of the four, `unchanged`, or the marker entry's
adjudication of exactly what moved. A snapshot that changed without an
adjudication fails the gate, so this item is answered by the suite being green —
what is recorded here is the adjudication text, not a second assertion.

⚠️ **And the verdict, per tool, for anything `tools-list.json` added, removed or
renamed.** *Added 2026-08-26.* [`tool-verdicts.json`](tool-verdicts.json) carries
one row per tool and BrowserAI **denies by default**, so a tool that arrived
unadjudicated is a tool no call can reach —
[`ToolVerdictTests`](TESTING.md#the-verdicts-file-and-the-tool-set-it-is-judged-against)
makes that a red build in both directions on every run, and `judgedAgainst` must
name the versions the payload lock resolves. **Evidence:** the rows added or
removed, each with its `why`, and the `judgedAgainst` stamp. **The test can see
that a row exists and never that a human meant it**, which is why this item is
here and not only there — the same reason item 5 exists.

A moved `browsers.json` deserves its own line in the release notes: every machine
re-downloads the browser and re-extracts it. **Updated 2026-08-17 (previously
"and the old revision sits on disk until something prunes it").** Something does:
`RevisionPrune` runs on the next successful provision and reclaims the ~430 MiB
the old revision holds, so what the note has to carry is the download, not the
disk. The one consequence worth a sentence is the other direction — a **rollback**
to the previous build re-downloads 207.3 MB, because the revision it names has
already been pruned. *(Re-measured 2026-09-16 at chromium 1244; previously
203.8 MB.)*

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

⚠️ **THE ICON IS THE CHOSEN ONE AND THIS IS THE PRE-CUT CHECK FOR IT.** *Added
2026-09-15; the placeholder was replaced 2026-09-16.* **Corrected 2026-09-16
(previously "THE ICON IS A PLACEHOLDER … `assets/BrowserAI.ico` is candidate 1
of the ten drawn that day").** [`assets/BrowserAI.ico`](assets/BrowserAI.ico) is
**candidate 3** — a globe with a reading eye — chosen by the maintainer on
2026-09-16 (Q196), and it is wired into both executables, the Setup stub, the
Add/Remove entry and the Start Menu shortcut. **Before a release is cut, confirm
that the two files still agree**: `assets/BrowserAI.ico` and
[`assets/icon.svg`](assets/icon.svg), which is the master the raster was rendered
from. `ReleaseScriptTests.TheShippedIconIsTheOneTheMaintainerChose` holds the
`.ico`'s **shape** — four entries, 16/32/48 as 32-bit DIBs and 256 as a
PNG-compressed entry — and that `icon-256.png` is 256×256; **nothing holds that
the drawing in the `.ico` is the drawing in the SVG**, because that is a render
comparison on every build to answer a question a person answers by looking.
That is what this line is for. *(Its planted red is a **doctored-file** control
rather than the old placeholder, and that is stated where it lives: candidate 1
was packed by the same script and has the identical directory shape, so putting
it back would not move one assertion.)*

📣 **The social preview is uploaded by hand and only the maintainer can do it.**
[`assets/social-preview.png`](assets/social-preview.png) is 1280×640, which is
the size GitHub's **Settings → General → Social preview** field expects; there is
no API for that setting and no file in the repository that supplies it, so a new
one only reaches the world when somebody drags it into that field. The file in
the tree is the record of what was uploaded, not the mechanism.

NativeAOT publish, analyzers at error severity. **A warning-as-error is a red
build**, and a severity is never weakened to make code pass. ILC output empty.
`UseSystemResourceKeys` never set — it strips the exception messages this project
exists to be able to read.

**Evidence:** the publish command, its exit code, and the warning count, which is
zero — **plus the two things an exit code does not establish**:

- **ILC's own output, read and reported empty — ONCE PER BINARY.** ⚠️ *Widened
  2026-09-15: there are two executables now, linked by two ILC passes, and the
  script prints a line for each.* `build/New-Release.ps1` prints
  `ILC output for the configuration app is clean (<n> lines read, 0 complaints)`
  and `ILC output for the MCP server is clean (<n> lines read, 0 complaints)`;
  **both lines are the evidence and one of them is not enough**, because a scan
  that read one of the two logs would ship a binary nobody had checked while
  reporting that ILC's output was clean. The publish's exit code is not evidence
  at all, because the failure this exists for exited 0 with an artifact on disk.
  Measured on the first two-binary run: **94** lines for the app and **388** for
  the server, 0 complaints in each.
- **Both executables are in the pack directory.** The script refuses by name
  when one is missing, and that refusal has already earned itself: publishing
  both with `-o` pointed at one directory left the server and deleted the app,
  leaving the app's `.pdb` behind so the directory looked populated. Each
  publish stages into `artifacts\publish-<exe stem>` and is copied in.
- **`UseSystemResourceKeys` unset**, quoted from `Directory.Build.props`.

⚠️ **EVERY RELEASE PUBLISH GOES THROUGH `build/New-Release.ps1`, AND IT LEAVES
A LOCK FILE MODIFIED.** *Added 2026-09-16.* **Corrected the same day (previously
"EVERY PUBLISH"):** the suite's own published slice is a different publish and is
refreshed by the command `PublishedSlice`'s refusal prints —
`dotnet publish src/BrowserAI/BrowserAI.csproj -c Release -r win-x64
--self-contained`, and the same for `BrowserAI.App` — because the release script
stages into `artifacts\publish-<exe stem>` and never touches
`src\<project>\bin\`. **Both leave the same diff.** A RID-specific restore adds an empty
`"net10.0-windows7.0/win-x64": {}` section to
[`src/BrowserAI.Core/packages.lock.json`](src/BrowserAI.Core/packages.lock.json)
— **including the restore this script performs**, watched on a full pack run on
2026-09-16, so it is not something a standalone `dotnet publish` does and the
release script avoids.

⚠️ **COMMIT IT — *corrected 2026-09-16 (previously "**Revert it; do not commit
it** — `git checkout -- src/BrowserAI.Core/packages.lock.json` — because an empty
section is a restore artifact rather than a resolution anybody reviewed, and a
`git add -A` after a publish carries it into the release commit, which is how it
reached `HEAD` once already. Nothing enforces this and a test would be red for
the whole window between this item and item 8")*.** The section is committed
under **Q199**, decided 2026-09-16: it is what a RID restore genuinely resolves,
and the revert habit had already failed once — it reached `HEAD` in a `git add
-A` and was reverted in `ac244ff` under a sentence calling it an artifact.
**This item therefore has nothing left to do about that file**, and the window
the old rule needed protecting is closed rather than narrowed: with the section
committed, a RID restore leaves the lock file **byte-identical** (measured
2026-09-16 — SHA-256 `fab160c4…` either side of a RID restore of both
executables), so a publish no longer produces a diff for anybody to remember to
revert. ⚠️ **THERE IS ONE STATE NOW, AND IT IS BY CONSTRUCTION — *corrected 2026-09-17
(previously "**What it does instead is show that file modified after every
[item 8](#8-run-everything) run**, because `dotnet test` restores the solution
without a RID and that writes the other of the file's two states (`7f30ec57…`).
**Nothing needs doing about it here:** step 5's re-pack restores with the RID and
leaves the tree clean before the release commit is written")*.**
[`src/BrowserAI.Core/BrowserAI.Core.csproj`](src/BrowserAI.Core/BrowserAI.Core.csproj)
declares `<RuntimeIdentifier>win-x64</RuntimeIdentifier>` from 2026-09-17 — the
way out that [Testing](TESTING.md#a-publish-rewrites-a-lock-file-and-the-diff-is-committed-rather-than-reverted)
had written down and deliberately not taken — so **every** restore shape resolves
the same set and writes the same bytes. Measured the day it went in, five reads,
all `fab160c4…`: `dotnet restore --force-evaluate` over the solution, a
`dotnet publish -c Release -r win-x64 --self-contained`, a plain `dotnet restore`,
`dotnet restore src/BrowserAI/BrowserAI.csproj -r win-x64 --force-evaluate`, and
`dotnet restore BrowserAI.slnx --force-evaluate` — the last two being exactly the
pair that used to disagree. `7f30ec57…` is no longer reachable. **So neither a
publish nor an [item 8](#8-run-everything) run leaves that file modified**, and
this item has nothing to do about it in either direction. What must not happen,
before this decision and after it, is a `git add -A` taken on trust.
**Q201**, decided 2026-09-17; Q199 above decided only *which* of the two states to
commit, which moved the churn rather than ending it, and its record is kept
because it is what a reader who met the flip-flop needs in order to recognise
that it is gone.

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

⚠️ **This item is numbered 8 and is executed third**, after a pack exists and
before the stamp and the tag — see
[the order the last six steps are executed in](#the-order-the-last-six-steps-are-executed-in--and-it-is-not-the-numbering).
Run in the numbered order it goes red on two things that are not defects.

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
- ⚠️ **Pack before you run this item, not after — added 2026-09-15.** Two
  capabilities are produced by `build/New-Release.ps1` and by nothing else — the
  packed `.nupkg` the notice check reads, and, since the install layout split,
  the real `Setup.exe` that
  `RealInstallerTests.InstallingTwiceOverOneRootLeavesTheDataRootByteIdentical`
  installs twice over one root. Under `BROWSERAI_RELEASE_RUN=1` an absent
  capability is a **failing test** rather than a skip, so a release gate run on a
  tree that has never been packed fails for want of an artefact rather than for
  anything about the code. Run [item 7's publish and pack](#7-build-clean)
  first; the gate then exercises the installer that is about to be published.
  ⚠️ **The arm refuses to run when an Add/Remove entry for the TEST pack id
  already exists**, because `--installto` would repoint that entry and the
  uninstall would delete it — so a leftover of the suite's own reports the
  capability ABSENT, with the key named in the coverage block, and the release
  run fails. Clear that key; it is one the suite wrote.

  ⚠️ *Corrected 2026-09-16 (previously "refuses to run at all when an
  Add/Remove entry for the pack id already exists … Uninstall it first, or cut
  the release from a machine that does not have one").* **That instruction was
  false from the day the test id landed** and asked a maintainer to uninstall a
  working product for no reason. The capability judges `BrowserAI.app.test`,
  never `BrowserAI.app`, precisely so that a real install is never in the way —
  and since 2026-09-16 the suite's pack is also **titled** `BrowserAI (suite)`,
  so the two cannot share a Start Menu shortcut either. A release is cut from a
  machine with a real install on it, which is the ordinary case.

  **A pack into a scratch directory counts**, if the gate is being run before the
  real one exists: `BROWSERAI_RELEASE_FEED` points the arm at any directory
  holding a packed `BrowserAI.exe` beside its `releases.win.json` *(named
  `BrowserAI-win-Setup.exe` until 2026-09-15)*. That
  is how the layout change of 2026-09-15 was exercised on the day it landed —
  `build/New-Release.ps1 -SkipPublish -PackDir <publish> -OutputDir <scratch>
  -AllowPreRelease`, then the arm against that directory — and it is the same two
  files the capability reads out of `Releases/`.

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
  $root = (Get-Location).Path
  $root = $root.Substring(0, 1).ToUpperInvariant() + $root.Substring(1)   # C:\… — forced
  $log  = ".work\suite\release-ps-$(Get-Date -Format yyyyMMdd-HHmmss).log"
  $run  = "`$env:BROWSERAI_RELEASE_RUN='1'; `$env:BROWSERAI_DRIVE_CASE='upper';" +
          " dotnet test '$root\BrowserAI.slnx' 2>&1 | Tee-Object -LiteralPath '$log';" +
          " Get-Content .work\suite-coverage.txt | Add-Content -LiteralPath '$log'"
  Start-Process pwsh -PassThru -WindowStyle Hidden -WorkingDirectory $root `
      -ArgumentList '-NoProfile','-Command',$run
  ```

  ```bash
  root=$(cygpath -m "$PWD")                                              # C:/…
  root="$(printf %s "${root:0:1}" | tr 'A-Z' 'a-z')${root:1}"            # c:/… — forced
  log=.work/suite/release-bash-$(date +%Y%m%d-%H%M%S).log
  nohup bash -c "BROWSERAI_RELEASE_RUN=1 BROWSERAI_DRIVE_CASE=lower dotnet test '$root/BrowserAI.slnx' 2>&1 | tee $log
                 cat .work/suite-coverage.txt >> $log" >/dev/null 2>&1 </dev/null &
  ```

  ⚠️ ***Corrected 2026-08-30 (previously a PowerShell block reading `$log =
  ".work\suite\release-$(Get-Date -Format yyyyMMdd-HHmmss).log"` then
  `Start-Process pwsh … -WorkingDirectory $PWD -ArgumentList '-NoProfile',
  '-Command', "`$env:BROWSERAI_RELEASE_RUN = '1'; dotnet test 2>&1 | Tee-Object
  -LiteralPath '$log'"`, and a bash block reading `nohup bash -c
  "BROWSERAI_RELEASE_RUN=1 dotnet test 2>&1 | tee $log" >/dev/null 2>&1
  </dev/null &`).*** Those two set the release variable correctly and did
  nothing else this item asks for. Neither handed `dotnet test` an
  **explicitly-spelled absolute path**, so both inherited whatever spelling
  started the shell — **the exact 2026-08-24 failure shape the bullet three
  above this one was written to close**, reproduced inside the instrument that
  bullet points at. Neither set `BROWSERAI_DRIVE_CASE`, so
  `SuiteCoverageTests.TheRunReportsTheDriveLetterSpellingItActuallyReceived` had
  nothing to check the run against and the `drive letter` row could report only
  what the run happened to get, never whether that was what anyone asked for.
  And neither appended `.work\suite-coverage.txt` to the log, so **six release
  logs would have carried no coverage block at all** — no `release run` row, no
  `first-run bytes` row, no `filter` row — while the two paragraphs immediately
  below name the coverage block as the check on all three. Taken literally this
  fence produced six undeclared-drive runs whose evidence was missing from its
  own logs. The blocks above are now [Testing's own two
  invocations](TESTING.md#how-the-suite-is-run-detached-teed-and-the-log-polled)
  with the release variable set inside the detached shell beside the drive-case
  one; the log names carry `-ps-` and `-bash-` so that six logs in one directory
  say which shell produced each.

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
  runs download 207.3 MB from Playwright's CDN [at most once an
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
character count and both digests to use. See
[the release gate](TESTING.md#the-dated-records-are-append-only).

⚠️ **THE HEADING DATE IS INSIDE THE SEAL, and that is the trap this item sets
for a re-ship — *added 2026-09-16*.** A record is sealed from its heading, so
`## [<version>] - <date>` is part of the sealed prefix. **Changing the date
breaks the seal**, which means the date change and the re-seal are **one
commit** — and on a re-ship, where the section already exists and is already
sealed, that is the only edit there is. Split them and the gate is red in
between, on a record nobody rewrote. The failure message tells the two apart
since 2026-09-16: when the heading line is the only thing that moved it says
**the HEADING LINE changed and nothing else did**, with the seal line to paste;
a body edit still says *REWRITTEN … revert it*.
`AppendOnlyRecordTests.ADateSetAtTheCutIsReportedAsAHeadingRatherThanAsARewrite`
holds both, over a doctored copy of the real 1.0.0 section.

⚠️ **For the 2026-09-15 re-ship of `1.0.0`, the stamp step is a MERGE rather
than a new section, and both halves of that sentence matter.** The version being
cut already has a section — it was stamped on 2026-09-15 and published — so
`Get-ReleaseNotes.ps1 -StampVersion 1.0.0` **refuses**, correctly: *"already has
a section for 1.0.0. Cutting the same version twice would leave two sections
claiming the same tag."* The entries that accumulated under `## [Unreleased]`
after that stamp were merged into the existing `1.0.0` groups **by hand**,
`[Unreleased]` was left empty, and the section was re-sealed. **From the next
release onwards the step is what this item says it is** — a new section, stamped
by the command — and nothing about the script changed to permit the merge. A
re-ship of a version that has already shipped is the one case this refusal is
in the way of, and a human moving entries between two headings is a smaller
mechanism than a flag that lets the script write into a released section.

**An empty `## [Unreleased]` is legal while a release is being cut**, and since
2026-09-15 `ChangelogTests.TheChangelogHasAnUnreleasedSectionWithEntriesInIt`
knows it: the section may be empty when the tag at HEAD names the newest dated
section **or** when nothing under `src/` or `tests/` has landed since the
changelog was last written. The second clause is what covers the interval this
checklist occupies — stamped, gate running, tag not yet placed — which the first
one cannot describe, and it expires the moment a product or test change lands
without an entry.

**Evidence:** the unreleased section's contents, moved under the version being
cut, and the seal line added beside it.

### The release body is generated, and its rendering is checked before it is published

**The GitHub release body is not the changelog section.** It was, until
2026-09-15, and the result is on the record: 236,567 characters do not fit in a
field that holds 125,000, so the body was the section cut at a heading boundary
— 110,225 characters ending mid-argument, opening with four warning icons, with
a permalink line added at the cut.

**It is generated now**, from the same section, by
[`build/New-ReleaseNotes.ps1`](build/New-ReleaseNotes.ps1), which
[`build/New-Release.ps1`](build/New-Release.ps1) runs as its last step and
writes beside the release manifest:

```
pwsh -File build/New-ReleaseNotes.ps1 -Version <the version item 9 recorded> -Destination <a path>
```

Each entry becomes its one-line headline with its own icon and a **`read more`
link carrying that entry's own line range** in the changelog as the tag carries
it — `CHANGELOG.md?plain=1#L<first>-L<last>`, the source view, which highlights
exactly those lines. The footer carries the palette legend — read out of the
changelog rather than written twice — and a link to the section at the tag, whose
anchor is computed by the same slug rule `DocumentationLinkTests` applies to
every relative link in the repository.

⚠️ **THE LEGEND IS A TABLE, AND THE NEWLINES ARE THE POINT — 2026-09-17, the
maintainer's instruction**: *"The legend at the bottom of the release notes that
explains the icons is missing newlines. Give it a nice yet compact layout."*
*Previously the legend was one paragraph of twelve entries separated by `·`, and
this script joined its wrapped lines with spaces before emitting it — so a reader
of the release page met one unbroken line.* The shape is **two icon-and-meaning
pairs per row, six rows for the twelve icons, under a one-word heading row**, and
the legend in [`CHANGELOG.md`](CHANGELOG.md) carries the same table so the two
cannot disagree. **A legend that is not a table is refused rather than
flattened** — the body has no legend of its own, so the read is the only place
the shape can be held — and
`ChangelogTests.TheLegendAtTheTopIsATableListingExactlyTheApprovedPalette` and
`.ALegendThatIsNotATableRefusesTheBody` hold both halves. Rendered once through
GitHub's own renderer on 2026-09-17: **one `<table>`, six `<tbody>` rows, 24
`<td>` cells**.

⚠️ **ONE SHAPE FOR EVERY RELEASE SINCE 2026-09-16, the maintainer's choice
(Q197 b).** *Previously: "its detail is folded into a
`<details><summary>read more</summary>` block nested inside the list item".* The
fold is gone. It put the detail in the release a second time, which is what made
the body enormous, and it meant a reader met one of two documents depending on
how much had happened — the fold under the limit, headlines with nothing to
click over it. A line range points **at** the record instead of copying it.
Re-measured 2026-09-16 over the `1.0.0` section as it stands: folded is
**288,437** characters and would have fallen back to **19,780**; **linked is
41,288**, a third of the limit, with a range on every one of the 227 entries.

⚠️ **RUN IT AFTER THE TAG IS MOVED AND BEFORE PUBLISH**, which is where the
running order above already puts it — step 4 creates the tag, step 5 re-packs
(and `New-Release.ps1` generates the body as its last step), step 6 publishes.
**The generator refuses anything else**, because a line range is only true of one
file: the changelog on disk must match `HEAD`, and a tag `v<version>`, if it
exists, must be at `HEAD`. Either failing is a refusal naming both the tag's
commit and `HEAD`, and the fix is to commit the changelog or move the tag. A
dirty tree **elsewhere** is reported rather than refused — this runs after a
publish that can leave restore artifacts behind, and none of those can move a
line number in a file that matches `HEAD`. The script's last lines say which
commit the ranges are true of.

⚠️ **THE SIZE GUARD CHANGES THE DOCUMENT, so read what the script says.** Over
the limit — 125,000 characters, GitHub's, a `[FLOATS]` fact — the per-entry
links are dropped and the body becomes **headlines alone plus the footer**, whose
section link is then the only way into the detail. It is a pathological fallback
rather than a second design: at 41,288 characters for the largest release this
project has cut, reaching it takes one several times that size.

**Check the rendering before publishing, against GitHub's own renderer:**

```
gh api -X POST markdown -f mode=gfm -F text=@<the body file> > <an html file>
```

The `read more` must land as an `<a href>` **inside** the `<li>`, beside a
`<strong>` headline, and no test in this repository can see it if it does not.
*(The leading slash is omitted from `markdown` deliberately: Git Bash rewrites
`/markdown` into a filesystem path and `gh` reports an endpoint under
`C:/Program Files/Git`. And it is `-F`, not `-f`: `-f` sends the literal text
`@<path>` and the API cheerfully renders that.)* Evidence for the 2026-09-15 cut
is
[`docs/evidence/2026-09-16-release-body/rendered-0.1.0.html`](docs/evidence/2026-09-16-release-body/README.md);
for the 2026-09-16 shape, `sample.html` beside it.

**The line anchors were verified on github.com rather than assumed.** `curl`
cannot show it — the highlight is applied client-side from the fragment, and the
served HTML carries only the first 1,000 lines of a 3,682-line file. Driven in a
real browser on 2026-09-16 against
`blob/v1.0.0/CHANGELOG.md?plain=1#L3496-L3532`: the page rendered the range and
**exactly 37 elements carried a highlighted class**, which is 3532 − 3496 + 1.

⚠️ **NO TRACE OF AI, IN WORDING AND IN CHARACTER USE — 2026-09-17, the
maintainer's directive, in his words:** *"Ensure there is no trace of AI both in
wording and character use."* He added it to these rules after reading the
published `v1.0.0` body: *"the intro text of the release post is very much
reading like AI."* It sits beside the icon and layout rules above and applies to
everything a reader of the release page meets — the preamble, every headline,
the legend and the footer.

**The two halves are not enforced the same way, and this says which is which.**

- **The CHARACTER half is a test.**
  `ChangelogTests.NothingThatReachesAReleaseBodyCarriesACharacterAPersonWouldNotType`
  reads every section preamble, every entry headline, the legend, and a body
  generated from the fixture — which is how the generator's own fixed text is
  covered — and refuses eight code points: the em dash `U+2014`, the en dash
  `U+2013`, the four curly quotes `U+2018`, `U+2019`, `U+201C` and `U+201D`, the
  ellipsis `U+2026` and the non-breaking space `U+00A0`. It is a **deny list**,
  so the twelve palette icons, `U+FE0F` and any other legitimate symbol are
  allowed without being enumerated in code. A **backticked code span is exempt**,
  because a span quotes something that exists rather than choosing a style; the
  live case is this repository's own `previously "..."` token. **An entry's
  detail is out of scope by construction** — since 2026-09-16 the body carries
  headlines and a `read more` link, so the detail never reaches a reader of the
  release page.
- **The WORDING half needs a person, and nothing will ever close it.** No test
  can see that a sentence reads generated. What to look for, from the one that
  did: an em-dash aside dropped into the middle of a sentence, a parade of three
  things where two would do, *"This release holds"*, *"brings"*, and a long
  opening sentence in bold that says four things at once. Write short sentences.
  Write what a person would say to a colleague who asked what this is.

**The preamble is checked by a person, not by the test.**
`ChangelogTests.TheNewestReleasedSectionOpensWithAPreamble` holds that the
section opens with a paragraph; whether that paragraph says what this release is
for, to somebody who has never seen the project, is a reading.

**Evidence:** the shape and size the script reported, and the rendered HTML.

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
| `package.json` | `build/payload/` — the payload's own manifest, and **the only record that an npm `overrides` entry is in force**: npm writes no `overrides` block into the lock it produces |
| `payload.json` | `payload/` — Node's version, LTS name, archive SHA-256 and both tree sizes |
| `browsers.json` | `upstream-snapshots/` — the browser revisions, from the resolved payload |
| `tool-verdicts.json` | the repository root — which tools this build forwards, and the `judgedAgainst` upstream versions that judgement was made on |
| The derived version and its tag | item 9 |
| The full `.nupkg` and its size | item 12 |
| `override` | item 1 — `null` unless a human held an upstream back, and then the held version, the newest one, the break and who decided |
| `pulledForward` | **`override`'s opposite** — `null` unless an npm `overrides` entry ships a dependency *ahead* of what the packages in the payload declare for themselves, and then, per package, the version that shipped, the version the override pinned to, and what each package in the lock declares |

**Evidence:** the manifest's path, the resolved version each file states, and
what its `override` and `pulledForward` keys say.

> ⚠️ **Eight since 2026-09-18** *(previously seven — the `package.json` row
> above, the `pulledForward` row, and the word "seven" in
> `build/Write-ReleaseManifest.ps1` and in
> `build/New-Release.ps1`'s test-pack comment)*. It is here because **the lock
> cannot answer why**: measured 2026-09-17, npm writes no `overrides` block into
> the lock it produces, so a reader holding `payload.package-lock.json` alone
> sees a resolved `playwright-core` and nothing saying anybody chose it. On
> today's tree that is nothing: **`pulledForward` reads `null`, and that is the
> normal state.** *Corrected 2026-09-21 (previously "On today's tree that is
> `1.64.0-alpha-2026-09-17` where `@playwright/mcp` 0.0.81 and `playwright` both
> declare `1.64.0-alpha-2026-09-14` for themselves — a dated exception with a
> written exit, and the manifest now carries both halves of the exit condition
> rather than one").* The
> [exception ENDED 2026-09-21](DECISIONS.md#the-two-exceptions-to-the-versioning-policy),
> `build/payload/package.json` carries no `overrides` block, and every release
> cut from here on states `"pulledForward": null` unless somebody takes a new
> exception — which is a change to that section rather than an application of it.
> **The key is still emitted on every release**, for the same reason `override`
> is: a manifest silent about a direction cannot be read as saying *not that
> either*. `ReleaseScriptTests` covers both states, and since 2026-09-21 **the
> null case is the real one and the block case is the fixture** — the reverse of
> how the pair was written on 2026-09-18, and the arms say so.
> **`override` keeps its meaning exactly**: a dependency a human *held back*.
> Nothing in a release could say *pulled forward* until this key existed, so
> every manifest written before 2026-09-18 says `override: null` and is silent
> about the other direction. **The one manifest in `Releases/archive/` that
> carries a `pulledForward` block — `BrowserAI-1.0.1-alpha.0.10` — is the record
> of the four days the exception stood.**

> ⚠️ **Seven since 2026-08-26** *(previously six — the row above and the word
> "six" in `build/Write-ReleaseManifest.ps1`)*. `tool-verdicts.json` arrived at
> the repository root [with the verdict decision](ARCHITECTURE.md#the-verdicts-file-and-why-a-tool-nobody-judged-is-refused)
> and was left out of this item's charter for a week. It belongs here rather
> than in the payload's provenance: it is the file that says which tools a
> release forwards and which it refuses, and **a release that cannot produce it
> cannot answer why it refused a tool the next release allows.** The manifest
> also states the file's own `judgedAgainst` pair, because a row set means
> nothing without the upstream it was adjudicated against.

> **Corrected twice on 2026-08-16, both on the day the run raised it
> (previously: "Evidence: the manifest, beside the artifact", then "nothing
> emits this manifest, so it is assembled by hand").** The first wording named
> an artifact that had never existed and that nothing produced, so the item
> could be neither satisfied nor failed; the second described the hand-assembly
> that satisfied it once, at
> [`docs/evidence/2026-08-16-step20-manifest/`](docs/evidence/2026-08-16-step20-manifest/README.md),
> which is a checklist item
> nobody satisfies twice.
>
> ✅ **It is emitted.** `build/Write-ReleaseManifest.ps1` copies the eight files
> and writes `manifest.json`; `build/New-Release.ps1` calls it as its eighth
> step and returns the path as `ResolvedSet`. It lives in its own script for the
> same reason `Test-ReleaseVersion.ps1` does — **so the suite can drive it** —
> and `ReleaseScriptTests` runs it both ways, including that **a missing file
> refuses rather than writing a partial**, because a manifest holding seven of
> eight reads exactly like a complete one a year later. *(Seven of eight since
> 2026-09-18, previously six of seven.)*

### 12. The rollback path is publishable

The mechanics are tested by the update layer ([Testing](TESTING.md)) and
implemented in [`src/BrowserAI/Updates/`](ARCHITECTURE.md#updates). Two halves
live **outside** a test run, and both must be true at release time:

- **The full `.nupkg` for this release is archived.** Velopack prunes `packages\`
  to the current full package and deltas are forward-only, so an unarchived
  release is one you cannot roll back to without a fresh full download.

  ⚠️ **SIMPLER SINCE 2026-09-22, AND THE REASON IS A DECISION RATHER THAN A
  MECHANISM.** Every release packs **full packages only**
  ([DECISIONS](DECISIONS.md#locking-logging-versioning-and-registration)), so
  "without a fresh full download" is no longer a penalty this item is warning
  about — a fresh full download is what a rollback and an update both are. The
  archive requirement is **unchanged and matters more, not less**: Velopack
  still prunes `packages\` to the current package, so the archived `.nupkg` is
  the only copy of an older release once the feed has moved past it, and it is
  now the *whole* of what a rollback needs rather than the base of a chain.
- **The release-validation rule permits a rollback republish.** Written as
  *"monotonic **or** an explicit rollback republish"*. Get this wrong in the
  strict direction and the client accepts a rollback the build refuses to emit —
  a pipeline that has made rolling back impossible while every component
  individually supports it, which is a real and observed state rather than a
  hypothetical one. ⚠️ **Equal is neither of the two**, and the re-pack step is
  where that bites — see
  [the order the last six steps are executed in](#the-order-the-last-six-steps-are-executed-in--and-it-is-not-the-numbering).

**Evidence:** the archived package path, and the validation rule's text.

### 13. Third-party notices ship

Redistribution obligations attach at **first installer handoff**, independent of
BrowserAI's own licence. ⚠️ **That handoff happened on 2026-08-17 and this item
said it had not.** *Corrected 2026-09-15 (previously "**That handoff has not
happened yet** — *corrected 2026-08-24, and it strengthens this item rather than
relaxing it*: `v1.0.0` is a tag and a packed artifact in a gitignored
`Releases/`, and nothing has been given to anyone. This checklist is what makes
the first handoff correct rather than a record of one already made.")* — a
non-draft release has carried `BrowserAI-win-Setup.exe` and
`BrowserAI-1.0.0-full.nupkg` at
[`releases/tag/v1.0.0`](https://github.com/SixFive7/BrowserAI/releases/tag/v1.0.0)
since 2026-08-17T01:54Z, so the obligations below have been live for a month.
**The 2026-08-24 correction read the tag and the gitignored directory and did not
read GitHub**, which is the failure shape this whole file is about: a claim
re-derived from the two places that could not see the answer, and re-stamped as
verified. What this checklist makes correct is therefore the handoff it is
**about to** perform; **whether the 2026-08-17 artifact carried the six notices
below was not re-checked when this correction was written**, and the only honest
thing to record is that nobody has looked. The evidence line at the foot of this
item is still about the package this run publishes. Verified against
[README → Third-party components](README.md#third-party-components):

- **Node's full `LICENSE`** — it aggregates OpenSSL, ICU, V8, zlib and c-ares
  terms. *"A single `node.exe`, nothing else"* drops it. **Not optional.**
- The vendored `node_modules` tree **intact**, which ships `@playwright/mcp`'s,
  `playwright`'s and `playwright-core`'s Apache-2.0 `LICENSE` and satisfies §4.
  ⚠️ ***Corrected 2026-09-18 (previously "`@playwright/mcp`'s and
  `playwright-core`'s")*** — **three Playwright packages ship and two documents
  said two.** `playwright` is `@playwright/mcp`'s other exact dependency and has
  been in `build/payload/package-lock.json` since the first payload build; its
  terms and `NOTICE` are byte-identical to `playwright-core`'s, so what was
  missing was a name. `ThirdPartyNoticeTests` now enumerates the payload lock
  rather than a typed list, so a fourth package is a red build
  ([row 26](kb/re-verification.md)).
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
is the reason for it. ⚠️ **That is an answer about what we owe and not an answer
about what somebody has installed**, and from 2026-09-18 the notices carry both:
a *Browsers provisioned on first run* block names each family in
`ProvisionedBrowsers.Families`, what it is, where it is fetched to and where its
own terms live — **Firefox's are inside `omni.ja` as `license.html`, because
there is no standalone licence file in that tree at all.** Firefox has been a
provisioned family since 2026-08-19 and the notices named it only in a list of
things no copy of which ships.

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
