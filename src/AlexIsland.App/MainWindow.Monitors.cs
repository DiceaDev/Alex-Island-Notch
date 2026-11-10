using AlexIsland.App.Native;
using AlexIsland.Core.Display;
using AlexIsland.Core.Models;
using Microsoft.UI.Xaml.Controls;

namespace AlexIsland.App;

public sealed partial class MainWindow : UserControl
{
    // ==============================================================
    // SECCIÓN VARIOS MONITORES
    // ==============================================================
    //
    // Hay una sola isla completa (un solo estado: agenda, tareas, Outlook, avisos),
    // que vive en el monitor del cursor. Cada uno de los demás monitores tiene un
    // notch satélite que solo refleja su estado. Así se ve una isla en cada pantalla
    // sin multiplicar el consumo ni arriesgar que dos copias se pisen los archivos.

    private readonly Dictionary<nint, SatelliteNotch> _satellites = [];
    private List<MonitorArea> _monitors = [];
    private bool? _appliedShowOnAllMonitors;

    private void InitializeMonitors()
    {
        // Conectar, desconectar o reordenar pantallas.
        _host.DisplayChanged += (_, _) =>
        {
            if (_isDisposed) return;
            ReconcileMonitors();
            ApplyIslandState();
        };
    }

    /// <summary>Se llama al aplicar los ajustes; solo actúa si cambió el de los monitores.</summary>
    private void ApplyMonitorSettings()
    {
        if (_appliedShowOnAllMonitors == _settings.ShowOnAllMonitors) return;
        _appliedShowOnAllMonitors = _settings.ShowOnAllMonitors;
        ReconcileMonitors();
    }

    /// <summary>Deja un satélite por cada monitor que no tiene la isla completa, y ninguno de más.</summary>
    private void ReconcileMonitors()
    {
        _monitors = NativeMethods.GetMonitors().ToList();

        if (!_settings.ShowOnAllMonitors)
        {
            CloseSatellites();

            // Con el ajuste desactivado, la isla vuelve al monitor principal.
            var primary = _monitors.FirstOrDefault(m => m.IsPrimary);
            if (primary is not null && NativeMethods.GetWindowMonitor(_hwnd) != primary.Id) MoveIslandTo(primary);
            ReconcileTopBands();
            return;
        }

        var wanted = MonitorLayout.SatelliteMonitors(_monitors, NativeMethods.GetWindowMonitor(_hwnd));

        foreach (var id in _satellites.Keys.Where(id => wanted.All(m => m.Id != id)).ToList())
        {
            _satellites[id].Close();
            _satellites.Remove(id);
        }

        foreach (var monitor in wanted)
        {
            if (_satellites.TryGetValue(monitor.Id, out var existing))
            {
                existing.MoveTo(monitor);
                continue;
            }

            try
            {
                _satellites[monitor.Id] = new SatelliteNotch(monitor, ShadowMargin);
            }
            catch (Exception ex)
            {
                // Sin ese satélite la isla sigue funcionando en los demás monitores.
                App.Log("No se pudo crear el notch de un monitor.", ex);
            }
        }

        SyncSatellites();
        UpdateSatelliteVisibility();
        ReconcileTopBands();
    }

    private void CloseSatellites()
    {
        foreach (var satellite in _satellites.Values) satellite.Close();
        _satellites.Clear();
    }

    /// <summary>Comprobación liviana (posición del cursor); no hace nada con un solo monitor.</summary>
    private void CheckCursorMonitor()
    {
        if (_isDisposed || _satellites.Count == 0) return;

        var active = NativeMethods.GetWindowMonitor(_hwnd);
        var cursor = NativeMethods.GetCursorMonitor();
        if (!MonitorLayout.ShouldFollowCursor(_settings.ShowOnAllMonitors, _isExpanded, _isPinned, _isPeekActive, cursor, active)) return;

        var target = _monitors.FirstOrDefault(m => m.Id == cursor);
        var previous = _monitors.FirstOrDefault(m => m.Id == active);
        if (target is null || previous is null)
        {
            ReconcileMonitors(); // la lista quedó vieja
            return;
        }

        // La isla y el satélite de ese monitor intercambian lugares.
        if (_satellites.Remove(target.Id, out var satellite))
        {
            satellite.MoveTo(previous);
            _satellites[previous.Id] = satellite;
        }

        MoveIslandTo(target);
        UpdateSatelliteVisibility();
        if (_settings.HideInFullscreen) CheckFullscreen();
    }

    private void MoveIslandTo(MonitorArea monitor)
    {
        var width = (int)Math.Ceiling((IslandContainer.Width + 2 * ShadowMargin) * PixelScale);
        _host.Move(MonitorLayout.CenteredX(monitor, width), monitor.Y);

        // En el monitor nuevo la escala puede ser otra: se recalcula tamaño y centrado.
        ApplyIslandState();
    }

    /// <summary>Envía a los satélites lo que debe verse en reposo; si nada cambió, no redibujan.</summary>
    private void SyncSatellites()
    {
        if (_satellites.Count == 0) return;

        var state = BuildSatelliteState();
        foreach (var satellite in _satellites.Values) satellite.Apply(state);
    }

    private SatelliteState BuildSatelliteState()
    {
        var scale = _settings.NotchScale;

        if (_pomodoroState == PomodoroState.Running)
        {
            var title = _pomodoroMode == PomodoroMode.Work ? "Enfoque activo" : "Pausa de descanso";
            return new SatelliteState(SatelliteContent.Text, NotchPlayingWidth, NotchHeight, 16, scale, title, $"{_pomodoroRemaining:mm\\:ss}");
        }

        if (_isPlaying)
        {
            return new SatelliteState(SatelliteContent.Text, NotchPlayingWidth, NotchHeight, 16, scale, CompactTrackTitle.Text, CompactTrackArtist.Text);
        }

        if (_settings.MinimalNotch)
        {
            return new SatelliteState(SatelliteContent.Blank, MinimalWidth, MinimalHeight, 13, scale);
        }

        return new SatelliteState(_settings.ShowMascot ? SatelliteContent.Eyes : SatelliteContent.Blank, RestingWidth, RestingHeight, 13, scale);
    }

    /// <summary>Cada satélite se oculta si el usuario ocultó la isla o si su monitor tiene una app en pantalla completa.</summary>
    private void UpdateSatelliteVisibility()
    {
        foreach (var satellite in _satellites.Values)
        {
            var fullscreen = _settings.HideInFullscreen && NativeMethods.IsFullscreenAppActive(satellite.Hwnd);
            satellite.SetVisible(!_isHiddenByUser && !fullscreen);
        }
    }
}
