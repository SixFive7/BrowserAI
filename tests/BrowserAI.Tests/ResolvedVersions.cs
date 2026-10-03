// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;

namespace BrowserAI.Tests;

/// <summary>
/// What the build actually resolved, read from the committed records of it.
/// </summary>
/// <remarks>
/// <para>
/// Every version in this project floats, and the resolved set is recorded
/// and not declared. There are four records and they are written by four
/// different steps: <c>build/payload/package-lock.json</c> by the payload
/// build, <c>packages.lock.json</c> by NuGet restore,
/// <c>upstream-snapshots/tools-list.json</c> by the snapshot generator, and
/// <c>build/payload/registerai.json</c> by <c>build/Get-RegisterAi.ps1</c>.
/// <i>Corrected 2026-10-03 (previously "There are three records and they are
/// written by three different steps"), when RegisterAI joined the payload.</i>
/// </para>
/// <para>
/// All three are committed on purpose, which is what lets the marker test run
/// on a clean clone with no payload assembled. Reading the assembled payload
/// instead would make the gate silently inert exactly when nobody has built
/// one.
/// </para>
/// </remarks>
internal static class ResolvedVersions
{
    /// <summary>The npm half: what the last payload build resolved.</summary>
    public static string? FromPayloadLock(string package)
    {
        using var lockFile = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryLayout.Root.FullName, "build", "payload", "package-lock.json")));

        return lockFile.RootElement.GetProperty("packages").TryGetProperty($"node_modules/{package}", out var entry)
            ? entry.GetProperty("version").GetString()
            : null;
    }

    /// <summary>
    /// The NuGet half. Returns <see langword="null"/> when no project
    /// references the package yet, which is a real state and not a failure:
    /// the marker test names which upstreams are in that state, so that adding
    /// one to the build without wiring it here turns the suite red.
    /// </summary>
    public static string? FromNuGetLocks(string package)
    {
        foreach (var project in RepositoryLayout.ProjectFiles)
        {
            var path = Path.Combine(project.DirectoryName!, "packages.lock.json");
            if (!File.Exists(path))
            {
                continue;
            }

            using var lockFile = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var framework in lockFile.RootElement.GetProperty("dependencies").EnumerateObject())
            {
                foreach (var dependency in framework.Value.EnumerateObject())
                {
                    if (string.Equals(dependency.Name, package, StringComparison.OrdinalIgnoreCase)
                        && dependency.Value.TryGetProperty("resolved", out var resolved))
                    {
                        return resolved.GetString();
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The RegisterAI half: the release the last payload build took, from the
    /// stamp <c>build/Get-RegisterAi.ps1</c> writes beside the npm lock. Null
    /// when the stamp names no version.
    /// </summary>
    public static string? FromRegisterAiStamp()
    {
        using var stamp = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryLayout.Root.FullName, "build", "payload", "registerai.json")));

        return stamp.RootElement.TryGetProperty("version", out var version) ? version.GetString() : null;
    }

    /// <summary>
    /// The runtime half: the node the snapshots were generated under, which
    /// the generator refuses to run without being the payload's own.
    /// </summary>
    public static string? FromSnapshotProvenance(string key)
    {
        using var snapshot = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryLayout.Root.FullName, "upstream-snapshots", "tools-list.json")));

        return snapshot.RootElement.GetProperty("resolvedFrom").TryGetProperty(key, out var value)
            ? value.GetString()
            : null;
    }
}
