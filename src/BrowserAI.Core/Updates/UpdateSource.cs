// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Updates;

/// <summary>
/// Where the one resident background takes its updates from: the folder or URL the
/// install's hooks wrote into its arguments, or the production feed when they named
/// none.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fixed at install time as an argument, and never read from the environment
/// at run time.</b> RESOLUTIONS 17 of 2026-10-08, settling H2: the maintainer's own
/// install takes its updates only from a folder on this machine, every other
/// install keeps GitHub, and the source is an argument the hooks write. The
/// variable <see cref="UpdateConfiguration.FeedVariable"/> names belongs to the
/// per-server update lane and nothing here reads it.
/// </para>
/// <para>
/// <b>Two shapes, told apart the way Velopack tells them apart.</b> An absolute
/// http or https URL is a served feed. A fully qualified folder path is a folder,
/// which Velopack reads through <c>SimpleFileSource</c>: <c>new UpdateManager(path)</c>
/// builds one for any string that is not an HTTP URL, read at Velopack 1.2.161
/// (<c>UpdateManager.cs:535-546</c>), and a folder holding 1.0.0 and 1.0.1 offered
/// 1.0.1 three times out of three, measured 2026-10-08 (step 0 of the one-binary
/// build, <c>.work/step0-velopack/FINDINGS.md</c>). A relative path is refused,
/// because a task-started process does not run in the folder the hook ran in; so is
/// a <c>file:</c> URI, which Velopack would read as a folder named after the whole
/// URI.
/// </para>
/// <para>
/// <b><see cref="UpdateFeed"/>'s channel rules hold for both</b>: a URL whose last
/// segment is the channel is refused, and the channel is
/// <see cref="UpdateFeed.DefaultChannel"/>.
/// </para>
/// </remarks>
internal sealed class UpdateSource
{
    /// <summary>The argument that names the source, followed by the folder or the URL.</summary>
    public const string Argument = "--update-source";

    private UpdateSource(UpdateFeed feed, bool isNamed)
    {
        Feed = feed;
        IsNamed = isNamed;
    }

    /// <summary>The production feed: what an install takes when no argument names a source.</summary>
    /// <remarks>
    /// <see cref="UpdateConfiguration.ProductionBaseUrl"/>, GitHub's
    /// <c>releases/latest/download/</c> alias on this repository.
    /// </remarks>
    public static UpdateSource Production { get; } = new(UpdateFeed.Create(UpdateConfiguration.ProductionBaseUrl!), isNamed: false);

    /// <summary>The feed Velopack is pointed at.</summary>
    public UpdateFeed Feed { get; }

    /// <summary>Whether the source is a folder and not a served feed.</summary>
    /// <remarks>
    /// <b>The one property a pre-release build is judged by</b>: it may check a
    /// folder and never a URL (H2, decided 2026-10-08).
    /// </remarks>
    public bool IsFolder => Feed.IsLocalDirectory;

    /// <summary>Whether an argument named the source; <see langword="false"/> for the production feed.</summary>
    public bool IsNamed { get; }

    /// <summary>Reads the source out of the background's arguments.</summary>
    /// <param name="arguments">The arguments the background was started with.</param>
    /// <returns>
    /// The source, or why the argument was refused. <b>A refused argument never
    /// falls back to the production feed</b>: an install that named a folder and
    /// got its spelling wrong would otherwise update from GitHub, which is the one
    /// thing H2 says his install does not do.
    /// </returns>
    public static UpdateSourceReading Read(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var at = -1;

        for (var index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], Argument, StringComparison.Ordinal))
            {
                continue;
            }

            if (at >= 0)
            {
                return Refused($"{Argument} is given twice, so which source the install meant cannot be told.");
            }

            at = index;
        }

        if (at < 0)
        {
            return new UpdateSourceReading(Production, null);
        }

        if (at + 1 >= arguments.Count || arguments[at + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return Refused($"{Argument} has no folder or URL after it.");
        }

        return FromValue(arguments[at + 1]);
    }

    /// <summary>Builds the source one argument value names.</summary>
    /// <param name="value">A folder or a URL.</param>
    /// <returns>The source, or why it was refused.</returns>
    private static UpdateSourceReading FromValue(string value)
    {
        var isUrl = Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

        var isFolder = !isUrl
            && !value.Contains("://", StringComparison.Ordinal)
            && Path.IsPathFullyQualified(value);

        if (!isUrl && !isFolder)
        {
            return Refused($"'{value}' is neither an absolute http or https URL nor a fully qualified folder path.");
        }

        try
        {
            var location = isUrl ? value : Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));

            return new UpdateSourceReading(new UpdateSource(UpdateFeed.Create(location), isNamed: true), null);
        }
        catch (ArgumentException refused)
        {
            return Refused(refused.Message);
        }
    }

    /// <summary>A reading that names no source.</summary>
    /// <param name="why">Why, as one sentence.</param>
    /// <returns>The reading.</returns>
    private static UpdateSourceReading Refused(string why) => new(null, why);
}

/// <summary>What the background's arguments say about its update source.</summary>
/// <param name="Source">The source, or <see langword="null"/> when the argument was refused.</param>
/// <param name="Refusal">Why the argument was refused, or <see langword="null"/>.</param>
internal sealed record UpdateSourceReading(UpdateSource? Source, string? Refusal);
