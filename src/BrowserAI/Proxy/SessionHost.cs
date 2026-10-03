// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Sessions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace BrowserAI.Proxy;

/// <summary>
/// What every connection of one process shares: the child that answers
/// <c>tools/list</c>, the sessions, and the verdicts.
/// </summary>
/// <remarks>
/// <para>
/// <b>Split out of <see cref="BrowserProxy"/> on 2026-10-03, for Q366 b</b>, the
/// maintainer's words verbatim: <i>"Q366 b - lets go with a fully build option
/// c."</i> A server a client starts has one connection and owns its host through
/// its one proxy, exactly as before. The session host has one proxy per pipe
/// connection over this one object, so a session outlives the connection that
/// opened it and the next connection can drive it.
/// </para>
/// <para>
/// <b>Disposing this ends every session</b>, each browser asked to close first
/// (<see cref="SessionManager.DisposeAsync"/>); ending one connection only detaches
/// what it drove (<see cref="EndAsync"/>).
/// </para>
/// </remarks>
internal sealed class SessionHost : IAsyncDisposable
{
    private int _disposed;

    private SessionHost(ChildConnection surface, SessionManager sessions, ToolVerdicts verdicts, ILoggerFactory loggerFactory)
    {
        Surface = surface;
        Sessions = sessions;
        Verdicts = verdicts;
        LoggerFactory = loggerFactory;
    }

    /// <summary>Where the host and every proxy over it log.</summary>
    public ILoggerFactory LoggerFactory { get; }

    /// <summary>The child that answers <c>tools/list</c> before any session exists.</summary>
    public ChildConnection Surface { get; }

    /// <summary>Every session this process holds.</summary>
    public SessionManager Sessions { get; }

    /// <summary>What this build does with a call naming each tool.</summary>
    public ToolVerdicts Verdicts { get; }

    /// <summary>Starts the tool list's own child over a transport, and the sessions beside it.</summary>
    /// <param name="transport">The transport to the tool list's child. The SDK client owns it.</param>
    /// <param name="loggerFactory">Where the host, its children and its sessions log.</param>
    /// <param name="environment">Where sessions keep their index, payload and configs.</param>
    /// <param name="cancellationToken">Cancels the connect.</param>
    /// <returns>The host.</returns>
    public static async Task<SessionHost> ConnectAsync(
        IClientTransport transport,
        ILoggerFactory loggerFactory,
        SessionEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(environment);

        // CA2000 is disabled for these two statements and nothing else, for the
        // reason BrowserProxy has always given: both are IAsyncDisposable, owned by
        // the host once it exists, and disposed in the finally on every path that
        // does not get that far.
        SessionManager? sessions = null;
        ChildConnection? surface = null;

        try
        {
#pragma warning disable CA2000
            sessions = new SessionManager(environment, loggerFactory);

            // The tool list's child carries no session and no progress worth
            // relaying: `tools/list` has no progress token to report against.
            surface = await ChildConnection.ConnectAsync(
                transport,
                loggerFactory,
                "browserai-",
                static (_, _) => ValueTask.CompletedTask,
                cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2000

            var host = new SessionHost(surface, sessions, environment.Verdicts, loggerFactory);

            sessions = null;
            surface = null;

            return host;
        }
        finally
        {
            if (surface is not null)
            {
                await surface.DisposeAsync().ConfigureAwait(false);
            }

            if (sessions is not null)
            {
                await sessions.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>A proxy for one more connection, over this host's sessions.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="activity">What the connection does, for a description, or <see langword="null"/> for one nobody reads.</param>
    /// <returns>The proxy. Disposing it detaches what the connection drove and leaves the host running.</returns>
    public BrowserProxy Accept(CallerConnection connection, ServerActivity? activity = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return BrowserProxy.For(this, connection, activity, ownsHost: false);
    }

    /// <summary>Detaches what a connection drove, once it has ended.</summary>
    /// <param name="connection">The connection.</param>
    /// <returns>The detach.</returns>
    public Task EndAsync(CallerConnection connection) => Sessions.DetachAsync(connection);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        // Sessions first: each owns a child whose job holds a browser, and each
        // holds a directory lock that should be released while the process is
        // still able to log why.
        await Sessions.DisposeAsync().ConfigureAwait(false);
        await Surface.DisposeAsync().ConfigureAwait(false);
    }
}
