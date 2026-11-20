using System;
using System.Windows.Threading;
using TuneDock.Views;

namespace TuneDock.Services;

/// <summary>
/// The "brain" of the overlay. Every 50 ms it checks what is in front and decides:
///   - Desktop / normal apps  -> show the corner mini player and keep it on top.
///   - Full-screen game       -> hide the mini player, arm the top-edge panel.
///   - Game + visible pointer resting at the top edge -> slide the panel down.
/// </summary>
public sealed class OverlayController : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan ReassertTopmostEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReselectSessionEvery = TimeSpan.FromSeconds(3);
    private const int PanelHitMarginPx = 48;

    private readonly MiniPlayerWindow _mini;
    private readonly TopEdgePanelWindow _panel;
    private readonly AppSettings _settings;
    private readonly MediaService _media;
    private readonly ForegroundDetector _detector;
    private readonly DispatcherTimer _timer;

    private bool _gameMode;
    private bool _userHidMini;
    private DateTime _edgeEnteredAt = DateTime.MinValue;
    private DateTime _panelLeftAt = DateTime.MinValue;
    private DateTime _lastTopmost = DateTime.MinValue;
    private DateTime _lastReselect = DateTime.MinValue;

    public OverlayController(MiniPlayerWindow mini, TopEdgePanelWindow panel, AppSettings settings, MediaService media)
    {
        _mini = mini;
        _panel = panel;
        _settings = settings;
        _media = media;
        _detector = new ForegroundDetector(settings);
        _timer = new DispatcherTimer(TickInterval, DispatcherPriority.Background, (_, _) => Tick(), mini.Dispatcher);
    }

    public event Action<bool>? GameModeChanged;

    public bool IsMiniPlayerHiddenByUser => _userHidMini;

    public void Start()
    {
        if (!_userHidMini) _mini.ShowOverlay();
        _timer.Start();
    }

    public void ToggleMiniPlayer()
    {
        _userHidMini = !_userHidMini;
        if (_userHidMini) _mini.HideOverlay();
        else if (!(_gameMode && _settings.HideMiniPlayerInGames)) _mini.ShowOverlay();
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        var snap = _detector.Sample();

        if (snap.IsGame != _gameMode)
        {
            _gameMode = snap.IsGame;
            OnGameModeChanged();
        }

        if (!_gameMode)
        {
            // Other "always on top" windows can cover us; quietly move back to the front
            // (but always underneath the taskbar so it stays clickable).
            if (_mini.IsVisible && now - _lastTopmost > ReassertTopmostEvery)
            {
                _mini.KeepOnTop();
                _lastTopmost = now;
            }
        }
        else if (!snap.IsExclusiveFullscreen)
        {
            UpdateTopEdgePanel(snap, now);
        }

        if (now - _lastReselect > ReselectSessionEvery)
        {
            _media.SelectSession();
            _lastReselect = now;
        }
    }

    private void OnGameModeChanged()
    {
        if (_gameMode)
        {
            if (_settings.HideMiniPlayerInGames) _mini.HideOverlay();
        }
        else
        {
            _panel.HideImmediately();
            if (!_userHidMini) _mini.ShowOverlay();
        }
        _edgeEnteredAt = DateTime.MinValue;
        _panelLeftAt = DateTime.MinValue;
        GameModeChanged?.Invoke(_gameMode);
    }

    private void UpdateTopEdgePanel(ForegroundSnapshot snap, DateTime now)
    {
        bool pointerUsable = snap.CursorVisible || !_settings.RequireVisibleCursor;
        var mon = snap.MonitorRect;

        if (!_panel.IsOpen)
        {
            // Trigger zone: the middle 60% of the game's monitor, within a few pixels of the top.
            bool atTopEdge = snap.Cursor.Y <= mon.Top + _settings.TopEdgeTriggerPixels - 1 &&
                             snap.Cursor.X >= mon.Left + mon.Width * 0.2 &&
                             snap.Cursor.X <= mon.Right - mon.Width * 0.2;

            if (pointerUsable && atTopEdge)
            {
                if (_edgeEnteredAt == DateTime.MinValue) _edgeEnteredAt = now;
                if ((now - _edgeEnteredAt).TotalMilliseconds >= _settings.TopEdgeDwellMs)
                {
                    _panel.SlideIn(snap.Monitor, mon);
                    _edgeEnteredAt = DateTime.MinValue;
                    _panelLeftAt = DateTime.MinValue;
                }
            }
            else
            {
                _edgeEnteredAt = DateTime.MinValue;
            }
            return;
        }

        // Panel is open: close it when the game hides the pointer (gameplay resumed)
        // or when the pointer has been away from the panel for a moment.
        if (!pointerUsable)
        {
            _panel.SlideOut();
            return;
        }

        var hitArea = _panel.PixelBounds.Inflate(PanelHitMarginPx);
        // Include the strip between the top edge and the panel so it doesn't flicker.
        hitArea.Top = Math.Min(hitArea.Top, mon.Top);

        if (hitArea.Contains(snap.Cursor))
        {
            _panelLeftAt = DateTime.MinValue;
        }
        else
        {
            if (_panelLeftAt == DateTime.MinValue) _panelLeftAt = now;
            if ((now - _panelLeftAt).TotalMilliseconds >= _settings.TopEdgeHideDelayMs)
            {
                _panel.SlideOut();
                _panelLeftAt = DateTime.MinValue;
            }
        }
    }

    public void Dispose() => _timer.Stop();
}
