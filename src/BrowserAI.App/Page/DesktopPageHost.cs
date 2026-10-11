// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.App.Interop;
using Microsoft.Extensions.Logging;

namespace BrowserAI.App.Page;

/// <summary>
/// The page's desktop in the product: Explorer and Windows' folder picker, each on a
/// thread of its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Explorer is opened on a thread of its own, in a single-threaded apartment</b>,
/// since 2026-10-11, the maintainer's decision 54, in his words verbatim: <i>"Never block
/// a worker anywhere."</i> The shell's calls want that apartment, and a request thread of
/// Kestrel's is a pool thread in the multithreaded one. <i>Corrected 2026-10-11
/// (previously "Explorer is opened on the coordinator's own thread, which is the
/// process's main thread and a single-threaded apartment", through a queue its loop ran
/// on each wake)</i>: <c>Main</c> is asynchronous since that day, no thread of the
/// background waits in a loop of its own, and an asynchronous <c>Main</c> is not given
/// the apartment <c>[STAThread]</c> asks for.
/// </para>
/// <para>
/// <b>The folder picker gets a thread of its own, Q311</b>, a single-threaded
/// apartment as <see cref="ShellInterop.PickFolder"/> requires, because it is modal
/// and waits for a person: on a shared thread it would hold everything else that thread
/// does for as long as the picker is open. It has no owner window, so by Windows' focus
/// rules it may open behind the browser; the page says so while it is open, and looking
/// at that on the real desktop is in TODO.md.
/// </para>
/// <para>
/// <b>Nothing here may run in the suite</b>: every call opens something on the
/// person's screen. The suite replaces this whole type, and its own banned-symbol
/// list refuses the shell calls underneath it.
/// </para>
/// </remarks>
/// <param name="logger">Where a failed call is recorded.</param>
internal sealed partial class DesktopPageHost(ILogger logger) : IPageHost
{
    /// <inheritdoc />
    public void OpenFolder(string directory) =>
        _ = ShellInterop.OnAShellThreadAsync(() => ShellInterop.OpenInExplorer(directory), "the page's Explorer").ContinueWith(
            opened => DesktopLog.Failed(logger, opened.Exception!),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

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
#pragma warning disable CA1031 // A picker that threw is a sentence on the page, and the background keeps serving.
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

    /// <summary>The desktop's own records.</summary>
    private static partial class DesktopLog
    {
        [LoggerMessage(EventId = 7021, Level = LogLevel.Warning, Message = "Something the page asked the desktop for threw, and nothing else was affected.")]
        public static partial void Failed(ILogger logger, Exception failure);
    }
}
