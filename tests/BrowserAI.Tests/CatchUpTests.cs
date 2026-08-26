// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Sessions;
using BrowserAI.Storage;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// <c>browserai_catch_up</c>: what the session was doing, and what is in its
/// directory now.
/// </summary>
/// <remarks>
/// <para>
/// <b>The test that matters is the disagreement one.</b> The two halves of this
/// answer come from different places and are expected to differ — a log-only
/// answer would say <i>"no credential tools were used"</i> about a directory
/// full of live session cookies, because cookies arrive from navigation rather
/// than from tools. So the arm below plants a cookie store the log knows nothing
/// about and requires the answer to report it anyway.
/// </para>
/// <para>
/// <b>And the read-only claim is asserted rather than described.</b> The tool
/// runs against a session this BrowserAI is driving, and the record is compared
/// byte for byte before and after: a version that appended its own entry, or took
/// the per-directory gate, would fail the one case the tool exists for.
/// </para>
/// </remarks>
internal sealed class CatchUpTests
{
    [Test]
    public async Task ItReportsWhatWasDoneAndWhatIsHereAndTheTwoAreSeparate()
    {
        await using var sessions = RigSessionEnvironment.Create(
            child => child.Tools["browser_navigate"] = new FakeToolBehaviour(),
            opensDefaultSession: false);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "catch-up");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "reproducing the checkout 500 on staging",
        });

        _ = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["url"] = "data:text/html,ok",
            ["session"] = directory,
            ["why"] = "establishing that the page loads at all",
        });

        // ⚠️ PLANTED BEHIND THE LOG'S BACK, which is the whole point: a browser
        // writes a cookie store from NAVIGATION, so nothing in the log will ever
        // mention it. A log-only answer reports "no credential tools were used"
        // about exactly this directory.
        var store = Path.Combine(directory, SessionLayout.ProfileFolderName, "Default", "Network");
        _ = Directory.CreateDirectory(store);
        await File.WriteAllBytesAsync(Path.Combine(store, "Cookies"), new byte[4096]);

        // And a HAR, which is the file the answer has to call out by name.
        var har = Path.Combine(directory, SessionLayout.OutputFolderName, "network-2026-08-20.har");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(har)!);
        await File.WriteAllTextAsync(har, """{"log":{"entries":[]}}""");

        var text = TextOf(await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["session"] = directory,
        }));

        // The two halves are labelled and separate, because a reader has to know
        // which source each fact came from before it can act on a disagreement.
        await Assert.That(text).Contains("WHAT WAS DONE HERE");
        await Assert.That(text).Contains("WHAT IS HERE NOW");

        // The log half: both entries, with what each was for.
        await Assert.That(text).Contains(SessionToolSurface.Init);
        await Assert.That(text).Contains("reproducing the checkout 500 on staging");
        await Assert.That(text).Contains("browser_navigate");
        await Assert.That(text).Contains("establishing that the page loads at all");

        // ⚠️ THE DISAGREEMENT. Nothing in the log mentions a cookie, and the
        // answer says the profile holds a cookie store anyway.
        await Assert.That(text).DoesNotContain("browser_cookie");
        await Assert.That(text).Contains("CREDENTIALS");
        await Assert.That(text).Contains("cookies arrive from navigation");

        // The HAR, named, with what it is rather than only that it exists.
        await Assert.That(text).Contains("network-2026-08-20.har");
        await Assert.That(text).Contains("PLAINTEXT CREDENTIALS");
        await Assert.That(text).Contains("in clear text");

        // Age, last touched, size and a breakdown by kind.
        await Assert.That(text).Contains("created:");
        await Assert.That(text).Contains("last touched:");
        await Assert.That(text).Contains("total:");
        await Assert.That(text).Contains("profile:");
        await Assert.That(text).Contains($"{SessionLayout.OutputFolderName} (unfiled):");

        // ⚠️ Q100e. The output size, and the sentence that says whose decision
        // retention is: nothing here is ever deleted on a schedule or at a size,
        // so the number is the whole of what a caller has to act on.
        await Assert.That(text).Contains("output:");
        await Assert.That(text).Contains("BrowserAI never deletes any of it");
        await Assert.That(text).Contains(SessionToolSurface.Destroy);

        // Every page says which one it is, how many there are, and where in the
        // whole log its entries sit.
        await Assert.That(text).Contains("page 1 of 1");

        // And whether anything is driving it right now, which is the fact that
        // decides whether the caller may act on any of the above.
        await Assert.That(text).Contains("in use: YES");
    }

    /// <summary>
    /// It changes nothing — not the record, not the log — and works on a session
    /// something else is holding.
    /// </summary>
    /// <remarks>
    /// <b>Byte-for-byte, because "the log did not grow" is a weaker claim.</b> An
    /// implementation that took the per-directory gate and rewrote the record
    /// with identical content would pass a count check, still refuse a session a
    /// live peer holds, and still move the record's mtime.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ItIsReadOnlyAndAnswersForASessionSomethingElseIsDriving()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "read-only");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session that is being held while it is read",
        });

        // Read the way a bystander has to: the holder keeps the file open
        // `FileShare.Read`, so `File.ReadAllBytes` -- which asks for a share
        // mode of None -- is refused.
        var file = Path.Combine(directory, SessionLayout.LockFileName);
        var before = ReadSharing(file);
        var written = File.GetLastWriteTimeUtc(file);

        // This BrowserAI is driving the session, so the per-directory gate is
        // exactly what a writing implementation would have to contend for.
        var text = TextOf(await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["session"] = directory,
        }));

        await Assert.That(text).Contains("a session that is being held while it is read");
        await Assert.That(ReadSharing(file)).IsEquivalentTo(before);
        await Assert.That(File.GetLastWriteTimeUtc(file)).IsEqualTo(written);

        // The record's own log did not gain a row for the read.
        var log = RecordedSession.LogOf(directory);

        await Assert.That(log.Count).IsEqualTo(1);
        await Assert.That(log.Any(entry => entry.Tool == SessionToolSurface.CatchUp)).IsFalse();
    }

    /// <summary>
    /// <c>browserai_init</c>'s purpose is printed <b>once</b>, in full, and
    /// never a second time as a stump.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The defect this holds against was not an aesthetic one.</b>
    /// <c>browserai_init</c> is the one call whose <c>why</c> and whose
    /// <c>purpose</c> are the same string — it takes no separate <c>why</c>, so
    /// the purpose <i>is</i> the why. The entry printed it in full under
    /// <c>why:</c> and then again directly beneath under <c>with: purpose=…</c>,
    /// cut at 200 characters with <c>(+N more characters)</c> after it.
    /// <b>Two adjacent lines, the second one shorter and different</b>: nothing
    /// in the answer said the second was a truncation of the first rather than a
    /// value that disagreed with it.
    /// </para>
    /// <para>
    /// ⚠️ <b>The mechanism that produced it is gone (2026-08-26): log rows carry
    /// no arguments at all.</b> So this is kept as a regression rather than as
    /// the fix's own test — what it now holds is that no cut marker of any kind
    /// reaches this answer, which is the property a caller relies on when it
    /// reads a <c>why</c> back and acts on it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnInitPurposeIsPrintedInFullOnceAndNeverAgainAsATruncatedArgument()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "printed-once");

        const string Purpose =
            "reproducing the checkout 500 on staging: the cart posts to /checkout, the response is a 500 with an "
            + "empty body, and it only happens for accounts whose default address is outside the billing country, "
            + "which is why the fixture needs a Norwegian address on a UK account";

        // The precondition that made a failure possible at all: under the old
        // 200-character argument cap this string was cut, and a shorter one
        // would make this test pass against the defect it exists for.
        await Assert.That(Purpose.Length).IsGreaterThan(200);

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = Purpose,
        });

        var text = TextOf(await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["session"] = directory,
        }));

        // Printed, in full, as the row's own reason.
        await Assert.That(text).Contains($"why: {Purpose}");

        // ⚠️ THE CLAIM. Nothing anywhere in the answer is a cut.
        await Assert.That(text).DoesNotContain("more characters)");
        await Assert.That(text).DoesNotContain($"{SessionToolSurface.PurposeParameter}=");

        // At the record, not only in the rendering: the row carries the whole
        // string, so nothing downstream can print a stump of it either.
        var log = RecordedSession.LogOf(directory);

        await Assert.That(log[0].Tool).IsEqualTo(SessionToolSurface.Init);
        await Assert.That(log[0].Why).IsEqualTo(Purpose);
    }

    /// <summary>
    /// A purpose set by <c>browserai_resume</c> or <c>browserai_set_purpose</c>
    /// is still recoverable, and still dated, from the record alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-26 (previously
    /// <c>ResumeAndSetPurposeStillRecordPurposeBesideTheirOwnWhy</c>, asserting
    /// a <c>with: purpose=…</c> line on the log entry).</b> Log rows carry no
    /// arguments, so the new purpose is no longer <i>in</i> the entry — and that
    /// is the one thing the argument drop genuinely cost, named as a cost rather
    /// than glossed. What replaces it is the <c>purpose</c> statement history:
    /// every value the session has been for, each with the instant it was
    /// recorded, so its <b>position in the stream</b> survives as a timestamp
    /// even though it is no longer a line beside the <c>why</c>.
    /// </para>
    /// <para>
    /// <b>Both tools, because the standing description and the disposable reason
    /// say different things</b> — one lasts, one explains a moment — and an
    /// implementation that recorded only one of them would lose the fact that
    /// the purpose moved.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ResumeAndSetPurposeRecordTheNewPurposeAsADatedStatement()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "two-values");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "reproducing the checkout 500 on staging",
        });

        _ = await CallAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "and the same 500 on the mobile checkout",
            ["why"] = "picking this up after the overnight run stopped",
        });

        _ = await CallAsync(rig, SessionToolSurface.SetPurpose, new JsonObject
        {
            ["session"] = directory,
            ["purpose"] = "tracking the checkout redirect loop on staging",
            ["why"] = "the 500 turned out to be a redirect loop",
        });

        var text = TextOf(await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["session"] = directory,
        }));

        // The disposable reasons, one per row.
        await Assert.That(text).Contains("why: picking this up after the overnight run stopped");
        await Assert.That(text).Contains("why: the 500 turned out to be a redirect loop");

        // And the standing descriptions, in the block that prints every field
        // that has been more than one thing.
        await Assert.That(text).Contains("how this session got here");
        await Assert.That(text).Contains("and the same 500 on the mobile checkout");
        await Assert.That(text).Contains("tracking the checkout redirect loop on staging");

        var record = SessionLock.ReadRecord(SessionPath.Resolve(directory))!;

        await Assert.That(record.PurposeHistory.Select(statement => statement.Value).ToArray())
            .IsEquivalentTo([
                "reproducing the checkout 500 on staging",
                "and the same 500 on the mobile checkout",
                "tracking the checkout redirect loop on staging",
            ]);

        // Dated, and in order, which is what carries "its position in the
        // stream" now that it is not a line in the stream.
        foreach (var (earlier, later) in record.PurposeHistory.Zip(record.PurposeHistory.Skip(1)))
        {
            await Assert.That(earlier.At).IsLessThanOrEqualTo(later.At);
        }

        await Assert.That(RecordedSession.LogOf(directory).Select(entry => entry.Tool).ToArray())
            .IsEquivalentTo([SessionToolSurface.Init, SessionToolSurface.Resume, SessionToolSurface.SetPurpose]);
    }

    /// <summary>
    /// The log is paged, numbered from the OLDEST entry, and a page a caller has
    /// already read never changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Oldest-first is what makes a page stable, and it is the whole reason
    /// the numbering runs that way.</b> The log only ever grows at the newest
    /// end and nothing evicts, so under this numbering an append can change the
    /// last page and no other. Numbering from the newest end — the shape the old
    /// truncation used, which cut from the front — shifts every boundary on
    /// every call, so page 2 of a live session would be a different set of
    /// entries each time it was fetched.
    /// </para>
    /// <para>
    /// <b>The stability is asserted by appending between two fetches</b>, which
    /// is the only way to tell a stable numbering from one that happens to have
    /// been quiet.
    /// </para>
    /// <para>
    /// <b>And the volatile half is on page 1 alone.</b> The inventory is a fresh
    /// directory walk and the in-use line is a fresh probe, so repeating them
    /// would let two pages of one answer disagree about one session in one
    /// minute — about information that has nothing to do with the page being
    /// fetched.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheLogIsPagedFromTheOldestEndAndAnEarlierPageNeverMoves()
    {
        await using var sessions = RigSessionEnvironment.Create(
            child => child.Tools["browser_navigate"] = new FakeToolBehaviour(),
            opensDefaultSession: false);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "paged");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session with more entries than fit on one page",
        });

        // One past the page size, so there are exactly two pages and the second
        // holds a known number of entries.
        const int Calls = 120;

        for (var i = 0; i < Calls; i++)
        {
            _ = await CallAsync(rig, "browser_navigate", new JsonObject
            {
                ["url"] = "data:text/html,ok",
                ["session"] = directory,
                ["why"] = $"call number {i.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            });
        }

        var first = TextOf(await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject { ["session"] = directory }));

        // Page 1 by default, and it says which page it is, how many there are,
        // and the call that fetches the next.
        await Assert.That(first).Contains("page 1 of 2");
        await Assert.That(first).Contains("entries 1–100 of 121");
        await Assert.That(first).Contains($"{SessionToolSurface.CatchUp}(session='{directory}', {SessionToolSurface.PageParameter}=2)");

        // Numbered from the oldest, so entry 1 is the init.
        await Assert.That(first).Contains($"1. ");
        await Assert.That(first).Contains(SessionToolSurface.Init);
        await Assert.That(first).Contains("call number 0");

        // The volatile half is here and nowhere else.
        await Assert.That(first).Contains("WHAT IS HERE NOW");
        await Assert.That(first).Contains("in use:");
        await Assert.That(first).Contains("created:");

        var second = TextOf(await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["session"] = directory,
            [SessionToolSurface.PageParameter] = 2,
        }));

        await Assert.That(second).Contains("page 2 of 2");
        await Assert.That(second).Contains("entries 101–121 of 121");
        await Assert.That(second).Contains("this is the last page");
        await Assert.That(second).Contains($"call number {(Calls - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        // ⚠️ AND NOT THE VOLATILE HALF. Two pages of one answer must not be able
        // to disagree about one session in one minute.
        await Assert.That(second).DoesNotContain("WHAT IS HERE NOW");
        await Assert.That(second).DoesNotContain("in use:");
        await Assert.That(second).DoesNotContain("created:");

        // ⚠️ THE STABILITY CLAIM, and it needs an append between the fetches.
        _ = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["url"] = "data:text/html,ok",
            ["session"] = directory,
            ["why"] = "a call that lands after page 1 was read",
        });

        var again = TextOf(await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject { ["session"] = directory }));

        // The one thing on page 1 that MAY move is the total, because there is a
        // new entry; the entries themselves are the same set in the same order.
        await Assert.That(Entries(again)).IsEquivalentTo(Entries(first));
        await Assert.That(again).Contains("entries 1–100 of 122");
        await Assert.That(again).DoesNotContain("a call that lands after page 1 was read");

        // Out of range is a refusal that names the range and says which end the
        // numbering starts from.
        var refused = await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["session"] = directory,
            [SessionToolSurface.PageParameter] = 9,
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).Contains("outside this session's log");
        await Assert.That(TextOf(refused)).Contains("numbered from the OLDEST end");
    }

    /// <summary>
    /// A row nothing settled renders as <i>no answer was recorded</i>, and a row
    /// that failed carries what failed.
    /// </summary>
    /// <remarks>
    /// <b>A stale <c>in-flight</c> row is the whole of what a hung call, a dead
    /// child or a killed process leaves</b> — the row is written before the call
    /// is forwarded, precisely so that those three leave something. What a
    /// reader must never be given is a <c>false</c> there, which is a lie, or a
    /// <c>true</c>, which is worse.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AStaleInFlightRowSaysNoAnswerWasRecordedAndAFailureCarriesWhy()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "stale-in-flight");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session with a call that never came back",
        });

        // ⚠️ Planted directly, because the state this renders is one no
        // cooperating process produces: it is what is left when a call was
        // forwarded and the process that forwarded it never returned. Writing it
        // through the store is the same thing that would be on disk afterwards.
        var path = SessionPath.Resolve(directory);

        using (var store = SessionStore.OpenForWriting(path.DataFile))
        {
            _ = store.AppendLog(
                SessionRecordReader.Stamp(DateTimeOffset.Now),
                "browser_navigate",
                "loading the page the process died on",
                SessionStore.InFlight);

            var failed = store.AppendLog(
                SessionRecordReader.Stamp(DateTimeOffset.Now),
                "browser_click",
                "clicking something that was not there",
                SessionStore.InFlight);

            _ = store.Settle(
                failed,
                SessionStore.Failed,
                SessionRecordReader.Stamp(DateTimeOffset.Now),
                System.Text.Encoding.UTF8.GetBytes("Error: locator.click: no element matches '#submit'"));
        }

        var text = TextOf(await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["session"] = directory,
        }));

        // ⚠️ THE CLAIM. Not "false", not "true", and not a blank: the true
        // statement about a call whose answer nobody recorded.
        await Assert.That(text).Contains("no answer was recorded");
        await Assert.That(text).Contains("loading the page the process died on");

        // The failure beside it, with what failed rather than a summary.
        await Assert.That(text).Contains("FAILED");
        await Assert.That(text).Contains("it failed with: Error: locator.click: no element matches '#submit'");

        // And the control: the call that worked says so, and stores nothing.
        await Assert.That(text).Contains("✓");
    }

    /// <summary>The numbered log lines of one page, for a stability comparison.</summary>
    /// <param name="text">The page.</param>
    /// <returns>Its entry lines.</returns>
    private static IReadOnlyList<string> Entries(string text) =>
        [.. text.Split('\n').Where(line => line.StartsWith("  ", StringComparison.Ordinal) && line.Contains(". 20", StringComparison.Ordinal))];

    [Test]
    public async Task ADirectoryThatIsNotASessionIsRefusedWithSomewhereToGo()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "never-a-session");
        _ = Directory.CreateDirectory(directory);

        var answer = await CallAsync(rig, SessionToolSurface.CatchUp, new JsonObject
        {
            ["session"] = directory,
        });

        await Assert.That((bool?)answer["isError"]).IsTrue();

        var text = TextOf(answer);

        await Assert.That(text).Contains(SessionLayout.DataFileName);
        await Assert.That(text).Contains(SessionToolSurface.List);
        await Assert.That(text).Contains(SessionToolSurface.Init);
    }

    /// <summary>Reads a file its holder has open.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its bytes.</returns>
    private static byte[] ReadSharing(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();

        stream.CopyTo(buffer);

        return buffer.ToArray();
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
