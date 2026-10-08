// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using BrowserAI.Interop;
using BrowserAI.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace InputCheck;

/// <summary>
/// What the visible-input check costs: each read and the product's whole check, timed
/// in tight loops, and the shipped timer left running for a fixed window in processes
/// of their own beside a process with no timer at all.
/// </summary>
/// <remarks>
/// Modes: <c>measure --out DIR --seconds N</c> runs everything and starts the four
/// timed processes itself, all at once; <c>arm</c> is one of those processes. The
/// product's own <c>VisibleInputWatch</c>, <c>InputActivity</c>,
/// <c>CoalescableTimer</c> and <c>SessionTimes</c> are compiled in from <c>src/</c>,
/// so the check measured is the shipped one. Nothing here sends input, moves the
/// mouse, changes the window in front or shows a window.
/// </remarks>
internal static class Program
{
    /// <summary>Calls or checks per aggregate loop, ten million unless <c>--calls</c> says otherwise.</summary>
    private static int Calls { get; set; } = 10_000_000;

    /// <summary>Checks timed one at a time.</summary>
    private const int Samples = 1_000_000;

    /// <summary>Calls before each measured loop, so the first page faults are not in it.</summary>
    private const int Warmup = 20_000;

    /// <summary>How many sessions the hundred-session arm registers.</summary>
    private const int Hundred = 100;

    private static ulong _sink;

    public static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "measure";
        var options = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 1; index + 1 < args.Length; index += 2)
        {
            options[args[index].TrimStart('-')] = args[index + 1];
        }

        var output = options.TryGetValue("out", out var directory) ? directory : Path.Combine(AppContext.BaseDirectory, "results");
        var seconds = options.TryGetValue("seconds", out var text) ? int.Parse(text, CultureInfo.InvariantCulture) : 720;

        if (options.TryGetValue("calls", out var calls))
        {
            Calls = int.Parse(calls, CultureInfo.InvariantCulture);
        }

        Directory.CreateDirectory(output);

        return mode switch
        {
            "measure" => Measure(output, seconds),
            "arm" => Arm(
                output,
                options["name"],
                seconds,
                int.Parse(options["tolerance-ms"], CultureInfo.InvariantCulture),
                options["watch"] is "1"),
            _ => 2,
        };
    }

    private static int Measure(string output, int seconds)
    {
        var report = new StringBuilder();
        var json = new MemoryStream();

        using (var writer = new Utf8JsonWriter(json, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            Describe(writer, report);

            writer.WriteStartObject("calls");
            EachRead(writer, report);
            writer.WriteEndObject();

            writer.WriteStartObject("checks");
            Checks(writer, report);
            writer.WriteEndObject();

            if (seconds > 0)
            {
                writer.WriteStartObject("timed");
                TimedArms(writer, report, output, seconds);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        File.WriteAllBytes(Path.Combine(output, "summary.json"), json.ToArray());
        File.WriteAllText(Path.Combine(output, "summary.txt"), report.ToString());
        return 0;
    }

    private static void Describe(Utf8JsonWriter writer, StringBuilder report)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var build = $"{key?.GetValue("CurrentBuildNumber")}.{key?.GetValue("UBR")} ({key?.GetValue("DisplayVersion")})";
        var resolution = Native.TimerResolution();

        writer.WriteStartObject("environment");
        writer.WriteString("taken", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        writer.WriteString("windowsBuild", build);
        writer.WriteString("os", RuntimeInformation.OSDescription);
        writer.WriteString("runtime", RuntimeInformation.FrameworkDescription);
        writer.WriteBoolean("nativeAot", !RuntimeFeature.IsDynamicCodeSupported);
        writer.WriteNumber("processors", Environment.ProcessorCount);
        writer.WriteNumber("stopwatchFrequency", Stopwatch.Frequency);
        writer.WriteNumber("intervalMs", SessionTimes.VisibleInputCheckInterval.TotalMilliseconds);
        writer.WriteNumber("toleranceMs", SessionTimes.VisibleInputCheckTolerance.TotalMilliseconds);

        if (resolution is { } r)
        {
            writer.WriteNumber("timerCoarsest100ns", r.Coarsest);
            writer.WriteNumber("timerFinest100ns", r.Finest);
            writer.WriteNumber("timerCurrent100ns", r.Current);
        }

        writer.WriteEndObject();

        report.AppendLine(CultureInfo.InvariantCulture, $"Windows {build}, {RuntimeInformation.FrameworkDescription}, NativeAOT {!RuntimeFeature.IsDynamicCodeSupported}, {Environment.ProcessorCount} logical processors, Stopwatch {Stopwatch.Frequency:N0} Hz");
        report.AppendLine(CultureInfo.InvariantCulture, $"system timer: current {resolution?.Current / 10_000.0} ms, coarsest {resolution?.Coarsest / 10_000.0} ms, finest {resolution?.Finest / 10_000.0} ms");
        report.AppendLine(CultureInfo.InvariantCulture, $"shipped check: every {SessionTimes.VisibleInputCheckInterval.TotalMilliseconds} ms, tolerance {SessionTimes.VisibleInputCheckTolerance.TotalMilliseconds} ms");
        report.AppendLine();
    }

    /// <summary>Each read on its own, with the thread's kernel and user time beside the clock.</summary>
    private static void EachRead(Utf8JsonWriter writer, StringBuilder report)
    {
        report.AppendLine(CultureInfo.InvariantCulture, $"Each read on its own, {Calls:N0} calls, per call:");

        // The floor: a loop that reads nothing.
        Loop(writer, report, "emptyLoop", static count =>
        {
            for (var index = 0; index < count; index++)
            {
                _sink ^= (ulong)index;
            }

            return default;
        });

        Loop(writer, report, "GetForegroundWindow", static count =>
        {
            var nulls = 0L;

            for (var index = 0; index < count; index++)
            {
                var window = InputActivity.ForegroundWindow();

                nulls += window == nint.Zero ? 1 : 0;
                _sink ^= (ulong)window;
            }

            return new Counts(nulls, 0, 0, 0);
        });

        var front = InputActivity.ForegroundWindow();

        Loop(writer, report, "GetWindowThreadProcessId", count =>
        {
            var zeroThreads = 0L;
            var zeroPids = 0L;

            for (var index = 0; index < count; index++)
            {
                var thread = InputActivity.WindowThread(front, out var process);

                zeroThreads += thread is 0 ? 1 : 0;
                zeroPids += process is 0 ? 1 : 0;
                _sink ^= thread;
            }

            return new Counts(0, zeroThreads, zeroPids, 0);
        });

        Loop(writer, report, "GetForegroundWindow+GetWindowThreadProcessId", static count =>
        {
            var nulls = 0L;
            var zeroThreads = 0L;
            var zeroPids = 0L;

            for (var index = 0; index < count; index++)
            {
                var window = InputActivity.ForegroundWindow();

                if (window == nint.Zero)
                {
                    nulls++;
                    continue;
                }

                var thread = InputActivity.WindowThread(window, out var process);

                zeroThreads += thread is 0 ? 1 : 0;
                zeroPids += process is 0 ? 1 : 0;
                _sink ^= thread;
            }

            return new Counts(nulls, zeroThreads, zeroPids, 0);
        });

        Loop(writer, report, "GetLastInputInfo", static count =>
        {
            var refused = 0L;

            for (var index = 0; index < count; index++)
            {
                refused += InputActivity.LastInput(out var tick) ? 0 : 1;
                _sink ^= tick;
            }

            return new Counts(0, 0, 0, refused);
        });

        Loop(writer, report, "GetTickCount", static count =>
        {
            for (var index = 0; index < count; index++)
            {
                _sink ^= InputActivity.TickCount();
            }

            return default;
        });

        writer.WriteNumber("foregroundAtStart", (long)front);
        report.AppendLine();
    }

    /// <summary>The product's check, with one session and with a hundred.</summary>
    private static void Checks(Utf8JsonWriter writer, StringBuilder report)
    {
        report.AppendLine(CultureInfo.InvariantCulture, $"VisibleInputWatch.Tick over this desktop, {Calls:N0} checks, per check:");

        using var one = Watching(1, out var oneRegistrations);
        Loop(writer, report, "tickOneSession", count =>
        {
            for (var index = 0; index < count; index++)
            {
                one.Tick();
            }

            return default;
        });

        using var hundred = Watching(Hundred, out var hundredRegistrations);
        var rate = Loop(writer, report, "tickHundredSessions", count =>
        {
            for (var index = 0; index < count; index++)
            {
                hundred.Tick();
            }

            return default;
        });

        // The in-memory half a check runs only when it saw new input with a known window
        // in front: the rig's copy of it, one array of a hundred sessions and a hundred
        // comparisons, since checks a few nanoseconds apart see new input almost never.
        IVisibleWindowOwner[] sessions = [.. Enumerable.Range(0, Hundred).Select(static index => new Session((index * 2) + 1))];

        Loop(writer, report, "scanOfAHundredSessions", count =>
        {
            for (var index = 0; index < count; index++)
            {
                IVisibleWindowOwner[] copy = [.. sessions];

                foreach (var session in copy)
                {
                    if (session.WindowProcessId == 4)
                    {
                        session.PersonWasActive();
                    }
                }

                _sink ^= (ulong)copy.Length;
            }

            return default;
        }, Samples);

        // Checks timed in batches and one at a time, beside the same instruments around
        // nothing.
        report.AppendLine(CultureInfo.InvariantCulture, $"Hot checks with a hundred sessions, {Samples:N0} of them, against the same instruments around nothing (cycles at {rate:F0} a microsecond):");
        Distribution(writer, report, "hotCheckHundredSessions", hundred.Tick);
        Distribution(writer, report, "hotNothing", static () => { });

        foreach (var registration in oneRegistrations.Concat(hundredRegistrations))
        {
            registration.Dispose();
        }

        report.AppendLine();
    }

    private static VisibleInputWatch Watching(int sessions, out List<IDisposable> registrations)
    {
        // The product's watch over this desktop, with no timer: the loop is the timer.
        var watch = new VisibleInputWatch(new VisibleInputWatch.DesktopInput(), static _ => new Nothing(), NullLogger.Instance);

        // Odd pids; a session told anyway would only bump a counter.
        registrations = [.. Enumerable.Range(0, sessions).Select(index => watch.Watch(new Session((index * 2) + 1)))];
        return watch;
    }

    /// <summary>Runs one loop between two readings and writes what it cost per call; answers cycles per microsecond.</summary>
    private static double Loop(Utf8JsonWriter writer, StringBuilder report, string name, Func<int, Counts> body, int? only = null)
    {
        var calls = only ?? Calls;

        _ = body(Warmup);

        var before = Reading.Take();
        var counts = body(calls);
        var after = Reading.Take();

        var seconds = (after.Clock - before.Clock) / (double)Stopwatch.Frequency;
        var nanoseconds = seconds * 1e9 / calls;
        var cycles = (double)(after.Cycles - before.Cycles) / calls;
        var kernel = (after.Kernel - before.Kernel) / 10_000.0;
        var user = (after.User - before.User) / 10_000.0;
        var rate = (after.Cycles - before.Cycles) / (seconds * 1e6);

        writer.WriteStartObject(name);
        writer.WriteNumber("calls", calls);
        writer.WriteNumber("loopSeconds", Math.Round(seconds, 4));
        writer.WriteNumber("nanosecondsPerCall", Math.Round(nanoseconds, 2));
        writer.WriteNumber("cyclesPerCall", Math.Round(cycles, 1));
        writer.WriteNumber("threadKernelMs", kernel);
        writer.WriteNumber("threadUserMs", user);
        writer.WriteNumber("cyclesPerMicrosecond", Math.Round(rate, 1));
        writer.WriteNumber("nullWindows", counts.NullWindows);
        writer.WriteNumber("zeroThreads", counts.ZeroThreads);
        writer.WriteNumber("zeroPids", counts.ZeroPids);
        writer.WriteNumber("lastInputRefused", counts.LastInputRefused);
        writer.WriteEndObject();

        report.AppendLine(CultureInfo.InvariantCulture,
            $"  {name,-46} {calls,10:N0} x {nanoseconds,8:F1} ns {cycles,7:F0} cycles   loop {seconds,7:F3} s: thread kernel {kernel,7:F1} ms user {user,8:F1} ms   nulls {counts.NullWindows} zero threads {counts.ZeroThreads} zero pids {counts.ZeroPids} refused {counts.LastInputRefused}");

        return rate;
    }

    /// <summary>
    /// Per-check figures two ways. In batches of <see cref="Batch"/>, a clock read and a
    /// cycle read either side, so the instrument costs each check a hundredth of itself;
    /// and one check at a time between two clock reads alone, which this machine's
    /// <see cref="Stopwatch"/> resolves to 100 ns.
    /// </summary>
    private static void Distribution(Utf8JsonWriter writer, StringBuilder report, string name, Action body)
    {
        var batches = Samples / Batch;
        var batchNs = new double[batches];
        var batchCycles = new double[batches];
        var single = new double[Samples];

        for (var index = 0; index < Warmup; index++)
        {
            body();
        }

        for (var batch = 0; batch < batches; batch++)
        {
            var clock = Stopwatch.GetTimestamp();
            var cycle = Native.ThreadCycles();

            for (var index = 0; index < Batch; index++)
            {
                body();
            }

            var cycles = Native.ThreadCycles() - cycle;
            var ticks = Stopwatch.GetTimestamp() - clock;

            batchNs[batch] = ticks * 1e9 / Stopwatch.Frequency / Batch;
            batchCycles[batch] = (double)cycles / Batch;
        }

        for (var index = 0; index < Samples; index++)
        {
            var clock = Stopwatch.GetTimestamp();

            body();

            single[index] = (Stopwatch.GetTimestamp() - clock) * 1e9 / Stopwatch.Frequency;
        }

        writer.WriteStartObject(name);
        writer.WriteNumber("checks", Samples);
        writer.WriteNumber("batch", Batch);
        Stats(writer, "perCheckInBatchesNs", batchNs);
        Stats(writer, "perCheckInBatchesCycles", batchCycles);
        Stats(writer, "oneAtATimeNs", single);
        writer.WriteEndObject();

        report.AppendLine(CultureInfo.InvariantCulture, $"  {name,-24} in batches of {Batch}, per check: {Line(batchNs, "ns")};  {Line(batchCycles, "cycles")}");
        report.AppendLine(CultureInfo.InvariantCulture, $"  {string.Empty,-24} one at a time: {Line(single, "ns")}");
    }

    /// <summary>Checks per batch in <see cref="Distribution"/>.</summary>
    private const int Batch = 100;

    private static double At(double[] sorted, double fraction) =>
        sorted[(int)Math.Min(sorted.Length - 1, Math.Floor(fraction * sorted.Length))];

    private static void Stats(Utf8JsonWriter writer, string name, double[] values)
    {
        var sorted = values.Order().ToArray();

        writer.WriteStartObject(name);
        writer.WriteNumber("n", sorted.Length);
        writer.WriteNumber("mean", Math.Round(sorted.Average(), 2));
        writer.WriteNumber("min", Math.Round(sorted[0], 2));
        writer.WriteNumber("median", Math.Round(At(sorted, 0.5), 2));
        writer.WriteNumber("p99", Math.Round(At(sorted, 0.99), 2));
        writer.WriteNumber("p999", Math.Round(At(sorted, 0.999), 2));
        writer.WriteNumber("max", Math.Round(sorted[^1], 2));
        writer.WriteEndObject();
    }

    private static string Line(double[] values, string unit)
    {
        var sorted = values.Order().ToArray();

        return string.Create(CultureInfo.InvariantCulture,
            $"mean {sorted.Average():F1} median {At(sorted, 0.5):F1} p99 {At(sorted, 0.99):F1} p99.9 {At(sorted, 0.999):F1} max {sorted[^1]:F1} {unit}");
    }

    /// <summary>Starts the four timed processes together, waits for all four, and summarises them.</summary>
    private static void TimedArms(Utf8JsonWriter writer, StringBuilder report, string output, int seconds)
    {
        var room = ((int)SessionTimes.VisibleInputCheckTolerance.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
        (string Name, string Tolerance, string Watch)[] arms =
        [
            ("timer", room, "1"),
            ("exact", "0", "1"),
            ("wake", room, "0"),
            ("control", "-1", "0"),
        ];

        var started = new List<(string Name, Process Process, DateTime Start)>();

        foreach (var (name, tolerance, watch) in arms)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in (string[])["arm", "--name", name, "--tolerance-ms", tolerance, "--watch", watch, "--out", output, "--seconds", seconds.ToString(CultureInfo.InvariantCulture)])
            {
                start.ArgumentList.Add(argument);
            }

            var process = Process.Start(start)!;
            started.Add((name, process, process.StartTime.ToUniversalTime()));
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"Timed window: {seconds} s, four processes started together, each a child of this one, held by the handle that started it:");

        foreach (var (name, process, start) in started)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"  {name,-8} pid {process.Id} started {start:o}");
        }

        foreach (var (_, process, _) in started)
        {
            process.WaitForExit();
        }

        using var control = Read(output, "control");
        var controlCycles = control.RootElement.GetProperty("windowCycles").GetUInt64();

        foreach (var (name, process, start) in started)
        {
            var whole = Native.ProcessCycles(process.SafeHandle);
            var (kernel, user) = Native.ProcessTimes(process.SafeHandle);
            using var window = Read(output, name);
            var root = window.RootElement;

            writer.WriteStartObject(name);
            writer.WriteNumber("pid", process.Id);
            writer.WriteString("started", start.ToString("o", CultureInfo.InvariantCulture));
            writer.WriteNumber("exitCode", process.ExitCode);
            writer.WriteNumber("wholeLifeCycles", whole);
            writer.WriteNumber("wholeLifeKernelMs", kernel / 10_000.0);
            writer.WriteNumber("wholeLifeUserMs", user / 10_000.0);

            foreach (var property in root.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            var cycles = root.GetProperty("windowCycles").GetUInt64();
            var ticks = root.GetProperty("ticks").GetInt32();
            var cpu = root.GetProperty("windowKernelMs").GetDouble() + root.GetProperty("windowUserMs").GetDouble();
            var over = (long)cycles - (long)controlCycles;

            writer.WriteNumber("windowCyclesOverControl", over);
            writer.WriteEndObject();

            report.AppendLine(CultureInfo.InvariantCulture,
                $"  {name,-8} window {root.GetProperty("windowSeconds").GetDouble(),7:F1} s: cycles {cycles,13:N0}, over control {over,13:N0}; CPU by GetProcessTimes {cpu,6:F1} ms; ticks {ticks}; exit {process.ExitCode}");

            if (ticks > 1)
            {
                var spacing = root.GetProperty("spacingMs").EnumerateArray().Select(static value => value.GetDouble()).Order().ToArray();

                report.AppendLine(CultureInfo.InvariantCulture,
                    $"           spacing ms: min {spacing[0]:F1}, median {spacing[spacing.Length / 2]:F1}, max {spacing[^1]:F1}; first tick {root.GetProperty("firstTickMs").GetDouble():F1} ms after the window opened; over control per tick {(double)over / ticks:N0} cycles");
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"           null windows {root.GetProperty("nullWindows").GetInt32()}, zero threads {root.GetProperty("zeroThreads").GetInt32()}, zero pids {root.GetProperty("zeroPids").GetInt32()}, last-input refused {root.GetProperty("lastInputRefused").GetInt32()}; the setting thread had ended before the first tick: {root.GetProperty("setterEndedBeforeFirstTick").GetBoolean()}");
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"           each tick's {(root.GetProperty("productWatch").GetBoolean() ? "four reads" : "instrument around nothing")}, cold: {Summary(root.GetProperty("coldCheckNs"), "ns")};  {Summary(root.GetProperty("coldCheckCycles"), "cycles")}");
            }
        }
    }

    private static JsonDocument Read(string output, string name) =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, name + ".json")));

    private static string Summary(JsonElement stats, string unit) =>
        string.Create(CultureInfo.InvariantCulture,
            $"median {stats.GetProperty("median").GetDouble():F1} p99 {stats.GetProperty("p99").GetDouble():F1} max {stats.GetProperty("max").GetDouble():F1} mean {stats.GetProperty("mean").GetDouble():F1} {unit} over {stats.GetProperty("n").GetInt32()}");

    /// <summary>
    /// One timed process: the shipped watch with one session, the shipped timer with an
    /// empty tick, or no timer at all, for a fixed window.
    /// </summary>
    private static int Arm(string output, string name, int seconds, int toleranceMs, bool watching)
    {
        // Every arm settles the same way before its window opens.
        Thread.Sleep(TimeSpan.FromSeconds(2));

        var probe = new RecordingProbe(capacity: seconds + 16);
        var setter = new SetterThread();
        VisibleInputWatch? watch = null;
        IDisposable? running = null;

        var cycles0 = Native.ProcessCycles();
        var (kernel0, user0) = Native.ProcessTimes();
        var clock0 = Stopwatch.GetTimestamp();

        if (toleranceMs >= 0 && watching)
        {
            // The shipped watch, except that its timer is started from a thread that ends
            // straight away, so the window also shows whether the timer outlives the
            // thread that set it.
            watch = new VisibleInputWatch(probe, tick => setter.Start(TimeSpan.FromMilliseconds(toleranceMs), tick), NullLogger.Instance);
            running = watch.Watch(new Session(1));
        }
        else if (toleranceMs >= 0)
        {
            // The same timer, waking for nothing but the instrument.
            running = setter.Start(TimeSpan.FromMilliseconds(toleranceMs), probe.Nothing);
        }

        Thread.Sleep(TimeSpan.FromSeconds(seconds));

        var clock1 = Stopwatch.GetTimestamp();
        var cycles1 = Native.ProcessCycles();
        var (kernel1, user1) = Native.ProcessTimes();

        running?.Dispose();
        watch?.Dispose();

        var stamps = probe.Stamps();
        var json = new MemoryStream();

        using (var writer = new Utf8JsonWriter(json, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("arm", name);
            writer.WriteNumber("toleranceMs", toleranceMs);
            writer.WriteBoolean("productWatch", watching);
            writer.WriteNumber("intervalMs", SessionTimes.VisibleInputCheckInterval.TotalMilliseconds);
            writer.WriteNumber("windowSeconds", Math.Round((clock1 - clock0) / (double)Stopwatch.Frequency, 3));
            writer.WriteNumber("windowCycles", cycles1 - cycles0);
            writer.WriteNumber("windowKernelMs", (kernel1 - kernel0) / 10_000.0);
            writer.WriteNumber("windowUserMs", (user1 - user0) / 10_000.0);
            writer.WriteNumber("ticks", stamps.Length);
            writer.WriteNumber("firstTickMs", stamps.Length > 0 ? Math.Round((stamps[0] - clock0) * 1000.0 / Stopwatch.Frequency, 2) : -1);
            writer.WriteNumber("nullWindows", probe.NullWindows);
            writer.WriteNumber("zeroThreads", probe.ZeroThreads);
            writer.WriteNumber("zeroPids", probe.ZeroPids);
            writer.WriteNumber("lastInputRefused", probe.Refused);
            writer.WriteBoolean("setterEndedBeforeFirstTick", setter.EndedBefore(stamps.Length > 0 ? stamps[0] : long.MaxValue));

            if (stamps.Length > 0)
            {
                Stats(writer, "coldCheckNs", probe.CheckNs());
                Stats(writer, "coldCheckCycles", probe.CheckCycles());
            }

            writer.WriteStartArray("spacingMs");

            for (var index = 1; index < stamps.Length; index++)
            {
                writer.WriteNumberValue(Math.Round((stamps[index] - stamps[index - 1]) * 1000.0 / Stopwatch.Frequency, 2));
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        File.WriteAllBytes(Path.Combine(output, name + ".json"), json.ToArray());
        return 0;
    }

    /// <summary>The four readings a measured loop is taken between.</summary>
    private readonly record struct Reading(long Clock, ulong Cycles, long Kernel, long User)
    {
        public static Reading Take()
        {
            var (kernel, user) = Native.ThreadTimes();
            var cycles = Native.ThreadCycles();

            return new Reading(Stopwatch.GetTimestamp(), cycles, kernel, user);
        }
    }

    /// <summary>What a loop of reads saw.</summary>
    private readonly record struct Counts(long NullWindows, long ZeroThreads, long ZeroPids, long LastInputRefused);

    /// <summary>A session whose browser is an odd pid.</summary>
    private sealed class Session(int pid) : IVisibleWindowOwner
    {
        public int? WindowProcessId { get; } = pid;

        public void PersonWasActive() => _sink++;
    }

    /// <summary>The timer the cost loops hand the watch: none.</summary>
    private sealed class Nothing : IDisposable
    {
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// The product's four reads, each check stamped with the clock as it starts, the four
    /// reads of each check timed from before the first to after the last (the product
    /// reads the last-input tick last), and the answers that mean <i>unknown</i> counted.
    /// </summary>
    private sealed class RecordingProbe(int capacity) : IInputProbe
    {
        private readonly long[] _stamps = new long[capacity];
        private readonly double[] _checkNs = new double[capacity];
        private readonly double[] _checkCycles = new double[capacity];
        private int _count;
        private long _clock;
        private ulong _cycles;

        public int NullWindows { get; private set; }

        public int ZeroThreads { get; private set; }

        public int ZeroPids { get; private set; }

        public int Refused { get; private set; }

        public long[] Stamps() => _stamps[..Math.Min(_count, _stamps.Length)];

        public double[] CheckNs() => _checkNs[..Math.Min(_count, _checkNs.Length)];

        public double[] CheckCycles() => _checkCycles[..Math.Min(_count, _checkCycles.Length)];

        /// <summary>The wake arm's whole tick: the stamp and the instrument around nothing.</summary>
        public void Nothing()
        {
            Begin();
            End();
        }

        public nint ForegroundWindow()
        {
            Begin();

            var window = InputActivity.ForegroundWindow();

            NullWindows += window == nint.Zero ? 1 : 0;
            return window;
        }

        public uint WindowThread(nint window, out int processId)
        {
            var thread = InputActivity.WindowThread(window, out processId);

            ZeroThreads += thread is 0 ? 1 : 0;
            ZeroPids += processId is 0 ? 1 : 0;
            return thread;
        }

        public bool LastInput(out uint tick)
        {
            var answered = InputActivity.LastInput(out tick);

            Refused += answered ? 0 : 1;
            End();
            return answered;
        }

        public uint TickCount() => InputActivity.TickCount();

        private void Begin()
        {
            if (_count < _stamps.Length)
            {
                _stamps[_count] = Stopwatch.GetTimestamp();
            }

            _cycles = Native.ThreadCycles();
            _clock = Stopwatch.GetTimestamp();
        }

        private void End()
        {
            var clock = Stopwatch.GetTimestamp();
            var cycles = Native.ThreadCycles();

            if (_count < _checkNs.Length)
            {
                _checkNs[_count] = (clock - _clock) * 1e9 / Stopwatch.Frequency;
                _checkCycles[_count] = cycles - _cycles;
            }

            _count++;
        }
    }

    /// <summary>Starts the product's timer from a thread that ends as soon as it has.</summary>
    private sealed class SetterThread
    {
        private long _ended = long.MaxValue;

        public IDisposable Start(TimeSpan tolerance, Action tick)
        {
            CoalescableTimer? timer = null;

            var thread = new Thread(() => timer = CoalescableTimer.Start(SessionTimes.VisibleInputCheckInterval, tolerance, tick))
            {
                IsBackground = true,
            };

            thread.Start();
            thread.Join();
            _ended = Stopwatch.GetTimestamp();

            return timer!;
        }

        public bool EndedBefore(long stamp) => _ended < stamp;
    }
}
