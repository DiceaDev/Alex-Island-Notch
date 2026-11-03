using AlexIsland.App.Native;
using AlexIsland.App.Services;
using AlexIsland.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private enum IslandTab
    {
        Media,
        Backlog
    }

    private const int NotchWidth = 196;
    private const int NotchHeight = 36;
    private const int ExpandedWidth = 430;
    private const int ExpandedHeight = 118;
    private const int ExpandedBacklogHeight = 188;
    private const double EarRadius = 12.0;

    private static readonly string[] AvailableTags = ["TODO", "FEAT", "FIX", "REFACTOR", "DOCS"];

    private readonly AppWindow _appWindow;
    private readonly nint _hwnd;
    private readonly DispatcherQueueTimer _collapseTimer;
    private readonly DispatcherQueueTimer _clockTimer;
    private readonly DispatcherQueueTimer _timelineTimer;
    private readonly DispatcherQueueTimer _waveformTimer;
    private readonly Random _random = new();

    private readonly DevTaskStore _taskStore = new();
    private readonly List<DevTask> _tasks = new();
    private IslandTab _activeTab = IslandTab.Media;
    private int _currentTagIndex;

    private GlobalSystemMediaTransportControlsSessionManager? _mediaManager;
    private GlobalSystemMediaTransportControlsSession? _mediaSession;
    private bool _isPointerInside;
    private bool _isPinned;
    private bool _isExpanded;
    private bool _isDisposed;
    private bool _isPlaying;
    private Storyboard? _activeAnimation;

    // Estado sincronizado del timeline del reproductor
    private TimeSpan _basePosition = TimeSpan.Zero;
    private TimeSpan _duration = TimeSpan.Zero;
    private TimeSpan _lastSmtcPosition = TimeSpan.Zero;
    private DateTimeOffset _lastPositionSyncTime = DateTimeOffset.UtcNow;

    public MainWindow()
    {
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        try
        {
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            }
        }
        catch
        {
        }

        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;

        _collapseTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(280);
        _collapseTimer.IsRepeating = false;
        _collapseTimer.Tick += CollapseTimer_Tick;

        _clockTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.IsRepeating = true;
        _clockTimer.Tick += (_, _) => UpdateClock();

        // Intervalo de 500ms para actualización suave y en tiempo real del progreso
        _timelineTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timelineTimer.Interval = TimeSpan.FromMilliseconds(500);
        _timelineTimer.IsRepeating = true;
        _timelineTimer.Tick += (_, _) => UpdateTimeline();

        _waveformTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _waveformTimer.Interval = TimeSpan.FromMilliseconds(130);
        _waveformTimer.IsRepeating = true;
        _waveformTimer.Tick += (_, _) => AnimateWaveform();

        UpdateClock();
        ApplyIslandState();
        _ = InitializeMediaAsync();
        _ = LoadTasksAsync();
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        try
        {
            NativeMethods.ConfigureIslandWindow(_hwnd);
            UpdateWindowRegion((int)IslandContainer.Width, (int)IslandContainer.Height, EarRadius, _isExpanded ? 18.0 : 16.0);
        }
        catch
        {
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _isDisposed = true;
        _collapseTimer.Stop();
        _clockTimer.Stop();
        _timelineTimer.Stop();
        _waveformTimer.Stop();
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
        if (e.OriginalSource is DependencyObject dep)
        {
            if (FindVisualParent<Button>(dep) is not null ||
                FindVisualParent<TextBox>(dep) is not null ||
                FindVisualParent<ScrollViewer>(dep) is not null)
            {
                return;
            }
        }

        _collapseTimer.Stop();
        _isPinned = !_isPinned;
        if (!_isPinned) _isPointerInside = NativeMethods.IsCursorWithinWindow(_hwnd);
        ApplyIslandState();
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var current = child;
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void ApplyIslandState()
    {
        var wasExpanded = _isExpanded;
        _isExpanded = _isPinned || _isPointerInside;

        CompactContent.Visibility = _isExpanded ? Visibility.Collapsed : Visibility.Visible;
        ExpandedContent.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;
        ExpandedContent.Opacity = _isExpanded ? 1 : 0;

        var width = _isExpanded ? ExpandedWidth : NotchWidth;
        var height = _isExpanded
            ? (_activeTab == IslandTab.Backlog ? ExpandedBacklogHeight : ExpandedHeight)
            : NotchHeight;

        IslandContainer.Width = width;
        IslandContainer.Height = height;

        UpdateNotchGeometry(width, height);
        ResizeIsland(width, height);
        PositionAtTopCenter();

        if (_isExpanded)
        {
            _clockTimer.Start();
            UpdateClock();

            if (_activeTab == IslandTab.Media)
            {
                _timelineTimer.Start();
                SyncTimelineSnapshot();
                UpdateTimeline();
            }
            else
            {
                _timelineTimer.Stop();
            }
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

        var earRadius = EarRadius;
        var bottomRadius = _isExpanded ? 18.0 : 16.0;
        var bodyWidth = width - 2 * earRadius;
        if (bodyWidth <= 0) bodyWidth = width;

        var figure = new PathFigure
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            IsClosed = true,
            IsFilled = true
        };

        // 1. Oreja cóncava izquierda (fillet fluido hacia el marco superior)
        figure.Segments.Add(new BezierSegment
        {
            Point1 = new Windows.Foundation.Point(earRadius * 0.552, 0),
            Point2 = new Windows.Foundation.Point(earRadius, earRadius * 0.448),
            Point3 = new Windows.Foundation.Point(earRadius, earRadius)
        });

        // 2. Borde vertical izquierdo
        figure.Segments.Add(new LineSegment
        {
            Point = new Windows.Foundation.Point(earRadius, height - bottomRadius)
        });

        // 3. Esquina inferior izquierda convexa
        figure.Segments.Add(new BezierSegment
        {
            Point1 = new Windows.Foundation.Point(earRadius, height - bottomRadius * 0.448),
            Point2 = new Windows.Foundation.Point(earRadius + bottomRadius * 0.448, height),
            Point3 = new Windows.Foundation.Point(earRadius + bottomRadius, height)
        });

        // 4. Borde inferior horizontal
        figure.Segments.Add(new LineSegment
        {
            Point = new Windows.Foundation.Point(earRadius + bodyWidth - bottomRadius, height)
        });

        // 5. Esquina inferior derecha convexa
        figure.Segments.Add(new BezierSegment
        {
            Point1 = new Windows.Foundation.Point(earRadius + bodyWidth - bottomRadius * 0.448, height),
            Point2 = new Windows.Foundation.Point(earRadius + bodyWidth, height - bottomRadius * 0.448),
            Point3 = new Windows.Foundation.Point(earRadius + bodyWidth, height - bottomRadius)
        });

        // 6. Borde vertical derecho
        figure.Segments.Add(new LineSegment
        {
            Point = new Windows.Foundation.Point(earRadius + bodyWidth, earRadius)
        });

        // 7. Oreja cóncava derecha (fillet fluido hacia el marco superior)
        figure.Segments.Add(new BezierSegment
        {
            Point1 = new Windows.Foundation.Point(earRadius + bodyWidth, earRadius * 0.448),
            Point2 = new Windows.Foundation.Point(earRadius + bodyWidth + earRadius * 0.448, 0),
            Point3 = new Windows.Foundation.Point(earRadius + bodyWidth + earRadius, 0)
        });

        var pathGeo = new PathGeometry();
        pathGeo.Figures.Add(figure);
        NotchBackground.Data = pathGeo;

        UpdateWindowRegion((int)width, (int)height, earRadius, bottomRadius);
    }

    private void UpdateWindowRegion(int width, int height, double earRadius, double bottomRadius)
    {
        if (_hwnd == 0) return;
        var bodyWidth = width - 2 * earRadius;
        if (bodyWidth <= 0) bodyWidth = width;

        var points = new List<NativeMethods.NativePoint>
        {
            new() { X = 0, Y = 0 }
        };

        // Oreja izquierda (cóncava)
        SampleBezier(
            0, 0,
            (int)(earRadius * 0.552), 0,
            (int)earRadius, (int)(earRadius * 0.448),
            (int)earRadius, (int)earRadius,
            points, 5);

        // Borde izquierdo
        points.Add(new() { X = (int)earRadius, Y = (int)(height - bottomRadius) });

        // Esquina inferior izquierda (convexa)
        SampleBezier(
            (int)earRadius, (int)(height - bottomRadius),
            (int)earRadius, (int)(height - bottomRadius * 0.448),
            (int)(earRadius + bottomRadius * 0.448), height,
            (int)(earRadius + bottomRadius), height,
            points, 5);

        // Borde inferior
        points.Add(new() { X = (int)(earRadius + bodyWidth - bottomRadius), Y = height });

        // Esquina inferior derecha (convexa)
        SampleBezier(
            (int)(earRadius + bodyWidth - bottomRadius), height,
            (int)(earRadius + bodyWidth - bottomRadius * 0.448), height,
            (int)(earRadius + bodyWidth), (int)(height - bottomRadius * 0.448),
            (int)(earRadius + bodyWidth), (int)(height - bottomRadius),
            points, 5);

        // Borde derecho
        points.Add(new() { X = (int)(earRadius + bodyWidth), Y = (int)earRadius });

        // Oreja derecha (cóncava)
        SampleBezier(
            (int)(earRadius + bodyWidth), (int)earRadius,
            (int)(earRadius + bodyWidth), (int)(earRadius * 0.448),
            (int)(earRadius + bodyWidth + earRadius * 0.448), 0,
            (int)(earRadius + bodyWidth + earRadius), 0,
            points, 5);

        NativeMethods.SetWindowPolygonRegion(_hwnd, points.ToArray());
    }

    private static void SampleBezier(
        int x0, int y0, int x1, int y1, int x2, int y2, int x3, int y3,
        List<NativeMethods.NativePoint> points, int steps)
    {
        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            double u = 1.0 - t;
            double tt = t * t;
            double uu = u * u;
            double uuu = uu * u;
            double ttt = tt * t;

            int x = (int)Math.Round(uuu * x0 + 3 * uu * t * x1 + 3 * u * tt * x2 + ttt * x3);
            int y = (int)Math.Round(uuu * y0 + 3 * uu * t * y1 + 3 * u * tt * y2 + ttt * y3);
            points.Add(new() { X = x, Y = y });
        }
    }

    private void AnimateIsland(bool expanding)
    {
        _activeAnimation?.Stop();
        var storyboard = new Storyboard();
        var scaleX = new DoubleAnimation
        {
            From = expanding ? 0.96 : 1.0,
            To = expanding ? 1.0 : 0.96,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var scaleY = new DoubleAnimation
        {
            From = expanding ? 0.93 : 1.0,
            To = expanding ? 1.0 : 0.93,
            Duration = TimeSpan.FromMilliseconds(220),
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
        try
        {
            IslandScale.CenterX = width / 2.0;
            IslandScale.CenterY = 0; // Escala anclada al borde superior de la pantalla
            _appWindow.Resize(new SizeInt32(width, height));
        }
        catch
        {
        }
    }

    private void PositionAtTopCenter()
    {
        try
        {
            DisplayArea? display = null;
            try
            {
                display = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
            }
            catch
            {
            }

            display ??= DisplayArea.Primary;
            if (display is null) return;

            var bounds = display.OuterBounds;
            var width = (int)IslandContainer.Width;
            var x = bounds.X + ((bounds.Width - width) / 2);
            _appWindow.Move(new PointInt32(x, bounds.Y));
        }
        catch
        {
        }
    }

    private void AnimateWaveform()
    {
        if (!_isPlaying) return;
        WaveBar1.Height = _random.Next(4, 9);
        WaveBar2.Height = _random.Next(8, 15);
        WaveBar3.Height = _random.Next(10, 18);
        WaveBar4.Height = _random.Next(5, 12);
    }

    private void ResetWaveform()
    {
        WaveBar1.Height = 5;
        WaveBar2.Height = 11;
        WaveBar3.Height = 14;
        WaveBar4.Height = 7;
        var brush = new SolidColorBrush(_isPlaying
            ? ColorHelper.FromArgb(255, 0, 230, 118)
            : ColorHelper.FromArgb(180, 245, 245, 247));
        WaveBar1.Background = brush;
        WaveBar2.Background = brush;
        WaveBar3.Background = brush;
        WaveBar4.Background = brush;
    }

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
    }

    private void UpdatePlaybackState()
    {
        if (_mediaSession is null)
        {
            _isPlaying = false;
            _waveformTimer.Stop();
            ResetWaveform();
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
        var culture = CultureInfo.GetCultureInfo("es-CL");
        var stamp = now.ToString("ddd d MMM · HH:mm", culture);
        HeaderClock.Text = stamp;
        CompactClock.Text = now.ToString("HH:mm");
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

    // ==============================================================
    // SECCIÓN DEV BACKLOG / TO-DO
    // ==============================================================

    private async Task LoadTasksAsync()
    {
        try
        {
            var loaded = await _taskStore.LoadAsync();
            _tasks.Clear();
            _tasks.AddRange(loaded);
            UpdateBacklogUI();
        }
        catch
        {
        }
    }

    private void UpdateBacklogUI()
    {
        var pendingCount = _tasks.Count(t => !t.IsCompleted);

        // Actualizar badges
        HeaderBacklogCount.Text = pendingCount.ToString();
        CompactTaskCount.Text = pendingCount.ToString();
        CompactTaskBadge.Visibility = pendingCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        BacklogSummaryText.Text = pendingCount == 1 ? "1 pendiente" : $"{pendingCount} pendientes";

        RenderTasksList();
    }

    private void RenderTasksList()
    {
        TasksItemsPanel.Children.Clear();

        if (_tasks.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = "⚡ Sin tareas en el backlog. ¡Escribe una arriba!",
                FontSize = 10,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(160, 133, 138, 150)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 18, 0, 0)
            };
            TasksItemsPanel.Children.Add(emptyText);
            return;
        }

        // Mostrar pendientes primero, luego completadas
        var ordered = _tasks.OrderBy(t => t.IsCompleted).ThenByDescending(t => t.CreatedAt).ToList();

        foreach (var task in ordered)
        {
            var itemContainer = new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(200, 26, 28, 35)),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 4, 6, 4),
                BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(35, 255, 255, 255)),
                BorderThickness = new Thickness(1)
            };

            var rowGrid = new Grid { ColumnSpacing = 6 };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Checkbox interactivo
            var checkBtn = new Button
            {
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (task.IsCompleted)
            {
                checkBtn.Background = new SolidColorBrush(ColorHelper.FromArgb(50, 0, 230, 118));
                checkBtn.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 0, 230, 118));
                checkBtn.Content = new FontIcon
                {
                    Glyph = "\uE73E",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 0, 230, 118))
                };
            }
            else
            {
                checkBtn.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 23, 25, 32));
                checkBtn.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 60, 65, 80));
                checkBtn.Content = null;
            }

            checkBtn.Click += (_, _) =>
            {
                task.IsCompleted = !task.IsCompleted;
                _ = _taskStore.SaveAsync(_tasks);
                UpdateBacklogUI();
            };
            Grid.SetColumn(checkBtn, 0);
            rowGrid.Children.Add(checkBtn);

            // Chip de categoría
            var (tagBg, tagFg) = GetCategoryBrushes(task.Category);
            var tagBadge = new Border
            {
                Background = tagBg,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = task.Category,
                    FontSize = 8,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = tagFg
                }
            };
            Grid.SetColumn(tagBadge, 1);
            rowGrid.Children.Add(tagBadge);

            // Título de la tarea
            var titleBlock = new TextBlock
            {
                Text = task.Title,
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                Foreground = task.IsCompleted
                    ? new SolidColorBrush(ColorHelper.FromArgb(140, 133, 138, 150))
                    : new SolidColorBrush(ColorHelper.FromArgb(255, 245, 245, 247))
            };

            if (task.IsCompleted)
            {
                titleBlock.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
            }

            Grid.SetColumn(titleBlock, 2);
            rowGrid.Children.Add(titleBlock);

            // Botón eliminar tarea
            var deleteBtn = new Button
            {
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Content = new FontIcon
                {
                    Glyph = "\uE711",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(ColorHelper.FromArgb(140, 133, 138, 150))
                }
            };
            deleteBtn.Click += (_, _) =>
            {
                _tasks.Remove(task);
                _ = _taskStore.SaveAsync(_tasks);
                UpdateBacklogUI();
            };
            Grid.SetColumn(deleteBtn, 3);
            rowGrid.Children.Add(deleteBtn);

            itemContainer.Child = rowGrid;
            TasksItemsPanel.Children.Add(itemContainer);
        }
    }

    private static (SolidColorBrush Bg, SolidColorBrush Fg) GetCategoryBrushes(string category)
    {
        return category switch
        {
            "FEAT" => (
                new SolidColorBrush(ColorHelper.FromArgb(40, 33, 150, 243)),
                new SolidColorBrush(ColorHelper.FromArgb(255, 66, 165, 245))),
            "FIX" => (
                new SolidColorBrush(ColorHelper.FromArgb(40, 239, 83, 80)),
                new SolidColorBrush(ColorHelper.FromArgb(255, 239, 83, 80))),
            "REFACTOR" => (
                new SolidColorBrush(ColorHelper.FromArgb(40, 255, 167, 38)),
                new SolidColorBrush(ColorHelper.FromArgb(255, 255, 167, 38))),
            "DOCS" => (
                new SolidColorBrush(ColorHelper.FromArgb(40, 171, 71, 188)),
                new SolidColorBrush(ColorHelper.FromArgb(255, 171, 71, 188))),
            _ => (
                new SolidColorBrush(ColorHelper.FromArgb(40, 38, 166, 154)),
                new SolidColorBrush(ColorHelper.FromArgb(255, 38, 166, 154)))
        };
    }

    private void TabMediaButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchTab(IslandTab.Media);
    }

    private void TabBacklogButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchTab(IslandTab.Backlog);
    }

    private void SwitchTab(IslandTab tab)
    {
        _activeTab = tab;

        if (_activeTab == IslandTab.Media)
        {
            TabMediaButton.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 42, 45, 55));
            TabMediaLabel.Foreground = new SolidColorBrush(Colors.White);

            TabBacklogButton.Background = new SolidColorBrush(Colors.Transparent);
            TabBacklogLabel.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 160, 165, 178));

            MediaView.Visibility = Visibility.Visible;
            BacklogView.Visibility = Visibility.Collapsed;
        }
        else
        {
            TabBacklogButton.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 42, 45, 55));
            TabBacklogLabel.Foreground = new SolidColorBrush(Colors.White);

            TabMediaButton.Background = new SolidColorBrush(Colors.Transparent);
            TabMediaLabel.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 160, 165, 178));

            MediaView.Visibility = Visibility.Collapsed;
            BacklogView.Visibility = Visibility.Visible;

            NewTaskInput.Focus(FocusState.Programmatic);
        }

        ApplyIslandState();
    }

    private void TagCycleButton_Click(object sender, RoutedEventArgs e)
    {
        _currentTagIndex = (_currentTagIndex + 1) % AvailableTags.Length;
        var tag = AvailableTags[_currentTagIndex];
        CurrentTagLabel.Text = tag;

        var (_, fg) = GetCategoryBrushes(tag);
        CurrentTagLabel.Foreground = fg;
    }

    private void NewTaskInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            AddTaskFromInput();
            e.Handled = true;
        }
    }

    private void AddTaskButton_Click(object sender, RoutedEventArgs e)
    {
        AddTaskFromInput();
    }

    private void AddTaskFromInput()
    {
        var rawText = NewTaskInput.Text;
        if (string.IsNullOrWhiteSpace(rawText)) return;

        var (parsedCat, cleanTitle) = DevTask.ParseInput(rawText);
        var finalCategory = parsedCat != "TODO" ? parsedCat : AvailableTags[_currentTagIndex];

        var task = new DevTask
        {
            Title = cleanTitle,
            Category = finalCategory,
            IsCompleted = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _tasks.Insert(0, task);
        NewTaskInput.Text = string.Empty;

        _ = _taskStore.SaveAsync(_tasks);
        UpdateBacklogUI();
    }

    private void CopyMarkdownButton_Click(object sender, RoutedEventArgs e)
    {
        var md = DevTaskStore.ToMarkdown(_tasks);
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(md);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);

            CopyMarkdownLabel.Text = "¡Copiado!";
            var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(1.5);
            timer.IsRepeating = false;
            timer.Tick += (_, _) =>
            {
                CopyMarkdownLabel.Text = "Copiar MD";
                timer.Stop();
            };
            timer.Start();
        }
        catch
        {
        }
    }

    private void ClearCompletedButton_Click(object sender, RoutedEventArgs e)
    {
        _tasks.RemoveAll(t => t.IsCompleted);
        _ = _taskStore.SaveAsync(_tasks);
        UpdateBacklogUI();
    }
}
