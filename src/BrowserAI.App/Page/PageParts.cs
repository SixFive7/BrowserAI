// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using BrowserAI.Registration;

namespace BrowserAI.App.Page;

/// <summary>The pieces more than one page draws: a session's browser in its three lengths, and an app's state.</summary>
internal static class PageParts
{
    /// <summary>The class a session's browser state is drawn with: <c>w</c>, <c>h</c> or <c>c</c>.</summary>
    /// <param name="session">The session.</param>
    /// <returns>The class.</returns>
    public static string ClassOf(SessionEntry session) => PageBoard.StateOf(session) switch
    {
        BrowserState.Window => "w",
        BrowserState.Hidden => "h",
        _ => "c",
    };

    /// <summary>The icon a session's browser state is drawn with.</summary>
    /// <param name="session">The session.</param>
    /// <returns>The icon's name.</returns>
    public static string IconOf(SessionEntry session) => PageBoard.StateOf(session) switch
    {
        BrowserState.Window => "window",
        BrowserState.Hidden => "hidden",
        _ => "closed",
    };

    /// <summary>A session's browser as a chip on the home's rows.</summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The moment the page is drawn at.</param>
    /// <returns>The HTML.</returns>
    public static string Chip(SessionEntry session, DateTimeOffset now) => PageBoard.StateOf(session) switch
    {
        BrowserState.Window => "<span class=\"chip window\">" + PageIcons.Of("window") + "Window on your screen</span>",
        BrowserState.Hidden => "<span class=\"chip hidden\">" + PageIcons.Of("hidden")
            + (session.IdleCloseAt is { } at && at > now ? "Hidden browser, closes in " + PageContent.Countdown(at, now) : "Hidden browser") + "</span>",
        _ => "<span class=\"chip\">" + PageIcons.Of("closed") + "No browser open</span>",
    };

    /// <summary>A session's browser in a few words, as the map's card says it.</summary>
    /// <param name="session">The session.</param>
    /// <param name="working">Whether a call of the conversation that uses it runs now.</param>
    /// <param name="now">The moment the page is drawn at.</param>
    /// <returns>The HTML.</returns>
    public static string Short(SessionEntry session, bool working, DateTimeOffset now)
    {
        var countdown = Remaining(session, now);

        return PageBoard.StateOf(session) switch
        {
            BrowserState.Window => "Window on your screen, " + (countdown ?? (working ? "in use" : "never closes")),
            BrowserState.Hidden => countdown is { } left ? "Hidden, closes in " + left : working ? "Hidden, in use" : "Hidden, never closes",
            _ => "No browser open",
        };
    }

    /// <summary>A session's browser in a sentence, as a list's row and a session's page say it.</summary>
    /// <param name="session">The session.</param>
    /// <param name="working">Whether a call of the conversation that uses it runs now.</param>
    /// <param name="now">The moment the page is drawn at.</param>
    /// <returns>The HTML.</returns>
    public static string Line(SessionEntry session, bool working, DateTimeOffset now)
    {
        var countdown = Remaining(session, now);

        return PageBoard.StateOf(session) switch
        {
            BrowserState.Window => countdown is { } left
                ? "A window on your screen. It closes by itself in " + left + " if nobody uses it."
                : working ? "A window on your screen, in use now." : "A window on your screen, set never to close by itself.",
            BrowserState.Hidden => countdown is { } left
                ? "A hidden browser. It closes in " + left + " if no call uses it."
                : working ? "A hidden browser, in use now." : "A hidden browser, set never to close by itself.",
            _ => "No browser open. A call that names the session opens it again.",
        };
    }

    /// <summary>How an app is set up, in a few words, with the class its icon is drawn with.</summary>
    /// <param name="client">The app's state as the last read found it.</param>
    /// <returns>The words, the state's class and its icon.</returns>
    public static (string Words, string Class, string Icon) SetupOf(ClientState client)
    {
        ArgumentNullException.ThrowIfNull(client);

        return client.Unanswered is not null ? ("Setup unknown", "warn", "warn")
            : !client.ClientFound ? ("Not found on this PC", "missing", "minus")
            : client.UserScope.Unreadable is not null ? ("Setup unknown", "warn", "warn")
            : client.UserScope.Ownership switch
            {
                RegistrationOwnership.OursAndPresent => ("Set up for all your projects", "ok", "checkCircle"),
                RegistrationOwnership.OursAndStale => ("Needs repair", "warn", "warn"),
                RegistrationOwnership.Foreign => ("Another BrowserAI is set up", "warn", "warn"),
                _ => ("Not set up", "missing", "minus"),
            };
    }

    /// <summary>An app's state in a few words, with its icon, as a line.</summary>
    /// <param name="client">The app's state.</param>
    /// <returns>The HTML.</returns>
    public static string SetupLine(ClientState client)
    {
        var (words, style, icon) = SetupOf(client);

        return "<span class=\"state " + style + "\">" + PageIcons.Of(icon) + PageContent.Text(words) + "</span>";
    }

    /// <summary>Whether an app's state is one a person has to act on: an entry of ours that is wrong, another install's, or one nobody could read.</summary>
    /// <param name="client">The app's state.</param>
    /// <returns><see langword="true"/> when it needs the person.</returns>
    public static bool NeedsYou(ClientState client)
    {
        ArgumentNullException.ThrowIfNull(client);

        return client.ClientFound
            && (client.Unanswered is not null
                || client.UserScope.Unreadable is not null
                || client.UserScope.Ownership is RegistrationOwnership.OursAndStale or RegistrationOwnership.Foreign);
    }

    /// <summary>A session's countdown, when it has one that has not run out.</summary>
    private static string? Remaining(SessionEntry session, DateTimeOffset now) =>
        session.IdleCloseAt is { } at && at > now ? PageContent.Countdown(at, now) : null;

    /// <summary>A list of words joined the way a sentence lists them.</summary>
    /// <param name="words">The words.</param>
    /// <returns>One, two joined by <i>and</i>, or a list with <i>and</i> before the last.</returns>
    public static string Listed(IReadOnlyList<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);

        return words.Count switch
        {
            0 => string.Empty,
            1 => words[0],
            2 => words[0] + " and " + words[1],
            _ => new StringBuilder(string.Join(", ", words.Take(words.Count - 1))).Append(" and ").Append(words[^1]).ToString(),
        };
    }
}
