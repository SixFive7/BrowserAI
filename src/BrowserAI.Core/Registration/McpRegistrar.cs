// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using Microsoft.Extensions.Logging;

namespace BrowserAI.Registration;

/// <summary>Which lifecycle event is asking, and therefore what it may do.</summary>
/// <remarks>
/// <b>The three differ in exactly one judgement: whose answer wins when an entry
/// is already there.</b> Getting that wrong in either direction is a real cost --
/// re-pointing always would silently discard a user's own edits on every update,
/// and never re-pointing would leave a stale path after a
/// <c>Setup.exe --installto</c> somewhere else, which is a product that cannot be
/// launched at all.
/// </remarks>
internal enum RegistrationIntent
{
    /// <summary>
    /// A fresh install. <b>This install wins:</b> any existing entry is replaced,
    /// because the path just changed and the newest install is the authority on
    /// where BrowserAI now is.
    /// </summary>
    Install,

    /// <summary>
    /// An update in place. <b>An existing entry of ours wins unless it names a
    /// file that is not there.</b>
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Corrected 2026-09-15 (previously "<c>current\</c> is replaced
    /// wholesale but its path does not move, so there is nothing to correct ...
    /// Only an <i>absent</i> entry is written, which self-heals a registration
    /// somebody removed").</b> The premise stopped being true the day the server
    /// was renamed: the path inside <c>current\</c> <i>can</i> move now, and
    /// every registration written before that day names a file the update
    /// deleted. The half that survives is the reason -- a user who added
    /// arguments or environment variables to their own registration must not
    /// have them deleted by a background update -- so an entry of ours that still
    /// resolves is left exactly as it is, and only one that resolves to nothing
    /// is re-pointed. A <c>browserai</c> entry outside our install root is
    /// somebody else's and is reported rather than touched.
    /// </remarks>
    Update,

    /// <summary>An uninstall. The entry goes, and its absence is not a failure.</summary>
    Uninstall,
}

/// <summary>What one registration pass concluded.</summary>
internal enum RegistrationStatus
{
    /// <summary>The entry was written.</summary>
    Registered,

    /// <summary>An entry was already there and was deliberately left alone.</summary>
    AlreadyRegistered,

    /// <summary>The entry was removed.</summary>
    Unregistered,

    /// <summary>There was no entry to remove, which is an ordinary outcome.</summary>
    NothingToUnregister,

    /// <summary>
    /// This machine has no client command line, so there is nothing to register
    /// with. Ordinary, and logged rather than failed.
    /// </summary>
    ClientNotFound,

    /// <summary>
    /// BrowserAI refused to register the path it was asked about -- the execution
    /// stub, or anything else outside <c>current\</c>.
    /// </summary>
    Refused,

    /// <summary>The client was there, ran, and did not do what was asked.</summary>
    Failed,
}

/// <summary>What a registration pass did, in the form the record file stores.</summary>
/// <param name="Status">The conclusion.</param>
/// <param name="Detail">
/// One sentence a person can act on, carrying the client's own words when it had
/// any and the manual command when the pass failed.
/// </param>
/// <param name="ClientPath">The client executable that was used, when one was found.</param>
/// <param name="Command">The path that was, or would have been, registered.</param>
internal sealed record RegistrationReport(RegistrationStatus Status, string Detail, string? ClientPath, string? Command)
{
    /// <summary>
    /// Whether the pass left the machine in the state it was asked for.
    /// </summary>
    /// <remarks>
    /// <see cref="RegistrationStatus.ClientNotFound"/> counts: a machine with no
    /// MCP client is correctly configured for the client it does not have. Only
    /// <see cref="RegistrationStatus.Failed"/> and
    /// <see cref="RegistrationStatus.Refused"/> are wrong.
    /// </remarks>
    public bool IsWhatWasAskedFor => Status is not (RegistrationStatus.Failed or RegistrationStatus.Refused);
}

/// <summary>
/// Registers and unregisters BrowserAI with the MCP client, idempotently, and
/// never throws.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here decides <i>how</i> -- that is
/// <see cref="McpClientRegistration"/>, deliberately in a file of its own.</b>
/// This type decides <i>when</i>, reads the client's answers, and makes certain
/// that whatever happened is legible afterwards.
/// </para>
/// <para>
/// ⚠️ <b>It cannot throw, and that is a requirement rather than a courtesy.</b>
/// It runs inside a Velopack fast-exit hook: an exception there fails the
/// install, and an install that fails because a <i>registration</i> failed is a
/// worse outcome than an installed product nobody registered. Every path returns
/// a <see cref="RegistrationReport"/>, and the two that mean <i>this did not
/// work</i> carry the command to run by hand.
/// </para>
/// <para>
/// <b>Idempotence is measured, not assumed.</b> Measured 2026-08-16 @ Claude Code
/// 2.1.233: a second <c>add</c> of the same name exits <b>1</b> with <i>"already
/// exists"</i> -- so <c>add</c> alone is <i>not</i> idempotent and this type
/// supplies the property the client does not. An install removes first and then
/// adds; an update adds and treats <i>already exists</i> as success. Install,
/// update, repair and reinstall therefore all converge on exactly one entry.
/// </para>
/// </remarks>
internal static class McpRegistrar
{
    /// <summary>Runs one registration pass.</summary>
    /// <param name="intent">Which lifecycle event is asking.</param>
    /// <param name="imagePath">
    /// The running image, normally <see cref="Environment.ProcessPath"/>. It is
    /// checked before it is used: see <see cref="RegistrationTarget"/>.
    /// </param>
    /// <param name="commands">The seam over starting the client.</param>
    /// <param name="logger">Where the pass reports.</param>
    /// <param name="existing">
    /// What is registered already, read rather than asked for. Supplied by the
    /// suite; resolved from the client's own user-scope file when omitted.
    /// </param>
    /// <returns>What happened. Never <see langword="null"/>, never throws.</returns>
    public static RegistrationReport Apply(
        RegistrationIntent intent,
        string? imagePath,
        IRegistrationCommand commands,
        ILogger logger,
        Func<string, RegistrationView>? existing = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            if (!RegistrationTarget.TryResolve(imagePath, out var target, out var refusal))
            {
                RegistrationLog.Refused(logger, refusal);
                return new RegistrationReport(RegistrationStatus.Refused, refusal, null, imagePath);
            }

            var command = target!.Command;
            var client = commands.Locate(McpClientRegistration.ClientExecutable);

            if (client is null)
            {
                var detail =
                    $"No '{McpClientRegistration.ClientExecutable}' was found on PATH or at '{ClientCommandLine.FallbackDirectory}', so BrowserAI has not registered itself with anything. " +
                    $"Install the client and run: {McpClientRegistration.ManualCommandFor(command)}";

                RegistrationLog.NoClient(logger, McpClientRegistration.ClientExecutable, ClientCommandLine.FallbackDirectory, command);
                return new RegistrationReport(RegistrationStatus.ClientNotFound, detail, null, command);
            }

            // ⚠️ READ BEFORE EVERY INTENT, AND NOT ONLY BEFORE AN UPDATE --
            // 2026-09-16. Until this day the install hook ran `mcp remove` and
            // then `mcp add` with no check at all, and the uninstall hook ran
            // `mcp remove` unconditionally -- so installing BrowserAI OVERWROTE
            // another BrowserAI's registration and uninstalling it DELETED one.
            // Three sentences in this codebase said that never happens
            // (RegistrationOwnership's own summary, AppState.MayRemove, and the
            // registration row in DECISIONS.md), and one intent out of three was
            // keeping them.
            var view = (existing ?? (root => McpRegistryView.User(root)))(target.InstallRoot);

            // Unreadable and Foreign answer the same way whatever was asked, so
            // they are decided once rather than three times. Everything below
            // this line is about a registration that is ABSENT or OURS.
            if (NotOursToTouch(logger, client, command, intent, view) is { } notOurs)
            {
                return notOurs;
            }

            return intent switch
            {
                RegistrationIntent.Uninstall => Remove(commands, logger, client, command),
                RegistrationIntent.Install => Reassert(commands, logger, client, command),
                _ => Repair(commands, logger, client, command, view),
            };
        }
#pragma warning disable CA1031 // The hook boundary. A registration failure is a log line, a record on disk and an install that still succeeds -- never an exception into the installer.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            RegistrationLog.PassFailed(logger, failure);

            return new RegistrationReport(
                RegistrationStatus.Failed,
                $"The registration pass threw: {failure.Message}. BrowserAI is installed and is not registered with any client; register it by hand with: {McpClientRegistration.ManualCommandFor(imagePath ?? "<the installed BrowserAI.exe>")}",
                null,
                imagePath);
        }
    }

    /// <summary>
    /// An update: repair an entry of ours that has gone stale, and touch nothing
    /// else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Added 2026-09-15 (previously this intent went to
    /// <see cref="EnsurePresent"/>, which adds when absent and otherwise does
    /// nothing at all).</b> That was right while the registered path could not
    /// change under an update, and it stopped being right the day the server was
    /// renamed: every registration written before that day names
    /// <c>current\BrowserAI.exe</c>, which is now the configuration app's name
    /// and, in an install that has been updated, a file the client can still
    /// launch. Left alone, a person who updates gets a window instead of a
    /// server.
    /// </para>
    /// <para>
    /// <b>Four states, and only one of them writes.</b> Absent adds, because an
    /// update of a BrowserAI somebody unregistered by hand is not an invitation
    /// to leave them without one and that has been this hook's behaviour since
    /// it existed. <i>Ours and present</i> is left exactly as it is, arguments
    /// and all -- a person may have added their own. <i>Ours and stale</i> is
    /// re-pointed, which is the whole reason this method exists. <b>Foreign is
    /// reported and never touched</b>: an entry named <c>browserai</c> whose
    /// command is not under our install root belongs to another BrowserAI, and
    /// adopting it would be an update of one product silently re-pointing
    /// another. <i>Since 2026-09-16 that last judgement is taken in
    /// <see cref="NotOursToTouch"/> and applies to every intent</i>, so this
    /// method only ever meets the two that are ours and the one that is nothing.
    /// </para>
    /// <para>
    /// <b>Ours is decided by the install root and not by the file name</b>, so a
    /// second install elsewhere reads as foreign -- which it is.
    /// </para>
    /// </remarks>
    private static RegistrationReport Repair(
        IRegistrationCommand commands,
        ILogger logger,
        string client,
        string command,
        RegistrationView existing)
    {
        // Unreadable and Foreign never reach here: Apply decides both before it
        // picks an intent, because the answer is the same for all three.
        switch (existing.Ownership)
        {
            case RegistrationOwnership.Absent:
                return Add(commands, logger, client, command);

            case RegistrationOwnership.OursAndStale:
                RegistrationLog.Repairing(logger, existing.Command ?? "<none>", command);
                _ = commands.Run(client, McpClientRegistration.RemoveArguments(), McpClientRegistration.Budget);
                return Add(commands, logger, client, command);

            default:
                RegistrationLog.AlreadyRegistered(logger, McpClientRegistration.ServerName, command);
                return new RegistrationReport(
                    RegistrationStatus.AlreadyRegistered,
                    $"'{McpClientRegistration.ServerName}' is already registered at '{existing.Command}' and was left exactly as it is.",
                    client,
                    existing.Command);
        }
    }

    /// <summary>
    /// The two states in which no intent may act: a configuration nobody could
    /// read, and an entry this install did not write.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Shared by all three intents since 2026-09-16.</b> It was
    /// <see cref="Repair"/>'s alone, which meant an <i>install</i> overwrote a
    /// foreign entry and an <i>uninstall</i> deleted one -- the exact two things
    /// <see cref="RegistrationOwnership"/> says this product never does. The
    /// wording is unchanged for the intents that write, so a person who has met
    /// this message before meets the same one; the uninstall's closing sentence
    /// differs because <i>register this one instead</i> is not advice about an
    /// uninstall.
    /// </para>
    /// <para>
    /// <b>Nothing runs.</b> The refusal is returned before any client command is
    /// started, so the verb list a double records is empty -- which is how the
    /// suite tells <i>refused</i> from <i>tried and failed</i>.
    /// </para>
    /// <para>
    /// <b>There is no exit code on this path and that is by design</b>: these run
    /// inside Velopack fast-exit callbacks, where a non-zero result fails
    /// somebody's install. What carries the outcome instead is
    /// <c>mcp-registration.json</c> -- <c>isWhatWasAskedFor</c> is
    /// <see langword="false"/> for a refusal and the detail names the foreign
    /// path -- and a warning-level line in the installer's own log.
    /// </para>
    /// </remarks>
    /// <param name="logger">Where the refusal is reported.</param>
    /// <param name="client">The client executable that was found.</param>
    /// <param name="command">What this install would have registered.</param>
    /// <param name="intent">Which lifecycle event asked.</param>
    /// <param name="existing">What is registered already.</param>
    /// <returns>The refusal, or <see langword="null"/> when the caller may act.</returns>
    private static RegistrationReport? NotOursToTouch(
        ILogger logger,
        string client,
        string command,
        RegistrationIntent intent,
        RegistrationView existing)
    {
        if (existing.Unreadable is { } unreadable)
        {
            // Never treated as "nothing is registered": that reading would make
            // an unreadable file into a licence to write one -- or, on an
            // uninstall, into a licence to delete one.
            RegistrationLog.Refused(logger, unreadable);
            return new RegistrationReport(RegistrationStatus.Refused, unreadable, client, command);
        }

        if (existing.Ownership is not RegistrationOwnership.Foreign)
        {
            return null;
        }

        var advice = intent is RegistrationIntent.Uninstall
            ? "That entry belongs to the other install, and removing it is for that install to do."
            : $"If this install is the one you want, unregister the other and register this one: {McpClientRegistration.ManualCommandFor(command)}";

        var foreign =
            $"Another BrowserAI is registered at '{existing.Command}', which is not under this install root. "
            + $"Nothing was changed: BrowserAI never adopts, overwrites or removes a '{McpClientRegistration.ServerName}' entry it did not write. "
            + advice;

        RegistrationLog.Refused(logger, foreign);
        return new RegistrationReport(RegistrationStatus.Refused, foreign, client, existing.Command);
    }

    /// <summary>
    /// An install: remove whatever <b>of ours</b> is there, then add. The newest
    /// install is the authority on where <i>this</i> BrowserAI is, and on
    /// nothing else.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Corrected 2026-09-16 (previously "remove whatever is there, then
    /// add. The newest install is the authority on where BrowserAI is").</b>
    /// That sentence was true of the code and false of the product: <i>whatever
    /// is there</i> included another BrowserAI's entry, and this method deleted
    /// it and wrote its own over the top. <see cref="Apply"/> now refuses a
    /// foreign entry before this is reached, so the remaining states really are
    /// the ones the sentence assumed -- absent, or ours.
    /// </remarks>
    private static RegistrationReport Reassert(IRegistrationCommand commands, ILogger logger, string client, string command)
    {
        // Deliberately unexamined. "Nothing to remove" is the ordinary case on a
        // first install, and a remove that failed for any other reason will
        // surface as the add failing, with the client's own words attached.
        _ = commands.Run(client, McpClientRegistration.RemoveArguments(), McpClientRegistration.Budget);

        return Add(commands, logger, client, command);
    }

    /// <summary>
    /// An update: add only if absent. An entry that is already there is left
    /// exactly as the user left it.
    /// </summary>
    private static RegistrationReport EnsurePresent(IRegistrationCommand commands, ILogger logger, string client, string command) =>
        Add(commands, logger, client, command);

    private static RegistrationReport Add(IRegistrationCommand commands, ILogger logger, string client, string command)
    {
        var outcome = commands.Run(client, McpClientRegistration.AddArguments(command), McpClientRegistration.Budget);

        if (outcome.Succeeded)
        {
            RegistrationLog.Registered(logger, McpClientRegistration.ServerName, command, client);

            return new RegistrationReport(
                RegistrationStatus.Registered,
                $"Registered '{McpClientRegistration.ServerName}' at {McpClientRegistration.UserScope} scope, pointing at '{command}'. It is available in every repository and wrote no file into any of them.",
                client,
                command);
        }

        if (McpClientRegistration.MeansAlreadyRegistered(outcome.ExitCode, outcome.Output))
        {
            RegistrationLog.AlreadyRegistered(logger, McpClientRegistration.ServerName, command);

            return new RegistrationReport(
                RegistrationStatus.AlreadyRegistered,
                $"'{McpClientRegistration.ServerName}' was already registered at {McpClientRegistration.UserScope} scope and was left exactly as it was. An update does not overwrite a registration, because the path does not move and the arguments may not be ours.",
                client,
                command);
        }

        return Failed(logger, client, command, outcome, "register");
    }

    private static RegistrationReport Remove(IRegistrationCommand commands, ILogger logger, string client, string command)
    {
        var outcome = commands.Run(client, McpClientRegistration.RemoveArguments(), McpClientRegistration.Budget);

        if (outcome.Succeeded)
        {
            RegistrationLog.Unregistered(logger, McpClientRegistration.ServerName);

            return new RegistrationReport(
                RegistrationStatus.Unregistered,
                $"Removed '{McpClientRegistration.ServerName}' from {McpClientRegistration.UserScope} scope.",
                client,
                command);
        }

        if (McpClientRegistration.MeansNothingToRemove(outcome.ExitCode, outcome.Output))
        {
            RegistrationLog.NothingToUnregister(logger, McpClientRegistration.ServerName);

            return new RegistrationReport(
                RegistrationStatus.NothingToUnregister,
                $"There was no '{McpClientRegistration.ServerName}' at {McpClientRegistration.UserScope} scope to remove, which is what an uninstall of a BrowserAI somebody had already unregistered looks like.",
                client,
                command);
        }

        return Failed(logger, client, command, outcome, "unregister");
    }

    private static RegistrationReport Failed(ILogger logger, string client, string command, CommandOutcome outcome, string verb)
    {
        var said = outcome switch
        {
            { TimedOut: true } => $"it did not finish within {McpClientRegistration.Budget.TotalSeconds:F0}s and was stopped",
            { Failure: { } failure } => $"it could not be started: {failure}",
            _ => $"it exited {outcome.ExitCode} saying: {(outcome.Output.Length is 0 ? "<nothing>" : outcome.Output)}",
        };

        var detail =
            $"BrowserAI could not {verb} itself. '{client}' was found and {said}. " +
            $"BrowserAI is installed and working; what is missing is the client's pointer at it. Run: {McpClientRegistration.ManualCommandFor(command)}";

        RegistrationLog.Failed(logger, verb, client, said, McpClientRegistration.ManualCommandFor(command));

        return new RegistrationReport(RegistrationStatus.Failed, detail, client, command);
    }
}

/// <summary>Source-generated log messages for the registration path.</summary>
internal static partial class RegistrationLog
{
    /// <summary>The entry was written.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="server">The name it was registered under.</param>
    /// <param name="command">The executable a client will now launch.</param>
    /// <param name="client">The client command line that did it.</param>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Registered '{Server}' as a user-scoped MCP server pointing at {Command}, using {Client}. It is available in every repository and needs no file in any of them.")]
    public static partial void Registered(ILogger logger, string server, string command, string client);

    /// <summary>An entry was already present and was left alone.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="server">The name.</param>
    /// <param name="command">What this build would have registered.</param>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "'{Server}' is already registered at user scope, so nothing was changed. This build would have pointed it at {Command}; an update never overwrites a registration, because the path does not move and any arguments on it may not be ours.")]
    public static partial void AlreadyRegistered(ILogger logger, string server, string command);

    /// <summary>The entry was removed.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="server">The name.</param>
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Removed '{Server}' from the client's user-scoped MCP servers.")]
    public static partial void Unregistered(ILogger logger, string server);

    /// <summary>
    /// An entry of ours naming a file that is not there any more, re-pointed.
    /// </summary>
    /// <param name="logger">Where the record goes.</param>
    /// <param name="was">What the entry named.</param>
    /// <param name="now">What it names now.</param>
    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Information,
        Message = "Repaired the MCP registration: it named '{Was}', which is no longer there, and now names '{Now}'.")]
    public static partial void Repairing(ILogger logger, string was, string now);

    /// <summary>There was nothing registered to remove.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="server">The name.</param>
    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Information,
        Message = "There was no '{Server}' registered at user scope to remove. Nothing is wrong: this is what uninstalling a BrowserAI that was already unregistered looks like.")]
    public static partial void NothingToUnregister(ILogger logger, string server);

    /// <summary>
    /// This machine has no client command line.
    /// </summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="executable">What was looked for.</param>
    /// <param name="fallback">The one directory searched beyond PATH.</param>
    /// <param name="command">What would have been registered.</param>
    /// <remarks>
    /// <b>Warning rather than Information.</b> An installed BrowserAI that no
    /// client can reach is the exact state this whole mechanism exists to
    /// prevent, and the fact that it is nobody's fault does not make it a state
    /// anyone should have to guess at.
    /// </remarks>
    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "No '{Executable}' was found on PATH or at {Fallback}, so BrowserAI has not registered itself with any MCP client. It is installed and working; nothing is configured to talk to it. Register it by hand once a client is installed: claude mcp add browserai --scope user -- \"{Command}\"")]
    public static partial void NoClient(ILogger logger, string executable, string fallback, string command);

    /// <summary>BrowserAI refused to register the path it was given.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="refusal">Which path, and why not.</param>
    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Error,
        Message = "BrowserAI refused to register itself. {Refusal}")]
    public static partial void Refused(ILogger logger, string refusal);

    /// <summary>The client ran and did not do what was asked.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="verb">Register or unregister.</param>
    /// <param name="client">The client executable.</param>
    /// <param name="said">What it did, in its own words where it had any.</param>
    /// <param name="manual">The command to run by hand.</param>
    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Error,
        Message = "BrowserAI could not {Verb} itself with the MCP client. {Client} {Said}. BrowserAI is installed and working; what is missing is the client's pointer at it. Run: {Manual}")]
    public static partial void Failed(ILogger logger, string verb, string client, string said, string manual);

    /// <summary>The pass threw, which the installer must never see.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Error,
        Message = "The MCP registration pass threw. The install itself is unaffected -- a hook that throws breaks an installer, so this is caught here and reported instead.")]
    public static partial void PassFailed(ILogger logger, Exception failure);

    /// <summary>Where the registration record went, and what it says.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="path">The record file.</param>
    /// <param name="status">The outcome it records.</param>
    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Information,
        Message = "MCP registration state is at {Path}: {Status}")]
    public static partial void RecordWritten(ILogger logger, string path, RegistrationStatus status);

    /// <summary>The record could not be written, which is a second silence.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="path">Where it was going.</param>
    /// <param name="failure">Why it did not get there.</param>
    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Warning,
        Message = "The MCP registration record at {Path} could not be written. The log record above is the only account of what happened.")]
    public static partial void RecordNotWritten(ILogger logger, string path, Exception failure);
}
