using AlexIsland.Core.Display;
using AlexIsland.Core.Settings;

namespace AlexIsland.Core.Tests;

public class MonitorLayoutTests
{
    private static readonly MonitorArea Left = new(1, 0, 0, 1366, 768, true);
    private static readonly MonitorArea Middle = new(2, 1366, 0, 1366, 768, false);
    private static readonly MonitorArea High = new(3, 2732, -592, 1280, 720, false);

    [Theory]
    [InlineData(1, 0, 1366, 200, 583)]
    [InlineData(2, 1366, 1366, 200, 1949)]
    [InlineData(3, 2732, 1280, 600, 3072)]
    public void Centers_the_window_inside_its_monitor(int id, int x, int width, int windowWidth, int expected)
        => Assert.Equal(expected, MonitorLayout.CenteredX(new MonitorArea(id, x, 0, width, 720, false), windowWidth));

    [Fact]
    public void Every_monitor_except_the_active_one_gets_a_satellite()
    {
        var satellites = MonitorLayout.SatelliteMonitors([Left, Middle, High], Middle.Id);

        Assert.Equal([Left, High], satellites);
    }

    [Fact]
    public void A_single_monitor_needs_no_satellites()
        => Assert.Empty(MonitorLayout.SatelliteMonitors([Left], Left.Id));

    [Fact]
    public void Island_follows_the_cursor_when_idle()
        => Assert.True(MonitorLayout.ShouldFollowCursor(true, false, false, false, Middle.Id, Left.Id));

    [Theory]
    [InlineData(false, false, false, false, 2, 1)] // ajuste desactivado
    [InlineData(true, true, false, false, 2, 1)]   // abierta
    [InlineData(true, false, true, false, 2, 1)]   // fijada
    [InlineData(true, false, false, true, 2, 1)]   // mostrando un aviso
    [InlineData(true, false, false, false, 1, 1)]  // ya está en ese monitor
    [InlineData(true, false, false, false, 0, 1)]  // monitor del cursor desconocido
    public void Island_stays_put_otherwise(bool enabled, bool expanded, bool pinned, bool peek, int cursor, int active)
        => Assert.False(MonitorLayout.ShouldFollowCursor(enabled, expanded, pinned, peek, cursor, active));

    [Fact]
    public void All_monitors_is_the_default()
        => Assert.True(new IslandSettings().ShowOnAllMonitors);
}
