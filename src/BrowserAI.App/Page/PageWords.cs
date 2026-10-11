// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Proxy;
using BrowserAI.Updates;

namespace BrowserAI.App.Page;

/// <summary>The page's words for what it shows, said one way on every page.</summary>
/// <remarks>
/// <para>
/// <b>42 a, the maintainer's choice of 2026-10-11</b>, in his words verbatim <i>"40-46
/// your recommendations"</i>: the mockups' words everywhere a person reads. A
/// <b>conversation</b> is a chat in Claude Code or Codex; an <b>app</b> is Claude Code or
/// Codex itself; a <b>session</b> is the folder an agent made; a <b>browser</b> is a
/// session's running browser, hidden or a window on your screen; and an app is <b>set
/// up</b> where it used to be registered. No page calls one thing two of these, says
/// <i>client</i> for an app or <i>driven by</i> for using, or shows a pid outside the
/// details for troubleshooting.
/// </para>
/// <para>
/// <b>The page's alone.</b> The update toast, the log and the status report keep their
/// own words; this decision is about the page.
/// </para>
/// </remarks>
internal static class PageWords
{
    /// <summary>What an app that gave no name is called.</summary>
    public const string UnnamedApp = "Unnamed app";

    /// <summary>The app a client name names: Claude Code, Codex, or the name it gave.</summary>
    /// <param name="clientName">What the client put in <c>clientInfo.name</c>, or <see langword="null"/>.</param>
    /// <returns>The app's name.</returns>
    public static string AppOf(string? clientName) =>
        KnownClients.Matches(clientName, KnownClients.ClaudeCode) ? "Claude Code"
        : KnownClients.Matches(clientName, KnownClients.Codex) ? "Codex"
        : clientName is { Length: > 0 } named ? named
        : UnnamedApp;

    /// <summary>The app and its version, as a line under a conversation says them.</summary>
    /// <param name="clientName">What the client put in <c>clientInfo.name</c>, or <see langword="null"/>.</param>
    /// <param name="version">What it put in <c>clientInfo.version</c>, or <see langword="null"/>.</param>
    /// <returns>The words.</returns>
    public static string AppWithVersion(string? clientName, string? version) =>
        version is { Length: > 0 } && clientName is { Length: > 0 } ? $"{AppOf(clientName)} {version}" : AppOf(clientName);

    /// <summary>Whether a client name is Codex's.</summary>
    /// <param name="clientName">The name.</param>
    /// <returns><see langword="true"/> for Codex.</returns>
    public static bool IsCodex(string? clientName) => KnownClients.Matches(clientName, KnownClients.Codex);

    /// <summary>Where a conversation runs, as its first line says it.</summary>
    /// <param name="clientName">What the client put in <c>clientInfo.name</c>, or <see langword="null"/>.</param>
    /// <param name="window">The VS Code window it is a tab of, or <see langword="null"/>.</param>
    /// <param name="reconnect">What its app needs after an update, which tells a terminal apart.</param>
    /// <returns><i>Claude Code in VS Code</i>, <i>Claude Code in a terminal</i>, <i>Codex</i>, or the app.</returns>
    public static string PlaceOf(string? clientName, ClientWindow? window, RelayReconnect reconnect)
    {
        var app = AppOf(clientName);

        if (!KnownClients.Matches(clientName, KnownClients.ClaudeCode))
        {
            return app;
        }

        // A window is VS Code's alone; a Claude Code that reconnects by itself and has no
        // window may be a print run, so only the terminal is named by its reconnect.
        return window is not null ? $"{app} in VS Code"
            : reconnect is RelayReconnect.McpReconnect ? $"{app} in a terminal"
            : app;
    }

    /// <summary>A conversation as the person sees it called.</summary>
    /// <param name="name">What its app's records call it, or <see langword="null"/>.</param>
    /// <param name="clientName">Its app's client name, for BrowserAI's own words when there is no name.</param>
    /// <param name="folder">Its folder.</param>
    /// <returns>A title in quotes, or BrowserAI's own words for it.</returns>
    public static string ConversationOf(ConversationName? name, string? clientName, string? folder) =>
        (name ?? ClientFolder.Unnamed(AppOf(clientName), folder)).Shown();

    /// <summary>What a conversation's app needs once an update has ended its connection.</summary>
    /// <param name="reconnect">What it needs.</param>
    /// <returns>The sentence.</returns>
    public static string Reconnect(RelayReconnect reconnect) => reconnect switch
    {
        RelayReconnect.None => "Reconnects by itself after an update.",
        RelayReconnect.McpReconnect => "After an update, run /mcp in that terminal, choose BrowserAI, then Reconnect.",
        RelayReconnect.NewConversation => "After an update, BrowserAI is back only in a new conversation.",
        _ => "After an update it may need BrowserAI reconnected: BrowserAI cannot tell whether this app reconnects by itself.",
    };

    /// <summary>A folder's own name, the last part of its path.</summary>
    /// <param name="directory">The folder.</param>
    /// <returns>The name.</returns>
    public static string NameOf(string directory) => ClientFolder.NameOf(directory) ?? directory;

    /// <summary>A size as Windows' Explorer shows one: whole kilobytes and megabytes, and gigabytes to one place.</summary>
    /// <param name="bytes">The size.</param>
    /// <returns>The words.</returns>
    public static string Size(long bytes)
    {
        const double Kilo = 1024;
        const double Mega = Kilo * 1024;
        const double Giga = Mega * 1024;

        return bytes switch
        {
            < 0 => "0 KB",
            < (long)Mega => string.Create(CultureInfo.InvariantCulture, $"{Math.Ceiling(bytes / Kilo):0} KB"),
            < (long)Giga => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(bytes / Mega, MidpointRounding.AwayFromZero):0} MB"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / Giga:0.0} GB"),
        };
    }

    /// <summary>A length of time in whole minutes, as <i>10 min</i>.</summary>
    /// <param name="span">The length.</param>
    /// <returns>The words.</returns>
    public static string Minutes(TimeSpan span) =>
        string.Create(CultureInfo.InvariantCulture, $"{(long)span.TotalMinutes} min");

    /// <summary>A moment as a clock on this PC shows it, to the minute.</summary>
    /// <param name="at">The moment.</param>
    /// <returns>The words.</returns>
    public static string Clock(DateTimeOffset at) => at.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>A moment as a clock on this PC shows it: the time alone on the day the page is drawn, and the date before it on any other.</summary>
    /// <param name="at">The moment.</param>
    /// <param name="now">The moment the page is drawn at.</param>
    /// <returns>The words.</returns>
    public static string When(DateTimeOffset at, DateTimeOffset now) =>
        at.ToLocalTime().Date == now.ToLocalTime().Date
            ? Clock(at)
            : at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>A moment as a clock on this PC shows it, to the second.</summary>
    /// <param name="at">The moment.</param>
    /// <returns>The words.</returns>
    public static string ClockToTheSecond(DateTimeOffset at) => at.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>How the background was started, as the words after <i>started</i> say it.</summary>
    /// <param name="how">The start.</param>
    /// <returns>The words, or <see langword="null"/> when nothing said.</returns>
    public static string? StartedHow(BackgroundStart how) => how switch
    {
        BackgroundStart.SignIn => "at sign-in",
        BackgroundStart.Person => "when you opened BrowserAI",
        BackgroundStart.Installer => "by the installer",
        BackgroundStart.AfterUpdate => "again after an update",
        _ => null,
    };

    /// <summary>Where the updates come from, as the words after <i>from</i> say it.</summary>
    /// <param name="watch">What the update core reports.</param>
    /// <returns>The words, or <see langword="null"/> when there is no source.</returns>
    public static string? SourceOf(UpdateWatch watch) => watch.Source switch
    {
        UpdateSourceKind.GitHubReleases => "GitHub releases",
        UpdateSourceKind.Folder => "a folder on this PC",
        UpdateSourceKind.Url => watch.Location,
        _ => null,
    };

    /// <summary>Where the updates come from, as the object of <i>checked</i> says it.</summary>
    /// <param name="watch">What the update core reports.</param>
    /// <returns>The words.</returns>
    public static string CheckedWhere(UpdateWatch watch) => watch.Source switch
    {
        UpdateSourceKind.GitHubReleases => "GitHub releases",
        UpdateSourceKind.Folder => "its update folder",
        UpdateSourceKind.Url => watch.Location ?? "its update address",
        _ => "its update source",
    };

    /// <summary>One thing or several, counted: <i>1 session folder</i>, <i>3 session folders</i>.</summary>
    /// <param name="count">How many.</param>
    /// <param name="one">The word for one.</param>
    /// <param name="many">The word for several.</param>
    /// <returns>The words.</returns>
    public static string Count(int count, string one, string many) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count is 1 ? one : many)}");
}
