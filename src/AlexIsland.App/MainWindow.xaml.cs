using AlexIsland.App.Native;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace AlexIsland.App;

public sealed partial class MainWindow : Window
{
    private const int CompactWidth = 420;
    private const int CompactHeight = 82;
    private const int ExpandedWidth = 520;
    private const int ExpandedHeight = 176;

    private readonly AppWindow _appWindow;
    private readonly nint _hwnd;
    private bool _isExpanded;

    public MainWindow()
    {
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        ConfigureTitleBar();
        Activated += MainWindow_Activated;

        ResizeIsland(CompactWidth, CompactHeight);
        PositionAtTopCenter();
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        // Reaplica TopMost después de que WinUI activa la ventana.
        // SWP_NOACTIVATE evita quitar el foco a la aplicación que está usando la persona.
        NativeMethods.ConfigureIslandWindow(_hwnd);
    }

    private void ConfigureTitleBar()
    {
        var titleBar = _appWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
    }

    private void IslandButton_Click(object sender, RoutedEventArgs e)
    {
        _isExpanded = !_isExpanded;
        ExpandedPanel.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = _isExpanded
            ? "Panel expandido · Windows 11"
            : "Núcleo iniciado · Windows 11";

        ResizeIsland(
            _isExpanded ? ExpandedWidth : CompactWidth,
            _isExpanded ? ExpandedHeight : CompactHeight);

        PositionAtTopCenter();
    }

    private void ResizeIsland(int width, int height)
    {
        _appWindow.Resize(new SizeInt32(width, height));
    }

    private void PositionAtTopCenter()
    {
        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea is null)
        {
            return;
        }

        var workArea = displayArea.WorkArea;
        var width = _isExpanded ? ExpandedWidth : CompactWidth;
        var x = workArea.X + ((workArea.Width - width) / 2);
        var y = workArea.Y + 8;

        _appWindow.Move(new PointInt32(x, y));
    }
}
