// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Coordination;
using BrowserAI.Sessions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace BrowserAI.Proxy;

/// <summary>
/// What a server answers while its own install's updater is running: the
/// handshake, as the full server would, and a refusal for everything else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q286 b, the maintainer's words verbatim: <i>"Q286 b"</i>.</b> A server that
/// starts while <c>&lt;install root&gt;\Update.exe</c> is running answers
/// <c>initialize</c> normally, refuses every tool call with
/// <see cref="SessionErrors.UpdateIsBeingInstalled"/>, starts no browser server
/// and never checks the update feed. Measured 2026-09-24 at Velopack 1.2.158: a
/// server started before the updater's kill pass is killed by it, and the client
/// starts one again on its next call, which is then the new version's.
/// </para>
/// <para>
/// ⚠️ <b>The tool list is refused too, as a JSON-RPC error carrying the same
/// sentence.</b> There is no child to list from, and a partial list would be
/// cached by the client for the rest of its session; an error is what a client
/// shows as a failed server, with the reason beside it.
/// </para>
/// <para>
/// <b>Nothing here holds a session, a child or a browser</b>, so a call is
/// answered the instant it arrives and <see cref="ServerActivity"/> still counts
/// it, because a call refused during an update is as much a model at work as one
/// that was served.
/// </para>
/// </remarks>
/// <param name="activity">What this server has been doing, for its pipe.</param>
/// <param name="logger">Where each refusal is recorded.</param>
internal sealed class UpdateInProgressServer(ServerActivity activity, ILogger logger)
{
    /// <summary>The options this server is built from.</summary>
    /// <returns>The caller-facing options, with a filter that refuses tools.</returns>
    public McpServerOptions ServerOptions()
    {
        var options = BrowserProxy.CallerFacingOptions();

        options.Filters.Message.IncomingFilters.Add(next => (context, cancellationToken) =>
            OnIncomingAsync(next, context, cancellationToken));

        return options;
    }

    private async Task OnIncomingAsync(McpMessageHandler next, MessageContext context, CancellationToken cancellationToken)
    {
        if (context.JsonRpcMessage is JsonRpcRequest request)
        {
            switch (request.Method)
            {
                case RequestMethods.ToolsCall:
                    using (activity.ToolCall())
                    {
                        var tool = BrowserProxy.ToolNameOf(request);

                        ProxyLog.RefusedForAnUpdate(logger, tool);

                        await context.Server.SendMessageAsync(
                            new JsonRpcResponse
                            {
                                Id = request.Id,
                                Result = BrowserProxy.TextResult(
                                    SessionErrors.UpdateIsBeingInstalled(tool, wasRunning: false, context.Server.ClientInfo?.Name),
                                    isError: true),
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return;

                case RequestMethods.ToolsList:
                    ProxyLog.RefusedForAnUpdate(logger, RequestMethods.ToolsList);

                    await context.Server.SendMessageAsync(
                        new JsonRpcError
                        {
                            Id = request.Id,
                            Error = new JsonRpcErrorDetail
                            {
                                Code = (int)McpErrorCode.InternalError,
                                Message = SessionErrors.UpdateIsBeingInstalled(RequestMethods.ToolsList, wasRunning: false, context.Server.ClientInfo?.Name),
                            },
                        },
                        cancellationToken).ConfigureAwait(false);

                    return;

                default:
                    break;
            }
        }

        await next(context, cancellationToken).ConfigureAwait(false);

        if (context.JsonRpcMessage is JsonRpcRequest { Method: RequestMethods.Initialize }
            && context.Server.ClientInfo is { } introduced)
        {
            activity.Introduced(new ClientIdentity(introduced.Name, introduced.Title, introduced.Version));
        }
    }
}
