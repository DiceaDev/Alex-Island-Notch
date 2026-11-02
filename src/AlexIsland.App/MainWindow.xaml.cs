using AlexIsland.App.Native;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using WinRT.Interop;

namespace AlexIsland.App;

public sealed partial class MainWindow : Window
{
    private const int NotchWidth = 160;
    private const int NotchHeight = 38;
    private const int HoverWidth = 420;
    private const int HoverHeight = 82;
    private const int PinnedWidth = 520;
    private const int PinnedHeight = 176;

    private readonly AppWindow _appWindow;
    private readonly nint _hwnd;
    private bool _isPointerInside;
    private bool _isPinned;
    private bool _isExpanded;
    private Storyboard? _activeAnimation;

    public MainWindow()
    {
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        ConfigureTitleBar();
        Activated += MainWindow_Activated;

        ApplyIslandState();
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        // Mantiene la isla por encima de ventanas normales sin robar el foco.
        NativeMethods.ConfigureIslandWindow(_hwnd);
    }

    private void ConfigureTitleBar()
    {
        _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
    }

    private void IslandSurface_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isPointerInside = true;
        ApplyIslandState();
    }

    private void IslandSurface_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerInside = false;
        ApplyIslandState();
    }

    private void IslandSurface_Tapped(object sender, TappedRoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        ApplyIslandState();
    }

    private void ApplyIslandState()
    {
        var wasExpanded = _isExpanded;
        _isExpanded = _isPinned || _isPointerInside;

        NotchContent.Visibility = _isExpanded ? Visibility.Collapsed : Visibility.Visible;
        ExpandedContent.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;
        ExpandedPanel.Visibility = _isPinned ? Visibility.Visible : Visibility.Collapsed;

        if (_isPinned)
        {
            StatusText.Text = "Panel fijado · Windows 11";
            IslandSurface.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(0, 0, 28, 28);
            IslandSurface.Padding = new Thickness(0);
            ResizeIsland(PinnedWidth, PinnedHeight);
        }
        else if (_isPointerInside)
        {
            StatusText.Text = "Núcleo iniciado · Windows 11";
            IslandSurface.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(0, 0, 26, 26);
            IslandSurface.Padding = new Thickness(0);
            ResizeIsland(HoverWidth, HoverHeight);
        }
        else
        {
            StatusText.Text = "Núcleo iniciado · Windows 11";
            IslandSurface.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(0, 0, 19, 19);
            IslandSurface.Padding = new Thickness(0);
            ResizeIsland(NotchWidth, NotchHeight);
        }

        PositionAtTopCenter();

        if (wasExpanded != _isExpanded)
        {
            AnimateIsland(_isExpanded);
        }
    }

    private void AnimateIsland(bool expanding)
    {
        _activeAnimation?.Stop();

        var storyboard = new Storyboard();
        var scaleX = new DoubleAnimation
        {
            From = expanding ? 0.90 : 1.0,
            To = expanding ? 1.0 : 0.90,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var scaleY = new DoubleAnimation
        {
            From = expanding ? 0.86 : 1.0,
            To = expanding ? 1.0 : 0.86,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(scaleX, IslandScale);
        Storyboard.SetTargetProperty(scaleX, "ScaleX");
        Storyboard.SetTarget(scaleY, IslandScale);
        Storyboard.SetTargetProperty(scaleY, "ScaleY");
        storyboard.Children.Add(scaleX);
        storyboard.Children.Add(scaleY);
        _activeAnimation = storyboard;
        storyboard.Begin();
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
        var width = _isPinned ? PinnedWidth : _isPointerInside ? HoverWidth : NotchWidth;
        var x = workArea.X + ((workArea.Width - width) / 2);
        var y = workArea.Y;

        _appWindow.Move(new PointInt32(x, y));
    }
}
