// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.IO.Pipelines;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// One hop: two <see cref="Pipe"/>s standing in for a process's stdin and
/// stdout, with both ends in the test's hands.
/// </summary>
/// <remarks>
/// <para>
/// <b>A proxy needs two of these, and that is the whole reason the SDK's own
/// fixtures are not vendored.</b> <c>ClientServerTestBase</c> wires one
/// client↔server pair; BrowserAI is a client on one side and a server on the
/// other, so the topology is test client → BrowserAI … BrowserAI → fake child
/// and there are two hops to own.
/// </para>
/// <para>
/// The buffer thresholds are raised well above the default 64 KiB because this
/// layer deliberately carries an oversized payload. With the default, a frame
/// larger than the pause threshold blocks the writer until the reader drains —
/// which is correct behaviour and still a deadlock whenever the same task is
/// on both sides of it.
/// </para>
/// </remarks>
internal sealed class PipeDuplex
{
    private const long PauseWriterThreshold = 64L * 1024 * 1024;
    private const long ResumeWriterThreshold = 32L * 1024 * 1024;

    private readonly Pipe _clientToServer = NewPipe();
    private readonly Pipe _serverToClient = NewPipe();

    /// <summary>Names this hop in a teardown failure message.</summary>
    /// <param name="name">What this hop connects.</param>
    public PipeDuplex(string name)
    {
        Name = name;
        ClientWrites = _clientToServer.Writer.AsStream();
        ServerReads = _clientToServer.Reader.AsStream();
        ServerWrites = _serverToClient.Writer.AsStream();
        ClientReads = _serverToClient.Reader.AsStream();
    }

    /// <summary>What this hop connects, for diagnostics.</summary>
    public string Name { get; }

    /// <summary>The client's outbound stream.</summary>
    public Stream ClientWrites { get; }

    /// <summary>The server's inbound stream.</summary>
    public Stream ServerReads { get; }

    /// <summary>The server's outbound stream.</summary>
    public Stream ServerWrites { get; }

    /// <summary>The client's inbound stream.</summary>
    public Stream ClientReads { get; }

    /// <summary>
    /// Completes <b>both</b> writers, so each side observes EOF on its read.
    /// </summary>
    /// <remarks>
    /// Both, not one. The read loop on each side ends on EOF or on its own
    /// shutdown token; completing only the writer whose reader is being torn
    /// down leaves the other loop parked on a read that nothing will ever
    /// satisfy.
    /// </remarks>
    /// <returns>A task that completes once both writers are closed.</returns>
    public async Task CompleteWritersAsync()
    {
        await _clientToServer.Writer.CompleteAsync();
        await _serverToClient.Writer.CompleteAsync();
    }

    /// <summary>
    /// Describes anything still live on this hop, or <see langword="null"/> if
    /// nothing is.
    /// </summary>
    /// <returns>A description of the defect, or <see langword="null"/>.</returns>
    public string? WhatIsStillLive()
    {
        var faults = new List<string>();

        check(_clientToServer.Reader, "client→server");
        check(_serverToClient.Reader, "server→client");

        return faults.Count is 0 ? null : $"{Name}: {string.Join("; ", faults)}";

        void check(PipeReader reader, string direction)
        {
            try
            {
                if (!reader.TryRead(out var result))
                {
                    // Nothing buffered and the writer is still open: the peer
                    // could still deliver, which is exactly a live pipe.
                    faults.Add($"{direction} is still open for writing");
                    return;
                }

                if (!result.IsCompleted)
                {
                    faults.Add($"{direction} is still open for writing, with {result.Buffer.Length} unread bytes");
                }

                reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
            }
            catch (InvalidOperationException)
            {
                // The reader has already been completed, which is the state
                // this method is checking for. Completed is closed.
            }
        }
    }

    private static Pipe NewPipe() => new(new PipeOptions(
        pauseWriterThreshold: PauseWriterThreshold,
        resumeWriterThreshold: ResumeWriterThreshold,
        useSynchronizationContext: false));
}
