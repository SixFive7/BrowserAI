// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using Microsoft.Extensions.Logging;

namespace BrowserAI.Updates;

/// <summary>
/// Where this build looks for updates, and the one environment variable that
/// moves it.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b><see cref="ProductionBaseUrl"/> is deliberately unset, and that is a
/// state rather than an omission.</b> The update feed will be a public GitHub
/// repository — the maintainer has agreed to make it public — but **nothing has
/// been published and what gets published is still open**. Writing a URL here
/// before one exists would produce a build that checks a 404 on every start and
/// reports *"no update available"*, which is
/// [precisely the failure §G is most afraid of](../../plan/G-updates.md) wearing
/// the costume of a working feature. A build with no feed configured says so
/// once, at Debug, and never asks.
/// </para>
/// <para>
/// <b>This is the one bullet of
/// [step 19's done-test](../../plan/build-order.md#19-velopack-package-update-roll-back)
/// that is deferred</b> — *the real production feed URL resolves over HTTP and
/// returns a manifest* — and it is deferred rather than faked. A local HTTP
/// server would compose paths the same way and pass, while proving nothing about
/// the URL nobody has chosen yet.
/// </para>
/// </remarks>
internal static class UpdateConfiguration
{
    /// <summary>
    /// The production feed's base URL. <b>Not set</b>; see the remarks on this
    /// type.
    /// </summary>
    /// <remarks>
    /// When it is set, it is the repository's release feed root and it must
    /// <b>not</b> carry the channel — <see cref="UpdateFeed.Create"/> refuses one
    /// that does.
    /// </remarks>
    /// <remarks>
    /// <para>
    /// <b>Set 2026-08-17, on the maintainer's instruction to cut v1.0.0.</b> It
    /// is GitHub's <c>releases/latest/download/</c> alias rather than a
    /// tag-specific path, and that choice is the whole point: the alias
    /// redirects to the newest <b>non-prerelease</b> release, so it never needs
    /// rewriting per version and a build can never be pointed at the feed of the
    /// version it already is.
    /// </para>
    /// <para>
    /// <b>The channel is NOT in this URL, and must never be.</b>
    /// <c>plan/G-updates.md</c> calls that its worst hazard because it is
    /// unrecoverable in the field: a client that cannot reach its feed cannot be
    /// told to roll back either. The channel goes in
    /// <c>UpdateOptions.ExplicitChannel</c>, which is asserted by a test that
    /// fails if the line is removed.
    /// </para>
    /// </remarks>
    public const string? ProductionBaseUrl = "https://github.com/SixFive7/BrowserAI/releases/latest/download/";

    /// <summary>
    /// Points this build at a different feed: an absolute directory or an
    /// http(s) URL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It exists for the update lane, which cannot be tested any other way.</b>
    /// Proving that a package applies, that a rollback applies, and that the
    /// browsers beside <c>current\</c> survive both needs a real install pointed
    /// at a real feed — and until the production one exists, the only feed there
    /// is is a directory on this machine.
    /// </para>
    /// <para>
    /// <b>Never silent.</b> A BrowserAI updating itself from somewhere nobody
    /// expected is exactly the shape of failure this project exists to
    /// eliminate, so an override is logged at Warning, with the composed
    /// manifest URL rather than the base.
    /// </para>
    /// </remarks>
    public const string FeedVariable = "BROWSERAI_UPDATE_FEED";

    /// <summary>
    /// Resolves the feed this process should use, or <see langword="null"/> when
    /// there is none.
    /// </summary>
    /// <param name="logger">Where the decision is recorded.</param>
    /// <returns>The feed, or <see langword="null"/>.</returns>
    public static UpdateFeed? Resolve(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var overridden = Environment.GetEnvironmentVariable(FeedVariable);
        var configured = overridden is { Length: > 0 } ? overridden : ProductionBaseUrl;

        if (configured is null)
        {
            UpdateConfigurationLog.NoFeedConfigured(logger);
            return null;
        }

        try
        {
            var feed = UpdateFeed.Create(configured);

            if (overridden is { Length: > 0 })
            {
                UpdateConfigurationLog.FeedOverridden(logger, FeedVariable, feed.ManifestUrl);
            }

            return feed;
        }
        catch (ArgumentException failure)
        {
            // A refusal here is one of the three shapes that 404 silently, and
            // every one of them is better as a startup log line than as an
            // update path that reports "no update available" forever.
            UpdateConfigurationLog.FeedRefused(logger, configured, failure.Message);
            return null;
        }
    }
}

/// <summary>Source-generated log messages for feed configuration.</summary>
internal static partial class UpdateConfigurationLog
{
    /// <summary>This build has no feed at all.</summary>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "No update feed is configured, so BrowserAI does not check for updates. This is a state rather than a fault: the production feed has not been published yet.")]
    public static partial void NoFeedConfigured(ILogger logger);

    /// <summary>The feed came from the environment.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="variable">Which variable moved it.</param>
    /// <param name="manifestUrl">The composed manifest URL, not the base.</param>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "{Variable} is set, so this BrowserAI checks {ManifestUrl} for updates rather than the feed it shipped with.")]
    public static partial void FeedOverridden(ILogger logger, string variable, string manifestUrl);

    /// <summary>The configured feed is one of the shapes that 404 silently.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="configured">What was configured.</param>
    /// <param name="reason">Why it was refused.</param>
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        Message = "The configured update feed '{Configured}' was refused and no update check will run: {Reason}")]
    public static partial void FeedRefused(ILogger logger, string configured, string reason);
}
