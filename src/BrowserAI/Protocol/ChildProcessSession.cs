// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
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
/// public <c>TransportBase</c> and not derived from it. What that costs is
/// this file; what it buys is that the process BrowserAI holds is the process
/// it started.
/// </para>
/// <para>
/// <b>This object owns the job handle for the child's whole life</b>, and that
/// is the containment guarantee, not a detail of it: if BrowserAI dies --
/// crash, <c>TerminateProcess</c>, a session limit, a power of ten of other
/// reasons -- the kernel closes the last handle and every process in the job goes
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
    /// <see cref="StdioChannel.Utf8NoBom"/> this one substitutes and does not
    /// throw -- an unreadable log line is a worse log line, and a dead session.
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
        //
        // [ASSUMED] That nothing a child writes to stderr can be lost. The claim
        // is broader than both the measurement behind it and the code below it,
        // which abandons the pump after two seconds. Settle it by writing to
        // stderr across that boundary and counting what arrives. Tagged
        // 2026-09-23; the list and the predicate are in TODO.md.
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
    /// <c>Dispose()</c>, so the ordinary shape -- dispose in a <c>finally</c>,
    /// report the exit code afterwards -- reports nothing, and the thing it fails
    /// to report is why the child died.
    /// </remarks>
    public int? ExitCode { get; private set; }

    /// <summary>
    /// Whether the child process itself has ended, read from the process handle
    /// this object holds open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The handle, never a pid lookup.</b> This object owns an open handle to
    /// the child for the child's whole life, which is what makes the answer
    /// about <i>this</i> process and not about whatever now wears its
    /// number -- Windows will not recycle a pid while a handle to it exists.
    /// </para>
    /// <para>
    /// <b>A child on its way out is alive until the handle says otherwise</b>,
    /// which is the property that keeps this from racing an exit: the wait is
    /// signalled by the kernel when the process object is terminated, not when
    /// something decides it ought to be.
    /// </para>
    /// <para>
    /// ⚠️ <b>It answers <see langword="true"/> once this transport has been
    /// disposed</b>, because the handle is closed by then and there is no child
    /// left to ask about. A failed query answers <see langword="false"/>: an
    /// instrument that could not read is not evidence that the child is gone,
    /// and <c>IsConnected</c> is the other half of the
    /// question anyway.
    /// </para>
    /// </remarks>
    public bool HasExited
    {
        get
        {
            if (Volatile.Read(ref _disposed) is not 0)
            {
                return true;
            }

            try
            {
                return _process.HasExited;
            }
            catch (Win32Exception)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }
    }

    /// <summary>
    /// The job containing the child and every process it spawns, exposed so the
    /// suite can assert on the flags that are actually set and not on the
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
    /// <remarks>
    /// <b><see langword="true"/>, because this transport owns the peer.</b> It
    /// started the child, it holds the write end of the child's stdin, and the
    /// job object below it is the guarantee: closing stdin ends the child,
    /// the child's exit closes the pipe this loop reads, and the read then
    /// returns end-of-file on its own.
    /// </remarks>
    protected override async ValueTask<bool> ShutdownPeerAsync()
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

        // The child is gone either way by this point -- gracefully above, or
        // through its job -- so its end of the pipe is closed and the read loop
        // is on its way out of its own accord.
        return true;
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

    /// <summary>
    /// One line off the child's stderr, classified before it is logged.
    /// </summary>
    /// <remarks>
    /// <b>Both directions are the requirement, because each is half of the bug
    /// this replaces.</b> A benign line logged loudly trains a reader to ignore
    /// warnings -- <c>@playwright/mcp</c> writes <c>Session: &lt;path&gt;</c> on
    /// every healthy start with session logging on -- and an error-shaped line
    /// logged quietly is a startup failure nobody sees. The classification is
    /// therefore the level: <see cref="StandardErrorClassifier"/> decides, and
    /// nothing here re-decides it.
    /// </remarks>
    /// <param name="line">The line, exactly as the child wrote it.</param>
    private void OnStandardErrorLine(string line)
    {
        if (_logger is not null)
        {
            if (StandardErrorClassifier.LooksLikeError(line))
            {
                TransportLog.ChildStandardErrorDiagnostic(_logger, Name, line);
            }
            else
            {
                TransportLog.ChildStandardError(_logger, Name, line);
            }
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
