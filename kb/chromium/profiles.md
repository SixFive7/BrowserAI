<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Profile directories, fallback, and native dialogs

**Versions in force** unless an entry says otherwise: Windows 11 Pro 26200 · Chrome for Testing 152.0.7977.8 (`chromium-1237`) · system Google Chrome 151.0.7922.138 · `@playwright/mcp` 0.0.79 · `playwright-core` 1.63.0-alpha-2026-08-05.
Measured on [the reference machine](../README.md#the-reference-machine).

Measured 2026-08-15. **The idea of deliberately poisoning a profile directory to
make an unwanted Chrome fail to start is refuted empirically, not merely from
source.**

**Path occupied by a file** → `RecursiveDirectoryCreate` fails →
`GetDefaultUserDataDirectory` fallback → **8 healthy processes, still running at
25 s**. MCP `initialize` and `browser_navigate` both returned OK. **The poisoning
is completely invisible to the MCP client.** Message window titled with the
fallback path. Not registered for restart. `[FLOATS]`

**Existing directory with a deny-all DACL** → **exits at ~2.5 s with code 21**
(`CHROME_RESULT_CODE_PROFILE_IN_USE`). **No fallback** -- the default profile
directory was never created. No dialog, no message window. **This is a different
code path from the file case**: `RecursiveDirectoryCreate` succeeds on an
existing directory, so there is no fallback; the singleton lock then fails and
Chrome fails closed. `[FLOATS]`

> ✅ *Verified 2026-10-03 @ Chrome for Testing 155.0.8059.12 (`chromium-1247`),
> `@playwright/mcp` 0.0.83, with one figure corrected (previously "exits at
> ~2.5 s").* **The file case still falls back invisibly**: through the payload's
> own `cli.js`, headless, with `userDataDir` naming a file, `initialize` and
> `browser_navigate` answered without error, and at 30.7 s seven processes were
> running with a message window titled with
> `%LOCALAPPDATA%\Google\Chrome for Testing\User Data`, not registered for
> restart. **The deny-all directory still exits 21 with no fallback and no
> window**, and it now does so **154 ms** after the launch. The arms and their
> logs: [the batch](../../docs/evidence/2026-10-03-reverify-0.0.83/README.md).

## Chromium's cookie store, and what it takes to read one -- measured 2026-08-18

**Measured 2026-08-18 @ Chrome for Testing 152.0.7977.8 (`chromium-1237`) /
`@playwright/mcp` 0.0.79 / `playwright-core` 1.63.0-alpha-2026-08-05 / Windows 11
Pro 26200.** **This entry exists because the sentence it replaces had none of
that.** *"The agent runs as the same Windows user, so DPAPI decrypts for it"* is
the argument that removed BrowserAI's entire `(tool, mode)` permission layer on
2026-08-18; it was repeated in six documents and terminated in a `kb/` line with
no date, no version and no method. The specific unasked question was **App-Bound
Encryption** -- Chrome 127+ wraps the DPAPI key in a second layer bound to the
browser's own code identity, precisely to stop another process on the same
machine doing this. **The argument holds. ABE is not in play here.** `[FLOATS]`

**The subject was a session BrowserAI configures**, created under the
repository's own scratch tree and nothing else: a `headless` child on the
generated config, navigated twice to a **loopback** HTTP server that set
`Set-Cookie: browserai_probe=...; Max-Age=86400`, confirmed live through
`document.cookie`, then closed with `browser_close` so the store was flushed.
**No profile outside that directory was read**, and the reader refuses any path
outside it by construction.

**What the profile holds.** 162 files, of which two matter:
`profile\Local State` (4,185 B) and `profile\Default\Network\Cookies` (20,480 B).

**What was measured, from a second process running as the same user:**

| Question | Answer |
|---|---|
| `os_crypt` keys in `Local State` | `audit_enabled` and `encrypted_key`. **Nothing else** |
| `os_crypt.app_bound_encrypted_key` | **absent** -- no ABE key was ever created for this profile |
| `encrypted_key` shape | 317 B, ASCII prefix `DPAPI`, remainder a DPAPI blob |
| Cookie row's scheme tag | **`v10`** -- the AES-256-GCM-under-DPAPI scheme. **Not `v20`**, which is what ABE writes |
| `CryptUnprotectData`, no entropy, no prompt struct, `dwFlags` 0 | **succeeded**, returning a **32-byte (256-bit)** master key |
| AES-256-GCM over the cookie's `encrypted_value` | **succeeded**: 90 B → 59 B of plaintext |
| The plaintext | a **32-byte prefix** and then the cookie value byte for byte |

**So the answer to *what exactly is required* is: DPAPI alone.** No elevation, no
COM, no service, no admin -- one `crypt32!CryptUnprotectData` and one AES-GCM,
against files the calling agent chose the location of. The 32-byte prefix on the
plaintext is Chromium's domain binding; it changes what a reader must skip and
nothing about whether it can read.

> ✅ *Verified 2026-10-03 @ Chrome for Testing 155.0.8059.12 (`chromium-1247`),
> `@playwright/mcp` 0.0.83, through a `headless` session of a BrowserAI published
> from `5f1166c`.* **Every row of the table holds**: `os_crypt` holds
> `audit_enabled` and `encrypted_key` and nothing else, no
> `app_bound_encrypted_key`, a 317-byte `DPAPI`-prefixed key that
> `CryptUnprotectData` turns into 32 bytes, scheme tag `v10`, and AES-256-GCM
> recovering the cookie: 101 B to 70 B, the value after a 32-byte prefix. **The
> prefix was checked this time and it is the SHA-256 of the cookie's host**,
> `127.0.0.1`, which is what *domain binding* above means. The cookie was set by a
> loopback server and read from a second process against a copy, as before:
> [the batch](../../docs/evidence/2026-10-03-reverify-0.0.83/README.md).

⚠️ **The stronger form of the result, and the reason it is not "ABE could not
possibly apply here".** `elevation_service.exe` **is present** in the provisioned
`chrome-win64` tree, and this machine **does have** a registered
`GoogleChromeElevationService` -- from the operator's own Google Chrome 151 under
`C:\Program Files`, stopped. So the ABE machinery exists on this machine and the
provisioned browser still produced a DPAPI-only key and a `v10` cookie. The
measurement is therefore about what a **zip-provisioned Chrome for Testing under
a custom `--user-data-dir`** does, not about what is installable. **What has not
been measured is the converse** -- whether any configuration of this browser
*would* produce `v20` -- and nothing here should be read as saying it cannot.
`[FLOATS]`

**How to re-establish.** Create the session as above; read
`os_crypt.encrypted_key` out of `Local State`, base64-decode it, drop the 5-byte
`DPAPI` prefix and pass the rest to `CryptUnprotectData`; read
`Default\Network\Cookies` **against a copy, read-only** (`node:sqlite` does it
with no dependency); split `encrypted_value` as 3-byte tag, 12-byte nonce,
ciphertext, 16-byte tag, and decrypt with the recovered key. **Check
`app_bound_encrypted_key` and the row's scheme tag first** -- they are what says
which scheme is in force, and a decrypt that fails without them looks like a
broken script, not like ABE.

## The dialog hazard -- worse than "a dialog appears"

**Chrome's "Failed to create data directory" box blocks startup entirely until
dismissed.** Measured on a short direct launch: at 6 s there was **one process,
no renderers, no GPU, and no registration**, with a visible `#32770` dialog. After
posting `WM_CLOSE`: **10 processes and registration**. `[FLOATS]`

> ✅ *Verified 2026-10-03 @ Chrome for Testing 155.0.8059.12 (`chromium-1247`),
> with one count corrected (previously "10 processes").* The dialog, titled
> *Failed to create data directory* with one OK button, was visible 215 ms after
> a direct launch against a path occupied by a file. At 6 s: **one browser
> process and nothing else**, the dialog the only visible window, no restart
> registration. After `WM_CLOSE`: **11 processes** (one browser, one GPU, seven
> renderers, two utility) and a registration of 157 characters. ⚠️ **What a
> dismissed dialog leads to is a headed browser on the fallback profile**: four
> seconds after the close, a visible *about:blank - Google Chrome for Testing*
> window was up, and the fallback directory had been created. This arm puts two
> windows on the screen and was run with the maintainer at the desk; what
> appeared and for how long is in
> [the batch](../../docs/evidence/2026-10-03-reverify-0.0.83/README.md).

- `--noerrdialogs` does **not** suppress it. A suppressing switch was not
  identified. `[UNVERIFIED]`
- Playwright's full arg list produced **no dialog at all** in the poisoned run --
  so the hazard is configuration-dependent and will not show up in every test.
- The dialog is class `#32770` and owned by a known PID, so it is findable and
  dismissable -- a usable mitigation, though prevention is better.

**This is the third native-dialog trap found this week**, after Firefox's
profile-lock modal (blocking up to 180 s;
`DEFAULT_PLAYWRIGHT_LAUNCH_TIMEOUT = 3 * 60 * 1e3`) and the same dialog reaching
a live desktop during measurement. The pattern is general enough to be
a rule: **the child's failure modes include GUI dialogs on a headless server, so
BrowserAI must validate every path it hands the child before launch.**

> ⚠️ `[UNVERIFIED]`, deliberately not tested: with `channel: "chrome"` and an
> unusable user-data-dir, Playwright's Chrome falls back to the **personal**
> profile, where `ProcessSingleton` forwards its command line to the
> already-running personal Chrome and exits. Running it would have driven the
> operator's own browser. It follows directly from the fallback and singleton
> behaviour both measured above, and it is a further argument for launching the
> Chrome for Testing build BrowserAI **provisions** instead of
> `channel: "chrome"`. **Provisioned, not bundled**: ["our own" means the build
> BrowserAI manages, never one shipped inside the
> installer](../../DECISIONS.md#processes-browsers-and-session-modes) -- the installer carries no
> browser at all.
