namespace AlexIsland.Core.Network;

public enum ConnectionKind { None, Ethernet, WiFi, Cellular, Other }

/// <summary>Estado de la conexión principal del equipo.</summary>
public sealed record NetworkSnapshot(
    bool IsConnected,
    bool HasInternet,
    ConnectionKind Kind,
    string? ProfileName,
    string? AdapterName,
    string? IPv4Address,
    string? Gateway,
    IReadOnlyList<string> VpnAdapters)
{
    public static NetworkSnapshot Disconnected { get; } =
        new(false, false, ConnectionKind.None, null, null, null, null, []);

    public bool IsVpnActive => VpnAdapters.Count > 0;

    /// <summary>Texto corto para el peek y la pestaña: "Wi-Fi · Oficina", "Ethernet", "Sin conexión".</summary>
    public string Describe()
    {
        if (!IsConnected) return "Sin conexión";

        var kind = Kind switch
        {
            ConnectionKind.WiFi => "Wi-Fi",
            ConnectionKind.Ethernet => "Ethernet",
            ConnectionKind.Cellular => "Datos móviles",
            _ => "Red",
        };

        var text = string.IsNullOrWhiteSpace(ProfileName) ? kind : $"{kind} · {ProfileName}";
        return HasInternet ? text : text + " (sin Internet)";
    }

    /// <summary>
    /// Indica si el cambio respecto a la lectura anterior merece avisar con un peek:
    /// se perdió o recuperó la conexión, cambió la red o se conectó/desconectó una VPN.
    /// </summary>
    public bool IsNoticeableChangeFrom(NetworkSnapshot? previous)
    {
        if (previous is null) return false;
        return previous.IsConnected != IsConnected
            || previous.HasInternet != HasInternet
            || previous.Kind != Kind
            || !string.Equals(previous.ProfileName, ProfileName, StringComparison.Ordinal)
            || previous.IsVpnActive != IsVpnActive;
    }
}

public static class VpnDetector
{
    private static readonly string[] Markers =
    [
        "vpn", "wireguard", "openvpn", "tap-windows", "wintun", "tun", "anyconnect",
        "globalprotect", "pangp", "fortinet", "forticlient", "juniper", "pulse secure",
        "zscaler", "tailscale", "zerotier", "nordlynx", "proton", "cloudflare warp",
    ];

    /// <summary>
    /// Heurística para reconocer adaptadores VPN por su tipo, nombre o descripción.
    /// <paramref name="isPpp"/> corresponde a NetworkInterfaceType.Ppp (VPN nativas de Windows).
    /// </summary>
    public static bool IsLikelyVpn(string? name, string? description, bool isPpp)
    {
        if (isPpp) return true;

        foreach (var text in new[] { name, description })
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            var lower = text.ToLowerInvariant();
            foreach (var marker in Markers)
            {
                if (marker == "tun")
                {
                    // "tun" es corto: se exige como palabra para no confundir "Realtek Tuning".
                    if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"\btun\d*\b")) return true;
                }
                else if (lower.Contains(marker, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

public static class LatencyHost
{
    /// <summary>
    /// Valida el host configurado para medir latencia: un nombre DNS o una IP,
    /// sin esquema, ruta, espacios ni puerto. Evita que un ajuste mal escrito
    /// termine usándose como algo distinto de un destino de ping.
    /// </summary>
    public static bool IsValid(string? host)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253) return false;
        if (System.Net.IPAddress.TryParse(host, out _)) return true;
        return Uri.CheckHostName(host) == UriHostNameType.Dns && !host.Contains('/') && !host.Contains(':');
    }
}
