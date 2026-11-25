using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TuneDock.Interop;
using TuneDock.ViewModels;
using static TuneDock.Interop.NativeMethods;

namespace TuneDock.Views;

/// <summary>
/// Pull-down panel shown at the top-center of a full-screen game's monitor when the
/// pointer rests on the top edge (typically while the game is paused).
/// </summary>
public partial class TopEdgePanelWindow : Window
{
    private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(180));

    private readonly PlayerViewModel _viewModel;

    public TopEdgePanelWindow(PlayerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        SourceInitialized += (_, _) => WindowHelper.ApplyOverlayStyles(this);
    }

    /// <summary>Clicking the card (not a button) switches to YouTube Music.</summary>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (e.Handled || InputHelper.IsFromInteractiveElement(e.OriginalSource)) return;
        e.Handled = true;
        _viewModel.OpenYouTubeMusic();
    }

    /// <summary>True while the panel is open or opening.</summary>
    public bool IsOpen { get; private set; }

    public void SlideIn(IntPtr monitor, RECT monitorRect)
    {
        if (IsOpen) return;
        IsOpen = true;

        bool firstShow = !IsVisible;
        if (firstShow)
        {
            Show();
            UpdateLayout();
            SlideTransform.Y = -(ActualHeight + 20);
        }

        // Center horizontally on the game's monitor, flush with its top edge (physical pixels).
        double scale = WindowHelper.GetMonitorScale(monitor);
        int widthPx = (int)Math.Round(ActualWidth * scale);
        int x = monitorRect.Left + (monitorRect.Width - widthPx) / 2;
        WindowHelper.MoveToPixels(this, x, monitorRect.Top);

        Animate(0, onDone: null);
    }

    public void SlideOut()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Animate(-(ActualHeight + 20), onDone: () =>
        {
            if (!IsOpen) Hide();
        });
    }

    public void HideImmediately()
    {
        IsOpen = false;
        SlideTransform.BeginAnimation(TranslateTransform.YProperty, null);
        SlideTransform.Y = -(ActualHeight + 20);
        if (IsVisible) Hide();
    }

    /// <summary>Current window bounds in physical pixels (for hit-testing the mouse).</summary>
    public RECT PixelBounds => WindowHelper.GetPixelRect(this);

    private void Animate(double to, Action? onDone)
    {
        var animation = new DoubleAnimation(to, SlideDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        if (onDone is not null) animation.Completed += (_, _) => onDone();
        SlideTransform.BeginAnimation(TranslateTransform.YProperty, animation);
    }
}
