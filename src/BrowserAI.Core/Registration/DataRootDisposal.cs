// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Hosting;
using BrowserAI.Interop;
using BrowserAI.Runtime;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Registration;

/// <summary>What an uninstall decided about the data root.</summary>
internal enum DataRootChoice
{
    /// <summary>It stays. The default, and what every unanswerable case means.</summary>
    Keep,

    /// <summary>The person asked for it to go, and it went.</summary>
    Remove,
}

/// <summary>What the uninstall hook did about the data root, for the log and the suite.</summary>
/// <param name="Choice">Keep or remove.</param>
/// <param name="DataRoot">The directory the question was about.</param>
/// <param name="Bytes">What it held when the question was asked, or <c>0</c> when there was nothing to ask about.</param>
/// <param name="Asked">Whether a human was actually asked.</param>
/// <param name="Failures">Every node a removal could not delete. Empty on every other path.</param>
internal sealed record DataRootDisposalReport(
    DataRootChoice Choice,
    string DataRoot,
    long Bytes,
    bool Asked,
    IReadOnlyList<string> Failures);

/// <summary>
/// The one question an uninstall asks: does BrowserAI's data go with the
/// program?
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists because the two are separate directories now</b>
/// (<see cref="Hosting.IAppPaths"/>, 2026-09-15). Velopack empties the install
/// root and knows nothing about
/// <c>%LocalAppData%\BrowserAI</c> — which holds ~768 MB of provisioned
/// browsers, the session index and the process log — so without this an
/// uninstall would silently leave all of it behind for ever. The old layout had
/// the opposite defect and no question to ask: uninstall took the browsers with
/// it, always, whether or not the person was about to reinstall.
/// </para>
/// <para>
/// <b>Keep is the default and every path that cannot ask keeps.</b> A
/// <c>--silent</c> uninstall keeps, an unreadable parent keeps, a dialog that
/// fails to open keeps, a closed window keeps. The asymmetry is deliberate:
/// data that stays can be deleted by hand in one action, and data that went
/// cannot be brought back — the browsers are a 768 MB download and the session
/// index is the only inventory of session directories there is.
/// </para>
/// <para>
/// <b>Only the uninstall hook reaches this.</b> An update replaces
/// <c>current\</c> and an install creates a root; neither has any business
/// asking, and <see cref="HookRegistration"/> calls this for
/// <see cref="RegistrationIntent.Uninstall"/> and for nothing else. That is not
/// a matter of care at the call site — it is what
/// <c>RegistrationTests.AnUpdateNeverAsksAboutTheDataRootAndNeverTouchesIt</c>
/// holds.
/// </para>
/// <para>
/// ⚠️ <b>It runs inside a fast-exit callback with a 60-second budget</b>
/// ([kb](../../../kb/packaging/velopack.md#nativeaot-hooks-and-vpk-output)), and
/// it is the one hook that may be interactive — the rest of that rule stands.
/// The ordering in <see cref="HookRegistration"/> is what makes that safe: the
/// client is unregistered and the record written <i>before</i> anything is
/// asked, so a hook killed at 60 seconds for want of an answer has already done
/// the work an uninstall must not skip, and the data root is kept by the same
/// default as every other unanswered case.
/// </para>
/// </remarks>
internal static class DataRootDisposal
{
    /// <summary>The window title the question is asked under.</summary>
    public const string PromptTitle = "Uninstalling BrowserAI";

    /// <summary>
    /// Whether the uninstall that started this hook was a silent one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The parent's command line is the only place this answer exists.</b>
    /// Velopack keeps silence in a process-wide atomic inside <c>Update.exe</c>
    /// (<c>dialogs::set_silent</c>) and passes the hook nothing but
    /// <c>--veloapp-uninstall &lt;version&gt;</c> — no flag and no environment
    /// variable, read out of 1.2.0's own <c>run_hook</c>. Windows itself keeps
    /// the two apart in the registry: <c>UninstallString</c> is
    /// <c>Update.exe --uninstall</c> and <c>QuietUninstallString</c> is the same
    /// with <c>--silent</c>.
    /// </para>
    /// <para>
    /// <b>An unknown command line reads as silent</b>, because the consequence
    /// of <see langword="false"/> is putting a modal window on somebody's screen
    /// and waiting for it. A hook that could not tell who started it has no
    /// business doing that.
    /// </para>
    /// <para>
    /// <b>Tokens, never a substring.</b> <c>--silent</c> and <c>-s</c> are the
    /// two spellings <c>clap</c> accepts for the flag, and a substring test
    /// would find <c>-s</c> inside every path on the line.
    /// </para>
    /// </remarks>
    /// <param name="parentCommandLine">
    /// What started this process, or <see langword="null"/> when that could not
    /// be read.
    /// </param>
    /// <returns>Whether to treat the uninstall as unattended.</returns>
    public static bool IsSilent(string? parentCommandLine)
    {
        if (parentCommandLine is not { Length: > 0 })
        {
            return true;
        }

        foreach (var token in parentCommandLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var bare = token.Trim('"');

            if (bare.Equals("--silent", StringComparison.OrdinalIgnoreCase) || bare.Equals("-s", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The whole question, naming the directory and what it holds.</summary>
    /// <remarks>
    /// <b>The size is in the question because it is the whole of what makes it a
    /// question.</b> A prompt about "application data" is answered by habit; one
    /// that says <c>742.3 MB</c> is answered by somebody who has understood that
    /// saying yes means downloading it again.
    /// </remarks>
    /// <param name="dataRoot">The data root.</param>
    /// <param name="bytes">What it holds.</param>
    /// <returns>The message.</returns>
    public static string Question(string dataRoot, long bytes) =>
        $"BrowserAI's data is kept separately from the program and has not been removed:\n\n"
        + $"{dataRoot}\n{Describe(bytes)} — downloaded browsers, the index of your session directories, and the log.\n\n"
        + "Delete it as well?\n\n"
        + "No keeps it, and installing BrowserAI again finds it exactly as it is. Yes deletes it; the browsers are a ~768 MB download the next install would have to fetch again. Your session directories are wherever you created them and are never touched either way.";

    /// <summary>Keep or remove, as a function of the two inputs and nothing else.</summary>
    /// <remarks>
    /// <b>Pure, so that the branch a release depends on is exercised rather than
    /// only written</b> — the same reason <c>SuiteEnvironment.Decide</c> is. The
    /// alternative is a decision reachable only from inside a real uninstall,
    /// which is the one context this suite may never enter.
    /// </remarks>
    /// <param name="silent">Whether the uninstall is unattended.</param>
    /// <param name="ask">Asks the question. Never called when <paramref name="silent"/>.</param>
    /// <returns>What to do.</returns>
    public static DataRootChoice Decide(bool silent, Func<bool> ask)
    {
        ArgumentNullException.ThrowIfNull(ask);

        return !silent && ask() ? DataRootChoice.Remove : DataRootChoice.Keep;
    }

    /// <summary>
    /// Asks and decides. <b>Removes nothing</b> — see <see cref="Remove"/>.
    /// </summary>
    /// <remarks>
    /// <b>Split from the removal because the log is inside the tree.</b> The
    /// hook's own process log lives in the data root and is open while this
    /// runs, so a delete performed here would leave exactly one node behind: the
    /// file recording the decision. The caller closes the log and then calls
    /// <see cref="Remove"/>, which is why the decision is logged and the outcome
    /// of the removal is returned to be written into the installer's own log
    /// instead — the file that survives either answer.
    /// </remarks>
    /// <param name="paths">The data seam, resolved exactly as the product resolves it.</param>
    /// <param name="silent">Whether the uninstall is unattended.</param>
    /// <param name="ask">
    /// The question seam: given the composed message, whether the answer was
    /// yes. The suite supplies its own; the product supplies
    /// <see cref="UserPrompt.AskYesNo"/>.
    /// </param>
    /// <param name="logger">Where the decision is recorded.</param>
    /// <returns>What was decided.</returns>
    public static DataRootDisposalReport Choose(IAppPaths paths, bool silent, Func<string, bool> ask, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(ask);
        ArgumentNullException.ThrowIfNull(logger);

        var dataRoot = paths.RootAppDir;

        if (!HoldsAnythingWorthAsking(paths))
        {
            // Nothing to ask about. The hook that is running created this
            // directory itself -- a process log and a registration record, a few
            // kilobytes of it -- and those two are exactly what somebody reads
            // AFTER an uninstall. Putting a modal window on the screen to offer
            // to delete them would be a question with no content.
            DataRootLog.NothingToDispose(logger, dataRoot);

            return new DataRootDisposalReport(DataRootChoice.Keep, dataRoot, 0, Asked: false, []);
        }

        var bytes = BytesUnder(dataRoot);
        var size = Describe(bytes);
        var asked = false;

        var choice = Decide(silent, () =>
        {
            asked = true;
            return ask(Question(dataRoot, bytes));
        });

        if (choice is DataRootChoice.Keep)
        {
            DataRootLog.Kept(logger, dataRoot, size, silent, asked);
        }
        else
        {
            DataRootLog.Removing(logger, dataRoot, size);
        }

        return new DataRootDisposalReport(choice, dataRoot, bytes, asked, []);
    }

    /// <summary>
    /// Carries out a <see cref="DataRootChoice.Remove"/>, and does nothing at
    /// all for a <see cref="DataRootChoice.Keep"/>.
    /// </summary>
    /// <remarks>
    /// <b>Through <see cref="TreeDelete"/> for the two properties the framework
    /// call does not have together</b>: it names every node it could not remove,
    /// and it unlinks a directory reparse point rather than descending into it.
    /// The second matters here more than anywhere else in this product — a
    /// junction anywhere under a browser profile would otherwise empty whatever
    /// it points at, on a path nobody typed.
    /// </remarks>
    /// <param name="decision">What <see cref="Choose"/> returned.</param>
    /// <returns>The same report, carrying whatever would not go.</returns>
    public static DataRootDisposalReport Remove(DataRootDisposalReport decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (decision.Choice is DataRootChoice.Keep)
        {
            return decision;
        }

        var failures = new List<string>();

        TreeDelete.Remove(decision.DataRoot, failures);

        return decision with { Failures = failures };
    }

    /// <summary>
    /// Whether the data root holds anything a person would mind losing.
    /// </summary>
    /// <remarks>
    /// <b>The three data directories, asked through the seam rather than by
    /// name.</b> A data root that holds only the hook's own log and the
    /// registration record is a BrowserAI that never ran — the install hook
    /// creates the log on the way in, so the directory always exists by the time
    /// an uninstall asks. What makes the question worth asking is a provisioned
    /// browser tree, a session index or an instance directory, and each of those
    /// is a member of <see cref="IAppPaths"/> rather than a literal spelled here.
    /// </remarks>
    /// <param name="paths">The data seam.</param>
    /// <returns>Whether to ask.</returns>
    public static bool HoldsAnythingWorthAsking(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return Directory.Exists(paths.BrowsersDirectory)
            || Directory.Exists(paths.IndexDirectory)
            || Directory.Exists(paths.InstanceRoot);
    }

    /// <summary>What a directory tree holds, in bytes.</summary>
    /// <remarks>
    /// <b>Reparse points are skipped rather than followed</b>, for
    /// <c>TreeDelete</c>'s reason: a junction would count somebody else's tree
    /// into a number this hook is about to quote, and a loop would never finish
    /// inside a 60-second budget. An unreadable file is skipped rather than
    /// thrown on — the number is for a sentence a person reads, not for an
    /// accounting record.
    /// </remarks>
    /// <param name="directory">The directory.</param>
    /// <returns>The total, or <c>0</c> when it could not be measured.</returns>
    public static long BytesUnder(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        try
        {
            long total = 0;

            foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*", options))
            {
                total += file.Length;
            }

            return total;
        }
#pragma warning disable CA1031 // A size that cannot be measured is reported as zero; it can never be a reason to fail an uninstall.
        catch (Exception)
#pragma warning restore CA1031
        {
            return 0;
        }
    }

    /// <summary>The size as the question states it.</summary>
    /// <param name="bytes">The measurement.</param>
    /// <returns>A short human string.</returns>
    private static string Describe(long bytes) => bytes switch
    {
        <= 0 => "empty",
        < 1024L * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:F0} KB"),
        < 1024L * 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024):F1} MB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024 * 1024):F2} GB"),
    };
}

/// <summary>Source-generated log messages for the data root's disposal.</summary>
internal static partial class DataRootLog
{
    /// <summary>There was no data root to ask about.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="dataRoot">The directory that was not there.</param>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Uninstall: there is no data root at '{DataRoot}', so there is nothing to keep or remove.")]
    public static partial void NothingToDispose(ILogger logger, string dataRoot);

    /// <summary>The data root stays.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="dataRoot">The directory that stays.</param>
    /// <param name="size">What it holds.</param>
    /// <param name="silent">Whether the uninstall was unattended.</param>
    /// <param name="asked">Whether a human was asked.</param>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Uninstall: keeping BrowserAI's data root '{DataRoot}' ({Size}). silent={Silent} asked={Asked}. Installing BrowserAI again finds it as it is; delete it by hand to reclaim the space.")]
    public static partial void Kept(ILogger logger, string dataRoot, string size, bool silent, bool asked);

    /// <summary>
    /// The data root is about to be deleted, at the person's request.
    /// </summary>
    /// <remarks>
    /// <b>Written before the deletion rather than after it, because this file is
    /// inside the tree.</b> What the removal actually managed is returned to the
    /// caller and written into the installer's own log, which survives either
    /// answer.
    /// </remarks>
    /// <param name="logger">Where to write.</param>
    /// <param name="dataRoot">The directory that is about to go.</param>
    /// <param name="size">What it holds.</param>
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Uninstall: removing BrowserAI's data root '{DataRoot}' ({Size}) at the user's request. This log is inside it and goes with it.")]
    public static partial void Removing(ILogger logger, string dataRoot, string size);
}
