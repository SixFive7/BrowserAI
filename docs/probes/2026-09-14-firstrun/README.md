<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-14 - a non-silent install starts the app in a console window

Re-establishes
[A non-silent install starts the app in a console window](../../../kb/packaging/velopack.md#a-non-silent-install-starts-the-app-in-a-console-window-and-nobody-is-on-the-other-end-of-it----measured-2026-09-14)
and the uninstall-key finding in
[Two installs of one app id share one uninstall key](../../../kb/packaging/velopack.md#two-installs-of-one-app-id-share-one-uninstall-key----measured-2026-09-14),
which is also the hazard-index row about Velopack's app id being `BrowserAI`.
`launch-detached.ps1` starts `Setup.exe` from a windowless parent, `observe.ps1`
records the process tree and the top-level windows, and `watch-tree.ps1` records
the install root as it fills. Evidence:
[`docs/evidence/2026-09-14-firstrun/`](../../../docs/evidence/2026-09-14-firstrun/README.md).

WARNING - **sandbox it**: point `CLAUDE_CONFIG_DIR` at a scratch directory
first, because the install hook registers with the real client by name, and
export the Add/Remove key for the pack id, because the uninstall deletes it.
