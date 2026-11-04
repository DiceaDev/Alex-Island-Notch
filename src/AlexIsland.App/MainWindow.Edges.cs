using AlexIsland.App.Native;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace AlexIsland.App;

public sealed partial class MainWindow : Window
{
    // ==============================================================
    // SECCIÓN BORDES: SUAVIZADOS (TRANSPARENCIA) O RECORTADOS (REGIÓN)
    // ==============================================================

    private Windows.UI.ViewManagement.UISettings? _uiSettings;
    private TransparentBackdrop? _transparentBackdrop;
    private bool _dwmAlphaAvailable;
    private bool _backdropFailed;
    private bool _useSmoothEdges;
    private bool _edgeModeApplied;

    /// <summary>
    /// Hay dos formas de dibujar el contorno del notch:
    /// - Suavizado: la ventana es transparente por píxel y XAML dibuja las curvas
    ///   con antialiasing, como Nusa. Requiere los efectos de transparencia de Windows.
    /// - Recortado: la ventana se recorta con una región (bordes sin suavizar) y el
    ///   fondo es negro. Se usa si la transparencia no está disponible, para que
    ///   nunca aparezca un rectángulo de fondo alrededor del notch.
    /// </summary>
    private void InitializeEdgeRendering()
    {
        _dwmAlphaAvailable = NativeMethods.EnablePerPixelTransparency(_hwnd);

        try
        {
            _uiSettings = new Windows.UI.ViewManagement.UISettings();
            // Llega en otro hilo cuando el usuario cambia los efectos de transparencia.
            _uiSettings.AdvancedEffectsEnabledChanged += (_, _) =>
                DispatcherQueue.TryEnqueue(UpdateEdgeRendering);
        }
        catch
        {
            _uiSettings = null;
        }

        UpdateEdgeRendering();
    }

    private void UpdateEdgeRendering()
    {
        if (_isDisposed) return;

        var advancedEffects = false;
        try { advancedEffects = _uiSettings?.AdvancedEffectsEnabled ?? false; } catch { }

        var smooth = _settings.SmoothEdges && _dwmAlphaAvailable && advancedEffects && !_backdropFailed;
        var changed = !_edgeModeApplied || smooth != _useSmoothEdges;
        _edgeModeApplied = true;
        _useSmoothEdges = smooth;

        if (smooth)
        {
            if (_transparentBackdrop is null)
            {
                _transparentBackdrop = new TransparentBackdrop();
                _transparentBackdrop.Failed += () =>
                {
                    _backdropFailed = true;
                    DispatcherQueue.TryEnqueue(UpdateEdgeRendering);
                };
            }

            SystemBackdrop = _transparentBackdrop;
            RootGrid.Background = new SolidColorBrush(Colors.Transparent);
        }
        else
        {
            SystemBackdrop = null;
            RootGrid.Background = new SolidColorBrush(Colors.Black);
        }

        if (changed)
        {
            App.Log($"Bordes: {(smooth ? "suavizados (transparencia por píxel)" : "recortados (región)")} · " +
                    $"ajuste={_settings.SmoothEdges}, DWM={_dwmAlphaAvailable}, efectos={advancedEffects}, fallo={_backdropFailed}.");
            if (IslandContainer.Width > 0) UpdateNotchGeometry(IslandContainer.Width, IslandContainer.Height);
        }
    }
}
