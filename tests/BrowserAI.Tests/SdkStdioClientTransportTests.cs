// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Tests.Harness;
using ModelContextProtocol.Client;

namespace BrowserAI.Tests;

/// <summary>
/// Whether the SDK's own stdio client transport still needs replacing.
/// </summary>
/// <remarks>
/// <para>
/// This is not a test of the SDK, and a failure here is not a bug. It answers
/// <a href="../../kb/README.md#re-verification-index">re-verification row
/// 31</a> — <i><c>StdioClientTransport</c> still wraps in <c>cmd.exe</c></i> —
/// which was <i>manual</i> until the transport that replaces it existed to be
/// compared against.
/// </para>
/// <para>
/// If upstream drops the wrapping, this test goes red, and what is owed is a
/// rewrite of the <b>reason</b> BrowserAI owns a client transport, not a
/// rewrite of the transport: direct spawning stays correct either way, and the
/// argument-fidelity failures the wrapping causes stop being the argument for
/// it.
/// </para>
/// </remarks>
internal sealed class SdkStdioClientTransportTests
{
    [Test]
    public async Task TheSdkTransportStillPutsCmdExeBetweenUsAndTheChild()
    {
        using var scratch = ScratchDirectory.Create("sdk-cmd-wrapping");

        var reportPath = Path.Combine(scratch.Path, "report.json");
        var framePath = Path.Combine(scratch.Path, "frames.jsonl");

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe"),
            Arguments = ["transport-child", reportPath, "0", framePath],
            WorkingDirectory = scratch.Path,
        });

        var session = await transport.ConnectAsync();

        try
        {
            var childProcessId = await WaitForReportedProcessIdAsync(reportPath);

            // The child is real, it started, and it answered. Everything a
            // functional test would check passes -- and its parent is a shell
            // that BrowserAI never asked for and cannot see.
            await Assert.That(ParentProcess.IdOf(childProcessId)).IsNotEqualTo(Environment.ProcessId);
            await Assert.That(ParentProcess.ParentImageNameOf(childProcessId)).IsEqualTo("cmd");
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    private static async Task<int> WaitForReportedProcessIdAsync(string reportPath)
    {
        for (var attempt = 0; attempt < 1200; attempt++)
        {
            if (File.Exists(reportPath))
            {
                try
                {
                    if (JsonNode.Parse(await File.ReadAllTextAsync(reportPath)) is JsonObject report)
                    {
                        return (int)report["pid"]!;
                    }
                }
                catch (Exception exception) when (exception is System.Text.Json.JsonException or IOException)
                {
                    // Caught mid-write; retried rather than failed.
                }
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"The probe child never wrote '{reportPath}'.");
    }
}
