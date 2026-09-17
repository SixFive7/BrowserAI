<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-17 — the re-verification batch, one key it had to clear, and one browser that died

What the re-establishment of [re-verification rows 21, 34, 38 and
85](../../../kb/re-verification.md) was read out of, the registry residue that
batch created and cleared, and — *added later the same day* — the one red of the
next batch's gate, kept because it is the wild signature an open hazard row says
it has never been able to reproduce.

## `removed-BrowserAI.app.test-key.txt`

**The values `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\BrowserAI.app.test`
held before it was removed on 2026-09-17**, recorded because the row that cites
it says a key of that shape may be residue *or* a live install, and the only
thing that tells them apart is where it points.

Cited by [the hazard row](../../../HAZARDS.md#hazard-index) and by
[the note in Testing](../../../TESTING.md#two-executables-and-what-each-half-of-the-suite-can-see).

**How it got there.** A gate run was started against a stale published slice —
product source had been edited after the last publish — and the run was stopped
by pid so the publish directory could be rewritten. The real-installer arm had
already installed under the suite's own pack id and had not yet reached the
teardown that removes the key, so the key outlived the run while the scratch
root it names was deleted with everything else.

**What was checked before removing it**, both of which are in the record above:

1. `InstallLocation` is under `%LocalAppData%\BrowserAI-test-scratch` — it is
   `…\real-install-window-27abcac3035a4d2aaed47b7c6984807f`.
2. That directory **does not exist**. A key pointing at a directory that is
   still there is a live install rather than residue, and deleting it would
   strand one.

`InstallDate` reads `20260917`, which is the same day, and every key under the
test id is one the suite wrote.

**What it was cleared with**: `reg delete "HKCU\…\Uninstall\BrowserAI.app.test" /f`
— the command `ReleaseLayout.ClearTheLeftoverKey` builds and the refusal message
itself names, rather than a different one chosen here.

⚠️ **The real key was read before and after and is byte-identical across all 13
values** — `Uninstall\BrowserAI.app`, still `InstallLocation
C:\Users\jori\AppData\Local\BrowserAI.app`, `InstallDate 20260916`. That
comparison is the reason the removal was safe to make, and it is the half a
record of the deleted key alone would not carry.

## What is not here

The four rows' own measurements are not in this directory: they are in the kb
entries that publish them, and the rigs that produced them are in
[`docs/probes/`](../../probes/README.md) —
[`2026-09-16-provisioning`](../../probes/2026-09-16-provisioning/README.md),
[`2026-09-16-resume`](../../probes/2026-09-16-resume/README.md) and
[`2026-09-17-cost-ratios`](../../probes/2026-09-17-cost-ratios/README.md). The
raw JSON each probe emitted was scratch and was deleted with the rest of
`.work/`; re-running a probe produces new files rather than these.

## `sweeper-exit1-20260917-140227.log`

**The whole of the failing run**, uncut — 13,202 bytes, SHA-256
`03503c40d3ab47ec66008d46aaea4f35bedbf3bae2b0c602299217e26b8f5c42`, identical to
the scratch log it was copied from (`.work/suite/ps-20260917-140227.log`, same
digest) before that directory was cleared. Nothing was extracted from it, so
there is no cut to describe.

**Why it is kept.**
`StraySweepTests.TheSweeperFindsARealBrowserItLaunchedItselfInTheInteractiveSession`
failed at 855 ms with **exit code `1`**, nothing on either stream, both pipes at
EOF and five lines in Chromium's own `--log-file`. [The hazard
row](../../../HAZARDS.md#hazard-index) for a browser dying on a spent desktop
heap records that exit `1` is the signature seen **in the wild** and that 80
deliberate reproductions produced `0x80000003` and `0xE0000008` and **never a
`1`** — so an instance of it is worth more than a line in a report.

**And the instrument that row nominates fired**, in its own words in this log:
*"A message-only window carrying a 2048-character title was created on this
desktop and destroyed again. Its heap had room for the allocation an exhausted
one refuses a starting Chromium, so whatever killed the browser, it was not
this."* For this shape the desktop heap is therefore **excluded**, and the cause
is still unnamed.

**What the rest of the gate did**, so the rate is readable rather than implied:
an immediate re-run of the whole suite was **747 / 0 failed / 0 skipped**, and so
was the Git Bash half after it — **1 red in 3 full runs** that day, on a tree
whose only product-source change was a doc comment. Nothing was retried in code
and no assertion was touched.
