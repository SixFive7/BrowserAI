// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Frozen;

namespace BrowserAI.Protocol;

/// <summary>
/// The environment block a <c>@playwright/mcp</c> child is started with. An
/// allowlist: a variable the child sees is one named here.
/// </summary>
/// <remarks>
/// <para>
/// <b>It has to be an allowlist rather than a strip-list.</b> Upstream reads
/// <b>47</b> <c>PLAYWRIGHT_MCP_*</c> variables, three of them outside its own
/// config mapping, and the merge order is config file → environment → CLI — so
/// an inherited variable silently overrides a key BrowserAI generated, with no
/// error anywhere. Naming what may pass makes the next variable upstream adds
/// absent by default; naming what may not makes it present, and nothing says
/// so.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-09-17 @ <c>playwright-core</c>
/// 1.64.0-alpha-2026-09-14 (previously "<b>43</b> … variables, two of them
/// outside its own config mapping").</b> <b>Reconciled rather than
/// re-measured, and the difference matters.</b> The figure above is now
/// [re-verification row 17](../../../kb/re-verification.md)'s, taken on
/// 2026-09-15 against the bundle that actually ships, with
/// the previous bundle as the positive control — it returned 41 + 2 = 43
/// exactly as this sentence carried, which is what says the old number was
/// right for its own version rather than wrong. <c>@playwright/mcp</c> 0.0.81
/// added <c>PLAYWRIGHT_MCP_IDLE_TIMEOUT</c>, inside the mapping (41 → 42), and
/// <c>PLAYWRIGHT_MCP_PROFILE_DIR_NAME</c>, read straight off
/// <c>process.env</c> in the <c>--extension</c> channel resolver and therefore
/// joining the <b>outside</b> set (2 → 3). <b>Two records held two numbers for
/// two days, each correct about a different version</b>, and neither could see
/// the other. <c>RecordedCountTests.TheUpstreamVariableCountInTheDocCommentIsWhatRowSeventeenSays</c>
/// is what stops that recurring: it holds this sentence to the row, in that
/// direction, because the row names the bundle and the control and this is
/// prose beside an allowlist. <b>Neither of them can tell you the number is
/// right</b> — only a re-measurement against the resolved bundle does that.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-09-21 @ <c>playwright-core</c>
/// 1.64.0-alpha-1789764292000 (previously "<b>46</b> … variables, three of them
/// outside its own config mapping").</b> The one addition is
/// <c>PLAYWRIGHT_MCP_WEBMCP</c>, which arrived with <c>@playwright/mcp</c>
/// 0.0.82's page-registered tool collection and is read <b>inside</b>
/// <c>configFromEnv</c>, so the mapping went 43 → 44 and the outside set is
/// unchanged at three — still <c>PING_TIMEOUT_MS</c>, <c>EXTENSION_TOKEN</c> and
/// <c>PROFILE_DIR_NAME</c>. <b>Re-measured against the resolved bundle with the
/// previous one as the positive control</b>, fetched with
/// <c>npm pack playwright-core@1.64.0-alpha-2026-09-17</c>: it returned
/// 43 + 3 = 46, exactly what this sentence carried, against 44 + 3 = 47 on the
/// bundle that ships. Nothing left the set.
/// <b><c>PLAYWRIGHT_MCP_WEBMCP</c> is deliberately NOT in <see cref="Refused"/></b>,
/// which is a different answer from the one <c>PLAYWRIGHT_MCP_FILE_PATHS</c>
/// got four days earlier and for a stated reason: that list names variables
/// that <i>override a key the config generator writes</i>, and this product
/// writes no <c>webmcp</c> key. The allowlist already makes it absent. Naming
/// it here would assert a decision about page-registered tool collection that
/// nobody has taken — see <c>upstream-review.json</c>, 2026-09-21.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-09-17 @ <c>playwright-core</c>
/// 1.64.0-alpha-2026-09-17 (previously "<b>45</b> … variables, three of them
/// outside its own config mapping").</b> The paragraph that stood here said the
/// next figure was already known and deliberately not written in, because the
/// alpha carrying it "is not what any released <c>@playwright/mcp</c> pins".
/// That is still true of the wrapper and is no longer true of this build: the
/// alpha is reached through the
/// [dated override](../../../DECISIONS.md#the-two-exceptions-to-the-versioning-policy),
/// so the number is now about the version that ships and belongs here.
/// <b>Re-measured rather than taken from that paragraph</b>, with
/// 1.64.0-alpha-2026-09-14 as the positive control — it returned 42 + 3 = 45,
/// exactly what the previous sentence carried — against 43 + 3 = 46 on the
/// bundle that ships. The one addition is <c>PLAYWRIGHT_MCP_FILE_PATHS</c>,
/// inside the mapping, and it is in <see cref="Refused"/> rather than merely
/// absent, because the config generator writes <c>filePaths</c> explicitly.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-09-14 @ <c>playwright-core</c>
/// 1.63.0-alpha-2026-08-31 (previously "<b>42</b> … variables").</b>
/// <b>Re-measured rather than incremented</b>, with the old bundle as the
/// positive control: distinct <c>PLAYWRIGHT_MCP_*</c> names read out of
/// <c>coreBundle.js</c> came back <b>40 in the <c>e.PLAYWRIGHT_MCP_*</c> config
/// mapping plus 2 outside it = 42</b> on 1.63.0-alpha-2026-08-05, which is
/// exactly the figure this sentence carried, and <b>41 + 2 = 43</b> on the
/// version that now ships. The one addition is <c>PLAYWRIGHT_MCP_CODEGEN</c>,
/// inside the mapping; the two outside it are still
/// <c>PLAYWRIGHT_MCP_PING_TIMEOUT_MS</c> and
/// <c>PLAYWRIGHT_MCP_EXTENSION_TOKEN</c>. <b>Nothing here needed a code
/// change, and that is the allowlist working rather than luck</b> — the new
/// variable is absent from a child by construction because it was never named
/// in <see cref="InheritedWhenSet"/>. It is deliberately <i>not</i> added to
/// <see cref="Refused"/>: that list names the variables that redirect a
/// decision the config generator already took, and the code-generation
/// language for recorded actions is not one of them.
/// </para>
/// <para>
/// <b><c>ProcessStartInfo.Environment</c> arrives pre-populated with the
/// inherited block and assignment merges into it</b>, so an allowlist that does
/// not call <c>Clear()</c> first is a no-op that reads like a policy.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-17 (previously "<c>DirectStdioClientTransport</c> is
/// the one caller and it clears").</b> It does not clear, because there is
/// nothing to clear: the child is never started through <c>ProcessStartInfo</c>
/// at all. What <see cref="Build"/> returns is passed whole to
/// <see cref="Interop.JobLauncher"/>, which writes it into the <c>CreateProcessW</c>
/// environment block under <c>CREATE_UNICODE_ENVIRONMENT</c> — so the allowlist
/// is the child's entire block <b>by construction</b> rather than by a call
/// somebody has to remember. The hazard above is real and is closed one step
/// further back than it asks; the sentence describing a <c>Clear()</c> that does
/// not happen was left behind by the move to <c>CreateProcessW</c> and stood for
/// as long as it existed. Found 2026-08-17 by <c>HazardIndexTests</c>, which
/// caught the same claim in the hazard index naming a method
/// (<c>DirectStdioClientTransport.BuildStartInfo</c>) that has never existed.
/// </para>
/// <para>
/// The four hazards this closes are all silent, and all measured:
/// <c>PLAYWRIGHT_DOWNLOAD_HOST</c> and its three per-browser variants collapse
/// the mirror list to a single host, turning the download's five retries into
/// five attempts at the same dead server; <c>PLAYWRIGHT_MCP_OUTPUT_MAX_SIZE</c>
/// makes the child evict files it did not create; a relative
/// <c>PLAYWRIGHT_BROWSERS_PATH</c> resolves against <c>INIT_CWD</c> first, which
/// npm sets to whatever ancestor invoked it; and
/// <c>PLAYWRIGHT_SKIP_VALIDATE_HOST_REQUIREMENTS</c> writes a line to stderr
/// merely by being set, which is enough on its own to trip the
/// error-shaped-stderr classifier.
/// </para>
/// </remarks>
internal static class ChildEnvironment
{
    /// <summary>
    /// Names inherited from this process when it has them, and omitted when it
    /// does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Windows group is not padding. A process handed an environment block
    /// without <c>SystemRoot</c> fails inside Win32 in ways that name neither
    /// the variable nor the block, and the temp and profile names are where
    /// Node and Chromium put files they must be able to write.
    /// </para>
    /// <para>
    /// <c>PATH</c> is deliberately included even though BrowserAI spawns
    /// everything by absolute path. Stripping it buys nothing here — the
    /// hazards above are all named variables, none of them <c>PATH</c> — and
    /// costs a class of failure that only appears on someone else's machine.
    /// </para>
    /// <para>
    /// The proxy and CA names are inherited because without them a machine behind
    /// TLS inspection cannot provision a browser at all — first-run provisioning
    /// downloads 207.3 MB from three hosts — <i>corrected 2026-09-17, previously
    /// "203.8 MB", and the live figure is
    /// <see cref="Runtime.BrowserProvisioner.FirstRunDownloadSizes"/> rather than
    /// this sentence</i> — and SOCKS is unsupported on that path
    /// regardless
    /// ([kb](../../../kb/playwright/provisioning-and-timings.md#first-run-provisioning)).
    /// </para>
    /// </remarks>
    public static FrozenSet<string> InheritedWhenSet { get; } = new[]
    {
        // Windows itself.
        "SystemRoot", "windir", "SystemDrive", "COMSPEC", "PATH", "PATHEXT",
        "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE", "PROCESSOR_IDENTIFIER", "OS",

        // Where a process is allowed to write.
        "TEMP", "TMP", "USERPROFILE", "LOCALAPPDATA", "APPDATA", "HOMEDRIVE", "HOMEPATH",
        "PUBLIC", "ProgramData", "ALLUSERSPROFILE", "ProgramFiles", "ProgramFiles(x86)",
        "ProgramW6432", "CommonProgramFiles", "CommonProgramFiles(x86)", "CommonProgramW6432",

        // Who and where, which Chromium reads directly.
        "USERNAME", "USERDOMAIN", "COMPUTERNAME", "SESSIONNAME",

        // Egress under a corporate proxy or TLS inspection.
        "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "ALL_PROXY", "NODE_EXTRA_CA_CERTS",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Names set on every child, whatever this process's own value is.</summary>
    /// <remarks>
    /// <c>PLAYWRIGHT_SKIP_BROWSER_GC</c> stops Playwright's stale-browser
    /// collector deleting any registry directory not referenced by a
    /// <c>.links</c> entry — against a tree BrowserAI provisioned, the blast
    /// radius of that sweep is "deletes our own Chromium". Pruning old revisions
    /// becomes BrowserAI's job as a direct consequence, and that obligation is
    /// discharged by <see cref="Runtime.RevisionPrune"/> on every successful
    /// provision — without it each <c>browsers.json</c> bump strands ~430 MiB per
    /// machine, forever.
    /// <c>PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD</c> keeps provisioning a decision
    /// BrowserAI makes rather than a side effect of the child starting.
    /// </remarks>
    public static FrozenDictionary<string, string> Forced { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["PLAYWRIGHT_SKIP_BROWSER_GC"] = "1",
        ["PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD"] = "1",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Names that must never reach a child, listed so that adding one to
    /// <see cref="InheritedWhenSet"/>, to <see cref="Forced"/>, or to a caller's
    /// own additions is a failure rather than a regression.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An allowlist already makes every one of these absent. Naming them is
    /// what turns "absent because nobody added it" into "absent because it is
    /// refused" — the difference between a property and an accident, and the
    /// only version of it a test can assert.
    /// </para>
    /// <para>
    /// ⚠️ <b>The one that had to be here and was not is
    /// <c>PLAYWRIGHT_MCP_ALLOW_UNRESTRICTED_FILE_ACCESS</c> — added
    /// 2026-08-26.</b> <see cref="Runtime.BrowserConfiguration"/> calls
    /// <c>allowUnrestrictedFileAccess: false</c> <i>the only containment this
    /// product has left</i>, and that variable is the environment route that
    /// switches it off: <c>playwright-core</c>'s <c>configFromEnv</c> maps it
    /// onto the same key, and the merge order is config file → environment →
    /// CLI, so it wins over the value the generator writes. It was not named on
    /// the day that key became load-bearing, which is exactly the gap this list
    /// exists to close. The four beside it redirect the same class of decision:
    /// a whole different config file, the allowed output root, and two scripts
    /// upstream loads into every page.
    /// </para>
    /// </remarks>
    public static FrozenSet<string> Refused { get; } = new[]
    {
        "INIT_CWD",
        "NODE_OPTIONS",
        "NODE_PATH",
        "DEBUG",
        "DEBUG_FILE",
        "PLAYWRIGHT_MCP_OUTPUT_MAX_SIZE",

        // Named rather than merely absent. `capabilities` REPLACES rather than
        // merges, so this variable silently wipes the capability list the config
        // generator writes -- and it is an environment route to the bug that a
        // "never pass --caps" rule does not close. A capability set to nothing is
        // a tool surface that shrank with no error anywhere.
        "PLAYWRIGHT_MCP_CAPS",

        // The six that override a key the config generator writes, read out of
        // the shipped `coreBundle.js`'s own `configFromEnv` rather than from a
        // changelog. The first is the one the product cannot afford: it is
        // `allowUnrestrictedFileAccess`, and turning it on gives the child every
        // path on the machine instead of the session's own `output\`.
        //
        // ⚠️ SIX SINCE 2026-09-17, previously five. PLAYWRIGHT_MCP_FILE_PATHS
        // maps onto `filePaths`, which the generator now writes as `absolute`.
        // An inherited `relative` would not fail -- it would put the child back
        // to naming every artifact against a working directory the reader does
        // not have, which is the defect that key was adopted to end, and nothing
        // anywhere would say so.
        "PLAYWRIGHT_MCP_ALLOW_UNRESTRICTED_FILE_ACCESS",
        "PLAYWRIGHT_MCP_CONFIG",
        "PLAYWRIGHT_MCP_OUTPUT_DIR",
        "PLAYWRIGHT_MCP_INIT_SCRIPT",
        "PLAYWRIGHT_MCP_INIT_PAGE",
        "PLAYWRIGHT_MCP_FILE_PATHS",

        "PLAYWRIGHT_DOWNLOAD_HOST",
        "PLAYWRIGHT_CHROMIUM_DOWNLOAD_HOST",
        "PLAYWRIGHT_FIREFOX_DOWNLOAD_HOST",
        "PLAYWRIGHT_WEBKIT_DOWNLOAD_HOST",
        "PLAYWRIGHT_SKIP_VALIDATE_HOST_REQUIREMENTS",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the block for one child: the inherited names this process has,
    /// then <see cref="Forced"/>, then the caller's own additions.
    /// </summary>
    /// <param name="additional">
    /// Variables this particular child needs — an absolute
    /// <c>PLAYWRIGHT_BROWSERS_PATH</c>, for instance. Later entries win over
    /// <see cref="Forced"/> only if they name something else; a
    /// <see cref="Refused"/> name throws.
    /// </param>
    /// <returns>The child's complete environment.</returns>
    /// <exception cref="ArgumentException"><paramref name="additional"/> names a refused variable.</exception>
    public static Dictionary<string, string> Build(IEnumerable<KeyValuePair<string, string>>? additional = null)
    {
        // Ordinal-ignore-case, because that is what Windows itself does with an
        // environment block. An ordinal comparer here would let `Path` and
        // `PATH` both through as separate entries, and a refused name reach the
        // child under a different casing.
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in InheritedWhenSet)
        {
            if (Environment.GetEnvironmentVariable(name) is { } value)
            {
                environment[name] = value;
            }
        }

        foreach (var (name, value) in Forced)
        {
            environment[name] = value;
        }

        foreach (var (name, value) in additional ?? [])
        {
            if (Refused.Contains(name))
            {
                throw new ArgumentException(
                    $"'{name}' is refused for every child process and cannot be added to one. See ChildEnvironment.Refused.",
                    nameof(additional));
            }

            environment[name] = value;
        }

        return environment;
    }
}
