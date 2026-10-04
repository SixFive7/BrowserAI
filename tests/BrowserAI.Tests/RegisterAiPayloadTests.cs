// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// How RegisterAI gets into the payload: only when it matches the checksum list
/// published beside it, and on the record when it does.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q349, decided 2026-10-01 by the maintainer, verbatim: <i>"Q349 a"</i></b> --
/// BrowserAI's build fetches RegisterAI's release file and checks it against the
/// release's own checksum list, with a local-path override while RegisterAI is
/// private. The check is the same for both sources and is driven through the override,
/// against the RegisterAI the payload already carries.
/// </para>
/// <para>
/// <b>Q379, decided 2026-10-04 by the maintainer, verbatim: <i>"Q379 b"</i></b> --
/// RegisterAI is public, and the fetch from GitHub is driven here too, with no
/// sign-in. <i>Corrected 2026-10-04 (previously "The fetch from GitHub is not driven
/// here, because it needs the network and a signed-in <c>gh</c>; the check is the
/// same for both sources and is driven through the override, against the RegisterAI
/// the payload already carries.")</i>
/// </para>
/// <para>
/// <b>Planted red 2026-10-03</b>: both arms ran before <c>build/Get-RegisterAi.ps1</c>
/// existed and failed on the script that was not there.
/// </para>
/// </remarks>
internal sealed class RegisterAiPayloadTests
{
    private const string Script = "Get-RegisterAi.ps1";

    /// <summary>What one run of the script did.</summary>
    /// <param name="Exit">Its exit code.</param>
    /// <param name="Everything">stdout followed by stderr.</param>
    private sealed record ScriptRun(int Exit, string Everything);

    /// <summary>
    /// A file whose hash is not on its list, or a list with no line for it, is refused
    /// and nothing is written; the release's own list is taken, the copy hashed again,
    /// and both records written.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePayloadTakesRegisterAiOnlyWhenItMatchesItsChecksumList()
    {
        var exe = RequireTheRegisterAiThePayloadCarries();

        using var scratch = ScratchDirectory.Create("registerai-payload");

        var from = Directory.CreateDirectory(Path.Combine(scratch.Path, "release")).FullName;
        var payload = Directory.CreateDirectory(Path.Combine(scratch.Path, "payload")).FullName;
        var stamp = Path.Combine(scratch.Path, "registerai.json");
        var placed = Path.Combine(payload, "registerai", "RegisterAI.exe");
        var sums = Path.Combine(from, "SHA256SUMS");
        var hash = Hash(exe);

        File.Copy(exe, Path.Combine(from, "RegisterAI.exe"));
        await File.WriteAllTextAsync(Path.Combine(payload, "payload.json"), "{ \"builtUtc\": \"2026-10-03T00:00:00Z\" }");

        string[] arguments = ["-From", from, "-PayloadRoot", payload, "-StampPath", stamp];

        // A list that names another hash: refused, and nothing written.
        await File.WriteAllTextAsync(sums, new string('0', 64) + "  RegisterAI.exe\n");

        var mismatched = await RunAsync(arguments);

        await Assert.That(mismatched.Exit).IsNotEqualTo(0);
        await Assert.That(mismatched.Everything).Contains("Nothing was put in the payload");
        await Assert.That(File.Exists(placed)).IsFalse();
        await Assert.That(File.Exists(stamp)).IsFalse();

        // A list with no line for this file: refused the same way.
        await File.WriteAllTextAsync(sums, hash + "  SomethingElse.exe\n");

        var unlisted = await RunAsync(arguments);

        await Assert.That(unlisted.Exit).IsNotEqualTo(0);
        await Assert.That(unlisted.Everything).Contains("carries no line for RegisterAI.exe");
        await Assert.That(File.Exists(placed)).IsFalse();

        // The release's own list: taken and recorded.
        await File.WriteAllTextAsync(sums, hash + "  RegisterAI.exe\n");

        var taken = await RunAsync(arguments);

        await Assert.That(taken.Exit).IsEqualTo(0).Because(taken.Everything);
        await Assert.That(Hash(placed)).IsEqualTo(hash);

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(payload, "payload.json")));
        using var record = JsonDocument.Parse(await File.ReadAllTextAsync(stamp));

        var block = manifest.RootElement.GetProperty("registerai");

        await Assert.That(manifest.RootElement.GetProperty("builtUtc").GetString()).IsEqualTo("2026-10-03T00:00:00Z");
        await Assert.That(block.GetProperty("sha256").GetString()).IsEqualTo(hash);
        await Assert.That(block.GetProperty("source").GetString()).IsEqualTo("folder");
        await Assert.That(record.RootElement.GetProperty("sha256").GetString()).IsEqualTo(hash);
        await Assert.That(record.RootElement.GetProperty("version").GetString()).IsEqualTo(block.GetProperty("version").GetString());
    }

    /// <summary>
    /// With no folder named, the newest release of the public repository is read and
    /// its two files downloaded with no GitHub sign-in: <c>gh</c> is pointed at an
    /// empty configuration folder and every token variable is removed, so a script
    /// that still needed <c>gh</c> fails here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q379 b</b>, the maintainer's words verbatim: <i>"Q379 b"</i>. A clone of
    /// <c>next</c> builds its payload with no <c>gh</c> access. <b>Planted red
    /// 2026-10-04</b> against the script that read the release with <c>gh</c>.
    /// </para>
    /// <para>
    /// It needs the network: one read of <c>api.github.com</c> and two downloads from
    /// <c>github.com</c>, which a payload build makes too.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePayloadTakesTheNewestRegisterAiReleaseWithNoGitHubSignIn()
    {
        using var scratch = ScratchDirectory.Create("registerai-public");

        var payload = Directory.CreateDirectory(Path.Combine(scratch.Path, "payload")).FullName;
        var stamp = Path.Combine(scratch.Path, "registerai.json");
        var placed = Path.Combine(payload, "registerai", "RegisterAI.exe");
        var signedOut = SignedOut(Directory.CreateDirectory(Path.Combine(scratch.Path, "gh")).FullName);

        // The control: under these variables gh, where it is installed, is signed in
        // to nothing, so the run below cannot borrow an account.
        var gh = await TryStartAsync("gh", ["auth", "status"], signedOut);

        await Assert.That(gh is null || gh.Exit != 0).IsTrue().Because($"gh auth status exited 0 under an empty configuration folder, so this run could still sign in: {gh?.Everything}");

        var taken = await RunAsync(["-PayloadRoot", payload, "-StampPath", stamp], signedOut);

        await Assert.That(taken.Exit).IsEqualTo(0).Because(taken.Everything);

        using var record = JsonDocument.Parse(await File.ReadAllTextAsync(stamp));

        var root = record.RootElement;
        var tag = root.GetProperty("tag").GetString();

        await Assert.That(root.GetProperty("source").GetString()).IsEqualTo("release");
        await Assert.That(root.GetProperty("repository").GetString()).IsEqualTo("SixFive7/RegisterAI");
        await Assert.That(tag).IsEqualTo("v" + root.GetProperty("version").GetString());
        await Assert.That(root.GetProperty("release").GetString()).IsEqualTo("https://github.com/SixFive7/RegisterAI/releases/tag/" + tag);
        await Assert.That(Hash(placed)).IsEqualTo(root.GetProperty("sha256").GetString());
    }

    /// <summary>
    /// The committed stamp, <c>build/payload/registerai.json</c>, names the RegisterAI
    /// the payload carries: the same version in both records, and the file hashing to
    /// what they say.
    /// </summary>
    /// <remarks>
    /// The stamp is what the upstream rows resolve RegisterAI's version from, so a
    /// payload rebuilt without it, or a stamp edited by hand, is a red build.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCommittedStampNamesTheRegisterAiThePayloadCarries()
    {
        var exe = RequireTheRegisterAiThePayloadCarries();

        using var stamp = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(RepositoryLayout.Root.FullName, "build", "payload", "registerai.json")));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(RepositoryPayload.Layout.Root, "payload.json")));

        var block = manifest.RootElement.GetProperty("registerai");

        await Assert.That(stamp.RootElement.GetProperty("version").GetString()).IsEqualTo(block.GetProperty("version").GetString());
        await Assert.That(stamp.RootElement.GetProperty("sha256").GetString()).IsEqualTo(block.GetProperty("sha256").GetString());
        await Assert.That(Hash(exe)).IsEqualTo(stamp.RootElement.GetProperty("sha256").GetString());
    }

    /// <summary>
    /// The committed stamp names a stable RegisterAI release: taken from GitHub, a version
    /// with no pre-release part, that version's own tag, and the tag's release page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The maintainer, 2026-10-04, verbatim:</b> <i>"Whenever there needs changing.
    /// Create a new stable RegisterAI release and reference that."</i> A change BrowserAI
    /// needs from RegisterAI arrives as a new stable release and a payload rebuilt from
    /// it. <c>-RegisterAiFrom</c> stays, for an offline build and for trying a RegisterAI
    /// build before it is released, and the stamp such a build writes is refused here, so
    /// it cannot be committed.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-04</b> against the stamp <c>build/Get-RegisterAi.ps1 -From</c>
    /// wrote over RegisterAI's own release folder, which names the folder and no tag, and
    /// green again once the script took the release from GitHub.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCommittedStampNamesAStableRegisterAiRelease()
    {
        var committed = await File.ReadAllTextAsync(Path.Combine(RepositoryLayout.Root.FullName, "build", "payload", "registerai.json"));

        await Assert.That(string.Join(Environment.NewLine, NotAStableRelease(committed))).IsEmpty();

        // The controls: every way a stamp can name something other than a stable
        // release, each one step from the stamp that passes.
        const string Release = "https://github.com/SixFive7/RegisterAI/releases/tag/";

        await Assert.That(NotAStableRelease(stampOf("9.9.9", "v9.9.9", "release", "SixFive7/RegisterAI", Release + "v9.9.9"))).IsEmpty();
        await Assert.That(NotAStableRelease(stampOf("9.9.9", null, "folder", "SixFive7/RegisterAI", null))).IsNotEmpty();
        await Assert.That(NotAStableRelease(stampOf("9.9.10-alpha.0.1", "v9.9.10-alpha.0.1", "release", "SixFive7/RegisterAI", Release + "v9.9.10-alpha.0.1"))).IsNotEmpty();
        await Assert.That(NotAStableRelease(stampOf("9.9.9", "v9.9.8", "release", "SixFive7/RegisterAI", Release + "v9.9.8"))).IsNotEmpty();
        await Assert.That(NotAStableRelease(stampOf("9.9.9", "v9.9.9", "release", "SomebodyElse/RegisterAI", "https://github.com/SomebodyElse/RegisterAI/releases/tag/v9.9.9"))).IsNotEmpty();
        await Assert.That(NotAStableRelease(stampOf("9.9.9", "v9.9.9", "release", "SixFive7/RegisterAI", Release + "v9.9.8"))).IsNotEmpty();

        static string stampOf(string version, string? tag, string source, string repository, string? release) =>
            JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["version"] = version,
                ["tag"] = tag,
                ["source"] = source,
                ["repository"] = repository,
                ["release"] = release,
            });
    }

    /// <summary>
    /// The published slice carries the payload's RegisterAI at the path an install
    /// looks for it beside its own image, byte for byte.
    /// </summary>
    /// <remarks>
    /// The plan's section 6: <i>"A release test requires the exe in the published
    /// slice."</i> The publish copies the payload whole, so a slice published before
    /// the payload gained RegisterAI, or before it moved, is the stale one this names.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePublishedSliceCarriesTheRegisterAiOfThePayload()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var exe = RequireTheRegisterAiThePayloadCarries();
        var published = Path.Combine(PublishedSlice.Directory, "payload", BrowserAI.Registration.RegisterAiTool.FolderName, BrowserAI.Registration.RegisterAiTool.FileName);

        await Assert.That(File.Exists(published)).IsTrue().Because($"the published slice holds no '{published}'. Publish again: {PublishedSlice.PublishCommand}");
        await Assert.That(Hash(published)).IsEqualTo(Hash(exe));
        await Assert.That(BrowserAI.Registration.RegisterAiTool.Beside(PublishedSlice.Executable).Executable).IsEqualTo(published);
    }

    private static string RequireTheRegisterAiThePayloadCarries()
    {
        SuiteEnvironment.RequireRepositoryPayload();

        var exe = RepositoryPayload.RegisterAi;

        return File.Exists(exe)
            ? exe
            : throw new InvalidOperationException($"The payload holds no '{exe}'. Run: pwsh -File build/Get-RegisterAi.ps1, or build/Build-Payload.ps1.");
    }

    private static string Hash(string file) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));

    /// <summary>
    /// Every way a stamp names something other than a stable RegisterAI release, or
    /// nothing when it names one.
    /// </summary>
    /// <param name="stamp">The stamp's text.</param>
    /// <returns>One sentence per problem.</returns>
    private static List<string> NotAStableRelease(string stamp)
    {
        using var document = JsonDocument.Parse(stamp);

        var root = document.RootElement;
        var version = text(root, "version");
        var tag = text(root, "tag");
        var source = text(root, "source");
        var repository = text(root, "repository");
        var release = text(root, "release");
        var problems = new List<string>();

        if (source is not "release")
        {
            problems.Add($"The stamp's source is '{source}', not 'release': the payload was built from a folder and not from a RegisterAI release. Run: pwsh -File build/Get-RegisterAi.ps1");
        }

        if (repository is not "SixFive7/RegisterAI")
        {
            problems.Add($"The stamp names the repository '{repository}', not SixFive7/RegisterAI.");
        }

        if (version?.Split('.') is not { Length: 3 } parts || !Array.TrueForAll(parts, part => part.Length > 0 && part.All(char.IsAsciiDigit)))
        {
            problems.Add($"The stamp names version '{version}', which is not a stable release: three numbers and no pre-release part.");
        }

        if (tag != "v" + version)
        {
            problems.Add($"The stamp names tag '{tag}' for version '{version}'; a RegisterAI release is tagged v<version>.");
        }

        if (release != "https://github.com/" + repository + "/releases/tag/" + tag)
        {
            problems.Add($"The stamp names the release page '{release}', which is not the page of tag '{tag}'.");
        }

        return problems;

        static string? text(JsonElement owner, string name) =>
            owner.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>
    /// The variables of a machine with no GitHub sign-in, for the child alone: an
    /// empty configuration folder for <c>gh</c>, no token variable, and no prompt.
    /// </summary>
    private static Dictionary<string, string?> SignedOut(string emptyConfiguration) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["GH_CONFIG_DIR"] = emptyConfiguration,
        ["GH_TOKEN"] = null,
        ["GITHUB_TOKEN"] = null,
        ["GH_ENTERPRISE_TOKEN"] = null,
        ["GITHUB_ENTERPRISE_TOKEN"] = null,
        ["GH_PROMPT_DISABLED"] = "1",
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GCM_INTERACTIVE"] = "never",
    };

    /// <summary>Runs the script and returns its exit code and everything it said.</summary>
    private static Task<ScriptRun> RunAsync(string[] arguments, IReadOnlyDictionary<string, string?>? environment = null) =>
        StartAsync("pwsh", ["-NoProfile", "-NonInteractive", "-File", Path.Combine(RepositoryLayout.Root.FullName, "build", Script), .. arguments], environment);

    /// <summary>Runs a program that may not be installed: null when it is not.</summary>
    private static async Task<ScriptRun?> TryStartAsync(string file, string[] arguments, IReadOnlyDictionary<string, string?> environment)
    {
        try
        {
            return await StartAsync(file, arguments, environment);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Runs a program, with these variables changed for it alone, and returns what it did.</summary>
    private static async Task<ScriptRun> StartAsync(string file, string[] arguments, IReadOnlyDictionary<string, string?>? environment)
    {
        var start = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment ?? new Dictionary<string, string?>())
        {
            if (value is null)
            {
                _ = start.Environment.Remove(name);
            }
            else
            {
                start.Environment[name] = value;
            }
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"'{file}' did not start.");

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync().WaitAsync(TestDefaults.ProcessHang);

        return new ScriptRun(process.ExitCode, await output + await error);
    }
}
