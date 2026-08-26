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

    /// <summary>
    /// The five names a machine behind a corporate proxy or TLS inspection
    /// cannot provision a browser without.
    /// </summary>
    private static readonly string[] TheEgressNames =
    [
        "ALL_PROXY",
        "HTTPS_PROXY",
        "HTTP_PROXY",
        "NODE_EXTRA_CA_CERTS",
        "NO_PROXY",
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
        "PLAYWRIGHT_MCP_ALLOW_UNRESTRICTED_FILE_ACCESS",
        "PLAYWRIGHT_MCP_CAPS",
        "PLAYWRIGHT_MCP_CONFIG",
        "PLAYWRIGHT_MCP_INIT_PAGE",
        "PLAYWRIGHT_MCP_INIT_SCRIPT",
        "PLAYWRIGHT_MCP_OUTPUT_DIR",
        "PLAYWRIGHT_MCP_OUTPUT_MAX_SIZE",
        "PLAYWRIGHT_SKIP_VALIDATE_HOST_REQUIREMENTS",
        "PLAYWRIGHT_WEBKIT_DOWNLOAD_HOST",
    ];

    /// <summary>
    /// The one variable that would switch off the only containment this product
    /// has left, and the four beside it that redirect the child's config, its
    /// output root or the scripts it runs.
    /// </summary>
    /// <remarks>
    /// <b>Read out of the shipped bundle rather than from a changelog</b>:
    /// <c>playwright-core</c>'s <c>configFromEnv</c> maps every one of these onto
    /// a config key, and the merge order is config file → environment → CLI, so
    /// an inherited value wins over the key BrowserAI generates.
    /// </remarks>
    private static readonly string[] TheFiveThatOverrideAGeneratedKey =
    [
        "PLAYWRIGHT_MCP_ALLOW_UNRESTRICTED_FILE_ACCESS",
        "PLAYWRIGHT_MCP_CONFIG",
        "PLAYWRIGHT_MCP_INIT_PAGE",
        "PLAYWRIGHT_MCP_INIT_SCRIPT",
        "PLAYWRIGHT_MCP_OUTPUT_DIR",
    ];

    /// <summary>
    /// The five names that override a key the generator writes are refused by
    /// name rather than merely absent.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>None of these was a hole and none of them is one now — the finding
    /// is against the list's own stated purpose (2026-08-26).</b> The allowlist
    /// is the child's entire block by construction, so all five were already
    /// absent; what <c>Refused</c> exists for, in its own words, is to turn
    /// <i>absent because nobody added it</i> into <i>absent because it is
    /// refused</i>. <c>PLAYWRIGHT_MCP_ALLOW_UNRESTRICTED_FILE_ACCESS</c> is the
    /// one that makes the omission worth a test: it switches off
    /// <c>allowUnrestrictedFileAccess: false</c>, which
    /// <c>BrowserConfiguration</c> calls the only containment this product has
    /// left, and it was not named on the day that key became load-bearing.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheVariablesThatWouldOverrideAGeneratedConfigKeyAreRefusedByName()
    {
        foreach (var name in TheFiveThatOverrideAGeneratedKey)
        {
            await Assert.That(ChildEnvironment.Refused.Contains(name)).IsTrue();

            // And refused for a caller too, not only absent from the inherited
            // set -- which is the difference between a property and an accident.
            _ = Assert.Throws<ArgumentException>(
                () => ChildEnvironment.Build([new KeyValuePair<string, string>(name, "1")]));
        }
    }

    [Test]
    public async Task TheRefusedNamesAreTheOnesTheDesignNames()
    {
        // Spelled out rather than counted. Every one of these is a documented
        // silent failure -- a collapsed download mirror list, an evicted output
        // file, a browsers path resolved against an npm ancestor's directory,
        // a wiped capability list, or a line on stderr that trips the error
        // classifier merely by the variable being set.
        await Assert.That(ChildEnvironment.Refused.Order(StringComparer.Ordinal)).IsEquivalentTo(EveryRefusedName);
    }

    [Test]
    public async Task TheProxyAndCertificateNamesTheDownloadPathNeedsSurviveIntoAChildsBlock()
    {
        // The one half of the allowlist whose absence is invisible on the
        // machine that writes it. Deleting NODE_EXTRA_CA_CERTS costs nothing
        // here and nothing in CI, and costs everything on a laptop behind TLS
        // inspection: the 203.8 MB provision fails on a certificate the host
        // was configured to trust and BrowserAI never asked for. Every other
        // name in the list is asserted by shape -- refused, forced, or covered
        // by the closed-world check below -- and these five were covered by
        // nothing until 2026-08-17.
        //
        // Asserted through Build() rather than against the set, because the
        // set is only half the mechanism: an inherited name reaches a child
        // only if this process has it, so the test seeds each one first.
        foreach (var name in TheEgressNames)
        {
            await Assert.That(ChildEnvironment.InheritedWhenSet.Contains(name)).IsTrue();
            await Assert.That(ChildEnvironment.Refused.Contains(name)).IsFalse();
        }

        var seeded = TheEgressNames
            .Select(name => new KeyValuePair<string, string>(name, $"value-of-{name}"))
            .ToList();

        var built = ChildEnvironment.Build(seeded);

        foreach (var name in TheEgressNames)
        {
            await Assert.That(built[name]).IsEqualTo($"value-of-{name}");
        }
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
