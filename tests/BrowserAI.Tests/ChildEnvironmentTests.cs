// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Protocol;

namespace BrowserAI.Tests;

/// <summary>
/// The shape of the child's environment allowlist, asserted without starting a
/// process.
/// </summary>
/// <remarks>
/// The end-to-end half — that a child really is handed this and nothing else —
/// is in <see cref="DirectStdioClientTransportTests"/>. This half is what makes
/// the list itself reviewable: a name moved from <c>Refused</c> into
/// <c>InheritedWhenSet</c> is a deliberate edit that fails here, rather than a
/// behaviour change nobody notices until a download starts failing against one
/// mirror.
/// </remarks>
internal sealed class ChildEnvironmentTests
{
    private static readonly string[] TheTwoForcedNames =
    [
        "PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD",
        "PLAYWRIGHT_SKIP_BROWSER_GC",
    ];

    private static readonly string[] EveryRefusedName =
    [
        "DEBUG",
        "DEBUG_FILE",
        "INIT_CWD",
        "NODE_OPTIONS",
        "NODE_PATH",
        "PLAYWRIGHT_CHROMIUM_DOWNLOAD_HOST",
        "PLAYWRIGHT_DOWNLOAD_HOST",
        "PLAYWRIGHT_FIREFOX_DOWNLOAD_HOST",
        "PLAYWRIGHT_MCP_OUTPUT_MAX_SIZE",
        "PLAYWRIGHT_SKIP_VALIDATE_HOST_REQUIREMENTS",
        "PLAYWRIGHT_WEBKIT_DOWNLOAD_HOST",
    ];

    [Test]
    public async Task TheRefusedNamesAreTheOnesTheDesignNames()
    {
        // Spelled out rather than counted. Every one of these is a documented
        // silent failure -- a collapsed download mirror list, an evicted output
        // file, a browsers path resolved against an npm ancestor's directory,
        // or a line on stderr that trips the error classifier merely by the
        // variable being set.
        await Assert.That(ChildEnvironment.Refused.Order(StringComparer.Ordinal)).IsEquivalentTo(EveryRefusedName);
    }

    [Test]
    public async Task NoRefusedNameIsAlsoAllowed()
    {
        var contradictions = ChildEnvironment.Refused
            .Where(name => ChildEnvironment.InheritedWhenSet.Contains(name) || ChildEnvironment.Forced.ContainsKey(name))
            .ToList();

        await Assert.That(string.Join(", ", contradictions)).IsEmpty();
    }

    [Test]
    public async Task TheForcedVariablesAreTheTwoThatMustNotDependOnTheHost()
    {
        await Assert.That(ChildEnvironment.Forced.Keys.Order(StringComparer.Ordinal))
            .IsEquivalentTo(TheTwoForcedNames);

        await Assert.That(ChildEnvironment.Forced["PLAYWRIGHT_SKIP_BROWSER_GC"]).IsEqualTo("1");
        await Assert.That(ChildEnvironment.Forced["PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD"]).IsEqualTo("1");
    }

    [Test]
    public async Task ARefusedVariableCannotBeAddedByACaller()
    {
        // The escape hatch a later step reaches for -- "just pass NODE_OPTIONS
        // through for this one child" -- is closed here rather than by review.
        var thrown = Assert.Throws<ArgumentException>(
            () => ChildEnvironment.Build([new KeyValuePair<string, string>("NODE_OPTIONS", "--max-old-space-size=4096")]));

        await Assert.That(thrown!.Message).Contains("NODE_OPTIONS");
    }

    [Test]
    public async Task ARefusedVariableCannotBeAddedUnderADifferentCasing()
    {
        // Windows environment blocks are case-insensitive. An ordinal set here
        // would let `Init_Cwd` through as a different name and the child would
        // read it as INIT_CWD.
        _ = Assert.Throws<ArgumentException>(
            () => ChildEnvironment.Build([new KeyValuePair<string, string>("init_cwd", @"C:\somewhere")]));

        await Assert.That(ChildEnvironment.Refused.Contains("Playwright_Download_Host")).IsTrue();
    }

    [Test]
    public async Task TheBuiltBlockCarriesTheForcedValuesAndTheCallersOwn()
    {
        var built = ChildEnvironment.Build([new KeyValuePair<string, string>("PLAYWRIGHT_BROWSERS_PATH", @"C:\browsers")]);

        await Assert.That(built["PLAYWRIGHT_SKIP_BROWSER_GC"]).IsEqualTo("1");
        await Assert.That(built["PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD"]).IsEqualTo("1");
        await Assert.That(built["PLAYWRIGHT_BROWSERS_PATH"]).IsEqualTo(@"C:\browsers");
    }

    [Test]
    public async Task EveryNameInABuiltBlockIsOneTheAllowlistNames()
    {
        var built = ChildEnvironment.Build();

        var unexpected = built.Keys
            .Where(name => !ChildEnvironment.InheritedWhenSet.Contains(name) && !ChildEnvironment.Forced.ContainsKey(name))
            .ToList();

        await Assert.That(string.Join(", ", unexpected)).IsEmpty();
    }
}
