<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Knowledge base -- what we measured

[`../DECISIONS.md`](../DECISIONS.md) records what we **decided** and
[`../ARCHITECTURE.md`](../ARCHITECTURE.md) records how the product is **put
together**. This directory records what we **measured**, and it exists because
those are different things with different half-lives. A decision stays true until
we change our minds. An architecture stays true until the code moves. A
measurement stays true until upstream ships.

**What belongs here:** a fact about Chromium, Firefox, Playwright, Node or
Windows that we established by running something, reading a shipped binary, or
reading upstream source -- together with enough provenance to re-establish it.

**What does not:** design decisions (`../DECISIONS.md`), what implements what
(`../ARCHITECTURE.md`), work items (`../TODO.md`), or the review procedure
([`../UPSTREAM-REVIEW.md`](../UPSTREAM-REVIEW.md)).

## The articles

| Article | Holds |
|---|---|
| [`windows/job-objects.md`](windows/job-objects.md) | Holding a process tree so that killing the supervisor kills everything: job objects, breakaway, nesting, and the two implementation mistakes that leak |
| [`windows/processes.md`](windows/processes.md) | Starting a process and talking to it -- stdio, exit codes, **third-party code that writes to stdout's *handle* from a type initializer**; durable writes, deletes and renames; the Win32 interop surface behind all three |
| [`windows/detection.md`](windows/detection.md) | Finding a browser that is already running -- message windows, cross-process title reads, image-path enumeration, lock files, Windows object-name scoping, **what a mapped drive letter costs and which path aliases `Path.GetFullPath` resolves**, **why Windows hands every path back with an upper-case drive letter while a process keeps whatever casing its shell gave it**, and **named-mutex / lock-file semantics from first-party C# prior art** |
| [`windows/notifications.md`](windows/notifications.md) | What a Windows toast can show and what it silently will not -- the one-row button layout that truncates five labels to their first word, the selection input that is the way round it, and whose identity a toast wears; **and, since 2026-09-24, what raising one from BrowserAI's own binaries takes** -- the app id Velopack already sets, the NativeAOT raw-call path and its interface ids, which activation path keeps the dropdown's value, what the X and a scheduled toast do, boot time against logon time, and what the sessions page could read from a live marker |
| [`chromium/resurrection.md`](chromium/resurrection.md) | `RegisterApplicationRestart`, the 1023-character limit, and what actually brought the browsers back after a reboot |
| [`chromium/profiles.md`](chromium/profiles.md) | What Chrome does with an unusable `--user-data-dir`: fallback, exit code 21, and the dialog that blocks startup |
| [`chromium/fingerprinting.md`](chromium/fingerprinting.md) | Whether `--browser-test` is web-detectable, and the baseline exposure that makes the question near-moot |
| [`playwright/configuration.md`](playwright/configuration.md) | Config keys that are silently discarded, defaults that are not what they look like, provisioning, environment and merge order, policy, shutdown |
| [`playwright/tools-and-artifacts.md`](playwright/tools-and-artifacts.md) | Tool counts, the package shape, tools that reach credentials, and how artifacts land on disk |
| [`playwright/provisioning-and-timings.md`](playwright/provisioning-and-timings.md) | Component sizes, first-run download, and every measured latency |
| [`mcp/protocol.md`](mcp/protocol.md) | The protocol split, the client at the other end, **how BrowserAI registers itself with that client**, and the tooling around both |
| [`mcp/sdk.md`](mcp/sdk.md) | `ModelContextProtocol` as a proxy has to drive it: the deviations, NativeAOT and ILC, the two custom transports, the in-process harness and lossless passthrough |
| [`packaging/velopack.md`](packaging/velopack.md) | The update path, its nine landmines, the install/update/rollback verification of each, and the restart/mutex handover race |
| [`packaging/dependencies.md`](packaging/dependencies.md) | Package provenance with date stamps, token cost, the licence terms of everything shipped, and **what vendoring a runtime cost two long-lived repositories** |
| [`toolchain.md`](toolchain.md) | MSBuild, NuGet, npm, PowerShell, analyzers, git line endings and the test host -- traps in the tooling that builds this, none of them about processes or browsers |

Two pages are not articles and are maintained differently:

| Page | What it is for |
|---|---|
| [`re-verification.md`](re-verification.md) | The **build gate**. Every `[FLOATS]` fact a version bump can silently invalidate, what would break it, and whether a test covers it. Read line by line at every upstream review; three of its numbers are asserted on every build |
| [`not-established.md`](not-established.md) | The negative results, aggregated: what this project has explicitly **not** measured, and why. Read it before treating a gap here as an oversight -- several of them are deliberate and will stay open |

## Conventions

Every entry carries a marker and a date:

| Marker | Meaning |
|---|---|
| **`[FLOATS]`** | Depends on a version this project floats. **Re-verify at every upstream review.** Listed in the [re-verification index](re-verification.md). |
| **`[STABLE]`** | A Windows or protocol fact that upstream cannot move. Re-verify only on a Windows major version. |
| **`[MACHINE]`** | Measured on the reference machine described below, and not established anywhere else. The number is real; its generality is not claimed. Never act on one as if it were universal. |
| **`[UNVERIFIED]`** | Inferred, not observed. Says so, and says why it was not observed. |
| **`[STALE]`** | A re-check is owed and has not happened. The sanctioned alternative to guessing, and the only honest way to leave an entry whose measurement could not be re-run. That **2** articles carry one today is the marker doing its job and not a fault. *Corrected 2026-09-17 (previously "That **1** article carries one today")* -- [`playwright/provisioning-and-timings.md`](playwright/provisioning-and-timings.md) joined it that day, with three sections stamped: the `playwright-core` pull-forward to 1.64.0-alpha-2026-09-17 moved chromium 1244 → 1245 and firefox 1544 → 1548, and the first-run download, the Firefox-against-Chromium cost ratios and the resume cost are all keyed to a revision. **Every number in them is left exactly as measured**, because adjusting one to match an expectation is the thing this marker exists instead of; re-taking them is three measurement sessions with rigs that already exist, and [re-verification](re-verification.md) rows 21, 34 and 38 carry the debt. ✅ *Amended 2026-09-17, later the same day: **one of the three is re-taken and two are still owed**.* [Row 21](re-verification.md) was re-run at chromium 1245 and firefox 1548 and its section's stamp is cleared -- Chromium came back identical to the byte and the file, because its archive is keyed on `browserVersion` and that did not move, and Firefox moved by 327 bytes on the wire and 902 on disk. **The article count did not move and is still 2**, because rows 34 and 38 -- the cost ratios and the resume cost -- are still stamped in place and still owed; re-counted, not decremented. ✅ *Amended again 2026-09-17, later still: **two of the three are re-taken and one is owed**.* [Row 34](re-verification.md) was re-run at chromium 1245 and firefox 1548 at **six rounds per family** and its section's stamp is cleared -- RAM, profile disk and process count came back identical, first navigate moved inside its own spread, second navigate moved, and the idle-CPU axis is **withdrawn, not re-stated**: it reversed sign for the second time in a day against a Chromium binary that is identical to the byte, which makes it a fact about the instrument. **The article count is STILL 2** -- re-counted, not decremented -- because row 38's resume section is still stamped and because this article carries a third, unrelated stamp of its own on the derived disk-after-first-run total. That last one is why clearing every owed row here would still leave the count at 2, and saying so now is cheaper than the next reader deriving it. ✅ *Amended a third time 2026-09-17, and this is the last of them: **all three are re-taken and none is owed**.* [Row 38](re-verification.md) was re-run at chromium 1245 and firefox 1548 and its section's stamp is cleared -- the durability claim held exactly and on two families, the cost moved 9%, and *resume* turned out to be **two paths** since that morning and not one, so the entry now says which path each number is of. **The count is STILL 2, and the sentence above predicted exactly that** -- re-counted, not reasoned: `playwright/provisioning-and-timings.md` keeps its third, unrelated stamp on the derived disk-after-first-run total, which goes stale again on every publish by construction, and `packaging/velopack.md` keeps its own. **Three owed rows were cleared in one day and the number did not move**, which is the marker measuring articles and not debts and matters before anyone reads a flat count as a lack of progress. The other is [`packaging/velopack.md`](packaging/velopack.md), which carries it for *what Velopack costs the AOT binary*, a pair measured either side of adding the package -- re-taking it means building a tree with the update lane removed, which is a different product, not a different measurement, so it is stamped in place with that as the reason. *Corrected 2026-09-17 (previously "[`packaging/velopack.md`](packaging/velopack.md)'s update-lane figures are owed a re-measurement that needs two real release publishes, and they say so at the head of the entry instead of reading as current")* -- **those two publishes were run on 2026-09-17** and [the sizes table](packaging/velopack.md#sizes) is measured again; the count did not move because one clause of that section is genuinely still un-re-runnable and kept its stamp, which is the marker doing the narrower job and not the article failing to clear. |

**Never edit a result without re-running the measurement.** An entry whose number
was updated by reasoning instead of by running something is worse than no entry,
because it reads identically to one that was measured. If a re-check is owed and
has not happened, mark it `[STALE]` instead of guessing.

> **The last sentence of the `[STALE]` row above is a count, and it is asserted
> on every build** by
> `RecordedCountTests.TheStaleMarkerCountInTheArticleIndexIsWhatTheArticlesHold`.
> **Stamping an entry and moving that sentence are one edit, not two:** the check
> reads the clause -- *"That no article carries one today"*, or *"That N articles
> carry one today"* -- and holds it against the articles, so a stamp with the
> sentence unmoved is red, and so is a sentence claiming a stamp no article
> carries. A **backticked** marker is the stamp; the bracketed token written any
> other way is prose about the convention and counts as nothing. *Added
> 2026-08-27, when that check stopped matching the bare token everywhere under
> `kb/` -- under which an article could not discuss this marker at all, and the
> escape hatch this section prescribes would itself have failed the build.*
>
> **Exercised for the first time 2026-08-29**, when the velopack update-lane
> entry took the stamp its own paragraph had spent two days explaining it could
> not take, and the count above moved from *no* to *1* in the same commit. **The
> stamp was watched red against the unmoved sentence first** -- the failure named
> the stamping article and named the resolution, which is precisely what the
> superseded guard could not do -- and the other direction is held by that check's
> own synthetic controls and not by doctoring this file.

### The four marks, written down 2026-09-23

The markers above say what a fact's half-life is. Four glyphs are used beside
them, and until today they were a habit kept by whoever wrote the entry. They
are a convention now, and this table is what each one means. **All four are
emphasis and none of them is data**: no test reads one, nothing in the product
parses one, and deleting every one of them would change no claim in this
directory.

| Mark | Meaning |
|---|---|
| ⚠️ | **Read this before acting on what is beside it.** A caveat, a trap, a correction, a limit the surrounding claim does not carry, or a thing the entry explicitly does not establish. It is the only one of the four that changes how the text around it should be read. |
| ✅ | **Something that was open or owed has been settled, and what follows is the settlement.** A re-verification row re-established, a staleness stamp cleared, a flagged defect closed, an unverified branch identified, an open question decided. It carries the date and the versions the re-run was taken at, and it never marks a fact that was never in doubt. |
| ⭐ | **The reading the section was run for.** The surprise, the control that turns out to decide the question, or the one arm of a matrix the other arms exist to frame. One or two in an entry at most, and a reader skimming everything else should still stop here. |
| ⛔ | **What this was measured for has been dropped.** The measurement itself stands and is kept, because it is the evidence for the decision that dropped it; nothing in the product consumes it any more. |

**The check mark and the star answer different questions.** A re-run that comes
back identical takes the check mark, because what it settles is the debt; it
takes the star only when coming back identical is itself the finding. And a
check mark closes a question, while the no-entry mark records that the thing
which asked the question went away.

A fifth mark is a change to this table, not a precedent.

### The reference machine

`[MACHINE]` entries were measured here. Nothing about this configuration is
special; it is written down so that a number measured on it can be compared
against a number measured somewhere else, which is the only thing that makes one
of these entries useful to a reader who is not sitting at it.

| | |
|---|---|
| OS | Windows 11 Pro 26200, x64 |
| .NET | SDK 10.0.400, runtime 10.0.11, ILC 10.0.11 |
| Shell | PowerShell 7 and Git Bash |
| Session | Interactive, medium-integrity, UAC-filtered administrator |
| Load | An ordinary developer desktop -- a browser, an editor and a dozen unrelated Electron/CEF applications, several of which publish real user-data-dirs and message-only windows of their own |

That last row is not colour. Several measurements here -- window-walk counts,
process-enumeration timings, the Restart Manager's cost -- scale with what else is
running, and a reader reproducing them on an idle VM should expect different
numbers and the same conclusions.

### Versions

Unless an article states otherwise, entries were measured against:
`@playwright/mcp` 0.0.79 · `playwright-core` 1.63.0-alpha-2026-08-05 ·
Node 26.7.0 (libuv 1.52.1) · Chrome for Testing 152.0.7977.8 (`chromium-1237`) ·
Firefox 153.0 (`firefox-1539`) · `ModelContextProtocol` 2.2.0 · Velopack 1.2.0.

**Each article also carries its own version line**, so that extracting one does
not silently strip the versions its numbers were measured under. This block is a
convenience, not the source of truth: if the two ever disagree, the article is
right, because it is the one that gets edited when a measurement is re-run.

> ⚠️ **The Node above is not the Node that ships.** Measurements here ran on
> **26.7.0**, the Current line; the bundled runtime is **v24.19.0 LTS**. That gap
> matters in one place specifically: **libuv's permissive job object**
> ([kb](windows/job-objects.md)) sits in the chain between BrowserAI's job and
> the browser, and the containment guarantee is stated as holding *through* it.
> That was established against libuv 1.52.1 as shipped in 26.7.0 and **has not
> been re-checked on v24.19.0** -- not "checked and found identical".
>
> **Half closed, 2026-08-16.** `JobContainmentTests.
> TheBundledNodeAndItsDescendantsAreContained` runs the bundled **v24.19.0** and
> its `child_process.spawn` grandchildren inside BrowserAI's own job, and
> containment holds -- 4 processes, 0 escapees, 0 survivors, twice. What that
> does **not** establish is the libuv claim itself: the test observes
> containment, not whether libuv still creates its permissive global job under
> this version. [Row 2](re-verification.md) covers the measured half; the source
> claim is unchanged and still owed a read of `src/win/process.c` at the version
> that ships.
