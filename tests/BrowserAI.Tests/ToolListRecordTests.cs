// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using BrowserAI.Interop;
using BrowserAI.Relay;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The file a relay keeps the tool list its client was last answered in: where it lives,
/// whose it is, what a reader makes of a file it cannot parse, and which records a write
/// removes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's 127 c, 2026-10-12, in his words verbatim: <i>"127 c"</i>.</b> The
/// relay's half, the decision a record feeds, is <c>RelayTests</c>'; these arms hold the
/// file under it.
/// </para>
/// <para>
/// <b>Every record is a real file in a scratch folder</b>, and the clients are this
/// process, or this process's pid with a creation time it never had, which is the standing
/// shape of a process that has gone and whose pid Windows may have handed on. Nothing is
/// started, as in <c>BackgroundRecordTests</c>.
/// </para>
/// </remarks>
internal sealed class ToolListRecordTests
{
    /// <summary>The relay command line every arm's records are keyed by, an installed build's.</summary>
    private const string CommandLine = "\"C:\\Users\\someone\\AppData\\Local\\BrowserAI.app\\current\\BrowserAI.exe\" --mcp";

    /// <summary>What every arm records.</summary>
    private static readonly ListedToolList Listed = new(
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
        "9.9.9-tool-list-record-tests",
        new DateTimeOffset(2026, 10, 13, 9, 30, 0, TimeSpan.Zero));

    /// <summary>This process, as a relay's client.</summary>
    private static ProcessStamp ThisProcess => new(Environment.ProcessId, ProcessLiveness.CreationTimeOfThisProcess());

    /// <summary>This process's pid with a creation time it never had: a client that has gone.</summary>
    private static ProcessStamp AClientThatHasGone => ThisProcess with { CreatedFileTime = ThisProcess.CreatedFileTime - 1 };

    /// <summary>
    /// What one relay writes, the next relay of the same client reads, from a file under the
    /// data root in a folder of its own, named for the client and the relay's command line,
    /// with nothing left beside it.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WhatOneRelayWritesTheNextRelayOfTheSameClientReads()
    {
        using var data = await ScratchDirectory.CreateAsync("tool-list-record-round-trip");

        var client = ThisProcess;
        var path = ToolListRecordFile.PathFor(data.Path, client, CommandLine);
        var folder = Path.Combine(data.Path, ToolListRecordFile.DirectoryName);

        await Assert.That(Path.GetDirectoryName(path)).IsEqualTo(folder);
        await Assert.That(Path.GetFileName(path)).StartsWith(string.Create(CultureInfo.InvariantCulture, $"{client.ProcessId}-{client.CreatedFileTime}-"));
        await Assert.That(Path.GetFileName(path)).EndsWith(".json");

        var first = new ToolListRecordFile(data.Path, () => client, CommandLine);
        await first.WriteAsync(Listed);

        var next = new ToolListRecordFile(data.Path, () => client, CommandLine);

        await Assert.That(await next.ReadAsync()).IsEqualTo(Listed);
        await Assert.That(Directory.GetFiles(folder)).IsEquivalentTo([path]).Because("a write left a file beside the record");

        // A second write replaces the record whole.
        var later = Listed with { Digest = new string('f', 64), Build = "9.9.10-tool-list-record-tests" };
        await first.WriteAsync(later);

        await Assert.That(await next.ReadAsync()).IsEqualTo(later);
        await Assert.That(Directory.GetFiles(folder)).IsEquivalentTo([path]);
    }

    /// <summary>
    /// A record is found only by the client process and the relay command line that wrote it:
    /// another creation time, another pid and another command line each find none.
    /// </summary>
    /// <remarks>
    /// <b>The command line is what keeps two registrations of BrowserAI in one client
    /// apart</b>, a checkout's build and the installed one, which hold two lists in that
    /// client. A key without it fails the third read.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARecordIsFoundOnlyByTheClientProcessAndTheCommandLineThatWroteIt()
    {
        using var data = await ScratchDirectory.CreateAsync("tool-list-record-key");

        var client = ThisProcess;

        await new ToolListRecordFile(data.Path, () => client, CommandLine).WriteAsync(Listed);

        await Assert.That(await new ToolListRecordFile(data.Path, () => AClientThatHasGone, CommandLine).ReadAsync()).IsNull();
        await Assert.That(await new ToolListRecordFile(data.Path, () => client with { ProcessId = client.ProcessId + 4 }, CommandLine).ReadAsync()).IsNull();
        await Assert.That(await new ToolListRecordFile(data.Path, () => client, CommandLine + " --data-root \"C:\\Data\\Other\"").ReadAsync()).IsNull();

        // The positive control: the same key reads it.
        await Assert.That(await new ToolListRecordFile(data.Path, () => client, CommandLine).ReadAsync()).IsEqualTo(Listed);
    }

    /// <summary>A relay that cannot read its client writes nothing and reads no record.</summary>
    /// <remarks>
    /// <b>There is no key without the client</b>, and a record under a guessed one could be
    /// read by a relay of another client.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARelayThatCannotReadItsClientWritesNothingAndReadsNone()
    {
        using var data = await ScratchDirectory.CreateAsync("tool-list-record-no-client");

        var record = new ToolListRecordFile(data.Path, () => null, CommandLine);

        await record.WriteAsync(Listed);

        await Assert.That(Directory.Exists(Path.Combine(data.Path, ToolListRecordFile.DirectoryName))).IsFalse();
        await Assert.That(await record.ReadAsync()).IsNull();
    }

    /// <summary>A file that does not hold a whole record is no record.</summary>
    /// <remarks>
    /// <b>The safe direction</b>: no record means the relay refuses a call before a list as
    /// Q261 b does, so a damaged file costs a client one refusal and never a call from a list
    /// that differs.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFileThatDoesNotHoldAWholeRecordIsNone()
    {
        using var data = await ScratchDirectory.CreateAsync("tool-list-record-unreadable");

        var client = ThisProcess;
        var path = ToolListRecordFile.PathFor(data.Path, client, CommandLine);
        var record = new ToolListRecordFile(data.Path, () => client, CommandLine);

        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Nothing there at all.
        await Assert.That(await record.ReadAsync()).IsNull();

        var digest = Listed.Digest;
        var at = Listed.ListedAt.ToString("O", CultureInfo.InvariantCulture);

        string[] damaged =
        [
            string.Empty,
            "not a record",
            "[]",
            "{}",
            $"{{\"digest\":\"{digest[..63]}\",\"build\":\"b\",\"listedAt\":\"{at}\"}}",
            $"{{\"digest\":\"{digest.ToUpperInvariant()}\",\"build\":\"b\",\"listedAt\":\"{at}\"}}",
            $"{{\"digest\":\"{digest[..63]}g\",\"build\":\"b\",\"listedAt\":\"{at}\"}}",
            $"{{\"digest\":\"{digest}\",\"listedAt\":\"{at}\"}}",
            $"{{\"digest\":\"{digest}\",\"build\":\"b\",\"listedAt\":\"yesterday\"}}",
            $"{{\"digest\":\"{digest}\",\"build\":\"b\",\"listedAt\":5}}",
            $"{{\"digest\":\"{digest}\",\"build\":\"b\"",
        ];

        foreach (var text in damaged)
        {
            await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(text));

            await Assert.That(await record.ReadAsync()).IsNull().Because($"'{text}' was read as a record");
        }

        // The positive control: the bytes a write produces read back.
        await File.WriteAllBytesAsync(path, ToolListRecordFile.Serialised(Listed));

        await Assert.That(await record.ReadAsync()).IsEqualTo(Listed);
    }

    /// <summary>
    /// A relay's first write removes every record, and every file a write left beside one,
    /// of a client that has gone, and nothing else: a live client's record of another
    /// command line, and a file that is not a record's, stay. Its next write removes nothing.
    /// </summary>
    /// <remarks>
    /// <b>One record per client process for as long as it runs</b>, and none once it has
    /// gone, since a client that has gone holds no list. A write that removed nothing would
    /// leave the folder growing by one file for every client process BrowserAI ever served.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AWriteRemovesTheRecordsOfClientsThatHaveGoneAndNothingElse()
    {
        using var data = await ScratchDirectory.CreateAsync("tool-list-record-removal");

        var client = ThisProcess;
        var folder = Path.Combine(data.Path, ToolListRecordFile.DirectoryName);

        _ = Directory.CreateDirectory(folder);

        var gone = ToolListRecordFile.PathFor(data.Path, AClientThatHasGone, CommandLine);
        var goneBeside = gone + ".4242.tmp";
        var live = ToolListRecordFile.PathFor(data.Path, client, CommandLine + " --pipe suite");
        var unrelated = Path.Combine(folder, "notes.txt");

        foreach (var file in new[] { gone, goneBeside, live, unrelated })
        {
            await File.WriteAllBytesAsync(file, ToolListRecordFile.Serialised(Listed));
        }

        var record = new ToolListRecordFile(data.Path, () => client, CommandLine);
        await record.WriteAsync(Listed);

        var own = ToolListRecordFile.PathFor(data.Path, client, CommandLine);

        await Assert.That(Directory.GetFiles(folder).Order(StringComparer.OrdinalIgnoreCase))
            .IsEquivalentTo(new[] { own, live, unrelated }.Order(StringComparer.OrdinalIgnoreCase))
            .Because("a write removed a record it had no reason to, or left one of a client that has gone");

        // Once in a relay's life: a record of a client that has gone, arriving after the
        // first write, stays through the second.
        await File.WriteAllBytesAsync(gone, ToolListRecordFile.Serialised(Listed));
        await record.WriteAsync(Listed);

        await Assert.That(File.Exists(gone)).IsTrue();
    }

    /// <summary>
    /// A write that fails removes the record it could not replace and the file beside it, so
    /// the next relay of the client finds no record and not the list before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The failure is a reader holding the record</b>: Windows renames a file over another
    /// only when nobody has the target open, sharing it for deletion or not, measured on
    /// 2026-10-09 in <c>BackgroundRecordTests</c>. The reader here shares it for deletion, so
    /// the removal goes through where the rename did not.
    /// </para>
    /// <para>
    /// <b>The record left in place would name a list the client no longer holds</b>: the
    /// client was just answered the later one. A relay that then served the earlier list,
    /// after a rollback, would pass a call on from a list that differs.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AWriteThatFailsRemovesTheRecordItCouldNotReplace()
    {
        using var data = await ScratchDirectory.CreateAsync("tool-list-record-failed-write");

        var client = ThisProcess;
        var path = ToolListRecordFile.PathFor(data.Path, client, CommandLine);
        var folder = Path.Combine(data.Path, ToolListRecordFile.DirectoryName);
        var record = new ToolListRecordFile(data.Path, () => client, CommandLine);

        await record.WriteAsync(Listed);

        Exception? failed = null;

        await using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, FileOptions.Asynchronous))
        {
            try
            {
                await record.WriteAsync(Listed with { Digest = new string('f', 64) });
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                failed = failure;
            }
        }

        // The premise: without a failed write this arm holds nothing.
        await Assert.That(failed).IsNotNull().Because("the rename went through while a reader held the record");

        await Assert.That(await record.ReadAsync()).IsNull().Because("a write that failed left the record of the list before it");
        await Assert.That(Directory.GetFiles(folder)).IsEmpty().Because("a write that failed left a file in the folder");
    }

    /// <summary>A record's file name names its client, and no other file name names one.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARecordsFileNameNamesItsClientAndNoOtherNameDoes()
    {
        var client = ThisProcess;
        var name = Path.GetFileName(ToolListRecordFile.PathFor(@"C:\Data", client, CommandLine));

        await Assert.That(ToolListRecordFile.ClientOf(name)).IsEqualTo(client);
        await Assert.That(ToolListRecordFile.ClientOf(name + ".4242.tmp")).IsEqualTo(client);

        await Assert.That(ToolListRecordFile.ClientOf("notes.txt")).IsNull();
        await Assert.That(ToolListRecordFile.ClientOf("12-x-y.json")).IsNull();
        await Assert.That(ToolListRecordFile.ClientOf("-12-34-y.json")).IsNull();
        await Assert.That(ToolListRecordFile.ClientOf("12-34")).IsNull();
    }
}
