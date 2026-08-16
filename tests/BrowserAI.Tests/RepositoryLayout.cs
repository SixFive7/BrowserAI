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
    /// Every file that can influence how the compiler treats a diagnostic:
    /// the project files plus the two shared property files at the root.
    /// </summary>
    public static IReadOnlyList<FileInfo> BuildFiles { get; } =
    [
        .. ProjectFiles,
        .. new[] { "Directory.Build.props", "Directory.Packages.props" }
            .Select(name => new FileInfo(Path.Combine(Root.FullName, name)))
            .Where(file => file.Exists),
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
