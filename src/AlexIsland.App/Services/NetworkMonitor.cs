using System.Net.NetworkInformation;
using System.Net.Sockets;
using AlexIsland.Core.Network;

namespace AlexIsland.App.Services;

/// <summary>
/// Observa la conectividad del equipo mediante eventos de Windows (sin sondeo)
/// y mide la latencia solo cuando se le pide, es decir, mientras la pestaña
/// Sistema está visible.
/// </summary>
internal sealed class NetworkMonitor : IDisposable
{
    private bool _disposed;

    /// <summary>Se dispara en un hilo de fondo cuando cambia la red. Puede llegar en ráfagas.</summary>
    public event EventHandler? Changed;

    public NetworkMonitor()
    {
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        if (!_disposed) Changed?.Invoke(this, EventArgs.Empty);
    }

    public NetworkSnapshot Sample()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                         && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .ToList();

            var vpns = interfaces
                .Where(n => VpnDetector.IsLikelyVpn(n.Name, n.Description, n.NetworkInterfaceType == NetworkInterfaceType.Ppp))
                .Select(n => n.Name)
                .ToList();

            // La conexión principal es la primera interfaz física (no VPN) con puerta de enlace IPv4.
            var primary = interfaces
                .Where(n => !vpns.Contains(n.Name))
                .Select(n => (Nic: n, Props: SafeProperties(n)))
                .FirstOrDefault(x => x.Props?.GatewayAddresses
                    .Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any)) == true);

            if (primary.Nic is null || primary.Props is null)
            {
                return NetworkSnapshot.Disconnected with { VpnAdapters = vpns };
            }

            var ipv4 = primary.Props.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            var gateway = primary.Props.GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();

            var kind = primary.Nic.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Wireless80211 => ConnectionKind.WiFi,
                NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
                    or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit => ConnectionKind.Ethernet,
                NetworkInterfaceType.Wman or NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => ConnectionKind.Cellular,
                _ => ConnectionKind.Other,
            };

            var (profileName, hasInternet) = ReadConnectionProfile();

            return new NetworkSnapshot(
                IsConnected: true,
                HasInternet: hasInternet,
                Kind: kind,
                ProfileName: profileName,
                AdapterName: primary.Nic.Name,
                IPv4Address: ipv4,
                Gateway: gateway,
                VpnAdapters: vpns);
        }
        catch
        {
            return NetworkSnapshot.Disconnected;
        }
    }

    /// <summary>
    /// Nombre del perfil de red (normalmente el nombre de la Wi-Fi) y si hay
    /// acceso a Internet, según Windows. No requiere permiso de ubicación.
    /// </summary>
    private static (string? ProfileName, bool HasInternet) ReadConnectionProfile()
    {
        try
        {
            var profile = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            if (profile is null) return (null, false);

            var level = profile.GetNetworkConnectivityLevel();
            return (profile.ProfileName, level == Windows.Networking.Connectivity.NetworkConnectivityLevel.InternetAccess);
        }
        catch
        {
            return (null, false);
        }
    }

    /// <summary>
    /// Mide la latencia con un ping ICMP. Devuelve null si el host no es válido,
    /// no responde o hay un error. Solo se usa mientras la pestaña Sistema está abierta.
    /// </summary>
    public static async Task<long?> MeasureLatencyAsync(string host, CancellationToken cancellationToken)
    {
        if (!LatencyHost.IsValid(host)) return null;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, TimeSpan.FromSeconds(2), cancellationToken: cancellationToken);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch
        {
            return null;
        }
    }

    private static IPInterfaceProperties? SafeProperties(NetworkInterface nic)
    {
        try { return nic.GetIPProperties(); }
        catch { return null; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
    }
}
