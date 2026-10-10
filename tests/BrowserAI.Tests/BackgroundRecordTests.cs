// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The background's record: written when it starts, marked when it ends cleanly, and
/// read by every relay, so an end without that mark is a crash any relay can name.
/// </summary>
/// <remarks>
/// <para>
/// <b>R, the maintainer's words of 2026-10-08, verbatim: <i>"R I like option 1 and the
/// call response"</i> and <i>"r ok"</i></b>: a background that ends without a clean exit
/// is recorded as crashed, and only a person's start clears the record. These arms hold
/// the file's half of that: whose record a write may touch, what a reader makes of a
/// file it cannot parse, and that a reader never meets half a record.
/// </para>
/// <para>
/// <b>Every record is a real file in a scratch folder</b>, and the identities are this
/// process's own, or this process's pid with a creation time it never had, which is the
/// standing shape of a pid Windows has handed to somebody else. Nothing is started.
/// </para>
/// </remarks>
internal sealed class BackgroundRecordTests
{
    /// <summary>A moment every arm writes, as the background's clock would give it.</summary>
    private static readonly DateTimeOffset StartedAt = HandWrittenRecord.StartedAt;

    /// <summary>
    /// A started record names this process by its pid and its creation time, reads back
    /// as running, and lives under the data root in a file named for the pipe.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-10-09</b> against a record that wrote the pid alone, which
    /// read back with a creation time of zero.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AStartedRecordNamesThisProcessWholeAndReadsBackAsRunning()
    {
        using var data = ScratchDirectory.Create("background-record-started");

        var pipe = BackgroundPipe.NamePrefix + "suite-" + Guid.NewGuid().ToString("N");
        var path = BackgroundRecord.PathFor(data.Path, pipe);

        await Assert.That(path).IsEqualTo(Path.Combine(data.Path, BackgroundRecord.DirectoryName, pipe[(pipe.LastIndexOf('\\') + 1)..] + ".json"));

        var written = BackgroundRecord.Started(path, "9.9.9-record-tests", @"C:\Users\someone\BrowserAI\current\BrowserAI.exe", StartedAt);

        await Assert.That(written.ProcessId).IsEqualTo(Environment.ProcessId);
        await Assert.That(written.CreatedFileTime).IsEqualTo(ProcessLiveness.CreationTimeOfThisProcess());
        await Assert.That(written.Ended).IsNull();

        var read = BackgroundRecord.Read(path);

        await Assert.That(read).IsEqualTo(written);
        await Assert.That(read!.StartedAt).IsEqualTo(StartedAt);
        await Assert.That(read.Build).IsEqualTo("9.9.9-record-tests");
        await Assert.That(read.Image).IsEqualTo(@"C:\Users\someone\BrowserAI\current\BrowserAI.exe");
        await Assert.That(read.EndedAt).IsNull();
        await Assert.That(read.ExitCode).IsNull();
        await Assert.That(read.ExitedAt).IsNull();
        await Assert.That(ProcessLiveness.IsAlive(read.ProcessId, read.CreatedFileTime)).IsTrue();

        // A pipe's characters a file name cannot carry are replaced, and two pipes
        // have two records.
        var odd = BackgroundRecord.PathFor(data.Path, BackgroundPipe.NamePrefix + "suite a:b*c");

        await Assert.That(Path.GetFileName(odd)).IsEqualTo("BrowserAI-Background-suite_a_b_c.json");
        await Assert.That(odd).IsNotEqualTo(path);
    }

    /// <summary>
    /// A clean end is marked only on this process's own record: a record whose pid or
    /// creation time names another process is left exactly as it was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The identity is the pid and its creation time</b>, the standing rule of this
    /// repository: a pid alone is reused within seconds, so a background that ended
    /// cleanly must not mark the record of whoever has its number now.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a mark that compared the pid alone.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACleanEndIsMarkedOnlyOnThisProcesssOwnRecord()
    {
        using var data = ScratchDirectory.Create("background-record-clean");

        var path = Path.Combine(data.Path, BackgroundRecord.DirectoryName, "clean.json");
        var created = ProcessLiveness.CreationTimeOfThisProcess();

        // No record: nothing to mark, and nothing is written.
        await Assert.That(BackgroundRecord.EndedCleanly(path, BackgroundEnd.Stopped, StartedAt)).IsFalse();
        await Assert.That(File.Exists(path)).IsFalse();

        // This pid, another creation time: somebody else's record.
        HandWrittenRecord.Write(path, Environment.ProcessId, created - 1);
        var before = await File.ReadAllBytesAsync(path);

        await Assert.That(BackgroundRecord.EndedCleanly(path, BackgroundEnd.Stopped, StartedAt)).IsFalse();
        await Assert.That((await File.ReadAllBytesAsync(path)).SequenceEqual(before)).IsTrue().Because("a record that was not this write's to touch was rewritten");

        // Another pid, this creation time.
        HandWrittenRecord.Write(path, Environment.ProcessId + 4, created);
        before = await File.ReadAllBytesAsync(path);

        await Assert.That(BackgroundRecord.EndedCleanly(path, BackgroundEnd.SessionEnding, StartedAt)).IsFalse();
        await Assert.That((await File.ReadAllBytesAsync(path)).SequenceEqual(before)).IsTrue().Because("a record that was not this write's to touch was rewritten");

        // This process's own: marked, with how and when.
        _ = BackgroundRecord.Started(path, "9.9.9-record-tests", "image", StartedAt);

        var endedAt = StartedAt.AddHours(3);

        await Assert.That(BackgroundRecord.EndedCleanly(path, BackgroundEnd.EndCommand, endedAt)).IsTrue();

        var read = BackgroundRecord.Read(path)!;

        await Assert.That(read.Ended).IsEqualTo(BackgroundEnd.EndCommand);
        await Assert.That(read.EndedAt).IsEqualTo(endedAt);
        await Assert.That(read.ProcessId).IsEqualTo(Environment.ProcessId);
        await Assert.That(read.StartedAt).IsEqualTo(StartedAt);
    }

    /// <summary>
    /// What a relay saw when its background went is written only into the record of
    /// the identity it names, and only when no clean end is recorded there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The design's "Where the reason can be found"</b>: a crash that wrote nothing
    /// still leaves its exit code with the relay that held a handle on it, and the first
    /// relay that finds it gone writes when. A background that ended cleanly is not a
    /// crash, and nothing a relay writes may make it read as one.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a write that ignored a recorded clean end.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnExitIsWrittenOnlyForTheIdentityItNamesAndOnlyWhenNoCleanEndIsRecorded()
    {
        using var data = ScratchDirectory.Create("background-record-exited");

        var path = Path.Combine(data.Path, BackgroundRecord.DirectoryName, "exited.json");
        const int Pid = 31337;
        const long Created = 133_700_000_000_000_000;
        var exitedAt = StartedAt.AddMinutes(42);

        await Assert.That(BackgroundRecord.Exited(path, Pid, Created, 3, exitedAt)).IsFalse().Because("there is no record to write into");
        await Assert.That(File.Exists(path)).IsFalse();

        HandWrittenRecord.Write(path, Pid, Created);
        var before = await File.ReadAllBytesAsync(path);

        // Another identity: neither the pid nor the creation time may differ.
        await Assert.That(BackgroundRecord.Exited(path, Pid + 1, Created, 3, exitedAt)).IsFalse();
        await Assert.That(BackgroundRecord.Exited(path, Pid, Created + 1, 3, exitedAt)).IsFalse();
        await Assert.That((await File.ReadAllBytesAsync(path)).SequenceEqual(before)).IsTrue().Because("a record that was not this write's to touch was rewritten");

        // Its own: the exit code and the time.
        await Assert.That(BackgroundRecord.Exited(path, Pid, Created, unchecked((int)0xC0000005), exitedAt)).IsTrue();

        var crashed = BackgroundRecord.Read(path)!;

        await Assert.That(crashed.ExitCode).IsEqualTo(unchecked((int)0xC0000005));
        await Assert.That(crashed.ExitedAt).IsEqualTo(exitedAt);
        await Assert.That(crashed.Ended).IsNull();

        // A relay that found it gone and could read no code writes the time alone.
        HandWrittenRecord.Write(path, Pid, Created);

        await Assert.That(BackgroundRecord.Exited(path, Pid, Created, exitCode: null, exitedAt)).IsTrue();
        await Assert.That(BackgroundRecord.Read(path)!.ExitCode).IsNull();
        await Assert.That(BackgroundRecord.Read(path)!.ExitedAt).IsEqualTo(exitedAt);

        // A record that says it ended cleanly is never written over.
        HandWrittenRecord.Write(path, Pid, Created, ended: nameof(BackgroundEnd.SessionEnding));
        before = await File.ReadAllBytesAsync(path);

        await Assert.That(BackgroundRecord.Exited(path, Pid, Created, 1, exitedAt)).IsFalse();
        await Assert.That((await File.ReadAllBytesAsync(path)).SequenceEqual(before)).IsTrue().Because("a record that was not this write's to touch was rewritten");
        await Assert.That(BackgroundRecord.Read(path)!.Ended).IsEqualTo(BackgroundEnd.SessionEnding);
    }

    /// <summary>
    /// A cleared record is gone, and clearing a record that is not there, or whose
    /// folder is not there, is nothing to do.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-10-09</b> against a clear that let a missing folder throw:
    /// a person's start on a machine where no background ever ran would have failed.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AClearedRecordIsGoneAndClearingNothingIsNothingToDo()
    {
        using var data = ScratchDirectory.Create("background-record-clear");

        var path = Path.Combine(data.Path, BackgroundRecord.DirectoryName, "cleared.json");

        _ = BackgroundRecord.Started(path, "9.9.9-record-tests", "image", StartedAt);

        BackgroundRecord.Clear(path);

        await Assert.That(File.Exists(path)).IsFalse();
        await Assert.That(BackgroundRecord.Read(path)).IsNull();

        // Again, with the folder still there and the file gone.
        BackgroundRecord.Clear(path);

        // And with no folder at all.
        BackgroundRecord.Clear(Path.Combine(data.Path, "no-such-folder", BackgroundRecord.DirectoryName, "never.json"));

        await Assert.That(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!)).IsEmpty();
    }

    /// <summary>
    /// A record that is missing, empty, not JSON, cut short, or missing or mistyping a
    /// member reads as no record at all, never as a running or a crashed background.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The safe direction</b>, as the record's own remarks put it: a relay then says
    /// no background runs, not that one crashed, and a person's start starts one.
    /// </para>
    /// <para>
    /// <b>The positive control is the last record</b>, written by hand in the same
    /// shape and read back whole, so a reader that answered nothing for everything
    /// would fail here.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a reader whose filter let a start time that
    /// is not a time escape as a <see cref="FormatException"/>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARecordThatCannotBeReadIsReadAsNoRecord()
    {
        using var data = ScratchDirectory.Create("background-record-unreadable");

        var path = Path.Combine(data.Path, BackgroundRecord.DirectoryName, "unreadable.json");

        await Assert.That(BackgroundRecord.Read(path)).IsNull().Because("the folder does not exist");

        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await Assert.That(BackgroundRecord.Read(path)).IsNull().Because("the file does not exist");

        string[] unreadable =
        [
            string.Empty,
            "not a record at all",
            """{"pid":7,"created":1,"startedAt":"2026-10-09T06:00:00.0000000+00:00","build":"9.9.9""",
            "[]",
            "{}",
            """{"created":1,"startedAt":"2026-10-09T06:00:00.0000000+00:00","build":"9.9.9","image":"x"}""",
            """{"pid":"seven","created":1,"startedAt":"2026-10-09T06:00:00.0000000+00:00","build":"9.9.9","image":"x"}""",
            """{"pid":7,"startedAt":"2026-10-09T06:00:00.0000000+00:00","build":"9.9.9","image":"x"}""",
            """{"pid":7,"created":1,"startedAt":"yesterday at six","build":"9.9.9","image":"x"}""",
            """{"pid":7,"created":1,"startedAt":42,"build":"9.9.9","image":"x"}""",
            """{"pid":7,"created":1,"startedAt":"2026-10-09T06:00:00.0000000+00:00","build":"9.9.9","image":"x","exitedAt":"not a time"}""",
        ];

        foreach (var text in unreadable)
        {
            await File.WriteAllTextAsync(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            await Assert.That(BackgroundRecord.Read(path)).IsNull().Because($"'{text}' was read as a record");
        }

        // The positive control.
        HandWrittenRecord.Write(path, 7, 1, ended: nameof(BackgroundEnd.Update));

        var readable = BackgroundRecord.Read(path);

        await Assert.That(readable).IsNotNull();
        await Assert.That(readable!.ProcessId).IsEqualTo(7);
        await Assert.That(readable.Ended).IsEqualTo(BackgroundEnd.Update);
    }

    /// <summary>
    /// A refused start's record says what was refused and the remedy, read back whole,
    /// and an end this build does not know reads as the clean end a later build wrote,
    /// never as no end at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The maintainer's 9 a, 2026-10-10.</b> Two hazard rows of lane ARCH's helper T1
    /// and T2 close here: a refused root was recorded as a crash, and an <c>ended</c>
    /// this build could not parse read as absent, which is a crash, so after a downgrade
    /// every relay told every call to report a bug. A name is read exactly: a number or
    /// a list of names, which <see cref="Enum.TryParse{TEnum}(string?, out TEnum)"/>
    /// would take for a known end, is a name this build does not know.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a reader that read an <c>ended</c> it could
    /// not parse as no end, as the reader before this arm did.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARefusalReadsBackWholeAndAnEndThisBuildDoesNotKnowIsStillAnEnd()
    {
        using var data = ScratchDirectory.Create("background-record-refused");

        var path = Path.Combine(data.Path, BackgroundRecord.DirectoryName, "refused.json");
        var refusal = new BrowserAI.Hosting.RootRefusal(
            BrowserAI.Hosting.JudgedRoot.Data,
            @"D:\Shared\BrowserAI",
            "it is outside this user's profile, so it is not storage Windows keeps per-user",
            @"give BrowserAI a data root under 'C:\Users\someone'.");

        var written = BackgroundRecord.Refused(path, "9.9.9-record-tests", "image", refusal, StartedAt);
        var read = BackgroundRecord.Read(path)!;

        await Assert.That(read).IsEqualTo(written);
        await Assert.That(read.ProcessId).IsEqualTo(Environment.ProcessId);
        await Assert.That(read.Ended).IsEqualTo(BackgroundEnd.Refused);
        await Assert.That(read.EndedAt).IsEqualTo(StartedAt);
        await Assert.That(read.Refusal).IsEqualTo(refusal);

        // A refusal missing one of its parts is a refusal with no parts, and still a refusal.
        var partial = (await File.ReadAllTextAsync(path)).Replace("\"remedy\"", "\"remedies\"", StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, partial, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        await Assert.That(BackgroundRecord.Read(path)!.Ended).IsEqualTo(BackgroundEnd.Refused);
        await Assert.That(BackgroundRecord.Read(path)!.Refusal).IsNull();

        // A later build's end: a clean end all the same, whatever it is called.
        foreach (var unknown in new[] { "SomethingALaterBuildWrites", "3", "Stopped, Update", "stopped" })
        {
            HandWrittenRecord.Write(path, 7, 1, ended: unknown);

            await Assert.That(BackgroundRecord.Read(path)!.Ended).IsEqualTo(BackgroundEnd.Unrecognised).Because($"'{unknown}' read as something else");
        }

        // An empty name is no end, and so is no name: the crash R names.
        HandWrittenRecord.Write(path, 7, 1, ended: string.Empty);

        await Assert.That(BackgroundRecord.Read(path)!.Ended).IsNull();

        HandWrittenRecord.Write(path, 7, 1);

        await Assert.That(BackgroundRecord.Read(path)!.Ended).IsNull();
    }

    /// <summary>
    /// A reader that opened the record before a write reads the old record entire,
    /// whatever became of the write; once the reader has gone, the write goes through,
    /// the next reader meets the new record whole, and nothing is left beside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The record's own remarks</b>: it is written to a file beside it and renamed
    /// over it, so a reader meets the old record or the new one and never half of
    /// either. A write in place would truncate the file under a reader that had it open,
    /// and that reader would read the new bytes, or none, through its old handle.
    /// </para>
    /// <para>
    /// ⚠️ <b>The write itself fails while the reader holds the record, measured
    /// 2026-10-09 in this arm's first run</b>: <c>File.Move</c> with
    /// <c>overwrite</c> answered <see cref="UnauthorizedAccessException"/>, because
    /// Windows renames a file over another only when nobody has the target open, and
    /// sharing it for deletion does not change that. The record stays whole, which is
    /// what this arm holds; that the write is lost, and that
    /// <see cref="BackgroundRecord.Started"/> and
    /// <see cref="BackgroundRecord.EndedCleanly"/> let the failure escape, is reported
    /// to lane ARCH and not held here either way. <i>Fixed 2026-10-09 by lane ARCH:</i>
    /// a write now waits out a reader for <see cref="BackgroundRecord.WriteBound"/>,
    /// <see cref="BackgroundRecord.EndedCleanly"/> never throws, the background serves
    /// on when its start cannot be recorded, and the arm below holds the write's side.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a write straight into the record's own
    /// file.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AReaderOfTheRecordReadsItWholeWhateverAWriteDoes()
    {
        using var data = ScratchDirectory.Create("background-record-whole");

        var path = Path.Combine(data.Path, BackgroundRecord.DirectoryName, "whole.json");

        _ = BackgroundRecord.Started(path, "9.9.9-record-tests", "image", StartedAt);

        var old = await File.ReadAllBytesAsync(path);
        string written;

        // A reader that opened the record before the write, sharing it as widely as a
        // reader can.
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            try
            {
                written = BackgroundRecord.EndedCleanly(path, BackgroundEnd.Stopped, StartedAt.AddHours(1)) ? "written" : "not this process's record";
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                written = $"refused: {failure.GetType().Name}, {failure.Message}";
            }

            var seen = new byte[old.Length + 1024];
            var length = 0;
            int read;

            while ((read = await reader.ReadAsync(seen.AsMemory(length))) > 0)
            {
                length += read;
            }

            await Assert.That(seen.Take(length).SequenceEqual(old)).IsTrue()
                .Because($"a reader that had the record open met a record that was being written; the write was {written}");
        }

        // The reader has gone: the write goes through, whole.
        var endedAt = StartedAt.AddHours(2);

        await Assert.That(BackgroundRecord.EndedCleanly(path, BackgroundEnd.Stopped, endedAt)).IsTrue();

        var record = BackgroundRecord.Read(path)!;

        await Assert.That(record.Ended).IsEqualTo(BackgroundEnd.Stopped);
        await Assert.That(record.EndedAt).IsEqualTo(endedAt);
        await Assert.That(string.Join(" | ", Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!).Select(Path.GetFileName)))
            .IsEqualTo(Path.GetFileName(path)).Because("a write left a file of its own beside the record");
    }

    /// <summary>
    /// A write waits out a reader that has the record open and then goes through
    /// whole, and one that cannot get through within the write bound gives up; neither
    /// leaves anything beside the record.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The defect the arm above found</b>: Windows renames a file over another only
    /// when nobody has the target open, so a relay or a person's start reading the
    /// record at the moment the background wrote it made the write fail, and at the
    /// background's start that failure ended the process with exit code 1.
    /// <see cref="BackgroundRecord.WriteBound"/> covers a read that takes one call.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> with the rename tried once: the write gave up at
    /// once while the reader held the record.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AWriteWaitsOutAReaderAndOneThatCannotGetThroughLeavesNothingBesideTheRecord()
    {
        using var data = ScratchDirectory.Create("background-record-waits");

        var path = Path.Combine(data.Path, BackgroundRecord.DirectoryName, "waits.json");

        _ = BackgroundRecord.Started(path, "9.9.9-record-tests", "image", StartedAt);

        Task<bool> ending;

        // A reader that opened the record the way BackgroundRecord.Read does, held for a
        // tenth of the bound.
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ending = Task.Run(() => BackgroundRecord.EndedCleanly(path, BackgroundEnd.Stopped, StartedAt.AddHours(1)));

            await Task.Delay(BackgroundRecord.WriteBound / 10);

            await Assert.That(ending.IsCompleted).IsFalse()
                .Because("a write gave up while a reader held the record for a tenth of its bound");
        }

        await Assert.That(await ending.WaitAsync(TestDefaults.InProcessHang)).IsTrue();
        await Assert.That(BackgroundRecord.Read(path)!.Ended).IsEqualTo(BackgroundEnd.Stopped);
        await Assert.That(Beside(path)).IsEqualTo(Path.GetFileName(path)).Because("a write that waited left a file of its own beside the record");

        // A reader that holds it past the bound: the write gives up, the record is as it
        // was, and nothing is left beside it.
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.That(BackgroundRecord.EndedCleanly(path, BackgroundEnd.Update, StartedAt.AddHours(2))).IsFalse();
        }

        await Assert.That(BackgroundRecord.Read(path)!.Ended).IsEqualTo(BackgroundEnd.Stopped);
        await Assert.That(Beside(path)).IsEqualTo(Path.GetFileName(path)).Because("a write that gave up left a file of its own beside the record");
    }

    /// <summary>Every name in the record's folder, joined.</summary>
    /// <param name="path">The record's path.</param>
    /// <returns>The names.</returns>
    private static string Beside(string path) =>
        string.Join(" | ", Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!).Select(Path.GetFileName));
}
