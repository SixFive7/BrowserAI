// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Runtime;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The product's one recursive delete, and the boundary it must not cross: a
/// directory reparse point is <b>unlinked</b>, never walked.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one dimension in which the hand-rolled walk was worse than the
/// call it replaced.</b> <c>Directory.Delete(path, recursive: true)</c> is
/// banned repository-wide for reporting one failed node where a tree may hold
/// many -- and it checks <c>FILE_ATTRIBUTE_REPARSE_POINT</c> while it walks,
/// which <c>TreeDelete</c> did not until 2026-08-18. On a caller-named path
/// (<c>browserai_destroy</c> takes the directory from the model) inside a
/// browser profile, where a junction to another volume is an ordinary thing to
/// find, that difference emptied the target
/// ([the adversarial review](../../docs/reviews/2026-08-18-adversarial-processes.md),
/// finding 2).
/// </para>
/// <para>
/// <b>A real junction, not a stand-in.</b> <c>mklink /J</c> needs no privilege
/// and no Developer Mode, where <c>Directory.CreateSymbolicLink</c> needs
/// <c>SeCreateSymbolicLinkPrivilege</c> and would make this test's coverage a
/// property of the machine it ran on. The point of the test is the attribute the
/// filesystem actually sets, so nothing here fakes it.
/// </para>
/// </remarks>
internal sealed class TreeDeleteTests
{
    [Test]
    public async Task AJunctionInsideTheTreeIsUnlinkedAndItsTargetIsLeftAlone()
    {
        using var scratch = ScratchDirectory.Create("tree-delete-junction");

        var target = Path.Combine(scratch.Path, "target");
        var precious = Path.Combine(target, "on-the-other-side.txt");

        _ = Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(precious, "this file is not under the tree being deleted");

        var tree = Path.Combine(scratch.Path, "tree");
        var nested = Path.Combine(tree, "nested");
        var ordinary = Path.Combine(nested, "really-under-the-tree.txt");

        _ = Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(ordinary, "this one goes");

        var link = Path.Combine(tree, "link");

        await PathAliases.JunctionAsync(link, target);

        // The positive control, and it is not decoration: a junction that was
        // not created would make every assertion below pass for the one reason
        // that proves nothing.
        await Assert.That(File.Exists(Path.Combine(link, "on-the-other-side.txt"))).IsTrue();

        var failures = new List<string>();

        TreeDelete.Remove(tree, failures);

        await Assert.That(string.Join(Environment.NewLine, failures)).IsEmpty();
        await Assert.That(Directory.Exists(tree)).IsFalse();
        await Assert.That(File.Exists(ordinary)).IsFalse();

        // The whole point.
        await Assert.That(Directory.Exists(target)).IsTrue();
        await Assert.That(File.Exists(precious)).IsTrue();
    }

    [Test]
    public async Task AJunctionNamedDirectlyIsRemovedAsTheLinkItIs()
    {
        using var scratch = ScratchDirectory.Create("tree-delete-junction-named");

        var target = Path.Combine(scratch.Path, "target");
        var precious = Path.Combine(target, "on-the-other-side.txt");

        _ = Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(precious, "this file is not under the link being deleted");

        var link = Path.Combine(scratch.Path, "link");

        await PathAliases.JunctionAsync(link, target);
        await Assert.That(File.Exists(Path.Combine(link, "on-the-other-side.txt"))).IsTrue();

        var failures = new List<string>();

        TreeDelete.Remove(link, failures);

        await Assert.That(string.Join(Environment.NewLine, failures)).IsEmpty();
        await Assert.That(Directory.Exists(link)).IsFalse();
        await Assert.That(File.Exists(precious)).IsTrue();
    }

    [Test]
    public async Task AnOrdinaryTreeIsStillRemovedWholeAndWhatWillNotGoIsNamed()
    {
        using var scratch = ScratchDirectory.Create("tree-delete-ordinary");

        var tree = Path.Combine(scratch.Path, "tree");
        var deep = Path.Combine(tree, "a", "b", "c");

        _ = Directory.CreateDirectory(deep);
        await File.WriteAllTextAsync(Path.Combine(deep, "leaf.txt"), "goes");
        await File.WriteAllTextAsync(Path.Combine(tree, "root.txt"), "also goes");

        var held = Path.Combine(tree, "a", "held.bin");

        await File.WriteAllTextAsync(held, "stays, because this test is holding it");

        var failures = new List<string>();

        using (var _ = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            TreeDelete.Remove(tree, failures);
        }

        // Every node it could not remove, not just the first: the held file, the
        // directory holding it, and the root above that.
        await Assert.That(failures.Count).IsEqualTo(3);
        await Assert.That(failures.Any(line => line.Contains("held.bin", StringComparison.Ordinal))).IsTrue();
        await Assert.That(File.Exists(held)).IsTrue();
        await Assert.That(Directory.Exists(deep)).IsFalse();
    }

    /// <summary>
    /// A read-only file is deleted, and so is every directory above it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Added 2026-09-17, planted red, off a real failure and not off an
    /// idea.</b> The 2026-09-17 release gate went red at the head of its own
    /// first run, in a test nothing had changed: the suite's reclaim pass could
    /// not take a <c>release-notes-tag-*</c> rig that
    /// <see cref="ChangelogTests.ABodyIsGeneratedOnlyFromTheChangelogTheTagCarries"/>
    /// leaves behind, because <b>git writes every loose object read-only</b> and
    /// this routine called <c>File.Delete</c> on the attribute as it found it.
    /// Windows refuses that with <c>ERROR_ACCESS_DENIED</c> -- the same message a
    /// held handle produces, which is why the survivor list read like a lock for
    /// as long as it did. It was deterministic and not a race: two consecutive
    /// runs left two identical residues of six objects each, and the only thing
    /// that had ever hidden it is
    /// <see href="../../TESTING.md">the between-runs clear</see>, which uses
    /// <c>Remove-Item -Force</c> and therefore clears the attribute.
    /// </para>
    /// <para>
    /// <b>The fix went into the product and not into the rig</b>, because a
    /// read-only file is ordinary content: anything a session downloaded, or a
    /// user dropped into a directory <c>browserai_destroy</c> is handed, would
    /// have been reported as a node the product could not remove when it could.
    /// Fixing the rig would have fixed one test and left every product caller
    /// exactly as wrong -- and <c>TreeDelete</c>'s own charter is that it is
    /// <i>one</i> routine, precisely so two callers cannot end up with two
    /// behaviours.
    /// </para>
    /// <para>
    /// <b>The held file is the control and it is in this arm and not beside
    /// it</b>: clearing an attribute must not turn into swallowing a sharing
    /// violation, so one tree carries both and the assertions say which node was
    /// removed and which was reported. The read-only <b>directory</b> is here
    /// for the same reason -- it is a second way Windows can refuse, and if it
    /// never refuses, this arm says so by passing without anything being done
    /// about it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AReadOnlyFileIsRemovedRatherThanReportedAsANodeThatWouldNotGo()
    {
        using var scratch = ScratchDirectory.Create("tree-delete-readonly");

        var tree = Path.Combine(scratch.Path, "tree");
        var objects = Path.Combine(tree, "objects", "06");

        _ = Directory.CreateDirectory(objects);

        // Exactly what git leaves behind: a loose object, read-only, under two
        // directories that are not.
        var loose = Path.Combine(objects, "e1985daea2cbd300fc423a1bf6bc4283dfbc36");

        await File.WriteAllTextAsync(loose, "a loose object");
        File.SetAttributes(loose, FileAttributes.ReadOnly);

        // A read-only DIRECTORY too, because RemoveDirectory is a second call
        // that Windows can refuse for the same reason.
        var marked = Path.Combine(tree, "marked");

        _ = Directory.CreateDirectory(marked);
        await File.WriteAllTextAsync(Path.Combine(marked, "ordinary.txt"), "goes");
        File.SetAttributes(marked, File.GetAttributes(marked) | FileAttributes.ReadOnly);

        // THE CONTROL, in the same tree: a genuine sharing violation must still
        // be reported, so that clearing an attribute cannot quietly become
        // ignoring a hold.
        var held = Path.Combine(tree, "held.bin");

        await File.WriteAllTextAsync(held, "stays, because this test is holding it");

        var failures = new List<string>();

        using (var _ = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            TreeDelete.Remove(tree, failures);
        }

        await Assert.That(File.Exists(loose)).IsFalse();
        await Assert.That(Directory.Exists(objects)).IsFalse();
        await Assert.That(Directory.Exists(marked)).IsFalse();
        await Assert.That(failures.Any(line => line.Contains("e1985daea2cb", StringComparison.Ordinal))).IsFalse();

        // The control's half: the held file and the one directory above it.
        await Assert.That(File.Exists(held)).IsTrue();
        await Assert.That(failures.Any(line => line.Contains("held.bin", StringComparison.Ordinal))).IsTrue();
        await Assert.That(failures.Count).IsEqualTo(2);
    }
}
