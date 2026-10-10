// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Hosting;

/// <summary>
/// The one folder the shipping install may be in, and the refusal of a copy that is in
/// another one.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The maintainer's 21 of 2026-10-10, verbatim: <i>"21 refusing installing into a
/// non-standard folder so the project specific setups always resolve on every dev's
/// pc."</i></b> And of the way to do it the same day, relayed in his words verbatim,
/// <i>"that proposal sounds go"</i>: the shipping install, pack id
/// <see cref="ShippingPackId"/>, reads no <c>BROWSERAI_ROOT</c> and keeps its data in
/// <c>%LOCALAPPDATA%\BrowserAI</c>; a shipping install outside
/// <c>%LOCALAPPDATA%\BrowserAI.app</c> sets nothing up, records why, and says
/// <see cref="Remedy"/>, in a box after a non-silent install and at every later start;
/// and uninstalling such a copy never touches the standard data root or the clients.
/// The suite's test pack keeps its scratch folders.
/// </para>
/// <para>
/// <b>Why a hook and not a refusal to install.</b> Velopack's Setup reads
/// <c>--installto</c> and offers no way to switch it off, and a hook that fails does not
/// stop or undo an install, measured at 1.2.161
/// (<c>kb/packaging/velopack.md</c>, the 2026-10-10 entry). What a hook can do is set
/// nothing up.
/// </para>
/// <para>
/// <b>The comparison is the session lock's own identity chain</b>
/// (<see cref="RootKey.Same"/>), so a spelling, a case, an alias or an 8.3 name of the
/// standard folder is the standard folder, with the full path compared as text when no
/// key can be made of either.
/// </para>
/// </remarks>
internal static class StandardLocation
{
    /// <summary>The shipping pack's id, as the release packs it.</summary>
    public const string ShippingPackId = "BrowserAI.app";

    /// <summary>The folder under the user's LocalAppData the shipping install is in.</summary>
    public const string InstallFolderName = "BrowserAI.app";

    /// <summary>What an install hook that refuses exits with, which Setup records as the hook's failure.</summary>
    /// <remarks>
    /// Any value but zero reads the same to Setup, which logs it, shows its own box after a
    /// non-silent install and exits 0 either way; 5 is the code the 2026-10-10 measurement
    /// used.
    /// </remarks>
    public const int RefusedExitCode = 5;

    /// <summary>What puts a copy outside the standard folder right, in the maintainer's words.</summary>
    public const string Remedy = "Uninstall this copy, then run BrowserAI.exe again without --installto.";

    /// <summary>The title of the box a person meets at a refused copy's start.</summary>
    public const string NoticeTitle = "BrowserAI was not set up";

    /// <summary>The standard install root of this Windows user: <c>%LOCALAPPDATA%\BrowserAI.app</c>.</summary>
    public static string InstallRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        InstallFolderName);

    /// <summary>Whether a pack id is the shipping pack's.</summary>
    /// <param name="packId">The pack id, or <see langword="null"/> for a build that is not installed.</param>
    /// <returns>Whether it is <see cref="ShippingPackId"/>.</returns>
    public static bool IsShipping(string? packId) =>
        string.Equals(packId, ShippingPackId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The data root a start may be handed: none for the shipping install, which keeps its
    /// data in <c>%LOCALAPPDATA%\BrowserAI</c> whatever it is handed.
    /// </summary>
    /// <param name="packId">The pack id this process was installed from, or <see langword="null"/>.</param>
    /// <param name="handed">The data root the start was handed, or <see langword="null"/>.</param>
    /// <returns>The data root to use, or <see langword="null"/> for the default.</returns>
    public static string? DataRootFor(string? packId, string? handed) =>
        IsShipping(packId) ? null : handed;

    /// <summary>Whether a copy may be set up and may start where it is.</summary>
    /// <param name="packId">The pack id the copy was installed from, or <see langword="null"/>.</param>
    /// <param name="installRoot">Its install root, or <see langword="null"/> for a build that is not installed.</param>
    /// <returns>The refusal, or <see langword="null"/> when it may.</returns>
    public static StandardLocationRefusal? Judge(string? packId, string? installRoot) =>
        Judge(packId, installRoot, InstallRoot);

    /// <summary>The same judgement against a standard folder the caller names: the suite's seam.</summary>
    /// <param name="packId">The pack id the copy was installed from, or <see langword="null"/>.</param>
    /// <param name="installRoot">Its install root, or <see langword="null"/>.</param>
    /// <param name="standardRoot">The folder that counts as the standard one.</param>
    /// <returns>The refusal, or <see langword="null"/> when it may.</returns>
    internal static StandardLocationRefusal? Judge(string? packId, string? installRoot, string standardRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(standardRoot);

        if (!IsShipping(packId) || installRoot is not { Length: > 0 })
        {
            return null;
        }

        return IsTheStandardFolder(installRoot, standardRoot) ? null : new StandardLocationRefusal(installRoot, standardRoot);
    }

    private static bool IsTheStandardFolder(string installRoot, string standardRoot) =>
        RootKey.Same(installRoot, standardRoot)
        || string.Equals(Comparable(installRoot), Comparable(standardRoot), StringComparison.OrdinalIgnoreCase);

    private static string Comparable(string root)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        }
        catch (Exception failure) when (failure is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return root;
        }
    }
}

/// <summary>A shipping copy outside the standard folder: where it is, where it belongs, and what it says.</summary>
/// <param name="InstallRoot">Where the copy is.</param>
/// <param name="StandardRoot">The one folder the shipping install goes into.</param>
internal sealed record StandardLocationRefusal(string InstallRoot, string StandardRoot)
{
    /// <summary>The whole sentence: the log's, the installer's log's and the box's.</summary>
    public string Sentence =>
        $"This copy of BrowserAI is in '{InstallRoot}', and BrowserAI installs only into '{StandardRoot}', so that a project's registration finds it on every developer's PC. Nothing was set up, and this copy does not start. {StandardLocation.Remedy}";

    /// <summary>The refusal in the parts a relay answers each call with.</summary>
    /// <returns>The parts, the remedy as a clause that starts with what to do.</returns>
    public RootRefusal AsRootRefusal() =>
        new(
            JudgedRoot.Install,
            InstallRoot,
            $"BrowserAI installs only into '{StandardRoot}', so that a project's registration finds it on every developer's PC, and nothing was set up for this copy",
            "uninstall this copy, then run BrowserAI.exe again without --installto.");
}
