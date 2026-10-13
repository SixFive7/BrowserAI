// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The tool-list records a run's relays write under the data root the suite shares with the
/// installed BrowserAI: the session's end removes those keyed by its own test host, and the
/// clearance names one left behind.
/// </summary>
/// <remarks>
/// <b>The maintainer's 160 c, 2026-10-13, in his words verbatim: <i>"160 c"</i></b>, both
/// halves of it. See <see cref="SuiteToolLists"/>.
/// </remarks>
internal sealed partial class SuiteCoverageTests
{
    /// <summary>
    /// A test host's session end removes the tool-list record keyed by itself under the data
    /// root the suite shares with the installed BrowserAI.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The first half of the maintainer's 160 c.</b> Every relay the suite starts is its
    /// test host's child and keys its tool-list record by it (127 c), so a run leaves a record
    /// per relay in that folder unless its session's end removes them, and a gate had left 29.
    /// </para>
    /// <para>
    /// <b>A child test host, because a run cannot watch its own session end.</b> The child
    /// selects <see cref="AReleaseRunFailsWhereAnOrdinaryRunSkips"/>, a pure test, and is handed
    /// <see cref="SuiteToolLists.PlantVariable"/>, so its session's start writes a record keyed
    /// by itself into the shared folder and names it in this arm's scratch, and its session's
    /// end names there each record it removed. The first name is the positive control: a child
    /// that planted nothing would leave nothing to find.
    /// </para>
    /// <para>
    /// <b>The child's account is what the arm holds, and the record's absence only beside it</b>:
    /// any relay of another arm that writes a record removes the records of clients that have
    /// gone, the child's among them, so a record the child's session end had left could be gone
    /// by the time this arm looks. A record the child left is removed here before the
    /// assertions, so a red leaves nothing behind for the clearance to stop the gate on.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-13</b> with the session end's removal taken out: the child's
    /// account named no removal of the record it planted, and the arm removed the record the
    /// child left.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ATestHostsSessionEndRemovesTheToolListRecordKeyedByItself()
    {
        // The recursion guard of the child arms above.
        if (Environment.GetEnvironmentVariable(SuiteFilter.ProbeVariable) is { Length: > 0 })
        {
            return;
        }

        var host = Path.Combine(AppContext.BaseDirectory, "BrowserAI.Tests.exe");

        await Assert.That(File.Exists(host)).IsTrue().Because(host);

        using var probe = await ScratchDirectory.CreateAsync("tool-list-plant");
        var planted = Path.Combine(probe.Path, "planted.txt");

        var run = await ChildRunner.RunAsync(
            host,
            ["--disable-logo", "--treenode-filter", "/*/*/SuiteCoverageTests/" + nameof(AReleaseRunFailsWhereAnOrdinaryRunSkips)],
            AppContext.BaseDirectory,
            TestDefaults.ProcessHang,
            new Dictionary<string, string?>
            {
                [SuiteFilter.ProbeVariable] = Path.Combine(probe.Path, "filter.txt"),
                [SuiteToolLists.PlantVariable] = planted,
            },
            encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var console = run.StandardOutput + run.StandardError;

        await Assert.That(run.Exited).IsTrue().Because(console);

        // The child reached its session's end, whose removal comes before the coverage block.
        await Assert.That(console).Contains("suite coverage -- what this run actually exercised").Because(console);
        await Assert.That(File.Exists(planted)).IsTrue().Because(console);

        var lines = await File.ReadAllLinesAsync(planted);
        var record = lines[0].Trim();
        var leftBehind = File.Exists(record);

        if (leftBehind)
        {
            File.Delete(record);
        }

        await Assert.That(Path.GetDirectoryName(record)).IsEqualTo(SuiteToolLists.SharedFolder);
        await Assert.That(SuiteToolLists.ClientOf(record) is { } child && child != SuiteToolLists.ThisTestHost && SuiteToolLists.HasGone(child))
            .IsTrue()
            .Because($"'{record}' is not keyed by a child test host that has gone");
        await Assert.That(lines.Skip(1).Contains(SuiteToolLists.RemovedPrefix + Path.GetFileName(record), StringComparer.Ordinal))
            .IsTrue()
            .Because($"the child's session ended without removing '{record}'; it said: {string.Join(" | ", lines)}{Environment.NewLine}{console}");
        await Assert.That(leftBehind)
            .IsFalse()
            .Because($"the child's session ended and left '{record}' behind.{Environment.NewLine}{console}");
    }

    /// <summary>
    /// The session end's removal takes the records of its own test host, and a file a write of
    /// one left beside it, and leaves every other file in the folder where it is.
    /// </summary>
    /// <remarks>
    /// <b>The other direction of the arm above</b>, over a scratch data root: the shared folder
    /// holds the maintainer's own records once the installed BrowserAI carries 127 c, and a
    /// removal that took a record of any other client would take his.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSessionEndsRemovalTakesTheRecordsOfItsOwnTestHostAndNoOther()
    {
        using var data = await ScratchDirectory.CreateAsync("tool-list-removal");

        var own = await SuiteToolLists.PlantAsync(data.Path, SuiteToolLists.ThisTestHost, "suite removal arm");
        var beside = own + ".4242.tmp";
        var another = await SuiteToolLists.PlantAsync(data.Path, SuiteToolLists.ThisTestHost with { CreatedFileTime = SuiteToolLists.ThisTestHost.CreatedFileTime - 1 }, "suite removal arm");
        var unnamed = Path.Combine(Path.GetDirectoryName(own)!, "notes.txt");

        await File.WriteAllTextAsync(beside, "a write of the record left this beside it");
        await File.WriteAllTextAsync(unnamed, "a file whose name names no client");

        var (removed, left) = SuiteToolLists.RemoveTheRecordsOf(data.Path, SuiteToolLists.ThisTestHost);

        await Assert.That(string.Join(" | ", removed.Order(StringComparer.Ordinal)))
            .IsEqualTo(string.Join(" | ", new[] { Path.GetFileName(own), Path.GetFileName(beside) }.Order(StringComparer.Ordinal)));
        await Assert.That(left).IsEmpty();

        await Assert.That(File.Exists(own)).IsFalse();
        await Assert.That(File.Exists(beside)).IsFalse();
        await Assert.That(File.Exists(another)).IsTrue();
        await Assert.That(File.Exists(unnamed)).IsTrue();

        // And a data root with no such folder is no failure.
        var (none, nothingLeft) = SuiteToolLists.RemoveTheRecordsOf(Path.Combine(data.Path, "nowhere"), SuiteToolLists.ThisTestHost);

        await Assert.That(none).IsEmpty();
        await Assert.That(nothingLeft).IsEmpty();
    }

    /// <summary>
    /// The clearance snapshot names a tool-list record keyed by a test host of this checkout,
    /// leaves a record keyed by any other client alone, and says none once the record has gone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The second half of the maintainer's 160 c.</b> The snapshot did not look in the
    /// data root's <c>tool-lists</c>, so the 29 records a gate had left there stopped nothing.
    /// Its eleventh reading names every record keyed by a test host a session's start recorded,
    /// marked <c>MUST BE ABSENT</c>, so a record left behind is a difference between a run's two
    /// snapshots, which stops the gate.
    /// </para>
    /// <para>
    /// <b>Driven against a scratch data root, through the script's <c>-DataRoot</c></b>, which
    /// the gate never passes, with this test host as the recorded one: its session's start
    /// recorded it, so a record keyed by it is one the reading must name, and a record keyed by
    /// this pid with a creation time it never had belongs to a client no session recorded. The
    /// second snapshot, after the removal, is the positive control for the first.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-13</b> against the script without its eleventh reading, which
    /// wrote no such line, and against the script of <c>40911cbb</c>, which refused
    /// <c>-DataRoot</c> and wrote no snapshot.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheClearanceNamesAToolListRecordATestHostLeftBehind()
    {
        using var data = await ScratchDirectory.CreateAsync("clearance-tool-lists");

        var leftBehind = await SuiteToolLists.PlantAsync(data.Path, SuiteToolLists.ThisTestHost, "suite clearance arm");

        _ = await SuiteToolLists.PlantAsync(data.Path, SuiteToolLists.ThisTestHost with { CreatedFileTime = SuiteToolLists.ThisTestHost.CreatedFileTime - 1 }, "suite clearance arm");

        var left = await ToolListReadingAsync(data.Path);

        File.Delete(leftBehind);

        var gone = await ToolListReadingAsync(data.Path);

        await Assert.That(left).IsEqualTo($"  keyed by a test host: {Path.GetFileName(leftBehind)} <<< MUST BE ABSENT");
        await Assert.That(gone).IsEqualTo("  keyed by a test host: none");
    }

    /// <summary>Runs the clearance script over a data root and returns its tool-list line.</summary>
    /// <param name="dataRoot">The data root the script reads in place of the one every BrowserAI resolves.</param>
    /// <returns>The line.</returns>
    private static async Task<string> ToolListReadingAsync(string dataRoot)
    {
        var tag = $"suite-tool-lists-{Guid.NewGuid():N}";
        var snapshot = Path.Combine(RepositoryLayout.Root.FullName, ".work", "clearance", $"{tag}.txt");

        await RunTheClearanceScriptAsync(["-Tag", tag, "-DataRoot", dataRoot]);

        try
        {
            var lines = await File.ReadAllLinesAsync(snapshot);

            return lines.Single(line => line.StartsWith("  keyed by a test host: ", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(snapshot);
        }
    }
}
