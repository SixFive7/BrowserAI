<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-23 / 2026-09-24 -- the password-save prompt, what suppresses it, and what the switch costs

**What this is.** The Q255 reproduction and suppression, and the Q259 follow-up
the maintainer asked for once the fix was chosen: whether
`--enable-automation` makes the browser look more automated to a page or a
server. **341 files, 1,696,345 bytes.** Taken at Chromium **154.0.8037.0**
(revision 1246) and Firefox **156.0** (revision 1549), `@playwright/mcp`
**0.0.82**, `playwright-core` **1.64.0-alpha-1789764292000**, node **v24.21.0**,
on Windows 10.0.26200.

⚠️ **Every browser here ran against a scratch profile under the researcher's own
directory**, never the provisioned session tree of a real install, and every
window observation is keyed on **handles and on the launching job's pid set** --
never on an image name.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: configuration](../../../kb/playwright/configuration.md) | *The password-save prompt and what actually suppresses it*, and *What `--enable-automation` changes that a page can see* |
| [re-verification index](../../../kb/re-verification.md) | Row 147 (the prompt and the switch) and row 109 (`navigator.webdriver`, whose staleness mark this batch reverted) |
| `PasswordPromptTests` | The window-class and title strings its planted red named |
| [`DECISIONS.md`](../../../DECISIONS.md#processes-browsers-and-session-modes) | Q257: the switch in `launchOptions.args` and the Firefox preference, both round-tripped |

## What is here

| Path | What it holds |
|---|---|
| `probe.mjs`, `config-probe.mjs` | The reproduction: a headed and a headless launch, a sign-in POST, and `EnumWindows` before and after filtered by the job's pids. The second drives the **product's own generated config** so the round trip is exercised |
| `out/`, `cfg/` | One directory per arm. `before.txt` / `after.txt` and `windows-before.json` / `windows-after.json` are the window sets; `Preferences-after.json` is what Chromium wrote back; `resolved-config.txt` and `config.json` are what the child was actually given |
| `fp.mjs`, `fp/` | The Q259 fingerprint diff: 43 JS-visible properties and 14 request headers, with and without the switch, through the product funnel and through raw `playwright-core` |
| `google.mjs`, `google/`, `gbatch.sh` | The Google search test, eight arms interleaved, each with its verdict, its console log and its page screenshot |
| `shoot.ps1`, `shoot-window.ps1`, `windows-of.ps1` | The window capture and enumeration helpers |
| `run-arms.sh`, `run-arms2.sh`, `*.log` | The arm drivers and their console output |

**The four window images that carry a finding are kept**: `win-Save_password_.png`
is the prompt itself, `win-Intermediate_D3D_Window.png` is the second window that
arrives with it, and the two `Mozilla*WindowClass.png` files are the Firefox
doorhanger raised by the positive control with `signon.rememberSignons` set back
to `true`.

## What was cut

⚠️ **The 28 full-desktop screenshots are gone, and they were 128 MB of the
130 MB this batch would otherwise be.** Each `screen-before.png` /
`screen-after.png` pair is a 3840x2160 capture of the whole desktop, taken to
show that nothing else on the screen changed; the per-window captures answer the
same question at 1/140th of the size and are what the findings actually quote.

⚠️ **Every browser profile tree is absent**, `out/*/profile/`, `cfg/*/profile/`,
`fp/*/profile-*/` and `google/*/profile-*/`: 894 MB of Chromium and Firefox
working state. What was read out of them is here --
`Preferences-after.json` per arm, which is the file the
`credentials_enable_service` measurement turns on.

**And 13 `win-signed_in_*.png` captures are dropped**: they are the page's own
window, present in every arm including the ones where nothing was being
established.

⚠️ **Two departures from the bytes as taken, both about what a public record may
carry.** `google/jar.txt` was a Netscape cookie jar holding live `google.com`
cookies from the scratch profiles, which is the artifact class
[`.gitignore`](../../../.gitignore) refuses by name everywhere else, and it is
**deleted**. And the maintainer's own public address, which the Google arms
printed so that *every arm ran from one address* could be established, is
**replaced by `<residential address, redacted>` in 33 files**. The claim it
supports is unaffected: what mattered was that the eight arms shared an address
and were interleaved, never which address it was.
