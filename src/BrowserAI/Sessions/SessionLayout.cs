// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Artifacts;

namespace BrowserAI.Sessions;

/// <summary>
/// What a session directory contains. One directory is one session, and it is
/// simultaneously the name, the handle and the lock.
/// </summary>
/// <remarks>
/// <para>
/// Everything a session accumulates is a subfolder, so the files at the root are
/// the three that describe it — <c>browserai.json</c>, the session log, and the
/// artifact index <c>session.json</c> — and artifacts get a typed home instead of
/// scattering among Chromium's internals.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-16 (previously: "<c>browserai.json</c> is the only file at
/// the root and everything else is a subfolder").</b> That was true when this
/// file was written and stopped being true twice: the session log landed beside
/// it once sessions had a lifetime to log, and <c>session.json</c> — which
/// <see cref="Artifacts.ArtifactRouter"/> writes into the session folder by name
/// — landed beside both once artifacts were routed. The claim it was making is
/// still worth keeping and is restated above: <b>no artifact is ever at the
/// root</b>, so the files that <i>are</i> there all describe the session rather
/// than being things it produced. The generated Playwright config stays
/// forbidden here for the same reason — it is a per-run artifact and lives in the
/// run's instance directory.
/// </para>
/// <para>
/// <b>The session log is deliberately not created here.</b>
/// <see cref="Logging.SessionLogFile"/> puts it at
/// <c>&lt;session-dir&gt;\browserai.log</c>, beside <c>browserai.json</c>, and it
/// holds <i>anything a session did</i> — so a file created by the layout and
/// written by nothing would be [a mechanism that only looks like
/// one](../Logging/ProcessLog.cs). It is created by the thing that writes it.
/// What the layout does provide is the half that is real from the moment a
/// directory is claimed: every log record written while a lock is held carries
/// the session, through <see cref="SessionLock"/>'s logging scope.
/// </para>
/// </remarks>
internal static class SessionLayout
{
    /// <summary>
    /// Ours. The session's own record, and the file whose open handle is the
    /// directory's lock.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Renamed 2026-08-20 (previously <c>lock.json</c>).</b> The record had
    /// stopped being only a lock: every field of it is an ordered list of
    /// timestamped statements about how the session got here — mode, browser,
    /// purpose, holder, client — and <c>browserai_list</c> and
    /// <c>browserai_resume</c> read it for those rather than for ownership. A
    /// name that said <i>lock</i> described the smallest thing the file does.
    /// <b>There is no compatibility read and no migration</b>: the maintainer
    /// took that decision on 2026-08-20, on the ground that nothing is in
    /// production and the only build that ever wrote the old name is this one.
    /// </remarks>
    public const string LockFileName = "browserai.json";

    /// <summary>
    /// What a record being written durably is called before it is renamed over
    /// <see cref="LockFileName"/> — and therefore what its presence beside an
    /// absent <c>browserai.json</c> means.
    /// </summary>
    /// <remarks>
    /// <b>Written once because two components read it for opposite reasons.</b>
    /// <c>SessionLock.WriteDurably</c> composes the name; <c>SessionIndex</c>
    /// matches it to tell <i>this directory is not a session</i> from <i>a
    /// rewrite is in flight right now</i>, which is the difference between
    /// dropping an entry and keeping it. A pattern that drifted from the name
    /// would make the second reader silently see nothing, which is the
    /// pre-2026-08-19 behaviour restored.
    /// </remarks>
    public const string NewLockFilePattern = $"{LockFileName}.new-*";

    /// <summary>The name a durable write gives its temp file.</summary>
    /// <returns>A fresh name matching <see cref="NewLockFilePattern"/>.</returns>
    public static string NewLockFileName() => $"{LockFileName}.new-{Guid.NewGuid():N}";

    /// <summary>The browser's <c>--user-data-dir</c>.</summary>
    public const string ProfileFolderName = "profile";

    /// <summary>The child's <c>--output-dir</c>.</summary>
    public const string OutputFolderName = "output";

    /// <summary>Where the browser puts downloads.</summary>
    public const string DownloadsFolderName = "downloads";

    /// <summary>Creates the directory and its three subfolders, idempotently.</summary>
    /// <remarks>
    /// <para>
    /// <b>The typed artifact folders are created on first use, not here</b>, and
    /// that was measured rather than assumed. Creating all of
    /// <see cref="ArtifactRouting.Folders"/> up front costs <b>10.4 ms per
    /// session</b> against <b>2.5 ms</b> for these three (measured twice,
    /// 2026-08-16, 120 sessions per pass) — about a second per suite run, plus
    /// the same again reclaiming them. It also leaves ten empty directories in
    /// every session a caller ever creates, for generators they never used,
    /// which is navigational noise in the tree the typed folders exist to make
    /// navigable in the first place.
    /// </para>
    /// <para>
    /// The property that would have bought is not lost: the folder set is
    /// declared by <see cref="ArtifactRouting"/> and asserted against the
    /// resolved child's prefixes on every build, and <c>session.json</c> names
    /// every one of them with its resolved path whether it exists yet or not.
    /// What changes is that a folder on disk now means <i>an artifact of that
    /// kind was produced</i>.
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
    public static long SizeOnDisk(string directory)
    {
        try
        {
            return new DirectoryInfo(directory)
                .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Sum(file => file.Length);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
