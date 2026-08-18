// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The two aliases a path can carry inside a volume: a junction, and an 8.3
/// short name.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both are real and neither needs a privilege.</b> That is what lets the
/// tests that use them assert on the product rather than on a stand-in — a
/// predicate checked only against strings a test invented is a predicate checked
/// against its author's idea of an alias.
/// </para>
/// <para>
/// The drive-letter aliases — <c>subst</c> and a mapped network drive — are
/// <see cref="DosDeviceAlias"/>'s, because they are made by a different
/// mechanism and have to be undone.
/// </para>
/// </remarks>
internal static partial class PathAliases
{
    /// <summary>
    /// Creates a directory junction, which the BCL has no API for.
    /// </summary>
    /// <remarks>
    /// <c>cmd /c mklink /J</c> rather than <c>CreateSymbolicLink</c>: a junction
    /// needs no privilege, so this works for an ordinary user and in CI, and it
    /// is the link kind actually found inside a relocated browser profile.
    /// </remarks>
    /// <param name="link">The link to create.</param>
    /// <param name="target">What it points at.</param>
    /// <returns>The awaitable creation.</returns>
    public static async Task JunctionAsync(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            ["/c", "mklink", "/J", link, target])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(link)!,
        })!;

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode is not 0 || !Directory.Exists(link))
        {
            throw new InvalidOperationException(
                $"mklink /J could not create '{link}' -> '{target}'. This test proves nothing without one. {output} {error}");
        }
    }

    /// <summary>
    /// The 8.3 spelling the filesystem itself keeps for a path, as
    /// <c>C:\PROGRA~1</c> is for <c>C:\Program Files</c>.
    /// </summary>
    /// <remarks>
    /// <b>Read out of the filesystem rather than constructed</b>, because 8.3
    /// generation is a per-volume setting: the name is whatever this volume
    /// assigned, and a hand-written <c>~1</c> would be a string that merely looks
    /// like one. A volume with 8.3 generation disabled answers with the long path
    /// unchanged, which is a fact a caller has to notice rather than something to
    /// paper over — so the answer is returned as it comes and the assertion that
    /// it differs belongs to the test.
    /// </remarks>
    /// <param name="path">An existing path.</param>
    /// <returns>The short spelling, or the path unchanged when there is none.</returns>
    public static unsafe string ShortNameOf(string path)
    {
        const int Capacity = 1024;
        var buffer = stackalloc char[Capacity];
        var written = GetShortPathNameW(path, buffer, Capacity);

        return written is 0 or >= Capacity ? path : new string(buffer, 0, (int)written);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial uint GetShortPathNameW(string lpszLongPath, char* lpszShortPath, uint cchBuffer);
}
