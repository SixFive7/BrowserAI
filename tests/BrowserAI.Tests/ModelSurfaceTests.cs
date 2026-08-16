// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Proxy;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// One table, six consumers, and the check that runs in both directions.
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure this file exists to catch has already happened once in this
/// project's history:</b> a fourth thing is added, three copies are updated, and
/// the fourth silently describes a system that no longer exists. Nothing breaks;
/// a model simply stops being told about a mode it should have picked, and the
/// symptom is an agent reporting a site as broken because it never logged in.
/// </para>
/// <para>
/// <b>So the assertion is bidirectional, and both directions were planted and
/// reverted on 2026-08-16.</b> Adding a fourth row to
/// <see cref="SessionModes.All"/> and nothing else turns
/// <see cref="EveryConsumerRendersEveryModeInTheTable"/> red, because the new
/// mode has no policy row and no expectation row; removing a mode from one
/// consumer — by hard-coding a rendering rather than deriving it — turns the same
/// test red from the other side.
/// </para>
/// </remarks>
internal sealed class ModelSurfaceTests
{
    /// <summary>
    /// The declared list of upstream phrases whose disappearance is a red build.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not a whole-string comparison</b> — the whole point of the rewrite is
    /// that the string changes. This is the record of <i>why</i> a sentence is
    /// there: a phrase is added the moment anyone decides upstream's wording is
    /// load-bearing, and a rewrite that drops it fails rather than quietly
    /// removing the warning a model was acting on.
    /// </para>
    /// <para>
    /// Every phrase below is one a model acts on — what a tool costs, what it
    /// refuses, that it is dangerous — read out of the committed snapshot on
    /// 2026-08-16 at <c>@playwright/mcp</c> 0.0.79.
    /// </para>
    /// </remarks>
    private static readonly (string Tool, string Phrase)[] LoadBearingUpstreamPhrases =
    [
        // The single most important sentence upstream ships: it is what tells a
        // model that this tool is not an ordinary one.
        ("browser_run_code_unsafe", "RCE-equivalent"),
        ("browser_run_code_unsafe", "executes arbitrary JavaScript in the Playwright server process"),

        // Blocks on a human. A model that does not know this schedules it and
        // waits forever.
        ("browser_annotate", "wait for the user to draw annotations"),

        // Names the tool whose output the argument comes from; without it the
        // number is unguessable.
        ("browser_network_request", "Use the number from browser_network_requests"),

        // Says the config is the RESOLVED one, which is the whole reason to call
        // it rather than to read the file.
        ("browser_get_config", "after merging CLI options, environment variables and config file"),

        // Where the credentials go, said in the description rather than only in
        // the schema.
        ("browser_storage_state", "cookies, local storage"),
    ];

    /// <summary>
    /// What each mode is expected to refuse, written down rather than computed.
    /// </summary>
    /// <remarks>
    /// <b>This is the sixth consumer of the one table, and it is a consumer
    /// precisely because it is written by hand.</b> Derived from the policy it
    /// would agree with the policy by construction and could never fail; written
    /// down, a reclassified tool or a fourth mode is a red build with a number in
    /// the message. The counts are of the <b>59-tool union surface</b> BrowserAI
    /// advertises — measured 2026-08-16 from the committed snapshot.
    /// </remarks>
    private static readonly (string Mode, int Allowed, string[] Refused)[] Expected =
    [
        ("headless", 41, ["browser_cookie_list", "browser_storage_state", "browser_localstorage_get", "browser_annotate"]),
        ("interactive", 41, ["browser_cookie_list", "browser_storage_state", "browser_localstorage_get", "browser_run_code_unsafe"]),
        ("persistent", 58, ["browser_annotate"]),
    ];

    [Test]
    public async Task EveryConsumerRendersEveryModeInTheTable()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var missing = new List<string>();

        // Consumer 2 — init's description, read out of the advertised surface
        // rather than off the class, so what is checked is what a model receives.
        var advertised = Advertised(rig.SurfaceChild.ToolsListResult);
        var initDescription = (string?)advertised[SessionToolSurface.Init]?["description"] ?? string.Empty;

        // Consumer 4 — the refusal channel, triggered rather than quoted: a mode
        // argument that is not one of the three is answered with the table.
        var badMode = await TextOfAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = Path.Combine(sessions.Root, "never-created"),
            ["purpose"] = "should never be created",
            ["mode"] = "headed",
        });

        foreach (var mode in SessionModes.All)
        {
            // 1. The server instructions.
            Require(missing, ServerInstructions.Text, mode.Name, "the server instructions");
            Require(missing, ServerInstructions.Text, mode.Grants, "the server instructions");

            // 2. init's description.
            Require(missing, initDescription, mode.Name, "browserai_init's description");
            Require(missing, initDescription, mode.Grants, "browserai_init's description");

            // 3. resume's result — one session per mode, opened and reopened.
            var resumed = await ResumeInModeAsync(rig, sessions, mode);
            Require(missing, resumed, mode.Name, "browserai_resume's result");
            Require(missing, resumed, mode.Grants, "browserai_resume's result");

            // 4. The refusal channel.
            Require(missing, badMode, mode.Name, "the refusal text");
            Require(missing, badMode, mode.Grants, "the refusal text");

            // 5. Session-type enforcement. Deny-by-default means an unclassified
            //    mode refuses everything, so this asks for a real policy row
            //    rather than for the sentinel.
            if (SessionToolPolicy.Decide("browser_navigate", mode) is { IsAllowed: false })
            {
                missing.Add($"session-type enforcement has no policy row for '{mode.Name}': {SessionToolPolicy.Summary(mode.Mode)}");
            }

            // 6. The tests. A mode nobody wrote an expectation for is a mode
            //    nobody checked, which reads as covered and is not.
            if (!Expected.Any(row => row.Mode == mode.Name))
            {
                missing.Add($"the tests carry no expected surface for '{mode.Name}'");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, missing)).IsEmpty();
    }

    [Test]
    public async Task TheInstructionsStringFitsTheClientsSilentTruncationBudget()
    {
        // In BYTES rather than characters, and that is not pedantry: this string
        // carries '·' (2 bytes) and '—' (3 bytes), so a character count
        // under-reports precisely the string that uses them. The client cuts at
        // 2 KB with nothing reported, so anything past the cut has never been
        // read by anybody.
        await Assert.That(ServerInstructions.ByteCount).IsLessThanOrEqualTo(ServerInstructions.MaximumBytes);

        // Non-empty and actually wired: an instructions string the server never
        // sends is the same as not having one.
        await Assert.That(ServerInstructions.ByteCount).IsGreaterThan(400);

        // ⚠️ Measured 2026-08-16: 1,613 characters and **1,628 bytes**, against
        // §H.3's predicted ~1,050. The prediction was written before the mode
        // lines carried what each mode REFUSES, which is most of the difference.
        //
        // The headroom is 420 bytes, and it is deliberately NOT a gate. Planting
        // a fourth mode measured its cost at 223 bytes, leaving 197 — so §H.3's
        // claim that the headroom "absorbs a fourth mode without a rewrite" is
        // true exactly once, and a fifth would need the lines shortened. A gate
        // on the headroom would have failed that plant on a budget line instead
        // of on the six-consumer line it was aimed at, which is the wrong test
        // failing; the hard cap above is the requirement and it already catches
        // running out.

        await using var rig = await McpTestHarness.ThroughTheProxyAsync();
        var initialize = await rig.Client.RoundTripAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = TestDefaults.CallerProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "budget-probe", ["version"] = "0" },
        });

        await Assert.That((string?)initialize["instructions"]).IsEqualTo(ServerInstructions.Text);
    }

    [Test]
    public async Task EveryToolDescriptionFitsTheSameBudget()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult = UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.SurfaceChild.ToolsListResult);
        var oversized = new List<string>();

        foreach (var (name, tool) in advertised)
        {
            var description = (string?)tool?["description"] ?? string.Empty;
            var bytes = Encoding.UTF8.GetByteCount(description);

            if (bytes > SessionToolSurface.DescriptionMaximumBytes)
            {
                oversized.Add($"{name}: {bytes} bytes, over the {SessionToolSurface.DescriptionMaximumBytes} the client silently truncates at");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, oversized)).IsEmpty();

        // The whole surface, not a sample: 5 authored plus the 69 upstream can
        // ever expose. A count that had quietly shrunk would make the loop above
        // pass by measuring less.
        await Assert.That(advertised.Count).IsEqualTo(SessionToolSurface.Names.Count + 69);
    }

    [Test]
    public async Task EveryLoadBearingUpstreamPhraseSurvivesOurRewrite()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult = UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.SurfaceChild.ToolsListResult);
        var lost = new List<string>();

        foreach (var (tool, phrase) in LoadBearingUpstreamPhrases)
        {
            var description = (string?)advertised[tool]?["description"];

            if (description is null)
            {
                lost.Add($"{tool}: not in the advertised surface at all, so its declared phrase cannot be checked");
                continue;
            }

            if (!description.Contains(phrase, StringComparison.Ordinal))
            {
                lost.Add($"{tool}: lost '{phrase}' — either our rewrite dropped it or upstream reworded it, and both are changes nobody adjudicated");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, lost)).IsEmpty();
    }

    [Test]
    public async Task OurAdditionIsAppendedAndNeverReplacesUpstreamsOwnText()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult = UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.SurfaceChild.ToolsListResult);
        var upstream = UpstreamSurface.SnapshotDescriptions();
        var offenders = new List<string>();

        foreach (var (name, original) in upstream)
        {
            var rewritten = (string?)advertised[name]?["description"] ?? string.Empty;

            // Append-only, asserted as a prefix rather than as containment: an
            // insertion in the middle would still "contain" upstream's text
            // while changing where a model reads it.
            if (!rewritten.StartsWith(original, StringComparison.Ordinal))
            {
                offenders.Add($"{name}: upstream's description is no longer the start of ours");
            }

            var note = SessionToolPolicy.Note(name);

            if (note is null && rewritten.Length != original.Length)
            {
                offenders.Add($"{name}: every mode permits it, so nothing should have been appended");
            }

            if (note is not null && !rewritten.EndsWith(note, StringComparison.Ordinal))
            {
                offenders.Add($"{name}: the mode note is missing from the end of the description");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // And the note itself is derived from the table: the storage tools name
        // the mode that permits them rather than carrying a hand-written word.
        var storage = (string?)advertised["browser_storage_state"]?["description"] ?? string.Empty;
        await Assert.That(storage).Contains("'persistent'");
        await Assert.That(storage).Contains("'headless'");
    }

    [Test]
    public async Task NoConditionalCompilationReachesTheEnforcementPath()
    {
        // §H.6, and it is a property of the artifact rather than of the source:
        // the decision a released binary takes must be the decision the suite
        // took. A `#if DEBUG` here would make every test above evidence about a
        // build nobody ships.
        string[] enforcement =
        [
            "src/BrowserAI/Sessions/SessionToolPolicy.cs",
            "src/BrowserAI/Sessions/SessionMode.cs",
            "src/BrowserAI/Sessions/SessionErrors.cs",
            "src/BrowserAI/Proxy/BrowserProxy.cs",
            "src/BrowserAI/Proxy/ServerInstructions.cs",
        ];

        string[] forbidden = ["#if", "#else", "#elif", "[Conditional", "System.Diagnostics.Conditional"];
        var offenders = new List<string>();

        foreach (var relative in enforcement)
        {
            var file = new FileInfo(Path.Combine(RepositoryLayout.Root.FullName, relative));

            if (!file.Exists)
            {
                offenders.Add($"{relative}: missing, so this scan no longer covers the path it names");
                continue;
            }

            // Comments stripped, because this file's own prose says "no #if, no
            // [Conditional]" -- and a scan that could not tell a rule from its
            // statement would fail on the sentence describing it.
            var code = await RepositoryLayout.ReadCodeAsync(file);

            offenders.AddRange(forbidden
                .Where(needle => code.Contains(needle, StringComparison.Ordinal))
                .Select(needle => $"{relative}: carries '{needle}' on the enforcement path"));
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    private static void Require(List<string> missing, string rendered, string expected, string consumer)
    {
        if (!rendered.Contains(expected, StringComparison.Ordinal))
        {
            missing.Add($"{consumer} does not render '{expected}'");
        }
    }

    /// <summary>The advertised surface, keyed by name, as a caller receives it.</summary>
    private static Dictionary<string, JsonObject?> Advertised(string childToolsList)
    {
        var rewritten = SessionToolSurface.Rewrite(JsonNode.Parse(childToolsList)!.AsObject());

        return (rewritten["tools"]?.AsArray() ?? [])
            .ToDictionary(tool => (string)tool!["name"]!, tool => tool?.AsObject(), StringComparer.Ordinal);
    }

    /// <summary>Opens a session in one mode, closes nothing, and resumes it.</summary>
    private static async Task<string> ResumeInModeAsync(
        McpTestHarness rig,
        RigSessionEnvironment sessions,
        SessionModeDefinition mode)
    {
        var directory = Path.Combine(sessions.Root, $"consumer-{mode.Name}");

        _ = await TextOfAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = $"the {mode.Name} session the mode-table test resumes",
            ["mode"] = mode.Name,
        });

        return await TextOfAsync(rig, SessionToolSurface.Resume, new JsonObject
        {
            ["directory"] = directory,
        });
    }

    private static async Task<string> TextOfAsync(McpTestHarness rig, string tool, JsonObject arguments)
    {
        var result = await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

        return string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));
    }
}
