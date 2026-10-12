// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Runtime;

namespace BrowserAI.App.Page;

/// <summary>What the after-update notice's record says: the version an update installed, and whether the person has seen what is new in it.</summary>
/// <param name="Version">The version an update installed.</param>
/// <param name="Seen">Whether a <i>What is new</i> link or the notice's dismiss has been pressed since.</param>
internal sealed record UpdateNotice(string Version, bool Seen);

/// <summary>Where the after-update notice's record is kept: one line in a file under the data root.</summary>
/// <remarks>
/// <para>
/// <b>The maintainer's 57 a, 2026-10-11, and his words verbatim: <i>"57 a - also allow
/// dismissing the banner in a nice way."</i></b> The notice is one-time: pressing any of the
/// <i>What is new</i> links, which all open the changelog page, or the notice's dismiss marks
/// it seen, and it comes back only when a newer version is installed. The browser cannot
/// remember it, because every listener is a new origin, so the background keeps it.
/// </para>
/// <para>
/// <b>Under the data root and never the install root</b>, for the reason
/// <c>UpdateCheckStampFile</c> gives: an update replaces the install root's <c>current\</c>,
/// so a record kept there would vanish on the restart it exists to survive. <b>One line, the
/// version and a word</b>, <c>2.0.0 waiting</c> or <c>2.0.0 seen</c>, so a person can read it;
/// a file that holds anything else is no record, and the notice then shows only after the
/// next update.
/// </para>
/// </remarks>
internal static class UpdateNoticeFile
{
    /// <summary>The record's file name, directly under the data root.</summary>
    public const string FileName = "update-notice.txt";

    private const string Waiting = "waiting";

    private const string Seen = "seen";

    /// <summary>Reads the record under a data root.</summary>
    /// <param name="dataRoot">The data root.</param>
    /// <returns>What it says, or <see langword="null"/> where there is none or it cannot be read.</returns>
    public static async Task<UpdateNotice?> ReadAsync(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataRoot);

        string text;

        try
        {
            text = await File.ReadAllTextAsync(Path.Combine(dataRoot, FileName)).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var parts = text.Trim().Split(' ');

        return parts is [{ Length: > 0 } version, Waiting or Seen]
            ? new UpdateNotice(version, string.Equals(parts[1], Seen, StringComparison.Ordinal))
            : null;
    }

    /// <summary>Writes the record under a data root, beside itself first and then moved into place.</summary>
    /// <param name="dataRoot">The data root.</param>
    /// <param name="notice">What it says.</param>
    /// <returns>The write.</returns>
    /// <exception cref="IOException">The record could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The record could not be written.</exception>
    public static async Task WriteAsync(string dataRoot, UpdateNotice notice)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataRoot);
        ArgumentNullException.ThrowIfNull(notice);

        var path = Path.Combine(dataRoot, FileName);
        var temporary = path + ".partial";

        _ = await FileSystemThread.CreateDirectoryAsync(dataRoot).ConfigureAwait(false);
        await File.WriteAllTextAsync(temporary, notice.Version + " " + (notice.Seen ? Seen : Waiting) + "\n").ConfigureAwait(false);
        await FileSystemThread.MoveFileAsync(temporary, path, overwrite: true).ConfigureAwait(false);
    }
}
