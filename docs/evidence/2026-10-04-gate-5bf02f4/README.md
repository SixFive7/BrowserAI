<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-04 - lane q371's gate at 5bf02f4, the first after a push, and its two reds

**What this is.** The two-shell ordinary gate lane q371 ran after it pushed
`5bf02f48` to `master`, under the push rule of 2026-10-04 (push first, gate after,
fix forward). It ran from a second worktree of the lane, `.work\wt\q371-gate`,
checked out at the pushed commit, so the lane's own worktree stayed free for the
next commit.

| Run | When | Build | Result |
|---|---|---|---|
| `5bf02f4-ps/` | 2026-10-04, 13:37:43Z to 13:42:58Z | `5bf02f48`, 1.1.1-alpha.0.206, PowerShell half, `FULL RUN`, publish `FRESH` | **980 of 981** |
| `5bf02f4-bash/` | 13:42:58Z to 13:48:44Z | the same commit and binary, Git Bash half, `FULL RUN` | **979 of 981** |

**The red in both halves is the checkout's path.**
`CanonicalPathTests.TheAncestorWalkGivesUpExactlyPastItsLimitAndSaysSoWhileStillAnsweringAPath`
composes 66 levels of `\L` under the suite's scratch root and then calls
`SessionPath.For` on the result. In `.work\wt\q371-gate` that path was 242
characters, and `SessionPath.For` refuses a session directory over 240
(`SessionPath.LongestSessionDirectory`): *"... is 242 characters, and a session
directory may be at most 240"*. The same arm passed in every gate run from
`.work\wt\q371`, five characters shorter, so the arm depends on where the
repository is checked out.

**The second red is in the Git Bash half only.**
`BrowserContainmentTests.AChromiumTreeIsContainedAndItsProfileDeletesCleanly`
read `escapees` 0 and then met a walked row whose `inOurJob` was not `true`. The
arm's own report was in its scratch directory, which the run removes, so which
process it was and why is not established. The PowerShell half ran the same arm
on the same binary and passed it. A sibling arm met the same shape on 2026-09-16,
a helper that exited between the walk and the per-row query, and was closed by
classifying that case (`ProcessQueryVerdict.ForFailedOpen`); this arm does not
use that classification.

**A first run that started nothing.** The gate driver was started twice in the
same worktree a few seconds apart, by mistake. The second publish failed against
the first's open files, and that run's two halves exited within four seconds,
before any test ran; the run the table describes took the suite lock after it and
ran alone. Both runs wrote the same log names, so only the run in the table is
here.

## Cited by

| Record | What it takes from here |
|---|---|
| [`HAZARDS.md`](../../../HAZARDS.md#hazard-index) | The two open rows on these reds |

## What was cut

The user-profile path, replaced by `%USERPROFILE%`, in the four files that held
it, which are stored under `.trimmed.` names with the originals' digests in
`originals.sha256`. `cut_gate_5bf02f4.py` is the script that cut them. Nothing
else was changed.

## Privacy

Nothing here names the user, the user profile or the machine.
