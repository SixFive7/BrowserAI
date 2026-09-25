// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging;

namespace BrowserAI.App;

/// <summary>How the app was started, as its arguments say.</summary>
internal enum StartMode
{
    /// <summary>By a person: the Start Menu, the installer's last step, a double click. It shows the window.</summary>
    User,

    /// <summary>By the per-user logon task at sign-in, <c>--sign-in</c>. It shows nothing.</summary>
    SignIn,

    /// <summary>Hidden, as the coordinator and nothing else, <c>--coordinate</c>. It shows nothing.</summary>
    Coordinate,
}

/// <summary>Reads the start mode out of the arguments.</summary>
internal static class StartModes
{
    /// <summary>The mode the arguments name.</summary>
    /// <remarks>
    /// <para>
    /// <b><c>--coordinate</c> wins over <c>--sign-in</c></b>, because the logon task
    /// starts the app with <c>--sign-in $(Arg0)</c> and a blocked server runs that
    /// task with <c>--coordinate</c> as its parameter, so an on-demand start carries
    /// both and means the second.
    /// </para>
    /// <para>
    /// <b>At sign-in the placeholder arrives as it is written.</b> A task run by its
    /// logon trigger has no parameter, and the scheduler then passes
    /// <c>$(Arg0)</c> literally, measured 2026-09-25; it is an argument this app does
    /// not know and it changes nothing.
    /// </para>
    /// </remarks>
    /// <param name="args">The command line.</param>
    /// <returns>The mode.</returns>
    public static StartMode Of(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args.Contains(CoordinatorProtocol.CoordinateArgument, StringComparer.Ordinal) ? StartMode.Coordinate
            : args.Contains(CoordinatorProtocol.SignInArgument, StringComparer.Ordinal) ? StartMode.SignIn
            : StartMode.User;
    }

    /// <summary>The verb a start in this mode hands to a coordinator that is already running.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns><see cref="CoordinatorVerb.Show"/> for a person's start, <see cref="CoordinatorVerb.Recheck"/> for every other.</returns>
    public static CoordinatorVerb VerbOf(StartMode mode) => mode is StartMode.User ? CoordinatorVerb.Show : CoordinatorVerb.Recheck;
}

/// <summary>The coordinator's window, as the coordinator sees it.</summary>
/// <remarks>
/// <b>A seam, so that a <c>show</c> in the suite is a recorded call and never a
/// window.</b> The product's is <see cref="ConfigurationWindow"/>.
/// </remarks>
internal interface ICoordinatorWindow
{
    /// <summary>Shows the window and returns when it closes.</summary>
    /// <returns>Zero when the window ran.</returns>
    int Show();
}

/// <summary>Why the coordinator stopped.</summary>
internal enum CoordinatorEnd
{
    /// <summary>No newer package is staged, so there is nothing to coordinate.</summary>
    NothingPending,

    /// <summary>Nothing else ran from the install, and the staged package was handed to Velopack.</summary>
    Applied,
}

/// <summary>
/// What the coordinator does while it holds its pipe: act on the verbs it is
/// handed, and apply the staged package once nothing else runs from the install.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q280 b and Q285 a, the maintainer's words verbatim: <i>"Q285 a"</i>. Phase
/// 2's minimal form.</b> Each pass asks whether a newer package is staged and
/// stops when none is; scans every process whose image lies under the install
/// root, with itself left out, exactly the set Velopack's kill pass would end;
/// applies, silent and without a restart, when that scan is empty, and stops;
/// and otherwise holds a handle on every process it found and waits on those
/// handles and the pipe's own. Any exit and any verb is a new pass. The toast
/// (phase 3) and the graceful close with its page (phase 4) are not here.
/// </para>
/// <para>
/// <b>No timer.</b> The one timer in the update lane is the server's
/// (DECISIONS, <i>Instance teardown</i>); the coordinator sleeps in the kernel
/// until a process it holds exits or a verb arrives.
/// </para>
/// <para>
/// ⚠️ <b>At most 63 processes are waited on at once</b>, with the pipe's handle the
/// 64th, which is what one wait can hold. More than that are still held open and
/// still counted; the wait wakes on the first of the 63 to go, and the next pass
/// waits on the next 63. The apply waits for all of them either way, because it
/// only happens on a pass whose scan finds nothing.
/// </para>
/// <para>
/// <b>It joins the live census on the first pass that finds a package staged, and
/// stays in it until it stops</b>, so a server finishing an update pass counts it
/// and wakes it instead of applying on its own exit, whose kill pass would end the
/// coordinator. It joins before it scans, so a server that starts after the scan
/// already counts it. A coordinator with nothing staged stops before it joins, which
/// is every start of a build that is not installed: such a build has no feed and
/// never touches a census.
/// </para>
/// <para>
/// <b>It waits on the main thread, which is the dialog's single-threaded
/// apartment</b>, through <see cref="WaitHandle.WaitAny(WaitHandle[])"/>, which on
/// such a thread keeps the thread's messages moving while it waits.
/// </para>
/// <para>
/// <b>A scan that could not establish how the root is spelled waits for a verb</b>:
/// it holds no handle, so no exit can wake it, and it applies nothing, which is the
/// sign-in step's rule. The next blocked server's <c>recheck</c> is what looks again.
/// </para>
/// </remarks>
/// <param name="installRoot">The install root, or the data root of a process that is not installed.</param>
/// <param name="inbox">The verbs the pipe has taken.</param>
/// <param name="staged">Whether a newer package is on disk, and the apply.</param>
/// <param name="scan">The path scan under the install root, this process left out.</param>
/// <param name="window">The window a <c>show</c> opens.</param>
/// <param name="logger">Where the coordinator reports.</param>
internal sealed class CoordinatorLoop(
    string installRoot,
    CoordinatorInbox inbox,
    IStagedUpdates staged,
    Func<RootScan> scan,
    ICoordinatorWindow window,
    ILogger logger)
{
    /// <summary>The most process handles one wait holds: the kernel's 64, less the pipe's.</summary>
    public const int ProcessesPerWait = 63;

    /// <summary>Runs until there is nothing left to coordinate, or the package has been handed over.</summary>
    /// <returns>Why it stopped.</returns>
    public CoordinatorEnd Run()
    {
        LiveInstances? member = null;

        try
        {
            var waitedOn = -1;

            while (true)
            {
                while (inbox.TryTake(out var arrival))
                {
                    if (arrival!.Verb is CoordinatorVerb.Show)
                    {
                        _ = window.Show();
                    }
                }

                if (staged.Pending() is not { } pending)
                {
                    CoordinatorLog.Stopping(logger, "no newer package is staged, so there is nothing to coordinate.");
                    return CoordinatorEnd.NothingPending;
                }

                member ??= LiveInstances.Join(installRoot, logger);

                using var found = scan();

                if (found.Held.Count is 0 && found.Unresolved.Count is 0)
                {
                    staged.ApplyAfterThisProcessExits(pending);

                    var handed = $"{pending.Version} is staged and nothing else runs from this install, so it was handed to Update.exe, silent and with no restart, to apply once this process exits.";

                    CoordinatorLog.Stopping(logger, handed);
                    return CoordinatorEnd.Applied;
                }

                if (found.Held.Count != waitedOn)
                {
                    waitedOn = found.Held.Count;

                    var running = SignInStep.Running(found);

                    CoordinatorLog.Waiting(logger, pending.Version, running);
                }

                WaitHandle[] handles = [inbox.Arrived, .. found.Held.Take(ProcessesPerWait)];

                _ = WaitHandle.WaitAny(handles);
            }
        }
        finally
        {
            member?.Dispose();
        }
    }
}

/// <summary>How the sign-in step ended.</summary>
internal enum SignInOutcome
{
    /// <summary>No newer package is staged. Nothing to do.</summary>
    NothingStaged,

    /// <summary>A newer package was staged and nothing else ran from the install, so it was handed to Velopack.</summary>
    Applied,

    /// <summary>A newer package is staged and something runs from the install, so nothing was applied.</summary>
    NotAlone,
}

/// <summary>What the sign-in step found, and the sentence it logged.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Why">The sentence.</param>
internal sealed record SignInReport(SignInOutcome Outcome, string Why);

/// <summary>
/// The sign-in step: apply a staged update when nothing else runs from the
/// install, and otherwise leave it for later.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q282 a and Q285 a, decided 2026-09-24 by the maintainer, in his words:
/// <i>"Q282 a"</i> and <i>"Q285 a"</i>.</b> The per-user logon task starts the app
/// with <c>--sign-in</c> at sign-in, which on the morning measured was 1.3 to 2.3 s
/// after it and minutes before the editor started its servers
/// ([kb](../../kb/windows/processes.md#what-starts-first-after-sign-in-and-what-a-task-started-process-may-do----measured-2026-09-24)).
/// </para>
/// <para>
/// <b>The staged package is Velopack's <c>UpdatePendingRestart</c></b>: the newest
/// full package on disk whose version is above the installed one, read with no
/// request. <b>The gate is a path scan</b>, every process whose image lies under the
/// install root except this one, which is exactly the set Velopack's kill pass would
/// end. An empty scan hands the package to <c>Update.exe</c>, silent and without a
/// restart, and the caller exits; anything else leaves it staged, and the log says
/// what was running. A scan that could not establish how the root is spelled is not
/// an empty one: it applies nothing.
/// </para>
/// <para>
/// <b>Automatic apply on startup stays off</b> (landmine 2 in the Velopack kb): this
/// step decides, and <c>VelopackApp.Run()</c> never does.
/// </para>
/// </remarks>
internal static class SignInStep
{
    /// <summary>How many running images the log names before it counts the rest.</summary>
    public const int NamedInTheLog = 5;

    /// <summary>Runs the step once.</summary>
    /// <param name="staged">Whether a newer package is on disk, and the apply.</param>
    /// <param name="scan">The path scan under the install root, this process left out.</param>
    /// <param name="logger">Where the outcome is recorded.</param>
    /// <returns>What happened.</returns>
    public static SignInReport Run(IStagedUpdates staged, Func<RootScan> scan, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(staged);
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(logger);

        SignInReport report;

        if (staged.Pending() is not { } pending)
        {
            report = new SignInReport(SignInOutcome.NothingStaged, "no newer package is staged, so there is nothing to apply.");
        }
        else
        {
            using var found = scan();

            if (found.Held.Count is 0 && found.Unresolved.Count is 0)
            {
                staged.ApplyAfterThisProcessExits(pending);
                report = new SignInReport(SignInOutcome.Applied, $"{pending.Version} is staged and nothing else runs from this install, so it was handed to Update.exe, silent and with no restart, to apply once this process exits.");
            }
            else
            {
                report = new SignInReport(SignInOutcome.NotAlone, $"{pending.Version} is staged and was not applied: {Running(found)}");
            }
        }

        var outcome = report.Outcome.ToString();

        CoordinatorLog.SignedIn(logger, outcome, report.Why);

        return report;
    }

    /// <summary>What a scan found, as a clause for the log.</summary>
    /// <param name="found">The scan.</param>
    /// <returns>The clause.</returns>
    public static string Running(RootScan found)
    {
        ArgumentNullException.ThrowIfNull(found);

        if (found.Held.Count is 0)
        {
            return $"the install root's spelling could not be established, so the scan cannot say nothing runs from it ({string.Join(" ", found.Unresolved)})";
        }

        var named = string.Join(", ", found.Held.Take(NamedInTheLog).Select(process => $"pid {process.ProcessId} {process.ImagePath}"));
        var more = found.Held.Count > NamedInTheLog ? $" and {found.Held.Count - NamedInTheLog} more" : string.Empty;

        return $"{found.Held.Count} process(es) run from this install and an apply would end them: {named}{more}.";
    }
}

/// <summary>What a coordinator with no update feed has staged: nothing, ever.</summary>
internal sealed class NothingStaged : IStagedUpdates
{
    /// <summary>The one instance.</summary>
    public static NothingStaged Instance { get; } = new();

    /// <inheritdoc />
    public UpdateCandidate? Pending() => null;

    /// <inheritdoc />
    public void ApplyAfterThisProcessExits(UpdateCandidate candidate) =>
        throw new InvalidOperationException("Nothing is staged without an update feed, so there is nothing to apply.");
}
