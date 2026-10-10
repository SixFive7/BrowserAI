// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Proxy;

namespace BrowserAI.Sessions;

/// <summary>
/// The sentence that says why a session's browser was last closed: what closed it,
/// and when.
/// </summary>
/// <remarks>
/// <para>
/// <b>8 b, decided 2026-10-04 by the maintainer, in his words verbatim:</b> <i>"8 b -
/// log in our catchup resume that it was the user who closed it. Also whe ntelling
/// the agent it needs to resume first give it the reason for the last close. Was it a
/// user? Was it a timeout? Was it a close call from the agent or another agent?"</i>
/// One sentence per cause, written once here, so the refusal that sends an agent to
/// <c>browserai_resume</c>, the resume's own answer, <c>browserai_catch_up</c> and the
/// row the close writes into the session's log all say the same thing.
/// </para>
/// <para>
/// <b>Who closed it, when a call did, is said relative to the reader when that is
/// known</b>: a close made by the connection that is now asking reads as this
/// client's own, and one made by another reads as another client's, named. Read back
/// from the record, the client is named and the reader decides. The agents one Claude
/// Code conversation starts share its one connection, so <i>this client</i> is as far as
/// BrowserAI can tell them apart; the call's own <c>why</c> is quoted so an agent can
/// recognise its own.
/// </para>
/// </remarks>
internal static class CloseReasons
{
    /// <summary>The tool name a close BrowserAI recorded itself carries in the session's log.</summary>
    /// <remarks>
    /// <b>Not a tool</b>, and spelled so it cannot be read as one: a person closing a
    /// window, a crash, a stop or the idle close is no call anybody made. ⚠️ <i>Corrected
    /// 2026-10-10 (previously "The idle close and a caller's own <c>browser_close</c> keep
    /// the row they always had, because each of those is a call."), the texts review's
    /// #180: since F1 a no model can call <c>browser_close</c>, so the idle close's row
    /// stood under a name a reader could not act on.</i> A caller's own
    /// <c>browserai_close</c> keeps the row its call writes.
    /// </remarks>
    public const string LogRowTool = "(browser closed)";

    /// <summary>
    /// The clause a visible window's idle close adds to its reason: nobody had used the
    /// window either.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-08 with E2 and F4</b>: a visible window is closed for being idle
    /// since that day, and only once the person's input in it has stopped too, so its
    /// reason says both. Kept in the close's own record, so the sentence read back later
    /// is the one said at the time.
    /// </remarks>
    public const string NobodyUsedTheWindow = "nobody had used its window either";

    /// <summary>The sentence for a close this process saw happen.</summary>
    /// <param name="closure">The close.</param>
    /// <param name="asking">The connection the sentence is for, or <see langword="null"/> when it is for the record.</param>
    /// <returns>One or two sentences, ending with a full stop.</returns>
    public static string Of(SessionClosure closure, CallerConnection? asking)
    {
        ArgumentNullException.ThrowIfNull(closure);

        // ⚠️ Corrected 2026-10-10, the texts polish (previously "this client" and
        // "another client 'name' (its BrowserAI server is pid N)"): a model reads a
        // conversation, by its client and folder, never by a pid.
        var client = closure.Cause is SessionCloseCause.Caller or SessionCloseCause.SettingsChanged && asking is not null && closure.ClosedBy is { } by
            ? ReferenceEquals(by, asking) ? "this conversation" : $"another conversation ({by.Conversation()})"
            : null;

        return Of(closure.Recorded, closure.At, closure.IdlePeriod, client);
    }

    /// <summary>The sentence for a close read back from the record.</summary>
    /// <param name="close">The close, dated when it was recorded.</param>
    /// <returns>One or two sentences, ending with a full stop.</returns>
    public static string Of(Statement<RecordedClose> close)
    {
        ArgumentNullException.ThrowIfNull(close);

        return Of(close.Value, close.At, idlePeriod: null, client: null);
    }

    /// <summary>Whether the close was one BrowserAI made cleanly, so the browser had its chance to write everything.</summary>
    /// <param name="cause">The cause.</param>
    /// <returns>Whether it was clean.</returns>
    public static bool WasACleanClose(SessionCloseCause cause) =>
        cause is SessionCloseCause.Idle or SessionCloseCause.Caller or SessionCloseCause.SettingsChanged
            or SessionCloseCause.Stopped or SessionCloseCause.Updating or SessionCloseCause.Failed or SessionCloseCause.ServerShutDown;

    private static string Of(RecordedClose close, DateTimeOffset at, TimeSpan? idlePeriod, string? client)
    {
        var when = SessionErrors.When(at);
        var why = close.Why is { Length: > 0 } said ? $", which gave the reason \"{said}\"" : string.Empty;

        // Who made the call: the conversation the sentence is said to, another one, or
        // read back from the record, a conversation; nothing when the record names nobody.
        var from = client is not null ? $" from {client}" : close.By is { Length: > 0 } recorded ? $" from a conversation ({recorded})" : string.Empty;

        return close.Cause switch
        {
            // ⚠️ Corrected 2026-10-08 (previously "because no browser call had reached
            // it for P; it closes an idle headless browser so that one nobody is using
            // does not hold memory"): since E2 and F2 every call that names the session
            // restarts its countdown, a visible window has one too, and in a visible
            // window the person's input counts (F4).
            //
            // ⚠️ Corrected 2026-10-10 a second time, the texts polish, page #28 (previously
            // "...; it closes an idle browser so that one nobody is using does not hold
            // memory or hold back an update."): every refusal of a closed session quotes this
            // reason, and the maintainer asked for the reason, not for the why of the idle
            // close.
            SessionCloseCause.Idle =>
                $"BrowserAI closed this session's browser at {when} because no call had named the session for {SessionErrors.Duration(idlePeriod)}{(close.Detail is { } nobody ? $", and {nobody}" : string.Empty)}.",

            // ⚠️ browserai_close since 2026-10-08, F1 a (previously "by a browser_close
            // call from"). A record written before that day holds the same cause for a
            // browser_close call, and is read back with this tool's name: the cause is
            // the stored spelling and the tool's name was never stored.
            //
            // ⚠️ Corrected 2026-10-10, the texts polish, page #27 (previously "This
            // session's browser was closed at ... from {client}"): browserai_close records
            // this before it asks whether a browser is open, so the session is what was
            // closed in every case.
            SessionCloseCause.Caller =>
                $"This session was closed at {when} by a {SessionToolSurface.Close} call{from}{why}.",

            // ⚠️ Added 2026-10-08, F1 a and F2 d: a resume that asked for other settings
            // and was sent again unchanged closes the browser itself, cleanly, to open it
            // with them.
            //
            // ⚠️ Corrected 2026-10-10, the texts polish, page #19 (previously "to open
            // again with the settings a browserai_resume call from {client} asked for"),
            // which put the asker between the call and its verb.
            SessionCloseCause.SettingsChanged =>
                $"This session's browser was closed at {when} to open again with new settings, for a {SessionToolSurface.Resume} call{from}{why}.",

            SessionCloseCause.WindowClosed =>
                $"A person closed this session's browser window at {when}: the browser exited cleanly, and BrowserAI had not asked it to close.",

            SessionCloseCause.LastTabClosed =>
                $"This session's browser exited at {when} after browser_tabs closed its last tab.",

            SessionCloseCause.BrowserEnded =>
                $"This session's browser exited on its own at {when}, cleanly, and BrowserAI had not asked it to close.",

            SessionCloseCause.BrowserCrashed =>
                $"This session's browser ended at {when} with exit code {Code(close.ExitCode)}, which a crash or a kill leaves; BrowserAI had not asked it to close.",

            SessionCloseCause.ServerEnded =>
                $"This session's browser server was found ended at {when}, and its browser with it; BrowserAI had not ended it.",

            // ⚠️ Corrected 2026-10-10 (previously "because BrowserAI was stopped, which it
            // is to install an update or when a person closes it from BrowserAI's own
            // page."): nothing recorded it until then, an update records its own reason
            // since, and the page asks no stop. Said so that it stays true of a record
            // an earlier build wrote for either of those.
            //
            // ⚠️ AND OF A SESSION, NOT ITS BROWSER, for these three: corrected 2026-10-10
            // a second time, round 2 of the texts review, #29 (previously "BrowserAI
            // closed this session's browser at ..."). The background's end records the
            // reason for every session it closes, whether or not a browser is up
            // (LiveSession.CloseTheBrowserForShutdownAsync, RecordTheBackgroundsEnd), so
            // the sentence says what is true of both.
            //
            // ⚠️ Corrected 2026-10-10 a second time, the texts polish, pages #30 and #31
            // (previously "because BrowserAI was asked to stop" and "because BrowserAI's
            // background ended on a failure"): BrowserAI is named once, and no model is told
            // about a background.
            SessionCloseCause.Stopped =>
                $"BrowserAI closed this session at {when} because it was asked to stop.",

            // Added 2026-10-10, the texts review's #24: the background's own ends.
            SessionCloseCause.Updating =>
                $"BrowserAI closed this session at {when} to install an update.",

            SessionCloseCause.Failed =>
                $"BrowserAI closed this session at {when} because it ended on a failure, which its log names.",

            // Since 2026-10-08 only a host a client's own server owns records this, and
            // the product starts no such server: the suite's in-process rig does.
            SessionCloseCause.ServerShutDown =>
                $"BrowserAI closed this session's browser at {when} because the BrowserAI holding it shut down when its client{(close.By is { } gone ? $", {gone}," : string.Empty)} went away.",

            SessionCloseCause.Released =>
                $"BrowserAI let this session go at {when} because the client driving it{(close.By is { } left ? $", {left}," : string.Empty)} went away and there was nothing to keep{(close.Detail is { } detail ? $": {detail}" : string.Empty)}.",

            SessionCloseCause.Unrecorded =>
                $"No close was recorded after this session was last opened, at {SessionErrors.When(close.OpenedAt ?? at)}: the BrowserAI holding it ended without closing it, which a kill, a crash, a sign-out or the machine stopping does. It was last used at {when}.",

            _ =>
                $"This session's browser was closed at {when} for a reason this build of BrowserAI cannot read{(close.Detail is { } unknown ? $": {unknown}" : string.Empty)}.",
        };
    }

    private static string Code(int? exitCode) =>
        exitCode is { } code
            ? $"{code.ToString(CultureInfo.InvariantCulture)} (0x{unchecked((uint)code).ToString("X8", CultureInfo.InvariantCulture)})"
            : "unknown";
}
