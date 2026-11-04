using System.Text;

namespace AlexIsland.Core.Notifications;

/// <summary>Decide qué notificaciones se muestran en la isla y con qué texto.</summary>
public static class NotificationPresenter
{
    /// <summary>Largo máximo de cada línea; la isla igual recorta con "…", esto evita cargar textos enormes.</summary>
    public const int MaxLineLength = 120;

    /// <summary>Convierte "Teams, Outlook ; Slack" en una lista de nombres sin repetir.</summary>
    public static IReadOnlyList<string> ParseMutedApps(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        return text
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Texto normalizado para guardar en los ajustes: "Teams, Outlook".</summary>
    public static string NormalizeMutedApps(string? text) => string.Join(", ", ParseMutedApps(text));

    /// <summary>
    /// Una notificación se muestra si tiene algo que decir, no la envía esta misma
    /// app y su app no está silenciada (comparación sin distinguir mayúsculas).
    /// </summary>
    public static bool ShouldShow(IslandNotification notification, IReadOnlyCollection<string> mutedApps, string ownAppName)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var app = notification.AppName.Trim();
        if (app.Length > 0 && string.Equals(app, ownAppName, StringComparison.OrdinalIgnoreCase)) return false;
        if (app.Length > 0 && mutedApps.Any(m => string.Equals(m, app, StringComparison.OrdinalIgnoreCase))) return false;

        return app.Length > 0
            || !string.IsNullOrWhiteSpace(notification.Title)
            || !string.IsNullOrWhiteSpace(notification.Body);
    }

    /// <summary>
    /// Título y subtítulo del aviso. Sin <paramref name="showContent"/> solo se ve la
    /// app, útil al compartir pantalla. <paramref name="extraCount"/> indica cuántas
    /// otras notificaciones llegaron a la vez.
    /// </summary>
    public static (string Title, string Subtitle) Format(IslandNotification notification, bool showContent, int extraCount = 0)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var app = Clean(notification.AppName);
        if (app.Length == 0) app = "Notificación";
        var more = extraCount > 0 ? $" (+{extraCount})" : string.Empty;

        if (!showContent)
        {
            return (app + more, extraCount > 0 ? $"{extraCount + 1} notificaciones nuevas" : "Nueva notificación");
        }

        var title = Clean(notification.Title);
        var body = Clean(notification.Body);

        if (title.Length == 0)
        {
            return (app + more, body.Length > 0 ? body : "Nueva notificación");
        }

        var subtitle = body.Length > 0 ? $"{app}{more} · {body}" : app + more;
        return (title, Truncate(subtitle));
    }

    /// <summary>Une saltos de línea y espacios repetidos en uno solo y recorta el largo.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var builder = new StringBuilder(Math.Min(text.Length, MaxLineLength + 1));
        var pendingSpace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace) builder.Append(' ');
            pendingSpace = false;
            builder.Append(ch);
            if (builder.Length > MaxLineLength) break;
        }

        return Truncate(builder.ToString());
    }

    private static string Truncate(string text) =>
        text.Length <= MaxLineLength ? text : text[..(MaxLineLength - 1)].TrimEnd() + "…";
}
