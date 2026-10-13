// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers;
using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BrowserAI.Interop;
using BrowserAI.Runtime;

namespace BrowserAI.Relay;

/// <summary>The tool list a relay's client was last answered, as the record keeps it.</summary>
/// <param name="Digest">The SHA-256 of the <c>tools/list</c> result the client was answered, byte for byte, in lower-case hexadecimal.</param>
/// <param name="Build">The version of the relay that answered it, for the log.</param>
/// <param name="ListedAt">When it was answered.</param>
internal sealed record ListedToolList(string Digest, string Build, DateTimeOffset ListedAt);

/// <summary>
/// Where a relay keeps the tool list its client was last answered, and where the
/// next relay that client starts finds it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's 127 c, 2026-10-12, in his words verbatim: <i>"127 c"</i></b>, of
/// the question whose option read <i>"Record which tool list each conversation last
/// fetched, and refuse only when it differs. That's a code change, best made after the
/// release."</i> Q261 b refuses, once, the first call of a connection that has not
/// listed, because a client that starts a dead server again calls from the list an
/// earlier server gave it. A relay that ended with no update between meets the same
/// refusal, and its sentence then says the list came from a different BrowserAI, which
/// is false there.
/// </para>
/// <para>
/// <b>What a client holds is the list of its process, so the record is kept per client
/// process.</b> Claude Code keeps the list its first connection fetched and calls a
/// server it started again from that list, and the server it starts again is the
/// child of the same process: in every one of the 27 runs of 2026-10-03 in which
/// <c>claude -p</c> or the stream-json transport started a server again and sent it no
/// <c>tools/list</c>, both servers' parent was the client, alive from before the first
/// until after the second (<see href="../../../kb/mcp/protocol.md">kb</see>). The key
/// is that process's pid and creation time, and the relay's own command line beside
/// them, so that two registrations of BrowserAI in one client keep two records.
/// </para>
/// <para>
/// <b>Never a refusal's input when it cannot be read.</b> A record that is absent,
/// unreadable or keyed by a client the relay could not read is no record, and the
/// relay then refuses as Q261 b does.
/// </para>
/// </remarks>
internal interface IToolListRecord
{
    /// <summary>What the record says this relay's client was last answered.</summary>
    /// <returns>The list, or <see langword="null"/> when there is no record, it cannot be read, or the client cannot be told.</returns>
    Task<ListedToolList?> ReadAsync();

    /// <summary>Records that this relay's client was just answered a list.</summary>
    /// <param name="listed">The list.</param>
    /// <returns>The write.</returns>
    /// <exception cref="IOException">The record could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The record's folder may not be written.</exception>
    Task WriteAsync(ListedToolList listed);
}

/// <summary>The digest a record names a tool list by.</summary>
internal static class ToolListDigest
{
    /// <summary>The SHA-256 of a <c>tools/list</c> result, in lower-case hexadecimal.</summary>
    /// <param name="result">The result's bytes, exactly as the client was answered them.</param>
    /// <returns>The digest.</returns>
    public static string Of(ReadOnlySpan<byte> result) => Convert.ToHexStringLower(SHA256.HashData(result));
}

/// <summary>The record of the tool list a client process was last answered, as one file under the data root.</summary>
/// <remarks>
/// <para>
/// <b>One file per client process and command line</b>, under
/// <see cref="DirectoryName"/>: <c>&lt;client pid&gt;-&lt;client creation
/// FILETIME&gt;-&lt;SHA-256 of the relay's command line&gt;.json</c>. The pid and the
/// creation time are the identity this repository gives every process; the command line
/// is what tells two registrations of BrowserAI in one client apart, a build of a
/// checkout's and the installed one, which keep two lists in that client.
/// </para>
/// <para>
/// <b>Written beside itself and renamed over, so a reader meets the old record or the
/// new one</b>, as <c>BackgroundRecord</c> writes its file, and the folder created on the
/// file-system thread (the maintainer's 81 c). A write that fails, a rename a reader
/// holds off among them, is not tried again, and the record it could not replace is
/// removed with the file beside it: the client holds the list it was just answered, so
/// the next relay of that client finds no record and refuses as Q261 b does. Only a
/// removal that fails as well leaves the older record, naming a list the client no
/// longer holds.
/// </para>
/// <para>
/// <b>A record whose client has gone is removed by the next relay that writes one</b>,
/// once per relay, by the pid and the creation time its name carries. A record of a live
/// client is never touched by another, and a file the folder holds that carries no such
/// name is left where it is.
/// </para>
/// </remarks>
internal sealed class ToolListRecordFile : IToolListRecord
{
    /// <summary>The folder under the data root that holds the records.</summary>
    public const string DirectoryName = "tool-lists";

    private const string Extension = ".json";

    private readonly string _directory;
    private readonly Lazy<string?> _path;
    private int _pruned;

    /// <summary>Creates the record of one relay's client.</summary>
    /// <param name="dataRoot">The data root the relay serves.</param>
    /// <param name="client">Reads the client process, the relay's parent, or answers <see langword="null"/> when it cannot be read. Asked once, on first use.</param>
    /// <param name="commandLine">The relay's own command line, as Windows handed it.</param>
    public ToolListRecordFile(string dataRoot, Func<ProcessStamp?> client, string commandLine)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataRoot);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(commandLine);

        _directory = Path.Combine(dataRoot, DirectoryName);
        _path = new Lazy<string?>(() => client() is { } stamp ? PathFor(dataRoot, stamp, commandLine) : null);
    }

    /// <summary>Where the record of one client process and command line lives.</summary>
    /// <param name="dataRoot">The data root.</param>
    /// <param name="client">The client process.</param>
    /// <param name="commandLine">The relay's command line.</param>
    /// <returns>The file's path.</returns>
    public static string PathFor(string dataRoot, ProcessStamp client, string commandLine)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataRoot);
        ArgumentNullException.ThrowIfNull(commandLine);

        var registration = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(commandLine)));

        return Path.Combine(
            dataRoot,
            DirectoryName,
            string.Create(CultureInfo.InvariantCulture, $"{client.ProcessId}-{client.CreatedFileTime}-{registration}{Extension}"));
    }

    /// <summary>The relay's parent, read through one handle and verified to have started no later than the relay.</summary>
    /// <returns>Its pid and creation time, or <see langword="null"/> when it cannot be read.</returns>
    public static ProcessStamp? Parent() =>
        ProcessLiveness.ReadParent() is { } parent ? new ProcessStamp(parent.ProcessId, parent.CreatedFileTime) : null;

    /// <summary>The client process a record's file name names, or <see langword="null"/> when the name is not a record's.</summary>
    /// <param name="fileName">The file's name, without a folder.</param>
    /// <returns>The pid and creation time.</returns>
    internal static ProcessStamp? ClientOf(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        var parts = fileName.Split('-', 3);

        return parts.Length is 3
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var created)
                ? new ProcessStamp(pid, created)
                : null;
    }

    /// <inheritdoc />
    public async Task<ListedToolList?> ReadAsync()
    {
        if (_path.Value is not { } path)
        {
            return null;
        }

        byte[] bytes;

        try
        {
            bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Absent, a folder that is not there, or refused: no record.
            return null;
        }

        return Parse(bytes);
    }

    /// <inheritdoc />
    public async Task WriteAsync(ListedToolList listed)
    {
        ArgumentNullException.ThrowIfNull(listed);

        if (_path.Value is not { } path)
        {
            // A client the relay cannot read has no key a record could be found by.
            return;
        }

        _ = await FileSystemThread.CreateDirectoryAsync(_directory).ConfigureAwait(false);

        var temporary = path + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";

        try
        {
            await File.WriteAllBytesAsync(temporary, Serialised(listed)).ConfigureAwait(false);
            await FileSystemThread.MoveFileAsync(temporary, path, overwrite: true).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // The client holds the list it was just answered, and the record it could not
            // replace names the one before: neither file is left to say otherwise.
            await TryRemoveAsync(temporary).ConfigureAwait(false);
            await TryRemoveAsync(path).ConfigureAwait(false);

            throw;
        }

        if (Interlocked.Exchange(ref _pruned, 1) is 0)
        {
            await RemoveTheRecordsOfClientsThatHaveGoneAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Removes a file, and says nothing when it cannot: a failure worth reporting is the caller's.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The removal.</returns>
    private static async Task TryRemoveAsync(string path)
    {
        try
        {
            await FileSystemThread.DeleteFileAsync(path).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Held by a reader, or refused. A file left beside a record goes with its
            // client's record once that client has gone.
        }
    }

    /// <summary>The record as its file holds it: one JSON object, written by this class and no other.</summary>
    /// <param name="listed">The list.</param>
    /// <returns>The file's bytes.</returns>
    internal static byte[] Serialised(ListedToolList listed)
    {
        ArgumentNullException.ThrowIfNull(listed);

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("digest", listed.Digest);
            writer.WriteString("build", listed.Build);
            writer.WriteString("listedAt", listed.ListedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>The record a file's bytes hold, or <see langword="null"/> when they do not hold one.</summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <returns>The list.</returns>
    internal static ListedToolList? Parse(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var reader = new Utf8JsonReader(bytes);

            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            return root.ValueKind is JsonValueKind.Object
                && Text(root, "digest") is { } digest
                && digest.Length == SHA256.HashSizeInBytes * 2
                && digest.All(char.IsAsciiHexDigitLower)
                && Text(root, "build") is { } build
                && Text(root, "listedAt") is { } at
                && DateTimeOffset.TryParseExact(at, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var listedAt)
                    ? new ListedToolList(digest, build, listedAt)
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// Removes every file of the folder whose name names a client process that has gone:
    /// its record, and a file a write of it left beside it.
    /// </summary>
    /// <returns>The removal. It never throws: a file that cannot be listed or removed is left for the next relay.</returns>
    private async Task RemoveTheRecordsOfClientsThatHaveGoneAsync()
    {
        IReadOnlyList<string> files;

        try
        {
            files = await FileSystemThread.RunAsync(() => FileSystemThread.Here.Files(_directory)).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files)
        {
            if (ClientOf(Path.GetFileName(file)) is not { } client || StillRuns(client))
            {
                continue;
            }

            try
            {
                await FileSystemThread.DeleteFileAsync(file).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // Held by a reader, or refused: the next relay that writes tries again.
            }
        }
    }

    /// <summary>Whether a client process still runs; one whose state cannot be read counts as running, so its record stays.</summary>
    /// <param name="client">The client.</param>
    /// <returns><see langword="true"/> unless it is known to have gone.</returns>
    private static bool StillRuns(ProcessStamp client)
    {
        try
        {
            return ProcessLiveness.IsAlive(client.ProcessId, client.CreatedFileTime);
        }
        catch (Win32Exception)
        {
            return true;
        }
    }
}
