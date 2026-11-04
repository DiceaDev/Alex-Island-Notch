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
            ? AccentColor // trabajo: naranja de la marca
            : ColorHelper.FromArgb(255, 0, 230, 118); // pausa: verde (semántico)

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
                ? AccentColor
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

        UpdateCompactCenter();
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
