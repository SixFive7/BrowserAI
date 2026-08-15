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
> [the rule against it](../../PLAN.md#never-by-image-name) once someone later treats
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
