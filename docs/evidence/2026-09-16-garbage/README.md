<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-16 - what old versions and old install paths left on this machine

`candidates.csv` is the inventory taken before the machine sweep of 2026-09-16:
every BrowserAI-adjacent path outside this repository, with its size, its age,
what put it there and a verdict of `GARBAGE`, `ASK` or `KEEP`. The fourteen
`GARBAGE` rows are what was deleted that day; nothing marked `ASK` or `KEEP` was
touched. The builder is
[`build/probes/2026-09-16-garbage/`](../../../build/probes/2026-09-16-garbage/README.md).

**It is a snapshot of one machine on one day**, not a claim about any other -
re-run the builder rather than reading ages out of it.

## What the sweep actually removed, 2026-09-16

[`swept-2026-09-16.json`](swept-2026-09-16.json) is the sweep's own record: every
path, the row it satisfies, its file and directory counts, its size, and whether
it came away clean. **444 entries, 220,268,343 bytes. Nothing refused, nothing
failed, nothing already absent.**

⚠️ **Three rows had moved between the inventory and the sweep, and the numbers
below are what was measured at the moment of deletion rather than what the CSV
says.** `ms-playwright\b` was **27,560 files / 45,836,998 B**, not 26,891 /
44,652,496 - it had kept leaking for the five and a half hours in between, which
is the hazard row's point rather than a discrepancy in it.
`velopack_BrowserAI.app.test.log` was **651,907 B**, not 281 KiB. And there were
**436** empty `playwright-artifacts-*` directories, not ~300. Every empty-only
row was checked for emptiness first and would have been refused and listed had
it not been empty; none was.

⚠️ **The six dead-pid instance directories the CSV names were already gone**, and
twelve different dead ones stood in their place, all created after the inventory
was taken. Nothing in `%LOCALAPPDATA%\BrowserAI\` was touched on the strength of
a row that no longer described it.

**Nothing marked `ASK` or `KEEP` was touched**: not `Releases/`, not
`%LOCALAPPDATA%\ms-playwright` beyond its `b` subdirectory, not `SquirrelTemp`,
not `Downloads	mp-*`, not the session index, and not `velopack.log` or
`velopack_BrowserAI.app.log` - the two Velopack logs that were deleted are the
**dead** pack ids `BrowserAI` and `BrowserAI.app.test`, and the live app's own
log is still there.
