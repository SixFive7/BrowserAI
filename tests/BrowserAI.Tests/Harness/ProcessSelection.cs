// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.RegularExpressions;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Reads a line of code for the thing <i>never by image name</i> actually
/// forbids: <b>selecting a process by its image name</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>It replaces a substring scan, and the difference is the whole point.</b>
/// Until 2026-09-17 the scan behind <c>NeverByImageNameTests</c> flagged the
/// <i>API</i> -- any file containing <c>Get-Process</c>, <c>Win32_Process</c> or
/// <c>GetProcessesByName</c> anywhere -- which cannot tell
/// <c>Get-Process -Id $pid</c> from <c>Get-Process chrome</c>. Those are opposite
/// things: one names a pid the caller already had, the other picks a stranger out
/// of the machine by what its executable is called. <b>Q203</b>, decided
/// 2026-09-17: the scan reads the <b>filter</b> rather than the API.
/// </para>
/// <para>
/// ⚠️ <b>This is a narrowing, so the shapes it must still catch are enumerated
/// rather than described</b>, and each has a synthetic control in
/// <c>NeverByImageNameTests</c> pointing both ways: a violation that must be
/// caught, and the pid-keyed spelling of the same call that must pass. A
/// narrowing nobody planted red in both directions is a hole with a test in
/// front of it.
/// </para>
/// <para>
/// <b>Every needle here is composed at run time</b>, for the same reason the
/// forbidden list always was: this file is inside the corpus the scan reads, and
/// a literal would make the rule's own implementation its first offender. The
/// alternative -- an exclusion naming this file -- would create the one place in
/// the repository where the rule does not apply.
/// </para>
/// <para>
/// ⚠️ <b>What it cannot see, stated rather than implied.</b> It reads one line
/// at a time, so a query built on one line and filtered on the next is only
/// caught by the second line -- which is why the comparison rule is gated on the
/// <b>file</b> carrying a process-enumeration call rather than on the line. And
/// it reads text, not meaning: a name assembled from variables, or a filter
/// passed through a parameter, is beyond any scan of this kind. The
/// <c>BannedApiAnalyzers</c> half still covers the C# call sites it can see, and
/// the two together are what the rule rests on.
/// </para>
/// </remarks>
internal static class ProcessSelection
{
    /// <summary>One thing wrong with one line.</summary>
    /// <param name="Spelling">The forbidden shape, as it appeared.</param>
    /// <param name="Why">Why it selects a process by name.</param>
    internal readonly record struct Offence(string Spelling, string Why);

    private const string GetProcess = "Get-" + "Process";
    private const string StopProcess = "Stop-" + "Process";
    private const string KillCommand = "task" + "kill";
    private const string ByName = "GetProcessesBy" + "Name";
    private const string ToolhelpField = "szExe" + "File";
    private const string WmiClass = "Win32_" + "Process";

    /// <summary>
    /// A name compared with an operator that can only ever be a comparison.
    /// </summary>
    /// <remarks>
    /// <b><c>=</c> is deliberately absent here and lives in
    /// <see cref="FilteredName"/> instead.</b> In PowerShell <c>=</c> is
    /// assignment, and <c>name = $p.Name</c> in a hashtable that <i>emits</i> a
    /// process's name is the exact shape the rule's own remark calls permitted --
    /// observing a name rather than selecting on one. Treating <c>=</c> as a
    /// comparison everywhere made a rig that reports a tree read as a rig that
    /// hunts one.
    /// </remarks>
    private static readonly Regex ComparedName = new(
        @"(?<![A-Za-z])(Process|Image)?Name\s*(-eq|-ne|-like|-notlike|-match|-notmatch|-in|-contains|==|===|!=)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>A name compared <i>inside a query string</i>, where <c>=</c> is a comparison.</summary>
    private static readonly Regex FilteredName = new(
        @"(?<![A-Za-z])(Process|Image)?Name\s*(=|-eq|-like|-match|\bLIKE\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Where a query string begins, after which <c>=</c> means comparison.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b><c>WHERE</c> is deliberately NOT a marker, and it was one for about
    /// ten minutes.</b> Under <see cref="RegexOptions.IgnoreCase"/> it matches
    /// <c>Where-Object</c> and LINQ's <c>.Where(</c>, which between them produced
    /// four offenders in this tree on the first run -- two release scripts
    /// filtering a <c>PSObject</c>'s properties and two test files writing
    /// <c>.Where(name =&gt; …)</c>, none of which has ever touched a process. WQL
    /// is recognised by its <c>FROM</c> clause instead, which cannot be anything
    /// else.
    /// </remarks>
    private static readonly Regex QueryMarker = new(
        @"-Filter\b|-Query\b|FROM\s+Win32_",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>A <c>-Name</c> parameter, including the alias PowerShell accepts for it.</summary>
    private static readonly Regex NameParameter = new(
        @"-(Process)?Name\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Whether this file does process work at all, which gates the comparison
    /// rule so that <c>$_.Name -eq</c> over a directory listing is not an
    /// offence.
    /// </summary>
    /// <param name="code">The whole file's code text.</param>
    /// <returns><see langword="true"/> if anything here enumerates processes.</returns>
    public static bool EnumeratesProcesses(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        return code.Contains(WmiClass, StringComparison.OrdinalIgnoreCase)
            || code.Contains(GetProcess, StringComparison.OrdinalIgnoreCase)
            || code.Contains(StopProcess, StringComparison.OrdinalIgnoreCase)
            || code.Contains(KillCommand, StringComparison.OrdinalIgnoreCase)
            || code.Contains("Diagnostics." + "Process", StringComparison.OrdinalIgnoreCase)
            || code.Contains("Process." + "GetProcesses", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every way this file selects a process by its image name.
    /// </summary>
    /// <param name="code">The file's text, comment-only lines already blanked.</param>
    /// <returns>One entry per offending line, empty when the file only keys on pids.</returns>
    public static IReadOnlyList<Offence> OffencesIn(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        var offences = new List<Offence>();
        var processFile = EnumeratesProcesses(code);

        foreach (var line in code.Split('\n'))
        {
            // ---- Shapes that are a name filter wherever they appear ----------
            if (line.Contains(ByName, StringComparison.OrdinalIgnoreCase))
            {
                offences.Add(new Offence(ByName, "enumerates every process on the machine whose image carries a given name"));
            }

            if (line.Contains(ToolhelpField, StringComparison.OrdinalIgnoreCase))
            {
                // Reading the field IS the offence: it is the only way a
                // toolhelp walk can learn a name, and a walk that never reads it
                // cannot compare one even by accident.
                offences.Add(new Offence(ToolhelpField, "the PROCESSENTRY32 member that carries an image name, and the only way a toolhelp walk can match on one"));
            }

            // ---- taskkill: /IM is the name form, /PID is the pid form --------
            if (line.Contains(KillCommand, StringComparison.OrdinalIgnoreCase))
            {
                if (line.Contains("/im", StringComparison.OrdinalIgnoreCase))
                {
                    offences.Add(new Offence($"{KillCommand} /IM", "kills every process with a given image name, and cannot tell our browser from the user's"));
                }
                else if (!line.Contains("/pid", StringComparison.OrdinalIgnoreCase))
                {
                    // Neither switch is spelled here, so what it kills is
                    // decided somewhere this scan cannot read. Refused rather
                    // than assumed, which is the direction an unreadable case
                    // has to fall in.
                    offences.Add(new Offence(KillCommand, "neither /IM nor /PID is spelled on this line, so what it selects cannot be read"));
                }
            }

            // ---- Get-Process: -Name, or a bare positional name ---------------
            AddCmdletOffences(offences, line, GetProcess, positionalIsAName: true);

            // Stop-Process's positional parameter is -Id, so only the explicit
            // -Name is a name form. A bare positional there is a pid.
            AddCmdletOffences(offences, line, StopProcess, positionalIsAName: false);

            // ---- A Name clause inside a WMI / CIM query ----------------------
            var marker = QueryMarker.Match(line);

            if (processFile && marker.Success && FilteredName.IsMatch(line[marker.Index..]))
            {
                offences.Add(new Offence($"{WmiClass} … Name=", "a query filtered on Name picks processes by image name, which is the same rule wearing a different API"));
            }

            // ---- A name compared with a comparison operator ------------------
            if (processFile && ComparedName.IsMatch(line))
            {
                offences.Add(new Offence("Name" + " -eq", "comparing a process's Name selects it by image name, however the list it came from was built"));
            }
        }

        return offences;
    }

    /// <summary>Reads one cmdlet's arguments on one line.</summary>
    /// <param name="offences">Where to add what is found.</param>
    /// <param name="line">The line.</param>
    /// <param name="cmdlet">The cmdlet name.</param>
    /// <param name="positionalIsAName">Whether this cmdlet's first positional parameter is the image name.</param>
    private static void AddCmdletOffences(List<Offence> offences, string line, string cmdlet, bool positionalIsAName)
    {
        var at = line.IndexOf(cmdlet, StringComparison.OrdinalIgnoreCase);

        if (at < 0)
        {
            return;
        }

        var rest = line[(at + cmdlet.Length)..];

        if (NameParameter.IsMatch(rest))
        {
            offences.Add(new Offence($"{cmdlet} -Name", "selects processes by image name rather than by the pid the caller already holds"));
            return;
        }

        if (!positionalIsAName)
        {
            return;
        }

        var argument = rest.TrimStart();

        // Nothing at all, a pipeline, or the end of an expression: no positional
        // argument was passed, so this enumerates everything and filters later --
        // and whatever it filters on is read by the rules above.
        if (argument.Length is 0 || "|)};,>&".Contains(argument[0], StringComparison.Ordinal))
        {
            return;
        }

        // A switch. Which one it is has already been read above.
        if (argument[0] is '-')
        {
            return;
        }

        offences.Add(new Offence($"{cmdlet} <name>", "the first positional parameter is the image name, so this picks processes out of the machine by what they are called"));
    }
}
