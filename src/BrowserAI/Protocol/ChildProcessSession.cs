// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using BrowserAI.Interop;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Protocol;

/// <summary>
/// The live half of <see cref="DirectStdioClientTransport"/>: one child process,
/// the job object that contains it, its three pipes, and its exit code.
/// </summary>
/// <remarks>
/// <para>
/// The SDK's equivalent is <c>internal</c>, so this is written against the
/// public <c>TransportBase</c> rather than derived from it. What that costs is
/// this file; what it buys is that the process BrowserAI holds is the process
/// it started.
/// </para>
/// <para>
/// <b>This object owns the job handle for the child's whole life</b>, and that
/// is the containment guarantee rather than a detail of it: if BrowserAI dies —
/// crash, <c>TerminateProcess</c>, a session limit, a power of ten of other
/// reasons — the kernel closes the last handle and every process in the job goes
/// with it. Nothing has to run for that to happen, which is the point. A cleanup
/// path that must execute is a cleanup path that will one day not.
/// </para>
/// </remarks>
internal sealed class ChildProcessSession : JsonLinesTransport
{
    private static readonly ReadOnlyMemory<byte> Terminator = "\n"u8.ToArray();

    /// <summary>
    /// Diagnostics, not protocol. A child that writes a byte this decoder
    /// cannot make sense of must not take the session down with it, so unlike
    /// <see cref="StdioChannel.Utf8NoBom"/> this one substitutes rather than
    /// throws — an unreadable log line is a worse log line, and a dead session.
    /// </summary>
    private static readonly UTF8Encoding LenientUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    /// <summary>
    /// How long the stderr reader is given to drain after the child exits. It
    /// is bounded because a grandchild that inherited the write end can hold it
    /// open, and a teardown must not wait on a process nobody is tracking.
    /// </summary>
    private static readonly TimeSpan StandardErrorDrainTimeout = TimeSpan.FromSeconds(2);

    private readonly LaunchedProcess _process;
    private readonly Action<string>? _standardErrorLines;
    private readonly TimeSpan _shutdownTimeout;
    private readonly ILogger? _logger;
    private readonly Stream _standardInput;
    private readonly Task _standardErrorPump;

    private int _disposed;

    /// <summary>Adopts a child that has already been launched into a job.</summary>
    /// <param name="job">The job the child was created in. This object owns it.</param>
    /// <param name="process">The started child. This object owns it.</param>
    /// <param name="standardErrorLines">Invoked for each line the child writes to stderr.</param>
    /// <param name="name">The transport's name in diagnostics.</param>
    /// <param name="shutdownTimeout">How long the child gets to exit on its own.</param>
    /// <param name="loggerFactory">Where the session logs.</param>
    public ChildProcessSession(
        JobObject job,
        LaunchedProcess process,
        Action<string>? standardErrorLines,
        string name,
        TimeSpan shutdownTimeout,
        ILoggerFactory? loggerFactory)
        : base(name, JsonLinesRole.ChildFacing, loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(process);

        Job = job;
        _process = process;
        _standardErrorLines = standardErrorLines;
        _shutdownTimeout = shutdownTimeout;
        _logger = loggerFactory?.CreateLogger<ChildProcessSession>();

        // Read once and cached, for the same reason the exit code is: a pid
        // that can only be read while the process object is alive is useless to
        // everything that needs it afterwards.
        ProcessId = process.Id;
        _standardInput = process.StandardInput;

        // Nothing a child writes to stderr can be lost, and it is not a matter
        // of timing: the pipe exists before the process does, so the earliest
        // possible byte is already buffered by the time this reader starts.
        // Five lines written by a child that then fails to launch are the only
        // explanation there will ever be.
        _standardErrorPump = Task.Run(PumpStandardErrorAsync, CancellationToken.None);

        StartReading(process.StandardOutput);
    }

    /// <summary>The child's process id, readable for the life of this object.</summary>
    public int ProcessId { get; }

    /// <summary>
    /// The child's exit code, once it has one, and readable after the
    /// underlying handles have been closed.
    /// </summary>
    /// <remarks>
    /// <b>This is why it is cached as an <see cref="int"/> the instant it
    /// exists.</b> The framework's <c>Process.ExitCode</c> throws after
    /// <c>Dispose()</c>, so the ordinary shape — dispose in a <c>finally</c>,
    /// report the exit code afterwards — reports nothing, and the thing it fails
    /// to report is why the child died.
    /// </remarks>
    public int? ExitCode { get; private set; }

    /// <summary>
    /// The job containing the child and every process it spawns, exposed so the
    /// suite can assert on the flags that are actually set rather than on the
    /// ones the code meant to set.
    /// </summary>
    internal JobObject Job { get; }

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
            // which is what stops the child.
            await base.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            // Belt and braces: if shutdown threw before caching, this is the
            // last moment the handle is still valid.
            CacheExitCode();

            await DrainStandardErrorAsync().ConfigureAwait(false);

            _process.Dispose();

            // Last, and only after the exit code has been read. Closing this
            // handle is what terminates anything still alive in the job.
            Job.Dispose();
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

        if (!await _process.WaitForExitAsync(_shutdownTimeout).ConfigureAwait(false))
        {
            await TerminateThroughTheJobAsync().ConfigureAwait(false);
        }

        CacheExitCode();

        if (_logger is not null && ExitCode is { } exitCode)
        {
            TransportLog.ChildExited(_logger, Name, ProcessId, exitCode);
        }
    }

    private async Task TerminateThroughTheJobAsync()
    {
        if (_logger is not null)
        {
            TransportLog.ChildKilled(_logger, Name, ProcessId, _shutdownTimeout);
        }

        // Closing the job handle, never a process-tree kill. A tree walk
        // follows parent-child links, which are re-parentable and pid-reusable,
        // and it loses a race against anything that respawns while it walks.
        // The job has neither problem: the kernel terminates every member at
        // once, and a process created inside it mid-teardown is already a
        // member.
        Job.Dispose();

        // Bounded, and its result is deliberately ignored: the exit code read
        // by the caller is the report, and there is nothing further this code
        // could do about a process the kernel has been told to terminate.
        _ = await _process.WaitForExitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
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

    private void CacheExitCode()
    {
        if (ExitCode is not null)
        {
            return;
        }

        try
        {
            ExitCode = _process.TryReadExitCode();
        }
#pragma warning disable CA1031 // A process that cannot report an exit code leaves this null, which is the honest answer.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private async Task DrainStandardErrorAsync()
    {
        try
        {
            await _standardErrorPump.WaitAsync(StandardErrorDrainTimeout).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A stderr reader that will not finish must not turn a teardown into a hang.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private async Task PumpStandardErrorAsync()
    {
        try
        {
            using var reader = new StreamReader(_process.StandardError, LenientUtf8);

            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                OnStandardErrorLine(line);
            }
        }
#pragma warning disable CA1031 // The pipe closing under a read in flight is how this loop normally ends.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private void OnStandardErrorLine(string line)
    {
        if (_logger is not null)
        {
            TransportLog.ChildStandardError(_logger, Name, line);
        }

        try
        {
            _standardErrorLines?.Invoke(line);
        }
#pragma warning disable CA1031 // This runs on the stderr reader; an exception escaping it would take down the process.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            if (_logger is not null)
            {
                TransportLog.StandardErrorCallbackFailed(_logger, Name, ex);
            }
        }
    }
}
