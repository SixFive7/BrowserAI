// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Runtime;

/// <summary>
/// The exact executables BrowserAI provisions, resolved from the payload's own
/// <c>browsers.json</c> rather than spelled anywhere.
/// </summary>
/// <remarks>
/// <para>
/// <b>This list is the whole detection surface of the stray sweep</b>
/// (<see cref="Sessions.StraySweep"/>, and
/// [kb](../../../kb/windows/detection.md#process-image-path--the-fully-documented-detection-path)
/// for the measurement). A
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

    /// <summary>
    /// The reinstall target that means <b>the components both families share</b>
    /// rather than a browser.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not a family, and deliberately not in <see cref="Families"/>.</b> It has
    /// no executable the stray sweep would look for, no session can be opened
    /// against it, and <c>browserai_init</c> must never offer it — a session's
    /// <c>browser</c> is a thing that renders web pages. What it is, is a third
    /// value for <c>browserai_reinstall_browser</c>'s required argument.
    /// </para>
    /// <para>
    /// ⚠️ <b>Added 2026-08-19, for a repair that had no route through the
    /// product's own surface.</b> <c>ffmpeg</c> and <c>winldd</c> are downloaded
    /// into the same browsers root by <i>both</i> families, each carries its own
    /// <c>INSTALLATION_COMPLETE</c>, and a family reinstall deletes only that
    /// family's revision directory — so a corrupted <c>ffmpeg</c> was permanent,
    /// and <c>ffmpeg</c> is what the <c>video</c> artifact type needs. Measured
    /// 2026-08-19: a Firefox install beside a complete <c>ffmpeg</c> and
    /// <c>winldd</c> downloads only the browser archive, because the marker
    /// short-circuits each component's check without validating anything.
    /// </para>
    /// </remarks>
    public const string Shared = "shared";

    /// <summary>
    /// The manifest entries <see cref="Shared"/> covers, in the order they are
    /// deleted and reported.
    /// </summary>
    /// <remarks>
    /// <b>Names, not revisions.</b> The revision and therefore the directory come
    /// from the payload's own <c>browsers.json</c> through
    /// <c>BrowsersManifest.For</c>, exactly as a family's does; what is spelled
    /// here is only which entries in that manifest are shared, which is a fact
    /// about upstream's installer rather than about any particular release.
    /// </remarks>
    public static IReadOnlyList<string> SharedComponents { get; } = ["ffmpeg", "winldd"];

    /// <summary>
    /// The one name handed to <c>install-browser</c> to rebuild every entry in
    /// <see cref="SharedComponents"/>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Measured, not assumed — 2026-08-19 against the resolved payload.</b>
    /// <c>install-browser ffmpeg</c> into an empty root downloaded
    /// <c>ffmpeg-1011</c> <b>and</b> <c>winldd-1007</c>, each with its own
    /// <c>INSTALLATION_COMPLETE</c>; re-run with only <c>winldd-1007</c> deleted,
    /// it downloaded <c>winldd</c> alone and left the complete <c>ffmpeg</c>
    /// untouched. So one invocation rebuilds whichever of the two is missing, and
    /// asking for both by name would be a second download of whatever was already
    /// there. <b>The completeness check is still per component</b> — one command
    /// is an efficiency, and the marker each component writes is the evidence.
    /// </remarks>
    public const string SharedInstallTarget = "ffmpeg";

    /// <summary>
    /// Everything <c>browserai_reinstall_browser</c>'s required argument accepts:
    /// the two families, then the shared components.
    /// </summary>
    /// <remarks>
    /// A superset of <see cref="Families"/> and never the same list.
    /// <c>browserai_init</c>'s <c>browser</c> reads <see cref="Families"/>, and
    /// the day the two lists become one is the day a caller can ask for a session
    /// driven by a codec.
    /// </remarks>
    public static IReadOnlyList<string> ReinstallTargets { get; } = [.. Families, Shared];

    /// <summary>Whether a reinstall target names the shared components rather than a family.</summary>
    /// <param name="target">The value the caller gave.</param>
    /// <returns>Whether it is <see cref="Shared"/>.</returns>
    public static bool IsShared(string? target) =>
        string.Equals(target, Shared, StringComparison.OrdinalIgnoreCase);

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
