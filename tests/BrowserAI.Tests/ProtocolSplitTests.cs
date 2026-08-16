// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Protocol;
using BrowserAI.Proxy;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The protocol split: newest upward to the caller, pinned downward to the
/// child.
/// </summary>
/// <remarks>
/// <para>
/// <b>The child never rejects a version.</b> It caps a newer one and echoes an
/// older one, both silently — verified from both directions, so a mis-negotiation
/// produces nothing to catch and the negotiated value has to be asserted
/// instead. That is why the product checks it at startup and why this exists.
/// </para>
/// <para>
/// <b>Pinning the client half is not optional.</b> Left unset, the SDK client
/// prefers <c>2026-07-28</c> and probes with <c>server/discover</c> first,
/// bounded by a five-second <c>DiscoverProbeTimeout</c>. Against a child that
/// drops the unknown method that is a flat five seconds per spawn, against a
/// ~300 ms baseline, presenting as "browser automation got slow" with no error
/// anywhere. The child here answers <c>-32601</c> rather than dropping it, so
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

    private const int MethodNotFound = -32601;

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
        await Assert.That(run.StandardError)
            .Contains($"requested={BrowserProxy.ChildProtocolVersion} negotiated={BrowserProxy.ChildProtocolVersion}");

        // And the pin itself is the child's measured ceiling rather than a
        // number somebody liked: the same value the snapshot generator recorded
        // by probing the child from both directions on every build.
        await Assert.That(BrowserProxy.ChildProtocolVersion).IsEqualTo(SnapshotCeiling());
    }

    [Test]
    public async Task ACallerMayNegotiateARevisionOlderThanTheOneUsedWithTheChild()
    {
        SuiteEnvironment.RequirePublishedSlice();

        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create("protocol-older");

        await using var client = RawStdioClient.Start(
            PublishedSlice.Executable,
            [],
            scratch.Path,
            PublishedSlice.InheritedEnvironment());

        var initialize = await client.InitializeAsync(OlderRevision);

        // Two independent negotiations, and this is the one that proves it: the
        // caller-facing server answered the caller's revision while the child
        // session, running in the same process at the same moment, is pinned to
        // a different one. A server that simply forwarded the child's answer
        // would return 2025-11-25 here.
        await Assert.That((string?)initialize["protocolVersion"]).IsEqualTo(OlderRevision);
        await Assert.That(OlderRevision).IsNotEqualTo(BrowserProxy.ChildProtocolVersion);
    }

    [Test]
    public async Task TheServerReachesARevisionTheChildDoesNotImplement()
    {
        SuiteEnvironment.RequirePublishedSlice();
        SuiteEnvironment.RequireRepositoryPayload();

        PublishedSlice.EnsureFresh();

        // `server/discover` exists only from 2026-07-28, the revision that
        // removed `initialize`. Asking both ends of the proxy the same question
        // is the sharpest available demonstration that the two halves of the
        // split really are different revisions, because unlike a version string
        // it cannot be echoed.
        var fromServer = await DiscoverAsync(
            "protocol-discover-server",
            PublishedSlice.Executable,
            [],
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
        // rather than the exact code, because the code is the SDK's to change
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
