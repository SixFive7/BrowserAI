// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Sessions;

namespace BrowserAI.Artifacts;

/// <summary>
/// What a <c>filename</c> argument is allowed to be, decided on the string
/// alone.
/// </summary>
/// <remarks>
/// <para>
/// <b>Refusals are refusals, never normalisations.</b> <c>..\..\foo.png</c>,
/// <c>C:\foo.png</c>, <c>C:foo.png</c>, <c>\\server\share\foo.png</c> and
/// <c>\foo.png</c> are each refused with a sentence naming the shape and the
/// fix. Silently collapsing any of them produces a path that happens to land
/// somewhere, and a caller that believes its file went where it asked.
/// </para>
/// <para>
/// ⚠️ <b>Every check here is on the string, and none of them touches the
/// filesystem.</b> That is not a performance preference: a <c>\\host\share</c>
/// naming a host that is not answering blocks a single file call for <b>21
/// seconds</b> (measured; [kb](../../../kb/windows/detection.md#enumeration-works--and-it-moves-the-safety-boundary)), so a validator
/// that probed before deciding would hand any caller a 21-second stall for the
/// price of one bad argument.
/// </para>
/// <para>
/// <b>Windows rewrites some names rather than rejecting them</b>, and a silent
/// rewrite is worse than a refusal. A trailing space or dot on a segment is
/// stripped, and a legacy device name — <c>NUL</c>, <c>CON</c>, <c>COM1</c> —
/// opens the device whatever extension follows it, so <c>NUL.png</c> is a
/// screenshot that reports success and writes nothing. Both are refused here.
/// </para>
/// </remarks>
internal static class ArtifactFilename
{
    /// <summary>
    /// The legacy DOS device names, which resolve to a device regardless of the
    /// extension that follows them.
    /// </summary>
    private static readonly string[] ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>
    /// Checks one <c>filename</c> argument and returns it as a relative path
    /// with one separator.
    /// </summary>
    /// <param name="tool">The tool that was called, for the refusal.</param>
    /// <param name="value">The argument, as it arrived.</param>
    /// <returns>The same name, with separators normalised and empty segments dropped.</returns>
    /// <exception cref="SessionToolException">The argument names a place a session may not reach.</exception>
    public static string Relative(string tool, string value)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(value);

        if (Shape(value) is { } shape)
        {
            throw new SessionToolException(SessionErrors.FilenameNotWithinSession(tool, value, shape));
        }

        var segments = value.Split(['\\', '/'], StringSplitOptions.None);
        var kept = new List<string>(segments.Length);

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];

            if (segment is "..")
            {
                throw new SessionToolException(SessionErrors.FilenameEscapesTheSession(tool, value));
            }

            if (segment.Length is 0 || segment is ".")
            {
                // A doubled separator or a `.` segment names the same place and
                // is dropped -- but a trailing one means the argument named a
                // directory, which is caught below.
                if (index == segments.Length - 1)
                {
                    throw new SessionToolException(SessionErrors.FilenameNotUsable(
                        tool,
                        value,
                        "it ends with a separator, so it names a directory rather than a file."));
                }

                continue;
            }

            if (Unusable(segment) is { } why)
            {
                throw new SessionToolException(SessionErrors.FilenameNotUsable(tool, value, why));
            }

            kept.Add(segment);
        }

        return kept.Count is 0
            ? throw new SessionToolException(SessionErrors.FilenameNotUsable(tool, value, "it names no file at all."))
            : string.Join(Path.DirectorySeparatorChar, kept);
    }

    /// <summary>
    /// Whether the whole path is inside a root, decided after combining.
    /// </summary>
    /// <remarks>
    /// The belt to <see cref="Relative"/>'s braces: the segment rules already
    /// refuse every escape this repository knows how to write, and this is the
    /// check that does not depend on knowing them all.
    /// </remarks>
    /// <param name="root">The directory nothing may leave. Absolute and canonical.</param>
    /// <param name="candidate">The combined path.</param>
    /// <returns><see langword="true"/> when <paramref name="candidate"/> is beneath <paramref name="root"/>.</returns>
    public static bool IsInside(string root, string candidate)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(candidate);

        var bounded = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return candidate.StartsWith(bounded, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Which absolute or drive-relative shape a name has, or
    /// <see langword="null"/> when it is an ordinary relative name.
    /// </summary>
    /// <remarks>
    /// Named rather than collapsed into one message: <c>C:foo.png</c> is the one
    /// most likely to be mishandled, because it looks absolute, is <i>rooted</i>
    /// by every API that asks, and resolves against whatever the process's
    /// current directory on drive C happens to be.
    /// </remarks>
    private static string? Shape(string value)
    {
        if (value.Length is 0 || value.AsSpan().Trim().Length is 0)
        {
            return "it is empty";
        }

        if (value.StartsWith(@"\\?\", StringComparison.Ordinal) || value.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return "it is a Win32 device path, which reaches past every check the filesystem would otherwise apply";
        }

        if (value.StartsWith(@"\\", StringComparison.Ordinal) || value.StartsWith("//", StringComparison.Ordinal))
        {
            return "it is a UNC path naming another machine";
        }

        if (value.Length >= 2 && value[1] is ':')
        {
            return value.Length >= 3 && value[2] is '\\' or '/'
                ? "it is an absolute path naming a drive"
                : "it is a drive-relative path, which resolves against whatever directory this process last used on that drive rather than against anything you named";
        }

        return value[0] is '\\' or '/'
            ? "it is rooted, so it names a place at the top of a drive rather than inside the session"
            : null;
    }

    /// <summary>Why one segment cannot be part of a file name, if it cannot.</summary>
    private static string? Unusable(string segment)
    {
        foreach (var character in segment)
        {
            if (Array.IndexOf(Path.GetInvalidFileNameChars(), character) >= 0)
            {
                return char.IsControl(character)
                    ? $"'{segment}' contains a control character (U+{(int)character:X4}), which no file name may."
                    : $"'{segment}' contains '{character}', which Windows does not allow in a file name.";
            }
        }

        if (segment[^1] is ' ' or '.')
        {
            return $"'{segment}' ends with a space or a dot, which Windows silently strips — so the file would not have the name you asked for.";
        }

        var stem = Path.GetFileNameWithoutExtension(segment);

        return Array.Exists(ReservedNames, name => string.Equals(name, stem, StringComparison.OrdinalIgnoreCase))
            ? $"'{segment}' is the reserved device name '{stem}', which opens a device rather than creating a file whatever extension follows it."
            : null;
    }
}
