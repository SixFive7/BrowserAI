// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using BrowserAI.Runtime;

namespace BrowserAI.Clients;

/// <summary>
/// How the background reads a client's own records: opened read-only, sharing read,
/// write and delete, read, and closed again at once, every time.
/// </summary>
/// <remarks>
/// <para>
/// <b>1.3 c, decided 2026-10-10</b>: <i>read the files on demand, when the dashboard or a
/// toast is drawn; never hold them open</i>. The client writes these files while
/// BrowserAI reads them, and deletes its per-process file when it exits (18 of 18 in the
/// measurement of 2026-10-08), so an open here must neither refuse the client's next
/// write nor its delete, which is what sharing all three grants. Nothing here is held
/// past the call that opened it.
/// </para>
/// <para>
/// <b>A file that cannot be read is a file with nothing in it</b>: absent, refused, or
/// gone between the open and the read, the answer is <see langword="null"/>, and the
/// caller falls back to the next source.
/// </para>
/// </remarks>
internal static class ClientRecordFile
{
    /// <summary>Every source of names the background reads, as the product reads them.</summary>
    public static ClientRecordAccess Files { get; } = new(ReadAllAsync, ReadEndsAsync, FoldersIn, ReadHeadAsync, FilesUnder, CodexState.TitleOfAsync);

    /// <summary>The first bytes of a file, as UTF-8 text, for a record whose start is all that names anything.</summary>
    /// <param name="path">The file.</param>
    /// <param name="bytes">How many bytes of its start.</param>
    /// <returns>The text, or <see langword="null"/> when the file is empty or cannot be read.</returns>
    public static async Task<string?> ReadHeadAsync(string path, int bytes)
    {
        try
        {
            var stream = Open(path);

            await using var opened = stream.ConfigureAwait(false);
            var buffer = new byte[(int)Math.Min(bytes, stream.Length)];

            if (buffer.Length is 0)
            {
                return null;
            }

            var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false).ConfigureAwait(false);

            return read is 0 ? null : Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The files a folder holds, itself and three folders deep, whose names match a pattern; none when it cannot be read.</summary>
    /// <remarks>
    /// <b>Three deep, and no deeper</b>, because Codex keeps a rollout under a year, a
    /// month and a day, and a junction under the folder then cannot send the walk on
    /// without end.
    /// </remarks>
    /// <param name="folder">The folder.</param>
    /// <param name="pattern">A file name pattern, <c>*</c> its only wildcard.</param>
    /// <returns>Their full paths, in ordinal order.</returns>
    public static IReadOnlyList<string> FilesUnder(string folder, string pattern)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return [];
            }

            var found = FileSystemThread.Here.Files(folder, pattern, new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 3,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive,
            }).ToList();

            found.Sort(StringComparer.Ordinal);
            return found;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>The whole of a small file, as UTF-8 text.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its text, or <see langword="null"/> when it cannot be read.</returns>
    public static async Task<string?> ReadAllAsync(string path)
    {
        try
        {
            using var reader = new StreamReader(Open(path), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: true);

            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The first and the last bytes of a file, each as UTF-8 text, the way the VS Code
    /// extension reads a conversation's record for its title.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="window">How many bytes of each end.</param>
    /// <returns>
    /// Both ends; the same text twice when the file is no longer than the window; or
    /// <see langword="null"/> when it is empty or cannot be read.
    /// </returns>
    public static async Task<(string Head, string Tail)?> ReadEndsAsync(string path, int window)
    {
        try
        {
            var stream = Open(path);

            await using var opened = stream.ConfigureAwait(false);
            var length = stream.Length;

            if (length is 0)
            {
                return null;
            }

            var buffer = new byte[(int)Math.Min(window, length)];
            var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false).ConfigureAwait(false);

            if (read is 0)
            {
                return null;
            }

            var head = Encoding.UTF8.GetString(buffer, 0, read);
            var tail = head;

            if (length > window)
            {
                stream.Position = length - window;

                var tailRead = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false).ConfigureAwait(false);

                tail = Encoding.UTF8.GetString(buffer, 0, tailRead);
            }

            return (head, tail);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The folders directly inside a folder, or none when it cannot be read.</summary>
    /// <param name="path">The folder.</param>
    /// <returns>Their full paths.</returns>
    public static IReadOnlyList<string> FoldersIn(string path)
    {
        try
        {
            return Directory.Exists(path) ? FileSystemThread.Here.Directories(path) : [];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>Opens a record for reading beside its writer, for asynchronous reads.</summary>
    /// <remarks>
    /// <b>Asynchronous since 2026-10-11</b>, the maintainer's decision 63, in his words
    /// verbatim: <i>"convert disk writes as well. Some users have slow spinning rust."</i>
    /// The coordinator added reads to it the same day. <i>Previously opened for
    /// synchronous reads.</i>
    /// </remarks>
    /// <param name="path">The file.</param>
    /// <returns>The stream.</returns>
    private static FileStream Open(string path) => new(path, new FileStreamOptions
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.ReadWrite | FileShare.Delete,
        BufferSize = 0,
        Options = FileOptions.Asynchronous,
    });
}

/// <summary>How a reader of a client's records reaches the files: the product's own reads, or the suite's.</summary>
/// <remarks>
/// <b>A seam for the suite</b>: the reads are the product's, and an arm wraps them to
/// hand the reader a record torn by a write, which no file on disk can be made to be
/// at the moment it is read.
/// </remarks>
/// <param name="ReadAll">The whole of a small file, or <see langword="null"/>.</param>
/// <param name="ReadEnds">The first and the last bytes of a file, or <see langword="null"/>.</param>
/// <param name="FoldersIn">The folders directly inside a folder.</param>
/// <param name="ReadHead">The first bytes of a file, or <see langword="null"/>.</param>
/// <param name="FilesUnder">The files under a folder whose names match a pattern.</param>
/// <param name="StateTitle">A Codex thread's first message from Codex's state database, or <see langword="null"/>.</param>
internal sealed record ClientRecordAccess(
    Func<string, Task<string?>> ReadAll,
    Func<string, int, Task<(string Head, string Tail)?>> ReadEnds,
    Func<string, IReadOnlyList<string>> FoldersIn,
    Func<string, int, Task<string?>> ReadHead,
    Func<string, string, IReadOnlyList<string>> FilesUnder,
    Func<string, string, Task<string?>> StateTitle);
