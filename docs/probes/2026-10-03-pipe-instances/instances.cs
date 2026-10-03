// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// How many instances a pipe created the way BrowserAI creates its parallel pipes
// can hold connected at once. Every argument to CreateNamedPipeW is the one
// src/BrowserAI.Core/Interop/NamedPipes.cs passes: PIPE_ACCESS_DUPLEX, with
// FILE_FLAG_FIRST_PIPE_INSTANCE on the first instance only; PIPE_REJECT_REMOTE_CLIENTS
// as the whole pipe mode; nMaxInstances 255 (PIPE_UNLIMITED_INSTANCES); 64 KiB out,
// 4 KiB in; a default timeout of 0; and a DACL whose one entry is the current user.
// The client end is opened the way NamedPipes.OpenClient opens it.
//
// The listener's shape is ServerPipe.ServeInParallel's: one instance listens, a
// client connects to it, the next instance is created, and the connected one is
// kept. Nothing is ever closed until the target is reached, so every instance
// stays connected for the whole run.
//
// Usage: dotnet run instances.cs -- <target> [<target> ...]
// Each target runs on a fresh pipe name, and every handle of one target is
// closed before the next begins.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

const uint PipeAccessDuplex = 0x00000003;
const uint FileFlagFirstPipeInstance = 0x00080000;
const uint FileFlagOverlapped = 0x40000000;
const uint PipeRejectRemoteClients = 0x00000008;
const uint UnlimitedInstances = 255;
const uint GenericRead = 0x80000000;
const uint GenericWrite = 0x40000000;
const uint OpenExisting = 3;
const int ErrorPipeConnected = 535;

// `--max <n>` replaces nMaxInstances for the positive control: a pipe created with a
// real cap, which the probe has to be able to see refused.
var maxIndex = Array.IndexOf(args, "--max");
var maxInstances = maxIndex >= 0 ? uint.Parse(args[maxIndex + 1], System.Globalization.CultureInfo.InvariantCulture) : UnlimitedInstances;
var targets = args.Where((_, index) => maxIndex < 0 || (index != maxIndex && index != maxIndex + 1)).Select(int.Parse).ToArray() is { Length: > 0 } given ? given : [300, 600];
var sid = WindowsIdentity.GetCurrent().User!.Value;

Console.WriteLine($"started {DateTimeOffset.UtcNow:O} pid {Environment.ProcessId} runtime {Environment.Version} os {RuntimeInformation.OSDescription}");
Console.WriteLine($"nMaxInstances {maxInstances}{(maxInstances == UnlimitedInstances ? " (PIPE_UNLIMITED_INSTANCES)" : " (a real cap: the positive control)")}, out 65536, in 4096, timeout 0, DACL D:P(A;;GA;;;<current user SID>)");

foreach (var target in targets)
{
    run(target);
}

Console.WriteLine($"finished {DateTimeOffset.UtcNow:O}");
return;

void run(int target)
{
    var name = $@"\\.\pipe\BrowserAI-instance-probe-{Guid.NewGuid():N}";
    var servers = new List<SafeFileHandle>();
    var clients = new List<SafeFileHandle>();
    var clock = Stopwatch.StartNew();
    var handlesBefore = Process.GetCurrentProcess().HandleCount;
    SafeFileHandle? listening = null;

    Console.WriteLine();
    Console.WriteLine($"== target {target} connected instances on {name}");

    try
    {
        listening = create(name, PipeAccessDuplex | FileFlagFirstPipeInstance, out var firstError);

        if (listening is null)
        {
            Console.WriteLine($"   the first instance was refused: {firstError} {new Win32Exception(firstError).Message}");
            return;
        }

        var connected = 0;
        string? stopped = null;

        while (connected < target)
        {
            var client = CreateFileW(name, GenericRead | GenericWrite, 0, nint.Zero, OpenExisting, FileFlagOverlapped, nint.Zero);

            if (client.IsInvalid)
            {
                var error = Marshal.GetLastPInvokeError();
                client.Dispose();
                stopped = $"client {connected + 1} could not open the pipe: {error} {new Win32Exception(error).Message}";
                break;
            }

            clients.Add(client);

            if (!ConnectNamedPipe(listening!, nint.Zero) && Marshal.GetLastPInvokeError() is var connectError && connectError is not ErrorPipeConnected)
            {
                stopped = $"instance {connected + 1} did not report its client connected: {connectError} {new Win32Exception(connectError).Message}";
                break;
            }

            if (!GetNamedPipeClientProcessId(listening!, out var clientPid) || clientPid != (uint)Environment.ProcessId)
            {
                stopped = $"instance {connected + 1} names client pid {clientPid}, not this process";
                break;
            }

            servers.Add(listening!);
            listening = null;
            connected++;

            if (connected is 254 or 255 or 256 or 257 || connected % 100 is 0 || connected == target)
            {
                Console.WriteLine($"   {connected,5} connected at {clock.Elapsed.TotalMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),9} ms");
            }

            var next = create(name, PipeAccessDuplex, out var nextError);

            if (next is null)
            {
                stopped = $"instance {connected + 1} was refused by CreateNamedPipeW: {nextError} {new Win32Exception(nextError).Message}";
                listening = null;
                break;
            }

            listening = next;
        }

        if (stopped is not null)
        {
            Console.WriteLine($"   STOPPED with {connected} connected: {stopped}");
        }

        // One more caller, to the instance that is listening now: the pipe still
        // takes a caller with every earlier instance held.
        if (stopped is null)
        {
            using var extra = CreateFileW(name, GenericRead | GenericWrite, 0, nint.Zero, OpenExisting, FileFlagOverlapped, nint.Zero);
            var extraError = extra.IsInvalid ? Marshal.GetLastPInvokeError() : 0;
            Console.WriteLine(extra.IsInvalid
                ? $"   one more caller, number {connected + 1}: REFUSED {extraError} {new Win32Exception(extraError).Message}"
                : $"   one more caller, number {connected + 1}: connected");

            // And bytes cross the last held connection, both ways.
            var lastServer = servers[^1];
            var lastClient = clients[^1];

            using var serverStream = new FileStream(lastServer, FileAccess.ReadWrite, bufferSize: 0, isAsync: false);
            using var clientStream = new FileStream(lastClient, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);

            clientStream.Write("ping\n"u8);
            var fromClient = new byte[5];
            serverStream.ReadExactly(fromClient);
            serverStream.Write("pong\n"u8);
            var fromServer = new byte[5];
            clientStream.ReadExactly(fromServer);

            Console.WriteLine($"   bytes through connection {connected}: server read '{System.Text.Encoding.ASCII.GetString(fromClient).TrimEnd()}', client read '{System.Text.Encoding.ASCII.GetString(fromServer).TrimEnd()}'");

            servers.RemoveAt(servers.Count - 1);
            clients.RemoveAt(clients.Count - 1);
        }

        Console.WriteLine($"   handles held by this process: {Process.GetCurrentProcess().HandleCount} (before the target: {handlesBefore})");
        Console.WriteLine($"   RESULT target {target}: {connected} connected at once, {(stopped is null ? "no refusal" : "refused")}, {clock.Elapsed.TotalMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} ms");
    }
    finally
    {
        listening?.Dispose();

        foreach (var handle in clients)
        {
            handle.Dispose();
        }

        foreach (var handle in servers)
        {
            handle.Dispose();
        }
    }
}

SafeFileHandle? create(string name, uint openMode, out int error)
{
    var sddl = $"D:P(A;;GA;;;{sid})";

    if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out var descriptor, nint.Zero))
    {
        throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    try
    {
        var attributes = new SecurityAttributes
        {
            Length = (uint)Marshal.SizeOf<SecurityAttributes>(),
            SecurityDescriptor = descriptor,
            InheritHandle = 0,
        };

        var handle = CreateNamedPipeW(name, openMode, PipeRejectRemoteClients, maxInstances, 65536, 4096, 0, ref attributes);

        if (handle.IsInvalid)
        {
            error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            return null;
        }

        error = 0;
        return handle;
    }
    finally
    {
        _ = LocalFree(descriptor);
    }
}

[DllImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", SetLastError = true, CharSet = CharSet.Unicode)]
static extern SafeFileHandle CreateNamedPipeW(string name, uint openMode, uint pipeMode, uint maxInstances, uint outBuffer, uint inBuffer, uint defaultTimeout, ref SecurityAttributes attributes);

[DllImport("kernel32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool ConnectNamedPipe(SafeFileHandle pipe, nint overlapped);

[DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

[DllImport("kernel32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool GetNamedPipeClientProcessId(SafeFileHandle pipe, out uint clientProcessId);

[DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", SetLastError = true, CharSet = CharSet.Unicode)]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, out nint descriptor, nint size);

[DllImport("kernel32.dll", SetLastError = true)]
static extern nint LocalFree(nint memory);

[StructLayout(LayoutKind.Sequential)]
internal struct SecurityAttributes
{
    public uint Length;
    public nint SecurityDescriptor;
    public int InheritHandle;
}
