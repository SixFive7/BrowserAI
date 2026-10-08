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
    /// here and not read from the class under test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b><c>browserai_catch_up</c> arrived 2026-08-20 and it is the one
    /// session-scoped tool with no <c>why</c>.</b> That is deliberate twice
    /// over: a tool whose whole purpose is to tell you what happened must not
    /// itself become the most recent thing that happened, and writing an entry
    /// would mean taking the per-directory gate, which a session another live
    /// BrowserAI is driving would refuse -- which is the case it exists for. The
    /// row is here so that a future <c>why</c> added to it is a red build and
    /// not a silent widening.
    /// </para>
    /// <para>
    /// ⚠️ <b>And it takes a required <c>why</c> since 2026-10-04</b>, the
    /// maintainer's words of 2026-10-03: <i>"browserai_catch_up should take a why.
    /// All other tool calls are fine like they are now when it comes to the why
    /// argument."</i> The row above changed with that decision, which is the
    /// widening it was here to make deliberate.
    /// </para>
    /// <para>
    /// ⚠️ <b>Changed 2026-08-20: <c>mode</c> is gone from <c>init</c> and
    /// <c>headed</c> is on both.</b> Session modes were deleted, every
    /// capability is granted to every session, and headedness became a per-run
    /// argument -- so <c>init</c> no longer has a third required property and
    /// <c>resume</c> takes one more optional one.
    /// </para>
    /// <para>
    /// <b>Three of these differ from §H.2's table, and the difference is
    /// deliberate, not drift.</b> <c>browserai_resume</c> ships
    /// <c>tracing</c> and <c>consoleLevel</c>, which §H.2 gives only to
    /// <c>init</c>. The rule §H.2 states for refusing an argument on
    /// <c>resume</c> is that <i>"a profile is browser-specific"</i> -- it is
    /// about <c>browser</c>, and about facts the directory on disk already
    /// records. Neither of these is such a fact: <c>tracing</c> becomes
    /// <c>saveSession</c> and <c>consoleLevel</c> becomes <c>console.level</c>
    /// in the config generated for <i>this run</i>, both of which may honestly
    /// differ from run to run without contradicting anything the session
    /// recorded. Refusing them would have meant destroying and recreating a
    /// session to turn tracing on for one afternoon.
    /// </para>
    /// <para>
    /// ⚠️ <b>Changed 2026-10-04: <c>tracing</c> is <c>transcript</c> on both
    /// tools.</b> Q371 c, the maintainer's words of 2026-10-03 verbatim: <i>"I like
    /// option c and the rename to transcript."</i> What the argument switches on
    /// is upstream's <c>saveSession</c>, a <c>session.md</c> transcript of the run's
    /// browser calls, and no Playwright trace; the old name is not accepted, like
    /// any other name the schema does not carry.
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
        // ⚠️ `idleMinutes` on both since 2026-10-08, E2.
        (SessionToolSurface.Init,
            ["directory", "purpose", "headed", "browser", "transcript", "captureNetwork", "viewport", "locale", "timezone", "ignoreHTTPSErrors", "debug", "idleMinutes"],
            ["directory", "purpose"]),
        (SessionToolSurface.Resume,
            ["directory", "purpose", "why", "headed", "debug", "transcript", "captureNetwork", "viewport", "locale", "timezone", "ignoreHTTPSErrors", "idleMinutes"],
            ["directory", "why"]),
        // ⚠️ ADDED 2026-10-08, F1 a: the ninth authored tool.
        (SessionToolSurface.Close, ["session", "why"], ["session", "why"]),
        (SessionToolSurface.CatchUp, ["session", "page", "why"], ["session", "why"]),
        (SessionToolSurface.List, ["directory"], ["directory"]),
        (SessionToolSurface.Destroy, ["directory", "why"], ["directory", "why"]),
        (SessionToolSurface.ChangePurpose, ["session", "purpose", "why"], ["session", "purpose", "why"]),
        (SessionToolSurface.ReinstallBrowser, ["browser"], ["browser"]),
        (SessionToolSurface.PageTool,
            ["session", "name", "arguments", "page", "why"],
            ["session", "name", "arguments", "why"]),
    ];

    /// <summary>
    /// The declared list of upstream phrases whose disappearance is a red build.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not a whole-string comparison</b> -- the whole point of the rewrite is
    /// that the string changes. This is the record of <i>why</i> a sentence is
    /// there: a phrase is added the moment anyone decides upstream's wording is
    /// load-bearing, and a rewrite that drops it fails instead of quietly
    /// removing the warning a model was acting on.
    /// </para>
    /// <para>
    /// Every phrase below is one a model acts on -- what a tool costs, what it
    /// refuses, that it is dangerous -- read out of the committed snapshot on
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
        // annotations") -- "blocks on a human. A model that does not know this
        // schedules it and waits forever." The tool is withheld from the surface
        // now, so there is no description of it for a phrase to survive in, and
        // the fact the phrase protected is answered by removal and not by
        // warning. Keeping the row would have failed the test below on its
        // "not in the advertised surface at all" arm, which is the arm that
        // exists to stop exactly this becoming a silent skip.

        // Names the tool whose output the argument comes from; without it the
        // number is unguessable.
        ("browser_network_request", "Use the number from browser_network_requests"),

        // Says the config is the RESOLVED one, which is the whole reason to call
        // it and not to read the file.
        ("browser_get_config", "after merging CLI options, environment variables and config file"),

        // Where the credentials go, said in the description and not only in
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
    /// warning, and the retention policy"* in the creation tool's description --
    /// the spec requires retention to be stated <i>there</i>, and <c>init</c>'s
    /// description is the channel a model sees at the moment it reaches for the
    /// tool; the
    /// [DECISIONS](../../DECISIONS.md#shape-and-packaging) demands the same thing independently, because
    /// <c>init</c> accepts any path -- *"the `init` tool description is a security
    /// surface ... say plainly what pointing at an existing browser profile
    /// does"*. Retention was stated on <c>resume</c> and on <c>list</c>, which is
    /// everywhere except where it was required.
    /// </para>
    /// <para>
    /// <b>Phrases and not a whole-string comparison</b>, for the same reason
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
        // anticipated, and the phrase moved WITH the fact instead of the fact
        // being trimmed to keep the phrase.
        "nothing else about it is validated",
        "live cookies and logins",

        // The retention policy, stated where the session is created and not
        // only where one is resumed or listed. The tool name is part of the
        // requirement: a retention policy with no way to act on it is a fact
        // and not guidance.
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
    /// <b>Written down and not read off
    /// <see cref="SessionToolSurface.NewlyGrantedTools"/>.</b> Derived from the
    /// product's own list this would agree with it by construction and could
    /// never fail -- the same reason the mode table this replaced kept a
    /// hand-written expectation row. What it can catch: a capability quietly
    /// dropped from <see cref="BrowserConfiguration.GrantedCapabilities"/>, an
    /// upstream rename, and a product list edited to match a surface instead of
    /// the other way round.
    /// </para>
    /// <para>
    /// <b>None of these has ever been reachable</b> -- not in BrowserAI and not
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
    /// The five test-writing helpers the maintainer dropped on 2026-10-03,
    /// Q365.2 a, written down here for the same reason the ten above are.
    /// </summary>
    private static readonly string[] TheTestHelpersDropped =
    [
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
    /// row of <c>SessionModes.All</c> -- the server instructions, <c>init</c>'s
    /// description, <c>resume</c>'s result, the refusal a bad <c>mode</c>
    /// produced, the generated child config, and the suite's own expectation
    /// table. Five of the six no longer exist, and the sixth renders nothing
    /// that varies. It was <b>replaced, not deleted</b>: the failure it
    /// existed to catch -- a capability decided in one place and rendered in
    /// another, drifting silently -- is exactly the failure a grant of ten
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
            toolsList: UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.ToolsList);
        var missing = new List<string>();

        // 1. The two lists agree, in both directions. The product's own list is
        //    what a reader is sent to; this one is what fails when it drifts.
        missing.AddRange(TheNewlyGrantedTen
            .Where(tool => !SessionToolSurface.NewlyGrantedTools.Contains(tool, StringComparer.Ordinal))
            .Select(tool => $"'{tool}' is expected to be a newly-granted tool and the product does not list it"));

        missing.AddRange(SessionToolSurface.NewlyGrantedTools
            .Where(tool => !TheNewlyGrantedTen.Contains(tool, StringComparer.Ordinal))
            .Select(tool => $"the product lists '{tool}' as newly granted and this test does not expect it"));

        // 2. Each of the ten, by name, in the surface a model receives -- except
        //    the five test-writing helpers, denied on 2026-10-03 by the
        //    maintainer's Q365.2 a: "Drop browser_verify_element_visible,
        //    browser_verify_text_visiblem, browser_verify_list_visible,
        //    browser_verify_value and browser_generate_locator per your
        //    recommendation." Those are granted to the child and kept out of the
        //    list, which is what a deny row does. A count is satisfied by the
        //    wrong tool as easily as by the right one, so each is named.
        missing.AddRange(TheNewlyGrantedTen
            .Where(tool => !TheTestHelpersDropped.Contains(tool, StringComparer.Ordinal) && !advertised.ContainsKey(tool))
            .Select(tool => $"'{tool}' is not in the advertised surface"));

        missing.AddRange(TheTestHelpersDropped
            .Where(advertised.ContainsKey)
            .Select(tool => $"'{tool}' was dropped by Q365.2 a and is still in the advertised surface"));

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
        //    has ever opened -- a reader meeting the grant must not come away
        //    thinking it arrived with it.
        if (SessionToolSurface.NewlyGrantedTools.Contains("browser_run_code_unsafe", StringComparer.Ordinal))
        {
            missing.Add("browser_run_code_unsafe is listed as newly granted; it is core and always was");
        }

        if (!UpstreamSurface.DefaultSurface().Contains("browser_run_code_unsafe", StringComparer.Ordinal))
        {
            missing.Add("browser_run_code_unsafe is not in upstream's default surface, so the claim that it is core is stale");
        }

        // 6. The response-mocking warning is a BrowserAI note on the two tools it
        //    is about since 2026-10-04 -- the maintainer's words of 2026-10-03:
        //    "Rewrite the instructions according to b." -- and no longer in the
        //    server instructions. Upstream's own description comes first and
        //    unchanged, the note after it, marked as BrowserAI's. Previously the
        //    warning was in the instructions and browser_route's description was
        //    held to upstream's bytes.
        foreach (var (tool, phrases) in new[]
        {
            ("browser_route", (string[])["mocked response", "browser_unroute", "'why'"]),
            ("browser_network_state_set", (string[])["offline", "'why'"]),
        })
        {
            var upstream = UpstreamSurface.SnapshotDescriptions().Single(entry => entry.Name == tool).Description;
            var description = (string?)advertised[tool]?["description"] ?? string.Empty;

            if (!description.StartsWith(upstream + SessionToolSurface.NoteMarker, StringComparison.Ordinal))
            {
                missing.Add($"{tool}'s description is not upstream's own bytes followed by a BrowserAI note");
            }

            var note = description.Length > upstream.Length ? description[upstream.Length..] : string.Empty;

            missing.AddRange(phrases
                .Where(phrase => !note.Contains(phrase, StringComparison.Ordinal))
                .Select(phrase => $"{tool}'s BrowserAI note does not say '{phrase}'"));
        }

        if (ServerInstructions.Text.Contains("browser_route", StringComparison.Ordinal))
        {
            missing.Add("the server instructions still carry the mocking warning, which moved to the tools it is about");
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
        // the ten. 58 was the advertised count on 2026-08-19, and that sentence
        // stays true -- what moves the base to 60 is @playwright/mcp 0.0.80,
        // which added browser_start_recording and browser_stop_recording, both
        // judged `allow` on 2026-09-15. The ten are still the ten: the addend is
        // the capability grant, and the base is whatever upstream ships.
        //
        // ⚠️ The base is 60 since 2026-10-03 (previously 61), and this time a
        // decision moved it: the maintainer denied browser_resume, which was
        // `allow` and is not one of the ten, so the base lost one and the
        // surface did not. It went red on the first gate after the deny,
        // naming 70 against 71.
        //
        // ⚠️ The base was 61 from 2026-09-21 (previously 62), and it went DOWN
        // for the first time. @playwright/mcp 0.0.82 marked
        // browser_webmcp_list and browser_webmcp_call `skillOnly`, so both left
        // the exposed surface entirely and their verdict rows were deleted as
        // judgements about nothing. The pair that moved this base by ONE on the
        // way in moved it by ONE on the way out, for the mirror-image reason:
        // only the `allow` was ever counted here. Re-counted off the
        // regenerated snapshot, not decremented.
        //
        // ⚠️ The base was 62 from 2026-09-17 (previously 61): the dated
        // playwright-core override added browser_emulate_media, `core` and so
        // unconditional, judged `allow`, so upstream's one tool moves this base
        // by one. That is the ORDINARY case and the note below is the one that
        // was not.
        //
        // ⚠️ The base was 61 from 2026-09-15 (previously 60): @playwright/mcp
        // 0.0.81 added browser_webmcp_list and browser_webmcp_call, and the two
        // were judged in OPPOSITE directions on the same day -- `allow` for the
        // list, `deny` for the call, on liveness -- so upstream's pair moved this
        // base by one and not by two. That asymmetry is the whole reason the
        // number is stated: a base one higher than the surface warrants would
        // mean a denial had stopped withholding, and one lower would mean a
        // tool had never arrived.
        //
        // ⚠️ And five fewer from 2026-10-04: the test-writing helpers are denied
        // by Q365.2 a and so leave the surface. Re-counted off the rewrite, not
        // decremented: 65.
        //
        // ⚠️ The base is 59 since 2026-10-04 (previously 60): the maintainer's
        // Q365.1 a denied browser_set_storage_state, which is `storage` and not
        // one of the ten, so the base lost one the way it did for
        // browser_resume. 64.
        //
        // ⚠️ The base is 58 since 2026-10-08 (previously 59): the maintainer's
        // F1 a denied browser_close, which is `core` and not one of the ten, so
        // the base lost one the way it did for browser_set_storage_state, and
        // BrowserAI's own browserai_close took its place among the authored
        // tools, which this count leaves out. 63.
        await Assert.That(advertised.Count(entry => !SessionToolSurface.IsAuthored(entry.Key)))
            .IsEqualTo(58 + TheNewlyGrantedTen.Length - TheTestHelpersDropped.Length);
    }

    /// <summary>The generated config's capability list, as JSON, for one headedness.</summary>
    /// <param name="headed">Whether the session opens a window.</param>
    /// <returns>The <c>capabilities</c> opinion, serialised.</returns>
    private static string CapabilitiesOf(bool headed) =>
        BrowserConfiguration.ForSession(
            SessionPath.For(Path.Combine(ScratchRoot.Path, $"capabilities-{(headed ? "headed" : "headless")}")),
            headed,
            SessionManager.DefaultBrowser,
            transcript: false,
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
        // It is reported, not gated, so that the figure a wire capture
        // shows is not a figure nothing in this repository names.
        await Assert.That(ServerInstructions.ByteCount).IsGreaterThanOrEqualTo(ServerInstructions.CharacterCount);

        // ⚠️ Re-measured 2026-09-21 off the published binary's own `initialize`
        // response: **2,026 characters and 2,036 bytes**, leaving 22.
        //
        // Corrected 2026-09-21 (previously "Re-measured 2026-08-18 ... **1,261
        // characters and 1,276 bytes**, leaving 772. The three mode lines cost
        // 106, 121 and 92 bytes apiece"). That reading was true of the string as
        // it stood; six changes have landed in it since and none of them came
        // back here, which is how a measured figure turns into a stale one
        // without anybody writing anything false. The number is not gated and
        // never has been -- the cap above is -- so nothing went red for 34 days.
        //
        // Corrected 2026-08-18 (previously "Measured 2026-08-16: 1,613
        // characters and **1,628 bytes** ... The headroom is 420 bytes ... Planting
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

    /// <summary>
    /// The server instructions are the lean text of the maintainer's rewrite b,
    /// which keeps only rules that span tools.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Decided 2026-10-03 by the maintainer, in his words:</b> <i>"About the
    /// server instructions. Rewrite the instructions according to b. Then on the
    /// why, keep the server instruction simple. The tool arguments will teach the
    /// model the exceptions anyway."</i> So the text says every call takes a
    /// <c>why</c>, and the schemas of the three that do not carry one show the
    /// exceptions. The advice for one tool moved to that tool: the full-page cost
    /// to <c>browser_take_screenshot</c>, the boxes line to the coordinate tools,
    /// the mocking warning to <c>browser_route</c> and
    /// <c>browser_network_state_set</c>, and the directory and purpose paragraph
    /// to <c>browserai_init</c>'s arguments.
    /// </para>
    /// <para>
    /// <b>Asserted whole, and that is deliberate for a text this short</b>: it is
    /// his, it was approved as a whole, and a change to it is a change he makes.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheInstructionsAreTheLeanTextThatKeepsOnlyRulesSpanningTools()
    {
        const string Expected =
            "BrowserAI drives a real browser through sessions. A session is a folder holding the browser profile (logins, cookies), downloads, screenshots and a log of every call. "
            + "Start with browserai_init for a new session or browserai_resume for an existing one; both answer with the session's folder path, which you pass as 'session' to every other tool.\n\n"
            + "Every call takes a 'why': write why you are making it, not what it does. It goes in the session's record, and browserai_catch_up reads it back beside what the folder holds now: "
            + "call it when you arrive at a session you did not create, and before you destroy one.\n\n"
            + "BrowserAI manages its own browsers. If a browser is missing or broken, call browserai_reinstall_browser.\n\n"
            + "Nothing but browserai_destroy deletes a session: destroy yours when the work is done, and promptly if it held a login.";

        await Assert.That(ServerInstructions.Text).IsEqualTo(Expected);
        await Assert.That(ServerInstructions.CharacterCount).IsLessThanOrEqualTo(ServerInstructions.MaximumCharacters);

        // Every tool the text names is one this build has.
        foreach (var tool in new[] { SessionToolSurface.Init, SessionToolSurface.Resume, SessionToolSurface.CatchUp, SessionToolSurface.ReinstallBrowser, SessionToolSurface.Destroy })
        {
            await Assert.That(Expected).Contains(tool);
        }
    }

    /// <summary>
    /// The directory and purpose advice is on <c>browserai_init</c>'s own
    /// arguments, and on <c>browserai_resume</c>'s purpose.
    /// </summary>
    /// <remarks>
    /// <b>Moved 2026-10-04 out of the instructions</b> by the maintainer's
    /// rewrite b. The paragraph was: <i>"Supply an absolute directory and a
    /// one-sentence 'purpose'. The directory IS the session -- its profile,
    /// screenshots, downloads and log live there -- so name it for the work, and
    /// write the purpose for the next agent that meets it."</i> Each half is now on
    /// the argument it is about.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheDirectoryAndPurposeAdviceIsOnTheArgumentsItIsAbout()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            toolsList: UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.ToolsList);

        string described(string tool, string argument) =>
            (string?)advertised[tool]?["inputSchema"]?["properties"]?[argument]?["description"] ?? string.Empty;

        var missing = new List<string>();

        foreach (var (tool, argument, phrase) in new[]
        {
            (SessionToolSurface.Init, "directory", "Absolute path"),
            (SessionToolSurface.Init, "directory", "The directory IS the session"),
            (SessionToolSurface.Init, "directory", "name it for the work"),
            (SessionToolSurface.Init, "purpose", "One sentence"),
            (SessionToolSurface.Init, "purpose", "the next agent that meets it"),
            (SessionToolSurface.Resume, "purpose", "the next agent that meets it"),
        })
        {
            if (!described(tool, argument).Contains(phrase, StringComparison.Ordinal))
            {
                missing.Add($"{tool}'s '{argument}' does not say '{phrase}'");
            }
        }

        if (ServerInstructions.Text.Contains("Supply an absolute directory", StringComparison.Ordinal))
        {
            missing.Add("the server instructions still carry the directory and purpose paragraph, which moved to the arguments");
        }

        await Assert.That(string.Join(Environment.NewLine, missing)).IsEmpty();
    }

    /// <summary>
    /// The model is told, before it calls anything, that BrowserAI owns the
    /// browsers and that <c>browserai_reinstall_browser</c> is the repair.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It is a pre-emption, and the thing it pre-empts is a model acting on
    /// its own training.</b> Every published account of a broken Playwright
    /// install ends in <c>npx playwright install</c>, and a model that runs it
    /// here either fails or succeeds into a second browser tree in a second
    /// location BrowserAI will never launch from -- the same harm
    /// <c>ProvisioningRemediation</c> exists to undo, arriving by a route no
    /// answer-rewrite can reach because nothing in this server said it.
    /// </para>
    /// <para>
    /// <b>Here and not on a tool description, for this file's standing
    /// reason.</b> <c>instructions</c> is the one model-facing string BrowserAI
    /// writes; every upstream description passes through byte for byte, and
    /// there is no tool to hang it on anyway -- the mistake is made <i>instead
    /// of</i> calling a tool.
    /// </para>
    /// <para>
    /// <b>The wording is asserted verbatim and not by phrase, and that is
    /// deliberate for this one sentence.</b> The maintainer wrote it; the two
    /// halves -- <i>never install any yourself</i> and <i>this is the repair</i> --
    /// are each useless without the other, and a re-draft that keeps one is the
    /// failure worth a red build.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheBrowserInstallationSentenceIsInTheInstructionsAndInsideTheBudget()
    {
        // ⚠️ REWRITTEN 2026-10-04 with the instructions -- the maintainer's words
        // of 2026-10-03: "Rewrite the instructions according to b." -- and the
        // sentence is the positive one: what BrowserAI does and what to call.
        // Previously "Browsers are managed by BrowserAI -- never install any
        // yourself (no `npx playwright install`). If the browser installation is
        // broken, `browserai_reinstall_browser` is the repair." The npx example
        // is gone from the one string that reaches a model first; upstream's own
        // install advice is still replaced in an answer by ProvisioningRemediation.
        const string Sentence =
            "BrowserAI manages its own browsers. If a browser is missing or broken, call browserai_reinstall_browser.";

        await Assert.That(ServerInstructions.Text).Contains(Sentence);
        await Assert.That(ServerInstructions.Text).DoesNotContain("npx");

        // The sentence names the tool that actually exists, and not a name
        // somebody typed: a repair a model cannot call is worse than none.
        await Assert.That(Sentence).Contains(SessionToolSurface.ReinstallBrowser);

        // ⚠️ The budget, asserted again HERE and not left to the test above,
        // because this is the change that spends it. The client cuts at 2,048
        // UTF-16 characters with nothing reported, so a sentence added past the
        // cut is a sentence nobody has ever read -- which is the exact failure it
        // was added to prevent, wearing a green suite.
        await Assert.That(ServerInstructions.CharacterCount).IsLessThanOrEqualTo(ServerInstructions.MaximumCharacters);

        // And it survives the wire, and not only the constant.
        await using var rig = await McpTestHarness.ThroughTheProxyAsync();

        var initialize = await rig.Client.RoundTripAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = TestDefaults.CallerProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "install-sentence-probe", ["version"] = "0" },
        });

        await Assert.That((string?)initialize["instructions"]).Contains(Sentence);
    }

    /// <summary>
    /// The cost of <c>fullPage: true</c> is stated in the one string BrowserAI
    /// writes itself, and <b>not</b> appended to the tool it is about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both halves, because either one alone is the wrong fix.</b> Upstream's
    /// <c>browser_take_screenshot</c> description explains what <c>fullPage</c>
    /// does and cannot know what it costs here -- nothing downscales the image on
    /// the way back, so what the page renders is what the model receives. The
    /// instinctive repair is to append a sentence to that description, and the
    /// append path was <b>deleted</b> on 2026-08-18 so that every upstream
    /// description passes through byte for byte. So this asserts the sentence is
    /// in the <c>instructions</c> <i>and</i> that the tool's own description is
    /// still upstream's bytes: a future edit that moves it onto the tool fails
    /// here instead of passing on the half it satisfied.
    /// </para>
    /// <para>
    /// ⚠️ <b>Re-measured 2026-09-14 and the required phrases changed with it.</b>
    /// <i>Previously: "Measured 2026-08-20 ... a viewport shot at the 1920x1080
    /// default arrives as 2,691 visual tokens; <c>fullPage: true</c> over a
    /// 3,637 px document leaves as 1920x3637 = 8,970, and the API downscales
    /// that to its per-image ceiling of 4,784. Break-even is a document about
    /// 1,960 px tall, so every full-page shot of a page long enough to want one
    /// costs the maximum", with the phrases <c>maximum</c> and <c>ceiling</c>.</i>
    /// Upstream deleted <c>scaleImageToFitMessage</c>, and a raw child measured
    /// at three viewports, both page shapes and three encodings returned an
    /// inline block <b>byte-identical to the file every time</b>, with bytes
    /// following pixels and no ceiling anywhere -- a <c>fullPage</c> shot of a
    /// 20,016 px document came back at 1280x20016. The sentence that told a model
    /// its image had been shrunk to a ceiling was therefore false in the
    /// direction that costs money.
    /// </para>
    /// <para>
    /// <b>The phrases are asserted and not the whole sentence.</b> Wording is
    /// the maintainer's to tune; what must survive a re-draft is that the model
    /// is told the parameter's name, that there is <b>no</b> ceiling, and that
    /// <c>filename</c> is the way to pay nothing -- which is the actionable half
    /// and the half the old sentence never had.
    /// </para>
    /// <para>
    /// ⚠️ <b>MOVED 2026-10-04 onto the tool, and both halves above are inverted
    /// by it</b> -- the maintainer's words of 2026-10-03: <i>"Rewrite the
    /// instructions according to b."</i>, a lean text that keeps only rules
    /// spanning tools, with the advice for one tool moved to that tool. It is a
    /// BrowserAI note now, declared beside the tool's verdict in
    /// <c>tool-verdicts.json</c> and appended after upstream's own description,
    /// which still comes first and unchanged. <i>Previously the arm was
    /// TheFullPageScreenshotCostIsInTheInstructionsAndNotOnTheToolsDescription</i>,
    /// asserting the sentence in the instructions and upstream's bytes alone on
    /// the tool.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheFullPageScreenshotCostIsANoteOnTheScreenshotToolAndNotInTheInstructions()
    {
        var missing = new List<string>();

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            toolsList: UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.ToolsList);
        var upstreamScreenshot = UpstreamSurface.SnapshotDescriptions()
            .Single(tool => tool.Name == "browser_take_screenshot").Description;
        var description = (string?)advertised["browser_take_screenshot"]?["description"] ?? string.Empty;

        if (!description.StartsWith(upstreamScreenshot + SessionToolSurface.NoteMarker, StringComparison.Ordinal))
        {
            missing.Add("browser_take_screenshot's description is not upstream's own bytes followed by a BrowserAI note");
        }

        var note = description.Length > upstreamScreenshot.Length ? description[upstreamScreenshot.Length..] : string.Empty;

        foreach (var required in RequiredFullPageCostPhrases)
        {
            if (!note.Contains(required, StringComparison.Ordinal))
            {
                missing.Add($"browser_take_screenshot's BrowserAI note does not say '{required}', so nothing tells a model what a full-page screenshot costs before it takes one");
            }

            if (ServerInstructions.Text.Contains(required, StringComparison.Ordinal))
            {
                missing.Add($"the server instructions still say '{required}', which moved to the tool it is about");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, missing)).IsEmpty();
    }

    /// <summary>
    /// What the <c>fullPage</c> cost line has to keep saying, however it is
    /// reworded.
    /// </summary>
    private static readonly string[] RequiredFullPageCostPhrases =
    [
        "'fullPage: true'",
        "no ceiling",
        "'filename'",
    ];

    /// <summary>
    /// Every coordinate tool tells a model to ask <c>browser_snapshot</c> for
    /// boxes first, and both names in that note are real.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q322 a, decided 2026-10-03 by the maintainer, in his words:
    /// <i>"Q322 a"</i>.</b> The generated config writes <c>snapshot.boxes</c> as
    /// <c>false</c>, which is upstream's own default, and
    /// <c>browser_snapshot</c>'s per-call <c>boxes</c> parameter stays. A
    /// snapshot without boxes carries no coordinates, and the
    /// <c>browser_mouse_*_xy</c> tools take nothing else, so the one string
    /// BrowserAI writes says how to get them before the first call is made.
    /// <i>Previously the config wrote <c>true</c> for every session</i>, and
    /// <c>browser_snapshot</c> returns its snapshot inline, so every snapshot paid
    /// for boxes: 175,611 tokens against 105,804 over nine pages, measured
    /// 2026-09-25.
    /// </para>
    /// <para>
    /// <b>The phrases are asserted and not the whole sentence</b>, for the reason
    /// <see cref="TheFullPageScreenshotCostIsANoteOnTheScreenshotToolAndNotInTheInstructions"/>
    /// gives. <b>And both names are held against upstream's own snapshot</b>:
    /// <c>browser_snapshot</c> really takes a boolean <c>boxes</c>, and the glob
    /// matches tools that exist and that require a coordinate. A sentence naming a
    /// parameter or a family of tools that has gone is one a model acts on and
    /// fails with.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryCoordinateToolCarriesANoteToAskForBoxesFirst()
    {
        // ⚠️ MOVED 2026-10-04 from the instructions to the tools it is about,
        // as a BrowserAI note -- the maintainer's words of 2026-10-03: "Rewrite
        // the instructions according to b." Previously the arm was
        // TheInstructionsTellAModelToAskForBoxesBeforeACoordinateTool, with the
        // sentence "Call browser_snapshot with 'boxes: true' before a
        // browser_mouse_*_xy tool." in the instructions.
        var missing = new List<string>();

        var tools = JsonNode.Parse(UpstreamSurface.SnapshotToolsListResult())!["tools"]!.AsArray().OfType<JsonObject>().ToList();

        var snapshot = tools.Single(tool => (string?)tool["name"] == "browser_snapshot");

        if ((string?)snapshot["inputSchema"]?["properties"]?["boxes"]?["type"] != "boolean")
        {
            missing.Add("browser_snapshot no longer takes a boolean 'boxes' in upstream's snapshot, so the note names a parameter that is not there");
        }

        var coordinateTools = tools
            .Where(tool => (string?)tool["name"] is { } name
                && name.StartsWith("browser_mouse_", StringComparison.Ordinal)
                && name.EndsWith("_xy", StringComparison.Ordinal))
            .ToList();

        if (coordinateTools.Count is 0)
        {
            missing.Add("no tool in upstream's snapshot matches browser_mouse_*_xy, so the note is on a family of tools that is not there");
        }

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            toolsList: UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.ToolsList);

        foreach (var tool in coordinateTools)
        {
            var name = (string)tool["name"]!;
            var required = (tool["inputSchema"]?["required"]?.AsArray() ?? []).Select(node => (string?)node).ToList();

            if (!required.Contains("x") && !required.Contains("startX"))
            {
                missing.Add($"{name} matches browser_mouse_*_xy and requires no coordinate");
            }

            var upstream = (string?)tool["description"] ?? string.Empty;
            var description = (string?)advertised[name]?["description"] ?? string.Empty;

            if (!description.StartsWith(upstream + SessionToolSurface.NoteMarker, StringComparison.Ordinal))
            {
                missing.Add($"{name}'s description is not upstream's own bytes followed by a BrowserAI note");
                continue;
            }

            missing.AddRange(RequiredBoxesPhrases
                .Where(phrase => !description[upstream.Length..].Contains(phrase, StringComparison.Ordinal))
                .Select(phrase => $"{name}'s BrowserAI note does not say '{phrase}'"));
        }

        if (ServerInstructions.Text.Contains("'boxes: true'", StringComparison.Ordinal))
        {
            missing.Add("the server instructions still carry the boxes line, which moved to the tools it is about");
        }

        await Assert.That(string.Join(Environment.NewLine, missing)).IsEmpty();
    }

    /// <summary>
    /// What the boxes note has to keep saying, however it is reworded: the tool to
    /// call and the argument to pass.
    /// </summary>
    private static readonly string[] RequiredBoxesPhrases =
    [
        "browser_snapshot",
        "'boxes: true'",
    ];

    /// <summary>
    /// The <c>transcript</c> argument says what it writes -- upstream's
    /// <c>session.md</c>, every browser call of the run with its arguments and its
    /// result -- and that typed passwords land in it, on both tools that take it;
    /// <c>tracing</c> is advertised nowhere, and the instructions say nothing
    /// about either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q371 c, decided 2026-10-03 by the maintainer, in his words: <i>"I like
    /// option c and the rename to transcript."</i></b> Option c was to rename the
    /// argument to what it does, shorten both descriptions, and drop the clause the
    /// instructions carried about it. It switches on upstream's <c>saveSession</c>,
    /// a <c>session.md</c> in a <c>session-&lt;milliseconds&gt;</c> folder of the
    /// output directory, and never a Playwright trace; under its old name both
    /// descriptions had to say what it was not, which is the sign of a wrong name.
    /// </para>
    /// <para>
    /// <b>Five checks, because each can go stale on its own.</b> The phrases, on
    /// both tools and identical between them; the absence of the old name from
    /// both schemas and of either name from the instructions; the generator, which
    /// must go on writing <c>transcript</c> as <c>saveSession</c>; and upstream's
    /// own code, read out of the assembled payload, which must go on writing
    /// <c>session.md</c> for that key. The payload half comes last, so a machine
    /// without the payload checks the others before it skips.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheTranscriptArgumentSaysWhatItWritesAndThatTypedPasswordsLandInIt()
    {
        var missing = new List<string>();

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            toolsList: UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.ToolsList);
        var descriptions = new List<string>();

        foreach (var tool in new[] { SessionToolSurface.Init, SessionToolSurface.Resume })
        {
            var properties = advertised[tool]?["inputSchema"]?["properties"]?.AsObject();
            var description = (string?)properties?["transcript"]?["description"];

            if (description is null)
            {
                missing.Add($"{tool} advertises no 'transcript'");
                continue;
            }

            descriptions.Add(description);

            foreach (var phrase in RequiredTranscriptPhrases)
            {
                if (!description.Contains(phrase, StringComparison.Ordinal))
                {
                    missing.Add($"{tool}'s 'transcript' description does not say '{phrase}'");
                }
            }

            foreach (var phrase in ForbiddenTranscriptPhrases)
            {
                if (description.Contains(phrase, StringComparison.Ordinal))
                {
                    missing.Add($"{tool}'s 'transcript' description still says '{phrase}', which the rename made unnecessary");
                }
            }

            if (properties!.ContainsKey("tracing"))
            {
                missing.Add($"{tool} still advertises 'tracing'");
            }
        }

        if (descriptions.Distinct(StringComparer.Ordinal).Count() > 1)
        {
            missing.Add("browserai_init and browserai_resume describe 'transcript' in two different texts");
        }

        if (ServerInstructions.Text.Contains("tracing", StringComparison.Ordinal)
            || ServerInstructions.Text.Contains("transcript", StringComparison.Ordinal)
            || ServerInstructions.Text.Contains("session.md", StringComparison.Ordinal))
        {
            missing.Add("the server instructions still carry a clause about the transcript, which Q371 c dropped");
        }

        var session = SessionPath.For(Path.Combine(ScratchRoot.Path, "generator-transcript"));

        foreach (var transcript in new[] { true, false })
        {
            var config = JsonNode.Parse(BrowserConfiguration.ForSession(session, headed: false, ProvisionedBrowsers.Chromium, transcript, RunOptions.Default).Json)!;

            if ((bool?)config["saveSession"] != transcript)
            {
                missing.Add($"'transcript: {(transcript ? "true" : "false")}' generates saveSession {config["saveSession"]?.ToJsonString() ?? "<absent>"}, so the description no longer says what it switches on");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, missing)).IsEmpty();

        // ---- Upstream's half, out of the code that actually runs.
        SuiteEnvironment.RequireRepositoryPayload();

        var bundle = await File.ReadAllTextAsync(Path.Combine(
            RepositoryPayload.Layout.Root,
            "mcp",
            "node_modules",
            "playwright-core",
            "lib",
            "coreBundle.js"));

        await Assert.That(bundle).Contains("this._config.saveSession ? await SessionLog.create(", StringComparison.Ordinal);
        await Assert.That(bundle).Contains("`session-${Date.now()}`", StringComparison.Ordinal);
        await Assert.That(bundle).Contains(", \"session.md\");", StringComparison.Ordinal);
    }

    /// <summary>
    /// What the <c>transcript</c> description has to keep saying, however it is
    /// reworded: the file it writes, and that what is typed into a page is in it.
    /// </summary>
    private static readonly string[] RequiredTranscriptPhrases =
    [
        "session.md",
        "passwords included",
        "plain text",
        "Defaults to false",
    ];

    /// <summary>
    /// What the <c>transcript</c> description must no longer carry: the
    /// explanation of what it is not, which the old name made necessary.
    /// </summary>
    private static readonly string[] ForbiddenTranscriptPhrases =
    [
        "Playwright trace",
        "browser_start_tracing",
        "browser_stop_tracing",
    ];

    [Test]
    public async Task EveryToolDescriptionFitsTheSameBudget()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            toolsList: UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.ToolsList);
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
        // and not `- 1`, so the day the decision is reversed this follows it.
        //
        // ⚠️ Corrected 2026-09-15 (previously `- 1`): it is two tools now --
        // `browser_annotate` and `browser_webmcp_call`, both on liveness -- and
        // the subtrahend is read off the file and not typed, so the arm
        // states a relationship and the file states the number.
        var advertisedUpstream = UpstreamSurface.SnapshotDescriptions()
            .Count(entry => !RepositoryVerdicts.Committed.IsWithheldFromTheSurface(entry.Name));

        await Assert.That(advertisedUpstream).IsEqualTo(UpstreamSurface.SnapshotToolCount() - RepositoryVerdicts.Count);
        await Assert.That(advertised.Count).IsEqualTo(SessionToolSurface.Names.Count + advertisedUpstream);
    }

    [Test]
    public async Task TheCreationToolsDescriptionCarriesTheProfileWarningAndTheRetentionPolicy()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            toolsList: UpstreamSurface.SnapshotToolsListResult());

        var description = (string?)Advertised(rig.ToolsList)[SessionToolSurface.Init]?["description"] ?? string.Empty;
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
        // draft DID NOT FIT ... the description now stands at 1,991 of 2,048 ...
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
        // red build and not a warning nobody reads.
        await Assert.That(description.Length).IsLessThanOrEqualTo(SessionToolSurface.DescriptionMaximumCharacters);
    }

    [Test]
    public async Task EveryLoadBearingUpstreamPhraseSurvivesOurRewrite()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            toolsList: UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.ToolsList);
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
                lost.Add($"{tool}: lost '{phrase}' -- either our rewrite dropped it or upstream reworded it, and both are changes nobody adjudicated");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, lost)).IsEmpty();
    }

    /// <summary>
    /// Who is responsible for deleting a session, stated in all three places a
    /// model reads before it can act on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Settled 2026-09-21, in the maintainer's words: the agent that created
    /// a session destroys it.</b> BrowserAI deletes nothing on a schedule and
    /// nothing at a size, which is a retention policy the surface already
    /// stated -- and stating it is not the same as saying whose job the other
    /// half is. A model that reads <i>"nothing here expires"</i> and stops there
    /// has been told the directory is permanent and nothing else, so a session
    /// that signed into something stays signed into it, on disk, until somebody
    /// notices.
    /// </para>
    /// <para>
    /// <b>Three placements because there are three moments.</b> The
    /// <c>instructions</c> arrive before the first call, so the short line is
    /// there; <c>browserai_init</c>'s description arrives when the model decides
    /// to make a session, which is when the obligation is incurred; and
    /// <c>browserai_destroy</c>'s arrives when it is about to be discharged.
    /// Only the first of those is cheap -- the instructions string was at
    /// <b>2,022 characters of 2,048</b> when this went in, measured off the
    /// published wire, so the line is one clause and the reasoning lives on the
    /// two descriptions where there is room for it.
    /// </para>
    /// <para>
    /// <b>Phrases and not whole sentences.</b> The wording is not the
    /// maintainer's the way the browser-installation sentence is, so a re-draft
    /// should be free; what must survive one is that something says nothing else
    /// deletes a session, that the caller is the one who does, and that a login
    /// makes it urgent.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheAgentIsToldThatDestroyingTheSessionsItMakesIsItsOwnJob()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SliceRun.SharedAsync();

        await Assert.That(MissingFromTheWire(run, RequiredDeletionResponsibilityPhrases)).IsEmpty();
    }

    /// <summary>
    /// What must go on saying whose job it is to destroy a session, and where.
    /// </summary>
    private static readonly (string Surface, string Phrase)[] RequiredDeletionResponsibilityPhrases =
    [
        // The one short line, in the channel that arrives before the first call.
        // ⚠️ Since 2026-10-04 (previously "Nothing else ever deletes a session"),
        // the lean instructions of the maintainer's rewrite b.
        ("instructions", "Nothing but browserai_destroy deletes a session"),
        ("instructions", "destroy yours when the work is done"),
        ("instructions", "promptly if it held a login"),

        // Where the obligation is incurred, beside the retention policy that
        // was already there and was only ever half of it.
        (SessionToolSurface.Init, "never deletes a session directory"),
        (SessionToolSurface.Init, "the agent that made a session destroys it"),
        (SessionToolSurface.Init, "promptly when it held a login"),

        // And where it is discharged, with the reason a login is the urgent
        // case: the cookies are in the profile and not in anything a tool
        // call put there.
        (SessionToolSurface.Destroy, "the agent that created a session destroys it"),
        (SessionToolSurface.Destroy, "cookies and logins live in the profile"),
    ];

    /// <summary>
    /// <c>browser_file_upload</c> can only reach a file inside the session's
    /// <c>output</c> folder, and the model is told so where the session
    /// directory is explained.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It is a consequence of a decision this project made, not upstream's
    /// default.</b> <c>allowUnrestrictedFileAccess</c> is written
    /// <see langword="false"/> and both of upstream's roots -- <c>outputDir</c>
    /// and the child's working directory -- are written as the same folder,
    /// <c>&lt;session&gt;\output</c>. So a caller pointing
    /// <c>browser_file_upload</c> at a file it wrote somewhere else gets
    /// upstream's <i>File access denied</i> and no explanation of why a server
    /// it did not configure is refusing.
    /// </para>
    /// <para>
    /// ⚠️ <b>It cannot go on the tool it is about.</b>
    /// <c>browser_file_upload</c> is upstream's, and every upstream description
    /// passes through this proxy byte for byte --
    /// <c>LosslessPassthroughTests</c> holds that and
    /// <see cref="EveryUpstreamDescriptionArrivesUnchangedAndTheWithheldToolDoesNotArriveAtAll"/>
    /// holds it again. The second assertion here is that half: the sentence is
    /// on <c>browserai_init</c> <i>and</i> the upstream tool is still upstream's
    /// own bytes, so the instinctive repair fails instead of passing on the
    /// half it satisfied.
    /// </para>
    /// <para>
    /// <b>On <c>browserai_init</c> and not in the <c>instructions</c>, and
    /// the budget decided that.</b> The instructions string had <b>26
    /// characters</b> of the client's 2,048 left when this went in; the sentence
    /// is beside <i>the directory IS the session</i>, which is the claim it
    /// qualifies.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheOnlyFolderAFileCanBeUploadedFromIsNamedWhereTheSessionDirectoryIs()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SliceRun.SharedAsync();
        var missing = MissingFromTheWire(run, RequiredUploadRootPhrases);
        var descriptions = DescriptionsOnTheWire(run);
        var upstream = UpstreamSurface.SnapshotDescriptions()
            .Single(tool => tool.Name == "browser_file_upload").Description;

        if (descriptions.TryGetValue("browser_file_upload", out var advertised) && advertised != upstream)
        {
            missing += $"{Environment.NewLine}browser_file_upload's description is not upstream's own bytes -- the roots sentence belongs on browserai_init, not appended to the tool";
        }

        await Assert.That(missing).IsEmpty();
    }

    /// <summary>What must go on saying where an uploadable file has to live.</summary>
    private static readonly (string Surface, string Phrase)[] RequiredUploadRootPhrases =
    [
        (SessionToolSurface.Init, "browser_file_upload"),
        (SessionToolSurface.Init, "'output' folder"),
        (SessionToolSurface.Init, "copy it in there first"),
        (SessionToolSurface.Init, "the copy goes when the session does"),
    ];

    /// <summary>
    /// Destroying a session takes everything in the directory with it, said
    /// before it runs and not reported after.
    /// </summary>
    /// <remarks>
    /// <b>The screenshots are the case worth naming.</b> A model that has spent
    /// an hour producing artifacts reads <i>deletes the whole directory</i> as a
    /// statement about the session and not about its own output, because the
    /// output is the thing it was asked for. <c>browserai_catch_up</c> already
    /// had to be called first and already reports the sizes -- this is the other
    /// half of that instruction, which is what to DO about what it reports.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task DestroyingASessionIsSaidToTakeTheScreenshotsAndDownloadsWithIt()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SliceRun.SharedAsync();

        await Assert.That(MissingFromTheWire(run, RequiredDestroyScopePhrases)).IsEmpty();
    }

    /// <summary>What must go on saying what a destroy actually removes.</summary>
    private static readonly (string Surface, string Phrase)[] RequiredDestroyScopePhrases =
    [
        (SessionToolSurface.Destroy, "screenshots and downloads included"),
        (SessionToolSurface.Destroy, "MOVE OUT WHAT MUST BE KEPT"),

        // Unchanged and asserted here so a rewrite of the paragraph around it
        // cannot quietly drop it: reading the sizes before deleting them was
        // already the instruction.
        (SessionToolSurface.Destroy, SessionToolSurface.CatchUp),

        (SessionToolSurface.Init, "screenshots and downloads included"),
    ];

    /// <summary>
    /// A session directory moves by hand, and <c>browserai_resume</c> says so --
    /// including that copying one instead duplicates its logins.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The move and copy tools were considered and deferred on 2026-09-21,
    /// in the maintainer's words: <i>"Skip the move and copy tool for now."</i></b>
    /// What that leaves is a capability the surface never mentions -- the
    /// directory is the identity, so moving it is an ordinary file operation and
    /// resume repairs the record. A model with no tool for it and no sentence
    /// about it concludes the session is pinned where it was created.
    /// </para>
    /// <para>
    /// <b>The copy half is a warning and not a capability.</b> Resume
    /// already detects a copy and says so <i>afterwards</i>; this says what it
    /// costs <i>before</i>, which is a second directory holding the same live
    /// logins with nothing tracking it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task MovingASessionDirectoryByHandIsOnTheResumeDescription()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SliceRun.SharedAsync();

        await Assert.That(MissingFromTheWire(run, RequiredMoveByHandPhrases)).IsEmpty();
    }

    /// <summary>What must go on saying that a session moves by hand.</summary>
    private static readonly (string Surface, string Phrase)[] RequiredMoveByHandPhrases =
    [
        (SessionToolSurface.Resume, "no move tool and no copy tool"),
        (SessionToolSurface.Resume, "while no browser is open on it"),
        (SessionToolSurface.Resume, "resume it at its new path"),
        (SessionToolSurface.Resume, "duplicates every login it holds"),
    ];

    /// <summary>
    /// Every required phrase that is not on the published binary's own wire,
    /// named one per line.
    /// </summary>
    /// <remarks>
    /// <b>Off the wire and not off the constants</b>, for this file's
    /// standing reason: these strings are assembled from concatenated constants
    /// and interpolated tables, and a sentence that exists in source and never
    /// reaches <c>tools/list</c> is the failure the assertion is for. A surface
    /// name that is not on the wire at all is reported as a loss instead of
    /// throwing, so one renamed tool does not hide the other rows.
    /// </remarks>
    /// <param name="run">The published slice's own <c>initialize</c> and <c>tools/list</c>.</param>
    /// <param name="required">The surface each phrase has to be on.</param>
    /// <returns>The failures, one per line, empty when there are none.</returns>
    private static string MissingFromTheWire(SliceRun run, (string Surface, string Phrase)[] required)
    {
        var descriptions = DescriptionsOnTheWire(run);
        var missing = new List<string>();

        foreach (var (surface, phrase) in required)
        {
            string? text;

            if (surface == "instructions")
            {
                text = (string?)run.InitializeResult["instructions"];
            }
            else if (!descriptions.TryGetValue(surface, out text))
            {
                missing.Add($"{surface}: not on the wire at all, so '{phrase}' cannot be checked");
                continue;
            }

            if (text?.Contains(phrase, StringComparison.Ordinal) != true)
            {
                missing.Add($"{surface}: no longer says '{phrase}'");
            }
        }

        return string.Join(Environment.NewLine, missing);
    }

    /// <summary>
    /// Every advertised tool's <c>description</c>, exactly as the published
    /// binary put it on the wire.
    /// </summary>
    /// <param name="run">The slice run to read.</param>
    /// <returns>Tool name to description.</returns>
    private static Dictionary<string, string> DescriptionsOnTheWire(SliceRun run)
    {
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var tool in run.ToolList)
        {
            if (tool?.AsObject() is { } definition && (string?)definition["name"] is { } name)
            {
                descriptions[name] = (string?)definition["description"] ?? string.Empty;
            }
        }

        return descriptions;
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
    /// all -- and it is the surface BrowserAI is most exposed on, because
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
    /// answering -- <see cref="RawStdioClient"/> holds the pipe open and flushes
    /// per frame, which is why it is the client here.)
    /// </para>
    /// <para>
    /// <b>Enumerated dynamically.</b> Nothing here names a tool or a parameter,
    /// so a tool upstream adds next year is covered without anybody editing this
    /// file. The floors below are what keep that from becoming vacuous.
    /// </para>
    /// <para>
    /// <b>Hard failure at 100%, and no warning tier -- deliberately.</b> The
    /// recorded argument against a headroom gate stands and is not contradicted
    /// here: that argument was against failing <i>below</i> 100%, because a
    /// fourth session mode should fail on the six-consumer line and not on a
    /// budget line. This fails only at the point where the client starts
    /// discarding text, which is a broken state, not a tight one.
    /// </para>
    /// <para>
    /// <b>The per-string reading is MEASURED -- see
    /// <see cref="ClientTruncationBudget"/>.</b> <i>Corrected 2026-08-18
    /// (previously "⚠️ The per-string reading is an ASSUMPTION ... the experiment
    /// that settles the reading needs the data, and a test must not pretend to
    /// have settled it").</i> The experiment ran on 2026-08-18 against Claude
    /// Code 2.1.234, reading the <c>tools</c> array the client sends to the
    /// Messages API: the cap is per string, it is <b>2,048 UTF-16 characters</b>
    /// and not bytes, and there is no per-tool and no whole-surface total.
    /// <c>browserai_init</c>'s whole entry -- 3,360 bytes as the client sends it --
    /// arrives intact, so it was never the casualty the old note feared.
    /// </para>
    /// <para>
    /// <b>The entry totals are still reported and still not asserted</b>, for a
    /// different reason than before: they are the figure that would matter if a
    /// client release ever did introduce a per-tool bucket, and a report nobody
    /// has to re-derive is what makes that re-check cheap. What <i>is</i> asserted
    /// is the measured predicate -- <c>Length &gt; 2048</c>, in characters, on
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
            // ⚠️ Serialised through Unminified and not ToJsonString(), and it
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
        // two diverge on the first em dash -- `initialize.instructions` is 2,026
        // characters and 2,036 bytes, re-measured 2026-09-21 (previously "1,261
        // ... 1,276") -- and the byte figure is the one that is
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
        // standing failure mode of a test that enumerates and does not name.
        await Assert.That(measured.Count(entry => entry.Surface is "instructions")).IsEqualTo(1);
        await Assert.That(measured.Count(entry => entry.Surface is "tool")).IsEqualTo(run.ToolNames.Count);
        await Assert.That(measured.Count(entry => entry.Surface is "parameter")).IsGreaterThan(100);

        // And every one of them is a real string and not an absent member
        // counted as zero: an empty description would satisfy the budget for
        // ever.
        await Assert.That(measured.Count(entry => entry.Gated is 0)).IsEqualTo(0);
    }

    /// <summary>
    /// Serialisation that escapes nothing it does not have to, so a measured
    /// size is the size the server actually wrote.
    /// </summary>
    /// <remarks>
    /// The default encoder turns <c>--</c> into six ASCII characters. Measuring a
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
    /// difference is visible where it is applied and not only in prose.
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
            toolsList: UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.ToolsList);
        var upstream = UpstreamSurface.SnapshotDescriptions();
        var offenders = new List<string>();

        foreach (var (name, original) in upstream)
        {
            // ⚠️ Corrected 2026-08-18 (previously every upstream tool was
            // expected in the advertised list, and one of them -- `browser_annotate`
            // -- was expected to have a sentence of ours appended). That tool is
            // now filtered out of `tools/list` entirely, so the shape of this
            // test changed with it: the withheld one must be ABSENT, and every
            // other description must be upstream's own, unchanged, to the byte.
            if (RepositoryVerdicts.Committed.IsWithheldFromTheSurface(name))
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

            // Unchanged, asserted as equality and not as a prefix. The
            // append hook was the only thing that ever made these differ and it
            // is gone (`SessionToolSurface.AppendModeNote`, deleted the same
            // day), so equality is now true and is the stronger claim: a prefix
            // check passes anything appended, which is what would come back if
            // the hook were reintroduced by habit.
            //
            // ⚠️ AND A NOTE IS APPENDED SINCE 2026-10-04, deliberately and never by
            // habit: the maintainer's rewrite b of the instructions moved the
            // advice for one tool onto that tool, declared beside its verdict in
            // tool-verdicts.json. So a tool with a note is upstream's own bytes,
            // the marker and the note -- equality, still, and against the note the
            // file declares, so nothing else can ride along -- and every other
            // tool is upstream's own bytes alone.
            var expected = RepositoryVerdicts.Committed.Find(name)?.Note is { } note
                ? original + SessionToolSurface.NoteMarker + note
                : original;

            if (!string.Equals(rewritten, expected, StringComparison.Ordinal))
            {
                offenders.Add($"{name}: the advertised description is not upstream's own, byte for byte{(expected.Length == original.Length ? string.Empty : ", followed by the note tool-verdicts.json declares")}");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();

        // Not vacuous: an `Advertised` that returned nothing would satisfy every
        // "unchanged" check above by never running one. The authored tools are
        // in that dictionary too, so the arithmetic names both halves.
        await Assert.That(advertised.Count).IsEqualTo(SessionToolSurface.Names.Count + upstream.Count - RepositoryVerdicts.Count);

        foreach (var denial in RepositoryVerdicts.TheDenials)
        {
            await Assert.That(advertised.ContainsKey(denial.Name)).IsFalse();
        }

        // And nothing of the removed matrix -- or of the withheld tool -- survives
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
            .. RepositoryVerdicts.TheDenials.Select(denial => denial.Name),
        ])
        {
            await Assert.That(everyDescription).DoesNotContain(gone);
        }
    }

    [Test]
    public async Task NoConditionalCompilationReachesTheEnforcementPath()
    {
        // A property of the artifact and not of the source: the decision a
        // released binary takes must be the decision the suite took. A `#if
        // DEBUG` here would make every test above evidence about a build nobody
        // ships.
        //
        // ⚠️ FOUR FILES SINCE 2026-08-20 (previously five, the fifth being
        // `Sessions/SessionMode.cs`). That file was DELETED with session modes
        // and this list is not allowed to shrink by accident -- the loop below
        // fails on a named file that is missing, which is exactly what it did
        // when the deletion landed. `Runtime/BrowserConfiguration.cs` takes its
        // place instead of the list simply getting shorter: it is where a
        // session's capability set is now decided, so it is on the enforcement
        // path by the same argument SessionMode.cs was.
        //
        // What these carry is routing -- `session` is mandatory and resolves to
        // one child -- the capability grant, and the single liveness refusal. All
        // three deserve the same guarantee for the same reason: a caller cannot
        // tell from the outside which build it is talking to.
        string[] enforcement =
        [
            "src/BrowserAI/Sessions/ToolVerdicts.cs",
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
        // It matters more, not less, now that only the liveness refusal is
        // left: an environment variable that turned `browser_annotate` back on
        // would hang an overnight run, and the hang is the thing this product
        // exists not to do. And since 2026-08-20 the capability GRANT is on this
        // list too -- a variable that quietly dropped `storage` from a session
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
            "src/BrowserAI/Sessions/ToolVerdicts.cs",
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

        // BrowserProxy is deliberately outside the list above and not
        // silently omitted from it. It is the enforcement *call site* and also
        // the process's own composition root, so it legitimately reads the
        // environment for things that are not the decision -- and a scan that
        // banned the read outright would either be red today or would train the
        // next person to move the decision somewhere the scan does not look.
        // What is asserted instead is that the decision it calls is the one in
        // ToolVerdicts, which the four files above are closed against.
        //
        // ⚠️ Corrected 2026-08-26 (previously `SessionToolPolicy.cs` in both
        // lists above, and `SessionToolPolicy.Decide` here). That file is
        // deleted: the decision is a verdicts FILE now, read at startup, and
        // ToolVerdicts is the type that reads it. The lists are not allowed to
        // shrink by accident -- the loop fails on a named file that is missing,
        // which is exactly what it did when this landed -- so the replacement is
        // named instead of the entry being dropped.
        var callSite = await RepositoryLayout.ReadCodeAsync(
            new FileInfo(Path.Combine(RepositoryLayout.Root.FullName, "src/BrowserAI/Proxy/BrowserProxy.cs")));

        await Assert.That(callSite).Contains("_verdicts.Decide(tool)");
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
        // Read out of the advertised surface and not off the class, for the
        // same reason the description assertions are: the rewrite is what a
        // model receives, and it is the rewrite that could lose a property.
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            toolsList: UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.ToolsList);
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

        // And the table above covers all eight, so a ninth authored tool cannot
        // arrive unasserted.
        await Assert.That(TheAuthoredSignatures.Select(signature => signature.Tool).Order(StringComparer.Ordinal))
            .IsEquivalentTo(SessionToolSurface.Names.Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Every schema in the surface says it takes nothing it does not list, which
    /// is what BrowserAI enforces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The maintainer's rule of 2026-10-03, in his words:</b> <i>"I'd expect
    /// that any call carrying any parameter or argument that we do not recognize
    /// would be refused actively with a syntax error."</i> BrowserAI refuses such a
    /// call since 2026-10-04 (<c>UnrecognisedArgumentTests</c>), so a schema that
    /// left <c>additionalProperties</c> open would tell a model it may send what
    /// the server then refuses.
    /// </para>
    /// <para>
    /// <b>Upstream's 72 already said so</b>, every one of them, in the snapshot
    /// read 2026-10-04; BrowserAI's own eight did not, which is the half this
    /// arm was planted red against. The sub-object <c>browserai_page_tool</c>
    /// hands to a page is deliberately open: its shape is the page's.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EverySchemaInTheSurfaceSaysItTakesNothingItDoesNotList()
    {
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(
            toolsList: UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.ToolsList);
        var open = advertised
            .Where(tool => tool.Value?["inputSchema"]?["additionalProperties"]?.GetValueKind() is not System.Text.Json.JsonValueKind.False)
            .Select(tool => tool.Key)
            .ToList();

        await Assert.That(advertised.Count).IsGreaterThan(SessionToolSurface.Names.Count);
        await Assert.That(string.Join(", ", open)).IsEmpty();
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
    /// <c>browserai.json</c> was a snapshot -- taking the copy over overwrote the only
    /// evidence that it <i>was</i> a copy -- and it stopped being necessary the
    /// moment the record became an append-only list of timestamped statements,
    /// because the resume can now hand the model the whole provenance instead.
    /// </para>
    /// <para>
    /// <b>The rule this asserts is a design rule, not a naming rule.</b> A
    /// confirmation flag is a question whose entire content can be returned as
    /// fact; a model that has been told what a thing is does not need to be asked
    /// whether it meant it, and a flag it must guess at is a flag it will pass
    /// <c>true</c> to. The one thing BrowserAI still refuses outright -- a
    /// reinstall while a browser is running out of the tree -- is refused with
    /// <i>no force option</i> and no argument to add one, which is the same
    /// principle from the other end.
    /// </para>
    /// <para>
    /// ⚠️ <b>The design rule gives way once, by the maintainer's decision, and the
    /// assertion still holds.</b> <i>Added 2026-10-08.</i> D2 b and F2 d: a resume that
    /// changes a session's settings, and an idle time longer than the default, are held
    /// back once and go through when the same call is sent again. His words of
    /// 2026-10-07, verbatim: <i>"What if we make all the init and resume parameters
    /// mandetory and then go withpattern b. But do make sure to communicate clearly to
    /// the llm that the first call did not work but that the second call will
    /// work."</i> No property confirms it, so nothing here changed: the same call is the
    /// confirmation, which is why the sweep below still finds no flag.
    /// <c>SettingsHoldBack</c> carries the rest.
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
            toolsList: UpstreamSurface.SnapshotToolsListResult());

        var advertised = Advertised(rig.ToolsList);
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
        var rewritten = SessionToolSurface.Rewrite(JsonNode.Parse(childToolsList)!.AsObject(), RepositoryVerdicts.Committed);

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
