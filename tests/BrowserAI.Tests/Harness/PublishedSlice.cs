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
    /// The committed provenance stamp for the payload that publishes beside the
    /// binary: <c>build/payload/package-lock.json</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Named by path, because it is the one input here that cannot be
    /// enumerated.</b> <c>RepositoryLayout</c> prunes any directory called
    /// <c>payload</c> during the walk — there are two of them and one carries an
    /// unpacked <c>node_modules</c> — so every corpus that class produces is
    /// blind to this file by construction. Naming it is not a shortcut around
    /// the walk; it is the only way to watch a tree the walk is right to prune.
    /// </para>
    /// <para>
    /// <b>The lock rather than the payload, and that is the honest input.</b>
    /// The thing that actually goes into the publish is the resolved
    /// <c>node_modules</c> tree, which is gitignored, is tens of thousands of
    /// files, and whose timestamps say when <c>npm ci</c> last ran rather than
    /// what it resolved. The lock is the committed record of exactly that
    /// resolution: it moves when and only when the payload's resolved set moves,
    /// and it is what the upstream review reads. Watching it means a re-resolve
    /// that changed something makes the publish stale; it does not mean an
    /// unpacked tree that was deleted and restored does, which is correct — that
    /// is the same payload.
    /// </para>
    /// </remarks>
    public static FileInfo PayloadProvenanceStamp { get; } = new(
        Path.Combine(RepositoryLayout.Root.FullName, "build", "payload", "package-lock.json"));

    /// <summary>
    /// Every file that goes into the published binary, and whose being newer
    /// than it means the binary is stale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Four kinds, and the last two each arrived as a measured gap.</b> The
    /// product's C#, the build files that decide how it is compiled, the source
    /// this repository vendors from elsewhere and compiles in — which until
    /// 2026-08-26 was watched by nothing, so a swapped SQLite amalgamation left
    /// the binary reading as fresh and every arm driving it asserting about a
    /// library nobody had built — and, since 2026-08-30, the payload's
    /// provenance stamp.
    /// </para>
    /// <para>
    /// <b>The fourth is the same failure one directory across.</b> A publish
    /// copies the resolved payload beside the executable, so a payload
    /// re-resolve that moved <c>@playwright/mcp</c>, <c>playwright-core</c> or
    /// <c>node</c> leaves the published tree carrying the old one — and until
    /// this row existed, reading as fresh. It was benign on the day it was
    /// found, 2026-08-29, and only because the re-resolve had come back byte for
    /// byte; nothing about the check made it benign. See
    /// <see cref="PayloadProvenanceStamp"/> for why the stamp is watched and the
    /// tree is not.
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
        PayloadProvenanceStamp,
    ];

    /// <summary>
    /// Fails if the published binary is older than anything that goes into it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Both sides of this comparison are file modification times, and a
    /// commit's date is neither of them.</b> It reads as pedantry until somebody
    /// makes the substitution, and on 2026-08-30 somebody did: a gate runner put
    /// the published binary's <c>LastWriteTime</c> of <b>01:14:16.500</b> beside
    /// commit <c>56383c9</c>'s date of <b>01:20:40</b>, saw the commit touching
    /// <c>src/BrowserAI/Sessions/SessionLock.cs</c> six minutes after the
    /// publish, and reported that four subsequent gate sets — twelve full runs —
    /// had driven a stale binary while passing this check. <b>Every part of that
    /// reading is true and the conclusion is false.</b> <c>SessionLock.cs</c>'s
    /// own timestamp was <b>01:12:22.665</b>, one minute 53.8 seconds
    /// <i>before</i> the publish; <c>git commit</c> records when it ran and never
    /// touches a working-tree file. The batch edited, published, gated and then
    /// committed, in that order, which is the order its own commit message says
    /// it took. Re-measured the same day over the whole of
    /// <see cref="FreshnessInputs"/>: <b>0 of 95 inputs newer than the binary</b>.
    /// </para>
    /// <para>
    /// <b>What made the misreading available is that this check says nothing when
    /// it passes.</b> It throws or it is silent, and the run's coverage block
    /// carries a <c>published slice</c> row that reports <c>PRESENT</c> — which is
    /// a claim about existence and not about freshness. So a log full of green
    /// runs offers no sentence to check a staleness suspicion against, and the
    /// nearest thing to hand is a commit date. Making the run state its own
    /// freshness margin is a change to the coverage block rather than to this
    /// method, and it is not made here.
    /// </para>
    /// <para>
    /// <b>Timestamps rather than content, and that is forced rather than
    /// chosen.</b> The obvious stronger check — hash the inputs, hash the
    /// binary, refuse a binary that does not belong to them — has no binary
    /// half to compare against here. Measured 2026-08-30: two publishes of an
    /// <i>identical</i> input set, nothing in <see cref="FreshnessInputs"/>
    /// touched between them, produced binaries of the same length
    /// (<c>19,186,688</c> bytes) and <b>different SHA-256</b>. So a content hash
    /// cannot answer <i>is this binary from this source</i> for this toolchain,
    /// and the modification times are what is left. Re-establish it by running
    /// <see cref="PublishCommand"/> twice with no edit between and comparing
    /// <c>Get-FileHash</c>.
    /// </para>
    /// </remarks>
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
