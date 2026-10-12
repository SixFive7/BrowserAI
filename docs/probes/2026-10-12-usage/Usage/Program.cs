// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Text;
using BrowserAI.Interop;
using Usage;

// usage measure --chrome PATH --out DIR [--sessions N] [--readings R] [--paced SECONDS]
//
// Starts N headless Chromiums from PATH, each in a kill-on-close job of its own as a
// session's child is, reads every job the way the background's reading does, and writes
// what one reading found and what readings cost: hot, back to back, and at the shipped
// pace of one every 2 s against the same wait with no reading.

var options = Options.Parse(args);
var culture = CultureInfo.InvariantCulture;
var summary = new StringBuilder();
var browserRoot = Path.GetDirectoryName(options.Chrome)! + Path.DirectorySeparatorChar;
var jobs = new List<JobObject>();
var kinds = new Dictionary<(int ProcessId, long Created), bool>();

void Say(string line)
{
    Console.WriteLine(line);
    _ = summary.AppendLine(line);
}

// One reading of every job, as BackgroundPageUsage.ReadSession composes the product's calls.
(int Members, long Browser, long Server, long Shared) ReadAll()
{
    var members = 0;
    long browser = 0;
    long server = 0;
    long shared = 0;

    foreach (var job in jobs)
    {
        _ = job.CpuTicks;

        foreach (var processId in job.ProcessIds())
        {
            if (ProcessUsage.ReadMember(job, processId, (pid, created) => !kinds.ContainsKey((pid, created))) is not { } member)
            {
                continue;
            }

            members++;

            if (member.ImagePath is { } image)
            {
                kinds[(member.ProcessId, member.CreatedFileTime)] = image.StartsWith(browserRoot, StringComparison.OrdinalIgnoreCase);
            }

            if (kinds.TryGetValue((member.ProcessId, member.CreatedFileTime), out var isBrowser) && isBrowser)
            {
                browser += member.Use.PrivateWorkingSet;
            }
            else
            {
                server += member.Use.PrivateWorkingSet;
            }

            shared = Math.Max(shared, member.Use.SharedWorkingSet);
        }
    }

    return (members, browser, server, shared);
}

static string Percentiles(List<double> microseconds)
{
    microseconds.Sort();

    double at(double share) => microseconds[Math.Min(microseconds.Count - 1, (int)(microseconds.Count * share))];

    return string.Create(CultureInfo.InvariantCulture, $"median {at(0.5):0.0} us, p99 {at(0.99):0.0} us, max {microseconds[^1]:0.0} us, over {microseconds.Count}");
}

Directory.CreateDirectory(options.Out);
Say($"usage rig at {DateTimeOffset.UtcNow:o}: {options.Sessions} session(s), {options.Readings} readings, paced {options.Paced} s");
Say($"chrome: {options.Chrome}");
Say($"processors: {Environment.ProcessorCount}");

try
{
    for (var i = 0; i < options.Sessions; i++)
    {
        var profile = Path.Combine(options.Out, $"profile-{i}");
        Directory.CreateDirectory(profile);

        var job = JobObject.CreateKillOnClose();
        jobs.Add(job);

        var pid = Native.StartInJob(job, options.Chrome, $"\"{options.Chrome}\" --headless=new --no-first-run --no-default-browser-check --user-data-dir=\"{profile}\" about:blank", options.Out);
        Say($"session {i}: chrome pid {pid}");
    }

    await Task.Delay(TimeSpan.FromSeconds(options.Settle));

    // What one reading finds, member by member: the shared figure's two candidates beside each.
    foreach (var (job, index) in jobs.Select((job, index) => (job, index)))
    {
        var ids = job.ProcessIds();
        Say($"session {index}: {ids.Count} processes in its job, CPU so far {job.CpuTicks / 1e7:0.000} s");

        foreach (var id in ids)
        {
            if (ProcessUsage.ReadMember(job, id, static (_, _) => true) is { } member)
            {
                const double Mb = 1024.0 * 1024.0;
                Say(string.Create(culture, $"  {member.ProcessId,6} {Path.GetFileName(member.ImagePath),-12} ws {member.Use.WorkingSet / Mb,7:0.0} MB  private ws {member.Use.PrivateWorkingSet / Mb,7:0.0} MB  shared ws {member.Use.SharedWorkingSet / Mb,7:0.0} MB  shared commit {member.Use.SharedCommit / Mb,7:0.0} MB"));
            }
        }
    }

    var self = ProcessUsage.OfThisProcess();
    Say($"the rig itself readable: {self is not null}");

    // Cold: the first reading, which reads every member's image.
    var watch = Stopwatch.StartNew();
    var first = ReadAll();
    watch.Stop();
    Say(string.Create(culture, $"first reading, images read: {watch.Elapsed.TotalMilliseconds * 1000:0.0} us, {first.Members} members, browser {first.Browser / 1048576.0:0.0} MB, server {first.Server / 1048576.0:0.0} MB, largest shared ws {first.Shared / 1048576.0:0.0} MB"));

    // Hot: back to back, every image already known.
    var hot = new List<double>(options.Readings);
    var members = 0;

    for (var i = 0; i < options.Readings; i++)
    {
        watch.Restart();
        members = ReadAll().Members;
        watch.Stop();
        hot.Add(watch.Elapsed.TotalMilliseconds * 1000);
    }

    Say($"hot readings, {members} members each: {Percentiles(hot)}");
    Say(string.Create(culture, $"per member, at the median: {hot[hot.Count / 2] / Math.Max(1, members):0.0} us"));

    // Paced: the same wait with no reading, then with one reading per tick.
    async Task<(ulong Cycles, long Time, List<double> Readings)> PacedAsync(bool read)
    {
        var readings = new List<double>();
        var cycles = Native.ProcessCycles();
        var time = Native.ProcessTime();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(options.Paced);

        while (DateTime.UtcNow < end && await timer.WaitForNextTickAsync())
        {
            if (read)
            {
                var tick = Stopwatch.StartNew();
                _ = ReadAll();
                tick.Stop();
                readings.Add(tick.Elapsed.TotalMilliseconds * 1000);
            }
        }

        return (Native.ProcessCycles() - cycles, Native.ProcessTime() - time, readings);
    }

    if (options.Paced > 0)
    {
        var idle = await PacedAsync(read: false);
        var paced = await PacedAsync(read: true);

        Say($"paced, no reading: {idle.Cycles} cycles, {idle.Time / 1e4:0.0} ms CPU over {options.Paced} s");
        Say($"paced, a reading every 2 s: {paced.Cycles} cycles, {paced.Time / 1e4:0.0} ms CPU over {options.Paced} s, {paced.Readings.Count} readings");
        Say($"paced readings, cold: {Percentiles(paced.Readings)}");
        Say(string.Create(culture, $"cycles per reading over the idle wait: {(paced.Cycles - (double)idle.Cycles) / Math.Max(1, paced.Readings.Count):0}"));
    }
}
finally
{
    foreach (var job in jobs)
    {
        job.Dispose();
    }

    Say($"jobs closed at {DateTimeOffset.UtcNow:o}");
    await File.WriteAllTextAsync(Path.Combine(options.Out, "summary.txt"), summary.ToString());
}

internal sealed record Options(string Chrome, string Out, int Sessions, int Readings, int Paced, int Settle)
{
    public static Options Parse(string[] args)
    {
        string? value(string name)
        {
            var at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        return args.FirstOrDefault() is "measure" && value("--chrome") is { } chrome && value("--out") is { } output
            ? new Options(
                chrome,
                output,
                int.Parse(value("--sessions") ?? "1", CultureInfo.InvariantCulture),
                int.Parse(value("--readings") ?? "1000", CultureInfo.InvariantCulture),
                int.Parse(value("--paced") ?? "120", CultureInfo.InvariantCulture),
                int.Parse(value("--settle") ?? "10", CultureInfo.InvariantCulture))
            : throw new ArgumentException("usage: measure --chrome PATH --out DIR [--sessions N] [--readings R] [--paced SECONDS] [--settle SECONDS]");
    }
}
