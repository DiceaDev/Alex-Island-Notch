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
    /// <summary>Un notch en cada monitor; la isla completa va al monitor del cursor.</summary>
    public bool ShowOnAllMonitors { get; set; } = true;
    /// <summary>Reserva arriba una franja del alto del notch: las ventanas maximizadas empiezan debajo.</summary>
    public bool ReserveTopBand { get; set; } = true;

    // Avisos (peeks)
    public bool ShowNetworkPeeks { get; set; } = true;
    public bool ShowVolumePeeks { get; set; } = true;
    public bool ShowBrightnessPeeks { get; set; } = true;
    public bool ShowKeyboardLayoutPeeks { get; set; } = true;
    public bool ShowCapsLockPeeks { get; set; } = true;
    /// <summary>Avisa cuando un accesorio Bluetooth conectado baja del 20 % de batería.</summary>
    public bool ShowBluetoothBatteryPeeks { get; set; } = true;

    // Notificaciones de Windows (desactivadas por defecto: requieren permiso del usuario)
    public bool ShowNotificationPeeks { get; set; }
    public bool ShowNotificationContent { get; set; } = true;
    /// <summary>Apps silenciadas, separadas por comas (por ejemplo, "Teams, Outlook").</summary>
    public string MutedNotificationApps { get; set; } = string.Empty;

    // Agenda: importar el calendario de Outlook clásico (solo lectura, Outlook → isla)
    public bool SyncOutlookCalendar { get; set; }

    // Planner: horario laboral (hora de inicio y de término)
    public const int MinPlannerHour = 6;
    public const int MaxPlannerHour = 22;
    public int PlannerStartHour { get; set; } = 8;
    /// <summary>Minuto de inicio: 0 o 30. Con 30, la primera fila del planner dura media hora.</summary>
    public int PlannerStartMinute { get; set; } = 30;
    public int PlannerEndHour { get; set; } = 18;

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

        PlannerStartHour = Math.Clamp(PlannerStartHour, MinPlannerHour, MaxPlannerHour - 1);
        PlannerEndHour = Math.Clamp(PlannerEndHour, MinPlannerHour + 1, MaxPlannerHour);
        if (PlannerEndHour <= PlannerStartHour) PlannerEndHour = PlannerStartHour + 1;
        if (PlannerStartMinute != 30) PlannerStartMinute = 0;

        MutedNotificationApps = Notifications.NotificationPresenter.NormalizeMutedApps(MutedNotificationApps);

        return this;
    }

    public IslandSettings Clone() => (IslandSettings)MemberwiseClone();
}
