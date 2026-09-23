// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.App.Ui;
using BrowserAI.Registration;

namespace BrowserAI.App;

/// <summary>Why the dialog was opened, which changes one sentence.</summary>
internal enum Occasion
{
    /// <summary>Somebody opened it.</summary>
    Ordinary,

    /// <summary>The installer started it, once, immediately after installing.</summary>
    FirstRun,

    /// <summary>It came back after applying an update.</summary>
    AfterUpdate,
}

/// <summary>
/// What the window says, as data.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything here is a pure function of an <see cref="AppState"/>.</b> The
/// suite asserts the sentences, the links and the set of buttons without opening
/// anything; what it cannot assert is that Windows draws them, which is the
/// smallest possible remainder.
/// </para>
/// <para>
/// <b>The words about scope are OutlookAI's words, deliberately.</b> <i>"for all
/// my Claude Code projects"</i> and <i>"in a specific project"</i> are what that
/// product's settings page says, they were arrived at by somebody watching
/// people read them, and two products in one estate describing one mechanism
/// differently is how a person learns it twice.
/// </para>
/// </remarks>
internal static class ConfigurationDialog
{
    /// <summary>The window title.</summary>
    public const string Title = "BrowserAI";

    /// <summary>The identifiers a command link reports.</summary>
    /// <remarks>
    /// Above 100 so that none of them can collide with the <c>IDOK</c> family,
    /// which is what the stock buttons and the close box report.
    /// </remarks>
    internal static class Command
    {
        /// <summary>Ask the feed whether there is something newer.</summary>
        public const int CheckForUpdates = 101;

        /// <summary>Apply what the check found.</summary>
        public const int ApplyUpdate = 102;

        /// <summary>Register at user scope.</summary>
        public const int Register = 103;

        /// <summary>Remove the user-scope registration.</summary>
        public const int Unregister = 104;

        /// <summary>Pick a folder and register in it.</summary>
        public const int RegisterInProject = 105;

        /// <summary>Open the log directory.</summary>
        public const int OpenLogs = 106;
    }

    /// <summary>Where the footer link goes.</summary>
    public const string GuideUrl = "https://github.com/SixFive7/BrowserAI#readme";

    /// <summary>
    /// The sentence every action that changes a registration is followed by.
    /// </summary>
    /// <remarks>
    /// <b>Verbatim from the product this one borrows its model from</b>, because
    /// it is the sentence that stops a person concluding the feature is broken.
    /// A client that is already running has already read its configuration.
    /// </remarks>
    public const string RestartHint =
        "Claude Code reads its MCP configuration when a session starts. Sessions already open will not see this change until they are restarted.";

    /// <summary>
    /// What a person is told after a project file is written.
    /// </summary>
    public const string ProjectApprovalHint =
        "Claude Code will ask you to approve this server the first time you open a session in that folder.";

    /// <summary>Builds the page for a state.</summary>
    /// <param name="state">What is true right now.</param>
    /// <param name="occasion">Why the window is open.</param>
    /// <param name="note">
    /// The result of the last action, shown above everything else, or
    /// <see langword="null"/> when nothing has happened yet.
    /// </param>
    /// <param name="updateAvailable">
    /// The version a check found, which turns the update command into an apply.
    /// </param>
    /// <returns>The page.</returns>
    public static TaskDialogPage Page(
        AppState state,
        Occasion occasion,
        string? note = null,
        string? updateAvailable = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new TaskDialogPage
        {
            Title = Title,
            Instruction = Instruction(state, occasion),
            Content = Content(state, occasion, note),
            Footer = $"""<a href="{GuideUrl}">How BrowserAI works</a>""",
            Commands = Commands(state, updateAvailable),
        };
    }

    /// <summary>The large heading: the version, and what state it is in.</summary>
    public static string Instruction(AppState state, Occasion occasion)
    {
        ArgumentNullException.ThrowIfNull(state);

        var heading = occasion is Occasion.AfterUpdate
            ? $"Updated to BrowserAI {state.Version}"
            : $"BrowserAI {state.Version}";

        return $"{heading}\n{state.StatusSentence()}";
    }

    /// <summary>The body: where things are, and what just happened.</summary>
    public static string Content(AppState state, Occasion occasion, string? note)
    {
        ArgumentNullException.ThrowIfNull(state);

        var lines = new List<string>();

        if (note is { Length: > 0 })
        {
            lines.Add(note);
            lines.Add(string.Empty);
        }

        if (occasion is Occasion.FirstRun)
        {
            lines.Add(
                "BrowserAI is installed and has registered itself with Claude Code. "
                + RestartHint);
            lines.Add(string.Empty);
        }

        if (state.InstallRoot is { Length: > 0 } install)
        {
            lines.Add($"""Installed in <a href="folder:{install}">{install}</a>""");
        }
        else
        {
            lines.Add("This is not an installed BrowserAI, so there is nothing to update and no install location to show.");
        }

        lines.Add($"""Browsers, sessions and logs in <a href="folder:{state.DataRoot}">{state.DataRoot}</a>""");

        if (state.ServerCommand is { Length: > 0 } server)
        {
            lines.Add($"Server: {server}");
        }
        else if (state.ServerRefusal is { Length: > 0 } refusal)
        {
            lines.Add(refusal);
        }

        if (state.ProjectScope is { Command: { Length: > 0 } project } view)
        {
            lines.Add($"This folder's project file ({view.File}) registers: {project}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>The command links, in the order they are offered.</summary>
    public static IReadOnlyList<TaskDialogCommand> Commands(AppState state, string? updateAvailable)
    {
        ArgumentNullException.ThrowIfNull(state);

        var commands = new List<TaskDialogCommand>();

        if (state.InstallRoot is { Length: > 0 })
        {
            commands.Add(updateAvailable is { Length: > 0 }
                ? new TaskDialogCommand(
                    Command.ApplyUpdate,
                    $"Install BrowserAI {updateAvailable} now\n"
                    + "BrowserAI will close and reopen. Claude Code sessions using it lose the server until they are restarted.")
                : new TaskDialogCommand(
                    Command.CheckForUpdates,
                    "Check for updates\nAsks the release feed whether there is a newer BrowserAI."));
        }

        if (state.MayRegister)
        {
            commands.Add(new TaskDialogCommand(
                Command.Register,
                "Register for all my Claude Code projects\nWrites one entry in your own Claude Code configuration."));
        }

        if (state.MayUnregister)
        {
            commands.Add(new TaskDialogCommand(
                Command.Unregister,
                "Unregister\nRemoves that entry. BrowserAI stays installed and your sessions and browsers are untouched."));
        }

        if (state.ClientFound && state.ServerCommand is not null)
        {
            commands.Add(new TaskDialogCommand(
                Command.RegisterInProject,
                "Register in a project...\nWrites a .mcp.json in a folder you choose, to be committed with it."));
        }

        commands.Add(new TaskDialogCommand(
            Command.OpenLogs,
            "Open logs\nShows the folder BrowserAI writes its log into."));

        return commands;
    }

    /// <summary>
    /// The prefix a content hyperlink uses to mean <i>open this in Explorer</i>.
    /// </summary>
    /// <remarks>
    /// <b>A scheme of our own, not <c>file:</c></b>, because the handler
    /// is ours: the click is delivered to this process as a string and this
    /// process decides what it means. A <c>file:</c> URL handed to the shell
    /// would open whatever is registered for it, which on some machines is not
    /// Explorer.
    /// </remarks>
    public const string FolderLinkPrefix = "folder:";

    /// <summary>
    /// What a hyperlink asks for: a directory to show, or an address to open.
    /// </summary>
    /// <param name="href">What the click carried.</param>
    /// <returns>The directory, or <see langword="null"/> when this is a URL.</returns>
    public static string? FolderFrom(string href)
    {
        ArgumentNullException.ThrowIfNull(href);

        return href.StartsWith(FolderLinkPrefix, StringComparison.Ordinal)
            ? href[FolderLinkPrefix.Length..]
            : null;
    }

    /// <summary>
    /// What is said after a registration action, given what it concluded.
    /// </summary>
    /// <param name="report">The pass.</param>
    /// <returns>One sentence, followed by the restart hint when anything changed.</returns>
    public static string NoteFor(RegistrationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return report.Status switch
        {
            RegistrationStatus.Registered => $"Registered. {RestartHint}",
            RegistrationStatus.Unregistered => $"Unregistered. {RestartHint}",
            RegistrationStatus.AlreadyRegistered => report.Detail,
            RegistrationStatus.NothingToUnregister => "There was nothing registered to remove.",
            _ => report.Detail,
        };
    }
}
