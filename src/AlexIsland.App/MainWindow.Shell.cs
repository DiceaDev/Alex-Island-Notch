using AlexIsland.App.Native;
using AlexIsland.App.Services;
using AlexIsland.Core.Settings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AlexIsland.App;

public sealed partial class MainWindow : UserControl
{
    // ==============================================================
    // SECCIÓN BANDEJA, AJUSTES Y PANTALLA COMPLETA
    // ==============================================================

    private TrayIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private DispatcherQueueTimer? _fullscreenTimer;
    private bool _isHiddenByUser;
    private bool _isHiddenForFullscreen;

    private void InitializeShellIntegration()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AlexIsland.ico");
            _trayIcon = new TrayIcon(iconPath, "Alex Island")
            {
                ToggleVisibilityText = () => _isHiddenByUser ? "Mostrar isla" : "Ocultar isla",
            };
            _trayIcon.Activated += () => DispatcherQueue.TryEnqueue(OnTrayActivated);
            _trayIcon.CommandInvoked += command => DispatcherQueue.TryEnqueue(() => OnTrayCommand(command));
        }
        catch
        {
            // Sin bandeja la isla sigue funcionando; solo se pierde el menú.
            _trayIcon = null;
        }

        // Comprobación ligera (una llamada a GetForegroundWindow por segundo);
        // solo corre cuando el ajuste está activado.
        _fullscreenTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _fullscreenTimer.Interval = TimeSpan.FromSeconds(1);
        _fullscreenTimer.IsRepeating = true;
        _fullscreenTimer.Tick += (_, _) => CheckFullscreen();
    }

    private void StopShellIntegration()
    {
        _fullscreenTimer?.Stop();
        _trayIcon?.Dispose();
        _trayIcon = null;

        try { _settingsWindow?.Close(); } catch { }
        _settingsWindow = null;
    }

    private void ApplyShellSettings()
    {
        if (_fullscreenTimer is null) return;

        if (_settings.HideInFullscreen)
        {
            _fullscreenTimer.Start();
        }
        else
        {
            _fullscreenTimer.Stop();
            _isHiddenForFullscreen = false;
            UpdateIslandVisibility();
        }

        // Si la app se movió de carpeta, la entrada de inicio se corrige sola.
        if (_settings.StartWithWindows)
        {
            _ = Task.Run(() => StartupRegistration.Apply(true));
        }
    }

    private void OnTrayActivated()
    {
        if (_isHiddenByUser)
        {
            _isHiddenByUser = false;
            UpdateIslandVisibility();
            return;
        }

        // Clic en el ícono: abre y fija la isla, como un atajo.
        _isPinned = !_isPinned;
        ApplyIslandState();
    }

    private void OnTrayCommand(int command)
    {
        switch (command)
        {
            case TrayIcon.CommandSettings:
                OpenSettingsWindow();
                break;

            case TrayIcon.CommandToggleVisibility:
                _isHiddenByUser = !_isHiddenByUser;
                UpdateIslandVisibility();
                break;

            case TrayIcon.CommandExit:
                ExitApplication();
                break;
        }
    }

    private void OpenSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings);
        _settingsWindow.SettingsChanged += OnSettingsChanged;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Activate();
    }

    private void OnSettingsChanged(IslandSettings updated)
    {
        _settings = updated;
        ApplySettings();
        _ = SaveSettingsAsync();
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch
        {
            // Si no se puede guardar, los ajustes siguen aplicados en esta sesión.
        }
    }

    private void CheckFullscreen()
    {
        if (_isDisposed) return;

        var fullscreen = NativeMethods.IsFullscreenAppActive(_hwnd);
        if (fullscreen == _isHiddenForFullscreen) return;

        _isHiddenForFullscreen = fullscreen;
        UpdateIslandVisibility();
    }

    private void UpdateIslandVisibility()
    {
        var visible = !_isHiddenByUser && !_isHiddenForFullscreen;
        NativeMethods.SetIslandVisible(_hwnd, visible);

        if (visible)
        {
            ApplyIslandState();
        }
        else
        {
            // Oculta, la isla no necesita refrescar nada que el usuario no ve.
            _isPinned = false;
            _isPointerInside = false;
            ApplyIslandState();
        }

        _trayIcon?.SetTooltip(_isHiddenByUser ? "Alex Island (oculta)" : "Alex Island");
    }

    private void ExitApplication()
    {
        StopShellIntegration();
        _host.Close();
        Application.Current.Exit();
    }
}
