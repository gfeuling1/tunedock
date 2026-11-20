using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using TuneDock.Interop;
using TuneDock.Services;
using TuneDock.ViewModels;

namespace TuneDock.Views;

/// <summary>Always-on-top "now playing" card locked to the bottom-right corner of the primary screen.</summary>
public partial class MiniPlayerWindow : Window
{
    // The Grid in XAML has a 10 DIP margin for the shadow; subtract it so the card itself sits at CornerMargin.
    private const double ShadowMargin = 10;
    private readonly AppSettings _settings;

    public MiniPlayerWindow(PlayerViewModel viewModel, AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        DataContext = viewModel;
        Opacity = settings.IdleOpacity;

        SourceInitialized += (_, _) => WindowHelper.ApplyOverlayStyles(this);
        SizeChanged += (_, _) => PositionInCorner();
        MouseEnter += (_, _) => Opacity = 1.0;
        MouseLeave += (_, _) => Opacity = _settings.IdleOpacity;

        // Re-pin when the taskbar moves/resizes or displays change.
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void ShowOverlay()
    {
        if (!IsVisible) Show();
        PositionInCorner();
        WindowHelper.BringToTop(this);
    }

    public void HideOverlay()
    {
        if (IsVisible) Hide();
    }

    public void PositionInCorner()
    {
        if (!IsLoaded && !IsVisible) return;
        var area = SystemParameters.WorkArea; // primary monitor, excluding the taskbar (in DIPs)
        double inset = _settings.CornerMargin - ShadowMargin;
        Left = area.Right - ActualWidth - inset;
        Top = area.Bottom - ActualHeight - inset;
    }

    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea))
            Dispatcher.BeginInvoke(PositionInCorner);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(PositionInCorner);

    // The overlay never takes focus, so ignore attempts to drag or activate it.
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) => e.Handled = true;

    protected override void OnClosed(EventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        base.OnClosed(e);
    }
}
