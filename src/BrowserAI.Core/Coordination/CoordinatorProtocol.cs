// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using System.Text.Json;
using BrowserAI.Updates;

namespace BrowserAI.Coordination;

/// <summary>What a second start or a server may ask of the coordinator.</summary>
internal enum CoordinatorVerb
{
    /// <summary>Show your window. A start the person made asks this.</summary>
    Show,

    /// <summary>Look again. A start nobody watches, and a server whose update is blocked, ask this.</summary>
    Recheck,
}

/// <summary>
/// What the coordinator's pipe is called, what it may be asked, how it answers,
/// and the two arguments the coordinator's hidden starts carry.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q284 a, the maintainer's words verbatim: <i>"Q284 a"</i>.</b> The
/// coordinator serves one pipe of its own, created with
/// <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c>, and holding it is what makes a process
/// the coordinator: a second start that cannot create it connects to it instead,
/// learns the coordinator's pid, and hands over. Measured 2026-09-24 over twenty
/// simultaneous starts, three rounds: one winner every round, every other start
/// handed over in 2.8 to 31 ms and learned the winner's pid, and after the winner
/// was killed the next start took over in 0.2 ms
/// ([kb](../../../kb/windows/processes.md#a-second-start-finds-the-first-through-a-pipe-and-learns-its-pid)).
/// </para>
/// <para>
/// <b>Named for the install root the way the census gate is.</b> The census gate is
/// <c>Global\BrowserAI-Live-</c> and the root's key; this is
/// <c>\\.\pipe\BrowserAI-Coordinator-</c> and the same key
/// (<see cref="LiveInstances.RootKeyFor"/>), so one install has one coordinator and
/// two installs of one pack id under two roots have two. A server's own pipe is
/// <c>\\.\pipe\BrowserAI-</c> and a pid, so the two families cannot collide.
/// </para>
/// <para>
/// <b>The framing is a server pipe's</b>: one verb and a newline in, four bytes of
/// little-endian length and that much UTF-8 JSON out, through the same serving
/// loop (<see cref="ServerPipe.OpenNamed(string, IPipeAnswers, Microsoft.Extensions.Logging.ILogger)"/>).
/// </para>
/// </remarks>
internal static class CoordinatorProtocol
{
    /// <summary>What every coordinator pipe's name starts with.</summary>
    public const string NamePrefix = @"\\.\pipe\BrowserAI-Coordinator-";

    /// <summary>The version of this protocol, carried in every acknowledgement.</summary>
    public const int Version = 1;

    /// <summary>The verb a start the person made sends.</summary>
    public const string ShowVerb = "show";

    /// <summary>The verb every other start, and a blocked server, sends.</summary>
    public const string RecheckVerb = "recheck";

    /// <summary>
    /// The argument the per-user logon task starts the app with: the sign-in step.
    /// </summary>
    public const string SignInArgument = "--sign-in";

    /// <summary>
    /// The argument that starts the app hidden, as the coordinator and nothing
    /// else. A blocked server passes it to the logon task, which appends it through
    /// <c>$(Arg0)</c>.
    /// </summary>
    public const string CoordinateArgument = "--coordinate";

    /// <summary>The coordinator's pipe for one install root.</summary>
    /// <param name="installRoot">The install root, or the data root of a process that is not installed.</param>
    /// <returns>The full pipe name.</returns>
    public static string NameFor(string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        return NamePrefix + LiveInstances.RootKeyFor(installRoot);
    }

    /// <summary>A verb as the pipe spells it.</summary>
    /// <param name="verb">The verb.</param>
    /// <returns>Its word.</returns>
    public static string Spelling(CoordinatorVerb verb) => verb is CoordinatorVerb.Show ? ShowVerb : RecheckVerb;

    /// <summary>The verb a request line names, or <see langword="null"/>.</summary>
    /// <param name="line">The request, without its newline.</param>
    /// <returns>The verb.</returns>
    public static CoordinatorVerb? Parse(string line) => line switch
    {
        ShowVerb => CoordinatorVerb.Show,
        RecheckVerb => CoordinatorVerb.Recheck,
        _ => null,
    };

    /// <summary>The bytes a client sends for a verb.</summary>
    /// <param name="verb">The verb.</param>
    /// <returns>Its word and a newline, in ASCII.</returns>
    public static byte[] Request(CoordinatorVerb verb) => Encoding.ASCII.GetBytes(Spelling(verb) + "\n");

    /// <summary>The coordinator's answer: the verb it took, and its pid.</summary>
    /// <param name="verb">The verb it took.</param>
    /// <param name="processId">The coordinator's pid.</param>
    /// <returns>The answer's JSON.</returns>
    public static byte[] Acknowledged(CoordinatorVerb verb, int processId) =>
        ServerPipeProtocol.Json(writer =>
        {
            writer.WriteString(ServerPipeProtocol.Fields.Answer, Spelling(verb));
            writer.WriteNumber(ServerPipeProtocol.Fields.Protocol, Version);
            writer.WriteNumber(ServerPipeProtocol.Fields.ProcessId, processId);
        });

    /// <summary>Reads an answer to a verb.</summary>
    /// <param name="body">The answer's JSON.</param>
    /// <param name="verb">The verb that was sent.</param>
    /// <param name="processId">The pid the coordinator gave, when it acknowledged.</param>
    /// <param name="why">Why it did not acknowledge, when it did not.</param>
    /// <returns>Whether the answer acknowledged that verb.</returns>
    public static bool TryReadAcknowledgement(byte[] body, CoordinatorVerb verb, out int processId, out string why)
    {
        ArgumentNullException.ThrowIfNull(body);

        processId = 0;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var answer = root.TryGetProperty(ServerPipeProtocol.Fields.Answer, out var kind) ? kind.GetString() : null;

            if (answer is ServerPipeProtocol.Answers.Refused)
            {
                why = root.TryGetProperty(ServerPipeProtocol.Fields.Why, out var reason) && reason.GetString() is { } text
                    ? text
                    : $"The coordinator refused '{Spelling(verb)}' and gave no reason.";
                return false;
            }

            if (answer != Spelling(verb)
                || !root.TryGetProperty(ServerPipeProtocol.Fields.ProcessId, out var pid)
                || !pid.TryGetInt32(out processId))
            {
                why = $"The coordinator answered '{Spelling(verb)}' with '{answer ?? "nothing it named"}' and no pid.";
                return false;
            }

            why = string.Empty;
            return true;
        }
        catch (JsonException failure)
        {
            why = $"The coordinator answered '{Spelling(verb)}' with something that is not an answer: {failure.Message}";
            return false;
        }
    }
}
