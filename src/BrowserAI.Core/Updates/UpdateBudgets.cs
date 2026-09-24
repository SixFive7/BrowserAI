// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Updates;

/// <summary>
/// The four durations an update pass is bounded by.
/// </summary>
/// <remarks>
/// <para>
/// <b>They are a record and not four static reads so that the DISCRIMINATION
/// between them can be watched.</b> The numbers themselves are
/// <see cref="UpdateService.CheckBudget"/>,
/// <see cref="UpdateService.AbsoluteBudget"/>,
/// <see cref="UpdateService.StallBudget"/> and
/// <see cref="UpdateService.CrashTripwire"/>, and
/// <see cref="Default"/> is exactly those; nothing in the product passes anything
/// else. What this buys is that a test can ask <i>which</i> timer fired without
/// waiting three quarters of an hour to find out, which is the difference between
/// a behaviour that is asserted and one that is commented.
/// </para>
/// <para>
/// ⚠️ <b>SCALING PRESERVES THE RELATIONSHIPS AND THAT IS THE WHOLE POINT.</b>
/// <see cref="Scaled"/> divides all four by the same factor, so a test runs
/// against the product's own arithmetic -- check plus absolute inside the
/// tripwire -- and not against four numbers somebody typed. A test that wrote
/// its own durations could satisfy itself with an ordering the product does not
/// have.
/// </para>
/// </remarks>
internal sealed record UpdateBudgets
{
    /// <summary>How long the manifest check may take.</summary>
    public required TimeSpan Check { get; init; }

    /// <summary>How long the download may take, however fast it is going.</summary>
    public required TimeSpan Absolute { get; init; }

    /// <summary>How long the download may make no progress at all.</summary>
    public required TimeSpan Stall { get; init; }

    /// <summary>The outer deadline, which is a crash tripwire and not a budget.</summary>
    public required TimeSpan Tripwire { get; init; }

    /// <summary>The product's own four, and the only set it ever runs with.</summary>
    public static UpdateBudgets Default { get; } = new()
    {
        Check = UpdateService.CheckBudget,
        Absolute = UpdateService.AbsoluteBudget,
        Stall = UpdateService.StallBudget,
        Tripwire = UpdateService.CrashTripwire,
    };

    /// <summary>The same four, divided by one factor.</summary>
    /// <param name="factor">What to divide by. Greater than zero.</param>
    /// <returns>A proportional set.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The factor is not positive.</exception>
    public UpdateBudgets Scaled(double factor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(factor, 0);

        return new UpdateBudgets
        {
            Check = Check / factor,
            Absolute = Absolute / factor,
            Stall = Stall / factor,
            Tripwire = Tripwire / factor,
        };
    }
}
