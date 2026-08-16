// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;
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
/// exit-code attribution, both of which the job object depends on.
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
/// <para>
/// <b>It is also not <c>Process.Start</c> underneath.</b> The child has to be a
/// member of a job object from the instant it exists, and .NET cannot express
/// that: <c>ProcessStartInfo</c> has no creation-flags surface. Starting first
/// and assigning afterwards was measured leaking grandchildren, so the launch
/// goes through <see cref="JobLauncher"/> and this class supplies the policy —
/// what to run, where, and with which environment.
/// </para>
/// </remarks>
internal sealed class DirectStdioClientTransport : IClientTransport
{
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

        // One job per child, created fresh here. BrowserAI itself is never a
        // member of it: with several sessions in one process that would fuse
        // every tree together and make BrowserAI a casualty of any single
        // teardown.
        var job = JobObject.CreateKillOnClose();
        LaunchedProcess? process = null;

        try
        {
            process = JobLauncher.Start(
                job,
                _options.Command,
                _options.Arguments,
                // Explicit, never left unset. Passed null, CreateProcessW gives
                // the child whatever directory the MCP client happened to be
                // launched from -- which for `@playwright/mcp` decides where
                // relative paths land. ChildProcessOptions makes it required so
                // the mistake cannot be made by omission.
                _options.WorkingDirectory,
                // The complete block, replacing ours rather than merging into
                // it. JobLauncher builds it from exactly these entries and
                // nothing else, so an allowlist here is a policy that holds.
                _options.Environment);

            if (logger is not null)
            {
                TransportLog.ChildStarted(logger, Name, _options.Command, process.Id, _options.WorkingDirectory);
            }

            return Task.FromResult<ITransport>(
                new ChildProcessSession(job, process, _options.StandardErrorLines, Name, _options.ShutdownTimeout, _loggerFactory));
        }
        catch (Exception ex)
        {
            // Closing the job is the kill path, and it is total: a child that
            // got far enough to spawn grandchildren before the failure takes
            // them with it. Nothing here enumerates or matches anything.
            job.Dispose();
            process?.Dispose();

            throw new IOException($"Could not start '{_options.Command}' in '{_options.WorkingDirectory}'.", ex);
        }
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

    /// <summary>
    /// Arguments, passed verbatim. <see cref="JobLauncher"/> does the quoting,
    /// because the command line reaches <c>CreateProcessW</c> as one buffer
    /// rather than as a list.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>How long a child gets to exit after its stdin closes before it is killed.</summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Invoked for each line the child writes to stderr.</summary>
    public Action<string>? StandardErrorLines { get; init; }

    /// <summary>The transport's name in diagnostics. Defaults to the command's filename.</summary>
    public string? Name { get; init; }
}
