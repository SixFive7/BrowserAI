// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using ModelContextProtocol.Client;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The hang detectors and protocol constants every rig in this suite shares,
/// declared once so that two of them cannot disagree.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Every duration below is a HANG DETECTOR, and none of them is a
/// promptness assertion.</b> The distinction is the maintainer's instruction of
/// 2026-08-18, verbatim: <i>"Remove any timings other than timeouts that catch
/// really hung processes. Even on slow systems … these should have ample of room
/// so tests do not hit these even under constrained system resources."</i>
/// </para>
/// <para>
/// So the rule for anything declared here: <b>it must be unreachable by a slow
/// machine.</b> Not "generous", not "an order of magnitude" — unreachable. The
/// suite runs at <see cref="SuiteParallelism.Unbounded"/>, which puts 419 tests
/// and (inside <c>SaturationTests</c>) 100 processes onto one machine at once,
/// and the .NET thread pool grows by about one worker a second past
/// <see cref="Environment.ProcessorCount"/>. Under that, an in-process exchange
/// that normally costs single-digit milliseconds can legitimately be starved for
/// <i>tens of seconds</i>. Measured 2026-08-17 at unbounded, on an unmodified
/// tree, the three most common failures in the whole suite were exactly that:
/// <c>Initialization timed out</c> (46), <c>No frame arrived on this pipe within
/// 30 s</c> (71) and a bare <c>A task was canceled</c> (48). Not one was a logic
/// fault. Every one was a bound of thirty or sixty seconds expiring under
/// starvation and then reporting something other than <i>"this machine is
/// busy"</i>.
/// </para>
/// <para>
/// <b>If a test needs to know that something happened promptly, it must assert
/// on an event and not on a clock</b> — a handle that signals, a process that
/// exits, a file that appears, a gate the test itself releases, or a
/// <see cref="ManualClock"/> it drives. A stopwatch compared against a constant
/// is the defect, not the safeguard.
/// </para>
/// </remarks>
internal static class TestDefaults
{
    /// <summary>
    /// A hang detector for one exchange between two objects <b>in this
    /// process</b>. Five minutes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is not a budget and nothing may assert on it.</b> This layer
    /// answers in single-digit milliseconds, so five minutes is roughly
    /// <b>30,000×</b> the normal cost — the boundary between "this machine is
    /// starved" and "nothing is ever coming". A test that trips it has found a
    /// deadlock. <b>A slow machine must never reach it.</b>
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-18 (previously <c>Patience</c>, 30 s).</b> Thirty
    /// seconds was reached routinely at unbounded parallelism: 71 failures in one
    /// twenty-run session read <i>"No frame arrived on this pipe within 30 s
    /// … The peer is in this process, so this is a deadlock or a dropped write
    /// rather than a slow machine"</i> — a message that was, every single time,
    /// wrong about its own cause. The name moved too: <c>Patience</c> says
    /// nothing about which of the two kinds of duration it is, and that ambiguity
    /// is what let a promptness assertion wear a timeout's clothes for a month.
    /// </para>
    /// <para>
    /// "Single exchange" is load-bearing, and until 2026-08-17
    /// <see cref="RawPipeClient"/> did not honour it: it armed one
    /// <see cref="CancellationTokenSource"/> of this length in its constructor and
    /// passed that same token to every frame read for the life of the client, so a
    /// conversation of forty prompt exchanges died on the fortieth.
    /// <see cref="RawStdioClient"/> carried the same defect until 2026-08-18.
    /// </para>
    /// </remarks>
    public static TimeSpan InProcessHang { get; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A hang detector for something that involves <b>another process</b>:
    /// starting a probe, waiting for the file it writes, watching a tree die
    /// after its job closed. Ten minutes.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Not a budget; nothing may assert on it.</b> A probe starts, writes a
    /// report and exits in well under a second, and <c>KILL_ON_JOB_CLOSE</c> is a
    /// kernel operation whose cost is scheduling latency — so this is several
    /// hundred times the normal cost, and <b>a slow machine must never reach
    /// it</b>. It is longer than <see cref="InProcessHang"/> because process
    /// creation is the single most contended operation on a saturated Windows
    /// box: the saturation test alone starts a hundred of them at once.
    /// </remarks>
    public static TimeSpan ProcessHang { get; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// A hang detector for a <b>real browser</b> coming up under a published
    /// BrowserAI, or for a whole conversation with one. Thirty minutes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Not a budget; nothing may assert on it.</b> A cold Chromium or
    /// Firefox tree comes up in 5–15 s on an idle machine, so this is more than a
    /// hundred times the normal cost, and <b>a slow machine must never reach
    /// it</b>.
    /// </para>
    /// <para>
    /// <b>Strictly larger than anything upstream imposes, and that is a
    /// correctness property rather than slack.</b> Playwright's own
    /// <c>DEFAULT_PLAYWRIGHT_LAUNCH_TIMEOUT</c> is three minutes. A harness bound
    /// at or below that always wins the race and replaces upstream's diagnosis
    /// with <i>"the budget expired"</i> — measured 2026-08-17, a Firefox launch
    /// reported at exactly 3m00s as a bare cancellation with the peer still
    /// running and its stderr empty, which names nothing. Widening this cannot
    /// turn a broken launch green: a browser that will not come up now fails with
    /// Playwright's own message instead.
    /// </para>
    /// </remarks>
    public static TimeSpan BrowserHang { get; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// A hang detector for the MCP <c>initialize</c> handshake, on both sides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Set explicitly because the SDK's default is 60 s and inheriting it
    /// silently is the "same bound at two layers, tighter one winning invisibly"
    /// shape.</b> Measured by reflection on <c>ModelContextProtocol.Core</c>
    /// 2.2.0, 2026-08-18: <c>McpClientOptions.InitializationTimeout</c> defaults
    /// to <c>00:01:00</c>. At unbounded parallelism that produced 46
    /// <c>Initialization timed out</c> failures in one twenty-run session — a
    /// message with no elapsed time, no peer identity and no stderr in it.
    /// </para>
    /// <para>
    /// The product sets the same thing for the same reason; see
    /// <c>ChildConnection.ChildInitializationHang</c>. Both are
    /// <see cref="ProcessHang"/>-shaped because the peer may be a node process
    /// that has to start.
    /// </para>
    /// </remarks>
    public static TimeSpan InitializationHang { get; } = ProcessHang;

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

        // Never left at the SDK's 60 s default: see InitializationHang.
        InitializationTimeout = InitializationHang,
    };
}
