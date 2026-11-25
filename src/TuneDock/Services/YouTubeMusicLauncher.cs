using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;
using static TuneDock.Interop.NativeMethods;

namespace TuneDock.Services;

/// <summary>
/// Brings the YouTube Music app (installed from Brave) to the front, or launches it.
/// </summary>
public sealed class YouTubeMusicLauncher
{
    private const string HomeUrl = "https://music.youtube.com/";
    private readonly AppSettings _settings;

    public YouTubeMusicLauncher(AppSettings settings) => _settings = settings;

    /// <summary>Switch to the open YouTube Music window, or launch the app if it isn't open.</summary>
    public void OpenApp()
    {
        if (TryFocusExistingWindow()) return;
        if (TryLaunchApp(url: null)) return;
        OpenInDefaultBrowser(HomeUrl);
    }

    private static bool TryFocusExistingWindow()
    {
        IntPtr match = IntPtr.Zero;
        var title = new StringBuilder(512);
        uint ownPid = (uint)Environment.ProcessId;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            title.Clear();
            GetWindowText(hwnd, title, title.Capacity);
            if (!title.ToString().Contains("YouTube Music", StringComparison.OrdinalIgnoreCase)) return true;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ownPid) return true;
            try
            {
                if (!string.Equals(Process.GetProcessById((int)pid).ProcessName, "brave", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch (Exception) { return true; }

            match = hwnd;
            return false; // stop enumerating
        }, IntPtr.Zero);

        if (match == IntPtr.Zero) return false;
        if (IsIconic(match)) ShowWindow(match, SW_RESTORE);
        SetForegroundWindow(match);
        return true;
    }

    private bool TryLaunchApp(string? url)
    {
        var brave = FindBrave();
        if (brave is null) return false;

        var args = $"--profile-directory=\"{_settings.BraveProfileDirectory}\" --app-id={_settings.YouTubeMusicAppId}";
        if (url is not null) args += $" --app-launch-url-for-shortcuts-menu-item=\"{url}\"";

        try
        {
            Process.Start(new ProcessStartInfo(brave, args) { UseShellExecute = false });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string? FindBrave()
    {
        if (!string.IsNullOrWhiteSpace(_settings.BraveExePath) && File.Exists(_settings.BraveExePath))
            return _settings.BraveExePath;

        var candidates = new List<string?>
        {
            Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\brave.exe", null, null) as string,
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\brave.exe", null, null) as string,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"BraveSoftware\Brave-Browser\Application\brave.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"BraveSoftware\Brave-Browser\Application\brave.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"BraveSoftware\Brave-Browser\Application\brave.exe"),
        };

        foreach (var path in candidates)
        {
            var clean = path?.Trim('"');
            if (!string.IsNullOrEmpty(clean) && File.Exists(clean)) return clean;
        }
        return null;
    }

    private static void OpenInDefaultBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { }
    }
}
