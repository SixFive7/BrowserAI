// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Logging;
using BrowserAI.Proxy;
using BrowserAI.Runtime;

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
internal sealed class LiveSession(
    SessionPath location,
    SessionLock sessionLock,
    SessionModeDefinition mode,
    ChildConnection child,
    SessionLogging logging,
    GeneratedConfig config,
    string configFile,
    bool createdHere) : IAsyncDisposable
{
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

    /// <summary>Whether the notice about driving somebody else's session has been given.</summary>
    public bool NoticeGiven { get; set; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        // The child first. Disposing it closes the child's stdin, which is
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
