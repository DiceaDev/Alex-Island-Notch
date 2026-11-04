using AlexIsland.Core.Network;
using AlexIsland.Core.Settings;
using AlexIsland.Core.SystemInfo;

namespace AlexIsland.Core.Tests;

public class SystemMetricsTests
{
    [Fact]
    public void Cpu_percent_subtracts_idle_from_kernel_time()
    {
        // 1000 ticks totales (kernel 600 incluye 400 de idle + user 400) → 600 ocupados = 60 %.
        var before = new CpuTimes(Idle: 1000, Kernel: 2000, User: 3000);
        var after = new CpuTimes(Idle: 1400, Kernel: 2600, User: 3400);
        Assert.Equal(60, SystemMetrics.CpuPercent(before, after), precision: 3);
    }

    [Fact]
    public void Cpu_percent_is_zero_when_counters_do_not_move_or_go_backwards()
    {
        var same = new CpuTimes(10, 20, 30);
        Assert.Equal(0, SystemMetrics.CpuPercent(same, same));
        Assert.Equal(0, SystemMetrics.CpuPercent(new CpuTimes(10, 20, 30), new CpuTimes(5, 20, 30)));
    }

    [Theory]
    [InlineData(0UL, "0 B")]
    [InlineData(512UL, "512 B")]
    [InlineData(1536UL, "1.5 KB")]
    [InlineData(16UL * 1024 * 1024 * 1024, "16.0 GB")]
    [InlineData(512UL * 1024 * 1024 * 1024, "512 GB")]
    public void Formats_bytes(ulong bytes, string expected)
        => Assert.Equal(expected, SystemMetrics.FormatBytes(bytes));

    [Fact]
    public void Formats_uptime()
    {
        Assert.Equal("8 m", SystemMetrics.FormatUptime(TimeSpan.FromMinutes(8.7)));
        Assert.Equal("5 h 12 m", SystemMetrics.FormatUptime(new TimeSpan(5, 12, 0)));
        Assert.Equal("3 d 4 h", SystemMetrics.FormatUptime(new TimeSpan(3, 4, 30, 0)));
    }

    [Fact]
    public void Snapshot_percentages_handle_zero_totals()
    {
        var s = new SystemSnapshot(0, 0, 0, 0, 0, "C:", TimeSpan.Zero, null, false);
        Assert.Equal(0, s.MemoryPercent);
        Assert.Equal(0, s.DiskPercent);
    }
}

public class NetworkTests
{
    [Theory]
    [InlineData("WireGuard Tunnel", null, false)]
    [InlineData("Ethernet 3", "Cisco AnyConnect Secure Mobility Client Virtual Miniport Adapter", false)]
    [InlineData("Conexión VPN", "WAN Miniport (IKEv2)", false)]
    [InlineData("tun0", null, false)]
    [InlineData("Tailscale", "Tailscale Tunnel", false)]
    [InlineData("Mi red", "algo", true)]
    public void Detects_vpn_adapters(string name, string? description, bool isPpp)
        => Assert.True(VpnDetector.IsLikelyVpn(name, description, isPpp));

    [Theory]
    [InlineData("Wi-Fi", "Intel(R) Wi-Fi 6E AX211 160MHz")]
    [InlineData("Ethernet", "Realtek PCIe GbE Family Controller")]
    [InlineData("Ethernet 2", "Realtek Tuning Adapter")]
    public void Ignores_regular_adapters(string name, string description)
        => Assert.False(VpnDetector.IsLikelyVpn(name, description, isPpp: false));

    [Theory]
    [InlineData("1.1.1.1", true)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("intranet.empresa.cl", true)]
    [InlineData("", false)]
    [InlineData("https://google.com", false)]
    [InlineData("google.com/x", false)]
    [InlineData("google.com:80", false)]
    [InlineData("host con espacios", false)]
    public void Validates_latency_host(string host, bool expected)
        => Assert.Equal(expected, LatencyHost.IsValid(host));

    [Fact]
    public void Describes_connection()
    {
        var wifi = new NetworkSnapshot(true, true, ConnectionKind.WiFi, "Oficina", "Wi-Fi", "10.0.0.5", "10.0.0.1", []);
        Assert.Equal("Wi-Fi · Oficina", wifi.Describe());
        Assert.Equal("Wi-Fi · Oficina (sin Internet)", (wifi with { HasInternet = false }).Describe());
        Assert.Equal("Sin conexión", NetworkSnapshot.Disconnected.Describe());
    }

    [Fact]
    public void Detects_noticeable_changes()
    {
        var wifi = new NetworkSnapshot(true, true, ConnectionKind.WiFi, "Oficina", "Wi-Fi", "10.0.0.5", "10.0.0.1", []);

        Assert.False(wifi.IsNoticeableChangeFrom(null));
        Assert.False((wifi with { IPv4Address = "10.0.0.9" }).IsNoticeableChangeFrom(wifi));
        Assert.True(NetworkSnapshot.Disconnected.IsNoticeableChangeFrom(wifi));
        Assert.True((wifi with { ProfileName = "Casa" }).IsNoticeableChangeFrom(wifi));
        Assert.True((wifi with { VpnAdapters = ["WireGuard"] }).IsNoticeableChangeFrom(wifi));
    }
}

public class IslandSettingsTests
{
    [Fact]
    public void Normalize_fixes_out_of_range_values()
    {
        var s = new IslandSettings { NotchScale = 5, LatencyHost = "https://evil.example" }.Normalize();
        Assert.Equal(IslandSettings.MaxNotchScale, s.NotchScale);
        Assert.Equal(IslandSettings.DefaultLatencyHost, s.LatencyHost);

        Assert.Equal(1.0, new IslandSettings { NotchScale = double.NaN }.Normalize().NotchScale);
        Assert.Equal(IslandSettings.DefaultLatencyHost, new IslandSettings { LatencyHost = null! }.Normalize().LatencyHost);
    }

    [Fact]
    public void Clone_is_independent()
    {
        var original = new IslandSettings { ShowLyrics = true };
        var copy = original.Clone();
        copy.ShowLyrics = false;
        Assert.True(original.ShowLyrics);
    }
}

public class KeyboardLayoutInfoTests
{
    [Theory]
    [InlineData(0x0C0A, "ES")] // español (España, alfabetización internacional)
    [InlineData(0x340A, "ES")] // español (Chile)
    [InlineData(0x0409, "EN")] // inglés (Estados Unidos)
    [InlineData(0x0416, "PT")] // portugués (Brasil)
    public void Describes_known_languages(int languageId, string expectedCode)
    {
        var (code, name) = KeyboardLayoutInfo.Describe(languageId);
        Assert.Equal(expectedCode, code);
        Assert.False(string.IsNullOrWhiteSpace(name));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Handles_invalid_ids(int languageId)
        => Assert.Equal("??", KeyboardLayoutInfo.Describe(languageId).Code);

    [Fact]
    public void Extracts_language_from_hkl()
    {
        Assert.Equal(0x340A, KeyboardLayoutInfo.LanguageIdFromLayout(0x0000_0000_340A_340AL));
        Assert.Equal(0x0409, KeyboardLayoutInfo.LanguageIdFromLayout(unchecked((long)0xFFFF_FFFF_F002_0409UL)));
    }
}
