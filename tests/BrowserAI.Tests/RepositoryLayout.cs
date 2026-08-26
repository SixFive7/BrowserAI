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
    /// Directory names that never hold this repository's own hand-written files,
    /// wherever in the tree they appear.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pruned during the walk rather than filtered after it: <c>payload\</c>
    /// carries an unpacked <c>node_modules</c>, so enumerating first and
    /// discarding second means reading tens of thousands of paths to keep none
    /// of them. <c>payload\</c> earns its place in this list rather than in
    /// <see cref="NotOursAtTheRoot"/> because there are two of them — the
    /// unpacked one at the root and the vendored npm tree under <c>build\</c>.
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
    private static readonly string[] NotOursAnywhere =
        [".git", ".vs", ".work", "payload", "bin", "obj", "node_modules"];

    /// <summary>
    /// Build output that exists at the repository root and only there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Split out of the single list on 2026-08-18, and it was hiding five
    /// product source files.</b> The prune matched a directory <i>name</i> at
    /// any depth, case-insensitively — so <c>src\BrowserAI\Artifacts\</c>, which
    /// is where the artifact router lives, matched the root's <c>artifacts\</c>
    /// build-output directory and was pruned out of <b>every</b> scan built on
    /// <see cref="LinkBearingFiles"/>: the link check, the fragment check and
    /// the SPDX house rule alike. Five files, silently outside three tests.
    /// </para>
    /// <para>
    /// <b>It was found by a new check rather than by review</b> — the fragment
    /// scan counted 552 where a script counting the same corpus outside the
    /// suite counted 554, and the two missing entries were both in
    /// <c>Artifacts\</c>. A prune that removes files reports nothing when it
    /// removes the wrong ones, which is why the counts are asserted at all.
    /// </para>
    /// </remarks>
    private static readonly string[] NotOursAtTheRoot =
        ["artifacts", "Releases", "TestResults"];

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
    /// Every file this repository vendors from somebody else and compiles into
    /// the product.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not <c>.cs</c>, and that is the whole reason this exists.</b>
    /// <c>third-party/sqlite</c> holds the SQLite amalgamation, which
    /// <c>build/Sqlite.targets</c> compiles with <c>cl.exe</c> and ILC links
    /// into <c>BrowserAI.exe</c>. It is product source by every meaning that
    /// matters and it is invisible to a scan anchored on an extension —
    /// measured 2026-08-26: a swapped amalgamation left the published binary
    /// reading as fresh, so a suite arm driving that binary would have been
    /// asserting about SQLite nobody had compiled.
    /// </para>
    /// <para>
    /// <b>Enumerated rather than named.</b> A list of two paths would go stale
    /// the day a third file is vendored, and the failure would be the same
    /// silent one: something compiled into the binary that nothing watches.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<FileInfo> VendoredSourceFiles { get; } = SourceFilesUnder(["third-party"], ["*"]);

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
    /// ⚠️ <b>Corrected 2026-08-24 (previously "Verified 2026-08-17: this yields
    /// the same 215 files as <c>git ls-files *.cs *.ps1 *.psm1 *.mjs *.js
    /// *.md</c>, with no difference in either direction").</b> That sentence was
    /// true when it was written, was never checked again, and was <b>false by
    /// 520 files</b> for as long as agent worktrees existed under
    /// <c>.claude\</c> — which this walk does not prune, because
    /// <c>settings.json</c> and <c>hooks\</c> are committed and pruning would
    /// lose the SPDX and link coverage of both. Every tree-as-text scan read a
    /// second checkout as repository content: the fragment scan counted
    /// <b>2,378</b> against a real <b>797</b>, and three gate arms went red for a
    /// reason no message named.
    /// </para>
    /// <para>
    /// <b>It is a mechanism now rather than a remark:</b>
    /// <see cref="HouseRuleTests.TheScannedCorpusIsExactlyWhatGitSaysTheRepositoryHolds"/>
    /// compares this list against <c>git ls-files</c> on every run, in both
    /// directions, and skips loudly when git is absent. No count is quoted here
    /// any more — a number in a remark is the thing that went stale, and the
    /// comparison does not need one.
    /// </para>
    /// <para>
    /// It is still not <c>git ls-files</c> itself, and that has not changed: the
    /// suite must run on an export with no git in it. Git is the oracle, never
    /// the source of truth.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<FileInfo> LinkBearingFiles { get; } =
    [
        .. Walk(Root, atRoot: true)
            .Where(file => IsLinkBearing(file.Name))
            .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>
    /// Whether a path names one of the file kinds
    /// <see cref="LinkBearingFiles"/> is made of.
    /// </summary>
    /// <remarks>
    /// <b>Public so that the walk and the thing that checks the walk ask the
    /// same question.</b> A second copy of this list on the git side of that
    /// comparison would eventually answer differently, and the divergence it
    /// reported would be its own — which is exactly the trap
    /// <c>RecordedCountTests</c> exists to avoid one layer up.
    /// </remarks>
    /// <param name="path">A file name or path.</param>
    /// <returns>Whether it carries prose this repository scans.</returns>
    public static bool IsLinkBearing(string path) =>
        Path.GetExtension(path) is ".cs" or ".ps1" or ".psm1" or ".mjs" or ".js" or ".md";

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

    /// <summary>
    /// Every file beneath a directory, skipping the trees in
    /// <see cref="NotOursAnywhere"/> and, at the root, those in
    /// <see cref="NotOursAtTheRoot"/>.
    /// </summary>
    /// <param name="directory">The directory to walk.</param>
    /// <param name="atRoot">Whether this is the repository root itself.</param>
    /// <returns>The files, in whatever order the file system yields them.</returns>
    private static IEnumerable<FileInfo> Walk(DirectoryInfo directory, bool atRoot)
    {
        foreach (var file in directory.EnumerateFiles())
        {
            yield return file;
        }

        foreach (var child in directory.EnumerateDirectories())
        {
            if (NotOursAnywhere.Contains(child.Name, StringComparer.OrdinalIgnoreCase)
                || (atRoot && NotOursAtTheRoot.Contains(child.Name, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            foreach (var file in Walk(child, atRoot: false))
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
