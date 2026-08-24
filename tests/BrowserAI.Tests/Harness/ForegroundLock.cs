// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Runtime.InteropServices;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Whether a run could have seen a browser take the foreground, which is a
/// property of the machine and never of the product.
/// </summary>
internal enum ForegroundLockVerdict
{
    /// <summary>
    /// Windows would not answer, so this run cannot say either way.
    /// </summary>
    Unreadable,

    /// <summary>
    /// The foreground lock never applies, so a steal is visible the moment it
    /// happens.
    /// </summary>
    Unlocked,

    /// <summary>
    /// The lock expires inside the budget an experiment here is given, so a
    /// machine nobody is typing at reaches the moment a steal becomes visible.
    /// </summary>
    Waitable,

    /// <summary>
    /// The lock outlasts any experiment this suite could run, so a focus steal
    /// cannot be observed here at all and a null trial reads as a pass.
    /// </summary>
    Blind,
}

/// <summary>
/// What <c>SPI_GETFOREGROUNDLOCKTIMEOUT</c> says about this machine's ability to
/// answer the focus question at all.
/// </summary>
/// <param name="Timeout">
/// The timeout Windows reported, or <see langword="null"/> when the call failed.
/// </param>
/// <param name="Error">
/// The Win32 error the call left behind, meaningful only when
/// <paramref name="Timeout"/> is <see langword="null"/>.
/// </param>
internal sealed record ForegroundLockReading(TimeSpan? Timeout, int Error);

/// <summary>
/// The one thing in this suite that says <b>which question a green run actually
/// answered</b> about a browser taking the foreground on launch.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem this exists for is the mirror image of the portability rule
/// this repository already keeps.</b> Elsewhere the rule is that a number
/// measured here is not claimed to hold anywhere else. Here it runs the other
/// way: a change that reintroduced focus stealing would be <i>refused by
/// Windows</i> on this machine and would work on a default install, so it
/// passes on the only machine that runs the suite and fails on a user's screen.
/// The local answer is <i>clean</i> rather than <i>unknown</i>, which is the
/// dangerous half.
/// </para>
/// <para>
/// <b>Measured 2026-08-24, and it took four attempts of which three proved
/// nothing.</b> <c>SPI_GETFOREGROUNDLOCKTIMEOUT</c> reads <c>2147483647</c> ms
/// on this machine — about 24.8 days — so Windows refuses a foreground change in
/// the general case and both arms of a focus experiment answer <i>no steal</i>.
/// The single trial that discriminated did so through the lock's own exception:
/// the foreground window belonged to VS Code, an <b>ancestor of the launching
/// process</b>, so the child inherited the right to take the foreground. See
/// [kb](../../../kb/windows/detection.md#this-machines-foreground-lock-is-effectively-infinite-so-it-cannot-see-a-focus-steal--measured-2026-08-24).
/// </para>
/// <para>
/// ⚠️ <b>It reports, and it never repairs.</b> The timeout is a machine-wide
/// user preference; a test that wrote to it would be mutating the developer's
/// desktop to make itself green, and every <c>[MACHINE]</c> measurement already
/// recorded against this machine would stop being comparable with the next one.
/// Nothing here calls <c>SPI_SETFOREGROUNDLOCKTIMEOUT</c>, and nothing here
/// launches anything or touches the foreground: it is one
/// <c>SystemParametersInfoW</c> read and a sentence.
/// </para>
/// <para>
/// <b>A row in the coverage block rather than a
/// <see cref="SuiteCapability"/>, and the distinction is the same one
/// <see cref="FirstRunCache"/> draws.</b> Every capability is something a run
/// can go and produce — publish the slice, assemble the payload, provision a
/// browser, pack a release — so <c>BROWSERAI_RELEASE_RUN=1</c> turning its
/// absence into a failure names a command that fixes it. This is not one: the
/// only way to turn it green is to change a machine-wide setting, which is out
/// of bounds, so a capability would make every release from this machine
/// unreachable with no permitted remedy. It is reported in every run instead,
/// in words, which is what the block is for.
/// </para>
/// </remarks>
internal static partial class ForegroundLock
{
    /// <summary>
    /// <c>SPI_GETFOREGROUNDLOCKTIMEOUT</c>, <c>0x2000</c> — <i>"the amount of
    /// time following user input, in milliseconds, during which the system will
    /// not allow applications to force themselves into the foreground"</i>.
    /// </summary>
    public const uint GetForegroundLockTimeout = 0x2000;

    /// <summary>The label this row carries in the coverage block.</summary>
    public const string Title = "foreground lock";

    /// <summary>The word a run prints when it could not have seen a focus steal.</summary>
    public const string BlindState = "BLIND";

    private static readonly Lazy<ForegroundLockReading> Once = new(Read, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// This run's reading, taken once.
    /// </summary>
    /// <remarks>
    /// Read once for <see cref="SuiteEnvironment.IsReleaseRun"/>'s reason: the
    /// whole run must report one answer rather than whatever the setting said at
    /// the moment each caller asked.
    /// </remarks>
    public static ForegroundLockReading Reading => Once.Value;

    /// <summary>
    /// The longest a focus experiment in this suite could wait for the lock to
    /// expire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Derived rather than written, because the question this type answers is
    /// a comparison against it.</b> The lock expires that many milliseconds
    /// after the last user input, so <i>can this machine discriminate?</i>
    /// reduces to <i>can the lock expire inside the time an experiment here is
    /// allowed to take?</i> — and the only budget in this suite for anything
    /// involving a real browser is <see cref="TestDefaults.BrowserHang"/>.
    /// </para>
    /// <para>
    /// ⚠️ <b>This is a classification threshold and not an assertion over a
    /// measured duration.</b> Nothing here starts a stopwatch, and the house
    /// rule that forbids a number written at an assertion is satisfied the way
    /// it asks to be — by deriving from <c>TestDefaults</c> rather than by
    /// choosing a figure that reads plausible.
    /// </para>
    /// </remarks>
    public static TimeSpan Budget => TestDefaults.BrowserHang;

    /// <summary>The coverage block's rows for this run.</summary>
    public static string CoverageRow => RowFor(Reading);

    /// <summary>
    /// What a timeout means, as a pure function of it and the budget, so that
    /// every band is exercised on every machine.
    /// </summary>
    /// <remarks>
    /// <b>Pure for <see cref="SuiteEnvironment.Decide"/>'s reason exactly.</b>
    /// A machine only ever sits in one band, so a classification written only
    /// against the live reading would be three quarters dead code — and the band
    /// this machine is in is the one that proves nothing.
    /// </remarks>
    /// <param name="timeout">The timeout Windows reported, or <see langword="null"/> when it would not answer.</param>
    /// <param name="budget">The longest an experiment may wait, normally <see cref="Budget"/>.</param>
    /// <returns>What a run may claim about focus.</returns>
    public static ForegroundLockVerdict Classify(TimeSpan? timeout, TimeSpan budget) => timeout switch
    {
        null => ForegroundLockVerdict.Unreadable,
        { Ticks: 0 } => ForegroundLockVerdict.Unlocked,
        var value when value <= budget => ForegroundLockVerdict.Waitable,
        _ => ForegroundLockVerdict.Blind,
    };

    /// <summary>
    /// Asks Windows for the foreground lock timeout.
    /// </summary>
    /// <returns>What it said, or the error it said it with.</returns>
    public static ForegroundLockReading Read()
    {
        var milliseconds = 0u;

        return SystemParametersInfoW(GetForegroundLockTimeout, 0, ref milliseconds, 0)
            ? new ForegroundLockReading(TimeSpan.FromMilliseconds(milliseconds), 0)
            : new ForegroundLockReading(null, Marshal.GetLastPInvokeError());
    }

    /// <summary>
    /// The block's rows for a reading, built here so a synthetic reading and the
    /// live one are written by one implementation.
    /// </summary>
    /// <param name="reading">The reading.</param>
    /// <returns>The row, and the warning beneath it when the run answered nothing.</returns>
    public static string RowFor(ForegroundLockReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        var verdict = Classify(reading.Timeout, Budget);
        var row = "  " + Title.PadRight(20) + StateWord(verdict) + "  " + WitnessFor(reading, verdict);

        // ⚠️ THE SECOND LINE IS THE WHOLE POINT OF THE ROW, and it appears only
        // in the band that cannot answer. A green run that printed the timeout
        // and stopped would still be implying an assurance it does not have; a
        // reader has to be told, in the run's own output, that the question went
        // unanswered and why a null trial here looks exactly like a pass.
        return verdict is not ForegroundLockVerdict.Blind
            ? row
            : row + "\n"
                + "      ⚠️  THIS RUN DID NOT ANSWER whether a browser takes the foreground on launch.\n"
                + "      A focus experiment answers 'no steal' on BOTH arms here unless the foreground\n"
                + "      window belongs to an ancestor of the launching process, so a change that stole\n"
                + "      focus on a default install would read as clean on this machine. It is a blind\n"
                + "      spot in the checking rather than a defect to repair, and nothing here may\n"
                + "      change the setting: see HAZARDS.md and kb/windows/detection.md.";
    }

    /// <summary>The seven-character state the block prints.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <returns>The state word, padded to the width the other rows use.</returns>
    public static string StateWord(ForegroundLockVerdict verdict) => verdict switch
    {
        ForegroundLockVerdict.Unlocked => "CAN SEE",
        ForegroundLockVerdict.Waitable => "IF IDLE",
        ForegroundLockVerdict.Blind => BlindState + "  ",
        _ => "UNREAD ",
    };

    /// <summary>The sentence beside the state, which is where the number lives.</summary>
    /// <param name="reading">The reading.</param>
    /// <param name="verdict">Its verdict.</param>
    /// <returns>The witness.</returns>
    public static string WitnessFor(ForegroundLockReading reading, ForegroundLockVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(reading);

        if (reading.Timeout is not { } timeout)
        {
            return $"SystemParametersInfoW(SPI_GETFOREGROUNDLOCKTIMEOUT) failed with Win32 error {reading.Error.ToString(CultureInfo.InvariantCulture)}";
        }

        var value = $"SPI_GETFOREGROUNDLOCKTIMEOUT is {timeout.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)} ms";

        return verdict switch
        {
            ForegroundLockVerdict.Unlocked =>
                $"{value} — the lock never applies, so a browser taking the foreground is visible here",
            ForegroundLockVerdict.Waitable =>
                $"{value} ({Humanise(timeout)}) — it expires inside the {Humanise(Budget)} an experiment here may take, so an idle machine sees a steal",
            _ =>
                $"{value} ({Humanise(timeout)}) — it outlasts the {Humanise(Budget)} an experiment here may take, so Windows refuses a foreground change in the general case",
        };
    }

    /// <summary>A duration in the largest unit that leaves a number a reader can hold.</summary>
    /// <param name="span">The duration.</param>
    /// <returns>The text.</returns>
    public static string Humanise(TimeSpan span) => span.TotalDays >= 1
        ? $"{span.TotalDays.ToString("F1", CultureInfo.InvariantCulture)} days"
        : span.TotalMinutes >= 1
            ? $"{span.TotalMinutes.ToString("F0", CultureInfo.InvariantCulture)} min"
            : $"{span.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)} s";

    // Reading only. SPI_SETFOREGROUNDLOCKTIMEOUT is deliberately absent: see the
    // remarks above. fWinIni is zero because nothing is being written, so there
    // is no profile to update and no change to broadcast.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoW(uint action, uint parameter, ref uint value, uint update);
}
