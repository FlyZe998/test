using System;
using System.Diagnostics;
using System.Threading;

namespace KeySpammer;

/// <summary>
/// Fires key presses on a dedicated high-priority thread. Timing is done in
/// raw Stopwatch ticks (not TimeSpan/double-ms) to keep the hot loop cheap,
/// and the process requests 1ms system timer resolution via timeBeginPeriod
/// so Thread.Sleep(1) actually means ~1ms instead of Windows' default
/// ~15.6ms scheduler granularity. The wait ladder in the hot loop backs
/// off Sleep(1) early and finishes the approach with spin-waits, and
/// activation is event-signalled rather than polled. All of this is about
/// tracking the configured KPS more tightly/accurately and with less
/// startup lag -- none of it raises a ceiling above what you set.
/// </summary>
internal sealed class SpamEngine
{
    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _active;
    private volatile int _kps = 10;

    // Signalled the instant the toggle turns on, so the send thread wakes
    // immediately instead of waiting out a poll interval -- this is what
    // removes the startup lag between "toggled on" and "first press sent".
    private readonly ManualResetEventSlim _activeSignal = new(false);

    // Cached input buffer -- rebuilt only when the key changes, not on every
    // press. volatile is required here (not just "atomic reference
    // assignment"): without it, the background send loop has no guarantee
    // of ever observing a new buffer from the UI thread -- the JIT is
    // allowed to cache the field in a register across loop iterations,
    // especially with full optimization forced on in the csproj.
    private volatile Native.INPUT[] _pressBuffer = Native.BuildKeyPress(0x75); // VK_F6 default
    private static readonly int InputSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.INPUT>();
    private static readonly long TickFrequency = Stopwatch.Frequency;

    private long _pressesInWindow;
    private long _sendFailures;

    private const uint TimerPeriodMs = 1;
    private bool _timerPeriodRequested;

    public bool IsActive => _active;

    /// <summary>Fires (actualKps) roughly every 250ms for the UI to read.</summary>
    public event Action<double>? KpsUpdated;

    /// <summary>Fires when SendInput reports 0 events sent (e.g. blocked by UIPI against an elevated foreground window).</summary>
    public event Action<long>? SendFailed;

    public void SetKps(int kps)
    {
        if (kps < 1) kps = 1;
        _kps = kps;
    }

    public void SetKey(ushort vk) => _pressBuffer = Native.BuildKeyPress(vk);

    public void SetActive(bool active)
    {
        _active = active;
        if (active) _activeSignal.Set(); else _activeSignal.Reset();
    }

    public void Toggle()
    {
        _active = !_active;
        if (_active) _activeSignal.Set(); else _activeSignal.Reset();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;

        try
        {
            Native.timeBeginPeriod(TimerPeriodMs);
            _timerPeriodRequested = true;
        }
        catch
        {
            // best-effort -- if winmm isn't available for some reason, the
            // loop still works, just with coarser Sleep(1) resolution
        }

        _thread = new Thread(Run)
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest
        };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(500);

        if (_timerPeriodRequested)
        {
            Native.timeEndPeriod(TimerPeriodMs);
            _timerPeriodRequested = false;
        }
    }

    private void Run()
    {
        try
        {
            Native.SetThreadPriority(Native.GetCurrentThread(), Native.THREAD_PRIORITY_TIME_CRITICAL);
        }
        catch
        {
            // best-effort -- ThreadPriority.Highest (set when the Thread was
            // constructed) still applies even if this native call is denied
        }

        long now = Stopwatch.GetTimestamp();
        long nextTick = now;
        long lastReport = now;
        long reportIntervalTicks = TickFrequency / 4; // 250ms

        while (_running)
        {
            if (!_active)
            {
                // Waits on the signal instead of polling -- Set() in
                // SetActive/Toggle wakes this immediately (sub-ms), so
                // there's no more "up to 5ms of dead air" between toggling
                // on and the first press. The 50ms timeout is just a safety
                // net in case a signal is ever missed; it doesn't add
                // latency to the normal wake path.
                _activeSignal.Wait(50);
                now = Stopwatch.GetTimestamp();
                nextTick = now;
                lastReport = now;
                continue;
            }

            long intervalTicks = TickFrequency / _kps;
            if (intervalTicks < 1) intervalTicks = 1; // guard against kps > TickFrequency (not realistically reachable, but cheap to guard)

            now = Stopwatch.GetTimestamp();

            if (now >= nextTick)
            {
                uint sent = Native.SendInput(2, _pressBuffer, InputSize);
                if (sent == 0)
                {
                    Interlocked.Increment(ref _sendFailures);
                }
                else
                {
                    Interlocked.Increment(ref _pressesInWindow);
                }

                // accumulate target instead of resetting from "now" -- avoids drift
                nextTick += intervalTicks;
                if (now - nextTick > intervalTicks * 4)
                {
                    // fell too far behind (system stall) -- resync instead of burst-catching-up
                    nextTick = now + intervalTicks;
                }
            }
            else
            {
                // Three-tier wait: coarse Sleep(1) while there's plenty of
                // runway (cheap, but Sleep(1) can itself overshoot by
                // ~1-2ms even with timeBeginPeriod(1)), then progressively
                // finer spin-waits as the target gets close. Backing off
                // Sleep(1) earlier (3ms instead of 2ms) and splitting the
                // spin into two tiers tightens how close the actual fire
                // time tracks the configured KPS at high rates -- this is
                // what was previously costing accuracy (and therefore
                // achieved KPS) well below the configured value, not a
                // raised ceiling.
                long remainingTicks = nextTick - now;
                long ticksPerMs = TickFrequency / 1000;

                if (remainingTicks > ticksPerMs * 3)
                {
                    Thread.Sleep(1);
                }
                else if (remainingTicks > ticksPerMs)
                {
                    Thread.SpinWait(50);
                }
                else
                {
                    Thread.SpinWait(10);
                }
            }

            if (now - lastReport >= reportIntervalTicks)
            {
                long count = Interlocked.Exchange(ref _pressesInWindow, 0);
                long failures = Interlocked.Exchange(ref _sendFailures, 0);
                double windowSeconds = (now - lastReport) / (double)TickFrequency;
                double kps = count / windowSeconds;
                lastReport = now;
                KpsUpdated?.Invoke(kps);
                if (failures > 0)
                {
                    SendFailed?.Invoke(failures);
                }
            }
        }
    }
}
