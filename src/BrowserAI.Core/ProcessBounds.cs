// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI;

/// <summary>
/// The waits between BrowserAI's own processes and on Windows and the tools it calls,
/// and how long its process log is kept, declared once.
/// </summary>
/// <remarks>
/// <para>
/// <b>One of the named classes of the numbers index</b>, decision F3 of the
/// one-binary build, the maintainer's words of 2026-10-07 verbatim: <i>"Maybe we should
/// start tracking all magical numbers used in this project in an index of sorts so
/// that we can at a later date re-check the data by using the provenance of these
/// records to re-determine if the number is still accurate?"</i> Every number here has a
/// row in <see href="../../kb/numbers.md">the numbers index</see> that says what it
/// governs, where it came from and how to check it again, and <c>NumbersIndexTests</c>
/// holds the two against each other in both directions. A literal duration anywhere
/// else in the product is a red build.
/// </para>
/// <para>
/// <b>Each member here is the value of a member its owner keeps</b>, so the code that
/// reads it did not move: <c>CoordinatorProtocol.HandOutBound</c> is
/// <see cref="HandOutBound"/>, and the remarks on the owner still say why the value
/// is what it is.
/// </para>
/// </remarks>
internal static class ProcessBounds
{
    /// <summary>
    /// How long a start waits for a verb that asks the background for a tab: 10 s. The
    /// value of <c>CoordinatorProtocol.HandOutBound</c>.
    /// </summary>
    public static TimeSpan HandOutBound { get; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long a client gives one call on a server's own pipe, from connect to the
    /// answer: 500 ms. The value of <c>ServerPipeProtocol.CallBound</c>.
    /// </summary>
    public static TimeSpan ServerPipeCallBound { get; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How long a connection to the background may take to send its first message:
    /// 10 s. The value of <c>BackgroundServer.FirstFrameBound</c>.
    /// </summary>
    public static TimeSpan BackgroundFirstFrameBound { get; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long one look for the background waits while every instance of its pipe is
    /// busy: 2 s. The value of <c>BackgroundFinder.BusyBound</c>.
    /// </summary>
    public static TimeSpan BackgroundBusyBound { get; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a relay waits for a background whose pipe has closed to finish exiting:
    /// 2 s. The value of <c>BackgroundFinder.ExitBound</c>.
    /// </summary>
    public static TimeSpan BackgroundExitBound { get; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long disposal waits for the sign-out window's thread to leave its loop: 5 s.
    /// The value of <c>SessionEndWindow.LeaveBound</c>.
    /// </summary>
    public static TimeSpan SessionEndWindowLeaveBound { get; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a write of the background's record goes on trying to rename itself over
    /// a record a reader has open: 2 s. The value of <c>BackgroundRecord.WriteBound</c>.
    /// </summary>
    public static TimeSpan BackgroundRecordWriteBound { get; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a write of the background's record waits between two tries of the
    /// rename: 20 ms. The value of <c>BackgroundRecord.WriteRetry</c>.
    /// </summary>
    public static TimeSpan BackgroundRecordWriteRetry { get; } = TimeSpan.FromMilliseconds(20);

    /// <summary>
    /// How long the uninstall hook waits for the background to end once asked: 45 s.
    /// The value of <c>BackgroundStop.Bound</c>.
    /// </summary>
    public static TimeSpan BackgroundStopBound { get; } = TimeSpan.FromSeconds(45);

    /// <summary>
    /// How long a person's start waits for a background the task started to open its
    /// pipe: 30 s. The value of <c>PersonStart.DefaultStartBound</c>.
    /// </summary>
    public static TimeSpan PersonStartBound { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often a person's start looks for that pipe while it waits: 100 ms. The value
    /// of <c>PersonStart.LookInterval</c>.
    /// </summary>
    public static TimeSpan PersonStartLookInterval { get; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// How long one call to the Task Scheduler may take before it is abandoned: 5 s. The
    /// value of <c>ScheduledTasks.CallBound</c>.
    /// </summary>
    public static TimeSpan ScheduledTasksCallBound { get; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The budget RegisterAI is handed for one run, as its <c>--timeout</c>: 12 s. The
    /// value of <c>McpRegistrar.ToolTimeout</c>.
    /// </summary>
    public static TimeSpan RegisterAiTimeout { get; } = TimeSpan.FromSeconds(12);

    /// <summary>
    /// How long BrowserAI waits for that run, the tool's own budget and a margin: 14 s.
    /// The value of <c>McpRegistrar.ToolBudget</c>.
    /// </summary>
    public static TimeSpan RegisterAiBudget { get; } = TimeSpan.FromSeconds(14);

    /// <summary>
    /// How long BrowserAI waits for RegisterAI's two output pipes to drain once the run
    /// is over: 5 s. Read by <c>RegisterAi</c>.
    /// </summary>
    public static TimeSpan RegisterAiOutputDrain { get; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long the page's listener is given to stop and close its connections: 5 s.
    /// Read by <c>PageListener.DisposeAsync</c>.
    /// </summary>
    public static TimeSpan PageListenerStopBound { get; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How often a listener reads what holds the update, to tell the open tabs of a
    /// change: 1 s. The value of <c>PageService.HoldsWatchPeriod</c>.
    /// </summary>
    public static TimeSpan PageHoldsWatchPeriod { get; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long the page's listener stays once the last tab has left: 1 minute, Q336 a.
    /// The value of <c>PageTabs.ProductLinger</c>.
    /// </summary>
    public static TimeSpan PageTabsLinger { get; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The shortest period a coalescable timer may be given: 1 ms, because Windows takes
    /// the period as a whole number of milliseconds and reads zero as a timer that fires
    /// once. Read by <c>CoalescableTimer.Start</c>.
    /// </summary>
    public static TimeSpan ShortestTimerPeriod { get; } = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// How many days a rolled file of the process log is kept: 30. The value of
    /// <c>RollingFileWriter.RetentionDays</c>.
    /// </summary>
    public const int ProcessLogRetentionDays = 30;
}
