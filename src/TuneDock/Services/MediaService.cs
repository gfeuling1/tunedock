using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace TuneDock.Services;

public sealed record TrackInfo(string Title, string Artist, string Album, BitmapImage? AlbumArt);

public sealed record PlaybackState(bool IsPlaying, bool CanPrevious, bool CanPlayPause, bool CanNext);

public sealed record TimelineState(TimeSpan Position, TimeSpan Duration, DateTimeOffset LastUpdated);

/// <summary>
/// Reads "what's playing" from Windows' Global System Media Transport Controls (the same
/// source the volume flyout uses). Brave reports YouTube Music here, so no browser
/// extension or web automation is needed.
///
/// Events are raised on background threads; subscribers must marshal to the UI thread.
/// </summary>
public sealed class MediaService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly object _gate = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private int _refreshVersion;

    public event Action<string?>? SourceChanged;
    public event Action<TrackInfo?>? TrackChanged;
    public event Action<PlaybackState>? PlaybackChanged;
    public event Action<TimelineState?>? TimelineChanged;

    public MediaService(AppSettings settings) => _settings = settings;

    public async Task InitializeAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.SessionsChanged += (_, _) => SelectSession();
        _manager.CurrentSessionChanged += (_, _) => SelectSession();
        SelectSession();
    }

    // ----- Controls -----------------------------------------------------------------

    public Task TogglePlayPauseAsync() => Run(s => s.TryTogglePlayPauseAsync().AsTask());
    public Task NextAsync() => Run(s => s.TrySkipNextAsync().AsTask());
    public Task PreviousAsync() => Run(s => s.TrySkipPreviousAsync().AsTask());

    private async Task Run(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> action)
    {
        var session = _session;
        if (session is null) return;
        try { await action(session); }
        catch (Exception) { /* the source app may have closed between click and call */ }
    }

    // ----- Session selection --------------------------------------------------------

    /// <summary>Re-check which media session to follow (also called periodically).</summary>
    public void SelectSession()
    {
        var best = PickBestSession();
        bool changed;
        lock (_gate)
        {
            changed = !ReferenceEquals(best, _session);
            if (changed)
            {
                Detach(_session);
                _session = best;
                Attach(best);
            }
        }

        if (!changed) return;
        SourceChanged?.Invoke(best?.SourceAppUserModelId);
        _ = RefreshAllAsync(best);
    }

    private GlobalSystemMediaTransportControlsSession? PickBestSession()
    {
        if (_manager is null) return null;
        try
        {
            var sessions = _manager.GetSessions().ToList();
            var keywords = _settings.PreferredSourceKeywords;

            bool IsPreferred(GlobalSystemMediaTransportControlsSession s) =>
                keywords.Any(k => !string.IsNullOrWhiteSpace(k) &&
                                  (s.SourceAppUserModelId ?? "").Contains(k, StringComparison.OrdinalIgnoreCase));

            var preferred = sessions.Where(IsPreferred).ToList();
            if (preferred.Count > 0)
                return preferred.FirstOrDefault(IsPlaying) ?? preferred[0];

            if (_settings.OnlyPreferredSources) return null;

            return sessions.FirstOrDefault(IsPlaying) ?? _manager.GetCurrentSession() ?? sessions.FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession s)
    {
        try
        {
            return s.GetPlaybackInfo()?.PlaybackStatus ==
                   GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch (Exception) { return false; }
    }

    private void Attach(GlobalSystemMediaTransportControlsSession? s)
    {
        if (s is null) return;
        s.MediaPropertiesChanged += OnMediaPropertiesChanged;
        s.PlaybackInfoChanged += OnPlaybackInfoChanged;
        s.TimelinePropertiesChanged += OnTimelineChanged;
    }

    private void Detach(GlobalSystemMediaTransportControlsSession? s)
    {
        if (s is null) return;
        try
        {
            s.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            s.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            s.TimelinePropertiesChanged -= OnTimelineChanged;
        }
        catch (Exception) { }
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e)
        => _ = RefreshTrackAsync(s);

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e)
        => RefreshPlayback(s);

    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e)
        => RefreshTimeline(s);

    // ----- Refreshing ---------------------------------------------------------------

    private async Task RefreshAllAsync(GlobalSystemMediaTransportControlsSession? s)
    {
        if (s is null)
        {
            TrackChanged?.Invoke(null);
            PlaybackChanged?.Invoke(new PlaybackState(false, false, false, false));
            TimelineChanged?.Invoke(null);
            return;
        }
        RefreshPlayback(s);
        RefreshTimeline(s);
        await RefreshTrackAsync(s);
    }

    private async Task RefreshTrackAsync(GlobalSystemMediaTransportControlsSession s)
    {
        int version = Interlocked.Increment(ref _refreshVersion);
        try
        {
            var props = await s.TryGetMediaPropertiesAsync();
            if (props is null) return;
            var art = await LoadThumbnailAsync(props.Thumbnail);

            // Ignore stale results if a newer refresh started or the session changed.
            if (version != Volatile.Read(ref _refreshVersion) || !ReferenceEquals(s, _session)) return;

            TrackChanged?.Invoke(new TrackInfo(props.Title ?? "", props.Artist ?? "", props.AlbumTitle ?? "", art));
        }
        catch (Exception) { }
    }

    private void RefreshPlayback(GlobalSystemMediaTransportControlsSession s)
    {
        if (!ReferenceEquals(s, _session)) return;
        try
        {
            var info = s.GetPlaybackInfo();
            var c = info.Controls;
            PlaybackChanged?.Invoke(new PlaybackState(
                info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                c.IsPreviousEnabled,
                c.IsPlayPauseToggleEnabled || c.IsPlayEnabled || c.IsPauseEnabled,
                c.IsNextEnabled));
        }
        catch (Exception) { }
    }

    private void RefreshTimeline(GlobalSystemMediaTransportControlsSession s)
    {
        if (!ReferenceEquals(s, _session)) return;
        try
        {
            var t = s.GetTimelineProperties();
            var duration = t.EndTime - t.StartTime;
            TimelineChanged?.Invoke(duration > TimeSpan.Zero
                ? new TimelineState(t.Position - t.StartTime, duration, t.LastUpdatedTime)
                : null);
        }
        catch (Exception) { }
    }

    private static async Task<BitmapImage?> LoadThumbnailAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null) return null;
        try
        {
            using var winStream = await thumbnail.OpenReadAsync();
            using var source = winStream.AsStreamForRead();
            var memory = new MemoryStream();
            await source.CopyToAsync(memory);
            memory.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 128;
            bitmap.StreamSource = memory;
            bitmap.EndInit();
            bitmap.Freeze(); // allows use from the UI thread
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            Detach(_session);
            _session = null;
        }
    }
}
