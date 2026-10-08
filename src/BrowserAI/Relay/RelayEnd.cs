// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Relay;

/// <summary>Why a relay stopped.</summary>
internal enum RelayEnding
{
    /// <summary>The client closed its end of the input, or its output could no longer be written.</summary>
    ClientWentAway,

    /// <summary>The token handed to the engine fired: the client's process went away.</summary>
    Cancelled,

    /// <summary>
    /// The background committed an update and told this relay to end (H1, RESOLUTIONS
    /// 13). The process exits 0 and the installer replaces it.
    /// </summary>
    ForAnUpdate,
}

/// <summary>How a relay ended.</summary>
/// <param name="Reason">Why it stopped.</param>
/// <param name="UpdateVersion">
/// The version being installed, when <paramref name="Reason"/> is
/// <see cref="RelayEnding.ForAnUpdate"/> and the background named one.
/// </param>
internal sealed record RelayEnd(RelayEnding Reason, string? UpdateVersion);
