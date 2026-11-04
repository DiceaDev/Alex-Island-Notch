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

public sealed partial class MainWindow : Window
{
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
        Windows.UI.Color? accentColor = null)
    {
        _isPinned = false;
        _isPointerInside = false;
        _isExpanded = false;

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

    private void CheckCapsLockState()
    {
        try
        {
            var keyState = GetAsyncKeyState(VK_CAPITAL);
            var isDown = (keyState & 0x8000) != 0;

            if (isDown && !_lastCapsKeyWasDown)
            {
                _lastCapsKeyWasDown = true;
                var isToggled = (GetKeyState(VK_CAPITAL) & 0x0001) != 0;
                var newCaps = !_lastCapsLockState;
                _lastCapsLockState = newCaps;

                ShowHardwarePeek(
                    HardwarePeekType.CapsLock,
                    "Bloq Mayús",
                    newCaps ? "ACTIVADO" : "DESACTIVADO",
                    percent01: newCaps ? 1.0 : 0.0);
            }
            else if (!isDown)
            {
                _lastCapsKeyWasDown = false;
            }
        }
        catch { }
    }

    private void DemoPeekBtn_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = false;
        _isPointerInside = false;
        _isExpanded = false;
        _demoPeekIndex = (_demoPeekIndex + 1) % 4;

        switch (_demoPeekIndex)
        {
            case 0:
                ShowHardwarePeek(
                    HardwarePeekType.Headphones,
                    "AirBuds Pro",
                    "Connected · 70%",
                    percent01: 0.70);
                break;
            case 1:
                ShowHardwarePeek(
                    HardwarePeekType.Charging,
                    "Cargador conectado",
                    "Cargando · 85%",
                    percent01: 0.85);
                break;
            case 2:
                ShowHardwarePeek(
                    HardwarePeekType.Battery,
                    "Batería en uso",
                    "68% restante",
                    percent01: 0.68);
                break;
            case 3:
                ShowHardwarePeek(
                    HardwarePeekType.CapsLock,
                    "Bloq Mayús",
                    "ACTIVADO",
                    percent01: 1.0);
                break;
        }
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
                ? ColorHelper.FromArgb(255, 0, 230, 118)
                : ColorHelper.FromArgb(255, 239, 83, 80));
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
