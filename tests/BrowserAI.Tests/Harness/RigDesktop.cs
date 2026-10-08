// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Sessions;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The desktop the in-process rig's sessions read the person's input from: the window
/// in front, its owner, and the two tick counts, as an arm sets them, and the check's
/// timer, which fires when the arm says so.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-10-08 with F4.</b> The product reads the real desktop, whose window in
/// front and last input are the developer's own, so the rig hands every session
/// environment this one instead (<see cref="SessionEnvironment.InputWatch"/>), and no
/// arm ever touches the keyboard or the mouse.
/// </para>
/// <para>
/// <b>A session's window is matched by the double's own browser pid</b>
/// (<see cref="FakePlaywrightChild.BrowserProcessId"/>), a number no real process has.
/// </para>
/// </remarks>
internal sealed class RigDesktop : IInputProbe
{
    private readonly Lock _gate = new();
    private readonly List<Action> _timers = [];

    private int _owner;
    private uint _lastInput;
    private uint _now = 1_000_000;

    /// <summary>The check every session in the rig shares, over this desktop.</summary>
    public VisibleInputWatch Watch { get; }

    /// <summary>Creates the desktop with nothing in front and no input yet.</summary>
    public RigDesktop() =>
        Watch = new VisibleInputWatch(
            this,
            tick =>
            {
                lock (_gate)
                {
                    _timers.Add(tick);
                }

                return new Stop(this, tick);
            },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

    /// <summary>Whether the check's timer is running: a visible session is in it.</summary>
    public bool CheckIsRunning
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count is not 0;
            }
        }
    }

    /// <summary>Puts a window owned by a process in front.</summary>
    /// <param name="owner">The pid that owns it, or zero for no window at all.</param>
    public void InFront(int owner)
    {
        lock (_gate)
        {
            _owner = owner;
        }
    }

    /// <summary>A person typed or clicked, now: the last input is the newest tick.</summary>
    public void PersonTypes()
    {
        lock (_gate)
        {
            _now += 10;
            _lastInput = _now;
            _now += 10;
        }
    }

    /// <summary>Time passes on the desktop's own tick count, with no input.</summary>
    public void TimePasses()
    {
        lock (_gate)
        {
            _now += 1_000;
        }
    }

    /// <summary>Runs one check, the way the timer's next firing does.</summary>
    public void Check()
    {
        Action[] timers;

        lock (_gate)
        {
            timers = [.. _timers];
        }

        foreach (var tick in timers)
        {
            tick();
        }
    }

    /// <inheritdoc />
    public nint ForegroundWindow()
    {
        lock (_gate)
        {
            return _owner is 0 ? nint.Zero : 0x1234;
        }
    }

    /// <inheritdoc />
    public uint WindowThread(nint window, out int processId)
    {
        lock (_gate)
        {
            processId = _owner;
            return _owner is 0 ? 0u : 7u;
        }
    }

    /// <inheritdoc />
    public bool LastInput(out uint tick)
    {
        lock (_gate)
        {
            tick = _lastInput;
            return true;
        }
    }

    /// <inheritdoc />
    public uint TickCount()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    private sealed class Stop(RigDesktop desktop, Action tick) : IDisposable
    {
        public void Dispose()
        {
            lock (desktop._gate)
            {
                _ = desktop._timers.Remove(tick);
            }
        }
    }
}
