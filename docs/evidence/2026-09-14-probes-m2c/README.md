<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-14 - how much a snapshot of a dense page costs

The measurement `src/BrowserAI/Proxy/ServerInstructions.cs` cites for the inline
block it publishes: `results.json` is the per-page cost, `m2c.log` the run, and
the `dense-*.html` and `flat-*.html` files are the pages it was taken over.
