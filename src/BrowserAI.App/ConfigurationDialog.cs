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
    /// <para>
    /// Above 100 so that none of them can collide with the <c>IDOK</c> family,
    /// which is what the stock buttons and the close box report.
    /// </para>
    /// <para>
    /// ⚠️ <b>The four registration verbs are BLOCKS and not single values since
    /// 2026-09-24, because every one of them is now per client.</b> An
    /// identifier is <c>&lt;verb&gt; + &lt;the client's index in
    /// <see cref="AppState.Clients"/>&gt;</c>, the blocks are
    /// <see cref="Slots"/> apart, and <see cref="ClientCommandOf"/> is the only
    /// place that arithmetic happens. The old single <c>Register</c> (103),
    /// <c>Unregister</c> (104) and <c>RegisterInProject</c> (105) are gone rather
    /// than kept as the first client's: a constant that means <i>whichever client
    /// is first</i> is exactly the ambiguity separate control per client exists to
    /// remove.
    /// </para>
    /// </remarks>
    internal static class Command
    {
        /// <summary>Ask the feed whether there is something newer.</summary>
        public const int CheckForUpdates = 101;

        /// <summary>Apply what the check found.</summary>
        public const int ApplyUpdate = 102;

        /// <summary>Open the log directory.</summary>
        public const int OpenLogs = 106;

        /// <summary>How many clients one verb's block has room for.</summary>
        /// <remarks>
        /// <b>Asserted against <see cref="RegistrationClient.All"/>, and never
        /// assumed to be enough</b>: a tenth client would silently make one verb's
        /// last identifier equal to the next verb's first, and a command link that
        /// fired the wrong action is the failure this whole class is numbered to
        /// avoid.
        /// </remarks>
        public const int Slots = 10;

        /// <summary>Register one client at user scope.</summary>
        public const int Register = 110;

        /// <summary>Remove one client's user-scope registration.</summary>
        public const int Unregister = 120;

        /// <summary>Pick a folder and register one client in it.</summary>
        public const int RegisterInProject = 130;

        /// <summary>Remove one client's registration from the project that has one.</summary>
        public const int UnregisterFromProject = 140;

        /// <summary>Every per-client verb, in the order the links are offered.</summary>
        public static IReadOnlyList<int> Verbs { get; } =
            [Register, Unregister, RegisterInProject, UnregisterFromProject];

        /// <summary>The identifier for one verb against one client.</summary>
        /// <param name="verb">The verb's block.</param>
        /// <param name="index">The client's index in <see cref="AppState.Clients"/>.</param>
        /// <returns>The identifier.</returns>
        public static int For(int verb, int index) => verb + index;

        /// <summary>
        /// Which verb and which client an identifier names, or
        /// <see langword="null"/> when it names neither.
        /// </summary>
        /// <param name="id">What the click reported.</param>
        /// <param name="clients">How many clients this window is showing.</param>
        /// <returns>The verb and the client's index, or null.</returns>
        public static (int Verb, int Index)? ClientCommandOf(int id, int clients)
        {
            foreach (var verb in Verbs)
            {
                var index = id - verb;

                if (index >= 0 && index < clients && index < Slots)
                {
                    return (verb, index);
                }
            }

            return null;
        }
    }

    /// <summary>Where the footer link goes.</summary>
    public const string GuideUrl = "https://github.com/SixFive7/BrowserAI#readme";

    /// <summary>
    /// The sentence every action that changes a registration is followed by.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The client's own sentence since 2026-09-24 (previously one constant
    /// naming Claude Code).</b> It was verbatim from the product this one borrows
    /// its model from, and it stays that for Claude Code -- it is the sentence
    /// that stops a person concluding the feature is broken. What changed is that
    /// the unit of staleness is not the same in both clients: Claude Code reads
    /// its configuration when a <b>session</b> starts, Codex when a <b>thread</b>
    /// does, and its desktop app holds a server it has already started. Both
    /// sentences are members of <see cref="RegistrationClient"/>.
    /// </remarks>
    /// <param name="who">The client the action was against.</param>
    /// <returns>The sentence.</returns>
    public static string RestartHintFor(RegistrationClient who)
    {
        ArgumentNullException.ThrowIfNull(who);

        return who.RestartHint;
    }

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

        return $"{heading}\n{state.Headline()}";
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
                "BrowserAI is installed and has registered itself with every MCP client it found. "
                + "Sessions and threads that are already open will not see it until they are started again.");
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

        // ⚠️ EVERY CLIENT IS NAMED HERE, WHETHER OR NOT IT HAS AN ACTION -- 2026-09-24.
        // The instruction carries the sentences and the links carry the actions;
        // without this block a client with nothing to offer would appear nowhere
        // at all, and a person would have no way to tell "Codex is fine" from
        // "BrowserAI has never heard of Codex".
        lines.Add(string.Empty);

        foreach (var client in state.Clients)
        {
            lines.Add($"{client.Client.DisplayName}: {client.StatusSentence()}");

            if (client.ProjectScope is { Command: { Length: > 0 } project } view)
            {
                lines.Add($"    this folder ({view.File}) registers: {project}");
            }
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

        // ⚠️ ONE GROUP PER CLIENT, AND EVERY LABEL NAMES ITS CLIENT -- 2026-09-24.
        // The maintainer, verbatim: "I easy I want separate control over system
        // level registration between codex and claude." So there is no action here
        // that applies to both at once, and no label whose client a person has to
        // infer from where it sits in the list.
        for (var index = 0; index < state.Clients.Count; index++)
        {
            AddClientCommands(commands, state.Clients[index], index);
        }

        commands.Add(new TaskDialogCommand(
            Command.OpenLogs,
            "Open logs\nShows the folder BrowserAI writes its log into."));

        return commands;
    }

    /// <summary>One client's links, in the order they are offered.</summary>
    /// <remarks>
    /// <b>The register link is ONE link whose label follows the state</b>, and
    /// that is how the re-register affordance fits without a second button per
    /// client: absent asks, stale repairs, and present offers to write it again.
    /// The alternative -- a separate <i>register again</i> link -- is a fourth
    /// link per client in a window that has to stay small, for a state in which
    /// the other three already say what is true.
    /// </remarks>
    /// <param name="commands">Where to add them.</param>
    /// <param name="client">The client.</param>
    /// <param name="index">Its index, which is the second half of every identifier.</param>
    private static void AddClientCommands(List<TaskDialogCommand> commands, ClientState client, int index)
    {
        var name = client.Client.DisplayName;

        if (client.MayRegister)
        {
            commands.Add(new TaskDialogCommand(
                Command.For(Command.Register, index),
                client.UserScope.Ownership switch
                {
                    RegistrationOwnership.OursAndStale =>
                        $"Repair the {name} registration\nRe-points that entry at this install, which is what an entry naming a file that is not the server needs.",
                    RegistrationOwnership.OursAndPresent =>
                        $"Register again for all my {name} projects\nRewrites the entry as BrowserAI would write it, which is how you undo your own edits to it.",
                    _ =>
                        $"Register for all my {name} projects\nWrites one entry in your own {name} configuration.",
                }));
        }

        if (client.MayUnregister)
        {
            commands.Add(new TaskDialogCommand(
                Command.For(Command.Unregister, index),
                $"Unregister from {name}\nRemoves that entry. BrowserAI stays installed and your sessions and browsers are untouched."));
        }

        if (client.MayRegisterInProject)
        {
            commands.Add(new TaskDialogCommand(
                Command.For(Command.RegisterInProject, index),
                $"Register in a project for {name}...\nWrites {client.Client.ProjectFileName} in a folder you choose, to be committed with it."));
        }

        if (client.MayUnregisterFromProject)
        {
            commands.Add(new TaskDialogCommand(
                Command.For(Command.UnregisterFromProject, index),
                $"Remove BrowserAI from this project for {name}\nEdits {client.Client.ProjectFileIn(client.ProjectDirectory!)}, which is the registration this folder already has."));
        }
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
    /// <remarks>
    /// ⚠️ <b>The client is a parameter since 2026-09-24</b>, because the sentence
    /// that follows a change is the client's own: see
    /// <see cref="RestartHintFor"/>. A note that told a Codex user to restart a
    /// session would be advice about a thing their client does not have.
    /// </remarks>
    /// <param name="report">The pass.</param>
    /// <param name="who">The client it was against.</param>
    /// <returns>One sentence, followed by the restart hint when anything changed.</returns>
    public static string NoteFor(RegistrationReport report, RegistrationClient who)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(who);

        return report.Status switch
        {
            RegistrationStatus.Registered => $"{report.Detail} {RestartHintFor(who)}",
            RegistrationStatus.Unregistered => $"{report.Detail} {RestartHintFor(who)}",
            RegistrationStatus.AlreadyRegistered => report.Detail,
            RegistrationStatus.NothingToUnregister => report.Detail,
            _ => report.Detail,
        };
    }

    /// <summary>
    /// What is said after a project registration, which adds the client's own
    /// caveat about when a project file is read at all.
    /// </summary>
    /// <param name="report">The pass.</param>
    /// <param name="who">The client it was against.</param>
    /// <param name="absoluteBecause">
    /// Why the command written is this machine's absolute path, or
    /// <see langword="null"/> when it is the portable spelling -- the only form
    /// that resolves on somebody else's machine.
    /// </param>
    /// <returns>The note.</returns>
    public static string ProjectNoteFor(RegistrationReport report, RegistrationClient who, string? absoluteBecause)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(who);

        if (report.Status is not RegistrationStatus.Registered)
        {
            return NoteFor(report, who);
        }

        return report.Detail
            + (absoluteBecause is { Length: > 0 } because ? " " + because : string.Empty)
            + " " + who.ProjectHint
            + " " + RestartHintFor(who);
    }
}
