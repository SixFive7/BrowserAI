// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Reflection;
using System.Runtime.InteropServices;
using BrowserAI.Interop;
using W = Windows.Win32;

namespace BrowserAI.Tests;

/// <summary>
/// Every hand-written interop struct has the size and the field offsets that
/// Windows' own metadata says it should.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one failure in this repository that cannot present as an
/// error.</b> A wrong access mask or a mis-shaped struct does not throw and does
/// not return a failure code: <c>SetInformationJobObject</c> reads whatever is at
/// the offset it expects, and a field that has slid four bytes yields a
/// <i>plausible wrong answer</i>. Everything else here is checked by driving a
/// real browser and asserting on the outcome; a layout defect can survive that,
/// because the outcome is only wrong in the cases nobody ran.
/// </para>
/// <para>
/// <b>The oracle is Microsoft's own Win32 metadata</b>, reached through
/// <c>Microsoft.Windows.CsWin32</c>, which generates a C# declaration for each
/// name in <c>NativeMethods.txt</c>. The reference is
/// <c>PrivateAssets="all"</c> and build-time only, exactly like MinVer: nothing
/// ships, ILC never sees it, and <c>NoticesTests</c> holds that it acquires no
/// redistribution obligation. <b>BrowserAI has not adopted CsWin32 for its
/// interop</b> and the reasoning is in <c>TODO.md</c>; it is here as a measuring
/// instrument, not as a supplier.
/// </para>
/// <para>
/// <b>What this does NOT catch, and nothing here should be read as claiming it
/// does: access masks.</b> <c>FILE_APPEND_DATA</c> without
/// <c>FILE_WRITE_DATA</c> is a semantic choice, not a layout fact, and the
/// atomic-append guarantee it buys is worth
/// [70 lost records in 200](../../kb/windows/processes.md#interop-and-the-toolchain).
/// No size or offset assertion can see it. Only
/// <c>ProcessLogTests.ConcurrentProcessesDoNotLoseEachOthersRecords</c> covers
/// that, by failing the way the original defect presented.
/// </para>
/// <para>
/// <b>The structs are reached by reflection because they are <c>private</c>
/// nested types</b>, and that is deliberate rather than a workaround. An oracle
/// that compared a <i>copy</i> of each struct would assert that the copy matches
/// Windows and say nothing at all about the declarations the product actually
/// marshals through -- the two would be free to drift, which is precisely the
/// defect being guarded against. Widening the real ones to <c>internal</c> to
/// make them nameable would change the product to suit the test. So the test
/// reads the shipped types themselves, and <see cref="TheOracleReachesEveryStruct"/>
/// fails if any of them is ever renamed or moved out from under it.
/// </para>
/// </remarks>
internal sealed class InteropLayoutTests
{
    /// <summary>
    /// The hand-written structs, each named by the type that nests it. Every
    /// row is asserted by <see cref="EveryStructHasTheSizeWindowsSaysItHas"/>.
    /// </summary>
    private static readonly (string Owner, string Nested, int Expected)[] Structs =
    [
        (nameof(JobLauncher), "StartupInfo", 104),
        (nameof(JobLauncher), "StartupInfoEx", 112),
        (nameof(JobLauncher), "ProcessInformation", 24),
        (nameof(JobLauncher), "SecurityAttributes", 24),
        (nameof(JobObject), "IoCounters", 48),
        (nameof(JobObject), "JobObjectBasicLimitInformation", 64),
        (nameof(JobObject), "JobObjectExtendedLimitInformation", 144),
    ];

    /// <summary>
    /// Resolves one of the product's private nested interop structs by name.
    /// </summary>
    private static Type Nested(string owner, string nested)
    {
        var ownerType = typeof(JobObject).Assembly
            .GetType($"BrowserAI.Interop.{owner}", throwOnError: true)!;

        return ownerType.GetNestedType(nested, BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"BrowserAI.Interop.{owner} has no nested type '{nested}'. The oracle "
                + "cannot check a struct it cannot find, so this is a failure rather "
                + "than a skip: either the struct was renamed or moved, in which case "
                + "update this test, or it was deleted, in which case delete its row.");
    }

    /// <summary>
    /// The oracle can still see every struct it claims to check.
    /// </summary>
    /// <remarks>
    /// Guards the one way this whole file can go quietly useless: a rename or a
    /// move would make <see cref="Nested"/> return <c>null</c>, and a test that
    /// silently checked nothing would still be green. It is the same shape as
    /// the coverage assertion in <c>NeverByImageNameTests</c>.
    /// </remarks>
    [Test]
    public async Task TheOracleReachesEveryStruct()
    {
        await Assert.That(Structs.Length).IsEqualTo(7);

        foreach (var (owner, nested, _) in Structs)
        {
            await Assert.That(Nested(owner, nested)).IsNotNull();
        }
    }

    /// <summary>
    /// Each hand-written struct is exactly the size Microsoft's metadata says.
    /// </summary>
    /// <remarks>
    /// The expected values are written out as literals as well as compared
    /// against the generated types. That is not redundancy: if a future CsWin32
    /// ever generated a <i>different</i> shape, comparing only the two would
    /// move both sides at once and report agreement. The literal is the
    /// tiebreaker, and it is the number this repository measured on 2026-08-17.
    /// </remarks>
    [Test]
    public async Task EveryStructHasTheSizeWindowsSaysItHas()
    {
        foreach (var (owner, nested, expected) in Structs)
        {
            var hand = Marshal.SizeOf(Nested(owner, nested));
            var metadata = SizeOfMetadata(nested);

            // Named in the message because a bare "48 is not 64" says nothing
            // about which of seven structs moved.
            await Assert.That((nested, hand)).IsEqualTo((nested, expected));
            await Assert.That((nested, hand)).IsEqualTo((nested, metadata));
        }
    }

    /// <summary>
    /// The sizes above are Windows' own, not a number this repository chose.
    /// </summary>
    [Test]
    public async Task TheExpectedSizesAreMicrosoftsOwn()
    {
        await Assert.That(SizeOfMetadata("StartupInfo")).IsEqualTo(104);
        await Assert.That(SizeOfMetadata("StartupInfoEx")).IsEqualTo(112);
        await Assert.That(SizeOfMetadata("ProcessInformation")).IsEqualTo(24);
        await Assert.That(SizeOfMetadata("SecurityAttributes")).IsEqualTo(24);
        await Assert.That(SizeOfMetadata("IoCounters")).IsEqualTo(48);
        await Assert.That(SizeOfMetadata("JobObjectBasicLimitInformation")).IsEqualTo(64);
        await Assert.That(SizeOfMetadata("JobObjectExtendedLimitInformation")).IsEqualTo(144);
    }

    /// <summary>
    /// <c>Affinity</c> sits at the same offset in both definitions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A size check alone is not enough, and this field is why.</b>
    /// <c>JOBOBJECT_BASIC_LIMIT_INFORMATION</c> mixes <c>uint</c> and
    /// <c>nuint</c>, so on x64 its fields sit at offsets that padding alone
    /// would not produce. Two definitions can agree on 64 bytes and disagree on
    /// where <c>Affinity</c> starts, and the kernel reads the offset.
    /// </para>
    /// <para>
    /// <c>LimitFlags</c> is checked for the same reason from the other end: it
    /// is the field <c>JobObject</c> writes <c>KILL_ON_JOB_CLOSE</c> into, and
    /// a wrong offset there is the containment guarantee silently not being
    /// requested.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheFieldsThatCarryPaddingSitWhereWindowsPutsThem()
    {
        var basic = Nested(nameof(JobObject), "JobObjectBasicLimitInformation");

        await Assert.That((int)Marshal.OffsetOf(basic, "Affinity")).IsEqualTo(48);
        await Assert.That((int)Marshal.OffsetOf(basic, "LimitFlags")).IsEqualTo(16);

        // And Microsoft agrees, on both.
        await Assert.That((int)Marshal.OffsetOf<W.System.JobObjects.JOBOBJECT_BASIC_LIMIT_INFORMATION>("Affinity"))
            .IsEqualTo(48);
        await Assert.That((int)Marshal.OffsetOf<W.System.JobObjects.JOBOBJECT_BASIC_LIMIT_INFORMATION>("LimitFlags"))
            .IsEqualTo(16);

        // The extended struct nests the basic one first, so its own tail moves
        // if anything above slides. IoInfo starting at 64 is that check.
        var extended = Nested(nameof(JobObject), "JobObjectExtendedLimitInformation");

        await Assert.That((int)Marshal.OffsetOf(extended, "IoInfo")).IsEqualTo(64);
        await Assert.That((int)Marshal.OffsetOf<W.System.JobObjects.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>("IoInfo"))
            .IsEqualTo(64);
    }

    /// <summary>
    /// The generated size for one of the seven, by the name this test uses.
    /// </summary>
    private static unsafe int SizeOfMetadata(string nested) => nested switch
    {
        "StartupInfo" => sizeof(W.System.Threading.STARTUPINFOW),
        "StartupInfoEx" => sizeof(W.System.Threading.STARTUPINFOEXW),
        "ProcessInformation" => sizeof(W.System.Threading.PROCESS_INFORMATION),
        "SecurityAttributes" => sizeof(W.Security.SECURITY_ATTRIBUTES),
        "IoCounters" => sizeof(W.System.Threading.IO_COUNTERS),
        "JobObjectBasicLimitInformation" => sizeof(W.System.JobObjects.JOBOBJECT_BASIC_LIMIT_INFORMATION),
        "JobObjectExtendedLimitInformation" => sizeof(W.System.JobObjects.JOBOBJECT_EXTENDED_LIMIT_INFORMATION),
        _ => throw new ArgumentOutOfRangeException(nameof(nested), nested, "Not one of the seven."),
    };
}
