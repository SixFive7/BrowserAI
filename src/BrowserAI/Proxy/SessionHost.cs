// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Sessions;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Proxy;

/// <summary>
/// What every connection of one process shares: the sessions, the tool list and
/// the verdicts.
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
/// ⚠️ <b>It starts no child, since 2026-10-08.</b> <i>Corrected (previously "the
/// child that answers <c>tools/list</c>, the sessions, and the verdicts", with a
/// <c>Surface</c> started over a transport before the host existed)</i>: the tool
/// list is compiled into the binary, so a host is ready the moment it is made, and
/// the only children are the sessions'.
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

    private SessionHost(SessionManager sessions, SessionEnvironment environment, ILoggerFactory loggerFactory)
    {
        Sessions = sessions;
        Verdicts = environment.Verdicts;
        UpstreamTools = environment.UpstreamTools;
        LoggerFactory = loggerFactory;
    }

    /// <summary>Where the host and every proxy over it log.</summary>
    public ILoggerFactory LoggerFactory { get; }

    /// <summary>Every session this process holds.</summary>
    public SessionManager Sessions { get; }

    /// <summary>What this build does with a call naming each tool.</summary>
    public ToolVerdicts Verdicts { get; }

    /// <summary>Upstream's tools as this build was compiled with them.</summary>
    public UpstreamToolList UpstreamTools { get; }

    /// <summary>Makes the sessions' manager, and nothing else: no child is started.</summary>
    /// <param name="loggerFactory">Where the host and its sessions log.</param>
    /// <param name="environment">Where sessions keep their index, payload and configs, and the list and verdicts.</param>
    /// <returns>The host.</returns>
    public static SessionHost Create(ILoggerFactory loggerFactory, SessionEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(environment);

        // CA2000 is disabled for this one statement: the manager is
        // IAsyncDisposable and owned by the host from here, which disposes it.
#pragma warning disable CA2000
        return new SessionHost(new SessionManager(environment, loggerFactory), environment, loggerFactory);
#pragma warning restore CA2000
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

        // Each session owns a child whose job holds a browser, and each holds a
        // directory lock that should be released while the process is still able
        // to log why.
        await Sessions.DisposeAsync().ConfigureAwait(false);
    }
}
