using AlexIsland.App.Native;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;
namespace AlexIsland.App;
public sealed partial class MainWindow : Window
{
    private const int IslandWidth = 420;
    private const int IslandHeight = 82;
    private readonly AppWindow _appWindow;
    public MainWindow()
    {
        InitializeComponent();
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Resize(new SizeInt32(IslandWidth, IslandHeight));
        PositionAtTopCenter();
        NativeMethods.SetWindowAsTopmostToolWindow(hwnd);
    }
    private void PositionAtTopCenter()
    {
        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea is null) return;
        var workArea = displayArea.WorkArea;
        var x = workArea.X + ((workArea.Width - IslandWidth) / 2);
        var y = workArea.Y + 8;
        _appWindow.Move(new PointInt32(x, y));
    }
}
