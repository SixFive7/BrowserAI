// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;

namespace BrowserAI.Updates;

/// <summary>
/// Where updates come from, and on which channel — with the one arrangement
/// that bricks a fleet made impossible to express.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The channel must never appear in the feed URL. This is the worst of the
/// Velopack hazards</b>
/// ([kb](../../../kb/packaging/velopack.md#1-the-channel-must-not-go-in-the-feed-url)),
/// because it is close to unrecoverable in
/// the field: a client that cannot reach the feed cannot be told to roll back
/// either, so every install already shipped needs a manual reinstall.
/// <c>SimpleWebSource</c> composes the request as
/// <c>{BaseUrl}/releases.{channel}.json</c>, so a base URL built as
/// <c>{BaseUrl}/{channel}</c> fetches <c>{BaseUrl}/{channel}/releases.{channel}.json</c>.
/// A shipped Velopack product did exactly that and lost auto-update for three
/// releases; the only recovery was a manual reinstall of every client.
/// </para>
/// <para>
/// <b>Three refusals, each from a measured failure</b>
/// ([kb](../../../kb/packaging/velopack.md#channel--the-charters-reason-was-wrong)):
/// a base URL whose last segment is the channel is the bug above and is refused;
/// an empty channel is refused because <c>ExplicitChannel = ""</c> is
/// <b>not</b> the same as unset — the code null-coalesces, so it yields
/// <c>releases..json</c> and a 404; and a channel that is not already lower-case
/// is refused because <c>vpk pack</c> lower-cases it while the client does not,
/// so <c>Beta</c> passes on NTFS and 404s on a case-sensitive object store.
/// </para>
/// <para>
/// <b>A 404 is catchable but is not by itself a misconfiguration signal.</b>
/// 1.2.0 throws <c>HttpRequestException … 404</c> rather than failing silently —
/// but a legitimately empty channel returns the same 404, so nothing here
/// alarms on one. That discrimination needs a second signal and is not
/// attempted.
/// </para>
/// </remarks>
internal sealed class UpdateFeed
{
    private UpdateFeed(string baseUrl, string channel)
    {
        BaseUrl = baseUrl;
        Channel = channel;
    }

    /// <summary>
    /// The single track BrowserAI publishes on.
    /// </summary>
    /// <remarks>
    /// <c>win</c> is Velopack's own default for Windows — the OS short name,
    /// stamped into <c>sq.version</c> and read back by the locator — so naming it
    /// explicitly costs nothing and buys the property that matters: an install
    /// that came from some other channel's <c>Setup.exe</c> still checks this
    /// one.
    /// </remarks>
    public const string DefaultChannel = "win";

    /// <summary>The feed's base URL, with no channel anywhere in it.</summary>
    public string BaseUrl { get; }

    /// <summary>
    /// The channel, which reaches Velopack through
    /// <c>UpdateOptions.ExplicitChannel</c> and through nothing else.
    /// </summary>
    public string Channel { get; }

    /// <summary>
    /// The manifest URL Velopack will compose from
    /// <see cref="BaseUrl"/> and <see cref="Channel"/>.
    /// </summary>
    /// <remarks>
    /// Reported, never requested by this type. It exists so a health check, a
    /// log line and a test can all say the same thing about where the client is
    /// actually looking — the shipped failure above was invisible precisely
    /// because nothing ever printed the composed URL.
    /// </remarks>
    public string ManifestUrl => $"{BaseUrl}/releases.{Channel}.json";

    /// <summary>Builds a feed, or refuses to.</summary>
    /// <param name="baseUrl">The feed root. A trailing slash is trimmed.</param>
    /// <param name="channel">The channel, lower-case and non-empty.</param>
    /// <returns>The feed.</returns>
    /// <exception cref="ArgumentException">The URL or the channel is one of the three shapes that 404.</exception>
    public static UpdateFeed Create(string baseUrl, string channel = DefaultChannel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        // Empty is not unset. Velopack null-coalesces ExplicitChannel, so ""
        // reaches the composer and produces `releases..json`.
        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new ArgumentException(
                "The update channel is empty. Velopack treats an empty ExplicitChannel as a channel rather than as 'unset', and composes 'releases..json', which 404s and is reported to the user as 'no update available'. Pass a channel, or take the default.",
                nameof(channel));
        }

        // ⚠️ CA1308 says to normalise upward. It is wrong here and the reason is
        // not a preference: `vpk pack` writes the channel LOWER-cased into the
        // manifest file name while the client composes the request from the
        // string it was given, so lower-case is the only form that resolves on a
        // case-sensitive object store. Normalising upward to satisfy the rule
        // would produce exactly the 404 this check exists to prevent.
#pragma warning disable CA1308
        var lowered = channel.ToLowerInvariant();
#pragma warning restore CA1308

        if (!string.Equals(channel, lowered, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The update channel '{channel}' is not lower-case. 'vpk pack' lower-cases the channel it writes into the manifest name while the client does not, so this resolves on a case-insensitive filesystem and 404s on a case-sensitive object store. Use '{lowered}'."),
                nameof(channel));
        }

        var trimmed = baseUrl.TrimEnd('/');
        var lastSegment = trimmed[(trimmed.LastIndexOf('/') + 1)..];

        if (string.Equals(lastSegment, channel, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The update feed URL '{baseUrl}' ends in the channel name '{channel}'. Velopack composes the request as '{{BaseUrl}}/releases.{{channel}}.json', so this would fetch '{trimmed}/releases.{channel}.json' under a directory that is itself the channel, which 404s and surfaces as 'no update available'. Put the channel in UpdateOptions.ExplicitChannel and nowhere else."),
                nameof(baseUrl));
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"The update feed URL '{baseUrl}' is not an absolute URI."),
                nameof(baseUrl));
        }

        // A local directory source composes paths differently and passes where
        // production 404s, which is why the release gate requires the REAL feed
        // URL to be resolved over HTTP (TESTING.md, and `UpdateTests`). Both are
        // accepted here -- the local one is how the update lane is exercised at
        // all -- but which is which must be visible.
        if (!parsed.IsFile && parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"The update feed URL '{baseUrl}' is neither a local directory nor an http(s) URL."),
                nameof(baseUrl));
        }

        return new UpdateFeed(trimmed, channel);
    }

    /// <summary>
    /// Whether this feed is a local directory rather than a served one.
    /// </summary>
    /// <remarks>
    /// <b>Load-bearing for what a green test means.</b> A local-directory source
    /// reads <c>releases.{channel}.json</c> out of the folder and never composes
    /// a URL, so it cannot fail the way landmine 1 fails — an update lane proven
    /// only against a directory has proven the packaging and the apply, and
    /// <i>not</i> the feed.
    /// </remarks>
    public bool IsLocalDirectory => !BaseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase);
}
