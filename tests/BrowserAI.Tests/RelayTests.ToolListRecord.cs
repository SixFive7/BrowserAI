// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Security.Cryptography;
using System.Text.Json.Nodes;
using BrowserAI.Proxy;
using BrowserAI.Relay;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The maintainer's 127 c: the record of the tool list a client was last answered, and the
/// stale-list refusal narrowed to a client whose list differs from the relay's.
/// </summary>
/// <remarks>
/// <para>
/// <b>His words of 2026-10-12, verbatim: <i>"127 c"</i></b>, of the option <i>"Record which
/// tool list each conversation last fetched, and refuse only when it differs."</i> Q261 b
/// refuses, once, the first call of a connection that has not listed. Two rigs sharing one
/// <see cref="FakeToolListRecord"/> stand for one client's relays one after another: the
/// first answers the client's <c>tools/list</c> and ends, and the second is what the client
/// starts next and calls before it lists, which is what a client that starts a dead server
/// again does (measured 2026-09-24 and 2026-10-03, in <c>kb/mcp/protocol.md</c>).
/// </para>
/// <para>
/// <b>The refusal's words do not change</b>, at the same decision: every arm that meets it
/// compares it with <see cref="SessionErrors.ToolListPredatesThisServer"/> whole.
/// </para>
/// <para>
/// <b>His answers of 2026-10-13, verbatim: <i>"149 a"</i>, <i>"150 a"</i>, <i>"151 b"</i>
/// and <i>"152 a"</i>.</b> A relay with no record refuses as Q261 b does (149 a), which the
/// Q261 b arms with the rig's empty record hold; the record's key is the file's, held by
/// <c>ToolListRecordTests</c> (150 a); and no arm drives a real Claude Code through this
/// (151 b): with a real client, the same list let through is checked by hand, and
/// re-verification row 149 says how.
/// </para>
/// </remarks>
internal sealed partial class RelayTests
{
    /// <summary>The event id of the line a relay writes when its client listed this same list before.</summary>
    private const int ListedTheSameListLine = 27;

    /// <summary>The event id of the line a relay writes when the record could not be written.</summary>
    private const int ListNotRecordedLine = 28;

    /// <summary>The event id of the line a relay writes when the record could not be read.</summary>
    private const int RecordUnreadLine = 29;

    /// <summary>
    /// A relay started again for a client that listed this same tool list through an earlier
    /// relay passes its first call on, with no refusal and no notification, and does so again
    /// after a second handshake.
    /// </summary>
    /// <remarks>
    /// <b>The arm that fails against Q261 b as it was built.</b> A re-dial with no update
    /// between, or across an update that left the list as it was, met the refusal, whose
    /// sentence then said the list came from a different BrowserAI; an engine that refuses
    /// every first call before a list fails here at the first barrier.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AClientThatListedThisSameListThroughAnEarlierRelayHasItsFirstCallPassedOn()
    {
        var record = await ListedThroughAnEarlierRelayAsync(RelayRig.ToolList);
        var readsBefore = record.Reads;

        await using var rig = RelayRig.Start(record: record);
        var (background, _) = await rig.ConnectedAsync(KnownClients.ClaudeCode);

        var call = RelayRig.CallFrame("1");
        await rig.SendAsync(call);

        await Assert.That(await rig.BarrierAsync()).IsEmpty().Because("a client whose list is this relay's list was told its list predates the server");
        await Assert.That((await background.NextAsync()).Text).IsEqualTo(call);

        var lines = rig.Logs.Where(logged => logged.EventId.Id == ListedTheSameListLine).ToList();

        await Assert.That(lines.Count).IsEqualTo(1).Because("the relay passed the call on and did not say why in its log");
        await Assert.That(lines[0].Message).Contains(RelayRig.Facts.Build);

        // A second handshake asks the question again, and the record answers it the same way.
        await rig.SendAsync(RelayRig.InitializeFrame(KnownClients.ClaudeCode, "2.1.290").Replace("\"id\":0", "\"id\":\"again\"", StringComparison.Ordinal));
        await Assert.That((await rig.NextAsync()).IdText).IsEqualTo("again");

        var again = RelayRig.CallFrame("2");
        await rig.SendAsync(again);

        await Assert.That(await rig.BarrierAsync()).IsEmpty();
        await Assert.That((await background.NextAsync()).Text).IsEqualTo(again);

        // This relay read the record once, when it started, and wrote nothing, since its
        // client listed nothing: the one write is the first relay's.
        await Assert.That(record.Reads - readsBefore).IsEqualTo(1);
        await Assert.That(record.Writes).IsEqualTo(1);
    }

    /// <summary>
    /// The same, with no background there yet: the first call is held for one and passed on
    /// when it appears, never refused.
    /// </summary>
    /// <remarks>
    /// <b>The moment an update that left the list as it was produces</b>: the relay the client
    /// starts again meets the background starting.
    /// <see cref="AFirstCallWithNoToolListAndNoBackgroundIsRefusedAtOnceAndTheNextIsHeld"/> is
    /// the same moment for a client that has no record, and stays as it is.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AClientThatListedThisSameListHasItsFirstCallHeldWhenThereIsNoBackgroundYet()
    {
        var record = await ListedThroughAnEarlierRelayAsync(RelayRig.ToolList);

        await using var rig = RelayRig.Start(record: record);
        rig.Finder.Absence = new BackgroundAbsence.Starting();

        _ = await rig.InitializeAsync(KnownClients.ClaudeCode, "2.1.290");
        await rig.SettledAsync();

        var call = RelayRig.CallFrame("1");
        await rig.SendAsync(call);

        await Assert.That(await rig.BarrierAsync()).IsEmpty().Because("the first call was refused, not held");

        var background = rig.Finder.Offer();
        await rig.StepAsync(RelayConstants.LookWhileHolding);

        var hello = await background.NextAsync();
        await background.SendAsync(Frames.HelloAnswer(hello.IdText!, FakeBackground.Pid));

        var replay = await background.NextAsync();
        await background.SendAsync(Frames.ReplayAnswer(replay.IdText!));

        await Assert.That((await background.NextAsync()).Method).IsEqualTo("notifications/initialized");
        await Assert.That((await background.NextAsync()).Text).IsEqualTo(call);
    }

    /// <summary>
    /// A relay started again for a client that listed a different tool list is refused once,
    /// with the notification first, exactly as Q261 b refuses, and its next call is passed on.
    /// </summary>
    /// <remarks>
    /// <b>The arm that keeps the refusal where the list really differs</b>: an update that
    /// moved the list. It passes against Q261 b as built and after 127 c, and fails if the
    /// narrowing ever lets a different list through.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AClientThatListedADifferentListIsRefusedOnceAsQ261BRefuses()
    {
        var record = await ListedThroughAnEarlierRelayAsync(RelayRig.ToolList);

        await using var rig = RelayRig.Start(toolList: AnUpdatedToolList, record: record);
        var (background, _) = await rig.ConnectedAsync(KnownClients.ClaudeCode);

        await rig.SendAsync(RelayRig.CallFrame("1"));
        var answered = await rig.BarrierAsync();

        await Assert.That(answered.Count).IsEqualTo(2);
        await Assert.That(answered[0].Method).IsEqualTo("notifications/tools/list_changed");
        await Assert.That(answered[1].IdText).IsEqualTo("1");
        await Assert.That(answered[1].IsToolError).IsTrue();
        await Assert.That(answered[1].ToolText).IsEqualTo(SessionErrors.ToolListPredatesThisServer(
            "browser_navigate",
            RelayRig.Facts.Build,
            KnownClients.ClaudeCode,
            ToolSignatures.From(AnUpdatedToolList())));

        var next = RelayRig.CallFrame("2");
        await rig.SendAsync(next);
        await Assert.That((await background.NextAsync()).Text).IsEqualTo(next);

        await Assert.That(rig.Logs.Any(logged => logged.EventId.Id == ListedTheSameListLine)).IsFalse();
    }

    /// <summary>
    /// The record names the bytes the client was answered, byte for byte, and the build and
    /// the moment; only an answered <c>tools/list</c> writes it, and once in a relay's life.
    /// </summary>
    /// <remarks>
    /// <b>The digest is computed here from the frame as it arrived</b>, sliced by
    /// <see cref="JsonSpan"/>, which is a second reading of the bytes and not the product's,
    /// so an engine that recorded a list other than the one it answered fails here.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRecordNamesTheBytesTheClientWasAnsweredAndOnlyAnAnsweredListWritesIt()
    {
        await using var rig = RelayRig.Start();
        var (background, _) = await rig.ConnectedAsync(KnownClients.ClaudeCode);

        // A refused call and a passed-on one change nothing the client holds.
        await rig.SendAsync(RelayRig.CallFrame("1"));
        _ = await rig.BarrierAsync();

        var passed = RelayRig.CallFrame("2");
        await rig.SendAsync(passed);
        await Assert.That((await background.NextAsync()).Text).IsEqualTo(passed);

        await rig.SettledAsync();
        await Assert.That(rig.Record.Writes).IsEqualTo(0);
        await Assert.That(rig.Record.Listed).IsNull();

        var list = await rig.ListAsync();
        await rig.SettledAsync();

        var listed = rig.Record.Listed;

        await Assert.That(rig.Record.Writes).IsEqualTo(1);
        await Assert.That(listed).IsNotNull();
        await Assert.That(listed!.Digest).IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(JsonSpan.MemberOf(list.Bytes, "result"))));
        await Assert.That(listed.Build).IsEqualTo(RelayRig.Facts.Build);
        await Assert.That(listed.ListedAt).IsEqualTo(rig.Clock.GetUtcNow());

        // The same relay answers the same list again, and writes nothing more.
        _ = await rig.ListAsync();
        await rig.SettledAsync();

        await Assert.That(rig.Record.Writes).IsEqualTo(1);
    }

    /// <summary>
    /// A record that cannot be read is no record: the first call before a list is refused as
    /// Q261 b refuses it, and the relay says in its log that it could not read the record.
    /// </summary>
    /// <remarks>
    /// <b>The record here names this relay's list</b>, so a relay that took a failed read for
    /// a matching one would pass the call on. It fails against Q261 b as built too, which
    /// read no record and wrote no such line.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARecordThatCannotBeReadIsNoneAndTheFirstCallIsRefusedAsQ261BRefuses()
    {
        var record = await ListedThroughAnEarlierRelayAsync(RelayRig.ToolList);
        var readsBefore = record.Reads;
        record.FailsToRead = true;

        await using var rig = RelayRig.Start(record: record);
        _ = await rig.ConnectedAsync(KnownClients.ClaudeCode);

        await rig.SendAsync(RelayRig.CallFrame("1"));
        var answered = await rig.BarrierAsync();

        await Assert.That(answered.Count).IsEqualTo(2);
        await Assert.That(answered[0].Method).IsEqualTo("notifications/tools/list_changed");
        await Assert.That(answered[1].ToolText).IsEqualTo(SessionErrors.ToolListPredatesThisServer(
            "browser_navigate",
            RelayRig.Facts.Build,
            KnownClients.ClaudeCode,
            ToolSignatures.From(RelayRig.ToolList())));

        await Assert.That(record.Reads - readsBefore).IsEqualTo(1);

        var lines = rig.Logs.Where(logged => logged.EventId.Id == RecordUnreadLine).ToList();

        await Assert.That(lines.Count).IsEqualTo(1).Because("a record the relay could not read went unsaid in its log");
        await Assert.That(lines[0].Message).Contains(nameof(InvalidOperationException));
    }

    /// <summary>
    /// A list whose record cannot be written is answered all the same, the failure is logged
    /// by its type, and the relay goes on.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AListWhoseRecordCannotBeWrittenIsAnsweredAllTheSameAndTheFailureIsLogged()
    {
        var record = new FakeToolListRecord { FailsToWrite = true };

        await using var rig = RelayRig.Start(record: record);
        _ = await rig.InitializeAsync(KnownClients.ClaudeCode, "2.1.290");

        var list = await rig.ListAsync();
        await rig.SettledAsync();

        await Assert.That(Json.Same(list.Result, RelayRig.ToolList())).IsTrue();
        await Assert.That(record.Writes).IsEqualTo(1);
        await Assert.That(record.Listed).IsNull();

        var lines = rig.Logs.Where(logged => logged.EventId.Id == ListNotRecordedLine).ToList();

        await Assert.That(lines.Count).IsEqualTo(1).Because("a record the relay could not write went unsaid in its log");
        await Assert.That(lines[0].Message).Contains(nameof(IOException));
        await Assert.That(await rig.BarrierAsync()).IsEmpty();
        await Assert.That(rig.Running.IsCompleted).IsFalse();
    }

    /// <summary>
    /// A record a first relay wrote when it answered its client's <c>tools/list</c> with
    /// <paramref name="toolList"/>, the relay ended since.
    /// </summary>
    /// <param name="toolList">The list the first relay answered.</param>
    /// <returns>The record, holding that list.</returns>
    private static async Task<FakeToolListRecord> ListedThroughAnEarlierRelayAsync(Func<JsonObject> toolList)
    {
        var record = new FakeToolListRecord();

        await using (var first = RelayRig.Start(toolList: toolList, record: record))
        {
            _ = await first.ConnectedAsync(KnownClients.ClaudeCode);
            _ = await first.ListAsync();
            await first.SettledAsync();
        }

        await Assert.That(record.Listed).IsNotNull().Because("the first relay answered the list and recorded nothing");

        return record;
    }

    /// <summary>
    /// The rig's list as a later build answers it: the same two tools, one of them described
    /// differently, which is a list that differs and keeps every name.
    /// </summary>
    /// <returns>A fresh copy.</returns>
    private static JsonObject AnUpdatedToolList()
    {
        var list = RelayRig.ToolList();
        list["tools"]![1]!["description"] = "Navigate to a URL, as a later build describes it.";
        return list;
    }
}
