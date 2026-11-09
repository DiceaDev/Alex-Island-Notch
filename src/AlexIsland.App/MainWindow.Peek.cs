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
    // ------------------------------------------------------------ Mascota: dormir

    /// <summary>Segundos en reposo antes de que el zorro cierre los ojos.</summary>
    private const double DozeAfterSeconds = 30;

    private DispatcherQueueTimer? _dozeTimer;
    private Storyboard? _dozeStoryboard;
    private bool _isDozing;

    /// <summary>
    /// Se llama cada vez que cambia el estado de la isla. En reposo programa la
    /// "siesta"; al salir del reposo (cursor, música, aviso) los ojos se abren.
    /// </summary>
    private void UpdateMascotSleep(bool restingVisible)
    {
        if (_dozeTimer is null)
        {
            _dozeTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _dozeTimer.Interval = TimeSpan.FromSeconds(DozeAfterSeconds);
            _dozeTimer.IsRepeating = false;
            _dozeTimer.Tick += (_, _) =>
            {
                _dozeTimer.Stop();
                StartDozing();
            };
        }

        if (restingVisible)
        {
            if (!_isDozing && !_dozeTimer.IsRunning) _dozeTimer.Start();
            return;
        }

        _dozeTimer.Stop();
        if (_isDozing) WakeUp();
    }

    private void StartDozing()
    {
        if (_currentDisplayMode != NotchDisplayMode.Resting || _isDozing) return;
        _isDozing = true;
        _blinkTimer?.Stop();

        // Los ojos se cierran despacio y quedan casi como una línea; el brillo baja.
        _dozeStoryboard = new Storyboard();
        foreach (var eye in new[] { LeftEyeScale, RightEyeScale })
        {
            var close = new DoubleAnimation
            {
                To = 0.14,
                Duration = TimeSpan.FromMilliseconds(900),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(close, eye);
            Storyboard.SetTargetProperty(close, "ScaleY");
            _dozeStoryboard.Children.Add(close);
        }

        var dim = new DoubleAnimation { To = 0.35, Duration = TimeSpan.FromMilliseconds(900) };
        Storyboard.SetTarget(dim, FoxEyesGlow);
        Storyboard.SetTargetProperty(dim, "Opacity");
        _dozeStoryboard.Children.Add(dim);

        _dozeStoryboard.Begin();
    }

    private void WakeUp()
    {
        _isDozing = false;
        // Al detener la animación, los ojos y el brillo vuelven a su valor normal.
        _dozeStoryboard?.Stop();
        _dozeStoryboard = null;
    }

    private void TriggerEyeBlink()
    {
        if (_currentDisplayMode != NotchDisplayMode.Resting || _isDozing) return;

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
        var strokeColor = color ?? AccentColor;
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
        var cx = 8.0;
        var cy = 8.0;
        var r = 6.0;

        var startX = 8.0;
        var startY = 2.0;
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
        var cx = 13.0;
        var cy = 13.0;
        var r = 10.0;

        var startX = 13.0;
        var startY = 3.0;
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
        Windows.UI.Color? accentColor = null,
        TimeSpan? duration = null)
    {
        _isPinned = false;
        _isPointerInside = false;
        _isExpanded = false;

        // Si ya se está mostrando un aviso del mismo tipo (por ejemplo, al subir
        // el volumen varias veces seguidas), solo se actualiza sin repetir la animación.
        // Cada notificación nueva, en cambio, sí vuelve a llamar la atención.
        var isUpdate = _isPeekActive && _activePeekType == type && type != HardwarePeekType.Notification;
        _activePeekType = type;

        _isPeekActive = true;
        _isRecentInteraction = true;

        PeekTitle.Text = title;
        PeekSubtitle.Text = subtitle;

        var (iconGlyph, defaultRightGlyph, defaultColor) = type switch
        {
            HardwarePeekType.Headphones => ("\uE702", "\uE7F6", ColorHelper.FromArgb(255, 93, 179, 204)),
            HardwarePeekType.Charging => ("\uE945", "\uE83E", ColorHelper.FromArgb(255, 79, 209, 165)),
            HardwarePeekType.Battery => ("\uE850", "\uE7BA", ColorHelper.FromArgb(255, 255, 194, 92)),
            HardwarePeekType.CapsLock => ("\uE765", "\uE72E", ColorHelper.FromArgb(255, 196, 143, 214)),
            HardwarePeekType.Notification => ("\uEA8F", "\uE8BD", AccentColor),
            HardwarePeekType.Network => ("\uE774", "\uE701", ColorHelper.FromArgb(255, 79, 209, 165)),
            HardwarePeekType.Volume => ("\uE767", "\uE995", ColorHelper.FromArgb(255, 93, 179, 204)),
            HardwarePeekType.Brightness => ("\uE706", "\uE706", ColorHelper.FromArgb(255, 255, 214, 102)),
            HardwarePeekType.KeyboardLayout => ("\uE765", "\uE774", ColorHelper.FromArgb(255, 69, 196, 176)),
            _ => ("\uE702", "\uE7F6", ColorHelper.FromArgb(255, 93, 179, 204))
        };

        var color = accentColor ?? defaultColor;
        PeekIcon.Glyph = iconGlyph;
        PeekRightIcon.Glyph = rightGlyph ?? defaultRightGlyph;
        PeekRightIcon.Foreground = new SolidColorBrush(color);
        PeekRippleInner.Stroke = new SolidColorBrush(color) { Opacity = 0.4 };
        PeekRippleOuter.Stroke = new SolidColorBrush(color) { Opacity = 0.2 };
        PeekGlow.Fill = CreateGlowBrush(color);

        UpdatePeekProgress(percent01, color);
        if (!isUpdate)
        {
            TriggerPeekRippleAnimation();
        }

        ApplyIslandState();

        _peekTimer?.Stop();
        _peekTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _peekTimer.Interval = duration ?? TimeSpan.FromSeconds(3.5);
        _peekTimer.IsRepeating = false;
        _peekTimer.Tick += (_, _) =>
        {
            _peekTimer.Stop();
            _isPeekActive = false;
            ApplyIslandState();

            // Tras un aviso, si nadie interactúa, la isla vuelve al reposo.
            _idleTimer.Stop();
            _idleTimer.Start();
        };
        _peekTimer.Start();
    }

    /// <summary>Halo radial: color del aviso en el centro que se desvanece hacia el borde.</summary>
    private static RadialGradientBrush CreateGlowBrush(Windows.UI.Color color)
    {
        Windows.UI.Color WithAlpha(byte alpha) => Windows.UI.Color.FromArgb(alpha, color.R, color.G, color.B);

        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop { Color = WithAlpha(110), Offset = 0.0 });
        brush.GradientStops.Add(new GradientStop { Color = WithAlpha(55), Offset = 0.45 });
        brush.GradientStops.Add(new GradientStop { Color = WithAlpha(0), Offset = 1.0 });
        return brush;
    }

    private void TriggerPeekRippleAnimation()
    {
        var sb = new Storyboard();

        // El halo respira junto con las ondas.
        var glow = new DoubleAnimation
        {
            From = 0.55,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(650),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(glow, PeekGlow);
        Storyboard.SetTargetProperty(glow, "Opacity");
        sb.Children.Add(glow);
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
            stOuter = new ScaleTransform { CenterX = 14, CenterY = 14 };
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

    private bool _capsLockInitialized;

    /// <summary>
    /// Lee el estado real de Bloq Mayús (el bit de "alternado" de GetKeyState) y avisa solo
    /// cuando cambia. Antes se contaba cada pulsación, por lo que el aviso se invertía si
    /// la tecla ya estaba activa al abrir la app o se cambiaba desde otra parte.
    /// </summary>
    private void CheckCapsLockState()
    {
        try
        {
            var isOn = (GetKeyState(VK_CAPITAL) & 0x0001) != 0;

            if (!_capsLockInitialized)
            {
                // Primera lectura: solo se guarda el estado actual, sin avisar.
                _capsLockInitialized = true;
                _lastCapsLockState = isOn;
                return;
            }

            if (isOn == _lastCapsLockState) return;
            _lastCapsLockState = isOn;

            if (_settings.ShowCapsLockPeeks && CanShowAutomaticPeek)
            {
                ShowHardwarePeek(
                    HardwarePeekType.CapsLock,
                    "Bloq Mayús",
                    isOn ? "ACTIVADO" : "DESACTIVADO",
                    percent01: isOn ? 1.0 : 0.0);
            }
        }
        catch { }
    }

    private void CompactCenter_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        _showClockInCompactCenter = !_showClockInCompactCenter;
        UpdateCompactCenter();
    }

    private void UpdateCompactCenter()
    {
        if (CompactClock is null || CompactMediaInfo is null || CompactFocusInfo is null) return;

        if (_showClockInCompactCenter)
        {
            CompactClock.Visibility = Visibility.Visible;
            CompactMediaInfo.Visibility = Visibility.Collapsed;
            CompactFocusInfo.Visibility = Visibility.Collapsed;
            return;
        }

        if (_pomodoroState == PomodoroState.Running)
        {
            CompactClock.Visibility = Visibility.Collapsed;
            CompactMediaInfo.Visibility = Visibility.Collapsed;
            CompactFocusInfo.Visibility = Visibility.Visible;
            var isBreak = _pomodoroMode != PomodoroMode.Work;
            CompactFocusTitle.Text = isBreak ? "Pausa de Descanso" : "Enfoque Activo";
            CompactFocusTitle.Foreground = new SolidColorBrush(isBreak
                ? ColorHelper.FromArgb(255, 79, 209, 165) // verde: pausa (semántico)
                : AccentColor);
            CompactFocusTime.Text = $"{_pomodoroRemaining:mm\\:ss}";
            return;
        }

        if (_isPlaying && !string.IsNullOrWhiteSpace(CompactTrackTitle.Text) && CompactTrackTitle.Text != "No hay reproducción activa")
        {
            CompactClock.Visibility = Visibility.Collapsed;
            CompactMediaInfo.Visibility = Visibility.Visible;
            CompactFocusInfo.Visibility = Visibility.Collapsed;
            return;
        }

        CompactClock.Visibility = Visibility.Visible;
        CompactMediaInfo.Visibility = Visibility.Collapsed;
        CompactFocusInfo.Visibility = Visibility.Collapsed;
    }
}
