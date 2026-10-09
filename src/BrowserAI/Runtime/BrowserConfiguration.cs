// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Sessions;

namespace BrowserAI.Runtime;

/// <summary>
/// The config file BrowserAI generates for one child. It never accepts one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Generating a key is not the same as the child honouring it.</b>
/// <c>loadConfig</c> is a bare <c>JSON.parse</c> with no schema validation, so a
/// renamed or removed key is discarded in silence -- <c>--output-mode</c> was a
/// no-op for its entire life and nobody noticed. Every opinion this type
/// generates is therefore listed in <see cref="GeneratedConfig.Opinions"/> and
/// asserted back out of the running child through <c>browser_get_config</c>. A
/// key we set that does not come back is a red build and not a mystery in
/// production.
/// </para>
/// <para>
/// <b>Three places where generation must not take the obvious route.</b>
/// <c>chromiumSandbox</c> is absent from this file because the config key is
/// discarded and only <c>--sandbox</c> on the command line works -- the CLI stage
/// merges last and commander defaults <c>sandbox</c> to <see langword="false"/>
/// and not to undefined, so it always overwrites what a file says.
/// <c>browserName</c> <i>and</i> an explicit chromium-alias channel are always
/// both present: omit them and <c>validateBrowserConfig</c> fills in
/// <c>chromium</c> + <c>channel: "chrome"</c>, the user's own Google Chrome, and
/// dropping the channel alone selects <c>chromium-headless-shell</c>, which is
/// never provisioned. And <c>outputMaxSize</c> is never set, because
/// <c>_enforceOutputBudget()</c> then runs on every tool response and unlinks
/// oldest-first across the whole output tree.
/// </para>
/// <para>
/// <b><c>capabilities</c> is written here and must never be passed as
/// <c>--caps</c>.</b> <c>mergeConfig</c> spreads defined overrides, so the flag
/// <i>replaces</i> this list instead of merging with it -- as does
/// <c>PLAYWRIGHT_MCP_CAPS</c>, which is why the environment is an allowlist
/// and not a strip list.
/// </para>
/// <para>
/// ⚠️ <b><c>allowUnrestrictedFileAccess</c> is written <c>false</c>
/// EXPLICITLY, and it is the only containment this product has left. Corrected
/// 2026-08-26 (previously "written <c>true</c> unconditionally, with no argument
/// to turn it off ... the maintainer's answer of 2026-08-20, asked whether it
/// should be always on, per mode or per call: <i>a always</i>").</b> That answer
/// was given while BrowserAI had a <c>filename</c> gate of its own -- a validator
/// that refused <c>..</c>, drive-relative, UNC, rooted and device paths on the
/// string, and then rewrote every surviving name into an absolute path inside
/// the session. The gate is deleted. Nothing of ours looks at a path any more,
/// so lifting upstream's guardrail on top of that would leave a caller's raw
/// string going straight to the filesystem with nothing between.
/// </para>
/// <para>
/// <b>What the key does, from upstream's own code.</b> <c>checkFile</c> refuses
/// any resolved name that is inside neither <c>outputDir</c> nor the child's
/// working directory, and <c>checkUrlAllowed</c> refuses the <c>file:</c>
/// protocol outright. BrowserAI writes both roots as the same directory --
/// <c>&lt;session&gt;\output</c> -- so the two coincide instead of overlapping,
/// and the refusal names the path and both roots.
/// </para>
/// <para>
/// <b>Written, not omitted, and the difference is the point.</b>
/// <see langword="false"/> is upstream's default, so the behaviour would be
/// identical either way -- but an omitted key says nothing about whether anybody
/// chose it, and <c>browser_get_config</c> cannot report back an opinion the
/// file does not carry. <c>ConfigRoundTripTests</c> fails an absent key exactly
/// as it fails a <see langword="true"/> one.
/// </para>
/// <para>
/// <b>What it costs, stated, not discovered.</b>
/// <c>browser_file_upload</c> can no longer reach a file outside the session's
/// output directory, and <c>browser_navigate</c> cannot open a <c>file:</c> URL
/// at all. Upstream calls the key a convenience defence and not a secure
/// boundary, in <c>config.d.ts</c>'s own words -- <i>"a guardrail to prevent the
/// LLM from accidentally wandering outside its intended workspace ... not a secure
/// boundary; a deliberate attempt to reach other directories can be easily
/// worked around"</i> -- and that is exactly what this product wants it for.
/// Hostile-caller defence is an explicit non-goal; steering an honest mistake is
/// the whole job, and upstream's roots do it in the one place a mistake actually
/// reaches a file.
/// </para>
/// </remarks>
internal static class BrowserConfiguration
{
    /// <summary>The default browser family. Never left to upstream's default, which is Chrome.</summary>
    public const string BrowserName = ProvisionedBrowsers.Chromium;

    /// <summary>The extension of a captured HTTP Archive.</summary>
    /// <remarks>
    /// ⚠️ <b><c>HarFolder</c> is deleted, 2026-08-26 (previously
    /// <c>"network"</c>, "which is already where BrowserAI's filename routing
    /// files anything a <c>network-</c> prefixed tool produces -- so the archive
    /// sits beside the request and response bodies it duplicates , not in
    /// a folder of its own").</b> There is no filename routing and there is no
    /// <c>output\network\</c>: the output directory is flat, and the archive
    /// lands at its root beside everything else the session writes. The name
    /// still carries a timestamp, and that half is unchanged and load-bearing --
    /// see <see cref="ForSession"/>.
    /// </remarks>
    public const string HarExtension = ".har";

    /// <summary>
    /// The chromium-alias channel, spelled as upstream's <c>chromiumAliases</c>
    /// spells it. Never <c>chrome</c>, which is the user's Google Chrome, and
    /// never absent, which selects the headless shell.
    /// </summary>
    /// <remarks>
    /// <b>Chromium only, and writing it for another family would be worse than
    /// useless.</b> <c>channel</c> is a Chromium concept -- <c>chromiumAliases</c>
    /// has no Firefox member -- and upstream's own <c>validateBrowserConfig</c>
    /// drops the key for a non-chromium <c>browserName</c>, so a channel written
    /// beside <c>firefox</c> would be an opinion that never arrives and a round
    /// trip that can never pass.
    /// </remarks>
    public const string Channel = "chrome-for-testing";

    /// <summary>
    /// The console level every session's child is launched with, and there is no
    /// argument to change it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Changed 2026-08-20 (previously <c>DefaultConsoleLevel = "info"</c>,
    /// upstream's own default, with a <c>consoleLevel</c> argument on
    /// <c>browserai_init</c> and <c>browserai_resume</c> offering all four
    /// levels).</b> The argument is deleted and the level is <c>debug</c>
    /// always.
    /// </para>
    /// <para>
    /// <b>Because the knob cost almost nothing to turn off and almost nothing to
    /// leave on.</b> Measured: moving from <c>error</c> to <c>debug</c> costs
    /// <b>+1 character</b> on a navigation response and <b>+5</b> otherwise,
    /// because the events line in a tool response is a <i>pointer</i> --
    /// <c>path#L1-L20</c> -- and never the message text. The whole cost of the
    /// most verbose setting is the width of a larger line number.
    /// </para>
    /// <para>
    /// <b>And the read-level knob already exists, one layer up.</b>
    /// <c>browser_console_messages</c> takes its own level, so a caller that
    /// wants only errors asks for only errors -- at the moment it asks, and
    /// not having had to decide at <c>init</c> and discovered the loss
    /// afterwards. A capture level chosen hours earlier cannot be raised
    /// retroactively; a read level can always be lowered.
    /// </para>
    /// </remarks>
    public const string ConsoleLevel = "debug";

    /// <summary>
    /// The code-generation language, hard-coded to none.
    /// </summary>
    /// <remarks>
    /// <b>It strips a <c>### Ran Playwright code</c> block from every response,
    /// for a feature this product does not have.</b> Upstream emits the
    /// equivalent Playwright source beside each result so a caller can build a
    /// test out of a session; BrowserAI ships no test recorder, no
    /// <c>browser_start_codegen</c> and nothing that reads the block, so it is
    /// tokens spent on every single call for something no reader exists for.
    /// There is deliberately no argument: an option nobody can act on is a
    /// second state to test.
    /// </remarks>
    public const string Codegen = "none";

    /// <summary>
    /// How the child renders a file path back to a caller -- <b>absolute</b>,
    /// where upstream's default is <c>relative</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The fix to this project's own upstream ask</b>,
    /// <see href="https://github.com/microsoft/playwright/issues/42497">microsoft/playwright#42497</see>,
    /// merged as
    /// <see href="https://github.com/microsoft/playwright/pull/42673">#42673</see>
    /// on 2026-09-16 and reached by a
    /// <see href="https://github.com/SixFive7/BrowserAI/blob/master/DECISIONS.md">dated
    /// <c>playwright-core</c> override</see> and not by an
    /// <c>@playwright/mcp</c> roll.
    /// </para>
    /// <para>
    /// <b>A relative pointer resolves against the child's working directory, and
    /// the reader of a tool result is a model, not a process.</b> Every
    /// artifact the child names -- the screenshot, PDF and storage-state links,
    /// the snapshot link, the console log link, the download line, a binary
    /// response body and the trace files -- arrived as
    /// <c>output\page-....png</c>, which names nothing a caller can open.
    /// BrowserAI used to answer that with a note of its own naming each artifact
    /// absolutely; that note went with artifact routing on 2026-08-26, and from
    /// then until this key those pointers reached a model unaccompanied.
    /// </para>
    /// <para>
    /// <b>Written, not omitted</b>, like <see cref="Codegen"/>,
    /// <c>allowUnrestrictedFileAccess</c> and <c>timeouts.idle</c>: upstream's
    /// default is the opposite of what this product wants, so an omission would
    /// be a silent revert and not a stance, and
    /// <c>browser_get_config</c> cannot read back a key the file never carried.
    /// <c>RequiredSessionOpinions</c> names it so a generator that drops it is a
    /// red build.
    /// </para>
    /// <para>
    /// ⚠️ <b>It is in <c>config.d.ts</c> since <c>@playwright/mcp</c>
    /// 0.0.82</b>, as <c>filePaths?: 'relative' | 'absolute'</c>. <i>Corrected
    /// 2026-09-21 (previously "It is not in <c>config.d.ts</c>. The typings ship
    /// with <c>@playwright/mcp</c>, which has not rolled; the implementation is
    /// in <c>playwright-core</c>, which the override moved.")</i> -- the wrapper
    /// rolled on 2026-09-21 and the dated override was retired with it.
    /// <b>Nothing about how this is checked changes, and the reason is the
    /// point:</b> <c>loadConfig</c> is a bare <c>JSON.parse</c> with no schema
    /// validation, so a declaration in the typings is not evidence that a
    /// running child honours the key any more than its absence was evidence that
    /// it does not. The honouring is measured over a running child by
    /// <c>ConfigRoundTripTests.TheChildHonoursFilePathsAndHandsBackAbsoluteWhereUpstreamDefaultsToRelative</c>
    /// and not assumed.
    /// </para>
    /// </remarks>
    public const string FilePaths = "absolute";

    /// <summary>
    /// Upstream's own idle timeout, in milliseconds -- <b>one hour</b>, written
    /// and not omitted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>No launch writes it since 2026-10-08</b>, E2 and F2: every launch writes
    /// <see cref="NoIdleTimeout"/>, because an agent may set a session's idle time past
    /// this hour or to never, and every call that names the session restarts
    /// BrowserAI's countdown where upstream's is restarted only by a call it receives.
    /// The value and its reasoning are kept below as the record of what upstream's
    /// default is.
    /// </para>
    /// <para>
    /// ⚠️ <b>It cannot fire, and that is why it is written down.</b>
    /// <see cref="Sessions.BrowserIdleTimer.DefaultIdlePeriod"/> is ten minutes
    /// and both timers are reset by the same event -- a tool call -- so BrowserAI's
    /// closes the browser six times over before upstream's deadline is reached.
    /// A key that changes nothing is exactly the kind an omission hides: with it
    /// absent, <c>browser_get_config</c> reads back nothing, the round-trip test
    /// has no leaf to follow, and the day upstream moves its default from an hour
    /// to a minute the change arrives here as behaviour nobody chose. Written, it
    /// is a leaf in <see cref="GeneratedConfig.Opinions"/>, asserted out of the
    /// running child, and a moved default is a red build.
    /// </para>
    /// <para>
    /// <b>The value is upstream's default and is deliberately not ours.</b>
    /// Judged 2026-09-15 at <c>@playwright/mcp</c> 0.0.81, where the key arrived:
    /// <i>"Defaults to one hour for headless browsers Playwright launched, and to
    /// no timeout for headed or attached ones."</i> Writing it makes the headed
    /// case carry an hour it would not otherwise have -- which is still
    /// unreachable behind ten minutes, and is the price of one value and not
    /// a second code path keyed on headedness.
    /// </para>
    /// <para>
    /// ⚠️ <b>What <c>config.d.ts</c> says about it is about a different
    /// program.</b> Its <i>"The CLI shuts the whole session down instead of
    /// relaunching"</i> belongs to the <c>playwright-cli</c> daemon, which this
    /// product does not run. Measured 2026-09-15 on the MCP stdio path BrowserAI
    /// really uses, with <c>--idle-timeout 4000</c>: the action is
    /// <c>browser.close()</c>, the child was still alive after twice the timeout,
    /// and the next tool call answered normally in 354 ms. So the alarming
    /// sentence is not a reason to omit the key.
    /// </para>
    /// <para>
    /// ⚠️ <b>Headless launches only since 2026-10-03; a headed launch writes
    /// <see cref="NoIdleTimeout"/>.</b> <i>Corrected 2026-10-03 (previously
    /// "Writing it makes the headed case carry an hour it would not otherwise
    /// have -- which is still unreachable behind ten minutes").</i> Q326 a stops
    /// BrowserAI's own timer for a headed session, so the hour became the one
    /// timer left, and it would close a window a person is using after an hour
    /// without a call. See <see cref="NoIdleTimeout"/>.
    /// </para>
    /// </remarks>
    public const int IdleTimeoutMilliseconds = SessionTimes.UpstreamIdleTimeoutMilliseconds;

    /// <summary>
    /// What every launch writes for upstream's idle timeout since 2026-10-08, and a
    /// <b>headed</b> one since 2026-10-03: zero, which upstream reads as no idle close
    /// at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q326 a, the maintainer's words of 2026-10-03, verbatim:</b> <i>"Q326 a -
    /// the timer is there to conserve system resources the user cannot see.
    /// Also, interactive windows mostly hold user state so they are super
    /// valuable."</i> BrowserAI never arms its own timer for a headed session, and
    /// this is the other half: the hour written into every launch until today
    /// would otherwise close the window after an hour without a call.
    /// </para>
    /// <para>
    /// <b>Zero and not an omitted key</b>, for the reason
    /// <see cref="IdleTimeoutMilliseconds"/> is written at all: an omission
    /// records no decision and <c>browser_get_config</c> cannot read it back.
    /// Upstream's own default for a headed launch is the same answer, so this
    /// changes nothing upstream would not have done. Read 2026-10-03 at
    /// <c>@playwright/mcp</c> 0.0.83 / <c>playwright-core</c>
    /// 1.64.0-alpha-1790635538000, <c>coreBundle.js</c> lines 75347-75348:
    /// <c>config.timeouts?.idle ?? (...)</c> and then <c>if (idleTimeout)</c>,
    /// so zero arms nothing (lines 74767-74768 at 0.0.82). Read, not run.
    /// </para>
    /// </remarks>
    public const int NoIdleTimeout = 0;

    /// <summary>
    /// The Chromium switch that reopens a profile's last session at launch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>P1, the maintainer's words of 2026-10-03, verbatim:</b> <i>"Restore as
    /// much as possible without adding lot's of complexity. So basically, use
    /// whatever playwright offers in capabilities."</i> This is the browser's own
    /// session restore, reached through a launch option, so BrowserAI drives
    /// nothing and replays nothing.
    /// </para>
    /// <para>
    /// <b>Measured 2026-10-03 at <c>@playwright/mcp</c> 0.0.82 and 0.0.83,
    /// chromium 1246 and 1247</b>, with <see cref="DefaultPageArgument"/> ignored
    /// beside it: across a clean close and across a new child, the tabs came back
    /// with their history, <c>sessionStorage</c>, typed text, scroll position and
    /// session cookies, 24 of 24 and 12 of 12 across children. What does not come
    /// back: refs from earlier snapshots, which tab was selected, and a page that
    /// was a form POST, which Chromium reopens as <c>chrome-error://chromewebdata/</c>.
    /// With nothing to restore, Chromium starts on <c>chrome://new-tab-page/</c>
    /// (<see href="../../../docs/evidence/2026-10-03-state-across-close/results/P-caller-close-teardown-resume.txt">evidence</see>,
    /// and the <see href="../../../kb/playwright/provisioning-and-timings.md#what-a-session-keeps-across-a-browser-close-and-what-brings-the-rest-back----measured-2026-10-03">kb entry</see> for the rest).
    /// </para>
    /// </remarks>
    public const string RestoreLastSessionSwitch = "--restore-last-session";

    /// <summary>
    /// The Chromium switch that flushes <c>localStorage</c> to disk within about a
    /// second, with no rate limit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>e1 of P7, decided by the root session 2026-10-03 for the maintainer's
    /// review</b>, against his words: <i>"b + e and if e is impossible or
    /// difficult c. But it all needs to be done in a super safe way so we don't
    /// permanently leak stuff."</i> A client that ends BrowserAI kills its
    /// browsers, so what matters is how long a write needs on disk before a kill
    /// cannot take it.
    /// </para>
    /// <para>
    /// <b>Measured 2026-10-03 at chromium 1246 (154.0.8037.0), through
    /// <c>playwright-core</c> directly, killing the job a set time after a
    /// write.</b> Without the switch <c>localStorage</c> survived from about 5 s,
    /// and a second batch written ten seconds later only from about 55 s, under
    /// Chromium's commit rate limit; with it, both from about 1 s. Cookies are
    /// unchanged by it: about 30 s, a fixed interval in Chromium's code
    /// (<see href="../../../kb/playwright/provisioning-and-timings.md#how-old-a-write-must-be-before-a-hard-kill-keeps-it----measured-2026-10-03">kb</see>).
    /// </para>
    /// </remarks>
    public const string AggressiveDomStorageFlushingSwitch = "--enable-aggressive-domstorage-flushing";

    /// <summary>
    /// The Chromium switch that lets a browser restore its last session after it was
    /// killed and not closed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q376, decided 2026-10-04 by the maintainer, in his words verbatim:
    /// <i>"Q376 a"</i></b>: the switch on every Chromium launch, and no guard of
    /// BrowserAI's own against a page that crashes the browser at every launch.
    /// </para>
    /// <para>
    /// <b>Why it is needed.</b> A browser that is killed leaves its profile's
    /// <c>exit_type</c> at <c>Crashed</c>, and Chromium does not restore on the launch
    /// after an unclean exit, so <see cref="RestoreLastSessionSwitch"/> alone brought
    /// nothing back after a hard kill: 0 of 27 runs, 21 of them with the tabs on disk,
    /// measured 2026-10-03 at <c>chromium-1247</c>. With this switch the relaunch
    /// restored the tabs in all 11 runs that had them on disk, and the session after it
    /// saved its tabs again, where without it the session file was not written until
    /// the crash was acknowledged
    /// (<see href="../../../kb/playwright/provisioning-and-timings.md#committing-to-disk-sooner-and-session-restore-after-a-hard-kill----measured-2026-10-03">kb</see>).
    /// A client's kill, the coordinator or the session host ending, and a close that
    /// ran out its cap all leave a browser in that state. The switch's own description
    /// is about ChromeOS; the check that reads it, <c>HasPendingUncleanExit</c>, has no
    /// platform condition.
    /// </para>
    /// <para>
    /// <b>What it gives up.</b> Chromium skips the restore after a crash so that a page
    /// which crashes the browser does not crash it again at every launch, and the
    /// switch turns the skip off. Whether such a page would loop was deliberately not
    /// provoked (<see href="../../../kb/not-established.md">kb</see>), and the decision
    /// took the switch without a guard.
    /// </para>
    /// </remarks>
    public const string HideCrashRestoreBubbleSwitch = "--hide-crash-restore-bubble";

    /// <summary>
    /// The argument Playwright adds to open one blank page at launch, which every
    /// session launch tells it to leave out.
    /// </summary>
    /// <remarks>
    /// <b>Without this the restore piles tabs up</b>: the browser reopens the last
    /// session and Playwright adds its own blank page beside it, so every close and
    /// resume adds one more. Measured 2026-10-03 with
    /// <see cref="RestoreLastSessionSwitch"/> and the Firefox session-store
    /// preferences, both families: the variants that kept the blank page grew by
    /// one tab per cycle and the ones that dropped it did not.
    /// </remarks>
    public const string DefaultPageArgument = "about:blank";

    /// <summary>
    /// The Firefox preferences that reopen a profile's last session at launch,
    /// eagerly and all at once.
    /// </summary>
    /// <remarks>
    /// <b>Firefox's half of <see cref="RestoreLastSessionSwitch"/>, measured the
    /// same day in the same rig at firefox 1549 and 1553.</b> The first resumes
    /// the last session once, and since every launch writes it again, the restore
    /// held across every cycle the rig ran. The other two stop Firefox restoring
    /// a tab only when it is selected, which would leave every tab but one empty
    /// until something clicked it. A page that was a form POST is fetched again
    /// with a GET.
    /// </remarks>
    public static IReadOnlyList<KeyValuePair<string, bool>> FirefoxSessionRestorePreferences { get; } =
    [
        new("browser.sessionstore.resume_session_once", true),
        new("browser.sessionstore.restore_on_demand", false),
        new("browser.sessionstore.restore_tabs_lazily", false),
    ];

    /// <summary>
    /// The permissions every context is granted, hard-coded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>clipboard-read</c>, and nothing else.</b> Measured: the provisioned
    /// Chromium already grants <c>clipboard-write</c> to a page without asking,
    /// so naming it would be an opinion with no effect; <c>clipboard-read</c> is
    /// the one that prompts, and a prompt in a headless browser is a call that
    /// silently does nothing.
    /// </para>
    /// <para>
    /// ⚠️ <b>CHROMIUM ONLY, and this is measured, not assumed.</b>
    /// Firefox does not know the permission at all: a context created with it
    /// fails at <c>initializeServer</c> with <c>Unknown permission:
    /// clipboard-read</c>, and the browser exits -- so writing it for both
    /// families would have made <b>every Firefox session unusable</b>, not
    /// degraded. Measured 2026-08-20 against the provisioned
    /// <c>firefox-1539</c>, by writing it for both families and watching
    /// <c>FirefoxSessionTests</c> go red on a real front-door navigation. It is
    /// therefore family-scoped exactly as <see cref="Channel"/> is, and
    /// <see cref="RequiredSessionOpinions"/> requires it for Chromium only.
    /// </para>
    /// <para>
    /// <b>Nothing else is granted, and that is the decision.</b> Geolocation,
    /// notifications, camera and microphone all change what a page can do about
    /// the machine and not about the page, and none of them is needed to read
    /// or drive one. A caller that needs one should have to ask for it, and
    /// nothing here offers a way -- which is a limitation stated and not a
    /// gap discovered.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Permissions { get; } = ["clipboard-read"];

    /// <summary>
    /// The key that lifts upstream's workspace guardrail, spelled once so the
    /// generator and the round trip cannot disagree about it.
    /// </summary>
    /// <remarks>
    /// It is a top-level key and not one under <c>browser</c>, which is how
    /// upstream's <c>config.d.ts</c> declares it and how <c>checkFile</c> and
    /// <c>checkUrlAllowed</c> read it. The reasoning for setting it at all is on
    /// <see cref="BrowserConfiguration"/> itself.
    /// </remarks>
    public const string AllowUnrestrictedFileAccessKey = "allowUnrestrictedFileAccess";

    /// <summary>
    /// The capabilities every session gets -- <b>all of them</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The MCP spec forbids the tool set varying per connection, and SEP-2567
    /// removed protocol-level sessions outright, so there is one static tool
    /// list and every session's child has to be able to answer all of it.
    /// </para>
    /// <para>
    /// ⚠️ <b>Changed 2026-08-20 (previously two lists -- <c>BaseCapabilities</c>
    /// of <c>config</c>, <c>vision</c> and <c>devtools</c>, and
    /// <c>UnionCapabilities</c> which added <c>storage</c>; which of the two a
    /// session got was decided by its mode's <c>Storage</c> flag).</b> Session
    /// modes are gone, so there is no longer anything to decide <i>between</i>,
    /// and the honest list is the whole of what upstream offers. Three
    /// capabilities are granted here that no BrowserAI session has ever carried:
    /// <c>network</c> (4 tools), <c>pdf</c> (1) and <c>testing</c> (5). See
    /// <see cref="Sessions.SessionToolSurface"/> for what those ten are and why
    /// granting them is a decision and not a consequence.
    /// </para>
    /// <para>
    /// <c>config</c> is what makes <c>browser_get_config</c> callable, and that
    /// tool is the only thing that can prove the rest of this file reached the
    /// child. The base 24 tools are unconditional -- upstream ors
    /// <c>capability.startsWith("core")</c> with whatever is configured -- so
    /// naming a <c>core*</c> capability here would do nothing, and
    /// <c>core-install</c> carries no tool at all.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> GrantedCapabilities { get; } =
        ["config", "vision", "devtools", "storage", "network", "pdf", "testing"];

    /// <summary>
    /// The default viewport, in CSS pixels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>1920×1080, and the number that decided it is the token cost of a
    /// screenshot.</b> Measured end to end through BrowserAI: a full-page
    /// screenshot at this size arrives as <b>2,691 visual tokens</b>; 1280×720
    /// is 1,196 and 2560×1440 is <b>4,784 -- exactly the API's per-image cap,
    /// with zero headroom</b>. So the largest size that is not on the edge of a
    /// hard limit is this one, and it is also the one a page's own desktop
    /// layout is designed for.
    /// </para>
    /// <para>
    /// ⚠️ <b>What arrives is what is set, unscaled.</b> A caller that asks for
    /// 2560×1440 gets 2560×1440 worth of tokens and not something downscaled
    /// on the way out. The argument exists and the description says what it
    /// costs. <i>Corrected 2026-09-15 (previously "... and that is specific to
    /// this product. Upstream's <c>scaleImageToFitMessage</c> never runs here --
    /// BrowserAI's image handling diverges before it -- so ...").</i> <b>The
    /// conclusion is unchanged and the reason for it is gone</b>: it was specific
    /// to this product while upstream had a scaler BrowserAI diverged before, and
    /// <c>playwright-core</c> 1.63.0-alpha-2026-08-31 deleted
    /// <c>scaleImageToFitMessage</c> outright -- so nothing downscales an image
    /// anywhere in the path, for anybody, and this is no longer a divergence at
    /// all. Measured 2026-09-14 through a raw child: the inline block is
    /// byte-identical to the file at every viewport tested.
    /// </para>
    /// </remarks>
    public static ViewportSize DefaultViewport { get; } = new(1920, 1080);

    /// <summary>
    /// The host machine's locale, as a BCP-47 tag.
    /// </summary>
    /// <remarks>
    /// <b>Read, not hard-coded, because a hard-coded one is a lie about
    /// the machine.</b> Upstream leaves <c>locale</c> unset, which gives the
    /// browser's own default -- for the provisioned Chromium that is
    /// <c>en-US</c> whatever the machine is, so a site that localises by
    /// <c>Accept-Language</c> shows an agent something a person at the same
    /// desk would never see. The argument overrides it for a caller who is
    /// deliberately testing another market.
    /// </remarks>
    public static string HostLocale { get; } = CultureInfo.CurrentCulture.Name is { Length: > 0 } name
        ? name
        : "en-US";

    /// <summary>
    /// The host machine's time zone as an IANA identifier, or
    /// <see langword="null"/> when Windows's own identifier cannot be converted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>IANA, because that is what Playwright accepts.</b>
    /// <c>TimeZoneInfo.Local.Id</c> on Windows is a Windows identifier --
    /// <c>W. Europe Standard Time</c> -- and passing one to
    /// <c>contextOptions.timezoneId</c> is rejected by the browser at context
    /// creation, so the conversion is the whole of the work.
    /// </para>
    /// <para>
    /// <b>Null and not a guess when the conversion fails.</b> The mapping
    /// comes from ICU, which a globalization-invariant build does not carry, and
    /// a Windows identifier written into the config would fail the launch and
    /// not degrade. An absent key is upstream's own default, which is the
    /// machine's UTC offset -- imperfect, and not a failure.
    /// </para>
    /// </remarks>
    public static string? HostTimeZone { get; } =
        TimeZoneInfo.Local.HasIanaId
            ? TimeZoneInfo.Local.Id
            : TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : null;

    /// <summary>
    /// The keys that must survive into the child for a session to be the session
    /// it was asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named, not derived, and that is the point: derived from the
    /// generator, this list would shrink in step with a deleted key and the round
    /// trip would stay green while the opinion vanished. Written down, deleting a
    /// key turns the suite red -- planted and reverted 2026-08-16, and the failure
    /// names the key.
    /// </para>
    /// <para>
    /// <b>Per family, because the two families require different keys and a
    /// union would assert a key that cannot exist.</b> Chromium requires the
    /// channel; Firefox has none and requires the restart-registration
    /// preference instead, which is the only thing standing between a Windows
    /// update and a resurrected browser no session claims.
    /// </para>
    /// <para>
    /// ⚠️ <b>The Firefox row's dotted path is ambiguous on purpose, and a reader
    /// following it key by key will not find it.</b> The preference's <i>name</i>
    /// contains dots, so <c>browser.launchOptions.firefoxUserPrefs.toolkit.
    /// winRegisterApplicationRestart</c> is four keys and a two-part leaf and
    /// not six keys. That is what the flattener produces and what a
    /// set-membership check compares against; anything that <i>walks</i> a
    /// generated config by splitting on dots has to special-case it.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The Chromium switches BrowserAI writes, and it is a pair for one reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The first one is the whole point.</b> <c>--enable-automation</c> is what
    /// stops Chromium offering to save a password, and it was measured both ways
    /// 2026-09-23 @ chromium 1246 (154.0.8037.0), headless, through a real
    /// generated config and a local login form that posts to itself: with the
    /// switch absent the POST produced **two new top-level windows** and the
    /// prompt; with it present, **zero**. An agent cannot answer a modal it did
    /// not ask for.
    /// </para>
    /// <para>
    /// ⚠️ <b>THE SECOND ONE IS HERE BECAUSE OMITTING IT WOULD DELETE
    /// UPSTREAM'S OWN.</b> <c>@playwright/mcp</c> appends
    /// <c>--disable-blink-features=AutomationControlled</c> to the Chromium
    /// command line unless the caller already passes an argument containing
    /// <c>--disable-blink-features</c> -- and any argument containing it,
    /// whatever its value, silences that append. So writing <c>args</c> at all
    /// without this entry would silently drop a switch upstream chose, which is a
    /// change to the browser's fingerprint made by accident. Writing it
    /// explicitly is how the launch comes out identical either way.
    /// </para>
    /// <para>
    /// <b>The round trip compares the array as ONE opinion</b>, which is why
    /// <see cref="RequiredSessionOpinions"/> names
    /// <c>browser.launchOptions.args</c> and not a path per element: the
    /// flattener produces one key for the array, and an element order that moved
    /// would be a different value under the same key.
    /// </para>
    /// <para>
    /// <b>What this is NOT.</b> It is not a fingerprint change: <b>43 of 43
    /// page-visible properties are identical</b> between a launch with
    /// <c>--enable-automation</c> and one without, measured 2026-09-24.
    /// <i>Corrected 2026-09-24 (previously "<c>navigator.webdriver</c> already read
    /// <c>true</c> at chromium 1246 in every arm, including a plain launch with
    /// neither switch").</i> That reading came from raw <c>playwright-core</c>
    /// launches without the blink switch; through this product's config the flag
    /// reads <c>false</c>, because Playwright's <c>--remote-debugging-pipe</c>
    /// enables Chromium's <c>EnableAutomationControlled</c> feature and the blink
    /// switch is applied later and wins.
    /// And <c>profile.password_manager_enabled</c> is a DEAD KEY at Chromium 154
    /// -- seeded <c>false</c> into the profile it survives the launch unread and
    /// the prompt still appears
    /// ([kb](../../../kb/playwright/configuration.md#the-password-save-prompt-and-what-actually-suppresses-it----measured-2026-09-23)).
    /// </para>
    /// <para>
    /// ⚠️ <b>Four since 2026-10-03 (previously the pair above).</b>
    /// <see cref="RestoreLastSessionSwitch"/> is the browser's own session
    /// restore and <see cref="AggressiveDomStorageFlushingSwitch"/> shortens how
    /// long a <c>localStorage</c> write needs on disk; see each constant for what
    /// was measured.
    /// </para>
    /// <para>
    /// ⚠️ <b>Five since 2026-10-04 (previously four), Q376 a.</b>
    /// <see cref="HideCrashRestoreBubbleSwitch"/> lets the restore work after a
    /// browser was killed and not closed.
    /// </para>
    /// <para>
    /// ⚠️ <b>And a sixth on a hidden launch since 2026-10-04, 6 b</b>, written after
    /// these five and not one of them: <c>--user-agent=</c> with the user agent a
    /// headed Chromium of the same build sends. See
    /// <see cref="BrowserConfigurationRequest.UserAgent"/>.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> ChromiumArguments { get; } =
    [
        "--enable-automation",
        "--disable-blink-features=AutomationControlled",
        RestoreLastSessionSwitch,
        AggressiveDomStorageFlushingSwitch,
        HideCrashRestoreBubbleSwitch,
    ];

    /// <param name="browser">The family, as upstream names it.</param>
    /// <returns>The keys that family's config must carry.</returns>
    public static IReadOnlyList<string> RequiredSessionOpinions(string browser) =>
    [
        "browser.browserName",
        "browser.userDataDir",
        .. IsFirefox(browser)
            ? new[]
            {
                $"browser.launchOptions.firefoxUserPrefs.{FirefoxProfile.RestartRegistrationPreference}",
                $"browser.launchOptions.firefoxUserPrefs.{FirefoxProfile.RememberSignonsPreference}",
            }
                .Concat(FirefoxSessionRestorePreferences.Select(preference => $"browser.launchOptions.firefoxUserPrefs.{preference.Key}"))
            : ["browser.launchOptions.channel", "browser.launchOptions.args"],

        // Added 2026-10-03 with the session restore: without it the restore piles
        // up a blank tab per cycle, so a generator that dropped it would degrade
        // every resume and fail nothing else.
        "browser.launchOptions.ignoreDefaultArgs",
        "browser.launchOptions.headless",
        "browser.launchOptions.downloadsPath",
        "browser.contextOptions.viewport.width",
        "browser.contextOptions.viewport.height",
        "browser.contextOptions.locale",
        "browser.contextOptions.ignoreHTTPSErrors",

        // Chromium only: Firefox rejects `clipboard-read` at context creation
        // and exits. See `Permissions`.
        .. IsFirefox(browser) ? [] : new[] { "browser.contextOptions.permissions" },

        // ⚠️ CONDITIONAL, AND IT IS A PROPERTY OF THE MACHINE AND NOT OF THE
        // SESSION. The Windows-to-IANA mapping comes from ICU; a
        // globalization-invariant host has none, and writing a Windows
        // identifier would fail the launch and not degrade. So the key is
        // absent there, and requiring it unconditionally would make the round
        // trip red on a machine where the product is behaving correctly.
        .. HostTimeZone is null ? [] : new[] { "browser.contextOptions.timezoneId" },

        "capabilities",
        "outputDir",
        "saveSession",
        AllowUnrestrictedFileAccessKey,
        "console.level",
        "snapshot.boxes",
        "codegen",

        // Added 2026-09-15 with the key itself. It is required here for the
        // reason the key is written at all: the generator dropping it would
        // otherwise remove it from both sides of the round trip and leave that
        // comparison green.
        "timeouts.idle",

        // Added 2026-09-17 with the key itself, for the same reason -- and this
        // one has a second: `relative` is upstream's DEFAULT, so a generator
        // that stopped writing this key would not fail, it would silently go
        // back to handing a model paths it cannot resolve. That is the exact
        // failure the key was adopted to end, and it would leave every other
        // assertion green.
        "filePaths",

        // Added 2026-09-21 with the key itself. `true` is upstream's default, so
        // this is the `allowUnrestrictedFileAccess` and `timeouts.idle` argument
        // again -- an omission records no decision and `browser_get_config`
        // cannot read back a key the file never carried -- and it has one more
        // of its own: what this key switches on is the only channel through which
        // a model learns a page offers tools at all, so a generator that stopped
        // writing it would take `browserai_page_tool`'s whole discovery surface
        // away and nothing else would fail.
        "webmcp",
    ];

    /// <summary>The config one session's child is started with.</summary>
    /// <param name="session">The session directory, which is where every path below lives.</param>
    /// <param name="headed">
    /// Whether a browser window appears. ⚠️ <b>A per-run argument since
    /// 2026-08-20 (previously <c>SessionModeDefinition mode</c>, whose
    /// <c>Headed</c> flag was bound at <c>init</c> and permanent for the
    /// directory's life).</b> Headedness is a property of <i>this launch</i>, and a
    /// caller that wants to watch a session it created headless should not have
    /// to destroy it first. ⚠️ <i>Corrected 2026-10-08 (previously "it changes
    /// nothing on disk, so nothing is served by recording it")</i>: F2 d records
    /// every setting a run used, this one included, so a resume can say what it
    /// would change; it is still never read back into a launch.
    /// </param>
    /// <param name="browser">
    /// The family this session was created for, read from its own
    /// <c>browserai.data</c> and not assumed. A profile belongs to the browser
    /// that made it, so generating a Chromium config for a session recorded as
    /// Firefox would point one browser at the other's profile -- which upstream
    /// would launch, and which nothing would report.
    /// </param>
    /// <param name="transcript">Whether upstream writes <c>session.md</c> into the output directory.</param>
    /// <param name="run">The per-run arguments a caller gave for this launch.</param>
    /// <param name="hiddenUserAgent">
    /// The user agent a headed Chromium of this build sends, from
    /// <see cref="HeadedUserAgent"/>, or <see langword="null"/> when it could not be
    /// had. Written only on a hidden Chromium launch; see
    /// <see cref="BrowserConfigurationRequest.UserAgent"/>.
    /// </param>
    /// <returns>The bytes to write, and every opinion they carry.</returns>
    /// <remarks>
    /// <para>
    /// ⚠️ <b><c>tracing</c> maps to upstream's <c>saveSession</c>, because there
    /// is nothing else left to map it to.</b> Measured 2026-08-16 against
    /// <c>@playwright/mcp</c> 0.0.79: neither the CLI surface nor
    /// <c>config.d.ts</c> carries a trace option at all -- <c>tracesDir</c> is
    /// computed internally as <c>&lt;outputDir&gt;/traces</c> and is not
    /// configurable -- so BrowserAI's own <c>tracing</c> modifier has no upstream
    /// trace key to reach
    /// ([kb](../../../kb/playwright/configuration.md#defaults-that-are-not-what-they-look-like)).
    /// <c>saveSession</c> is the surviving
    /// feature with the same purpose: it records what the session did into the
    /// output directory.
    /// </para>
    /// <para>
    /// ⚠️ <b>Renamed <c>transcript</c> on 2026-10-04, argument and parameter
    /// both</b> -- Q371 c, the maintainer's words of 2026-10-03 verbatim: <i>"I like
    /// option c and the rename to transcript."</i> The mapping above is unchanged;
    /// what changed is the name, because <c>saveSession</c> writes a Markdown
    /// transcript of the run's calls and a model reading <c>tracing</c> expected a
    /// Playwright trace.
    /// </para>
    /// </remarks>
    public static GeneratedConfig ForSession(
        SessionPath session,
        bool headed,
        string browser,
        bool transcript,
        RunOptions run,
        string? hiddenUserAgent = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(browser);

        var output = Path.Combine(session.FullPath, SessionLayout.OutputFolderName);

        return Generate(new BrowserConfigurationRequest
        {
            Browser = browser,
            Headless = !headed,
            UserDataDirectory = Path.Combine(session.FullPath, SessionLayout.ProfileFolderName),
            OutputDirectory = output,
            DownloadsDirectory = Path.Combine(session.FullPath, SessionLayout.DownloadsFolderName),
            Capabilities = GrantedCapabilities,
            SaveSession = transcript,
            Viewport = run.Viewport,
            Locale = run.Locale,
            TimeZone = run.TimeZone,
            IgnoreHttpsErrors = run.IgnoreHttpsErrors,

            // 6 b: a hidden Chromium sends the headed user agent, and only a hidden
            // one, because a headed one already sends it. See HeadedUserAgent.
            UserAgent = headed || IsFirefox(browser) ? null : hiddenUserAgent,

            // ⚠️ A NEW FILENAME PER LAUNCH, and it is the whole reason the path
            // is computed here and not fixed. `recordHar` truncates and
            // rewrites whatever path it is given at every context creation, so a
            // fixed name would silently destroy the previous run's capture the
            // moment a session was resumed -- an overwrite that a caller would
            // find out about by looking for evidence that had gone. The config
            // is regenerated per launch, so a timestamp in the name makes the
            // problem avoidable and not documentable.
            //
            // ⚠️ At the OUTPUT ROOT since 2026-08-26 (previously
            // `output\network\`). The output directory is flat and BrowserAI
            // adds no structure to it. This is the one artifact whose directory
            // BrowserAI still chooses at all, because it is a launch-time config
            // value and not something a tool names -- and the choice it
            // makes is to choose nothing.
            HarPath = run.CaptureNetwork
                ? Path.Combine(output, $"network-{DateTimeOffset.Now.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture)}{HarExtension}")
                : null,
        });
    }

    // ⚠️ DELETED 2026-10-08: `ForSurface(string instanceDirectory)`, the config of
    // the run's own child -- "the one that answers tools/list before any session
    // exists" -- headless, every capability, and a profile, output and downloads
    // folder of its own in the run's directory. The tool list is compiled into the
    // binary since that day and no such child is started. Every child left is a
    // session's, configured by ForSession above with the same capability set the
    // list was taken with.

    /// <summary>Writes a generated config, creating the directories it names.</summary>
    /// <param name="path">Where the config file goes. Overwritten if present.</param>
    /// <param name="config">What to write.</param>
    public static void WriteTo(string path, GeneratedConfig config)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(config);

        foreach (var directory in config.Directories)
        {
            _ = Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, config.Json);
    }

    /// <summary>Builds the config bytes and the list of opinions they carry.</summary>
    /// <param name="request">What this child is for.</param>
    /// <returns>The generated config.</returns>
    /// <exception cref="ArgumentException">A path is relative.</exception>
    public static GeneratedConfig Generate(BrowserConfigurationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Absolute(request.UserDataDirectory, nameof(request.UserDataDirectory));
        Absolute(request.OutputDirectory, nameof(request.OutputDirectory));
        Absolute(request.DownloadsDirectory, nameof(request.DownloadsDirectory));

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = true,

            // A Windows path is full of backslashes and a config file is read by
            // people. The default encoder additionally escapes characters a path
            // may legitimately contain, which round-trips perfectly and is
            // unreadable.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();

            writer.WriteStartObject("browser");
            writer.WriteString("browserName", request.Browser);
            writer.WriteString("userDataDir", request.UserDataDirectory);

            writer.WriteStartObject("launchOptions");

            if (IsFirefox(request.Browser))
            {
                // ⚠️ The one lever that prevents browser resurrection instead
                // of cleaning up after it, and it is written on EVERY Firefox
                // launch. Upstream writes these into the profile's `user.js`
                // before the browser starts, so the preference is in force at
                // the moment `nsAppRunner` decides whether to register -- which
                // is why a pref delivered this way works where a runtime one
                // would be too late.
                writer.WriteStartObject("firefoxUserPrefs");
                writer.WriteBoolean(FirefoxProfile.RestartRegistrationPreference, false);

                // The password-save doorhanger. Same delivery and the same
                // reason: a pref in `user.js` is in force before the first
                // navigation. See FirefoxProfile.RememberSignonsPreference for
                // what was and was not established about it.
                writer.WriteBoolean(FirefoxProfile.RememberSignonsPreference, false);

                // The session restore, delivered the same way and for the same
                // reason: in force before the browser decides what to open. See
                // FirefoxSessionRestorePreferences.
                foreach (var (name, value) in FirefoxSessionRestorePreferences)
                {
                    writer.WriteBoolean(name, value);
                }

                writer.WriteEndObject();
            }
            else
            {
                writer.WriteString("channel", Channel);

                writer.WriteStartArray("args");

                foreach (var argument in ChromiumArguments)
                {
                    writer.WriteStringValue(argument);
                }

                // 6 b, the maintainer's words of 2026-10-04 verbatim: "6 b". The
                // browser's own switch and not Playwright's per-page override,
                // because the override reaches a page only after it exists and a
                // session restore's first requests went out without it, measured
                // the same day. See HeadedUserAgent for what the switch costs.
                if (request.UserAgent is { } userAgent)
                {
                    writer.WriteStringValue(HeadedUserAgent.Switch + userAgent);
                }

                writer.WriteEndArray();
            }

            // Both families. A restored session plus Playwright's own blank page
            // is one tab more after every close; see DefaultPageArgument.
            writer.WriteStartArray("ignoreDefaultArgs");
            writer.WriteStringValue(DefaultPageArgument);
            writer.WriteEndArray();

            writer.WriteBoolean("headless", request.Headless);
            writer.WriteString("downloadsPath", request.DownloadsDirectory);
            writer.WriteEndObject();

            // `contextOptions` is `playwright.BrowserContextOptions` verbatim --
            // upstream passes it straight to `launchPersistentContext` -- so
            // every key here is Playwright's and not upstream's, and none of
            // them appears in `config.d.ts` by name.
            writer.WriteStartObject("contextOptions");

            writer.WriteStartObject("viewport");
            writer.WriteNumber("width", request.Viewport.Width);
            writer.WriteNumber("height", request.Viewport.Height);
            writer.WriteEndObject();

            writer.WriteString("locale", request.Locale);

            if (request.TimeZone is { } zone)
            {
                writer.WriteString("timezoneId", zone);
            }

            writer.WriteBoolean("ignoreHTTPSErrors", request.IgnoreHttpsErrors);

            // ⚠️ CHROMIUM ONLY. Firefox rejects `clipboard-read` outright --
            // `Unknown permission: clipboard-read`, thrown at context creation,
            // with the browser exiting -- so writing it for both families does
            // not degrade a Firefox session, it makes every one of them
            // unusable. The same shape as `channel` two blocks up, for the same
            // kind of reason.
            if (!IsFirefox(request.Browser))
            {
                writer.WriteStartArray("permissions");

                foreach (var permission in Permissions)
                {
                    writer.WriteStringValue(permission);
                }

                writer.WriteEndArray();
            }

            if (request.HarPath is { } har)
            {
                // ⚠️ `serviceWorkers: "block"` IS NOT OPTIONAL BESIDE THIS, and
                // it is the half that makes the capture honest. A request served
                // out of a service worker's cache never reaches the network
                // layer the HAR is written from, so a page with a worker
                // produces an archive that is silently INCOMPLETE -- and
                // incomplete in the direction that matters, because the
                // requests a worker serves are the repeat ones a reader is
                // looking for. Blocking workers changes what the site does; the
                // description says so.
                writer.WriteString("serviceWorkers", "block");

                writer.WriteStartObject("recordHar");
                writer.WriteString("path", har);

                // `full` and not `minimal`: minimal omits response bodies,
                // which is most of the reason to capture at all.
                writer.WriteString("mode", "full");

                // `embed` and not `attach`: `attach` writes bodies as
                // separate files beside the archive, which turns one file a
                // caller can reason about -- and delete -- into a directory.
                writer.WriteString("content", "embed");
                writer.WriteEndObject();
            }

            writer.WriteEndObject();

            writer.WriteEndObject();

            if (request.Capabilities.Count is not 0)
            {
                writer.WriteStartArray("capabilities");

                foreach (var capability in request.Capabilities)
                {
                    writer.WriteStringValue(capability);
                }

                writer.WriteEndArray();
            }

            writer.WriteString("outputDir", request.OutputDirectory);
            writer.WriteBoolean("saveSession", request.SaveSession);

            // ⚠️ FALSE, WRITTEN , NOT OMITTED, AND THERE IS NO REQUEST
            // FIELD TO TURN IT ON. Upstream's file-access roots are the whole of
            // BrowserAI's containment since 2026-08-26: our own `filename` gate
            // was deleted that day as a weaker duplicate of them, so a config
            // that lifted this would leave a caller's raw string reaching the
            // filesystem with nothing in between. `false` is also upstream's
            // default, which is exactly why it is spelled out -- an omission
            // behaves the same and records no decision, and
            // `browser_get_config` cannot read back a key the file never
            // carried. See the type's remarks for what it costs.
            writer.WriteBoolean(AllowUnrestrictedFileAccessKey, false);

            writer.WriteStartObject("console");
            writer.WriteString("level", ConsoleLevel);
            writer.WriteEndObject();

            // ⚠️ FALSE, UPSTREAM'S OWN DEFAULT, WRITTEN AND NOT OMITTED -- Q322 a,
            // decided 2026-10-03 by the maintainer, in his words: "Q322 a". A
            // model that needs coordinates asks for them on the one call:
            // browser_snapshot takes a per-call `boxes`, and the server
            // instructions tell a model to pass `boxes: true` before it uses a
            // browser_mouse_*_xy tool. Written for the reason
            // `allowUnrestrictedFileAccess` is: an omission records no decision,
            // and browser_get_config cannot read back a key the file never
            // carried.
            //
            // Corrected 2026-10-03 (previously "ALWAYS TRUE, AND THE COST IS
            // DEFERRED AND NOT PAID. A snapshot response carries a LINK to the
            // file and not the snapshot text, so boxes cost nothing until
            // something reads it -- and BrowserAI grants the `vision` capability
            // to every session, whose six `browser_mouse_*_xy` tools take
            // viewport coordinates that a snapshot without boxes gives a model
            // no way to compute. Granting the tools and withholding the numbers
            // they need would be a surface that looks complete and is not.").
            // Two halves of that were wrong. The cost was deferred for an
            // action's response and NOT for browser_snapshot, which returns the
            // snapshot inline: measured 2026-09-25 @ @playwright/mcp 0.0.82,
            // playwright-core 1.64.0-alpha-1789764292000, Chrome for Testing
            // 154.0.8037.0 headless at 1920x1080, over nine pages, medians of 6
            // to 10 runs counted with the o200k_base tokenizer as a proxy, boxes
            // on cost 175,611 tokens and boxes off 105,804 (the zoom-out
            // research, track D). And `vision` grants six tools of which THREE
            // take coordinates, the three the glob names; the other three press,
            // release and scroll. What the old sentence feared is answered by
            // the per-call parameter and the instructions line, so the tools are
            // still usable and nothing is withheld.
            writer.WriteStartObject("snapshot");
            writer.WriteBoolean("boxes", false);
            writer.WriteEndObject();

            // See the constant: it strips a `### Ran Playwright code` block from
            // every response, for a feature this product does not have.
            writer.WriteString("codegen", Codegen);

            // ⚠️ THE OPPOSITE OF UPSTREAM'S DEFAULT, WRITTEN , NOT
            // OMITTED, AND THE ONE KEY HERE THAT config.d.ts DOES NOT DECLARE.
            // See the constant. Omitting it is not neutral: `relative` is what
            // the child falls back to, and that is the defect this key was
            // adopted to end -- every artifact pointer in every tool result
            // named against a working directory the reader does not have.
            writer.WriteString("filePaths", FilePaths);

            // ⚠️ UPSTREAM'S OWN DEFAULT, WRITTEN AND NOT OMITTED, AND FOR A
            // HEADLESS LAUNCH IT CANNOT FIRE. See `IdleTimeoutMilliseconds`:
            // BrowserAI's own timer is ten minutes and both are reset by a tool
            // call, so upstream's hour is unreachable under the shipped
            // configuration. The key is written anyway because an omission
            // records no decision and `browser_get_config` cannot read back a key
            // the file never carried -- the same argument as
            // `allowUnrestrictedFileAccess` two blocks up.
            //
            // ⚠️ A HEADED LAUNCH WRITES ZERO since 2026-10-03, Q326 a: BrowserAI
            // no longer arms its own timer for one, so the hour would have been
            // the timer that closes a person's window. See `NoIdleTimeout`.
            //
            // ⚠️ AND SO DOES EVERY LAUNCH since 2026-10-08, E2 and F2. An agent may
            // set a session's idle time past an hour, or to never, and every call
            // that names the session restarts BrowserAI's countdown where upstream's
            // is restarted only by a call it receives, so upstream's hour would close
            // a browser BrowserAI is still keeping. BrowserAI's own countdown is the
            // only one.
            writer.WriteStartObject("timeouts");
            writer.WriteNumber("idle", NoIdleTimeout);
            writer.WriteEndObject();

            // ⚠️ UPSTREAM'S OWN DEFAULT, WRITTEN AND NOT OMITTED, AND THIS
            // ONE IS A STANCE AND NOT A RECORD OF ONE. Added 2026-09-21 with
            // Q219. `webmcp: true` is what lets a page put its own tools on the
            // child's tool list and its own tool names, descriptions and schemas
            // into the snapshot every snapshot-bearing result carries -- which is
            // page-authored text reaching a model, and is a live hazard row.
            //
            // It is written `true` because BrowserAI can now CALL those tools:
            // `browserai_page_tool` reaches one through a judged tool, bounded,
            // re-resolved per call. `false` would remove the capability, and --
            // decisively -- it would remove the only catalogue there is. That
            // snapshot block is how a model learns a page offers anything at all;
            // with the key false there is no block, no header count and no
            // dynamic tools, so the caller would have a tool and no way to know
            // what to name.
            //
            // Written and not omitted for the reason two blocks up: an
            // omission records no decision, `browser_get_config` cannot read back
            // a key the file never carried, and the day upstream's default moves
            // this is a red build and not a capability that quietly went.
            writer.WriteBoolean("webmcp", true);

            writer.WriteEndObject();
        }

        var json = buffer.ToArray();

        return new GeneratedConfig
        {
            Browser = request.Browser,
            ProfileDirectory = request.UserDataDirectory,
            HarPath = request.HarPath,
            Json = json,
            Opinions = Flatten(json),
            Directories =
            [
                request.UserDataDirectory,
                request.OutputDirectory,
                request.DownloadsDirectory,

                // The archive's own folder, so that a capture whose directory
                // does not exist yet is not a launch failure. Playwright creates
                // it, and creating it here means the ONE place that creates the
                // config's directories creates all of them.
                .. request.HarPath is { } archive && Path.GetDirectoryName(archive) is { Length: > 0 } folder
                    ? new[] { folder }
                    : [],
            ],
        };
    }

    /// <summary>Whether a family name is Firefox's.</summary>
    /// <param name="browser">The family, as upstream names it.</param>
    /// <returns>Whether this is the Firefox family.</returns>
    public static bool IsFirefox(string browser) =>
        string.Equals(browser, ProvisionedBrowsers.Firefox, StringComparison.OrdinalIgnoreCase);

    private static void Absolute(string path, string name)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                $"'{path}' is not absolute, and every path in a generated config must be: upstream resolves a relative one against the config file's directory, which is not where the caller meant.",
                name);
        }
    }

    /// <summary>
    /// Every leaf of the generated config as a dotted path and a value, which is
    /// exactly what the round trip looks up in the child's answer.
    /// </summary>
    /// <remarks>
    /// Read back out of the bytes and not accumulated while writing them, so
    /// the list cannot claim an opinion the file does not carry.
    /// </remarks>
    private static List<ConfigOpinion> Flatten(byte[] json)
    {
        var opinions = new List<ConfigOpinion>();
        Walk(JsonNode.Parse(json)!.AsObject(), string.Empty, opinions);
        return opinions;
    }

    private static void Walk(JsonObject node, string prefix, List<ConfigOpinion> opinions)
    {
        foreach (var (name, value) in node)
        {
            var path = prefix.Length is 0 ? name : $"{prefix}.{name}";

            if (value is JsonObject nested)
            {
                Walk(nested, path, opinions);
            }
            else if (value is not null)
            {
                // An array is one opinion and not one per element: the whole
                // point of `capabilities` is that upstream replaces it wholesale,
                // so a per-element check would pass a list that had been merged
                // with something else.
                opinions.Add(new ConfigOpinion(path, value));
            }
        }
    }
}

/// <summary>What one child's config is for.</summary>
internal sealed record BrowserConfigurationRequest
{
    /// <summary>
    /// The browser family, as upstream names it.
    /// </summary>
    /// <remarks>
    /// Defaulted, not required, and the default is the same one
    /// <c>browserai_init</c> applies. ⚠️ <b>Corrected 2026-08-19 (previously
    /// "every caller in this build asks for Chromium").</b> That stopped being
    /// true when Firefox was offered -- <see cref="BrowserConfiguration.ForSession"/> passes whatever
    /// the session's <c>browserai.data</c> records. The default survives for the
    /// reason it always had: it keeps the Firefox branch a property of the
    /// session's own record and not a decision each call site takes.
    /// <i>Corrected 2026-10-08 (previously it went on "and <c>ForSurface</c> -- the
    /// run's own browser-less child -- genuinely has no family to state"): that
    /// child is gone with the tool list compiled into the binary.</i>
    /// </remarks>
    public string Browser { get; init; } = BrowserConfiguration.BrowserName;

    /// <summary>Whether the browser runs without a window.</summary>
    /// <remarks>
    /// Written explicitly, not omitted. Upstream fills an absent
    /// <c>headless</c> with <c>platform === "linux" &amp;&amp; !DISPLAY</c>, so
    /// on Windows "no key" means "a window appears" and not "upstream
    /// decides". The assignment is guarded, so a value set here survives.
    /// </remarks>
    public required bool Headless { get; init; }

    /// <summary>The browser profile directory. Absolute.</summary>
    public required string UserDataDirectory { get; init; }

    /// <summary>Where the child writes artifacts. Absolute.</summary>
    public required string OutputDirectory { get; init; }

    /// <summary>Where the browser puts downloads. Absolute.</summary>
    public required string DownloadsDirectory { get; init; }

    /// <summary>The capabilities to enable, beyond the unconditional <c>core*</c> family.</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>Whether upstream records the session into the output directory.</summary>
    public bool SaveSession { get; init; }

    /// <summary>The viewport every page in this context gets.</summary>
    public ViewportSize Viewport { get; init; } = BrowserConfiguration.DefaultViewport;

    /// <summary>The BCP-47 locale the browser reports and formats with.</summary>
    public string Locale { get; init; } = BrowserConfiguration.HostLocale;

    /// <summary>
    /// The IANA time zone the browser reports, or <see langword="null"/> to leave
    /// upstream's default in place.
    /// </summary>
    public string? TimeZone { get; init; } = BrowserConfiguration.HostTimeZone;

    /// <summary>Whether TLS errors are ignored for every navigation in this context.</summary>
    public bool IgnoreHttpsErrors { get; init; }

    /// <summary>
    /// The user agent a Chromium launch sends in place of its own, as Chromium's
    /// <c>--user-agent</c> switch, or <see langword="null"/> to leave the browser's own.
    /// </summary>
    /// <remarks>
    /// <b>6 b, decided 2026-10-04 by the maintainer: a hidden Chromium sends the user
    /// agent a headed one does.</b> <see cref="BrowserConfiguration.ForSession"/> sets
    /// it on a hidden Chromium launch only, from <see cref="HeadedUserAgent"/>, which
    /// reads it off the browser itself; nothing writes a version here.
    /// </remarks>
    public string? UserAgent { get; init; }

    /// <summary>
    /// Where the HTTP Archive goes, or <see langword="null"/> for no capture.
    /// </summary>
    /// <remarks>
    /// <b>A path and not a boolean, because the path is per launch.</b>
    /// <c>recordHar</c> truncates whatever it is given at every context
    /// creation, so the name carries a timestamp and the decision about what it
    /// is called belongs to the caller that knows which launch this is.
    /// </remarks>
    public string? HarPath { get; init; }
}

/// <summary>
/// A viewport, in CSS pixels.
/// </summary>
/// <param name="Width">How wide.</param>
/// <param name="Height">How tall.</param>
internal sealed record ViewportSize(int Width, int Height)
{
    /// <summary>The smallest side either dimension may be.</summary>
    /// <remarks>
    /// <b>A floor and not a validation of taste.</b> A viewport of a few
    /// pixels is a page that lays out as nothing, and the failure presents as a
    /// screenshot of an empty box and not as a refusal.
    /// </remarks>
    public const int Smallest = 200;

    /// <summary>
    /// The largest side either dimension may be.
    /// </summary>
    /// <remarks>
    /// <b>4,096, and it is about tokens and not about the browser.</b> A
    /// screenshot arrives unscaled -- nothing in the path downscales one -- so a
    /// viewport past this is an image the API refuses and does not shrink, and
    /// the failure lands on the call after the one that set it. <i>Corrected
    /// 2026-09-15 (previously "upstream's <c>scaleImageToFitMessage</c> never
    /// runs here"), which named a divergence that no longer exists: upstream
    /// deleted that function, so the unscaled arrival is now everybody's
    /// behaviour and not this product's.</i>
    /// </remarks>
    public const int Largest = 4096;

    /// <summary>How it is written and how it is read: <c>WIDTHxHEIGHT</c>.</summary>
    /// <returns>The size as a caller writes it.</returns>
    public override string ToString() =>
        $"{Width.ToString(CultureInfo.InvariantCulture)}x{Height.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Reads a <c>WIDTHxHEIGHT</c> string.</summary>
    /// <param name="text">The value as the caller wrote it.</param>
    /// <param name="size">The size, when it parsed.</param>
    /// <returns>Whether it parsed and is within the bounds above.</returns>
    public static bool TryParse(string? text, out ViewportSize size)
    {
        size = BrowserConfiguration.DefaultViewport;

        if (text is null)
        {
            return false;
        }

        var parts = text.Split('x', StringSplitOptions.TrimEntries);

        if (parts.Length is not 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height)
            || width is < Smallest or > Largest
            || height is < Smallest or > Largest)
        {
            return false;
        }

        size = new ViewportSize(width, height);
        return true;
    }
}

/// <summary>
/// The per-run arguments a caller gives one launch of a session.
/// </summary>
/// <remarks>
/// <b>Every one of these is regenerated at every child launch</b> -- the same rule
/// <c>headed</c>, <c>transcript</c> and <c>debug</c> follow. A session created at one
/// viewport is resumed at another without being destroyed first. ⚠️ <i>Corrected
/// 2026-10-08 (previously "and none is written to the session record ... and nothing on
/// disk differs between the two.")</i>: F2 d writes what each run used into the record
/// as a <c>settings</c> statement, which a resume is compared with and never filled in
/// from.
/// </remarks>
internal sealed record RunOptions
{
    /// <summary>The defaults, for a call that named none of them.</summary>
    public static RunOptions Default { get; } = new();

    /// <summary>The viewport every page gets.</summary>
    public ViewportSize Viewport { get; init; } = BrowserConfiguration.DefaultViewport;

    /// <summary>The BCP-47 locale, defaulting to the host machine's.</summary>
    public string Locale { get; init; } = BrowserConfiguration.HostLocale;

    /// <summary>The IANA time zone, defaulting to the host machine's.</summary>
    public string? TimeZone { get; init; } = BrowserConfiguration.HostTimeZone;

    /// <summary>Whether TLS errors are ignored.</summary>
    public bool IgnoreHttpsErrors { get; init; }

    /// <summary>Whether this launch writes an HTTP Archive.</summary>
    public bool CaptureNetwork { get; init; }
}

/// <summary>A generated config: the bytes, and every opinion in them.</summary>
internal sealed record GeneratedConfig
{
    /// <summary>The browser family this config selects.</summary>
    /// <remarks>
    /// Carried on the config and not re-derived by parsing the bytes back,
    /// so the one function every child launch passes through can ask which
    /// family it is about to start without a JSON read.
    /// </remarks>
    public required string Browser { get; init; }

    /// <summary>The profile directory this config points the browser at.</summary>
    /// <remarks>
    /// The same string as the first entry of <see cref="Directories"/>, named
    /// and not indexed: the preflight has to open a file inside it, and a
    /// guard that depends on the order of a list is one reordering away from
    /// examining the downloads folder instead.
    /// </remarks>
    public required string ProfileDirectory { get; init; }

    /// <summary>
    /// Where this launch's HTTP Archive goes, or <see langword="null"/> when
    /// nothing is being captured.
    /// </summary>
    /// <remarks>
    /// Carried on the config so the answer <c>browserai_init</c> returns can name
    /// the file. A caller that turned network capture on has created a plaintext
    /// credential dump, and being told its path in the same answer is the
    /// difference between a fact it can act on and one it has to go looking for.
    /// </remarks>
    public string? HarPath { get; init; }

    /// <summary>The file's bytes, UTF-8, no BOM.</summary>
    public required byte[] Json { get; init; }

    /// <summary>Every leaf key and its value, for the <c>browser_get_config</c> round trip.</summary>
    public required IReadOnlyList<ConfigOpinion> Opinions { get; init; }

    /// <summary>The directories this config names, which must exist before the child starts.</summary>
    public required IReadOnlyList<string> Directories { get; init; }
}

/// <summary>One generated key, and what it was set to.</summary>
/// <param name="Path">The dotted path to the key, as it appears in the config object.</param>
/// <param name="Value">The value BrowserAI wrote.</param>
internal sealed record ConfigOpinion(string Path, JsonNode Value);
