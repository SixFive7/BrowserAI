// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using BrowserAI.Proxy;
using BrowserAI.Registration;

namespace BrowserAI.App.Page;

/// <summary>
/// The page behind the home's <i>Claude Code and Codex</i> card: how each app is set up, the
/// buttons that change it, and what a person troubleshooting needs, folded.
/// </summary>
/// <remarks>
/// <para>
/// <b>The status page's registration, moved behind its card</b> (40 a). <b>Every button names
/// its app</b>, the maintainer's words of 2026-09-24 verbatim: <i>"I easy I want separate
/// control over system level registration between codex and claude."</i> No action here
/// applies to both at once, one runs at a time, and a state offers only what it allows.
/// </para>
/// <para>
/// <b>42 a</b>: an app is <i>set up</i>, never <i>registered</i>, on the page. <b>45 a</b>:
/// where each app was found and what its entry starts are folded under <i>Details for
/// troubleshooting</i>.
/// </para>
/// <para>
/// <b>The words about scope are OutlookAI's</b>, <i>"for all my ... projects"</i> and
/// <i>"in a project"</i>, because two products in one estate describing one mechanism
/// differently is how a person learns it twice.
/// </para>
/// </remarks>
internal static class SetupContent
{
    /// <summary>The setup page's main part.</summary>
    /// <param name="view">What is true now.</param>
    /// <returns>The HTML.</returns>
    public static string Render(PageView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var html = new StringBuilder()
            .Append(PageContent.Crumbs(view.Tab, "Claude Code and Codex"))
            .Append(PageContent.Title(
                "Claude Code and Codex",
                "Your agents reach BrowserAI through these apps, and each is set up on its own. Setting one up for all your projects writes one entry in that app's own settings. A project setup writes a file in one folder, to commit with the project."));

        PageContent.AppendNote(html, view.Note);

        if (view.Registering is { Length: > 0 } working)
        {
            _ = html.Append("<p class=\"working-line\" role=\"status\">").Append(PageContent.Text(working)).Append("</p>\n");
        }

        switch (view.Registration)
        {
            case null:
                _ = html.Append("<p class=\"small\">Reading how BrowserAI is set up in Claude Code and Codex.</p>\n");
                break;

            case { State: null } failed:
                _ = html.Append("<p>BrowserAI could not read how it is set up.</p>\n");
                PageContent.AppendDetails(html, "d-read", failed.Failure);
                _ = html.Append("<p>").Append(PageContent.Button("b-read-registration", "read-registration", "Read it again")).Append("</p>\n");
                break;

            case { State: { } state } read:
                _ = html.Append("<p class=\"small read-at\">").Append(PageContent.Text($"Read at {PageWords.ClockToTheSecond(read.ReadAt)}."))
                    .Append(' ').Append(PageContent.Button("b-read-registration", "read-registration", "Read again", "btn quiet", "refresh")).Append("</p>\n");

                foreach (var client in state.Clients)
                {
                    _ = html.Append(AppCard(client, offerActions: view.Registering is null));
                }

                break;
        }

        return html.ToString();
    }

    /// <summary>The one sentence about how an app is set up, in the page's words.</summary>
    /// <remarks>
    /// <b>The same reading as the line under the home's card</b>, <see cref="PageParts.SetupOf"/>,
    /// in the same order: an app that is not on this PC first, then what its own settings say.
    /// A reason RegisterAI or the reader gave is folded under the details, since it is theirs.
    /// </remarks>
    /// <param name="client">The app's state.</param>
    /// <returns>The sentence.</returns>
    public static string Sentence(ClientState client)
    {
        ArgumentNullException.ThrowIfNull(client);

        var app = client.Client.DisplayName;

        if (client.Unanswered is not null || (client.ClientFound && client.UserScope.Unreadable is not null))
        {
            return $"BrowserAI cannot tell how {app} is set up. The details below say why.";
        }

        if (!client.ClientFound)
        {
            return $"{app} was not found on this PC, so BrowserAI is not set up in it.";
        }

        return client.UserScope.Ownership switch
        {
            RegistrationOwnership.OursAndPresent => $"Every new {app} conversation can use BrowserAI, in any folder.",
            RegistrationOwnership.OursAndStale => $"{app} is set up to start a file that is not this install's BrowserAI, so it cannot reach BrowserAI. Repair it to point it at this install.",
            RegistrationOwnership.Foreign => $"Another BrowserAI is set up in {app}, at {client.UserScope.Command}. Nothing here changes it.",
            _ => $"BrowserAI is not set up for your {app} projects.",
        };
    }

    /// <summary>One app's card: its state, its buttons, its project setup and its details.</summary>
    private static string AppCard(ClientState client, bool offerActions)
    {
        var who = client.Client;
        var name = who.DisplayName;
        var html = new StringBuilder("<article class=\"card app-card\" id=\"client-").Append(PageContent.Text(who.Key)).Append("\">")
            .Append("<div class=\"head\">").Append(PageContent.Badge(who.Key is "codex" ? KnownClients.Codex : KnownClients.ClaudeCode))
            .Append("<h2>").Append(PageContent.Text(name)).Append("</h2>").Append(PageParts.SetupLine(client)).Append("</div>\n")
            .Append("<p>").Append(PageContent.Text(Sentence(client))).Append("</p>\n");

        if (client.ClientFound)
        {
            _ = html.Append("<p class=\"small\">").Append(PageContent.Text(who.Key is "codex" ? PageContent.CodexStartedBeforeTheInstall : PageContent.ClaudeStartedBeforeTheInstall)).Append("</p>\n");
        }

        if (client.ProjectScope is { Command: { Length: > 0 } command } project && client.ProjectDirectory is { Length: > 0 } directory)
        {
            _ = html.Append("<p class=\"small\">The project at <code>").Append(PageContent.Text(directory)).Append("</code> names <code>").Append(PageContent.Text(command))
                .Append("</code> in <code>").Append(PageContent.Text(project.File)).Append("</code>.</p>\n");
        }

        if (offerActions)
        {
            var buttons = new StringBuilder();

            if (client.MayRegister)
            {
                var (label, what) = client.UserScope.Ownership switch
                {
                    RegistrationOwnership.OursAndStale => ($"Repair the {name} setup", "Points that entry at this install."),
                    RegistrationOwnership.OursAndPresent => ($"Set up again for all my {name} projects", "Rewrites the entry as BrowserAI writes it, which undoes your own edits to it."),
                    _ => ($"Set up for all my {name} projects", $"Writes one entry in your own {name} settings."),
                };

                _ = buttons.Append(Action("register", who.Key, label, what, client.UserScope.Ownership is RegistrationOwnership.OursAndStale or RegistrationOwnership.Absent ? "btn primary" : "btn"));
            }

            if (client.MayUnregister)
            {
                _ = buttons.Append(Action("unregister", who.Key, $"Remove BrowserAI from {name}", "Removes that entry. BrowserAI stays installed, and your sessions are untouched.", "btn"));
            }

            if (buttons.Length > 0)
            {
                _ = html.Append("<div class=\"actions\">").Append(buttons).Append("</div>\n");
            }

            if (client.MayRegisterInProject || client.MayUnregisterFromAProject || client.MayUnregisterFromProject)
            {
                _ = html.Append("<div class=\"sub-block\"><h3>In one project</h3>");

                // 75 a, 2026-10-11: on a page drawn with links the folder link's own process
                // shows the picker, and it comes to the front; a button's picker is the
                // background's, which may stay behind, so only the buttons' page warns of it.
                // Corrected 2026-10-12 by the reader's pass (previously "Writes or removes
                // {file} in a folder you choose in Windows' folder picker." on a page drawn with
                // links): each row says what it writes or removes, so no sentence leads them.
                if (PageLinks.Current is null)
                {
                    _ = html.Append("<p>")
                        .Append(PageContent.Text($"Writes or removes {who.ProjectFileName} in a folder you choose. Windows' folder picker opens, and it can appear behind this browser window."))
                        .Append("</p>");
                }

                _ = html.Append("<div class=\"actions\">");

                if (client.MayRegisterInProject)
                {
                    _ = html.Append(Action("register-in-project", who.Key, $"Set up in a project for {name}", $"Writes {who.ProjectFileName} in a folder you choose, to commit with the project.", "btn"));
                }

                if (client.MayUnregisterFromProject)
                {
                    _ = html.Append(Action("unregister-from-project", who.Key, $"Remove BrowserAI from that project for {name}", $"Edits {who.ProjectFileIn(client.ProjectDirectory!)}.", "btn"));
                }

                if (client.MayUnregisterFromAProject)
                {
                    _ = html.Append(Action("unregister-from-a-project", who.Key, $"Remove from a project for {name}", $"Removes BrowserAI's entry from {who.ProjectFileName} in a folder you choose. An entry another install wrote is left alone.", "btn"));
                }

                _ = html.Append("</div></div>\n");
            }
        }

        return html.Append(Trouble(client)).Append("</article>\n").ToString();
    }

    private static string Action(string action, string client, string label, string what, string style) =>
        "<p class=\"action\">" + PageContent.Button("b-" + action + "-" + client, action, label, style, null, ("client", client))
        + " <span class=\"small\">" + PageContent.Text(what) + "</span></p>";

    /// <summary>Where the app was found and what its entries start, folded (45 a).</summary>
    private static string Trouble(ClientState client)
    {
        var lines = new List<string>
        {
            client.ClientPath is { Length: > 0 } found ? $"{client.Client.DisplayName} found at: {found}" : $"{client.Client.DisplayName} was not found on this PC.",
            $"Its settings: {client.UserScope.File}",
            client.UserScope.Command is { Length: > 0 } command ? $"Its entry for BrowserAI starts: {command}" : "Its settings have no entry for BrowserAI.",
        };

        if (client.UserScope.Unreadable is { Length: > 0 } unreadable)
        {
            lines.Add(unreadable);
        }

        if (client.Unanswered is { Length: > 0 } unanswered)
        {
            lines.Add(unanswered);
        }

        if (client.ProjectScope is { } project)
        {
            lines.Add($"The project setup found: {project.File}");
        }

        var id = "d-trouble-" + client.Client.Key;

        return "<details class=\"trouble\" id=\"" + id + "\"><summary id=\"" + id + "-s\">Details for troubleshooting</summary><pre>"
            + PageContent.Lines(string.Join('\n', lines)) + "</pre></details>\n";
    }
}
