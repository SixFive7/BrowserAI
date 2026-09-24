// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using BrowserAI.Hosting;
using BrowserAI.Proxy;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// What a <b>real</b> client does when BrowserAI goes away and comes back, driven
/// against the published binary.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the half <c>StaleToolListTests</c> cannot claim.</b> That file
/// drives a stub client and establishes that BrowserAI refuses once, with the
/// right sentence, and sends the notification. Whether a real client re-dials at
/// all, whether the sentence reaches the model, and whether the retry it asks for
/// then works are facts about somebody else's binary, and only that binary can
/// answer them.
/// </para>
/// <para>
/// <b>The maintainer's own ask, 2026-09-24, verbatim:</b> <i>"MAke sure to test
/// Q261 from a subagent once implemented and make sure to have test coverage."</i>
/// The measurement he asked for was taken by hand through
/// <c>docs/probes/2026-09-24-q261</c> and is in
/// <c>docs/evidence/2026-09-24-q261</c>; these two arms are the part of it a run
/// can repeat.
/// </para>
/// <para>
/// ⚠️ <b>NOTHING HERE TOUCHES THE MAINTAINER'S OWN STATE, and each half of that
/// is a deliberate override.</b> <c>CLAUDE_CONFIG_DIR</c> is a scratch directory
/// seeded through <see cref="OnboardedClientConfig"/>; <c>CODEX_HOME</c> is a
/// scratch directory with a <c>config.toml</c> this file writes; the model is a
/// local HTTP stub, so <b>no credential is used and no inference happens
/// anywhere</b>; and <see cref="BrowserAiPaths.AppRootOverride"/> points the
/// server BrowserAI's own data root at <see cref="ScratchRoot.ProfileScratch"/>.
/// </para>
/// <para>
/// ⚠️ <b>The app-root override is not tidiness -- it is the one thing that keeps
/// these arms off another agent's browsers.</b> <c>Program.Main</c> starts the
/// stray sweep in the background at startup and that sweep is machine-wide by
/// design: it hunts browsers belonging to <i>any</i> session under the app root it
/// was given. Left at the default, an arm here would sweep the developer's own.
/// The override is set on the CHILD's environment and never on this process's, so
/// no other arm can inherit it -- which is the defect
/// <see cref="HouseRuleTests.EveryArmInAFileThatOverridesTheEnvironmentRunsBesideNothing"/>
/// exists for, avoided and not serialised around.
/// </para>
/// <para>
/// <b>The tool both arms call is <c>browserai_list</c>, and the choice is
/// load-bearing.</b> It resolves no session, starts no browser and provisions
/// nothing, so neither arm can trip a 200 MB download or leave a profile behind --
/// and it is still an ordinary <c>tools/call</c>, which is all Q261's door looks
/// at.
/// </para>
/// </remarks>
internal sealed class ClientReconnectTests
{
    /// <summary>Where the rig this file drives lives.</summary>
    private static string Rig { get; } =
        Path.Combine(RepositoryLayout.Root.FullName, "docs", "probes", "2026-09-24-q261");

    /// <summary>
    /// A real Claude Code whose server exited mid-session meets the refusal once,
    /// and the retry it asks for succeeds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The server is made to exit from outside, because BrowserAI has no switch
    /// for it and must not grow one.</b> The registered command is
    /// <c>shim.js</c>, which starts the published server, forwards stdio and ends
    /// that server after it has answered one <c>tools/call</c>. The client then
    /// meets a closed transport, re-dials its registered command, and gets a fresh
    /// server -- which is the shape an update leaves behind.
    /// </para>
    /// <para>
    /// ⚠️ <b>What this arm does NOT assert, and it was measured and not
    /// assumed:</b> that the client re-lists on
    /// <c>notifications/tools/list_changed</c>. Measured 2026-09-24 @ 2.1.281,
    /// 3/3, with 5.1 s of idle connection left between the refusal and the retry:
    /// <b>no <c>tools/list</c> reached the re-dialled server at all.</b> So the
    /// thing that recovers the turn is the refusal, and this arm asserts the
    /// refusal and the retry. The notification is still asserted to have been
    /// SENT, because that is BrowserAI's own behaviour and the reason the wording
    /// mentions it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARealClaudeCodeMeetsTheRefusalOnceAndItsRetryGoesThrough()
    {
        var client = SuiteEnvironment.RequireClientCommandLine();

        PublishedSlice.EnsureFresh();
        SuiteEnvironment.RequireRepositoryPayload();

        var run = $"suite-cc-{Guid.NewGuid():N}"[..20];
        var work = Directory.CreateDirectory(Path.Combine(ScratchRoot.Path, run));
        var port = FreePort();

        var configuration = OnboardedClientConfig.Seed(Path.Combine(work.FullName, "cfg"));
        var project = Directory.CreateDirectory(Path.Combine(work.FullName, "proj")).FullName;
        var sessions = Directory.CreateDirectory(Path.Combine(work.FullName, "sessions")).FullName;
        var appRoot = Directory.CreateDirectory(Path.Combine(ScratchRoot.ProfileScratch, run)).FullName;

        var arguments = new JsonObject { ["directory"] = sessions }.ToJsonString();
        var call = $"tool:mcp__browserai__{SessionToolSurface.List}:{arguments}";

        var mcp = Path.Combine(work.FullName, "mcp.json");

        await File.WriteAllTextAsync(mcp, new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                ["browserai"] = new JsonObject
                {
                    ["type"] = "stdio",
                    ["command"] = RepositoryPayload.Layout.NodeExecutable,
                    ["args"] = new JsonArray(Path.Combine(Rig, "shim.js")),
                    ["env"] = new JsonObject
                    {
                        ["Q261_SERVER"] = PublishedSlice.Executable,
                        ["Q261_LOGDIR"] = work.FullName,
                        ["Q261_TAG"] = run,
                        ["Q261_DIE_AFTER_CALLS"] = "1",
                        ["Q261_DIED_MARKER"] = Path.Combine(work.FullName, "died-once"),
                        [BrowserAiPaths.AppRootOverride] = appRoot,
                    },
                },
            },
        }.ToJsonString());

        using var stub = StartNode(
            Path.Combine(Rig, "apistub.js"),
            work,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["STUB_PORT"] = port.ToString(CultureInfo.InvariantCulture),
                ["STUB_LOGDIR"] = work.FullName,
                ["STUB_TAG"] = run,

                // Four calls: one the first server answers, one that meets its
                // closed pipe, the one the re-dialled server refuses, and the
                // retry. Then a sentence, so the turn ends instead of timing out.
                ["STUB_SCRIPT"] = $"{call}||{call}||{call}||{call}||text:done",
            });

        using var claude = StartProcess(client, project, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CLAUDE_CONFIG_DIR"] = configuration,
            ["ANTHROPIC_BASE_URL"] = $"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}",
            ["ANTHROPIC_API_KEY"] = "stub-key-not-real",
            ["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1",
        },
        [
            "-p",
            "--mcp-config", mcp,
            "--strict-mcp-config",
            "--model", "sonnet",
            "--tools", string.Empty,
            "--allowedTools", $"mcp__browserai__{SessionToolSurface.List}",
            "--permission-mode", "acceptEdits",
            "--output-format", "stream-json",
            "--verbose",
            "List the BrowserAI sessions under the directory, four times.",
        ]);

        await WaitFor(claude, "the real client's headless run");

        var wire = Wire(Path.Combine(work.FullName, $"{run}.wire.jsonl"));
        var connections = wire.Select(frame => frame.Shim).Distinct().ToList();

        // ⚠️ THE PREMISE, ASSERTED BEFORE ANYTHING ELSE. Every claim below is
        // about a SECOND server, and a client that did not re-dial produces one --
        // at which point the arm would pass by asserting nothing.
        await Assert.That(connections.Count).IsEqualTo(2)
            .Because($"the client had to re-dial BrowserAI for this arm to be about anything. What it said: {WhatItSaid(work, client)}");

        var second = wire.Where(frame => frame.Shim == connections[^1]).ToList();

        // The condition Q261 exists for, read off the wire: the re-dialled
        // connection handshakes and calls, and never asks for a tool list.
        await Assert.That(second.Count(frame => frame.Method == "tools/list")).IsEqualTo(0);
        await Assert.That(second.Any(frame => frame.Method == "initialize")).IsTrue();
        await Assert.That(second.Count(frame => frame.Method == "notifications/tools/list_changed")).IsEqualTo(1);

        // And the refusal reached the MODEL, which is the only place it matters.
        // Read out of the request bodies the stub captured, which are exactly what
        // the model was sent.
        var refusal = SessionErrors.ToolListPredatesThisServer(
            SessionToolSurface.List,
            BuildVersion.Current,
            KnownClients.ClaudeCode);

        var received = ToolResults(Path.Combine(work.FullName, $"{run}.requests.jsonl"));

        await Assert.That(received.Count(text => text == refusal)).IsEqualTo(1);

        // The retry the sentence asks for was forwarded and answered: the same
        // tool's ordinary answer arrives after the refusal.
        await Assert.That(received.Any(text => text.Contains("No BrowserAI sessions under", StringComparison.Ordinal))).IsTrue();
        await Assert.That(received.IndexOf(refusal) < received.FindLastIndex(text => text.Contains("No BrowserAI sessions under", StringComparison.Ordinal))).IsTrue();
    }

    /// <summary>
    /// A real Codex thread asks for the tool list at first connect, so it never
    /// meets the refusal.
    /// </summary>
    /// <remarks>
    /// <b>Driven through <c>codex app-server</c>, which needs no model at all.</b>
    /// <c>mcpServer/tool/call</c> makes the client connect and call without a turn,
    /// so this arm has no HTTP stub, no credential and nothing scripted for a
    /// model to do. What it establishes is an ordering -- <c>tools/list</c> before
    /// the first <c>tools/call</c> -- and the absence of the refusal, which
    /// together are the whole of why Codex needs no refusal and gets none.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARealCodexThreadListsAtFirstConnectAndIsNeverRefused()
    {
        var codex = SuiteEnvironment.RequireCodexCommandLine();

        PublishedSlice.EnsureFresh();
        SuiteEnvironment.RequireRepositoryPayload();

        var run = $"suite-cx-{Guid.NewGuid():N}"[..20];
        var work = Directory.CreateDirectory(Path.Combine(ScratchRoot.Path, run));
        var home = Directory.CreateDirectory(Path.Combine(work.FullName, "codexhome")).FullName;
        var sessions = Directory.CreateDirectory(Path.Combine(work.FullName, "sessions")).FullName;
        var appRoot = Directory.CreateDirectory(Path.Combine(ScratchRoot.ProfileScratch, run)).FullName;

        // TOML, and the quoting is why it is written here and not composed by a
        // helper: every path goes in as a JSON string so a backslash cannot end a
        // value early, which is the one way this file can fail silently.
        await File.WriteAllTextAsync(Path.Combine(home, "config.toml"), string.Join(
            '\n',
            "approval_policy = \"never\"",
            "sandbox_mode = \"read-only\"",
            string.Empty,
            "[mcp_servers.browserai]",
            $"command = {Quote(RepositoryPayload.Layout.NodeExecutable)}",
            $"args = [{Quote(Path.Combine(Rig, "shim.js"))}]",
            "env = { "
                + $"Q261_SERVER = {Quote(PublishedSlice.Executable)}, "
                + $"Q261_LOGDIR = {Quote(work.FullName)}, "
                + $"Q261_TAG = {Quote(run)}, "
                + "Q261_DIE_AFTER_CALLS = \"0\", "
                + $"{BrowserAiPaths.AppRootOverride} = {Quote(appRoot)} }}",
            "startup_timeout_sec = 60",
            "tool_timeout_sec = 120",
            string.Empty));

        var steps = new JsonArray(
            new JsonObject { ["m"] = "initialize", ["p"] = new JsonObject { ["clientInfo"] = new JsonObject { ["name"] = "browserai-suite", ["title"] = "suite", ["version"] = "1" } } },
            new JsonObject { ["m"] = "thread/start", ["p"] = new JsonObject() },
            new JsonObject
            {
                ["m"] = "mcpServer/tool/call",
                ["p"] = new JsonObject
                {
                    ["threadId"] = "$THREAD",
                    ["server"] = "browserai",
                    ["tool"] = SessionToolSurface.List,
                    ["arguments"] = new JsonObject { ["directory"] = sessions },
                },
            }).ToJsonString();

        using var driver = StartNode(
            Path.Combine(Rig, "appserver.js"),
            work,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CODEX_EXE"] = codex,
                ["CODEX_HOME"] = home,
                ["DRIVER_LOG"] = Path.Combine(work.FullName, $"{run}.driver.log"),
            },
            steps);

        await WaitFor(driver, "the real Codex app-server run");

        var wire = Wire(Path.Combine(work.FullName, $"{run}.wire.jsonl"));

        // The premise: it connected at all. Without this the two assertions below
        // are both true of an empty file.
        await Assert.That(wire.Any(frame => frame.Method == "initialize")).IsTrue();

        var listed = wire.FindIndex(frame => frame.Method == "tools/list");
        var called = wire.FindIndex(frame => frame.Method == "tools/call");

        await Assert.That(listed).IsGreaterThanOrEqualTo(0);
        await Assert.That(called).IsGreaterThan(listed);

        // And so the refusal never fired: no notification, and the call was
        // answered and not refused.
        await Assert.That(wire.Any(frame => frame.Method == "notifications/tools/list_changed")).IsFalse();

        var driverLog = await File.ReadAllTextAsync(Path.Combine(work.FullName, $"{run}.driver.log"));

        await Assert.That(driverLog).Contains("No BrowserAI sessions under");
        await Assert.That(driverLog).DoesNotContain("has never asked BrowserAI for its tool list");
    }

    /// <summary>One frame the shim saw, reduced to what these arms read.</summary>
    /// <param name="Shim">Which shim process it passed through, which is which server.</param>
    /// <param name="Method">The JSON-RPC method, or null for a response.</param>
    private readonly record struct WireFrame(int Shim, string? Method);

    /// <summary>The shim's wire log, in order.</summary>
    /// <param name="path">The log the shim wrote.</param>
    /// <returns>Every frame it recorded.</returns>
    private static List<WireFrame> Wire(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var frames = new List<WireFrame>();

        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Length is 0 || JsonNode.Parse(line) is not JsonObject row)
            {
                continue;
            }

            frames.Add(new WireFrame(
                (int?)row["shim"] ?? 0,
                row["frame"] is JsonObject frame ? (string?)frame["method"] : null));
        }

        return frames;
    }

    /// <summary>
    /// Every <c>tool_result</c> the model was sent, in order, deduplicated by
    /// position and not by value.
    /// </summary>
    /// <remarks>
    /// <b>Read from the API request bodies and not from the client's output</b>,
    /// because those bodies are literally what the model saw -- the client's own
    /// stream is a rendering of it. Each turn resends the whole conversation, so
    /// the LAST turn's message list carries every result exactly once, which is why
    /// only that one is read.
    /// </remarks>
    /// <param name="path">The stub's request capture.</param>
    /// <returns>The results, oldest first.</returns>
    private static List<string> ToolResults(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var lines = File.ReadAllLines(path).Where(line => line.Length is not 0).ToList();

        if (lines.Count is 0 || JsonNode.Parse(lines[^1]) is not JsonObject last)
        {
            return [];
        }

        var results = new List<string>();

        foreach (var message in last["body"]?["messages"]?.AsArray() ?? [])
        {
            if (message?["content"] is not JsonArray blocks)
            {
                continue;
            }

            foreach (var block in blocks)
            {
                if (block is not JsonObject typed || (string?)typed["type"] != "tool_result")
                {
                    continue;
                }

                results.Add(typed["content"] switch
                {
                    JsonArray parts => string.Concat(parts.Select(part => (string?)part?["text"] ?? string.Empty)),
                    JsonValue text => (string?)text ?? string.Empty,
                    _ => string.Empty,
                });
            }
        }

        return results;
    }

    /// <summary>A TOML string, with every backslash and quote escaped.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The quoted literal.</returns>
    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    /// <summary>A port nothing is listening on, taken by binding it and letting go.</summary>
    /// <remarks>
    /// <b>Bound and released, and not picked from a range</b>, because the suite
    /// runs in parallel and two arms choosing the same number is a flake that looks
    /// like a client defect. The window between the release and the stub's own bind
    /// is the residual and is named and not implied.
    /// </remarks>
    /// <returns>The port.</returns>
    private static int FreePort()
    {
        using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        return ((IPEndPoint)probe.LocalEndPoint!).Port;
    }

    /// <summary>Starts a node script from the rig.</summary>
    /// <param name="script">The script.</param>
    /// <param name="work">Its working directory.</param>
    /// <param name="environment">What to add to the inherited environment.</param>
    /// <param name="argument">One argument, for the driver that takes its steps that way.</param>
    /// <returns>The process.</returns>
    private static Process StartNode(string script, DirectoryInfo work, Dictionary<string, string> environment, string? argument = null) =>
        StartProcess(
            RepositoryPayload.Layout.NodeExecutable,
            work.FullName,
            environment,
            argument is null ? [script] : [script, argument]);

    /// <summary>Starts a process with its streams redirected and no console.</summary>
    /// <remarks>
    /// <c>CreateNoWindow</c> is set here as it is at every launch site in this
    /// tree: redirecting the streams does not suppress the console, and from a
    /// windowless parent each omission puts a terminal on the user's screen.
    /// </remarks>
    /// <param name="executable">What to start.</param>
    /// <param name="workingDirectory">Where.</param>
    /// <param name="environment">What to add to the inherited environment.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>The process.</returns>
    private static Process StartProcess(
        string executable,
        string workingDirectory,
        Dictionary<string, string> environment,
        IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // ⚠️ REMOVED BEFORE ANYTHING IS ADDED, AND THIS IS WHY THE ARM EXISTED FOR
        // AN HOUR WITHOUT WORKING. The suite may itself be running underneath one
        // of these clients -- which is the ordinary case on this machine -- and the
        // variables that say so are inherited by every child. A nested client that
        // reads them takes a different path entirely: measured 2026-09-24, the
        // client connected to BrowserAI, asked the stub nothing but
        // `HEAD /api/hello`, and exited, so the arm saw one server process and
        // failed on its own premise with nothing to say about why. The rig's shell
        // drivers unset the same four for the same reason.
        foreach (var inherited in ParentClientVariables)
        {
            _ = start.Environment.Remove(inherited);
        }

        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        var process = Process.Start(start)
            ?? throw new InvalidOperationException($"'{executable}' did not start.");

        // Drained INTO FILES, not into nowhere. A child whose redirected pipe fills
        // stops writing and then stops working, so the drain is mandatory -- and a
        // real client's own complaint is the only thing that explains a run that
        // did nothing, so it is kept where a failure can quote it.
        var stem = Path.Combine(workingDirectory, Path.GetFileNameWithoutExtension(executable));

        _ = Drain(process.StandardOutput, $"{stem}.stdout.txt");
        _ = Drain(process.StandardError, $"{stem}.stderr.txt");
        process.StandardInput.Close();

        return process;
    }

    /// <summary>
    /// The variables that say the suite is running underneath a real client, which
    /// a child of that suite must not inherit.
    /// </summary>
    private static readonly string[] ParentClientVariables =
    [
        "CLAUDECODE",
        "CLAUDE_CODE_ENTRYPOINT",
        "CLAUDE_CODE_SESSION_ID",
        "CLAUDE_CODE_CHILD_SESSION",
        "CLAUDE_CONFIG_DIR",
        "ANTHROPIC_BASE_URL",
        "ANTHROPIC_API_KEY",
        "ANTHROPIC_AUTH_TOKEN",
        "CODEX_HOME",
    ];

    /// <summary>Copies a redirected stream into a file as it arrives.</summary>
    /// <param name="stream">The reader.</param>
    /// <param name="path">Where to put it.</param>
    /// <returns>The copy.</returns>
    private static async Task Drain(StreamReader stream, string path)
    {
        var text = await stream.ReadToEndAsync();

        await File.WriteAllTextAsync(path, text);
    }

    /// <summary>What a child said, for a failure message.</summary>
    /// <param name="work">The run's working directory.</param>
    /// <param name="executable">The child whose streams to read.</param>
    /// <returns>Its stderr and the tail of its stdout.</returns>
    private static string WhatItSaid(DirectoryInfo work, string executable)
    {
        var stem = Path.Combine(work.FullName, Path.GetFileNameWithoutExtension(executable));
        var parts = new List<string>();

        foreach (var (what, file) in new[] { ("stderr", $"{stem}.stderr.txt"), ("stdout", $"{stem}.stdout.txt") })
        {
            var text = File.Exists(file) ? File.ReadAllText(file) : "<no file>";

            parts.Add($"{what}: {(text.Length > 1200 ? text[^1200..] : text)}");
        }

        return string.Join(Environment.NewLine, parts);
    }

    /// <summary>
    /// Waits for a real client to finish, or says which one did not.
    /// </summary>
    /// <remarks>
    /// <b>A hang detector and not a promptness claim.</b> The bound is
    /// <see cref="TestDefaults.RealClientHang"/>, which is derived and not written
    /// here; what these runs actually take is seconds.
    /// </remarks>
    /// <param name="process">The process.</param>
    /// <param name="what">What it was, for the failure.</param>
    /// <returns>The wait.</returns>
    private static async Task WaitFor(Process process, string what)
    {
        using var deadline = new CancellationTokenSource(TestDefaults.RealClientHang);

        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"{what} had not exited after {TestDefaults.RealClientHang.TotalMinutes.ToString("F0", CultureInfo.InvariantCulture)} minutes. "
                + "That bound is a hang detector and not a promptness claim: these runs take seconds. Something is waiting for input, or the stub never answered.");
        }
    }
}
