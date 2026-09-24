// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using BrowserAI.Coordination;
using BrowserAI.Updates;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The suite's side of a server's pipe: finding a server's marker, waiting for
/// its pipe to listen, and reading what Windows says about a pipe handle.
/// </summary>
/// <remarks>
/// <b>Windows is asked directly, and the product's own code is not.</b>
/// <c>GetSecurityInfo</c> and <c>GetNamedPipeInfo</c> are declared here and
/// nowhere in the product, so what an arm reads about a pipe is Windows' answer
/// and not the product describing itself.
/// </remarks>
internal static partial class ServerPipeRig
{
    private const int SeKernelObject = 6;
    private const uint DaclSecurityInformation = 0x00000004;

    /// <summary>The prefix every full pipe name carries, which the framework's pipe classes leave off.</summary>
    public const string PipeNamespace = @"\\.\pipe\";

    /// <summary>The name <see cref="System.IO.Pipes"/> wants for a full pipe name.</summary>
    /// <param name="fullName">A name starting <c>\\.\pipe\</c>.</param>
    /// <returns>The name without the prefix.</returns>
    public static string ShortName(string fullName) => fullName[PipeNamespace.Length..];

    /// <summary>
    /// Waits for a process to join the live set under a root, and answers its
    /// marker.
    /// </summary>
    /// <param name="processId">The process.</param>
    /// <param name="installRoot">The root it joined under.</param>
    /// <param name="patience">A hang detector, never a budget.</param>
    /// <returns>The marker file.</returns>
    public static async Task<string> MarkerOfAsync(int processId, string installRoot, TimeSpan patience)
    {
        var directory = LiveInstances.DirectoryUnder(installRoot);
        var waited = Stopwatch.StartNew();

        while (true)
        {
            if (Directory.Exists(directory)
                && Directory.EnumerateFiles(directory, $"{processId}-*.live").FirstOrDefault() is { } marker)
            {
                return marker;
            }

            if (waited.Elapsed > patience)
            {
                throw new TimeoutException(
                    $"pid {processId} took no marker under '{directory}' in {patience.TotalMinutes:F0} minutes. That is a hang detector: a server joins the live set before it starts anything slow.");
            }

            await Task.Delay(20);
        }
    }

    /// <summary>
    /// Describes a server, waiting out the moment between its marker and its
    /// pipe: a server joins the census a few statements before it listens.
    /// </summary>
    /// <param name="marker">The server's marker.</param>
    /// <param name="patience">A hang detector, never a budget; also each call's bound.</param>
    /// <returns>The first answer that is not <see cref="ServerPipeOutcome.NoPipe"/>.</returns>
    public static async Task<ServerPipeAnswer> DescribeWhenListeningAsync(string marker, TimeSpan patience)
    {
        var waited = Stopwatch.StartNew();

        while (true)
        {
            var answer = await ServerPipeClient.DescribeAsync(marker, patience);

            if (answer.Outcome is not ServerPipeOutcome.NoPipe || waited.Elapsed > patience)
            {
                return answer;
            }

            await Task.Delay(20);
        }
    }

    /// <summary>
    /// Describes a server until it counts no call in flight, or until the
    /// patience is spent; the last answer either way.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>A call's count ends after its answer is written, not before -- found
    /// 2026-09-24.</b> The proxy closes a call's activity scope when the handler
    /// that wrote the answer returns, so a client that asks the pipe the instant
    /// its answer arrives can be told the call is still in flight. That went red
    /// once in a gate. The claim an arm makes is that an answered call stops
    /// counting, never how fast, so it waits, bounded by the suite's hang detector.
    /// </remarks>
    /// <param name="marker">The server's live marker.</param>
    /// <param name="patience">The hang detector.</param>
    /// <returns>The last answer.</returns>
    public static async Task<ServerPipeAnswer> DescribeWhenSettledAsync(string marker, TimeSpan patience)
    {
        var waited = Stopwatch.StartNew();

        while (true)
        {
            var answer = await ServerPipeClient.DescribeAsync(marker, patience);

            if (answer.Description is not { CallsInFlight: > 0 } || waited.Elapsed > patience)
            {
                return answer;
            }

            await Task.Delay(20);
        }
    }

    /// <summary>The DACL Windows reports for a pipe, read off an open handle to it.</summary>
    /// <param name="handle">Any handle to the pipe that carries <c>READ_CONTROL</c>.</param>
    /// <returns>The DACL.</returns>
    public static RawAcl DaclOf(SafeHandle handle)
    {
        var error = GetSecurityInfo(handle, SeKernelObject, DaclSecurityInformation, out _, out _, out _, out _, out var descriptor);

        if (error is not 0)
        {
            throw new System.ComponentModel.Win32Exception((int)error, "GetSecurityInfo refused the pipe handle.");
        }

        try
        {
            var bytes = new byte[GetSecurityDescriptorLength(descriptor)];
            Marshal.Copy(descriptor, bytes, 0, bytes.Length);

            return new RawSecurityDescriptor(bytes, 0).DiscretionaryAcl
                ?? throw new InvalidOperationException("The pipe's security descriptor carries no DACL at all.");
        }
        finally
        {
            _ = LocalFree(descriptor);
        }
    }

    /// <summary>The flags <c>GetNamedPipeInfo</c> reports for a pipe handle.</summary>
    /// <param name="handle">A client or server end.</param>
    /// <returns>The flags word.</returns>
    public static uint FlagsOf(SafeHandle handle) =>
        GetNamedPipeInfo(handle, out var flags, out _, out _, out _)
            ? flags
            : throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "GetNamedPipeInfo refused the pipe handle.");

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll")]
    private static partial uint GetSecurityInfo(
        SafeHandle handle,
        int objectType,
        uint securityInfo,
        out nint owner,
        out nint group,
        out nint dacl,
        out nint sacl,
        out nint securityDescriptor);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll")]
    private static partial uint GetSecurityDescriptorLength(nint securityDescriptor);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint LocalFree(nint memory);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeInfo(
        SafeHandle namedPipe,
        out uint flags,
        out uint outBufferSize,
        out uint inBufferSize,
        out uint maxInstances);
}

/// <summary>
/// A responder the suite scripts: a fixed description, a stop that is counted,
/// and a gate an arm can hold shut to make the pipe's thread hang.
/// </summary>
internal sealed class ScriptedResponder : IServerPipeResponder, IDisposable
{
    private readonly ManualResetEventSlim _open = new(initialState: true);
    private int _stops;

    /// <summary>How many stops arrived.</summary>
    public int Stops => Volatile.Read(ref _stops);

    /// <summary>Makes every describe wait until <see cref="Release"/>.</summary>
    public void Hang() => _open.Reset();

    /// <summary>Lets every waiting describe finish.</summary>
    public void Release() => _open.Set();

    /// <inheritdoc />
    public ServerPipeReply Describe()
    {
        // Bounded, so a teardown that forgot Release cannot hold the pipe's
        // thread for ever -- a hang detector, not a budget.
        _ = _open.Wait(TestDefaults.InProcessHang);

        return new ServerPipeReply(new ServerDescription(
            ServerPipeProtocol.Version,
            Environment.ProcessId,
            Interop.ProcessLiveness.CreationTimeOfThisProcess(),
            "scripted",
            Environment.ProcessPath ?? string.Empty,
            ServerDescription.States.Serving,
            new ClientIdentity("scripted-client", null, "1"),
            Environment.CurrentDirectory,
            DateTimeOffset.UtcNow,
            null,
            0,
            []).ToJson());
    }

    /// <inheritdoc />
    public ServerPipeReply Stop() =>
        new(ServerPipeProtocol.Acknowledged(Environment.ProcessId), () => Interlocked.Increment(ref _stops));

    /// <inheritdoc />
    public void Dispose()
    {
        _open.Set();
        _open.Dispose();
    }
}
