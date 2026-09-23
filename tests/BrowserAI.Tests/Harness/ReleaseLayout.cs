// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;
using System.Text.RegularExpressions;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// What a release looks like on disk, read out of the release script rather than
/// spelled here.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two names, and the difference between them is the install layout.</b>
/// <c>$packId</c> is <c>BrowserAI.app</c> and is what Velopack derives the
/// install directory from; <c>$downloadId</c> is <c>BrowserAI</c> and is what the
/// two human-facing artifacts are renamed to — <c>BrowserAI.exe</c> and
/// <c>BrowserAI.zip</c> on the default channel. A suite that typed either one would go on
/// agreeing with itself after somebody changed the script, which is the one
/// thing it must not do — the capability below is what decides whether a real
/// installer is exercised at all.
/// </para>
/// <para>
/// <b>The feed manifest is read for the id rather than the installer being
/// trusted for it.</b> <c>Releases/</c> is gitignored and accumulates: a
/// <c>BrowserAI.exe</c> left there by a build from before the current naming has
/// exactly the name the current one has, and installing it would prove a
/// property of last month's layout while reporting a pass. *(Was
/// <c>BrowserAI-win-Setup.exe</c> until 2026-09-15; the hazard is the same one,
/// and a stale file under the NEW name is the same trap under a shorter
/// spelling.)* <c>vpk pack</c>
/// rewrites <c>releases.&lt;channel&gt;.json</c> on every pack, so the
/// <c>PackageId</c> in it is the freshest statement of what the file beside it
/// was built from.
/// </para>
/// </remarks>
internal static partial class ReleaseLayout
{
    /// <summary>The channel the release script packs by default.</summary>
    public const string Channel = "win";

    /// <summary>
    /// The variable that points this at a feed somewhere other than
    /// <c>Releases/</c>.
    /// </summary>
    /// <remarks>
    /// <b>The same lever <c>BROWSERAI_RELEASE_PACKAGE</c> is, one level up.</b>
    /// It names a <i>directory</i> rather than a file, because the capability is
    /// two files that have to agree — the installer and the feed manifest packed
    /// beside it — and a variable naming only the installer would be a way of
    /// pointing the arm at last month's layout by hand. It exists so that a pack
    /// into a scratch output directory can be exercised without writing into the
    /// repository's own <c>Releases/</c>.
    /// </remarks>
    public const string FeedVariable = "BROWSERAI_RELEASE_FEED";

    /// <summary>Where <c>vpk</c> writes, which <c>.gitignore</c> already covers.</summary>
    public static string Directory { get; } =
        Environment.GetEnvironmentVariable(FeedVariable) is { Length: > 0 } feed
            ? feed
            : Path.Combine(RepositoryLayout.Root.FullName, "Releases");

    /// <summary>The Velopack pack id, which is also the install directory's name.</summary>
    public static string PackId { get; } = ReadVariable("packId");

    /// <summary>The name the downloads are renamed to.</summary>
    public static string DownloadId { get; } = ReadVariable("downloadId");

    /// <summary>
    /// The pack id the <b>suite</b> installs, which is the shipping id plus a
    /// suffix and is never published.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Velopack writes one Add/Remove Programs key per pack id per user,
    /// named for the id and never for the location.</b> An install under
    /// <c>--installto</c> still rewrites <c>HKCU\…\Uninstall\&lt;packId&gt;</c> to
    /// point at the scratch root, and <c>Update.exe uninstall</c> from that root
    /// calls <c>delete_subkey_all(&lt;id&gt;)</c> unconditionally — there is no
    /// comparison against <c>InstallLocation</c> anywhere in it. So an installer
    /// arm packed under the shipping id destroys a real install's entry, and a
    /// run killed part-way leaves it gone with nothing to restore it. Measured
    /// on this machine: no <c>BrowserAI.app</c> key after six installer-arm runs,
    /// and that was with no real install present to lose.
    /// </para>
    /// <para>
    /// <b>The id and the title are the delta</b>, and
    /// <c>ReleaseScriptTests.TheSuitesInstallerIsPackedUnderATestIdIntoADirectoryOfItsOwn</c>
    /// holds that the second pack is the first one's argument list with the id,
    /// the title and the output directory replaced. What the arm exercises is
    /// therefore the same code path under names that cannot collide with
    /// anybody's install. <i>Corrected 2026-09-16 (previously "The id is the only
    /// delta … with the id and the output directory replaced")</i> — see
    /// <see cref="TestPackTitle"/> for what a shared title did to the Start Menu.
    /// </para>
    /// </remarks>
    public static string TestPackId { get; } = ReadVariable("testPackId");

    /// <summary>What the shipping pack calls itself.</summary>
    /// <remarks>
    /// The Start Menu shortcut is named for this and never for the id, so it is
    /// the second thing that has to differ between the two packs.
    /// </remarks>
    public static string PackTitle { get; } = ReadVariable("packTitle");

    /// <summary>
    /// What the <b>suite's</b> pack calls itself, which is never what the
    /// shipping one does.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Velopack names the Start Menu shortcut after the TITLE, not after
    /// the pack id</b> (<c>shortcuts.rs</c>, read at 1.2.0: the link file is
    /// <c>&lt;title&gt;.lnk</c>), shortcut creation is not gated on a silent
    /// install, and the uninstall removes shortcuts by target. Two packs sharing
    /// one title therefore share one <c>.lnk</c>: the suite's installer arm
    /// rewrote <c>%APPDATA%\…\Start Menu\Programs\BrowserAI.lnk</c> to point at
    /// its scratch root, and its uninstall then deleted it — destroying a real
    /// install's Start Menu entry exactly the way the shared pack id destroyed
    /// the real Add/Remove entry. <i>Found 2026-09-16 by review, after the id
    /// split had been made and the title had been left behind.</i>
    /// </remarks>
    public static string TestPackTitle { get; } = ReadVariable("testPackTitle");

    /// <summary>
    /// The per-user Start Menu directory the shortcuts land in.
    /// </summary>
    /// <remarks>
    /// <c>Environment.SpecialFolder.Programs</c> — the same directory Velopack
    /// writes to for a per-user install, which is the only kind this product
    /// does.
    /// </remarks>
    public static string StartMenuPrograms { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.Programs);

    /// <summary>What the suite's own installer is called.</summary>
    /// <remarks>
    /// Deliberately nothing that could be read as a release artifact: these two
    /// files sit one directory below the ones a person downloads.
    /// </remarks>
    public static string TestDownloadId { get; } = ReadVariable("testDownloadId");

    /// <summary>
    /// What the download names carry after the id, which is nothing on the
    /// default channel.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The rule is the script's and the literal is read out of it —
    /// 2026-09-15.</b> A person downloads <c>BrowserAI.exe</c> and
    /// <c>BrowserAI.zip</c>; a channel that is not the default keeps its name so
    /// that two packs into one output directory cannot overwrite each other,
    /// which is the one property vpk's own <c>-win-Setup</c> naming had.
    /// <see cref="Channel"/> is what this suite packs, and
    /// <c>$defaultChannel</c> is what the script compares against — read from
    /// the script for the same reason <see cref="PackId"/> is, so a suite that
    /// typed either would go on agreeing with itself after somebody changed it.
    /// </remarks>
    public static string DownloadSuffix { get; } =
        string.Equals(Channel, ReadVariable("defaultChannel"), StringComparison.Ordinal)
            ? string.Empty
            : $"-{Channel}";

    /// <summary>The installer a person downloads, if this machine has built one.</summary>
    public static string SetupExecutable { get; } = Path.Combine(Directory, $"{DownloadId}{DownloadSuffix}.exe");

    /// <summary>The portable archive published beside it.</summary>
    public static string PortableArchive { get; } = Path.Combine(Directory, $"{DownloadId}{DownloadSuffix}.zip");

    /// <summary>The feed manifest <c>vpk</c> rewrites on every pack.</summary>
    public static string FeedManifest { get; } = Path.Combine(Directory, $"releases.{Channel}.json");

    /// <summary>
    /// Where the second pack lands, which nothing that publishes ever looks at.
    /// </summary>
    /// <remarks>
    /// A directory of its own rather than a second file beside the first, so
    /// that a glob over the feed directory for the artifacts to upload cannot
    /// pick one up.
    /// </remarks>
    public static string TestDirectory { get; } = Path.Combine(Directory, "test-pack");

    /// <summary>The installer the suite is allowed to run.</summary>
    public static string TestSetupExecutable { get; } = Path.Combine(TestDirectory, $"{TestDownloadId}-installer.exe");

    /// <summary>The feed manifest packed beside it.</summary>
    public static string TestFeedManifest { get; } = Path.Combine(TestDirectory, $"releases.{Channel}.json");

    /// <summary>The full package of each pack, or <see langword="null"/> when one is absent.</summary>
    /// <param name="test">Whether to look for the test pack rather than the shipping one.</param>
    /// <returns>The newest matching <c>.nupkg</c>, or <see langword="null"/>.</returns>
    public static FileInfo? FullPackage(bool test)
    {
        var directory = new DirectoryInfo(test ? TestDirectory : Directory);

        return directory.Exists
            ? directory.EnumerateFiles($"{(test ? TestPackId : PackId)}-*-full.nupkg", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault()
            : null;
    }

    /// <summary>
    /// The Add/Remove Programs key Velopack writes for this pack id, per user.
    /// </summary>
    /// <remarks>
    /// <b>One key per id per user, named for the id and never for the
    /// location</b> — [measured 2026-09-14](../../../kb/packaging/velopack.md#two-installs-of-one-app-id-share-one-uninstall-key--measured-2026-09-14),
    /// by accident, on this machine: a second install with <c>--installto</c>
    /// rewrote the first one's entry, and the second uninstall deleted it.
    /// </remarks>
    public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>The key the suite's own installs write, and the only one it judges.</summary>
    public static string TestUninstallKey { get; } = $@"{UninstallKeyPath}\{TestPackId}";

    /// <summary>The one line that clears a leftover key by hand.</summary>
    public static string ClearTheLeftoverKey { get; } = $@"reg delete ""HKCU\{TestUninstallKey}"" /f";

    /// <summary>What an uninstall entry found on this machine is.</summary>
    internal enum UninstallKeyState
    {
        /// <summary>No key at all, which is what a clean machine looks like.</summary>
        Absent,

        /// <summary>A key nothing answers to: its install location is gone, or it is scratch.</summary>
        Dangling,

        /// <summary>A key pointing at a directory that exists and is not the suite's.</summary>
        Real,
    }

    /// <summary>
    /// Classifies an uninstall entry from its <c>InstallLocation</c> alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A pure function over the value, so it can be watched in both
    /// directions without writing a registry key.</b> Planting a real
    /// <c>HKCU</c> entry to exercise a refusal would be the suite doing the
    /// exact thing the refusal exists to prevent, so the classification is
    /// asserted over constructed inputs and the <i>reading</i> of the key is the
    /// one line that touches the registry.
    /// </para>
    /// <para>
    /// ⚠️ <b>A key whose location exists and is not scratch is REAL and is never
    /// refused.</b> The maintainer has one; refusing it would redden every run
    /// on the machine this product is developed on. That is why the capability
    /// judges the <b>test</b> id and never the shipping one — under the test id
    /// every key is the suite's own, so any key that outlives a run is a run
    /// that did not clean up.
    /// </para>
    /// </remarks>
    /// <param name="installLocation">The key's <c>InstallLocation</c>, or <see langword="null"/> when there is no key.</param>
    /// <param name="scratchRoot">The root every directory the suite installs into lies under.</param>
    /// <returns>What the entry is.</returns>
    internal static UninstallKeyState Judge(string? installLocation, string scratchRoot)
    {
        if (installLocation is null)
        {
            return UninstallKeyState.Absent;
        }

        if (installLocation.Length is 0 || !System.IO.Directory.Exists(installLocation))
        {
            return UninstallKeyState.Dangling;
        }

        var full = Path.GetFullPath(installLocation);
        var root = Path.GetFullPath(scratchRoot);

        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? UninstallKeyState.Dangling
            : UninstallKeyState.Real;
    }

    /// <summary>
    /// The <c>InstallLocation</c> of a leftover test-id entry, or
    /// <see langword="null"/> when there is none.
    /// </summary>
    /// <remarks>
    /// <b>The empty string is a key with no location rather than no key</b>, and
    /// the two are different: the second is a clean machine and the first is an
    /// entry Windows will show in Settings with nothing behind it.
    /// </remarks>
    /// <returns>The recorded location, <c>""</c> when the key carries none, or <see langword="null"/>.</returns>
    public static string? LeftoverTestInstallLocation()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(TestUninstallKey);

        return key is null ? null : key.GetValue("InstallLocation") as string ?? string.Empty;
    }

    /// <summary>
    /// Whether the installer beside the feed manifest was packed with the pack
    /// id this tree uses.
    /// </summary>
    /// <remarks>
    /// <see langword="false"/> is <i>this machine has no installer of this
    /// layout</i> rather than <i>something is broken</i> — a developer who has
    /// never run the release script and one whose <c>Releases/</c> holds a build
    /// from before the rename are the same absence to every caller, and both
    /// are answered by running the script.
    /// </remarks>
    public static bool HasCurrentInstaller()
    {
        if (!File.Exists(TestSetupExecutable) || !File.Exists(TestFeedManifest) || LeftoverTestInstallLocation() is not null)
        {
            return false;
        }

        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(TestFeedManifest));

            if (!manifest.RootElement.TryGetProperty("Assets", out var assets))
            {
                return false;
            }

            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("PackageId", out var id)
                    && string.Equals(id.GetString(), TestPackId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception failure) when (failure is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>What is wrong, or what is there, in one clause for the coverage block.</summary>
    /// <returns>The witness.</returns>
    public static string Witness() =>
        !File.Exists(TestSetupExecutable)
            ? $"{TestSetupExecutable} (nothing has packed one)"
            : LeftoverTestInstallLocation() is { } leftover
                ? $"HKCU\\{TestUninstallKey} survived a previous run, pointing at '{(leftover.Length is 0 ? "<no InstallLocation>" : leftover)}'. "
                    + "Every key under the TEST id is one this suite wrote, so one that outlived a run is a run that did not clean up — and Settings is showing an uninstall entry for something that is not there. "
                    + $"Clear it with: {ClearTheLeftoverKey}"
                : HasCurrentInstaller()
                    ? TestSetupExecutable
                    : $"{TestSetupExecutable} exists but {Path.GetFileName(TestFeedManifest)} names no package with the id '{TestPackId}', so it was packed under a different layout";

    /// <summary>
    /// The asset names the release script declares a release publishes, exactly
    /// as they are written in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Read out of the script for the same reason <see cref="PackId"/> is.</b>
    /// The upload set was a judgement until 2026-09-23 — nothing named one, so
    /// whoever ran <c>gh release create</c> chose the assets by looking at
    /// <c>Releases/</c>. A suite that typed the three names here would go on
    /// agreeing with itself after somebody changed the script, which is the one
    /// thing it must not do about a list whose whole job is to stop being a
    /// judgement.
    /// </para>
    /// <para>
    /// <b>The templates as written, not the files they resolve to.</b> The names
    /// carry the script's own variables, so the caller expands them and can then
    /// assert both halves separately: that the declaration is these three
    /// templates, and that on the default channel they mean the installer, the
    /// full package and the feed.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> UploadSet { get; } =
        ReadUploadSet(File.ReadAllText(Path.Combine(RepositoryLayout.Root.FullName, "build", "New-Release.ps1")));

    /// <summary>
    /// The quoted names inside the script's <c>$uploadSet</c> declaration.
    /// </summary>
    /// <remarks>
    /// A pure function over the text so that a doctored declaration can be
    /// exercised without writing one into <c>build/</c>.
    /// </remarks>
    /// <param name="script">The release script's text.</param>
    /// <returns>The declared names, in the order they are written.</returns>
    /// <exception cref="InvalidOperationException">The declaration is not there in the shape this reads.</exception>
    internal static List<string> ReadUploadSet(string script)
    {
        var block = UploadSetBlock().Match(script);

        if (!block.Success)
        {
            throw new InvalidOperationException(
                "build/New-Release.ps1 no longer declares $uploadSet as an @( ) list of double-quoted names, "
                + "so the suite cannot read what a release publishes out of it — and an upload set nothing reads back is a judgement again.");
        }

        return [.. QuotedName().Matches(block.Groups["body"].Value).Select(name => name.Groups["name"].Value)];
    }

    /// <summary>The declaration block, from <c>$uploadSet = @(</c> to the closing parenthesis in column one.</summary>
    [GeneratedRegex(@"(?ms)^\$uploadSet\s*=\s*@\(\s*?$(?<body>.*?)^\)")]
    private static partial Regex UploadSetBlock();

    /// <summary>One double-quoted name.</summary>
    [GeneratedRegex("\"(?<name>[^\"]+)\"")]
    private static partial Regex QuotedName();

    private static string ReadVariable(string name)
    {
        var script = File.ReadAllText(Path.Combine(RepositoryLayout.Root.FullName, "build", "New-Release.ps1"));
        var match = Regex.Match(script, $@"(?m)^\${name}\s*=\s*'(?<value>[^']+)'");

        return match.Success
            ? match.Groups["value"].Value
            : throw new InvalidOperationException(
                $"build/New-Release.ps1 no longer assigns ${name} as a single-quoted literal, so the suite cannot read the release layout out of it.");
    }
}
