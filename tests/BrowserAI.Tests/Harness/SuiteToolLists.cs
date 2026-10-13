// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Globalization;
using BrowserAI.Hosting;
using BrowserAI.Interop;
using BrowserAI.Relay;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The tool-list records this test host's relays write under the data root the suite shares
/// with the installed BrowserAI: the record of the test host they are keyed by, which the
/// clearance reads, and their removal at the session's end.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The maintainer's 160 c, 2026-10-13, in his words verbatim: <i>"160 c"</i>.</b> Since
/// 127 c a relay keeps the tool list its client was last answered under the data root's
/// <c>tool-lists</c>, in a file named for the client's pid and creation time, and a relay the
/// suite starts is the test host's child, so every record it writes under
/// <c>%LOCALAPPDATA%\BrowserAI</c> is keyed by the test host. The question 160 opened on a gate
/// that had left 29 of them there, keyed by its test host after it had gone, and nothing had
/// noticed, because the clearance snapshot did not look in that folder. Since 160 c the
/// session's end removes every record keyed by its own test host
/// (<see cref="RemoveThisTestHostsRecordsAsync"/>), and the snapshot's eleventh reading names
/// every record keyed by a test host a session's start recorded
/// (<see cref="RecordThisTestHostAsync"/>), so one left behind is a difference that stops the
/// gate.
/// </para>
/// <para>
/// <b>The recorded test hosts, and not the whole folder</b>, which is the build's choice, for
/// his review. From the release that carries 127 c the installed BrowserAI keeps the
/// maintainer's own records in that folder, keyed by his clients, and every relay removes the
/// records of clients that have gone, the suite's relays among them; a reading of the whole
/// folder would then stop a gate on a change that was not the suite's, the shape Q292 a closed
/// for the two clients' configuration files.
/// </para>
/// <para>
/// <b>An identity, never a name</b>, as in <see cref="SpawnRecord"/>: a test host is its pid
/// and its creation time, which is what each of its records' names begins with, and a pid
/// Windows has handed on names a different test host.
/// </para>
/// </remarks>
internal static class SuiteToolLists
{
    /// <summary>
    /// The variable the arm that holds the session's end sets for the child test host it
    /// starts: the file in which the child names the record it planted and, at its session's
    /// end, each record it removed.
    /// </summary>
    /// <remarks>
    /// Set by that one launcher and nothing else, as <see cref="SuiteFilter.ProbeVariable"/>
    /// is, so an ordinary run plants nothing.
    /// </remarks>
    public const string PlantVariable = "BROWSERAI_TOOL_LIST_PLANT";

    /// <summary>What a line the session's end appends to the plant's file begins with, before the name of a file it removed.</summary>
    public const string RemovedPrefix = "removed ";

    /// <summary>This test host, as the client every relay it starts keys its record by.</summary>
    public static ProcessStamp ThisTestHost { get; } = new(Environment.ProcessId, ProcessLiveness.CreationTimeOfThisProcess());

    /// <summary>Where a session's start records its test host: one file per test host, named for it.</summary>
    /// <remarks>
    /// <b>Under the checkout's own <c>.work</c>, beside the snapshots</b>, because the snapshot
    /// script of the same checkout reads it, and outside the scratch root, whose reclaim would
    /// take it.
    /// </remarks>
    public static string TestHostsDirectory { get; } = Path.Combine(RepositoryLayout.Root.FullName, ".work", "clearance", "test-hosts");

    /// <summary>The folder of tool-list records under the data root every BrowserAI resolves when nothing names another.</summary>
    public static string SharedFolder { get; } = Path.Combine(LocalAppDataPaths.Default, ToolListRecordFile.DirectoryName);

    /// <summary>What the names of a test host's records begin with, and the name of its file under <see cref="TestHostsDirectory"/>.</summary>
    /// <param name="host">The test host.</param>
    /// <returns>Its pid and its creation time, joined as a record's name joins them.</returns>
    public static string KeyOf(ProcessStamp host) =>
        string.Create(CultureInfo.InvariantCulture, $"{host.ProcessId}-{host.CreatedFileTime}");

    /// <summary>The client a record's file name names, or <see langword="null"/> when the name is not a record's.</summary>
    /// <param name="path">The record's path.</param>
    /// <returns>The client.</returns>
    public static ProcessStamp? ClientOf(string path) => ToolListRecordFile.ClientOf(Path.GetFileName(path));

    /// <summary>Whether a process is known to have gone; one whose state cannot be read counts as running.</summary>
    /// <param name="process">The process.</param>
    /// <returns><see langword="true"/> only when it has gone.</returns>
    public static bool HasGone(ProcessStamp process)
    {
        try
        {
            return !ProcessLiveness.IsAlive(process.ProcessId, process.CreatedFileTime);
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Writes a record keyed by a client under a data root, as a relay leaves one, and returns its path.</summary>
    /// <param name="dataRoot">The data root.</param>
    /// <param name="client">The client the record is keyed by.</param>
    /// <param name="commandLine">The relay command line the record's name carries the digest of.</param>
    /// <returns>The record's path.</returns>
    public static async Task<string> PlantAsync(string dataRoot, ProcessStamp client, string commandLine)
    {
        var record = ToolListRecordFile.PathFor(dataRoot, client, commandLine);

        _ = Directory.CreateDirectory(Path.GetDirectoryName(record)!);
        await File.WriteAllBytesAsync(
            record,
            ToolListRecordFile.Serialised(new ListedToolList(new string('0', 64), "9.9.9-suite-tool-lists", DateTimeOffset.UtcNow)));

        return record;
    }

    /// <summary>
    /// Records this test host for the clearance's eleventh reading, and plants a record keyed
    /// by itself when the arm that holds the session's end asked it to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Called from a session hook, so no filter can deselect it</b>, before the first test
    /// starts a relay. A file that cannot be written fails the session: a run whose test host
    /// is not recorded is one whose records the reading cannot name.
    /// </para>
    /// <para>
    /// <b>The file of a test host that has gone goes first, once no record is keyed by it</b>,
    /// so the folder holds the live test hosts and those a run left records of. A gone test
    /// host's file outlives its session until the next one starts, so the snapshot taken after
    /// a run still names a record written after that run's end.
    /// </para>
    /// </remarks>
    /// <returns>The recording.</returns>
    public static async Task RecordThisTestHostAsync()
    {
        _ = Directory.CreateDirectory(TestHostsDirectory);

        var records = NamesIn(SharedFolder);

        foreach (var key in NamesIn(TestHostsDirectory))
        {
            if (HostOf(key) is { } host
                && host != ThisTestHost
                && HasGone(host)
                && !records.Exists(name => name.StartsWith(key + "-", StringComparison.Ordinal)))
            {
                try
                {
                    File.Delete(Path.Combine(TestHostsDirectory, key));
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
                {
                    // Left for the next session's start: a file of a host with no records names nothing.
                }
            }
        }

        await File.WriteAllTextAsync(
            Path.Combine(TestHostsDirectory, KeyOf(ThisTestHost)),
            string.Create(CultureInfo.InvariantCulture, $"test host {ThisTestHost.ProcessId}, created {ThisTestHost.CreatedFileTime}, session started {DateTimeOffset.UtcNow:O}\n"));

        if (Environment.GetEnvironmentVariable(PlantVariable) is { Length: > 0 } plant)
        {
            var record = await PlantAsync(LocalAppDataPaths.Default, ThisTestHost, "the suite's planted record " + Guid.NewGuid().ToString("N"));

            // Its own line: the session's end appends a line for each record it removes.
            await File.WriteAllTextAsync(plant, record + Environment.NewLine);
        }
    }

    /// <summary>
    /// Removes every tool-list record keyed by this test host under the shared data root, and
    /// names each file it removed in the plant's file when the arm that holds it asked for one.
    /// </summary>
    /// <remarks>
    /// <b>The names are the child's own account, and the arm reads them for that reason</b>:
    /// any relay that writes a record removes the records of clients that have gone, so a
    /// record the session's end left could still be gone by the time the arm looks, removed by a
    /// relay of another arm. Only this call writes the lines.
    /// </remarks>
    /// <returns>The names of the files it could not remove.</returns>
    public static async Task<IReadOnlyList<string>> RemoveThisTestHostsRecordsAsync()
    {
        var (removed, left) = RemoveTheRecordsOf(LocalAppDataPaths.Default, ThisTestHost);

        if (Environment.GetEnvironmentVariable(PlantVariable) is { Length: > 0 } plant)
        {
            await File.AppendAllLinesAsync(plant, removed.Select(static name => RemovedPrefix + name));
        }

        return left;
    }

    /// <summary>
    /// Removes every file of a data root's tool-list folder whose name names a client: its
    /// records, and a file a write of one left beside it.
    /// </summary>
    /// <remarks>
    /// <b>Never throws</b>: a file it cannot remove is named in what it returns, and the
    /// clearance's eleventh reading names it after the run. A file whose name names any other
    /// client, or no client, is left where it is.
    /// </remarks>
    /// <param name="dataRoot">The data root.</param>
    /// <param name="client">The client whose records go.</param>
    /// <returns>The names of the files it removed, and of those it could not.</returns>
    public static (IReadOnlyList<string> Removed, IReadOnlyList<string> Left) RemoveTheRecordsOf(string dataRoot, ProcessStamp client)
    {
        var folder = Path.Combine(dataRoot, ToolListRecordFile.DirectoryName);
        var removed = new List<string>();
        var left = new List<string>();

        foreach (var name in NamesIn(folder).Where(file => ToolListRecordFile.ClientOf(file) == client))
        {
            try
            {
                File.Delete(Path.Combine(folder, name));
                removed.Add(name);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                left.Add(name);
            }
        }

        return (removed, left);
    }

    /// <summary>The names of the files a folder holds, or none when it cannot be listed.</summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The names.</returns>
    private static List<string> NamesIn(string folder)
    {
        try
        {
            return [.. Directory.GetFiles(folder).Select(static path => Path.GetFileName(path))];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The test host a file under <see cref="TestHostsDirectory"/> is named for, or <see langword="null"/>.</summary>
    /// <param name="key">The file's name.</param>
    /// <returns>The test host.</returns>
    private static ProcessStamp? HostOf(string key)
    {
        var parts = key.Split('-');

        return parts.Length is 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var created)
                ? new ProcessStamp(pid, created)
                : null;
    }
}
