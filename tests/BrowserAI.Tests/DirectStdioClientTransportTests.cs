// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Protocol;
using BrowserAI.Tests.Harness;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Tests;

/// <summary>
/// The client transport, driven against a real child process.
/// </summary>
/// <remarks>
/// Every assertion here is aimed at a failure that reports healthy: a shell
/// nobody can see, an inherited variable that overrides a generated config key,
/// an exit code that throws when read, and a startup error on stderr that
/// arrived before anything was listening.
/// </remarks>
internal sealed class DirectStdioClientTransportTests
{
    /// <summary>
    /// What a child writes to stderr before it does anything else, so that a
    /// handler attached a moment too late loses them.
    /// </summary>
    private static readonly string[] TheFiveStderrLines =
    [
        "probe-stderr#0",
        "probe-stderr#1",
        "probe-stderr#2",
        "probe-stderr#3",
        "probe-stderr#4",
    ];

    /// <summary>
    /// One character from each class the SDK's server transport escapes, so the
    /// same payload proves the point on both sides of the proxy.
    /// </summary>
    private const string AwkwardText = "back`tick it's <angled> café — ünïcødé";

    [Test]
    public async Task TheChildsDirectParentIsThisProcess()
    {
        await using var child = await ProbeChild.StartAsync("client-parent");

        // The pid the CHILD reports about itself, not the one the transport
        // recorded. That distinction is the entire test, and it was got wrong
        // once: asserting on the transport's own pid passes with `cmd.exe /c`
        // in front, because the shell is then the process the transport
        // spawned and BrowserAI really is its parent. Measured by planting the
        // wrapping, 2026-08-16 -- the assertion that was supposed to catch it
        // did not, and a second, weaker one did.
        await Assert.That(ParentProcess.IdOf(child.ReportedProcessId)).IsEqualTo(Environment.ProcessId);

        // With that established, the process the transport owns is the process
        // that is really there -- which is what makes its exit code, its
        // stderr and, at step 6, its job object refer to the right thing.
        await Assert.That(child.ReportedProcessId).IsEqualTo(child.Session.ProcessId);
    }

    [Test]
    [NotInParallel(nameof(TheChildsEnvironmentIsExactlyTheAllowlist))]
    public async Task TheChildsEnvironmentIsExactlyTheAllowlist()
    {
        // Planted in THIS process, which is what makes the assertion mean
        // something: an allowlist that forgets to Clear() first passes every
        // test written against a host that happened not to have these set.
        var planted = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["INIT_CWD"] = @"C:\some\npm\ancestor",
            ["NODE_OPTIONS"] = "--max-old-space-size=4096",
            ["NODE_PATH"] = @"C:\some\modules",
            ["DEBUG"] = "pw:*",
            ["DEBUG_FILE"] = @"C:\debug.log",
            ["PLAYWRIGHT_MCP_OUTPUT_MAX_SIZE"] = "1024",
            ["PLAYWRIGHT_DOWNLOAD_HOST"] = "https://mirror.invalid",
            ["PLAYWRIGHT_CHROMIUM_DOWNLOAD_HOST"] = "https://mirror.invalid",
            ["PLAYWRIGHT_FIREFOX_DOWNLOAD_HOST"] = "https://mirror.invalid",
            ["PLAYWRIGHT_WEBKIT_DOWNLOAD_HOST"] = "https://mirror.invalid",
            ["PLAYWRIGHT_SKIP_VALIDATE_HOST_REQUIREMENTS"] = "1",
            ["BROWSERAI_UNLISTED_VARIABLE"] = "should not survive",
        };

        var original = planted.Keys.ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var (name, value) in planted)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            await using var child = await ProbeChild.StartAsync("client-environment");
            var environment = child.Environment;

            foreach (var name in planted.Keys)
            {
                await Assert.That(environment.ContainsKey(name)).IsFalse();
            }

            await Assert.That(environment["PLAYWRIGHT_SKIP_BROWSER_GC"]).IsEqualTo("1");
            await Assert.That(environment["PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD"]).IsEqualTo("1");

            // Nothing beyond the allowlist reaches the child, which is the
            // stronger statement and the one that survives upstream adding a
            // forty-third PLAYWRIGHT_MCP_* variable.
            var unexpected = environment.Keys
                .Where(name => !ChildEnvironment.InheritedWhenSet.Contains(name) && !ChildEnvironment.Forced.ContainsKey(name))
                .Order(StringComparer.Ordinal)
                .ToList();

            await Assert.That(string.Join(", ", unexpected)).IsEmpty();
        }
        finally
        {
            foreach (var (name, value) in original)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    [Test]
    public async Task TheChildIsGivenTheWorkingDirectoryItWasTold()
    {
        await using var child = await ProbeChild.StartAsync("client-cwd");

        // Left unset, .NET passes null to CreateProcess and the child silently
        // inherits the test host's directory -- which for a real child decides
        // where every relative path lands.
        var reported = (string)child.Report["workingDirectory"]!;
        await Assert.That(reported).IsNotEqualTo(Environment.CurrentDirectory);
    }

    [Test]
    public async Task EveryStderrLineWrittenBeforeAnyWorkReachesTheSink()
    {
        var lines = new ConcurrentQueue<string>();

        await using var child = await ProbeChild.StartAsync(
            "client-stderr",
            standardErrorLineCount: 5,
            standardErrorLines: lines.Enqueue);

        // The child writes these before it writes its report, so by the time
        // StartAsync returns they are already in the pipe. What is being proven
        // is that the handler was attached before Start(): five lines a child
        // wrote while failing to launch are the only explanation there will be.
        var deadline = Stopwatch.StartNew();

        while (lines.Count < 5 && deadline.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(25);
        }

        await Assert.That(lines.Order(StringComparer.Ordinal))
            .IsEquivalentTo(TheFiveStderrLines);
    }

    [Test]
    public async Task AFrameReachesTheChildUnescapedAndComesBack()
    {
        await using var child = await ProbeChild.StartAsync("client-roundtrip");

        await child.SendAsync(new JsonRpcRequest
        {
            Id = new RequestId(7),
            Method = "probe/echo",
            Params = new JsonObject { ["text"] = AwkwardText },
        });

        var echoed = await child.ReceiveAsync();

        await Assert.That(echoed).IsTypeOf<JsonRpcRequest>();
        await Assert.That(((JsonRpcRequest)echoed).Params!["text"]!.GetValue<string>()).IsEqualTo(AwkwardText);

        // Downward as well as upward. The SDK's own client session transport
        // would have sent \u0060 and \u00e9 here; the bytes the child actually
        // received say which happened.
        var received = child.ReceivedFrameBytes();
        await Assert.That(Contains(received, Encoding.UTF8.GetBytes(AwkwardText))).IsTrue();
        await Assert.That(Contains(received, "\\u0060"u8)).IsFalse();
    }

    [Test]
    public async Task TheExitCodeIsStillReadableAfterTheChildAndItsProcessAreGone()
    {
        var child = await ProbeChild.StartAsync("client-exitcode");

        using (var victim = Process.GetProcessById(child.Session.ProcessId))
        {
            victim.Kill();
            await victim.WaitForExitAsync();
        }

        await child.DisposeAsync();

        // Cached as an int the moment it existed. Read from the Process object
        // instead, this is the point at which the answer becomes an exception
        // -- see the test below, which proves that rather than assuming it.
        await Assert.That(child.Session.ExitCode).IsNotNull();
    }

    [Test]
    public async Task ProcessExitCodeThrowsAfterDisposeWhichIsWhyTheSessionCachesIt()
    {
        // The hazard, reproduced rather than quoted. If a future .NET made
        // Process.ExitCode survive disposal, this test says so and the caching
        // becomes belt and braces instead of load bearing.
        var process = Process.Start(new ProcessStartInfo(
            Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
        })!;

        await process.WaitForExitAsync();

        var cached = process.ExitCode;
        await Assert.That(cached).IsEqualTo(2);

        process.Dispose();

        _ = Assert.Throws<InvalidOperationException>(() => _ = process.ExitCode);
    }

    private static bool Contains(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle) =>
        haystack.IndexOf(needle) >= 0;
}
