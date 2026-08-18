<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# `Runtime\` — the payload, the browsers, and the one recursive delete

Everything here writes to or deletes from a tree the user did not choose, on evidence read from the payload. The rules below hold for every file; each says what enforces it, and where the mechanism stops.

- **Every tree delete goes through `TreeDelete.Remove`.** It walks post-order with a `try`/`catch` per node and returns **every** node it could not remove, where the framework primitive reports one and a caller asked "what survived?" then cannot answer. `Directory.Delete(String, Boolean)` is banned repository-wide in [`build/BannedSymbols.txt`](../../../build/BannedSymbols.txt) with zero suppressions anywhere. **The ban cannot see a delete loop written by hand**, and `TreeDelete` itself is the one file that legitimately calls the single-argument overload.
- **Nothing spells a browser revision, a directory name or a download URL.** All of it is read from the payload's own `browsers.json` through `BrowsersManifest` and `ProvisionedBrowsers`. **Nothing asserts the absence of a hand-typed one** — and a hand-typed revision is correct until the next resolve and silently wrong after it.
- **A browser is identified by full image path and creation time, never by image name** — the same rule as `Interop\`, and it reaches here through `ProvisionedBrowsers`, which is the only place the candidate strings come from.
- **Every path is absolute and derived; nothing is searched for and nothing resolves through `PATH`.** `PayloadLayout` names `node.exe` and the `@playwright/mcp` tree absolutely and verifies them, so a missing file is reported as a missing file rather than as a failure inside `CreateProcessW`. **`AppContext.BaseDirectory` is correct in `PayloadLayout` and forbidden everywhere else** — a payload *should* be replaced wholesale by an update; a log or a browser tree must not be.
- **A long pass reports what it did, measured, not what it intended.** `RevisionPrune` weighs the tree before and after rather than summing the sizes it meant to delete. **Nothing enforces this**; it is the difference between a report and a restatement of the plan.
