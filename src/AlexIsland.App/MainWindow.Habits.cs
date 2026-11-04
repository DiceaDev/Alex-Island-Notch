using AlexIsland.App.Services;
using AlexIsland.Core.Habits;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace AlexIsland.App;

public sealed partial class MainWindow : UserControl
{
    // ==============================================================
    // SECCIÓN HÁBITOS: SEGUIMIENTO DIARIO
    // ==============================================================

    private const int ExpandedHabitsHeight = 188;

    private readonly HabitStore _habitStore = new();
    private readonly List<Habit> _habits = [];

    /// <summary>Hábito que espera una segunda pulsación en "quitar" para borrarse.</summary>
    private string? _habitPendingDeleteId;

    private async Task LoadHabitsAsync()
    {
        var loaded = await _habitStore.LoadAsync();
        if (_isDisposed) return;

        _habits.Clear();
        _habits.AddRange(loaded);
        RenderHabits();
    }

    private void TabHabitsButton_Click(object sender, RoutedEventArgs e) => SwitchTab(IslandTab.Habits);

    private void NewHabitInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        AddHabitFromInput();
        e.Handled = true;
    }

    private void AddHabitButton_Click(object sender, RoutedEventArgs e) => AddHabitFromInput();

    private void AddHabitFromInput()
    {
        var text = NewHabitInput.Text;
        if (string.IsNullOrWhiteSpace(text)) return;

        if (!HabitRules.Add(_habits, text))
        {
            HabitsSummary.Text = _habits.Count >= HabitRules.MaxHabits
                ? $"Máximo {HabitRules.MaxHabits} hábitos"
                : "Ese hábito ya existe o el nombre no es válido";
            return;
        }

        NewHabitInput.Text = string.Empty;
        _habitPendingDeleteId = null;
        SaveHabits();
        RenderHabits();
        NewHabitInput.Focus(FocusState.Programmatic);
    }

    private void SaveHabits() => _ = _habitStore.SaveAsync(_habits);

    private static DateOnly HabitToday => DateOnly.FromDateTime(DateTime.Today);

    private void RenderHabits()
    {
        var today = HabitToday;
        HabitsItemsPanel.Children.Clear();

        if (_habits.Count == 0)
        {
            HabitsItemsPanel.Children.Add(new TextBlock
            {
                Text = "Escribe un hábito y pulsa Enter.\nPara una meta diaria usa «x»: Agua x8",
                FontSize = 9.5,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 111, 116, 128)),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 22, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            HabitsSummary.Text = "Sin hábitos todavía";
            return;
        }

        foreach (var habit in _habits)
        {
            HabitsItemsPanel.Children.Add(BuildHabitRow(habit, today));
        }

        var done = HabitRules.DoneToday(_habits, today);
        HabitsSummary.Text = done == _habits.Count
            ? $"¡Todo cumplido hoy! ({done} de {_habits.Count})"
            : $"{done} de {_habits.Count} cumplidos hoy";
    }

    private UIElement BuildHabitRow(Habit habit, DateOnly today)
    {
        var count = HabitRules.CountOn(habit, today);
        var done = HabitRules.IsDone(habit, today);
        var streak = HabitRules.Streak(habit, today);

        var row = new Grid { ColumnSpacing = 7, MinHeight = 24 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Botón de cumplir: círculo naranja con check cuando está hecho; con meta > 1 muestra "2/8".
        UIElement checkContent = done
            ? new FontIcon { Glyph = "", FontSize = 10, Foreground = new SolidColorBrush(Colors.White) }
            : habit.Target > 1
                ? new TextBlock
                {
                    Text = $"{count}/{habit.Target}",
                    FontSize = 8.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 165, 170, 184)),
                }
                : new Border();

        var check = new Button
        {
            MinWidth = 24,
            Height = 24,
            Padding = new Thickness(5, 0, 5, 0),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(done ? AccentColor : ColorHelper.FromArgb(255, 34, 37, 48)),
            BorderBrush = new SolidColorBrush(done ? AccentColor : ColorHelper.FromArgb(255, 50, 54, 69)),
            BorderThickness = new Thickness(1),
            Content = checkContent,
            VerticalAlignment = VerticalAlignment.Center,
        };
        check.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(done ? ColorHelper.FromArgb(255, 255, 122, 51) : ColorHelper.FromArgb(255, 44, 48, 62));
        check.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(ColorHelper.FromArgb(255, 217, 77, 0));
        ToolTipService.SetToolTip(check, habit.Target > 1 ? "Sumar una vez (al pasar la meta vuelve a cero)" : "Marcar o desmarcar hoy");
        check.Click += (_, _) =>
        {
            HabitRules.Tap(habit, HabitToday);
            _habitPendingDeleteId = null;
            SaveHabits();
            RenderHabits();
        };
        row.Children.Add(check);

        var name = new TextBlock
        {
            Text = habit.Name,
            FontSize = 11,
            Foreground = new SolidColorBrush(done ? ColorHelper.FromArgb(255, 143, 148, 163) : ColorHelper.FromArgb(255, 240, 242, 245)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(name, 1);
        row.Children.Add(name);

        // Racha y últimos 7 días.
        var streakText = new TextBlock
        {
            Text = streak > 0 ? $"🔥{streak}" : string.Empty,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(AccentColor),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(streakText, 2);
        row.Children.Add(streakText);

        var week = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var days = HabitRules.LastDays(habit, today);
        for (var i = 0; i < days.Length; i++)
        {
            var isToday = i == days.Length - 1;
            week.Children.Add(new Ellipse
            {
                Width = isToday ? 7 : 6,
                Height = isToday ? 7 : 6,
                Fill = new SolidColorBrush(days[i] ? AccentColor : ColorHelper.FromArgb(255, 47, 51, 66)),
            });
        }
        ToolTipService.SetToolTip(week, "Últimos 7 días");
        Grid.SetColumn(week, 3);
        row.Children.Add(week);

        // Quitar: pide una segunda pulsación para no borrar por error.
        var pending = _habitPendingDeleteId == habit.Id;
        var remove = new Button
        {
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new FontIcon
            {
                Glyph = pending ? "" : "",
                FontSize = 8,
                Foreground = new SolidColorBrush(pending ? ColorHelper.FromArgb(255, 239, 83, 80) : ColorHelper.FromArgb(255, 111, 116, 128)),
            },
        };
        ToolTipService.SetToolTip(remove, pending ? "Pulsa otra vez para quitar" : "Quitar hábito");
        remove.Click += (_, _) =>
        {
            if (_habitPendingDeleteId == habit.Id)
            {
                _habits.Remove(habit);
                _habitPendingDeleteId = null;
                SaveHabits();
            }
            else
            {
                _habitPendingDeleteId = habit.Id;
            }
            RenderHabits();
        };
        Grid.SetColumn(remove, 4);
        row.Children.Add(remove);

        return row;
    }
}
