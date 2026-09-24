// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Coordination;

namespace BrowserAI.Proxy;

/// <summary>
/// What this server has been doing, kept in memory for its pipe to describe:
/// its state, its client, and when its tool calls arrive and finish.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every tool call counts, the refused ones included.</b> The page asks
/// whether a model is working through this server right now, and a call that
/// was refused is as much evidence of that as one that was forwarded. The
/// maintainer's words for Q269, verbatim: <i>"there is no semantic difference
/// for the user between call in progress and call fired and finished 1 ms or
/// 4 min. ago."</i>
/// </para>
/// <para>
/// <b>The last call is the later of arriving and finishing</b>, so a call that
/// ran for fifteen minutes and ended a minute ago reads as a minute ago and not
/// as sixteen.
/// </para>
/// <para>
/// <b>One lock, held for a few assignments.</b> The pipe's thread reads this
/// while the protocol's threads write it, and a snapshot taken under the lock is
/// one moment and not several.
/// </para>
/// </remarks>
/// <param name="time">The clock. <see cref="TimeProvider.System"/> in the product.</param>
/// <param name="workingDirectory">The directory this process was started in.</param>
internal sealed class ServerActivity(TimeProvider time, string workingDirectory)
{
    private readonly Lock _gate = new();

    private string _state = ServerDescription.States.Starting;
    private ClientIdentity? _client;
    private DateTimeOffset? _started;
    private DateTimeOffset? _lastToolCall;
    private int _callsInFlight;

    /// <summary>The directory this process was started in, which is its client's.</summary>
    public string WorkingDirectory { get; } = workingDirectory;

    /// <summary>This server has begun answering its client.</summary>
    public void Serving() => Begin(ServerDescription.States.Serving);

    /// <summary>
    /// This server began answering its client while its install's updater was
    /// running, and refuses every tool call.
    /// </summary>
    public void Updating() => Begin(ServerDescription.States.Updating);

    /// <summary>This server has been asked to stop and is on its way out.</summary>
    public void Stopping()
    {
        lock (_gate)
        {
            _state = ServerDescription.States.Stopping;
        }
    }

    /// <summary>Records what the client called itself at <c>initialize</c>.</summary>
    /// <param name="client">The client's identity.</param>
    public void Introduced(ClientIdentity client)
    {
        ArgumentNullException.ThrowIfNull(client);

        lock (_gate)
        {
            _client = client;
        }
    }

    /// <summary>Marks one tool call as arrived; dispose the answer when it has been answered.</summary>
    /// <returns>A scope that marks the call finished.</returns>
    public IDisposable ToolCall()
    {
        lock (_gate)
        {
            _callsInFlight++;
            _lastToolCall = time.GetUtcNow();
        }

        return new CallScope(this);
    }

    /// <summary>A copy of everything above, taken at one moment.</summary>
    /// <returns>The snapshot.</returns>
    public ActivitySnapshot Read()
    {
        lock (_gate)
        {
            return new ActivitySnapshot(_state, _client, _started, _lastToolCall, _callsInFlight);
        }
    }

    private void Begin(string state)
    {
        lock (_gate)
        {
            _state = state;
            _started ??= time.GetUtcNow();
        }
    }

    private void Finished()
    {
        lock (_gate)
        {
            _callsInFlight--;
            _lastToolCall = time.GetUtcNow();
        }
    }

    private sealed class CallScope(ServerActivity owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) is 0)
            {
                owner.Finished();
            }
        }
    }
}

/// <summary>What <see cref="ServerActivity"/> held at one moment.</summary>
/// <param name="State">One of <see cref="ServerDescription.States"/>.</param>
/// <param name="Client">What the client called itself, once it has.</param>
/// <param name="Started">When this server began answering its client.</param>
/// <param name="LastToolCall">When a tool call last arrived or finished.</param>
/// <param name="CallsInFlight">How many tool calls are being answered.</param>
internal sealed record ActivitySnapshot(
    string State,
    ClientIdentity? Client,
    DateTimeOffset? Started,
    DateTimeOffset? LastToolCall,
    int CallsInFlight);
