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
/// serialises it. That is deliberate -- a support artifact that described a
/// different state from the window would be worse than no artifact -- and it is
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

    /// <summary>
    /// Every MCP client, in <see cref="RegistrationClient.All"/> order, each with
    /// its own state and its own actions.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>A list since 2026-09-24, and there is deliberately no single
    /// <c>UserScope</c>, <c>ProjectScope</c> or <c>ClientPath</c> any more.</b>
    /// Those three read as <i>the</i> client's and silently meant Claude Code's;
    /// keeping them beside the list would be two answers to the same question,
    /// one of which is only ever right by accident. The order is
    /// <see cref="RegistrationClient.All"/>'s, so the record on disk, the report
    /// and the window read the same way.
    /// </remarks>
    public required IReadOnlyList<ClientState> Clients { get; init; }

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

    /// <summary>
    /// The line under the heading: one sentence per client, in order.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Joined and never reduced -- 2026-09-24.</b> Two clients are two
    /// states, and any single sentence about both would have to drop one of them:
    /// a person whose Claude Code registration is fine and whose Codex entry
    /// belongs to another install must be told the second thing, not reassured
    /// about the first. Each client's own sentence is
    /// <see cref="ClientState.StatusSentence"/>, which already names the client it
    /// is about, so the join needs no labels of its own.
    /// </remarks>
    /// <returns>The sentences, in <see cref="Clients"/> order.</returns>
    public string StatusSentence() =>
        string.Join(" ", Clients.Select(client => client.StatusSentence()));

    /// <summary>
    /// Every client's state in two or three words, for the line under the
    /// heading.
    /// </summary>
    /// <remarks>
    /// <b>The heading gets the short form and the body gets the sentences</b>,
    /// because the heading has one line and two clients to fit on it, while the
    /// thing that makes a foreign entry actionable is the path -- and that belongs
    /// where there is room for it.
    /// </remarks>
    /// <returns>One clause per client.</returns>
    public string Headline() =>
        string.Join("   ", Clients.Select(client => $"{client.Client.DisplayName}: {client.ShortStatus}"));

    /// <summary>Whether any client was found to talk to.</summary>
    public bool AnyClientFound => Clients.Any(client => client.ClientFound);

    /// <summary>One client's state, by key.</summary>
    /// <param name="key">The client's <see cref="RegistrationClient.Key"/>.</param>
    /// <returns>That client's state.</returns>
    /// <exception cref="InvalidOperationException">No such client was read.</exception>
    public ClientState For(string key) =>
        Clients.Single(client => string.Equals(client.Client.Key, key, StringComparison.Ordinal));

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
            Clients = [.. RegistrationClient.All.Select(who => ClientState.Read(
                who,
                commands,
                workingDirectory,
                installRoot ?? target?.InstallRoot,
                resolved))],
        };
    }
}
