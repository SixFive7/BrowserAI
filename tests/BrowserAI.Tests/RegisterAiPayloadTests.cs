// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
internal sealed partial class RegisterAiPayloadTests
{
    private const string Script = "Get-RegisterAi.ps1";

    /// <summary>The variable the script takes its API read's token from, which <c>gh</c> does not read.</summary>
    private const string TokenVariable = "REGISTERAI_GITHUB_TOKEN";

    /// <summary>
    /// The hourly limit GitHub gives an address that reads its API unsigned: 60, read
    /// off GitHub's own <c>/rate_limit</c> on 2026-10-14 (kb, toolchain.md).
    /// </summary>
    private const long UnsignedHourlyLimit = 60;

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
        var exe = await RequireTheRegisterAiThePayloadCarriesAsync();

        using var scratch = await ScratchDirectory.CreateAsync("registerai-payload");

        var from = Directory.CreateDirectory(Path.Combine(scratch.Path, "release")).FullName;
        var payload = Directory.CreateDirectory(Path.Combine(scratch.Path, "payload")).FullName;
        var stamp = Path.Combine(scratch.Path, "registerai.json");
        var placed = Path.Combine(payload, "registerai", "RegisterAI.exe");
        var sums = Path.Combine(from, "SHA256SUMS");
        var hash = await HashAsync(exe);

        await TestFiles.CopyAsync(exe, Path.Combine(from, "RegisterAI.exe"));
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
        await Assert.That(await HashAsync(placed)).IsEqualTo(hash);

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
    /// its two files downloaded with no <c>gh</c>: <c>gh</c> is pointed at an empty
    /// configuration folder and every variable it takes a token from is removed, so a
    /// script that still needed <c>gh</c> fails here. The one API read is signed in
    /// with the token the developer's own <c>gh</c> holds, when it holds one, and the
    /// two downloads are never signed in.
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
    /// <para>
    /// ⚠️ <b>The read is signed in since 2026-10-14, the maintainer's 171</b>, in his
    /// words verbatim: <i>"171 your recommendations"</i>. Unsigned, it counted against
    /// the 60 calls an hour GitHub gives one address, which everything on the machine
    /// that reads the API unsigned shares, and in the PowerShell half of the gate at
    /// <c>24202002</c> that hour was spent before the arm's call (the hazard index).
    /// The token is asked of <c>gh auth token</c> in this process's own environment and
    /// handed to the script in <see cref="TokenVariable"/>, a variable <c>gh</c> does not
    /// read, and no file, log or message of this arm carries it: the script reads it
    /// once, takes it out of its own environment and puts it on the API read alone. The
    /// downloads stay unsigned, so a release that stopped being public is still a red
    /// here. <b>With no token, because <c>gh</c> is not installed or holds no sign-in,
    /// the read goes out unsigned</b>, which is the read a payload build makes on such a
    /// machine, so the arm proves there what a build there does. Failing instead would
    /// make a machine's missing sign-in a red tree, and a recorded answer would stop the
    /// arm reading the newest release on the machines where a build reads it live.
    /// Either way the script names the read it made and the hourly limit GitHub
    /// answered it with, and the arm holds both: a read handed a token that GitHub
    /// counted against an address's 60 is a red. <b>Planted red 2026-10-14</b> in a filtered
    /// run against the script of <c>86f9dd8a</c>, which read the release unsigned and named
    /// no read; green with the script that signs it in and names it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePayloadTakesTheNewestRegisterAiReleaseWithNoGitHubSignIn()
    {
        using var scratch = await ScratchDirectory.CreateAsync("registerai-public");

        var payload = Directory.CreateDirectory(Path.Combine(scratch.Path, "payload")).FullName;
        var stamp = Path.Combine(scratch.Path, "registerai.json");
        var placed = Path.Combine(payload, "registerai", "RegisterAI.exe");
        var token = await GitHubTokenAsync();
        var signedOut = SignedOut(Directory.CreateDirectory(Path.Combine(scratch.Path, "gh")).FullName, token);

        // The control: under exactly the variables the script is given, the token's
        // own included, gh, where it is installed, is signed in to nothing, so the run
        // below cannot borrow an account.
        var gh = await TryStartAsync("gh", ["auth", "status"], signedOut);

        await Assert.That(gh is null || gh.Exit != 0).IsTrue().Because($"gh auth status exited 0 under an empty configuration folder, so this run could still sign in: {Scrubbed(gh?.Everything, token)}");

        var taken = await RunAsync(["-PayloadRoot", payload, "-StampPath", stamp], signedOut);
        var said = Scrubbed(taken.Everything, token);

        await Assert.That(token is null || !taken.Everything.Contains(token, StringComparison.Ordinal)).IsTrue()
            .Because("the script printed the token it was handed");
        await Assert.That(taken.Exit).IsEqualTo(0).Because(said);

        // Which read the script made, in its own words, and GitHub's limit for it.
        var read = ApiRead().Match(said);

        await Assert.That(read.Success).IsTrue().Because($"the script named no API read and no limit GitHub answered it with: {said}");

        if (token is null)
        {
            await Assert.That(read.Groups["how"].Value).IsEqualTo("with no sign-in").Because(said);
        }
        else
        {
            await Assert.That(read.Groups["how"].Value).IsEqualTo("signed in with the token handed in " + TokenVariable).Because(said);
            await Assert.That(long.Parse(read.Groups["limit"].Value, CultureInfo.InvariantCulture)).IsGreaterThan(UnsignedHourlyLimit)
                .Because($"GitHub counted a read handed a token against an address's {UnsignedHourlyLimit} calls an hour: {said}");
        }

        using var record = JsonDocument.Parse(await File.ReadAllTextAsync(stamp));

        var root = record.RootElement;
        var tag = root.GetProperty("tag").GetString();

        await Assert.That(root.GetProperty("source").GetString()).IsEqualTo("release");
        await Assert.That(root.GetProperty("repository").GetString()).IsEqualTo("SixFive7/RegisterAI");
        await Assert.That(tag).IsEqualTo("v" + root.GetProperty("version").GetString());
        await Assert.That(root.GetProperty("release").GetString()).IsEqualTo("https://github.com/SixFive7/RegisterAI/releases/tag/" + tag);
        await Assert.That(await HashAsync(placed)).IsEqualTo(root.GetProperty("sha256").GetString());
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
        var exe = await RequireTheRegisterAiThePayloadCarriesAsync();

        using var stamp = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(RepositoryLayout.Root.FullName, "build", "payload", "registerai.json")));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(RepositoryPayload.Layout.Root, "payload.json")));

        var block = manifest.RootElement.GetProperty("registerai");

        await Assert.That(stamp.RootElement.GetProperty("version").GetString()).IsEqualTo(block.GetProperty("version").GetString());
        await Assert.That(stamp.RootElement.GetProperty("sha256").GetString()).IsEqualTo(block.GetProperty("sha256").GetString());
        await Assert.That(await HashAsync(exe)).IsEqualTo(stamp.RootElement.GetProperty("sha256").GetString());
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
        await SuiteEnvironment.RequirePublishedSliceAsync();

        var exe = await RequireTheRegisterAiThePayloadCarriesAsync();
        var published = Path.Combine(PublishedSlice.Directory, "payload", BrowserAI.Registration.RegisterAiTool.FolderName, BrowserAI.Registration.RegisterAiTool.FileName);

        await Assert.That(File.Exists(published)).IsTrue().Because($"the published slice holds no '{published}'. Publish again: {PublishedSlice.PublishCommand}");
        await Assert.That(await HashAsync(published)).IsEqualTo(await HashAsync(exe));
        await Assert.That(BrowserAI.Registration.RegisterAiTool.Beside(PublishedSlice.Executable).Executable).IsEqualTo(published);
    }

    private static async Task<string> RequireTheRegisterAiThePayloadCarriesAsync()
    {
        await SuiteEnvironment.RequireRepositoryPayloadAsync();

        var exe = RepositoryPayload.RegisterAi;

        return File.Exists(exe)
            ? exe
            : throw new InvalidOperationException($"The payload holds no '{exe}'. Run: pwsh -File build/Get-RegisterAi.ps1, or build/Build-Payload.ps1.");
    }

    private static async Task<string> HashAsync(string file) =>
        Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(file)));

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
    /// empty configuration folder for <c>gh</c>, no variable <c>gh</c> takes a token
    /// from, and no prompt; and the token for the script's one API read, in the
    /// variable only the script reads, when there is one.
    /// </summary>
    /// <param name="emptyConfiguration">An empty folder for <c>gh</c>'s configuration.</param>
    /// <param name="token">The token the API read is signed in with, or <see langword="null"/> for an unsigned read.</param>
    private static Dictionary<string, string?> SignedOut(string emptyConfiguration, string? token) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["GH_CONFIG_DIR"] = emptyConfiguration,
        ["GH_TOKEN"] = null,
        ["GITHUB_TOKEN"] = null,
        ["GH_ENTERPRISE_TOKEN"] = null,
        ["GITHUB_ENTERPRISE_TOKEN"] = null,
        ["GH_PROMPT_DISABLED"] = "1",
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GCM_INTERACTIVE"] = "never",
        [TokenVariable] = token,
    };

    /// <summary>
    /// The token the developer's own <c>gh</c> holds, asked in this process's own
    /// environment, or <see langword="null"/> when <c>gh</c> is not installed or holds no
    /// sign-in.
    /// </summary>
    /// <remarks>
    /// Kept in memory and handed to the script in its environment, and never put in a
    /// file, a log or a message: every text of a child this arm prints goes through
    /// <see cref="Scrubbed"/>. <c>gh auth token</c> reads the sign-in and changes nothing.
    /// </remarks>
    /// <returns>The token, or <see langword="null"/>.</returns>
    private static async Task<string?> GitHubTokenAsync()
    {
        ChildRunner.Result asked;

        try
        {
            asked = await ChildRunner.RunAsync(
                "gh",
                ["auth", "token"],
                Environment.CurrentDirectory,
                TestDefaults.ProcessHang,
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["GH_PROMPT_DISABLED"] = "1",
                    ["GIT_TERMINAL_PROMPT"] = "0",
                    ["GCM_INTERACTIVE"] = "never",
                },
                encoding: new UTF8Encoding(false));
        }
        catch (Exception failure) when (failure is Win32Exception or FileNotFoundException)
        {
            return null;
        }

        var token = asked.StandardOutput.Trim();

        return asked.ExitCode is 0 && token.Length > 0 && !token.Any(char.IsWhiteSpace) ? token : null;
    }

    /// <summary>A child's text with the token taken out, for a message the arm prints.</summary>
    /// <param name="text">The text, or <see langword="null"/>.</param>
    /// <param name="token">The token, or <see langword="null"/> when there is none.</param>
    /// <returns>The text, the token replaced by a name for it.</returns>
    private static string Scrubbed(string? text, string? token) =>
        token is null || text is null ? text ?? string.Empty : text.Replace(token, "<the token>", StringComparison.Ordinal);

    /// <summary>The line in which the script names its API read and the hourly limit GitHub answered it with.</summary>
    [GeneratedRegex(@"GitHub API read (?<how>[^;\r\n]+); GitHub answered it with an hourly limit of (?<limit>\d+)\.")]
    private static partial Regex ApiRead();

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
        catch (Exception failure) when (failure is Win32Exception or FileNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Runs a program, with these variables changed for it alone, and returns what it did.</summary>
    private static async Task<ScriptRun> StartAsync(string file, string[] arguments, IReadOnlyDictionary<string, string?>? environment)
    {
        // Through the product's launcher since 2026-10-11, decision 54, with both pipes
        // read as UTF-8, as before.
        var run = await ChildRunner.RunAsync(
            file,
            arguments,
            Environment.CurrentDirectory,
            TestDefaults.ProcessHang,
            environment,
            encoding: new UTF8Encoding(false));

        return new ScriptRun(run.ExitCode ?? -1, run.StandardOutput + run.StandardError);
    }
}
