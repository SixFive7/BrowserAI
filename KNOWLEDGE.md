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
~60 µs. The exact-title probe cannot return a profile you did not name — but see
[§3.2](#32-enumeration-works--and-it-moves-the-safety-boundary): the
**enumerating** sweep can and does, which moves the safety boundary onto the
ownership test. `[FLOATS]`

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

### 3.1 Cross-process title reads — settled by two independent agents

An earlier probe reported `GetWindowTextW` returning empty for all 43
`Chrome_MessageWindow`s on this machine, and concluded enumerate-and-read does
not work. **That conclusion was wrong and is retracted.** Re-measured 2026-08-15
by two agents working independently, one of them briefed to refute the claim.
Both reached the same result.

**The rule: cross-process, `GetWindowTextW` never sends `WM_GETTEXT`. It reads
the kernel-side window name set at `CreateWindowExW`.** A message is sent only
when the window belongs to the calling process. `[STABLE]`

Proven by discriminator windows whose procedures deliberately lie — measured
independently by both agents, agreeing exactly:

| Window | Same-process read | Cross-process read |
|---|---|---|
| Normal | its name | its name, 0.4 µs |
| WndProc **returns hijacked text** for `WM_GETTEXT` | the hijacked text | **the real kernel name** |
| WndProc **suppresses** `WM_GETTEXT` | `""` | **the real name**, 0.5 µs |
| Kernel name absent, WndProc returns text | the WndProc's text | **`""`** |
| Owning thread **never pumps** | **blocked > 6 s, abandoned** | **the real name, 0.2 µs** |

Each row is only explicable if the cross-process path bypasses the message queue
entirely. Explicit `SendMessageTimeout(WM_GETTEXT)` on the same windows returned
the *hijacked* strings, locating the divergence in the API rather than the
WndProc.

**The 43 empties were genuinely nameless windows, not failed reads.** Confirmed
three ways: `GetWindowTextLengthW` also returns 0, `InternalGetWindowText` also
returns 0, and `MessageWindow::Create()` passes `nullptr` while only
`CreateNamed()` sets a name — so every embedder owns several anonymous windows
plus at most one titled singleton. Machine-wide: **55 `Chrome_MessageWindow`s
across 28 owners, 11 titled, 44 nameless, 11/11 read, zero disagreements between
all four APIs.** Picking one window per process gives an ~80% chance of landing
on a nameless one, which is the likeliest shape of the original error. `[MACHINE]`

**All the plausible failure modes were built and refuted:** `[FLOATS]`

| Hypothesis | Result |
|---|---|
| Busy / non-pumping UI thread | **Refuted.** 0.9–3.8 µs against a thread blocked 15 s inside its WndProc |
| Suspended process | **Refuted.** 2000/2000 reads at 0.28 µs against a suspended real Chromium |
| UIPI / integrity level | **Refuted.** 1271 windows swept from Medium IL: every named one read, including from High-IL owners and from processes whose token *and* process handle could not be opened at all. UIPI filters messages; this is not a message |
| WndProc doesn't answer `WM_GETTEXT` | **Refuted at source, then made irrelevant** — `ProcessLaunchNotification` returns `false` for anything but `WM_COPYDATA`, so `DefWindowProc` answers it. But the cross-process path never asks |

**API comparison, cross-process:** `[FLOATS]`

| API | Works | Cost | Defeatable |
|---|---|---|---|
| **`GetWindowTextW`** | **all 55/55** | **0.1–0.7 µs** | No |
| `GetWindowTextLengthW` | yes, exact length | 0.1–0.8 µs | No — cheapest "is this named at all" filter, skipping 44 of 55 before any allocation |
| `InternalGetWindowText` | identical string, always | 0.7–22 µs | No |
| `SendMessageTimeoutW(WM_GETTEXT)` | **only if the owner cooperates and pumps** | 87–800 µs | **Yes, both ways** — returned `""` against a suppressing WndProc and failed outright after a full 3 s timeout against a non-pumping one. `SMTO_ABORTIFHUNG` did **not** abort early |
| `GetWindowTextA` | yes | 0.5–0.8 µs | No, but mojibakes non-ANSI paths |

`SendMessageTimeout` is the worst available option: it is the only one a stray in
exactly the state we care about — hung, wedged, mid-crash — can defeat.

**Do not take a dependency on `InternalGetWindowText.`** ~1550 window-level
comparisons across every integrity level, a blocked UI thread and a suspended
process produced **zero divergences** from `GetWindowTextW`. It binds fine under
NativeAOT and is declared unguarded in the public SDK, so the usual worry is
unfounded — but an undocumented dependency that buys nothing is a pure loss.
Keep it as a test oracle instead. `[FLOATS]`

> ⚠️ **We are depending on undocumented behaviour of a documented function, and
> it must be pinned by a test.** `GetWindowTextW`'s contract says: *"If the target
> window is owned by another process **and has a caption** … If the window does
> not have a caption, the return value is a null string."* A `Chrome_MessageWindow`
> is created with `dwStyle = 0` and **has no caption**. By the documentation this
> should return empty. It does not. Stable since NT and not plausibly changeable,
> but unverified on any build but Windows 11 26200 — and this is precisely the
> silent-failure class the project exists to eliminate.

### 3.2 Enumeration works — and it moves the safety boundary

**`FindWindowExW(HWND_MESSAGE, prev, "Chrome_MessageWindow", NULL)` walks all 55
windows in 0.43 ms**; the full sweep including a title read per window costs
~2.7 ms. The class name is **mandatory** — a `NULL` class returns 0, as does
`EnumChildWindows(HWND_MESSAGE, …)`, and `EnumWindows` finds 632 top-level windows
with **zero overlap** with the message-only set. `[FLOATS]`

Demonstrated live: one agent's sweep surfaced *the other agent's* browser, in a
directory it had never been told about — the forgotten-directory case the sweep
exists for, observed rather than argued.

> ⚠️ **Correction to an earlier claim in this file.** "The API is structurally
> incapable of returning a profile you did not name" is true of the **exact-title
> probe** and **false of the enumerating sweep**. Enumeration hands back
> strangers' paths — Docker Desktop, Discord, Signal, 1Password, Steam, Teams,
> WhatsApp and ChatGPT all publish real user-data-dirs there. **The ownership test
> is therefore the entire safety boundary**, not a refinement on top of a safe
> primitive.

**And the signal is forgeable.** A plain .NET console app called
`RegisterClassExW("Chrome_MessageWindow")` — window classes are per-process, so it
succeeded — and created a message-only window titled with an arbitrary path. An
external sweep found it by both exact-title lookup and enumeration,
indistinguishable from a real Chromium singleton. `[STABLE]`

**Two guards, both required:** `[FLOATS]`

1. The titled directory contains our `lock.json`, with our schema.
2. The owning process's **full image path** equals the Chrome for Testing binary
   BrowserAI provisioned — `QueryFullProcessImageNameW`, exact path comparison.
   **This is not image-name matching and does not weaken that rule**: matching one
   absolute path to a binary we installed is the opposite of matching `chrome.exe`
   wherever it appears. It also independently catches the personal-Chrome fallback
   hazard below.

**Two hazards specific to enumeration:** `[FLOATS]`

- **The title is an untrusted string on a filesystem path.** Measured
  `File.Exists(<title>\lock.json)`: local existing 0.56 ms, unmapped `Z:\`
  0.01 ms, `\\127.0.0.1\C$\nope` **22 ms**, `\\10.255.255.1\share` **21,037 ms**,
  `\\no-such-host\share` **22,225 ms**. **One UNC title stalls the sweep for 21
  seconds.** Reject anything that is not a rooted local drive-letter path
  *before* touching the filesystem.
- **The walk truncates silently.** If the `prev` handle is destroyed between
  iterations, `FindWindowExW` returns `NULL` with `GetLastError() == 1400`
  (`ERROR_INVALID_WINDOW_HANDLE`) and the walk stops early. Normal exhaustion
  returns `NULL` with error 0, so 1400 is an unambiguous discriminator — check it
  and restart, or the sweep under-reports **exactly when browsers are exiting**.

**`chrome-headless-shell` publishes two unnamed windows and no `lockfile`** — both
primitives blind. Measured via `launchPersistentContext(dir, {headless:true})`,
which spawns it. `@playwright/mcp` 0.0.79 does not take that path: headed by
default, and `--headless --browser chromium` spawns full `chrome.exe` with a
titled window (2.0 µs read, driven over real stdio JSON-RPC). `[FLOATS]`

> **This is recorded as a property of the shell, not as a risk to us.** BrowserAI
> [does not provision it](README.md#settled-2026-08-15) — full Chromium in every
> mode — and `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1` means it cannot appear on disk
> later. So an upstream change to binary selection would produce a **failed
> launch**, which is loud, rather than a silently untrackable browser. It matters
> only if that decision is ever revisited. Note `chromium.executablePath()`
> reports `chrome.exe` for **both** binaries, so it is not a usable indicator of
> which one is running.

### 3.3 Process image path — the fully documented detection path

Measured 2026-08-15 on this machine, `.work/procenum/measure.ps1`. `EnumProcesses`
→ `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` →
`QueryFullProcessImageNameW`, comparing each full image path against a target.
**Every API on this path is documented and supported.** `[MACHINE]` for the
numbers, `[STABLE]` for the APIs.

| | |
|---|---|
| PIDs returned | 611 |
| Opened successfully | 454 |
| `OpenProcess` denied | **156** — protected and SYSTEM-owned processes |
| Image path read | 454 / 454 |
| **Full sweep, median** | **13.88 ms** (min 12.33, max 21.65, 25 runs) |
| Per opened process | 30.6 µs |
| `EnumProcesses` alone | 0.061 ms — negligible; the cost is the per-process open |

**The 156 denials do not matter.** They are protected and SYSTEM processes; a
BrowserAI-launched Chrome for Testing runs as the user, non-elevated, and the
Windows sign-in restore path relaunches as the same user. Nothing we need to see
is in that set.

13.88 ms on a background thread, once per sweep, with the sweep mutex ensuring
one process pays it rather than ninety-six. Roughly 5× the cost of the
window-title walk (0.43 ms to enumerate, ~2.7 ms including title reads).

**What it covers that the title walk cannot: a browser that fell back to a
different profile** (§4). Such a process retitles its message window to the
fallback path, so title-keyed detection loses it — while image-path detection
still sees it, because the binary is unchanged. It cannot safely be *killed*
(it may belong to a live session whose directory was unusable), so it takes the
report-don't-kill path; but knowing beats not knowing.

> **Rejected optimisation, recorded so it is not re-proposed.**
> `NtQuerySystemInformation(SystemProcessInformation)` returns image *names* in a
> single call and could pre-filter before any `OpenProcess`. At 13.88 ms there is
> nothing to buy, and it would put an image-name comparison inside the detection
> path — which is exactly the pattern that erodes into
> [the rule against it](README.md#never-by-image-name) once someone later treats
> the pre-filter as the filter.

**Launch race: 225 ms** from `chrome.exe` start to the titled window existing.
After a job-close kill, `IsWindow` goes false and the walk drops it immediately —
no zombie HWNDs. `IsHungAppWindow` returns **False** for a fully suspended
process, so it is not a usable liveness proxy. `[FLOATS]`

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

**Measured 2026-08-14: the full build refuses a second instance; the shell never
notices one.** Two full `chrome.exe` instances against one profile directory —
the second is refused with **`Browser is already in use for <dir>`**. Two
`chrome-headless-shell` instances against the same directory — **both launched,
both worked, and no error was raised anywhere**, because it writes no `lockfile`
and nothing arbitrates. Two browsers writing one profile's cookie and storage
databases is silent corruption, and headless is the mode upstream defaults to. So
Chromium's own single-instance protection exists **only in the headed build**,
and a directory-keyed lock of our own is the only protection that covers both,
not defence in depth. `[FLOATS]`

**Firefox has no `Chrome_MessageWindow` equivalent**, so its stray detection is a
different path entirely: `parent.lock` sharing violation → Restart Manager
`RmGetList`. `[FLOATS]`

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
life — a hardcoded literal in 0.0.78's bundle, never read from config — and was
then removed outright in 0.0.79, where passing it produces `error: unknown
option` and exit 1. The two failure classes are asymmetric and both are live: a
**CLI flag fails loudly**, a **JSON config key fails silently**.

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

### Environment, merge order and startup output

**The merge order is config file → environment → CLI**, and `@playwright/mcp`
reads **40** `PLAYWRIGHT_MCP_*` variables in its config env mapping — `BROWSER`,
`HEADLESS`, `USER_DATA_DIR`, `EXECUTABLE_PATH`, `OUTPUT_DIR`, `ISOLATED`,
`CONFIG`, `SECRETS_FILE`, `STORAGE_STATE`, `CAPS` and 30 more. **The real total
is 42**: `PLAYWRIGHT_MCP_PING_TIMEOUT_MS` and `PLAYWRIGHT_MCP_EXTENSION_TOKEN`
are read *outside* that mapping. An allowlist test must derive the count from the
resolved bundle and never carry a literal.

**`capabilities` replaces, it does not merge.** `mergeConfig` spreads defined
overrides, so passing `--caps` on the command line **silently wipes** the config
file's capability list — and `PLAYWRIGHT_MCP_CAPS` triggers the identical wipe,
which is an environment route to a bug that a "never pass `--caps`" rule does not
close.

**`PLAYWRIGHT_SKIP_VALIDATE_HOST_REQUIREMENTS` writes a line to stderr when
set** — enough on its own to trip an error-shaped-stderr classifier.

**Playwright's stale-browser GC deletes any registry directory not referenced by
a `.links` entry.** Against a browsers tree we installed, the blast radius is
"deletes our own Chromium", so `PLAYWRIGHT_SKIP_BROWSER_GC=1` is mandatory and
pruning old revisions becomes the caller's job.

**A healthy start prints `Session: <path>` to stderr, every time.** Any
classifier treating stderr output as an error signal fires on every clean launch;
the legacy setup's did.

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

## 7. The tool surface and the package shape

Read from the shipped tree during the 2026-08-13 feasibility research unless a
later date is given. `@playwright/mcp` 0.0.79.

**`@playwright/mcp` is a 20-line shim.** The whole package is `cli.js`,
`index.js` and type definitions. `index.js` in full:

```js
const { tools } = require('playwright-core/lib/coreBundle');
module.exports = { createConnection: tools.createConnection };
```

The implementation is `playwright-core/lib/coreBundle.js` — **3.4 MB**,
esbuild-bundled. `[FLOATS]`

**Three tool counts, and a golden test written against the wrong one fails on
day one.** **78** entries in the internal registry array; **69** the maximum ever
exposed over MCP (9 are `skillOnly` and always stripped); **24** the default with
no `capabilities` set. The founding-bug reproduction saw the 24 over a real
`tools/list`. `[FLOATS]`

**The `storage` capability is 17 tools** — the cookie / localStorage /
`storageState` set. The legacy `interactive` server ran without it, so in that
process they did not exist at all.

> **A per-capability breakdown is not recorded anywhere in this repository.**
> Only the base 24 and `storage`'s 17 were ever written down; `vision`,
> `devtools` and `config` were not counted. `[UNVERIFIED]` — the numbers were
> never observed, not merely lost. Count them from the resolved bundle at the
> next review rather than from memory.

**One node process can serve several configurations.** Verified: two connections
built through the programmatic `createConnection` API produced correctly
divergent surfaces — **42 vs 59 tools** — with no module-global browser state and
browsers created lazily on first tool call. It is reachable only through that
API, which is why the charter rejects it on scope rather than on capability.
`[FLOATS]`

**`playwright-core` whitelists `"./lib/coreBundle"` in its `exports` map**, so
`require('playwright-core/lib/coreBundle')` is a supported import, not a blocked
deep path. It exposes `browserTools` (a flat array of plain, inert objects),
`filteredTools`, `createConnection` and `BrowserBackend`. `defineTool` is the
identity function — no class, no registry, no side effect. No type definitions
and no semver guarantee attach to it. `[FLOATS]`

**The `playwright` package (4.85 MB) is a declared dependency that is never
loaded.** Prunable, but `npm ls` then calls the tree broken. `[FLOATS]`

**`core-install` is declared in `config.d.ts` but no tool carries it** in 0.0.79 —
a dead capability string; setting it does nothing. `[FLOATS]`

**Upstream publishes daily alpha builds of `playwright-core`.**
`@playwright/mcp@latest` is the released dist-tag; the `playwright-core` alpha
beneath it arrives as that package's own **exact** dependency (no `^`, no `~`),
which is what makes the browser revision pinned while the package is not.
`[FLOATS]`

### 7.1 Tools that reach credentials

**`browser_run_code_unsafe` returns an `httpOnly` cookie against the default
surface.** Demonstrated 2026-08-14: with the default **24-tool** surface and zero
`browser_cookie_*` tools exposed, `async (page) => page.context().cookies()`
returned an `httpOnly` bearer token. The tool is in `core`, so **no capability
setting removes it**. It was the *only* hole — `browser_evaluate` →
`document.cookie` returns `""`, and `browser_network_request` strips `Cookie` and
`Set-Cookie`. `[FLOATS]`

**`browser_storage_state` and the cookie tools return `httpOnly` cookies** —
session bearer tokens JavaScript cannot read. Any mode permitted to call them is
credential-bearing. `[FLOATS]`

**`browser_storage_state` never captures IndexedDB.** It calls `storageState()`
with no options, so `{indexedDB: true}` is never passed. A persistent profile
carries IndexedDB, so a "saved" session silently omits it and the tool is
*weaker* than doing nothing. `[FLOATS]`

**`browser_get_config` does not redact.** Its handler is
`JSON.stringify(context.config, null, 2)` with no filtering, so it emits
`config.secrets` in plaintext if that key is ever set. It is not set today.
`[FLOATS]`

**`browser_annotate` opens a dashboard window and blocks until a human finishes
drawing** — and the window appears in headless too. `[FLOATS]`

---

## 8. Payload sizes and first-run provisioning

### 8.1 Component sizes

Measured during the 2026-08-13 research. Every row is a version-specific artifact
size, so every row is `[FLOATS]`.

| Component | Version / revision | Size |
|---|---|---:|
| `node.exe` | v24.19.0 LTS | 88.53 MB |
| `@playwright/mcp` + `playwright-core` tree | 0.0.79 | 18.11 MB |
| `chromium-headless-shell` | rev 1237 | 268.49 MB |
| `ffmpeg` | rev 1011 | 3.35 MB |
| `winldd` | rev 1007 | 0.25 MB |
| full `chromium` | rev 1237 (152.0.7977.8) | 426.88 MB |

**Total payload ~806 MB installed, ~239 MB compressed** — 7z LZMA2 `-mx=5`.
NativeAOT single-file BrowserAI is estimated at ~10–15 MB and the trimmed
self-contained fallback at ~70 MB — both `[UNVERIFIED]`, nothing having been
built.

> ⚠️ **The update budget is written against a different figure: ~380 MB**,
> described in the charter as "dominated by Chromium", with **~600–700 MB**
> transient disk during a swap and a full re-extraction of ~380 MB per update.
> **How that reconciles with ~806 MB installed — and with browsers provisioned on
> first run rather than shipped — is nowhere recorded.** `[UNVERIFIED]`. Settle it
> the next time either figure is re-measured; do not settle it by arithmetic.

**A single `node.exe` drives the full MCP protocol** — no npm, no `node_modules`
belonging to Node, no `.cmd` shims. Verified by execution. Node **v26 is Current
rather than LTS and its `node.exe` is 10 MB larger**. `[FLOATS]`

**The vendored JS tree contains zero native binaries** and is portable as-is.
**`ffmpeg` is required for video capture** — without it the `video` artifact type
throws. `[FLOATS]`

### 8.2 First-run provisioning

**Measured 2026-08-14: 20.3 s end to end on a 300 Mbps link** for chromium +
`ffmpeg` + `winldd`, the chromium download recorded as **202.3 MB**. Stated for
slower links: **4 m 19 s at 10 Mbps, 43 m at 1 Mbps**. `[FLOATS]`

> ⚠️ **The download size is stated four ways across the repository, and the
> charter states it three of those ways.** 202.3 MB in the sentence above; **323
> MB** where `init` is required to check free disk space; **~300 MB** in the
> legacy-setup table; peak disk during provisioning given as **~0.9 GB**.
>
> **`TODO.md` carries the same 2026-08-14 measurement in fuller form** and is the
> only record that reconciles: *"chromium 202.3 MB + shell 119.7 MB + ffmpeg +
> winldd = **323.5 MB down, ~700 MiB on disk**, 20.3 s end to end on a 300 Mbps
> link"*, with the same 4 m 19 s / 43 m arithmetic. So the charter's 202.3 MB is
> **one term of a sum**, and the slow-link figures belong to the total.
>
> **What that means for today's payload is deliberately not decided here.** The
> shell is [no longer provisioned](README.md#settled-2026-08-15), so whether the
> current download is still 323.5 MB, or 202.3 MB plus the small components, is a
> question one run answers and no amount of arithmetic may. Treat the size as
> `[UNVERIFIED]` until then, and re-state it in one place when it is settled.

**In-session recovery is proven.** The same child navigates successfully once the
install lands, with no restart. `[FLOATS]`

**The revision is pinned for free and is never looked up online.**
`playwright-core/browsers.json` carries the revision and `browserVersion`; the URL
is built by substituting that version into a template that **307s** to Google's
bucket. That file is inside the artifact and **no "latest" lookup exists anywhere
in the registry code**, so a release knows forever which browser it wants. Old
builds still resolve back to **Chrome 115 (Jul 2023)** — about three years of
evidence — but **Google documents no retention policy**, so it is evidence and
not a guarantee. `[FLOATS]`

**Egress hosts:** `cdn.playwright.dev`, `storage.googleapis.com`,
`playwright.download.prss.microsoft.com`. `HTTPS_PROXY` / `HTTP_PROXY` /
`NO_PROXY` / `ALL_PROXY` and **`NODE_EXTRA_CA_CERTS`** are honoured on the
download path; **SOCKS is not supported** there. `[FLOATS]`

**`PLAYWRIGHT_BROWSERS_PATH` must be absolute.** A relative value resolves
against `INIT_CWD` — inherited from any npm ancestor — before `cwd`. `[FLOATS]`

**Layout under the browsers root, verified by execution:** `[FLOATS]`

```
<browsers-root>\
  chromium_headless_shell-1237\chrome-headless-shell-win64\chrome-headless-shell.exe
  chromium-1237\chrome-win64\chrome.exe
  ffmpeg-1011\ffmpeg-win64.exe
```

Note the asymmetry: the **outer** directory uses underscores, the **inner** one
dashes, so a path built consistently is wrong. **No sentinel file is needed to
launch** — not `INSTALLATION_COMPLETE`, not `DEPENDENCIES_VALIDATED`; the only
launch-time check is file accessibility of the executable. `.links/` records the
**build machine's** absolute paths and is useless on the target.

**`DEPENDENCIES_VALIDATED` is written into the browsers root on first launch.**
Under `Program Files` that write silently fails and the validation re-runs every
launch. Prefer `%LOCALAPPDATA%` or a `%ProgramData%` path with write ACLs.
`[FLOATS]`

**Node SEA, `pkg` and `nexe` are dead ends.** `playwright-core` violates SEA's
"no filesystem module loading" constraint in **five verified ways**: `packageRoot`
computed from `__dirname`; a runtime `require` of `browsers.json` at a computed
path; two `childProcess.fork()` calls on sibling scripts; sibling bundle
requires; and `.wasm`/`vite` assets loaded by path. SEA would also save nothing —
its output *is* a copy of `node.exe` plus the blob. `vercel/pkg` was archived
**2024-01-13**. Bun and Deno both carry open issues on the Playwright
browser-launch path. `[FLOATS]`

---

## 9. Timings: spawn, resume, idle close, proxy overhead

`[MACHINE]` for every number, `[FLOATS]` for what they are numbers *about*.

> ⚠️ **Only the resume figure carries a date in the charter.** Spawn, navigation,
> idle close and proxy overhead are all recorded undated, so treat their dates as
> `[UNVERIFIED]` and re-stamp each at the next run. The numbers themselves are
> carried forward exactly as written — none has been adjusted.

**Child spawn costs ~300 ms.** That is the baseline a flat 5 s discovery probe
would be paid against ([§10](#10-protocol-sdk-and-client-behaviour)), and the
per-instance price of one node child per handle.

**A real navigation costs 0.43 s** — `browser_navigate` to
`data:text/html,<h1>ok</h1>`, no network and no local server. `about:blank`
succeeds too trivially and its snapshot is empty, which is why the smoke
assertion uses a `data:` URL.

**Browser-idle close recovers 329 MB → 110 MB, and relaunch costs 186 ms.**
Closing the browser while keeping the node child is therefore cheap enough that a
caller navigating afterwards need never see "browser is closed".

**Resume costs 515 ms and loses only `sessionStorage`.** Measured 2026-08-14:
after killing the node child, a resume against the recorded directory preserved
cookies, localStorage, IndexedDB, service workers and CacheStorage. This is the
measurement the no-expiry-timer decision rests on — the durable thing is the
profile, not the process.

**Proxying costs ~50 ms on a 500 KB payload.** From an equivalent Node prototype:
images passed through byte-identical (**509,620** base64 bytes), error shapes
preserved, ~50 ms added latency, ~300 ms one-off child spawn. It measured a
**Node** prototype rather than the C# proxy, so it is `[UNVERIFIED]` as a
prediction of BrowserAI's own overhead — a precedent, not a measurement of this
product.

**Suite costs, for cadence decisions:** real-child contract 2–5 s, smoke 10–30 s,
update 1–3 min. Estimates, not stopwatch figures. `[UNVERIFIED]`

---

## 10. Protocol, SDK and client behaviour

### 10.1 The protocol split

**`@playwright/mcp` 0.0.79 caps at protocol `2025-11-25`.** The child never
*rejects* a version — it caps or echoes silently: verified, offering
`1999-01-01` returned `2025-11-25` with no error, so a mis-negotiation produces
nothing to catch and the negotiated value must be asserted. `[FLOATS]`

**The current spec is `2026-07-28`, a breaking rewrite.** It removes `initialize`
and `notifications/initialized`, adds `server/discover`, replaces server→client
requests with the MRTR retry pattern, and deprecates Roots, Sampling and Logging.
**SEP-2567 removed protocol-level sessions outright**, and *Tools § Capabilities*
states the tool set "MAY change over time … but MUST NOT vary per-connection or
as a side effect of other requests on the connection." `ping` was removed at
`2026-07-28`. SEP-2567 also names `destroy_*` and `list_*` as the documented
companions to a creation tool. `[STABLE]` — a published revision does not move.

**The .NET SDK implements every revision from `2024-11-05` through `2026-07-28`**
and shipped `2026-07-28` support on the spec's release date. `[FLOATS]`

**`DiscoverProbeTimeout` is 5 seconds by default.** With the client version left
unpinned, the client probes the child with `server/discover` first; if the child
drops the unknown method rather than answering, **every child spawn costs a flat
5 s against a ~300 ms baseline**, presenting as "browser automation got slow"
with no error anywhere. The SDK's own test base class pins it explicitly, citing
[csharp-sdk#1701](https://github.com/modelcontextprotocol/csharp-sdk/issues/1701)
— CI slowness tripped the probe there. `[FLOATS]`

### 10.2 SDK behaviours a proxy must work around

All read from the shipped `ModelContextProtocol` package. `[FLOATS]`

**`StdioClientTransport` prepends `cmd.exe /c` to every non-cmd command on
Windows, unconditionally.** That inserts a shell layer and an extra process
between BrowserAI and `node`, plus cmd.exe quoting semantics. `IClientTransport`
is two members (`Name`, `ConnectAsync`); the replacement is ~120 lines.

**`ListToolsAsync(RequestOptions?, ct)` silently drops any tool whose
`x-mcp-header` annotations fail SEP-2243 validation.** The raw
`ListToolsAsync(ListToolsRequestParams, ct)` overload returns the server's result
unfiltered. Using the wrong one shrinks the exposed surface with no error.

**The `ContentBlock` converter silently drops unknown properties** — the SDK has
tests asserting exactly that, which is correct forward-compatibility for a client
and data loss for a proxy — **and throws on unknown content *types***, failing
the whole call at deserialization before any proxy code runs. `WithMessageFilters`
operates on `JsonRpcMessage`, where `JsonRpcResponse.Result` is a raw
`JsonNode?`.

### 10.3 Measured by spike, 2026-08-15

A throwaway proxy was built on `ModelContextProtocol` 2.2.0 and driven against
both a scriptable fake child and the real `@playwright/mcp` 0.0.79, on CoreCLR
and as a published NativeAOT binary. Everything here is observed. `[FLOATS]`

**NativeAOT works, with nothing of ours required.** `PublishAot=true`, win-x64,
self-contained: **zero trim/AOT warnings, no `JsonSerializerContext` of our own,
10,233,856 bytes (9.76 MiB)**. The published binary drove a real child: 24 tools
through the proxy, `handle` injected into every schema, `browser_navigate` to a
`data:` URL returning a non-error result, recorded node PID gone after dispose.
`IsReflectionEnabledByDefault=false` confirmed at runtime. Even
`CallToolAsync(name, Dictionary<string,object?>)` worked. One AOT trap, in *our*
code rather than the SDK: `JsonArray.Add(x)` binds to the generic overload, which
is `RequiresDynamicCode` + `RequiresUnreferencedCode`; casting to the `JsonNode`
overload clears both.

**Passthrough is semantically lossless, not byte-identical, through the SDK's own
server transport.** Unknown content `type`, unknown properties on a known block,
unknown top-level result members, base64 image with `annotations`, and a
1,000,039-byte payload all survived byte-identically with key order preserved and
numeric forms unchanged. The one mutation is **string escaping**:
`McpJsonUtilities.JsonContext` sets no `Encoder`, so `JavaScriptEncoder.Default`
re-escapes on the way out. Backticks, apostrophes, angle brackets and every
non-ASCII character become escape sequences — measured on real
`browser_navigate` output, and a unicode case grew **154 to 218 bytes**.
`StreamServerTransport.cs:75` hard-codes the context with no options seam, so
byte-identity is unobtainable without our own server-side `ITransport`.

**`WithMessageFilters` is a hosting-package DI extension, not Core.** The Core
equivalent, and what an AOT proxy wants, is
`McpServerOptions.Filters.Message.IncomingFilters` / `OutgoingFilters`
(`IList<McpMessageFilter>`). `JsonRpcResponse.Result` and `JsonRpcRequest.Params`
are both `JsonNode?`, asserted by reflection at runtime. An incoming filter that
never calls `next` and answers via `ctx.Server.SendMessageAsync` short-circuits
cleanly: typed handlers that throw were never reached.

**Typed `ListToolsResult` discards unknown tool-level members.** `Tool` has no
`[JsonExtensionData]`. `inputSchema` keywords survive because it is a
`JsonElement`; a top-level `x-tool-extension` does not. On `JsonNode`, injecting
`handle` into `properties` + `required` preserved `$schema`, nested vendor
keywords, property-level hints and the top-level extension, with ordering stable
across repeated calls.

**The convenience-overload trap, reproduced exactly.** Same client, same child:
`ListToolsAsync(new ListToolsRequestParams(), ct)` returned 5 tools;
`ListToolsAsync(cancellationToken: ct)` returned 4, silently. Two details the
charter omitted: the drop **is** logged at `Warning` (`Tool '{ToolName}' excluded
from tools/list: {Reason}`), visible only if an `ILoggerFactory` is supplied, and
the `ToolRejected` hook is `internal` with no public event. `AddKnownTools`
**throws** on the same input rather than dropping.

**`cmd.exe` wrapping is worse than "an extra process".** Verified via a node
probe reporting `process.ppid`. Two argument-fidelity failures beyond the shell
layer: `%USERNAME%-literal` reached node as the expanded value, and an argument
containing whitespace **and** `&` caused the child to fail to start entirely
(exit 1, `'C:/Program' is not recognized`), because `EscapeArgumentString` skips
caret-escaping for whitespace-bearing arguments and cmd then splits the command
path — which contains a space in the stock Node install location. Metacharacters
alone round-trip fine. The replacement is **164 lines / 136 non-blank**, without
the stderr ring buffer or `ILogger` plumbing the SDK version carries:
`IClientTransport` is two members, but its session classes are `internal`, so the
`ITransport` half must be written against public `TransportBase`.

**Cancellation is never relayed.** A caller's `notifications/cancelled` cancels
the proxy's handler token in ~2 ms and `SendRequestAsync` throws, but **nothing
is emitted downstream**. Isolated away from the proxy: a plain `McpClient` over a
plain transport, cancelling both raw and typed call paths, produced nothing the
child could see within 6 s, on CoreCLR and AOT alike. `McpSessionHandler` has the
machinery (`RegisterCancellation`), but its registration is disposed as
`tcs.Task.WaitAsync(ct)` unwinds; CTS callbacks run LIFO, so `WaitAsync`'s
callback wins. **Remedy proven in the same run:** assign `JsonRpcRequest.Id`
yourself (it reaches the wire verbatim) and send the notification from your own
`ct.Register`.

**JSON-RPC errors are lossy above the transport.** `code` and `data` survive; the
message is prefixed by `CreateRemoteProtocolExceptionFromError`, so
`"upstream exploded"` arrives as `"Request failed (remote): upstream exploded"`,
and `data` is destructured into `Exception.Data`. A child dying mid-call surfaces
as `-32603`, an error rather than a hang — but only once its stdout reaches EOF.

**`McpClientOptions` has no `Filters`.** All filter APIs are server-side, so
wildcard observation of child-to-proxy traffic needs an `ITransport` decorator
(~30 lines). With one, an unknown notification reached the caller with
byte-identical `params`.

**`RequestHandlers` contradicts its own documentation.**
`McpServerOptions.RequestHandlers` (`[Experimental("MCPEXP002")]`) is documented
as taking precedence over built-in handlers. It does not:
`ConfigureCustomRequestHandlers` runs last and **throws
`InvalidOperationException`** for a method already handled. Using it for
`tools/list` requires leaving `Capabilities.Tools` unset, which then requires
re-injecting `capabilities.tools` into the `initialize` result from an outgoing
filter. The `IncomingFilters` short-circuit needs no such surgery.

**An unanswered `server/discover` costs the full `DiscoverProbeTimeout` per
connect** — 30 s per rig against a fake child until it returned `-32601`. Real
`@playwright/mcp` 0.0.79 handles it, so this bites our own test doubles rather
than production.

**Not tested, not claimed:** HTTP transports, resumption, pagination cursors on
the real child, `structuredContent` on a real tool, stderr back-pressure under
load, ordering of concurrent in-flight `tools/call`s.

**The typed client flattens JSON-RPC `error.data` to primitives**, losing nested
error structures. Protocol errors only — tool failures travel as `isError: true`
data.

**`McpServerToolCreateOptions` has `OutputSchema` but no `InputSchema`**, so the
obvious factory API always reflects the schema from the .NET signature — unusable
for a proxy, and the first one reached for.

**Roughly half of §E's observability is already in the SDK:**
`StandardErrorLines` wired before `Start()`, a rolling stderr tail, and a
`StdioClientCompletionDetails { ProcessId, ExitCode, StandardErrorTail }` type.
The SDK also carries a `beforeDispose` callback commented *"to read ExitCode
before Dispose() invalidates it"* — upstream hit
[§11](#11-runtime-toolchain-and-windows-primitives)'s `ExitCode` trap too.

**`IsAotCompatible=true` is declared by both Velopack (net8.0+) and
`ModelContextProtocol`** — verified in-source **2026-08-14**, set on every target
except `netstandard2.0`, at both `v1.4.1` and `v2.2.0`. **A declaration is the
author's claim about their code, not a proof for our usage.**

**The SDK's test fixtures are 1,082 lines** (`ClientServerTestBase` +
`tests/Common/Utils/*`), Apache-2.0, **unpublished to NuGet**, and they wire a
single client↔server pipe pair where a proxy needs two hops. `NodeHelpers.cs` is
a further 577 lines of `npm install` machinery for the conformance suite.
**Disposal order in that harness is load-bearing:** cancel the token → complete
*both* pipe writers → await the server task → dispose the provider; any other
order hangs or throws.

### 10.3 Package provenance, as looked up

**`ModelContextProtocol` 2.2.0 was latest as of 2026-08-13**, Apache-2.0, 23.6M
downloads, the **Tier 1** SDK under the MCP project — which Anthropic donated to
the Linux Foundation's Agentic AI Foundation on **2025-12-09**. It began as
`PederHP/mcpdotnet`, now archived. The main package's hosting dependency is
abstractions-only and does **not** drag in ASP.NET; `ModelContextProtocol.Core`
alone is a viable smaller surface (`McpServer.Create` + `StdioServerTransport`,
and the `[McpServerTool]` attributes already live there). Verified 2026-08-14.
`[FLOATS]`

**A correctly stamped version comment went stale in three weeks.**
`SixFive7/OutlookAI` pins `ModelContextProtocol` 1.4.1 with a csproj comment
reading *"1.4.1 = latest stable on nuget.org as of 2026-07-23 (2.0.0 is still
preview)."* Re-checked against nuget.org's flat-container index on **2026-08-14**:
2.0.0, 2.1.0 and 2.2.0 have all shipped stable, so the comment's central claim is
now false and nothing in that build says so. **The date stamp is the only reason
the staleness is detectable at all.** `[FLOATS]`

**Other versions looked up, with their stamps:** Velopack and `vpk` **1.2.0**
(MIT); TUnit **1.65.0** as of 2026-08-13 (MIT, source-generated, reflection-free,
MTP-native; 1.0 shipped 2025-11-05; ~623K downloads/mo, growing 2.24× YoY);
`Verify.TUnit` **31.28.0** as of 2026-07-31, same monorepo and release as
`Verify.XunitV3`, with *more* test projects covering the TUnit integration;
`@modelcontextprotocol/inspector` **2.2.0**. **FluentAssertions relicensed at
exactly 8.0.0** to a bespoke non-SPDX licence with a commercial tier. TUnit is
**MTP-only and conflicts with `Microsoft.NET.Test.Sdk`**; Coverlet does not work
under MTP (`Microsoft.Testing.Extensions.CodeCoverage` instead). `[FLOATS]`

### 10.4 The client: Claude Code

`[FLOATS]` on a client version this project does not control.

**Tool names and server `instructions` load eagerly; schemas are deferred.** So
`instructions` is the only channel that reaches the model before it calls
anything.

**Server `instructions` and every tool description are truncated silently at
2 KB.** The tail simply does not exist and nothing reports it.

**`notifications/tools/list_changed` handling changed, and the charter's citation
is stale.** *"Claude Code registers no handler"* was accurate at **2.0.65**
(Dec 2025) — issues
[#13646](https://github.com/anthropics/claude-code/issues/13646) and
[#4118](https://github.com/anthropics/claude-code/issues/4118). At **2.1.231 it
is false**: measured twice, the client re-listed in **1–2 ms** and the model
called a tool that appeared only in the second list. This does **not** unlock a
per-connection tool list — SEP-2567 stands — but the cited issues need re-dating.

### 10.5 Token cost of the tool surface

Measured **2026-08-13** with `tiktoken` `cl100k_base` against live `tools/list`
payloads from `@playwright/mcp` 0.0.79. `[FLOATS]`

| | Eager clients | Claude Code, deferred loading |
|---|---:|---:|
| Four servers as registered today | ~23,000 tok | ~985 tok |
| One perfectly-curated proxy | ~11,600 tok | ~330 tok |

**The entire achievable saving under deferred loading is ~650 tokens**, about
0.3% of a 200k window. Without deferred loading a consolidated surface saves
~65%. Recorded because the charter's *Non-reasons* section rests on it: this is
not why the project exists.

### 10.6 Tooling around the protocol

**`claude mcp list` and `claude mcp get` exit 0 even when the server is dead** —
unusable as a CI gate without grepping stdout for `✘`. **The official MCP
conformance suite is HTTP-only** (`--url`), so it needs a test-only listener or a
small bridge to reach a stdio server. **The Inspector CLI cannot spawn `.cmd`
shims on Windows** — same root cause as
[#58510](https://github.com/anthropics/claude-code/issues/58510) — so address
`cli.js` by absolute path; its **exit code 5 means the tool reported `isError`**,
which is the signal `claude mcp` does not give you. `[FLOATS]`

---

## 11. Runtime, toolchain and Windows primitives

### 11.1 stdio, exit codes and process startup

**`Console` stdio is wrong by default in both directions.** Measured:
`Console.Out` writes **CP437**, not UTF-8 (`é` → `0x82`); `Console.InputEncoding`
also defaults to CP437; **any** `TextWriter` emits CRLF; and a hand-rolled
`new StreamWriter(stream, Encoding.UTF8)` emits a **BOM**. On a JSON-RPC channel
each of the three corrupts the stream on first contact. `[STABLE]`

> **The charter does not date this measurement.** The date is `[UNVERIFIED]`; the
> observations are carried forward as written.

**`Process.ExitCode` throws after `Dispose()`, and
`Process.GetProcessById(pid).ExitCode` always throws.** .NET is *worse* here than
PowerShell, which merely returns `$null`. Cache the value as an `int` the moment
the child exits. `[STABLE]`

**`WaitForExit(int)` does not drain the async readers** — only `WaitForExit()`
and `WaitForExitAsync(ct)` do, so the timeout overload truncates stderr.
`[STABLE]`

**stderr survives the child.** The anonymous pipe exists before `CreateProcess`
and the kernel buffers it: **5 lines survived a 3 s delay *and* child exit**. The
real risk runs the other way — a full pipe blocks the child. `[STABLE]` for the
mechanism; **the charter does not date the measurement**, so the date is
`[UNVERIFIED]`.

**stdin EOF fires instantly when the parent holding the pipe is
`TerminateProcess`d**, which is what makes EOF a usable backstop for reaping
instances. Measured; **undated in the charter**. `[STABLE]`

**`ProcessStartInfo.Environment` is pre-populated with the inherited block and
assignment *merges*** — an allowlist requires `Clear()` first. **`WorkingDirectory`
left unset passes `null` to `CreateProcess`**, so the child inherits the parent's
cwd, whatever the MCP client happened to have. **`ArgumentList` and `Arguments`
are mutually exclusive**; setting both is undefined behaviour. `[STABLE]`

**Node's `child_process` has no job object support at all**, and Node's `spawn`
cannot execute `.cmd` shims without `shell: true` — a live Claude Code bug for
plugin-shipped servers using bare `npx`
([#58510](https://github.com/anthropics/claude-code/issues/58510)). Every Node
process supervisor on Windows falls back to `taskkill /T /F` or a native addon,
and none survives a hard kill of the supervisor. `[FLOATS]`

### 11.2 Windows object names and window scoping

**You cannot put a path in a mutex name.** Backslashes are illegal after the
`Global\` prefix — `"Global\C:\Source\..."` throws `DirectoryNotFoundException`,
so a path-keyed lock must canonicalise and hash. The real length limit is
**~32,000 characters, not the documented 260**, but hashing is required
regardless. `Global\` additionally needs `SeCreateGlobalPrivilege`, which
interactive users have and low-integrity / AppContainer processes do not.
`[STABLE]`

**`FindWindowExW(HWND_MESSAGE, …)` is scoped to a window station and desktop.** A
scheduled task configured *"run whether user is logged on or not"* lands in
session 0 and **sees no message windows at all** — it would sweep, find nothing,
and report success forever. Any sweeper must run in the user's interactive
session. `[STABLE]`

**Windows will not rename a directory holding open executables**, and a live
browser holds `chrome.exe`. Download-alongside-and-swap is therefore not
available for a browser reinstall. `[STABLE]`

### 11.3 Interop and the toolchain

**`NtQueryInformationProcess` reads a parent PID in ~0.77 µs/call**, against
~3.3 ms for `Process.GetProcessById` and milliseconds for WMI. `dotnet/runtime`
itself uses it. `[MACHINE]` for the numbers, `[STABLE]` for the API.

**`LibraryImport` does not support `StringBuilder`**, so a `CreateProcessW`
command line must be passed as a writable `char[]`/`Span<char>` — the API mutates
the buffer, and a `string` literal is not valid. `DllImport` is the wrong choice
under NativeAOT because it relies on runtime IL-stub generation. `[STABLE]`

**No credible NuGet job-object wrapper exists** — the candidates have <6K
downloads and the newest was published in **2017**. `dotnet/runtime`
[#126273](https://github.com/dotnet/runtime/issues/126273) proposed built-in
support and was closed as not planned. The hand-rolled surface is ~60 lines.
`[FLOATS]`

**Floating NuGet is two restore steps, not one.** `dotnet restore
--force-evaluate` resolves the float; a second, locked-mode restore verifies it.
They are mutually exclusive in one invocation: **with a lock file present and no
`--force-evaluate`, NuGet does not re-resolve and the float is silently dead**
([NU1512](https://learn.microsoft.com/nuget/reference/errors-and-warnings/nu1512),
warned by default from the .NET 11 SDK). `git diff --exit-code --
"**/packages.lock.json"` after the resolve is then the cheapest available drift
detector. `[FLOATS]`

---

## 12. Artifacts and output-directory behaviour

All read from the shipped bundle or observed against a real child. `[FLOATS]`

**Playwright writes every artifact flat into one directory with a generated
name**, mixing machine churn with hand-named work. **Nine fixed generator
prefixes** make classification exact rather than heuristic: `console`,
`download`, `network`, `page`, `request`, `response`, `result`, `storage-state`,
`video`.

**The generated name format is `page-2026-08-14T04-11-50-882Z.png`** — a
timestamp, which is precisely what made 346 accumulated session directories
untriageable.

**A relative `filename` resolves against the child's cwd.** That is the whole
reason ten repositories currently run a `deny` hook on `browser_take_screenshot`,
and it is closed by setting the child's `WorkingDirectory` instead of by a hook.

**`_meta.json`, `_meta.cwd` and `_meta.raw` are read by the child before zod
parsing** and stripped before the tool sees them. Undocumented but real, and
available for a proxy to inject (JSON error format, relative-path base).

**Killed children leak `browser@<guid>` descriptors.** Each is a JSON file in the
browsers-registry root holding the absolute `userDataDir` and `workspaceDir`;
`BrowserServer.stop()` removes them only when there is **no** `userDataDir`. **28
were observed and removed on 2026-08-14** (`[MACHINE]` for the count). The
registry root sits inside the Velopack payload under the current design — a tree
that should be read-only and is wiped on update.

**Real screenshots are not byte-stable across runs**, so passthrough-fidelity
assertions need a canned blob from a fake child rather than a live capture.

---

## 13. Velopack and the update path

Read from Velopack **1.2.0** and its Rust binaries unless noted. `[FLOATS]` —
this is a floating dependency like any other.

**Per-user install to `%LocalAppData%`, no elevation, MIT, no commercial tier.**
The same 1.2.0 release ships `lib-nodejs`, `lib-rust` and `lib-python`, and the
Rust `Update.exe` doing the real work is identical for all of them — so the
update story does not by itself require C#. `--msi PerMachine` installs to
`Program Files` and makes the updater self-elevate, which a background stdio
server cannot answer.

**The delta scheme is per-file zstd `--patch-from`, and unchanged files collapse
to zero-byte markers.** `current\` is a real directory, not a junction, so the
executable path is stable across updates.

**`SimpleWebSource` composes the feed request as
`{BaseUrl}/releases.{channel}.json`.** A base URL built as `{BaseUrl}/{channel}`
therefore fetches `{BaseUrl}/{channel}/releases.{channel}.json` — a 404, surfaced
as *"no update available"* and nothing else. The channel belongs in
`UpdateOptions.ExplicitChannel`. A local-directory source composes paths
differently and passes where production 404s.

**`SetAutoApplyOnStartup` defaults to `true`.** On finding a staged package,
`VelopackApp.Run()` applies it, `exit(0)`s and relaunches — **with no inherited
stdio**, so an MCP client sees its server exit at handshake time.

**The execution stub is compiled `#![windows_subsystem = "windows"]` and returns
immediately** without waiting, so a stdio client registered against the stub sees
the child die instantly with no pipes attached.

**`force_stop_package` kills every process under the install root** without
asking.

**Constructing an `UpdateManager` merely to read the installed version issues a
network request.** `VelopackLocator` reads local metadata only.

**`NotInstalledException` is the normal outcome under `dotnet run` and every test
host** — neither is a Velopack install, so every Velopack call throws.
`Debugger.IsAttached` does not detect a test runner.

**`ApplyUpdatesAndRestart(null)` restarts without a package by undocumented
fall-through** — the internals skip the `--package` argument when there is no
local full package. `UpdateExe.Start(waitPid)` is the supported restart.

**`IVelopackLogger` takes two separate registrations** — the runtime
`UpdateManager` and the `VelopackApp.Build()` startup hooks. Bridging only the
first leaves the installer, first-run and post-restart hooks silent.

**Velopack's Rust `Setup.exe`/`Update.exe` carry their own Windows floor,
separate from .NET's**, and can fail *before* the managed app exists: before
**0.0.530** they statically linked `IsWow64Process2` and crashed below Windows 10
1709. `--runtime win7` does not help if the installer binary cannot run.

**Velopack prunes `packages\` down to the current full `.nupkg` and deltas are
forward-only**, so every rollback is a fresh full download (~105 MB here) unless
packages are archived by hand. `AllowVersionDowngrade` is the client half of
rollback.

**`vpk` rejects 4-part version numbers** — semver2, three parts only.

**MSIX is disqualified on evidence.** A package cannot re-register while any
process in its family is running: claude-code
[#63397](https://github.com/anthropics/claude-code/issues/63397) (`0x80073D02` /
`ERROR_SHARING_VIOLATION`, the report naming "Claude Code runs as a child process
of Claude Desktop") and openai/codex
[#25770](https://github.com/openai/codex/issues/25770), both in 2026. Hydraulic
Conveyor emits MSIX on Windows and inherits the same failure.

**Every unsigned `Setup.exe` is a new file to SmartScreen.** Azure Artifact
Signing at roughly **$10/mo** buys instant reputation. `[UNVERIFIED]` price — a
list figure, not a quote obtained.

### 13.1 Prior art: ExoFabric/UCC

In-house evidence, not upstream behaviour. `[MACHINE]` — true of one repository
at one point in time.

**UCC runs Velopack 0.0.1298, not 1.2.0** — the pre-1.0 line, with both behaviour
and API surface since moved. It ships per-user to `%LocalAppData%\UCC\current\`,
no elevation, S3-compatible feed, silent background check, in production across
multiple releases.

**Five of the nine landmines above are ones UCC hit rather than avoided**, and
none announced itself:

- **Feed URL composition** bricked auto-update for **three shipped versions**;
  manual reinstall was the only recovery.
- **`SetAutoApplyOnStartup` is never called**, so the default is live in a
  shipping app — survivable for a foreground tray app, fatal for a stdio child.
- **Logs are written to `AppContext.BaseDirectory`** — inside `current\` — with a
  10-day retention policy that every update resets.
- **Delta packages have never been produced.** Every shipped artifact is a full
  `.nupkg`; delta validation is still an open TODO. Delta granularity is the
  stated reason Velopack was chosen at all, so it is unproven in-house.
- **Rollback has no code and no documentation.** The client would accept one; the
  version-validation script refuses to emit one.

**What UCC does prove:** the per-user `current\`-swap layout works in production;
a test seam of `virtual` network methods carries **48 hermetic update tests**; and
its restart choreography — cooperative shutdown with per-component acks, a 10 s
hard-kill backstop, log flush, *then* apply — is worth copying wholesale.

**Coverage of the update wrapper itself is zero tests**, which is exactly where
the feed-URL bug lived. **UCC is single-instance** via a named mutex, so
`force_stop_package` is harmless there — meaning the landmine that matters most
for concurrent registrations is **untested by the only prior art available**. No
signing: no certificate, no `--signParams`, package signature verification
unexplored.

---

## 14. Third-party payload, as shipped

Verified **2026-08-14** against the versions in the payload table
([§8.1](#81-component-sizes)), by reading the shipped trees and binaries.
`[FLOATS]` — every row moves when its component does.

| Component | Terms as shipped | What is in the tree |
|---|---|---|
| `@playwright/mcp`, `playwright-core` 0.0.79 | Apache-2.0 | The vendored `node_modules` tree carries the package `LICENSE`. **No `NOTICE` file is published upstream**, so §4(d) has nothing to propagate |
| `ModelContextProtocol` 2.2.0 | Apache-2.0 | Mid-transition from MIT; unrelicensed contributions remain MIT |
| Velopack 1.2.0 | MIT | Notice only |
| Node.js v24 | MIT **plus aggregate terms** for OpenSSL, ICU, V8, zlib and c-ares | Shipping "a single `node.exe`, nothing else" drops Node's `LICENSE`, which is not optional |
| `chromium-headless-shell` 1237 | BSD-3-Clause | `LICENSE.headless_shell` plus a **40,178-line** credits file. Binary is unbranded |
| `ffmpeg` 1011 | LGPL-2.1 | `COPYING.LGPLv2.1` already ships in the directory. Spawned by `playwright-core` as an unmodified separate executable, so §6's relink requirement does not bite |
| `winldd` 1007 | **no license file shipped at all** | Nothing in the tree to ship |
| full `chromium` 1237 | **Google-branded, no OSS license file anywhere in the tree** | `chrome.exe` reports CompanyName **"Google LLC"** and **"Copyright 2026 Google LLC. All rights reserved."**; its `ABOUT` points at Google's Chrome Terms of Service |

**The only on-point public statement on redistributing Chrome for Testing is
adverse.** A Google engineer, 2023: *"Chrome for Testing is a flavor of Google
Chrome, so google.com/chrome/terms applies"* — which forbids redistribution. This
is a citation, not a measurement, and it is not legal advice; it is recorded
because it is the single piece of evidence the provisioning decision rests on.

---

## 15. The legacy setup and this machine

Everything here is `[MACHINE]`. It is motivation for the project, and none of it
generalises. It is recorded because the charter's opening argument cites these
numbers and they carry no other provenance.

**13 copies of `playwright/launch.ps1` across 10 repositories.** Filesystem sweep
of `C:\Source` to depth 7, **2026-08-13**: `ExoFabric/Infrastructure`,
`Netwerkplek`, `FluxTone`, `HitsterCardGenerator`, `ImmichDater`, `Jeeves`,
`PortainerCompose`, `StationeersPlus`, `SyncthingMonitor`, `Workspace657` — plus
3 worktree/backup copies inside `StationeersPlus`. **All nine non-Workspace657
copies are byte-identical to each other and all differ from Workspace657**, and
the same holds for `.claude/hooks/playwright-config-hook.ps1`. If the true count
is 15+, the remainder live outside `C:\Source` or deeper than 7 levels.

**Thirteen checkouts means thirteen `persistent/profile/` directories**, so a
login established in one repository does nothing for the other twelve.

**A stderr-pipe inheritance bug cost 11.71 s per spawn; the fix took it to
0.37 s.** `Start-Process` redirection does not prevent stderr-pipe inheritance, so
a client reading stderr blocked for the entire browser download. Diagnosed and
fixed 2026-08-12/13, along with everything else in the charter's opening table.

**A hard startup failure logged identically to a clean shutdown for five days.**
The process handle was not cached before `WaitForExit`, so `.ExitCode` read back
`$null`. That is why a deleted CLI flag — `--output-mode`, removed in
`@playwright/mcp` 0.0.79, producing `error: unknown option` and exit 1 with **all
four servers dead** — went unnoticed.

**A healthy start prints `Session: <path>` to stderr every time**, which is why
warning on *any* stderr output was the wrong classifier. `[FLOATS]` — this one is
upstream behaviour rather than machine state.

**One flat `output/` grew to 346 session directories and 1.5 GB in ~3 months**,
and nobody pruned it because nobody could tell what any of the directories had
been.

**Mutexes were named `Global\<RepoName>-PlaywrightInteractive`** — keyed on a
repository folder name rather than on the profile directory that actually
requires exclusivity. All four `config.json` files used paths relative to the
working directory, including `userDataDir: "playwright/persistent/profile"`, with
cwd guaranteed only by `Set-Location $RepoRoot`.

**Our own Chromium probes counted and killed by image name.** Harmless for
Chromium on this machine at that moment; adapted naively to Firefox it would have
killed **~40 personal `firefox.exe` processes**. This is the measurement behind
the structural never-by-image-name rule.

**A `deny` hook keyed on `browser_take_screenshot` exists in ten repositories**,
which is what a tool rename would silently disable.

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
| 4a | **Cross-process `GetWindowTextW` bypasses `WM_GETTEXT`** — undocumented behaviour of a documented function, and the sweep rests on it | A Windows change routes the read through the message queue | Child process with a WndProc that suppresses `WM_GETTEXT`; assert the parent still reads the kernel name. **No browser needed — runs in milliseconds on every build** |
| 4b | Playwright's headless path still spawns full `chrome.exe`, not `chrome-headless-shell` | Upstream switches binaries. Not silent — the shell is never provisioned, so the launch **fails loudly** — but the failure would be baffling without this note | Launch through the real path, assert the walk yields a titled window owned by that PID |
| 5 | Chromium/Firefox request no breakaway on browser paths | Either adds one | Source search for `CREATE_BREAKAWAY_FROM_JOB` |
| 6 | `--browser-test` call-site inventory (11 files) | Chromium adds a web-facing site | Source search for `switches::kBrowserTest` |
| 7 | `browserName`/`channel`/binary-selection defaults | `validateBrowserConfig` or `getExecutableName` changes | Config round-trip via `browser_get_config` |
| 8 | `outputMaxSize` has no default | `defaultConfig` gains one | Assert unset in the resolved config |
| 9 | Firefox honours `toolkit.winRegisterApplicationRestart` | Mozilla removes the pref | Source check in `nsAppRunner.cpp` |
| 10 | `winldd` no-op for Chromium | Upstream fixes `chrome-win` → `chrome-win64` | Cold-start latency; source check |
| 11 | Tool counts — 78 internal, 69 exposed, 24 default ([§7](#7-the-tool-surface-and-the-package-shape)) | Upstream adds, removes or reclassifies a tool | Golden `tools/list` snapshot; count `skillOnly` in the resolved bundle |
| 12 | `storage` is 17 tools — **and the other capabilities were never counted** | Any capability's membership changes | Count every capability's tools from the resolved bundle. Never from memory |
| 13 | `browser_run_code_unsafe` reaches `httpOnly` cookies from the **default** surface ([§7.1](#71-tools-that-reach-credentials)) | Upstream sandboxes it or moves it out of `core` | Run the probe: default caps, `page.context().cookies()` |
| 14 | `browser_storage_state` omits IndexedDB | Upstream passes `{indexedDB:true}` | Source check at the `storageState()` call site |
| 15 | The child's protocol ceiling is `2025-11-25`, and it never rejects — it caps or echoes | Upstream adopts a newer revision | Assert the negotiated version at startup |
| 16 | `DiscoverProbeTimeout` is 5 s, and the client pin is what skips the probe ([§10.1](#101-the-protocol-split)) | The SDK changes the default or the probe | Assert the pin in every test client; time a spawn against the ~300 ms baseline |
| 17 | `PLAYWRIGHT_MCP_*` count is **42**, two of them outside the config mapping | Upstream adds a variable | Derive the count from the resolved bundle; the allowlist test must not carry a literal |
| 18 | `--caps` and `PLAYWRIGHT_MCP_CAPS` replace rather than merge | `mergeConfig` changes | Config round-trip via `browser_get_config` |
| 19 | Nine artifact generator prefixes ([§12](#12-artifacts-and-output-directory-behaviour)) | Upstream adds an artifact type | Enumerate prefixes in the resolved bundle; an unknown prefix must fail the sort test |
| 20 | Killed children leak `browser@<guid>` descriptors | `BrowserServer.stop()` learns to clean up with a `userDataDir` set | Kill a child, list the browsers-registry root |
| 21 | Payload sizes and the 20.3 s provisioning time ([§8](#8-payload-sizes-and-first-run-provisioning)) | Any browser or Node revision bump | Re-measure at each bump — **and settle the 202.3 / 323 MB / ~300 MB discrepancy while doing it** |
| 22 | Full Chromium refuses a second instance; `chrome-headless-shell` does not notice one | Upstream changes the singleton, or the shell is ever shipped | Launch twice against one profile directory, on both binaries |
| 23 | SDK behaviours — `cmd.exe /c` prefix, `ListToolsAsync` filtering, `ContentBlock` drop-and-throw ([§10.2](#102-sdk-behaviours-a-proxy-must-work-around)) | Any `ModelContextProtocol` bump | The fake-child passthrough tests are written against exactly these |
| 24 | Velopack landmines — feed-URL composition, `SetAutoApplyOnStartup`, the stub, `force_stop_package` ([§13](#13-velopack-and-the-update-path)) | Any Velopack bump | The update lane: real feed URL, real N→N+1, real delta |
| 25 | Claude Code truncates `instructions` and tool descriptions at 2 KB, defers schemas, and **now handles** `tools/list_changed` ([§10.4](#104-the-client-claude-code)) | Any client release | Measure both strings at build time; re-stamp the client version the claim was checked at |
| 26 | Payload licensing as shipped — `winldd` has no license file, full Chromium has no OSS license ([§14](#14-third-party-payload-as-shipped)) | Upstream adds one, or the payload composition changes | Re-read the shipped trees at each revision bump |

| 27 | **NativeAOT publishes clean and the proxy runs** ([§10.3](#103-measured-by-spike-2026-08-15)) | Any SDK bump | `PublishAot`, zero warnings, run the published binary against a real child |
| 28 | The SDK still never relays `notifications/cancelled` | The SDK fixes it — our hand-rolled path would then double-send | Cancel a call, assert exactly one downstream notification |
| 29 | `Filters.Message.IncomingFilters` still exposes `Result` as raw `JsonNode?` | Any SDK bump; this is the whole proxy hook | Short-circuit a `tools/call` and compare bytes |
| 30 | `ListToolsAsync(RequestOptions?, ct)` still drops silently | SDK fixes or changes it | Fake child with an invalid `x-mcp-header`; compare both overloads |
| 31 | `StdioClientTransport` still wraps in `cmd.exe` | SDK fixes it — the custom transport stays correct either way, but the rationale changes | Probe `process.ppid` from a node child |

Add a row whenever a new `[FLOATS]` entry lands. An entry with no row is an entry
nobody will re-check.

---

## 16. Corrections applied 2026-08-15 (late)

Recorded because a corrected number that leaves no trace is indistinguishable
from one that was never wrong, and because two of these were introduced *by this
session* rather than inherited.

**First-run download is 203.8 MB, not 323.5 MB.** Measured 2026-08-15 by exact
`content-length` from `cdn.playwright.dev`: `chrome-win64.zip` 202,283,919 B +
`ffmpeg-win64.zip` 1,411,741 B + `winldd-win64.zip` 128,684 B. On disk, 433 MiB
(chromium 428 + ffmpeg 4 + winldd 1). Slow-link arithmetic: **2 m 43 s at
10 Mbps, 27 m 11 s at 1 Mbps.** `[FLOATS]`

The superseded 323.5 MB / ~700 MiB figures were correct on 2026-08-14 and
included `chrome-headless-shell` (119.7 MB down, 269 MiB on disk). The
[2026-08-15 decision](README.md#settled-2026-08-15) to run full Chromium in every
mode stopped provisioning the shell, which is what changed the number — the old
measurement was never wrong, it just stopped applying. Peak disk during
provisioning is now ~640 MiB while archive and extracted tree coexist, superseding
the ~0.9 GB previously stated.

**`--output-max-size` has no default; the charter said "unverified" in two places
after it had been established.** Verified in `coreBundle.js` on 2026-08-15:
`defaultConfig` carries only `browser` and `timeouts`, and `mergeConfig` filters
through `pickDefined`, which drops `undefined`. See §6. The README's two stale
passages are retired. `[FLOATS]`

**The §A payload table listed browsers as bundled for a day after the decision
that they would not be.** Provisioning moved to first run on 2026-08-14; the table
row survived it. Installer payload is ~117 MB (`node.exe` 88.53 + JS tree 18.11 +
BrowserAI ~10–15); the ~806 MB figure describes disk *after* first run, and
remains the right number for a bundled build if the Chrome-for-Testing
redistribution question is ever resolved favourably. `[MACHINE]` for the
component sizes, `[FLOATS]` for what is in the set.

> **The pattern worth noticing.** All three are the same defect: a measurement
> that was correct when taken, invalidated by a *later decision* rather than by
> upstream, and left in place because nothing links a decision to the numbers it
> falsifies. The re-verification index catches upstream drift; it does not catch
> this. **When a decision changes what is provisioned, configured or shipped, the
> measurements describing the old shape must be re-stated or retired in the same
> commit.**

**Firefox costs relative to Chromium** — measured 2026-08-14, and previously
recorded nowhere in the repository despite being cited in design discussion:
**~2× RAM, ~10× first navigate, ~24× idle CPU, ~20× profile disk.** Chromium
stays the default on these grounds alone. `[UNVERIFIED]` as to method — the
figures are carried forward from a measurement session whose harness was not
preserved, so treat them as order-of-magnitude guidance and re-measure before any
decision turns on them. `[FLOATS]`

---

## 17. Velopack 1.2.0, verified by install/update/rollback

Spike 2026-08-15. A NativeAOT app packed with `vpk`, installed per-user, updated
1.0.0 → 1.0.1, rolled back, and uninstalled, against a local feed server.
Everything here is observed. `[FLOATS]`

### 17.1 The finding the provisioning design rests on

**`RootAppDir` is the directory containing `Update.exe` — the parent of
`current\`.** Across update *and* rollback, a sibling directory accumulated all 7
hook stamps including the original install's, and a 5 MB payload file kept its
sha256 unchanged. **Siblings of `current\` survive both.** The
`AppContext.BaseDirectory` trap is real and was confirmed in the same run: files
written inside `current\` lost their pre-swap contents both times.

Three caveats that bear on the design, none of which the charter had:

- ⚠️ **A repair or overwrite install destroys them.** `install.rs` renames a
  non-empty root to `{root}.{random16}` and, on success, **deletes it**. Re-running
  `Setup.exe` over an existing install therefore costs a **203.8 MB re-download**.
  **Updates must go through the update path; `Setup.exe` must never be re-run over
  an existing install.**
- **Uninstall wipes the whole root** (`remove_dir_contents`) — browsers included,
  which is correct but worth stating.
- Transient update space is `<root>\packages\VelopackTemp\`: same volume, outside
  `current\`.

⚠️ **`force_stop_package` will kill our browsers.** It matches by image **path**
under the root and runs on `apply`, `install`, `start`, `uninstall` **and after
every hook returns** (`windows/util.rs:59`). Two unrelated processes were killed
by an update launched from a third. Our browsers live under `RootAppDir`, so an
update terminates every running browser without warning and without our teardown.
Chromium survives hard kills and our locks release on process death, so the damage
is a lost session rather than corruption — but it bypasses the job object entirely,
and a hook must never leave a helper running under the root.

### 17.2 The nine landmines, re-verified

| # | Charter claim | Verdict |
|---|---|---|
| 1 | Channel in the feed URL → 404 | **Still real, consequence wrong.** 1.2.0 throws `HttpRequestException … 404`. Not silent, not "unrecoverable in the field" — catchable, so a health check can detect it |
| 2 | `SetAutoApplyOnStartup(false)` mandatory | **Still real.** Default is `true`; the relaunch is detached |
| 3 | Never register the stub | **Still real, reason wrong.** "No pipes attached" is false — stdio is inherited stub → `Update.exe` → app, and 3,220 bytes of app stdout arrived on the stub's pipe 12.9 s after the stub died. The killer is that the stub **exits in 59 ms** while the app runs on |
| 4 | `force_stop_package` kills everything under root | **Still real, broader than stated** — see above |
| 5 | `UpdateManager` ctor issues a network request | **Never applied to 1.2.0.** Ctor only assigns fields; 0 ms against an invalid host, zero requests logged. *New caveat:* `VelopackLocator` is not free — it probes writability, **creates `packages\` and `packages\VelopackTemp`**, and opens a log file |
| 6 | `NotInstalledException` is the normal test-host outcome | **Wrong for 1.2.0.** `VelopackLocator.Current` and `new UpdateManager(url)` throw `InvalidOperationException: No VelopackLocator has been set`; `VelopackApp.Build().Run()` **succeeds**, warns, and leaves `IsInstalled == false`. The test seam is a **boolean**, not exception handling |
| 7 | `ApplyUpdatesAndRestart(null)` is undocumented | **Now documented.** Advice still right for a different reason: `toApply ?? GetLatestLocalFullPackage()` means null is "apply whatever is staged", not "just restart". ⚠️ **Charter code error:** `UpdateExe.Start(waitPid)` does not compile — the first positional parameter is the locator |
| 8 | `IVelopackLogger` needs two registrations | **Fixed.** One `VelopackApp.SetLogger()` reaches installer, hooks, `UpdateManager` and bridged Rust output |
| 9 | Rust binaries' Windows floor | **General claim holds; the cited defect is fixed.** `IsWow64Process2` is now dynamically loaded with an error path. Shipped binaries are MinOS 6.0, 32-bit PE32 GUI; no `vpk pack` option sets `os_min_version` |

### 17.3 Update mechanics

Delta for an 8.76 MiB binary change: **3,210 bytes**. Update wall time ~2.5 s, and
**`current\` is absent for 1.7 ms** — two `fs::rename` calls, effectively atomic.
Running instances are killed without warning.

**Rollback needs `AllowVersionDowngrade = true`** (default false yields "no
updates", silently) and then forces a **full re-download** — 6,072,200 b, zero
deltas — because `packages\` was pruned to the new full nupkg during the forward
update. Rollback fires the same obsolete/updated/restarted hooks; from the app's
view it is an ordinary update. `restartArgs` pass through, but **the relaunched
app does not inherit the caller's stdio.**

### 17.4 Channel — the charter's reason was wrong

Default channel is `win` (the OS short name), stamped into `sq.version` and read
back by the locator. **A `-beta` version suffix has zero effect on channel
derivation** — packing `1.0.3-beta.1` with no `--channel` emitted
`releases.win.json`. The charter attributed the hazard to Velopack; it was
application code in a sibling project.

The real reason to set it explicitly: **a client installed from a beta
`Setup.exe` inherits `beta` in its manifest and stays there silently.** Two new
hazards: `ExplicitChannel = ""` produces `releases..json` → 404 (the code
null-coalesces, so empty is not unset), and **`vpk pack` lowercases the channel
while the client does not** — `"Beta"` passes on NTFS and 404s on a
case-sensitive store, which is exactly a sibling project's S3 setup.

### 17.5 NativeAOT, hooks, and `vpk` output

**NativeAOT + Velopack 1.2.0: zero trim/AOT/IL warnings.** `VelopackApp.Build().Run()`
works; install, delta update and rollback all work. Exe 9,182,720 b (8.76 MiB),
full nupkg 6,072,200 b, `Setup.exe` 10,533,768 b. The 34 MB pdb is excluded
automatically. ⚠️ **Target `net10.0-windows`** — the hook callbacks are
`[SupportedOSPlatform("windows")]`, so plain `net10.0` produces CA1416.

**Hooks can register the logon sweep task — confirmed.** All hooks ran **as the
user, non-elevated**, session 1. Fast-exit hooks with their timeouts:
`--veloapp-install` (30 s), `--veloapp-updated` (15 s), `--veloapp-obsolete`
(15 s), `--veloapp-uninstall` (60 s); `OnFirstRun` and `OnRestarted` do not exit.
`schtasks /Create /XML` from the install hook succeeded with
**`LogonType=InteractiveToken`** — "run only when user is logged on", which is
exactly what the sweep needs and the opposite of the session-0 trap. The task
**survived update and rollback** because it targets the stable `current\` path,
and the uninstall hook removed it.

**`vpk` emits**, into `Releases` by default: `{id}-{version}-full.nupkg`,
`{id}-{version}-delta.nupkg`, `{id}-{channel}-Portable.zip`,
`{id}-{channel}-Setup.exe`, `releases.{channel}.json`, `assets.{channel}.json`,
`RELEASES`. **It does not prune** — after 5 versions all 5 fulls and 4 deltas
remained and the feed advertised all of them.

**`.gitignore` verdict** (closes the deferred v1 item): `/Releases/` ✅ ·
`*-Portable.zip` ✅ · `/RELEASES` ✅ default channel only · **`Setup.exe` never
matches** — the real name is `{id}-{channel}-Setup.exe` · **`/payload/`,
`/staging/`, `/.staging/` are not vpk output at all**; they are BrowserAI's own
build conventions and must be justified on that basis or dropped.

### 17.6 New defect: `Setup.exe -- <args>` hangs forever

`setup.rs` declares `EXE_ARGS` without `.value_parser(value_parser!(OsString))`
but reads `get_many::<OsString>`, so passing start arguments panics with
*"Mismatch between definition and access of EXE_ARGS … Could not downcast"*. **The
process never exits**, installs nothing, and leaves one log line. `update.rs` has
the value parser and is unaffected. Any scripted install passing start arguments
hangs forever — the purest instance of this project's own failure class, found in
the tool we were about to trust with it. **BrowserAI must never pass start
arguments to `Setup.exe`.**

Two smaller ones: **Desktop and Start Menu shortcuts are created by default**
(`--shortcuts` defaults to `Desktop,StartMenuRoot`), and
`%LOCALAPPDATA%\velopack\` is created unconditionally, **not removed by
uninstall**, with non-installed runs writing to a machine-shared `velopack.log`.

**Not verified:** MSI/PerMachine, signing, `--runtime win7`, `autoApply=true` with
a staged package, behaviour on older Windows, stdin inheritance through the stub.
