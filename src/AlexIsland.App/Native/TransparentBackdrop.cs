using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace AlexIsland.App.Native;

/// <summary>
/// Fondo de sistema totalmente transparente. Con él, la ventana se compone
/// con transparencia por píxel y los bordes suavizados (antialiasing) del
/// notch se mezclan con el escritorio, en vez de cortarse en escalera.
/// </summary>
internal sealed class TransparentBackdrop : SystemBackdrop
{
    private static Windows.UI.Composition.Compositor? s_compositor;
    private static nint s_dispatcherQueueController;

    /// <summary>Se dispara si la transparencia no se pudo activar en este equipo.</summary>
    public event Action? Failed;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        try
        {
            connectedTarget.SystemBackdrop = GetCompositor().CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            App.Log("Transparencia: fondo de sistema transparente conectado.");
        }
        catch (Exception ex)
        {
            App.Log("Transparencia: no se pudo conectar el fondo transparente.", ex);
            Failed?.Invoke();
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        disconnectedTarget.SystemBackdrop = null;
    }

    /// <summary>
    /// El pincel del fondo debe venir del compositor de Windows.UI, que exige una
    /// cola de Windows.System en el hilo. Las apps WinUI 3 no la crean por sí solas.
    /// </summary>
    private static Windows.UI.Composition.Compositor GetCompositor()
    {
        if (s_compositor is not null) return s_compositor;

        if (Windows.System.DispatcherQueue.GetForCurrentThread() is null)
        {
            var options = new DispatcherQueueOptions
            {
                Size = Marshal.SizeOf<DispatcherQueueOptions>(),
                ThreadType = 2,     // DQTYPE_THREAD_CURRENT
                ApartmentType = 2,  // DQTAT_COM_STA
            };
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out s_dispatcherQueueController));
        }

        s_compositor = new Windows.UI.Composition.Compositor();
        return s_compositor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;
        public int ApartmentType;
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);
}
