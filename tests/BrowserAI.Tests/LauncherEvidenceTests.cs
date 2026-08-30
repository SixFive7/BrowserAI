// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The failure dump the containment arms hand a reader, held to the two
/// properties it silently lacked: it must be able to read a file somebody is
/// still writing, and every byte count it prints must have been measured.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a test about an instrument rather than about the product, and it
/// earns its place the way an instrument does — by having failed.</b> On
/// 2026-08-29 a Firefox containment arm stalled out Playwright's own 180 s
/// <c>initializeServer</c> budget inside a gate run. The dump that arrived
/// named three capture files and said <c>(unreadable: … because it is being
/// used by another process)</c> for every one of them, with <c>(0 bytes)</c>
/// beside each. Everything worth knowing about that stall was in those files;
/// the scratch tree was then deleted, as it is designed to be, and the account
/// went with it. An instrument that cannot read the evidence is not a weaker
/// instrument — for that run it was no instrument at all.
/// </para>
/// <para>
/// <b>Both arms are in process and neither needs a browser</b>, because the
/// defect was never about browsers: it is Windows share-mode arithmetic and a
/// cached property, and both reproduce against a <see cref="FileStream"/> in
/// four lines. What the containment arms contribute is the <i>reason</i> this
/// matters — a dump is taken at the instant a launch did not happen, which is
/// exactly the instant every writer in the tree is still holding its handle.
/// </para>
/// </remarks>
internal sealed class LauncherEvidenceTests
{
    /// <summary>
    /// The measured shape: a file a live writer is holding, with content already
    /// in the OS, must come back readable and at its true length.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The writer's sharing is node's</b>, because the writer this exists for
    /// is node: <c>fs.openSync(path, 'a')</c> goes through libuv, which asks for
    /// <c>FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE</c>
    /// indiscriminately so that a file can be read and deleted while it is open.
    /// <b>The reader still lost</b>, and that is the half worth stating: the
    /// refusal was never the writer's doing. <c>File.ReadAllText</c> offers
    /// <c>FileShare.Read</c>, Windows checks that against the
    /// <c>GENERIC_WRITE</c> the writer already holds, and a reader that does not
    /// permit writing is refused however permissive the writer was.
    /// </para>
    /// <para>
    /// <b>Watched red 2026-08-30 against the old body</b>, which threw
    /// <c>IOException</c> — <i>"the process cannot access the file … because it
    /// is being used by another process"</i>, the same sentence the 2026-08-29
    /// dump carried — and reported it as <c>(unreadable: …)</c>. Established
    /// first outside the suite against a real <c>node</c> holder and then in
    /// process, so the sharing arithmetic is not being inferred from one API.
    /// </para>
    /// <para>
    /// ⚠️ <b>The red reproduced the phantom too, which was not the
    /// expectation.</b> The length assertion was written to be about provenance
    /// — the number must have been measured — on the assumption that the cached
    /// figure would happen to be right, as it had been in a probe minutes
    /// earlier. It was not: the old body printed
    /// <c>--- cli-stderr.log (0 bytes) ---</c> for the 63 bytes this arm had just
    /// written and flushed, character for character the line the 2026-08-29 dump
    /// carried. So this is a staleness assertion as well as a provenance one, and
    /// it is recorded that way because it was measured that way rather than
    /// argued.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFileALiveWriterIsHoldingComesBackReadableAndAtItsTrueLength()
    {
        using var scratch = ScratchDirectory.Create("launcher-evidence-held");

        // Upstream's own sentence from the 2026-08-29 stall, so the arm carries
        // the thing that was lost rather than a placeholder.
        var written = Encoding.UTF8.GetBytes("TimeoutError: async initializeServer: Timeout 180000ms exceeded");

        // Declared after the scratch directory, so it is disposed before the
        // tree it lives in is removed. It stays open across the Evidence call
        // below -- that is the whole scenario.
        using var writer = new FileStream(
            Path.Combine(scratch.Path, "cli-stderr.log"),
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);

        await writer.WriteAsync(written);

        // In the OS by the time this returns, which is what the driver's
        // writeSync per chunk buys and what makes the bytes another process's to
        // read.
        await writer.FlushAsync();

        var evidence = LauncherWait.Evidence(scratch.Path);

        await Assert.That(evidence).Contains("TimeoutError: async initializeServer").Because(evidence);
        await Assert.That(evidence).DoesNotContain("used by another process").Because(evidence);

        // The number beside the name, and it is the file's real size rather than
        // whatever the enumeration happened to carry.
        await Assert.That(evidence)
            .Contains($"cli-stderr.log ({written.Length} bytes)")
            .Because(evidence);
    }

    /// <summary>
    /// A file nothing could open must carry no byte count at all, because
    /// nothing measured one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the arm the fix above cannot satisfy by accident.</b> An
    /// exclusive holder refuses every reader, including one sharing everything,
    /// so <c>(unreadable: …)</c> is the correct and honest answer here and stays.
    /// What must not survive is the number that used to sit beside it: the dump
    /// printed <c>(0 bytes)</c> for three files in the same breath as saying it
    /// could not read them, and a reader has no way to tell that figure from one
    /// somebody took.
    /// </para>
    /// <para>
    /// <b>Watched red 2026-08-30 against the old body</b>, which printed a
    /// cached byte count beside its own <c>(unreadable: …)</c> for a file it had
    /// just been refused. The control that keeps this from passing vacuously is
    /// the arm above: if <see cref="LauncherWait.Evidence"/> ever stopped
    /// printing lengths altogether, that one goes red.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFileNothingCouldOpenCarriesNoByteCountBecauseNothingMeasuredOne()
    {
        using var scratch = ScratchDirectory.Create("launcher-evidence-exclusive");

        var path = Path.Combine(scratch.Path, "sealed.log");

        await File.WriteAllTextAsync(path, "content a cached length would happily report");

        // FileShare.None: not the driver's shape, and not a shape the fix can
        // reach -- deliberately, because what is under test here is what the
        // dump says when it genuinely cannot look.
        using var heldByAnother = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var evidence = LauncherWait.Evidence(scratch.Path);

        await Assert.That(evidence).Contains("--- sealed.log ---").Because(evidence);
        await Assert.That(evidence).Contains("(unreadable: ").Because(evidence);
        await Assert.That(evidence).DoesNotContain("bytes)").Because(evidence);
    }
}
