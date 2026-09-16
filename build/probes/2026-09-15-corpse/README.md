<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-15 - measuring the app that does not exit

`Measure-Corpse.ps1` is the re-establishment named by
[Re-measured 2026-09-15 against the published v1.0.0](../../../kb/packaging/velopack.md#re-measured-2026-09-15-against-the-published-v100-and-the-paragraph-above-was-half-wrong):
it samples a pid's threads, handles and children after the process has logged
that it is exiting, which is how the 213.6 s figure was taken.
