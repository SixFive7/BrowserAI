// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Registration;
using Microsoft.Extensions.Logging;

namespace BrowserAI.App.Page;

/// <summary>What the page asks of the registration machinery.</summary>
/// <remarks>
/// <b>A seam, so that the suite drives every registration click with a stand-in
/// and starts no client and no RegisterAI.</b> The product's is
/// <see cref="RegisterAiPageRegistration"/>.
/// </remarks>
internal interface IPageRegistration
{
    /// <summary>Reads every client's registration, and the server a registration names.</summary>
    /// <param name="cancellationToken">Ends the read.</param>
    /// <returns>What is true now.</returns>
    Task<AppState> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Registers one client for all the person's projects, rewriting an entry of ours.</summary>
    /// <param name="who">The client.</param>
    /// <param name="cancellationToken">Ends the wait.</param>
    /// <returns>What the pass concluded.</returns>
    Task<RegistrationReport> RegisterAsync(RegistrationClient who, CancellationToken cancellationToken);

    /// <summary>Removes one client's registration for all the person's projects.</summary>
    /// <param name="who">The client.</param>
    /// <param name="cancellationToken">Ends the wait.</param>
    /// <returns>What the pass concluded.</returns>
    Task<RegistrationReport> UnregisterAsync(RegistrationClient who, CancellationToken cancellationToken);

    /// <summary>Registers one client in one project folder.</summary>
    /// <param name="who">The client.</param>
    /// <param name="folder">The project folder a person picked.</param>
    /// <param name="command">The command the project entry names.</param>
    /// <param name="cancellationToken">Ends the wait.</param>
    /// <returns>What the pass concluded.</returns>
    Task<RegistrationReport> RegisterInProjectAsync(RegistrationClient who, string folder, string command, CancellationToken cancellationToken);

    /// <summary>Removes one client's entry of ours from one project folder.</summary>
    /// <param name="who">The client.</param>
    /// <param name="folder">The project folder.</param>
    /// <param name="cancellationToken">Ends the wait.</param>
    /// <returns>What the pass concluded.</returns>
    Task<RegistrationReport> UnregisterFromProjectAsync(RegistrationClient who, string folder, CancellationToken cancellationToken);
}

/// <summary>
/// The page's registration in the product: RegisterAI, beside the running image.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every call is the one the configuration window made, moved off a click
/// handler.</b> <see cref="McpRegistrar.Apply(RegistrationClient, RegistrationIntent, string?, IRegisterAi, ILogger, bool)"/>
/// and <see cref="McpRegistrar.ApplyToProject(RegistrationClient, bool, string, string?, IRegisterAi, ILogger, string?)"/>
/// start RegisterAI and wait for it within <see cref="McpRegistrar.ToolBudget"/>,
/// so each runs on the thread pool and never on a request thread of Kestrel's
/// that the page is waiting on.
/// </para>
/// <para>
/// <b>A register rewrites an entry of ours that is already there</b>, which is
/// Q347 a's explicit flag and what the window's <i>Register again</i> passed: a
/// person who clicks it wants the product's version back.
/// </para>
/// <para>
/// <b>The image path and the read are handed in</b>, the seam the window's session
/// had since Q289 b, so the suite drives the real registrar against an in-process
/// RegisterAI, a scratch install and a state it built. The product passes
/// <c>Environment.ProcessPath</c> and <see cref="AppState.Read(IRegisterAi, string, string)"/>.
/// </para>
/// </remarks>
/// <param name="tool">RegisterAI.</param>
/// <param name="imagePath">This process's own image, which every registration is judged from.</param>
/// <param name="read">Reads every client's registration; it starts RegisterAI, so it runs on the thread pool.</param>
/// <param name="logger">Where the registrar reports.</param>
internal sealed class RegisterAiPageRegistration(IRegisterAi tool, string? imagePath, Func<AppState> read, ILogger logger) : IPageRegistration
{
    /// <inheritdoc />
    public Task<AppState> ReadAsync(CancellationToken cancellationToken) => Task.Run(read, cancellationToken);

    /// <inheritdoc />
    public Task<RegistrationReport> RegisterAsync(RegistrationClient who, CancellationToken cancellationToken) =>
        Task.Run(() => McpRegistrar.Apply(who, RegistrationIntent.Install, imagePath, tool, logger, replace: true), cancellationToken);

    /// <inheritdoc />
    public Task<RegistrationReport> UnregisterAsync(RegistrationClient who, CancellationToken cancellationToken) =>
        Task.Run(() => McpRegistrar.Apply(who, RegistrationIntent.Uninstall, imagePath, tool, logger), cancellationToken);

    /// <inheritdoc />
    public Task<RegistrationReport> RegisterInProjectAsync(RegistrationClient who, string folder, string command, CancellationToken cancellationToken) =>
        Task.Run(() => McpRegistrar.ApplyToProject(who, register: true, folder, imagePath, tool, logger, command), cancellationToken);

    /// <inheritdoc />
    public Task<RegistrationReport> UnregisterFromProjectAsync(RegistrationClient who, string folder, CancellationToken cancellationToken) =>
        Task.Run(() => McpRegistrar.ApplyToProject(who, register: false, folder, imagePath, tool, logger), cancellationToken);
}

/// <summary>What the page says after a registration action, Q309 b.</summary>
/// <remarks>
/// <para>
/// <b>Q309 b, the maintainer's words verbatim: <i>"Q309 b"</i></b>, which was put to
/// him as <i>"One plain sentence, with the raw text, or Codex's own first error
/// line, under "Show details"."</i> A pass that did what was asked is its own
/// sentence and the client's sentence about when the change is seen. A pass that
/// did not is one plain sentence naming the client and the verb, and the
/// registrar's whole text under <i>Show details</i>, which carries RegisterAI's
/// error, what the client itself printed, and the command to run by hand.
/// </para>
/// <para>
/// <b>Moved from the configuration window with the window's deletion</b>, where
/// the same sentences were its notes; the restart and project hints are the
/// client's own members, as they were there.
/// </para>
/// </remarks>
internal static class RegistrationNotes
{
    /// <summary>The note after a register or unregister for all of a client's projects.</summary>
    /// <param name="report">What the pass concluded.</param>
    /// <param name="who">The client.</param>
    /// <param name="registering">Whether it was a register.</param>
    /// <returns>The note.</returns>
    public static PageNote For(RegistrationReport report, RegistrationClient who, bool registering)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(who);

        return report.Status switch
        {
            RegistrationStatus.Registered or RegistrationStatus.Unregistered => new PageNote($"{report.Detail} {who.RestartHint}"),
            RegistrationStatus.AlreadyRegistered or RegistrationStatus.NothingToUnregister => new PageNote(report.Detail),
            _ => NotDone(report, who, registering ? $"BrowserAI could not register itself with {who.DisplayName}." : $"BrowserAI could not remove itself from {who.DisplayName}."),
        };
    }

    /// <summary>The note after a project registration or removal.</summary>
    /// <param name="report">What the pass concluded.</param>
    /// <param name="who">The client.</param>
    /// <param name="registering">Whether it was a register.</param>
    /// <param name="note">
    /// The client's own sentence about what a project entry names: its
    /// <see cref="ProjectCommand.Note"/>, or else what
    /// <see cref="RegistrationClient.ProjectNoteAfter"/> makes of the file RegisterAI
    /// said the entry resolves to; <see langword="null"/> when there is neither. For
    /// Codex it is where Q314 b's sentence lives: a Codex that was already running
    /// before BrowserAI was installed may need to be restarted to find the server.
    /// </param>
    /// <returns>The note.</returns>
    public static PageNote ForProject(RegistrationReport report, RegistrationClient who, bool registering, string? note)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(who);

        return report.Status switch
        {
            RegistrationStatus.Registered => new PageNote(
                report.Detail + (note is { Length: > 0 } said ? " " + said : string.Empty) + " " + who.ProjectHint + " " + who.RestartHint),
            RegistrationStatus.Unregistered => new PageNote($"{report.Detail} {who.RestartHint}"),
            RegistrationStatus.AlreadyRegistered or RegistrationStatus.NothingToUnregister => new PageNote(report.Detail),
            _ => NotDone(report, who, registering
                ? $"BrowserAI could not register itself in that project for {who.DisplayName}."
                : $"BrowserAI could not remove itself from that project for {who.DisplayName}."),
        };
    }

    private static PageNote NotDone(RegistrationReport report, RegistrationClient who, string failed) => report.Status switch
    {
        RegistrationStatus.ClientNotFound => new PageNote($"{who.DisplayName} was not found on this machine, so nothing was changed.", report.Detail),
        RegistrationStatus.Refused => new PageNote($"BrowserAI changed nothing for {who.DisplayName}.", report.Detail),
        _ => new PageNote(failed, report.Detail),
    };
}
