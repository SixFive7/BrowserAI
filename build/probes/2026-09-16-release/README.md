<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-16 - what Setup asks before installing over an install

`Read-Dialog.ps1` enumerates a dialog's child windows and reads their text,
which is how
[Setup will not install over an existing install without being told to](../../../kb/packaging/velopack.md#setup-will-not-install-over-an-existing-install-without-being-told-to-and-on-a-same-version-re-ship-the-button-says-repair--measured-2026-09-16)
and re-verification row 130 read the `#32770` and its `Repair` button.
