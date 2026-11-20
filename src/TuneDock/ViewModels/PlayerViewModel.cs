using System;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TuneDock.Services;

namespace TuneDock.ViewModels;

/// <summary>
/// Shared state for both the corner mini player and the top-edge game panel.
/// Both views bind to the same instance, so they always show the same thing.
/// </summary>
public sealed class PlayerViewModel : ObservableObject
{
    private const string PlayGlyph = "\uE768";
    private const string PauseGlyph = "\uE769";

    private readonly Dispatcher _dispatcher;
    private readonly YouTubeMusicLauncher _launcher;
    private readonly DispatcherTimer _progressTimer;
    private TimelineState? _timeline;

    private string _title = "Nothing playing";
    private string _artist = "Open YouTube Music in Brave";
    private string _album = "";
    private ImageSource? _albumArt;
    private bool _isPlaying;
    private bool _canPrevious;
    private bool _canPlayPause;
    private bool _canNext;
    private bool _hasTimeline;
    private double _progress;
    private string _positionText = "0:00";
    private string _durationText = "0:00";
    private string? _sourceId;

    public PlayerViewModel(MediaService media, YouTubeMusicLauncher launcher, Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _launcher = launcher;
        OpenAlbumCommand = new RelayCommand(() =>
        {
            if (HasAlbum) _launcher.OpenAlbum(Album, Artist);
        });

        PlayPauseCommand = new AsyncCommand(media.TogglePlayPauseAsync);
        NextCommand = new AsyncCommand(media.NextAsync);
        PreviousCommand = new AsyncCommand(media.PreviousAsync);

        media.SourceChanged += id => OnUi(() => SourceId = id);
        media.TrackChanged += track => OnUi(() => ApplyTrack(track));
        media.PlaybackChanged += state => OnUi(() => ApplyPlayback(state));
        media.TimelineChanged += timeline => OnUi(() => ApplyTimeline(timeline));

        // Windows only reports the position occasionally, so we advance it locally.
        _progressTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => UpdateProgress(), dispatcher);
        _progressTimer.Start();
    }

    public ICommand PlayPauseCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand OpenAlbumCommand { get; }

    /// <summary>Called when the card itself (not a button or link) is clicked.</summary>
    public void OpenYouTubeMusic() => _launcher.OpenApp();

    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public string Artist { get => _artist; private set => SetProperty(ref _artist, value); }

    public string Album
    {
        get => _album;
        private set
        {
            if (SetProperty(ref _album, value)) OnPropertyChanged(nameof(HasAlbum));
        }
    }

    public bool HasAlbum => !string.IsNullOrWhiteSpace(_album);

    public ImageSource? AlbumArt
    {
        get => _albumArt;
        private set
        {
            if (SetProperty(ref _albumArt, value)) OnPropertyChanged(nameof(HasNoAlbumArt));
        }
    }

    public bool HasNoAlbumArt => _albumArt is null;

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (SetProperty(ref _isPlaying, value)) OnPropertyChanged(nameof(PlayPauseGlyph));
        }
    }

    public string PlayPauseGlyph => _isPlaying ? PauseGlyph : PlayGlyph;

    public bool CanPrevious { get => _canPrevious; private set => SetProperty(ref _canPrevious, value); }
    public bool CanPlayPause { get => _canPlayPause; private set => SetProperty(ref _canPlayPause, value); }
    public bool CanNext { get => _canNext; private set => SetProperty(ref _canNext, value); }

    public bool HasTimeline { get => _hasTimeline; private set => SetProperty(ref _hasTimeline, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public string PositionText { get => _positionText; private set => SetProperty(ref _positionText, value); }
    public string DurationText { get => _durationText; private set => SetProperty(ref _durationText, value); }

    /// <summary>The Windows app ID of the media source being followed (handy for troubleshooting).</summary>
    public string? SourceId { get => _sourceId; private set => SetProperty(ref _sourceId, value); }

    public void ShowMessage(string title, string detail)
    {
        Title = title;
        Artist = detail;
        Album = "";
    }

    private void ApplyTrack(TrackInfo? track)
    {
        if (track is null || string.IsNullOrWhiteSpace(track.Title))
        {
            Title = "Nothing playing";
            Artist = "Open YouTube Music in Brave";
            Album = "";
            AlbumArt = null;
            return;
        }
        Title = track.Title;
        Artist = track.Artist;
        Album = track.Album;
        AlbumArt = track.AlbumArt;
    }

    private void ApplyPlayback(PlaybackState state)
    {
        IsPlaying = state.IsPlaying;
        CanPrevious = state.CanPrevious;
        CanPlayPause = state.CanPlayPause;
        CanNext = state.CanNext;
        UpdateProgress();
    }

    private void ApplyTimeline(TimelineState? timeline)
    {
        _timeline = timeline;
        HasTimeline = timeline is not null;
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        if (_timeline is not { } t) return;

        var position = t.Position;
        if (IsPlaying && t.LastUpdated != default)
            position += DateTimeOffset.Now - t.LastUpdated;
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        if (position > t.Duration) position = t.Duration;

        Progress = t.Duration.TotalSeconds > 0 ? position.TotalSeconds / t.Duration.TotalSeconds : 0;
        PositionText = Format(position);
        DurationText = Format(t.Duration);
    }

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private void OnUi(Action action)
    {
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.BeginInvoke(action);
    }
}
