// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Hosting;
using BrowserAI.Proxy;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The two refusals that send a caller back to its tool list name every tool this
/// BrowserAI has now, each with one line saying what it does.
/// </summary>
/// <remarks>
/// <para>
/// <b>Decided 2026-10-04 by the maintainer, 2 b</b>: the refusal for a tool
/// BrowserAI does not have, and Q261 b's once-per-connection refusal for a tool
/// list read before this server started, carry every current tool's name and one
/// line saying what it does. He had first chosen the whole list, definitions and
/// all (Q369.3 c and Q371.6 c); measured on 2026-10-03, no client hands a model an
/// error result that long whole, so the list is names and one line each.
/// </para>
/// <para>
/// <b>Generated from the live list and never written here.</b> Each arm reads the
/// list the server answers and holds the block against it, tool by tool and in its
/// order, so a block that a person typed, or one that went on naming a tool after
/// it was denied, is a red build. The line is the tool's own title where it has
/// one, which BrowserAI's tools do, and otherwise the first sentence of its
/// description, which Playwright's titles are too short to replace: two of them
/// read "Click" and two "Drag mouse".
/// </para>
/// </remarks>
internal sealed class ToolListInRefusalsTests
{
    /// <summary>The line that opens the block, as the refusals write it.</summary>
    private const string Header = "\n\nThe tools this BrowserAI has now:\n";

    /// <summary>
    /// A call naming a tool BrowserAI does not have is answered with every tool it
    /// does have, in the order its list carries them, each with what it does.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ANameBrowserAiDoesNotHaveIsAnsweredWithEveryToolItHasAndWhatEachDoes()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            toolsList: UpstreamSurface.SnapshotToolsListResult());

        var answer = await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_frobnicate",
            ["arguments"] = new JsonObject
            {
                ["session"] = rig.Session!,
                ["why"] = "the suite calling a tool nobody built",
            },
        });

        var listed = await rig.Client.RoundTripAsync("tools/list", new JsonObject());
        var text = TextOf(answer);

        await Assert.That((bool?)answer["isError"]).IsTrue();

        // The approved sentences come first and are unchanged, the reconnect
        // sentence included, and the list the client was given follows them. Since
        // 2026-10-10 the refusal is only ever built with its list, round 2 of the texts
        // review, #49 (previously this held a prefix built with none).
        await Assert.That(text).IsEqualTo(SessionErrors.ToolDoesNotExist("browser_frobnicate", ToolSignatures.From(listed)));

        await Assert.That(string.Join(Environment.NewLine, Disagreements(text, listed))).IsEmpty();
        await Assert.That(text.Length).IsLessThanOrEqualTo(ClientTruncationBudget.ErrorResultCharacters);
    }

    /// <summary>
    /// Q261 b's refusal, for a connection that never asked for a tool list, names
    /// every tool this server has now, each with what it does.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheStaleListRefusalNamesEveryToolTheServerHasNow()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false,
            toolsList: UpstreamSurface.SnapshotToolsListResult());
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            sessions: sessions,
            listsBeforeCalling: false);

        var first = await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject
            {
                ["url"] = "data:text/html,<h1>ok</h1>",
                ["why"] = "the suite calling before it listed",
            },
        });

        // Read after the refusal, because listing first is what would have
        // prevented it.
        var listed = await rig.Client.RoundTripAsync("tools/list", new JsonObject());
        var text = TextOf(first);

        await Assert.That((bool?)first["isError"]).IsTrue();
        await Assert.That(text).StartsWith(SessionErrors.ToolListPredatesThisServer("browser_navigate", BuildVersion.Current, "BrowserAI.RawPipeClient"));

        await Assert.That(string.Join(Environment.NewLine, Disagreements(text, listed))).IsEmpty();
        await Assert.That(text.Length).IsLessThanOrEqualTo(ClientTruncationBudget.ErrorResultCharacters);
    }

    /// <summary>
    /// Both refusals, written against the whole surface this build advertises, fit
    /// what a client hands a model whole.
    /// </summary>
    /// <remarks>
    /// <b>The budget is the measured one</b>: Claude Code 2.1.288 hands a model an
    /// error result of up to about 10,100 characters whole and cuts the middle out of
    /// a longer one, measured 2026-10-03 (see
    /// <see cref="ClientTruncationBudget.ErrorResultCharacters"/>). The longest
    /// spellings are the ones taken here: a 64-character tool name, the most a
    /// client's own tool list can carry, and Q261 b's sentence for each client it
    /// knows and for one it does not, through the session host and not.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task BothRefusalsThatNameEveryToolFitWhatAClientHandsAModelWhole()
    {
        var tools = ToolSignatures.From(SessionToolSurface.Rewrite(
            JsonNode.Parse(UpstreamSurface.SnapshotToolsListResult())!.AsObject(),
            RepositoryVerdicts.Committed));

        var name = new string('x', 64);
        var refusals = new List<string> { SessionErrors.ToolDoesNotExist(name, tools) };

        foreach (var client in new string?[] { null, KnownClients.ClaudeCode, KnownClients.Codex, "an-unknown-client" })
        {
            refusals.Add(SessionErrors.ToolListPredatesThisServer(name, BuildVersion.Current, client, tools));
        }

        var longest = refusals.Max(refusal => refusal.Length);

        await Assert.That(longest).IsLessThanOrEqualTo(ClientTruncationBudget.ErrorResultCharacters);

        // Not vacuous: every refusal carries the block, and the block carries the
        // whole advertised surface.
        foreach (var refusal in refusals)
        {
            await Assert.That(refusal).Contains(Header);
        }

        // ⚠️ 63 upstream tools since 2026-10-08 (previously 64): F1 a denied
        // browser_close beside the eight denials before it, and browserai_close joined
        // BrowserAI's own, so the block still lists 72 and Names counts one more.
        await Assert.That(refusals[0].Split('\n').Count(line => line.StartsWith("- ", StringComparison.Ordinal)))
            .IsEqualTo(SessionToolSurface.Names.Count + 63);
    }

    /// <summary>
    /// Everything about the block that disagrees with the list the server answers.
    /// </summary>
    /// <param name="refusal">The refusal's text.</param>
    /// <param name="listed">The <c>tools/list</c> result.</param>
    /// <returns>One line per disagreement.</returns>
    private static List<string> Disagreements(string refusal, JsonObject listed)
    {
        var faults = new List<string>();
        var at = refusal.IndexOf(Header, StringComparison.Ordinal);

        if (at < 0)
        {
            faults.Add("the refusal carries no block of tools");
            return faults;
        }

        var lines = refusal[(at + Header.Length)..].Split('\n');
        var tools = (listed["tools"]?.AsArray() ?? []).OfType<JsonObject>().ToList();

        if (lines.Length != tools.Count)
        {
            faults.Add($"the block has {lines.Length} lines and the list has {tools.Count} tools");
        }

        foreach (var (line, tool) in lines.Zip(tools))
        {
            var name = (string)tool["name"]!;
            var prefix = $"- {name}";

            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                faults.Add($"'{line}' is where the list has '{name}'");
                continue;
            }

            var said = line.Length > prefix.Length && line[prefix.Length..].StartsWith(": ", StringComparison.Ordinal)
                ? line[(prefix.Length + 2)..]
                : string.Empty;

            var title = (string?)tool["title"];
            var description = (string?)tool["description"] ?? string.Empty;

            if (title is { Length: > 0 })
            {
                if (!string.Equals(said, title, StringComparison.Ordinal))
                {
                    faults.Add($"{name}: '{said}' is not its title, '{title}'");
                }
            }
            else if (said.Length is 0 || !description.StartsWith(said, StringComparison.Ordinal))
            {
                faults.Add($"{name}: '{said}' is not the start of its description");
            }
        }

        return faults;
    }

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? []).Select(block => (string?)block?["text"] ?? string.Empty));
}
