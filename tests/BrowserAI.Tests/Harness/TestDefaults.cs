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
    /// Generous on purpose. This layer normally answers in single-digit
    /// milliseconds, so this is not a performance budget — it is the boundary
    /// between "slow machine" and "nothing is ever coming", and a test that
    /// trips it has found a deadlock rather than a busy CI agent.
    /// </remarks>
    public static TimeSpan Patience { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The budget for a whole rig: construct, handshake, one round trip, tear
    /// down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It bounds a whole rig rather than one exchange</b>, which is what
    /// separates it from <see cref="Patience"/>: a rig that constructs, hands
    /// shakes, round-trips and tears down should take single-digit
    /// milliseconds, and one that takes seconds is doing something nobody asked
    /// for.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-16 at build-order step 16 (previously two
    /// seconds, "chosen against a specific failure, not as a benchmark": an
    /// unanswered <c>server/discover</c> costing the client
    /// <see cref="McpClientOptions.DiscoverProbeTimeout"/>, five seconds by
    /// default).</b> That justification does not survive reading
    /// <see cref="DiscoverProbeTimeout"/> beside it: this suite pins the probe
    /// to <b>250 ms</b> for every in-process rig, so the failure the two-second
    /// bound was chosen to catch would add a quarter of a second and the bound
    /// could never have detected it. The mechanism is asserted directly instead,
    /// from three sides, by
    /// <c>FakeChildHarnessTests.TheClientPinIsWhatSkipsTheDiscoverProbe</c> —
    /// which is what <a href="../../../kb/re-verification.md">row
    /// 16</a> records.
    /// </para>
    /// <para>
    /// <b>What the old value did do was fail three times in twelve full-suite
    /// runs</b> — 2.57 s, 2.89 s and 4.20 s against a rig that normally answers
    /// in milliseconds — because the suite runs in parallel and a pool thread
    /// can simply be descheduled. Those are 250–400× the normal figure, so they
    /// are starvation rather than creep, and a flaky test is a red build wearing
    /// a disguise. Ten seconds is above every observed value and well below
    /// <see cref="Patience"/>, so a rig that has genuinely stopped is still
    /// caught here rather than thirty seconds later.
    /// </para>
    /// </remarks>
    public static TimeSpan RigBudget { get; } = TimeSpan.FromSeconds(10);

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
