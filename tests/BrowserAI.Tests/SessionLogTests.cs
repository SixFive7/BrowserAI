// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The one time-ordered log inside <c>browserai.json</c>: what goes in it, in
/// what order, and what happens to a value nobody should write down.
/// </summary>
/// <remarks>
/// <para>
/// <b>One stream is the whole design, and the assertion that proves it is the
/// ordering one.</b> A purpose change has to land <i>between</i> the calls it
/// explains, in the same list, or a reader has two lists to merge by timestamp
/// and will not.
/// </para>
/// <para>
/// <b>The argument policy is asserted against real upstream parameter names.</b>
/// <c>LoggedArgument.WithheldNames</c> is two strings, and two strings typed into
/// a product are two strings that stop matching upstream the day it renames one —
/// so the names are checked against the committed <c>tools/list</c> snapshot,
/// which the build regenerates from the resolved payload and diffs on every run.
/// </para>
/// </remarks>
internal sealed class SessionLogTests
{
    [Test]
    public async Task ThePurposeChangeLandsBetweenTheCallsItExplains()
    {
        await using var sessions = RigSessionEnvironment.Create(
            child => child.Tools["browser_navigate"] = new FakeToolBehaviour(),
            opensDefaultSession: false);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "one-stream");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "reproducing the checkout 500 on staging",
        });

        _ = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["url"] = "data:text/html,before",
            ["session"] = directory,
            ["why"] = "establishing that the page loads at all",
        });

        _ = await CallAsync(rig, SessionToolSurface.SetPurpose, new JsonObject
        {
            ["session"] = directory,
            ["purpose"] = "tracking the checkout redirect loop on staging",
            ["why"] = "the 500 turned out to be a redirect loop",
        });

        _ = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["url"] = "data:text/html,after",
            ["session"] = directory,
            ["why"] = "watching where the redirect goes now that the cause is known",
        });

        var record = SessionLock.ReadRecord(SessionPath.Resolve(directory))!;

        // ⚠️ THE CLAIM: one list, in the order the things happened, with the
        // human's decision sitting between the calls that led to it and the
        // calls that followed. Two arrays merged by timestamp would satisfy
        // every other assertion in this file and fail this one.
        await Assert.That(record.Log.Select(entry => entry.Tool).ToArray())
            .IsEquivalentTo([
                SessionToolSurface.Init,
                "browser_navigate",
                SessionToolSurface.SetPurpose,
                "browser_navigate",
            ]);

        // init's entry carries the PURPOSE, because init has no `why` — and the
        // purpose is why the session exists.
        await Assert.That(record.Log[0].Why).IsEqualTo("reproducing the checkout 500 on staging");

        // Every other entry carries its own `why`, unmodified.
        await Assert.That(record.Log[1].Why).IsEqualTo("establishing that the page loads at all");
        await Assert.That(record.Log[2].Why).IsEqualTo("the 500 turned out to be a redirect loop");
        await Assert.That(record.Log[3].Why).IsEqualTo("watching where the redirect goes now that the cause is known");

        // Non-decreasing in time, which is what makes "in order" a fact about
        // the file rather than about the order things were appended.
        foreach (var (earlier, later) in record.Log.Zip(record.Log.Skip(1)))
        {
            await Assert.That(earlier.At).IsLessThanOrEqualTo(later.At);
        }

        // The arguments are there, minus the two BrowserAI injected: `session`
        // is the directory the file itself lives in, and `why` is the entry's
        // own field.
        await Assert.That(record.Log[1].Arguments.Select(argument => argument.Name).ToArray()).IsEquivalentTo(["url"]);
        await Assert.That(record.Log[1].Arguments[0].Value).IsEqualTo("data:text/html,before");

        // And the purpose change is legible from the entry alone rather than
        // only from the record's own purpose history.
        await Assert.That(record.Log[2].Arguments.Single(argument => argument.Name == "purpose").Value)
            .IsEqualTo("tracking the checkout redirect loop on staging");

        // The record's standing purpose moved, and the previous one is kept —
        // which is the half `why` is NOT: one is durable and one is disposable.
        await Assert.That(record.Purpose).IsEqualTo("tracking the checkout redirect loop on staging");
        await Assert.That(record.PurposeHistory.Select(statement => statement.Value).ToArray())
            .IsEquivalentTo(["reproducing the checkout 500 on staging", "tracking the checkout redirect loop on staging"]);
    }

    [Test]
    public async Task AResumeWritesItsOwnArrivalIntoTheSameStream()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "arrival");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the session somebody else will pick up",
        });

        // Already open in this BrowserAI, which is the arm that returns early
        // and would have been the easy one to leave unrecorded.
        _ = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["why"] = "picking this up after the overnight run stopped",
        });

        var record = SessionLock.ReadRecord(SessionPath.Resolve(directory))!;

        await Assert.That(record.Log.Select(entry => entry.Tool).ToArray())
            .IsEquivalentTo([SessionToolSurface.Init, SessionToolSurface.Resume]);

        await Assert.That(record.Log[^1].Why).IsEqualTo("picking this up after the overnight run stopped");
    }

    /// <summary>
    /// A refused call leaves no entry, and the refusal is in the session's own
    /// text log instead.
    /// </summary>
    /// <remarks>
    /// <b>This is the log's own definition, asserted.</b> It records what the
    /// session <i>did</i>, so that <c>browserai_catch_up</c> can hold it against
    /// what the directory <i>holds</i>; an entry for a call that never reached a
    /// browser would make the log a record of intent, which is a different and
    /// less useful object.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARefusedCallLeavesNoEntry()
    {
        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools[SessionToolPolicy.AnnotateTool] = new FakeToolBehaviour());

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var before = SessionLock.ReadRecord(SessionPath.Resolve(rig.Session!))!.Log.Count;

        var refused = await CallAsync(rig, SessionToolPolicy.AnnotateTool, new JsonObject
        {
            ["session"] = rig.Session!,
            ["why"] = "reaching for a tool this server does not advertise",
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();

        var after = SessionLock.ReadRecord(SessionPath.Resolve(rig.Session!))!.Log;

        await Assert.That(after.Count).IsEqualTo(before);
        await Assert.That(after.Any(entry => entry.Tool == SessionToolPolicy.AnnotateTool)).IsFalse();
    }

    /// <summary>
    /// What happens to a value that is large, structured, or something a person
    /// typed.
    /// </summary>
    /// <param name="name">The parameter's name.</param>
    /// <param name="value">The value, as JSON.</param>
    /// <param name="expected">Exactly what is stored.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments("url", "\"https://example.test/\"", "https://example.test/")]
    [Arguments("submit", "true", "true")]
    [Arguments("index", "12", "12")]
    [Arguments("fields", "[{\"a\":1},{\"b\":2},{\"c\":3}]", "<array, 3 items>")]
    [Arguments("headers", "{\"Authorization\":\"Bearer x\"}", "<object, 1 keys>")]
    [Arguments("value", "\"the-session-cookie\"", "<withheld, 18 characters>")]
    [Arguments("text", "\"hunter2\"", "<withheld, 7 characters>")]
    [Arguments("element", "null", "<null>")]
    public async Task OneArgumentIsStoredExactlyAsThePolicySays(string name, string value, string expected)
    {
        var stored = LoggedArgument.Of(name, JsonNode.Parse(value));

        await Assert.That(stored.Name).IsEqualTo(name);
        await Assert.That(stored.Value).IsEqualTo(expected);
    }

    /// <summary>
    /// A long value is cut with a count rather than stored whole or dropped.
    /// </summary>
    /// <remarks>
    /// <b>A <c>browser_evaluate</c> body is the case this exists for.</b> A log
    /// that embedded one would be the session's transcript; a log that dropped
    /// it would not say what the call was reaching for. The first two hundred
    /// characters plus a count is the summary.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ALongValueIsCutWithACountOfWhatWasLeftOut()
    {
        var body = "async (page) => { " + new string('x', 500) + " }";
        var stored = LoggedArgument.Of("function", JsonValue.Create(body));

        await Assert.That(stored.Value).StartsWith("async (page) => {");
        await Assert.That(stored.Value).Contains("more characters)");
        await Assert.That(stored.Value.Length)
            .IsLessThan(LockRecord.ArgumentValueMaximumLength + 40);

        // The count is of what the CALLER sent, not of what survived the flatten
        // — a reader has to be able to tell a 500-character script from a
        // 50,000-character one.
        await Assert.That(stored.Value).Contains((body.Length - LockRecord.ArgumentValueMaximumLength).ToString(System.Globalization.CultureInfo.InvariantCulture));

        // A short one is untouched, which is the control: a cut that always
        // fired would satisfy everything above.
        await Assert.That(LoggedArgument.Of("function", JsonValue.Create("() => 1")).Value).IsEqualTo("() => 1");
    }

    /// <summary>
    /// Every withheld name is a real parameter on a real upstream tool.
    /// </summary>
    /// <remarks>
    /// <b>The positive control this list cannot do without.</b> A withhold-list
    /// that matched nothing would read as a policy and be one in name only, and
    /// nothing else in the suite would notice: a value that should have been
    /// withheld and was not looks exactly like a value that was allowed. Read
    /// off the committed snapshot, so an upstream rename arrives as a snapshot
    /// diff first and a red build second.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryWithheldNameIsARealUpstreamParameter()
    {
        var carriers = UpstreamSurface.ToolsCarryingParameter();
        var offenders = new List<string>();

        foreach (var withheld in LoggedArgument.WithheldNames)
        {
            var tools = carriers.GetValueOrDefault(withheld, []);

            if (tools.Count is 0)
            {
                offenders.Add($"'{withheld}' is withheld and no upstream tool declares a parameter by that name");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // And the named tools, individually, because a count is satisfied by the
        // wrong tool as easily as by the right one. These four are the reason
        // the list is what it is: three set a stored credential and one types
        // what a person would have typed.
        await Assert.That(carriers["value"]).Contains("browser_cookie_set");
        await Assert.That(carriers["value"]).Contains("browser_localstorage_set");
        await Assert.That(carriers["value"]).Contains("browser_sessionstorage_set");
        await Assert.That(carriers["text"]).Contains("browser_type");
    }

    /// <summary>
    /// The log is capped, and the trim keeps entry zero.
    /// </summary>
    /// <remarks>
    /// <b>Entry zero is <c>browserai_init</c></b> — the only statement of why
    /// the directory exists. A trim at the front would lose it, and lose it
    /// silently, which is the same defect the statement trim was written to
    /// avoid.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheLogIsCappedAndTheTrimKeepsTheFirstEntry()
    {
        var at = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);
        IReadOnlyList<LogEntry> log = [new LogEntry(at, SessionToolSurface.Init, "why this session exists", [])];

        foreach (var index in Enumerable.Range(1, LockRecord.MaximumLogEntries + 50))
        {
            log = LockRecord.AppendLog(log, new LogEntry(at.AddSeconds(index), "browser_navigate", $"call {index}", []));
        }

        await Assert.That(log.Count).IsEqualTo(LockRecord.MaximumLogEntries);

        // The first entry survived, and it is the init one.
        await Assert.That(log[0].Tool).IsEqualTo(SessionToolSurface.Init);
        await Assert.That(log[0].Why).IsEqualTo("why this session exists");

        // The newest survived too, so the trim came out of the middle.
        await Assert.That(log[^1].Why).IsEqualTo($"call {LockRecord.MaximumLogEntries + 50}");

        // A record holding a full log says so, which is what every answer that
        // reads one turns into "may have had entries elided".
        var record = LockRecordTests.SampleWith(log);

        await Assert.That(record.LogIsAtTheCap).IsTrue();
        await Assert.That(LockRecordTests.SampleWith([.. log.Take(2)]).LogIsAtTheCap).IsFalse();
    }

    private static async Task<JsonObject> CallAsync(McpTestHarness rig, string tool, JsonObject arguments) =>
        await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });
}
