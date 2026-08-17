// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Updates;

/// <summary>
/// Everything the update path asks of Velopack, behind one interface.
/// </summary>
/// <remarks>
/// <para>
/// <b>The seam is the reason the update path can be tested at all.</b> Under
/// <c>dotnet run</c> and under every test host this process is not a Velopack
/// install, so a build that called <c>UpdateManager</c> directly could only ever
/// be exercised by installing itself — and a server that self-restarts would
/// relaunch itself out of the suite
/// ([kb](../../../kb/packaging/velopack.md#6-notinstalledexception-under-dotnet-run-and-every-test-host)).
/// </para>
/// <para>
/// <b>It is deliberately four members.</b> Every one of them is a place Velopack
/// is touched, and a fifth would mean a fifth thing the suite cannot see. The
/// timers, the gate, the channel and the decision to apply are all on this side
/// of the seam, in <see cref="UpdateService"/>, where they are ordinary code.
/// </para>
/// </remarks>
internal interface IUpdateClient
{
    /// <summary>
    /// Where this client is looking, composed exactly as Velopack will compose
    /// it. Logged on every check.
    /// </summary>
    string ManifestUrl { get; }

    /// <summary>Asks the feed what is available.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The candidate, or <see langword="null"/> when there is nothing to do.</returns>
    Task<UpdateCandidate?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Downloads and stages a candidate.</summary>
    /// <param name="candidate">What <see cref="CheckAsync"/> returned.</param>
    /// <param name="progress">Called with 0–100. <b>Every call resets the stall timer</b>, so it must be invoked from the download rather than from a clock.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The download.</returns>
    Task DownloadAsync(UpdateCandidate candidate, Action<int> progress, CancellationToken cancellationToken);

    /// <summary>
    /// Spawns <c>Update.exe apply --silent --norestart --waitPid &lt;ownPid&gt;</c>
    /// and returns.
    /// </summary>
    /// <remarks>
    /// <b>It does not restart and it does not exit.</b> <c>--norestart</c> is
    /// deliberate: a relaunched BrowserAI does not inherit the caller's stdio
    /// ([kb](../../../kb/packaging/velopack.md#rollback)), so a restarted process
    /// would be a server with no client. The next session starts the new version
    /// from the identical path, which is why there is no *restart to apply*
    /// prompt in normal use. Exiting is the caller's job, so that the ordinary
    /// shutdown path runs and the lock, the job objects and the log all close.
    /// </remarks>
    /// <param name="candidate">The staged candidate to apply.</param>
    void ApplyAfterThisProcessExits(UpdateCandidate candidate);
}

/// <summary>What the feed is offering, in terms this side of the seam can read.</summary>
/// <remarks>
/// <see cref="Native"/> is Velopack's own <c>UpdateInfo</c> and is opaque
/// everywhere except <see cref="VelopackUpdateClient"/>. Carrying it is what
/// lets the decision to download and the download itself be separate calls
/// without this type having to reproduce Velopack's delta bookkeeping.
/// </remarks>
internal sealed record UpdateCandidate
{
    /// <summary>The version being offered.</summary>
    public required string Version { get; init; }

    /// <summary>Whether this is a rollback.</summary>
    /// <remarks>
    /// Only ever <see langword="true"/> when <c>AllowVersionDowngrade</c> is on,
    /// which is the client half of rollback and is on by design.
    /// </remarks>
    public required bool IsDowngrade { get; init; }

    /// <summary>
    /// How many delta packages stand between the installed version and the
    /// target. <b>Zero means a full download.</b>
    /// </summary>
    /// <remarks>
    /// Logged because it is the number that decides whether an update costs
    /// single-digit MB or the whole payload — and because a rollback always
    /// reports zero: <c>packages\</c> is pruned to the current full package
    /// during the forward update and deltas are forward-only.
    /// </remarks>
    public required int DeltaCount { get; init; }

    /// <summary>The size of the full package behind this candidate, in bytes.</summary>
    public required long FullPackageSize { get; init; }

    /// <summary>Velopack's own object. Opaque outside the real client.</summary>
    public object? Native { get; init; }
}
