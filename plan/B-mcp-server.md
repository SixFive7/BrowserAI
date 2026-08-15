<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# B. Be the MCP server

stdio transport. Registered once at system or user scope, available in every repository, with no per-repo files.

**The protocol split is solved by configuration, not code.** `@playwright/mcp` 0.0.79 caps at `2025-11-25`; the current spec is `2026-07-28`, a breaking rewrite (removes `initialize`/`notifications/initialized`, adds `server/discover`, replaces server→client requests with the MRTR retry pattern, deprecates Roots/Sampling/Logging). The .NET SDK implements every revision from `2024-11-05` through `2026-07-28` and shipped 2026-07-28 support **on the spec's release date**. So the newer-upward/older-downward split is two properties:

```csharp
McpServerOptions.ProtocolVersion = null;          // upward: accept 2024-11-05 … 2026-07-28
McpClientOptions.ProtocolVersion = "2025-11-25";  // downward: pin to the child's ceiling
```

**The second line is not optional.** Left at `null`, the client probes the child with `server/discover` first, bounded by `DiscoverProbeTimeout` — **5 seconds by default**. If the child silently drops the unknown method instead of returning an error, *every child spawn costs a flat 5 s* against a ~300 ms baseline. It would present as "browser automation got slow," with no error anywhere. Pinning the client version skips the probe entirely ([kb: the protocol split](../kb/mcp/protocol.md#the-protocol-split)).

Assert on the negotiated version at startup. The child never *rejects* a version — it caps or echoes silently (verified: offering `1999-01-01` returns `2025-11-25` with no error), so a mis-negotiation produces nothing to catch. The child's ceiling, the shape of the `2026-07-28` rewrite and the SDK's revision coverage are in [kb: the protocol split](../kb/mcp/protocol.md#the-protocol-split).
