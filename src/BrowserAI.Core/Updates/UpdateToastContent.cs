// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Security;
using System.Text;

namespace BrowserAI.Updates;

/// <summary>What a click on an update toast asks for.</summary>
internal enum ToastAction
{
    /// <summary>Nothing BrowserAI knows.</summary>
    None,

    /// <summary>Open the dashboard's update page: <i>Install now</i>, or a click on the ready toast itself.</summary>
    UpdatePage,

    /// <summary><i>Wait for inactivity</i>: the toast closes and the update installs by itself once BrowserAI is idle.</summary>
    Wait,

    /// <summary>Open the dashboard's changelog page.</summary>
    Changelog,

    /// <summary><i>Dismiss</i>: the toast closes and nothing else happens.</summary>
    Dismiss,
}

/// <summary>One click on an update toast, read back from the arguments it carried.</summary>
/// <param name="Action">What it asks for.</param>
/// <param name="Version">The version the toast named, or <see langword="null"/>.</param>
internal sealed record ToastClick(ToastAction Action, string? Version);

/// <summary>One toast to show.</summary>
/// <param name="Xml">Its content.</param>
/// <param name="Data">The first values of its bound fields, or <see langword="null"/> when it binds none.</param>
/// <param name="SuppressPopup">Whether it goes to the Notification Centre without a banner.</param>
/// <param name="ExpiresOnReboot">Whether Windows removes it at the next restart.</param>
internal sealed record ToastRequest(string Xml, IReadOnlyDictionary<string, string>? Data, bool SuppressPopup, bool ExpiresOnReboot);

/// <summary>Where the ready toast's progress bar stands, carried from one second to the next.</summary>
/// <param name="Ends">The countdown's end the bar was last measured against, or <see langword="null"/>.</param>
/// <param name="Length">How long that countdown had left when the bar started on it.</param>
internal readonly record struct CountdownTrack(DateTimeOffset? Ends, TimeSpan Length);

/// <summary>
/// The update toasts' words and data, as pure functions of what holds the update.
/// </summary>
/// <remarks>
/// <para>
/// <b>Four toasts, decided 2026-10-08 by the maintainer (T):</b> ready, installing,
/// installed and failed. <b>Every one is a reminder</b>, his words verbatim:
/// <i>"Make sure the toasts have no timeout."</i> A reminder stays on screen until
/// the person acts, and the stricter of Microsoft's two pages ignores the scenario
/// unless a button activates in the background, so every button here does.
/// </para>
/// <para>
/// <b>The countdown lives in the progress element's four bound fields</b>, the one
/// form measured on 2026-10-08 to update in place: 60 updates of 60 succeeded,
/// each changing only the progress line, with no new banner and no sound
/// ([kb](../../../kb/windows/notifications.md#a-toast-that-counts-down-in-place-stays-until-acted-on-and-is-replaced----read-and-measured-2026-10-08)). The holders are its title, which changes
/// as agents go idle and browsers close. <b>No plain text line is bound</b>,
/// because that form was not measured: the reconnect line is written when the
/// toast is raised and does not change after.
/// </para>
/// <para>
/// ⚠️ <b>Each toast has a tag of its own, and raising one removes the others
/// first.</b> Measured 2026-10-08: a replacement under the same tag and group is
/// always in place in the Notification Centre, and on screen the old banner goes at
/// once while the new one popped up in only 4 of 6 replacements, with no pattern by
/// order or content. A toast under a tag not yet shown is a first show, and a first
/// show popped up in every run. That removing one tag and showing another pops up
/// every time is the reading, not a measurement.
/// </para>
/// </remarks>
internal static class UpdateToastContent
{
    /// <summary>The group every update toast carries.</summary>
    public const string Group = "update";

    /// <summary>The ready toast's tag.</summary>
    public const string ReadyTag = "ready";

    /// <summary>The installing toast's tag.</summary>
    public const string InstallingTag = "installing";

    /// <summary>The installed toast's tag.</summary>
    public const string InstalledTag = "installed";

    /// <summary>The failed toast's tag.</summary>
    public const string FailedTag = "failed";

    /// <summary>Every update toast's tag, so raising one can remove the others.</summary>
    public static IReadOnlyList<string> Tags { get; } = [ReadyTag, InstallingTag, InstalledTag, FailedTag];

    /// <summary>The bound field the holders are in: the progress element's title.</summary>
    public const string HoldersField = "progressTitle";

    /// <summary>The bound field the bar's fill is in.</summary>
    public const string ValueField = "progressValue";

    /// <summary>The bound field beside the bar: when it installs.</summary>
    public const string WhenField = "progressValueString";

    /// <summary>The bound field under the bar: the countdown.</summary>
    public const string StatusField = "progressStatus";

    private const string ActionKey = "action";
    private const string VersionKey = "version";

    /// <summary>The ready toast.</summary>
    /// <param name="version">The version that waits.</param>
    /// <param name="holds">What holds it when the toast is raised, which the reconnect line is written from.</param>
    /// <returns>Its XML.</returns>
    public static string Ready(string version, UpdateHoldSnapshot holds)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(holds);

        var texts = new List<string>
        {
            $"BrowserAI {version} is ready to install",
            "It installs by itself once BrowserAI has been idle.",
        };

        if (Reconnects(holds) is { } reconnects)
        {
            texts.Add(reconnects);
        }

        return Toast(
            Arguments(ToastAction.UpdatePage, version),
            texts,
            $"<progress title=\"{{{HoldersField}}}\" value=\"{{{ValueField}}}\" valueStringOverride=\"{{{WhenField}}}\" status=\"{{{StatusField}}}\"/>",
            ("Install now", Arguments(ToastAction.UpdatePage, version, "install-now")),
            ("Wait for inactivity", Arguments(ToastAction.Wait, version)));
    }

    /// <summary>The installing toast.</summary>
    /// <param name="version">The version being installed.</param>
    /// <returns>Its XML.</returns>
    public static string Installing(string version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return Toast(
            Arguments(ToastAction.Dismiss, null),
            [$"Installing BrowserAI {version} now", "BrowserAI starts again by itself when it is done."],
            null,
            ("Dismiss", Arguments(ToastAction.Dismiss, null)));
    }

    /// <summary>The installed toast.</summary>
    /// <param name="version">The version now installed.</param>
    /// <returns>Its XML.</returns>
    public static string Installed(string version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return Toast(
            Arguments(ToastAction.Changelog, version),
            [$"BrowserAI {version} is installed"],
            null,
            ("Changelog", Arguments(ToastAction.Changelog, version)),
            ("Dismiss", Arguments(ToastAction.Dismiss, null)));
    }

    /// <summary>The failed toast.</summary>
    /// <param name="version">The version that did not install.</param>
    /// <param name="running">The version still installed, which raises it.</param>
    /// <param name="velopackLog">Where Velopack's log is.</param>
    /// <param name="browserAiLog">Where BrowserAI's log is.</param>
    /// <returns>Its XML.</returns>
    public static string Failed(string version, string running, string velopackLog, string browserAiLog)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(velopackLog);
        ArgumentNullException.ThrowIfNull(browserAiLog);

        return Toast(
            Arguments(ToastAction.Dismiss, null),
            [
                $"The update to {version} failed",
                $"BrowserAI {running} is still installed.",
                $"What happened is in Velopack's log, {velopackLog}, and in BrowserAI's log in {browserAiLog}.",
            ],
            null,
            ("Dismiss", Arguments(ToastAction.Dismiss, null)));
    }

    /// <summary>The reconnects the update will cost, as one sentence, or <see langword="null"/> when it costs none.</summary>
    /// <remarks>
    /// <b>Every relay counts, holding the update or not</b>: each one ends when the
    /// update installs (H1), and the person is told before it does which clients will
    /// not come back by themselves (H1-T a, 2026-10-08).
    /// </remarks>
    /// <param name="holds">What holds the update.</param>
    /// <returns>The sentence.</returns>
    public static string? Reconnects(UpdateHoldSnapshot holds)
    {
        ArgumentNullException.ThrowIfNull(holds);

        var terminals = holds.Relays.Count(relay => relay.Reconnect is RelayReconnect.McpReconnect);
        var codex = holds.Relays.Count(relay => relay.Reconnect is RelayReconnect.NewConversation);
        var unknown = holds.Relays.Count(relay => relay.Reconnect is RelayReconnect.Unknown);
        var parts = new List<string>();

        if (terminals > 0)
        {
            parts.Add(terminals is 1
                ? "1 Claude Code terminal needs /mcp, BrowserAI, Reconnect"
                : $"{terminals} Claude Code terminals need /mcp, BrowserAI, Reconnect");
        }

        if (codex > 0)
        {
            parts.Add(codex is 1
                ? "1 Codex conversation needs a new conversation"
                : $"{codex} Codex conversations need a new conversation");
        }

        if (unknown > 0)
        {
            parts.Add(unknown is 1
                ? "1 client may need BrowserAI reconnected"
                : $"{unknown} clients may need BrowserAI reconnected");
        }

        return parts.Count is 0 ? null : "After the update: " + string.Join("; ", parts) + ".";
    }

    /// <summary>The ready toast's bound fields at one moment.</summary>
    /// <param name="holds">What holds the update.</param>
    /// <param name="now">The moment.</param>
    /// <param name="zone">The time zone the install time is said in.</param>
    /// <param name="track">The bar's state from the last second; the next second's comes back.</param>
    /// <returns>The values, and the bar's state to carry on.</returns>
    public static (IReadOnlyDictionary<string, string> Values, CountdownTrack Track) ReadyData(
        UpdateHoldSnapshot holds,
        DateTimeOffset now,
        TimeZoneInfo zone,
        CountdownTrack track)
    {
        ArgumentNullException.ThrowIfNull(holds);
        ArgumentNullException.ThrowIfNull(zone);

        var reading = holds.WaitAt(now);
        string status;
        string when;
        double value;

        switch (reading)
        {
            case { Wait: UpdateWait.Counting, Ends: { } ends }:
                var left = ends - now;

                // The bar starts again whenever activity moves the end later; an
                // end that moves earlier only jumps the bar forward.
                track = track.Ends is { } previous && ends <= previous && track.Length > TimeSpan.Zero
                    ? new CountdownTrack(ends, track.Length)
                    : new CountdownTrack(ends, left);

                status = $"Installs in {Remaining(left)} if nothing uses it";
                when = "at " + TimeZoneInfo.ConvertTime(ends, zone).ToString("HH:mm", CultureInfo.InvariantCulture);
                value = 1 - (left.TotalMilliseconds / track.Length.TotalMilliseconds);
                break;

            case { Wait: UpdateWait.WindowNeverCloses }:
                (status, when, value) = ("Waits for you to close the window", "no countdown", 0);
                break;

            case { Wait: UpdateWait.SessionNeverCloses }:
                (status, when, value) = ("Waits for an agent to close its session", "no countdown", 0);
                break;

            case { Wait: UpdateWait.CallRunning }:
                (status, when, value) = ("Waits for a running call to finish", "no countdown", 1);
                break;

            case { Wait: UpdateWait.Closing }:
                (status, when, value) = ("Installs once the last browser has closed", "soon", 1);
                break;

            default:
                (status, when, value) = ("Installs in a moment", "now", 1);
                break;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [HoldersField] = Holders(holds, now),
            [StatusField] = status,
            [WhenField] = when,
            [ValueField] = Math.Clamp(value, 0, 1).ToString("0.0000", CultureInfo.InvariantCulture),
        };

        return (values, track);
    }

    /// <summary>How long is left, in whole seconds rounded up: <c>9:30</c>, or <c>1:02:03</c> from an hour on.</summary>
    /// <param name="left">What is left.</param>
    /// <returns>The words.</returns>
    public static string Remaining(TimeSpan left)
    {
        var seconds = left <= TimeSpan.Zero ? 0 : (long)Math.Ceiling(left.TotalSeconds);
        var (hours, rest) = Math.DivRem(seconds, 3600);
        var (minutes, secondsLeft) = Math.DivRem(rest, 60);

        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{secondsLeft:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{secondsLeft:00}");
    }

    /// <summary>The arguments a click carries back to the activator.</summary>
    /// <param name="action">What it asks for.</param>
    /// <param name="version">The version it names, or <see langword="null"/>.</param>
    /// <returns>The arguments.</returns>
    public static string Arguments(ToastAction action, string? version) => Arguments(action, version, null);

    /// <summary>Reads a click back from the arguments its button or the toast itself carried.</summary>
    /// <param name="arguments">The arguments, as Windows hands them to the activator.</param>
    /// <returns>The click.</returns>
    public static ToastClick Parse(string? arguments)
    {
        string? action = null;
        string? version = null;

        foreach (var pair in (arguments ?? string.Empty).Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);

            if (equals <= 0)
            {
                continue;
            }

            var value = Uri.UnescapeDataString(pair[(equals + 1)..]);

            switch (pair[..equals])
            {
                case ActionKey:
                    action = value;
                    break;

                case VersionKey:
                    version = value;
                    break;

                default:
                    break;
            }
        }

        var read = action switch
        {
            "update-page" or "install-now" => ToastAction.UpdatePage,
            "wait" => ToastAction.Wait,
            "changelog" => ToastAction.Changelog,
            "dismiss" => ToastAction.Dismiss,
            _ => ToastAction.None,
        };

        return read is ToastAction.None ? new ToastClick(ToastAction.None, null) : new ToastClick(read, version);
    }

    /// <summary>What uses BrowserAI now, as the progress element's title.</summary>
    /// <param name="holds">What holds the update.</param>
    /// <param name="now">The moment.</param>
    /// <returns>The words.</returns>
    private static string Holders(UpdateHoldSnapshot holds, DateTimeOffset now)
    {
        var agents = holds.HoldingRelaysAt(now).Count();
        var parts = new List<string>();

        if (agents > 0)
        {
            parts.Add(agents is 1 ? "1 agent" : $"{agents} agents");
        }

        if (holds.HiddenSessions.Count > 0)
        {
            parts.Add(holds.HiddenSessions.Count is 1 ? "1 hidden browser" : $"{holds.HiddenSessions.Count} hidden browsers");
        }

        if (holds.VisibleWindows.Count > 0)
        {
            parts.Add(holds.VisibleWindows.Count is 1 ? "1 visible window" : $"{holds.VisibleWindows.Count} visible windows");
        }

        return parts.Count switch
        {
            0 => "Nothing uses BrowserAI now",
            1 => "In use by " + parts[0],
            _ => "In use by " + string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
        };
    }

    /// <summary>The arguments a click carries, with the word it is spelled as.</summary>
    /// <param name="action">What it asks for.</param>
    /// <param name="version">The version it names, or <see langword="null"/>.</param>
    /// <param name="spelling">The word, when it is not the action's own.</param>
    /// <returns>The arguments.</returns>
    private static string Arguments(ToastAction action, string? version, string? spelling)
    {
        var word = spelling ?? action switch
        {
            ToastAction.UpdatePage => "update-page",
            ToastAction.Wait => "wait",
            ToastAction.Changelog => "changelog",
            ToastAction.Dismiss => "dismiss",
            _ => "none",
        };

        return version is null
            ? $"{ActionKey}={word}"
            : $"{ActionKey}={word}&{VersionKey}={Uri.EscapeDataString(version)}";
    }

    /// <summary>One toast's XML: a reminder, its lines, an optional progress element, and its buttons.</summary>
    /// <param name="launch">What a click on the toast itself carries.</param>
    /// <param name="texts">Its top-level lines, the first being its title.</param>
    /// <param name="progress">The progress element, or <see langword="null"/>.</param>
    /// <param name="buttons">Each button's label and arguments.</param>
    /// <returns>The XML.</returns>
    private static string Toast(string launch, IReadOnlyList<string> texts, string? progress, params (string Label, string Arguments)[] buttons)
    {
        var xml = new StringBuilder();

        _ = xml.Append("<toast scenario=\"reminder\" launch=\"").Append(Escape(launch)).Append("\">")
            .Append("<visual><binding template=\"ToastGeneric\">");

        foreach (var text in texts)
        {
            _ = xml.Append("<text>").Append(Escape(text)).Append("</text>");
        }

        _ = xml.Append(progress).Append("</binding></visual><actions>");

        foreach (var (label, arguments) in buttons)
        {
            _ = xml.Append("<action content=\"").Append(Escape(label))
                .Append("\" arguments=\"").Append(Escape(arguments))
                .Append("\" activationType=\"background\"/>");
        }

        return xml.Append("</actions></toast>").ToString();
    }

    private static string Escape(string text) => SecurityElement.Escape(text);
}
