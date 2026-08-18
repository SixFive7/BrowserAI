// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using BrowserAI.Sessions;

namespace BrowserAI.Proxy;

/// <summary>
/// The one string that reaches a model <b>before</b> it calls anything.
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries the choice itself rather than a pointer to it.</b> Claude Code
/// loads tool <i>names</i> and the server <c>instructions</c> eagerly and defers
/// schemas, so this is the only channel that arrives before the first mistake —
/// and the first mistake after a restart is calling a browser tool with no
/// session. A sentence here that says "see <c>browserai_init</c>'s description"
/// is a sentence that arrives after the failure it was meant to prevent.
/// </para>
/// <para>
/// <b>Hard cap 2 KB, and the truncation is silent.</b> The client cuts both this
/// string and every tool description at 2 KB with nothing reported: the tail
/// simply does not exist, and a paragraph past the cut is a paragraph nobody has
/// ever read. <c>ModelSurfaceTests</c> measures it in <b>bytes</b> rather than
/// characters — <c>·</c> and <c>—</c> are two and three bytes of UTF-8 apiece, so
/// a character count would under-report exactly the string that uses them — and
/// fails over budget.
/// </para>
/// <para>
/// <b>The mode lines are rendered from
/// <see cref="SessionModes.Lines"/>, never typed here.</b> This is one of the six
/// consumers of the one table, and it is the one where a stale copy would be
/// least visible: it is read by every model on every connection and by no human
/// ever.
/// </para>
/// </remarks>
internal static class ServerInstructions
{
    /// <summary>
    /// What the client silently truncates at, and therefore what this string
    /// must fit inside.
    /// </summary>
    /// <remarks>
    /// One of the two surfaces <see cref="ClientTruncationBudget"/> quotes the
    /// documentation for by name. The number lives there so that all three
    /// model-facing surfaces cannot drift apart, and so the unresolved reading of
    /// the documentation's <i>"each"</i> is stated once where it can be read.
    /// </remarks>
    public const int MaximumBytes = ClientTruncationBudget.Bytes;

    /// <summary>The instructions sent on <c>initialize</c>.</summary>
    public static string Text { get; } =
        $"""
        BrowserAI drives a real browser. Call {SessionToolSurface.Init} first: it returns a session directory that every other tool requires as 'session'. There is no default and BrowserAI never guesses one.

        Modes — chosen at init, permanent for the session's life:
        {SessionModes.Lines}
        'tracing: true' records the session into its output directory, and works with any of them.

        You must supply an absolute directory. The directory IS the session — its profile, screenshots, downloads and log all live there — so name it for what the work is. You must also supply a one-sentence 'purpose': another agent meeting this directory later reads it.

        {SessionToolSurface.Init} refuses a directory that already holds a session and directs you to {SessionToolSurface.Resume}. That is deliberate rather than an obstacle: it turns an accidental collision into a stated intent. {SessionToolSurface.List} reports the sessions beneath a directory, {SessionToolSurface.Destroy} deletes one, {SessionToolSurface.SetPurpose} rewrites what one says it is for.
        """;

    /// <summary>How many bytes <see cref="Text"/> costs of the budget.</summary>
    public static int ByteCount { get; } = Encoding.UTF8.GetByteCount(Text);
}
