using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using TuneDock.Services;
using TuneDock.ViewModels;
using TuneDock.Views;

namespace TuneDock;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private MediaService? _media;
    private OverlayController? _controller;
    private TrayIcon? _tray;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Only one TuneDock at a time.
        _singleInstance = new Mutex(true, "TuneDock.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        var settings = AppSettings.Load();
        _media = new MediaService(settings);
        var launcher = new YouTubeMusicLauncher(settings);
        var viewModel = new PlayerViewModel(_media, launcher, Dispatcher);

        var mini = new MiniPlayerWindow(viewModel, settings);
        var panel = new TopEdgePanelWindow(viewModel);

        _controller = new OverlayController(mini, panel, settings, _media);
        _tray = new TrayIcon(
            toggleMiniPlayer: () => _controller.ToggleMiniPlayer(),
            resetPosition: mini.ResetPosition,
            openSettings: OpenSettingsFile,
            exit: ExitApp);

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(PlayerViewModel.Title) or nameof(PlayerViewModel.Artist))
                _tray?.SetStatus($"TuneDock - {viewModel.Title}");
        };

        _controller.Start();

        try
        {
            await _media.InitializeAsync();
        }
        catch (Exception ex)
        {
            viewModel.ShowMessage("Media controls unavailable", ex.Message);
        }
    }

    private static void OpenSettingsFile()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppSettings.FilePath) { UseShellExecute = true });
        }
        catch (Exception)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", AppSettings.FolderPath) { UseShellExecute = true });
        }
    }

    private void ExitApp()
    {
        _controller?.Dispose();
        _media?.Dispose();
        _tray?.Dispose();
        _tray = null;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
