<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-17 - Firefox against Chromium, the four cost ratios

The re-establishment named by
[Firefox against Chromium: the standing cost ratios](../../../kb/playwright/provisioning-and-timings.md#firefox-against-chromium-the-standing-cost-ratios),
which is [re-verification row 34](../../../kb/re-verification.md).

**This rig exists because the previous one did not.** The 2026-08-14 figures
came from a session whose harness was not preserved, so the entry carried
`[UNVERIFIED]` as to method and told the reader to re-measure before any
decision turned on them. Keeping the rig is the whole point of this directory.

`ratios-probe.js` opens one session per family through the product's own
`browserai_init`, drives the same navigation against a local origin
(`page-server.js`), and records:

- **resident set** -- `WorkingSet64` summed over every process whose
  `ExecutablePath` is under the browsers root;
- **wall time to first paint** -- the first `browser_navigate`, which is what
  launches the browser, and a second one with the browser already up so the
  launch half of the first is visible;
- **idle CPU** -- `TotalProcessorTime` across a 30-second window with no page
  activity at all. ⚠️ **This axis is RETIRED as not established, 2026-09-18
  (Q215 = a), and the rig still prints it.** Added here rather than removed
  because this directory is a record of what was run: two sign reversals across
  three readings, distributions that overlap completely, and a control --
  Chromium byte-identical across 1244 and 1245 -- that moved **−41%** on this axis
  while moving 0.9% on resident set. **A number this rig prints for it is not an
  axis re-established**; see
  [not-established](../../../kb/not-established.md);
- **profile directory size** on disk, in bytes and files.

`mcp.js` is the same minimal stdio JSON-RPC client the resume rig uses.

## Run it three times per family, not once

The 2026-09-17 measurement is **three rounds per family**, and the reason is in
the numbers: first-navigate ranges 413-1,297 ms for Chromium and
1,907-2,962 ms for Firefox, so the per-round ratio spans **1.47× to 7.16×**
around a 4.62× median. One pair supports any answer in that band. The
profile-disk axis is the opposite -- 2.76× on all three rounds, varying by under
a kilobyte -- and is the only one worth quoting to three figures.

One family per process run, sequentially, so one family's idle window is never
measured beside the other's browser.

## What it touches

The session directories it is given and the product's shared data root. Each
session is destroyed through `browserai_destroy` on the way out. It never
touches `%LocalAppData%\BrowserAI.app`. Both families must already be
provisioned or the first call will start a download.

⚠️ **THE DESTROY ON THE WAY OUT TAKES THE SESSION LOG WITH IT, SO A ROUND THAT
GOES WRONG CANNOT BE DIAGNOSED AFTERWARDS.** *Added 2026-09-22 by addition,
after it cost exactly that.* On the 2026-09-22 sitting **one Firefox round in
nine produced no browser at all**: `browserai_init` answered in 459 ms, both
navigations returned only after ~180 s, zero processes were under the browsers
root and the profile stalled at 1,159,208 B against ~36.47 MB. The product's own
stray sweep was excluded from its own announcements -- all fifteen sweeps in the
sitting reported `candidates=0` -- and **what it actually was is not
established**, because the session's own log had already been deleted by the
probe's own teardown. The shared process log carries the server's startup, its
sweep and the destroy, and nothing in between: per-session events go to the
session. **Nothing here is changed to fix that**, because this directory is a
record of the method the recorded measurements were taken with, and changing the
rig would falsify it. What a future run should do is keep the session directory
when a round returns no processes, and that is a change to make deliberately
rather than a line to slip in.

⚠️ **`TotalProcessorTime.TotalMilliseconds` is formatted as an integer on
purpose.** PowerShell's `-f` uses the current culture, and on a machine with a
comma decimal separator the double arrives as `123,456` and parses as `NaN` on
the other side of the pipe -- which is how the first run of this probe reported a
null idle-CPU figure for both families.

| Trips `NeverByImageNameTests` | **No** -- *corrected 2026-09-17 (previously "Yes -- `Get-Process`, filtered on `Path` under the browsers root and never on a name")*. Same code, a different scan: it reads the FILTER rather than the API from 2026-09-17 (Q203), and a bare `Get-Process` piped into a `Path` test names no image |
|---|---|
