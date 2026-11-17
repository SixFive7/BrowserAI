// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.RegularExpressions;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// How long the payload's own Playwright waits for a page's tool before it gives up on it,
/// read out of the assembled payload.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-11-17 for the maintainer's 201</b>, in his words verbatim: <i>"201 can we
/// set the playwright limit to 600 sec. so ours comes earlier? If not, go with b."</i>
/// <c>playwright-core</c> 1.66.0-alpha-2026-11-09 calls a page's tool through
/// <c>frame.webmcp.callTool(name, params2 ?? {})</c>, with no options, so the call takes the
/// frame's default timeout, which with nothing set on the page or its context is
/// <c>DEFAULT_PLAYWRIGHT_TIMEOUT</c>. Nothing BrowserAI passes sets it, so BrowserAI's own
/// budget has to be the shorter one, and this is what reads the other side of that order.
/// </para>
/// <para>
/// <b>Both halves are read, and either missing is a failure and not a guess.</b> A Playwright
/// that passed a timeout of its own to the call, or renamed the constant, would make this
/// reading wrong in silence; so the call's spelling is required as well as the number.
/// </para>
/// </remarks>
internal static partial class UpstreamPageTool
{
    /// <summary>The payload's own limit on a page tool's call.</summary>
    /// <returns>The limit.</returns>
    public static async Task<TimeSpan> TimeoutAsync()
    {
        var bundle = await File.ReadAllTextAsync(Path.Combine(
            RepositoryPayload.Layout.Root,
            "mcp",
            "node_modules",
            "playwright-core",
            "lib",
            "coreBundle.js")).ConfigureAwait(false);

        return TimeoutIn(bundle);
    }

    /// <summary>The limit one bundle's text sets on a page tool's call.</summary>
    /// <param name="bundle">The text of <c>coreBundle.js</c>.</param>
    /// <returns>The limit.</returns>
    /// <exception cref="InvalidOperationException">The call or the constant is not spelled as read.</exception>
    public static TimeSpan TimeoutIn(string bundle)
    {
        if (!CallWithNoOptions().IsMatch(bundle))
        {
            throw new InvalidOperationException(
                "callWebMCPTool no longer calls frame.webmcp.callTool(name, params2 ?? {}): re-read how the payload's Playwright bounds a page tool before trusting any order of limits.");
        }

        var constant = DefaultTimeout().Match(bundle);

        if (!constant.Success)
        {
            throw new InvalidOperationException("DEFAULT_PLAYWRIGHT_TIMEOUT is no longer set where it was read: re-read the payload's Playwright.");
        }

        return TimeSpan.FromMilliseconds(double.Parse(constant.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    [GeneratedRegex(@"await frame\.webmcp\.callTool\(name, params2 \?\? \{\}\)")]
    private static partial Regex CallWithNoOptions();

    [GeneratedRegex(@"DEFAULT_PLAYWRIGHT_TIMEOUT = (?<value>[0-9.e]+);")]
    private static partial Regex DefaultTimeout();
}
