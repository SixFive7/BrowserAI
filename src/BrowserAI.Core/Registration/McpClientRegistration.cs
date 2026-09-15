// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Registration;

/// <summary>
/// ⚠️ <b>THE ONE PLACE THAT DECIDES HOW BROWSERAI IS REGISTERED.</b> Change this
/// file to change the mechanism; nothing else in the product knows what a client
/// configuration looks like.
/// </summary>
/// <remarks>
/// <para>
/// <b>The requirement is one sentence, and it is
/// [the charter's](../../../DECISIONS.md#locking-logging-versioning-and-registration)</b> —
/// <i>"registered once at system or user scope, available in every repository,
/// with no per-repo files"</i> — and it is the founding promise:
/// [DECISIONS §1](../../../DECISIONS.md#1-there-is-no-update-path-and-that-is-the-actual-problem) opens by rejecting a world where onboarding
/// <i>"requires a repo, a `.mcp.json`, hook registrations"</i>. Until this
/// existed, what shipped was an installed, self-updating, self-sweeping binary
/// that no client was configured to talk to.
/// </para>
/// <para>
/// <b>The mechanism is the client's own supported command:
/// <c>claude mcp add --scope user</c>.</b> User scope <i>is</i> the charter's
/// sentence — one registration, available in every repository, writing no file
/// into any of them. Decided 2026-08-16 in the maintainer's absence, having been
/// asked twice, because the product is unusable without it.
/// </para>
/// <para>
/// <b>The three alternatives, and why they lost.</b>
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Write the client's configuration file directly.</b> Means owning another
/// product's on-disk format <i>and</i> its merge semantics forever — a second
/// surface to re-review on every client release, for a file the maintainer edits
/// daily and cannot afford to have rewritten by a background installer.
/// </description></item>
/// <item><description>
/// <b>A registry key.</b> Only some clients read one, so it registers with
/// nothing on the machine this was written for.
/// </description></item>
/// <item><description>
/// <b>A documented manual step.</b> Abandons the promise that distinguishes this
/// product from the setup it replaces — the whole complaint in
/// [DECISIONS §7](../../../DECISIONS.md#7-distribution-to-colleagues-has-no-story) is that onboarding is a list of manual steps.
/// </description></item>
/// </list>
/// <para>
/// <b>What a replacement has to keep.</b> Whatever mechanism is next must be
/// idempotent (install, update, repair and reinstall must not produce two
/// registrations), must never fail an install, must name
/// <c>current\BrowserAI.exe</c> and never the stub
/// (<see cref="RegistrationTarget"/>), and must finish well inside a fast-exit
/// hook's timeout — <c>--veloapp-install</c> gets 30 s, <c>--veloapp-updated</c>
/// 15 s, <c>--veloapp-uninstall</c> 60 s
/// ([kb](../../../kb/packaging/velopack.md#nativeaot-hooks-and-vpk-output)).
/// </para>
/// <para>
/// <b>Measured 2026-08-16 @ Claude Code 2.1.233</b>, three runs each, from a
/// non-elevated token: <c>mcp add --scope user</c> takes <b>613–645 ms</b> and
/// <c>mcp remove</c> <b>646–671 ms</b>, and neither needs elevation because both
/// write the invoking user's own configuration
/// ([kb](../../../kb/mcp/protocol.md#registering-browserai-with-the-client)).
/// </para>
/// </remarks>
internal static class McpClientRegistration
{
    /// <summary>
    /// The name the server is registered under, and therefore the prefix every
    /// tool the model sees carries.
    /// </summary>
    /// <remarks>
    /// Lower-case and unqualified, matching the product name. It is <b>not</b> a
    /// tool name — upstream tool names pass through byte-for-byte
    /// ([DECISIONS → Tool naming](../../../DECISIONS.md#licence-release-policy-and-the-tool-surface)) — it is the
    /// server key in the client's own configuration.
    /// </remarks>
    public const string ServerName = "browserai";

    /// <summary>
    /// The client's command-line executable, by file name only. Where it lives
    /// is <see cref="IRegistrationCommand.Locate"/>'s problem.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>An <c>.exe</c> and never a <c>.cmd</c> shim.</b> A shim cannot be
    /// started without <c>cmd.exe</c>, and routing through a shell is what
    /// [SDK deviation 1](../../../STACK.md#nine-places-where-the-sdk-must-be-deviated-from)
    /// exists to forbid: measured
    /// against a node probe, a literal <c>%USERNAME%</c> reached the child
    /// expanded and an argument containing whitespace and <c>&amp;</c> made the
    /// child fail to start outright. A registered path is exactly the kind of
    /// argument that carries spaces.
    /// </remarks>
    public const string ClientExecutable = "claude.exe";

    /// <summary>
    /// The same client as a person types it, for a message somebody is meant to
    /// paste.
    /// </summary>
    /// <remarks>
    /// Split from <see cref="ClientExecutable"/> deliberately: the search needs
    /// the extension and a reader does not, and one constant serving both
    /// produced a recovery line reading <c>claude.exe mcp add …</c> in one place
    /// and <c>claude mcp add …</c> in another.
    /// </remarks>
    public const string ClientCommandName = "claude";

    /// <summary>
    /// The scope: <c>user</c>, which <i>is</i> the sentence quoted on this type
    /// and not a preference.
    /// </summary>
    /// <remarks>
    /// <c>local</c> is per-project (the default, and exactly the per-repository
    /// world being replaced) and <c>project</c> writes a <c>.mcp.json</c> into
    /// the repository, which the charter rejects by name.
    /// </remarks>
    public const string UserScope = "user";

    /// <summary>
    /// The other scope BrowserAI writes: <c>project</c>, a <c>.mcp.json</c> at
    /// the root of one directory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Added 2026-09-15, and the sentence above about it is corrected
    /// rather than deleted (previously "<c>project</c> writes a
    /// <c>.mcp.json</c> into the repository, which the charter rejects by
    /// name").</b> What the charter rejects is BrowserAI <i>requiring</i> a
    /// per-repository file in order to work — being installed once and being
    /// available everywhere is the promise, and user scope is what keeps it. It
    /// does not reject a person choosing to commit one: a team that wants
    /// BrowserAI pinned in a repository, so that a teammate who clones it is
    /// offered the server without installing anything by hand, is asking for
    /// exactly what this scope is for.
    /// </para>
    /// <para>
    /// <b>It is never written without an explicit click</b>, never written to a
    /// directory BrowserAI chose, and the person is told what the client will do
    /// next: a project-scoped server is approved once per project, and until
    /// somebody approves it the entry is inert.
    /// </para>
    /// </remarks>
    public const string ProjectScope = "project";

    /// <summary>
    /// How long one client invocation may take before it is abandoned.
    /// </summary>
    /// <remarks>
    /// <b>Sized against the shortest hook, not against the measurement.</b> The
    /// tightest fast-exit budget is <c>--veloapp-updated</c>'s 15 s, and that
    /// hook makes one call; 10 s leaves the installer time to notice rather than
    /// being killed mid-write. The measurement — 613–645 ms — is fifteen times
    /// under it, which is the headroom rather than the target: a budget derived
    /// from the observed duration would go wrong on the first slow machine.
    /// </remarks>
    public static TimeSpan Budget => TimeSpan.FromSeconds(10);

    /// <summary>
    /// The arguments that register BrowserAI at user scope.
    /// </summary>
    /// <param name="command">The absolute path a client is to launch.</param>
    /// <returns>The argument vector, to be passed one element at a time.</returns>
    /// <remarks>
    /// <b><c>--</c> is load-bearing.</b> Everything after it is the command and
    /// its arguments rather than options, so a path that begins with a dash — or
    /// a future one carrying flags — cannot be re-read as an option by the
    /// client's own parser.
    /// </remarks>
    public static IReadOnlyList<string> AddArguments(string command) =>
        AddArguments(command, UserScope);

    /// <summary>
    /// The arguments that register BrowserAI in a named scope.
    /// </summary>
    /// <param name="command">The command a client is to launch.</param>
    /// <param name="scope"><see cref="UserScope"/> or <see cref="ProjectScope"/>.</param>
    /// <returns>The argument vector, to be passed one element at a time.</returns>
    /// <remarks>
    /// ⚠️ <b>A project-scope call is the same call with a different scope and a
    /// different WORKING DIRECTORY</b> — the client writes <c>.mcp.json</c>
    /// where it is run, and nowhere else. The command for that scope is written
    /// in its portable form, which is why it must reach the client as one
    /// argument through this vector and never be spelled into a shell line:
    /// both of this machine's shells expand <c>${…}</c> themselves, and what
    /// would then be committed is one person's absolute path.
    /// </remarks>
    public static IReadOnlyList<string> AddArguments(string command, string scope) =>
        ["mcp", "add", ServerName, "--scope", scope, "--", command];

    /// <summary>The arguments that remove BrowserAI from user scope.</summary>
    /// <returns>The argument vector, to be passed one element at a time.</returns>
    /// <remarks>
    /// <b>The scope is stated on the remove too.</b> Without it the client
    /// removes the entry <i>from whichever scope it exists in</i> — so an
    /// uninstall could delete a project-scoped server somebody else configured.
    /// </remarks>
    public static IReadOnlyList<string> RemoveArguments() => RemoveArguments(UserScope);

    /// <summary>The arguments that remove BrowserAI from a named scope.</summary>
    /// <param name="scope"><see cref="UserScope"/> or <see cref="ProjectScope"/>.</param>
    /// <returns>The argument vector, to be passed one element at a time.</returns>
    public static IReadOnlyList<string> RemoveArguments(string scope) =>
        ["mcp", "remove", ServerName, "--scope", scope];

    /// <summary>
    /// The portable form of an installed server's path, for a committed
    /// <c>.mcp.json</c>.
    /// </summary>
    /// <param name="packId">The Velopack pack id, which is the install folder.</param>
    /// <returns>The command, with the environment reference unexpanded.</returns>
    /// <remarks>
    /// <para>
    /// <b>The client expands <c>${VAR}</c> inside <c>.mcp.json</c> and this is
    /// the only reason the form is usable.</b> A committed absolute path under
    /// one person's user profile is wrong on every teammate's machine and right
    /// on exactly one, which makes committing it worse than committing nothing.
    /// </para>
    /// <para>
    /// ⚠️ <b>It is only written when it expands to the install this process is
    /// running out of.</b> A non-default install root — <c>Setup.exe</c> with an
    /// install-to argument — does not sit under <c>%LOCALAPPDATA%</c>, and
    /// writing this form there would commit a path that resolves to nothing on
    /// the very machine that wrote it. That case gets its absolute path and the
    /// person is told why.
    /// </para>
    /// <para>
    /// <b>Forward slashes</b>, because JSON is where this lands and a backslash
    /// is an escape there; the client and Windows both accept them.
    /// </para>
    /// </remarks>
    public static string PortableCommandFor(string packId) =>
        $"${{LOCALAPPDATA}}/{packId}/{RegistrationTarget.CurrentDirectoryName}/{RegistrationTarget.ServerFileName}";

    /// <summary>
    /// Whether a failed <c>add</c> failed only because the entry was already
    /// there.
    /// </summary>
    /// <param name="exitCode">What the client exited with.</param>
    /// <param name="output">Everything it wrote, on both streams.</param>
    /// <returns>Whether this is the benign outcome.</returns>
    /// <remarks>
    /// ⚠️ <b>This reads upstream's English, because the client offers nothing
    /// else.</b> Measured 2026-08-16 @ 2.1.233: adding a name that already exists
    /// exits <b>1</b> and prints <c>MCP server browserai already exists in user
    /// config</c>, which is the same exit code every other failure uses. Being
    /// wrong here is safe in the direction that matters — a wording change makes
    /// this return <see langword="false"/>, which reports the pass as
    /// <see cref="RegistrationStatus.Failed"/> in the log and in the registration
    /// record, rather than reporting success for something that did not happen.
    /// <c>RegistrationTests.TheClientStillSaysWhatTheExitCodesCannot</c> asserts
    /// the wording against the real client, so the drift is a red test rather
    /// than a discovery in the field.
    /// </remarks>
    public static bool MeansAlreadyRegistered(int exitCode, string output) =>
        exitCode is not 0 && (output ?? string.Empty).Contains(AlreadyExistsNeedle, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a failed <c>remove</c> failed only because there was nothing to
    /// remove.
    /// </summary>
    /// <param name="exitCode">What the client exited with.</param>
    /// <param name="output">Everything it wrote, on both streams.</param>
    /// <returns>Whether this is the benign outcome.</returns>
    /// <remarks>
    /// Measured 2026-08-16 @ 2.1.233: removing a name that is not there exits
    /// <b>1</b> and prints <c>No MCP server named "browserai" in user scope</c>.
    /// An uninstall of a BrowserAI the user had already unregistered by hand must
    /// not report a failure.
    /// </remarks>
    public static bool MeansNothingToRemove(int exitCode, string output) =>
        exitCode is not 0 && (output ?? string.Empty).Contains(NotRegisteredNeedle, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The wording an <c>add</c> uses when the entry is already there, as
    /// measured. Asserted against the real client by the suite.
    /// </summary>
    public const string AlreadyExistsNeedle = "already exists";

    /// <summary>
    /// The wording a <c>remove</c> uses when there is nothing to remove, as
    /// measured. Asserted against the real client by the suite.
    /// </summary>
    public const string NotRegisteredNeedle = "No MCP server named";

    /// <summary>
    /// What a person is told to run when BrowserAI could not register itself.
    /// </summary>
    /// <param name="command">The path that would have been registered.</param>
    /// <returns>The command line, ready to paste.</returns>
    /// <remarks>
    /// <b>Every failure path names this.</b> A hook that swallows leaves a
    /// product nobody can reach and nothing to say so; a hook that throws breaks
    /// the installer. The third option is the one taken — fail visibly, and put
    /// the recovery in the same sentence as the failure.
    /// </remarks>
    public static string ManualCommandFor(string command) =>
        $"{ClientCommandName} mcp add {ServerName} --scope {UserScope} -- \"{command}\"";
}
