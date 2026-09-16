<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-14 - a WebP screenshot past 16,383 px is zero bytes

Evidence for
[A WebP screenshot past 16,383 px comes back as zero bytes](../../../kb/playwright/tools-and-artifacts.md#a-webp-screenshot-past-16383-px-comes-back-as-zero-bytes-with-iserror-false--measured-2026-09-14)
and for the `TODO.md` entry beside it, including the duplicate search over both
trackers and the issue as posted. The rig is
[`build/probes/2026-09-14-webp-ask/`](../../../build/probes/2026-09-14-webp-ask/README.md).

**Cut:** the four large rasters, whose claim is their size and nothing else -
`page-...-106Z.png` and `page-...-416Z.png` at 137,816 bytes each, and
`page-...-352Z.jpeg` and `page-...-655Z.jpeg` at 768,991 bytes each. The four
`.webp` files are kept: the two at 12,284 bytes are the *valid VP8X* half of the
claim, and the two at zero bytes are the claim itself.
