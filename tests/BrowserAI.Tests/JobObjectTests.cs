// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.RegularExpressions;
using BrowserAI.Interop;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// What is actually set on the job object, read back from the kernel and
/// not from the code that set it.
/// </summary>
/// <remarks>
/// Every assertion here is aimed at a mistake that leaves the suite green: a
/// breakaway flag that turns the guarantee into a suggestion, a UI restriction
/// that stops Chromium's sandbox job nesting, and an inheritable handle that
/// silently makes <c>KILL_ON_JOB_CLOSE</c> never fire. None of the three
/// produces an error, and two of them were measured letting the entire tree
/// survive.
/// </remarks>
internal sealed partial class JobObjectTests
{
    /// <summary>
    /// The one flag the product is allowed to set, so a second flag arriving
    /// later fails on the exact value and not on a bitmask test that still
    /// passes.
    /// </summary>
    private const uint OnlyKillOnJobClose = 0x00002000;

    [Test]
    public async Task TheJobCarriesKillOnJobCloseAndNothingElse()
    {
        using var job = JobObject.CreateKillOnClose();

        // Equality, not a bit test. `(flags & KillOnJobClose) != 0` would pass
        // with BREAKAWAY_OK sitting beside it, which is the configuration that
        // actively arms Firefox's escape and does not merely permit one.
        await Assert.That(job.LimitFlags).IsEqualTo(OnlyKillOnJobClose);
        await Assert.That(job.LimitFlags & JobObject.BreakawayOk).IsEqualTo(0u);
        await Assert.That(job.LimitFlags & JobObject.SilentBreakawayOk).IsEqualTo(0u);
    }

    [Test]
    public async Task TheJobSetsNoUiRestrictions()
    {
        using var job = JobObject.CreateKillOnClose();

        // Jobs nest only if neither sets UI limits, and Chromium's sandbox job
        // has to nest inside ours. A non-zero value here is a containment hole
        // that appears only under a real browser.
        await Assert.That(job.UiRestrictions).IsEqualTo(0u);
    }

    [Test]
    public async Task TheJobHandleCannotBeInherited()
    {
        using var job = JobObject.CreateKillOnClose();

        // Measured fatal: with an inheritable handle, redirection duplicates it
        // into the child, ours stops being the last handle, KILL_ON_JOB_CLOSE
        // never fires, and every child survives. Redirecting stdio forces
        // bInheritHandles=TRUE, so this is one flag away at all times.
        await Assert.That(job.HandleIsInheritable).IsFalse();
    }

    [Test]
    public async Task TheTransportsOwnJobIsConfiguredTheSameWay()
    {
        // The job a real transport creates, not one built by this test. The two
        // could drift apart, and the one that matters is the transport's.
        await using var child = await ProbeChild.StartAsync("job-transport");

        await Assert.That(child.Session.Job.LimitFlags).IsEqualTo(OnlyKillOnJobClose);
        await Assert.That(child.Session.Job.UiRestrictions).IsEqualTo(0u);
        await Assert.That(child.Session.Job.HandleIsInheritable).IsFalse();
    }

    [Test]
    public async Task AChildIsAMemberOfTheJobBeforeItCanRunAnything()
    {
        await using var child = await ProbeChild.StartAsync("job-membership");

        // The pid the CHILD reported about itself, so an interposed process
        // could not satisfy this by being a member in its place.
        await Assert.That(child.Session.Job.ProcessIds()).Contains(child.ReportedProcessId);
    }

    [Test]
    public async Task TheProductAssignsThroughTheAttributeListAndNeverAfterTheFact()
    {
        // Code only: the paragraph in JobLauncher.cs explaining WHY
        // AssignProcessToJobObject is the wrong mechanism must not read as a
        // use of it.
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in RepositoryLayout.ProductSourceFiles)
        {
            sources[Path.GetFileName(file.FullName)] = await RepositoryLayout.ReadCodeAsync(file);
        }

        // A behavioural test cannot tell PROC_THREAD_ATTRIBUTE_JOB_LIST from
        // Process.Start-then-assign after the fact: both end with the child in
        // the job. What distinguishes them is the window in between, and the
        // only way to be sure that window does not exist is that the code
        // cannot open one. JobContainmentTests then proves the consequence --
        // a child that spawns grandchildren immediately loses none of them.
        await Assert.That(sources.Values.Any(text => text.Contains("ProcThreadAttributeJobList", StringComparison.Ordinal))).IsTrue();

        // CREATE_SUSPENDED measured 0 escapees too, so this is not a
        // correctness assertion -- it is that the product has exactly one way
        // of putting a child in a job, and a second one arriving without a
        // decision is what this catches.
        //
        // ⚠️ ONE SECOND WAY IS DECIDED, AND IT IS NAMED HERE SO THAT IT STAYS ONE --
        // the maintainer's 188 a, 2026-11-18, in his words verbatim: "188 a - only
        // surface this question again after you have verified if that solution
        // works." Windows refuses the job list for an app execution alias, so
        // JobLauncher.JoinAndResume assigns a suspended alias after CreateProcessW
        // returns, and only there
        // (DECISIONS.md, "The Store's PowerShell under the gate drivers' arms").
        // Every other file still names neither, and JobLauncher.cs itself holds one
        // call, inside JoinAndResume, reached only for an alias, and one suspended
        // flag, set only for one.
        var elsewhere = sources.Where(entry => entry.Key is not "JobLauncher.cs").ToDictionary(StringComparer.Ordinal);

        await Assert.That(Naming(elsewhere, "AssignProcessToJobObject")).IsEmpty();
        await Assert.That(Naming(elsewhere, "CreateSuspended")).IsEmpty();
        await Assert.That(Naming(sources, "CREATE_SUSPENDED")).IsEmpty();

        await Assert.That(TheDecidedExceptionIn(sources["JobLauncher.cs"])).IsEmpty();
    }

    private static string Naming(Dictionary<string, string> sources, string needle) =>
        string.Join(", ", sources.Where(entry => entry.Value.Contains(needle, StringComparison.Ordinal)).Select(entry => entry.Key));

    /// <summary>
    /// What in <c>JobLauncher.cs</c> goes past the one decided exception of 188 a, as
    /// a list of sentences, empty when nothing does.
    /// </summary>
    /// <param name="code">The file's code, its comment lines left out.</param>
    /// <returns>Each departure, one per line.</returns>
    private static string TheDecidedExceptionIn(string code)
    {
        var wrong = new List<string>();
        var body = MethodBody(code, "private static void JoinAndResume(");

        // The declaration is not a call, so it is left out of the count.
        var assignments = CallSites(code, "AssignProcessToJobObject(", "static partial bool AssignProcessToJobObject(");

        if (assignments.Count is not 1 || body is null || !assignments.All(at => at > body.Value.Start && at < body.Value.End))
        {
            wrong.Add($"JobLauncher.cs calls AssignProcessToJobObject {assignments.Count} time(s); 188 a decided exactly one call, inside JoinAndResume");
        }

        if (CallSites(code, "JoinAndResume(", "private static void JoinAndResume(").Count is not 1
            || !TheGuardedCall().IsMatch(code))
        {
            wrong.Add("JobLauncher.cs reaches JoinAndResume from somewhere other than one call guarded by 'if (packaged)'");
        }

        if (CallSites(code, "CreateSuspended", "private const uint CreateSuspended = ").Count is not 1
            || !code.Contains("(packaged ? CreateSuspended : 0u)", StringComparison.Ordinal))
        {
            wrong.Add("JobLauncher.cs starts a child suspended other than through '(packaged ? CreateSuspended : 0u)'");
        }

        if (CallSites(code, "packaged = ", "var packaged = job is not null && IsAppExecutionAlias(command);").Count is not 0
            || !code.Contains("var packaged = job is not null && IsAppExecutionAlias(command);", StringComparison.Ordinal))
        {
            wrong.Add("JobLauncher.cs decides 'packaged' from something other than an app execution alias launched into a job");
        }

        return string.Join(Environment.NewLine, wrong);
    }

    /// <summary>The one call of <c>JoinAndResume</c> 188 a decided, under the alias guard.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"if \(packaged\)\s*\{\s*JoinAndResume\(job!, information, command\);")]
    private static partial Regex TheGuardedCall();

    /// <summary>Where a needle stands in the code, its one permitted line left out.</summary>
    /// <param name="code">The code.</param>
    /// <param name="needle">What is counted.</param>
    /// <param name="permitted">A line holding it that is not counted, the declaration.</param>
    /// <returns>The offsets of the rest.</returns>
    private static List<int> CallSites(string code, string needle, string permitted)
    {
        var sites = new List<int>();

        for (var at = code.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = code.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            var lineStart = code.LastIndexOf('\n', at, StringComparison.Ordinal) + 1;
            var lineEnd = code.IndexOf('\n', at, StringComparison.Ordinal);
            var line = code[lineStart..(lineEnd < 0 ? code.Length : lineEnd)];

            if (!line.Contains(permitted, StringComparison.Ordinal))
            {
                sites.Add(at);
            }
        }

        return sites;
    }

    /// <summary>The span of a method's body: from its signature to the first closing brace at the class's member indent.</summary>
    /// <param name="code">The code.</param>
    /// <param name="signature">The start of the method's signature.</param>
    /// <returns>The span, or <see langword="null"/> when the method is not there.</returns>
    private static (int Start, int End)? MethodBody(string code, string signature)
    {
        var start = code.IndexOf(signature, StringComparison.Ordinal);

        if (start < 0)
        {
            return null;
        }

        var end = code.IndexOf("\n    }\n", start, StringComparison.Ordinal);

        return end < 0 ? null : (start, end);
    }
}
