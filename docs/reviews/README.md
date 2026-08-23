<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Reviews

Findings that are **too long to inline and too valuable to lose**. Everything
here was produced in `.work/`, which is gitignored — these are the copies that
survive a clean.

**These are records, not work lists.** Anything actionable is lifted into
[`TODO.md`](../../TODO.md) or [`HAZARDS.md`](../../HAZARDS.md); a finding that
lives only here is a finding nobody will act on.

| File | What it is |
|---|---|
| `2026-08-18-adversarial-locking.md` | Adversarial review of session locking and provisioning. Four wrong-answer findings, thirteen `lock.json` readers enumerated, and the probe-before-gate redesign attacked before it was built |
| `2026-08-18-adversarial-processes.md` | Adversarial review of process supervision, containment and attribution. Fourteen findings, four Tier-1 |
| `2026-08-18-truncation-findings.md` | What Claude Code's *"2KB each"* actually means, measured off the client's own outbound API request |
| `2026-08-18-truncation-prompt-for-sibling-project.md` | The same, written self-contained for another MCP server's maintainer. Includes the full re-run recipe |
| `2026-08-19-auth-transfer-and-session-modes.md` | Can a human's interactive login be handed to an unattended headless run? Measured yes, on one route of three — and copying the profile directory is **not** it. Also three claims this repository makes about its own modes that the code does not make |

**Both adversarial reviews state what they tried to break and could not**, which
is the half that makes the rest trustworthy. A review that reports only findings
is indistinguishable from a shallow one.

## What has been acted on, as of 2026-08-23

**The review files are not edited when a finding is fixed** — they are dated
records of what was true when they were written, and rewriting one would destroy
the only account of the reasoning. This table is where the status lives. `git log`
carries the fix; [`HAZARDS.md`](../../HAZARDS.md) carries what is bounded rather
than closed.

⚠️ **That was prose until 2026-08-20, and prose did not hold it.** A rename sweep
reached these files and rewrote them; it was caught by a human reading the diff.
`AppendOnlyRecordTests` now seals every file in this directory **except this
one** — by character count and SHA-256, on the prefix, so an addendum may be
appended and a body may not be rewritten. This index is deliberately *not*
sealed: the status table above is meant to move. See
[the release gate](../../TESTING.md#the-dated-records-are-append-only); a new
review is registered in the seal list in the same change that adds it.

| Finding | State |
|---|---|
| locking **A1** — `destroy` releases the lock before deleting the tree | **fixed**, `SessionDestroyTests` |
| locking **A2** — `AcquiredAbandoned` deletes a complete, in-use tree | **fixed**, `ProvisioningTests` |
| locking **A3** — `reinstall` deletes with no provisioning mutex | **fixed**, `ReinstallBrowserTests` |
| locking **A4** — path aliasing gives one directory two gates | **closed by refusal** 2026-08-19 — `SessionDirectoryGuard` refuses an aliased spelling at `init` and `resume`, and one of A4's four aliases was measured away: `Path.GetFullPath` **does** expand 8.3 short names on .NET 10 ([kb](../../kb/windows/detection.md#a-mapped-drive-letter-is-a-network-path-and-costs-the-same-22-seconds)). Two gaps stayed open as hazard rows |
| locking **A5** — `SessionIndex.Sweep` deletes a live session's entry | open, hazard row; the kb claim it falsified is corrected |
| locking **A6** — `init`'s `Existing` guard acts on a transient absence | open, hazard row |
| locking **B1** — the gate is outlasted by the waits inside it | **fixed**: the sum is asserted and the gate re-sized |
| locking **B2** — unbounded calls inside the gate, incl. UNC | **network half closed** 2026-08-19, the rest open. The caller-visible decision was taken: a network session directory is refused, **by semantics rather than by spelling** — a mapped drive letter costs the same measured 22 s and passes every string test |
| locking **B3** — `LiveInstances` shares the per-directory mutex namespace | **fixed** 2026-08-23, `UpdateTests.TheLiveSetsGateCannotCollideWithASessionOpenedOnTheInstallRoot`. Still live when triaged: nothing refuses a session on the install root, so the collision was exactly reachable. The live set has `Global\BrowserAI-Live-` of its own now, and [`LockScopes`](../../src/BrowserAI/Sessions/LockScopes.cs) documents four scopes where it documented three — the undocumented fourth is how the name came to be shared |
| locking **B4** — same-process `destroy` racing `set_purpose` leaks the lock forever | **real, recorded, not started** — hazard row. Re-read against the tree 2026-08-23 and unchanged: `_live` is still a bare `ConcurrentDictionary`, `Rewrite` still tests `_disposed` *before* taking `_gate`, and `Dispose` still disposes `_gate` under it. **Large:** the fix is a per-session lock every mutating path and both disposal paths take, and the test that proves it needs a deterministic same-process interleaving |
| locking **B5** — instance-directory liveness rests on the surface child alone | **real, recorded, not started** — hazard row, **and it is the same finding as processes 11**, which is itself a triage outcome: two reviews found it independently and the index carried it as two items. `Claim` is still `Directory.Move`, `Sweep` still judges on the directory's own mtime, and the BrowserAI process still holds nothing in its own instance directory |
| locking **D** — the probe-before-gate redesign | not adopted; the verdict stands as written |
| processes **1** — the client watch acts on a bare pid | **fixed**, `ProcessLivenessTests`, plus a mechanism for the next one |
| processes **2** — `TreeDelete` follows directory junctions | **fixed**, `TreeDeleteTests`; the ban message now names both properties |
| processes **3** — `RevisionPrune` decides on a stale census | **narrowed**, hazard row; the held handle was declined with a reason |
| processes **4** — a junction above the install root empties the sweep's candidate set | **real, recorded, not started** — hazard row. `BrowserProcesses` still matches `QueryFullProcessImageNameW`'s reparse-resolved answer against `Path.Combine`-composed strings, and there is still no `candidates=0` tripwire. **It got cheaper without getting done:** `VolumeIdentity.FinalNameOf` arrived 2026-08-19 and is what would canonicalise the wanted set — but this decides what the sweep may *terminate*, which is the one area the charter is most conservative about |
| processes **5** — concurrent launches cross-inherit pipes | **fixed**, `JobContainmentTests` |
| processes **6** — the job handle crosses `CreateProcessW` with no ref-count | **taken 2026-08-23, and it is this tree's one named exception to the planted-red rule** *(previously "real, recorded, not started, and the reason is unusual … That trade is the maintainer's")*. The maintainer took the trade. `ProcessAttributeList` now calls `DangerousAddRef` **before** it reads the raw value, holds the `SafeJobHandle` so the object cannot be collected either, and releases in `Dispose`, which the `using` runs after `CreateProcessW`; the misplaced `GC.KeepAlive(job)` moved to the line after the launch. **The behaviour change has no failing test behind it and the exception is recorded in [`CLAUDE.md`](../../CLAUDE.md)** rather than left implicit — what is asserted is the source-level rule, `HouseRuleTests.EveryRawHandleThatOutlivesItsExpressionIsRefCounted`, watched red against both real escapes |
| processes **7** — the sweep's remediation is one `TerminateProcess` | **declined, with the reason** — the tree teardown it asks for would mean ending helpers this pass could not attribute, and *never terminate on a guess* is the rule the whole class is built on. Attribution failing while detection holds is the designed outcome, and `unattributable` is reported by full path on every pass. **What is recorded instead is the sentence**: `SweepLog.Terminated`'s *"Terminated a stray browser"* reads as a tree dealt with when at most one process was |
| processes **8** — the title guard rejects the UNC *spelling*, not UNC *semantics* | **fixed** 2026-08-23, `StraySweepTests.AMappedNetworkDriveTitleIsRefusedThoughItIsSpelledLocally`, against a real mapped drive. **It was expensive when written and cheap when triaged**: `VolumeIdentity` arrived 2026-08-19 for `SessionDirectoryGuard` and answers *is this letter a network volume* with no filesystem call at all, so the guard still cannot pay the 22 s it exists to prevent. Only `Network` is refused — a `subst`ed or absent letter is local-speed and refusing it would narrow what the sweep can see |
| processes **9** — `NativeFile.Append`'s completion loop breaks per-call atomicity | **real, recorded, not started** — hazard row. The loop is unchanged and the partial-write case is still silent. **What HAS changed is its reachability, and that is a triage finding**: the review's route in was a log directory on a mapped network drive, and the log directory is derived from the install location rather than supplied by a caller, with a UNC app root refused at startup since 2026-08-19 — so what survives is quota and disk-full boundaries. **It is a decision rather than a fix**: making a short write throw turns a torn record into a lost one plus an exception, on the one path that must never take the process down. Directions are in [`TODO.md`](../../TODO.md) |
| processes **10** — `FILE_SHARE_DELETE` lets anything unlink the live process log | **real, recorded, not started** — hazard row. The share mode is unchanged. **Also a decision**: dropping `FILE_SHARE_DELETE` would make the log undeletable while any BrowserAI holds it, including by this product's own `SweepExpired`, so the remedy trades a silent failure for a loud one somewhere else |
| processes **11** — the instance-directory liveness test rests on the surface child's cwd | **the same finding as locking B5** — see that row. Recorded once, in one hazard row, rather than carried twice |
| processes **12** — a title over 32,768 characters is silently truncated | **declined, with the reason, written where it happens** — a truncated title still has to resolve to a directory whose `browserai.json` can be taken, so a prefix fails that test and lands in the *refuse* direction. The property is now stated in `MessageWindows.WindowText`'s own remarks, because the review's point was that the premise *everything here fails safe* deserved to be checkable by a reader rather than rediscovered |
| processes **13** — `WaitForExitAsync` waits over a handle another thread may close | **taken 2026-08-23, in the same decision as 6** *(previously "real, recorded, not started … the fix is small and cannot be planted red")*. The reference is taken before the non-owning `SafeWaitHandle` is built and released after the registration is unregistered, so `Dispose`'s release is no longer the one that closes the handle. `GC.KeepAlive(handle)` was retired rather than kept beside it: it covered collection, this covers disposal, and the finding was right that those are different problems. Under the same recorded exception; this file's escape is the one the new `HouseRuleTests` arm named when it was watched red |
| processes **14** — stdout: third-party code on a background thread | **closed at the referenced version, and half of it closed by accident.** The stray sweep now starts at `Program.cs:202`, *before* `StdioChannel.OpenStandardStreams()` — the review had it after. The half the review could not trace is now traced: `VelopackUpdateClient` builds its `UpdateManager` with no logger, which resolves to `VelopackLocator.Log` = *the logger this product set* or `NullVelopackLogger`, and **`ConsoleVelopackLogger` is constructed by nothing in the library** (read at Velopack 1.2.0). `[FLOATS]` — it is a fact about a dependency version |
