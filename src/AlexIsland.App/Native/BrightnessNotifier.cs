using System.Runtime.InteropServices;

namespace AlexIsland.App.Native;

/// <summary>
/// Avisa cuando cambia el brillo de la pantalla integrada (portátiles), usando
/// las notificaciones de energía de Windows. No requiere WMI ni administrador.
/// En monitores externos Windows no informa el brillo, así que no hay avisos.
/// </summary>
internal sealed class BrightnessNotifier : IDisposable
{
    // GUID_VIDEO_CURRENT_MONITOR_BRIGHTNESS
    private static Guid BrightnessSetting = new("8FFEE2C6-2D01-46BE-ADB9-398ADDC5B4FF");

    private const uint DeviceNotifyCallback = 2;
    private const uint PbtPowerSettingChange = 0x8013;

    private readonly PowerCallback _callback; // se mantiene vivo mientras esté registrado
    private readonly GCHandle _paramsHandle;
    private nint _registration;
    private bool _receivedInitialValue;
    private bool _disposed;

    /// <summary>Brillo de 0 a 100. Llega en un hilo del sistema, no en el de la interfaz.</summary>
    public event Action<int>? BrightnessChanged;

    public BrightnessNotifier()
    {
        _callback = OnPowerSetting;
        var parameters = new DeviceNotifySubscribeParameters
        {
            Callback = Marshal.GetFunctionPointerForDelegate(_callback),
            Context = 0,
        };
        _paramsHandle = GCHandle.Alloc(parameters, GCHandleType.Pinned);

        var result = PowerSettingRegisterNotification(
            ref BrightnessSetting, DeviceNotifyCallback, _paramsHandle.AddrOfPinnedObject(), out _registration);
        if (result != 0)
        {
            _registration = 0;
        }
    }

    private uint OnPowerSetting(nint context, uint type, nint setting)
    {
        try
        {
            if (_disposed || type != PbtPowerSettingChange || setting == 0) return 0;

            // POWERBROADCAST_SETTING: GUID (16 bytes), DataLength (4 bytes), Data.
            var length = Marshal.ReadInt32(setting, 16);
            if (length < 4) return 0;
            var value = Marshal.ReadInt32(setting, 20);

            // Windows envía el valor actual justo al registrarse; ese no es un cambio.
            if (!_receivedInitialValue)
            {
                _receivedInitialValue = true;
                return 0;
            }

            BrightnessChanged?.Invoke(Math.Clamp(value, 0, 100));
        }
        catch
        {
        }

        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_registration != 0)
        {
            PowerSettingUnregisterNotification(_registration);
            _registration = 0;
        }

        if (_paramsHandle.IsAllocated) _paramsHandle.Free();
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint PowerCallback(nint context, uint type, nint setting);

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceNotifySubscribeParameters
    {
        public nint Callback;
        public nint Context;
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerSettingRegisterNotification(
        ref Guid settingGuid, uint flags, nint recipient, out nint registrationHandle);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSettingUnregisterNotification(nint registrationHandle);
}
