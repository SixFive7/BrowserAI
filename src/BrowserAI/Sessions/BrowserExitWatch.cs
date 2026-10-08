// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;
using BrowserAI.Proxy;

namespace BrowserAI.Sessions;

/// <summary>
/// A wait on a session's browser's main process, so the session knows the moment
/// the browser ends and how: its exit code.
/// </summary>
/// <remarks>
/// <para>
/// <b>8 b, decided 2026-10-04 by the maintainer</b>: a person closing a headed
/// session's window closes the session as the agent's own <c>browser_close</c>
/// does, and every close says why. Nothing on the child's wire says the browser
/// went: <c>@playwright/mcp</c> 0.0.83 drops its backend when the browser
/// disconnects and starts a new one on the next call
/// (<c>coreBundle.js</c> line 73895), with no notification. So this watches the
/// process itself.
/// </para>
/// <para>
/// <b>An event and not a poll</b>: one thread-pool wait registration on a handle
/// held from the moment the browser is found, so the exit is seen when it happens
/// and its code is the process's own. Measured 2026-10-04 at <c>chromium-1247</c>
/// and <c>firefox-1553</c>: the main process exits with 0 when a person closes the
/// window (Chromium 3 of 3, Firefox 2 of 2), when Playwright closes it (2 of 2
/// each) and when Firefox's last tab is closed (2 of 2), and with the code it was
/// given when it is killed (2 of 2 each).
/// </para>
/// </remarks>
internal sealed class BrowserExitWatch : IDisposable, IWatchedBrowser
{
    private readonly HeldProcess _browser;
    private readonly RegisteredWaitHandle _registration;
    private readonly Action<int?> _ended;
    private int _fired;
    private int _disposed;

    private BrowserExitWatch(HeldProcess browser, Action<int?> ended)
    {
        _browser = browser;
        _ended = ended;

        // Registered last, after every field the callback reads is set: a browser
        // that has already gone makes this fire on this very line.
        _registration = ThreadPool.RegisterWaitForSingleObject(
            browser,
            static (state, _) => ((BrowserExitWatch)state!).Fire(),
            this,
            Timeout.InfiniteTimeSpan,
            executeOnlyOnce: true);
    }

    /// <summary>The browser's main process, by pid and creation time.</summary>
    public (int ProcessId, long CreatedFileTime) Browser => (_browser.ProcessId, _browser.CreatedFileTime);

    /// <inheritdoc />
    int IWatchedBrowser.ProcessId => _browser.ProcessId;

    /// <summary>
    /// Starts watching the browser a session child's job holds, or answers
    /// <see langword="null"/> when it holds none running <paramref name="executable"/>.
    /// </summary>
    /// <param name="child">The session's child.</param>
    /// <param name="executable">The browser's absolute executable path.</param>
    /// <param name="ended">Called once, on a thread-pool thread, with the exit code, when the browser ends.</param>
    /// <returns>The watch, which the caller disposes.</returns>
    public static BrowserExitWatch? Start(ChildConnection child, string executable, Action<int?> ended)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(ended);

        IReadOnlyList<int> members;

        try
        {
            members = child.JobProcessIds();
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or ObjectDisposedException)
        {
            return null;
        }

        return BrowserProcesses.HoldTheEarliest(members, executable) is { } browser
            ? new BrowserExitWatch(browser, ended)
            : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        // Before the handle is closed underneath the wait.
        _ = _registration.Unregister(null);
        _browser.Dispose();
    }

    private void Fire()
    {
        if (Interlocked.Exchange(ref _fired, 1) is not 0 || Volatile.Read(ref _disposed) is not 0)
        {
            return;
        }

        int? code;

        try
        {
            code = _browser.ExitCodeOnceEnded();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        _ended(code);
    }
}

/// <summary>
/// A watch on a session's browser that knows the browser's main process, which the
/// visible-input check matches the window in front against.
/// </summary>
/// <remarks>
/// <b>Added 2026-10-08 with F4.</b> The pid is safe to compare only because the watch
/// holds the process open: it was found inside the session's own job by full image
/// path, never by image name, and Windows cannot give the number to another process
/// while the handle is held. The in-process rig's double answers with a number of its
/// own, which no real process has.
/// </remarks>
internal interface IWatchedBrowser
{
    /// <summary>The browser's main process.</summary>
    int ProcessId { get; }
}
