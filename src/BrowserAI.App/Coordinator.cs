// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Coordination;
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
}

/// <summary>
/// What the coordinator does while it holds its pipe: act on the verbs it is
/// handed, for as long as a newer package is staged.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is not resident</b>, Q280 b's design in the maintainer's words: the
/// coordinator runs when it is needed. It stops as soon as no newer package is
/// staged, and every verb it takes is a reason to ask again.
/// </para>
/// <para>
/// <b>It waits on one handle and on nothing that ticks.</b> The one timer in the
/// update lane is the server's; the coordinator wakes when a verb arrives, and
/// sleeps otherwise.
/// </para>
/// </remarks>
/// <param name="inbox">The verbs the pipe has taken.</param>
/// <param name="staged">Whether a newer package is on disk.</param>
/// <param name="window">The window a <c>show</c> opens.</param>
/// <param name="logger">Where the coordinator reports.</param>
internal sealed class CoordinatorLoop(
    CoordinatorInbox inbox,
    IStagedUpdates staged,
    ICoordinatorWindow window,
    ILogger logger)
{
    /// <summary>Runs until there is nothing left to coordinate.</summary>
    /// <returns>Why it stopped.</returns>
    public CoordinatorEnd Run()
    {
        while (true)
        {
            while (inbox.TryTake(out var arrival))
            {
                if (arrival!.Verb is CoordinatorVerb.Show)
                {
                    _ = window.Show();
                }
            }

            if (staged.Pending() is null)
            {
                CoordinatorLog.Stopping(logger, "no newer package is staged, so there is nothing to coordinate.");
                return CoordinatorEnd.NothingPending;
            }

            _ = inbox.Arrived.WaitOne();
        }
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
