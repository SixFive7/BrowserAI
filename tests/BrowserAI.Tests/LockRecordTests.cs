// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// <c>browserai.json</c>'s schema: strict on the way in, invariant on the way out.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two failures this catches, both otherwise silent.</b> A newer BrowserAI's
/// file read by an older one — a field the older build has never heard of is a
/// field it will not honour, and ignoring it means reporting the session as
/// understood while acting on a partial record. And a hand-edited or corrupted
/// file — under lenient parsing a typo in a key name is indistinguishable from
/// an absent key, so <c>"purpse"</c> reads as no purpose at all and the wrong
/// answer is returned confidently.
/// </para>
/// <para>
/// ⚠️ <b>Schema 2 since 2026-08-18: every field is an ordered list of timestamped
/// statements.</b> Every refusal schema 1 made has to still hold one nesting
/// level deeper, which is what most of the cases below are for — an unknown key
/// inside a statement, a missing <c>at</c>, a timestamp that is not
/// round-trippable, an unknown key inside the holder. Two refusals are new: an
/// <b>empty statement list</b>, and a <b>version-1 file</b>, which is refused
/// with a fix rather than converted.
/// </para>
/// <para>
/// <b>The culture test runs on its own thread, deliberately.</b> A test that
/// only ever runs under the developer's own locale asserts nothing about the
/// case that breaks, and setting the culture on a shared runner thread would
/// leak into whatever else is running in parallel.
/// </para>
/// </remarks>
internal sealed class LockRecordTests
{
    private const string Path = @"C:\sessions\example\browserai.json";

    private static readonly DateTimeOffset Born = new(2026, 8, 16, 9, 30, 0, TimeSpan.FromHours(2));
    private static readonly DateTimeOffset Taken = new(2026, 8, 16, 10, 0, 0, TimeSpan.FromHours(2));
    private static readonly DateTimeOffset Repurposed = new DateTimeOffset(2026, 8, 16, 11, 45, 30, TimeSpan.FromHours(2)).AddTicks(1234567);

    [Test]
    public async Task ARecordRoundTripsThroughItsOwnBytes()
    {
        var original = Sample();
        var parsed = LockRecord.Read(original.ToUtf8(), Path);

        await Assert.That(parsed).IsEqualTo(original);
    }

    [Test]
    public async Task AnUnknownKeyIsRefusedAndTheRefusalNamesARecoveryThatIsNotThisCall()
    {
        // A typo, which is the shape that matters: lenient parsing cannot tell
        // it from an absent key.
        var damaged = Text(Sample()).Replace(@"""purpose""", @"""purpse""", StringComparison.Ordinal);

        var failure = Assert.Throws<LockFileException>(() => _ = LockRecord.Read(Encoding.UTF8.GetBytes(damaged), Path));

        await Assert.That(failure!.Message).Contains("purpse");
        await Assert.That(failure.Message).Contains(Path);

        // The recovery has to be something other than the call that just
        // failed, and the message has to say so -- offering a retry to a model
        // is how a caller ends up in a loop that never terminates and never
        // explains itself.
        await Assert.That(failure.Message).Contains("Recovery:");
        await Assert.That(failure.Message).Contains("remove");
        await Assert.That(failure.Message).Contains("Repeating the call that just failed will fail identically.");
    }

    [Test]
    public async Task AnUnknownKeyInsideAStatementIsRefusedToo()
    {
        var damaged = Text(Sample()).Replace(@"""at"":", @"""when"":", StringComparison.Ordinal);

        var failure = Assert.Throws<LockFileException>(() => _ = LockRecord.Read(Encoding.UTF8.GetBytes(damaged), Path));

        await Assert.That(failure!.Message).Contains("directory.when");
    }

    [Test]
    public async Task AnUnknownKeyInsideTheHolderIsRefusedToo()
    {
        var damaged = Text(Sample()).Replace(@"""processId""", @"""processID""", StringComparison.Ordinal);

        var failure = Assert.Throws<LockFileException>(() => _ = LockRecord.Read(Encoding.UTF8.GetBytes(damaged), Path));

        // Nested one level deeper than in schema 1, and the message says where.
        await Assert.That(failure!.Message).Contains("holder.value.processID");
    }

    [Test]
    public async Task AMissingKeyIsRefusedRatherThanDefaulted()
    {
        var damaged = Cut(Text(Sample()), @"""browser"": [");

        var failure = Assert.Throws<LockFileException>(() => _ = LockRecord.Read(Encoding.UTF8.GetBytes(damaged), Path));

        await Assert.That(failure!.Message).Contains("browser");
        await Assert.That(failure.Message).Contains("Repeating the call that just failed will fail identically.");
    }

    [Test]
    public async Task AStatementWithNoTimestampIsRefusedAndTheRefusalNamesTheField()
    {
        var damaged = Text(Sample()).Replace($@"""at"": ""{Stamp(Born)}"",", string.Empty, StringComparison.Ordinal);

        var failure = Assert.Throws<LockFileException>(() => _ = LockRecord.Read(Encoding.UTF8.GetBytes(damaged), Path));

        await Assert.That(failure!.Message).Contains("directory.at");
    }

    /// <summary>
    /// An empty statement list is refused rather than read as "no value".
    /// </summary>
    /// <remarks>
    /// <b>New with schema 2, and load-bearing.</b> A field's current value is its
    /// newest statement, so an empty list has no current value at all — and under
    /// a parser that let one through, every scalar accessor on the record would
    /// throw an index-out-of-range from somewhere a long way from the file that
    /// caused it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnEmptyStatementListIsRefusedRatherThanReadAsNoValue()
    {
        var damaged = Replace(Text(Sample()), @"""browser"": [", @"""browser"": []");

        var failure = Assert.Throws<LockFileException>(() => _ = LockRecord.Read(Encoding.UTF8.GetBytes(damaged), Path));

        await Assert.That(failure!.Message).Contains("empty 'browser'");
        await Assert.That(failure.Message).Contains("Recovery:");
    }

    [Test]
    public async Task ARecordFromANewerBuildIsRefusedAndSaysWhichVersionWroteIt()
    {
        var damaged = Text(Sample()).Replace(@"""schemaVersion"": 3", @"""schemaVersion"": 4", StringComparison.Ordinal);

        var failure = Assert.Throws<LockFileException>(() => _ = LockRecord.Read(Encoding.UTF8.GetBytes(damaged), Path));

        await Assert.That(failure!.Message).Contains("schema version 4");
        await Assert.That(failure.Message).Contains("newer BrowserAI");
    }

    /// <summary>
    /// A superseded record is refused with the fix in the message, and <b>no
    /// converter is offered</b> — for schema 1 and for schema 2 alike.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The recovery for this one is a deletion, so the sentence has to say
    /// what deleting costs and what it does not.</b> A model told only "delete
    /// it" either will not, or will and then re-create a browser profile it
    /// still had. What is lost is the recorded purpose and the history; what is
    /// untouched is everything beside <c>browserai.json</c>.
    /// </para>
    /// <para>
    /// ⚠️ <b>Two arms since 2026-08-20 (previously
    /// <c>ASchemaOneRecordIsRefusedWithTheFixAndNoConverter</c>, one arm).</b>
    /// Schema 3 dropped <c>mode</c>, so there is a second superseded shape on
    /// disk — and it is the one an installed base actually has. <b>The schema-2
    /// arm is the interesting one:</b> its keys are a strict superset of what
    /// this build reads, so a parser that checked the version last would report
    /// it as an unrecognised <c>mode</c> key rather than as an old file, and the
    /// recovery a caller was handed would be "remove what does not belong"
    /// instead of "delete it and init again".
    /// </para>
    /// </remarks>
    /// <param name="version">The superseded schema version.</param>
    /// <param name="record">A real record as that version wrote it.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments(1, """
        {
          "schemaVersion": 1,
          "directory": "C:\\sessions\\example",
          "mode": "headless",
          "browser": "chromium",
          "purpose": "checking the customer portal",
          "purposeHistory": [ "first purpose", "checking the customer portal" ],
          "created": "2026-08-16T09:30:00.0000000+02:00",
          "lastUsed": "2026-08-16T11:45:30.1234567+02:00",
          "browserAiVersion": "1.0.0.0",
          "holder": { "processId": 4242, "processCreatedFileTime": 133000000000000000, "clientProcessName": "node" }
        }
        """)]
    [Arguments(2, """
        {
          "schemaVersion": 2,
          "directory": [ { "at": "2026-08-16T09:30:00.0000000+02:00", "value": "C:\\sessions\\example" } ],
          "mode": [ { "at": "2026-08-16T09:30:00.0000000+02:00", "value": "headless" } ],
          "browser": [ { "at": "2026-08-16T09:30:00.0000000+02:00", "value": "chromium" } ],
          "purpose": [ { "at": "2026-08-16T09:30:00.0000000+02:00", "value": "checking the customer portal" } ],
          "browserAiVersion": [ { "at": "2026-08-16T09:30:00.0000000+02:00", "value": "1.0.0.0" } ],
          "holder": [ { "at": "2026-08-16T09:30:00.0000000+02:00", "value": { "processId": 4242, "processCreatedFileTime": 133000000000000000, "clientProcessName": "node" } } ]
        }
        """)]
    public async Task ASupersededRecordIsRefusedWithTheFixAndNoConverter(int version, string record)
    {
        var failure = Assert.Throws<LockFileException>(() => _ = LockRecord.Read(Encoding.UTF8.GetBytes(record), Path));

        await Assert.That(failure!.Message).Contains($"schema version {version.ToString(CultureInfo.InvariantCulture)}");
        await Assert.That(failure.Message).Contains("There is no converter and there will not be one.");
        await Assert.That(failure.Message).Contains($"Delete '{Path}'");
        await Assert.That(failure.Message).Contains(SessionToolSurface.Init);

        // What survives the deletion, said explicitly.
        await Assert.That(failure.Message).Contains("profile, output and downloads beside it are untouched");
        await Assert.That(failure.Message).Contains("Repeating the call that just failed will fail identically.");

        // Refused as a VERSION rather than as damage. Both shapes are
        // well-formed JSON whose top-level keys this build still recognises by
        // name, so a parser that checked the version last would report the wrong
        // thing about them -- `directory` "where an array was expected" for
        // schema 1, and an unrecognised `mode` key for schema 2.
        await Assert.That(failure.Message).DoesNotContain("was expected");
        await Assert.That(failure.Message).DoesNotContain("does not recognise");
    }

    [Test]
    public async Task GarbageIsRefusedAsGarbageRatherThanAsAnEmptySession()
    {
        var failure = Assert.Throws<LockFileException>(() => _ = LockRecord.Read("not json at all"u8, Path));

        await Assert.That(failure!.Message).Contains(Path);
    }

    [Test]
    public async Task EveryTimestampRoundTripsUnderADeliberatelyNonInvariantCulture()
    {
        var original = Sample();
        var bytes = original.ToUtf8();

        LockRecord? parsed = null;
        byte[]? writtenElsewhere = null;
        Exception? failure = null;

        // ar-SA carries the Umm al-Qura calendar, so a date formatted or parsed
        // against the current culture comes out with a different year entirely
        // -- the failure this rule exists to close, rather than a cosmetic one.
        var worker = new Thread(() =>
        {
            try
            {
                parsed = LockRecord.Read(bytes, Path);
                writtenElsewhere = parsed.ToUtf8();
            }
#pragma warning disable CA1031 // Carried across the thread boundary and rethrown as an assertion below.
            catch (Exception thrown)
#pragma warning restore CA1031
            {
                failure = thrown;
            }
        })
        {
            CurrentCulture = new CultureInfo("ar-SA"),
            CurrentUICulture = new CultureInfo("ar-SA"),
            IsBackground = true,
        };

        worker.Start();
        worker.Join(TestDefaults.InProcessHang);

        await Assert.That(failure).IsNull();
        await Assert.That(parsed).IsEqualTo(original);

        // Byte-identical, so the file a machine in Riyadh writes is the file a
        // machine in Amsterdam reads.
        await Assert.That(Encoding.UTF8.GetString(writtenElsewhere!)).IsEqualTo(Encoding.UTF8.GetString(bytes));
    }

    [Test]
    public async Task TimestampsAreWrittenAsIso8601WithAnExplicitOffset()
    {
        var text = Text(Sample());

        // The literal, so a change of format is a failing test rather than a
        // file that still parses here and nowhere else.
        await Assert.That(text).Contains(@"""at"": ""2026-08-16T09:30:00.0000000+02:00""");
        await Assert.That(text).Contains(@"""at"": ""2026-08-16T11:45:30.1234567+02:00""");
    }

    [Test]
    public async Task ATimestampThatIsNotRoundTrippableIsRefusedRatherThanCoerced()
    {
        var damaged = Text(Sample()).Replace(Stamp(Born), "16/08/2026 09:30", StringComparison.Ordinal);

        var failure = Assert.Throws<LockFileException>(() => _ = LockRecord.Read(Encoding.UTF8.GetBytes(damaged), Path));

        await Assert.That(failure!.Message).Contains("ISO 8601");
    }

    /// <summary>
    /// <c>created</c> and <c>lastUsed</c> are read from the statements rather than
    /// stored beside them.
    /// </summary>
    /// <remarks>
    /// A stored copy of either could only ever disagree with the statements it
    /// summarises, and a file is where a disagreement becomes permanent. The
    /// sample carries three distinct instants so that <c>Created</c>,
    /// <c>TakenAt</c> and <c>LastUsed</c> cannot all be right by coincidence.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task CreatedAndLastUsedAreReadFromTheStatementsAndAreNotStored()
    {
        var record = Sample();
        var text = Text(record);

        await Assert.That(text).DoesNotContain(@"""created""");
        await Assert.That(text).DoesNotContain(@"""lastUsed""");

        await Assert.That(record.Created).IsEqualTo(Born);
        await Assert.That(record.LastUsed).IsEqualTo(Repurposed);

        // TakenAt is the current holder's own statement, which a later purpose
        // change moves LastUsed past. They are different questions, and the
        // refusal that says "took the lock at" asks this one.
        await Assert.That(record.TakenAt).IsEqualTo(Taken);
        await Assert.That(record.TakenAt).IsNotEqualTo(record.LastUsed);
    }

    /// <summary>
    /// A field grows only when its value changes, and never past the cap.
    /// </summary>
    /// <remarks>
    /// <b>Both halves are the answer to "does an append-only file grow without
    /// bound".</b> The dedupe stops a re-opened session recording that a file was
    /// written; the cap bounds the rest. The trim keeps the <b>first</b>
    /// statement, which is what <see cref="LockRecord.Created"/> is read from — a
    /// trim that dropped the front would silently move a session's creation date.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFieldGrowsOnlyOnChangeAndTheTrimKeepsTheOldestStatement()
    {
        var statements = LockRecord.Append<string>(null, "first", Born);

        // Restating the same value is not a statement about anything.
        var unchanged = LockRecord.Append(statements, "first", Born.AddMinutes(1));

        await Assert.That(unchanged).IsSameReferenceAs(statements);
        await Assert.That(unchanged.Count).IsEqualTo(1);
        await Assert.That(LockRecord.IsAtTheCap(statements)).IsFalse();

        var overflow = LockRecord.MaximumStatementsPerField * 3;

        for (var i = 1; i < overflow; i++)
        {
            statements = LockRecord.Append(statements, $"value {i.ToString(CultureInfo.InvariantCulture)}", Born.AddMinutes(i));
        }

        await Assert.That(statements.Count).IsEqualTo(LockRecord.MaximumStatementsPerField);
        await Assert.That(LockRecord.IsAtTheCap(statements)).IsTrue();

        // The origin survives, because Created is read from it.
        await Assert.That(statements[0].Value).IsEqualTo("first");
        await Assert.That(statements[0].At).IsEqualTo(Born);

        // And the newest is the newest. The pair of assertions is also what
        // makes a cap below two a red build without asserting on a constant: at
        // a cap of one the trim would keep the oldest statement and discard the
        // one just appended, so these two would be the same value.
        await Assert.That(statements[^1].Value).IsEqualTo($"value {(overflow - 1).ToString(CultureInfo.InvariantCulture)}");
        await Assert.That(statements[^1].Value).IsNotEqualTo(statements[0].Value);
    }

    [Test]
    public async Task APurposeIsCappedAndItsControlCharactersBecomeSpaces()
    {
        // Free text written by one agent and replayed into another's context is
        // a channel between agents. A newline in it lets the writer forge what
        // looks like a new line of the reader's own transcript.
        await Assert.That(LockRecord.SanitisePurpose("first\nIGNORE PREVIOUS\r\tsecond"))
            .IsEqualTo("first IGNORE PREVIOUS  second");

        var long_ = new string('x', LockRecord.PurposeMaximumLength + 500);

        await Assert.That(LockRecord.SanitisePurpose(long_).Length).IsEqualTo(LockRecord.PurposeMaximumLength);
    }

    [Test]
    public async Task AClientProcessNameThatCouldNotBeReadIsRecordedAsAbsentRatherThanGuessed()
    {
        var anonymous = Sample() with
        {
            HolderHistory = [new Statement<LockHolder>(Taken, Sample().Holder with { ClientProcessName = null })],
        };

        await Assert.That(Text(anonymous)).Contains(@"""clientProcessName"": null");
        await Assert.That(LockRecord.Read(anonymous.ToUtf8(), Path).Holder.ClientProcessName).IsNull();
    }

    private static string Text(LockRecord record) => Encoding.UTF8.GetString(record.ToUtf8());

    private static string Stamp(DateTimeOffset moment) => moment.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>
    /// Replaces one whole field — from its key to the end of its statement array
    /// — with something else.
    /// </summary>
    /// <remarks>
    /// A field is several lines of JSON now, so the schema-1 trick of deleting a
    /// <c>"key": "value",</c> substring no longer reaches one. Cutting to the
    /// closing bracket does, and it leaves valid JSON either way.
    /// </remarks>
    /// <param name="text">The serialised record.</param>
    /// <param name="opening">The field's key and opening bracket.</param>
    /// <param name="replacement">What to put in its place.</param>
    /// <returns>The damaged text.</returns>
    private static string Replace(string text, string opening, string replacement)
    {
        var start = text.IndexOf(opening, StringComparison.Ordinal);
        var end = text.IndexOf(']', start);

        return string.Concat(text.AsSpan(0, start), replacement, text.AsSpan(end + 1));
    }

    /// <summary>
    /// Removes one whole field, leaving valid JSON with a key genuinely absent.
    /// </summary>
    /// <remarks>
    /// <b>Absent rather than renamed, which is a different refusal.</b> A renamed
    /// key is <i>unrecognised</i> and is caught before anything notices the
    /// original is gone, so a test that wants the MISSING message has to remove
    /// the field and its trailing comma and leave the rest well-formed.
    /// </remarks>
    /// <param name="text">The serialised record.</param>
    /// <param name="opening">The field's key and opening bracket.</param>
    /// <returns>The record without that field.</returns>
    private static string Cut(string text, string opening)
    {
        var start = text.IndexOf(opening, StringComparison.Ordinal);
        var end = text.IndexOf(']', start) + 1;

        if (end < text.Length && text[end] is ',')
        {
            end++;
        }

        return string.Concat(text.AsSpan(0, start), text.AsSpan(end).TrimStart());
    }

    /// <summary>
    /// A record with three distinct instants in it, so that <c>Created</c>,
    /// <c>TakenAt</c> and <c>LastUsed</c> cannot all be right by coincidence.
    /// </summary>
    /// <returns>The sample.</returns>
    private static LockRecord Sample() => new()
    {
        SchemaVersion = LockRecord.CurrentSchemaVersion,
        DirectoryHistory = [new Statement<string>(Born, @"C:\sessions\example")],
        BrowserHistory = [new Statement<string>(Born, "chromium")],
        PurposeHistory =
        [
            new Statement<string>(Born, "first purpose"),
            new Statement<string>(Repurposed, "checking the customer portal"),
        ],
        BrowserAiVersionHistory = [new Statement<string>(Born, "1.0.0.0")],
        HolderHistory =
        [
            new Statement<LockHolder>(Taken, new LockHolder
            {
                ProcessId = 4242,
                ProcessCreatedFileTime = 133_000_000_000_000_000,
                ClientProcessName = "node",
            }),
        ],
    };
}
