// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Proxy;
using BrowserAI.Runtime;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Sessions;

/// <summary>
/// The start of a resumed Firefox session's browser, made by the resume itself: the
/// browser comes up on Playwright's own blank page, Firefox's session restore reopens the
/// session's tabs beside it, the blank page is closed once a reopened tab is up, and the
/// resume's answer says what came back.
/// </summary>
/// <remarks>
/// <para>
/// <b>161, decided 2026-10-13 by the maintainer, in his words verbatim:</b> <i>"161 your
/// recommendation"</i>, after his question of the same day: <i>"could we add the blank start
/// page back but inform the model resuming on what has happenend so there are no surprises?
/// How would an upstream fix look functionaly?"</i> Playwright's persistent launch waits for
/// its first page's <c>load</c>, and juggler sends <c>Page.ready</c> and
/// <c>DOMContentLoaded</c> for Firefox's error page and never <c>load</c>. With Playwright's
/// blank page left out, the first page was the restored tab, so a tab whose page could
/// neither load nor come back from Firefox's cache held the start for Playwright's whole
/// 180 s: 14 of 14 such resumes on 2026-10-13, at <c>firefox-1553</c> and
/// <c>playwright-core</c> 1.64.0-alpha-1790635538000
/// (<see href="../../../kb/playwright/provisioning-and-timings.md#a-firefox-session-resumed-onto-a-page-that-can-neither-load-nor-come-back-from-the-cache-waits-out-the-180-s-launch----measured-2026-10-13">kb</see>).
/// With the blank page back the launch waits for that page alone, which a replay of a stalled
/// profile started in 1,070 ms.
/// </para>
/// <para>
/// <b>Why the resume starts the browser, where the first browser call started it until
/// 161.</b> With the blank page back, Playwright's browser server makes the blank page its
/// current tab, so a first call that started the browser would act on the blank page with the
/// session's own tabs beside it; and which tabs came back, and which of them shows Firefox's
/// error page, is known only once the browser is up. So the resume starts it, tidies the
/// tabs, and says what it found, and the first browser call meets the tabs the session had.
/// A Chromium session is unchanged: its restore does not wait out the launch, and its first
/// browser call still starts its browser.
/// </para>
/// <para>
/// <b>Upstream's own tools and nothing else</b>, the way the idle close is a
/// <c>browser_close</c>: <c>browser_tabs</c> calls to the session's child, a list that starts
/// the browser and then reads the tabs, a <c>select</c> that makes the first reopened tab the
/// current one and a <c>close</c> of the blank page; and one <c>browser_run_code_unsafe</c>
/// call that reads every tab's <c>document.documentURI</c> at once, the one way the child
/// offers to read a tab that is not its current one without making it current. A forwarded
/// call that names the session meanwhile waits for the start to end
/// (<see cref="LiveSession.BrowserStartSettled"/>), so no call acts on the blank page or on a
/// tab that is being closed.
/// </para>
/// <para>
/// <b>Undone when upstream fixes the wait</b>: the maintainer's 161 took direction d beside
/// it, a report to Playwright, and <see href="../../../TODO.md">the TODO's watch item</see>
/// puts the blank page back out and this start back into the first browser call once a
/// Firefox build reports <c>load</c> for an error page, or a <c>playwright-core</c> stops
/// waiting for the <c>load</c> of a tab it did not open.
/// </para>
/// </remarks>
internal static class FirefoxRestore
{
    /// <summary>Upstream's own tab tool, spelled as upstream spells it.</summary>
    /// <remarks>
    /// <b>A call to a tool the child advertises, never a schema or a rename</b>, held against
    /// the committed tool list by the suite, so an upstream rename is a red build.
    /// </remarks>
    public const string TabsTool = "browser_tabs";

    /// <summary>Upstream's own tool that runs a Playwright snippet, spelled as upstream spells it.</summary>
    /// <remarks>
    /// <b>Called once per start, with <see cref="ErrorPageRead"/> and nothing else</b>, held
    /// against the committed tool list and the verdicts file by the suite as
    /// <see cref="TabsTool"/> is.
    /// </remarks>
    public const string RunCodeTool = "browser_run_code_unsafe";

    /// <summary>
    /// What a tab's <c>document.documentURI</c> starts with while it shows Firefox's network
    /// error page, which is how a reopened tab whose page could not be reached is told apart.
    /// </summary>
    /// <remarks>
    /// <b>Firefox's own address for the page, the same in every language</b>: the error page
    /// commits under the failed page's own address, so <c>location</c> and the tab list cannot
    /// tell it apart, and its title is Firefox's localised text. Decided 2026-10-13 for the
    /// maintainer's 161, over the title the first draft read, because a Firefox in another
    /// language words the title differently. Firefox's own <c>DocLoader</c> log of 2026-10-13
    /// named the page <c>about:neterror?e=connectionFailure</c>.
    /// </remarks>
    public const string ErrorPagePrefix = "about:neterror";

    /// <summary>
    /// The codes of Firefox's network error page that say the address gave no answer: a
    /// refused connection, a timeout, a reset, an interruption and a name that did not
    /// resolve, read from the page's <c>e=</c>. See <see cref="NothingAnswered"/>.
    /// </summary>
    internal static FrozenSet<string> NoAnswerCodes { get; } =
        new[] { "connectionFailure", "netTimeout", "netReset", "netInterrupt", "dnsNotFound" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The snippet <see cref="RunCodeTool"/> is handed: every tab's address and
    /// <c>document.documentURI</c>, read at once and in the order the context lists its pages.
    /// </summary>
    /// <remarks>
    /// A tab whose document cannot be evaluated answers <c>null</c> and is read as not on the
    /// error page. The child's tab list is built from the same pages in the same order, which
    /// <see cref="Match"/> checks address by address before it trusts it.
    /// </remarks>
    public const string ErrorPageRead =
        "async (page) => Promise.all(page.context().pages().map(async (each) => ({ url: each.url(), documentURI: await each.evaluate(() => document.documentURI).catch(() => null) })))";

    /// <summary>
    /// The files Firefox restores a session from, relative to the profile, in the order
    /// Firefox reads them: the one a clean close writes, and the one written while it runs.
    /// </summary>
    /// <remarks>
    /// <b>None of them, nothing to reopen</b>, so the start reads the tabs once and does not
    /// wait for a restore that cannot come: a session created and resumed before its browser
    /// ever started has a profile with none of them.
    /// </remarks>
    public static IReadOnlyList<string> SessionFiles { get; } =
    [
        "sessionstore.jsonlz4",
        Path.Combine("sessionstore-backups", "recovery.jsonlz4"),
    ];

    /// <summary>Whether a profile holds a session Firefox would restore.</summary>
    /// <param name="profileDirectory">The profile directory, Playwright's <c>userDataDir</c>.</param>
    /// <returns>Whether any of <see cref="SessionFiles"/> exists in it.</returns>
    public static bool HoldsASession(string profileDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);

        // A metadata query, which the file-system thread's ban leaves where it is.
        return SessionFiles.Any(file => File.Exists(Path.Combine(profileDirectory, file)));
    }

    /// <summary>
    /// Starts a resumed Firefox session's browser through its child, waits for Firefox's
    /// session restore, closes Playwright's blank page once a reopened tab is up, and says
    /// what came back.
    /// </summary>
    /// <param name="live">The session, published and gated, whose child has started no browser yet.</param>
    /// <param name="clock">The session's clock, which the waits run on.</param>
    /// <param name="cancellationToken">The resume's own token.</param>
    /// <returns>What the start found.</returns>
    public static async Task<FirefoxStart> StartAsync(LiveSession live, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(clock);

        // The countdown is held while the start runs, as it is under a forwarded call.
        using var driving = live.Driving(TabsTool, arguments: null);

        var started = clock.GetTimestamp();
        var first = await AskAsync(live, "list", index: null, cancellationToken).ConfigureAwait(false);

        if (first.Failure is { } failure)
        {
            FirefoxRestoreLog.DidNotStart(live.Logger, live.Location.FullPath, failure);

            return new FirefoxStart(FirefoxStartOutcome.DidNotStart, [], ClosedTheBlankPage: false, Unreachable: [], failure, live.Child.ChildHasGone);
        }

        // A browser is up now, so its end is watched, as after a forwarded call.
        live.WatchTheBrowser();

        var tabs = first.Tabs;

        if (!HoldsASession(live.Config.ProfileDirectory))
        {
            Say(live, clock.GetElapsedTime(started), tabs.Count, closed: false);

            return new FirefoxStart(FirefoxStartOutcome.NothingToReopen, tabs, ClosedTheBlankPage: false, Unreachable: [], Failure: null, ServerEnded: false);
        }

        var closed = false;
        var changed = clock.GetTimestamp();

        while (true)
        {
            if (!closed && TheBlankPageCanGo(tabs, out var reopened))
            {
                // The first reopened page becomes the current tab, and Firefox's selected one,
                // before the blank page goes, so no call meets a current tab being closed and
                // the page the browser shows is the page browser calls act on.
                var selected = await AskAsync(live, "select", reopened, cancellationToken).ConfigureAwait(false);

                if (selected.Failure is not null)
                {
                    break;
                }

                var after = await AskAsync(live, "close", 0, cancellationToken).ConfigureAwait(false);

                if (after.Failure is not null)
                {
                    tabs = selected.Tabs;
                    break;
                }

                tabs = after.Tabs;
                closed = true;
                changed = clock.GetTimestamp();
            }

            if (clock.GetElapsedTime(started) >= SessionTimes.FirefoxRestoreWait)
            {
                break;
            }

            if ((closed || tabs.Count > 1) && clock.GetElapsedTime(changed) >= SessionTimes.FirefoxRestoreQuiet)
            {
                break;
            }

            await Task.Delay(SessionTimes.FirefoxRestorePoll, clock, cancellationToken).ConfigureAwait(false);

            // A close made meanwhile is the caller's or BrowserAI's own, and a list sent after
            // it would start the browser again.
            if (live.Closed is not null || live.CloseIsInFlight)
            {
                break;
            }

            var listed = await AskAsync(live, "list", index: null, cancellationToken).ConfigureAwait(false);

            if (listed.Failure is not null)
            {
                break;
            }

            if (!Same(tabs, listed.Tabs))
            {
                changed = clock.GetTimestamp();
            }

            tabs = listed.Tabs;
        }

        var outcome = closed || tabs.Count > 1 ? FirefoxStartOutcome.Reopened : FirefoxStartOutcome.NothingCameUp;

        // Which reopened tabs show Firefox's error page, read once for all of them.
        var unreachable = outcome is FirefoxStartOutcome.Reopened && live.Closed is null && !live.CloseIsInFlight
            ? await ErrorPagesAsync(live, tabs, clock, cancellationToken).ConfigureAwait(false)
            : [];

        Say(live, clock.GetElapsedTime(started), tabs.Count, closed);

        return new FirefoxStart(outcome, tabs, closed, unreachable, Failure: null, ServerEnded: false);
    }

    /// <summary>
    /// Whether Playwright's blank page can be closed, and which tab becomes the current one
    /// when it is.
    /// </summary>
    /// <remarks>
    /// <b>The blank page is the first tab, and only when it is blank.</b> The launch waits for
    /// its first page's <c>load</c>, which is Playwright's blank page since 161, and the child
    /// lists tabs in the order their pages came up. A first tab that is anything else came up
    /// before the blank page, and nothing is closed. The tab made current is the first reopened
    /// one that is not blank itself, or the first reopened one when every one is.
    /// </remarks>
    /// <param name="tabs">The tab list, in the child's order.</param>
    /// <param name="reopened">The index of the tab to make current.</param>
    /// <returns>Whether the blank page can go.</returns>
    internal static bool TheBlankPageCanGo(IReadOnlyList<UpstreamTab> tabs, out int reopened)
    {
        ArgumentNullException.ThrowIfNull(tabs);

        reopened = 1;

        if (tabs.Count < 2 || !string.Equals(tabs[0].Url, BrowserConfiguration.DefaultPageArgument, StringComparison.Ordinal))
        {
            return false;
        }

        for (var index = 1; index < tabs.Count; index++)
        {
            if (!string.Equals(tabs[index].Url, BrowserConfiguration.DefaultPageArgument, StringComparison.Ordinal))
            {
                reopened = index;
                break;
            }
        }

        return true;
    }

    /// <summary>
    /// What a resume that started a Firefox session's browser says about it: the note, without
    /// its <c>NOTE: </c> prefix.
    /// </summary>
    /// <param name="start">What the start found.</param>
    /// <returns>The note.</returns>
    /// <remarks>
    /// <b>The maintainer's question of 2026-10-13, answered here:</b> <i>"inform the model
    /// resuming on what has happenend so there are no surprises"</i>. Each tab is listed as
    /// <c>browser_tabs</c> lists it, so a caller can match the two, and a tab on Firefox's error
    /// page says why it is not the page it had.
    /// </remarks>
    public static string Note(FirefoxStart start)
    {
        ArgumentNullException.ThrowIfNull(start);

        switch (start.Outcome)
        {
            case FirefoxStartOutcome.DidNotStart:
                return $"this resume started a new browser server, and its browser did not start: {start.Failure}. "
                    + (start.ServerEnded
                        ? $"The browser server ended with it, so call {SessionToolSurface.Resume} again to start a new one."
                        // ⚠️ 2026-10-13, a reading of the texts of 161 (previously "The next
                        // browser call starts it again."): the call that starts it acts on
                        // Playwright's blank page, and the reopened tabs come up beside it.
                        : "The next browser call starts it again and acts on the blank page it starts on. "
                            + "A tab Firefox reopens appears in browser_tabs once it is up, with the blank page beside it.");

            case FirefoxStartOutcome.NothingToReopen:
                return "this resume started the browser. Firefox had no tabs of this session to reopen, so the browser shows one blank page.";

            case FirefoxStartOutcome.NothingCameUp:
                return $"this resume started the browser, and no tab of the session's last run had come up after {SessionErrors.Duration(SessionTimes.FirefoxRestoreWait)}, so the browser shows the blank page it starts on. "
                    + "A tab Firefox reopens later appears in browser_tabs once it is up, with the blank page beside it.";

            default:
                break;
        }

        var text = new StringBuilder("this resume started the browser, and Firefox's session restore reopened ");

        if (start.ClosedTheBlankPage)
        {
            _ = text.Append(Count(start.Tabs.Count)).Append(". The blank page the browser starts on was closed, so the tabs are:\n");
        }
        else
        {
            // ⚠️ 2026-10-13, a reading of the texts of 161 (previously "reopened N tabs:",
            // which counted the blank page among them): nothing closed is a blank page that
            // was not the first tab, or a select or a close that failed, and the list may
            // hold the blank page, so the note counts nothing.
            _ = text.Append("tabs of the session's last run. Nothing was closed, so the tabs are:\n");
        }

        foreach (var tab in start.Tabs)
        {
            _ = text.Append("  ").Append(tab.Line);

            if (start.Unreachable.Contains(tab.Index))
            {
                _ = text.Append(" -- Firefox's error page: nothing answered at this address when the tab was reopened, so the page it had is not there. Navigate to it again once its server is back.");
            }

            _ = text.Append('\n');
        }

        return text
            .Append("Browser calls act on the current tab until browser_tabs selects another. Refs from earlier snapshots do not carry over: take a browser_snapshot before you act.")
            .ToString();
    }

    /// <summary>Writes the start's one Information line, when the session's log takes it.</summary>
    /// <param name="live">The session.</param>
    /// <param name="elapsed">How long the start and the wait took.</param>
    /// <param name="tabs">How many tabs the child listed last.</param>
    /// <param name="closed">Whether Playwright's blank page was closed.</param>
    private static void Say(LiveSession live, TimeSpan elapsed, int tabs, bool closed)
    {
        if (live.Logger.IsEnabled(LogLevel.Information))
        {
            FirefoxRestoreLog.Started(live.Logger, live.Location.FullPath, elapsed, tabs, closed);
        }
    }

    private static string Count(int tabs) =>
        tabs is 1 ? "1 tab" : $"{tabs.ToString(CultureInfo.InvariantCulture)} tabs";

    /// <summary>Whether two tab lists say the same thing, tab by tab.</summary>
    /// <param name="before">The earlier list.</param>
    /// <param name="after">The later one.</param>
    /// <returns>Whether every tab has the same address, title and current mark.</returns>
    internal static bool Same(IReadOnlyList<UpstreamTab> before, IReadOnlyList<UpstreamTab> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        if (before.Count != after.Count)
        {
            return false;
        }

        for (var index = 0; index < before.Count; index++)
        {
            if (before[index] != after[index])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Which tabs show Firefox's error page for an address that gave no answer
    /// (<see cref="NothingAnswered"/>), matched from what <see cref="ErrorPageRead"/> answered
    /// to the child's tab list.
    /// </summary>
    /// <remarks>
    /// <b>By position when every address agrees, and by address otherwise</b>: the child's
    /// list and the context's pages are the same pages in the same order, so an answer whose
    /// addresses line up with the list is read in its order, and one that does not, such as a
    /// tab that came up between the list and the read, is matched address by address, each
    /// answer used once.
    /// </remarks>
    /// <param name="tabs">The child's tab list.</param>
    /// <param name="pages">Each page's address and document URI, in the context's order.</param>
    /// <returns>The <see cref="UpstreamTab.Index"/> of every tab on Firefox's error page for an address that gave no answer.</returns>
    internal static List<int> Match(IReadOnlyList<UpstreamTab> tabs, IReadOnlyList<(string Url, string? DocumentUri)> pages)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(pages);

        var aligned = tabs.Count == pages.Count
            && tabs.Select((tab, at) => string.Equals(tab.Url, pages[at].Url, StringComparison.Ordinal)).All(same => same);
        var used = new bool[pages.Count];
        var unreachable = new List<int>();

        for (var at = 0; at < tabs.Count; at++)
        {
            var found = aligned ? at : -1;

            for (var candidate = 0; found < 0 && candidate < pages.Count; candidate++)
            {
                if (!used[candidate] && string.Equals(tabs[at].Url, pages[candidate].Url, StringComparison.Ordinal))
                {
                    found = candidate;
                }
            }

            if (found < 0)
            {
                continue;
            }

            used[found] = true;

            if (pages[found].DocumentUri is { } uri && NothingAnswered(uri))
            {
                unreachable.Add(tabs[at].Index);
            }
        }

        return unreachable;
    }

    /// <summary>
    /// Whether a document URI is Firefox's network error page for an address that gave no
    /// answer: its <c>e=</c> is one of <see cref="NoAnswerCodes"/>.
    /// </summary>
    /// <param name="documentUri">The tab's <c>document.documentURI</c>.</param>
    /// <returns><see langword="true"/> for such a page.</returns>
    /// <remarks>
    /// <b>Added 2026-10-13, on a reading of the texts of 161 that day, its fact 2.</b> In
    /// Firefox's <c>docshell/base/nsDocShell.cpp</c>, read on its main branch that day and not
    /// in the build BrowserAI runs, <c>about:neterror</c> is the page for most loads that fail,
    /// a redirect loop, a server error and a failed TLS handshake among them, where something
    /// did answer, so the sentence the note adds, that nothing answered, is said only of the
    /// codes that mean so. The one measured is <c>connectionFailure</c>, a refused connection
    /// (<see href="https://github.com/microsoft/playwright/issues/43341">microsoft/playwright#43341</see>).
    /// </remarks>
    internal static bool NothingAnswered(string documentUri)
    {
        ArgumentNullException.ThrowIfNull(documentUri);

        if (!documentUri.StartsWith(ErrorPagePrefix + "?", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var part in documentUri[(ErrorPagePrefix.Length + 1)..].Split('&'))
        {
            if (part.StartsWith("e=", StringComparison.Ordinal))
            {
                return NoAnswerCodes.Contains(part[2..]);
            }
        }

        return false;
    }

    /// <summary>
    /// What <see cref="ErrorPageRead"/> answered: each page's address and document URI, or
    /// nothing when the answer holds no list of them.
    /// </summary>
    /// <param name="text">The text of the call's answer.</param>
    /// <returns>The pages, in the context's order.</returns>
    internal static List<(string Url, string? DocumentUri)> ParsePages(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();

            if (!trimmed.StartsWith('['))
            {
                continue;
            }

            try
            {
                if (JsonNode.Parse(trimmed) is not JsonArray array)
                {
                    continue;
                }

                return [.. array.Select(entry => (
                    Url: entry?["url"] is JsonValue url && url.GetValueKind() is JsonValueKind.String ? url.GetValue<string>() : string.Empty,
                    DocumentUri: entry?["documentURI"] is JsonValue uri && uri.GetValueKind() is JsonValueKind.String ? uri.GetValue<string>() : null))];
            }
            catch (JsonException)
            {
                // Not the list; the answer's next line may be.
            }
        }

        return [];
    }

    /// <summary>
    /// Reads every tab's <c>document.documentURI</c> through one <see cref="RunCodeTool"/> call,
    /// bounded by <see cref="SessionTimes.FirefoxRestoreWait"/>, and says which tabs show
    /// Firefox's error page for an address that gave no answer.
    /// </summary>
    /// <remarks>
    /// <b>A read that fails marks nothing</b>, and the answer then lists the tabs without the
    /// sentence an error page gets: what the tab list says is still true, and the session's log
    /// says why the read did not answer.
    /// </remarks>
    /// <param name="live">The session.</param>
    /// <param name="tabs">The child's tab list, as the start left it.</param>
    /// <param name="clock">The session's clock.</param>
    /// <param name="cancellationToken">The resume's token.</param>
    /// <returns>The <see cref="UpstreamTab.Index"/> of every tab on Firefox's error page for an address that gave no answer.</returns>
    private static async Task<IReadOnlyList<int>> ErrorPagesAsync(LiveSession live, IReadOnlyList<UpstreamTab> tabs, TimeProvider clock, CancellationToken cancellationToken)
    {
        using var bound = new CancellationTokenSource(SessionTimes.FirefoxRestoreWait, clock);
        using var either = CancellationTokenSource.CreateLinkedTokenSource(bound.Token, cancellationToken);

        ChildAnswer answer;

        try
        {
            answer = await live.Child.AskAsync(
                RequestMethods.ToolsCall,
                new JsonObject
                {
                    ["name"] = RunCodeTool,
                    ["arguments"] = new JsonObject { ["code"] = ErrorPageRead },
                },
                either.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (bound.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            FirefoxRestoreLog.ErrorPagesUnread(live.Logger, live.Location.FullPath, $"no answer within {SessionErrors.Duration(SessionTimes.FirefoxRestoreWait)}");
            return [];
        }

        if (answer.Response?.Result is not JsonObject result)
        {
            FirefoxRestoreLog.ErrorPagesUnread(live.Logger, live.Location.FullPath, FirstLine(answer.ProtocolFailure?.Message ?? answer.TransportFailure?.Message ?? "the browser server gave no answer"));
            return [];
        }

        var text = TextOf(result);

        if (result["isError"] is JsonValue flag && flag.GetValueKind() is JsonValueKind.True)
        {
            FirefoxRestoreLog.ErrorPagesUnread(live.Logger, live.Location.FullPath, FirstLine(text));
            return [];
        }

        return Match(tabs, ParsePages(text));
    }

    /// <summary>One <c>browser_tabs</c> call to the session's child, and the tab list it answered with.</summary>
    /// <param name="live">The session.</param>
    /// <param name="action">The tool's own action: <c>list</c>, <c>select</c> or <c>close</c>.</param>
    /// <param name="index">The tab, for <c>select</c> and <c>close</c>.</param>
    /// <param name="cancellationToken">The resume's token.</param>
    /// <returns>The tabs, or what went wrong, in one line.</returns>
    private static async Task<TabsAnswer> AskAsync(LiveSession live, string action, int? index, CancellationToken cancellationToken)
    {
        var arguments = new JsonObject { ["action"] = action };

        if (index is { } at)
        {
            arguments["index"] = at;
        }

        var answer = await live.Child.AskAsync(
            RequestMethods.ToolsCall,
            new JsonObject
            {
                ["name"] = TabsTool,
                ["arguments"] = arguments,
            },
            cancellationToken).ConfigureAwait(false);

        if (answer.Response?.Result is not JsonObject result)
        {
            return new TabsAnswer(
                [],
                FirstLine(answer.ProtocolFailure?.Message ?? answer.TransportFailure?.Message ?? "the browser server gave no answer"));
        }

        var text = TextOf(result);

        if (result["isError"] is JsonValue flag && flag.GetValueKind() is JsonValueKind.True)
        {
            return new TabsAnswer([], FirstLine(text));
        }

        return new TabsAnswer(UpstreamTab.ParseList(text), Failure: null);
    }

    /// <summary>Every text block of a tool result, joined.</summary>
    /// <param name="result">The result.</param>
    /// <returns>The text.</returns>
    private static string TextOf(JsonObject result) =>
        string.Join(
            "\n",
            (result["content"] as JsonArray ?? [])
                .Where(block => (block as JsonObject)?["type"]?.GetValueKind() is JsonValueKind.String
                    && block!["type"]!.GetValue<string>() is "text")
                .Select(block => block!["text"]?.GetValueKind() is JsonValueKind.String ? block["text"]!.GetValue<string>() : string.Empty));

    /// <summary>The first line of an error that says something, without upstream's section heading.</summary>
    /// <param name="text">The error text.</param>
    /// <returns>One line.</returns>
    private static string FirstLine(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.Length is not 0 && !trimmed.StartsWith('#'))
            {
                return trimmed.TrimEnd('.');
            }
        }

        return "the browser server said nothing about why";
    }

    /// <summary>The tabs a <c>browser_tabs</c> call answered with, or what went wrong.</summary>
    /// <param name="Tabs">The tabs, in the child's order.</param>
    /// <param name="Failure">What went wrong, in one line, or <see langword="null"/>.</param>
    private readonly record struct TabsAnswer(IReadOnlyList<UpstreamTab> Tabs, string? Failure);
}

/// <summary>How a resume's start of a Firefox session's browser ended.</summary>
internal enum FirefoxStartOutcome
{
    /// <summary>Firefox reopened tabs of the session's last run, and the blank page went once one was up.</summary>
    Reopened,

    /// <summary>The profile holds no session, so there was nothing to reopen.</summary>
    NothingToReopen,

    /// <summary>The profile holds a session, and no tab of it came up within the wait.</summary>
    NothingCameUp,

    /// <summary>The browser did not start.</summary>
    DidNotStart,
}

/// <summary>What a resume's start of a Firefox session's browser found.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Tabs">The tabs, as the child listed them last.</param>
/// <param name="ClosedTheBlankPage">Whether Playwright's blank page was closed.</param>
/// <param name="Unreachable">The <see cref="UpstreamTab.Index"/> of every tab on Firefox's error page.</param>
/// <param name="Failure">Why the browser did not start, in one line, or <see langword="null"/>.</param>
/// <param name="ServerEnded">Whether the browser server ended with a start that failed.</param>
internal sealed record FirefoxStart(
    FirefoxStartOutcome Outcome,
    IReadOnlyList<UpstreamTab> Tabs,
    bool ClosedTheBlankPage,
    IReadOnlyList<int> Unreachable,
    string? Failure,
    bool ServerEnded);

/// <summary>One tab as <c>@playwright/mcp</c>'s <c>browser_tabs</c> lists it.</summary>
/// <param name="Index">Its index in the child's list.</param>
/// <param name="IsCurrent">Whether it is the child's current tab.</param>
/// <param name="Title">Its title, as the page set it.</param>
/// <param name="Url">Its address.</param>
/// <param name="Crashed">Whether the child marks it crashed.</param>
/// <remarks>
/// <b>Read off upstream's own line</b>, which is <c>- N: (current) [title]</c>, then the
/// address in parentheses, then <c>[crashed]</c>, with the two marks optional (<c>renderTabsMarkdown</c> in <c>playwright-core</c>'s
/// <c>coreBundle.js</c>, read at 1.64.0-alpha-1790635538000). A line that does not have that
/// shape is not a tab and is passed over.
/// </remarks>
internal sealed record UpstreamTab(int Index, bool IsCurrent, string Title, string Url, bool Crashed)
{
    private const string CurrentMark = " (current)";
    private const string CrashedMark = " [crashed]";

    // Between the title and the address, written in two halves so that the line below is
    // not a Markdown link to DocumentationLinkTests, which reads every .cs file for them.
    private const string TitleThenAddress = "]" + "(";

    /// <summary>The tab as upstream's own line spells it, on one line whatever its title holds.</summary>
    public string Line =>
        $"- {Index.ToString(CultureInfo.InvariantCulture)}:{(IsCurrent ? CurrentMark : string.Empty)} [{OneLine(Title)}{TitleThenAddress}{OneLine(Url)}){(Crashed ? CrashedMark : string.Empty)}";

    /// <summary>Every tab in a <c>browser_tabs</c> answer, in its order.</summary>
    /// <param name="text">The answer's text.</param>
    /// <returns>The tabs.</returns>
    public static List<UpstreamTab> ParseList(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var tabs = new List<UpstreamTab>();

        foreach (var raw in text.Split('\n'))
        {
            if (Parse(raw.TrimEnd('\r')) is { } tab)
            {
                tabs.Add(tab);
            }
        }

        return tabs;
    }

    /// <summary>One line of a <c>browser_tabs</c> answer, or <see langword="null"/> when it is not a tab.</summary>
    /// <param name="line">The line.</param>
    /// <returns>The tab, or <see langword="null"/>.</returns>
    public static UpstreamTab? Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (!line.StartsWith("- ", StringComparison.Ordinal))
        {
            return null;
        }

        var colon = line.IndexOf(':', 2);

        if (colon < 3 || !int.TryParse(line.AsSpan(2, colon - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            return null;
        }

        var rest = line[(colon + 1)..];
        var current = rest.StartsWith(CurrentMark, StringComparison.Ordinal);

        if (current)
        {
            rest = rest[CurrentMark.Length..];
        }

        if (!rest.StartsWith(" [", StringComparison.Ordinal))
        {
            return null;
        }

        rest = rest[2..];

        var crashed = rest.EndsWith(CrashedMark, StringComparison.Ordinal);

        if (crashed)
        {
            rest = rest[..^CrashedMark.Length];
        }

        // The title is the page's own and may hold anything; the address is the part after the
        // last "](" up to the closing parenthesis at the end of the line.
        var split = rest.LastIndexOf("](", StringComparison.Ordinal);

        if (split < 0 || !rest.EndsWith(')'))
        {
            return null;
        }

        return new UpstreamTab(index, current, rest[..split], rest[(split + 2)..^1], crashed);
    }

    private static string OneLine(string text) =>
        text.Contains('\n', StringComparison.Ordinal) || text.Contains('\r', StringComparison.Ordinal)
            ? text.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal)
            : text;
}

/// <summary>Source-generated log messages for the start of a resumed Firefox session's browser.</summary>
/// <remarks>Event ids start at 7301, after every range in use on 2026-10-13.</remarks>
internal static partial class FirefoxRestoreLog
{
    /// <summary>The resume started the session's browser and read what Firefox reopened.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="session">The session directory.</param>
    /// <param name="elapsed">How long the start and the wait took.</param>
    /// <param name="tabs">How many tabs the child listed last.</param>
    /// <param name="closed">Whether Playwright's blank page was closed.</param>
    [LoggerMessage(
        EventId = 7301,
        Level = LogLevel.Information,
        Message = "The resume of the Firefox session at {Session} started its browser in {Elapsed}: the child listed {Tabs} tab(s) last, and Playwright's blank page was closed: {Closed}.")]
    public static partial void Started(ILogger logger, string session, TimeSpan elapsed, int tabs, bool closed);

    /// <summary>The resume's start of the session's browser failed.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="session">The session directory.</param>
    /// <param name="failure">What the child said, in one line.</param>
    [LoggerMessage(
        EventId = 7302,
        Level = LogLevel.Warning,
        Message = "The resume of the Firefox session at {Session} could not start its browser: {Failure}. The next browser call starts it again if the browser server is still there.")]
    public static partial void DidNotStart(ILogger logger, string session, string failure);

    /// <summary>The read of which reopened tabs show Firefox's error page did not answer.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="session">The session directory.</param>
    /// <param name="why">What the child said, in one line.</param>
    [LoggerMessage(
        EventId = 7303,
        Level = LogLevel.Warning,
        Message = "The resume of the Firefox session at {Session} could not read which reopened tabs show Firefox's error page: {Why}. Its answer lists the tabs without saying so.")]
    public static partial void ErrorPagesUnread(ILogger logger, string session, string why);
}
