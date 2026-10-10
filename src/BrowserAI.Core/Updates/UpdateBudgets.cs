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
/// <see cref="CheckBudget"/>,
/// <see cref="AbsoluteBudget"/>,
/// <see cref="StallBudget"/> and
/// <see cref="CrashTripwire"/>, and
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

    // ⚠️ THE FOUR NUMBERS MOVED HERE 2026-10-08 FROM UpdateService, which was a
    // server's own update lane and went with the in-process server (S a): only the
    // background checks for updates since that day, through BackgroundUpdates, and
    // these are its bounds. Their remarks moved with them, unchanged.

    /// <summary>
    /// The whole download, end to end.
    /// </summary>
    /// <remarks>
    /// Sized against a link, not a payload: 30 minutes carries
    /// <b>112.4 MB</b> at ~500 kbit/s, which is slower than any link this
    /// product is usable on -- a first-run browser provisioning of 208.8 MB has
    /// to succeed on the same connection before BrowserAI works at all
    /// (<i>corrected 2026-09-17, previously "203.8 MB"; re-measured 2026-09-16 at
    /// chromium 1244, and the figure the server renders is
    /// <c>BrowserProvisioner.FirstRunDownloadSizes</c> and not this
    /// sentence</i>). It is a
    /// bound on a pathology, not a service level.
    /// <para>
    /// <b>Corrected 2026-08-16 at the plan's final audit (previously "the
    /// measured **112.4 MB** full package").</b> 112.4 MB is what the budget
    /// <i>carries</i>, derived from 30 minutes × 500 kbit/s; it is a link
    /// budget and was never a measurement of anything. The full package is
    /// <b>49,050,382 bytes</b>, measured 2026-08-16 -- less than half of it, so
    /// the headroom is larger than the sentence claimed, which is why nothing
    /// downstream broke. A derived number wearing the word <i>measured</i> is
    /// indistinguishable from a real one, which is the exact failure this
    /// repository forbids.
    /// </para>
    /// </remarks>
    public static TimeSpan AbsoluteBudget => TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long the download may make no progress at all before it is abandoned.
    /// </summary>
    /// <remarks>
    /// Reset on every progress callback, and it is the <b>only</b> stall bound
    /// that exists anywhere on this download.
    /// <para>
    /// <b>The downloader underneath bounds a half-dead socket at nothing at
    /// all</b> -- Velopack's only bound is a 30-minute <c>HttpClient.Timeout</c>
    /// that stops at the response headers, measured 2026-09-23 @ Velopack
    /// 1.2.158
    /// ([kb](../../../kb/packaging/velopack.md#what-bounds-a-stalled-download-and-a-stalled-check----measured-2026-09-23)).
    /// So 60 s is a number this product
    /// <i>chooses</i>: long enough that a link which is coming back has come
    /// back, short enough that <see cref="AbsoluteBudget"/> is not the first
    /// thing to notice.
    /// </para>
    /// <para>
    /// <i>Corrected 2026-09-23 (previously "60 s is twice upstream Playwright's
    /// own per-socket <c>NET_DEFAULT_TIMEOUT</c> of 30 s, so a transport that is
    /// going to recover has already recovered").</i> That constant is real --
    /// <c>NET_DEFAULT_TIMEOUT = 3e4</c> at
    /// <c>payload/mcp/node_modules/playwright-core/lib/coreBundle.js:9087</c> @
    /// playwright-core 1.64.0-alpha-1789764292000 -- but it is read exactly once,
    /// at line 34415, as the socket timeout for a <i>browser download in Node</i>.
    /// A different downloader in a different runtime, bounding nothing this
    /// constant governs.
    /// </para>
    /// </remarks>
    public static TimeSpan StallBudget => TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long the manifest check may take before it is abandoned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The maintainer's decision, 2026-09-24, verbatim:</b> <i>"Wrap the check
    /// in its own timer. So all three timers sit in the tripwire's time."</i>
    /// </para>
    /// <para>
    /// ⚠️ <b>WITHOUT IT THE CHECK WAS BOUNDED BY NOTHING THIS PRODUCT
    /// CONTROLS.</b> Velopack 1.2.158's <c>UpdateManager.CheckForUpdatesAsync()</c>
    /// takes no <see cref="CancellationToken"/> at all, so the token handed to
    /// <c>IUpdateClient.CheckAsync</c> was read once on entry and never again: a
    /// stalled manifest fetch ended only on Velopack's own
    /// <c>HttpClient.Timeout</c>, <b>30 minutes</b>, taken as the default
    /// ([kb](../../../kb/packaging/velopack.md#what-bounds-a-stalled-download-and-a-stalled-check----measured-2026-09-23)).
    /// 30 minutes of check plus a download inside its own 30-minute
    /// <see cref="AbsoluteBudget"/> is 60 against a 45-minute
    /// <see cref="CrashTripwire"/>, so a pass in which every timer behaved could
    /// reach the tripwire -- which is exactly what the tripwire is supposed to
    /// prove cannot happen.
    /// </para>
    /// <para>
    /// <b>15 minutes, and the number is derived and not chosen.</b> The
    /// arithmetic the tripwire needs is <c>this + AbsoluteBudget &lt;
    /// CrashTripwire</c>, and at 30 and 45 that leaves 15 as the ceiling. Since
    /// 2026-10-09 the code computes it that way, <see cref="CrashTripwire"/> less
    /// <see cref="AbsoluteBudget"/>, so the ceiling moves with either. It is
    /// taken whole and not shaded down, because the check is one small HTTPS
    /// GET of a JSON feed and 15 minutes is already three orders of magnitude
    /// above it: **a bound this far out is a hang detector and not a promptness
    /// claim**, which is the only kind of duration assertion this project allows.
    /// The margin the tripwire keeps is therefore exactly zero by arithmetic and
    /// the whole of <see cref="StallBudget"/> in practice, since a download that
    /// is moving resets that timer and a download that is not never reaches
    /// <see cref="AbsoluteBudget"/>.
    /// </para>
    /// <para>
    /// ⚠️ <b>IT IS APPLIED BY WAITING, NOT BY PASSING A TOKEN, because passing
    /// one does not work.</b> <c>CheckAsync</c> honours cancellation only up to
    /// the point where it calls Velopack; past that the token is inert. So the
    /// call is awaited through <c>WaitAsync</c> with this budget's token, which
    /// ends THIS pass on time and leaves Velopack's own call to finish into
    /// nothing. That is a deliberate trade: the alternative is a pass that cannot
    /// be ended at all.
    /// </para>
    /// </remarks>
    public static TimeSpan CheckBudget => CrashTripwire - AbsoluteBudget;

    /// <summary>
    /// The outer deadline. <b>A crash tripwire, never flow control.</b>
    /// </summary>
    /// <remarks>
    /// It is deliberately far outside <see cref="CheckBudget"/> plus
    /// <see cref="AbsoluteBudget"/> plus <see cref="StallBudget"/>. It exists
    /// because the alternative to a wedged background pass is a thread that never
    /// ends and never says so.
    /// <para>
    /// ⚠️ <b>ALL THREE TIMERS SIT INSIDE IT NOW, AND REACHING IT IS A DEFECT
    /// AGAIN.</b> <i>Corrected 2026-09-24 (previously "What it does NOT cover is
    /// the check, and a working pass can reach it ... so reaching this deadline is
    /// not by itself evidence that an inner timer failed"), when the maintainer
    /// took the decision quoted on <see cref="CheckBudget"/>.</i> The check is
    /// bounded by <see cref="CheckBudget"/> at 15 minutes and the download by
    /// <see cref="AbsoluteBudget"/> at 30, so the arithmetic is 45 against this
    /// 45 and no pass in which every timer behaved can arrive here. It is the
    /// sentence this constant is named for, and it is true again.
    /// </para>
    /// <para>
    /// <b>And a check timeout no longer arrives wearing this deadline's name.</b>
    /// <c>GetStringAsync</c> ends its <c>HttpClient.Timeout</c> by throwing a
    /// <c>TaskCanceledException</c>, which is an
    /// <see cref="OperationCanceledException"/>, so before the budget existed
    /// every network timeout on the check reported as the tripwire firing.
    /// <c>RunOnceAsync</c> now discriminates: a cancellation whose source is the
    /// check budget logs <c>UpdateLog.CheckTimedOut</c>, and only a cancellation
    /// that is neither the lifetime nor the check reaches
    /// <c>UpdateLog.TripwireFired</c>.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-10-10 by addition:</b> <c>RunOnceAsync</c> went with the
    /// server's update pass on 2026-10-08, and the two records above were deleted with
    /// the rest of that pass's records under the maintainer's "9 a". The background's
    /// check logs <c>BackgroundUpdateLog.CheckTimedOut</c> at its own budget, and this
    /// deadline bounds the dashboard's <i>Install now</i> (<c>PageService</c>).
    /// </para>
    /// </remarks>
    public static TimeSpan CrashTripwire => TimeSpan.FromMinutes(45);

    /// <summary>
    /// How often the background asks its update source: at most once every ten minutes,
    /// across crashes and restarts (D9). The value of <c>BackgroundUpdates.CheckInterval</c>.
    /// </summary>
    public static TimeSpan CheckInterval => TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long each relay's answer to "ready to end?" is waited for, and how long the
    /// relays are given to end: 10 s. The value of <c>BackgroundUpdates.ReadyToEndBound</c>.
    /// </summary>
    public static TimeSpan ReadyToEndBound => TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long the toast's activator waits for COM to hand it the click: 10 s. The
    /// value of <c>ToastActivation.ActivationBound</c>.
    /// </summary>
    public static TimeSpan ToastActivationBound => TimeSpan.FromSeconds(10);

    /// <summary>
    /// How often the ready toast's countdown is written: once a second. The value of
    /// <c>UpdateToasts.Tick</c>.
    /// </summary>
    public static TimeSpan ToastTick => TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long a caller waits for Windows to answer one toast call: 10 s. The value of
    /// <c>WindowsToastSurface.CallBound</c>.
    /// </summary>
    public static TimeSpan ToastCallBound => TimeSpan.FromSeconds(10);

    /// <summary>The product's own four, and the only set it ever runs with.</summary>
    public static UpdateBudgets Default { get; } = new()
    {
        Check = CheckBudget,
        Absolute = AbsoluteBudget,
        Stall = StallBudget,
        Tripwire = CrashTripwire,
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
