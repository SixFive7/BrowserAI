<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-23 / 2026-09-24 -- Playwright's server registry: growth, reaping and what reading it costs

**What this is.** Six probes behind T7: whether the descriptor directory
`playwright-core` writes for every launched browser is ever pruned, how fast it
grows here, what `ServerRegistry.list()` reaps, whether a concurrent caller can
reap a **live** descriptor, and what reading a registry the real size costs.
**18 files, 89,296 bytes.** Taken against the repository's own payload
`playwright-core` **1.64.0-alpha-1789764292000** and Chromium **154.0.8037.0**
(revision 1246), node **v24.21.0**.

⚠️ **The real registry was read and never written**, in every probe. Every
planted descriptor, every reap and every timing ran inside a scratch directory
named by `PWTEST_SERVER_REGISTRY`, seeded with **copies** of the real ones, and
each probe prints the real directory's count at its start and its end so the
claim is in the log and not in this sentence: `3761`, `3762` or `3815` before and
the same after, in all six. `probe-a` is the one exception by design -- it
launches a browser against the real directory and deletes **only** the descriptor
that launch created, named in the log by GUID.

## Cited by

| Record | What it takes from here |
|---|---|
| [`DECISIONS.md`](../../../DECISIONS.md#processes-browsers-and-session-modes) | T7: the detached `list` call at session close, the growth rates, the cost curve and the three accepted risks |
| [kb: tools and artifacts](../../../kb/playwright/tools-and-artifacts.md#every-launched-browser-leaves-a-descriptor-in-localappdatams-playwrightb-and-nothing-reaps-it----measured-2026-09-16) | The correction that a harness **can** move the directory, the growth rates, the reap semantics, the concurrency result and the cost curve |
| [re-verification index](../../../kb/re-verification.md) | The row keyed on the `playwright-core` version |

## What is here

| File | What it establishes |
|---|---|
| `probe-a.cjs` / `probe-a.log` | The descriptor lifecycle against the real directory: a persistent-profile browser leaves its descriptor behind after `close()`, and `isConnected()` is false while the file stands |
| `probe-b.cjs` / `probe-b.log` | `PWTEST_SERVER_REGISTRY` **does** move the directory: the descriptor appears in scratch, the real path has none, and the real count does not move |
| `probe-c.cjs` / `probe-c.log` | What `list()` reaps: 6 of 7 planted dead descriptors unlinked, the one live descriptor survived, and the page still worked afterwards |
| `probe-d.cjs` / `probe-d.log` | The first attempt at the cost-and-race question, at the real size. Superseded by `probe-e`, kept because it is where the shape of the measurement came from |
| `probe-e.cjs` / `probe-e.log` | The two halves separated. **E1**: one process over 3,762 copies -- watcher ready 19,088 ms, `list()` 141,616 ms, 2 connectable, 3,760 reaped. **E2**: eight concurrent `list()` callers over 40 dead and 1 live -- the live descriptor survived all eight, 738 ms wall |
| `probe-f.cjs` / `probe-f.log` | The cost curve at five sizes: 125, 250, 500, 1,000 and 2,000 entries, each with the watcher-ready and `list()` halves timed separately |
| `dashboard.png`, `dashboard-after-reload.png`, `dashboard-url.txt`, `dashboard-demo.log`, `dashboard-demo-relaunch.log`, `dashboard-shot.log`, `dashboard-shot2.log` | The Playwright dashboard the maintainer asked to see, and the reload that hangs its session list. The rig that produced them is a probe record at [`docs/probes/2026-09-24-playwright-dashboard`](../../probes/2026-09-24-playwright-dashboard/README.md) |

## What was cut

**Nothing was trimmed.** The two demo browser profiles and the scratch registries
the probes created are not here, for the reason every profile tree is absent from
this directory: they are the rig's working state and none of them is read by a
finding.

## Corrected 2026-10-01: what the dashboard files show

⚠️ *Corrected 2026-10-01 by addition (previously, in the last row of the table
above, "The Playwright dashboard the maintainer asked to see, and the reload that
hangs its session list", over seven file names).* Checked against the files as
committed, while the dashboard measurements of 2026-09-25 were being written up.
Nothing in this directory was changed.

- **Five of the seven files are here and two never were.** `dashboard-demo.log`
  and `dashboard-demo-relaunch.log` are in no commit on any branch of this
  repository. The 18 files and 89,296 bytes counted at the top are the 18 that
  are here, this README among them.
- **The two screenshots are one file.** `dashboard.png` and
  `dashboard-after-reload.png` are 24,397 bytes each, one git blob, SHA-256
  `d0f351087250435163d92b2e3ad87278d9cb777516924769cecf3886f8a18182`.
- **Neither capture is of a reload.** `dashboard-shot.cjs` in the probe record
  goes to the URL once and takes one screenshot. It never reloads; the word is
  in it once, in the name of the file it writes. `dashboard-shot.log` is a run
  that wrote `dashboard.png`, and `dashboard-shot2.log` is a run that wrote
  `dashboard-after-reload.png`, which is the name the stored script carries.
  Both went to the same URL, and both print a session list that filled: the
  demo session and its two tabs.
- **`dashboard-url.txt` is from another start.** It holds port 61165, and both
  logs are of port 55913.
  [The session's ledger](../../ledger/2026-09-23-development-session.md) records
  a relaunch of the demo at 61165, with the second of the two missing logs as
  its log.

So no file here shows a reload, hung or otherwise. What a reload does was
measured on 2026-09-25: a lone tab, reloaded, listed its sessions 20 times out
of 20, and the viewer that stops updating is one that stays while another
leaves. That
is in
[`2026-09-25-dashboard-exposure`](../2026-09-25-dashboard-exposure/README.md)
and in
[the kb](../../../kb/playwright/tools-and-artifacts.md#a-reload-works-and-a-viewer-that-stays-stops-updating-when-another-leaves).
