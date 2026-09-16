<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-16 - rendering the shipped assets, and reading the icon back out

`render-assets.mjs` is what produced every raster in
[`assets/`](../../../assets/README.md): each size rendered natively by headless
Chromium rather than downscaled, `omitBackground` on, `deviceScaleFactor: 1`. It
drives the Chromium already in this machine's `%LocalAppData%\ms-playwright`
cache through `playwright-core`, so nothing is downloaded and the product's own
browsers root is not touched.

`Read-GroupIcon.ps1` reads the `RT_GROUP_ICON` resource back out of a built
executable, which is the only way to tell an icon that was embedded from one
that was merely copied beside the exe.
