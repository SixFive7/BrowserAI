// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Protocol;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The child leg of the harness: what <see cref="Protocol.DirectStdioClientTransport"/>
/// is, minus the process.
/// </summary>
/// <remarks>
/// <para>
/// <b>It reuses <c>JsonLinesTransport</c> rather than reimplementing framing,
/// and the split is exactly where the process boundary is.</b> The product's
/// client leg is a launcher, a job object, three pipes and a stderr pump
/// (<see cref="ChildProcessSession"/>) wrapped around framing and serialisation
/// shared with the server leg (<c>JsonLinesTransport</c>). This layer exists to
/// exercise the second half in milliseconds; the first half is what steps 5, 6
/// and 7 already prove against real processes, and re-proving it here would
/// need the processes back.
/// </para>
/// <para>
/// Stated plainly, because a harness that quietly covers less than it looks
/// like it does is the failure this project is about: <b>nothing below is
/// evidence about <see cref="ChildProcessSession"/></b> — not its job
/// ownership, not its exit-code caching, not its stderr drain.
/// </para>
/// </remarks>
/// <param name="link">The hop whose client end BrowserAI occupies.</param>
/// <param name="loggerFactory">Where the transport logs.</param>
internal sealed class PipeClientTransport(PipeDuplex link, ILoggerFactory? loggerFactory = null) : IClientTransport
{
    /// <inheritdoc />
    public string Name => "fake child (pipes)";

    /// <inheritdoc />
    public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<ITransport>(new PipeChildSession(link, loggerFactory));
    }
}

/// <summary>The live half of <see cref="PipeClientTransport"/>: one hop, no process.</summary>
internal sealed class PipeChildSession : JsonLinesTransport
{
    private static readonly ReadOnlyMemory<byte> Terminator = "\n"u8.ToArray();

    private readonly Stream _toChild;

    /// <summary>Connects to the child end of a hop.</summary>
    /// <param name="link">The hop to speak over.</param>
    /// <param name="loggerFactory">Where the session logs.</param>
    public PipeChildSession(PipeDuplex link, ILoggerFactory? loggerFactory)
        : base("fake child (pipes)", JsonLinesRole.ChildFacing, loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(link);

        _toChild = link.ClientWrites;
        StartReading(link.ClientReads);
    }

    /// <inheritdoc />
    protected override async ValueTask WriteFrameAsync(ReadOnlyMemory<byte> utf8Payload, CancellationToken cancellationToken)
    {
        await _toChild.WriteAsync(utf8Payload, cancellationToken);
        await _toChild.WriteAsync(Terminator, cancellationToken);
        await _toChild.FlushAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ShutdownPeerAsync() =>
        // Closing this end is the graceful path here for the same reason
        // closing stdin is against a real child: it is the only signal the peer
        // gets that the conversation is over.
        await _toChild.DisposeAsync();
}
