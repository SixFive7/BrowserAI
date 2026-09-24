// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BrowserAI.Runtime;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// T7: a session close starts Playwright's own reaper over Playwright's own
/// server registry, the dead descriptors go, a live browser's stays, and the
/// close does not wait for any of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Driven through the published binary and not the in-process rig</b>, because
/// two of the three things under test only exist out there: the reaper is a
/// detached <c>node</c> process, and the record naming it is written to the
/// <b>machine's process log</b>, which is the file
/// <see cref="ProcessLogRecords"/> reads and which an in-process rig does not
/// have.
/// </para>
/// <para>
/// ⚠️ <b>The registry is isolated by handing the slice
/// <c>PWTEST_SERVER_REGISTRY</c> in its own environment block -- never by setting
/// it on this process.</b> A process-wide scope would put every arm in this file
/// under a keyless <c>[NotInParallel]</c> and, worse, would reach every child
/// every other arm starts. Handing it to the one process under test is what makes
/// this arm ordinary, and it also exercises the forwarding itself end to end: the
/// variable reaches the slice's own children only because it is in
/// <c>ChildEnvironment.InheritedWhenSet</c>, so a tree that stopped forwarding it
/// would leave the scratch directory empty here and would grow the developer's
/// own registry instead.
/// </para>
/// <para>
/// <b>Nothing in this file reads or writes the real
/// <c>%LOCALAPPDATA%\ms-playwright\b</c></b>, which is the discipline the T7
/// probes were run under and the reason those probes are quotable:
/// [evidence](../../docs/evidence/2026-09-23-server-registry/README.md).
/// </para>
/// </remarks>
internal sealed class ServerRegistryReapTests
{
    /// <summary>
    /// How many dead descriptors the arm plants before it closes a session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It is a backlog and not a duration, and it is what makes the
    /// "the close did not wait" assertion an event read rather than a
    /// stopwatch.</b> Reading this registry is quadratic -- 1,000 entries measured
    /// at 9,911 ms for <c>list()</c> plus about 6 s of watcher-ready, against a
    /// close that tears down a Chromium and deletes its profile in a few
    /// ([kb](../../kb/playwright/tools-and-artifacts.md#every-launched-browser-leaves-a-descriptor-in-localappdatams-playwrightb-and-nothing-reaps-it----measured-2026-09-16))
    /// -- so the reaper is provably still working when the caller has its answer,
    /// and no assertion here compares any elapsed time to any number.
    /// </para>
    /// <para>
    /// <b>It is also what makes the reap worth asserting at all:</b> one dead
    /// descriptor would be collected too quickly to tell a working reap from a
    /// file that never existed.
    /// </para>
    /// </remarks>
    private const int StaleDescriptorsPlanted = 1_000;

    /// <summary>Matches the identity <c>ReapLog.Started</c> writes.</summary>
    /// <remarks>
    /// The pair and not the pid: the process log is machine-wide and retained for
    /// thirty days, so a bare number in it is an identity only until Windows
    /// re-uses it.
    /// </remarks>
    private static readonly Regex ReaperIdentity =
        new(@"reaper=(?<pid>\d+)@(?<created>-?\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A destroy reaps every dead descriptor in Playwright's registry, spares the
    /// one belonging to a browser that is still up, and answers its caller while
    /// the reap is still running.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ADestroyReapsTheDeadDescriptorsSparesALiveOneAndDoesNotWaitForEither()
    {
        SuiteEnvironment.RequirePublishedSlice();
        SuiteEnvironment.RequireProvisionedChromium();

        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create("t7-reap");

        var registry = Path.Combine(scratch.Path, "registry");
        _ = Directory.CreateDirectory(registry);

        var environment = PublishedSlice.InheritedEnvironment();
        environment[ServerRegistryReap.RegistryDirectoryVariable] = registry;

        await using var client = RawStdioClient.Start(PublishedSlice.Executable, [], scratch.Path, environment);

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);

        // Two sessions with a real browser each: one to close, and one whose
        // browser is alive throughout and whose descriptor must therefore survive
        // a reap that unlinks a thousand others.
        var keeper = Path.Combine(scratch.Path, "keeper");
        var closing = Path.Combine(scratch.Path, "closing");

        await OpenWithABrowserAsync(client, keeper, "the session whose browser stays up across a reap");
        await OpenWithABrowserAsync(client, closing, "the session whose close starts the reap");

        // ⚠️ The forwarding, asserted before anything is planted. Each child wrote
        // its own descriptor here, which it can only have done by being handed the
        // variable this process gave the slice.
        await Assert.That(DescriptorFor(registry, keeper)).IsNotNull();
        await Assert.That(DescriptorFor(registry, closing)).IsNotNull();

        var planted = PlantDeadDescriptors(registry, StaleDescriptorsPlanted);

        await Assert.That(planted.Count(File.Exists)).IsEqualTo(StaleDescriptorsPlanted);

        var destroyed = await client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browserai_destroy",
            ["arguments"] = new JsonObject
            {
                ["directory"] = closing,
                ["why"] = "the suite closing a session so the reap it starts can be watched",
            },
        });

        await Assert.That((bool?)destroyed["isError"]).IsNotEqualTo(true);

        // The record, read out of the machine's process log and scoped to this
        // slice's own identity -- never the whole file, which every BrowserAI on
        // this machine appends to.
        var records = ProcessLogRecords.For(client.ProcessId, ProcessIdentity.CreationTimeOf(client.ProcessId));
        var reaper = ReaperIdentity.Match(records);

        await Assert.That(reaper.Success)
            .IsTrue()
            .Because($"the close should have recorded the reaper it started, and the slice's records are:{Environment.NewLine}{records}");

        var reaperPid = int.Parse(reaper.Groups["pid"].Value, CultureInfo.InvariantCulture);
        var reaperCreated = long.Parse(reaper.Groups["created"].Value, CultureInfo.InvariantCulture);

        await Assert.That(reaperCreated)
            .IsNotEqualTo(0)
            .Because("a pid without its creation time is not an identity, and @0 means the launcher could not read one");

        // ⚠️ THE ORDERING, READ OUT OF THE LOG. The reap is recorded before the
        // destroy's own record, so it was started inside the close -- and the
        // close went on to finish afterwards.
        await Assert.That(RecordIndex(records, "server-registry reap"))
            .IsLessThan(RecordIndex(records, "Session destroyed at"))
            .Because($"the reap should be started during the close and not after it, and the records are:{Environment.NewLine}{records}");

        // ⚠️ AND THE HALF THE LOG CANNOT SHOW: the caller has its answer while
        // the reaper is still working. This is an event read and not a timing
        // assertion -- see StaleDescriptorsPlanted for why the window is wide by
        // construction.
        await Assert.That(ProcessIdentity.IsAlive(reaperPid, reaperCreated))
            .IsTrue()
            .Because("the destroy answered its caller, so it cannot have waited for a reap over a thousand descriptors");

        await Assert.That(ProcessIdentity.WaitUntilGone(reaperPid, reaperCreated, TestDefaults.ProcessHang))
            .IsTrue()
            .Because("the reaper is one call to upstream's list() and then an exit; a reaper that never exits is the hang this bound is for");

        // What it reaped: every planted descriptor, and the closed session's own,
        // whose browser went down with it.
        var survivors = planted.Where(File.Exists).ToList();

        await Assert.That(survivors.Count)
            .IsEqualTo(0)
            .Because($"the reap should have unlinked every dead descriptor, and {survivors.Count} of {StaleDescriptorsPlanted} are still there");

        await Assert.That(DescriptorFor(registry, closing))
            .IsNull()
            .Because("the closed session's browser is gone, so its descriptor is one of the dead ones");

        // And what it spared, which is the accepted risk turned into an
        // assertion: a live browser's descriptor is not collateral.
        await Assert.That(DescriptorFor(registry, keeper))
            .IsNotNull()
            .Because("the keeper's browser was up throughout, so unlinking its descriptor would be a browser lost from every list");

        // The session behind the spared descriptor is still a session, which is
        // the half a file on disk cannot say.
        var again = await client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject
            {
                ["url"] = SliceRun.TargetUrl,
                ["session"] = keeper,
                ["why"] = "the suite checking the spared session still answers after a reap",
            },
        });

        await Assert.That((bool?)again["isError"]).IsNotEqualTo(true);
    }

    /// <summary>
    /// The script is upstream's reaper against the module it was given, with a
    /// Windows path spelled the one way a JavaScript literal reads back.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheScriptRequiresTheModuleItWasGivenAndCallsNothingElse()
    {
        var script = ServerRegistryReap.ScriptFor(@"C:\Users\someone\AppData\Local\BrowserAI.app\current\payload\mcp\node_modules\playwright-core\lib\serverRegistry.js");

        await Assert.That(script).Contains("require('C:/Users/someone/AppData/Local/BrowserAI.app/current/payload/mcp/node_modules/playwright-core/lib/serverRegistry.js')");
        await Assert.That(script).Contains("serverRegistry.list()");

        // Nothing else of upstream is touched: the CLI client's `list` command
        // would also prune the daemon session registry of whatever workspace it
        // resolved from its working directory.
        await Assert.That(script).DoesNotContain("cli-client");
        await Assert.That(script).DoesNotContain(@"\");

        // An apostrophe in a user's profile name is the one character left to get
        // wrong once the backslashes are gone.
        var quoted = ServerRegistryReap.ScriptFor(@"C:\Users\O'Brien\payload\lib\serverRegistry.js");

        await Assert.That(quoted).Contains(@"require('C:/Users/O\'Brien/payload/lib/serverRegistry.js')");
    }

    /// <summary>Opens one session through the slice and binds a real browser to it.</summary>
    /// <param name="client">The slice under test.</param>
    /// <param name="directory">Where the session goes.</param>
    /// <param name="purpose">What it is for.</param>
    /// <returns>The task.</returns>
    private static async Task OpenWithABrowserAsync(RawStdioClient client, string directory, string purpose)
    {
        var opened = await client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browserai_init",
            ["arguments"] = new JsonObject
            {
                ["directory"] = directory,
                ["purpose"] = purpose,
            },
        });

        await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true);

        // ⚠️ The navigate is what BINDS the browser, and binding is what writes
        // the descriptor: Playwright creates the browser lazily on first use, so
        // an init alone leaves this registry empty and the reap with nothing to
        // find.
        var navigated = await client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject
            {
                ["url"] = SliceRun.TargetUrl,
                ["session"] = directory,
                ["why"] = "the suite binding a browser so Playwright writes its descriptor",
            },
        });

        await Assert.That((bool?)navigated["isError"]).IsNotEqualTo(true);
    }

    /// <summary>
    /// The descriptor in <paramref name="registry"/> belonging to the browser of
    /// one session, or <see langword="null"/> when there is none.
    /// </summary>
    /// <remarks>
    /// Matched on the profile path inside the descriptor, which is the session's
    /// own directory: the file name is a guid, and this suite runs several
    /// browsers at once.
    /// </remarks>
    /// <param name="registry">The scratch registry directory.</param>
    /// <param name="session">The session directory.</param>
    /// <returns>The descriptor's path, or <see langword="null"/>.</returns>
    private static string? DescriptorFor(string registry, string session)
    {
        foreach (var file in Directory.EnumerateFiles(registry))
        {
            JsonNode? descriptor;

            try
            {
                descriptor = JsonNode.Parse(File.ReadAllText(file));
            }
            catch (Exception failure) when (failure is IOException or System.Text.Json.JsonException)
            {
                // A descriptor being written or unlinked while this reads. It is
                // not the one being looked for either way: the reap has already
                // exited wherever this is asserted.
                continue;
            }

            var profile = (string?)descriptor?["browser"]?["userDataDir"];

            if (profile is not null && profile.StartsWith(session, StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        return null;
    }

    /// <summary>
    /// Plants descriptors shaped like upstream's own, each naming a pipe nothing
    /// serves.
    /// </summary>
    /// <remarks>
    /// <b>The file name is the guid inside it</b>, because that is how upstream
    /// finds the file again: the watcher keys its map on the file name and
    /// <c>list()</c> unlinks <c>descriptor.browser.guid</c>. A plant whose two
    /// disagreed would be reaped by name and leave the file, which reads as a
    /// reaper that does not work.
    /// </remarks>
    /// <param name="registry">The scratch registry directory.</param>
    /// <param name="count">How many to plant.</param>
    /// <returns>The paths planted.</returns>
    private static List<string> PlantDeadDescriptors(string registry, int count)
    {
        var planted = new List<string>(count);

        for (var i = 0; i < count; i++)
        {
            var guid = $"browser@{Guid.NewGuid():N}";
            var file = Path.Combine(registry, guid);

            var descriptor = new JsonObject
            {
                ["playwrightVersion"] = "planted-by-the-suite",
                ["playwrightLib"] = Path.Combine(registry, "no-such-lib"),
                ["title"] = "BrowserAI",
                ["browser"] = new JsonObject
                {
                    ["guid"] = guid,
                    ["browserName"] = "chromium",
                    ["userDataDir"] = Path.Combine(registry, "no-such-profile", guid),
                },

                // Dead, and dead in the way a real one is: the browser it named
                // has gone, so the pipe is simply not there any more.
                ["endpoint"] = $@"\\.\pipe\pw-planted-{guid}",
            };

            File.WriteAllText(file, descriptor.ToJsonString());
            planted.Add(file);
        }

        return planted;
    }

    /// <summary>Where a record first appears in one process's records.</summary>
    /// <param name="records">The records, as <see cref="ProcessLogRecords"/> returns them.</param>
    /// <param name="needle">Something only that record says.</param>
    /// <returns>The character index, or <see cref="int.MaxValue"/> when it is absent.</returns>
    private static int RecordIndex(string records, string needle)
    {
        var at = records.IndexOf(needle, StringComparison.Ordinal);

        // MaxValue and not -1: an absent record must FAIL an ordering assertion
        // rather than satisfy it by sorting first.
        return at < 0 ? int.MaxValue : at;
    }
}
