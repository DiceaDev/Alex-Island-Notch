using AlexIsland.App.Services;
using AlexIsland.Core.Planner;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace AlexIsland.App;

public sealed partial class MainWindow : UserControl
{
    // ==============================================================
    // SECCIÓN PLANNER SEMANAL
    // ==============================================================
    //
    // Notas libres por día, como un planner de escritorio: lunes a viernes, sábado
    // y domingo juntos, y una columna "Recuerda" que no cambia con la semana.
    // Se redibuja solo cuando algo cambia; no usa temporizadores.

    private const int ExpandedPlannerHeight = 318;
    private const string PlannerRemindersKey = "reminders";

    private readonly PlannerStore _plannerStore = new();
    private PlannerBoard? _planner;
    private DateOnly _plannerWeek = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Casillero donde se está escribiendo una nota nueva (o null).</summary>
    private string? _plannerEditingKey;

    /// <summary>Aumenta con cada redibujado; permite reconocer un campo de texto ya reemplazado.</summary>
    private int _plannerRenderGeneration;

    private void TabPlannerButton_Click(object sender, RoutedEventArgs e) => SwitchTab(IslandTab.Planner);

    private async Task LoadPlannerAsync()
    {
        _planner = await _plannerStore.LoadAsync();
        if (!_isDisposed && _activeTab == IslandTab.Planner) RenderPlanner();
    }

    private async void SavePlanner()
    {
        if (_planner is null) return;
        try
        {
            await _plannerStore.SaveAsync(_planner);
        }
        catch (Exception ex)
        {
            App.Log("Planner: no se pudo guardar.", ex);
        }
    }

    private void PlannerPrevWeek_Click(object sender, RoutedEventArgs e) => ShowPlannerWeek(_plannerWeek.AddDays(-7));

    private void PlannerNextWeek_Click(object sender, RoutedEventArgs e) => ShowPlannerWeek(_plannerWeek.AddDays(7));

    private void PlannerToday_Click(object sender, RoutedEventArgs e) => ShowPlannerWeek(DateOnly.FromDateTime(DateTime.Today));

    private void ShowPlannerWeek(DateOnly week)
    {
        _plannerWeek = week;
        _plannerEditingKey = null;
        RenderPlanner();
    }

    private void RenderPlanner()
    {
        if (_planner is null) return;
        _plannerRenderGeneration++;

        var today = DateOnly.FromDateTime(DateTime.Today);
        PlannerWeekLabel.Text = PlannerWeek.Describe(_plannerWeek);
        PlannerTodayButton.Visibility = PlannerWeek.StartOf(_plannerWeek) == PlannerWeek.StartOf(today)
            ? Visibility.Collapsed
            : Visibility.Visible;

        PlannerGrid.Children.Clear();

        var cells = PlannerWeek.CellsOf(_plannerWeek);
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var meetings = _events.Count(e => cell.Contains(DateOnly.FromDateTime(e.StartTime.LocalDateTime)));
            var element = CreatePlannerCell(cell.Key, cell.Label, _planner.NotesOf(cell.Key), cell.Contains(today), meetings);
            Grid.SetColumn(element, i % 3);
            Grid.SetRow(element, i / 3);
            PlannerGrid.Children.Add(element);
        }

        var reminders = CreatePlannerCell(PlannerRemindersKey, "Recuerda", _planner.Reminders, isToday: false, meetings: 0);
        Grid.SetColumn(reminders, 3);
        Grid.SetRowSpan(reminders, 2);
        PlannerGrid.Children.Add(reminders);
    }

    private FrameworkElement CreatePlannerCell(string key, string label, IReadOnlyList<PlannerNote> notes, bool isToday, int meetings)
    {
        var layout = new Grid { RowSpacing = 3 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Encabezado: día, reuniones que tiene y botón para agregar.
        var header = new Grid { ColumnSpacing = 4 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(isToday ? AccentColor : Colors.White),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        });
        if (meetings > 0)
        {
            title.Children.Add(new TextBlock
            {
                Text = meetings == 1 ? "1 reunión" : $"{meetings} reuniones",
                FontSize = 10,
                Foreground = MutedTextBrush,
            });
        }
        header.Children.Add(title);

        var add = IconButton("", ColorHelper.FromArgb(255, 150, 156, 170), $"Agregar nota a {label}", () =>
        {
            _plannerEditingKey = key;
            RenderPlanner();
        });
        add.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(add, 1);
        header.Children.Add(add);
        layout.Children.Add(header);

        // Notas y, si corresponde, el campo para escribir una nueva.
        var list = new StackPanel { Spacing = 2 };
        foreach (var note in notes) list.Children.Add(CreatePlannerNote(note));
        if (_plannerEditingKey == key) list.Children.Add(CreatePlannerInput(key));

        var scroll = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);

        return new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 24, 26, 33)),
            BorderBrush = new SolidColorBrush(isToday ? Accent(170) : ColorHelper.FromArgb(28, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 5, 4, 6),
            Child = layout,
        };
    }

    private FrameworkElement CreatePlannerNote(PlannerNote note)
    {
        var row = new Grid { ColumnSpacing = 4 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new Button
        {
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(note.Done ? AccentColor : ColorHelper.FromArgb(120, 255, 255, 255)),
            Background = new SolidColorBrush(note.Done ? AccentColor : Colors.Transparent),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 0, 0),
        };
        if (note.Done)
        {
            check.Content = new FontIcon { Glyph = "", FontSize = 10, Foreground = new SolidColorBrush(Colors.White) };
            ApplyAccentButtonStates(check);
        }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(check, note.Done ? "Desmarcar nota" : "Marcar nota como hecha");
        check.Click += (_, _) =>
        {
            if (_planner?.Toggle(note.Id) != true) return;
            SavePlanner();
            RenderPlanner();
        };
        row.Children.Add(check);

        var text = new TextBlock
        {
            Text = note.Text,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = note.Done ? MutedTextBrush : new SolidColorBrush(ColorHelper.FromArgb(255, 235, 237, 242)),
            TextDecorations = note.Done ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None,
        };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        var remove = IconButton("", ColorHelper.FromArgb(150, 150, 156, 170), "Borrar nota", () =>
        {
            if (_planner?.Remove(note.Id) != true) return;
            SavePlanner();
            RenderPlanner();
        });
        remove.Width = 20;
        remove.Height = 20;
        remove.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(remove, 2);
        row.Children.Add(remove);

        return row;
    }

    private TextBox CreatePlannerInput(string key)
    {
        var input = new TextBox
        {
            FontSize = 11,
            MinHeight = 26,
            Padding = new Thickness(7, 3, 7, 3),
            Margin = new Thickness(0, 2, 5, 0),
            MaxLength = 120,
            PlaceholderText = "Escribe y pulsa Enter",
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 28, 29, 34)),
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 240, 242, 245)),
        };

        // Oscuro también al escribir: el tema por defecto lo vuelve blanco al enfocarlo.
        input.Resources["TextControlBackgroundFocused"] = new SolidColorBrush(ColorHelper.FromArgb(255, 28, 29, 34));
        input.Resources["TextControlBackgroundPointerOver"] = new SolidColorBrush(ColorHelper.FromArgb(255, 28, 29, 34));
        input.Resources["TextControlForegroundFocused"] = new SolidColorBrush(ColorHelper.FromArgb(255, 240, 242, 245));
        input.Resources["TextControlBorderBrushFocused"] = new SolidColorBrush(AccentColor);

        var generation = _plannerRenderGeneration;
        input.KeyDown += (_, e) => PlannerInput_KeyDown(key, input, e);
        input.LostFocus += (_, _) =>
        {
            // Si el planner ya se redibujó, este campo fue reemplazado y su texto ya
            // se guardó con Enter: volver a guardarlo crearía una nota duplicada.
            if (generation != _plannerRenderGeneration || _plannerEditingKey != key) return;

            // Al salir del campo sin escribir, se cierra. Si había texto, no se pierde.
            CommitPlannerInput(key, input.Text);
            _plannerEditingKey = null;
            DispatcherQueue.TryEnqueue(RenderPlanner);
        };
        input.Loaded += (_, _) => input.Focus(FocusState.Programmatic);
        return input;
    }

    private void PlannerInput_KeyDown(string key, TextBox input, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            // El campo queda abierto para anotar varias cosas seguidas.
            if (CommitPlannerInput(key, input.Text)) RenderPlanner();
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            _plannerEditingKey = null;
            RenderPlanner();
        }
    }

    private bool CommitPlannerInput(string key, string text)
    {
        if (_planner is null) return false;

        var note = key == PlannerRemindersKey ? _planner.AddReminder(text) : _planner.AddNote(key, text);
        if (note is null) return false;

        SavePlanner();
        return true;
    }
}
