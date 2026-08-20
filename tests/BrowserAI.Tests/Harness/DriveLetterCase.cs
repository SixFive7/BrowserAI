// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Which way a test spells the drive letter of a path it composes.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>This exists because the alternative was choosing a shell.</b> A test
/// host started from PowerShell walks up from an <c>AppContext.BaseDirectory</c>
/// spelled <c>C:\…</c>; the same host started from Git Bash gets <c>c:\…</c>.
/// Windows itself always answers <b>upper-case</b> — <c>GetFinalPathNameByHandleW</c>,
/// <c>QueryFullProcessImageNameW</c> and the mount manager underneath them all
/// report the letter that way. So an assertion comparing a path a test composed
/// against a path the OS re-spelled is green from one shell and red from the
/// other, <i>with no change to the product at all</i>.
/// </para>
/// <para>
/// <b>A single-shell run cannot see it, whoever runs it.</b> The hosted CI this
/// project had between 2026-08-18 and 2026-08-20 ran every step under
/// <c>pwsh</c>, so it picked the casing that happens to agree and baked it in —
/// which is why the defect was reported twice from a machine and never once from
/// a build. A test parameterised over both values below is red on the wrong
/// comparison <i>whatever</i> started it. That property is now load-bearing
/// rather than a bonus: with CI removed, the release gate is the suite run on the
/// maintainer's machine, and <b>the standing instruction is to run it from
/// PowerShell and from Git Bash</b> — see
/// [the release gate](../../../RELEASING.md#the-release-gate). This type is what
/// makes a single-shell run catch the defect anyway.
/// </para>
/// <para>
/// The right comparison is case-insensitive, and that is not a loosening: two
/// spellings of a Windows path differing only in case are the same directory, a
/// claim this product makes for itself in <c>SessionPath.Key</c>. What this type
/// defends is the <i>assertion</i>, which has no such canonicalisation and gets
/// whatever the caller's shell handed it.
/// </para>
/// </remarks>
internal enum DriveLetterCase
{
    /// <summary>
    /// <c>C:\…</c> — the spelling Windows itself hands back, so a path composed
    /// this way matches an OS-read one byte for byte.
    /// </summary>
    Upper,

    /// <summary>
    /// <c>c:\…</c> — the spelling Git Bash hands the test host, and the one no
    /// Windows API ever returns. A composed path spelled this way <b>never</b>
    /// matches an OS-read one ordinally, which is what makes the wrong
    /// comparison fail on every machine rather than on some of them.
    /// </summary>
    Lower,
}

/// <summary>
/// Spelling a composed path's drive letter a chosen way.
/// </summary>
internal static class DriveLetterCases
{
    /// <summary>
    /// The path with its drive letter spelled this way, and every other
    /// character untouched.
    /// </summary>
    /// <remarks>
    /// A path that does not begin with a drive letter is returned unchanged:
    /// there is nothing to re-spell, and refusing would make the caller ask a
    /// question it does not need the answer to.
    /// </remarks>
    /// <param name="casing">Which spelling to produce.</param>
    /// <param name="path">A rooted local path.</param>
    /// <returns>The same path, drive letter re-spelled.</returns>
    public static string Spell(this DriveLetterCase casing, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (path is not { Length: >= 2 } || !char.IsAsciiLetter(path[0]) || path[1] is not ':')
        {
            return path;
        }

        var letter = casing is DriveLetterCase.Upper
            ? char.ToUpperInvariant(path[0])
            : char.ToLowerInvariant(path[0]);

        return letter == path[0] ? path : string.Concat(letter.ToString(), path.AsSpan(1));
    }
}
