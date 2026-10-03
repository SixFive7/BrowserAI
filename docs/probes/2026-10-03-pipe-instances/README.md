<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 -- how many callers one pipe of ours can hold at once

Establishes
[A pipe created with `PIPE_UNLIMITED_INSTANCES` holds more than 255 callers](../../../kb/windows/processes.md#a-pipe-created-with-pipe_unlimited_instances-holds-more-than-255-callers----measured-2026-10-03).
Evidence:
[`docs/evidence/2026-10-03-pipe-instances/`](../../evidence/2026-10-03-pipe-instances/README.md).

**Why it exists.** BrowserAI's pipes create every instance with
`nMaxInstances` 255, and its remarks read that as Windows' own ceiling. The
maintainer expects more than 255 sessions in flight at once, and the
coordinator's pipe was about to carry them, so the number was measured
before anything was built on it.

## What is here

| File | What it does |
|---|---|
| `instances.cs` | A .NET file-based program. It creates a pipe with exactly the arguments `src/BrowserAI.Core/Interop/NamedPipes.cs` passes to `CreateNamedPipeW`, listens the way `ServerPipe` does -- one listening instance, a client connects, the next instance is made, the connected one is kept -- and opens each client the way `NamedPipes.OpenClient` does. For each target it holds that many connections at once, connects one more caller, sends bytes both ways through the last held connection, and prints how many it held and whether anything was refused. `--max <n>` replaces 255 with a real cap, which is the positive control |

## What keeps it off the rest of the machine

- One process and one pipe name per target, a fresh GUID each time, and every
  handle is closed before the next target starts. It starts no other process.
- **It selects no process at all**, by name or otherwise: none of the eight
  spellings
  [`ProcessSelection`](../../../tests/BrowserAI.Tests/Harness/ProcessSelection.cs)
  keys on is in the file, by a search that found one in
  `2026-09-14-firstrun/observe.ps1`, the positive control.
- It stops at 2,000 connections. Where system resources run out was not looked
  for, on a machine other work shares.

## Running it

From this directory, with the .NET 10 SDK:

```
dotnet run instances.cs -- 300 600 1000 2000
dotnet run instances.cs -- --max 254 300
```

The repository's `.editorconfig` applies to a file-based program run from inside
the tree, which is why the local functions are spelled in camel case.

## How the stored copy differs from the ones that ran

`run-2.log` and `run-control.log` were taken with this copy, byte for byte.
`run-1.log` was taken a minute earlier with a copy that had no `--max` and
printed times with the machine's decimal comma; its counts are the same
measurement and its numbers are kept as printed.
