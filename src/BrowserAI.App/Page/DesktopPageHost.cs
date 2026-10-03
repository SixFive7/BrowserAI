// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using BrowserAI.App.Interop;
using BrowserAI.Coordination;
using Microsoft.Extensions.Logging;

namespace BrowserAI.App.Page;

/// <summary>
/// The page's desktop in the product: Explorer, the configuration window, and the
/// coordinator's own thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything that touches the desktop runs on the coordinator's own thread</b>,
/// which is the process's main thread and a single-threaded apartment. The shell's
/// calls want one, and a request thread of Kestrel's is in the multithreaded
/// apartment, so an action queues its work here and wakes the coordinator through
/// its inbox; the loop runs it on its next pass.
/// </para>
/// <para>
/// <b>Nothing here may run in the suite</b>: every call opens something on the
/// person's screen. The suite replaces this whole type, and its own banned-symbol
/// list refuses the shell calls underneath it.
/// </para>
/// </remarks>
/// <param name="inbox">The coordinator's inbox, whose wake runs the queue.</param>
/// <param name="window">The configuration window, for the registration link.</param>
/// <param name="logger">Where a failed call is recorded.</param>
internal sealed partial class DesktopPageHost(CoordinatorInbox inbox, ICoordinatorWindow window, ILogger logger) : IPageHost
{
    private readonly ConcurrentQueue<Action> _work = new();

    /// <inheritdoc />
    public void OpenFolder(string directory) => Post(() => _ = ShellInterop.OpenInExplorer(directory));

    /// <inheritdoc />
    public string? OpenTrace(string trace) =>
        "This build of BrowserAI does not open Playwright's trace viewer yet. The trace is in the session's output folder.";

    /// <inheritdoc />
    public void ShowRegistrationWindow() => Post(() => _ = window.Show());

    /// <inheritdoc />
    public void RunQueuedWork()
    {
        while (_work.TryDequeue(out var work))
        {
            try
            {
                work();
            }
#pragma warning disable CA1031 // A shell call that threw is a line in the log; the coordinator keeps coordinating.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                DesktopLog.Failed(logger, failure);
            }
        }
    }

    private void Post(Action work)
    {
        _work.Enqueue(work);
        inbox.Wake();
    }

    /// <summary>The desktop's own records.</summary>
    private static partial class DesktopLog
    {
        [LoggerMessage(EventId = 7021, Level = LogLevel.Warning, Message = "Something the page asked the desktop for threw, and nothing else was affected.")]
        public static partial void Failed(ILogger logger, Exception failure);
    }
}
