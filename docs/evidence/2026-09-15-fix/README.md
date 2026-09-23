<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-15 - three repros from the day of the fixes

`repro-red-2.txt` is the orphaned-console repro
`tests/BrowserAI.Tests/Harness/OrphanedConsoleStart.cs` cites; `repro-red-3.txt`
is the one `InstallerHandoffTests` cites; `repro-firstrun.txt` is the
`VELOPACK_FIRSTRUN` run
[kb](../../../kb/packaging/velopack.md#re-measured-2026-09-15-against-the-published-v100-and-the-paragraph-above-was-half-wrong)
cites. `consoleprobe/` holds the two parked-stdin captures behind
[A read parked on standard input is woken by neither cancelling it nor disposing the stream](../../../kb/windows/processes.md#a-read-parked-on-standard-input-is-woken-by-neither-cancelling-it-nor-disposing-the-stream----measured-2026-09-15);
the probe itself is
[`docs/probes/2026-09-15-consoleprobe/`](../../../docs/probes/2026-09-15-consoleprobe/README.md).
