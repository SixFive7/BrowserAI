<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

## Pre-release checklist

**This is the only gate that exists.** Decided 2026-08-16: no GitHub Actions, no
scheduled task, no git hook. Nothing else stands between a change and a shipped
release.

Every item is **executed and evidenced**, and **any failing item blocks the
release**. A failure is a work item, never a waiver — the response to a breaking
upstream change is to make the newest version work
([rule 4](../README.md#the-five-rules-that-make-floating-safe)), and where there
is no forward fix, **blocking the release indefinitely is the intended answer.**

**Green is necessary and not sufficient.** This checklist decides whether a
release is *permitted*, never whether one *happens*. A human decides when a green
build becomes a release — [README → Release trigger](../README.md#settled-2026-08-14)
and [the release gate](testing.md#the-release-gate). Nothing here overrides that,
and item 14 is where it lands.

**This file points; it does not restate.** [The release gate](testing.md#the-release-gate)
owns the six-step sequence and [Testing](testing.md) owns the enumerated suite.
Where a rule is already specified, the item below names the evidence to record
and links to the rule. A second copy of a fact in this repository is a defect.

### Evidence, and what does not count

Record, for each item: **what was run, and what it returned.** A version number,
a count, a diff, an exit code, a file size.

What does not count: a restatement of the rule, *"as expected"*, or a date
written from intent. [`drift-check.json`](../drift-check.json) already carries
this rule for one field — *a date written from intent reads identically to a real
one and silences the next check for a day* — and it applies to every line of this
file. An item whose evidence is *"I believe this is fine"* is not evidence; it is
worse than a gap, because a gap announces itself.

**Where the evidence goes:** beside the release, with the resolved-set manifest
that [rule 1](../README.md#the-five-rules-that-make-floating-safe) already
requires. The adjudications in items 3–6 go in the
[`upstream-review.json`](../upstream-review.json) entry, which is where the suite
reads them from. Not in this file — this file is the list, not the log.

> **Prerequisites that do not exist yet.** Verified 2026-08-16: there is no
> `CHANGELOG.md` in this repository, so **item 10 cannot be checked until
> [build order step 18](build-order.md) creates it**. The snapshots, the marker
> test and the suite that items 3–8 rest on arrive at
> [build order steps 4, 8 and 9](build-order.md). Until then, an item naming a
> test names something that has not been built, and saying so is the honest
> reading of a checklist against a repository with no product code in it.

---

## Resolve

### 1. Everything re-resolved to latest, and green

The versioning policy is that everything floats and the build freezes it. **A
release may only be cut when everything has been re-resolved to latest and every
check passes.** No pinning to an old version without the maintainer knowing.

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

**Evidence:** `git diff -- "**/packages.lock.json"` — the diff *is* the drift
report, and it is the cheapest detector this policy has. Record it, empty or not;
an empty diff is a result. Plus the resolved version of each of the five
upstreams and the browser revision.

> **If this item is doing real work at release time, the working rhythm has
> drifted, and that is itself a finding.** The maintainer's rule of 2026-08-16 is
> that **updating everything is the first step of touching this project, not a
> step before release** — re-resolve, fix the fallout, then do the work. Doing it
> here for the first time is how one upgrade nobody ever takes gets built.

### 2. No pin anywhere

Every package version lives in `Directory.Packages.props` as `*`. A `Version=` on
a `PackageReference`, or a version literal in any `.csproj`, is a pin — and a
pin is invisible once it exists, because a stale number reads exactly like a
current one.

**Evidence:** the check from [build order step 1](build-order.md), run, with its
output.

---

## Adjudicate

### 3. Upstream drift adjudicated

Resolve the five upstreams **the way the build resolves them** — the table in
[`CLAUDE.md` → the daily drift check](../CLAUDE.md#the-daily-drift-check). A
registry query's defaults are not that: on 2026-08-15, npm `latest` for
`playwright-core` was `1.62.1` while the shipping version was
`1.63.0-alpha-2026-08-05`.

**Drift blocks the release.** If any resolved version is newer than the reviewed
one in [`upstream-review.json`](../upstream-review.json), run
[`UPSTREAM-REVIEW.md`](../UPSTREAM-REVIEW.md) before going further. Finding a
newer version does **not** license editing the marker; the review does.

The marker test enforces this and is red until the entry adjudicates what moved.
**A red marker is not a stale file to fix.** If the diff is large, split it: bump
to an intermediate version, review, land it green, then bump again.

**Evidence:** the resolved-versus-reviewed pair for each of the five upstreams,
and the marker test's result. [`drift-check.json`](../drift-check.json) stamped
with `lastChecked` **only after a lookup actually returned a version.**

### 4. The four snapshots adjudicated

`tools-list.json`, `cli-help.txt`, `config-schema.d.ts`, `browsers.json` —
regenerated from the resolved payload and diffed. The mechanism is
[the upstream-review gate](testing.md#the-upstream-review-gate); read it there.

**Evidence:** for each of the four, `unchanged`, or the marker entry's
adjudication of exactly what moved. A snapshot that changed without an
adjudication fails the gate, so this item is answered by the suite being green —
what is recorded here is the adjudication text, not a second assertion.

A moved `browsers.json` deserves its own line in the release notes: every machine
re-downloads the browser and re-extracts it, and the old revision sits on disk
until something prunes it.

### 5. Upstream tool-description drift adjudicated

**New, and it closes a gap nothing else covers.** BrowserAI's tool descriptions
are **append-only** on top of upstream's
([README → Tool naming](../README.md#settled-2026-08-14)). Upstream can reword
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
model relies on — is a test, not a checklist item
([build order step 13](build-order.md)). A build gate needs no evidence here
beyond the suite being green.

**Evidence:** for every tool whose description moved, the composed description as
the model will see it, and a yes/no on whether the appended text still reads
correctly beside the new wording.

### 6. The re-verification index answered

[`kb/` → re-verification index](../kb/README.md#re-verification-index) lists the
measured facts a version bump can silently invalidate — the half of the review no
snapshot can do.

- **Automated rows are answered by the suite** and need nobody.
- **Every manual row must be answered by name, with an outcome**, in the marker
  entry.
- **A row that is neither automated nor answered fails the gate.**

**Never update a measured fact by reasoning. Re-run the measurement, or mark the
entry `[STALE]`.** An adjusted number is indistinguishable from a measured one,
which makes it worse than a gap.

**Evidence:** the marker entry's `reverification` block, one outcome per manual
row.

---

## Build and run

### 7. Build clean

NativeAOT publish, analyzers at error severity. **A warning-as-error is a red
build**, and a severity is never weakened to make code pass. ILC output empty.
`UseSystemResourceKeys` never set — it strips the exception messages this project
exists to be able to read.

**Evidence:** the publish command, its exit code, and the warning count, which is
zero.

### 8. Run everything

All five layers, including the two marked *mandatory before release*. **Not a
subset, not "the fast ones", not "the ones related to this change".** The layers,
their cadences and the enumerated tests are in [Testing](testing.md) — this item
does not restate them.

Three things to record rather than assume, because each is easy to skim past:

- **The skipped count, which must be zero.** No `Skip`, no quarantine, no
  conditional ignore anywhere in the tree. A `Skip` at release time is a red
  build wearing a disguise, and flakiness is a defect to fix rather than a state
  to tolerate.
- **Every tool classified.** An unclassified tool fails the build. That rule is
  what turns an upstream addition into a red run instead of a security incident.
- **The smoke layer ran against a real browser**, not against an empty browsers
  directory that would let the batteries-included premise be silently dead code.

**Evidence:** total, passed, failed and skipped counts from the run's own output,
and the exit code.

> **This whole checklist rests on this item being *run* rather than assumed.**
> [The release gate](testing.md#the-release-gate) says exactly that about its own
> step 3, and it is the honest cost of the 2026-08-16 decision to have no
> automation.

---

## Version and record

### 9. The version is derived, and `0.0.0` is refused

Versions come from **git tags** — three parts plus a pre-release suffix, the
shape the packager accepts. Nothing hand-edited. `0.0.0` means the derivation
found no tag: a build that does not know what it is, and therefore a build that
cannot be rolled back to or bisected against. **Refuse it.**

**Evidence:** the version the build stamped, and the tag it came from.

### 10. The changelog's unreleased section is not empty

**Refuse to release on an empty unreleased section.** A release with nothing to
say is a release nobody can describe afterwards — and the first thing a rollback
needs is a statement of what changed.

**Prerequisite:** verified 2026-08-16, `CHANGELOG.md` does not exist in this
repository. [Build order step 18](build-order.md) creates it; until then this
item cannot be checked, and saying so is the check.

**Evidence:** the unreleased section's contents, moved under the version being
cut.

### 11. The resolved set is recorded beside the artifact

`packages.lock.json`, the resolved `package-lock.json`, the browser revisions
from the resolved `browsers.json`, and the Node version.

**An artifact that cannot state exactly what went into it is not releasable** —
that is what makes a rollback meaningful and a regression bisectable
([rule 1](../README.md#the-five-rules-that-make-floating-safe)).

**Evidence:** the manifest, beside the artifact.

### 12. The rollback path is publishable

The mechanics are tested by the update layer ([Testing](testing.md)) and
specified in [§G](G-updates.md). Two halves live **outside** a test run, and both
must be true at release time:

- **The full `.nupkg` for this release is archived.** Velopack prunes `packages\`
  to the current full package and deltas are forward-only, so an unarchived
  release is one you cannot roll back to without a fresh full download.
- **The release-validation rule permits a rollback republish.** Written as
  *"monotonic **or** an explicit rollback republish"*, or the client accepts a
  rollback the build refuses to emit — which is the state `ExoFabric/UCC` is in
  today.

**Evidence:** the archived package path, and the validation rule's text.

### 13. Third-party notices ship

Redistribution obligations attach at **first installer handoff**, independent of
BrowserAI's own licence, and no test covers them. Verified against
[README → Third-party components](../README.md#third-party-components):

- **Node's full `LICENSE`** — it aggregates OpenSSL, ICU, V8, zlib and c-ares
  terms. *"A single `node.exe`, nothing else"* drops it. **Not optional.**
- The vendored `node_modules` tree **intact**, which ships `@playwright/mcp`'s
  and `playwright-core`'s Apache-2.0 `LICENSE` and satisfies §4.
- Velopack's MIT notice.
- **A short trademark disclaimer in the installed artifact.** Apache-2.0 §6
  grants no trademark rights, and the inherited `browser_*` names surface
  upstream branding directly in BrowserAI's own API.

Nothing the user's machine downloads on first run creates an obligation for us —
we ship no copy of it. That is not a side benefit of first-run provisioning; it
is the reason for it.

**Evidence:** the paths of each notice file inside the packaged artifact.

---

## The decision

### 14. The maintainer decides

Green is **releasable**, not **released**. There is no release pipeline, no
scheduled publish and no auto-merge on green. Nothing in items 1–13 authorises a
release; they only permit one.

**Evidence:** the maintainer said so.

---

## Two things to inherit rather than rediscover

### Nothing makes this gate fire

It works when it is invoked, and **there is no mechanism anywhere that invokes
it.** That is deliberate, decided 2026-08-16: it allows many commits without
re-running everything each time, and it keeps the project off hosted CI.

The cost, stated plainly so it is inherited as a decision:

- **The gate is only as good as the person invoking it.** It rests entirely on
  the run happening rather than being assumed.
- The one gap it leaves — *upstream moved while nobody was looking* — is covered
  by the [daily drift check](../CLAUDE.md#the-daily-drift-check), which is a
  directive rather than a job, and which fires by construction because this
  project is built entirely through an agent: the check happens because the work
  happens.

**Review this after the product is finished.** The condition that ends the
arrangement is already named in
[the release gate](testing.md#the-release-gate): the day a second person can cut
a release, the assumption breaks and the gate has to move into automation.

### Being green is necessary and not sufficient

Stated twice on purpose, because the two halves fail differently. A red item
means there is **nothing to decide**. A green run means there is **something to
decide**, and the deciding is a human's. This checklist never says *ship it*.
