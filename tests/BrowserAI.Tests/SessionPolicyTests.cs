// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The <c>(tool, mode)</c> decision: classified exhaustively, deny-by-default,
/// and correct while sessions of different modes are being driven at once.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is where the charter's security trade-off is checked rather than
/// asserted.</b> Under four processes the <c>interactive</c> server ran without
/// the <c>storage</c> capability and the storage tools did not exist in it; under
/// one server they exist, and the only thing between an <c>interactive</c> session
/// and an <c>httpOnly</c> bearer token is a lookup. The charter's price for that
/// demotion is four properties, and each has a test below: one place,
/// deny-by-default, every tool in the surface, correct under concurrency.
/// </para>
/// <para>
/// <b>"It uses a concurrent collection" is a claim about a type, not about the
/// read-decide-act sequence around it</b> — so the concurrency arm drives real
/// calls across real sessions of different modes and asserts each answer matches
/// the handle that was passed, never a neighbour's.
/// </para>
/// </remarks>
internal sealed class SessionPolicyTests
{
    [Test]
    public async Task EveryToolTheChildCanExposeCarriesAnExplicitClassification()
    {
        var exposed = UpstreamSurface.SnapshotDescriptions().Select(tool => tool.Name).ToList();
        var classified = SessionToolPolicy.Classification.Keys.ToHashSet(StringComparer.Ordinal);

        // Both directions. An upstream tool nobody classified would be refused at
        // runtime and is a red build here, which is the whole mechanism: a new
        // tool arrives as a snapshot diff, and once that diff is accepted this is
        // what says the security judgement has not been made yet.
        var unclassified = exposed.Where(name => !classified.Contains(name)).ToList();

        // And a classification for a tool that no longer exists is stale
        // documentation, which reads as covered.
        var stale = classified.Where(name => !exposed.Contains(name, StringComparer.Ordinal)).ToList();

        await Assert.That(string.Join(", ", unclassified)).IsEmpty();
        await Assert.That(string.Join(", ", stale)).IsEmpty();

        // Stated as a number too, because two lists that drifted the same way
        // would agree with each other.
        await Assert.That(SessionToolPolicy.Classification.Count).IsEqualTo(69);
    }

    [Test]
    public async Task EachModePermitsExactlyTheSurfaceTheTestsDeclare()
    {
        var union = UpstreamSurface.For(BrowserConfiguration.UnionCapabilities);
        var offenders = new List<string>();

        // Measured 2026-08-16 from the committed snapshot: of the 59 tools
        // BrowserAI advertises, headless permits 41, interactive 41 and
        // persistent 58. The two 41s are different sets, which is the point --
        // headless refuses the annotation tool and permits arbitrary code,
        // interactive does the opposite.
        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["headless"] = 41,
            ["interactive"] = 41,
            ["persistent"] = 58,
        };

        foreach (var mode in SessionModes.All)
        {
            var allowed = union.Where(tool => SessionToolPolicy.Decide(tool, mode).IsAllowed).ToList();

            if (!expected.TryGetValue(mode.Name, out var count))
            {
                offenders.Add($"{mode.Name}: no declared surface size");
                continue;
            }

            if (allowed.Count != count)
            {
                offenders.Add($"{mode.Name}: permits {allowed.Count} of {union.Count}, declared {count}");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // The three named holes, individually, because a count is satisfied by
        // the wrong tool as easily as by the right one.
        await Assert.That(Allows("headless", "browser_run_code_unsafe")).IsTrue();
        await Assert.That(Allows("interactive", "browser_run_code_unsafe")).IsFalse();
        await Assert.That(Allows("persistent", "browser_run_code_unsafe")).IsTrue();

        await Assert.That(Allows("headless", "browser_annotate")).IsFalse();
        await Assert.That(Allows("interactive", "browser_annotate")).IsTrue();
        await Assert.That(Allows("persistent", "browser_annotate")).IsFalse();

        await Assert.That(Allows("headless", "browser_storage_state")).IsFalse();
        await Assert.That(Allows("interactive", "browser_cookie_list")).IsFalse();
        await Assert.That(Allows("persistent", "browser_cookie_list")).IsTrue();
    }

    [Test]
    public async Task ThePolicyRowsAndTheModeTableCannotDriftApart()
    {
        var offenders = new List<string>();

        foreach (var mode in SessionModes.All)
        {
            // The two expressions of the same fact. The policy row is written
            // down because a new mode's permissions must be a decision; the mode
            // table's flags say what the mode IS. They are checked against each
            // other so neither can be edited alone.
            if (Allows(mode.Name, "browser_cookie_list") != mode.Storage)
            {
                offenders.Add($"{mode.Name}: storage={mode.Storage} in the table, but the policy {(mode.Storage ? "refuses" : "permits")} the cookie tools");
            }

            // The mode whose promise is that a human types credentials the agent
            // never sees is exactly the headed mode with no stored credentials --
            // and it is the one, and only one, that must refuse the back door
            // while permitting the tool that needs a human.
            var humanIsPresent = mode.Headed && !mode.Storage;

            if (Allows(mode.Name, "browser_annotate") != humanIsPresent)
            {
                offenders.Add($"{mode.Name}: a human is {(humanIsPresent ? "" : "not ")}present by the table, and the policy disagrees about browser_annotate");
            }

            if (Allows(mode.Name, "browser_run_code_unsafe") == humanIsPresent)
            {
                offenders.Add($"{mode.Name}: arbitrary code and the human-present promise must be opposites, and here they agree");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    [Test]
    public async Task AToolNobodyClassifiedIsRefusedInEveryMode()
    {
        // Deny-by-default, from the product's own decision function rather than
        // from a test's reading of a table.
        foreach (var mode in SessionModes.All)
        {
            var decision = SessionToolPolicy.Decide("browser_a_tool_from_the_future", mode);

            await Assert.That(decision.IsAllowed).IsFalse();
            await Assert.That(decision.Refusal).Contains("does not classify");
        }
    }

    [Test]
    public async Task EveryToolTheProxyAdvertisesIsClassified()
    {
        // The surface as a caller actually receives it, rather than as the
        // snapshot records it: this is what turns "adding an unclassified tool to
        // the fake child's list fails the build" into a mechanism. Planted and
        // reverted 2026-08-16 by adding one to FakePlaywrightChild's default
        // list.
        await using var rig = await McpTestHarness.ThroughTheProxyAsync();

        var advertised = await rig.Client.RoundTripAsync("tools/list");

        var unclassified = (advertised["tools"]?.AsArray() ?? [])
            .Select(tool => (string)tool!["name"]!)
            .Where(name => !SessionToolSurface.IsAuthored(name))
            .Where(name => !SessionToolPolicy.Classification.ContainsKey(name))
            .ToList();

        await Assert.That(string.Join(", ", unclassified)).IsEmpty();

        // And the surface was non-empty, so an empty list cannot satisfy it.
        await Assert.That(advertised["tools"]?.AsArray()?.Count ?? 0).IsGreaterThan(SessionToolSurface.Names.Count);
    }

    [Test]
    public async Task AnUnclassifiedToolInTheChildsListIsRefusedOverTheWire()
    {
        // The same rule reached the way it would be in production: a child that
        // advertises something this build has never judged. It is advertised --
        // one static list, and the spec forbids varying it -- and refused when
        // called.
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult =
                """{"tools":[{"name":"browser_navigate","description":"Navigate to a URL","inputSchema":{"type":"object","properties":{}}},{"name":"browser_exfiltrate_everything","description":"A tool no build of BrowserAI has classified","inputSchema":{"type":"object","properties":{}}}]}""",
            sessions: sessions);

        var directory = Path.Combine(sessions.Root, "unclassified");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "meets a tool from the future",
            ["mode"] = "persistent",
        });

        var answer = await CallAsync(rig, "browser_exfiltrate_everything", new JsonObject
        {
            ["session"] = directory,
        });

        await Assert.That((bool?)answer["isError"]).IsTrue();
        await Assert.That(TextOf(answer)).Contains("does not classify");

        // And the child was never asked, which is the difference between
        // refusing and forwarding-then-hiding.
        await Assert.That(sessions.SessionChildren.Sum(child => child.MethodsReceived.Count(method => method is "tools/call"))).IsEqualTo(0);
    }

    [Test]
    public async Task AStorageToolOnAHeadlessSessionIsRefusedWithTextNamingPersistent()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "headless-session");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "the headless session a storage tool is refused on",
            ["mode"] = "headless",
        });

        var answer = await CallAsync(rig, "browser_storage_state", new JsonObject
        {
            ["session"] = directory,
        });

        var text = TextOf(answer);

        await Assert.That((bool?)answer["isError"]).IsTrue();

        // The mode that WOULD permit it, derived from the table rather than
        // written by hand -- so a reclassification changes this sentence with
        // nobody editing it.
        await Assert.That(text).Contains("'persistent'");
        await Assert.That(text).Contains("'headless'");
        await Assert.That(text).Contains("browser_storage_state");

        // Names the fix and does not blame the caller for a decision we made.
        await Assert.That(text).Contains(SessionToolSurface.Init);
        await Assert.That(text).Contains("working as designed");

        // Nothing reached the child.
        await Assert.That(sessions.SessionChildren.Sum(child => child.MethodsReceived.Count(method => method is "tools/call"))).IsEqualTo(0);
    }

    [Test]
    public async Task TheHandleToTypeLookupHoldsUnderConcurrencyAcrossModes()
    {
        // ⚠️ The race this exists for is not a glitch: an `interactive` handle
        // resolving to a `persistent` classification for one call is an
        // ENFORCEMENT BYPASS, and it presents as nothing at all -- a successful
        // call, a correct-looking result, and a cookie that should never have
        // left the session. So this drives real calls across sessions of
        // DIFFERENT modes, all outstanding at the server together, WHILE other
        // sessions are being opened and destroyed on the same connection, and
        // checks every answer against the mode of the handle that call named.
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directories = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var mode in SessionModes.All)
        {
            var directory = Path.Combine(sessions.Root, $"concurrent-{mode.Name}");
            directories[mode.Name] = directory;

            var opened = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
            {
                ["directory"] = directory,
                ["purpose"] = $"the {mode.Name} session driven concurrently",
                ["mode"] = mode.Name,
            });

            await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true);
        }

        // Four tools chosen so that no two modes agree about all of them: a
        // handle resolved to a neighbour's classification changes at least one
        // answer whichever pair of modes got swapped.
        string[] probes = ["browser_storage_state", "browser_run_code_unsafe", "browser_annotate", "browser_navigate"];
        const int Rounds = 25;

        var requests = new List<(string Method, JsonNode? Parameters)>();
        var expectations = new List<(SessionModeDefinition Mode, string Tool)>();

        foreach (var round in Enumerable.Range(0, Rounds))
        {
            foreach (var mode in SessionModes.All)
            {
                foreach (var tool in probes)
                {
                    expectations.Add((mode, tool));
                    requests.Add(("tools/call", new JsonObject
                    {
                        ["name"] = tool,
                        ["arguments"] = new JsonObject
                        {
                            ["session"] = directories[mode.Name],
                            ["round"] = round,
                        },
                    }));
                }
            }

            // Churn, interleaved into the same batch rather than run beside it:
            // the dictionary the lookup reads is being written while the
            // decisions above are being taken, which is the only state that
            // could produce the race.
            var churn = Path.Combine(sessions.Root, $"churn-{round}");

            expectations.Add((SessionModes.Recorded("persistent"), SessionToolSurface.Init));
            requests.Add(("tools/call", new JsonObject
            {
                ["name"] = SessionToolSurface.Init,
                ["arguments"] = new JsonObject
                {
                    ["directory"] = churn,
                    ["purpose"] = "opened and destroyed while the probes run",
                    ["mode"] = "persistent",
                },
            }));

            expectations.Add((SessionModes.Recorded("persistent"), SessionToolSurface.Destroy));
            requests.Add(("tools/call", new JsonObject
            {
                ["name"] = SessionToolSurface.Destroy,
                ["arguments"] = new JsonObject { ["directory"] = churn },
            }));
        }

        var answers = await rig.Client.RoundTripManyAsync(requests);

        await Assert.That(answers.Count).IsEqualTo(requests.Count);

        // The ids are 1-based and allocated in order, and the first request in
        // this batch is not the first the client ever sent, so the offset is
        // read off the batch rather than assumed.
        var firstId = answers.Min(answer => answer.Id);
        var wrong = new ConcurrentBag<string>();

        foreach (var (id, envelope) in answers)
        {
            var index = id - firstId;
            var (mode, tool) = expectations[index];

            if (SessionToolSurface.IsAuthored(tool))
            {
                continue;
            }

            var result = envelope["result"]?.AsObject() ?? [];
            var text = TextOf(result);
            var refused = (bool?)result["isError"] is true && text.Contains("needs a session in", StringComparison.Ordinal);
            var expected = SessionToolPolicy.Decide(tool, mode);

            if (refused == expected.IsAllowed)
            {
                wrong.Add($"{tool} on the '{mode.Name}' session: expected {(expected.IsAllowed ? "allow" : "refuse")}, got {(refused ? "refuse" : "allow")} — {text}");
                continue;
            }

            // And when it was refused, the refusal names THIS handle's mode
            // rather than a neighbour's — the half a pass/fail count cannot see,
            // because a swapped lookup that happened to agree on the verdict
            // would still be a swapped lookup.
            if (!refused)
            {
                continue;
            }

            foreach (var other in SessionModes.All.Where(candidate => candidate.Name != mode.Name))
            {
                if (text.Contains($"this one is '{other.Name}'", StringComparison.Ordinal))
                {
                    wrong.Add($"{tool} on the '{mode.Name}' session was refused as though it were '{other.Name}'");
                }
            }
        }

        await Assert.That(string.Join(Environment.NewLine, wrong.Take(10))).IsEmpty();

        // The batch really was concurrent rather than a queue the client drained
        // one at a time: answers came back in a different order from the
        // requests. Asserted rather than noted, because a serialising server
        // would make every claim above evidence about one call at a time.
        var outOfOrder = answers
            .Select((answer, position) => answer.Id - firstId != position)
            .Count(moved => moved);

        await Assert.That(outOfOrder).IsGreaterThan(0);
    }

    private static bool Allows(string mode, string tool) =>
        SessionToolPolicy.Decide(tool, SessionModes.Recorded(mode)).IsAllowed;

    private static async Task<JsonObject> CallAsync(McpTestHarness rig, string tool, JsonObject arguments)
    {
        var result = await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

        return result;
    }

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));
}
