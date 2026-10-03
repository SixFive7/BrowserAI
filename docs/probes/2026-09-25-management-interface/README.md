<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-25 -- listeners on loopback, a page in two browsers, and the windows that could show it

Establishes [a management page on loopback](../../../kb/windows/loopback-page.md)
and re-verification rows 178 to 181. Evidence:
[`docs/evidence/2026-09-25-management-interface/`](../../evidence/2026-09-25-management-interface/README.md),
whose `second-pass/notes/index.txt` says which rig produced each run.

**Why it exists.** The zoom-out's track B asked what should replace the
configuration window. The first pass measured the listeners and the windows; the
second built a listener with one gate and attacked it from two real browsers.

## What is here

| Path | What it does |
|---|---|
| `first-pass/probes/` | Five NativeAOT probes: a baseline that serves nothing, a raw socket, HttpListener, and Kestrel from the empty and the slim builder, each serving one status document on an ephemeral `127.0.0.1` port; `common/` holds what they share. And `wv2probe`, a hidden window that drives WebView2 through hand-declared COM interfaces, and `avaprobe`, a minimal Avalonia window |
| `first-pass/driver/` | Starts a probe on a private desktop, inside a kill-on-close job, and measures it |
| `first-pass/publish-*.sh` | Publish the probes, the WebView2 builds and one copy of `BrowserAI.exe` per listener variant |
| `first-pass/hijack-test.ps1` | Whether a second process of the same user can register a longer http.sys prefix on a port the first holds |
| `second-pass/probes/sprobe/` | The listener with one gate: `Loopback.cs` is the gate, `Peer.cs` the same-user check, `Program.cs` the routes and the event stream |
| `second-pass/probes/p1probe/` | Starts the provisioned Chromium itself with a DevTools pipe, and serves the page over it |
| `second-pass/stest/`, `ffcheck.mjs`, `fuzz.mjs`, `resume.mjs` | The attacks and the tab checks: the raw request table and the Chromium checks, the same checks in Firefox, the 4,000 mutated requests, and the restart on the same port and token |
| `second-pass/p2.mjs` | The same window through node and playwright-core |
| `second-pass/run1.sh`, `netlog-summary.mjs`, `netlog-bytes.mjs` | One run under the driver, and the reading of a Chromium net log |
| `second-pass/driver/` | The second pass's driver: a private desktop with no right to switch to it, a kill-on-close job, and the job's memory sampled |
| `*/appcopy/` | The listener and window variants as they were compiled into a copy of `BrowserAI.exe`: the files the copy added, and `appcopy.patch.txt`, the change to the product's `BrowserAI.App.csproj` and `Program.cs` against `a81bad8` |
| `*/Directory.Build.*`, `Directory.Packages.props`, `nuget.config`, `build.sh`, `publish-app.sh` | The scratch build settings, which stop MSBuild walking up into this repository's own |

## What keeps it off the rest of the machine

- Every listener binds `127.0.0.1` only. Every window was created on a private
  desktop opened without the right to switch to it, inside a kill-on-close job,
  and the browsers ran headless or there.
- The idle-traffic runs sat behind a proxy that answered nothing. The runs
  outside it made their usual background requests for the seconds they lasted.
- ⚠️ **A Firefox run outside the suite adds five values under
  `HKCU\Software\Mozilla\Firefox\Launcher`**; the second pass removed exactly
  the five its scratch Firefox added. Export the key before a run.
- ⚠️ **The app copies replace the house `.editorconfig` with `root = true`**, so
  the house analyzer preferences did not apply to them. Zero banned symbols and
  zero trim or AOT warnings are established for them, and the full analyzer set
  is not.
- **It selects no process by image name**, put to the real scan: both passes
  were copied under `build/` beside `observe.ps1`, `NeverByImageNameTests` was
  run, and the copy was removed. The scan named `observe.ps1`, the positive
  control, and nothing here. `hijack-test.ps1` starts the HttpListener probe by
  path and stops only the pid it started.

## Running it

Nobody ran it when this record was written; what follows is read from the
files. The scratch roots are compiled in as
`C:\Source\SixFive7\BrowserAI\.work\zoomout\b` and `...\b2`, with the packages
restored there; the second pass also needs the provisioned `chromium-1246` and
Playwright's Firefox 156.0 downloaded into `b2\browsers`. A run is a new
measurement with a date of its own.

## How the stored copies differ from the ones that ran

The two SPDX lines were added to every `.cs`, `.ps1`, `.sh` and `.mjs` file. The
app copies are stored as their added files and a patch; the lock-file changes
the variants caused are not in the patch, and the rest of the copy was the
product's own source at the time.
