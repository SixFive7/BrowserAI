<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-14 - a non-silent install starts the app in a console window

Evidence for
[A non-silent install starts the app in a console window](../../../kb/packaging/velopack.md#a-non-silent-install-starts-the-app-in-a-console-window-and-nobody-is-on-the-other-end-of-it--measured-2026-09-14),
for
[Two installs of one app id share one uninstall key](../../../kb/packaging/velopack.md#two-installs-of-one-app-id-share-one-uninstall-key--measured-2026-09-14),
for `src/BrowserAI/Program.cs`'s remark about the full exe path on the desktop,
and for `InstallerHandoffTests`.

`setup.log` and `uninstall.log` are Velopack's own; `observe.jsonl` is the
process-and-window sampling; `tree.jsonl` is the install root filling;
`arp-before.reg` and `arp-after.reg` are the Add/Remove key before the probe and
after it was restored - **identical**, which is the finding. The rig is
[`build/probes/2026-09-14-firstrun/`](../../../build/probes/2026-09-14-firstrun/README.md).
