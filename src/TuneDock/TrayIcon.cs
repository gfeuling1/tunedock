using System;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace TuneDock;

/// <summary>System tray icon: the only way to hide the mini player or quit, since the overlay never takes focus.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public TrayIcon(Action toggleMiniPlayer, Action openSettings, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show / hide mini player", null, (_, _) => toggleMiniPlayer());
        menu.Items.Add("Open settings file", null, (_, _) => openSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit TuneDock", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Application,
            Text = "TuneDock",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => toggleMiniPlayer();
    }

    public void SetStatus(string text)
    {
        // NotifyIcon tooltips are limited in length.
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
