// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using System.Text.RegularExpressions;
using BrowserAI.App.Interop;

namespace BrowserAI.App.Page;

/// <summary>What a folder link asks for.</summary>
internal enum LinkVerb
{
    /// <summary>One of BrowserAI's own folders, by its name on the page: the install, the data or the logs.</summary>
    OpenFolder,

    /// <summary>A session's folder, by the opaque name the page gave it.</summary>
    OpenSession,

    /// <summary>Windows' folder picker, for one setup action of one app.</summary>
    Pick,
}

/// <summary>One folder link, as a link start read it and asks the background about it.</summary>
/// <param name="Verb">What it asks for.</param>
/// <param name="Token">The page's token it carries.</param>
/// <param name="Folder">The folder's name, for <see cref="LinkVerb.OpenFolder"/>.</param>
/// <param name="Session">The session's opaque name, for <see cref="LinkVerb.OpenSession"/>.</param>
/// <param name="Action">The setup action, for <see cref="LinkVerb.Pick"/>.</param>
/// <param name="Client">The app's key, for <see cref="LinkVerb.Pick"/>.</param>
internal sealed record LinkAsk(LinkVerb Verb, string Token, string? Folder = null, string? Session = null, string? Action = null, string? Client = null);

/// <summary>What the background answers a folder link with.</summary>
internal abstract record LinkAnswer
{
    private LinkAnswer()
    {
    }

    /// <summary>The link start opens this folder in Explorer itself.</summary>
    /// <param name="Directory">The folder.</param>
    internal sealed record Open(string Directory) : LinkAnswer;

    /// <summary>The link start shows Windows' folder picker itself and sends back what was chosen.</summary>
    /// <param name="Prompt">What the picker says the folder is for.</param>
    /// <param name="Finish">What the background does with the choice, and the sentence it says of it.</param>
    internal sealed record Pick(string Prompt, Func<FolderPick, Task<string>> Finish) : LinkAnswer;

    /// <summary>Nothing is opened, and why.</summary>
    /// <param name="Sentence">The reason, for the log of the background and of the link start.</param>
    internal sealed record Refused(string Sentence) : LinkAnswer;
}

/// <summary>The scheme and the token a page's folder links carry, while one page is drawn.</summary>
/// <param name="Scheme">The scheme this install registered, <c>browserai</c> for the shipping install.</param>
/// <param name="Token">The token of the listener the page is drawn for.</param>
internal sealed record PageLinkContext(string Scheme, string Token);

/// <summary>
/// The page's folder links: how a control becomes one, how a link reads back, and the scope
/// a page is drawn in.
/// </summary>
/// <remarks>
/// <para>
/// <b>55 a, with 70 a, 72 a and 75 a, the maintainer's words of 2026-10-11 verbatim:
/// <i>"55 a"</i>, <i>"70 a"</i>, <i>"72 a"</i> and <i>"75 a"</i>.</b> Every folder button on
/// the page is a <c>browserai:</c> link carrying the page's token, and so is each setup
/// button that opens Windows' folder picker. The browser hands the link to Windows, which
/// starts the link start (<c>LinkStart</c>); it asks the background over the pipe whether
/// the token is this page's, and opens the folder, or shows the picker, itself.
/// </para>
/// <para>
/// <b>The controls become links in one place, <see cref="PageContent.Button"/></b>, which
/// every page draws its folder buttons through with the actions <c>open-folder</c> and
/// <c>open-session</c> and its pickers with theirs. A page is drawn inside
/// <see cref="Use"/>, which <see cref="PageContent.Document"/> and
/// <see cref="PageContent.Fragment"/> open from the view, so no composer names the token. A
/// build that registered no scheme draws no links: its buttons post their action, and the
/// background opens the folder, behind the browser.
/// </para>
/// <para>
/// <b>A link names what to open by the page's own names</b>, <c>install</c>, <c>data</c> or
/// <c>logs</c>, a session's opaque name, or an app's key and an action, and never a path: the
/// background turns a name into a folder, as it did for the buttons.
/// </para>
/// </remarks>
internal static partial class PageLinks
{
    /// <summary>The query member a link carries the page's token in.</summary>
    public const string TokenMember = "t";

    private static readonly AsyncLocal<PageLinkContext?> Drawing = new();

    /// <summary>The links of the page being drawn, or <see langword="null"/> when it draws buttons.</summary>
    public static PageLinkContext? Current => Drawing.Value;

    /// <summary>Draws a page with these links, until the scope is disposed.</summary>
    /// <param name="links">The scheme and the token, or <see langword="null"/> for buttons.</param>
    /// <returns>The scope.</returns>
    public static IDisposable Use(PageLinkContext? links)
    {
        var before = Drawing.Value;

        Drawing.Value = links;
        return new Restore(before);
    }

    /// <summary>The verb an action of the page's buttons is as a link, or <see langword="null"/> for one that stays a button.</summary>
    /// <param name="action">The button's action.</param>
    /// <returns>The verb.</returns>
    public static LinkVerb? VerbOf(string action) => action switch
    {
        "open-folder" => LinkVerb.OpenFolder,
        "open-session" => LinkVerb.OpenSession,
        "register-in-project" or "unregister-from-a-project" => LinkVerb.Pick,
        _ => null,
    };

    /// <summary>The link a button becomes, or <see langword="null"/> for a button that stays one.</summary>
    /// <param name="links">The scheme and the token.</param>
    /// <param name="action">The button's action.</param>
    /// <param name="data">What the button posts besides its action.</param>
    /// <returns>The link, not yet encoded for an attribute.</returns>
    public static string? Href(PageLinkContext links, string action, IReadOnlyList<(string Name, string Value)> data)
    {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(data);

        if (VerbOf(action) is not { } verb)
        {
            return null;
        }

        string? valueOf(string name) => data.FirstOrDefault(pair => string.Equals(pair.Name, name, StringComparison.Ordinal)).Value;

        var query = verb switch
        {
            LinkVerb.OpenFolder => valueOf("folder") is { } folder ? "folder=" + folder : null,
            LinkVerb.OpenSession => valueOf("session") is { } session ? "session=" + session : null,
            _ => valueOf("client") is { } client ? "action=" + action + "&client=" + client : null,
        };

        return query is null || !Safe().IsMatch(query.Replace('=', '-').Replace('&', '-'))
            ? null
            : $"{links.Scheme}:{NameOf(verb)}?{query}&{TokenMember}={links.Token}";
    }

    /// <summary>Reads a link a link start was handed, or <see langword="null"/> for one that is not a link of this scheme.</summary>
    /// <remarks>
    /// <b>Strict, since anything can start it</b>: any web page can show a link of this scheme,
    /// and the browser's prompt is all that stands between such a page and a link start. So a
    /// link is read only in the exact shape the page draws, with a slash or two after the colon
    /// or one before the query allowed, as browsers have passed them on; at most
    /// <see cref="Registration.LinkScheme.LongestLink"/> characters, every value letters, digits,
    /// a hyphen or an underscore.
    /// </remarks>
    /// <param name="link">The link, as Windows passed it.</param>
    /// <param name="scheme">This install's scheme.</param>
    /// <returns>What it asks for.</returns>
    public static LinkAsk? Parse(string? link, string scheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);

        if (link is not { Length: > 0 } || link.Length > Registration.LinkScheme.LongestLink || link.Length >= 0)
        {
            return null;
        }

        var match = Shape().Match(link);

        if (!match.Success || !string.Equals(match.Groups["scheme"].Value, scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in match.Groups["query"].Value.Split('&'))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);

            if (equals <= 0 || !values.TryAdd(pair[..equals], pair[(equals + 1)..]))
            {
                return null;
            }
        }

        if (!values.TryGetValue(TokenMember, out var token) || token.Length is 0)
        {
            return null;
        }

        return match.Groups["verb"].Value switch
        {
            "open-folder" when values.Count is 2 && values.TryGetValue("folder", out var folder) => new LinkAsk(LinkVerb.OpenFolder, token, Folder: folder),
            "open-session" when values.Count is 2 && values.TryGetValue("session", out var session) => new LinkAsk(LinkVerb.OpenSession, token, Session: session),
            "pick" when values.Count is 3 && values.TryGetValue("action", out var action) && values.TryGetValue("client", out var client) && VerbOf(action) is LinkVerb.Pick
                => new LinkAsk(LinkVerb.Pick, token, Action: action, Client: client),
            _ => null,
        };
    }

    /// <summary>A verb's name in a link.</summary>
    /// <param name="verb">The verb.</param>
    /// <returns>Its name.</returns>
    public static string NameOf(LinkVerb verb) => verb switch
    {
        LinkVerb.OpenFolder => "open-folder",
        LinkVerb.OpenSession => "open-session",
        _ => "pick",
    };

    /// <summary>A link's text for a log: the token left out.</summary>
    /// <param name="ask">The link.</param>
    /// <returns>The words.</returns>
    public static string Described(LinkAsk ask)
    {
        ArgumentNullException.ThrowIfNull(ask);

        var text = new StringBuilder(NameOf(ask.Verb));

        _ = ask.Verb switch
        {
            LinkVerb.OpenFolder => text.Append(" '").Append(ask.Folder).Append('\''),
            LinkVerb.OpenSession => text.Append(" of session ").Append(ask.Session),
            _ => text.Append(" for ").Append(ask.Action).Append(" in ").Append(ask.Client),
        };

        return text.ToString();
    }

    /// <summary>A link as the page draws it and a link start reads it.</summary>
    [GeneratedRegex(@"^(?<scheme>[A-Za-z][A-Za-z0-9.+-]*):/{0,2}(?<verb>open-folder|open-session|pick)/?\?(?<query>[A-Za-z0-9_=&-]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();

    /// <summary>The characters a value of a link may carry.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Safe();

    private sealed class Restore(PageLinkContext? before) : IDisposable
    {
        public void Dispose() => Drawing.Value = before;
    }
}
