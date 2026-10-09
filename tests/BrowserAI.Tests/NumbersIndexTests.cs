// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BrowserAI.App.Page;
using BrowserAI.Relay;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>
/// The numbers index and the code, held against each other: every number in a named
/// class has a row with its value, every row names a number that exists and holds that
/// value, and no literal duration lives anywhere else in the product.
/// </summary>
/// <remarks>
/// <para>
/// <b>Decision F3 of the one-binary build</b>, after the maintainer's question of
/// 2026-10-07, verbatim: <i>"Maybe we should start tracking all magical numbers used in
/// this project in an index of sorts so that we can at a later date re-check the data by
/// using the provenance of these records to re-determine if the number is still
/// accurate?"</i>, and his <i>"f3 a"</i> of 2026-10-08. The index is
/// <see href="../../kb/numbers.md">kb/numbers.md</see>; what a row says and what this
/// class can and cannot see are written at its head.
/// </para>
/// <para>
/// <b>Both directions, and the second is not symmetry for its own sake.</b> A number
/// declared with no row is a number nobody can trace, which is what F3 exists to end. A
/// row naming a member that is gone, or holding a value the code no longer has, is
/// provenance for a number the product does not use, and it reads exactly like provenance
/// for one it does.
/// </para>
/// <para>
/// <b>Planted red before it went in, 2026-10-09</b>, one plant at a time, against the
/// tree with the index and the sweep in place. A row taken out of the index turned the
/// first arm red, naming the member; a value changed in a row turned both directions
/// red, naming the member and the line; a row for a member nobody declared turned the
/// second, naming the line; and a literal duration written back into an owner's source
/// turned the scan, naming the file, the line and the shape. The controls' arm went red
/// on the first two plants as well, because it counted every disagreement the doctored
/// index had, the plant's own included; it now counts only what a control adds to what
/// the index says that day.
/// </para>
/// </remarks>
internal sealed partial class NumbersIndexTests
{
    /// <summary>The classes every tunable duration in the product is declared in.</summary>
    /// <remarks>
    /// A class joins this list in the same change that names it in the index's own
    /// sentence about what holds it; each maps to exactly one source file, which the scan
    /// leaves out, and that mapping is asserted, so a rename cannot exempt a stranger.
    /// </remarks>
    internal static Type[] NamedClasses { get; } =
    [
        typeof(SessionTimes),
        typeof(ProcessBounds),
        typeof(UpdateBudgets),
        typeof(RelayConstants),
        typeof(ClientLimits),
        typeof(WordingTimes),
    ];

    /// <summary>The four kinds a row may be, as the index defines them.</summary>
    private static readonly string[] Kinds = ["Measured", "Derived", "Chosen", "Upstream"];

    /// <summary>The cells of a row of the index, after the empty ones outside the first and last pipe.</summary>
    private const int Cells = 9;

    private static string IndexPath { get; } = Path.Combine(RepositoryLayout.Root.FullName, "kb", "numbers.md");

    /// <summary>The three assemblies the product is built from, which a row may name a member of.</summary>
    private static Assembly[] ProductAssemblies { get; } =
        [typeof(SessionTimes).Assembly, typeof(RelayConstants).Assembly, typeof(PageGate).Assembly];

    /// <summary>
    /// Every number in a named class has a row in the index, and the row holds the value
    /// the code holds.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryNumberInANamedClassHasARowWithItsValue()
    {
        var (rows, _) = Read(await File.ReadAllLinesAsync(IndexPath));
        var named = NamedNumbers();

        await Assert.That(string.Join(Environment.NewLine, MissingOrWrong(named, rows))).IsEmpty()
            .Because("every number in a named class is a row of kb/numbers.md with the value the code holds: add the row, or correct its value from the code, in the same change");

        // Not vacuous: a reflection that found nothing would agree with any index.
        foreach (var type in NamedClasses)
        {
            await Assert.That(named.Count(number => number.Symbol.StartsWith(type.Name + ".", StringComparison.Ordinal))).IsGreaterThan(0)
                .Because($"{type.Name} is a named class and declares no number this reflection can see");
        }
    }

    /// <summary>
    /// Every row of the index names a static number the product declares, and the number
    /// holds the value the row says.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryRowNamesANumberThatExistsAndHoldsItsValue()
    {
        var (rows, unreadable) = Read(await File.ReadAllLinesAsync(IndexPath));

        await Assert.That(string.Join(Environment.NewLine, unreadable)).IsEmpty()
            .Because("every line of a table in kb/numbers.md is a header, a separator or a row of nine cells whose first is a `Type.Member` code span; a line the parser cannot read would be a row that vanished from every check");
        await Assert.That(string.Join(Environment.NewLine, Unresolvable(rows))).IsEmpty();

        // Each symbol once: two rows for one number are two provenances for it.
        var twice = rows.GroupBy(row => row.Symbol, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key);

        await Assert.That(string.Join(", ", twice)).IsEmpty();
        await Assert.That(rows.Count).IsGreaterThanOrEqualTo(NamedNumbers().Count);
    }

    /// <summary>
    /// Every row says what kind of number it is, when it was established and how to
    /// check it again, and a derived row names the rows it comes from.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryRowSaysItsKindWhenItWasEstablishedAndHowToCheckItAgain()
    {
        var (rows, _) = Read(await File.ReadAllLinesAsync(IndexPath));

        await Assert.That(string.Join(Environment.NewLine, Incomplete(rows))).IsEmpty();
        await Assert.That(rows.Count).IsGreaterThan(0);
    }

    /// <summary>
    /// The comparisons above, against an index doctored each way, so that they cannot
    /// pass by asking nothing.
    /// </summary>
    /// <remarks>
    /// <b>A check that returns no disagreement needs a positive control</b>, because it
    /// reads the same whether the index is complete or the comparison is broken. Each
    /// control below changes one thing and expects exactly that thing back.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheComparisonsFindAMissingRowAWrongValueARowForNothingAndALineTheyCannotRead()
    {
        var lines = await File.ReadAllLinesAsync(IndexPath);
        var (rows, _) = Read(lines);
        var named = NamedNumbers();

        // Each control is read against what the index says today, so that it asks
        // about the one thing it changed and not about the rest of the index: a row
        // missing from the real index is the first arm's to report, not this one's.
        var today = MissingOrWrong(named, rows);

        // One row taken out.
        var taken = named.First(number => number.Value is TimeSpan && rows.Any(row => row.Symbol == number.Symbol)).Symbol;
        var missing = MissingOrWrong(named, [.. rows.Where(row => row.Symbol != taken)]).Except(today).ToList();

        await Assert.That(missing.Count).IsEqualTo(1);
        await Assert.That(missing[0]).Contains(taken);

        // One value changed.
        var changed = rows.First(row => row.Symbol == taken) with { Value = "`7 ms`" };
        var wrong = MissingOrWrong(named, [.. rows.Select(row => row.Symbol == taken ? changed : row)]).Except(today).ToList();

        await Assert.That(wrong.Count).IsEqualTo(1);
        await Assert.That(wrong[0]).Contains(taken);

        // A row for a member nobody declared, and one for a type nobody declared.
        var nobody = rows[0] with { Symbol = nameof(SessionTimes) + ".ANumberNobodyDeclared" };
        var nowhere = rows[0] with { Symbol = "ATypeNobodyDeclared.Bound" };

        await Assert.That(Unresolvable([nobody, nowhere]).Count).IsEqualTo(2);

        // A value the index cannot spell.
        var unspelled = rows[0] with { Value = "`ten minutes`" };

        await Assert.That(Unresolvable([unspelled]).Count).IsEqualTo(1);

        // A row with a pipe in a cell, which splits it past nine cells, and a header so
        // the table is a table.
        var header = lines.First(line => line.StartsWith("| Symbol |", StringComparison.Ordinal));
        var broken = "| `SessionTimes.ChildShutdown` | `5 s` | a cell | with a stray | pipe | **Chosen** | it | 2026-10-09 | nothing |";
        var (_, unreadable) = Read([header, "|---|---|---|---|---|---|---|", broken]);

        await Assert.That(unreadable.Count).IsEqualTo(1);

        // An incomplete row: no kind, no date.
        var bare = rows[0] with { Kind = "chosen", Established = "the first build" };

        await Assert.That(Incomplete([bare]).Count).IsEqualTo(2);
    }

    /// <summary>
    /// Every wait a tool call can meet ends inside the stricter client's limit on one
    /// call, as the table of "The first call" in the one-binary design says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The maintainer's question, R, 2026-10-08, verbatim:</b> <i>"Do all these
    /// timeouts work within both the claude or codex timeouts?"</i> The table that answered
    /// it, answered in turn with <i>"r ok"</i>, names four waits, and they are these four.
    /// Nothing here can find a fifth: a new wait inside a tool call joins this list by
    /// hand.
    /// </para>
    /// <para>
    /// <b>Not planted red against the product</b>, because no number in the tree breaks
    /// it and none should be made to. The control at the end is a wait of exactly the
    /// limit, which the same predicate refuses.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryWaitAToolCallCanMeetEndsInsideTheStricterClientsLimit()
    {
        (string Name, TimeSpan Wait)[] waits =
        [
            ("RelayConstants.HoldBound", RelayConstants.HoldBound),
            ("RelayConstants.HangBound", RelayConstants.HangBound),
            ("SessionTimes.BrowserCloseCap", SessionTimes.BrowserCloseCap),
            ("SessionTimes.PageToolBudget", SessionTimes.PageToolBudget),
        ];

        await Assert.That(string.Join(Environment.NewLine, Outside(waits, ClientLimits.StricterToolCall))).IsEmpty();

        // The stricter limit is the shorter of the two clients'.
        await Assert.That(ClientLimits.StricterToolCall).IsLessThanOrEqualTo(ClientLimits.CodexToolCall);
        await Assert.That(ClientLimits.StricterToolCall).IsLessThanOrEqualTo(ClientLimits.ClaudeCodeToolCall);
        await Assert.That(ClientLimits.StricterToolCall == ClientLimits.CodexToolCall || ClientLimits.StricterToolCall == ClientLimits.ClaudeCodeToolCall).IsTrue();

        // The control: a wait as long as the limit is outside it.
        await Assert.That(Outside([("a wait of the limit itself", ClientLimits.StricterToolCall)], ClientLimits.StricterToolCall).Count).IsEqualTo(1);
    }

    /// <summary>
    /// No source file of the product outside the named classes writes a duration as a
    /// number.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NoLiteralDurationLivesOutsideTheNamedClasses()
    {
        var exempt = NamedClassFiles();

        await Assert.That(exempt.Count).IsEqualTo(NamedClasses.Length);

        var offenders = new List<string>();
        var scanned = 0;

        foreach (var file in RepositoryLayout.ProductSourceFiles)
        {
            if (exempt.Contains(file.FullName))
            {
                continue;
            }

            scanned++;

            offenders.AddRange(LiteralDurations(await File.ReadAllTextAsync(file.FullName))
                .Select(hit => $"{Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName)}:{hit.Line}: {hit.Shape}: {hit.Text}"));
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty()
            .Because("a duration the product tunes is declared in a named class and has a row in kb/numbers.md; the owner keeps a member whose value is the named one");
        await Assert.That(scanned).IsGreaterThan(100);

        // The positive control on real files: the named classes' own declarations are
        // literal durations, and the same scan finds them in every one.
        foreach (var path in exempt)
        {
            await Assert.That(LiteralDurations(await File.ReadAllTextAsync(path)).Count).IsGreaterThan(0)
                .Because($"{Path.GetFileName(path)} declares literal durations, and a scan that cannot see them there sees nothing anywhere");
        }
    }

    /// <summary>
    /// The scan finds every shape it names, and lets through what the index says it
    /// lets through.
    /// </summary>
    /// <remarks>
    /// Synthetic, because the tree is clean and a clean tree reads the same as a scan
    /// whose patterns stopped matching. The timed wait on a child's exit is composed from
    /// halves, so that <c>ProcessLogTests</c>' own scan of the suite does not read this
    /// sample as a call.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheScanFindsEveryShapeItNamesAndPassesWhatItLetsThrough()
    {
        string[] caught =
        [
            "    public static TimeSpan Bound { get; } = TimeSpan.FromSeconds(5);",
            "    private static readonly TimeSpan Half = TimeSpan.FromMilliseconds(0.5);",
            "    public static TimeSpan Pair { get; } = TimeSpan.FromMinutes(1, 30);",
            "        var span = new TimeSpan(0, 0, 5);",
            "        Thread.Sleep(250);",
            "        await Task.Delay(100, token);",
            "        handle.WaitOne(2000);",
            "        process.WaitFor" + "Exit(5000);",
            "        work.Wait(1000);",
            "        source.CancelAfter(1000);",
            "        using var bounded = new CancellationTokenSource(3000);",
            "    private const int RetentionDays = 30;",
            "    public static int PollMilliseconds { get; } = 250;",
            "    private const long TimeoutMs = 5_000;",
            "        var delay = 5;",
            "                delay = Math.Min(delay * 2, 100);",
            "        var cutoff = DateTime.UtcNow.AddDays(-30);",
        ];

        foreach (var line in caught)
        {
            await Assert.That(LiteralDurations(line).Count).IsGreaterThan(0).Because(line);
        }

        string[] passed =
        [
            "    // Thread.Sleep(250) in a comment",
            "    /// <summary>TimeSpan.FromSeconds(5) in a documentation comment.</summary>",
            "    public static TimeSpan Bound { get; } = SessionTimes.ChildShutdown;",
            "        ArgumentOutOfRangeException.ThrowIfGreaterThan(period, TimeSpan.FromMilliseconds(int.MaxValue));",
            "        var alive = WaitForSingleObject(handle, 0);",
            "        Thread.Sleep(0);",
            "    public const int NoIdleTimeout = 0;",
            "    public const int ErrorSemaphoreTimeout = 121;",
            "        var estimate = TimeSpan.FromSeconds(remaining * 8d / rate);",
            "        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);",
            "                delay = Math.Min(delay * 2, SessionTimes.RenameRetryLongestDelayMilliseconds);",
        ];

        foreach (var line in passed)
        {
            await Assert.That(LiteralDurations(line).Count).IsEqualTo(0).Because(line);
        }
    }

    /// <summary>One row of the index, its cells trimmed.</summary>
    /// <param name="Line">The line it is written on, from one.</param>
    /// <param name="Symbol">The member, as <c>Type.Member</c>.</param>
    /// <param name="Value">The value cell, a code span.</param>
    /// <param name="Governs">What it governs.</param>
    /// <param name="Kind">Its kind, in bold first.</param>
    /// <param name="Evidence">What it rests on.</param>
    /// <param name="Established">When, and under which versions.</param>
    /// <param name="Recheck">How to check it again.</param>
    internal sealed record NumberRow(
        int Line,
        string Symbol,
        string Value,
        string Governs,
        string Kind,
        string Evidence,
        string Established,
        string Recheck);

    /// <summary>Every row of the index, and every table line that is neither a row nor a header nor a separator.</summary>
    /// <param name="lines">The index's lines.</param>
    /// <returns>The rows, and a sentence for each line that could not be read.</returns>
    internal static (List<NumberRow> Rows, List<string> Unreadable) Read(IReadOnlyList<string> lines)
    {
        var rows = new List<NumberRow>();
        var unreadable = new List<string>();

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];

            if (!line.StartsWith('|'))
            {
                continue;
            }

            var cells = HazardIndex.SplitRow(line);

            if (cells.Skip(1).SkipLast(1).All(cell => cell.Trim().Trim('-', ':').Length is 0))
            {
                continue;
            }

            if (cells.Length is Cells && cells[1].Trim() is "Symbol")
            {
                continue;
            }

            if (cells.Length is not Cells || SymbolCell().Match(cells[1].Trim()) is not { Success: true } symbol)
            {
                unreadable.Add($"kb/numbers.md:{index + 1}: {cells.Length} cells where a row has {Cells}, or a first cell that is not a `Type.Member` code span: {line}");
                continue;
            }

            rows.Add(new NumberRow(
                index + 1,
                symbol.Groups["symbol"].Value,
                cells[2].Trim(),
                cells[3].Trim(),
                cells[4].Trim(),
                cells[5].Trim(),
                cells[6].Trim(),
                cells[7].Trim()));
        }

        return (rows, unreadable);
    }

    /// <summary>Every static number the named classes declare, with its value.</summary>
    /// <returns>Each as <c>Type.Member</c> and its value, normalised.</returns>
    internal static List<(string Symbol, object Value)> NamedNumbers() =>
    [
        .. NamedClasses.SelectMany(type =>
            type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(field => !field.IsDefined(typeof(CompilerGeneratedAttribute)) && IsNumber(field.FieldType))
                .Select(field => ($"{type.Name}.{field.Name}", Normalise(ValueOf(field))))
                .Concat(type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(property => IsNumber(property.PropertyType) && property.GetIndexParameters().Length is 0)
                    .Select(property => ($"{type.Name}.{property.Name}", Normalise(property.GetValue(null)!))))),
    ];

    /// <summary>Every named number with no row, or with a row that says another value.</summary>
    /// <param name="named">The named numbers.</param>
    /// <param name="rows">The index's rows.</param>
    /// <returns>A sentence for each disagreement, naming the symbol.</returns>
    internal static List<string> MissingOrWrong(IReadOnlyList<(string Symbol, object Value)> named, IReadOnlyList<NumberRow> rows)
    {
        var found = new List<string>();

        foreach (var (symbol, value) in named)
        {
            var row = rows.FirstOrDefault(candidate => candidate.Symbol == symbol);

            if (row is null)
            {
                found.Add($"{symbol} holds {Spell(value)} and has no row in kb/numbers.md");
            }
            else if (ParseValue(row.Value) is not { } said || !said.Equals(value))
            {
                found.Add($"{symbol} holds {Spell(value)} and its row on line {row.Line} says {row.Value}");
            }
        }

        return found;
    }

    /// <summary>Every row whose symbol names no static number, or whose number holds another value.</summary>
    /// <param name="rows">The index's rows.</param>
    /// <returns>A sentence for each, naming the row.</returns>
    internal static List<string> Unresolvable(IReadOnlyList<NumberRow> rows)
    {
        var found = new List<string>();

        foreach (var row in rows)
        {
            var (resolved, problem) = Resolve(row.Symbol);

            if (resolved is null)
            {
                found.Add($"line {row.Line}: {row.Symbol} {problem}");
            }
            else if (ParseValue(row.Value) is not { } said)
            {
                found.Add($"line {row.Line}: {row.Symbol}'s value {row.Value} is not a code span the index can read: a duration in ms, s, min, h or d, or a number with KiB or MiB or nothing");
            }
            else if (!said.Equals(resolved))
            {
                found.Add($"line {row.Line}: {row.Symbol} holds {Spell(resolved)} and the row says {row.Value}");
            }
        }

        return found;
    }

    /// <summary>Every row that leaves out its kind, its date or how to check it again.</summary>
    /// <param name="rows">The index's rows.</param>
    /// <returns>A sentence for each thing left out.</returns>
    internal static List<string> Incomplete(IReadOnlyList<NumberRow> rows)
    {
        var found = new List<string>();
        var symbols = rows.Select(row => row.Symbol).ToHashSet(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (KindCell().Match(row.Kind) is not { Success: true } kind || !Kinds.Contains(kind.Groups["kind"].Value, StringComparer.Ordinal))
            {
                found.Add($"line {row.Line}: {row.Symbol}'s kind does not open with one of {string.Join(", ", Kinds.Select(name => "**" + name + "**"))}");
            }
            else if (kind.Groups["kind"].Value is "Derived"
                && !CodeSpan().Matches(row.Kind).Select(span => span.Groups["text"].Value).Any(text => text != row.Symbol && symbols.Contains(text)))
            {
                found.Add($"line {row.Line}: {row.Symbol} is derived and its kind names no other row it is derived from");
            }

            if (!IsoDate().IsMatch(row.Established))
            {
                found.Add($"line {row.Line}: {row.Symbol} carries no date in its Established cell");
            }

            if (row.Governs.Length is 0 || row.Evidence.Length is 0 || row.Recheck.Length is 0)
            {
                found.Add($"line {row.Line}: {row.Symbol} leaves what it governs, its evidence or its re-check empty");
            }
        }

        return found;
    }

    /// <summary>Every wait that is not shorter than the limit.</summary>
    /// <param name="waits">The waits, named.</param>
    /// <param name="limit">The limit they must end inside.</param>
    /// <returns>A sentence for each.</returns>
    internal static List<string> Outside(IReadOnlyList<(string Name, TimeSpan Wait)> waits, TimeSpan limit) =>
    [
        .. waits
            .Where(wait => wait.Wait >= limit)
            .Select(wait => $"{wait.Name} is {Spell(wait.Wait)}, and a client gives up on the call at {Spell(limit)} before BrowserAI's answer arrives"),
    ];

    /// <summary>Every literal duration in one file's code, comments left out.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>Each hit, with its line from one, the shape it has and the line's text.</returns>
    internal static List<(int Line, string Shape, string Text)> LiteralDurations(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var code = text.ToCharArray();

        foreach (var (start, end) in Commentary.SpansOf(text, ".cs"))
        {
            for (var at = start; at < end; at++)
            {
                if (code[at] is not '\n' and not '\r')
                {
                    code[at] = ' ';
                }
            }
        }

        var bare = new string(code);
        var lines = text.Split('\n');
        var found = new List<(int, string, string)>();

        foreach (var (shape, pattern) in Shapes)
        {
            foreach (Match match in pattern.Matches(bare))
            {
                var line = bare.AsSpan(0, match.Index).Count('\n') + 1;

                found.Add((line, shape, lines[line - 1].Trim()));
            }
        }

        return found;
    }

    /// <summary>The shapes a duration is written in, each with the words a failure names it by.</summary>
    private static (string Shape, Regex Pattern)[] Shapes { get; } =
    [
        ("a TimeSpan built from a number", TimeSpanFromANumber()),
        ("a TimeSpan constructed from a number", TimeSpanConstructedFromANumber()),
        ("a wait handed a number", WaitHandedANumber()),
        ("a cancellation handed a number", CancellationHandedANumber()),
        ("a number named for a unit", NumberNamedForAUnit()),
        ("a local named for a unit", LocalNamedForAUnit()),
        ("a back-off capped at a number", BackoffCappedAtANumber()),
        ("a date moved by a number", DateMovedByANumber()),
    ];

    /// <summary>The source file of each named class, by its full path.</summary>
    /// <returns>The paths, one per named class.</returns>
    private static HashSet<string> NamedClassFiles()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in NamedClasses)
        {
            var candidates = RepositoryLayout.ProductSourceFiles.Where(file => file.Name == type.Name + ".cs").ToList();

            if (candidates.Count is 1)
            {
                _ = files.Add(candidates[0].FullName);
            }
        }

        return files;
    }

    private static (object? Value, string Problem) Resolve(string symbol)
    {
        var dot = symbol.LastIndexOf('.');
        var typeName = symbol[..dot];
        var memberName = symbol[(dot + 1)..];
        var types = ProductTypes.Where(type => type.Name == typeName || type.FullName == typeName).ToList();

        if (types.Count is 0)
        {
            return (null, "names no type the product declares");
        }

        if (types.Count > 1)
        {
            return (null, $"names {types.Count} types the product declares; write the namespace in front");
        }

        const BindingFlags Statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

        if (types[0].GetField(memberName, Statics) is { } field && IsNumber(field.FieldType))
        {
            return (Normalise(ValueOf(field)), string.Empty);
        }

        if (types[0].GetProperty(memberName, Statics) is { } property && IsNumber(property.PropertyType) && property.GetIndexParameters().Length is 0)
        {
            return (Normalise(property.GetValue(null)!), string.Empty);
        }

        return (null, $"names no static number on {types[0].FullName}");
    }

    private static List<Type> ProductTypes { get; } = [.. ProductAssemblies.SelectMany(assembly => assembly.GetTypes())];

    private static object ValueOf(FieldInfo field) =>
        (field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null))!;

    private static bool IsNumber(Type type) =>
        type == typeof(TimeSpan)
        || type == typeof(sbyte) || type == typeof(byte)
        || type == typeof(short) || type == typeof(ushort)
        || type == typeof(int) || type == typeof(uint)
        || type == typeof(long) || type == typeof(ulong)
        || type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    /// <summary>A value as the index compares it: a duration, or a whole number as a <see cref="long"/>.</summary>
    /// <param name="value">The member's value.</param>
    /// <returns>The value to compare; a fractional number stays what it was, and no row can equal it.</returns>
    private static object Normalise(object value) => value switch
    {
        TimeSpan duration => duration,
        sbyte or byte or short or ushort or int or uint or long => Convert.ToInt64(value, CultureInfo.InvariantCulture),
        ulong whole when whole <= long.MaxValue => (long)whole,
        double fraction when fraction == Math.Floor(fraction) && Math.Abs(fraction) < 1e15 => (long)fraction,
        float fraction when fraction == MathF.Floor(fraction) && MathF.Abs(fraction) < 1e7f => (long)fraction,
        decimal fraction when fraction == decimal.Floor(fraction) => (long)fraction,
        _ => value,
    };

    /// <summary>A value cell as the index spells it.</summary>
    /// <param name="cell">The cell, trimmed.</param>
    /// <returns>The value, or null when the cell is not one the index can read.</returns>
    private static object? ParseValue(string cell)
    {
        if (ValueCell().Match(cell) is not { Success: true } span)
        {
            return null;
        }

        var text = span.Groups["text"].Value;

        if (DurationText().Match(text) is { Success: true } duration)
        {
            var amount = double.Parse(duration.Groups["amount"].Value.Replace(",", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);

            return duration.Groups["unit"].Value switch
            {
                "ms" => TimeSpan.FromMilliseconds(amount),
                "s" => TimeSpan.FromSeconds(amount),
                "min" => TimeSpan.FromMinutes(amount),
                "h" => TimeSpan.FromHours(amount),
                "d" => TimeSpan.FromDays(amount),
                _ => null,
            };
        }

        if (CountText().Match(text) is { Success: true } count)
        {
            var amount = long.Parse(count.Groups["amount"].Value.Replace(",", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);

            return count.Groups["unit"].Value switch
            {
                "" => amount,
                "KiB" => amount * 1024,
                "MiB" => amount * 1024 * 1024,
                _ => null,
            };
        }

        return null;
    }

    private static string Spell(object value) => value switch
    {
        TimeSpan duration => duration.ToString("c", CultureInfo.InvariantCulture),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    [GeneratedRegex(@"^`(?<symbol>(?:[A-Za-z_][A-Za-z0-9_]*\.)+[A-Za-z_][A-Za-z0-9_]*)`$")]
    private static partial Regex SymbolCell();

    [GeneratedRegex("^`(?<text>[^`]+)`$")]
    private static partial Regex ValueCell();

    [GeneratedRegex(@"^(?<amount>\d[\d,]*(?:\.\d+)?) (?<unit>ms|s|min|h|d)$")]
    private static partial Regex DurationText();

    [GeneratedRegex(@"^(?<amount>\d[\d,]*)(?: (?<unit>KiB|MiB))?$")]
    private static partial Regex CountText();

    [GeneratedRegex(@"^\*\*(?<kind>[A-Za-z]+)\*\*")]
    private static partial Regex KindCell();

    [GeneratedRegex("`(?<text>[^`]+)`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}\b")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"\bTimeSpan\s*\.\s*From(?:Ticks|Microseconds|Milliseconds|Seconds|Minutes|Hours|Days)\s*\(\s*[-+]?\d[\d_]*(?:\.\d+)?[dDfFmMlLuU]?\s*[,)]")]
    private static partial Regex TimeSpanFromANumber();

    [GeneratedRegex(@"\bnew\s+TimeSpan\s*\(\s*[-+]?\d")]
    private static partial Regex TimeSpanConstructedFromANumber();

    [GeneratedRegex(@"(?:\bThread\s*\.\s*Sleep|\bTask\s*\.\s*Delay|\bSleepEx|\.\s*Wait(?:One|ForExit|ForExitAsync|Async)?|\.\s*CancelAfter)\s*\(\s*[-+]?[1-9]")]
    private static partial Regex WaitHandedANumber();

    [GeneratedRegex(@"\bnew\s+CancellationTokenSource\s*\(\s*[-+]?[1-9]")]
    private static partial Regex CancellationHandedANumber();

    [GeneratedRegex(@"\b(?:int|uint|long|ulong|short|ushort|double|float|decimal)\s+(?!Error)\w*(?:[Mm]illiseconds|[Ss]econds|[Mm]inutes|[Hh]ours|[Dd]ays|Ms|[Tt]imeout|[Dd]elay|[Ii]nterval|[Pp]eriod)\s*(?:\{\s*get;\s*(?:init;\s*)?\}\s*)?=>?\s*[-+]?[1-9]")]
    private static partial Regex NumberNamedForAUnit();

    [GeneratedRegex(@"\bvar\s+\w*(?:[Mm]illiseconds|[Ss]econds|[Mm]inutes|[Hh]ours|[Dd]ays|Ms|[Tt]imeout|[Dd]elay|[Ii]nterval|[Pp]eriod)\s*=\s*[-+]?[1-9]")]
    private static partial Regex LocalNamedForAUnit();

    [GeneratedRegex(@"\bMath\s*\.\s*(?:Min|Max|Clamp)\s*\(\s*\w*(?:[Mm]illiseconds|[Ss]econds|[Mm]inutes|[Hh]ours|[Dd]ays|Ms|[Tt]imeout|[Dd]elay|[Ii]nterval|[Pp]eriod)\w*\s*\*\s*[\d.]+\s*,\s*[-+]?[1-9]")]
    private static partial Regex BackoffCappedAtANumber();

    [GeneratedRegex(@"\.\s*Add(?:Milliseconds|Seconds|Minutes|Hours|Days|Ticks)\s*\(\s*[-+]?[1-9]")]
    private static partial Regex DateMovedByANumber();
}
