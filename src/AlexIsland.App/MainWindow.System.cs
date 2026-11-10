using System.Text;
using AlexIsland.App.Services;
using AlexIsland.Core.Network;
using AlexIsland.Core.SystemInfo;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AlexIsland.App;

public sealed partial class MainWindow : UserControl
{
    // ==============================================================
    // SECCIÓN SISTEMA Y RED (IT)
    // ==============================================================

    private readonly SystemMonitor _systemMonitor = new();
    private NetworkMonitor? _networkMonitor;
    private DispatcherQueueTimer? _systemTimer;
    private DispatcherQueueTimer? _networkDebounceTimer;
    private NetworkSnapshot? _lastNetwork;
    private SystemSnapshot? _lastSystem;
    private long? _lastLatencyMs;
    private int _systemTickCount;
    private CancellationTokenSource? _latencyCts;

    private void InitializeSystemModule()
    {
        _systemTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _systemTimer.Interval = TimeSpan.FromSeconds(2);
        _systemTimer.IsRepeating = true;
        _systemTimer.Tick += (_, _) => RefreshSystemView();

        // Las notificaciones de red llegan en ráfagas (cada cambio de dirección
        // dispara varios eventos). Se espera 1,5 s de calma antes de leer el estado.
        _networkDebounceTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _networkDebounceTimer.Interval = TimeSpan.FromMilliseconds(1500);
        _networkDebounceTimer.IsRepeating = false;
        _networkDebounceTimer.Tick += (_, _) =>
        {
            _networkDebounceTimer.Stop();
            OnNetworkSettled();
        };

        try
        {
            _networkMonitor = new NetworkMonitor();
            _networkMonitor.Changed += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                if (_isDisposed) return;
                _networkDebounceTimer.Stop();
                _networkDebounceTimer.Start();
            });
        }
        catch
        {
            _networkMonitor = null;
        }

        _ = Task.Run(() => _networkMonitor?.Sample())
            .ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully) DispatcherQueue.TryEnqueue(() => _lastNetwork = t.Result);
            }, TaskScheduler.Default);
    }

    private void StopSystemModule()
    {
        _systemTimer?.Stop();
        _networkDebounceTimer?.Stop();
        _latencyCts?.Cancel();
        _networkMonitor?.Dispose();
    }

    /// <summary>Activa el muestreo solo mientras la pestaña Sistema está visible.</summary>
    private void UpdateSystemSampling()
    {
        if (_systemTimer is null) return;

        var visible = _isExpanded && _activeTab == IslandTab.System && !_isDisposed;
        if (visible && !_systemTimer.IsRunning)
        {
            _systemMonitor.Reset();
            _systemTickCount = 0;
            RefreshSystemView();
            _systemTimer.Start();
        }
        else if (!visible && _systemTimer.IsRunning)
        {
            _systemTimer.Stop();
            _latencyCts?.Cancel();
        }
    }

    private void TabSystemButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchTab(IslandTab.System);
    }

    private void OnNetworkSettled()
    {
        if (_isDisposed || _networkMonitor is null) return;

        var monitor = _networkMonitor;
        _ = Task.Run(() => monitor.Sample()).ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                var previous = _lastNetwork;
                var current = t.Result;
                _lastNetwork = current;

                if (_settings.ShowNetworkPeeks && CanShowAutomaticPeek && current.IsNoticeableChangeFrom(previous))
                {
                    ShowNetworkPeek(previous, current);
                }

                if (_activeTab == IslandTab.System && _isExpanded)
                {
                    RenderNetwork(current);
                }
            });
        }, TaskScheduler.Default);
    }

    private void ShowNetworkPeek(NetworkSnapshot? previous, NetworkSnapshot current)
    {
        if (previous is not null && previous.IsVpnActive != current.IsVpnActive)
        {
            ShowHardwarePeek(
                HardwarePeekType.Network,
                current.IsVpnActive ? "VPN conectada" : "VPN desconectada",
                current.IsVpnActive ? string.Join(", ", current.VpnAdapters) : current.Describe(),
                percent01: current.IsVpnActive ? 1.0 : 0.0,
                rightGlyph: "\uE705",
                accentColor: ColorHelper.FromArgb(255, 93, 179, 204));
            return;
        }

        if (!current.IsConnected || !current.HasInternet)
        {
            ShowHardwarePeek(
                HardwarePeekType.Network,
                current.IsConnected ? "Sin acceso a Internet" : "Conexión perdida",
                current.IsConnected ? current.Describe() : "Revisa el cable o la Wi-Fi",
                percent01: 0.0,
                rightGlyph: "\uE7BA",
                accentColor: ColorHelper.FromArgb(255, 255, 127, 120));
            return;
        }

        ShowHardwarePeek(
            HardwarePeekType.Network,
            previous is { IsConnected: false } or { HasInternet: false } ? "Conexión restablecida" : "Red cambiada",
            current.Describe(),
            percent01: 1.0,
            rightGlyph: NetworkGlyph(current),
            accentColor: ColorHelper.FromArgb(255, 79, 209, 165));
    }

    private void RefreshSystemView()
    {
        if (_isDisposed) return;

        try
        {
            var snapshot = _systemMonitor.Sample();
            _lastSystem = snapshot;
            RenderSystem(snapshot);
        }
        catch
        {
            // Una lectura fallida no debe detener la pestaña; se reintenta en el próximo tick.
        }

        // La red cambia por eventos; aquí solo se refresca cada 10 s por si
        // Windows no avisó (por ejemplo, la Wi-Fi se queda sin Internet).
        // La batería de los accesorios cambia despacio: también cada 10 s.
        if (_systemTickCount % 5 == 0)
        {
            RefreshBluetoothBatteries();
        }

        if (_systemTickCount % 5 == 0 && _networkMonitor is not null)
        {
            var monitor = _networkMonitor;
            _ = Task.Run(() => monitor.Sample()).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully) DispatcherQueue.TryEnqueue(() =>
                {
                    _lastNetwork = t.Result;
                    RenderNetwork(t.Result);
                });
            }, TaskScheduler.Default);
        }

        // Latencia cada 4 s mientras la pestaña está abierta.
        if (_systemTickCount % 2 == 0)
        {
            _ = UpdateLatencyAsync();
        }

        _systemTickCount++;
    }

    private void RenderSystem(SystemSnapshot s)
    {
        SysCpuValue.Text = $"{s.CpuPercent:0}%";
        SysCpuDetail.Text = $"{Environment.ProcessorCount} núcleos";
        SetBar(SysCpuTrack, SysCpuBar, s.CpuPercent, warnAt: 85);

        SysRamValue.Text = $"{s.MemoryPercent:0}%";
        SysRamDetail.Text = $"{SystemMetrics.FormatBytes(s.MemoryUsedBytes)} / {SystemMetrics.FormatBytes(s.MemoryTotalBytes)}";
        SetBar(SysRamTrack, SysRamBar, s.MemoryPercent, warnAt: 90);

        SysDiskValue.Text = $"{s.DiskPercent:0}%";
        SysDiskDetail.Text = $"{s.DiskName} · {SystemMetrics.FormatBytes(s.DiskTotalBytes - Math.Min(s.DiskUsedBytes, s.DiskTotalBytes))} libres";
        SetBar(SysDiskTrack, SysDiskBar, s.DiskPercent, warnAt: 90);

        SysUptimeValue.Text = SystemMetrics.FormatUptime(s.Uptime);
        if (s.BatteryPercent is int battery)
        {
            SysUptimeDetail.Text = s.IsCharging ? $"Batería {battery}% · cargando" : $"Batería {battery}%";
            SetBar(SysUptimeTrack, SysUptimeBar, battery, warnAt: null);
        }
        else
        {
            SysUptimeDetail.Text = "Sin batería";
            SetBar(SysUptimeTrack, SysUptimeBar, 100, warnAt: null);
        }
    }

    private static void SetBar(FrameworkElement track, Border bar, double percent, double? warnAt)
    {
        var width = track.ActualWidth;
        bar.Width = width > 0 ? width * Math.Clamp(percent, 0, 100) / 100.0 : 0;

        if (warnAt is double threshold)
        {
            // La primera vez se guarda el color original para poder restaurarlo.
            bar.Tag ??= bar.Background;
            bar.Background = percent >= threshold
                ? new SolidColorBrush(ColorHelper.FromArgb(255, 255, 127, 120))
                : (Brush)bar.Tag;
        }
    }

    private void RenderNetwork(NetworkSnapshot n)
    {
        SysNetIcon.Glyph = NetworkGlyph(n);
        SysNetIcon.Foreground = new SolidColorBrush(!n.IsConnected
            ? ColorHelper.FromArgb(255, 255, 127, 120)
            : n.HasInternet ? ColorHelper.FromArgb(255, 79, 209, 165) : ColorHelper.FromArgb(255, 255, 194, 92));

        SysNetTitle.Text = n.Describe();

        var detail = new List<string>();
        if (!string.IsNullOrWhiteSpace(n.IPv4Address)) detail.Add($"IP {n.IPv4Address}");
        if (!string.IsNullOrWhiteSpace(n.Gateway)) detail.Add($"Puerta {n.Gateway}");
        if (!string.IsNullOrWhiteSpace(n.AdapterName)) detail.Add(n.AdapterName);
        SysNetDetail.Text = detail.Count > 0 ? string.Join(" · ", detail) : "Sin dirección IPv4";

        SysVpnPill.Visibility = n.IsVpnActive ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(SysVpnPill, n.IsVpnActive ? "VPN activa: " + string.Join(", ", n.VpnAdapters) : null);
    }

    private static string NetworkGlyph(NetworkSnapshot n) => !n.IsConnected
        ? "\uE7BA"
        : n.Kind switch
        {
            ConnectionKind.WiFi => "\uE701",
            ConnectionKind.Ethernet => "\uE839",
            _ => "\uE774",
        };

    private async Task UpdateLatencyAsync()
    {
        _latencyCts?.Cancel();
        _latencyCts = new CancellationTokenSource();
        var token = _latencyCts.Token;

        if (_lastNetwork is { IsConnected: false })
        {
            RenderLatency(null);
            return;
        }

        var latency = await NetworkMonitor.MeasureLatencyAsync(_settings.LatencyHost, token);
        if (token.IsCancellationRequested || _isDisposed) return;

        _lastLatencyMs = latency;
        RenderLatency(latency);
    }

    private void RenderLatency(long? ms)
    {
        SysLatencyText.Text = ms is long value ? $"{value} ms" : "-- ms";
        var color = ms switch
        {
            null => ColorHelper.FromArgb(255, 184, 203, 211),
            < 60 => ColorHelper.FromArgb(255, 79, 209, 165),
            < 150 => ColorHelper.FromArgb(255, 255, 194, 92),
            _ => ColorHelper.FromArgb(255, 255, 127, 120),
        };
        SysLatencyText.Foreground = new SolidColorBrush(color);
        ToolTipService.SetToolTip(SysLatencyPill, $"Ping a {_settings.LatencyHost}");
    }

    /// <summary>
    /// Copia un resumen del equipo listo para pegar en un ticket de soporte.
    /// Solo va al portapapeles: no se envía a ningún servicio.
    /// </summary>
    private void SysCopyInfoBtn_Click(object sender, RoutedEventArgs e) => CopyMachineSummary(SysCopyInfoLabel, "Copiar", "¡Copiado!");

    private void CopyMachineSummary(TextBlock label, string idleText, string doneText)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Equipo: {Environment.MachineName}");
            sb.AppendLine($"Usuario: {Environment.UserDomainName}\\{Environment.UserName}");
            sb.AppendLine($"Sistema: {System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim()}");

            if (_lastNetwork is { } n)
            {
                sb.AppendLine($"Red: {n.Describe()}");
                if (n.IPv4Address is not null) sb.AppendLine($"IP: {n.IPv4Address}");
                if (n.Gateway is not null) sb.AppendLine($"Puerta de enlace: {n.Gateway}");
                if (n.IsVpnActive) sb.AppendLine($"VPN: {string.Join(", ", n.VpnAdapters)}");
            }

            if (_lastLatencyMs is long ms) sb.AppendLine($"Latencia ({_settings.LatencyHost}): {ms} ms");

            if (_lastSystem is { } s)
            {
                sb.AppendLine($"CPU: {s.CpuPercent:0}% · Memoria: {s.MemoryPercent:0}% ({SystemMetrics.FormatBytes(s.MemoryTotalBytes)})");
                sb.AppendLine($"Disco {s.DiskName}: {s.DiskPercent:0}% usado de {SystemMetrics.FormatBytes(s.DiskTotalBytes)}");
                sb.AppendLine($"Encendido: {SystemMetrics.FormatUptime(s.Uptime)}");
            }

            foreach (var device in _bluetoothDevices)
            {
                sb.AppendLine($"Bluetooth {device.Name}: batería {device.Percent}%");
            }

            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(sb.ToString());
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            FlashLabel(label, doneText, idleText);
        }
        catch
        {
            FlashLabel(label, "No se pudo copiar", idleText);
        }
    }

    private void FlashLabel(TextBlock label, string flash, string original)
    {
        label.Text = flash;
        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1.5);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            label.Text = original;
            timer.Stop();
        };
        timer.Start();
    }
}
