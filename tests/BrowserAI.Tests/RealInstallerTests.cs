// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using BrowserAI.Hosting;
using BrowserAI.Registration;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The real installer, run twice over its own install root, against a data root
/// that must survive both.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one thing the structural scan cannot say.</b>
/// <c>UpdateTests.NoDataPathResolvesUnderAnyInstallRoot</c> holds that BrowserAI
/// never composes a data path under an install root; it says nothing about what
/// <c>Setup.exe</c> actually does to a directory, and the whole preservation
/// decision rests on what it does: <c>install.rs</c> renames a non-empty root to
/// <c>{root}.{random16}</c> and, on success, <b>deletes it</b>. That is not a
/// failure path — it is what a repair install, an overwrite install and a
/// re-run of the same installer all do, and until 2026-09-15 it took 768 MB of
/// provisioned browsers and the session index with it every time.
/// </para>
/// <para>
/// <b>The second install is the test; the first one only creates something to
/// destroy.</b> So the arm asserts a positive control from Velopack's own log —
/// <c>Renaming existing directory</c> — because an installer that quietly
/// skipped the destructive branch would leave the markers alone for the wrong
/// reason and report exactly the same pass.
/// </para>
/// <para>
/// ⚠️ <b>Everything it touches is scratch, and the sandbox is not a
/// convenience.</b> The hooks inherit this process's environment, so
/// <c>CLAUDE_CONFIG_DIR</c> points the registration at a scratch configuration
/// directory — without it the install hook would rewrite the maintainer's own
/// <c>~/.claude.json</c> and the uninstall hook would then remove the entry it
/// found there. <c>BROWSERAI_ROOT</c> does the same for the data root, so the
/// markers this plants are in a directory of its own rather than in the real
/// one. It installs only under <c>--installto</c>, and it uninstalls what it
/// installed.
/// </para>
/// <para>
/// <b>It shares the MCP client's serialisation key</b>
/// (<see cref="RegistrationTests.ClientGroup"/>), because the hooks start the
/// real client and that variable is process-wide.
/// </para>
/// </remarks>
internal sealed class RealInstallerTests
{
    /// <summary>
    /// A second install over the same root destroys the install root and leaves
    /// the data root byte-identical.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    [NotInParallel(RegistrationTests.ClientGroup)]
    public async Task InstallingTwiceOverOneRootLeavesTheDataRootByteIdentical()
    {
        var setup = SuiteEnvironment.RequireReleaseInstaller();

        using var installRoot = ScratchDirectory.CreateUnderProfile("real-install");
        using var dataRoot = ScratchDirectory.CreateUnderProfile("real-install-data");
        using var clientConfig = ScratchDirectory.Create("real-install-client");
        using var logs = ScratchDirectory.Create("real-install-logs");

        var paths = new LocalAppDataPaths(dataRoot.Path);
        var planted = Plant(paths);

        using var sandbox = new EnvironmentScope(new Dictionary<string, string?>
        {
            [RegistrationTests.ConfigDirectoryVariable] = clientConfig.Path,
            [BrowserAiPaths.AppRootOverride] = dataRoot.Path,
        });

        // ⚠️ TWICE. The first install creates a non-empty root; the second one is
        // the one that renames it aside and deletes it.
        var first = await RunAsync(setup, ["--silent", "--log", Path.Combine(logs.Path, "setup-1.log"), "--installto", installRoot.Path]);
        var second = await RunAsync(setup, ["--silent", "--log", Path.Combine(logs.Path, "setup-2.log"), "--installto", installRoot.Path]);

        await Assert.That(first).IsEqualTo(0);
        await Assert.That(second).IsEqualTo(0);

        // The positive control, out of Velopack's own log: the destructive
        // branch RAN. Without this, an installer that refused the second install
        // would leave the markers alone and pass.
        var setupLog = await File.ReadAllTextAsync(Path.Combine(logs.Path, "setup-2.log"));

        await Assert.That(setupLog).Contains("Renaming existing directory");

        // The install root really was replaced, and the binary is where a client
        // is pointed: <root>\current\BrowserAI.exe, never the stub beside it.
        await Assert.That(File.Exists(Path.Combine(installRoot.Path, RegistrationTarget.CurrentDirectoryName, "BrowserAI.exe"))).IsTrue();

        await Unchanged(planted);

        // The hook wrote its record into the DATA root, which is the other half
        // of the same decision: it is still there after an install that deleted
        // the install root twice over.
        await Assert.That(File.Exists(RegistrationRecord.PathFor(dataRoot.Path))).IsTrue();

        // ---- And uninstall, in the same sandbox --------------------------------
        var update = Path.Combine(installRoot.Path, "Update.exe");

        await Assert.That(File.Exists(update)).IsTrue();

        var removed = await RunAsync(update, ["--uninstall", "--silent"]);

        await Assert.That(removed).IsEqualTo(0);

        // Velopack schedules the install root's own removal for after Update.exe
        // exits, so the directory goes a moment later than the process does.
        await WaitOutTheDeferredRemoval(Path.Combine(installRoot.Path, RegistrationTarget.CurrentDirectoryName));

        // ⚠️ THE CLAIM. A silent uninstall keeps the data root, and every byte of
        // it is the byte that was planted.
        await Unchanged(planted);
    }

    /// <summary>Plants files whose content is their own name, and records their hashes.</summary>
    /// <param name="paths">The scratch data seam.</param>
    /// <returns>Each planted path and the SHA-256 it held.</returns>
    private static List<(string Path, string Sha256)> Plant(LocalAppDataPaths paths)
    {
        var planted = new List<(string Path, string Sha256)>();

        foreach (var (directory, name) in new[]
        {
            (paths.BrowsersDirectory, "chromium-0000.marker"),
            (paths.IndexDirectory, "0123456789abcdef.json"),
            (paths.LogDirectory, "browserai-planted.log"),
            (paths.RootAppDir, "a-file-at-the-root.txt"),
        })
        {
            _ = Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, name);

            File.WriteAllText(path, $"planted by the suite at {DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)} — {path}");
            planted.Add((path, Hash(path)));
        }

        return planted;
    }

    private static async Task Unchanged(IReadOnlyList<(string Path, string Sha256)> planted)
    {
        var moved = new List<string>();

        foreach (var (path, sha) in planted)
        {
            if (!File.Exists(path))
            {
                moved.Add($"{path} is gone");
                continue;
            }

            var now = Hash(path);

            if (!string.Equals(now, sha, StringComparison.Ordinal))
            {
                moved.Add($"{path} was {sha} and is now {now}");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, moved)).IsEmpty();
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static async Task<int> RunAsync(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,

            // The house rule for every launch in this tree: redirecting the
            // streams does not suppress the console, and from a windowless test
            // host each omission puts a terminal on the maintainer's screen.
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;

        // Both streams are drained before the wait, which is the pairing the
        // banned timed overload exists to protect: a process whose output is not
        // read can fill a pipe and never exit.
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync().WaitAsync(TestDefaults.ProcessHang);
        _ = await output;
        _ = await error;

        return process.ExitCode;
    }

    /// <summary>
    /// Waits for Velopack's own deferred removal of the install root.
    /// </summary>
    /// <remarks>
    /// <b>A hang detector, not a promptness claim.</b> <c>Update.exe</c> cannot
    /// delete the directory it is running out of, so it schedules the removal and
    /// exits; the bound is <see cref="TestDefaults.ProcessHang"/> because what is
    /// being waited for is another process's teardown, and the only number that
    /// would be wrong here is one invented at this line.
    /// </remarks>
    /// <param name="directory">The directory that must go.</param>
    private static async Task WaitOutTheDeferredRemoval(string directory)
    {
        var deadline = DateTime.UtcNow + TestDefaults.ProcessHang;

        while (Directory.Exists(directory) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
        }
    }
}
