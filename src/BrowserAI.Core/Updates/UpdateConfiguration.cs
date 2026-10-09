// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr


namespace BrowserAI.Updates;

/// <summary>
/// Where this build looks for updates, and the one environment variable that
/// moves it.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b><see cref="ProductionBaseUrl"/> is deliberately unset, and that is a
/// state and not an omission.</b> The update feed will be a public GitHub
/// repository -- the maintainer has agreed to make it public -- but **nothing has
/// been published and what gets published is still open**. Writing a URL here
/// before one exists would produce a build that checks a 404 on every start and
/// reports *"no update available"*, which is
/// [precisely the failure that bricked a shipped fleet](../../../kb/packaging/velopack.md#1-the-channel-must-not-go-in-the-feed-url),
/// wearing the costume of a working feature. A build with no feed configured says so
/// once, at Debug, and never asks.
/// </para>
/// <para>
/// <b>This is the one release-gate assertion that is deferred</b> -- *the real
/// production feed URL resolves over HTTP and
/// returns a manifest* -- and it is deferred, not faked. A local HTTP
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
    /// <b>not</b> carry the channel -- <see cref="UpdateFeed.Create"/> refuses one
    /// that does.
    /// </remarks>
    /// <remarks>
    /// <para>
    /// <b>Set 2026-08-17, on the maintainer's instruction to cut v1.0.0.</b> It
    /// is GitHub's <c>releases/latest/download/</c> alias and not a
    /// tag-specific path, and that choice is the whole point: the alias
    /// redirects to the newest <b>non-prerelease</b> release, so it never needs
    /// rewriting per version and a build can never be pointed at the feed of the
    /// version it already is.
    /// </para>
    /// <para>
    /// <b>The channel is NOT in this URL, and must never be.</b> It is the worst
    /// of the Velopack hazards
    /// ([kb](../../../kb/packaging/velopack.md#1-the-channel-must-not-go-in-the-feed-url))
    /// because it is
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
    /// at a real feed -- and until the production one exists, the only feed there
    /// is is a directory on this machine.
    /// </para>
    /// <para>
    /// <b>Never silent.</b> A BrowserAI updating itself from somewhere nobody
    /// expected is exactly the shape of failure this project exists to
    /// eliminate, so an override is logged at Warning, with the composed
    /// manifest URL and not the base.
    /// </para>
    /// </remarks>
    public const string FeedVariable = "BROWSERAI_UPDATE_FEED";

    // Resolve WENT 2026-10-08 with step 5 of the one-binary build. It read
    // BROWSERAI_UPDATE_FEED in a running BrowserAI, and nothing called it once
    // UpdateService was deleted: the background takes its source as
    // --update-source (UpdateSource), which the hooks write from the installer's
    // environment, and UpdateConfigurationLog went with it, ids 1 to 3 and all.
}
