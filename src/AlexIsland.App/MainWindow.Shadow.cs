using System.Numerics;
using AlexIsland.App.Native;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace AlexIsland.App;

public sealed partial class MainWindow : UserControl
{
    // ==============================================================
    // SECCIÓN SOMBRA DIFUMINADA DEL NOTCH
    // ==============================================================

    private SpriteVisual? _shadowVisual;

    /// <summary>
    /// Sombra suave alrededor del notch, como en Nusa. Usa la silueta real
    /// (máscara alfa del Path) para que siga las orejas y las esquinas, y se
    /// dibuja en el margen transparente de la ventana.
    /// </summary>
    private void InitializeNotchShadow()
    {
        try
        {
            var compositor = ElementCompositionPreview.GetElementVisual(NotchShadowHost).Compositor;

            var shadow = compositor.CreateDropShadow();
            shadow.BlurRadius = 14f;
            shadow.Opacity = 0.55f;
            shadow.Color = Microsoft.UI.Colors.Black;
            shadow.Offset = new Vector3(0, 3, 0);
            shadow.Mask = NotchBackground.GetAlphaMask();

            _shadowVisual = compositor.CreateSpriteVisual();
            _shadowVisual.Shadow = shadow;
            ElementCompositionPreview.SetElementChildVisual(NotchShadowHost, _shadowVisual);
        }
        catch (Exception ex)
        {
            // Sin sombra la isla funciona igual.
            _shadowVisual = null;
            App.Log("No se pudo crear la sombra del notch.", ex);
        }
    }

    private void UpdateNotchShadowSize(double width, double height)
    {
        if (_shadowVisual is null) return;
        _shadowVisual.Size = new Vector2((float)width, (float)height);
    }

    /// <summary>El cursor está sobre el notch, sin contar el margen de la sombra.</summary>
    private bool IsCursorOverNotch()
    {
        var margin = (int)Math.Round(ShadowMargin * PixelScale);
        return NativeMethods.IsCursorWithinWindow(_hwnd, margin, margin, margin);
    }
}
