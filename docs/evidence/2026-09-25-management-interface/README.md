<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-25 -- the management interface: listeners on loopback, a page in two browsers, and the windows that could show it (zoom-out track B)

**What this is.** The record of the zoom-out's track B, which asked what should
replace the configuration window. Two passes, one folder each:

- `first-pass/`, **2026-09-25**: five NativeAOT listener probes, standalone and
  compiled into a copy of `BrowserAI.exe`, and what each answers to hostile
  requests by default; a WebView2 host and an Avalonia window. It recommended
  WebView2, and the maintainer turned that down.
- `second-pass/`, **2026-10-01**: a raw-socket listener with one gate, attacked
  from raw sockets, Chrome for Testing 154.0.8037.0 and Playwright's Firefox
  156.0; how a tab behaves across a reload, a close and a restart; the
  provisioned Chromium as a window of BrowserAI's own over a DevTools pipe;
  Chrome for Testing's idle traffic; and the same window through node and
  playwright-core.

The decision was a tab in the person's own browser (Q315), served by Kestrel from
the empty builder (Q340). **489 files beside this README, 778,692 bytes as cut.**
The rigs are a probe record at
[`docs/probes/2026-09-25-management-interface`](../../probes/2026-09-25-management-interface/README.md).
Nothing here ran on the real desktop.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: a management page on loopback](../../../kb/windows/loopback-page.md) | Every number in the article |
| [kb: re-verification](../../../kb/re-verification.md) | Rows 178 to 181 |
| [kb: not established](../../../kb/not-established.md#browsers-and-the-web-surface) | What the two passes did not run |
| [`DECISIONS.md`](../../../DECISIONS.md#the-management-interface-is-a-tab-in-the-system-browser) | Quotes the second pass's numbers; written before this batch, so it does not link here |

## What is here

| Path | What it is |
|---|---|
| `first-pass/REPORT.md` | The first pass's report: what the interface has to cover, the listener measurements, WebView2 and the native frameworks, the security model, and the directions |
| `first-pass/logs/` | Every publish log and the size summaries, `measure-1.tsv` and `measure-2.tsv` (20 starts per probe after 2 warm-ups), `security-1.txt` and `security-2.txt` (what each listener answered), `hijack-test.txt` (a second registration on the same port) and the WebView2 runs |
| `first-pass/run/` | The port each probe run announced, and the WebView2 runs' outputs |
| `second-pass/REPORT.md` | The second pass's report: the system-browser design and its evidence, the provisioned Chromium and node windows, the local file with a protocol link, the comparison and the open questions |
| `second-pass/notes/` | `index.txt`, which says what each run is, and the switch set the idle and start-up runs used |
| `second-pass/logs/` | Build and publish logs, the in-app sizes, and the driver's output for each run with the job's memory samples |
| `second-pass/run/` | Per run: the instrument's table (`*.result.txt`), page-ready and memory (`*.ready`, `*.mem.txt`), the listener's own log (`*.sprobe.log`), the idle runs' proxy logs (`*.sink.tsv`) and net-log summaries, the restart rounds (`rc*`, `rf*`), and the window and icon captures |
| `second-pass/run/z1.sprobe.log.zip` | The fuzz run's listener log, zipped: the mutated requests it records hold control characters the tree's text rules refuse. The zip holds it byte for byte: 421,425 bytes, SHA-256 `3aaa4bf3ffb30b49c1c9765cc3cbeab6f5d1af3820d600ead76b15ddd4a6cd70` |
| `first-pass/originals.sha256`, `second-pass/originals.sha256`, `second-pass/left-out.sha256` | What was changed, and what was left out with digests |

## What was cut, and what was left out

- **Left out whole and not listed file by file**, because they are packages,
  build output and browser state: from the first pass, the NuGet packages
  (2,254 files, 1,713,631,319 bytes), the published binaries (63 files,
  763,339,225 bytes), the WebView2 SDK package (91 files, 56,895,102 bytes), the
  probes' build output (1,886 files and 1,231,502,150 bytes with the 16 sources
  the rig record keeps), the app copy and its build output (343 files,
  192,773,791 bytes), two WebView2 user-data folders (322 files, 23,561,751
  bytes) and the driver's build output (20 files, 275,985 bytes); from the
  second, Playwright's Firefox 156.0 (71 files, 365,579,913 bytes), the
  published binaries (24 files, 40,476,214 bytes), the app copy (83 files,
  1,110,433 bytes) and three empty folders. The app copies are the product's
  App and Core with a few files added and changed; the rig record keeps those as
  files and a patch.
- **Listed in `second-pass/left-out.sha256`**: the four Chromium net logs, which
  carry this machine's network configuration (the summary of each is kept), and
  37 third-party source files read for the second pass: 24 of Chromium at
  `41d4e764`, 12 of Firefox's `main` at a commit that was not recorded, and one
  of Velopack's `develop`.
- **Changed, under the names in `originals.sha256`**: the two SPDX lines added
  to the two reports; terminal escapes removed from the Firefox install log and
  two Firefox results; this machine's LAN addresses replaced in two net-log
  summaries; and the user profile replaced in one first-pass log, each under a
  `.trimmed.` name.

## Privacy

No file here names the user or the machine. The privacy scan, with a positive
control, found nothing in this batch; its six hits in the rig record are made-up
profile paths, for a user named `x`, in a probe's sample document. During the second pass a
read-only search printed the name of the real Firefox profile folder into the
researcher's own output; that name is in no file here, which the scan's
profile-folder pattern checked.
