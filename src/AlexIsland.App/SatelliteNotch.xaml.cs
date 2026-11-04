using System.Numerics;
using AlexIsland.App.Native;
using AlexIsland.Core.Display;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace AlexIsland.App;

public enum SatelliteContent
{
    /// <summary>Solo la silueta (notch mínimo o sin mascota).</summary>
    Blank,
    Eyes,
    Text,
}

/// <summary>Lo que muestra un notch satélite; si no cambia, no se redibuja nada.</summary>
public sealed record SatelliteState(
    SatelliteContent Content,
    int Width,
    int Height,
    double BottomRadius,
    double NotchScale,
    string Line1 = "",
    string Line2 = "");

/// <summary>
/// Notch liviano para los monitores donde no está la isla completa. No tiene
/// temporizadores ni estado propio: la isla le envía qué mostrar, y como la isla
/// se mueve al monitor del cursor, nadie llega a interactuar con él.
/// </summary>
public sealed partial class SatelliteNotch : UserControl
{
    private readonly IslandHostWindow _host;
    private readonly double _shadowMargin;
    private MonitorArea _monitor;
    private SatelliteState? _state;
    private SpriteVisual? _shadowVisual;

    internal nint Hwnd => _host.Hwnd;

    internal SatelliteNotch(MonitorArea monitor, double shadowMargin)
    {
        InitializeComponent();
        _monitor = monitor;
        _shadowMargin = shadowMargin;
        NotchContainer.Margin = new Thickness(shadowMargin, 0, shadowMargin, shadowMargin);

        _host = new IslandHostWindow(this, 1, 1);
        NativeMethods.ConfigureIslandWindow(_host.Hwnd);
        _host.Move(monitor.X, monitor.Y);
        CreateShadow();

        // Al cambiar la escala del monitor se recalcula el tamaño real de la ventana.
        Loaded += (_, _) =>
        {
            if (XamlRoot is { } root) root.Changed += (_, _) => Layout();
        };
    }

    internal void Apply(SatelliteState state)
    {
        if (state == _state) return;
        var shapeChanged = _state is null
            || _state.Width != state.Width
            || _state.Height != state.Height
            || _state.BottomRadius != state.BottomRadius;
        _state = state;

        EyesContent.Visibility = state.Content == SatelliteContent.Eyes ? Visibility.Visible : Visibility.Collapsed;
        TextContent.Visibility = state.Content == SatelliteContent.Text ? Visibility.Visible : Visibility.Collapsed;
        Line1Text.Text = state.Line1;
        Line2Text.Text = state.Line2;
        Line2Text.Visibility = state.Line2.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (shapeChanged)
        {
            NotchContainer.Width = state.Width;
            NotchContainer.Height = state.Height;
            NotchShape.Data = NotchGeometry.Create(state.Width, state.Height, state.BottomRadius);
            if (_shadowVisual is not null) _shadowVisual.Size = new Vector2(state.Width, state.Height);
        }

        Layout();
    }

    internal void MoveTo(MonitorArea monitor)
    {
        _monitor = monitor;
        // Primero se lleva al monitor nuevo: ahí se conoce su escala real.
        _host.Move(monitor.X, monitor.Y);
        Layout();
    }

    internal void SetVisible(bool visible)
    {
        if (visible && !_shown)
        {
            _shown = true;
            _host.Show();
            return;
        }
        NativeMethods.SetIslandVisible(_host.Hwnd, visible);
    }

    private bool _shown;

    internal void Close() => _host.Close();

    private void Layout()
    {
        if (_state is null) return;

        UserScale.ScaleX = _state.NotchScale;
        UserScale.ScaleY = _state.NotchScale;

        var scale = NativeMethods.GetDpiScale(_host.Hwnd) * _state.NotchScale;
        var width = (int)Math.Ceiling((_state.Width + 2 * _shadowMargin) * scale);
        var height = (int)Math.Ceiling((_state.Height + _shadowMargin) * scale);
        _host.Resize(width, height);
        _host.Move(MonitorLayout.CenteredX(_monitor, width), _monitor.Y);
    }

    private void CreateShadow()
    {
        try
        {
            var compositor = ElementCompositionPreview.GetElementVisual(ShadowHost).Compositor;
            var shadow = compositor.CreateDropShadow();
            shadow.BlurRadius = 14f;
            shadow.Opacity = 0.55f;
            shadow.Color = Microsoft.UI.Colors.Black;
            shadow.Offset = new Vector3(0, 3, 0);
            shadow.Mask = NotchShape.GetAlphaMask();

            _shadowVisual = compositor.CreateSpriteVisual();
            _shadowVisual.Shadow = shadow;
            ElementCompositionPreview.SetElementChildVisual(ShadowHost, _shadowVisual);
        }
        catch (Exception ex)
        {
            // Sin sombra el notch se ve igual de bien.
            _shadowVisual = null;
            App.Log("No se pudo crear la sombra del notch satélite.", ex);
        }
    }
}
