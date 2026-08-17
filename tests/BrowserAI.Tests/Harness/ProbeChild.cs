// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Protocol;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// One <c>BrowserAI.TestProbe transport-child</c> running behind a real
/// <see cref="DirectStdioClientTransport"/>, with the report it wrote about
/// itself.
/// </summary>
internal sealed class ProbeChild : IAsyncDisposable
{
    private static readonly string ProbePath = Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe");

    /// <summary>
    /// How long anything in this harness waits before calling a child dead.
    /// Generous on purpose: a machine under load starting a process is slow,
    /// and a flaky timeout reports as a transport bug.
    /// </summary>
    private static readonly TimeSpan Patience = TestDefaults.ProcessHang;

    private readonly ScratchDirectory _scratch;
    private readonly string _framePath;

    private ProbeChild(ScratchDirectory scratch, ChildProcessSession session, JsonObject report, string framePath)
    {
        _scratch = scratch;
        _framePath = framePath;
        Session = session;
        Report = report;
    }

    /// <summary>The live transport, typed so its pid and exit code are reachable.</summary>
    public ChildProcessSession Session { get; }

    /// <summary>What the child said about itself once it was running.</summary>
    public JsonObject Report { get; }

    /// <summary>The child's process id, as the child itself reported it.</summary>
    public int ReportedProcessId => (int)Report["pid"]!;

    /// <summary>The environment block the child was actually handed.</summary>
    public IReadOnlyDictionary<string, string> Environment =>
        Report["environment"]!.AsObject().ToDictionary(
            entry => entry.Key,
            entry => entry.Value!.GetValue<string>(),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Every frame the child received, exactly as its bytes arrived.</summary>
    public byte[] ReceivedFrameBytes()
    {
        using var stream = new FileStream(_framePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Starts a probe child behind the transport under test.</summary>
    /// <param name="label">Names the scratch directory, so a leftover says which test left it.</param>
    /// <param name="standardErrorLineCount">How many lines the child writes to stderr before doing anything else.</param>
    /// <param name="standardErrorLines">Where those lines are delivered.</param>
    /// <param name="additionalEnvironment">Variables this child needs beyond the allowlist.</param>
    /// <returns>The running child.</returns>
    public static async Task<ProbeChild> StartAsync(
        string label,
        int standardErrorLineCount = 0,
        Action<string>? standardErrorLines = null,
        IEnumerable<KeyValuePair<string, string>>? additionalEnvironment = null)
    {
        var scratch = ScratchDirectory.Create(label);

        try
        {
            var reportPath = Path.Combine(scratch.Path, "report.json");
            var framePath = Path.Combine(scratch.Path, "frames.jsonl");

            var transport = new DirectStdioClientTransport(new ChildProcessOptions
            {
                Command = ProbePath,
                WorkingDirectory = scratch.Path,
                Environment = ChildEnvironment.Build(additionalEnvironment),
                Arguments =
                [
                    "transport-child",
                    reportPath,
                    standardErrorLineCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    framePath,
                ],
                StandardErrorLines = standardErrorLines,
                ShutdownTimeout = TimeSpan.FromSeconds(5),
            });

            var session = (ChildProcessSession)await transport.ConnectAsync().ConfigureAwait(false);

            try
            {
                var report = await WaitForReportAsync(reportPath).ConfigureAwait(false);
                return new ProbeChild(scratch, session, report, framePath);
            }
            catch
            {
                await session.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            scratch.Dispose();
            throw;
        }
    }

    /// <summary>Sends one message down to the child.</summary>
    /// <param name="message">The message to send.</param>
    /// <returns>A task that completes once the frame is on the wire.</returns>
    public Task SendAsync(JsonRpcMessage message) => Session.SendMessageAsync(message);

    /// <summary>Waits for the child to echo a message back.</summary>
    /// <returns>The echoed message.</returns>
    public async Task<JsonRpcMessage> ReceiveAsync()
    {
        using var deadline = new CancellationTokenSource(Patience);

        return await Session.MessageReader.ReadAsync(deadline.Token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Session.DisposeAsync().ConfigureAwait(false);
        _scratch.Dispose();
    }

    private static async Task<JsonObject> WaitForReportAsync(string reportPath)
    {
        // The child writes this once, early, and the test cannot proceed until
        // it exists. Polled rather than signalled: a named event would be a
        // second mechanism to get wrong, and the whole wait is milliseconds.
        var deadline = Stopwatch.StartNew();

        while (deadline.Elapsed < Patience)
        {
            if (File.Exists(reportPath))
            {
                try
                {
                    var text = await File.ReadAllTextAsync(reportPath).ConfigureAwait(false);

                    if (JsonNode.Parse(text) is JsonObject report)
                    {
                        return report;
                    }
                }
                catch (JsonException)
                {
                    // Caught mid-write. Retried rather than failed, because a
                    // torn read here would report as "the child never started".
                }
                catch (IOException)
                {
                    // Same: the child still has the handle open.
                }
            }

            await Task.Delay(25).ConfigureAwait(false);
        }

        throw new TimeoutException($"The probe child never wrote '{reportPath}'.");
    }
}
