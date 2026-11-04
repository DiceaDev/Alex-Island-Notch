using AlexIsland.App.Native;
using AlexIsland.App.Services;
using AlexIsland.Core.Models;
using AlexIsland.Core.Security;
using AlexIsland.Core.Services;
using AlexIsland.Core.Settings;
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
    private enum IslandTab
    {
        Media,
        Backlog,
        Calendar,
        Pomodoro,
        System,
        Shelf
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
        Network,
        Volume,
        Brightness,
        KeyboardLayout,
        Notification,
        Custom
    }

    private const int NotchWidth = 196;
    private const int NotchPlayingWidth = 236;
    private const int NotchHeight = 36;
    private const int RestingWidth = 146;
    private const int RestingHeight = 32;
    private const int PeekWidth = 300;
    private const int PeekHeight = 42;
    // Más ancha para que las seis pestañas muestren su nombre y Inicio tenga dos columnas.
    private const int ExpandedWidth = 540;
    private const int ExpandedHeight = 162;
    private const int ExpandedBacklogHeight = 196;
    private const int ExpandedCalendarHeight = 234;
    private const int ExpandedPomodoroHeight = 196;
    private const int ExpandedSystemHeight = 158;
    private const double EarRadius = 12.0;

    /// <summary>Naranja de Alex Island (#FF5A00), color de acento de toda la isla.</summary>
    private static readonly Windows.UI.Color AccentColor = ColorHelper.FromArgb(255, 255, 90, 0);

    /// <summary>El color de acento con otra opacidad (fondos suaves, bordes).</summary>
    private static Windows.UI.Color Accent(byte alpha) => ColorHelper.FromArgb(alpha, 255, 90, 0);

    /// <summary>
    /// Botón naranja creado en código: texto blanco y se mantiene naranja (más
    /// claro u oscuro) al pasar el cursor o presionarlo, en vez del gris del tema.
    /// </summary>
    private static void ApplyAccentButtonStates(Button button)
    {
        button.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(ColorHelper.FromArgb(255, 255, 122, 51));
        button.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(ColorHelper.FromArgb(255, 217, 77, 0));
        button.Resources["ButtonForegroundPointerOver"] = new SolidColorBrush(Colors.White);
        button.Resources["ButtonForegroundPressed"] = new SolidColorBrush(Colors.White);
    }

    /// <summary>Espacio transparente a los lados y debajo del notch para su sombra difuminada.</summary>
    private const double ShadowMargin = 16.0;
    private const int MinimalWidth = 120;
    private const int MinimalHeight = 8;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
    private const int VK_CAPITAL = 0x14;
    private bool _showClockInCompactCenter;

    private static readonly string[] AvailableTags = ["TODO", "FEAT", "FIX", "REFACTOR", "DOCS"];
    private static readonly string[] EventCategories = ["MEETING", "DEV", "REVIEW", "PLANNING", "GENERAL"];

    private readonly IslandHostWindow _host;
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
    private HardwarePeekType _activePeekType;
    private bool _isRecentInteraction = true;
    private bool _lastCapsLockState;

    // Ajustes del usuario
    private readonly SettingsStore _settingsStore = new();
    private IslandSettings _settings = new();

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

        // Ventana propia con transparencia por píxel que aloja este control.
        _host = new IslandHostWindow(this, NotchWidth, NotchHeight);
        _hwnd = _host.Hwnd;
        _host.Closed += (_, _) => OnHostClosed();
        NativeMethods.ConfigureIslandWindow(_hwnd);

        // El notch queda pegado arriba y centrado; alrededor queda espacio para la sombra.
        IslandContainer.Margin = new Thickness(ShadowMargin, 0, ShadowMargin, ShadowMargin);
        InitializeNotchShadow();

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
        _reminderTimer.Tick += (_, _) =>
        {
            CheckUpcomingMeetings();
            // Mantiene al día la cuenta regresiva y la línea de "ahora" si la agenda está a la vista.
            if (_isExpanded && _activeTab == IslandTab.Calendar && _calendarSubMode == CalendarSubMode.Agenda
                && CalEventFormOverlay.Visibility != Visibility.Visible)
            {
                RenderCalendarView();
            }
        };
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
        _capsLockTimer.Tick += (_, _) =>
        {
            CheckCapsLockState();
            CheckKeyboardLayout();
        };
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

        InitializeSystemModule();
        InitializeShellIntegration();
        InitializeOverlays();
        InitializeNotifications();
        InitializeLyrics();

        // Si cambia el DPI (por ejemplo, al mover la isla a otro monitor), se recalcula el tamaño real.
        IslandContainer.Loaded += (_, _) =>
        {
            if (IslandContainer.XamlRoot is { } root)
            {
                root.Changed += (_, _) => ApplyIslandState();
            }
        };

        UpdateClock();
        ApplyIslandState();

        // Si nadie interactúa al abrir la app, a los pocos segundos pasa a reposo
        // (mascota). Antes solo ocurría después de pasar el cursor por la isla.
        _idleTimer.Start();

        _ = LoadSettingsAsync();
        _ = InitializeMediaAsync();
        _ = LoadTasksAsync();
        _calendarLoadTask = LoadCalendarEventsAsync();
        _ = LoadPomodoroAsync();
        _ = LoadShelfAsync();
    }

    /// <summary>Muestra la isla en pantalla (sin quitarle el foco a la app activa).</summary>
    public void Show() => _host.Show();

    private void OnHostClosed()
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
        StopSystemModule();
        StopShellIntegration();
        StopOverlays();
        StopNotifications();
        StopOutlookSync();
        StopLyrics();
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            _settings = await _settingsStore.LoadAsync();
        }
        catch
        {
            _settings = new IslandSettings();
        }

        if (!_isDisposed) ApplySettings();
    }

    /// <summary>Aplica en la interfaz los ajustes que no se leen en el momento de usarse.</summary>
    private void ApplySettings()
    {
        ApplyShellSettings();
        ApplyNotificationSettings();
        ApplyOutlookSyncSettings();

        // Si se activaron las letras con una canción sonando, se piden ahora.
        if (_settings.ShowLyrics && _lyrics is null && _lyricsTrackKey is not null)
        {
            _lyricsTrackKey = null;
            OnTrackInfoChanged(_currentTrackTitle, _currentTrackArtist);
        }

        ApplyIslandState();
    }

    /// <summary>
    /// Píxeles físicos por unidad de XAML: escala del monitor (DPI) por el tamaño
    /// elegido en los ajustes. La ventana y su región se calculan con este factor.
    /// </summary>
    private double PixelScale => NativeMethods.GetDpiScale(_hwnd) * _settings.NotchScale;

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

        _isPointerInside = IsCursorOverNotch();
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
        if (!_isPinned) _isPointerInside = IsCursorOverNotch();
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

        if (_isPeekActive)
        {
            _currentDisplayMode = NotchDisplayMode.Peek;
            _isExpanded = false;
        }
        else if (_isExpanded)
        {
            _currentDisplayMode = NotchDisplayMode.Expanded;
        }
        else if (_isPlaying || _pomodoroState == PomodoroState.Running || _isRecentInteraction)
        {
            _currentDisplayMode = NotchDisplayMode.Closed;
        }
        else
        {
            _currentDisplayMode = NotchDisplayMode.Resting;
        }

        RestingContent.Visibility = _currentDisplayMode == NotchDisplayMode.Resting && _settings.ShowMascot && !_settings.MinimalNotch
            ? Visibility.Visible
            : Visibility.Collapsed;
        CompactContent.Visibility = _currentDisplayMode == NotchDisplayMode.Closed ? Visibility.Visible : Visibility.Collapsed;
        PeekContent.Visibility = _currentDisplayMode == NotchDisplayMode.Peek ? Visibility.Visible : Visibility.Collapsed;
        ExpandedContent.Visibility = _currentDisplayMode == NotchDisplayMode.Expanded ? Visibility.Visible : Visibility.Collapsed;
        ExpandedContent.Opacity = _currentDisplayMode == NotchDisplayMode.Expanded ? 1 : 0;

        var (width, height) = _currentDisplayMode switch
        {
            NotchDisplayMode.Resting when _settings.MinimalNotch => (MinimalWidth, MinimalHeight),
            NotchDisplayMode.Resting => (RestingWidth, RestingHeight),
            NotchDisplayMode.Closed => (_isPlaying ? NotchPlayingWidth : NotchWidth, NotchHeight),
            NotchDisplayMode.Peek => (PeekWidth, PeekHeight),
            NotchDisplayMode.Expanded => (
                ExpandedWidth,
                _activeTab switch
                {
                    IslandTab.Calendar => CalendarExpandedHeight,
                    IslandTab.Backlog => ExpandedBacklogHeight,
                    IslandTab.Pomodoro => ExpandedPomodoroHeight,
                    IslandTab.System => ExpandedSystemHeight,
                    IslandTab.Shelf => ExpandedShelfHeight,
                    IslandTab.Media when HasLyrics => ExpandedMediaLyricsHeight,
                    _ => ExpandedHeight
                }),
            _ => (NotchWidth, NotchHeight)
        };

        IslandContainer.Width = width;
        IslandContainer.Height = height;
        NotchUserScale.ScaleX = _settings.NotchScale;
        NotchUserScale.ScaleY = _settings.NotchScale;

        UpdateNotchGeometry(width, height);
        ResizeIsland(width, height);
        PositionAtTopCenter();

        var restingVisible = _currentDisplayMode == NotchDisplayMode.Resting && RestingContent.Visibility == Visibility.Visible;
        if (restingVisible && !_isDozing)
        {
            _blinkTimer?.Start();
        }
        else
        {
            _blinkTimer?.Stop();
        }
        UpdateMascotSleep(restingVisible);

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

        UpdateSystemSampling();
        ApplyLyricsState();

        if (wasExpanded != _isExpanded || previousMode != _currentDisplayMode)
        {
            AnimateIsland(_isExpanded, _currentDisplayMode);
        }

        UpdateCompactCenter();
    }

    private void UpdateNotchGeometry(double width, double height)
    {
        if (width <= 0 || height <= 0) return;

        // En el notch mínimo la altura es muy baja: los radios se limitan para
        // que la silueta no se cruce consigo misma.
        var earRadius = Math.Min(EarRadius, height / 2.0);
        var bottomRadius = _isExpanded ? 18.0 : (_currentDisplayMode == NotchDisplayMode.Resting ? 13.0 : 16.0);
        bottomRadius = Math.Min(bottomRadius, height / 2.0);
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

        UpdateNotchShadowSize(width, height);

    }

    private void AnimateIsland(bool expanding, NotchDisplayMode mode = NotchDisplayMode.Expanded)
    {
        _activeAnimation?.Stop();
        var storyboard = new Storyboard();

        var isPeek = mode == NotchDisplayMode.Peek;
        var durationMs = expanding ? 270 : (isPeek ? 240 : 180);

        // Expansión y contracción elástica estilo Apple Dynamic Island (Spring / BackEase).
        // Ambas terminan en escala 1: la ventana ya tiene el tamaño final, y si el
        // dibujo quedara más chico (antes terminaba en 0,94) se verían los bordes
        // de la ventana alrededor del notch.
        var grow = expanding || isPeek;
        var scaleX = new DoubleAnimation
        {
            From = grow ? 0.90 : 1.05,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = grow
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.22 }
                : new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var scaleY = new DoubleAnimation
        {
            From = grow ? 0.85 : 1.08,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = grow
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.22 }
                : new CubicEase { EasingMode = EasingMode.EaseOut }
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

            // La ventana trabaja en píxeles físicos; XAML, en unidades independientes del DPI.
            var scale = PixelScale;
            _host.Resize(
                (int)Math.Ceiling((width + 2 * ShadowMargin) * scale),
                (int)Math.Ceiling((height + ShadowMargin) * scale));
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
                display = DisplayArea.GetFromWindowId(_host.WindowId, DisplayAreaFallback.Primary);
            }
            catch
            {
            }

            display ??= DisplayArea.Primary;
            if (display is null) return;

            var bounds = display.OuterBounds;
            var width = (int)Math.Ceiling((IslandContainer.Width + 2 * ShadowMargin) * PixelScale);
            var x = bounds.X + ((bounds.Width - width) / 2);
            _host.Move(x, bounds.Y);
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
        // Siempre en el naranja de la marca: intenso con música, más tenue en pausa.
        var brush = new SolidColorBrush(_isPlaying ? AccentColor : Accent(150));
        WaveBar1.Background = brush;
        WaveBar2.Background = brush;
        WaveBar3.Background = brush;
        WaveBar4.Background = brush;
    }


    // ==============================================================
    // NAVEGACIÓN ENTRE PESTAÑAS
    // ==============================================================

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

        var inactiveBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 142, 146, 156));
        var activeBackground = new SolidColorBrush(ColorHelper.FromArgb(255, 44, 45, 51));

        // Todas las pestañas muestran ícono y nombre; la activa va en una píldora
        // gris y en blanco. El naranja queda para lo que pide atención.
        foreach (var (id, button, label, view) in GetTabs())
        {
            var isActive = id == tab;
            button.Background = isActive ? activeBackground : new SolidColorBrush(Colors.Transparent);
            label.Foreground = isActive ? new SolidColorBrush(Colors.White) : inactiveBrush;
            view.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var (id, icon) in GetTabIcons())
        {
            icon.Foreground = id == tab ? new SolidColorBrush(Colors.White) : inactiveBrush;
        }

        RefreshTabBadges();
        CheckUpcomingMeetings();

        switch (_activeTab)
        {
            case IslandTab.Media:
                AnimateViewEntrance(MediaView);
                break;

            case IslandTab.Backlog:
                AnimateViewEntrance(BacklogView);
                NewTaskInput.Focus(FocusState.Programmatic);
                break;

            case IslandTab.Calendar:
                AnimateViewEntrance(CalendarView);
                RenderCalendarView();
                break;

            case IslandTab.Pomodoro:
                AnimateViewEntrance(PomodoroView);
                PopulatePomodoroTaskCombo();
                UpdatePomodoroUI();
                break;

            case IslandTab.System:
                AnimateViewEntrance(SystemView);
                if (_lastNetwork is { } network) RenderNetwork(network);
                break;

            case IslandTab.Shelf:
                AnimateViewEntrance(ShelfView);
                break;
        }

        AnimateTabPillPulse();
        ApplyIslandState();
    }

    private (IslandTab Id, Button Button, TextBlock Label, FrameworkElement View)[] GetTabs() =>
    [
        (IslandTab.Media, TabMediaButton, TabMediaLabel, MediaView),
        (IslandTab.Calendar, TabCalendarButton, TabCalendarLabel, CalendarView),
        (IslandTab.Backlog, TabBacklogButton, TabBacklogLabel, BacklogView),
        (IslandTab.Pomodoro, TabPomodoroButton, TabPomodoroLabel, PomodoroView),
        (IslandTab.System, TabSystemButton, TabSystemLabel, SystemView),
        (IslandTab.Shelf, TabShelfButton, TabShelfLabel, ShelfView),
    ];

    private (IslandTab Id, FontIcon Icon)[] GetTabIcons() =>
    [
        (IslandTab.Media, TabMediaIcon),
        (IslandTab.Calendar, TabCalendarIcon),
        (IslandTab.Backlog, TabBacklogIcon),
        (IslandTab.Pomodoro, TabPomodoroIcon),
        (IslandTab.System, TabSystemIcon),
        (IslandTab.Shelf, TabShelfIcon),
    ];

    /// <summary>
    /// Contadores y puntos de las pestañas. El número solo se ve en la pestaña
    /// activa (y si es mayor que cero); en las inactivas, un punto naranja marca
    /// únicamente lo que pide atención: una reunión que empieza pronto o está en
    /// curso, o un Pomodoro corriendo.
    /// </summary>
    private void RefreshTabBadges()
    {
        static bool HasCount(TextBlock count) => int.TryParse(count.Text, out var value) && value > 0;

        HeaderBacklogBadge.Visibility = _activeTab == IslandTab.Backlog && HasCount(HeaderBacklogCount) ? Visibility.Visible : Visibility.Collapsed;
        HeaderCalendarBadge.Visibility = _activeTab == IslandTab.Calendar && HasCount(HeaderCalendarCount) ? Visibility.Visible : Visibility.Collapsed;
        HeaderPomodoroBadge.Visibility = _activeTab == IslandTab.Pomodoro && HasCount(HeaderPomodoroCount) ? Visibility.Visible : Visibility.Collapsed;
        HeaderShelfBadge.Visibility = _activeTab == IslandTab.Shelf && HasCount(HeaderShelfCount) ? Visibility.Visible : Visibility.Collapsed;

        TabCalendarDot.Visibility = _activeTab != IslandTab.Calendar && _upcomingMeetingSoon ? Visibility.Visible : Visibility.Collapsed;
        TabPomodoroDot.Visibility = _activeTab != IslandTab.Pomodoro && _pomodoroState == PomodoroState.Running ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Con la rueda del mouse sobre la barra de pestañas se pasa a la siguiente
    /// o la anterior, como en Nusa.
    /// </summary>
    private void TabBar_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(sender as UIElement).Properties.MouseWheelDelta;
        if (delta == 0) return;

        var tabs = GetTabs();
        var index = Array.FindIndex(tabs, t => t.Id == _activeTab);
        var next = (index + (delta < 0 ? 1 : -1) + tabs.Length) % tabs.Length;
        SwitchTab(tabs[next].Id);
        e.Handled = true;
    }
}
