// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Protocol;

/// <summary>
/// The live half of <see cref="DirectStdioClientTransport"/>: one child process,
/// its two pipes, and its exit code.
/// </summary>
/// <remarks>
/// The SDK's equivalent is <c>internal</c>, so this is written against the
/// public <c>TransportBase</c> rather than derived from it. What that costs is
/// this file; what it buys is that the process BrowserAI holds is the process
/// it started.
/// </remarks>
internal sealed class ChildProcessSession : JsonLinesTransport
{
    private static readonly ReadOnlyMemory<byte> Terminator = "\n"u8.ToArray();

    private readonly Process _process;
    private readonly DataReceivedEventHandler _standardErrorHandler;
    private readonly TimeSpan _shutdownTimeout;
    private readonly ILogger? _logger;
    private readonly Stream _standardInput;

    private int _disposed;

    /// <summary>Adopts an already-started process.</summary>
    /// <param name="process">The started child.</param>
    /// <param name="standardErrorHandler">The handler to detach on disposal.</param>
    /// <param name="name">The transport's name in diagnostics.</param>
    /// <param name="shutdownTimeout">How long the child gets to exit on its own.</param>
    /// <param name="loggerFactory">Where the session logs.</param>
    public ChildProcessSession(
        Process process,
        DataReceivedEventHandler standardErrorHandler,
        string name,
        TimeSpan shutdownTimeout,
        ILoggerFactory? loggerFactory)
        : base(name, loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(process);

        _process = process;
        _standardErrorHandler = standardErrorHandler;
        _shutdownTimeout = shutdownTimeout;
        _logger = loggerFactory?.CreateLogger<ChildProcessSession>();

        // Read once and cached. Process.Id throws after Dispose() for the same
        // reason ExitCode does, and a pid that can only be read while the
        // object is alive is useless to everything that needs it after.
        ProcessId = process.Id;

        // The BaseStream, not the StreamWriter over it. Frames are already
        // UTF-8 by the time they reach here, so putting an encoder in front of
        // them could only introduce a BOM or a CRLF -- two of the three
        // failures StdioChannel exists to prevent, reappearing on the child
        // pipe where nobody was looking for them.
        _standardInput = process.StandardInput.BaseStream;

        StartReading(process.StandardOutput.BaseStream);
    }

    /// <summary>The child's process id, readable for the life of this object.</summary>
    public int ProcessId { get; }

    /// <summary>
    /// The child's exit code, once it has one, and readable after the
    /// underlying <see cref="Process"/> has been disposed.
    /// </summary>
    /// <remarks>
    /// <b>This is why it is cached as an <see cref="int"/> the instant it
    /// exists.</b> <see cref="Process.ExitCode"/> throws after
    /// <see cref="Process.Dispose"/>, so the ordinary shape — dispose in a
    /// <c>finally</c>, report the exit code afterwards — reports nothing, and
    /// the thing it fails to report is why the child died.
    /// </remarks>
    public int? ExitCode { get; private set; }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        try
        {
            // The base ends the read loop and calls ShutdownPeerAsync below,
            // which is what stops the child. The Process object stays alive
            // across that, because the read loop is still draining its stdout.
            await base.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _process.ErrorDataReceived -= _standardErrorHandler;

            // Belt and braces: if shutdown threw before caching, this is the
            // last moment the handle is still valid.
            CacheExitCode();
            _process.Dispose();
        }
    }

    /// <inheritdoc />
    protected override async ValueTask WriteFrameAsync(ReadOnlyMemory<byte> utf8Payload, CancellationToken cancellationToken)
    {
        await _standardInput.WriteAsync(utf8Payload, cancellationToken).ConfigureAwait(false);
        await _standardInput.WriteAsync(Terminator, cancellationToken).ConfigureAwait(false);
        await _standardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async ValueTask ShutdownPeerAsync()
    {
        // Closing stdin is the graceful path and the only one upstream
        // recognises: `@playwright/mcp`'s exit watchdog hooks stdin close and
        // runs gracefullyCloseAll(). Killing first would skip it and leave
        // profile directories mid-write.
        CloseStandardInput();

        try
        {
            using var deadline = new CancellationTokenSource(_shutdownTimeout);

            // WaitForExitAsync, never WaitForExit(int). Only the async form and
            // the parameterless one drain the async stderr reader, so the
            // timed overload silently truncates the last thing the child said
            // -- which, when a child is being killed, is the interesting part.
            await _process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillTree();
        }
#pragma warning disable CA1031 // The child is going away regardless; what matters is that the code below still runs.
        catch (Exception)
#pragma warning restore CA1031
        {
            KillTree();
        }

        CacheExitCode();

        if (_logger is not null && ExitCode is { } exitCode)
        {
            TransportLog.ChildExited(_logger, Name, ProcessId, exitCode);
        }
    }

    private void CloseStandardInput()
    {
        try
        {
            _standardInput.Close();
        }
#pragma warning disable CA1031 // A child that already exited has already closed it for us.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private void KillTree()
    {
        try
        {
            if (_process.HasExited)
            {
                return;
            }

            if (_logger is not null)
            {
                TransportLog.ChildKilled(_logger, Name, ProcessId, _shutdownTimeout);
            }

            // entireProcessTree, because node does not take its children with
            // it. This is the fallback; the job object at step 6 is what makes
            // containment hold when BrowserAI is the thing being killed and
            // this line never runs at all.
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit();
        }
#pragma warning disable CA1031 // Nothing above this can act on why a kill failed, and the exit code below still reports what happened.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private void CacheExitCode()
    {
        if (ExitCode is not null)
        {
            return;
        }

        try
        {
            if (_process.HasExited)
            {
                ExitCode = _process.ExitCode;
            }
        }
#pragma warning disable CA1031 // A process that cannot report an exit code leaves this null, which is the honest answer.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
