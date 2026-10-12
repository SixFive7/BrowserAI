// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Sessions;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// A session directory's four folders, made the way <see cref="SessionLayout.CreateAsync"/>
/// makes them, on the calling thread, for an arm that builds a session by hand.
/// </summary>
/// <remarks>
/// <b>Added 2026-10-12 with the maintainer's 81 c</b>, which put the product's creation of
/// those folders on the file-system thread. The folder names are the product's own
/// constants, so the layout an arm builds cannot drift from the one the product makes.
/// </remarks>
internal static class SessionFolders
{
    /// <summary>Makes the session directory and its profile, output and downloads folders.</summary>
    /// <param name="path">The canonicalised session directory.</param>
    public static void Create(SessionPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        _ = Directory.CreateDirectory(path.FullPath);
        _ = Directory.CreateDirectory(Path.Combine(path.FullPath, SessionLayout.ProfileFolderName));
        _ = Directory.CreateDirectory(Path.Combine(path.FullPath, SessionLayout.OutputFolderName));
        _ = Directory.CreateDirectory(Path.Combine(path.FullPath, SessionLayout.DownloadsFolderName));
    }
}
