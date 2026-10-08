// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The tool list compiled into the binary: answered with no Playwright running,
/// each session's child held to it byte for byte, and what a difference says.
/// </summary>
/// <remarks>
/// <para>
/// <b>Step 1 of the one-binary plan, built 2026-10-08.</b> The maintainer's words of
/// 2026-10-04, verbatim: <i>"I'd argue that the relay always answers the tool list
/// from the binary. I see no reason why it would ever defer to Playwright, as the
/// Playwright version is bound to that binary version is it not?"</i> Until then
/// every server started a Playwright of its own before it answered its handshake,
/// for one reason: to be asked <c>tools/list</c>.
/// </para>
/// <para>
/// <b>Three arms drive the published binary and the rest are the list itself.</b>
/// The rig's arm for the refusal is
/// <c>ErrorCatalogueTests.TheInstallIsBrokenRowIsEmittedByASessionChildWhoseListDiffers</c>,
/// where the catalogue's census needs it.
/// </para>
/// </remarks>
internal sealed class BuiltInToolListTests
{
    /// <summary>
    /// The published server answers <c>tools/list</c> while no Playwright server runs
    /// in its job, and the list is the compiled one through the rewrite.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Planted red 2026-10-08 against the published binary that still started its
    /// own child before serving: the job held the payload's <c>node.exe</c>.
    /// </para>
    /// <para>
    /// ⚠️ <i>Corrected 2026-10-08 (previously
    /// <c>ThePublishedServerAnswersTheToolListWithNothingFromThePayloadRunning</c>,
    /// which held that no process in the job ran an image from the payload)</i>:
    /// the server's own stray sweep starts the registry reap when it ends a stray
    /// browser, which is the payload's <c>node.exe</c> running
    /// <c>playwright-core</c>'s registry, in this job because the server is. The Git
    /// Bash half of lane S1's gate at <c>079e3d1c</c> met one. What this arm is about
    /// is the child that answered <c>tools/list</c>, a <c>node.exe</c> from the
    /// payload running <c>@playwright/mcp</c>'s <c>cli.js</c>, so that is what it
    /// looks for, by the command line.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePublishedServerAnswersTheToolListWithNoPlaywrightServerRunning()
    {
        SuiteEnvironment.RequirePublishedSlice();

        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create("built-in-list");

        await using var client = RawStdioClient.Start(
            PublishedSlice.Executable,
            PublishedSlice.Mcp,
            scratch.Path,
            PublishedSlice.InheritedEnvironment());

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion, listsTools: false);

        var listed = await client.RoundTripAsync("tools/list", new JsonObject());

        var payload = Path.Combine(PublishedSlice.Directory, "payload") + Path.DirectorySeparatorChar;
        var cli = Path.Combine("mcp", "node_modules", "@playwright", "mcp", "cli.js");

        var playwrightServers = client.JobProcessIds()
            .Where(processId => processId != client.ProcessId)
            .Select(processId => (Id: processId, Image: ProcessCommandLine.ImagePathOf(processId), CommandLine: ProcessCommandLine.Of(processId)))
            .Where(member => member.Image?.StartsWith(payload, StringComparison.OrdinalIgnoreCase) is true
                && member.CommandLine?.Contains(cli, StringComparison.OrdinalIgnoreCase) is true)
            .Select(member => $"{member.Id} {member.CommandLine}")
            .ToList();

        await Assert.That(string.Join("; ", playwrightServers)).IsEmpty();

        var expected = SessionToolSurface.Rewrite(UpstreamToolList.Compiled.Result(), RepositoryVerdicts.Committed);

        await Assert.That(string.Join(", ", Names(listed)))
            .IsEqualTo(string.Join(", ", Names(expected)));
    }

    /// <summary>
    /// A session opens in each family the product offers, which is the check passing:
    /// each child was asked for its tools right after its handshake and answered the
    /// compiled list byte for byte.
    /// </summary>
    /// <remarks>
    /// <b>No browser starts</b>: <c>browserai_init</c> starts the session's child and
    /// the check, and a browser starts at the first browser call. Measured before the
    /// check was built, 2026-10-08: five configurations of the payload's own child,
    /// Chromium and Firefox among them, each answered the 45,612 bytes the compiled
    /// list produces.
    /// </remarks>
    /// <param name="browser">The family.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments("chromium")]
    [Arguments("firefox")]
    public async Task ASessionOpensInEachFamilyBecauseItsChildListsTheCompiledToolsByteForByte(string browser)
    {
        SuiteEnvironment.RequirePublishedSlice();

        if (browser is "firefox")
        {
            SuiteEnvironment.RequireProvisionedFirefox();
        }
        else
        {
            SuiteEnvironment.RequireProvisionedChromium();
        }

        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create($"built-in-list-{browser}");

        await using var client = RawStdioClient.Start(
            PublishedSlice.Executable,
            PublishedSlice.Mcp,
            scratch.Path,
            PublishedSlice.InheritedEnvironment());

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);

        var session = Path.Combine(scratch.Path, "session");

        var opened = await client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = SessionToolSurface.Init,
            ["arguments"] = new JsonObject
            {
                ["directory"] = session,
                ["browser"] = browser,
                ["purpose"] = "the built-in tool list's own check",
            },
        });

        var text = string.Concat((opened["content"]?.AsArray() ?? []).Select(block => (string?)block?["text"] ?? string.Empty));

        await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true).Because(text);
        await Assert.That(text).Contains("Session ready.");

        _ = await client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = SessionToolSurface.Destroy,
            ["arguments"] = new JsonObject
            {
                ["directory"] = session,
                ["why"] = "the suite's own session, done",
            },
        });
    }

    /// <summary>The list compiled into the binary is the committed snapshot's tools, with the indentation taken out.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCompiledListIsTheCommittedSnapshotsToolsWithTheIndentationTakenOut()
    {
        var committed = await File.ReadAllBytesAsync(SnapshotPath);
        var reread = UpstreamToolList.Parse(committed, SnapshotPath);

        await Assert.That(UpstreamToolList.Compiled.ResultBytes.SequenceEqual(reread.ResultBytes)).IsTrue();

        // The same tools, in the same order, as the committed file's own parse.
        using var snapshot = System.Text.Json.JsonDocument.Parse(committed);
        var names = snapshot.RootElement.GetProperty("tools").EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).ToList();

        await Assert.That(UpstreamToolList.Compiled.Count).IsEqualTo(names.Count);
        await Assert.That(string.Join(", ", Names(UpstreamToolList.Compiled.Result()))).IsEqualTo(string.Join(", ", names));

        // And nothing outside a string survived: no newline, no indent.
        await Assert.That(Encoding.UTF8.GetString(UpstreamToolList.Compiled.ResultBytes)).DoesNotContain("\n");
    }

    /// <summary>
    /// The binary carries the snapshot byte for byte as the repository holds it.
    /// The verdicts beside it are <c>ToolVerdictTests</c>'s.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheBinaryCarriesTheSnapshotByteForByte()
    {
        var snapshot = await File.ReadAllBytesAsync(SnapshotPath);

        var carried = Resource(typeof(UpstreamToolList).Assembly, UpstreamToolList.ResourceName).AsSpan().SequenceEqual(snapshot);

        await Assert.That(carried).IsTrue();
    }

    /// <summary>The comparison, every shape of difference it names, and the bytes it takes as the same.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryDifferenceNamesTheFirstToolItMeets()
    {
        var list = UpstreamToolList.Parse(Encoding.UTF8.GetBytes(Two), "two");

        // The same bytes are the same list; the snapshot's indentation is not part of it.
        await Assert.That(list.FirstDifference(Encoding.UTF8.GetBytes(Two))).IsNull();
        await Assert.That(UpstreamToolList.Parse(Encoding.UTF8.GetBytes("{\n  \"count\": 2,\n  \"tools\": " + Two[9..^1] + "\n}"), "indented").FirstDifference(Encoding.UTF8.GetBytes(Two))).IsNull();

        // A definition that differs, a name that differs, one more and one fewer.
        await Assert.That(list.FirstDifference(Encoding.UTF8.GetBytes(Two.Replace("\"b\"", "\"B\"", StringComparison.Ordinal))))
            .IsEqualTo("'second', whose definition differs");
        await Assert.That(list.FirstDifference(Encoding.UTF8.GetBytes(Two.Replace("\"second\"", "\"other\"", StringComparison.Ordinal))))
            .IsEqualTo("'second', where the browser server lists 'other'");
        await Assert.That(list.FirstDifference(Encoding.UTF8.GetBytes(Two[..^2] + ",{\"name\":\"third\",\"description\":\"c\"}]}")))
            .IsEqualTo("'third', which the browser server lists and this BrowserAI does not");
        await Assert.That(list.FirstDifference(Encoding.UTF8.GetBytes("{\"tools\":[" + Two[10..Two.IndexOf("},{", StringComparison.Ordinal)] + "}]}")))
            .IsEqualTo("'second', which this BrowserAI lists and the browser server does not");

        // Byte for byte means byte for byte: the child's own whitespace is a difference.
        await Assert.That(list.FirstDifference(Encoding.UTF8.GetBytes(Two.Replace("[{", "[ {", StringComparison.Ordinal))))
            .IsNotNull();

        // And a list with the same tools and something else beside them.
        await Assert.That(list.FirstDifference(Encoding.UTF8.GetBytes(Two[..^1] + ",\"nextCursor\":\"x\"}")))
            .IsEqualTo("the list around the tools, which hold the same bytes");

        // An answer that is no list at all.
        await Assert.That(list.FirstDifference("{\"content\":[]}"u8)).IsEqualTo("its answer, which holds no list of tools");
    }

    private const string Two =
        """{"tools":[{"name":"first","description":"a","inputSchema":{"type":"object"}},{"name":"second","description":"b","inputSchema":{"type":"object"}}]}""";

    private static string SnapshotPath { get; } = Path.Combine(RepositoryLayout.Root.FullName, "upstream-snapshots", "tools-list.json");

    private static byte[] Resource(System.Reflection.Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The build embedded no resource named '{name}'.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static List<string?> Names(JsonObject result) =>
        [.. (result["tools"]?.AsArray() ?? []).Select(tool => (string?)tool?["name"])];
}
