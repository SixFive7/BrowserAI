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

    /// <summary>
    /// Hand out a tab on the sessions page. What the update toast's <i>Review</i>
    /// asks, the way a Start Menu click asks <see cref="Show"/> (Q339).
    /// </summary>
    Sessions,

    /// <summary>
    /// Start the session host if none runs. A server a client started asks this when
    /// it finds no host to relay to (Q366 b).
    /// </summary>
    Host,
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
    /// <remarks>
    /// <b>2 since 2026-10-03</b> (previously <c>1</c>): the acknowledgement of a
    /// verb that asks for a tab carries the page's address, Q334 a, and
    /// <c>sessions</c> is a third verb. A reader of version 1 ignores the member it
    /// does not know, so an older second start meeting a newer coordinator hands
    /// over as it always did and opens nothing.
    /// </remarks>
    public const int Version = 2;

    /// <summary>The verb the update toast's <i>Review</i> sends: a tab on the sessions page.</summary>
    public const string SessionsVerb = "sessions";

    /// <summary>The member of an acknowledgement that carries the page's address.</summary>
    public const string AddressField = "address";

    /// <summary>
    /// The refusal a coordinator gives a verb asking for a tab once it has decided
    /// to stop.
    /// </summary>
    /// <remarks>
    /// <b>A start that meets it tries again</b>: the coordinator lets its pipe go
    /// moments after deciding, and the next attempt either creates the pipe or meets
    /// the coordinator that did.
    /// </remarks>
    public const string StoppingRefusal = "The coordinator is stopping, so it hands out no page; this start becomes the coordinator once it has gone.";

    /// <summary>
    /// How long a start waits for a verb that asks for a tab: <b>10 s</b>.
    /// </summary>
    /// <remarks>
    /// <b>Longer than <see cref="ServerPipeProtocol.CallBound"/> on purpose.</b>
    /// Such a verb may start the page's listener inside the coordinator before it is
    /// answered, and that is Kestrel starting, not a pipe answering from memory. The
    /// bound is a hang detector for a person's start: a coordinator that has not
    /// answered by then is not starting a listener.
    /// </remarks>
    public static TimeSpan HandOutBound { get; } = TimeSpan.FromSeconds(10);

    /// <summary>Whether a verb asks the coordinator for a tab.</summary>
    /// <param name="verb">The verb.</param>
    /// <returns>Whether its acknowledgement carries an address.</returns>
    public static bool AsksForATab(CoordinatorVerb verb) => verb is CoordinatorVerb.Show or CoordinatorVerb.Sessions;

    /// <summary>The verb a start the person made sends.</summary>
    public const string ShowVerb = "show";

    /// <summary>The verb every other start, and a blocked server, sends.</summary>
    public const string RecheckVerb = "recheck";

    /// <summary>The verb a server sends when it finds no session host to relay to.</summary>
    public const string HostVerb = "host";

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

    /// <summary>
    /// The argument that makes a person's start open the sessions page: what the
    /// update toast's <i>Review</i> starts the app with, the way the Start Menu
    /// starts it with nothing (Q339).
    /// </summary>
    public const string SessionsArgument = "--sessions";

    /// <summary>
    /// The argument that starts the app hidden, as the coordinator, to start the
    /// session host. A server that finds neither a host nor a coordinator passes it to
    /// the logon task, which appends it through <c>$(Arg0)</c> (Q366 b).
    /// </summary>
    public const string StartHostArgument = "--start-host";

    /// <summary>
    /// The refusal a coordinator gives <c>host</c> when it cannot start the session
    /// host, so the server that asked serves its client itself at once and does not
    /// wait for a pipe that will not come.
    /// </summary>
    public const string NoHostRefusal =
        "This coordinator cannot start a session host: it is not an installed BrowserAI, the host would not start, or an update is closing the one it had.";

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
    public static string Spelling(CoordinatorVerb verb) => verb switch
    {
        CoordinatorVerb.Show => ShowVerb,
        CoordinatorVerb.Sessions => SessionsVerb,
        CoordinatorVerb.Host => HostVerb,
        _ => RecheckVerb,
    };

    /// <summary>The verb a request line names, or <see langword="null"/>.</summary>
    /// <param name="line">The request, without its newline.</param>
    /// <returns>The verb.</returns>
    public static CoordinatorVerb? Parse(string line) => line switch
    {
        ShowVerb => CoordinatorVerb.Show,
        RecheckVerb => CoordinatorVerb.Recheck,
        SessionsVerb => CoordinatorVerb.Sessions,
        HostVerb => CoordinatorVerb.Host,
        _ => null,
    };

    /// <summary>The bytes a client sends for a verb.</summary>
    /// <param name="verb">The verb.</param>
    /// <returns>Its word and a newline, in ASCII.</returns>
    public static byte[] Request(CoordinatorVerb verb) => Encoding.ASCII.GetBytes(Spelling(verb) + "\n");

    /// <summary>The coordinator's answer: the verb it took, its pid, and the page's address when it handed one out.</summary>
    /// <param name="verb">The verb it took.</param>
    /// <param name="processId">The coordinator's pid.</param>
    /// <param name="address">The address of the tab it handed out, or <see langword="null"/>.</param>
    /// <returns>The answer's JSON.</returns>
    public static byte[] Acknowledged(CoordinatorVerb verb, int processId, string? address = null) =>
        ServerPipeProtocol.Json(writer =>
        {
            writer.WriteString(ServerPipeProtocol.Fields.Answer, Spelling(verb));
            writer.WriteNumber(ServerPipeProtocol.Fields.Protocol, Version);
            writer.WriteNumber(ServerPipeProtocol.Fields.ProcessId, processId);

            if (address is not null)
            {
                writer.WriteString(AddressField, address);
            }
        });

    /// <summary>Reads an answer to a verb.</summary>
    /// <param name="body">The answer's JSON.</param>
    /// <param name="verb">The verb that was sent.</param>
    /// <param name="processId">The pid the coordinator gave, when it acknowledged.</param>
    /// <param name="why">Why it did not acknowledge, when it did not.</param>
    /// <returns>Whether the answer acknowledged that verb.</returns>
    public static bool TryReadAcknowledgement(byte[] body, CoordinatorVerb verb, out int processId, out string why) =>
        TryReadAcknowledgement(body, verb, out processId, out _, out why);

    /// <summary>Reads an answer to a verb, with the address it carries.</summary>
    /// <param name="body">The answer's JSON.</param>
    /// <param name="verb">The verb that was sent.</param>
    /// <param name="processId">The pid the coordinator gave, when it acknowledged.</param>
    /// <param name="address">The page's address, when the answer carried one.</param>
    /// <param name="why">Why it did not acknowledge, when it did not.</param>
    /// <returns>Whether the answer acknowledged that verb.</returns>
    public static bool TryReadAcknowledgement(byte[] body, CoordinatorVerb verb, out int processId, out string? address, out string why)
    {
        ArgumentNullException.ThrowIfNull(body);

        processId = 0;
        address = null;

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

            address = root.TryGetProperty(AddressField, out var handed) && handed.ValueKind is JsonValueKind.String
                ? handed.GetString()
                : null;
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
