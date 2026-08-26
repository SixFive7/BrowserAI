// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using BrowserAI.Storage;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The SQLite that is compiled from source in this tree and linked into the
/// published binary — that it is the version the pin says, and that the binary
/// reports it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The version moved from "floats with everything else" to "pinned by us",
/// and these two tests are the price of that.</b> Every other native dependency
/// here arrives through a package manager and a lock file, so a version nobody
/// chose is a diff. SQLite arrives as ~9 MB of vendored C that nobody reads,
/// compiled by <c>build/Sqlite.targets</c> and linked by ILC — three places that
/// can each say a different number while every signal stays green.
/// </para>
/// <para>
/// <b>Nothing here calls into SQLite.</b> The product's single declaration, in
/// <c>src\BrowserAI\Storage\Sqlite.cs</c>, binds the module <c>e_sqlite3</c> —
/// which under the published binary is a symbol linked into the executable and
/// under this test host, CoreCLR, is a DLL that does not exist. So the version
/// is read off the <i>artifact's own record</i> rather than by calling the
/// function here, which is also the stronger claim: what a test host could load
/// says nothing about what ILC linked.
/// </para>
/// <para>
/// <b>The split is on capability, not on subject.</b>
/// <see cref="ThePinnedVersionIsTheVersionOfTheVendoredSource"/> needs nothing
/// but the tree and therefore runs on every machine, publish or no publish;
/// <see cref="ThePublishedBinaryReportsTheStaticallyLinkedSqliteVersion"/> needs
/// the published slice and skips loudly without it. Folded into one test, a
/// machine with no publish would silently lose the pin check as well.
/// </para>
/// </remarks>
internal sealed partial class SqliteTests
{
    /// <summary>
    /// What the drift row pins, what the vendored source says, and that the
    /// bytes on disk are the bytes that were downloaded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three files have to agree and only one of them is read by a human.</b>
    /// <c>drift-check.json</c> is what a daily check compares against sqlite.org;
    /// <c>sqlite3.c</c> is what <c>cl.exe</c> actually compiles; <c>sqlite3.h</c>
    /// is what a reader opens. A pin that drifted from the source would report a
    /// version this build does not contain, and the drift check would then be
    /// answering a question about a file nobody is compiling.
    /// </para>
    /// <para>
    /// <b>The hashes are the half that catches an edit rather than a swap.</b>
    /// Third-party source is exactly the kind of file a sweep walks through —
    /// this repository has already had one rewrite a sealed record — and 9 MB of
    /// C is the last place anybody would look. The archive's own SHA3-256 is
    /// recorded beside them as provenance and deliberately <b>not</b> asserted:
    /// the zip is not in the tree, so nothing here could re-derive it, and a test
    /// that pretended to check it would be checking that a string equals itself.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePinnedVersionIsTheVersionOfTheVendoredSource()
    {
        var pin = PinnedRow();
        var pinned = pin.GetProperty("pinned").GetString();

        // The positive control comes first: a parse that stopped matching would
        // make every comparison below true of nothing at all.
        await Assert.That(pinned).IsNotNull();
        await Assert.That(pinned!).Matches(ThreePartVersion());

        var inTheCompiledFile = VersionDefinedIn("sqlite3.c");
        var inTheHeader = VersionDefinedIn("sqlite3.h");

        await Assert.That(inTheCompiledFile)
            .IsEqualTo(pinned)
            .Because("drift-check.json pins a SQLite version and third-party/sqlite/sqlite3.c is the file cl.exe compiles. A disagreement means the drift check is watching a release nobody linked.");

        // The header is read as well, because the two are vendored as a pair and
        // a mismatched pair is a real way to get this wrong: sqlite3.c carries
        // its own inlined copy of the header, so a stale sqlite3.h beside it
        // compiles, links, and reads correctly to a human.
        await Assert.That(inTheHeader).IsEqualTo(inTheCompiledFile);

        var wrong = new List<string>();

        foreach (var entry in pin.GetProperty("files").EnumerateObject())
        {
            if (entry.Name is "note")
            {
                continue;
            }

            var file = Path.Combine(RepositoryLayout.Root.FullName, entry.Name.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(file))
            {
                wrong.Add($"{entry.Name}: recorded in drift-check.json and not in the tree");
                continue;
            }

            // Lower-case at the source rather than through ToLowerInvariant,
            // which CA1308 forbids: the recorded hashes are lower case because
            // every tool that prints one is.
            var actual = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(file)));

            if (!string.Equals(actual, entry.Value.GetString(), StringComparison.Ordinal))
            {
                wrong.Add($"{entry.Name}: is {actual} and drift-check.json records {entry.Value.GetString()}");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, wrong))
            .IsEmpty()
            .Because("the vendored amalgamation is upstream's bytes or it is not upstream's. Re-download the archive named in the row, check its SHA3-256 against sqlite.org's own PRODUCT line, and re-extract -- never edit the recorded hash to match the file.");

        // And the loop looked at something. An empty `files` object satisfies
        // the assertion above perfectly.
        await Assert.That(pin.GetProperty("files").EnumerateObject().Count(entry => entry.Name is not "note")).IsEqualTo(2);
    }

    /// <summary>
    /// The published NativeAOT binary names the SQLite it was linked against, in
    /// the one record it writes about what it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the arm that says the static link happened at all.</b> Nothing
    /// else in the suite can: <c>DirectPInvoke</c> and <c>NativeLibrary</c> are
    /// inert MSBuild items outside a publish, so a build in which the compile
    /// step silently did nothing looks identical everywhere except here — and
    /// the failure it would produce is a <c>DllNotFoundException</c> at whatever
    /// moment a session first touched the record, which is the worst possible
    /// place to find out.
    /// </para>
    /// <para>
    /// <b>Read from the process log, never from stderr.</b> <see cref="SliceRun"/>
    /// terminates the binary from outside on purpose, and stderr goes through
    /// <c>AddConsole</c>'s background queue, which loses whatever it still held.
    /// <c>ProcessLogRecords</c>' own remarks are the record of that costing two
    /// red CI runs for a line the product had written correctly.
    /// </para>
    /// <para>
    /// <b>Asserted against the vendored source rather than a literal.</b> A
    /// number typed here would have to be edited by whoever swaps the
    /// amalgamation, which is the one moment they are thinking about something
    /// else.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePublishedBinaryReportsTheStaticallyLinkedSqliteVersion()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var expected = VersionDefinedIn("sqlite3.c");
        var run = await SliceRun.SharedAsync();

        await Assert.That(run.ProcessLog)
            .Contains($"sqlite={expected}")
            .Because($"BrowserAI ran as pid {run.BrowserAiProcessId} and wrote {run.ProcessLog.Split('\n').Length} record(s) to the shared process log");
    }

    /// <summary>
    /// The vendored amalgamation is part of what makes the published binary
    /// stale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>It was not, and the gap fails in the direction nothing reports.</b>
    /// <c>PublishedSlice.EnsureFresh</c> compared the binary's timestamp
    /// against the product's <c>.cs</c> files and the build files, and
    /// <c>third-party/sqlite/sqlite3.c</c> is neither — so swapping the
    /// amalgamation and running the suite left every slice arm driving a binary
    /// that still had the old SQLite linked into it, reading as fresh, with the
    /// tree saying otherwise.
    /// </para>
    /// <para>
    /// <b>Asserted over the corpus rather than over the check.</b> A staleness
    /// check says nothing about what it never looked at: it is silent by
    /// construction when it is passing, and passing is exactly what it does
    /// when a file is outside it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheFreshnessCheckWatchesTheVendoredAmalgamation()
    {
        var watched = PublishedSlice.FreshnessInputs
            .Select(file => Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName).Replace('\\', '/'))
            .ToList();

        // The positive control: the corpus is the one that was already there,
        // so an empty or broken enumeration cannot make the two arms below pass.
        await Assert.That(watched).Contains("src/BrowserAI/Program.cs");
        await Assert.That(watched).Contains("build/Sqlite.targets");

        await Assert.That(watched)
            .Contains("third-party/sqlite/sqlite3.c")
            .Because("build/Sqlite.targets compiles that file with cl.exe and ILC links the archive into BrowserAI.exe, so it is product source by every meaning that matters and a swapped copy must make the publish stale");

        await Assert.That(watched).Contains("third-party/sqlite/sqlite3.h");
    }

    /// <summary>
    /// The options the product says it intends are the options the build
    /// actually passes, in both directions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two spellings of one decision, and neither can be derived from the
    /// other.</b> <c>build/Sqlite.targets</c> writes <c>cl</c> switches;
    /// <c>Sqlite.IntendedCompileOptions</c> writes what <c>PRAGMA
    /// compile_options</c> reports. SQLite's own <c>ctime.c</c> emits some
    /// options with their value and some without, so
    /// <c>SQLITE_STRICT_SUBTYPE=1</c> is reported as a bare
    /// <c>STRICT_SUBTYPE</c> while <c>SQLITE_DQS=0</c> is reported as
    /// <c>DQS=0</c> — a mechanical translation gets that wrong, which is why
    /// the correspondence is asserted on the <i>names</i> and the values are
    /// carried by the list.
    /// </para>
    /// <para>
    /// <b>Both directions, because each is a different mistake.</b> A flag
    /// added to the build and not to the list is an option the published binary
    /// carries and no test asks about; a name in the list and not in the build
    /// is an option the binary will never report, which would make
    /// <see cref="TheStaticallyLinkedLibraryReportsTheIntendedBuild"/> fail
    /// with a message about the wrong file.
    /// </para>
    /// <para>
    /// <b>This arm needs nothing but the tree</b>, so a machine that cannot
    /// publish still holds the correspondence — the same split
    /// <see cref="ThePinnedVersionIsTheVersionOfTheVendoredSource"/> is on.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheIntendedCompileOptionsAreTheOnesTheBuildPasses()
    {
        var targets = await File.ReadAllTextAsync(
            Path.Combine(RepositoryLayout.Root.FullName, "build", "Sqlite.targets"));

        var passed = CompileDefinition().Matches(targets)
            .Select(match => match.Groups["name"].Value)
            .Order(StringComparer.Ordinal)
            .ToList();

        // The positive control first: a scan that stopped matching would make
        // every comparison below true of two empty sets.
        await Assert.That(passed.Count).IsGreaterThan(0);
        await Assert.That(passed).Contains("OMIT_AUTOINIT");

        var intended = Sqlite.IntendedCompileOptions
            .Select(option => option.Split('=', 2)[0])
            .Order(StringComparer.Ordinal)
            .ToList();

        await Assert.That(string.Join(", ", intended))
            .IsEqualTo(string.Join(", ", passed))
            .Because("build/Sqlite.targets is what cl.exe is handed and Sqlite.IntendedCompileOptions is what a test asserts the published binary reports. A name in one and not the other is either a flag nothing checks or a check no flag can satisfy.");

        // THREADSAFE is deliberately in neither: it is the one deviation from
        // sqlite.org's recommended set -- taking their SQLITE_THREADSAFE=0
        // would be the defect -- so it is asserted as a floor under every build
        // rather than listed as an intended flag.
        await Assert.That(passed).DoesNotContain("THREADSAFE");
        await Assert.That(targets).DoesNotContain("/DSQLITE_THREADSAFE");
    }

    /// <summary>
    /// The statically linked library reports the build this tree intends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the only place the compile flags can be checked at all.</b>
    /// <c>PRAGMA compile_options</c> describes the library that is actually
    /// bound, and the two hosts differ: the published binary links the archive
    /// <c>build/Sqlite.targets</c> compiles from vendored source, while this
    /// test host loads a loose <c>e_sqlite3.dll</c> somebody else built. So the
    /// claim is about the artifact, and the artifact is what has to say it.
    /// </para>
    /// <para>
    /// <b>Read from the process log, never from stderr.</b> <c>SliceRun</c>
    /// terminates the binary from outside on purpose, and stderr goes through a
    /// background queue that loses whatever it still held.
    /// </para>
    /// <para>
    /// ⚠️ <b>It also carries the initialisation check, which nothing else
    /// can — and that check does not fail as a wrong value.</b>
    /// <c>SQLITE_OMIT_AUTOINIT</c> is in the flags, so a published binary that
    /// opened a database without calling <c>sqlite3_initialize</c> first
    /// <b>faults</b>: measured 2026-08-26, an access violation inside the first
    /// <c>sqlite3_open_v2</c>, exit code <c>-1073741819</c>, stdout closed
    /// before <c>initialize</c> was answered. So this arm's red for that defect
    /// is not a field reading <c>&lt;unavailable&gt;</c> — it is the slice
    /// failing to start at all, which is also what
    /// <see cref="ThePublishedBinaryReportsTheStaticallyLinkedSqliteVersion"/>
    /// reports. The DLL a test host loads is built <i>without</i> that flag and
    /// initialises itself, so the whole class is invisible everywhere except
    /// against the artifact.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheStaticallyLinkedLibraryReportsTheIntendedBuild()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SliceRun.SharedAsync();

        await Assert.That(run.ProcessLog)
            .Contains("sqliteBuild=intended")
            .Because($"BrowserAI ran as pid {run.BrowserAiProcessId}; anything other than 'intended' names the options the linked archive is missing -- 'missing STRICT_SUBTYPE=1' is what this printed when the intended list carried a value SQLite reports without one -- and '<unavailable' means the library was reachable and refused");
    }

    /// <summary>The <c>vendored.sqlite-amalgamation</c> row of the drift check.</summary>
    /// <returns>The row.</returns>
    private static JsonElement PinnedRow()
    {
        using var drift = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepositoryLayout.Root.FullName, "drift-check.json")));

        return drift.RootElement.GetProperty("vendored").GetProperty("sqlite-amalgamation").Clone();
    }

    /// <summary>The version one vendored file defines.</summary>
    /// <param name="name">The file, under <c>third-party/sqlite</c>.</param>
    /// <returns>The <c>SQLITE_VERSION</c> string literal.</returns>
    private static string? VersionDefinedIn(string name)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryLayout.Root.FullName, "third-party", "sqlite", name));
        var matches = SqliteVersionDefine().Matches(text);

        // Exactly one, or the answer is a guess. The amalgamation inlines its own
        // header, so a second definition would mean the two disagree somewhere a
        // single match would hide.
        return matches.Count is 1 ? matches[0].Groups["version"].Value : null;
    }

    /// <summary>The amalgamation's own version definition, anchored to a line start.</summary>
    [GeneratedRegex(@"^#define SQLITE_VERSION\s+""(?<version>[^""]+)""", RegexOptions.Multiline)]
    private static partial Regex SqliteVersionDefine();

    /// <summary>Three dotted numbers, which is every SQLite release since 3.0.</summary>
    [GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial Regex ThreePartVersion();

    /// <summary>
    /// One <c>cl</c> preprocessor definition in <c>build/Sqlite.targets</c>,
    /// with the <c>SQLITE_</c> prefix and any value stripped off the capture.
    /// </summary>
    /// <remarks>
    /// The prefix is required rather than optional: it is what separates a
    /// compile-time option from every other switch the command line carries,
    /// and <c>PRAGMA compile_options</c> reports the names without it.
    /// </remarks>
    [GeneratedRegex(@"/DSQLITE_(?<name>[A-Z0-9_]+)")]
    private static partial Regex CompileDefinition();
}
