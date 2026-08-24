// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;
using BrowserAI.Hosting;
using BrowserAI.Runtime;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The real paths the product resolves at runtime, and the browser revision it
/// expects to find under them.
/// </summary>
/// <remarks>
/// <para>
/// Resolved through the product's own <see cref="LocalAppDataPaths"/> rather
/// than rebuilt from <c>%LOCALAPPDATA%</c> here, so the shape of the root and
/// every directory name beneath it come from the product and never from a
/// literal spelled in the suite.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-24 (previously "so a test asserting <i>the browser
/// is ours</i> is asserting against the directory the product would actually
/// have used").</b> That was false whenever <see cref="AppRootOverride"/> is
/// set, which is a case this suite creates on purpose:
/// <c>Program.Main</c> reads <see cref="AppRootOverride"/> and takes it over
/// the computed default, and
/// <see cref="PublishedSlice.InheritedEnvironment"/> copies the whole
/// environment — that variable included — into the published child. This type
/// constructs <see cref="LocalAppDataPaths"/> with <b>no</b> root argument, so
/// it always answers the per-user default under
/// <c>%LOCALAPPDATA%</c>. The two disagree exactly when the override is set,
/// and the members below are the default rather than "what the product used".
/// </para>
/// <para>
/// <b>Making it honour the override was considered and deliberately not
/// taken.</b> The override exists to hand a real BrowserAI an <i>empty</i>
/// browsers root without deleting the developer's own, and the assertions that
/// read these members are about the real provisioned tree that empty root is
/// standing in for. Reading the variable here would re-point them at the rig
/// the override was set up to create, which is the opposite of what they ask.
/// The comment is what changed; the resolution is not.
/// </para>
/// </remarks>
internal static class BrowserAiPaths
{
    private static readonly LocalAppDataPaths Paths = new();

    /// <summary>The browsers root, absolute.</summary>
    public static string BrowsersDirectory => Paths.BrowsersDirectory;

    /// <summary>
    /// The variable that moves a published binary's whole app root, named from
    /// the product rather than typed here.
    /// </summary>
    /// <remarks>
    /// The only way to give a real BrowserAI an <b>empty</b> browsers root
    /// without deleting the developer's own — which would destroy 430 MiB and
    /// break every other browser test running beside it.
    /// </remarks>
    public static string AppRootOverride => Program.AppRootVariable;

    /// <summary>
    /// Every path the product resolves under the default app root — the same
    /// object <see cref="BrowsersDirectory"/> comes from.
    /// </summary>
    /// <remarks>
    /// <b>One route rather than a <c>new LocalAppDataPaths()</c> at each call
    /// site</b>, so the day the override above is honoured it is honoured
    /// everywhere at once instead of in the places somebody remembered.
    /// </remarks>
    public static IAppPaths Real => Paths;

    /// <summary>
    /// The chromium revision the committed snapshot names, so nothing in the
    /// suite spells a directory with a literal.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Declared before everything that uses it, and that ordering is
    /// load-bearing.</b> Static field initialisers run in declaration order, so
    /// with this below <see cref="ExpectedChromiumExecutable"/> the path composed
    /// there is <c>chromium-</c> with no revision at all — which reads as a
    /// browser that resolved to the wrong place. Caught by the suite on
    /// 2026-08-16, not by review.
    /// </remarks>
    public static string ChromiumRevision { get; } = RevisionOf("chromium");

    /// <summary>The firefox revision the committed snapshot names.</summary>
    /// <remarks>
    /// ⚠️ <b>Corrected 2026-08-19 (previously "Firefox is not a browser BrowserAI
    /// creates sessions for — that is still owed, and carried in TODO.md").</b>
    /// It is one now: <c>browserai_init</c> accepts <c>browser: "firefox"</c>,
    /// and <c>FirefoxSessionTests</c> drives one through the front door. The
    /// revision was already needed before that, because the
    /// [job-object containment contract](../../../kb/windows/job-objects.md)
    /// is stated against <b>both</b> families, and the
    /// second one is the harder case: Firefox stacks a second permissive job of
    /// its own, and its background tasks are the only code in either browser that
    /// asks to break away.
    /// </remarks>
    public static string FirefoxRevision { get; } = RevisionOf("firefox");

    /// <summary>
    /// The revision directory a provisioned Chromium lives in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Named so that "nobody has provisioned it" is distinguishable from "it
    /// was provisioned and the executable is missing".</b> The second is a real
    /// defect and reads as a clean machine without it —
    /// <see cref="SuiteEnvironment.StateOf"/> answers
    /// <see cref="CapabilityState.Partial"/> for that shape, which fails in
    /// every run rather than skipping. The distinction used to be drawn inside
    /// the one test that needed it, which is why it was lost when that test's
    /// degraded branch reported <i>passed</i>.
    /// </para>
    /// <para>
    /// ⚠️ <b>Above <see cref="ExpectedChromiumExecutable"/> on purpose</b>, for
    /// the reason stated on <see cref="ChromiumRevision"/>: an initialiser that
    /// reads a later one gets <see langword="null"/>, silently.
    /// </para>
    /// </remarks>
    public static string ChromiumDirectory { get; } = Path.Combine(
        Paths.BrowsersDirectory,
        $"chromium-{ChromiumRevision}");

    /// <summary>
    /// The exact Chromium executable the resolved payload's <c>browsers.json</c>
    /// asks for.
    /// </summary>
    /// <remarks>
    /// <b>The revision comes from the committed snapshot, never from a literal.</b>
    /// It moves with every upstream bump, and a hard-coded <c>chromium-1237</c>
    /// here would start passing against whatever happened to be on disk.
    /// Note the asymmetry the path carries: the outer directory uses an
    /// underscore before the revision and the inner one uses a dash.
    /// </remarks>
    public static string ExpectedChromiumExecutable { get; } = Path.Combine(
        ChromiumDirectory,
        "chrome-win64",
        "chrome.exe");

    /// <summary>
    /// The headless shell's directory, which must never be consulted: it is
    /// deliberately not provisioned.
    /// </summary>
    public static string HeadlessShellDirectory { get; } = Path.Combine(
        Paths.BrowsersDirectory,
        $"chromium_headless_shell-{ChromiumRevision}");

    /// <summary>Where a provisioned Firefox lands, and the executable inside it.</summary>
    public static string FirefoxDirectory { get; } = Path.Combine(
        Paths.BrowsersDirectory,
        $"firefox-{FirefoxRevision}");

    /// <summary>The Firefox executable inside that directory.</summary>
    /// <remarks>
    /// Note the layout differs from Chromium's: the inner directory is plain
    /// <c>firefox</c> rather than a platform-suffixed one, which is also why
    /// upstream's <c>winldd</c> dependency validation actually runs for Firefox
    /// and is a permanent no-op for Chromium.
    /// </remarks>
    public static string FirefoxExecutable { get; } = Path.Combine(FirefoxDirectory, "firefox", "firefox.exe");

    /// <summary>
    /// Where each shared component's tree lands under a browsers root, in the
    /// product's own order.
    /// </summary>
    /// <remarks>
    /// <b>Spelled from the committed snapshot, exactly as every other directory
    /// here is</b>, so a revision bump moves it. Needed by a test whose installer
    /// double is constructed <i>before</i> the rig exists and therefore cannot
    /// ask the rig's provisioner for the same answer;
    /// <c>BrowserProvisioner.SharedComponentDirectories</c> is the product's own
    /// route and is what the assertions read.
    /// </remarks>
    /// <param name="browsersRoot">The browsers root, absolute.</param>
    /// <returns>The absolute directories.</returns>
    public static IReadOnlyList<string> SharedComponentDirectoriesIn(string browsersRoot) =>
        [.. ProvisionedBrowsers.SharedComponents.Select(component => Path.Combine(browsersRoot, $"{component}-{RevisionOf(component)}"))];

    private static string RevisionOf(string browser)
    {
        using var snapshot = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryLayout.Root.FullName, "upstream-snapshots", "browsers.json")));

        foreach (var entry in snapshot.RootElement.GetProperty("browsers").EnumerateArray())
        {
            if (entry.GetProperty("name").GetString() == browser)
            {
                return entry.GetProperty("revision").GetString()!;
            }
        }

        throw new InvalidOperationException($"The committed browsers.json snapshot carries no '{browser}' entry.");
    }
}
