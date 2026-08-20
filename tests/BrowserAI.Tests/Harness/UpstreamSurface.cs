// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The tool names a child exposes for a given capability set, computed from the
/// committed <c>tools-list.json</c> snapshot rather than typed anywhere.
/// </summary>
/// <remarks>
/// <para>
/// The snapshot is regenerated from the resolved payload on every build and
/// diffed, so an upstream change is a snapshot diff first and a surface
/// assertion second. Computing the expected list from it means a test never
/// carries a list of tool names, which would be a hand-written copy of upstream's
/// surface — exactly what the scope boundary forbids.
/// </para>
/// <para>
/// <b>The <c>core*</c> family is unconditional.</b> Upstream ors
/// <c>capability.startsWith("core")</c> with whatever <c>capabilities</c> says,
/// so nothing can go below the default 24 and naming a <c>core*</c> capability
/// does nothing. <see cref="For"/> reproduces that, and
/// <c>UpstreamSnapshotTests.TheCapabilityFilterReproducesTheRecordedSurfaces</c>
/// is what keeps the reproduction honest: it recomputes the snapshot's own
/// <c>defaultSurface</c> from this helper and compares.
/// </para>
/// </remarks>
internal static class UpstreamSurface
{
    /// <summary>The tools a child exposes with these capabilities configured, in upstream's order.</summary>
    /// <param name="capabilities">The capabilities BrowserAI writes into the config.</param>
    /// <returns>The tool names, in the order the child returns them.</returns>
    public static IReadOnlyList<string> For(IEnumerable<string> capabilities)
    {
        using var snapshot = Snapshot();

        var enabled = new HashSet<string>(
            snapshot.RootElement.GetProperty("unconditionalCapabilities").EnumerateArray().Select(value => value.GetString()!),
            StringComparer.Ordinal);

        enabled.UnionWith(capabilities);

        var capabilityOf = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var group in snapshot.RootElement.GetProperty("toolsByCapability").EnumerateObject())
        {
            foreach (var tool in group.Value.EnumerateArray())
            {
                capabilityOf[tool.GetString()!] = group.Name;
            }
        }

        return
        [
            .. snapshot.RootElement.GetProperty("tools").EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString()!)
                .Where(name => capabilityOf.TryGetValue(name, out var capability) && enabled.Contains(capability)),
        ];
    }

    /// <summary>
    /// Every capability upstream declares that actually carries at least one
    /// tool.
    /// </summary>
    /// <remarks>
    /// <b>Derived from <c>toolsByCapability</c> rather than from
    /// <c>declaredCapabilities</c>, and the two differ.</b> Upstream declares
    /// <c>core-install</c>, which carries no tool at all — the snapshot records
    /// it under <c>capabilitiesCarryingNoTool</c> — so a check that every
    /// declared capability is granted would fail on a capability there is
    /// nothing to grant.
    /// </remarks>
    /// <returns>The capability names, in the snapshot's own order.</returns>
    public static IReadOnlyList<string> CapabilitiesCarryingTools()
    {
        using var snapshot = Snapshot();

        return
        [
            .. snapshot.RootElement.GetProperty("toolsByCapability").EnumerateObject()
                .Where(group => group.Value.GetArrayLength() > 0)
                .Select(group => group.Name),
        ];
    }

    /// <summary>
    /// The capabilities upstream enables whatever the config says, which is why
    /// naming one in <c>capabilities</c> would do nothing.
    /// </summary>
    /// <returns>The capability names.</returns>
    public static IReadOnlyList<string> UnconditionalCapabilities()
    {
        using var snapshot = Snapshot();

        return
        [
            .. snapshot.RootElement.GetProperty("unconditionalCapabilities").EnumerateArray().Select(value => value.GetString()!),
        ];
    }

    /// <summary>
    /// How many tools the snapshot carries in total — every tool upstream can
    /// expose under any capability set.
    /// </summary>
    /// <remarks>
    /// <b>Derived so that no document and no test carries the literal.</b> It is
    /// stated in <c>DECISIONS.md</c> and asserted in two tests, and a number
    /// typed into three places is a number that will disagree with itself the
    /// first time upstream adds a tool.
    /// </remarks>
    /// <returns>The count.</returns>
    public static int SnapshotToolCount()
    {
        using var snapshot = Snapshot();

        return snapshot.RootElement.GetProperty("tools").GetArrayLength();
    }

    /// <summary>
    /// The capability object the real child advertises on <c>initialize</c>,
    /// exactly as the snapshot recorded it.
    /// </summary>
    /// <returns>The minified JSON.</returns>
    public static string ServerCapabilities()
    {
        using var snapshot = Snapshot();

        // Minified through the node API rather than by stripping characters out
        // of the raw text: the snapshot is pretty-printed, and the double it is
        // compared against is a compact literal.
        return System.Text.Json.Nodes.JsonNode.Parse(
            snapshot.RootElement.GetProperty("serverCapabilities").GetRawText())!.ToJsonString();
    }

    /// <summary>The snapshot's own record of the default, no-capabilities surface.</summary>
    /// <returns>The 24 names upstream exposes with nothing configured.</returns>
    public static IReadOnlyList<string> DefaultSurface()
    {
        using var snapshot = Snapshot();

        return
        [
            .. snapshot.RootElement.GetProperty("defaultSurface").EnumerateArray().Select(name => name.GetString()!),
        ];
    }

    /// <summary>
    /// The snapshot's whole tool array as a <c>tools/list</c> result, for a
    /// double that has to answer with the real surface rather than with two
    /// invented tools.
    /// </summary>
    /// <remarks>
    /// <b>Still not a hand-written schema.</b> The bytes are upstream's own, read
    /// out of a file the build regenerates from the resolved payload and diffs on
    /// every run — so a test that asserts on a description is asserting on what
    /// upstream actually shipped, and an upstream reword reaches it as a diff
    /// first.
    /// </remarks>
    /// <returns>The literal JSON a fake child can answer <c>tools/list</c> with.</returns>
    /// <remarks>
    /// ⚠️ <b>Minified, and that is a framing requirement rather than a
    /// preference.</b> The snapshot on disk is pretty-printed, so
    /// <c>GetRawText()</c> hands back a string full of newlines — and the
    /// double's transport is <b>newline-delimited</b>, so answering with it
    /// splits one result into three hundred unparseable frames, the caller waits
    /// out its five-minute hang detector, and the failure names the pipe rather
    /// than the payload. Measured 2026-08-18, on the first test that answered
    /// <c>tools/list</c> with this over the wire rather than passing it to
    /// <c>SessionToolSurface.Rewrite</c> in process. Minified through the node
    /// API for the same reason <see cref="ServerCapabilities"/> is: stripping
    /// whitespace by hand would corrupt any string that contains a newline.
    /// </remarks>
    public static string SnapshotToolsListResult()
    {
        using var snapshot = Snapshot();

        return new System.Text.Json.Nodes.JsonObject
        {
            ["tools"] = System.Text.Json.Nodes.JsonNode.Parse(snapshot.RootElement.GetProperty("tools").GetRawText()),
        }.ToJsonString();
    }

    /// <summary>Upstream's own description for every tool it can expose.</summary>
    /// <returns>Name to description, in the snapshot's order.</returns>
    public static IReadOnlyList<(string Name, string Description)> SnapshotDescriptions()
    {
        using var snapshot = Snapshot();

        return
        [
            .. snapshot.RootElement.GetProperty("tools").EnumerateArray()
                .Select(tool => (
                    tool.GetProperty("name").GetString()!,
                    tool.TryGetProperty("description", out var description) ? description.GetString()! : string.Empty)),
        ];
    }

    private static JsonDocument Snapshot() =>
        JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryLayout.Root.FullName, "upstream-snapshots", "tools-list.json")));
}
