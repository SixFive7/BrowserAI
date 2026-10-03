// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using BrowserAI.App.Interop;
using BrowserAI.Coordination;
using Microsoft.Extensions.Logging;

namespace BrowserAI.App.Page;

/// <summary>
/// The page's desktop in the product: Explorer, Windows' folder picker, and the
/// coordinator's own thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>Explorer is opened on the coordinator's own thread</b>, which is the
/// process's main thread and a single-threaded apartment. The shell's calls want
/// one, and a request thread of Kestrel's is in the multithreaded apartment, so an
/// action queues its work here and wakes the coordinator through its inbox; the
/// loop runs it on its next pass.
/// </para>
/// <para>
/// <b>The folder picker gets a thread of its own, Q311</b>, a single-threaded
/// apartment as <see cref="ShellInterop.PickFolder"/> requires, because it is modal
/// and waits for a person: on the coordinator's thread it would hold the loop, and
/// with it an update the loop would otherwise apply, for as long as the picker is
/// open. It has no owner window, so by Windows' focus rules it may open behind the
/// browser; the page says so while it is open, and looking at that on the real
/// desktop is in TODO.md.
/// </para>
/// <para>
/// <b>Nothing here may run in the suite</b>: every call opens something on the
/// person's screen. The suite replaces this whole type, and its own banned-symbol
/// list refuses the shell calls underneath it.
/// </para>
/// </remarks>
/// <param name="inbox">The coordinator's inbox, whose wake runs the queue.</param>
/// <param name="logger">Where a failed call is recorded.</param>
internal sealed partial class DesktopPageHost(CoordinatorInbox inbox, ILogger logger) : IPageHost
{
    private readonly ConcurrentQueue<Action> _work = new();

    /// <inheritdoc />
    public void OpenFolder(string directory) => Post(() => _ = ShellInterop.OpenInExplorer(directory));

    /// <inheritdoc />
    public string? OpenTrace(string trace) =>
        "This build of BrowserAI does not open Playwright's trace viewer yet. The trace is in the session's output folder.";

    /// <inheritdoc />
    public Task<FolderPick> PickFolderAsync(string prompt)
    {
        var picked = new TaskCompletionSource<FolderPick>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                picked.SetResult(ShellInterop.PickFolder(0, prompt));
            }
#pragma warning disable CA1031 // A picker that threw is a sentence on the page, and the coordinator keeps coordinating.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                DesktopLog.Failed(logger, failure);
                picked.SetResult(FolderPick.Broke(failure.Message));
            }
        })
        {
            IsBackground = true,
            Name = "the page's folder picker",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return picked.Task;
    }

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
