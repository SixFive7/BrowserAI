// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;

namespace BrowserAI.Tests;

/// <summary>
/// Asserts the shape of the vendored payload's declared dependencies.
/// </summary>
/// <remarks>
/// <para>
/// The npm half of the float. <c>Directory.Packages.props</c> is guarded by
/// <see cref="BuildConfigurationTests"/>; this guards the one other place a
/// version can be declared, and the failure it prevents is the same one:
/// a payload that stopped following upstream while every surface signal — a
/// green build, a committed lock, a passing suite — reads healthy.
/// </para>
/// <para>
/// These read the tracked files under <c>build/payload/</c>, never the
/// assembled tree under <c>payload/</c>, so they run on a clean clone with no
/// payload built. What the assembled tree must satisfy is checked by
/// <c>build/Build-Payload.ps1</c> as it builds it.
/// </para>
/// </remarks>
internal sealed class PayloadTests
{
    private static readonly string[] ExpectedDependencies = ["@playwright/mcp"];

    [Test]
    public async Task ThePayloadDeclaresOneDependencyAndItIsADistTag()
    {
        using var manifest = ReadJson("package.json");

        var dependencies = manifest.RootElement.GetProperty("dependencies")
            .EnumerateObject()
            .Select(property => (property.Name, Value: property.Value.GetString()))
            .ToList();

        // playwright-core is the one that matters. It arrives as
        // @playwright/mcp's own exact dependency, and upstream publishes daily
        // alphas alongside a lagging `latest`: on 2026-08-15 npm `latest` was
        // 1.62.1 while the shipping version was 1.63.0-alpha-2026-08-05.
        // Declaring it here would resolve the wrong one, and the tree would
        // still install.
        await Assert.That(dependencies.Select(dependency => dependency.Name))
            .IsEquivalentTo(ExpectedDependencies);

        // `latest` is the dist-tag, so the payload build re-resolves on every
        // run. A range — `^0.0.79` — would look equally floating and would pin
        // the major forever, which for a 0.0.x package pins everything.
        await Assert.That(dependencies[0].Value).IsEqualTo("latest");
    }

    [Test]
    public async Task TheLockRecordsUpstreamsOwnExactPinOfPlaywrightCore()
    {
        using var lockFile = ReadJson("package-lock.json");

        var packages = lockFile.RootElement.GetProperty("packages");
        var resolved = packages.GetProperty("node_modules/playwright-core").GetProperty("version").GetString();
        var declared = packages.GetProperty("node_modules/@playwright/mcp")
            .GetProperty("dependencies")
            .GetProperty("playwright-core")
            .GetString();

        // An exact version, byte for byte, not a range that happens to match.
        // If upstream ever loosened this pin the payload would start floating
        // on a second axis, and the lock alone would not say so.
        await Assert.That(declared).IsEqualTo(resolved);
        await Assert.That(declared).DoesNotContain("^");
        await Assert.That(declared).DoesNotContain("~");
    }

    private static JsonDocument ReadJson(string name) =>
        JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryLayout.Root.FullName, "build", "payload", name)));
}
