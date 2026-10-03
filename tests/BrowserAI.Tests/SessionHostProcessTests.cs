// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text.Json.Nodes;
using BrowserAI.Interop;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The session host and the front that relays to it, as published binaries against
/// a real Chromium: a session outlives the front that opened it, and the host's own
/// death takes every child and browser with it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q366 b, the maintainer's words of 2026-10-03, verbatim:</b> <i>"If the server
/// crashes and the coordinator loses the pipe, keep the browser around with the
/// already running activity timeout timer active. This allows restarting vscode, the
/// claude code plugin or soemthing without losing the state."</i> And P7: <i>"it all
/// needs to be done in a super safe way so we don't permanently leak stuff."</i>
/// </para>
/// <para>
/// <b>A front is ended the way Codex ends a server</b>: its job is closed, which
/// terminates it with no warning. The host is in a job of its own, as it is in the
/// coordinator's, so nothing done to the front can reach it.
/// </para>
/// </remarks>
internal sealed class SessionHostProcessTests
{
    /// <summary>
    /// A session outlives a front that is killed, and the next front finds its page
    /// exactly as it was left: no restore, no new browser.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASessionOutlivesAFrontThatIsKilledAndTheNextFrontFindsItsPageAsItWasLeft()
    {
        SuiteEnvironment.RequirePublishedSlice();
        SuiteEnvironment.RequireProvisionedChromium();
        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create("session-host-front");

        var pipe = UniquePipe();
        var marker = $"KEPT-{Guid.NewGuid():N}";
        var session = Path.Combine(scratch.Path, "kept-across-a-front");

        await using var host = StartHost(pipe, scratch.Path);

        await WaitForTheHostAsync(pipe, host);

        // ---- The first front: open the session and leave a page in it.
        var first = StartFront(pipe, scratch.Path);

        try
        {
            _ = await first.InitializeAsync(SliceRun.OfferedProtocolVersion);

            await CallOkAsync(first, SessionToolSurface.Init, new JsonObject
            {
                ["directory"] = session,
                ["purpose"] = "the session host arm's session, kept across a front's death",
            });

            await CallOkAsync(first, "browser_navigate", new JsonObject
            {
                ["url"] = "data:text/html," + Uri.EscapeDataString($"<title>{marker}</title><h1>{marker}</h1><input id=typed value=left-as-it-was>"),
                ["session"] = session,
                ["why"] = "the suite leaving a page in a session whose front it is about to kill",
            });
        }
        finally
        {
            // ⚠️ THE FRONT DIES THE WAY CODEX ENDS A SERVER: its job is closed and it
            // is terminated, with nothing sent and nothing closed first.
            await first.DisposeAsync();
        }

        // ---- The next front: the page is still there.
        await using var second = StartFront(pipe, scratch.Path);

        _ = await second.InitializeAsync(SliceRun.OfferedProtocolVersion);

        var snapshot = await CallOkAsync(second, "browser_snapshot", new JsonObject
        {
            ["session"] = session,
            ["why"] = "the suite's next front reading the page the dead front left",
        });

        await Assert.That(TextOf(snapshot)).Contains(marker)
            .Because("the browser the first front drove is the one the second front reads, page and all");
        await Assert.That(TextOf(snapshot)).Contains("left-as-it-was");

        _ = await second.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = SessionToolSurface.Destroy,
            ["arguments"] = new JsonObject
            {
                ["directory"] = session,
                ["why"] = "the suite cleaning up its session",
            },
        });
    }

    /// <summary>
    /// Killing the session host leaves none of the children and browsers it started
    /// running, while the job the suite put the host in is still open.
    /// </summary>
    /// <remarks>
    /// <b>Two things end them, and this arm cannot tell them apart.</b> Each session's
    /// child is in a kill-on-close job whose only handle dies with the host; and the
    /// host's death ends the child's stdin, on which <c>@playwright/mcp</c> closes its
    /// browser and exits. Planted on 2026-10-03 with every such job created without
    /// kill-on-close, it stayed green, through the second. The first is held by
    /// <c>JobContainmentTests</c>, whose probe tree outlives its launcher unless the job
    /// ends it, and the coordinator's own job by
    /// <c>SessionHostCoordinatorTests.TheKeeperStartsOneHostInAJobOfItsOwnAndClosingItEndsTheHostAndWhatItStarted</c>.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task KillingTheSessionHostTakesEveryChildAndBrowserItStartedWithIt()
    {
        SuiteEnvironment.RequirePublishedSlice();
        SuiteEnvironment.RequireProvisionedChromium();
        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create("session-host-death");

        var pipe = UniquePipe();
        var session = Path.Combine(scratch.Path, "dies-with-its-host");

        await using var host = StartHost(pipe, scratch.Path);

        await WaitForTheHostAsync(pipe, host);

        await using (var front = StartFront(pipe, scratch.Path))
        {
            _ = await front.InitializeAsync(SliceRun.OfferedProtocolVersion);

            await CallOkAsync(front, SessionToolSurface.Init, new JsonObject
            {
                ["directory"] = session,
                ["purpose"] = "the session host arm's session, which dies with its host",
            });

            await CallOkAsync(front, "browser_navigate", new JsonObject
            {
                ["url"] = SliceRun.TargetUrl,
                ["session"] = session,
                ["why"] = "the suite bringing a browser up for the host to take down with it",
            });
        }

        // Everything the host started is in the job the suite put it in, nested
        // there through its own jobs. More than the host and its console host means a
        // node child and a browser are up.
        var before = host.JobProcessIds();

        await Assert.That(before.Count).IsGreaterThan(2).Because("a session's node child and its browser should be up");

        ProcessIdentity.Terminate(host.ProcessId, ProcessIdentity.CreationTimeOf(host.ProcessId));

        // The suite's own job is still open, so only the host's own jobs, whose
        // handles died with it, can have ended the rest.
        var waited = Stopwatch.StartNew();

        while (host.JobProcessIds().Count is not 0)
        {
            if (waited.Elapsed > TestDefaults.ProcessHang)
            {
                throw new TimeoutException(
                    $"The session host was killed and these processes of its tree were still alive {waited.Elapsed.TotalSeconds:F1} s later: "
                    + string.Join(", ", host.JobProcessIds()));
            }

            await Task.Delay(100);
        }
    }

    /// <summary>A pipe name no other arm and no installed host uses.</summary>
    /// <returns>The full pipe name.</returns>
    private static string UniquePipe() => $@"\\.\pipe\BrowserAI-Host-suite-{Guid.NewGuid():N}";

    /// <summary>Starts the published binary as a session host, in a job of the suite's.</summary>
    /// <param name="pipe">The pipe it serves.</param>
    /// <param name="workingDirectory">Its working directory.</param>
    /// <returns>The host, whose disposal closes its job.</returns>
    private static RawStdioClient StartHost(string pipe, string workingDirectory) =>
        RawStdioClient.Start(
            PublishedSlice.Executable,
            ["--host", pipe],
            workingDirectory,
            PublishedSlice.InheritedEnvironment());

    /// <summary>Starts the published binary as a front relaying to a host, in a job of its own.</summary>
    /// <param name="pipe">The host's pipe.</param>
    /// <param name="workingDirectory">Its working directory.</param>
    /// <returns>The front, whose disposal closes its job.</returns>
    private static RawStdioClient StartFront(string pipe, string workingDirectory) =>
        RawStdioClient.Start(
            PublishedSlice.Executable,
            ["--relay", pipe],
            workingDirectory,
            PublishedSlice.InheritedEnvironment());

    /// <summary>Waits for a host's pipe to take a connection, failing only on a hang or a host that died.</summary>
    /// <param name="pipe">The pipe.</param>
    /// <param name="host">The host, whose early death is reported with its stderr.</param>
    /// <returns>The wait.</returns>
    private static async Task WaitForTheHostAsync(string pipe, RawStdioClient host)
    {
        var waited = Stopwatch.StartNew();

        while (!NamedPipes.WaitForFreeInstance(pipe, 0))
        {
            if (host.ExitCode is { } code)
            {
                throw new InvalidOperationException($"The session host exited {code} before it served its pipe: {host.StandardErrorSoFar()}");
            }

            if (waited.Elapsed > TestDefaults.ProcessHang)
            {
                throw new TimeoutException($"The session host never served {pipe}: {host.StandardErrorSoFar()}");
            }

            await Task.Delay(100);
        }
    }

    /// <summary>Calls a tool and requires an answer that is not an error.</summary>
    /// <param name="client">The front.</param>
    /// <param name="tool">The tool.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>The result.</returns>
    private static async Task<JsonObject> CallOkAsync(RawStdioClient client, string tool, JsonObject arguments)
    {
        var result = await client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

        if ((bool?)result["isError"] is true)
        {
            throw new InvalidOperationException($"'{tool}' was refused: {TextOf(result)}{Environment.NewLine}{client.StandardErrorSoFar()}");
        }

        return result;
    }

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));
}
