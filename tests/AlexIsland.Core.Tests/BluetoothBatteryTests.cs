using AlexIsland.Core.SystemInfo;

namespace AlexIsland.Core.Tests;

public class BluetoothBatteryTests
{
    private static readonly Guid Earbuds = Guid.NewGuid();
    private static readonly Guid Mouse = Guid.NewGuid();
    private static readonly Guid OldHeadset = Guid.NewGuid();

    [Fact]
    public void Headset_takes_the_battery_from_its_hands_free_profile()
    {
        var devices = BluetoothBattery.Combine(
        [
            new(@"BTHENUM\DEV_4C3C8F47E6B7\8&1", Earbuds, "Earbuds Clip 2 Pro", true, null),
            new(@"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_LOCALMFG\8&1_C00000000", Earbuds, "Earbuds Clip 2 Pro Hands-Free AG", false, 40),
        ]);

        var device = Assert.Single(devices);
        Assert.Equal("Earbuds Clip 2 Pro", device.Name);
        Assert.Equal(40, device.Percent);
        Assert.Equal(BluetoothDeviceKind.Audio, device.Kind);
    }

    [Fact]
    public void Low_energy_mouse_reports_its_own_battery()
    {
        var devices = BluetoothBattery.Combine(
        [
            new(@"BTHLE\DEV_D09E6C64D81B\8&1", Mouse, "LIFT", true, 100),
            new(@"BTHLEDEVICE\{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&02046D\9&1", Mouse, "Dispositivo HID", null, null),
        ]);

        var device = Assert.Single(devices);
        Assert.Equal(("LIFT", 100, BluetoothDeviceKind.Mouse), (device.Name, device.Percent, device.Kind));
    }

    [Fact]
    public void Disconnected_devices_are_hidden_even_with_a_cached_level()
    {
        var devices = BluetoothBattery.Combine(
        [
            new(@"BTHENUM\DEV_1406A7809767\8&1", OldHeadset, "WH-CH520", false, null),
            new(@"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_VID\8&1_C00000000", OldHeadset, "WH-CH520 Hands-Free AG", false, 100),
        ]);

        Assert.Empty(devices);
    }

    [Fact]
    public void Connected_devices_without_battery_are_not_listed()
        => Assert.Empty(BluetoothBattery.Combine([new(@"BTHENUM\DEV_B8EA98B809D7\8&1", Guid.NewGuid(), "Diego", true, null)]));

    [Fact]
    public void Several_devices_are_sorted_by_name()
    {
        var devices = BluetoothBattery.Combine(
        [
            new(@"BTHLE\DEV_D09E6C64D81B\8&1", Mouse, "LIFT", true, 100),
            new(@"BTHENUM\DEV_4C3C8F47E6B7\8&1", Earbuds, "Earbuds Clip 2 Pro", true, 40),
        ]);

        Assert.Equal(["Earbuds Clip 2 Pro", "LIFT"], devices.Select(d => d.Name));
    }

    [Fact]
    public void Keyboards_are_told_apart_from_mice_by_name()
    {
        var keyboard = Guid.NewGuid();
        var device = Assert.Single(BluetoothBattery.Combine(
        [
            new(@"BTHLE\DEV_AA\8&1", keyboard, "MX Keys", true, 75),
            new(@"BTHLEDEVICE\{00001812-0000-1000-8000-00805F9B34FB}_DEV\9&1", keyboard, null, null, null),
        ]));

        Assert.Equal(BluetoothDeviceKind.Keyboard, device.Kind);
    }
}
