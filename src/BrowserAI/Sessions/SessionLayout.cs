// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Sessions;

/// <summary>
/// What a session directory contains. One directory is one session, and it is
/// simultaneously the name, the handle and the lock.
/// </summary>
/// <remarks>
/// <para>
/// Everything a session accumulates is a subfolder, so the two files at the root
/// are the ones that describe it — <c>browserai.lock</c> and
/// <c>browserai.data</c> — and everything a tool writes goes into
/// <c>output\</c>.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-26 (previously "<c>browserai.lock</c>,
/// <c>browserai.data</c> and the artifact index <c>session.json</c> — and
/// artifacts get a typed home instead of scattering among Chromium's
/// internals").</b> <c>session.json</c> is gone and so are the typed homes:
/// <c>output\</c> is flat, holding what the child wrote under the name the
/// child chose, with upstream's own subdirectories (<c>traces\</c>,
/// <c>session-&lt;stamp&gt;\</c>) inside it because they are upstream's to make.
/// <i>The 2026-08-16 correction this replaces read: "<c>browserai.json</c> is
/// the only file at the root and everything else is a subfolder".</i> What
/// survives from both is the claim they were each making — <b>no artifact is
/// ever at the session root</b>, so the files that <i>are</i> there describe the
/// session rather than being things it produced. The generated Playwright config
/// stays forbidden here for the same reason: it is a per-run artifact and lives
/// in the run's instance directory.
/// </para>
/// <para>
/// ⚠️ <b>The session's own log file is gone (2026-08-26, previously
/// <c>&lt;session-dir&gt;\browserai.log</c>).</b> Everything it carried is on
/// stderr, which <c>ProcessLog.OpenSessionLog</c> already wrote to at every
/// level, and everything about the session's own calls is in
/// <c>browserai.data</c> — including, since the same day, the refusals that used
/// to reach only the file. What the layout still provides is the half that is
/// real from the moment a directory is claimed: every log record written while a
/// lock is held carries the session, through <see cref="SessionLock"/>'s logging
/// scope.
/// </para>
/// </remarks>
internal static class SessionLayout
{
    /// <summary>
    /// Ours. The guard: the file whose open handle proves who owns the
    /// directory, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Renamed 2026-08-26 (previously <c>browserai.json</c>, and
    /// <c>lock.json</c> before that).</b> The record and the guard were one file
    /// for as long as the record was JSON, which is what made an append a
    /// whole-file durable rewrite and a rename — 3.94 ms at 1 KB, 13.62 ms at
    /// 400 KB, with the name unbound for every one of those windows. They are
    /// two files now: this one says <i>who owns this directory</i> and is
    /// written once, and <see cref="DataFileName"/> says <i>what happened
    /// here</i>. The name is <see cref="Storage.LockFile.FileName"/> rather
    /// than a second literal, because two spellings of one file name is how a
    /// prober and a holder come to look at different files.
    /// </para>
    /// <para>
    /// <b>There is no compatibility read and no migration.</b> A directory
    /// holding the old <c>browserai.json</c> is refused with the format as the
    /// reason — see <see cref="OldFormatRefusal"/>.
    /// </para>
    /// </remarks>
    public const string LockFileName = Storage.LockFile.FileName;

    /// <summary>Ours. Everything the session has said and done.</summary>
    public const string DataFileName = Storage.SessionStore.DataFileName;

    /// <summary>
    /// The record this build does not read, named so that meeting one is an
    /// answer rather than a directory that mysteriously is not a session.
    /// </summary>
    /// <remarks>
    /// <b>It is a constant because three callers have to recognise it</b> —
    /// acquisition, every read, and <c>browserai_destroy</c> — and a directory
    /// that one of them recognised and another did not would be taken by the
    /// one that did.
    /// </remarks>
    public const string LegacyRecordFileName = "browserai.json";

    /// <summary>
    /// What a guard being written durably is called before it is renamed over
    /// <see cref="LockFileName"/> — and therefore what its presence beside an
    /// absent <c>browserai.lock</c> means.
    /// </summary>
    /// <remarks>
    /// <b>Written once because two components read it for opposite reasons.</b>
    /// <c>LockFile.TakeAndWrite</c> composes the name; <c>SessionIndex</c>
    /// matches it to tell <i>this directory is not a session</i> from <i>an
    /// acquisition is in flight right now</i>, which is the difference between
    /// dropping an entry and keeping it.
    /// </remarks>
    public const string NewLockFilePattern = Storage.LockFile.TemporaryFilePattern;

    /// <summary>
    /// Why a directory holding the old record is not a session this build can
    /// open, or <see langword="null"/> when it holds no such thing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A version refusal, not a damage report, and the difference is what
    /// the caller does next.</b> The file is intact and was written by a
    /// BrowserAI; what this build cannot do is read it. So the sentence names
    /// the format, says there is no converter, and names the one recovery there
    /// is — which is not <c>browserai_destroy</c>, because that tool refuses a
    /// directory it cannot recognise as a session and would leave the caller
    /// with a refusal about a refusal.
    /// </para>
    /// <para>
    /// <b>There is no converter and there will not be one.</b> Nothing has ever
    /// been distributed: the annotated <c>v1.0.0</c> tag exists and no build has
    /// been handed to anybody, so the population that would need migrating is
    /// this machine's own scratch directories.
    /// </para>
    /// </remarks>
    /// <param name="location">The canonicalised session directory.</param>
    /// <returns>The refusal, or <see langword="null"/>.</returns>
    public static string? OldFormatRefusal(SessionPath location)
    {
        ArgumentNullException.ThrowIfNull(location);

        var legacy = Path.Combine(location.FullPath, LegacyRecordFileName);

        return File.Exists(legacy)
            ? $"'{location.FullPath}' holds a '{LegacyRecordFileName}', which is the record format BrowserAI used before {DataFileName} and this build does not read. "
                + $"It is not damaged and nothing was changed. There is no converter: a session in that format cannot be opened, listed, caught up on or destroyed by this build. "
                + $"Delete the directory yourself if you no longer need it, or move '{LegacyRecordFileName}' aside and call browserai_init to start a session here — the profile beneath it is a browser profile and is not BrowserAI's to read."
            : null;
    }

    /// <summary>The browser's <c>--user-data-dir</c>.</summary>
    public const string ProfileFolderName = "profile";

    /// <summary>The child's <c>--output-dir</c>.</summary>
    public const string OutputFolderName = "output";

    /// <summary>Where the browser puts downloads.</summary>
    public const string DownloadsFolderName = "downloads";

    /// <summary>Creates the directory and its three subfolders, idempotently.</summary>
    /// <remarks>
    /// <para>
    /// <b>Three, and there are no others to make.</b> ⚠️ <i>Corrected 2026-08-26
    /// (previously "The typed artifact folders are created on first use, not
    /// here", with the measurement that creating all of
    /// <c>ArtifactRouting.Folders</c> up front cost 10.4 ms per session against
    /// 2.5 ms for these three).</i> There are no typed artifact folders. The
    /// measurement stands and its subject does not: what it was weighing was a
    /// choice between creating ten directories eagerly and creating them
    /// lazily, and the answer now is that nothing creates them at all.
    /// </para>
    /// <para>
    /// <c>output\</c> exists before the child starts because it is the child's
    /// working directory <i>and</i> its <c>outputDir</c> <i>and</i> the root
    /// upstream's file-access check measures against; <c>downloads\</c> because
    /// the launch config names it; <c>profile\</c> because the browser is
    /// pointed at it.
    /// </para>
    /// </remarks>
    /// <param name="path">The canonicalised session directory.</param>
    public static void Create(SessionPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        _ = Directory.CreateDirectory(path.FullPath);
        _ = Directory.CreateDirectory(Path.Combine(path.FullPath, ProfileFolderName));
        _ = Directory.CreateDirectory(Path.Combine(path.FullPath, OutputFolderName));
        _ = Directory.CreateDirectory(Path.Combine(path.FullPath, DownloadsFolderName));
    }

    /// <summary>What a directory tree adds up to, in bytes.</summary>
    /// <remarks>
    /// Inaccessible entries are skipped rather than throwing: this number is
    /// reported, never enforced, and a session that cannot be sized should still
    /// answer the call it was asked.
    /// </remarks>
    /// <param name="directory">The tree to measure.</param>
    /// <returns>The total size of every file beneath it.</returns>
    public static long SizeOnDisk(string directory) => SizeAndFiles(directory).Bytes;

    /// <summary>What a directory tree adds up to, in bytes and in files.</summary>
    /// <remarks>
    /// <b>Both halves out of one walk.</b> <c>browserai_list</c> and
    /// <c>browserai_catch_up</c> both report what a session has written to
    /// <c>output\</c>, and a count without a size — or the reverse — is the
    /// half of a retention decision that cannot be acted on: forty files is
    /// nothing at four kilobytes each and a problem at four hundred megabytes.
    /// A second enumeration to get the other number would double the one cost
    /// this answer already pays.
    /// </remarks>
    /// <param name="directory">The tree to measure.</param>
    /// <returns>The total size and the file count.</returns>
    public static (long Bytes, int Files) SizeAndFiles(string directory)
    {
        var bytes = 0L;
        var files = 0;

        try
        {
            foreach (var file in new DirectoryInfo(directory)
                .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                bytes += file.Length;
                files++;
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Reported, never enforced: a session that cannot be sized should
            // still answer the call it was asked. A directory that is not there
            // is zero of both, which is what an output folder nothing has
            // written to actually is.
            return (0, 0);
        }

        return (bytes, files);
    }
}
