// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.App.Interop;
using BrowserAI.App.Page;
using BrowserAI.Coordination;
using BrowserAI.Registration;
using Microsoft.Extensions.Logging;

namespace BrowserAI.App;

/// <summary>What a link start opens with: Explorer and Windows' folder picker, or what stands in for them.</summary>
/// <param name="OpenFolder">Opens a folder in Explorer, and says whether the shell took it.</param>
/// <param name="PickFolder">Shows Windows' folder picker with a sentence, and says what was chosen.</param>
internal sealed record LinkDesktop(Func<string, Task<bool>> OpenFolder, Func<string, Task<FolderPick>> PickFolder)
{
    /// <summary>The person's desktop: Explorer and the picker, each on a thread of its own in a single-threaded apartment.</summary>
    public static LinkDesktop Windows { get; } = new(
        directory => ShellInterop.OnAShellThreadAsync(() => ShellInterop.OpenInExplorer(directory), "BrowserAI link's Explorer"),
        prompt => ShellInterop.OnAShellThreadAsync(() => ShellInterop.PickFolder(0, prompt), "BrowserAI link's folder picker"));
}

/// <summary>
/// The start Windows makes for one of the dashboard's folder links: it asks the background
/// whether the link is the page's, and opens the folder, or shows the folder picker, itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>55 a and 75 a, the maintainer's words of 2026-10-11 verbatim: <i>"55 a"</i> and
/// <i>"75 a"</i>.</b> The browser hands the link to Windows, and Windows starts this program
/// with <see cref="LinkScheme.LinkArgument"/> and the link. A process started that way may
/// bring a window to the front, which the background the Task Scheduler started may not:
/// measured on 2026-10-11 in his Firefox 157.0.1 with a scratch profile, a folder and the
/// picker came to the front when the link's own process opened them, and the picker stayed
/// behind when the background showed it, even with the right handed over
/// (<c>kb/windows/loopback-page.md</c>).
/// </para>
/// <para>
/// <b>It asks the background first, over the pipe</b>: the token the link carries has to be
/// the token of the page the background serves now, and the link names what it opens by the
/// page's own names, so a link another page drew opens nothing. For the picker it keeps the
/// connection open, shows the picker, and sends back what was chosen; the background runs
/// the setup the button would have run.
/// </para>
/// <para>
/// <b>71 a, the maintainer's words verbatim: <i>"71 a"</i></b>: with no background to ask, it
/// writes one line in the log and does nothing else. It starts no background and opens no
/// tab, because any web page can show a link of this scheme, and a click there would then
/// start BrowserAI. The page that stopped answering says to start BrowserAI from the Start
/// Menu.
/// </para>
/// </remarks>
internal static partial class LinkStart
{
    /// <summary>The link a start was handed, or <see langword="null"/> for a start that is no link's.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>The link.</returns>
    public static string? LinkFrom(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], LinkScheme.LinkArgument, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    /// <summary>Asks the background about one link, and opens what it answers.</summary>
    /// <param name="link">The link, as Windows passed it.</param>
    /// <param name="scheme">
    /// This install's scheme, or <see langword="null"/> for a build that is not installed,
    /// which reads a link of any scheme BrowserAI could have registered.
    /// </param>
    /// <param name="pipeName">The background's pipe.</param>
    /// <param name="desktop">Explorer and the picker: <see cref="LinkDesktop.Windows"/> in the product.</param>
    /// <param name="logger">Where the one line it writes goes, never with the token.</param>
    /// <returns>Zero when a folder was opened or a picker shown, and one otherwise.</returns>
    public static async Task<int> RunAsync(string link, string? scheme, string pipeName, LinkDesktop desktop, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(logger);

        if (Read(link, scheme) is not { } ask)
        {
            LinkLog.NotALink(logger, link.Length);
            return 1;
        }

        var described = PageLinks.Described(ask);
        var (conversation, failure) = await BackgroundConversation.OpenAsync(pipeName, Coordination.CoordinatorProtocol.HandOutBound).ConfigureAwait(false);

        if (conversation is null)
        {
            LinkLog.NoBackground(logger, described, failure?.Sentence ?? "No background serves the pipe.");
            return 1;
        }

        await using (conversation.ConfigureAwait(false))
        {
            var answer = await conversation.AskAsync(BackgroundPipe.Link, ParametersOf(ask), Coordination.CoordinatorProtocol.HandOutBound).ConfigureAwait(false);

            switch (answer.Outcome)
            {
                case BackgroundAnswerOutcome.Refused:
                    LinkLog.Refused(logger, described, answer.Sentence ?? "It gave no reason.");
                    return 1;

                case not BackgroundAnswerOutcome.Answered:
                    LinkLog.NoBackground(logger, described, answer.Sentence ?? "It did not answer.");
                    return 1;
            }

            if (answer.ResultMember("open") is { Length: > 0 } directory)
            {
                var opened = await desktop.OpenFolder(directory).ConfigureAwait(false);

                LinkLog.Opened(logger, described, directory, opened);
                return opened ? 0 : 1;
            }

            if (answer.ResultMember("pick") is not { Length: > 0 } prompt)
            {
                LinkLog.Refused(logger, described, "It named neither a folder nor a picker.");
                return 1;
            }

            var picked = await desktop.PickFolder(prompt).ConfigureAwait(false);

            // The background runs the setup the choice asks for before it answers, so the
            // wait covers RegisterAI's own budget and the background's.
            var done = await conversation.AskAsync(BackgroundPipe.Picked, PickedParametersOf(picked), ProcessBounds.RegisterAiBudget + ProcessBounds.HandOutBound).ConfigureAwait(false);

            LinkLog.Picked(logger, described, done.Outcome is BackgroundAnswerOutcome.Answered ? done.ResultMember("said") ?? "The background said nothing of the choice." : done.Sentence ?? "The background did not answer after the choice.");
            return 0;
        }
    }

    /// <summary>
    /// A link read with this install's scheme, or, for a build that is not installed, with the
    /// scheme the link carries when it is one BrowserAI could have registered.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <param name="scheme">This install's scheme, or <see langword="null"/>.</param>
    /// <returns>What it asks for, or <see langword="null"/>.</returns>
    internal static LinkAsk? Read(string link, string? scheme)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (scheme is { Length: > 0 })
        {
            return PageLinks.Parse(link, scheme);
        }

        var colon = link.IndexOf(':', StringComparison.Ordinal);
        var carried = colon > 0 ? link[..colon] : string.Empty;

        return carried.Equals(LinkScheme.ShippingScheme, StringComparison.OrdinalIgnoreCase)
            || carried.StartsWith(LinkScheme.ShippingScheme + "-", StringComparison.OrdinalIgnoreCase)
                ? PageLinks.Parse(link, carried)
                : null;
    }

    /// <summary>The question's parameters, as the background's pipe reads them.</summary>
    private static string ParametersOf(LinkAsk ask)
    {
        var parameters = new JsonObject
        {
            ["verb"] = PageLinks.NameOf(ask.Verb),
            ["token"] = ask.Token,
        };

        if (ask.Folder is { } folder)
        {
            parameters["folder"] = folder;
        }

        if (ask.Session is { } session)
        {
            parameters["session"] = session;
        }

        if (ask.Action is { } action)
        {
            parameters["action"] = action;
        }

        if (ask.Client is { } client)
        {
            parameters["client"] = client;
        }

        return parameters.ToJsonString();
    }

    /// <summary>What the picker came back with, as the background's pipe reads it.</summary>
    private static string PickedParametersOf(FolderPick picked) =>
        picked.Outcome switch
        {
            FolderPickOutcome.Picked when picked.Path is { Length: > 0 } path => new JsonObject { ["folder"] = path }.ToJsonString(),
            FolderPickOutcome.Failed => new JsonObject { ["failed"] = picked.Reason ?? "the chosen folder could not be turned into a path" }.ToJsonString(),
            _ => new JsonObject { ["cancelled"] = true }.ToJsonString(),
        };

    /// <summary>The link start's own records: one line each, never the token.</summary>
    private static partial class LinkLog
    {
        // 71 a, 2026-10-11: the one line, and nothing else.
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "A folder link reached BrowserAI while no background answered, so nothing was opened ({Link}). {Why}")]
        public static partial void NoBackground(ILogger logger, string link, string why);

        [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "BrowserAI's background refused a folder link, so nothing was opened ({Link}). {Sentence}")]
        public static partial void Refused(ILogger logger, string link, string sentence);

        [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "A folder link opened {Directory} in Explorer ({Link}), and the shell said {Opened}.")]
        public static partial void Opened(ILogger logger, string link, string directory, bool opened);

        [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "A folder link showed Windows' folder picker ({Link}). {Said}")]
        public static partial void Picked(ILogger logger, string link, string said);

        [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "BrowserAI was started for a link of {Length} characters that is not one of its folder links, so nothing was asked or opened.")]
        public static partial void NotALink(ILogger logger, int length);
    }
}
