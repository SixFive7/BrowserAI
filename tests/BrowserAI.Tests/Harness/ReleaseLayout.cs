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
    /// The Add/Remove Programs key Velopack writes for this pack id, per user.
    /// </summary>
    /// <remarks>
    /// <b>One key per id per user, named for the id and never for the
    /// location</b> — [measured 2026-09-14](../../../kb/packaging/velopack.md#two-installs-of-one-app-id-share-one-uninstall-key--measured-2026-09-14),
    /// by accident, on this machine: a second install with <c>--installto</c>
    /// rewrote the first one's entry, and the second uninstall deleted it.
    /// </remarks>
    public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>
    /// Whether a real install of this pack id is registered on this machine.
    /// </summary>
    /// <remarks>
    /// <b>This is a refusal, not a diagnostic.</b> The arm that runs a real
    /// <c>Setup.exe</c> installs under <c>--installto</c>, which is safe for the
    /// files and <i>not</i> safe for the uninstall key: Velopack keeps one per id
    /// per user, so an install here would repoint a real install's entry at a
    /// scratch directory and the uninstall that follows would delete it — leaving
    /// a real BrowserAI on the machine with no Add/Remove entry at all. So the
    /// capability reads ABSENT on a machine that has one, the coverage block says
    /// which key stopped it, and a release run fails rather than doing the damage
    /// quietly.
    /// </remarks>
    /// <returns>Whether an uninstall entry for this id already exists.</returns>
    public static bool ARealInstallIsRegistered()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"{UninstallKeyPath}\{PackId}");

        return key is not null;
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
        if (!File.Exists(SetupExecutable) || !File.Exists(FeedManifest) || ARealInstallIsRegistered())
        {
            return false;
        }

        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(FeedManifest));

            if (!manifest.RootElement.TryGetProperty("Assets", out var assets))
            {
                return false;
            }

            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("PackageId", out var id)
                    && string.Equals(id.GetString(), PackId, StringComparison.OrdinalIgnoreCase))
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
        !File.Exists(SetupExecutable)
            ? $"{SetupExecutable} (nothing has packed one)"
            : ARealInstallIsRegistered()
                ? $"HKCU\\{UninstallKeyPath}\\{PackId} exists, so this machine already has an install of this id — installing here would repoint its Add/Remove entry and the uninstall would delete it"
                : HasCurrentInstaller()
                    ? SetupExecutable
                    : $"{SetupExecutable} exists but {Path.GetFileName(FeedManifest)} names no package with the id '{PackId}', so it was packed under a different layout";

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
