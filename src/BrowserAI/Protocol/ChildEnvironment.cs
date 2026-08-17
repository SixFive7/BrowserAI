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
/// <b>42</b> <c>PLAYWRIGHT_MCP_*</c> variables, two of them outside its own
/// config mapping, and the merge order is config file → environment → CLI — so
/// an inherited variable silently overrides a key BrowserAI generated, with no
/// error anywhere. Naming what may pass makes the next variable upstream adds
/// absent by default; naming what may not makes it present, and nothing says
/// so.
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
/// caught the same claim in <c>plan/hazards.md</c> naming a method
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
    /// The proxy and CA names are required by
    /// <c>plan/A-runtime.md</c>: without them a machine behind TLS inspection
    /// cannot provision a browser at all, and SOCKS is unsupported on that path
    /// regardless.
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
    /// An allowlist already makes every one of these absent. Naming them is
    /// what turns "absent because nobody added it" into "absent because it is
    /// refused" — the difference between a property and an accident, and the
    /// only version of it a test can assert.
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
