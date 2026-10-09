// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using BrowserAI.Interop;
using BrowserAI.Proxy;
using BrowserAI.Registration;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;

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
/// anywhere</b>; and <see cref="BrowserAiPaths.DataRootArgument"/> points the
/// server BrowserAI's own data root at <see cref="ScratchRoot.ProfileScratch"/>
/// (<i>the argument since 2026-10-08, step 5 of the one-binary build; previously
/// <c>BROWSERAI_ROOT</c> in each client's configuration</i>).
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
/// ⚠️ <b>A relay and a background since 2026-10-09.</b> What a client starts is a
/// relay, which holds no session and starts nothing (S a), and a build that is not
/// installed starts no background (D11 a). So every arm here starts the background
/// itself, in a job of the arm's own and over the same scratch data root, and hands
/// the relay its pipe: through <c>Q261_SERVER_ARGS</c> where the shim starts it, and
/// in the registration's arguments where the client starts it the product's way. The
/// re-dialled server of the first arm is a second relay to the same background, and
/// the stale-list refusal it answers is the relay's (Q261 b).
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

        // The background both relays reach, the first and the one re-dialled.
        await using var background = BackgroundOver(appRoot, work);

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
                        ["Q261_SERVER_ARGS"] = background.RelayArguments,
                        ["Q261_LOGDIR"] = work.FullName,
                        ["Q261_TAG"] = run,
                        ["Q261_DIE_AFTER_CALLS"] = "1",
                        ["Q261_DIED_MARKER"] = Path.Combine(work.FullName, "died-once"),
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
        //
        // ⚠️ THE VERSION IN IT IS THE ONE THE RE-DIALLED SERVER NAMED ON THE WIRE
        // -- N16, 2026-09-24 (previously BuildVersion.Current, the TEST HOST's). The
        // refusal names the version of the server that wrote it, and that is the
        // published binary, which a commit touching no source can leave one
        // version behind the tree: measured the same day, 1.1.1-alpha.0.72 against
        // a host built as 1.1.1-alpha.0.73, and this arm went red in both shells
        // over a sentence that was right. The freshness guard now refuses that
        // state by name; this is the half that makes the arm right by construction.
        var served = second.Select(frame => frame.ServerVersion).FirstOrDefault(version => version is not null);

        await Assert.That(served).IsNotNull()
            .Because("the re-dialled server's initialize answer names its version, and the shim records it; without it this arm would be comparing against a guess");

        var refusal = SessionErrors.ToolListPredatesThisServer(
            SessionToolSurface.List,
            served!,
            KnownClients.ClaudeCode);

        var received = ToolResults(Path.Combine(work.FullName, $"{run}.requests.jsonl"));

        // Its sentences and, since 2026-10-04 (2 b), the server's tool list after
        // them, which ToolListInRefusalsTests holds tool by tool.
        await Assert.That(received.Count(text => text.StartsWith(refusal, StringComparison.Ordinal))).IsEqualTo(1);
        await Assert.That(received.Single(text => text.StartsWith(refusal, StringComparison.Ordinal)))
            .Contains("\n\nThe tools this BrowserAI has now:\n- " + SessionToolSurface.Init + ": ");

        // The retry the sentence asks for was forwarded and answered: the same
        // tool's ordinary answer arrives after the refusal.
        await Assert.That(received.Any(text => text.Contains("No BrowserAI sessions under", StringComparison.Ordinal))).IsTrue();
        await Assert.That(received.FindIndex(text => text.StartsWith(refusal, StringComparison.Ordinal)) < received.FindLastIndex(text => text.Contains("No BrowserAI sessions under", StringComparison.Ordinal))).IsTrue();
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

        await using var background = BackgroundOver(appRoot, work);

        // TOML, and the quoting is why it is written here and not composed by a
        // helper: every path goes in as a JSON string so a backslash cannot end a
        // value early, which is the one way this file can fail silently.
        await File.WriteAllTextAsync(Path.Combine(home, "config.toml"), string.Join(
            '\n',
            "approval_policy = \"never\"",
            "sandbox_mode = \"read-only\"",
            // Codex reads AGENTS.md from the git root down to its working directory when
            // a thread starts (codex-rs 0.155.0-alpha.9.2, agents_md.rs), and this one is
            // inside the repository. A budget of zero keeps the repository's instructions
            // out of the client, as before the instruction files were renamed.
            "project_doc_max_bytes = 0",
            string.Empty,
            "[mcp_servers.browserai]",
            $"command = {Quote(RepositoryPayload.Layout.NodeExecutable)}",
            $"args = [{Quote(Path.Combine(Rig, "shim.js"))}]",
            "env = { "
                + $"Q261_SERVER = {Quote(PublishedSlice.Executable)}, "
                + $"Q261_SERVER_ARGS = {Quote(background.RelayArguments)}, "
                + $"Q261_LOGDIR = {Quote(work.FullName)}, "
                + $"Q261_TAG = {Quote(run)}, "
                + "Q261_DIE_AFTER_CALLS = \"0\" }",
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

    /// <summary>
    /// A BrowserAI registered in Codex the product's own way serves a call, does
    /// not outlive the Codex that started it, and leaves nothing that could hold
    /// an update.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The maintainer's ask, verbatim:</b> <i>"I want the same update effects to
    /// be tested on coded."</i> An update applies only when no other BrowserAI is
    /// live, and a live instance is a HELD marker under the app root -- so the
    /// update effect of a Codex-hosted server is two questions: does the process
    /// go when its host goes, and does anything it leaves stay held.
    /// </para>
    /// <para>
    /// ⚠️ <b>THE PREMISE THIS ARM WAS WRITTEN AGAINST DID NOT HOLD, AND IT WAS
    /// MEASURED AND NOT ASSUMED.</b> It was briefed as "exits on stdin EOF the way
    /// Codex ends it". Measured 2026-09-24 @ codex-cli 0.155.0-alpha.9.2, 3/3,
    /// against the published server under a scratch <c>CODEX_HOME</c>: after the
    /// app-server's own stdin EOF it exits cleanly within about 50 ms, and the
    /// BrowserAI server is gone about 100 ms after that EOF -- with NO
    /// end-of-stream line and no client-exit line in its own log, and its live
    /// marker left behind. Codex TERMINATES it; the server never meets an EOF it
    /// could act on. What the update lane needs survives that: the process is
    /// gone, and the marker it left is not held, so the census does not count it.
    /// </para>
    /// <para>
    /// <b>Registered through <c>McpRegistrar</c> and the RegisterAI the published slice
    /// carries, into a scratch home forced on every run of the tool</b> -- never on
    /// this process's environment, so this file needs no <c>[NotInParallel]</c> and no
    /// other arm inherits it. The install root's <c>current\</c> is a junction to the
    /// published slice, so the registrar composes and checks the server exactly as it
    /// does for an install, finds RegisterAI where an install has it, and Codex starts
    /// the published binary with its payload beside it. <i>Corrected 2026-10-03
    /// (previously "forced on every call the runner makes"), when the runner of each
    /// client's command line went to RegisterAI.</i>
    /// </para>
    /// <para>
    /// ⚠️ <b><c>BROWSERAI_ROOT</c> is added to that registration with Codex's
    /// own <c>--env</c>, and the arm cannot do without it.</b> Codex hands a stdio
    /// server a fixed allowlist of variables and nothing else -- measured
    /// 2026-09-24 @ codex-cli 0.155.0-alpha.9.2, 3/3, with a stand-in server that
    /// recorded what it was given: exactly 20 variables, <c>APPDATA</c> to
    /// <c>WINDIR</c>, and a <c>BROWSERAI_ROOT</c> set on the app-server was not
    /// among them. A server started without it would run its stray sweep over the
    /// developer's own app root.
    /// </para>
    /// <para>
    /// <b>Both directions of the marker check are in the arm.</b> While the server
    /// is serving, the product's own reclaim pass finds its marker HELD; after
    /// Codex has ended, the same pass finds it not held and takes it. A check that
    /// could only ever answer "not held" would pass against a server that never
    /// wrote one.
    /// </para>
    /// <para>
    /// ⚠️ <b>No marker since 2026-10-09, and the question moved to the background's
    /// roster</b> (previously the two directions above). Nothing in the product joins
    /// the live-instance census since the one resident background (S a): what holds an
    /// update is what the background's update core reads, every relay with its
    /// countdown and every kept session (H1), and the background writes what it still
    /// holds each time a relay goes. So the positive control is the relay the
    /// background recorded as connected, alive while Codex serves; and after Codex,
    /// that relay is gone and the background's record of it going leaves no relay and
    /// no session. The registration is the product's own, with the background's pipe
    /// added beside the variable Codex would otherwise drop.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ABrowserAiRegisteredInCodexServesACallAndLeavesNothingThatHoldsAnUpdate()
    {
        var codex = SuiteEnvironment.RequireCodexCommandLine();

        PublishedSlice.EnsureFresh();
        SuiteEnvironment.RequireRepositoryPayload();

        var run = $"suite-cu-{Guid.NewGuid():N}"[..20];
        var work = Directory.CreateDirectory(Path.Combine(ScratchRoot.Path, run));
        var home = Directory.CreateDirectory(Path.Combine(work.FullName, "codexhome")).FullName;
        var sessions = Directory.CreateDirectory(Path.Combine(work.FullName, "sessions")).FullName;
        var install = Directory.CreateDirectory(Path.Combine(work.FullName, "install")).FullName;
        var appRoot = Directory.CreateDirectory(Path.Combine(ScratchRoot.ProfileScratch, run)).FullName;

        await using var background = BackgroundOver(appRoot, work);

        var current = Path.Combine(install, RegistrationTarget.CurrentDirectoryName);

        await PathAliases.JunctionAsync(current, PublishedSlice.Directory);

        // project_doc_max_bytes = 0: see ARealCodexThreadListsAtFirstConnectAndIsNeverRefused.
        await File.WriteAllTextAsync(Path.Combine(home, "config.toml"), "approval_policy = \"never\"\nsandbox_mode = \"read-only\"\nproject_doc_max_bytes = 0\n");

        // ---- Registered the product's way --------------------------------------
        var image = Path.Combine(current, RegistrationTarget.AppFileName);
        var server = Path.Combine(current, RegistrationTarget.AppFileName);
        var tool = new RegisterAiTool(
            RegisterAiTool.Beside(image).Executable,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RegistrationTests.CodexHomeVariable] = home,
            });

        // ⚠️ THE HOME IS PROVEN BEFORE ANYTHING IS WRITTEN: RegisterAI names the
        // configuration it reads, and a variable that did not reach it would name
        // the person's own.
        var before = RegistrationReader.Read(tool, [RegistrationClient.Codex], install, server, Path.GetPathRoot(home)!)[0];

        await Assert.That(before.UserScope.File).IsEqualTo(Path.Combine(home, "config.toml"));

        var registered = McpRegistrar.Apply(
            RegistrationClient.Codex, RegistrationIntent.Install, image, tool, NullLogger.Instance);

        await Assert.That(registered.Status).IsEqualTo(RegistrationStatus.Registered).Because(registered.Detail);

        var listed = RegistrationReader.Read(tool, [RegistrationClient.Codex], install, server, Path.GetPathRoot(home)!)[0];

        await Assert.That(listed.UserScope.Command).IsEqualTo(server);
        await Assert.That(listed.UserScope.Ownership).IsEqualTo(RegistrationOwnership.OursAndPresent);

        // The sandbox, added through RegisterAI and Codex's own flag and not by
        // writing its file: the same entry, rewritten with the data root as an
        // argument (since step 5; previously the one variable Codex would otherwise
        // drop, BROWSERAI_ROOT, which no running BrowserAI reads any more), and since
        // 2026-10-09 the pipe of the background the arm started, which a build that is
        // not installed is given (D11 a).
        var sandboxed = tool.Run(
            [
                "register", "--name", McpRegistrar.ServerName, "--client", RegistrationClient.Codex.ToolId, "--scope", "user",
                "--owned-root", install, "--replace", "--", server, RegistrationTarget.McpArgument, BrowserAiPaths.DataRootArgument, appRoot,
                BrowserAI.Coordination.BackgroundPipe.PipeArgument, background.Pipe,
            ],
            McpRegistrar.ToolBudget);

        await Assert.That(ToolDocuments.TryRead(sandboxed, out var rewritten, out var problem)).IsTrue().Because(problem);
        await Assert.That(rewritten!.For(RegistrationClient.Codex.ToolId)!.Action).IsEqualTo("replaced");

        // ---- Started by the real client, one call ------------------------------
        var steps = new JsonArray(
            new JsonObject { ["m"] = "initialize", ["p"] = new JsonObject { ["clientInfo"] = new JsonObject { ["name"] = "browserai-suite", ["title"] = "suite", ["version"] = "1" } } },
            new JsonObject { ["m"] = "thread/start", ["p"] = new JsonObject() },
            new JsonObject
            {
                ["m"] = "mcpServer/tool/call",
                ["p"] = new JsonObject
                {
                    ["threadId"] = "$THREAD",
                    ["server"] = McpRegistrar.ServerName,
                    ["tool"] = SessionToolSurface.List,
                    ["arguments"] = new JsonObject { ["directory"] = sessions },
                },
            },
            new JsonObject { ["sleep"] = 6000 }).ToJsonString();

        var driverLog = Path.Combine(work.FullName, $"{run}.driver.log");

        using var driver = StartNode(
            Path.Combine(Rig, "appserver.js"),
            work,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CODEX_EXE"] = codex,
                ["CODEX_HOME"] = home,
                ["DRIVER_LOG"] = driverLog,

                // Long enough that the app-server's own exit, if it comes, is on
                // the log before the driver's kill could have caused it.
                ["DRIVER_KILL_AFTER_MS"] = "10000",
            },
            steps);

        // ---- While it serves: the background holds its relay ----------------------
        await UntilTheLogSays(driverLog, "RESP id=3", driver);

        var (pid, created) = RelayOf(appRoot, background);

        await Assert.That(ProcessLiveness.IsAlive(pid, created)).IsTrue()
            .Because("the positive control: while Codex serves, the relay the background recorded is alive, or the check below proves nothing");

        await WaitFor(driver, "the real Codex app-server run");

        var log = await File.ReadAllTextAsync(driverLog);

        // It served the call.
        await Assert.That(log).Contains("No BrowserAI sessions under");

        // The app-server left on its own after its stdin EOF, before the kill.
        var exited = log.IndexOf("APPSERVER EXIT code=0", StringComparison.Ordinal);
        var killed = log.IndexOf("KILLING the app-server", StringComparison.Ordinal);

        await Assert.That(exited).IsGreaterThanOrEqualTo(0);
        await Assert.That(killed < 0 || exited < killed).IsTrue();

        // ---- After Codex: the relay is gone, and the background holds nothing ----
        await Assert.That(ProcessLiveness.IsAlive(pid, created)).IsFalse()
            .Because($"BrowserAI's relay, pid {pid.ToString(CultureInfo.InvariantCulture)}, outlived the Codex app-server that started it, and a relay that outlives its host holds every update");

        var went = await RelayWentAsync(appRoot, background, pid);

        await Assert.That(went).Contains("0 relay(s) and 0 session(s) are left")
            .Because("the background's update core reads its relays and its kept sessions, and Codex left it neither");

        // The control for the liveness read itself: it can say "alive".
        await Assert.That(ProcessLiveness.IsAlive(Environment.ProcessId, ProcessLiveness.CreationTimeOfThisProcess())).IsTrue();
    }

    /// <summary>
    /// The relay the background recorded as connected, as the pid and creation time
    /// the relay stamped on its own start line.
    /// </summary>
    /// <remarks>
    /// <b>The pair and never the pid alone</b>, read off the <c>pid=N@FILETIME</c>
    /// stamp every line of the process log carries: a pid on its own can be
    /// Windows' next process by the time it is asked about, and a liveness answer
    /// about a stranger is worse than none. ⚠️ <i>Corrected 2026-10-09 (previously
    /// <c>ServerStartedIn</c>, the first start line under the root): the background
    /// writes the first one, so the relay is found through the background's record
    /// of it.</i>
    /// </remarks>
    /// <param name="appRoot">The app root both were given.</param>
    /// <param name="background">The background.</param>
    /// <returns>The relay's pid and creation time.</returns>
    private static (int Pid, long Created) RelayOf(string appRoot, ArmBackground background)
    {
        var said = ProcessLogRecords.In(Path.Combine(appRoot, "logs"), background.Process.Id, background.Created);
        var connected = System.Text.RegularExpressions.Regex.Match(said, @"Relay (?<pid>\d+)-\d+ connected");

        if (!connected.Success)
        {
            throw new InvalidOperationException($"The background recorded no relay connecting under '{appRoot}'. Its records:{Environment.NewLine}{said}");
        }

        var relay = int.Parse(connected.Groups["pid"].Value, CultureInfo.InvariantCulture);

        return StartOf(appRoot, relay);
    }

    /// <summary>The background's record of one relay going, once it is written.</summary>
    /// <remarks>A hang detector and not a promptness claim, on the bound <see cref="WaitFor"/> uses.</remarks>
    /// <param name="appRoot">The app root the background was given.</param>
    /// <param name="background">The background.</param>
    /// <param name="relay">The relay's pid.</param>
    /// <returns>The record's line.</returns>
    private static async Task<string> RelayWentAsync(string appRoot, ArmBackground background, int relay)
    {
        var needle = string.Create(CultureInfo.InvariantCulture, $"Relay {relay}-");
        using var deadline = new CancellationTokenSource(TestDefaults.RealClientHang);

        while (true)
        {
            var line = ProcessLogRecords.In(Path.Combine(appRoot, "logs"), background.Process.Id, background.Created)
                .Split('\n')
                .FirstOrDefault(record => record.Contains(needle, StringComparison.Ordinal) && record.Contains(" went; ", StringComparison.Ordinal));

            if (line is not null)
            {
                return line;
            }

            if (deadline.IsCancellationRequested)
            {
                throw new TimeoutException($"The background recorded no going of relay {relay.ToString(CultureInfo.InvariantCulture)}. That bound is a hang detector: a relay's pipe closes when it ends.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None);
        }
    }

    /// <summary>The pid and creation time one process stamped on its own start line.</summary>
    /// <param name="appRoot">The app root it was given.</param>
    /// <param name="processId">The pid.</param>
    /// <returns>The pid and its creation time.</returns>
    private static (int Pid, long Created) StartOf(string appRoot, int processId)
    {
        var header = string.Create(CultureInfo.InvariantCulture, $" pid={processId}@");

        // Read shared: the background and the relay are both alive and appending to
        // these files while the arm reads them (2026-10-09; the reader this replaced
        // ran once the one server it looked for had gone).
        foreach (var file in Directory.EnumerateFiles(Path.Combine(appRoot, "logs"), "*.log"))
        {
            foreach (var line in ReadShared(file).Split('\n'))
            {
                if (!line.Contains(" started. pid=", StringComparison.Ordinal) || !line.Contains(header, StringComparison.Ordinal))
                {
                    continue;
                }

                var stamp = line.IndexOf(" pid=", StringComparison.Ordinal);
                var at = line.IndexOf('@', stamp);
                var end = line.IndexOf(' ', at);

                return (
                    int.Parse(line[(stamp + 5)..at], CultureInfo.InvariantCulture),
                    long.Parse(line[(at + 1)..end], CultureInfo.InvariantCulture));
            }
        }

        throw new InvalidOperationException($"No start line of pid {processId.ToString(CultureInfo.InvariantCulture)} under '{appRoot}'.");
    }

    /// <summary>
    /// Waits until a log carries a line, or the process writing it has gone.
    /// </summary>
    /// <remarks>
    /// <b>A hang detector and not a promptness claim</b>, on the same derived
    /// bound <see cref="WaitFor"/> uses.
    /// </remarks>
    /// <param name="path">The log.</param>
    /// <param name="needle">What to wait for.</param>
    /// <param name="writer">The process that writes it.</param>
    /// <returns>The wait.</returns>
    private static async Task UntilTheLogSays(string path, string needle, Started writer)
    {
        using var deadline = new CancellationTokenSource(TestDefaults.RealClientHang);

        while (!deadline.IsCancellationRequested)
        {
            if (File.Exists(path) && ReadShared(path).Contains(needle, StringComparison.Ordinal))
            {
                return;
            }

            if (writer.Process.HasExited)
            {
                throw new InvalidOperationException($"The driver exited before its log said '{needle}'. Its log: {(File.Exists(path) ? ReadShared(path) : "<none>")}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token).ConfigureAwait(false);
        }

        throw new TimeoutException($"'{path}' never said '{needle}'. That bound is a hang detector: the real run takes seconds.");
    }

    /// <summary>A file another process is still appending to, read without taking it.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its text.</returns>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    /// <summary>
    /// A process this file started, ended on dispose.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Because <c>Process.Dispose</c> releases a handle and does not end a
    /// process, and an API stub never exits on its own.</b> Measured: four leaked
    /// <c>node.exe</c> stubs from four runs of these arms held four scratch
    /// directories open, which the next run's reclaim pass could not delete -- so
    /// the defect surfaced as <c>TheReclaimPassIsItselfATestAndReportsWhatItCouldNotTake</c>
    /// naming somebody else's directory, which is the hardest shape of this to
    /// read. The client and the app-server driver do exit on their own; this ends
    /// them anyway, because "it usually exits" is not a teardown.
    /// </remarks>
    /// <param name="Process">The process.</param>
    private readonly record struct Started(Process Process) : IDisposable
    {
        /// <inheritdoc />
        public void Dispose()
        {
            try
            {
                if (!Process.HasExited)
                {
                    // ⚠️ THIS ONE AND NOT THE TREE. `Process.Kill(entireProcessTree:
                    // true)` is banned repository-wide -- it walks re-parentable,
                    // pid-reusable links and loses the race against anything that
                    // respawns while it walks -- and nothing here needs it: what
                    // leaks is the API stub, which has no children at all. The
                    // client and the app-server driver exit on their own; what they
                    // started is ended by its own stdin reaching EOF when their
                    // pipes close, and every BrowserAI server behind them is held by
                    // BrowserAI's own job object.
                    Process.Kill();
                }
            }
            catch (Exception failure) when (failure is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
            {
                // It has already gone, which is the ordinary case.
            }

            Process.Dispose();
        }
    }

    /// <summary>One frame the shim saw, reduced to what these arms read.</summary>
    /// <param name="Shim">Which shim process it passed through, which is which server.</param>
    /// <param name="Method">The JSON-RPC method, or null for a response.</param>
    /// <param name="ServerVersion">
    /// The version the server named in its <c>initialize</c> answer, on that one
    /// frame, and null on every other.
    /// </param>
    private readonly record struct WireFrame(int Shim, string? Method, string? ServerVersion);

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

            var frame = row["frame"] as JsonObject;

            frames.Add(new WireFrame(
                (int?)row["shim"] ?? 0,
                frame is null ? null : (string?)frame["method"],
                frame is null ? null : (string?)frame["serverVersion"]));
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

    /// <summary>
    /// A published background over a scratch data root, in a job of the arm's own, on
    /// a pipe nothing else serves.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-09.</b> The data root reaches it the way it reaches the relay,
    /// through the suite's variable on its own environment, and never through this
    /// process's: the background judges the relay's greeting against its data root,
    /// so the two must name the same one.
    /// </remarks>
    /// <param name="appRoot">The scratch data root, under the user's profile.</param>
    /// <param name="work">Its working directory.</param>
    /// <returns>The background.</returns>
    private static ArmBackground BackgroundOver(string appRoot, DirectoryInfo work) => ArmBackground.Start(appRoot, work);

    /// <summary>A background an arm started, and what a relay needs to reach it.</summary>
    private sealed class ArmBackground : IAsyncDisposable
    {
        private readonly JobObject _job;

        private ArmBackground(JobObject job, LaunchedProcess process, string pipe, string appRoot)
        {
            _job = job;
            Process = process;
            Pipe = pipe;
            AppRoot = appRoot;
            Created = ProcessIdentity.CreationTimeOf(process.Id);
        }

        /// <summary>The background.</summary>
        public LaunchedProcess Process { get; }

        /// <summary>Its pipe.</summary>
        public string Pipe { get; }

        /// <summary>Its creation time, the other half of its identity.</summary>
        public long Created { get; }

        /// <summary>Its data root, a scratch root under the profile, with no space in it.</summary>
        public string AppRoot { get; }

        /// <summary>
        /// A relay's arguments, space-separated as the shim takes them: <c>--mcp</c>, the
        /// data root and the pipe. <i>The data root since 2026-10-08, step 5 (previously
        /// each client's configuration carried the suite's <c>BROWSERAI_ROOT</c>).</i>
        /// </summary>
        public string RelayArguments => $"{Program.McpArgument} {BrowserAiPaths.DataRootArgument} {AppRoot} {BrowserAI.Coordination.BackgroundPipe.PipeArgument} {Pipe}";

        /// <summary>Starts one and waits for its pipe.</summary>
        /// <param name="appRoot">The scratch data root.</param>
        /// <param name="work">Its working directory.</param>
        /// <returns>The background.</returns>
        public static ArmBackground Start(string appRoot, DirectoryInfo work)
        {
            var job = JobObject.CreateKillOnClose();

            try
            {
                var environment = PublishedSlice.InheritedEnvironment();

                foreach (var inherited in ParentClientVariables)
                {
                    _ = environment.Remove(inherited);
                }

                var pipe = PublishedBackground.NewPipeName();
                var process = PublishedBackground.Start(job, work.FullName, environment, pipe, [BrowserAiPaths.DataRootArgument, appRoot]);

                return new ArmBackground(job, process, pipe, appRoot);
            }
            catch
            {
                job.Dispose();
                throw;
            }
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            _job.Dispose();
            Process.Dispose();
            return ValueTask.CompletedTask;
        }
    }

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
    private static Started StartNode(string script, DirectoryInfo work, Dictionary<string, string> environment, string? argument = null) =>
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
    private static Started StartProcess(
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

        return new Started(process);
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
    /// <param name="started">The process.</param>
    /// <param name="what">What it was, for the failure.</param>
    /// <returns>The wait.</returns>
    private static async Task WaitFor(Started started, string what)
    {
        var process = started.Process;
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
