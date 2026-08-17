<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# What this project has not established

The rest of [`kb/`](README.md) records what was measured. This page records what
was **not**, aggregated from the per-article "not verified" lists and from every
`[UNVERIFIED]` entry in the corpus.

**It exists because a gap that announces itself is recoverable and a gap that
does not is a trap.** The failure this whole knowledge base is built against is
the confident wrong answer — a number adjusted by reasoning, an observation
generalised past its evidence, an absence read as a finding. An agent or a
reader arriving at one of the questions below will otherwise infer from silence
that it was considered and settled. It was considered and left open, usually for
a reason, and the reason is here too.

**How to read a row.** *Not established* means nobody has run the thing that
would settle it. *Deliberately not established* means running it was judged to
cost more than the answer is worth, or to be unsafe, and the cost is named. Three
rows are **closed** — the question stopped mattering because the feature it
served was dropped — and they stay listed so that nobody re-opens them looking
for an answer that is no longer owed.

**Adding to this page is not optional.** An article that writes *"was not
measured"*, *"not tested"* or `[UNVERIFIED]` and does not appear here has made
the same omission this page exists to prevent, one level up.

## Browsers and the web surface

| Question | Status | Why, and what it would take |
|---|---|---|
| Whether `--browser-test` is detectable **headful** | Not established | The whole fingerprinting comparison was headless. A hard constraint of the harness rather than an oversight; nothing renderer-side depends on the switch, which is why it was accepted. [kb](chromium/fingerprinting.md) |
| Whether the memory-pressure difference is observable under **real OS memory pressure** | **Deliberately** not established | It is the one genuine behavioural delta the switch makes, and the only way to see it is to exhaust a machine's memory. Not induced on purpose. [kb](chromium/fingerprinting.md) |
| Whether a real bot-detection service classifies the two arms differently | Not established | Local-only constraint: no commercial detector was exercised. The 486-field local differ found zero differences, which is a different claim. [kb](chromium/fingerprinting.md) |
| Whether **any** vendor or anti-detect project references `--browser-test` | Searched and nothing found | **Absence of evidence, and it is labelled as such** rather than as evidence of absence: five projects' code searched and four web searches. [kb](chromium/fingerprinting.md) |
| Whether more than **one** Chromium version behaves this way | Not established | One revision, one differ run. [kb](chromium/fingerprinting.md) |
| Which switch, if any, suppresses Chrome's *"Failed to create data directory"* dialog | Not established | `--noerrdialogs` does not, measured. No suppressing switch was identified, and the design avoids the dialog rather than dismissing it. [kb](chromium/profiles.md) |
| What `channel: "chrome"` plus an unusable `--user-data-dir` does to an **already-running personal Chrome** | **Deliberately** not established | It follows from the fallback and singleton behaviour, both measured — and running it would have driven a real person's browser. The cost of the experiment is the reason. [kb](chromium/profiles.md) |
| How long a Playwright-launched Firefox stays **registered for restart** before the preference unregisters it | Not established | The preference arrives over the wire after startup, so there is a window. Measuring it means sampling `GetApplicationRestartSettings` from the instant the process appears. The steady state — what a reboot hours later would find — **is** measured. [kb](chromium/resurrection.md) |
| What actually resurrected the browsers that motivated this project | Not established, and now unfalsifiable cheaply | By elimination it is the Windows sign-in restore path; `RegisterApplicationRestart` is excluded by measurement. Observing the sign-in path directly requires a reboot, which was not performed. A diagnostic is recorded for if it recurs. [kb](chromium/resurrection.md) |

## Windows

| Question | Status | Why, and what it would take |
|---|---|---|
| Whether raw `CreateMutexW` **relocates** a backslashed name or fails it | Not established | The .NET layer is measured — `DirectoryNotFoundException` on every shape tried. The Win32 layer is where a silent relocation could still be real, because the object manager treats a backslash as a path separator. Nothing here calls it directly. [kb](windows/detection.md#windows-object-names-and-window-scoping) |
| Whether **elevation** would let a logon scheduled task register | Not established, and **closed** | A UAC prompt cannot be answered from a non-interactive session. What is settled is that the *non-elevated* claim was false. The feature was dropped rather than elevated for, so nothing depends on the answer. [kb](windows/detection.md#the-logon-sweep-task) |
| When the original `Console` stdio measurements were taken — CP437 both directions, CRLF from any `TextWriter`, a BOM from a hand-rolled `StreamWriter` | Date not established | The observations are carried forward from the charter, which did not date them. Independently corroborated since, from two unrelated codebases; the corroboration does not supply a date. The **default itself** is reproducible in a minute: write a non-ASCII character to `Console.Out` and read the bytes. [kb](windows/processes.md) |
| When *"stderr survives the child"* and *"stdin EOF fires instantly on an external kill"* were measured | Date not established | Same provenance and the same treatment. [kb](windows/processes.md) |
| Whether **libuv still creates its permissive global job** under the bundled Node v24.19.0 | Not established | Containment *through* it is measured on v24.19.0 — 4 processes, 0 escapees, 0 survivors, twice. That observes containment, not libuv's internals. It needs a read of `src/win/process.c` at the shipped version. The probe arm reproduces the job shape explicitly, so the nested-permissive case is covered either way. [kb](windows/job-objects.md) |
| The boot-id clock-quantisation figures (10.4 ms spread, 0.12 ms across eight seconds) | Carried, not re-measured | Taken 2026-08-14 in an unpublished library and not re-run here. Read as an order of magnitude. [kb](windows/detection.md#named-mutexes-and-lock-files) |

## The toolchain

| Question | Status | Why, and what it would take |
|---|---|---|
| Why `dotnet test` reported **zero tests** from one shell and ran the whole suite from another, on the same commit | Not established | Both sets of observations are real; twice a correct observation was generalised to the wrong subject before the execution context was identified as the variable. Candidates not yet tested: MSBuild node reuse across long build sequences, a stale build server, concurrent `dotnet` processes. **Do not write a cause in without measuring one.** [kb](toolchain.md#dotnet-test-and-the-test-host) |
| Whether `npm install` re-resolves a dist-tag dependency with a lock **already present** | Not established | The payload build deletes the lock first, so it never reaches that state. Open rather than answered. [kb](toolchain.md#npm-for-a-vendored-payload) |
| Whether the **anchor half** of a Markdown link resolves | **Deliberately** not checked | `DocumentationLinkTests` checks the file half of every relative link and not the fragment. Resolving one means reproducing the heading-to-slug rule of whichever renderer is reading, and a wrong slug rule reports failures that are not real — the one outcome worse than the gap. |

## The MCP SDK

| Question | Status | Why, and what it would take |
|---|---|---|
| HTTP transports, resumption, pagination cursors against the real child, `structuredContent` on a real tool, stderr back-pressure under load, ordering of concurrent in-flight `tools/call`s | Not established | None was exercised. Listed in the article as *not tested, not claimed*. [kb](mcp/sdk.md#driving-the-whole-sdk-aot-passthrough-filters-and-cancellation) |
| Whether the SDK's **own** stdio client transport still yields `-32603` for a child that dies mid-call | Not established | Ours raised `IOException` instead, so the comparison was never made from the same starting point. [kb](mcp/sdk.md#error-shape-and-teardown-seen-from-an-in-process-harness) |
| Whether `ListToolsAsync(RequestOptions?, ct)` **still** drops a tool silently | Not established since the product stopped using it | Constructing an annotation that fails SEP-2243 validation has never been done here. The product's immunity is a source scan, not a measurement of the SDK — and the row says so rather than reading as covered. [row 30](re-verification.md) |

## Packaging and updates

| Question | Status | Why, and what it would take |
|---|---|---|
| MSI / PerMachine installs, code signing, `--runtime win7`, `autoApply=true` with a staged package, behaviour on older Windows, stdin inheritance through the stub | Not established | The article's own *Not verified* list. [kb](packaging/velopack.md#not-verified) |
| The **compressed** size of the shipped payload, and therefore every full-package download figure | Not established | Nothing has ever compressed the real payload. The only compression figure on record is for an earlier, browser-dominated tree that no longer exists, and it is explicitly not reused. [kb](playwright/provisioning-and-timings.md#component-sizes) |
| The size of a trimmed self-contained fallback (~70 MB) | Not established | Nothing was ever built in that configuration. [kb](playwright/provisioning-and-timings.md#component-sizes) |
| The price of Azure Artifact Signing | Not established | ~$10/mo is a list figure, not a quote obtained. [kb](packaging/velopack.md#distribution-msix-and-code-signing) |
| Whether redistributing Chrome for Testing is permitted | Not established, and **not answerable here** | The only on-point public statement is adverse and is a **citation, not a measurement, and not legal advice**. It is recorded because the provisioning decision rests on it. [kb](packaging/dependencies.md#third-party-payload-as-shipped) |

## Timings carried rather than measured

| Question | Status | Why, and what it would take |
|---|---|---|
| Dates for the spawn, navigation, idle-close and proxy-overhead figures | Not established | Only the resume figure was dated in the charter. The numbers are carried forward **exactly as written** — none has been adjusted — and each is owed a re-stamp at the next run. [kb](playwright/provisioning-and-timings.md) |
| Whether the ~50 ms proxy overhead predicts **this** product's | Not established | It measured an equivalent Node prototype, not the C# proxy. A precedent, not a measurement of what ships. [kb](playwright/provisioning-and-timings.md) |
| Suite costs — real-child contract 2–5 s, smoke 10–30 s, update 1–3 min | Estimates | Not stopwatch figures, and labelled as such. [kb](playwright/provisioning-and-timings.md) |
| The method behind Firefox's cost ratios — ~2× RAM, ~10× first navigate, ~24× idle CPU, ~20× profile disk | Not established | Carried from a session whose harness was not preserved. Order-of-magnitude guidance; re-measure before any decision turns on them. [kb](playwright/provisioning-and-timings.md#firefox-against-chromium-the-standing-cost-ratios) |

## What this page is not

It is **not** the [re-verification index](re-verification.md). That table lists
facts that **are** established and will stop being true when a version moves;
this one lists questions that were never answered. A row here does not become a
row there by being measured — it becomes an entry in an article, and *then* a row
there if what it depends on floats.

It is also not a backlog. Several of these are deliberate and will stay open
forever: a browser nobody will drive, a machine nobody will exhaust, a UAC
prompt nobody can answer from a script. The value is that the next reader stops
looking, rather than that somebody eventually looks.
