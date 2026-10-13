<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-13 -- Chrome for Testing's stack sampling profiler and the browser it crashes

Establishes
[Chrome for Testing samples its own stacks, and the sampler can crash the browser](../../../kb/playwright/configuration.md#chrome-for-testing-samples-its-own-stacks-and-the-sampler-can-crash-the-browser----measured-2026-10-13),
which re-verification row 220 re-checks. Evidence:
[`docs/evidence/2026-10-13-stack-profiler/`](../../evidence/2026-10-13-stack-profiler/README.md).

**Why it exists.** The release run's red of the real Chromium arm of `ScreenshotLimitTests`
at `44368995`, on 2026-10-12 at 22:04Z, kept its rig by the maintainer's 94 a, and the
profile in it held a Crashpad dump of the browser process. The dump named Chromium's own
stack sampling profiler. This rig measures whether that crash comes back without the
suite, and whether `--disable-stack-profiler` takes it away; the maintainer's 129 d uses it
to decide when the switch can come out again.

## What is here

| File | What it does |
|---|---|
| `stress.mjs` | The A/B. `node stress.mjs <out> <launches> <seconds> <on\|off\|alternate>` launches Chromium through `playwright-core`'s `launchPersistentContext` with the arguments BrowserAI handed Playwright for the arm's session, headless at 1920x1080, a fresh profile per launch under `<out>`, and takes full-page screenshots of a 1920x16385 and a 1920x16384 page, in turn, until `<seconds>` have passed since the launch. `off` adds `--disable-stack-profiler`; `alternate` starts with `on`. After its first capture past 1.5 s it reads the browser's thread names through `threadnames_live.py`, with the browser's pid from CDP's `SystemInfo.getProcessInfo`. A launch whose capture fails records the error; every `.dmp` under the profile's `Crashpad` folder is read with `dumpsig.py`. One JSON line per launch goes to `<out>\results.jsonl`, and a launch with no error and no dump has its folder removed |
| `threadnames_live.py` | `python -I threadnames_live.py <pid>`: the description of every thread of that process, from a toolhelp snapshot of threads filtered by the owning pid, each thread opened for limited query and closed again |
| `dumpsig.py` | `python -I dumpsig.py <dump> ...`: one JSON line per minidump, the exception's code, its address as module and offset, the faulting thread's name, the read address, and Crashpad's `ptype` and `pid` |
| `mdump.py`, `threadnames.py` | The first readers of the release red's dump, before `cdb` was run on it: the stream directory, the exception and its context, the module list with each module's PDB, Crashpad's annotations, and every thread's name, start of stack and instruction pointer |
| `summarize.py`, `stats.py` | The A/B's table out of `results.jsonl`, and Fisher's one-sided p over launches with the chance of the second row's captures losing nothing at the first row's rate |
| `epilogues.py` | `python -I epilogues.py <chrome.dll>`: counts the byte patterns of `pop <register>; vzeroupper; ret` in the image's `.text` and prints the bytes at the crash site |

## What keeps it off the rest of the machine

- **Nothing shows a window**: every browser is headless, and every script runs from a
  shell with its output in files.
- **It ends what it started and nothing else.** Each browser is Playwright's own child and
  is closed through Playwright; nothing is stopped by pid or by name.
- **It selects no process by image name.** The thread reader takes a pid the browser gave
  over CDP, and the dumps are read from the rig's own profiles.
- ⚠️ **It ran a copy of `chromium-1247` in a scratch folder**, so that no browser of the
  rig ran from the shared browsers root, where a containment arm of a running gate counts
  one in no session's job as an escapee. It was run under the suite's lock all the same,
  because it starts browsers.

## Running it

With the payload's `node.exe`, Python 3.12 and a copy of the provisioned Chromium. The
paths are constants at the top of `stress.mjs`, as it ran on 2026-10-13: the
`playwright-core` it imports, the copy's `chrome.exe`, `python.exe` and this folder. Set
them for the machine, take the suite's lock, and start it detached with Playwright's
browser log on, from Git Bash:

```
DEBUG=pw:browser <payload>\node\node.exe stress.mjs <out> 20 28 alternate > <out>.node.log 2>&1
python -I summarize.py <out>\results.jsonl
python -I stats.py 10 <lost on> <captures on> 10 <lost off> <captures off>
```

To read a dump the way the kb section was read, with Microsoft's public symbols:

```
cdb -z <dump> -y "srv*<cache>*https://msdl.microsoft.com/download/symbols" -i <chrome-win64 folder>
.ecxr ; kn ; .cxr @r14 ; .fnent @rip ; ub @rip L30 ; u @rip L4
```

`@r14` held the context the unwinder was handed in the four dumps of 2026-10-13 it was
read in, the release red's and three of the A/B's; where it does not, read the context off
`RtlVirtualUnwind`'s fifth argument.
