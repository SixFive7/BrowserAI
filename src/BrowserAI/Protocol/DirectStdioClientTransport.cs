// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Protocol;

/// <summary>
/// How BrowserAI starts its <c>@playwright/mcp</c> child: the named executable,
/// directly, with nothing between the two processes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The SDK's <c>StdioClientTransport</c> cannot be configured into this
/// shape.</b> Read from the shipped 2.2.0 source: on Windows it rewrites every
/// command whose filename is not <c>cmd.exe</c> into
/// <c>cmd.exe /c &lt;command&gt; …</c>, unconditionally and with no opt-out. The
/// consequences are not cosmetic:
/// </para>
/// <list type="number">
/// <item>
/// A shell sits between BrowserAI and <c>node</c>, so the process BrowserAI can
/// see is not the process it needs to own — which breaks tree ownership and
/// exit-code attribution, both of which the job object at step 6 depends on.
/// </item>
/// <item>
/// Argument fidelity is lost. Measured against a node probe: a literal
/// <c>%USERNAME%</c> reached the child as the expanded value, and an argument
/// containing whitespace <b>and</b> <c>&amp;</c> made the child fail to start
/// outright — <c>'C:/Program' is not recognized</c> — because the SDK's
/// caret-escaping skips arguments that contain whitespace and cmd then splits
/// the command path, which contains a space in the stock Node install location.
/// </item>
/// </list>
/// <para>
/// <c>IClientTransport</c> is two members, so replacing it is cheaper than
/// working around it.
/// </para>
/// </remarks>
internal sealed class DirectStdioClientTransport : IClientTransport
{
    /// <summary>
    /// Diagnostics, not protocol. A child that writes a byte this decoder
    /// cannot make sense of must not take the session down with it, so unlike
    /// <see cref="StdioChannel.Utf8NoBom"/> this one substitutes rather than
    /// throws — an unreadable log line is a worse log line, and a dead session.
    /// </summary>
    private static readonly UTF8Encoding LenientUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    private readonly ChildProcessOptions _options;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>Prepares a transport. Nothing is started until <see cref="ConnectAsync"/>.</summary>
    /// <param name="options">What to start, and how.</param>
    /// <param name="loggerFactory">Where the transport and its session log.</param>
    public DirectStdioClientTransport(ChildProcessOptions options, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _loggerFactory = loggerFactory;
        Name = options.Name ?? $"child ({Path.GetFileName(options.Command)})";
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var logger = _loggerFactory?.CreateLogger<DirectStdioClientTransport>();
        var startInfo = BuildStartInfo();

        Process? process = null;
        DataReceivedEventHandler? standardErrorHandler = null;
        var started = false;

        try
        {
            process = new Process { StartInfo = startInfo };

            // Subscribed BEFORE Start. A child that writes to stderr in its
            // first milliseconds -- which upstream does, on every healthy
            // launch -- would otherwise lose those lines to a handler attached
            // afterwards, and the lines lost are exactly the ones that explain
            // a failure to start.
            standardErrorHandler = (_, e) => OnStandardErrorLine(logger, e.Data);
            process.ErrorDataReceived += standardErrorHandler;

            started = process.Start();

            if (!started)
            {
                throw new IOException($"'{_options.Command}' did not start, and Windows reported no reason.");
            }

            // BeginErrorReadLine can only run after Start. Nothing is lost in
            // between: the pipe buffers whatever the child wrote first.
            process.BeginErrorReadLine();

            if (logger is not null)
            {
                TransportLog.ChildStarted(logger, Name, _options.Command, process.Id, _options.WorkingDirectory);
            }

            return Task.FromResult<ITransport>(
                new ChildProcessSession(process, standardErrorHandler, Name, _options.ShutdownTimeout, _loggerFactory));
        }
        catch (Exception ex)
        {
            if (process is not null)
            {
                if (standardErrorHandler is not null)
                {
                    process.ErrorDataReceived -= standardErrorHandler;
                }

                KillOnFailedConnect(process, started);
                process.Dispose();
            }

            throw new IOException($"Could not start '{_options.Command}' in '{_options.WorkingDirectory}'.", ex);
        }
    }

    private static void KillOnFailedConnect(Process process, bool started)
    {
        if (!started)
        {
            return;
        }

        try
        {
            // The whole tree: node does not take its own children with it, and
            // a half-connected child that survives this method is a leak with
            // nothing left holding a reference to it. The job object at step 6
            // is what makes this belt-and-braces rather than the only guard.
            process.Kill(entireProcessTree: true);
        }
#pragma warning disable CA1031 // A connect that already failed is not made worse by a kill that also failed; the IOException below carries the real reason.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private void OnStandardErrorLine(ILogger? logger, string? line)
    {
        if (line is null)
        {
            return;
        }

        if (logger is not null)
        {
            TransportLog.ChildStandardError(logger, Name, line);
        }

        try
        {
            _options.StandardErrorLines?.Invoke(line);
        }
#pragma warning disable CA1031 // This runs on the thread that dispatches ErrorDataReceived; an exception escaping it takes down the process.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            if (logger is not null)
            {
                TransportLog.StandardErrorCallbackFailed(logger, Name, ex);
            }
        }
    }

    private ProcessStartInfo BuildStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.Command,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,

            // Explicit, never left unset. .NET passes null to CreateProcess for
            // an unset WorkingDirectory, and the child then inherits whatever
            // directory the MCP client happened to be launched from -- which
            // for `@playwright/mcp` decides where relative paths land.
            // ChildProcessOptions makes this required so the mistake cannot be
            // made by omission.
            WorkingDirectory = _options.WorkingDirectory,

            // Applies to the stderr StreamReader only. stdin and stdout are
            // driven as raw byte streams by ChildProcessSession, so no encoder
            // exists on the protocol path at all -- which is a stronger
            // guarantee than setting the right one.
            StandardErrorEncoding = LenientUtf8,
        };

        foreach (var argument in _options.Arguments)
        {
            // ArgumentList, never Arguments: .NET quotes each element for
            // CreateProcess itself. The two are mutually exclusive and setting
            // both is undefined.
            startInfo.ArgumentList.Add(argument);
        }

        // Clear FIRST. ProcessStartInfo.Environment arrives pre-populated with
        // this process's own block and assignment merges into it, so an
        // allowlist that skips this line is a policy that does nothing.
        startInfo.Environment.Clear();

        foreach (var (name, value) in _options.Environment)
        {
            startInfo.Environment[name] = value;
        }

        return startInfo;
    }
}

/// <summary>What <see cref="DirectStdioClientTransport"/> starts, and how.</summary>
internal sealed class ChildProcessOptions
{
    /// <summary>The executable to run. An absolute path; nothing resolves it.</summary>
    public required string Command { get; init; }

    /// <summary>
    /// The working directory, which is required rather than optional because
    /// the failure of leaving it unset is invisible: the child inherits the
    /// caller's.
    /// </summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// The child's complete environment, normally from
    /// <see cref="ChildEnvironment.Build"/>. It replaces this process's block
    /// rather than adding to it.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Environment { get; init; }

    /// <summary>Arguments, passed verbatim and quoted by .NET for <c>CreateProcess</c>.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>How long a child gets to exit after its stdin closes before it is killed.</summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Invoked for each line the child writes to stderr.</summary>
    public Action<string>? StandardErrorLines { get; init; }

    /// <summary>The transport's name in diagnostics. Defaults to the command's filename.</summary>
    public string? Name { get; init; }
}
