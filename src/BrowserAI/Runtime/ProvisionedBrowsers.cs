// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Runtime;

/// <summary>
/// The exact executables BrowserAI provisions, resolved from the payload's own
/// <c>browsers.json</c> rather than spelled anywhere.
/// </summary>
/// <remarks>
/// <para>
/// <b>This list is the whole detection surface of
/// [the stray sweep](../../plan/build-order.md#16-the-stray-sweep).</b> A
/// process is a candidate because its full image path is one of these strings
/// and for no other reason — which is why the strings are computed from the
/// resolved revision and the app-paths seam, and never typed as a literal. A
/// hard-coded <c>chromium-1237</c> here would keep matching after the payload
/// moved on, and the failure would be a sweep that finds nothing and reports
/// success.
/// </para>
/// <para>
/// <b>The inner directory is the part no manifest carries.</b>
/// <see cref="BrowserRevision.DirectoryName"/> is a pure function of the
/// revision; the leaf beneath it is a per-family convention Playwright's own
/// downloader knows and publishes nowhere —
/// <c>chromium-&lt;rev&gt;\chrome-win64\chrome.exe</c> against
/// <c>firefox-&lt;rev&gt;\firefox\firefox.exe</c>. Note the asymmetry, and
/// build no path that assumes otherwise; it is asserted against the real
/// provisioned tree by the suite.
/// </para>
/// </remarks>
internal static class ProvisionedBrowsers
{
    /// <summary>The Chromium family, as upstream names it.</summary>
    public const string Chromium = "chromium";

    /// <summary>The Firefox family, as upstream names it.</summary>
    public const string Firefox = "firefox";

    /// <summary>The two families BrowserAI provisions, as upstream names them.</summary>
    public static IReadOnlyList<string> Families { get; } = [Chromium, Firefox];

    /// <summary>Where a family's executable sits inside its revision directory.</summary>
    /// <param name="family">The family, as upstream names it.</param>
    /// <returns>The relative path, or <see langword="null"/> for a family this build does not provision.</returns>
    public static string? ExecutableWithin(string family) => family switch
    {
        Chromium => Path.Combine("chrome-win64", "chrome.exe"),
        Firefox => Path.Combine("firefox", "firefox.exe"),
        _ => null,
    };

    /// <summary>
    /// Every executable this build provisions, absolute, whether or not it is
    /// installed yet.
    /// </summary>
    /// <remarks>
    /// <b>Existence is deliberately not checked.</b> The set is what counts as
    /// ours, and a browser that is mid-provisioning or mid-reinstall is still
    /// ours; filtering on <c>File.Exists</c> would make the sweep blind exactly
    /// while a tree is being replaced.
    /// </remarks>
    /// <param name="browsersDirectory">The provisioned browsers root, absolute.</param>
    /// <param name="manifest">The resolved payload's manifest.</param>
    /// <returns>The absolute executable paths.</returns>
    public static IReadOnlyList<string> Executables(string browsersDirectory, BrowsersManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(browsersDirectory);
        ArgumentNullException.ThrowIfNull(manifest);

        var executables = new List<string>();

        foreach (var family in Families)
        {
            executables.AddRange(ExecutablesFor(family, browsersDirectory, manifest));
        }

        return executables;
    }

    /// <summary>
    /// One family's executables, absolute, whether or not they are installed
    /// yet.
    /// </summary>
    /// <remarks>
    /// <b>The sweep needs the families apart as well as together.</b> Detection
    /// is one question over all of them — is a process running a binary we
    /// provisioned — but attribution is not: Chromium answers <i>which
    /// profile</i> through a message window's title and Firefox only through its
    /// profile lock, so the sweep has to know which candidate belongs to which
    /// mechanism.
    /// </remarks>
    /// <param name="family">The family, as upstream names it.</param>
    /// <param name="browsersDirectory">The provisioned browsers root, absolute.</param>
    /// <param name="manifest">The resolved payload's manifest.</param>
    /// <returns>The absolute executable paths, empty for a family this build does not provision.</returns>
    public static IReadOnlyList<string> ExecutablesFor(string family, string browsersDirectory, BrowsersManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentException.ThrowIfNullOrWhiteSpace(browsersDirectory);
        ArgumentNullException.ThrowIfNull(manifest);

        return ExecutableWithin(family) is { } within
            ? [Path.Combine(browsersDirectory, manifest.For(family).DirectoryName, within)]
            : [];
    }
}
