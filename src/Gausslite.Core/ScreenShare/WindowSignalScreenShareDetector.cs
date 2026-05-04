// SPDX-License-Identifier: AGPL-3.0-or-later
using Gausslite.Core.Diagnostics;
using Gausslite.Core.WindowTracking;

namespace Gausslite.Core.ScreenShare;

/// <summary>
/// Polls visible top-level windows on a fixed cadence and matches them against
/// <see cref="KnownShareSignatures.All"/>.  Emits <see cref="StateChanged"/> only
/// on transitions — not on every tick.
///
/// Threading: the polling tick fires on whatever thread the injected scheduler
/// uses (in production, a WPF <c>DispatcherTimer</c> bound to the UI thread).
/// All listeners receive <see cref="StateChanged"/> on that same thread.
/// </summary>
public sealed class WindowSignalScreenShareDetector : IScreenShareDetector
{
    /// <summary>Schedules a recurring tick. Returns an <see cref="IDisposable"/> that stops the timer.</summary>
    public delegate IDisposable PollScheduler(TimeSpan interval, Action onTick);

    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Process names whose mere presence (any visible window owned by a process
    /// with this name) is treated as an active share when the user has opted
    /// into the "blur whenever any sharing app is running" heuristic.  The
    /// canonical set is <c>Zoom</c>, <c>ms-teams</c>, <c>Discord</c> — desktop
    /// clients whose share-control UI is unreliable to detect via window
    /// enumeration.  Browsers are intentionally absent: matching on
    /// <c>chrome</c> / <c>msedge</c> would mean blur is on whenever the
    /// browser is open, which defeats the purpose for most users.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultTriggerProcessNames =
        new[] { "Zoom", "ms-teams", "Discord" };

    private readonly IWin32Api _win32;
    private readonly IReadOnlyList<ShareSignature> _signatures;
    private readonly PollScheduler _schedule;
    private readonly TimeSpan _interval;

    // Mutable at runtime so the tray-menu toggle can flip the heuristic on/off
    // without tearing down the detector.  Empty list = heuristic disabled
    // (signature-only matching).  Read once per Poll(); writes from the UI
    // thread are safe because all reads also happen on the UI thread.
    private IReadOnlyList<string> _triggerProcessNames = Array.Empty<string>();

    private IDisposable? _ticker;
    private bool _disposed;

    public ScreenShareState CurrentState { get; private set; } = ScreenShareState.Idle;
    public ActiveShareEvidence? CurrentEvidence { get; private set; }

    public event EventHandler<ScreenShareState>? StateChanged;

    public WindowSignalScreenShareDetector(IWin32Api win32, PollScheduler schedule)
        : this(win32, KnownShareSignatures.All, schedule, DefaultInterval)
    {
    }

    // Test-only ctor — overrides the signature set and/or poll interval.
    internal WindowSignalScreenShareDetector(
        IWin32Api win32,
        IReadOnlyList<ShareSignature> signatures,
        PollScheduler schedule,
        TimeSpan interval)
    {
        _win32 = win32 ?? throw new ArgumentNullException(nameof(win32));
        _signatures = signatures ?? throw new ArgumentNullException(nameof(signatures));
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        _interval = interval;
    }

    /// <summary>
    /// Updates the set of process names to treat as active-share triggers when
    /// the "any sharing app running" heuristic is enabled.  Pass an empty list
    /// to disable.  Safe to call from the same thread that runs the poll
    /// scheduler (the WPF UI thread in production).
    /// </summary>
    public void SetTriggerProcessNames(IReadOnlyList<string> processNames)
    {
        _triggerProcessNames = processNames ?? throw new ArgumentNullException(nameof(processNames));
        DiagLog.Info($"WindowSignalScreenShareDetector: trigger process names = [{string.Join(", ", _triggerProcessNames)}]");
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowSignalScreenShareDetector));
        if (_ticker is not null) return; // idempotent

        DiagLog.Info($"WindowSignalScreenShareDetector.Start: polling every {_interval.TotalMilliseconds:F0} ms against {_signatures.Count} signature(s)");
        _ticker = _schedule(_interval, Poll);
    }

    public void Stop()
    {
        _ticker?.Dispose();
        _ticker = null;
    }

    /// <summary>Runs one poll cycle. Public for tests; production code should rely on the scheduler.</summary>
    internal void Poll()
    {
        if (_disposed) return;

        // Read once per poll — flipping the toggle mid-poll won't produce a torn read.
        var triggers = _triggerProcessNames;
        bool heuristicOn = triggers.Count > 0;

        var windows = _win32.EnumerateVisibleWindows();
        ActiveShareEvidence? match = null;
        foreach (var w in windows)
        {
            // Phase 1 — strict window-signature match.  Highest-quality evidence;
            // never produces false positives.
            foreach (var sig in _signatures)
            {
                if (sig.Matches(w))
                {
                    match = new ActiveShareEvidence(sig.AppName, w.ProcessName, w.ClassName, w.Title, w.Hwnd);
                    break;
                }
            }
            if (match.HasValue) break;

            // Phase 2 — heuristic fallback: any visible window owned by a known
            // sharing process implies "share might be active."  Heavy-handed
            // (Zoom in the tray = blur on); only consulted when the user opts in.
            if (heuristicOn)
            {
                for (int i = 0; i < triggers.Count; i++)
                {
                    if (string.Equals(triggers[i], w.ProcessName, StringComparison.OrdinalIgnoreCase))
                    {
                        match = new ActiveShareEvidence(
                            $"{w.ProcessName} (process heuristic)",
                            w.ProcessName,
                            w.ClassName,
                            w.Title,
                            w.Hwnd);
                        break;
                    }
                }
                if (match.HasValue) break;
            }
        }

        var newState = match.HasValue ? ScreenShareState.Active : ScreenShareState.Idle;
        if (newState == CurrentState)
            return;

        CurrentState = newState;
        CurrentEvidence = match;

        if (match.HasValue)
            // Title intentionally omitted — for the Browser signature it contains the
            // sharing site's domain (e.g. "<host>.com is sharing your screen.").
            // AppName + WindowClass uniquely identify which signature fired.
            DiagLog.Info($"ScreenShare: {match.Value.AppName} active share detected (class={match.Value.WindowClass})");
        else
            DiagLog.Info("ScreenShare: active share ended");

        StateChanged?.Invoke(this, newState);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
