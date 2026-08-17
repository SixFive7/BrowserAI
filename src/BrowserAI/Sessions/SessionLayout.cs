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
/// the three that describe it — <c>lock.json</c>, the session log, and
/// [§F](../../../plan/F-artifacts.md)'s <c>session.json</c> — and artifacts get a
/// typed home instead of scattering among Chromium's internals.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-16 (previously: "<c>lock.json</c> is the only file at
/// the root and everything else is a subfolder").</b> That was true when this
/// file was written and stopped being true twice: the session log landed beside
/// it at step 12, and <c>session.json</c> — which [§F](../../../plan/F-artifacts.md)
/// puts in the session folder by name — lands beside both at step 14. The claim
/// it was making is still worth keeping and is restated above: no artifact is
/// ever at the root, so the files that <i>are</i> there all describe the session
/// rather than being things it produced.
/// </para>
/// <para>
/// <b>The session log is deliberately not created here.</b>
/// [§E](../../../plan/E-lifecycle.md) puts it at <c>&lt;session-dir&gt;\browserai.log</c>,
/// beside <c>lock.json</c>, and it holds <i>anything a session did</i>. At this
/// step a session does exactly one thing — it gets locked — and there is no
/// session lifetime to log into it, so a file created here and written by
/// nothing would be [a mechanism that only looks like
/// one](../Logging/ProcessLog.cs). It lands with the session tools. What this
/// step does provide is the half that is real today: every log record written
/// while a lock is held carries the session, through
/// <see cref="SessionLock"/>'s logging scope.
/// </para>
/// </remarks>
internal static class SessionLayout
{
    /// <summary>Ours. The lock and the record, and the only file at the root.</summary>
    public const string LockFileName = "lock.json";

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
    /// which is navigational noise in the tree
    /// [§F](../../../plan/F-artifacts.md) exists to make navigable.
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
