// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.RegularExpressions;

namespace BrowserAI.Tests;

/// <summary>
/// An event id is a key somebody's saved log query is written against, so two
/// events may not share one and a retired one may not come back.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because the rule was broken in the tree for twenty-two days
/// and nothing could see it.</b> <c>ProxyLog</c>'s closing comment has said
/// since 2026-08-26 that ids 10, 11, 12 and 16 were retired and not to be
/// reused; <c>ChildHasGone</c> was nevertheless given 16 on 2026-09-17, and
/// <c>PageToolAbandoned</c> then took 17 -- the id 16 would have been had anybody
/// read the comment. It was found by reading, on 2026-09-22, and the comment was
/// corrected instead of the event being renumbered (Q224 b): 16 is what the shipped
/// <c>v1.0.0</c> binaries emit for <c>ChildHasGone</c>, so renumbering would
/// trade one stale meaning for a second one in the same key.
/// </para>
/// <para>
/// <b>The retired set is read out of the comment and not typed here</b>,
/// through the <c>RETIRED-EVENT-IDS:</c> marker line that comment carries. A
/// second copy of the list in this file would be a second thing to keep in step,
/// and the comment is the record a reader of an old log actually meets. The
/// marker is the machine-readable line beside the prose, which is the same
/// arrangement <c>drift-check.json</c> prescribes for sqlite.org's
/// <c>PRODUCT</c> line: read the line meant to be read, not the sentence meant
/// to be understood.
/// </para>
/// <para>
/// <b>What it cannot see.</b> It holds that an id is unique within its declaring
/// class and that a retired id is not in use. It cannot know that an id ever
/// SHIPPED under an older meaning -- that is what the retired list is for, and
/// keeping the list honest is still a person's job. Nor does it stop a
/// deliberate renumbering: moving an event to a free id passes, because that is
/// a decision and not a defect, and the retired list is where the decision
/// has to be recorded.
/// </para>
/// </remarks>
internal sealed class ProxyLogTests
{
    /// <summary>
    /// Matches one <c>EventId = N</c> inside a <c>[LoggerMessage(...)]</c>
    /// attribute, and the enclosing class declaration that scopes it.
    /// </summary>
    /// <remarks>
    /// Split so this file cannot match its own scan, the arrangement
    /// <c>HouseRuleTests</c> uses for the same reason.
    /// </remarks>
    private const string EventIdSpelling = "Event" + "Id";

    private static readonly Regex ClassDeclaration =
        new(@"^\s*(?:internal|public|private)?\s*(?:static\s+)?(?:sealed\s+)?partial\s+class\s+(?<name>\w+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MemberDeclaration =
        new(@"partial\s+void\s+(?<name>\w+)\s*\(",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RetiredMarker =
        new(@"RETIRED-" + "EVENT" + @"-IDS:\s*(?<ids>[0-9,\s]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>One declared log event, and where it was declared.</summary>
    private sealed record Event(string File, string Class, int Id, string Member, int Line);

    /// <summary>
    /// <b>No two events in one log class share an id, and no id the retired
    /// marker names is in use.</b>
    /// </summary>
    [Test]
    public async Task EveryLogEventIdIsUniqueInItsClassAndNoRetiredIdIsInUse()
    {
        var events = new List<Event>();

        foreach (var file in RepositoryLayout.ProductSourceFiles)
        {
            events.AddRange(EventsIn(Relative(file), await RepositoryLayout.ReadCodeAsync(file)));
        }

        // ---- Not vacuous. The tree declared 16 events across 3 classes on
        // 2026-09-22; a scan whose needle stopped matching reports no
        // duplicates, which reads exactly like there being none.
        await Assert.That(events.Count).IsGreaterThanOrEqualTo(14);
        await Assert.That(events.Select(e => e.Class).Distinct().Count()).IsGreaterThanOrEqualTo(2);

        await Assert.That(string.Join(Environment.NewLine, Duplicates(events))).IsEmpty();

        // ---- The retired half, read out of the comments that own the rule.
        // ⚠️ RAW, not through RepositoryLayout.ReadCodeAsync: that blanks
        // comment-only lines, which is right for the scan above -- a
        // commented-out event is not a declared one -- and would blank the very
        // lines this half exists to read.
        //
        // ⚠️ PER CLASS SINCE 2026-09-22, and the widening is what Q226 c
        // needed. It read ONE marker, out of BrowserProxy.cs, and applied it to
        // ProxyLog alone -- which was right while ProxyLog was the only class
        // with a retired id and silently covered nothing anywhere else. A
        // second class retired an id the same day.
        var retired = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        foreach (var file in RepositoryLayout.ProductSourceFiles)
        {
            foreach (var (type, ids) in RetiredIn(await File.ReadAllTextAsync(file.FullName)))
            {
                retired[type] = ids;
            }
        }

        // Not vacuous, and it names the two that exist instead of asserting a
        // count: a marker that stopped parsing would leave this empty and every
        // reuse below would pass.
        await Assert.That(retired.ContainsKey("ProxyLog"))
            .IsTrue()
            .Because("ProxyLog retired ids 10, 11 and 12 on 2026-08-26 and must carry the marker line this test reads them from");

        await Assert.That(retired.ContainsKey("ClientLivenessLog"))
            .IsTrue()
            .Because("ClientLivenessLog retired id 76 on 2026-09-22 under Q226 c and must carry the marker line this test reads it from");

        var reused = events
            .Where(e => retired.TryGetValue(e.Class, out var ids) && ids.Contains(e.Id))
            .Select(e => $"{e.File}({e.Line}): {e.Class}.{e.Member} uses {EventIdSpelling} {e.Id}, which that class's own"
                + " retired marker says is retired and not to be reused. An id is a key somebody's saved log query may"
                + " still be written against, so a retired one reassigned makes an old query answer about a new event."
                + " Give the event a free id, or -- if the reuse is deliberate -- say so in that comment and take it off the marker.")
            .ToList();

        await Assert.That(string.Join(Environment.NewLine, reused)).IsEmpty();

        // ---- The positive controls, synthetic on purpose and in both
        // directions, because each half of this test can only ever report
        // "nothing found" against the real tree.
        const string CleanSource = """
            internal static partial class SyntheticLog
            {
                [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "one")]
                public static partial void One(ILogger logger);

                [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "two")]
                public static partial void Two(ILogger logger);
            }
            """;

        var clean = EventsIn("synthetic.cs", CleanSource);

        await Assert.That(clean.Count).IsEqualTo(2);
        await Assert.That(Duplicates(clean)).IsEmpty();

        // A duplicate is caught.
        var collided = EventsIn("synthetic.cs", CleanSource.Replace(
            $"{EventIdSpelling} = 2", $"{EventIdSpelling} = 1", StringComparison.Ordinal));

        await Assert.That(collided.Count).IsEqualTo(2);
        await Assert.That(Duplicates(collided)).IsNotEmpty();

        // The same id in a DIFFERENT class is not a collision, because the id is
        // scoped to the class that declares it.
        var twoClasses = EventsIn("synthetic.cs", CleanSource + Environment.NewLine + CleanSource.Replace(
            "SyntheticLog", "OtherSyntheticLog", StringComparison.Ordinal));

        await Assert.That(twoClasses.Count).IsEqualTo(4);
        await Assert.That(Duplicates(twoClasses)).IsEmpty();

        // And the marker parse, both ways: it reads the list it is given, and it
        // does not read the prose around it -- the correction paragraph in that
        // same comment names 16 as an id that IS in use again, and a parse that
        // took numbers out of prose would retire it and fail the tree.
        const string Marker = "// RETIRED-" + "EVENT" + "-IDS: ";

        var parsed = RetiredIn($"internal static partial class SyntheticLog\n{{\n    {Marker}10, 11, 12\n}}");

        await Assert.That(parsed.Count).IsEqualTo(1);
        await Assert.That(parsed[0].Class).IsEqualTo("SyntheticLog");
        await Assert.That(parsed[0].Ids).IsEquivalentTo([10, 11, 12]);

        // It reads the LINE that is meant to be read, never the prose around
        // it: the same comment carries a correction paragraph naming 16 as an
        // id that is in use again, and a parse that took numbers out of prose
        // would retire 16 and fail the tree.
        await Assert.That(RetiredIn("// EVENT IDS 10, 11 AND 16 ARE RETIRED")).IsEmpty();
        await Assert.That(retired["ProxyLog"].Contains(16)).IsFalse();

        // Two markers in one file belong to their own classes, which is the
        // property the per-class widening rests on.
        var twoMarkers = RetiredIn(
            $"internal static partial class OneLog\n{{\n    {Marker}1, 2\n}}\n"
            + $"internal static partial class TwoLog\n{{\n    {Marker}3\n}}");

        await Assert.That(twoMarkers.Count).IsEqualTo(2);
        await Assert.That(twoMarkers[0].Class).IsEqualTo("OneLog");
        await Assert.That(twoMarkers[1].Class).IsEqualTo("TwoLog");
        await Assert.That(twoMarkers[1].Ids).IsEquivalentTo([3]);
    }

    /// <param name="file">The path to name in a failure.</param>
    /// <param name="code">The file's text.</param>
    /// <returns>Every log event the text declares, with its enclosing class.</returns>
    private static List<Event> EventsIn(string file, string code)
    {
        var lines = code.Split('\n');
        var found = new List<Event>();
        var enclosing = "(none)";

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (ClassDeclaration.Match(line) is { Success: true } declaration)
            {
                enclosing = declaration.Groups["name"].Value;
            }

            var marker = line.IndexOf($"{EventIdSpelling} = ", StringComparison.Ordinal);

            if (marker < 0)
            {
                continue;
            }

            var digits = new string([.. line[(marker + EventIdSpelling.Length + 3)..].TakeWhile(char.IsAsciiDigit)]);

            if (!int.TryParse(digits, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            // The member the attribute decorates is the next partial void below
            // it; the attribute may span several lines, so this looks forward
            // and not at i + 1.
            var member = "(unnamed)";

            for (var j = i + 1; j < Math.Min(i + 12, lines.Length); j++)
            {
                if (MemberDeclaration.Match(lines[j]) is { Success: true } declared)
                {
                    member = declared.Groups["name"].Value;
                    break;
                }
            }

            found.Add(new Event(file, enclosing, id, member, i + 1));
        }

        return found;
    }

    /// <param name="events">Everything the scan found.</param>
    /// <returns>One line per id declared twice in one class, naming both sites.</returns>
    private static List<string> Duplicates(IReadOnlyList<Event> events) =>
    [
        .. events
            .GroupBy(e => (e.Class, e.Id))
            .Where(g => g.Count() > 1)
            .Select(g =>
                $"{g.Key.Class} declares {EventIdSpelling} {g.Key.Id} {g.Count()} times -- "
                + string.Join(", ", g.Select(e => $"{e.Member} at {e.File}({e.Line})"))
                + ". An id identifies an event to whoever reads the log, so two events under one id make a saved query ambiguous."),
    ];

    /// <param name="code">The text of the file carrying the retired-id rule.</param>
    /// <returns>The ids its marker line names, in the order it names them.</returns>
    private static List<(string Class, List<int> Ids)> RetiredIn(string code)
    {
        var lines = code.Split('\n');
        var found = new List<(string, List<int>)>();
        var enclosing = "(none)";

        foreach (var line in lines)
        {
            if (ClassDeclaration.Match(line) is { Success: true } declaration)
            {
                enclosing = declaration.Groups["name"].Value;
            }

            if (RetiredMarker.Match(line) is not { Success: true } marker)
            {
                continue;
            }

            found.Add((enclosing, [.. marker.Groups["ids"].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => int.Parse(part, CultureInfo.InvariantCulture))]));
        }

        return found;
    }

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName);
}
