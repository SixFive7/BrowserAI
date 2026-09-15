// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using Velopack;

namespace BrowserAI.Updates;

/// <summary>
/// The real <see cref="IUpdateClient"/>: the only type in the product that
/// constructs an <see cref="UpdateManager"/>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The channel reaches Velopack through
/// <see cref="UpdateOptions.ExplicitChannel"/> and through nothing else</b>, and
/// <see cref="UpdateFeed"/> has already refused a base URL that carries one.
/// Two independent guards on the same hazard, because it is the one that cannot
/// be recovered from in the field.
/// </para>
/// <para>
/// ⚠️ <b><see cref="UpdateOptions.AllowVersionDowngrade"/> is on, and it is half
/// of a pair.</b> It is what makes an older version acceptable to the client —
/// it <i>is</i> the rollback mechanism, and its default is <see langword="false"/>,
/// which yields *"no updates"* silently. The other half is on the pipeline:
/// <c>build/New-Release.ps1</c>'s validation rule reads *monotonic <b>or</b> an
/// explicit rollback republish*. Turn on one without the other and the runtime
/// accepts a rollback the build refuses to emit, which is the state a shipping
/// product examined for this project is in.
/// </para>
/// <para>
/// <b>Constructing this is cheap and issues no request.</b> The landmine list
/// this product was built against said an <c>UpdateManager</c> touches
/// the network on construction; that never applied to 1.2.0 — the constructor
/// only assigns fields
/// ([kb](../../../kb/packaging/velopack.md#5-reading-the-installed-version-must-not-touch-the-network)).
/// The installed version is still read from
/// <see cref="InstallLocation"/> rather than from here, because that is the type
/// that owns the locator.
/// </para>
/// </remarks>
internal sealed class VelopackUpdateClient : IUpdateClient
{
    private readonly UpdateManager _manager;
    private readonly UpdateFeed _feed;

    /// <summary>Builds a client against a feed.</summary>
    /// <param name="feed">The feed, already validated.</param>
    public VelopackUpdateClient(UpdateFeed feed)
    {
        ArgumentNullException.ThrowIfNull(feed);

        _feed = feed;
        _manager = new UpdateManager(
            feed.BaseUrl,
            new UpdateOptions
            {
                ExplicitChannel = feed.Channel,
                AllowVersionDowngrade = true,
            });
    }

    /// <inheritdoc />
    public string ManifestUrl => _feed.ManifestUrl;

    /// <inheritdoc />
    public async Task<UpdateCandidate?> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // CheckForUpdatesAsync takes no token. The caller's timers are what bound
        // it, and they bound it by abandoning the wait rather than by cancelling
        // the request -- which is honest about what this API can do, instead of
        // passing a token that is ignored.
        var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);

        return info is null
            ? null
            : new UpdateCandidate
            {
                Version = info.TargetFullRelease.Version.ToFullString(),
                IsDowngrade = info.IsDowngrade,
                DeltaCount = info.DeltasToTarget.Length,
                FullPackageSize = info.TargetFullRelease.Size,
                Native = info,
            };
    }

    /// <inheritdoc />
    public Task DownloadAsync(UpdateCandidate candidate, Action<int> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return candidate.Native is UpdateInfo info
            ? _manager.DownloadUpdatesAsync(info, progress, cancellationToken)
            : throw new InvalidOperationException("This candidate did not come from the Velopack client and cannot be downloaded by it.");
    }

    /// <inheritdoc />
    public void ApplyAfterThisProcessExits(UpdateCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.Native is not UpdateInfo info)
        {
            throw new InvalidOperationException("This candidate did not come from the Velopack client and cannot be applied by it.");
        }

        // silent: no dialogs -- there is no user at a background MCP server to
        // answer one. restart: false -- a relaunched process does not inherit
        // the caller's stdio, so restarting would produce a server with no
        // client. waitPid is this process, supplied by Velopack itself, which is
        // what guarantees the session locks are released before the swap.
        _manager.WaitExitThenApplyUpdates(info.TargetFullRelease, silent: true, restart: false);
    }
}
