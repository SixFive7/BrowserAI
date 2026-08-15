<!--
SPDX-FileCopyrightText: 2026 Jori Huisman
SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
-->

# Knowledge base — what we measured

[`README.md`](README.md) records what we **decided**. This file records what we
**measured**, and it exists because those are different things with different
half-lives. A decision stays true until we change our minds. A measurement stays
true until upstream ships.

**What belongs here:** a fact about Chromium, Firefox, Playwright, Node or
Windows that we established by running something, reading a shipped binary, or
reading upstream source — together with enough provenance to re-establish it.

**What does not:** design decisions (README), work items (TODO), or the review
procedure ([`UPSTREAM-REVIEW.md`](UPSTREAM-REVIEW.md)).

## Conventions

Every entry carries a marker and a date:

| Marker | Meaning |
|---|---|
| **`[FLOATS]`** | Depends on a version this project floats. **Re-verify at every upstream review.** Listed in [Re-verification index](#re-verification-index). |
| **`[STABLE]`** | A Windows or protocol fact that upstream cannot move. Re-verify only on a Windows major version. |
| **`[MACHINE]`** | True of the maintainer's machine, not of the world. Never generalise; never act on it as if universal. |
| **`[UNVERIFIED]`** | Inferred, not observed. Says so, and says why it was not observed. |

**Never edit a result without re-running the measurement.** An entry whose number
was updated by reasoning rather than by running something is worse than no entry,
because it reads identically to one that was measured. If a re-check is owed and
has not happened, mark it `[STALE]` rather than guessing.

Versions in force for everything below unless stated otherwise:
`@playwright/mcp` 0.0.79 · `playwright-core` 1.63.0-alpha-2026-08-05 ·
Node 26.7.0 (libuv 1.52.1) · Chrome for Testing 152.0.7977.8 (`chromium-1237`) ·
system Google Chrome 151.0.7922.138 · Firefox `firefox-1539` · Windows 11 Pro
26200.

---

## 1. Windows job objects and process containment

Measured 2026-08-15. Harness: `.work/jobtest/`.

**The headline: containment holds.** 16 runs, **106 spawned processes, 0
escapees, 0 survivors**, across real Chromium and Firefox trees. `[FLOATS]`

**Job membership is inherited automatically.** MS Learn,
[Job Objects](https://learn.microsoft.com/windows/win32/procthread/job-objects#managing-processes-in-jobs):
*"After a process is associated with a job, by default any child processes it
creates using CreateProcess are also associated with the job."* Escaping requires
`CREATE_BREAKAWAY_FROM_JOB` **and** `JOB_OBJECT_LIMIT_BREAKAWAY_OK` on the job,
or `JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK`. This is the inverse of Linux
process-group semantics. `[STABLE]`

**A denied breakaway fails the launch rather than escaping.** Measured:
`CreateProcessW` returns `ERROR_ACCESS_DENIED` (5). libuv's own source gives the
same reason for avoiding the flag (`src/win/process.c:1124`). **This is the fact
the whole guarantee rests on** — a job granting no breakaway flags converts every
escape attempt into a launch failure. `[STABLE]`

**Nested jobs cannot launder a process out.** MS Learn,
[Nested Jobs](https://learn.microsoft.com/windows/win32/procthread/nested-jobs):
a breakaway *"moves up the hierarchy until it reaches a job that does not permit
breakaway."* Depth 4 measured (outer → ours → libuv's → Chromium sandbox).
`KILL_ON_JOB_CLOSE` on the outer job reaches child jobs in the hierarchy. Jobs
nest only if **neither sets UI limits** — so never call
`SetInformationJobObject` with `JobObjectBasicUIRestrictions`. `[STABLE]`

**libuv puts a permissive job in our chain.** `src/win/process.c:69-106` creates a
global job with `BREAKAWAY_OK | SILENT_BREAKAWAY_OK | DIE_ON_UNHANDLED_EXCEPTION
| KILL_ON_JOB_CLOSE` and assigns every non-detached child to it. Playwright
spawns the browser with `detached: process.platform !== "win32"` — so **not**
detached on Windows, so the browser lands in libuv's job. Containment held
through it, which is the strongest available confirmation: that is exactly the
configuration that would leak if our job permitted breakaway. Firefox stacks a
second such job via its launcher process. `[FLOATS]`

**Neither browser requests breakaway on a browser path.** Chromium: every caller
of `CREATE_BREAKAWAY_FROM_JOB` is installer, updater or remote-desktop code
(`chrome/installer/*`, `chrome/browser/updater/scheduler_impl.cc`,
`remoting/host/win/wts_session_process_delegate.cc`). No renderer, GPU, utility,
network-service or crashpad path. `crashpad_handler` is an ordinary child
(`crashpad_client_win.cc:437-463`). Firefox's launcher uses `CREATE_SUSPENDED |
CREATE_UNICODE_ENVIRONMENT` only. `[FLOATS]`

**Firefox actively checks and declines.** `nsWindowsRestart.cpp`'s
`NeedToBreakAwayFromJob()` returns false unless the job carries **both**
`KILL_ON_JOB_CLOSE` and `BREAKAWAY_OK`. Ours carries only the first. Consequence:
setting `BREAKAWAY_OK` would not merely permit an escape, it would **cause** one.
`[FLOATS]`

**Two implementation mistakes, both proven fatal by measurement:** `[STABLE]`

| Mistake | Measured |
|---|---|
| `Process.Start` then `AssignProcessToJobObject` | **2 escapees** — the child spawns grandchildren before the assign lands |
| Inheritable job handle (`bInheritHandle=TRUE`) | **All children survived** — ours is no longer the last handle, so `KILL_ON_JOB_CLOSE` never fires |

The second is one flag away at all times: redirecting stdio forces
`bInheritHandles=TRUE`.

**`PROC_THREAD_ATTRIBUTE_JOB_LIST` beats `CREATE_SUSPENDED`.** Both measured at 0
escapees, but the attribute makes membership part of process creation, so the
race window does not exist rather than being closed afterwards — and it cannot
leak a suspended process if we die mid-sequence. `.NET` can express neither;
`ProcessStartInfo` has no creation-flags surface. A P/Invoke is mandatory.
Measured with real sandboxed Chromium: 9 processes, 0 escapees. `[STABLE]`

**BrowserAI inside someone else's job works**, measured in all three ancestor
configurations — `KILL_ON_JOB_CLOSE` only, `+ BREAKAWAY_OK`, and
`+ SILENT_BREAKAWAY_OK`. The third is the realistic case: any MCP client that
spawns BrowserAI through Node `child_process` puts us in libuv's job. `[STABLE]`

**Firefox background tasks and the crash reporter fail inside our job** with
`ERROR_ACCESS_DENIED`, because `BackgroundTasksRunner` and
`nsExceptionHandler.cpp` request breakaway. This is the correct trade — a failed
helper launch beats an escaped `firefox.exe --backgroundtask`. Not a bug to fix.
`[FLOATS]`

**Playwright's own force-kill is `taskkill /pid <pid> /T /F`** — by PID with the
tree flag, never by image name. Upstream is clean on that axis.
(`coreBundle.js:9046`) `[FLOATS]`

---

## 2. Browser resurrection after a reboot

Measured 2026-08-15. Harness: `.work/restart-measure/RestartProbe.exe`.

### 2.1 The verdict

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

### 2.2 Margins, per shippable configuration

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

### 2.3 The mechanism, and what is still unproven

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

### 2.4 Fallback profiles do not close the gap

The in-process `base::CommandLine` **is** rewritten when Chrome falls back to a
default profile; the **PEB command line is never rewritten** (the poisoned
`--user-data-dir` survives verbatim at 1803 chars). So a broken Chrome *can*
register where a healthy one would too — but the swap is path-for-path, worth ~12
characters (1722 as-launched vs 1710 rewritten). Against a 699-character overflow
it cannot bridge the gap, and direct measurement agrees: the poisoned Playwright
browser was **not** registered. `[FLOATS]`

### 2.5 Machine state

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

---

## 3. Detection primitives for stray browsers

Measured 2026-08-15.

**Chromium writes its user-data-dir path as the title of a message-only window**
of class `Chrome_MessageWindow`, for its own single-instance logic
(`chrome/browser/process_singleton_win.cc`). `FindWindowExW(HWND_MESSAGE, NULL,
"Chrome_MessageWindow", <title>)` → HWND → `GetWindowThreadProcessId` → PID, in
~60 µs. **The API is structurally incapable of returning a profile you did not
name**, which is what makes it safe. `[FLOATS]`

**Canonicalisation rules, measured exactly:** `[FLOATS]`

| Form | Result |
|---|---|
| Backslashes, absolute, no trailing separator | **HIT** |
| Forward slashes (as passed on the command line) | **MISS** |
| Trailing separator | **MISS** |
| Lower- or UPPER-case | **HIT** — the title compare is case-insensitive |

So BrowserAI must convert to backslashes, absolutise, and strip any trailing
separator. Case need not be normalised. Note the asymmetry: the config passed
forward slashes and the **process command line still carries forward slashes**,
but the window title is backslashes.

**The class alone is ambiguous — the title match is load-bearing.** The same
process also owns a `Chrome_MessageWindow` titled `DeviceMonitorMessageWindow`
plus several empty-titled ones, and the GPU process owns one too. 43 such windows
exist on the maintainer's machine (Discord, Signal, VS Code, 1Password, Teams,
WhatsApp, Steam, ChatGPT, `msedgewebview2`, …), enumerated in 52 ms. `[MACHINE]`

**`GetWindowTextW` works cross-process here** — no need for
`InternalGetWindowText`. But enumerate-and-read-titles does **not** work as a
discovery strategy: reading titles off arbitrary message windows returned empty
in an earlier probe. Look up by exact title; do not enumerate. `[FLOATS]`

**`chrome-headless-shell` has no titled window.** It owns two
`Chrome_MessageWindow` instances, both empty-titled; all probe forms miss. It also
writes no `lockfile`. It is the one binary that can leak but cannot be cheaply
found — which is why BrowserAI ships full Chromium in every mode. `[FLOATS]`

**Lock files differ by browser, and the difference matters:** `[FLOATS]`

- **Chromium** `<dir>\lockfile` — opened `GENERIC_WRITE, FILE_SHARE_READ,
  CREATE_ALWAYS, FILE_FLAG_DELETE_ON_CLOSE`. The kernel deletes it when the
  handle closes, including on crash, so **existence is liveness**. An open for
  write while held returns `ERROR_SHARING_VIOLATION`.
- **Firefox** `<dir>\parent.lock` — `GENERIC_READ | GENERIC_WRITE`, no sharing,
  `CREATE_ALWAYS`, and **never deleted** (the mtime is used to detect startup
  crashes). **Existence proves nothing**; only the sharing violation does.
- Playwright's `isProfileLocked` checks only Chromium's `lockfile`, never
  Firefox's `parent.lock`.

**File → PID** is `RmStartSession` → `RmRegisterResources` → `RmGetList`, which
returns `RM_UNIQUE_PROCESS { dwProcessId, ProcessStartTime }`. The start time is
the PID-reuse guard, re-verified with `GetProcessTimes` before any kill. Mozilla's
`ProfileUnlockerWin::TryToTerminate` does exactly this and is worth copying line
for line. `[STABLE]`

> ⚠️ **The detector is blind to fallback-profile instances, and covering them is
> a trap.** A Chrome that cannot open our profile falls back, and its message
> window is titled with the **fallback** path. With `channel: "chrome"` that path
> is `%LOCALAPPDATA%\Google\Chrome\User Data` — **the user's own browser's
> message window**. A detector extended to match it would identify a personal
> Chrome as a stray. The answer is not a better matcher: **validate the directory
> before launch so the fallback never happens**, and ship bundled Chrome for
> Testing rather than `channel: "chrome"`.

> ⚠️ `--user-data-dir` alone is **not** an ownership signal. Discord, VS Code,
> Signal, Teams, WhatsApp, Steam, ChatGPT and four `msedgewebview2.exe` processes
> all pass it. Only an exact match against a directory BrowserAI created is safe.
> `[MACHINE]`

---

## 4. Profile directories, fallback, and native dialogs

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

### The dialog hazard — worse than "a dialog appears"

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
> behaviour both measured above, and it is a further argument for shipping
> bundled Chrome for Testing.

---

## 5. Automation fingerprinting

Measured 2026-08-15. 65 headless launches across four phases. Harness:
`.work/fingerprint-test/`.

**`--browser-test` is not web-detectable.** **0 deterministic differences** across
486 leaf fields (chrome.exe) / 487 (headless shell), with 8–10 replicates per arm,
interleaved with alternating lead. Four self-controls (each arm split against
itself) also returned 0 with the same noise structure, proving the differ was
sensitive enough to have caught a single changed bit. `[FLOATS]`

Surface covered: full `window` enumeration (1,242 keys), `navigator` prototype
chain and descriptor shapes, `window.chrome` shape, 14 permission states vs
`Notification.permission`, plugin/mimeType arrays, WebGL1+2 (unmasked
vendor/renderer, 67 extensions, 31 params, 12 precision formats), canvas PNG and
pixel hashes, font metrics, OfflineAudioContext hash, WebGPU adapter, media
capabilities, WebRTC candidate types, storage estimate, high-entropy UA hints,
`matchMedia` × 18, Intl/timezone, native `Function.prototype.toString` of 8
patchable natives, `Error.stack` shape, and the `console.debug` / `Error.stack`
CDP timing tricks.

Also measured identical: HTTP headers (30 headers × 13 requests × 5 arms,
including all Client Hints after `Accept-CH`/`Critical-CH`), and reachability of
`chrome://version` from web content (`fetch` → `TypeError`, `window.open` →
`null`, iframe → empty, `pushState` → `SecurityError`), byte-identically in both
arms.

**Two Phase-1 candidates were my own confounds** and vanished in Phase 2: a
6-byte `performance.memory` delta caused by a per-run URL tag of differing length
landing on the JS heap, and a Compute Pressure `fair`/`nominal` split caused by
CPU-heavy benchmarks running inside the observation window. Worth recording
because both looked like real signals.

**The switch is not propagated to renderers.** Absent from
`render_process_host_impl.cc`'s `kSwitchNames[]`, absent from
`runtime_features.cc`'s `switchToFeatureMapping`, absent from
`bad_flags_prompt.cc`'s `kBadFlags` (so no "unsupported flag" infobar). Measured:
it appears on exactly one line of the process tree, and both renderer command
lines are byte-identical between arms. `[FLOATS]`

**Call-site inventory: 11 files**, not the 9 an earlier pass found — the two
missed are Fuchsia-only (`fuchsia_web/webengine/...`), including the only
renderer-side consumer anywhere in Chromium, which is not built for Windows. The
one that deserved the closest look was
`content/browser/in_memory_federated_permission_context.cc` — auto-completing
FedCM requests *is* web-observable — and it is ruled out because Chrome's
`ProfileImpl`/`OffTheRecordProfileImpl` override
`GetFederatedIdentity*PermissionContext()`, so the in-memory context is
content_shell-only. `[FLOATS]`

**The only real behavioural delta is the memory-pressure monitor.** With the
switch, `CreateMemoryPressureMonitor` returns `nullptr`, so the browser never
fires `MemoryPressureListener` → `ChildProcess::OnMemoryPressure` → Blink cache
purge / V8 pressure. That chain fires only under **genuine OS memory pressure**
and its absence is observable only by waiting for a purge that never comes — not
a static fingerprint bit. Compute Pressure does not expose it:
`PressureObserver.knownSources === ['cpu']`, and `observe('memory')` throws.
`[FLOATS]`

**Baseline exposure, identical in both arms — this is the context that makes the
question near-moot:** `[FLOATS]`

- The user agent contains the literal string **`HeadlessChrome`**. That is a
  one-line detection, already present.
- Playwright passes **43 switches** plus `about:blank`. It does **not** pass
  `--enable-automation`. `--disable-blink-features=AutomationControlled` is added
  by the **MCP config layer** (`coreBundle.js:71899`), not by `chromiumSwitches`.
- `navigator.webdriver === false` — `runtime_features.cc:377-379` maps
  `kEnableAutomation`, `kHeadless` **and** `kRemoteDebuggingPipe` to
  `EnableAutomationControlled`, and the MCP-added blink flag cancels it.
- `chrome-headless-shell` is far more exposed than full `chrome.exe`:
  `window.chrome` absent entirely, `plugins.length === 0`, SwiftShader renderer,
  and `Notification.permission === 'denied'` while `permissions.query()` reports
  `'prompt'` — the classic mismatch tell.

**The padding alternative measures identically** — 0 differences across 486
fields. Unknown switches are not in `kSwitchNames` and not in `kBadFlags`, so they
are web-invisible by the same mechanism. `[FLOATS]`

**No evidence any bot-detection vendor or anti-detect project references
`--browser-test`.** Zero hits across GitHub code search (rebrowser-patches,
patchright, puppeteer-extra, undetected-chromedriver, nodriver) and four web
searches spanning DataDome, Castle, CloakBrowser and BotBrowser writeups. **This
is absence of evidence, not evidence of absence** — stated as such. The
literature's switch-detection surface is `--enable-automation`, `--headless` and
`--disable-blink-features=AutomationControlled`, all detectable via their
*effects*, and all already in play here regardless. `[FLOATS]`

**Residual risk measurement cannot rule out:** real OS memory pressure was never
induced (deliberately); headful was not tested (hard constraint, though nothing
renderer-side depends on the switch); one Chromium version; and no real
bot-detection service was exercised (local-only constraint).

---

## 6. Upstream configuration facts

All `[FLOATS]`, all read from the shipped
`playwright-core/lib/coreBundle.js` or the shipped binaries unless noted.

### Silent config failures

**`chromiumSandbox: true` in a config file is discarded.** With it set
explicitly, the browser and every child still ran `--no-sandbox`. Only the CLI
`--sandbox` flag enabled it. `validateBrowserConfig` *intends*
`chromiumSandbox = true` on non-Linux, so this is upstream behaviour
contradicting upstream intent — and it means the default posture is unsandboxed.

**`loadConfig` is a bare `JSON.parse` with no schema validation**, so a renamed
or removed key is silently ignored. `--output-mode` was a no-op for its entire
life.

### Defaults that are not what they look like

**`validateBrowserConfig` defaults to `chromium` *and* sets `channel: "chrome"`**
when no `browserName` is given — i.e. the user's **installed Google Chrome**, not
anything we shipped. Verified empirically: with an *empty* browsers directory,
`initialize`, `tools/list` and `browser_navigate` all succeeded.

**Binary selection** (`getExecutableName`): a channel that is a chromium alias
(`chrome-for-testing`) → `chromium`; any other channel → that channel; otherwise
`headless ? "chromium-headless-shell" : "chromium"`. So **headless does not force
the shell — absence of a channel does**, and `chrome-for-testing` yields the full
binary even headless.

**On Windows `headless` defaults to `false`.** `resolveCLIConfigForMCP` sets it
only to `os.platform() === "linux" && !process.env.DISPLAY`.

**`isolated` is not auto-defaulted on the MCP path.** The auto-default block
(`!options.profile && !options.persistent && !userDataDir && ...`) lives in
`resolveCLIConfigForCLI`, the `playwright` CLI daemon path — not in
`resolveCLIConfigForMCP`. It is also structurally impossible for us:
`validateBrowserConfig` throws on `isolated` + `userDataDir`. Note the legacy
setup set it explicitly in three of its four modes.

**`outputMaxSize` has no default at any merge stage.** `defaultConfig` contains
only `browser: {launchOptions:{}, contextOptions:{}}` and `timeouts: {action:
5e3, navigation: 6e4, expect: 5e3, settle: 500}`; `mergeConfig` filters through
`pickDefined`, which drops `undefined`. When set, `_enforceOutputBudget()` runs on
**every tool response**, recursively lists the whole output directory, and unlinks
oldest-mtime-first past the threshold, sparing only the current response's writes.
Unlink failures go to a debug log. Settable via
`PLAYWRIGHT_MCP_OUTPUT_MAX_SIZE`, so stripping the flag is not enough.

**Inline images are always downscaled.** `scaleImageToFitMessage` shrinks to fit
1568 px and ~1.15 megapixels, unconditionally and with no config. The **file
written to disk is full resolution** — the cap is on the copy entering the
model's context.

**`--console-level` defaults to `info`**, which silently drops `debug` messages.

### Browser provisioning

**Downloads retry 5 times, rotating mirrors** —
`downloadURLs[(attempt - 1) % downloadURLs.length]`. This is why
`PLAYWRIGHT_DOWNLOAD_HOST` must be stripped: it collapses the mirror list to one
host, so all five attempts hit the same dead server.

**`INSTALLATION_COMPLETE` short-circuits without validating anything.** Written
last, so an *interrupted* install self-heals. But a browser corrupted **after** a
successful install never re-downloads — `spawn EFTYPE` forever — and upstream's
remediation string points at `npx @playwright/mcp install-browser chromium`, a
package we do not ship resolving a different revision.

**Integrity is ours to provide.** Playwright validates only `content-length`;
upstream closed and locked the request for checksums
([microsoft/playwright#39559](https://github.com/microsoft/playwright/issues/39559)).

**`winldd` dependency validation is a permanent no-op for Chromium.** Upstream
passes `["chrome-win"]` while Chromium extracts to `chrome-win64`
(`EXECUTABLE_PATHS.chromium["win-x64"] = ["chrome-win64","chrome.exe"]`), so it
checks a directory that does not exist. Same for `chromium-headless-shell` vs
`chrome-headless-shell-win64`. Firefox passes `["firefox"]`, the real directory,
so it **does** run — 39 binaries, +329 ms, cached in `DEPENDENCIES_VALIDATED` with
`kMaximumReValidationPeriod = 30 * 24 * 60 * 60 * 1e3`, i.e. a recurring monthly
cost. If upstream ever fixes the directory name, Chromium starts validating 39
binaries on cold start — a latency regression from a one-character fix.

### Policy

**Chrome for Testing reads policy from
`HKLM|HKCU\SOFTWARE\Policies\Google\Chrome for Testing`** — verified from Unicode
strings in the shipped `chrome.exe`/`chrome.dll`. Not `Policies\Chromium`, not
`Policies\Google\Chrome`. A perfectly isolated namespace: nothing set there can
reach the user's Chrome. Recorded as a reusable lever even though no policy
solves the resurrection problem.

⚠️ **`GetUserDataDirFromRegistryPolicyIfSet` reads
`SOFTWARE\Policies\<brand>\UserDataDir` and *overrides the command line***, in
`chrome_elf` before the browser parses argv. If that key is ever set, per-session
profile isolation collapses silently. Measured absent everywhere on this machine
(all three brands, HKLM and HKCU, including `WOW6432Node`). **Assert at startup
that the resolved user-data-dir is what we passed.** `[MACHINE]` for the absence,
`[FLOATS]` for the mechanism.

### Shutdown

**`setupExitWatchdog`** hooks `stdin` close, `SIGINT` and `SIGTERM`, calls
`gracefullyCloseAll()`, and hard-exits after 15 s
(`setTimeout(() => process.exit(0), 15e3)`). Closing stdin is therefore the
graceful teardown path and needs no killing at all.

---

## Re-verification index

Everything marked `[FLOATS]` is re-checked at upstream review. In priority order —
the first three would each silently invalidate a design decision:

| # | Fact | Breaks if | Check |
|---|---|---|---|
| 1 | Playwright's restart command line overshoots 1023 by 531+ | Playwright trims its arg list | `GetApplicationRestartSettings` on a live browser returns `ERROR_NOT_FOUND` |
| 2 | Job containment holds end to end | Playwright, Chromium or Firefox changes spawn flags | Run `.work/jobtest/` against both browsers |
| 3 | `chromiumSandbox` config key still discarded | Upstream fixes it | Assert `--no-sandbox` absent from the child's browser command line |
| 4 | `Chrome_MessageWindow` title format | Chromium changes `ProcessSingleton` | Exact-title lookup against a launched browser |
| 5 | Chromium/Firefox request no breakaway on browser paths | Either adds one | Source search for `CREATE_BREAKAWAY_FROM_JOB` |
| 6 | `--browser-test` call-site inventory (11 files) | Chromium adds a web-facing site | Source search for `switches::kBrowserTest` |
| 7 | `browserName`/`channel`/binary-selection defaults | `validateBrowserConfig` or `getExecutableName` changes | Config round-trip via `browser_get_config` |
| 8 | `outputMaxSize` has no default | `defaultConfig` gains one | Assert unset in the resolved config |
| 9 | Firefox honours `toolkit.winRegisterApplicationRestart` | Mozilla removes the pref | Source check in `nsAppRunner.cpp` |
| 10 | `winldd` no-op for Chromium | Upstream fixes `chrome-win` → `chrome-win64` | Cold-start latency; source check |

Add a row whenever a new `[FLOATS]` entry lands. An entry with no row is an entry
nobody will re-check.
