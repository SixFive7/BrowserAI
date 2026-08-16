// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// What is actually set on the job object, read back from the kernel rather
/// than from the code that set it.
/// </summary>
/// <remarks>
/// Every assertion here is aimed at a mistake that leaves the suite green: a
/// breakaway flag that turns the guarantee into a suggestion, a UI restriction
/// that stops Chromium's sandbox job nesting, and an inheritable handle that
/// silently makes <c>KILL_ON_JOB_CLOSE</c> never fire. None of the three
/// produces an error, and two of them were measured letting the entire tree
/// survive.
/// </remarks>
internal sealed class JobObjectTests
{
    /// <summary>
    /// The one flag the product is allowed to set, so a second flag arriving
    /// later fails on the exact value rather than on a bitmask test that still
    /// passes.
    /// </summary>
    private const uint OnlyKillOnJobClose = 0x00002000;

    [Test]
    public async Task TheJobCarriesKillOnJobCloseAndNothingElse()
    {
        using var job = JobObject.CreateKillOnClose();

        // Equality, not a bit test. `(flags & KillOnJobClose) != 0` would pass
        // with BREAKAWAY_OK sitting beside it, which is the configuration that
        // actively arms Firefox's escape rather than merely permitting one.
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

        await Assert.That(Naming(sources, "AssignProcessToJobObject")).IsEmpty();

        // CREATE_SUSPENDED measured 0 escapees too, so this is not a
        // correctness assertion -- it is that the product has exactly one way
        // of putting a child in a job, and a second one arriving without a
        // decision is what this catches.
        await Assert.That(Naming(sources, "CreateSuspended")).IsEmpty();
        await Assert.That(Naming(sources, "CREATE_SUSPENDED")).IsEmpty();
    }

    private static string Naming(Dictionary<string, string> sources, string needle) =>
        string.Join(", ", sources.Where(entry => entry.Value.Contains(needle, StringComparison.Ordinal)).Select(entry => entry.Key));
}
