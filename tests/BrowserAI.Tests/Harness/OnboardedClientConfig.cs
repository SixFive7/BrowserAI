// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Tests.Harness;

/// <summary>
/// A scratch <c>CLAUDE_CONFIG_DIR</c> seeded so the real client reads it as a
/// configuration a human has already finished onboarding.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is for.</b> Three sites in this suite point
/// <c>CLAUDE_CONFIG_DIR</c> at a <i>fresh, empty</i> directory and then start the
/// real <c>claude.exe</c>. An empty configuration directory is, to that client,
/// a machine nobody has ever signed in on -- which is the shape in which it may
/// run its first-run onboarding and sign-in flow, and that flow opens a browser
/// window. The maintainer runs these arms on his own desktop. <b>It has not
/// happened</b>: no <c>hasCompletedOnboarding</c> read on the <c>mcp</c>
/// subcommand path exists in the client this was established against, and a
/// window seen on 2026-09-21 was traced elsewhere. This seeds the marker anyway,
/// because the cost of being wrong is a browser window on somebody's screen and
/// the cost of the guard is four lines.
/// </para>
/// <para>
/// <b>ESTABLISHED FROM THE CLI ITSELF, 2026-09-22 @ Claude Code 2.1.278</b>
/// (<c>BUILD_TIME</c> 2026-09-19T01:12:36Z, <c>GIT_SHA</c>
/// 809c980662e3525645594dc8b74f78c38a348db1, read out of the same bundle), by
/// reading the installed single-file bundle at
/// <c>C:\Users\&lt;user&gt;\.local\bin\claude.exe</c>. Three functions, quoted:
/// </para>
/// <code>
/// function s(){return process.env.CLAUDE_CONFIG_DIR}
/// var we=Jo(()=&gt;(s()??a(g(),".claude")).normalize("NFC"),s);
///
/// function ct(){if(le().existsSync(i(we(),".config.json")))return i(we(),".config.json");return EIn()}
/// function EIn(){let t=`.claude${wz()}.json`;return i(process.env.CLAUDE_CONFIG_DIR||Dt(),t)}
/// function Oa(){return O().getGlobalClaudeFile()}
/// </code>
/// <para>
/// So the global configuration file is <c>$CLAUDE_CONFIG_DIR\.config.json</c>
/// <i>if that file exists</i> and otherwise
/// <c>$CLAUDE_CONFIG_DIR\.claude&lt;suffix&gt;.json</c>, where <c>wz()</c>
/// returns the empty string on the <c>prod</c> channel and
/// <c>-custom-oauth</c> / <c>-local-oauth</c> / <c>-staging-oauth</c> otherwise.
/// A fresh scratch directory holds no <c>.config.json</c>, so
/// <see cref="FileName"/> is the file that is read.
/// </para>
/// <para>
/// <b>And the read that decides</b>, the first step of the REPL's dialog chain,
/// where <c>N</c> is the global configuration:
/// </para>
/// <code>
/// (B)=&gt;{if(N.hasCompletedOnboarding&amp;&amp;a.CLAUDE_CODE_POWERUP_ONBOARDING!=="banner"&amp;&amp;a.CLAUDE_CODE_POWERUP_ONBOARDING!=="step")return null;
/// return H.onboardingShown=!0,import("B:/~BUN/root/chunk-sm0cw74b.js").then(({Onboarding:G})=&gt;e(G,{host:h.host,onDone:()=&gt;{Ea(M),B()}}))}
/// </code>
/// <para>
/// <b>The value written here is the client's OWN recipe for a throwaway
/// configuration directory</b>, not one this repository invented. It is what
/// <c>claude plugin eval</c> writes when it builds its sandbox, quoted from the
/// same bundle -- <c>i</c> is the directory the function then returns as
/// <c>configDir</c>:
/// </para>
/// <code>
/// await gr(ue.join(i,".claude.json"),S({hasCompletedOnboarding:!0,autoUpdates:!1,bypassPermissionsModeAccepted:!1}))
/// </code>
/// <para>
/// ⚠️ <b>THE GUARD IS ASSERTED, NEVER MEASURED AGAINST THE FLOW.</b> Nothing
/// here proves that an unseeded directory would have opened a window, because
/// proving it means running the sign-in flow on the maintainer's desktop, which
/// is the event being guarded against. What is asserted is that the marker is
/// written before any <c>claude</c> invocation
/// (<see cref="BrowserAI.Tests.HouseRuleTests.EveryScratchClientConfigurationIsSeededAsOnboardedBeforeTheClientRuns"/>)
/// and that the file carries what the CLI reads
/// (<see cref="BrowserAI.Tests.RegistrationTests.TheScratchConfigurationIsSeededWithWhatTheClientReadsAsOnboarded"/>).
/// That is a weaker claim than a red test and is stated as one, on the
/// <c>DangerousAddRef</c> precedent in <c>CLAUDE.md</c>.
/// </para>
/// <para>
/// ⚠️ <b>THERE IS NO NON-INTERACTIVE SIGNAL TO SET INSTEAD, and that was
/// searched for, not assumed.</b> <c>claude mcp --help</c> and
/// <c>claude mcp add --help</c>, run 2026-09-22 against a seeded scratch
/// directory, carry no such flag: <c>add</c>'s options are
/// <c>--callback-port</c>, <c>--client-id</c>, <c>--client-secret</c>,
/// <c>-e/--env</c>, <c>-H/--header</c>, <c>-h/--help</c>, <c>-s/--scope</c> and
/// <c>-t/--transport</c>. The only onboarding-named environment variable in the
/// bundle is <c>CLAUDE_CODE_POWERUP_ONBOARDING</c>, and the line quoted above
/// shows it is a <b>force-on</b> and not a suppressor -- <c>"banner"</c> or
/// <c>"step"</c> shows onboarding even when the configuration says it is
/// complete. A grep for <c>CLAUDE_CODE_NONINTERACTIVE</c> over the bundle
/// returned <b>zero</b>, with <c>CLAUDE_CONFIG_DIR</c> at 74 hits and
/// <c>hasCompletedOnboarding</c> at 19 from the same grep over the same file as
/// the positive control. <b>The published documentation does not document the
/// marker at all</b>: the settings reference's global-config list does not carry
/// it, and the <c>claude-directory</c> page documents <c>~/.claude.json</c> with
/// no onboarding or first-run key. So the bundle is the only source there is,
/// and this fact is <c>[FLOATS]</c> -- it is a private key in a shipped binary.
/// </para>
/// </remarks>
internal static class OnboardedClientConfig
{
    /// <summary>The file the client keeps its global configuration in.</summary>
    /// <remarks>
    /// <c>.config.json</c> beside it would win if it existed, and in a fresh
    /// scratch directory it does not. See the remarks on this type.
    /// </remarks>
    public const string FileName = ".claude.json";

    /// <summary>The key the client reads as "onboarding is finished".</summary>
    public const string Marker = "hasCompletedOnboarding";

    /// <summary>
    /// What is written, character for character the client's own throwaway-config
    /// recipe.
    /// </summary>
    /// <remarks>
    /// The two keys beside the marker are not decoration. <c>autoUpdates: false</c>
    /// keeps a scratch run from starting the client's self-update, and
    /// <c>bypassPermissionsModeAccepted: false</c> records that no dangerous mode
    /// was ever accepted in this directory. Both are what <c>claude plugin
    /// eval</c> writes; neither is asserted, because what the guard is about is
    /// the marker.
    /// </remarks>
    public const string Contents =
        "{\"" + Marker + "\":true,\"autoUpdates\":false,\"bypassPermissionsModeAccepted\":false}";

    /// <summary>
    /// Seeds a scratch configuration directory and hands back its path, so that
    /// the seeding cannot be separated from the use.
    /// </summary>
    /// <remarks>
    /// It returns the path and not <see langword="void"/> on purpose: every
    /// site that points the client at a directory writes that path into an
    /// environment block, and threading it through here makes the seeding part
    /// of the expression the scan reads and not a line above it that can be
    /// deleted on its own.
    /// </remarks>
    /// <param name="directory">The scratch configuration directory.</param>
    /// <returns><paramref name="directory"/>, unchanged.</returns>
    public static string Seed(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _ = Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, FileName), Contents);

        return directory;
    }
}
