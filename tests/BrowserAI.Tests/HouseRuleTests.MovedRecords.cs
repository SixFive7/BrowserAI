// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.RegularExpressions;

namespace BrowserAI.Tests;

/// <summary>
/// The house rules on the records the product writes beside themselves and moves into place:
/// each writer waits out a holder of the file it replaces, and a poll in the suite asks the
/// product's own reader.
/// </summary>
/// <remarks>
/// <b>The maintainer's 157 a and 159 a, 2026-10-13</b>, in his words verbatim:
/// <i>"157 a"</i> and <i>"159 a"</i>, after the sweep of his 154 c of the arms that went red
/// one run in thirty-five on an order the machine's load chose. Windows refuses a rename over
/// a file that any process has open, whatever its share mode
/// (<see href="../../kb/windows/processes.md">the kb</see>, measured 2026-08-16), and a file
/// being replaced refuses a new open for the instant of the move, so a reader and a writer of
/// one of these records can each make the other fail.
/// </remarks>
internal sealed partial class HouseRuleTests
{
    /// <summary>The one file whose own move into place is the waiting move itself.</summary>
    private const string WaitingMoveFile = @"BrowserAI.Core\Runtime\FileSystemThread.cs";

    /// <summary>The waiting move, composed so this file is not its own offender.</summary>
    private const string WaitingMove = "Replace" + "FileAsync(";

    /// <summary>The move that replaces a file and waits for nothing, composed the same way.</summary>
    private const string BareMove = "Move" + "FileAsync(";

    /// <summary>The argument that makes a move replace the file at its destination.</summary>
    private const string Replacing = "overwrite: " + "true";

    /// <summary>
    /// Every record the product moves into place: the file that writes it, the reader a poll
    /// asks, the names a test gives its path, and what the writer keeps when it does not take
    /// the waiting move.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Checked against <c>src</c> in both directions</b> by
    /// <see cref="EveryMoveIntoPlaceWaitsOutAHolderOfTheFileItReplaces"/>: a move into place in
    /// a file with no row is red, and so is a row whose file moves nothing into place. The two
    /// writers with loops of their own keep the budgets their remarks give: a background's
    /// record has <c>BackgroundRecord.WriteBound</c>, and a session-index entry gives up after
    /// <c>SessionIndex.MoveBudget</c> because the next use re-asserts it.
    /// </para>
    /// <para>
    /// ⚠️ <b>A third writer keeps its own handling since 2026-10-13</b>, the build's choice,
    /// for the maintainer's review: 127 c's record of the tool list a client was last answered
    /// was written at <c>69587bb4</c>, outside <c>8534c95e</c>, the tree the sweep read, and is not tried again
    /// by its own design. A record it cannot replace is removed with the file beside it, so the
    /// client's next relay finds no record and refuses its first call once, as Q261 b does.
    /// </para>
    /// </remarks>
    private static MovedRecord[] MovedRecords { get; } =
    [
        new(@"BrowserAI.App\Page\UpdateNotice.cs", "UpdateNoticeFile.ReadAsync", ["UpdateNoticeFile.FileName", "update-notice.txt"], Keeps: null),
        new(@"BrowserAI.App\Page\PageListenerMemory.cs", "PageListenerMemoryFile.ReadAsync", ["PageListenerMemoryFile.FileName", "page-listener.txt"], Keeps: null),
        new(@"BrowserAI.Core\Updates\UpdateCheckStamp.cs", "UpdateCheckStampFile.ReadAsync", ["UpdateCheckStampFile.FileName", "last-update-check.txt"], Keeps: null),
        new(@"BrowserAI\Storage\LockFile.cs", "LockFile.ReadAsync", ["LockFile.FileName", "SessionLayout.LockFileName", ".LockFile", "browserai.lock"], Keeps: null),
        new(@"BrowserAI\Runtime\HeadedUserAgent.cs", "HeadedUserAgent.ForAsync", ["HeadedUserAgent.CacheFileName", "browserai-user-agent.txt"], Keeps: null),
        new(@"BrowserAI.Core\Coordination\BackgroundRecord.cs", "BackgroundRecord.ReadAsync", ["BackgroundRecord.PathFor", "BackgroundRecord.DirectoryName"], Keeps: "a loop of its own, bounded by BackgroundRecord.WriteBound"),
        new(@"BrowserAI\Sessions\SessionIndex.cs", "SessionIndex.FollowAsync", [".IndexDirectory", ".IndexKey"], Keeps: "a loop of its own, bounded by SessionIndex.MoveBudget"),
        new(@"BrowserAI\Relay\ToolListRecord.cs", "ToolListRecordFile.ReadAsync", ["ToolListRecordFile.PathFor", "ToolListRecordFile.DirectoryName"], Keeps: "one try, removing the record it could not replace (127 c)"),
    ];

    /// <summary>
    /// Every move into place in the product waits out a holder of the file it replaces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The maintainer's 157 a.</b> Five writers made one move and took its first refusal for
    /// the answer, so a reader holding the record at that instant made the write fail: an
    /// after-update notice marked seen came back after the next start, and a session's
    /// <c>browserai.lock</c> could not be written while a peer's probe passed through it. Each
    /// moves its record through <c>FileSystemThread.ReplaceFileAsync</c> since, and
    /// <c>DedicatedThreadTests.AMoveIntoPlaceWaitsOutAHolderOfTheFileItReplacesAndAnswersAnyOtherFailureAtOnce</c>
    /// holds what that move does. This holds that every writer takes it: a
    /// <c>MoveFileAsync</c> that replaces a file is refused anywhere but in the waiting move
    /// itself and in the writers whose own handling <see cref="MovedRecords"/> names.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-13</b> against the five writers as they stood at <c>40911cbb</c>:
    /// it named the five and nothing else.
    /// </para>
    /// <para>
    /// <b>What it cannot see</b> is a move written some other way, which the product's ban
    /// keeps to the file-system thread, and a loop of its own that waits for nothing.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryMoveIntoPlaceWaitsOutAHolderOfTheFileItReplaces()
    {
        var moves = new List<(string Writer, string Call)>();
        var source = Path.Combine(RepositoryLayout.Root.FullName, "src");

        foreach (var file in RepositoryLayout.ProductSourceFiles)
        {
            var writer = Path.GetRelativePath(source, file.FullName);

            moves.AddRange(MovesIntoPlace(await RepositoryLayout.ReadCodeAsync(file)).Select(call => (writer, call)));
        }

        await Assert.That(string.Join(Environment.NewLine, MoveOffences(moves))).IsEmpty();

        // Not vacuous: the waiting move's own call, and one move for every row.
        await Assert.That(moves.Count(move => move.Writer == WaitingMoveFile)).IsEqualTo(1);

        // ---- The controls, over moves this tree will never hold ----------------------------
        string[] bare = ["        await FileSystemThread." + BareMove + "temporary, path, " + Replacing + ").ConfigureAwait(false);"];
        string[] waiting = ["        await FileSystemThread." + WaitingMove + "temporary, path).ConfigureAwait(false);"];
        string[] renaming = ["        await FileSystemThread." + BareMove + "temporary, path, overwrite: false).ConfigureAwait(false);"];

        // A writer of a row that replaces its record without waiting is named.
        await Assert.That(MoveOffences([.. Rows(waiting, MovedRecords[1..]), .. Rows(bare, MovedRecords[..1])]).Count).IsEqualTo(1);

        // A move into place in a file with no row is named, and so is a row whose file moves nothing.
        await Assert.That(MoveOffences([.. Rows(waiting, MovedRecords), (@"BrowserAI\Somewhere\New.cs", MovesIntoPlace(waiting[0])[0])]).Count).IsEqualTo(1);
        await Assert.That(MoveOffences([.. Rows(waiting, MovedRecords[1..])]).Count).IsEqualTo(1);

        // And a rename that replaces nothing is not a move into place.
        await Assert.That(MovesIntoPlace(renaming[0])).IsEmpty();
    }

    /// <summary>
    /// No poll in the suite reads a record the product moves into place except through the
    /// product's own reader.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The maintainer's 159 a</b>, the check his 154 c asked for: polls only, the records
    /// <see cref="MovedRecords"/> names, and the product's reader required. A poll reads while a
    /// writer can be at work, which is why it polls: a bare read that meets the move is refused
    /// with <c>UnauthorizedAccessException</c>, can find the name gone for an instant, or meets
    /// a sharing violation, and throws out of the wait, where the product's reader answers that
    /// there is no record yet. <c>HomeBrowserTests</c> went red on it at <c>3626ec67</c>, and the
    /// sweep found <c>HomeTests</c> polling the same record the same way.
    /// </para>
    /// <para>
    /// <b>A poll</b> is a lambda handed to a method whose name carries <i>Wait</i>,
    /// <i>Until</i>, <i>Poll</i>, <i>Eventually</i> or <i>Reach</i>, or a <c>while</c> loop that
    /// awaits a delay. <b>A bare read</b> is <c>File.Exists</c>, one of <c>File</c>'s reads and
    /// opens, or a <c>FileStream</c> or <c>StreamReader</c> made over a path, whose argument names
    /// a record or a variable the same file assigned from one.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-13</b> against <c>HomeTests.cs</c> as it stood at <c>40911cbb</c>:
    /// it named the poll's two reads, lines 116 and 117, and nothing else.
    /// </para>
    /// <para>
    /// <b>What it cannot see</b>: a poll of another shape, a path composed in another method or
    /// a field, and a method that reads bare called from a poll. And it says nothing about the
    /// writer's side of the same race, which
    /// <see cref="EveryMoveIntoPlaceWaitsOutAHolderOfTheFileItReplaces"/> holds.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NoPollReadsARecordTheProductMovesIntoPlaceExceptThroughItsOwnReader()
    {
        var offences = new List<string>();
        var polls = 0;

        foreach (var file in RepositoryLayout.SourceFilesUnder(["tests"], ["*.cs"]))
        {
            var (found, reads) = BareReadsOfMovedRecords(await RepositoryLayout.ReadCodeAsync(file));

            polls += found;
            offences.AddRange(reads.Select(read => $"{Relative(file)}:{read}"));
        }

        await Assert.That(string.Join(Environment.NewLine, offences)).IsEmpty();

        // Not vacuous: the suite's polls were found.
        await Assert.That(polls).IsGreaterThan(0);

        // ---- The controls, over polls this tree will never hold ----------------------------
        var bareRead = "File." + "ReadAllTextAsync(";
        var exists = "File." + "Exists(";
        var named = "UpdateNoticeFile." + "FileName";

        // A waiter's lambda reading the record by its constant, and a delay loop reading it
        // through a variable the file assigned from its literal name, are each named.
        var byTheConstant = "await WaitForAsync(async () => " + exists + "Path.Combine(root, " + named + ")) && (await " + bareRead + "Path.Combine(root, " + named + "))) is \"9.0.0 seen\");";
        var byAVariable = "var record = Path.Combine(root, \"update-notice" + ".txt\");\nwhile (!" + exists + "record))\n{\n    await Task.Delay(TimeSpan.FromMilliseconds(20));\n}";

        await Assert.That(BareReadsOfMovedRecords(byTheConstant).Reads.Count).IsEqualTo(2);
        await Assert.That(BareReadsOfMovedRecords(byAVariable).Reads.Count).IsEqualTo(1);

        // The product's reader in a poll, a bare read outside one, and the record's name in a
        // poll but not in a read are each left alone.
        var throughTheReader = "await WaitForAsync(async () => await UpdateNoticeFile.ReadAsync(root) is { Seen: true });";
        var outsideAPoll = "var text = await " + bareRead + "Path.Combine(root, " + named + "));";
        var nameOnly = "await WaitForAsync(() => Task.FromResult(" + named + ".Length > 0));";

        await Assert.That(BareReadsOfMovedRecords(throughTheReader).Reads).IsEmpty();
        await Assert.That(BareReadsOfMovedRecords(outsideAPoll).Reads).IsEmpty();
        await Assert.That(BareReadsOfMovedRecords(nameOnly).Reads).IsEmpty();
    }

    /// <summary>One record the product moves into place.</summary>
    /// <param name="Writer">The file that writes it, under <c>src</c>.</param>
    /// <param name="Reader">The product's reader a poll asks.</param>
    /// <param name="Names">What a test gives the record's path with: constants and literals.</param>
    /// <param name="Keeps">What the writer keeps in place of the waiting move, or <see langword="null"/> when it takes it.</param>
    private sealed record MovedRecord(string Writer, string Reader, string[] Names, string? Keeps);

    /// <summary>The same lines, written as every row's writer, for the controls.</summary>
    /// <param name="lines">The lines.</param>
    /// <param name="rows">The rows.</param>
    /// <returns>A move per row.</returns>
    private static List<(string Writer, string Call)> Rows(string[] lines, MovedRecord[] rows) =>
        [.. rows.SelectMany(row => MovesIntoPlace(string.Join('\n', lines)).Select(call => (row.Writer, call)))];

    /// <summary>Every move into place in one file's code: the waiting move, and a move that replaces.</summary>
    /// <param name="code">The code, comment lines blanked.</param>
    /// <returns>The calls, as written.</returns>
    private static List<string> MovesIntoPlace(string code)
    {
        var calls = new List<string>();

        foreach (var needle in new[] { WaitingMove, BareMove })
        {
            for (var at = code.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = code.IndexOf(needle, at + 1, StringComparison.Ordinal))
            {
                var open = at + needle.Length - 1;
                var call = code[at..(MatchingAt(code, open, '(', ')') + 1)];
                var declared = code[..at].TrimEnd().EndsWith("Task", StringComparison.Ordinal);

                if (!declared && (needle == WaitingMove || call.Contains(Replacing, StringComparison.Ordinal)))
                {
                    calls.Add(call);
                }
            }
        }

        return calls;
    }

    /// <summary>What is wrong with a set of moves into place, against <see cref="MovedRecords"/>.</summary>
    /// <param name="moves">Each move, with the file that makes it.</param>
    /// <returns>One line per offence.</returns>
    private static List<string> MoveOffences(List<(string Writer, string Call)> moves)
    {
        var offences = new List<string>();

        foreach (var (writer, call) in moves.Where(static move => move.Writer != WaitingMoveFile))
        {
            var row = Array.Find(MovedRecords, record => string.Equals(record.Writer, writer, StringComparison.OrdinalIgnoreCase));

            if (row is null)
            {
                offences.Add($"{writer}: '{call}' moves a record into place, and no row of HouseRuleTests.MovedRecords names the file, the reader a poll asks or the names its path is given");
            }
            else if (row.Keeps is null && !call.StartsWith(WaitingMove, StringComparison.Ordinal))
            {
                offences.Add($"{writer}: '{call}' replaces the record and takes the first refusal for the answer; move it through FileSystemThread.{WaitingMove}...) so a reader holding it is waited out");
            }
        }

        foreach (var row in MovedRecords.Where(record => !moves.Exists(move => string.Equals(move.Writer, record.Writer, StringComparison.OrdinalIgnoreCase))))
        {
            offences.Add($"{row.Writer}: a row of HouseRuleTests.MovedRecords whose file moves nothing into place; delete the row or name the file that writes the record now");
        }

        return offences;
    }

    /// <summary>Every bare read of a moved record inside a poll, in one file's code.</summary>
    /// <param name="code">The code, comment lines blanked.</param>
    /// <returns>How many polls the code holds, and one line per bare read, its line first.</returns>
    private static (int Polls, List<string> Reads) BareReadsOfMovedRecords(string code)
    {
        // Every name the file assigns a record's path to, with the record it names.
        var assigned = new Dictionary<string, MovedRecord>(StringComparer.Ordinal);

        foreach (Match assignment in Assignments().Matches(code))
        {
            if (Array.Find(MovedRecords, record => NamesOneOf(assignment.Groups["value"].Value, record)) is { } named)
            {
                _ = assigned.TryAdd(assignment.Groups["name"].Value, named);
            }
        }

        var polls = Polls(code);
        var reads = new List<string>();

        foreach (var (start, poll) in polls)
        {
            foreach (Match read in BareReads().Matches(poll))
            {
                var open = read.Index + read.Length - 1;
                var argument = poll[open..(MatchingAt(poll, open, '(', ')') + 1)];
                var row = Array.Find(MovedRecords, record => NamesOneOf(argument, record))
                    ?? assigned.Where(pair => Mentions(argument, pair.Key)).Select(static pair => pair.Value).FirstOrDefault();

                if (row is not null)
                {
                    var line = code[..(start + read.Index)].Count(static character => character is '\n') + 1;

                    reads.Add($"{line.ToString(CultureInfo.InvariantCulture)}: a poll reads {argument.Trim()} with {read.Value}...), a record the product moves into place; ask {row.Reader}");
                }
            }
        }

        return (polls.Count, reads);
    }

    /// <summary>Every poll in one file's code, each with where it starts.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The polls: a waiter's arguments that hand it a lambda, and a delay loop's condition and body.</returns>
    private static List<(int Start, string Poll)> Polls(string code)
    {
        var polls = new List<(int Start, string Poll)>();

        foreach (Match waiter in Waiters().Matches(code))
        {
            var open = waiter.Index + waiter.Length - 1;
            var arguments = code[open..(MatchingAt(code, open, '(', ')') + 1)];
            var declared = Declaration().IsMatch(code[Math.Max(0, waiter.Index - 60)..waiter.Index]);

            if (!declared && arguments.Contains("=>", StringComparison.Ordinal))
            {
                polls.Add((open, arguments));
            }
        }

        foreach (Match loop in Loops().Matches(code))
        {
            var open = loop.Index + loop.Length - 1;
            var close = MatchingAt(code, open, '(', ')');
            var brace = code.IndexOf('{', close);
            var end = brace >= 0 && code[(close + 1)..brace].Trim().Length is 0 ? MatchingAt(code, brace, '{', '}') + 1 : close + 1;
            var spanned = code[open..end];

            if (spanned.Contains(".Delay(", StringComparison.Ordinal))
            {
                polls.Add((open, spanned));
            }
        }

        return polls;
    }

    /// <summary>Whether a piece of code names one of a record's names.</summary>
    /// <param name="code">The code.</param>
    /// <param name="row">The record.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool NamesOneOf(string code, MovedRecord row) =>
        Array.Exists(row.Names, name => code.Contains(name, StringComparison.Ordinal));

    /// <summary>Whether a piece of code mentions a variable by its whole name, and not as a call.</summary>
    /// <param name="code">The code.</param>
    /// <param name="name">The variable.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool Mentions(string code, string name) =>
        Regex.IsMatch(code, $@"(?<![A-Za-z0-9_]){Regex.Escape(name)}(?![A-Za-z0-9_(])", RegexOptions.CultureInvariant);

    /// <summary>Where the bracket that closes the one at <paramref name="open"/> stands, string and character literals passed over.</summary>
    /// <param name="code">The code.</param>
    /// <param name="open">Where the opening bracket stands.</param>
    /// <param name="opening">The opening bracket.</param>
    /// <param name="closing">The closing bracket.</param>
    /// <returns>Its index, or the last index of the code when nothing closes it.</returns>
    private static int MatchingAt(string code, int open, char opening, char closing)
    {
        var depth = 0;

        for (var at = open; at < code.Length; at++)
        {
            var after = PastLiteral(code, at);

            if (after > at)
            {
                at = after - 1;
            }
            else if (code[at] == opening)
            {
                depth++;
            }
            else if (code[at] == closing && --depth is 0)
            {
                return at;
            }
        }

        return code.Length - 1;
    }

    /// <summary>Where a string or character literal starting at <paramref name="at"/> ends.</summary>
    /// <param name="code">The code.</param>
    /// <param name="at">Where to look.</param>
    /// <returns>The index just past the literal, or <paramref name="at"/> itself when none starts there.</returns>
    private static int PastLiteral(string code, int at)
    {
        if (code[at] is '\'')
        {
            var end = code.IndexOf('\'', at + (code.Length > at + 1 && code[at + 1] is '\\' ? 3 : 2));

            return end < 0 ? code.Length : end + 1;
        }

        if (code[at] is not '"')
        {
            return at;
        }

        if (string.CompareOrdinal(code, at, "\"\"\"", 0, 3) is 0)
        {
            var end = code.IndexOf("\"\"\"", at + 3, StringComparison.Ordinal);

            return end < 0 ? code.Length : end + 3;
        }

        var verbatim = (at > 0 && code[at - 1] is '@') || (at > 1 && code[at - 1] is '$' && code[at - 2] is '@');

        for (var next = at + 1; next < code.Length; next++)
        {
            if (verbatim && code[next] is '"' && next + 1 < code.Length && code[next + 1] is '"')
            {
                next++;
            }
            else if (!verbatim && code[next] is '\\')
            {
                next++;
            }
            else if (code[next] is '"' || (!verbatim && code[next] is '\n'))
            {
                return next + 1;
            }
        }

        return code.Length;
    }

    /// <summary>A call to a method whose name says it waits.</summary>
    [GeneratedRegex(@"\b[A-Za-z0-9_]*(?:Wait|Until|Poll|Eventually|Reach)[A-Za-z0-9_]*\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex Waiters();

    /// <summary>The end of a method's declaration: its return type, just before its name.</summary>
    [GeneratedRegex(@"(?:Task<[^>]*>|Task|bool|void)\s+$", RegexOptions.CultureInvariant)]
    private static partial Regex Declaration();

    /// <summary>A <c>while</c> loop's condition.</summary>
    [GeneratedRegex(@"\bwhile\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex Loops();

    /// <summary>A read or an open of a file by its path.</summary>
    [GeneratedRegex(@"\bFile\.(?:ReadAll(?:Text|Bytes|Lines)Async|ReadLinesAsync|Open|OpenRead|OpenText|Exists)\(|\bnew (?:FileStream|StreamReader)\(", RegexOptions.CultureInvariant)]
    private static partial Regex BareReads();

    /// <summary>An assignment to a name: a variable, a field or a property in an initialiser.</summary>
    [GeneratedRegex(@"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=(?![=>])\s*(?<value>[^;]+);", RegexOptions.CultureInvariant)]
    private static partial Regex Assignments();
}
