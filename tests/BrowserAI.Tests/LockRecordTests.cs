// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using BrowserAI.Sessions;

namespace BrowserAI.Tests;

/// <summary>
/// <c>lock.json</c>'s schema: strict on the way in, invariant on the way out.
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
/// <b>The culture test runs on its own thread, deliberately.</b> A test that
/// only ever runs under the developer's own locale asserts nothing about the
/// case that breaks, and setting the culture on a shared runner thread would
/// leak into whatever else is running in parallel.
/// </para>
/// </remarks>
internal sealed class LockRecordTests
{
    private const string Path = @"C:\sessions\example\lock.json";

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
    public async Task AnUnknownKeyInsideTheHolderIsRefusedToo()
    {
        var damaged = Text(Sample()).Replace(@"""processId""", @"""processID""", StringComparison.Ordinal);

        var failure = Assert.Throws<LockFileException>(() => _ = LockRecord.Read(Encoding.UTF8.GetBytes(damaged), Path));

        await Assert.That(failure!.Message).Contains("holder.processID");
    }

    [Test]
    public async Task AMissingKeyIsRefusedRatherThanDefaulted()
    {
        var damaged = Text(Sample()).Replace(@"""browser"": ""chromium"",", string.Empty, StringComparison.Ordinal);

        var failure = Assert.Throws<LockFileException>(() => _ = LockRecord.Read(Encoding.UTF8.GetBytes(damaged), Path));

        await Assert.That(failure!.Message).Contains("browser");
        await Assert.That(failure.Message).Contains("Repeating the call that just failed will fail identically.");
    }

    [Test]
    public async Task ARecordFromANewerBuildIsRefusedAndSaysWhichVersionWroteIt()
    {
        var damaged = Text(Sample()).Replace(@"""schemaVersion"": 1", @"""schemaVersion"": 2", StringComparison.Ordinal);

        var failure = Assert.Throws<LockFileException>(() => _ = LockRecord.Read(Encoding.UTF8.GetBytes(damaged), Path));

        await Assert.That(failure!.Message).Contains("schema version 2");
        await Assert.That(failure.Message).Contains("newer BrowserAI");
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
        worker.Join(TimeSpan.FromSeconds(30));

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
        await Assert.That(text).Contains(@"""created"": ""2026-08-16T09:30:00.0000000+02:00""");
        await Assert.That(text).Contains(@"""lastUsed"": ""2026-08-16T11:45:30.1234567+02:00""");
    }

    [Test]
    public async Task ATimestampThatIsNotRoundTrippableIsRefusedRatherThanCoerced()
    {
        var damaged = Text(Sample()).Replace("2026-08-16T09:30:00.0000000+02:00", "16/08/2026 09:30", StringComparison.Ordinal);

        var failure = Assert.Throws<LockFileException>(() => _ = LockRecord.Read(Encoding.UTF8.GetBytes(damaged), Path));

        await Assert.That(failure!.Message).Contains("ISO 8601");
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
        var anonymous = Sample() with { Holder = Sample().Holder with { ClientProcessName = null } };
        var text = Text(anonymous);

        await Assert.That(text).Contains(@"""clientProcessName"": null");
        await Assert.That(LockRecord.Read(anonymous.ToUtf8(), Path).Holder.ClientProcessName).IsNull();
    }

    private static string Text(LockRecord record) => Encoding.UTF8.GetString(record.ToUtf8());

    private static LockRecord Sample() => new()
    {
        SchemaVersion = LockRecord.CurrentSchemaVersion,
        Directory = @"C:\sessions\example",
        Mode = "headless",
        Browser = "chromium",
        Purpose = "checking the customer portal",
        PurposeHistory = ["first purpose", "checking the customer portal"],
        Created = new DateTimeOffset(2026, 8, 16, 9, 30, 0, TimeSpan.FromHours(2)),
        LastUsed = new DateTimeOffset(2026, 8, 16, 11, 45, 30, TimeSpan.FromHours(2)).AddTicks(1234567),
        BrowserAiVersion = "1.0.0.0",
        Holder = new LockHolder
        {
            ProcessId = 4242,
            ProcessCreatedFileTime = 133_000_000_000_000_000,
            ClientProcessName = "node",
        },
    };
}
