// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// A red arm's scratch folder is moved whole to where no reclaim reaches it, and the
/// failure says where, after the arm's own message; a folder that cannot be kept is said
/// there too, and the arm's own failure stays the one reported.
/// </summary>
/// <remarks>
/// <b>The maintainer's 94 a, 2026-10-12, in his words verbatim:</b> <i>"94 your
/// recommendation"</i>. <see cref="KeptOnRed"/> is what keeps the real Chromium arm of
/// <see cref="ScreenshotLimitTests"/>'s session folder on a red, and an arm that keeps
/// nothing on a red looks the same as one that keeps it on every green run: this holds it
/// on every run, over folders of its own under a kept root of its own, so no run writes
/// into the repository's real kept folder.
/// </remarks>
internal sealed class KeptOnRedTests
{
    /// <summary>
    /// The folder is moved whole, the failure names where it went after the arm's own
    /// message, and the arm's failure is the inner exception; a folder that is not there is
    /// said in the failure and moves nothing.
    /// </summary>
    /// <remarks>
    /// <b>Planted red against a <see cref="KeptOnRed.KeepAsync"/> that answered the arm's own
    /// failure and moved nothing</b>: the failure came back with no inner exception, at the
    /// arm's second assertion.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARedArmsFolderIsMovedWholeOutOfTheScratchAndTheFailureSaysWhere()
    {
        using var scratch = await ScratchDirectory.CreateAsync("kept-on-red");

        var folder = Path.Combine(scratch.Path, "rig");
        var session = Path.Combine(folder, "a-session");

        _ = Directory.CreateDirectory(session);
        await File.WriteAllTextAsync(Path.Combine(session, "browserai.data"), "the record");

        var keptRoot = Path.Combine(scratch.Path, "kept");
        var planted = new InvalidOperationException("the arm's own failure");

        var thrown = await KeptOnRed.KeepAsync(folder, planted, "planted", keptRoot);

        await Assert.That(thrown.Message).StartsWith("the arm's own failure").Because(thrown.Message);
        await Assert.That(thrown.InnerException).IsSameReferenceAs(planted);

        var kept = Directory.GetDirectories(keptRoot).Single();
        var destination = Path.Combine(kept, "rig");

        await Assert.That(Directory.Exists(folder)).IsFalse().Because("a kept folder leaves the scratch the next run reclaims");
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(destination, "a-session", "browserai.data"))).IsEqualTo("the record");
        await Assert.That(thrown.Message).Contains($"is kept at '{destination}'").Because(thrown.Message);
        await Assert.That(Path.GetFileName(kept)).Contains("-planted-");

        // A folder that is not there: said beside the arm's own message, and nothing moved.
        var unkept = await KeptOnRed.KeepAsync(Path.Combine(scratch.Path, "never-made"), planted, "planted", keptRoot);

        await Assert.That(unkept.Message).StartsWith("the arm's own failure").Because(unkept.Message);
        await Assert.That(unkept.Message).Contains("could not be kept").Because(unkept.Message);
        await Assert.That(unkept.InnerException).IsSameReferenceAs(planted);
    }

    /// <summary>
    /// The kept root is <c>.work\kept</c> in the repository, outside the scratch the reclaim
    /// at the head of every run deletes and every gate driver clears.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheKeptRootIsOutsideTheScratchThatTheReclaimAndTheDriversClear()
    {
        var kept = Path.GetFullPath(KeptOnRed.Root);
        var scratch = Path.GetFullPath(await ScratchRoot.PathAsync());

        await Assert.That(kept).IsEqualTo(Path.Combine(RepositoryLayout.Root.FullName, ".work", "kept"));
        await Assert.That(kept.StartsWith(Path.TrimEndingDirectorySeparator(scratch) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).IsFalse();
    }
}
