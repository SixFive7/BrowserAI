// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;
using BrowserAI.Hosting;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The real paths the product resolves at runtime, and the browser revision it
/// expects to find under them.
/// </summary>
/// <remarks>
/// Resolved through the product's own <see cref="LocalAppDataPaths"/> rather
/// than rebuilt from <c>%LOCALAPPDATA%</c> here, so a test asserting "the
/// browser is <i>ours</i>" is asserting against the directory the product would
/// actually have used.
/// </remarks>
internal static class BrowserAiPaths
{
    private static readonly LocalAppDataPaths Paths = new();

    /// <summary>The browsers root, absolute.</summary>
    public static string BrowsersDirectory => Paths.BrowsersDirectory;

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
        Paths.BrowsersDirectory,
        $"chromium-{ChromiumRevision()}",
        "chrome-win64",
        "chrome.exe");

    /// <summary>
    /// The headless shell's directory, which must never be consulted: it is
    /// deliberately not provisioned.
    /// </summary>
    public static string HeadlessShellDirectory { get; } = Path.Combine(
        Paths.BrowsersDirectory,
        $"chromium_headless_shell-{ChromiumRevision()}");

    private static string ChromiumRevision()
    {
        using var snapshot = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryLayout.Root.FullName, "upstream-snapshots", "browsers.json")));

        foreach (var browser in snapshot.RootElement.GetProperty("browsers").EnumerateArray())
        {
            if (browser.GetProperty("name").GetString() is "chromium")
            {
                return browser.GetProperty("revision").GetString()!;
            }
        }

        throw new InvalidOperationException("The committed browsers.json snapshot carries no 'chromium' entry.");
    }
}
