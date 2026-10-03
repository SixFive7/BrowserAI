// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace BrowserAI.Proxy;

/// <summary>
/// One MCP conversation with one client: the server it is answered through, who the
/// client said it was, and whether the conversation is still open.
/// </summary>
/// <remarks>
/// <para>
/// <b>A server started by a client has exactly one of these for its whole life.</b>
/// The session host has one per pipe connection, and that is the reason the type
/// exists: Q366 b, the maintainer's words of 2026-10-03, verbatim: <i>"Q366 b - lets
/// go with a fully build option c. If the server crashes and the coordinator loses
/// the pipe, keep the browser around with the already running activity timeout timer
/// active."</i> A session is attached to the connection that drives it, and when that
/// connection ends the session outlives it.
/// </para>
/// <para>
/// <b>The number is the host's own count</b> and names a connection in the log. The
/// pid Windows reports is the process on the pipe's other end, which is the BrowserAI
/// server the client started and relays through, so a person reading it finds that
/// server and, as its parent, the client.
/// </para>
/// </remarks>
/// <param name="clientProcessId">The pid on the other end, when Windows says, or <see langword="null"/>.</param>
internal sealed class CallerConnection(int? clientProcessId = null)
{
    private static long _numbers;

    private McpServer? _server;
    private int _ended;

    /// <summary>This process's own number for the connection, in the order it was accepted.</summary>
    public long Number { get; } = Interlocked.Increment(ref _numbers);

    /// <summary>The pid on the other end, when Windows says: the server the client started.</summary>
    public int? ClientProcessId { get; } = clientProcessId;

    /// <summary>What the client called itself at <c>initialize</c>, or <see langword="null"/> before then.</summary>
    public string? ClientName { get; private set; }

    /// <summary>Whether the conversation is still open.</summary>
    public bool IsOpen => Volatile.Read(ref _ended) is 0;

    /// <summary>The server this connection is answered through, once a message has arrived on it.</summary>
    public McpServer? Server => Volatile.Read(ref _server);

    /// <summary>Records the server a message arrived through.</summary>
    /// <param name="server">The server.</param>
    public void AnsweredThrough(McpServer server) => Volatile.Write(ref _server, server);

    /// <summary>Records what the client called itself.</summary>
    /// <param name="name">Its <c>clientInfo.name</c>.</param>
    public void Introduced(string? name) => ClientName = name;

    /// <summary>Records that the conversation has ended.</summary>
    /// <returns>Whether this call ended it.</returns>
    public bool End() => Interlocked.Exchange(ref _ended, 1) is 0;

    /// <summary>The connection as a person reads it in a log or a refusal.</summary>
    /// <returns>For example <c>client 'claude-code' (its BrowserAI server is pid 1234)</c>.</returns>
    public string Describe()
    {
        var name = ClientName is { Length: > 0 } named ? $"'{named}'" : "with no name yet";
        var pid = ClientProcessId is { } id ? $" (its BrowserAI server is pid {id.ToString(CultureInfo.InvariantCulture)})" : string.Empty;

        return $"client {name}{pid}";
    }

    /// <summary>Sends a child's notification to this connection's client, when there is anybody to send it to.</summary>
    /// <remarks>
    /// <b>A fresh envelope</b>, for the reason a forwarded result gets one: the child's
    /// own message may carry a related transport that would send it straight back
    /// where it came from. The parameters, progress token included, pass through
    /// untouched.
    /// </remarks>
    /// <param name="notification">The child's notification.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>The send.</returns>
    public async ValueTask RelayAsync(JsonRpcNotification notification, CancellationToken cancellationToken)
    {
        if (!IsOpen || Server is not { } server)
        {
            return;
        }

        await server.SendMessageAsync(
            new JsonRpcNotification { Method = notification.Method, Params = notification.Params },
            cancellationToken).ConfigureAwait(false);
    }
}
