// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Tests.Harness;

/// <summary>What a run can say about whether it was filtered.</summary>
internal enum SuiteFilterVerdict
{
    /// <summary>
    /// Nothing took the reading, or the framework had not populated its
    /// contexts when it was taken, so this run cannot say either way.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>This is the state that must never be spelled <c>FULL RUN</c>.</b>
    /// An instrument that reads <i>no filter</i> when it is really reading
    /// <i>nothing at all</i> is a false green, and a false green about coverage
    /// is worse than no row: the run then publishes, in its own output, the one
    /// sentence a reader would have gone looking for.
    /// </remarks>
    Unread,

    /// <summary>
    /// The platform handed the framework no filter, so the run discovered the
    /// whole assembly.
    /// </summary>
    NotFiltered,

    /// <summary>
    /// The platform handed the framework a filter, so this run is evidence about
    /// what it selected and about nothing else.
    /// </summary>
    Filtered,

    /// <summary>
    /// The two contexts the framework populates from one value carry different
    /// values, so the instrument is broken and may not be read in either
    /// direction.
    /// </summary>
    Disagreed,
}

/// <summary>What a verdict amounts to for the run that produced it.</summary>
internal enum SuiteFilterDecision
{
    /// <summary>Nothing here stops the run.</summary>
    Proceed,

    /// <summary>The run must fail, and <see cref="SuiteFilter.Refusal"/> says why.</summary>
    Refuse,
}

/// <summary>
/// What the framework held about this run's filter at the instant it was read.
/// </summary>
/// <param name="Taken">
/// Whether anything read it at all. <see langword="false"/> is the initial value
/// and is never overwritten by a reading that failed.
/// </param>
/// <param name="SessionContextPopulated">
/// Whether <see cref="TestSessionContext.Current"/> was non-null at that
/// instant, which is the only positive witness that the framework had populated
/// its contexts. Without it a <see langword="null"/> in <paramref name="Global"/>
/// means nothing, because <see cref="GlobalContext.Current"/> lazily creates an
/// empty instance for whoever asks first.
/// </param>
/// <param name="Global">
/// <see cref="GlobalContext.TestFilter"/> — TUnit's stringification of the
/// <c>ITestExecutionFilter</c> the platform put on the execute request.
/// </param>
/// <param name="Session">
/// <see cref="TestSessionContext.TestFilter"/>, set from the same value by the
/// same object, and carried separately so that a disagreement between them is a
/// state rather than a coin toss.
/// </param>
internal sealed record SuiteFilterReading(bool Taken, bool SessionContextPopulated, string? Global, string? Session)
{
    /// <summary>The reading a run has before anything has read anything.</summary>
    public static SuiteFilterReading NotTaken { get; } = new(false, false, null, null);
}

/// <summary>
/// Whether this run was filtered, read from the platform's own filter and never
/// from this process's command line.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect this closes is not that a filtered run is wrong — it is that a
/// filtered run is indistinguishable from a full one in everything the run
/// publishes about itself.</b> Every number a filtered run prints is true of what
/// it ran; what is false is the sentence a human writes underneath it, and
/// [`CLAUDE.md`](../../../CLAUDE.md) says plainly that no test can read that
/// sentence. What a test *can* do is make the run state the premise, so the
/// sentence can be checked against something. That is this row, and it is why it
/// is a row rather than a refusal: a mechanism that forbade filtered runs would
/// forbid the iteration loop the rule exists to permit.
/// </para>
/// <para>
/// ⚠️ <b>Read from MTP's own filter, and the distinction is the whole point.</b>
/// <c>TUnitTestFramework.ExecuteRequestAsync</c> takes the
/// <c>ITestExecutionFilter</c> off <c>ExecuteRequestContext.Request</c>, hands it
/// to <c>FilterParser.StringifyFilter</c>, and gives the string to
/// <c>ContextProvider</c>, which is what fills
/// <see cref="GlobalContext.TestFilter"/> and
/// <see cref="TestSessionContext.TestFilter"/>. So the value below is the filter
/// the framework actually applied, whatever route it arrived by — a
/// <c>TreeNodeFilter</c> from <c>--treenode-filter</c>, a
/// <c>TestNodeUidListFilter</c> from an IDE's selection, or nothing at all.
/// <b><c>Environment.GetCommandLineArgs()</c> is deliberately not consulted</b>:
/// it is unconfirmed that a filter reaches the test host's own command line under
/// <c>dotnet test</c>, and a row reading <i>full run</i> on a filtered run is the
/// exact false green this type exists to prevent.
/// </para>
/// <para>
/// <b><c>ICommandLineOptions</c> was the first choice and it is unreachable from
/// a test — established by reading the resolved packages rather than by
/// assuming.</b> Decompiled 2026-08-24 at TUnit <b>1.65.0</b> /
/// <c>Microsoft.Testing.Platform</c> <b>2.3.3</b>: the platform's
/// <c>ICommandLineOptions</c> is handed to <c>TUnitServiceProvider</c>, which is
/// <c>internal</c>, holds it in a plain property, and never registers it in the
/// <c>_services</c> dictionary its own <c>GetService</c> reads — so even the one
/// public seam that surfaces an <c>IServiceProvider</c> to user code
/// (<c>DataSourceContext.ServiceProvider</c>, reachable only from a data-source
/// attribute) answers <see langword="null"/> for it. <see cref="TestContext"/>
/// takes an <c>IServiceProvider</c> in its constructor and exposes no property
/// for it. <b>The filter below is a strictly stronger source than
/// <c>ICommandLineOptions</c> would have been</b>, because it is what the
/// framework applied rather than what was typed, and it is the reason this
/// shipped instead of stopping.
/// </para>
/// <para>
/// <b>A row in the coverage block rather than a <see cref="SuiteCapability"/>,
/// for <see cref="ForegroundLock"/>'s reason.</b> Every capability names a
/// command that produces it; being unfiltered is not something a run can go and
/// acquire. And like that row it reports four states, one of which is <i>this
/// run could not tell</i> — see <see cref="SuiteFilterVerdict.Unread"/>.
/// </para>
/// </remarks>
internal static class SuiteFilter
{
    /// <summary>The label this row carries in the coverage block.</summary>
    public const string Title = "filter";

    /// <summary>The word a run prints when it could not tell.</summary>
    public const string UnreadState = "UNREAD";

    /// <summary>The word a run prints when the platform handed it no filter.</summary>
    public const string FullRunState = "FULL RUN";

    /// <summary>The word a run prints when the platform handed it a filter.</summary>
    public const string FilteredState = "FILTERED";

    /// <summary>The word a run prints when the two seams disagree.</summary>
    public const string DisagreedState = "DISAGREED";

    /// <summary>
    /// Where a child run writes what it read, so that a parent run can prove the
    /// instrument tells a filtered run from a full one.
    /// </summary>
    /// <remarks>
    /// <b>It is also the recursion guard.</b> The child is filtered down to one
    /// method, and the method that launches it is a different one — so a child
    /// can never select the launcher and recursion is impossible by
    /// construction. This variable is the second bolt on that door, for the day
    /// a filter fails open: it is set only by the launcher, so a process that
    /// sees it set knows it is the child and does not launch.
    /// </remarks>
    public const string ProbeVariable = "BROWSERAI_FILTER_PROBE";

    private static SuiteFilterReading _reading = SuiteFilterReading.NotTaken;

    /// <summary>What the framework held when the reading was taken.</summary>
    public static SuiteFilterReading Reading => Volatile.Read(ref _reading);

    /// <summary>What this run's reading amounts to.</summary>
    public static SuiteFilterVerdict Verdict => Judge(Reading);

    /// <summary>What this run must do about it.</summary>
    public static SuiteFilterDecision Decision => Decide(Verdict, SuiteEnvironment.IsReleaseRun);

    /// <summary>
    /// The filter this run received, or <see langword="null"/> when it did not
    /// receive one or could not tell.
    /// </summary>
    /// <remarks>
    /// <b>Only ever non-null in <see cref="SuiteFilterVerdict.Filtered"/>.</b>
    /// Reading the raw field instead would hand a caller a filter string out of a
    /// state that says the reading cannot be trusted.
    /// </remarks>
    public static string? Filter => Verdict is SuiteFilterVerdict.Filtered ? Reading.Global : null;

    /// <summary>The coverage block's row for this run.</summary>
    public static string CoverageRow => RowFor(Reading, SuiteEnvironment.IsReleaseRun);

    /// <summary>
    /// Takes the reading, once, from a place where the framework has certainly
    /// populated its contexts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Called from a <c>[Before(TestSession)]</c> hook rather than lazily on
    /// first use, and that is the honesty half of this type.</b>
    /// <c>TUnitTestFramework.ExecuteRequestAsync</c> assigns both contexts before
    /// it runs a single hook, so a reading taken there is taken after the only
    /// event that could populate them. A lazy reading would be taken at whatever
    /// moment somebody first asked, and a <see langword="null"/> filter read too
    /// early is indistinguishable from a run that really had none.
    /// </para>
    /// <para>
    /// <b><see cref="TestSessionContext.Current"/> is the witness and not the
    /// value.</b> It is an <c>AsyncLocal</c>, so it can be null on an execution
    /// context the framework never flowed through; <see cref="GlobalContext"/> is
    /// a process-wide static and is what the verdict is taken on. Reading both
    /// makes <i>the framework has not populated anything</i> a state of its own
    /// instead of a null nobody can interpret.
    /// </para>
    /// </remarks>
    public static void Take() => Volatile.Write(ref _reading, Observe());

    /// <summary>
    /// Reads both seams without latching, so a test can compare a live reading
    /// against the latched one.
    /// </summary>
    /// <returns>What the framework holds right now.</returns>
    public static SuiteFilterReading Observe() => new(
        Taken: true,
        SessionContextPopulated: TestSessionContext.Current is not null,
        Global: GlobalContext.Current.TestFilter,
        Session: TestSessionContext.Current?.TestFilter);

    /// <summary>
    /// The verdict, as a pure function of the reading.
    /// </summary>
    /// <remarks>
    /// <b>Pure for <see cref="SuiteEnvironment.Decide"/>'s reason exactly.</b> A
    /// gate run is never filtered, so a classification written only against the
    /// live reading would have three of its four states unexercised — and the one
    /// it does exercise is the one that proves the least.
    /// </remarks>
    /// <param name="reading">The reading.</param>
    /// <returns>What a run may claim about its own coverage.</returns>
    public static SuiteFilterVerdict Judge(SuiteFilterReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return reading switch
        {
            { Taken: false } or { SessionContextPopulated: false } => SuiteFilterVerdict.Unread,
            { Global: null, Session: null } => SuiteFilterVerdict.NotFiltered,
            _ when !string.Equals(reading.Global, reading.Session, StringComparison.Ordinal) => SuiteFilterVerdict.Disagreed,
            _ => SuiteFilterVerdict.Filtered,
        };
    }

    /// <summary>
    /// What a verdict costs a run, as a pure function of it and the mode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A filtered run is permitted and a filtered RELEASE is not.</b> That is
    /// the whole asymmetry: a filtered run is a correct run and the iteration
    /// loop depends on it, but a release cut from one is a release whose gate
    /// never ran most of the suite.
    /// </para>
    /// <para>
    /// <b><see cref="SuiteFilterVerdict.Unread"/> refuses a release too</b>,
    /// because <i>this run cannot say whether it was filtered</i> is not a
    /// premise a release may rest on, and treating it as <i>not filtered</i> is
    /// the false green the whole type is built against.
    /// </para>
    /// <para>
    /// <b><see cref="SuiteFilterVerdict.Disagreed"/> refuses in both modes</b>,
    /// exactly as <see cref="CapabilityState.Partial"/> does: two seams filled
    /// from one value that carry different values is a broken instrument, and a
    /// broken instrument is a failure in every run rather than an absence a
    /// developer run may tolerate.
    /// </para>
    /// </remarks>
    /// <param name="verdict">What the reading amounted to.</param>
    /// <param name="isReleaseRun">Whether this run asked to be a release run.</param>
    /// <returns>Whether the run may proceed.</returns>
    public static SuiteFilterDecision Decide(SuiteFilterVerdict verdict, bool isReleaseRun) => verdict switch
    {
        SuiteFilterVerdict.Disagreed => SuiteFilterDecision.Refuse,
        SuiteFilterVerdict.NotFiltered => SuiteFilterDecision.Proceed,
        _ => isReleaseRun ? SuiteFilterDecision.Refuse : SuiteFilterDecision.Proceed,
    };

    /// <summary>
    /// The sentence a run that may not proceed fails with.
    /// </summary>
    /// <param name="reading">The reading.</param>
    /// <param name="isReleaseRun">Whether this run asked to be a release run.</param>
    /// <returns>The refusal.</returns>
    public static string Refusal(SuiteFilterReading reading, bool isReleaseRun)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return Judge(reading) switch
        {
            SuiteFilterVerdict.Filtered =>
                $"{SuiteEnvironment.ReleaseRunVariable} is set, so this run asked to be a release run — and the platform handed the "
                + $"framework a filter: '{reading.Global}'. A filtered run is a correct run and every number it prints is true of what "
                + "it ran; it is not a gate. No release may be cut from it, and this is a failing test rather than a line nobody reads "
                + "because a filtered run's summary is character-for-character the shape a full one's is. "
                + "Re-run without a filter, or unset the variable and stop calling it a release.",

            SuiteFilterVerdict.Unread =>
                $"{SuiteEnvironment.ReleaseRunVariable} is set, so this run asked to be a release run — and it CANNOT SAY whether it "
                + "was filtered. TUnit's session context was not populated when the reading was taken"
                + $" (taken={reading.Taken}, sessionContext={reading.SessionContextPopulated}), so a null filter "
                + "here means 'nothing was read' and not 'nothing was filtered'. That is not a premise a release may rest on. "
                + "Whatever populates GlobalContext.TestFilter and TestSessionContext.TestFilter has moved: see SuiteFilter.",

            SuiteFilterVerdict.Disagreed =>
                "TUnit fills GlobalContext.TestFilter and TestSessionContext.TestFilter from ONE value, and this run read two: "
                + $"global='{reading.Global}', session='{reading.Session}'. The instrument is broken, so this run may not be read as "
                + "filtered OR as unfiltered, and that is a failure in every run rather than only in a release one. See SuiteFilter.",

            _ =>
                $"The platform handed the framework no filter and {SuiteEnvironment.ReleaseRunVariable} is "
                + $"{(isReleaseRun ? "set" : "unset")}, so there is nothing here to refuse. This sentence is a defect if it is ever read.",
        };
    }

    /// <summary>
    /// The block's row for a reading, built here so a synthetic reading and the
    /// live one are written by one implementation.
    /// </summary>
    /// <param name="reading">The reading.</param>
    /// <param name="isReleaseRun">Whether this run asked to be a release run.</param>
    /// <returns>The row, and the warning beneath it when the run is not a gate.</returns>
    public static string RowFor(SuiteFilterReading reading, bool isReleaseRun)
    {
        ArgumentNullException.ThrowIfNull(reading);

        var verdict = Judge(reading);
        var row = "  " + Title.PadRight(20) + StateWord(verdict).PadRight(9) + "  " + WitnessFor(reading, verdict);

        // ⚠️ THE SECOND BLOCK IS THE WHOLE POINT OF THE ROW, and it appears only
        // in the states where the run is not a gate. A run that printed the
        // filter and stopped would still leave the reader to notice; the sentence
        // a filtered run gets written underneath it is the thing no test can
        // read, so the run says it itself.
        return verdict switch
        {
            SuiteFilterVerdict.Filtered => row + "\n"
                + "      ⚠️  THIS RUN IS A DEVELOPMENT CONVENIENCE AND NOT A VERIFICATION. It is evidence\n"
                + "      about what it selected and about nothing else, and its total/failed/succeeded\n"
                + "      block is character-for-character the shape a full run's is. --treenode-filter\n"
                + "      also reads '|' as an OR INSIDE one path segment and never between whole path\n"
                + "      patterns, so a filter can select far more or far less than it looks like it\n"
                + $"      selects — see kb/toolchain.md. {SuiteEnvironment.ReleaseRunVariable}=1 makes this state a failure.",

            SuiteFilterVerdict.Unread => row + "\n"
                + "      ⚠️  THIS RUN DID NOT ANSWER whether it was filtered, and 'FULL RUN' is therefore\n"
                + "      NOT what it says. TUnit had not populated its contexts when the reading was\n"
                + "      taken, so a null filter here means nothing was read rather than nothing was\n"
                + "      filtered. Treat this run's coverage as unknown: see SuiteFilter.",

            SuiteFilterVerdict.Disagreed => row + "\n"
                + "      ⚠️  THE INSTRUMENT IS BROKEN. Two seams the framework fills from one value carry\n"
                + "      different values, so this run may not be read as filtered or as unfiltered.\n"
                + "      This is a failing test in every run, release or not: see SuiteFilter.",

            _ => row,
        };
    }

    /// <summary>The state word the block prints.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <returns>The word.</returns>
    public static string StateWord(SuiteFilterVerdict verdict) => verdict switch
    {
        SuiteFilterVerdict.NotFiltered => FullRunState,
        SuiteFilterVerdict.Filtered => FilteredState,
        SuiteFilterVerdict.Disagreed => DisagreedState,
        _ => UnreadState,
    };

    /// <summary>The sentence beside the state, which is where the filter lives.</summary>
    /// <param name="reading">The reading.</param>
    /// <param name="verdict">Its verdict.</param>
    /// <returns>The witness.</returns>
    public static string WitnessFor(SuiteFilterReading reading, SuiteFilterVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(reading);

        const string Source =
            "read from TUnit's GlobalContext.TestFilter, which carries the ITestExecutionFilter the platform put on the "
            + "execute request — never this process's command line";

        return verdict switch
        {
            SuiteFilterVerdict.NotFiltered =>
                $"the platform handed the framework no filter, so this run discovered the whole assembly · {Source}",

            SuiteFilterVerdict.Filtered =>
                $"--treenode-filter '{reading.Global}' · this run selected a subset and is evidence about that subset only · {Source}",

            SuiteFilterVerdict.Disagreed =>
                $"global='{reading.Global}' and session='{reading.Session}' came from ONE value and differ · {Source}",

            _ =>
                $"nothing read the filter (taken={reading.Taken}, sessionContext={reading.SessionContextPopulated}), "
                + $"so this run cannot say whether it was filtered · {Source}",
        };
    }

    /// <summary>
    /// The reading as lines a child run writes and a parent run reads back.
    /// </summary>
    /// <remarks>
    /// <b>Written by the child rather than parsed out of its console output.</b>
    /// A run summary's wording belongs to the platform and moves with it; a file
    /// this type writes and this type reads cannot drift apart.
    /// </remarks>
    /// <param name="reading">The reading.</param>
    /// <param name="isReleaseRun">Whether the run asked to be a release run.</param>
    /// <returns>The block.</returns>
    public static string Describe(SuiteFilterReading reading, bool isReleaseRun)
    {
        ArgumentNullException.ThrowIfNull(reading);

        var verdict = Judge(reading);

        return $"verdict={StateWord(verdict)}\n"
            + $"taken={reading.Taken}\n"
            + $"sessionContext={reading.SessionContextPopulated}\n"
            + $"global={reading.Global}\n"
            + $"session={reading.Session}\n"
            + $"releaseRun={isReleaseRun}\n"
            + $"decision={Decide(verdict, isReleaseRun)}\n"
            + $"commandLineCarriesIt={CommandLineCarries(reading.Global)}\n"
            + $"commandLine={string.Join(' ', Environment.GetCommandLineArgs())}\n";
    }

    /// <summary>
    /// Whether this process's own command line carries the filter the platform
    /// handed the framework.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Recorded as a diagnostic and never read for the verdict, and the
    /// distinction is the reason this type exists in the shape it does.</b> The
    /// question <i>does a filter reach the test host's own command line under
    /// <c>dotnet test</c>?</i> was open when this was written, and an instrument
    /// that answered it wrongly would print <c>FULL RUN</c> over a filtered run —
    /// the one failure worse than having no row. So the verdict comes from the
    /// platform's <c>ITestExecutionFilter</c>, and this line sits beside it in
    /// the probe's report so that the two can be compared by whoever wants to,
    /// on a run that really was filtered, instead of reasoned about.
    /// </remarks>
    /// <param name="filter">The filter the platform handed the framework, if any.</param>
    /// <returns>Whether an argument of this process carries it.</returns>
    public static bool CommandLineCarries(string? filter) =>
        filter is { Length: > 0 }
        && Environment.GetCommandLineArgs().Any(argument => argument.Contains(filter, StringComparison.Ordinal));
}
