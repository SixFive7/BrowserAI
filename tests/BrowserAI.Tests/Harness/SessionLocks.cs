// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Sessions;

namespace BrowserAI.Tests.Harness;

/// <summary>The suite's way to let go of a session lock an acquisition may not have handed over.</summary>
/// <remarks>
/// <b>Added 2026-10-12 with the maintainer's 82 b</b>, which made a lock's release
/// asynchronous: the store's close runs on the store's own thread, and the directory is
/// free once it has run.
/// </remarks>
internal static class SessionLocks
{
    /// <summary>Releases the lock when there is one, and does nothing when there is not.</summary>
    /// <param name="held">The lock, or <see langword="null"/>.</param>
    /// <returns>The release.</returns>
    public static async Task DisposeIfTakenAsync(this SessionLock? held)
    {
        if (held is not null)
        {
            await held.DisposeAsync();
        }
    }
}
