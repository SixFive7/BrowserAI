// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;

namespace BrowserAI.Runtime;

/// <summary>
/// What <c>playwright-core/browsers.json</c> says about the browsers this build
/// provisions: the revision, the version, and therefore the directory name.
/// </summary>
/// <remarks>
/// <para>
/// <b>The revision is read from the payload, never typed into C#.</b> That file
/// is inside the artifact and no "latest" lookup exists anywhere in upstream's
/// registry code, so a release knows forever exactly which browser it wants —
/// and a bump moves this number without anybody editing anything. A literal
/// <c>chromium-1237</c> in the product would keep resolving after the payload
/// moved on, and the failure would be a browser that is present and wrong.
/// </para>
/// <para>
/// <b>The directory layout is asymmetric and a path built consistently is
/// wrong.</b> The outer directory uses an underscore before the revision
/// (<c>chromium-1237</c> is a dash, but <c>chromium_headless_shell-1237</c> is
/// an underscore inside the name) and the inner one uses dashes:
/// <c>chromium-1237\chrome-win64\chrome.exe</c>. Only the outer directory is
/// computed here, because it is the only one whose spelling is a pure function
/// of the revision.
/// </para>
/// <para>
/// <b><c>INSTALLATION_COMPLETE</c> is written last, and Playwright never checks
/// it at launch.</b> That asymmetry is the whole reason this type exposes it: an
/// <i>interrupted</i> install self-heals because the marker is absent, but a
/// tree that is half there and unmarked launches as <c>spawn EFTYPE</c> — and
/// upstream then writes <c>DEPENDENCIES_VALIDATED</c> into the corrupt directory
/// and suppresses revalidation for thirty days. BrowserAI checks the marker
/// before it decides a browser is present, which is the check upstream does not
/// make.
/// </para>
/// </remarks>
internal sealed class BrowsersManifest
{
    /// <summary>
    /// The sentinel Playwright writes after a successful install, and the only
    /// evidence that a browser directory is complete rather than partial.
    /// </summary>
    public const string InstallationCompleteMarker = "INSTALLATION_COMPLETE";

    private readonly Dictionary<string, BrowserRevision> _browsers;

    private BrowsersManifest(Dictionary<string, BrowserRevision> browsers) => _browsers = browsers;

    /// <summary>
    /// Every entry the resolved manifest names, which is the set that decides
    /// what is <b>current</b> and therefore what is superseded.
    /// </summary>
    /// <remarks>
    /// <b>All of them, not just the two families BrowserAI installs.</b> A
    /// chromium install also lays down <c>ffmpeg</c> and <c>winldd</c>, so a
    /// revision bump strands those too, and
    /// <see cref="RevisionPrune"/> has to recognise a directory as one of
    /// upstream's before it will consider deleting it. The two BrowserAI asks for
    /// by name are <see cref="ProvisionedBrowsers.Families"/>; this is what a
    /// browsers root may legitimately contain.
    /// </remarks>
    public IReadOnlyList<BrowserRevision> Entries => [.. _browsers.Values];

    /// <summary>Reads the manifest out of an assembled payload.</summary>
    /// <param name="payload">Where <c>playwright-core</c> lives.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="FileNotFoundException">The payload carries no <c>browsers.json</c>.</exception>
    /// <exception cref="InvalidOperationException">It carries one this build cannot read.</exception>
    public static BrowsersManifest Read(PayloadLayout payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var path = payload.BrowsersManifest;

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The payload is incomplete: '{path}' does not exist, so BrowserAI cannot tell which browser revision it is meant to provision. Run build/Build-Payload.ps1, or reinstall.",
                path);
        }

        var browsers = new Dictionary<string, BrowserRevision>(StringComparer.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(File.ReadAllBytes(path));

        if (!document.RootElement.TryGetProperty("browsers", out var entries) || entries.ValueKind is not JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"'{path}' carries no 'browsers' array, so the revision this build provisions cannot be read from it.");
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } browser
                && entry.TryGetProperty("revision", out var revision) && revision.GetString() is { Length: > 0 } number)
            {
                var version = entry.TryGetProperty("browserVersion", out var browserVersion)
                    ? browserVersion.GetString()
                    : null;

                browsers[browser] = new BrowserRevision(browser, number, version);
            }
        }

        return browsers.Count is 0
            ? throw new InvalidOperationException($"'{path}' names no browser this build could read a revision for.")
            : new BrowsersManifest(browsers);
    }

    /// <summary>What the manifest says about one browser family.</summary>
    /// <param name="browser">The family, as upstream names it — <c>chromium</c>, <c>firefox</c>.</param>
    /// <returns>The revision.</returns>
    /// <exception cref="InvalidOperationException">The manifest does not name it.</exception>
    public BrowserRevision For(string browser) =>
        _browsers.TryGetValue(browser, out var revision)
            ? revision
            : throw new InvalidOperationException(
                $"'{browser}' is not a browser the resolved playwright-core knows about. It names: {string.Join(", ", _browsers.Keys.Order(StringComparer.Ordinal))}.");
}

/// <summary>One browser family's pinned revision, and where it lands on disk.</summary>
/// <param name="Name">The family, as upstream names it.</param>
/// <param name="Revision">The revision, as a string, because that is how it appears in the directory name.</param>
/// <param name="BrowserVersion">The marketing version, when the manifest carries one.</param>
internal sealed record BrowserRevision(string Name, string Revision, string? BrowserVersion)
{
    /// <summary>
    /// Everything a directory of this browser's is named before the revision,
    /// including the separator: <c>chromium-</c>, <c>chromium_headless_shell-</c>.
    /// </summary>
    /// <remarks>
    /// <b>This is upstream's own rule for deciding that a directory belongs to a
    /// browser</b>, and it is why the underscore matters:
    /// <c>browserDirectoryPrefix.replace(/-/g, "_") + "-" + revision</c>, read
    /// 2026-08-17 out of the resolved <c>playwright-core</c> bundle, with the
    /// comment beside it saying why — <c>webkit</c> is a prefix of
    /// <c>webkit-technology-preview</c>, so a folder name that kept its dashes
    /// would make an older registry delete the wrong tree. Matching on this rather
    /// than on the bare name is what stops <see cref="RevisionPrune"/> inheriting
    /// that bug.
    /// </remarks>
    public string DirectoryPrefix => Name.Replace('-', '_') + "-";

    /// <summary>The directory this revision installs into, relative to the browsers root.</summary>
    /// <remarks>
    /// ⚠️ <b>Corrected 2026-08-17 (previously <c>$"{Name}-{Revision}"</c>).</b>
    /// That is right for every family this build provisions and wrong for
    /// <c>chromium-headless-shell</c>, which lands in
    /// <c>chromium_headless_shell-1237</c>. It never mattered while the only
    /// callers asked about <c>chromium</c> and <c>firefox</c>; it matters the
    /// moment something walks the whole root, because a name computed one way and
    /// a directory spelled another is a tree nothing recognises.
    /// </remarks>
    public string DirectoryName => DirectoryPrefix + Revision;

    /// <summary>How this browser reads in a sentence written for a model.</summary>
    public string Description =>
        BrowserVersion is { Length: > 0 } version
            ? $"{Name} {version} (revision {Revision})"
            : $"{Name} (revision {Revision})";
}
