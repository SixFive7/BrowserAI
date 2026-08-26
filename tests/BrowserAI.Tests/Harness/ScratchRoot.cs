// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Runtime;
using BrowserAI.Sessions;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The suite's scratch root, reclaimed once per run before anything else
/// happens.
/// </summary>
/// <remarks>
/// <para>
/// Every run starts by reclaiming what a previous run may have leaked. This
/// suite drives real processes, machine-wide named objects and real
/// directories, so a run that is killed — a failed assertion taking the host
/// with it, a debugger detached — leaves state behind that the <i>next</i> run
/// meets as a failure. That failure reports the wrong cause: it names the
/// change under test while describing the previous run's crash, and the time
/// goes on the wrong bug.
/// </para>
/// <para>
/// The reclaim is idempotent and never fatal. What it cannot delete, it leaves;
/// a directory still held open belongs to something still running, and killing
/// that is a later step's job with a later step's evidence.
/// </para>
/// </remarks>
internal static class ScratchRoot
{
    private static readonly Lock Gate = new();
    private static bool _reclaimed;

    /// <summary>
    /// Everything the reclaim pass could not remove, in the order it met them.
    /// </summary>
    /// <remarks>
    /// <b>Exposed so the pass can be a test rather than only a side effect.</b>
    /// The suite's own specification says <i>"the pass is itself a test — it
    /// runs the same reclaim the product performs, so a defect in reclaim shows
    /// up as a suite that cannot start clean, which is a louder signal than a
    /// sweep that quietly finds nothing"</i>, and until 2026-08-17 the pass ran
    /// and nothing asserted that it had. A list that stays empty is the healthy
    /// state; a list that fills is the previous run's leak, named.
    /// </remarks>
    public static List<string> LastPassSurvivors { get; } = [];

    /// <summary>
    /// Every line the spawn-record half of the pass produced — terminated,
    /// skipped, or could not be terminated.
    /// </summary>
    /// <remarks>
    /// <b>Separate from <see cref="LastPassSurvivors"/> because most of it is
    /// the healthy state.</b> A machine that has never crashed a run produces a
    /// file of pids that all read <i>not that process any more</i>, and that is
    /// the pass confirming there is nothing to do rather than a finding. Only a
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
    /// <c>%LocalAppData%\BrowserAI-test-scratch</c> — the one place the suite
    /// writes outside the repository, created and swept on the same pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>It exists for exactly one thing and must not be used for anything
    /// else: an <b>app root</b> a published BrowserAI will accept.</b> Since
    /// 2026-08-20 the product refuses at startup when its app root is outside
    /// the current user's profile — see
    /// <see cref="BrowserAI.Hosting.InstallRootScope"/> — so a test that hands
    /// it <c>&lt;repo&gt;\.work\…</c> through
    /// <see cref="BrowserAiPaths.AppRootOverride"/> is handed a process that
    /// exits 1 before it serves anything.
    /// </para>
    /// <para>
    /// <b>A sibling of the product's own root rather than a child of it</b>, so
    /// the reclaim below can delete the whole thing without ever being one
    /// mistake away from a developer's real browsers, sessions and log. The
    /// repository's own rule — everything the suite writes goes in
    /// <c>.work\</c> — is deliberately broken here and nowhere else, because the
    /// property under test is <i>where the app root is</i> and no directory
    /// inside the repository can have it.
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
    /// — so the class of defect is still driven both ways on every run. This
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
            // that cleaned up rather than the run that leaked.
            LastPassReport.AddRange(SpawnRecord.Reclaim(SpawnRecord.Path));
            LastPassSurvivors.AddRange(LastPassReport
                .Where(line => line.StartsWith("could not terminate ", StringComparison.Ordinal)));

            Reclaim(path);

            // The profile-anchored root gets the identical pass, because it
            // holds whole app roots -- browsers directory, index and live
            // markers -- and a leaked one is exactly the leftover this class
            // exists to stop the next run meeting as a failure.
            Reclaim(profile);

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
    /// else. A test that pointed the index at the real root — by taking
    /// <see cref="LocalAppDataPaths"/>'s default rather than a scratch root —
    /// would put this run's throwaway directories into a developer's own
    /// <c>browserai_list</c>, and they would stay there.
    /// </para>
    /// <para>
    /// <b>Only entries pointing inside the scratch root are removed.</b> A
    /// developer's real sessions are never touched, and the reclaim cleans a
    /// leak from any earlier run rather than only from this one.
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
    /// suite deliberately kills processes holding that object — it is how race
    /// R3 is provoked at all — so a run cut short between the kill and the
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

    private static void Reclaim(string path)
    {
        foreach (var directory in Directory.EnumerateDirectories(path))
        {
            try
            {
                // TreeDelete rather than Directory.Delete(recursive: true),
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
