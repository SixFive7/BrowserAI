// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using BrowserAI.Storage;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// <c>browserai.lock</c> — the guard, on its own, before anything is wired to
/// it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Six properties, and every one of them is a property of the kernel rather
/// than of this code.</b> One writer per directory comes from the share mode;
/// readers proceed because the same share mode admits them; the hold lasts the
/// session because nothing closes it; the OS releases it on death; the probe is
/// one <c>CreateFile</c>; and the holder is named because it is written inside.
/// What these tests can hold is that the two load-bearing literals are the ones
/// that produce those properties, and that the probe's answers are the three it
/// claims.
/// </para>
/// <para>
/// <b>Within one process, and that is a real limit stated rather than
/// glossed.</b> Windows applies its sharing rules per handle rather than per
/// process, so a second <c>FileStream</c> here is refused by exactly the same
/// arithmetic a second BrowserAI would be — which is why the probe arms are
/// meaningful. What a single process cannot show is the fourth property,
/// release-on-death; <c>SessionLockTests</c> owns that across real processes
/// for the record this replaces, and it moves with the cutover rather than
/// being duplicated here.
/// </para>
/// </remarks>
internal sealed class LockFileTests
{
    /// <summary>
    /// The probe tells apart a directory nothing has taken, one a live holder
    /// has, and one whose holder let go.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The third state is the one the record this replaces could not
    /// have.</b> That record was rewritten on every forwarded call, so its name
    /// was unbound for milliseconds at a time and an absence had to be read as
    /// *undetermined* — a record being replaced as often as a session that had
    /// gone. Nothing rewrites this file, so an absence is an absence, and a
    /// file that opens is a holder that has released.
    /// </para>
    /// <para>
    /// <b>Held is the kernel's own answer and the only one read
    /// positively.</b> The other two are read off what is on disk, so the order
    /// here is the file's own life: nothing, then held, then released, then
    /// nothing again.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheProbeTellsFreeFromHeldFromReleased()
    {
        using var scratch = ScratchDirectory.Create("lockfile-states");

        var path = Path.Combine(scratch.Path, LockFile.FileName);

        await Assert.That(LockFile.Probe(path).State).IsEqualTo(LockFileState.Free);
        await Assert.That(LockFile.Probe(path).Why).IsNull();

        var hold = LockFile.TakeAndWrite(path, LockFileHolder.ForThisProcess());

        try
        {
            await Assert.That(LockFile.Probe(path).State).IsEqualTo(LockFileState.Held);
            await Assert.That(LockFile.Probe(path).Why).IsNull();
        }
        finally
        {
            hold.Dispose();
        }

        // Released: the file is still there and names who had it, and nobody
        // has it now. This is what a killed session leaves behind.
        await Assert.That(LockFile.Probe(path).State).IsEqualTo(LockFileState.Released);
        await Assert.That(LockFile.Read(path)).IsNotNull();

        File.Delete(path);

        await Assert.That(LockFile.Probe(path).State).IsEqualTo(LockFileState.Free);
        await Assert.That(LockFile.Read(path)).IsNull();
    }

    /// <summary>
    /// A probe that cannot answer says so, and never says free.
    /// </summary>
    /// <remarks>
    /// <b>The asymmetry is this product's standing rule.</b> A sharing
    /// violation may be read as owned; nothing else may be read as free.
    /// Collapsing a denied open into *nobody has this* hands a caller a
    /// confident wrong answer in the one direction that costs somebody else's
    /// session — and <c>browserai_destroy</c> is the call standing behind that
    /// answer.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AProbeThatCannotAnswerSaysSoRatherThanSayingFree()
    {
        using var scratch = ScratchDirectory.Create("lockfile-undetermined");

        // A directory sitting where the lock file should be: the open fails
        // with an access denial rather than with a sharing violation or an
        // absence, which is the shape every "something else is wrong here"
        // failure has.
        var path = Path.Combine(scratch.Path, LockFile.FileName);

        _ = Directory.CreateDirectory(path);

        var answer = LockFile.Probe(path);

        await Assert.That(answer.State).IsEqualTo(LockFileState.Undetermined);
        await Assert.That(answer.Why).IsNotNull();
        await Assert.That(answer.Why!).Contains(path);
        await Assert.That(answer.Why!).Contains("not a sharing violation");
    }

    /// <summary>
    /// A holder refuses a second writer and admits every reader.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The two load-bearing literals, asserted through their consequences
    /// rather than by reading the source.</b> <c>FileShare.Read</c> on the hold
    /// is what refuses a peer; <c>FileAccess.ReadWrite</c> on the probe is what
    /// that refusal is triggered by. Neither can be weakened without one of the
    /// two assertions below going red.
    /// </para>
    /// <para>
    /// <b>The reader arm is the half that is easy to lose.</b> A guard that
    /// refused readers too would pass every ownership test in this file and
    /// break the one call that exists to read a session another BrowserAI is
    /// driving.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AHolderRefusesASecondWriterAndAdmitsEveryReader()
    {
        using var scratch = ScratchDirectory.Create("lockfile-sharing");

        var path = Path.Combine(scratch.Path, LockFile.FileName);

        using var hold = LockFile.TakeAndWrite(path, LockFileHolder.ForThisProcess());

        // A second holder is refused, and refused with a sharing violation
        // rather than with anything else.
        var refused = Assert.Throws<IOException>(() => LockFile.Hold(path).Dispose());

        await Assert.That(refused).IsNotNull();
        await Assert.That(BrowserAI.Sessions.RenameWindow.IsSharingViolation(refused!)).IsTrue();

        // Readers are admitted, both through the layer and through a bare
        // FileStream, because a caller that has only a path is the case this
        // property exists for.
        await Assert.That(LockFile.Read(path)).IsNotNull();

        using (var bystander = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            await Assert.That(bystander.Length).IsGreaterThan(0);
        }

        await Assert.That(hold.IsHeld).IsTrue();
    }

    /// <summary>
    /// The lock file names its holder as a process and a creation time, and
    /// never as a pid alone.
    /// </summary>
    /// <remarks>
    /// <b>A pid on its own is not an identity.</b> Windows reuses process ids
    /// within seconds, so a guard that recorded only the number would let a
    /// reclaim take a live stranger's directory — which is why the creation
    /// FILETIME is written beside it and why a file missing one is refused
    /// rather than read.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheLockFileNamesAProcessAndItsCreationTimeAndNotAPidAlone()
    {
        using var scratch = ScratchDirectory.Create("lockfile-holder");

        var path = Path.Combine(scratch.Path, LockFile.FileName);
        var mine = LockFileHolder.ForThisProcess();

        using (var hold = LockFile.TakeAndWrite(path, mine))
        {
            var throughTheHold = hold.ReadHolder();

            await Assert.That(throughTheHold).IsNotNull();
            await Assert.That(throughTheHold!.ProcessId).IsEqualTo(Environment.ProcessId);
            await Assert.That(throughTheHold.ProcessCreatedFileTime).IsEqualTo(mine.ProcessCreatedFileTime);
            await Assert.That(throughTheHold.ProcessCreatedFileTime).IsNotEqualTo(0L);
            await Assert.That(throughTheHold.IsAlive()).IsTrue();
        }

        var fromOutside = LockFile.Read(path);

        await Assert.That(fromOutside).IsEqualTo(mine);

        // The file is text a person can read, in the encoding this repository
        // writes everything in.
        var bytes = ReadWhileHeld(path);
        var text = Encoding.UTF8.GetString(bytes);

        await Assert.That(bytes[0]).IsNotEqualTo((byte)0xEF);
        await Assert.That(text).Contains("\"processId\":");
        await Assert.That(text).Contains("\"processCreatedFileTime\":");
        await Assert.That(text).Contains("\"clientProcessName\":");
        await Assert.That(text).DoesNotContain("\r");

        // And the identity really is two halves: the same pid with somebody
        // else's creation time is not this process.
        await Assert.That(new LockFileHolder(mine.ProcessId, mine.ProcessCreatedFileTime + 1, null).IsAlive()).IsFalse();
    }

    /// <summary>
    /// The lock file is written once and is not touched again for the life of
    /// the session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the window that used to open once per forwarded call, turned
    /// into a permanent guard.</b> The record this replaces closed its own
    /// ownership handle, wrote a temporary file, renamed it over the target and
    /// re-opened — every time anything was appended — so for a few milliseconds
    /// per call the directory was genuinely unheld and its name genuinely
    /// unbound. Everything a session says now goes into a different file, so
    /// this one is written at acquisition and never again.
    /// </para>
    /// <para>
    /// <b>It is asserted against a session's worth of writing, not against
    /// nothing.</b> The store beside it is appended to two hundred times while
    /// the probe is asked after each one; a design that rewrote the guard would
    /// be caught by the bytes, by the timestamp, or by a probe that came back
    /// anything other than held.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheLockFileIsWrittenOnceAndNeverRewrittenMidSession()
    {
        using var scratch = ScratchDirectory.Create("lockfile-once");

        var path = Path.Combine(scratch.Path, LockFile.FileName);

        using var hold = LockFile.TakeAndWrite(path, LockFileHolder.ForThisProcess());
        using var store = SessionStore.OpenForWriting(Path.Combine(scratch.Path, SessionStore.DataFileName));

        var written = ReadWhileHeld(path);
        var stamped = File.GetLastWriteTimeUtc(path);
        var states = new HashSet<LockFileState>();

        for (var index = 0; index < 200; index++)
        {
            _ = store.AppendLog(
                DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
                "browser_click",
                index.ToString(CultureInfo.InvariantCulture),
                SessionStore.InFlight);

            _ = states.Add(LockFile.Probe(path).State);
        }

        // Held at every single look, with no other answer anywhere in the run.
        await Assert.That(states.Count).IsEqualTo(1);
        await Assert.That(states.Contains(LockFileState.Held)).IsTrue();

        // The same bytes and the same timestamp: nothing rewrote it, and no
        // temporary file was left beside it either.
        await Assert.That(ReadWhileHeld(path)).IsEquivalentTo(written);
        await Assert.That(File.GetLastWriteTimeUtc(path)).IsEqualTo(stamped);
        await Assert.That(Directory.GetFiles(scratch.Path, LockFile.TemporaryFilePattern).Length).IsEqualTo(0);

        // And the store really did the writing the guard was being held across.
        await Assert.That(store.LogLength()).IsEqualTo(200);
    }

    /// <summary>
    /// A lock file that is not one of ours is refused rather than guessed at.
    /// </summary>
    /// <remarks>
    /// <b>The set of things a lock file may say is closed.</b> A file carrying
    /// a property this build does not write is somebody else's file, and *this
    /// is not ours* is a different answer from *this directory is free* — one
    /// of them means walk away and the other means take it.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ALockFileThatIsNotOursIsRefusedRatherThanGuessedAt()
    {
        using var scratch = ScratchDirectory.Create("lockfile-strangers");

        var strangers = new[]
        {
            ("not JSON at all", "held by nobody in particular"),
            ("a property we do not write", "{\"processId\":1,\"processCreatedFileTime\":2,\"clientProcessName\":null,\"mood\":\"cheerful\"}"),
            ("no creation time", "{\"processId\":1,\"clientProcessName\":null}"),
            ("no process id", "{\"processCreatedFileTime\":2,\"clientProcessName\":null}"),
            ("no client name", "{\"processId\":1,\"processCreatedFileTime\":2}"),
        };

        foreach (var (what, content) in strangers)
        {
            var path = Path.Combine(scratch.Path, $"{what}.lock");

            await File.WriteAllTextAsync(path, content);

            var failure = Assert.Throws<InvalidDataException>(() => LockFile.Read(path));

            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.Message).Contains(path);
            await Assert.That(failure.Message).Contains("will not guess at ownership");
        }

        // The pid-alone case carries the reason, because it is the one that
        // looks most like a valid record and is the most dangerous to accept.
        var pidOnly = Path.Combine(scratch.Path, "pid-only.lock");

        await File.WriteAllTextAsync(pidOnly, "{\"processId\":1,\"clientProcessName\":null}");

        await Assert.That(Assert.Throws<InvalidDataException>(() => LockFile.Read(pidOnly))!.Message)
            .Contains("a pid on its own is not an identity");

        // The positive control: a file this build did write reads back fine
        // through the same path, so the arms above are refusing content rather
        // than refusing everything.
        var ours = Path.Combine(scratch.Path, "ours.lock");

        using (LockFile.TakeAndWrite(ours, LockFileHolder.ForThisProcess()))
        {
            await Assert.That(LockFile.Read(ours)).IsNotNull();
        }
    }

    /// <summary>
    /// Reads a lock file that a holder may have open.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b><c>File.ReadAllBytes</c> cannot do this, and finding that out is
    /// half the point of the property being tested.</b> It opens sharing reads
    /// only, and a holder's <i>granted</i> access is <c>ReadWrite</c> — so the
    /// framework's convenience method is refused by exactly the mechanism that
    /// makes the guard a guard. Every reader in the product shares write and
    /// delete on the way in for that reason, and a test that used the
    /// convenience method would report the guard working as the guard being
    /// broken.
    /// </remarks>
    /// <param name="path">The lock file.</param>
    /// <returns>Its bytes.</returns>
    private static byte[] ReadWhileHeld(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096);
        using var buffer = new MemoryStream();

        stream.CopyTo(buffer);

        return buffer.ToArray();
    }
}
