<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-04 - a drive-letter alias taken over mid-test, red once in lane q371's gate

**What this is.** The full suite run in which
`CanonicalPathTests.ASubstOntoAMappedDriveIsRefusedAsNetworkRatherThanSentBackForASecondTurn`
went red, and the runs beside it. The arm defines a mapped drive and a `subst`
onto it as real drive letters, asks the product about a path through the `subst`,
and removes both. Every assertion about the product passed; the red came from the
harness removing its own letter, after 49 ms: *"The alias 'H:' -> '\??\G:\dir'
could not be removed and is still defined as
'\Device\LanmanRedirector\;H:0000000000012345\10.255.255.1\share'. Remove it
before running this suite again: subst H: /d."* The definition found on `H:` is
the shape `DosDeviceAlias.MappedTo` writes, so another arm of the same run had
mapped a drive onto that letter.

| Run | When | Build | Result |
|---|---|---|---|
| `a34ce65-ps/` | 2026-10-04, 03:26:44Z to 03:31:55Z | lane q371's gate at `a34ce65`, 1.1.1-alpha.0.202, PowerShell half, `FULL RUN`, publish `FRESH` | **967 of 968**, this arm the one red |
| `a34ce65-bash/` | the same gate, 03:31:55Z to 03:37:11Z | the same commit and binary, Git Bash half, `FULL RUN` | 968 of 968 |
| `a34ce65-ps-again/` | 2026-10-04, 03:53:55Z to 03:59:08Z | the same commit and binary, the PowerShell half run again | 968 of 968 |

**What the harness does, read and not measured.** `DosDeviceAlias.Define` takes
its lock, picks the first letter from `E:` upward that has no definition, and
defines it. `Dispose` takes the same lock to remove its definition and lets it go
before it reads the letter back to check that the removal took. Between those two
steps another arm's `Define` can find the letter free and map it, and the
read-back then finds that arm's definition and throws. That fits this run, and
nothing was measured to confirm it.

**How far the search for another red went.** On 2026-10-04 every `.log` under the
main checkout's `.work`, the lane worktrees' included, was searched for the
sentence *"could not be removed and is still defined"*: it is in this run's logs
and nowhere else.

## Cited by

| Record | What it takes from here |
|---|---|
| [`HAZARDS.md`](../../../HAZARDS.md#hazard-index) | The open row on a test's drive-letter alias being taken over by a parallel arm between its removal and its check |

## What was cut

The user-profile path, replaced by `%USERPROFILE%`, in the files that held it,
which are stored under `.trimmed.` names with the originals' digests in
`originals.sha256`. `cut_alias_red.py` is the script that cut them. Nothing else
was changed: the logs are the gate driver's and the test host's whole output for
those runs.

## Privacy

Nothing here names the user, the user profile or the machine.
