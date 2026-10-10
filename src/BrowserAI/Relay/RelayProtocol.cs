// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Relay;

/// <summary>
/// The names only a relay and the background speak to each other, so that both
/// sides spell them from one place.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every method starts with <see cref="MethodPrefix"/>, and a relay passes none
/// of them to its client</b>: a request under the prefix that a relay does not know
/// is answered <c>-32601</c>, a notification under it is dropped. Everything else on
/// the pipe is MCP, newline-delimited JSON-RPC 2.0 in UTF-8, as on stdio.
/// </para>
/// <para>
/// <b>The relay's greeting</b>, its first frame on a new connection, a request with
/// the id <c>browserai-relay-hello</c>:
/// <c>{"build":..., "relayPid":..., "clientPid":... or null, "client":{"name":..., "version":...},
/// "reconnect":"None"|"McpReconnect"|"NewConversation"|"Unknown", "folder":..., "dataRoot":...,
/// "idleAt":"ISO 8601, UTC"}</c>, and since 2026-10-10, when the relay read them,
/// <c>"conversation":{"claudeConfig":..., "clientStarted":"FILETIME", "sessionId":...,
/// "commandLineSessionId":..., "codexHome":...}</c>, each member only when it was read, and
/// <c>"window":"pid-FILETIME"</c> for a VS Code tab (<see cref="ConversationFacts"/>).
/// The background answers <c>{"build":..., "pid":...}</c>,
/// or an error whose <c>message</c> is a sentence for the model and whose
/// <c>data.refusal</c> is one of the four refusal kinds below. After an answer, the
/// relay replays its client's <c>initialize</c> under an id of its own, sends
/// <c>notifications/initialized</c>, and passes calls on.
/// </para>
/// <para>
/// <b>The countdown</b> travels as a moment, <c>{"idleAt":"ISO 8601, UTC"}</c>, in
/// <see cref="Activity"/> and <see cref="Withdraw"/>, both notifications from the relay.
/// <b>The agreement</b>: the background asks <see cref="ReadyToEnd"/> as a request and
/// is answered <c>{"ready":true,"idleAt":...}</c> or
/// <c>{"ready":false,"idleAt":...,"callInFlight":true|false}</c>; it sends
/// <see cref="CalledOff"/> or <see cref="End"/> as notifications, the second with
/// <c>{"now":true|false,"version":...}</c>.
/// </para>
/// <para>
/// <b>Liveness</b> is MCP's own <c>ping</c>, sent by the relay with ids it owns while a
/// call is outstanding, which the background answers apart from its other work (D10).
/// </para>
/// </remarks>
internal static class RelayProtocol
{
    /// <summary>What every method of the relay's own starts with.</summary>
    public const string MethodPrefix = Coordination.BackgroundPipe.MethodPrefix;

    /// <summary>The relay's greeting, a request.</summary>
    public const string Hello = MethodPrefix + "hello";

    /// <summary>The relay's countdown moved, a notification.</summary>
    public const string Activity = MethodPrefix + "activity";

    /// <summary>The relay's client sent something after the relay agreed to end: the update is off, a notification.</summary>
    public const string Withdraw = MethodPrefix + "withdraw";

    /// <summary>The background's question whether the relay may end for an update, a request.</summary>
    public const string ReadyToEnd = MethodPrefix + "ready-to-end";

    /// <summary>The background calls the update off and releases what each relay held, a notification.</summary>
    public const string CalledOff = MethodPrefix + "called-off";

    /// <summary>The background commits the update and tells the relay to end, a notification.</summary>
    public const string End = MethodPrefix + "end";

    /// <summary>A refusal of the greeting: the relay and the background are different builds.</summary>
    public const string RefusedForTheBuild = "build";

    /// <summary>A refusal of the greeting: the relay serves a data root the background does not.</summary>
    public const string RefusedForTheDataRoot = "dataRoot";

    /// <summary>A refusal of the greeting: an update is installing, and the relay answers with the update sentence.</summary>
    public const string RefusedForAnUpdate = "updating";

    /// <summary>A refusal of the greeting: the background has decided to stop.</summary>
    public const string RefusedWhileStopping = "stopping";
}
