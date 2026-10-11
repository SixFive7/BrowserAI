<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Browser resurrection after a reboot

**Versions in force** unless an entry says otherwise: Windows 11 Pro 26200 · Chrome for Testing 152.0.7977.8 (`chromium-1237`) · Firefox 153.0 (`firefox-1539`) · system Google Chrome 151.0.7922.138 · `@playwright/mcp` 0.0.79 · `playwright-core` 1.63.0-alpha-2026-08-05.
Measured on [the reference machine](../README.md#the-reference-machine).

Measured 2026-08-15 with a throwaway probe that called
`GetApplicationRestartSettings` against live browsers; that harness no longer
exists, and **the parts that survived are re-established by
`BrowserContainmentTests` and `FirefoxTests` in this repository's own suite**,
named per entry below. Anything not carrying a test name was measured once by the
spike and is marked as such -- the general route is the same either way: ask the
Win32 API of a live process, never argue from a command line's length.

> ⚠️ **Corrected 2026-08-16 @ build-order step 17 (previously: the whole of this
> article's verdict was stated of "Playwright-launched" browsers, and the margin
> table was the answer to "can a browser we launch be resurrected").** **It holds
> for Chromium and not for Firefox**, and the difference is not one of degree:
> Chromium's registration fails because the command line is too long, and Firefox
> never goes near that limit -- its call site is gated on a preference instead. So
> the length argument covers one family and is silent about the other, and
> [the measurement below](#the-firefox-half-measured-on-both-sides----2026-08-16)
> is what fills the gap. The consequence is that BrowserAI does ship a
> restart-registration lever after all, for Firefox only; see
> [DECISIONS → Processes, browsers and session modes](../../DECISIONS.md#processes-browsers-and-session-modes).

## The verdict

**Playwright-launched Chrome does not register for restart, and never has.**
`GetApplicationRestartSettings` on a live Playwright browser returns
`0x80070490` (`ERROR_NOT_FOUND`). The command line is **1,770 characters**
against a limit of 1,023. `[FLOATS]`

**The apparatus is proven, not assumed.** The same Chrome binary launched
directly with a 206-character command line returns `0x00000000`, flags `0x7`
(`RESTART_NO_CRASH | RESTART_NO_HANG | RESTART_NO_PATCH`), registered command
line 189 characters. Both ends of the measurement are live. `[FLOATS]`

**The boundary is 1023, not 1024** -- `RESTART_MAX_CMD_LINE` counts the NUL.
Reproduced twice: synthetically in an isolated process, and **inside Chrome
itself** by padding a short command line (1023 → registered, 1024 →
`ERROR_NOT_FOUND`). Rejection is total and silent -- no truncation, no partial
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
RESTART_NO_PATCH` -- deliberately **omitting** `RESTART_NO_REBOOT`.
`GetRestartCommandLine` rebuilds from a sorted, deduplicated `std::map`, drops
non-switch args and `kFromInstaller`, strips `about_flags` sentinels, and appends
`--restore-last-session` and `--restart`. `[FLOATS]`

**Firefox registers too**, in `nsAppRunner.cpp`, with `RESTART_NO_CRASH |
RESTART_NO_HANG` and the original argv (`argv[0]` replaced by `-os-restarted`),
so `-profile <dir>` survives. Gated on the pref
`toolkit.winRegisterApplicationRestart`, default `true`, **observed at runtime** --
setting it false calls `UnregisterApplicationRestart()`. This is the only place
resurrection can be prevented outright instead of cleaned up after. `[FLOATS]`

## The Firefox half, measured on both sides -- 2026-08-16

**A Playwright-launched Firefox registers, and the preference turns it off.**
Both arms measured the same day on the same machine, against the Firefox
BrowserAI provisions (`firefox-1539`, Firefox 153.0), by asking
`GetApplicationRestartSettings` of the **live processes** after a real
`browser_navigate` completed. `[FLOATS]`

| Launch | Processes from our browsers root | Registered |
|---|---:|---:|
| Upstream defaults, no preference set | 7 | **1** (`S_OK`) |
| BrowserAI's generated config | 7 | **0** (all `ERROR_NOT_FOUND`) |

Re-establish with `BrowserContainmentTests.AFirefoxTreeIsContainedAndItsProfileDeletesCleanly`
-- the control, which launches from a hand-written config with no preference --
and `FirefoxTests.AFirefoxWeLaunchedIsAttributedToItsSessionAndIsNotRegisteredForRestart`,
which launches from the product's generator. **Both are assertions**, so a
change in either direction is a red build.

**The preference does not arrive the way it looks like it does, and this is the
trap.** `firefoxUserPrefs` is written into the profile's `user.js` only on the
**BiDi** launcher's path -- `BidiFirefox.prepareUserDataDir` → `createProfile` →
`writePreferences`, read in `coreBundle.js` 2026-08-16. `@playwright/mcp` takes
the **classic (juggler)** path, whose `prepareUserDataDir` is the base class's
empty one; there the preferences are delivered over the wire in
`FFBrowser.connect` as `Browser.enable { userPrefs }`. Measured: a driven
profile contains **no `user.js` at all**, and `browser_get_config` echoes
`browser.launchOptions.firefoxUserPrefs` intact. A test asserting the file would
be green-when-broken on the day the delivery moved, and was red-when-working on
the day it was written. `[FLOATS]`

> **The consequence, stated plainly: this is an unregistration, not
> a prevention.** The preference reaches Firefox after its own startup has run,
> so a Firefox launched through Playwright is registered for a moment and then
> unregisters itself. **The width of that window is `[UNVERIFIED]`** -- it was not
> measured, and measuring it means sampling `GetApplicationRestartSettings` from
> the instant the process appears. What is measured is the steady state, which
> is what a reboot or an update hours later would find.

**`--browser-test` does suppress registration.** Measured with two launches
differing only by the switch: 206 chars → registered; 221 chars → not. At 221 it
would have succeeded on length alone, so suppression is the only explanation. The
browser stays fully functional through Playwright. `[FLOATS]`

> **What actually resurrected the browsers that motivated this project is
> `[UNVERIFIED]`.** By
> elimination it is the Windows sign-in restore path and not
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
register where a healthy one would too -- but the swap is path-for-path, worth ~12
characters (1722 as-launched vs 1710 rewritten). Against a 699-character overflow
it cannot bridge the gap, and direct measurement agrees: the poisoned Playwright
browser was **not** registered. `[FLOATS]`

## An exited process answers `ERROR_PARTIAL_COPY`, not a registration -- measured 2026-10-11

**`GetApplicationRestartSettings` asked of a process that has exited answers
`0x8007012B`**, `HRESULT_FROM_WIN32(ERROR_PARTIAL_COPY)`, when another handle to the
process is still open, so its pid still opens: the registration is read out of the
process's address space, and an exited process has none. Measured 2026-10-11 on
Windows 11 Pro 10.0.26300 with the program below: a `cmd.exe /d /c exit 0`, waited for
with its handle held, answered `0x8007012B`; a `cmd.exe` that was running answered
`0x80070490`, and the same `cmd.exe` asked again after its exit answered `0x8007012B`.
`[FLOATS]`

**Why it matters here.** An arm that walks a browser's tree and asks every process
can meet one that exits in between, and on 2026-10-11 the Chromium arm of
`BrowserContainmentTests` did, in the first gate after the maintainer's decision 54:
*"pid 79676 answered 0x8007012B rather than ERROR_NOT_FOUND"*. Since that day the
suite's reader asks the exit code of the same handle when the call answers this, and
reads an exited process as `Gone`, no answer either way, and both arms that walk a
browser leave it out.

**To re-establish it**, run this from PowerShell 7; the three lines read `0x8007012B`,
`0x80070490` and `0x8007012B` while it holds:

```powershell
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Restart {
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] public static extern int GetApplicationRestartSettings(IntPtr process, IntPtr commandLine, ref uint size, out uint flags);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    public static int Of(int pid) {
        var h = OpenProcess(0x400 | 0x10, false, (uint)pid);
        if (h == IntPtr.Zero) return Marshal.GetLastWin32Error() | 0x70000000;
        uint size = 0; uint flags;
        var r = GetApplicationRestartSettings(h, IntPtr.Zero, ref size, out flags);
        CloseHandle(h);
        return r;
    }
}
"@
$info = [System.Diagnostics.ProcessStartInfo]::new((Join-Path $env:SystemRoot 'System32\cmd.exe'), '/d /c exit 0')
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$exited = [System.Diagnostics.Process]::Start($info)
$exited.WaitForExit()
"exited: 0x{0:X8}" -f [Restart]::Of($exited.Id)
$info2 = [System.Diagnostics.ProcessStartInfo]::new((Join-Path $env:SystemRoot 'System32\cmd.exe'), '/d /c ping -n 3 127.0.0.1 >nul')
$info2.UseShellExecute = $false
$info2.CreateNoWindow = $true
$running = [System.Diagnostics.Process]::Start($info2)
Start-Sleep -Milliseconds 300
"running: 0x{0:X8}" -f [Restart]::Of($running.Id)
$running.WaitForExit()
"after its exit: 0x{0:X8}" -f [Restart]::Of($running.Id)
```

`RestartRegistrationTests.AProcessThatHasExitedReadsAsGoneAndOneThatRunsReadsAsNotRegistered`
holds the reader's half of it on every run.

## Machine state

`HKCU\...\Winlogon\RestartApps = 1` -- Settings › Accounts › Sign-in options ›
"Automatically save my restartable apps". `DisableAutomaticRestartSignOn` not
set, so ARSO is at its default: apps relaunch **into a locked session before
anyone signs in**, which is why they were invisible. `HKCU\...\RunOnce` present
but empty (consumed at logon). `HKCU\...\Run` has 12 entries, **no Chrome entry**
-- consistent with `StartupLaunchManager::UpdateLaunchOnStartup` returning early
whenever `--user-data-dir` is present, so Chrome never writes a Run entry for a
Playwright profile. `[MACHINE]`

**BrowserAI must never read or write any of this.** `RestartApps` is a personal,
global, per-user setting.
