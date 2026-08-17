// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using ModelContextProtocol.Client;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The timeouts and protocol constants every in-process rig shares, declared
/// once so that two of them cannot disagree.
/// </summary>
internal static class TestDefaults
{
    /// <summary>
    /// How long any single in-process exchange may take before it is called a
    /// hang.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Generous on purpose. This layer normally answers in single-digit
    /// milliseconds, so this is not a performance budget — it is the boundary
    /// between "slow machine" and "nothing is ever coming", and a test that
    /// trips it has found a deadlock rather than a busy CI agent.
    /// </para>
    /// <para>
    /// ⚠️ <b>"Single" is load-bearing, and until 2026-08-17
    /// <see cref="RawPipeClient"/> did not honour it</b>: it armed one
    /// <see cref="CancellationTokenSource"/> of this length in its constructor
    /// and passed that same token to every frame read for the life of the
    /// client. A conversation of forty quick exchanges therefore died at thirty
    /// seconds however promptly each one was answered — and died as a bare
    /// <c>OperationCanceledException: The operation was canceled.</c>, naming
    /// neither the method nor the elapsed time. Measured under full parallelism:
    /// it took out <c>AnIdleSessionLosesItsBrowserKeepsItsNodeChildAndTheNextCallStillWorks</c>
    /// at 34.7 s and <c>ItDeletesTheTreeAndDownloadsItAgainWhenNothingIsRunning</c>
    /// at 30.0 s, both of which spend most of their time legitimately waiting on
    /// a real browser. <see cref="RawStdioClient"/> keeps a whole-conversation
    /// budget on purpose and says so in its own remarks; this one is per
    /// exchange, which is what its name always claimed.
    /// </para>
    /// </remarks>
    public static TimeSpan Patience { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The <c>server/discover</c> probe timeout every test client pins.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pinned short, and deliberately in the opposite direction to the SDK's
    /// own fixtures.</b> Upstream pins this longer, citing
    /// <see href="https://github.com/modelcontextprotocol/csharp-sdk/issues/1701">csharp-sdk#1701</see>,
    /// because CI slowness was tripping the probe against servers that do
    /// answer it. Here every peer is an in-process double that answers
    /// immediately, so a probe that ever runs to its timeout is a defect in a
    /// double — and the cheapest way to find it is to make it cost 250 ms
    /// rather than five seconds.
    /// </para>
    /// <para>
    /// The value only has an effect when the client prefers <c>2026-07-28</c>,
    /// that is, when <see cref="McpClientOptions.ProtocolVersion"/> is left
    /// null. Production pins an initialize-capable revision and issues no probe
    /// at all, which is the property
    /// <c>FakeChildHarnessTests.TheClientPinIsWhatSkipsTheDiscoverProbe</c>
    /// asserts from both sides.
    /// </para>
    /// </remarks>
    public static TimeSpan DiscoverProbeTimeout { get; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The revision a test client offers <b>BrowserAI</b>.
    /// </summary>
    /// <remarks>
    /// Not <c>2026-07-28</c>: that revision removes <c>initialize</c>, and the
    /// caller-facing half of the protocol split exists to prove BrowserAI
    /// answers whatever the caller offers rather than whatever the child
    /// negotiated.
    /// </remarks>
    public const string CallerProtocolVersion = "2025-11-25";

    /// <summary>
    /// The highest revision <see cref="FakePlaywrightChild"/> will negotiate,
    /// matching the ceiling measured on <c>@playwright/mcp</c> 0.0.79.
    /// </summary>
    /// <remarks>
    /// A provenance stamp rather than a target. The double caps at the same
    /// place the real child does, and — like the real child — it never rejects
    /// a version, it caps or echoes.
    /// </remarks>
    public const string ChildProtocolCeiling = "2025-11-25";

    /// <summary>Client options with the probe timeout pinned.</summary>
    /// <param name="protocolVersion">
    /// The revision to pin, or <see langword="null"/> to leave the client on
    /// its dual-path default — which is the mode that issues the probe.
    /// </param>
    /// <returns>Options carrying the pin and nothing else surprising.</returns>
    public static McpClientOptions ClientOptions(string? protocolVersion) => new()
    {
        ClientInfo = new ModelContextProtocol.Protocol.Implementation { Name = "BrowserAI.Tests", Version = "1" },
        ProtocolVersion = protocolVersion,
        DiscoverProbeTimeout = DiscoverProbeTimeout,
    };
}
