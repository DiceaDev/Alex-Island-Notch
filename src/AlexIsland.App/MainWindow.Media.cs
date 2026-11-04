using AlexIsland.App.Native;
using AlexIsland.App.Services;
using AlexIsland.Core.Models;
using AlexIsland.Core.Security;
using AlexIsland.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using Windows.Media.Control;
using WinRT.Interop;
using System.Globalization;

namespace AlexIsland.App;

public sealed partial class MainWindow : UserControl
{
    // ==============================================================
    // SECCIÓN MULTIMEDIA
    // ==============================================================

    private async Task InitializeMediaAsync()
    {
        try
        {
            _mediaManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _mediaManager.CurrentSessionChanged += MediaManager_CurrentSessionChanged;
            await AttachCurrentSessionAsync();
        }
        catch
        {
            SetNoMediaSession();
        }
    }

    private void MediaManager_CurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        if (_isDisposed) return;
        DispatcherQueue.TryEnqueue(() => _ = AttachCurrentSessionAsync());
    }

    private async Task AttachCurrentSessionAsync()
    {
        if (_isDisposed || _mediaManager is null) return;
        var next = _mediaManager.GetCurrentSession();
        if (!ReferenceEquals(next, _mediaSession))
        {
            DetachMediaSession();
            _mediaSession = next;
            if (_mediaSession is not null)
            {
                _mediaSession.MediaPropertiesChanged += MediaSession_MediaPropertiesChanged;
                _mediaSession.PlaybackInfoChanged += MediaSession_PlaybackInfoChanged;
                _mediaSession.TimelinePropertiesChanged += MediaSession_TimelinePropertiesChanged;
            }
        }
        await RefreshMediaAsync();
    }

    private void DetachMediaSession()
    {
        if (_mediaSession is null) return;
        _mediaSession.MediaPropertiesChanged -= MediaSession_MediaPropertiesChanged;
        _mediaSession.PlaybackInfoChanged -= MediaSession_PlaybackInfoChanged;
        _mediaSession.TimelinePropertiesChanged -= MediaSession_TimelinePropertiesChanged;
        _mediaSession = null;
    }

    private void MediaSession_MediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args)
    {
        if (!_isDisposed)
            DispatcherQueue.TryEnqueue(() => _ = RefreshMediaAsync());
    }

    private void MediaSession_PlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args)
    {
        if (!_isDisposed)
            _ = DispatcherQueue.TryEnqueue(UpdatePlaybackState);
    }

    private void MediaSession_TimelinePropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs args)
    {
        if (!_isDisposed)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                SyncTimelineSnapshot();
                UpdateTimeline();
            });
        }
    }

    private async Task RefreshMediaAsync()
    {
        if (_mediaSession is null)
        {
            SetNoMediaSession();
            return;
        }

        try
        {
            var properties = await _mediaSession.TryGetMediaPropertiesAsync();
            TrackTitle.Text = string.IsNullOrWhiteSpace(properties.Title)
                ? "Reproducción multimedia"
                : properties.Title;
            TrackArtist.Text = string.IsNullOrWhiteSpace(properties.Artist)
                ? _mediaSession.SourceAppUserModelId
                : properties.Artist;

            CompactTrackTitle.Text = TrackTitle.Text;
            CompactTrackArtist.Text = TrackArtist.Text;
            OnTrackInfoChanged(properties.Title, properties.Artist);
            UpdateCompactCenter();
            AlbumPlaceholder.Visibility = Visibility.Visible;
            AlbumArt.Source = null;

            if (properties.Thumbnail is not null)
            {
                using var stream = await properties.Thumbnail.OpenReadAsync();
                if (stream.Size > 0)
                {
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(stream);
                    AlbumArt.Source = bitmap;
                    AlbumPlaceholder.Visibility = Visibility.Collapsed;
                }
            }

            UpdatePlaybackState();
            SyncTimelineSnapshot();
            UpdateTimeline();
        }
        catch
        {
            SetNoMediaSession();
        }
    }

    private void SetNoMediaSession()
    {
        TrackTitle.Text = "No hay reproducción activa";
        TrackArtist.Text = "Inicia música en Spotify o en tu navegador";
        CompactTrackTitle.Text = "No hay reproducción activa";
        CompactTrackArtist.Text = "Windows Media Session";
        OnTrackInfoChanged(null, null);
        AlbumArt.Source = null;
        AlbumPlaceholder.Visibility = Visibility.Visible;
        PlayPauseButton.Content = "\uE768";
        ElapsedText.Text = "00:00";
        RemainingText.Text = "--:--";
        ProgressFill.Width = 0;
        _isPlaying = false;
        _basePosition = TimeSpan.Zero;
        _duration = TimeSpan.Zero;
        _lastSmtcPosition = TimeSpan.Zero;
        _lastPositionSyncTime = DateTimeOffset.UtcNow;
        _waveformTimer.Stop();
        ResetWaveform();
        UpdateCompactCenter();
        ApplyIslandState();
    }

    private void UpdatePlaybackState()
    {
        if (_mediaSession is null)
        {
            _isPlaying = false;
            _waveformTimer.Stop();
            ResetWaveform();
            UpdateCompactCenter();
            return;
        }

        var playback = _mediaSession.GetPlaybackInfo();
        var wasPlaying = _isPlaying;
        _isPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        PlayPauseButton.Content = _isPlaying ? "\uE769" : "\uE768";

        if (wasPlaying != _isPlaying)
        {
            if (_isPlaying)
            {
                _lastPositionSyncTime = DateTimeOffset.UtcNow;
                _waveformTimer.Start();
            }
            else
            {
                _basePosition = GetCurrentPosition();
                _lastPositionSyncTime = DateTimeOffset.UtcNow;
                _waveformTimer.Stop();
                ResetWaveform();
            }
            UpdateCompactCenter();
            ApplyIslandState();
        }

        SyncTimelineSnapshot();
        UpdateTimeline();
    }

    private void SyncTimelineSnapshot()
    {
        if (_mediaSession is null) return;
        try
        {
            var timeline = _mediaSession.GetTimelineProperties();
            var duration = timeline.EndTime - timeline.StartTime;
            if (duration <= TimeSpan.Zero) duration = timeline.EndTime;
            if (duration > TimeSpan.Zero) _duration = duration;

            var newPos = timeline.Position;
            if (newPos != _lastSmtcPosition || _lastPositionSyncTime == DateTimeOffset.MinValue)
            {
                _lastSmtcPosition = newPos;
                _basePosition = newPos;
                if (timeline.LastUpdatedTime > DateTimeOffset.MinValue && timeline.LastUpdatedTime <= DateTimeOffset.Now.AddSeconds(5))
                {
                    _lastPositionSyncTime = timeline.LastUpdatedTime;
                }
                else
                {
                    _lastPositionSyncTime = DateTimeOffset.UtcNow;
                }
            }
        }
        catch
        {
        }
    }

    private TimeSpan GetCurrentPosition()
    {
        if (!_isPlaying || _duration <= TimeSpan.Zero)
        {
            return _basePosition;
        }

        var elapsed = DateTimeOffset.UtcNow - _lastPositionSyncTime.ToUniversalTime();
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        var current = _basePosition + elapsed;
        if (_duration > TimeSpan.Zero && current > _duration)
        {
            current = _duration;
        }
        return current;
    }

    private void UpdateTimeline()
    {
        if (_mediaSession is null) return;
        try
        {
            SyncTimelineSnapshot();

            var currentPosition = GetCurrentPosition();
            ElapsedText.Text = FormatTime(currentPosition);

            if (_duration > TimeSpan.Zero)
            {
                var remaining = _duration - currentPosition;
                if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
                RemainingText.Text = "-" + FormatTime(remaining);

                var ratio = Math.Clamp(currentPosition.TotalSeconds / _duration.TotalSeconds, 0, 1);
                var trackWidth = ProgressBarTrack.ActualWidth;
                ProgressFill.Width = Math.Max(0, trackWidth) * ratio;

                if (_pomodoroState != PomodoroState.Running)
                {
                    UpdateCompactProgress(ratio, AccentColor);
                }
            }
            else
            {
                RemainingText.Text = "--:--";
                ProgressFill.Width = 0;
            }
        }
        catch
        {
        }
    }

    private static string FormatTime(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss")
            : value.ToString(@"mm\:ss");
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        // En la cabecera basta la hora: las pestañas con nombre ocupan el resto del ancho.
        HeaderClock.Text = now.ToString("HH:mm", CultureInfo.GetCultureInfo("es-CL"));
        CompactClock.Text = now.ToString("HH:mm");
        UpdateCompactCenter();
    }

    private async void ProgressBarTrack_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_mediaSession is null || _duration <= TimeSpan.Zero) return;
        var trackWidth = ProgressBarTrack.ActualWidth;
        if (trackWidth <= 0) return;

        var point = e.GetPosition(ProgressBarTrack);
        var ratio = Math.Clamp(point.X / trackWidth, 0, 1);
        var targetTicks = (long)(_duration.Ticks * ratio);
        var targetTime = TimeSpan.FromTicks(targetTicks);

        _basePosition = targetTime;
        _lastPositionSyncTime = DateTimeOffset.UtcNow;
        UpdateTimeline();

        try
        {
            await _mediaSession.TryChangePlaybackPositionAsync(targetTicks);
        }
        catch
        {
        }
    }

    private void ProgressBarTrack_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTimeline();
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaSession is not null)
            await _mediaSession.TrySkipPreviousAsync();
    }

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaSession is not null)
            await _mediaSession.TryTogglePlayPauseAsync();
        UpdatePlaybackState();
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaSession is not null)
            await _mediaSession.TrySkipNextAsync();
    }
}
