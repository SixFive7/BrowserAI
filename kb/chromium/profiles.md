<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Profile directories, fallback, and native dialogs

Measured 2026-08-15. **The maintainer's poison-the-profile idea is refuted
empirically, not merely from source.**

**Path occupied by a file** → `RecursiveDirectoryCreate` fails →
`GetDefaultUserDataDirectory` fallback → **8 healthy processes, still running at
25 s**. MCP `initialize` and `browser_navigate` both returned OK. **The poisoning
is completely invisible to the MCP client.** Message window titled with the
fallback path. Not registered for restart. `[FLOATS]`

**Existing directory with a deny-all DACL** → **exits at ~2.5 s with code 21**
(`CHROME_RESULT_CODE_PROFILE_IN_USE`). **No fallback** — the default profile
directory was never created. No dialog, no message window. **This is a different
code path from the file case**: `RecursiveDirectoryCreate` succeeds on an
existing directory, so there is no fallback; the singleton lock then fails and
Chrome fails closed. `[FLOATS]`

## The dialog hazard — worse than "a dialog appears"

**Chrome's "Failed to create data directory" box blocks startup entirely until
dismissed.** Measured on a short direct launch: at 6 s there was **one process,
no renderers, no GPU, and no registration**, with a visible `#32770` dialog. After
posting `WM_CLOSE`: **10 processes and registration**. `[FLOATS]`

- `--noerrdialogs` does **not** suppress it. A suppressing switch was not
  identified. `[UNVERIFIED]`
- Playwright's full arg list produced **no dialog at all** in the poisoned run —
  so the hazard is configuration-dependent and will not show up in every test.
- The dialog is class `#32770` and owned by a known PID, so it is findable and
  dismissable — a usable mitigation, though prevention is better.

**This is the third native-dialog trap found this week**, after Firefox's
profile-lock modal (blocking up to 180 s;
`DEFAULT_PLAYWRIGHT_LAUNCH_TIMEOUT = 3 * 60 * 1e3`) and the same dialog reaching
the maintainer's desktop during measurement. The pattern is general enough to be
a rule: **the child's failure modes include GUI dialogs on a headless server, so
BrowserAI must validate every path it hands the child before launch.**

> ⚠️ `[UNVERIFIED]`, deliberately not tested: with `channel: "chrome"` and an
> unusable user-data-dir, Playwright's Chrome falls back to the **personal**
> profile, where `ProcessSingleton` forwards its command line to the
> already-running personal Chrome and exits. Running it would have driven the
> maintainer's browser. It follows directly from the fallback and singleton
> behaviour both measured above, and it is a further argument for launching the
> Chrome for Testing build BrowserAI **provisions** rather than
> `channel: "chrome"`. **Provisioned, not bundled**: ["our own" means the build
> BrowserAI manages, never one shipped inside the
> installer](../../README.md#settled-2026-08-15) — the installer carries no
> browser at all.
