// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Probe E: does an UNHANDLED exception unwind the stack (run finally / Dispose)
// before the process dies?  And which shapes provably do NOT unwind?
using System.Runtime.CompilerServices;

var mode = args.Length > 0 ? args[0] : "main";

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Console.Out.WriteLine($"OBSERVED: AppDomain.UnhandledException handler ran. IsTerminating={((UnhandledExceptionEventArgs)e).IsTerminating}");
    Console.Out.Flush();
};

switch (mode)
{
    case "catchtest":   // POSITIVE CONTROL: proves the probe can see a finally at all.
        Console.Out.WriteLine("MODE catchtest: POSITIVE CONTROL, same code, caught outside");
        Console.Out.Flush();
        try { Throwing(); } catch (InvalidOperationException) { Console.Out.WriteLine("OBSERVED: outer catch ran"); }
        break;

    case "main":
        Console.Out.WriteLine("MODE main: unhandled out of Main");
        Console.Out.Flush();
        Throwing();
        break;

    case "thread":
        Console.Out.WriteLine("MODE thread: unhandled on a background thread");
        Console.Out.Flush();
        var t = new Thread(Throwing); t.Start(); t.Join();
        break;

    case "failfast":
        Console.Out.WriteLine("MODE failfast: Environment.FailFast under the same try/finally/using");
        Console.Out.Flush();
        FailingFast();
        break;

    case "stackoverflow":
        Console.Out.WriteLine("MODE stackoverflow: StackOverflowException under the same try/finally/using");
        Console.Out.Flush();
        Overflowing();
        break;
}

Console.Out.WriteLine("OBSERVED: reached the end of Main");
Console.Out.Flush();
return 0;

[MethodImpl(MethodImplOptions.NoInlining)]
static void Throwing()
{
    using var d = new Marker("using/Dispose");
    try { try { throw new InvalidOperationException("boom"); } finally { P.Say("inner finally ran"); } }
    finally { P.Say("outer finally ran"); }
}

[MethodImpl(MethodImplOptions.NoInlining)]
static void FailingFast()
{
    using var d = new Marker("using/Dispose");
    try { try { Environment.FailFast("probe: fail fast"); } finally { P.Say("inner finally ran"); } }
    finally { P.Say("outer finally ran"); }
}

[MethodImpl(MethodImplOptions.NoInlining)]
static void Overflowing()
{
    using var d = new Marker("using/Dispose");
    try { try { Recurse(0); } finally { P.Say("inner finally ran"); } }
    finally { P.Say("outer finally ran"); }
}

[MethodImpl(MethodImplOptions.NoInlining)]
static long Recurse(long n) => n + Recurse(n + 1) + Recurse(n + 2);



static class P { public static void Say(string s) { Console.Out.WriteLine("OBSERVED: " + s); Console.Out.Flush(); } }

sealed class Marker(string what) : IDisposable
{
    public void Dispose() => P.Say($"Dispose ran ({what})");
}
