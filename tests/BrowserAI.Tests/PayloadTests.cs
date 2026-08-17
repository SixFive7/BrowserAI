// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;
using BrowserAI.Tests.Harness;

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
/// The two declaration tests read the tracked files under
/// <c>build/payload/</c>, so they run on a clean clone with no payload built.
/// The two content tests read the assembled tree under <c>payload/</c> and go
/// through <see cref="SuiteEnvironment"/>'s gate for it, because a scan of a
/// tree that is not there passes trivially and reads identically to a scan that
/// found nothing — which is the whole failure this suite's capability gate
/// exists to make loud. <i>(Corrected 2026-08-17, previously "These read the
/// tracked files under <c>build/payload/</c>, never the assembled tree under
/// <c>payload/</c>".)</i>
/// </para>
/// </remarks>
internal sealed class PayloadTests
{
    private static readonly string[] ExpectedDependencies = ["@playwright/mcp"];

    /// <summary>
    /// What a platform-native binary looks like on any platform, not only this
    /// one — a tree that is portable is portable everywhere or it is not
    /// portable.
    /// </summary>
    private static readonly HashSet<string> NativeExtensions =
        new([".node", ".dll", ".exe", ".so", ".dylib", ".a", ".lib", ".pdb"], StringComparer.OrdinalIgnoreCase);

    private static readonly string[] TheOnePortableBinary =
        ["node_modules/playwright-core/lib/webp_codec.wasm"];

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

    [Test]
    public async Task TheVendoredTreeCarriesNoPlatformNativeBinary()
    {
        // The one claim in the payload table that nothing checked. "Zero native
        // binaries; the tree is portable JS" is what makes the JS half of the
        // payload a per-file delta of text rather than an architecture-specific
        // artifact -- and it is upstream's property, not ours, so it can be
        // undone by a dependency upstream adds without a word to us. A `.node`
        // arriving in `mcp\` would also cross the batteries-included boundary:
        // it would need a toolchain the installer does not carry.
        //
        // `node\` is deliberately outside the scan. `node.exe` is the native
        // binary the payload exists to ship.
        SuiteEnvironment.RequireRepositoryPayload();

        var mcp = new DirectoryInfo(Path.Combine(RepositoryPayload.Layout.Root, "mcp"));

        var native = mcp.EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(file => NativeExtensions.Contains(file.Extension))
            .Select(file => Path.GetRelativePath(mcp.FullName, file.FullName))
            .Order(StringComparer.Ordinal)
            .ToList();

        await Assert.That(string.Join(", ", native)).IsEmpty();
    }

    [Test]
    public async Task TheOneNonJavaScriptArtefactInTheTreeIsPortableAndIsNamed()
    {
        // Measured 2026-08-17 @ @playwright/mcp 0.0.79 / playwright-core
        // 1.63.0-alpha-2026-08-05: the tree is not JS alone. It carries exactly
        // one `.wasm`, and a `.wasm` is portable bytecode rather than a native
        // binary -- so the premise survives and the wording above it did not.
        // Named rather than allowed by extension, because the interesting event
        // is a *second* one arriving: upstream adding a WASM codec is how a
        // "portable JS tree" acquires a component nobody reviewed.
        SuiteEnvironment.RequireRepositoryPayload();

        var mcp = new DirectoryInfo(Path.Combine(RepositoryPayload.Layout.Root, "mcp"));

        var portable = mcp.EnumerateFiles("*.wasm", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(mcp.FullName, file.FullName).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        await Assert.That(portable).IsEquivalentTo(TheOnePortableBinary);
    }

    private static JsonDocument ReadJson(string name) =>
        JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryLayout.Root.FullName, "build", "payload", name)));
}
