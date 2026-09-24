<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-23 -- the rigs that settled the 27 assumed justifications

**What this is.** T2 was the maintainer's instruction to tag every unmeasured
justification in the tree and drive the count to zero: _"tag everything ASSUMED
now and then start measuring and researching to get the number to 0. I want the
rule to be that this number needs to remain zero. Add a test to check if it is
zeo."_ Twenty-seven markers went in, a test held the published number, and three
readers settled all twenty-seven -- eight in `kb/`, ten in the top-level
documents, nine in `src/`. **This is the rigs and the raw output they produced.**
**91 files, 247,359 bytes.**

**What it is not is the analysis.** Each reader wrote a `REPORT.md` naming every
claim, what it now cites and what was measured for it; all three were consumed by
the commits that settled the claims (`4e71bbb`, `1dc270a`, `339d0a9`) and are not
kept here, because what a report concluded is in the tree and what a rig produced
is not.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb](../../../kb/README.md) | The measurements the eight `kb/` claims and the four that moved out of `src/` now stand on |
| [`TODO.md`](../../../TODO.md) | The justification sweep, whose published count `RecordedCountTests.NoClaimInTheTreeIsStillMarkedAssumed` holds at zero |
| [re-verification index](../../../kb/re-verification.md) | Rows 139 to 146, added with the settlements |

## What is here

| Path | What it establishes |
|---|---|
| `kb/denials*.ps1.txt` + `.out.txt` | What a denied tool name costs a caller, over three rounds |
| `kb/eftype.sh` + `.out.txt` | The enumeration the type check reads |
| `kb/gc.sh` + `.out.txt` | The browser GC's own selection: a basename prefix match and nothing else |
| `kb/lockdeath.ps1.txt` + `.out.txt` | What a lock holder's death leaves behind |
| `kb/integrity-control.js` + `.out.txt` | The integrity level a child inherits, with its control |
| `kb/profile.js` + the five `profile-*.out.txt` | Clean, concurrent, killed, single-shell and control profile arms |
| `kb/readkey/` + `readkey.out.txt` | A .NET rig for the console read, kept as source with its own build files so it inherits none of this repository's central package management or analyzer severities |
| `kb/shots*.js`, `kb/arm-*.log`, `kb/approot-size.out.txt`, `kb/citations.out.txt`, `kb/client-deferral.out.txt` | The remaining `kb/` arms and their raw output |
| `src/probes/` | Five .NET rigs, as source: `cmdshim`, `httptimeout`, `mcpneg`, `stderrloss`, `unwind` -- each answering one `src/` justification |
| `src/probe-*.txt` | What each of those five produced |
| `src/durable-count.txt`, `src/durable-classification.txt` | Every `durabl*` occurrence in `src/`, raw and then classified by sense with the predicate quoted |
| `docs/nu1512-probe/` + `nu1512-probe.txt` | A two-package local feed and a consumer, which is what it takes to establish what NU1512 does to a lock file |
| `docs/*.txt`, `docs/*.cc`, `docs/*.json`, `docs/*.html`, `docs/node-download.log` | The upstream sources and fetched pages the ten document claims now cite, including Chromium's `process_singleton_win.cc` |

## What was cut

⚠️ **Every `bin/` and `obj/` directory under the five .NET rigs and the
`readkey` rig.** What is kept is the source and the project files, which is what
makes a rig re-runnable; a compiled `cmdshim.exe` is build output and this
repository commits none.

⚠️ **Four PowerShell rigs are named `*.ps1.txt`, and the bytes are unchanged.**
`[int]($needed/4)` inside a PowerShell expression is a Markdown link as far as a
text scan is concerned, and the link scan reads every `.ps1` in the tree. The
capture keeps its text and loses the extension that made a scan try to resolve
its arithmetic.

⚠️ **`kb/eftype.out.txt` had its ANSI colour escapes stripped**, and nothing else
about it moved: it is a captured terminal transcript, and the tree refuses a C0
control byte in text. What is stored is what the terminal would have shown.

⚠️ **Two large fetched indexes, cited by URL instead.** `pwmcp-registry.json`
(1,328,143 bytes, the npm registry document for `@playwright/mcp`) and
`node-dist-index.json` (331,923 bytes, `nodejs.org/dist/index.json`). Both are
third-party documents that change under their own URL; the derived answers --
`docs/pwmcp-toolnames.json`, `docs/playwright-mcp-renames.txt` and
`docs/node-size.txt` -- are here, and they are what the claims cite.
