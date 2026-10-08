// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;

namespace BrowserAI.Updates;

/// <summary>What the record of the last update check said when it was read.</summary>
/// <param name="At">When the last check started, or <see langword="null"/> when there is no usable record.</param>
/// <param name="Unreadable">Why a record that exists could not be used, or <see langword="null"/>.</param>
internal readonly record struct UpdateCheckStampReading(DateTimeOffset? At, string? Unreadable);

/// <summary>Where the background keeps the time of its last update check.</summary>
/// <remarks>
/// <b>D9, decided 2026-10-08 by the maintainer, in his words verbatim:</b>
/// <i>"Managed from the background only on a timer and at max once per 10 min.
/// even across crashes and restarts."</i> Velopack keeps no such time: the only
/// thing a check writes is the staging id <c>packages\.betaId</c>, read at
/// Velopack 1.2.161 (<c>VelopackLocator.cs:177-202</c>) and seen on disk
/// 2026-10-08 (step 0 of the one-binary build,
/// <c>.work/step0-velopack/FINDINGS.md</c>). So this record is the only thing that
/// carries the rate limit across a restart.
/// </remarks>
internal interface IUpdateCheckStamp
{
    /// <summary>Where the record is, for the log.</summary>
    string Where { get; }

    /// <summary>Reads the record.</summary>
    /// <returns>When the last check started, or why that is not known.</returns>
    UpdateCheckStampReading Read();

    /// <summary>Records that a check started at <paramref name="at"/>.</summary>
    /// <param name="at">When it started.</param>
    /// <exception cref="IOException">The record could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The record could not be written.</exception>
    void Write(DateTimeOffset at);
}

/// <summary>The record of the last update check, as one line in a file under the data root.</summary>
/// <remarks>
/// <para>
/// <b>Under the data root and never the install root</b>, for the reason the log
/// and the registration record are there (<see cref="Hosting.IAppPaths"/>): an
/// update replaces the install root's <c>current\</c>, and a repair install empties
/// the whole root, so a record kept there would vanish on exactly the restart it
/// exists to survive.
/// </para>
/// <para>
/// <b>One line, the start of the last check in the round-trip format</b>, so a
/// person can read it and nothing has to parse more than a time. A file that does
/// not hold one is unreadable, and the core then checks at once: the cost of a
/// wrong answer here is one check too many, never one too few.
/// </para>
/// </remarks>
/// <param name="path">The file.</param>
internal sealed class UpdateCheckStampFile(string path) : IUpdateCheckStamp
{
    /// <summary>The record's file name, directly under the data root.</summary>
    public const string FileName = "last-update-check.txt";

    /// <summary>The longest part of an unreadable record that a log line quotes.</summary>
    private const int QuotedLength = 40;

    /// <inheritdoc />
    public string Where => path;

    /// <summary>The record for one data root.</summary>
    /// <param name="dataRoot">The data root, <see cref="Hosting.IAppPaths.RootAppDir"/>.</param>
    /// <returns>The record.</returns>
    public static UpdateCheckStampFile In(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataRoot);
        return new UpdateCheckStampFile(Path.Combine(dataRoot, FileName));
    }

    /// <inheritdoc />
    public UpdateCheckStampReading Read()
    {
        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            return default;
        }
        catch (DirectoryNotFoundException)
        {
            return default;
        }
        catch (IOException failure)
        {
            return new UpdateCheckStampReading(null, failure.Message);
        }
        catch (UnauthorizedAccessException failure)
        {
            return new UpdateCheckStampReading(null, failure.Message);
        }

        var line = text.Trim();

        return DateTimeOffset.TryParseExact(line, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? new UpdateCheckStampReading(at, null)
            : new UpdateCheckStampReading(null, $"it holds '{Quoted(line)}', which is not a time");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Written beside itself and moved into place, so a reader never meets half a
    /// line; the data root holds one background at a time, so the temporary name
    /// is never contended.
    /// </remarks>
    public void Write(DateTimeOffset at)
    {
        var temporary = path + ".partial";

        File.WriteAllText(temporary, at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + "\n");
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Enough of an unreadable record to recognise it, and no more.</summary>
    /// <param name="line">The record's text.</param>
    /// <returns>At most <see cref="QuotedLength"/> characters of it.</returns>
    private static string Quoted(string line) =>
        line.Length <= QuotedLength ? line : line[..QuotedLength] + "...";
}
