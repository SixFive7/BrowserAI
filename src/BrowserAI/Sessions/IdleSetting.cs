// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrowserAI.Sessions;

/// <summary>
/// How long a session's browser may sit unused before BrowserAI closes it: a whole
/// number of minutes, or never.
/// </summary>
/// <remarks>
/// <para>
/// <b>E2, decided 2026-10-07 by the maintainer, in his words verbatim:</b> <i>"Right
/// now browsers automatically close after 10 min. of inactivity and interactive
/// windows never do. What if we change the never to 1 hour and then allow the calling
/// agent to change this default behaviour with a parameter? This would allow critical
/// interactive user processes to remain indefinite but have a sensical timeout default
/// for interactive sessions that can be restarted. Basically unlocking automatic
/// updates overnight."</i> And of the two kinds, 2026-10-08: <i>"From the three kinds
/// both the hidden and visible browser sessions have timeouts that can be overriden
/// (with a warning) by the agent."</i> So the setting is the same for both modes, and
/// only its default differs: <see cref="SessionTimes.HiddenIdleMinutes"/> and
/// <see cref="SessionTimes.VisibleIdleMinutes"/>.
/// </para>
/// <para>
/// <b>Minutes or the word "never", as RESOLUTIONS 6 of 2026-10-08 settled it</b>, and
/// both modes may take "never". The upper bound is the type's and no choice of ours: a
/// count of minutes that does not fit a 32-bit integer is refused, and the countdown
/// arms its timer in steps the platform timer can take
/// (<see cref="BrowserIdleTimer"/>).
/// </para>
/// <para>
/// <b>A sealed record and not a struct</b>, so a setting nobody chose cannot read as
/// "never" by being left at its default value.
/// </para>
/// </remarks>
internal sealed record IdleSetting
{
    /// <summary>The argument's name on <c>browserai_init</c> and <c>browserai_resume</c>.</summary>
    public const string ParameterName = "idleMinutes";

    /// <summary>The word for no countdown at all.</summary>
    public const string NeverWord = "never";

    private IdleSetting(int? minutes) => Minutes = minutes;

    /// <summary>The setting that never closes the browser for being idle.</summary>
    public static IdleSetting Never { get; } = new(minutes: null);

    /// <summary>The minutes, or <see langword="null"/> for never.</summary>
    public int? Minutes { get; }

    /// <summary>Whether this setting never closes the browser for being idle.</summary>
    public bool IsNever => Minutes is null;

    /// <summary>A setting of a whole number of minutes.</summary>
    /// <param name="minutes">At least one.</param>
    /// <returns>The setting.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Fewer than one.</exception>
    public static IdleSetting Of(int minutes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minutes, 1);

        return new IdleSetting(minutes);
    }

    /// <summary>The default for a mode: an hour for a visible window, ten minutes without one.</summary>
    /// <param name="headed">Whether the run has a window.</param>
    /// <returns>The default.</returns>
    public static IdleSetting DefaultFor(bool headed) =>
        Of(headed ? SessionTimes.VisibleIdleMinutes : SessionTimes.HiddenIdleMinutes);

    /// <summary>
    /// Whether this setting keeps a browser open longer than its mode's default, which
    /// is what holds BrowserAI's updates back for longer than the default would.
    /// </summary>
    /// <param name="headed">Whether the run has a window.</param>
    /// <returns>Whether it is longer; "never" always is.</returns>
    public bool IsLongerThanTheDefaultFor(bool headed) =>
        Minutes is not { } minutes || minutes > DefaultFor(headed).Minutes;

    /// <summary>The setting as a call spells it: the number, or "never".</summary>
    /// <returns>The value.</returns>
    public override string ToString() =>
        Minutes is { } minutes ? minutes.ToString(CultureInfo.InvariantCulture) : NeverWord;

    /// <summary>The setting in words: "10 minutes", "1 minute" or "never".</summary>
    /// <returns>The words.</returns>
    public string InWords() => Minutes switch
    {
        null => NeverWord,
        1 => "1 minute",
        { } minutes => $"{minutes.ToString(CultureInfo.InvariantCulture)} minutes",
    };

    /// <summary>
    /// The setting a call's argument carries, or <see langword="null"/> when the call
    /// carries none.
    /// </summary>
    /// <remarks>
    /// <b>A named refusal for every wrong shape, never a parse that happens to
    /// work</b>, the rule every argument here keeps: <c>"10"</c> is a string and is
    /// refused, a fraction is refused, and the only word accepted is "never", in any
    /// case.
    /// </remarks>
    /// <param name="arguments">The call's arguments.</param>
    /// <returns>The setting, or <see langword="null"/>.</returns>
    /// <exception cref="SessionToolException">The argument is there and is not a setting.</exception>
    public static IdleSetting? From(JsonObject? arguments)
    {
        if (arguments?[ParameterName] is not { } value || value.GetValueKind() is JsonValueKind.Null)
        {
            return null;
        }

        if (value.GetValueKind() is JsonValueKind.Number
            && value.GetValue<JsonElement>().TryGetInt32(out var minutes)
            && minutes >= 1)
        {
            return Of(minutes);
        }

        if (value.GetValueKind() is JsonValueKind.String
            && string.Equals(value.GetValue<string>(), NeverWord, StringComparison.OrdinalIgnoreCase))
        {
            return Never;
        }

        throw new SessionToolException(
            $"'{ParameterName}' must be a whole number of minutes from 1 to {int.MaxValue.ToString(CultureInfo.InvariantCulture)}, or \"{NeverWord}\", "
            + $"and it arrived as {ArgumentKind.Of(value)}. Nothing was done.");
    }

    // ⚠️ MOVED 2026-10-10: `Shown`, which named a string and a number in words and
    // every other kind by .NET's own name for it ("True", "Object", "Array"), is
    // `ArgumentKind.Of` now, which names every kind in words, and every refusal that
    // says what arrived uses it.
}
