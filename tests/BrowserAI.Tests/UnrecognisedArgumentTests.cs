// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Proxy;
using BrowserAI.Sessions;
using BrowserAI.Storage;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// A call carrying an argument its tool's schema does not have is refused, and
/// nothing runs -- on BrowserAI's own tools and on every forwarded one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Decided 2026-10-03 by the maintainer, in his words:</b> <i>"I'd expect that
/// any call carrying any parameter or argument that we do not recognize would be
/// refused actively with a syntax error. This would teach the LLM it has somethign
/// wrong. Also, I do not like us keeping history and translating certen arguments
/// for historical sake. The product is what it is and the llm needs to learn to use
/// it."</i>
/// </para>
/// <para>
/// <b>Measured the day before the rule was built</b>, through the published
/// binary at <c>d8a0101a</c>: an argument no schema has was dropped without a
/// word by <c>browserai_list</c> and by <c>browser_navigate</c> alike, and the
/// call went ahead. Upstream's own <c>zod</c> parse strips an unknown key, so the
/// forwarded half was silent at both ends.
/// </para>
/// <para>
/// <b>The check reads the list the caller was given and nothing else</b>: the
/// rewritten <c>tools/list</c>, with <c>session</c> and <c>why</c> added to every
/// forwarded tool. <see cref="TheListTheCallerWasGivenDecidesAndNotAHandWrittenOne"/>
/// is the arm that would fail against a hand-written table.
/// </para>
/// </remarks>
internal sealed class UnrecognisedArgumentTests
{
    /// <summary>
    /// An authored tool refuses an argument it does not take, names it, lists
    /// what it does take, and runs nothing -- the old name of the transcript
    /// argument included, which is one more unrecognised name and nothing else.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnArgumentAnAuthoredToolDoesNotTakeIsRefusedAndNothingRuns()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "never-created");

        var refused = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session an init with an unknown argument must not create",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
            ["tracing"] = true,
            ["notAnArgument"] = "x",
        });

        var text = TextOf(refused);

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(text).StartsWith("Syntax error:");
        await Assert.That(text).Contains($"'{SessionToolSurface.Init}' has no argument named 'tracing' or 'notAnArgument'");
        await Assert.That(text).Contains("nothing ran");

        // Q371.5 b: the tool's whole definition, generated from the list it is
        // served in -- the description, and every argument with its own.
        await Assert.That(text).Contains($"\n\n{SessionToolSurface.Init}: ");
        await Assert.That(text).Contains("\n- 'directory' (required, string): Absolute path of the session directory");
        await Assert.That(text).Contains("\n- 'transcript' (boolean): ");

        // No history and no translation: the old name is one more name the
        // schema does not have. Read off the refusal's own sentences, above the
        // definition, whose wording is the tool's and not the refusal's.
        var own = text[..text.IndexOf("\n\n", StringComparison.Ordinal)];

        await Assert.That(own).DoesNotContain("renamed");
        await Assert.That(own).DoesNotContain("previously");
        await Assert.That(own).DoesNotContain("instead of");
        await Assert.That(own).DoesNotContain("transcript");

        // Nothing ran: the directory was never made, so nothing was created.
        await Assert.That(Directory.Exists(directory)).IsFalse();
        await Assert.That(sessions.SessionChildren.Count).IsEqualTo(0);

        // A read-only tool refuses the same way, and runs nothing either.
        var list = await CallAsync(rig, SessionToolSurface.List, new JsonObject
        {
            ["directory"] = sessions.Root,
            ["recursive"] = true,
        });

        await Assert.That((bool?)list["isError"]).IsTrue();
        await Assert.That(TextOf(list)).StartsWith($"Syntax error: '{SessionToolSurface.List}' has no argument named 'recursive'");
        await Assert.That(TextOf(list)).EndsWith("\nArguments:\n- 'directory' (required, string): Absolute path of the tree to look under. A drive root lists everything on that volume.");

        // ⚠️ THE POSITIVE CONTROL: the same init without the two names creates
        // the session, so the refusal above is about them and nothing else.
        var created = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the same init, with only the arguments it takes",
            ["transcript"] = true,
            ["headed"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        await Assert.That((bool?)created["isError"]).IsNotEqualTo(true);
        await Assert.That(Directory.Exists(directory)).IsTrue();
    }

    /// <summary>
    /// A forwarded tool refuses an argument its schema does not have: the call
    /// is recorded on its session as refused and never reaches the child.
    /// </summary>
    /// <remarks>
    /// <b>Against upstream's real surface</b>, so the schema the call is checked
    /// against is the one a model reads. <c>scale</c> on
    /// <c>browser_take_screenshot</c> is required by upstream's schema and has a
    /// default; leaving it out is not this refusal, which is about names a schema
    /// does not carry and never about names it marks required.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnArgumentAForwardedToolDoesNotTakeIsRefusedRecordedAndNeverForwarded()
    {
        await using var sessions = RigSessionEnvironment.Create(
            child =>
            {
                child.Tools["browser_navigate"] = new FakeToolBehaviour();
                child.Tools["browser_take_screenshot"] = new FakeToolBehaviour();
            },
            opensDefaultSession: false,
            toolsList: UpstreamSurface.SnapshotToolsListResult());

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            sessions: sessions);

        var directory = Path.Combine(sessions.Root, "forwarded-unknown");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "meets a forwarded call with an argument its schema does not have",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        var refused = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite navigating with an argument the schema does not have",
            ["url"] = "data:text/html,x",
            ["waitUntil"] = "load",
        });

        var text = TextOf(refused);

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(text).StartsWith(
            "Syntax error: 'browser_navigate' has no argument named 'waitUntil', so nothing ran and nothing changed. "
            + "Call it again with only the arguments its definition lists:\n\n"
            + "browser_navigate: Navigate to a URL\n"
            + "Arguments:\n"
            + "- 'url' (required, string): The URL to navigate to\n"
            + "- 'session' (required, string): The session directory, exactly as browserai_init or browserai_resume returned it.");
        await Assert.That(text).Contains("\n- 'why' (required, string): Why you are making this call");

        // Never forwarded, and recorded on the session it named, as every
        // refused call is.
        await Assert.That(sessions.SessionChildren[0].ToolCallsReceived).DoesNotContain("browser_navigate");

        var row = RecordedSession.LogOf(directory).Single(entry => entry.Tool == "browser_navigate");

        await Assert.That(row.Outcome).IsEqualTo(SessionStore.Failed);
        await Assert.That(row.Failure).IsEqualTo(text);
        await Assert.That(row.Why).IsEqualTo("the suite navigating with an argument the schema does not have");

        // ⚠️ THE POSITIVE CONTROLS: the same call without it is forwarded, and a
        // call leaving out a name the schema marks required is not this refusal.
        var forwarded = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite navigating with only the arguments the schema has",
            ["url"] = "data:text/html,x",
        });

        await Assert.That((bool?)forwarded["isError"]).IsNotEqualTo(true);
        await Assert.That(sessions.SessionChildren[0].ToolCallsReceived).Contains("browser_navigate");

        var screenshot = await CallAsync(rig, "browser_take_screenshot", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite leaving out a required argument that has a default",
        });

        await Assert.That(TextOf(screenshot)).DoesNotContain("Syntax error");
        await Assert.That(sessions.SessionChildren[0].ToolCallsReceived).Contains("browser_take_screenshot");
    }

    /// <summary>
    /// The list the caller was given decides what an argument is: an argument
    /// one list carries is forwarded and the same argument against another list
    /// is refused.
    /// </summary>
    /// <remarks>
    /// <b>The arm that fails against a hand-written table.</b> Upstream's real
    /// <c>browser_navigate</c> takes <c>url</c> alone, and the arm above refuses
    /// <c>waitUntil</c> on it; here the rig's list advertises a
    /// <c>browser_navigate</c> that takes <c>waitUntil</c> too, and the same call
    /// goes through. <i>Corrected 2026-10-08 (previously "here the run's own child
    /// advertises"): the list is the binary's since that day, and here the
    /// rig's.</i>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheListTheCallerWasGivenDecidesAndNotAHandWrittenOne()
    {
        await using var sessions = RigSessionEnvironment.Create(
            child => child.Tools["browser_navigate"] = new FakeToolBehaviour(),
            opensDefaultSession: false,
            toolsList: """{"tools":[{"name":"browser_navigate","description":"Navigate to a URL","inputSchema":{"type":"object","properties":{"url":{"type":"string"},"waitUntil":{"type":"string"}},"required":["url"],"additionalProperties":false}}]}""");

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            sessions: sessions);

        var directory = Path.Combine(sessions.Root, "another-list");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "meets a list whose navigate takes waitUntil",
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        var forwarded = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite passing an argument this list carries",
            ["url"] = "data:text/html,x",
            ["waitUntil"] = "load",
        });

        await Assert.That((bool?)forwarded["isError"]).IsNotEqualTo(true);
        await Assert.That(sessions.SessionChildren[0].ToolCallsReceived).Contains("browser_navigate");

        var refused = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["session"] = directory,
            ["why"] = "the suite passing an argument no list carries",
            ["url"] = "data:text/html,x",
            ["timeout"] = 5,
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).StartsWith("Syntax error: 'browser_navigate' has no argument named 'timeout'");
        await Assert.That(TextOf(refused)).Contains("\n- 'url' (required, string)\n- 'waitUntil' (string)\n- 'session' (required, string): ");
    }

    /// <summary>
    /// A forwarded call that names no session and carries an argument its
    /// schema does not have is told both in one answer.
    /// </summary>
    /// <remarks>
    /// <b>The syntax comes first</b>, so a caller that spelled <c>session</c>
    /// wrong is told the name it used is not an argument and that
    /// <c>session</c> is required, and fixes both on the next call; told only that
    /// a session is missing, it would meet this refusal one turn later.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACallThatMisspellsSessionIsToldTheNameAndWhatIsRequiredInOneAnswer()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync();

        var refused = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["sessionDirectory"] = "C:\\work\\checkout",
            ["why"] = "the suite misspelling session",
            ["url"] = "data:text/html,x",
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).StartsWith("Syntax error: 'browser_navigate' has no argument named 'sessionDirectory'");
        await Assert.That(TextOf(refused)).Contains("\n- 'session' (required, string): ");
    }

    /// <summary>
    /// The refusal that carries a tool's whole definition reaches a model whole,
    /// for every tool in the surface.
    /// </summary>
    /// <remarks>
    /// <b>Q371.5 b, decided by the maintainer on 2026-10-03</b>: the refusal of an
    /// argument a schema does not have carries that tool's whole current
    /// definition. Measured 2026-10-03, Claude Code 2.1.288 hands a model an
    /// error result of up to about 10,000 characters whole and cuts the middle out
    /// of a longer one, and Codex does the same a little above that (see
    /// <see cref="ClientTruncationBudget.ErrorResultCharacters"/>). So the longest
    /// refusal this build can write is held to that, tool by tool, against the
    /// surface it advertises; a description that grows past it is a red build
    /// and not a definition a model receives with its middle missing.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryDefinitionCarryingRefusalFitsWhatAClientHandsAModelWhole()
    {
        var rewritten = SessionToolSurface.Rewrite(
            JsonNode.Parse(UpstreamSurface.SnapshotToolsListResult())!.AsObject(),
            RepositoryVerdicts.Committed);

        var signatures = ToolSignatures.From(rewritten);
        var over = new List<string>();
        var longest = 0;

        foreach (var name in (rewritten["tools"]?.AsArray() ?? []).Select(tool => (string)tool!["name"]!))
        {
            var refusal = SessionErrors.UnrecognisedArguments(name, ["an_argument_no_schema_declares"], signatures.Find(name)!);

            longest = Math.Max(longest, refusal.Length);

            if (refusal.Length > ClientTruncationBudget.ErrorResultCharacters)
            {
                over.Add($"{name}: {refusal.Length} characters");
            }
        }

        await Assert.That(string.Join(", ", over)).IsEmpty();

        // Not vacuous: the longest is a real refusal, not an empty walk.
        await Assert.That(longest).IsGreaterThan(2_000);
    }

    private static async Task<JsonObject> CallAsync(McpTestHarness rig, string tool, JsonObject arguments) =>
        await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));
}
