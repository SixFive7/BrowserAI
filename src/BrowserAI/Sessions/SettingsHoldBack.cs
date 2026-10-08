// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Json.Nodes;
using BrowserAI.Runtime;

namespace BrowserAI.Sessions;

/// <summary>
/// The per-run settings <c>browserai_init</c> and <c>browserai_resume</c> take, as a
/// call spells them.
/// </summary>
/// <remarks>
/// <b>F2, decided 2026-10-08 by the maintainer</b>: four of them are stated on every
/// call, because a person notices each one, and the other five keep this machine's
/// defaults when a call leaves them out. <see cref="All"/> is also the order a
/// held-back call lists its differences in.
/// </remarks>
internal static class RunSettingNames
{
    /// <summary>Whether the browser has a window.</summary>
    public const string Headed = "headed";

    /// <summary>Whether upstream writes <c>session.md</c>.</summary>
    public const string Transcript = "transcript";

    /// <summary>Whether the run writes an HTTP Archive.</summary>
    public const string CaptureNetwork = "captureNetwork";

    /// <summary>The window's size.</summary>
    public const string Viewport = "viewport";

    /// <summary>The locale the browser reports.</summary>
    public const string Locale = "locale";

    /// <summary>The time zone the browser reports.</summary>
    public const string TimeZone = "timezone";

    /// <summary>Whether TLS errors are passed over.</summary>
    public const string IgnoreHttpsErrors = "ignoreHTTPSErrors";

    /// <summary>Whether the session's own log is at debug level.</summary>
    public const string Debug = "debug";

    /// <summary>
    /// The four every call states: a window on the person's screen, what is written to
    /// disk in plain text, and how long the browser stays open and holds updates back.
    /// </summary>
    /// <remarks>
    /// <b>F2 a then d, the maintainer's words of 2026-10-07, verbatim:</b> <i>"What if
    /// we make all the init and resume parameters mandetory and then go withpattern
    /// b."</i>, narrowed by the proposal he took on 2026-10-08 to these four: the
    /// others' defaults are this machine's own, and a model told to state them
    /// invents values that change what a site serves.
    /// </remarks>
    public static IReadOnlyList<string> Stated { get; } = [Headed, Transcript, CaptureNetwork, IdleSetting.ParameterName];

    /// <summary>Every per-run setting, in the order a held-back call lists them.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Headed, Transcript, CaptureNetwork, IdleSetting.ParameterName, Viewport, Locale, TimeZone, IgnoreHttpsErrors, Debug];
}

/// <summary>One setting a call asks for that differs from what the session's last run used.</summary>
/// <param name="Name">The setting, as a call spells it.</param>
/// <param name="LastRun">What the last run used, as a call would write it.</param>
/// <param name="Asked">What this call asks for, as a call would write it.</param>
/// <param name="LeftOut">Whether the call left the setting out, so that it counts as its default.</param>
internal sealed record SettingDifference(string Name, string LastRun, string Asked, bool LeftOut);

/// <summary>Where a held-back resume finds the session it names.</summary>
internal enum ResumeFinds
{
    /// <summary>Not open in this BrowserAI, or open with its browser closed or its browser server gone.</summary>
    NotLive,

    /// <summary>Live in this BrowserAI, and its browser has not started yet.</summary>
    LiveWithNoBrowserYet,

    /// <summary>Live in this BrowserAI, with its browser up.</summary>
    LiveWithItsBrowserUp,
}

/// <summary>
/// The call that changes a session's settings, or sets a longer idle time than the
/// default, held back once: what differs, and what is said.
/// </summary>
/// <remarks>
/// <para>
/// <b>F2 d, decided 2026-10-08 by the maintainer, in his words verbatim:</b> <i>"f2 d -
/// so we need to store this in the session. Also, explain in the hold text what
/// parameter is different, what the previous values was and what the newly requested
/// value was. The agent can then do 1 of 3 things: request the original/lastrun
/// parameter set with an instant ok, request the same changed parameter set as the
/// last call with an instant ok, or request a new parameter set with a similar single
/// refuse message again."</i> The resolutions of the same day settle the rest: every
/// per-run setting is compared, an optional one a call leaves out counts as its
/// default and the text says so, "the same changed set" is the call held back just
/// before on the same connection for the same session, and an idle time longer than
/// its mode's default is held back with a strong warning that updates wait.
/// </para>
/// <para>
/// <b>What the text has to make plain is that nothing is wrong</b>, his words of
/// 2026-10-07: <i>"But do make sure to communicate clearly to the llm that the first
/// call did not work but that the second call will work. I want to prevent the calling
/// agent from thinking the parameters are wrong on the first refusal and it thinking it
/// should work differently."</i> So every hold-back opens with "Not done yet, and
/// nothing in this call is wrong", and names the call that goes through.
/// </para>
/// <para>
/// ⚠️ <b>It is the one place BrowserAI asks a caller to confirm anything</b>, and the
/// design rule against that gives way here by his decision: D2 b, then F2 d. No flag
/// confirms it; the same call sent again does, which is why
/// <c>ModelSurfaceTests.NoAuthoredToolAsksTheCallerToConfirmAnything</c> still holds.
/// </para>
/// </remarks>
internal static class SettingsHoldBack
{
    /// <summary>
    /// The line every answer that opens a visible window ends with.
    /// </summary>
    /// <remarks>
    /// <b>F5 a, decided 2026-10-08 by the maintainer</b>, realising E1, his words of
    /// 2026-10-07: <i>"Basically I want to hint towards the flow of having only a small
    /// section of the browseruse be interactive with the user (say the login) and teach
    /// the model that it can then immediately after make it a headless session."</i> The
    /// sentence is the one the proposal he took wrote, word for word.
    /// </remarks>
    public const string HeadedHint = "When the part that needs the person is done, resuming with headed: false keeps everything.";

    /// <summary>The sentence every hold-back opens with.</summary>
    public const string NothingIsWrong = "Not done yet, and nothing in this call is wrong.";

    /// <summary>Every setting the call asks for that differs from the last run's, in the order they are listed.</summary>
    /// <param name="lastRun">What the session's last run used.</param>
    /// <param name="asked">What this call asks for, a left-out optional setting at its default.</param>
    /// <param name="arguments">The call's arguments, which say what it left out.</param>
    /// <returns>The differences; empty when the call asks for what the last run used.</returns>
    public static IReadOnlyList<SettingDifference> Differences(SessionRunSettings lastRun, SessionRunSettings asked, JsonObject? arguments)
    {
        ArgumentNullException.ThrowIfNull(lastRun);
        ArgumentNullException.ThrowIfNull(asked);

        var differences = new List<SettingDifference>();

        foreach (var name in RunSettingNames.All)
        {
            var was = Shown(lastRun, name);
            var now = Shown(asked, name);

            if (!string.Equals(was, now, StringComparison.Ordinal))
            {
                differences.Add(new SettingDifference(name, was, now, LeftOut(arguments, name)));
            }
        }

        return differences;
    }

    /// <summary>One setting's value, written the way a call writes it.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="name">The setting.</param>
    /// <returns>The value: <c>true</c>, <c>10</c>, <c>"never"</c>, <c>'1280x720'</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="name"/> is no per-run setting.</exception>
    public static string Shown(SessionRunSettings settings, string name)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return name switch
        {
            RunSettingNames.Headed => Flag(settings.Headed),
            RunSettingNames.Transcript => Flag(settings.Transcript),
            RunSettingNames.CaptureNetwork => Flag(settings.Run.CaptureNetwork),
            IdleSetting.ParameterName => settings.Idle.IsNever ? $"\"{IdleSetting.NeverWord}\"" : settings.Idle.ToString(),
            RunSettingNames.Viewport => Quoted(settings.Run.Viewport.ToString()),
            RunSettingNames.Locale => Quoted(settings.Run.Locale),
            RunSettingNames.TimeZone => settings.Run.TimeZone is { } zone ? Quoted(zone) : "the browser's own",
            RunSettingNames.IgnoreHttpsErrors => Flag(settings.Run.IgnoreHttpsErrors),
            RunSettingNames.Debug => Flag(settings.Debug),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Not a per-run setting."),
        };
    }

    /// <summary>
    /// The strong warning a longer idle time is held back with: BrowserAI's updates wait
    /// for as long as the browser stays open.
    /// </summary>
    /// <remarks>
    /// <b>E2, the maintainer's words of 2026-10-07, verbatim:</b> <i>"Changing the
    /// default timeout to something longer than 10 min or 1 hour for headless and headed
    /// should come with a warning for the agent. Maybe using the same pattern of refusal
    /// with clear instructions that re-calling with the same parameters will work if the
    /// agent is really sure. This warning should be strong about blockign updates."</i>
    /// </remarks>
    /// <param name="asked">What the call asks for.</param>
    /// <returns>The warning, one paragraph.</returns>
    public static string UpdatesWait(SessionRunSettings asked)
    {
        ArgumentNullException.ThrowIfNull(asked);

        var usual = IdleSetting.DefaultFor(asked.Headed).InWords();
        var mode = asked.Headed ? $"a visible window closes after {usual}" : $"a browser with no window closes after {usual}";

        return asked.Idle.IsNever
            ? $"UPDATES WAIT WHILE THIS BROWSER IS OPEN. {IdleSetting.ParameterName}: \"{IdleSetting.NeverWord}\" means BrowserAI never closes it for being idle, where {mode} by default. "
                + $"BrowserAI cannot install an update while a session's browser is open, so every update waits until {SessionToolSurface.Close} closes this one{(asked.Headed ? " or the person closes its window" : string.Empty)}."
            : $"UPDATES WAIT WHILE THIS BROWSER IS OPEN. {IdleSetting.ParameterName}: {asked.Idle} is longer than the default: {mode}. "
                + $"BrowserAI cannot install an update while a session's browser is open, so every update waits until this one closes: after {asked.Idle.InWords()} in which no call names the session{(asked.Headed ? " and nobody uses its window" : string.Empty)}, or when {SessionToolSurface.Close} closes it.";
    }

    /// <summary>
    /// The last run's settings as a call writes them: the four every call states, and
    /// each optional one whose value is not this machine's default.
    /// </summary>
    /// <remarks>
    /// <b>Only the optional settings that need saying</b>: one a call leaves out counts as
    /// its default, so a last run at the default needs nothing. A time zone this machine
    /// cannot name has no value a call can write, and is left out of the list.
    /// </remarks>
    /// <param name="lastRun">The last run's settings.</param>
    /// <returns>For example <c>headed: false, transcript: false, captureNetwork: false, idleMinutes: 10, viewport: '1920x1080'</c>.</returns>
    public static string LastRunAsACall(SessionRunSettings lastRun)
    {
        ArgumentNullException.ThrowIfNull(lastRun);

        var defaults = new SessionRunSettings(false, false, false, RunOptions.Default, lastRun.Idle);
        var said = new List<string>();

        foreach (var name in RunSettingNames.All)
        {
            var value = Shown(lastRun, name);

            if (RunSettingNames.Stated.Contains(name)
                || (!string.Equals(value, Shown(defaults, name), StringComparison.Ordinal)
                    && !(name is RunSettingNames.TimeZone && lastRun.Run.TimeZone is null)))
            {
                said.Add($"{name}: {value}");
            }
        }

        return string.Join(", ", said);
    }

    private static bool LeftOut(JsonObject? arguments, string name) => arguments?[name] is null;

    private static string Flag(bool value) => value ? "true" : "false";

    // A value a caller wrote, quoted back into a model's context, so it is escaped.
    private static string Quoted(string value) => $"'{RecordText.Escape(value)}'";
}

/// <summary>
/// The calls held back on one connection, one per tool and session: the call that,
/// sent again unchanged, goes through.
/// </summary>
/// <remarks>
/// <para>
/// <b>RESOLUTIONS 5 of 2026-10-08</b>: "the same changed set as the last call" is the
/// call held back just before on the same connection, for the same session, and any
/// other connection meets its own hold-back. So the memory belongs to the connection
/// and ends with it.
/// </para>
/// <para>
/// <b>One call per tool and session, the newest</b>: a call held back replaces the one
/// held before it, so a new set is held once again (F2 d's third way on), and a call
/// that goes through clears it, so a change made, undone and asked for again is a
/// choice made again.
/// </para>
/// </remarks>
internal sealed class HeldBackCalls
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, SessionRunSettings> _held = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether this call is the one held back just before for the same tool and
    /// session. When it is, it goes through and is forgotten; when it is not, it is the
    /// one held back now.
    /// </summary>
    /// <param name="tool">The tool, <c>browserai_init</c> or <c>browserai_resume</c>.</param>
    /// <param name="session">The session's key.</param>
    /// <param name="asked">What the call asks for.</param>
    /// <returns>Whether it goes through.</returns>
    public bool GoesThrough(string tool, string session, SessionRunSettings asked)
    {
        ArgumentNullException.ThrowIfNull(asked);

        var key = Key(tool, session);

        lock (_gate)
        {
            if (_held.TryGetValue(key, out var held) && held == asked)
            {
                _ = _held.Remove(key);
                return true;
            }

            _held[key] = asked;
            return false;
        }
    }

    /// <summary>Forgets what was held back for a tool and session, because a call went through without it.</summary>
    /// <param name="tool">The tool.</param>
    /// <param name="session">The session's key.</param>
    public void Forget(string tool, string session)
    {
        lock (_gate)
        {
            _ = _held.Remove(Key(tool, session));
        }
    }

    private static string Key(string tool, string session) => string.Create(CultureInfo.InvariantCulture, $"{tool}\n{session}");
}
