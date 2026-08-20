<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Detecting stray browsers

**Versions in force** unless an entry says otherwise: Windows 11 Pro 26200 · Chrome for Testing 152.0.7977.8 (`chromium-1237`) · Firefox 153.0 (`firefox-1539`) · `@playwright/mcp` 0.0.79 · `playwright-core` 1.63.0-alpha-2026-08-05 · .NET SDK 10.0.400, runtime 10.0.11.
Measured on [the reference machine](../README.md#the-reference-machine).

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
plus several empty-titled ones, and the GPU process owns one too. **43 such
windows existed on the reference machine**, published by a dozen unrelated
Electron and CEF applications plus `msedgewebview2`, enumerated in 52 ms. The
roster is deliberately not listed: what matters is that an ordinary desktop
carries dozens of these, owned by software with no connection to browser
automation at all. `[MACHINE]` for the count; the ambiguity is `[STABLE]`.

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
> [The sweep's design](../../ARCHITECTURE.md#locking-ownership-and-the-sweep)
> and the work that built it both specified the
> fallback; this entry was the odd one out.

> ⚠️ **We are depending on undocumented behaviour of a documented function, and
> it must be pinned by a test.** `GetWindowTextW`'s contract says: *"If the target
> window is owned by another process **and has a caption** … If the window does
> not have a caption, the return value is a null string."* A `Chrome_MessageWindow`
> is created with `dwStyle = 0` and **has no caption**. By the documentation this
> should return empty. It does not. Stable since NT and not plausibly changeable,
> but unverified on any build but Windows 11 26200 — and this is precisely the
> silent-failure class the project exists to eliminate.

## The sweep, measured through the product's own code paths

Every number here comes from the product's own code paths at build-order
while building the stray sweep, on Windows 11 Pro 26200,
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
> the Velopack update lane, on
> the strength of the first measurement below. `LogonSweepTask.cs` and its tests
> are deleted and BrowserAI's own startup sweep is the only trigger.
> **The measurements are kept and the section is not**, because both are facts
> about Windows rather than about our code, and the first is the evidence for the
> decision: deleting it would leave the drop looking like a preference. What has
> changed is that **nothing in the product consumes either of them**, so treat
> this section as a record rather than as a specification.

> ⚠️ **Registering a scheduled task non-elevated fails on this machine, and
> the sweep's own build notes said it had been
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
> so [row 81](../re-verification.md) is *manual*.

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
> strangers' paths: **a dozen unrelated Electron and CEF applications on the
> reference machine — chat clients, a password manager, a game launcher, a
> container GUI — all publish real user-data-dirs there**, and a sweep that
> trusted the channel would be handed every one of them. **The ownership test is
> therefore the entire safety boundary**, not a refinement on top of a safe
> primitive.

**And the signal is forgeable.** A plain .NET console app called
`RegisterClassExW("Chrome_MessageWindow")` — window classes are per-process, so it
succeeded — and created a message-only window titled with an arbitrary path. An
external sweep found it by both exact-title lookup and enumeration,
indistinguishable from a real Chromium singleton. `[STABLE]`

**Two guards, both required:** `[FLOATS]`

1. The titled directory contains our `browserai.json`, with our schema.
2. The owning process's **full image path** equals the Chrome for Testing binary
   BrowserAI provisioned — `QueryFullProcessImageNameW`, exact path comparison.
   **This is not image-name matching and does not weaken that rule**: matching one
   absolute path to a binary we installed is the opposite of matching `chrome.exe`
   wherever it appears. It also independently catches the personal-Chrome fallback
   hazard below.

> **The measurement behind the rule, and it is a near miss rather than a
> principle.** The probe scripts this project grew out of counted and killed
> Chromium **by image name**. That was harmless where it ran — the only
> `chrome.exe` processes on that machine were the probe's own — and it passed
> review for exactly that reason. Swept 2026-08-13 on the same machine, the same
> predicate against `firefox.exe` would have matched **roughly forty of the
> user's own processes**. `[MACHINE]` for the count, and the count is not the
> finding: **an image-name match is a query whose blast radius is a property of
> whoever's desktop it runs on**, so it can be correct in every test and
> catastrophic on first contact with a real machine. Re-establish by enumerating
> processes by image name on any developer workstation and comparing the result
> against the set the tool actually owns. This is why ownership here is
> structural — a job object for the living, a full image path for survivors — and
> why `GetProcessesByName`, `taskkill /IM` and name-filtered WMI are refused by
> an analyzer rather than by review
> ([`NeverByImageNameTests`](../../tests/BrowserAI.Tests/NeverByImageNameTests.cs)).

**Two hazards specific to enumeration:** `[FLOATS]`

- **The title is an untrusted string on a filesystem path.** Measured
  `File.Exists(<title>\browserai.json)`: local existing 0.56 ms, unmapped `Z:\`
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

> ✅ **Settled 2026-08-17. `Corrected 2026-08-17 (previously "That last
> observation disagrees with the selector logic, and the selector is
> authoritative … `[UNVERIFIED]` as to which branch the run took")`.** There was
> never a disagreement: `--browser chromium` **is** a channel, and the missing
> half was the CLI stage that supplies it. Read from the resolved
> `playwright-core/lib/coreBundle.js`, three functions in a row —
>
> 1. `resolveBrowserParam("chromium")` returns
>    `{ browserName: "chromium", channel: "chrome-for-testing" }`. It is a
>    `switch`, and `"chromium"` is the one case that substitutes a channel the
>    caller did not type.
> 2. `configFromCLIOptions` copies that straight into `launchOptions.channel`.
> 3. `getExecutableName` tests `options.channel && registry.isChromiumAlias(…)`
>    **before** it reaches the `headless ? "chromium-headless-shell" :
>    "chromium"` line, and `chromiumAliases` is exactly `["chrome-for-testing"]`.
>
> So `--headless --browser chromium` resolves the **full binary**, which is what
> was observed. The headless-shell branch is reachable only with **no channel at
> all**, and no `--browser` value produces that state. The earlier note read
> `getExecutableName` in isolation and treated its last line as the default; it
> is the fall-through. **Both entries were right and neither needed retracting** —
> what was missing was one function upstream of the one being read.
> `[FLOATS]` — re-establish by grepping the resolved bundle for
> `resolveBrowserParam` and `getExecutableName` and reading them together, never
> either alone.

> **This is recorded as a property of the shell, not as a risk to us.** BrowserAI
> [does not provision it](../../DECISIONS.md#processes-browsers-and-session-modes) — full Chromium in every
> mode — and `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1` means it cannot appear on disk
> later. So an upstream change to binary selection would produce a **failed
> launch**, which is loud, rather than a silently untrackable browser. It matters
> only if that decision is ever revisited. Note `chromium.executablePath()`
> reports `chrome.exe` for **both** binaries, so it is not a usable indicator of
> which one is running.

## A mapped drive letter is a network path, and costs the same 22 seconds

Measured 2026-08-19 on this machine, .NET 10, Windows 11 Pro 26200. The entry
above says *"reject anything that is not a rooted local drive-letter path before
touching the filesystem"*, and this is the half that sentence does not cover: a
drive letter **is** a rooted local drive-letter path by every character in it and
can still be the redirector.

The alias was made with `DefineDosDeviceW(DDD_RAW_TARGET_PATH, "T:",
@"\Device\LanmanRedirector\;T:0000000000012345\<host>\share")`, which is the same
object-manager symbolic link the multiple-UNC provider writes for `net use` —
**and needs no administrator rights**, which is what makes this testable at all.
No SMB session is established by it; the redirector establishes one on first use,
which is exactly the call being timed. `[MACHINE]` for the timings,
`[FLOATS]` for the .NET and SMB timeouts behind them.

| call | through `T:` (mapped) | through the UNC spelling |
|---|---|---|
| `File.Exists`, dead **hostname** | **22,210 ms** | 22,186 ms |
| `File.Exists`, unroutable **address** `10.255.255.1` | 12.8 ms | 11.4 ms |
| `File.Exists`, local missing path | — | 1.1 ms |

> ⚠️ **The address row is 11 ms here and was 21,037 ms on 2026-08-14**, on the
> same machine and the same address. Nothing was re-reasoned and the older number
> is **not** retracted: an unroutable address fails at whatever layer answers
> first, and that depends on the network the machine is attached to at the time.
> **The dead-hostname figure is the stable one** — it reproduced at 22,210 ms
> against 22,225 ms five days earlier — because a name that does not resolve
> fails at DNS, which does not depend on the route. Use the hostname case to
> re-establish this, and expect the address case to move.
>
> **Negative caching makes the second measurement of a dead hostname worthless:**
> the same lookup immediately afterwards came back in 17.7 ms. Re-measure from a
> cold cache or with a hostname nothing has asked for yet.

**The two cheap ways to ask, and what each can and cannot see.** Both read the
object manager rather than the filesystem, and neither talks to a server.

| | `GetDriveTypeW("X:\")` | `QueryDosDeviceW("X:")` |
|---|---|---|
| an ordinary local volume | `DRIVE_FIXED` (3) | `\Device\HarddiskVolume3` |
| a mapped network drive | **`DRIVE_REMOTE` (4)** | `\Device\LanmanRedirector\…` or `\Device\Mup\…` |
| a `subst` | `DRIVE_FIXED` (3) — **invisible** | **`\??\C:\the\real\path`** |
| a letter that names nothing | `DRIVE_NO_ROOT_DIR` (1) | fails, `ERROR_FILE_NOT_FOUND` |
| cost | **0.9 ms** warm | **0.0103–0.0212 ms**, 1,000 calls |

**`GetDriveTypeW` does not block on a dead mapping.** 0.9 ms against `T:`
*immediately after* the 22,210 ms `File.Exists` on that same letter, so the SMB
session was unestablished and the redirector unresponsive at the moment it was
asked. `[FLOATS]` — this is the entry that would invalidate the whole ordering if
it moved, so it has a re-verification row.

> ⚠️ **`Corrected 2026-08-19 (previously, in `RollingFileWriter`: "telling the
> difference needs GetDriveType — a filesystem call, which on a disconnected
> mapping can block for exactly as long as the thing being avoided")`.** That
> sentence justified leaving mapped drives uncovered, and it was **reasoning
> rather than a measurement** — the one thing this knowledge base exists to stop.
> It is false as written: `GetDriveTypeW` is not a filesystem call, and it did
> not block.

**Which alias forms `Path.GetFullPath` resolves, on .NET 10.** Measured
2026-08-19 by round-tripping each spelling of one real directory. `[FLOATS]` —
this is a BCL behaviour and the two `no` rows are what
`Sessions/SessionDirectoryGuard` exists for.

| spelling | resolved by `Path.GetFullPath`? |
|---|---|
| 8.3 short name of an existing path | **yes** — expanded in full |
| 8.3 short prefix with a tail that does not exist | **yes** — prefix expanded, tail preserved verbatim |
| `\\?\C:\…` and `\\.\…` | no — passed through untouched |
| a directory junction | no |
| a `subst` or mapped drive letter | no |

> ⚠️ **8.3 generation is PER VOLUME, and half the volumes here do not do it.**
> Measured 2026-08-19 by creating a directory with spaces in its name on each
> volume and calling `GetShortPathNameW`: **`C:` shortens, `D:`, `E:` and `F:` do
> not** — and neither does the volume the **GitHub Windows runner** checks out
> onto, which is how this was found. A path with no short name comes back
> unchanged rather than failing, so a test that builds an 8.3 alias and does not
> check that it got one is asserting that a path equals itself. `[MACHINE]` for
> which volumes, `[STABLE]` for the setting being per-volume — it is
> `fsutil 8dot3name query <volume>`, and reading it needs administrator rights,
> which is why the observable check is the round trip rather than the setting.

> ⚠️ **The 8.3 rows correct
> [the adversarial review](../../docs/reviews/2026-08-18-adversarial-locking.md),
> finding A4**, which lists 8.3 names among the four things `Path.GetFullPath`
> "does not resolve". On this toolchain it does: `PathHelper.Normalize` expands a
> path containing `~` through the filesystem. The review's other three hold, and
> A4's conclusion is untouched — it needed only one unresolved alias and has
> three. Re-establish with `GetShortPathNameW` on a directory whose name has a
> space in it, then `Path.GetFullPath` on what comes back.

**`GetFinalPathNameByHandleW` resolves all of them in one call, for 0.071 ms.**
Measured over 200 calls on a local volume, `CreateFileW` with no access and
`FILE_FLAG_BACKUP_SEMANTICS` plus `GetFinalPathNameByHandleW` with
`VOLUME_NAME_DOS | FILE_NAME_NORMALIZED`. A junction, an 8.3 name, a `\\?\`
prefix and a `subst`ed letter all came back as the same `\\?\C:\…` true path.
`[MACHINE]` for the figure, `[STABLE]` for the resolution behaviour.

## Windows re-spells a path's drive letter; a process never re-spells its own

Measured 2026-08-19 on this machine, .NET 10 / Windows 11 26200. `[STABLE]` for
the API behaviour, `[MACHINE]` for the run.

**Every Windows API that hands a path back answers with the mount manager's
canonical DOS name, whose drive letter is upper-case** —
`GetFinalPathNameByHandleW`, `QueryFullProcessImageNameW`, `GetShortPathNameW`
and `QueryDosDeviceW` alike. Nothing re-spells a path a process composed for
itself: `Path.GetFullPath` collapses `.` and `..` and expands an 8.3 component
through the filesystem, and leaves the drive letter exactly as it was handed in.

So the casing of every path a process composes is inherited from **whatever
started it**, and stays that way for the life of the process:

| Test host invoked from | `AppContext.BaseDirectory` |
|---|---|
| PowerShell / `pwsh` | `C:\Source\…` |
| Git Bash | `c:\Source\…` |

**Which makes an ordinal comparison between a composed path and an OS-read one a
property of the caller's shell rather than of the product.** Reproduced at
`cc45900` by running the same tree twice with nothing changed but the shell:

| Shell | `dotnet test` |
|---|---|
| PowerShell | total 484, **0 failed** |
| Git Bash | total 484, **2 failed** — both in `SessionDirectoryGuardTests`, both `Expected to contain "directory='c:\…'"` against a refusal that named `C:\…` |

**To re-establish it:** run `dotnet test` from each shell on one commit. The
cheap version is `[System.IO.Path]::GetFullPath('c:\windows')` beside
`(Get-Item 'c:\windows').FullName` in the same session.

⚠️ **A single-shell run cannot see this, and structurally never will.** The
hosted CI this project had between 2026-08-18 and 2026-08-20 ran every step under
`pwsh`, so it picked the casing that happens to agree and baked it in — which is
why this was reported twice from a machine and never once from a build. What puts
it in front of a single-shell run is `DriveLetterCase`, over which six of
`SessionDirectoryGuardTests`' arms are parameterised: its `Lower` value composes a
spelling no Windows API ever returns, so the wrong comparison goes red everywhere
rather than somewhere. Proof it does not need a shell: the planted arm failed
*from PowerShell*, with the identical two failures Git Bash had produced.

*Corrected 2026-08-20 (previously "CI cannot see this … Every step in `build.yml`
runs under `pwsh`"): CI was removed that day, and the property is about
single-shell runs rather than about CI.* With CI gone the release gate is the
suite on one machine, so [release checklist item
8](../../RELEASING.md#8-run-everything) now requires it to be run **from
PowerShell and from Git Bash**, and both totals recorded. That is belt beside
`DriveLetterCase`'s braces, and it is what catches the next defect of this shape
before the parameterisation has been extended to cover it.

## Process image path — the fully documented detection path

Measured 2026-08-15 with a PowerShell harness that is not in this repository; the
sequence is three documented calls and is trivially rebuilt. `EnumProcesses`
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
> [the rule against it](../../tests/BrowserAI.Tests/NeverByImageNameTests.cs) once someone later treats
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

**What that gap costs, measured 2026-08-19: Chromium refuses in 5,036 ms naming
the cause; Firefox hangs for 180,402 ms with an error that never mentions the
profile.** Two browsers pointed at one profile directory, one family at a time:

| Family | Elapsed | What came back |
|---|--:|---|
| Chromium | **5,036 ms** | `Browser is already in use for <dir>, use --isolated to run multiple instances of the same browser` — the cause, the directory, and the flag that avoids it |
| Firefox | **180,402 ms** | Playwright's launch timeout. **The profile is not mentioned anywhere in it** |

**The 5,036 ms is upstream's own retry loop and it lines up exactly.**
`isProfileLocked5Times` calls `isProfileLocked` and sleeps 1,000 ms between
attempts, five times, before it gives up — so five seconds is the *refusal*
succeeding, not a slow failure. And `isProfileLocked` opens
`path.join(userDataDir, "lockfile")` on win32, which is **Chromium's** lock file
name; Firefox's is `parent.lock`. The guard therefore never fires for Firefox, the
launch proceeds, and Firefox's own lock blocks the juggler handshake until
Playwright's three-minute launch timeout expires.

**This became reachable when this product started offering Firefox**, on
2026-08-19. [Re-verification row 22](../re-verification.md) covers the Chromium
half only, and the Firefox half is what the numbers above add.

**BrowserAI's own path is covered, and it is covered by construction rather than
by ordering.** `Runtime/ChildLaunch.Create` is the one route to a child, and it
calls `FirefoxProfileLockedException.For(config)` **before** it writes the config
and long before anything spawns; that inspects `parent.lock` with a write-open and
refuses on the sharing violation, naming the holder through the Restart Manager.
So a BrowserAI-launched Firefox never reaches upstream's `isProfileLocked` with a
held profile. Two things are worth saying beside that: the session lock already
makes the collision unreachable by *ordering*, and the preflight exists precisely
because coverage by ordering is a guarantee no test states and no refactor
notices losing; and the preflight reads the **live handle**, never the file's
existence, because Firefox never deletes `parent.lock`.

**Do not fix upstream's bug.** The check would have to know both names and both
lock semantics, and the product does not need it: what it needs is the refusal it
already has. Recorded here so that a reader meeting a three-minute silence knows
what it is.

**Re-establish** by launching the provisioned browser twice against one profile
directory, one family at a time, under a hard timeout, and timing both. **The
control is the Chromium arm** — without it, a Firefox launch that takes three
minutes cannot be told from a slow machine. Read `isProfileLocked` out of the
resolved `coreBundle.js` for the file name it probes. `[FLOATS]`

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
> the passthrough work took when it built
> parse-error recovery from the MCP SDK's behaviour rather than from its
> Apache-2.0 code. The measurement is unchanged; only the instruction was wrong.
> [The Firefox preflight and its second detection path](../../ARCHITECTURE.md#locking-ownership-and-the-sweep)
> carried the same sentence and is corrected too.

## The Restart Manager, as the product uses it

Measured while building Firefox support on
Windows 11 Pro 26200, against `firefox-1539` (Firefox 153.0) launched through
`@playwright/mcp` 0.0.79. `[FLOATS]`

**It answers exactly what a sharing violation cannot: who.** A session profile
driven by a live Firefox reports **one** holder — the browser's parent process —
whose `ProcessStartTime` equals the creation time `GetProcessTimes` reports for
the same pid, so the pair matches the identity the rest of this product uses with
no conversion. A second session's profile, whose `parent.lock` exists and is held
by nobody, reports **zero**. Re-establish with
`FirefoxTests.AFirefoxWeLaunchedIsAttributedToItsSessionAndIsNotRegisteredForRestart`,
which runs on every build and writes its numbers into the run's scratch directory
as `firefox-attribution.json`.

**`parent.lock` outlives its holder, confirmed rather than carried over.** After
the holding process was terminated, the file was still on disk 15 seconds later
and the Restart Manager reported no holders for it — which is the state an
existence check would misread as "a browser is running", and the reason the
preflight reads the live handle instead. Asserted in both directions by the same
test, so a Windows or Mozilla change that started deleting the file is a red
build rather than a silently stricter product.

**It costs 638 ms a query, and that is a design constraint rather than a
detail.** Measured on this machine 2026-08-16 across two real Firefox profiles,
recorded as `firefox-attribution-negative.json` in the run's scratch directory by
`FirefoxTests.AnUnheldLockAttributesNobodyAndAForeignFirefoxIsAttributedToNoSession`.
The query walks every handle on the machine, so the cost scales with what else
is running — **42 processes of a foreign Firefox, not launched by BrowserAI,**
were, and a whole sweep pass is otherwise ~27 ms. Two consequences, both built:

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

**A foreign Firefox is the negative control, and it is a live one.** This arm is
methodologically load-bearing rather than incidental: it proves the attribution
path *rejects* a browser it did not launch, instead of merely never meeting one.
Measured 2026-08-16: **2** foreign profiles under
`%APPDATA%\Mozilla\Firefox\Profiles`, **1** of them held, by an ordinary
system-installed `%ProgramFiles%\Mozilla Firefox\firefox.exe` — **42 processes**,
~85 hours old, with a visible window, none of it launched by BrowserAI. The
Restart Manager names it perfectly, and it is
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
> the installer](../../DECISIONS.md#processes-browsers-and-session-modes).

> ⚠️ **`--user-data-dir` alone is not an ownership signal, and this is one of
> the most load-bearing facts in this article.** On the reference machine **a
> dozen unrelated Electron and CEF applications** pass it — chat clients, an
> editor, a password manager, a game launcher — plus **four `msedgewebview2.exe`
> processes**, none of which has anything to do with browser automation. A
> detector keyed on the switch would claim all of them. Only an exact match
> against a directory BrowserAI created is safe. The roster is deliberately not
> reproduced: it is a property of *a* desktop, while the count and the conclusion
> transfer. `[MACHINE]` for the count; the conclusion is `[STABLE]` — the switch
> is a public CLI argument any Chromium embedder may pass.

## Windows object names and window scoping

**You cannot put a path in a mutex name, and .NET throws rather than relocating
the object.** Any backslash after the namespace prefix is refused: re-measured
2026-08-17 on **.NET 10.0.400**, `new Mutex(false, name)` threw
`System.IO.DirectoryNotFoundException` — *"Could not find a part of the path"* —
for `Global\<a drive-letter path>`, for `Global\a\b` and for `Local\a\b` alike,
while `Global\plain` succeeded. So a path-keyed lock must canonicalise and hash.
The real length limit is **~32,000 characters, not the documented 260**, but
hashing is required regardless. `Global\` additionally needs
`SeCreateGlobalPrivilege`, which interactive users have and low-integrity /
AppContainer processes do not. Re-establish by constructing those four names in
a loop and catching. `[STABLE]`

> ✅ **This settles the disagreement flagged
> [below](#named-mutexes-and-lock-files).**
> `Corrected 2026-08-17`. The prior art's guard against a caller-supplied
> backslash is commented *"one in the caller's string would silently relocate the
> object"*; this entry recorded a throw. **The throw is what happens**, on every
> shape tested, and *silent relocation* does not describe .NET's behaviour at
> all — the framework validates the name before the kernel ever sees it. The
> guard is still right; only its stated reason was.
>
> **What was not measured is raw `CreateMutexW`**, which is where the relocation
> story would have to be true if it is true anywhere: the object manager treats
> a backslash as a path separator, so a name with one in it plausibly names an
> object in a different directory rather than failing. Nothing in this project
> calls `CreateMutexW` directly, so the question is left open rather than
> answered. `[UNVERIFIED]` for the Win32 layer; the .NET layer is measured.

**`FindWindowExW(HWND_MESSAGE, …)` is scoped to a window station and desktop.** A
scheduled task configured *"run whether user is logged on or not"* lands in
session 0 and **sees no message windows at all** — it would sweep, find nothing,
and report success forever. Any sweeper must run in the user's interactive
session. `[STABLE]`

⚠️ **A live browser will not let its own tree be renamed — but Windows has no
such general rule, and this entry asserted one for eight days.** Corrected
2026-08-19 *(previously "**Windows will not rename a directory holding open
executables**, and a live browser holds `chrome.exe`. Download-alongside-and-swap
is therefore not available for a browser reinstall. `[STABLE]`" — cited to an
article about mutex naming that does not discuss renames at all)*. The general
claim is **false**: a running executable can be renamed, and so can its parent and
its grandparent; only deleting the image is refused. The **conclusion** survives
anyway, for a reason the entry never gave, and it is now measured for both
provisioned families rather than assumed for either — a live Chromium **and** a
live Firefox each refuse both renames of their own tree and of the browsers root,
and every one of those renames succeeds the moment the browser is gone
([kb](processes.md#the-same-measurement-for-firefox-and-for-what-both-families-share--2026-08-19)).
The two halves have to be carried together: a reader who has only the general rule
will reach for download-alongside-and-swap, and a reader who has only the browser
result will believe Windows forbids something it permits. `[STABLE]` for the
browser refusals; `[UNVERIFIED]` for why.

## What a suite run puts on the screen

Measured 2026-08-17, because a developer running the suite while working
reported windows flickering over their work and stealing focus, and **nobody had
established which tests actually showed a window** — two guesses had already been
wrong. The point of the entry is that the answer was one test, and that
everything the guesses blamed was measurably innocent.

**How.** Two independent detectors running in one out-of-process watcher for the
duration of a full run: a `SetWinEventHook` on `EVENT_OBJECT_CREATE`,
`EVENT_OBJECT_SHOW` and `EVENT_SYSTEM_FOREGROUND`, and a 40 ms `EnumWindows`
poll. Both filtered to whole top-level windows (`GetAncestor(hwnd, GA_ROOT) ==
hwnd`, `idObject == OBJID_WINDOW`, `idChild == CHILDID_SELF`); every window that
already existed was recorded as a baseline and could not be reported as new. Each
record carries the owning process's image path from
`QueryFullProcessImageNameW`, so a window can be attributed to a binary rather
than to a pid. Correlation to a test is by timestamp against TUnit's own
per-test `startTime`/`endTime` in its JSON report. Re-establish by running that
watcher across `dotnet test` and intersecting the two timelines.

**The result, before the fix: two windows in a 410-test run, and both took the
foreground.** Both were `Chrome_WidgetWin_1`, both full size at
`10,10,1905x2092`, both titled *Untitled – Google Chrome for Testing*, four
seconds apart, and both fell inside the single interval of
`BrowserIdleTimerTests.AnIdleSessionLosesItsBrowserKeepsItsNodeChildAndTheNextCallStillWorks`
— the suite's only `realSessionChildren: true` arm, which reached a real Chromium
through a harness that opened its session in `persistent` mode
(`Headed: true`). Two windows rather than one because that test lets the idle
timer close the browser and then drives it again. `[MACHINE]`

**Headless Chromium creates window objects and never shows one. This is the
guess that was wrong.** That run created **297** distinct top-level windows in
all, and **295 of them were never visible and never took the foreground**.
Chromium accounted for 202: 43 `OleMainThreadWndClass`, 38
`Chrome_MessageWindow`, 35 `IME`, 26 `Chrome_WidgetWin_0`, 19
`crashpad_SessionEndWatcher`, 11 `Base_PowerMessageWindow`, 10
`CicMarshalWndClass`, 10 `Chrome_StatusTrayWindow`, **8 `Chrome_WidgetWin_1`**
and 2 `MSCTFIME UI`. The last-but-one is the one that matters:
**`Chrome_WidgetWin_1` — the class a visible browser window uses — is created in
headless mode too, without `WS_VISIBLE`, and is never shown.** So "the full
`chrome.exe` runs in every mode, therefore headless must flash a window" is
false, and a detector that keys on the class rather than on visibility will
report windows that do not exist on screen. `[FLOATS]`

> **One tool breaks this, and it is not the headless browser changing its mind.**
> `browser_annotate` on a `headless` session puts a **visible, foreground**
> `Chrome_WidgetWin_1` at `100,100,1280x800` on the screen within 1.2 s, measured
> three times on 2026-08-18. The window belongs to a **second** Chromium that
> upstream's dashboard daemon launches headed unconditionally, with its own
> profile under `%TEMP%`, so both facts are true at once: a headless browser
> still shows nothing, and a headless *session* can still be made to show
> something. The measurement, the process tree and the reason the product
> refuses the call are in
> [kb](../playwright/tools-and-artifacts.md#what-browser_annotate-actually-does--measured-2026-08-18).

**Firefox is the same**, 26 windows and none of them visible: 14
`OleMainThreadWndClass`, 4 `Chrome_MessagePumpWindow`, 2 each of
`nsAppShell:EventWindowClass`, `IME` and `MozillaHiddenWindowClass`, and 2
per-profile `Mozilla_firefox-default_<profile>_RemoteWindow`. `[FLOATS]`

**`CREATE_NO_WINDOW` really does suppress the console window.** 9
`ConsoleWindowClass` windows were created by `conhost.exe` and `pwsh.exe` during
the run and **none was ever shown**. A console-window flash is not a cause of
this class of complaint on a launcher that sets the flag, and the flag is set on
every launch here. `[STABLE]`

**After making that one arm windowless** — the rig that starts real children now
opens its session in `headless`, decided in `RigSessionEnvironment` rather than
at the call site — the same watcher across a full green 411-test run recorded
**zero visible windows and zero foreground events**, against 283 top-level
windows created, with the developer's editor holding the foreground throughout.
Both runs took the same time to the tenth of a second — **35.99 s** before and
**35.50 s** after, from TUnit's own `totalDurationMs` — so the change costs
nothing measurable. Neither number should be read as a timing result: they
differ by less than this suite's run-to-run spread. `[MACHINE]`

**`EnumWindows` returns invisible top-level windows in bulk.** The watcher's
baseline sweep of the developer's own desktop, three times across the session:
**590, 586 and 587** top-level windows, of which **100** were visible each time.
So roughly five in six of what `EnumWindows` returns is not on screen. Recorded
because a count of `EnumWindows`' result is sometimes read as *"how busy is this
screen"* and it is not that. `[MACHINE]`

> **Corrected 2026-08-18 (previously: "`MessageWindowTests`' non-vacuity floor of
> 50 is sensitive to this, and it is a `[MACHINE]` property").** That floor is
> gone. It asserted the developer's screen was busy, which is false on a CI agent
> with no interactive desktop — a service window station holds a handful of
> windows and nothing is wrong. The probe now publishes a **second window, top-level
> and never shown, in its own GUID-suffixed class**, and the test asserts
> **disjointness by handle identity**: `EnumWindows` returns the control and never
> the message-only window, both created seconds apart in one process, differing
> only in the parent each was given. Proven by planting — give the control
> `HWND_MESSAGE` as its parent and the assertion goes red. The numbers above stay
> as a measurement; nothing asserts on them now.

> ⚠️ **The first version of the watcher reported zero shows and was wrong**, and
> the mistake is worth carrying because it is the shape of every silent detector
> failure in this repository. It de-duplicated by window handle alone, so a
> window **created hidden and shown a moment later** was recorded once, as a
> `create`, with `visible: false` — and the `EVENT_OBJECT_SHOW` that actually put
> it on screen was dropped as a duplicate. It reported **308 creates and 0
> shows**, which reads as *nothing was ever shown* and is the opposite of what
> happened. **Create and show are distinct events about the same handle**; a
> detector must key on both. `[STABLE]`

## Named mutexes and lock files

Windows and .NET facts about cross-process locking, and the design lessons that
arrived with them.

**Provenance, stated once so no entry below has to repeat it.** The design
lessons were read from source 2026-08-16 in an **unpublished first-party C#
locking library** — four files, a few hundred lines, the successor to a retired
PowerShell rig and the only cross-process locking code available here with a
suite behind it. That codebase is **not reproducible from this repository**; its
file names are kept so the finding stays auditable to whoever has it, and no
entry rests on them alone. **The Windows behaviours are a different matter**:
where an entry names a `SessionLockTests` case, that is this repository's own
suite, it runs on every build, and it is the route to re-establish the fact.
Those are the majority of what follows.

**`AbandonedMutexException` means the wait *succeeded*.** The thread now owns the
mutex; what the exception reports is that the *previous* holder died without
releasing, so whatever that holder was mid-way through writing may be torn.
Catching it and returning a plain success therefore **discards the only warning
the OS gives that the protected state is suspect** — the acquisition was never in
doubt. That library surfaces it as a distinct
`MutexAcquisition.AcquiredAbandoned` outcome rather than folding it into
`Acquired`, and its own remarks name swallowing it as one of two things the
PowerShell version it replaced got wrong. The holder must still be disposed, or the next
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

**⚠️ Extended to the design point on 2026-08-18, and the gate's five-second
timeout turned out to be reachable by queueing alone.** Same rig, same idle
machine, N processes released together on one event; 6 runs at N=100 and 4 at
N=200. The entry above stopped at N=64 and read its 1.40 s as comfortable, which
it is — the problem is what happens between 64 and the hundred the charter
designs for.

| N | slowest refusal | p50 | p99 | what the refusals said |
|---:|---:|---:|---:|---|
| 16 | 367 ms | 209 ms | 367 ms | all `Held`, holder named |
| **100** | **3,349 ms** | 1,353 ms | 3,227 ms | all `Held` — **a margin of 1.49× on the five-second gate, with the machine otherwise idle** |
| 200 | 5,056 ms | 2,750 ms | 5,032 ms | **73 refusals of 796 came back `Busy`**, every one at 5,022–5,056 ms |

**The cost is super-linear in N and the gate is the reason.** Each contender
enters the per-directory mutex *in turn* merely to discover the file is held —
open, meet the sharing violation, read the record to name the holder, release —
so the slowest refusal is the whole queue, not one section. 64→100 is 1.56× the
contenders and 2.4× the wait.

> **`Busy` was therefore reachable with nothing wrong: no wedged holder, no
> abandoned mutex, no starvation.** That matters because of what `Busy` *said*:
> *"That section takes milliseconds, so something is wrong that waiting longer
> will not fix"* — a diagnosis the code could not support, about a machine where
> waiting was the entire remedy — and because a `Busy` withholds the holder's
> identity, which is the one thing this lock exists to be able to report.
>
> **And the gate was smaller than a wait taken inside it.** `SessionLock`'s own
> `OpenHeld` and `ReadRecord` run under this mutex and both go through
> [`RenameWindow`](processes.md#files-durable-writes-and-deletes), whose budget
> became **30 s** on 2026-08-18 — six times the gate that contained it. Nobody
> looked at the pair when the second number moved.
>
> **Re-measured after `LockScopes.PerDirectoryGate` was raised to sixty
> seconds:** N=200, 4 runs of 4, **zero `Busy`**, every loser naming the holder,
> slowest refusal 5,736 ms. `SessionLockTests.TheGateOutlastsEveryWaitTakenInsideIt`
> fails the build if either number crosses the other again.

**⚠️ The queue is gone for contenders that can name the holder, measured
2026-08-18 before and after `SessionLock.ProbeForHolder`.** A contender now
opens `browserai.json` **in front of** the per-directory gate; a sharing violation is
the kernel's answer to *who owns this*, so it refuses there and never creates
the mutex. Same rig, same idle machine, 3 runs at each N, gate at 120 s
throughout so nothing can reach `Busy` on either side. Every refusal named the
holder in all 18 runs. `[MACHINE]` for the timings, `[STABLE]` for the shape.

**A live holder is planted before the contenders are released**, which is what
the entry above does *not* do and is the whole reason the two tables differ.

| N | slowest refusal, before | slowest refusal, after | p50 before → after |
|---:|---:|---:|---:|
| 16 | 329.2 · 330.1 · 331.9 ms | **30.3 · 36.8 · 33.2 ms** | 165 → 29 ms |
| **100 — the charter's design point** | 2,083.9 · 2,047.3 · 2,063.9 ms | **202.8 · 219.4 · 244.9 ms** | 1,047 → 194 ms |
| 200 | 4,267.4 · 4,496.6 · 4,344.5 ms | **448.6 · 485.5 · 559.2 ms** | 2,195 → 406 ms |

**Roughly an order of magnitude at every N — 9.9×, 9.4×, 8.7× on the slowest
refusal — and the *shape* is the stronger evidence.** Before, `p50 ≈ max/2` at
every N, which is the signature of a queue being drained one entrant at a time.
After, `p50 ≈ 0.85 × max` and the fastest and slowest refusals sit within a
factor of three: no queue, just N processes doing the same two file opens at
once.

> **The cold race is unchanged, and that is the design rather than a
> disappointment.** Run the same N contenders against an **empty** directory —
> the rig in the entry above, where the winner is one of the N — and the numbers
> barely move: 349.8–365.7 → 284.1–396.7 ms at 16, 2,100–2,508 → 1,661–2,747 ms
> at 100, 4,191–4,269 → 3,391–4,376 ms at 200, inside the run-to-run noise. At
> `t=0` nothing is held, so **every contender's probe correctly answers "looks
> free"** and every one of them falls through to the gate, exactly as it is
> required to — a probe is a sound ownership test and an unsound freedom test,
> and the free path is not allowed to act on it
> ([review](../../docs/reviews/2026-08-18-adversarial-locking.md), D). The
> queue that gets removed is the queue of peers arriving at a session somebody
> already has, which is the case the product is in for a session's whole life.

**To re-establish the pair**, use the same recipe as below with one addition:
start one `BrowserAI.TestProbe.exe session-hold <directory> <ready.json>
<purpose>` and wait for its report before releasing the contenders, then kill it
afterwards. The suite's own arm is
`SessionLockTests.AContenderThatCanNameTheHolderIsRefusedInFrontOfTheGate`,
which holds the gate from a third process for the whole call — so a `TryAcquire`
that still entered it could only come back `Busy`, and the outcome is the
discriminator rather than any clock.

**To re-establish**, at any N without a test host: create a session directory and
a manual-reset `EventWaitHandle`, start N ×
`BrowserAI.TestProbe.exe session-race <directory> <eventName> <report-i.json> <release.flag>`,
wait for every `<report-i.json>.ready` to appear — that handshake is what makes it
a race rather than a queue — set the event, then read the reports and group them
by `outcome`. Each one carries `elapsedMilliseconds` and `gateTimeoutMilliseconds`
beside it. The suite's own arm is
`SessionLockTests.UnderConcurrentProcessesExactlyOneAcquiresAndEveryOtherIsToldWho`,
which pays for N=16 on every run and takes any N by raising `Contenders`.
`[MACHINE]` for every timing, `[STABLE]` for the outcome and for the super-linear
shape.

The machine-wide sweep scope was measured the same way and separately: **8
processes, zero timeout, 1 acquired and 7 refused**, each refusal asserted under
one second — try-acquire-and-skip, with no queue behind it, which is the whole
difference between that scope and the gate above. `[MACHINE]`

**A named mutex is owned by the thread that waited on it, and releasing it from
another thread throws a message that names nothing relevant** —
`ApplicationException` about "an unsynchronized block of code"
— the library pre-empts it with its own `InvalidOperationException` naming both
thread ids, which is the mitigation worth copying. The operational consequence
is one line and it is severe: **do not `await` across a named-mutex critical
section**, because the continuation may resume on a different pool thread and the
release then fails with a diagnostic that points nowhere near the cause. `[STABLE]`

**A `Global\` → `Local\` fallback exists there, and it is deliberately not
silent.** `Global\` creation is caught for `UnauthorizedAccessException`,
`IOException`, `NotSupportedException` and `WaitHandleCannotBeOpenedException`,
then retried under `Local\`, with the resolved `Name` and an `IsProcessLocal` flag
exposed as properties so a caller can print them and a test can assert on them
. The reason given matches
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

**A caller-supplied backslash in a mutex name is guarded against, and the guard's
stated reason was wrong.** That library rejects any backslash in the base name
with the comment that it *"separates the namespace from the name, so one in the
caller's string would silently relocate the object"*. **Settled by measurement
2026-08-17** and recorded in full
[above](#windows-object-names-and-window-scoping): .NET refuses such a name with
`DirectoryNotFoundException` on every shape tried, so there is no silent
relocation to guard against at that layer — the guard is correct and its
justification was not. Both descriptions always agreed on the action, which is
why nothing ever turned on it: the name must be hashed or canonicalised either
way, which is what the design does. `Corrected 2026-08-17 (previously
"Unresolved and flagged rather than reconciled … `[UNVERIFIED]` as to which
failure a given name produces")`. What stays open is the raw `CreateMutexW`
layer, named as open in the entry above rather than left as a disagreement here.

**An unreadable lock file must throw, not read as free.**
`SessionLockService.ReadLock` returns null for exactly three conditions — the file
does not exist, it vanished mid-read, or the parsed field set carries no `owner`
key (covering an empty file, a comment-only file and pure garbage) — while an
**unreadable** file propagates the exception instead. The stated reason is the
whole point: *"a read failure that reads as 'the rig is free'
is exactly the answer that gets a live session stomped"*
. The general shape is the one this project keeps
meeting — **an error path that resolves to the permissive answer is worse than a
crash**, because it is silent and it is wrong in the direction that destroys
state. `[STABLE]`

**That lock records no process identity at all — and the boot id is for something
else.** Its lock-state type is explicit that "held by a dead process" is
deliberately *not* a state: a session there spans many launcher processes (the
launcher exits between commands), so **a dead owner is indistinguishable from an
idle one**, and an idle ceiling is the entire substitute. There is no
`(pid, creationFileTime)` pair to make reboot-safe. A boot id exists
there but guards a different file — a crash
marker — where a *changed* boot id means a recorded pid means nothing and every
world must be treated as protected. `[MACHINE]` for the design; the underlying
constraint is `[STABLE]`.

**Deriving a boot id without WMI.** That library takes
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
non-trivial — both reproducible anywhere, the first by suspending a machine and
comparing, the second by timing a cold `Win32_OperatingSystem` query. `[MACHINE]`
and **carried rather than re-measured** for the 10.4 ms / 0.12 ms figures: they
were taken in that unpublished library on 2026-08-14 and not re-run here, so read
them as an order of magnitude and not as this project's own numbers.

## The pre-gate probe as a liveness report — measured 2026-08-20

`SessionLock.ProbeLiveness` is the open the per-directory gate's short-circuit
already performed, given a name and three answers. It became a reporting call as
well as a decision on 2026-08-20, when `browserai_list` started saying whether
each session it lists is being driven, so what it **costs per entry** stopped
being an implementation detail.

The open is `FileMode.Open`, `FileAccess.ReadWrite`,
`FileShare.ReadWrite | FileShare.Delete`, `bufferSize: 1` — one `CreateFile` and
one `CloseHandle`, no directory walk, no process handle and no mutex.

| Arm | Warm, 2,000 iterations, 3 runs | What it is |
|---|---:|---|
| `browserai.json` free | **0.0342 · 0.0351 · 0.0359 ms** | the open succeeds and the handle is closed |
| `browserai.json` held | **0.0481 · 0.0490 · 0.0493 ms** | a sharing violation, so a **managed exception** rather than a return |
| first probe in a process | 0.85 · 0.95 · 6.32 ms | one-time initialisation; the 6.32 ms run is the one that also JIT-compiled |

**The held arm costs about 40% more than the free arm, and that is the
exception rather than the filesystem.** The syscall is the same one; what differs
is `FileStream` raising and this code catching.

**Against the walk the same loop already performs.** `SessionLayout.SizeOnDisk`
is a recursive `EnumerateFiles` with a `Sum`, and `browserai_list` calls it once
per entry. Measured the same day over a provisioned Chromium tree — **310 files,
447,613,809 bytes** — it took **2.3 ms cold and 0.6–0.7 ms warm**. So the probe
is about **a seventeenth** of the cheapest walk available to measure, and a
session whose profile has actually been used holds far more than 310 files.
Adding a probe per entry therefore cannot make a listing pathological: the
listing's cost was already the walk. `[MACHINE]`

> **Reproduce, and keep the sanity check.** Time the open above in a loop against
> a real `browserai.json`, once with nothing holding it and once with a second handle
> open `FileAccess.ReadWrite, FileShare.Read` — and **assert that the second arm
> really is a sharing violation**, `(HResult & 0xFFFF) is 32 or 33`, before
> believing its number. Without that assertion an open that quietly succeeded
> would be timed as the held arm and would report the free arm's cost under the
> wrong name.

## Two users and one install root — what spans users and what does not — measured 2026-08-20

`%LocalAppData%` separates users. `BROWSERAI_ROOT` and the installer's
install-to flag both defeat that, and two users then share one browsers
directory, one session index and one live-marker directory. This section is what
could be established about that arrangement on this machine, and — first,
because it bounds everything after it — what could not.

### What could not be measured, and why

**No second user account could be created.** The token this was measured from is
a *filtered* administrator token: `whoami /groups` reports
`BUILTIN\Administrators` as **"Group used for deny only"**, `New-LocalUser`
returns `AccessDeniedException`, and every other local account on the machine
(`Administrator`, `DefaultAccount`, `Guest`, `WDAGUtilityAccount`) is disabled.

**No second logon session could be created either.** `query user` reports
exactly one — console, id 1 — and a loopback network logon
(`New-PSSession -ComputerName localhost`, which would produce a *different*
logon-session SID for the same user) fails Negotiate with `0x8009030e`.

**So nothing below was measured across two real users.** What was measured is
the security descriptor each object is created with, a positive control proving
that dump would show a broader ACE if one existed, and what a token holding **no
ACE** on such an object actually gets back. That last one is the same code path a
second user would take, with the only variable set the same way — it is not the
same sentence as *a second user was refused*, and it is not written as if it
were.

### 1. A `Global\` object needs no `SeCreateGlobalPrivilege` here

The whole privilege list of this token is `SeShutdownPrivilege`,
`SeChangeNotifyPrivilege`, `SeUndockPrivilege`,
`SeIncreaseWorkingSetPrivilege` and `SeTimeZonePrivilege`.
**`SeCreateGlobalPrivilege` is absent**, and
`new Mutex(false, "Global\\…", out created)` from session 1, non-elevated, still
returned `createdNew=True`. So the machine-wide namespace is reachable by an
ordinary interactive user and the name resolves in one place for every logon
session — which is the premise `LockScopes`' refusal to fall back to `Local\`
rests on. `[MACHINE]`: a domain policy can grant or remove that privilege
elsewhere.

### 2. The DACL the kernel puts on it names three SIDs and no group

Read off the created handle:

```
D:(A;;0x1f0001;;;SY)(A;;0x120001;;;S-1-5-5-0-260717)(A;;0x1f0001;;;S-1-5-21-…-1001)
```

LOCAL SYSTEM full (`0x1F0001` is `MUTEX_ALL_ACCESS`); **the creating logon
session**, `S-1-5-5-X-Y`, with `SYNCHRONIZE | READ_CONTROL | MUTANT_QUERY_STATE`;
and **the creating user** full. There is no ACE for `Everyone`,
`Authenticated Users`, `BUILTIN\Users` or `BUILTIN\Administrators`.

> **The positive control, because an absence found by reading is not an
> absence.** The same dump of a mutex created with an explicit `Everyone` ACE
> reads `D:(A;;0x100000;;;WD)`. A group ACE would therefore have been visible;
> there is not one. `[STABLE]` for the mechanism — this is the token's default
> DACL — and `[MACHINE]` for the exact SIDs.

### 3. A token with no ACE is refused at the constructor the product uses

Measured by creating a `Global\` mutex whose DACL grants only `SY`, then opening
that same name from the same process:

| Call | Result |
|---|---|
| `new Mutex(false, name, out created)` | `UnauthorizedAccessException` — *"Access to the path 'Global\\…' is denied."* |
| `Mutex.OpenExisting(name)` | the same |

That is exactly `MachineMutex.Create`'s documented
`UnauthorizedAccessException` arm — *"an object of that name exists and this
token cannot open it"* — reached for real rather than reasoned about. `[STABLE]`

### 4. The file half spans users under a shared root; the default root's does not

SDDLs measured the same day. `BU` is `BUILTIN\Users`, `AU` is
`Authenticated Users`, `BA` is `BUILTIN\Administrators`.

| Object | DACL | What a second *standard* user could do |
|---|---|---|
| `%LocalAppData%\BrowserAI\live\` | `(A;OICIID;FA;;;SY)(A;OICIID;FA;;;BA)(A;OICIID;FA;;;<user>)` | **nothing** — no `BU`, `AU` or `WD` ACE |
| a `.live` marker inside it | `(A;ID;FA;;;SY)(A;ID;FA;;;BA)(A;ID;FA;;;<user>)` | **nothing** |
| `<browsers>\reinstall.lock` | the same three | **nothing** |
| a directory created at `C:\…`, the install-to shape | `(A;OICIID;FA;;;BA)(A;OICIID;FA;;;SY)(A;OICIID;0x1200a9;;;BU)(A;ID;0x1301bf;;;AU)(A;OICIIOID;SDGXGWGR;;;AU)` | list it and add to it |
| a `.live` marker created inside **that** | `(A;ID;FA;;;BA)(A;ID;FA;;;SY)(A;ID;0x1200a9;;;BU)(A;ID;0x1301bf;;;AU)` | **read it, open it for write, and delete it** |

`0x1200A9` is `FILE_GENERIC_READ | FILE_GENERIC_EXECUTE`. `0x1301BF` carries
`DELETE`, `FILE_READ_DATA` and `FILE_WRITE_DATA`, and it reaches new files
through the inherit-only `SDGXGWGR` ACE that the volume root's own DACL
propagates. `[MACHINE]` for the SDDLs; `[STABLE]` for the inheritance rule.

### 5. The consequence, and it is asymmetric

**A file lock is enforced by the kernel against handles and is indifferent to
which token opened them.** So under a shared root `browserai.json`, `reinstall.lock`
and every `.live` marker stay honestly held-or-free across users, a second user
can probe them, and the held-ness rule keeps working: a marker another user
holds answers *sharing violation* and is left alone, and one that cannot be
opened at all answers *undetermined* and is also left alone. **The marker
reclaim is therefore safe across users by construction** — it acts only on
`Free`, and neither cross-user case can produce that answer.

**The mutexes that serialise the work around those files are not.** Whichever
user creates a `Global\` name first owns its DACL, and by section 3 the other
user's `MachineMutex.Create` is refused. Every consumer then takes its own
degraded path:

| Consumer | What it does when the mutex is refused | Does a caller hear? |
|---|---|---|
| `LiveInstances.Join` | returns `null` — **this process announces nothing** | no, a log line |
| `LiveInstances.ReclaimStaleMarkers` | `NoLock`, nothing touched | no, a log line |
| `StraySweep.Run` | `NoLock`, nothing swept | no, a log line |
| `SessionLock.TryAcquire` | `Refused`, carrying `SessionErrors.NoMachineWideLock` | **yes** |

**The first row is the dangerous one and it is the whole finding.** A process
that cannot join creates no marker, so it is invisible to the other user's
census; that census answers *Alone*, and an apply runs `force_stop_package`,
which kills every process under the install root — including the other user's
BrowserAI and its browsers. A shared root therefore re-opens precisely the
failure the live-marker set exists to prevent, and it does so silently, because
three of those four rows never reach a caller.

### 6. What is still not established

- **Whether a real second user's token is refused.** Section 3 measured a token
  with no ACE, which is the same condition arrived at a different way. It is not
  the same claim and must not be quoted as one.
- **Whether the logon-session ACE ever decides anything on its own.** Same user,
  different logon session, was not reachable — and the user-SID ACE would grant
  access in that case regardless, so that ACE's practical effect is untested.
- **Two administrators, one elevated.** `BA` has `FA` on the default
  `%LocalAppData%` tree, so an elevated peer could read another user's markers
  and lock files. Nothing here measured what that does to any of the four
  consumers above.
- **What the installer's install-to flag actually produces.** The volume-root row
  in section 4 is a directory created by this token, not by the installer, and an
  installer running elevated can set a different DACL.
- **Session 0 and services.** Every measurement here is from an interactive
  session-1 token.
