<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Knowledge base — what we measured

[`../README.md`](../README.md) records what we **decided** and [`../PLAN.md`](../PLAN.md)
records what we are going to **build**. This directory records what we
**measured**, and it exists because those are different things with different
half-lives. A decision stays true until we change our minds. A plan is consumed
as the code gets written. A measurement stays true until upstream ships.

**What belongs here:** a fact about Chromium, Firefox, Playwright, Node or
Windows that we established by running something, reading a shipped binary, or
reading upstream source — together with enough provenance to re-establish it.

**What does not:** design decisions (`../README.md`), the implementation plan
(`../PLAN.md`), work items (`../TODO.md`), or the review procedure
([`../UPSTREAM-REVIEW.md`](../UPSTREAM-REVIEW.md)).

## The articles

| Article | Holds |
|---|---|
| [`windows/processes.md`](windows/processes.md) | Job objects and containment; stdio, exit codes and process startup — **including third-party code that writes to stdout's *handle* from a type initializer**; durable writes; recursive-enumeration and interop traps |
| [`windows/detection.md`](windows/detection.md) | Finding a browser that is already running — message windows, cross-process title reads, image-path enumeration, lock files, Windows object-name scoping, and **named-mutex / lock-file semantics from the C# prior art** |
| [`chromium/resurrection.md`](chromium/resurrection.md) | `RegisterApplicationRestart`, the 1023-character limit, and what actually resurrected the maintainer's browsers |
| [`chromium/profiles.md`](chromium/profiles.md) | What Chrome does with an unusable `--user-data-dir`: fallback, exit code 21, and the dialog that blocks startup |
| [`chromium/fingerprinting.md`](chromium/fingerprinting.md) | Whether `--browser-test` is web-detectable, and the baseline exposure that makes the question near-moot |
| [`playwright/configuration.md`](playwright/configuration.md) | Config keys that are silently discarded, defaults that are not what they look like, provisioning, environment and merge order, policy, shutdown |
| [`playwright/tools-and-artifacts.md`](playwright/tools-and-artifacts.md) | Tool counts, the package shape, tools that reach credentials, and how artifacts land on disk |
| [`playwright/provisioning-and-timings.md`](playwright/provisioning-and-timings.md) | Component sizes, first-run download, and every measured latency |
| [`mcp/protocol.md`](mcp/protocol.md) | The protocol split, the client at the other end, and the tooling around both |
| [`mcp/sdk.md`](mcp/sdk.md) | `ModelContextProtocol` as a proxy has to drive it, including the 2026-08-15 spike and the 2026-08-16 NativeAOT/ILC additions |
| [`packaging/velopack.md`](packaging/velopack.md) | The update path, its nine landmines, the install/update/rollback verification of each, and the restart/mutex handover race |
| [`packaging/dependencies.md`](packaging/dependencies.md) | Package provenance with date stamps, token cost, the licence terms of everything shipped, and **what vendoring a runtime cost two in-house repositories** |
| [`history.md`](history.md) | The legacy setup this project replaces, and corrections applied after the fact |

## Conventions

Every entry carries a marker and a date:

| Marker | Meaning |
|---|---|
| **`[FLOATS]`** | Depends on a version this project floats. **Re-verify at every upstream review.** Listed in [Re-verification index](#re-verification-index). |
| **`[STABLE]`** | A Windows or protocol fact that upstream cannot move. Re-verify only on a Windows major version. |
| **`[MACHINE]`** | True of the maintainer's machine, not of the world. Never generalise; never act on it as if universal. |
| **`[UNVERIFIED]`** | Inferred, not observed. Says so, and says why it was not observed. |

**Never edit a result without re-running the measurement.** An entry whose number
was updated by reasoning rather than by running something is worse than no entry,
because it reads identically to one that was measured. If a re-check is owed and
has not happened, mark it `[STALE]` rather than guessing.

Versions in force for everything below unless stated otherwise:
`@playwright/mcp` 0.0.79 · `playwright-core` 1.63.0-alpha-2026-08-05 ·
Node 26.7.0 (libuv 1.52.1) · Chrome for Testing 152.0.7977.8 (`chromium-1237`) ·
system Google Chrome 151.0.7922.138 · Firefox `firefox-1539` · Windows 11 Pro
26200.

> ⚠️ **The Node above is not the Node we ship.** Measurements here ran on
> **26.7.0**, the Current line; the bundled runtime is **v24.19.0 LTS**
> ([§A](../plan/A-runtime.md#a-ship-and-own-the-runtime)). That gap matters in one place
> specifically: **libuv's permissive job object**
> ([kb](windows/processes.md#job-objects-and-process-containment)) sits in the
> chain between BrowserAI's job and the browser, and the containment guarantee is
> stated as holding *through* it. That was established against libuv 1.52.1 as
> shipped in 26.7.0 and **has not been re-checked on v24.19.0** — not "checked and
> found identical". Re-run `.work/jobtest/` under the bundled `node.exe` before
> the guarantee is claimed for a shipped build; nothing else here is known to
> depend on the Node version.
>
> **Half closed, 2026-08-16.** `JobContainmentTests.
> TheBundledNodeAndItsDescendantsAreContained` runs the bundled **v24.19.0** and
> its `child_process.spawn` grandchildren inside BrowserAI's own job, and
> containment holds — 4 processes, 0 escapees, 0 survivors, twice. What that
> does **not** establish is the libuv claim itself: the test observes
> containment, not whether libuv still creates its permissive global job under
> this version. Row 2 covers the measured half; the source claim above is
> unchanged and still owed a read.

## Re-verification index

Every `[FLOATS]` fact is *meant* to be re-checked at upstream review, and this
table is how that happens. **It does not yet cover all of them**: **147**
`[FLOATS]` markers stand across the articles against the **61** numbered rows
below (67 lines, counting 2a, 2b, 4a, 4b, 4c and 16a),
because one row often stands for a cluster of related entries and because rows
have simply been missed — two whole articles carried none until 2026-08-15. Read a
missing row as a gap in this table, never as permission to skip the fact.

> **These three numbers are now checked on every build**, by
> `ReVerificationIndexTests` — it re-runs the count below, counts the rows, and
> fails if the sentence above disagrees with either. That is build-order step
> 4's answer to a note that had already drifted twice: a tally maintained by
> hand is a tally that is wrong, and the two paragraphs after this one are what
> that looked like. **The test reads the sentence above as its anchor**, so
> rewording it fails the suite rather than quietly unhooking the check.

> **There was an earlier row 54, added and withdrawn on 2026-08-16.** The
> number has since been reused by a different fact, so read this note as being
> about the withdrawal rather than about whatever row 54 says today. It asserted
> that
> `dotnet test` runs zero tests on this toolchain. It does not: the same command
> returns 51 passed / exit 0, and a fresh worktree of the very commit the row
> cited as its proof returns 30 passed / exit 0. One transient observation had
> been written up as a standing property, so the row is **deleted rather than
> marked**, because this index lists facts that must be re-checked and there is
> no fact left to re-check. The retraction itself is kept, in
> [the article](windows/processes.md#interop-and-the-toolchain), in
> [`TODO.md`](../TODO.md) and on
> [build-order step 5](../plan/build-order.md), so a reader who met the original
> meets the correction. **The row was caught by this table's own test** —
> `EveryRowIsEitherManualOrNamesSomethingThatExists` refused `— *withdrawn*` in
> the `Automated by` column, which is the mechanism working on the person
> maintaining it rather than only on upstream.

> **The 124 is a marker count, not an entry count** — re-counted 2026-08-16 with
> `grep -ro "\[FLOATS\]" --include=*.md . | grep -v '^\./\.work/'`, then
> subtracting this file's own five occurrences. Some entries carry a split
> marker (`[FLOATS]` for the numbers, `[STABLE]` for the mechanism) and are
> counted once each, so the true number of distinct floating *facts* is somewhat
> lower. It is recorded this way because a reproducible command beats a hand
> tally that silently drifts.
>
> **Corrected 2026-08-16 (previously "97 … against the 41 numbered rows …
> excluding this file's own four occurrences").** Both halves were wrong before
> anything was added today. Re-running the command as it was written returns
> **120**, because it has no exclusion and sweeps `.work/`, the gitignored
> scratch directory, which carried 7 markers in spike code that is not part of
> this knowledge base at all; and this file has carried **five** occurrences,
> not four. Netting those out gives **103** on the tracked documents before
> today's work, against a recorded 97. Five new markers landed with build-order
> step 1 (rows 42–44), which is the 108. The command above is the corrected one
> and excludes `.work/` explicitly — a count nobody can reproduce is the tally
> this note exists to prevent.
>
> **Re-counted again 2026-08-16 after build-order step 3: 117 across the
> articles.** Eight new markers, all covered by rows 46–50 below —
> `.links/`'s real location, Node's `LICENSE` not being published beside
> `node.exe`, the payload tree's shape, two npm/PowerShell toolchain traps, and
> the two provisioning findings in row 50. Subtract what the command actually
> reports for this file, never the number written down last time. The command
> needed no other change: `payload/` is a build output and holds no marker, so
> excluding it or not gives the same total. Run it both ways if that ever stops
> being true.
>
> ⚠️ **Corrected 2026-08-16 (previously "123 raw, minus this file's six, is
> 117", and "the self-reference count moved from five to six").** Both inputs
> were wrong and cancelled: measured against the step-3 commit itself,
> `git grep -o "\[FLOATS\]" 9410876 -- '*.md'` returns **122**, and this file
> held **five** occurrences, not six — the note claiming six did not add one.
> 122 − 5 is the 117 that was recorded, so the answer survived two wrong
> operands, which is precisely why the count is now a test instead of a
> sentence.
>
> **Re-counted 2026-08-16 after build-order step 4: 124.** Seven new markers —
> four on the per-capability tool breakdown that step measured, and three
> toolchain traps found wiring the gate (rows 51 and 52). Two new rows, so the
> table is 52 numbered rows over 54 lines.
>
> **Re-counted 2026-08-16 after build-order step 5: 132.** Eight new markers —
> six in [kb: SDK](mcp/sdk.md#added-2026-08-16--writing-the-two-transports-at-220)
> from writing the two transports against 2.2.0, one naming the four
> `PLAYWRIGHT_DOWNLOAD_HOST` variants from the resolved bundle, and one for
> `dotnet test` running zero tests on this toolchain. Two new rows (53 and 54),
> so the table is 54 numbered rows over 56 lines. **Row 31 moved from *manual*
> to a test** — the first row this build has automated by writing the code the
> row was waiting for, which is the shape the column exists to reward.
>
> **Re-counted 2026-08-16 after build-order step 6: 135.** Three new markers —
> the containment numbers measured against the product's own job and launcher,
> the double-hyphen XML trap, and `BannedApiAnalyzers` merging multiple banned
> lists. Two new rows (54 and 55) and one **split**: row 2 was one row covering
> both process trees and browsers, and only the first half is now automated, so
> the browser half is row 2a and stays *manual* until [step
> 15](../plan/build-order.md#15-first-run-provisioning-and-browserai_reinstall_browser)
> has a browser to run it against. Splitting rather than marking the whole row
> automated is the same rule as naming a test that does not exist: a row that is
> half covered reads as covered. **Row 2 moved from *manual* to a test.** The
> table is 55 numbered rows over 58 lines.
>
> **Re-counted 2026-08-16 after build-order step 7: 144.** Nine new markers —
> four in [kb: upstream config](playwright/configuration.md) from measuring the
> sandbox three ways and finding the mechanism behind it, one in
> [kb: protocol](mcp/protocol.md) for the `server/discover` asymmetry, three in
> [kb: SDK](mcp/sdk.md#added-2026-08-16--the-published-binary-at-220) from the
> published binary, and one in
> [kb: processes](windows/processes.md#job-objects-and-process-containment) for
> containment after ILC. Three new rows (56–58), one new sub-row (2b) and one
> **split** (4b into 4b and 4c), so the table is 58 numbered rows over 63 lines.
>
> **Four rows moved from *manual* to a test, which is the point of the column.**
> Rows **3** (`SandboxFlagTests`), **4b** (`HeadlessBinaryTests`), **15**
> (`ProtocolSplitTests`) and the new **2b**, **56** and **58**. Rows 3, 4b and 15
> had named tests that did not exist and were put back to *manual* at step 4;
> this is the step that wrote them. **Row 7 did not move** — it is the
> `browser_get_config` round trip, which needs the `config` capability BrowserAI
> does not yet enable, and it stays *manual* until
> [step 12](../plan/build-order.md#12-the-session-tools-and-config-generation).
> Its binary-selection half is now row 4b's; what is left in row 7 is the part
> only the child can answer. **Row 27 did not move either**: the suite can assert
> things about a published binary but cannot run `dotnet publish`, so the
> zero-warning and `will always throw` checks stay an operator step, and only its
> *"run the published binary against a real child"* half is covered, by row 2b.
>
> **Re-counted 2026-08-16 after build-order step 8: 147.** Three new markers,
> all in [kb: SDK](mcp/sdk.md#added-2026-08-16--the-in-process-harness-at-220)
> from driving the proxy against a scriptable double — what an exception escaping
> a `CallToolHandler` turns into, what survives a child's JSON-RPC error, and
> which teardown step does what. Three new rows (59–61) and one **split** (16
> into 16 and 16a), so the table is 61 numbered rows over 67 lines. **Row 16
> moved from *manual* to a test**, and its wall-clock half became 16a rather than
> riding along: the in-process arm pins the probe timeout deliberately short, so
> it can prove the *mechanism* and can say nothing about the ~300 ms production
> baseline. Counted with the command above rather than by adding to the last
> figure.

**The `Automated by` column is what makes this a gate rather than a checklist.** A row naming a test is answered by the suite and needs nobody. A row marked *manual* **must be answered by name in the [`upstream-review.json`](../upstream-review.json) entry**, with an outcome — whether an upstream change touches one of our abstractions is judgement, and judgement cannot be asserted mechanically. **A row that is neither automated nor answered fails [the gate](../plan/testing.md#the-upstream-review-gate).** Automating a manual row is always an improvement; naming a test here that does not exist is worse than leaving it manual, because it reads as covered. In
priority order — the first three would each silently invalidate a design
decision:

> **A build script is a third answer, and it is named as one.** Rows 46–49 cite
> `build/Build-Payload.ps1` rather than a test. That is deliberate and it is not
> the suite: the script throws while assembling the payload, so `dotnet test`
> passing says nothing about those rows. It is nonetheless a stronger gate than
> *manual* for exactly these facts, because **there is no route to a bumped
> upstream that skips it** — the new version only exists in the tree because the
> script put it there. Read a script row as *automated, by the build rather than
> by the suite*, and never as covered by a test run.

| # | Fact | Breaks if | Check | Automated by |
|---|---|---|---|---|
| 1 | Playwright's restart command line overshoots 1023 by 531+ ([kb](chromium/resurrection.md)) | Playwright trims its arg list | `GetApplicationRestartSettings` on a live browser returns `ERROR_NOT_FOUND` | — *manual*; owed as `RestartRegistrationTests` at [step 15](../plan/build-order.md#15-first-run-provisioning-and-browserai_reinstall_browser) |
| 2 | Job containment holds end to end for **process trees**, measured against the product's own job and launcher — 10 processes in the probe arm, 4 under the bundled `node.exe`, 0 escapees and 0 survivors in both ([kb](windows/processes.md#job-objects-and-process-containment)) | Node, libuv or the SDK changes how a child is created; a flag is added to the job | Run the suite. Each arm asserts `IsProcessInJob` for every walked pid, cross-checks against the job's own pid list **in both directions**, then hard-kills the launcher and requires zero survivors | `JobContainmentTests` · `JobObjectTests` |
| 2a | The same guarantee **against real browsers** — the 16 runs / 106 processes / 0 escapees half ([kb](windows/processes.md#job-objects-and-process-containment)) | Chromium or Firefox starts requesting breakaway on a browser path, or Playwright changes `detached` | Re-run the acceptance test with a Chromium and a Firefox tree, and require every profile directory to delete cleanly afterwards — a directory still holding a lock proves an escaped browser | — *manual*; owed at [step 15](../plan/build-order.md#15-first-run-provisioning-and-browserai_reinstall_browser), which is the first step that has a browser to run it against |
| 2b | The same guarantee **from the published NativeAOT binary, against a real browser** — `[LibraryImport]` and `PROC_THREAD_ATTRIBUTE_JOB_LIST` after ILC rather than under the test host ([kb](windows/processes.md#job-objects-and-process-containment)) | Any SDK, ILC or runtime bump changes how the attribute list or the handles survive native compilation. Step 6 proved the flags under the test host and said so; this is the arm that closes it | Drive the published binary until a browser is up, record every job member with its creation time, `TerminateProcess` the binary from outside, and require zero survivors | `VerticalSliceTests.KillingThePublishedBinaryLeavesNoNodeAndNoBrowser` |
| 3 | `chromiumSandbox` config key still discarded, and the CLI stage always defining `chromiumSandbox` is why ([kb](playwright/configuration.md#silent-config-failures)) | Upstream fixes it, or stops giving commander's `sandbox` option a `false` default | Read the **resolved browser command line** of a live Chromium, never the config: with `--sandbox`, `--no-sandbox` must be absent from the browser and every child; with the config key alone and no flag, it must still be present | `SandboxFlagTests` |
| 4 | `Chrome_MessageWindow` title format ([kb](windows/detection.md)) | Chromium changes `ProcessSingleton` | Exact-title lookup against a launched browser | — *manual*; owed as `MessageWindowTests` at [step 16](../plan/build-order.md#16-the-stray-sweep) |
| 4a | **Cross-process `GetWindowTextW` bypasses `WM_GETTEXT`** — undocumented behaviour of a documented function, and the sweep rests on it ([kb](windows/detection.md#cross-process-title-reads--settled-by-two-independent-agents)) | A Windows change routes the read through the message queue | Child process with a WndProc that suppresses `WM_GETTEXT`; assert the parent still reads the kernel name. **No browser needed — runs in milliseconds on every build** | — *manual*; owed as `WindowTextBypassTests` at [step 16](../plan/build-order.md#16-the-stray-sweep) |
| 4b | A headless launch resolves to full `chrome.exe`, not `chrome-headless-shell` — **because we set a chromium-alias channel**, and `chromiumAliases` is exactly `["chrome-for-testing"]`. An empty browsers root therefore **fails** rather than falling back to system Chrome ([selector](playwright/configuration.md#defaults-that-are-not-what-they-look-like)) | Upstream changes binary selection, renames the one alias, or our channel stops reaching the child. Not silent — the shell is never provisioned, so the launch **fails loudly** — but the failure would be baffling without this note | Launch through the real path and read the browser process's own image path: it must be `<browsers-root>\chromium-<rev>\chrome-win64\chrome.exe` with `--headless` on its command line. Then repeat against an **empty** browsers root and require a refusal naming `chrome-for-testing` | `HeadlessBinaryTests` |
| 4c | The other half of 4b: the **resolved channel as the child reports it**, and the window walk that finds a titled window owned by that pid ([selector](playwright/configuration.md#defaults-that-are-not-what-they-look-like), and the [unreconciled 0.0.79 observation](windows/detection.md#enumeration-works--and-it-moves-the-safety-boundary)) | The same changes as 4b, but observed from inside the child rather than from the process table | `browser_get_config` round trip for the channel, which is also what closes the open disagreement between the two entries; the walk needs the sweep | — *manual*; the channel half owed at [step 12](../plan/build-order.md#12-the-session-tools-and-config-generation), the walk at [step 16](../plan/build-order.md#16-the-stray-sweep). Split out of 4b on 2026-08-16, when the binary-selection half became a test and marking the whole row automated would have read as covering these too |
| 5 | Chromium/Firefox request no breakaway on browser paths ([kb](windows/processes.md#job-objects-and-process-containment)) | Either adds one | Source search for `CREATE_BREAKAWAY_FROM_JOB` | — *manual* |
| 6 | `--browser-test` call-site inventory (11 files) ([kb](chromium/fingerprinting.md)) | Chromium adds a web-facing site | Source search for `switches::kBrowserTest` | — *manual* |
| 7 | `browserName`/`channel`/binary-selection defaults ([kb](playwright/configuration.md#defaults-that-are-not-what-they-look-like)) | `validateBrowserConfig` or `getExecutableName` changes | Config round-trip via `browser_get_config` | — *manual*; owed as `ConfigRoundTripTests` at [step 12](../plan/build-order.md#12-the-session-tools-and-config-generation) |
| 8 | `outputMaxSize` has no default ([kb](playwright/configuration.md#defaults-that-are-not-what-they-look-like)) | `defaultConfig` gains one | Assert unset in the resolved config | — *manual*; owed as `OutputMaxSizeTests` at [step 12](../plan/build-order.md#12-the-session-tools-and-config-generation) |
| 9 | Firefox honours `toolkit.winRegisterApplicationRestart` ([kb](chromium/resurrection.md#the-mechanism-and-what-is-still-unproven)) | Mozilla removes the pref | Source check in `nsAppRunner.cpp` | — *manual* |
| 10 | `winldd` no-op for Chromium ([kb](playwright/configuration.md#browser-provisioning)) | Upstream fixes `chrome-win` → `chrome-win64` | Cold-start latency; source check | — *manual* |
| 11 | Tool counts — 78 internal, 69 exposed, 24 default, and **which nine are `skillOnly`** ([kb](playwright/tools-and-artifacts.md#the-tool-surface-and-the-package-shape)) | Upstream adds, removes or reclassifies a tool | Golden `tools/list` snapshot; count `skillOnly` in the resolved bundle | `build/Update-UpstreamSnapshots.ps1` · `UpstreamSnapshotTests` |
| 12 | `storage` is 17 tools, and **the per-capability breakdown**: 42 tools in `headless`/`interactive`, 59 in `persistent` ([kb](playwright/tools-and-artifacts.md#the-per-capability-breakdown-counted)) | Any capability's membership changes | Count every capability's tools from the resolved bundle. Never from memory | `build/Update-UpstreamSnapshots.ps1` · `UpstreamSnapshotTests` |
| 13 | `browser_run_code_unsafe` reaches `httpOnly` cookies from the **default** surface ([kb](playwright/tools-and-artifacts.md#tools-that-reach-credentials)) | Upstream sandboxes it or moves it out of `core` | Run the probe: default caps, `page.context().cookies()` | — *manual* |
| 14 | `browser_storage_state` omits IndexedDB ([kb](playwright/tools-and-artifacts.md#tools-that-reach-credentials)) | Upstream passes `{indexedDB:true}` | Source check at the `storageState()` call site | — *manual* |
| 15 | The child's protocol ceiling is `2025-11-25`, and it never rejects — it caps or echoes ([kb](mcp/protocol.md#the-protocol-split)) | Upstream adopts a newer revision | Assert the negotiated version at startup. The product logs `requested=… negotiated=…` and throws when they differ; the test asserts that pair against the ceiling the snapshot recorded, so the pin and the measurement cannot drift apart | `build/Update-UpstreamSnapshots.ps1` · `ProtocolSplitTests` |
| 16 | `DiscoverProbeTimeout` is 5 s, and **the client pin is what skips the probe** ([kb](mcp/protocol.md#the-protocol-split)) | The SDK changes the default, the probe, or which revisions trigger it | Read the default off `McpClientOptions` rather than from a document; then, against a double that records every method it is asked for, assert three things in one run — a pinned client sends **no** `server/discover`, an unpinned one **does**, and an unpinned one against a double that drops the method pays the **whole** timeout. The third arm is what makes the first evidence rather than a tautology | `FakeChildHarnessTests.TheClientPinIsWhatSkipsTheDiscoverProbe` |
| 16a | The same cost **against a real spawn**: the ~300 ms baseline a probe stall would be measured against ([kb](mcp/protocol.md#the-protocol-split)) | The same changes as 16, but observed as wall-clock rather than as a method the double did or did not hear | Time a real `node` child spawn with the pin in place and again without it. The in-process arm cannot answer this: it pins the probe timeout **short** deliberately, so its numbers are not comparable to production's | — *manual*; split out of 16 on 2026-08-16, when the mechanism half became a test and marking the whole row automated would have read as covering the timing too |
| 17 | `PLAYWRIGHT_MCP_*` count is **42**, two of them outside the config mapping ([kb](playwright/configuration.md#environment-merge-order-and-startup-output)) | Upstream adds a variable | Derive the count from the resolved bundle; the allowlist test must not carry a literal | — *manual* |
| 18 | `--caps` and `PLAYWRIGHT_MCP_CAPS` replace rather than merge ([kb](playwright/configuration.md#environment-merge-order-and-startup-output)) | `mergeConfig` changes | Config round-trip via `browser_get_config` | — *manual* |
| 19 | Nine artifact generator prefixes ([kb](playwright/tools-and-artifacts.md#artifacts-and-output-directory-behaviour)) | Upstream adds an artifact type | Enumerate prefixes in the resolved bundle; an unknown prefix must fail the sort test | — *manual* |
| 20 | Killed children leak `browser@<guid>` descriptors ([kb](playwright/tools-and-artifacts.md#artifacts-and-output-directory-behaviour)) | `BrowserServer.stop()` learns to clean up with a `userDataDir` set | Kill a child, list the browsers-registry root | — *manual* |
| 21 | Payload sizes, the 203.8 MB first-run download and the 20.3 s provisioning time ([kb](playwright/provisioning-and-timings.md)) | Any browser or Node revision bump | Re-measure at each bump. The 202.3 / 323 MB / ~300 MB discrepancy is **settled** — 203.8 MB by exact CDN `content-length`, 2026-08-15 — so what is owed now is the 20.3 s figure, which was timed on a run that also fetched the shell, and the **compressed** payload size, which has never been measured at all. **Partly automated 2026-08-16:** `build/Build-Payload.ps1` writes the `node.exe` and `node_modules` byte counts into `payload/payload.json` on every payload build, and both confirmed the recorded MiB figures exactly, so the two *payload* rows now re-measure themselves. The browser download and the timings do not, and remain the manual half | `build/Build-Payload.ps1` (payload rows only) |
| 22 | Full Chromium refuses a second instance; `chrome-headless-shell` does not notice one ([kb](windows/detection.md#process-image-path--the-fully-documented-detection-path)) | Upstream changes the singleton, or the shell is ever shipped | Launch twice against one profile directory, on both binaries | — *manual* |
| 23 | SDK behaviours — `cmd.exe /c` prefix, `ListToolsAsync` filtering, `ContentBlock` drop-and-throw ([kb](mcp/sdk.md#sdk-behaviours-a-proxy-must-work-around)) | Any `ModelContextProtocol` bump | The fake-child passthrough tests are written against exactly these | — *manual* |
| 24 | Velopack landmines — feed-URL composition, `SetAutoApplyOnStartup`, the stub, `force_stop_package` ([kb](packaging/velopack.md#the-nine-landmines-claim-and-verdict)) | Any Velopack bump | The update lane: real feed URL, real N→N+1, real delta | — *manual* |
| 25 | Claude Code truncates `instructions` and tool descriptions at 2 KB, defers schemas, and **now handles** `tools/list_changed` ([kb](mcp/protocol.md#the-client-claude-code)) | Any client release | Measure both strings at build time; re-stamp the client version the claim was checked at | — *manual* |
| 26 | Payload licensing as shipped — `winldd` has no license file, full Chromium has no OSS license ([kb](packaging/dependencies.md#third-party-payload-as-shipped)) | Upstream adds one, or the payload composition changes | Re-read the shipped trees at each revision bump | — *manual* |
| 27 | **NativeAOT publishes clean and the proxy runs** ([kb](mcp/sdk.md#measured-by-spike-2026-08-15)) | Any SDK bump | `PublishAot`, zero warnings, run the published binary against a real child — **and grep the publish output for `will always throw`**. Amended 2026-08-16: ILC reports an always-throwing method as [neither a warning nor an error](mcp/sdk.md#added-2026-08-16--not-part-of-the-2026-08-15-spike), so exit 0 plus zero warnings does not cover it, and the check as originally written would have passed a binary with a dead code path in it | — *manual* |
| 28 | The SDK still never relays `notifications/cancelled` ([kb](mcp/sdk.md#measured-by-spike-2026-08-15)) | The SDK fixes it — our hand-rolled path would then double-send | Cancel a call, assert exactly one downstream notification | — *manual* |
| 29 | `Filters.Message.IncomingFilters` still exposes `Result` as raw `JsonNode?` ([kb](mcp/sdk.md#measured-by-spike-2026-08-15)) | Any SDK bump; this is the whole proxy hook | Short-circuit a `tools/call` and compare bytes | — *manual* |
| 30 | `ListToolsAsync(RequestOptions?, ct)` still drops silently ([kb](mcp/sdk.md#sdk-behaviours-a-proxy-must-work-around)) | SDK fixes or changes it | Fake child with an invalid `x-mcp-header`; compare both overloads | — *manual* |
| 31 | `StdioClientTransport` still wraps in `cmd.exe` ([kb](mcp/sdk.md#added-2026-08-16--writing-the-two-transports-at-220)) | SDK fixes it — the custom transport stays correct either way, but the rationale changes | Start a child through the SDK's own transport and read its **real** parent, via `NtQueryInformationProcess` rather than `process.ppid`: the answer has to come from outside the child, or a child that cannot report is indistinguishable from one with no shell above it | `SdkStdioClientTransportTests.TheSdkTransportStillPutsCmdExeBetweenUsAndTheChild` |
| 32 | An unusable `--user-data-dir` still falls back invisibly, a deny-all DACL still exits 21, and the "failed to create data directory" dialog still blocks startup ([kb](chromium/profiles.md)) | Chromium changes `RecursiveDirectoryCreate`, `ProcessSingleton` or its error dialogs | Launch against a file-occupied path and against a DACL'd directory; assert fallback, exit 21 and the dialog behave as recorded. **That article is the whole basis for [validate every path before launch](../README.md#settled-2026-08-15)** and had no row here until 2026-08-15 | — *manual* |
| 33 | A healthy start still prints `Session: <path>` to stderr, every time ([kb](history.md#the-legacy-setup-and-this-machine)) | Upstream changes startup output — in either direction: a new benign line trips the classifier, a removed one turns it into dead code | Classify a real start's stderr. [§E](../plan/E-lifecycle.md#e-lifecycle-and-observability) ports the two regexes **verbatim**, so this is behaviour our code copies rather than merely observes | — *manual* |
| 34 | Firefox's cost ratios against Chromium — ~2× RAM, ~10× first navigate, ~24× idle CPU, ~20× profile disk ([kb](history.md#the-legacy-setup-and-this-machine)) | Any Firefox revision bump | Re-measure. The original harness was not preserved, so these are order-of-magnitude guidance; re-establish them properly before any decision turns on them again | — *manual* |
| 35 | Floating NuGet still needs **two** restores — `--force-evaluate` to resolve, locked-mode to verify ([kb](windows/processes.md#interop-and-the-toolchain)) | NuGet changes lock-file semantics, or the SDK's default. A one-step `--locked-mode` build looks green with the float silently dead | Resolve, then `git diff --exit-code -- "**/packages.lock.json"`. The whole floating-dependency policy rests on this being two commands | — *manual* |
| 36 | `setupExitWatchdog` still hooks stdin close / `SIGINT` / `SIGTERM` → `gracefullyCloseAll()`, hard-exiting after **15 s** ([kb](playwright/configuration.md#shutdown)) | Upstream changes its shutdown path or that ceiling | Close stdin on a real child; assert graceful close, and that nothing survives the ceiling. Teardown is built on it — [no killing is involved in the normal path](../plan/C-sessions.md#lifetime-one-timer-and-reclaim-is-forever) | — *manual* |
| 37 | `--console-level` still defaults to `info`, silently dropping `debug` ([kb](playwright/configuration.md#defaults-that-are-not-what-they-look-like)) | Upstream changes the default | Config round-trip via `browser_get_config`. The default is [exposed on `init`](../README.md#settled-2026-08-15), so an upstream change silently changes what a caller is choosing between | — *manual* |
| 38 | Resume costs **515 ms** and loses only `sessionStorage`; browser-idle close recovers 329 → 110 MB and relaunch costs 186 ms ([kb](playwright/provisioning-and-timings.md#timings-spawn-resume-idle-close-proxy-overhead)) | Any browser or Playwright bump that changes profile durability or launch cost | Kill the node child, resume against the directory, assert cookies, localStorage, IndexedDB, service workers and CacheStorage all survive. **[Reclaim is forever](../plan/C-sessions.md#lifetime-one-timer-and-reclaim-is-forever) rests on this**: if a resume stopped preserving the profile the no-expiry decision would be wrong, and nothing else in the design would say so | — *manual* |
| 39 | A console logging sink's **type initializer** calls `SetConsoleMode` on `STD_OUTPUT_HANDLE` before any log line, and no-ops silently when stdout is a pipe ([kb](windows/processes.md#stdio-exit-codes-and-process-startup)) | We adopt `Serilog.Sinks.Console` or any sink shaped like it — or the referenced version moves. Upstream `main` has **already dropped** the `#if PINVOKE` guard that leaves 3.1.2 inert on `netstandard2.0`, so a version bump alone can arm it | Read `ConsoleSink.cs` and `Platform/WindowsConsole.cs` **at the version actually referenced**, not at whichever copy is on disk. **Conditional row:** nothing today depends on Serilog, so this is owed only once a console sink reaches the dependency list — but it must be answered *then*, because the write is invisible under MCP and shows up only in interactive diagnostics | — *manual* |
| 40 | A legitimately **empty** channel 404s exactly like a misconfigured feed URL ([kb](packaging/velopack.md#1-the-channel-must-not-go-in-the-feed-url)) | Velopack returns an empty release list instead, or changes the status code | Request a channel with nothing published, assert the status, and confirm the health check does **not** alarm. Row 24's update lane only ever exercises a populated channel, so this is not covered there | — *manual* |
| 41 | `UpdateExe.Start`'s first positional parameter is the **locator**, not `waitPid`; `ApplyUpdatesAndRestart` already restarts on its own ([kb](packaging/velopack.md#the-restart-handover-race-and-why-updateexe-is-the-answer)) | Any Velopack bump changes either signature | Compile a call against the shipped assembly, and assert exactly **one** relaunch on the update path. The double-restart failure is silent — two working launches look like one slow one | — *manual* |
| 42 | **Diagnostic-severity precedence**: `NoWarn` beats both `WarningsAsErrors` and an `.editorconfig` `dotnet_diagnostic` severity; bulk `dotnet_analyzer_diagnostic.category-*` entries are ignored once `AnalysisMode` is an MSBuild property; IDE0005 needs `GenerateDocumentationFile` ([kb](windows/processes.md#diagnostic-severity-what-actually-enforces-a-rule-and-what-only-looks-like-it)) | Any SDK or Roslyn bump. **The SDK floats** under `rollForward: latestMajor`, so this moves without anyone choosing it | Plant the failure and rebuild with `--no-incremental`, one variable at a time. `BuildConfigurationTests.NoBuildFileSuppressesWarnings` guards the *consequence* on every build; the precedence itself needs re-measuring | — *manual* |
| 43 | Central package management refuses `Version="*"` without `CentralPackageFloatingVersionsEnabled` — NU1011 ([kb](windows/processes.md#interop-and-the-toolchain)) | Any SDK or NuGet bump. If it ever became the default, the property is harmless; if the property were ever renamed, restore fails loudly | Delete the property and restore. Every ordinary restore already proves the positive half, so only the negative half is owed | — *manual* |
| 44 | NativeAOT embeds `ApplicationManifest` into the published binary ([kb](windows/processes.md#diagnostic-severity-what-actually-enforces-a-rule-and-what-only-looks-like-it)) | Any SDK or ILC bump changes Win32 resource handling | Read the published exe's bytes for `longPathAware`, `asInvoker` and the supportedOS GUID. Cheap, and the guarantee is otherwise unfalsifiable until a caller picks a path over MAX_PATH | — *manual* |
| 45 | `FileMode.Append` loses records across processes; `FILE_APPEND_DATA` without `FILE_WRITE_DATA` does not ([kb](windows/processes.md#interop-and-the-toolchain)) | .NET changes its append implementation — in which case the Win32 path we took stays correct and the note's first half stops being a live hazard | `ProcessLogTests.ConcurrentProcessesDoNotLoseEachOthersRecords`: eight processes, 25 records each, assert all 200 present. It is a real regression guard rather than a re-measurement, because it fails the same way the original defect presented — silently missing lines | `ProcessLogTests` |
| 46 | **Node's `LICENSE` is not published beside `node.exe`** — `dist/<version>/win-x64/` carries only `node.exe`, `node.lib` and two `node_pdb` archives, so the licence comes out of `node-<version>-win-x64.zip` ([kb](playwright/provisioning-and-timings.md#component-sizes)) | Node publishes a standalone `LICENSE`, or renames or restructures the archive. **The renaming half is the dangerous one**: the extraction is keyed on `node-<version>-win-x64/LICENSE`, and a build that quietly shipped no licence would look identical to one that did | Extract `node.exe` **and** `LICENSE` from the SHA-256-verified archive and fail if either entry is absent. Never fetch the bare `node.exe` — that route is one `GET` shorter, 53 MB heavier, and ships no licence | `build/Build-Payload.ps1` |
| 47 | **`.links/` is written into the browsers root and never into `node_modules`** — `path.join(registryDirectory, '.links')`, three call sites in `playwright-core/lib/coreBundle.js` ([kb](playwright/provisioning-and-timings.md#first-run-provisioning)) | Upstream moves it into the package tree, at which point the strip that [§A](../plan/A-runtime.md) once demanded becomes real again and build paths would ship | Assert the assembled payload contains no `.links` directory. Also the reason the stale-browser GC must stay off: a registry directory with no `.links` entry is prunable | `build/Build-Payload.ps1` |
| 48 | **The payload tree's shape** — `@playwright/mcp` pins `playwright-core` **exactly**, and no non-optional package declares an install script ([kb](playwright/provisioning-and-timings.md#component-sizes)) | Upstream loosens the pin to a range, or adds a `postinstall`. Either is silent: the tree still installs, and the payload starts floating on a second axis or starts executing upstream code on the build machine | Compare the declared dependency against the resolved version, and scan the lock for `hasInstallScript` outside `optional` entries. The install runs **without** `--ignore-scripts` on purpose, so a new script is caught rather than suppressed | `build/Build-Payload.ps1` · `PayloadTests` |
| 49 | **npm keys a lock's root package on the empty string, and `npm ci` does not rewrite the lock** ([kb](windows/processes.md#interop-and-the-toolchain)) | npm changes `lockfileVersion`, or PowerShell changes `ConvertFrom-Json`. The first fails loudly; the second is why `-AsHashtable` is not stylistic | Parse the resolved lock, and compare it byte for byte either side of `npm ci`. **Not covered:** whether `npm install` re-resolves a dist tag with a lock already present — the build deletes the lock first, so that state never arises | `build/Build-Payload.ps1` |
| 50 | **`PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD` gates the npm-postinstall and server auto-install paths only, not `registry.install()`** — and installing `ffmpeg` on Windows pulls `winldd` with it ([kb](playwright/provisioning-and-timings.md#first-run-provisioning)) | Upstream extends the flag to the explicit installer, which would stop [step 15](../plan/build-order.md#15-first-run-provisioning-and-browserai_reinstall_browser)'s provisioning dead while the variable stays [mandated in the child environment](../README.md#the-five-rules-that-make-floating-safe) for the reason it was always mandated | `install-browser ffmpeg --no-progress` against an empty browsers root with the flag set; assert it still downloads. The payload build's own `chrome.exe` assertion catches the regression too, one step later and just as loudly | `build/Build-Payload.ps1` |

| 51 | **The snapshot gate is byte-exact across line endings.** All four snapshots are **LF** today; the `-text` exemption in `.gitattributes` is what keeps a committed byte copy from being normalised out from under the comparison if upstream ever ships CRLF ([kb](windows/processes.md#interop-and-the-toolchain)) | Upstream changes its line endings **and** the exemption has been lost. Either alone is harmless; together the committed copy is LF, the regenerated one is CRLF, and every build is red on a difference that is not one | Count CR **bytes**, by piping `tr -cd '\r'` into `wc -c`, on the four snapshots and on the payload originals. Never `grep -c $'\r'`, which reported every line of an all-LF file as a match | `build/Update-UpstreamSnapshots.ps1` |
| 52 | **Three traps in the gate's own plumbing**: `$(IntermediateOutputPath)` is empty in a `.targets` imported from the project body (the stamp escapes `obj\`); PowerShell 7 colours redirected output; `Get-Command git` returns two executables on this machine ([kb](windows/processes.md#interop-and-the-toolchain)) | Any SDK bump for the first, any PowerShell bump for the second. **The SDK floats**, so the first moves without anyone choosing it | Re-measure the first by pointing a `Touch` at `$(IntermediateOutputPath)` from an imported `.targets`. Its *consequence* is caught on every step by `git status --porcelain`, which is how it was found | — *manual* |
| 53 | **`StreamServerTransport` still re-escapes every result**, because the escaping comes from `Utf8JsonWriter`'s own encoder and it sets none — 127 bytes through our transport against 190 through the SDK's, for one message ([kb](mcp/sdk.md#added-2026-08-16--writing-the-two-transports-at-220)) | Upstream sets an `Encoder`, or gains an options seam. Deviation 5's transport stays correct either way, but [the reason recorded for owning it](../plan/stack.md#nine-places-where-the-sdk-must-be-deviated-from) would be stale, and a stale reason is how a component nobody needs survives a rewrite | Serialise one message through both transports and compare bytes, never decoded strings — the escaping is semantically lossless and invisible to anything that round-trips | `DirectStdioServerTransportTests.TheSdkServerTransportStillEscapesTheSameResult` |
| 54 | **A double hyphen in an XML comment in a shared props file presents as `NETSDK1207: Ahead-of-time compilation is not supported for the target framework`**, and only `dotnet msbuild -getProperty` names the real cause ([kb](windows/processes.md#interop-and-the-toolchain)) | Any SDK bump changes the diagnostic or the order of the checks. **The SDK floats** under `rollForward: latestMajor`, so this moves without anyone choosing it | Put `--` inside a comment in `Directory.Build.props`, then run `dotnet build` and `dotnet msbuild <project> -getProperty:TargetFramework` and compare what each says. Revert | — *manual* |
| 55 | **`BannedApiAnalyzers` merges every additional file named `BannedSymbols.txt`**, which is what lets the repository-wide never-by-image-name list and the product-only Console list be separate files ([kb](windows/processes.md#interop-and-the-toolchain)) | Any analyzer bump changes the file discovery. It would fail **open**: one of the two lists would stop applying, with a green build and no diagnostic anywhere | Plant one banned call from each file in the product project and require both RS0030s on the same build. Revert | — *manual* |
| 56 | **`server/discover` exists on our side of the proxy and not on the child's** — BrowserAI answers `-32602` (per-request metadata missing), the child answers `-32601` ([kb](mcp/protocol.md#the-protocol-split)) | The SDK stops implementing `2026-07-28`, or upstream adopts it. Either would make the two halves of the protocol split the same revision, which is the state the split exists to avoid | Send `server/discover` as the first frame to each end of the running proxy and compare the two error codes. A version string cannot show this, because a version can be echoed | `ProtocolSplitTests.TheServerReachesARevisionTheChildDoesNotImplement` |
| 57 | **`JsonArray.Add(x)` without the `(JsonNode)` cast fails `dotnet build` as well as `dotnet publish`**, as `IL2026` and `IL3050` at the same call site ([kb](mcp/sdk.md#added-2026-08-16--the-published-binary-at-220)) | Any SDK or `System.Text.Json` bump removes either attribute, or `EnableAotAnalyzer`/`EnableTrimAnalyzer` stop being set unconditionally. It fails **open**: the trap would then only appear at publish, or not at all | Plant the call, build, publish, revert. [Step 9](../plan/build-order.md#9-lossless-passthrough) rewrites `tools/list` on `JsonNode`, which is where this shape is most likely to be written | — *manual* |
| 58 | **`Capabilities.Tools` is what makes `initialize` advertise tools**, independently of whether tool handlers are registered ([kb](mcp/sdk.md#added-2026-08-16--writing-the-two-transports-at-220)) | Any SDK bump changes how capabilities are derived. Silent in the worst way: `tools/list` keeps working for anything that asks, and a caller that respects capabilities stops asking | Assert `capabilities.tools` on the `initialize` result the raw client receives, not on the options object we built | `VerticalSliceTests.ToolsListReturnsTheChildsToolsWithUpstreamsOwnNames` |
| 59 | **An exception escaping a `CallToolHandler` becomes a JSON-RPC *success* carrying `isError: true`, with a body that names no cause** — the same frame for an unknown content type and for a child that died mid-call ([kb](mcp/sdk.md#added-2026-08-16--the-in-process-harness-at-220)) | Any SDK bump changes the handler's exception policy — including a fix, which would then make our own error shaping double-handle it. It is the founding failure shape arriving from a dependency, so it can never be caught by a transport-level or exit-code check | Drive both causes through the proxy against the double and assert on the **caller's** frame: a success envelope, `isError` true, and the cause present only in the log | `FakeChildHarnessTests.TheFakeChildDiesMidCall` · `FakeChildHarnessTests.TheFakeChildReturnsAnUnknownContentType` |
| 60 | **A child's JSON-RPC error keeps its `code` and its `data` verbatim through the proxy; only the message is prefixed** with `Request failed (remote): ` ([kb](mcp/sdk.md#added-2026-08-16--the-in-process-harness-at-220)) | Any SDK bump changes `CreateRemoteProtocolExceptionFromError` or how the server re-serialises an `McpException`. Both halves matter in opposite directions: a lost `data` is silent data loss, and a removed prefix would make [step 9](../plan/build-order.md#9-lossless-passthrough)'s strip cut into the real message | Program the double to answer one tool with a code, a message and a nested `data`, and assert all three at the caller by **exact equality** rather than containment | `FakeChildHarnessTests.TheFakeChildInjectsAJsonRpcError` |
| 61 | **The in-process teardown order: cancellation and completing both pipe writers each end `McpServer.RunAsync` independently, and only completion closes the hop** ([kb](mcp/sdk.md#added-2026-08-16--the-in-process-harness-at-220)) | Any SDK bump changes what `RunAsync` returns on. The consequence is guarded on every build by the rig's own liveness assertion; what is owed here is the *mechanism*, which that assertion cannot distinguish | Comment out step 1 or step 2 of `McpTestHarness.DisposeAsync` and run the suite, one at a time and then both. Ten tests turn red rather than hanging, because the wait is bounded and the server-task state is read **before** anything is disposed | — *manual*; the consequence is automated by `McpTestHarness` on every run |

Add a row whenever a new `[FLOATS]` entry lands. An entry with no row is an entry
nobody will re-check.
