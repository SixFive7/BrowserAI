// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Coordination;

/// <summary>
/// What is left of the coordinator's protocol: the bound a person's start gives the
/// background to hand out a tab, and the three arguments that make a start open a
/// page.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Corrected 2026-10-10 (previously "What the coordinator's pipe is
/// called, what it may be asked, how it answers, and the two arguments the
/// coordinator's hidden starts carry.")</b>. <b>This is no longer a protocol.</b>
/// The coordinator's pipe went with S a on 2026-10-08, when one resident background
/// took the coordinator's place, and a person's start and the toast's activator ask
/// the background through its own pipe (<c>BackgroundPipe</c>,
/// <c>BackgroundClient</c>). What nothing read after that day was deleted on
/// 2026-10-10 by the maintainer's decision <i>"9 a"</i>: the verbs and
/// <c>CoordinatorVerb</c>, the pipe's name and <c>NameFor</c>, the version, the
/// acknowledgement and its reading, the address member, the two refusals
/// (<c>StoppingRefusal</c>, <c>NoHostRefusal</c>), and the hidden starts' three
/// arguments, <c>--sign-in</c>, <c>--coordinate</c> and <c>--start-host</c>, which
/// <c>Program</c> still recognises as a task an older build wrote, under names of its
/// own. What is left is read by a running BrowserAI: <see cref="HandOutBound"/> by
/// <c>PersonStart</c>, whose wait for the background's <c>show</c> it bounds, and the
/// three page arguments by <c>App.Program.PageNameOf</c>, which turns them into the
/// page a start asks for, and by <c>StartModes.ArgumentsFor</c>, which turns a page
/// back into them for the toast's activator. The name is kept so that none of their
/// readers moves. The paragraphs below are the record of the protocol.
/// </para>
/// <para>
/// <b>Q284 a, the maintainer's words verbatim: <i>"Q284 a"</i>.</b> The
/// coordinator serves one pipe of its own, created with
/// <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c>, and holding it is what makes a process
/// the coordinator: a second start that cannot create it connects to it instead,
/// learns the coordinator's pid, and hands over. Measured 2026-09-24 over twenty
/// simultaneous starts, three rounds: one winner every round, every other start
/// handed over in 2.8 to 31 ms and learned the winner's pid, and after the winner
/// was killed the next start took over in 0.2 ms
/// ([kb](../../../kb/windows/processes.md#a-second-start-finds-the-first-through-a-pipe-and-learns-its-pid)).
/// </para>
/// <para>
/// <b>Named for the install root the way the census gate is.</b> The census gate is
/// <c>Global\BrowserAI-Live-</c> and the root's key; this is
/// <c>\\.\pipe\BrowserAI-Coordinator-</c> and the same key
/// (<c>LiveInstances.RootKeyFor</c>), so one install has one coordinator and
/// two installs of one pack id under two roots have two. A server's own pipe is
/// <c>\\.\pipe\BrowserAI-</c> and a pid, so the two families cannot collide.
/// </para>
/// <para>
/// <b>The framing is a server pipe's</b>: one verb and a newline in, four bytes of
/// little-endian length and that much UTF-8 JSON out, through the same serving
/// loop (<c>ServerPipe.OpenNamed</c>, deleted with the coordinator's pipe on 2026-10-08).
/// </para>
/// </remarks>
internal static class CoordinatorProtocol
{
    /// <summary>
    /// How long a start waits for a verb that asks for a tab: <b>10 s</b>.
    /// </summary>
    /// <remarks>
    /// <b>Longer than <c>ServerPipeProtocol.CallBound</c> on purpose</b>, a bound
    /// deleted on 2026-10-10 with the pipe per server it bounded.
    /// Such a verb may start the page's listener inside the coordinator before it is
    /// answered, and that is Kestrel starting, not a pipe answering from memory. The
    /// bound is a hang detector for a person's start: a coordinator that has not
    /// answered by then is not starting a listener.
    /// </remarks>
    public static TimeSpan HandOutBound { get; } = ProcessBounds.HandOutBound;

    /// <summary>
    /// The argument that makes a person's start open the sessions page: what the
    /// update toast's <i>Review</i> starts the app with, the way the Start Menu
    /// starts it with nothing (Q339).
    /// </summary>
    public const string SessionsArgument = "--sessions";

    /// <summary>The argument that makes a person's start open the update page: what the toast activator starts with for <i>Install now</i>.</summary>
    public const string UpdateArgument = "--update";

    /// <summary>The argument that makes a person's start open the changelog page: what the toast activator starts with for <i>Changelog</i>.</summary>
    public const string ChangelogArgument = "--changelog";
}
