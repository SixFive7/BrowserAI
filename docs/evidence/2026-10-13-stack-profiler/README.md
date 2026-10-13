<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-13 -- Chrome for Testing's stack sampling profiler and the browser it crashes

Cited by
[Chrome for Testing samples its own stacks, and the sampler can crash the browser](../../../kb/playwright/configuration.md#chrome-for-testing-samples-its-own-stacks-and-the-sampler-can-crash-the-browser----measured-2026-10-13)
and by the decisions of
[the browser's own stack sampling profiler](../../../DECISIONS.md#the-browsers-own-stack-sampling-profiler-decided-2026-10-13).
The rig is [`docs/probes/2026-10-13-stack-profiler`](../../probes/2026-10-13-stack-profiler/README.md).

| Path | What it holds |
|---|---|
| `release-red/arm-output.txt` | Lines 15 to 126 of the release run's log `rel-ps-2-1.log`, at `44368995` on 2026-10-12: the red of `ScreenshotLimitTests.AgainstARealChromiumAPageOnePixelPastTheLineIsRefusedAndOneAtItIsNot` with the arm's own output, Playwright's browser log, event 14 and the child's lines after the end |
| `release-red/kept-folder-listing.txt` | Every file of the rig 94 a kept, `rig-f54e7cd919324a5eb361954ff5082992`, with its size and time, among them the dump |
| `release-red/session-record.txt` | The session's `browserai.data` read through SQLite: its statements, `closed` with `BrowserCrashed` and -1073741819 among them, and the rows of its log |
| `release-red/playwright-mcp-48F4177FEE82EA11.chromium-logging.json` | The config the arm's child was handed, with the Chromium arguments BrowserAI wrote and the rig's `--enable-logging=stderr` |
| `release-red/mdump.txt`, `release-red/threadnames.txt`, `release-red/dumpsig.txt` | The dump read by the rig's own readers: its stream directory, the exception and its context, the modules with their PDBs, Crashpad's annotations, and every thread with its name |
| `release-red/cdb-stack.log`, `release-red/cdb-context-and-threads.log`, `release-red/cdb-unwind-info.log`, `release-red/cdb-stack-copy.log` | The dump read with `cdb` 10.0.29661.1004 against Microsoft's public symbols: the faulting stack and the unloaded modules; the disassembly at the fault, the context being unwound and every thread's stack; the unwind data and disassembly of the function at RVA 0x557D080 and the arguments on the stack; and what the dump holds of the copied stack |
| `ab/results.jsonl`, `ab/summary.txt`, `ab/node.log`, `ab/runner.log` | The A/B of 2026-10-12T22:27:38Z to 22:36:03Z: one line per launch, the table `summarize.py` and `stats.py` printed, the rig's whole output with Playwright's browser log, and the lock wrapper's lines |
| `ab/cdb-launch-000.log`, `ab/cdb-launch-006.log`, `ab/cdb-launch-012.log` | Three of the A/B's five dumps read with `cdb`: the same fault site, with the context's instruction pointer at RVA 0x557D48C in two and at RVA 0x54E4350, another function's `vzeroupper` before `ret`, in the third |
| `epilogues.txt` | `epilogues.py` over the provisioned `chrome.dll`: the counts of `pop <register>; vzeroupper; ret` and the bytes at the crash site |
| `application-log.txt` | The Application log from 22:00Z to 22:40Z on 2026-10-12, with its positive control |
| `chromium-source.txt` | The Chromium source the kb section reads, at 155.0.8059.12: each file's path, digest and size, and the lines read; and the code searches with their controls |
| `reports/posted-body.md.txt`, `reports/playwright-title.txt`, `reports/gh-create.log`, `reports/playwright-issue.md.txt` | The Playwright issue [microsoft/playwright#43327](https://github.com/microsoft/playwright/issues/43327) as it was posted on 2026-10-13 at 00:40:38Z, its title, the link `gh` printed, and the draft before it |
| `reports/chromium-comment-draft.md.txt` | The comment drafted for the maintainer to post on [Chromium issue 473587142](https://issues.chromium.org/issues/473587142), with the repro, the counts over three Chromium builds and the stacks. ⚠️ *Corrected 2026-10-13 by addition (previously the sentence before, with nothing after it)*: never posted as a comment. By the maintainer's 134 b the report went out as a new issue that links 473587142, [572877174](https://issues.chromium.org/issues/572877174), whose fields are in the row below, and the draft stays as it was |
| `reports/chromium-issue-summary.txt`, `reports/chromium-issue-steps.txt`, `reports/chromium-issue-description.txt`, `reports/chromium-issue-comment.txt`, `reports/gh-comment.log` | [Chromium issue 572877174](https://issues.chromium.org/issues/572877174) as it was posted on 2026-10-13 at 01:26:48Z through the tracker's new-issue form, field by field: the summary, 94 characters; the steps, 1,889; the description, 2,276; and the additional comments, 4,040, which the tracker shows inside the description; each the length the form read back before it was submitted. Then the link `gh` printed for the comment of 01:29:06Z on [microsoft/playwright#43327](https://github.com/microsoft/playwright/issues/43327) that points at it. *Added 2026-10-13* |
| `reports/cdb-*.txt`, `reports/mainlive_ret.txt`, `reports/dumpthreads.py`, `reports/check-text.py`, `reports/cl8482885.json` | The reports' own readings: `cdb` over the dumps of 155, 156 and 157, the main thread's return addresses, the thread reader and the text check the drafts were put through, and CL 8482885 as Gerrit served it |
| `plants/stack-profiler-red.log` | `StackProfilerTests` filtered against the tree with the switch declared and not passed, 2026-10-13T00:18:39Z: red, the browser's threads listing `StackSamplingProfiler` beside `CrBrowserMain` |
| `plants/stack-profiler-green.log` | The same arm with the switch passed, 2026-10-13T00:47:02Z, filtered beside `ConfigRoundTripTests` against the published binary and the record classes: the arm and the round trip green, and two reds of this batch's own first copies, three Markdown drafts with no header and a log with control bytes, which the departures below answer; the record classes then read 83 of 83 at 00:49:37Z |

**Not here: the fifteen minidumps**, each a binary of about 2.8 MB that holds memory of the
browser process it was taken of. Their digests, all SHA-256:

| Dump | Bytes | SHA-256 |
|---|--:|---|
| the release red's, `49ec8e29-f6d3-4f4d-a02c-c50f5d705d46.dmp` | 2805056 | `3de082ccb6cf31955eaf97416c273b8b640fe21895e4b45582bc9c6d19b6d903` |
| launch 0, `dc8d64ce-0c24-4a41-85b8-ca03c4515327.dmp` | 2758816 | `f93854014b1370dc19f15e55458d77b97d27a48b3c65b650531faf0e71995458` |
| launch 6, `7e5cea7a-06a8-4161-bf83-af25ad7dcefa.dmp` | 2811248 | `f1cff88dbb0a9bc9ef17b396910be4da90f0570610495931bf08586104536e23` |
| launch 10, `b2876c2b-027d-4ef4-b53f-5262c8a6cd4c.dmp` | 2849456 | `d3e5cd39c3713629b8bef8826a78cc6a980fff7804f619c0632869fe118e3151` |
| launch 12, `e133eff3-4a60-42d5-8d6b-5bd7dde7d00f.dmp` | 2792160 | `f5728d3e28c92a259e87990c40d66b229c920536086b330e1d06287e14bcb3e2` |
| launch 16, `a26ee774-33d4-408d-be37-34ac27a32ad8.dmp` | 2794432 | `2fa9e8fd4bc673334fff754646dbeae87e20b7b6a1572db192e1a256dae6f6e5` |
| the reports' 155.0.8059.12, `13ef36a8-56da-416f-981a-1cc3f3b435e3.dmp` | 2782032 | `31260ac5f7ff826a0c771fe59ba26ec0d4d898ca90ae626ea258297807ca3245` |
| the reports' 155.0.8059.12, `69874206-86e3-4694-9666-226b00a9d770.dmp` | 2805264 | `741bd8f65df0e4bee64599297dd12df61ad4a166f54a67ab822f25e39bbc4f75` |
| the reports' 155.0.8059.12, `a1f72319-3e53-406c-82e5-0aa094e4c0a1.dmp` | 2795248 | `eea97ddcb7ce68380a24cf82a16fb920cfda9c4011d189acf0808435f3b8da74` |
| the reports' 156.0.8078.4, `91c67610-64a4-44fb-8076-8160e211a864.dmp` | 2846880 | `3a37c91b24cdae755b203706a4117d3e80d582cff114edb316e2851daac3fb47` |
| the reports' 156.0.8078.4, `c603a113-5acf-40f6-96d1-441219ccc841.dmp` | 2774016 | `8844fe45ad378a48c0344c483f510d460ea90ca888f60d46bbb3fa408e0acc49` |
| the reports' 157.0.8101.0, `0ba3716b-87b5-4456-a426-ec699946c6cd.dmp` | 2829616 | `0991b1539d05c607cc00660ff4645ae8f51c99fcab37d469942c65b1ec5f5c74` |
| the reports' 157.0.8101.0, `0bab6708-16a7-4ff1-bcf9-9426c064124f.dmp` | 2869072 | `2052b1a32d9bcbc238e416d2b3651155209c33e5ef50bd183f3e8fcef9e1c088` |
| the reports' 157.0.8101.0, `9a46afe9-85ea-4ae5-a0f0-bf7e5ddc2e54.dmp` | 2835456 | `140b75a4de6746f8d3140ed513591efa792800d6e19d1d03e35e85e28c639542` |
| the reports' 157.0.8101.0, `a27c62ff-5ff3-4b40-8679-70009c1696fb.dmp` | 2789776 | `77a4d1b0f8bb4ec10c80eb445c9658793a5c4395372b893758fb9b1e57f7dbe1` |

**Every other byte is as taken**, but for the line endings `.gitattributes` normalises, and
every path in them names this machine's profile and the trees the runs were taken in,
uncut. Two departures, both in the reports' folder: its three Markdown drafts carry a `.txt` after
their names, so that no header has to be written into what was posted, and its log of the
install of Playwright 1.64.0's Chromium is left out, because its progress bars are terminal
control bytes.
