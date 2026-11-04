using AlexIsland.App.Services;
using AlexIsland.Core.Network;
using AlexIsland.Core.Settings;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace AlexIsland.App;

/// <summary>Ventana de ajustes. Cada cambio se guarda y se aplica al momento.</summary>
public sealed partial class SettingsWindow : Window
{
    private readonly IslandSettings _settings;
    private bool _loading = true;

    /// <summary>Se dispara con una copia de los ajustes cada vez que el usuario cambia algo.</summary>
    internal event Action<IslandSettings>? SettingsChanged;

    internal SettingsWindow(IslandSettings current)
    {
        InitializeComponent();
        _settings = current.Clone();

        try
        {
            var appWindow = AppWindow;
            appWindow.Resize(new SizeInt32(520, 760));
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AlexIsland.ico");
            if (File.Exists(iconPath)) appWindow.SetIcon(iconPath);
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }
        }
        catch
        {
            // La ventana funciona igual aunque no se pueda ajustar su tamaño o ícono.
        }

        StartWithWindowsToggle.IsOn = _settings.StartWithWindows;
        HideInFullscreenToggle.IsOn = _settings.HideInFullscreen;
        ShowMascotToggle.IsOn = _settings.ShowMascot;
        MinimalNotchToggle.IsOn = _settings.MinimalNotch;
        NotchScaleSlider.Value = Math.Round(_settings.NotchScale * 100);
        NotchScaleLabel.Text = $"{NotchScaleSlider.Value:0}%";

        NetworkPeeksToggle.IsOn = _settings.ShowNetworkPeeks;
        VolumePeeksToggle.IsOn = _settings.ShowVolumePeeks;
        BrightnessPeeksToggle.IsOn = _settings.ShowBrightnessPeeks;
        KeyboardLayoutPeeksToggle.IsOn = _settings.ShowKeyboardLayoutPeeks;
        CapsLockPeeksToggle.IsOn = _settings.ShowCapsLockPeeks;
        ShowLyricsToggle.IsOn = _settings.ShowLyrics;
        LatencyHostBox.Text = _settings.LatencyHost;

        DataFolderText.Text = "Tus datos se guardan solo en este equipo: " +
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlexIsland");

        _loading = false;
    }

    /// <summary>Refleja cambios hechos desde otro lugar (por ejemplo, el menú de la bandeja).</summary>
    internal void Refresh(IslandSettings current)
    {
        _loading = true;
        _settings.StartWithWindows = current.StartWithWindows;
        _settings.ShowMascot = current.ShowMascot;
        _settings.MinimalNotch = current.MinimalNotch;
        StartWithWindowsToggle.IsOn = current.StartWithWindows;
        ShowMascotToggle.IsOn = current.ShowMascot;
        MinimalNotchToggle.IsOn = current.MinimalNotch;
        _loading = false;
    }

    private void OnToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        _settings.HideInFullscreen = HideInFullscreenToggle.IsOn;
        _settings.ShowMascot = ShowMascotToggle.IsOn;
        _settings.MinimalNotch = MinimalNotchToggle.IsOn;
        _settings.ShowNetworkPeeks = NetworkPeeksToggle.IsOn;
        _settings.ShowVolumePeeks = VolumePeeksToggle.IsOn;
        _settings.ShowBrightnessPeeks = BrightnessPeeksToggle.IsOn;
        _settings.ShowKeyboardLayoutPeeks = KeyboardLayoutPeeksToggle.IsOn;
        _settings.ShowCapsLockPeeks = CapsLockPeeksToggle.IsOn;
        _settings.ShowLyrics = ShowLyricsToggle.IsOn;

        if (sender == StartWithWindowsToggle)
        {
            var ok = StartupRegistration.Apply(StartWithWindowsToggle.IsOn);
            StartupError.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
            if (!ok)
            {
                _loading = true;
                StartWithWindowsToggle.IsOn = _settings.StartWithWindows;
                _loading = false;
                return;
            }
            _settings.StartWithWindows = StartWithWindowsToggle.IsOn;
        }

        Publish();
    }

    private void NotchScaleSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (NotchScaleLabel is null) return;
        NotchScaleLabel.Text = $"{e.NewValue:0}%";
        if (_loading) return;

        _settings.NotchScale = e.NewValue / 100.0;
        Publish();
    }

    private void LatencyHostBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) CommitLatencyHost();
    }

    private void LatencyHostBox_LostFocus(object sender, RoutedEventArgs e) => CommitLatencyHost();

    private void CommitLatencyHost()
    {
        var host = LatencyHostBox.Text.Trim();
        if (host.Length == 0) host = IslandSettings.DefaultLatencyHost;

        if (!LatencyHost.IsValid(host))
        {
            LatencyHostError.Visibility = Visibility.Visible;
            return;
        }

        LatencyHostError.Visibility = Visibility.Collapsed;
        LatencyHostBox.Text = host;
        if (host == _settings.LatencyHost) return;

        _settings.LatencyHost = host;
        Publish();
    }

    private void Publish() => SettingsChanged?.Invoke(_settings.Clone().Normalize());
}
