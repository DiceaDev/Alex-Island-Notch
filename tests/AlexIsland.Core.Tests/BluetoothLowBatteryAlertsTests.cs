using AlexIsland.Core.SystemInfo;

namespace AlexIsland.Core.Tests;

public class BluetoothLowBatteryAlertsTests
{
    private static BluetoothBatteryInfo Earbuds(int percent) => new("Earbuds Clip 2 Pro", percent, BluetoothDeviceKind.Audio);
    private static BluetoothBatteryInfo Mouse(int percent) => new("LIFT", percent, BluetoothDeviceKind.Mouse);

    /// <summary>Simula una revisión: devuelve el aviso que se mostraría y lo marca como avisado.</summary>
    private static BluetoothBatteryInfo? Check(BluetoothLowBatteryAlerts alerts, params BluetoothBatteryInfo[] devices)
    {
        var pending = alerts.Pending(devices);
        if (pending.Count == 0) return null;
        alerts.MarkWarned(pending[0]);
        return pending[0];
    }

    [Fact]
    public void Warns_once_when_the_level_reaches_twenty_percent()
    {
        var alerts = new BluetoothLowBatteryAlerts();

        Assert.Null(Check(alerts, Earbuds(30)));
        Assert.Null(Check(alerts, Earbuds(21)));
        Assert.Equal(20, Check(alerts, Earbuds(20))?.Percent);
        Assert.Null(Check(alerts, Earbuds(18)));
        Assert.Null(Check(alerts, Earbuds(10)));
    }

    [Fact]
    public void Small_bounces_around_the_threshold_do_not_repeat_the_warning()
    {
        var alerts = new BluetoothLowBatteryAlerts();

        Assert.NotNull(Check(alerts, Earbuds(20)));
        Assert.Null(Check(alerts, Earbuds(22)));
        Assert.Null(Check(alerts, Earbuds(19)));
    }

    [Fact]
    public void Warns_again_after_charging()
    {
        var alerts = new BluetoothLowBatteryAlerts();

        Assert.NotNull(Check(alerts, Earbuds(15)));
        Assert.Null(Check(alerts, Earbuds(80)));
        Assert.NotNull(Check(alerts, Earbuds(20)));
    }

    [Fact]
    public void Warns_again_when_a_low_device_reconnects()
    {
        var alerts = new BluetoothLowBatteryAlerts();

        Assert.NotNull(Check(alerts, Earbuds(15)));
        Assert.Null(Check(alerts));
        Assert.NotNull(Check(alerts, Earbuds(15)));
    }

    [Fact]
    public void An_unshown_warning_stays_pending()
    {
        var alerts = new BluetoothLowBatteryAlerts();

        Assert.Single(alerts.Pending([Earbuds(12)])); // la isla estaba abierta: no se marca
        Assert.NotNull(Check(alerts, Earbuds(12)));
    }

    [Fact]
    public void Each_device_is_warned_separately_lowest_first()
    {
        var alerts = new BluetoothLowBatteryAlerts();

        Assert.Equal("LIFT", Check(alerts, Earbuds(18), Mouse(5))?.Name);
        Assert.Equal("Earbuds Clip 2 Pro", Check(alerts, Earbuds(18), Mouse(5))?.Name);
        Assert.Null(Check(alerts, Earbuds(18), Mouse(5)));
    }
}
