using AlexIsland.Core.Notifications;
using AlexIsland.Core.Settings;

namespace AlexIsland.Core.Tests;

public class NotificationTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private static IslandNotification N(uint id, int minute = 0, string app = "Teams") =>
        new(id, app, $"Título {id}", "Cuerpo", T0.AddMinutes(minute));

    [Fact]
    public void First_read_only_records_existing_notifications()
    {
        var tracker = new NotificationTracker();

        Assert.Empty(tracker.Update([N(1), N(2)]));
    }

    [Fact]
    public void Later_reads_return_only_new_ones_oldest_first()
    {
        var tracker = new NotificationTracker();
        tracker.Update([N(1)]);

        var fresh = tracker.Update([N(1), N(5, minute: 3), N(4, minute: 2)]);

        Assert.Equal([4u, 5u], fresh.Select(n => n.Id));
        Assert.Empty(tracker.Update([N(1), N(4), N(5)]));
    }

    [Fact]
    public void Dismissed_notifications_are_forgotten()
    {
        var tracker = new NotificationTracker();
        tracker.Update([N(1), N(2)]);

        tracker.Update([N(2)]);
        var fresh = tracker.Update([N(1), N(2)]);

        Assert.Equal([1u], fresh.Select(n => n.Id));
    }

    [Fact]
    public void Empty_first_read_still_counts_as_seeding()
    {
        var tracker = new NotificationTracker();
        tracker.Update([]);

        Assert.Single(tracker.Update([N(7)]));
    }

    [Fact]
    public void Reset_makes_next_read_a_seed_again()
    {
        var tracker = new NotificationTracker();
        tracker.Update([N(1)]);
        tracker.Reset();

        Assert.Empty(tracker.Update([N(1), N(2)]));
    }
}

public class NotificationPresenterTests
{
    private static IslandNotification N(string app, string title, string body) =>
        new(1, app, title, body, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Parses_muted_apps_without_duplicates_or_blanks()
    {
        Assert.Equal(["Teams", "Outlook", "slack"],
            NotificationPresenter.ParseMutedApps(" Teams, Outlook ;; slack\nteams ,Slack"));
        Assert.Empty(NotificationPresenter.ParseMutedApps("  "));
        Assert.Empty(NotificationPresenter.ParseMutedApps(null));
    }

    [Fact]
    public void Muted_and_own_apps_are_hidden_case_insensitively()
    {
        var muted = NotificationPresenter.ParseMutedApps("teams");

        Assert.False(NotificationPresenter.ShouldShow(N("Teams", "Hola", ""), muted, "Alex Island"));
        Assert.False(NotificationPresenter.ShouldShow(N("ALEX ISLAND", "Hola", ""), muted, "Alex Island"));
        Assert.True(NotificationPresenter.ShouldShow(N("Outlook", "Hola", ""), muted, "Alex Island"));
    }

    [Fact]
    public void Notifications_without_any_text_are_hidden()
    {
        Assert.False(NotificationPresenter.ShouldShow(N(" ", " ", ""), [], "Alex Island"));
        Assert.True(NotificationPresenter.ShouldShow(N("", "", "Solo cuerpo"), [], "Alex Island"));
    }

    [Fact]
    public void Formats_title_and_app_with_body()
    {
        var (title, subtitle) = NotificationPresenter.Format(N("Teams", "Ana Pérez", "¿Vemos el\r\ninforme?"), showContent: true);

        Assert.Equal("Ana Pérez", title);
        Assert.Equal("Teams · ¿Vemos el informe?", subtitle);
    }

    [Fact]
    public void Uses_app_as_title_when_notification_has_no_title()
    {
        var (title, subtitle) = NotificationPresenter.Format(N("Outlook", "", "Nuevo correo"), showContent: true, extraCount: 2);

        Assert.Equal("Outlook (+2)", title);
        Assert.Equal("Nuevo correo", subtitle);
    }

    [Fact]
    public void Hiding_content_shows_only_the_app()
    {
        var single = NotificationPresenter.Format(N("Teams", "Ana", "Contraseña: 1234"), showContent: false);
        var several = NotificationPresenter.Format(N("Teams", "Ana", "x"), showContent: false, extraCount: 1);

        Assert.Equal(("Teams", "Nueva notificación"), single);
        Assert.Equal(("Teams (+1)", "2 notificaciones nuevas"), several);
        Assert.DoesNotContain("1234", single.Subtitle);
    }

    [Fact]
    public void Missing_app_name_has_a_fallback()
    {
        var (title, _) = NotificationPresenter.Format(N("", "", "Texto"), showContent: true);

        Assert.Equal("Notificación", title);
    }

    [Fact]
    public void Long_text_is_truncated_with_ellipsis()
    {
        var cleaned = NotificationPresenter.Clean(new string('a', 500));

        Assert.Equal(NotificationPresenter.MaxLineLength, cleaned.Length);
        Assert.EndsWith("…", cleaned);
    }

    [Fact]
    public void Clean_collapses_whitespace_and_control_characters()
    {
        Assert.Equal("a b c", NotificationPresenter.Clean("  a\t\u0007 b\n\n c  "));
        Assert.Equal(string.Empty, NotificationPresenter.Clean(null));
    }

    [Fact]
    public void Settings_normalize_muted_apps_list()
    {
        var settings = new IslandSettings { MutedNotificationApps = "teams;; Outlook ,TEAMS" }.Normalize();

        Assert.Equal("teams, Outlook", settings.MutedNotificationApps);
        Assert.False(new IslandSettings().ShowNotificationPeeks);
    }
}
