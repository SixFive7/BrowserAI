// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// What BrowserAI decides about a browser call: which child it goes to, and the
/// one call it declines to make because it would never come back.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Rewritten 2026-08-18 (previously "the <c>(tool, mode)</c> decision:
/// classified exhaustively, deny-by-default, and correct while sessions of
/// different modes are being driven at once").</b> Seven of this file's eight
/// tests asserted a permission matrix that no longer exists. <b>Six were deleted
/// outright</b> and the seventh was inverted, rather than any of them being left
/// asserting a tautology — a test that can no longer fail is worse than a gap,
/// because it reads as covered. The eighth, the concurrency arm, was reframed and
/// is the last test below. Across the suite that is <b>432 tests before and 428
/// after</b>: six deleted here plus one in <c>ErrorCatalogueTests</c>
/// (<c>AConfigAnswerCarryingSecretsIsWithheldRatherThanForwarded</c>, which
/// provoked the removed <c>Guard</c>), against three added. What went, and why
/// each was testing something that is gone:
/// </para>
/// <list type="bullet">
/// <item><c>EveryToolTheChildCanExposeCarriesAnExplicitClassification</c> and
/// <c>EveryToolTheProxyAdvertisesIsClassified</c> — there is no classification
/// table to be exhaustive about. The change-detection they provided is the
/// [golden snapshot](../../upstream-snapshots/tools-list.json)'s job, and the
/// snapshot does it better: it diffs each tool's <c>inputSchema</c> as well as
/// its name, which a name-keyed table never saw.</item>
/// <item><c>EachModePermitsExactlyTheSurfaceTheTestsDeclare</c> and
/// <c>ThePolicyRowsAndTheModeTableCannotDriftApart</c> — there are no policy rows
/// left to drift from the mode table. The first is replaced below by the counts
/// that survive, which are nearly the whole surface.</item>
/// <item><c>AToolNobodyClassifiedIsRefusedInEveryMode</c> and
/// <c>AnUnclassifiedToolInTheChildsListIsRefusedOverTheWire</c> — deny-by-default
/// is gone, so the second is inverted below: a tool this build has never heard of
/// is <i>forwarded</i>, and asserting that is what proves the removal is real
/// rather than accidentally still in force.</item>
/// <item><c>AStorageToolOnAHeadlessSessionIsRefusedWithTextNamingPersistent</c> —
/// BrowserAI no longer refuses it. A headless session's own child is launched
/// without the <c>storage</c> capability, so the storage tools do not exist in
/// it and upstream answers; that is a property of
/// <c>BrowserConfiguration.ForSession</c> and <c>ConfigRoundTripTests</c> owns
/// it.</item>
/// </list>
/// <para>
/// <b>Why the matrix went.</b> It was described as the charter's security
/// trade-off made true and it was never a boundary against the caller: the
/// calling agent chooses the session directory, the profile and its cookie
/// database are created inside it, and the agent runs as the same Windows user,
/// so DPAPI decrypts for it. Refusing <c>browser_cookie_list</c> to a caller
/// holding file tools costs a lookup per call and buys one extra step.
/// <b>Measured 2026-08-18</b>, from a second process as the same user against a
/// session this product configured — <c>CryptUnprotectData</c> and AES-256-GCM,
/// no App-Bound Encryption
/// ([kb](../../kb/chromium/profiles.md#chromiums-cookie-store-and-what-it-takes-to-read-one--measured-2026-08-18)).
/// </para>
/// <para>
/// <b>What is asserted instead is what is actually true.</b> A call names its
/// session or it is refused — that is <b>routing</b>, and the concurrency arm
/// below drives it across sessions being opened and destroyed at the same time.
/// And <c>browser_annotate</c> is <b>not advertised at all</b>, because it would
/// <b>hang</b>: that is a liveness claim and is asserted as one — and
/// <b>measured on 2026-08-18</b>, three runs against a real headless child: a
/// visible window took the foreground within 1.2 s every time and the call was
/// still silent 90 s later
/// ([kb](../../kb/playwright/tools-and-artifacts.md#what-browser_annotate-actually-does--measured-2026-08-18)).
/// <b>The measurement is deliberately not an arm of this class</b>: asserting
/// that a call does not return means spending the budget waiting for it, with a
/// focus-stealing window on the developer's screen throughout.
/// </para>
/// <para>
/// ⚠️ <b>Amended 2026-08-18, later the same day (previously "<c>browser_annotate</c>
/// is refused on a windowless session … permitted on the two modes that open
/// one").</b> The same measurement said the daemon is detached, per-user and
/// writes into <c>%TEMP%</c>, and that the call is unbounded on every mode — so
/// the tool is withheld from <c>tools/list</c> in every mode and refused if a
/// caller names it anyway. Both halves are asserted below, and against a child
/// double that would happily answer the call.
/// </para>
/// </remarks>
internal sealed class SessionPolicyTests
{
    /// <summary>
    /// What each mode permits of the surface BrowserAI advertises, written down
    /// rather than computed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-18 to 58 / 58 / 58 of 58 (previously 58 / 59 / 59
    /// of 59, and 41 / 41 / 58 before that, measured 2026-08-16 against the
    /// five-class permission matrix).</b> The denominator moved because
    /// <c>browser_annotate</c> is no longer advertised: of the 59 upstream tools
    /// the union child exposes, BrowserAI's <c>tools/list</c> carries <b>58</b>.
    /// The numerators moved with it — every mode permits everything it
    /// advertises, and the tool that would hang is not in the list to permit.
    /// </para>
    /// <para>
    /// <b>Written down rather than derived, for the reason the old table was:</b>
    /// derived from the product's own decision it would agree with it by
    /// construction and could never fail. This one still can — a per-mode
    /// refusal reintroduced anywhere, a surface that changed size, or a fourth
    /// mode nobody wrote a row for.
    /// </para>
    /// </remarks>
    private static readonly (string Mode, int Allowed, string[] Refused)[] Expected =
    [
        ("headless", 58, []),
        ("interactive", 58, []),
        ("persistent", 58, []),
    ];

    [Test]
    public async Task EveryModePermitsEveryToolItAdvertisesAndTheOneThatWouldHangIsNotAdvertised()
    {
        var union = UpstreamSurface.For(BrowserConfiguration.UnionCapabilities);
        var advertised = union.Where(tool => !SessionToolPolicy.IsWithheldFromTheSurface(tool)).ToList();
        var offenders = new List<string>();

        // The denominators are stated before the numerators, and there are two
        // of them: the union child exposes 59 tools, BrowserAI advertises 58 of
        // them, and every mode permits all 58.
        await Assert.That(union.Count).IsEqualTo(59);
        await Assert.That(advertised.Count).IsEqualTo(58);

        // The named hole, individually, because a count is satisfied by the
        // wrong tool as easily as by the right one. It is in the child's surface
        // and out of ours, which is the whole of the change.
        await Assert.That(union).Contains(SessionToolPolicy.AnnotateTool);
        await Assert.That(advertised).DoesNotContain(SessionToolPolicy.AnnotateTool);

        foreach (var mode in SessionModes.All)
        {
            var rows = Expected.Where(candidate => candidate.Mode == mode.Name).ToList();

            if (rows.Count is not 1)
            {
                offenders.Add($"{mode.Name}: {rows.Count} declared surface sizes, expected exactly one");
                continue;
            }

            var (_, declared, refused) = rows[0];
            var allowed = advertised.Where(tool => SessionToolPolicy.Decide(tool).IsAllowed).ToList();

            if (allowed.Count != declared)
            {
                offenders.Add($"{mode.Name}: permits {allowed.Count} of {advertised.Count}, declared {declared}");
            }

            offenders.AddRange(refused
                .Where(tool => allowed.Contains(tool, StringComparer.Ordinal))
                .Select(tool => $"{mode.Name}: permits '{tool}', which it is declared to refuse"));
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // And the call is refused as well as unadvertised, which is the half a
        // filtered list cannot do: a model that knows the name from upstream can
        // still send it.
        //
        // ⚠️ Asserted ONCE rather than per mode. `Decide` no longer takes a
        // mode, so a loop here would ask the same question three times and read
        // as coverage it is not — the per-mode claim that survives is the one
        // above, that every mode's advertised surface is the same 58.
        await Assert.That(SessionToolPolicy.Decide(SessionToolPolicy.AnnotateTool).IsAllowed).IsFalse();

        // The tools the old matrix turned on are permitted now. Asserted rather
        // than left implied: these three are the whole of what that removal
        // changed, and a reader who learned the old behaviour needs to see it
        // stated.
        await Assert.That(Allows("browser_run_code_unsafe")).IsTrue();
        await Assert.That(Allows("browser_cookie_list")).IsTrue();
        await Assert.That(Allows("browser_get_config")).IsTrue();
    }

    [Test]
    public async Task AToolThisBuildHasNeverHeardOfIsForwardedRatherThanRefused()
    {
        // The inversion of the old deny-by-default arm, and it is the test that
        // proves the removal actually happened: a child that advertises
        // something no build of BrowserAI has ever judged has its call
        // FORWARDED, and the answer the caller gets is the child's own.
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult =
                """{"tools":[{"name":"browser_navigate","description":"Navigate to a URL","inputSchema":{"type":"object","properties":{}}},{"name":"browser_a_tool_from_the_future","description":"A tool no build of BrowserAI has ever judged","inputSchema":{"type":"object","properties":{}}}]}""",
            sessions: sessions);

        var directory = Path.Combine(sessions.Root, "a-tool-from-the-future");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "meets a tool from the future",
            ["mode"] = "headless",
        });

        // Sent rather than round-tripped, because the answer is the CHILD's own
        // JSON-RPC error and a helper that threw on one would hide the finding.
        var answer = await rig.Client.SendAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_a_tool_from_the_future",
            ["arguments"] = new JsonObject { ["session"] = directory },
        });

        // The child was asked, which is the whole claim. Before 2026-08-18 this
        // never left BrowserAI.
        await Assert.That(sessions.SessionChildren.Any(child =>
            child.ToolCallsReceived.Contains("browser_a_tool_from_the_future", StringComparer.Ordinal))).IsTrue();

        // And the answer is the double's own words rather than ours: it refuses
        // a tool no test programmed, and that sentence is what comes back.
        await Assert.That((string?)answer.Error?["message"])
            .Contains("The fake child answers only tools a test programmed");

        await Assert.That(answer.Envelope.ToJsonString()).DoesNotContain("does not classify");
    }

    [Test]
    public async Task TheAnnotationToolIsAbsentFromTheSurfaceInEveryModeAndRefusedIfNamedAnyway()
    {
        // ⚠️ Rewritten 2026-08-18 (previously
        // TheAnnotationToolIsRefusedWhereNoWindowWasPromisedAndForwardedWhereOneWas,
        // which asserted the headed arm FORWARDED the call and got the child's
        // answer back). It no longer does, and the child double below is what
        // makes that a real claim rather than a missing case: it answers the
        // tool happily, so a proxy that forwarded would visibly succeed here.
        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools[SessionToolPolicy.AnnotateTool] = new FakeToolBehaviour
            {
                RawResult = """{"content":[{"type":"text","text":"the human drew something"}]}""",
            });

        // The surface child answers with upstream's own committed list, so the
        // absence asserted below is a filter rather than a double that never had
        // the tool.
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult = UpstreamSurface.SnapshotToolsListResult(),
            sessions: sessions);

        // The surface half, off the wire: the child advertises it, BrowserAI
        // does not. Both halves matter — "absent from our list" is satisfied
        // vacuously by a child that never had it.
        var childsOwn = rig.SurfaceChild.ToolsListResult;
        var advertised = await rig.Client.RoundTripAsync("tools/list", new JsonObject());

        var names = (advertised["tools"]?.AsArray() ?? [])
            .Select(tool => (string?)tool?["name"] ?? string.Empty)
            .ToList();

        await Assert.That(childsOwn).Contains(SessionToolPolicy.AnnotateTool);
        await Assert.That(names).DoesNotContain(SessionToolPolicy.AnnotateTool);
        await Assert.That(names.Count).IsGreaterThan(10);

        // And nothing of it is left in the surface for a model to read: no
        // description mentioning it, no note saying it would refuse.
        await Assert.That(advertised.ToJsonString()).DoesNotContain(SessionToolPolicy.AnnotateTool);

        // The call half, on a session of every mode, because "in every mode" is
        // the claim and one session cannot make it.
        foreach (var mode in SessionModes.All)
        {
            var directory = Path.Combine(sessions.Root, $"annotate-{mode.Name}");

            _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
            {
                ["directory"] = directory,
                ["purpose"] = $"a '{mode.Name}' session that reaches for the annotation tool by name",
                ["mode"] = mode.Name,
            });

            var callsBefore = sessions.SessionChildren.Sum(child =>
                child.ToolCallsReceived.Count(tool => tool == SessionToolPolicy.AnnotateTool));

            var refused = await CallAsync(rig, SessionToolPolicy.AnnotateTool, new JsonObject { ["session"] = directory });
            var text = TextOf(refused);

            await Assert.That((bool?)refused["isError"]).IsTrue();

            // ⚠️ The sentence has to say LIVENESS, and it has to say the tool is
            // not in the list. A model told "not permitted" goes looking for a
            // permission to acquire; a model told the tool broke retries; a
            // model told the call cannot return acts on it in one turn.
            await Assert.That(text).Contains("NOT in this server's tools/list");
            await Assert.That(text).Contains("liveness rather than security");
            await Assert.That(text).Contains("no self-timeout");
            await Assert.That(text).Contains("browser_take_screenshot");

            // Nothing reached the child: a refusal that forwarded first and hid
            // the answer would still have hung.
            await Assert.That(sessions.SessionChildren.Sum(child =>
                child.ToolCallsReceived.Count(tool => tool == SessionToolPolicy.AnnotateTool))).IsEqualTo(callsBefore);
        }
    }

    [Test]
    public async Task ACallNamingNoSessionIsRefusedAndReachesNoChildAtAll()
    {
        // ROUTING, and it is the one thing at this layer that is not negotiable.
        // A proxy holding N children cannot answer a call that names none of
        // them: before `session` was made mandatory such a call was answered by
        // the RUN'S OWN child, which is a session nobody chose the mode or the
        // directory of, and whose profile outlives the call.
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var callsBefore = sessions.SessionChildren.Sum(child => child.ToolCallsReceived.Count);

        var answer = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["url"] = "data:text/html,<h1>ok</h1>",
        });

        await Assert.That((bool?)answer["isError"]).IsTrue();
        await Assert.That(TextOf(answer)).IsEqualTo(SessionErrors.SessionMissing("browser_navigate"));

        // Neither a session child nor the run's own surface child was asked.
        await Assert.That(sessions.SessionChildren.Sum(child => child.ToolCallsReceived.Count)).IsEqualTo(callsBefore);
        await Assert.That(rig.SurfaceChild.ToolCallsReceived).DoesNotContain("browser_navigate");

        // And it is required in the advertised schema too, so a model is told
        // before it is refused rather than after.
        var advertised = await rig.Client.RoundTripAsync("tools/list");

        var navigate = (advertised["tools"]?.AsArray() ?? [])
            .Single(tool => (string?)tool!["name"] == "browser_navigate")!
            .AsObject();

        await Assert.That(navigate["inputSchema"]?["required"]?.AsArray()
            .Select(entry => (string?)entry)
            .Contains(SessionToolSurface.SessionParameter)).IsTrue();
    }

    [Test]
    public async Task EveryCallReachesTheChildOfTheSessionItNamedUnderConcurrency()
    {
        // ⚠️ The failure this exists for is not a glitch: a call that resolved
        // one session's handle to another's child drives the WRONG BROWSER, and
        // it presents as nothing at all — a successful call and a plausible
        // result, against a page the caller never asked for. So this drives real
        // calls across sessions of different modes, all outstanding at the server
        // together, WHILE other sessions are being opened and destroyed on the
        // same connection, and checks which child each one landed in.
        //
        // Reframed 2026-08-18 (previously TheHandleToTypeLookupHoldsUnderConcurrencyAcrossModes,
        // which asserted that each answer matched the (tool, mode) verdict for
        // the handle that call named). With the permission matrix gone, a
        // verdict-based check would have been satisfied by "allowed" everywhere
        // and could no longer see a swapped lookup at all. What replaces it is
        // stronger rather than weaker: the per-child call log says which session
        // actually received the work.
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

        // One probe per session per round, and the tool name carries which
        // session it was meant for -- so a call that landed in a neighbour's
        // child is visible in that child's own log rather than inferred.
        const int Rounds = 25;

        var requests = new List<(string Method, JsonNode? Parameters)>();
        var expected = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var round in Enumerable.Range(0, Rounds))
        {
            foreach (var mode in SessionModes.All)
            {
                foreach (var tool in Probes)
                {
                    // The annotation probe is refused before it is routed, on
                    // every mode, so it is counted out of the expectation rather
                    // than out of the batch: the call still goes over the wire,
                    // and a proxy that forwarded it anyway would show up as a
                    // surplus in that child's log.
                    if (SessionToolPolicy.Decide(tool).IsAllowed)
                    {
                        expected[directories[mode.Name]] = expected.GetValueOrDefault(directories[mode.Name]) + 1;
                    }

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
            // the index the lookup reads is being written while the calls above
            // are being routed, which is the only state that could produce the
            // race.
            var churn = Path.Combine(sessions.Root, $"churn-{round}");

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

            requests.Add(("tools/call", new JsonObject
            {
                ["name"] = SessionToolSurface.Destroy,
                ["arguments"] = new JsonObject { ["directory"] = churn },
            }));
        }

        var answers = await rig.Client.RoundTripManyAsync(requests);

        await Assert.That(answers.Count).IsEqualTo(requests.Count);

        // Each double is paired with the session directory the product built its
        // launch options for, so the mapping is the product's rather than a
        // guess about creation order.
        var children = sessions.SessionChildren;
        var launches = sessions.Launches;
        var wrong = new ConcurrentBag<string>();

        await Assert.That(children.Count).IsEqualTo(launches.Count);

        for (var index = 0; index < children.Count; index++)
        {
            // The child's working directory is the session's `output` folder,
            // which is how a bare relative filename lands inside the session.
            var session = Path.GetDirectoryName(launches[index].WorkingDirectory)!;
            var received = children[index].ToolCallsReceived.Where(Probes.Contains).ToList();

            if (received.Count != expected.GetValueOrDefault(session))
            {
                wrong.Add($"the child for '{session}' received {received.Count} probe calls, expected {expected.GetValueOrDefault(session)}");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, wrong.Take(10))).IsEmpty();

        // The denominator, so a proxy that dropped calls could not pass by
        // making every count zero: 25 rounds × 3 sessions × 4 probes = 300
        // calls, minus the annotation probe, which every session refuses.
        //
        // Corrected 2026-08-18 (previously 25, counted as the rounds times the
        // modes that refused it — one of three). The tool is withheld from the
        // surface and refused everywhere now, so it is 75, one per round per
        // session.
        var refusedByLiveness = SessionToolPolicy.Decide(SessionToolPolicy.AnnotateTool).IsAllowed
            ? 0
            : Rounds * SessionModes.All.Count;

        await Assert.That(expected.Values.Sum())
            .IsEqualTo((Rounds * SessionModes.All.Count * Probes.Length) - refusedByLiveness);

        await Assert.That(refusedByLiveness).IsEqualTo(75);

        // The batch really was concurrent rather than a queue the client drained
        // one at a time: answers came back in a different order from the
        // requests. Asserted rather than noted, because a serialising server
        // would make every claim above evidence about one call at a time.
        var firstId = answers.Min(answer => answer.Id);

        var outOfOrder = answers
            .Select((answer, position) => answer.Id - firstId != position)
            .Count(moved => moved);

        await Assert.That(outOfOrder).IsGreaterThan(0);
    }

    /// <summary>
    /// Four tools chosen so the probe set spans what the removal changed: the
    /// back door, a storage tool, the annotation tool and an ordinary one.
    /// </summary>
    private static readonly string[] Probes =
        ["browser_storage_state", "browser_run_code_unsafe", SessionToolPolicy.AnnotateTool, "browser_navigate"];

    private static bool Allows(string tool) => SessionToolPolicy.Decide(tool).IsAllowed;

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
