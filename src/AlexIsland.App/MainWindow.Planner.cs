using AlexIsland.App.Services;
using AlexIsland.Core.Models;
using AlexIsland.Core.Planner;
using AlexIsland.Core.Security;
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
    // SECCIÓN PLANNER: HORARIO LABORAL DE LUNES A VIERNES
    // ==============================================================
    //
    // Una fila por cada hora del horario (Ajustes) y una columna por día, con las
    // reuniones reales en su bloque y notas libres. "Recuerda" no depende de la
    // fecha. Se redibuja solo cuando algo cambia o cambia la hora en curso.

    private const int PlannerHeaderHeight = 118;
    private const int PlannerRowHeight = 26;
    private const int PlannerMenuTextLength = 40;
    private const string PlannerRemindersKey = "reminders";

    private readonly PlannerStore _plannerStore = new();
    private PlannerBoard? _planner;
    private DateOnly _plannerWeek = PlannerWeek.CurrentWorkWeek(DateOnly.FromDateTime(DateTime.Today));

    /// <summary>Bloque donde se está escribiendo una nota nueva ("clave|hora"), o la lista "Recuerda".</summary>
    private string? _plannerEditingSlot;

    /// <summary>Aumenta con cada redibujado; permite reconocer un campo de texto ya reemplazado.</summary>
    private int _plannerRenderGeneration;

    /// <summary>Hora resaltada en el último dibujo: al cambiar, se redibuja.</summary>
    private int _plannerRenderedHour = -1;

    private int ExpandedPlannerHeight =>
        PlannerHeaderHeight + (_settings.PlannerEndHour - _settings.PlannerStartHour) * PlannerRowHeight;

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

    private void PlannerToday_Click(object sender, RoutedEventArgs e) =>
        ShowPlannerWeek(PlannerWeek.CurrentWorkWeek(DateOnly.FromDateTime(DateTime.Today)));

    private void ShowPlannerWeek(DateOnly week)
    {
        _plannerWeek = week;
        _plannerEditingSlot = null;
        RenderPlanner();
    }

    /// <summary>Al cambiar la hora, el bloque resaltado debe moverse (salvo que se esté escribiendo).</summary>
    private void RefreshPlannerIfHourChanged()
    {
        if (_isExpanded && _activeTab == IslandTab.Planner && _plannerEditingSlot is null && DateTime.Now.Hour != _plannerRenderedHour)
        {
            RenderPlanner();
        }
    }

    private void RenderPlanner()
    {
        if (_planner is null) return;
        _plannerRenderGeneration++;

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        _plannerRenderedHour = now.Hour;

        PlannerWeekLabel.Text = PlannerWeek.Describe(_plannerWeek);
        PlannerTodayButton.Visibility = PlannerWeek.StartOf(_plannerWeek) == PlannerWeek.CurrentWorkWeek(today)
            ? Visibility.Collapsed
            : Visibility.Visible;

        var start = _settings.PlannerStartHour;
        var end = _settings.PlannerEndHour;
        var hours = PlannerHours.Blocks(start, end);
        var days = PlannerWeek.DaysOf(_plannerWeek);

        PlannerGrid.Children.Clear();
        PlannerGrid.RowDefinitions.Clear();
        PlannerGrid.ColumnDefinitions.Clear();

        PlannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        foreach (var _ in days) PlannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        PlannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });

        PlannerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var _ in hours) PlannerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Encabezado: un día por columna.
        for (var d = 0; d < days.Count; d++)
        {
            var header = new TextBlock
            {
                Text = days[d].Label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(days[d].Date == today ? AccentColor : Colors.White),
                Margin = new Thickness(5, 0, 0, 2),
            };
            Grid.SetColumn(header, d + 1);
            PlannerGrid.Children.Add(header);
        }

        // Filas: una hora y un bloque por cada día.
        for (var h = 0; h < hours.Count; h++)
        {
            var hour = hours[h];
            var label = new TextBlock
            {
                Text = $"{hour:00}:00",
                FontSize = 10,
                Foreground = MutedTextBrush,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(label, h + 1);
            PlannerGrid.Children.Add(label);

            for (var d = 0; d < days.Count; d++)
            {
                var slot = CreatePlannerSlot(days[d], hour, days[d].Date == today && now.Hour == hour);
                Grid.SetRow(slot, h + 1);
                Grid.SetColumn(slot, d + 1);
                PlannerGrid.Children.Add(slot);
            }
        }

        var reminders = CreatePlannerReminders();
        Grid.SetColumn(reminders, days.Count + 1);
        Grid.SetRowSpan(reminders, hours.Count + 1);
        PlannerGrid.Children.Add(reminders);
    }

    /// <summary>Reuniones que ocupan un bloque; las de todo el día no son de una hora concreta.</summary>
    private List<CalendarEvent> PlannerMeetingsAt(DateOnly date, int hour) => _events
        .Where(e => !e.IsAllDay && PlannerHours.Overlaps(e.StartTime.LocalDateTime, e.EndTime.LocalDateTime, date, hour))
        .OrderBy(e => e.StartTime)
        .ToList();

    private FrameworkElement CreatePlannerSlot(PlannerDay day, int hour, bool isNow)
    {
        var slotKey = $"{day.Key}|{hour}";
        if (_plannerEditingSlot == slotKey) return CreatePlannerInput(slotKey, day.Key, hour);

        var meetings = PlannerMeetingsAt(day.Date, hour);
        var notes = _planner!.NotesAt(day.Key, hour, _settings.PlannerStartHour, _settings.PlannerEndHour);

        // Una reunión se pinta en todos sus bloques, pero el título solo donde empieza.
        var startingHere = meetings.FirstOrDefault(m => m.StartTime.LocalDateTime.Hour == hour && DateOnly.FromDateTime(m.StartTime.LocalDateTime) == day.Date);
        var firstNote = notes.FirstOrDefault();

        string text;
        if (startingHere is not null) text = startingHere.Title;
        else if (firstNote is not null) text = firstNote.Text;
        else text = string.Empty;

        var isDoneNote = startingHere is null && firstNote is { Done: true };
        var visibleItems = (startingHere is not null ? 1 : 0) + notes.Count + meetings.Count(m => !ReferenceEquals(m, startingHere) && m.StartTime.LocalDateTime.Hour == hour);
        var hidden = Math.Max(0, visibleItems - 1);

        var background = meetings.Count > 0
            ? ColorHelper.FromArgb(70, 66, 165, 245)
            : notes.Count > 0
                ? ColorHelper.FromArgb(255, 40, 42, 51)
                : ColorHelper.FromArgb(255, 24, 26, 33);

        var row = new Grid { ColumnSpacing = 2 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Foreground = isDoneNote ? MutedTextBrush : new SolidColorBrush(ColorHelper.FromArgb(255, 235, 237, 242)),
            TextDecorations = isDoneNote ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None,
        });

        if (hidden > 0)
        {
            var more = new TextBlock { Text = $"+{hidden}", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = MutedTextBrush, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(more, 1);
            row.Children.Add(more);
        }

        var slot = new Border
        {
            Background = new SolidColorBrush(background),
            BorderBrush = new SolidColorBrush(isNow ? AccentColor : ColorHelper.FromArgb(18, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(5, 0, 4, 0),
            Margin = new Thickness(1),
            Child = row,
        };

        slot.Tapped += (_, e) =>
        {
            // Que el clic no llegue a la isla: la fijaría o soltaría.
            e.Handled = true;

            if (meetings.Count == 0 && notes.Count == 0)
            {
                BeginPlannerNote(slotKey);
                return;
            }
            ShowPlannerSlotMenu(slot, slotKey, meetings, notes);
        };
        return slot;
    }

    private void BeginPlannerNote(string slotKey)
    {
        _plannerEditingSlot = slotKey;
        RenderPlanner();
    }

    private void ShowPlannerSlotMenu(FrameworkElement anchor, string slotKey, List<CalendarEvent> meetings, IReadOnlyList<PlannerNote> notes)
    {
        var menu = new MenuFlyout();

        foreach (var meeting in meetings)
        {
            var title = $"{meeting.StartTime.LocalDateTime:HH:mm} {ShortText(meeting.Title)}";
            if (MeetingUrlPolicy.TryGetSafeUri(meeting.MeetingUrl, out _))
            {
                var join = new MenuFlyoutItem { Text = $"Unirse: {title}", Icon = new FontIcon { Glyph = "" } };
                join.Click += async (_, _) => await LaunchMeetingAsync(meeting.MeetingUrl);
                menu.Items.Add(join);
            }
            else
            {
                menu.Items.Add(new MenuFlyoutItem { Text = title, IsEnabled = false });
            }
        }

        if (meetings.Count > 0 && notes.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());

        foreach (var note in notes)
        {
            var toggle = new MenuFlyoutItem
            {
                Text = $"{(note.Done ? "Desmarcar" : "Marcar como hecha")}: {ShortText(note.Text)}",
                Icon = new FontIcon { Glyph = "" },
            };
            toggle.Click += (_, _) =>
            {
                if (_planner?.Toggle(note.Id) != true) return;
                SavePlanner();
                RenderPlanner();
            };
            menu.Items.Add(toggle);

            var remove = new MenuFlyoutItem { Text = $"Borrar: {ShortText(note.Text)}", Icon = new FontIcon { Glyph = "" } };
            remove.Click += (_, _) =>
            {
                if (_planner?.Remove(note.Id) != true) return;
                SavePlanner();
                RenderPlanner();
            };
            menu.Items.Add(remove);
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        var add = new MenuFlyoutItem { Text = "Agregar una nota aquí", Icon = new FontIcon { Glyph = "" } };
        add.Click += (_, _) => BeginPlannerNote(slotKey);
        menu.Items.Add(add);

        menu.ShowAt(anchor);
    }

    private static string ShortText(string text) =>
        text.Length <= PlannerMenuTextLength ? text : text[..(PlannerMenuTextLength - 1)].TrimEnd() + "…";

    /// <summary>Columna "Recuerda": cosas sin fecha, igual todas las semanas.</summary>
    private FrameworkElement CreatePlannerReminders()
    {
        var layout = new Grid { RowSpacing = 3 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "Recuerda",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var add = IconButton("", ColorHelper.FromArgb(255, 150, 156, 170), "Agregar a Recuerda", () => BeginPlannerNote(PlannerRemindersKey));
        Grid.SetColumn(add, 1);
        header.Children.Add(add);
        layout.Children.Add(header);

        var list = new StackPanel { Spacing = 2 };
        foreach (var note in _planner!.Reminders) list.Children.Add(CreatePlannerReminder(note));
        if (_plannerEditingSlot == PlannerRemindersKey) list.Children.Add(CreatePlannerInput(PlannerRemindersKey, PlannerRemindersKey, null));

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
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(28, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 4, 3, 6),
            Margin = new Thickness(5, 0, 0, 0),
            Child = layout,
        };
    }

    private FrameworkElement CreatePlannerReminder(PlannerNote note)
    {
        var row = new Grid { ColumnSpacing = 4 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new Button
        {
            Width = 16,
            Height = 16,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(note.Done ? AccentColor : ColorHelper.FromArgb(120, 255, 255, 255)),
            Background = new SolidColorBrush(note.Done ? AccentColor : Colors.Transparent),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
        };
        if (note.Done)
        {
            check.Content = new FontIcon { Glyph = "", FontSize = 9, Foreground = new SolidColorBrush(Colors.White) };
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
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 3,
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

    private TextBox CreatePlannerInput(string slotKey, string key, int? hour)
    {
        var input = new TextBox
        {
            FontSize = 10,
            MinHeight = 0,
            MinWidth = 0,
            Padding = new Thickness(5, 2, 3, 2),
            Margin = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center,
            MaxLength = 120,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 28, 29, 34)),
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 240, 242, 245)),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, "Nueva nota");

        // Oscuro también al escribir: el tema por defecto lo vuelve blanco al enfocarlo.
        input.Resources["TextControlBackgroundFocused"] = new SolidColorBrush(ColorHelper.FromArgb(255, 28, 29, 34));
        input.Resources["TextControlBackgroundPointerOver"] = new SolidColorBrush(ColorHelper.FromArgb(255, 28, 29, 34));
        input.Resources["TextControlForegroundFocused"] = new SolidColorBrush(ColorHelper.FromArgb(255, 240, 242, 245));
        input.Resources["TextControlBorderBrushFocused"] = new SolidColorBrush(AccentColor);

        var generation = _plannerRenderGeneration;
        input.Tapped += (_, e) => e.Handled = true;
        input.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                CommitPlannerInput(key, hour, input.Text);
                // En "Recuerda" se puede seguir anotando; en un bloque, ya se ve la nota.
                if (key != PlannerRemindersKey) _plannerEditingSlot = null;
                RenderPlanner();
            }
            else if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                _plannerEditingSlot = null;
                RenderPlanner();
            }
        };
        input.LostFocus += (_, _) =>
        {
            // Si el planner ya se redibujó, este campo fue reemplazado y su texto ya
            // se guardó con Enter: volver a guardarlo crearía una nota duplicada.
            if (generation != _plannerRenderGeneration || _plannerEditingSlot != slotKey) return;

            // Al salir del campo sin escribir, se cierra. Si había texto, no se pierde.
            CommitPlannerInput(key, hour, input.Text);
            _plannerEditingSlot = null;
            DispatcherQueue.TryEnqueue(RenderPlanner);
        };
        input.Loaded += (_, _) => input.Focus(FocusState.Programmatic);
        return input;
    }

    private void CommitPlannerInput(string key, int? hour, string text)
    {
        if (_planner is null) return;

        var note = key == PlannerRemindersKey ? _planner.AddReminder(text) : _planner.AddNote(key, text, hour);
        if (note is not null) SavePlanner();
    }
}
