// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BrowserAI.Interop;
using BrowserAI.Protocol;
using BrowserAI.Sessions;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Runtime;

/// <summary>Where a browser family stands on this machine, right now.</summary>
internal enum ProvisioningState
{
    /// <summary>
    /// The tree is there and complete — <c>INSTALLATION_COMPLETE</c> is present,
    /// which is the check upstream never makes at launch.
    /// </summary>
    Installed,

    /// <summary>
    /// The tree is being made ready, here or in another BrowserAI process.
    /// Browser calls are refused with
    /// <see cref="Sessions.SessionErrors.ProvisioningInProgress"/> rather than
    /// blocked.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Renamed 2026-08-18 (previously <c>Downloading</c>, and the word it
    /// produced at the caller was <c>downloading</c>).</b> One state covers five
    /// things — waiting on another process's provisioning mutex, deleting an
    /// abandoned tree, downloading, extracting, and pruning superseded revisions
    /// — and only the middle one is a download. The waiting case is the one that
    /// bit: that process has started nothing, cannot see what the holder is
    /// doing, and the holder may already be finished. <c>QUESTIONS.md</c> §9
    /// carries the four directions considered and why a fourth state word for
    /// the mutex-loser was not one of them: no caller acts differently on it, so
    /// it would add surface without adding an action. The sentence beside the
    /// word is what separates the five phases, and it always has.
    /// </remarks>
    Provisioning,

    /// <summary>The last attempt ended badly, and the reason is in the status.</summary>
    Failed,
}

/// <summary>What the browsers root holds for one family, and why.</summary>
/// <param name="Browser">The family asked about.</param>
/// <param name="State">Where it stands.</param>
/// <param name="Directory">Where the tree is, or would be.</param>
/// <param name="Detail">
/// One sentence a person or a model can act on. Never <see langword="null"/>,
/// because a state with no reason is the shape this project exists to remove.
/// </param>
internal sealed record ProvisioningStatus(string Browser, ProvisioningState State, string Directory, string Detail)
{
    /// <summary>
    /// What the browsers root weighs right now against what this family's
    /// download costs, or <see langword="null"/> when nothing is in flight.
    /// </summary>
    /// <remarks>
    /// <b>Carried on the status rather than folded into <see cref="Detail"/>,
    /// because two different sentences render it</b> — the provisioner's own
    /// state line and <c>SessionErrors.ProvisioningInProgress</c>, which is what
    /// a model reads. A pre-rendered string would make the second one quote the
    /// first one's phrasing forever.
    /// </remarks>
    public ProvisioningProgress? Progress { get; init; }
}

/// <summary>
/// How far a provisioning run has got, measured rather than reported by
/// upstream.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no upstream progress to relay, and that is measured.</b>
/// <c>@playwright/mcp</c> emits no <c>notifications/progress</c> at all — every
/// occurrence in the shipped payload is the MCP SDK's own schema or capability
/// arm ([kb](../../../kb/mcp/sdk.md#lossless-passthrough-cancellation-notifications-and-error-frames),
/// re-verification row 104). And the installer's own percentage lines are
/// switched off by the <c>--no-progress</c> flag BrowserAI passes, which sets
/// <c>PLAYWRIGHT_DOWNLOAD_NO_PROGRESS</c> and makes
/// <c>downloadFile</c>'s <c>reportProgress</c> false. So bytes on disk are not
/// the convenient signal, they are <b>the only</b> signal.
/// </para>
/// <para>
/// <b>The underlying figure is the whole browsers root, and it is not
/// monotonic.</b> It climbs through the download, climbs again through the
/// extraction, and drops once when upstream unlinks the archive it has finished
/// with. That is fine for both consumers: a stall detector asks whether the
/// number <i>changed</i>, and a progress report quotes it against a phase.
/// </para>
/// </remarks>
/// <param name="Written">
/// What THIS attempt has added under the browsers root, as of the last poll.
/// <para>
/// <b>Net of a baseline taken at the first poll, and the baseline is what makes
/// the figure true on a second install.</b> A machine that already has Chromium
/// holds 451 MB under that root before a Firefox download writes its first byte;
/// reported raw, the refusal would tell a caller that a 127.2 MB download was
/// 354% complete.
/// </para>
/// </param>
/// <param name="DownloadBytes">What this family's download costs in total, or 0 when nobody has measured it.</param>
/// <param name="Elapsed">How long this attempt has been running.</param>
/// <param name="Extracting">Whether the revision directory has appeared, which is the download-to-disk boundary.</param>
internal sealed record ProvisioningProgress(long Written, long DownloadBytes, TimeSpan Elapsed, bool Extracting);

/// <summary>
/// The four clocks first-run provisioning runs against, gathered so a test can
/// shrink them without the product carrying a debug branch.
/// </summary>
/// <remarks>
/// <b>The first one is not ours and is deliberately not here as a setting.</b>
/// Playwright's own per-socket stall timeout is
/// <c>NET_DEFAULT_TIMEOUT = 30_000</c> ms, overridable through
/// <c>PLAYWRIGHT_DOWNLOAD_CONNECTION_TIMEOUT</c> — read out of the resolved
/// bundle 2026-08-16 rather than from memory. BrowserAI sets nothing, so the
/// figure stays upstream's; <see cref="BrowserProvisioner.UpstreamStallTimeout"/>
/// records what we are relying on and
/// <see cref="BrowserProvisioner.UpstreamStallTimeoutVariable"/> names the
/// variable a test asserts is absent from the installer's environment. A
/// duplicate of the number in our own configuration would drift silently the day
/// upstream changed theirs.
/// </remarks>
internal sealed record ProvisioningTimers
{
    /// <summary>
    /// How long provisioning may make <b>no progress at all</b> before its job
    /// is closed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Replaced the 45-minute <c>AbsoluteCap</c> on 2026-08-19, at the
    /// maintainer's decision (previously "how long the installer child may run
    /// before its job is closed", a ceiling on the <i>total</i>).</b> A total cap
    /// can only ever fire on a link that is working — the arithmetic was
    /// 1,630,594,752 bits in 2,700 s, so it stopped any link sustaining less than
    /// <b>~0.60 Mbps</b> and stopped nothing else, because a link that has died
    /// is caught by upstream's own per-socket timeout twenty times sooner. It
    /// punished the one case it could reach.
    /// </para>
    /// <para>
    /// <b>Progress is bytes on disk under the browsers root, and that is one
    /// number only because the download target was moved under it</b> — see
    /// <see cref="BrowserProvisioner.DownloadDirectoryName"/>. Measured
    /// 2026-08-19 against the assembled payload, sampling every 250 ms
    /// ([kb](../../../kb/playwright/provisioning-and-timings.md#what-grows-on-disk-while-an-install-runs-and-when--2026-08-19)):
    /// the archive grows in the download directory from 47,892 B to
    /// 202,283,919 B, then the revision directory grows from 15,971,824 B to
    /// 447,613,878 B, then the archive is unlinked. <b>Every one of the 41
    /// samples in that run differed from the one before it</b>, and Firefox's 27
    /// behaved identically.
    /// </para>
    /// <para>
    /// <b>Ten minutes, and the number is set by upstream's own lock rather than
    /// by taste.</b> <c>registry.install()</c> waits on
    /// <c>&lt;browsers root&gt;\__dirlock</c> <i>before</i> it writes anything at
    /// all, and measurement C of 2026-08-19 timed that wait at <b>470 s</b>
    /// before upstream gives up by itself with <c>ELOCKED</c>. So a healthy
    /// install queued behind another legitimately writes nothing for up to
    /// 7 m 50 s, and any cap at or under that kills the queued one. Ten minutes
    /// clears it by 130 s.
    /// </para>
    /// <para>
    /// <b>It is deliberately ten times
    /// <see cref="Updates.UpdateService.StallBudget"/>, which is 60 s "twice
    /// upstream's <c>NET_DEFAULT_TIMEOUT</c>".</b> That reasoning is right there
    /// and wrong here: a Velopack download has no directory lock in front of it,
    /// so the only thing a stall can mean is a dead socket. Here a stall can also
    /// mean <i>correctly waiting for another installer</i>, and 60 s would turn
    /// the commonest healthy case on a two-family machine into a failure.
    /// </para>
    /// <para>
    /// <b>What it now permits, stated as a threshold.</b> Anything that moves one
    /// byte every ten minutes survives; nothing else does. That is not a slow
    /// link, it is a dead one — and unlike the old cap, it holds for a 400 MB
    /// download on a 0.1 Mbps line, which would legitimately take nine hours.
    /// </para>
    /// </remarks>
    public TimeSpan StallCap { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long everything after the browser's own directory first appears may
    /// take.
    /// </summary>
    /// <remarks>
    /// <b>The phase boundary is observable, which is why this cap can exist at
    /// all.</b> Upstream downloads into a temp directory and only creates
    /// <c>&lt;browsers-root&gt;\&lt;browser&gt;-&lt;rev&gt;\</c> when it starts
    /// unzipping, so that directory appearing <i>is</i> the transition from
    /// network to disk. What the cap covers is therefore extraction plus the two
    /// small companion downloads that follow it (<c>ffmpeg</c>, and
    /// <c>winldd</c> which Windows pulls in with it) — measured 2026-08-16 at
    /// 1.5 s of the 12 s total, so ten minutes is three orders of magnitude of
    /// headroom rather than a guess.
    /// <para>
    /// <b>It is a total and it stays a total, unlike
    /// <see cref="StallCap"/>.</b> The reason the argument against a total cap
    /// does not apply here is that this phase is not on the network: it is a
    /// local unzip of a file that has already arrived, so there is no
    /// slow-but-working case for it to punish.
    /// </para>
    /// </remarks>
    public TimeSpan ExtractionCap { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>How often the phase watcher looks.</summary>
    public TimeSpan Poll { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The clock every duration above is measured against, and the one that
    /// releases each poll.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Added 2026-08-20, and it is the fix for a named flake rather than a
    /// generalisation.</b> <c>ProvisioningTests.ASlowInstallThatKeepsWritingIsNotStoppedHoweverLongItTakes</c>
    /// went red once in nine consecutive full-suite runs. It asserted that an
    /// install writing every 25 ms survives a 1-second stall cap, which is a
    /// <i>ratio</i> and was chosen for exactly that reason — but a ratio between
    /// two real clocks is still a race, and on a machine running 500 tests at
    /// once the double's 25 ms gap can stretch past the product's cap while the
    /// product behaves perfectly.
    /// </para>
    /// <para>
    /// <b>The clock alone would not have fixed it, and that is the half worth
    /// stating.</b> The detector judges an install on <b>bytes on disk</b> as
    /// well as on time, so a test that froze the clock and still read a real
    /// directory would still be racing the filesystem. The second seam is
    /// <see cref="BrowserProvisioner.WeighBrowsersRoot"/>, and the two together
    /// are what make the arm a statement about the product rather than about the
    /// machine.
    /// </para>
    /// <para>
    /// <b><see cref="TimeProvider"/> because it is the framework's own seam</b>,
    /// and because the poll wait goes through it too — see
    /// <c>BrowserProvisioner.PollTicker</c>. A clock that governed the elapsed
    /// arithmetic and left the sleep on the wall clock would leave a test
    /// advancing a frozen clock while the product slept for real.
    /// </para>
    /// </remarks>
    public TimeProvider Clock { get; init; } = TimeProvider.System;
}

/// <summary>
/// First-run browser provisioning: it downloads, it never blocks, and it says
/// which of those it is doing.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>init</c> must not block, and that constraint shapes the whole type.</b>
/// A caller that waits three minutes inside one tool call has had whatever
/// timing it was managing corrupted, with nothing to read and no way to decide
/// whether waiting is worth it. So <see cref="Ensure"/> returns immediately with
/// <see cref="ProvisioningState.Provisioning"/> and every upstream call is
/// refused with <see cref="Sessions.SessionErrors.ProvisioningInProgress"/>,
/// which names the download size and says the same call will work shortly. The
/// same child then navigates once the install lands — no restart, because
/// nothing about the child depended on the browser existing when it started.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-16 (previously "and <c>browser_get_config</c> keeps
/// working because it needs no browser").</b> It does not. Measured twice
/// against the child directly with an empty browsers root, it answers
/// <c>isError: true</c> from <c>throwIfExecutableMissing</c> — it never
/// <i>launches</i> a browser, which is why the round trip is cheap on a
/// provisioned machine, but the executable has to exist
/// ([kb](../../../kb/playwright/configuration.md#browser-provisioning)). So
/// <b>every</b> upstream tool is refused meanwhile, that one included, and
/// letting it through would have bought a worse answer rather than a working
/// one. What keeps a downloading session inspectable is BrowserAI's <b>own</b>
/// tools — <c>browserai_list</c>, <c>browserai_resume</c> and
/// <c>browserai_set_purpose</c> all answer throughout, because none of them
/// needs a browser.
/// </para>
/// <para>
/// <b>Browsers live outside <c>current\</c>, resolved through
/// <see cref="Hosting.IAppPaths"/>.</b> Inside it, every update would re-download
/// 203.8 MB. The path also has to be <b>absolute</b>: it reaches the child as
/// <c>PLAYWRIGHT_BROWSERS_PATH</c>, and a relative value there resolves against
/// <c>INIT_CWD</c> — inherited from whatever npm ancestor last ran — before the
/// child's own working directory.
/// </para>
/// <para>
/// <b>The installer is upstream's own, run out of the payload.</b>
/// <c>node.exe cli.js install-browser &lt;browser&gt; --no-shell --no-progress</c>,
/// so the revision comes from the vendored <c>browsers.json</c> rather than from
/// a URL anybody typed. <c>--no-shell</c> is load-bearing:
/// <c>chrome-headless-shell</c> is never provisioned, which is what makes the
/// chromium-alias channel mandatory rather than a preference.
/// </para>
/// <para>
/// ⚠️ <b><c>PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD</c> does not gate this.</b>
/// Measured 2026-08-16: the variable is read in exactly two places —
/// <c>installBrowsersForNpmInstall</c> and <c>ensureConfiguredBrowserInstalled</c>
/// — and <c>registry.install()</c>, which <c>install-browser</c> calls, does not
/// consult it. It stays in the child environment for the reason it was always
/// mandated (the <i>server</i> must not provision behind our back) and is not a
/// kill switch this code could rely on.
/// </para>
/// <para>
/// <b>One install per machine, not one per process.</b> The mutex is
/// <c>Global\</c>-scoped, taken at a zero timeout, and a process that does not
/// get it does not queue a second download — it watches for the marker the
/// winner will write. Two concurrent <c>registry.install()</c> runs against one
/// root is precisely how a half-extracted tree acquires an
/// <c>INSTALLATION_COMPLETE</c>.
/// </para>
/// <para>
/// <b>The install runs on its own thread, and that is a correctness requirement
/// rather than a performance one.</b> A named mutex is owned by the
/// <i>thread</i> that waited on it, so a continuation resuming on a different
/// pool thread makes the release throw about "an unsynchronized block of code" —
/// naming nothing relevant and pointing nowhere near the cause.
/// </para>
/// </remarks>
internal sealed class BrowserProvisioner : IDisposable
{
    /// <summary>
    /// Playwright's own per-socket stall timeout, which BrowserAI deliberately
    /// leaves alone.
    /// </summary>
    /// <remarks>
    /// Read 2026-08-16 out of the resolved <c>playwright-core</c> bundle:
    /// <c>NET_DEFAULT_TIMEOUT = 3e4</c>, applied as
    /// <c>+(PLAYWRIGHT_DOWNLOAD_CONNECTION_TIMEOUT || "0") || NET_DEFAULT_TIMEOUT</c>.
    /// It is recorded here so that the caps above can be reasoned about against
    /// a stated figure, and it is never set, so it can never drift from
    /// upstream's.
    /// </remarks>
    public static TimeSpan UpstreamStallTimeout { get; } = TimeSpan.FromSeconds(30);

    /// <summary>The variable that would override it, named so its absence is testable.</summary>
    public const string UpstreamStallTimeoutVariable = "PLAYWRIGHT_DOWNLOAD_CONNECTION_TIMEOUT";

    /// <summary>The mutex prefix that makes an install machine-wide rather than per process.</summary>
    public const string MutexPrefix = $@"{LockScopes.GlobalPrefix}BrowserAI-Provision-";

    /// <summary>
    /// The machine-wide name that serialises installs of one browser into one
    /// browsers root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Keyed on the root as well as the family, and the root is the half that
    /// is easy to leave out.</b> Two BrowserAI installations with different
    /// browsers roots are genuinely independent: their downloads write to
    /// different directories and neither can corrupt the other. A name carrying
    /// only the family would serialise them anyway — and worse, the loser would
    /// then sit watching for a marker in <i>its own</i> root that the winner is
    /// never going to write, so it would report "provisioning" until the outer
    /// deadline. Found by the suite, where every rig has a browsers root of its
    /// own.
    /// </para>
    /// <para>
    /// 128 bits of a SHA-256, exactly as
    /// [the per-directory gate](../Sessions/SessionPath.cs) does it, and
    /// <c>Global\</c> because there is no <c>Local\</c> fallback anywhere in this
    /// product: a logon-session-scoped name would let a Remote Desktop session and
    /// the console session install into one directory at once, each reporting
    /// success.
    /// </para>
    /// </remarks>
    /// <param name="browsersDirectory">The browsers root.</param>
    /// <param name="browser">The family.</param>
    /// <returns>The mutex name.</returns>
    public static string MutexNameFor(string browsersDirectory, string browser)
    {
        ArgumentNullException.ThrowIfNull(browsersDirectory);
        ArgumentNullException.ThrowIfNull(browser);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{browsersDirectory.ToUpperInvariant()}|{browser.ToUpperInvariant()}"));

        return MutexPrefix + Convert.ToHexString(digest)[..LockScopes.PerDirectoryHashLength];
    }

    /// <summary>
    /// How large each family's first-run download is, quoted to a caller
    /// deciding whether waiting is worth it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured, dated, and re-established by asking the CDN rather than by
    /// reasoning.</b> Every figure is the sum of the exact <c>content-length</c>
    /// of the three archives that family's install fetches — the browser, plus
    /// <c>ffmpeg-win64.zip</c> (1,411,741 B) and <c>winldd-win64.zip</c>
    /// (128,684 B), which both families download into the same root:
    /// </para>
    /// <list type="bullet">
    /// <item><b>chromium</b> — 202,283,919 + 1,411,741 + 128,684 =
    /// 203,824,344 B. Re-measured 2026-08-16 at rev 1237 / 152.0.7977.8,
    /// unchanged from 2026-08-15.</item>
    /// <item><b>firefox</b> — 125,706,704 + 1,411,741 + 128,684 =
    /// 127,247,129 B. Measured 2026-08-19 at rev 1539 / 153.0, the same way and
    /// on the same day as a clean provisioning run that produced 356,674,059 B
    /// on disk, twice, byte-identical.</item>
    /// </list>
    /// <para>
    /// <b>Both figures are for one family into an empty root, which is the
    /// predicate and not an accident.</b> A machine that already has the other
    /// family pays less — measured 2026-08-19, Firefox beside an existing
    /// <c>ffmpeg</c> and <c>winldd</c> downloads 125,706,704 B and nothing else,
    /// because each of the three archives carries its own completion marker. The
    /// upper bound is quoted, for the same reason
    /// <see cref="Sessions.SessionManager.RequiredFreeBytes"/> is sized on the
    /// larger family: a caller deciding whether to wait is not helped by a
    /// number that is right only on the machines that already paid.
    /// </para>
    /// <para>
    /// ⚠️ <b>Added 2026-08-19 at Firefox support (previously a single
    /// <c>FirstRunDownloadSize</c> const of <c>"203.8 MB"</c>).</b> The const
    /// was reached through a <c>browser</c> parameter that never varied, so the
    /// day <c>browserai_init</c> accepted <c>firefox</c> it would have quoted
    /// Chromium's figure for a Firefox download — a measured-looking number
    /// measured of something else, which is the exact defect class this
    /// repository exists to catch.
    /// </para>
    /// <para>
    /// ⚠️ <b>Derived from <see cref="FirstRunDownloadBytes"/> since 2026-08-19
    /// (previously two hand-written strings, defended as "strings rather than
    /// numbers because the only thing done with one is putting it in a sentence,
    /// and a byte count formatted at the call site is a second place for it to
    /// drift").</b> That stopped being true the moment the refusal became a
    /// <i>progress</i> report: a percentage needs the number, so the number is in
    /// the code either way, and hand-writing the string beside it is exactly the
    /// second place the old sentence was worried about. One figure now, formatted
    /// once. <c>ProvisioningTests.EveryProvisionedFamilyHasAMeasuredDownloadSize</c>
    /// still fails the build for a family with no figure, and
    /// <c>.TheQuotedDownloadSizeIsTheMeasuredByteCountFormatted</c> holds the two
    /// halves together.
    /// </para>
    /// <para>
    /// <b>Keyed on <see cref="ProvisionedBrowsers.Families"/>.</b>
    /// [kb](../../../kb/playwright/provisioning-and-timings.md#first-run-provisioning)
    /// carries both figures and how to re-establish them.
    /// </para>
    /// </remarks>
    /// <summary>
    /// How many bytes each family's first-run download moves, exactly.
    /// </summary>
    /// <remarks>
    /// <b>The sum of the exact <c>content-length</c> of the three archives that
    /// family's install fetches</b>, which is the same predicate
    /// <see cref="FirstRunDownloadSizes"/> has always quoted — one family into an
    /// empty root. It is a <c>long</c> rather than a string because the
    /// provisioning refusal reports bytes so far <i>against</i> it.
    /// </remarks>
    public static IReadOnlyDictionary<string, long> FirstRunDownloadBytes { get; } =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            [ProvisionedBrowsers.Chromium] = 203_824_344,
            [ProvisionedBrowsers.Firefox] = 127_247_129,
        };

    public static IReadOnlyDictionary<string, string> FirstRunDownloadSizes { get; } =
        FirstRunDownloadBytes.ToDictionary(
            entry => entry.Key,
            entry => Megabytes(entry.Value),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The directory inside the browsers root that the installer downloads
    /// into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-08-19 so that "is provisioning making progress" is one
    /// question about one directory.</b> Upstream downloads into
    /// <c>os.tmpdir()\playwright-download-XXXXXX\</c> and only creates
    /// <c>&lt;browsers root&gt;\&lt;browser&gt;-&lt;rev&gt;\</c> when it starts
    /// unzipping — so for the whole of the download phase, which is the phase
    /// that takes the time, <b>nothing under the browsers root grows at all</b>.
    /// A stall detector reading the root alone would kill every install on a slow
    /// link, which is the defect the stall detector was written to remove.
    /// </para>
    /// <para>
    /// <b>The alternative was to scan <c>%TEMP%</c> for
    /// <c>playwright-download-*</c>, and it is measurably wrong.</b> On this
    /// machine on 2026-08-19 that scan found a <c>playwright-download-PRU23e</c>
    /// abandoned on 2026-08-16 holding 128,684 B — an unrelated installer's
    /// residue, counted as our progress. A directory BrowserAI names, empties
    /// before each run and owns cannot be polluted that way.
    /// </para>
    /// <para>
    /// <b>One subdirectory per family under it</b>, because the provisioning
    /// mutex is keyed on the family and two of them install at once by design;
    /// each run empties only its own. <b>It is redirected by setting
    /// <c>TEMP</c> and <c>TMP</c> for the installer child</b>, which is what
    /// Node's <c>os.tmpdir()</c> reads on Windows. Proven rather than assumed: the 2026-08-19 measurement ran the
    /// real installer with both set and produced a byte-identical tree —
    /// 451,389,780 B for chromium, matching
    /// [the figure taken with the default temp](../../../kb/playwright/provisioning-and-timings.md#first-run-provisioning)
    /// exactly.
    /// </para>
    /// <para>
    /// <b>The name is dot-prefixed and that is not decoration.</b>
    /// <see cref="RevisionPrune"/> walks this root and removes anything whose
    /// name starts with a manifest <c>DirectoryPrefix</c>; a download directory
    /// named <c>chromium-tmp</c> would be deleted out from under the installer
    /// writing into it.
    /// </para>
    /// <para>
    /// <b>Same volume as the extraction, which is a small improvement it is worth
    /// naming.</b> <c>SessionManager.RequiredFreeBytes</c> is sized on archive
    /// and tree coexisting; before this the archive could be on a different
    /// volume from the tree, so that figure was checked against a volume only
    /// half the work landed on.
    /// </para>
    /// </remarks>
    public const string DownloadDirectoryName = ".downloads";

    /// <summary>
    /// How large one family's first-run download is, as a sentence fragment.
    /// </summary>
    /// <remarks>
    /// <b>A family with no measured figure is named rather than given one.</b>
    /// The caller is a refusal explaining why a browser is not there yet, and
    /// "an amount nobody has measured" is a worse sentence than a number and a
    /// better one than the wrong number.
    /// </remarks>
    /// <param name="browser">The family, as upstream names it.</param>
    /// <returns>The measured download size, or a phrase saying there is none.</returns>
    public static string DownloadSizeFor(string browser)
    {
        ArgumentNullException.ThrowIfNull(browser);

        return FirstRunDownloadSizes.TryGetValue(browser, out var size)
            ? size
            : "a size nobody has measured for this browser";
    }

    private readonly ConcurrentDictionary<string, Attempt> _attempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly PayloadLayout _payload;
    private readonly ILogger _logger;
    private readonly ProvisioningTimers _timers;
    private readonly CancellationTokenSource _stopping = new();
    private BrowsersManifest? _manifest;
    private int _disposed;

    /// <summary>Creates the provisioner for one process.</summary>
    /// <param name="payload">Where <c>node.exe</c>, <c>cli.js</c> and <c>browsers.json</c> live.</param>
    /// <param name="browsersDirectory">The browsers root. <b>Must be absolute.</b></param>
    /// <param name="loggerFactory">Where progress and failures go.</param>
    /// <param name="timers">The caps. The default is production's.</param>
    /// <exception cref="ArgumentException"><paramref name="browsersDirectory"/> is not absolute.</exception>
    public BrowserProvisioner(
        PayloadLayout payload,
        string browsersDirectory,
        ILoggerFactory loggerFactory,
        ProvisioningTimers? timers = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(browsersDirectory);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (!Path.IsPathFullyQualified(browsersDirectory))
        {
            throw new ArgumentException(
                $"The browsers root must be absolute, and '{browsersDirectory}' is not: it reaches the child as {ChildLaunch.BrowsersPathVariable}, and a relative value there resolves against INIT_CWD first.",
                nameof(browsersDirectory));
        }

        _payload = payload;
        BrowsersDirectory = browsersDirectory;
        _logger = loggerFactory.CreateLogger<BrowserProvisioner>();
        _timers = timers ?? new ProvisioningTimers();

        // Assigned here rather than as a property initialiser, which cannot see
        // the payload. An object initialiser still replaces it, which is what
        // makes it a seam.
        StartInstaller = (browser, root) => new NodeInstallerRun(_payload, browser, root);
        PruneRevisions = browser => _ = RevisionPrune.Run(BrowsersDirectory, Manifest(), _logger, familyAlreadyHeld: browser);
    }

    /// <summary>
    /// How the installer is started, so the suite can drive the state machine and
    /// the three caps without downloading 203.8 MB per assertion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The seam is the launch and nothing above it.</b> Everything that
    /// decides behaviour — the marker check, the machine-wide mutex, the phase
    /// watcher, every cap, the removal of a partial tree — is on this side of the
    /// line and runs identically against a double. What a substitute replaces is
    /// one <c>CreateProcessW</c>.
    /// </para>
    /// <para>
    /// <b>The default is the real thing, and the real thing is proven
    /// separately</b> against an empty browsers root through the published
    /// binary — so this cannot become the only path anybody exercises.
    /// </para>
    /// </remarks>
    public Func<string, string, IInstallerRun> StartInstaller { get; init; }

    /// <summary>
    /// What a successful provision does about superseded revisions, so the suite
    /// can prove that a prune which throws never fails the provision it followed.
    /// </summary>
    /// <remarks>
    /// <b>The seam exists for exactly one property, and that property is
    /// otherwise unassertable.</b> Nothing on
    /// <see cref="RevisionPrune.Run"/>'s
    /// own path throws by design — the process census, the enumeration and the
    /// sizing all catch — so the <c>catch</c> at the call site is a guard against
    /// a future edit rather than against a reachable case today, and a guard
    /// nothing exercises is the shape this project's audit keeps finding. The
    /// argument is the family whose mutex the calling thread already holds.
    /// </remarks>
    public Action<string> PruneRevisions { get; init; }

    /// <summary>
    /// What the browsers root weighs, which is the one number both the stall
    /// detector and the progress report read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>A seam since 2026-08-20, and it exists for one arm that could not
    /// otherwise be honest.</b> The stall detector judges an install on two
    /// inputs — a clock and a byte count — so freezing only the clock leaves the
    /// second one racing a real directory that a real double is really writing
    /// to. With both replaced, <i>survives while bytes arrive</i> and <i>dies the
    /// instant they stop</i> become statements a test can make in lockstep
    /// rather than statements about how busy the machine was.
    /// </para>
    /// <para>
    /// <b>The default is the real measurement and the arithmetic above it is
    /// untouched</b> — the baseline, the stall comparison and the sentence a
    /// caller reads are all on this side of the seam, so a substitute changes
    /// where the number comes from and nothing about what is done with it.
    /// </para>
    /// <para>
    /// <b>The signature carries the previous sample deliberately.</b> A root
    /// that cannot be weighed answers with the caller's last figure rather than
    /// with zero, so the stall clock keeps running instead of being reset by a
    /// failure — and a substitute has to be handed the same fact to be able to
    /// stand in for that.
    /// </para>
    /// </remarks>
    public Func<string, long, long> WeighBrowsersRoot { get; init; } = ObservedBytes;

    /// <summary>The browsers root this provisioner installs into. Always absolute.</summary>
    public string BrowsersDirectory { get; }

    /// <summary>Where a family's tree is, or would be.</summary>
    /// <param name="browser">The family, as upstream names it.</param>
    /// <returns>The absolute directory.</returns>
    public string DirectoryFor(string browser) =>
        Path.Combine(BrowsersDirectory, Manifest().For(browser).DirectoryName);

    /// <summary>
    /// Reports a family's state without starting anything.
    /// </summary>
    /// <remarks>
    /// The read-only half, for callers that are deciding what to say rather than
    /// what to do — <c>browserai_list</c>, a status line, a refusal.
    /// </remarks>
    /// <param name="browser">The family, as upstream names it.</param>
    /// <returns>Where it stands.</returns>
    public ProvisioningStatus Peek(string browser)
    {
        ArgumentNullException.ThrowIfNull(browser);

        var revision = Manifest().For(browser);
        var directory = Path.Combine(BrowsersDirectory, revision.DirectoryName);

        if (IsComplete(directory))
        {
            return new ProvisioningStatus(browser, ProvisioningState.Installed, directory, $"{revision.Description} is installed at '{directory}'.");
        }

        if (_attempts.TryGetValue(browser, out var attempt) && !attempt.Task.IsCompleted)
        {
            return Provisioning(browser, directory, revision, attempt);
        }

        if (attempt is { Task.IsCompleted: true } finished && finished.Task.Result is { } result && !result.Succeeded)
        {
            return new ProvisioningStatus(browser, ProvisioningState.Failed, directory, result.Detail);
        }

        return new ProvisioningStatus(
            browser,
            ProvisioningState.Failed,
            directory,
            $"{revision.Description} is not installed at '{directory}' and no download is running.");
    }

    /// <summary>
    /// Makes sure a family is being provisioned, and <b>returns immediately</b>.
    /// </summary>
    /// <remarks>
    /// Idempotent and safe to call on every <c>init</c>: an installed family
    /// costs one <c>File.Exists</c>, and a download already running is joined
    /// rather than started again.
    /// </remarks>
    /// <param name="browser">The family, as upstream names it.</param>
    /// <returns>Where it stands, as of this instant.</returns>
    public ProvisioningStatus Ensure(string browser)
    {
        ArgumentNullException.ThrowIfNull(browser);

        var revision = Manifest().For(browser);
        var directory = Path.Combine(BrowsersDirectory, revision.DirectoryName);

        // The hot path, and it has to stay one syscall: this runs on every
        // session start.
        if (IsComplete(directory))
        {
            return new ProvisioningStatus(browser, ProvisioningState.Installed, directory, $"{revision.Description} is installed at '{directory}'.");
        }

        var attempt = _attempts.AddOrUpdate(
            browser,
            static (key, state) => state.Provisioner.Start(key, state.Revision),
            static (key, existing, state) => existing.Task.IsCompleted && !Result(existing).Succeeded
                ? state.Provisioner.Start(key, state.Revision)
                : existing,
            (Provisioner: this, Revision: revision));

        return attempt.Task.IsCompleted
            ? Peek(browser)
            : Provisioning(browser, directory, revision, attempt);
    }

    /// <summary>
    /// Waits for a family to finish provisioning, for callers that legitimately
    /// block — <c>browserai_reinstall_browser</c>, and the suite.
    /// </summary>
    /// <remarks>
    /// <b>No tool a model calls goes through here except the reinstall</b>, which
    /// is explicitly an operation the caller asked to have happen. Everything on
    /// the <c>init</c> path uses <see cref="Ensure"/> and is answered
    /// immediately.
    /// </remarks>
    /// <param name="browser">The family, as upstream names it.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>Where it ended up.</returns>
    public async Task<ProvisioningStatus> WaitAsync(string browser, CancellationToken cancellationToken = default)
    {
        var status = Ensure(browser);

        if (status.State is not ProvisioningState.Provisioning || !_attempts.TryGetValue(browser, out var attempt))
        {
            return status;
        }

        _ = await attempt.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return Peek(browser);
    }

    /// <summary>
    /// Deletes a family's tree and provisions it again, in that order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Delete-then-download rather than download-beside-and-swap, and Windows
    /// decides that rather than taste.</b> A directory holding open executables
    /// cannot be renamed, so the swap that would make this atomic is not
    /// available: the window in which no browser is installed is unavoidable, and
    /// the caller is told the operation is destructive rather than being sold an
    /// atomicity that does not exist.
    /// </para>
    /// <para>
    /// The caller establishes that nothing is <b>running from</b> the tree; this
    /// method establishes that nothing is <b>writing into</b> it. Those are two
    /// different questions and only the first one used to be asked.
    /// </para>
    /// <para>
    /// ⚠️ <b>The provisioning mutex is taken across the delete, added
    /// 2026-08-18 (previously no lock at all).</b> The two reasons the old
    /// comment gave were both wrong. <i>"The delete is itself the guard"</i> — it
    /// guards against running <b>executables</b>, and a concurrent installer is
    /// not one: it is <c>node.exe</c> out of the payload directory, extracting
    /// <i>into</i> the tree, so <see cref="BrowserProcesses.RunningFrom"/>
    /// returns empty and the guard passes. The reinstall then deleted the
    /// installer's partially-extracted files, the installer finished and wrote
    /// <c>INSTALLATION_COMPLETE</c> over the gutted tree, and both processes
    /// reported success — after which <c>IsComplete</c> reports installed for
    /// ever, the first launch produces <c>spawn EFTYPE</c>, and upstream writes
    /// <c>DEPENDENCIES_VALIDATED</c> into the corrupt directory and suppresses
    /// revalidation for thirty days. A durable confident wrong answer.
    /// </para>
    /// <para>
    /// <i>"Taking the provisioning mutex here would deadlock against the
    /// installer, which takes it on its own thread"</i> — a different thread
    /// taking a mutex is a wait, not a deadlock. The real obstacle is that this
    /// method is <c>async</c> and a named mutex is owned by the <b>thread</b>
    /// that waited on it, so a continuation resuming on another pool thread would
    /// make the release throw. That is a shape problem with a known answer, and
    /// it is the one <see cref="Start"/> already uses: the locked section runs on
    /// its own <c>LongRunning</c> thread and is finished before anything is
    /// awaited. The mutex is <b>released before</b> <see cref="WaitAsync"/>, or
    /// the install this method exists to trigger would queue behind it. Found by
    /// [the adversarial review](../../../docs/reviews/2026-08-18-adversarial-locking.md),
    /// A3.
    /// </para>
    /// </remarks>
    /// <param name="browser">The family, as upstream names it.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>What was removed, what would not go, and where the new tree stands.</returns>
    public async Task<ReinstallOutcome> ReinstallAsync(string browser, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(browser);

        var revision = Manifest().For(browser);
        var directory = Path.Combine(BrowsersDirectory, revision.DirectoryName);

        // On a thread of its own, for the reason in the remarks: everything
        // inside the named mutex has to happen on the thread that took it, and
        // nothing may be awaited while it is held.
        var (removed, removedBytes, failures) = await Task.Factory.StartNew(
            () => RemoveForReinstall(browser, directory),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).ConfigureAwait(false);

        if (!removed)
        {
            // An install is in flight. Nothing was deleted, and saying so is the
            // whole value of the lock: the alternative is deleting the files a
            // running installer is about to declare complete.
            ProvisioningLog.ReinstallDeferred(_logger, browser, directory);

            return new ReinstallOutcome(
                browser,
                directory,
                Deleted: false,
                RemovedBytes: 0,
                Failures: [],
                new ProvisioningStatus(
                    browser,
                    ProvisioningState.Failed,
                    directory,
                    $"Another BrowserAI process is provisioning {browser} into '{directory}' right now, so nothing was deleted and nothing was downloaded. "
                    + "Deleting a tree an installer is extracting into produces a directory that is neither the old install nor the new one, and upstream then marks it complete. Wait for that download to finish and call this again."));
        }

        if (failures.Count is not 0)
        {
            // Re-provisioning on top of a tree that would not delete is how a
            // half-old, half-new browser directory gets an INSTALLATION_COMPLETE
            // written over it. Stop instead, and say what is holding it.
            return new ReinstallOutcome(
                browser,
                directory,
                Deleted: true,
                removedBytes,
                failures,
                new ProvisioningStatus(
                    browser,
                    ProvisioningState.Failed,
                    directory,
                    $"'{directory}' was not fully removed, so nothing was re-downloaded on top of it."));
        }

        var status = await WaitAsync(browser, cancellationToken).ConfigureAwait(false);

        return new ReinstallOutcome(browser, directory, Deleted: true, removedBytes, failures, status);
    }

    /// <summary>
    /// Where each shared component's tree is, or would be — in
    /// <see cref="ProvisionedBrowsers.SharedComponents"/>' order.
    /// </summary>
    /// <remarks>
    /// Read through the manifest exactly as a family's directory is, so a
    /// revision bump moves these without anybody editing anything, and a payload
    /// whose <c>browsers.json</c> stops naming one of them throws a sentence
    /// listing what it does name rather than composing a path to nowhere.
    /// </remarks>
    /// <returns>The absolute directories.</returns>
    public IReadOnlyList<string> SharedComponentDirectories() =>
        [.. ProvisionedBrowsers.SharedComponents.Select(component => Path.Combine(BrowsersDirectory, Manifest().For(component).DirectoryName))];

    /// <summary>
    /// Deletes every shared component's tree and downloads them again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The whole operation is on one thread and holds three mutexes, which is
    /// the difference from <see cref="ReinstallAsync"/>.</b> That method releases
    /// the family's mutex before the install, because the install it triggers
    /// would otherwise queue behind it. This one cannot: the installer it runs
    /// writes into the same two directories that <b>both</b> families' installers
    /// write into, so letting go between the delete and the download would put
    /// this process's extraction and a family install's extraction into one tree
    /// — which is exactly the "neither install, and upstream marks it complete"
    /// corruption the provisioning mutex exists to prevent. The delete and the
    /// install therefore happen inside one hold, and nothing is awaited while it
    /// is held.
    /// </para>
    /// <para>
    /// <b>Three mutexes, in a fixed order, none of them waiting.</b> Both
    /// families' names plus <see cref="ProvisionedBrowsers.Shared"/>'s own: a
    /// chromium or a firefox install in flight is also an <c>ffmpeg</c> install
    /// in flight, and only that family's mutex says so. Zero timeout on all three
    /// means this cannot deadlock against a peer taking them in another order —
    /// it fails, releases what it has, and reports that an install is running.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-19 (previously "A pre-existing hazard this makes
    /// visible rather than creates … two family installs racing into one shared
    /// component directory is reachable in the shipped product and is not
    /// addressed here").</b> The <i>reachability</i> half was right and the
    /// <i>hazard</i> half was not, and the difference was never measured before
    /// it was written down. Chromium's installer and Firefox's installer do both
    /// lay down <c>ffmpeg</c> and <c>winldd</c>, and
    /// <see cref="MutexNameFor(string, string)"/> keys on the family, so BrowserAI
    /// really does allow the two to run at once. <b>They cannot extract
    /// concurrently, because upstream serialises them.</b>
    /// <c>registry.install()</c> takes a <c>proper-lockfile</c> directory lock at
    /// <c>&lt;PLAYWRIGHT_BROWSERS_PATH&gt;\__dirlock</c> <i>before</i> it touches
    /// any executable and holds it for the whole install, so every install on
    /// this machine against this root queues — measured 2026-08-19 four ways
    /// ([kb](../../../kb/playwright/provisioning-and-timings.md), re-verification
    /// row 101), including both families started 8 ms apart into one empty root
    /// and finishing with four complete trees.
    /// </para>
    /// <para>
    /// <b>What is real is a wait, not a corruption</b>, and it belongs to the
    /// <i>waiter</i>: upstream retries the lock for a bounded budget and then
    /// fails the install outright with its own <c>ELOCKED</c> message. Nothing in
    /// this file is sized against that budget, and nothing needs to be — a wait
    /// happens before the browser's directory appears, so
    /// <see cref="ProvisioningTimers.ExtractionCap"/> has not started.
    /// ⚠️ <b>Corrected 2026-08-19 (previously "and only the 45-minute
    /// <c>AbsoluteCap</c> covers it").</b> That cap is gone, and its replacement
    /// is sized <b>on this very number</b>:
    /// <see cref="ProvisioningTimers.StallCap"/> is ten minutes precisely because
    /// upstream's 470 s <c>__dirlock</c> wait writes nothing at all, so a shorter
    /// stall cap would kill the queued install this paragraph says is healthy.
    /// The hazard index carries the row.
    /// </para>
    /// <para>
    /// <b>None of that makes the three mutexes unnecessary here</b>, and this is
    /// exactly the part upstream's lock does not do. Upstream serialises
    /// <i>installs</i> against each other; it knows nothing about the
    /// <b>delete</b> this method performs first, which takes no <c>__dirlock</c>
    /// and never could — it is not an install. Without the hold, this thread's
    /// recursive delete lands inside a family install's extraction and removes
    /// files out from under it, and upstream's marker is written by a run that
    /// completed over a tree somebody else was emptying. That is ours to close,
    /// and the hold across delete <i>and</i> install is what closes it.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>What was removed, what would not go, and where the components stand.</returns>
    public async Task<ReinstallOutcome> ReinstallSharedAsync(CancellationToken cancellationToken = default)
    {
        var directories = SharedComponentDirectories();

        return await Task.Factory.StartNew(
            () => ReinstallShared(directories),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        // Signals the phase watcher to close the installer's job, which is what
        // stops a 200 MB download when the process serving the caller goes away.
        _stopping.Cancel();
        _stopping.Dispose();
    }

    /// <summary>Whether a browser directory is complete rather than merely present.</summary>
    /// <remarks>
    /// <b>The marker is the whole check, and Playwright never makes it at
    /// launch.</b> A partial tree without it produces <c>spawn EFTYPE</c>, after
    /// which upstream writes <c>DEPENDENCIES_VALIDATED</c> into the corrupt
    /// directory and suppresses revalidation for thirty days.
    /// </remarks>
    /// <summary>
    /// The destructive half of <see cref="ReinstallAsync"/>, inside the
    /// machine-wide provisioning mutex, on one thread.
    /// </summary>
    /// <remarks>
    /// <b>Zero timeout, exactly as the install path uses it.</b> An install in
    /// flight is not something to queue behind — the caller is a model waiting on
    /// a tool call, and a 203.8 MB download is minutes — it is a reason to refuse
    /// and say which. <b>An abandoned mutex is an acquisition</b> (race R3): the
    /// previous holder died, this thread owns it, and refusing here would make one
    /// crashed installer disable reinstalls until the machine is rebooted.
    /// </remarks>
    /// <param name="browser">The family.</param>
    /// <param name="directory">Its revision directory.</param>
    /// <returns>What was there, what would not go, and whether the delete happened at all.</returns>
    private (bool Removed, long RemovedBytes, List<string> Failures) RemoveForReinstall(string browser, string directory)
    {
        using var mutex = MachineMutex.Create(MutexNameFor(BrowsersDirectory, browser));

        if (mutex.Acquire(LockScopes.NeverWaits) is MutexAcquisition.NotAcquired)
        {
            return (false, 0, []);
        }

        try
        {
            var removedBytes = SizeOf(directory);
            var failures = new List<string>();

            ProvisioningLog.Reinstalling(_logger, browser, directory);

            // §E's routine, not a second one: a per-node try/catch, so one locked
            // file costs that file rather than the whole tree.
            TreeDelete.Remove(directory, failures);

            // A completed attempt would otherwise make Ensure believe the tree it
            // just deleted is still there.
            _ = _attempts.TryRemove(browser, out _);

            return (true, removedBytes, failures);
        }
        finally
        {
            mutex.Release();
        }
    }

    /// <summary>
    /// The whole of <see cref="ReinstallSharedAsync"/>, on the thread that took
    /// the mutexes.
    /// </summary>
    /// <param name="directories">Every shared component's tree, absolute.</param>
    /// <returns>The outcome.</returns>
    private ReinstallOutcome ReinstallShared(IReadOnlyList<string> directories)
    {
        // Two lists deliberately. `created` is what has to be disposed and
        // `acquired` is what has to be RELEASED first, and they differ by exactly
        // one object on the refusal path -- the mutex that was opened and not
        // taken. Releasing one this thread does not own throws, and dropping a
        // handle on one it does own leaves ownership to be resolved by the next
        // waiter as an abandonment, which is a warning somebody then has to
        // explain.
        var created = new List<MachineMutex>();
        var acquired = new List<MachineMutex>();

        try
        {
            foreach (var name in ProvisionedBrowsers.Families.Append(ProvisionedBrowsers.Shared))
            {
                var mutex = MachineMutex.Create(MutexNameFor(BrowsersDirectory, name));
                created.Add(mutex);

                // An abandoned acquisition IS an acquisition, exactly as the
                // family path reads it: refusing here would let one crashed
                // installer disable shared repairs until the machine is rebooted.
                if (mutex.Acquire(LockScopes.NeverWaits) is MutexAcquisition.NotAcquired)
                {
                    ProvisioningLog.ReinstallDeferred(_logger, name, string.Join(", ", directories));

                    return Unchanged(
                        directories,
                        $"Another BrowserAI process is provisioning {name} right now, and a {name} install also writes the shared components — so nothing was deleted and nothing was downloaded. "
                        + "Deleting a tree an installer is extracting into produces a directory that is neither the old install nor the new one, and upstream then marks it complete. Wait for that download to finish and call this again.");
                }

                acquired.Add(mutex);
            }

            return RebuildShared(directories);
        }
        finally
        {
            for (var index = acquired.Count - 1; index >= 0; index--)
            {
                acquired[index].Release();
            }

            for (var index = created.Count - 1; index >= 0; index--)
            {
                created[index].Dispose();
            }
        }
    }

    /// <summary>
    /// The destructive half and the install, with every mutex held by this
    /// thread.
    /// </summary>
    /// <remarks>
    /// <b>One installer invocation for both components, and the completeness
    /// check is still per component.</b> Measured 2026-08-19:
    /// <c>install-browser ffmpeg</c> rebuilds whichever of the two is missing,
    /// because each carries its own marker. So the install is one command and the
    /// verdict is two <c>File.Exists</c> — a run that exits 0 having left one of
    /// them unmarked is the shape that produces <c>spawn EFTYPE</c> later, and it
    /// is reported here rather than discovered then.
    /// </remarks>
    /// <param name="directories">Every shared component's tree, absolute.</param>
    /// <returns>The outcome.</returns>
    private ReinstallOutcome RebuildShared(IReadOnlyList<string> directories)
    {
        var sizes = directories.Select(SizeOf).ToList();
        var removedBytes = sizes.Contains(-1) ? -1 : sizes.Sum();
        var failures = new List<string>();

        foreach (var directory in directories)
        {
            ProvisioningLog.Reinstalling(_logger, ProvisionedBrowsers.Shared, directory);
            TreeDelete.Remove(directory, failures);
        }

        if (failures.Count is not 0)
        {
            return new ReinstallOutcome(
                ProvisionedBrowsers.Shared,
                directories[0],
                Deleted: true,
                removedBytes,
                failures,
                new ProvisioningStatus(
                    ProvisionedBrowsers.Shared,
                    ProvisioningState.Failed,
                    directories[0],
                    "The shared components were not fully removed, so nothing was re-downloaded on top of them."))
            {
                Directories = directories,
            };
        }

        var revision = Manifest().For(ProvisionedBrowsers.SharedInstallTarget);
        var target = Path.Combine(BrowsersDirectory, revision.DirectoryName);
        var result = RunInstaller(ProvisionedBrowsers.SharedInstallTarget, revision, target, AttemptClock.Start(_timers.Clock), new AttemptPhase());

        // Every component's own marker, not the installer's exit code and not the
        // one directory RunInstaller was pointed at. A second component that
        // silently did not arrive is exactly the state this tool exists to
        // repair, and shipping it back as success would make the answer the
        // defect.
        var incomplete = directories.Where(directory => !IsComplete(directory)).ToList();

        var status = result.Succeeded && incomplete.Count is 0
            ? new ProvisioningStatus(
                ProvisionedBrowsers.Shared,
                ProvisioningState.Installed,
                target,
                $"The shared components ({string.Join(", ", ProvisionedBrowsers.SharedComponents)}) were downloaded again into '{BrowsersDirectory}'. {result.Detail}")
            : new ProvisioningStatus(
                ProvisionedBrowsers.Shared,
                ProvisioningState.Failed,
                target,
                incomplete.Count is 0
                    ? result.Detail
                    : $"{result.Detail} These are still incomplete and carry no '{BrowsersManifest.InstallationCompleteMarker}': {string.Join(", ", incomplete)}.");

        return new ReinstallOutcome(ProvisionedBrowsers.Shared, target, Deleted: true, removedBytes, failures, status)
        {
            Directories = directories,
        };
    }

    /// <summary>An outcome that changed nothing, for the shared path.</summary>
    /// <param name="directories">The trees that were left alone.</param>
    /// <param name="detail">Why.</param>
    /// <returns>The outcome.</returns>
    private static ReinstallOutcome Unchanged(IReadOnlyList<string> directories, string detail) =>
        new(
            ProvisionedBrowsers.Shared,
            directories[0],
            Deleted: false,
            RemovedBytes: 0,
            Failures: [],
            new ProvisioningStatus(ProvisionedBrowsers.Shared, ProvisioningState.Failed, directories[0], detail))
        {
            Directories = directories,
        };

    private static bool IsComplete(string directory) =>
        File.Exists(Path.Combine(directory, BrowsersManifest.InstallationCompleteMarker));

    /// <summary>
    /// Whether an abandoned provisioning mutex is a reason to delete the tree —
    /// which it is <b>only when the tree is unmarked</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Extracted 2026-08-18, so that the rule has a name and a test.</b> It
    /// was <c>acquisition is AcquiredAbandoned</c> alone, and the marker — the
    /// discriminator — was consulted on the line <i>after</i> the tree had already
    /// been deleted. An abandoned mutex over a <b>complete</b> install therefore
    /// wiped a tree ~100 processes may have been running out of and re-downloaded
    /// 203.8 MB, on the strength of an inference that is simply false: <i>the
    /// holder died, therefore what is in the directory is unusable</i>. The holder
    /// keeps this mutex through <see cref="Prune"/>, which walks every process on
    /// the machine and is slow exactly when the machine is busy, so <i>died after
    /// writing the marker</i> is the reachable case rather than the exotic one —
    /// it is the same interval the 2026-08-17 fix was made in, and the mirror of
    /// the same false inference.
    /// </para>
    /// <para>
    /// <b>The window between the marker appearing and this line is microseconds
    /// and cannot be staged</b>, which is why the test is of this predicate rather
    /// than of an interleaving: all four combinations, so removing the second half
    /// is red. What an abandoned mutex over a marked tree means is that the holder
    /// died during <see cref="Prune"/>, and the recovery for that is to re-run the
    /// prune — which the success path below already does.
    /// </para>
    /// </remarks>
    /// <param name="acquisition">How the provisioning mutex was acquired.</param>
    /// <param name="directory">The revision directory the acquisition guards.</param>
    /// <returns>Whether the tree must be removed before installing.</returns>
    internal static bool AbandonedTreeIsUnusable(MutexAcquisition acquisition, string directory) =>
        acquisition is MutexAcquisition.AcquiredAbandoned && !IsComplete(directory);

    private static ProvisioningResult Result(Attempt attempt) =>
        attempt.Task.IsCompletedSuccessfully
            ? attempt.Task.Result
            : new ProvisioningResult(false, "The provisioning task itself failed.");

    private static long SizeOf(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length)
                : 0;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // A size that cannot be measured is reported as unknown rather than
            // as zero, which would read as "there was nothing there".
            return -1;
        }
    }

    /// <summary>
    /// Every byte under the browsers root, which is the one number both the
    /// stall detector and the progress report read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It tolerates a tree changing underneath it rather than throwing, and
    /// that is required rather than defensive.</b> The thing being weighed is
    /// being written and deleted by another process while this enumeration runs —
    /// upstream unlinks the whole download directory the instant it has finished
    /// extracting — so <c>IgnoreInaccessible</c> and a swallowed failure are the
    /// only shapes in which a sampler can exist at all.
    /// </para>
    /// <para>
    /// <b>An unreadable root answers with the caller's previous figure</b>, so
    /// the stall clock keeps running rather than being reset by a failure. A root
    /// nobody can weigh is not progress.
    /// </para>
    /// </remarks>
    /// <param name="root">The browsers root.</param>
    /// <param name="previous">What the last successful sample said.</param>
    /// <returns>The byte total.</returns>
    private static long ObservedBytes(string root, long previous)
    {
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };

            return new DirectoryInfo(root).Exists
                ? new DirectoryInfo(root).EnumerateFiles("*", options).Sum(file => file.Length)
                : 0;
        }
#pragma warning disable CA1031 // A sampler that can fail the install it is measuring is worse than one that reports no news; every failure shape means the same thing here.
        catch (Exception)
#pragma warning restore CA1031
        {
            return previous;
        }
    }

    /// <summary>
    /// The one unfinished state, and the sentence that says which of its five
    /// phases this is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Renamed 2026-08-18 (previously <c>Downloading</c>).</b> The word at
    /// the caller became <c>provisioning</c>, and the method that produces it
    /// could not keep naming the one phase out of five that this is not always.
    /// </para>
    /// <para>
    /// <b>Both sentences end with what to do next, and that is not decoration.</b>
    /// <c>downloading</c> told a model, by itself, that time and bandwidth were
    /// what it was waiting on. <c>provisioning</c> says only <i>not yet</i>, so
    /// the sentence has to carry the recovery the word used to imply — otherwise
    /// the rename would leave the reader worse off, which is the one outcome it
    /// was not allowed to have.
    /// </para>
    /// </remarks>
    /// <param name="browser">The family.</param>
    /// <param name="directory">Where the tree is going.</param>
    /// <param name="revision">The revision being provisioned.</param>
    /// <param name="attempt">The attempt in flight, and the phase it reached.</param>
    /// <returns>The status.</returns>
    private static ProvisioningStatus Provisioning(string browser, string directory, BrowserRevision revision, Attempt attempt) =>
        Provisioning(browser, directory, revision, attempt, DownloadBytesFor(browser));

    private static ProvisioningStatus Provisioning(
        string browser,
        string directory,
        BrowserRevision revision,
        Attempt attempt,
        long downloadBytes) =>
        new(
            browser,
            ProvisioningState.Provisioning,
            directory,
            attempt.Phase.IsWaitingForAnotherProcess

                // ⚠️ Never "is being downloaded" on this branch. This process has
                // started nothing: it lost the machine-wide provisioning mutex
                // and is watching for the holder's completion marker. What the
                // holder is doing -- downloading, extracting, or pruning old
                // revisions, which walks every process on the machine -- is not
                // knowable from here, so it is not claimed. See AttemptPhase.
                ? $"Another BrowserAI process holds the provisioning lock for {browser}; this one is watching for its completion marker into '{directory}' rather than starting a second copy, and has been since {attempt.Started.ToString("O", CultureInfo.InvariantCulture)}. Browser tools are refused until the marker appears and BrowserAI's own tools keep working; wait and call the same tool again on the same session, which does not have to be re-created."
                : $"{revision.Description} is being downloaded into '{directory}'; started {attempt.Started.ToString("O", CultureInfo.InvariantCulture)}. Browser tools are refused until it lands and BrowserAI's own tools keep working; wait and call the same tool again on the same session, which does not have to be re-created.")
        {
            Progress = attempt.Phase.Reading(downloadBytes),
        };

    /// <summary>
    /// What one family's whole first-run download weighs, or <c>0</c> when
    /// nobody has measured it.
    /// </summary>
    /// <remarks>
    /// <b>Zero rather than a guess, and the renderer branches on it.</b> A family
    /// added without a measurement gets a progress report that quotes bytes and
    /// elapsed time and no percentage, which is the same rule
    /// <see cref="DownloadSizeFor"/> already follows for the sentence.
    /// </remarks>
    /// <param name="browser">The family, as upstream names it.</param>
    /// <returns>The measured byte count, or <c>0</c>.</returns>
    public static long DownloadBytesFor(string browser)
    {
        ArgumentNullException.ThrowIfNull(browser);

        return FirstRunDownloadBytes.TryGetValue(browser, out var bytes) ? bytes : 0;
    }

    private BrowsersManifest Manifest() => _manifest ??= BrowsersManifest.Read(_payload);

    /// <summary>
    /// Removes browser revisions the resolved manifest no longer names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pruning is ours because we turned upstream's off.</b>
    /// <c>PLAYWRIGHT_SKIP_BROWSER_GC=1</c> is mandated in the child environment,
    /// so nothing else will ever remove a superseded revision — and a browser
    /// tree is ~430 MiB, stranded per bump, per machine, forever.
    /// </para>
    /// <para>
    /// <b><paramref name="browser"/> is passed as the family already held.</b>
    /// This runs inside the per-family mutex, so the pruner must not try to
    /// re-acquire it; a zero-timeout acquire would refuse and a waiting one
    /// would deadlock against this very call.
    /// </para>
    /// <para>
    /// <b>The <c>catch</c> is the whole reason this is a method rather than a
    /// line</b>, and what it prevents was measured rather than assumed. It runs
    /// inside <see cref="Install"/>'s catch-all, so without it a prune that threw
    /// would be logged as
    /// <see cref="ProvisioningLog.Failed"/> — <i>"Provisioning chromium failed"</i>
    /// at <see cref="LogLevel.Error"/>, carrying the pruner's exception — over a
    /// 203.8 MB download that had just succeeded, and nothing anywhere would say
    /// the disk went unreclaimed. Planted 2026-08-17: the status the caller sees
    /// stays <see cref="ProvisioningState.Installed"/>, because
    /// <see cref="Peek"/> reads the completion marker before it reads the cached
    /// result — so the damage is a confident wrong answer in the log rather than
    /// a wrong answer to the model, which is exactly the kind that survives.
    /// <see cref="PruneLog.PassFailed"/> was written for this and was called from
    /// nowhere until 2026-08-17; the audit of the reconstructed call site is what
    /// found it.
    /// </para>
    /// <para>
    /// Reconstructed 2026-08-17 after the agent building it was cut off
    /// mid-edit by an API limit, having reverted this call site and not yet
    /// restored it. The build was red on exactly one symbol, which is the
    /// cheapest possible way for that to present.
    /// </para>
    /// </remarks>
    /// <param name="browser">The family whose mutex the caller already holds.</param>
    private void Prune(string browser)
    {
        try
        {
            PruneRevisions(browser);
        }
#pragma warning disable CA1031 // Reclaiming disk is never urgent and never worth failing an install that succeeded; every failure shape means the same thing here.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            PruneLog.PassFailed(_logger, failure);
        }
    }

    private Attempt Start(string browser, BrowserRevision revision)
    {
        var started = DateTimeOffset.Now;
        var phase = new AttemptPhase();

        // LongRunning, so this gets a thread of its own rather than a pool
        // thread: the body takes a named mutex, and a named mutex is owned by
        // the thread that waited on it.
        var task = Task.Factory.StartNew(
            () => Install(browser, revision, phase),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        return new Attempt(task, started, phase);
    }

    private ProvisioningResult Install(string browser, BrowserRevision revision, AttemptPhase phase)
    {
        var directory = Path.Combine(BrowsersDirectory, revision.DirectoryName);
        var deadline = AttemptClock.Start(_timers.Clock);

        try
        {
            using var mutex = MachineMutex.Create(MutexNameFor(BrowsersDirectory, browser));

            // Zero, deliberately: a process that does not get the mutex must not
            // queue a SECOND download of the same 203.8 MB, it must watch for the
            // marker the winner is about to write.
            var acquisition = mutex.Acquire(LockScopes.NeverWaits);

            if (acquisition is MutexAcquisition.NotAcquired)
            {
                ProvisioningLog.AnotherProcessIsInstalling(_logger, browser);

                // Recorded before the wait and cleared after it, so the sentence
                // a caller reads says what this process is actually doing.
                // Nothing here knows what the HOLDER is doing, which is the whole
                // reason the old sentence was wrong.
                phase.EnterWait();

                try
                {
                    var (finished, taken) = WaitForAnotherProcess(browser, directory, mutex, deadline, phase);

                    if (finished is not null)
                    {
                        return finished;
                    }

                    acquisition = taken;
                }
                finally
                {
                    phase.LeaveWait();
                }
            }

            try
            {
                // ⚠️ Corrected 2026-08-18 (previously `if (acquisition is
                // AcquiredAbandoned)` alone, justified as "whatever it left is
                // unmarked and therefore unusable"). The marker is the
                // discriminator and it was asked on the line AFTER the tree was
                // deleted, so an abandoned mutex over a COMPLETE tree wiped an
                // install ~100 processes may be running out of, and re-downloaded
                // 203.8 MB. That is the same false inference the 2026-08-17 fix
                // removed from the other side -- "I lost the mutex, therefore a
                // download is in flight" -- and it lives in the same interval:
                // the holder keeps the mutex through Prune, which walks every
                // process on the machine and is slow exactly when the machine is
                // busy, so dying after the marker and before the release is the
                // reachable case rather than the exotic one.
                //
                // An abandoned mutex over a MARKED tree means the holder died
                // during Prune. The correct recovery is to re-run Prune, which is
                // what the success path below already does.
                // (docs/reviews/2026-08-18-adversarial-locking.md, A2.)
                if (AbandonedTreeIsUnusable(acquisition, directory))
                {
                    // The previous holder died mid-install and left something
                    // unmarked, and re-running on top of it is the same call
                    // again -- so the tree goes first, which is what makes this a
                    // recovery rather than a retry.
                    ProvisioningLog.AbandonedInstallFound(_logger, browser, directory);
                    TreeDelete.Remove(directory, []);
                }

                // Re-checked under the mutex: another process may have finished
                // between the hot-path check and this line, and re-downloading
                // 203.8 MB on top of a complete tree is the cost of not looking.
                var result = IsComplete(directory)
                    ? new ProvisioningResult(true, $"{revision.Description} was already installed at '{directory}'.")
                    : RunInstaller(browser, revision, directory, deadline, phase);

                if (result.Succeeded)
                {
                    // §A: PLAYWRIGHT_SKIP_BROWSER_GC=1 is mandated, so pruning old
                    // revisions is BrowserAI's job. Here rather than at startup
                    // because this is the one moment the answer can have changed —
                    // a revision becomes superseded when a new one lands.
                    Prune(browser);
                }

                return result;
            }
            finally
            {
                mutex.Release();
            }
        }
#pragma warning disable CA1031 // A provisioning failure is reported to the caller as a state with a reason; there is no path on which it may take the process down.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            ProvisioningLog.Failed(_logger, browser, failure);
            return new ProvisioningResult(false, $"Provisioning {browser} failed: {failure.GetType().Name}: {failure.Message}");
        }
    }

    /// <summary>
    /// Waits out whoever holds the provisioning mutex, watching for <b>both</b>
    /// the marker they may write and the mutex they will certainly drop.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-17 (previously: wait only for the
    /// marker).</b> Losing the mutex was taken to mean <i>somebody is
    /// downloading, and a marker is coming</i>. That inference is false whenever
    /// the holder is past its download - and it is reachably false, because the
    /// holder keeps the mutex through <see cref="Prune"/>, which walks every
    /// process on the machine and is slow exactly when the machine is busy. A
    /// caller that had just <b>deleted</b> the tree then waited for a marker
    /// nobody was going to write, for the full outer deadline of <b>sixty
    /// minutes</b> — the timer this method no longer has — with no browser
    /// installed the whole time.
    /// </para>
    /// <para>
    /// <b>Found 2026-08-17 by running the suite with every test at once</b> -
    /// <c>ReinstallBrowserTests.ItDeletesTheTreeAndDownloadsItAgainWhenNothingIsRunning</c>
    /// hung twice in thirteen runs, and the process log named it exactly:
    /// <i>"browserai_reinstall_browser is deleting ..."</i> followed by
    /// <i>"Another BrowserAI process is already provisioning chromium; watching
    /// for its marker"</i>. There was no other process. It is not a test-only
    /// shape: <c>init</c> provisions on a background thread and returns at once,
    /// so any caller that follows an <c>init</c> with a
    /// <c>browserai_reinstall_browser</c> can meet it in production, and at the
    /// ~100-process design point it needs no sequencing at all.
    /// </para>
    /// <para>
    /// <b>The marker is still checked first</b>, so a genuine second downloader
    /// is still never started: the mutex is only taken when the holder has let go
    /// <i>without</i> leaving a complete tree, which is precisely the case that
    /// used to hang.
    /// </para>
    /// <para>
    /// ⚠️ <b>The waiter's own tripwire became the SAME stall detector on
    /// 2026-08-19 (previously <c>OuterDeadline</c>, sixty minutes of wall clock,
    /// justified as "the absolute cap is 45 minutes and this is 60, so anything
    /// that reaches it is a bug in the caps above").</b> That justification was
    /// arithmetic against a number that no longer exists: with the installer
    /// capped on <i>stalling</i> rather than on total time, a holder on a
    /// 0.3 Mbps link legitimately runs for over an hour, and a sixty-minute
    /// tripwire here would report <i>"has not finished"</i> about a download that
    /// is working. The fourth timer went with it — <see cref="ProvisioningTimers"/>
    /// carries three now, not four.
    /// </para>
    /// <para>
    /// <b>The waiter can measure the holder, which is what makes one cap enough.</b>
    /// Both processes write into the same browsers root and the download target
    /// is inside it (<see cref="DownloadDirectoryName"/>), so the holder's
    /// progress is visible from here as bytes. A holder that died leaves the
    /// mutex abandoned and is overtaken on the line above without any timer at
    /// all; a holder that is alive and frozen is exactly a stall, and it is
    /// caught by the same number and the same measurement the installer uses on
    /// itself.
    /// </para>
    /// </remarks>
    /// <param name="browser">The family.</param>
    /// <param name="directory">The revision directory a complete install marks.</param>
    /// <param name="mutex">The provisioning mutex, not held by this thread.</param>
    /// <param name="deadline">The clock this attempt has been running against.</param>
    /// <param name="phase">Where the sample is published for a caller to read.</param>
    /// <returns>
    /// Either a finished result - the other process installed it, or the stall
    /// cap fired - or no result and the acquisition this thread now holds.
    /// </returns>
    private (ProvisioningResult? Finished, MutexAcquisition Acquisition) WaitForAnotherProcess(
        string browser,
        string directory,
        MachineMutex mutex,
        AttemptClock deadline,
        AttemptPhase phase)
    {
        var bytes = WeighBrowsersRoot(BrowsersDirectory, 0);
        var lastChange = deadline.Elapsed;

        phase.Observed(bytes, deadline.Elapsed, extracting: false);

        using var ticker = new PollTicker(_timers.Clock, _timers.Poll);

        while (true)
        {
            if (IsComplete(directory))
            {
                return (
                    new ProvisioningResult(true, $"{browser} was provisioned by another BrowserAI process into '{directory}'."),
                    MutexAcquisition.NotAcquired);
            }

            // The holder let go and left no complete tree, so there is nothing to
            // wait for: this thread installs. Zero timeout, so a holder that is
            // genuinely mid-download keeps it and this loop keeps watching.
            var acquisition = mutex.Acquire(LockScopes.NeverWaits);

            if (acquisition is not MutexAcquisition.NotAcquired)
            {
                ProvisioningLog.TheOtherProcessLetGoWithoutInstalling(_logger, browser, directory);
                return (null, acquisition);
            }

            var sample = WeighBrowsersRoot(BrowsersDirectory, bytes);

            if (sample != bytes)
            {
                bytes = sample;
                lastChange = deadline.Elapsed;
            }

            phase.Observed(bytes, deadline.Elapsed, Directory.Exists(directory));

            if (deadline.Elapsed - lastChange > _timers.StallCap)
            {
                // The holder is alive -- it still owns the mutex -- and has
                // written nothing for the whole cap. Nothing here can stop it:
                // it belongs to another process and this one has no job object
                // over it, so what is reported is that waiting has stopped.
                ProvisioningLog.HolderStalled(_logger, browser, (int)_timers.StallCap.TotalMinutes);

                return (
                    new ProvisioningResult(
                        false,
                        $"Another BrowserAI process still holds the provisioning lock for {browser} and has written nothing at all under '{BrowsersDirectory}' for {Minutes(_timers.StallCap)} minutes; the root has weighed {Bytes(bytes)} since {Seconds(lastChange)} s in, out of {Seconds(deadline.Elapsed)} s of waiting. Nothing was downloaded here, and nothing was stopped -- that install belongs to another process. Close it, or wait for it, and call the same tool again."),
                    MutexAcquisition.NotAcquired);
            }

            if (_stopping.IsCancellationRequested)
            {
                return (
                    new ProvisioningResult(false, $"BrowserAI is shutting down; the wait for another process to finish provisioning {browser} was abandoned."),
                    MutexAcquisition.NotAcquired);
            }

            ticker.WaitForNextPoll(_stopping.Token);
        }
    }

    private ProvisioningResult RunInstaller(string browser, BrowserRevision revision, string directory, AttemptClock deadline, AttemptPhase phase)
    {
        _ = Directory.CreateDirectory(BrowsersDirectory);

        ProvisioningLog.Downloading(_logger, browser, revision.Revision, BrowsersDirectory);

        using var run = StartInstaller(browser, BrowsersDirectory);

        var failure = Watch(run, browser, directory, deadline, phase);

        if (failure is not null)
        {
            // Closing the job is the kill: TerminateProcess on the installer
            // alone would leave the download helper it forked behind.
            run.Stop();

            // A partial tree is unmarked and would be re-downloaded anyway, but
            // leaving it costs 400 MB and makes the next attempt's progress
            // unreadable.
            TreeDelete.Remove(directory, []);

            return new ProvisioningResult(false, failure);
        }

        var exitCode = run.ExitCode;

        // Both halves, and the second is the one upstream never checks. An
        // installer that exits 0 having written no marker has left a tree that
        // launches as `spawn EFTYPE` and never re-downloads.
        if (exitCode is not 0 || !IsComplete(directory))
        {
            ProvisioningLog.InstallerRefused(_logger, browser, exitCode, Tail(run.Output));
            TreeDelete.Remove(directory, []);

            return new ProvisioningResult(
                false,
                $"The installer for {revision.Description} exited with code {exitCode.ToString(CultureInfo.InvariantCulture)} and left no '{BrowsersManifest.InstallationCompleteMarker}' in '{directory}'. {Tail(run.Output)}");
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var said = run.Output.Trim();
            ProvisioningLog.InstallerSaid(_logger, browser, said);
        }

        ProvisioningLog.Installed(_logger, browser, revision.Revision, (int)deadline.Elapsed.TotalSeconds, directory);

        return new ProvisioningResult(true, $"{revision.Description} was downloaded into '{directory}' in {deadline.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s.");
    }

    /// <summary>
    /// Watches one installer run against the two caps, and answers with the cap
    /// that fired.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The first cap became a <b>stall</b> detector on 2026-08-19
    /// (previously a 45-minute ceiling on the total).</b> The reasoning is on
    /// <see cref="ProvisioningTimers.StallCap"/>; what changed here is that the
    /// loop now takes a measurement rather than only reading a clock, and the
    /// measurement is the same one <see cref="ProvisioningProgress"/> publishes.
    /// </para>
    /// <para>
    /// <b>One sample serves both, and that is deliberate.</b> A stall detector
    /// reading one number and a progress report reading another is two answers
    /// to one question, and the day they disagree the refusal would say a
    /// download was moving while the watcher killed it for not moving.
    /// </para>
    /// </remarks>
    /// <param name="run">The installer.</param>
    /// <param name="browser">The family.</param>
    /// <param name="directory">The revision directory whose appearance is the phase boundary.</param>
    /// <param name="deadline">The clock this attempt has been running against.</param>
    /// <param name="phase">Where the sample is published for a caller to read.</param>
    /// <returns><see langword="null"/> when the installer exited on its own.</returns>
    private string? Watch(IInstallerRun run, string browser, string directory, AttemptClock deadline, AttemptPhase phase)
    {
        var extraction = default(TimeSpan?);
        var bytes = WeighBrowsersRoot(BrowsersDirectory, 0);
        var lastChange = deadline.Elapsed;

        phase.Observed(bytes, deadline.Elapsed, extracting: false);

        using var ticker = new PollTicker(_timers.Clock, _timers.Poll);

        while (!run.HasExited)
        {
            if (_stopping.IsCancellationRequested)
            {
                return $"BrowserAI is shutting down; the download of {browser} was stopped.";
            }

            // The phase boundary, and it is observable rather than inferred:
            // upstream downloads into the directory named by DownloadDirectoryName
            // and creates this one only when it starts unzipping.
            if (extraction is null && Directory.Exists(directory))
            {
                extraction = deadline.Elapsed;
                ProvisioningLog.Extracting(_logger, browser, (int)deadline.Elapsed.TotalSeconds);
            }

            var sample = WeighBrowsersRoot(BrowsersDirectory, bytes);

            if (sample != bytes)
            {
                // ANY change, in either direction. Upstream unlinks the archive
                // once it has extracted it, so a shrinking root is a working
                // installer and a total that has not moved at all is the only
                // thing a stall can honestly be read off.
                bytes = sample;
                lastChange = deadline.Elapsed;
            }

            phase.Observed(bytes, deadline.Elapsed, extraction is not null);

            if (deadline.Elapsed - lastChange > _timers.StallCap)
            {
                ProvisioningLog.CapReached(_logger, browser, "stall", (int)_timers.StallCap.TotalMinutes);

                return $"Provisioning {browser} wrote nothing at all under '{BrowsersDirectory}' for {Minutes(_timers.StallCap)} minutes and was stopped; the root has weighed {Bytes(bytes)} since {Seconds(lastChange)} s in, out of {Seconds(deadline.Elapsed)} s elapsed. Nothing usable was left on disk.";
            }

            if (extraction is { } began && deadline.Elapsed - began > _timers.ExtractionCap)
            {
                ProvisioningLog.CapReached(_logger, browser, "extraction", (int)_timers.ExtractionCap.TotalMinutes);

                return $"Extracting {browser} passed the {Minutes(_timers.ExtractionCap)}-minute cap and was stopped. Nothing usable was left on disk.";
            }

            ticker.WaitForNextPoll(_stopping.Token);
        }

        return null;
    }

    /// <summary>A byte count in the unit the download figures are quoted in.</summary>
    /// <param name="bytes">The count.</param>
    /// <returns>The figure, decimal MB to one place.</returns>
    internal static string Megabytes(long bytes) =>
        $"{(bytes / 1_000_000d).ToString("F1", CultureInfo.InvariantCulture)} MB";

    private static string Bytes(long bytes) =>
        $"{Megabytes(bytes)} ({bytes.ToString("N0", CultureInfo.InvariantCulture)} B)";

    private static string Seconds(TimeSpan span) =>
        span.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture);

    private static string Minutes(TimeSpan span) =>
        span.TotalMinutes.ToString(span.TotalMinutes < 1 ? "F2" : "F0", CultureInfo.InvariantCulture);

    private static string Tail(string output)
    {
        var text = output.Trim();

        return text.Length switch
        {
            0 => "The installer wrote nothing.",
            > 800 => "The installer said: " + text[^800..],
            _ => "The installer said: " + text,
        };
    }

    /// <summary>
    /// How long this attempt has been running, read from the injected clock.
    /// </summary>
    /// <remarks>
    /// <b>It replaced a <c>Stopwatch</c> on 2026-08-20</b>,
    /// which is the same object with the wall clock welded into it. Everything
    /// the caps compare against comes from here, so a test that freezes the
    /// clock freezes the whole detector rather than half of it.
    /// </remarks>
    /// <param name="Clock">The clock this attempt is measured against.</param>
    /// <param name="Started">The timestamp the attempt began at.</param>
    private readonly record struct AttemptClock(TimeProvider Clock, long Started)
    {
        /// <summary>Starts one.</summary>
        /// <param name="clock">The clock to measure against.</param>
        /// <returns>The running clock.</returns>
        public static AttemptClock Start(TimeProvider clock) => new(clock, clock.GetTimestamp());

        /// <summary>How long it has been running.</summary>
        public TimeSpan Elapsed => Clock.GetElapsedTime(Started);
    }

    /// <summary>
    /// The wait between two polls of the browsers root, released by the same
    /// clock the caps are measured against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>It replaced <c>Thread.Sleep(_timers.Poll)</c> on 2026-08-20, and
    /// the replacement is what makes the clock seam usable at all.</b> A loop
    /// whose arithmetic reads an injected clock and whose sleep reads the wall
    /// clock cannot be driven: a test advancing a frozen clock would be
    /// advancing it while the product slept for a real second, so the test would
    /// be exactly as load-dependent as before and would additionally look
    /// deterministic.
    /// </para>
    /// <para>
    /// <b>A latching event rather than a wait on the timer itself</b>, because
    /// the tick can arrive while the loop is between the sample and the wait —
    /// with <see cref="TimeProvider.System"/> that is ordinary scheduling, and
    /// with a manual clock it is the normal case, since a test advances the
    /// clock from inside the sampler the loop calls. An
    /// <see cref="AutoResetEvent"/> keeps that signal, so the wait returns at
    /// once instead of missing a tick and stopping for ever.
    /// </para>
    /// <para>
    /// <b>It also wakes on shutdown</b>, so the two waits this replaced keep the
    /// property they had: a process going down does not sit in a poll interval
    /// first.
    /// </para>
    /// </remarks>
    private sealed class PollTicker : IDisposable
    {
        private readonly AutoResetEvent _tick = new(initialState: false);
        private readonly ITimer _timer;

        /// <summary>Arms a ticker.</summary>
        /// <param name="clock">The clock that releases each poll.</param>
        /// <param name="period">How long between polls.</param>
        public PollTicker(TimeProvider clock, TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(clock);

            _timer = clock.CreateTimer(_ => _ = _tick.Set(), null, period, period);
        }

        /// <summary>Waits for the next poll, or for shutdown.</summary>
        /// <param name="stopping">Cancelled when the process is going down.</param>
        public void WaitForNextPoll(CancellationToken stopping) =>
            _ = WaitHandle.WaitAny([_tick, stopping.WaitHandle]);

        /// <inheritdoc />
        public void Dispose()
        {
            _timer.Dispose();
            _tick.Dispose();
        }
    }

    /// <summary>One provisioning attempt in this process.</summary>
    /// <param name="Task">The background install.</param>
    /// <param name="Started">When it was started.</param>
    /// <param name="Phase">What it is doing right now, for the sentence a caller reads.</param>
    private sealed record Attempt(Task<ProvisioningResult> Task, DateTimeOffset Started, AttemptPhase Phase);

    /// <summary>
    /// Which half of an attempt is running, written by the installing thread and
    /// read by whichever thread is answering a caller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This exists because the detail sentence was stating a fact that was
    /// not true.</b> Every path that has not finished rendered
    /// <i>"… is being downloaded into '…'"</i>, including the one where this
    /// process is <b>not</b> downloading anything: it lost the machine-wide
    /// provisioning mutex and is watching for the holder's marker. The holder may
    /// be downloading, or extracting, or walking every process on the machine
    /// inside <see cref="BrowserProvisioner.Prune"/> — and the loser cannot tell
    /// which, so it must not claim one.
    /// </para>
    /// <para>
    /// ⚠️ <b>The state word was renamed on 2026-08-18 (previously
    /// <c>downloading</c>, described here as "unchanged and deliberate").</b> The
    /// word being narrower than the state it names was the defect; the honest
    /// replacement is <c>provisioning</c>, and the maintainer took that decision
    /// in <c>QUESTIONS.md</c> §9. Nothing about the bucketing moved: every
    /// consumer still branches on <i>installed</i> / <i>not yet, work is in
    /// progress</i> / <i>failed</i>, and this loser still belongs in the middle
    /// one. <b>This class is still the discriminator</b> — the word says only
    /// <i>not yet</i>, so which of the five phases it is remains a question only
    /// the sentence answers.
    /// </para>
    /// </remarks>
    private sealed class AttemptPhase
    {
        private int _waitingForAnotherProcess;

        private long _written;
        private long _elapsedTicks;
        private int _extracting;
        private long _baseline = -1;

        /// <summary>Whether this attempt is waiting out another process rather than installing.</summary>
        public bool IsWaitingForAnotherProcess => Volatile.Read(ref _waitingForAnotherProcess) is not 0;

        /// <summary>
        /// The last sample the watching thread took, as a value a caller-facing
        /// sentence can quote.
        /// </summary>
        /// <remarks>
        /// <b>Three interlocked reads rather than one lock, and they can be
        /// microseconds apart.</b> That is acceptable and it is worth saying why:
        /// the three come from one poll a second, so the worst skew is a byte
        /// count from one second paired with an elapsed time from the next. A
        /// lock here would serialise every caller against a sampler that runs
        /// while a 203.8 MB download is in flight, to remove an error smaller
        /// than the poll interval.
        /// </remarks>
        /// <param name="downloadBytes">What the family's whole download weighs.</param>
        /// <returns>The reading, or <see langword="null"/> before the first poll.</returns>
        public ProvisioningProgress? Reading(long downloadBytes)
        {
            var elapsed = Volatile.Read(ref _elapsedTicks);

            return elapsed is 0
                ? null
                : new ProvisioningProgress(
                    Volatile.Read(ref _written),
                    downloadBytes,
                    TimeSpan.FromTicks(elapsed),
                    Volatile.Read(ref _extracting) is not 0);
        }

        /// <summary>Records one poll of the browsers root.</summary>
        /// <param name="bytes">What it weighed.</param>
        /// <param name="elapsed">How long the attempt has been running.</param>
        /// <param name="extracting">Whether the revision directory has appeared.</param>
        public void Observed(long bytes, TimeSpan elapsed, bool extracting)
        {
            if (Volatile.Read(ref _baseline) < 0)
            {
                // The first poll is the baseline, and it is taken from the same
                // sampler rather than from a second measurement: what this
                // attempt has written is what has appeared SINCE it started, and
                // on a machine that already holds the other family the root is
                // not empty.
                Volatile.Write(ref _baseline, bytes);
            }

            Volatile.Write(ref _written, Math.Max(0, bytes - Volatile.Read(ref _baseline)));
            Volatile.Write(ref _extracting, extracting ? 1 : 0);

            // Last, because it is the field that makes a reading valid: a caller
            // that saw a non-zero elapsed time has already seen the other two.
            // Never zero, so the very first poll of a run that has taken no
            // measurable time still publishes.
            Volatile.Write(ref _elapsedTicks, Math.Max(1, elapsed.Ticks));
        }

        /// <summary>Records that the machine-wide mutex went to somebody else.</summary>
        public void EnterWait() => Volatile.Write(ref _waitingForAnotherProcess, 1);

        /// <summary>Records that this thread now holds the mutex and is installing.</summary>
        public void LeaveWait() => Volatile.Write(ref _waitingForAnotherProcess, 0);
    }

    /// <summary>How one attempt ended.</summary>
    private sealed record ProvisioningResult(bool Succeeded, string Detail);
}

/// <summary>One running installer, seen only through what the watcher needs.</summary>
/// <remarks>
/// Deliberately four members. Anything richer would let the watcher reach past
/// the seam into a <c>Process</c>, and the point of the seam is that everything
/// deciding behaviour stays on the provisioner's side of it.
/// </remarks>
internal interface IInstallerRun : IDisposable
{
    /// <summary>Whether the installer has finished, however it finished.</summary>
    bool HasExited { get; }

    /// <summary>Its exit code, valid once <see cref="HasExited"/> is true.</summary>
    int ExitCode { get; }

    /// <summary>Everything it has written so far, for a failure message to quote.</summary>
    string Output { get; }

    /// <summary>Stops it and everything it started.</summary>
    void Stop();
}

/// <summary>
/// The real installer: upstream's own <c>install-browser</c>, run out of the
/// payload, inside a job.
/// </summary>
/// <remarks>
/// <para>
/// <b>The job is not optional.</b> Upstream forks a separate
/// <c>oopBrowserDownload.js</c> process to do the transfer, so terminating the
/// <c>node</c> we started would leave a grandchild pulling 200 MB with nobody to
/// receive it. Closing the job takes the whole tree, which is the same mechanism
/// every other child in this product is contained by.
/// </para>
/// <para>
/// <b>Both streams are pumped from the moment the process starts.</b> A child
/// whose pipe buffer fills stops making progress, and a download stalled on its
/// own stdout is indistinguishable from a network stall — which is precisely the
/// failure the caps above would then mis-attribute.
/// </para>
/// </remarks>
internal sealed class NodeInstallerRun : IInstallerRun
{
    private readonly JobObject _job;
    private readonly LaunchedProcess _process;
    private readonly StringBuilder _output = new();
    private int _disposed;

    /// <summary>Starts the installer.</summary>
    /// <param name="payload">Where <c>node.exe</c> and <c>cli.js</c> live.</param>
    /// <param name="browser">The family to install.</param>
    /// <param name="browsersDirectory">The absolute browsers root.</param>
    public NodeInstallerRun(PayloadLayout payload, string browser, string browsersDirectory)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(browsersDirectory);
        payload.Verify();

        var downloads = DownloadDirectory(browsersDirectory, browser);

        _job = JobObject.CreateKillOnClose();

        try
        {
            _process = JobLauncher.Start(
                _job,
                payload.NodeExecutable,
                [
                    payload.PlaywrightMcpCli,
                    "install-browser",
                    browser,

                    // Load-bearing: chrome-headless-shell is never provisioned,
                    // which is what makes the chromium-alias channel in the
                    // generated config mandatory rather than a preference.
                    "--no-shell",

                    // A progress bar on a pipe nobody renders is noise in the log
                    // and bytes in a buffer.
                    "--no-progress",
                ],
                browsersDirectory,

                // The same allowlist every other child gets, plus the absolute
                // browsers root. Note what it does NOT carry: no
                // PLAYWRIGHT_DOWNLOAD_HOST variant, so the five retries rotate
                // through whatever mirror list upstream has for that download,
                // and no PLAYWRIGHT_DOWNLOAD_CONNECTION_TIMEOUT, so the
                // per-socket stall timeout stays upstream's 30 s.
                //
                // Corrected 2026-08-17 (previously "the five retries really do
                // rotate through upstream's mirror list"). Chrome for Testing
                // resolves through `cftUrl`, whose list is ONE host, so the
                // rotation protects ffmpeg, winldd and Firefox and not the
                // 202 MB half. The strip is still right; the sentence claimed
                // more than the measurement. §A carried this correction from
                // 2026-08-16 and it was never swept into the shipped comments.
                ChildEnvironment.Build(
                [
                    new KeyValuePair<string, string>(ChildLaunch.BrowsersPathVariable, browsersDirectory),

                    // ⚠️ Added 2026-08-19, and it is what makes provisioning
                    // MEASURABLE. Upstream downloads into
                    // `os.tmpdir()\playwright-download-XXXXXX\`, which on
                    // Windows is whatever TEMP says -- so for the whole of the
                    // download phase nothing under the browsers root grows, and
                    // a stall detector reading the root would kill every install
                    // on a slow link. Pointed here, the archive and the extracted
                    // tree are one number under one directory BrowserAI owns.
                    //
                    // Both names, because Node reads TEMP first and TMP second
                    // and an inherited TMP would otherwise win on a machine where
                    // only TMP is set.
                    new KeyValuePair<string, string>("TEMP", downloads),
                    new KeyValuePair<string, string>("TMP", downloads),
                ]));
        }
        catch
        {
            _job.Dispose();
            throw;
        }

        Pump(_process.StandardOutput);
        Pump(_process.StandardError);
    }

    /// <summary>
    /// Empties and re-creates the directory this install downloads into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Emptied first, and that is not tidiness.</b> Upstream removes its own
    /// <c>playwright-download-XXXXXX</c> in a <c>finally</c>, which does not run
    /// when BrowserAI closes the job on a cap — so without this, a stopped
    /// install leaves a partial archive of up to 202 MB behind and the next run's
    /// byte count starts from somebody else's residue. Measured on this machine
    /// 2026-08-19: the user's own <c>%TEMP%</c> held a
    /// <c>playwright-download-PRU23e</c> abandoned three days earlier, with
    /// 128,684 B still in it.
    /// </para>
    /// <para>
    /// ⚠️ <b>PER FAMILY, and that is what makes emptying it safe.</b> The
    /// provisioning mutex is keyed on the family, so a chromium install and a
    /// firefox install run at once by design — and this constructor runs
    /// <i>before</i> the child starts, which is before upstream's
    /// <c>__dirlock</c> is taken, so a shared directory would have the second
    /// installer delete the first one's archive mid-download. On Windows that
    /// delete would in fact fail on the open handle and the archive would
    /// survive; relying on that is relying on a sharing rule instead of on a
    /// design. Two directories cannot collide at all.
    /// </para>
    /// <para>
    /// <b>Through <c>TreeDelete</c>, like every other tree delete in this
    /// product</b>, and its failures are deliberately ignored: a residue that
    /// will not go is a byte count that starts high, not a reason to refuse an
    /// install.
    /// </para>
    /// </remarks>
    /// <param name="browsersDirectory">The absolute browsers root.</param>
    /// <param name="browser">The family being installed, which is this run's own subdirectory.</param>
    /// <returns>The absolute download directory.</returns>
    private static string DownloadDirectory(string browsersDirectory, string browser)
    {
        var downloads = Path.Combine(
            browsersDirectory,
            BrowserProvisioner.DownloadDirectoryName,
            Path.GetFileName(browser));

        TreeDelete.Remove(downloads, []);
        _ = Directory.CreateDirectory(downloads);

        return downloads;
    }

    /// <inheritdoc />
    public bool HasExited
    {
        get
        {
            if (!_process.HasExited)
            {
                return false;
            }

            // Cached as an int the moment it exists: Process.ExitCode's
            // equivalent throws after disposal, which turns "why did the
            // installer die" into an exception on the reporting path.
            ExitCode = _process.TryReadExitCode() ?? ExitCode;
            return true;
        }
    }

    /// <inheritdoc />
    public int ExitCode { get; private set; } = -1;

    /// <inheritdoc />
    public string Output
    {
        get
        {
            lock (_output)
            {
                return _output.ToString();
            }
        }
    }

    /// <inheritdoc />
    public void Stop() => _job.Dispose();

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        _process.Dispose();

        // Last: closing the job is what kills anything still running, and the
        // process object's streams should be closed before that happens.
        _job.Dispose();
    }

    private void Pump(Stream stream) =>
        _ = Task.Run(async () =>
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Length is 0)
                {
                    continue;
                }

                lock (_output)
                {
                    _ = _output.AppendLine(line);
                }
            }
        });
}

/// <summary>What <c>browserai_reinstall_browser</c> did.</summary>
/// <param name="Browser">The family.</param>
/// <param name="Directory">The tree that was removed and re-created.</param>
/// <param name="Deleted">
/// Whether the delete happened at all.
/// <para>
/// ⚠️ <b>Added 2026-08-18, with the provisioning mutex.</b> Before it, "nothing
/// was deleted" and "everything deleted and the download failed" were the same
/// shape — no failures, and a status that is not <c>Installed</c> — so the
/// caller was told <i>"'…' was deleted (0.0 MiB) and the download that should
/// have replaced it did not complete, so there is no browser installed now"</i>
/// about a tree that is entirely intact. An answer that asserts a destructive
/// act which did not happen is the failure class this product exists to remove,
/// and it arrived in the same change that added the refusal.
/// </para>
/// </param>
/// <param name="RemovedBytes">What was there before, or -1 when it could not be measured.</param>
/// <param name="Failures">What would not delete, one line each. Empty on the ordinary path.</param>
/// <param name="Status">Where the family stands now.</param>
internal sealed record ReinstallOutcome(
    string Browser,
    string Directory,
    bool Deleted,
    long RemovedBytes,
    IReadOnlyList<string> Failures,
    ProvisioningStatus Status)
{
    /// <summary>
    /// Every tree this call removed and re-created, which is one for a family and
    /// two for <see cref="ProvisionedBrowsers.Shared"/>.
    /// </summary>
    /// <remarks>
    /// <b>Defaulted to <see cref="Directory"/> so the family path is unchanged.</b>
    /// Added 2026-08-19 with the shared target, and added as a list rather than by
    /// making <c>Directory</c> a joined string: <c>Directory</c> is what the
    /// provisioning log records and what a status carries, and a log line naming
    /// two paths at once is not a path.
    /// </remarks>
    public IReadOnlyList<string> Directories { get; init; } = [Directory];

    /// <summary>Those trees as a sentence names them, between the caller's own quotes.</summary>
    /// <remarks>
    /// Renders identically to <see cref="Directory"/> for one tree, which is why
    /// every message could move to it without the family answers changing a byte.
    /// </remarks>
    public string Named => string.Join("' and '", Directories);
}

/// <summary>Source-generated log messages for provisioning.</summary>
/// <remarks>Event ids start at 60, after <see cref="Sessions.SessionToolLog"/>'s 40s.</remarks>
internal static partial class ProvisioningLog
{
    /// <summary>A download has started.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="revision">The revision the payload's browsers.json names.</param>
    /// <param name="root">The browsers root.</param>
    [LoggerMessage(
        EventId = 60,
        Level = LogLevel.Information,
        Message = "Provisioning {Browser} revision {Revision} into {Root}. init does not wait for this; browser calls are refused until it lands.")]
    public static partial void Downloading(ILogger logger, string browser, string revision, string root);

    /// <summary>The browser's own directory appeared, so extraction has begun.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="afterSeconds">How long the download took.</param>
    [LoggerMessage(
        EventId = 61,
        Level = LogLevel.Information,
        Message = "Extracting {Browser}; the download took {AfterSeconds} s.")]
    public static partial void Extracting(ILogger logger, string browser, int afterSeconds);

    /// <summary>A download finished and left the marker behind.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="revision">The revision.</param>
    /// <param name="seconds">How long the whole install took.</param>
    /// <param name="directory">Where it landed.</param>
    [LoggerMessage(
        EventId = 62,
        Level = LogLevel.Information,
        Message = "Provisioned {Browser} revision {Revision} in {Seconds} s into {Directory}.")]
    public static partial void Installed(ILogger logger, string browser, string revision, int seconds, string directory);

    /// <summary>Everything the installer wrote, kept for the log rather than discarded.</summary>
    /// <remarks>
    /// Its lines name the exact CDN URL each component came from, which is the
    /// only record of <i>which mirror</i> a machine actually reached — the thing
    /// nobody can reconstruct afterwards when a download fails behind a proxy.
    /// </remarks>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="output">What it said.</param>
    [LoggerMessage(EventId = 63, Level = LogLevel.Debug, Message = "install-browser {Browser} wrote: {Output}")]
    public static partial void InstallerSaid(ILogger logger, string browser, string output);

    /// <summary>The installer exited badly, or left no marker.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="exitCode">Its exit code.</param>
    /// <param name="tail">The tail of what it wrote.</param>
    [LoggerMessage(
        EventId = 64,
        Level = LogLevel.Error,
        Message = "The installer for {Browser} exited {ExitCode} without completing. {Tail}")]
    public static partial void InstallerRefused(ILogger logger, string browser, int exitCode, string tail);

    /// <summary>One of the caps fired and the installer was stopped.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="cap">Which cap.</param>
    /// <param name="minutes">What it was set to.</param>
    [LoggerMessage(
        EventId = 65,
        Level = LogLevel.Error,
        Message = "The {Cap} cap of {Minutes} minutes fired while provisioning {Browser}; its job was closed and the partial tree removed.")]
    public static partial void CapReached(ILogger logger, string browser, string cap, int minutes);

    /// <summary>Another process holds the machine-wide provisioning mutex.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    [LoggerMessage(
        EventId = 66,
        Level = LogLevel.Information,
        Message = "Another BrowserAI process is already provisioning {Browser}; watching for its marker rather than downloading a second copy.")]
    public static partial void AnotherProcessIsInstalling(ILogger logger, string browser);

    /// <summary>
    /// The process holding the provisioning mutex is alive and has written
    /// nothing for a whole stall cap.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Replaced <c>OuterDeadlineReached</c> on 2026-08-19, at the same
    /// event id.</b> That record said <i>"this is past every cap, so it is a
    /// defect rather than a slow link"</i>, which was true while an installer
    /// capped itself on total time and is not true now: there is no total any
    /// more, so the only thing reaching this line proves is that the holder has
    /// stopped writing. <c>Critical</c> is kept — it still means an install
    /// nobody is going to finish.
    /// </remarks>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="minutes">The stall cap that expired.</param>
    [LoggerMessage(
        EventId = 67,
        Level = LogLevel.Critical,
        Message = "The process provisioning {Browser} still holds the mutex and has written nothing under the browsers root for {Minutes} minutes. This process stopped waiting; it cannot stop that install, which belongs to another process.")]
    public static partial void HolderStalled(ILogger logger, string browser, int minutes);

    /// <summary>The previous holder of the provisioning mutex died mid-install.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="directory">The tree it left.</param>
    [LoggerMessage(
        EventId = 68,
        Level = LogLevel.Warning,
        Message = "The process that was provisioning {Browser} died holding the mutex; removing whatever it left in {Directory} before starting again.")]
    public static partial void AbandonedInstallFound(ILogger logger, string browser, string directory);

    /// <summary>A reinstall was asked for and is deleting the tree.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="directory">The tree.</param>
    [LoggerMessage(
        EventId = 69,
        Level = LogLevel.Warning,
        Message = "browserai_reinstall_browser is deleting {Directory} and re-provisioning {Browser}. Nothing on this machine has a browser open from it.")]
    public static partial void Reinstalling(ILogger logger, string browser, string directory);

    /// <summary>
    /// Provisioning threw rather than failing, which is the one shape the caller
    /// cannot infer from a state.
    /// </summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="exception">Why.</param>
    [LoggerMessage(
        EventId = 70,
        Level = LogLevel.Error,
        Message = "Provisioning {Browser} failed. The state is reported to the caller; this is the cause.")]
    public static partial void Failed(ILogger logger, string browser, Exception exception);

    /// <summary>
    /// The holder of the provisioning mutex released it without leaving a
    /// complete tree, so this process installs after all.
    /// </summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="directory">The revision directory that is still incomplete.</param>
    [LoggerMessage(
        EventId = 71,
        Level = LogLevel.Information,
        Message = "The process that held the {Browser} provisioning mutex let go without completing {Directory}, so this process is installing it rather than waiting for a marker that is not coming.")]
    public static partial void TheOtherProcessLetGoWithoutInstalling(ILogger logger, string browser, string directory);

    /// <summary>
    /// A reinstall did not delete anything, because an install is in flight.
    /// </summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="browser">The family.</param>
    /// <param name="directory">The revision directory that was left alone.</param>
    [LoggerMessage(
        EventId = 72,
        Level = LogLevel.Warning,
        Message = "browserai_reinstall_browser did not delete {Directory}: another BrowserAI process holds the {Browser} provisioning mutex, so an installer is extracting into that tree right now. Deleting it here would leave a directory that is neither install, which upstream would then mark complete.")]
    public static partial void ReinstallDeferred(ILogger logger, string browser, string directory);
}
