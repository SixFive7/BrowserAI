<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-11-17 - first-run provisioning at chromium 1254 and firefox 1573

**The re-establishment of [re-verification row 21](../../../kb/re-verification.md)
on the `@playwright/mcp` 0.0.83 -> 0.0.85 roll**, taken in the batch that adopted
the roll, because the product quotes these figures:
`BrowserProvisioner.FirstRunDownloadBytes` is what every caller refused during
provisioning reads. The rig is
[`docs/probes/2026-09-16-provisioning`](../../probes/2026-09-16-provisioning/README.md);
the figures it produced are in
[kb: first-run provisioning](../../../kb/playwright/provisioning-and-timings.md#first-run-provisioning)
and
[Firefox, measured the same way](../../../kb/playwright/provisioning-and-timings.md#firefox-measured-the-same-way----2026-08-19).

## What is here

| File | What it is |
|---|---|
| `chromium-1.json`, `chromium-2.json` | Two clean Chromium provisions into an empty root, each the probe's own output: exit code, stopwatch seconds, total bytes and files, and the per-component breakdown |
| `firefox-1.json`, `firefox-2.json` | The same two runs for Firefox |
| `download-urls.json` | What `Get-DownloadUrls.ps1` derived from the adopted payload's **own** `browsers.json`, so that no revision in the wire figures was typed |
| `wire-content-length.txt` | A `HEAD` on each of those four URLs, following the CDN's redirect, and the two family totals summed from them. This is where `217,612,545` and `134,029,725` come from |

**Nothing is cut and nothing is trimmed.** The six data files are whole and are
exactly what the rig and the `HEAD` loop wrote, with this repository's LF
normalisation and nothing else, so there is no digest of an original to record.

⚠️ **What is deliberately NOT here: the installer's own output**, for the reason
the 2026-09-21 batch gives: upstream colours its download line with ANSI escape
bytes, which this repository refuses in any text file. What it said, with the
escapes removed for reading only, is *"Downloading Chrome for Testing
158.0.8127.18 (playwright chromium v1254) from
https://cdn.playwright.dev/builds/cft/158.0.8127.18/win64/chrome-win64.zip"*,
which is the URL `download-urls.json` derived, to the character. That line was
read from the log of the install that put the same revision into the shared
browsers root the same day, under the suite lock, at 15:22:57Z.

⚠️ **The Chromium `HEAD` was answered through a redirect**: `cdn.playwright.dev`
answered 307 with an empty body and a `Location` on `storage.googleapis.com`, whose
200 carried the length. A `HEAD` that does not follow it reads no length at all;
the loop followed it, and the length is the one the final answer carried. Whether
the 2026-10-03 reading met the same redirect is not recorded.

## What the run found

**Both browsers are new browsers.** Chromium 1254 carries `browserVersion`
**158.0.8127.18** where 1247 carried 155.0.8059.12: 215,820,284 B on the wire
against 207,283,631 (`+8,536,653`), and `chromium-1254` holds **471,925,977 B
across 307 files** against 458,240,242 across 307. Firefox 1573 is **159.0**
where 1553 was 156.0: 132,237,464 B on the wire (`+2,842,008`) and 370,295,464 B
on disk (`+8,490,010`), with one file more, `firefox\notification-helper.exe`.

**`ffmpeg` moved too**, 1011 -> **1013**: 1,663,577 B on the wire (`+251,836`) and
4,025,246 B on disk (`+507,904`) at the same four files. `winldd` 1007 is
byte-identical, which is the control.

**Both pairs came back identical to each other** in every byte and file count,
which is the property the procedure asks for before a figure is recorded.

`.links` is 81 B in all four runs, as on 2026-10-03.

## What it touched

**The scratch roots it was given and nothing else.** Each run emptied
`.work\pw-scratch\prov\root-<family>-<n>` and provisioned into it, at below-normal
priority, one run at a time. Nothing read or wrote `%LocalAppData%\BrowserAI` or
`%LocalAppData%\BrowserAI.app`, and `TEMP` was left alone. No browser was started.

## What is not here

**No per-phase breakdown**, for the instrument reason the rig's README records.

**No third or fourth run.** The stopwatch seconds -- Chromium 20.59 and 16.98,
Firefox 10.30 and 9.55 -- are `[MACHINE]` figures, taken while other sessions were
working on this machine.
