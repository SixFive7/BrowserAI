// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Security.AccessControl;
using System.Text;
using BrowserAI.Storage;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The storage layer: the hand-written interop, the schema, and the two
/// properties the whole design rests on — that a reader sees a live writer's
/// work, and that a crashed writer's does not come back until somebody with
/// write access asks for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>These run against a loose <c>e_sqlite3.dll</c> and not against the
/// artifact, and the difference is stated rather than glossed.</b> The test
/// host is CoreCLR, so <c>DirectPInvoke</c> and <c>NativeLibrary</c> are inert
/// and the module name resolves to the DLL <c>SourceGear.sqlite3</c> puts
/// beside the host. That library is the same SQLite <i>version</i> as the
/// vendored amalgamation — the package's version number is the SQLite version —
/// and a <i>different build</i> of it, with somebody else's compile flags. So
/// everything here is a claim about this layer's behaviour against SQLite, and
/// nothing here is a claim about the flags: those are asserted against the
/// published binary in <see cref="SqliteTests"/>.
/// </para>
/// <para>
/// <b>No duration is asserted anywhere in this file.</b> Every property below
/// is an event — a row that is there, an exception that was thrown, a value
/// that came back — and the one number that looks like a duration is a
/// configured budget read back from the connection, which is a setting rather
/// than a measurement.
/// </para>
/// </remarks>
internal sealed class SqliteStorageTests
{
    /// <summary>
    /// A timestamp in the shape the record uses, so that a stored value is a
    /// realistic one rather than a placeholder.
    /// </summary>
    /// <returns>The text.</returns>
    private static string Now() => DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>
    /// Opening a store that is not there says so, names the file, and does not
    /// create one.
    /// </summary>
    /// <remarks>
    /// <b>The read-only open is the one that must not create.</b> A reader that
    /// quietly conjured an empty database would turn *this directory has no
    /// session* into *this session has no history*, and leave a file behind in
    /// a directory it was only asked to look at.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AReadOnlyOpenOfAStoreThatIsNotThereIsRefusedAndCreatesNothing()
    {
        using var scratch = ScratchDirectory.Create("sqlite-absent");

        var path = Path.Combine(scratch.Path, SessionStore.DataFileName);

        var failure = Assert.Throws<SqliteException>(() => SessionStore.OpenForReading(path).Dispose());

        await Assert.That(failure!.Result).IsEqualTo(Sqlite.CannotOpen);
        await Assert.That(failure.Message).Contains(path);
        await Assert.That(File.Exists(path)).IsFalse();
    }

    /// <summary>
    /// A statement SQLite cannot compile is refused with SQLite's own complaint
    /// and the statement that produced it.
    /// </summary>
    /// <remarks>
    /// <b>The message has to carry both halves.</b> A bare result code says a
    /// prepare failed and not which one, and this layer prepares a dozen fixed
    /// statements — so a message without the SQL is a number and a shrug.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AStatementThatDoesNotCompileIsRefusedWithSqlitesOwnComplaintAndTheStatement()
    {
        using var database = SqliteDatabase.OpenInMemory();

        var failure = Assert.Throws<SqliteException>(() => database.Prepare("SELECT * FROM a_table_nobody_made;").Dispose());

        await Assert.That(failure!.Result).IsEqualTo(Sqlite.GenericError);
        await Assert.That(failure.Message).Contains("no such table: a_table_nobody_made");
        await Assert.That(failure.Message).Contains("a_table_nobody_made");

        // And the same for a statement that is not SQL at all, so the arm above
        // is not passing on the one error SQLite happens to phrase that way.
        var nonsense = Assert.Throws<SqliteException>(() => database.Prepare("THIS IS NOT SQL;").Dispose());

        await Assert.That(nonsense!.Message).Contains("syntax error");
    }

    /// <summary>
    /// Binding a parameter this statement does not have is refused, and the
    /// refusal names the index.
    /// </summary>
    /// <remarks>
    /// <b>The misuse path, and it is the one that would otherwise be
    /// silent.</b> A bind that returned a code nobody read would leave the
    /// parameter unbound, which SQLite treats as SQL <c>NULL</c> — so the
    /// statement runs, the row lands, and the record says the call had no
    /// reason.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ABindOutsideTheStatementsParametersIsRefusedAndNamesTheIndex()
    {
        using var database = SqliteDatabase.OpenInMemory();

        database.Execute("CREATE TABLE one (value TEXT);");

        using var statement = database.Prepare("INSERT INTO one (value) VALUES (?);");

        var failure = Assert.Throws<SqliteException>(() => statement.BindText(2, "there is no second parameter"));

        await Assert.That(failure!.Result).IsEqualTo(Sqlite.Range);
        await Assert.That(failure.Message).Contains("binding parameter 2");
    }

    /// <summary>
    /// A fresh store is schema version one, in write-ahead logging mode, and
    /// carrying the same patience the per-directory gate gives a contender.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>All three in one test because all three are properties of the same
    /// open</b>, and the failure mode they share is that any of them can be
    /// silently absent: an unstamped database reads as *not ours*, a database
    /// that stayed in rollback-journal mode blocks every reader at every
    /// commit, and a connection with no busy timeout answers
    /// <c>SQLITE_BUSY</c> the instant it is contended instead of waiting.
    /// </para>
    /// <para>
    /// <b>The timeout is compared against the product constant it derives
    /// from</b>, never against a number written here — which is also what keeps
    /// it out of the class of assertion this repository forbids, since it is a
    /// setting read back rather than a duration measured.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFreshStoreIsVersionOneInWalModeWithTheDirectoryGatesPatience()
    {
        using var scratch = ScratchDirectory.Create("sqlite-fresh");
        using var store = SessionStore.OpenForWriting(Path.Combine(scratch.Path, SessionStore.DataFileName));

        await Assert.That(store.RecordedSchemaVersion()).IsEqualTo((long)SessionStore.SchemaVersion);
        await Assert.That(store.JournalMode()).IsEqualTo("wal");
        await Assert.That(store.BusyTimeoutInMilliseconds()).IsEqualTo((long)SessionStore.BusyTimeout.TotalMilliseconds);

        // And the file that makes WAL what it is exists beside the store, which
        // is the half a mode string alone does not prove.
        await Assert.That(File.Exists(store.Path + "-wal")).IsTrue();
    }

    /// <summary>
    /// A store recording a schema version this build does not read is refused
    /// with the version as the reason.
    /// </summary>
    /// <remarks>
    /// <b>The version is checked in a pass of its own, so that a version error
    /// reads as a version error.</b> That is this repository's standing
    /// position on a record it cannot act on, and the reason it matters here is
    /// that the alternative — letting the first statement fail on a missing
    /// column — reports damage about a file that is perfectly intact.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AStoreFromAnotherSchemaIsRefusedWithTheVersionAsTheReason()
    {
        using var scratch = ScratchDirectory.Create("sqlite-version");

        var path = Path.Combine(scratch.Path, SessionStore.DataFileName);

        using (var store = SessionStore.OpenForWriting(path))
        {
            await Assert.That(store.RecordedSchemaVersion()).IsEqualTo((long)SessionStore.SchemaVersion);
        }

        using (var raw = SqliteDatabase.OpenForWriting(path))
        {
            raw.Execute("PRAGMA user_version = 7;");
        }

        var writing = Assert.Throws<SqliteException>(() => SessionStore.OpenForWriting(path).Dispose());
        var reading = Assert.Throws<SqliteException>(() => SessionStore.OpenForReading(path).Dispose());

        foreach (var failure in new[] { writing, reading })
        {
            await Assert.That(failure!.Message).Contains("schema version 7");
            await Assert.That(failure.Message).Contains("There is no converter");
        }
    }

    /// <summary>
    /// The statements one acquisition makes arrive together or not at all.
    /// </summary>
    /// <remarks>
    /// <b>The one explicit transaction in the store, and the reason it is the
    /// one.</b> A holder row and the statements saying what the session now is
    /// are a single fact; a reader that caught half of it would see a directory
    /// held for no reason, or a reason with no holder. The failure is provoked
    /// by a statement whose value is not a value, so the rollback runs on a real
    /// SQLite refusal rather than on a thrown test double.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnAcquisitionsStatementsArriveTogetherOrNotAtAll()
    {
        using var scratch = ScratchDirectory.Create("sqlite-acquire");
        using var store = SessionStore.OpenForWriting(Path.Combine(scratch.Path, SessionStore.DataFileName));

        var at = Now();

        store.RecordAcquisition(
        [
            new StoredStatement("directory", at, scratch.Path),
            new StoredStatement("browser", at, "chromium"),
            new StoredStatement("purpose", at, "read the two properties this test is about"),
        ]);

        await Assert.That(store.Statements().Count).IsEqualTo(3);
        await Assert.That(store.Statements()[0].Field).IsEqualTo("directory");
        await Assert.That(store.Statements()[2].Value).IsEqualTo("read the two properties this test is about");

        // A second acquisition in which one statement cannot be written. The
        // refusal has to come from SQLite rather than from a guard in this
        // layer, or the rollback path is never reached at all -- so the store
        // is given a trigger that aborts on one value, which is a real
        // constraint failure arriving in the middle of a real transaction.
        using (var raw = SqliteDatabase.OpenForWriting(store.Path))
        {
            raw.Execute(
                """
                CREATE TRIGGER refuse_that_one AFTER INSERT ON statements
                WHEN NEW.value = 'the value the trigger refuses'
                BEGIN
                    SELECT RAISE(ABORT, 'this row is refused on purpose');
                END;
                """);
        }

        var failure = Assert.Throws<SqliteException>(() => store.RecordAcquisition(
        [
            new StoredStatement("browserAiVersion", at, "1.0.0"),
            new StoredStatement("directory", at, "the value the trigger refuses"),
        ]));

        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Message).Contains("this row is refused on purpose");

        // Nothing from the failed acquisition survived, including the statement
        // that was written before the one that failed.
        await Assert.That(store.Statements().Count).IsEqualTo(3);
        await Assert.That(store.Statements().Any(statement => statement.Field is "browserAiVersion")).IsFalse();

        // And the store is still usable, which is what the rollback buys: a
        // transaction left open would refuse every append after it.
        store.Append(new StoredStatement("purpose", Now(), "and it still works afterwards"));

        await Assert.That(store.Statements().Count).IsEqualTo(4);

        // A guard in this layer would have refused before SQLite saw anything,
        // which is a different code path and would leave the rollback above
        // unexercised. Asserted so that a later ArgumentNullException on the
        // bind cannot quietly take the place of the trigger.
        await Assert.That(failure.Result).IsNotEqualTo(Sqlite.Ok);
    }

    /// <summary>
    /// A log row is written before its call is forwarded and settled on the same
    /// row afterwards, and a settle that matches nothing says so.
    /// </summary>
    /// <remarks>
    /// <b>Settling in place is the whole reason the log is a table.</b> The
    /// record this replaces could only append, so an outcome had to be a second
    /// entry that a reader then had to pair up — and *no answer was ever
    /// recorded* had no representation at all. Here a hung call is a row that
    /// still says <c>in-flight</c>.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ALogRowIsWrittenInFlightAndSettledOnTheSameRow()
    {
        using var scratch = ScratchDirectory.Create("sqlite-log");
        using var store = SessionStore.OpenForWriting(Path.Combine(scratch.Path, SessionStore.DataFileName));

        var hung = store.AppendLog(Now(), "browser_navigate", "the one that never came back", SessionStore.InFlight);
        var answered = store.AppendLog(Now(), "browser_click", "the one that did", SessionStore.InFlight);

        await Assert.That(answered).IsGreaterThan(hung);

        var payload = Encoding.UTF8.GetBytes("{\"error\":{\"code\":-32000,\"message\":\"the child said no\"}}");

        await Assert.That(store.Settle(answered, SessionStore.Failed, Now(), payload)).IsTrue();
        await Assert.That(store.Settle(hung + answered + 1_000, SessionStore.Successful, Now(), null)).IsFalse();

        var rows = store.Log();

        await Assert.That(rows.Count).IsEqualTo(2);

        // The hung call is still in flight, has no settled time, and carries no
        // payload -- which is the state a reader has to be able to render as
        // "no answer was recorded".
        await Assert.That(rows[0].Outcome).IsEqualTo(SessionStore.InFlight);
        await Assert.That(rows[0].SettledAt).IsNull();
        await Assert.That(rows[0].Failure).IsNull();

        // The settled one carries the outcome, the time and the child's exact
        // bytes.
        await Assert.That(rows[1].Outcome).IsEqualTo(SessionStore.Failed);
        await Assert.That(rows[1].SettledAt).IsNotNull();
        await Assert.That(rows[1].Failure).IsNotNull();
        await Assert.That(Encoding.UTF8.GetString(rows[1].Failure!)).IsEqualTo(Encoding.UTF8.GetString(payload));

        // A successful settle stores no payload, which is the decision that
        // keeps the common path small.
        await Assert.That(store.Settle(hung, SessionStore.Successful, Now(), null)).IsTrue();
        await Assert.That(store.Log()[0].Failure).IsNull();
        await Assert.That(store.Log()[0].Outcome).IsEqualTo(SessionStore.Successful);
    }

    /// <summary>
    /// A stored value carrying U+0000 comes back whole.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is the interop defect the obvious spelling produces.</b>
    /// <c>StringMarshalling.Utf8</c> plus a byte count of <c>-1</c> — which is
    /// what every example writes — tells SQLite to read to the first zero byte,
    /// and U+0000 encodes as exactly that. The value is then stored truncated,
    /// read back truncated, and nothing anywhere reports it.
    /// </para>
    /// <para>
    /// <b>It is not hypothetical for this product.</b> A <c>why</c> is the
    /// caller's own text, uncapped by decision, and the sanitiser that will
    /// neutralise control characters runs above this layer rather than inside
    /// it — so the storage layer has to be the thing that stores what it was
    /// handed.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AStoredValueCarryingAZeroByteComesBackWhole()
    {
        using var scratch = ScratchDirectory.Create("sqlite-nul");
        using var store = SessionStore.OpenForWriting(Path.Combine(scratch.Path, SessionStore.DataFileName));

        const string Why = "before\0after";

        _ = store.AppendLog(Now(), "browser_navigate", Why, SessionStore.InFlight);

        var stored = store.Log()[0].Why;

        await Assert.That(stored).IsEqualTo(Why);
        await Assert.That(stored.Length).IsEqualTo(Why.Length);

        // The empty string is the other end of the same problem: a zero-length
        // buffer marshals to a pointer SQLite reads as SQL NULL, so an empty
        // `why` would be stored as "there was no why at all".
        _ = store.AppendLog(Now(), "browser_click", string.Empty, SessionStore.InFlight);

        await Assert.That(store.Log()[1].Why).IsEqualTo(string.Empty);
        await Assert.That(store.Log()[1].Why).IsNotNull();
    }

    /// <summary>
    /// Nothing in the store is capped: not a value's length, not the number of
    /// rows.
    /// </summary>
    /// <remarks>
    /// <b>The maintainer's explicit decision, asserted rather than assumed.</b>
    /// The record this replaces capped a <c>why</c> at 400 characters, a purpose
    /// at 2,000 and the log at 250 entries, and every one of those caps existed
    /// because an append rewrote the whole file durably. An append is now an
    /// <c>INSERT</c>, so the caps have no reason left — and a cap that came back
    /// silently would be the same silent data loss under a new name.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NothingInTheStoreIsCappedByLengthOrByCount()
    {
        using var scratch = ScratchDirectory.Create("sqlite-uncapped");
        using var store = SessionStore.OpenForWriting(Path.Combine(scratch.Path, SessionStore.DataFileName));

        // Comfortably past every cap the old record carried, and past the
        // 2,048-character client truncation budget as well, so the number is
        // chosen against the constraints it is about rather than at random.
        var essay = new string('w', 100_000);

        _ = store.AppendLog(Now(), "browser_navigate", essay, SessionStore.InFlight);
        store.Append(new StoredStatement("purpose", Now(), essay));

        await Assert.That(store.Log()[0].Why.Length).IsEqualTo(essay.Length);
        await Assert.That(store.Statements()[0].Value.Length).IsEqualTo(essay.Length);

        // And the count, which is where the 250-entry cap used to bite.
        for (var index = 0; index < 500; index++)
        {
            _ = store.AppendLog(Now(), "browser_click", index.ToString(CultureInfo.InvariantCulture), SessionStore.InFlight);
        }

        await Assert.That(store.LogLength()).IsEqualTo(501);

        // The first entry is still the first entry: nothing evicted out of the
        // middle, which is what made paging unstable in the shape this replaces.
        await Assert.That(store.Log(skip: 0, take: 1)[0].Why.Length).IsEqualTo(essay.Length);
        await Assert.That(store.Log(skip: 500, take: 1)[0].Why).IsEqualTo("499");
    }

    /// <summary>
    /// A reader opened while the writer is holding the session sees what the
    /// writer has just appended.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is property two of the guard, and it is the property every
    /// database-as-the-guard design fails.</b> A session another BrowserAI is
    /// driving is the case a reader exists for, so a reader that opened
    /// successfully and then showed the session as of its start would satisfy
    /// the letter of *readers proceed* and none of its purpose — a confident
    /// wrong answer rather than a refusal.
    /// </para>
    /// <para>
    /// <b>The lock file is held for the whole of it</b>, because *while the
    /// writer holds* is the condition being tested and a reader that only
    /// worked after the guard let go would prove nothing.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AReaderSeesWhatALiveWriterHasJustAppended()
    {
        using var scratch = ScratchDirectory.Create("sqlite-live-read");

        var lockFile = Path.Combine(scratch.Path, LockFile.FileName);
        var data = Path.Combine(scratch.Path, SessionStore.DataFileName);

        using var hold = LockFile.TakeAndWrite(lockFile, LockFileHolder.ForThisProcess());
        using var writer = SessionStore.OpenForWriting(data);

        writer.RecordAcquisition([new StoredStatement("purpose", Now(), "be read from while it is being written to")]);

        var first = writer.AppendLog(Now(), "browser_navigate", "the first call", SessionStore.InFlight);

        using (var reader = SessionStore.OpenForReading(data))
        {
            await Assert.That(reader.IsWritable).IsFalse();
            await Assert.That(reader.LogLength()).IsEqualTo(1);
            await Assert.That(reader.Statements()[0].Value).IsEqualTo("be read from while it is being written to");
        }

        // Now write more, with the guard still held, and open a second reader.
        _ = writer.Settle(first, SessionStore.Successful, Now(), null);
        _ = writer.AppendLog(Now(), "browser_click", "the second call", SessionStore.InFlight);

        using (var reader = SessionStore.OpenForReading(data))
        {
            await Assert.That(reader.LogLength()).IsEqualTo(2);
            await Assert.That(reader.Log()[0].Outcome).IsEqualTo(SessionStore.Successful);
            await Assert.That(reader.Log()[1].Why).IsEqualTo("the second call");
        }

        // And the guard was held throughout, which is the condition all of the
        // above was under: a peer asking to write is still refused.
        await Assert.That(LockFile.Probe(lockFile).State).IsEqualTo(LockFileState.Held);
        await Assert.That(hold.IsHeld).IsTrue();
    }

    /// <summary>
    /// ⚠️ A read-only open against a write-ahead log nobody checkpointed is
    /// refused, and the next read-write open recovers it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a characterisation test, not a regression test.</b> It pins
    /// behaviour that is accepted rather than fixed: when a holder dies without
    /// closing, its <c>-wal</c> carries committed transactions the store file
    /// does not, and recovering them means <i>writing</i> the wal-index, which a
    /// read-only connection cannot do. So a crashed session reads as unreadable
    /// until somebody opens it for writing — which the next acquisition does.
    /// </para>
    /// <para>
    /// <b>Why that is the right trade.</b> The alternative shape — a read-only
    /// reader that ignored the <c>-wal</c> and answered from the store file
    /// alone — would report a session's history as of its last checkpoint,
    /// confidently, with nothing saying the newest part was missing. This
    /// repository has spent two hazard rows closing exactly that class of
    /// answer. A refusal a caller can act on is worth more than a number that
    /// is quietly old.
    /// </para>
    /// <para>
    /// <b>The crash is constructed rather than staged.</b> The state that
    /// matters is *a valid store file plus a hot <c>-wal</c> and no
    /// <c>-shm</c>*, and copying both out from under a live writer produces it
    /// exactly, deterministically, without killing a process — the <c>-shm</c>
    /// is explicitly not persistent state, so leaving it behind is faithful
    /// rather than convenient.
    /// </para>
    /// <para>
    /// <b>Its own positive control is the third step</b>: the same read-only
    /// open against the same file succeeds once the log has been folded back,
    /// so the refusal is about the hot log and not about read-only opens.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AReadOnlyOpenAgainstAnUncheckpointedWalIsRefusedUntilSomebodyWritesToIt()
    {
        using var scratch = ScratchDirectory.Create("sqlite-hot-wal");

        var live = Path.Combine(scratch.Path, "live");
        var dead = Path.Combine(scratch.Path, "dead");

        _ = Directory.CreateDirectory(live);
        _ = Directory.CreateDirectory(dead);

        var livePath = Path.Combine(live, SessionStore.DataFileName);
        var deadPath = Path.Combine(dead, SessionStore.DataFileName);

        // A clean close first, so the schema is in the store file itself and the
        // -wal that follows carries nothing but the log row. Without this the
        // copy would be refused for having no schema, which is a different
        // failure wearing the same message.
        SessionStore.OpenForWriting(livePath).Dispose();

        using (var writer = SessionStore.OpenForWriting(livePath))
        {
            _ = writer.AppendLog(Now(), "browser_navigate", "committed into the log and never folded back", SessionStore.InFlight);

            CopyWhileHeld(livePath, deadPath);
            CopyWhileHeld(livePath + "-wal", deadPath + "-wal");
        }

        // The state a killed holder leaves: a store file, a hot log beside it,
        // and no shared-memory index.
        await Assert.That(File.Exists(deadPath)).IsTrue();
        await Assert.That(new FileInfo(deadPath + "-wal").Length).IsGreaterThan(0);
        await Assert.That(File.Exists(deadPath + "-shm")).IsFalse();

        using (var denial = DirectoryDenial.Apply(dead, FileSystemRights.CreateFiles, InheritanceFlags.None, PropagationFlags.None))
        {
            var refused = Assert.Throws<SqliteException>(() => SessionStore.OpenForReading(deadPath).Dispose());

            await Assert.That(refused).IsNotNull();
            await Assert.That(refused!.Message).Contains(deadPath);
            await Assert.That(refused.Result).IsEqualTo(Sqlite.CannotOpen);

            // Nothing was created despite the attempt, which is the other half
            // of what "cannot recover" means here.
            await Assert.That(File.Exists(deadPath + "-shm")).IsFalse();
        }

        // The next acquisition is a read-write open, and it recovers the log.
        using (var recovered = SessionStore.OpenForWriting(deadPath))
        {
            await Assert.That(recovered.LogLength()).IsEqualTo(1);
            await Assert.That(recovered.Log()[0].Why).IsEqualTo("committed into the log and never folded back");
        }

        // The positive control: the same read-only open, on the same file, now
        // works -- so the refusal above was about the wal-index it could not
        // build rather than about opening read-only at all.
        using var reader = SessionStore.OpenForReading(deadPath);

        await Assert.That(reader.LogLength()).IsEqualTo(1);
    }

    /// <summary>
    /// ⚠️ Where the directory is writable, a read-only open recovers a crashed
    /// holder's log itself — and writes a file into the directory to do it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured 2026-08-26, and it corrects the assumption this phase was
    /// planned on.</b> The design note said a crashed holder's log is
    /// unreadable to a read-only caller until the next acquisition. On Windows
    /// with an ordinary writable session directory that is <b>false</b>:
    /// <c>SQLITE_OPEN_READONLY</c> constrains the main database file and not
    /// the shared-memory index, so the connection creates the <c>-shm</c>,
    /// recovers the log, and answers with the crashed session's newest rows.
    /// </para>
    /// <para>
    /// <b>Which is the better outcome, and it still has a consequence worth
    /// pinning.</b> A read-only caller gets the truth rather than a refusal,
    /// so the accepted-failure paragraph applies only to the read-only-directory
    /// case that
    /// <see cref="AReadOnlyOpenAgainstAnUncheckpointedWalIsRefusedUntilSomebodyWritesToIt"/>
    /// pins. What it costs is that <i>reading</i> a session directory is not a
    /// side-effect-free act: a <c>-shm</c> appears beside the store, and a
    /// caller that asserted a directory's file list after a read would find one
    /// more file than it put there.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AReadOnlyOpenInAWritableDirectoryRecoversACrashedLogAndLeavesAnIndexBehind()
    {
        using var scratch = ScratchDirectory.Create("sqlite-hot-wal-writable");

        var live = Path.Combine(scratch.Path, "live");
        var dead = Path.Combine(scratch.Path, "dead");

        _ = Directory.CreateDirectory(live);
        _ = Directory.CreateDirectory(dead);

        var livePath = Path.Combine(live, SessionStore.DataFileName);
        var deadPath = Path.Combine(dead, SessionStore.DataFileName);

        SessionStore.OpenForWriting(livePath).Dispose();

        using (var writer = SessionStore.OpenForWriting(livePath))
        {
            _ = writer.AppendLog(Now(), "browser_navigate", "committed by a holder that never closed", SessionStore.InFlight);

            CopyWhileHeld(livePath, deadPath);
            CopyWhileHeld(livePath + "-wal", deadPath + "-wal");
        }

        await Assert.That(File.Exists(deadPath + "-shm")).IsFalse();

        using (var reader = SessionStore.OpenForReading(deadPath))
        {
            // The rows are there, so the read-only connection recovered the log
            // rather than answering from the store file alone -- which is the
            // reading that would have been a confident wrong answer.
            await Assert.That(reader.LogLength()).IsEqualTo(1);
            await Assert.That(reader.Log()[0].Why).IsEqualTo("committed by a holder that never closed");
        }

        // And the price: a file appeared in a directory that was only read.
        await Assert.That(File.Exists(deadPath + "-shm")).IsTrue();
    }

    /// <summary>
    /// The SQLite this host loaded kept its mutexes.
    /// </summary>
    /// <remarks>
    /// <b>The one compile-time property that holds under every host</b>, and
    /// therefore the only one the product asserts at run time rather than
    /// reports. sqlite.org's recommended option set includes
    /// <c>SQLITE_THREADSAFE=0</c>; this tree deliberately does not take it,
    /// because BrowserAI reaches storage from an async message loop, a
    /// background sweep, a background update check and an idle timer — and a
    /// library without mutexes corrupts quietly instead of failing.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheLibraryThisHostLoadedKeptItsMutexes()
    {
        Sqlite.RequireASupportedBuild();

        // The positive control: the reader found options at all. An empty list
        // would satisfy "does not report THREADSAFE=0" perfectly.
        await Assert.That(Sqlite.CompileOptions.Count).IsGreaterThan(0);
        await Assert.That(Sqlite.CompileOptions.Any(option => option.StartsWith("THREADSAFE=", StringComparison.Ordinal))).IsTrue();
        await Assert.That(Sqlite.CompileOptions).DoesNotContain("THREADSAFE=0");

        // And the version the library reports is a version, which is the other
        // half of "this is a working library and not a stub that answers
        // nothing".
        await Assert.That(Sqlite.Version).IsNotEqualTo(Sqlite.UnknownVersion);
    }

    /// <summary>
    /// Copies a file that a live SQLite connection is holding open.
    /// </summary>
    /// <remarks>
    /// <b><c>File.Copy</c> cannot do this and the reason is the sharing
    /// arithmetic.</b> It opens the source asking to share reads only, and
    /// SQLite's own open has been <i>granted</i> read and write — so the copy is
    /// refused. Sharing write and delete on the way in is what makes the read a
    /// bystander rather than a second opinion about who owns the file.
    /// </remarks>
    /// <param name="from">The file to copy.</param>
    /// <param name="to">Where to put it.</param>
    private static void CopyWhileHeld(string from, string to)
    {
        using var source = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 65_536);
        using var destination = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 65_536);

        source.CopyTo(destination);
    }
}
