// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Artifacts;
using BrowserAI.Logging;
using BrowserAI.Proxy;
using BrowserAI.Runtime;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Sessions;

/// <summary>
/// One session this process is driving: the directory it owns, the lock that
/// proves it, its own child, and its own log.
/// </summary>
/// <remarks>
/// <para>
/// <b>One job object per child, never one shared job.</b> A shared job fuses
/// every session's process tree together, so tearing down one session would kill
/// them all — and assigning BrowserAI itself would make it a casualty of its own
/// cleanup. The job lives inside <see cref="ChildConnection"/>'s transport, which
/// is per session by construction.
/// </para>
/// <para>
/// <b>Disposal releases the directory and leaves the record.</b> The holder
/// record outliving the holder is what makes a stale lock a sentence — <i>"held
/// by PID 1234 since 14:02, no longer running — reclaiming"</i> — rather than a
/// refusal, and reclaim is forever, so a torn-down session stays resumable
/// against its directory indefinitely.
/// </para>
/// </remarks>
/// <param name="location">The canonicalised session directory.</param>
/// <param name="sessionLock">The held lock. This object owns it.</param>
/// <param name="mode">The mode bound at <c>init</c>.</param>
/// <param name="child">The child driving this session. This object owns it.</param>
/// <param name="logging">This session's own logging stack. This object owns it.</param>
/// <param name="config">The config the child was started with.</param>
/// <param name="configFile">Where that config was written.</param>
/// <param name="createdHere">Whether this connection is the one that created the session.</param>
/// <param name="artifacts">Where this session's files go, and what it knows about them.</param>
/// <param name="idlePeriod">How long this session's browser may sit unused before it is closed.</param>
/// <param name="clock">The clock the idle timer reads. <see cref="TimeProvider.System"/> in the product.</param>
internal sealed class LiveSession(
    SessionPath location,
    SessionLock sessionLock,
    SessionModeDefinition mode,
    ChildConnection child,
    SessionLogging logging,
    GeneratedConfig config,
    string configFile,
    bool createdHere,
    ArtifactRouter artifacts,
    TimeSpan idlePeriod,
    TimeProvider clock) : IAsyncDisposable
{
    /// <summary>
    /// Upstream's own tool, spelled as upstream spells it.
    /// </summary>
    /// <remarks>
    /// <b>It is not a schema and it is not a rename.</b> The scope boundary
    /// forbids authoring a tool definition in C#; this is a <i>call</i> to a tool
    /// the child already advertises, whose name passes through byte for byte
    /// everywhere else. It is checked against the committed
    /// <c>upstream-snapshots/tools-list.json</c> by the suite, so an upstream
    /// rename turns the build red instead of turning the timer into a no-op.
    /// </remarks>
    public const string BrowserCloseTool = "browser_close";

    private int _disposed;

    /// <summary>The canonicalised session directory. It is the identity.</summary>
    public SessionPath Location { get; } = location;

    /// <summary>The held lock, and the record inside it.</summary>
    public SessionLock Lock { get; } = sessionLock;

    /// <summary>What this session is, bound at creation and never changed.</summary>
    public SessionModeDefinition Mode { get; } = mode;

    /// <summary>The <c>@playwright/mcp</c> child driving it.</summary>
    public ChildConnection Child { get; } = child;

    /// <summary>This session's own log file and level.</summary>
    public SessionLogging Logging { get; } = logging;

    /// <summary>The config the child was started with, and every opinion in it.</summary>
    public GeneratedConfig Config { get; } = config;

    /// <summary>Where that config was written.</summary>
    public string ConfigFile { get; } = configFile;

    /// <summary>
    /// Whether <b>this</b> connection created the session, as opposed to
    /// resuming one somebody else made.
    /// </summary>
    /// <remarks>
    /// There is no bearer token, so this is what recovers the guarantee a minted
    /// handle was going to provide: a caller driving a session it did not create
    /// is told so, at first use, rather than at reclaim time.
    /// </remarks>
    public bool CreatedHere { get; } = createdHere;

    /// <summary>
    /// Where this session's artifacts go, and the record of the ones that got
    /// there.
    /// </summary>
    public ArtifactRouter Artifacts { get; } = artifacts;

    /// <summary>Whether the notice about driving somebody else's session has been given.</summary>
    public bool NoticeGiven { get; set; }

    /// <summary>
    /// The one timer: this session's browser is closed once nothing has driven it
    /// for <see cref="BrowserIdleTimer.Period"/>, and the node child is kept.
    /// </summary>
    /// <remarks>
    /// It belongs to this lifetime rather than to the manager because everything
    /// it acts on does: one session is one child, one job and one log, and a
    /// timer owned anywhere else would need a way to name a session that has
    /// already gone.
    /// </remarks>
    public BrowserIdleTimer Idle { get; } = new(
        location.FullPath,
        idlePeriod,
        token => CloseBrowserAsync(child, token),
        logging.Factory.CreateLogger<BrowserIdleTimer>(),
        clock);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        // The timer first, so it cannot send a close into a child that is being
        // torn down -- and so a close already in flight is waited for here
        // rather than failing noisily against a closed transport.
        await Idle.DisposeAsync().ConfigureAwait(false);

        // The child next. Disposing it closes the child's stdin, which is
        // upstream's own graceful teardown path, and then closes the job handle,
        // which is what guarantees no browser is left behind.
        await Child.DisposeAsync().ConfigureAwait(false);

        Lock.Dispose();

        // Last, so anything logged on the way down still lands in the session's
        // own file -- and so the file handle is closed before a destroy tries to
        // delete the directory holding it.
        Logging.Dispose();

        TryDeleteConfig();
    }

    /// <summary>
    /// Closes this session's browser and keeps its node child, by calling
    /// upstream's own tool.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The tool is the mechanism, and nothing else would be.</b> Killing the
    /// browser out of the job would take the node child with it — the job is per
    /// child, which is the containment contract — and there is no other lever:
    /// Playwright owns the browser process, so asking Playwright is the only way
    /// to put it down without putting the session down too.
    /// </para>
    /// <para>
    /// <b>Measured 2026-08-16 against <c>@playwright/mcp</c> 0.0.79, twice.</b>
    /// The tool's own result text reads <c>await page.close()</c> and <i>"No open
    /// tabs"</i>, which reads as closing a tab and is not: closing the last page
    /// tears the persistent context down, and every process under the browsers
    /// root goes with it while the node child stays. Called again with no browser
    /// open it answers the same text and is not an error, so a close that races
    /// anything costs a round trip rather than a failure.
    /// </para>
    /// </remarks>
    private static async Task<BrowserCloseResult> CloseBrowserAsync(ChildConnection child, CancellationToken cancellationToken)
    {
        var before = child.JobProcessIds().Count;

        var answer = await child.AskAsync(
            RequestMethods.ToolsCall,
            new JsonObject
            {
                ["name"] = BrowserCloseTool,
                ["arguments"] = new JsonObject(),
            },
            cancellationToken).ConfigureAwait(false);

        if (answer.Response is null)
        {
            return new BrowserCloseResult(
                before,
                child.JobProcessIds().Count,
                answer.ProtocolFailure?.Message ?? answer.TransportFailure?.Message ?? "the child answered with neither a result nor an error");
        }

        return new BrowserCloseResult(before, child.JobProcessIds().Count, Failure: null);
    }

    private void TryDeleteConfig()
    {
        try
        {
            File.Delete(ConfigFile);
        }
#pragma warning disable CA1031 // A generated config that will not delete is litter in a per-run directory the next run sweeps.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
