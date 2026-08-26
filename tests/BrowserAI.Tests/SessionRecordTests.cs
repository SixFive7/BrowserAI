// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using BrowserAI.Sessions;
using BrowserAI.Storage;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// What <c>browserai.data</c> holds and what it refuses: purpose as rows,
/// outcomes as an enum, failure payloads only, and no caps anywhere.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>This file replaces <c>LockRecordTests</c> (2026-08-26), and most of
/// what that file held is gone rather than migrated.</b> Its subject was a JSON
/// document's strict parse — an unknown key, a missing key, a statement with no
/// timestamp, a superseded schema — and those refusals now belong to two other
/// mechanisms that already assert them: <c>LockFileTests</c> for
/// <c>browserai.lock</c>'s closed property set, and <c>SqliteStorageTests</c> for
/// the store's version refusal and for the absence of every cap. What is here is
/// what neither of those can say: the <b>semantics</b> the product reads out of
/// the two files together.
/// </para>
/// <para>
/// <b>Four properties in this file were watched red before the code went in</b>,
/// by removing the behaviour and running these: the purpose row (with the
/// concatenation restored, which puts a <c>|</c> in the value), the dedup, the
/// failure-payload asymmetry, and the sanitiser's line-break rule.
/// </para>
/// </remarks>
internal sealed class SessionRecordTests
{
    /// <summary>
    /// A resume that says what the session is now for adds a <b>row</b>, and the
    /// value it adds is the caller's own sentence and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>THE CONCATENATION IS THE DEFECT AND THIS IS THE TEST FOR IT.</b>
    /// <c>SessionManager.ResumeAsync</c> built the next purpose out of the whole
    /// of the previous one — <c>$"{record.Purpose} | {appended}"</c> — so value
    /// <i>N</i> contained every value before it. Two consequences, and the
    /// second is the one that loses data: the header grew quadratically (57.6
    /// KiB at 50 resumes, 860 KiB at 200), and at the 2,000-character cap the
    /// <b>tail</b> was cut — which is the clause the caller had just written,
    /// silently, with nothing reporting it.
    /// </para>
    /// <para>
    /// <b>The assertion is on the separator as well as on the values</b>, because
    /// a version that stored the whole concatenated string as one row would keep
    /// every value visible and still be the defect.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APurposeChangeIsARowAndNeverAConcatenationOfEveryPurposeBeforeIt()
    {
        using var scratch = ScratchDirectory.Create("record-purpose-rows");
        var path = NewSession(scratch, "purpose-rows");

        var first = SessionLock.TryAcquire(path, Request("reproducing the checkout 500 on staging"), NullLogger.Instance);
        first.Acquired!.Dispose();

        var second = SessionLock.TryAcquire(path, Request("and the same 500 on the mobile checkout"), NullLogger.Instance);
        second.Acquired!.Dispose();

        var third = SessionLock.TryAcquire(path, Request("tracking the checkout redirect loop"), NullLogger.Instance);
        third.Acquired!.AppendPurpose("the redirect loop is a trailing-slash rule");
        third.Acquired.Dispose();

        var record = SessionLock.ReadRecord(path)!;

        // Four statements, each one exactly what its caller said.
        await Assert.That(record.PurposeHistory.Select(statement => statement.Value).ToArray())
            .IsEquivalentTo([
                "reproducing the checkout 500 on staging",
                "and the same 500 on the mobile checkout",
                "tracking the checkout redirect loop",
                "the redirect loop is a trailing-slash rule",
            ]);

        // ⚠️ THE CLAIM. Nothing anywhere carries a value built out of another.
        foreach (var statement in record.PurposeHistory)
        {
            await Assert.That(statement.Value).DoesNotContain(" | ");
        }

        // Current means newest, and the origin is still there to say where the
        // session started.
        await Assert.That(record.Purpose).IsEqualTo("the redirect loop is a trailing-slash rule");
        await Assert.That(record.PurposeHistory[0].Value).IsEqualTo("reproducing the checkout 500 on staging");
    }

    /// <summary>
    /// A field gains a row only when its value changes, and nothing is ever
    /// evicted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both halves used to be the answer to "does an append-only file grow
    /// without bound"; only the first one is left.</b> The dedup stops a
    /// re-opened session recording that a file was written. The cap that bounded
    /// the rest is gone at the maintainer's decision, so the second half of this
    /// test is now the <i>opposite</i> assertion: past what used to be the
    /// 32-statement cap, every statement is still there, in order, with the
    /// oldest first.
    /// </para>
    /// <para>
    /// ⚠️ <b>The holder field is the one dedup does not bound, and that is
    /// deliberate.</b> <c>(pid, creationFileTime)</c> is never the same twice, so
    /// a session opened <i>n</i> times has <i>n</i> holder rows — which is what
    /// makes it a history of acquisitions rather than a note about the current
    /// one.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFieldGainsARowOnlyOnChangeAndNothingIsEverEvicted()
    {
        using var scratch = ScratchDirectory.Create("record-dedup");
        var path = NewSession(scratch, "dedup");

        const int Rounds = 40;

        var opened = SessionLock.TryAcquire(path, Request("the one purpose"), NullLogger.Instance);
        var held = opened.Acquired!;

        try
        {
            for (var i = 0; i < Rounds; i++)
            {
                held.AppendPurpose($"purpose {i.ToString(CultureInfo.InvariantCulture)}");
            }
        }
        finally
        {
            held.Dispose();
        }

        // Restating the same value is not a statement about anything: four
        // acquisitions of one directory, one `directory` row and one `browser`
        // row.
        for (var i = 0; i < 3; i++)
        {
            SessionLock.TryAcquire(path, Request($"purpose {(Rounds - 1).ToString(CultureInfo.InvariantCulture)}"), NullLogger.Instance)
                .Acquired!.Dispose();
        }

        var record = SessionLock.ReadRecord(path)!;

        await Assert.That(record.DirectoryHistory.Count).IsEqualTo(1);
        await Assert.That(record.BrowserHistory.Count).IsEqualTo(1);

        // ⚠️ NOTHING EVICTS. The old record kept 32 per field and trimmed out of
        // the middle; 41 statements past that would have been 32.
        await Assert.That(record.PurposeHistory.Count).IsEqualTo(Rounds + 1);
        await Assert.That(record.PurposeHistory[0].Value).IsEqualTo("the one purpose");
        await Assert.That(record.PurposeHistory[^1].Value).IsEqualTo($"purpose {(Rounds - 1).ToString(CultureInfo.InvariantCulture)}");

        // And the holder history is the one dedup does not bound: four
        // acquisitions, four rows.
        await Assert.That(record.HolderHistory.Count).IsEqualTo(4);
    }

    /// <summary>
    /// <c>created</c> and <c>lastUsed</c> are derived from the record rather than
    /// stored, and the log moves the second one.
    /// </summary>
    /// <remarks>
    /// <b>The log half is what makes <c>lastUsed</c> mean anything during a
    /// session.</b> Before the log existed an hour of driving a browser moved no
    /// timestamp at all, because nothing but an acquisition wrote a statement —
    /// so a listing said a busy session had not been touched since it opened.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task CreatedAndLastUsedAreDerivedAndTheLogIsWhatMovesLastUsed()
    {
        using var scratch = ScratchDirectory.Create("record-dates");
        var path = NewSession(scratch, "dates");

        var opened = SessionLock.TryAcquire(path, Request("a session with dates"), NullLogger.Instance);
        var held = opened.Acquired!;

        var atOpen = SessionLock.ReadRecord(path)!;
        await Assert.That(atOpen.Created).IsLessThanOrEqualTo(atOpen.LastUsed);

        // The store keeps `at` as text, so a stamp written one way and parsed
        // another would sort wrongly rather than fail.
        await Assert.That(atOpen.Created.Year).IsEqualTo(DateTimeOffset.Now.Year);

        try
        {
            // A forwarded call is one row and no statement, so this moves
            // `lastUsed` and leaves `created` where it was.
            var row = held.Append("browser_navigate", "checking that lastUsed moves");
            held.Settle(row, SessionStore.Successful, failure: null);
        }
        finally
        {
            held.Dispose();
        }

        var afterCall = SessionLock.ReadRecord(path)!;

        await Assert.That(afterCall.Created).IsEqualTo(atOpen.Created);
        await Assert.That(afterCall.LastUsed).IsGreaterThanOrEqualTo(atOpen.LastUsed);

        // One row, because this acquisition carried no `Entry` -- which is the
        // `browserai_destroy` shape, and is what makes the row below the only
        // one there is.
        await Assert.That(afterCall.LogLength).IsEqualTo(1);
    }

    /// <summary>
    /// A failure carries its payload and a success carries none.
    /// </summary>
    /// <remarks>
    /// <b>The asymmetry is the decision, and it is what keeps the record the
    /// reasons rather than the traffic.</b> A call that worked already returned
    /// the child's own answer to the caller byte-identical; a copy of it here
    /// would make <c>browserai.data</c> a transcript of every page the session
    /// ever loaded. A call that failed stores what failed, because that is the
    /// one thing nobody can reconstruct afterwards.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFailureCarriesItsPayloadAndASuccessCarriesNone()
    {
        using var scratch = ScratchDirectory.Create("record-payloads");
        var path = NewSession(scratch, "payloads");

        var opened = SessionLock.TryAcquire(path, Request("a session with two outcomes"), NullLogger.Instance);
        var held = opened.Acquired!;

        const string Detail = "TimeoutError: page.goto: Timeout 30000ms exceeded.\n   at Page.goto (page.ts:1)";

        try
        {
            var good = held.Append("browser_snapshot", "reading the page");
            held.Settle(good, SessionStore.Successful, failure: null);

            var bad = held.Append("browser_navigate", "loading a page that will not load");
            held.Settle(bad, SessionStore.Failed, Encoding.UTF8.GetBytes(Detail));
        }
        finally
        {
            held.Dispose();
        }

        using var store = SessionStore.OpenForReading(path.DataFile);
        var rows = SessionRecordReader.Log(store, 0, -1);

        var success = rows.Single(row => row.Tool is "browser_snapshot");
        var failure = rows.Single(row => row.Tool is "browser_navigate");

        await Assert.That(success.Outcome).IsEqualTo(SessionStore.Successful);
        await Assert.That(success.Failure).IsNull();
        await Assert.That(success.SettledAt).IsNotNull();

        await Assert.That(failure.Outcome).IsEqualTo(SessionStore.Failed);
        await Assert.That(failure.Failure).IsEqualTo(Detail);
        await Assert.That(failure.SettledAt).IsNotNull();
    }

    /// <summary>
    /// A row is written <c>in-flight</c> before anything is done about it and
    /// stays that way until something settles it.
    /// </summary>
    /// <remarks>
    /// <b>This is the ordering the old write-before-forward existed for, and it
    /// is the property rather than an implementation detail.</b> A navigation
    /// that hangs, a child that dies, a process that is killed — the calls
    /// anybody investigates — leave exactly this row and nothing else. A row
    /// written on the way back would be missing from all three.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARowIsInFlightUntilSomethingSettlesIt()
    {
        using var scratch = ScratchDirectory.Create("record-in-flight");
        var path = NewSession(scratch, "in-flight");

        var opened = SessionLock.TryAcquire(path, Request("a session with a call that never answers"), NullLogger.Instance);
        var held = opened.Acquired!;

        long row;

        try
        {
            row = held.Append("browser_navigate", "a call nothing will answer");

            // ⚠️ READ FROM ANOTHER CONNECTION, BEFORE ANYTHING SETTLES IT. The
            // point is that the row is durable and visible to a reader at the
            // instant the call is forwarded, which is what a hung call leaves.
            using var reader = SessionStore.OpenForReading(path.DataFile);
            var pending = SessionRecordReader.Log(reader, 0, -1).Single(entry => entry.Id == row);

            await Assert.That(pending.Outcome).IsEqualTo(SessionStore.InFlight);
            await Assert.That(pending.SettledAt).IsNull();
            await Assert.That(pending.Failure).IsNull();
        }
        finally
        {
            held.Dispose();
        }

        // And nothing settled it on the way out, so it stays the true statement
        // about a call that never came back.
        using var after = SessionStore.OpenForReading(path.DataFile);

        await Assert.That(SessionRecordReader.Log(after, 0, -1).Single(entry => entry.Id == row).Outcome)
            .IsEqualTo(SessionStore.InFlight);
    }

    /// <summary>
    /// The sanitiser keeps line breaks, drops carriage returns, neutralises
    /// every other control character and drops the invisible ones — and caps
    /// nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Free text written by one agent and replayed into another's context is
    /// a channel between agents.</b> What keeps it data is no longer a length —
    /// every cap is gone — it is that it cannot carry the characters a terminal,
    /// a renderer or a prompt assembler acts on.
    /// </para>
    /// <para>
    /// ⚠️ <b>U+2028 and U+2029 are the two <c>char.IsControl</c> cannot see</b>,
    /// because it tests category <c>Cc</c> alone and those are <c>Zl</c> and
    /// <c>Zp</c>; U+200B, U+202E and U+FEFF are <c>Cf</c> and are invisible by
    /// construction, so neutralising them to a space would leave a space nobody
    /// typed where the honest answer is nothing.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSanitiserKeepsLineBreaksDropsCarriageReturnsAndCapsNothing()
    {
        // ⚠️ THE CHANGE. A newline survives, because multi-line text is now
        // allowed and a paragraph flattened into one line is a different
        // paragraph.
        await Assert.That(RecordText.Sanitise("first\nsecond")).IsEqualTo("first\nsecond");

        // The carriage return is dropped rather than turned into a space, so a
        // record written on Windows does not carry a stray one into a renderer.
        await Assert.That(RecordText.Sanitise("first\r\nsecond")).IsEqualTo("first\nsecond");

        // Every other Cc becomes a space -- dropping them silently joins two
        // words into one, which changes what the text says.
        await Assert.That(RecordText.Sanitise("first\tsecond\u0007third")).IsEqualTo("first second third");

        // The two char.IsControl does not see, because it tests category Cc
        // alone and these are Zl and Zp.
        await Assert.That(RecordText.Sanitise("first\u2028second\u2029third")).IsEqualTo("first second third");

        // And the three that are invisible by construction, which are dropped
        // outright rather than turned into a space nobody typed.
        await Assert.That(RecordText.Sanitise("pay\u200bload\u202eand\ufeffmore")).IsEqualTo("payloadandmore");

        // ⚠️ NO CAP. The old record cut a purpose at 2,000 characters and a
        // `why` at 400.
        var long_ = new string('x', 50_000);

        await Assert.That(RecordText.Sanitise(long_).Length).IsEqualTo(50_000);
    }

    /// <summary>
    /// A multi-line <c>why</c> survives a round trip through the store, and a
    /// <c>tool</c> is recorded exactly as the caller spelled it.
    /// </summary>
    /// <remarks>
    /// <b>The tool name is the caller's own string, even when it names nothing
    /// this build has ever heard of</b> — because <i>the agent reached for a
    /// tool that does not exist</i> is a fact about the session, and it is the
    /// one a reader most wants when they are looking at a refusal. It goes
    /// through the same sanitiser as a <c>why</c>: a newline in a recorded tool
    /// name would put a forged line into the replay that prints it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AToolIsRecordedVerbatimAndAMultiLineWhySurvivesTheRoundTrip()
    {
        using var scratch = ScratchDirectory.Create("record-verbatim");
        var path = NewSession(scratch, "verbatim");

        const string Why = "first line\nsecond line\nthird line";

        var opened = SessionLock.TryAcquire(path, Request("a session recording an unknown tool"), NullLogger.Instance);
        var held = opened.Acquired!;

        try
        {
            var row = held.Append("browser_teleport", Why);
            held.Settle(row, SessionStore.Failed, Encoding.UTF8.GetBytes("no such tool"));
        }
        finally
        {
            held.Dispose();
        }

        using var store = SessionStore.OpenForReading(path.DataFile);
        var entry = SessionRecordReader.Log(store, 0, -1).Single(row => row.Tool is not SessionToolSurface.Init);

        await Assert.That(entry.Tool).IsEqualTo("browser_teleport");
        await Assert.That(entry.Why).IsEqualTo(Why);
    }

    /// <summary>
    /// A client process name that could not be read is recorded as absent rather
    /// than guessed, in the record's own holder history as well as in the guard.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AClientProcessNameThatCouldNotBeReadIsRecordedAsAbsentRatherThanGuessed()
    {
        var anonymous = new LockFileHolder(4321, 133_000_000_000_000_000L, null);
        var value = SessionRecordReader.WriteHolder(anonymous);

        await Assert.That(value).Contains(@"""clientProcessName"": null");

        var back = LockFile.Parse(Encoding.UTF8.GetBytes(value), "holder");

        await Assert.That(back.ClientProcessName).IsNull();
        await Assert.That(back.ProcessId).IsEqualTo(4321);
        await Assert.That(back.ProcessCreatedFileTime).IsEqualTo(133_000_000_000_000_000L);
    }

    /// <summary>
    /// Every timestamp round-trips under a culture that spells dates
    /// differently.
    /// </summary>
    /// <remarks>
    /// <b>It runs on its own thread, deliberately.</b> A test that only ever runs
    /// under the developer's own locale asserts nothing about the case that
    /// breaks, and setting the culture on a shared runner thread would leak into
    /// whatever else is running in parallel.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryTimestampRoundTripsUnderADeliberatelyNonInvariantCulture()
    {
        var moment = new DateTimeOffset(2026, 8, 26, 11, 45, 30, TimeSpan.FromHours(2)).AddTicks(1234567);
        string? stamp = null;
        DateTimeOffset parsed = default;

        // ar-SA carries the Umm al-Qura calendar, so a date formatted or parsed
        // against the current culture comes out with a different year entirely --
        // the failure this rule exists to close, rather than a cosmetic one.
        var thread = new Thread(() =>
        {
            stamp = SessionRecordReader.Stamp(moment);
            parsed = SessionRecordReader.Moment(stamp);
        })
        {
            CurrentCulture = new CultureInfo("ar-SA"),
            CurrentUICulture = new CultureInfo("ar-SA"),
            IsBackground = true,
        };

        thread.Start();

        await Assert.That(thread.Join(TestDefaults.InProcessHang)).IsTrue();

        // 2026 rather than 2569, and the offset spelled out rather than escaped.
        await Assert.That(stamp).StartsWith("2026-08-26T11:45:30");
        await Assert.That(stamp).EndsWith("+02:00");
        await Assert.That(parsed).IsEqualTo(moment);
    }

    /// <summary>
    /// A directory holding the record format this build does not read is refused
    /// as a <b>format</b>, and never as damage or as a directory that is not a
    /// session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three different refusals were available and only one of them is
    /// honest.</b> <i>Damaged</i> sends the caller to repair a file that is
    /// intact. <i>Not a BrowserAI session</i> sends them to
    /// <c>browserai_init</c>, which would then write a second record beside the
    /// first. What is true is that the file was written by a BrowserAI and this
    /// build does not read it, and the sentence says so with the format as the
    /// reason and the recovery in it.
    /// </para>
    /// <para>
    /// <b>Nothing is written and nothing is taken.</b> Asserted on the directory
    /// listing afterwards, because a refusal that had already created
    /// <c>browserai.lock</c> would leave a guard beside a record that still
    /// claims to be one.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ADirectoryHoldingTheOldRecordIsRefusedAsAFormatAndNothingIsWritten()
    {
        using var scratch = ScratchDirectory.Create("record-old-format");
        var path = NewSession(scratch, "old-format");

        await File.WriteAllTextAsync(
            Path.Combine(path.FullPath, SessionLayout.LegacyRecordFileName),
            """{"schemaVersion": 4, "purpose": []}""");

        var refused = SessionLock.TryAcquire(path, Request("about to meet the old format"), NullLogger.Instance);
        refused.Acquired?.Dispose();

        await Assert.That(refused.Outcome).IsEqualTo(SessionLockOutcome.NotThisFormat);
        await Assert.That(refused.Taken).IsFalse();

        // The sentence names the format, says there is no converter, and names a
        // recovery that is not the call that just failed.
        await Assert.That(refused.Message).Contains(SessionLayout.LegacyRecordFileName);
        await Assert.That(refused.Message).Contains("There is no converter");
        await Assert.That(refused.Message).Contains("Delete the directory yourself");

        // Nothing was written: no guard, no store.
        await Assert.That(File.Exists(path.LockFile)).IsFalse();
        await Assert.That(File.Exists(path.DataFile)).IsFalse();

        // And a read is refused the same way rather than answering "no session".
        var thrown = Assert.Throws<SessionRecordException>(() => SessionLock.ReadRecord(path));

        await Assert.That(thrown!.Message).Contains(SessionLayout.LegacyRecordFileName);
    }

    private static SessionLockRequest Request(string purpose) =>
        new() { Browser = "chromium", Purpose = purpose };

    private static SessionPath NewSession(ScratchDirectory scratch, string name)
    {
        var path = SessionPath.Resolve(Path.Combine(scratch.Path, name));
        SessionLayout.Create(path);

        return path;
    }
}
