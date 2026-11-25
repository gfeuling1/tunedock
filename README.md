# TuneDock

A small Windows overlay that shows what's playing in **YouTube Music (installed as an app from Brave)** and gives you previous / play-pause / next controls.

- **Desktop:** a compact "now playing" card starts in the **bottom-right corner** and stays on top of every window. **Drag it** anywhere and it remembers the spot. The taskbar always stays above it, so it never blocks the Start button or tray icons.
- **On the desktop:** the card hides while you're looking at the desktop (Win+D or clicking the wallpaper), so it doesn't double up with a desktop music widget. It comes back as soon as you switch to a window.
- **Click the card** to jump to YouTube Music.
- **Games:** when a full-screen game is in front, the card gets out of the way. While the game is paused, push the mouse to the **top edge** of the screen and a panel **slides down** with the same info and controls, similar to the Windows 11 Snap Layouts bar.

Built with C# / WPF on .NET 10 (LTS, supported until November 2028).

## How it works

| Piece | File | What it does |
|---|---|---|
| Media reader | `Services/MediaService.cs` | Uses Windows' built-in media controls API (`GlobalSystemMediaTransportControlsSessionManager`), the same source as the volume flyout. Brave reports YouTube Music there, so no extension or web automation is needed. Sessions whose app ID contains `brave` are preferred. |
| Shared state | `ViewModels/PlayerViewModel.cs` | Title, artist, album art, play state, progress. Both windows bind to it. |
| Card UI | `Views/PlayerView.xaml` | The reusable "now playing" card. |
| Corner player | `Views/MiniPlayerWindow.xaml(.cs)` | Borderless, transparent, always-on-top window pinned above the taskbar in the bottom-right corner. |
| Game panel | `Views/TopEdgePanelWindow.xaml(.cs)` | Pull-down card at the top-center of the game's monitor, with a slide animation. |
| Game detection | `Services/ForegroundDetector.cs` | Treats the foreground window as a game when it covers its whole monitor (and isn't a browser / Explorer). Also checks if the mouse pointer is visible. |
| Orchestration | `Services/OverlayController.cs` | 50 ms loop that switches between desktop mode and game mode and opens/closes the top panel. |
| Win32 glue | `Interop/*.cs` | Foreground window, monitor bounds, cursor info, and "never steal focus" window styles. |
| App launcher | `Services/YouTubeMusicLauncher.cs` | Focuses the open YouTube Music window, or launches the Brave app (`brave.exe --app-id=...`). |
| Tray icon | `TrayIcon.cs` | Show/hide the mini player, move it back to the corner, open settings, exit. |

### Overlay behavior

- **Click vs. drag:** pressing on the card and moving it more than a few pixels drags it; a press without moving counts as a click and opens YouTube Music. The buttons do their own thing and never drag.
- **Taskbar on top:** TuneDock keeps itself in the "always on top" layer but directly *underneath* the taskbar, so if you drag the card over the taskbar, the taskbar wins.
- Both overlay windows use `WS_EX_NOACTIVATE` + `WS_EX_TOOLWINDOW`. **Clicking them never takes focus from your game** and they don't appear in Alt+Tab.
- **Detecting "paused":** there is no universal way to read a game's pause state. TuneDock uses a reliable proxy instead: games hide the mouse pointer during gameplay and show it in menus. The panel only opens when the pointer is **visible**, has rested at the top edge for ~150 ms, and is in the middle 60% of the screen. It closes when the pointer moves away or the game hides the pointer again.
- **Exclusive full-screen games:** in true exclusive full screen, the game owns the display and no normal window can draw over it. TuneDock still hides the corner player, but the top panel can't appear. **Set those games to "Borderless" / "Windowed Fullscreen"** (most modern games default to this). TuneDock deliberately doesn't inject into games the way Discord/Steam overlays do, since that can trigger anti-cheat.

## Requirements

- Windows 10 (version 2004 / build 19041) or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build (or just the .NET 10 Desktop Runtime to run a published build)
- YouTube Music installed from Brave (open music.youtube.com in Brave, then Menu → Install YouTube Music)

## Build and run

```powershell
git clone https://github.com/gfeuling1/tunedock.git
cd tunedock
dotnet run --project src/TuneDock
```

Or open `TuneDock.sln` in Visual Studio 2026 and press F5.

Every push also builds on GitHub Actions; the **Actions** tab has a downloadable `TuneDock-win-x64` artifact with a ready-to-run `TuneDock.exe`.

### Start with Windows (optional)

Press `Win + R`, run `shell:startup`, and put a shortcut to `TuneDock.exe` in that folder.

## Settings

On first launch TuneDock writes `%APPDATA%\TuneDock\settings.json` (tray icon → **Open settings file**). Restart TuneDock after editing.

| Setting | Default | Meaning |
|---|---|---|
| `PreferredSourceKeywords` | `["brave"]` | Media sources whose app ID contains one of these are followed first. |
| `OnlyPreferredSources` | `false` | Ignore Spotify, other browsers, etc. entirely. |
| `CornerMargin` | `12` | Gap between the card and the screen corner. |
| `IdleOpacity` | `0.9` | Card opacity when the mouse isn't over it. |
| `HideMiniPlayerInGames` | `true` | Hide the corner card while a game is full screen. |
| `HideMiniPlayerOnDesktop` | `true` | Hide the corner card while the desktop is in front. |
| `DesktopWidgetProcesses` | `["Rainmeter"]` | Widget apps that still count as "on the desktop" when you click them. Add yours (process name from Task Manager → Details, without `.exe`). |
| `NotGameProcesses` | browsers + Explorer | Full-screen apps that should **not** count as games (so full-screen video keeps the card). Remove `brave` if you'd rather it hide during full-screen video. |
| `MiniPlayerLeft` / `MiniPlayerTop` | `null` | Saved drag position. `null` means the bottom-right corner (tray → **Move mini player back to corner** resets it). |
| `YouTubeMusicAppId` | `cinhimbnkkaeohfgghhklpknlkffjgod` | ID of the YouTube Music app installed in Brave. |
| `BraveProfileDirectory` | `Default` | Brave profile the app is installed in (`Profile 1`, etc. if you use multiple profiles). |
| `BraveExePath` | `null` | Path to `brave.exe`; found automatically when `null`. |
| `RequireVisibleCursor` | `true` | Only open the top panel when the pointer is visible. Set `false` for games that draw their own cursor. |
| `TopEdgeTriggerPixels` | `2` | How close to the top edge the pointer must be. |
| `TopEdgeDwellMs` | `150` | How long to rest at the edge before the panel opens. |
| `TopEdgeHideDelayMs` | `400` | How long after leaving the panel before it closes. |

## Troubleshooting

- **"Nothing playing" while music plays:** confirm the song shows in the Windows volume flyout (`Win + A` on Windows 11). If it does, hover the tray icon; if another app is being followed, set `OnlyPreferredSources` to `true`.
- **Top panel never appears in a game:** switch the game to borderless/windowed fullscreen. If the game uses its own cursor, set `RequireVisibleCursor` to `false`.
- **Clicking the card opens the wrong thing (or nothing):** open `brave://apps` in Brave, right-click YouTube Music → **App info**, and compare the ID with `YouTubeMusicAppId`. If you installed it in a different Brave profile, set `BraveProfileDirectory`.
- **Card hides when it shouldn't:** add that app's process name (as shown in Task Manager → Details, without `.exe`) to `NotGameProcesses`.

## Roadmap ideas

- Settings window instead of a JSON file
- Custom tray icon / app icon
- Seek by clicking the progress bar
- Volume control and like/dislike (would need a Brave extension, since Windows' media API doesn't expose those)
- Snap the mini player to screen edges/corners while dragging
