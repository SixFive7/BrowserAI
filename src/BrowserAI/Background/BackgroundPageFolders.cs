// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.App.Page;
using BrowserAI.Sessions;

namespace BrowserAI.Background;

/// <summary>
/// The session folders the page shows, read by the background: every folder the session
/// index names, every session the background holds, and what each weighs.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's 43 b, 2026-10-11</b>: the page may show each session folder's size
/// and the session folders not open now, which the background passes to it.
/// </para>
/// <para>
/// <b>Read and nothing else.</b> The index is walked at
/// <see cref="SessionIndexDepth.Presence"/>, which opens no session's store and probes no
/// lock, so a session an agent is using right now is not touched; a folder is weighed by
/// listing its files, and a junction or a symbolic link inside it is not followed, so a
/// folder linked into a session is never counted as part of it. Nothing here writes,
/// moves or deletes anything (41 a).
/// </para>
/// </remarks>
/// <param name="index">The session index of the background's data root.</param>
/// <param name="held">The folders of the sessions the background holds now.</param>
/// <param name="clock">The clock a measurement is stamped with.</param>
internal sealed class BackgroundPageFolders(SessionIndex index, Func<IEnumerable<string>> held, TimeProvider clock) : IPageFolders
{
    /// <summary>How a folder's files are listed: every file below it, past what cannot be read, and no reparse point followed.</summary>
    private static readonly EnumerationOptions Every = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <inheritdoc />
    public FolderMeasure Measure()
    {
        var folders = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var session in index.Present())
        {
            _ = folders.TryAdd(session.Key, session.FullPath);
        }

        foreach (var directory in held())
        {
            if (Canonical(directory) is { } session)
            {
                _ = folders.TryAdd(session.Key, session.FullPath);
            }
        }

        return new FolderMeasure(
            clock.GetUtcNow(),
            [
                .. folders.Values
                    .Select(static directory => new SessionFolder(CensusPageSessions.IdOf(directory), directory, Weigh(directory)))
                    .OrderByDescending(static folder => folder.Bytes ?? -1)
                    .ThenBy(static folder => folder.Directory, StringComparer.OrdinalIgnoreCase),
            ]);
    }

    /// <summary>A held session's folder as the index spells it, or <see langword="null"/> for one that names no session folder.</summary>
    private static SessionPath? Canonical(string directory)
    {
        try
        {
            return SessionPath.For(directory);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>What a folder's files weigh together, or <see langword="null"/> when it could not be listed.</summary>
    private static long? Weigh(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).EnumerateFiles("*", Every).Sum(static file => file.Length);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
