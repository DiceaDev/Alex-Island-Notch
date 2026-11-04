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

    /// <summary>Bordes suavizados con transparencia por píxel (si Windows lo permite).</summary>
    public bool SmoothEdges { get; set; } = true;

    // Avisos (peeks)
    public bool ShowNetworkPeeks { get; set; } = true;
    public bool ShowVolumePeeks { get; set; } = true;
    public bool ShowBrightnessPeeks { get; set; } = true;
    public bool ShowKeyboardLayoutPeeks { get; set; } = true;
    public bool ShowCapsLockPeeks { get; set; } = true;

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

        return this;
    }

    public IslandSettings Clone() => (IslandSettings)MemberwiseClone();
}
