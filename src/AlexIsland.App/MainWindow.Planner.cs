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

    /// <summary>Día (o "Recuerda") donde se está escribiendo una nota nueva, y los bloques que ocupará.</summary>
    private string? _plannerEditingDay;
    private int _plannerEditingHour;
    private int _plannerEditingSpan = 1;

    /// <summary>Selección en curso al arrastrar sobre bloques libres para combinarlos.</summary>
    private sealed record PlannerDrag(string Day, int Anchor, int Current);
    private sealed record PlannerEmptySlot(Border Border, Brush NormalBrush);
    private PlannerDrag? _plannerDrag;
    private readonly Dictionary<(string Day, int Hour), PlannerEmptySlot> _plannerEmptySlots = [];

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
        EndPlannerEditing();
        RenderPlanner();
    }

    /// <summary>Al cambiar la hora, el bloque resaltado debe moverse (salvo que se esté escribiendo).</summary>
    private void RefreshPlannerIfHourChanged()
    {
        if (_isExpanded && _activeTab == IslandTab.Planner && _plannerEditingDay is null && DateTime.Now.Hour != _plannerRenderedHour)
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

        // Filas: la etiqueta de cada hora.
        for (var h = 0; h < hours.Count; h++)
        {
            var label = new TextBlock
            {
                Text = $"{hours[h]:00}:00",
                FontSize = 10,
                Foreground = MutedTextBrush,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(label, h + 1);
            PlannerGrid.Children.Add(label);
        }

        // Bloques de cada día: una hora, o varias si se combinaron.
        _plannerEmptySlots.Clear();
        _plannerDrag = null;
        for (var d = 0; d < days.Count; d++)
        {
            var day = days[d];
            var editing = _plannerEditingDay == day.Key;

            foreach (var block in PlannerHours.LayoutDay(_planner.NotesOf(day.Key), start, end))
            {
                FrameworkElement element;
                int span;
                if (editing && block.Hour >= _plannerEditingHour && block.Hour < _plannerEditingHour + _plannerEditingSpan)
                {
                    // El campo de texto ocupa toda la selección; el resto de esos bloques no se dibuja.
                    if (block.Hour != _plannerEditingHour) continue;
                    span = _plannerEditingSpan;
                    element = CreatePlannerInput(day.Key, _plannerEditingHour, span);
                }
                else
                {
                    span = block.Span;
                    element = CreatePlannerSlot(day, block, day.Date == today && now.Hour >= block.Hour && now.Hour < block.Hour + block.Span);
                }

                Grid.SetRow(element, block.Hour - start + 1);
                Grid.SetRowSpan(element, span);
                Grid.SetColumn(element, d + 1);
                PlannerGrid.Children.Add(element);
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

    private FrameworkElement CreatePlannerSlot(PlannerDay day, PlannerBlock block, bool isNow)
    {
        // Todas las reuniones que tocan el bloque, sin repetir las que duran varias horas.
        var meetings = Enumerable.Range(block.Hour, block.Span)
            .SelectMany(hour => PlannerMeetingsAt(day.Date, hour))
            .Distinct()
            .OrderBy(m => m.StartTime)
            .ToList();
        var notes = block.Notes;

        // Una reunión se pinta en todos sus bloques, pero el título solo donde empieza.
        var startingHere = meetings.Where(m => StartsInBlock(m, day.Date, block)).ToList();
        var firstNote = notes.FirstOrDefault();

        var text = startingHere.Count > 0 ? startingHere[0].Title : firstNote?.Text ?? string.Empty;
        var isDoneNote = startingHere.Count == 0 && firstNote is { Done: true };
        var hidden = Math.Max(0, startingHere.Count + notes.Count - 1);

        var row = new Grid { ColumnSpacing = 2 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            // Un bloque combinado tiene espacio para más renglones.
            TextWrapping = block.Span > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MaxLines = (block.Span * 2) - 1,
            Foreground = isDoneNote ? MutedTextBrush : new SolidColorBrush(ColorHelper.FromArgb(255, 235, 237, 242)),
            TextDecorations = isDoneNote ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None,
        });

        if (hidden > 0)
        {
            var more = new TextBlock { Text = $"+{hidden}", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = MutedTextBrush, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(more, 1);
            row.Children.Add(more);
        }

        var isEmpty = meetings.Count == 0 && notes.Count == 0;
        var normalBrush = new SolidColorBrush(meetings.Count > 0
            ? ColorHelper.FromArgb(70, 66, 165, 245)
            : notes.Count > 0
                ? ColorHelper.FromArgb(255, 40, 42, 51)
                : ColorHelper.FromArgb(255, 24, 26, 33));

        var slot = new Border
        {
            Background = normalBrush,
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

            if (isEmpty)
            {
                BeginPlannerNote(day.Key, block.Hour, block.Span);
                return;
            }
            ShowPlannerSlotMenu(slot, day.Key, block, meetings, notes);
        };

        if (isEmpty)
        {
            // Solo los bloques libres se pueden combinar arrastrando, como en Excel.
            _plannerEmptySlots[(day.Key, block.Hour)] = new PlannerEmptySlot(slot, normalBrush);
            slot.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(slot).Properties.IsLeftButtonPressed)
                {
                    _plannerDrag = new PlannerDrag(day.Key, block.Hour, block.Hour);
                }
            };
            slot.PointerEntered += (_, _) => ExtendPlannerDrag(day.Key, block.Hour);
            slot.PointerReleased += (_, _) => FinishPlannerDrag();
        }

        return slot;
    }

    private static bool StartsInBlock(CalendarEvent meeting, DateOnly date, PlannerBlock block)
    {
        var start = meeting.StartTime.LocalDateTime;
        return DateOnly.FromDateTime(start) == date && start.Hour >= block.Hour && start.Hour < block.Hour + block.Span;
    }

    private void ExtendPlannerDrag(string dayKey, int hour)
    {
        if (_plannerDrag is not { } drag || drag.Day != dayKey) return;

        // Solo hasta donde todos los bloques sigan libres, y sin cambiar de día.
        var from = Math.Min(drag.Anchor, hour);
        var to = Math.Max(drag.Anchor, hour);
        for (var h = from; h <= to; h++)
        {
            if (!_plannerEmptySlots.ContainsKey((dayKey, h))) return;
        }

        _plannerDrag = drag with { Current = hour };
        PaintPlannerSelection();
    }

    private void PaintPlannerSelection()
    {
        foreach (var ((day, hour), slot) in _plannerEmptySlots)
        {
            var selected = _plannerDrag is { } drag && drag.Day == day
                && hour >= Math.Min(drag.Anchor, drag.Current) && hour <= Math.Max(drag.Anchor, drag.Current);
            slot.Border.Background = selected ? new SolidColorBrush(Accent(70)) : slot.NormalBrush;
        }
    }

    /// <summary>Al soltar sobre varios bloques se escribe una nota combinada; con uno solo, actúa el clic normal.</summary>
    private void FinishPlannerDrag()
    {
        if (_plannerDrag is not { } drag) return;
        _plannerDrag = null;

        var first = Math.Min(drag.Anchor, drag.Current);
        var span = Math.Abs(drag.Current - drag.Anchor) + 1;
        if (span > 1) BeginPlannerNote(drag.Day, first, span);
        else PaintPlannerSelection();
    }

    private void CancelPlannerDrag()
    {
        if (_plannerDrag is null) return;
        _plannerDrag = null;
        PaintPlannerSelection();
    }

    private void PlannerGrid_PointerReleased(object sender, PointerRoutedEventArgs e) => CancelPlannerDrag();

    private void PlannerGrid_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        // Este evento también sube desde cada bloque: solo cuenta si el cursor salió de la cuadrícula.
        var position = e.GetCurrentPoint(PlannerGrid).Position;
        if (position.X < 0 || position.Y < 0 || position.X >= PlannerGrid.ActualWidth || position.Y >= PlannerGrid.ActualHeight)
        {
            CancelPlannerDrag();
        }
    }

    private void BeginPlannerNote(string dayKey, int hour, int span)
    {
        _plannerEditingDay = dayKey;
        _plannerEditingHour = hour;
        _plannerEditingSpan = span;
        RenderPlanner();
    }

    private void EndPlannerEditing()
    {
        _plannerEditingDay = null;
        _plannerEditingHour = 0;
        _plannerEditingSpan = 1;
    }

    private void ShowPlannerSlotMenu(FrameworkElement anchor, string dayKey, PlannerBlock block, List<CalendarEvent> meetings, IReadOnlyList<PlannerNote> notes)
    {
        var menu = new MenuFlyout();

        foreach (var meeting in meetings)
        {
            var title = $"{meeting.StartTime.LocalDateTime:HH:mm} {ShortText(meeting.Title)}";
            if (MeetingUrlPolicy.TryGetSafeUri(meeting.MeetingUrl, out _))
            {
                var join = new MenuFlyoutItem { Text = $"Unirse: {title}", Icon = new FontIcon { Glyph = "\uE714" } };
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
                Icon = new FontIcon { Glyph = "\uE73E" },
            };
            toggle.Click += (_, _) =>
            {
                if (_planner?.Toggle(note.Id) != true) return;
                SavePlanner();
                RenderPlanner();
            };
            menu.Items.Add(toggle);

            // Combinar con el bloque de abajo, hasta el final del horario.
            var noteHour = PlannerHours.BlockOf(note, _settings.PlannerStartHour, _settings.PlannerEndHour);
            if (noteHour + note.Span < _settings.PlannerEndHour)
            {
                var merge = new MenuFlyoutItem { Text = $"Combinar con el bloque de abajo: {ShortText(note.Text)}", Icon = new FontIcon { Glyph = "\uE74B" } };
                merge.Click += (_, _) => ChangePlannerSpan(note, note.Span + 1);
                menu.Items.Add(merge);
            }

            if (note.Span > 1)
            {
                var split = new MenuFlyoutItem { Text = $"Separar bloques: {ShortText(note.Text)}", Icon = new FontIcon { Glyph = "\uE74A" } };
                split.Click += (_, _) => ChangePlannerSpan(note, 1);
                menu.Items.Add(split);
            }

            var remove = new MenuFlyoutItem { Text = $"Borrar: {ShortText(note.Text)}", Icon = new FontIcon { Glyph = "\uE74D" } };
            remove.Click += (_, _) =>
            {
                if (_planner?.Remove(note.Id) != true) return;
                SavePlanner();
                RenderPlanner();
            };
            menu.Items.Add(remove);
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        var add = new MenuFlyoutItem { Text = "Agregar una nota aquí", Icon = new FontIcon { Glyph = "\uE710" } };
        add.Click += (_, _) => BeginPlannerNote(dayKey, block.Hour, block.Span);
        menu.Items.Add(add);

        menu.ShowAt(anchor);
    }

    private void ChangePlannerSpan(PlannerNote note, int span)
    {
        if (_planner?.SetSpan(note.Id, span) != true) return;
        SavePlanner();
        RenderPlanner();
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

        var add = IconButton("", ColorHelper.FromArgb(255, 150, 156, 170), "Agregar a Recuerda", () => BeginPlannerNote(PlannerRemindersKey, 0, 1));
        Grid.SetColumn(add, 1);
        header.Children.Add(add);
        layout.Children.Add(header);

        var list = new StackPanel { Spacing = 2 };
        foreach (var note in _planner!.Reminders) list.Children.Add(CreatePlannerReminder(note));
        if (_plannerEditingDay == PlannerRemindersKey) list.Children.Add(CreatePlannerInput(PlannerRemindersKey, 0, 1));

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

    private TextBox CreatePlannerInput(string key, int hour, int span)
    {
        var input = new TextBox
        {
            FontSize = 10,
            MinHeight = 0,
            MinWidth = 0,
            // Alto explícito: el mínimo del tema es mayor que una fila y recortaba el borde.
            Height = (PlannerRowHeight * span) - 2,
            VerticalAlignment = VerticalAlignment.Top,
            TextWrapping = span > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap,
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
                CommitPlannerInput(key, hour, span, input.Text);
                // En "Recuerda" se puede seguir anotando; en un bloque, ya se ve la nota.
                if (key != PlannerRemindersKey) EndPlannerEditing();
                RenderPlanner();
            }
            else if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                EndPlannerEditing();
                RenderPlanner();
            }
        };
        input.LostFocus += (_, _) =>
        {
            // Si el planner ya se redibujó, este campo fue reemplazado y su texto ya
            // se guardó con Enter: volver a guardarlo crearía una nota duplicada.
            if (generation != _plannerRenderGeneration || _plannerEditingDay != key) return;

            // Al salir del campo sin escribir, se cierra. Si había texto, no se pierde.
            CommitPlannerInput(key, hour, span, input.Text);
            EndPlannerEditing();
            DispatcherQueue.TryEnqueue(RenderPlanner);
        };
        input.Loaded += (_, _) => input.Focus(FocusState.Programmatic);
        return input;
    }

    private void CommitPlannerInput(string key, int hour, int span, string text)
    {
        if (_planner is null) return;

        var note = key == PlannerRemindersKey ? _planner.AddReminder(text) : _planner.AddNote(key, text, hour, span);
        if (note is not null) SavePlanner();
    }
}
