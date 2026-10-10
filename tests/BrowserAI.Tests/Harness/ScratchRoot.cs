// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using BrowserAI.Hosting;
using BrowserAI.Logging;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The suite's scratch root, reclaimed once per run before anything else
/// happens.
/// </summary>
/// <remarks>
/// <para>
/// Every run starts by reclaiming what a previous run may have leaked. This
/// suite drives real processes, machine-wide named objects and real
/// directories, so a run that is killed -- a failed assertion taking the host
/// with it, a debugger detached -- leaves state behind that the <i>next</i> run
/// meets as a failure. That failure reports the wrong cause: it names the
/// change under test while describing the previous run's crash, and the time
/// goes on the wrong bug.
/// </para>
/// <para>
/// The reclaim is idempotent and never fatal. What it cannot delete, it leaves;
/// a directory still held open belongs to something still running, and killing
/// that is a later step's job with a later step's evidence.
/// </para>
/// <para>
/// ⚠️ <b>The shared root's folders have owners since 2026-10-10, 5 a, the
/// maintainer's words verbatim: "5 a".</b> <see cref="ProfileScratch"/> is one
/// folder for every checkout of this repository, and until that day the pass
/// deleted every folder in it with no check at all, so a run in one worktree
/// deleted the live app roots of a gate in another: proven on 2026-10-08, when an
/// install's folder went from under the gate that made it. Each folder there now
/// has a record beside it naming the process that made it, by pid and creation
/// time as <see cref="InstallerLock"/> names a holder, and the pass takes only the
/// folders whose owner is gone, naming each in the machine's process log with the
/// moment and itself. The repository's own scratch is one per checkout and keeps
/// the old pass. <c>ScratchReclaimTests</c> drives it from a second test host.
/// </para>
/// </remarks>
internal static class ScratchRoot
{
    /// <summary>
    /// What the file beside a folder of <see cref="ProfileScratch"/> that names its
    /// owner is called after the folder's own name.
    /// </summary>
    public const string OwnerSuffix = ".owner";

    /// <summary>
    /// The variable that makes a child test host run the profile half of the reclaim
    /// and write what it did to the file the variable names.
    /// </summary>
    public const string ReclaimProbeVariable = "BROWSERAI_SCRATCH_RECLAIM_PROBE";

    /// <summary>The category the reclaim's own records carry in the process log.</summary>
    private const string AnnouncementCategory = "BrowserAI.Tests.ScratchReclaim";

    private static readonly Lock Gate = new();
    private static bool _reclaimed;

    /// <summary>
    /// How old a folder of the shared root with no owner record has to be before the
    /// pass takes it.
    /// </summary>
    /// <remarks>
    /// <b>A day, because a folder with no record is either a leftover from before
    /// the records or one a run of an older harness is using</b>, and the second
    /// kind is the live folder this whole check exists to keep. No test host has
    /// run for a day, so a folder made more than a day ago is nobody's. An older
    /// harness writes no record, so its runs cannot be told any other way.
    /// </remarks>
    private static readonly TimeSpan UnownedAge = TimeSpan.FromDays(1);

    /// <summary>
    /// How long a record that names nobody is given to be one being written, which
    /// is <see cref="InstallerLock"/>'s grace for the same moment.
    /// </summary>
    private static readonly TimeSpan UnreadableOwnerGrace = TimeSpan.FromSeconds(10);

    /// <summary>This process, as an owner record names it.</summary>
    private static readonly Lazy<InstallerLockHolder> Self = new(() =>
        new InstallerLockHolder(Environment.ProcessId, ProcessIdentity.CreationTimeOf(Environment.ProcessId)));

    /// <summary>What an owner record says about the folder beside it.</summary>
    private enum Ownership
    {
        /// <summary>There is no record, or one that names nobody and is past its grace.</summary>
        None,

        /// <summary>A record that names nobody yet, inside its grace: its owner is writing it.</summary>
        Writing,

        /// <summary>The owner it names is running.</summary>
        Alive,

        /// <summary>The owner it names is gone.</summary>
        Gone,
    }

    /// <summary>
    /// Everything the reclaim pass could not remove, in the order it met them.
    /// </summary>
    /// <remarks>
    /// <b>Exposed so the pass can be a test and not only a side effect.</b>
    /// The suite's own specification says <i>"the pass is itself a test -- it
    /// runs the same reclaim the product performs, so a defect in reclaim shows
    /// up as a suite that cannot start clean, which is a louder signal than a
    /// sweep that quietly finds nothing"</i>, and until 2026-08-17 the pass ran
    /// and nothing asserted that it had. A list that stays empty is the healthy
    /// state; a list that fills is the previous run's leak, named.
    /// </remarks>
    public static List<string> LastPassSurvivors { get; } = [];

    /// <summary>
    /// Every line the spawn-record half of the pass produced -- terminated,
    /// skipped, or could not be terminated.
    /// </summary>
    /// <remarks>
    /// <b>Separate from <see cref="LastPassSurvivors"/> because most of it is
    /// the healthy state.</b> A machine that has never crashed a run produces a
    /// file of pids that all read <i>not that process any more</i>, and that is
    /// the pass confirming there is nothing to do and not a finding. Only a
    /// process it could not end is a survivor.
    /// </remarks>
    public static List<string> LastPassReport { get; } = [];

    /// <summary>Whether the reclaim pass has run in this process.</summary>
    public static bool HasReclaimed
    {
        get
        {
            lock (Gate)
            {
                return _reclaimed;
            }
        }
    }

    /// <summary>
    /// <c>&lt;repo&gt;\.work\test-scratch</c>, created and swept on first use.
    /// </summary>
    public static string Path
    {
        get
        {
            EnsureReclaimed();
            return RepositoryScratch;
        }
    }

    /// <summary>
    /// <c>%LocalAppData%\BrowserAI-test-scratch</c> -- the one place the suite
    /// writes outside the repository, created and swept on the same pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>It exists for exactly one thing and must not be used for anything
    /// else: an <b>app root</b> a published BrowserAI will accept.</b> Since
    /// 2026-08-20 the product refuses at startup when its app root is outside
    /// the current user's profile -- see
    /// <see cref="BrowserAI.Hosting.InstallRootScope"/> -- so a test that hands
    /// it <c>&lt;repo&gt;\.work\...</c> through
    /// <see cref="BrowserAiPaths.DataRootArgument"/> is handed a process that
    /// exits 1 before it serves anything.
    /// </para>
    /// <para>
    /// <b>A sibling of the product's own root and not a child of it</b>, so
    /// the reclaim below can delete the whole thing without ever being one
    /// mistake away from a developer's real browsers, sessions and log. The
    /// repository's own rule -- everything the suite writes goes in
    /// <c>.work\</c> -- is deliberately broken here, because the property under
    /// test is <i>where the app root is</i> and no directory inside the
    /// repository can have it.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-29 (previously "is deliberately broken here and
    /// nowhere else").</b> There is a second place since the reclaim began
    /// announcing what it terminated: <see cref="SpawnRecord"/> writes that to
    /// the machine's process log under <c>%LocalAppData%\BrowserAI\logs</c>. Two
    /// and not one, and the count is the whole of the change -- the reason
    /// stands, and so does the rule that a third needs the same argument. The
    /// suite's own published slices have always written there, which is why
    /// <see cref="ProcessLogRecords"/> exists to read them
    /// back, so what moved is that the <i>harness</i> writes there too.
    /// </para>
    /// </remarks>
    public static string ProfileScratch
    {
        get
        {
            EnsureReclaimed();
            return ProfileAnchoredScratch;
        }
    }

    /// <summary>
    /// <see cref="Path"/> as it is composed, <b>without</b> the reclaim pass.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>For a reader that has to know where the scratch root is and must not
    /// sweep it -- 2026-09-24.</b> <see cref="WindowWatch"/> needs the root at
    /// session start in every process, and a child test host started by an arm is
    /// such a process: taking <see cref="Path"/> there would run the reclaim in
    /// the child and delete the parent's live scratch directories out from under
    /// the parent's own arms.
    /// </remarks>
    public static string PathAsComposed => RepositoryScratch;

    /// <summary>
    /// <see cref="ProfileScratch"/> as it is composed, <b>without</b> the reclaim
    /// pass, for <see cref="PathAsComposed"/>'s reason.
    /// </summary>
    public static string ProfileScratchAsComposed => ProfileAnchoredScratch;

    private static string RepositoryScratch =>
        Canonical(System.IO.Path.Combine(RepositoryLayout.Root.FullName, ".work", "test-scratch"));

    private static string ProfileAnchoredScratch =>
        Canonical(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            "BrowserAI-test-scratch"));

    /// <summary>
    /// The filesystem's own spelling of a scratch root, so that every path this
    /// suite composes is anchored on the spelling the product answers with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Added 2026-08-26, with the one path function.</b> Before it, a
    /// session directory was recorded and answered with whatever spelling the
    /// caller used; now it is recorded and answered with the one
    /// <c>GetFinalPathNameByHandleW</c> reports, which is the drive letter
    /// <b>upper-case, always</b> and every component as it is stored. A scratch
    /// root carrying the invoking shell's own casing would therefore make every
    /// ordinal comparison in this suite a property of the shell: green from
    /// PowerShell, red from Git Bash, with no change to the product at all.
    /// </para>
    /// <para>
    /// <b>It does not weaken <see cref="DriveLetterCase"/> and does not overlap
    /// it.</b> That type re-spells a path deliberately, at the sites whose whole
    /// subject is the spelling, including a spelling no Windows API ever returns
    /// -- so the class of defect is still driven both ways on every run. This
    /// removes an <i>accidental</i> dependence on the shell from every other
    /// site, which is the opposite of removing coverage.
    /// </para>
    /// <para>
    /// <b>Best effort, and a failure is not fatal.</b> Before the directory
    /// exists there is nothing to ask the filesystem about, and a root this
    /// process cannot open is a problem the run will meet again immediately with
    /// a better message than this could give.
    /// </para>
    /// </remarks>
    /// <param name="path">The composed root.</param>
    /// <returns>The filesystem's spelling of it, or the composed one.</returns>
    private static string Canonical(string path) =>
        BrowserAI.Interop.VolumeIdentity.DosSpellingOf(path, CanonicalPath.AncestorWalkLimit).Spelling ?? path;

    private static void EnsureReclaimed()
    {
        lock (Gate)
        {
            if (_reclaimed)
            {
                return;
            }

            var path = RepositoryScratch;
            var profile = ProfileAnchoredScratch;

            _ = Directory.CreateDirectory(path);
            _ = Directory.CreateDirectory(profile);

            // FIRST, and before the tree: a process the previous run
            // left running is what holds the files the delete below
            // cannot take, so reclaiming in the other order reports a
            // locked file where the cause is a live process.
            //
            // Only what it COULD NOT terminate joins the survivors. A
            // leftover this pass ended is the pass working, and putting
            // it in a list something asserts is empty would fail the run
            // that cleaned up and not the run that leaked.
            LastPassReport.AddRange(SpawnRecord.Reclaim(SpawnRecord.Path));
            LastPassSurvivors.AddRange(LastPassReport
                .Where(line => line.StartsWith("could not terminate ", StringComparison.Ordinal)));

            Reclaim(path);

            // The profile-anchored root gets a pass too, because it holds whole
            // app roots -- browsers directory, index and live markers -- and a
            // leaked one is exactly the leftover this class exists to stop the
            // next run meeting as a failure.
            //
            // ⚠️ NOT THE IDENTICAL PASS SINCE 2026-10-10, 5 a: every checkout's
            // runs share this root, so it takes only the folders whose owner is
            // gone and names each one it deletes. See ReclaimOwned.
            LastPassReport.AddRange(ReclaimOwned(profile));

            ReclaimStrayIndexEntries(path);
            ReclaimStrayIndexEntries(profile);
            ReclaimTheSweepMutex();
            _reclaimed = true;
        }
    }

    /// <summary>
    /// Removes entries from the <b>real</b> session index that point into the
    /// scratch tree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The index root is machine-wide state, and it is the one piece of it this
    /// suite creates that a directory sweep cannot reach: an entry lives under
    /// <c>%LocalAppData%\BrowserAI\index\</c> and names a directory somewhere
    /// else. A test that pointed the index at the real root -- by taking
    /// <see cref="LocalAppDataPaths"/>'s default and not a scratch root --
    /// would put this run's throwaway directories into a developer's own
    /// <c>browserai_list</c>, and they would stay there.
    /// </para>
    /// <para>
    /// <b>Only entries pointing inside the scratch root are removed.</b> A
    /// developer's real sessions are never touched, and the reclaim cleans a
    /// leak from any earlier run and not only from this one.
    /// </para>
    /// </remarks>
    private static void ReclaimStrayIndexEntries(string scratchRoot)
    {
        var index = BrowserAiPaths.Real.IndexDirectory;

        if (!Directory.Exists(index))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFiles(index))
        {
            try
            {
                if (File.ReadAllText(entry).Trim().StartsWith(scratchRoot, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(entry);
                }
            }
#pragma warning disable CA1031 // See the type's remarks: reclaim is never fatal.
            catch (Exception)
#pragma warning restore CA1031
            {
                // An entry that cannot be read cannot be shown to be ours, and
                // an index entry is never deleted on a guess.
            }
        }
    }

    /// <summary>
    /// Consumes an abandonment left on <c>Global\BrowserAI-Sweep</c> by a
    /// previous run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A rig that inherits an abandoned sweep mutex tests nothing.</b> This
    /// suite deliberately kills processes holding that object -- it is how race
    /// R3 is provoked at all -- so a run cut short between the kill and the
    /// acquire leaves the abandonment pending. The next run's <i>first</i>
    /// acquire then reports <c>AcquiredAbandoned</c> whatever it was testing,
    /// and every arm that asserts an ordinary acquisition fails while naming the
    /// wrong cause.
    /// </para>
    /// <para>
    /// <b>An abandonment is consumed by acquiring and releasing once</b>, which
    /// is exactly what this does. It cannot disturb a live sweeper: a zero
    /// timeout means a process that really holds the object keeps it and this
    /// returns immediately.
    /// </para>
    /// </remarks>
    private static void ReclaimTheSweepMutex()
    {
        try
        {
            using var mutex = MachineMutex.Create(LockScopes.Sweep);

            if (mutex.Acquire(LockScopes.NeverWaits) is not MutexAcquisition.NotAcquired)
            {
                mutex.Release();
            }
        }
#pragma warning disable CA1031 // See the type's remarks: reclaim is never fatal.
        catch (Exception)
#pragma warning restore CA1031
        {
            // No machine-wide objects at all is a condition the sweep itself
            // survives; a rig that refused to start over it would be worse.
        }
    }

    /// <summary>The line an owner record holds: who made the folder beside it.</summary>
    /// <param name="owner">The process that made it.</param>
    /// <returns>The line.</returns>
    public static string OwnerRecord(InstallerLockHolder owner) =>
        $"holder=suite {InstallerLock.TokenOf(owner)} at={DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture)}\n";

    /// <summary>
    /// Runs the profile half of the reclaim now, in this process, whether or not the
    /// once-per-run pass has run here, and answers with what it did.
    /// </summary>
    /// <remarks>
    /// For the second test host of <c>ScratchReclaimTests</c>, which has to run the
    /// pass every other run's first use of <see cref="ProfileScratch"/> runs, over the
    /// shared root and nothing else: the full pass would also sweep the repository
    /// scratch its parent is using.
    /// </remarks>
    /// <returns>One line per folder it acted on.</returns>
    public static List<string> ReclaimProfileScratchNow() => ReclaimOwned(ProfileAnchoredScratch);

    /// <summary>
    /// Creates a folder under <see cref="ProfileScratch"/>, its owner record first.
    /// </summary>
    /// <remarks>
    /// <b>The record is written before the folder exists</b>, so no pass anywhere can
    /// meet the folder without one: a folder with no record is a leftover from before
    /// the records, or a run of an older harness, and is judged by its age.
    /// </remarks>
    /// <param name="name">The folder's name, unique to the caller.</param>
    /// <returns>The folder's absolute path.</returns>
    public static string CreateUnderProfile(string name)
    {
        var folder = System.IO.Path.Combine(ProfileScratch, name);

        using (var record = new FileStream(folder + OwnerSuffix, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete))
        {
            record.Write(Encoding.UTF8.GetBytes(OwnerRecord(Self.Value)));
        }

        _ = Directory.CreateDirectory(folder);

        return folder;
    }

    /// <summary>
    /// Removes a folder of <see cref="ProfileScratch"/> that its owner is done with,
    /// and its record once nothing of the folder is left.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <returns>Every node that could not be deleted, one line each.</returns>
    public static IReadOnlyList<string> RemoveOwned(string folder)
    {
        var survivors = new List<string>();

        TreeDelete.Remove(folder, survivors);

        // A folder that would not go keeps its record, so it stays this run's until
        // this run is gone, and the next pass after that takes it.
        if (survivors.Count is 0)
        {
            TryDelete(folder + OwnerSuffix);
        }

        return survivors;
    }

    /// <summary>
    /// The owner-checked pass over the shared root: every folder whose owner is gone,
    /// or which has no owner and is older than <see cref="UnownedAge"/>, is deleted and
    /// named in the process log with the moment and this process.
    /// </summary>
    /// <param name="root">The shared root.</param>
    /// <returns>One line per folder it deleted, or could delete only part of.</returns>
    private static List<string> ReclaimOwned(string root)
    {
        var acted = new List<string>();

        if (!Directory.Exists(root))
        {
            return acted;
        }

        var announced = new List<(string Folder, string At, string Reason, int Left)>();

        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            var (ownership, owner) = OwnershipOf(folder + OwnerSuffix);
            string reason;

            switch (ownership)
            {
                case Ownership.Gone:
                    reason = $"its owner {owner} is no longer running";
                    break;

                case Ownership.None when TryCreationTime(folder) is { } made && DateTime.UtcNow - made > UnownedAge:
                    reason = $"it has no owner record and was made at {Utc(made)}, more than a day ago";
                    break;

                default:
                    // Alive, being written, or with no record and young enough to be a
                    // live run of an older harness: left alone.
                    continue;
            }

            var survivors = new List<string>();

            try
            {
                TreeDelete.Remove(folder, survivors);
            }
#pragma warning disable CA1031 // See the type's remarks: reclaim is never fatal.
            catch (Exception)
#pragma warning restore CA1031
            {
                survivors.Add(folder);
            }

            var at = Utc(DateTime.UtcNow);

            if (survivors.Count is 0)
            {
                TryDelete(folder + OwnerSuffix);
                acted.Add($"deleted {folder} at {at} by {SelfText()}: {reason}");
            }
            else
            {
                LastPassSurvivors.AddRange(survivors);
                acted.Add($"deleted part of {folder} at {at} by {SelfText()}: {reason}; {survivors.Count.ToString(CultureInfo.InvariantCulture)} node(s) would not go");
            }

            announced.Add((folder, at, reason, survivors.Count));
        }

        // A record whose folder is gone, left by a run that removed its folder and
        // was ended before it removed the record, goes with its owner.
        foreach (var record in Directory.EnumerateFiles(root, "*" + OwnerSuffix))
        {
            if (!Directory.Exists(record[..^OwnerSuffix.Length]) && OwnershipOf(record).Ownership is Ownership.Gone or Ownership.None)
            {
                TryDelete(record);
            }
        }

        Announce(announced);

        return acted;
    }

    /// <summary>What the record at a path says about its folder, and the owner it names.</summary>
    /// <param name="record">The owner record's path.</param>
    /// <returns>The answer, and the owner's token when the record names one.</returns>
    private static (Ownership Ownership, string? Owner) OwnershipOf(string record)
    {
        string text;
        DateTime written;

        try
        {
            using var stream = new FileStream(record, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            text = reader.ReadToEnd();
            written = File.GetLastWriteTimeUtc(record);
        }
        catch (Exception failure) when (failure is FileNotFoundException or DirectoryNotFoundException)
        {
            return (Ownership.None, null);
        }
#pragma warning disable CA1031 // See the type's remarks: reclaim is never fatal.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Open for writing by its owner this instant, or unreadable to this
            // process: either way, nobody may be told the folder is free.
            return (Ownership.Writing, null);
        }

        if (InstallerLock.Parse(text) is not { } holder)
        {
            return DateTime.UtcNow - written < UnreadableOwnerGrace ? (Ownership.Writing, null) : (Ownership.None, null);
        }

        return (InstallerLock.IsAlive(holder) ? Ownership.Alive : Ownership.Gone, InstallerLock.TokenOf(holder));
    }

    /// <summary>
    /// Writes what the owner-checked pass deleted to the machine's process log, and
    /// nothing when it deleted nothing.
    /// </summary>
    /// <remarks>
    /// <b>The process log, for <see cref="SpawnRecord"/>'s reasons</b>: it is the
    /// machine-wide record of what processes on this box did, it is outside the
    /// root the pass deletes from, and the one question a vanished folder raises is
    /// which process took it and when. Never fatal: a pass that could not describe
    /// itself has still reclaimed.
    /// </remarks>
    /// <param name="deleted">Each folder, the moment, and why.</param>
    private static void Announce(List<(string Folder, string At, string Reason, int Left)> deleted)
    {
        if (deleted.Count is 0)
        {
            return;
        }

        try
        {
            using var log = ProcessLog.Create(BrowserAiPaths.Real, LogLevel.Information);
            var logger = log.Factory.CreateLogger(AnnouncementCategory);
            var host = SelfText();

            // A folder that went in part is said to have, as the pass says it for itself:
            // round 2 of the texts review, 2026-10-10, #230 (previously every folder the
            // pass acted on was announced as deleted).
            foreach (var (folder, at, reason, left) in deleted)
            {
                if (left is 0)
                {
                    ScratchReclaimAnnouncement.Deleted(logger, folder, at, host, reason);
                }
                else
                {
                    ScratchReclaimAnnouncement.DeletedPart(logger, folder, at, host, reason, left);
                }
            }
        }
#pragma warning disable CA1031 // An announcement never becomes the outage.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>This process as <c>pid@createdFileTime</c>, the spelling the process log uses.</summary>
    /// <returns>The identity.</returns>
    private static string SelfText() =>
        string.Create(CultureInfo.InvariantCulture, $"{Self.Value.ProcessId}@{Self.Value.CreatedFileTime}");

    /// <summary>A moment in UTC, to the millisecond.</summary>
    /// <param name="moment">The moment, in UTC.</param>
    /// <returns>Its text.</returns>
    private static string Utc(DateTime moment) =>
        moment.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    /// <summary>When a folder was made, or nothing when it cannot be read.</summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The moment, in UTC.</returns>
    private static DateTime? TryCreationTime(string folder)
    {
        try
        {
            return Directory.GetCreationTimeUtc(folder);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Deletes a file, and says nothing when it cannot.</summary>
    /// <param name="path">The file.</param>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void Reclaim(string path)
    {
        foreach (var directory in Directory.EnumerateDirectories(path))
        {
            try
            {
                // TreeDelete and not Directory.Delete(recursive: true),
                // which is what this did until 2026-08-17 and is the one
                // primitive §E says never to use. The suite's own reclaim spec
                // names it: "the scratch root is deleted with the routine that
                // survives a locked file, because the common leftover is a
                // session directory a browser has not finished letting go of".
                //
                // The difference is the report, not the outcome: the framework
                // primitive names ONE surviving node where the per-node walk
                // names all of them, and here the survivors are the whole
                // signal -- they are the previous run's leak, and a reclaim
                // that says "one thing was locked" when eleven were is a
                // reclaim nobody can act on. Measured 2026-08-16; kb row 86.
                TreeDelete.Remove(directory, LastPassSurvivors);
            }
#pragma warning disable CA1031 // See the type's remarks: reclaim is never fatal.
            catch (Exception)
#pragma warning restore CA1031
            {
                // Held by something still alive. Left alone deliberately.
                LastPassSurvivors.Add(directory);
            }
        }
    }
}

/// <summary>The one record the shared root's reclaim writes about itself.</summary>
/// <remarks>
/// Source-generated so the message is a single literal, at
/// <see cref="LogLevel.Information"/>: a folder whose owner is gone is a killed
/// run's leftover, and taking it is the pass working.
/// </remarks>
internal static partial class ScratchReclaimAnnouncement
{
    /// <summary>Records one folder the pass deleted.</summary>
    /// <param name="logger">The process log's logger.</param>
    /// <param name="folder">The folder.</param>
    /// <param name="at">The moment, in UTC.</param>
    /// <param name="host">The process that deleted it, as <c>pid@createdFileTime</c>.</param>
    /// <param name="reason">Why it was taken.</param>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "The test harness's scratch reclaim deleted {Folder} at {At}, from {Host}, because {Reason}.")]
    public static partial void Deleted(ILogger logger, string folder, string at, string host, string reason);

    /// <summary>Records one folder the pass could delete only part of.</summary>
    /// <remarks>Added 2026-10-10, round 2 of the texts review, #230.</remarks>
    /// <param name="logger">The process log's logger.</param>
    /// <param name="folder">The folder.</param>
    /// <param name="at">The moment, in UTC.</param>
    /// <param name="host">The process that deleted it, as <c>pid@createdFileTime</c>.</param>
    /// <param name="reason">Why it was taken.</param>
    /// <param name="left">How many nodes would not go.</param>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "The test harness's scratch reclaim deleted part of {Folder} at {At}, from {Host}, because {Reason}; {Left} node(s) would not go, and the folder keeps its owner record until a later pass takes the rest.")]
    public static partial void DeletedPart(ILogger logger, string folder, string at, string host, string reason, int left);
}
