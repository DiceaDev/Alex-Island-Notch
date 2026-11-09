using AlexIsland.App.Native;
using AlexIsland.Core.Display;
using Microsoft.UI.Xaml.Controls;

namespace AlexIsland.App;

public sealed partial class MainWindow : UserControl
{
    // ==============================================================
    // SECCIÓN FRANJA SUPERIOR RESERVADA
    // ==============================================================
    //
    // En cada monitor con isla se reserva una franja del alto del notch, como hace la
    // barra de tareas abajo: las ventanas maximizadas empiezan debajo y la isla no tapa
    // sus pestañas. La franja toma el color de la barra de la ventana que tiene debajo.

    private static readonly TimeSpan TopBandSampleInterval = TimeSpan.FromMilliseconds(800);
    private const int TopBandDefaultColor = 0x032539;

    private readonly Dictionary<nint, TopBandWindow> _topBands = [];
    private volatile TopBandWindow[] _topBandSnapshot = [];
    private System.Threading.Timer? _topBandSampler;
    private int _topBandSampling;

    /// <summary>Deja una franja en cada monitor que muestra la isla, y ninguna si el ajuste está apagado.</summary>
    private void ReconcileTopBands()
    {
        if (_isDisposed) return;

        var wanted = !_settings.ReserveTopBand || _isHiddenByUser
            ? []
            : _settings.ShowOnAllMonitors ? _monitors : _monitors.Where(m => m.IsPrimary).ToList();

        foreach (var id in _topBands.Keys.Where(id => wanted.All(m => m.Id != id)).ToList())
        {
            _topBands[id].Dispose();
            _topBands.Remove(id);
        }

        foreach (var monitor in wanted)
        {
            if (!_topBands.TryGetValue(monitor.Id, out var band))
            {
                try
                {
                    band = new TopBandWindow(TopBandDefaultColor);
                    band.Repositioned += (_, _) => RaiseIslandsAboveBands();
                    _topBands[monitor.Id] = band;
                }
                catch (Exception ex)
                {
                    // Sin franja la isla funciona igual; solo vuelve a tapar lo de arriba.
                    App.Log("No se pudo reservar la franja superior de un monitor.", ex);
                    continue;
                }
            }

            var height = TopBand.HeightFor(NotchHeight, _settings.NotchScale, NativeMethods.GetMonitorDpiScale(monitor.Id));
            band.Place(monitor, height);
        }

        _topBandSnapshot = [.. _topBands.Values];
        RaiseIslandsAboveBands();

        if (_topBands.Count == 0)
        {
            _topBandSampler?.Dispose();
            _topBandSampler = null;
        }
        else
        {
            _topBandSampler ??= new System.Threading.Timer(_ => SampleTopBandColors(), null, TimeSpan.Zero, TopBandSampleInterval);
        }
    }

    /// <summary>La franja también es "siempre visible": la isla y los satélites deben quedar encima.</summary>
    private void RaiseIslandsAboveBands()
    {
        if (_isDisposed || _topBands.Count == 0) return;
        NativeMethods.BringToTopmostFront(_hwnd);
        foreach (var satellite in _satellites.Values) NativeMethods.BringToTopmostFront(satellite.Hwnd);
    }

    /// <summary>
    /// Lee la fila de píxeles justo debajo de cada franja y usa su color predominante.
    /// Corre fuera del hilo de la interfaz; solo el cambio de color vuelve a él.
    /// </summary>
    private void SampleTopBandColors()
    {
        if (Interlocked.Exchange(ref _topBandSampling, 1) == 1) return;
        try
        {
            foreach (var band in _topBandSnapshot)
            {
                if (band.IsHidden) continue;
                var (x, y, width, height) = band.Bounds;
                if (width <= 0 || height <= 0) continue;

                var row = ScreenRowSampler.ReadRow(x, y + height + 1, width);
                if (row is null || TopBand.DominantColor(row) is not int color) continue;

                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_isDisposed) band.SetColor(color);
                });
            }
        }
        catch (Exception ex)
        {
            App.Log("Franja superior: no se pudo leer el color.", ex);
        }
        finally
        {
            Volatile.Write(ref _topBandSampling, 0);
        }
    }

    /// <summary>Devuelve a Windows todo el espacio reservado.</summary>
    private void CloseTopBands()
    {
        _topBandSampler?.Dispose();
        _topBandSampler = null;
        _topBandSnapshot = [];
        foreach (var band in _topBands.Values) band.Dispose();
        _topBands.Clear();
    }
}
