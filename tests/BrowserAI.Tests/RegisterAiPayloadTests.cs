// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

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
/// private. The fetch from GitHub is not driven here, because it needs the network and
/// a signed-in <c>gh</c>; the check is the same for both sources and is driven through
/// the override, against the RegisterAI the payload already carries.
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

    /// <summary>Runs the script and returns its exit code and everything it said.</summary>
    private static async Task<ScriptRun> RunAsync(string[] arguments)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(RepositoryLayout.Root.FullName, "build", Script));

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("'pwsh' did not start.");

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync().WaitAsync(TestDefaults.ProcessHang);

        return new ScriptRun(process.ExitCode, await output + await error);
    }
}
