// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Xml.Linq;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The suite's file copies and XML reads, each awaited over a stream opened for
/// asynchronous I/O.
/// </summary>
/// <remarks>
/// <b>Added 2026-10-11 with the maintainer's decisions 54 and 63</b>, in his words verbatim:
/// <i>"Never block a worker anywhere."</i> and <i>"convert disk writes as well. Some users
/// have slow spinning rust."</i> <c>File.Copy</c> and <c>XDocument.Load(string)</c> are banned
/// repository-wide since that day (<c>build/BannedSymbols.txt</c>), and these replace them
/// in the suite.
/// </remarks>
internal static class TestFiles
{
    /// <summary>How much of a file one read of a copy takes.</summary>
    private const int CopyBufferBytes = 1024 * 1024;

    /// <summary>How much of a file one read of a document takes: the framework's own default.</summary>
    private const int ReadBufferBytes = 4096;

    /// <summary>Copies a file the way <c>File.Copy</c> does, with the bytes read and written asynchronously.</summary>
    /// <remarks>
    /// The copy keeps the source's last-write time and attributes, as <c>CopyFileW</c> does.
    /// An alternate data stream is not copied, and nothing of the suite's reads one. The
    /// source is opened sharing everything, so a copy never fails where <c>File.Copy</c>
    /// would have succeeded.
    /// </remarks>
    /// <param name="source">The file to copy.</param>
    /// <param name="destination">Where the copy goes.</param>
    /// <param name="overwrite">Whether a file already there is replaced; otherwise its presence is an <see cref="IOException"/>.</param>
    /// <returns>The copy.</returns>
    public static async Task CopyAsync(string source, string destination, bool overwrite = false)
    {
        await using (var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var to = new FileStream(destination, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferBytes, FileOptions.Asynchronous))
        {
            await from.CopyToAsync(to, CopyBufferBytes);
        }

        File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
        File.SetAttributes(destination, File.GetAttributes(source));
    }

    /// <summary>Reads each file's text, keyed by its full path, for a query that reads them.</summary>
    /// <param name="files">The files.</param>
    /// <returns>Each text by its file's full path, compared without case.</returns>
    public static Task<IReadOnlyDictionary<string, string>> ReadAllTextAsync(IEnumerable<FileInfo> files) =>
        ReadAllTextAsync(files.Select(static file => file.FullName));

    /// <summary>Reads each file's text, keyed by its path as given, for a query that reads them.</summary>
    /// <param name="paths">The files.</param>
    /// <returns>Each text by its path, compared without case.</returns>
    public static async Task<IReadOnlyDictionary<string, string>> ReadAllTextAsync(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            texts[path] = await File.ReadAllTextAsync(path);
        }

        return texts;
    }

    /// <summary>Loads each file's XML document, keyed by its full path, for a query that reads them.</summary>
    /// <param name="files">The files.</param>
    /// <returns>Each document by its file's full path, compared without case.</returns>
    public static async Task<IReadOnlyDictionary<string, XDocument>> LoadXmlAsync(IEnumerable<FileInfo> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var documents = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            documents[file.FullName] = await LoadXmlAsync(file.FullName);
        }

        return documents;
    }

    /// <summary>Loads an XML document the way <c>XDocument.Load(string)</c> does, read asynchronously.</summary>
    /// <param name="path">The document.</param>
    /// <returns>The document.</returns>
    public static async Task<XDocument> LoadXmlAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, ReadBufferBytes, FileOptions.Asynchronous);
        return await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
    }
}
