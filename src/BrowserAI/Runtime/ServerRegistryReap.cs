// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;
using BrowserAI.Protocol;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Runtime;

/// <summary>
/// Starts Playwright's own reaper over Playwright's own server registry: one
/// detached process, never awaited, at the moment a close has just made one of
/// that registry's descriptors dead.
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole mechanism is that somebody has to call <c>list()</c>, and nobody
/// did.</b> <c>playwright-core</c> writes one JSON descriptor per browser bind
/// into <c>%LOCALAPPDATA%\ms-playwright\b</c> -- a directory
/// <c>PLAYWRIGHT_BROWSERS_PATH</c> does not move -- and <c>browser.ts</c>'s
/// <c>stop()</c> skips the delete for a <b>persistent</b> profile, which is every
/// browser this product opens. The only code upstream has that unlinks a dead
/// descriptor is inside <c>ServerRegistry.list()</c>, whose own call site upstream
/// carries the comment <i>List early to GC</i>. Nothing called it, so the
/// directory only grew: <b>3,815 descriptors on this machine on 2026-09-24</b>, at
/// about <b>499 a day</b> from the suite and <b>3.5 a day</b> from the installed
/// product, and <c>playwright uninstall --all</c> never touches it
/// ([kb](../../../kb/playwright/tools-and-artifacts.md#every-launched-browser-leaves-a-descriptor-in-localappdatams-playwrightb-and-nothing-reaps-it----measured-2026-09-16)).
/// </para>
/// <para>
/// <b>What makes the growth matter is that reading it is quadratic</b>, which is
/// also what makes this call expensive exactly once: 125 entries 261 ms, 1,000
/// entries 9,911 ms, <b>3,762 entries 141,616 ms</b> -- 2.4 minutes for a single
/// call at the size this machine already stood at, and milliseconds afterwards.
/// What degrades is Playwright's own dashboard, <c>list</c> and attach; nothing in
/// BrowserAI reads this directory and disk was never the problem.
/// </para>
/// <para>
/// <b>Decided as T7</b>
/// ([DECISIONS](../../../DECISIONS.md#processes-browsers-and-session-modes)), in
/// the maintainer's words: <i>"I want to avoid complexity. Does throtthling not
/// mean we need to implement anything ourselves? I'm leaning towards c."</i> So
/// there is <b>no throttle, no concurrency arm and no bookkeeping of our own</b>,
/// and three things are accepted by name. <b>A detached call that never runs,
/// fails or hangs is one nobody hears</b> -- that is the price of not awaiting it
/// at the one moment a session is closing, and the record below is all that is
/// left of it. <b>Eight concurrent <c>list()</c> callers reaped nothing live, 8 of
/// 8</b>, and beyond eight the pipe-busy false positive is <b>unmeasured</b>: a
/// caller that cannot open a live browser's pipe would unlink a live descriptor,
/// which costs discoverability and never a browser. And <b>the first call on a
/// real backlog costs those 2.4 minutes once</b>, in a process nobody is waiting
/// for.
/// </para>
/// <para>
/// ⚠️ <b>It is the one-line <c>serverRegistry.list()</c> and not the CLI client's
/// <c>list</c> command, and that is a decision rather than a shortcut.</b> Both
/// reach the same reaper -- <c>collectList</c> in
/// <c>lib/tools/cli-client/program.js</c> calls <c>serverRegistry.list()</c>
/// before it needs anything from it, which is the <i>List early to GC</i> line --
/// but the command brings two things with it that BrowserAI has no business
/// bringing to a session close. It resolves a <b>workspace</b> by walking up ten
/// directories from its working directory looking for <c>.playwright</c>, so the
/// same code would do different things depending on where a session directory
/// happens to sit; and having resolved one, it <b>deletes that workspace's dead
/// daemon session configs</b> under <c>%LOCALAPPDATA%\ms-playwright\daemon</c> --
/// a second registry this product never writes and was never asked to prune.
/// Requiring <c>lib/serverRegistry.js</c> by absolute path is exactly the reap and
/// nothing else. <b>It is an internal module and not a documented entry
/// point</b>, which is why its absence is a logged fact below and why the
/// re-verification row for this mechanism is keyed on the <c>playwright-core</c>
/// version.
/// </para>
/// <para>
/// <b>Detached here means: in no job, inheriting nothing, and with its own
/// invisible console.</b> A job would be its undoing -- on the client-exit path
/// BrowserAI is gone moments later, so a reaper inside a job whose last handle
/// closes dies with the close that started it. Inheriting nothing is the stronger
/// half: <c>stdout</c> is the JSON-RPC channel, and the one way a child could ever
/// reach it is by inheriting the handle, so
/// <see cref="JobLauncher.StartDetached"/> passes <c>bInheritHandles: FALSE</c>
/// and names no standard handle at all. <c>CREATE_NO_WINDOW</c> then gives the
/// child a console of its own with no window on it, which is where its output goes
/// and dies. ⚠️ <b>A job this process is itself inside would still take the reaper
/// with it</b>, and no breakaway is attempted: a job that forbids breakaway fails
/// the launch outright, and the cost either way is the prune and nothing else.
/// </para>
/// <para>
/// <b>Started only where a browser tree really went down</b>, which is what the
/// callers' condition is about and not tidiness. A descriptor is written at a
/// browser <b>bind</b>, so a session that never bound one made nothing dead and
/// has nothing to collect -- and the suite closes sessions against in-process
/// doubles by the hundred, none of which ever started a browser. A dead descriptor
/// nobody reaps today is collected by the next close that does, which is the sense
/// in which this is housekeeping and not a guarantee.
/// </para>
/// </remarks>
/// <param name="payload">Where <c>node.exe</c> and <c>playwright-core</c> live.</param>
/// <param name="logger">
/// The <b>process</b> log and never a session's own, because the registry is
/// machine-wide and so is every consequence of reaping it -- the same reason the
/// stray sweep records there.
/// </param>
internal sealed class ServerRegistryReap(PayloadLayout payload, ILogger logger)
{
    /// <summary>
    /// The one variable that moves the registry directory, forwarded to every
    /// child so that the suite can isolate a reap.
    /// </summary>
    /// <remarks>
    /// Read first by <c>registryDirectory()</c> and spelled <c>PWTEST_</c> rather
    /// than <c>PLAYWRIGHT_</c>, which is why the kb said for a week that a harness
    /// could not move this directory at all. It is in
    /// <see cref="ChildEnvironment.InheritedWhenSet"/> and nothing in this product
    /// ever sets it; the paragraph there is why forwarding it is the whole of the
    /// test hook.
    /// </remarks>
    public const string RegistryDirectoryVariable = "PWTEST_SERVER_REGISTRY";

    /// <summary>A session was destroyed, browser and all.</summary>
    public const string AfterDestroy = "a session was destroyed";

    /// <summary>A session's browser was closed for being idle.</summary>
    public const string AfterIdleClose = "a session's browser was closed for being idle";

    /// <summary>
    /// A session was torn down for a reason no caller asked for: the client went
    /// away, or this process is shutting down.
    /// </summary>
    public const string AfterTeardown = "the client went away or BrowserAI is shutting down";

    /// <summary>The stray sweep ended browsers no session accounted for.</summary>
    public const string AfterSweep = "the stray sweep ended browsers no session accounted for";

    /// <summary>
    /// The script the detached <c>node</c> runs: upstream's reaper, and nothing
    /// around it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Forward slashes, because the alternative is escaping a Windows path into
    /// a JavaScript string literal</b> -- node accepts them on Windows, and after
    /// the replacement there is no backslash left for the literal to mis-read. The
    /// one character a path under a user's profile can still carry is an
    /// apostrophe, and that is escaped.
    /// </para>
    /// <para>
    /// <b>The <c>exit</c> runs after the promise has settled</b>, so every unlink
    /// <c>list()</c> awaited has already happened; it is there because the call
    /// owns and disposes a filesystem watcher, and a watcher that failed to dispose
    /// would otherwise keep <c>node</c> alive with nothing left to do.
    /// </para>
    /// </remarks>
    /// <param name="module">The absolute path of <c>playwright-core</c>'s <c>lib/serverRegistry.js</c>.</param>
    /// <returns>One line of JavaScript, for <c>node -e</c>.</returns>
    internal static string ScriptFor(string module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var literal = module.Replace('\\', '/').Replace("'", "\\'", StringComparison.Ordinal);

        return $"require('{literal}').serverRegistry.list().then(() => process.exit(0), () => process.exit(1))";
    }

    /// <summary>Starts one reap and returns without waiting for anything.</summary>
    /// <remarks>
    /// <b>It cannot throw, and that is not defensiveness.</b> Two of its call
    /// sites are inside a teardown -- one of them
    /// <see cref="Sessions.LiveSession.DisposeAsync"/>, which a destroy awaits
    /// before it answers a caller -- so an exception here would turn a prune
    /// nobody asked for into a failed close. What a failure costs is the prune,
    /// and what it produces is the record.
    /// </remarks>
    /// <param name="cause">Which close this reap follows, for the record.</param>
    public void Start(string cause)
    {
        try
        {
            var module = payload.ServerRegistryModule;

            // Named, and deliberately not added to PayloadLayout.Verify: a
            // payload missing this file must cost the prune and never a session.
            // The check is here so the reason is a sentence in the log rather
            // than a CreateProcessW error code.
            if (!File.Exists(module) || !File.Exists(payload.NodeExecutable))
            {
                ReapLog.NothingToStartItFrom(logger, cause, module, payload.NodeExecutable);
                return;
            }

            var reaper = JobLauncher.StartDetached(
                payload.NodeExecutable,
                ["-e", ScriptFor(module)],

                // ⚠️ NEVER A DIRECTORY BROWSERAI OWNS, and the two that read like
                // the obvious answers are both wrong. A destroy deletes the
                // session tree the instant this returns, and a working directory
                // inside it is an open directory handle that makes the delete
                // report survivors; and `current\` is what an update replaces
                // wholesale. The profile is where `ClientCommandLine` sends a
                // command with no directory of its own, for the same reason: it
                // exists, and nothing this product does deletes it.
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify),

                // The same allowlist every child gets, which is what carries
                // PWTEST_SERVER_REGISTRY when this process has it -- without that
                // the reaper and the child would read two different directories
                // and no test could isolate either.
                ChildEnvironment.Build());

            ReapLog.Started(logger, cause, reaper.ProcessId, reaper.CreatedFileTime);
        }
#pragma warning disable CA1031 // A reap that cannot be started is a log record and a prune that did not happen. It is never a failed close.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            ReapLog.CouldNotStart(logger, cause, failure);
        }
    }
}

/// <summary>Source-generated log messages for the reap.</summary>
/// <remarks>
/// <para>
/// <b>The identity is spelled <c>&lt;pid&gt;@&lt;createdFileTime&gt;</c></b>, the
/// way <c>browserai.lock</c> and every record in the process log spell a process --
/// so that a reaper named here can be found again, and so that a stranger wearing
/// its number later cannot be mistaken for it.
/// </para>
/// <para>
/// <b>Event ids start at 90</b>, which is free: the highest this assembly had
/// taken was <c>PruneLog</c>'s 86, and a range of its own is what keeps a person
/// scanning <c>[nn]</c> in one machine-wide file from meeting two meanings under
/// one number -- the same reason <c>IdleLog</c> starts at 60 and
/// <c>ClientLivenessLog</c> at 70. <c>ProxyLogTests</c> holds the uniqueness that
/// is enforceable, which is uniqueness within the declaring class.
/// </para>
/// </remarks>
internal static partial class ReapLog
{
    [LoggerMessage(
        EventId = 90,
        Level = LogLevel.Information,
        Message = "Started Playwright's own server-registry reap after {Cause}, detached and not awaited: reaper={ProcessId}@{CreatedFileTime}.")]
    public static partial void Started(ILogger logger, string cause, int processId, long createdFileTime);

    [LoggerMessage(
        EventId = 91,
        Level = LogLevel.Warning,
        Message = "Playwright's own server-registry reap could not be started after {Cause}, which costs the prune and nothing else.")]
    public static partial void CouldNotStart(ILogger logger, string cause, Exception failure);

    [LoggerMessage(
        EventId = 92,
        Level = LogLevel.Warning,
        Message = "No server-registry reap after {Cause}: the payload has no '{Module}' or no '{Node}', so there is nothing to start it from.")]
    public static partial void NothingToStartItFrom(ILogger logger, string cause, string module, string node);
}
