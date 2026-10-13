// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The repository files a static initialiser of the harness reads, compiled into the
/// suite by its project file.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-10-11 with the maintainer's decision 63</b>, in his words verbatim:
/// <i>"convert disk writes as well. Some users have slow spinning rust."</i> A static
/// initialiser cannot await, and each of these files is a build input the build already
/// reads: <c>upstream-snapshots/browsers.json</c> is regenerated and diffed by
/// <c>build/UpstreamSnapshots.targets</c> on every build, <c>tool-verdicts.json</c> is
/// compiled into the product, and <c>build/New-Release.ps1</c> is the release layout the
/// installer arms are built against. So the suite carries the copy its build saw, as
/// the product carries its own, and a read of one is a copy of memory.
/// <i>Previously each was read from the working tree at first use.</i>
/// </para>
/// <para>
/// <b>A run with <c>--no-build</c> reads the copy of its last build</b>, which is the
/// one limit this adds; every gate in this repository builds first. A test whose
/// subject is one of these files reads the working tree's own, in its body.
/// </para>
/// </remarks>
internal static class RepositoryCopy
{
    /// <summary>The committed snapshot of the payload's <c>browsers.json</c>.</summary>
    public const string BrowsersSnapshot = "upstream-snapshots/browsers.json";

    /// <summary>The release script, whose variables are the release layout.</summary>
    public const string ReleaseScript = "build/New-Release.ps1";

    /// <summary>The committed tool verdicts.</summary>
    public const string ToolVerdicts = "tool-verdicts.json";

    /// <summary>Where the product's project files are, and the prefix their copies are compiled in under.</summary>
    public const string ProductProjectPrefix = "src/";

    /// <summary>
    /// Every product project file the suite carries: its path from the repository's root, with
    /// forward slashes, and the name its copy is compiled in under.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-13 with the maintainer's 147 b, in his words verbatim: <i>"147 b"</i></b>:
    /// <see cref="PublishedSlice.CompiledInFiles"/> reads the files each project compiles into the
    /// binary out of these copies. The project file compiles them in with a glob, so a project
    /// added under <c>src</c> is carried with the rest, and the names are read back from the
    /// assembly and not typed here; MSBuild spells the folder part of a glob's name with a
    /// backslash, which the path turns into a slash.
    /// </remarks>
    /// <returns>The projects, ordered by path.</returns>
    public static IReadOnlyList<(string Path, string Resource)> ProductProjects() =>
    [
        .. typeof(RepositoryCopy).Assembly.GetManifestResourceNames()
            .Select(static resource => (Path: resource.Replace('\\', '/'), Resource: resource))
            .Where(static project => project.Path.StartsWith(ProductProjectPrefix, StringComparison.Ordinal)
                && project.Path.EndsWith(".csproj", StringComparison.Ordinal))
            .OrderBy(static project => project.Path, StringComparer.Ordinal),
    ];

    /// <summary>The bytes of one of the files, as the build compiled them in.</summary>
    /// <param name="relativePath">The file, one of this type's constants.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="InvalidOperationException">The suite was built without it.</exception>
    public static byte[] Bytes(string relativePath) =>
        EmbeddedResource.BytesOf(typeof(RepositoryCopy).Assembly, relativePath)
        ?? throw new InvalidOperationException(
            $"The suite was built without its copy of '{relativePath}', which BrowserAI.Tests.csproj compiles in.");

    /// <summary>The text of one of the files, as <c>File.ReadAllText</c> reads it.</summary>
    /// <param name="relativePath">The file, one of this type's constants.</param>
    /// <returns>The text, a UTF-8 byte-order mark left out.</returns>
    public static string Text(string relativePath)
    {
        var bytes = Bytes(relativePath);
        var start = bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0;

        return System.Text.Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }
}
