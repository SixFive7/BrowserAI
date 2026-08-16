// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The acceptance test for the vertical slice: a published NativeAOT binary
/// proxying a real <c>@playwright/mcp</c> child, driven by a client that shares
/// no protocol code with it.
/// </summary>
/// <remarks>
/// Every assertion here is aimed at something that would otherwise report
/// healthy — a tool list quietly shortened by the SDK's convenience overload, a
/// navigation whose result is an error nobody reads, a node process left running
/// after the binary that owned it was killed.
/// </remarks>
internal sealed class VerticalSliceTests
{
    [Test]
    public async Task ToolsListReturnsTheChildsToolsWithUpstreamsOwnNames()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SliceRun.SharedAsync();

        // `initialize` has to advertise tools, and it only does so because
        // McpServerOptions.Capabilities.Tools is set: a server carrying tool
        // handlers but no declared capability tells the caller it has none, and
        // a caller that respects capabilities then never asks. The tool list
        // below would still be right, and the surface would still be invisible.
        await Assert.That(run.InitializeResult["capabilities"]?["tools"]).IsNotNull();

        // Byte for byte, and in upstream's order. Renaming is settled as
        // forbidden, so this asserts identity rather than exercising a map; the
        // day a rename map appears, this is what says so. The expected list is
        // computed from the committed snapshot, which the build regenerates from
        // the resolved payload, so an upstream change is a snapshot diff first
        // and this test second.
        //
        // Compared as one joined string rather than as a set, because order is
        // part of the contract: the spec asks for deterministic ordering for
        // prompt-cache hit rates, and a set comparison would pass a proxy that
        // shuffled the list.
        //
        // The five authored tools come first; upstream's follow, and it is the
        // UNION surface — 59 rather than the default 24 — because the run's own
        // child is started with every capability any mode can have. The spec
        // forbids the tool set varying per connection, so one static list is the
        // only shape available and it has to be the superset.
        await Assert.That(string.Join(", ", run.ToolNames))
            .IsEqualTo(string.Join(", ", [.. SessionToolSurface.Names, .. UpstreamSurface.For(BrowserConfiguration.UnionCapabilities)]));

        // Stated as a number as well, because 42/59 is what §C records and a
        // list comparison that both sides got wrong the same way would not say
        // so.
        await Assert.That(run.ToolNames.Count).IsEqualTo(SessionToolSurface.Names.Count + 59);

        // And every one of them carries an explicit classification, asserted
        // against the REAL child's list rather than against the snapshot the
        // build regenerates from it. Deny-by-default means an unclassified tool
        // is refused at runtime, so this is the check that turns a tool upstream
        // added into a red build instead of a silent refusal in production.
        var unclassified = run.ToolNames
            .Where(name => !SessionToolSurface.IsAuthored(name))
            .Where(name => !SessionToolPolicy.Classification.ContainsKey(name))
            .ToList();

        await Assert.That(string.Join(", ", unclassified)).IsEmpty();
    }

    [Test]
    public async Task EveryUpstreamToolGainsTheSessionParameterAndNoneLosesItsOwn()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SliceRun.SharedAsync();
        var offenders = new List<string>();

        foreach (var tool in run.ToolList)
        {
            var name = (string)tool!["name"]!;

            if (SessionToolSurface.Names.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            var schema = tool["inputSchema"]!.AsObject();
            var properties = schema["properties"]?.AsObject();
            var required = schema["required"]?.AsArray().Select(entry => (string)entry!).ToList() ?? [];

            if (properties?["session"] is null)
            {
                offenders.Add($"{name}: no session property");
            }

            if (!required.Contains("session", StringComparer.Ordinal))
            {
                offenders.Add($"{name}: session is not required");
            }

            // Appended, never inserted: upstream's own properties keep their
            // order, and `session` is last. A rewrite that reordered would cost
            // a prompt-cache miss per call with nothing failing.
            if (properties is { Count: > 1 } && properties.Last().Key is not "session")
            {
                offenders.Add($"{name}: session is not the last property");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    [Test]
    public async Task NavigatingToADataUrlReturnsANonErrorResultThatNamesThePage()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SliceRun.SharedAsync();

        // A JSON-RPC error would mean the call never reached the child.
        await Assert.That(run.NavigateEnvelope.ContainsKey("error")).IsFalse();

        // isError is the other half, and it is the one that looks like success:
        // a tool failure travels as a perfectly valid result.
        await Assert.That((bool?)run.NavigateEnvelope["result"]!["isError"] is true).IsFalse();

        // The proof that a page was really loaded rather than that a result
        // shaped like one came back.
        await Assert.That(run.NavigateText).Contains("Page URL: data:text/html");
    }

    [Test]
    public async Task KillingThePublishedBinaryLeavesNoNodeAndNoBrowser()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SliceRun.SharedAsync();

        // A tree that never came up would satisfy "no survivors" vacuously, so
        // the shape of what was contained is asserted first: the binary, its
        // node child, and a real browser with children of its own.
        await Assert.That(run.Processes.Any(process =>
            process.ImagePath?.EndsWith(@"payload\node\node.exe", StringComparison.OrdinalIgnoreCase) is true)).IsTrue();

        await Assert.That(run.ChromiumProcesses(BrowserAiPaths.BrowsersDirectory).Count).IsGreaterThanOrEqualTo(3);

        // The contract. BrowserAI was terminated from outside and ran no code
        // afterwards; the only thing that can have cleaned this up is the kernel
        // closing its last job handle.
        var survivors = string.Join(
            ", ",
            run.Survivors.Select(process => $"{process.ProcessId} {process.ImagePath ?? "<unknown>"}"));

        await Assert.That(survivors).IsEmpty();
    }

}
