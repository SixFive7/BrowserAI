// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Updates;

namespace BrowserAI.App.Page;

/// <summary>The page's update machinery in the product: Velopack, against the configured feed.</summary>
/// <remarks>
/// <para>
/// <b>What the 2026-09-24 rendering of the window found carries over, and two of
/// its three findings are answered here.</b> A feed that is a folder with no release
/// list in it read as <i>up to date</i>, because Velopack answers a missing list
/// with no update; <see cref="MissingReleaseList"/> asks first, so the page says
/// the list is missing. A check that hung showed <i>Checking</i> with no way out;
/// the page can always give the wait up, and the check is still bounded by the
/// server's own tripwire for the same call.
/// </para>
/// <para>
/// <b>An install downloads only what is not on disk</b>: a candidate from a check
/// carries Velopack's <c>UpdateInfo</c> and is downloaded first, and a staged one
/// carries the package a server already downloaded. Both are handed to
/// <c>Update.exe</c> with a restart, which is what brings the new tab after the
/// update (Q338 b).
/// </para>
/// </remarks>
/// <param name="feed">The configured feed, or <see langword="null"/> when there is none.</param>
/// <param name="installed">Whether this process is an installed BrowserAI.</param>
internal sealed class VelopackPageUpdates(UpdateFeed? feed, bool installed) : IPageUpdates
{
    private readonly VelopackUpdateClient? _client = installed && feed is not null ? new VelopackUpdateClient(feed) : null;

    /// <inheritdoc />
    public UpdateStage? Unavailable =>
        !installed ? UpdateStage.NotInstalled
            : feed is null ? UpdateStage.NoFeed
            : null;

    /// <inheritdoc />
    public string? MissingReleaseList() =>
        feed is { IsLocalDirectory: true } local && !File.Exists(local.ManifestUrl) ? local.ManifestUrl : null;

    /// <inheritdoc />
    public Task<UpdateCandidate?> CheckAsync(CancellationToken cancellationToken) =>
        _client is { } client
            ? client.CheckAsync(cancellationToken)
            : throw new InvalidOperationException("There is no update feed to check: this BrowserAI is not installed, or no feed is set.");

    /// <inheritdoc />
    public UpdateCandidate? Staged() => _client?.Pending();

    /// <inheritdoc />
    public async Task InstallAsync(UpdateCandidate candidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (_client is not { } client)
        {
            throw new InvalidOperationException("There is no update feed to install from: this BrowserAI is not installed, or no feed is set.");
        }

        if (candidate.Native is Velopack.UpdateInfo)
        {
            await client.DownloadAsync(candidate, _ => { }, cancellationToken).ConfigureAwait(false);
        }

        client.ApplyAndRestart(candidate);
    }
}
