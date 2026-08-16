<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Detecting stray browsers

Measured 2026-08-15.

**Chromium writes its user-data-dir path as the title of a message-only window**
of class `Chrome_MessageWindow`, for its own single-instance logic
(`chrome/browser/process_singleton_win.cc`). `FindWindowExW(HWND_MESSAGE, NULL,
"Chrome_MessageWindow", <title>)` → HWND → `GetWindowThreadProcessId` → PID, in
~60 µs. The exact-title probe cannot return a profile you did not name — but see
[Enumeration works](#enumeration-works--and-it-moves-the-safety-boundary): the
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

## Cross-process title reads — settled by two independent agents

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

**`InternalGetWindowText` diverges from `GetWindowTextW` nowhere.** ~1550
window-level comparisons across every integrity level, a blocked UI thread and a
suspended process produced **zero divergences**. It binds fine under NativeAOT
and is declared unguarded in the public SDK, so the usual worry is unfounded.
`[FLOATS]`

> ⚠️ **Corrected 2026-08-16 @ build-order step 16 (previously "an undocumented
> dependency that buys nothing is a pure loss. Keep it as a test oracle
> instead").** The measurement is unchanged; the recommendation was wrong in two
> ways. It is **not** undocumented — MS Learn documents it as copying a window's
> text *without sending `WM_GETTEXT`*, which is precisely the behaviour
> `GetWindowTextW` delivers here and its own contract denies. And it does not buy
> nothing: it is the only documented spelling of the read the sweep depends on,
> so the day `GetWindowTextW` starts honouring its contract for caption-less
> windows is the day this is the API that still answers. It is therefore the
> product's **fallback**, reached only when the documented call returned empty —
> which on this machine is never — in `MessageWindows.TitleOf`. It remains the
> suite's oracle as well:
> `TheTwoTitleApisAgreeOnEveryMessageWindowOnThisMachine` in
> `MessageWindowTests` compares the two on every window it walks, so a divergence
> appearing is a red build rather than a silent behaviour change.
> [§C](../../plan/C-sessions.md#detection-is-documented-attribution-may-fail-and-must-fail-safe)
> and [step 16](../../plan/build-order.md#16-the-stray-sweep) both specified the
> fallback; this entry was the odd one out.

> ⚠️ **We are depending on undocumented behaviour of a documented function, and
> it must be pinned by a test.** `GetWindowTextW`'s contract says: *"If the target
> window is owned by another process **and has a caption** … If the window does
> not have a caption, the return value is a null string."* A `Chrome_MessageWindow`
> is created with `dwStyle = 0` and **has no caption**. By the documentation this
> should return empty. It does not. Stable since NT and not plausibly changeable,
> but unverified on any build but Windows 11 26200 — and this is precisely the
> silent-failure class the project exists to eliminate.

## Re-measured 2026-08-16, building the sweep

Every number here comes from the product's own code paths at build-order
[step 16](../../plan/build-order.md#16-the-stray-sweep), on Windows 11 Pro 26200,
and each is re-established by running the named test. `[MACHINE]` for the counts
and timings; `[STABLE]` for the API behaviours.

**The cross-process bypass reproduces exactly, against a window built to defeat
it.** A probe process registers its own class, creates a message-only window
named with a **GUID**, and answers `WM_GETTEXT` with an empty string from its own
WndProc. Same-process, both `GetWindowTextW` and an explicit
`SendMessageW(WM_GETTEXT)` come back **empty** — the probe reports both itself,
so the suppression is evidence rather than an assumption. Cross-process,
`GetWindowTextW` returns the GUID, and so does `InternalGetWindowText`. No
browser, milliseconds, every build:
`MessageWindowTests.ACrossProcessReadGetsTheRealNameFromAWindowThatSuppressesWmGetText`.
`[STABLE]`

**`EnumWindows` finds zero `Chrome_MessageWindow`s while the class-qualified walk
finds dozens**, at the same instant in the same process — **673 top-level windows
on this machine, 0 of that class, against 64 the walk finds.** This is the one
that has to be a test: the obvious simplification does not throw, does not warn,
and would make every sweep report a clean machine forever.
`MessageWindowTests.EnumWindowsFindsNoMessageWindowsAtAllWhileTheWalkFindsThem`.
`[STABLE]`

**The exact-title canonicalisation table, re-measured rather than carried
over** — same four rows as
[the original](#detecting-stray-browsers), now asserted on every build against a
window whose title the test chose:
`MessageWindowTests.TheExactTitleProbeMatchesOnlyTheSpellingWindowsItselfMatches`.
Backslashes hit, upper- and lower-cased drive letters hit, a trailing separator
misses, forward slashes miss, and a `NULL` class finds nothing at all. `[FLOATS]`

**A full sweep pass over this machine, end to end:** `[MACHINE]`

| | Published AOT binary (`--sweep`) | Framework-dependent probe (Debug) |
|---|---|---|
| Elapsed, per pass | **24.9 / 26.2 / 26.5 / 26.7 / 28.4 / 28.6 ms** | 37.5 / 38.0 / 38.1 / 39.3 ms |
| Pids from `EnumProcesses` | 666–668 | 662–665 |
| Opened | 496–504 | 496–501 |
| `Chrome_MessageWindow`s walked | 64 | 63–64 |
| Of those, **titled** | 13 | 13 |
| Walk restarts / truncations | 0 / 0 | 0 / 0 |

That is the whole pass — process enumeration, the window walk, a title read per
window, and the index self-clean — not just the process half. Re-establish with
`BrowserAI.exe --sweep` under a scratch `BROWSERAI_ROOT` and read the process
log, or with the probe's `stray-sweep` mode.

**51 of the 64 message windows are nameless, and one of the 13 named ones is not
a path.** It is `DeviceMonitorMessageWindow`, owned by a Chromium embedder, and
the sweep's string guard refuses it before any filesystem call — **a live example
of the untrusted-title hazard, on this machine, today**, rather than a
hypothetical. Re-establish by reading `rejectedTitles` out of the probe's
`stray-sweep` report. `[MACHINE]`

**A real headless Chromium publishes its `userDataDir` and is attributed in
about half a second.** `chrome.exe --headless=new --user-data-dir=<profile>
about:blank` out of the provisioned tree, found by image path and tied to the
profile by its message window: **543 ms and 547 ms**, two runs, from
`CreateProcessW` returning to the title matching.
`StraySweepTests.TheSweeperFindsARealBrowserItLaunchedItselfInTheInteractiveSession`.
`[MACHINE]`

> ⚠️ **`--no-startup-window` is the wrong way to keep it alive, and the failure
> is a timing one.** With no window and nothing to do, a headless Chromium exits
> on its own within a second or so; a test that then waits for its window waits
> out the whole deadline against a browser that has already gone. Observed
> 2026-08-16 — passed alone, failed under a fully parallel suite. `about:blank`
> keeps it running, and the test now fails loudly on an exited browser rather
> than timing out.

**The legacy `%LOCALAPPDATA%\ms-playwright` tree is not matched, and that is the
property the whole design exists for.** Five `chrome-headless-shell.exe`
processes have been running on this machine since 2026-08-15 08:43:58 — a
leftover of the `npx`-based setup this project replaces, same vendor, same
Chromium revision 1237. Detection against the two binaries BrowserAI provisioned
returns **zero candidates** with all five alive. Re-establish with the probe's
`stray-sweep` mode pointed at the real browsers root, or by
`StraySweepTests.DetectionMatchesOnlyTheBinariesBrowserAiProvisionedAndMissesTheLegacyTree`,
which additionally plants a process at the same *shape* of path so the check
means something on a machine that has never had one. `[MACHINE]` for the tree;
`[FLOATS]` for the match rule.

## The logon sweep task

Measured 2026-08-16 on Windows 11 Pro 26200, from a **medium-integrity,
UAC-filtered administrator** token.

> ⛔ **The feature these measurements were taken for is DROPPED**, decided
> 2026-08-16 at
> [step 19](../../plan/build-order.md#19-velopack-package-update-roll-back), on
> the strength of the first measurement below. `LogonSweepTask.cs` and its tests
> are deleted and BrowserAI's own startup sweep is the only trigger.
> **The measurements are kept and the section is not**, because both are facts
> about Windows rather than about our code, and the first is the evidence for the
> decision: deleting it would leave the drop looking like a preference. What has
> changed is that **nothing in the product consumes either of them**, so treat
> this section as a record rather than as a specification.

> ⚠️ **Registering a scheduled task non-elevated fails on this machine, and
> [step 16](../../plan/build-order.md#16-the-stray-sweep) said it had been
> verified to work.** It has not. `schtasks /Create /XML` and the
> `Schedule.Service` COM API both answer **`Access is denied` / `0x80070005`**,
> in the task-library root and in a new `\BrowserAI\` folder alike. A **minimal**
> task definition — one logon trigger, one `cmd.exe` action — fails identically,
> so it is the machine's policy rather than anything about our XML. The
> filesystem is not the gate: `Authenticated Users` do have Write on
> `C:\Windows\System32\Tasks` and a plain file lands there; the Task Scheduler
> service refuses the registration itself. `[MACHINE]`
>
> **Whether elevation fixes it is `[UNVERIFIED]`** — a UAC prompt cannot be
> answered from a non-interactive session, so it was not tried. What this settles
> is only that the *non-elevated* claim was false. **It stays unverified and
> that is now final rather than owed**: step 19 dropped the task instead of
> elevating for it, so nothing depends on the answer.

**`LogonType` is valid only beside a `UserId`, never beside a `GroupId`.**
Measured: with `<GroupId>S-1-5-32-545</GroupId>` and
`<LogonType>InteractiveToken</LogonType>`, `schtasks /Create` refuses the file
with *"The task XML contains an unexpected node"* and names the `LogonType` line.
A group principal would still have run in the user's own interactive session —
but only by implication, and *"run only when user is logged on"* is the setting
whose absence makes a sweeper in session 0 report success forever, so it has to
be stated rather than implied. ~~The definition therefore names the installing
user.~~ `[STABLE]` for the schema; `[MACHINE]` for the error text.

> **Corrected 2026-08-16 (previously "The definition therefore names the
> installing user").** There is no definition any more — it was deleted with the
> task. The schema fact above is unchanged and was measured before the drop; it
> is retained because it is a property of Task Scheduler that the next person to
> reach for a logon task on this machine will need, and it cost a *"the task XML
> contains an unexpected node"* to learn. **It is no longer asserted by a test**,
> so [row 81](../README.md#re-verification-index) is *manual*.

## Enumeration works — and it moves the safety boundary

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

> ⚠️ **That last observation disagrees with the selector logic, and the selector
> is authoritative.**
> [`getExecutableName`](../playwright/configuration.md#defaults-that-are-not-what-they-look-like),
> read from the shipped bundle, picks `headless ? "chromium-headless-shell" :
> "chromium"` when **no channel** is set — so a headless launch with no channel
> should have taken the shell. The run above did not record its resolved channel
> and so cannot say which branch it went down, and a source read covering every
> configuration outranks a single run. **The observation stands and is not
> retracted**; re-run it capturing `browser_get_config`'s resolved channel. It
> changes nothing about what BrowserAI builds: `browserName` and an explicit
> chromium-alias channel are set in every mode, which lands on the full binary
> down either branch. `[UNVERIFIED]` as to which branch the run took.

> **This is recorded as a property of the shell, not as a risk to us.** BrowserAI
> [does not provision it](../../README.md#settled-2026-08-15) — full Chromium in every
> mode — and `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1` means it cannot appear on disk
> later. So an upstream change to binary selection would produce a **failed
> launch**, which is loud, rather than a silently untrackable browser. It matters
> only if that decision is ever revisited. Note `chromium.executablePath()`
> reports `chrome.exe` for **both** binaries, so it is not a usable indicator of
> which one is running.

## Process image path — the fully documented detection path

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
different profile** ([profile fallback](../chromium/profiles.md)). Such a process retitles its message window to the
fallback path, so title-keyed detection loses it — while image-path detection
still sees it, because the binary is unchanged. It cannot safely be *killed*
(it may belong to a live session whose directory was unusable), so it takes the
report-don't-kill path; but knowing beats not knowing.

> **Rejected optimisation, recorded so it is not re-proposed.**
> `NtQuerySystemInformation(SystemProcessInformation)` returns image *names* in a
> single call and could pre-filter before any `OpenProcess`. At 13.88 ms there is
> nothing to buy, and it would put an image-name comparison inside the detection
> path — which is exactly the pattern that erodes into
> [the rule against it](../../plan/D-locking.md#never-by-image-name) once someone later treats
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
`ProfileUnlockerWin::TryToTerminate` does exactly this. `[STABLE]`

> ⚠️ **Corrected 2026-08-16 @ build-order step 17 (previously "…and is worth
> copying line for line").** **It is not copyable at all.** Mozilla's source is
> **MPL-2.0** and this repository is `LicenseRef-BrowserAI-FSL-1.1-MIT-5yr`, so
> taking its text would relicense a file of ours under terms the charter does not
> carry. The *sequence* is the documented API contract and belongs to nobody;
> `src/BrowserAI/Interop/RestartManager.cs` was written from that contract and
> from the observed behaviour, which is the same route
> [step 9](../../plan/build-order.md#9-lossless-passthrough) took when it built
> parse-error recovery from the MCP SDK's behaviour rather than from its
> Apache-2.0 code. The measurement is unchanged; only the instruction was wrong.
> [§D](../../plan/D-locking.md#firefox-the-preflight-and-a-second-detection-path)
> carried the same sentence and is corrected too.

## The Restart Manager, as the product uses it — 2026-08-16

Measured at build-order [step 17](../../plan/build-order.md#17-firefox) on
Windows 11 Pro 26200, against `firefox-1539` (Firefox 153.0) launched through
`@playwright/mcp` 0.0.79. `[FLOATS]`

**It answers exactly what a sharing violation cannot: who.** A session profile
driven by a live Firefox reports **one** holder — the browser's parent process —
whose `ProcessStartTime` equals the creation time `GetProcessTimes` reports for
the same pid, so the pair matches the identity the rest of this product uses with
no conversion. A second session's profile, whose `parent.lock` exists and is held
by nobody, reports **zero**. Re-establish with
`FirefoxTests.AFirefoxWeLaunchedIsAttributedToItsSessionAndIsNotRegisteredForRestart`,
which records its numbers to `.work/firefox-attribution.json`.

**`parent.lock` outlives its holder, confirmed rather than carried over.** After
the holding process was terminated, the file was still on disk 15 seconds later
and the Restart Manager reported no holders for it — which is the state an
existence check would misread as "a browser is running", and the reason the
preflight reads the live handle instead. Asserted in both directions by the same
test, so a Windows or Mozilla change that started deleting the file is a red
build rather than a silently stricter product.

**It costs 638 ms a query, and that is a design constraint rather than a
detail.** Measured on this machine 2026-08-16 across two real Firefox profiles,
recorded to `.work/firefox-attribution-negative.json` by
`FirefoxTests.AnUnheldLockAttributesNobodyAndAForeignFirefoxIsAttributedToNoSession`.
The query walks every handle on the machine, so the cost scales with what else
is running — 42 of the developer's own `firefox.exe` were, and a whole sweep
pass is otherwise ~27 ms. Two consequences, both built:

- **The sweep asks `File.Exists(parent.lock)` first** — 0.56 ms — and only pays
  the Restart Manager where a Firefox has ever run. Absence proves no Firefox
  ever opened the profile; presence proves nothing, which is why the expensive
  question still has to follow it.
- **Nothing polls it.** Waiting for a lock to be released is a liveness check on
  the pid, which costs microseconds; a 100 ms poll of this would be ten seconds
  of machine-wide handle enumeration per wait.

The preflight pays one query per refusal, and only on a refusal: **1,367 ms end
to end against the three-minute modal it replaces.** `[MACHINE]`

**The layout of `RM_PROCESS_INFO` is a trap worth naming.** `RM_UNIQUE_PROCESS`
is `{ DWORD; FILETIME }` — 12 bytes, 4-aligned. Declaring the `FILETIME` as a
64-bit integer aligns the struct to 8 and inserts four bytes of padding after the
pid, so every field after it is read from the wrong offset and the pid itself
still looks right. Two `uint`s, recombined by hand. `[STABLE]`

**The developer's own Firefox is the negative control, and it is a live one.**
On this machine, 2026-08-16: **2** foreign profiles under
`%APPDATA%\Mozilla\Firefox\Profiles`, **1** of them held, by a process running
`C:\Program Files\Mozilla Firefox\firefox.exe` — dozens of processes, ~85 hours
old, with a visible window. The Restart Manager names it perfectly, and it is
attributed to **none** of our sessions and is **not** a candidate, because the
detection guard is a full-path match against the binary BrowserAI provisioned.
That is the sharper half of this test: the process passes the mechanism the
attribution path is built on and is rejected by the guard, rather than failing
the first filter and never reaching the second. `[MACHINE]`

> ⚠️ **The detector is blind to fallback-profile instances, and covering them is
> a trap.** A Chrome that cannot open our profile falls back, and its message
> window is titled with the **fallback** path. With `channel: "chrome"` that path
> is `%LOCALAPPDATA%\Google\Chrome\User Data` — **the user's own browser's
> message window**. A detector extended to match it would identify a personal
> Chrome as a stray. The answer is not a better matcher: **validate the directory
> before launch so the fallback never happens**, and launch the Chrome for Testing
> build BrowserAI provisions rather than `channel: "chrome"`. **Provisioned, not
> bundled** — ["our own" is the build BrowserAI manages, not one shipped inside
> the installer](../../README.md#settled-2026-08-15).

> ⚠️ `--user-data-dir` alone is **not** an ownership signal. Discord, VS Code,
> Signal, Teams, WhatsApp, Steam, ChatGPT and four `msedgewebview2.exe` processes
> all pass it. Only an exact match against a directory BrowserAI created is safe.
> `[MACHINE]`

## Windows object names and window scoping

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

## Named mutexes and lock files — first-party prior art in C#

Read from source 2026-08-16, **not run here**:
`C:\Source\SixFive7\StationeersPlus\TestRig\src\TestRig.Core\` —
`Infrastructure\CrossProcessLock.cs` (131 lines), `Session\LockState.cs`,
`Session\SessionLockService.cs`, `Infrastructure\BootIdentity.cs`. This is the C#
successor to a retired PowerShell rig, in the same language BrowserAI is written
in, and it is the only cross-process locking code on this machine with a suite
behind it. Re-establish by reading those four files.

**`AbandonedMutexException` means the wait *succeeded*.** The thread now owns the
mutex; what the exception reports is that the *previous* holder died without
releasing, so whatever that holder was mid-way through writing may be torn.
Catching it and returning a plain success therefore **discards the only warning
the OS gives that the protected state is suspect** — the acquisition was never in
doubt. `CrossProcessLock.cs:96-101` surfaces it as a distinct
`MutexAcquisition.AcquiredAbandoned` outcome rather than folding it into
`Acquired`, and the type's own remarks name swallowing it as one of two things the
PowerShell version got wrong. The holder must still be disposed, or the next
waiter inherits the abandonment. `[STABLE]`

**An abandoned mutex is only observable by a process that already held a
handle.** Measured 2026-08-16 across real processes: a probe created
`Global\BrowserAI-<hash>`, acquired it, and was `TerminateProcess`d from outside.
A process that opened the name **afterwards** got a clean `Acquired` — the last
handle closed with the dying holder, so the kernel object was destroyed and the
next `CreateMutexW` made a new, unabandoned one. A process holding a handle
**before** the kill got `AbandonedMutexException`, and releasing it succeeded,
which is the proof it really was acquired. The abandonment is then **consumed**:
the next acquire on the same object is ordinary. `[STABLE]`

> **This is why a test for race R3 can pass while the handling is missing
> entirely.** Written the intuitive way round — kill the holder, then open the
> name — the acquisition is not abandoned and the test observes `Acquired`, so it
> passes for a build that swallows `AbandonedMutexException` and for a build that
> never meets one. The order is the test. Reproduce:
> `SessionLockTests.AnAbandonedMutexIsAcquiredAndTheAcquisitionSaysSo`, whose
> comment records the ordering as load-bearing. It also means R3 matters
> *because* BrowserAI is designed for ~100 concurrent processes: with one process
> on a machine there is nothing to abandon to.

**The three lock scopes under real concurrency, measured across processes.**
2026-08-16, N real processes started, parked on one manual-reset event, and
released together; every one drives the product's own `SessionLock.TryAcquire`
against one session directory. Two runs at each N. `[MACHINE]` for the timings,
`[STABLE]` for the outcome.

| N | acquired | refused | winner (ms) | fastest refusal (ms) | slowest refusal (ms) |
|---:|---:|---:|---:|---:|---:|
| 16 | **1** | 15 | 37.6 · 41.7 | 60.9 · 66.2 | 366.0 · 374.4 |
| 64 | **1** | 63 | 47.6 · 41.7 | 69.5 · 62.5 | 1403.7 · 1405.7 |

Every refusal named the holder's pid, its process start time, when the lock was
taken and its recorded purpose — so no caller is ever told merely "busy". The
slowest refusal is the queue behind the per-directory gate, which each loser
enters in turn to discover the file is held: **1.40 s at N=64 against a
five-second gate**, and no run came close to it. Reproduce by raising
`Contenders` in `SessionLockTests`, rebuilding, and running
`UnderConcurrentProcessesExactlyOneAcquiresAndEveryOtherIsToldWho` alone; the
suite pays for N=16 on every run.

The machine-wide sweep scope was measured the same way and separately: **8
processes, zero timeout, 1 acquired and 7 refused**, each refusal asserted under
one second — try-acquire-and-skip, with no queue behind it, which is the whole
difference between that scope and the gate above. `[MACHINE]`

**A named mutex is owned by the thread that waited on it, and releasing it from
another thread throws a message that names nothing relevant** —
`ApplicationException` about "an unsynchronized block of code"
(`CrossProcessLock.cs:134-145`, which pre-empts it with its own
`InvalidOperationException` naming both thread ids). The operational consequence
is one line and it is severe: **do not `await` across a named-mutex critical
section**, because the continuation may resume on a different pool thread and the
release then fails with a diagnostic that points nowhere near the cause. `[STABLE]`

**A `Global\` → `Local\` fallback exists there, and it is deliberately not
silent.** `Global\` creation is caught for `UnauthorizedAccessException`,
`IOException`, `NotSupportedException` and `WaitHandleCannotBeOpenedException`,
then retried under `Local\`, with the resolved `Name` and an `IsProcessLocal` flag
exposed as properties so a caller can print them and a test can assert on them
(`CrossProcessLock.cs:60-75`). The reason given matches
[the entry above](#windows-object-names-and-window-scoping): `Global\` needs
`SeCreateGlobalPrivilege`, which an interactive user normally has and a service or
container user may not. **The recorded defect is instructive** — the PowerShell
version fell back *silently and per process*, so two processes could resolve to
two different kernel objects, not be serialised against each other at all, **and
both report success**. `[STABLE]` for the mechanism; `[MACHINE]` for the defect.

> **BrowserAI has decided the other way — `Global\` only, no fallback, no lock
> means no session.** That is not a contradiction of the above but the opposite
> reading of the same fact: a `Local\` mutex still *reports* success while
> serialising nothing across sessions, and for a browser profile the failure it
> permits is two live sessions on one `userDataDir`. The prior art keeps the
> fallback because a degraded rig beats an unusable one; we refuse it because a
> degraded lock is indistinguishable from a working one at exactly the moment it
> matters. Both need `IsProcessLocal`-style visibility — the divergence is only in
> what to do when it is true.

**A caller-supplied backslash in a mutex name is guarded against, and the two
descriptions of *why* do not agree.** `CrossProcessLock.cs:52-58` rejects any
backslash in the base name with the comment that it *"separates the namespace from
the name, so one in the caller's string would silently relocate the object"*. [The
entry above](#windows-object-names-and-window-scoping) instead records a
**measured** `DirectoryNotFoundException` for `"Global\C:\Source\..."`. **Unresolved
and flagged rather than reconciled:** one says silent relocation, the other says a
throw, and only the second was measured. Both agree the name must be hashed or
canonicalised, which is what the design already does, so nothing turns on it
today. `[UNVERIFIED]` as to which failure a given name produces — settle it by
running both shapes if anything ever depends on the distinction.

**An unreadable lock file must throw, not read as free.**
`SessionLockService.ReadLock` returns null for exactly three conditions — the file
does not exist, it vanished mid-read, or the parsed field set carries no `owner`
key (covering an empty file, a comment-only file and pure garbage) — while an
**unreadable** file propagates the exception out of `RigFiles.ReadTextOrNull`. The
stated reason is the whole point: *"a read failure that reads as 'the rig is free'
is exactly the answer that gets a live session stomped"*
(`SessionLockService.cs:222-236`). The general shape is the one this project keeps
meeting — **an error path that resolves to the permissive answer is worse than a
crash**, because it is silent and it is wrong in the direction that destroys
state. `[STABLE]`

**That lock records no process identity at all — and the boot id is for something
else.** `LockState.cs:5-12` is explicit that "held by a dead process" is
deliberately *not* a state: a session there spans many launcher processes (the
launcher exits between commands), so **a dead owner is indistinguishable from an
idle one**, and an idle ceiling is the entire substitute. There is no
`(pid, creationFileTime)` pair to make reboot-safe. The boot id exists
(`BootIdentity.cs`) but guards a different file — `session.dirty`, the crash
marker — where a *changed* boot id means a recorded pid means nothing and every
world must be treated as protected. `[MACHINE]` for the design; the underlying
constraint is `[STABLE]`.

**Deriving a boot id without WMI.** `BootIdentity.cs` takes
`DateTimeOffset.UtcNow` minus `Environment.TickCount64` rather than
`Win32_OperatingSystem.LastBootUpTime`, for two reasons that both bind here:
LastBootUpTime **costs hundreds of milliseconds on a cold WMI service**, and
`System.Management` is **not AOT friendly**. `GetTickCount64` counts *biased
interrupt time*, which includes time the machine spent asleep, so **a laptop that
suspends overnight does not read as rebooted** — the property that makes the
derivation usable at all. The subtraction is quantised to a 10 s bucket because
two clocks of ~15.6 ms resolution disagree: measured on this machine 2026-08-14,
40 samples in a tight loop spread the derived instant over **10.4 ms**, and two
samples eight seconds apart differed by **0.12 ms**. The failure direction is
chosen deliberately — a spurious *change* reads as "rebooted" and is
conservative; a boot id that failed to change across a real reboot is the
dangerous one, and no bucket size can produce it. It cannot survive a **step**
correction to the system clock, which Windows normally slews rather than steps.
`[STABLE]` for `GetTickCount64` including sleep and for the WMI cost being
non-trivial; `[MACHINE]` and carried from that file for the 10.4 ms / 0.12 ms
figures, which were measured there and not re-run here.
