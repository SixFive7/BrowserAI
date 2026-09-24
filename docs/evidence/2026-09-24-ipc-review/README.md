<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-24 -- the IPC review: torn reads, pipes, the census and a single instance

**What this is.** The measurements behind Q268 and Q284: how a per-server record
read from a file tears, what a per-server named pipe costs and what a client sees
when one fails, what the census answers against a hung server, and how a second
start finds the first. Taken on 2026-09-24 between 11:46Z and 12:37Z on Windows 11
Pro 10.0.26200, AMD Ryzen 9 5950X (32 logical), 128 GB, .NET SDK 10.0.401,
NativeAOT win-x64, with Defender real-time protection on, by a read-only reviewer
whose prototype stood in for BrowserAI servers. **167 files with this README,
180 KB.** The maintainer's decision it fed, verbatim: *"Q284 a"*.

Every stand-in server held its live marker exactly as `LiveInstances.Join` does --
`CreateNew`, `ReadWrite`, `FileShare.Read`, buffer size 1 -- and every child the
prototype started was created with `CreateNoWindow`, so no run put anything on the
screen.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: the per-server pipe](../../../kb/windows/processes.md#a-per-server-named-pipe-answers-from-memory-and-cannot-tear----measured-2026-09-24) | Every number in that section and its six sub-sections |
| [`ServerPipeProtocol.cs`](../../../src/BrowserAI.Core/Coordination/ServerPipeProtocol.cs) | `CallBound`'s derivation: the walk-100 percentiles, and the no-checksum argument from the torn reads |
| [`NamedPipes.cs`](../../../src/BrowserAI.Core/Interop/NamedPipes.cs) | The size of the raw call against the framework's, and the default DACL |
| [`ServerPipeClient.cs`](../../../src/BrowserAI.Core/Coordination/ServerPipeClient.cs) | The census against a gone server and a hung one |
| `ServerPipeTests` | The DACL, flag and first-instance readings its arms assert, and the failure shapes it plants |

## What is here

| Path | What it holds |
|---|---|
| `MEASUREMENTS.txt` | The reviewer's own concatenation of every `run/` file, with the machine and the notes on the two runs that were redone |
| `run/torn-*.txt` | The torn-read runs: every framing at the maximum rate and at 10 and 100 writes a second |
| `run/readlat.txt`, `run/walk-*.txt` | What reading one record and walking 20 and 100 servers cost, per mechanism |
| `run/pipefail.txt`, `run/pipefail-listener.txt` | A server dying mid-answer, hanging after accept, never listening again, killed, and eight clients at once |
| `run/micro-census-touch.txt` | Description sizes and build costs, and the census against a hung listener and a killed holder |
| `run/sddl.txt` | The DACLs Windows gives a pipe and a `Global\` event, the reject-remote flags from both ends, and the first-instance refusal |
| `run/single.txt`, `run/single-*/` | The single-instance rounds: one file per contender, pid-named, three rounds of twenty for each shape, and the start after the winner was killed |
| `run/derive.txt`, `run/jobreport.txt` | Reading a process's working directory and parent from outside it, and the job a started process found itself in |
| `proto/` | The prototype's source, its project and its three build files; `editorconfig.txt` is its `.editorconfig`, renamed so it cannot apply to anything here |
| `size/` | The six NativeAOT size probes, the script that built them over `git archive` of `40df5db`, and its logs |
| `cwdcount.py` | The read-only count of installed-server starts whose working directory is a repository, run by the writer on 2026-09-24 |

## What was cut

| Left out | Why | SHA-256 of what it was |
|---|---|---|
| `bin/proto/IpcProto.exe`, 3,601,920 bytes | A build output; `proto/` rebuilds it | `9533e0259f850031da1466e61cdc5ec26c0e1fba77c864fa68e2b4921eb6bd33` |
| `bin/proto/IpcProto.pdb`, 15,781,888 bytes | Its symbols | `9d4e970c36dcdb151bd7bdfd54afc17b5b3fe452050ffa42bdcf6d374a135264` |
| `proto/bin/`, `proto/obj/` | Build outputs and restore state | -- |
| `size/tree-*` | The six `git archive` trees the size probes were published from; `size/build-variants.sh` recreates them | -- |
| `libuv-process.c`, 44,940 bytes | A copy of upstream libuv's process source the reviewer read; upstream is the record, not this copy | `4b9975bde42483f8915dff79c3d2a6f2da58374ac4dbad0983be0bb94442c8fe` |
| `run/single-*-crash/` | Six empty directories: no contender crashed | -- |

⚠️ **Two lines were added to the code files, and nothing else about them moved.**
Every `.cs` file here and `size/build-variants.sh` carry the two-line SPDX header
this repository requires of every source file it tracks, prepended when the batch
was cut; the prototype below them is the prototype that ran.

⚠️ **The review's own report is not here.** It went to the session that
dispatched it, and its conclusions are recorded in that session's ledger and in
[`DECISIONS.md`](../../../DECISIONS.md). Two figures in the kb came from the report
and not from a file in this batch: the inventory's count of **17**, whose list is
not preserved, and **143 of 162** installed-server starts in a repository, which
the writer re-read with `cwdcount.py` the same day and recorded beside it.
