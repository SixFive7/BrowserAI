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
/// And <c>browser_annotate</c> is refused on a windowless session because it
/// would <b>hang</b>, which is a liveness claim and is asserted as one — and
/// <b>measured on 2026-08-18</b>, three runs against a real headless child: a
/// visible window took the foreground within 1.2 s every time and the call was
/// still silent 90 s later
/// ([kb](../../kb/playwright/tools-and-artifacts.md#what-browser_annotate-actually-does--measured-2026-08-18)).
/// <b>The measurement is deliberately not an arm of this class</b>: asserting
/// that a call does not return means spending the budget waiting for it, with a
/// focus-stealing window on the developer's screen throughout.
/// </para>
/// </remarks>
internal sealed class SessionPolicyTests
{
    /// <summary>
    /// What each mode permits of the 59-tool union surface, written down rather
    /// than computed.
    /// </summary>
    /// <remarks>
    /// <b>Corrected 2026-08-18 (previously 41 / 41 / 58, measured 2026-08-16
    /// against the five-class permission matrix).</b> The matrix is gone, so the
    /// only tool any mode refuses is <c>browser_annotate</c>, and only where no
    /// window was promised. Written down rather than derived for the reason the
    /// old table was: derived from the product's own decision it would agree with
    /// it by construction and could never fail.
    /// </remarks>
    private static readonly (string Mode, int Allowed, string[] Refused)[] Expected =
    [
        ("headless", 58, [SessionToolPolicy.AnnotateTool]),
        ("interactive", 59, []),
        ("persistent", 59, []),
    ];

    [Test]
    public async Task EveryModePermitsEveryToolItAdvertisesExceptTheOneThatWouldHang()
    {
        var union = UpstreamSurface.For(BrowserConfiguration.UnionCapabilities);
        var offenders = new List<string>();

        // The denominator is stated before the numerators: of the 59 tools
        // BrowserAI advertises, headless permits 58 and the two headed modes
        // permit all 59.
        await Assert.That(union.Count).IsEqualTo(59);

        foreach (var mode in SessionModes.All)
        {
            var rows = Expected.Where(candidate => candidate.Mode == mode.Name).ToList();

            if (rows.Count is not 1)
            {
                offenders.Add($"{mode.Name}: {rows.Count} declared surface sizes, expected exactly one");
                continue;
            }

            var (_, declared, refused) = rows[0];
            var allowed = union.Where(tool => SessionToolPolicy.Decide(tool, mode).IsAllowed).ToList();

            if (allowed.Count != declared)
            {
                offenders.Add($"{mode.Name}: permits {allowed.Count} of {union.Count}, declared {declared}");
            }

            offenders.AddRange(refused
                .Where(tool => allowed.Contains(tool, StringComparer.Ordinal))
                .Select(tool => $"{mode.Name}: permits '{tool}', which it is declared to refuse"));
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // The named hole, individually, because a count is satisfied by the
        // wrong tool as easily as by the right one -- and because the refusal
        // keys on the table's own `Headed` column rather than on a mode name.
        await Assert.That(Allows("headless", SessionToolPolicy.AnnotateTool)).IsFalse();
        await Assert.That(Allows("interactive", SessionToolPolicy.AnnotateTool)).IsTrue();
        await Assert.That(Allows("persistent", SessionToolPolicy.AnnotateTool)).IsTrue();

        // And the tools the old matrix turned on are permitted everywhere now.
        // Asserted rather than left implied: these three are the whole of what
        // the removal changed, and a reader who learned the old behaviour needs
        // to see it stated.
        foreach (var mode in SessionModes.All)
        {
            await Assert.That(Allows(mode.Name, "browser_run_code_unsafe")).IsTrue();
            await Assert.That(Allows(mode.Name, "browser_cookie_list")).IsTrue();
            await Assert.That(Allows(mode.Name, "browser_get_config")).IsTrue();
        }
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
    public async Task TheAnnotationToolIsRefusedWhereNoWindowWasPromisedAndForwardedWhereOneWas()
    {
        // Both arms against one rig, because separately either is vacuous: "it
        // refuses" passes against a proxy that refuses everything, and "it
        // forwards" passes against one that forwards everything.
        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools[SessionToolPolicy.AnnotateTool] = new FakeToolBehaviour
            {
                RawResult = """{"content":[{"type":"text","text":"the human drew something"}]}""",
            });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var windowless = Path.Combine(sessions.Root, "windowless");
        var headed = Path.Combine(sessions.Root, "headed");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = windowless,
            ["purpose"] = "the unattended session the annotation tool would hang",
            ["mode"] = "headless",
        });

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = headed,
            ["purpose"] = "the session with a window, where a human may be at the keyboard",
            ["mode"] = "interactive",
        });

        var callsBefore = sessions.SessionChildren.Sum(child =>
            child.ToolCallsReceived.Count(tool => tool == SessionToolPolicy.AnnotateTool));

        var refused = await CallAsync(rig, SessionToolPolicy.AnnotateTool, new JsonObject { ["session"] = windowless });
        var text = TextOf(refused);

        await Assert.That((bool?)refused["isError"]).IsTrue();

        // ⚠️ The sentence has to say LIVENESS. A model told "not permitted" goes
        // looking for a permission to acquire; a model told the call would hang
        // can act on it in one turn, which is the catalogue's own rule.
        await Assert.That(text).Contains("block until this run is killed");
        await Assert.That(text).Contains("liveness refusal and not a security one");
        await Assert.That(text).Contains("'interactive'");
        await Assert.That(text).Contains(SessionToolSurface.Init);

        // Nothing reached the child: a refusal that forwarded first and hid the
        // answer would still have hung.
        await Assert.That(sessions.SessionChildren.Sum(child =>
            child.ToolCallsReceived.Count(tool => tool == SessionToolPolicy.AnnotateTool))).IsEqualTo(callsBefore);

        var forwarded = await CallAsync(rig, SessionToolPolicy.AnnotateTool, new JsonObject { ["session"] = headed });

        await Assert.That((bool?)forwarded["isError"]).IsNotEqualTo(true);
        await Assert.That(TextOf(forwarded)).IsEqualTo("the human drew something");
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
                    // The annotation probe on the windowless session is refused
                    // before it is routed, so it is counted out of the
                    // expectation rather than out of the batch: the call still
                    // goes over the wire, and a proxy that forwarded it anyway
                    // would show up as a surplus in that child's log.
                    if (SessionToolPolicy.Decide(tool, mode).IsAllowed)
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
        // calls, minus the 25 annotation probes the windowless session refuses.
        var refusedByLiveness = Rounds * SessionModes.All.Count(mode =>
            !SessionToolPolicy.Decide(SessionToolPolicy.AnnotateTool, mode).IsAllowed);

        await Assert.That(expected.Values.Sum())
            .IsEqualTo((Rounds * SessionModes.All.Count * Probes.Length) - refusedByLiveness);

        await Assert.That(refusedByLiveness).IsEqualTo(25);

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
