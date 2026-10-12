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
    public async Task<(string? Address, string? Refusal)> ShowAsync(string? page)
    {
        if (State is not BackgroundState.Serving)
        {
            return (null, State is BackgroundState.Updating
                // The texts polish, 2026-10-10, page #158 (previously "Start it again in a
                // few seconds."): the background is ending, and then the installer runs.
                ? "BrowserAI is installing an update, so it opens no page now. Start it again once the update is installed."
                : "BrowserAI's background is stopping, so it opens no page now.");
        }

        // Not reached by the background: the page is set before the pipe serves. The page
        // is a property set after construction, so the answer stays for a verbs whose page
        // was never set (the texts review's #132, 2026-10-10).
        if (Page is not { } served)
        {
            return (null, "BrowserAI's background is still starting, so it has no page to open yet. Start it again in a few seconds.");
        }

        var address = await served.HandOutAsync(PageNames.Parse(page)).ConfigureAwait(false);

        return address is null
            ? (null, "BrowserAI's page could not start its listener; the log says why.")
            : (address, null);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>55 a and 75 a, 2026-10-11</b>: the page answers, since the token is its listener's.
    /// A background that is stopping or still starting has no page a link could be from, and
    /// one installing an update says so in the words a person's start is told
    /// (<see cref="ShowAsync"/>). <i>Corrected 2026-10-12, from the reader's pass (previously
    /// "BrowserAI's background is stopping, so it opens no folder now." during an update as
    /// well).</i>
    /// </remarks>
    public Task<LinkAnswer> LinkAsync(LinkAsk ask) =>
        State switch
        {
            BackgroundState.Updating => Task.FromResult<LinkAnswer>(new LinkAnswer.Refused("BrowserAI is installing an update, so it opens no folder now.")),
            not BackgroundState.Serving => Task.FromResult<LinkAnswer>(new LinkAnswer.Refused("BrowserAI's background is stopping, so it opens no folder now.")),
            _ => Page is { } served
                ? served.LinkAsync(ask)
                : Task.FromResult<LinkAnswer>(new LinkAnswer.Refused("BrowserAI's background is still starting, so it has no page a link could be from.")),
        };

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
        // The texts polish, 2026-10-10, page #199 (previously "each within the minute's cap").
        [LoggerMessage(EventId = 20, Level = LogLevel.Information, Message = "The background was asked to stop: every session closes cleanly, each within a minute, and then it ends.")]
        public static partial void Stopping(ILogger logger);
    }
}
