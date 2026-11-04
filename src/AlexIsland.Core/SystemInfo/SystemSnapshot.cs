namespace AlexIsland.Core.SystemInfo;

/// <summary>Lectura puntual del estado del equipo.</summary>
public sealed record SystemSnapshot(
    double CpuPercent,
    ulong MemoryUsedBytes,
    ulong MemoryTotalBytes,
    ulong DiskUsedBytes,
    ulong DiskTotalBytes,
    string DiskName,
    TimeSpan Uptime,
    int? BatteryPercent,
    bool IsCharging)
{
    public double MemoryPercent => Percent(MemoryUsedBytes, MemoryTotalBytes);
    public double DiskPercent => Percent(DiskUsedBytes, DiskTotalBytes);

    private static double Percent(ulong used, ulong total)
        => total == 0 ? 0 : Math.Clamp(used * 100.0 / total, 0, 100);
}

/// <summary>Contadores acumulados de CPU tal como los entrega GetSystemTimes.</summary>
public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);

public static class SystemMetrics
{
    /// <summary>
    /// Calcula el uso de CPU entre dos lecturas de GetSystemTimes. En Windows el
    /// tiempo de kernel ya incluye el tiempo inactivo, por eso se resta.
    /// </summary>
    public static double CpuPercent(CpuTimes previous, CpuTimes current)
    {
        // Si los contadores retroceden (no debería pasar), se ignora la muestra.
        if (current.Idle < previous.Idle || current.Kernel < previous.Kernel || current.User < previous.User)
        {
            return 0;
        }

        var idle = current.Idle - previous.Idle;
        var total = (current.Kernel - previous.Kernel) + (current.User - previous.User);
        if (total == 0) return 0;

        var busy = total > idle ? total - idle : 0;
        return Math.Clamp(busy * 100.0 / total, 0, 100);
    }

    /// <summary>Formatea bytes en la unidad más legible (B, KB, MB, GB, TB).</summary>
    public static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = unit == 0 || value >= 100 ? "0" : "0.0";
        return value.ToString(format, System.Globalization.CultureInfo.InvariantCulture) + " " + units[unit];
    }

    /// <summary>Formatea el tiempo encendido como "3 d 4 h", "5 h 12 m" o "8 m".</summary>
    public static string FormatUptime(TimeSpan uptime)
    {
        if (uptime < TimeSpan.Zero) uptime = TimeSpan.Zero;
        if (uptime.TotalDays >= 1) return $"{(int)uptime.TotalDays} d {uptime.Hours} h";
        if (uptime.TotalHours >= 1) return $"{(int)uptime.TotalHours} h {uptime.Minutes} m";
        return $"{Math.Max(0, (int)uptime.TotalMinutes)} m";
    }
}
