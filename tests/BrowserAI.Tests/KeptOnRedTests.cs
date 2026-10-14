// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// A red arm's scratch folder is moved whole to where no reclaim reaches it, and the
/// failure says where, after the arm's own message; a folder that cannot be kept is said
/// there too, and the arm's own failure stays the one reported. An arm that keeps its
/// folder with no failure is answered the same sentence.
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
    /// <b>Planted red against a <see cref="KeptOnRed.KeepAsync(string, Exception, string, string?)"/> that answered the arm's own
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
    /// The folder of an arm that has not failed is moved whole the same way, and the
    /// answer says where it went; a folder that is not there is said and moves nothing.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-14 with the maintainer's 171</b>, for the stop of
    /// <c>BackgroundProcessTests</c>, which keeps its rig when the stop outlasts the
    /// uninstall hook's 45 s and goes on to pass. <b>Planted red 2026-10-14</b> in a filtered
    /// run against a <see cref="KeptOnRed.KeepAsync(string, string, string?)"/> that answered
    /// the sentence and moved nothing: the folder was still in the scratch, at the arm's
    /// first assertion.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFolderKeptWithNoFailureIsMovedWholeOutOfTheScratchAndTheAnswerSaysWhere()
    {
        using var scratch = await ScratchDirectory.CreateAsync("kept-with-no-failure");

        var folder = Path.Combine(scratch.Path, "rig");
        var session = Path.Combine(folder, "a-session");

        _ = Directory.CreateDirectory(session);
        await File.WriteAllTextAsync(Path.Combine(session, "browserai.data"), "the record");

        var keptRoot = Path.Combine(scratch.Path, "kept");

        var said = await KeptOnRed.KeepAsync(folder, "planted", keptRoot);

        var kept = Directory.GetDirectories(keptRoot).Single();
        var destination = Path.Combine(kept, "rig");

        await Assert.That(Directory.Exists(folder)).IsFalse().Because("a kept folder leaves the scratch the next run reclaims");
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(destination, "a-session", "browserai.data"))).IsEqualTo("the record");
        await Assert.That(said).Contains($"is kept at '{destination}'").Because(said);
        await Assert.That(Path.GetFileName(kept)).Contains("-planted-");

        // A folder that is not there: said, and nothing moved.
        var unkept = await KeptOnRed.KeepAsync(Path.Combine(scratch.Path, "never-made"), "planted", keptRoot);

        await Assert.That(unkept).Contains("could not be kept").Because(unkept);
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
