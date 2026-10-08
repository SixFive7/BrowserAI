// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Coordination;

namespace BrowserAI.App;

/// <summary>The arguments a person's start opens one page with.</summary>
/// <remarks>
/// <para>
/// ⚠️ <b>What is left of the coordinator, since 2026-10-08</b> (S a and D13 a, the
/// maintainer's words verbatim: <i>"s a"</i> and <i>"d13 a"</i>). Until that day
/// this file held the coordinator: the start modes it read (<c>--sign-in</c>,
/// <c>--coordinate</c>, <c>--start-host</c>), its loop, which waited on every
/// process running from the install and applied a staged update once nothing else
/// ran, the sign-in step, and the page's interface to that loop. The one resident
/// background took all of it: the task starts the background, the background's
/// update core decides when an update installs, and a person's start only asks the
/// background for a tab (<see cref="PersonStart"/>).
/// </para>
/// <para>
/// <b>The toasts' activator still opens a page as a person's start</b> (T, decided
/// 2026-10-08): a click on <i>Install now</i> or <i>Changelog</i> becomes the
/// arguments below, and the start goes on as the start a person makes with them.
/// </para>
/// </remarks>
internal static class StartModes
{
    /// <summary>The argument a person's start opens one page with, by the page's name.</summary>
    /// <param name="page">The page's name: <c>update</c>, <c>changelog</c> or <c>sessions</c>; anything else is the status page.</param>
    /// <returns>The arguments.</returns>
    public static string[] ArgumentsFor(string page) => page switch
    {
        "update" => [CoordinatorProtocol.UpdateArgument],
        "changelog" => [CoordinatorProtocol.ChangelogArgument],
        "sessions" => [CoordinatorProtocol.SessionsArgument],
        _ => [],
    };
}
