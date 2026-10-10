// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The suite's probe copied into a directory an arm chooses, with everything it needs to
/// start there, so a process of the probe's can have its image under a root the product
/// verifies.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-10-10</b>, for a person's start that ends a hung background only once its
/// image is verified to lie under the install root. <c>PlantedProcess</c> plants
/// <c>cmd.exe</c> instead, because a lone copy of the probe's <c>.exe</c> is a
/// framework-dependent apphost that cannot find its <c>.dll</c> and dies at once; this
/// copies the whole of what the probe's own <c>.deps.json</c> names, which is what the
/// host loads it from.
/// </para>
/// <para>
/// <b>Read from the manifest and never from a list written here</b>, so a reference the
/// probe gains is copied without anybody remembering to add it: every runtime asset, and
/// the native assets for this machine's own runtime identifiers.
/// </para>
/// </remarks>
internal static class ProbeImage
{
    /// <summary>The probe's name, beside the test host.</summary>
    public const string Name = "BrowserAI.TestProbe";

    /// <summary>Copies the probe into a directory and returns its executable there.</summary>
    /// <param name="directory">Where to copy it. Created if absent.</param>
    /// <returns>The copy's executable.</returns>
    public static string CopyInto(string directory)
    {
        var source = AppContext.BaseDirectory;

        _ = Directory.CreateDirectory(directory);

        foreach (var file in new[] { Name + ".exe", Name + ".dll", Name + ".runtimeconfig.json", Name + ".deps.json" })
        {
            File.Copy(Path.Combine(source, file), Path.Combine(directory, file), overwrite: true);
        }

        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(source, Name + ".deps.json")));

        foreach (var target in manifest.RootElement.GetProperty("targets").EnumerateObject())
        {
            foreach (var library in target.Value.EnumerateObject())
            {
                if (library.Value.TryGetProperty("runtime", out var runtime))
                {
                    foreach (var asset in runtime.EnumerateObject())
                    {
                        var leaf = Path.GetFileName(asset.Name);
                        File.Copy(Path.Combine(source, leaf), Path.Combine(directory, leaf), overwrite: true);
                    }
                }

                if (library.Value.TryGetProperty("runtimeTargets", out var native))
                {
                    foreach (var asset in native.EnumerateObject())
                    {
                        var relative = asset.Name.Replace('/', Path.DirectorySeparatorChar);

                        if (!relative.StartsWith(Path.Combine("runtimes", "win"), StringComparison.OrdinalIgnoreCase)
                            || !File.Exists(Path.Combine(source, relative)))
                        {
                            continue;
                        }

                        var copy = Path.Combine(directory, relative);
                        _ = Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                        File.Copy(Path.Combine(source, relative), copy, overwrite: true);
                    }
                }
            }
        }

        return Path.Combine(directory, Name + ".exe");
    }
}
