// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Coordination;
using BrowserAI.Hosting;
using BrowserAI.Interop;

namespace BrowserAI.Proxy;

/// <summary>
/// This server's answers to its pipe: a description built from its own memory,
/// and a stop that is acknowledged before it is acted on.
/// </summary>
/// <remarks>
/// <para>
/// <b>The description reads memory and nothing else.</b> No file is opened and
/// no process is asked anything, so an answer cannot tear and cannot block on
/// somebody else's lock: the identity was read once at startup, and the rest
/// comes from <see cref="ServerActivity"/> and the session manager's own list.
/// </para>
/// <para>
/// <b>The sessions are attached late</b>, once the proxy that owns them
/// exists. Until then a description says <i>starting</i> and lists none, which
/// is the truth about a server that has not begun answering its client.
/// </para>
/// </remarks>
/// <param name="activity">What this server has been doing.</param>
/// <param name="stop">
/// What a stop does once it has been acknowledged. It runs on the pipe's thread
/// and must hand its work off and return.
/// </param>
internal sealed class ServerPipeResponder(ServerActivity activity, Action stop) : IServerPipeResponder
{
    private readonly int _processId = Environment.ProcessId;
    private readonly long _created = ProcessLiveness.CreationTimeOfThisProcess();
    private readonly string _image = Environment.ProcessPath ?? string.Empty;

    private Func<IReadOnlyList<HeldSession>>? _sessions;

    /// <summary>Where the sessions this server holds are read from, once there is one.</summary>
    /// <param name="sessions">Answers the list at the moment of asking.</param>
    public void AttachSessions(Func<IReadOnlyList<HeldSession>> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        Volatile.Write(ref _sessions, sessions);
    }

    /// <inheritdoc />
    public ServerPipeReply Describe()
    {
        var now = activity.Read();

        var description = new ServerDescription(
            ServerPipeProtocol.Version,
            _processId,
            _created,
            BuildVersion.Current,
            _image,
            now.State,
            now.Client,
            activity.WorkingDirectory,
            now.Started,
            now.LastToolCall,
            now.CallsInFlight,
            Volatile.Read(ref _sessions)?.Invoke() ?? []);

        return new ServerPipeReply(description.ToJson());
    }

    /// <inheritdoc />
    public ServerPipeReply Stop() =>
        new(
            ServerPipeProtocol.Acknowledged(_processId),
            AfterDelivery: () =>
            {
                activity.Stopping();
                stop();
            });
}
