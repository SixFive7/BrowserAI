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
/// many — and it checks <c>FILE_ATTRIBUTE_REPARSE_POINT</c> while it walks,
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
}
