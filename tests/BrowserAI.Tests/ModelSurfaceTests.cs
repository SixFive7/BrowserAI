// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Proxy;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// Everything a model reads before it acts, and the checks that run in both
/// directions over it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure this file exists to catch has already happened once in this
/// project's history:</b> a fourth thing is added, three copies are updated, and
/// the fourth silently describes a system that no longer exists. Nothing breaks;
/// a model is simply told something that is not true, and the symptom arrives
/// somewhere else entirely.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-20 (previously "One table, six consumers", and the
/// paragraph here described planting a fourth row in <c>SessionModes.All</c> to
/// turn <c>EveryConsumerRendersEveryModeInTheTable</c> red in both
/// directions).</b> Session modes are gone and five of those six consumers went
/// with them. The bidirectional shape survives in
/// <see cref="EverySessionGetsEveryCapabilityAndTheNewlyGrantedTenAreInTheSurface"/>,
/// which holds the product's own list of newly-granted tools against a
/// hand-written one and fails whichever side moves.
/// </para>
/// </remarks>
internal sealed class ModelSurfaceTests
{
    /// <summary>
    /// Every authored tool's argument set and its required subset, written
    /// here rather than read from the class under test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b><c>browserai_catch_up</c> arrived 2026-08-20 and it is the one
    /// session-scoped tool with no <c>why</c>.</b> That is deliberate twice
    /// over: a tool whose whole purpose is to tell you what happened must not
    /// itself become the most recent thing that happened, and writing an entry
    /// would mean taking the per-directory gate, which a session another live
    /// BrowserAI is driving would refuse — which is the case it exists for. The
    /// row is here so that a future <c>why</c> added to it is a red build rather
    /// than a silent widening.
    /// </para>
    /// <para>
    /// ⚠️ <b>Changed 2026-08-20: <c>mode</c> is gone from <c>init</c> and
    /// <c>headed</c> is on both.</b> Session modes were deleted, every
    /// capability is granted to every session, and headedness became a per-run
    /// argument — so <c>init</c> no longer has a third required property and
    /// <c>resume</c> takes one more optional one.
    /// </para>
    /// <para>
    /// <b>Three of these differ from §H.2's table, and the difference is
    /// deliberate rather than drift.</b> <c>browserai_resume</c> ships
    /// <c>tracing</c> and <c>consoleLevel</c>, which §H.2 gives only to
    /// <c>init</c>. The rule §H.2 states for refusing an argument on
    /// <c>resume</c> is that <i>"a profile is browser-specific"</i> — it is
    /// about <c>browser</c>, and about facts the directory on disk already
    /// records. Neither of these is such a fact: <c>tracing</c> becomes
    /// <c>saveSession</c> and <c>consoleLevel</c> becomes <c>console.level</c>
    /// in the config generated for <i>this run</i>, both of which may honestly
    /// differ from run to run without contradicting anything the session
    /// recorded. Refusing them would have meant destroying and recreating a
    /// session to turn tracing on for one afternoon.
    /// </para>
    /// <para>
    /// Recorded here because §H.2 is a plan section and this is what outlives
    /// it. The reason a signature table lives in the suite at all is that the
    /// arguments are the half of the model-facing surface nothing measured: the
    /// descriptions were budgeted in bytes and the mode table was rendered into
    /// four consumers, while the property names were whatever the class
    /// declared on the day.
    /// </para>
    /// </remarks>
    private static readonly (string Tool, string[] Properties, string[] Required)[] TheAuthoredSignatures =
    [
        (SessionToolSurface.Init,
            ["directory", "purpose", "headed", "browser", "tracing", "captureNetwork", "viewport", "locale", "timezone", "ignoreHTTPSErrors", "debug"],
            ["directory", "purpose"]),
        (SessionToolSurface.Resume,
            ["directory", "purpose", "why", "headed", "debug", "tracing", "captureNetwork", "viewport", "locale", "timezone", "ignoreHTTPSErrors"],
            ["directory", "why"]),
        (SessionToolSurface.CatchUp, ["session"], ["session"]),
        (SessionToolSurface.List, ["directory"], ["directory"]),
        (SessionToolSurface.Destroy, ["directory", "why"], ["directory", "why"]),
        (SessionToolSurface.SetPurpose, ["session", "purpose", "why"], ["session", "purpose", "why"]),
        (SessionToolSurface.ReinstallBrowser, ["browser"], ["browser"]),
    ];

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

        // ⚠️ DELETED 2026-08-18: ("browser_annotate", "wait for the user to draw
        // annotations") — "blocks on a human. A model that does not know this
        // schedules it and waits forever." The tool is withheld from the surface
        // now, so there is no description of it for a phrase to survive in, and
        // the fact the phrase protected is answered by removal rather than by
        // warning. Keeping the row would have failed the test below on its
        // "not in the advertised surface at all" arm, which is the arm that
        // exists to stop exactly this becoming a silent skip.

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
    /// What <c>browserai_init</c>'s description must say, beyond what its
    /// arguments mean.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two requirements, from two documents, and neither was met until
    /// 2026-08-17.</b> The guidance design puts *"the real-Chrome-profile
    /// warning, and the retention policy"* in the creation tool's description —
    /// the spec requires retention to be stated <i>there</i>, and <c>init</c>'s
    /// description is the channel a model sees at the moment it reaches for the
    /// tool; the
    /// [DECISIONS](../../DECISIONS.md#shape-and-packaging) demands the same thing independently, because
    /// <c>init</c> accepts any path — *"the `init` tool description is a security
    /// surface … say plainly what pointing at an existing browser profile
    /// does"*. Retention was stated on <c>resume</c> and on <c>list</c>, which is
    /// everywhere except where it was required.
    /// </para>
    /// <para>
    /// <b>Phrases rather than a whole-string comparison</b>, for the same reason
    /// <see cref="LoadBearingUpstreamPhrases"/> is: the text will be reworded,
    /// and what must survive a rewording is the fact, not the sentence.
    /// </para>
    /// </remarks>
    private static readonly string[] RequiredInitPhrases =
    [
        // The security surface. A model that reads this and still points a
        // session at a real profile has been told; one that is not told has not.
        "real Chrome profile",

        // ⚠️ Corrected 2026-08-19 (previously "Any path is accepted and none
        // is validated"). That sentence stopped being true on 2026-08-19: a
        // network path and a second spelling of one directory are both refused
        // now. The FACT it stood for is untouched and is what is required here --
        // nothing about what the directory CONTAINS is looked at, so pointing a
        // session at a real profile still works and still does what the rest of
        // this list warns about. This is the rewording the remark above
        // anticipated, and the phrase moved WITH the fact rather than the fact
        // being trimmed to keep the phrase.
        "nothing else about it is validated",
        "live cookies and logins",

        // The retention policy, stated where the session is created rather than
        // only where one is resumed or listed. The tool name is part of the
        // requirement: a retention policy with no way to act on it is a fact
        // rather than guidance.
        "nothing here expires",
        "never deletes a session directory",
        SessionToolSurface.Destroy,
    ];

    /// <summary>
    /// The ten tools that became reachable on 2026-08-20, written down here as
    /// well as in the product, because the two lists are the claim.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Written down rather than read off
    /// <see cref="SessionToolSurface.NewlyGrantedTools"/>.</b> Derived from the
    /// product's own list this would agree with it by construction and could
    /// never fail — the same reason the mode table this replaced kept a
    /// hand-written expectation row. What it can catch: a capability quietly
    /// dropped from <see cref="BrowserConfiguration.GrantedCapabilities"/>, an
    /// upstream rename, and a product list edited to match a surface rather than
    /// the other way round.
    /// </para>
    /// <para>
    /// <b>None of these has ever been reachable</b> — not in BrowserAI and not
    /// in the predecessor product it was written against. Four are
    /// <c>network</c>, one is <c>pdf</c>, five are <c>testing</c>, and BrowserAI
    /// never named any of those three capabilities until session modes were
    /// deleted.
    /// </para>
    /// </remarks>
    private static readonly string[] TheNewlyGrantedTen =
    [
        "browser_route",
        "browser_route_list",
        "browser_unroute",
        "browser_network_state_set",
        "browser_pdf_save",
        "browser_generate_locator",
        "browser_verify_element_visible",
        "browser_verify_text_visible",
        "browser_verify_list_visible",
        "browser_verify_value",
    ];

    /// <summary>
    /// Every capability is granted to every session, and the ten tools that
    /// arrived with the last three of them are in the surface a model reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Replaces <c>EveryConsumerRendersEveryModeInTheTable</c>,
    /// 2026-08-20.</b> That test asserted that six consumers each rendered every
    /// row of <c>SessionModes.All</c> — the server instructions, <c>init</c>'s
    /// description, <c>resume</c>'s result, the refusal a bad <c>mode</c>
    /// produced, the generated child config, and the suite's own expectation
    /// table. Five of the six no longer exist, and the sixth renders nothing
    /// that varies. It was <b>replaced rather than deleted</b>: the failure it
    /// existed to catch — a capability decided in one place and rendered in
    /// another, drifting silently — is exactly the failure a grant of ten
    /// previously-unreachable tools can reintroduce.
    /// </para>
    /// <para>
    /// <b>This is the record that the grant was deliberate.</b> Ten tools became
    /// callable for the first time in this product's history as a side effect of
    /// deleting something else, and a side effect nothing asserts is
    /// indistinguishable from an accident at the next review.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EverySessionGetsEveryCapabilityAndTheNewlyGrantedTenAreInTheSurface()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult = UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.SurfaceChild.ToolsListResult);
        var missing = new List<string>();

        // 1. The two lists agree, in both directions. The product's own list is
        //    what a reader is sent to; this one is what fails when it drifts.
        missing.AddRange(TheNewlyGrantedTen
            .Where(tool => !SessionToolSurface.NewlyGrantedTools.Contains(tool, StringComparer.Ordinal))
            .Select(tool => $"'{tool}' is expected to be a newly-granted tool and the product does not list it"));

        missing.AddRange(SessionToolSurface.NewlyGrantedTools
            .Where(tool => !TheNewlyGrantedTen.Contains(tool, StringComparer.Ordinal))
            .Select(tool => $"the product lists '{tool}' as newly granted and this test does not expect it"));

        // 2. Each of the ten, by name, in the surface a model receives. A count
        //    is satisfied by the wrong tool as easily as by the right one.
        missing.AddRange(TheNewlyGrantedTen
            .Where(tool => !advertised.ContainsKey(tool))
            .Select(tool => $"'{tool}' is not in the advertised surface"));

        // 3. And in the child a session is actually launched with, which is the
        //    half that decides whether the call works. A tool in `tools/list`
        //    whose capability the session's own config never names reaches
        //    upstream and is answered with "unknown tool".
        var reachable = UpstreamSurface.For(BrowserConfiguration.GrantedCapabilities);

        missing.AddRange(TheNewlyGrantedTen
            .Where(tool => !reachable.Contains(tool, StringComparer.Ordinal))
            .Select(tool => $"'{tool}' does not exist in a child launched with the granted capabilities"));

        // 4. THE GRANT ITSELF: every capability upstream declares that carries a
        //    tool is either unconditional or named in the generated config.
        //    Nothing upstream offers is left out, which is what "the full union"
        //    means and what a per-mode subset used to break.
        foreach (var capability in UpstreamSurface.CapabilitiesCarryingTools())
        {
            if (!UpstreamSurface.UnconditionalCapabilities().Contains(capability, StringComparer.Ordinal)
                && !BrowserConfiguration.GrantedCapabilities.Contains(capability, StringComparer.Ordinal))
            {
                missing.Add($"upstream's '{capability}' capability carries tools and no session is granted it");
            }
        }

        // 5. `browser_run_code_unsafe` is NOT one of the ten and never was. It
        //    is `core`, so it has been reachable in every session this product
        //    has ever opened — a reader meeting the grant must not come away
        //    thinking it arrived with it.
        if (SessionToolSurface.NewlyGrantedTools.Contains("browser_run_code_unsafe", StringComparer.Ordinal))
        {
            missing.Add("browser_run_code_unsafe is listed as newly granted; it is core and always was");
        }

        if (!UpstreamSurface.DefaultSurface().Contains("browser_run_code_unsafe", StringComparer.Ordinal))
        {
            missing.Add("browser_run_code_unsafe is not in upstream's default surface, so the claim that it is core is stale");
        }

        // 6. The response-mocking warning is in the server instructions, which
        //    are BrowserAI's own string, and NOT appended to browser_route's
        //    description, which passes through byte for byte.
        if (!ServerInstructions.Text.Contains("browser_route", StringComparison.Ordinal))
        {
            missing.Add("the server instructions do not name browser_route");
        }

        if (!ServerInstructions.Text.Contains("mocked response", StringComparison.Ordinal))
        {
            missing.Add("the server instructions do not warn that a mocked response renders as if it came from the server");
        }

        var upstreamRoute = UpstreamSurface.SnapshotDescriptions().Single(tool => tool.Name == "browser_route").Description;

        if ((string?)advertised["browser_route"]?["description"] != upstreamRoute)
        {
            missing.Add("browser_route's description is not upstream's own bytes");
        }

        // 7. Headedness changes the window and nothing else. A generated config
        //    for a headed session and one for a headless session carry the same
        //    capability list, which is what "no session-scoped capability
        //    decision survives" means at the one place it used to be taken.
        var headless = CapabilitiesOf(headed: false);
        var headed = CapabilitiesOf(headed: true);

        if (headless != headed)
        {
            missing.Add($"a headed session's capabilities ({headed}) differ from a headless one's ({headless})");
        }

        await Assert.That(string.Join(Environment.NewLine, missing)).IsEmpty();

        // Not vacuous: the surface really is bigger than it was, and by exactly
        // the ten. 58 was the advertised count on 2026-08-19.
        await Assert.That(advertised.Count(entry => !SessionToolSurface.IsAuthored(entry.Key)))
            .IsEqualTo(58 + TheNewlyGrantedTen.Length);
    }

    /// <summary>The generated config's capability list, as JSON, for one headedness.</summary>
    /// <param name="headed">Whether the session opens a window.</param>
    /// <returns>The <c>capabilities</c> opinion, serialised.</returns>
    private static string CapabilitiesOf(bool headed) =>
        BrowserConfiguration.ForSession(
            SessionPath.Resolve(Path.Combine(ScratchRoot.Path, $"capabilities-{(headed ? "headed" : "headless")}")),
            headed,
            SessionManager.DefaultBrowser,
            tracing: false,
            RunOptions.Default)
        .Opinions.Single(opinion => opinion.Path == "capabilities").Value.ToJsonString();

    [Test]
    public async Task TheInstructionsStringFitsTheClientsSilentTruncationBudget()
    {
        // In CHARACTERS, because that is what the client counts. Corrected
        // 2026-08-18 (previously "In BYTES rather than characters, and that is
        // not pedantry: this string carries '·' (2 bytes) and '—' (3 bytes), so a
        // character count under-reports precisely the string that uses them").
        // The conservatism was real and the fact was wrong: measured @ Claude
        // Code 2.1.234, the cut is at 2,048 UTF-16 characters and a byte count is
        // never consulted. The client cuts with nothing reported, so anything
        // past the cut has never been read by anybody.
        await Assert.That(ServerInstructions.CharacterCount).IsLessThanOrEqualTo(ServerInstructions.MaximumCharacters);

        // Non-empty and actually wired: an instructions string the server never
        // sends is the same as not having one.
        await Assert.That(ServerInstructions.CharacterCount).IsGreaterThan(400);

        // The byte count is still computed, and is still the larger of the two.
        // It is reported rather than gated, so that the figure a wire capture
        // shows is not a figure nothing in this repository names.
        await Assert.That(ServerInstructions.ByteCount).IsGreaterThanOrEqualTo(ServerInstructions.CharacterCount);

        // ⚠️ Re-measured 2026-08-18 off the published binary's own `initialize`
        // response: **1,261 characters and 1,276 bytes**, leaving 772. The three
        // mode lines cost 106, 121 and 92 bytes apiece.
        //
        // Corrected 2026-08-18 (previously "Measured 2026-08-16: 1,613
        // characters and **1,628 bytes** … The headroom is 420 bytes … Planting
        // a fourth mode measured its cost at 223 bytes, leaving 197"). The mode
        // lines used to carry what each mode REFUSES, rendered from the
        // (tool, mode) permission policy, and that policy was removed -- it was
        // never a boundary against the caller, who chooses the session directory
        // and reads the profile inside it as the same Windows user. The string
        // lost 352 bytes with it. The 223-byte figure is NOT carried forward: it
        // was measured against a line shape that no longer exists, and an
        // adjusted number is indistinguishable from a measured one.
        //
        // The headroom is still deliberately NOT a gate. A gate on it would fail
        // a fourth mode on a budget line instead of on the six-consumer line the
        // plant was aimed at, which is the wrong test failing; the hard cap above
        // is the requirement and it already catches running out.

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

            if (description.Length > SessionToolSurface.DescriptionMaximumCharacters)
            {
                oversized.Add(
                    $"{name}: {description.Length} characters ({Encoding.UTF8.GetByteCount(description)} bytes), "
                    + $"over the {SessionToolSurface.DescriptionMaximumCharacters} the client silently truncates at");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, oversized)).IsEmpty();

        // The whole surface, not a sample: the authored tools plus every tool
        // upstream can ever expose. A count that had quietly shrunk would make
        // the loop above pass by measuring less.
        //
        // ⚠️ Corrected 2026-08-18 (previously the literal `+ 69`). The same
        // number is published in DECISIONS.md and asserted in
        // RecordedCountTests, and three copies of an upstream count is three
        // things to remember on the day upstream adds a tool. It is now read from
        // the snapshot the build regenerates from the resolved payload, which is
        // where it comes from in the first place.
        //
        // ⚠️ Corrected again, later the same day: minus whatever this build
        // withholds, which is one tool. Through the product's own predicate
        // rather than `- 1`, so the day the decision is reversed this follows it.
        var advertisedUpstream = UpstreamSurface.SnapshotDescriptions()
            .Count(entry => !SessionToolPolicy.IsWithheldFromTheSurface(entry.Name));

        await Assert.That(advertisedUpstream).IsEqualTo(UpstreamSurface.SnapshotToolCount() - 1);
        await Assert.That(advertised.Count).IsEqualTo(SessionToolSurface.Names.Count + advertisedUpstream);
    }

    [Test]
    public async Task TheCreationToolsDescriptionCarriesTheProfileWarningAndTheRetentionPolicy()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult = UpstreamSurface.SnapshotToolsListResult());

        var description = (string?)Advertised(rig.SurfaceChild.ToolsListResult)[SessionToolSurface.Init]?["description"] ?? string.Empty;
        var missing = new List<string>();

        foreach (var required in RequiredInitPhrases)
        {
            if (!description.Contains(required, StringComparison.Ordinal))
            {
                missing.Add($"browserai_init's description no longer says '{required}'");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, missing)).IsEmpty();

        // ⚠️ Re-measured 2026-08-18 off the published binary's own tools/list:
        // **1,623 characters of 2,048, leaving 425** (1,639 bytes, which is not
        // the figure the client counts -- see ClientTruncationBudget).
        //
        // Corrected 2026-08-18 (previously "Measured 2026-08-17, and the first
        // draft DID NOT FIT … the description now stands at 1,991 of 2,048 …
        // **57 bytes of headroom is the finding, and it is not a comfortable
        // number**"). It is comfortable now, and nothing was cut to make it so:
        // this description used to render SessionModes.Table, whose clauses each
        // carried a second half naming what the mode REFUSES. That half came
        // from the (tool, mode) permission policy, which was removed, and the
        // description lost 352 bytes with it. The mode table itself went on
        // 2026-08-20, which freed the rest of it.
        //
        // The finding the old note carried still stands and is why the assertion
        // below exists: both required sentences are at the END of the string, so
        // an overflow deletes exactly the two things the charter demanded be
        // present. Together with EveryToolDescriptionFitsTheSameBudget that is a
        // red build rather than a warning nobody reads.
        await Assert.That(description.Length).IsLessThanOrEqualTo(SessionToolSurface.DescriptionMaximumCharacters);
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

    /// <summary>
    /// Every model-facing string the published binary actually emits, measured
    /// off the wire and gated at 100% of the client's silent truncation budget.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gap this closes is parameter descriptions.</b> The two tests above
    /// cover the <c>instructions</c> string and every tool <c>description</c>;
    /// <c>inputSchema.properties[*].description</c> was asserted by nothing at
    /// all — and it is the surface BrowserAI is most exposed on, because
    /// <c>SessionToolSurface</c> injects one shared <c>session</c> description
    /// into every upstream tool, so a single edit lands on it fifty-nine times.
    /// </para>
    /// <para>
    /// <b>From the wire, not from source, and that distinction is the whole
    /// design.</b> These strings are assembled from concatenated constants,
    /// interpolated tables and a schema rewrite performed on the child's own
    /// nodes, so a scan of string literals in <c>.cs</c> files misses precisely
    /// the cases that break. This reads <see cref="SliceRun"/>'s capture: the
    /// published NativeAOT binary, a real <c>@playwright/mcp</c> child, real
    /// JSON-RPC over real pipes, <c>initialize</c> → <c>notifications/initialized</c>
    /// → <c>tools/list</c>. (⚠️ A client that feeds the server from a redirected
    /// <i>file</i> gets instant EOF on stdin and the server exits before
    /// answering — <see cref="RawStdioClient"/> holds the pipe open and flushes
    /// per frame, which is why it is the client here.)
    /// </para>
    /// <para>
    /// <b>Enumerated dynamically.</b> Nothing here names a tool or a parameter,
    /// so a tool upstream adds next year is covered without anybody editing this
    /// file. The floors below are what keep that from becoming vacuous.
    /// </para>
    /// <para>
    /// <b>Hard failure at 100%, and no warning tier — deliberately.</b> The
    /// recorded argument against a headroom gate stands and is not contradicted
    /// here: that argument was against failing <i>below</i> 100%, because a
    /// fourth session mode should fail on the six-consumer line rather than on a
    /// budget line. This fails only at the point where the client starts
    /// discarding text, which is a broken state rather than a tight one.
    /// </para>
    /// <para>
    /// <b>The per-string reading is MEASURED — see
    /// <see cref="ClientTruncationBudget"/>.</b> <i>Corrected 2026-08-18
    /// (previously "⚠️ The per-string reading is an ASSUMPTION … the experiment
    /// that settles the reading needs the data, and a test must not pretend to
    /// have settled it").</i> The experiment ran on 2026-08-18 against Claude
    /// Code 2.1.234, reading the <c>tools</c> array the client sends to the
    /// Messages API: the cap is per string, it is <b>2,048 UTF-16 characters</b>
    /// rather than bytes, and there is no per-tool and no whole-surface total.
    /// <c>browserai_init</c>'s whole entry — 3,360 bytes as the client sends it —
    /// arrives intact, so it was never the casualty the old note feared.
    /// </para>
    /// <para>
    /// <b>The entry totals are still reported and still not asserted</b>, for a
    /// different reason than before: they are the figure that would matter if a
    /// client release ever did introduce a per-tool bucket, and a report nobody
    /// has to re-derive is what makes that re-check cheap. What <i>is</i> asserted
    /// is the measured predicate — <c>Length &gt; 2048</c>, in characters, on
    /// each string separately.
    /// </para>
    /// </remarks>
    [Test]
    public async Task EveryModelFacingStringFitsTheClientsSilentTruncationBudget()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SliceRun.SharedAsync();
        var measured = new List<ModelFacingString>();
        var entryTotals = new List<(string Tool, int Characters, int Bytes)>();

        measured.Add(ModelFacingString.Of(
            "instructions",
            "initialize.instructions",
            (string?)run.InitializeResult["instructions"]));

        foreach (var tool in run.ToolList)
        {
            if (tool?.AsObject() is not { } definition || (string?)definition["name"] is not { } name)
            {
                continue;
            }

            measured.Add(ModelFacingString.Of("tool", name, (string?)definition["description"]));

            foreach (var (parameter, schema) in definition["inputSchema"]?["properties"]?.AsObject() ?? [])
            {
                measured.Add(ModelFacingString.Of("parameter", $"{name}.{parameter}", (string?)schema?["description"]));
            }

            // Reported, never asserted. See the ⚠️ paragraph in the remarks.
            //
            // ⚠️ Serialised through Unminified rather than ToJsonString(), and it
            // is not a nicety: the default encoder is JavaScriptEncoder.Default,
            // which escapes every non-ASCII character to \uXXXX and would report
            // `browserai_init` at 3,614 bytes for a 3,428-byte entry. That is a
            // 5% over-count on the one figure the per-tool-total experiment turns
            // on, and it over-counts most on exactly the strings that use em
            // dashes. Verified 2026-08-18 against the raw `tools/list` frame,
            // sliced by brace depth: 3,428 B / 3,404 c, and the whole frame
            // 56,946 B for 65 tools.
            var entry = definition.ToJsonString(Unminified);
            entryTotals.Add((name, entry.Length, Encoding.UTF8.GetByteCount(entry)));
        }

        Report(measured, entryTotals);

        // Both counts measured, gated on CHARACTERS. Corrected 2026-08-18
        // (previously "Both counts, failing on whichever is larger. It is not
        // documented whether the client counts characters or bytes"). It is now
        // measured: the client counts UTF-16 characters and cuts at > 2048. The
        // two diverge on the first em dash -- `initialize.instructions` is 1,261
        // characters and 1,276 bytes -- and the byte figure is the one that is
        // never consulted, so it is printed and not gated.
        var oversized = measured
            .Where(entry => entry.Gated > BudgetFor(entry.Surface))
            .OrderByDescending(entry => entry.Gated)
            .Select(entry =>
                $"{entry.Surface} '{entry.Name}' is {entry.Characters} characters / {entry.Bytes} bytes, "
                + $"{entry.Gated - BudgetFor(entry.Surface)} over the {BudgetFor(entry.Surface)} the client silently truncates at. "
                + "Everything past the cut is replaced by an ellipsis and '[truncated]' before the model sees it, so it exists in source, reads correctly in review, and never arrives.");

        await Assert.That(string.Join(Environment.NewLine, oversized)).IsEmpty();

        // Not vacuous, in each surface separately. A rewrite that stopped
        // injecting `session`, or a capture that returned an empty tool array,
        // would leave every assertion above green over nothing -- which is the
        // standing failure mode of a test that enumerates rather than names.
        await Assert.That(measured.Count(entry => entry.Surface is "instructions")).IsEqualTo(1);
        await Assert.That(measured.Count(entry => entry.Surface is "tool")).IsEqualTo(run.ToolNames.Count);
        await Assert.That(measured.Count(entry => entry.Surface is "parameter")).IsGreaterThan(100);

        // And every one of them is a real string rather than an absent member
        // counted as zero: an empty description would satisfy the budget for
        // ever.
        await Assert.That(measured.Count(entry => entry.Gated is 0)).IsEqualTo(0);
    }

    /// <summary>
    /// Serialisation that escapes nothing it does not have to, so a measured
    /// size is the size the server actually wrote.
    /// </summary>
    /// <remarks>
    /// The default encoder turns <c>—</c> into six ASCII characters. Measuring a
    /// budget through it reports a number that is not on any wire.
    /// </remarks>
    private static readonly System.Text.Json.JsonSerializerOptions Unminified = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>One model-facing string, measured both ways.</summary>
    /// <param name="Surface">Which of the three surfaces it belongs to.</param>
    /// <param name="Name">What to look at when it is over budget.</param>
    /// <param name="Characters">Its length in UTF-16 characters.</param>
    /// <param name="Bytes">Its length in UTF-8 bytes.</param>
    private sealed record ModelFacingString(string Surface, string Name, int Characters, int Bytes)
    {
        /// <summary>The figure the client actually counts.</summary>
        /// <remarks>
        /// <b>Characters.</b> <i>Corrected 2026-08-18 (previously "the
        /// conservative figure: whichever count is larger", which was always the
        /// byte count).</i> Measured @ Claude Code 2.1.234: the cut is on
        /// <see cref="string.Length"/> and a byte count is never consulted, so a
        /// byte gate fails strings the client delivers whole. Bytes stay in the
        /// record because the report prints them.
        /// </remarks>
        public int Gated => Characters;

        /// <summary>Measures one string, treating an absent one as empty.</summary>
        /// <param name="surface">Which surface it belongs to.</param>
        /// <param name="name">What to name it in a failure.</param>
        /// <param name="text">The string, as it came off the wire.</param>
        /// <returns>The measurement.</returns>
        public static ModelFacingString Of(string surface, string name, string? text) =>
            new(surface, name, text?.Length ?? 0, text is null ? 0 : Encoding.UTF8.GetByteCount(text));
    }

    /// <summary>The budget one surface is held to.</summary>
    /// <param name="surface">The surface name.</param>
    /// <returns>The cap in UTF-16 characters.</returns>
    /// <remarks>
    /// Two of the three are the client's measured cap; the parameter surface is a
    /// <b>house limit</b> the client does not impose (20,000 characters measured
    /// through intact @ 2.1.234), and it stays a separate constant so that the
    /// difference is visible where it is applied rather than only in prose.
    /// </remarks>
    private static int BudgetFor(string surface) => surface switch
    {
        "instructions" => ServerInstructions.MaximumCharacters,
        "tool" => SessionToolSurface.DescriptionMaximumCharacters,
        _ => SessionToolSurface.ParameterDescriptionMaximumCharacters,
    };

    /// <summary>
    /// Writes every measured length, sorted, so the strings near the line are
    /// visible on a run that passes.
    /// </summary>
    /// <remarks>
    /// A gate that only speaks when it fails cannot tell anybody they are 40
    /// characters from silent truncation. The per-tool entry totals go in the
    /// same block, unasserted, as the figure a future client release introducing
    /// a per-tool bucket would be judged against.
    /// </remarks>
    /// <param name="measured">Every measured string.</param>
    /// <param name="entryTotals">Each tool's whole serialized <c>tools/list</c> entry.</param>
    private static void Report(
        IReadOnlyList<ModelFacingString> measured,
        IReadOnlyList<(string Tool, int Characters, int Bytes)> entryTotals)
    {
        var report = new StringBuilder();

        _ = report.AppendLine("Model-facing string budget, measured off the published binary's own wire.");
        _ = report.AppendLine(CultureInfo.InvariantCulture, $"Per-string budget: {ServerInstructions.MaximumCharacters} UTF-16 characters, cut at > {ServerInstructions.MaximumCharacters} (measured 2026-08-18 @ Claude Code 2.1.234).");
        _ = report.AppendLine("The client does NOT cap parameter descriptions at all; that column is a house limit. Bytes are printed and never gated.");

        foreach (var surface in new[] { "instructions", "tool", "parameter" })
        {
            var rows = measured.Where(entry => entry.Surface == surface).OrderByDescending(entry => entry.Gated).ToList();

            _ = report.AppendLine();
            _ = report.AppendLine(CultureInfo.InvariantCulture, $"--- {surface}: {rows.Count} strings, largest {(rows.Count is 0 ? 0 : rows[0].Gated)} characters ---");

            foreach (var row in rows)
            {
                _ = report.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"  {row.Bytes,6} B  {row.Characters,6} c  {row.Gated * 100 / BudgetFor(surface),3}%  {row.Name}");
            }
        }

        _ = report.AppendLine();
        _ = report.AppendLine("--- UNASSERTED: each tool's WHOLE tools/list entry. Measured 2026-08-18 @ 2.1.234 there is NO per-tool bucket, so these are re-check data rather than a budget ---");

        foreach (var (tool, characters, bytes) in entryTotals.OrderByDescending(entry => entry.Bytes))
        {
            _ = report.AppendLine(CultureInfo.InvariantCulture, $"  {bytes,6} B  {characters,6} c  {(bytes > ServerInstructions.MaximumCharacters ? "2KB+" : "    ")}  {tool}");
        }

        TestContext.Current?.OutputWriter.WriteLine(report.ToString());

        try
        {
            var path = Path.Combine(RepositoryLayout.Root.FullName, ".work", "description-budget.txt");

            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, report.ToString());
        }
        catch (IOException)
        {
            // The written copy is a convenience; the assertions are the contract
            // and a scratch directory that cannot be written must not turn a
            // green run red.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Test]
    public async Task EveryUpstreamDescriptionArrivesUnchangedAndTheWithheldToolDoesNotArriveAtAll()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult = UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.SurfaceChild.ToolsListResult);
        var upstream = UpstreamSurface.SnapshotDescriptions();
        var offenders = new List<string>();

        foreach (var (name, original) in upstream)
        {
            // ⚠️ Corrected 2026-08-18 (previously every upstream tool was
            // expected in the advertised list, and one of them — `browser_annotate`
            // — was expected to have a sentence of ours appended). That tool is
            // now filtered out of `tools/list` entirely, so the shape of this
            // test changed with it: the withheld one must be ABSENT, and every
            // other description must be upstream's own, unchanged, to the byte.
            if (SessionToolPolicy.IsWithheldFromTheSurface(name))
            {
                if (advertised.ContainsKey(name))
                {
                    offenders.Add($"{name}: withheld from the surface, and still in it");
                }

                continue;
            }

            if (!advertised.ContainsKey(name))
            {
                offenders.Add($"{name}: upstream advertises it and BrowserAI does not");
                continue;
            }

            var rewritten = (string?)advertised[name]?["description"] ?? string.Empty;

            // Unchanged, asserted as equality rather than as a prefix. The
            // append hook was the only thing that ever made these differ and it
            // is gone (`SessionToolSurface.AppendModeNote`, deleted the same
            // day), so equality is now true and is the stronger claim: a prefix
            // check passes anything appended, which is what would come back if
            // the hook were reintroduced by habit.
            if (!string.Equals(rewritten, original, StringComparison.Ordinal))
            {
                offenders.Add($"{name}: the advertised description is no longer upstream's own, byte for byte");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // Not vacuous: an `Advertised` that returned nothing would satisfy every
        // "unchanged" check above by never running one. The authored tools are
        // in that dictionary too, so the arithmetic names both halves.
        await Assert.That(advertised.Count).IsEqualTo(SessionToolSurface.Names.Count + upstream.Count - 1);
        await Assert.That(advertised.ContainsKey(SessionToolPolicy.AnnotateTool)).IsFalse();

        // And nothing of the removed matrix — or of the withheld tool — survives
        // anywhere in the surface a model reads. A positive control comes first,
        // because a sweep that matches nothing is indistinguishable from a
        // genuine absence.
        var everyDescription = string.Concat(advertised.Values.Select(tool => (string?)tool?["description"] ?? string.Empty));

        await Assert.That(everyDescription).Contains("Take a screenshot of the current page");

        foreach (var gone in (string[])
        [
            "needs a session created in",
            "is not one this build has classified",
            "refuses every browser tool",
            "BrowserAI refuses this",
            SessionToolPolicy.AnnotateTool,
        ])
        {
            await Assert.That(everyDescription).DoesNotContain(gone);
        }
    }

    [Test]
    public async Task NoConditionalCompilationReachesTheEnforcementPath()
    {
        // A property of the artifact rather than of the source: the decision a
        // released binary takes must be the decision the suite took. A `#if
        // DEBUG` here would make every test above evidence about a build nobody
        // ships.
        //
        // ⚠️ FOUR FILES SINCE 2026-08-20 (previously five, the fifth being
        // `Sessions/SessionMode.cs`). That file was DELETED with session modes
        // and this list is not allowed to shrink by accident — the loop below
        // fails on a named file that is missing, which is exactly what it did
        // when the deletion landed. `Runtime/BrowserConfiguration.cs` takes its
        // place rather than the list simply getting shorter: it is where a
        // session's capability set is now decided, so it is on the enforcement
        // path by the same argument SessionMode.cs was.
        //
        // What these carry is routing — `session` is mandatory and resolves to
        // one child — the capability grant, and the single liveness refusal. All
        // three deserve the same guarantee for the same reason: a caller cannot
        // tell from the outside which build it is talking to.
        string[] enforcement =
        [
            "src/BrowserAI/Sessions/SessionToolPolicy.cs",
            "src/BrowserAI/Runtime/BrowserConfiguration.cs",
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

    [Test]
    public async Task NoEnvironmentVariableOrLaunchSwitchReachesTheEnforcementPath()
    {
        // The other half, and the half nothing checked until 2026-08-17. `#if`
        // is the compile-time route to relaxing a refusal; this is the run-time
        // one, and it is the worse of the two, because a conditionally-compiled
        // check at least ships as one artifact. A variable read here means the
        // binary the suite proved and the binary a developer runs with
        // BROWSERAI_LET_ME_THROUGH=1 set are the same artifact taking different
        // decisions, and nothing about the second reads differently from the
        // first.
        //
        // It matters more rather than less now that only the liveness refusal is
        // left: an environment variable that turned `browser_annotate` back on
        // would hang an overnight run, and the hang is the thing this product
        // exists not to do. And since 2026-08-20 the capability GRANT is on this
        // list too — a variable that quietly dropped `storage` from a session
        // would present as upstream not knowing the tool.
        //
        // ⚠️ `Sessions/SessionMode.cs` was replaced by
        // `Runtime/BrowserConfiguration.cs` here on 2026-08-20, for the reason
        // given on the scan above: the file was deleted with session modes, and
        // a list that only got shorter would have covered less while reading the
        // same.
        //
        // The convenience this forbids is real and is answered elsewhere:
        // `debug` on init and resume raises the log level so a refusal can be
        // *seen*, and changes no decision. That is the supported way to find
        // out why a call was refused.
        string[] enforcement =
        [
            "src/BrowserAI/Sessions/SessionToolPolicy.cs",
            "src/BrowserAI/Runtime/BrowserConfiguration.cs",
            "src/BrowserAI/Sessions/SessionErrors.cs",
            "src/BrowserAI/Proxy/ServerInstructions.cs",
        ];

        string[] forbidden =
        [
            "Environment.GetEnvironmentVariable",
            "Environment.GetEnvironmentVariables",
            "Environment.GetCommandLineArgs",
            "AppContext.TryGetSwitch",
            "Debugger.IsAttached",
            "RuntimeFeature",
        ];

        var offenders = new List<string>();

        foreach (var relative in enforcement)
        {
            var file = new FileInfo(Path.Combine(RepositoryLayout.Root.FullName, relative));

            if (!file.Exists)
            {
                offenders.Add($"{relative}: missing, so this scan no longer covers the path it names");
                continue;
            }

            var code = await RepositoryLayout.ReadCodeAsync(file);

            offenders.AddRange(forbidden
                .Where(needle => code.Contains(needle, StringComparison.Ordinal))
                .Select(needle => $"{relative}: reads '{needle}' on the enforcement path"));
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // BrowserProxy is deliberately outside the list above rather than
        // silently omitted from it. It is the enforcement *call site* and also
        // the process's own composition root, so it legitimately reads the
        // environment for things that are not the decision -- and a scan that
        // banned the read outright would either be red today or would train the
        // next person to move the decision somewhere the scan does not look.
        // What is asserted instead is that the decision it calls is the one in
        // SessionToolPolicy, which the four files above are closed against.
        var callSite = await RepositoryLayout.ReadCodeAsync(
            new FileInfo(Path.Combine(RepositoryLayout.Root.FullName, "src/BrowserAI/Proxy/BrowserProxy.cs")));

        await Assert.That(callSite).Contains("SessionToolPolicy.Decide");
    }

    private static void Require(List<string> missing, string rendered, string expected, string consumer)
    {
        if (!rendered.Contains(expected, StringComparison.Ordinal))
        {
            missing.Add($"{consumer} does not render '{expected}'");
        }
    }

    /// <summary>The advertised surface, keyed by name, as a caller receives it.</summary>
    [Test]
    public async Task EveryAuthoredToolAdvertisesExactlyTheArgumentSetItIsSpecifiedWith()
    {
        // §H.2 gives each of the six a signature, and until 2026-08-17 nothing
        // asserted any of them. Descriptions were measured, the mode table was
        // rendered into four consumers and checked -- and the *arguments*, which
        // are the half a model actually fills in, were whatever the class
        // happened to declare. An argument silently dropped from `init` would
        // show as a call the model stopped making, not as a red build.
        //
        // Read out of the advertised surface rather than off the class, for the
        // same reason the description assertions are: the rewrite is what a
        // model receives, and it is the rewrite that could lose a property.
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult = UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.SurfaceChild.ToolsListResult);
        var wrong = new List<string>();

        foreach (var (tool, expectedProperties, expectedRequired) in TheAuthoredSignatures)
        {
            var schema = advertised[tool]?["inputSchema"]?.AsObject();

            if (schema is null)
            {
                wrong.Add($"{tool}: not advertised at all");
                continue;
            }

            var properties = (schema["properties"]?.AsObject() ?? [])
                .Select(property => property.Key)
                .Order(StringComparer.Ordinal)
                .ToList();

            var required = (schema["required"]?.AsArray() ?? [])
                .Select(entry => (string)entry!)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (!properties.SequenceEqual(expectedProperties.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            {
                wrong.Add($"{tool}: arguments are [{string.Join(", ", properties)}], specified as [{string.Join(", ", expectedProperties.Order(StringComparer.Ordinal))}]");
            }

            if (!required.SequenceEqual(expectedRequired.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            {
                wrong.Add($"{tool}: required are [{string.Join(", ", required)}], specified as [{string.Join(", ", expectedRequired.Order(StringComparer.Ordinal))}]");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, wrong)).IsEmpty();

        // And the table above covers all six, so a seventh authored tool cannot
        // arrive unasserted.
        await Assert.That(TheAuthoredSignatures.Select(signature => signature.Tool).Order(StringComparer.Ordinal))
            .IsEquivalentTo(SessionToolSurface.Names.Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// <b>No tool asks the caller to confirm anything.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>BrowserAI reached zero confirmation flags on 2026-08-18, when
    /// <c>acknowledgeCopy</c> was deleted</b>, and this is what keeps it there.
    /// That flag gated <c>browserai_resume</c> on a directory that looked like a
    /// copy of a session that still existed. It was necessary while
    /// <c>browserai.json</c> was a snapshot — taking the copy over overwrote the only
    /// evidence that it <i>was</i> a copy — and it stopped being necessary the
    /// moment the record became an append-only list of timestamped statements,
    /// because the resume can now hand the model the whole provenance instead.
    /// </para>
    /// <para>
    /// <b>The rule this asserts is a design rule, not a naming rule.</b> A
    /// confirmation flag is a question whose entire content can be returned as
    /// fact; a model that has been told what a thing is does not need to be asked
    /// whether it meant it, and a flag it must guess at is a flag it will pass
    /// <c>true</c> to. The one thing BrowserAI still refuses outright — a
    /// reinstall while a browser is running out of the tree — is refused with
    /// <i>no force option</i> and no argument to add one, which is the same
    /// principle from the other end.
    /// </para>
    /// <para>
    /// <b>The matcher is proved before it is trusted.</b> A pattern that matches
    /// nothing is indistinguishable from a genuine absence, so the deleted flag's
    /// own name is run through it first: a sweep that cannot find the thing it
    /// was written for has not established that the thing is gone.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NoAuthoredToolAsksTheCallerToConfirmAnything()
    {
        // The vocabulary a confirmation flag arrives under. Not exhaustive over
        // English, and it does not need to be: what it has to catch is the next
        // one somebody adds by analogy with the last one.
        string[] confirmations = ["acknowledge", "confirm", "force", "iamsure", "reallY", "yesireally", "override"];

        static bool asksForConfirmation(string[] words, string property) =>
            words.Any(word => property.Contains(word, StringComparison.OrdinalIgnoreCase));

        // The positive control, first. `acknowledgeCopy` is the flag this test
        // exists because of, and if the matcher cannot see it the emptiness below
        // proves nothing at all.
        await Assert.That(asksForConfirmation(confirmations, "acknowledgeCopy")).IsTrue();
        await Assert.That(asksForConfirmation(confirmations, "directory")).IsFalse();

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            child => child.ToolsListResult = UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.SurfaceChild.ToolsListResult);
        var found = new List<string>();
        var examined = 0;

        foreach (var tool in SessionToolSurface.Names)
        {
            foreach (var property in advertised[tool]?["inputSchema"]?["properties"]?.AsObject() ?? [])
            {
                examined++;

                if (asksForConfirmation(confirmations, property.Key))
                {
                    found.Add($"{tool} takes '{property.Key}'");
                }
            }
        }

        await Assert.That(string.Join(Environment.NewLine, found)).IsEmpty();

        // And the sweep really looked at something. An enumeration that silently
        // produced nothing would satisfy the assertion above.
        await Assert.That(examined).IsGreaterThan(SessionToolSurface.Names.Count);
    }

    private static Dictionary<string, JsonObject?> Advertised(string childToolsList)
    {
        var rewritten = SessionToolSurface.Rewrite(JsonNode.Parse(childToolsList)!.AsObject());

        return (rewritten["tools"]?.AsArray() ?? [])
            .ToDictionary(tool => (string)tool!["name"]!, tool => tool?.AsObject(), StringComparer.Ordinal);
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
