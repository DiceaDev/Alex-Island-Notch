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
            _upcomingMeetingUrl = MeetingUrlPolicy.Sanitize(upcoming.MeetingUrl);

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
        await LaunchMeetingAsync(_upcomingMeetingUrl);
    }

    /// <summary>
    /// Único punto de la app que abre enlaces de reunión. Vuelve a validar el
    /// enlace justo antes de abrirlo, aunque ya se haya filtrado al importarlo,
    /// porque el archivo de eventos en disco puede haberse modificado.
    /// </summary>
    private static async Task LaunchMeetingAsync(string? meetingUrl)
    {
        if (!MeetingUrlPolicy.TryGetSafeUri(meetingUrl, out var uri)) return;

        try
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch
        {
            // Si el sistema no puede abrir el enlace, no hay nada más que hacer.
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
                    ? new SolidColorBrush(Accent(50))
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
                        ? new SolidColorBrush(AccentColor)
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
                    Background = new SolidColorBrush(Accent(40)),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(3, 0, 3, 0),
                    Child = new TextBlock
                    {
                        Text = $"⚡ {ev.LinkedTaskTitle}",
                        FontSize = 7.5,
                        Foreground = new SolidColorBrush(AccentColor),
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

            if (MeetingUrlPolicy.TryGetSafeUri(ev.MeetingUrl, out _))
            {
                var joinBtn = new Button
                {
                    Height = 20,
                    Padding = new Thickness(5, 0, 5, 0),
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(AccentColor),
                    Foreground = new SolidColorBrush(Colors.White),
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
                ApplyAccentButtonStates(joinBtn);
                joinBtn.Click += async (_, _) => await LaunchMeetingAsync(ev.MeetingUrl);
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
                    ? new SolidColorBrush(AccentColor)
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
                    ? new SolidColorBrush(AccentColor)
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
                    ? new SolidColorBrush(AccentColor)
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
                    ? new SolidColorBrush(AccentColor)
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
        else
        {
            var safeUrl = MeetingUrlPolicy.Sanitize(meetingUrl);
            if (safeUrl is null)
            {
                FormConflictWarning.Text = "⚠️ Enlace no válido: usa https:// de Meet, Teams, Zoom o Webex";
                return;
            }
            meetingUrl = safeUrl;
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

            var properties = await file.GetBasicPropertiesAsync();
            if (properties.Size > (ulong)IcsCalendarHelper.MaxIcsLength)
            {
                ShowSyncStatus("El archivo es demasiado grande para ser un calendario (máx. 2 MB)", isError: true);
                return;
            }

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
                if (LooksLikeUrl(trimmed))
                {
                    if (!TryGetCalendarDownloadUri(trimmed, out var calendarUri))
                    {
                        ShowSyncStatus("Solo se pueden importar calendarios desde enlaces https:// o webcal://", isError: true);
                        return;
                    }

                    ShowSyncStatus("Descargando calendario desde URL...", isError: false);
                    var downloaded = await DownloadCalendarAsync(calendarUri);
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

    private static readonly System.Net.Http.HttpClient CalendarHttpClient = CreateCalendarHttpClient();

    private static System.Net.Http.HttpClient CreateCalendarHttpClient()
    {
        // .NET ya bloquea las redirecciones de https a http; además se limita
        // su número y se fija un tope de tamaño para no cargar respuestas enormes.
        var handler = new System.Net.Http.SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
        };

        return new System.Net.Http.HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15),
            MaxResponseContentBufferSize = IcsCalendarHelper.MaxIcsLength,
        };
    }

    private static bool LooksLikeUrl(string text)
        => !text.Contains('\n') && text.Contains("://", StringComparison.Ordinal);

    /// <summary>
    /// Acepta únicamente enlaces https:// y webcal:// (que se convierte en https://).
    /// http:// sin cifrar y cualquier otro esquema se rechazan.
    /// </summary>
    private static bool TryGetCalendarDownloadUri(string text, out Uri uri)
    {
        uri = null!;
        var candidate = text.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)
            ? "https://" + text["webcal://".Length..]
            : text;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.IsNullOrEmpty(parsed.UserInfo)) return false;
        if (parsed.HostNameType != UriHostNameType.Dns) return false;

        uri = parsed;
        return true;
    }

    private static async Task<string> DownloadCalendarAsync(Uri uri)
    {
        using var response = await CalendarHttpClient.GetAsync(uri);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long length && length > IcsCalendarHelper.MaxIcsLength)
        {
            throw new InvalidDataException("El calendario descargado es demasiado grande (máx. 2 MB).");
        }

        // MaxResponseContentBufferSize aborta la lectura si el servidor
        // envía más datos de los permitidos aunque no declare el tamaño.
        return await response.Content.ReadAsStringAsync();
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
            : ColorHelper.FromArgb(255, 0, 230, 118)); // verde: éxito (semántico)
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
}
