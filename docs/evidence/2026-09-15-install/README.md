<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-15 - installing the published v1.0.0 and watching it

Evidence for
[Re-measured 2026-09-15 against the published v1.0.0](../../../kb/packaging/velopack.md#re-measured-2026-09-15-against-the-published-v100-and-the-paragraph-above-was-half-wrong):
`setup.log` lines 44 and 51 are the two creation-flag readings,
`install-observe.jsonl` the window-and-process sampling,
`deleted-defaultroot-inventory.csv` what the uninstall took with it, and
`rc-uninstall.log` the release-candidate uninstall. The `before-*` files are the
baseline each diff is against - the Add/Remove key, the client registration, the
default root and the RC root as they stood before anything ran. The rig is
[`docs/probes/2026-09-15-install/`](../../../docs/probes/2026-09-15-install/README.md).

**Cut:** the `BrowserAI.exe` that was installed (54,659,603 B) - it is the
published `v1.0.0` Setup asset and is downloadable from the release - and four
zero-byte `.stop` sentinels the watchers used to end themselves.
