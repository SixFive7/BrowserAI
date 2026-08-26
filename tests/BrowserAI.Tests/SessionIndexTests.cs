// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using BrowserAI.Hosting;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// The session-index file layout — the third of the three decisions
/// [DECISIONS → Still open](../../DECISIONS.md#still-open) named as settled on paper
/// and unexercised.
/// </summary>
/// <remarks>
/// <para>
/// <b>The index is deliberately weaker than it looks, and each property here is
/// a property of that weakness.</b> It is never trusted, only followed; it takes
/// no lock; and it repairs itself. So the assertions are about what it does
/// <i>not</i> do — it does not authorise, it does not serialise, and above all
/// it does not touch anything outside its own directory.
/// </para>
/// <para>
/// <b>Every index root here is a scratch root.</b> The real one is machine-wide
/// state, and a test that wrote into it would put throwaway directories into a
/// developer's own <c>browserai_list</c>. <see cref="ScratchRoot"/> additionally
/// reclaims any entry in the real index that points into the scratch tree, so a
/// leak from a run that predates this rule is cleaned rather than inherited.
/// </para>
/// </remarks>
internal sealed class SessionIndexTests
{
    private static readonly string ProbePath = Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe");

    /// <summary>
    /// How long a probe gets to start a runtime and report. Generous on purpose:
    /// a slow machine starting eight processes is the ordinary reason this is
    /// slow, and a tight deadline reports as an index failure.
    /// </summary>
    private static readonly TimeSpan Patience = TestDefaults.ProcessHang;

    /// <summary>How many processes re-assert one entry at the same instant.</summary>
    /// <remarks>
    /// The done-test asks for two. Eight is what the suite pays for, because the
    /// window a rename leaves open is small and two processes can miss each
    /// other entirely — which would pass while proving nothing.
    /// </remarks>
    private const int Writers = 8;

    /// <summary>How many times each writer re-asserts the same entry.</summary>
    private const int WritesEach = 250;

    [Test]
    public async Task InitThenResumeLeavesExactlyOneEntryAndAHandDeletionIsRepairedByTheNextResume()
    {
        using var scratch = ScratchDirectory.Create("index-idempotent");
        var (index, path) = NewIndex(scratch, "session");

        // init: the directory is taken for the first time.
        var first = SessionLock.TryAcquire(path, Request("the first"), NullLogger.Instance);
        await Assert.That(first.Outcome).IsEqualTo(SessionLockOutcome.Acquired);
        index.Record(path);
        first.Acquired!.Dispose();

        // resume: the same directory, taken again. The entry is re-asserted
        // rather than written once, which is the whole reason a lost one heals.
        var second = SessionLock.TryAcquire(path, Request("the second"), NullLogger.Instance);
        await Assert.That(second.Outcome).IsEqualTo(SessionLockOutcome.Reclaimed);
        index.Record(path);
        second.Acquired!.Dispose();

        var entries = Directory.GetFiles(index.Root);
        await Assert.That(entries.Length).IsEqualTo(1);
        await Assert.That(Path.GetFileName(entries[0])).IsEqualTo(path.IndexKey);

        // The path and nothing else, asserted on the bytes. A wrapper object, a
        // trailing newline or a BOM would all round-trip and all be more than
        // this file is allowed to be.
        await Assert.That(Convert.ToHexString(await File.ReadAllBytesAsync(entries[0])))
            .IsEqualTo(Convert.ToHexString(Encoding.UTF8.GetBytes(path.FullPath)));

        var followed = index.Follow();
        await Assert.That(followed.Count).IsEqualTo(1);
        await Assert.That(followed[0].State).IsEqualTo(SessionIndexEntryState.Session);
        await Assert.That(followed[0].Record!.Purpose).IsEqualTo("the second");

        // Deleted by hand, which is a thing a person may do and a thing a wrong
        // sweep may do. Either way the next use puts it back.
        File.Delete(entries[0]);
        await Assert.That(index.Follow().Count).IsEqualTo(0);

        var third = SessionLock.TryAcquire(path, Request("the third"), NullLogger.Instance);
        index.Record(path);
        third.Acquired!.Dispose();

        await Assert.That(Directory.GetFiles(index.Root).Length).IsEqualTo(1);
        await Assert.That(index.Follow()[0].State).IsEqualTo(SessionIndexEntryState.Session);
    }

    [Test]
    public async Task AnEntryPointingAtADeletedDirectoryIsRemovedOnTheNextSweep()
    {
        using var scratch = ScratchDirectory.Create("index-deleted");
        var (index, path) = NewIndex(scratch, "session");

        var lease = SessionLock.TryAcquire(path, Request("about to be deleted"), NullLogger.Instance);
        index.Record(path);
        lease.Acquired!.Dispose();

        await Assert.That(index.Follow()[0].State).IsEqualTo(SessionIndexEntryState.Session);

        // The sweep below can only mean something if the directory really is
        // gone, so the survivors are asserted rather than discarded.
        await Assert.That(string.Join(Environment.NewLine, ScratchDirectory.RemoveTree(path.FullPath))).IsEmpty();

        var sweep = index.Sweep();

        await Assert.That(sweep.Followed).IsEqualTo(1);
        await Assert.That(sweep.Removed.Count).IsEqualTo(1);
        await Assert.That(sweep.Removed[0].State).IsEqualTo(SessionIndexEntryState.DirectoryMissing);
        await Assert.That(Directory.GetFiles(index.Root).Length).IsEqualTo(0);

        // Idempotent: a second sweep over an index that is already clean does
        // nothing and says so.
        var again = index.Sweep();
        await Assert.That(again.Followed).IsEqualTo(0);
        await Assert.That(again.Removed.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AnEntryPointingAtADirectoryWithNoLockFileIsRemovedOnTheNextSweep()
    {
        using var scratch = ScratchDirectory.Create("index-nolock");
        var (index, path) = NewIndex(scratch, "session");

        var lease = SessionLock.TryAcquire(path, Request("about to be destroyed"), NullLogger.Instance);
        index.Record(path);
        lease.Acquired!.Dispose();

        // The directory survives both of its files, which is what a destroy
        // leaves behind if the caller keeps the folder.
        File.Delete(path.LockFile);
        File.Delete(path.DataFile);

        var followed = index.Follow();
        await Assert.That(followed[0].State).IsEqualTo(SessionIndexEntryState.NotASession);
        await Assert.That(followed[0].Problem).Contains(SessionLayout.DataFileName);

        var sweep = index.Sweep();

        await Assert.That(sweep.Removed.Count).IsEqualTo(1);
        await Assert.That(Directory.GetFiles(index.Root).Length).IsEqualTo(0);

        // The sweep removed a pointer and nothing else.
        await Assert.That(Directory.Exists(path.FullPath)).IsTrue();
    }

    /// <summary>
    /// A session whose record is being renamed into place is kept, not swept —
    /// the one ungated reader in the product that ACTED on an absence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The absence is real and is nobody's fault.</b> A guard arrives by
    /// <c>MoveFileEx</c> with <c>MOVEFILE_REPLACE_EXISTING</c>, and the store
    /// beside it is created after that — so between the moment the temp file
    /// exists and the moment the store does, a directory that IS being taken
    /// holds neither of the two files that say so. <c>SessionIndex</c> read that
    /// instant as <i>never was a session</i>, which is a **removable** state,
    /// and dropped a live session out of the only inventory there is. Measured
    /// 2026-08-18 and recorded in the
    /// [hazard index](../../HAZARDS.md#hazard-index).
    /// </para>
    /// <para>
    /// ⚠️ <b>The window narrowed with the cutover and did not close (2026-08-26,
    /// previously "a rename over a file with an open handle is refused, so the
    /// writer sits in its retry loop … the name <c>browserai.json</c> does not
    /// resolve").</b> That window opened on <b>every forwarded call</b>, because
    /// the record was rewritten whole each time. Nothing rewrites either file
    /// now, so what is left is the first acquisition of a directory — once per
    /// session rather than once per call — and the discriminator is unchanged
    /// because the shape on disk is.
    /// </para>
    /// <para>
    /// <b>The window is not raced for here, and it does not need to be.</b> What
    /// it produces on disk is exactly this: a directory with neither file and a
    /// <c>browserai.lock.new-…</c> beside them. Composing that state directly
    /// tests the discriminator rather than the scheduler, and the pattern comes
    /// from <c>SessionLayout.NewLockFilePattern</c> — the same constant the
    /// durable write's name is built from, so a rename of the convention cannot
    /// leave this test passing against a pattern nothing produces.
    /// </para>
    /// <para>
    /// <b>The second arm is what stops the fix being "never sweep anything".</b>
    /// With no temp file beside it the same directory is still swept, which is
    /// the behaviour the test above this one asserts and this one re-asserts
    /// from the other side.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnEntryWhoseRecordIsMidRenameIsKeptAndTheSameEntryWithoutOneIsSwept()
    {
        using var scratch = ScratchDirectory.Create("index-in-flight");
        var (index, path) = NewIndex(scratch, "session");

        var lease = SessionLock.TryAcquire(path, Request("rewriting its own record"), NullLogger.Instance);
        index.Record(path);
        lease.Acquired!.Dispose();

        // Exactly what the window leaves on disk: neither file bound, the
        // durable write's temp file beside them.
        var temp = Path.Combine(path.FullPath, $"{SessionLayout.LockFileName}.new-{Guid.NewGuid():N}");
        File.Delete(path.DataFile);
        File.Move(path.LockFile, temp);

        var followed = index.Follow();

        await Assert.That(followed[0].State).IsEqualTo(SessionIndexEntryState.RecordInFlight);
        await Assert.That(followed[0].IsRemovable).IsFalse();
        await Assert.That(followed[0].Problem).Contains(Path.GetFileName(temp));

        var kept = index.Sweep();

        await Assert.That(kept.Removed.Count).IsEqualTo(0);
        await Assert.That(kept.Kept.Count).IsEqualTo(1);
        await Assert.That(Directory.GetFiles(index.Root).Length).IsEqualTo(1);

        // The other arm. The rewrite never lands and the temp file goes; the
        // directory is then genuinely recordless and the entry is re-assertable,
        // so the sweep takes it.
        File.Delete(temp);

        var swept = index.Sweep();

        await Assert.That(swept.Removed.Count).IsEqualTo(1);
        await Assert.That(swept.Removed[0].State).IsEqualTo(SessionIndexEntryState.NotASession);
        await Assert.That(Directory.GetFiles(index.Root).Length).IsEqualTo(0);
    }

    [Test]
    public async Task AnEntryPointingAtAPersonalChromeProfileIsFollowedAndProducesNoAction()
    {
        using var scratch = ScratchDirectory.Create("index-chrome");
        var index = NewIndex(scratch);

        // A profile shaped like the real thing, including the file whose name is
        // one character from ours: Chromium's own `lockfile`. Keying on anything
        // fuzzier than an exact `browserai.json` would claim this directory.
        var profile = Path.Combine(scratch.Path, "User Data");
        _ = Directory.CreateDirectory(Path.Combine(profile, "Default"));
        await File.WriteAllTextAsync(Path.Combine(profile, "Local State"), """{"os_crypt":{"encrypted_key":"x"}}""");
        await File.WriteAllTextAsync(Path.Combine(profile, "lockfile"), "hostname-1234");
        await File.WriteAllTextAsync(Path.Combine(profile, "Default", "Preferences"), """{"profile":{"name":"Person 1"}}""");
        await File.WriteAllTextAsync(Path.Combine(profile, "Default", "Cookies"), "SQLite format 3\0");
        await File.WriteAllTextAsync(Path.Combine(profile, "Default", "History"), "SQLite format 3\0");

        var before = Manifest(profile);
        var path = SessionPath.For(profile);
        index.Record(path);

        // Followed, and found not to be ours. That is the entire interaction.
        var followed = index.Follow();
        await Assert.That(followed.Count).IsEqualTo(1);
        await Assert.That(followed[0].State).IsEqualTo(SessionIndexEntryState.NotASession);
        await Assert.That(followed[0].Record).IsNull();

        var sweep = index.Sweep();
        await Assert.That(sweep.Removed.Count).IsEqualTo(1);

        // NO ACTION. Not a lock taken, not a file written, not a file removed,
        // not a directory deleted -- the manifest is every file under the tree
        // with its length and its content hash, so an addition fails this as
        // loudly as a deletion would.
        await Assert.That(Manifest(profile)).IsEqualTo(before);
        await Assert.That(File.Exists(Path.Combine(profile, SessionLayout.LockFileName))).IsFalse();
        await Assert.That(Directory.Exists(profile)).IsTrue();
    }

    [Test]
    public async Task TwoProcessesWritingOneEntryConcurrentlyLeaveOneValidFile()
    {
        using var scratch = ScratchDirectory.Create("index-race");
        var root = Path.Combine(scratch.Path, "appdata");
        var index = new SessionIndex(new LocalAppDataPaths(root), NullLogger.Instance);

        var directory = Path.Combine(scratch.Path, "session");
        var path = SessionPath.For(directory);
        SessionLayout.Create(path);

        var lease = SessionLock.TryAcquire(path, Request("raced for"), NullLogger.Instance);
        lease.Acquired!.Dispose();

        var startName = $@"Local\BrowserAI-Test-Start-{Guid.NewGuid():N}";
        using var start = new EventWaitHandle(initialState: false, EventResetMode.ManualReset, startName, out var createdNew);
        await Assert.That(createdNew).IsTrue();

        var reports = Enumerable.Range(0, Writers)
            .Select(i => Path.Combine(scratch.Path, $"writer-{i.ToString(CultureInfo.InvariantCulture)}.json"))
            .ToList();

        using (var scope = new JobObjectScope())
        {
            foreach (var report in reports)
            {
                _ = scope.Launch(
                    ProbePath,
                    AppContext.BaseDirectory,
                    "session-index",
                    directory,
                    root,
                    startName,
                    report,
                    WritesEach.ToString(CultureInfo.InvariantCulture));
            }

            await WaitForAllAsync(reports.Select(report => $"{report}.ready"));

            // Everybody is parked on the event. This is the instant they all
            // start renaming over one another's file.
            _ = start.Set();

            var outcomes = await ProbeReport.ReadAllAsync(reports, Patience);

            foreach (var outcome in outcomes)
            {
                await Assert.That((int?)outcome["writes"]).IsEqualTo(WritesEach);
                await Assert.That((string?)outcome["indexRoot"]).IsEqualTo(index.Root);
            }
        }

        // One valid file, and only one. No lock was taken anywhere on this path.
        var entries = Directory.GetFiles(index.Root);
        await Assert.That(entries.Length).IsEqualTo(1);
        await Assert.That(Path.GetFileName(entries[0])).IsEqualTo(path.IndexKey);
        await Assert.That(Convert.ToHexString(await File.ReadAllBytesAsync(entries[0])))
            .IsEqualTo(Convert.ToHexString(Encoding.UTF8.GetBytes(path.FullPath)));

        // Valid means followable, not merely present.
        var followed = index.Follow();
        await Assert.That(followed.Count).IsEqualTo(1);
        await Assert.That(followed[0].State).IsEqualTo(SessionIndexEntryState.Session);

        // And no rename temp survived 2,000 renames over one name.
        await Assert.That(entries.Count(file => Path.GetFileName(file).Contains(".new-", StringComparison.Ordinal))).IsEqualTo(0);

        // Recording never throws, so a failure can only be found in the log.
        // Reading it is what stops "one valid file" being satisfied by every
        // writer having quietly given up.
        var log = ProbeProcess.ReadProcessLog(root);
        await Assert.That(log).DoesNotContain("Could not write the session index entry");
    }

    /// <summary>
    /// An entry whose record cannot be read is kept, because nothing else can
    /// ever restore it.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The corruption moved with the record (2026-08-26, previously a
    /// misspelled key in <c>browserai.json</c>).</b> The record is a database
    /// now, so what a damaged one looks like is a file whose header SQLite will
    /// not accept — and the entry state it produces is the same one, for the
    /// same reason: a session nobody can open is a session nobody can
    /// re-record.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnEntryWhoseRecordCannotBeReadIsKeptBecauseNothingElseCanRestoreIt()
    {
        using var scratch = ScratchDirectory.Create("index-corrupt");
        var (index, path) = NewIndex(scratch, "session");

        var lease = SessionLock.TryAcquire(path, Request("about to be corrupted"), NullLogger.Instance);
        index.Record(path);
        lease.Acquired!.Dispose();

        var original = await File.ReadAllBytesAsync(path.DataFile);

        // Not a database, and not an empty file either: an empty one would be
        // created afresh rather than refused, which is a different state.
        await File.WriteAllBytesAsync(path.DataFile, System.Text.Encoding.UTF8.GetBytes("this is not a database at all, it is a note"));

        // THIS IS THE MEASUREMENT THE KEEP RESTS ON. A session whose record
        // cannot be read is refused by TryAcquire, so there is no init and no
        // resume that would ever re-assert its index entry. Removing the entry
        // would make a directory that still exists permanently invisible to the
        // only inventory there is.
        var refused = SessionLock.TryAcquire(path, Request("trying to resume it"), NullLogger.Instance);
        refused.Acquired?.Dispose();
        await Assert.That(refused.Outcome).IsEqualTo(SessionLockOutcome.Unreadable);
        await Assert.That(Directory.GetFiles(index.Root).Length).IsEqualTo(1);

        var followed = index.Follow();
        await Assert.That(followed[0].State).IsEqualTo(SessionIndexEntryState.LockUnreadable);
        await Assert.That(followed[0].Record).IsNull();
        await Assert.That(followed[0].Problem).Contains(SessionLayout.DataFileName);

        var sweep = index.Sweep();

        await Assert.That(sweep.Removed.Count).IsEqualTo(0);
        await Assert.That(sweep.Kept.Count).IsEqualTo(1);
        await Assert.That(Directory.GetFiles(index.Root).Length).IsEqualTo(1);

        // And once the file is repaired, the entry is an ordinary session again
        // without anyone re-recording it.
        await File.WriteAllBytesAsync(path.DataFile, original);
        await Assert.That(index.Follow()[0].State).IsEqualTo(SessionIndexEntryState.Session);
    }

    [Test]
    public async Task AnEntryOnAVolumeThatIsNotMountedIsKeptRatherThanSwept()
    {
        using var scratch = ScratchDirectory.Create("index-volume");
        var index = NewIndex(scratch);

        var absent = FirstUnmountedDriveLetter();
        var path = SessionPath.For($@"{absent}:\browserai\sessions\research");
        index.Record(path);

        var followed = index.Follow();

        // Not "gone". Unknown. A session directory on a drive that is not
        // plugged in, or a share that is not reachable, has not been destroyed —
        // and dropping it from the only inventory there is would lose it for
        // good, because nothing on an absent volume can re-assert its entry.
        await Assert.That(followed.Count).IsEqualTo(1);
        await Assert.That(followed[0].State).IsEqualTo(SessionIndexEntryState.VolumeMissing);
        await Assert.That(followed[0].Problem).Contains($@"{absent}:\");

        var sweep = index.Sweep();

        await Assert.That(sweep.Removed.Count).IsEqualTo(0);
        await Assert.That(Directory.GetFiles(index.Root).Length).IsEqualTo(1);
    }

    [Test]
    public async Task AnEntryThatCannotBeFollowedAtAllIsRemoved()
    {
        using var scratch = ScratchDirectory.Create("index-unusable");
        var index = NewIndex(scratch);
        _ = Directory.CreateDirectory(index.Root);

        var real = SessionPath.For(Path.Combine(scratch.Path, "elsewhere"));

        // Four ways an entry can fail to be a pointer, planted by hand because
        // this build cannot write any of them.
        var planted = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Key("empty")] = string.Empty,
            [Key("relative")] = @"sessions\research",
            [Key("garbage")] = "this is not a path at all\u0000?",

            // A real, absolute, followable path -- under the wrong name. Left in
            // place it would be a second inventory line for a directory that
            // already has a correctly-named one, and no sweep would ever
            // converge.
            [Key("mismatched")] = real.FullPath,
        };

        foreach (var (name, content) in planted)
        {
            await File.WriteAllTextAsync(Path.Combine(index.Root, name), content);
        }

        var followed = index.Follow();
        await Assert.That(followed.Count).IsEqualTo(planted.Count);

        foreach (var entry in followed)
        {
            await Assert.That(entry.State).IsEqualTo(SessionIndexEntryState.Unusable);
            await Assert.That(entry.Problem).Contains("cannot be followed");
        }

        var sweep = index.Sweep();

        await Assert.That(sweep.Removed.Count).IsEqualTo(planted.Count);
        await Assert.That(Directory.GetFiles(index.Root).Length).IsEqualTo(0);
    }

    [Test]
    public async Task AnEntryPointingAtASpellingThisBuildWouldNotHaveWrittenIsUnusableRatherThanFollowed()
    {
        // ⚠️ THE READ PATH CHECKS AND NEVER RESOLVES, and this is what that buys.
        // An entry whose pointer is an aliased spelling is SELF-CONSISTENT --
        // its file name really is the hash of what it holds -- so the
        // name-is-the-hash test admits it, and following it produces a second
        // inventory line, a second identity and a second gate for one directory.
        // Resolving the alias here would cost one directory open per entry on
        // the whole machine, on every listing, every roll-up and every sweep.
        //
        // So the pointer is checked instead: a stored path that is not the
        // spelling this build writes was not written by this build, and the
        // index already knows what to do with one of those.
        using var scratch = ScratchDirectory.Create("index-non-canonical");
        var index = NewIndex(scratch);
        _ = Directory.CreateDirectory(index.Root);

        var real = Path.Combine(scratch.Path, "a-session");
        _ = Directory.CreateDirectory(real);

        var aliased = BrowserAI.Interop.VolumeIdentity.ExtendedLengthPrefix + real;

        await File.WriteAllTextAsync(Path.Combine(index.Root, SessionPath.For(aliased).IndexKey), aliased);

        var followed = index.Follow();

        await Assert.That(followed.Count).IsEqualTo(1);
        await Assert.That(followed[0].State).IsEqualTo(SessionIndexEntryState.Unusable);
        await Assert.That(followed[0].Problem!).Contains("not the spelling BrowserAI records");

        // Removing it is safe for the reason every removal here is safe: the
        // next init or resume on the real directory records it again, and this
        // time canonically.
        await Assert.That(index.Sweep().Removed.Count).IsEqualTo(1);

        // The positive control, without which the assertion above is satisfied
        // by an index that cannot follow anything: the same directory, spelled
        // the way this build spells it, is followed rather than refused.
        await File.WriteAllTextAsync(Path.Combine(index.Root, SessionPath.For(real).IndexKey), real);

        await Assert.That(index.Follow()[0].State).IsNotEqualTo(SessionIndexEntryState.Unusable);
    }

    [Test]
    public async Task ASweepClearsThisStoresOwnAbandonedRenameTempsAndNothingElse()
    {
        using var scratch = ScratchDirectory.Create("index-litter");
        var (index, path) = NewIndex(scratch, "session");

        var lease = SessionLock.TryAcquire(path, Request("littered"), NullLogger.Instance);
        index.Record(path);
        lease.Acquired!.Dispose();

        var abandoned = Path.Combine(index.Root, $"{path.IndexKey}.new-{Guid.NewGuid():N}");
        var live = Path.Combine(index.Root, $"{path.IndexKey}.new-{Guid.NewGuid():N}");
        var foreign = Path.Combine(index.Root, "readme.txt");

        foreach (var file in new[] { abandoned, live, foreign })
        {
            await File.WriteAllTextAsync(file, path.FullPath);
        }

        // Only a temp far too old to belong to a running writer is cleared. The
        // age bound is what stops a sweep deleting the file another process is
        // two microseconds from renaming into place.
        File.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddHours(-2));

        var sweep = index.Sweep();

        await Assert.That(sweep.LitterRemoved).IsEqualTo(1);
        await Assert.That(File.Exists(abandoned)).IsFalse();
        await Assert.That(File.Exists(live)).IsTrue();

        // A file this product did not write is never deleted by anything here,
        // however old it is and whatever it holds.
        File.SetLastWriteTimeUtc(foreign, DateTime.UtcNow.AddYears(-1));
        _ = index.Sweep();
        await Assert.That(File.Exists(foreign)).IsTrue();

        // And the entry itself survived all of it.
        await Assert.That(index.Follow().Single(entry => entry.Key == path.IndexKey).State)
            .IsEqualTo(SessionIndexEntryState.Session);
    }

    [Test]
    public async Task AFailureToRecordIsLoggedRatherThanThrownAndTheSessionIsUnaffected()
    {
        using var scratch = ScratchDirectory.Create("index-unwritable");
        using var provider = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider).SetMinimumLevel(LogLevel.Trace));

        var root = Path.Combine(scratch.Path, "appdata");
        _ = Directory.CreateDirectory(root);

        // An index root that is a file. Directory.CreateDirectory on it fails,
        // which is the cheapest deterministic way to make the store unwritable.
        await File.WriteAllTextAsync(Path.Combine(root, "index"), "not a directory");

        var index = new SessionIndex(new LocalAppDataPaths(root), factory.CreateLogger("BrowserAI.Tests"));
        var path = SessionPath.For(Path.Combine(scratch.Path, "session"));
        SessionLayout.Create(path);

        // Does not throw. An inventory line that could not be written must never
        // be what fails a session, because the next use re-asserts it.
        index.Record(path);

        await Assert.That(provider.Logged("Could not write the session index entry")).IsTrue();
        await Assert.That(provider.Records.Any(record => record.Level is LogLevel.Warning)).IsTrue();

        // Silence is the enemy; a lost line is not. The session itself is fine.
        var lease = SessionLock.TryAcquire(path, Request("uninventoried"), NullLogger.Instance);

        try
        {
            await Assert.That(lease.Taken).IsTrue();
        }
        finally
        {
            lease.Acquired?.Dispose();
        }

        // And Follow over an unusable root answers empty rather than throwing.
        await Assert.That(index.Follow().Count).IsEqualTo(0);
    }

    [Test]
    public async Task TheIndexTakesNoLockAndDeletesNoDirectory()
    {
        // Both halves are design decisions that a later edit could reverse with
        // no test failing anywhere else, so they are asserted on the source.
        // The lock half is the plan's own reason the store is cheap; the
        // directory half is what makes an entry a pointer rather than a warrant.
        var file = RepositoryLayout.ProductSourceFiles
            .Single(candidate => candidate.Name is "SessionIndex.cs");

        var code = await RepositoryLayout.ReadCodeAsync(file);

        string[] forbidden =
        [
            "MachineMutex",
            "new Mutex(",
            "Semaphore",
            "lock (",
            "Monitor.",
            "Interlocked",
            "Directory.Delete",
            "Directory.Move",
        ];

        await Assert.That(string.Join(", ", forbidden.Where(needle => code.Contains(needle, StringComparison.Ordinal))))
            .IsEmpty();
    }

    [Test]
    public async Task TheIndexRootComesFromTheSeamAndSitsBesideTheOtherRoots()
    {
        var paths = new LocalAppDataPaths(@"C:\somewhere\BrowserAI");

        await Assert.That(paths.IndexDirectory).IsEqualTo(@"C:\somewhere\BrowserAI\index");

        // A sibling of current\, never a child: an update replaces that folder.
        await Assert.That(new SessionIndex(paths, NullLogger.Instance).Root).IsEqualTo(paths.IndexDirectory);

        // And the production answer is the one the plan names, computed rather
        // than typed at a call site.
        var real = BrowserAiPaths.Real.IndexDirectory;
        await Assert.That(real).IsEqualTo(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            "BrowserAI",
            "index"));
    }

    /// <summary>
    /// Following one subtree returns element for element what following
    /// everything would have returned for it — <b>plus every entry that carries
    /// no session to compare</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This could not be planted red as a statement about the product, and
    /// that is a property of the fix rather than a gap in the effort — say so
    /// rather than implying otherwise.</b> <c>FollowUnder</c> did not exist
    /// before the change, so no run of any tree can have failed it. It is weaker
    /// than a red test: what it holds is that the two reads cannot drift apart
    /// later, which is the claim the fix rests on. The half that <i>was</i> red
    /// is <c>HouseRuleTests.NoIndexWalkFiltersBySubtreeAfterFollowingTheEntry</c>,
    /// over the two real offenders.
    /// </para>
    /// <para>
    /// ⚠️ ***Corrected 2026-08-24 (previously the whole of it built
    /// <c>expected</c> as <c>Follow().Where(entry =&gt; entry.Session is { }
    /// session &amp;&amp; …)</c> and asserted two).*** That predicate drops
    /// exactly the class where the equivalence <b>fails</b>, so the test named an
    /// equivalence and excluded its only counter-example — and
    /// <c>SessionIndex.FollowUnder</c>'s own remark cited it as asserting that
    /// equivalence directly. The truth is narrower and is now what is asserted:
    /// <c>FollowUnder(p)</c> is <c>Follow()</c> filtered by <c>IsUnder</c> <b>for
    /// the entries that resolved to a session</b>, and every entry that could not
    /// be resolved at all is returned whatever subtree it does or does not name.
    /// The third entry below is one of those — mis-hashed, pointing nowhere near
    /// <c>left</c> — and it comes back from a read scoped to <c>left</c>. This
    /// arm <b>was</b> watched red in the corrected direction: with it planted and
    /// the old expectation in place, <c>Expected to be equal to 2 but received
    /// 3</c>.
    /// </para>
    /// <para>
    /// <b>Harmless in the product today and asserted anyway.</b>
    /// <c>SessionManager.List</c> and <c>SessionManager.Beneath</c> both drop
    /// <c>Session is null</c> on the next line, so no caller sees the divergence;
    /// what a caller of the API sees is what this holds.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task FollowingOneSubtreeReturnsExactlyWhatFollowingEverythingWouldHaveReturnedForIt()
    {
        using var scratch = ScratchDirectory.Create("index-subtree");

        var left = Path.Combine(scratch.Path, "left");
        var right = Path.Combine(scratch.Path, "right");

        var index = NewIndex(scratch);

        foreach (var (root, name) in new[] { (left, "one"), (left, "two"), (right, "three"), (right, "four") })
        {
            var session = SessionPath.For(Path.Combine(root, name));
            SessionLayout.Create(session);

            var lease = SessionLock.TryAcquire(session, Request($"the session called {name}"), NullLogger.Instance);
            index.Record(session);
            lease.Acquired!.Dispose();
        }

        // ⚠️ THE DIVERGENT CLASS, PLANTED. An entry whose name is not the hash of
        // what it holds is refused above the subtree test, so it carries no
        // session to compare against the prefix — and it names a directory under
        // NEITHER root.
        var divergent = Key("mis-hashed and out of both subtrees");
        _ = Directory.CreateDirectory(index.Root);
        await File.WriteAllTextAsync(Path.Combine(index.Root, divergent), Path.Combine(scratch.Path, "elsewhere", "nowhere"));

        await Assert.That(index.Follow().Count).IsEqualTo(5);

        var prefix = Prefix(left);
        var scoped = index.FollowUnder(prefix);

        var expected = index.Follow()
            .Where(entry => entry.Session is not { } session
                || (session.Key + Path.DirectorySeparatorChar).StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        // Two under the prefix, and the one nothing could resolve.
        await Assert.That(scoped.Count).IsEqualTo(3);
        await Assert.That(scoped.Count(entry => entry.Session is null)).IsEqualTo(1);
        await Assert.That(scoped.Any(entry => string.Equals(entry.Key, divergent, StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(scoped.Select(entry => entry.Key).ToList()).IsEquivalentTo(expected.Select(entry => entry.Key).ToList());

        // Element for element, and the record with it: the claim is that the
        // entries are followed the same way and not merely that the same three
        // came back.
        foreach (var (one, other) in scoped.Zip(expected))
        {
            await Assert.That(one.Key).IsEqualTo(other.Key);
            await Assert.That(one.State).IsEqualTo(other.State);
            await Assert.That(one.Session?.FullPath).IsEqualTo(other.Session?.FullPath);
            await Assert.That(one.Record?.Purpose).IsEqualTo(other.Record?.Purpose);
        }
    }

    /// <summary>
    /// Following one subtree opens no record outside it.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Weaker than a red test, for the same reason as the test above</b> —
    /// it names an API that did not exist, so nothing can have watched it fail.
    /// What makes it worth having is the control: the denied session out of
    /// prefix is proved to be a record that <i>would</i> have failed to open, so
    /// the scoped read returning one clean entry is a statement about the open
    /// rather than about the filter.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task FollowingOneSubtreeOpensNoRecordOutsideIt()
    {
        using var scratch = ScratchDirectory.Create("index-subtree-denied");

        var mine = SessionPath.For(Path.Combine(scratch.Path, "mine", "session"));
        var stranger = SessionPath.For(Path.Combine(scratch.Path, "stranger", "session"));

        SessionLayout.Create(mine);
        SessionLayout.Create(stranger);

        var index = NewIndex(scratch);

        foreach (var session in new[] { mine, stranger })
        {
            var lease = SessionLock.TryAcquire(session, Request("a session somebody else's tree holds"), NullLogger.Instance);
            index.Record(session);
            lease.Acquired!.Dispose();
        }

        // ReadData on the directory, inherited by the browserai.json inside it:
        // the record cannot be opened at all, which is what a whole-machine walk
        // pays for and a scoped one must not.
        using (DirectoryDenial.Apply(
            stranger.FullPath,
            FileSystemRights.ReadData,
            InheritanceFlags.ObjectInherit,
            PropagationFlags.None))
        {
            // ⚠️ THE CONTROL, and without it this test passes on a denial that
            // never bit.
            var everything = index.Follow();

            await Assert.That(everything.Count).IsEqualTo(2);
            await Assert.That(everything.Count(entry => entry.State is SessionIndexEntryState.LockUnreadable)).IsEqualTo(1);

            var scoped = index.FollowUnder(Prefix(Path.Combine(scratch.Path, "mine")));

            await Assert.That(scoped.Count).IsEqualTo(1);
            await Assert.That(scoped[0].State).IsEqualTo(SessionIndexEntryState.Session);
            await Assert.That(scoped[0].Session!.FullPath).IsEqualTo(mine.FullPath);
        }
    }

    /// <summary>
    /// A subtree prefix spelled the one way <c>SessionManager.Subtree</c> spells
    /// it: upper-cased and separator-terminated.
    /// </summary>
    private static string Prefix(string root)
    {
        var key = Path.GetFullPath(root).ToUpperInvariant();

        return key.EndsWith(Path.DirectorySeparatorChar) ? key : key + Path.DirectorySeparatorChar;
    }

    private static SessionLockRequest Request(string purpose) =>
        new() { Browser = "chromium", Purpose = purpose };

    private static SessionIndex NewIndex(ScratchDirectory scratch) =>
        new(new LocalAppDataPaths(Path.Combine(scratch.Path, "appdata")), NullLogger.Instance);

    private static (SessionIndex Index, SessionPath Path) NewIndex(ScratchDirectory scratch, string name)
    {
        var path = SessionPath.For(Path.Combine(scratch.Path, name));
        SessionLayout.Create(path);

        return (NewIndex(scratch), path);
    }

    /// <summary>A key that is well-formed and is not any real directory's.</summary>
    private static string Key(string label) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(label)));

    /// <summary>
    /// Every file under a tree, with its length and the hash of its bytes.
    /// </summary>
    /// <remarks>
    /// Compared as one string so that an added file fails as loudly as a
    /// modified or a deleted one. "No action" is a claim about the whole tree,
    /// and checking three named files would pass a routine that wrote a fourth.
    /// </remarks>
    private static string Manifest(string root) =>
        string.Join(
            '\n',
            Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(file => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Path.GetRelativePath(root, file)} {new FileInfo(file).Length} {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))}")));

    /// <summary>The first drive letter this machine has no volume mounted on.</summary>
    private static char FirstUnmountedDriveLetter()
    {
        var mounted = DriveInfo.GetDrives()
            .Select(drive => char.ToUpperInvariant(drive.Name[0]))
            .ToHashSet();

        foreach (var letter in "ZYXWVUTSRQPONMLKJIHGFE")
        {
            if (!mounted.Contains(letter) && !Directory.Exists($@"{letter}:\"))
            {
                return letter;
            }
        }

        // Not a skip. A machine with twenty-two volumes mounted cannot answer
        // this question, and saying so beats reporting a pass that means
        // nothing.
        throw new InvalidOperationException("Every drive letter from E: to Z: is in use, so an unmounted volume cannot be named.");
    }

    private static async Task WaitForAllAsync(IEnumerable<string> paths)
    {
        var wanted = paths.ToList();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        while (clock.Elapsed < Patience && wanted.Exists(path => !File.Exists(path)))
        {
            await Task.Delay(10);
        }

        var missing = wanted.Where(path => !File.Exists(path)).ToList();

        if (missing.Count is not 0)
        {
            throw new TimeoutException($"{missing.Count} of {wanted.Count} writers never reported ready within {Patience}.");
        }
    }
}
