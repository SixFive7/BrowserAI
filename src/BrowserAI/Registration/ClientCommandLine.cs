// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text;

namespace BrowserAI.Registration;

/// <summary>
/// The real <see cref="IRegistrationCommand"/>: find the client's executable on
/// this machine, and start it directly.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Directly, never through <c>cmd.exe</c>.</b>
/// <see cref="ProcessStartInfo.UseShellExecute"/> is <see langword="false"/> and
/// the arguments go through <see cref="ProcessStartInfo.ArgumentList"/>, so the
/// path being registered reaches the client as one argument however many spaces,
/// ampersands or percent signs it contains. This is
/// [stack.md deviation 1](../../../plan/stack.md) applied to a one-shot command:
/// the SDK's own transport rewrites every command into <c>cmd.exe /c …</c>, and
/// the measured cost was a literal <c>%USERNAME%</c> arriving expanded and a
/// path containing a space failing to start at all.
/// </para>
/// <para>
/// <b>It is <see cref="Process"/> rather than
/// <see cref="Interop.JobLauncher"/>, and the difference is deliberate.</b> The
/// job object exists so that a long-lived child and every grandchild it spawns
/// die with BrowserAI. This process lives for ~650 ms, spawns nothing, and runs
/// inside a hook that is about to exit — and
/// <c>force_stop_package</c> kills everything under the install root after every
/// hook returns anyway
/// ([kb](../../../kb/packaging/velopack.md#4-force_stop_package-kills-everything-under-the-root)),
/// which the client is not.
/// </para>
/// <para>
/// <b>Both streams are read asynchronously and the working directory is
/// fixed.</b> Reading one stream to the end while the other fills its pipe is
/// the classic deadlock, and it would present as a hook that hangs until its
/// timeout kills the install. The working directory is the user profile rather
/// than the inherited one for two reasons: a hook's inherited directory can be
/// <i>inside the install root Velopack is about to replace</i>, and the client
/// resolves project-scoped configuration relative to where it was started, which
/// must have no bearing on a user-scoped registration.
/// </para>
/// </remarks>
internal sealed class ClientCommandLine : IRegistrationCommand
{
    /// <summary>
    /// The one location searched beyond <c>PATH</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured, not guessed:</b> Claude Code's native installer places
    /// <c>claude.exe</c> in <c>%USERPROFILE%\.local\bin</c>, which is where
    /// 2.1.233 was found on this machine on 2026-08-16. It is searched as a
    /// fallback because a hook inherits the installer's environment block, and an
    /// environment that has lost a <c>PATH</c> entry added since the shell
    /// started is exactly the case that would otherwise leave a working machine
    /// unregistered. A list of guessed locations is deliberately <i>not</i> here:
    /// one measured path is provenance, five plausible ones are a search that
    /// cannot be re-verified.
    /// </para>
    /// <para>
    /// <b>It earned its place the same day.</b> Measured 2026-08-16 against the
    /// real installed binary: with <c>PATH</c> cut down to <c>system32</c> alone,
    /// the install hook still registered — <i>through this directory</i>. A hook
    /// whose inherited environment is thinner than the shell's is not
    /// hypothetical.
    /// </para>
    /// <para>
    /// ⚠️ <b>It does not follow <c>%USERPROFILE%</c>.</b>
    /// <see cref="Environment.GetFolderPath(Environment.SpecialFolder, Environment.SpecialFolderOption)"/>
    /// reads the <i>token</i>, not the environment block, so setting that
    /// variable moves nothing
    /// ([kb](../../../kb/windows/processes.md#interop-and-the-toolchain)). Right for
    /// the product — an environment variable must not be able to point a
    /// registration at somebody else's profile — and it is written down because
    /// it silently defeated an attempt to measure a machine with no client on it.
    /// </para>
    /// </remarks>
    public static string FallbackDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify),
        ".local",
        "bin");

    /// <inheritdoc />
    public string? Locate(string executableName)
    {
        ArgumentException.ThrowIfNullOrEmpty(executableName);

        foreach (var directory in SearchDirectories())
        {
            string candidate;

            try
            {
                candidate = Path.Combine(directory, executableName);
            }
            catch (ArgumentException)
            {
                // A PATH entry with an invalid character in it. Every machine has
                // one eventually, and it must not stop the search.
                continue;
            }

            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public CommandOutcome Run(string executable, IReadOnlyList<string> arguments, TimeSpan budget)
    {
        ArgumentException.ThrowIfNullOrEmpty(executable);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,

            // Never the inherited one. See the remarks.
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify),
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var said = new StringBuilder();

        try
        {
            using var process = new Process { StartInfo = startInfo };

            void collect(object _, DataReceivedEventArgs line)
            {
                if (line.Data is null)
                {
                    return;
                }

                lock (said)
                {
                    _ = said.AppendLine(line.Data);
                }
            }

            process.OutputDataReceived += collect;
            process.ErrorDataReceived += collect;

            if (!process.Start())
            {
                return new CommandOutcome(-1, string.Empty, TimedOut: false, $"'{executable}' could not be started and Windows reused an existing process instead.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // RS0030: the timed WaitForExit overload is banned repository-wide
            // because on its own it returns without draining the asynchronous
            // readers, which truncates stderr silently. This is the one
            // sanctioned exception in the tree, and it is sanctioned only
            // because of the bare WaitForExit() below: a client CLI that hangs
            // has to be given up on, and there is no WaitForExitAsync here
            // because this whole path is synchronous by design -- it runs during
            // registration, before any of the async machinery exists.
#pragma warning disable RS0030
            if (!process.WaitForExit((int)budget.TotalMilliseconds))
#pragma warning restore RS0030
            {
                // Never Kill(entireProcessTree: true) -- it is banned, and it
                // walks re-parentable links. This process spawns nothing.
                TryKill(process);

                return new CommandOutcome(-1, Collected(said), TimedOut: true, null);
            }

            // The second, argument-less wait is what flushes the asynchronous
            // readers: without it the output of a process that has already
            // exited is routinely half-collected.
            process.WaitForExit();

            // Cached the instant it is available. Process.ExitCode throws after
            // Dispose(), and the `using` above is why that matters here.
            var exitCode = process.ExitCode;

            return new CommandOutcome(exitCode, Collected(said), TimedOut: false, null);
        }
#pragma warning disable CA1031 // A client that cannot be started is a logged fact and a registration that did not happen. It is never an installer failure.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            return new CommandOutcome(-1, Collected(said), TimedOut: false, failure.Message);
        }
    }

    /// <summary>
    /// Where <see cref="Locate"/> looks, in order.
    /// </summary>
    /// <returns>The directories, <c>PATH</c> first.</returns>
    private static IEnumerable<string> SearchDirectories()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // A quoted PATH entry is legal and common; Path.Combine is not told
            // about quotes and would produce a directory nothing matches.
            yield return entry.Trim('"');
        }

        yield return FallbackDirectory;
    }

    private static string Collected(StringBuilder said)
    {
        lock (said)
        {
            return said.ToString().Trim();
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill();
        }
#pragma warning disable CA1031 // It exited between the timeout and the kill, or it cannot be killed. Either way the outcome is already "timed out".
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
