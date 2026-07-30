# KeySpammer (C# / WPF)

WPF only builds on Windows, so this can't be compiled in this sandbox —
build it on your own machine with the .NET 8 SDK installed.

## Run while developing
```
dotnet run
```
(The csproj forces `Optimize=true` even under `dotnet run`'s default Debug
config, so this now gets JIT-optimized code without needing `-c Release`
— Debug normally disables optimization entirely, which matters a lot for
a tight timing loop.)

## Build a single self-contained .exe (like your PyInstaller output)
```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
Output lands in `bin\Release\net8.0-windows\win-x64\publish\KeySpammer.exe`.
No .NET runtime needs to be installed on the target machine.

## What's in here
- **Native.cs** — SendInput / low-level mouse hook P/Invoke bindings.
- **SpamEngine.cs** — the actual timing loop. Uses a Stopwatch + spin-wait
  instead of Thread.Sleep, because Sleep's ~15.6ms default resolution caps
  you well under 1000 KPS no matter what number you type in — this is the
  part that actually removes the ceiling.
- **MainWindow.xaml / .xaml.cs** — dark borderless overlay GUI: key
  dropdown, editable KPS field, READY/ACTIVE status, live KPS counter,
  ON/OFF button. XButton1 (mouse button 4) toggles it globally via a
  low-level mouse hook, same as the Python version.

## Notes
- Thread priority is set to Highest on the send loop — if you're pushing
  very high KPS (thousands+) and the UI feels sluggish, that's expected;
  the send thread is intentionally starving lower-priority work. Process
  priority is also bumped to High on startup (best-effort, ignored if it
  fails rather than crashing).
- `SendInput` now populates the real hardware scan code (via `MapVirtualKeyW`)
  and sets `KEYEVENTF_SCANCODE`, not just the virtual-key code. A lot of
  games specifically read the scan code, or use its presence as a
  real-hardware signal, and silently ignore VK-only synthetic input even
  though `SendInput` itself reports success. If a key still does nothing
  in-game after this, that's more likely genuine raw-input/anti-cheat
  handling that bypasses the Windows message queue entirely — not
  something to route around here.
- If the target window is running elevated and this app isn't, Windows
  (UIPI) silently blocks the synthetic input — SendInput returns 0. The
  status text now shows "BLOCKED (run as admin?)" when that happens
  instead of silently doing nothing.

## Review pass (post-first-draft fixes)
- Timing loop was pure spin-wait even at low KPS, pegging a CPU core at
  100% for no reason — now sleeps down to ~2ms before the deadline and
  only spins for the final approach.
- The key-press INPUT buffer was being rebuilt and allocated on every
  single send — now cached and only rebuilt when the key selection
  changes.
- `WindowStyle="None"` removed all window chrome with no close button
  and no keyboard shortcut, so there was no way to close the app short
  of Task Manager — added a close (✕) button and Escape-to-close.
- `SendInput`'s return value was never checked, so a blocked send (e.g.
  target window running elevated) failed completely silently — now
  surfaced in the status text.

## Speed pass
"Faster KPS" here means tracking the configured rate more tightly, not
raising a ceiling — these all reduce overshoot/latency, not "more":
- `TieredCompilationQuickJit=false` + `Optimize=true` in the csproj — the
  biggest single win. `dotnet run` normally starts on Debug's unoptimized
  JIT tier; for a loop this tight, that's a much bigger factor than
  anything below it.
- `timeBeginPeriod(1)` on start (`timeEndPeriod(1)` on stop) — Windows
  defaults to ~15.6ms scheduler granularity, so `Thread.Sleep(1)` could
  actually sleep up to 15x longer than asked. This makes the hybrid
  sleep-then-spin approach in the loop meaningfully tighter.
- Timing loop switched from `Stopwatch.Elapsed.TotalMilliseconds`
  (builds a `TimeSpan`, does floating-point division) to raw
  `Stopwatch.GetTimestamp()` ticks — cheaper per iteration, so the loop
  spends more of its time actually checking/sending and less on
  bookkeeping.
- Send thread now requests `THREAD_PRIORITY_TIME_CRITICAL` directly via
  `SetThreadPriority` — one step above what the managed
  `ThreadPriority.Highest` enum can express.
- `ServerGarbageCollection` + non-concurrent GC in the csproj — minor at
  this point, since the loop was already made allocation-free in the
  earlier review pass (cached press buffer). Not the reason things were
  slow; included for completeness.

**What I didn't do:** pinning the press buffer permanently and calling
`SendInput` through a raw pointer instead of a managed array. It would
save the array-pinning step .NET already does automatically for blittable
arrays like this one — a few nanoseconds per call, dwarfed by the actual
`SendInput` kernel-mode transition (microseconds). Not worth the added
complexity/risk (a torn read on the shared buffer if the key changes
mid-send) for a change that wouldn't be measurable.

## Second review pass
- `_pressBuffer` (the cached key-press struct array) wasn't marked
  `volatile`. Reference assignment across threads is atomic in .NET, but
  atomicity isn't visibility — without `volatile`, the background send
  thread had no guarantee of ever observing a key change from the UI
  thread; the JIT is legally allowed to cache the field in a register
  across loop iterations, and that risk went up specifically because of
  the speed-pass change to force fully-optimized JIT from the start
  (`TieredCompilationQuickJit=false`) — optimized code is exactly what's
  allowed to hoist a non-volatile read out of a loop. Fixed.

## Third review pass — tighter tracking + persisted config
- Idle wait was `Thread.Sleep(5)` polling for `_active` to flip true — up
  to 5ms of dead air between toggling on (button or XButton1) and the
  first press. Replaced with a `ManualResetEventSlim` that `SetActive`/
  `Toggle` signal directly, so the send thread wakes essentially
  immediately instead of waiting out a poll interval. (50ms `Wait`
  timeout kept only as a safety net, not part of the normal wake path.)
- The near-deadline wait was a flat "`Sleep(1)` above 2ms, spin below"
  split. `Sleep(1)` can itself oversleep by ~1-2ms even with
  `timeBeginPeriod(1)`, which was costing accuracy (and therefore
  achieved KPS) at high configured rates — not from a ceiling, but from
  overshooting past each tick's deadline before firing. Backed the
  `Sleep(1)` cutoff off to 3ms and split the remainder into two
  spin-wait tiers (50 iterations, then 10) for a closer final approach.
  Still bounded by `now >= nextTick`, so it tracks the configured KPS
  more tightly without ever exceeding it.
- Added `Settings.cs` — last-used KPS and key are now persisted to
  `%AppData%\KeySpammer\settings.json` and restored on startup, instead
  of always resetting to F6 / 10 KPS. Save is best-effort (a locked-down
  AppData won't crash the app) and Load falls back to defaults on any
  missing/corrupt file, including a saved key name that no longer
  exists in `KeyMap`.
