// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Relay;

/// <summary>
/// What a relay knows about itself and its client before the client says
/// anything, for its greeting to the background and for the sentences it answers
/// with.
/// </summary>
/// <remarks>
/// <b>A fact about a client travels over the pipe and never through the
/// environment</b> ("Settings travel as arguments and over the pipe" in the
/// one-binary plan): a process the Task Scheduler starts never sees a client's
/// environment, measured on 2026-10-04, so the background learns these only from
/// the relay's greeting. The client's name and version are not here because they
/// arrive later, in its <c>initialize</c>.
/// </remarks>
/// <param name="Build">The version this binary was built as: <c>BuildVersion.Current</c>.</param>
/// <param name="RelayPid">This process's id.</param>
/// <param name="ClientPid">The client's process id, the relay's parent, or <see langword="null"/> when it could not be read.</param>
/// <param name="Folder">The relay's working directory, which is the folder the client runs in.</param>
/// <param name="DataRoot">The data root the relay was registered for, and the background has to serve.</param>
/// <param name="LogPath">The log a person reads when the background stops answering, named in the hang sentence.</param>
/// <param name="DeveloperStart">
/// For a build that is not installed, the command that starts its background, which
/// every sentence that would send the person to the Start Menu names instead, since the
/// Start Menu starts the installed build (D11 a); <see langword="null"/> for an installed
/// one. Added 2026-10-10 for round 2 of the texts review, #136 to #139.
/// </param>
internal sealed record RelayFacts(
    string Build,
    int RelayPid,
    int? ClientPid,
    string Folder,
    string DataRoot,
    string LogPath,
    string? DeveloperStart = null);
