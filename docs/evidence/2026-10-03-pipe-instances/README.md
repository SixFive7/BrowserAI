<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 -- one pipe holding 300 to 2,000 callers at once

**What this is.** The three runs of the pipe-instance probe, taken on
2026-10-03 between 13:22Z and 13:24Z on Windows 11 Pro 10.0.26300 under .NET
10.0.12: how many connections one pipe created the way BrowserAI creates its
pipes holds at once with `nMaxInstances` 255, `PIPE_UNLIMITED_INSTANCES`, and
the positive control with a cap of 254. **Three files beside this README**, each
the probe's whole standard output. The rig is a probe record at
[`docs/probes/2026-10-03-pipe-instances`](../../probes/2026-10-03-pipe-instances/README.md).

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: processes](../../../kb/windows/processes.md#a-pipe-created-with-pipe_unlimited_instances-holds-more-than-255-callers----measured-2026-10-03) | Every number in the entry |

## What is here

| File | What it is |
|---|---|
| `run-1.log` | Targets 300, 600 and 1,000, taken with an earlier copy of the probe that printed times with the machine's decimal comma |
| `run-control.log` | The positive control: `--max 254` against a target of 300, refused at the 255th instance with `ERROR_PIPE_BUSY` |
| `run-2.log` | Targets 300, 600, 1,000 and 2,000 with the stored copy of the probe, every one held with no refusal |

## What was cut, and what was left out

Nothing was cut and nothing was left out. The pipe names carry a fresh GUID
each, and no file names the user, the profile or the machine: the probe prints
the DACL with the current user's SID replaced by a placeholder.
