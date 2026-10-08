// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Proxy;
using BrowserAI.Sessions;

namespace BrowserAI.Relay;

/// <summary>
/// Every sentence the relay answers with in place of the background, one row per
/// condition.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reader is a model, and through it the person at the computer.</b> A tool
/// result is the only channel that reaches the model in both clients, so each row
/// is a tool result's text, and each says three things: what happened to the call,
/// whether any of it may have run, and who does what next. A row that needs a person
/// says so in as many words, because the model cannot fix a stopped background
/// process and must not try. <i>"Only that person can restart it: do not start
/// BrowserAI yourself"</i> went into R's sentence at the maintainer's request
/// (2026-10-08), since nothing technically stops an agent from running the program
/// from a shell and the sentence is the guard.
/// </para>
/// <para>
/// <b>Every public method is provoked by a test</b>, through the engine and a real
/// condition, and <c>RelayTests</c> holds the census the way <c>ErrorCatalogueTests</c>
/// holds <see cref="SessionErrors"/>'s: a row nothing can reach is documentation, not
/// behaviour.
/// </para>
/// <para>
/// <b>Two kinds of call, and the sentence says which.</b> A call the relay never
/// passed on reached nothing, and the row says it was NOT run. A call already passed
/// on to the background may have clicked, typed or navigated before the answer
/// stopped coming, so that row says to check before repeating and never claims that
/// nothing ran.
/// </para>
/// </remarks>
internal static class RelayErrors
{
    /// <summary>Where a person reports a crash or a hang.</summary>
    public const string IssuesUrl = "https://github.com/SixFive7/BrowserAI/issues";

    /// <summary>
    /// The background crashed, and the call was never passed on: R's accepted text,
    /// verbatim.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Said at once, to every call, until the person's Start Menu start clears the
    /// crash record</b> (R, "r ok", 2026-10-08): waiting cannot help, because nothing
    /// restarts a crashed background but that person. It replaced the plan's draft of
    /// 2026-10-04, which counted restarts that no longer happen.
    /// </para>
    /// <para>
    /// The exit code reads <c>unknown</c> when nothing recorded one: a fail-fast leaves
    /// no record of its own.
    /// </para>
    /// </remarks>
    /// <param name="at">When the background ended.</param>
    /// <param name="exitCode">Its exit code, or <see langword="null"/>.</param>
    /// <param name="logPath">The log the person reads.</param>
    /// <returns>The sentence.</returns>
    public static string Crashed(DateTimeOffset at, int? exitCode, string logPath) =>
        $"BrowserAI's background process crashed at {When(at)} (exit code {Code(exitCode)}). Nothing was run. "
        + $"The person at this computer needs to read {logPath}, report the bug at {IssuesUrl}, and then start BrowserAI from the Start Menu. "
        + "Only that person can restart it: do not start BrowserAI yourself, and do not retry this call until they have.";

    /// <summary>
    /// The background crashed while a call it had been given was running.
    /// </summary>
    /// <remarks>
    /// Said when the pipe closes under a call in flight and a crash is recorded. The
    /// call had reached a browser server, so this row says to check before repeating,
    /// and keeps R's instruction about who restarts BrowserAI.
    /// </remarks>
    /// <param name="tool">The tool the call named.</param>
    /// <param name="at">When the background ended.</param>
    /// <param name="exitCode">Its exit code, or <see langword="null"/>.</param>
    /// <param name="logPath">The log the person reads.</param>
    /// <returns>The sentence.</returns>
    public static string CrashedDuringTheCall(string tool, DateTimeOffset at, int? exitCode, string logPath) =>
        $"BrowserAI's background process crashed at {When(at)} (exit code {Code(exitCode)}) while '{tool}' was running. "
        + PassedOn
        + $"The person at this computer needs to read {logPath}, report the bug at {IssuesUrl}, and then start BrowserAI from the Start Menu. "
        + "Only that person can restart it: do not start BrowserAI yourself, and do not retry this call until they have.";

    /// <summary>
    /// The background stopped answering: no liveness probe was answered for the hang
    /// bound, or it never got as far as running a held call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>D10 in the maintainer's version, with R's timeout</b> (2026-10-08): <i>"I want
    /// no crash monitoring or autoamtic restarts. [...] I want the user to be informed to
    /// check the logs and report the bug so we solve the root issue."</i> So nothing is
    /// ended and nothing restarts; the person's Start Menu start is what ends a stuck
    /// background and starts a new one (RESOLUTIONS 10).
    /// </para>
    /// <para>
    /// <b>Two forms.</b> A call that was passed on and then met the silence may have run
    /// in part. A call that arrived while the background was already silent, or that was
    /// held while the background failed to finish its greeting or to open its pipe, was
    /// not passed on at all.
    /// </para>
    /// </remarks>
    /// <param name="tool">The tool the call named.</param>
    /// <param name="wasPassedOn">Whether the call had already been passed on to the background.</param>
    /// <param name="logPath">The log the person reads.</param>
    /// <returns>The sentence.</returns>
    public static string Hung(string tool, bool wasPassedOn, string logPath) =>
        (wasPassedOn
            ? $"BrowserAI's background process stopped answering while '{tool}' was running: for {Seconds(RelayConstants.HangBound)} seconds it answered none of BrowserAI's checks that it is still working. " + PassedOn
            : $"BrowserAI's background process is running but is not answering, so '{tool}' was NOT run: nothing reached a browser. ")
        + "Nothing was stopped and nothing was restarted. "
        + $"The person at this computer needs to read {logPath}, report the bug at {IssuesUrl}, and then start BrowserAI from the Start Menu, which ends the stuck process and starts a new one. "
        + "Only that person can do that: do not start BrowserAI yourself. If the background process answers again first, the next call goes through.";

    /// <summary>
    /// No background process appeared while the call was held, and the task says
    /// why, or says nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three task states, one row</b> (RESOLUTIONS 9): a disabled task is named with
    /// how to enable it and is left as it is (D12 b, the person's choice stands); a
    /// missing task is named, and a Start Menu start registers it again; and when the
    /// task is ready, or could not be read, nothing about it is said. Each form tells the
    /// PERSON to start BrowserAI from the Start Menu, which is the one start a relay may
    /// ask for, since relays never start anything themselves (S).
    /// </para>
    /// <para>
    /// <b>Said only after the hold bound</b>, because the background appears by itself
    /// at sign-in and after an update, and a call held through either is served.
    /// </para>
    /// </remarks>
    /// <param name="tool">The tool the call named.</param>
    /// <param name="task">Where the task stands.</param>
    /// <param name="taskName">The task's name in the Task Scheduler.</param>
    /// <param name="detail">What the Task Scheduler said, or <see langword="null"/>.</param>
    /// <returns>The sentence.</returns>
    public static string NotRunning(string tool, TaskState task, string taskName, string? detail) =>
        $"BrowserAI's background process is not running, and none started in the {Seconds(RelayConstants.HoldBound)} seconds this call was held, so '{tool}' was NOT run: nothing reached a browser. "
        + task switch
        {
            TaskState.Disabled =>
                $"Its scheduled task, '{taskName}', is disabled, and BrowserAI does not change that. "
                + $"The person at this computer needs to enable it, in Task Scheduler (Task Scheduler Library, '{taskName}', Enable) or with schtasks /Change /TN \"{taskName}\" /ENABLE, and then start BrowserAI from the Start Menu. ",
            TaskState.Missing =>
                $"Its scheduled task, '{taskName}', is missing, so nothing starts BrowserAI at sign-in. "
                + "The person at this computer needs to start BrowserAI from the Start Menu, which registers the task again and starts BrowserAI. ",
            _ => "The person at this computer needs to start BrowserAI from the Start Menu. ",
        }
        + (detail is { Length: > 0 } said ? $"The Task Scheduler reported: {said.TrimEnd('.', ' ')}. " : string.Empty)
        + "Only that person can do this: do not start BrowserAI or change its task yourself, and do not retry this call until they have.";

    /// <summary>
    /// This build is not installed, so nothing will start a background for it.
    /// </summary>
    /// <remarks>
    /// <b>D11 a, decided 2026-10-08</b>: a checkout, the suite or a debugging session
    /// runs a binary with no install and no task, and its relay never starts a
    /// background. Said at once (D8 a: <i>"answer at once when waiting can't help"</i>),
    /// with the command a developer runs.
    /// </remarks>
    /// <param name="tool">The tool the call named.</param>
    /// <param name="executable">This binary's path.</param>
    /// <param name="dataRoot">The data root the relay serves.</param>
    /// <returns>The sentence.</returns>
    public static string NotInstalled(string tool, string executable, string dataRoot) =>
        $"No BrowserAI background process is running for this build, so '{tool}' was NOT run: nothing reached a browser. "
        + $"This BrowserAI, {executable}, is not installed, so nothing starts a background process for it, and waiting cannot help. "
        + $"A developer starts one with \"{executable}\" --background --data-root \"{dataRoot}\", and the next call goes through.";

    /// <summary>
    /// The background stopped, with no crash recorded, while a call it had been given
    /// was running.
    /// </summary>
    /// <remarks>
    /// The in-flight shape the plan gave it on 2026-10-04 ("The background dies"):
    /// the call may have happened in part. What follows is the relay's own behaviour,
    /// said so the model knows the next call is held and not lost.
    /// </remarks>
    /// <param name="tool">The tool the call named.</param>
    /// <returns>The sentence.</returns>
    public static string StoppedDuringTheCall(string tool) =>
        $"BrowserAI's background process stopped while '{tool}' was running. "
        + PassedOn
        + $"BrowserAI holds the next call for up to {Seconds(RelayConstants.HoldBound)} seconds while the background process starts again; if it does not start, the person at this computer needs to start BrowserAI from the Start Menu. "
        + "Do not start BrowserAI yourself.";

    /// <summary>The background refused this relay's greeting, and said why.</summary>
    /// <remarks>
    /// The background's own sentence is passed on whole: it is the one that knows
    /// whether the builds differ, the data roots differ or it is stopping. The relay
    /// goes on looking, so the next call meets whatever serves the pipe then.
    /// </remarks>
    /// <param name="tool">The tool the call named.</param>
    /// <param name="refusal">The background's sentence.</param>
    /// <returns>The sentence.</returns>
    public static string BackgroundRefused(string tool, string refusal) =>
        $"'{tool}' was NOT run: nothing reached a browser. BrowserAI's background process refused this connection and said: {refusal}";

    /// <summary>
    /// An update is installing, and the call was not run: U2's sentence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Kept for one short window only</b> (U2, "u2 looks good", 2026-10-08): a
    /// message that reaches a relay after it agreed to end and before the new version
    /// is in place, and a relay a client starts while the installer still runs. In
    /// the maintainer's approved words, <i>"BrowserAI is installing an update; nothing
    /// was run; call again in a few seconds"</i>.
    /// </para>
    /// <para>
    /// <b>Then what each client needs</b> (H1-T a, RESOLUTIONS 12): Claude Code run with
    /// <c>-p</c> or in VS Code starts the new BrowserAI by itself; in a terminal it
    /// needs <c>/mcp</c>, BrowserAI, Reconnect; Codex needs a new conversation.
    /// </para>
    /// </remarks>
    /// <param name="tool">The tool the call named.</param>
    /// <param name="version">The version being installed, or <see langword="null"/> when nobody named it.</param>
    /// <param name="clientName">What the client put in <c>clientInfo.name</c>, if anything.</param>
    /// <returns>The sentence.</returns>
    public static string UpdateInstalling(string tool, string? version, string? clientName) =>
        $"BrowserAI is installing an update{To(version)}; nothing was run; call again in a few seconds. "
        + $"'{tool}' did not reach a browser, and nothing changed. "
        + UpdateRemedy(clientName)
        + SessionsAfterAnUpdate;

    /// <summary>
    /// An update is installing, and it ended the relay while a call was running.
    /// </summary>
    /// <remarks>
    /// Said when the background commits an update with <c>now</c> set, which is a
    /// person's Install now: calls still running are cut off. Ordinarily no call is
    /// running, because a relay agrees to end only with nothing in flight.
    /// </remarks>
    /// <param name="tool">The tool the call named.</param>
    /// <param name="version">The version being installed, or <see langword="null"/> when nobody named it.</param>
    /// <param name="clientName">What the client put in <c>clientInfo.name</c>, if anything.</param>
    /// <returns>The sentence.</returns>
    public static string UpdateInstallingDuringTheCall(string tool, string? version, string? clientName) =>
        $"BrowserAI is installing an update{To(version)} and ended while '{tool}' was running. "
        + PassedOn
        + UpdateRemedy(clientName)
        + SessionsAfterAnUpdate;

    /// <summary>A client frame that is not a JSON-RPC message.</summary>
    /// <remarks>
    /// The message of the <c>-32700</c> the relay answers with when it can find the
    /// frame's id, so the sender fails instead of waiting for an answer that is never
    /// coming. Nothing is passed on.
    /// </remarks>
    /// <returns>The sentence.</returns>
    public static string UnreadableFrame() =>
        "BrowserAI could not read that frame as one JSON-RPC message, so nothing was passed on: send the request again.";

    /// <summary>The middle of every row about a call that had been passed on.</summary>
    private const string PassedOn =
        "The call had already been passed on to it, so part of it may have happened: check what it was doing before you repeat it. ";

    /// <summary>What becomes of a session when an update installs, said after every update row.</summary>
    private const string SessionsAfterAnUpdate =
        $" A session you were using is closed by the update and not lost: its profile, files and log stay on disk, so call {SessionToolSurface.Resume} on its directory before you use it again.";

    /// <summary>What a client needs once an update has ended its relay, spelled for that client.</summary>
    /// <remarks>
    /// Not a public row: one condition and one sentence, and this is the recovery
    /// clause inside it, as <c>SessionErrors</c> spells its own. The names are
    /// <see cref="KnownClients"/>' and were read off the wire.
    /// </remarks>
    /// <param name="clientName">What the client called itself.</param>
    /// <returns>One or two sentences.</returns>
    private static string UpdateRemedy(string? clientName) =>
        KnownClients.Matches(clientName, KnownClients.ClaudeCode)
            ? "When your client runs with -p or in VS Code, it starts the updated BrowserAI by itself on your next call. In a terminal session it shows BrowserAI as disconnected instead, and the person at this computer needs to run /mcp, choose BrowserAI and choose Reconnect before BrowserAI answers again."
            : KnownClients.Matches(clientName, KnownClients.Codex)
                ? "Your client does not start BrowserAI again once this one has ended, so BrowserAI answers again in a new conversation."
                : "If your client then shows BrowserAI as disconnected, reconnect the BrowserAI server, or start a new conversation.";

    private static string When(DateTimeOffset at) => SessionErrors.When(at);

    private static string Code(int? exitCode) =>
        exitCode is { } code ? code.ToString(CultureInfo.InvariantCulture) : "unknown";

    private static string Seconds(TimeSpan span) =>
        span.TotalSeconds.ToString("0", CultureInfo.InvariantCulture);

    private static string To(string? version) => version is { Length: > 0 } named ? $" to version {named}" : string.Empty;
}
