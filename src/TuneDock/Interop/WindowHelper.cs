using System;
using System.Windows;
using System.Windows.Interop;
using static TuneDock.Interop.NativeMethods;

namespace TuneDock.Interop;

/// <summary>Helpers that turn a normal WPF window into a non-intrusive overlay.</summary>
internal static class WindowHelper
{
    public static IntPtr GetHandle(Window window) => new WindowInteropHelper(window).Handle;

    /// <summary>
    /// Hide the window from Alt+Tab and stop it from taking keyboard focus when clicked.
    /// Call from SourceInitialized (once the HWND exists).
    /// </summary>
    public static void ApplyOverlayStyles(Window window)
    {
        var hwnd = GetHandle(window);
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>Push the window back to the top of the "always on top" band without activating it.</summary>
    public static void BringToTop(Window window)
    {
        var hwnd = GetHandle(window);
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Move the window to a position given in physical pixels and keep it topmost.</summary>
    public static void MoveToPixels(Window window, int x, int y)
    {
        var hwnd = GetHandle(window);
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public static RECT GetPixelRect(Window window)
    {
        GetWindowRect(GetHandle(window), out var rect);
        return rect;
    }

    /// <summary>DPI scale factor (1.0 = 100%) of a monitor.</summary>
    public static double GetMonitorScale(IntPtr hMonitor)
    {
        try
        {
            if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                return dpiX / 96.0;
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return 1.0;
    }
}
