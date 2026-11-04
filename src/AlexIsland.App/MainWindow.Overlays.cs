using AlexIsland.App.Native;
using AlexIsland.Core.SystemInfo;
using Microsoft.UI;
using Microsoft.UI.Xaml;

namespace AlexIsland.App;

public sealed partial class MainWindow : Window
{
    // ==============================================================
    // SECCIÓN OVERLAYS: VOLUMEN, BRILLO E IDIOMA DEL TECLADO
    // ==============================================================

    private VolumeNotifier? _volumeNotifier;
    private BrightnessNotifier? _brightnessNotifier;
    private nint _lastKeyboardWindow;
    private nint _lastKeyboardLayout;

    private void InitializeOverlays()
    {
        try
        {
            _volumeNotifier = new VolumeNotifier();
            _volumeNotifier.VolumeChanged += (level, muted) =>
                DispatcherQueue.TryEnqueue(() => OnVolumeChanged(level, muted));
        }
        catch
        {
            _volumeNotifier = null;
        }

        try
        {
            _brightnessNotifier = new BrightnessNotifier();
            _brightnessNotifier.BrightnessChanged += value =>
                DispatcherQueue.TryEnqueue(() => OnBrightnessChanged(value));
        }
        catch
        {
            _brightnessNotifier = null;
        }

        try
        {
            // Al cambiar de salida (parlantes ↔ auriculares) se vuelve a escuchar el nuevo dispositivo.
            Windows.Media.Devices.MediaDevice.DefaultAudioRenderDeviceChanged += (_, _) =>
                DispatcherQueue.TryEnqueue(() => _volumeNotifier?.Reattach());
        }
        catch
        {
        }

        (_lastKeyboardWindow, _lastKeyboardLayout) = NativeMethods.GetForegroundKeyboardLayout();
    }

    private void StopOverlays()
    {
        _volumeNotifier?.Dispose();
        _volumeNotifier = null;
        _brightnessNotifier?.Dispose();
        _brightnessNotifier = null;
    }

    /// <summary>
    /// Los avisos automáticos no interrumpen al usuario: si la isla está abierta
    /// (escribiendo una tarea, mirando la agenda...) o está oculta, se omiten.
    /// </summary>
    private bool CanShowAutomaticPeek =>
        !_isDisposed && !_isExpanded && !_isHiddenByUser && !_isHiddenForFullscreen;

    private void OnVolumeChanged(float level, bool muted)
    {
        if (!CanShowAutomaticPeek || !_settings.ShowVolumePeeks) return;

        var percent = (int)Math.Round(level * 100);
        ShowHardwarePeek(
            HardwarePeekType.Volume,
            muted ? "Silenciado" : "Volumen",
            muted ? "Sonido desactivado" : $"{percent}%",
            percent01: muted ? 0 : level,
            rightGlyph: muted ? "\uE74F" : VolumeGlyph(level));
    }

    private void OnBrightnessChanged(int percent)
    {
        if (!CanShowAutomaticPeek || !_settings.ShowBrightnessPeeks) return;

        ShowHardwarePeek(
            HardwarePeekType.Brightness,
            "Brillo",
            $"{percent}%",
            percent01: percent / 100.0);
    }

    /// <summary>
    /// Se llama desde el temporizador de teclado. Solo avisa si el idioma cambió
    /// en la misma ventana; cambiar de aplicación no es un cambio de idioma.
    /// </summary>
    private void CheckKeyboardLayout()
    {
        var (window, layout) = NativeMethods.GetForegroundKeyboardLayout();
        if (window == 0 || layout == 0) return;

        var changedInSameWindow = window == _lastKeyboardWindow && layout != _lastKeyboardLayout;
        _lastKeyboardWindow = window;
        _lastKeyboardLayout = layout;

        if (!changedInSameWindow || !_settings.ShowKeyboardLayoutPeeks || window == _hwnd || !CanShowAutomaticPeek) return;

        var (code, name) = KeyboardLayoutInfo.Describe(KeyboardLayoutInfo.LanguageIdFromLayout(layout.ToInt64()));
        ShowHardwarePeek(
            HardwarePeekType.KeyboardLayout,
            $"Teclado · {code}",
            name,
            percent01: 1.0);
    }

    private static string VolumeGlyph(float level) => level switch
    {
        <= 0.01f => "\uE992",
        < 0.34f => "\uE993",
        < 0.67f => "\uE994",
        _ => "\uE995",
    };
}
