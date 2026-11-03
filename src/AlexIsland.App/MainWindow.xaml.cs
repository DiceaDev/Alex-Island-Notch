using AlexIsland.App.Native;
using AlexIsland.App.Services;
using AlexIsland.Core.Models;
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

public sealed partial class MainWindow : Window
{
    private enum IslandTab
    {
        Media,
        Backlog,
        Calendar,
        Pomodoro
    }

    private enum CalendarSubMode
    {
        Agenda,
        Week,
        Month
    }

    private enum NotchDisplayMode
    {
        Resting,
        Closed,
        Peek,
        Expanded
    }

    public enum HardwarePeekType
    {
        Headphones,
        Charging,
        Battery,
        CapsLock,
        Custom
    }

    private const int NotchWidth = 196;
    private const int NotchHeight = 36;
    private const int RestingWidth = 146;
    private const int RestingHeight = 32;
    private const int PeekWidth = 280;
    private const int PeekHeight = 40;
    private const int ExpandedWidth = 440;
    private const int ExpandedHeight = 118;
    private const int ExpandedBacklogHeight = 188;
    private const int ExpandedCalendarHeight = 226;
    private const int ExpandedPomodoroHeight = 188;
    private const double EarRadius = 12.0;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
    private const int VK_CAPITAL = 0x14;

    private static readonly string[] AvailableTags = ["TODO", "FEAT", "FIX", "REFACTOR", "DOCS"];
    private static readonly string[] EventCategories = ["MEETING", "DEV", "REVIEW", "PLANNING", "GENERAL"];

    private readonly AppWindow _appWindow;
    private readonly nint _hwnd;
    private readonly DispatcherQueueTimer _collapseTimer;
    private readonly DispatcherQueueTimer _clockTimer;
    private readonly DispatcherQueueTimer _timelineTimer;
    private readonly DispatcherQueueTimer _waveformTimer;
    private readonly DispatcherQueueTimer _reminderTimer;
    private readonly DispatcherQueueTimer _pomodoroTimer;
    private readonly DispatcherQueueTimer _blinkTimer;
    private readonly DispatcherQueueTimer _capsLockTimer;
    private readonly DispatcherQueueTimer _idleTimer;
    private DispatcherQueueTimer? _peekTimer;
    private readonly Random _random = new();

    // Estado de visualización y hardware de la isla
    private NotchDisplayMode _currentDisplayMode = NotchDisplayMode.Closed;
    private bool _isPeekActive;
    private bool _isRecentInteraction = true;
    private bool _lastCapsLockState;

    // Módulo Backlog
    private readonly DevTaskStore _taskStore = new();
    private readonly List<DevTask> _tasks = new();
    private int _currentTagIndex;

    // Módulo Calendario & Agenda
    private readonly CalendarEventStore _calendarStore = new();
    private readonly List<CalendarEvent> _events = new();
    private DateTimeOffset _selectedCalendarDate = DateTimeOffset.Now;
    private DateTimeOffset _selectedMonth = new(DateTime.Now.Year, DateTime.Now.Month, 1, 0, 0, 0, DateTimeOffset.Now.Offset);
    private CalendarSubMode _calendarSubMode = CalendarSubMode.Agenda;
    private CalendarEvent? _editingEvent;
    private int _currentCategoryIndex;
    private string? _upcomingMeetingUrl;

    // Módulo Pomodoro / Focus
    private readonly PomodoroStore _pomodoroStore = new();
    private PomodoroSettings _pomodoroSettings = new();
    private PomodoroMode _pomodoroMode = PomodoroMode.Work;
    private PomodoroState _pomodoroState = PomodoroState.Stopped;
    private TimeSpan _pomodoroRemaining = TimeSpan.FromMinutes(25);
    private TimeSpan _pomodoroTotalDuration = TimeSpan.FromMinutes(25);
    private string? _linkedPomodoroTaskId;

    // Estado general de la isla
    private IslandTab _activeTab = IslandTab.Media;
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

        // Temporizador de recordatorios de reuniones (cada 30s)
        _reminderTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _reminderTimer.Interval = TimeSpan.FromSeconds(30);
        _reminderTimer.IsRepeating = true;
        _reminderTimer.Tick += (_, _) => CheckUpcomingMeetings();
        _reminderTimer.Start();

        // Temporizador de Pomodoro (cada 1s)
        _pomodoroTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _pomodoroTimer.Interval = TimeSpan.FromSeconds(1);
        _pomodoroTimer.IsRepeating = true;
        _pomodoroTimer.Tick += (_, _) => PomodoroTimer_Tick();

        // Ojitos parpadeantes para modo Resting
        _blinkTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _blinkTimer.Interval = TimeSpan.FromSeconds(3.5);
        _blinkTimer.IsRepeating = true;
        _blinkTimer.Tick += (_, _) => TriggerEyeBlink();

        // Monitor de tecla Bloq Mayús (Caps Lock)
        _capsLockTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _capsLockTimer.Interval = TimeSpan.FromMilliseconds(220);
        _capsLockTimer.IsRepeating = true;
        _capsLockTimer.Tick += (_, _) => CheckCapsLockState();
        _capsLockTimer.Start();

        // Inactividad para entrar suavemente al modo Resting
        _idleTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _idleTimer.Interval = TimeSpan.FromSeconds(5);
        _idleTimer.IsRepeating = false;
        _idleTimer.Tick += (_, _) =>
        {
            _idleTimer.Stop();
            if (!_isPlaying && _pomodoroState != PomodoroState.Running && !_isExpanded && !_isPeekActive)
            {
                _isRecentInteraction = false;
                ApplyIslandState();
            }
        };

        // Monitoreo de estado de batería / cargador de Windows
        try
        {
            Windows.System.Power.PowerManager.BatteryStatusChanged += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    var isCharging = Windows.System.Power.PowerManager.BatteryStatus == Windows.System.Power.BatteryStatus.Charging;
                    var pct = Windows.System.Power.PowerManager.RemainingChargePercent;
                    ShowHardwarePeek(
                        isCharging ? HardwarePeekType.Charging : HardwarePeekType.Battery,
                        isCharging ? "Cargador conectado" : "Batería en uso",
                        isCharging ? $"Cargando · {pct}%" : $"{pct}% restante",
                        percent01: pct / 100.0);
                });
            };
        }
        catch { }

        // Monitoreo de salida de audio / auriculares
        try
        {
            Windows.Media.Devices.MediaDevice.DefaultAudioRenderDeviceChanged += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ShowHardwarePeek(
                        HardwarePeekType.Headphones,
                        "Auriculares",
                        "Dispositivo de audio conectado",
                        percent01: 0.85);
                });
            };
        }
        catch { }

        UpdateClock();
        ApplyIslandState();
        _ = InitializeMediaAsync();
        _ = LoadTasksAsync();
        _ = LoadCalendarEventsAsync();
        _ = LoadPomodoroAsync();
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
        _reminderTimer.Stop();
        _pomodoroTimer.Stop();
        _blinkTimer.Stop();
        _capsLockTimer.Stop();
        _idleTimer.Stop();
        _peekTimer?.Stop();
        _syncStatusTimer?.Stop();
        if (_mediaManager is not null)
        {
            _mediaManager.CurrentSessionChanged -= MediaManager_CurrentSessionChanged;
        }
        DetachMediaSession();
    }

    private void IslandContainer_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _collapseTimer.Stop();
        _idleTimer.Stop();
        _isRecentInteraction = true;
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

        _idleTimer.Stop();
        _idleTimer.Start();
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
                FindVisualParent<ComboBox>(dep) is not null ||
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
        var previousMode = _currentDisplayMode;

        _isExpanded = _isPinned || _isPointerInside;

        if (_isExpanded)
        {
            _currentDisplayMode = NotchDisplayMode.Expanded;
        }
        else if (_isPeekActive)
        {
            _currentDisplayMode = NotchDisplayMode.Peek;
        }
        else if (_isPlaying || _pomodoroState == PomodoroState.Running || _isRecentInteraction)
        {
            _currentDisplayMode = NotchDisplayMode.Closed;
        }
        else
        {
            _currentDisplayMode = NotchDisplayMode.Resting;
        }

        RestingContent.Visibility = _currentDisplayMode == NotchDisplayMode.Resting ? Visibility.Visible : Visibility.Collapsed;
        CompactContent.Visibility = _currentDisplayMode == NotchDisplayMode.Closed ? Visibility.Visible : Visibility.Collapsed;
        PeekContent.Visibility = _currentDisplayMode == NotchDisplayMode.Peek ? Visibility.Visible : Visibility.Collapsed;
        ExpandedContent.Visibility = _currentDisplayMode == NotchDisplayMode.Expanded ? Visibility.Visible : Visibility.Collapsed;
        ExpandedContent.Opacity = _currentDisplayMode == NotchDisplayMode.Expanded ? 1 : 0;

        var (width, height) = _currentDisplayMode switch
        {
            NotchDisplayMode.Resting => (RestingWidth, RestingHeight),
            NotchDisplayMode.Closed => (NotchWidth, NotchHeight),
            NotchDisplayMode.Peek => (PeekWidth, PeekHeight),
            NotchDisplayMode.Expanded => (
                ExpandedWidth,
                _activeTab switch
                {
                    IslandTab.Calendar => ExpandedCalendarHeight,
                    IslandTab.Backlog => ExpandedBacklogHeight,
                    IslandTab.Pomodoro => ExpandedPomodoroHeight,
                    _ => ExpandedHeight
                }),
            _ => (NotchWidth, NotchHeight)
        };

        IslandContainer.Width = width;
        IslandContainer.Height = height;

        UpdateNotchGeometry(width, height);
        ResizeIsland(width, height);
        PositionAtTopCenter();

        if (_currentDisplayMode == NotchDisplayMode.Resting)
        {
            _blinkTimer?.Start();
        }
        else
        {
            _blinkTimer?.Stop();
        }

        if (_isExpanded || _currentDisplayMode == NotchDisplayMode.Closed)
        {
            _clockTimer.Start();
            UpdateClock();
        }
        else
        {
            _clockTimer.Stop();
        }

        if (_isExpanded)
        {
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

            CheckUpcomingMeetings();
            UpdatePomodoroUI();
        }
        else
        {
            _timelineTimer.Stop();
        }

        if (wasExpanded != _isExpanded || previousMode != _currentDisplayMode)
        {
            AnimateIsland(_isExpanded, _currentDisplayMode);
        }
    }

    private void UpdateNotchGeometry(double width, double height)
    {
        if (width <= 0 || height <= 0) return;

        var earRadius = EarRadius;
        var bottomRadius = _isExpanded ? 18.0 : (_currentDisplayMode == NotchDisplayMode.Resting ? 13.0 : 16.0);
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

    private void AnimateIsland(bool expanding, NotchDisplayMode mode = NotchDisplayMode.Expanded)
    {
        _activeAnimation?.Stop();
        var storyboard = new Storyboard();

        var isPeek = mode == NotchDisplayMode.Peek;
        var durationMs = expanding ? 270 : (isPeek ? 240 : 180);

        // Expansión y contracción elástica estilo Apple Dynamic Island (Spring / BackEase)
        var scaleX = new DoubleAnimation
        {
            From = (expanding || isPeek) ? 0.90 : 1.0,
            To = (expanding || isPeek) ? 1.0 : 0.94,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = (expanding || isPeek)
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.22 }
                : new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var scaleY = new DoubleAnimation
        {
            From = (expanding || isPeek) ? 0.85 : 1.0,
            To = (expanding || isPeek) ? 1.0 : 0.90,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = (expanding || isPeek)
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.22 }
                : new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(scaleX, IslandScale);
        Storyboard.SetTargetProperty(scaleX, "ScaleX");
        Storyboard.SetTarget(scaleY, IslandScale);
        Storyboard.SetTargetProperty(scaleY, "ScaleY");
        storyboard.Children.Add(scaleX);
        storyboard.Children.Add(scaleY);

        if (mode == NotchDisplayMode.Expanded)
        {
            var fade = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fade, ExpandedContent);
            Storyboard.SetTargetProperty(fade, "Opacity");
            storyboard.Children.Add(fade);
        }
        else if (mode == NotchDisplayMode.Peek)
        {
            AnimateViewEntrance(PeekContent);
        }

        _activeAnimation = storyboard;
        storyboard.Begin();
    }

    private void TriggerEyeBlink()
    {
        if (_currentDisplayMode != NotchDisplayMode.Resting) return;

        var sb = new Storyboard();

        var blinkY = new DoubleAnimationUsingKeyFrames();
        blinkY.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1.0 });
        blinkY.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(60), Value = 0.1 });
        blinkY.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(140), Value = 1.0 });

        Storyboard.SetTarget(blinkY, LeftEyeScale);
        Storyboard.SetTargetProperty(blinkY, "ScaleY");
        sb.Children.Add(blinkY);

        var blinkRightY = new DoubleAnimationUsingKeyFrames();
        blinkRightY.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1.0 });
        blinkRightY.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(60), Value = 0.1 });
        blinkRightY.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(140), Value = 1.0 });

        Storyboard.SetTarget(blinkRightY, RightEyeScale);
        Storyboard.SetTargetProperty(blinkRightY, "ScaleY");
        sb.Children.Add(blinkRightY);

        sb.Begin();

        if (_blinkTimer is not null)
        {
            _blinkTimer.Interval = TimeSpan.FromMilliseconds(_random.Next(3000, 6000));
        }
    }

    private void UpdateCompactProgress(double progress01, Windows.UI.Color? color = null)
    {
        progress01 = Math.Clamp(progress01, 0.0, 1.0);
        var strokeColor = color ?? ColorHelper.FromArgb(255, 0, 230, 118);
        CompactProgressArc.Stroke = new SolidColorBrush(strokeColor);
        CompactCenterDot.Fill = new SolidColorBrush(strokeColor);

        if (progress01 <= 0.02)
        {
            CompactProgressArc.Data = null;
            CompactCenterDot.Visibility = Visibility.Visible;
            return;
        }

        CompactCenterDot.Visibility = Visibility.Collapsed;
        var angle = Math.Min(progress01 * 360.0, 359.9);
        var rad = (angle - 90.0) * Math.PI / 180.0;
        var cx = 7.0;
        var cy = 7.0;
        var r = 6.0;

        var startX = 7.0;
        var startY = 1.0;
        var endX = cx + r * Math.Cos(rad);
        var endY = cy + r * Math.Sin(rad);

        var figure = new PathFigure
        {
            StartPoint = new Windows.Foundation.Point(startX, startY),
            IsClosed = false,
            IsFilled = false
        };

        figure.Segments.Add(new ArcSegment
        {
            Point = new Windows.Foundation.Point(endX, endY),
            Size = new Windows.Foundation.Size(r, r),
            IsLargeArc = angle > 180.0,
            SweepDirection = SweepDirection.Clockwise
        });

        var geo = new PathGeometry();
        geo.Figures.Add(figure);
        CompactProgressArc.Data = geo;
    }

    private void UpdatePeekProgress(double progress01, Windows.UI.Color color)
    {
        progress01 = Math.Clamp(progress01, 0.0, 1.0);
        PeekProgressArc.Stroke = new SolidColorBrush(color);

        if (progress01 <= 0.01)
        {
            PeekProgressArc.Data = null;
            return;
        }

        var angle = Math.Min(progress01 * 360.0, 359.9);
        var rad = (angle - 90.0) * Math.PI / 180.0;
        var cx = 11.0;
        var cy = 11.0;
        var r = 10.0;

        var startX = 11.0;
        var startY = 1.0;
        var endX = cx + r * Math.Cos(rad);
        var endY = cy + r * Math.Sin(rad);

        var figure = new PathFigure
        {
            StartPoint = new Windows.Foundation.Point(startX, startY),
            IsClosed = false,
            IsFilled = false
        };

        figure.Segments.Add(new ArcSegment
        {
            Point = new Windows.Foundation.Point(endX, endY),
            Size = new Windows.Foundation.Size(r, r),
            IsLargeArc = angle > 180.0,
            SweepDirection = SweepDirection.Clockwise
        });

        var geo = new PathGeometry();
        geo.Figures.Add(figure);
        PeekProgressArc.Data = geo;
    }

    public void ShowHardwarePeek(
        HardwarePeekType type,
        string title,
        string subtitle,
        double percent01 = 1.0,
        string? rightGlyph = null,
        Windows.UI.Color? accentColor = null)
    {
        if (_isExpanded) return;

        _isPeekActive = true;
        _isRecentInteraction = true;

        PeekTitle.Text = title;
        PeekSubtitle.Text = subtitle;

        var (iconGlyph, defaultRightGlyph, defaultColor) = type switch
        {
            HardwarePeekType.Headphones => ("\uE702", "\uE7F6", ColorHelper.FromArgb(255, 66, 165, 245)),
            HardwarePeekType.Charging => ("\uE945", "\uE83E", ColorHelper.FromArgb(255, 0, 230, 118)),
            HardwarePeekType.Battery => ("\uE850", "\uE7BA", ColorHelper.FromArgb(255, 255, 167, 38)),
            HardwarePeekType.CapsLock => ("\uE765", "\uE72E", ColorHelper.FromArgb(255, 171, 71, 188)),
            _ => ("\uE702", "\uE7F6", ColorHelper.FromArgb(255, 66, 165, 245))
        };

        var color = accentColor ?? defaultColor;
        PeekIcon.Glyph = iconGlyph;
        PeekRightIcon.Glyph = rightGlyph ?? defaultRightGlyph;
        PeekRightIcon.Foreground = new SolidColorBrush(color);
        PeekRippleInner.Stroke = new SolidColorBrush(color) { Opacity = 0.4 };
        PeekRippleOuter.Stroke = new SolidColorBrush(color) { Opacity = 0.2 };

        UpdatePeekProgress(percent01, color);
        TriggerPeekRippleAnimation();

        ApplyIslandState();

        _peekTimer?.Stop();
        _peekTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _peekTimer.Interval = TimeSpan.FromSeconds(3.5);
        _peekTimer.IsRepeating = false;
        _peekTimer.Tick += (_, _) =>
        {
            _peekTimer.Stop();
            _isPeekActive = false;
            ApplyIslandState();
        };
        _peekTimer.Start();
    }

    private void TriggerPeekRippleAnimation()
    {
        var sb = new Storyboard();
        var rippleAnim = new DoubleAnimation
        {
            From = 0.85,
            To = 1.15,
            Duration = TimeSpan.FromMilliseconds(650),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        if (PeekRippleOuter.RenderTransform is not ScaleTransform stOuter)
        {
            stOuter = new ScaleTransform { CenterX = 13, CenterY = 13 };
            PeekRippleOuter.RenderTransform = stOuter;
        }

        Storyboard.SetTarget(rippleAnim, stOuter);
        Storyboard.SetTargetProperty(rippleAnim, "ScaleX");
        sb.Children.Add(rippleAnim);

        var rippleY = new DoubleAnimation
        {
            From = 0.85,
            To = 1.15,
            Duration = TimeSpan.FromMilliseconds(650),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(rippleY, stOuter);
        Storyboard.SetTargetProperty(rippleY, "ScaleY");
        sb.Children.Add(rippleY);

        sb.Begin();
    }

    private void CheckCapsLockState()
    {
        try
        {
            var isCaps = (GetKeyState(VK_CAPITAL) & 0x0001) != 0;
            if (isCaps != _lastCapsLockState)
            {
                _lastCapsLockState = isCaps;
                ShowHardwarePeek(
                    HardwarePeekType.CapsLock,
                    "Bloq Mayús",
                    isCaps ? "ACTIVADO" : "DESACTIVADO",
                    percent01: isCaps ? 1.0 : 0.0);
            }
        }
        catch { }
    }

    private void DemoPeekBtn_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = false;
        _isPointerInside = false;
        ApplyIslandState();

        ShowHardwarePeek(
            HardwarePeekType.Headphones,
            "AirBuds Pro",
            "Connected · 70%",
            percent01: 0.70);
    }

    private static void AnimateViewEntrance(FrameworkElement view)
    {
        if (view is null) return;
        view.Opacity = 0.0;

        var tt = view.RenderTransform as TranslateTransform;
        if (tt is null)
        {
            tt = new TranslateTransform();
            view.RenderTransform = tt;
        }
        tt.Y = 6.0;

        var storyboard = new Storyboard();

        var opacityAnim = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(opacityAnim, view);
        Storyboard.SetTargetProperty(opacityAnim, "Opacity");
        storyboard.Children.Add(opacityAnim);

        var slideAnim = new DoubleAnimation
        {
            From = 6.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slideAnim, tt);
        Storyboard.SetTargetProperty(slideAnim, "Y");
        storyboard.Children.Add(slideAnim);

        storyboard.Begin();
    }

    private void AnimateTabPillPulse()
    {
        var sb = new Storyboard();
        var pulseX = new DoubleAnimation
        {
            From = 0.98,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var pulseY = new DoubleAnimation
        {
            From = 0.97,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(pulseX, IslandScale);
        Storyboard.SetTargetProperty(pulseX, "ScaleX");
        Storyboard.SetTarget(pulseY, IslandScale);
        Storyboard.SetTargetProperty(pulseY, "ScaleY");
        sb.Children.Add(pulseX);
        sb.Children.Add(pulseY);
        sb.Begin();
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

                if (_pomodoroState != PomodoroState.Running)
                {
                    UpdateCompactProgress(ratio, ColorHelper.FromArgb(255, 0, 230, 118));
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
        PopulatePomodoroTaskCombo();
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

    private void TabCalendarButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchTab(IslandTab.Calendar);
    }

    private void TabPomodoroButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchTab(IslandTab.Pomodoro);
    }

    private void SwitchTab(IslandTab tab)
    {
        _activeTab = tab;

        // Reset all tabs
        TabMediaButton.Background = new SolidColorBrush(Colors.Transparent);
        TabMediaLabel.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 160, 165, 178));
        MediaView.Visibility = Visibility.Collapsed;

        TabBacklogButton.Background = new SolidColorBrush(Colors.Transparent);
        TabBacklogLabel.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 160, 165, 178));
        BacklogView.Visibility = Visibility.Collapsed;

        TabCalendarButton.Background = new SolidColorBrush(Colors.Transparent);
        TabCalendarLabel.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 160, 165, 178));
        CalendarView.Visibility = Visibility.Collapsed;

        TabPomodoroButton.Background = new SolidColorBrush(Colors.Transparent);
        TabPomodoroLabel.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 160, 165, 178));
        PomodoroView.Visibility = Visibility.Collapsed;

        switch (_activeTab)
        {
            case IslandTab.Media:
                TabMediaButton.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 42, 45, 55));
                TabMediaLabel.Foreground = new SolidColorBrush(Colors.White);
                MediaView.Visibility = Visibility.Visible;
                AnimateViewEntrance(MediaView);
                break;

            case IslandTab.Backlog:
                TabBacklogButton.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 42, 45, 55));
                TabBacklogLabel.Foreground = new SolidColorBrush(Colors.White);
                BacklogView.Visibility = Visibility.Visible;
                AnimateViewEntrance(BacklogView);
                NewTaskInput.Focus(FocusState.Programmatic);
                break;

            case IslandTab.Calendar:
                TabCalendarButton.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 42, 45, 55));
                TabCalendarLabel.Foreground = new SolidColorBrush(Colors.White);
                CalendarView.Visibility = Visibility.Visible;
                AnimateViewEntrance(CalendarView);
                RenderCalendarView();
                break;

            case IslandTab.Pomodoro:
                TabPomodoroButton.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 42, 45, 55));
                TabPomodoroLabel.Foreground = new SolidColorBrush(Colors.White);
                PomodoroView.Visibility = Visibility.Visible;
                AnimateViewEntrance(PomodoroView);
                PopulatePomodoroTaskCombo();
                UpdatePomodoroUI();
                break;
        }

        AnimateTabPillPulse();
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

    // ==============================================================
    // SECCIÓN CALENDARIO, AGENDA Y REUNIONES
    // ==============================================================

    private async Task LoadCalendarEventsAsync()
    {
        try
        {
            var loaded = await _calendarStore.LoadAsync();
            _events.Clear();
            _events.AddRange(loaded);

            UpdateCalendarBadges();
            CheckUpcomingMeetings();
            RenderCalendarView();
        }
        catch
        {
        }
    }

    private void UpdateCalendarBadges()
    {
        var todayCount = _events.Count(e => e.StartTime.Date == DateTime.Today);
        HeaderCalendarCount.Text = todayCount.ToString();
    }

    private void CheckUpcomingMeetings()
    {
        var now = DateTimeOffset.Now;
        var upcoming = _events
            .Where(e => (now >= e.StartTime && now <= e.EndTime) || (e.StartTime > now && (e.StartTime - now) <= TimeSpan.FromMinutes(e.ReminderMinutes > 0 ? e.ReminderMinutes : 15)))
            .OrderBy(e => e.StartTime)
            .FirstOrDefault();

        if (upcoming is not null)
        {
            _upcomingMeetingUrl = upcoming.MeetingUrl;

            if (now >= upcoming.StartTime)
            {
                CompactMeetingText.Text = "EN VIVO";
                UpcomingBannerTitle.Text = $"🔴 En curso: {upcoming.Title}";
            }
            else
            {
                CompactMeetingText.Text = upcoming.StartTime.ToString("HH:mm");
                var mins = (int)Math.Max(1, Math.Ceiling((upcoming.StartTime - now).TotalMinutes));
                UpcomingBannerTitle.Text = $"Reunión en {mins} min: {upcoming.Title}";
            }

            CompactMeetingBadge.Visibility = Visibility.Visible;
            UpcomingMeetingBanner.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;
            UpcomingBannerJoinBtn.Visibility = !string.IsNullOrWhiteSpace(_upcomingMeetingUrl) ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            CompactMeetingBadge.Visibility = Visibility.Collapsed;
            UpcomingMeetingBanner.Visibility = Visibility.Collapsed;
            _upcomingMeetingUrl = null;
        }
    }

    private async void UpcomingBannerJoinBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_upcomingMeetingUrl) && Uri.TryCreate(_upcomingMeetingUrl, UriKind.Absolute, out var uri))
        {
            try
            {
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
            catch
            {
            }
        }
    }

    private void CalSubAgendaBtn_Click(object sender, RoutedEventArgs e)
    {
        SetCalendarSubMode(CalendarSubMode.Agenda);
    }

    private void CalSubWeekBtn_Click(object sender, RoutedEventArgs e)
    {
        SetCalendarSubMode(CalendarSubMode.Week);
    }

    private void CalSubMonthBtn_Click(object sender, RoutedEventArgs e)
    {
        SetCalendarSubMode(CalendarSubMode.Month);
    }

    private void SetCalendarSubMode(CalendarSubMode mode)
    {
        _calendarSubMode = mode;

        CalSubAgendaBtn.Background = new SolidColorBrush(_calendarSubMode == CalendarSubMode.Agenda ? ColorHelper.FromArgb(255, 42, 45, 55) : Colors.Transparent);
        CalSubAgendaLabel.Foreground = new SolidColorBrush(_calendarSubMode == CalendarSubMode.Agenda ? Colors.White : ColorHelper.FromArgb(255, 160, 165, 178));

        CalSubWeekBtn.Background = new SolidColorBrush(_calendarSubMode == CalendarSubMode.Week ? ColorHelper.FromArgb(255, 42, 45, 55) : Colors.Transparent);
        CalSubWeekLabel.Foreground = new SolidColorBrush(_calendarSubMode == CalendarSubMode.Week ? Colors.White : ColorHelper.FromArgb(255, 160, 165, 178));

        CalSubMonthBtn.Background = new SolidColorBrush(_calendarSubMode == CalendarSubMode.Month ? ColorHelper.FromArgb(255, 42, 45, 55) : Colors.Transparent);
        CalSubMonthLabel.Foreground = new SolidColorBrush(_calendarSubMode == CalendarSubMode.Month ? Colors.White : ColorHelper.FromArgb(255, 160, 165, 178));

        CalAgendaContainer.Visibility = _calendarSubMode == CalendarSubMode.Agenda ? Visibility.Visible : Visibility.Collapsed;
        CalWeekContainer.Visibility = _calendarSubMode == CalendarSubMode.Week ? Visibility.Visible : Visibility.Collapsed;
        CalMonthContainer.Visibility = _calendarSubMode == CalendarSubMode.Month ? Visibility.Visible : Visibility.Collapsed;
        CalEventFormOverlay.Visibility = Visibility.Collapsed;
        CalSyncOverlay.Visibility = Visibility.Collapsed;

        if (_calendarSubMode == CalendarSubMode.Agenda) AnimateViewEntrance(CalAgendaContainer);
        else if (_calendarSubMode == CalendarSubMode.Week) AnimateViewEntrance(CalWeekContainer);
        else if (_calendarSubMode == CalendarSubMode.Month) AnimateViewEntrance(CalMonthContainer);

        RenderCalendarView();
    }

    private void CalPrevDateBtn_Click(object sender, RoutedEventArgs e)
    {
        switch (_calendarSubMode)
        {
            case CalendarSubMode.Agenda:
                _selectedCalendarDate = _selectedCalendarDate.AddDays(-1);
                break;
            case CalendarSubMode.Week:
                _selectedCalendarDate = _selectedCalendarDate.AddDays(-7);
                break;
            case CalendarSubMode.Month:
                _selectedMonth = _selectedMonth.AddMonths(-1);
                break;
        }
        RenderCalendarView();
    }

    private void CalNextDateBtn_Click(object sender, RoutedEventArgs e)
    {
        switch (_calendarSubMode)
        {
            case CalendarSubMode.Agenda:
                _selectedCalendarDate = _selectedCalendarDate.AddDays(1);
                break;
            case CalendarSubMode.Week:
                _selectedCalendarDate = _selectedCalendarDate.AddDays(7);
                break;
            case CalendarSubMode.Month:
                _selectedMonth = _selectedMonth.AddMonths(1);
                break;
        }
        RenderCalendarView();
    }

    private void RenderCalendarView()
    {
        var culture = CultureInfo.GetCultureInfo("es-CL");

        switch (_calendarSubMode)
        {
            case CalendarSubMode.Agenda:
                if (_selectedCalendarDate.Date == DateTime.Today)
                    CalCurrentTitleLabel.Text = "Hoy, " + _selectedCalendarDate.ToString("d MMM", culture);
                else
                    CalCurrentTitleLabel.Text = _selectedCalendarDate.ToString("ddd d MMM", culture);

                RenderAgendaEvents(AgendaEventsList, _selectedCalendarDate.Date);
                break;

            case CalendarSubMode.Week:
                var startOfWeek = GetStartOfWeek(_selectedCalendarDate.Date);
                var endOfWeek = startOfWeek.AddDays(6);
                CalCurrentTitleLabel.Text = $"{startOfWeek:d MMM} - {endOfWeek:d MMM}";
                RenderWeekStrip();
                RenderAgendaEvents(WeekEventsList, _selectedCalendarDate.Date);
                break;

            case CalendarSubMode.Month:
                CalCurrentTitleLabel.Text = _selectedMonth.ToString("MMMM yyyy", culture);
                RenderMonthGrid();
                break;
        }
    }

    private static DateTime GetStartOfWeek(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-1 * diff).Date;
    }

    private void RenderAgendaEvents(StackPanel container, DateTime targetDate)
    {
        container.Children.Clear();

        var dayEvents = _events
            .Where(e => e.StartTime.Date == targetDate)
            .OrderBy(e => e.StartTime)
            .ToList();

        if (dayEvents.Count == 0)
        {
            container.Children.Add(new TextBlock
            {
                Text = "📅 Sin eventos programados para este día",
                FontSize = 9.5,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(160, 133, 138, 150)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 16, 0, 0)
            });
            return;
        }

        foreach (var ev in dayEvents)
        {
            var conflicts = CalendarEventStore.FindConflicts(ev, _events);
            var isConflicted = conflicts.Count > 0;

            var card = new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(200, 26, 28, 35)),
                BorderBrush = isConflicted
                    ? new SolidColorBrush(ColorHelper.FromArgb(180, 255, 167, 38))
                    : new SolidColorBrush(ColorHelper.FromArgb(35, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 4, 6, 4)
            };

            var grid = new Grid { ColumnSpacing = 6 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Col 0: Horario
            var timePill = new Border
            {
                Background = ev.IsHappeningNow()
                    ? new SolidColorBrush(ColorHelper.FromArgb(50, 0, 230, 118))
                    : new SolidColorBrush(ColorHelper.FromArgb(255, 20, 22, 28)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = ev.IsAllDay ? "Todo el día" : $"{ev.StartTime:HH:mm}-{ev.EndTime:HH:mm}",
                    FontSize = 8.5,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = ev.IsHappeningNow()
                        ? new SolidColorBrush(ColorHelper.FromArgb(255, 0, 230, 118))
                        : new SolidColorBrush(ColorHelper.FromArgb(255, 180, 185, 195))
                }
            };
            Grid.SetColumn(timePill, 0);
            grid.Children.Add(timePill);

            // Col 1: Categoría
            var (bgBrush, fgBrush) = GetEventCategoryBrushes(ev.Category);
            var catBadge = new Border
            {
                Background = bgBrush,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(3, 1, 3, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = ev.Category,
                    FontSize = 7.5,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = fgBrush
                }
            };
            Grid.SetColumn(catBadge, 1);
            grid.Children.Add(catBadge);

            // Col 2: Info (Título, Origen, Backlog Link, Conflicto)
            var infoStack = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            var titleBlock = new TextBlock
            {
                Text = ev.Title,
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                Foreground = new SolidColorBrush(Colors.White),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            };
            infoStack.Children.Add(titleBlock);

            var metaStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

            // Badge de origen (Google, Outlook, Local)
            var sourceText = new TextBlock
            {
                Text = ev.Source,
                FontSize = 7.5,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(180, 120, 125, 138))
            };
            metaStack.Children.Add(sourceText);

            // Badge de Backlog vinculado
            if (!string.IsNullOrWhiteSpace(ev.LinkedTaskTitle))
            {
                var linkBadge = new Border
                {
                    Background = new SolidColorBrush(ColorHelper.FromArgb(40, 0, 230, 118)),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(3, 0, 3, 0),
                    Child = new TextBlock
                    {
                        Text = $"⚡ {ev.LinkedTaskTitle}",
                        FontSize = 7.5,
                        Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 0, 230, 118)),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = 100
                    }
                };
                metaStack.Children.Add(linkBadge);
            }

            // Indicador de Conflicto de horario
            if (isConflicted)
            {
                var conflictBadge = new Border
                {
                    Background = new SolidColorBrush(ColorHelper.FromArgb(50, 255, 167, 38)),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(3, 0, 3, 0),
                    Child = new TextBlock
                    {
                        Text = "⚠️ Solapado",
                        FontSize = 7.5,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 255, 167, 38))
                    }
                };
                metaStack.Children.Add(conflictBadge);
            }

            infoStack.Children.Add(metaStack);
            Grid.SetColumn(infoStack, 2);
            grid.Children.Add(infoStack);

            // Col 3: Acciones rápidas (Unirse a videollamada, editar, borrar)
            var actionsStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };

            if (!string.IsNullOrWhiteSpace(ev.MeetingUrl))
            {
                var joinBtn = new Button
                {
                    Height = 20,
                    Padding = new Thickness(5, 0, 5, 0),
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0, 230, 118)),
                    Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 10, 11, 14)),
                    BorderThickness = new Thickness(0),
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 2,
                        Children =
                        {
                            new FontIcon { Glyph = "\uE717", FontSize = 8, FontWeight = Microsoft.UI.Text.FontWeights.Bold },
                            new TextBlock { Text = "Unirse", FontSize = 8.5, FontWeight = Microsoft.UI.Text.FontWeights.Bold }
                        }
                    }
                };
                joinBtn.Click += async (_, _) =>
                {
                    if (Uri.TryCreate(ev.MeetingUrl, UriKind.Absolute, out var uri))
                    {
                        try { await Windows.System.Launcher.LaunchUriAsync(uri); } catch { }
                    }
                };
                actionsStack.Children.Add(joinBtn);
            }

            var editBtn = new Button
            {
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Content = new FontIcon { Glyph = "\uE70F", FontSize = 8, Foreground = new SolidColorBrush(ColorHelper.FromArgb(180, 140, 145, 155)) }
            };
            editBtn.Click += (_, _) => OpenEditForm(ev);
            actionsStack.Children.Add(editBtn);

            var deleteBtn = new Button
            {
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 8, Foreground = new SolidColorBrush(ColorHelper.FromArgb(180, 239, 83, 80)) }
            };
            deleteBtn.Click += (_, _) =>
            {
                _events.Remove(ev);
                _ = _calendarStore.SaveAsync(_events);
                UpdateCalendarBadges();
                CheckUpcomingMeetings();
                RenderCalendarView();
            };
            actionsStack.Children.Add(deleteBtn);

            Grid.SetColumn(actionsStack, 3);
            grid.Children.Add(actionsStack);

            card.Child = grid;
            container.Children.Add(card);
        }
    }

    private void RenderWeekStrip()
    {
        WeekStripGrid.Children.Clear();
        WeekStripGrid.ColumnDefinitions.Clear();

        var startOfWeek = GetStartOfWeek(_selectedCalendarDate.Date);

        for (int i = 0; i < 7; i++)
        {
            WeekStripGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var dayDate = startOfWeek.AddDays(i);
            var isSelected = dayDate.Date == _selectedCalendarDate.Date;
            var isToday = dayDate.Date == DateTime.Today;
            var eventsCount = _events.Count(e => e.StartTime.Date == dayDate.Date);

            var btn = new Button
            {
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                Background = isSelected
                    ? new SolidColorBrush(ColorHelper.FromArgb(255, 42, 45, 55))
                    : new SolidColorBrush(Colors.Transparent),
                BorderBrush = isToday
                    ? new SolidColorBrush(ColorHelper.FromArgb(255, 0, 230, 118))
                    : (isSelected ? new SolidColorBrush(ColorHelper.FromArgb(60, 255, 255, 255)) : new SolidColorBrush(Colors.Transparent)),
                BorderThickness = new Thickness(1)
            };

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var culture = CultureInfo.GetCultureInfo("es-CL");
            var dayLetter = dayDate.ToString("ddd", culture)[..1].ToUpperInvariant();
            stack.Children.Add(new TextBlock
            {
                Text = $"{dayLetter} {dayDate.Day}",
                FontSize = 8.5,
                FontWeight = isSelected || isToday ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = isToday
                    ? new SolidColorBrush(ColorHelper.FromArgb(255, 0, 230, 118))
                    : new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            if (eventsCount > 0)
            {
                var dot = new Ellipse
                {
                    Width = 3,
                    Height = 3,
                    Fill = new SolidColorBrush(ColorHelper.FromArgb(255, 66, 165, 245)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 1, 0, 0)
                };
                stack.Children.Add(dot);
            }

            btn.Content = stack;
            btn.Click += (_, _) =>
            {
                _selectedCalendarDate = dayDate;
                RenderCalendarView();
            };

            Grid.SetColumn(btn, i);
            WeekStripGrid.Children.Add(btn);
        }
    }

    private void RenderMonthGrid()
    {
        MonthDayHeadersGrid.Children.Clear();
        MonthDayHeadersGrid.ColumnDefinitions.Clear();
        MonthDaysGrid.Children.Clear();
        MonthDaysGrid.ColumnDefinitions.Clear();
        MonthDaysGrid.RowDefinitions.Clear();

        string[] headers = ["D", "L", "M", "M", "J", "V", "S"];
        for (int i = 0; i < 7; i++)
        {
            MonthDayHeadersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            MonthDaysGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var headerBlock = new TextBlock
            {
                Text = headers[i],
                FontSize = 8,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(160, 140, 145, 155)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(headerBlock, i);
            MonthDayHeadersGrid.Children.Add(headerBlock);
        }

        for (int r = 0; r < 6; r++)
        {
            MonthDaysGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        var firstDayOfMonth = new DateTime(_selectedMonth.Year, _selectedMonth.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(_selectedMonth.Year, _selectedMonth.Month);
        var startOffset = (int)firstDayOfMonth.DayOfWeek; // 0 = Domingo

        for (int day = 1; day <= daysInMonth; day++)
        {
            var cellIndex = startOffset + day - 1;
            var row = cellIndex / 7;
            var col = cellIndex % 7;
            if (row >= 6) break;

            var thisDate = new DateTime(_selectedMonth.Year, _selectedMonth.Month, day);
            var isToday = thisDate == DateTime.Today;
            var isSelected = thisDate == _selectedCalendarDate.Date;
            var dayEvents = _events.Where(e => e.StartTime.Date == thisDate).ToList();

            var cellBtn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(3),
                Background = isSelected
                    ? new SolidColorBrush(ColorHelper.FromArgb(255, 42, 45, 55))
                    : new SolidColorBrush(Colors.Transparent),
                BorderBrush = isToday
                    ? new SolidColorBrush(ColorHelper.FromArgb(255, 0, 230, 118))
                    : new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(1)
            };

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = day.ToString(),
                FontSize = 8,
                FontWeight = isToday || isSelected ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = isToday
                    ? new SolidColorBrush(ColorHelper.FromArgb(255, 0, 230, 118))
                    : new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            if (dayEvents.Count > 0)
            {
                var dot = new Ellipse
                {
                    Width = 2.5,
                    Height = 2.5,
                    Fill = new SolidColorBrush(ColorHelper.FromArgb(255, 66, 165, 245)),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                stack.Children.Add(dot);
            }

            cellBtn.Content = stack;
            cellBtn.Click += (_, _) =>
            {
                _selectedCalendarDate = thisDate;
                SetCalendarSubMode(CalendarSubMode.Agenda);
            };

            Grid.SetRow(cellBtn, row);
            Grid.SetColumn(cellBtn, col);
            MonthDaysGrid.Children.Add(cellBtn);
        }
    }

    private static (SolidColorBrush Bg, SolidColorBrush Fg) GetEventCategoryBrushes(string category)
    {
        return category switch
        {
            "MEETING" => (
                new SolidColorBrush(ColorHelper.FromArgb(40, 33, 150, 243)),
                new SolidColorBrush(ColorHelper.FromArgb(255, 66, 165, 245))),
            "REVIEW" => (
                new SolidColorBrush(ColorHelper.FromArgb(40, 171, 71, 188)),
                new SolidColorBrush(ColorHelper.FromArgb(255, 171, 71, 188))),
            "PLANNING" => (
                new SolidColorBrush(ColorHelper.FromArgb(40, 255, 167, 38)),
                new SolidColorBrush(ColorHelper.FromArgb(255, 255, 167, 38))),
            "DEADLINE" => (
                new SolidColorBrush(ColorHelper.FromArgb(40, 239, 83, 80)),
                new SolidColorBrush(ColorHelper.FromArgb(255, 239, 83, 80))),
            _ => (
                new SolidColorBrush(ColorHelper.FromArgb(40, 38, 166, 154)),
                new SolidColorBrush(ColorHelper.FromArgb(255, 38, 166, 154)))
        };
    }

    private void CalNewEventBtn_Click(object sender, RoutedEventArgs e)
    {
        _editingEvent = null;
        OpenCreateForm();
    }

    private void OpenCreateForm()
    {
        FormEventTitle.Text = string.Empty;
        FormEventDate.Text = _selectedCalendarDate.ToString("yyyy-MM-dd");
        FormStartTime.Text = DateTime.Now.ToString("HH:00");
        FormEndTime.Text = DateTime.Now.AddHours(1).ToString("HH:00");
        FormMeetingUrl.Text = string.Empty;
        _currentCategoryIndex = 0;
        FormCategoryLabel.Text = EventCategories[_currentCategoryIndex];
        var (_, fg) = GetEventCategoryBrushes(FormCategoryLabel.Text);
        FormCategoryLabel.Foreground = fg;

        PopulateTaskLinkCombo();
        ValidateFormConflicts();

        CalEventFormOverlay.Visibility = Visibility.Visible;
        CalSyncOverlay.Visibility = Visibility.Collapsed;
        AnimateViewEntrance(CalEventFormOverlay);
        FormEventTitle.Focus(FocusState.Programmatic);
    }

    private void OpenEditForm(CalendarEvent ev)
    {
        _editingEvent = ev;
        FormEventTitle.Text = ev.Title;
        FormEventDate.Text = ev.StartTime.ToString("yyyy-MM-dd");
        FormStartTime.Text = ev.StartTime.ToString("HH:mm");
        FormEndTime.Text = ev.EndTime.ToString("HH:mm");
        FormMeetingUrl.Text = ev.MeetingUrl ?? string.Empty;

        var catIndex = Array.IndexOf(EventCategories, ev.Category);
        _currentCategoryIndex = catIndex >= 0 ? catIndex : 0;
        FormCategoryLabel.Text = EventCategories[_currentCategoryIndex];
        var (_, fg) = GetEventCategoryBrushes(FormCategoryLabel.Text);
        FormCategoryLabel.Foreground = fg;

        PopulateTaskLinkCombo(ev.LinkedTaskId);
        ValidateFormConflicts();

        CalEventFormOverlay.Visibility = Visibility.Visible;
        CalSyncOverlay.Visibility = Visibility.Collapsed;
        AnimateViewEntrance(CalEventFormOverlay);
        FormEventTitle.Focus(FocusState.Programmatic);
    }

    private void PopulateTaskLinkCombo(string? selectedTaskId = null)
    {
        FormTaskLinkCombo.Items.Clear();
        FormTaskLinkCombo.Items.Add(new ComboBoxItem { Content = "Sin vincular a backlog", Tag = null });

        var selectIndex = 0;
        var idx = 1;

        foreach (var task in _tasks.Where(t => !t.IsCompleted))
        {
            var item = new ComboBoxItem
            {
                Content = $"⚡ [{task.Category}] {task.Title}",
                Tag = task.Id
            };
            FormTaskLinkCombo.Items.Add(item);

            if (selectedTaskId is not null && task.Id == selectedTaskId)
            {
                selectIndex = idx;
            }
            idx++;
        }

        FormTaskLinkCombo.SelectedIndex = selectIndex;
    }

    private void FormCategoryBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentCategoryIndex = (_currentCategoryIndex + 1) % EventCategories.Length;
        FormCategoryLabel.Text = EventCategories[_currentCategoryIndex];
        var (_, fg) = GetEventCategoryBrushes(FormCategoryLabel.Text);
        FormCategoryLabel.Foreground = fg;
    }

    private void FormTime_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateFormConflicts();
    }

    private void ValidateFormConflicts()
    {
        if (CalEventFormOverlay.Visibility != Visibility.Visible) return;

        if (TryParseFormTimes(out var start, out var end))
        {
            var temp = new CalendarEvent
            {
                Id = _editingEvent?.Id ?? Guid.NewGuid().ToString("N"),
                StartTime = start,
                EndTime = end
            };

            var conflicts = CalendarEventStore.FindConflicts(temp, _events);
            if (conflicts.Count > 0)
            {
                FormConflictWarning.Text = $"⚠️ Conflicto con: \"{conflicts[0].Title}\" ({conflicts[0].StartTime:HH:mm}-{conflicts[0].EndTime:HH:mm})";
            }
            else
            {
                FormConflictWarning.Text = string.Empty;
            }
        }
        else
        {
            FormConflictWarning.Text = string.Empty;
        }
    }

    private bool TryParseFormTimes(out DateTimeOffset start, out DateTimeOffset end)
    {
        start = DateTimeOffset.MinValue;
        end = DateTimeOffset.MinValue;

        if (!DateTime.TryParse(FormEventDate.Text, out var date)) return false;
        if (!TimeSpan.TryParse(FormStartTime.Text, out var startTime)) return false;
        if (!TimeSpan.TryParse(FormEndTime.Text, out var endTime)) return false;

        var startDt = date.Date + startTime;
        var endDt = date.Date + endTime;
        if (endDt <= startDt) endDt = startDt.AddMinutes(30);

        start = new DateTimeOffset(startDt, DateTimeOffset.Now.Offset);
        end = new DateTimeOffset(endDt, DateTimeOffset.Now.Offset);
        return true;
    }

    private void FormSaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var title = FormEventTitle.Text.Trim();
        if (string.IsNullOrWhiteSpace(title)) return;

        if (!TryParseFormTimes(out var start, out var end))
        {
            start = _selectedCalendarDate;
            end = _selectedCalendarDate.AddMinutes(30);
        }

        var meetingUrl = FormMeetingUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(meetingUrl))
        {
            meetingUrl = CalendarEvent.TryExtractMeetingUrl(title) ?? string.Empty;
        }

        string? linkedTaskId = null;
        string? linkedTaskTitle = null;

        if (FormTaskLinkCombo.SelectedItem is ComboBoxItem comboItem && comboItem.Tag is string taskId)
        {
            var linked = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (linked is not null)
            {
                linkedTaskId = linked.Id;
                linkedTaskTitle = linked.Title;
            }
        }

        if (_editingEvent is not null)
        {
            _editingEvent.Title = title;
            _editingEvent.StartTime = start;
            _editingEvent.EndTime = end;
            _editingEvent.MeetingUrl = string.IsNullOrWhiteSpace(meetingUrl) ? null : meetingUrl;
            _editingEvent.Category = FormCategoryLabel.Text;
            _editingEvent.LinkedTaskId = linkedTaskId;
            _editingEvent.LinkedTaskTitle = linkedTaskTitle;
        }
        else
        {
            var newEv = new CalendarEvent
            {
                Title = title,
                StartTime = start,
                EndTime = end,
                MeetingUrl = string.IsNullOrWhiteSpace(meetingUrl) ? null : meetingUrl,
                Category = FormCategoryLabel.Text,
                LinkedTaskId = linkedTaskId,
                LinkedTaskTitle = linkedTaskTitle,
                Source = "LOCAL"
            };
            _events.Add(newEv);
        }

        _ = _calendarStore.SaveAsync(_events);
        CalEventFormOverlay.Visibility = Visibility.Collapsed;

        UpdateCalendarBadges();
        CheckUpcomingMeetings();
        RenderCalendarView();
    }

    private void FormCancelBtn_Click(object sender, RoutedEventArgs e)
    {
        CalEventFormOverlay.Visibility = Visibility.Collapsed;
    }

    private DispatcherQueueTimer? _syncStatusTimer;

    private void CalSyncBtn_Click(object sender, RoutedEventArgs e)
    {
        if (CalSyncOverlay.Visibility == Visibility.Visible)
        {
            CalSyncOverlay.Visibility = Visibility.Collapsed;
        }
        else
        {
            CalEventFormOverlay.Visibility = Visibility.Collapsed;
            CalSyncOverlay.Visibility = Visibility.Visible;
            SyncStatusMsg.Visibility = Visibility.Collapsed;
            AnimateViewEntrance(CalSyncOverlay);
        }
    }

    private void CloseSyncBtn_Click(object sender, RoutedEventArgs e)
    {
        CalSyncOverlay.Visibility = Visibility.Collapsed;
    }

    private async void ImportIcsFileBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
            picker.FileTypeFilter.Add(".ics");
            picker.FileTypeFilter.Add(".ical");
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, _hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            var content = await Windows.Storage.FileIO.ReadTextAsync(file);
            await ProcessIcsImportAsync(content, file.Name);
        }
        catch (Exception ex)
        {
            ShowSyncStatus($"Error al seleccionar archivo: {ex.Message}", isError: true);
        }
    }

    private async void ImportClipboardBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dataPackage = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (dataPackage.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                var text = await dataPackage.GetTextAsync();
                if (string.IsNullOrWhiteSpace(text))
                {
                    ShowSyncStatus("El portapapeles está vacío", isError: true);
                    return;
                }

                var trimmed = text.Trim();
                if (trimmed.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase) ||
                    (trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase) && (trimmed.EndsWith(".ics", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("calendar"))))
                {
                    var url = trimmed.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)
                        ? "https://" + trimmed[9..]
                        : trimmed;

                    ShowSyncStatus("Descargando calendario desde URL...", isError: false);
                    using var client = new System.Net.Http.HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var downloaded = await client.GetStringAsync(url);
                    await ProcessIcsImportAsync(downloaded, "Enlace de Calendario");
                    return;
                }

                await ProcessIcsImportAsync(text, "Portapapeles");
            }
            else
            {
                ShowSyncStatus("No se encontró texto o archivo en el portapapeles", isError: true);
            }
        }
        catch (Exception ex)
        {
            ShowSyncStatus($"Error al importar: {ex.Message}", isError: true);
        }
    }

    private async Task ProcessIcsImportAsync(string icsText, string sourceName)
    {
        try
        {
            var imported = IcsCalendarHelper.ParseIcs(icsText);
            if (imported.Count == 0)
            {
                ShowSyncStatus("No se detectaron eventos válidos en formato .ics", isError: true);
                return;
            }

            var added = 0;
            foreach (var ev in imported)
            {
                var exists = _events.Any(e =>
                    (!string.IsNullOrWhiteSpace(e.Id) && !string.IsNullOrWhiteSpace(ev.Id) && e.Id == ev.Id) ||
                    (e.Title.Equals(ev.Title, StringComparison.OrdinalIgnoreCase) && e.StartTime == ev.StartTime));

                if (!exists)
                {
                    _events.Add(ev);
                    added++;
                }
            }

            if (added > 0)
            {
                await _calendarStore.SaveAsync(_events);
                UpdateCalendarBadges();
                CheckUpcomingMeetings();
                RenderCalendarView();
                ShowSyncStatus($"✓ ¡{added} reunión(es) importada(s) con éxito!", isError: false);
            }
            else
            {
                ShowSyncStatus($"Todos los eventos ({imported.Count}) ya estaban agregados", isError: false);
            }
        }
        catch (Exception ex)
        {
            ShowSyncStatus($"Error al importar: {ex.Message}", isError: true);
        }
    }

    private void ShowSyncStatus(string message, bool isError)
    {
        SyncStatusMsg.Text = message;
        SyncStatusMsg.Foreground = new SolidColorBrush(isError
            ? ColorHelper.FromArgb(255, 255, 167, 38)
            : ColorHelper.FromArgb(255, 0, 230, 118));
        SyncStatusMsg.Visibility = Visibility.Visible;

        _syncStatusTimer?.Stop();
        _syncStatusTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _syncStatusTimer.Interval = TimeSpan.FromSeconds(3.5);
        _syncStatusTimer.IsRepeating = false;
        _syncStatusTimer.Tick += (_, _) =>
        {
            SyncStatusMsg.Visibility = Visibility.Collapsed;
            _syncStatusTimer.Stop();
        };
        _syncStatusTimer.Start();
    }

    private async void ExportIcsFileBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ics = IcsCalendarHelper.ToIcs(_events);
            var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlexIsland");
            Directory.CreateDirectory(folder);
            var filePath = System.IO.Path.Combine(folder, "alex_island_calendar.ics");

            await File.WriteAllTextAsync(filePath, ics);

            ExportIcsLabel.Text = "¡Exportado!";
            ShowSyncStatus("✓ Calendario exportado y abierto en Outlook", isError: false);

            var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(2);
            timer.IsRepeating = false;
            timer.Tick += (_, _) =>
            {
                ExportIcsLabel.Text = "Abrir en Outlook";
                timer.Stop();
            };
            timer.Start();

            // Abrir archivo .ics con el cliente predeterminado (Outlook / Calendario de Windows)
            await Windows.System.Launcher.LaunchUriAsync(new Uri(filePath));
        }
        catch (Exception ex)
        {
            ShowSyncStatus($"Error al exportar: {ex.Message}", isError: true);
        }
    }

    private void CopyIcsBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ics = IcsCalendarHelper.ToIcs(_events);
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(ics);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);

            CopyIcsLabel.Text = "¡Copiado!";
            ShowSyncStatus("✓ Archivo .ics copiado para Google Calendar", isError: false);

            var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(2);
            timer.IsRepeating = false;
            timer.Tick += (_, _) =>
            {
                CopyIcsLabel.Text = "Copiar .ics (Google)";
                timer.Stop();
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            ShowSyncStatus($"Error al copiar: {ex.Message}", isError: true);
        }
    }

    // ==============================================================
    // SECCIÓN TEMPORIZADOR POMODORO / DEEP WORK
    // ==============================================================

    private async Task LoadPomodoroAsync()
    {
        try
        {
            _pomodoroSettings = await _pomodoroStore.LoadAsync();
            SetPomodoroMode(PomodoroMode.Work, resetTimer: true);
            UpdatePomodoroUI();
        }
        catch
        {
        }
    }

    private void SetPomodoroMode(PomodoroMode mode, bool resetTimer = true)
    {
        _pomodoroMode = mode;

        var mins = _pomodoroMode switch
        {
            PomodoroMode.ShortBreak => _pomodoroSettings.ShortBreakMinutes,
            PomodoroMode.LongBreak => _pomodoroSettings.LongBreakMinutes,
            _ => _pomodoroSettings.WorkMinutes
        };

        _pomodoroTotalDuration = TimeSpan.FromMinutes(mins);
        if (resetTimer)
        {
            _pomodoroRemaining = _pomodoroTotalDuration;
            _pomodoroState = PomodoroState.Stopped;
            _pomodoroTimer.Stop();
        }

        UpdatePomodoroModeVisuals();
        UpdatePomodoroUI();
    }

    private void UpdatePomodoroModeVisuals()
    {
        PomoModeWorkBtn.Background = new SolidColorBrush(_pomodoroMode == PomodoroMode.Work ? ColorHelper.FromArgb(255, 42, 45, 55) : Colors.Transparent);
        PomoModeWorkLabel.Foreground = new SolidColorBrush(_pomodoroMode == PomodoroMode.Work ? Colors.White : ColorHelper.FromArgb(255, 160, 165, 178));

        PomoModeShortBtn.Background = new SolidColorBrush(_pomodoroMode == PomodoroMode.ShortBreak ? ColorHelper.FromArgb(255, 42, 45, 55) : Colors.Transparent);
        PomoModeShortLabel.Foreground = new SolidColorBrush(_pomodoroMode == PomodoroMode.ShortBreak ? Colors.White : ColorHelper.FromArgb(255, 160, 165, 178));

        PomoModeLongBtn.Background = new SolidColorBrush(_pomodoroMode == PomodoroMode.LongBreak ? ColorHelper.FromArgb(255, 42, 45, 55) : Colors.Transparent);
        PomoModeLongLabel.Foreground = new SolidColorBrush(_pomodoroMode == PomodoroMode.LongBreak ? Colors.White : ColorHelper.FromArgb(255, 160, 165, 178));

        var accentColor = _pomodoroMode == PomodoroMode.Work
            ? ColorHelper.FromArgb(255, 239, 83, 80) // Coral Red
            : ColorHelper.FromArgb(255, 0, 230, 118); // Emerald Green

        PomoPlayPauseBtn.Background = new SolidColorBrush(accentColor);
        PomodoroProgressBar.Background = new SolidColorBrush(accentColor);
    }

    private void UpdatePomodoroUI()
    {
        // Texto del temporizador grande
        PomodoroTimerDisplay.Text = $"{_pomodoroRemaining:mm\\:ss}";

        // Subtítulo
        PomodoroStateSubtitle.Text = _pomodoroMode switch
        {
            PomodoroMode.ShortBreak => "Pausa de Descanso Corto",
            PomodoroMode.LongBreak => "Descanso Largo Reparador",
            _ => (_pomodoroState == PomodoroState.Running ? "Enfoque Activo" : "Sesión de Concentración")
        };

        // Ícono de Play / Pausa
        PomoPlayPauseIcon.Glyph = _pomodoroState == PomodoroState.Running ? "\uE769" : "\uE768";

        // Contador de hoy
        PomodoroCompletedCountLabel.Text = $"{_pomodoroSettings.CompletedToday} hoy";
        HeaderPomodoroCount.Text = _pomodoroSettings.CompletedToday.ToString();

        // Barra de progreso
        UpdatePomodoroProgressBar();

        // Modo compacto
        if (_pomodoroState != PomodoroState.Stopped)
        {
            CompactPomodoroBadge.Visibility = Visibility.Visible;
            CompactPomodoroIcon.Text = _pomodoroMode == PomodoroMode.Work ? "🍅" : "☕";
            CompactPomodoroText.Text = $"{_pomodoroRemaining:mm\\:ss}";

            var color = _pomodoroMode == PomodoroMode.Work
                ? ColorHelper.FromArgb(255, 239, 83, 80)
                : ColorHelper.FromArgb(255, 0, 230, 118);
            CompactPomodoroText.Foreground = new SolidColorBrush(color);
            CompactPomodoroBadge.BorderBrush = new SolidColorBrush(color);

            if (_pomodoroTotalDuration > TimeSpan.Zero)
            {
                var elapsedSeconds = (_pomodoroTotalDuration - _pomodoroRemaining).TotalSeconds;
                var ratio = Math.Clamp(elapsedSeconds / _pomodoroTotalDuration.TotalSeconds, 0, 1);
                UpdateCompactProgress(ratio, color);
            }
        }
        else
        {
            CompactPomodoroBadge.Visibility = Visibility.Collapsed;
            if (!_isPlaying)
            {
                UpdateCompactProgress(0.0);
            }
        }
    }

    private void UpdatePomodoroProgressBar()
    {
        if (_pomodoroTotalDuration <= TimeSpan.Zero) return;
        var trackWidth = PomodoroTrackGrid.ActualWidth;
        if (trackWidth <= 0) return;

        var elapsedSeconds = (_pomodoroTotalDuration - _pomodoroRemaining).TotalSeconds;
        var ratio = Math.Clamp(elapsedSeconds / _pomodoroTotalDuration.TotalSeconds, 0, 1);
        PomodoroProgressBar.Width = trackWidth * ratio;
    }

    private void PomodoroTrackGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePomodoroProgressBar();
    }

    private void PomodoroTimer_Tick()
    {
        if (_pomodoroState != PomodoroState.Running) return;

        if (_pomodoroRemaining > TimeSpan.Zero)
        {
            _pomodoroRemaining = _pomodoroRemaining.Subtract(TimeSpan.FromSeconds(1));
            UpdatePomodoroUI();
        }

        if (_pomodoroRemaining <= TimeSpan.Zero)
        {
            OnPomodoroIntervalFinished();
        }
    }

    private void OnPomodoroIntervalFinished()
    {
        _pomodoroTimer.Stop();
        _pomodoroState = PomodoroState.Stopped;

        // Alerta sonora
        try
        {
            Console.Beep(880, 250);
        }
        catch
        {
        }

        if (_pomodoroMode == PomodoroMode.Work)
        {
            _pomodoroSettings.CompletedToday++;
            _ = _pomodoroStore.SaveAsync(_pomodoroSettings);

            // Si se completaron 4 bloques, descanso largo
            if (_pomodoroSettings.CompletedToday % _pomodoroSettings.LongBreakInterval == 0)
            {
                SetPomodoroMode(PomodoroMode.LongBreak, resetTimer: true);
            }
            else
            {
                SetPomodoroMode(PomodoroMode.ShortBreak, resetTimer: true);
            }
        }
        else
        {
            // Termina el descanso -> volver a trabajo
            SetPomodoroMode(PomodoroMode.Work, resetTimer: true);
        }

        UpdatePomodoroUI();
    }

    private void PomoPlayPauseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_pomodoroState == PomodoroState.Running)
        {
            _pomodoroState = PomodoroState.Paused;
            _pomodoroTimer.Stop();
        }
        else
        {
            _pomodoroState = PomodoroState.Running;
            _pomodoroTimer.Start();
        }
        UpdatePomodoroUI();
    }

    private void PomoResetBtn_Click(object sender, RoutedEventArgs e)
    {
        SetPomodoroMode(_pomodoroMode, resetTimer: true);
    }

    private void PomoSkipBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_pomodoroMode == PomodoroMode.Work)
        {
            SetPomodoroMode(PomodoroMode.ShortBreak, resetTimer: true);
        }
        else
        {
            SetPomodoroMode(PomodoroMode.Work, resetTimer: true);
        }
    }

    private void PomoModeWorkBtn_Click(object sender, RoutedEventArgs e)
    {
        SetPomodoroMode(PomodoroMode.Work, resetTimer: true);
    }

    private void PomoModeShortBtn_Click(object sender, RoutedEventArgs e)
    {
        SetPomodoroMode(PomodoroMode.ShortBreak, resetTimer: true);
    }

    private void PomoModeLongBtn_Click(object sender, RoutedEventArgs e)
    {
        SetPomodoroMode(PomodoroMode.LongBreak, resetTimer: true);
    }

    private void PopulatePomodoroTaskCombo()
    {
        PomoTaskLinkCombo.Items.Clear();
        PomoTaskLinkCombo.Items.Add(new ComboBoxItem { Content = "Sin vincular a backlog", Tag = null });

        var selectIndex = 0;
        var idx = 1;

        foreach (var task in _tasks.Where(t => !t.IsCompleted))
        {
            var item = new ComboBoxItem
            {
                Content = $"⚡ [{task.Category}] {task.Title}",
                Tag = task.Id
            };
            PomoTaskLinkCombo.Items.Add(item);

            if (_linkedPomodoroTaskId is not null && task.Id == _linkedPomodoroTaskId)
            {
                selectIndex = idx;
            }
            idx++;
        }

        PomoTaskLinkCombo.SelectedIndex = selectIndex;
    }

    private void PomoTaskLinkCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PomoTaskLinkCombo.SelectedItem is ComboBoxItem item && item.Tag is string taskId)
        {
            _linkedPomodoroTaskId = taskId;
            var linked = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (linked is not null)
            {
                PomodoroStateSubtitle.Text = $"🎯 {linked.Title}";
            }
        }
        else
        {
            _linkedPomodoroTaskId = null;
        }
    }
}
