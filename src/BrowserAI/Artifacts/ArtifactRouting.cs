// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Sessions;

namespace BrowserAI.Artifacts;

/// <summary>How a generator prefix reaches its folder.</summary>
internal enum ArtifactRoute
{
    /// <summary>
    /// The caller names the file, so BrowserAI rewrites the argument before the
    /// child sees it and the file is born in the right place.
    /// </summary>
    Inbound,

    /// <summary>
    /// Nothing names the file on the way in — a browser-initiated download, or a
    /// tool that generates its own name — so it is classified after the fact by
    /// the prefix it was written with.
    /// </summary>
    AfterTheFact,

    /// <summary>
    /// A folder that is not named by a prefix at all. There is exactly one, and
    /// it is <c>traces</c>.
    /// </summary>
    NotAPrefix,
}

/// <summary>One generator prefix and the folder its artifacts belong in.</summary>
/// <param name="Prefix">The prefix, spelled exactly as upstream spells it.</param>
/// <param name="RelativeFolder">Where those artifacts go, relative to the session directory.</param>
/// <param name="Route">How a file gets there.</param>
internal sealed record ArtifactDestination(string Prefix, string RelativeFolder, ArtifactRoute Route);

/// <summary>
/// Where each artifact generator's output belongs beneath a session directory.
/// </summary>
/// <remarks>
/// <para>
/// <b>The folder is named for the prefix, spelled exactly as upstream spells
/// it</b>, because a folder whose name differs from the prefix that fills it is
/// a mapping table nobody maintains. <c>storage-state</c> keeps its hyphen and
/// <c>element</c> is not folded into <c>page</c>.
/// </para>
/// <para>
/// <b>This declares the folders; it does not decide what the prefixes are.</b>
/// The prefix set is measured from the resolved bundle and recorded in
/// <c>upstream-snapshots/tools-list.json</c>, regenerated and diffed on every
/// build. <c>ArtifactRoutingTests</c> compares the two in both directions — a
/// prefix with no folder <i>and</i> a folder with no prefix are each a red
/// build, because a rename presents as one of each and that diff is what says
/// what happened.
/// </para>
/// <para>
/// ⚠️ <b>Twelve entries, not nine.</b> Every document that had counted them —
/// this project's design notes and
/// [kb](../../../kb/playwright/tools-and-artifacts.md#artifacts-and-output-directory-behaviour)
/// alike — said nine until 2026-08-16, when the set was derived from the bundle
/// for the first time rather than counted by hand: <c>element</c> (an element
/// screenshot, chosen by a ternary the earlier scan could not see) and
/// <c>annotations</c> (a template literal, from <c>browser_annotate</c>) were
/// missing, and the empty prefix that produces <c>traces</c> was described as
/// ours by choice when it is upstream's. <b>The coverage gate found its first
/// drift on the day it was written</b>, which is the whole argument for deriving
/// the set instead of typing it.
/// </para>
/// </remarks>
internal static class ArtifactRouting
{
    /// <summary>
    /// The one prefix whose folder is at the session root rather than under
    /// <c>output</c>.
    /// </summary>
    /// <remarks>
    /// A browser-initiated download lands where the browser puts it, not where a
    /// <c>filename</c> argument says, so it is the one exception to routing and
    /// the difference is visible here rather than discovered.
    /// </remarks>
    public const string DownloadPrefix = "download";

    /// <summary>
    /// The empty prefix, which is what the traces template carries.
    /// </summary>
    /// <remarks>
    /// Not a generator prefix: the template supplies <c>suggestedFilename:
    /// "traces"</c> and an empty extension, so upstream resolves it to
    /// <c>&lt;outputDir&gt;/traces</c> and writes a trace into a path it was
    /// handed. The folder is upstream's rather than ours, and the sort must not
    /// pretend to derive it.
    /// </remarks>
    public const string TracesPrefix = "";

    /// <summary>Every prefix, and where its artifacts belong.</summary>
    public static IReadOnlyDictionary<string, ArtifactDestination> Destinations { get; } =
        new[]
        {
            // Ten under `output\`, in the order upstream's own prefix list
            // sorts. All but `annotations` are routed inbound; that one has no
            // `filename` argument to rewrite, so it is sorted after the fact
            // like a download.
            Under("annotations", ArtifactRoute.AfterTheFact),
            Under("console", ArtifactRoute.Inbound),
            Under("element", ArtifactRoute.Inbound),
            Under("network", ArtifactRoute.Inbound),
            Under("page", ArtifactRoute.Inbound),
            Under("request", ArtifactRoute.Inbound),
            Under("response", ArtifactRoute.Inbound),
            Under("result", ArtifactRoute.Inbound),
            Under("storage-state", ArtifactRoute.Inbound),
            Under("video", ArtifactRoute.Inbound),

            // The eleventh, at the session root.
            new ArtifactDestination(DownloadPrefix, SessionLayout.DownloadsFolderName, ArtifactRoute.AfterTheFact),

            // And the one that is not a prefix.
            new ArtifactDestination(TracesPrefix, Path.Combine(SessionLayout.OutputFolderName, "traces"), ArtifactRoute.NotAPrefix),
        }.ToDictionary(destination => destination.Prefix, StringComparer.Ordinal);

    /// <summary>
    /// Every folder a session directory holds, relative to its root, in creation
    /// order.
    /// </summary>
    public static IReadOnlyList<string> Folders { get; } =
    [
        SessionLayout.ProfileFolderName,
        SessionLayout.OutputFolderName,
        .. Destinations.Values.Select(destination => destination.RelativeFolder).Order(StringComparer.Ordinal),
    ];

    /// <summary>
    /// The prefix a generated file name carries, or <see langword="null"/> when
    /// it carries none.
    /// </summary>
    /// <remarks>
    /// This is the <i>after the fact</i> half, and it is deliberately by prefix
    /// rather than by date: a generated name is <c>&lt;prefix&gt;-&lt;timestamp&gt;.&lt;ext&gt;</c>,
    /// and no date rule can tell one from a file a caller named. The longest
    /// match wins so that a future <c>page-x</c> prefix would not be swallowed by
    /// <c>page</c>.
    /// </remarks>
    /// <param name="fileName">A file name, with no directory part.</param>
    /// <returns>The matching prefix, or <see langword="null"/>.</returns>
    public static string? PrefixOf(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        string? best = null;

        foreach (var prefix in Destinations.Keys)
        {
            if (prefix.Length is 0 || !fileName.StartsWith(prefix + "-", StringComparison.Ordinal))
            {
                continue;
            }

            if (best is null || prefix.Length > best.Length)
            {
                best = prefix;
            }
        }

        return best;
    }

    /// <summary>The folder one prefix's artifacts belong in.</summary>
    /// <param name="prefix">The generator prefix.</param>
    /// <returns>The folder, relative to the session directory.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No folder is declared for that prefix.</exception>
    public static string FolderFor(string prefix) =>
        Destinations.TryGetValue(prefix, out var destination)
            ? destination.RelativeFolder
            : throw new ArgumentOutOfRangeException(
                nameof(prefix),
                prefix,
                "No folder is declared for that generator prefix. The prefix set is derived from the resolved bundle and asserted against this table on every build, so reaching this means the gate was bypassed rather than that a prefix is new.");

    private static ArtifactDestination Under(string prefix, ArtifactRoute route) =>
        new(prefix, Path.Combine(SessionLayout.OutputFolderName, prefix), route);
}
