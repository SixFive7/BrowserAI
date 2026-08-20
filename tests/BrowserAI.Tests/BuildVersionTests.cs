// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.RegularExpressions;
using System.Xml.Linq;
using BrowserAI.Hosting;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// The version is derived from the git tag, is typed nowhere, and is read from
/// the one attribute that carries it.
/// </summary>
/// <remarks>
/// <para>
/// Build-order step 18's done-tests, as tests rather than as a checklist. Each
/// one guards a failure that reports healthy: a version nobody chose, a version
/// decorated after publication, a version collapsed to its major, or a build
/// that does not know what it is at all.
/// </para>
/// <para>
/// <b>What is measured rather than asserted here is in
/// [kb](../../kb/packaging/velopack.md#deriving-the-version-from-git-tags-with-minver).</b>
/// The suite cannot run a build of itself, so the shape of an untagged version
/// and the refusal of a tagless one are recorded there with the commands that
/// re-establish them; what these tests hold is that the mechanism producing them
/// is still wired up.
/// </para>
/// </remarks>
internal sealed partial class BuildVersionTests
{
    [Test]
    public async Task TheVersionIsDerivedAndCarriesNoBuildMetadata()
    {
        // Three parts and an optional pre-release suffix: the shape `vpk`
        // accepts, and the shape MinVer produces from a `v` tag. A `+` anywhere
        // in it is the SourceRevisionId decoration, which is the hourly-restart
        // defect this project inherited a warning about from a shipped product
        // that hit it fleet-wide -- an update path that MATCHES the served
        // version against the reported one can never match a decorated copy.
        await Assert.That(BuildVersion.Current).Matches(DerivedVersion());

        // And it is not the fallback. A binary that could not read its own
        // attribute would answer something honest and useless, which must never
        // be what a released build reports.
        await Assert.That(BuildVersion.Current).IsNotEqualTo(BuildVersion.Unknown);
    }

    [Test]
    public async Task TheAssemblyVersionIsMajorOnlyWhichIsWhyNothingReadsIt()
    {
        // MinVer sets AssemblyVersion to {Major}.0.0.0 BY DESIGN, so every
        // build of the 0.x line reports 0.0.0.0 and every build of the 1.x line
        // reports 1.0.0.0. Measured on this artifact rather than read from
        // MinVer's documentation, because the whole point is that the number a
        // caller would naturally reach for is not the version.
        //
        // Another shipped Velopack product filed the widened form of this as an
        // observed symptom -- "version shows 4 parts" -- and BrowserAI carried
        // the collapsed form live until step 18: SessionLock stamped every
        // browserai.json from GetName().Version.
        var assemblyVersion = typeof(BuildVersion).Assembly.GetName().Version;

        await Assert.That(assemblyVersion).IsNotNull();

        var major = BuildVersion.Current.Split('.', 2)[0];
        await Assert.That(assemblyVersion!.ToString()).IsEqualTo($"{major}.0.0.0");
    }

    [Test]
    public async Task NothingInTheProductReadsTheAssemblyVersion()
    {
        // The mechanism behind the test above. A source scan rather than an
        // analyzer rule because the shape is an ordinary property read on an
        // ordinary type, and because the file that explains why it is forbidden
        // must be able to say so: whole-line comments are stripped first.
        var offenders = new List<string>();

        foreach (var file in RepositoryLayout.ProductSourceFiles)
        {
            var code = await RepositoryLayout.ReadCodeAsync(file);

            if (AssemblyVersionRead().IsMatch(code))
            {
                offenders.Add(Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName));
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    [Test]
    public async Task ThePreReleaseSuffixIsWhatSaysABuildIsNotARelease()
    {
        // The rule this exists for is §G's: never self-update from a build that
        // is not a release. It needs no sentinel and no development-build
        // number, because the same mechanism that derives the version also
        // decides whether it carries a suffix.
        await Assert.That(BuildVersion.HasPreReleaseSuffix("0.1.0")).IsFalse();
        await Assert.That(BuildVersion.HasPreReleaseSuffix("0.1.1-alpha.0.5")).IsTrue();
        await Assert.That(BuildVersion.HasPreReleaseSuffix(BuildVersion.Unknown)).IsTrue();

        await Assert.That(BuildVersion.IsPreRelease).IsEqualTo(BuildVersion.Current.Contains('-', StringComparison.Ordinal));
    }

    [Test]
    public async Task EverySessionRecordsTheVersionTheBuildWasDerivedAs()
    {
        // browserai.json's build stamp is the first thing a support question reads,
        // and until step 18 it would have said 0.0.0.0 for the entire 0.x line.
        using var scratch = ScratchDirectory.Create("version-stamp");
        var path = SessionPath.Resolve(Path.Combine(scratch.Path, "stamped"));
        SessionLayout.Create(path);

        var acquired = SessionLock.TryAcquire(
            path,
            new SessionLockRequest { Browser = "chromium", Purpose = "record the build stamp" },
            NullLogger.Instance);

        try
        {
            await Assert.That(acquired.Taken).IsTrue();

            var record = SessionLock.ReadRecord(path);

            await Assert.That(record).IsNotNull();
            await Assert.That(record!.BrowserAiVersion).IsEqualTo(BuildVersion.Current);
        }
        finally
        {
            acquired.Acquired?.Dispose();
        }
    }

    [Test]
    public async Task TheBuildRefusesAVersionDerivedFromNoTag()
    {
        // The refusal itself cannot be exercised from inside the suite -- it is
        // an MSBuild error, and provoking it means building a tree with no
        // reachable tag. It was provoked on 2026-08-16, before v0.1.0 existed,
        // and the message is in kb. What is guarded here is that the mechanism
        // is still in the project file, because a target quietly deleted leaves
        // every other signal green.
        var project = XDocument.Load(Path.Combine(RepositoryLayout.Root.FullName, "src", "BrowserAI", "BrowserAI.csproj"));

        var target = project.Descendants()
            .FirstOrDefault(element => element.Name.LocalName is "Target"
                && element.Attribute("Name")?.Value is "RefuseAVersionDerivedFromNoTag");

        await Assert.That(target).IsNotNull();
        await Assert.That(target!.Attribute("AfterTargets")?.Value).IsEqualTo("MinVer");

        var refusals = target.Descendants()
            .Where(element => element.Name.LocalName is "Error")
            .ToList();

        // Two: one for a version that is 0.0.0-something, and one for MinVer
        // not having produced a version at all. The second is not redundant --
        // an empty property would pass a StartsWith check.
        await Assert.That(refusals.Count).IsEqualTo(2);
        await Assert.That(refusals.Exists(error => error.Attribute("Condition")?.Value.Contains("0.0.0", StringComparison.Ordinal) is true)).IsTrue();
        await Assert.That(refusals.Exists(error => error.Attribute("Condition")?.Value.Contains("'$(MinVerVersion)' == ''", StringComparison.Ordinal) is true)).IsTrue();

        // The remedy is in the message because the cause is never local. A
        // reader who sees 0.0.0 cannot guess `fetch-depth`.
        var messages = string.Join(" ", refusals.Select(error => error.Attribute("Text")?.Value));
        await Assert.That(messages).Contains("fetch-depth");
    }

    [Test]
    public async Task TheSdkIsForbiddenFromDecoratingTheVersion()
    {
        // Repository-wide rather than on the product project: a global property
        // passed with -p: reaches every referenced project, and a project still
        // carrying a decorated string gets linked into the same AOT binary.
        //
        // Measured 2026-08-16 on SDK 10.0.302, twice: with the source-control
        // feature switched on and a SourceRevisionId supplied, this property
        // set to false leaves the version undecorated, and setting it to true
        // produces `0.1.0+<40-char sha>`. It is the property that decides.
        var props = XDocument.Load(Path.Combine(RepositoryLayout.Root.FullName, "Directory.Build.props"));

        var setting = props.Descendants()
            .Where(element => element.Name.LocalName is "IncludeSourceRevisionInInformationalVersion")
            .Select(element => element.Value)
            .ToList();

        await Assert.That(setting).IsEquivalentTo(["false"]);
    }

    [Test]
    public async Task TheVersionMechanismIsOnTheProductProjectAndNowhereElse()
    {
        var references = new Dictionary<string, string?>(StringComparer.Ordinal);
        var prefixes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in RepositoryLayout.ProjectFiles)
        {
            var name = Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName).Replace('\\', '/');
            var project = XDocument.Load(file.FullName);

            foreach (var reference in project.Descendants()
                .Where(element => element.Name.LocalName is "PackageReference" && element.Attribute("Include")?.Value is "MinVer"))
            {
                references[name] = reference.Attribute("PrivateAssets")?.Value;
            }

            foreach (var prefix in project.Descendants().Where(element => element.Name.LocalName is "MinVerTagPrefix"))
            {
                prefixes[name] = prefix.Value;
            }
        }

        // Versioning the test project buys nothing: no artifact is cut from it
        // and no caller ever sees its version. Two projects deriving a version
        // is two things that can disagree.
        await Assert.That(references.Keys.Order(StringComparer.Ordinal)).IsEquivalentTo(["src/BrowserAI/BrowserAI.csproj"]);

        // Build-time only. Without this it would be a runtime dependency of a
        // NativeAOT binary, for a package that exists to run `git describe`.
        await Assert.That(references["src/BrowserAI/BrowserAI.csproj"]).IsEqualTo("all");

        // The house prefix, unanimous across every tagged repository in this
        // estate. It is a tag prefix rather than a version, which is why it can
        // live in a project file at all.
        await Assert.That(prefixes).IsEquivalentTo(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/BrowserAI/BrowserAI.csproj"] = "v",
        });
    }

    [Test]
    public async Task ThePublishedBinaryReportsADerivedVersionOverTheWire()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SliceRun.SharedAsync();
        var reported = (string?)run.InitializeResult["serverInfo"]?["version"];

        // This is the arm that says the attribute survives ILC. Under NativeAOT
        // the version is read reflectively off an assembly attribute, and a
        // trimmed-away attribute would surface as the fallback rather than as a
        // failure -- a caller would see a plausible version and never know.
        await Assert.That(reported).IsNotNull();
        await Assert.That(reported!).Matches(DerivedVersion());
        await Assert.That(reported).IsNotEqualTo(BuildVersion.Unknown);

        // Deliberately a shape rather than equality with this assembly's own
        // version. The published binary is a separate artifact with its own
        // build timestamp, and PublishedSlice.EnsureFresh compares it against
        // SOURCE files -- so a commit that changes no source leaves a publish
        // that is legitimately fresh and legitimately one commit behind. An
        // equality assertion would be red for a reason that is not a defect.
    }

    /// <summary>Three parts, an optional pre-release suffix, and no build metadata at all.</summary>
    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.\-]*)?$")]
    private static partial Regex DerivedVersion();

    /// <summary>Reading the version off the assembly name, in either spelling.</summary>
    [GeneratedRegex(@"GetName\(\)\s*[.?]\s*Version|AssemblyVersionAttribute")]
    private static partial Regex AssemblyVersionRead();
}
