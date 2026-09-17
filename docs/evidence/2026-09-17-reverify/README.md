<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-17 — the re-verification batch, and one key it had to clear

What the re-establishment of [re-verification rows 21, 34, 38 and
85](../../../kb/re-verification.md) was read out of, plus the registry residue
that batch created and cleared.

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
