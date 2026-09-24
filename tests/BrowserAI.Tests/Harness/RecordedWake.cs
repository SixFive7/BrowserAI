// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Coordination;

namespace BrowserAI.Tests.Harness;

/// <summary>A coordinator wake that counts and starts nothing.</summary>
/// <remarks>
/// <b>What every update-lane arm hands the service</b>: the service takes the wake
/// as a required argument so that no arm can start the developer's logon task by
/// forgetting one.
/// </remarks>
internal sealed class RecordedWake : ICoordinatorWake
{
    private int _wakes;

    /// <summary>How many times a pass asked for a coordinator.</summary>
    public int Wakes => Volatile.Read(ref _wakes);

    /// <inheritdoc />
    public WakeReport Wake()
    {
        _ = Interlocked.Increment(ref _wakes);
        return new WakeReport(WakeOutcome.Rechecked, "recorded");
    }
}
