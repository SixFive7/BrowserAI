// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;

namespace BrowserAI.Coordination;

/// <summary>
/// The one pipe the background serves: how it is named, and the two questions a
/// process that is not a relay asks on it.
/// </summary>
/// <remarks>
/// <para>
/// <b>S a, the maintainer's words of 2026-10-08, verbatim: <i>"s a"</i></b>: one
/// resident background per user, install root and data root, from sign-in to
/// sign-out, holding every session, the tab and the update. Every relay, every
/// person's start and the uninstall hook reach it here, and nothing else serves this
/// name: it is created with <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c>, so a second
/// background meets a name already taken and exits, and with the current user's DACL
/// and the refusal of remote clients every pipe of ours has.
/// </para>
/// <para>
/// <b>Newline-delimited JSON-RPC 2.0 both ways, the framing of MCP's stdio
/// transport</b>, so a relay forwards its client's frames to it byte for byte. What
/// BrowserAI says to itself is JSON-RPC as well, under methods that begin
/// <see cref="MethodPrefix"/>, which no client sends. The relay's own methods are
/// <c>Relay.RelayProtocol</c>'s, in the executable that speaks them; the two here are
/// the ones a person's start and the uninstall hook send, from the configuration
/// app's code and the hooks', which that executable links.
/// </para>
/// <para>
/// <b>Named for the data root and, for an install, the install root too</b>, with the
/// key the session lock and the logon task have always used (<see cref="RootKey"/>),
/// so the suite's backgrounds over scratch roots never meet the real install's, and
/// two installs of one pack id under two roots have two. The suite names its own pipe
/// with <see cref="PipeArgument"/>, which no registration and no task ever passes.
/// </para>
/// </remarks>
internal static class BackgroundPipe
{
    /// <summary>What every background pipe's name starts with.</summary>
    public const string NamePrefix = @"\\.\pipe\BrowserAI-Background-";

    /// <summary>
    /// The suite's seam: a pipe named on the command line of a background and of the
    /// relays and starts that reach it, in place of the name the roots compose.
    /// </summary>
    public const string PipeArgument = "--pipe";

    /// <summary>What every method BrowserAI's own processes exchange begins with.</summary>
    public const string MethodPrefix = "browserai/";

    /// <summary>A person's start: hand out a tab, and answer its address.</summary>
    public const string Show = MethodPrefix + "show";

    /// <summary>Close every session cleanly and end: the uninstall hook's, and the suite's.</summary>
    public const string Stop = MethodPrefix + "stop";

    /// <summary>What every request id BrowserAI's own processes put on the pipe begins with.</summary>
    /// <remarks>
    /// <b>A client never writes it</b>, so a response carrying one is the relay's or the
    /// background's own and is never handed to a client or to the MCP server.
    /// </remarks>
    public const string IdPrefix = "browserai-";

    /// <summary>The pipe for one install and one data root.</summary>
    /// <param name="installRoot">The install root, or <see langword="null"/> for a build that is not installed.</param>
    /// <param name="dataRoot">The data root.</param>
    /// <returns>The full pipe name.</returns>
    public static string NameFor(string? installRoot, string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        return installRoot is { Length: > 0 }
            ? $"{NamePrefix}{RootKey.For(installRoot)}-{RootKey.For(dataRoot)}"
            : $"{NamePrefix}{RootKey.For(dataRoot)}";
    }

    /// <summary>Whether a JSON-RPC method is one of BrowserAI's own, never a client's.</summary>
    /// <param name="method">The method, or <see langword="null"/> for a response.</param>
    /// <returns>Whether it begins <see cref="MethodPrefix"/>.</returns>
    public static bool IsOurs(string? method) =>
        method is not null && method.StartsWith(MethodPrefix, StringComparison.Ordinal);
}
