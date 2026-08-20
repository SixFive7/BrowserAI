// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Artifacts;

namespace BrowserAI.Sessions;

/// <summary>
/// What a session directory <b>holds</b>, read off the filesystem rather than
/// off the record.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the half of <c>browserai_catch_up</c> that the log cannot
/// answer, and the two routinely disagree.</b> The log says what BrowserAI
/// <i>did</i>: which tools were called, in what order, and what the caller said
/// each was for. This says what is <i>true now</i> — and the load-bearing
/// example is credentials. **Cookies arrive from navigation, not from tools**, so
/// a session whose log shows no <c>browser_cookie_*</c> call at all can hold a
/// live signed-in profile, and a log-only answer would say <i>"no credential
/// tools were used"</i> about a directory full of them.
/// </para>
/// <para>
/// <b>Nothing here opens a file.</b> The cookie question is answered by the
/// <i>existence</i> of the store, not by reading it: opening a Chromium cookie
/// database to count rows would mean holding a file a running browser has open,
/// and the answer a caller acts on — <i>this directory may hold credentials</i>
/// — does not need the count.
/// </para>
/// <para>
/// <b>Every failure is reported as an unknown rather than as a zero.</b> A tree
/// that could not be walked is not an empty tree, and a caller about to call
/// <c>browserai_destroy</c> on the strength of <i>"nothing here"</i> is exactly
/// who must not be told that.
/// </para>
/// </remarks>
internal static class SessionInventory
{
    /// <summary>
    /// The extension of a HTTP Archive, which is a plaintext credential file.
    /// </summary>
    /// <remarks>
    /// A HAR records every request and response the browser made, headers
    /// included — so a session that holds one holds every bearer token and
    /// session cookie that crossed the wire in clear text. It is named
    /// separately from the artifact folders because it can land anywhere: it is
    /// upstream's <c>recordHar</c> output rather than something BrowserAI's
    /// filename routing places.
    /// </remarks>
    public const string HarExtension = ".har";

    /// <summary>Walks one session directory and reports what is in it.</summary>
    /// <param name="session">The canonicalised session directory.</param>
    /// <returns>What the directory holds.</returns>
    public static SessionContents Of(SessionPath session)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            var root = new DirectoryInfo(session.FullPath);

            if (!root.Exists)
            {
                return SessionContents.Unreadable($"'{session.FullPath}' does not exist.");
            }

            var files = root.EnumerateFiles("*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
            }).ToList();

            var kinds = new Dictionary<string, ArtifactKind>(StringComparer.OrdinalIgnoreCase);
            var archives = new List<SessionFile>();
            long bytes = 0;
            var touched = DateTimeOffset.MinValue;

            foreach (var file in files)
            {
                bytes += file.Length;

                var written = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);

                if (written > touched)
                {
                    touched = written;
                }

                var kind = KindOf(session.FullPath, file);

                if (!kinds.TryGetValue(kind, out var tally))
                {
                    tally = new ArtifactKind(kind);
                    kinds[kind] = tally;
                }

                tally.Add(file.Length);

                if (string.Equals(file.Extension, HarExtension, StringComparison.OrdinalIgnoreCase))
                {
                    archives.Add(new SessionFile(
                        Path.GetRelativePath(session.FullPath, file.FullName),
                        file.Length));
                }
            }

            return new SessionContents
            {
                Bytes = bytes,
                Files = files.Count,
                LastWritten = touched == DateTimeOffset.MinValue ? null : touched,
                Kinds = [.. kinds.Values.OrderByDescending(kind => kind.Bytes)],
                Archives = archives,
                CookieStore = CookieStoreIn(session),
            };
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return SessionContents.Unreadable(failure.Message);
        }
    }

    /// <summary>
    /// Which bucket one file belongs to: an artifact folder, the profile, the
    /// downloads, or the session's own files.
    /// </summary>
    /// <remarks>
    /// <b>Keyed on the folder rather than on the filename's generator prefix.</b>
    /// The prefix is what <see cref="ArtifactRouting"/> uses to decide where a
    /// file <i>goes</i>; a caller reading this wants to know what is <i>there</i>,
    /// including anything that arrived without a prefix at all.
    /// </remarks>
    /// <param name="root">The session directory.</param>
    /// <param name="file">One file inside it.</param>
    /// <returns>The bucket's name, as it is printed.</returns>
    private static string KindOf(string root, FileInfo file)
    {
        var relative = Path.GetRelativePath(root, file.FullName);
        var separator = relative.IndexOf(Path.DirectorySeparatorChar, StringComparison.Ordinal);

        if (separator < 0)
        {
            return "session files";
        }

        var top = relative[..separator];

        if (!string.Equals(top, SessionLayout.OutputFolderName, StringComparison.OrdinalIgnoreCase))
        {
            return top;
        }

        var rest = relative[(separator + 1)..];
        var nested = rest.IndexOf(Path.DirectorySeparatorChar, StringComparison.Ordinal);

        return nested < 0
            ? $"{SessionLayout.OutputFolderName} (unfiled)"
            : $"{SessionLayout.OutputFolderName}\\{rest[..nested]}";
    }

    /// <summary>
    /// Whether the profile holds a cookie store, and which browser's.
    /// </summary>
    /// <remarks>
    /// <b>Both families, by their own file names.</b> Chromium keeps
    /// <c>Network\Cookies</c> under the profile's default directory — and kept it
    /// at the profile root in older revisions, which is why both are looked for;
    /// Firefox keeps <c>cookies.sqlite</c>. A search rather than a fixed path,
    /// because the profile's inner directory name is the browser's business and
    /// not ours.
    /// </remarks>
    /// <param name="session">The session directory.</param>
    /// <returns>The store's path relative to the session, or <see langword="null"/>.</returns>
    private static string? CookieStoreIn(SessionPath session)
    {
        var profile = Path.Combine(session.FullPath, SessionLayout.ProfileFolderName);

        if (!Directory.Exists(profile))
        {
            return null;
        }

        foreach (var name in new[] { "Cookies", "cookies.sqlite" })
        {
            try
            {
                var found = Directory.EnumerateFiles(profile, name, new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                }).FirstOrDefault();

                if (found is not null)
                {
                    return Path.GetRelativePath(session.FullPath, found);
                }
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }
}

/// <summary>What one session directory holds, right now.</summary>
internal sealed record SessionContents
{
    /// <summary>Every byte under the session directory.</summary>
    public long Bytes { get; init; }

    /// <summary>How many files there are.</summary>
    public int Files { get; init; }

    /// <summary>
    /// When anything under the directory was last written, or
    /// <see langword="null"/> when there is nothing.
    /// </summary>
    /// <remarks>
    /// <b>Distinct from the record's <c>lastUsed</c>, and the difference is
    /// worth printing.</b> The record moves when BrowserAI writes it; this moves
    /// when a <i>browser</i> writes into the profile, which happens continuously
    /// while a page is open and not at all afterwards.
    /// </remarks>
    public DateTimeOffset? LastWritten { get; init; }

    /// <summary>What is there, by bucket, largest first.</summary>
    public IReadOnlyList<ArtifactKind> Kinds { get; init; } = [];

    /// <summary>Every HTTP Archive under the session.</summary>
    public IReadOnlyList<SessionFile> Archives { get; init; } = [];

    /// <summary>Where the profile's cookie store is, or <see langword="null"/>.</summary>
    public string? CookieStore { get; init; }

    /// <summary>Why the directory could not be read, or <see langword="null"/>.</summary>
    public string? Failure { get; init; }

    /// <summary>Builds the answer for a directory that could not be walked.</summary>
    /// <param name="why">What went wrong.</param>
    /// <returns>The contents, which report nothing rather than zero.</returns>
    public static SessionContents Unreadable(string why) => new() { Failure = why };
}

/// <summary>One bucket of a session's contents.</summary>
/// <param name="Name">The bucket, as it is printed.</param>
internal sealed record ArtifactKind(string Name)
{
    /// <summary>How many files are in it.</summary>
    public int Files { get; private set; }

    /// <summary>How many bytes they come to.</summary>
    public long Bytes { get; private set; }

    /// <summary>Counts one file.</summary>
    /// <param name="bytes">Its size.</param>
    public void Add(long bytes)
    {
        Files++;
        Bytes += bytes;
    }

    /// <summary>The bucket as one line.</summary>
    /// <returns>Name, count and size.</returns>
    public override string ToString() =>
        $"{Name}: {Files.ToString(CultureInfo.InvariantCulture)} file(s), {Sizes.Describe(Bytes)}";
}

/// <summary>
/// A byte count in a unit that does not read as "empty" for a small one.
/// </summary>
/// <remarks>
/// <b>A fourth spelling of a size, and it earns its place.</b> The three that
/// exist are fixed-unit: <c>browserai_list</c> and the artifact roll-up print
/// MiB to one place, and the provisioner prints decimal MB because that is the
/// unit a CDN's <c>content-length</c> is quoted in. Fixed MiB is exactly wrong
/// for this answer — a bucket holding three screenshots prints <c>0.0 MiB</c>,
/// which a caller about to destroy a session reads as <i>nothing here</i>. The
/// two established figures are deliberately left alone rather than migrated:
/// they are published numbers with tests over them, and changing what they say
/// is a separate decision from adding a place that needed something else.
/// </remarks>
internal static class Sizes
{
    /// <summary>Formats one byte count.</summary>
    /// <param name="bytes">The count.</param>
    /// <returns>Bytes, KiB or MiB, whichever does not round to zero.</returns>
    public static string Describe(long bytes) =>
        bytes switch
        {
            < 1024 => $"{bytes.ToString("N0", CultureInfo.InvariantCulture)} B",
            < 1024 * 1024 => $"{(bytes / 1024d).ToString("F1", CultureInfo.InvariantCulture)} KiB",
            _ => $"{(bytes / (1024d * 1024d)).ToString("F1", CultureInfo.InvariantCulture)} MiB",
        };
}

/// <summary>One named file inside a session.</summary>
/// <param name="RelativePath">Its path relative to the session directory.</param>
/// <param name="Bytes">Its size.</param>
internal sealed record SessionFile(string RelativePath, long Bytes);
