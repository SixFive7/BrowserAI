// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI;

/// <summary>
/// Where a sentence that says how long something took, or how long ago it happened,
/// changes its unit, declared once.
/// </summary>
/// <remarks>
/// <para>
/// <b>One of the named classes of the numbers index</b>, decision F3 of the one-binary
/// build, and the one whose numbers nobody measured or needs to: nothing waits on
/// them. Each is the point at which a phrase such as <i>a minute ago</i> stops being
/// true and the next one starts, so it moves only when the wording does. They are here
/// because a literal duration anywhere else in the product is a red build, and each
/// has a row in <see href="../../kb/numbers.md">the numbers index</see> like every
/// other.
/// </para>
/// </remarks>
internal static class WordingTimes
{
    /// <summary>
    /// One minute: under it the page says <i>less than a minute ago</i> and a provisioning
    /// refusal counts in seconds, and from it a refusal counts in minutes and seconds.
    /// </summary>
    public static TimeSpan Minute { get; } = TimeSpan.FromMinutes(1);

    /// <summary>Two minutes: under it the page says <i>a minute ago</i>.</summary>
    public static TimeSpan TwoMinutes { get; } = TimeSpan.FromMinutes(2);

    /// <summary>One hour: under it the page counts in minutes.</summary>
    public static TimeSpan Hour { get; } = TimeSpan.FromHours(1);

    /// <summary>Two hours: under it the page says <i>an hour ago</i>.</summary>
    public static TimeSpan TwoHours { get; } = TimeSpan.FromHours(2);

    /// <summary>One day: under it the page counts in hours, and from it in days.</summary>
    public static TimeSpan Day { get; } = TimeSpan.FromDays(1);
}
