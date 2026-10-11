// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The two shells the gate drivers run under, each by its absolute path.
/// </summary>
/// <remarks>
/// <para>
/// <b>Absolute, because <see cref="JobObjectScope"/> resolves nothing.</b> It starts a
/// child through <c>JobLauncher.Start</c>, which hands <c>CreateProcessW</c> the
/// application name it is given, so a bare <c>pwsh</c> is refused there and never looked
/// for on the search path.
/// </para>
/// <para>
/// <b>Git Bash is found beside the git on the search path</b>, at the layout Git for
/// Windows installs: <c>cmd\git.exe</c> or <c>mingw64\bin\git.exe</c> under the install
/// root, and <c>bin\bash.exe</c> under the same root. A git with no bash beside it, which
/// MinGit is, answers <see langword="null"/> and the caller says so.
/// </para>
/// </remarks>
internal static class Shells
{
    /// <summary>The PowerShell the drivers run under, or <see langword="null"/>.</summary>
    public static string? PowerShell { get; } = OnSearchPath("pwsh.exe");

    /// <summary>The Git Bash the drivers run under, or <see langword="null"/>.</summary>
    public static string? GitBash { get; } = BashBeside(OnSearchPath("git.exe"));

    /// <summary>The first file of that name in a directory of the search path.</summary>
    /// <param name="file">The file name, extension included.</param>
    /// <returns>Its full path, or <see langword="null"/>.</returns>
    private static string? OnSearchPath(string file) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Path.IsPathFullyQualified)
            .Select(directory => Path.Combine(directory, file))
            .FirstOrDefault(File.Exists);

    /// <summary>Git for Windows' own bash, found from the git it installed.</summary>
    /// <param name="git">The git on the search path, or <see langword="null"/>.</param>
    /// <returns>The bash, or <see langword="null"/>.</returns>
    private static string? BashBeside(string? git)
    {
        // Three levels: the directory git is in, its parent and the one above, which
        // reaches the install root from either of the two places git is installed.
        var directory = git is null ? null : Path.GetDirectoryName(git);

        for (var level = 0; level < 3 && directory is not null; level++, directory = Path.GetDirectoryName(directory))
        {
            var bash = Path.Combine(directory, "bin", "bash.exe");

            if (File.Exists(bash))
            {
                return bash;
            }
        }

        return null;
    }
}
