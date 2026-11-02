using AlexIsland.App.Native;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Media.Control;
using WinRT.Interop;
using System.Globalization;

namespace AlexIsland.App;

public sealed partial class MainWindow : Window
{
    private const int NotchWidth = 160;
    private const int NotchHeight = 38;
    private const int ExpandedWidth = 920;
    private const int ExpandedHeight = 350;
    private const double EarRadius = 10;

    private readonly AppWindow _appWindow;
    private readonly nint _hwnd;
    private readonly DispatcherQueueTimer _collapseTimer;
    private readonly DispatcherQueueTimer _clockTimer;
    private readonly DispatcherQueueTimer _timelineTimer;

    private GlobalSystemMediaTransportControlsSessionManager? _mediaManager;
    private GlobalSystemMediaTransportControlsSession? _mediaSession;
    private bool _isPointerInside;
    private bool _isPinned;
    private bool _isExpanded;
    private bool _isDisposed;
    private Storyboard? _activeAnimation;

    public MainWindow()
    {
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;

        _collapseTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(300);
        _collapseTimer.IsRepeating = false;
        _collapseTimer.Tick += CollapseTimer_Tick;

        _clockTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.IsRepeating = true;
        _clockTimer.Tick += (_, _) => UpdateClock();

        _timelineTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timelineTimer.Interval = TimeSpan.FromSeconds(1);
        _timelineTimer.IsRepeating = true;
        _timelineTimer.Tick += (_, _) => UpdateTimeline();

        UpdateClock();
        UpdateNotchGeometry(NotchWidth, NotchHeight);
        ApplyIslandState();
        _ = InitializeMediaAsync();
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        NativeMethods.ConfigureIslandWindow(_hwnd);
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _isDisposed = true;
        _collapseTimer.Stop();
        _clockTimer.Stop();
        _timelineTimer.Stop();
        if (_mediaManager is not null)
        {
            _mediaManager.CurrentSessionChanged -= MediaManager_CurrentSessionChanged;
        }
        DetachMediaSession();
    }

    private void IslandContainer_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _collapseTimer.Stop();
        if (_isPointerInside) return;
        _isPointerInside = true;
        ApplyIslandState();
    }

    private void IslandContainer_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_isPinned) return;
        _isPointerInside = false;
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private void CollapseTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        _collapseTimer.Stop();
        if (_isPinned) return;

        _isPointerInside = NativeMethods.IsCursorWithinWindow(_hwnd);
        if (!_isPointerInside) ApplyIslandState();
    }

    private void IslandContainer_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Los botones multimedia conservan su acción; tocar el fondo fija o libera.
        if (e.OriginalSource is Microsoft.UI.Xaml.Controls.Button) return;
        _collapseTimer.Stop();
        _isPinned = !_isPinned;
        if (!_isPinned) _isPointerInside = NativeMethods.IsCursorWithinWindow(_hwnd);
        ApplyIslandState();
    }

    private void ApplyIslandState()
    {
        var wasExpanded = _isExpanded;
        _isExpanded = _isPinned || _isPointerInside;

        CompactContent.Visibility = _isExpanded ? Visibility.Collapsed : Visibility.Visible;
        ExpandedContent.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;

        var width = _isExpanded ? ExpandedWidth : NotchWidth;
        var height = _isExpanded ? ExpandedHeight : NotchHeight;
        IslandContainer.Width = width;
        IslandContainer.Height = height;
        UpdateNotchGeometry(width, height);
        ResizeIsland(width, height);
        PositionAtTopCenter();

        if (_isExpanded)
        {
            _clockTimer.Start();
            _timelineTimer.Start();
            UpdateClock();
            UpdateTimeline();
        }
        else
        {
            _clockTimer.Stop();
            _timelineTimer.Stop();
        }

        if (wasExpanded != _isExpanded) AnimateIsland(_isExpanded);
    }

    private void UpdateNotchGeometry(double width, double height)
    {
        if (width <= 0 || height <= 0) return;

        var bottomRadius = _isExpanded ? 24.0 : 14.0;
        var figure = new PathFigure
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            IsClosed = true,
            IsFilled = true
        };

        // Oreja cóncava superior izquierda: la silueta cae desde el borde
        // superior hasta el lateral, imitando la unión de una muesca física.
        figure.Segments.Add(new ArcSegment
        {
            Point = new Windows.Foundation.Point(EarRadius, EarRadius),
            Size = new Windows.Foundation.Size(EarRadius, EarRadius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = false
        });
        figure.Segments.Add(new LineSegment
        {
            Point = new Windows.Foundation.Point(EarRadius, height - bottomRadius)
        });
        figure.Segments.Add(new ArcSegment
        {
            Point = new Windows.Foundation.Point(EarRadius + bottomRadius, height),
            Size = new Windows.Foundation.Size(bottomRadius, bottomRadius),
            SweepDirection = SweepDirection.Counterclockwise,
            IsLargeArc = false
        });
        figure.Segments.Add(new LineSegment
        {
            Point = new Windows.Foundation.Point(width - EarRadius - bottomRadius, height)
        });
        figure.Segments.Add(new ArcSegment
        {
            Point = new Windows.Foundation.Point(width - EarRadius, height - bottomRadius),
            Size = new Windows.Foundation.Size(bottomRadius, bottomRadius),
            SweepDirection = SweepDirection.Counterclockwise,
            IsLargeArc = false
        });
        figure.Segments.Add(new LineSegment
        {
            Point = new Windows.Foundation.Point(width - EarRadius, EarRadius)
        });
        figure.Segments.Add(new ArcSegment
        {
            Point = new Windows.Foundation.Point(width, 0),
            Size = new Windows.Foundation.Size(EarRadius, EarRadius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = false
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        NotchBackground.Data = geometry;
        NotchBorderStroke.Data = geometry;
    }

    private void AnimateIsland(bool expanding)
    {
        _activeAnimation?.Stop();
        var storyboard = new Storyboard();
        var scaleX = new DoubleAnimation
        {
            From = expanding ? 0.96 : 1.0,
            To = expanding ? 1.0 : 0.96,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var scaleY = new DoubleAnimation
        {
            From = expanding ? 0.94 : 1.0,
            To = expanding ? 1.0 : 0.94,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(scaleX, IslandScale);
        Storyboard.SetTargetProperty(scaleX, "ScaleX");
        Storyboard.SetTarget(scaleY, IslandScale);
        Storyboard.SetTargetProperty(scaleY, "ScaleY");
        storyboard.Children.Add(scaleX);
        storyboard.Children.Add(scaleY);
        _activeAnimation = storyboard;
        storyboard.Begin();
    }

    private void ResizeIsland(int width, int height)
    {
        IslandScale.CenterX = width / 2.0;
        IslandScale.CenterY = height / 2.0;
        _appWindow.Resize(new SizeInt32(width, height));
    }

    private void PositionAtTopCenter()
    {
        var display = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        if (display is null) return;
        var bounds = display.OuterBounds;
        var x = bounds.X + ((bounds.Width - (int)IslandContainer.Width) / 2);
        _appWindow.Move(new PointInt32(x, bounds.Y));
    }

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
            _ = DispatcherQueue.TryEnqueue(UpdateTimeline);
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
            PlaybackSource.Text = _mediaSession.SourceAppUserModelId;
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
        PlaybackSource.Text = "Windows Media Session";
        AlbumArt.Source = null;
        AlbumPlaceholder.Visibility = Visibility.Visible;
        PlayPauseButton.Content = "\uE768";
        ElapsedText.Text = "00:00";
        RemainingText.Text = "--:--";
        ProgressFill.Width = 0;
    }

    private void UpdatePlaybackState()
    {
        if (_mediaSession is null) return;
        var playback = _mediaSession.GetPlaybackInfo();
        var playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        PlayPauseButton.Content = playing ? "\uE769" : "\uE768";
    }

    private void UpdateTimeline()
    {
        if (_mediaSession is null) return;
        try
        {
            var timeline = _mediaSession.GetTimelineProperties();
            var position = timeline.Position;
            var duration = timeline.EndTime - timeline.StartTime;
            if (duration <= TimeSpan.Zero) duration = timeline.EndTime;
            ElapsedText.Text = FormatTime(position);
            var remaining = duration - position;
            RemainingText.Text = remaining > TimeSpan.Zero ? "-" + FormatTime(remaining) : "00:00";
            var ratio = duration.TotalSeconds > 0
                ? Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1)
                : 0;
            ProgressFill.Width = Math.Max(0, ProgressBarTrack.ActualWidth) * ratio;
        }
        catch { }
    }

    private static string FormatTime(TimeSpan value)
    {
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss")
            : value.ToString(@"mm\:ss");
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        var culture = CultureInfo.GetCultureInfo("es-CL");
        var stamp = now.ToString("ddd d MMM yyyy  HH:mm", culture);
        HeaderClock.Text = stamp;
        CompactClock.Text = now.ToString("HH:mm");
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
