// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.App.Page;
using BrowserAI.Background;
using BrowserAI.Hosting;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// The session folders the background hands its page: every session the index names and
/// every one it holds, each weighed, and nothing touched.
/// </summary>
/// <remarks>
/// <b>43 b, the maintainer's choice of 2026-10-11</b>: the page may show each session
/// folder's size and the session folders not open now, which the background passes to it.
/// The index and the folders here are real ones in a scratch data root.
/// </remarks>
internal sealed class BackgroundPageFoldersTests
{
    /// <summary>
    /// A measurement lists every folder the index names that holds a session's record, and
    /// every folder the background holds, each with what its files weigh, largest first; a
    /// junction inside a folder is not followed, and a folder with no record, an entry whose
    /// folder is gone and the target of the junction are left as they were.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryIndexedSessionAndEveryHeldOneIsWeighedAndAJunctionInsideOneIsNotFollowed()
    {
        using var scratch = await ScratchDirectory.CreateAsync("page-folders");

        var paths = new LocalAppDataPaths(Path.Combine(scratch.Path, "appdata"));
        var index = new SessionIndex(paths, NullLogger.Instance);

        var first = await SessionAsync(scratch.Path, "first", 3_000);
        var second = await SessionAsync(scratch.Path, "second", 5_000);
        var held = await SessionAsync(scratch.Path, "held", 700);

        await index.RecordAsync(SessionPath.For(first));
        await index.RecordAsync(SessionPath.For(second));

        // A junction inside the second session, to a folder that weighs a mebibyte.
        var big = Directory.CreateDirectory(Path.Combine(scratch.Path, "big")).FullName;

        await File.WriteAllBytesAsync(Path.Combine(big, "large.bin"), new byte[1024 * 1024]);
        await PathAliases.JunctionAsync(Path.Combine(second, "link"), big);

        // A folder the index names that holds no session's record, and an entry whose folder is gone.
        var stranger = Directory.CreateDirectory(Path.Combine(scratch.Path, "stranger")).FullName;

        await File.WriteAllBytesAsync(Path.Combine(stranger, "profile.bin"), new byte[4_000]);
        await index.RecordAsync(SessionPath.For(stranger));
        await index.RecordAsync(SessionPath.For(Path.Combine(scratch.Path, "gone")));

        var entries = Directory.GetFiles(index.Root).Length;
        var measured = await new BackgroundPageFolders(index, () => [held], new ManualClock()).MeasureAsync();

        await Assert.That(string.Join(" | ", measured.Folders.Select(folder => folder.Directory)))
            .IsEqualTo(string.Join(" | ", new[] { second, first, held }));
        await Assert.That(measured.Folders.Select(folder => folder.Bytes).ToArray())
            .IsEquivalentTo(new long?[] { 5_000 + Record.Length, 3_000 + Record.Length, 700 + Record.Length });

        string[] ids = [CensusPageSessions.IdOf(second), CensusPageSessions.IdOf(first), CensusPageSessions.IdOf(held)];

        await Assert.That(measured.Folders.Select(folder => folder.Id).ToArray())
            .IsEquivalentTo(ids);

        // Nothing was touched: the junction's target, the stranger and every index entry are where they were.
        await Assert.That(File.Exists(Path.Combine(big, "large.bin"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(stranger, "profile.bin"))).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(second, "link"))).IsTrue();
        await Assert.That(Directory.GetFiles(index.Root).Length).IsEqualTo(entries);
    }

    /// <summary>What every scratch session's record holds: a stand-in, since only its presence is read.</summary>
    private static readonly byte[] Record = "a session's record"u8.ToArray();

    /// <summary>A folder holding a session's record and one file of the given weight.</summary>
    private static async Task<string> SessionAsync(string under, string name, int bytes)
    {
        var directory = Directory.CreateDirectory(Path.Combine(under, name)).FullName;

        await File.WriteAllBytesAsync(Path.Combine(directory, SessionLayout.DataFileName), Record);
        await File.WriteAllBytesAsync(Path.Combine(directory, "page.bin"), new byte[bytes]);

        return directory;
    }
}
