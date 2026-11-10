using AlexIsland.App.Services;
using AlexIsland.Core.SystemInfo;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AlexIsland.App;

public sealed partial class MainWindow : UserControl
{
    // ==============================================================
    // BATERÍA DE ACCESORIOS BLUETOOTH (pestaña Sistema)
    // ==============================================================

    private const int BluetoothRowHeight = 36;
    private const int MaxBluetoothChips = 4;

    private IReadOnlyList<BluetoothBatteryInfo> _bluetoothDevices = [];
    private bool _bluetoothReading;

    /// <summary>La pestaña Sistema crece una fila solo cuando hay accesorios que mostrar.</summary>
    private int SystemExpandedHeight =>
        ExpandedSystemHeight + (_bluetoothDevices.Count > 0 ? BluetoothRowHeight : 0);

    /// <summary>Lee la batería fuera del hilo de la interfaz y actualiza la fila.</summary>
    private void RefreshBluetoothBatteries()
    {
        if (_bluetoothReading || _isDisposed) return;
        _bluetoothReading = true;

        _ = Task.Run(BluetoothBatteryReader.Read).ContinueWith(t =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _bluetoothReading = false;
                if (_isDisposed || !t.IsCompletedSuccessfully) return;

                _bluetoothDevices = t.Result;
                RenderBluetoothBatteries();
            });
        }, TaskScheduler.Default);
    }

    private void RenderBluetoothBatteries()
    {
        SysBluetoothRow.Children.Clear();
        SysBluetoothRow.ColumnDefinitions.Clear();

        var devices = _bluetoothDevices.Take(MaxBluetoothChips).ToList();
        for (var i = 0; i < devices.Count; i++)
        {
            SysBluetoothRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var chip = CreateBluetoothChip(devices[i]);
            Grid.SetColumn(chip, i);
            SysBluetoothRow.Children.Add(chip);
        }

        SysBluetoothRow.Visibility = devices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_isExpanded && _activeTab == IslandTab.System && (int)IslandContainer.Height != SystemExpandedHeight)
        {
            ApplyIslandState();
        }
    }

    private static Border CreateBluetoothChip(BluetoothBatteryInfo device)
    {
        var color = BatteryColor(device.Percent);

        var header = new Grid { ColumnSpacing = 5 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = device.Kind switch
            {
                BluetoothDeviceKind.Audio => "",
                BluetoothDeviceKind.Mouse => "",
                BluetoothDeviceKind.Keyboard => "",
                _ => "",
            },
            FontSize = 10,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 184, 203, 211)),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var name = new TextBlock
        {
            Text = device.Name,
            FontSize = 9.5,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 251, 243, 242)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(name, 1);

        var percent = new TextBlock
        {
            Text = $"{device.Percent}%",
            FontSize = 9.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(percent, 2);

        header.Children.Add(icon);
        header.Children.Add(name);
        header.Children.Add(percent);

        // Barra proporcional: la columna de relleno toma el porcentaje y el resto queda vacío.
        var track = new Grid
        {
            Height = 3,
            CornerRadius = new CornerRadius(1.5),
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 17, 64, 89)),
        };
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(device.Percent, 0.01), GridUnitType.Star) });
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - device.Percent, 0.01), GridUnitType.Star) });
        track.Children.Add(new Border { Background = new SolidColorBrush(color), CornerRadius = new CornerRadius(1.5) });

        var chip = new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 10, 50, 73)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 5, 7, 5),
            Child = new StackPanel { Spacing = 3, Children = { header, track } },
        };
        ToolTipService.SetToolTip(chip, $"{device.Name}: batería {device.Percent}%");
        return chip;
    }

    private static Color BatteryColor(int percent) => percent switch
    {
        <= 20 => ColorHelper.FromArgb(255, 255, 127, 120),
        <= 40 => ColorHelper.FromArgb(255, 255, 194, 92),
        _ => ColorHelper.FromArgb(255, 79, 209, 165),
    };
}
