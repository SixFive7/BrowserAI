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
/// <i>Since 2026-10-10 it only reads</i> (#101 of the texts review): the toasts never
/// install, and its answer to an install-now could never be shown.
/// </remarks>
internal sealed class DeferredUpdateHolds : IUpdateHoldsReader
{
    /// <summary>The holds every read goes to, once they exist.</summary>
    public IUpdateHoldsReader? Target { get; set; }

    /// <inheritdoc />
    public UpdateHoldSnapshot Read() => Target?.Read() ?? UpdateHoldSnapshot.Nothing(TimeProvider.System.GetUtcNow());

    /// <inheritdoc />
    public UpdateHoldSnapshot ReadCountdown() => Target?.ReadCountdown() ?? UpdateHoldSnapshot.Nothing(TimeProvider.System.GetUtcNow());
}
