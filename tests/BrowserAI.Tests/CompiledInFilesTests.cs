// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// Every file a product project compiles into the binary is an input of the published slice's
/// freshness, so a publish older than one of them is stale.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's 147 b, 2026-10-13, in his words verbatim: <i>"147 b"</i></b>: the
/// freshness inputs are derived from the projects' <c>EmbeddedResource</c> items. Since 2026-10-08
/// the binary compiles in <c>CHANGELOG.md</c>, <c>upstream-snapshots/tools-list.json</c> and
/// <c>tool-verdicts.json</c>, and until 2026-10-13 none of them was an input, so an edit to one
/// after a publish left every slice arm driving a binary that did not carry it, reading as fresh.
/// </para>
/// <para>
/// <b>Asserted over the corpus and not over the check</b>, for the reason
/// <see cref="SqliteTests.TheFreshnessCheckWatchesTheVendoredAmalgamation"/> gives: a staleness
/// check is silent by construction about what it never looked at. The items are read here from
/// the working tree's own project files, in the arm's body, and the inputs from the copies the
/// suite carries, so the two halves are two readings.
/// </para>
/// </remarks>
internal sealed class CompiledInFilesTests
{
    /// <summary>
    /// Every file a product project's <c>EmbeddedResource</c> items name is a freshness input and is
    /// there; the suite carries a copy of every product project; and the reading resolves the
    /// shapes this tree writes and refuses every other.
    /// </summary>
    /// <remarks>
    /// <b>Watched red on 2026-10-13</b> against the tree with <c>FreshnessInputs</c> not reading
    /// <c>CompiledInFiles</c>, in a filtered run: <i>"Expected to be empty ... but received
    /// "CHANGELOG.md, upstream-snapshots/tools-list.json, tool-verdicts.json""</i>; green as
    /// committed.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheFreshnessCheckWatchesEveryFileAProductProjectCompilesIn()
    {
        var projects = RepositoryLayout.ProjectFiles
            .Select(static file => Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName).Replace('\\', '/'))
            .Where(static path => path.StartsWith(RepositoryCopy.ProductProjectPrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        await Assert.That(projects.Count).IsGreaterThanOrEqualTo(3).Because("the positive control: the tree holds the product's three projects");
        await Assert.That(string.Join(", ", RepositoryCopy.ProductProjects().Select(static project => project.Path)))
            .IsEqualTo(string.Join(", ", projects))
            .Because("the freshness inputs are read from the copies of the product projects the suite carries, so it must carry every one");

        var named = new List<string>();

        foreach (var project in projects)
        {
            var (paths, problems) = PublishedSlice.CompiledInBy(project, await File.ReadAllTextAsync(Path.Combine(RepositoryLayout.Root.FullName, project)));

            await Assert.That(string.Join(Environment.NewLine, problems)).IsEmpty();
            named.AddRange(paths);
        }

        // The positive control: the three the binary has compiled in since 2026-10-08.
        await Assert.That(named).Contains("CHANGELOG.md");
        await Assert.That(named).Contains("upstream-snapshots/tools-list.json");
        await Assert.That(named).Contains("tool-verdicts.json");

        var watched = PublishedSlice.FreshnessInputs
            .Select(static file => Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName).Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await Assert.That(string.Join(", ", named.Where(path => !watched.Contains(path))))
            .IsEmpty()
            .Because("a file the binary compiles in and the freshness check does not watch leaves a publish older than it reading as fresh");
        await Assert.That(string.Join(Environment.NewLine, PublishedSlice.CompiledInProblems)).IsEmpty();

        foreach (var path in named)
        {
            await Assert.That(File.Exists(Path.Combine(RepositoryLayout.Root.FullName, path)))
                .IsTrue()
                .Because($"{path}: an input that is not there never reads as newer than the binary");
        }

        // The reading over project text it is handed: each shape it resolves, and each it refuses.
        var (resolved, refused) = PublishedSlice.CompiledInBy(
            "src/Example/Example.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <EmbeddedResource Include="..\..\CHANGELOG.md" LogicalName="A" />
                <EmbeddedResource Include="$(MSBuildThisFileDirectory)..\..\tool-verdicts.json" LogicalName="B" />
                <EmbeddedResource Include="Assets\page.css;Assets\page.js" />
                <EmbeddedResource Update="Strings.resx" />
                <EmbeddedResource Include="..\..\docs\*.md" />
                <EmbeddedResource Include="$(SolutionDir)x.json" />
                <EmbeddedResource Include="..\..\..\outside.txt" />
              </ItemGroup>
            </Project>
            """);

        await Assert.That(string.Join("|", resolved)).IsEqualTo("CHANGELOG.md|tool-verdicts.json|src/Example/Assets/page.css|src/Example/Assets/page.js");
        await Assert.That(refused.Count).IsEqualTo(3).Because(string.Join(Environment.NewLine, refused));
        await Assert.That(refused[0]).Contains("docs\\*.md");
        await Assert.That(refused[1]).Contains("$(SolutionDir)");
        await Assert.That(refused[2]).Contains("outside the repository");
    }
}
