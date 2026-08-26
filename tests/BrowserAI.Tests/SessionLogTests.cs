// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text.Json.Nodes;
using BrowserAI.Sessions;
using BrowserAI.Storage;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The one time-ordered log inside <c>browserai.data</c>: what goes in it, in
/// what order, what outcome each row carries, and what a row keeps when it
/// fails.
/// </summary>
/// <remarks>
/// <para>
/// <b>One stream is the whole design, and the assertion that proves it is the
/// ordering one.</b> A purpose change has to land <i>between</i> the calls it
/// explains, in the same list, or a reader has two lists to merge by timestamp
/// and will not.
/// </para>
/// <para>
/// ⚠️ <b>Three things this file used to hold are gone with the shape they were
/// about (2026-08-26).</b> The <c>arguments</c> array and its policy —
/// withheld names, <c>&lt;object, N keys&gt;</c>, the 200-character cut — went
/// with the decision to drop arguments from the record entirely; the cap and
/// its middle-trim went with the decision that there are no caps; and
/// <c>ARefusedCallLeavesNoEntry</c> <b>inverted</b>, because a refused call is
/// now recorded. That last one is the interesting migration: the property it
/// was protecting was <i>the log says what the session DID</i>, and with
/// <c>browserai.log</c> deleted the record is the only place a refusal survives
/// at all — so <i>the agent reached for a tool this build will not forward</i>
/// became replay rather than diagnostics.
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

        var log = await SettledLogOf(directory);

        // ⚠️ THE CLAIM: one list, in the order the things happened, with the
        // human's decision sitting between the calls that led to it and the
        // calls that followed. Two arrays merged by timestamp would satisfy
        // every other assertion in this file and fail this one.
        await Assert.That(log.Select(entry => entry.Tool).ToArray())
            .IsEquivalentTo([
                SessionToolSurface.Init,
                "browser_navigate",
                SessionToolSurface.SetPurpose,
                "browser_navigate",
            ]);

        // init's row carries the PURPOSE, because init has no `why` — and the
        // purpose is why the session exists.
        await Assert.That(log[0].Why).IsEqualTo("reproducing the checkout 500 on staging");

        // Every other row carries its own `why`, unmodified.
        await Assert.That(log[1].Why).IsEqualTo("establishing that the page loads at all");
        await Assert.That(log[2].Why).IsEqualTo("the 500 turned out to be a redirect loop");
        await Assert.That(log[3].Why).IsEqualTo("watching where the redirect goes now that the cause is known");

        // Non-decreasing in time, which is what makes "in order" a fact about
        // the record rather than about the order things were appended.
        foreach (var (earlier, later) in log.Zip(log.Skip(1)))
        {
            await Assert.That(earlier.At).IsLessThanOrEqualTo(later.At);
        }

        // ⚠️ AND THE ROW ID IS THE ORDER, which is what makes a page stable: the
        // ids are strictly increasing, so entry i names the same entry forever
        // and an append can only ever change the last page.
        foreach (var (earlier, later) in log.Zip(log.Skip(1)))
        {
            await Assert.That(earlier.Id).IsLessThan(later.Id);
        }

        // Every row settled, and none of them stored a payload for succeeding.
        foreach (var entry in log)
        {
            await Assert.That(entry.Outcome).IsEqualTo(SessionStore.Successful);
            await Assert.That(entry.SettledAt).IsNotNull();
            await Assert.That(entry.Failure).IsNull();
        }

        var record = SessionLock.ReadRecord(SessionPath.Resolve(directory))!;

        // The record's standing purpose moved, and the previous one is kept —
        // which is the half `why` is NOT: one is durable and one is disposable.
        // The purpose change is dated, so its position in the stream survives
        // the loss of the `arguments` array that used to carry it.
        await Assert.That(record.Purpose).IsEqualTo("tracking the checkout redirect loop on staging");
        await Assert.That(record.PurposeHistory.Select(statement => statement.Value).ToArray())
            .IsEquivalentTo(["reproducing the checkout 500 on staging", "tracking the checkout redirect loop on staging"]);

        var moved = record.PurposeHistory[^1].At;

        await Assert.That(moved).IsGreaterThanOrEqualTo(log[1].At);
        await Assert.That(moved).IsLessThanOrEqualTo(log[3].At);
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

        var log = await SettledLogOf(directory);

        await Assert.That(log.Select(entry => entry.Tool).ToArray())
            .IsEquivalentTo([SessionToolSurface.Init, SessionToolSurface.Resume]);

        await Assert.That(log[^1].Why).IsEqualTo("picking this up after the overnight run stopped");
        await Assert.That(log[^1].Outcome).IsEqualTo(SessionStore.Successful);
    }

    /// <summary>
    /// A refused call is recorded, as a <c>failed</c> row carrying the refusal
    /// the caller was given.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>THIS TEST INVERTED (2026-08-26, previously
    /// <c>ARefusedCallLeavesNoEntry</c>).</b> Its old reasoning was that the log
    /// records what the session <i>did</i>, so a row for a call that never
    /// reached a browser would make it a record of intent — and it pointed the
    /// reader at the session's own text log for <i>what was attempted and
    /// refused</i>. <b>That file is gone.</b> With it gone, the choice is
    /// between recording the refusal here and losing it: <i>the agent reached
    /// for a tool this build will not forward</i> is a fact about the session,
    /// and it is exactly the fact a reader most wants when a session did
    /// nothing.
    /// </para>
    /// <para>
    /// <b>Written and settled in one go, with no in-flight window.</b> Nothing
    /// was forwarded, so there is no instant at which the answer is unknown —
    /// which is what distinguishes a refusal from a call that hung.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARefusedCallIsRecordedAsAFailedRowCarryingTheRefusal()
    {
        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools[RepositoryVerdicts.TheOneDenial.Name] = new FakeToolBehaviour());

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var before = RecordedSession.LogOf(rig.Session!).Count;

        var refused = await CallAsync(rig, RepositoryVerdicts.TheOneDenial.Name, new JsonObject
        {
            ["session"] = rig.Session!,
            ["why"] = "reaching for a tool this server does not advertise",
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();

        var after = RecordedSession.LogOf(rig.Session!);
        var row = after.Single(entry => entry.Tool == RepositoryVerdicts.TheOneDenial.Name);

        await Assert.That(after.Count).IsEqualTo(before + 1);
        await Assert.That(row.Why).IsEqualTo("reaching for a tool this server does not advertise");

        // Failed, settled, and carrying what the caller was told rather than a
        // summary of it.
        await Assert.That(row.Outcome).IsEqualTo(SessionStore.Failed);
        await Assert.That(row.SettledAt).IsNotNull();
        await Assert.That(row.Failure).IsEqualTo(TextOf(refused));
    }

    /// <summary>
    /// A call the child answers with an error is a <c>failed</c> row, not a
    /// successful one.
    /// </summary>
    /// <remarks>
    /// <b>Upstream answers a tool error inside an ordinary JSON-RPC result</b>,
    /// so a navigation that timed out and a navigation that worked are the same
    /// shape at the transport. Reading them the same way would put
    /// <i>successful</i> beside every timeout in the record, which is the
    /// confident-wrong-answer class this repository keeps closing — a reader
    /// would see <c>browser_navigate</c> with a <c>why</c> and could not tell a
    /// load from a failure.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AChildErrorIsAFailedRowAndItsPayloadIsWhatTheChildSaid()
    {
        await using var sessions = RigSessionEnvironment.Create(
            child => child.Tools["browser_navigate"] = new FakeToolBehaviour
            {
                // The bytes the child sends, verbatim: an ordinary JSON-RPC
                // RESULT carrying `isError`, which is how upstream reports a
                // tool failure and is exactly why the outcome cannot be read
                // off the transport alone.
                RawResult = """{"content":[{"type":"text","text":"TimeoutError: page.goto: Timeout 30000ms exceeded."}],"isError":true}""",
            },
            opensDefaultSession: false);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "child-error");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session whose navigation fails",
        });

        _ = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["url"] = "data:text/html,never",
            ["session"] = directory,
            ["why"] = "loading a page that will not load",
        });

        var settledLog = await SettledLogOf(directory);
        var row = settledLog.Single(entry => entry.Tool is "browser_navigate");

        await Assert.That(row.Outcome).IsEqualTo(SessionStore.Failed);
        await Assert.That(row.SettledAt).IsNotNull();
        await Assert.That(row.Failure).IsNotNull();
        await Assert.That(row.Failure!).Contains("Timeout 30000ms exceeded");

        // And the control, on the same session: a call that worked stores no
        // payload at all. Without this a version that stored every answer would
        // pass every assertion above.
        var init = settledLog.Single(entry => entry.Tool == SessionToolSurface.Init);

        await Assert.That(init.Outcome).IsEqualTo(SessionStore.Successful);
        await Assert.That(init.Failure).IsNull();
    }

    /// <summary>
    /// The row is on disk, readable by another process, <b>while the call is
    /// still outstanding</b> — and it settles only when the answer arrives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>THIS IS THE ORDERING PROPERTY, AND IT IS WHY THE ROW IS WRITTEN
    /// BEFORE THE FORWARD RATHER THAN AFTER.</b> A navigation that hangs, a
    /// child that dies, a process that is killed — the calls anybody
    /// investigates — leave exactly this row and nothing else. A row written on
    /// the way back would be missing from all three, which is the shape the
    /// comment above the old append existed to prevent: *a log line written on
    /// the way back would be missing from exactly the calls anybody
    /// investigates*.
    /// </para>
    /// <para>
    /// <b>Read from a second connection, not from the session's own.</b> The
    /// claim is that the row is <i>durable and visible</i> at that instant, not
    /// that some object in this process is holding it — and a reader in another
    /// process is what <c>browserai_catch_up</c> against a live session
    /// actually is.
    /// </para>
    /// <para>
    /// <b>The child is held open rather than delayed.</b>
    /// <c>FakeToolBehaviour.HoldUntil</c> keeps the call outstanding without
    /// blocking the child's read loop, so nothing here depends on how long a
    /// machine takes: the release is a <c>TaskCompletionSource</c> this test
    /// sets, and the only duration is the suite's own in-process hang detector.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARowIsOnDiskAndInFlightWhileTheCallIsStillOutstanding()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sessions = RigSessionEnvironment.Create(
            child => child.Tools["browser_navigate"] = new FakeToolBehaviour { HoldUntil = release.Task },
            opensDefaultSession: false);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "in-flight-before-forward");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session whose call is watched while it is outstanding",
        });

        var call = CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["url"] = "data:text/html,held",
            ["session"] = directory,
            ["why"] = "a call that is watched while it is still outstanding",
        });

        try
        {
            // The child really did receive it, so "in flight" is a fact rather
            // than a call that never left. Bounded by the suite's own hang
            // detector and by nothing this test invented.
            var arrived = Stopwatch.StartNew();

            while (!sessions.SessionChildren.Any(child => child.ToolCallsReceived.Contains("browser_navigate")))
            {
                await Assert.That(arrived.Elapsed).IsLessThan(TestDefaults.InProcessHang)
                    .Because("the held call never reached the child, so nothing below is about an outstanding call");

                await Task.Delay(10);
            }

            // ⚠️ THE CLAIM. Another connection, reading the file on disk, sees
            // the row with what the call was for and no answer.
            var pending = RecordedSession.LogOf(directory).Single(row => row.Tool is "browser_navigate");

            await Assert.That(pending.Outcome).IsEqualTo(SessionStore.InFlight);
            await Assert.That(pending.Why).IsEqualTo("a call that is watched while it is still outstanding");
            await Assert.That(pending.SettledAt).IsNull();
            await Assert.That(pending.Failure).IsNull();
        }
        finally
        {
            release.SetResult();
        }

        _ = await call;

        // And it settles when the answer arrives, with the instant it settled at
        // — which is what makes a duration derivable and what tells a stale row
        // from a finished one.
        var settled = (await SettledLogOf(directory)).Single(row => row.Tool is "browser_navigate");

        await Assert.That(settled.Outcome).IsEqualTo(SessionStore.Successful);
        await Assert.That(settled.SettledAt).IsNotNull();
        await Assert.That(settled.SettledAt!.Value).IsGreaterThanOrEqualTo(settled.At);
        await Assert.That(settled.Failure).IsNull();
    }

    /// <summary>
    /// One session's log, once every row in it has settled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>THE SETTLE IS NOT ORDERED BEFORE THE ANSWER, and reading the row
    /// the instant a round trip returns is a race a test loses under load.</b>
    /// <c>BrowserProxy</c> settles in a <c>finally</c> that runs <i>after</i>
    /// the answer has gone back to the caller — deliberately, because that
    /// <c>finally</c> is what covers the ways out the child never hears about,
    /// and because the arm above asserts the in-flight window is genuinely
    /// visible to a second reader while a call is outstanding. Nothing in the
    /// product ever promised the settle precedes the answer.
    /// </para>
    /// <para>
    /// <b>Found 2026-08-26 by a full run</b>, not by review:
    /// <c>AChildErrorIsAFailedRowAndItsPayloadIsWhatTheChildSaid</c> read
    /// <c>in-flight</c> where it expected <c>failed</c>, on a call whose answer
    /// the client was already holding. Four arms in this file had the same
    /// shape and three of them had never lost.
    /// </para>
    /// <para>
    /// <b>Bounded by the suite's own hang detector and by nothing this file
    /// invented.</b> What is asserted is still that the row settles — never how
    /// fast, which is a promptness claim and would be a defect.
    /// </para>
    /// </remarks>
    /// <param name="directory">The session directory.</param>
    /// <returns>The rows, oldest first, none of them in flight.</returns>
    private static async Task<IReadOnlyList<SessionLogRow>> SettledLogOf(string directory)
    {
        var waited = Stopwatch.StartNew();

        while (true)
        {
            var log = RecordedSession.LogOf(directory);

            if (log.Count > 0 && log.All(row => row.Outcome is not SessionStore.InFlight))
            {
                return log;
            }

            await Assert.That(waited.Elapsed).IsLessThan(TestDefaults.InProcessHang)
                .Because($"a row this session logged never settled, so every outcome below would be about a call that never came back ({log.Count} rows)");

            await Task.Delay(10);
        }
    }

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
