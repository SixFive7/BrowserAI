<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Browser resurrection after a reboot

Measured 2026-08-15. Harness: `.work/restart-measure/RestartProbe.exe`.

## The verdict

**Playwright-launched Chrome does not register for restart, and never has.**
`GetApplicationRestartSettings` on a live Playwright browser returns
`0x80070490` (`ERROR_NOT_FOUND`). The command line is **1,770 characters**
against a limit of 1,023. `[FLOATS]`

**The apparatus is proven, not assumed.** The same Chrome binary launched
directly with a 206-character command line returns `0x00000000`, flags `0x7`
(`RESTART_NO_CRASH | RESTART_NO_HANG | RESTART_NO_PATCH`), registered command
line 189 characters. Both ends of the measurement are live. `[FLOATS]`

**The boundary is 1023, not 1024** — `RESTART_MAX_CMD_LINE` counts the NUL.
Reproduced twice: synthetically in an isolated process, and **inside Chrome
itself** by padding a short command line (1023 → registered, 1024 →
`ERROR_NOT_FOUND`). Rejection is total and silent — no truncation, no partial
registration; the browser runs normally either way. `[STABLE]`

## Margins, per shippable configuration

Restart command lines computed by a validated reimplementation of
`GetRestartCommandLine` (checked exactly against four measured registrations:
189, 723, 998, 1023 characters). `[FLOATS]`

| Config | Process cmdline | Restart cmdline | Margin over 1023 |
|---|---:|---:|---:|
| Chrome, headless, short path | 1770 | 1738 | −715 |
| Chrome, headless, long path (144) | 1862 | 1830 | −807 |
| Chrome, **headed** | 1626 | 1554 | **−531** |
| CfT, headless | 1797 | 1741 | −718 |
| CfT, **headed** | 1653 | 1557 | −534 |
| `chrome-headless-shell` | 1844 | 1743 | −720 |

**The margin is arg-list-driven, not path-driven.** Profile path length
contributes 1:1 (52 → 144 characters moved the restart command line 1738 → 1830,
exactly +92). Playwright would have to delete **more than 531 characters** of
switches before registration silently returned. CfT and system Chrome differ by
~3 characters.

**Headless does not branch.** A *short* headed command line (192 chars) registers
just as a short headless one does. Length is the only variable at that call site.
`[FLOATS]`

## The mechanism, and what is still unproven

Chromium calls `RegisterApplicationRestart` in
`ChromeBrowserMainParts::PreMainMessageLoopRunImpl()`, guarded only by
`--browser-test`. It passes `RESTART_NO_CRASH | RESTART_NO_HANG |
RESTART_NO_PATCH` — deliberately **omitting** `RESTART_NO_REBOOT`.
`GetRestartCommandLine` rebuilds from a sorted, deduplicated `std::map`, drops
non-switch args and `kFromInstaller`, strips `about_flags` sentinels, and appends
`--restore-last-session` and `--restart`. `[FLOATS]`

**Firefox registers too**, in `nsAppRunner.cpp`, with `RESTART_NO_CRASH |
RESTART_NO_HANG` and the original argv (`argv[0]` replaced by `-os-restarted`),
so `-profile <dir>` survives. Gated on the pref
`toolkit.winRegisterApplicationRestart`, default `true`, **observed at runtime** —
setting it false calls `UnregisterApplicationRestart()`. This is the only place
resurrection can be prevented outright rather than cleaned up after. `[FLOATS]`

**`--browser-test` does suppress registration.** Measured with two launches
differing only by the switch: 206 chars → registered; 221 chars → not. At 221 it
would have succeeded on length alone, so suppression is the only explanation. The
browser stays fully functional through Playwright. `[FLOATS]`

> **What actually resurrected the maintainer's browsers is `[UNVERIFIED]`.** By
> elimination it is the Windows sign-in restore path rather than
> `RegisterApplicationRestart`, which is now excluded by measurement. Observing
> the sign-in path directly requires a reboot, which was not performed. The story
> is coherent: the legacy setup ran **headed** system Chrome, which has visible
> top-level windows and is therefore eligible for the session snapshot, whereas
> headless Chrome has none.
>
> **The diagnostic, if it happens again:** read the resurrected process's command
> line. **Alphabetically sorted switches with `--restart --restore-last-session`
> and no `about:blank`** → `RegisterApplicationRestart`. **Playwright's original
> arg order** → the sign-in snapshot, and no registration lever would have helped.

## Fallback profiles do not close the gap

The in-process `base::CommandLine` **is** rewritten when Chrome falls back to a
default profile; the **PEB command line is never rewritten** (the poisoned
`--user-data-dir` survives verbatim at 1803 chars). So a broken Chrome *can*
register where a healthy one would too — but the swap is path-for-path, worth ~12
characters (1722 as-launched vs 1710 rewritten). Against a 699-character overflow
it cannot bridge the gap, and direct measurement agrees: the poisoned Playwright
browser was **not** registered. `[FLOATS]`

## Machine state

`HKCU\...\Winlogon\RestartApps = 1` — Settings › Accounts › Sign-in options ›
"Automatically save my restartable apps". `DisableAutomaticRestartSignOn` not
set, so ARSO is at its default: apps relaunch **into a locked session before
anyone signs in**, which is why they were invisible. `HKCU\...\RunOnce` present
but empty (consumed at logon). `HKCU\...\Run` has 12 entries, **no Chrome entry**
— consistent with `StartupLaunchManager::UpdateLaunchOnStartup` returning early
whenever `--user-data-dir` is present, so Chrome never writes a Run entry for a
Playwright profile. `[MACHINE]`

**BrowserAI must never read or write any of this.** `RestartApps` is a personal,
global, per-user setting.
