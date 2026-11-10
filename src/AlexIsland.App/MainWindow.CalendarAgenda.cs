using AlexIsland.App.Services;
using AlexIsland.Core.Calendar;
using AlexIsland.Core.Models;
using AlexIsland.Core.Security;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace AlexIsland.App;

public sealed partial class MainWindow : UserControl
{
    // ==============================================================
    // AGENDA DEL DÍA: tarjetas, línea de "ahora" y altura según el contenido
    // ==============================================================

    /// <summary>Alto máximo de la lista (el de antes); con pocas reuniones la isla se achica.</summary>
    private const double AgendaListMaxHeight = 136;
    private const double AgendaListMinHeight = 44;

    /// <summary>"Unirse" se destaca en naranja desde estos minutos antes del inicio.</summary>
    private static readonly TimeSpan JoinHighlightLead = TimeSpan.FromMinutes(10);

    private double _agendaListHeight = AgendaListMaxHeight;

    /// <summary>Hay una reunión empezando pronto o en curso (punto naranja en la pestaña Agenda).</summary>
    private bool _upcomingMeetingSoon;

    private static readonly SolidColorBrush MutedTextBrush = new(ColorHelper.FromArgb(255, 143, 169, 181));

    /// <summary>Nombre visible de cada categoría (en disco se guarda el código en inglés).</summary>
    private static string CategoryLabel(string? category) => category switch
    {
        "MEETING" => "Reunión",
        "DEV" => "Desarrollo",
        "REVIEW" => "Revisión",
        "PLANNING" => "Planificación",
        "DEADLINE" => "Entrega",
        _ => "General",
    };

    /// <summary>"Reunión de Outlook", "Revisión de Google"; lo creado en la isla muestra solo la categoría.</summary>
    private static string DetailLabel(CalendarEvent ev) => ev.Source?.ToUpperInvariant() switch
    {
        "OUTLOOK" => $"{CategoryLabel(ev.Category)} de Outlook",
        "GOOGLE" => $"{CategoryLabel(ev.Category)} de Google",
        "ICAL" => $"{CategoryLabel(ev.Category)} importada",
        _ => CategoryLabel(ev.Category),
    };

    /// <summary>Las reuniones traídas de Outlook se editan allá: aquí se reemplazarían en la próxima lectura.</summary>
    private static bool IsSyncedFromOutlook(CalendarEvent ev) =>
        ev.Id.StartsWith(OutlookCalendarSync.IdPrefix, StringComparison.Ordinal);

    private static string FormatCountdown(TimeSpan span)
    {
        var minutes = (int)Math.Ceiling(span.TotalMinutes);
        if (minutes < 60) return $"en {Math.Max(1, minutes)} min";
        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? $"en {hours} h" : $"en {hours} h {rest} min";
    }

    private void RenderAgendaEvents(StackPanel container, DateTime targetDate)
    {
        container.Children.Clear();

        var now = DateTimeOffset.Now;
        var isToday = targetDate == DateTime.Today;
        var dayEvents = _events
            .Where(e => e.StartTime.Date == targetDate)
            .OrderBy(e => e.IsAllDay ? 0 : 1)
            .ThenBy(e => e.StartTime)
            .ToList();

        if (dayEvents.Count == 0)
        {
            // Un día vacío invita a actuar: traer Outlook o crear un evento.
            var empty = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
            empty.Children.Add(new TextBlock
            {
                Text = isToday ? "No tienes reuniones hoy" : "No hay eventos este día",
                FontSize = 11.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 220, 227, 229)),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            empty.Children.Add(new TextBlock
            {
                Text = _settings.SyncOutlookCalendar
                    ? "Crea uno con \"+ Evento\"."
                    : "Trae tu calendario con el botón de sincronizar o crea uno con \"+ Evento\".",
                FontSize = 10,
                Foreground = MutedTextBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });
            container.Children.Add(empty);
            UpdateAgendaHeight(container);
            return;
        }

        // La próxima reunión de hoy que aún no empieza se destaca con su cuenta regresiva.
        var next = isToday
            ? dayEvents.FirstOrDefault(e => !e.IsAllDay && e.StartTime > now)
            : null;
        var nowLineAdded = !isToday;

        foreach (var ev in dayEvents)
        {
            if (!nowLineAdded && !ev.IsAllDay && ev.StartTime > now)
            {
                container.Children.Add(CreateNowLine(now));
                nowLineAdded = true;
            }

            container.Children.Add(CreateAgendaCard(ev, now, ReferenceEquals(ev, next)));
        }

        // Si todo ya pasó, la línea queda al final.
        if (!nowLineAdded && dayEvents.Any(e => !e.IsAllDay))
        {
            container.Children.Add(CreateNowLine(now));
        }

        UpdateAgendaHeight(container);
    }

    private FrameworkElement CreateAgendaCard(CalendarEvent ev, DateTimeOffset now, bool isNext)
    {
        var isPast = !ev.IsAllDay && ev.EndTime < now;
        var isLive = !ev.IsAllDay && now >= ev.StartTime && now <= ev.EndTime;
        var isConflicted = !ev.IsAllDay && CalendarEventStore.FindConflicts(ev, _events).Count > 0;
        var (_, categoryBrush) = GetEventCategoryBrushes(ev.Category);

        var card = new Border
        {
            Background = new SolidColorBrush(isLive
                ? Accent(34)
                : ColorHelper.FromArgb(255, 10, 50, 73)),
            BorderBrush = new SolidColorBrush(isLive || isNext
                ? Accent(140)
                : ColorHelper.FromArgb(28, 251, 243, 242)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(0, 5, 6, 5),
            Opacity = isPast ? 0.5 : 1.0,
        };

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Col 0: barra de color (naranja si es la próxima o está en curso; si no, la de su categoría).
        var bar = new Rectangle
        {
            Width = 3,
            RadiusX = 1.5,
            RadiusY = 1.5,
            Margin = new Thickness(0, 1, 0, 1),
            Fill = isLive || isNext ? new SolidColorBrush(AccentColor) : categoryBrush,
        };
        grid.Children.Add(bar);

        // Col 1: hora de inicio y de término.
        var timeStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 0 };
        if (ev.IsAllDay)
        {
            timeStack.Children.Add(new TextBlock { Text = "Todo", FontSize = 10.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(TextColor) });
            timeStack.Children.Add(new TextBlock { Text = "el día", FontSize = 9.5, Foreground = MutedTextBrush });
        }
        else
        {
            timeStack.Children.Add(new TextBlock
            {
                Text = ev.StartTime.ToString("HH:mm"),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(isLive ? AccentColor : TextColor),
            });
            timeStack.Children.Add(new TextBlock { Text = ev.EndTime.ToString("HH:mm"), FontSize = 9.5, Foreground = MutedTextBrush });
        }
        Grid.SetColumn(timeStack, 1);
        grid.Children.Add(timeStack);

        // Col 2: título y una línea de detalle.
        var info = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = ev.Title,
            FontSize = 11.5,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(TextColor),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        });

        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (isLive)
        {
            meta.Children.Add(MetaText($"En curso, termina a las {ev.EndTime:HH:mm}", AccentColor, bold: true));
        }
        else if (isNext)
        {
            meta.Children.Add(MetaText("Empieza " + FormatCountdown(ev.StartTime - now), AccentColor, bold: true));
        }

        meta.Children.Add(new TextBlock
        {
            Text = DetailLabel(ev),
            FontSize = 9.5,
            Foreground = MutedTextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        });

        // Invitación de Outlook que aún no se acepta ni rechaza: se marca para no olvidarla.
        if (ev.NeedsResponse && !isPast)
        {
            meta.Children.Add(new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(40, 255, 194, 92)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 0, 5, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = MetaText("Sin responder", ColorHelper.FromArgb(255, 255, 201, 122), bold: true),
            });
        }

        if (isConflicted && !isPast)
        {
            meta.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                Children =
                {
                    new FontIcon { Glyph = "", FontSize = 9, Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 255, 194, 92)) },
                    MetaText("Se solapa", ColorHelper.FromArgb(255, 255, 194, 92), bold: true),
                },
            });
        }
        info.Children.Add(meta);

        if (!string.IsNullOrWhiteSpace(ev.LinkedTaskTitle))
        {
            info.Children.Add(new TextBlock
            {
                Text = $"Tarea: {ev.LinkedTaskTitle}",
                FontSize = 9.5,
                Foreground = new SolidColorBrush(AccentColor),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            });
        }

        Grid.SetColumn(info, 2);
        grid.Children.Add(info);

        // Col 3: acciones.
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };

        if (!isPast && MeetingUrlPolicy.TryGetSafeUri(ev.MeetingUrl, out _))
        {
            // Naranja solo cuando toca unirse (en curso o a punto de empezar); si no, un botón discreto.
            var isJoinTime = isLive || (ev.StartTime > now && ev.StartTime - now <= JoinHighlightLead);
            var joinBtn = new Button
            {
                Height = 24,
                Padding = new Thickness(8, 0, 8, 0),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(isJoinTime ? AccentColor : ColorHelper.FromArgb(255, 17, 64, 89)),
                Foreground = new SolidColorBrush(isJoinTime ? OnAccentColor : ColorHelper.FromArgb(255, 220, 227, 229)),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        new FontIcon { Glyph = "", FontSize = 11 },
                        new TextBlock { Text = "Unirse", FontSize = 10.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    },
                },
            };
            if (isJoinTime) ApplyAccentButtonStates(joinBtn);
            joinBtn.Click += async (_, _) => await LaunchMeetingAsync(ev.MeetingUrl);
            actions.Children.Add(joinBtn);
        }

        if (!IsSyncedFromOutlook(ev))
        {
            actions.Children.Add(IconButton("", ColorHelper.FromArgb(200, 143, 169, 181), "Editar", () => OpenEditForm(ev)));
            actions.Children.Add(IconButton("", ColorHelper.FromArgb(200, 255, 127, 120), "Eliminar", () =>
            {
                _events.Remove(ev);
                _ = _calendarStore.SaveAsync(_events);
                UpdateCalendarBadges();
                CheckUpcomingMeetings();
                RenderCalendarView();
            }));
        }

        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        card.Child = grid;
        return card;
    }

    private static TextBlock MetaText(string text, Windows.UI.Color color, bool bold = false) => new()
    {
        Text = text,
        FontSize = 9.5,
        FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        Foreground = new SolidColorBrush(color),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Button IconButton(string glyph, Windows.UI.Color color, string name, Action onClick)
    {
        var button = new Button
        {
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new FontIcon { Glyph = glyph, FontSize = 11, Foreground = new SolidColorBrush(color) },
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Línea naranja con la hora actual entre las reuniones que ya pasaron y las que vienen.</summary>
    private FrameworkElement CreateNowLine(DateTimeOffset now)
    {
        var grid = new Grid { ColumnSpacing = 6, Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(new TextBlock
        {
            Text = $"Ahora {now:HH:mm}",
            FontSize = 9.5,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(AccentColor),
        });

        var line = new Rectangle
        {
            Height = 1,
            Fill = new SolidColorBrush(Accent(150)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(line, 1);
        grid.Children.Add(line);
        return grid;
    }

    /// <summary>
    /// Ajusta el alto de la lista de la agenda a su contenido (con tope) para que
    /// la isla no muestre medio panel vacío cuando hay pocas reuniones.
    /// </summary>
    private void UpdateAgendaHeight(StackPanel container)
    {
        if (!ReferenceEquals(container, AgendaEventsList)) return;

        container.Measure(new Windows.Foundation.Size(ExpandedWidth - 30, double.PositiveInfinity));
        _agendaListHeight = Math.Clamp(Math.Ceiling(container.DesiredSize.Height) + 2, AgendaListMinHeight, AgendaListMaxHeight);
        AgendaScrollViewer.Height = _agendaListHeight;
        RefreshCalendarHeight();
    }

    /// <summary>Alto de la pestaña Agenda: completo para Semana, Mes y formularios; ajustado en la vista de día.</summary>
    private int CalendarExpandedHeight =>
        _calendarSubMode == CalendarSubMode.Agenda
        && CalEventFormOverlay.Visibility != Visibility.Visible
        && CalSyncOverlay.Visibility != Visibility.Visible
            ? ExpandedCalendarHeight - (int)(AgendaListMaxHeight - _agendaListHeight)
            : ExpandedCalendarHeight;

    /// <summary>Recalcula el tamaño de la isla si la agenda está abierta y su alto cambió.</summary>
    private void RefreshCalendarHeight()
    {
        if (_isExpanded && _activeTab == IslandTab.Calendar && (int)IslandContainer.Height != CalendarExpandedHeight)
        {
            ApplyIslandState();
        }
    }
}
