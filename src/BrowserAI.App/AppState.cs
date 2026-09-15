// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Registration;
using BrowserAI.Updates;

namespace BrowserAI.App;

/// <summary>
/// Everything the configuration app knows, read and never inferred.
/// </summary>
/// <remarks>
/// <para>
/// <b>One type, two consumers</b>: the dialog renders it and <c>--report</c>
/// serialises it. That is deliberate — a support artifact that described a
/// different state from the window would be worse than no artifact — and it is
/// what makes the window's content assertable without a window.
/// </para>
/// <para>
/// ⚠️ <b>Reading is all it does.</b> A first run shows state; it does not ask,
/// and it does not act. Every edit in this application is behind one explicit
/// click.
/// </para>
/// </remarks>
internal sealed record AppState
{
    /// <summary>The product version, as the binary reports it.</summary>
    public required string Version { get; init; }

    /// <summary>Where this install lives, or <see langword="null"/> when this is not one.</summary>
    public required string? InstallRoot { get; init; }

    /// <summary>Where the browsers, sessions and log live.</summary>
    public required string DataRoot { get; init; }

    /// <summary>
    /// The server this install would register, or <see langword="null"/> when it
    /// refused to compose one.
    /// </summary>
    public required string? ServerCommand { get; init; }

    /// <summary>Why there is no server command, when there is not.</summary>
    public required string? ServerRefusal { get; init; }

    /// <summary>What the client's user-scope configuration says.</summary>
    public required RegistrationView UserScope { get; init; }

    /// <summary>
    /// The nearest project configuration at or above the working directory, or
    /// <see langword="null"/> when there is none.
    /// </summary>
    public required RegistrationView? ProjectScope { get; init; }

    /// <summary>The client executable, or <see langword="null"/> when none was found.</summary>
    public required string? ClientPath { get; init; }

    /// <summary>
    /// What the last update check in THIS session concluded, or
    /// <see langword="null"/> when nothing has been checked.
    /// </summary>
    /// <remarks>
    /// <b>This session and not the log.</b> A dialog that reported a check some
    /// other process made an hour ago would be reporting somebody else's answer
    /// as its own; the button is there for a person who wants to know now.
    /// </remarks>
    public string? LastUpdateCheck { get; init; }

    /// <summary>Whether a client was found to talk to.</summary>
    public bool ClientFound => ClientPath is { Length: > 0 };

    /// <summary>
    /// The one sentence under the heading, which is what a person reads first.
    /// </summary>
    /// <remarks>
    /// <b>The order is the order of what a person can do about it.</b> A machine
    /// with no client cannot be registered at all, so that is said before
    /// anything about registration; a foreign entry is said with its path,
    /// because the only way out of it is for a person to decide which install
    /// they meant.
    /// </remarks>
    public string StatusSentence()
    {
        if (!ClientFound)
        {
            return "Claude Code was not found on this machine, so BrowserAI has not been registered with anything.";
        }

        if (UserScope.Unreadable is { } unreadable)
        {
            return unreadable;
        }

        return UserScope.Ownership switch
        {
            RegistrationOwnership.OursAndPresent =>
                "Registered for all your Claude Code projects.",
            RegistrationOwnership.OursAndStale =>
                $"Registered, but the entry names '{UserScope.Command}', which is not there any more. Register again to repair it.",
            RegistrationOwnership.Foreign =>
                $"Another BrowserAI is registered at '{UserScope.Command}'. Nothing here will change it.",
            _ => "Not registered for your Claude Code projects.",
        };
    }

    /// <summary>
    /// Whether the <i>register</i> action is offered, as opposed to
    /// <i>unregister</i> or nothing at all.
    /// </summary>
    public bool MayRegister =>
        ClientFound
        && ServerCommand is not null
        && UserScope.Unreadable is null
        && UserScope.Ownership is RegistrationOwnership.Absent or RegistrationOwnership.OursAndStale;

    /// <summary>Whether the <i>unregister</i> action is offered.</summary>
    /// <remarks>
    /// <b>Only for an entry we wrote.</b> A foreign one is never removed — that
    /// is somebody else's install and removing it would be this product
    /// uninstalling another.
    /// </remarks>
    public bool MayUnregister =>
        ClientFound && UserScope.Ownership is RegistrationOwnership.OursAndPresent;

    /// <summary>Reads the whole state.</summary>
    /// <param name="commands">The seam over starting the client.</param>
    /// <param name="workingDirectory">Where the search for a project file starts.</param>
    /// <returns>What is true right now.</returns>
    public static AppState Read(IRegistrationCommand commands, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var installRoot = InstallLocation.RootAppDir;
        var resolved = RegistrationTarget.TryResolve(Environment.ProcessPath, out var target, out var refusal);

        return new AppState
        {
            Version = BuildVersion.Current,
            InstallRoot = installRoot,
            DataRoot = new LocalAppDataPaths(LocalAppDataPaths.Overridden()).RootAppDir,
            ServerCommand = resolved ? target!.Command : null,
            ServerRefusal = resolved ? null : refusal,
            UserScope = McpRegistryView.User(installRoot ?? target?.InstallRoot),
            ProjectScope = NearestProject(workingDirectory, installRoot ?? target?.InstallRoot),
            ClientPath = commands.Locate(McpClientRegistration.ClientExecutable),
        };
    }

    /// <summary>
    /// The nearest <c>.mcp.json</c> at or above a directory.
    /// </summary>
    /// <param name="start">Where to start looking.</param>
    /// <param name="installRoot">The root ownership is judged against.</param>
    /// <returns>What it says, or <see langword="null"/> when there is no such file.</returns>
    /// <remarks>
    /// <b>Upward, because that is how the client finds one.</b> A person running
    /// this from inside a repository expects it to report that repository's
    /// file, and a person running it from the Start Menu — whose working
    /// directory is the install root — expects it to report nothing, which is
    /// what an upward walk from there answers.
    /// </remarks>
    public static RegistrationView? NearestProject(string start, string? installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(start);

        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(McpRegistryView.ProjectConfigFile(directory.FullName)))
            {
                return McpRegistryView.Project(directory.FullName, installRoot);
            }
        }

        return null;
    }
}
