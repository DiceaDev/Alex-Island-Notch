using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace AlexIsland.App;

public sealed partial class MainWindow : UserControl
{
    // ==============================================================
    // BARRAS DE SONIDO DEL NOTCH CERRADO
    // ==============================================================
    //
    // Antes cambiaban de alto cada 130 ms con un temporizador: cada cambio
    // obligaba a recalcular la disposición de la isla en el hilo de interfaz,
    // aunque las barras ni siquiera se vieran. Ahora es una animación de escala
    // (RenderTransform), que Windows ejecuta en el hilo de composición, y solo
    // corre mientras el notch cerrado está a la vista con música sonando.

    private Storyboard? _waveformStoryboard;

    private void UpdateWaveformAnimation()
    {
        var shouldRun = !_isDisposed && _isPlaying && _currentDisplayMode == NotchDisplayMode.Closed;
        if (shouldRun) StartWaveform();
        else StopWaveform();
    }

    private void StartWaveform()
    {
        if (_waveformStoryboard is not null) return;

        // Cada barra con su propio ritmo y alturas, para que no se muevan al unísono.
        _waveformStoryboard = new Storyboard();
        AddBar(WaveBar1Scale, 0.85, [0.30, 0.55, 0.25, 0.50]);
        AddBar(WaveBar2Scale, 0.70, [0.55, 0.90, 0.50, 0.80]);
        AddBar(WaveBar3Scale, 0.95, [0.65, 1.00, 0.60, 0.95]);
        AddBar(WaveBar4Scale, 0.78, [0.35, 0.70, 0.30, 0.60]);
        _waveformStoryboard.Begin();

        // Saltos discretos (como un ecualizador) en lugar de una curva continua:
        // así el compositor solo redibuja unas pocas veces por segundo, no a 60 fps.
        void AddBar(ScaleTransform target, double seconds, double[] values)
        {
            var animation = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever,
                Duration = TimeSpan.FromSeconds(seconds),
            };
            for (var i = 0; i < values.Length; i++)
            {
                animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
                {
                    KeyTime = TimeSpan.FromSeconds(seconds * i / values.Length),
                    Value = values[i],
                });
            }
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, "ScaleY");
            _waveformStoryboard.Children.Add(animation);
        }
    }

    private void StopWaveform()
    {
        // Al detenerla, cada barra vuelve a su altura de reposo (la de la XAML).
        _waveformStoryboard?.Stop();
        _waveformStoryboard = null;
    }
}
