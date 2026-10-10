// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.App;
using BrowserAI.Hosting;
using BrowserAI.Logging;
using BrowserAI.Protocol;
using BrowserAI.Sessions;
using BrowserAI.Storage;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Tests;

/// <summary>
/// What a client hands its server in the environment, a messaging socket and its
/// secret token among it, never reaches a log, a report, a child or the background.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured 2026-10-08, the startup measurement</b>: every Claude Code server is
/// handed a messaging pipe and its token in its environment, so a server that writes
/// its environment anywhere writes the token there. A relay inherits it; the
/// background, which the Task Scheduler starts, does not, and nothing in the relay's
/// greeting is read from the environment. These arms plant the variables in this
/// process and read back the four places a BrowserAI process writes what it is: the
/// status report, the process log's startup records, the block a child is started
/// with, and the greeting a relay sends the background.
/// </para>
/// <para>
/// <b>The names are planted three ways</b>: the measurement's two,
/// <c>CLAUDE_CODE_MESSAGING_SOCKET</c> and <c>CLAUDE_CODE_MESSAGING_TOKEN</c>, and the
/// spelling the build brief of 2026-10-09 gave the second,
/// <c>CLAUDE_CODE_MESSAGING_SOCKET_TOKEN</c>, each holding a token minted for the arm.
/// </para>
/// <para>
/// ⚠️ <b>The class runs beside nothing, keyless <c>[NotInParallel]</c></b>: the
/// variables are process-wide, and every child any other arm started meanwhile would
/// inherit them.
/// </para>
/// </remarks>
[NotInParallel]
internal sealed class InheritedEnvironmentTests
{
    /// <summary>The names a client's messaging socket travels under.</summary>
    private static readonly string[] Names =
    [
        "CLAUDE_CODE_MESSAGING_SOCKET",
        "CLAUDE_CODE_MESSAGING_TOKEN",
        "CLAUDE_CODE_MESSAGING_SOCKET_TOKEN",
    ];

    /// <summary>
    /// The status report a person attaches when BrowserAI is not working carries
    /// nothing of the environment it was written in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The positive control is the same report with the token in a value the report
    /// does carry</b>, the data root, so a search that could find nothing would fail.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a report that wrote the client's messaging
    /// variables from the environment it was written in.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheStatusReportCarriesNothingOfTheEnvironment()
    {
        var token = NewToken();

        using var output = ScratchDirectory.Create("environment-report");
        using var planted = Plant(token);

        var state = AppState.Read(new FakeRegisterAi(), output.Path, output.Path);
        var text = await File.ReadAllTextAsync(StatusReport.Write(state, Path.Combine(output.Path, "report.json")));

        await Assert.That(text).Contains("\"schemaVersion\"").Because("the report was not written at all");
        await AssertCarriesNoneOfItAsync(text, token, "the status report");

        // The positive control.
        var control = await File.ReadAllTextAsync(StatusReport.Write(state with { DataRoot = Path.Combine(output.Path, token) }, Path.Combine(output.Path, "control.json")));

        await Assert.That(control).Contains(token);
    }

    /// <summary>
    /// The process log's startup records, the one every mode writes first and the
    /// background's own, carry nothing of the environment the process was started with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The records are written through the product's own log</b>, to a file in a
    /// scratch data root, with exactly the values the program gives them: the build, the
    /// pid, the image, the working directory and the linked SQLite.
    /// </para>
    /// <para>
    /// <b>The positive control is a record whose own parameter carries the token</b>,
    /// read back from a file of its own.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a log writer that wrote the client's
    /// messaging variables from the environment into every record.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheStartupRecordsCarryNothingOfTheEnvironment()
    {
        var token = NewToken();

        using var data = ScratchDirectory.Create("environment-log");
        using var controlData = ScratchDirectory.Create("environment-log-control");
        using var planted = Plant(token);

        string? written;

        // Exactly what the program hands the startup record, read first.
        var image = Environment.ProcessPath ?? "<unknown>";
        var workingDirectory = Environment.CurrentDirectory;
        var sqlite = Sqlite.Version;
        var sqliteBuild = Sqlite.BuildReport;
        var pipe = PublishedBackground.NewPipeName();
        var record = Path.Combine(data.Path, "background", "record.json");

        using (var log = ProcessLog.Create(new LocalAppDataPaths(data.Path), LogLevel.Information))
        {
            var startup = log.Factory.CreateLogger("BrowserAI.Startup");
            var background = log.Factory.CreateLogger("BrowserAI.Background");

            StartupLog.Started(startup, BuildVersion.Current, Environment.ProcessId, image, workingDirectory, sqlite, sqliteBuild);
            var how = Program.HowStarted(PersonStart.StartedByPerson);

            BackgroundLog.Started(background, pipe, how, record);

            written = log.CurrentFile;
        }

        var text = await ReadSharedAsync(written!);

        await Assert.That(text).Contains($"BrowserAI {BuildVersion.Current} started.").Because("the startup record was not written at all");
        await Assert.That(text).Contains("BrowserAI's background serves");
        await Assert.That(text).Contains("; it was started by a person's start, and its record is ");
        await AssertCarriesNoneOfItAsync(text, token, "the process log");

        // The positive control.
        string? control;

        var carrying = Path.Combine(controlData.Path, token);

        using (var log = ProcessLog.Create(new LocalAppDataPaths(controlData.Path), LogLevel.Information))
        {
            var startup = log.Factory.CreateLogger("BrowserAI.Startup");

            StartupLog.AppRootOverridden(startup, Names[0], carrying);
            control = log.CurrentFile;
        }

        await Assert.That(await ReadSharedAsync(control!)).Contains(token);
    }

    /// <summary>
    /// A session's child is started with none of the client's messaging variables, while
    /// a variable the child is allowed planted the same way reaches it, and nothing the
    /// session host logged while opening the session carries the token.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The child's block is an allowlist</b> (<see cref="ChildEnvironment"/>), so the
    /// messaging variables are absent by construction; this holds the construction, and
    /// the session the host opens, whose launch is recorded by the rig as the product
    /// built it. <c>NODE_EXTRA_CA_CERTS</c> is the positive control, an allowed name that
    /// nothing in this process reads.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against an allowlist that named the messaging token.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AChildIsStartedWithNoneOfTheMessagingVariables()
    {
        var token = NewToken();
        const string Allowed = "NODE_EXTRA_CA_CERTS";

        using var planted = Plant(token, (Allowed, @"C:\Users\someone\certificates-" + token + ".pem"));

        var block = ChildEnvironment.Build();

        await Assert.That(string.Join(" | ", block.Where(variable => variable.Value.Contains(token, StringComparison.Ordinal)).Select(variable => variable.Key)))
            .IsEqualTo(Allowed)
            .Because("the child's block carries the token under another name, or the planted control did not reach it");

        foreach (var name in Names)
        {
            await Assert.That(block.ContainsKey(name)).IsFalse().Because($"a child is handed {name}");
        }

        // The session the host opens hands its child the same block.
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await SessionHostRig.StartAsync(sessions);

        var connection = await rig.ConnectAsync("claude-code");
        var directory = Path.Combine(sessions.Root, "inherited-environment");
        var opened = await connection.CallAsync(SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session opened while the client's messaging variables are set",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = SessionTimes.HiddenIdleMinutes,
        });

        await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true).Because(HostConnection.TextOf(opened));

        var launch = sessions.Launches.Single();

        await Assert.That(launch.Environment.Keys.Intersect(Names, StringComparer.OrdinalIgnoreCase)).IsEmpty();
        await Assert.That(launch.Environment.TryGetValue(Allowed, out var allowed) && allowed.Contains(token, StringComparison.Ordinal)).IsTrue();
        await Assert.That(rig.Logs.Records.Where(record => (record.Message + record.Exception).Contains(token, StringComparison.Ordinal)).Select(record => record.Message)).IsEmpty();
    }

    /// <summary>
    /// The greeting a relay sends the background carries nothing of the environment the
    /// client handed the relay.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The greeting carries every fact about the client</b>, because the background
    /// never sees the client's environment, and the engine's own remarks say nothing in
    /// it is read from this process's environment. This holds that for the frame itself.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-09</b> against a greeting that carried the messaging token.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRelaysGreetingCarriesNothingOfTheEnvironment()
    {
        var token = NewToken();

        using var planted = Plant(token);
        await using var relay = RelayRig.Start();

        var (_, hello) = await relay.ConnectedAsync("claude-code");

        await Assert.That(hello.Method).IsEqualTo(Relay.RelayProtocol.Hello);
        await AssertCarriesNoneOfItAsync(hello.Text, token, "the relay's greeting");
    }

    /// <summary>
    /// What the relay reads of its client's environment to name the conversation carries
    /// nothing of the messaging variables, while a folder planted where it does read
    /// reaches the greeting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-10-10 with the maintainer's 1.1 c</b>: the relay reads four variables
    /// by name, the entrypoint, <c>CLAUDE_CONFIG_DIR</c>, <c>CLAUDE_CODE_SESSION_ID</c>
    /// and <c>USERPROFILE</c>, and sends the folder and the session they name to the
    /// background. This holds the product's own reading, over this process's real
    /// environment and parent, with the messaging variables planted.
    /// </para>
    /// <para>
    /// <b>The positive control is the token planted in <c>CLAUDE_CONFIG_DIR</c></b>,
    /// which the reading does carry, so a search that could find nothing would fail.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a reading that carried every variable whose
    /// name begins <c>CLAUDE_CODE_</c>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WhatTheRelayReadsToNameTheConversationCarriesNothingOfTheMessagingVariables()
    {
        var token = NewToken();

        string greeting;

        using (Plant(token))
        {
            var read = Relay.ClientRecognition.Read(Relay.ClientRecognition.ClaudeCode, Interop.ProcessLiveness.ReadParent(), Environment.GetEnvironmentVariable);

            greeting = new JsonObject { ["conversation"] = read.Conversation?.ToJson(), ["window"] = read.Window }.ToJsonString();
        }

        await Assert.That(greeting).Contains("claudeConfig").Because("the reading read nothing at all");
        await AssertCarriesNoneOfItAsync(greeting, token, "what the relay read for the conversation");

        // The positive control.
        string control;

        using (Plant(token, (Relay.ClientRecognition.ConfigFolderVariable, @"C:\Users\someone\claude-" + token)))
        {
            control = Relay.ClientRecognition.Read(Relay.ClientRecognition.ClaudeCode, parent: null, Environment.GetEnvironmentVariable).Conversation!.ToJson().ToJsonString();
        }

        await Assert.That(control).Contains(token);
    }

    /// <summary>A token no file and no log on this machine can hold by chance.</summary>
    /// <returns>The token.</returns>
    private static string NewToken() => $"suite-messaging-token-{Guid.NewGuid():N}";

    /// <summary>Plants the client's messaging variables, and any others an arm names, for the scope.</summary>
    /// <param name="token">The token every messaging variable carries.</param>
    /// <param name="others">Further variables, as a client's environment might hold them.</param>
    /// <returns>The scope, which puts back what was there.</returns>
    private static EnvironmentScope Plant(string token, params (string Name, string Value)[] others)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [Names[0]] = @"\\.\pipe\claude-code-messaging-" + token,
            [Names[1]] = token,
            [Names[2]] = token,
        };

        foreach (var (name, value) in others)
        {
            values[name] = value;
        }

        return new EnvironmentScope(values);
    }

    /// <summary>Holds that a text carries neither the token nor any of the names.</summary>
    /// <param name="text">The text.</param>
    /// <param name="token">The token.</param>
    /// <param name="what">What the text is, for the message.</param>
    /// <returns>The assertion task.</returns>
    private static async Task AssertCarriesNoneOfItAsync(string text, string token, string what)
    {
        await Assert.That(text).DoesNotContain(token).Because($"{what} carries the client's messaging token");

        foreach (var name in Names)
        {
            await Assert.That(text).DoesNotContain(name).Because($"{what} names {name}");
        }
    }

    /// <summary>Reads a log file the way a person does while it may still be open.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its text.</returns>
    private static async Task<string> ReadSharedAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        return await reader.ReadToEndAsync();
    }
}
