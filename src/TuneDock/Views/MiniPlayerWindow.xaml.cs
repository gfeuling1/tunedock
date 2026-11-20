using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using TuneDock.Interop;
using TuneDock.Services;
using TuneDock.ViewModels;

namespace TuneDock.Views;

/// <summary>
/// Always-on-top "now playing" card. Starts in the bottom-right corner; drag it anywhere
/// and it remembers the spot. Click it (without dragging) to open YouTube Music.
/// </summary>
public partial class MiniPlayerWindow : Window
{
    // The Grid in XAML has a 10 DIP margin for the shadow; subtract it so the card itself sits at CornerMargin.
    private const double ShadowMargin = 10;
    private const double ClickTolerance = 4; // DIPs the card may move and still count as a click

    private readonly AppSettings _settings;
    private readonly PlayerViewModel _viewModel;

    public MiniPlayerWindow(PlayerViewModel viewModel, AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _viewModel = viewModel;
        DataContext = viewModel;
        Opacity = settings.IdleOpacity;

        SourceInitialized += (_, _) => WindowHelper.ApplyOverlayStyles(this);
        SizeChanged += (_, _) => ApplyPosition();
        MouseEnter += (_, _) => Opacity = 1.0;
        MouseLeave += (_, _) => Opacity = _settings.IdleOpacity;

        // Re-check placement when the taskbar moves/resizes or displays change.
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private bool HasCustomPosition => _settings.MiniPlayerLeft.HasValue && _settings.MiniPlayerTop.HasValue;

    public void ShowOverlay()
    {
        if (!IsVisible) Show();
        ApplyPosition();
        KeepOnTop();
    }

    public void HideOverlay()
    {
        if (IsVisible) Hide();
    }

    /// <summary>Topmost, but tucked under the taskbar so the taskbar stays usable.</summary>
    public void KeepOnTop() => WindowHelper.PlaceBelowTaskbars(this);

    /// <summary>Forget the dragged position and go back to the bottom-right corner.</summary>
    public void ResetPosition()
    {
        _settings.MiniPlayerLeft = null;
        _settings.MiniPlayerTop = null;
        _settings.Save();
        ApplyPosition();
    }

    private void ApplyPosition()
    {
        if (!IsLoaded && !IsVisible) return;

        if (HasCustomPosition && IsOnScreen(_settings.MiniPlayerLeft!.Value, _settings.MiniPlayerTop!.Value))
        {
            Left = _settings.MiniPlayerLeft.Value;
            Top = _settings.MiniPlayerTop.Value;
            return;
        }

        var area = SystemParameters.WorkArea; // primary monitor, excluding the taskbar (in DIPs)
        double inset = _settings.CornerMargin - ShadowMargin;
        Left = area.Right - ActualWidth - inset;
        Top = area.Bottom - ActualHeight - inset;
    }

    /// <summary>At least part of the card must be on a connected monitor (e.g. after unplugging one).</summary>
    private bool IsOnScreen(double left, double top)
    {
        const double minVisible = 40;
        double vsLeft = SystemParameters.VirtualScreenLeft;
        double vsTop = SystemParameters.VirtualScreenTop;
        double vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
        double vsBottom = vsTop + SystemParameters.VirtualScreenHeight;
        return left + ActualWidth - minVisible > vsLeft && left + minVisible < vsRight &&
               top + ActualHeight - minVisible > vsTop && top + minVisible < vsBottom;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.Handled || InputHelper.IsFromInteractiveElement(e.OriginalSource)) return;
        e.Handled = true;

        double startLeft = Left, startTop = Top;
        try
        {
            DragMove(); // returns when the mouse button is released
        }
        catch (InvalidOperationException)
        {
            return;
        }

        bool moved = Math.Abs(Left - startLeft) > ClickTolerance || Math.Abs(Top - startTop) > ClickTolerance;
        if (moved)
        {
            _settings.MiniPlayerLeft = Math.Round(Left);
            _settings.MiniPlayerTop = Math.Round(Top);
            _settings.Save();
        }
        else
        {
            Left = startLeft;
            Top = startTop;
            _viewModel.OpenYouTubeMusic();
        }
        KeepOnTop();
    }

    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea))
            Dispatcher.BeginInvoke(ApplyPosition);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(ApplyPosition);

    protected override void OnClosed(EventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        base.OnClosed(e);
    }
}
