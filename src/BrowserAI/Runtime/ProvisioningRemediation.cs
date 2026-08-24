// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.RegularExpressions;

namespace BrowserAI.Runtime;

/// <summary>
/// Replaces upstream's "run this command" advice with advice that works in a
/// BrowserAI install.
/// </summary>
/// <remarks>
/// <para>
/// <b>The audience is a model, and it will act on the sentence.</b> When the
/// browser executable is missing, <c>throwIfExecutableMissing</c> raises
/// <c>Browser "&lt;target&gt;" is not installed; expected executable at
/// &lt;path&gt;. Run `npx @playwright/mcp install-browser &lt;target&gt;` to
/// install</c> — read out of the resolved bundle 2026-08-16 rather than from
/// memory. Every clause of that is true of a normal Playwright install and wrong
/// here: BrowserAI ships no <c>npx</c>, has no npm project to run it in, and the
/// package that command would fetch resolves to whatever npm calls latest today
/// rather than to the revision this build's <c>browsers.json</c> pins. A model
/// that follows it either fails, or succeeds into a second browser tree in a
/// second location that BrowserAI will never launch.
/// </para>
/// <para>
/// <b>Only the remediation clause is replaced.</b> The half before it —
/// <i>which</i> browser, and the exact path it was expected at — is the useful
/// half and is upstream's to phrase. The target it names is the resolved
/// <c>channel</c> rather than the browser family, so the text a caller sees says
/// <c>chrome-for-testing</c>; that is what makes an empty browsers root fail
/// loudly and recognisably instead of falling back to the user's own Chrome, and
/// it is asserted elsewhere in the suite.
/// </para>
/// <para>
/// ⚠️ <b>This is the one place BrowserAI rewrites a child's answer rather than
/// forwarding its bytes</b>, and it is worth naming the trade. Byte-identical
/// passthrough is the property <c>LosslessPassthroughTests</c> exists to
/// protect; here it is deliberately given up for the one payload that
/// contains an instruction which would send the caller somewhere harmful. The
/// rewrite fires only when the child reported an error <b>and</b> the marker is
/// present — every other answer goes through untouched — and the proxy logs the
/// fact when it does, so a lost byte-identity is a recorded event rather than a
/// silent one.
/// </para>
/// <para>
/// ⚠️ ***Corrected 2026-08-24 (previously "The rewrite fires only when the marker
/// is present — every other answer, including every other error, goes through
/// untouched").*** That understated nothing and overstated the gate: the marker
/// was the whole test, so any answer whose text contained upstream's sentence —
/// a page rendering it in its title, an issue, release notes — had BrowserAI's
/// own instruction text spliced into it and lost byte-identity on an ordinary
/// successful call. <c>BrowserProxy.Remediate</c> now requires
/// <c>isError: true</c> as well, which upstream sets on every answer carrying an
/// <c>Error</c> section and on no other. <b>The gate lives in the proxy and not
/// in <see cref="Rewrite"/></b>, because <c>Rewrite</c> is a pure function over
/// one text block and has no answer to ask about.
/// </para>
/// </remarks>
internal static partial class ProvisioningRemediation
{
    /// <summary>
    /// The substring that decides whether an answer needs looking at, cheap
    /// enough to run on every result.
    /// </summary>
    /// <remarks>
    /// Deliberately the <b>subcommand</b> rather than the package name: upstream
    /// builds the same sentence two ways —
    /// <c>npx @playwright/mcp install-browser &lt;t&gt;</c> normally and
    /// <c>playwright-cli install-browser &lt;t&gt;</c> under <c>skillMode</c> —
    /// and a marker keyed on <c>npx</c> would miss the second the day upstream
    /// changed which branch it takes.
    /// </remarks>
    public const string Marker = "install-browser";

    /// <summary>
    /// Rewrites one text block, or answers <see langword="null"/> when there is
    /// nothing in it to rewrite.
    /// </summary>
    /// <param name="text">A text block from the child's result.</param>
    /// <param name="browsersDirectory">
    /// The browsers root, so the replacement can name the directory a human would
    /// actually delete.
    /// </param>
    /// <returns>The rewritten text, or <see langword="null"/> to forward as-is.</returns>
    public static string? Rewrite(string? text, string browsersDirectory)
    {
        if (text is null || !text.Contains(Marker, StringComparison.Ordinal))
        {
            return null;
        }

        var rewritten = UpstreamAdvice().Replace(text, Replacement(browsersDirectory));

        return string.Equals(rewritten, text, StringComparison.Ordinal) ? null : rewritten;
    }

    /// <summary>What BrowserAI says instead.</summary>
    /// <remarks>
    /// Two routes rather than one, because the two failures behind this message
    /// have different recoveries. A tree that was never downloaded is fixed by
    /// <c>browserai_init</c>, which starts the download and returns immediately.
    /// A tree that was downloaded and then corrupted — a quarantined DLL, a
    /// half-restored backup — is <b>not</b>, because
    /// <c>INSTALLATION_COMPLETE</c> is still sitting in it and every check short-
    /// circuits on that marker without validating anything. Only deleting the
    /// directory, which is what <c>browserai_reinstall_browser</c> does, gets out
    /// of that state.
    /// </remarks>
    /// <param name="browsersDirectory">The browsers root.</param>
    /// <returns>The replacement clause.</returns>
    public static string Replacement(string browsersDirectory) =>
        $"Call browserai_init again to re-provision it — BrowserAI downloads the exact revision it pins, into '{browsersDirectory}', and returns immediately while that happens. "
        + "If it was already downloaded and is damaged, call browserai_reinstall_browser naming the session's own browser family: an install that completed once is never re-downloaded on its own, because the marker upstream writes short-circuits the check without validating anything. "
        + "Do NOT run npx or npm: BrowserAI ships neither, and that command would fetch a different revision into a directory BrowserAI never launches from.";

    /// <summary>
    /// Upstream's whole remediation clause, from <c>Run</c> to the end of the
    /// sentence.
    /// </summary>
    /// <remarks>
    /// Anchored on the backticked command rather than on the package name, so
    /// both branches of upstream's ternary are covered by one pattern and by one
    /// triggering test.
    /// </remarks>
    [GeneratedRegex(@"Run `[^`]*install-browser[^`]*` to install\.?", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UpstreamAdvice();
}
