// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Clients;

namespace BrowserAI.Tests;

/// <summary>
/// The title BrowserAI gives a Claude Code conversation is the one the VS Code
/// extension gives it, read from the first and the last 64 KB of its record by the
/// extension's own rule.
/// </summary>
/// <remarks>
/// <para>
/// <b>1.2 a, the maintainer's answer of 2026-10-10, verbatim: <i>"1.1-2.3 I accept all
/// your recommendations"</i></b>: custom title from the end, then the start; AI title
/// from the end, then the start; the last prompt; the summary; the first real prompt; and
/// <i>Image</i> or <i>Document</i>. Read in the extension's <c>extension.js</c> at 2.1.292
/// and 2.1.296.
/// </para>
/// <para>
/// <b>Every record here is written by the suite</b>, in the shapes the measurement of
/// 2026-10-08 read in Claude Code's own records; none is the maintainer's.
/// </para>
/// </remarks>
internal sealed class ClaudeCodeTitleTests
{
    /// <summary>
    /// A title the person gave wins over one the model gave, from the end before the
    /// start, and of several in one window the last wins.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-10-10</b> against a rule that read the start before the end,
    /// and against one that kept the first occurrence in a window.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCustomTitleWinsThenTheAiTitleEachFromTheEndFirstAndTheLastOneInAWindow()
    {
        var head = Lines(Prompt("Fix the login bug"), Custom("Named at the start"), Ai("Model title at the start"));
        var tail = Lines(Ai("Model title one"), Custom("Renamed once"), Custom("Renamed twice"), Ai("Model title two"));

        await Assert.That(ClaudeCodeTitle.Of(head, tail)).IsEqualTo(("Renamed twice", TitleSource.CustomTitle));

        // No custom title at the end: the start's.
        await Assert.That(ClaudeCodeTitle.Of(head, Lines(Ai("Model title one"), Ai("Model title two")))).IsEqualTo(("Named at the start", TitleSource.CustomTitle));

        // No custom title anywhere: the end's AI title, the last of them.
        await Assert.That(ClaudeCodeTitle.Of(Lines(Prompt("Fix the login bug"), Ai("Model title at the start")), Lines(Ai("Model title one"), Ai("Model title two"))))
            .IsEqualTo(("Model title two", TitleSource.AiTitle));

        // And with none at the end, the start's.
        await Assert.That(ClaudeCodeTitle.Of(Lines(Prompt("Fix the login bug"), Ai("Model title at the start")), Lines(Prompt("and the tests"))))
            .IsEqualTo(("Model title at the start", TitleSource.AiTitle));
    }

    /// <summary>
    /// With no title, the last prompt answers, then the summary, then the first real
    /// prompt, then the attachment a first prompt was.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-10-10</b> against a rule that went to the first prompt before
    /// the last.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WithNoTitleTheLastPromptThenTheSummaryThenTheFirstPromptAnswer()
    {
        var head = Lines(Prompt("Fix the login bug"), Prompt("and the tests too"));

        await Assert.That(ClaudeCodeTitle.Of(head, Lines(Record("last-prompt", "lastPrompt", "and the tests too"), Record("summary", "summary", "Login fixed"))))
            .IsEqualTo(("and the tests too", TitleSource.LastPrompt));

        await Assert.That(ClaudeCodeTitle.Of(head, Lines(Record("summary", "summary", "Login fixed"))))
            .IsEqualTo(("Login fixed", TitleSource.Summary));

        await Assert.That(ClaudeCodeTitle.Of(head, head)).IsEqualTo(("Fix the login bug", TitleSource.FirstPrompt));

        var image = Line(new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject { ["role"] = "user", ["content"] = new JsonArray(new JsonObject { ["type"] = "image", ["source"] = new JsonObject() }) },
        });

        await Assert.That(ClaudeCodeTitle.Of(image, image)).IsEqualTo(("Image", TitleSource.FirstPrompt));

        // A record that names nothing at all names nothing.
        var nothing = Lines(Record("system", "content", "started"));

        await Assert.That(ClaudeCodeTitle.Of(nothing, nothing)).IsNull();
    }

    /// <summary>
    /// The first real prompt is a person's: Claude Code's own records, tool results,
    /// compacted summaries, tags and interruptions are passed over, a <c>!</c> line is
    /// shown after an exclamation mark, a slash command only names it when nothing else
    /// does, and a long prompt is cut at 200 characters.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-10-10</b> against a rule that took a line Claude Code wrote
    /// itself, a tag first or an interruption, for the person's prompt.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheFirstPromptIsTheFirstOneAPersonTyped()
    {
        var meta = Line(new JsonObject { ["type"] = "user", ["isMeta"] = true, ["message"] = new JsonObject { ["role"] = "user", ["content"] = "Caveat: written by Claude Code" } });
        var compact = Line(new JsonObject { ["type"] = "user", ["isCompactSummary"] = true, ["message"] = new JsonObject { ["role"] = "user", ["content"] = "This session continues" } });
        var toolResult = Line(new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject { ["role"] = "user", ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["content"] = "ok" }) },
        });
        var command = Prompt("<command-name>/model</command-name>\n<command-args></command-args>");
        var tagged = Prompt("<local-command-stdout>Set model</local-command-stdout>");
        var interrupted = Prompt("[Request interrupted by user for tool use]");
        var typed = Line(new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "Open the page\nand read it" }),
            },
        });

        var head = Lines(meta, compact, toolResult, command, tagged, interrupted, typed);

        await Assert.That(ClaudeCodeTitle.FirstPrompt(head)).IsEqualTo("Open the page and read it");

        // A slash command names the conversation only when nothing else does.
        await Assert.That(ClaudeCodeTitle.FirstPrompt(Lines(meta, command, tagged))).IsEqualTo("/model");

        // A line typed with ! is shown after an exclamation mark.
        await Assert.That(ClaudeCodeTitle.FirstPrompt(Lines(Prompt("<bash-input>git status</bash-input>")))).IsEqualTo("! git status");

        // A long prompt is cut at 200 characters, with three full stops.
        var longPrompt = new string('a', 150) + " " + new string('b', 100);

        await Assert.That(ClaudeCodeTitle.FirstPrompt(Lines(Prompt(longPrompt)))).IsEqualTo(longPrompt[..ClaudeCodeTitle.PromptWidth] + "...");

        // A pasted block is shown as what was pasted, its marks and the line breaks
        // around them gone, as the extension joins the parts.
        await Assert.That(ClaudeCodeTitle.FirstPrompt(Lines(Prompt("Look at this\n\n<pasted_content id=\"1a2b\">\nthe log line\n</pasted_content id=\"1a2b\">\n"))))
            .IsEqualTo("Look at thisthe log line");
    }

    /// <summary>
    /// A field is found as the extension finds it: in either spacing, escapes decoded, a
    /// value the window cut before its closing quote passed over, and an empty title
    /// stops the search for one, so the prompts answer.
    /// </summary>
    /// <remarks>
    /// <b>Planted red 2026-10-10</b> against a reader that took a value with no closing
    /// quote to the end of the window, and against one that read on to the AI title past
    /// an empty custom title.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFieldIsFoundAsTextTheWayTheExtensionFindsIt()
    {
        await Assert.That(ClaudeCodeTitle.Field("{\"type\":\"ai-title\", \"aiTitle\": \"Spaced \\\"quoted\\\" caf\\u00e9\"}", "aiTitle"))
            .IsEqualTo("Spaced \"quoted\" café");

        // The last occurrence that closes wins; one cut off by the window does not.
        await Assert.That(ClaudeCodeTitle.Field(Lines(Ai("Whole title")) + "{\"type\":\"ai-title\",\"aiTitle\":\"Cut off by the win", "aiTitle"))
            .IsEqualTo("Whole title");

        // An empty custom title hides the AI title, as `??` does in the extension.
        var empty = Lines(Prompt("Fix the login bug"), Custom(string.Empty), Ai("Model title"));

        await Assert.That(ClaudeCodeTitle.Of(empty, empty)).IsEqualTo(("Fix the login bug", TitleSource.FirstPrompt));
    }

    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    private static string Line(JsonObject record) => record.ToJsonString();

    private static string Prompt(string text) =>
        Line(new JsonObject { ["type"] = "user", ["message"] = new JsonObject { ["role"] = "user", ["content"] = text }, ["uuid"] = Guid.NewGuid().ToString() });

    private static string Custom(string title) => Record("custom-title", "customTitle", title);

    private static string Ai(string title) => Record("ai-title", "aiTitle", title);

    private static string Record(string type, string field, string value) =>
        Line(new JsonObject { ["type"] = type, [field] = value, ["sessionId"] = "aaaaaaaa-1111-4111-8111-111111111111" });
}
