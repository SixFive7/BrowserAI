// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// What git says about this repository: the files it holds, and the tag that is
/// exactly at HEAD.
/// </summary>
/// <remarks>
/// <para>
/// <b>Git is the oracle here, never the source of truth.</b> The suite has to
/// run on an export with no git in it, which is why
/// <see cref="RepositoryLayout"/> walks the disk rather than shelling out — and
/// that objection does not reach this type, because nothing here is asked unless
/// git answers. Absent, <see cref="SuiteCapability.Git"/> reads ABSENT in the
/// coverage block and <see cref="RepositoryFilesAsync"/>'s one reader skips
/// loudly, exactly as every other absent capability does.
/// <see cref="TagExactlyAtHeadAsync"/>'s reader does not skip and must not:
/// absent git it reads <see langword="null"/>, which is <i>no tag here</i>, and
/// the claim it guards only gets weaker — see the remark on that member.
/// </para>
/// <para>
/// ⚠️ <b>This exists because the walk's own remark was false by 520 files while
/// nothing noticed.</b> It claimed the walk yielded the same files as
/// <c>git ls-files</c> — verified once, by hand, on 2026-08-17, and never again.
/// When agent worktrees appeared under <c>.claude/worktrees/</c>, ignored by git
/// and not pruned by the walk, every tree-as-text scan read a second checkout as
/// repository content: the fragment scan counted <b>2,378</b> against a real
/// <b>797</b>, and three gate arms went red for a reason no message named. A
/// sentence in a remark cannot notice that. This can.
/// </para>
/// <para>
/// <b>Cached and untracked-but-not-ignored, which is what "repository content"
/// means to git.</b> <c>--cached</c> alone would make a file written this minute
/// and not yet staged read as a divergence, which is the normal state of the
/// tree during any piece of work; <c>--others</c> alone would miss everything
/// committed. <c>--exclude-standard</c> is what applies <c>.gitignore</c>, and
/// it is the half that makes a stray ignored directory — a worktree, a cache,
/// anything — a divergence rather than agreement.
/// </para>
/// <para>
/// <b><c>-z</c> rather than one path per line.</b> Git quotes and escapes a path
/// holding a space, a quote or a non-ASCII byte when it writes one per line, so
/// a line reader would compare an escaped spelling against a real one and report
/// a file present in both as missing from each. The NUL-separated form is never
/// quoted.
/// </para>
/// </remarks>
internal static class GitOracle
{
    /// <summary>How long git gets to answer before this is treated as a hang.</summary>
    /// <remarks>
    /// <b>A hang detector, not a promptness claim.</b> <c>git ls-files</c> over a
    /// few hundred paths is milliseconds; what this bounds is a git that never
    /// returns — a credential helper prompting on a redirected stdin, an index on
    /// a filesystem that went away. Without it the whole suite hangs instead of
    /// failing, which is the one outcome no gate can report.
    /// </remarks>
    private static readonly TimeSpan Patience = TestDefaults.ProcessHang;

    private static readonly Lazy<bool> Available = new(
        // ⚠️ Blocking on the async probe, deliberately, and it is safe for one
        // reason worth writing down: SuiteEnvironment.StateOf is synchronous and
        // is called from the coverage block as well as from a guard, so the
        // probe cannot be awaited by its caller. A test host is a console
        // application with no SynchronizationContext, so there is nothing for
        // the continuation to be posted back to and nothing to deadlock against;
        // and the wait underneath is bounded by Patience rather than open-ended.
        () => ProbeAsync().GetAwaiter().GetResult(),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Whether git can answer questions about the tree the suite is running in.
    /// </summary>
    /// <remarks>
    /// <b>Asked as "is this a work tree", not as "is there a git.exe".</b> The
    /// two absences this has to cover are a machine with no git installed and an
    /// export with no repository in it, and only a question put to git from the
    /// repository root answers both at once. It also settles the third case
    /// nobody thinks of — a git that starts and then refuses — which a
    /// <c>File.Exists</c> probe would call present.
    /// </remarks>
    public static bool IsAvailable => Available.Value;

    /// <summary>
    /// Every path git considers part of this repository: tracked, plus untracked
    /// and not ignored.
    /// </summary>
    /// <returns>Repository-relative paths with forward slashes, as git spells them.</returns>
    public static async Task<IReadOnlyList<string>> RepositoryFilesAsync()
    {
        var (exitCode, output, error) = await RunAsync("ls-files", "-z", "--cached", "--others", "--exclude-standard");

        if (exitCode is not 0)
        {
            throw new InvalidOperationException(
                $"'git ls-files' exited {exitCode.ToString(CultureInfo.InvariantCulture)} in '{RepositoryLayout.Root.FullName}', so the walk has nothing to be compared against: {error}");
        }

        return
        [
            .. output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                // A nested repository is listed as the directory itself rather
                // than as its contents. It is not a file, cannot be compared
                // against one, and the divergence it stands for is reported by
                // the walk's side of the comparison anyway.
                .Where(path => !path.EndsWith('/')),
        ];
    }

    /// <summary>The tag that is exactly at HEAD, if HEAD carries one.</summary>
    /// <remarks>
    /// <para>
    /// <b><c>git describe --tags --exact-match</c>, which is the whole of the
    /// question.</b> Distance zero or nothing: <c>--exact-match</c> makes a
    /// commit one past the tag an <i>error</i> rather than a
    /// <c>v1.0.0-1-g&lt;sha&gt;</c> string somebody then has to parse the
    /// distance out of. Today's tree is the case that matters — <c>v1.0.0</c>
    /// sits on <c>0455ca7</c> and HEAD is 215 commits past it, and this answers
    /// <see langword="null"/> for that as firmly as it does for an untagged
    /// repository.
    /// </para>
    /// <para>
    /// ⚠️ <b>A refusal and an absence are the same answer here, deliberately.</b>
    /// No git, no repository, HEAD not tagged and a git that started and then
    /// declined all read <see langword="null"/> — <i>this commit is not a
    /// release</i>. The one reader uses it to <b>relax</b> a requirement, so
    /// every one of those states leaves the stricter claim standing; there is no
    /// arrangement in which failing to reach git turns a red into a green.
    /// </para>
    /// </remarks>
    /// <returns>The tag name as git spells it, or <see langword="null"/>.</returns>
    public static async Task<string?> TagExactlyAtHeadAsync()
    {
        if (!IsAvailable)
        {
            return null;
        }

        var (exitCode, output, _) = await RunAsync("describe", "--tags", "--exact-match");

        return exitCode is 0 && output.Trim() is { Length: > 0 } tag ? tag : null;
    }

    private static async Task<bool> ProbeAsync()
    {
        try
        {
            var (exitCode, output, _) = await RunAsync("rev-parse", "--is-inside-work-tree");

            return exitCode is 0 && output.Trim().Equals("true", StringComparison.Ordinal);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No git on PATH at all: the export case, and the one absence this
            // whole type is allowed to have.
            return false;
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] arguments)
    {
        using var git = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = RepositoryLayout.Root.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        // -C, although the working directory is already the root. A host started
        // somewhere else, or a git resolving the working directory through a
        // junction, would otherwise answer about a different repository — and
        // the answer would look perfectly ordinary.
        git.StartInfo.ArgumentList.Add("-C");
        git.StartInfo.ArgumentList.Add(RepositoryLayout.Root.FullName);

        foreach (var argument in arguments)
        {
            git.StartInfo.ArgumentList.Add(argument);
        }

        _ = git.Start();

        // Both streams drained concurrently. Taking them in turn deadlocks the
        // moment the other fills its pipe buffer, and `ls-files` over this tree
        // is tens of kilobytes.
        var output = git.StandardOutput.ReadToEndAsync();
        var error = git.StandardError.ReadToEndAsync();

        using var patience = new CancellationTokenSource(Patience);

        try
        {
            await git.WaitForExitAsync(patience.Token);
        }
        catch (OperationCanceledException)
        {
            // Parameterless, so this is the process this method started and
            // nothing else: never a tree walk, never a pid found by name.
            git.Kill();

            await git.WaitForExitAsync(CancellationToken.None);

            throw new InvalidOperationException(
                $"git did not answer within {Patience.TotalSeconds.ToString(CultureInfo.InvariantCulture)}s and was killed, so the walk has nothing to be compared against.");
        }

        return (git.ExitCode, await output, await error);
    }
}
