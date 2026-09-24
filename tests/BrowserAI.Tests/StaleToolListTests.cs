// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Hosting;
using BrowserAI.Sessions;
using BrowserAI.Storage;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// Q261: a connection whose tool list came from a BrowserAI that has gone, and
/// the two things this server does about it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's decision, 2026-09-24, verbatim: <i>"Q261 b"</i></b> --
/// against his own framing of the question: <i>"How will restarted codex or
/// reconnected claude code sessions learn about the new version and that it needs
/// to refresh the tool list?"</i> Two mechanisms came out of it. A per-connection
/// flag holding whether a <c>tools/list</c> has arrived since the handshake, with
/// the first <c>tools/call</c> that precedes one refused <b>once</b> and
/// <c>notifications/tools/list_changed</c> sent with the refusal; and the serving
/// version in the session record, surfaced as a courtesy line on
/// <c>browserai_resume</c> and <c>browserai_catch_up</c> and never as a refusal.
/// </para>
/// <para>
/// <b>The condition is real and was measured before any of this was written.</b>
/// At Claude Code 2.1.281, 3/3: a client whose stdio server has exited
/// re-launches it transparently on the next tool call and sends
/// <c>initialize</c> and <c>tools/call</c> and <b>no</b> <c>tools/list</c>. At
/// codex-cli 0.155.0-alpha.9.2, 3/3: a thread's list is taken at first connect
/// and the list-changed notification is ignored. Both are in
/// <c>kb/mcp/protocol.md</c> with the evidence beside them.
/// </para>
/// <para>
/// ⚠️ <b>Every arm here drives a stub client, and that is a different claim from
/// the real-client one.</b> What a stub proves is that BrowserAI refuses, once,
/// with the right sentence, and sends the notification. What it cannot prove is
/// that a real client acts on any of it -- that needs the real binary, is held by
/// <c>ClientReconnectTests</c> where a binary is present, and is in the
/// re-verification index either way.
/// </para>
/// </remarks>
internal sealed class StaleToolListTests
{
    /// <summary>A version string no build of this repository can carry.</summary>
    /// <remarks>
    /// It is not merely different from <see cref="BuildVersion.Current"/>: it is
    /// outside the shape MinVer produces at all, so an arm asserting on it cannot
    /// pass by accidentally comparing this build against itself.
    /// </remarks>
    private const string AnOlderBuild = "1.0.0-planted.q261";

    /// <summary>
    /// The first <c>tools/call</c> of a connection that never listed is refused,
    /// the notification goes with it, and the second call is forwarded.
    /// </summary>
    /// <remarks>
    /// <b>Planted red in both directions.</b> With the refusal removed the first
    /// call succeeded and no notification was on the wire; with the
    /// <c>Interlocked.Exchange</c> guard removed the second call was refused too,
    /// which is the wall Q261 rejected. The third assertion is the one that costs
    /// nothing to get wrong and everything to leave out.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACallBeforeAnyToolsListIsRefusedOnceWithTheNotificationAndThenForwarded()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            sessions: sessions,
            listsBeforeCalling: false);

        var directory = Path.Combine(sessions.Root, "q261-refused-once");

        var arguments = new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session a refused first call did not open",
        };

        var first = await CallAsync(rig, SessionToolSurface.Init, arguments.DeepClone().AsObject());

        await Assert.That((bool?)first["isError"]).IsTrue();
        await Assert.That(TextOf(first)).IsEqualTo(
            SessionErrors.ToolListPredatesThisServer(SessionToolSurface.Init, BuildVersion.Current, "BrowserAI.RawPipeClient"));

        // ⚠️ THE NOTIFICATION, READ OFF THE WIRE AND NOT INFERRED FROM THE
        // SENTENCE. It is written before the response, so the round trip above has
        // already drained it -- the client records every frame it reads and skips
        // the ones whose id it is not waiting for.
        await Assert.That(Notifications(rig, "notifications/tools/list_changed")).IsEqualTo(1);

        // ⚠️ AND NOTHING WAS OPENED. A refusal that had already created the
        // directory would make the retry below meet `init`'s own
        // already-a-session refusal, and the arm would still be green.
        await Assert.That(Directory.Exists(directory)).IsFalse();

        // Once per connection, never a wall: the same call, still with no
        // `tools/list` behind it, is forwarded.
        var second = await CallAsync(rig, SessionToolSurface.Init, arguments);

        await Assert.That((bool?)second["isError"]).IsFalse();
        await Assert.That(TextOf(second)).Contains("Session ready.");

        // And exactly one notification for the whole connection.
        await Assert.That(Notifications(rig, "notifications/tools/list_changed")).IsEqualTo(1);
    }

    /// <summary>
    /// A connection that lists before it calls is never refused and is sent no
    /// notification.
    /// </summary>
    /// <remarks>
    /// <b>The both-directions control, and the reason a first connect pays
    /// nothing.</b> Every client measured asks for the tool list immediately after
    /// the handshake, so this is the ordinary path and the arm above is the
    /// exception. It is also what stops the refusal being written as an
    /// unconditional one: with the flag ignored this arm goes red and the one
    /// above stays green.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AConnectionThatListsFirstIsNeverRefusedAndIsSentNoNotification()
    {
        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools["browser_navigate"] = new FakeToolBehaviour
            {
                RawResult = """{"content":[{"type":"text","text":"navigated"}]}""",
            });

        // The rig lists before it calls, which is what a real client does, and it
        // opens its default session through a `tools/call` -- so the session it
        // hands back is itself the evidence that nothing was refused.
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        await Assert.That(rig.Session).IsNotNull();

        var navigate = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["session"] = rig.Session!,
            ["why"] = "the suite checking that a listed connection is not refused",
            ["url"] = "data:text/html,q261",
        });

        await Assert.That((bool?)navigate["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(navigate)).DoesNotContain("has never asked BrowserAI for its tool list");
        await Assert.That(Notifications(rig, "notifications/tools/list_changed")).IsEqualTo(0);
    }

    /// <summary>
    /// <c>browserai_resume</c> says when the session's record was written by
    /// another build, and says nothing when it was not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Planted red</b> by asserting the note against a record carrying this
    /// build, which is the second half below and is what a note computed from the
    /// wrong record would satisfy: <c>OpenAsync</c> re-acquires the directory and
    /// stamps this build, so a comparison made after it can never differ.
    /// </para>
    /// <para>
    /// <b>The record is planted, because the state is one no cooperating process
    /// produces.</b> It is what an older BrowserAI left behind and a newer one
    /// finds -- which on this machine would need two installed builds -- and
    /// writing it through the store is the same bytes that would be on disk.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ResumeSaysWhenTheRecordWasWrittenByAnotherBuildAndSaysNothingWhenItWasNot()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var older = PlantASessionWrittenBy(sessions, "q261-resume-older", AnOlderBuild);
        var same = PlantASessionWrittenBy(sessions, "q261-resume-same", BuildVersion.Current);

        var resumedOlder = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = older,
            [SessionToolSurface.WhyParameter] = "the suite reading the courtesy line back",
        });

        await Assert.That((bool?)resumedOlder["isError"]).IsFalse();
        await Assert.That(TextOf(resumedOlder)).Contains(SessionManager.ServedByADifferentVersion(AnOlderBuild));

        // The history block prints the chain underneath, because the resume above
        // stamped this build beside the planted one. That is the second half of
        // the stamp and it is asserted here and not taken on trust.
        await Assert.That(TextOf(resumedOlder)).Contains(AnOlderBuild);
        await Assert.That(TextOf(resumedOlder)).Contains(BuildVersion.Current);

        var resumedSame = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = same,
            [SessionToolSurface.WhyParameter] = "the suite checking the note stays silent",
        });

        await Assert.That((bool?)resumedSame["isError"]).IsFalse();
        await Assert.That(TextOf(resumedSame)).DoesNotContain("the BrowserAI serving you now is");
    }

    /// <summary>
    /// <c>browserai_catch_up</c> carries the same line on page 1, and writes
    /// nothing to say it.
    /// </summary>
    /// <remarks>
    /// <b>The no-write half is the point of the second assertion.</b> This tool is
    /// documented as taking no lock it can be refused by and as answering about a
    /// session another BrowserAI is driving -- so the version it notices is one it
    /// may only report. A stamp written here would be a read tool mutating a
    /// record whose holder is somebody else.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task CatchUpCarriesTheSameLineOnPageOneAndStampsNothing()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var older = PlantASessionWrittenBy(sessions, "q261-catchup-older", AnOlderBuild);
        var same = PlantASessionWrittenBy(sessions, "q261-catchup-same", BuildVersion.Current);

        var read = await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject { [SessionToolSurface.SessionParameter] = older });

        await Assert.That((bool?)read["isError"]).IsFalse();
        await Assert.That(TextOf(read)).Contains(SessionManager.ServedByADifferentVersion(AnOlderBuild));

        // Read, not written: the record still names only the build that planted it.
        var after = SessionLock.ReadRecord(SessionPath.For(older))!;

        await Assert.That(after.BrowserAiVersion).IsEqualTo(AnOlderBuild);
        await Assert.That(after.BrowserAiVersionHistory.Count).IsEqualTo(1);

        var quiet = await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject { [SessionToolSurface.SessionParameter] = same });

        await Assert.That(TextOf(quiet)).DoesNotContain("the BrowserAI serving you now is");
    }

    /// <summary>
    /// A session directory as an older BrowserAI would have left it: a record and
    /// nothing driving it.
    /// </summary>
    /// <param name="sessions">The environment whose root it goes under.</param>
    /// <param name="name">The directory's name.</param>
    /// <param name="version">The build the record says wrote it.</param>
    /// <returns>The absolute path of the planted session.</returns>
    private static string PlantASessionWrittenBy(RigSessionEnvironment sessions, string name, string version)
    {
        var directory = Path.Combine(sessions.Root, name);

        _ = Directory.CreateDirectory(directory);

        var location = SessionPath.For(directory);

        // Yesterday, so the record reads as one this run did not make and the
        // ordering of the statements a resume adds is unambiguous.
        var at = SessionRecordReader.Stamp(DateTimeOffset.Now.AddDays(-1));

        using var store = SessionStore.OpenForWriting(location.DataFile);

        store.RecordAcquisition(
        [
            new StoredStatement(RecordFields.Directory, at, location.FullPath),
            new StoredStatement(RecordFields.Browser, at, SessionManager.DefaultBrowser),
            new StoredStatement(RecordFields.Purpose, at, $"planted as a session a BrowserAI {version} left behind"),
            new StoredStatement(RecordFields.BrowserAiVersion, at, version),
        ]);

        return directory;
    }

    /// <summary>How many notifications of one method this client has read.</summary>
    /// <param name="rig">The rig whose client to read.</param>
    /// <param name="method">The JSON-RPC method.</param>
    /// <returns>The count.</returns>
    private static int Notifications(McpTestHarness rig, string method) =>
        rig.Client.FramesReceived
            .Select(frame => JsonNode.Parse(FrameChannel.TextOf(frame)) as JsonObject)
            .Count(envelope => envelope is not null
                && envelope["id"] is null
                && (string?)envelope["method"] == method);

    private static async Task<JsonObject> CallAsync(McpTestHarness rig, string tool, JsonObject arguments) =>
        await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));
}
