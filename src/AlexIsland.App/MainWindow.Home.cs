using AlexIsland.Core.Security;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AlexIsland.App;

public sealed partial class MainWindow : UserControl
{
    // ==============================================================
    // VISTA INICIO: columna derecha (próxima reunión y accesos rápidos)
    // ==============================================================

    private string? _homeMeetingUrl;

    /// <summary>Hay una reunión en curso o a punto de empezar: le gana a la letra en la columna derecha.</summary>
    private bool _homeMeetingNeedsAttention;

    /// <summary>
    /// La columna derecha de Inicio muestra la letra mientras suena una canción
    /// que la tiene; si toca unirse a una reunión, la reunión tiene prioridad.
    /// </summary>
    private void UpdateHomeRightColumn()
    {
        var showLyrics = HasLyrics && !_homeMeetingNeedsAttention;
        LyricsPanel.Visibility = showLyrics ? Visibility.Visible : Visibility.Collapsed;
        HomeInfoPanel.Visibility = showLyrics ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Muestra la reunión en curso o la próxima de hoy. "Unirse" se pinta naranja
    /// solo cuando toca entrar (en curso o a 10 minutos o menos); antes es una
    /// píldora gris para no gritar con horas de anticipación.
    /// </summary>
    private void UpdateHomeMeeting()
    {
        var now = DateTimeOffset.Now;
        var meeting = _events
            .Where(e => !e.IsAllDay && e.StartTime.Date == DateTime.Today && e.EndTime > now)
            .OrderBy(e => e.StartTime)
            .FirstOrDefault();

        if (meeting is null)
        {
            HomeMeetingTitle.Text = "No tienes más reuniones hoy";
            HomeMeetingWhen.Text = "Agenda libre";
            HomeMeetingWhen.Foreground = MutedTextBrush;
            HomeJoinButton.Visibility = Visibility.Collapsed;
            _homeMeetingUrl = null;
            _homeMeetingNeedsAttention = false;
            UpdateHomeRightColumn();
            return;
        }

        var isLive = now >= meeting.StartTime;
        var isJoinTime = isLive || meeting.StartTime - now <= JoinHighlightLead;
        _homeMeetingNeedsAttention = isJoinTime;
        UpdateHomeRightColumn();

        HomeMeetingTitle.Text = meeting.Title;
        HomeMeetingWhen.Text = isLive
            ? $"En curso, termina a las {meeting.EndTime:HH:mm}"
            : $"{meeting.StartTime:HH:mm}, empieza {FormatCountdown(meeting.StartTime - now)}";
        HomeMeetingWhen.Foreground = isJoinTime ? new SolidColorBrush(AccentColor) : MutedTextBrush;

        _homeMeetingUrl = MeetingUrlPolicy.Sanitize(meeting.MeetingUrl);
        HomeJoinButton.Visibility = _homeMeetingUrl is null ? Visibility.Collapsed : Visibility.Visible;
        HomeJoinButton.Background = new SolidColorBrush(isJoinTime ? AccentColor : ColorHelper.FromArgb(255, 30, 31, 36));
        if (isJoinTime)
        {
            ApplyAccentButtonStates(HomeJoinButton);
        }
        else
        {
            foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonForegroundPointerOver", "ButtonForegroundPressed" })
            {
                HomeJoinButton.Resources.Remove(key);
            }
        }
    }

    private async void HomeJoinButton_Click(object sender, RoutedEventArgs e) => await LaunchMeetingAsync(_homeMeetingUrl);

    private void HomeCopyInfoButton_Click(object sender, RoutedEventArgs e) =>
        CopyMachineSummary(HomeCopyInfoLabel, "Copiar datos del equipo", "Copiado para el ticket");

    private void HeaderSettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettingsWindow();
}
