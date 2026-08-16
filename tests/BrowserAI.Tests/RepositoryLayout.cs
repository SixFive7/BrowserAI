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
