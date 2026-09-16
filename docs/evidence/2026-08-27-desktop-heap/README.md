<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-08-27 - the desktop heap ceiling nothing reports

The logs behind
[Desktop heap: the ceiling nothing reports, measured](../../../kb/windows/processes.md#desktop-heap-the-ceiling-nothing-reports-measured).
The rig is
[`build/probes/2026-08-27-desktop-heap/`](../../../build/probes/2026-08-27-desktop-heap/README.md).

**Cut, and how much:** `main1.log` was 12,016,030 bytes in 53 lines, five of them
2.4 MB each; `main1.log.trimmed.txt` holds all 53 verbatim with those five cut at
4,000 characters, and names every one. `main1/results.json` was 13,250,345 bytes
in 15 records; `results.trimmed.json` holds all 15 with the 15 string fields over
2,000 characters cut at that length, each listed in its own `_cut` block. Both
carry the SHA-256 of the file they came from. The per-process Chromium logs and
profile trees under `main1/` (111 MB) were not retained at all - re-run the rig.
