using System;
using System.IO;
using System.Text.Json;

namespace TuneDock.Services;

/// <summary>
/// User-editable settings stored at %APPDATA%\TuneDock\settings.json.
/// The file is created with defaults on first launch. Restart TuneDock after editing.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Media sessions whose app ID contains any of these words are preferred.
    /// "brave" matches YouTube Music installed as an app from Brave.
    /// </summary>
    public string[] PreferredSourceKeywords { get; set; } = { "brave" };

    /// <summary>If true, ignore every media source that does not match PreferredSourceKeywords.</summary>
    public bool OnlyPreferredSources { get; set; } = false;

    /// <summary>Gap (in DIPs) between the mini player and the screen corner.</summary>
    public double CornerMargin { get; set; } = 12;

    /// <summary>Mini player opacity when the mouse is not over it (0.2 - 1.0).</summary>
    public double IdleOpacity { get; set; } = 0.9;

    /// <summary>Hide the corner mini player whenever a full-screen game is in front.</summary>
    public bool HideMiniPlayerInGames { get; set; } = true;

    /// <summary>
    /// Processes that are never treated as games, even when full screen.
    /// Browsers are listed so full-screen video keeps the mini player visible.
    /// Remove them if you want full-screen video to behave like a game.
    /// </summary>
    public string[] NotGameProcesses { get; set; } = { "brave", "chrome", "msedge", "firefox", "explorer" };

    /// <summary>
    /// Only open the top-edge panel when the mouse pointer is visible.
    /// Games hide the pointer during gameplay and show it in pause menus, so this
    /// is how TuneDock tells "paused" from "playing". Set to false for games that
    /// draw their own cursor.
    /// </summary>
    public bool RequireVisibleCursor { get; set; } = true;

    /// <summary>How close (in pixels) to the top edge the pointer must be.</summary>
    public int TopEdgeTriggerPixels { get; set; } = 2;

    /// <summary>How long (ms) the pointer must rest at the top edge before the panel drops down.</summary>
    public int TopEdgeDwellMs { get; set; } = 150;

    /// <summary>How long (ms) after the pointer leaves the panel before it slides away.</summary>
    public int TopEdgeHideDelayMs { get; set; } = 400;

    public static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TuneDock");

    public static string FilePath => Path.Combine(FolderPath, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (loaded is not null) return loaded.Normalize();
            }
        }
        catch (Exception)
        {
            // A broken settings file should never stop the app from starting.
        }

        var defaults = new AppSettings();
        defaults.Save();
        return defaults;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
            // Settings are optional; ignore write failures (e.g. read-only profile).
        }
    }

    private AppSettings Normalize()
    {
        PreferredSourceKeywords ??= Array.Empty<string>();
        NotGameProcesses ??= Array.Empty<string>();
        IdleOpacity = Math.Clamp(IdleOpacity, 0.2, 1.0);
        TopEdgeTriggerPixels = Math.Max(1, TopEdgeTriggerPixels);
        TopEdgeDwellMs = Math.Max(0, TopEdgeDwellMs);
        TopEdgeHideDelayMs = Math.Max(0, TopEdgeHideDelayMs);
        return this;
    }
}
