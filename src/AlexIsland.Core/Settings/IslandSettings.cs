namespace AlexIsland.Core.Settings;

public sealed class IslandSettings
{
    public const double MinNotchScale = 0.8;
    public const double MaxNotchScale = 1.4;
    public const string DefaultLatencyHost = "1.1.1.1";

    // General
    public bool StartWithWindows { get; set; }
    public bool HideInFullscreen { get; set; } = true;
    public bool ShowMascot { get; set; } = true;
    public bool MinimalNotch { get; set; }
    public double NotchScale { get; set; } = 1.0;

    // Avisos (peeks)
    public bool ShowNetworkPeeks { get; set; } = true;
    public bool ShowVolumePeeks { get; set; } = true;
    public bool ShowBrightnessPeeks { get; set; } = true;
    public bool ShowKeyboardLayoutPeeks { get; set; } = true;
    public bool ShowCapsLockPeeks { get; set; } = true;

    // Notificaciones de Windows (desactivadas por defecto: requieren permiso del usuario)
    public bool ShowNotificationPeeks { get; set; }
    public bool ShowNotificationContent { get; set; } = true;
    /// <summary>Apps silenciadas, separadas por comas (por ejemplo, "Teams, Outlook").</summary>
    public string MutedNotificationApps { get; set; } = string.Empty;

    // Agenda: importar el calendario de Outlook clásico (solo lectura, Outlook → isla)
    public bool SyncOutlookCalendar { get; set; }

    // Música
    public bool ShowLyrics { get; set; } = true;

    // Sistema y red
    public string LatencyHost { get; set; } = DefaultLatencyHost;

    /// <summary>
    /// Corrige valores fuera de rango que podrían venir de un archivo editado a
    /// mano o dañado, para que la app siempre arranque con algo válido.
    /// </summary>
    public IslandSettings Normalize()
    {
        NotchScale = double.IsFinite(NotchScale)
            ? Math.Clamp(NotchScale, MinNotchScale, MaxNotchScale)
            : 1.0;

        LatencyHost = LatencyHost?.Trim() ?? DefaultLatencyHost;
        if (!Network.LatencyHost.IsValid(LatencyHost))
        {
            LatencyHost = DefaultLatencyHost;
        }

        MutedNotificationApps = Notifications.NotificationPresenter.NormalizeMutedApps(MutedNotificationApps);

        return this;
    }

    public IslandSettings Clone() => (IslandSettings)MemberwiseClone();
}
