// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The published NativeAOT binary, and the payload beside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The slice is driven from the published binary rather than from
/// <c>dotnet run</c>, and that is the point of the step it belongs to.</b> The
/// decisions under test — <c>PROC_THREAD_ATTRIBUTE_JOB_LIST</c> under
/// <c>[LibraryImport]</c>, the SDK's serialization under ILC, a
/// <c>JsonSerializerContext</c>-free JSON path — are all things that behave
/// identically under the JIT and can only fail after native compilation.
/// </para>
/// <para>
/// <b>A stale publish is refused rather than tested.</b> Nothing rebuilds it
/// automatically: ILC costs about a minute and a test that silently ran last
/// week's binary would report a green suite for code that was never compiled.
/// <see cref="EnsureFresh"/> compares its timestamp against every source file
/// that goes into it and fails with the command to run.
/// </para>
/// </remarks>
internal static class PublishedSlice
{
    /// <summary>Where <c>dotnet publish -r win-x64 --self-contained</c> puts it.</summary>
    public static string Directory { get; } = Path.Combine(
        RepositoryLayout.Root.FullName,
        "src", "BrowserAI", "bin", "Release", "net10.0-windows", "win-x64", "publish");

    /// <summary>The published binary.</summary>
    public static string Executable { get; } = Path.Combine(Directory, "BrowserAI.exe");

    /// <summary>The payload that must sit beside it for a child to start.</summary>
    public static string PayloadMarker { get; } = Path.Combine(Directory, "payload", "payload.json");

    /// <summary>The command that produces both.</summary>
    public const string PublishCommand =
        "dotnet publish src/BrowserAI/BrowserAI.csproj -c Release -r win-x64 --self-contained";

    /// <summary>Whether a published binary with a payload beside it exists.</summary>
    public static bool IsPresent => File.Exists(Executable) && File.Exists(PayloadMarker);

    /// <summary>
    /// Whether the publish directory is absent <b>as a whole</b>, which is what
    /// a clean clone looks like.
    /// </summary>
    /// <remarks>
    /// Asserted rather than <see cref="IsPresent"/>'s negation, so that "nobody
    /// has published" is distinguishable from "the publish ran and the binary or
    /// the payload is missing from it". The second is a real defect and would
    /// otherwise read as a clean clone.
    /// </remarks>
    public static bool IsAbsentAsAWhole => !System.IO.Directory.Exists(Directory);

    /// <summary>
    /// Every file that goes into the published binary, and whose being newer
    /// than it means the binary is stale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three kinds, and the third arrived on 2026-08-26 as a measured
    /// gap.</b> The product's C#, the build files that decide how it is
    /// compiled, and the source this repository vendors from elsewhere and
    /// compiles in — which until then was watched by nothing, so a swapped
    /// SQLite amalgamation left the binary reading as fresh and every arm
    /// driving it asserting about a library nobody had built.
    /// </para>
    /// <para>
    /// <b>A property rather than a local, so that a test can assert what is in
    /// it.</b> A staleness check is exactly the kind of thing that silently
    /// stops covering something: it fails loudly when it fires and says
    /// nothing at all about what it never looked at.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<FileInfo> FreshnessInputs { get; } =
    [
        .. RepositoryLayout.ProductSourceFiles,
        .. RepositoryLayout.BuildFiles,
        .. RepositoryLayout.VendoredSourceFiles,
    ];

    /// <summary>
    /// Fails if the published binary is older than anything that goes into it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The publish is stale.</exception>
    public static void EnsureFresh()
    {
        var published = File.GetLastWriteTimeUtc(Executable);

        var newer = FreshnessInputs
            .Where(file => file.LastWriteTimeUtc > published)
            .Select(file => Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName))
            .Order(StringComparer.Ordinal)
            .ToList();

        if (newer.Count is not 0)
        {
            throw new InvalidOperationException(
                $"The published binary at '{Executable}' is older than {newer.Count} source file(s), so this test would prove nothing about the code in the tree. Run: {PublishCommand}"
                + Environment.NewLine + string.Join(Environment.NewLine, newer));
        }
    }

    /// <summary>
    /// The environment BrowserAI itself is started with: this process's own,
    /// which is what an MCP client would hand it.
    /// </summary>
    /// <remarks>
    /// Deliberately <b>not</b> <c>ChildEnvironment.Build()</c>. That is the
    /// allowlist BrowserAI applies to its own child, and using it here would
    /// mean the test proved the allowlist by supplying it — the child's
    /// environment has to be whatever BrowserAI decides when handed an ordinary
    /// one.
    /// </remarks>
    /// <returns>The inherited environment block.</returns>
    public static Dictionary<string, string> InheritedEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                environment[name] = value;
            }
        }

        return environment;
    }
}
