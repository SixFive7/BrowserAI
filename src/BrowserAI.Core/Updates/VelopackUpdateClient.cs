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
/// of a pair.</b> It is what makes an older version acceptable to the client --
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
/// the network on construction; that never applied to 1.2.0 and does not apply at
/// the resolved 1.2.158 either, re-read 2026-09-23 -- the constructor
/// only assigns fields
/// ([kb](../../../kb/packaging/velopack.md#5-reading-the-installed-version-must-not-touch-the-network)).
/// The installed version is still read from
/// <see cref="InstallLocation"/> and not from here, because that is the type
/// that owns the locator.
/// </para>
/// </remarks>
internal sealed class VelopackUpdateClient : IUpdateClient, IStagedUpdates
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
        // it, and they bound it by abandoning the wait and not by cancelling
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
    /// <remarks>
    /// <b>No request is made.</b> <c>UpdatePendingRestart</c> reads the packages
    /// directory through the locator and compares versions, read at Velopack
    /// 1.2.158 (<c>UpdateManager.cs</c>); in a process that is not installed the
    /// installed version is unknown and the answer is always none.
    /// </remarks>
    public UpdateCandidate? Pending() =>
        _manager.UpdatePendingRestart is { } asset
            ? new UpdateCandidate
            {
                Version = asset.Version.ToFullString(),
                IsDowngrade = false,
                DeltaCount = 0,
                FullPackageSize = asset.Size,
                Native = asset,
            }
            : null;

    /// <inheritdoc cref="IUpdateClient.ApplyAfterThisProcessExits" />
    public void ApplyAfterThisProcessExits(UpdateCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        // ⚠️ TWO KINDS OF CANDIDATE SINCE 2026-09-25: the server's, which came
        // from a feed check and carries Velopack's UpdateInfo, and the
        // coordinator's, which came from the packages directory and carries the
        // asset itself. Both reach Update.exe the same way.
        var asset = candidate.Native switch
        {
            UpdateInfo info => info.TargetFullRelease,
            VelopackAsset staged => staged,
            _ => throw new InvalidOperationException("This candidate did not come from the Velopack client and cannot be applied by it."),
        };

        // silent: no dialogs -- there is no user at a background MCP server to
        // answer one. restart: false -- a relaunched process does not inherit
        // the caller's stdio, so restarting would produce a server with no
        // client. waitPid is this process, supplied by Velopack itself, which is
        // what guarantees the session locks are released before the swap.
        _manager.WaitExitThenApplyUpdates(asset, silent: true, restart: false);
    }

    /// <summary>
    /// Applies a downloaded update and starts the application again afterwards.
    /// </summary>
    /// <param name="candidate">What was downloaded.</param>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The opposite of <see cref="ApplyAfterThisProcessExits"/>, and the
    /// two must never be confused.</b> The SERVER never restarts: a restart
    /// there would start a process no client is speaking to, and with the
    /// configuration app as the main executable it would put a window on the
    /// screen in the middle of somebody's session. The configuration APP always
    /// restarts, because a window that vanished mid-click with nothing to say it
    /// had succeeded is the same defect from the other side.
    /// </para>
    /// <para>
    /// <b>It is Velopack's own pattern for a foreground application</b>, and the
    /// restarted process is started with <c>VELOPACK_RESTART</c> in its
    /// environment, which is how the window that comes back knows to say
    /// <i>Updated to ...</i>.
    /// </para>
    /// <para>
    /// ⚠️ <b>Whatever is under the install root is killed either way.</b>
    /// Velopack's apply ends in <c>force_stop_package</c>, which matches image
    /// path and not name, so a server serving a session goes with it. That is
    /// what the warning beside the button says out loud instead of leaving it to
    /// be discovered.
    /// </para>
    /// </remarks>
    public void ApplyAndRestart(UpdateCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.Native is not UpdateInfo info)
        {
            throw new InvalidOperationException("This candidate did not come from the Velopack client and cannot be applied by it.");
        }

        _manager.WaitExitThenApplyUpdates(info.TargetFullRelease, silent: true, restart: true);
    }
}
