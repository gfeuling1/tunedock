using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using static TuneDock.Interop.NativeMethods;

namespace TuneDock.Services;

/// <summary>One snapshot of what is on screen right now.</summary>
public readonly record struct ForegroundSnapshot(
    bool IsGame,             // a full-screen app (not on the NotGameProcesses list) is in front
    bool IsExclusiveFullscreen, // Direct3D exclusive full screen: overlays cannot draw on top
    IntPtr Monitor,          // monitor the foreground app is on
    RECT MonitorRect,        // that monitor's bounds in physical pixels
    bool CursorVisible,
    POINT Cursor,
    bool IsDesktop = false); // the desktop (or a desktop-widget app) is in front

/// <summary>
/// Decides whether a game is in front by checking if the foreground window covers its
/// whole monitor. Also reports whether the mouse pointer is visible, which is how we
/// guess that a game is in its pause menu.
/// </summary>
public sealed class ForegroundDetector
{
    // Classes of the desktop itself (wallpaper + icons).
    private static readonly HashSet<string> DesktopClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW",
    };

    // Taskbar classes: clicking the taskbar shouldn't change whether we're "on the desktop".
    private static readonly HashSet<string> TaskbarClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    private readonly AppSettings _settings;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private readonly Dictionary<uint, string> _processNames = new();
    private ForegroundSnapshot? _last;

    public ForegroundDetector(AppSettings settings) => _settings = settings;

    public ForegroundSnapshot Sample()
    {
        var cursorInfo = new CURSORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<CURSORINFO>() };
        GetCursorInfo(ref cursorInfo);
        bool cursorVisible = (cursorInfo.flags & CURSOR_SHOWING) != 0;

        var hwnd = GetForegroundWindow();
        GetWindowThreadProcessId(hwnd, out uint pid);

        // When our own overlay is in front (rare, since it never activates), keep the previous answer.
        if (pid == _ownProcessId && _last is { } prev)
            return prev with { CursorVisible = cursorVisible, Cursor = cursorInfo.ptScreenPos };

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref monitorInfo);

        bool exclusive = false;
        if (SHQueryUserNotificationState(out var state) == 0)
            exclusive = state == QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN;

        bool isGame = (exclusive || IsFullscreenWindow(hwnd, monitorInfo.rcMonitor)) && !IsExempt(pid);
        bool isDesktop = !isGame && IsDesktopInFront(hwnd, pid);

        var snapshot = new ForegroundSnapshot(isGame, exclusive && isGame, monitor, monitorInfo.rcMonitor,
            cursorVisible, cursorInfo.ptScreenPos, isDesktop);
        _last = snapshot;
        return snapshot;
    }

    private bool IsDesktopInFront(IntPtr hwnd, uint pid)
    {
        if (hwnd == IntPtr.Zero || hwnd == GetShellWindow()) return true;

        var cls = new StringBuilder(64);
        GetClassName(hwnd, cls, cls.Capacity);
        var name = cls.ToString();
        if (DesktopClasses.Contains(name)) return true;

        // Start menu / taskbar clicks: keep whatever we decided last time to avoid flicker.
        if (TaskbarClasses.Contains(name)) return _last?.IsDesktop ?? false;

        // Clicking a desktop widget (e.g. Rainmeter) still counts as being on the desktop.
        var process = GetProcessName(pid);
        return _settings.DesktopWidgetProcesses.Any(p => string.Equals(p, process, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFullscreenWindow(IntPtr hwnd, RECT monitor)
    {
        if (hwnd == IntPtr.Zero || hwnd == GetDesktopWindow() || hwnd == GetShellWindow()) return false;

        var cls = new StringBuilder(64);
        GetClassName(hwnd, cls, cls.Capacity);
        if (ShellClasses.Contains(cls.ToString())) return false;

        if (!GetWindowRect(hwnd, out var r)) return false;
        bool coversMonitor = r.Left <= monitor.Left && r.Top <= monitor.Top &&
                             r.Right >= monitor.Right && r.Bottom >= monitor.Bottom;
        if (!coversMonitor) return false;

        // A normal maximized window (title bar present) can cover the monitor when the
        // taskbar auto-hides. That is not a game, so skip it.
        long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        bool hasCaption = (style & WS_CAPTION) == WS_CAPTION;
        return !(hasCaption && IsZoomed(hwnd));
    }

    private bool IsExempt(uint pid)
    {
        if (_settings.NotGameProcesses.Length == 0 || pid == 0) return false;
        var name = GetProcessName(pid);
        return _settings.NotGameProcesses.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
    }

    private string GetProcessName(uint pid)
    {
        if (pid == 0) return "";
        if (!_processNames.TryGetValue(pid, out var name))
        {
            try { name = Process.GetProcessById((int)pid).ProcessName; }
            catch (Exception) { name = ""; }
            if (_processNames.Count > 256) _processNames.Clear();
            _processNames[pid] = name;
        }
        return name;
    }
}
