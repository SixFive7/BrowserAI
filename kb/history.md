<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# History: the legacy setup, and corrections

## The legacy setup and this machine

Everything here is `[MACHINE]`. It is motivation for the project, and none of it
generalises. It is recorded because the charter's opening argument cites these
numbers and they carry no other provenance.

**13 copies of `playwright/launch.ps1` across 10 repositories.** Filesystem sweep
of `C:\Source` to depth 7, **2026-08-13**: `ExoFabric/Infrastructure`,
`Netwerkplek`, `FluxTone`, `HitsterCardGenerator`, `ImmichDater`, `Jeeves`,
`PortainerCompose`, `StationeersPlus`, `SyncthingMonitor`, `Workspace657` — plus
3 worktree/backup copies inside `StationeersPlus`. **All nine non-Workspace657
copies are byte-identical to each other and all differ from Workspace657**, and
the same holds for `.claude/hooks/playwright-config-hook.ps1`. If the true count
is 15+, the remainder live outside `C:\Source` or deeper than 7 levels.

**Thirteen checkouts means thirteen `persistent/profile/` directories**, so a
login established in one repository does nothing for the other twelve.

**A stderr-pipe inheritance bug cost 11.71 s per spawn; the fix took it to
0.37 s.** `Start-Process` redirection does not prevent stderr-pipe inheritance, so
a client reading stderr blocked for the entire browser download. Diagnosed and
fixed 2026-08-12/13, along with everything else in the charter's opening table.

**A hard startup failure logged identically to a clean shutdown for five days.**
The process handle was not cached before `WaitForExit`, so `.ExitCode` read back
`$null`. That is why a deleted CLI flag — `--output-mode`, removed in
`@playwright/mcp` 0.0.79, producing `error: unknown option` and exit 1 with **all
four servers dead** — went unnoticed.

**A healthy start prints `Session: <path>` to stderr every time**, which is why
warning on *any* stderr output was the wrong classifier. `[FLOATS]` — this one is
upstream behaviour rather than machine state. ⚠️ **Qualified 2026-08-16
@ `@playwright/mcp` 0.0.79 (previously stated without a condition):** the line
appears only when `saveSession` is on. It was unconditionally true *here* because
all four of this launcher's `config.json` files set it; it is not unconditionally
true of upstream. Re-measured and reasoned in
[kb: startup output](playwright/configuration.md#environment-merge-order-and-startup-output).

**One flat `output/` grew to 346 session directories and 1.5 GB in ~3 months**,
and nobody pruned it because nobody could tell what any of the directories had
been.

**Mutexes were named `Global\<RepoName>-PlaywrightInteractive`** — keyed on a
repository folder name rather than on the profile directory that actually
requires exclusivity. All four `config.json` files used paths relative to the
working directory, including `userDataDir: "playwright/persistent/profile"`, with
cwd guaranteed only by `Set-Location $RepoRoot`.

**Our own Chromium probes counted and killed by image name.** Harmless for
Chromium on this machine at that moment; adapted naively to Firefox it would have
killed **~40 personal `firefox.exe` processes**. This is the measurement behind
the structural never-by-image-name rule.

**A `deny` hook keyed on `browser_take_screenshot` exists in ten repositories**,
which is what a tool rename would silently disable.

## Corrections applied 2026-08-15 (late)

Recorded because a corrected number that leaves no trace is indistinguishable
from one that was never wrong, and because two of these were introduced *by this
session* rather than inherited.

**First-run download is 203.8 MB, not 323.5 MB.** Measured 2026-08-15 by exact
`content-length` from `cdn.playwright.dev`: `chrome-win64.zip` 202,283,919 B +
`ffmpeg-win64.zip` 1,411,741 B + `winldd-win64.zip` 128,684 B. On disk, 430.48 MiB
(re-measured 2026-08-16; the 433 recorded here was three rounded components added
up)
(chromium 428 + ffmpeg 4 + winldd 1). Slow-link arithmetic: **2 m 43 s at
10 Mbps, 27 m 11 s at 1 Mbps.** `[FLOATS]`

The superseded 323.5 MB / ~700 MiB figures were correct on 2026-08-14 and
included `chrome-headless-shell` (119.7 MB down, 269 MiB on disk). The
[2026-08-15 decision](../README.md#settled-2026-08-15) to run full Chromium in every
mode stopped provisioning the shell, which is what changed the number — the old
measurement was never wrong, it just stopped applying. Peak disk during
provisioning is now ~640 MiB while archive and extracted tree coexist, superseding
the ~0.9 GB previously stated.

**`--output-max-size` has no default; the charter said "unverified" in two places
after it had been established.** Verified in `coreBundle.js` on 2026-08-15:
`defaultConfig` carries only `browser` and `timeouts`, and `mergeConfig` filters
through `pickDefined`, which drops `undefined`. See
[Upstream configuration facts](playwright/configuration.md). The README's two
stale passages are retired. `[FLOATS]`

**The [§A](../plan/A-runtime.md#a-ship-and-own-the-runtime) payload table listed browsers
as bundled for a day after the decision
that they would not be.** Provisioning moved to first run on 2026-08-14; the table
row survived it. Installer payload is ~117 MB (`node.exe` 88.53 + JS tree 18.11 +
BrowserAI 9.76 MiB), and the ~806 MB figure remains the right number for a
**bundled** build if the Chrome-for-Testing redistribution question is ever
resolved favourably. **It is not the number for disk after first run**, which
this entry and [§A](../plan/A-runtime.md#a-ship-and-own-the-runtime) both went on to claim:
806 counts `chrome-headless-shell` (268.49 MB), which is not provisioned at all.
Disk after first run is the 116.40 MiB payload plus 430.48 MiB of browsers,
**546.88 MiB ≈ 573 MB** (both halves re-measured 2026-08-16; the browsers figure
was 433 MiB, and the ≈ 570 MB it produced was
[right by way of two cancelling unit conflations](playwright/provisioning-and-timings.md#first-run-provisioning)). `[MACHINE]` for the component sizes, `[FLOATS]` for what is in the
set.

> **The pattern worth noticing.** All three are the same defect: a measurement
> that was correct when taken, invalidated by a *later decision* rather than by
> upstream, and left in place because nothing links a decision to the numbers it
> falsifies. The re-verification index catches upstream drift; it does not catch
> this. **When a decision changes what is provisioned, configured or shipped, the
> measurements describing the old shape must be re-stated or retired in the same
> commit.**

**Firefox costs relative to Chromium** — measured 2026-08-14, and previously
recorded nowhere in the repository despite being cited in design discussion:
**~2× RAM, ~10× first navigate, ~24× idle CPU, ~20× profile disk.** Chromium
stays the default on these grounds alone. `[UNVERIFIED]` as to method — the
figures are carried forward from a measurement session whose harness was not
preserved, so treat them as order-of-magnitude guidance and re-measure before any
decision turns on them. `[FLOATS]`
