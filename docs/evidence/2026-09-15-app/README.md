<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-15 - two binaries in one pack

Evidence for
[Two binaries in one pack, measured end to end](../../../kb/packaging/velopack.md#two-binaries-in-one-pack-measured-end-to-end----2026-09-15).
`pack2.log` is the pack run; `packfeed/` is the feed it produced, text files
only.

**Cut:** the feed's three large binaries - the `.nupkg` (54,853,873 B), the
`.exe` (59,353,329 B) and the `.zip` (54,824,804 B), and their copies under
`archive/` and `test-pack/`. `packfeed-inventory.csv` lists every one of the 21
files the feed held with its size and SHA-256, so what was cut is identifiable
and not merely absent. 392,969,672 bytes in total.
