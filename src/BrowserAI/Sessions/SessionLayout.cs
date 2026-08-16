// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Sessions;

/// <summary>
/// What a session directory contains. One directory is one session, and it is
/// simultaneously the name, the handle and the lock.
/// </summary>
/// <remarks>
/// <para>
/// <c>lock.json</c> is the only file at the root and everything else is a
/// subfolder, so the one file that matters is unmissable and
/// [§F](../../plan/F-artifacts.md)'s routing gets a home instead of scattering
/// artifacts among Chromium's internals.
/// </para>
/// <para>
/// <b>The session log is deliberately not created here.</b>
/// [§E](../../plan/E-lifecycle.md) puts it at <c>&lt;session-dir&gt;\browserai.log</c>,
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
    /// <param name="path">The canonicalised session directory.</param>
    public static void Create(SessionPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        _ = Directory.CreateDirectory(path.FullPath);
        _ = Directory.CreateDirectory(Path.Combine(path.FullPath, ProfileFolderName));
        _ = Directory.CreateDirectory(Path.Combine(path.FullPath, OutputFolderName));
        _ = Directory.CreateDirectory(Path.Combine(path.FullPath, DownloadsFolderName));
    }
}
