// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The release script: the validation rule the suite can drive, and the
/// packaging decisions that must never quietly change.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule is driven, the flags are scanned, and the difference is
/// deliberate.</b> <c>build/Test-ReleaseVersion.ps1</c> is a pure decision and
/// is executed here for real, both ways. The <c>vpk pack</c> invocation cannot
/// be — it needs the tool, a publish and two minutes — so what is asserted about
/// it is that the four decisions with a blast radius are still in the file:
/// never <c>--msi</c>, the entry executable rather than the stub, no shortcuts,
/// and an ILC scan that looks for the one thing that is not a diagnostic.
/// </para>
/// <para>
/// <b>What a scan can and cannot do is stated rather than implied.</b> It cannot
/// prove the pack behaves; the install → update → rollback cycle at
/// [step 19](../../plan/build-order.md#19-velopack-package-update-roll-back) did
/// that, by hand, against a real installer this suite must never run — an
/// installer renames a non-empty root aside and deletes it, which on a
/// developer's machine is 768 MB of provisioned browsers. What it does do is
/// make a silent edit to any of those four decisions a red build.
/// </para>
/// </remarks>
internal sealed class ReleaseScriptTests
{
    private static string ValidationScript => Path.Combine(RepositoryLayout.Root.FullName, "build", "Test-ReleaseVersion.ps1");

    private static string ReleaseScript => Path.Combine(RepositoryLayout.Root.FullName, "build", "New-Release.ps1");

    /// <summary>An empty channel accepts anything, because there is nothing to be older than.</summary>
    /// <remarks>
    /// The same 404 an unpublished channel returns is what a misconfigured feed
    /// URL returns, so this state is ordinary and has to be expressible.
    /// </remarks>
    [Test]
    public async Task AnEmptyChannelAcceptsTheFirstRelease()
    {
        using var scratch = ScratchDirectory.Create("release-first");
        var (exit, output) = await RunAsync(ValidationScript, "-Manifest", Path.Combine(scratch.Path, "releases.win.json"), "-Version", "0.1.0");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(output.Trim()).IsEqualTo("first");
    }

    /// <summary>A newer version is ordinary and needs nothing said.</summary>
    [Test]
    public async Task AHigherVersionIsMonotonicAndPasses()
    {
        using var scratch = ScratchDirectory.Create("release-monotonic");
        var manifest = await WriteFeedAsync(scratch.Path, "0.9.0");

        var (exit, output) = await RunAsync(ValidationScript, "-Manifest", manifest, "-Version", "0.9.1");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(output.Trim()).IsEqualTo("monotonic");
    }

    /// <summary>
    /// A lower version is refused <b>unless it is stated</b> — and this is the
    /// half that must exist, or rollback is a client-side fiction.
    /// </summary>
    /// <remarks>
    /// <b>Both halves or neither works.</b> <c>AllowVersionDowngrade</c> on the
    /// client makes an older version acceptable; a pipeline rule of *strictly
    /// increasing* makes one impossible to publish. `ExoFabric/UCC` has exactly
    /// that pair and therefore has no rollback at all, in either direction —
    /// which is why the refusal here names the switch instead of being final.
    /// </remarks>
    [Test]
    public async Task ALowerVersionIsRefusedWithoutRollbackRepublishAndAcceptedWithIt()
    {
        using var scratch = ScratchDirectory.Create("release-rollback");
        var manifest = await WriteFeedAsync(scratch.Path, "0.9.0", "0.9.1");

        var (refusedExit, refusedOutput) = await RunAsync(ValidationScript, "-Manifest", manifest, "-Version", "0.9.0");

        await Assert.That(refusedExit).IsNotEqualTo(0);
        await Assert.That(refusedOutput).Contains("ROLLBACK");
        await Assert.That(refusedOutput).Contains("-RollbackRepublish");
        await Assert.That(refusedOutput).Contains("AllowVersionDowngrade");

        var (statedExit, statedOutput) = await RunAsync(ValidationScript, "-Manifest", manifest, "-Version", "0.9.0", "-RollbackRepublish");

        await Assert.That(statedExit).IsEqualTo(0);
        await Assert.That(statedOutput.Trim()).IsEqualTo("rollback");
    }

    /// <summary>Publishing a version over itself is refused in both directions.</summary>
    [Test]
    public async Task RepublishingTheNewestVersionOverItselfIsRefused()
    {
        using var scratch = ScratchDirectory.Create("release-same");
        var manifest = await WriteFeedAsync(scratch.Path, "0.9.1");

        var (exit, output) = await RunAsync(ValidationScript, "-Manifest", manifest, "-Version", "0.9.1", "-RollbackRepublish");

        await Assert.That(exit).IsNotEqualTo(0);
        await Assert.That(output).Contains("already the newest release");
    }

    /// <summary>A four-part version and a <c>0.0.0</c> are both refused.</summary>
    /// <remarks>
    /// <c>vpk</c> rejects four-part versions outright, so catching it here is
    /// the difference between a message and a pack failure two minutes into a
    /// publish. <c>0.0.0</c> is what a derivation that found no git tag
    /// produces.
    /// </remarks>
    [Test]
    public async Task AFourPartVersionAndAZeroVersionAreBothRefused()
    {
        using var scratch = ScratchDirectory.Create("release-shape");
        var manifest = Path.Combine(scratch.Path, "releases.win.json");

        var (fourPartExit, fourPartOutput) = await RunAsync(ValidationScript, "-Manifest", manifest, "-Version", "0.9.1.0");
        await Assert.That(fourPartExit).IsNotEqualTo(0);
        await Assert.That(fourPartOutput).Contains("four-part");

        var (noTagExit, noTagOutput) = await RunAsync(ValidationScript, "-Manifest", manifest, "-Version", "0.0.0-alpha.0.71");
        await Assert.That(noTagExit).IsNotEqualTo(0);
        await Assert.That(noTagOutput).Contains("no git tag");
    }

    /// <summary>
    /// The four packaging decisions with a blast radius, asserted on the script
    /// itself.
    /// </summary>
    /// <remarks>
    /// Each one fails silently if it changes. <c>--msi</c> installs to
    /// <c>Program Files</c> and makes the updater self-elevate, which a
    /// background MCP server cannot answer. The execution stub is
    /// <c>windows_subsystem = "windows"</c> and returns in 59 ms while the app
    /// runs on, so a client registered against it sees its server die instantly.
    /// Shortcuts default to <c>Desktop,StartMenuRoot</c>, which is two entries a
    /// stdio server has no use for. And <i>will always throw</i> is not a
    /// diagnostic, so no MSBuild property can catch it.
    /// </remarks>
    [Test]
    public async Task TheFourPackagingDecisionsAreStillInTheReleaseScript()
    {
        var script = await File.ReadAllTextAsync(ReleaseScript);

        // ⚠️ THE NEGATIVE CHECK IS SCOPED TO THE ARGUMENT ARRAY, and that is not
        // a convenience. The script explains at length WHY --msi is never
        // passed, in a comment block and in a comment inside the array itself,
        // so a whole-file scan matches the explanation and fails -- which trains
        // the next person to delete the reasoning in order to make a test pass.
        // What is asserted is what is passed.
        var start = script.IndexOf("$packArgs = @(", StringComparison.Ordinal);
        await Assert.That(start).IsGreaterThan(-1);

        var end = script.IndexOf("\n)", start, StringComparison.Ordinal);
        await Assert.That(end).IsGreaterThan(start);

        var passed = string.Join(
            '\n',
            script[start..end].Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

        // Never per-machine: --msi PerMachine installs to Program Files and
        // makes the updater self-elevate.
        await Assert.That(passed).DoesNotContain("--msi");

        await Assert.That(passed).Contains("'--mainExe', 'BrowserAI.exe'");
        await Assert.That(passed).Contains("'--shortcuts', 'None'");

        // And nothing anywhere in the script hands start arguments to the
        // installer: `Setup.exe -- <args>` panics with a downcast failure and
        // NEVER EXITS, installing nothing and leaving one log line.
        await Assert.That(script).DoesNotContain("Setup.exe' --");
        await Assert.That(script).Contains("will always throw");
    }

    private static async Task<string> WriteFeedAsync(string directory, params string[] versions)
    {
        var manifest = Path.Combine(directory, "releases.win.json");
        var assets = versions.Select(version =>
            $$"""{"PackageId":"BrowserAI","Version":"{{version}}","Type":"Full","FileName":"BrowserAI-{{version}}-full.nupkg","SHA1":"","SHA256":"","Size":1}""");

        await File.WriteAllTextAsync(manifest, $$"""{"Assets":[{{string.Join(",", assets)}}]}""");
        return manifest;
    }

    private static async Task<(int Exit, string Output)> RunAsync(string script, params string[] arguments)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"'pwsh' did not start for {script}.");

        var captured = new StringBuilder();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        _ = captured.Append(await stdout).Append(await stderr);

        // Cached immediately: Process.ExitCode throws after Dispose(), and this
        // object is disposed on the way out of the using.
        return (process.ExitCode, captured.ToString());
    }
}
