// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Tests;

/// <summary>
/// Locates the repository root from the running test assembly, for the tests
/// that assert things about the build configuration itself.
/// </summary>
/// <remarks>
/// Anchored on <c>Directory.Packages.props</c> rather than on <c>.git</c>: a
/// worktree carries a <c>.git</c> file rather than a directory, and the tests
/// that use this are about the build configuration, so the file that declares
/// it is the honest anchor.
/// </remarks>
internal static class RepositoryLayout
{
    private const string RootMarker = "Directory.Packages.props";

    /// <summary>
    /// Directory names that never hold this repository's own hand-written files.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pruned during the walk rather than filtered after it: <c>payload\</c>
    /// carries an unpacked <c>node_modules</c>, so enumerating first and
    /// discarding second means reading tens of thousands of paths to keep none
    /// of them.
    /// </para>
    /// <para>
    /// <b>Declared above every member that reads it, and that is load-bearing
    /// rather than tidy.</b> Static field initializers run in textual order, so
    /// this list sitting below <see cref="LinkBearingFiles"/> made it empty at
    /// the moment that walk ran — the prune silently did nothing and the scan
    /// swept in the whole gitignored <c>.work\</c> tree. Observed 2026-08-17
    /// while this was being written; it fails open, which is why it is written
    /// down here rather than left to whoever moves it next.
    /// </para>
    /// </remarks>
    private static readonly string[] NotOurs =
        [".git", ".vs", ".work", "payload", "artifacts", "Releases", "TestResults", "bin", "obj", "node_modules"];

    /// <summary>The repository root directory.</summary>
    public static DirectoryInfo Root { get; } = FindRoot();

    /// <summary>Every project file that ships or proves the product.</summary>
    public static IReadOnlyList<FileInfo> ProjectFiles { get; } =
    [
        .. new[] { "src", "tests" }
            .Select(name => new DirectoryInfo(Path.Combine(Root.FullName, name)))
            .Where(directory => directory.Exists)
            .SelectMany(directory => directory.EnumerateFiles("*.csproj", SearchOption.AllDirectories))
            .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>
    /// The build files that reach <b>every</b> project by construction, whether
    /// or not that project asks for them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the subset the "never repo-wide" rules are about</b>
    /// ([TESTING.md](../../TESTING.md#what-the-build-itself-must-fail-on)). A
    /// property set here applies to the assembly nobody has written yet, which
    /// is precisely why AOT and trim suppression must not be set here — and why
    /// <c>TreatWarningsAsErrors</c> must.
    /// </para>
    /// <para>
    /// <b><c>Directory.Build.targets</c> is named although it does not exist.</b>
    /// A scan that enumerates only the files present cannot fail when a new
    /// repo-wide file arrives carrying the thing it forbids, which is the shape
    /// of hole the plan's final audit found twice.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<FileInfo> RepositoryWideBuildFiles { get; } =
    [
        .. new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" }
            .Select(name => new FileInfo(Path.Combine(Root.FullName, name)))
            .Where(file => file.Exists),
    ];

    /// <summary>
    /// Every file that can influence how the compiler treats a diagnostic: the
    /// project files, the shared property files at the root, and the importable
    /// MSBuild fragments under <c>build/</c>.
    /// </summary>
    /// <remarks>
    /// The <c>build/</c> half was added 2026-08-16. <c>UpstreamSnapshots.targets</c>
    /// is imported by the test project and sets properties; a <c>NoWarn</c>
    /// placed there was invisible to every scan the suite made, which is the
    /// same hole one directory across.
    /// </remarks>
    public static IReadOnlyList<FileInfo> BuildFiles { get; } =
    [
        .. ProjectFiles,
        .. RepositoryWideBuildFiles,
        .. new[] { "*.props", "*.targets" }
            .SelectMany(pattern => new DirectoryInfo(Path.Combine(Root.FullName, "build")) is { Exists: true } build
                ? build.EnumerateFiles(pattern, SearchOption.AllDirectories)
                : [])
            .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>Every C# file the product ships.</summary>
    public static IReadOnlyList<FileInfo> ProductSourceFiles { get; } = SourceFilesUnder(["src"], ["*.cs"]);

    /// <summary>
    /// Every hand-written source and script file in the repository, product and
    /// suite alike.
    /// </summary>
    /// <remarks>
    /// This is what the never-by-image-name scan reads, and its breadth is the
    /// point: the rule has no exception for test code or for a build script,
    /// and a kill-by-image-name command line inside a <c>.ps1</c> is exactly
    /// the shape an analyzer cannot see.
    /// </remarks>
    public static IReadOnlyList<FileInfo> SourceAndScriptFiles { get; } =
        SourceFilesUnder(["src", "tests", "build"], ["*.cs", "*.ps1", "*.psm1", "*.mjs", "*.js"]);

    /// <summary>
    /// Every hand-written file in the repository that can carry a Markdown link:
    /// the prose, the scripts, and the code whose XML doc comments are prose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Enumerated over the whole tree rather than over a list of directory
    /// names, and that is the point.</b> A documentation restructure moves files
    /// between directories; a scan anchored on <c>src</c>, <c>tests</c> and
    /// <c>build</c> would stop seeing a file the day it moved — silently, and in
    /// exactly the change it exists to guard. What is pruned is what is not this
    /// repository's own hand-written text: version control, agent scratch, the
    /// bundled payload, and build output under every name it takes here.
    /// </para>
    /// <para>
    /// Verified 2026-08-17: this yields the same 215 files as
    /// <c>git ls-files *.cs *.ps1 *.psm1 *.mjs *.js *.md</c>, with no difference
    /// in either direction. It is not <c>git ls-files</c> itself because the
    /// suite must run on an export with no git in it.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<FileInfo> LinkBearingFiles { get; } =
    [
        .. Walk(Root)
            .Where(file => file.Extension is ".cs" or ".ps1" or ".psm1" or ".mjs" or ".js" or ".md")
            .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>
    /// A file's text with whole-line comments removed, so that a scan for a
    /// forbidden construct reads code rather than prose about it.
    /// </summary>
    /// <remarks>
    /// <b>Writing down why a rule exists must not violate the rule.</b> Without
    /// this, the paragraph explaining that <c>AssignProcessToJobObject</c> is
    /// the wrong mechanism is itself an occurrence of it, and the only way out
    /// would be to stop explaining — which is the worse of the two trades. It
    /// is line-based on purpose: a needle sitting at the end of a line of real
    /// code is left visible, because that is where a suppression comment beside
    /// a violation would be.
    /// </remarks>
    /// <param name="file">The file to read.</param>
    /// <returns>The file's text with comment-only lines blanked.</returns>
    public static async Task<string> ReadCodeAsync(FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var lines = await File.ReadAllLinesAsync(file.FullName);

        return string.Join(
            '\n',
            lines.Where(line =>
            {
                var trimmed = line.TrimStart();

                // C#, PowerShell and JavaScript comment openers, plus the
                // continuation and closing lines of a block comment.
                return !trimmed.StartsWith("//", StringComparison.Ordinal)
                    && !trimmed.StartsWith("/*", StringComparison.Ordinal)
                    && !trimmed.StartsWith('*')
                    && !trimmed.StartsWith('#');
            }));
    }

    /// <summary>Every hand-written file of the given kinds beneath the given directories.</summary>
    /// <param name="directories">Repository-relative directory names.</param>
    /// <param name="patterns">File patterns, such as <c>*.cs</c>.</param>
    /// <returns>The files, build output excluded, in path order.</returns>
    public static IReadOnlyList<FileInfo> SourceFilesUnder(string[] directories, string[] patterns) =>
    [
        .. directories
            .Select(name => new DirectoryInfo(Path.Combine(Root.FullName, name)))
            .Where(directory => directory.Exists)
            .SelectMany(directory => patterns.SelectMany(pattern => directory.EnumerateFiles(pattern, SearchOption.AllDirectories)))
            // Build output is not source. A generated file under obj\ carries
            // whatever a source generator emitted and asserting on it would
            // make the scan depend on the last build's configuration.
            .Where(file => !Path.GetRelativePath(Root.FullName, file.FullName)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj" or "node_modules"))
            .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>Every file beneath a directory, skipping the trees in <see cref="NotOurs"/>.</summary>
    /// <param name="directory">The directory to walk.</param>
    /// <returns>The files, in whatever order the file system yields them.</returns>
    private static IEnumerable<FileInfo> Walk(DirectoryInfo directory)
    {
        foreach (var file in directory.EnumerateFiles())
        {
            yield return file;
        }

        foreach (var child in directory.EnumerateDirectories())
        {
            if (NotOurs.Contains(child.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var file in Walk(child))
            {
                yield return file;
            }
        }
    }

    private static DirectoryInfo FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, RootMarker)))
            {
                return directory;
            }
        }

        throw new InvalidOperationException(
            $"No '{RootMarker}' found in any ancestor of '{AppContext.BaseDirectory}', so the repository root cannot be located.");
    }
}
