// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.App.Page;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Background;

/// <summary>
/// What the background answers a person's start and a stop with: a tab from its page,
/// and the end of every session.
/// </summary>
/// <remarks>
/// <b>The page is the coordinator's page, unchanged</b> (the design's "What moves"):
/// a person's start hands over <c>show</c> and opens the address it gets back, as a
/// start that found a coordinator did until 2026-10-08.
/// </remarks>
/// <param name="logger">Where it reports.</param>
internal sealed partial class BackgroundVerbs(ILogger logger) : IBackgroundVerbs
{
    private int _state = (int)BackgroundState.Serving;

    /// <summary>The page tabs are handed out of, once it exists.</summary>
    public PageService? Page { get; set; }

    /// <summary>What a stop does: ends the background's wait.</summary>
    public Action StopRequested { get; set; } = static () => { };

    /// <inheritdoc />
    public BackgroundState State
    {
        get => (BackgroundState)Volatile.Read(ref _state);
        set => Volatile.Write(ref _state, (int)value);
    }

    /// <inheritdoc />
    public (string? Address, string? Refusal) Show(string? page)
    {
        if (State is not BackgroundState.Serving)
        {
            return (null, State is BackgroundState.Updating
                ? "BrowserAI is installing an update, so it opens no page now. Start it again in a few seconds."
                : "BrowserAI's background is stopping, so it opens no page now.");
        }

        // Not reached by the background: the page is set before the pipe serves. The page
        // is a property set after construction, so the answer stays for a verbs whose page
        // was never set (the texts review's #132, 2026-10-10).
        if (Page is not { } served)
        {
            return (null, "BrowserAI's background is still starting, so it has no page to open yet. Start it again in a few seconds.");
        }

        var address = served.HandOut(PageNames.Parse(page));

        return address is null
            ? (null, "BrowserAI's page could not start its listener; the log says why.")
            : (address, null);
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (State is BackgroundState.Serving)
        {
            State = BackgroundState.Stopping;
        }

        VerbsLog.Stopping(logger);
        StopRequested();
    }

    private static partial class VerbsLog
    {
        [LoggerMessage(EventId = 20, Level = LogLevel.Information, Message = "The background was asked to stop: every session closes cleanly, each within the minute's cap, and then it ends.")]
        public static partial void Stopping(ILogger logger);
    }
}
