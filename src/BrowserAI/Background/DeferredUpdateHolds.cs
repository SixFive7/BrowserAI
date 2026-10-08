// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Updates;

namespace BrowserAI.Background;

/// <summary>
/// The update core's holds, handed to the toasts before the core exists.
/// </summary>
/// <remarks>
/// <b>The two need each other</b>: the update core raises the toasts, and the ready
/// toast reads the core's holds once a second for its live countdown. The toasts are
/// built first and read through this; until <see cref="Target"/> is set, which the
/// background does on the next line after building the core, there is nothing held.
/// </remarks>
internal sealed class DeferredUpdateHolds : IUpdateHolds
{
    /// <summary>The holds every read goes to, once they exist.</summary>
    public IUpdateHolds? Target { get; set; }

    /// <inheritdoc />
    public UpdateHoldSnapshot Read() => Target?.Read() ?? UpdateHoldSnapshot.Nothing(TimeProvider.System.GetUtcNow());

    /// <inheritdoc />
    public Task<string?> InstallNowAsync(string version, CancellationToken cancellationToken) =>
        Target is { } target
            ? target.InstallNowAsync(version, cancellationToken)
            : Task.FromResult<string?>("BrowserAI is still starting, so it installs nothing now.");
}
