// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Protocol;
using BrowserAI.Proxy;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The protocol revisions: one offered upward to the caller, one pinned downward
/// to the child.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Changed 2026-10-08 (previously "The protocol split: newest upward to the
/// caller, pinned downward to the child").</b> The caller is offered exactly
/// <c>2025-11-25</c>, the one revision whose answers BrowserAI writes correctly,
/// and the child is pinned to its own ceiling, which is the same value today for
/// a different reason. The two are still two negotiations and the code still
/// keeps them apart.
/// </para>
/// <para>
/// <b>The child never rejects a version.</b> It caps a newer one and echoes an
/// older one, both silently -- verified from both directions, so a mis-negotiation
/// produces nothing to catch and the negotiated value has to be asserted
/// instead. That is why the product checks it at startup and why this exists.
/// </para>
/// <para>
/// <b>Pinning the client half is not optional.</b> Left unset, the SDK client
/// prefers <c>2026-07-28</c> and probes with <c>server/discover</c> first,
/// bounded by a five-second <c>DiscoverProbeTimeout</c>. Against a child that
/// drops the unknown method that is a flat five seconds per spawn, against a
/// ~300 ms baseline, presenting as "browser automation got slow" with no error
/// anywhere. The child here answers <c>-32601</c> instead of dropping it, so
/// the cost would be small today and is one upstream refactor away from being
/// large.
/// </para>
/// </remarks>
internal sealed class ProtocolSplitTests
{
    /// <summary>
    /// A revision older than the child's ceiling, and older than anything
    /// BrowserAI would choose on its own.
    /// </summary>
    private const string OlderRevision = "2025-06-18";

    /// <summary>The revision Claude Code opens with since 2026-09-30, by <c>server/discover</c>.</summary>
    private const string NewOpeningRevision = "2026-07-28";

    private const int MethodNotFound = -32601;

    /// <summary>SEP-2575's <c>UnsupportedProtocolVersionError</c>.</summary>
    private const int UnsupportedProtocolVersion = -32022;

    /// <summary>
    /// Claude Code's new opening request is refused with the one revision BrowserAI
    /// implements, and the opening Claude Code falls back to lists the tools.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The suite's own client runs cannot see this opening at all</b>, which is why
    /// it is driven by hand here. From 2026-09-30T10:52Z Claude Code opened with
    /// <c>server/discover</c> at <c>2026-07-28</c>, and the installed 1.1.0 accepted
    /// that revision and then answered <c>tools/list</c> without the
    /// <c>resultType</c> the revision requires: 145 of 153 connections to it listed
    /// no tool at all. Every Claude Code the suite starts runs under a scratch
    /// configuration folder and opened with <c>initialize</c> throughout, so no arm
    /// ever met the new opening
    /// ([kb](../../kb/mcp/protocol.md), re-counted 2026-10-08).
    /// </para>
    /// <para>
    /// <b>The two frames are the ones Claude Code sends</b>: <c>server/discover</c>
    /// carrying the revision and the client's capabilities as per-request metadata,
    /// then, on <c>-32022</c> with a list holding no revision of 2026-07-28 or later,
    /// the <c>initialize</c> handshake on the same connection. That fallback is read
    /// in Claude Code's own code at 2.1.288 and 2.1.294, and the list has to be
    /// exactly <c>["2025-11-25"]</c>: a later revision in it would send Claude Code
    /// down its corrective path instead.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheNewOpeningRequestIsRefusedWithTheOneRevisionBrowserAiOffersAndTheOldOneListsTheTools()
    {
        SuiteEnvironment.RequirePublishedSlice();

        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create("protocol-new-opening");

        await using var client = RawStdioClient.Start(
            PublishedSlice.Executable,
            [],
            scratch.Path,
            PublishedSlice.InheritedEnvironment());

        var discover = await client.EnvelopeAsync("server/discover", new JsonObject
        {
            ["_meta"] = new JsonObject
            {
                ["io.modelcontextprotocol/protocolVersion"] = NewOpeningRevision,
                ["io.modelcontextprotocol/clientCapabilities"] = new JsonObject { ["roots"] = new JsonObject() },
            },
        });

        await Assert.That(ErrorCode(discover))
            .IsEqualTo(UnsupportedProtocolVersion)
            .Because($"the server answered {discover.ToJsonString()}");

        await Assert.That(discover["error"]?["data"]?["supported"]?.ToJsonString())
            .IsEqualTo("""["2025-11-25"]""");

        // The fallback, on the same connection.
        var initialize = await client.InitializeAsync(SliceRun.OfferedProtocolVersion, listsTools: false);

        await Assert.That((string?)initialize["protocolVersion"]).IsEqualTo("2025-11-25");

        var listed = await client.RoundTripAsync("tools/list", new JsonObject());
        var names = (listed["tools"]?.AsArray() ?? []).Select(tool => (string?)tool?["name"]).ToList();

        await Assert.That(names.Count).IsGreaterThan(BrowserAI.Sessions.SessionToolSurface.Names.Count);
        await Assert.That(names).Contains(BrowserAI.Sessions.SessionToolSurface.Init);
        await Assert.That(names).Contains("browser_navigate");
    }

    [Test]
    public async Task TheChildNegotiatesItsCeilingAndTheProductRecordsWhichVersionThatWas()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SliceRun.SharedAsync();

        // The caller half: what BrowserAI told the raw client.
        await Assert.That((string?)run.InitializeResult["protocolVersion"])
            .IsEqualTo(SliceRun.OfferedProtocolVersion);

        // The child half. It is not visible on the wire at all -- the caller
        // sees only its own negotiation -- so the product logs it, and this is
        // the assertion the kb's protocol row has been owed since it was
        // written.
        //
        // ⚠️ ASSERTED ON THE PROCESS LOG, NOT ON STDERR, and the difference is a
        // durability guarantee and not a preference. Corrected 2026-08-18
        // (previously `run.StandardError`), which was red on CI twice for a
        // record the product had written correctly both times. stderr goes
        // through `AddConsole`, which hands records to a background processor
        // thread; `SliceRun` then kills BrowserAI with TerminateProcess, on
        // purpose, to prove containment -- and the queue's contents go with it.
        // The two runs lost different amounts of the tail, which is the
        // signature of a queue and not of a missing call. `RollingFileWriter`
        // is unbuffered per record and `ProcessLog`'s own remarks say so, so the
        // file is where "the product recorded this" can actually be asserted.
        // Scoped to this run's pid, because that log is machine-wide.
        await Assert.That(run.ProcessLog)
            .Contains($"requested={BrowserProxy.ChildProtocolVersion} negotiated={BrowserProxy.ChildProtocolVersion}")
            .Because($"BrowserAI ran as pid {run.BrowserAiProcessId} and wrote {run.ProcessLog.Split('\n').Length} record(s) to the shared process log");

        // And the pin itself is the child's measured ceiling and not a
        // number somebody liked: the same value the snapshot generator recorded
        // by probing the child from both directions on every build.
        await Assert.That(BrowserProxy.ChildProtocolVersion).IsEqualTo(SnapshotCeiling());
    }

    /// <summary>
    /// A caller offering an older revision is answered with the one BrowserAI
    /// offers, which is the caller's to accept.
    /// </summary>
    /// <remarks>
    /// <b>Changed 2026-10-08 with the pin (previously
    /// <c>ACallerMayNegotiateARevisionOlderThanTheOneUsedWithTheChild</c>, which held
    /// the answer to be the caller's own <c>2025-06-18</c>).</b> With
    /// <c>ProtocolVersion</c> set, the SDK's <c>initialize</c> answers the configured
    /// revision and echoes a caller's only when none is configured, which the
    /// 2025-11-25 lifecycle allows a server whose one revision that is. Codex offers
    /// <c>2025-06-18</c> and accepts the answer: <c>rmcp</c> 3.2.0's handshake keeps
    /// whatever revision the server names, read in its own code and served by the
    /// suite's real Codex arms. Watched both ways on 2026-10-08: this assertion red
    /// against the unpinned build, which answered <c>2025-06-18</c>, and the old one
    /// red against the pinned build.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACallerOfferingAnOlderRevisionIsAnsweredWithTheOneBrowserAiOffers()
    {
        SuiteEnvironment.RequirePublishedSlice();

        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create("protocol-older");

        await using var client = RawStdioClient.Start(
            PublishedSlice.Executable,
            PublishedSlice.Mcp,
            scratch.Path,
            PublishedSlice.InheritedEnvironment());

        var initialize = await client.InitializeAsync(OlderRevision);

        await Assert.That((string?)initialize["protocolVersion"]).IsEqualTo(BrowserProxy.CallerProtocolVersion);
        await Assert.That(OlderRevision).IsNotEqualTo(BrowserProxy.CallerProtocolVersion);
    }

    /// <summary>
    /// Both ends are asked for <c>server/discover</c> with no revision named: the
    /// server knows the method and refuses the request, and the child has no such
    /// method.
    /// </summary>
    /// <remarks>
    /// <b>Renamed 2026-10-08 (previously
    /// <c>TheServerReachesARevisionTheChildDoesNotImplement</c>).</b> The server no
    /// longer serves that revision: it refuses every request naming one, as the arm
    /// above holds. What this still shows is that the two ends differ by a method,
    /// which a version string cannot show because a version can be echoed.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheServerKnowsTheNewOpeningMethodAndTheChildDoesNot()
    {
        SuiteEnvironment.RequirePublishedSlice();
        SuiteEnvironment.RequireRepositoryPayload();

        PublishedSlice.EnsureFresh();

        // `server/discover` exists only from 2026-07-28, the revision that
        // removed `initialize`, and asking both ends the same question tells
        // the two apart by method.
        var fromServer = await DiscoverAsync(
            "protocol-discover-server",
            PublishedSlice.Executable,
            PublishedSlice.Mcp,
            PublishedSlice.InheritedEnvironment());

        var fromChild = await DiscoverAsync(
            "protocol-discover-child",
            RepositoryPayload.Layout.NodeExecutable,
            [RepositoryPayload.Layout.PlaywrightMcpCli],
            ChildEnvironment.Build());

        // The child does not have the method.
        await Assert.That(ErrorCode(fromChild)).IsEqualTo(MethodNotFound);

        // BrowserAI does. It refuses this particular call for a different reason
        // -- the request carries no per-request metadata naming a protocol
        // version -- and the assertion is deliberately "not method-not-found"
        // and not the exact code, because the code is the SDK's to change
        // and the routing is the fact under test.
        await Assert.That(ErrorCode(fromServer)).IsNotEqualTo(MethodNotFound);
    }

    private static async Task<JsonObject> DiscoverAsync(
        string label,
        string command,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment)
    {
        using var scratch = ScratchDirectory.Create(label);

        await using var client = RawStdioClient.Start(command, arguments, scratch.Path, environment);

        // Sent as the very first frame, before `initialize`, which is what a
        // 2026-07-28 client does.
        return await client.EnvelopeAsync("server/discover", new JsonObject());
    }

    private static int? ErrorCode(JsonObject envelope) => (int?)envelope["error"]?["code"];

    private static string SnapshotCeiling()
    {
        using var snapshot = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryLayout.Root.FullName, "upstream-snapshots", "tools-list.json")));

        return snapshot.RootElement.GetProperty("protocol").GetProperty("ceiling").GetString()!;
    }
}
